using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OurPlaneCore.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    // Takeoff selection, reveal, lookup, and removal helpers.

    private TreeViewItem? FindFirstTakeoffTreeItem(ItemsControl parent)
    {
        foreach (TreeViewItem tvi in parent.Items.OfType<TreeViewItem>())
        {
            if (tvi.Tag is TakeoffItem)
                return tvi;

            if (FindFirstTakeoffTreeItem(tvi) is { } found)
                return found;
        }

        return null;
    }

    private bool SelectFirstTakeoffItem()
    {
        if (FindFirstTakeoffTreeItem(TakeoffsTree) is not { } first)
        {
            _activeItem = null;
            _viewport.ActiveColor = "#FF4444";
            _viewport.ActiveTakeoffFolder = "";
            RefreshActiveTakeoffVisuals();
            return false;
        }

        first.IsSelected = true;
        first.BringIntoView();
        return true;
    }

    private void SelectTakeoffItem(TakeoffItem item)
    {
        if (FindTakeoffTreeItem(item) is { } tvi)
        {
            _takeoffsMultiSelection.Clear();
            _takeoffSectionMultiSelection.Clear();
            if (!string.IsNullOrWhiteSpace(item.FolderPath))
                _takeoffsMultiSelection.Add(item.FolderPath);
            ApplyTakeoffPageHighlights();
            tvi.IsSelected = true;
            tvi.BringIntoView();
        }
    }

    private void SelectTakeoffItemSilently(TakeoffItem item)
    {
        if (FindTakeoffTreeItem(item) is not { } tvi)
            return;

        _syncingTakeoffTreeSelection = true;
        try
        {
            _takeoffsMultiSelection.Clear();
            _takeoffSectionMultiSelection.Clear();
            if (!string.IsNullOrWhiteSpace(item.FolderPath))
                _takeoffsMultiSelection.Add(item.FolderPath);
            tvi.IsSelected = true;
            tvi.BringIntoView();
        }
        finally
        {
            _syncingTakeoffTreeSelection = false;
        }
    }

    private void SelectTakeoffItemsSilently(IReadOnlyList<TakeoffItem> items, TakeoffItem focusItem)
    {
        var paths = items
            .Select(item => item.FolderPath)
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (paths.Count == 0 || FindTakeoffTreeItem(focusItem) is not { } focusNode)
            return;

        _syncingTakeoffTreeSelection = true;
        try
        {
            _takeoffsMultiSelection.Clear();
            _takeoffSectionMultiSelection.Clear();
            foreach (string path in paths)
                _takeoffsMultiSelection.Add(path);
            ExpandTakeoffFolderAncestorsWithoutTracking(focusNode);
            focusNode.IsSelected = true;
            focusNode.BringIntoView();
        }
        finally
        {
            _syncingTakeoffTreeSelection = false;
        }

        ApplyTakeoffPageHighlights();
    }

    private void ActivateTakeoffItem(TakeoffItem item)
    {
        if (TryBlockTakeoffSwitchDuringRecord(item))
            return;

        item.MeasurementType = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
        _activeItem = item;
        _activeTakeoffParentFolder = Path.GetDirectoryName(item.FolderPath) ?? _currentJob?.TakeoffsRoot ?? "";
        _viewport.ActiveColor = item.Color;
        _viewport.ActiveTakeoffFolder = item.FolderPath;
        _viewport.ActiveCountSymbol = item.CountSymbol;
        SyncToolTypeForTakeoffItem(item);
    }

    private void SelectFirstTakeoffItemSilently(IReadOnlyList<TakeoffItem> items)
    {
        TreeViewItem? first = items
            .Select(FindTakeoffTreeItem)
            .FirstOrDefault(item => item != null);
        if (first == null)
            return;

        _syncingTakeoffTreeSelection = true;
        try
        {
            ExpandTakeoffFolderAncestorsWithoutTracking(first);
            first.IsSelected = true;
            first.BringIntoView();
        }
        finally
        {
            _syncingTakeoffTreeSelection = false;
        }
    }

    private IReadOnlyList<TakeoffItem> TakeoffItemsForSelection(TreeViewItem? anchor)
    {
        IReadOnlyList<TakeoffsClipboardEntry> entries = anchor == null
            ? []
            : GetSelectedTakeoffEntries(anchor);

        if (entries.Count == 0)
        {
            return anchor?.Tag switch
            {
                TakeoffItem item => [item],
                TakeoffMeasurementNode node => [node.Item],
                TakeoffFolderNode folder => TakeoffItemsInsideFolder(folder.FolderPath),
                _ => [],
            };
        }

        return _takeoffItems
            .Where(item => entries.Any(entry => OurPlaneCoreJobStore.IsSameOrDescendant(entry.SourcePath, item.FolderPath)))
            .GroupBy(item => NormalizePath(item.FolderPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private IReadOnlyList<TakeoffItem> TakeoffItemsInsideFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return [];

        return _takeoffItems
            .Where(item => OurPlaneCoreJobStore.IsSameOrDescendant(folderPath, item.FolderPath))
            .GroupBy(item => NormalizePath(item.FolderPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private void RevealPagesForTakeoffSelection(TreeViewItem? anchor, string? preferredPageFolder = null)
    {
        RevealPagesForTakeoffItems(TakeoffItemsForSelection(anchor), preferredPageFolder ?? _currentPage?.FolderPath);
    }

    private void RevealPagesForTakeoffItems(IReadOnlyList<TakeoffItem> items, string? preferredPageFolder = null)
    {
        HashSet<string> affectedPageKeys = PageTreePathKeysFromPageTakeoffSelection(_pageTakeoffMultiSelection);
        affectedPageKeys.UnionWith(_pagesMultiSelection.Select(NormalizePathForCompare));

        _pageTakeoffMultiSelection.Clear();
        _pagesMultiSelection.Clear();

        var selectedFolders = items
            .Select(item => item.FolderPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedFolders.Count == 0)
        {
            RefreshPageTreeRowsByFolderKeys(affectedPageKeys);
            return;
        }

        TreeViewItem? preferredLinked = null;
        TreeViewItem? firstLinked = null;
        var candidatePageFolders = items
            .SelectMany(item => item.Measurements.Select(measurement => measurement.PageFolder))
            .Where(pageFolder => !string.IsNullOrWhiteSpace(pageFolder))
            .GroupBy(NormalizePathForCompare, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        foreach (string pageFolder in candidatePageFolders)
        {
            if (FindPageTreeItemByFolder(pageFolder) is not { Tag: PageInfo page } pageItem)
                continue;

            var matchedTakeoffs = items
                .Where(item => selectedFolders.Contains(item.FolderPath) &&
                               item.Measurements.Any(measurement => IsSamePageFolder(measurement.PageFolder, page.FolderPath)))
                .ToList();
            if (matchedTakeoffs.Count == 0)
                continue;

            string pageKey = NormalizePathForCompare(page.FolderPath);
            affectedPageKeys.Add(pageKey);
            bool isPreferredPage = !string.IsNullOrWhiteSpace(preferredPageFolder) &&
                                   IsSamePageFolder(page.FolderPath, preferredPageFolder);

            foreach (TakeoffItem takeoff in matchedTakeoffs)
            {
                TreeViewItem? linked = FindPageTakeoffTreeItem(pageItem, takeoff.FolderPath);
                firstLinked ??= linked;
                if (isPreferredPage)
                    preferredLinked ??= linked;
            }
        }

        RefreshPageTreeRowsByFolderKeys(affectedPageKeys);
        preferredLinked ??= firstLinked;

        if (preferredLinked == null)
        {
            if (_currentPage != null)
                SelectPageTreeNodeSilently(_currentPage.FolderPath);
            return;
        }

        _syncingPageTreeSelection = true;
        try
        {
            ExpandTreeItemAndAncestorsWithoutTracking(preferredLinked);
            BringPageTreeItemIntoCenteredView(preferredLinked);
        }
        finally
        {
            _syncingPageTreeSelection = false;
        }
    }

    private void SelectTakeoffSectionNode(Measurement measurement)
    {
        if (!_settings.ShowTakeoffSectionsInTree)
        {
            if (FindTakeoffItemForMeasurement(measurement) is { } item)
            {
                SelectTakeoffItemSilently(item);
                ApplyTakeoffPageHighlights();
            }
            return;
        }

        if (FindTakeoffSectionTreeItem(TakeoffsTree, measurement) is not { } tvi)
            return;

        _syncingTakeoffTreeSelection = true;
        try
        {
            TreeViewItem visibleTarget = TakeoffVisibleSelectionTarget(tvi);
            ExpandTakeoffFolderAncestorsWithoutTracking(visibleTarget);
            visibleTarget.IsSelected = true;
            visibleTarget.BringIntoView();
        }
        finally
        {
            _syncingTakeoffTreeSelection = false;
        }
    }

    private void ExpandTakeoffFolderAncestorsWithoutTracking(TreeViewItem item)
    {
        WithTreeExpansionTrackingSuppressed(() =>
        {
            ItemsControl? parent = ItemsControl.ItemsControlFromItemContainer(item);
            while (parent is TreeViewItem parentItem)
            {
                if (parentItem.Tag is not TakeoffItem)
                    parentItem.IsExpanded = true;
                parent = ItemsControl.ItemsControlFromItemContainer(parentItem);
            }
        });
    }

    private static TreeViewItem TakeoffVisibleSelectionTarget(TreeViewItem item)
    {
        if (item.Tag is TakeoffMeasurementNode &&
            ItemsControl.ItemsControlFromItemContainer(item) is TreeViewItem parentTakeoff &&
            !parentTakeoff.IsExpanded)
        {
            return parentTakeoff;
        }

        return item;
    }

    private TreeViewItem? FindTakeoffTreeItem(TakeoffItem item) =>
        !string.IsNullOrWhiteSpace(item.FolderPath) &&
        FindTakeoffTreeItemByFolder(item.FolderPath) is { } indexedItem
            ? indexedItem
            : FindTakeoffTreeItem(TakeoffsTree, item);

    private TreeViewItem? FindTakeoffTreeItem(ItemsControl parent, TakeoffItem item)
    {
        foreach (TreeViewItem tvi in parent.Items.OfType<TreeViewItem>())
        {
            if (tvi.Tag is TakeoffItem candidate && ReferenceEquals(candidate, item))
                return tvi;

            if (FindTakeoffTreeItem(tvi, item) is { } found)
                return found;
        }

        return null;
    }

    private TreeViewItem? FindTakeoffSectionTreeItem(ItemsControl parent, Measurement measurement)
    {
        foreach (TreeViewItem tvi in parent.Items.OfType<TreeViewItem>())
        {
            if (tvi.Tag is TakeoffMeasurementNode node && ReferenceEquals(node.Measurement, measurement))
                return tvi;

            if (FindTakeoffSectionTreeItem(tvi, measurement) is { } found)
                return found;
        }

        return null;
    }

    private TreeViewItem? FindTakeoffTreeItemByFolder(string folderPath) =>
        FindTakeoffTreeItemByFolderIndexed(folderPath);

    private void SelectTakeoffNodeByFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return;

        if (FindTakeoffTreeItemByFolder(folderPath) is { } item)
        {
            item.IsSelected = true;
            item.IsExpanded = true;
            item.BringIntoView();
        }
    }

    private TreeViewItem? FindTakeoffTreeItemByFolder(ItemsControl parent, string folderPath)
    {
        foreach (TreeViewItem tvi in parent.Items.OfType<TreeViewItem>())
        {
            string? current = tvi.Tag switch
            {
                TakeoffItem item => item.FolderPath,
                TakeoffFolderNode folder => folder.FolderPath,
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(current) &&
                string.Equals(current, folderPath, StringComparison.OrdinalIgnoreCase))
            {
                return tvi;
            }

            if (FindTakeoffTreeItemByFolder(tvi, folderPath) is { } found)
                return found;
        }

        return null;
    }

    private void RemoveTreeItem(TreeViewItem tvi)
    {
        UnregisterTakeoffTreeItemSubtree(tvi);
        ItemsControl? parent = ItemsControl.ItemsControlFromItemContainer(tvi);
        parent?.Items.Remove(tvi);
    }
}
