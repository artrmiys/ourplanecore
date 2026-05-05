using OurPlaneCore;
using SkiaSharp;

var tests = new List<(string Name, Action Run)>
{
    ("measurement count value and label", MeasurementCountValueAndLabel),
    ("measurement line uses own scale first", MeasurementLineUsesOwnScaleFirst),
    ("measurement area uses fallback scale", MeasurementAreaUsesFallbackScale),
    ("measurement area joist without direction is blocked", MeasurementJoistWithoutDirectionIsBlocked),
    ("takeoff item normalizes count type totals", TakeoffItemNormalizesCountTotals),
    ("takeoff creation policy chooses safe parents", TakeoffCreationPolicyChoosesSafeParents),
    ("job store sanitizes unsafe names", JobStoreSanitizesUnsafeNames),
    ("pdf metadata page name and scale gate", PdfMetadataPageNameAndScaleGate),
    ("pdf scale parser handles architectural scale", PdfScaleParserHandlesArchitecturalScale),
    ("joist rounding aliases normalize", JoistRoundingAliasesNormalize),
    ("joist pitch normalizes common input", JoistPitchNormalizesCommonInput),
    ("joist pitch flat input normalizes empty", JoistPitchFlatInputNormalizesEmpty),
    ("joist pitch rejects invalid input", JoistPitchRejectsInvalidInput),
    ("joist pitch factor matches rise run", JoistPitchFactorMatchesRiseRun),
    ("joist pitch accepts single rise over twelve", JoistPitchAcceptsSingleRiseOverTwelve),
    ("joist pitch length applies slope factor", JoistPitchLengthAppliesSlopeFactor),
    ("joist pitch rounding applies per segment", JoistPitchRoundingAppliesPerSegment),
    ("joist pitch label shows indicator", JoistPitchLabelShowsIndicator),
    ("joist pitch persists on takeoff item", JoistPitchPersistsOnTakeoffItem),
    ("joist pitch applies item properties", JoistPitchAppliesItemProperties),
    ("job recovery normalizes snapshot reasons", JobRecoveryNormalizesSnapshotReasons),
    ("job recovery filters metadata files", JobRecoveryFiltersMetadataFiles),
    ("job recovery lock writes reads and clears", JobRecoveryLockWritesReadsAndClears),
    ("job recovery snapshot copies metadata only", JobRecoverySnapshotCopiesMetadataOnly),
    ("job recovery snapshot pruning keeps newest", JobRecoverySnapshotPruningKeepsNewest),
    ("app settings job roots dedupe", AppSettingsJobRootsDedupe),
    ("app settings recent job preserves pin and thumbnail", AppSettingsRecentPreservesPinAndThumbnail),
    ("app settings removes recent job by path", AppSettingsRemovesRecentJobByPath),
    ("pdf metadata needs fallback when scale is unresolved", PdfMetadataNeedsFallbackWhenScaleUnresolved),
    ("pdf metadata skip scale avoids fallback", PdfMetadataSkipScaleAvoidsFallback),
};

int passed = 0;
var failures = new List<string>();
foreach ((string name, Action run) in tests)
{
    try
    {
        run();
        passed++;
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
        Console.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

Console.WriteLine($"{passed}/{tests.Count} tests passed");
if (failures.Count > 0)
{
    Console.Error.WriteLine("Failures:");
    foreach (string failure in failures)
        Console.Error.WriteLine($"- {failure}");
    return 1;
}

return 0;

static void MeasurementCountValueAndLabel()
{
    var measurement = new Measurement
    {
        MType = "point",
        Points = [new SKPoint(1, 1), new SKPoint(2, 2), new SKPoint(3, 3)],
    };

    AssertClose(3.0, measurement.Value(0), "count value");
    AssertEqual("3 ea", measurement.Label(0), "count label");
}

static void MeasurementLineUsesOwnScaleFirst()
{
    var measurement = new Measurement
    {
        MType = "line",
        ScaleMetersPerPt = 2,
        Points = [new SKPoint(0, 0), new SKPoint(3, 4)],
    };

    AssertClose(10.0, measurement.Value(100), "line length should use measurement scale");
}

static void MeasurementAreaUsesFallbackScale()
{
    var measurement = new Measurement
    {
        MType = "area",
        Points =
        [
            new SKPoint(0, 0),
            new SKPoint(10, 0),
            new SKPoint(10, 10),
            new SKPoint(0, 10),
        ],
    };

    AssertClose(25.0, measurement.AreaValue(0.5), "scaled area");
}

static void MeasurementJoistWithoutDirectionIsBlocked()
{
    var measurement = new Measurement
    {
        MType = "area",
        JoistEnabled = true,
        JoistDirectionLocked = false,
        Points =
        [
            new SKPoint(0, 0),
            new SKPoint(10, 0),
            new SKPoint(10, 10),
            new SKPoint(0, 10),
        ],
    };

    AssertClose(0.0, measurement.Value(0.5), "pending joist value");
    AssertEqual("joists: set direction", measurement.Label(0.5), "pending joist label");
}

static void TakeoffItemNormalizesCountTotals()
{
    var item = new TakeoffItem
    {
        MeasurementType = "count",
    };
    item.Measurements.Add(new Measurement
    {
        MType = "point",
        Points = [new SKPoint(0, 0), new SKPoint(1, 1)],
    });

    AssertEqual("2 ea", item.TotalLabel(0), "count total label");
}

static void TakeoffCreationPolicyChoosesSafeParents()
{
    var job = new OurPlaneCoreJob
    {
        RootPath = @"C:\tmp\job",
    };

    string selected = @"C:\tmp\job\Takeoffs\selected";
    string itemParent = @"C:\tmp\job\Takeoffs\item";
    string active = @"C:\tmp\job\Takeoffs\active";

    AssertEqual(job.TakeoffsRoot, TakeoffCreationPolicy.NewItemParentFolder(job), "new item parent");
    AssertEqual(selected, TakeoffCreationPolicy.NewFolderParentFolder(job, selected, itemParent, active, _ => false), "selected folder wins");
    AssertEqual(itemParent, TakeoffCreationPolicy.NewFolderParentFolder(job, "", itemParent, active, _ => false), "selected item parent wins");
    AssertEqual(active, TakeoffCreationPolicy.NewFolderParentFolder(job, "", "", active, path => path == active), "active existing folder wins");
    AssertEqual(job.TakeoffsRoot, TakeoffCreationPolicy.NewFolderParentFolder(job, "", "", active, _ => false), "root fallback");
}

static void JobStoreSanitizesUnsafeNames()
{
    string clean = OurPlaneCoreJobStore.SanitizeName("  bad:name?.  ", 120);
    AssertEqual("bad_name_", clean, "sanitized invalid characters");

    string truncated = OurPlaneCoreJobStore.SanitizeName(new string('a', 10), 4);
    AssertEqual("aaaa", truncated, "max length");
}

static void PdfMetadataPageNameAndScaleGate()
{
    var metadata = new PdfSheetMetadata
    {
        SheetKey = "s100",
        Suffix = "f",
        SelectedScaleMetersPerPt = 1,
    };

    AssertEqual("s100 f", metadata.ProposedPageName(), "proposed page name");
    AssertTrue(metadata.CanApplyScale(), "scale can apply before skip");

    metadata.SkipScale = true;
    AssertFalse(metadata.CanApplyScale(), "skip scale blocks scale apply");
}

static void PdfScaleParserHandlesArchitecturalScale()
{
    bool parsed = PdfSheetMetadataService.TryParseScaleMetersPerPt("1/8\" = 1'0\"", out double metersPerPt);

    AssertTrue(parsed, "scale should parse");
    AssertTrue(metersPerPt > 0, "scale should be positive");
    AssertEqual("1/8\" = 1'0\"", PdfSheetMetadataService.FormatImperialScale(metersPerPt), "roundtrip label");
}

static void JoistRoundingAliasesNormalize()
{
    AssertEqual(JoistTakeoffCalculator.RoundingNearestFoot, JoistTakeoffCalculator.NormalizeLengthRounding("foot"), "foot alias");
    AssertEqual(JoistTakeoffCalculator.RoundingNearestEvenFoot, JoistTakeoffCalculator.NormalizeLengthRounding("even foot"), "even alias");
    AssertEqual(JoistTakeoffCalculator.RoundingNearestTwoFeet, JoistTakeoffCalculator.NormalizeLengthRounding("2 feet"), "two feet alias");
    AssertEqual("Nearest 2 Feet", JoistTakeoffCalculator.LengthRoundingTitle("nearesttwofeet"), "rounding title");
}

static void JoistPitchNormalizesCommonInput()
{
    AssertEqual("3:12", JoistTakeoffCalculator.NormalizePitch("3:12"), "colon pitch");
    AssertEqual("3:12", JoistTakeoffCalculator.NormalizePitch("3/12"), "slash pitch");
    AssertEqual("3.5:12", JoistTakeoffCalculator.NormalizePitch("3,5 in 12"), "decimal pitch");
}

static void JoistPitchFlatInputNormalizesEmpty()
{
    AssertEqual("", JoistTakeoffCalculator.NormalizePitch(""), "blank pitch");
    AssertEqual("", JoistTakeoffCalculator.NormalizePitch("0:12"), "flat pitch");
}

static void JoistPitchRejectsInvalidInput()
{
    AssertFalse(JoistTakeoffCalculator.TryNormalizePitch("3:0", out _), "zero run rejected");
    AssertFalse(JoistTakeoffCalculator.TryParsePitchFactor("bad", out _), "bad pitch rejected");
}

static void JoistPitchFactorMatchesRiseRun()
{
    AssertTrue(JoistTakeoffCalculator.TryParsePitchFactor("3:12", out double factor), "factor parsed");
    AssertClose(Math.Sqrt(153) / 12.0, factor, "3:12 factor");
}

static void JoistPitchAcceptsSingleRiseOverTwelve()
{
    AssertEqual("4:12", JoistTakeoffCalculator.NormalizePitch("4"), "single rise default run");
}

static void JoistPitchLengthAppliesSlopeFactor()
{
    JoistLayoutResult flat = JoistTakeoffCalculator.Calculate(
        SimpleJoistAreaPolygon(),
        0.3048,
        120,
        0,
        JoistTakeoffCalculator.RoundingNone);
    JoistLayoutResult pitched = JoistTakeoffCalculator.Calculate(
        SimpleJoistAreaPolygon(),
        0.3048,
        120,
        0,
        JoistTakeoffCalculator.RoundingNone,
        "3:12");

    AssertTrue(JoistTakeoffCalculator.TryParsePitchFactor("3:12", out double factor), "pitch factor");
    AssertClose(flat.TotalRawLengthMeters * factor, pitched.TotalRawLengthMeters, "sloped raw length");
    AssertEqual("3:12", pitched.Pitch, "layout pitch");
}

static void JoistPitchRoundingAppliesPerSegment()
{
    JoistLayoutResult rounded = JoistTakeoffCalculator.Calculate(
        SimpleJoistAreaPolygon(),
        0.3048,
        120,
        0,
        JoistTakeoffCalculator.RoundingNearestEvenFoot,
        "6:12");

    AssertEqual("2", rounded.Count.ToString(), "joist count");
    AssertClose(20.0, rounded.TotalLengthMeters / 0.3048, "each sloped 8 ft joist rounds up to 10 ft");
}

static void JoistPitchLabelShowsIndicator()
{
    var measurement = new Measurement
    {
        MType = "area",
        JoistEnabled = true,
        JoistDirectionLocked = true,
        JoistSpacingInches = 120,
        JoistPitch = "3:12",
        JoistLengthRounding = JoistTakeoffCalculator.RoundingNone,
        ScaleMetersPerPt = 0.3048,
        Points = SimpleJoistAreaPolygon().ToList(),
    };

    AssertTrue(measurement.Label(0.3048, UnitMode.Imperial).Contains("Pitch 3:12", StringComparison.Ordinal), "label pitch line");
}

static void JoistPitchPersistsOnTakeoffItem()
{
    WithTempJob("Joist Pitch", job =>
    {
        TakeoffItem item = OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Roof Joists", "#FF0000", "area");
        item.IsJoistTakeoff = true;
        item.JoistPitch = "3/12";
        item.JoistSpacingInches = 120;
        item.JoistLengthRounding = JoistTakeoffCalculator.RoundingNone;
        item.Measurements.Add(new Measurement
        {
            MType = "area",
            JoistDirectionLocked = true,
            ScaleMetersPerPt = 0.3048,
            Points = SimpleJoistAreaPolygon().ToList(),
        });

        OurPlaneCoreJobStore.SaveTakeoffItem(item);
        TakeoffItem loaded = OurPlaneCoreJobStore.TryReadTakeoffItem(item.FolderPath)
            ?? throw new InvalidOperationException("takeoff item not loaded");

        AssertEqual("3:12", loaded.JoistPitch, "loaded item pitch");
        AssertEqual("3:12", loaded.Measurements[0].JoistPitch, "loaded measurement pitch");
    });
}

static void JoistPitchAppliesItemProperties()
{
    var item = new TakeoffItem
    {
        MeasurementType = "area",
        IsJoistTakeoff = true,
        JoistPitch = "4/12",
    };
    item.Measurements.Add(new Measurement
    {
        MType = "area",
        Points = SimpleJoistAreaPolygon().ToList(),
    });

    OurPlaneCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);

    AssertEqual("4:12", item.Measurements[0].JoistPitch, "measurement pitch copied");
}

static void JobRecoveryNormalizesSnapshotReasons()
{
    AssertEqual("manual_save", JobRecoveryService.NormalizeSnapshotReason("Manual Save"), "spaces become underscores");
    AssertEqual("before_switch", JobRecoveryService.NormalizeSnapshotReason(" before-switch "), "symbols become underscores");
    AssertEqual("snapshot", JobRecoveryService.NormalizeSnapshotReason(" / "), "empty fallback");
}

static void JobRecoveryFiltersMetadataFiles()
{
    AssertTrue(JobRecoveryService.ShouldCopySnapshotFile(@"C:\job\Takeoffs\Walls\measurements.json"), "measurements copied");
    AssertTrue(JobRecoveryService.ShouldCopySnapshotFile(@"C:\job\Pages\a100\source_pdf.json"), "source metadata copied");
    AssertFalse(JobRecoveryService.ShouldCopySnapshotFile(@"C:\job\sources\plans.pdf"), "pdf skipped");
    AssertFalse(JobRecoveryService.ShouldCopySnapshotFile(@"C:\job\AI_Context\crops\crop.png"), "image skipped");
}

static void JobRecoveryLockWritesReadsAndClears()
{
    WithTempJob("Recovery Lock", job =>
    {
        JobRecoveryService.WriteLock(job);
        AssertTrue(File.Exists(JobRecoveryService.LockPath(job)), "lock written");
        AssertTrue(JobRecoveryService.TryReadLock(job, out JobRecoveryLockInfo info), "lock read");
        AssertEqual(Environment.ProcessId.ToString(), info.ProcessId.ToString(), "process id");
        AssertFalse(JobRecoveryService.IsStaleLock(info), "current lock should not be stale");

        JobRecoveryService.ClearLock(job);
        AssertFalse(File.Exists(JobRecoveryService.LockPath(job)), "lock cleared");
    });
}

static void JobRecoverySnapshotCopiesMetadataOnly()
{
    WithTempJob("Recovery Snapshot", job =>
    {
        string page = OurPlaneCoreJobStore.EnsureFolder(job.PagesRoot, "A100");
        File.WriteAllText(Path.Combine(page, "source.json"), "{}");
        File.WriteAllText(Path.Combine(page, "sheet.pdf"), "not a real pdf");

        TakeoffItem item = OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Walls", "#FF0000", "line");
        item.Measurements.Add(new Measurement
        {
            MType = "line",
            Points = [new SKPoint(0, 0), new SKPoint(1, 1)],
        });
        OurPlaneCoreJobStore.SaveTakeoffItem(item);

        string snapshot = JobRecoveryService.SaveSnapshot(job, "manual save", maxSnapshots: 5);
        AssertTrue(File.Exists(Path.Combine(snapshot, "snapshot_manifest.json")), "manifest copied");
        AssertTrue(File.Exists(Path.Combine(snapshot, "Pages", "A100", "source.json")), "page source copied");
        AssertTrue(File.Exists(Path.Combine(snapshot, "Takeoffs", "Walls", "measurements.json")), "measurements copied");
        AssertFalse(File.Exists(Path.Combine(snapshot, "Pages", "A100", "sheet.pdf")), "pdf skipped");
    });
}

static void JobRecoverySnapshotPruningKeepsNewest()
{
    WithTempJob("Recovery Prune", job =>
    {
        JobRecoveryService.SaveSnapshot(job, "one", maxSnapshots: 2);
        JobRecoveryService.SaveSnapshot(job, "two", maxSnapshots: 2);
        JobRecoveryService.SaveSnapshot(job, "three", maxSnapshots: 2);

        int count = Directory.EnumerateDirectories(JobRecoveryService.SnapshotRoot(job)).Count();
        AssertEqual("2", count.ToString(), "pruned snapshot count");
    });
}

static void AppSettingsJobRootsDedupe()
{
    var settings = new AppSettings();
    string root = Path.Combine(Path.GetTempPath(), "opc_jobs");
    AppSettingsStore.AddJobsRoot(settings, root);
    AppSettingsStore.AddJobsRoot(settings, root + Path.DirectorySeparatorChar);

    AssertEqual("1", AppSettingsStore.CurrentJobsRootPaths(settings).Count.ToString(), "deduped roots");
}

static void AppSettingsRecentPreservesPinAndThumbnail()
{
    var settings = new AppSettings();
    string path = Path.Combine(Path.GetTempPath(), "opc_job");
    settings.RecentJobs.Add(new RecentJobInfo
    {
        Name = "Old",
        Path = path,
        ThumbnailPath = "thumb.png",
        IsPinned = true,
    });

    AppSettingsStore.AddRecentJob(settings, path, "New");

    AssertEqual("1", settings.RecentJobs.Count.ToString(), "deduped recent job");
    AssertEqual("New", settings.RecentJobs[0].Name, "updated name");
    AssertEqual("thumb.png", settings.RecentJobs[0].ThumbnailPath, "thumbnail preserved");
    AssertTrue(settings.RecentJobs[0].IsPinned, "pin preserved");
}

static void AppSettingsRemovesRecentJobByPath()
{
    var settings = new AppSettings();
    string path = Path.Combine(Path.GetTempPath(), "opc_job_remove");
    AppSettingsStore.AddRecentJob(settings, path, "Remove Me");
    AppSettingsStore.RemoveRecentJob(settings, path + Path.DirectorySeparatorChar);

    AssertEqual("0", settings.RecentJobs.Count.ToString(), "recent job removed");
}

static void PdfMetadataNeedsFallbackWhenScaleUnresolved()
{
    var metadata = new PdfSheetMetadata
    {
        SheetLabel = "A100",
        SheetTitle = "FIRST FLOOR PLAN",
        Suffix = "1st",
        SkipScale = false,
    };

    AssertTrue(PdfSheetMetadataService.NeedsFallback(metadata), "missing needed scale should need fallback");
}

static void PdfMetadataSkipScaleAvoidsFallback()
{
    var metadata = new PdfSheetMetadata
    {
        SheetLabel = "D1",
        SheetTitle = "DETAILS",
        Suffix = "d",
        SkipScale = true,
    };

    AssertFalse(PdfSheetMetadataService.NeedsFallback(metadata), "skip scale detail should not need fallback");
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void AssertFalse(bool condition, string message) =>
    AssertTrue(!condition, message);

static void AssertEqual(string expected, string actual, string message)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new InvalidOperationException($"{message}: expected '{expected}', got '{actual}'");
}

static void AssertClose(double expected, double actual, string message, double tolerance = 0.000001)
{
    if (Math.Abs(expected - actual) > tolerance)
        throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
}

static IReadOnlyList<SKPoint> SimpleJoistAreaPolygon() =>
[
    new SKPoint(0, 0),
    new SKPoint(8, 0),
    new SKPoint(8, 10),
    new SKPoint(0, 10),
];

static void WithTempJob(string name, Action<OurPlaneCoreJob> action)
{
    string root = Path.Combine(Path.GetTempPath(), "opc_tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        File.WriteAllText(Path.Combine(root, "Data.xml"), $"<Item Class=\"Folder\" Name=\"{name}\" />");
        OurPlaneCoreJobStore.EnsureFolder(root, "Pages");
        OurPlaneCoreJobStore.EnsureFolder(root, "Takeoffs");
        var job = new OurPlaneCoreJob
        {
            Name = name,
            RootPath = root,
        };
        action(job);
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
