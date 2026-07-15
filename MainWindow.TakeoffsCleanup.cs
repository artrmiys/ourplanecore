using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace OurPlanCore;

public partial class MainWindow
{
    private void BtnDeleteEmptyTakeoffs_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
        {
            PostStatusInfo("Open or create a job before deleting empty takeoffs.");
            return;
        }

        if (IsTakeoffRecordActive())
        {
            PostStatusInfo("Stop Record before deleting empty takeoffs.");
            return;
        }

        try
        {
            HashSet<string> safePathsBeforeFlush = FindSafeEmptyTakeoffsInCurrentJob()
                .Select(item => NormalizePath(item.FolderPath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            FlushTakeoffAutosaves();
            IReadOnlyList<TakeoffItem> emptyItems = FindSafeEmptyTakeoffsInCurrentJob(safePathsBeforeFlush);
            if (emptyItems.Count == 0)
            {
                PostStatusInfo("No safely confirmed empty takeoff items found.");
                return;
            }

            if (!ConfirmDeleteEmptyTakeoffs(emptyItems))
                return;

            DeleteEmptyTakeoffItems(safePathsBeforeFlush);
        }
        catch (Exception ex)
        {
            ShowOperationError("Delete Empty Takeoffs", ex);
        }
    }

    private IReadOnlyList<TakeoffItem> FindSafeEmptyTakeoffsInCurrentJob()
    {
        if (_currentJob == null)
            return [];

        return TakeoffCleanupService.FindSafeItemsWithoutMeasurements(_takeoffItems)
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.FolderPath) &&
                Directory.Exists(item.FolderPath) &&
                OurPlanCoreJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, item.FolderPath))
            .ToList();
    }

    private IReadOnlyList<TakeoffItem> FindSafeEmptyTakeoffsInCurrentJob(
        IReadOnlySet<string> safePathsBeforeFlush) =>
        FindSafeEmptyTakeoffsInCurrentJob()
            .Where(item => safePathsBeforeFlush.Contains(NormalizePath(item.FolderPath)))
            .ToList();

    private bool ConfirmDeleteEmptyTakeoffs(IReadOnlyList<TakeoffItem> emptyItems)
    {
        if (_currentJob == null)
            return false;

        string preview = string.Join(
            Environment.NewLine,
            emptyItems.Take(12).Select(item =>
                $"- {Path.GetRelativePath(_currentJob.TakeoffsRoot, item.FolderPath)}"));
        if (emptyItems.Count > 12)
            preview += $"{Environment.NewLine}- ... and {emptyItems.Count - 12} more";

        MessageBoxResult result = MessageBox.Show(
            $"Delete {emptyItems.Count} empty takeoff item(s) from this job?\n\n" +
            "Only verified items with zero Count, Line, or Area records are included. " +
            "Folders, unreadable items, and multiline-linked items will stay.\n\n" +
            $"{preview}\n\nThe whole batch can be restored with Ctrl+Z in Takeoffs.",
            "Delete Empty Takeoffs",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }

    private void DeleteEmptyTakeoffItems(IReadOnlySet<string> safePathsBeforeFlush)
    {
        IReadOnlyList<TakeoffItem> verifiedItems = FindSafeEmptyTakeoffsInCurrentJob(safePathsBeforeFlush);
        var entries = verifiedItems
            .Select(item => new TakeoffsClipboardEntry(item.FolderPath, true))
            .ToList();
        TakeoffDeleteUndoBatch undoBatch = MoveTakeoffEntriesToUndoTrash(entries);
        if (undoBatch.Entries.Count == 0)
        {
            PostStatusInfo("No empty takeoff items were deleted.");
            return;
        }

        PushTakeoffDeleteUndo(undoBatch);
        ClearTakeoffsClipboardForDeletedEntries(entries);
        _takeoffsMultiSelection.Clear();
        _takeoffSectionMultiSelection.Clear();
        LoadTakeoffsForJob();
        RefreshPagesTakeoffIndicators();
        RefreshSheetLegend();
        RefreshEstimateTable();
        UpdateTotalDisplay();
        TxtStatus.Text = $"Deleted {undoBatch.Entries.Count} empty takeoff item(s). Press Ctrl+Z in Takeoffs to restore.";
    }

    private void ClearTakeoffsClipboardForDeletedEntries(IReadOnlyList<TakeoffsClipboardEntry> entries)
    {
        if (_takeoffsClipboard == null)
            return;

        bool containsDeletedPath = entries.Any(entry =>
            _takeoffsClipboard.Entries.Any(clipboardEntry =>
                OurPlanCoreJobStore.IsSameOrDescendant(entry.SourcePath, clipboardEntry.SourcePath)));
        if (containsDeletedPath)
            _takeoffsClipboard = null;
    }
}
