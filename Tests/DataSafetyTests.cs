using System.Text.Json;
using OurPlanCore;
using SkiaSharp;

internal static class DataSafetyTests
{
    public static IEnumerable<(string Name, Action Run)> Cases => new (string, Action)[]
    {
        ("data safety distinguishes missing valid and unreadable", ReadStates),
        ("data safety locked measurements cannot overwrite existing bytes", LockedMeasurements),
        ("data safety corruption remains protected after restart", CorruptionSurvivesRestart),
        ("data safety locked annotations and bookmarks stay protected", OtherStores),
        ("data safety source IO failure cannot trigger metadata repair", SourceRepairGuard),
        ("data safety recovery preserves all source fields", RecoveryPreservesSource),
        ("data safety rejects null empty and unsupported measurements", InvalidMeasurementDocuments),
        ("data safety page copy preserves legend and extension metadata", CopyPreservesSource),
        ("data safety paths reject escape rooted and reserved names", UnsafePaths),
        ("data safety paths reject junction escapes", JunctionEscape),
        ("data safety AI attachments enforce paths types and sizes", AiAttachments),
        ("data safety failed move restores complete original project", FailedMove),
        ("data safety failed copy restores complete original project", FailedCopy),
        ("data safety interrupted move recovers on job open", InterruptedMove),
        ("data safety sort undo survives restart and preserves other edits", SortUndo),
        ("data safety undo refuses conflicting later edits", UndoConflict),
        ("data safety invalid recovery manifest cannot escape job", UnsafeManifest),
        ("data safety corrupt source requires explicit recovery", CorruptSourceNeedsRecovery),
        ("data safety failed PDF import restores original nodes", FailedPdfImport),
        ("data safety bulk operation blocks package checkpoints", BulkCheckpointGate),
        ("data safety interrupted batch restores already moved pages", InterruptedBatch),
        ("data safety page source cannot escape project root", EscapingPageSource),
        ("data safety interrupted undo recovers on job open", InterruptedUndo),
        ("data safety overlay source cannot escape project root", EscapingOverlay),
        ("raster cache loads full image and overview beyond MAX_PATH", LongRasterPaths),
    };

    private static void ReadStates() => WithJob(job =>
    {
        string folder = OurPlanCoreJobStore.CreateTakeoffItem(job, "Walls", "#FF0000").FolderPath;
        Check(TakeoffStore.ReadMeasurements(folder).State == DataFileState.Missing, "Missing");
        File.WriteAllText(Path.Combine(folder, "measurements.json"), "[]");
        Check(TakeoffStore.ReadMeasurements(folder).State == DataFileState.Valid, "Valid empty array");
        using var locked = new FileStream(Path.Combine(folder, "measurements.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Check(TakeoffStore.ReadMeasurements(folder).State == DataFileState.Unreadable, "Unreadable");
    });

    private static void LockedMeasurements() => WithJob(job =>
    {
        TakeoffItem item = MeasuredItem(job);
        string path = Path.Combine(item.FolderPath, "measurements.json");
        byte[] original = File.ReadAllBytes(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Check(TakeoffStore.LoadMeasurements(item.FolderPath).Count == 0, "Unavailable UI fallback");
            Throws(() => OurPlanCoreJobStore.SaveTakeoffItem(item));
        }
        Check(original.SequenceEqual(File.ReadAllBytes(path)), "Original survives read failure");
        DataFileReader.ResetForTests();
        _ = TakeoffStore.LoadMeasurements(item.FolderPath);
        Throws(() => OurPlanCoreJobStore.SaveTakeoffItem(item));
        DataFileReader.RestoreOrRetry(path);
        OurPlanCoreJobStore.SaveTakeoffItem(item);
        Check(TakeoffStore.LoadMeasurements(item.FolderPath).Count == 1, "Recovered measurements");
    });

    private static void CorruptionSurvivesRestart() => WithJob(job =>
    {
        TakeoffItem item = MeasuredItem(job);
        string path = Path.Combine(item.FolderPath, "measurements.json");
        string valid = File.ReadAllText(path);
        File.WriteAllText(path, "[broken");
        Check(TakeoffStore.ReadMeasurements(item.FolderPath).State == DataFileState.Corrupt, "Corrupt");
        Check(Directory.GetFiles(item.FolderPath, "measurements.json.corrupt-*").Length == 1, "Quarantine");
        DataFileReader.ResetForTests();
        Check(TakeoffStore.ReadMeasurements(item.FolderPath).State == DataFileState.Corrupt, "Protected after restart");
        Throws(() => OurPlanCoreJobStore.SaveTakeoffItem(item));
        string copy = Path.Combine(job.RootPath, "good.json"); File.WriteAllText(copy, valid);
        DataFileReader.RestoreOrRetry(path, copy);
        Check(File.ReadAllText(path) == valid, "Restored original bytes");
    });

    private static void OtherStores() => WithJob(job =>
    {
        PageInfo page = OurPlanCoreJobStore.CreateBlankPage(job, "A1", job.PagesRoot);
        string annotations = PageAnnotationStore.PageAnnotationsJsonPath(page.FolderPath);
        string bookmarks = Path.Combine(job.RootPath, "bookmarks.json");
        File.WriteAllText(annotations, "[]"); File.WriteAllText(bookmarks, "[]");
        using var lockA = new FileStream(annotations, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var lockB = new FileStream(bookmarks, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Check(PageAnnotationStore.ReadPageAnnotations(page.FolderPath).State == DataFileState.Unreadable, "Annotations unavailable");
        Check(PageBookmarkStore.ReadPageBookmarks(job).State == DataFileState.Unreadable, "Bookmarks unavailable");
        Throws(() => OurPlanCoreJobStore.SavePageAnnotations(page.FolderPath, []));
        Throws(() => OurPlanCoreJobStore.SavePageBookmarks(job, []));
    });

    private static void SourceRepairGuard() => WithJob(job =>
    {
        PageInfo page = OurPlanCoreJobStore.CreateBlankPage(job, "A1", job.PagesRoot);
        string path = Path.Combine(page.FolderPath, "source.json"); byte[] original = File.ReadAllBytes(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Check(OurPlanCoreJobStore.TryReadPage(page.FolderPath) == null, "No synthetic page on IO failure");
        Check(original.SequenceEqual(File.ReadAllBytes(path)), "Source untouched");
        Check(Directory.GetFiles(page.FolderPath, "source.json.corrupt-*").Length == 0, "IO is not corruption");
    });

    private static void RecoveryPreservesSource() => WithJob(job =>
    {
        PageInfo page = OurPlanCoreJobStore.CreateBlankPage(job, "A1", job.PagesRoot);
        string path = Path.Combine(page.FolderPath, "source.json");
        SourceInfo src = OurPlanCoreJobStore.ReadSource(page.FolderPath)!;
        src.LegendTakeoffOrder = ["first", "second"]; src.LegendTakeoffOrderMode = "manual";
        src.HiddenTakeoffs = ["hidden"]; src.OverlayOpacity = .21;
        src.AdditionalData = new() { ["future_field"] = JsonDocument.Parse("{\"keep\":true}").RootElement.Clone() };
        string good = JsonSerializer.Serialize(src); File.WriteAllText(path, good);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) _ = PageStore.ReadSource(page.FolderPath);
        DataFileReader.RestoreOrRetry(path);
        Check(File.ReadAllText(path) == good, "Recovery preserves source exactly");
        Check(Directory.GetFiles(page.FolderPath, "source.json.recovered-*").Length == 1, "Recovery backup exists");
    });

    private static void InvalidMeasurementDocuments()
    {
        foreach (string json in new[] { "", "null", "{}", "{\"schema_version\":999,\"measurements\":[]}" })
            WithJob(job =>
            {
                string folder = OurPlanCoreJobStore.CreateTakeoffItem(job, "Invalid", "#FF0000").FolderPath;
                File.WriteAllText(Path.Combine(folder, "measurements.json"), json);
                Check(TakeoffStore.ReadMeasurements(folder).State == DataFileState.Corrupt, "Invalid document protected");
            });
    }

    private static void CopyPreservesSource() => WithJob(job =>
    {
        PageInfo page = OurPlanCoreJobStore.CreateBlankPage(job, "A1", job.PagesRoot);
        string path = Path.Combine(page.FolderPath, "source.json"); SourceInfo src = OurPlanCoreJobStore.ReadSource(page.FolderPath)!;
        src.LegendTakeoffOrder = ["z", "a"]; src.LegendTakeoffOrderMode = "manual";
        src.AdditionalData = new() { ["future_field"] = JsonDocument.Parse("42").RootElement.Clone() };
        File.WriteAllText(path, JsonSerializer.Serialize(src));
        string copy = OurPlanCoreJobStore.DuplicatePage(page.FolderPath);
        SourceInfo result = OurPlanCoreJobStore.ReadSource(copy)!;
        Check(result.LegendTakeoffOrder.SequenceEqual(src.LegendTakeoffOrder) && result.LegendTakeoffOrderMode == "manual", "Legend preserved");
        Check(result.AdditionalData!["future_field"].GetInt32() == 42, "Unknown metadata preserved");
        Check(OurPlanCoreJobStore.DisplayName(copy) == "A1", "Visible name unchanged");
    });

    private static void UnsafePaths() => WithJob(job =>
    {
        foreach (string value in new[] { "../escape.txt", "C:\\outside.txt", "/outside", "NUL.txt", "safe/COM1.json", "file.txt:stream", "bad. /file" })
            Throws(() => SafeJobPathResolver.ResolveRelative(job.RootPath, value));
        foreach (string id in new[] { "CON", "aux", "../x", " spaced ", "id.exe", "COM1" }) Throws(() => SmartContextFileId.Require(id, "test"));
        Check(SafeJobPathResolver.ResolveRelative(job.RootPath, "Pages/../sources/test.pdf").StartsWith(job.RootPath), "Contained legacy relative references work");
    });

    private static void JunctionEscape() => WithJob(job =>
    {
        string outside = Path.Combine(Path.GetDirectoryName(job.RootPath)!, "outside"); Directory.CreateDirectory(outside);
        string link = Path.Combine(job.RootPath, "link");
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J"); start.ArgumentList.Add(link); start.ArgumentList.Add(outside);
        using var process = System.Diagnostics.Process.Start(start)!; process.WaitForExit();
        Check(process.ExitCode == 0, "Junction fixture created");
        try { Throws(() => SafeJobPathResolver.ResolveRelative(job.RootPath, "link/secret.json")); }
        finally { Directory.Delete(link); }
    });

    private static void AiAttachments() => WithJob(job =>
    {
        var request = new SmartAiRequest { Id = "safe_id", CropPath = "../../secret.png" };
        Throws(() => AiAttachmentPolicy.Validate(job, request));
        request.CropPath = ""; request.LayerManifestPath = "Data.xml";
        Throws(() => AiAttachmentPolicy.Validate(job, request));
        string layers = Path.Combine(job.RootPath, "layers.json");
        using (var f = File.Create(layers)) f.SetLength(AiAttachmentPolicy.MaxTextBytes + 1);
        request.LayerManifestPath = "layers.json";
        Throws(() => AiAttachmentPolicy.Validate(job, request));
    });

    private static void FailedMove() => WithJob(job =>
    {
        PageInfo page = OurPlanCoreJobStore.CreateBlankPage(job, "A1", job.PagesRoot);
        string target = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Target"); var before = Metadata(job);
        JobOperationJournal.FailureInjectionForTests = stage => { if (stage == "before-commit") throw new IOException("Injected final write failure"); };
        Throws(() => OurPlanCoreJobStore.MoveNode(page.FolderPath, target)); JobOperationJournal.FailureInjectionForTests = null;
        Check(Directory.Exists(page.FolderPath), "Original folder restored"); EqualMetadata(before, Metadata(job));
    });

    private static void FailedCopy() => WithJob(job =>
    {
        PageInfo page = OurPlanCoreJobStore.CreateBlankPage(job, "A1", job.PagesRoot); var before = Metadata(job);
        JobOperationJournal.FailureInjectionForTests = stage => { if (stage == "before-commit") throw new IOException("Injected copy failure"); };
        Throws(() => OurPlanCoreJobStore.DuplicatePage(page.FolderPath)); JobOperationJournal.FailureInjectionForTests = null;
        EqualMetadata(before, Metadata(job));
    });

    private static void InterruptedMove() => WithJob(job =>
    {
        PageInfo page = OurPlanCoreJobStore.CreateBlankPage(job, "A1", job.PagesRoot);
        string target = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Target"); var before = Metadata(job);
        _ = JobOperationJournal.Begin(job.RootPath, "Interrupted move");
        _ = OurPlanCoreJobStore.MoveNode(page.FolderPath, target);
        JobOperationJournal.AbandonForTests();
        _ = OurPlanCoreJobStore.LoadJob(job.RootPath);
        EqualMetadata(before, Metadata(job));
        _ = OurPlanCoreJobStore.LoadJob(job.RootPath); // Recovery is idempotent.
        EqualMetadata(before, Metadata(job));
    });

    private static void SortUndo() => WithJob(job =>
    {
        string scope = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Scope");
        OurPlanCoreJobStore.CreateFolder(scope, "Z"); OurPlanCoreJobStore.CreateFolder(scope, "A");
        string sibling = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Sibling");
        string[] before = OurPlanCoreJobStore.GetOrderedChildDirectories(scope).ToArray();
        OurPlanCoreJobStore.SortChildren(scope, false);
        OurPlanCoreJobStore.SetProperty(sibling, "UserNote", "Later independent edit");
        JobOperationJournal.AbandonForTests();
        _ = JobOperationJournal.UndoLast(job.RootPath, "page-sort");
        Check(before.SequenceEqual(OurPlanCoreJobStore.GetOrderedChildDirectories(scope)), "Order restored");
        Check(OurPlanCoreJobStore.ReadProperty(sibling, "UserNote") == "Later independent edit", "Sibling edit preserved");
    });

    private static void UndoConflict() => WithJob(job =>
    {
        string z = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Z"); OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "A");
        OurPlanCoreJobStore.SortChildren(job.PagesRoot, false);
        OurPlanCoreJobStore.SetProperty(z, "UserNote", "New edit");
        Throws(() => JobOperationJournal.UndoLast(job.RootPath, "page-sort"));
        Check(OurPlanCoreJobStore.ReadProperty(z, "UserNote") == "New edit", "Conflicting edit retained");
    });

    private static void UnsafeManifest() => WithJob(job =>
    {
        using (var op = JobOperationJournal.Begin(job.RootPath, "Fixture")) op.Commit();
        string manifest = Directory.GetFiles(Path.Combine(job.RootPath, ".undo", "operations"), "operation.json", SearchOption.AllDirectories).Single();
        var record = JsonSerializer.Deserialize<JobOperationJournal.OperationRecord>(File.ReadAllText(manifest))!;
        record.State = "pending"; record.Before.Directories.Add("../../outside");
        File.WriteAllText(manifest, JsonSerializer.Serialize(record));
        File.WriteAllText(Path.Combine(job.RootPath, ".undo", "operations", "pending-operation.txt"), Path.GetFileName(Path.GetDirectoryName(manifest)));
        Throws(() => JobOperationJournal.RecoverPending(job.RootPath));
    });

    private static TakeoffItem MeasuredItem(OurPlanCoreJob job)
    {
        PageInfo page = OurPlanCoreJobStore.CreateBlankPage(job, "A1", job.PagesRoot);
        TakeoffItem item = OurPlanCoreJobStore.CreateTakeoffItem(job, "Walls", "#FF0000");
        item.Measurements.Add(new Measurement { MType = "line", PageFolder = page.FolderPath, ScaleMetersPerPt = .01, Points = [new SKPoint(1, 2), new SKPoint(10, 20)] });
        OurPlanCoreJobStore.SaveTakeoffItem(item); return item;
    }

    private static void CorruptSourceNeedsRecovery() => WithJob(job =>
    {
        PageInfo page = OurPlanCoreJobStore.CreateBlankPage(job, "A1", job.PagesRoot);
        string path = Path.Combine(page.FolderPath, "source.json");
        File.WriteAllText(path, "{ broken source");
        Check(OurPlanCoreJobStore.TryReadPage(page.FolderPath) == null, "Corrupt source was not reconstructed with defaults");
        Check(!File.Exists(path) && File.Exists(path + ".read-protected"), "Quarantine and protection retained");
        Check(Directory.GetFiles(page.FolderPath, "source.json.corrupt-*").Select(File.ReadAllText).Single() == "{ broken source", "Exact corrupt bytes preserved");
    });

    private static void FailedPdfImport() => WithJob(job =>
    {
        PageInfo source = OurPlanCoreJobStore.CreateBlankPage(job, "Source", job.PagesRoot); var before = Metadata(job);
        JobOperationJournal.FailureInjectionForTests = stage => { if (stage == "before-commit") throw new IOException("Injected import failure"); };
        Throws(() => OurPlanCoreJobStore.ImportPdf(job, source.PdfPath, ["A1", "A2", "A3"], job.PagesRoot));
        JobOperationJournal.FailureInjectionForTests = null;
        EqualMetadata(before, Metadata(job));
    });

    private static void BulkCheckpointGate() => WithJob(job =>
    {
        using (var op = JobOperationJournal.Begin(job.RootPath, "Bulk fixture"))
        {
            using var checkpoint = JobFileWriteActivity.BeginPackageCheckpoint();
            Check(checkpoint.HadActiveWriters, "Packaging observes the entire bulk mutation as active");
            op.Commit();
        }
        Check(!JobFileWriteActivity.HasActiveBackgroundWriters, "Bulk writer was released");
    });

    private static void InterruptedBatch() => WithJob(job =>
    {
        PageInfo one = OurPlanCoreJobStore.CreateBlankPage(job, "A1", job.PagesRoot);
        PageInfo two = OurPlanCoreJobStore.CreateBlankPage(job, "A2", job.PagesRoot);
        string target = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Target"); var before = Metadata(job);
        int moves = 0;
        JobOperationJournal.FailureInjectionForTests = stage => { if (stage == "before-move" && ++moves == 2) throw new IOException("Second move failed"); };
        Throws(() => OurPlanCoreJobStore.MoveNodes([one.FolderPath, two.FolderPath], target));
        JobOperationJournal.FailureInjectionForTests = null;
        EqualMetadata(before, Metadata(job));
    });

    private static void EscapingPageSource() => WithJob(job =>
    {
        PageInfo page = OurPlanCoreJobStore.CreateBlankPage(job, "A1", job.PagesRoot);
        string source = Path.Combine(page.FolderPath, "source.json");
        File.WriteAllText(source, "{\"pdf\":\"../../../../private.pdf\"}");
        Check(PageStore.ReadSourceResult(page.FolderPath).State == DataFileState.Corrupt, "Outside PDF reference rejected");
    });
    private static void InterruptedUndo() => WithJob(job =>
    {
        OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Z"); OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "A");
        var before = Metadata(job);
        OurPlanCoreJobStore.SortChildren(job.PagesRoot, false);
        JobOperationJournal.FailureInjectionForTests = stage => { if (stage == "before-undo") throw new IOException("Process interruption"); };
        Throws(() => JobOperationJournal.UndoLast(job.RootPath, "page-sort"));
        JobOperationJournal.AbandonForTests();
        _ = OurPlanCoreJobStore.LoadJob(job.RootPath);
        EqualMetadata(before, Metadata(job));
        Check(!JobOperationJournal.HasPending(job.RootPath), "Interrupted undo recovered");
    });
    private static void EscapingOverlay() => WithJob(job =>
    {
        PageInfo page = OurPlanCoreJobStore.CreateBlankPage(job, "A1", job.PagesRoot);
        File.WriteAllText(SheetOverlayLayerStore.ManifestPath(page.FolderPath), "{\"layers\":[{\"source_page_folder\":\"../../../../private\"}]}");
        Check(SheetOverlayLayerStore.Load(page.FolderPath).Layers.Count == 0, "Unsafe overlay not loaded");
        Check(DataFileReader.IsProtected(page.FolderPath), "Unsafe overlay protected");
    });
    private static void LongRasterPaths() => WithJob(job =>
    {
        PageInfo page = OurPlanCoreJobStore.CreateBlankPage(job, "A1", job.PagesRoot);
        string folder = Path.Combine(page.FolderPath, new string('a', 90), new string('b', 90));
        Directory.CreateDirectory(folder);
        string imagePath = Path.Combine(folder, "working.png");
        using var original = new SKBitmap(32, 24);
        original.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(original);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(imagePath, encoded.ToArray());
        Check(imagePath.Length > 260, "Long real filesystem path");
        using SKBitmap? native = SKBitmap.Decode(imagePath);
        Console.WriteLine($"Raster path proof: {imagePath.Length} characters; native filename decode={(native == null ? "failed" : "available")}");
        var source = new RasterSheetSource { Enabled = true, RenderProfile = RasterSheetCacheService.SourceImageRasterProfile, Image = Path.GetRelativePath(page.FolderPath, imagePath),
            OverviewImage = Path.GetRelativePath(page.FolderPath, imagePath), WidthPt = 32, HeightPt = 24, RenderScale = 1, OverviewRenderScale = 1 };
        Check(RasterSheetCacheService.TryReadReady(page.FolderPath, page.PdfPath, source, out var full, out string reason), "Full raster: " + reason);
        using (full.Bitmap) Check(full.Bitmap.Width == 32 && full.Bitmap.GetPixel(10, 10) == SKColors.Red, "Full image pixels retained");
        Check(RasterSheetCacheService.TryReadOverviewReady(page.FolderPath, page.PdfPath, source, out var overview, out reason), "Overview: " + reason);
        using (overview.Bitmap) Check(overview.Bitmap.Height == 24 && overview.Bitmap.GetPixel(10, 10) == SKColors.Red, "Overview pixels retained");
    });
    private static Dictionary<string, string> Metadata(OurPlanCoreJob job) => Directory.GetFiles(job.RootPath, "*", SearchOption.AllDirectories)
        .Where(p => !Path.GetRelativePath(job.RootPath, p).StartsWith(".undo", StringComparison.OrdinalIgnoreCase))
        .Where(p => new[] { ".json", ".xml", ".jsonl", ".txt" }.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
        .ToDictionary(p => Path.GetRelativePath(job.RootPath, p), File.ReadAllText, StringComparer.OrdinalIgnoreCase);
    private static void EqualMetadata(Dictionary<string, string> a, Dictionary<string, string> b) =>
        Check(a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out string? text) && pair.Value == text), "Exact metadata restored");
    private static void WithJob(Action<OurPlanCoreJob> action)
    {
        string parent = Path.Combine(Path.GetTempPath(), "opc-data-safety-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        try { action(OurPlanCoreJobStore.CreateJob(parent, "Fixture")); }
        finally { JobOperationJournal.AbandonForTests(); DataFileReader.ResetForTests(); Directory.Delete(parent, true); }
    }
    private static void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); }
    private static void Throws(Action action)
    {
        try { action(); } catch (Exception) { return; }
        throw new InvalidOperationException("Expected operation to be rejected.");
    }
}
