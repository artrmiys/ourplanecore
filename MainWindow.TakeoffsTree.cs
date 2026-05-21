using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace OurPlaneCore;

public partial class MainWindow
{
    // ── Takeoffs tree ─────────────────────────────────────────────────────────

    private void TakeoffsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_syncingTakeoffTreeSelection)
            return;

        if (e.NewValue is TreeViewItem selectedItem &&
            TryBlockTakeoffTreeSelectionDuringRecord(selectedItem))
        {
            e.Handled = true;
            return;
        }

        CancelPendingTakeoffSelectionSync();
        string? previousActiveTakeoffFolder = _activeItem?.FolderPath;

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
            TreeViewItem? anchor = e.NewValue as TreeViewItem;
            var selectedTakeoffs = TakeoffItemsForSelection(anchor);
            _takeoffSectionMultiSelection.Clear();
            item.MeasurementType = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
            _activeItem           = item;
            _activeTakeoffParentFolder = Path.GetDirectoryName(item.FolderPath) ?? _currentJob?.TakeoffsRoot ?? "";
            _viewport.ActiveColor = item.Color;
            _viewport.ActiveTakeoffFolder = item.FolderPath;
            _viewport.ActiveCountSymbol = item.CountSymbol;
            SyncToolTypeForTakeoffItem(item);
            RefreshPageTakeoffIndicatorsForActiveChange(previousActiveTakeoffFolder, selectedTakeoffs);
            RefreshActiveTakeoffVisualsForPaths(
                TakeoffVisualPathsForActiveChange(previousActiveTakeoffFolder, selectedTakeoffs)
                    .Append(item.FolderPath));
            RevealPagesForTakeoffItems(selectedTakeoffs, _currentPage?.FolderPath);
            ScheduleTakeoffSelectionSync(() => SelectTakeoffSelectionMeasurementsOnCurrentPage(anchor));
            UpdateTotalDisplay(refreshEstimate: false);
        }
        else if (e.NewValue is TreeViewItem { Tag: TakeoffFolderNode folder })
        {
            TreeViewItem? anchor = e.NewValue as TreeViewItem;
            var selectedTakeoffs = TakeoffItemsForSelection(anchor);
            _takeoffSectionMultiSelection.Clear();
            _activeItem = null;
            _activeTakeoffParentFolder = folder.FolderPath;
            _viewport.ActiveTakeoffFolder = "";
            TxtStatus.Text = TakeoffFolderStatusText(folder);
            UpdateToolStatus();
            RefreshPageTakeoffIndicatorsForActiveChange(previousActiveTakeoffFolder, selectedTakeoffs);
            RefreshActiveTakeoffVisualsForPaths(
                TakeoffVisualPathsForActiveChange(previousActiveTakeoffFolder, selectedTakeoffs)
                    .Append(folder.FolderPath));
            RevealPagesForTakeoffItems(selectedTakeoffs, _currentPage?.FolderPath);
            ScheduleTakeoffSelectionSync(() => SelectTakeoffSelectionMeasurementsOnCurrentPage(anchor));
            UpdateTotalDisplay(refreshEstimate: false);
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
            node.Item.MeasurementType = OurPlaneCoreJobStore.NormalizeMeasurementType(node.Item.MeasurementType);
            _activeTakeoffParentFolder = Path.GetDirectoryName(node.Item.FolderPath) ?? _currentJob?.TakeoffsRoot ?? "";
            _viewport.ActiveColor = node.Item.Color;
            _viewport.ActiveTakeoffFolder = node.Item.FolderPath;
            _viewport.ActiveCountSymbol = node.Item.CountSymbol;
            if (_suppressCanvasFocusFromTakeoffSelection || Mouse.LeftButton == MouseButtonState.Pressed)
            {
                if (_currentPage != null && IsSamePageFolder(_currentPage.FolderPath, node.Measurement.PageFolder))
                    _viewport.SelectMeasurements([node.Measurement]);
            }
            else
            {
                SelectSectionOnCanvas(node.Measurement, suppressTakeoffSync: true);
            }
            SyncToolTypeForTakeoffItem(node.Item);
            RefreshPageTakeoffIndicatorsForActiveChange(previousActiveTakeoffFolder, [node.Item]);
            RefreshActiveTakeoffVisualsForPaths(
                TakeoffVisualPathsForActiveChange(previousActiveTakeoffFolder, [node.Item]));
            RevealPagesForTakeoffItems([node.Item], node.Measurement.PageFolder);
            UpdateTotalDisplay(refreshEstimate: false);
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

    private void TakeoffsTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is { } item)
        {
            if (TryBlockTakeoffTreeSelectionDuringRecord(item))
            {
                e.Handled = true;
                return;
            }

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

    private void TakeoffsTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _takeoffsDragStart = e.GetPosition(TakeoffsTree);
        _takeoffsDragItem = null;
        _takeoffsDragArmed = false;
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is not { } item)
        {
            ClearTakeoffFolderSelectionFromBlankClick();
            return;
        }

        if (TryRefreshStaleTakeoffTreeNode(item))
        {
            e.Handled = true;
            return;
        }

        if (TryBlockTakeoffTreeSelectionDuringRecord(item))
        {
            _takeoffsDragItem = null;
            _takeoffsDragArmed = false;
            e.Handled = true;
            return;
        }

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
        if (item.Tag is TakeoffMeasurementNode)
            return true;

        string? path = GetTakeoffNodePath(item);
        return path != null && Directory.Exists(path);
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

    private void ClearTakeoffFolderSelectionFromBlankClick()
    {
        if (TakeoffsTree.SelectedItem is not TreeViewItem { Tag: TakeoffFolderNode } selectedFolder)
            return;

        _syncingTakeoffTreeSelection = true;
        try
        {
            selectedFolder.IsSelected = false;
        }
        finally
        {
            _syncingTakeoffTreeSelection = false;
        }

        _takeoffsMultiSelection.Clear();
        _takeoffSectionMultiSelection.Clear();
        _takeoffsRangeAnchorPath = null;
        _activeItem = null;
        _activeTakeoffParentFolder = _currentJob?.TakeoffsRoot ?? "";
        _viewport.ActiveTakeoffFolder = "";
        ApplyTakeoffPageHighlights();
        TxtStatus.Text = "Takeoffs folder selection cleared. New Item will use the Takeoffs root.";
        UpdateToolStatus();
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

}
