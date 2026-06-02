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

        ApplyPageSelectionTiming(report, SelectSinglePageForSmoke(single.Paths[0]), single: true);
        report.PagesBulkSelectionCount = bulk.Paths.Count;
        ApplyPageSelectionTiming(report, SelectPagesBulkForSmoke(bulk.Paths), single: false);
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

        ApplyTakeoffSelectionTiming(report, SelectSingleTakeoffForSmoke(single.Paths[0]), single: true);
        report.TakeoffsBulkSelectionCount = bulk.Paths.Count;
        ApplyTakeoffSelectionTiming(report, SelectTakeoffsBulkForSmoke(bulk.Paths), single: false);
        (report.TakeoffsSingleMoveDownMs, report.TakeoffsSingleMoveRestoreMs) = MoveTakeoffsDownAndRestore(single.Parent, single.Paths);
        (report.TakeoffsBulkMoveDownMs, report.TakeoffsBulkMoveRestoreMs) = MoveTakeoffsDownAndRestore(bulk.Parent, bulk.Paths);
        report.TakeoffsSectionDropPageJumpMs = MoveTakeoffSectionAndRestoreWithPageJump();
        report.TakeoffsPassed = true;
    }

    private TreeSelectionTiming SelectSinglePageForSmoke(string path)
    {
        if (FindPageTreeItemByFolder(path) is not { } item)
            throw new InvalidOperationException($"Pages tree item was not found for '{path}'.");

        var total = Stopwatch.StartNew();
        var phase = Stopwatch.StartNew();
        _pagesMultiSelection.Clear();
        _pagesMultiSelection.Add(path);
        phase.Stop();
        long setMs = phase.ElapsedMilliseconds;

        phase.Restart();
        ApplyPagesMultiSelectionVisuals();
        phase.Stop();
        long visualMs = phase.ElapsedMilliseconds;

        phase.Restart();
        SelectPagesTreeItemSilently(item);
        if (item.Tag is PageInfo page)
        {
            TryRefreshDirtyPageTakeoffIndicator(page.FolderPath);
            OpenPageInActiveTab(page);
        }
        phase.Stop();
        long selectedEventMs = phase.ElapsedMilliseconds;

        phase.Restart();
        PagesTree.UpdateLayout();
        phase.Stop();

        total.Stop();
        return new TreeSelectionTiming(total.ElapsedMilliseconds, setMs, selectedEventMs, phase.ElapsedMilliseconds, visualMs);
    }

    private TreeSelectionTiming SelectPagesBulkForSmoke(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            throw new InvalidOperationException("No pages were selected for bulk smoke.");

        var total = Stopwatch.StartNew();
        var phase = Stopwatch.StartNew();
        _pagesMultiSelection.Clear();
        foreach (string path in paths)
            _pagesMultiSelection.Add(path);
        phase.Stop();
        long setMs = phase.ElapsedMilliseconds;

        phase.Restart();
        ApplyPagesMultiSelectionVisuals();
        phase.Stop();
        long visualMs = phase.ElapsedMilliseconds;

        phase.Restart();
        if (FindPageTreeItemByFolder(paths[0]) is { } item)
            item.IsSelected = true;
        phase.Stop();
        long selectedEventMs = phase.ElapsedMilliseconds;

        phase.Restart();
        PagesTree.UpdateLayout();
        phase.Stop();

        total.Stop();
        return new TreeSelectionTiming(total.ElapsedMilliseconds, setMs, selectedEventMs, phase.ElapsedMilliseconds, visualMs);
    }

    private (long MoveDownMs, long RestoreMs) MovePagesDownAndRestore(string parent, IReadOnlyList<string> paths)
    {
        IReadOnlyList<string> before = OrderedChildSnapshot(parent);
        _ = SelectPagesBulkForSmoke(paths);
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

    private TreeSelectionTiming SelectSingleTakeoffForSmoke(string path)
    {
        if (FindTakeoffTreeItemByFolder(path) is not { } item)
            throw new InvalidOperationException($"Takeoffs tree item was not found for '{path}'.");

        var total = Stopwatch.StartNew();
        var phase = Stopwatch.StartNew();
        SetTakeoffMultiSelection([path]);
        phase.Stop();
        long setMs = phase.ElapsedMilliseconds;

        phase.Restart();
        item.IsSelected = true;
        phase.Stop();
        long selectedEventMs = phase.ElapsedMilliseconds;

        phase.Restart();
        TakeoffsTree.UpdateLayout();
        phase.Stop();
        long primaryLayoutMs = phase.ElapsedMilliseconds;

        phase.Restart();
        PagesTree.UpdateLayout();
        phase.Stop();

        total.Stop();
        return new TreeSelectionTiming(total.ElapsedMilliseconds, setMs, selectedEventMs, primaryLayoutMs, phase.ElapsedMilliseconds);
    }

    private TreeSelectionTiming SelectTakeoffsBulkForSmoke(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            throw new InvalidOperationException("No takeoffs were selected for bulk smoke.");

        var total = Stopwatch.StartNew();
        var phase = Stopwatch.StartNew();
        SetTakeoffMultiSelection(paths);
        phase.Stop();
        long setMs = phase.ElapsedMilliseconds;

        phase.Restart();
        if (FindTakeoffTreeItemByFolder(paths[0]) is { } item)
            item.IsSelected = true;
        phase.Stop();
        long selectedEventMs = phase.ElapsedMilliseconds;

        phase.Restart();
        TakeoffsTree.UpdateLayout();
        phase.Stop();
        long primaryLayoutMs = phase.ElapsedMilliseconds;

        phase.Restart();
        PagesTree.UpdateLayout();
        phase.Stop();

        total.Stop();
        return new TreeSelectionTiming(total.ElapsedMilliseconds, setMs, selectedEventMs, primaryLayoutMs, phase.ElapsedMilliseconds);
    }

    private (long MoveDownMs, long RestoreMs) MoveTakeoffsDownAndRestore(string parent, IReadOnlyList<string> paths)
    {
        IReadOnlyList<string> before = OrderedChildSnapshot(parent);
        _ = SelectTakeoffsBulkForSmoke(paths);
        string pageBefore = _currentPage?.FolderPath
            ?? throw new InvalidOperationException("Takeoffs move smoke needs an active viewport page.");
        if (FindTakeoffTreeItemByFolder(paths[0]) is not { } anchor)
            throw new InvalidOperationException($"Takeoffs move anchor was not found for '{paths[0]}'.");

        var move = Stopwatch.StartNew();
        MoveTakeoffNodes(anchor, 1);
        TakeoffsTree.UpdateLayout();
        PagesTree.UpdateLayout();
        move.Stop();
        AssertCurrentPageUnchangedForTakeoffMove(pageBefore, "moving takeoff node(s) down");
        if (OrdersEqual(before, OrderedChildSnapshot(parent)))
            throw new InvalidOperationException("Takeoffs move smoke did not change sibling order.");

        var restore = Stopwatch.StartNew();
        if (FindTakeoffTreeItemByFolder(paths[0]) is not { } movedAnchor)
            throw new InvalidOperationException($"Takeoffs restore anchor was not found for '{paths[0]}'.");
        MoveTakeoffNodes(movedAnchor, -1);
        TakeoffsTree.UpdateLayout();
        PagesTree.UpdateLayout();
        restore.Stop();
        AssertCurrentPageUnchangedForTakeoffMove(pageBefore, "restoring takeoff node order");
        if (!OrdersEqual(before, OrderedChildSnapshot(parent)))
            throw new InvalidOperationException("Takeoffs move smoke did not restore original sibling order.");

        return (move.ElapsedMilliseconds, restore.ElapsedMilliseconds);
    }

    private long MoveTakeoffSectionAndRestoreWithPageJump()
    {
        var candidate = FindTakeoffSectionDropSmokeCandidate()
            ?? throw new InvalidOperationException("Takeoffs section drop smoke needs two measured takeoffs with the same type.");
        (TakeoffItem source, TakeoffItem target, Measurement measurement) = candidate;

        OpenDifferentPageForSectionDropSmoke(measurement.PageFolder);

        var stopwatch = Stopwatch.StartNew();
        MoveSectionForSmoke(source, target, measurement);
        AssertCurrentPageIsMeasurementPage(measurement, "moving section/count row into target");
        if (!target.Measurements.Contains(measurement) || source.Measurements.Contains(measurement))
            throw new InvalidOperationException("Takeoffs section drop smoke did not move the measurement into the target item.");

        OpenDifferentPageForSectionDropSmoke(measurement.PageFolder);
        MoveSectionForSmoke(target, source, measurement);
        AssertCurrentPageIsMeasurementPage(measurement, "restoring section/count row to source");
        if (!source.Measurements.Contains(measurement) || target.Measurements.Contains(measurement))
            throw new InvalidOperationException("Takeoffs section drop smoke did not restore the measurement to the source item.");

        stopwatch.Stop();
        return stopwatch.ElapsedMilliseconds;
    }

    private (TakeoffItem Source, TakeoffItem Target, Measurement Measurement)? FindTakeoffSectionDropSmokeCandidate()
    {
        var measured = _takeoffItems
            .Where(item => item.Measurements.Count > 0)
            .Select(item => new
            {
                Item = item,
                Type = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType),
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Type))
            .ToList();

        foreach (var source in measured)
        {
            Measurement? measurement = source.Item.Measurements.FirstOrDefault(measurement =>
                OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) == source.Type &&
                !string.IsNullOrWhiteSpace(measurement.PageFolder));
            if (measurement == null)
                continue;

            TakeoffItem? target = measured
                .Where(entry => !ReferenceEquals(entry.Item, source.Item) && entry.Type == source.Type)
                .Select(entry => entry.Item)
                .FirstOrDefault();
            if (target != null)
                return (source.Item, target, measurement);
        }

        return null;
    }

    private void OpenDifferentPageForSectionDropSmoke(string measurementPageFolder)
    {
        if (_currentJob == null)
            throw new InvalidOperationException("No current job is open.");

        PageInfo? page = CollectPagesUnder(_currentJob.PagesRoot)
            .FirstOrDefault(candidate => !IsSamePageFolder(candidate.FolderPath, measurementPageFolder));
        if (page == null)
            throw new InvalidOperationException("Takeoffs section drop smoke needs at least two pages.");

        OpenPageInActiveTab(page);
    }

    private void MoveSectionForSmoke(TakeoffItem source, TakeoffItem target, Measurement measurement)
    {
        if (FindTakeoffTreeItem(target) is not { } targetItem)
            throw new InvalidOperationException($"Takeoffs section drop smoke target was not found: {target.Name}.");

        DropTakeoffSections(
            new TakeoffSectionDrag([new TakeoffMeasurementNode(source, measurement)]),
            targetItem,
            copy: false);
        TakeoffsTree.UpdateLayout();
        PagesTree.UpdateLayout();
    }

    private void AssertCurrentPageIsMeasurementPage(Measurement measurement, string action)
    {
        if (string.IsNullOrWhiteSpace(measurement.PageFolder))
            throw new InvalidOperationException("Takeoffs section drop smoke measurement has no page folder.");

        if (_currentPage == null || !IsSamePageFolder(_currentPage.FolderPath, measurement.PageFolder))
            throw new InvalidOperationException($"Takeoffs section drop smoke did not jump to the measurement page while {action}.");
    }

    private void AssertCurrentPageUnchangedForTakeoffMove(string pageBefore, string action)
    {
        if (_currentPage == null || !IsSamePageFolder(_currentPage.FolderPath, pageBefore))
            throw new InvalidOperationException($"Takeoffs move smoke changed the viewport page while {action}.");
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
    private sealed record TreeSelectionTiming(
        long TotalMs,
        long SetSelectionMs,
        long SelectedEventMs,
        long PrimaryLayoutMs,
        long SecondaryLayoutMs);

    private static void ApplyPageSelectionTiming(
        ViewportTreeOpsSmokeResult report,
        TreeSelectionTiming timing,
        bool single)
    {
        if (single)
        {
            report.PagesSingleSelectionMs = timing.TotalMs;
            report.PagesSingleSelectionSetMs = timing.SetSelectionMs;
            report.PagesSingleSelectionEventMs = timing.SelectedEventMs;
            report.PagesSingleSelectionLayoutMs = timing.PrimaryLayoutMs;
            report.PagesSingleSelectionVisualMs = timing.SecondaryLayoutMs;
        }
        else
        {
            report.PagesBulkSelectionMs = timing.TotalMs;
            report.PagesBulkSelectionSetMs = timing.SetSelectionMs;
            report.PagesBulkSelectionEventMs = timing.SelectedEventMs;
            report.PagesBulkSelectionLayoutMs = timing.PrimaryLayoutMs;
            report.PagesBulkSelectionVisualMs = timing.SecondaryLayoutMs;
        }
    }

    private static void ApplyTakeoffSelectionTiming(
        ViewportTreeOpsSmokeResult report,
        TreeSelectionTiming timing,
        bool single)
    {
        if (single)
        {
            report.TakeoffsSingleSelectionMs = timing.TotalMs;
            report.TakeoffsSingleSelectionSetMs = timing.SetSelectionMs;
            report.TakeoffsSingleSelectionEventMs = timing.SelectedEventMs;
            report.TakeoffsSingleSelectionTakeoffsLayoutMs = timing.PrimaryLayoutMs;
            report.TakeoffsSingleSelectionPagesLayoutMs = timing.SecondaryLayoutMs;
        }
        else
        {
            report.TakeoffsBulkSelectionMs = timing.TotalMs;
            report.TakeoffsBulkSelectionSetMs = timing.SetSelectionMs;
            report.TakeoffsBulkSelectionEventMs = timing.SelectedEventMs;
            report.TakeoffsBulkSelectionTakeoffsLayoutMs = timing.PrimaryLayoutMs;
            report.TakeoffsBulkSelectionPagesLayoutMs = timing.SecondaryLayoutMs;
        }
    }

    private sealed class ViewportTreeOpsSmokeResult
    {
        public bool Passed { get; set; }
        public bool PagesPassed { get; set; }
        public bool TakeoffsPassed { get; set; }
        public int PagesBulkSelectionCount { get; set; }
        public long PagesSingleSelectionMs { get; set; }
        public long PagesSingleSelectionSetMs { get; set; }
        public long PagesSingleSelectionEventMs { get; set; }
        public long PagesSingleSelectionLayoutMs { get; set; }
        public long PagesSingleSelectionVisualMs { get; set; }
        public long PagesBulkSelectionMs { get; set; }
        public long PagesBulkSelectionSetMs { get; set; }
        public long PagesBulkSelectionEventMs { get; set; }
        public long PagesBulkSelectionLayoutMs { get; set; }
        public long PagesBulkSelectionVisualMs { get; set; }
        public long PagesSingleMoveDownMs { get; set; }
        public long PagesSingleMoveRestoreMs { get; set; }
        public long PagesBulkMoveDownMs { get; set; }
        public long PagesBulkMoveRestoreMs { get; set; }
        public int TakeoffsBulkSelectionCount { get; set; }
        public long TakeoffsSingleSelectionMs { get; set; }
        public long TakeoffsSingleSelectionSetMs { get; set; }
        public long TakeoffsSingleSelectionEventMs { get; set; }
        public long TakeoffsSingleSelectionTakeoffsLayoutMs { get; set; }
        public long TakeoffsSingleSelectionPagesLayoutMs { get; set; }
        public long TakeoffsBulkSelectionMs { get; set; }
        public long TakeoffsBulkSelectionSetMs { get; set; }
        public long TakeoffsBulkSelectionEventMs { get; set; }
        public long TakeoffsBulkSelectionTakeoffsLayoutMs { get; set; }
        public long TakeoffsBulkSelectionPagesLayoutMs { get; set; }
        public long TakeoffsSingleMoveDownMs { get; set; }
        public long TakeoffsSingleMoveRestoreMs { get; set; }
        public long TakeoffsBulkMoveDownMs { get; set; }
        public long TakeoffsBulkMoveRestoreMs { get; set; }
        public long TakeoffsSectionDropPageJumpMs { get; set; }
        public List<string> Failures { get; } = [];
    }
}
