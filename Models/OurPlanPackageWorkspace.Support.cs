using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace OurPlanCore;

public static partial class OurPlanPackageWorkspace
{
    private static List<WorkspaceCandidate> ReadProjectCandidates(string projectRoot)
    {
        var result = new List<WorkspaceCandidate>();
        if (!Directory.Exists(projectRoot))
            return result;
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(
                         projectRoot,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                if (TryReadCandidate(directory, out WorkspaceCandidate? candidate) && candidate != null)
                    result.Add(candidate);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn(ex, $"Could not scan OurPlan workspaces under '{projectRoot}'.");
        }
        return result;
    }

    private static IEnumerable<WorkspaceCandidate> ReadAllCandidates()
    {
        string root = WorkspacesRoot();
        if (!Directory.Exists(root))
            yield break;
        List<string> projectRoots;
        try
        {
            projectRoots = Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
                .Where(path => Guid.TryParse(Path.GetFileName(path), out _))
                .ToList();
        }
        catch
        {
            yield break;
        }
        foreach (string projectRoot in projectRoots)
        {
            foreach (WorkspaceCandidate candidate in ReadProjectCandidates(projectRoot))
                yield return candidate;
        }
    }

    private static bool TryReadCandidate(string workspace, out WorkspaceCandidate? candidate)
    {
        candidate = null;
        if (!Directory.Exists(workspace) || !File.Exists(Path.Combine(workspace, "Data.xml")))
            return false;
        string markerPath = Path.Combine(workspace, OurPlanPackageFormat.WorkspaceMarkerFileName);
        if (!File.Exists(markerPath))
            return false;
        try
        {
            OurPlanWorkspaceMarker? marker = JsonSerializer.Deserialize<OurPlanWorkspaceMarker>(
                File.ReadAllText(markerPath),
                OurPlanPackageArchive.JsonOptions);
            if (marker == null || marker.MarkerSchemaVersion is < 2 or > 4 ||
                !marker.Format.Equals(OurPlanPackageFormat.FormatId, StringComparison.Ordinal) ||
                !Guid.TryParse(marker.ProjectId, out _) || !Guid.TryParse(marker.RevisionId, out _) ||
                !Guid.TryParse(marker.SessionId, out _) ||
                marker.MarkerSchemaVersion >= 3 && !Guid.TryParse(marker.MarkerVersionToken, out _) ||
                !IsValidInventory(marker.BaseInventory))
            {
                return false;
            }
            marker.PackagePath = NormalizePackagePath(marker.PackagePath);
            candidate = new WorkspaceCandidate(Path.GetFullPath(workspace), marker);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryClaimCandidate(
        WorkspaceCandidate candidate,
        out WorkspaceCandidate? claimedCandidate,
        out OurPlanWorkspaceClaim? claim)
    {
        claimedCandidate = null;
        claim = null;
        string sessionId = Guid.NewGuid().ToString("N");
        if (!TryAcquireWorkspaceClaim(candidate.WorkspaceRoot, sessionId, out claim))
            return false;
        if (!TryReadCandidate(candidate.WorkspaceRoot, out claimedCandidate) || claimedCandidate == null)
        {
            claim?.Dispose();
            claim = null;
            return false;
        }
        return true;
    }

    private static bool TryAcquireWorkspaceClaim(
        string workspaceRoot,
        string sessionId,
        out OurPlanWorkspaceClaim? claim)
    {
        claim = null;
        string workspace = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        if (!Directory.Exists(workspace) ||
            (new DirectoryInfo(workspace).Attributes & FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }

        string guardPath = Path.Combine(workspace, OurPlanPackageFormat.WorkspaceClaimFileName);
        try
        {
            if (File.Exists(guardPath) &&
                (File.GetAttributes(guardPath) & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }
            var handle = new FileStream(
                guardPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.Delete,
                1,
                FileOptions.WriteThrough);
            claim = new OurPlanWorkspaceClaim(workspace, sessionId, handle);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void EnsureSessionClaim(OurPlanPackageSession session)
    {
        if (session.WorkspaceClaim?.Owns(session.WorkspaceRoot, session.SessionId) == true)
            return;
        session.WorkspaceClaim?.Dispose();
        session.WorkspaceClaim = null;
        if (!TryAcquireWorkspaceClaim(
                session.WorkspaceRoot,
                session.SessionId,
                out OurPlanWorkspaceClaim? claim) || claim == null)
        {
            throw new OurPlanPackageConflictException(
                "This local project workspace is already open in another OurPlanCore session.");
        }

        session.WorkspaceClaim = claim;
        OurPlanWorkspaceMarker? marker = ReadMarkerForCas(session.WorkspaceRoot);
        session.ClaimedMarkerSessionId = marker?.SessionId ?? "";
        session.ExpectedMarkerVersionToken = marker?.MarkerVersionToken ?? "";
    }

    private static OurPlanWorkspaceMarker? ReadMarkerForCas(string workspaceRoot)
    {
        string path = Path.Combine(workspaceRoot, OurPlanPackageFormat.WorkspaceMarkerFileName);
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<OurPlanWorkspaceMarker>(
                File.ReadAllText(path),
                OurPlanPackageArchive.JsonOptions)
                ?? throw new OurPlanPackageConflictException(
                    "The workspace ownership marker is empty.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new OurPlanPackageConflictException(
                $"The workspace ownership marker cannot be read safely: {ex.Message}");
        }
    }

    private static bool MarkerProcessAppearsLive(OurPlanWorkspaceMarker marker)
    {
        if (!marker.SessionOpen || marker.ProcessId <= 0 || marker.ProcessStartUtcTicks <= 0)
            return false;
        try
        {
            using Process process = Process.GetProcessById(marker.ProcessId);
            return !process.HasExited &&
                   process.StartTime.ToUniversalTime().Ticks == marker.ProcessStartUtcTicks;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static void ReleaseSessionClaim(OurPlanPackageSession session)
    {
        OurPlanWorkspaceClaim? claim = session.WorkspaceClaim;
        session.WorkspaceClaim = null;
        claim?.Dispose();
    }

    internal static bool TryPruneCleanClosedWorkspace(
        string workspaceRoot,
        DateTime cutoffUtc,
        DateTime missingPackageCutoffUtc)
    {
        string workspace = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        string? projectRoot = Path.GetDirectoryName(workspace);
        if (string.IsNullOrWhiteSpace(projectRoot) ||
            !string.Equals(
                Path.GetDirectoryName(projectRoot),
                WorkspacesRoot(),
                StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParse(Path.GetFileName(projectRoot), out _) ||
            !TryReadCandidate(workspace, out WorkspaceCandidate? candidate) || candidate == null)
        {
            return false;
        }

        OurPlanWorkspaceMarker initial = candidate.Marker;
        DateTime initialCutoff = File.Exists(initial.PackagePath)
            ? cutoffUtc
            : missingPackageCutoffUtc;
        if (initial.Dirty || initial.SessionOpen || MarkerTimestamp(initial) > initialCutoff ||
            !TryClaimCandidate(
                candidate,
                out WorkspaceCandidate? claimedCandidate,
                out OurPlanWorkspaceClaim? claim) ||
            claimedCandidate == null || claim == null)
        {
            return false;
        }

        try
        {
            OurPlanWorkspaceMarker current = claimedCandidate.Marker;
            DateTime currentCutoff = File.Exists(current.PackagePath)
                ? cutoffUtc
                : missingPackageCutoffUtc;
            if (current.Dirty || current.SessionOpen || MarkerTimestamp(current) > currentCutoff ||
                CompareWorkspaceToInventory(
                    claimedCandidate.WorkspaceRoot,
                    current.BaseInventory) != WorkspaceComparison.Unchanged)
            {
                return false;
            }

            string quarantine = Path.Combine(projectRoot, $".prune-{Guid.NewGuid():N}.tmp");
            string markerPath = Path.Combine(
                claimedCandidate.WorkspaceRoot,
                OurPlanPackageFormat.WorkspaceMarkerFileName);
            string pruningToken = Guid.NewGuid().ToString("N");
            current.SessionOpen = true;
            current.SessionId = claim.SessionId;
            current.MarkerVersionToken = pruningToken;
            current.ProcessId = Environment.ProcessId;
            current.ProcessStartUtcTicks = CurrentProcessStartUtcTicks;
            current.StateUpdatedUtc = DateTime.UtcNow.ToString("O");
            IoUtil.WriteAllTextAtomic(
                markerPath,
                JsonSerializer.Serialize(current, OurPlanPackageArchive.JsonOptions));
            // Windows does not rename a directory while a child claim file is open.
            // The live pruning marker prevents another process from adopting this
            // workspace during the small close-and-rename window.
            claim.Dispose();
            claim = null;
            Directory.Move(claimedCandidate.WorkspaceRoot, quarantine);
            bool removed = TryDeleteOwnedWorkspace(quarantine, projectRoot);
            if (removed)
                TryDeleteEmptyDirectory(projectRoot);
            return removed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RestoreClosedMarkerAfterFailedPrune(workspace, initial);
            AppLog.Warn(ex, $"Could not quarantine stale workspace '{workspace}'.");
            return false;
        }
        finally
        {
            claim?.Dispose();
        }
    }

    private static int PruneUnmarkedWorkspaceOrphans(DateTime cutoffUtc)
    {
        string root = WorkspacesRoot();
        if (!Directory.Exists(root))
            return 0;
        int removed = 0;
        IEnumerable<string> projectRoots;
        try
        {
            projectRoots = Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
                .Where(path => Guid.TryParse(Path.GetFileName(path), out _))
                .ToList();
        }
        catch
        {
            return 0;
        }

        foreach (string projectRoot in projectRoots)
        {
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(projectRoot, "*", SearchOption.TopDirectoryOnly).ToList();
            }
            catch
            {
                continue;
            }

            foreach (string child in children)
            {
                if (TryPruneUnmarkedWorkspaceOrphan(child, projectRoot, cutoffUtc))
                    removed++;
            }
            TryDeleteEmptyDirectory(projectRoot);
        }
        return removed;
    }

    private static bool TryPruneUnmarkedWorkspaceOrphan(
        string path,
        string projectRoot,
        DateTime cutoffUtc)
    {
        try
        {
            string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            string name = Path.GetFileName(fullPath);
            if (!IsDirectChild(fullPath, projectRoot) || !IsOwnedOrphanName(name) ||
                !Directory.Exists(fullPath) ||
                (new DirectoryInfo(fullPath).Attributes & FileAttributes.ReparsePoint) != 0 ||
                Directory.GetLastWriteTimeUtc(fullPath) > cutoffUtc ||
                File.Exists(Path.Combine(fullPath, OurPlanPackageFormat.WorkspaceMarkerFileName)) ||
                !TryAcquireWorkspaceClaim(
                    fullPath,
                    Guid.NewGuid().ToString("N"),
                    out OurPlanWorkspaceClaim? claim) || claim == null)
            {
                return false;
            }

            using (claim)
            {
                if (File.Exists(Path.Combine(fullPath, OurPlanPackageFormat.WorkspaceMarkerFileName)))
                    return false;
            }

            string quarantine = Path.Combine(projectRoot, $".orphan-prune-{Guid.NewGuid():N}.tmp");
            Directory.Move(fullPath, quarantine);
            return TryDeleteOwnedWorkspace(quarantine, projectRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn(ex, $"Could not remove stale unmarked OurPlan workspace '{path}'.");
            return false;
        }
    }

    private static bool IsOwnedOrphanName(string name) =>
        name.Equals(WorkingFolderName, StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("working-", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith(".import-", StringComparison.OrdinalIgnoreCase) &&
        name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith(".prune-", StringComparison.OrdinalIgnoreCase) &&
        name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith(".orphan-prune-", StringComparison.OrdinalIgnoreCase) &&
        name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);

    private static void RestoreClosedMarkerAfterFailedPrune(
        string workspace,
        OurPlanWorkspaceMarker original)
    {
        if (!Directory.Exists(workspace) ||
            !TryAcquireWorkspaceClaim(
                workspace,
                Guid.NewGuid().ToString("N"),
                out OurPlanWorkspaceClaim? restoreClaim) ||
            restoreClaim == null)
        {
            return;
        }

        using (restoreClaim)
        {
            original.SessionOpen = false;
            original.ProcessId = 0;
            original.ProcessStartUtcTicks = 0;
            original.MarkerVersionToken = Guid.NewGuid().ToString("N");
            original.StateUpdatedUtc = DateTime.UtcNow.ToString("O");
            IoUtil.WriteAllTextAtomic(
                Path.Combine(workspace, OurPlanPackageFormat.WorkspaceMarkerFileName),
                JsonSerializer.Serialize(original, OurPlanPackageArchive.JsonOptions));
        }
    }

    private static bool IsValidInventory(IReadOnlyList<OurPlanWorkspaceInventoryEntry>? inventory)
    {
        if (inventory == null || inventory.Count == 0)
            return false;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (OurPlanWorkspaceInventoryEntry entry in inventory)
        {
            if (string.IsNullOrWhiteSpace(entry.Path) ||
                !entry.Path.Equals(
                    OurPlanPackageArchive.NormalizeLogicalPath(entry.Path),
                    StringComparison.Ordinal) ||
                !paths.Add(entry.Path) || entry.Length < 0 ||
                entry.LastWriteUtcTicks < DateTime.MinValue.Ticks ||
                entry.LastWriteUtcTicks > DateTime.MaxValue.Ticks ||
                entry.ObjectSha256.Length != 64 || !entry.ObjectSha256.All(Uri.IsHexDigit) ||
                entry.WorkspaceLength >= 0 &&
                (entry.WorkspaceSha256.Length != 64 ||
                 !entry.WorkspaceSha256.All(Uri.IsHexDigit) ||
                 entry.WorkspaceLastWriteUtcTicks < DateTime.MinValue.Ticks ||
                 entry.WorkspaceLastWriteUtcTicks > DateTime.MaxValue.Ticks))
            {
                return false;
            }
        }
        return paths.Contains("Data.xml");
    }

    private static string AllocateWorkspaceRoot(string projectId, string revisionId, string packagePath)
    {
        string projectRoot = ProjectRoot(projectId);
        Directory.CreateDirectory(projectRoot);
        string revision = revisionId[..Math.Min(10, revisionId.Length)];
        string pathKey = Convert.ToHexString(SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(packagePath.ToUpperInvariant())))
            .ToLowerInvariant()[..10];
        return Path.Combine(
            projectRoot,
            $"working-{revision}-{pathKey}-{Guid.NewGuid():N}");
    }

    private static string ProjectRoot(string projectId)
    {
        if (!Guid.TryParse(projectId, out Guid parsed))
            throw new OurPlanPackageValidationException("The package project identifier is invalid.");
        return Path.Combine(WorkspacesRoot(), parsed.ToString("N"));
    }

    private static string WorkspacesRoot() =>
        Path.GetFullPath(Path.Combine(AppIdentity.LocalRoot, WorkspacesFolderName));

    private static PackageState ReadPackageState(string package)
    {
        try
        {
            return new PackageState(
                true,
                OurPlanPackageArchive.ReadManifest(package, verifyObjects: false),
                null);
        }
        catch (Exception ex)
        {
            return new PackageState(false, null, ex.Message);
        }
    }

    private static string HashStable(string path, OurPlanLocalFileStamp before)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.SequentialScan);
        string hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        OurPlanLocalFileStamp after = OurPlanLocalFileStamp.Read(path);
        bool stable = before.IsStrong
            ? before.SameGeneration(after)
            : before.Length == after.Length && before.LastWriteUtcTicks == after.LastWriteUtcTicks;
        if (!stable)
            throw new IOException($"File changed while it was being verified: {path}");
        return hash;
    }

    private static void CopyFileByteExact(string source, string destination)
    {
        byte[] buffer = new byte[1024 * 1024];
        byte[] sourceHash;
        using (var input = new FileStream(
                   source,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read,
                   buffer.Length,
                   FileOptions.SequentialScan))
        using (var output = new FileStream(
                   destination,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None,
                   buffer.Length,
                   FileOptions.SequentialScan))
        using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, read);
                hash.AppendData(buffer, 0, read);
            }
            output.Flush(flushToDisk: true);
            sourceHash = hash.GetHashAndReset();
        }

        using var copied = new FileStream(
            destination,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            buffer.Length,
            FileOptions.SequentialScan);
        byte[] destinationHash = SHA256.HashData(copied);
        if (!sourceHash.AsSpan().SequenceEqual(destinationHash))
            throw new IOException($"Copied file failed byte-exact verification: {source}");
    }

    private static string ResolveWorkspaceFile(string workspace, string logicalPath)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace));
        string result = Path.GetFullPath(Path.Combine(
            root,
            logicalPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!result.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new OurPlanPackageValidationException($"Unsafe workspace inventory path: {logicalPath}");
        return result;
    }

    private static OurPlanWorkspaceInventoryEntry ToInventoryEntry(
        OurPlanPackageFileManifest file,
        OurPlanLocalFileStamp? stamp,
        string? workspaceSha256 = null) =>
        new()
        {
            Path = file.Path,
            ObjectSha256 = file.ObjectSha256,
            Length = file.Length,
            LastWriteUtcTicks = file.LastWriteUtcTicks,
            LocalStamp = stamp,
            WorkspaceSha256 = string.IsNullOrWhiteSpace(workspaceSha256)
                ? file.ObjectSha256
                : workspaceSha256,
            WorkspaceLength = stamp?.Length ?? file.Length,
            WorkspaceLastWriteUtcTicks = stamp?.LastWriteUtcTicks ?? file.LastWriteUtcTicks,
        };

    private static List<OurPlanWorkspaceInventoryEntry> CloneInventory(
        IEnumerable<OurPlanWorkspaceInventoryEntry> inventory) =>
        inventory.Select(item => new OurPlanWorkspaceInventoryEntry
        {
            Path = item.Path,
            ObjectSha256 = item.ObjectSha256,
            Length = item.Length,
            LastWriteUtcTicks = item.LastWriteUtcTicks,
            LocalStamp = CloneStamp(item.LocalStamp),
            WorkspaceSha256 = item.WorkspaceSha256,
            WorkspaceLength = item.WorkspaceLength,
            WorkspaceLastWriteUtcTicks = item.WorkspaceLastWriteUtcTicks,
        }).ToList();

    private static long WorkspaceLength(OurPlanWorkspaceInventoryEntry entry) =>
        entry.WorkspaceLength >= 0 ? entry.WorkspaceLength : entry.Length;

    private static long WorkspaceLastWriteUtcTicks(OurPlanWorkspaceInventoryEntry entry) =>
        entry.WorkspaceLength >= 0
            ? entry.WorkspaceLastWriteUtcTicks
            : entry.LastWriteUtcTicks;

    private static string WorkspaceSha256(OurPlanWorkspaceInventoryEntry entry) =>
        entry.WorkspaceLength >= 0 && entry.WorkspaceSha256.Length == 64
            ? entry.WorkspaceSha256
            : entry.ObjectSha256;

    private static OurPlanLocalFileStamp? CloneStamp(OurPlanLocalFileStamp? stamp) =>
        stamp == null ? null : new OurPlanLocalFileStamp
        {
            Length = stamp.Length,
            LastWriteUtcTicks = stamp.LastWriteUtcTicks,
            CreationUtcTicks = stamp.CreationUtcTicks,
            ChangeTimeFileTime = stamp.ChangeTimeFileTime,
            VolumeSerialNumber = stamp.VolumeSerialNumber,
            FileIdHigh = stamp.FileIdHigh,
            FileIdLow = stamp.FileIdLow,
            IsStrong = stamp.IsStrong,
        };

    private static OurPlanPackageFingerprint FingerprintFromMarker(OurPlanWorkspaceMarker marker) =>
        new(
            marker.PackageLength,
            marker.PackageLastWriteUtcTicks,
            marker.PackageChangeTimeFileTime,
            marker.PackageVolumeSerialNumber,
            marker.PackageFileIdHigh,
            marker.PackageFileIdLow);

    private static OurPlanPackageFingerprint ResolveStableOrEquivalentPackageFingerprint(
        string packagePath,
        OurPlanPackageFingerprint expectedFingerprint,
        OurPlanPackageManifest expectedManifest)
    {
        OurPlanPackageFingerprint current = OurPlanPackageFingerprint.Read(packagePath);
        if (current == expectedFingerprint)
            return current;

        OurPlanPackageManifest verified = OurPlanPackageArchive.ReadManifest(
            packagePath,
            verifyObjects: true);
        OurPlanPackageFingerprint afterVerification = OurPlanPackageFingerprint.Read(packagePath);
        if (afterVerification != current || !ManifestsExactlyMatch(expectedManifest, verified))
            throw new OurPlanPackageConflictException(
                "The .ourplan file changed while it was being opened. Wait for cloud synchronization and retry.");
        return afterVerification;
    }

    private static void BindDirtyPersistence(OurPlanPackageSession session)
    {
        session.DirtyStateChanged ??= changed =>
        {
            try
            {
                MarkDirty(changed);
            }
            catch (Exception ex)
            {
                AppLog.Warn(ex, "Could not persist the dirty OurPlan workspace marker.");
            }
        };
    }

    private static object MarkerLock(string workspace)
    {
        lock (MarkerLocksGate)
        {
            if (!MarkerLocks.TryGetValue(workspace, out object? value))
            {
                value = new object();
                MarkerLocks[workspace] = value;
            }
            return value;
        }
    }

    private static DateTime MarkerTimestamp(OurPlanWorkspaceMarker marker) =>
        DateTime.TryParse(marker.StateUpdatedUtc, out DateTime value)
            ? value.ToUniversalTime()
            : DateTime.MinValue;

    private static long ReadCurrentProcessStartUtcTicks()
    {
        try
        {
            return Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
        }
        catch
        {
            return DateTime.UtcNow.Ticks;
        }
    }

    private static string NormalizePackagePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A package path is required.", nameof(path));
        return Path.GetFullPath(path.Trim());
    }

    private static string CleanDisplayName(string value) =>
        OurPlanCoreJobStore.NormalizeDisplayName(
            string.IsNullOrWhiteSpace(value) ? "Untitled Project" : value.Trim(),
            120);

    private static bool SamePath(string left, string right)
    {
        try
        {
            return Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsDirectChild(string child, string parent) =>
        string.Equals(
            Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(child))),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)),
            StringComparison.OrdinalIgnoreCase);

    private static bool TryDeleteOwnedWorkspace(string path, string projectRoot)
    {
        try
        {
            string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
            if (!IsDirectChild(fullPath, root) || !Directory.Exists(fullPath) ||
                (new DirectoryInfo(fullPath).Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }
            Directory.Delete(fullPath, recursive: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDeleteExportStage(string stage, string exactParent)
    {
        try
        {
            string fullStage = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stage));
            string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(exactParent));
            if (IsDirectChild(fullStage, parent) && Directory.Exists(fullStage) &&
                Path.GetFileName(fullStage).StartsWith(".", StringComparison.Ordinal) &&
                Path.GetFileName(fullStage).EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(fullStage, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup of the exact export staging directory only.
        }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                Directory.Delete(path, recursive: false);
        }
        catch
        {
            // Empty app-owned scaffolding is harmless.
        }
    }

    private enum WorkspaceComparison
    {
        Unchanged,
        Changed,
        Unknown,
    }

    private sealed record WorkspaceCandidate(string WorkspaceRoot, OurPlanWorkspaceMarker Marker);
    private sealed record PackageState(
        bool IsValid,
        OurPlanPackageManifest? Manifest,
        string? Error);
}
