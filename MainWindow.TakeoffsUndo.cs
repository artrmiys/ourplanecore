using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace OurPlanCore;

public partial class MainWindow
{
    private const int MaxTakeoffDeleteUndoBatches = 20;
    private readonly List<TakeoffDeleteUndoBatch> _takeoffDeleteUndoStack = [];

    private TakeoffDeleteUndoBatch MoveTakeoffEntriesToUndoTrash(IReadOnlyList<TakeoffsClipboardEntry> entries)
    {
        if (_currentJob == null)
            throw new InvalidOperationException("Open a job before deleting takeoffs.");
        if (!EnsureCurrentJobWritable("delete takeoffs"))
            throw new InvalidOperationException("Takeoff deletion is unavailable while the job is read-only.");

        // Land pending edits first so the trash copy holds the latest data and
        // no debounced autosave fires after the folders are gone.
        FlushTakeoffAutosaves();

        var validEntries = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.SourcePath))
            .Select(entry => entry with { SourcePath = NormalizePath(entry.SourcePath) })
            .Where(entry => Directory.Exists(entry.SourcePath))
            .Where(entry =>
                OurPlanCoreJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, entry.SourcePath) &&
                !string.Equals(NormalizePath(entry.SourcePath), NormalizePath(_currentJob.TakeoffsRoot), StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (validEntries.Count == 0)
            return new TakeoffDeleteUndoBatch(_currentJob.RootPath, "", [], "takeoff node");

        string trashRoot = CreateTakeoffUndoTrashRoot(_currentJob);
        var moved = new List<TakeoffDeleteUndoEntry>();
        try
        {
            for (int i = 0; i < validEntries.Count; i++)
            {
                string sourcePath = validEntries[i].SourcePath;
                int orderIndex = OurPlanCoreJobStore.GetOrderIndex(sourcePath);
                string trashPath = UniqueTakeoffUndoTrashPath(trashRoot, sourcePath, i);
                JobWriteAccess.Demand(sourcePath, "move a takeoff to undo trash");
                JobWriteAccess.Demand(trashPath, "create takeoff undo trash");
                Directory.Move(sourcePath, trashPath);
                moved.Add(new TakeoffDeleteUndoEntry(sourcePath, trashPath, orderIndex));
            }
        }
        catch
        {
            RestoreMovedTakeoffUndoEntries(moved);
            throw;
        }

        return new TakeoffDeleteUndoBatch(
            _currentJob.RootPath,
            trashRoot,
            moved,
            TakeoffUndoStatusName(validEntries));
    }

    private void PushTakeoffDeleteUndo(TakeoffDeleteUndoBatch batch)
    {
        if (batch.Entries.Count == 0)
            return;

        _takeoffDeleteUndoStack.Add(batch);
        if (_takeoffDeleteUndoStack.Count > MaxTakeoffDeleteUndoBatches)
            _takeoffDeleteUndoStack.RemoveAt(0);
    }

    private bool TryUndoLastTakeoffDelete()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Nothing to undo: no job is open.";
            return false;
        }
        if (!EnsureCurrentJobWritable("undo a takeoff deletion"))
            return true;

        // The restore reloads the takeoffs tree and replaces item instances;
        // flush pending edits now so they are not lost with the old objects.
        FlushTakeoffAutosaves();

        while (_takeoffDeleteUndoStack.Count > 0)
        {
            TakeoffDeleteUndoBatch batch = _takeoffDeleteUndoStack[^1];
            if (!string.Equals(NormalizePath(batch.JobRoot), NormalizePath(_currentJob.RootPath), StringComparison.OrdinalIgnoreCase))
            {
                TxtStatus.Text = "Last takeoff delete belongs to another job. Reopen that job to undo it.";
                return true;
            }

            _takeoffDeleteUndoStack.RemoveAt(_takeoffDeleteUndoStack.Count - 1);
            if (batch.Entries.All(entry => !Directory.Exists(entry.TrashPath)))
                continue;

            try
            {
                var restoredPaths = RestoreTakeoffDeleteUndoBatch(batch);
                if (restoredPaths.Count == 0)
                    continue;

                RefreshAfterTakeoffDeleteUndo(restoredPaths);
                TxtStatus.Text = restoredPaths.Count == 1
                    ? $"Restored: {OurPlanCoreJobStore.DisplayName(restoredPaths[0])}."
                    : $"Restored {restoredPaths.Count} takeoff nodes.";
                return true;
            }
            catch (Exception ex)
            {
                ShowOperationError("Undo Takeoff Delete", ex);
                return true;
            }
        }

        TxtStatus.Text = "Nothing to undo in Takeoffs.";
        return false;
    }

    private IReadOnlyList<string> RestoreTakeoffDeleteUndoBatch(TakeoffDeleteUndoBatch batch)
    {
        var restoredPaths = new List<string>();
        foreach (TakeoffDeleteUndoEntry entry in batch.Entries)
        {
            if (!Directory.Exists(entry.TrashPath))
                continue;

            string targetPath = ResolveTakeoffUndoRestorePath(entry.OriginalPath);
            string? targetParent = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetParent))
            {
                JobWriteAccess.Demand(targetParent, "restore a deleted takeoff");
                Directory.CreateDirectory(targetParent);
            }

            JobWriteAccess.Demand(entry.TrashPath, "restore a deleted takeoff");
            JobWriteAccess.Demand(targetPath, "restore a deleted takeoff");
            Directory.Move(entry.TrashPath, targetPath);
            if (entry.OrderIndex != int.MaxValue)
                OurPlanCoreJobStore.SetOrderIndex(targetPath, entry.OrderIndex);
            restoredPaths.Add(targetPath);
        }

        return restoredPaths;
    }

    private void RefreshAfterTakeoffDeleteUndo(IReadOnlyList<string> restoredPaths)
    {
        LoadTakeoffsForJob();
        _viewport.SetMeasurements(_takeoffItems.SelectMany(item => item.Measurements));
        SetTakeoffMultiSelection(restoredPaths);
        SelectFirstTakeoffPath(restoredPaths);
        RefreshPagesTakeoffIndicators();
        ApplyTakeoffPageHighlights();
        RefreshSheetLegend();
        RefreshEstimateTable();
        UpdateTotalDisplay();
    }

    private string ResolveTakeoffUndoRestorePath(string originalPath)
    {
        if (_currentJob == null)
            return originalPath;

        string targetPath = originalPath;
        string? parent = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrWhiteSpace(parent) ||
            !Directory.Exists(parent) ||
            !OurPlanCoreJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, parent))
        {
            string leaf = Path.GetFileName(targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            targetPath = Path.Combine(_currentJob.TakeoffsRoot, string.IsNullOrWhiteSpace(leaf) ? "Restored Takeoff" : leaf);
        }

        return Directory.Exists(targetPath)
            ? OurPlanCoreJobStore.UniqueDirectoryPath(targetPath)
            : targetPath;
    }

    private static string CreateTakeoffUndoTrashRoot(OurPlanCoreJob job)
    {
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture);
        string suffix = Guid.NewGuid().ToString("N")[..8];
        string root = Path.Combine(job.RootPath, ".undo", "takeoffs", $"{stamp}_{suffix}");
        JobWriteAccess.Demand(root, "create takeoff undo trash");
        Directory.CreateDirectory(root);
        return root;
    }

    private static string UniqueTakeoffUndoTrashPath(string trashRoot, string sourcePath, int index)
    {
        string leaf = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(leaf))
            leaf = "takeoff";

        string name = $"{index + 1:D3}_{OurPlanCoreJobStore.SanitizeName(leaf, 80)}";
        return OurPlanCoreJobStore.UniqueDirectoryPath(Path.Combine(trashRoot, name));
    }

    private static string TakeoffUndoStatusName(IReadOnlyList<TakeoffsClipboardEntry> entries) =>
        entries.Count == 1
            ? OurPlanCoreJobStore.DisplayName(entries[0].SourcePath)
            : $"{entries.Count} takeoff nodes";

    private static void RestoreMovedTakeoffUndoEntries(IEnumerable<TakeoffDeleteUndoEntry> moved)
    {
        foreach (TakeoffDeleteUndoEntry entry in moved.Reverse())
        {
            if (!Directory.Exists(entry.TrashPath) || Directory.Exists(entry.OriginalPath))
                continue;

            string? parent = Path.GetDirectoryName(entry.OriginalPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                JobWriteAccess.Demand(parent, "roll back a takeoff delete");
                Directory.CreateDirectory(parent);
            }
            JobWriteAccess.Demand(entry.TrashPath, "roll back a takeoff delete");
            JobWriteAccess.Demand(entry.OriginalPath, "roll back a takeoff delete");
            Directory.Move(entry.TrashPath, entry.OriginalPath);
        }
    }

    private sealed record TakeoffDeleteUndoEntry(string OriginalPath, string TrashPath, int OrderIndex);

    private sealed record TakeoffDeleteUndoBatch(
        string JobRoot,
        string TrashRoot,
        IReadOnlyList<TakeoffDeleteUndoEntry> Entries,
        string StatusName);
}
