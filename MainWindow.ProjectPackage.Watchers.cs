using System.IO;

namespace OurPlanCore;

public partial class MainWindow
{
    private readonly SemaphoreSlim _packageArtifactInspectionGate = new(1, 1);
    private long _packageArtifactEventGeneration;

    private void StartPackageWorkspaceWatcher(OurPlanPackageSession session)
    {
        StopPackageWorkspaceWatcher();
        var watcher = new FileSystemWatcher(session.WorkspaceRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                           NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = false,
        };
        watcher.Changed += PackageWorkspaceChanged;
        watcher.Created += PackageWorkspaceChanged;
        watcher.Deleted += PackageWorkspaceChanged;
        watcher.Renamed += PackageWorkspaceChanged;
        _packageWorkspaceWatcher = watcher;
        Interlocked.Exchange(ref _packageWorkspaceGeneration, 0);
        watcher.EnableRaisingEvents = true;
        StartPackageArtifactWatcher(session);
        if (session.HasUnpackagedChanges)
            QueueAutomaticPackageCheckpoint(session);
    }

    private void PackageWorkspaceChanged(object sender, FileSystemEventArgs e)
    {
        OurPlanPackageSession? session = _currentPackageSession;
        if (session == null || !ReferenceEquals(sender, _packageWorkspaceWatcher) ||
            IsTransientPackageWorkspacePath(session.WorkspaceRoot, e.FullPath))
        {
            return;
        }

        Interlocked.Increment(ref _packageWorkspaceGeneration);
        session.HasUnpackagedChanges = true;
        long autosaveEpoch = Interlocked.Read(ref _packageAutosaveScheduleEpoch);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!ReferenceEquals(session, _currentPackageSession) ||
                !ReferenceEquals(sender, _packageWorkspaceWatcher) ||
                autosaveEpoch != Interlocked.Read(ref _packageAutosaveScheduleEpoch))
            {
                return;
            }
            if (!_packageAutosaveBlocked)
                _packageSaveStatus = "Save: Pending";
            ScheduleAutomaticPackageCheckpoint(
                session,
                waitForQuietPeriod: true,
                retryDelay: TimeSpan.Zero);
            UpdateStatusBarSegments();
        }));
    }

    private static bool IsTransientPackageWorkspacePath(string root, string path)
    {
        string relative;
        try
        {
            relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
        }
        catch
        {
            return true;
        }

        string first = relative.Split('/')[0];
        string fileName = Path.GetFileName(path);
        return first.Equals(".snapshots", StringComparison.OrdinalIgnoreCase) ||
               first.Equals(".undo", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals(JobLeaseFileStore.LeaseFileName, StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals(JobLeaseFileStore.GuardFileName, StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals(OurPlanPackageFormat.WorkspaceClaimFileName, StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals(OurPlanPackageFormat.WorkspaceMarkerFileName, StringComparison.OrdinalIgnoreCase) ||
               IsWorkspaceControlAtomicTemp(
                   fileName,
                   JobLeaseFileStore.LeaseFileName) ||
               IsWorkspaceControlAtomicTemp(
                   fileName,
                   JobLeaseFileStore.GuardFileName) ||
               IsWorkspaceControlAtomicTemp(
                   fileName,
                   OurPlanPackageFormat.WorkspaceClaimFileName) ||
               IsWorkspaceControlAtomicTemp(
                   fileName,
                   OurPlanPackageFormat.WorkspaceMarkerFileName) ||
               IsWorkspaceControlReplaceTemp(
                   fileName,
                   JobLeaseFileStore.LeaseFileName) ||
               IsWorkspaceControlReplaceTemp(
                   fileName,
                   JobLeaseFileStore.GuardFileName) ||
               IsWorkspaceControlReplaceTemp(
                   fileName,
                   OurPlanPackageFormat.WorkspaceClaimFileName) ||
               IsWorkspaceControlReplaceTemp(
                   fileName,
                   OurPlanPackageFormat.WorkspaceMarkerFileName);
    }

    private static bool IsWorkspaceControlAtomicTemp(string fileName, string targetFileName)
    {
        const string suffix = ".tmp";
        string prefix = $".{targetFileName}.";
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string token = fileName[prefix.Length..^suffix.Length];
        return Guid.TryParseExact(token, "N", out _);
    }

    private static bool IsWorkspaceControlReplaceTemp(string fileName, string targetFileName)
    {
        const string replacePrefix = "~RF";
        const string suffix = ".TMP";
        string prefix = targetFileName + replacePrefix;
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string token = fileName[prefix.Length..^suffix.Length];
        return token.Length > 0 && token.All(Uri.IsHexDigit);
    }

    private void StopPackageWorkspaceWatcher()
    {
        SupersedeAutomaticPackageCheckpoint();
        ResetAutomaticPackageCheckpointScheduler();
        CancellationTokenSource? artifactCheck = Interlocked.Exchange(
            ref _packageArtifactCheckCts,
            null);
        artifactCheck?.Cancel();
        artifactCheck?.Dispose();
        if (_packageWorkspaceWatcher != null)
        {
            _packageWorkspaceWatcher.EnableRaisingEvents = false;
            _packageWorkspaceWatcher.Dispose();
            _packageWorkspaceWatcher = null;
        }
        if (_packageArtifactWatcher != null)
        {
            _packageArtifactWatcher.EnableRaisingEvents = false;
            _packageArtifactWatcher.Dispose();
            _packageArtifactWatcher = null;
        }
    }

    private void CloseCurrentPackageWorkspaceSession()
    {
        StopPackageWorkspaceWatcher();
        if (_currentPackageSession != null)
            ClosePackageSessionMarker(_currentPackageSession);
    }

    private static void ClosePackageSessionMarker(OurPlanPackageSession session)
    {
        try
        {
            OurPlanPackageWorkspace.MarkSessionClosed(session);
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Could not close OurPlan workspace marker '{session.WorkspaceRoot}'.");
        }
    }

    private void StartPackageWorkspacePruneOnce()
    {
        if (Interlocked.Exchange(ref _packageWorkspacePruneStarted, 1) != 0)
            return;
        _ = Task.Run(() =>
        {
            try
            {
                int removed = OurPlanPackageWorkspace.PruneCleanClosedWorkspaces(TimeSpan.FromDays(7));
                if (removed > 0)
                    AppLog.Info($"Pruned {removed} clean stale OurPlan package workspace(s).");
            }
            catch (Exception ex)
            {
                AppLog.Warn(ex, "OurPlan package workspace pruning failed.");
            }
        });
    }

    private void StartPackageArtifactWatcher(OurPlanPackageSession session)
    {
        try
        {
            string? parent = Path.GetDirectoryName(session.PackagePath);
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
                return;
            var watcher = new FileSystemWatcher(parent, Path.GetFileName(session.PackagePath))
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = false,
            };
            watcher.Changed += PackageArtifactChanged;
            watcher.Created += PackageArtifactChanged;
            watcher.Deleted += PackageArtifactChanged;
            watcher.Renamed += PackageArtifactChanged;
            watcher.Error += PackageArtifactWatcherError;
            _packageArtifactWatcher = watcher;
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            AppLog.Warn(ex, "Could not watch the OurPlan project file for external changes.");
        }
    }

    private void PackageArtifactChanged(object sender, FileSystemEventArgs e)
    {
        if (!ReferenceEquals(sender, _packageArtifactWatcher) ||
            Volatile.Read(ref _packageSaveInProgress) > 0)
        {
            return;
        }

        OurPlanPackageSession? session = _currentPackageSession;
        if (session == null)
            return;
        long eventGeneration = Interlocked.Increment(ref _packageArtifactEventGeneration);
        OurPlanPackageFingerprint expectedBaseFingerprint = session.BaseFingerprint;
        var next = new CancellationTokenSource();
        CancellationTokenSource? previous = Interlocked.Exchange(
            ref _packageArtifactCheckCts,
            next);
        previous?.Cancel();
        previous?.Dispose();
        _ = InspectPackageArtifactAfterQuietPeriodAsync(
            session,
            expectedBaseFingerprint,
            eventGeneration,
            next);
    }

    private async Task InspectPackageArtifactAfterQuietPeriodAsync(
        OurPlanPackageSession session,
        OurPlanPackageFingerprint expectedBaseFingerprint,
        long eventGeneration,
        CancellationTokenSource owner)
    {
        CancellationToken token = owner.Token;
        try
        {
            await Task.Delay(750, token).ConfigureAwait(false);
            await _packageArtifactInspectionGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                if (eventGeneration != Interlocked.Read(ref _packageArtifactEventGeneration))
                    return;
                ArtifactInspection inspection = await Task.Run(
                    () => InspectPackageArtifact(session, expectedBaseFingerprint)).ConfigureAwait(false);
                if (eventGeneration != Interlocked.Read(ref _packageArtifactEventGeneration))
                    return;
                await Dispatcher.InvokeAsync(() =>
                {
                    if (!ReferenceEquals(session, _currentPackageSession) ||
                        token.IsCancellationRequested ||
                        eventGeneration != Interlocked.Read(ref _packageArtifactEventGeneration) ||
                        Volatile.Read(ref _packageSaveInProgress) > 0 ||
                        session.BaseFingerprint != inspection.BaseFingerprintAtStart ||
                        !ArtifactInspectionStillCurrent(session.PackagePath, inspection))
                    {
                        return;
                    }

                    if (inspection.Status == ArtifactInspectionStatus.Equivalent)
                    {
                        bool recoveringFromConflict =
                            _packageSaveStatus.Equals("Save: Conflict", StringComparison.OrdinalIgnoreCase) ||
                            _packageSaveStatus.Equals("Save: Watch restarted", StringComparison.OrdinalIgnoreCase);
                        if (recoveringFromConflict)
                            _packageAutosaveBlocked = false;
                        try
                        {
                            OurPlanPackageWorkspace.AcceptEquivalentPackageFingerprint(
                                session,
                                inspection.Fingerprint);
                        }
                        catch (Exception ex)
                        {
                            AppLog.Warn(ex, "Could not record an equivalent cloud package fingerprint.");
                        }
                        if (!_packageAutosaveBlocked)
                        {
                            _packageSaveStatus = session.HasUnpackagedChanges
                                ? "Save: Pending"
                                : "Save: Saved";
                        }
                        if (recoveringFromConflict)
                        {
                            TxtStatus.Text = session.HasUnpackagedChanges
                                ? "The cloud project file is current; local changes will save automatically."
                                : "The cloud project file is current again.";
                        }
                        UpdateStatusBarSegments();
                        if (session.HasUnpackagedChanges)
                        {
                            ScheduleAutomaticPackageCheckpoint(
                                session,
                                waitForQuietPeriod: true,
                                retryDelay: TimeSpan.Zero);
                        }
                        return;
                    }

                    CancelScheduledAutomaticPackageCheckpoint();
                    _packageAutosaveBlocked = true;
                    _packageSaveStatus = "Save: Conflict";
                    TxtStatus.Text = inspection.Status switch
                    {
                        ArtifactInspectionStatus.Missing =>
                            "The .ourplan file is missing. Your local working copy is preserved.",
                        ArtifactInspectionStatus.Unreadable =>
                            "The .ourplan file is unreadable. Your local working copy is preserved.",
                        _ =>
                            "The .ourplan file changed outside this window. Your local working copy is preserved.",
                    };
                    UpdateStatusBarSegments();
                });
            }
            finally
            {
                _packageArtifactInspectionGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // A newer cloud-file event superseded this check.
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(
                    ref _packageArtifactCheckCts,
                    null,
                    owner), owner))
            {
                owner.Dispose();
            }
        }
    }

    private void PackageArtifactWatcherError(object sender, ErrorEventArgs e)
    {
        if (!ReferenceEquals(sender, _packageArtifactWatcher))
            return;
        Exception error = e.GetException();
        AppLog.Warn(error, "OurPlan package file watcher stopped or overflowed.");
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!ReferenceEquals(sender, _packageArtifactWatcher))
                return;
            _packageArtifactWatcher.EnableRaisingEvents = false;
            _packageArtifactWatcher.Dispose();
            _packageArtifactWatcher = null;
            if (_currentPackageSession != null)
                StartPackageArtifactWatcher(_currentPackageSession);
            _packageSaveStatus = "Save: Watch restarted";
            TxtStatus.Text = "Cloud file monitoring restarted; the next save will also verify the project revision.";
            UpdateStatusBarSegments();
        }));
    }

    private void CancelPendingPackageArtifactInspection()
    {
        CancellationTokenSource? pending = Interlocked.Exchange(
            ref _packageArtifactCheckCts,
            null);
        pending?.Cancel();
        pending?.Dispose();
    }

    private static ArtifactInspection InspectPackageArtifact(
        OurPlanPackageSession session,
        OurPlanPackageFingerprint expectedBaseFingerprint)
    {
        try
        {
            if (!File.Exists(session.PackagePath))
            {
                return new ArtifactInspection(
                    ArtifactInspectionStatus.Missing,
                    default,
                    HasFingerprint: false,
                    expectedBaseFingerprint);
            }
            OurPlanPackageFingerprint before = OurPlanPackageFingerprint.Read(session.PackagePath);
            OurPlanPackageManifest manifest = OurPlanPackageArchive.ReadManifest(
                session.PackagePath,
                verifyObjects: true);
            OurPlanPackageFingerprint after = OurPlanPackageFingerprint.Read(session.PackagePath);
            bool equivalent = before == after &&
                              OurPlanPackageWorkspace.ManifestMatchesSessionBase(session, manifest);
            return new ArtifactInspection(
                equivalent ? ArtifactInspectionStatus.Equivalent : ArtifactInspectionStatus.Divergent,
                after,
                HasFingerprint: true,
                expectedBaseFingerprint);
        }
        catch
        {
            if (!File.Exists(session.PackagePath))
            {
                return new ArtifactInspection(
                    ArtifactInspectionStatus.Missing,
                    default,
                    HasFingerprint: false,
                    expectedBaseFingerprint);
            }
            try
            {
                return new ArtifactInspection(
                    ArtifactInspectionStatus.Unreadable,
                    OurPlanPackageFingerprint.Read(session.PackagePath),
                    HasFingerprint: true,
                    expectedBaseFingerprint);
            }
            catch
            {
                return new ArtifactInspection(
                    ArtifactInspectionStatus.Unreadable,
                    default,
                    HasFingerprint: false,
                    expectedBaseFingerprint);
            }
        }
    }

    private static bool ArtifactInspectionStillCurrent(
        string packagePath,
        ArtifactInspection inspection)
    {
        try
        {
            if (inspection.Status == ArtifactInspectionStatus.Missing)
                return !File.Exists(packagePath);
            if (!File.Exists(packagePath))
                return false;
            return !inspection.HasFingerprint ||
                   OurPlanPackageFingerprint.Read(packagePath) == inspection.Fingerprint;
        }
        catch
        {
            return false;
        }
    }

    private readonly record struct ArtifactInspection(
        ArtifactInspectionStatus Status,
        OurPlanPackageFingerprint Fingerprint,
        bool HasFingerprint,
        OurPlanPackageFingerprint BaseFingerprintAtStart);

    private enum ArtifactInspectionStatus
    {
        Missing,
        Unreadable,
        Divergent,
        Equivalent,
    }
}
