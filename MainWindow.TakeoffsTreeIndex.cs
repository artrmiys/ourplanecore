using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    private readonly Dictionary<string, TreeViewItem> _takeoffTreeItemsByPath = new(StringComparer.OrdinalIgnoreCase);
    private bool _takeoffTreeItemIndexReady = true;

    private void ResetTakeoffTreeItemIndex()
    {
        _takeoffTreeItemsByPath.Clear();
        _takeoffTreeItemIndexReady = true;
    }

    private void InvalidateTakeoffTreeItemIndex() =>
        _takeoffTreeItemIndexReady = false;

    private TreeViewItem? FindTakeoffTreeItemByFolderIndexed(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return null;

        string key = NormalizePath(folderPath);
        if (!_takeoffTreeItemIndexReady)
            RebuildTakeoffTreeItemIndex();

        if (TryGetIndexedTakeoffTreeItem(key, out TreeViewItem? item))
            return item;

        RebuildTakeoffTreeItemIndex();
        return TryGetIndexedTakeoffTreeItem(key, out item) ? item : null;
    }

    private bool TryGetIndexedTakeoffTreeItem(string key, out TreeViewItem? item)
    {
        item = null;
        if (!_takeoffTreeItemsByPath.TryGetValue(key, out TreeViewItem? candidate))
            return false;

        string? currentPath = GetTakeoffNodePath(candidate);
        if (string.IsNullOrWhiteSpace(currentPath) ||
            !string.Equals(NormalizePath(currentPath), key, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        item = candidate;
        return true;
    }

    private void RebuildTakeoffTreeItemIndex()
    {
        _takeoffTreeItemsByPath.Clear();
        foreach (TreeViewItem item in EnumerateTakeoffTreeItems(TakeoffsTree))
            RegisterTakeoffTreeItem(item);
        _takeoffTreeItemIndexReady = true;
    }

    private void RegisterTakeoffTreeItemSubtree(TreeViewItem item)
    {
        RegisterTakeoffTreeItem(item);
        foreach (TreeViewItem child in item.Items.OfType<TreeViewItem>())
            RegisterTakeoffTreeItemSubtree(child);
        _takeoffTreeItemIndexReady = true;
    }

    private void RegisterTakeoffTreeItem(TreeViewItem item)
    {
        string? path = GetTakeoffNodePath(item);
        if (!string.IsNullOrWhiteSpace(path))
            _takeoffTreeItemsByPath[NormalizePath(path)] = item;
    }

    private void UnregisterTakeoffTreeItemSubtree(TreeViewItem item)
    {
        foreach (TreeViewItem child in item.Items.OfType<TreeViewItem>())
            UnregisterTakeoffTreeItemSubtree(child);

        string? path = GetTakeoffNodePath(item);
        if (string.IsNullOrWhiteSpace(path))
            return;

        UnregisterTakeoffTreeItemPath(path, item);
    }

    private void UnregisterTakeoffTreeItemPath(string path, TreeViewItem item)
    {
        string key = NormalizePath(path);
        if (_takeoffTreeItemsByPath.TryGetValue(key, out TreeViewItem? existing) &&
            ReferenceEquals(existing, item))
        {
            _takeoffTreeItemsByPath.Remove(key);
        }
    }
}
