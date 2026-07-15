using System;
using System.Windows;

namespace OurPlanCore;

public partial class MainWindow
{
    private bool PrepareCurrentJobForSwitch()
    {
        if (_currentJob == null)
            return true;

        if (IsCurrentJobReadOnly)
            return PrepareReadOnlyJobForExit("switch jobs");

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

    private bool PrepareReadOnlyJobForExit(string operation)
    {
        _takeoffSaveService.Stop();
        if (_takeoffSaveService.HasPending)
        {
            MessageBoxResult choice = MessageBox.Show(
                this,
                "This job is read-only and has in-memory takeoff changes that cannot be saved.\n\n" +
                $"Discard those pending changes and {operation}?",
                "Unsaved Read-Only Changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (choice != MessageBoxResult.Yes)
                return false;

            _takeoffSaveService.DiscardAllPending($"job became read-only before {operation}");
        }

        CloseDetachedSheetsForModuleDisable();
        return true;
    }

    private string? SaveJobRecoverySnapshot(string reason)
    {
        if (_currentJob == null || !IsCurrentJobWritable)
            return null;

        return JobRecoveryService.SaveSnapshot(_currentJob, reason);
    }
}
