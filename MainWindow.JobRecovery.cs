using System;
using System.Windows;
using Microsoft.Win32;

namespace OurPlanCore;

public partial class MainWindow
{
    private bool PrepareCurrentJobForSwitch()
    {
        if (_currentJob == null)
            return true;

        SupersedeAutomaticPackageCheckpoint();
        if (!EnsureNoActiveJobFileWriters("switch projects"))
            return false;

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
            if (HasCurrentPackageSession)
                _currentPackageSession!.HasUnpackagedChanges = true;
            if (!TrySaveCurrentPackage("switch jobs", showDialog: false) &&
                !ResolveFailedPackageCheckpointBeforeExit("switch jobs"))
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

    private bool PrepareCurrentJobForPackageCopy()
    {
        if (_currentJob == null || !EnsureNoActiveJobFileWriters("save a project copy"))
            return false;

        if (IsCurrentJobReadOnly)
        {
            if (!EnsureNoUnsavedReadOnlyPageAnnotations("save a project copy"))
                return false;
            if (!HasCurrentPackageSession)
                return false;
            _takeoffSaveService.Stop();
            if (!_takeoffSaveService.HasPending)
                return true;

            return MessageBox.Show(
                       this,
                       "The working folder became read-only and still has in-memory takeoff changes. " +
                       "Those pending changes cannot be written into the project copy; all files already present " +
                       "in the preserved local workspace will be included.\n\nContinue with Save As?",
                       "Save Preserved Working Copy",
                       MessageBoxButton.YesNo,
                       MessageBoxImage.Warning,
                       MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        if (!TryFlushTakeoffAutosaves("save a project copy"))
            return false;
        try
        {
            SaveCurrentPageScale();
            SaveCurrentPageAnnotations();
            return TryFlushTakeoffAutosaves("save a project copy after page state");
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, "Could not prepare the current project for Save As.");
            MessageBox.Show(
                this,
                $"The current project state could not be prepared safely.\n\n{ex.Message}",
                "Save As Canceled",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private bool EnsureNoActiveJobFileWriters(string operation)
    {
        if (_aiContextMaintenanceTask is { IsCompleted: false })
        {
            TxtStatus.Text = "AI project maintenance is still finishing. Retry shortly.";
            MessageBox.Show(
                this,
                $"AI project maintenance is still writing files. Wait for it to finish, then {operation} again.",
                "Project Maintenance Still Running",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        if (JobFileWriteActivity.HasActiveBackgroundWriters)
        {
            TxtStatus.Text = "Background project maintenance is still finishing. Retry shortly.";
            MessageBox.Show(
                this,
                $"Background project maintenance is still writing files. Wait for it to finish, then {operation} again.",
                "Project Maintenance Still Running",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        if (_busyOverlayDepth > 0)
        {
            _activeAiRequestCts?.Cancel();
            _materialsWorkCts?.Cancel();
            TxtStatus.Text = "Background project work is stopping. Retry when the progress overlay closes.";
            MessageBox.Show(
                this,
                $"A project operation is still running. It must finish or stop before the final project checkpoint can be created.\n\n" +
                $"Wait for the progress overlay to close, then {operation} again.",
                "Project Operation Still Running",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        if (_sheetManagerRasterPrepareCts == null)
        {
            if (_projectStorageAnalysisCancellation == null)
                return true;

            _projectStorageAnalysisCancellation.Cancel();
            TxtStatus.Text = "Project storage work is stopping. Retry after it finishes.";
            MessageBox.Show(
                this,
                $"Project storage analysis or compaction is still active. It is being stopped so the project can be saved safely.\n\n" +
                $"Wait for the storage status to finish, then {operation} again.",
                "Project Save Waiting for Storage Work",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        _sheetManagerRasterPrepareCts.Cancel();
        TxtStatus.Text = "Raster preparation is stopping. Retry after it finishes.";
        MessageBox.Show(
            this,
            $"Raster preparation is still writing project cache files. It is being stopped so the project can be saved safely.\n\n" +
            $"Wait for the raster progress to finish, then {operation} again.",
            "Project Save Waiting for Raster Work",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
    }

    private bool PrepareReadOnlyJobForExit(string operation)
    {
        if (!EnsureNoUnsavedReadOnlyPageAnnotations(operation))
            return false;

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

        if (!SaveDirtyReadOnlyPackageBeforeExit(operation))
            return false;

        CloseDetachedSheetsForModuleDisable();
        return true;
    }

    private bool EnsureNoUnsavedReadOnlyPageAnnotations(string operation)
    {
        if (!_currentPageAnnotationsDirty && _dirtyDetachedPageAnnotations.Count == 0)
            return true;

        int detachedCount = _dirtyDetachedPageAnnotations.Count;
        string locations = _currentPageAnnotationsDirty && detachedCount > 0
            ? $"the main sheet and {detachedCount} detached sheet window(s)"
            : _currentPageAnnotationsDirty
                ? "the main sheet"
                : $"{detachedCount} detached sheet window(s)";
        TxtStatus.Text = $"Cannot {operation}: unsaved annotations remain in {locations}.";
        MessageBox.Show(
            this,
            $"This project became read-only while annotation changes were still pending in {locations}.\n\n" +
            $"OurPlanCore will not discard them or create an incomplete project copy. Restore write access, then retry {operation}.",
            "Unsaved Read-Only Annotations",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    private bool SaveDirtyReadOnlyPackageBeforeExit(string operation)
    {
        if (!HasCurrentPackageSession || !_currentPackageSession!.HasUnpackagedChanges || _currentJob == null)
            return true;

        MessageBoxResult choice = MessageBox.Show(
            this,
            "This project became read-only after local changes were written to its private working copy. " +
            "Those changes are not in the .ourplan file yet.\n\n" +
            "Yes = save the complete working copy as a new .ourplan file.\n" +
            "No = leave it in local recovery and continue.\n" +
            "Cancel = keep the project open.",
            "Unpackaged OurPlan Changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Yes);
        if (choice == MessageBoxResult.Cancel)
            return false;
        if (choice == MessageBoxResult.No)
        {
            AppLog.Warn($"User chose local recovery instead of packaging read-only changes before {operation}.");
            return true;
        }

        return TrySaveCurrentWorkspaceAsNewPackage(operation);
    }

    private bool ResolveFailedPackageCheckpointBeforeExit(string operation)
    {
        if (!HasCurrentPackageSession || _currentJob == null)
            return false;

        string continueAction = operation.StartsWith("close", StringComparison.OrdinalIgnoreCase)
            ? "close the app"
            : "continue";
        MessageBoxResult choice = MessageBox.Show(
            this,
            "The current .ourplan file could not be updated, but the complete working copy is preserved locally.\n\n" +
            "Yes = retry the same .ourplan file now.\n" +
            $"No = leave it in local recovery and {continueAction}.\n" +
            "Cancel = keep this project open.",
            "OurPlan Checkpoint Not Written",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Yes);
        if (choice == MessageBoxResult.Cancel)
            return false;
        if (choice == MessageBoxResult.No)
        {
            _currentPackageSession!.HasUnpackagedChanges = true;
            AppLog.Warn($"User chose local recovery after package checkpoint failure before {operation}.");
            return true;
        }

        return TrySaveCurrentPackage(operation, showDialog: false);
    }

    private bool TrySaveCurrentWorkspaceAsNewPackage(string operation)
    {
        if (_currentJob == null || _currentPackageSession == null)
            return false;
        SaveFileDialog dialog = CreateOurPlanSaveDialog(_currentJob.Name);
        if (dialog.ShowDialog(this) != true)
            return false;

        OurPlanPackageSession previous = _currentPackageSession;
        try
        {
            OurPlanPackageSession saved = SavePackageWorkspaceAs(_currentJob, previous, dialog.FileName);
            previous.DirtyStateChanged = null;
            previous.MarkerSessionOpen = false;
            _currentPackageSession = saved;
            StartPackageWorkspaceWatcher(saved);
            _packageSaveStatus = $"Save: Saved {DateTime.Now:HH:mm:ss}";
            TxtStatusSave.ToolTip = saved.PackagePath;
            PersistCurrentDocumentIdentity();
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, $"Could not save read-only package changes before {operation}.");
            MessageBox.Show(
                this,
                $"The new .ourplan file could not be created. The local recovery is still preserved.\n\n{ex.Message}",
                "Save Preserved Project Copy",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private string? SaveJobRecoverySnapshot(string reason)
    {
        if (_currentJob == null || !IsCurrentJobWritable)
            return null;

        return JobRecoveryService.SaveSnapshot(_currentJob, reason);
    }
}
