using System.Windows.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    private bool TryRefreshTakeoffSortFast(IReadOnlyList<string> parents)
    {
        var plans = new List<(ItemsControl Parent, List<TreeViewItem> Children)>();
        foreach (string folder in parents)
        {
            if (TakeoffTreeItemsParentControl(folder) is not { } parent) return false;
            var byPath = new Dictionary<string, TreeViewItem>(StringComparer.OrdinalIgnoreCase);
            foreach (TreeViewItem child in parent.Items.OfType<TreeViewItem>())
            {
                string? path = GetTakeoffNodePath(child);
                if (string.IsNullOrEmpty(path)) return false;
                byPath[NormalizePath(path)] = child;
            }
            var paths = OurPlanCoreJobStore.GetOrderedChildDirectories(folder).Select(NormalizePath).ToList();
            if (paths.Count != parent.Items.Count || paths.Any(path => !byPath.ContainsKey(path))) return false;
            plans.Add((parent, paths.Select(path => byPath[path]).ToList()));
        }

        TreeViewItem? selected = TakeoffsTree.SelectedItem as TreeViewItem;
        bool previousSync = _syncingTakeoffTreeSelection;
        _syncingTakeoffTreeSelection = true;
        try
        {
            foreach (var plan in plans)
            {
                plan.Parent.Items.Clear();
                foreach (TreeViewItem item in plan.Children) plan.Parent.Items.Add(item);
            }
            if (selected != null) selected.IsSelected = true;
        }
        finally { _syncingTakeoffTreeSelection = previousSync; }

        // Keep the same measurements, selection and Undo objects. Only their
        // traversal order changes; a disk reload would rebuild the whole viewport.
        var ordered = TakeoffItemsInTreeOrder(TakeoffsTree).ToList();
        if (ordered.Count != _takeoffItems.Count) return false;
        _takeoffItems.Clear();
        _takeoffItems.AddRange(ordered);
        RefreshEstimateTable();
        RefreshSheetLegend();
        return true;
    }

    private static IEnumerable<TakeoffItem> TakeoffItemsInTreeOrder(ItemsControl parent)
    {
        foreach (TreeViewItem node in parent.Items.OfType<TreeViewItem>())
        {
            if (node.Tag is TakeoffItem item) yield return item;
            foreach (TakeoffItem descendant in TakeoffItemsInTreeOrder(node)) yield return descendant;
        }
    }
}
