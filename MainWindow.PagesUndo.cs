using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OurPlanCore;

public partial class MainWindow
{
    private PageDeleteUndoBatch? _lastPageDeleteUndo;

    private PageDeleteUndoBatch MovePageEntriesToUndoTrash(IReadOnlyList<PagesClipboardEntry> entries)
    {
        if (_currentJob == null)
            throw new InvalidOperationException("Open a job before deleting Pages items.");
        if (!EnsureCurrentJobWritable("delete pages or folders"))
            throw new InvalidOperationException("Pages deletion is unavailable while the job is read-only.");

        FlushTakeoffAutosaves();
        SaveCurrentPageScale();
        SaveCurrentPageAnnotations();

        return PageDeleteUndoService.MoveToTrash(
            _currentJob,
            entries.Select(entry => new PageDeleteUndoRequest(entry.SourcePath, entry.IsPage)).ToList());
    }

    private void RememberLastPageDelete(PageDeleteUndoBatch batch)
    {
        if (batch.Entries.Count == 0)
            return;

        PageDeleteUndoBatch? previous = _lastPageDeleteUndo;
        _lastPageDeleteUndo = batch;
        if (previous == null || string.Equals(previous.TrashRoot, batch.TrashRoot, StringComparison.OrdinalIgnoreCase))
            return;

        RetirePageDeleteUndo(previous);
    }

    private bool CanUndoLastPageDelete()
    {
        if (_currentJob == null || _lastPageDeleteUndo == null || !IsCurrentJobWritable)
            return false;
        if (!string.Equals(
                NormalizePath(_currentJob.RootPath),
                NormalizePath(_lastPageDeleteUndo.JobRoot),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return _lastPageDeleteUndo.Entries.Count > 0 &&
               _lastPageDeleteUndo.Entries.All(entry => Directory.Exists(entry.TrashPath));
    }

    private string UndoLastPageDeleteMenuLabel() =>
        CanUndoLastPageDelete() && _lastPageDeleteUndo != null
            ? $"Undo Delete: {_lastPageDeleteUndo.StatusName}"
            : "Undo Last Page Delete";

    private bool TryUndoLastPageDelete()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Nothing to restore: no job is open.";
            return false;
        }
        if (!EnsureCurrentJobWritable("undo a Pages deletion"))
            return true;
        if (_lastPageDeleteUndo == null)
        {
            TxtStatus.Text = "Nothing to restore in Pages.";
            return false;
        }
        if (!string.Equals(
                NormalizePath(_lastPageDeleteUndo.JobRoot),
                NormalizePath(_currentJob.RootPath),
                StringComparison.OrdinalIgnoreCase))
        {
            TxtStatus.Text = "The last Pages delete belongs to another job.";
            return true;
        }

        try
        {
            IReadOnlyList<PageDeleteRestoreEntry> restored =
                PageDeleteUndoService.Restore(_currentJob, _lastPageDeleteUndo);
            if (restored.Count == 0)
            {
                TxtStatus.Text = "Nothing to restore in Pages.";
                return false;
            }

            _lastPageDeleteUndo = null;
            RefreshAfterPageDeleteUndo(restored);
            TxtStatus.Text = restored.Count == 1
                ? $"Restored: {OurPlanCoreJobStore.DisplayName(restored[0].RestoredPath)}."
                : $"Restored {restored.Count} page/folder items.";
            return true;
        }
        catch (Exception ex)
        {
            ShowOperationError("Undo Page Delete", ex);
            return true;
        }
    }

    private void RefreshAfterPageDeleteUndo(IReadOnlyList<PageDeleteRestoreEntry> restored)
    {
        var movedPaths = restored
            .Where(entry => !string.Equals(entry.OriginalPath, entry.RestoredPath, StringComparison.OrdinalIgnoreCase))
            .Select(entry => (entry.OriginalPath, entry.RestoredPath))
            .ToList();
        if (movedPaths.Count > 0)
            UpdatePageReferencesForMovedPaths(movedPaths);

        List<string> restoredPaths = restored.Select(entry => entry.RestoredPath).ToList();
        _pagesMultiSelection.Clear();
        ReloadPagesTree(restoredPaths[0]);
        foreach (string restoredPath in restoredPaths)
            _pagesMultiSelection.Add(restoredPath);
        ApplyPagesMultiSelectionVisuals();

        PageInfo? pageToOpen = restored
            .Where(entry => entry.IsPage)
            .Select(entry => OurPlanCoreJobStore.TryReadPage(entry.RestoredPath))
            .FirstOrDefault(page => page != null);
        pageToOpen ??= restoredPaths.SelectMany(CollectPagesUnder).FirstOrDefault();
        if (pageToOpen != null)
            SelectPageByFolder(pageToOpen.FolderPath);

        RefreshPagesTakeoffIndicators();
        ApplyTakeoffPageHighlights();
        RefreshSheetLegend();
        RefreshEstimateTable();
        UpdateTotalDisplay();
    }

    private void CloseDetachedSheetsForDeletedPages(IEnumerable<PagesClipboardEntry> entries)
    {
        List<string> deletedRoots = entries.Select(entry => entry.SourcePath).ToList();
        foreach (var window in _detachedSheetWindows.ToList())
        {
            if (deletedRoots.Any(root =>
                    OurPlanCoreJobStore.IsSameOrDescendant(root, window.Page.FolderPath)))
            {
                window.Close();
            }
        }
    }

    private void FinalizeLastPageDeleteUndo()
    {
        if (_lastPageDeleteUndo == null)
            return;

        PageDeleteUndoBatch batch = _lastPageDeleteUndo;
        _lastPageDeleteUndo = null;
        RetirePageDeleteUndo(batch);
    }

    private static void RetirePageDeleteUndo(PageDeleteUndoBatch batch)
    {
        if (string.IsNullOrWhiteSpace(batch.TrashRoot) || !Directory.Exists(batch.TrashRoot))
            return;

        try
        {
            DeleteDirectoryToRecycle(batch.TrashRoot);
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Could not retire Pages undo data: {batch.TrashRoot}");
        }
    }
}
