using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace OurPlanCore;

public static partial class OurPlanPackageWorkspace
{
    internal static bool TryGetReusableObjectHash(
        OurPlanPackageSession session,
        string logicalPath,
        OurPlanLocalFileStamp currentStamp,
        out string sha256,
        out long manifestLastWriteUtcTicks)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(currentStamp);
        OurPlanWorkspaceInventoryEntry? cached = session.BaseInventory.FirstOrDefault(entry =>
            entry.Path.Equals(logicalPath, StringComparison.OrdinalIgnoreCase));
        if (!session.BaseInventoryRevisionId.Equals(
                session.BaseRevisionId,
                StringComparison.OrdinalIgnoreCase) ||
            cached?.LocalStamp?.SameGeneration(currentStamp) != true)
        {
            sha256 = "";
            manifestLastWriteUtcTicks = 0;
            return false;
        }

        sha256 = cached.ObjectSha256;
        manifestLastWriteUtcTicks = cached.LastWriteUtcTicks;
        return true;
    }

    internal static bool ManifestMatchesSessionBase(
        OurPlanPackageSession session,
        OurPlanPackageManifest manifest) =>
        session.ProjectId.Equals(manifest.ProjectId, StringComparison.OrdinalIgnoreCase) &&
        session.BaseRevisionId.Equals(manifest.RevisionId, StringComparison.OrdinalIgnoreCase) &&
        ManifestMatchesInventory(manifest, session.BaseInventory);

    internal static void AcceptEquivalentPackageFingerprint(
        OurPlanPackageSession session,
        OurPlanPackageFingerprint fingerprint)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.BaseFingerprint = fingerprint;
        WriteMarkerCore(session, refreshCleanInventory: false);
    }

    internal static bool PersistSavedBase(
        OurPlanPackageSession session,
        OurPlanPackageManifest manifest,
        OurPlanPackageFingerprint fingerprint,
        IReadOnlyDictionary<string, OurPlanSavedWorkspaceFileState> hashedFileStates)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(hashedFileStates);
        OurPlanPackageArchive.ValidateManifest(manifest);
        if (!manifest.ProjectId.Equals(session.ProjectId, StringComparison.OrdinalIgnoreCase))
            throw new OurPlanPackageValidationException("The saved manifest belongs to another project.");
        if (OurPlanPackageFingerprint.Read(session.PackagePath) != fingerprint)
            throw new OurPlanPackageConflictException("The package changed before its saved state was recorded.");

        Dictionary<string, OurPlanSavedWorkspaceFileState> states = hashedFileStates.ToDictionary(
            item => item.Key,
            item => item.Value,
            StringComparer.OrdinalIgnoreCase);
        var inventory = new List<OurPlanWorkspaceInventoryEntry>(manifest.Files.Count);
        bool stable = true;
        foreach (OurPlanPackageFileManifest file in manifest.Files)
        {
            bool fileStable = false;
            OurPlanSavedWorkspaceFileState? savedState = null;
            if (states.TryGetValue(file.Path, out savedState))
            {
                try
                {
                    OurPlanLocalFileStamp current = OurPlanLocalFileStamp.Read(
                        ResolveWorkspaceFile(session.WorkspaceRoot, file.Path));
                    OurPlanLocalFileStamp hashedStamp = savedState.Stamp;
                    fileStable = hashedStamp.IsStrong
                        ? hashedStamp.SameGeneration(current)
                        : hashedStamp.Length == current.Length &&
                          hashedStamp.LastWriteUtcTicks == current.LastWriteUtcTicks &&
                          HashStable(
                              ResolveWorkspaceFile(session.WorkspaceRoot, file.Path),
                              current).Equals(savedState.Sha256, StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    AppLog.Warn(ex, $"Could not capture post-save state for '{file.Path}'.");
                }
            }
            stable &= fileStable;
            inventory.Add(ToInventoryEntry(
                file,
                savedState?.Stamp,
                savedState?.Sha256));
        }

        session.BaseRevisionId = manifest.RevisionId;
        session.BaseFingerprint = fingerprint;
        session.BaseInventoryRevisionId = manifest.RevisionId;
        session.BaseInventory = inventory;
        session.SetDirtyWithoutNotification(!stable);
        session.MarkerSessionOpen = true;
        BindDirtyPersistence(session);
        WriteMarkerCore(session, refreshCleanInventory: false, allowClaimTakeover: true);
        return stable;
    }

    private static void WriteMarkerCore(
        OurPlanPackageSession session,
        bool refreshCleanInventory,
        bool allowClaimTakeover = false)
    {
        string workspace = Path.TrimEndingDirectorySeparator(Path.GetFullPath(session.WorkspaceRoot));
        EnsureSessionClaim(session);
        lock (MarkerLock(workspace))
        {
            OurPlanWorkspaceMarker? existing = ReadMarkerForCas(workspace);
            bool markerExpected = existing != null &&
                                  existing.MarkerVersionToken.Equals(
                                      session.ExpectedMarkerVersionToken,
                                      StringComparison.Ordinal);
            bool sameOwner = existing != null &&
                             existing.SessionId.Equals(session.SessionId, StringComparison.Ordinal);
            bool claimedOwner = existing != null &&
                                existing.SessionId.Equals(
                                    session.ClaimedMarkerSessionId,
                                    StringComparison.Ordinal);
            bool canTakeOwnership = allowClaimTakeover && claimedOwner &&
                                    (!existing!.SessionOpen || !MarkerProcessAppearsLive(existing));
            if (existing == null)
            {
                if (!string.IsNullOrEmpty(session.ExpectedMarkerVersionToken) ||
                    !string.IsNullOrEmpty(session.ClaimedMarkerSessionId))
                {
                    throw new OurPlanPackageConflictException(
                        "The workspace ownership marker disappeared before it could be updated.");
                }
            }
            else if (!markerExpected || !sameOwner && !canTakeOwnership)
            {
                throw new OurPlanPackageConflictException(
                    "The workspace ownership marker changed in another session.");
            }

            if (refreshCleanInventory &&
                (!session.BaseInventoryRevisionId.Equals(
                     session.BaseRevisionId,
                     StringComparison.OrdinalIgnoreCase) ||
                 session.BaseInventory.Count == 0))
            {
                RefreshSessionInventory(session);
            }

            OurPlanPackageFingerprint fingerprint = session.BaseFingerprint;
            string markerVersionToken = Guid.NewGuid().ToString("N");
            var marker = new OurPlanWorkspaceMarker
            {
                PackagePath = NormalizePackagePath(session.PackagePath),
                ProjectId = session.ProjectId,
                RevisionId = session.BaseRevisionId,
                ExtractedUtc = DateTime.UtcNow.ToString("O"),
                DisplayName = session.DisplayName,
                PackageLength = fingerprint.Length,
                PackageLastWriteUtcTicks = fingerprint.LastWriteUtcTicks,
                PackageChangeTimeFileTime = fingerprint.ChangeTimeFileTime,
                PackageVolumeSerialNumber = fingerprint.VolumeSerialNumber,
                PackageFileIdHigh = fingerprint.FileIdHigh,
                PackageFileIdLow = fingerprint.FileIdLow,
                Dirty = session.HasUnpackagedChanges,
                SessionOpen = session.MarkerSessionOpen,
                SessionId = session.SessionId,
                MarkerVersionToken = markerVersionToken,
                ProcessId = Environment.ProcessId,
                ProcessStartUtcTicks = CurrentProcessStartUtcTicks,
                StateUpdatedUtc = DateTime.UtcNow.ToString("O"),
                BaseInventory = CloneInventory(session.BaseInventory),
            };
            IoUtil.WriteAllTextAtomic(
                Path.Combine(workspace, OurPlanPackageFormat.WorkspaceMarkerFileName),
                JsonSerializer.Serialize(marker, OurPlanPackageArchive.JsonOptions));
            session.ClaimedMarkerSessionId = session.SessionId;
            session.ExpectedMarkerVersionToken = markerVersionToken;
        }
    }

    private static void RefreshSessionInventory(OurPlanPackageSession session)
    {
        try
        {
            OurPlanPackageManifest manifest = OurPlanPackageArchive.ReadManifest(
                session.PackagePath,
                verifyObjects: false);
            if (!manifest.ProjectId.Equals(session.ProjectId, StringComparison.OrdinalIgnoreCase) ||
                !manifest.RevisionId.Equals(session.BaseRevisionId, StringComparison.OrdinalIgnoreCase))
            {
                session.SetDirtyWithoutNotification(true);
                return;
            }

            (List<OurPlanWorkspaceInventoryEntry> inventory, bool matches) = CaptureVerifiedInventory(
                session.WorkspaceRoot,
                manifest,
                session.BaseInventory);
            session.BaseInventory = inventory;
            session.BaseInventoryRevisionId = manifest.RevisionId;
            if (!matches)
                session.SetDirtyWithoutNotification(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            session.SetDirtyWithoutNotification(true);
            AppLog.Warn(ex, "Could not refresh the OurPlan workspace base inventory; it remains recoverable.");
        }
    }

    private static OurPlanPackageSession CreateSession(
        string package,
        string workspace,
        OurPlanPackageManifest manifest,
        OurPlanPackageFingerprint fingerprint,
        IReadOnlyList<OurPlanWorkspaceInventoryEntry> inventory,
        OurPlanWorkspaceClaim? claim = null,
        OurPlanWorkspaceMarker? claimedMarker = null)
    {
        var session = new OurPlanPackageSession
        {
            PackagePath = package,
            WorkspaceRoot = workspace,
            ProjectId = manifest.ProjectId,
            DisplayName = manifest.DisplayName,
            BaseRevisionId = manifest.RevisionId,
            BaseFingerprint = fingerprint,
            HasUnpackagedChanges = false,
            BaseInventoryRevisionId = manifest.RevisionId,
            BaseInventory = CloneInventory(inventory),
            SessionId = claim?.SessionId ?? Guid.NewGuid().ToString("N"),
            WorkspaceClaim = claim,
            ClaimedMarkerSessionId = claimedMarker?.SessionId ?? "",
            ExpectedMarkerVersionToken = claimedMarker?.MarkerVersionToken ?? "",
        };
        BindDirtyPersistence(session);
        return session;
    }

    private static OurPlanPackageSession CreateRecoverySession(
        WorkspaceCandidate candidate,
        string package,
        OurPlanWorkspaceClaim claim)
    {
        OurPlanWorkspaceMarker marker = candidate.Marker;
        var session = new OurPlanPackageSession
        {
            PackagePath = package,
            WorkspaceRoot = candidate.WorkspaceRoot,
            ProjectId = marker.ProjectId,
            DisplayName = string.IsNullOrWhiteSpace(marker.DisplayName)
                ? Path.GetFileNameWithoutExtension(package)
                : marker.DisplayName,
            BaseRevisionId = marker.RevisionId,
            BaseFingerprint = FingerprintFromMarker(marker),
            HasUnpackagedChanges = true,
            IsRecoverySession = true,
            RecoveryReason = "A preserved local working copy contains changes or belongs to an interrupted session.",
            BaseInventoryRevisionId = marker.RevisionId,
            BaseInventory = CloneInventory(marker.BaseInventory),
            SessionId = claim.SessionId,
            WorkspaceClaim = claim,
            ClaimedMarkerSessionId = marker.SessionId,
            ExpectedMarkerVersionToken = marker.MarkerVersionToken,
        };
        BindDirtyPersistence(session);
        return session;
    }

    private static IReadOnlyList<OurPlanPackageRecoveryInfo> BuildRecoveryInfos(
        string package,
        IReadOnlyList<WorkspaceCandidate> candidates,
        OurPlanPackageManifest manifest,
        string? exceptWorkspace)
    {
        var packageState = new PackageState(true, manifest, null);
        return candidates
            .Where(candidate => exceptWorkspace == null ||
                                !SamePath(candidate.WorkspaceRoot, exceptWorkspace))
            .Select(candidate => BuildRecoveryInfoIfClaimable(candidate, packageState))
            .Where(info => info != null)
            .Cast<OurPlanPackageRecoveryInfo>()
            .OrderByDescending(info => info.StateUpdatedUtc)
            .ToList();
    }

    private static OurPlanPackageRecoveryInfo? BuildRecoveryInfoIfClaimable(
        WorkspaceCandidate candidate,
        PackageState packageState)
    {
        if (!TryClaimCandidate(
                candidate,
                out WorkspaceCandidate? claimedCandidate,
                out OurPlanWorkspaceClaim? claim) ||
            claimedCandidate == null || claim == null)
        {
            return null;
        }

        try
        {
            if (MarkerProcessAppearsLive(claimedCandidate.Marker))
                return null;
            return BuildRecoveryInfo(claimedCandidate, packageState);
        }
        finally
        {
            claim.Dispose();
        }
    }

    private static OurPlanPackageRecoveryInfo? BuildRecoveryInfo(
        WorkspaceCandidate candidate,
        PackageState packageState)
    {
        OurPlanWorkspaceMarker marker = candidate.Marker;
        WorkspaceComparison comparison = CompareWorkspaceToInventory(
            candidate.WorkspaceRoot,
            marker.BaseInventory);
        bool packageUnavailable = !packageState.IsValid;
        bool packageChanged = packageState.Manifest != null &&
                              !packageState.Manifest.RevisionId.Equals(
                                  marker.RevisionId,
                                  StringComparison.OrdinalIgnoreCase);
        if (!packageUnavailable && comparison == WorkspaceComparison.Unchanged)
        {
            return null;
        }

        OurPlanRecoveryKind kind = packageUnavailable
            ? OurPlanRecoveryKind.PackageUnavailable
            : packageChanged
                ? OurPlanRecoveryKind.PackageChanged
                : comparison != WorkspaceComparison.Unchanged || marker.Dirty
                    ? OurPlanRecoveryKind.UnsavedChanges
                    : OurPlanRecoveryKind.InterruptedSession;
        return new OurPlanPackageRecoveryInfo(
            marker.PackagePath,
            candidate.WorkspaceRoot,
            marker.ProjectId,
            marker.RevisionId,
            marker.DisplayName,
            kind,
            MarkerTimestamp(marker));
    }

    private static WorkspaceComparison CompareWorkspaceToInventory(
        string workspace,
        IReadOnlyList<OurPlanWorkspaceInventoryEntry> expected)
    {
        if (expected.Count == 0 || !Directory.Exists(workspace))
            return WorkspaceComparison.Unknown;
        try
        {
            IReadOnlyList<OurPlanPackageSourceFile> actual = OurPlanPackageFileSelector.Collect(workspace);
            if (actual.Count != expected.Count)
                return WorkspaceComparison.Changed;
            Dictionary<string, OurPlanWorkspaceInventoryEntry> byPath = expected.ToDictionary(
                entry => entry.Path,
                StringComparer.OrdinalIgnoreCase);
            foreach (OurPlanPackageSourceFile file in actual)
            {
                if (!byPath.TryGetValue(file.LogicalPath, out OurPlanWorkspaceInventoryEntry? baseline) ||
                    file.Length != WorkspaceLength(baseline))
                {
                    return WorkspaceComparison.Changed;
                }

                OurPlanLocalFileStamp stamp = OurPlanLocalFileStamp.Read(file.FullPath);
                bool timestampMatches = !RequiresExactTimestamp(file.LogicalPath) ||
                                        file.LastWriteUtcTicks == WorkspaceLastWriteUtcTicks(baseline);
                if (timestampMatches && baseline.LocalStamp?.SameGeneration(stamp) == true)
                    continue;
                if (!timestampMatches || !HashStable(file.FullPath, stamp).Equals(
                        WorkspaceSha256(baseline),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return WorkspaceComparison.Changed;
                }
            }
            return WorkspaceComparison.Unchanged;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn(ex, $"Could not compare recovery workspace '{workspace}' to its base inventory.");
            return WorkspaceComparison.Unknown;
        }
    }

    private static List<OurPlanWorkspaceInventoryEntry> CaptureTrustedInventory(
        string workspace,
        OurPlanPackageManifest manifest)
    {
        var result = new List<OurPlanWorkspaceInventoryEntry>(manifest.Files.Count);
        foreach (OurPlanPackageFileManifest file in manifest.Files)
        {
            string fullPath = ResolveWorkspaceFile(workspace, file.Path);
            OurPlanLocalFileStamp stamp = OurPlanLocalFileStamp.Read(fullPath);
            if (stamp.Length != file.Length || stamp.LastWriteUtcTicks != file.LastWriteUtcTicks)
                throw new IOException($"Extracted project file metadata changed: {file.Path}");
            result.Add(ToInventoryEntry(file, stamp, file.ObjectSha256));
        }
        return result;
    }

    private static (List<OurPlanWorkspaceInventoryEntry> Inventory, bool Matches)
        CaptureVerifiedInventory(
            string workspace,
            OurPlanPackageManifest manifest,
            IReadOnlyList<OurPlanWorkspaceInventoryEntry> previous)
    {
        Dictionary<string, OurPlanWorkspaceInventoryEntry> prior = previous.ToDictionary(
            entry => entry.Path,
            StringComparer.OrdinalIgnoreCase);
        var result = new List<OurPlanWorkspaceInventoryEntry>(manifest.Files.Count);
        bool matches = true;
        foreach (OurPlanPackageFileManifest file in manifest.Files)
        {
            OurPlanLocalFileStamp? stamp = null;
            bool metadataMatches = false;
            bool contentMatches = false;
            try
            {
                string fullPath = ResolveWorkspaceFile(workspace, file.Path);
                stamp = OurPlanLocalFileStamp.Read(fullPath);
                OurPlanWorkspaceInventoryEntry? old = prior.GetValueOrDefault(file.Path);
                long expectedLength = old == null ? file.Length : WorkspaceLength(old);
                long expectedTimestamp = old == null
                    ? file.LastWriteUtcTicks
                    : WorkspaceLastWriteUtcTicks(old);
                string expectedHash = old == null
                    ? file.ObjectSha256
                    : WorkspaceSha256(old);
                metadataMatches = stamp.Length == expectedLength &&
                                  (!RequiresExactTimestamp(file.Path) ||
                                   stamp.LastWriteUtcTicks == expectedTimestamp);
                contentMatches = old != null &&
                                 old.ObjectSha256.Equals(
                                     file.ObjectSha256,
                                     StringComparison.OrdinalIgnoreCase) &&
                                 old.LocalStamp?.SameGeneration(stamp) == true;
                if (!contentMatches)
                {
                    contentMatches = HashStable(fullPath, stamp).Equals(
                        expectedHash,
                        StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn(ex, $"Workspace file changed after package save: '{file.Path}'.");
            }
            matches &= metadataMatches && contentMatches;
            OurPlanWorkspaceInventoryEntry? priorEntry = prior.GetValueOrDefault(file.Path);
            result.Add(ToInventoryEntry(
                file,
                metadataMatches && contentMatches ? stamp : priorEntry?.LocalStamp,
                priorEntry == null ? file.ObjectSha256 : WorkspaceSha256(priorEntry)));
        }
        return (result, matches);
    }

    private static bool ManifestMatchesInventory(
        OurPlanPackageManifest manifest,
        IReadOnlyList<OurPlanWorkspaceInventoryEntry> inventory)
    {
        if (manifest.Files.Count != inventory.Count)
            return false;
        Dictionary<string, OurPlanWorkspaceInventoryEntry> expected = inventory.ToDictionary(
            item => item.Path,
            StringComparer.OrdinalIgnoreCase);
        return manifest.Files.All(file =>
            expected.TryGetValue(file.Path, out OurPlanWorkspaceInventoryEntry? item) &&
            item.ObjectSha256.Equals(file.ObjectSha256, StringComparison.OrdinalIgnoreCase) &&
            item.Length == file.Length &&
            item.LastWriteUtcTicks == file.LastWriteUtcTicks);
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
            OurPlanPackageFileManifest expected = left.Files[index];
            OurPlanPackageFileManifest actual = right.Files[index];
            if (!expected.Path.Equals(actual.Path, StringComparison.Ordinal) ||
                !expected.ObjectSha256.Equals(actual.ObjectSha256, StringComparison.OrdinalIgnoreCase) ||
                expected.Length != actual.Length ||
                expected.LastWriteUtcTicks != actual.LastWriteUtcTicks)
            {
                return false;
            }
        }
        return true;
    }

    private static bool RequiresExactTimestamp(string logicalPath)
    {
        if (Path.GetExtension(logicalPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return true;
        return logicalPath.Split('/').Any(segment => segment.Equals(
            RasterSheetCacheService.CacheFolderName,
            StringComparison.OrdinalIgnoreCase));
    }

    private static void CopyDurableFilesStable(string source, string destination)
    {
        IReadOnlyList<OurPlanPackageSourceFile> files = OurPlanPackageFileSelector.Collect(source);
        var stamps = new Dictionary<string, OurPlanLocalFileStamp>(StringComparer.OrdinalIgnoreCase);
        foreach (OurPlanPackageSourceFile file in files)
        {
            OurPlanLocalFileStamp before = OurPlanLocalFileStamp.Read(file.FullPath);
            string output = Path.Combine(
                destination,
                file.LogicalPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            CopyFileByteExact(file.FullPath, output);
            File.SetLastWriteTimeUtc(output, new DateTime(file.LastWriteUtcTicks, DateTimeKind.Utc));
            OurPlanLocalFileStamp after = OurPlanLocalFileStamp.Read(file.FullPath);
            if (before.IsStrong ? !before.SameGeneration(after) :
                before.Length != after.Length || before.LastWriteUtcTicks != after.LastWriteUtcTicks)
            {
                throw new IOException($"Project file changed while it was copied: {file.LogicalPath}");
            }
            stamps[file.LogicalPath] = after;
        }

        IReadOnlyList<OurPlanPackageSourceFile> final = OurPlanPackageFileSelector.Collect(source);
        if (final.Count != files.Count)
            throw new IOException("The project changed while its managed copy was being created.");
        foreach (OurPlanPackageSourceFile file in final)
        {
            if (!stamps.TryGetValue(file.LogicalPath, out OurPlanLocalFileStamp? before))
                throw new IOException($"The project changed while it was copied: {file.LogicalPath}");
            OurPlanLocalFileStamp after = OurPlanLocalFileStamp.Read(file.FullPath);
            if (before.IsStrong ? !before.SameGeneration(after) :
                before.Length != after.Length || before.LastWriteUtcTicks != after.LastWriteUtcTicks)
            {
                throw new IOException($"The project changed while it was copied: {file.LogicalPath}");
            }
        }
    }
}
