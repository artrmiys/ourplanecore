using System;
using System.IO;
using System.Windows;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void PrepareCurrentJobForSwitch()
    {
        if (_currentJob == null)
            return;

        try
        {
            FlushTakeoffAutosaves();
            SaveCurrentPageScale();
            SaveCurrentPageAnnotations();
            SaveJobRecoverySnapshot("before_switch");
            JobRecoveryService.ClearLock(_currentJob);
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Job switch snapshot skipped: {ex.Message}";
        }
    }

    private void HandleOpenedJobRecovery()
    {
        if (_currentJob == null)
            return;

        try
        {
            if (JobRecoveryService.TryReadLock(_currentJob, out JobRecoveryLockInfo info) &&
                JobRecoveryService.IsStaleLock(info))
            {
                var result = MessageBox.Show(
                    "This job has a recovery marker from a previous session. Create a metadata snapshot before continuing?",
                    "Job Recovery",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                    SaveJobRecoverySnapshot("recovery_marker");
            }

            JobRecoveryService.WriteLock(_currentJob);
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Recovery marker skipped: {ex.Message}";
        }
    }

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
            JobRecoveryService.ClearLock(_currentJob);
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
