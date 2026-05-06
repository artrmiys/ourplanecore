using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OurPlaneCore;

public partial class MainWindow
{
    private static string? GetPagesNodePath(TreeViewItem item)
    {
        return item.Tag switch
        {
            PageFolderNode folder => folder.FolderPath,
            PageInfo page => page.FolderPath,
            _ => null,
        };
    }

    private static string PageTakeoffSelectionKey(PageTakeoffNode node) =>
        $"{NormalizePath(node.Page.FolderPath)}|{NormalizePath(node.Takeoff.FolderPath)}";

    private static string? GetPageTakeoffSelectionKey(TreeViewItem item) =>
        item.Tag is PageTakeoffNode node ? PageTakeoffSelectionKey(node) : null;

    private void SelectPagesRange(string? anchorPath, string targetPath, bool additive)
    {
        var candidates = EnumerateVisibleTreeItems(PagesTree)
            .Where(item => !IsRootPagesNode(item))
            .Select(item => (Item: item, Key: GetPagesNodePath(item)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .Select(entry => (entry.Item, Key: entry.Key!))
            .ToList();

        SelectRangeKeys(candidates, anchorPath, targetPath, _pagesMultiSelection, additive);
    }

    private void SelectPageTakeoffRange(string? anchorKey, string targetKey, string pageFolder, bool additive)
    {
        var candidates = EnumerateVisibleTreeItems(PagesTree)
            .Where(item => item.Tag is PageTakeoffNode node &&
                           IsSamePageFolder(node.Page.FolderPath, pageFolder))
            .Select(item => (Item: item, Key: GetPageTakeoffSelectionKey(item)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .Select(entry => (entry.Item, Key: entry.Key!))
            .ToList();

        SelectRangeKeys(candidates, anchorKey, targetKey, _pageTakeoffMultiSelection, additive);
    }

    private static void SelectRangeKeys(
        IReadOnlyList<(TreeViewItem Item, string Key)> candidates,
        string? anchorKey,
        string targetKey,
        HashSet<string> selection,
        bool additive)
    {
        int targetIndex = FindRangeKeyIndex(candidates, targetKey);
        if (targetIndex < 0)
            return;

        int anchorIndex = string.IsNullOrWhiteSpace(anchorKey)
            ? -1
            : FindRangeKeyIndex(candidates, anchorKey);
        if (anchorIndex < 0)
            anchorIndex = targetIndex;

        if (!additive)
            selection.Clear();

        int start = Math.Min(anchorIndex, targetIndex);
        int end = Math.Max(anchorIndex, targetIndex);
        for (int i = start; i <= end; i++)
            selection.Add(candidates[i].Key);
    }

    private static int FindRangeKeyIndex(IReadOnlyList<(TreeViewItem Item, string Key)> candidates, string key)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            if (string.Equals(candidates[i].Key, key, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private IReadOnlyList<PagesClipboardEntry> GetSelectedPageEntries(TreeViewItem anchor)
    {
        string? anchorPath = GetPagesNodePath(anchor);
        if (anchorPath == null || IsRootPagesNode(anchor))
            return [];

        IEnumerable<string> paths = _pagesMultiSelection.Contains(anchorPath)
            ? _pagesMultiSelection
            : [anchorPath];

        var entries = paths
            .Where(path => IsPathInsidePagesRoot(path, allowRoot: false))
            .Where(Directory.Exists)
            .Select(path => new PagesClipboardEntry(path, OurPlaneCoreJobStore.IsPageFolder(path)))
            .ToList();

        return NormalizeSelectedEntries(entries);
    }

    private int PageSelectionCount(TreeViewItem anchor) =>
        GetSelectedPageEntries(anchor).Count;

    private static IReadOnlyList<PagesClipboardEntry> NormalizeSelectedEntries(
        IReadOnlyList<PagesClipboardEntry> entries)
    {
        var distinct = entries
            .GroupBy(e => NormalizePath(e.SourcePath), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(e => NormalizePath(e.SourcePath).Length)
            .ToList();

        var result = new List<PagesClipboardEntry>();
        foreach (var entry in distinct)
        {
            if (result.Any(parent => OurPlaneCoreJobStore.IsSameOrDescendant(parent.SourcePath, entry.SourcePath)))
                continue;
            result.Add(entry);
        }

        return result;
    }

    private void PrunePagesMultiSelection()
    {
        _pagesMultiSelection.RemoveWhere(path =>
            !Directory.Exists(path) || !IsPathInsidePagesRoot(path, allowRoot: false));
    }

    private void ApplyPagesMultiSelectionVisuals()
    {
        Brush? brushOrNull(string key) => Application.Current.Resources[key] as Brush;
        Brush dropOk = brushOrNull("RowDropOkBrush") ?? new SolidColorBrush(Color.FromRgb(204, 245, 218));
        Brush activeLinked = brushOrNull("RowSelectionBrush") ?? new SolidColorBrush(Color.FromRgb(204, 229, 255));
        Brush multiLinked = brushOrNull("RowMultiSelectBrush") ?? new SolidColorBrush(Color.FromRgb(205, 226, 255));
        Brush measuredByActive = brushOrNull("RowActiveBrush") ?? new SolidColorBrush(Color.FromRgb(255, 236, 190));
        Brush rowFg = brushOrNull("RowFlagForegroundBrush") ?? Brushes.Black;

        foreach (TreeViewItem item in EnumeratePageTreeItems())
        {
            string? path = GetPagesNodePath(item);
            bool selected = path != null && !IsRootPagesNode(item) && _pagesMultiSelection.Contains(path);
            string? pageTakeoffKey = GetPageTakeoffSelectionKey(item);
            bool linkedSelected = pageTakeoffKey != null && _pageTakeoffMultiSelection.Contains(pageTakeoffKey);
            item.ClearValue(Control.BorderBrushProperty);
            item.ClearValue(Control.BorderThicknessProperty);

            if (ReferenceEquals(item, _pagesPositionDropTarget))
            {
                item.ClearValue(Control.BackgroundProperty);
                item.ClearValue(Control.ForegroundProperty);
                item.FontWeight = FontWeights.Normal;
                item.BorderBrush = _pagesPositionDropAllowed ? Brushes.SeaGreen : Brushes.IndianRed;
                item.BorderThickness = _pagesPositionDropAfter
                    ? new Thickness(0, 0, 0, 2)
                    : new Thickness(0, 2, 0, 0);
            }
            else if (ReferenceEquals(item, _pageTakeoffLegendDropTarget))
            {
                item.Background = dropOk;
                item.Foreground = rowFg;
                item.FontWeight = FontWeights.Normal;
                item.BorderBrush = Brushes.SeaGreen;
                item.BorderThickness = _pageTakeoffLegendDropAfter
                    ? new Thickness(0, 0, 0, 2)
                    : new Thickness(0, 2, 0, 0);
            }
            else if (IsActivePageTakeoffNode(item))
            {
                item.Background = activeLinked;
                item.Foreground = rowFg;
                item.FontWeight = FontWeights.Normal;
            }
            else if (selected || linkedSelected)
            {
                item.Background = multiLinked;
                item.Foreground = rowFg;
                item.ClearValue(Control.FontWeightProperty);
            }
            else if (IsPageMeasuredByActiveTakeoff(item))
            {
                item.Background = measuredByActive;
                item.Foreground = rowFg;
                item.ClearValue(Control.FontWeightProperty);
            }
            else
            {
                item.ClearValue(Control.BackgroundProperty);
                item.ClearValue(Control.ForegroundProperty);
                item.ClearValue(Control.FontWeightProperty);
            }
        }
    }

    private IEnumerable<TreeViewItem> EnumeratePageTreeItems()
    {
        foreach (TreeViewItem root in PagesTree.Items)
        {
            foreach (TreeViewItem item in EnumeratePageTreeItems(root))
                yield return item;
        }
    }

    private static IEnumerable<TreeViewItem> EnumeratePageTreeItems(TreeViewItem item)
    {
        yield return item;
        foreach (TreeViewItem child in item.Items)
        {
            foreach (TreeViewItem nested in EnumeratePageTreeItems(child))
                yield return nested;
        }
    }

    private static IEnumerable<TreeViewItem> EnumerateVisibleTreeItems(ItemsControl parent)
    {
        foreach (TreeViewItem child in parent.Items.OfType<TreeViewItem>())
        {
            yield return child;
            if (!child.IsExpanded)
                continue;

            foreach (TreeViewItem nested in EnumerateVisibleTreeItems(child))
                yield return nested;
        }
    }

    private static bool IsRootPagesNode(TreeViewItem item) =>
        item.Tag is PageFolderNode { IsRoot: true };

    private bool IsPathInsidePagesRoot(string path, bool allowRoot = true)
    {
        if (_currentJob == null) return false;
        string root = NormalizePath(_currentJob.PagesRoot);
        string full = NormalizePath(path);
        if (allowRoot && string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            return true;
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
