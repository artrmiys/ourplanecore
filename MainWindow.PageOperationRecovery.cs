using System.IO;

namespace OurPlanCore;

public partial class MainWindow
{
    private JobOperationJournal BeginPageSort(string label) => BeginPageOperation(label, "page-sort");

    private JobOperationJournal BeginPageOperation(string label, string kind = "operation")
    {
        if (_currentJob == null) throw new InvalidOperationException("Open a project first.");
        if (!_takeoffSaveService.Flush().Success)
            throw new IOException("Pending takeoff changes must be saved before a bulk page operation.");
        SaveCurrentPageAnnotations();
        string root = _currentJob.RootPath;
        JobOperationJournal operation = JobOperationJournal.Begin(root, label, kind);
        operation.AfterRollback = () =>
        {
            _takeoffSaveService.DiscardAllPending("bulk operation rolled back to its saved starting state");
            _currentPageAnnotationsDirty = false;
            _dirtyDetachedPageAnnotations.Clear();
            CloseDetachedSheetsForModuleDisable();
            Dispatcher.BeginInvoke(new Action(() => OpenJob(root, currentJobPrepared: true)));
        };
        return operation;
    }

    private void UndoLastPageOperation(string? kind = null)
    {
        if (_currentJob == null || !EnsureCurrentJobWritable("undo a page operation")) return;
        try
        {
            if (!_takeoffSaveService.Flush().Success)
                throw new IOException("Save pending takeoff edits before undoing a page operation.");
            string root = _currentJob.RootPath;
            SaveCurrentPageAnnotations();
            string label = JobOperationJournal.UndoLast(root, kind);
            CloseDetachedSheetsForModuleDisable();
            OpenJob(root, currentJobPrepared: true);
            PostStatusInfo("Restored project state before: " + label);
        }
        catch (Exception ex) { ShowOperationError("Undo Page Operation", ex); }
    }
}
