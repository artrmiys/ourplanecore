using System.IO;
using System.Threading;
using System.Windows;
using OurPlanCore.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    private JobLeaseService? _currentJobLeaseService;
    private JobAccessSessionToken _currentJobAccessToken;
    private JobAccessMode _currentJobAccessMode = JobAccessMode.Closed;

    private bool IsCurrentJobWritable =>
        _currentJob != null &&
        _currentJobAccessMode == JobAccessMode.Writable &&
        JobWriteAccess.GetMode(_currentJob.RootPath) == JobAccessMode.Writable;

    private bool IsCurrentJobReadOnly =>
        _currentJob != null && !IsCurrentJobWritable;

    private sealed class PendingJobAccess : IDisposable
    {
        public PendingJobAccess(
            string jobRoot,
            JobAccessMode mode,
            JobAccessSessionToken token,
            JobLeaseService? leaseService)
        {
            JobRoot = jobRoot;
            Mode = mode;
            Token = token;
            LeaseService = leaseService;
        }

        public string JobRoot { get; }
        public JobAccessMode Mode { get; }
        public JobAccessSessionToken Token { get; }
        public JobLeaseService? LeaseService { get; }
        public bool IsCommitted { get; private set; }

        public bool IsWriteAccessCurrent =>
            Mode != JobAccessMode.Writable ||
            (LeaseService?.HasLease == true &&
             JobWriteAccess.GetMode(JobRoot) == JobAccessMode.Writable);

        public void Commit() => IsCommitted = true;

        public void Dispose()
        {
            if (!IsCommitted)
                ReleaseJobAccess(Token, LeaseService, "abandon job open");
        }
    }

    private bool TryPrepareJobAccess(string rootPath, out PendingJobAccess? pending)
    {
        pending = null;
        string jobRoot;
        try
        {
            jobRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        }
        catch (Exception ex)
        {
            ShowJobOpenError("The selected job path is invalid.", ex.Message);
            return false;
        }

        if (!Directory.Exists(jobRoot))
        {
            ShowJobOpenError("The selected job folder does not exist.", jobRoot);
            return false;
        }

        var leaseService = JobLeaseService.CreateDefault();
        int busyRetries = 0;
        while (true)
        {
            JobLeaseAcquireResult result = leaseService.TryAcquire(jobRoot);
            if (result.Success)
                return TryRegisterPendingAccess(jobRoot, JobAccessMode.Writable, leaseService, out pending);

            if (result.Status == JobLeaseAcquireStatus.Busy && busyRetries++ < 3)
            {
                Thread.Sleep(60);
                continue;
            }

            if (result.Status == JobLeaseAcquireStatus.Conflict && result.Observation.Lease != null)
            {
                JobLeaseConflictAction action = ShowJobLeaseConflict(result.Observation);
                switch (action)
                {
                    case JobLeaseConflictAction.Retry:
                        busyRetries = 0;
                        continue;
                    case JobLeaseConflictAction.OpenReadOnly:
                        leaseService.Dispose();
                        return TryRegisterPendingAccess(jobRoot, JobAccessMode.ReadOnly, null, out pending);
                    case JobLeaseConflictAction.TakeOver:
                    {
                        JobLeaseAcquireResult takeover = leaseService.TryTakeOver(jobRoot, result.Observation);
                        if (takeover.Success)
                            return TryRegisterPendingAccess(jobRoot, JobAccessMode.Writable, leaseService, out pending);

                        TxtStatus.Text = takeover.Message;
                        busyRetries = 0;
                        continue;
                    }
                    default:
                        leaseService.Dispose();
                        return false;
                }
            }

            MessageBoxResult fallback = MessageBox.Show(
                this,
                $"OurPlanCore cannot safely acquire the job lease.\n\n{result.Message}\n\n" +
                "Yes = open read-only.\nNo = retry.\nCancel = keep the current job open.",
                "Job Lease Unavailable",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Cancel);
            if (fallback == MessageBoxResult.No)
            {
                busyRetries = 0;
                continue;
            }

            leaseService.Dispose();
            return fallback == MessageBoxResult.Yes &&
                   TryRegisterPendingAccess(jobRoot, JobAccessMode.ReadOnly, null, out pending);
        }
    }

    private bool TryRegisterPendingAccess(
        string jobRoot,
        JobAccessMode mode,
        JobLeaseService? leaseService,
        out PendingJobAccess? pending)
    {
        pending = null;
        try
        {
            JobAccessSessionToken token = JobWriteAccess.RegisterJob(jobRoot, mode);
            if (leaseService != null)
            {
                leaseService.OwnershipLost += args =>
                    HandleJobLeaseOwnershipLost(leaseService, token, jobRoot, args);
            }

            pending = new PendingJobAccess(jobRoot, mode, token, leaseService);
            return true;
        }
        catch (Exception ex)
        {
            leaseService?.Dispose();
            ShowJobOpenError("The job access boundary could not be installed.", ex.Message);
            return false;
        }
    }

    private JobLeaseConflictAction ShowJobLeaseConflict(JobLeaseObservation observation)
    {
        JobLeaseInfo lease = observation.Lease!;
        var dialog = new JobLeaseConflictDialog(new JobLeaseConflictDetails(
            observation.JobRoot,
            lease.OwnerDisplay,
            lease.Machine,
            lease.ProcessId,
            lease.ProcessStartedAtUtc,
            lease.HeartbeatAtUtc,
            lease.AppVersion,
            lease.InstanceId,
            observation.State == JobLeaseObservationState.Stale))
        {
            Owner = this,
        };
        dialog.ShowDialog();
        return dialog.SelectedAction;
    }

    private bool TryAdoptJobAccess(PendingJobAccess pending)
    {
        if (!pending.IsWriteAccessCurrent)
            return false;

        _currentJobAccessMode = pending.Mode;
        _currentJobAccessToken = pending.Token;
        _currentJobLeaseService = pending.LeaseService;
        pending.Commit();
        return true;
    }

    private bool ConfirmPendingJobAccess(PendingJobAccess pending)
    {
        if (pending.IsWriteAccessCurrent)
            return true;

        pending.Dispose();
        ShowJobOpenError(
            "Write ownership was lost while the job was loading.",
            "Retry, or open the job read-only.");
        return false;
    }

    private void HandleJobLeaseOwnershipLost(
        JobLeaseService leaseService,
        JobAccessSessionToken token,
        string jobRoot,
        JobLeaseOwnershipLostEventArgs args)
    {
        try
        {
            JobWriteAccess.SetMode(token, JobAccessMode.ReadOnly);
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Failed to close the write gate after lease loss for {jobRoot}.");
        }

        if (ReferenceEquals(_currentJobLeaseService, leaseService))
            _activeAiRequestCts?.Cancel();

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!ReferenceEquals(_currentJobLeaseService, leaseService) ||
                _currentJob == null ||
                !SameJobPath(_currentJob.RootPath, jobRoot))
            {
                return;
            }

            _currentJobAccessMode = JobAccessMode.ReadOnly;
            _takeoffSaveService.Stop();
            ApplyCurrentJobAccessToUi();
            string message = $"Write access lost. This job is now read-only. {args.Message}";
            TxtStatus.Text = message;
            AppLog.Warn(message);
            MessageBox.Show(
                this,
                message,
                "Job Switched to Read-Only",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }));
    }

    private void ApplyCurrentJobAccessToUi()
    {
        bool readOnly = IsCurrentJobReadOnly;
        _viewport.IsReadOnlyMode = readOnly;
        ApplySheetOverlayJobAccessState();
        foreach (DetachedSheetWindow window in _detachedSheetWindows.ToList())
            window.Viewport.IsReadOnlyMode = readOnly;
        ApplyThreeDReadOnlyState(readOnly);

        PagesTree.AllowDrop = !readOnly;
        TakeoffsTree.AllowDrop = !readOnly;
        BtnNewPageMenu.IsEnabled = !readOnly;
        BtnNewPageFolderDirect.IsEnabled = !readOnly;
        BtnPageSetupMenu.IsEnabled = !readOnly;
        BtnNewTakeoffItem.IsEnabled = !readOnly;
        BtnDeleteEmptyTakeoffs.IsEnabled = !readOnly;
        BtnTakeoffMoreMenu.IsEnabled = !readOnly;
        BtnTakeoffManagerAutoTree.IsEnabled = !readOnly;
        BtnTakeoffManagerFromPages.IsEnabled = !readOnly;
        if (_recordButton != null)
            _recordButton.IsEnabled = !readOnly;

        foreach ((string tool, System.Windows.Controls.RadioButton button) in _toolBtns)
            button.IsEnabled = !readOnly || tool is "pan" or "select";
        if (readOnly && _activeTool is not ("pan" or "select"))
            ApplyToolSelection("select");

        ApplyBookmarksJobAccessState();
        RefreshJobHeaderLabels();
    }

    private bool EnsureCurrentJobWritable(string operation, bool showDialog = true)
    {
        if (IsCurrentJobWritable)
            return true;

        string message = _currentJob == null
            ? $"Open a job before you {operation}."
            : $"'{_currentJob.Name}' is open read-only. You cannot {operation}.";
        TxtStatus.Text = message;
        if (showDialog)
        {
            MessageBox.Show(
                this,
                message,
                "Read-Only Job",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        return false;
    }

    private bool IsExpectedJobWritable(OurPlanCoreJob expectedJob) =>
        _currentJob != null &&
        ReferenceEquals(_currentJob, expectedJob) &&
        SameJobPath(_currentJob.RootPath, expectedJob.RootPath) &&
        JobWriteAccess.GetMode(expectedJob.RootPath) == JobAccessMode.Writable;

    private bool EnsureExpectedJobWritable(
        OurPlanCoreJob expectedJob,
        string operation,
        bool showDialog = false)
    {
        if (IsExpectedJobWritable(expectedJob))
            return true;

        string message = $"Cannot {operation}: the originating job changed or is now read-only.";
        TxtStatus.Text = message;
        if (showDialog)
        {
            MessageBox.Show(
                this,
                message,
                "Job Access Changed",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        return false;
    }

    private string CurrentJobDisplayName()
    {
        if (_currentJob == null)
            return "No job";
        string name = HasCurrentPackageSession
            ? $"{_currentJob.Name} [.ourplan]"
            : _currentJob.Name;
        return IsCurrentJobReadOnly ? $"{name} [READ-ONLY]" : name;
    }

    private string CurrentJobWindowTitle() =>
        _currentJob == null
            ? $"OurPlanCore {AppVersion.Display}"
            : $"OurPlanCore {AppVersion.Display} — {CurrentJobDisplayName()}";

    private void CloseCurrentJobAccess()
    {
        JobAccessSessionToken token = _currentJobAccessToken;
        JobLeaseService? leaseService = _currentJobLeaseService;
        _currentJobAccessToken = default;
        _currentJobLeaseService = null;
        _currentJobAccessMode = JobAccessMode.Closed;
        ReleaseJobAccess(token, leaseService, "close job");
    }

    private static void ReleaseJobAccess(
        JobAccessSessionToken token,
        JobLeaseService? leaseService,
        string reason)
    {
        if (!token.IsEmpty)
        {
            try
            {
                JobWriteAccess.Close(token);
            }
            catch (Exception ex)
            {
                AppLog.Warn(ex, $"Failed to close job write gate during {reason}.");
            }
        }

        if (leaseService == null)
            return;

        try
        {
            leaseService.StopHeartbeat();
            JobLeaseOperationResult release = leaseService.TryRelease();
            if (!release.Success)
                AppLog.Warn($"Job lease release during {reason}: {release.Message}");
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Failed to release job lease during {reason}.");
        }
        finally
        {
            leaseService.Dispose();
        }
    }

    private static bool SameJobPath(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void ShowJobOpenError(string summary, string details)
    {
        TxtStatus.Text = $"{summary} {details}";
        MessageBox.Show(
            this,
            $"{summary}\n\n{details}",
            "Job Open Failed",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
