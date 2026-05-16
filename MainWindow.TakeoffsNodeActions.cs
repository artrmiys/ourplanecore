using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using OurPlaneCore.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void RenameItem(TreeViewItem tvi, TakeoffItem item)
    {
        string? name = ShowInputDialog("New name:", item.Name, "Rename Item");
        if (name == null || name == item.Name) return;
        try
        {
            if (!string.IsNullOrWhiteSpace(item.FolderPath) && Directory.Exists(item.FolderPath))
            {
                string oldPath = item.FolderPath;
                string newPath = OurPlaneCoreJobStore.RenameNodeAllowDuplicateName(item.FolderPath, name);
                UnregisterTakeoffTreeItemPath(oldPath, tvi);
                item.FolderPath = newPath;
                RebasePageLegendTakeoffOrderReferences(oldPath, item.FolderPath);
                item.Name = OurPlaneCoreJobStore.DisplayName(item.FolderPath);
                foreach (var measurement in item.Measurements)
                    measurement.TakeoffFolder = item.FolderPath;
                OurPlaneCoreJobStore.SaveTakeoffItem(item);
                RegisterTakeoffTreeItemSubtree(tvi);
            }
            else
            {
                item.Name = OurPlaneCoreJobStore.NormalizeDisplayName(name, 120);
            }

            SetTreeItemHeader(tvi, item);
            RefreshPagesTakeoffIndicators();
            RefreshSheetLegend();
            UpdateTotalDisplay();
        }
        catch (Exception ex)
        {
            ShowOperationError("Rename Takeoff Item", ex);
        }
    }

    private void DeleteItem(TreeViewItem tvi, TakeoffItem item)
    {
        var res = MessageBox.Show(
            $"Delete \"{item.Name}\" and all its measurements?",
            "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;

        TakeoffDeleteUndoBatch? undoBatch = null;
        try
        {
            FlushTakeoffAutosaves();
            if (!string.IsNullOrWhiteSpace(item.FolderPath) && Directory.Exists(item.FolderPath))
                undoBatch = MoveTakeoffEntriesToUndoTrash([new TakeoffsClipboardEntry(item.FolderPath, true)]);
            if (undoBatch != null)
                PushTakeoffDeleteUndo(undoBatch);
        }
        catch (Exception ex)
        {
            ShowOperationError("Delete Takeoff Item", ex);
            return;
        }

        _viewport.DeleteMeasurements(item.Measurements);
        _takeoffItems.Remove(item);
        RemoveTreeItem(tvi);

        if (ReferenceEquals(_activeItem, item))
        {
            _activeItem = null;
            SelectFirstTakeoffItem();
        }
        RefreshPagesTakeoffIndicators();
        ApplyTakeoffPageHighlights();
        RefreshSheetLegend();
        RefreshEstimateTable();
        UpdateTotalDisplay();
        if (undoBatch != null)
            TxtStatus.Text = $"Deleted: {undoBatch.StatusName}. Press Ctrl+Z in Takeoffs to restore.";
    }

    private void EditTakeoffFolderProperties(TreeViewItem tvi, TakeoffFolderNode folder)
    {
        if (_currentJob == null || !Directory.Exists(folder.FolderPath))
            return;

        TakeoffFolderProperties properties = TakeoffFolderPropertiesStore.Load(folder.FolderPath);
        var dialog = new TakeoffFolderPropertiesDialog(folder.Name, properties)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            string oldPath = folder.FolderPath;
            string newPath = folder.FolderPath;
            string requestedName = dialog.FolderName.Trim();
            if (!string.IsNullOrWhiteSpace(requestedName) &&
                !string.Equals(requestedName, folder.Name, StringComparison.Ordinal))
            {
                newPath = OurPlaneCoreJobStore.RenameNodeAllowDuplicateName(folder.FolderPath, requestedName);
                RebasePageLegendTakeoffOrderReferences(oldPath, newPath);
            }

            var updatedFolder = new TakeoffFolderNode
            {
                Name = OurPlaneCoreJobStore.DisplayName(newPath),
                FolderPath = newPath,
            };
            var updatedProperties = new TakeoffFolderProperties
            {
                DisplayName = updatedFolder.Name,
                Notes = dialog.Notes,
                DefaultColor = dialog.DefaultColor,
                DefaultMeasurementType = dialog.DefaultMeasurementType,
                DefaultUnitPrice = dialog.DefaultUnitPrice,
                DefaultItemNotes = dialog.DefaultItemNotes,
                DefaultNamePrefix = dialog.DefaultNamePrefix,
            };
            TakeoffFolderPropertiesStore.Save(newPath, updatedProperties);

            if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                UnregisterTakeoffTreeItemSubtree(tvi);
                RebaseTakeoffTreeItemPath(tvi, oldPath, newPath);
                RegisterTakeoffTreeItemSubtree(tvi);
            }
            else
            {
                tvi.Tag = updatedFolder;
                SetFolderTreeItemHeader(tvi, updatedFolder);
                AttachFolderContextMenu(tvi, updatedFolder);
                RegisterTakeoffTreeItemSubtree(tvi);
            }

            if (tvi.Tag is TakeoffFolderNode rebasedFolder)
                updatedFolder = rebasedFolder;
            _activeTakeoffParentFolder = newPath;
            TxtStatus.Text = TakeoffFolderStatusText(updatedFolder);
        }
        catch (Exception ex)
        {
            ShowOperationError("Takeoff Folder Properties", ex);
        }
    }

    private void RenameTakeoffFolder(TreeViewItem tvi, TakeoffFolderNode folder)
    {
        string? name = ShowInputDialog("New name:", "Rename Folder", folder.Name);
        if (name == null || name == folder.Name) return;

        try
        {
            string oldPath = folder.FolderPath;
            string newPath = OurPlaneCoreJobStore.RenameNodeAllowDuplicateName(folder.FolderPath, name);
            RebasePageLegendTakeoffOrderReferences(oldPath, newPath);
            UnregisterTakeoffTreeItemSubtree(tvi);
            RebaseTakeoffTreeItemPath(tvi, oldPath, newPath);
            RegisterTakeoffTreeItemSubtree(tvi);

            _activeTakeoffParentFolder = newPath;
        }
        catch (Exception ex)
        {
            ShowOperationError("Rename Takeoff Folder", ex);
        }
    }

    private void DeleteTakeoffFolder(TreeViewItem tvi, TakeoffFolderNode folder)
    {
        var res = MessageBox.Show(
            $"Delete folder \"{folder.Name}\" and all child takeoffs?",
            "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;

        var removedItems = _takeoffItems
            .Where(i => OurPlaneCoreJobStore.IsSameOrDescendant(folder.FolderPath, i.FolderPath))
            .ToList();

        TakeoffDeleteUndoBatch? undoBatch = null;
        try
        {
            FlushTakeoffAutosaves();
            undoBatch = MoveTakeoffEntriesToUndoTrash([new TakeoffsClipboardEntry(folder.FolderPath, false)]);
            PushTakeoffDeleteUndo(undoBatch);
        }
        catch (Exception ex)
        {
            ShowOperationError("Delete Takeoff Folder", ex);
            return;
        }

        foreach (var item in removedItems)
        {
            _viewport.DeleteMeasurements(item.Measurements);
            _takeoffItems.Remove(item);
        }
        RemoveTreeItem(tvi);

        if (_activeItem != null && removedItems.Contains(_activeItem))
            SelectFirstTakeoffItem();
        else
            UpdateTotalDisplay();
        RefreshPagesTakeoffIndicators();
        ApplyTakeoffPageHighlights();
        RefreshSheetLegend();
        RefreshEstimateTable();
        if (undoBatch != null)
            TxtStatus.Text = $"Deleted: {undoBatch.StatusName}. Press Ctrl+Z in Takeoffs to restore.";
    }

    private void DeleteTakeoffNodes(TreeViewItem anchor)
    {
        if (_currentJob == null)
            return;

        var entries = GetSelectedTakeoffEntries(anchor);
        if (entries.Count == 0)
            return;

        string message = entries.Count == 1
            ? $"Delete \"{OurPlaneCoreJobStore.DisplayName(entries[0].SourcePath)}\" and all contained measurements?"
            : $"Delete {entries.Count} selected takeoff node(s) and all contained measurements?";
        var res = MessageBox.Show(message, "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes)
            return;

        try
        {
            FlushTakeoffAutosaves();
            var removedItems = _takeoffItems
                .Where(item => entries.Any(entry =>
                    OurPlaneCoreJobStore.IsSameOrDescendant(entry.SourcePath, item.FolderPath)))
                .ToList();

            TakeoffDeleteUndoBatch undoBatch = MoveTakeoffEntriesToUndoTrash(entries);
            PushTakeoffDeleteUndo(undoBatch);

            foreach (var item in removedItems)
                _takeoffItems.Remove(item);

            if (_activeItem != null && removedItems.Contains(_activeItem))
                _activeItem = null;

            if (_takeoffsClipboard != null && entries.Any(entry =>
                    _takeoffsClipboard.Entries.Any(clip =>
                        OurPlaneCoreJobStore.IsSameOrDescendant(entry.SourcePath, clip.SourcePath))))
                _takeoffsClipboard = null;

            _takeoffsMultiSelection.Clear();
            LoadTakeoffsForJob();
            _viewport.SetMeasurements(_takeoffItems.SelectMany(item => item.Measurements));
            RefreshPagesTakeoffIndicators();
            RefreshEstimateTable();
            UpdateTotalDisplay();
            TxtStatus.Text = entries.Count == 1
                ? $"Deleted: {undoBatch.StatusName}. Press Ctrl+Z in Takeoffs to restore."
                : $"Deleted {entries.Count} takeoff nodes. Press Ctrl+Z in Takeoffs to restore.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Delete Takeoffs", ex);
        }
    }

    private void MoveTakeoffNode(string folderPath, int offset)
    {
        if (FindTakeoffTreeItemByFolder(folderPath) is { } item)
        {
            MoveTakeoffNodes(item, offset);
            return;
        }

        try
        {
            if (!OurPlaneCoreJobStore.MoveSibling(folderPath, offset))
                return;
            LoadTakeoffsForJob();
        }
        catch (Exception ex)
        {
            ShowOperationError("Move Takeoff Node", ex);
        }
    }

    private bool CanMoveTakeoffNodes(TreeViewItem anchor, int offset)
    {
        var paths = GetSelectedTakeoffEntries(anchor)
            .Select(entry => entry.SourcePath)
            .ToList();
        return OurPlaneCoreJobStore.CanMoveSiblings(paths, offset);
    }

    private void MoveTakeoffNodes(TreeViewItem anchor, int offset)
    {
        var entries = GetSelectedTakeoffEntries(anchor);
        var paths = entries.Select(entry => entry.SourcePath).ToList();
        if (paths.Count == 0)
            return;

        try
        {
            if (!OurPlaneCoreJobStore.MoveSiblings(paths, offset))
                return;

            string parentFolder = Path.GetDirectoryName(paths[0]) ?? "";
            if (!TryRefreshTakeoffTreeParentOrderFast(parentFolder, paths))
            {
                LoadTakeoffsForJob();
                SetTakeoffMultiSelection(paths);
                SelectFirstTakeoffPath(paths);
            }

            TxtStatus.Text = paths.Count == 1
                ? (offset < 0 ? "Moved takeoff node up." : "Moved takeoff node down.")
                : (offset < 0 ? $"Moved {paths.Count} takeoff nodes up." : $"Moved {paths.Count} takeoff nodes down.");
        }
        catch (Exception ex)
        {
            ShowOperationError(offset < 0 ? "Move Takeoff Nodes Up" : "Move Takeoff Nodes Down", ex);
        }
    }

    private void SortTakeoffChildren(string folderPath, bool descending)
    {
        try
        {
            OurPlaneCoreJobStore.SortChildren(folderPath, descending);
            if (!TryRefreshTakeoffTreeParentOrderFast(folderPath, _takeoffsMultiSelection))
                LoadTakeoffsForJob();
        }
        catch (Exception ex)
        {
            ShowOperationError("Sort Takeoffs", ex);
        }
    }
}
