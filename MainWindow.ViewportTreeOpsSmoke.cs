using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void RunViewportTreeOpsSmoke(ViewportPageStressSmokeReport report)
    {
        var treeOps = new ViewportTreeOpsSmokeResult();
        report.TreeOps = treeOps;
        try
        {
            RunPagesTreeOpsSmoke(treeOps);
            RunTakeoffsTreeOpsSmoke(treeOps);
            treeOps.Passed = treeOps.Failures.Count == 0;
        }
        catch (Exception ex)
        {
            treeOps.Failures.Add(ex.ToString());
            treeOps.Passed = false;
        }

        foreach (string failure in treeOps.Failures)
            report.Failures.Add($"tree ops: {failure}");
    }

    private void RunPagesTreeOpsSmoke(ViewportTreeOpsSmokeResult report)
    {
        if (_currentJob == null)
            throw new InvalidOperationException("No current job is open.");

        var single = FindMovableSiblingSet(_currentJob.PagesRoot, IsPageTreeSmokeSheet, 1);
        var bulk = FindMovableSiblingSet(_currentJob.PagesRoot, IsPageTreeSmokeSheet, 3);
        if (single == null || bulk == null)
            throw new InvalidOperationException("Pages tree smoke needs movable sibling sheets for single and bulk operations.");

        report.PagesSingleSelectionMs = SelectSinglePageForSmoke(single.Paths[0]);
        report.PagesBulkSelectionCount = bulk.Paths.Count;
        report.PagesBulkSelectionMs = SelectPagesBulkForSmoke(bulk.Paths);
        (report.PagesSingleMoveDownMs, report.PagesSingleMoveRestoreMs) = MovePagesDownAndRestore(single.Parent, single.Paths);
        (report.PagesBulkMoveDownMs, report.PagesBulkMoveRestoreMs) = MovePagesDownAndRestore(bulk.Parent, bulk.Paths);
        report.PagesPassed = true;
    }

    private void RunTakeoffsTreeOpsSmoke(ViewportTreeOpsSmokeResult report)
    {
        if (_currentJob == null)
            throw new InvalidOperationException("No current job is open.");

        var single = FindMovableSiblingSet(_currentJob.TakeoffsRoot, OurPlaneCoreJobStore.IsTakeoffItemFolder, 1);
        var bulk = FindMovableSiblingSet(_currentJob.TakeoffsRoot, OurPlaneCoreJobStore.IsTakeoffItemFolder, 3);
        if (single == null || bulk == null)
            throw new InvalidOperationException("Takeoffs tree smoke needs movable sibling takeoff items for single and bulk operations.");

        report.TakeoffsSingleSelectionMs = SelectSingleTakeoffForSmoke(single.Paths[0]);
        report.TakeoffsBulkSelectionCount = bulk.Paths.Count;
        report.TakeoffsBulkSelectionMs = SelectTakeoffsBulkForSmoke(bulk.Paths);
        (report.TakeoffsSingleMoveDownMs, report.TakeoffsSingleMoveRestoreMs) = MoveTakeoffsDownAndRestore(single.Parent, single.Paths);
        (report.TakeoffsBulkMoveDownMs, report.TakeoffsBulkMoveRestoreMs) = MoveTakeoffsDownAndRestore(bulk.Parent, bulk.Paths);
        report.TakeoffsPassed = true;
    }

    private long SelectSinglePageForSmoke(string path)
    {
        if (FindPageTreeItemByFolder(path) is not { } item)
            throw new InvalidOperationException($"Pages tree item was not found for '{path}'.");

        var watch = Stopwatch.StartNew();
        _pagesMultiSelection.Clear();
        _pagesMultiSelection.Add(path);
        item.IsSelected = true;
        PagesTree.UpdateLayout();
        ApplyPagesMultiSelectionVisuals();
        watch.Stop();
        return watch.ElapsedMilliseconds;
    }

    private long SelectPagesBulkForSmoke(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            throw new InvalidOperationException("No pages were selected for bulk smoke.");

        var watch = Stopwatch.StartNew();
        _pagesMultiSelection.Clear();
        foreach (string path in paths)
            _pagesMultiSelection.Add(path);
        ApplyPagesMultiSelectionVisuals();
        if (FindPageTreeItemByFolder(paths[0]) is { } item)
            item.IsSelected = true;
        PagesTree.UpdateLayout();
        watch.Stop();
        return watch.ElapsedMilliseconds;
    }

    private (long MoveDownMs, long RestoreMs) MovePagesDownAndRestore(string parent, IReadOnlyList<string> paths)
    {
        IReadOnlyList<string> before = OrderedChildSnapshot(parent);
        SelectPagesBulkForSmoke(paths);
        if (FindPageTreeItemByFolder(paths[0]) is not { } anchor)
            throw new InvalidOperationException($"Pages move anchor was not found for '{paths[0]}'.");

        var move = Stopwatch.StartNew();
        MovePagesNodes(anchor, 1);
        PagesTree.UpdateLayout();
        move.Stop();
        if (OrdersEqual(before, OrderedChildSnapshot(parent)))
            throw new InvalidOperationException("Pages move smoke did not change sibling order.");

        var restore = Stopwatch.StartNew();
        if (FindPageTreeItemByFolder(paths[0]) is not { } movedAnchor)
            throw new InvalidOperationException($"Pages restore anchor was not found for '{paths[0]}'.");
        MovePagesNodes(movedAnchor, -1);
        PagesTree.UpdateLayout();
        restore.Stop();
        if (!OrdersEqual(before, OrderedChildSnapshot(parent)))
            throw new InvalidOperationException("Pages move smoke did not restore original sibling order.");

        return (move.ElapsedMilliseconds, restore.ElapsedMilliseconds);
    }

    private long SelectSingleTakeoffForSmoke(string path)
    {
        if (FindTakeoffTreeItemByFolder(path) is not { } item)
            throw new InvalidOperationException($"Takeoffs tree item was not found for '{path}'.");

        var watch = Stopwatch.StartNew();
        SetTakeoffMultiSelection([path]);
        item.IsSelected = true;
        TakeoffsTree.UpdateLayout();
        PagesTree.UpdateLayout();
        watch.Stop();
        return watch.ElapsedMilliseconds;
    }

    private long SelectTakeoffsBulkForSmoke(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            throw new InvalidOperationException("No takeoffs were selected for bulk smoke.");

        var watch = Stopwatch.StartNew();
        SetTakeoffMultiSelection(paths);
        if (FindTakeoffTreeItemByFolder(paths[0]) is { } item)
            item.IsSelected = true;
        TakeoffsTree.UpdateLayout();
        PagesTree.UpdateLayout();
        watch.Stop();
        return watch.ElapsedMilliseconds;
    }

    private (long MoveDownMs, long RestoreMs) MoveTakeoffsDownAndRestore(string parent, IReadOnlyList<string> paths)
    {
        IReadOnlyList<string> before = OrderedChildSnapshot(parent);
        SelectTakeoffsBulkForSmoke(paths);
        if (FindTakeoffTreeItemByFolder(paths[0]) is not { } anchor)
            throw new InvalidOperationException($"Takeoffs move anchor was not found for '{paths[0]}'.");

        var move = Stopwatch.StartNew();
        MoveTakeoffNodes(anchor, 1);
        TakeoffsTree.UpdateLayout();
        PagesTree.UpdateLayout();
        move.Stop();
        if (OrdersEqual(before, OrderedChildSnapshot(parent)))
            throw new InvalidOperationException("Takeoffs move smoke did not change sibling order.");

        var restore = Stopwatch.StartNew();
        if (FindTakeoffTreeItemByFolder(paths[0]) is not { } movedAnchor)
            throw new InvalidOperationException($"Takeoffs restore anchor was not found for '{paths[0]}'.");
        MoveTakeoffNodes(movedAnchor, -1);
        TakeoffsTree.UpdateLayout();
        PagesTree.UpdateLayout();
        restore.Stop();
        if (!OrdersEqual(before, OrderedChildSnapshot(parent)))
            throw new InvalidOperationException("Takeoffs move smoke did not restore original sibling order.");

        return (move.ElapsedMilliseconds, restore.ElapsedMilliseconds);
    }

    private static MovableSiblingSet? FindMovableSiblingSet(
        string root,
        Func<string, bool> include,
        int count)
    {
        if (!Directory.Exists(root))
            return null;

        foreach (string parent in EnumerateTreeOpsParents(root))
        {
            var candidates = OurPlaneCoreJobStore.GetOrderedChildDirectories(parent)
                .Where(include)
                .ToList();
            for (int i = 0; i + count < candidates.Count; i++)
            {
                var paths = candidates.Skip(i).Take(count).ToList();
                if (OurPlaneCoreJobStore.CanMoveSiblings(paths, 1) &&
                    OurPlaneCoreJobStore.CanMoveSiblings(paths, -1))
                {
                    return new MovableSiblingSet(parent, paths);
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateTreeOpsParents(string root)
    {
        yield return root;
        foreach (string child in Directory.EnumerateDirectories(root))
        {
            foreach (string nested in EnumerateTreeOpsParents(child))
                yield return nested;
        }
    }

    private static bool IsPageTreeSmokeSheet(string path) =>
        OurPlaneCoreJobStore.TryReadPage(path) != null;

    private static IReadOnlyList<string> OrderedChildSnapshot(string parent) =>
        OurPlaneCoreJobStore.GetOrderedChildDirectories(parent)
            .Select(NormalizePath)
            .ToList();

    private static bool OrdersEqual(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.Count == right.Count &&
        left.Zip(right, (a, b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase)).All(match => match);

    private sealed record MovableSiblingSet(string Parent, IReadOnlyList<string> Paths);

    private sealed class ViewportTreeOpsSmokeResult
    {
        public bool Passed { get; set; }
        public bool PagesPassed { get; set; }
        public bool TakeoffsPassed { get; set; }
        public int PagesBulkSelectionCount { get; set; }
        public long PagesSingleSelectionMs { get; set; }
        public long PagesBulkSelectionMs { get; set; }
        public long PagesSingleMoveDownMs { get; set; }
        public long PagesSingleMoveRestoreMs { get; set; }
        public long PagesBulkMoveDownMs { get; set; }
        public long PagesBulkMoveRestoreMs { get; set; }
        public int TakeoffsBulkSelectionCount { get; set; }
        public long TakeoffsSingleSelectionMs { get; set; }
        public long TakeoffsBulkSelectionMs { get; set; }
        public long TakeoffsSingleMoveDownMs { get; set; }
        public long TakeoffsSingleMoveRestoreMs { get; set; }
        public long TakeoffsBulkMoveDownMs { get; set; }
        public long TakeoffsBulkMoveRestoreMs { get; set; }
        public List<string> Failures { get; } = [];
    }
}
