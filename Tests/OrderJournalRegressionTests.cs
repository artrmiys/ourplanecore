using System.Diagnostics;
using System.Text.Json;
using OurPlanCore;

internal static class OrderJournalRegressionTests
{
    public static IEnumerable<(string Name, Action Run)> Cases => new (string, Action)[]
    {
        ("sort journal ignores locked unrelated project data", LockedUnrelatedData),
        ("sort journal creates no history for an unchanged order", NoOp),
        ("sort journal rollback preserves unrelated edits and files", Rollback),
        ("sort journal restart recovery restores node order", Recovery),
        ("move journal excludes only derived page snap cache", MoveCache),
        ("recursive wall sort uses one undo and skips unchanged repeats", RecursiveSort),
    };

    private static void LockedUnrelatedData() => WithJob(job =>
    {
        string z = OurPlanCoreJobStore.CreateFolder(job.TakeoffsRoot, "dem 2x4");
        string a = OurPlanCoreJobStore.CreateFolder(job.TakeoffsRoot, "ext 2x6");
        string measurements = Path.Combine(z, "measurements.json");
        File.WriteAllText(measurements, "[]");
        string raster = Path.Combine(job.PagesRoot, "unrelated", "raster");
        Directory.CreateDirectory(raster);
        using var locked = new FileStream(measurements, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var cache = new FileStream(Path.Combine(raster, "snap.json"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        cache.SetLength(32 * 1024 * 1024);
        var timer = Stopwatch.StartNew();
        OurPlanCoreJobStore.SortTakeoffWallChildren(job.TakeoffsRoot);
        Check(OurPlanCoreJobStore.GetOrderedChildDirectories(job.TakeoffsRoot).First() == a, "Wall order");
        long bytes = Directory.GetFiles(Path.Combine(job.RootPath, ".undo"), "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length);
        Check(bytes < 32 * 1024, "Undo contains only node metadata");
        JobOperationJournal.UndoLast(job.RootPath);
        Check(OurPlanCoreJobStore.GetOrderedChildDirectories(job.TakeoffsRoot).First() == z, "Undo order");
        Console.WriteLine($"Wall sort and undo with locked 32 MiB cache: {timer.ElapsedMilliseconds} ms, journal {bytes} bytes.");
    });

    private static void NoOp() => WithJob(job =>
    {
        OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "A");
        OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Z");
        OurPlanCoreJobStore.SortChildren(job.PagesRoot, false);
        string undo = Path.Combine(job.RootPath, ".undo", "operations");
        int count = Directory.Exists(undo) ? Directory.GetDirectories(undo).Length : 0;
        var original = Directory.GetFiles(job.PagesRoot, "Data.xml", SearchOption.AllDirectories)
            .ToDictionary(p => p, File.GetLastWriteTimeUtc);
        for (int i = 0; i < 10; i++) OurPlanCoreJobStore.SortChildren(job.PagesRoot, false);
        Check((Directory.Exists(undo) ? Directory.GetDirectories(undo).Length : 0) == count, "No new history");
        Check(original.All(pair => File.GetLastWriteTimeUtc(pair.Key) == pair.Value), "No rewrites");
    });

    private static void Rollback() => WithJob(job =>
    {
        string z = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Z");
        string a = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "A");
        OurPlanCoreJobStore.SetOrderIndex(z, 1);
        OurPlanCoreJobStore.SetOrderIndex(a, 2);
        string[] original = OurPlanCoreJobStore.GetOrderedChildDirectories(job.PagesRoot).ToArray();
        string unrelated = Path.Combine(job.RootPath, "unrelated.json");
        JobOperationJournal.FailureInjectionForTests = stage =>
        {
            if (stage != "before-commit") return;
            File.WriteAllText(unrelated, "[42]");
            throw new IOException("Injected commit failure");
        };
        try { OurPlanCoreJobStore.SortChildren(job.PagesRoot, false); throw new Exception("Expected failure"); }
        catch (IOException) { }
        Check(original.SequenceEqual(OurPlanCoreJobStore.GetOrderedChildDirectories(job.PagesRoot)), "Rollback order");
        Check(File.ReadAllText(unrelated) == "[42]", "Unrelated file retained");
    });

    private static void Recovery() => WithJob(job =>
    {
        string z = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Z");
        string a = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "A");
        OurPlanCoreJobStore.SetOrderIndex(z, 1);
        OurPlanCoreJobStore.SetOrderIndex(a, 2);
        string[] original = OurPlanCoreJobStore.GetOrderedChildDirectories(job.PagesRoot).ToArray();
        _ = JobOperationJournal.BeginOrder(job.PagesRoot, [a, z]);
        OurPlanCoreJobStore.SetOrderIndex(a, 1);
        OurPlanCoreJobStore.SetOrderIndex(z, 2);
        JobOperationJournal.AbandonForTests();
        JobOperationJournal.RecoverPending(job.RootPath);
        Check(original.SequenceEqual(OurPlanCoreJobStore.GetOrderedChildDirectories(job.PagesRoot)), "Recovered order");
        Check(!JobOperationJournal.HasPending(job.RootPath), "Recovery finished");
    });

    private static void MoveCache() => WithJob(job =>
    {
        PageInfo page = OurPlanCoreJobStore.CreateBlankPage(job, "A1", job.PagesRoot);
        string target = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Target");
        string raster = Path.Combine(page.FolderPath, "raster"); Directory.CreateDirectory(raster);
        File.WriteAllText(Path.Combine(raster, "snap.json"), "[" + new string(' ', 1024 * 1024) + "]");
        string takeoff = OurPlanCoreJobStore.CreateFolder(job.TakeoffsRoot, "raster");
        File.WriteAllText(Path.Combine(takeoff, "measurements.json"), "[]");
        OurPlanCoreJobStore.MoveNode(page.FolderPath, target);
        string manifest = Directory.GetFiles(Path.Combine(job.RootPath, ".undo"), "operation.json", SearchOption.AllDirectories).Single();
        var record = JsonSerializer.Deserialize<JobOperationJournal.OperationRecord>(File.ReadAllText(manifest))!;
        Check(record.Before.Files.Single(f => f.Path.EndsWith("/snap.json")).Backup == "", "Snap not copied");
        Check(record.Before.Files.Single(f => f.Path.EndsWith("/measurements.json")).Backup != "", "Measurements retained");
        JobOperationJournal.UndoLast(job.RootPath);
        Check(File.Exists(Path.Combine(raster, "snap.json")), "Original cache returned with page");
    });

    private static void RecursiveSort() => WithJob(job =>
    {
        var original = new Dictionary<string, string[]>();
        for (int i = 0; i < 20; i++)
        {
            string floor = OurPlanCoreJobStore.CreateFolder(job.TakeoffsRoot, "Floor " + i);
            OurPlanCoreJobStore.CreateFolder(floor, "dem 2x4");
            OurPlanCoreJobStore.CreateFolder(floor, "ext 2x6");
            original[floor] = OurPlanCoreJobStore.GetOrderedChildDirectories(floor).ToArray();
        }
        NodeStore.SortTakeoffTree(job.TakeoffsRoot, walls: true);
        string undo = Path.Combine(job.RootPath, ".undo", "operations");
        Check(Directory.GetDirectories(undo).Length == 1, "One undo for 20 floors");
        NodeStore.SortTakeoffTree(job.TakeoffsRoot, walls: true);
        Check(Directory.GetDirectories(undo).Length == 1, "Repeat creates no undo");
        JobOperationJournal.UndoLast(job.RootPath);
        Check(original.All(pair => pair.Value.SequenceEqual(OurPlanCoreJobStore.GetOrderedChildDirectories(pair.Key))), "All floors restored together");
    });

    private static void WithJob(Action<OurPlanCoreJob> action)
    {
        string parent = Path.Combine(Path.GetTempPath(), "opc-order-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        try { action(OurPlanCoreJobStore.CreateJob(parent, "Fixture")); }
        finally { JobOperationJournal.AbandonForTests(); DataFileReader.ResetForTests(); Directory.Delete(parent, true); }
    }
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
    }
}
