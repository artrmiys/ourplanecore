using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    private readonly Dictionary<string, TreeViewItem> _pageTreeItemsByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TreeViewItem> _pageTakeoffTreeItemsByKey = new(StringComparer.OrdinalIgnoreCase);
    private bool _pageTreeItemIndexReady = true;

    private void ResetPageTreeItemIndex()
    {
        _pageTreeItemsByPath.Clear();
        _pageTakeoffTreeItemsByKey.Clear();
        _pageTreeItemIndexReady = true;
    }

    private void RebuildPageTreeItemIndex()
    {
        _pageTreeItemsByPath.Clear();
        _pageTakeoffTreeItemsByKey.Clear();
        foreach (TreeViewItem item in EnumeratePageTreeItems())
            RegisterPageTreeItem(item);
        _pageTreeItemIndexReady = true;
    }

    private void RegisterPageTreeItem(TreeViewItem item)
    {
        string? path = GetPagesNodePath(item);
        if (!string.IsNullOrWhiteSpace(path))
            _pageTreeItemsByPath[NormalizePathForCompare(path)] = item;

        if (item.Tag is PageTakeoffNode node)
        {
            string key = PageTakeoffSelectionKey(node);
            if (!string.IsNullOrWhiteSpace(key))
                _pageTakeoffTreeItemsByKey[key] = item;
        }
    }

    private void RegisterPageTreeItemSubtree(TreeViewItem item)
    {
        RegisterPageTreeItem(item);
        foreach (TreeViewItem child in item.Items.OfType<TreeViewItem>())
            RegisterPageTreeItemSubtree(child);
        _pageTreeItemIndexReady = true;
    }

    private void UnregisterPageTreeItemSubtree(TreeViewItem item)
    {
        foreach (TreeViewItem child in item.Items.OfType<TreeViewItem>())
            UnregisterPageTreeItemSubtree(child);

        string? path = GetPagesNodePath(item);
        if (!string.IsNullOrWhiteSpace(path))
        {
            string pathKey = NormalizePathForCompare(path);
            if (_pageTreeItemsByPath.TryGetValue(pathKey, out TreeViewItem? existing) &&
                ReferenceEquals(existing, item))
            {
                _pageTreeItemsByPath.Remove(pathKey);
            }
        }

        if (item.Tag is PageTakeoffNode node)
        {
            string key = PageTakeoffSelectionKey(node);
            if (_pageTakeoffTreeItemsByKey.TryGetValue(key, out TreeViewItem? existing) &&
                ReferenceEquals(existing, item))
            {
                _pageTakeoffTreeItemsByKey.Remove(key);
            }
        }
    }

    private TreeViewItem? FindPageTreeItemByFolderIndexed(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return null;

        return FindPageTreeItemByFolderKeyIndexed(NormalizePathForCompare(folderPath));
    }

    private TreeViewItem? FindPageTreeItemByFolderKeyIndexed(string folderKey)
    {
        if (string.IsNullOrWhiteSpace(folderKey))
            return null;

        if (!_pageTreeItemIndexReady)
            RebuildPageTreeItemIndex();

        if (TryGetIndexedPageTreeItem(folderKey, out TreeViewItem? item))
            return item;

        RebuildPageTreeItemIndex();
        return TryGetIndexedPageTreeItem(folderKey, out item) ? item : null;
    }

    private bool TryGetIndexedPageTreeItem(string folderKey, out TreeViewItem? item)
    {
        item = null;
        if (!_pageTreeItemsByPath.TryGetValue(folderKey, out TreeViewItem? candidate))
            return false;

        string? currentPath = GetPagesNodePath(candidate);
        if (string.IsNullOrWhiteSpace(currentPath) ||
            !string.Equals(NormalizePathForCompare(currentPath), folderKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        item = candidate;
        return true;
    }

    private TreeViewItem? FindPageTakeoffTreeItemIndexed(string pageFolder, string takeoffFolder)
    {
        string key = PageTakeoffSelectionKey(pageFolder, takeoffFolder);
        if (string.IsNullOrWhiteSpace(key))
            return null;

        if (!_pageTreeItemIndexReady)
            RebuildPageTreeItemIndex();

        if (TryGetIndexedPageTakeoffTreeItem(key, pageFolder, takeoffFolder, out TreeViewItem? item))
            return item;

        RebuildPageTreeItemIndex();
        return TryGetIndexedPageTakeoffTreeItem(key, pageFolder, takeoffFolder, out item) ? item : null;
    }

    private bool TryGetIndexedPageTakeoffTreeItem(
        string key,
        string pageFolder,
        string takeoffFolder,
        out TreeViewItem? item)
    {
        item = null;
        if (!_pageTakeoffTreeItemsByKey.TryGetValue(key, out TreeViewItem? candidate))
            return false;

        if (candidate.Tag is not PageTakeoffNode node ||
            !IsSamePageFolder(node.Page.FolderPath, pageFolder) ||
            !string.Equals(node.Takeoff.FolderPath, takeoffFolder, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        item = candidate;
        return true;
    }
}
