using System;
using System.IO;
using System.Windows;

namespace OurPlanCore;

public partial class MainWindow
{
    private bool PrepareCurrentJobForSwitch()
    {
        if (_currentJob == null)
            return true;

        if (!TryFlushTakeoffAutosaves("switch jobs"))
            return false;

        try
        {
            SaveCurrentPageScale();
            SaveCurrentPageAnnotations();
            SaveJobRecoverySnapshot("before_switch");
            CloseDetachedSheetsForModuleDisable();
            if (!TryFlushTakeoffAutosaves("switch jobs after closing detached sheets"))
                return false;

            ClearJobRecoveryLock();
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, "Job switch preparation failed; current job remains open.");
            TxtStatus.Text = $"Job switch canceled: {ex.Message}";
            MessageBox.Show(
                $"The current job could not be saved safely. The job switch was canceled.\n\n{ex.Message}",
                "Job Switch Canceled",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private void HandleOpenedJobRecovery()
    {
        if (_currentJob == null)
            return;

        try
        {
            bool shouldWriteLock = true;
            if (JobRecoveryService.TryReadLock(_currentJob, out JobRecoveryLockInfo info))
            {
                if (JobRecoveryService.IsStaleLock(info))
                {
                    if (ShouldSuppressAutomatedRecoveryPrompt())
                    {
                        AppLog.Info($"Skipping stale recovery prompt during automation for process {info.ProcessId}.");
                    }
                    else
                    {
                        var result = MessageBox.Show(
                            "This job has a recovery marker from a previous session. Create a metadata snapshot before continuing?",
                            "Job Recovery",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);
                        if (result == MessageBoxResult.Yes)
                            SaveJobRecoverySnapshot("recovery_marker");
                    }
                }
                else if (!JobRecoveryService.IsCurrentProcessLock(info))
                {
                    shouldWriteLock = false;
                    TxtStatus.Text = $"Job is already open in process {info.ProcessId}; recovery marker preserved.";
                }
            }

            if (shouldWriteLock)
                JobRecoveryService.WriteLock(_currentJob);
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Recovery marker skipped: {ex.Message}";
        }
    }

    private void QueueOpenedJobRecovery()
    {
        if (_currentJob == null)
            return;

        string jobRoot = _currentJob.RootPath;
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (_currentJob == null ||
                    !string.Equals(_currentJob.RootPath, jobRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                HandleOpenedJobRecovery();
            }),
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private static bool ShouldSuppressAutomatedRecoveryPrompt() =>
        IsTruthyEnvironment(ViewportPageStressSmokeEnv) ||
        IsTruthyEnvironment(TakeoffsMoveSmokeEnv) ||
        IsTruthyEnvironment(GuideScreenshotCaptureEnv);

    private string? SaveJobRecoverySnapshot(string reason)
    {
        if (_currentJob == null)
            return null;

        string path = JobRecoveryService.SaveSnapshot(_currentJob, reason);
        return path;
    }

    private void ClearJobRecoveryLock()
    {
        if (_currentJob == null)
            return;

        try
        {
            JobRecoveryService.TryClearLockForCurrentProcess(_currentJob);
        }
        catch (IOException)
        {
            // Closing should not be blocked by a best-effort lock cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Closing should not be blocked by a best-effort lock cleanup.
        }
    }
}
