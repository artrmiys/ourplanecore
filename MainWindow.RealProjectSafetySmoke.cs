using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace OurPlanCore;

public partial class MainWindow
{
    // Explicit diagnostic mode, run only against a disposable copy chosen by the test launcher.
    private async Task RunRealProjectSafetySmokeAsync()
    {
        string reportPath = Environment.GetEnvironmentVariable("OURPLANCORE_REAL_PROJECT_SAFETY_REPORT")
            ?? throw new InvalidOperationException("A real-project safety report path is required.");
        OurPlanCoreJob job = _currentJob ?? throw new InvalidOperationException("Open the copied project first.");
        var steps = new List<object>();
        bool passed = false;
        string error = "";
        var pages = CollectPagesUnder(job.PagesRoot).ToList();
        int itemCount = _takeoffItems.Count, measurementCount = _takeoffItems.Sum(item => item.Measurements.Count);
        var totals = _takeoffItems.ToDictionary(item => item.FolderPath, item => item.Total(0), StringComparer.OrdinalIgnoreCase);
        try
        {
            if (pages.Count < 100 || measurementCount < 500)
                throw new InvalidOperationException("This check requires a real project with at least 100 sheets and 500 measurements.");
            if (!_takeoffSaveService.Flush().Success) throw new IOException("Initial takeoff flush failed.");
            SaveCurrentPageAnnotations();
            var before = RealProjectMetadata(job);
            string scope = pages.GroupBy(page => Path.GetDirectoryName(page.FolderPath)!)
                .Where(group => !SameFolder(group.Key, job.PagesRoot))
                .OrderByDescending(group => group.Count()).First().Key;
            int scopedPages = CollectPagesUnder(scope).Count();
            await Step("Scoped A/S sort", () => SortPagesIntoArchStruct(scope));
            Check(CollectPagesUnder(job.PagesRoot).Count() == pages.Count, "Sort changed the sheet count.");
            foreach (var pair in before.Where(pair => !SafeJobPathResolver.Inside(scope, Path.Combine(job.RootPath, pair.Key)) && pair.Key.StartsWith("Pages", StringComparison.OrdinalIgnoreCase)))
                Check(File.Exists(Path.Combine(job.RootPath, pair.Key)) && RealProjectHash(Path.Combine(job.RootPath, pair.Key)) == pair.Value, "Sort changed a sibling: " + pair.Key);
            CheckTotals();
            steps.Add(new { Check = "Scoped sort preserves siblings and all takeoff totals", ScopedPages = scopedPages });
            await Step("Undo A/S sort", () => UndoLastPageOperation("page-sort"));
            CheckMetadata(before);

            var measured = _takeoffItems.SelectMany(item => item.Measurements).Select(measurement => NormalizePageReferencePath(measurement.PageFolder))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var sourceGroup = CollectPagesUnder(job.PagesRoot).Where(page => measured.Contains(NormalizePath(page.FolderPath)))
                .GroupBy(page => Path.GetDirectoryName(page.FolderPath)!).OrderByDescending(group => group.Count()).First();
            PageInfo[] selected = sourceGroup.Take(3).ToArray();
            Check(selected.Length == 3, "Need three measured sheets in one folder.");
            OpenPageInActiveTab(selected[0]);
            await WaitForViewportPageRenderAsync(selected[0], 30000);
            await WaitForViewportPagePaintAsync(selected[0], 30000);
            CaptureViewportSmokeImage(selected[0], "measurements-before");
            string destination = SameFolder(sourceGroup.Key, job.PagesRoot) ? scope : job.PagesRoot;
            foreach (PagesClipboardMode mode in new[] { PagesClipboardMode.Cut, PagesClipboardMode.Copy })
            {
                var payload = new PagesClipboard(selected.Select(page => new PagesClipboardEntry(page.FolderPath, true)).ToList(), mode);
                await Step(mode == PagesClipboardMode.Cut ? "Move three measured sheets" : "Copy three measured sheets", () =>
                {
                    RunDrop(payload, destination, mode);
                    FlushPendingPagesTreeDropRefresh();
                });
                var resultPaths = _pagesMultiSelection.ToArray();
                Check(resultPaths.Length == selected.Length, "Clipboard operation did not create all three destinations.");
                var resultNames = resultPaths.Select(OurPlanCoreJobStore.DisplayName).Order().ToArray();
                Check(resultNames.SequenceEqual(selected.Select(page => page.Name).Order()), "Visible sheet names changed.");
                Check(CollectPagesUnder(job.PagesRoot).Count() == pages.Count + (mode == PagesClipboardMode.Copy ? selected.Length : 0), "Unexpected sheet count after clipboard operation.");
                CheckTotals();
                foreach (var measurement in _takeoffItems.SelectMany(item => item.Measurements).Where(measurement =>
                    resultPaths.Any(path => SameFolder(path, NormalizePageReferencePath(measurement.PageFolder)))))
                    Check(Directory.Exists(NormalizePageReferencePath(measurement.PageFolder)), "Moved measurement points to a missing sheet.");
                await Step("Undo " + mode, () => UndoLastPageOperation());
                CheckMetadata(before);
            }

            // Fail after the real storage move, before committing its metadata inventory.
            JobOperationJournal.FailureInjectionForTests = stage => { if (stage == "before-commit") throw new IOException("Injected real-project interruption"); };
            bool interrupted = false;
            try { OurPlanCoreJobStore.MoveNode(selected[0].FolderPath, destination); }
            catch (IOException ex) when (ex.Message == "Injected real-project interruption") { interrupted = true; }
            finally { JobOperationJournal.FailureInjectionForTests = null; }
            Check(interrupted, "The injected interruption did not run.");
            CheckMetadata(before);
            steps.Add(new { Check = "Interrupted move restored every original Pages/Takeoffs metadata file" });

            string measurementsPath = Path.Combine(_takeoffItems.First(item => item.Measurements.Count > 0).FolderPath, "measurements.json");
            using (var locked = new FileStream(measurementsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                _ = TakeoffStore.ReadMeasurements(Path.GetDirectoryName(measurementsPath)!);
            Check(DataFileReader.IsProtected(measurementsPath), "Locked real measurements were not protected.");
            bool blocked = false;
            try { IoUtil.WriteAllTextAtomic(measurementsPath, "[]"); } catch (IOException) { blocked = true; }
            Check(blocked, "A read failure allowed an empty overwrite.");
            DataFileReader.RestoreOrRetry(measurementsPath);
            CheckMetadata(before);
            steps.Add(new { Check = "Locked real measurements resisted empty overwrite; explicit Retry preserved all bytes" });
            passed = true;

            async Task Step(string name, Action action)
            {
                Stopwatch watch = Stopwatch.StartNew(); action(); watch.Stop();
                steps.Add(new { Operation = name, ActionMs = watch.ElapsedMilliseconds });
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                await Task.Delay(200);
                if (_currentPage is { } page) CaptureViewportSmokeImage(page, name.Replace(' ', '-'));
            }
            void CheckTotals()
            {
                Check(_takeoffItems.Count == itemCount && _takeoffItems.Sum(item => item.Measurements.Count) == measurementCount, "Takeoff or measurement count changed.");
                Check(_takeoffItems.All(item => totals.TryGetValue(item.FolderPath, out double value) && Math.Abs(item.Total(0) - value) < 1e-8), "A takeoff quantity changed.");
            }
            void CheckMetadata(Dictionary<string, string> original)
            {
                var actual = RealProjectMetadata(job);
                string[] changed = original.Keys.Union(actual.Keys, StringComparer.OrdinalIgnoreCase)
                    .Where(key => !original.TryGetValue(key, out string? a) || !actual.TryGetValue(key, out string? b) || a != b).ToArray();
                Check(changed.Length == 0, "Metadata differs after rollback: " + string.Join(", ", changed.Take(8)));
                CheckTotals();
            }
        }
        catch (Exception ex) { error = ex.ToString(); throw; }
        finally
        {
            JobOperationJournal.FailureInjectionForTests = null;
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new
            { Passed = passed, Pages = pages.Count, Takeoffs = itemCount, Measurements = measurementCount, Steps = steps, Error = error }, new JsonSerializerOptions { WriteIndented = true }));
        }
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }

    private static Dictionary<string, string> RealProjectMetadata(OurPlanCoreJob job) =>
        new[] { job.PagesRoot, job.TakeoffsRoot }.SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .Where(path => Path.GetExtension(path) is ".json" or ".xml")
            .ToDictionary(path => Path.GetRelativePath(job.RootPath, path), RealProjectHash, StringComparer.OrdinalIgnoreCase);

    private static string RealProjectHash(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }
}
