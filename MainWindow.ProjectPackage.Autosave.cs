using System.IO;
using System.Windows.Threading;

namespace OurPlanCore;

public partial class MainWindow
{
    private DispatcherTimer? _packageAutosaveTimer;
    private OurPlanPackageSession? _packageAutosaveScheduledSession;
    private Task? _packageAutosaveTask;
    private DateTime _packageAutosaveDirtySinceUtc = DateTime.MinValue;
    private DateTime _packageAutosaveLastStartedUtc = DateTime.MinValue;
    private int _packageAutosaveFailureCount;
    private long _packageAutosaveScheduleEpoch;
    private bool _packageAutosaveBlocked;
    private bool _packageAutosaveWaitActive;

    private void QueueAutomaticPackageCheckpoint(OurPlanPackageSession session)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;
        long epoch = Interlocked.Read(ref _packageAutosaveScheduleEpoch);
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                if (epoch == Interlocked.Read(ref _packageAutosaveScheduleEpoch))
                {
                    ScheduleAutomaticPackageCheckpoint(
                        session,
                        waitForQuietPeriod: true,
                        retryDelay: TimeSpan.Zero);
                }
            }));
    }

    private void ScheduleAutomaticPackageCheckpoint(
        OurPlanPackageSession session,
        bool waitForQuietPeriod,
        TimeSpan retryDelay)
    {
        if (!Dispatcher.CheckAccess())
        {
            QueueAutomaticPackageCheckpoint(session);
            return;
        }
        if (!ReferenceEquals(session, _currentPackageSession) ||
            !HasCurrentPackageSession ||
            !session.HasUnpackagedChanges ||
            session.IsRecoverySession ||
            _packageAutosaveBlocked)
        {
            return;
        }

        DateTime nowUtc = DateTime.UtcNow;
        if (_packageAutosaveDirtySinceUtc == DateTime.MinValue)
            _packageAutosaveDirtySinceUtc = nowUtc;
        DateTime dueUtc = OurPlanPackageAutosaveSchedule.CalculateDueUtc(
            nowUtc,
            _packageAutosaveDirtySinceUtc,
            _packageAutosaveLastStartedUtc,
            waitForQuietPeriod,
            retryDelay);

        DispatcherTimer timer = EnsurePackageAutosaveTimer();
        timer.Stop();
        timer.Interval = TimeSpan.FromMilliseconds(Math.Max(
            250,
            (dueUtc - nowUtc).TotalMilliseconds));
        _packageAutosaveScheduledSession = session;
        timer.Start();
        TxtStatusSave.ToolTip =
            $"Saved in local recovery. This same .ourplan file will update automatically around " +
            $"{dueUtc.ToLocalTime():HH:mm:ss}.";
        UpdateStatusBarSegments();
    }

    private DispatcherTimer EnsurePackageAutosaveTimer()
    {
        if (_packageAutosaveTimer != null)
            return _packageAutosaveTimer;
        _packageAutosaveTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
        _packageAutosaveTimer.Tick += AutomaticPackageAutosaveTimer_Tick;
        return _packageAutosaveTimer;
    }

    private async void AutomaticPackageAutosaveTimer_Tick(object? sender, EventArgs e)
    {
        _packageAutosaveTimer?.Stop();
        OurPlanPackageSession? session = _packageAutosaveScheduledSession;
        if (session == null || !ReferenceEquals(session, _currentPackageSession))
            return;
        if (!session.HasUnpackagedChanges)
        {
            _packageAutosaveScheduledSession = null;
            _packageAutosaveDirtySinceUtc = DateTime.MinValue;
            return;
        }
        if (!CanRunAutomaticPackageCheckpoint(session, out bool retry))
        {
            if (retry)
            {
                ScheduleAutomaticPackageCheckpoint(
                    session,
                    waitForQuietPeriod: false,
                    OurPlanPackageAutosaveSchedule.BusyRetryDelay);
            }
            return;
        }

        Task checkpoint = RunAutomaticPackageCheckpointAsync(session);
        _packageAutosaveTask = checkpoint;
        try
        {
            await checkpoint;
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(session, _currentPackageSession))
                FailAutomaticPackageCheckpoint(session, ex);
            AppLog.Error(ex, "Unexpected automatic OurPlan checkpoint failure; automatic retry stopped.");
        }
        finally
        {
            if (ReferenceEquals(_packageAutosaveTask, checkpoint))
                _packageAutosaveTask = null;
        }
    }

    private bool CanRunAutomaticPackageCheckpoint(
        OurPlanPackageSession session,
        out bool retry)
    {
        retry = false;
        if (!ReferenceEquals(session, _currentPackageSession) ||
            !HasCurrentPackageSession ||
            !session.HasUnpackagedChanges)
        {
            return false;
        }
        if (session.IsRecoverySession ||
            _packageAutosaveBlocked ||
            !IsCurrentJobWritable)
        {
            return false;
        }
        try
        {
            if (!File.Exists(session.PackagePath) ||
                (File.GetAttributes(session.PackagePath) & FileAttributes.ReadOnly) != 0)
            {
                _packageAutosaveBlocked = true;
                _packageSaveStatus = "Save: Conflict";
                TxtStatusSave.ToolTip =
                    "Automatic save stopped because the original .ourplan file is missing or read-only. " +
                    "The complete working copy remains in local recovery.";
                UpdateStatusBarSegments();
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            retry = true;
            return false;
        }

        retry = Volatile.Read(ref _packageOperationActive) > 0 ||
                _closingPackageCheckpoint != null ||
                _busyOverlayDepth > 0 ||
                _takeoffSaveService.HasPending ||
                _takeoffSaveService.State is TakeoffSaveState.Saving or TakeoffSaveState.Failed ||
                _currentPageAnnotationsDirty ||
                _dirtyDetachedPageAnnotations.Count > 0 ||
                _aiContextMaintenanceTask is { IsCompleted: false } ||
                JobFileWriteActivity.HasActiveBackgroundWriters ||
                JobFileWriteActivity.HasActivePackageCheckpoints ||
                !CanUpdatePackageArtifact(session.PackagePath);
        return !retry;
    }

    private async Task RunAutomaticPackageCheckpointAsync(OurPlanPackageSession session)
    {
        if (Interlocked.CompareExchange(ref _packageOperationActive, 1, 0) != 0)
        {
            ScheduleAutomaticPackageCheckpoint(
                session,
                waitForQuietPeriod: false,
                OurPlanPackageAutosaveSchedule.BusyRetryDelay);
            return;
        }

        long generation = Interlocked.Read(ref _packageWorkspaceGeneration);
        _packageAutosaveLastStartedUtc = DateTime.UtcNow;
        Interlocked.Increment(ref _packageSaveInProgress);
        try
        {
            CancelPendingPackageArtifactInspection();
            _packageSaveStatus = "Save: Auto packing...";
            TxtStatusSave.ToolTip = $"Automatically updating {session.PackagePath}";
            UpdateStatusBarSegments();

            OurPlanPackageSaveResult result = await Task.Run(() =>
                OurPlanPackageWriter.Save(session));
            if (!ReferenceEquals(session, _currentPackageSession))
                return;

            bool stable = generation == Interlocked.Read(ref _packageWorkspaceGeneration) &&
                          !session.HasUnpackagedChanges &&
                          PackageArtifactStillMatchesSession(session);
            if (!stable)
            {
                session.HasUnpackagedChanges = true;
                _packageSaveStatus = "Save: Pending";
                ScheduleAutomaticPackageCheckpoint(
                    session,
                    waitForQuietPeriod: true,
                    retryDelay: TimeSpan.Zero);
                return;
            }

            CompleteAutomaticPackageCheckpoint(session, result);
        }
        catch (OurPlanPackageConflictException ex)
        {
            if (!ReferenceEquals(session, _currentPackageSession))
                return;
            session.HasUnpackagedChanges = true;
            _packageAutosaveTimer?.Stop();
            _packageAutosaveScheduledSession = null;
            _packageAutosaveBlocked = true;
            _packageSaveStatus = "Save: Conflict";
            TxtStatusSave.ToolTip =
                "Automatic save stopped because the .ourplan file changed elsewhere. " +
                "The complete local working copy is preserved.";
            UpdateStatusBarSegments();
            AppLog.Warn(ex, "Automatic OurPlan package checkpoint found an external conflict.");
        }
        catch (Exception ex) when (ShouldRetryAutomaticPackageCheckpoint(ex))
        {
            if (!ReferenceEquals(session, _currentPackageSession))
                return;
            RetryAutomaticPackageCheckpoint(session, ex.Message);
            AppLog.Warn(ex, "Automatic OurPlan package checkpoint will retry.");
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(session, _currentPackageSession))
                return;
            FailAutomaticPackageCheckpoint(session, ex);
            AppLog.Error(ex, "Automatic OurPlan package checkpoint stopped after a permanent failure.");
        }
        finally
        {
            Interlocked.Decrement(ref _packageSaveInProgress);
            Interlocked.Exchange(ref _packageOperationActive, 0);
        }
    }

    private void CompleteAutomaticPackageCheckpoint(
        OurPlanPackageSession session,
        OurPlanPackageSaveResult result)
    {
        _packageAutosaveTimer?.Stop();
        _packageAutosaveScheduledSession = null;
        _packageAutosaveDirtySinceUtc = DateTime.MinValue;
        _packageAutosaveFailureCount = 0;
        _packageAutosaveBlocked = false;
        _packageSaveStatus = $"Save: Saved {DateTime.Now:HH:mm:ss}";
        TxtStatusSave.ToolTip = session.PackagePath;
        TxtStatus.Text =
            $"Automatically saved to {Path.GetFileName(result.PackagePath)} " +
            $"({result.LogicalFileCount:N0} files, {FormatPackageBytes(result.PackageBytes)}).";
        PersistCurrentDocumentIdentity();
        UpdateStatusBarSegments();
        AppLog.Info(
            $"OurPlan package saved during automatic save: '{result.PackagePath}', " +
            $"revision={result.RevisionId}, logicalFiles={result.LogicalFileCount}, " +
            $"uniqueObjects={result.UniqueObjectCount}, bytes={result.PackageBytes}.");
    }

    private void RetryAutomaticPackageCheckpoint(OurPlanPackageSession session, string detail)
    {
        session.HasUnpackagedChanges = true;
        _packageAutosaveFailureCount++;
        TimeSpan delay = OurPlanPackageAutosaveSchedule.FailureRetryDelay(
            _packageAutosaveFailureCount);
        _packageSaveStatus = "Save: Retrying";
        TxtStatusSave.ToolTip =
            $"The complete project is saved in local recovery. Automatic same-file save will retry " +
            $"in {delay.TotalSeconds:0} seconds. {detail}";
        UpdateStatusBarSegments();
        ScheduleAutomaticPackageCheckpoint(
            session,
            waitForQuietPeriod: false,
            retryDelay: delay);
    }

    private void FailAutomaticPackageCheckpoint(
        OurPlanPackageSession session,
        Exception failure)
    {
        session.HasUnpackagedChanges = true;
        _packageAutosaveTimer?.Stop();
        _packageAutosaveScheduledSession = null;
        _packageAutosaveBlocked = true;
        _packageSaveStatus = "Save: Failed";
        TxtStatusSave.ToolTip =
            "Automatic same-file save stopped after a non-transient error. " +
            $"The complete project remains in local recovery. Press Ctrl+S to retry. {failure.Message}";
        UpdateStatusBarSegments();
    }

    internal static bool ShouldRetryAutomaticPackageCheckpoint(Exception failure)
    {
        for (Exception? current = failure; current != null; current = current.InnerException)
        {
            if (current is OurPlanPackageValidationException or OurPlanPackageConflictException)
                return false;
            if (current is OurPlanPackageTransientException)
                return true;
            if (current is IOException io && OurPlanPackageWriter.IsTransientReplaceFailure(io))
                return true;
        }
        return false;
    }

    internal static void PromoteRecoveredPackageSessionAfterSamePathSave(
        OurPlanPackageSession session)
    {
        if (!session.IsRecoverySession)
            return;
        session.IsRecoverySession = false;
        session.RecoveryReason = "";
        session.AvailableRecoverySessions = [];
    }

    private void SupersedeAutomaticPackageCheckpoint()
    {
        Interlocked.Increment(ref _packageAutosaveScheduleEpoch);
        CancelScheduledAutomaticPackageCheckpoint();
        if (_packageAutosaveWaitActive)
            return;

        _packageAutosaveWaitActive = true;
        bool disableWindow = _packageAutosaveTask is { IsCompleted: false } && IsEnabled;
        if (disableWindow)
            IsEnabled = false;
        try
        {
            WaitForAutomaticPackageCheckpoint();
        }
        finally
        {
            Interlocked.Increment(ref _packageAutosaveScheduleEpoch);
            CancelScheduledAutomaticPackageCheckpoint();
            if (disableWindow && !Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                IsEnabled = true;
            _packageAutosaveWaitActive = false;
        }
    }

    private void CancelScheduledAutomaticPackageCheckpoint()
    {
        if (!Dispatcher.CheckAccess())
            return;
        _packageAutosaveTimer?.Stop();
        _packageAutosaveScheduledSession = null;
    }

    private void WaitForAutomaticPackageCheckpoint()
    {
        if (!Dispatcher.CheckAccess())
            return;
        Task? checkpoint = _packageAutosaveTask;
        if (checkpoint == null)
            return;
        if (!checkpoint.IsCompleted)
        {
            var frame = new DispatcherFrame();
            _ = checkpoint.ContinueWith(
                _ => Dispatcher.BeginInvoke(
                    DispatcherPriority.Send,
                    new Action(() => frame.Continue = false)),
                TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        try
        {
            checkpoint.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, "Automatic package checkpoint ended while a foreground save was waiting.");
        }
    }

    private void ResetAutomaticPackageCheckpointScheduler()
    {
        CancelScheduledAutomaticPackageCheckpoint();
        _packageAutosaveDirtySinceUtc = DateTime.MinValue;
        _packageAutosaveLastStartedUtc = DateTime.MinValue;
        _packageAutosaveFailureCount = 0;
        _packageAutosaveBlocked = false;
    }
}
