using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void BtnCollapsePagesTree_Click(object sender, RoutedEventArgs e) =>
        SetProjectTreeExpanded(PagesTree, false, "Pages tree collapsed.");

    private void BtnExpandPagesTree_Click(object sender, RoutedEventArgs e) =>
        SetProjectTreeExpanded(PagesTree, true, "Pages tree expanded.");

    private void BtnCollapseTakeoffsTree_Click(object sender, RoutedEventArgs e) =>
        SetProjectTreeExpanded(TakeoffsTree, false, "Takeoffs tree collapsed.");

    private void BtnExpandTakeoffsTree_Click(object sender, RoutedEventArgs e) =>
        SetProjectTreeExpanded(TakeoffsTree, true, "Takeoffs tree expanded.");

    private void SetProjectTreeExpanded(ItemsControl tree, bool isExpanded, string statusText)
    {
        SetTreeItemsExpanded(tree, isExpanded);
        if (ReferenceEquals(tree, PagesTree))
            CaptureExpandedTreeState(PagesTree, _expandedPageTreePaths, GetPagesNodePath);
        else if (ReferenceEquals(tree, TakeoffsTree))
            CaptureExpandedTreeState(TakeoffsTree, _expandedTakeoffTreePaths, GetTakeoffNodePath);
        TxtStatus.Text = statusText;
    }

    private void CollapseProjectTreeDisplays()
    {
        CollapseTreeAndExpansionState(PagesTree, _expandedPageTreePaths);
        CollapseTreeAndExpansionState(TakeoffsTree, _expandedTakeoffTreePaths);
    }

    private static void SetTreeItemsExpanded(ItemsControl parent, bool isExpanded)
    {
        foreach (TreeViewItem item in parent.Items.OfType<TreeViewItem>())
        {
            item.IsExpanded = isExpanded;
            SetTreeItemsExpanded(item, isExpanded);
        }
    }

    private void CollapseTreeAndExpansionState(ItemsControl tree, TreeExpansionState expandedPaths)
    {
        SetTreeItemsExpanded(tree, false);
        expandedPaths.Clear();
    }

    private static void RebaseExpandedTreePaths(TreeExpansionState expandedPaths, string oldPath, string newPath) =>
        expandedPaths.Rebase(oldPath, newPath);

    private void RestoreExpandedTreeState(
        ItemsControl tree,
        TreeExpansionState expandedPaths,
        Func<TreeViewItem, string?> getPath)
    {
        WithTreeExpansionTrackingSuppressed(() =>
            RestoreExpandedTreeStateCore(tree, expandedPaths, getPath));
    }

    private static void RestoreExpandedTreeStateCore(
        ItemsControl parent,
        TreeExpansionState expandedPaths,
        Func<TreeViewItem, string?> getPath)
    {
        foreach (TreeViewItem item in parent.Items.OfType<TreeViewItem>())
        {
            if (expandedPaths.Contains(getPath(item)))
                item.IsExpanded = true;

            RestoreExpandedTreeStateCore(item, expandedPaths, getPath);
        }
    }

    private static void CaptureExpandedTreeState(
        ItemsControl tree,
        TreeExpansionState expandedPaths,
        Func<TreeViewItem, string?> getPath)
    {
        expandedPaths.Clear();
        CaptureExpandedTreeStateCore(tree, expandedPaths, getPath);
    }

    private static void CaptureExpandedTreeStateCore(
        ItemsControl parent,
        TreeExpansionState expandedPaths,
        Func<TreeViewItem, string?> getPath)
    {
        foreach (TreeViewItem item in parent.Items.OfType<TreeViewItem>())
        {
            if (item.IsExpanded)
                expandedPaths.Add(getPath(item));

            CaptureExpandedTreeStateCore(item, expandedPaths, getPath);
        }
    }

    private void PagesTreeItem_Expanded(object sender, RoutedEventArgs e)
    {
        TrackTreeExpansion(e, _expandedPageTreePaths, GetPagesNodePath, expanded: true);
        if (e.OriginalSource is TreeViewItem { Tag: PageInfo page })
            TryRefreshDirtyPageTakeoffIndicator(page.FolderPath);
    }

    private void PagesTreeItem_Collapsed(object sender, RoutedEventArgs e) =>
        TrackTreeExpansion(e, _expandedPageTreePaths, GetPagesNodePath, expanded: false);

    private void TakeoffsTreeItem_Expanded(object sender, RoutedEventArgs e) =>
        TrackTreeExpansion(e, _expandedTakeoffTreePaths, GetTakeoffNodePath, expanded: true);

    private void TakeoffsTreeItem_Collapsed(object sender, RoutedEventArgs e) =>
        TrackTreeExpansion(e, _expandedTakeoffTreePaths, GetTakeoffNodePath, expanded: false);

    private void TrackTreeExpansion(
        RoutedEventArgs e,
        TreeExpansionState expandedPaths,
        Func<TreeViewItem, string?> getPath,
        bool expanded)
    {
        if (_suppressTreeExpansionTracking || e.OriginalSource is not TreeViewItem item)
            return;

        if (expanded)
            expandedPaths.Add(getPath(item));
        else
            expandedPaths.Remove(getPath(item));
    }

    private void WithTreeExpansionTrackingSuppressed(Action action)
    {
        bool previous = _suppressTreeExpansionTracking;
        _suppressTreeExpansionTracking = true;
        try
        {
            action();
        }
        finally
        {
            _suppressTreeExpansionTracking = previous;
        }
    }

    private void ExpandTreeItemAndAncestorsWithoutTracking(TreeViewItem item)
    {
        WithTreeExpansionTrackingSuppressed(() =>
            ExpandTreeItemAndAncestors(item));
    }

    private static string? ExpansionPathKey(string? path) =>
        TreeExpansionState.NormalizePathKey(path);
}
