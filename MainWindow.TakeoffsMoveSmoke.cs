using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace OurPlaneCore;

public partial class MainWindow
{
    private const string TakeoffsMoveSmokeEnv = "OURPLANECORE_TAKEOFFS_MOVE_SMOKE";
    private const string TakeoffsMoveSmokeReportEnv = "OURPLANECORE_TAKEOFFS_MOVE_SMOKE_REPORT";
    private const string TakeoffsMoveSmokeSelectionCountEnv = "OURPLANECORE_TAKEOFFS_SMOKE_SELECTION_COUNT";

    private async Task TryRunTakeoffsMoveSmokeAsync()
    {
        if (!IsTruthyEnvironment(TakeoffsMoveSmokeEnv))
            return;

        var report = new TakeoffsMoveSmokeReport
        {
            StartedUtc = DateTime.UtcNow,
        };
        int exitCode = 0;
        try
        {
            await Task.Yield();
            RunTakeoffsMoveSmoke(report);
            report.Passed = report.Failures.Count == 0;
            exitCode = report.Passed ? 0 : 2;
        }
        catch (Exception ex)
        {
            report.Failures.Add(ex.ToString());
            report.Passed = false;
            exitCode = 2;
        }
        finally
        {
            report.FinishedUtc = DateTime.UtcNow;
            WriteTakeoffsMoveSmokeReport(report);
            Application.Current.Shutdown(exitCode);
        }
    }

    private void RunTakeoffsMoveSmoke(TakeoffsMoveSmokeReport report)
    {
        if (_currentJob == null)
            throw new InvalidOperationException("No current job was opened before Takeoffs move smoke.");

        report.JobPath = _currentJob.RootPath;
        string root = _currentJob.TakeoffsRoot;
        string wallBRoot = Path.Combine(root, "Smoke Wall B");
        string targetFolder = Path.Combine(root, "Smoke Target Folder");
        string wallBInsideTarget = Path.Combine(targetFolder, "Smoke Wall B");
        report.PageCount = CollectPagesUnder(_currentJob.PagesRoot).Count();
        report.TakeoffItemCount = _takeoffItems.Count;

        RequireSmokePath(wallBRoot, "source takeoff");
        RequireSmokePath(targetFolder, "target folder");

        RunTakeoffsSelectionStressSmoke(report);
        RunTakeoffDrop(
            new TakeoffsClipboard([new TakeoffsClipboardEntry(wallBRoot, IsItem: true)], TakeoffsClipboardMode.Cut),
            targetFolder,
            TakeoffsClipboardMode.Cut);

        report.MoveInPassed =
            Directory.Exists(wallBInsideTarget) &&
            !Directory.Exists(wallBRoot) &&
            FindTakeoffTreeItemByFolder(wallBInsideTarget) != null;
        if (!report.MoveInPassed)
            report.Failures.Add("Smoke Wall B was not moved into Smoke Target Folder in filesystem and UI tree.");

        RunTakeoffDrop(
            new TakeoffsClipboard([new TakeoffsClipboardEntry(wallBInsideTarget, IsItem: true)], TakeoffsClipboardMode.Cut),
            root,
            TakeoffsClipboardMode.Cut);

        report.MoveOutPassed =
            Directory.Exists(wallBRoot) &&
            !Directory.Exists(wallBInsideTarget) &&
            FindTakeoffTreeItemByFolder(wallBRoot) != null;
        if (!report.MoveOutPassed)
            report.Failures.Add("Smoke Wall B was not moved back to the Takeoffs root in filesystem and UI tree.");

        RunTakeoffsCreateFolderSmoke(report, root);
        RunTakeoffsBulkCopySmoke(report, root, targetFolder);
        report.Passed = report.Failures.Count == 0;
    }

    private void RunTakeoffsCreateFolderSmoke(TakeoffsMoveSmokeReport report, string root)
    {
        if (_currentJob == null)
            throw new InvalidOperationException("No current job is open.");

        var stopwatch = Stopwatch.StartNew();
        string folderPath = OurPlaneCoreJobStore.CreateTakeoffFolder(_currentJob, root, "Smoke Created Folder");
        var node = new TakeoffFolderNode
        {
            Name = OurPlaneCoreJobStore.DisplayName(folderPath),
            FolderPath = folderPath,
        };
        var tvi = AddTakeoffFolderTreeItem(node, TakeoffsTree);
        tvi.IsSelected = true;
        _activeTakeoffParentFolder = folderPath;
        stopwatch.Stop();

        report.CreateFolderMilliseconds = stopwatch.ElapsedMilliseconds;
        report.CreateFolderPassed = Directory.Exists(folderPath) &&
                                    FindTakeoffTreeItemByFolder(folderPath) != null;
        if (!report.CreateFolderPassed)
            report.Failures.Add("Smoke Created Folder was not created in filesystem and UI tree.");
        if (report.CreateFolderMilliseconds > 500)
            report.Failures.Add($"Takeoff folder create was too slow: {report.CreateFolderMilliseconds} ms.");
    }

    private void RunTakeoffsBulkCopySmoke(TakeoffsMoveSmokeReport report, string root, string targetFolder)
    {
        var bulkSources = Directory.GetDirectories(root)
            .Where(path => OurPlaneCoreJobStore.DisplayName(path).StartsWith("Smoke Bulk ", StringComparison.Ordinal))
            .OrderBy(path => OurPlaneCoreJobStore.DisplayName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (bulkSources.Count == 0)
            throw new InvalidOperationException("No Smoke Bulk takeoffs were available for copy smoke.");

        var payload = new TakeoffsClipboard(
            bulkSources.Select(path => new TakeoffsClipboardEntry(path, IsItem: true)).ToList(),
            TakeoffsClipboardMode.Copy);

        var timings = new TakeoffDropTimings();
        var stopwatch = Stopwatch.StartNew();
        RunTakeoffDrop(payload, targetFolder, TakeoffsClipboardMode.Copy, timings);
        stopwatch.Stop();

        var copied = Directory.GetDirectories(targetFolder)
            .Where(path => OurPlaneCoreJobStore.DisplayName(path).StartsWith("Smoke Bulk ", StringComparison.Ordinal))
            .OrderBy(path => OurPlaneCoreJobStore.DisplayName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();

        report.BulkCopyCount = bulkSources.Count;
        report.BulkCopyMilliseconds = stopwatch.ElapsedMilliseconds;
        report.BulkFlushMilliseconds = timings.FlushMilliseconds;
        report.BulkFileOperationMilliseconds = timings.FileOperationMilliseconds;
        report.BulkUiRefreshMilliseconds = timings.UiRefreshMilliseconds;
        report.BulkCopyLoadMilliseconds = timings.CopyLoadMilliseconds;
        report.BulkCopyAppendMilliseconds = timings.CopyAppendMilliseconds;
        report.BulkCopyViewportMilliseconds = timings.CopyViewportMilliseconds;
        report.BulkCopySelectionMilliseconds = timings.CopySelectionMilliseconds;
        report.BulkCopyPageIndicatorsMilliseconds = timings.CopyPageIndicatorsMilliseconds;
        report.BulkCopyLegendMilliseconds = timings.CopyLegendMilliseconds;
        report.BulkCopyEstimateMilliseconds = timings.CopyEstimateMilliseconds;
        report.BulkCopyTotalMilliseconds = timings.CopyTotalMilliseconds;
        report.BulkCopyPassed = copied.Count == bulkSources.Count &&
                                copied.All(path => FindTakeoffTreeItemByFolder(path) != null);
        if (!report.BulkCopyPassed)
            report.Failures.Add($"Bulk copy expected {bulkSources.Count} copied takeoff tree nodes but found {copied.Count}.");

        long copyLimitMs = Math.Max(4000, report.BulkCopyCount * 50L);
        if (report.BulkCopyMilliseconds > copyLimitMs)
            report.Failures.Add($"Bulk takeoff copy was too slow: {report.BulkCopyMilliseconds} ms for {report.BulkCopyCount} nodes.");
    }

    private void RunTakeoffsSelectionStressSmoke(TakeoffsMoveSmokeReport report)
    {
        int selectionCount = ReadEnvironmentInt(TakeoffsMoveSmokeSelectionCountEnv, 80, 1, 500);
        var candidates = _takeoffItems
            .Where(item => item.Measurements.Count > 0 &&
                           item.Name.StartsWith("Smoke Measured ", StringComparison.Ordinal))
            .ToList();
        if (candidates.Count == 0)
            throw new InvalidOperationException("No measured smoke takeoffs were available for selection stress.");

        var elapsed = new List<long>();
        var selectElapsed = new List<long>();
        var takeoffsLayoutElapsed = new List<long>();
        var pagesLayoutElapsed = new List<long>();
        foreach (TakeoffItem item in SelectEvenly(candidates, selectionCount))
        {
            if (FindTakeoffTreeItemByFolder(item.FolderPath) is not { } treeItem)
            {
                report.Failures.Add($"Measured takeoff was missing from the UI tree: {item.Name}.");
                continue;
            }

            var stopwatch = Stopwatch.StartNew();
            var selectWatch = Stopwatch.StartNew();
            treeItem.IsSelected = true;
            selectWatch.Stop();
            var takeoffsLayoutWatch = Stopwatch.StartNew();
            TakeoffsTree.UpdateLayout();
            takeoffsLayoutWatch.Stop();
            var pagesLayoutWatch = Stopwatch.StartNew();
            PagesTree.UpdateLayout();
            pagesLayoutWatch.Stop();
            stopwatch.Stop();
            elapsed.Add(stopwatch.ElapsedMilliseconds);
            selectElapsed.Add(selectWatch.ElapsedMilliseconds);
            takeoffsLayoutElapsed.Add(takeoffsLayoutWatch.ElapsedMilliseconds);
            pagesLayoutElapsed.Add(pagesLayoutWatch.ElapsedMilliseconds);
        }

        report.SelectionCount = elapsed.Count;
        report.SelectionAverageMilliseconds = elapsed.Count == 0 ? 0 : Math.Round(elapsed.Average(), 1);
        report.SelectionMaxMilliseconds = elapsed.Count == 0 ? 0 : elapsed.Max();
        report.SelectionEventAverageMilliseconds = selectElapsed.Count == 0 ? 0 : Math.Round(selectElapsed.Average(), 1);
        report.SelectionTakeoffsLayoutAverageMilliseconds = takeoffsLayoutElapsed.Count == 0 ? 0 : Math.Round(takeoffsLayoutElapsed.Average(), 1);
        report.SelectionPagesLayoutAverageMilliseconds = pagesLayoutElapsed.Count == 0 ? 0 : Math.Round(pagesLayoutElapsed.Average(), 1);
        if (elapsed.Count == 0)
            report.Failures.Add("Selection stress did not select any measured takeoffs.");
        if (report.SelectionAverageMilliseconds > 200)
            report.Failures.Add($"Measured takeoff selection average was too slow: {report.SelectionAverageMilliseconds} ms.");
        if (report.SelectionMaxMilliseconds > 800)
            report.Failures.Add($"Measured takeoff selection max was too slow: {report.SelectionMaxMilliseconds} ms.");
    }

    private static IReadOnlyList<T> SelectEvenly<T>(IReadOnlyList<T> source, int count)
    {
        if (source.Count <= count)
            return source.ToList();

        var indexes = new SortedSet<int>();
        double step = (double)(source.Count - 1) / Math.Max(1, count - 1);
        for (int i = 0; indexes.Count < count && i < count; i++)
            indexes.Add((int)Math.Round(i * step));

        for (int i = 0; indexes.Count < count && i < source.Count; i++)
            indexes.Add(i);

        return indexes.Select(index => source[index]).ToList();
    }

    private static void RequireSmokePath(string path, string label)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Missing {label}: {path}");
    }

    private static void WriteTakeoffsMoveSmokeReport(TakeoffsMoveSmokeReport report)
    {
        string path = Environment.GetEnvironmentVariable(TakeoffsMoveSmokeReportEnv) ?? "";
        if (string.IsNullOrWhiteSpace(path))
            return;

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class TakeoffsMoveSmokeReport
    {
        public DateTime StartedUtc { get; set; }
        public DateTime FinishedUtc { get; set; }
        public bool Passed { get; set; }
        public string JobPath { get; set; } = "";
        public int PageCount { get; set; }
        public int TakeoffItemCount { get; set; }
        public bool MoveInPassed { get; set; }
        public bool MoveOutPassed { get; set; }
        public int SelectionCount { get; set; }
        public double SelectionAverageMilliseconds { get; set; }
        public long SelectionMaxMilliseconds { get; set; }
        public double SelectionEventAverageMilliseconds { get; set; }
        public double SelectionTakeoffsLayoutAverageMilliseconds { get; set; }
        public double SelectionPagesLayoutAverageMilliseconds { get; set; }
        public bool CreateFolderPassed { get; set; }
        public long CreateFolderMilliseconds { get; set; }
        public bool BulkCopyPassed { get; set; }
        public int BulkCopyCount { get; set; }
        public long BulkCopyMilliseconds { get; set; }
        public long BulkFlushMilliseconds { get; set; }
        public long BulkFileOperationMilliseconds { get; set; }
        public long BulkUiRefreshMilliseconds { get; set; }
        public long BulkCopyLoadMilliseconds { get; set; }
        public long BulkCopyAppendMilliseconds { get; set; }
        public long BulkCopyViewportMilliseconds { get; set; }
        public long BulkCopySelectionMilliseconds { get; set; }
        public long BulkCopyPageIndicatorsMilliseconds { get; set; }
        public long BulkCopyLegendMilliseconds { get; set; }
        public long BulkCopyEstimateMilliseconds { get; set; }
        public long BulkCopyTotalMilliseconds { get; set; }
        public List<string> Failures { get; } = [];
    }
}
