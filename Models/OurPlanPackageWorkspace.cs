using System.IO;

namespace OurPlanCore;

public static partial class OurPlanPackageWorkspace
{
    private const string WorkspacesFolderName = "project-workspaces";
    private const string WorkingFolderName = "working";
    private static readonly long CurrentProcessStartUtcTicks = ReadCurrentProcessStartUtcTicks();
    private static readonly Dictionary<string, object> MarkerLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object MarkerLocksGate = new();

    public static OurPlanPackageSession Open(string packagePath)
    {
        string package = NormalizePackagePath(packagePath);
        OurPlanPackageFingerprint before = OurPlanPackageFingerprint.Read(package);
        OurPlanPackageManifest manifest = OurPlanPackageArchive.ReadManifest(package, verifyObjects: false);
        string projectRoot = ProjectRoot(manifest.ProjectId);
        List<WorkspaceCandidate> candidates = ReadProjectCandidates(projectRoot)
            .Where(candidate => SamePath(candidate.Marker.PackagePath, package) &&
                                candidate.Marker.ProjectId.Equals(
                                    manifest.ProjectId,
                                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        bool packageFullyVerified = false;
        foreach (WorkspaceCandidate candidate in candidates
                     .Where(candidate => candidate.Marker.RevisionId.Equals(
                         manifest.RevisionId,
                         StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(candidate => MarkerTimestamp(candidate.Marker)))
        {
            if (!TryClaimCandidate(
                    candidate,
                    out WorkspaceCandidate? claimedCandidate,
                    out OurPlanWorkspaceClaim? claim) ||
                claimedCandidate == null || claim == null)
            {
                continue;
            }

            try
            {
                OurPlanWorkspaceMarker claimedMarker = claimedCandidate.Marker;
                if (MarkerProcessAppearsLive(claimedMarker) ||
                    !SamePath(claimedMarker.PackagePath, package) ||
                    !claimedMarker.ProjectId.Equals(manifest.ProjectId, StringComparison.OrdinalIgnoreCase) ||
                    !claimedMarker.RevisionId.Equals(manifest.RevisionId, StringComparison.OrdinalIgnoreCase) ||
                    !ManifestMatchesInventory(manifest, claimedMarker.BaseInventory))
                {
                    continue;
                }

                bool exactFingerprint = FingerprintFromMarker(claimedMarker) == before;
                if (!exactFingerprint && !packageFullyVerified)
                {
                    before = ResolveStableOrEquivalentPackageFingerprint(
                        package,
                        before,
                        manifest);
                    packageFullyVerified = true;
                }

                WorkspaceComparison comparison = CompareWorkspaceToInventory(
                    claimedCandidate.WorkspaceRoot,
                    claimedMarker.BaseInventory);
                if (comparison == WorkspaceComparison.Unchanged)
                {
                    try
                    {
                        ValidateWorkspaceForOpen(
                            claimedCandidate.WorkspaceRoot,
                            manifest,
                            requireExactManifestFiles: true);
                    }
                    catch (OurPlanPackageValidationException ex)
                    {
                        AppLog.Warn(ex, $"Cached package workspace failed validation: {claimedCandidate.WorkspaceRoot}");
                        continue;
                    }
                    before = ResolveStableOrEquivalentPackageFingerprint(
                        package,
                        before,
                        manifest);
                    OurPlanPackageSession reused = CreateSession(
                        package,
                        claimedCandidate.WorkspaceRoot,
                        manifest,
                        before,
                        claimedMarker.BaseInventory,
                        claim,
                        claimedMarker);
                    reused.AvailableRecoverySessions = BuildRecoveryInfos(
                        package,
                        candidates,
                        manifest,
                        exceptWorkspace: claimedCandidate.WorkspaceRoot);
                    MarkSessionOpen(reused);
                    claim = null;
                    return reused;
                }

                // Changed local data is offered for explicit recovery, never opened implicitly.
            }
            finally
            {
                claim?.Dispose();
            }
        }

        IReadOnlyList<OurPlanPackageRecoveryInfo> recoveries = BuildRecoveryInfos(
            package,
            candidates,
            manifest,
            exceptWorkspace: null);
        string workingRoot = AllocateWorkspaceRoot(manifest.ProjectId, manifest.RevisionId, package);
        string stage = workingRoot + $".{Guid.NewGuid():N}.tmp";
        OurPlanPackageSession? opened = null;
        try
        {
            OurPlanPackageArchive.Extract(package, stage);
            ValidateWorkspaceForOpen(stage, manifest, requireExactManifestFiles: true);
            before = ResolveStableOrEquivalentPackageFingerprint(package, before, manifest);
            Directory.Move(stage, workingRoot);
            List<OurPlanWorkspaceInventoryEntry> inventory = CaptureTrustedInventory(
                workingRoot,
                manifest);
            opened = CreateSession(
                package,
                workingRoot,
                manifest,
                before,
                inventory);
            opened.AvailableRecoverySessions = recoveries;
            MarkSessionOpen(opened);
            return opened;
        }
        catch
        {
            if (opened != null)
                ReleaseSessionClaim(opened);
            TryDeleteOwnedWorkspace(stage, projectRoot);
            if (Directory.Exists(workingRoot) &&
                !File.Exists(Path.Combine(workingRoot, OurPlanPackageFormat.WorkspaceMarkerFileName)))
            {
                TryDeleteOwnedWorkspace(workingRoot, projectRoot);
            }
            throw;
        }
    }

    public static bool TryOpenRecoverySession(
        string packagePath,
        out OurPlanPackageSession? session)
    {
        string package;
        try
        {
            package = NormalizePackagePath(packagePath);
        }
        catch
        {
            session = null;
            return false;
        }

        OurPlanPackageRecoveryInfo? recovery = FindRecoverySessions(package).FirstOrDefault();
        if (recovery == null)
        {
            session = null;
            return false;
        }

        return TryOpenRecoverySession(recovery, out session);
    }

    public static bool TryOpenRecoverySession(
        OurPlanPackageRecoveryInfo recovery,
        out OurPlanPackageSession? session)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        string package = NormalizePackagePath(recovery.PackagePath);
        string projectRoot = ProjectRoot(recovery.ProjectId);
        if (!IsDirectChild(recovery.WorkspaceRoot, projectRoot) ||
            !TryReadCandidate(recovery.WorkspaceRoot, out WorkspaceCandidate? candidate) ||
            candidate == null ||
            !TryClaimCandidate(
                candidate,
                out WorkspaceCandidate? claimedCandidate,
                out OurPlanWorkspaceClaim? claim) ||
            claimedCandidate == null || claim == null)
        {
            session = null;
            return false;
        }

        try
        {
            OurPlanWorkspaceMarker marker = claimedCandidate.Marker;
            if (MarkerProcessAppearsLive(marker) ||
                !SamePath(marker.PackagePath, package) ||
                !marker.ProjectId.Equals(recovery.ProjectId, StringComparison.OrdinalIgnoreCase) ||
                !marker.RevisionId.Equals(recovery.BaseRevisionId, StringComparison.OrdinalIgnoreCase))
            {
                session = null;
                return false;
            }

            try
            {
                OurPlanPackageFileSelector.ValidateRecoveryTreeSafety(
                    claimedCandidate.WorkspaceRoot);
                OurPlanPackagePortability.ValidateRecoveryReferences(
                    claimedCandidate.WorkspaceRoot);
            }
            catch (OurPlanPackageValidationException ex)
            {
                AppLog.Warn(ex, $"Recovery workspace failed validation: {claimedCandidate.WorkspaceRoot}");
                session = null;
                return false;
            }

            session = CreateRecoverySession(claimedCandidate, package, claim);
            session.AvailableRecoverySessions = FindRecoverySessions(package)
                .Where(info => !SamePath(info.WorkspaceRoot, recovery.WorkspaceRoot))
                .ToList();
            MarkSessionOpen(session);
            claim = null;
            return true;
        }
        finally
        {
            claim?.Dispose();
        }
    }

    public static IReadOnlyList<OurPlanPackageRecoveryInfo> FindRecoverySessions(string packagePath)
    {
        string package = NormalizePackagePath(packagePath);
        PackageState packageState = ReadPackageState(package);
        return ReadAllCandidates()
            .Where(candidate => SamePath(candidate.Marker.PackagePath, package))
            .Select(candidate => BuildRecoveryInfoIfClaimable(candidate, packageState))
            .Where(info => info != null)
            .Cast<OurPlanPackageRecoveryInfo>()
            .OrderByDescending(info => info.StateUpdatedUtc)
            .ToList();
    }

    public static (OurPlanCoreJob Job, string ProjectId, string RevisionId) CreateNewJob(
        string displayName)
    {
        string cleanName = CleanDisplayName(displayName);
        OurPlanManagedWorkspaceReservation reservation = ReserveManagedWorkspace(cleanName);
        try
        {
            OurPlanCoreJob created = OurPlanCoreJobStore.CreateJob(
                reservation.ImportParentRoot,
                cleanName);
            OurPlanCoreJob completed = CompleteManagedWorkspace(reservation, created.RootPath);
            return (completed, reservation.ProjectId, reservation.RevisionId);
        }
        catch
        {
            AbandonManagedWorkspace(reservation);
            throw;
        }
    }

    public static (OurPlanCoreJob Job, string ProjectId, string RevisionId) CreateManagedCopyFromJob(
        string existingRoot,
        string displayName)
    {
        string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(existingRoot));
        string cleanName = CleanDisplayName(displayName);
        OurPlanManagedWorkspaceReservation reservation = ReserveManagedWorkspace(cleanName);
        try
        {
            Directory.CreateDirectory(reservation.ExpectedJobRoot);
            CopyDurableFilesStable(source, reservation.ExpectedJobRoot);
            OurPlanPackagePortability.RebaseInternalReferences(
                source,
                reservation.ExpectedJobRoot);
            OurPlanCoreJob completed = CompleteManagedWorkspace(
                reservation,
                reservation.ExpectedJobRoot);
            return (completed, reservation.ProjectId, reservation.RevisionId);
        }
        catch
        {
            AbandonManagedWorkspace(reservation);
            throw;
        }
    }

    public static OurPlanManagedWorkspaceReservation ReserveManagedWorkspace(string displayName)
    {
        string cleanName = CleanDisplayName(displayName);
        string projectId = Guid.NewGuid().ToString("N");
        string revisionId = Guid.NewGuid().ToString("N");
        string projectRoot = ProjectRoot(projectId);
        string importParent = Path.Combine(projectRoot, $".import-{Guid.NewGuid():N}.tmp");
        string expected = Path.Combine(
            importParent,
            OurPlanCoreJobStore.SanitizeName(cleanName, 120));
        Directory.CreateDirectory(importParent);
        return new OurPlanManagedWorkspaceReservation(
            projectId,
            revisionId,
            cleanName,
            importParent,
            expected,
            Path.Combine(projectRoot, WorkingFolderName));
    }

    public static OurPlanCoreJob CompleteManagedWorkspace(
        OurPlanManagedWorkspaceReservation reservation,
        string importedJobRoot)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(importedJobRoot));
        string importParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(reservation.ImportParentRoot));
        if (!IsDirectChild(source, importParent) || !Directory.Exists(source) ||
            !File.Exists(Path.Combine(source, "Data.xml")))
        {
            throw new OurPlanPackageValidationException(
                "The imported job is not a complete job inside its reserved staging folder.");
        }

        string destination = Path.GetFullPath(reservation.WorkspaceRoot);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException($"The managed workspace already exists: {destination}");

        Directory.Move(source, destination);
        TryDeleteEmptyDirectory(importParent);
        OurPlanCoreJobStore.UpdateItemName(destination, reservation.DisplayName);
        return OurPlanCoreJobStore.LoadJob(destination);
    }

    public static void AbandonManagedWorkspace(OurPlanManagedWorkspaceReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        string projectRoot = ProjectRoot(reservation.ProjectId);
        TryDeleteOwnedWorkspace(reservation.ImportParentRoot, projectRoot);
        if (!File.Exists(Path.Combine(
                reservation.WorkspaceRoot,
                OurPlanPackageFormat.WorkspaceMarkerFileName)))
        {
            TryDeleteOwnedWorkspace(reservation.WorkspaceRoot, projectRoot);
        }
        TryDeleteEmptyDirectory(projectRoot);
    }

    public static void AbandonUnpublishedWorkspace(string workspaceRoot, string projectId)
    {
        string projectRoot = ProjectRoot(projectId);
        string workspace = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        if (File.Exists(Path.Combine(workspace, OurPlanPackageFormat.WorkspaceMarkerFileName)))
            return;
        TryDeleteOwnedWorkspace(workspace, projectRoot);
        TryDeleteEmptyDirectory(projectRoot);
    }

    public static void WriteMarker(OurPlanPackageSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        BindDirtyPersistence(session);
        WriteMarkerCore(
            session,
            refreshCleanInventory: !session.HasUnpackagedChanges,
            allowClaimTakeover: true);
    }

    public static void MarkSessionOpen(OurPlanPackageSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.MarkerSessionOpen = true;
        BindDirtyPersistence(session);
        WriteMarkerCore(
            session,
            refreshCleanInventory: !session.HasUnpackagedChanges,
            allowClaimTakeover: true);
    }

    public static void MarkDirty(OurPlanPackageSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.SetDirtyWithoutNotification(true);
        BindDirtyPersistence(session);
        WriteMarkerCore(session, refreshCleanInventory: false);
    }

    public static void MarkClean(OurPlanPackageSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.SetDirtyWithoutNotification(false);
        session.BaseInventoryRevisionId = "";
        BindDirtyPersistence(session);
        WriteMarkerCore(session, refreshCleanInventory: true);
    }

    public static void MarkSessionClosed(OurPlanPackageSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        try
        {
            WorkspaceComparison comparison = CompareWorkspaceToInventory(
                session.WorkspaceRoot,
                session.BaseInventory);
            if (comparison != WorkspaceComparison.Unchanged)
                session.SetDirtyWithoutNotification(true);
            session.MarkerSessionOpen = false;
            WriteMarkerCore(session, refreshCleanInventory: false);
        }
        finally
        {
            session.DirtyStateChanged = null;
            ReleaseSessionClaim(session);
        }
    }

    internal static void TransferWorkspaceClaim(
        OurPlanPackageSession source,
        OurPlanPackageSession target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        if (!SamePath(source.WorkspaceRoot, target.WorkspaceRoot))
            throw new ArgumentException("Workspace ownership can only transfer within the same root.");
        if (target.WorkspaceClaim != null)
            throw new InvalidOperationException("The target session already has a workspace claim.");
        OurPlanWorkspaceClaim claim = source.WorkspaceClaim
            ?? throw new OurPlanPackageConflictException("The source session no longer owns its workspace.");
        if (!claim.Owns(source.WorkspaceRoot, source.SessionId))
            throw new OurPlanPackageConflictException("The source workspace claim is no longer valid.");

        target.SessionId = source.SessionId;
        target.WorkspaceClaim = claim;
        target.ClaimedMarkerSessionId = source.ClaimedMarkerSessionId;
        target.ExpectedMarkerVersionToken = source.ExpectedMarkerVersionToken;
        target.MarkerSessionOpen = true;
        source.WorkspaceClaim = null;
        source.DirtyStateChanged = null;
        source.MarkerSessionOpen = false;
        BindDirtyPersistence(target);
    }

    public static int PruneCleanClosedWorkspaces(TimeSpan? minimumAge = null)
    {
        TimeSpan age = minimumAge ?? TimeSpan.FromDays(90);
        if (age < TimeSpan.FromDays(7))
            age = TimeSpan.FromDays(7);
        DateTime cutoff = DateTime.UtcNow - age;
        DateTime missingPackageCutoff = DateTime.UtcNow -
                                        (age > TimeSpan.FromDays(180)
                                            ? age
                                            : TimeSpan.FromDays(180));
        int removed = 0;
        foreach (WorkspaceCandidate candidate in ReadAllCandidates())
        {
            if (TryPruneCleanClosedWorkspace(
                    candidate.WorkspaceRoot,
                    cutoff,
                    missingPackageCutoff))
                removed++;
        }
        removed += PruneUnmarkedWorkspaceOrphans(cutoff);
        return removed;
    }

    public static void ExportLegacyCopy(string workspaceRoot, string destinationRoot)
    {
        string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        string destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationRoot));
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException($"The project copy destination already exists: {destination}");
        if (destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The project copy cannot be created inside the active working project.");

        string? parent = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(parent))
            throw new IOException("The project copy destination has no parent folder.");
        Directory.CreateDirectory(parent);
        string stage = Path.Combine(parent, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(stage);
            CopyDurableFilesStable(source, stage);
            OurPlanPackagePortability.RebaseInternalReferences(source, stage);
            Directory.Move(stage, destination);
        }
        catch
        {
            TryDeleteExportStage(stage, parent);
            throw;
        }
    }
}
