using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
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

    private bool TryRefreshStaleTakeoffTreeNode(TreeViewItem item)
    {
        if (_currentJob == null)
            return false;

        string? path = TakeoffTreeNodeStoragePath(item);
        if (string.IsNullOrWhiteSpace(path) || Directory.Exists(path))
            return false;

        if (!OurPlaneCoreJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, path))
            return false;

        AppLog.Warn($"Takeoffs tree row referenced missing path '{path}'. Reloading tree from disk.");
        LoadTakeoffsForJob();
        TxtStatus.Text = "Takeoffs tree was out of sync after a move and was refreshed. Drag again.";
        return true;
    }

    private static string? TakeoffTreeNodeStoragePath(TreeViewItem item) =>
        item.Tag switch
        {
            TakeoffItem takeoff => takeoff.FolderPath,
            TakeoffFolderNode folder => folder.IsRoot ? null : folder.FolderPath,
            TakeoffMeasurementNode node => node.Item.FolderPath,
            _ => null,
        };

    private static string? GetTakeoffNodePath(TreeViewItem item) =>
        item.Tag switch
        {
            TakeoffItem takeoff => takeoff.FolderPath,
            TakeoffFolderNode folder => folder.IsRoot ? null : folder.FolderPath,
            _ => null,
        };

    private static string TakeoffSectionSelectionKey(TakeoffMeasurementNode node)
    {
        string itemKey = string.IsNullOrWhiteSpace(node.Item.FolderPath)
            ? $"legacy:{TakeoffSectionLegacyItemKey(node.Item)}"
            : NormalizePath(node.Item.FolderPath);
        return $"{itemKey}|{node.Measurement.Id}";
    }

    private static string TakeoffSectionLegacyItemKey(TakeoffItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Id))
            return item.Id;
        if (!string.IsNullOrWhiteSpace(item.Name))
            return item.Name;
        return "unfiled";
    }

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

    private bool TrySetTakeoffMultiSelectionFast(IEnumerable<string> paths)
    {
        if (_takeoffSectionMultiSelection.Count > 0)
            return false;

        var cleanPaths = paths
            .Where(Directory.Exists)
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var affectedPaths = _takeoffsMultiSelection
            .Concat(cleanPaths)
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _takeoffsMultiSelection.Clear();
        foreach (string path in cleanPaths)
            _takeoffsMultiSelection.Add(path);

        RefreshTakeoffTreeRowsByPath(affectedPaths);
        return true;
    }

    private void RefreshTakeoffTreeRowsByPath(IEnumerable<string> paths)
    {
        TreeViewItem?[] items = paths
            .Select(FindTakeoffTreeItemByFolder)
            .Where(item => item != null)
            .Distinct()
            .ToArray();
        RefreshTakeoffDropCueRows(items);
    }

    private void RefreshActiveTakeoffVisualsForPaths(IEnumerable<string?> paths)
    {
        var cleanPaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => NormalizePath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (cleanPaths.Count > 0)
        {
            RefreshTakeoffHeadersByPath(cleanPaths);
            RefreshTakeoffTreeRowsByPath(cleanPaths);
        }

        UpdateActiveTakeoffTargetBar();
    }

    private static IEnumerable<string?> TakeoffVisualPathsForActiveChange(
        string? previousActiveTakeoffFolder,
        IEnumerable<TakeoffItem> selectedTakeoffs)
    {
        yield return previousActiveTakeoffFolder;
        foreach (TakeoffItem takeoff in selectedTakeoffs)
            yield return takeoff.FolderPath;
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

    private TreeViewItem? SelectFirstTakeoffPathForMoveFast(IReadOnlyList<string> paths)
    {
        var pathSet = paths
            .Where(Directory.Exists)
            .Select(NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (pathSet.Count == 0)
            return null;

        if (TakeoffsTree.SelectedItem is TreeViewItem selectedItem &&
            GetTakeoffNodePath(selectedItem) is { } selectedPath &&
            pathSet.Contains(NormalizePath(selectedPath)))
        {
            selectedItem.BringIntoView();
            return selectedItem;
        }

        foreach (string path in paths)
        {
            if (FindTakeoffTreeItemByFolder(path) is not { } selected)
                continue;

            _syncingTakeoffTreeSelection = true;
            try
            {
                selected.IsSelected = true;
                selected.BringIntoView();
            }
            finally
            {
                _syncingTakeoffTreeSelection = false;
            }
            return selected;
        }

        return null;
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
