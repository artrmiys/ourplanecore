using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private static readonly bool FastTakeoffsTreeRefreshEnabled = false;

    private bool TryRefreshTakeoffTreeParentOrderFast(
        string parentFolder,
        IEnumerable<string> selectedPaths,
        bool refreshSheetLegend = false,
        bool refreshPageIndicators = false)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(parentFolder))
            return false;

        if (TakeoffTreeItemsParentControl(parentFolder) is not { } parent)
            return false;

        var existingItems = parent.Items.OfType<TreeViewItem>().ToList();
        var byPath = new Dictionary<string, TreeViewItem>(StringComparer.OrdinalIgnoreCase);
        foreach (TreeViewItem item in existingItems)
        {
            string? path = GetTakeoffNodePath(item);
            if (string.IsNullOrWhiteSpace(path))
                return false;
            byPath[NormalizePath(path)] = item;
        }

        var orderedPaths = OurPlaneCoreJobStore.GetOrderedChildDirectories(parentFolder)
            .Select(NormalizePath)
            .ToList();
        if (orderedPaths.Count != existingItems.Count ||
            orderedPaths.Any(path => !byPath.ContainsKey(path)))
        {
            return false;
        }

        parent.Items.Clear();
        foreach (string path in orderedPaths)
            parent.Items.Add(byPath[path]);

        parent.Items.Refresh();
        RefreshTakeoffMoveSelectionFast(selectedPaths, refreshSheetLegend, refreshPageIndicators);
        return true;
    }

    private bool TryApplyTakeoffStructureMoveFast(
        IReadOnlyList<(string OldPath, string NewPath)> movedPaths,
        string targetParent,
        IEnumerable<string> selectedPaths)
    {
        if (!FastTakeoffsTreeRefreshEnabled)
            return false;

        if (_currentJob == null || movedPaths.Count == 0)
            return false;

        if (TakeoffTreeItemsParentControl(targetParent) is not { } targetControl)
            return false;

        var moves = new List<(TreeViewItem Item, ItemsControl OldParent, string OldPath, string NewPath)>();
        foreach ((string oldPath, string newPath) in movedPaths)
        {
            if (FindTakeoffTreeItemByFolder(oldPath) is not { } item)
                return false;

            ItemsControl? oldParent = ItemsControl.ItemsControlFromItemContainer(item);
            if (oldParent == null)
                return false;

            moves.Add((item, oldParent, oldPath, newPath));
        }

        foreach ((TreeViewItem item, ItemsControl oldParent, string oldPath, string newPath) in moves)
        {
            UnregisterTakeoffTreeItemSubtree(item);
            oldParent.Items.Remove(item);
            RebaseTakeoffTreeItemPath(item, oldPath, newPath);
            targetControl.Items.Add(item);
            RegisterTakeoffTreeItemSubtree(item);
        }

        return TryRefreshTakeoffTreeParentOrderFast(
            targetParent,
            selectedPaths,
            refreshSheetLegend: true,
            refreshPageIndicators: true);
    }

    private bool TryApplyTakeoffStructureCopyFast(
        IReadOnlyList<string> copiedPaths,
        TakeoffDropTimings? timings = null)
    {
        if (_currentJob == null || copiedPaths.Count == 0)
            return false;

        var loadWatch = Stopwatch.StartNew();
        var paths = copiedPaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (paths.Count != copiedPaths.Count)
            return false;

        var loadedItems = new List<TakeoffItem>();
        var additions = new List<(ItemsControl Parent, TreeViewItem Node)>();
        foreach (string path in paths)
        {
            if (FindTakeoffTreeItemByFolder(path) != null)
                return false;

            string parentFolder = Path.GetDirectoryName(path) ?? "";
            if (TakeoffTreeItemsParentControl(parentFolder) is not { } parent)
                return false;

            if (!TryCreateTakeoffTreeItemForExistingFolder(path, loadedItems, out TreeViewItem? node))
                return false;

            additions.Add((parent, node!));
        }
        loadWatch.Stop();
        if (timings != null)
            timings.CopyLoadMilliseconds = loadWatch.ElapsedMilliseconds;

        foreach (TakeoffItem item in loadedItems)
            _takeoffItems.Add(item);

        var appendWatch = Stopwatch.StartNew();
        foreach ((ItemsControl parent, TreeViewItem node) in additions)
        {
            if (parent is TreeViewItem parentItem)
                parentItem.IsExpanded = true;
            parent.Items.Add(node);
            RegisterTakeoffTreeItemSubtree(node);
        }
        appendWatch.Stop();
        if (timings != null)
            timings.CopyAppendMilliseconds = appendWatch.ElapsedMilliseconds;

        var viewportWatch = Stopwatch.StartNew();
        _viewport.SetMeasurements(_takeoffItems.SelectMany(item => item.Measurements));
        viewportWatch.Stop();
        if (timings != null)
            timings.CopyViewportMilliseconds = viewportWatch.ElapsedMilliseconds;

        RefreshTakeoffCopySelectionFast(paths, timings);
        return true;
    }

    private bool TryCreateTakeoffTreeItemForExistingFolder(
        string folder,
        List<TakeoffItem> loadedItems,
        out TreeViewItem? node)
    {
        node = null;
        if (!Directory.Exists(folder))
            return false;

        if (OurPlaneCoreJobStore.TryReadTakeoffItem(folder) is { } item)
        {
            loadedItems.Add(item);
            node = CreateTakeoffTreeItem(item);
            return true;
        }

        var folderNode = new TakeoffFolderNode
        {
            Name = OurPlaneCoreJobStore.DisplayName(folder),
            FolderPath = folder,
        };
        var folderItem = CreateTakeoffFolderTreeItem(folderNode);
        foreach (TreeViewItem child in BuildTakeoffChildren(folder, loadedItems))
            folderItem.Items.Add(child);

        node = folderItem;
        return true;
    }

    private void RefreshTakeoffCopySelectionFast(
        IReadOnlyList<string> copiedPaths,
        TakeoffDropTimings? timings = null)
    {
        var selectionWatch = Stopwatch.StartNew();
        string? previousActivePath = _activeItem?.FolderPath;
        if (!TrySetTakeoffMultiSelectionFast(copiedPaths))
            SetTakeoffMultiSelection(copiedPaths);

        TreeViewItem? selected = SelectFirstTakeoffPathForMoveFast(copiedPaths);
        RefreshFastMoveActiveState(selected, previousActivePath);
        selectionWatch.Stop();
        if (timings != null)
            timings.CopySelectionMilliseconds = selectionWatch.ElapsedMilliseconds;

        var pagesWatch = Stopwatch.StartNew();
        RefreshPageTakeoffIndicatorsForTakeoffPaths(copiedPaths);
        pagesWatch.Stop();
        if (timings != null)
            timings.CopyPageIndicatorsMilliseconds = pagesWatch.ElapsedMilliseconds;

        var legendWatch = Stopwatch.StartNew();
        RefreshSheetLegend();
        legendWatch.Stop();
        if (timings != null)
            timings.CopyLegendMilliseconds = legendWatch.ElapsedMilliseconds;

        var estimateWatch = Stopwatch.StartNew();
        RefreshEstimateTable();
        estimateWatch.Stop();
        if (timings != null)
            timings.CopyEstimateMilliseconds = estimateWatch.ElapsedMilliseconds;

        var totalWatch = Stopwatch.StartNew();
        UpdateTotalDisplay(refreshEstimate: false);
        totalWatch.Stop();
        if (timings != null)
            timings.CopyTotalMilliseconds = totalWatch.ElapsedMilliseconds;
    }

    private ItemsControl? TakeoffTreeItemsParentControl(string parentFolder)
    {
        if (_currentJob == null)
            return null;

        return string.Equals(parentFolder, _currentJob.TakeoffsRoot, StringComparison.OrdinalIgnoreCase)
            ? TakeoffsTree
            : FindTakeoffTreeItemByFolder(parentFolder);
    }

    private void RebaseTakeoffTreeItemPath(TreeViewItem item, string oldRoot, string newRoot)
    {
        if (item.Tag is TakeoffItem takeoff)
        {
            RebaseTakeoffItemPath(takeoff, oldRoot, newRoot);
            SetTreeItemHeader(item, takeoff);
            AttachContextMenu(item, takeoff);
        }
        else if (item.Tag is TakeoffFolderNode folder &&
                 OurPlaneCoreJobStore.IsSameOrDescendant(oldRoot, folder.FolderPath))
        {
            string folderPath = RebaseTakeoffPath(folder.FolderPath, oldRoot, newRoot);
            var updated = new TakeoffFolderNode
            {
                Name = OurPlaneCoreJobStore.DisplayName(folderPath),
                FolderPath = folderPath,
                IsRoot = folder.IsRoot,
            };
            item.Tag = updated;
            SetFolderTreeItemHeader(item, updated);
            AttachFolderContextMenu(item, updated);
        }

        foreach (TreeViewItem child in item.Items.OfType<TreeViewItem>())
            RebaseTakeoffTreeItemPath(child, oldRoot, newRoot);

        if (!string.IsNullOrWhiteSpace(_activeTakeoffParentFolder) &&
            OurPlaneCoreJobStore.IsSameOrDescendant(oldRoot, _activeTakeoffParentFolder))
        {
            _activeTakeoffParentFolder = RebaseTakeoffPath(_activeTakeoffParentFolder, oldRoot, newRoot);
        }
    }

    private static void RebaseTakeoffItemPath(TakeoffItem item, string oldRoot, string newRoot)
    {
        if (!OurPlaneCoreJobStore.IsSameOrDescendant(oldRoot, item.FolderPath))
            return;

        item.FolderPath = RebaseTakeoffPath(item.FolderPath, oldRoot, newRoot);
        item.Name = OurPlaneCoreJobStore.DisplayName(item.FolderPath);
        foreach (Measurement measurement in item.Measurements)
            measurement.TakeoffFolder = item.FolderPath;
    }

    private static string RebaseTakeoffPath(string path, string oldRoot, string newRoot)
    {
        string relative = Path.GetRelativePath(oldRoot, path);
        return relative == "."
            ? newRoot
            : Path.Combine(newRoot, relative);
    }

    private void RefreshTakeoffMoveSelectionFast(
        IEnumerable<string> selectedPaths,
        bool refreshSheetLegend,
        bool refreshPageIndicators)
    {
        var paths = selectedPaths.ToList();
        string? previousActivePath = _activeItem?.FolderPath;
        if (!TrySetTakeoffMultiSelectionFast(paths))
            SetTakeoffMultiSelection(paths);
        TreeViewItem? selected = SelectFirstTakeoffPathForMoveFast(paths);
        RefreshFastMoveActiveState(selected, previousActivePath);
        if (refreshPageIndicators)
            RefreshPageTakeoffIndicatorsForTakeoffPaths(paths);
        if (refreshSheetLegend)
            RefreshSheetLegend();
        UpdateActiveTakeoffTargetBar();
    }

    private void ReloadTakeoffsForMoveSelection(
        IReadOnlyList<string> selectedPaths,
        string? previousActivePath)
    {
        LoadTakeoffsForJob();
        SetTakeoffMultiSelection(selectedPaths);
        TreeViewItem? selected = SelectFirstTakeoffPathForMoveFast(selectedPaths);
        RefreshFastMoveActiveState(selected, previousActivePath);
    }

    private void RefreshFastMoveActiveState(TreeViewItem? selected, string? previousActivePath)
    {
        if (selected?.Tag is TakeoffItem item)
        {
            if (!TryBlockTakeoffSwitchDuringRecord(item))
                ActivateTakeoffItem(item);
        }
        else if (selected?.Tag is TakeoffFolderNode folder)
        {
            _activeItem = null;
            _activeTakeoffParentFolder = folder.FolderPath;
            _viewport.ActiveTakeoffFolder = "";
            UpdateToolStatus();
        }
        else if (_activeItem != null)
        {
            _activeTakeoffParentFolder = Path.GetDirectoryName(_activeItem.FolderPath) ?? _currentJob?.TakeoffsRoot ?? "";
            _viewport.ActiveTakeoffFolder = _activeItem.FolderPath;
            _viewport.ActiveCountSymbol = _activeItem.CountSymbol;
        }

        var visualPaths = new List<string>();
        if (!string.IsNullOrWhiteSpace(previousActivePath))
            visualPaths.Add(previousActivePath);
        if (_activeItem != null && !string.IsNullOrWhiteSpace(_activeItem.FolderPath))
            visualPaths.Add(_activeItem.FolderPath);
        if (selected != null && GetTakeoffNodePath(selected) is { } selectedPath)
            visualPaths.Add(selectedPath);

        RefreshTakeoffHeadersByPath(visualPaths);
        RefreshTakeoffTreeRowsByPath(visualPaths);
    }

    private void RefreshTakeoffHeadersByPath(IEnumerable<string> paths)
    {
        foreach (TreeViewItem item in paths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Select(FindTakeoffTreeItemByFolder)
                     .Where(item => item != null)
                     .Distinct()
                     .Cast<TreeViewItem>())
        {
            if (item.Tag is TakeoffItem takeoff)
                SetTreeItemHeader(item, takeoff);
        }
    }

    private void RefreshPageTakeoffIndicatorsForTakeoffPaths(IEnumerable<string> takeoffPaths)
    {
        var roots = takeoffPaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (roots.Count == 0)
            return;

        var pageFolders = _takeoffItems
            .Where(item => !string.IsNullOrWhiteSpace(item.FolderPath) &&
                           roots.Any(root => OurPlaneCoreJobStore.IsSameOrDescendant(root, item.FolderPath)))
            .SelectMany(item => item.Measurements.Select(measurement => measurement.PageFolder))
            .Where(pageFolder => !string.IsNullOrWhiteSpace(pageFolder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (pageFolders.Count == 0)
        {
            ApplyPagesMultiSelectionVisuals();
            return;
        }

        RefreshPageTakeoffIndicatorsForFoldersOrDefer(pageFolders);
    }
}
