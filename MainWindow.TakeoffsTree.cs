using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

public partial class MainWindow
{
    // ── Takeoffs tree ─────────────────────────────────────────────────────────

    private void TakeoffsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_syncingTakeoffTreeSelection)
            return;

        CancelPendingTakeoffSelectionSync();

        if (e.NewValue is TreeViewItem selectedNode &&
            GetTakeoffNodePath(selectedNode) is { } selectedPath &&
            (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control &&
            !_takeoffsMultiSelection.Contains(selectedPath))
        {
            _takeoffsMultiSelection.Clear();
            _takeoffSectionMultiSelection.Clear();
            _takeoffsMultiSelection.Add(selectedPath);
            ApplyTakeoffPageHighlights();
        }

        if (e.NewValue is TreeViewItem { Tag: TakeoffItem item })
        {
            _takeoffSectionMultiSelection.Clear();
            item.MeasurementType = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
            _activeItem           = item;
            _activeTakeoffParentFolder = Path.GetDirectoryName(item.FolderPath) ?? _currentJob?.TakeoffsRoot ?? "";
            _viewport.ActiveColor = item.Color;
            _viewport.ActiveTakeoffFolder = item.FolderPath;
            if (_activeTool is "point" or "line" or "area" && _activeTool != item.MeasurementType)
                ApplyToolSelection(item.MeasurementType);
            else
            UpdateToolStatus();
            RefreshPagesTakeoffIndicators();
            RefreshActiveTakeoffVisuals();
            RevealPagesForTakeoffSelection(e.NewValue as TreeViewItem);
            ScheduleTakeoffSelectionSync(() => SelectTakeoffSelectionMeasurementsOnCurrentPage(e.NewValue as TreeViewItem));
            UpdateTotalDisplay();
        }
        else if (e.NewValue is TreeViewItem { Tag: TakeoffFolderNode folder })
        {
            _takeoffSectionMultiSelection.Clear();
            _activeItem = null;
            _activeTakeoffParentFolder = folder.FolderPath;
            _viewport.ActiveTakeoffFolder = "";
            TxtStatus.Text = TakeoffFolderStatusText(folder);
            UpdateToolStatus();
            RefreshPagesTakeoffIndicators();
            RefreshActiveTakeoffVisuals();
            RevealPagesForTakeoffSelection(e.NewValue as TreeViewItem);
            ScheduleTakeoffSelectionSync(() => SelectTakeoffSelectionMeasurementsOnCurrentPage(e.NewValue as TreeViewItem));
            UpdateTotalDisplay();
        }
        else if (e.NewValue is TreeViewItem { Tag: TakeoffMeasurementNode node })
        {
            string sectionKey = TakeoffSectionSelectionKey(node);
            _takeoffsMultiSelection.Clear();
            if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control &&
                !_takeoffSectionMultiSelection.Contains(sectionKey))
            {
                _takeoffSectionMultiSelection.Clear();
                _takeoffSectionMultiSelection.Add(sectionKey);
                _takeoffSectionRangeAnchorKey = sectionKey;
                ApplyTakeoffPageHighlights();
            }

            _activeItem = node.Item;
            _activeTakeoffParentFolder = Path.GetDirectoryName(node.Item.FolderPath) ?? _currentJob?.TakeoffsRoot ?? "";
            _viewport.ActiveColor = node.Item.Color;
            _viewport.ActiveTakeoffFolder = node.Item.FolderPath;
            if (_suppressCanvasFocusFromTakeoffSelection || Mouse.LeftButton == MouseButtonState.Pressed)
            {
                if (_currentPage != null && IsSamePageFolder(_currentPage.FolderPath, node.Measurement.PageFolder))
                    _viewport.SelectMeasurements([node.Measurement]);
            }
            else
            {
                SelectSectionOnCanvas(node.Measurement, suppressTakeoffSync: true);
            }
            UpdateToolStatus();
            RefreshPagesTakeoffIndicators();
            RefreshActiveTakeoffVisuals();
            RevealPagesForTakeoffItems([node.Item], node.Measurement.PageFolder);
            UpdateTotalDisplay();
        }
    }

    private void CancelPendingTakeoffSelectionSync()
    {
        unchecked
        {
            _takeoffSelectionSyncVersion++;
        }
    }

    private void ScheduleTakeoffSelectionSync(Action action)
    {
        int version;
        unchecked
        {
            _takeoffSelectionSyncVersion++;
            version = _takeoffSelectionSyncVersion;
        }

        Dispatcher.InvokeAsync(() =>
        {
            if (version != _takeoffSelectionSyncVersion)
                return;

            action();
        });
    }

    // ── Takeoff item context menu ─────────────────────────────────────────────

    private void AttachContextMenu(TreeViewItem tvi, TakeoffItem item)
    {
        var menu = new ContextMenu();
        int selectedCount = TakeoffSelectionCount(tvi);
        bool singleSelection = selectedCount <= 1;

        var activeTarget = new MenuItem { Header = IsActiveTakeoffItem(item) ? "Active Target" : "Set Active Target" };
        activeTarget.Click += (_, _) => SetActiveTakeoffTarget(tvi, item);
        activeTarget.IsEnabled = singleSelection;
        menu.Items.Add(activeTarget);
        menu.Items.Add(new Separator());

        var properties = new MenuItem { Header = "Properties..." };
        properties.Click += (_, _) => EditTakeoffItemProperties(tvi, item);
        properties.IsEnabled = singleSelection;
        menu.Items.Add(properties);
        menu.Items.Add(MakeMenuItem(
            item.IsJoistArea ? "Joist Properties..." : "Use Area As Joists...",
            singleSelection && OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "area",
            () => EditTakeoffItemProperties(tvi, item)));
        menu.Items.Add(MakeMenuItem(
            "Generate Joists / Draw Direction",
            singleSelection && OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "area",
            () => SetJoistDirectionFromSelectedLine(tvi, item)));

        int selectedItemsCount = TakeoffItemsForSelection(tvi).Count;
        menu.Items.Add(MakeMenuItem(
            selectedItemsCount > 1 ? $"Bulk Properties ({selectedItemsCount} Items)..." : "Bulk Properties...",
            selectedItemsCount > 1,
            () => EditSelectedTakeoffProperties(tvi)));

        var rename = new MenuItem { Header = "Rename..." };
        rename.Click += (_, _) => RenameItem(tvi, item);
        rename.IsEnabled = singleSelection;
        menu.Items.Add(rename);

        var newSection = new MenuItem { Header = item.MeasurementType == "point" ? "Add Count" : "New Section" };
        newSection.Click += (_, _) => StartNewSection(tvi, item);
        newSection.IsEnabled = singleSelection;
        menu.Items.Add(newSection);

        var unitPrice = new MenuItem { Header = "Set Unit Price" };
        unitPrice.Click += (_, _) => SetUnitPrice(item);
        unitPrice.IsEnabled = singleSelection;
        menu.Items.Add(unitPrice);

        menu.Items.Add(new Separator());

        menu.Items.Add(MakeMenuItem(selectedCount > 1 ? "Copy Selected" : "Copy Item", true, () => CopyCutTakeoffNode(tvi, TakeoffsClipboardMode.Copy)));
        menu.Items.Add(MakeMenuItem(selectedCount > 1 ? "Cut Selected" : "Cut Item", true, () => CopyCutTakeoffNode(tvi, TakeoffsClipboardMode.Cut)));
        menu.Items.Add(MakeMenuItem(
            "Paste Into Parent Folder",
            CanPasteTakeoffsInto(Path.GetDirectoryName(item.FolderPath)),
            () => PasteIntoSelectedTakeoffTarget(tvi)));
        menu.Items.Add(MakeMenuItem(selectedCount > 1 ? "Duplicate Selected" : "Duplicate Item", true, () => DuplicateTakeoffNode(tvi)));

        menu.Items.Add(new Separator());

        var moveUp = new MenuItem
        {
            Header = selectedCount > 1 ? $"Move {selectedCount} Up" : "Move Up",
            IsEnabled = CanMoveTakeoffNodes(tvi, -1),
        };
        moveUp.Click += (_, _) => MoveTakeoffNodes(tvi, -1);
        menu.Items.Add(moveUp);

        var moveDown = new MenuItem
        {
            Header = selectedCount > 1 ? $"Move {selectedCount} Down" : "Move Down",
            IsEnabled = CanMoveTakeoffNodes(tvi, 1),
        };
        moveDown.Click += (_, _) => MoveTakeoffNodes(tvi, 1);
        menu.Items.Add(moveDown);

        menu.Items.Add(new Separator());

        var delete = new MenuItem { Header = selectedCount > 1 ? "Delete selected takeoffs" : "Delete item + measurements" };
        delete.Click += (_, _) => DeleteTakeoffNodes(tvi);
        menu.Items.Add(delete);

        tvi.ContextMenu = menu;
    }

    private void AttachFolderContextMenu(TreeViewItem tvi, TakeoffFolderNode folder)
    {
        var menu = new ContextMenu();
        int selectedCount = TakeoffSelectionCount(tvi);
        bool singleSelection = selectedCount <= 1;
        bool canEditFolder = !folder.IsRoot && singleSelection;

        var newFolder = new MenuItem { Header = "New Folder" };
        newFolder.Click += (_, _) =>
        {
            tvi.IsSelected = true;
            BtnNewTakeoffFolder_Click(tvi, new RoutedEventArgs());
        };
        menu.Items.Add(newFolder);

        var newItem = new MenuItem { Header = "New Item" };
        newItem.Click += (_, _) =>
        {
            tvi.IsSelected = true;
            BtnNewItem_Click(tvi, new RoutedEventArgs());
        };
        menu.Items.Add(newItem);

        menu.Items.Add(MakeMenuItem("Auto Create Tree", true, () => AutoCreateTakeoffTree(folder.FolderPath)));
        menu.Items.Add(MakeMenuItem("Create Folders From Pages", true, () => AutoCreateTakeoffFoldersFromPages(folder.FolderPath)));

        menu.Items.Add(new Separator());

        var rename = new MenuItem { Header = "Rename Folder…" };
        menu.Items.Add(MakeMenuItem(selectedCount > 1 ? "Copy Selected" : "Copy Folder", !folder.IsRoot, () => CopyCutTakeoffNode(tvi, TakeoffsClipboardMode.Copy)));
        menu.Items.Add(MakeMenuItem(selectedCount > 1 ? "Cut Selected" : "Cut Folder", !folder.IsRoot, () => CopyCutTakeoffNode(tvi, TakeoffsClipboardMode.Cut)));
        menu.Items.Add(MakeMenuItem("Paste Into Folder", CanPasteTakeoffsInto(folder.FolderPath), () => PasteIntoSelectedTakeoffTarget(tvi)));
        menu.Items.Add(MakeMenuItem(selectedCount > 1 ? "Duplicate Selected" : "Duplicate Folder", !folder.IsRoot, () => DuplicateTakeoffNode(tvi)));

        menu.Items.Add(new Separator());

        menu.Items.Add(MakeMenuItem("Folder Properties...", canEditFolder, () => EditTakeoffFolderProperties(tvi, folder)));
        int nestedTakeoffCount = TakeoffItemsForSelection(tvi).Count;
        menu.Items.Add(MakeMenuItem(
            nestedTakeoffCount > 1 ? $"Bulk Item Properties ({nestedTakeoffCount})..." : "Bulk Item Properties...",
            nestedTakeoffCount > 0,
            () => EditSelectedTakeoffProperties(tvi)));

        rename.Click += (_, _) => RenameTakeoffFolder(tvi, folder);
        rename.IsEnabled = canEditFolder;
        menu.Items.Add(rename);

        var moveUp = new MenuItem
        {
            Header = selectedCount > 1 ? $"Move {selectedCount} Up" : "Move Up",
            IsEnabled = CanMoveTakeoffNodes(tvi, -1),
        };
        moveUp.Click += (_, _) => MoveTakeoffNodes(tvi, -1);
        menu.Items.Add(moveUp);

        var moveDown = new MenuItem
        {
            Header = selectedCount > 1 ? $"Move {selectedCount} Down" : "Move Down",
            IsEnabled = CanMoveTakeoffNodes(tvi, 1),
        };
        moveDown.Click += (_, _) => MoveTakeoffNodes(tvi, 1);
        menu.Items.Add(moveDown);

        menu.Items.Add(new Separator());

        var sortAz = new MenuItem { Header = "Sort Children A-Z" };
        sortAz.Click += (_, _) => SortTakeoffChildren(folder.FolderPath, descending: false);
        menu.Items.Add(sortAz);

        var sortZa = new MenuItem { Header = "Sort Children Z-A" };
        sortZa.Click += (_, _) => SortTakeoffChildren(folder.FolderPath, descending: true);
        menu.Items.Add(sortZa);

        menu.Items.Add(new Separator());

        var open = new MenuItem { Header = "Open in Explorer" };
        open.Click += (_, _) => OpenFolderInExplorer(folder.FolderPath);
        menu.Items.Add(open);

        var delete = new MenuItem
        {
            Header = selectedCount > 1 ? "Delete selected takeoffs" : "Delete folder + children",
            IsEnabled = !folder.IsRoot,
        };
        delete.Click += (_, _) => DeleteTakeoffNodes(tvi);
        menu.Items.Add(delete);

        tvi.ContextMenu = menu;
    }

    private ContextMenu BuildTakeoffsRootContextMenu()
    {
        var menu = new ContextMenu();

        menu.Items.Add(MakeMenuItem(
            "Auto Create Tree",
            _currentJob != null,
            () =>
            {
                if (_currentJob != null)
                    AutoCreateTakeoffTree(_currentJob.TakeoffsRoot);
            }));
        menu.Items.Add(MakeMenuItem(
            "Create Folders From Pages",
            _currentJob != null,
            () =>
            {
                if (_currentJob != null)
                    AutoCreateTakeoffFoldersFromPages(_currentJob.TakeoffsRoot);
            }));

        menu.Items.Add(new Separator());

        menu.Items.Add(MakeMenuItem(
            "Paste Into Root",
            _currentJob != null && CanPasteTakeoffsInto(_currentJob.TakeoffsRoot),
            () =>
            {
                if (_currentJob != null)
                    PasteTakeoffsIntoFolder(_currentJob.TakeoffsRoot);
            }));

        menu.Items.Add(new Separator());

        var sortAz = new MenuItem { Header = "Sort Root A-Z" };
        sortAz.Click += (_, _) =>
        {
            if (_currentJob != null)
                SortTakeoffChildren(_currentJob.TakeoffsRoot, descending: false);
        };
        menu.Items.Add(sortAz);

        var sortZa = new MenuItem { Header = "Sort Root Z-A" };
        sortZa.Click += (_, _) =>
        {
            if (_currentJob != null)
                SortTakeoffChildren(_currentJob.TakeoffsRoot, descending: true);
        };
        menu.Items.Add(sortZa);

        menu.Items.Add(new Separator());

        var open = new MenuItem { Header = "Open Takeoffs in Explorer" };
        open.Click += (_, _) =>
        {
            if (_currentJob != null)
                OpenFolderInExplorer(_currentJob.TakeoffsRoot);
        };
        menu.Items.Add(open);

        return menu;
    }

    private void RenameItem(TreeViewItem tvi, TakeoffItem item)
    {
        string? name = ShowInputDialog("New name:", item.Name, "Rename Item");
        if (name == null || name == item.Name) return;
        try
        {
            if (!string.IsNullOrWhiteSpace(item.FolderPath) && Directory.Exists(item.FolderPath))
            {
                string oldPath = item.FolderPath;
                item.FolderPath = OurPlaneCoreJobStore.RenameNode(item.FolderPath, name);
                RebasePageLegendTakeoffOrderReferences(oldPath, item.FolderPath);
                item.Name = OurPlaneCoreJobStore.DisplayName(item.FolderPath);
                foreach (var measurement in item.Measurements)
                    measurement.TakeoffFolder = item.FolderPath;
                OurPlaneCoreJobStore.SaveTakeoffItem(item);
            }
            else
            {
                item.Name = OurPlaneCoreJobStore.SanitizeName(name, 120);
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

        try
        {
            if (!string.IsNullOrWhiteSpace(item.FolderPath) && Directory.Exists(item.FolderPath))
                DeleteDirectoryToRecycle(item.FolderPath);
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
        UpdateTotalDisplay();
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
                newPath = OurPlaneCoreJobStore.RenameNode(folder.FolderPath, requestedName);
                RebasePageLegendTakeoffOrderReferences(oldPath, newPath);
                foreach (var item in _takeoffItems)
                {
                    if (!OurPlaneCoreJobStore.IsSameOrDescendant(oldPath, item.FolderPath))
                        continue;

                    item.FolderPath = Path.Combine(newPath, Path.GetRelativePath(oldPath, item.FolderPath));
                    foreach (var measurement in item.Measurements)
                        measurement.TakeoffFolder = item.FolderPath;
                }
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

            tvi.Tag = updatedFolder;
            SetFolderTreeItemHeader(tvi, updatedFolder);
            AttachFolderContextMenu(tvi, updatedFolder);
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
            string newPath = OurPlaneCoreJobStore.RenameNode(folder.FolderPath, name);
            RebasePageLegendTakeoffOrderReferences(oldPath, newPath);
            folder = new TakeoffFolderNode
            {
                Name = OurPlaneCoreJobStore.DisplayName(newPath),
                FolderPath = newPath,
            };
            tvi.Tag = folder;
            SetFolderTreeItemHeader(tvi, folder);
            AttachFolderContextMenu(tvi, folder);

            foreach (var item in _takeoffItems)
            {
                if (!OurPlaneCoreJobStore.IsSameOrDescendant(oldPath, item.FolderPath))
                    continue;

                item.FolderPath = Path.Combine(newPath, Path.GetRelativePath(oldPath, item.FolderPath));
                foreach (var measurement in item.Measurements)
                    measurement.TakeoffFolder = item.FolderPath;
            }

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

        try
        {
            DeleteDirectoryToRecycle(folder.FolderPath);
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

            foreach (var entry in entries)
            {
                if (Directory.Exists(entry.SourcePath))
                    DeleteDirectoryToRecycle(entry.SourcePath);
            }

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
                ? $"Deleted: {OurPlaneCoreJobStore.DisplayName(entries[0].SourcePath)}"
                : $"Deleted {entries.Count} takeoff nodes.";
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

            LoadTakeoffsForJob();
            SetTakeoffMultiSelection(paths);
            SelectFirstTakeoffPath(paths);
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
            LoadTakeoffsForJob();
        }
        catch (Exception ex)
        {
            ShowOperationError("Sort Takeoffs", ex);
        }
    }

    private void TakeoffsTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is { } item)
        {
            if (item.Tag is TakeoffMeasurementNode sectionNode)
            {
                string key = TakeoffSectionSelectionKey(sectionNode);
                _takeoffsMultiSelection.Clear();
                if (!_takeoffSectionMultiSelection.Contains(key))
                {
                    _takeoffSectionMultiSelection.Clear();
                    _takeoffSectionMultiSelection.Add(key);
                    _takeoffSectionRangeAnchorKey = key;
                    _takeoffsMultiSelection.Clear();
                    ApplyTakeoffPageHighlights();
                }

                item.Focus();
                item.IsSelected = true;
                item.ContextMenu = BuildTakeoffSectionContextMenu(sectionNode);
                e.Handled = true;
                return;
            }

            string? path = GetTakeoffNodePath(item);
            if (path != null)
                _takeoffSectionMultiSelection.Clear();
            if (path != null && !_takeoffsMultiSelection.Contains(path))
            {
                _takeoffsMultiSelection.Clear();
                _takeoffSectionMultiSelection.Clear();
                _takeoffsMultiSelection.Add(path);
                _takeoffsRangeAnchorPath = path;
                ApplyTakeoffPageHighlights();
            }
            if (path != null)
                RevealPagesForTakeoffSelection(item);

            item.Focus();
            item.IsSelected = true;
            RefreshTakeoffNodeContextMenu(item);
            e.Handled = true;
        }
    }

    private void TakeoffsTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is { } item)
        {
            RefreshTakeoffNodeContextMenu(item);
            return;
        }

        TakeoffsTree.ContextMenu = BuildTakeoffsRootContextMenu();
    }

    private void RefreshTakeoffNodeContextMenu(TreeViewItem item)
    {
        switch (item.Tag)
        {
            case TakeoffItem takeoff:
                AttachContextMenu(item, takeoff);
                break;
            case TakeoffFolderNode folder:
                AttachFolderContextMenu(item, folder);
                break;
        }
    }

    private void TakeoffsTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _takeoffsDragStart = e.GetPosition(TakeoffsTree);
        _takeoffsDragItem = null;
        _takeoffsDragArmed = false;
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is not { } item)
            return;

        _takeoffsDragItem = item;
        _takeoffsDragArmed = CanArmTakeoffsTreeDrag(item, e.OriginalSource as DependencyObject);

        if (item.Tag is TakeoffMeasurementNode sectionNode)
        {
            HandleTakeoffSectionNodeMultiSelect(item, sectionNode, e);
            return;
        }

        string? path = GetTakeoffNodePath(item);
        if (path == null)
            return;

        ModifierKeys modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None &&
            _takeoffsMultiSelection.Count > 1 &&
            _takeoffsMultiSelection.Contains(path))
        {
            _takeoffSectionMultiSelection.Clear();
            item.IsSelected = true;
            ApplyTakeoffPageHighlights();
            RevealPagesForTakeoffSelection(item);
            ScheduleTakeoffSelectionSync(() => SelectTakeoffSelectionMeasurementsOnCurrentPage(item));
            return;
        }

        if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            bool additive = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            SelectTakeoffsRange(_takeoffsRangeAnchorPath, path, additive);
            _takeoffsRangeAnchorPath = path;
            _takeoffSectionMultiSelection.Clear();
            item.IsSelected = true;
            ApplyTakeoffPageHighlights();
            RevealPagesForTakeoffSelection(item);
            ScheduleTakeoffSelectionSync(() => SelectTakeoffSelectionMeasurementsOnCurrentPage(item));
            e.Handled = true;
            return;
        }

        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (!_takeoffsMultiSelection.Add(path))
                _takeoffsMultiSelection.Remove(path);
            _takeoffsRangeAnchorPath = path;
            _takeoffSectionMultiSelection.Clear();
            item.IsSelected = true;
            ApplyTakeoffPageHighlights();
            RevealPagesForTakeoffSelection(item);
            ScheduleTakeoffSelectionSync(() => SelectTakeoffSelectionMeasurementsOnCurrentPage(item));
            e.Handled = true;
            return;
        }

        if (!_takeoffsMultiSelection.SetEquals([path]))
        {
            _takeoffsMultiSelection.Clear();
            _takeoffsMultiSelection.Add(path);
            _takeoffSectionMultiSelection.Clear();
            ApplyTakeoffPageHighlights();
        }
        _takeoffsRangeAnchorPath = path;
        item.IsSelected = true;
        RevealPagesForTakeoffSelection(item);
        ScheduleTakeoffSelectionSync(() => SelectTakeoffSelectionMeasurementsOnCurrentPage(item));
    }

    private bool CanArmTakeoffsTreeDrag(TreeViewItem item, DependencyObject? source)
    {
        if (FindAncestor<ToggleButton>(source) != null)
            return false;
        if (ReferenceEquals(TakeoffsTree.SelectedItem, item))
            return true;
        if (item.Tag is TakeoffMeasurementNode sectionNode)
            return _takeoffSectionMultiSelection.Contains(TakeoffSectionSelectionKey(sectionNode));

        string? path = GetTakeoffNodePath(item);
        return path != null && _takeoffsMultiSelection.Contains(path);
    }

    private void HandleTakeoffSectionNodeMultiSelect(TreeViewItem item, TakeoffMeasurementNode node, MouseButtonEventArgs e)
    {
        string key = TakeoffSectionSelectionKey(node);
        ModifierKeys modifiers = Keyboard.Modifiers;
        _takeoffsMultiSelection.Clear();

        if (modifiers == ModifierKeys.None &&
            _takeoffSectionMultiSelection.Count > 1 &&
            _takeoffSectionMultiSelection.Contains(key))
        {
            item.IsSelected = true;
            ApplyTakeoffPageHighlights();
            ScheduleTakeoffSelectionSync(() => SelectTakeoffSectionMeasurementsOnCanvas(SelectedTakeoffSectionNodes(node, fallbackToAnchor: false)));
            return;
        }

        if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            bool additive = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            SelectTakeoffSectionRange(_takeoffSectionRangeAnchorKey, key, node.Item, additive);
            _takeoffSectionRangeAnchorKey = key;
            item.IsSelected = true;
            ApplyTakeoffPageHighlights();
            ScheduleTakeoffSelectionSync(() => SelectTakeoffSectionMeasurementsOnCanvas(SelectedTakeoffSectionNodes(node, fallbackToAnchor: false)));
            e.Handled = true;
            return;
        }

        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (!_takeoffSectionMultiSelection.Add(key))
                _takeoffSectionMultiSelection.Remove(key);
            _takeoffSectionRangeAnchorKey = key;
            item.IsSelected = true;
            ApplyTakeoffPageHighlights();
            ScheduleTakeoffSelectionSync(() => SelectTakeoffSectionMeasurementsOnCanvas(SelectedTakeoffSectionNodes(node, fallbackToAnchor: false)));
            e.Handled = true;
            return;
        }

        _takeoffSectionMultiSelection.Clear();
        _takeoffSectionMultiSelection.Add(key);
        _takeoffSectionRangeAnchorKey = key;
        ApplyTakeoffPageHighlights();
        ScheduleTakeoffSelectionSync(() => SelectTakeoffSectionMeasurementsOnCanvas(SelectedTakeoffSectionNodes(node, fallbackToAnchor: true)));
    }

    private void SelectTakeoffsRange(string? anchorPath, string targetPath, bool additive)
    {
        var candidates = EnumerateVisibleTreeItems(TakeoffsTree)
            .Select(item => (Item: item, Key: GetTakeoffNodePath(item)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .Select(entry => (entry.Item, Key: entry.Key!))
            .ToList();

        SelectRangeKeys(candidates, anchorPath, targetPath, _takeoffsMultiSelection, additive);
    }

    private void TakeoffsTree_MouseMove(object sender, MouseEventArgs e)
    {
        if (_takeoffsDragStart == null || e.LeftButton != MouseButtonState.Pressed)
            return;

        if (!_takeoffsDragArmed)
        {
            _takeoffsDragStart = null;
            _takeoffsDragItem = null;
            return;
        }

        Point pos = e.GetPosition(TakeoffsTree);
        if (Math.Abs(pos.X - _takeoffsDragStart.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _takeoffsDragStart.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if ((_takeoffsDragItem ?? TakeoffsTree.SelectedItem) is not TreeViewItem item)
            return;

        if (item.Tag is TakeoffMeasurementNode sectionNode)
        {
            var nodes = SelectedTakeoffSectionNodes(sectionNode, fallbackToAnchor: true);
            if (nodes.Count == 0)
            {
                _takeoffsDragStart = null;
                _takeoffsDragItem = null;
                return;
            }

            var sectionPayload = new TakeoffSectionDrag(nodes);
            DragDrop.DoDragDrop(TakeoffsTree, sectionPayload, DragDropEffects.Move | DragDropEffects.Copy);
            ClearTakeoffSectionDropCue();
            ClearTakeoffPositionDropCue();
            _takeoffsDragStart = null;
            _takeoffsDragItem = null;
            _takeoffsDragArmed = false;
            return;
        }

        var entries = GetSelectedTakeoffEntries(item);
        if (entries.Count == 0)
        {
            _takeoffsDragStart = null;
            _takeoffsDragItem = null;
            _takeoffsDragArmed = false;
            return;
        }

        var payload = new TakeoffsClipboard(entries, TakeoffsClipboardMode.Cut);
        DragDrop.DoDragDrop(TakeoffsTree, payload, DragDropEffects.Move | DragDropEffects.Copy);
        ClearTakeoffPositionDropCue();
        _takeoffsDragStart = null;
        _takeoffsDragItem = null;
        _takeoffsDragArmed = false;
    }

    private void TakeoffsTree_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        TreeViewItem? targetItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        bool copy = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;
        if (e.Data.GetData(typeof(TakeoffSectionDrag)) is TakeoffSectionDrag sectionDrag)
        {
            ClearTakeoffPositionDropCue();
            bool canDropSection = CanDropTakeoffSections(sectionDrag, targetItem, copy);
            UpdateTakeoffSectionDropCue(sectionDrag, targetItem, copy, canDropSection);
            if (canDropSection)
                e.Effects = copy ? DragDropEffects.Copy : DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        ClearTakeoffSectionDropCue();
        if (e.Data.GetData(typeof(TakeoffsClipboard)) is not TakeoffsClipboard payload)
            return;

        if (TryGetTakeoffPositionDropCue(payload, targetItem, copy, e, out bool after, out bool canDropPosition, out string positionStatus))
        {
            UpdateTakeoffPositionDropCue(targetItem, after, canDropPosition, positionStatus);
            if (canDropPosition)
                e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        ClearTakeoffPositionDropCue();
        string? targetFolder = targetItem == null ? _currentJob?.TakeoffsRoot : GetTakeoffPasteTargetFolder(targetItem);
        if (CanDropTakeoffsInto(payload, targetFolder, copy ? TakeoffsClipboardMode.Copy : TakeoffsClipboardMode.Cut))
            e.Effects = copy ? DragDropEffects.Copy : DragDropEffects.Move;
        e.Handled = true;
    }

    private void TakeoffsTree_Drop(object sender, DragEventArgs e)
    {
        TreeViewItem? targetItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        bool copy = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;
        if (e.Data.GetData(typeof(TakeoffSectionDrag)) is TakeoffSectionDrag sectionDrag)
        {
            ClearTakeoffPositionDropCue();
            if (CanDropTakeoffSections(sectionDrag, targetItem, copy))
                DropTakeoffSections(sectionDrag, targetItem!, copy);
            ClearTakeoffSectionDropCue();
            e.Handled = true;
            return;
        }

        ClearTakeoffSectionDropCue();
        if (e.Data.GetData(typeof(TakeoffsClipboard)) is not TakeoffsClipboard payload)
            return;

        if (TryGetTakeoffPositionDropCue(payload, targetItem, copy, e, out bool after, out bool canDropPosition, out _) &&
            canDropPosition &&
            targetItem != null)
        {
            DropTakeoffPosition(payload, targetItem, after);
            ClearTakeoffPositionDropCue();
            e.Handled = true;
            return;
        }

        ClearTakeoffPositionDropCue();
        string? targetFolder = targetItem == null ? _currentJob?.TakeoffsRoot : GetTakeoffPasteTargetFolder(targetItem);
        TakeoffsClipboardMode mode = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey
            ? TakeoffsClipboardMode.Copy
            : TakeoffsClipboardMode.Cut;
        if (CanDropTakeoffsInto(payload, targetFolder, mode))
            RunTakeoffDrop(payload, targetFolder!, mode);
        e.Handled = true;
    }

    private void TakeoffsTree_DragLeave(object sender, DragEventArgs e)
    {
        if (!TakeoffsTree.IsMouseOver)
        {
            ClearTakeoffSectionDropCue();
            ClearTakeoffPositionDropCue();
        }
    }

    private void TakeoffsTree_KeyDown(object sender, KeyEventArgs e)
    {
        if (TakeoffsTree.SelectedItem is not TreeViewItem item) return;
        if (item.Tag is TakeoffMeasurementNode sectionNode)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Up)
            {
                MoveTakeoffSections(sectionNode, -1);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Down)
            {
                MoveTakeoffSections(sectionNode, 1);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Delete)
            {
                DeleteTakeoffSections(sectionNode);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.F2 &&
                     SelectedTakeoffSectionNodes(sectionNode, fallbackToAnchor: true).Count <= 1)
            {
                RenameSection(sectionNode.Item, sectionNode.Measurement);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter)
            {
                SelectTakeoffSectionMeasurementsOnCanvas(SelectedTakeoffSectionNodes(sectionNode, fallbackToAnchor: true));
                e.Handled = true;
            }
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.C)
        {
            CopyCutTakeoffNode(item, TakeoffsClipboardMode.Copy);
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.X)
        {
            CopyCutTakeoffNode(item, TakeoffsClipboardMode.Cut);
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.V)
        {
            PasteIntoSelectedTakeoffTarget(item);
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.D)
        {
            DuplicateTakeoffNode(item);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Up)
        {
            MoveTakeoffNodes(item, -1);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Down)
        {
            MoveTakeoffNodes(item, 1);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Delete)
        {
            DeleteTakeoffNodes(item);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.F2 && TakeoffSelectionCount(item) <= 1)
        {
            if (item.Tag is TakeoffItem takeoff)
                RenameItem(item, takeoff);
            else if (item.Tag is TakeoffFolderNode folder && !folder.IsRoot)
                RenameTakeoffFolder(item, folder);
            e.Handled = true;
        }
    }

    private void CopyCutTakeoffNode(TreeViewItem item, TakeoffsClipboardMode mode)
    {
        var entries = GetSelectedTakeoffEntries(item);
        if (entries.Count == 0) return;

        _takeoffsClipboard = new TakeoffsClipboard(entries, mode);
        string verb = mode == TakeoffsClipboardMode.Copy ? "Copied" : "Cut";
        TxtStatus.Text = entries.Count == 1
            ? $"{verb}: {OurPlaneCoreJobStore.DisplayName(entries[0].SourcePath)}"
            : $"{verb} {entries.Count} takeoff nodes.";
    }

    private void PasteIntoSelectedTakeoffTarget(TreeViewItem item)
    {
        string? targetFolder = GetTakeoffPasteTargetFolder(item);
        if (targetFolder != null)
            PasteTakeoffsIntoFolder(targetFolder);
    }

    private void PasteTakeoffsIntoFolder(string targetFolder)
    {
        if (_takeoffsClipboard == null || !CanDropTakeoffsInto(_takeoffsClipboard, targetFolder, _takeoffsClipboard.Mode))
            return;

        RunTakeoffDrop(_takeoffsClipboard, targetFolder, _takeoffsClipboard.Mode);
    }

    private void DuplicateTakeoffNode(TreeViewItem item)
    {
        var entries = GetSelectedTakeoffEntries(item);
        if (entries.Count == 0) return;

        try
        {
            FlushTakeoffAutosaves();
            var changed = new List<string>();
            foreach (var entry in entries)
            {
                string? parent = Path.GetDirectoryName(entry.SourcePath);
                if (string.IsNullOrWhiteSpace(parent) ||
                    !CanDropTakeoffsInto(new TakeoffsClipboard([entry], TakeoffsClipboardMode.Copy), parent, TakeoffsClipboardMode.Copy))
                {
                    continue;
                }

                changed.Add(OurPlaneCoreJobStore.CopyNode(entry.SourcePath, parent));
            }

            if (changed.Count == 0)
                return;

            LoadTakeoffsForJob();
            SetTakeoffMultiSelection(changed);
            SelectFirstTakeoffPath(changed);
            TxtStatus.Text = changed.Count == 1
                ? $"Duplicated: {OurPlaneCoreJobStore.DisplayName(changed[0])}"
                : $"Duplicated {changed.Count} takeoff nodes.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Duplicate Takeoffs", ex);
        }
    }

    private void RunTakeoffDrop(TakeoffsClipboard payload, string targetFolder, TakeoffsClipboardMode mode)
    {
        bool wasCut = mode == TakeoffsClipboardMode.Cut;
        try
        {
            FlushTakeoffAutosaves();
            var changed = new List<string>();
            var rebasedLegendPaths = new List<(string OldPath, string NewPath)>();
            foreach (var entry in payload.Entries)
            {
                if (!CanDropTakeoffsInto(new TakeoffsClipboard([entry], mode), targetFolder, mode))
                    continue;

                string changedPath = wasCut
                    ? OurPlaneCoreJobStore.MoveNode(entry.SourcePath, targetFolder)
                    : OurPlaneCoreJobStore.CopyNode(entry.SourcePath, targetFolder);
                changed.Add(changedPath);
                if (wasCut)
                    rebasedLegendPaths.Add((entry.SourcePath, changedPath));
            }

            if (changed.Count == 0)
                return;

            if (wasCut)
                _takeoffsClipboard = null;

            foreach (var (oldPath, newPath) in rebasedLegendPaths)
                RebasePageLegendTakeoffOrderReferences(oldPath, newPath);

            LoadTakeoffsForJob();
            SetTakeoffMultiSelection(changed);
            SelectFirstTakeoffPath(changed);
            TxtStatus.Text = changed.Count == 1
                ? $"{(wasCut ? "Moved" : "Pasted")}: {OurPlaneCoreJobStore.DisplayName(changed[0])}"
                : $"{(wasCut ? "Moved" : "Pasted")} {changed.Count} takeoff nodes.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Takeoff Paste", ex);
        }
    }

    private bool TryGetTakeoffPositionDropCue(
        TakeoffsClipboard payload,
        TreeViewItem? targetItem,
        bool copy,
        DragEventArgs e,
        out bool after,
        out bool canDrop,
        out string status)
    {
        after = false;
        canDrop = false;
        status = "";

        if (copy || _currentJob == null || payload.Entries.Count == 0 || targetItem == null)
            return false;

        string? targetPath = GetTakeoffNodePath(targetItem);
        if (string.IsNullOrWhiteSpace(targetPath) || !Directory.Exists(targetPath))
            return false;

        Point targetPoint = e.GetPosition(targetItem);
        if (targetItem.Tag is TakeoffFolderNode && !IsTakeoffPositionEdgeDrop(targetItem, targetPoint))
            return false;

        after = IsTakeoffPositionDropAfter(targetItem, targetPoint);
        var paths = payload.Entries.Select(entry => entry.SourcePath).ToList();
        canDrop = CanDropTakeoffsToPosition(payload, targetPath, after);
        string targetName = OurPlaneCoreJobStore.DisplayName(targetPath);
        string position = after ? "after" : "before";
        status = canDrop
            ? $"Move {paths.Count} takeoff node(s) {position} {targetName}."
            : $"Cannot reorder here. Drag onto another sibling position in the same folder.";
        return true;
    }

    private bool CanDropTakeoffsToPosition(TakeoffsClipboard payload, string targetPath, bool after)
    {
        if (_currentJob == null || payload.Entries.Count == 0 || string.IsNullOrWhiteSpace(targetPath))
            return false;

        string targetParent = Path.GetDirectoryName(targetPath) ?? "";
        if (string.IsNullOrWhiteSpace(targetParent) ||
            !OurPlaneCoreJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, targetParent) ||
            !Directory.Exists(targetParent))
        {
            return false;
        }

        var paths = payload.Entries.Select(entry => entry.SourcePath).ToList();
        if (paths.Any(path => string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (paths.All(path => string.Equals(Path.GetDirectoryName(path) ?? "", targetParent, StringComparison.OrdinalIgnoreCase)))
            return OurPlaneCoreJobStore.CanMoveSiblingsToPosition(paths, targetPath, after);

        return CanDropTakeoffsInto(payload, targetParent, TakeoffsClipboardMode.Cut);
    }

    private static bool IsTakeoffPositionDropAfter(TreeViewItem item, Point targetPoint) =>
        targetPoint.Y >= TakeoffNodeHeaderDropHeight(item) / 2.0;

    private static bool IsTakeoffPositionEdgeDrop(TreeViewItem item, Point targetPoint)
    {
        double height = TakeoffNodeHeaderDropHeight(item);
        if (targetPoint.Y < 0 || targetPoint.Y > height)
            return false;

        double edge = Math.Min(8.0, Math.Max(5.0, height * 0.25));
        return targetPoint.Y <= edge || targetPoint.Y >= height - edge;
    }

    private static double TakeoffNodeHeaderDropHeight(TreeViewItem item)
    {
        double itemHeight = Math.Max(1.0, item.ActualHeight);
        if (item.Header is FrameworkElement header && header.ActualHeight > 0)
            return Math.Min(itemHeight, Math.Max(18.0, header.ActualHeight + 6.0));

        return Math.Min(itemHeight, 28.0);
    }

    private void UpdateTakeoffPositionDropCue(TreeViewItem? targetItem, bool after, bool canDrop, string status)
    {
        if (ReferenceEquals(_takeoffPositionDropTarget, targetItem) &&
            _takeoffPositionDropAfter == after &&
            _takeoffPositionDropAllowed == canDrop &&
            string.Equals(_takeoffPositionDropStatus, status, StringComparison.Ordinal))
        {
            return;
        }

        _takeoffPositionDropTarget = targetItem;
        _takeoffPositionDropAfter = after;
        _takeoffPositionDropAllowed = canDrop;
        _takeoffPositionDropStatus = status;
        ApplyTakeoffPageHighlights();
        if (!string.IsNullOrWhiteSpace(status))
            TxtStatus.Text = status;
    }

    private void ClearTakeoffPositionDropCue()
    {
        if (_takeoffPositionDropTarget == null && string.IsNullOrEmpty(_takeoffPositionDropStatus))
            return;

        _takeoffPositionDropTarget = null;
        _takeoffPositionDropAfter = false;
        _takeoffPositionDropAllowed = false;
        _takeoffPositionDropStatus = "";
        ApplyTakeoffPageHighlights();
    }

    private void DropTakeoffPosition(TakeoffsClipboard payload, TreeViewItem targetItem, bool after)
    {
        string? targetPath = GetTakeoffNodePath(targetItem);
        if (string.IsNullOrWhiteSpace(targetPath))
            return;

        var paths = payload.Entries.Select(entry => entry.SourcePath).ToList();
        if (paths.Count == 0)
            return;

        try
        {
            FlushTakeoffAutosaves();
            string targetParent = Path.GetDirectoryName(targetPath) ?? "";
            if (string.IsNullOrWhiteSpace(targetParent))
                return;

            var changed = new List<string>();
            var rebasedLegendPaths = new List<(string OldPath, string NewPath)>();
            if (paths.All(path => string.Equals(Path.GetDirectoryName(path) ?? "", targetParent, StringComparison.OrdinalIgnoreCase)))
            {
                if (!OurPlaneCoreJobStore.MoveSiblingsToPosition(paths, targetPath, after))
                    return;
                changed.AddRange(paths);
            }
            else
            {
                foreach (var entry in payload.Entries)
                {
                    if (string.Equals(Path.GetDirectoryName(entry.SourcePath) ?? "", targetParent, StringComparison.OrdinalIgnoreCase))
                    {
                        changed.Add(entry.SourcePath);
                        continue;
                    }

                    if (!CanDropTakeoffsInto(new TakeoffsClipboard([entry], TakeoffsClipboardMode.Cut), targetParent, TakeoffsClipboardMode.Cut))
                        continue;

                    string changedPath = OurPlaneCoreJobStore.MoveNode(entry.SourcePath, targetParent);
                    changed.Add(changedPath);
                    rebasedLegendPaths.Add((entry.SourcePath, changedPath));
                }

                if (changed.Count == 0 ||
                    !OurPlaneCoreJobStore.MoveSiblingsToPosition(changed, targetPath, after))
                {
                    return;
                }

                _takeoffsClipboard = null;
                foreach (var (oldPath, newPath) in rebasedLegendPaths)
                    RebasePageLegendTakeoffOrderReferences(oldPath, newPath);
            }

            LoadTakeoffsForJob();
            SetTakeoffMultiSelection(changed);
            SelectFirstTakeoffPath(changed);
            TxtStatus.Text = changed.Count == 1
                ? $"Moved takeoff node {(after ? "after" : "before")} {OurPlaneCoreJobStore.DisplayName(targetPath)}."
                : $"Moved {changed.Count} takeoff nodes {(after ? "after" : "before")} {OurPlaneCoreJobStore.DisplayName(targetPath)}.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Reorder Takeoffs", ex);
        }
    }

    private bool CanPasteTakeoffsInto(string? targetFolder) =>
        _takeoffsClipboard != null && CanDropTakeoffsInto(_takeoffsClipboard, targetFolder, _takeoffsClipboard.Mode);

    private bool CanDropTakeoffsInto(TakeoffsClipboard payload, string? targetFolder, TakeoffsClipboardMode mode)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(targetFolder) || payload.Entries.Count == 0)
            return false;
        if (!Directory.Exists(targetFolder))
            return false;

        if (!OurPlaneCoreJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, targetFolder) ||
            OurPlaneCoreJobStore.IsTakeoffItemFolder(targetFolder))
            return false;

        bool hasMovableEntry = false;
        foreach (var entry in payload.Entries)
        {
            if (!Directory.Exists(entry.SourcePath))
                return false;
            if (!OurPlaneCoreJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, entry.SourcePath) ||
                string.Equals(entry.SourcePath, _currentJob.TakeoffsRoot, StringComparison.OrdinalIgnoreCase))
                return false;

            if (OurPlaneCoreJobStore.IsSameOrDescendant(entry.SourcePath, targetFolder))
                return false;

            if (mode == TakeoffsClipboardMode.Cut)
            {
                string parent = Path.GetDirectoryName(entry.SourcePath) ?? "";
                if (string.Equals(parent, targetFolder, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            hasMovableEntry = true;
        }

        return mode == TakeoffsClipboardMode.Copy || hasMovableEntry;
    }

    private bool CanDropTakeoffSections(TakeoffSectionDrag payload, TreeViewItem? targetItem, bool copy)
    {
        if (payload.Nodes.Count == 0 || GetTakeoffSectionDropTarget(targetItem) is not { } target)
            return false;

        string targetType = OurPlaneCoreJobStore.NormalizeMeasurementType(target.MeasurementType);
        bool hasMovableNode = false;
        foreach (TakeoffMeasurementNode node in payload.Nodes)
        {
            if (!node.Item.Measurements.Contains(node.Measurement))
                return false;
            if (OurPlaneCoreJobStore.NormalizeMeasurementType(node.Measurement.MType) != targetType ||
                OurPlaneCoreJobStore.NormalizeMeasurementType(node.Item.MeasurementType) != targetType)
                return false;
            if (!ReferenceEquals(node.Item, target))
                hasMovableNode = true;
        }

        return copy || hasMovableNode;
    }

    private string TakeoffSectionDropStatus(TakeoffSectionDrag payload, TreeViewItem? targetItem, bool copy, bool canDrop)
    {
        if (payload.Nodes.Count == 0)
            return "Select section/count rows before dragging.";
        if (targetItem == null)
            return "Drop section/count rows on a takeoff item.";
        if (targetItem.Tag is TakeoffFolderNode)
            return "Drop section/count rows on a takeoff item, not a folder.";
        if (GetTakeoffSectionDropTarget(targetItem) is not { } target)
            return "Drop section/count rows on a takeoff item.";

        string action = copy ? "Copy" : "Move";
        string targetType = OurPlaneCoreJobStore.NormalizeMeasurementType(target.MeasurementType);
        TakeoffMeasurementNode? stale = payload.Nodes.FirstOrDefault(node => !node.Item.Measurements.Contains(node.Measurement));
        if (stale != null)
            return "Selected section/count row no longer exists.";

        TakeoffMeasurementNode? mismatch = payload.Nodes.FirstOrDefault(node =>
            OurPlaneCoreJobStore.NormalizeMeasurementType(node.Measurement.MType) != targetType ||
            OurPlaneCoreJobStore.NormalizeMeasurementType(node.Item.MeasurementType) != targetType);
        if (mismatch != null)
        {
            string sourceType = MeasurementTypeTitle(OurPlaneCoreJobStore.NormalizeMeasurementType(mismatch.Measurement.MType));
            string destinationType = MeasurementTypeTitle(targetType);
            return $"{action} blocked: {sourceType} rows can only drop on {sourceType} takeoff items, not {destinationType}.";
        }

        if (!copy && payload.Nodes.All(node => ReferenceEquals(node.Item, target)))
            return $"Already in {target.Name}. Hold Ctrl while dropping to copy.";

        return canDrop
            ? $"{action} {payload.Nodes.Count} section/count row(s) to {target.Name}."
            : $"Cannot {(copy ? "copy" : "move")} selected section/count rows to {target.Name}.";
    }

    private void UpdateTakeoffSectionDropCue(TakeoffSectionDrag payload, TreeViewItem? targetItem, bool copy, bool canDrop)
    {
        string status = TakeoffSectionDropStatus(payload, targetItem, copy, canDrop);
        if (ReferenceEquals(_takeoffSectionDropTarget, targetItem) &&
            _takeoffSectionDropAllowed == canDrop &&
            string.Equals(_takeoffSectionDropStatus, status, StringComparison.Ordinal))
        {
            return;
        }

        _takeoffSectionDropTarget = targetItem;
        _takeoffSectionDropAllowed = canDrop;
        _takeoffSectionDropStatus = status;
        ApplyTakeoffPageHighlights();
        if (!string.IsNullOrWhiteSpace(status))
            TxtStatus.Text = status;
    }

    private void ClearTakeoffSectionDropCue()
    {
        if (_takeoffSectionDropTarget == null && string.IsNullOrEmpty(_takeoffSectionDropStatus))
            return;

        _takeoffSectionDropTarget = null;
        _takeoffSectionDropAllowed = false;
        _takeoffSectionDropStatus = "";
        ApplyTakeoffPageHighlights();
    }

    private void DropTakeoffSections(TakeoffSectionDrag payload, TreeViewItem targetItem, bool copy)
    {
        if (!CanDropTakeoffSections(payload, targetItem, copy) ||
            GetTakeoffSectionDropTarget(targetItem) is not { } target)
        {
            return;
        }

        FlushTakeoffAutosaves();
        string targetType = OurPlaneCoreJobStore.NormalizeMeasurementType(target.MeasurementType);
        var changedItems = new HashSet<TakeoffItem>();
        var resultingNodes = new List<TakeoffMeasurementNode>();

        foreach (TakeoffMeasurementNode node in payload.Nodes
                     .GroupBy(node => node.Measurement.Id, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            if (copy)
            {
                Measurement copied = CloneMeasurementForTakeoff(node.Measurement, target, targetType);
                target.Measurements.Add(copied);
                changedItems.Add(target);
                resultingNodes.Add(new TakeoffMeasurementNode(target, copied));
                continue;
            }

            if (ReferenceEquals(node.Item, target))
                continue;

            if (!node.Item.Measurements.Remove(node.Measurement))
                continue;

            node.Measurement.TakeoffFolder = target.FolderPath;
            node.Measurement.MType = targetType;
            node.Measurement.Color = target.Color;
            target.Measurements.Add(node.Measurement);
            changedItems.Add(node.Item);
            changedItems.Add(target);
            resultingNodes.Add(new TakeoffMeasurementNode(target, node.Measurement));
        }

        if (resultingNodes.Count == 0)
            return;

        foreach (TakeoffItem changed in changedItems)
        {
            OurPlaneCoreJobStore.SaveTakeoffItem(changed);
            RefreshTreeItem(changed);
        }

        _viewport.LoadMeasurements(_takeoffItems.SelectMany(item => item.Measurements));
        CancelPendingTakeoffSelectionSync();
        SelectTakeoffSectionNodesSilently(resultingNodes);
        SelectTakeoffSectionMeasurementsOnCanvas(resultingNodes);
        RefreshPagesTakeoffIndicators();
        RefreshSheetLegend();
        RefreshEstimateTable();
        UpdateTotalDisplay();
        TxtStatus.Text = copy
            ? $"Copied {resultingNodes.Count} section/count row(s) to {target.Name}."
            : $"Moved {resultingNodes.Count} section/count row(s) to {target.Name}.";
    }

    private static Measurement CloneMeasurementForTakeoff(Measurement source, TakeoffItem target, string targetType) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            Name = source.Name,
            Notes = source.Notes,
            MType = targetType,
            Points = source.Points.ToList(),
            Color = target.Color,
            PageFolder = source.PageFolder,
            TakeoffFolder = target.FolderPath,
            ScaleMetersPerPt = source.ScaleMetersPerPt,
        };

    private static TakeoffItem? GetTakeoffSectionDropTarget(TreeViewItem? item) =>
        item?.Tag switch
        {
            TakeoffItem target => target,
            TakeoffMeasurementNode node => node.Item,
            _ => null,
        };

    private string? GetTakeoffPasteTargetFolder(TreeViewItem item)
    {
        return item.Tag switch
        {
            TakeoffFolderNode folder => folder.FolderPath,
            TakeoffItem takeoff => Path.GetDirectoryName(takeoff.FolderPath),
            _ => _currentJob?.TakeoffsRoot,
        };
    }

    private IReadOnlyList<TakeoffsClipboardEntry> GetSelectedTakeoffEntries(TreeViewItem anchor)
    {
        string? path = GetTakeoffNodePath(anchor);
        if (path == null || _currentJob == null || string.Equals(path, _currentJob.TakeoffsRoot, StringComparison.OrdinalIgnoreCase))
            return [];

        IEnumerable<string> paths = _takeoffsMultiSelection.Contains(path)
            ? _takeoffsMultiSelection
            : [path];

        var entries = paths
            .Where(Directory.Exists)
            .Where(candidate => _currentJob != null &&
                                OurPlaneCoreJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, candidate) &&
                                !string.Equals(candidate, _currentJob.TakeoffsRoot, StringComparison.OrdinalIgnoreCase))
            .Select(candidate => new TakeoffsClipboardEntry(
                candidate,
                OurPlaneCoreJobStore.IsTakeoffItemFolder(candidate)))
            .ToList();

        return NormalizeSelectedTakeoffEntries(entries);
    }

    private static string? GetTakeoffNodePath(TreeViewItem item) =>
        item.Tag switch
        {
            TakeoffItem takeoff => takeoff.FolderPath,
            TakeoffFolderNode folder => folder.IsRoot ? null : folder.FolderPath,
            _ => null,
        };

    private static string TakeoffSectionSelectionKey(TakeoffMeasurementNode node) =>
        $"{NormalizePath(node.Item.FolderPath)}|{node.Measurement.Id}";

    private static string? GetTakeoffSectionSelectionKey(TreeViewItem item) =>
        item.Tag is TakeoffMeasurementNode node ? TakeoffSectionSelectionKey(node) : null;

    private List<TakeoffMeasurementNode> SelectedTakeoffSectionNodes(TakeoffMeasurementNode anchor, bool fallbackToAnchor)
    {
        string anchorKey = TakeoffSectionSelectionKey(anchor);
        IEnumerable<string> keys = _takeoffSectionMultiSelection.Contains(anchorKey)
            ? _takeoffSectionMultiSelection
            : fallbackToAnchor
                ? [anchorKey]
                : Enumerable.Empty<string>();

        var keySet = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        if (keySet.Count == 0)
            return [];

        return EnumerateTakeoffTreeItems(TakeoffsTree)
            .Select(item => item.Tag as TakeoffMeasurementNode)
            .Where(node => node != null && keySet.Contains(TakeoffSectionSelectionKey(node)))
            .Select(node => node!)
            .ToList();
    }

    private void SelectTakeoffSectionRange(string? anchorKey, string targetKey, TakeoffItem item, bool additive)
    {
        var candidates = EnumerateVisibleTreeItems(TakeoffsTree)
            .Where(treeItem => treeItem.Tag is TakeoffMeasurementNode node && ReferenceEquals(node.Item, item))
            .Select(treeItem => (Item: treeItem, Key: GetTakeoffSectionSelectionKey(treeItem)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .Select(entry => (entry.Item, Key: entry.Key!))
            .ToList();

        SelectRangeKeys(candidates, anchorKey, targetKey, _takeoffSectionMultiSelection, additive);
    }

    private void SelectTakeoffSectionNodesSilently(IReadOnlyList<TakeoffMeasurementNode> nodes)
    {
        _takeoffsMultiSelection.Clear();
        _takeoffSectionMultiSelection.Clear();
        foreach (TakeoffMeasurementNode node in nodes)
            _takeoffSectionMultiSelection.Add(TakeoffSectionSelectionKey(node));

        TreeViewItem? first = nodes
            .Select(node => FindTakeoffSectionTreeItem(TakeoffsTree, node.Measurement))
            .FirstOrDefault(item => item != null);
        if (first != null)
        {
            _syncingTakeoffTreeSelection = true;
            try
            {
                TreeViewItem visibleTarget = TakeoffVisibleSelectionTarget(first);
                ExpandTakeoffFolderAncestorsWithoutTracking(visibleTarget);
                visibleTarget.IsSelected = true;
                visibleTarget.BringIntoView();
            }
            finally
            {
                _syncingTakeoffTreeSelection = false;
            }
        }

        ApplyTakeoffPageHighlights();
    }

    private static IReadOnlyList<TakeoffsClipboardEntry> NormalizeSelectedTakeoffEntries(
        IReadOnlyList<TakeoffsClipboardEntry> entries)
    {
        var distinct = entries
            .GroupBy(e => NormalizePath(e.SourcePath), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(e => NormalizePath(e.SourcePath).Length)
            .ToList();

        var result = new List<TakeoffsClipboardEntry>();
        foreach (var entry in distinct)
        {
            if (result.Any(parent => OurPlaneCoreJobStore.IsSameOrDescendant(parent.SourcePath, entry.SourcePath)))
                continue;
            result.Add(entry);
        }

        return result;
    }

    private int TakeoffSelectionCount(TreeViewItem anchor) =>
        GetSelectedTakeoffEntries(anchor).Count;

    private void SetTakeoffMultiSelection(IEnumerable<string> paths)
    {
        _takeoffsMultiSelection.Clear();
        _takeoffSectionMultiSelection.Clear();
        foreach (string path in paths.Where(Directory.Exists))
            _takeoffsMultiSelection.Add(path);
        ApplyTakeoffPageHighlights();
    }

    private void SelectFirstTakeoffPath(IReadOnlyList<string> paths)
    {
        foreach (string path in paths)
        {
            if (FindTakeoffTreeItemByFolder(path) is { } selected)
            {
                selected.IsSelected = true;
                selected.BringIntoView();
                return;
            }
        }
    }

    private void PruneTakeoffsMultiSelection()
    {
        if (_currentJob == null)
        {
            _takeoffsMultiSelection.Clear();
            return;
        }

        _takeoffsMultiSelection.RemoveWhere(path =>
            !Directory.Exists(path) ||
            !OurPlaneCoreJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, path) ||
            string.Equals(path, _currentJob.TakeoffsRoot, StringComparison.OrdinalIgnoreCase));
    }

    private void PruneTakeoffSectionMultiSelection()
    {
        var validKeys = _takeoffItems
            .SelectMany(item => item.Measurements.Select(measurement => TakeoffSectionSelectionKey(new TakeoffMeasurementNode(item, measurement))))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _takeoffSectionMultiSelection.RemoveWhere(key => !validKeys.Contains(key));
        if (_takeoffSectionRangeAnchorKey != null && !validKeys.Contains(_takeoffSectionRangeAnchorKey))
            _takeoffSectionRangeAnchorKey = null;
    }
}
