using System.IO;

namespace OurPlanCore;

public static partial class OurPlanPackageWriter
{
    private const int SharingViolationHResult = unchecked((int)0x80070020);
    private const int LockViolationHResult = unchecked((int)0x80070021);
    private const int UnableToRemoveReplacedHResult = unchecked((int)0x80070497);
    private const int UnableToMoveReplacementHResult = unchecked((int)0x80070498);

    private static readonly TimeSpan[] ReplaceRetryDelays =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromMilliseconds(1000),
    ];

    private static DestinationState CaptureExpectedDestination(string targetPath, PublishContext context)
    {
        bool exists = File.Exists(targetPath);
        if (exists != context.AllowExistingDestination)
        {
            throw new OurPlanPackageConflictException(exists
                ? "A file appeared at the save destination. It was not overwritten."
                : "The project file disappeared after it was opened.");
        }
        if (!exists)
            return new DestinationState(false, null, null, null);

        OurPlanPackageFingerprint fingerprint = OurPlanPackageFingerprint.Read(targetPath);
        if (context.ExpectedFingerprint is { } expectedFingerprint && fingerprint != expectedFingerprint)
            throw new OurPlanPackageConflictException("The project file changed elsewhere after it was opened.");
        OurPlanPackageManifest? destinationManifest = null;
        if (!string.IsNullOrWhiteSpace(context.ExpectedRevisionId) || context.BaseManifest != null)
        {
            destinationManifest = OurPlanPackageArchive.ReadManifest(targetPath, verifyObjects: false);
            if (!string.IsNullOrWhiteSpace(context.ExpectedRevisionId) &&
                !destinationManifest.RevisionId.Equals(context.ExpectedRevisionId, StringComparison.OrdinalIgnoreCase))
                throw new OurPlanPackageConflictException("The project file revision changed elsewhere.");
            if (context.BaseManifest != null &&
                !ManifestsExactlyMatch(destinationManifest, context.BaseManifest))
            {
                throw new OurPlanPackageConflictException(
                    "The project file contents changed without a new revision identifier.");
            }
        }
        return new DestinationState(
            true,
            fingerprint,
            context.ExpectedRevisionId,
            destinationManifest,
            context.ExpectedContentSha256);
    }

    private static DestinationState CaptureSaveAsDestination(string targetPath, bool overwriteExisting)
    {
        if (!File.Exists(targetPath))
            return new DestinationState(false, null, null, null);
        if (!overwriteExisting)
            throw new IOException($"The destination already exists: {targetPath}");

        OurPlanPackageFingerprint fingerprint = OurPlanPackageFingerprint.Read(targetPath);
        string? revision = OurPlanPackageArchive.TryReadManifest(targetPath, out OurPlanPackageManifest? manifest)
            ? manifest!.RevisionId
            : null;
        string contentSha256 = HashFile(targetPath);
        if (OurPlanPackageFingerprint.Read(targetPath) != fingerprint)
        {
            throw new OurPlanPackageConflictException(
                "The existing destination changed while its overwrite was being prepared.");
        }
        return new DestinationState(true, fingerprint, revision, manifest, contentSha256);
    }

    private static void EnsureDestinationUnchanged(string targetPath, DestinationState original)
    {
        bool exists = File.Exists(targetPath);
        if (exists != original.Exists)
            throw new OurPlanPackageConflictException("The save destination changed while the project was being packed.");
        if (!exists)
            return;
        if (OurPlanPackageFingerprint.Read(targetPath) != original.Fingerprint)
            throw new OurPlanPackageConflictException("The project file changed elsewhere while this save was running.");
        if (!string.IsNullOrWhiteSpace(original.RevisionId))
        {
            OurPlanPackageManifest manifest = OurPlanPackageArchive.ReadManifest(targetPath, verifyObjects: false);
            if (!manifest.RevisionId.Equals(original.RevisionId, StringComparison.OrdinalIgnoreCase))
                throw new OurPlanPackageConflictException("The project revision changed elsewhere while this save was running.");
            if (original.Manifest != null && !ManifestsExactlyMatch(manifest, original.Manifest))
            {
                throw new OurPlanPackageConflictException(
                    "The project contents changed elsewhere without a new revision identifier.");
            }
        }
    }

    private static void ReplaceAtomically(
        string tempPath,
        string targetPath,
        DestinationState original,
        string stagingDirectory)
    {
        if (!original.Exists)
        {
            File.Move(tempPath, targetPath);
            return;
        }

        string rollbackPath = Path.Combine(
            stagingDirectory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.rollback.tmp");
        int retryCount = ExecuteTransientReplaceWithRetry(
            () => File.Replace(tempPath, targetPath, rollbackPath, ignoreMetadataErrors: true),
            () => EnsureReplaceRetryState(
                tempPath,
                targetPath,
                rollbackPath,
                original));
        if (retryCount > 0)
        {
            AppLog.Info(
                $"OurPlan package replace succeeded after {retryCount} transient retry attempt(s): " +
                $"'{targetPath}'.");
        }
        if (RollbackMatchesExpectedDestination(rollbackPath, original))
        {
            TryDeleteTemp(rollbackPath);
            return;
        }

        string preservedPath = PreserveConflictFile(rollbackPath, targetPath);
        throw new OurPlanPackageConflictException(
            "The project changed during the final replace. The displaced external version was preserved at " +
            $"'{preservedPath}' and was not overwritten again.");
    }

    internal static int ExecuteTransientReplaceWithRetry(
        Action replace,
        Action validateRetryState,
        Action<TimeSpan>? wait = null)
    {
        ArgumentNullException.ThrowIfNull(replace);
        ArgumentNullException.ThrowIfNull(validateRetryState);
        wait ??= static delay => Thread.Sleep(delay);

        for (int retryCount = 0; ; retryCount++)
        {
            try
            {
                replace();
                return retryCount;
            }
            catch (IOException ex) when (
                IsTransientReplaceFailure(ex) &&
                retryCount < ReplaceRetryDelays.Length)
            {
                wait(ReplaceRetryDelays[retryCount]);
                validateRetryState();
            }
        }
    }

    internal static bool IsTransientReplaceFailure(IOException exception) =>
        exception.HResult is
            SharingViolationHResult or
            LockViolationHResult or
            UnableToRemoveReplacedHResult or
            UnableToMoveReplacementHResult;

    private static void EnsureReplaceRetryState(
        string tempPath,
        string targetPath,
        string rollbackPath,
        DestinationState original)
    {
        if (!File.Exists(tempPath) || File.Exists(rollbackPath))
        {
            throw new OurPlanPackageConflictException(
                "The package replacement state changed during a retry. " +
                "The local working copy remains preserved.");
        }
        EnsureDestinationUnchanged(targetPath, original);
    }

    private static bool RollbackMatchesExpectedDestination(
        string rollbackPath,
        DestinationState expected)
    {
        try
        {
            if (!File.Exists(rollbackPath))
                return false;
            if (!string.IsNullOrWhiteSpace(expected.ContentSha256))
            {
                return HashFile(rollbackPath).Equals(
                    expected.ContentSha256,
                    StringComparison.OrdinalIgnoreCase);
            }
            if (expected.Manifest == null)
                return false;
            OurPlanPackageManifest manifest = OurPlanPackageArchive.ReadManifest(
                rollbackPath,
                verifyObjects: true);
            return ManifestsExactlyMatch(manifest, expected.Manifest);
        }
        catch
        {
            return false;
        }
    }

    private static OurPlanPackageFingerprint ValidatePublishedTarget(
        string targetPath,
        OurPlanPackageManifest expected)
    {
        OurPlanPackageFingerprint before = OurPlanPackageFingerprint.Read(targetPath);
        OurPlanPackageManifest actual = OurPlanPackageArchive.ReadManifest(targetPath, verifyObjects: false);
        OurPlanPackageFingerprint after = OurPlanPackageFingerprint.Read(targetPath);
        if (before != after || !ManifestsExactlyMatch(actual, expected))
        {
            throw new OurPlanPackageConflictException(
                "The project file changed immediately after it was published. The local working copy remains recoverable.");
        }
        return after;
    }

    private static bool ManifestsExactlyMatch(
        OurPlanPackageManifest left,
        OurPlanPackageManifest right)
    {
        if (!left.Format.Equals(right.Format, StringComparison.Ordinal) ||
            left.SchemaVersion != right.SchemaVersion ||
            !left.ProjectId.Equals(right.ProjectId, StringComparison.OrdinalIgnoreCase) ||
            !left.RevisionId.Equals(right.RevisionId, StringComparison.OrdinalIgnoreCase) ||
            !left.ParentRevisionId.Equals(right.ParentRevisionId, StringComparison.OrdinalIgnoreCase) ||
            !left.DisplayName.Equals(right.DisplayName, StringComparison.Ordinal) ||
            !left.CreatedUtc.Equals(right.CreatedUtc, StringComparison.Ordinal) ||
            !left.SavedUtc.Equals(right.SavedUtc, StringComparison.Ordinal) ||
            left.Files.Count != right.Files.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Files.Count; index++)
        {
            OurPlanPackageFileManifest first = left.Files[index];
            OurPlanPackageFileManifest second = right.Files[index];
            if (!first.Path.Equals(second.Path, StringComparison.Ordinal) ||
                !first.ObjectSha256.Equals(second.ObjectSha256, StringComparison.OrdinalIgnoreCase) ||
                first.Length != second.Length ||
                first.LastWriteUtcTicks != second.LastWriteUtcTicks)
            {
                return false;
            }
        }
        return true;
    }

    private static string PreserveConflictFile(string rollbackPath, string targetPath)
    {
        string directory = Path.GetDirectoryName(targetPath) ?? ".";
        string stem = Path.GetFileNameWithoutExtension(targetPath);
        string candidate = Path.Combine(
            directory,
            $"{stem}.external-conflict-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.ourplan");
        try
        {
            File.Move(rollbackPath, candidate);
            return candidate;
        }
        catch
        {
            return rollbackPath;
        }
    }
}
