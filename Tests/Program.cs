using OurPlaneCore;
using SkiaSharp;
using System.Reflection;

string testGlobalRoot = Path.Combine(Path.GetTempPath(), "opc_tests_global", Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable(SmartContextStore.GlobalRootEnvironmentVariable, testGlobalRoot);

var tests = new List<(string Name, Action Run)>
{
    ("measurement count value and label", MeasurementCountValueAndLabel),
    ("measurement line uses own scale first", MeasurementLineUsesOwnScaleFirst),
    ("measurement area uses fallback scale", MeasurementAreaUsesFallbackScale),
    ("measurement area subtracts holes", MeasurementAreaSubtractsHoles),
    ("pdf export area path cuts holes", PdfExportAreaPathCutsHoles),
    ("job store persists measurement holes", JobStorePersistsMeasurementHoles),
    ("measurement area joist without direction is blocked", MeasurementJoistWithoutDirectionIsBlocked),
    ("takeoff item normalizes count type totals", TakeoffItemNormalizesCountTotals),
    ("takeoff creation policy chooses safe parents", TakeoffCreationPolicyChoosesSafeParents),
    ("takeoff section order moves single up", TakeoffSectionOrderMovesSingleUp),
    ("takeoff section order moves single down", TakeoffSectionOrderMovesSingleDown),
    ("takeoff section order blocks top up", TakeoffSectionOrderBlocksTopUp),
    ("takeoff section order blocks bottom down", TakeoffSectionOrderBlocksBottomDown),
    ("takeoff section order blocks all selected", TakeoffSectionOrderBlocksAllSelected),
    ("takeoff section order moves contiguous block up", TakeoffSectionOrderMovesContiguousBlockUp),
    ("takeoff section order moves contiguous block down", TakeoffSectionOrderMovesContiguousBlockDown),
    ("takeoff section order moves disjoint selection up", TakeoffSectionOrderMovesDisjointSelectionUp),
    ("takeoff section order moves disjoint selection down", TakeoffSectionOrderMovesDisjointSelectionDown),
    ("takeoff section order ignores invalid duplicate ids", TakeoffSectionOrderIgnoresInvalidDuplicateIds),
    ("takeoff tree order keeps creation order", TakeoffTreeOrderKeepsCreationOrder),
    ("takeoff tree order moves sibling up", TakeoffTreeOrderMovesSiblingUp),
    ("takeoff tree order moves sibling down", TakeoffTreeOrderMovesSiblingDown),
    ("takeoff tree order blocks top up", TakeoffTreeOrderBlocksTopUp),
    ("takeoff tree order blocks bottom down", TakeoffTreeOrderBlocksBottomDown),
    ("takeoff tree order moves sibling block up", TakeoffTreeOrderMovesSiblingBlockUp),
    ("takeoff tree order moves sibling block down", TakeoffTreeOrderMovesSiblingBlockDown),
    ("takeoff tree order moves before target", TakeoffTreeOrderMovesBeforeTarget),
    ("takeoff tree order moves after target", TakeoffTreeOrderMovesAfterTarget),
    ("takeoff tree order appends moved node into folder", TakeoffTreeOrderAppendsMovedNodeIntoFolder),
    ("page tree order moves sheet before folder", PageTreeOrderMovesSheetBeforeFolder),
    ("page tree order moves folder before folder", PageTreeOrderMovesFolderBeforeFolder),
    ("page tree order moves nested folder out below parent", PageTreeOrderMovesNestedFolderOutBelowParent),
    ("page rename allows duplicate display names", PageRenameAllowsDuplicateDisplayNames),
    ("job store sanitizes unsafe names", JobStoreSanitizesUnsafeNames),
    ("node sort uses natural page order", StorageTests.NodeSortUsesNaturalPageOrder),
    ("duplicate page clones page and rejects folder", StorageTests.DuplicatePageClonesPageAndRejectsFolder),
    ("tree expansion starts collapsed and tracks user opened paths", TreeExpansionStateTests.StartsCollapsedAndTracksUserOpenedPaths),
    ("tree expansion restores snapshot across reload", TreeExpansionStateTests.RestoresSnapshotAcrossReload),
    ("tree expansion rebases moved descendants", TreeExpansionStateTests.RebasesMovedDescendants),
    ("job layout create and load ensures base folders", StorageTests.JobLayoutCreateAndLoadEnsuresBaseFolders),
    ("page import writes layer manifest and metadata", StorageTests.PageImportWritesLayerManifestAndMetadata),
    ("page copy and move preserve source overlay and layers", StorageTests.PageCopyAndMovePreserveSourceOverlayAndLayers),
    ("page corrupt source json is quarantined", StorageTests.PageCorruptSourceJsonIsQuarantined),
    ("page annotations save load normalize defaults", StorageTests.PageAnnotationsSaveLoadNormalizeDefaults),
    ("page corrupt annotations json is quarantined", StorageTests.PageCorruptAnnotationsJsonIsQuarantined),
    ("takeoff save writes counters and reloads fallback scale", StorageTests.TakeoffSaveWritesCountersAndReloadsFallbackScale),
    ("takeoff corrupt measurements json is quarantined", StorageTests.TakeoffCorruptMeasurementsJsonIsQuarantined),
    ("pdf metadata page name and scale gate", PdfMetadataPageNameAndScaleGate),
    ("pdf scale parser handles architectural scale", PdfScaleParserHandlesArchitecturalScale),
    ("pdf scale parser handles mixed fraction scale", PdfScaleParserHandlesMixedFractionScale),
    ("joist rounding aliases normalize", JoistRoundingAliasesNormalize),
    ("joist pitch normalizes common input", JoistPitchNormalizesCommonInput),
    ("joist pitch flat input normalizes empty", JoistPitchFlatInputNormalizesEmpty),
    ("joist pitch rejects invalid input", JoistPitchRejectsInvalidInput),
    ("joist pitch factor matches rise run", JoistPitchFactorMatchesRiseRun),
    ("joist pitch accepts single rise over twelve", JoistPitchAcceptsSingleRiseOverTwelve),
    ("joist layout subtracts area cut holes", JoistLayoutSubtractsAreaCutHoles),
    ("joist pitch length applies slope factor", JoistPitchLengthAppliesSlopeFactor),
    ("joist pitch rounding applies per segment", JoistPitchRoundingAppliesPerSegment),
    ("joist pitch label shows indicator", JoistPitchLabelShowsIndicator),
    ("joist length label shows order and raw lengths", JoistLengthLabelShowsOrderAndRawLengths),
    ("joist length label can use standard format", JoistLengthLabelCanUseStandardFormat),
    ("joist pitch label explains flat slope and order lengths", JoistPitchLabelExplainsFlatSlopeAndOrderLengths),
    ("joist export uses visible label lines", JoistExportUsesVisibleLabelLines),
    ("joist pitch persists on takeoff item", JoistPitchPersistsOnTakeoffItem),
    ("joist pitch applies item properties", JoistPitchAppliesItemProperties),
    ("page overlay persists through source rewrites", PageOverlayPersistsThroughSourceRewrites),
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
    ("viewport render scale chooses next quality step", ViewportRenderScaleChoosesNextQualityStep),
    ("viewport background defaults to opaque white", ViewportBackgroundDefaultsToOpaqueWhite),
    ("viewport background strips transparency", ViewportBackgroundStripsTransparency),
    ("viewport background tints comfort colors", ViewportBackgroundTintsComfortColors),
    ("viewport high zoom respects fast navigation toggle", ViewportHighZoomRespectsFastNavigationToggle),
    ("viewport far zoom respects fast navigation toggle", ViewportFarZoomRespectsFastNavigationToggle),
    ("viewport dense page respects fast navigation toggle", ViewportDensePageRespectsFastNavigationToggle),
    ("viewport editing blocks fast navigation frame", ViewportEditingBlocksFastNavigationFrame),
    ("viewport measurement labels survive distant zoom", ViewportMeasurementLabelsSurviveDistantZoom),
    ("viewport measurement LOD limits dense details", ViewportMeasurementLodLimitsDenseDetails),
    ("viewport LOD hides expensive layers during fast frames", ViewportLodHidesExpensiveLayersDuringFastFrames),
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
        string detail = string.Equals(
            Environment.GetEnvironmentVariable("OURPLANECORE_TEST_VERBOSE_FAILURES"),
            "1",
            StringComparison.Ordinal)
            ? ex.ToString()
            : ex.Message;
        failures.Add($"{name}: {detail}");
        Console.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

Console.WriteLine($"{passed}/{tests.Count} tests passed");
if (failures.Count > 0)
{
    Console.Error.WriteLine("Failures:");
    foreach (string failure in failures)
        Console.Error.WriteLine($"- {failure}");
    CleanupTestGlobalRoot(testGlobalRoot);
    return 1;
}

CleanupTestGlobalRoot(testGlobalRoot);
return 0;

static void CleanupTestGlobalRoot(string path)
{
    TryDeleteDirectory(path);
}

static void TryDeleteDirectory(string path)
{
    try
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
    catch
    {
    }
}

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

static void MeasurementAreaSubtractsHoles()
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
        Holes =
        [
            [
                new SKPoint(2, 2),
                new SKPoint(5, 2),
                new SKPoint(5, 6),
                new SKPoint(2, 6),
            ],
        ],
    };

    AssertClose(88.0, measurement.AreaValue(1), "area should subtract the 3x4 hole");
}

static void PdfExportAreaPathCutsHoles()
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
        Holes =
        [
            [
                new SKPoint(2, 2),
                new SKPoint(5, 2),
                new SKPoint(5, 6),
                new SKPoint(2, 6),
            ],
        ],
    };

    MethodInfo method = typeof(PdfExporter).GetMethod(
        "BuildPdfExportAreaPath",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("PDF export area path helper was not found.");

    using var path = (SKPath)(method.Invoke(null, [measurement])
        ?? throw new InvalidOperationException("PDF export area path helper returned null."));

    AssertEqual(SKPathFillType.EvenOdd.ToString(), path.FillType.ToString(), "export area path fill rule");
    AssertEqual("8", path.PointCount.ToString(), "export area path should include outer and hole contour points");
    AssertTrue(path.Contains(1, 1), "export area path should include outer area");
    AssertFalse(path.Contains(3, 3), "export area path should cut the hole");
    AssertTrue(path.Contains(8, 8), "export area path should include area outside the hole");
}

static void JobStorePersistsMeasurementHoles()
{
    WithTempJob("Hole Job", job =>
    {
        TakeoffItem item = OurPlaneCoreJobStore.CreateTakeoffItem(
            job,
            job.TakeoffsRoot,
            "Area",
            "#FF4444",
            "area");
        var measurement = new Measurement
        {
            MType = "area",
            PageFolder = job.PagesRoot,
            Points =
            [
                new SKPoint(0, 0),
                new SKPoint(12, 0),
                new SKPoint(12, 10),
                new SKPoint(0, 10),
            ],
            Holes =
            [
                [
                    new SKPoint(3, 3),
                    new SKPoint(7, 3),
                    new SKPoint(7, 6),
                    new SKPoint(3, 6),
                ],
            ],
        };

        item.Measurements.Add(measurement);
        OurPlaneCoreJobStore.SaveTakeoffItem(item);
        List<Measurement> loaded = OurPlaneCoreJobStore.LoadMeasurements(item.FolderPath);

        AssertEqual("1", loaded.Count.ToString(), "loaded measurement count");
        AssertEqual("1", loaded[0].Holes.Count.ToString(), "loaded hole count");
        AssertEqual("4", loaded[0].Holes[0].Count.ToString(), "loaded hole vertices");
        AssertClose(108.0, loaded[0].AreaValue(1), "loaded area should subtract persisted hole");
    });
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

static void TakeoffSectionOrderMovesSingleUp()
{
    var measurements = SectionMeasurements("a", "b", "c");

    AssertTrue(TakeoffSectionOrderService.Move(measurements, ["b"], -1), "move should apply");
    AssertSectionOrder("b,a,c", measurements, "single up");
}

static void TakeoffSectionOrderMovesSingleDown()
{
    var measurements = SectionMeasurements("a", "b", "c");

    AssertTrue(TakeoffSectionOrderService.Move(measurements, ["b"], 1), "move should apply");
    AssertSectionOrder("a,c,b", measurements, "single down");
}

static void TakeoffSectionOrderBlocksTopUp()
{
    var measurements = SectionMeasurements("a", "b", "c");

    AssertFalse(TakeoffSectionOrderService.Move(measurements, ["a"], -1), "top cannot move up");
    AssertSectionOrder("a,b,c", measurements, "top up should not change order");
}

static void TakeoffSectionOrderBlocksBottomDown()
{
    var measurements = SectionMeasurements("a", "b", "c");

    AssertFalse(TakeoffSectionOrderService.Move(measurements, ["c"], 1), "bottom cannot move down");
    AssertSectionOrder("a,b,c", measurements, "bottom down should not change order");
}

static void TakeoffSectionOrderBlocksAllSelected()
{
    var measurements = SectionMeasurements("a", "b", "c");

    AssertFalse(TakeoffSectionOrderService.CanMove(measurements, ["a", "b", "c"], -1), "all selected cannot move up");
    AssertFalse(TakeoffSectionOrderService.CanMove(measurements, ["a", "b", "c"], 1), "all selected cannot move down");
    AssertFalse(TakeoffSectionOrderService.Move(measurements, ["a", "b", "c"], 1), "all selected move should be blocked");
    AssertSectionOrder("a,b,c", measurements, "all selected should not change order");
}

static void TakeoffSectionOrderMovesContiguousBlockUp()
{
    var measurements = SectionMeasurements("a", "b", "c", "d");

    AssertTrue(TakeoffSectionOrderService.Move(measurements, ["b", "c"], -1), "block should move up");
    AssertSectionOrder("b,c,a,d", measurements, "contiguous block up");
}

static void TakeoffSectionOrderMovesContiguousBlockDown()
{
    var measurements = SectionMeasurements("a", "b", "c", "d");

    AssertTrue(TakeoffSectionOrderService.Move(measurements, ["b", "c"], 1), "block should move down");
    AssertSectionOrder("a,d,b,c", measurements, "contiguous block down");
}

static void TakeoffSectionOrderMovesDisjointSelectionUp()
{
    var measurements = SectionMeasurements("a", "b", "c", "d", "e");

    AssertTrue(TakeoffSectionOrderService.Move(measurements, ["c", "e"], -1), "disjoint selection should move up");
    AssertSectionOrder("a,c,b,e,d", measurements, "disjoint up");
}

static void TakeoffSectionOrderMovesDisjointSelectionDown()
{
    var measurements = SectionMeasurements("a", "b", "c", "d", "e");

    AssertTrue(TakeoffSectionOrderService.Move(measurements, ["a", "c"], 1), "disjoint selection should move down");
    AssertSectionOrder("b,a,d,c,e", measurements, "disjoint down");
}

static void TakeoffSectionOrderIgnoresInvalidDuplicateIds()
{
    var measurements = SectionMeasurements("a", "b", "c");

    AssertTrue(TakeoffSectionOrderService.Move(measurements, ["missing", "b", "b", ""], -1), "valid selected id should move once");
    AssertSectionOrder("b,a,c", measurements, "invalid and duplicate ids should not duplicate moves");

    AssertFalse(TakeoffSectionOrderService.Move(measurements, ["missing", ""], 1), "invalid-only selection should not move");
    AssertSectionOrder("b,a,c", measurements, "invalid-only selection should leave order unchanged");
}

static void TakeoffTreeOrderKeepsCreationOrder()
{
    WithTempJob("tree_order_create", job =>
    {
        CreateRootTakeoffItem(job, "B");
        CreateTakeoffFolder(job, "A");
        CreateRootTakeoffItem(job, "C");

        AssertTakeoffChildOrder(job.TakeoffsRoot, "B,A,C", "creation order");
    });
}

static void TakeoffTreeOrderMovesSiblingUp()
{
    WithTempJob("tree_order_up", job =>
    {
        var items = CreateTakeoffItems(job, "A", "B", "C");

        AssertTrue(OurPlaneCoreJobStore.MoveSibling(items[1].FolderPath, -1), "move up should apply");
        AssertTakeoffChildOrder(job.TakeoffsRoot, "B,A,C", "sibling up");
    });
}

static void TakeoffTreeOrderMovesSiblingDown()
{
    WithTempJob("tree_order_down", job =>
    {
        var items = CreateTakeoffItems(job, "A", "B", "C");

        AssertTrue(OurPlaneCoreJobStore.MoveSibling(items[0].FolderPath, 1), "move down should apply");
        AssertTakeoffChildOrder(job.TakeoffsRoot, "B,A,C", "sibling down");
    });
}

static void TakeoffTreeOrderBlocksTopUp()
{
    WithTempJob("tree_order_top", job =>
    {
        var items = CreateTakeoffItems(job, "A", "B", "C");

        AssertFalse(OurPlaneCoreJobStore.MoveSibling(items[0].FolderPath, -1), "top move up should be blocked");
        AssertTakeoffChildOrder(job.TakeoffsRoot, "A,B,C", "top up blocked");
    });
}

static void TakeoffTreeOrderBlocksBottomDown()
{
    WithTempJob("tree_order_bottom", job =>
    {
        var items = CreateTakeoffItems(job, "A", "B", "C");

        AssertFalse(OurPlaneCoreJobStore.MoveSibling(items[2].FolderPath, 1), "bottom move down should be blocked");
        AssertTakeoffChildOrder(job.TakeoffsRoot, "A,B,C", "bottom down blocked");
    });
}

static void TakeoffTreeOrderMovesSiblingBlockUp()
{
    WithTempJob("tree_order_block_up", job =>
    {
        var items = CreateTakeoffItems(job, "A", "B", "C", "D");

        AssertTrue(OurPlaneCoreJobStore.MoveSiblings([items[1].FolderPath, items[2].FolderPath], -1), "block up should apply");
        AssertTakeoffChildOrder(job.TakeoffsRoot, "B,C,A,D", "block up");
    });
}

static void TakeoffTreeOrderMovesSiblingBlockDown()
{
    WithTempJob("tree_order_block_down", job =>
    {
        var items = CreateTakeoffItems(job, "A", "B", "C", "D");

        AssertTrue(OurPlaneCoreJobStore.MoveSiblings([items[1].FolderPath, items[2].FolderPath], 1), "block down should apply");
        AssertTakeoffChildOrder(job.TakeoffsRoot, "A,D,B,C", "block down");
    });
}

static void TakeoffTreeOrderMovesBeforeTarget()
{
    WithTempJob("tree_order_before", job =>
    {
        var items = CreateTakeoffItems(job, "A", "B", "C", "D");

        AssertTrue(OurPlaneCoreJobStore.MoveSiblingsToPosition([items[2].FolderPath], items[0].FolderPath, after: false), "move before should apply");
        AssertTakeoffChildOrder(job.TakeoffsRoot, "C,A,B,D", "move before target");
    });
}

static void TakeoffTreeOrderMovesAfterTarget()
{
    WithTempJob("tree_order_after", job =>
    {
        var items = CreateTakeoffItems(job, "A", "B", "C", "D");

        AssertTrue(OurPlaneCoreJobStore.MoveSiblingsToPosition([items[0].FolderPath], items[2].FolderPath, after: true), "move after should apply");
        AssertTakeoffChildOrder(job.TakeoffsRoot, "B,C,A,D", "move after target");
    });
}

static void TakeoffTreeOrderAppendsMovedNodeIntoFolder()
{
    WithTempJob("tree_order_into_folder", job =>
    {
        string targetFolder = CreateTakeoffFolder(job, "Folder");
        CreateNestedTakeoffItem(job, targetFolder, "X");
        CreateNestedTakeoffItem(job, targetFolder, "Y");
        TakeoffItem moving = CreateRootTakeoffItem(job, "A");

        string movedPath = OurPlaneCoreJobStore.MoveNode(moving.FolderPath, targetFolder);

        AssertTrue(OurPlaneCoreJobStore.IsSameOrDescendant(targetFolder, movedPath), "node should move into folder");
        AssertTakeoffChildOrder(targetFolder, "X,Y,A", "moved node appended into target folder");
    });
}

static void PageTreeOrderMovesSheetBeforeFolder()
{
    WithTempJob("page_order_sheet_before_folder", job =>
    {
        string parent = OurPlaneCoreJobStore.CreateFolder(job.PagesRoot, "Parent");
        string folderA = OurPlaneCoreJobStore.CreateFolder(parent, "Folder A");
        PageInfo sheetB = CreatePageItem(job, parent, "Sheet B");
        OurPlaneCoreJobStore.CreateFolder(parent, "Folder C");

        AssertPageChildOrder(parent, "Folder A,Sheet B,Folder C", "initial page/folder order");
        AssertTrue(OurPlaneCoreJobStore.MoveSiblingsToPosition([sheetB.FolderPath], folderA, after: false), "sheet before folder should apply");
        AssertPageChildOrder(parent, "Sheet B,Folder A,Folder C", "sheet moved before folder");
    });
}

static void PageTreeOrderMovesFolderBeforeFolder()
{
    WithTempJob("page_order_folder_before_folder", job =>
    {
        string parent = OurPlaneCoreJobStore.CreateFolder(job.PagesRoot, "Parent");
        string folderA = OurPlaneCoreJobStore.CreateFolder(parent, "Folder A");
        string folderB = OurPlaneCoreJobStore.CreateFolder(parent, "Folder B");
        CreatePageItem(job, parent, "Sheet C");

        AssertPageChildOrder(parent, "Folder A,Folder B,Sheet C", "initial folder/folder order");
        AssertTrue(OurPlaneCoreJobStore.MoveSiblingsToPosition([folderB], folderA, after: false), "folder before folder should apply");
        AssertPageChildOrder(parent, "Folder B,Folder A,Sheet C", "folder moved before folder");
    });
}

static void PageTreeOrderMovesNestedFolderOutBelowParent()
{
    WithTempJob("page_order_nested_folder_out", job =>
    {
        string parent = OurPlaneCoreJobStore.CreateFolder(job.PagesRoot, "Parent");
        string child = OurPlaneCoreJobStore.CreateFolder(parent, "Child");
        OurPlaneCoreJobStore.CreateFolder(job.PagesRoot, "Other");

        string moved = OurPlaneCoreJobStore.MoveNode(child, job.PagesRoot);
        AssertTrue(OurPlaneCoreJobStore.MoveSiblingsToPosition([moved], parent, after: true), "nested folder out should apply");
        AssertPageChildOrder(job.PagesRoot, "Parent,Child,Other", "nested folder moved out below parent");
    });
}

static void PageRenameAllowsDuplicateDisplayNames()
{
    WithTempJob("page_duplicate_names", job =>
    {
        PageInfo first = CreatePageItem(job, job.PagesRoot, "S101");
        PageInfo second = CreatePageItem(job, job.PagesRoot, "S102");

        string renamed = OurPlaneCoreJobStore.RenamePageAllowDuplicateName(second.FolderPath, "S101");
        PageInfo? renamedPage = OurPlaneCoreJobStore.TryReadPage(renamed);

        AssertTrue(Directory.Exists(first.FolderPath), "first duplicate-name page should remain");
        AssertTrue(Directory.Exists(renamed), "renamed duplicate-name page should exist");
        AssertFalse(string.Equals(first.FolderPath, renamed, StringComparison.OrdinalIgnoreCase), "duplicate-name pages need unique folders");
        AssertEqual("S101", OurPlaneCoreJobStore.DisplayName(first.FolderPath), "first display name");
        AssertEqual("S101", OurPlaneCoreJobStore.DisplayName(renamed), "renamed display name");
        AssertEqual("S101", renamedPage?.Name ?? "", "renamed page info name");
    });
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

static void PdfScaleParserHandlesMixedFractionScale()
{
    bool parsed = PdfSheetMetadataService.TryParseScaleMetersPerPt("1 1/2\" = 1'0\"", out double mixedMetersPerPt);
    bool parsedLegacy = PdfSheetMetadataService.TryParseScaleMetersPerPt("1-1/2\" = 1'0\"", out double legacyMetersPerPt);

    AssertTrue(parsed, "space mixed fraction scale should parse");
    AssertTrue(parsedLegacy, "hyphen mixed fraction scale should still parse");
    AssertClose(legacyMetersPerPt, mixedMetersPerPt, "mixed fraction styles should match");
    AssertEqual("1 1/2\" = 1'0\"", PdfSheetMetadataService.FormatImperialScale(mixedMetersPerPt), "mixed fraction roundtrip label");
}

static void JoistRoundingAliasesNormalize()
{
    AssertEqual(JoistTakeoffCalculator.RoundingNearestFoot, JoistTakeoffCalculator.NormalizeLengthRounding("foot"), "foot alias");
    AssertEqual(JoistTakeoffCalculator.RoundingNearestEvenFoot, JoistTakeoffCalculator.NormalizeLengthRounding("even foot"), "even alias");
    AssertEqual(JoistTakeoffCalculator.RoundingNearestTwoFeet, JoistTakeoffCalculator.NormalizeLengthRounding("2 feet"), "two feet alias");
    AssertEqual("Round Up 2 Feet", JoistTakeoffCalculator.LengthRoundingTitle("nearesttwofeet"), "rounding title");
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

static void JoistLayoutSubtractsAreaCutHoles()
{
    var measurement = new Measurement
    {
        MType = "area",
        JoistEnabled = true,
        JoistDirectionLocked = true,
        JoistSpacingInches = 60,
        JoistDirectionDegrees = 0,
        JoistLengthRounding = JoistTakeoffCalculator.RoundingNone,
        ScaleMetersPerPt = 0.3048,
        Points =
        [
            new SKPoint(0, 0),
            new SKPoint(10, 0),
            new SKPoint(10, 10),
            new SKPoint(0, 10),
        ],
        Holes =
        [
            [
                new SKPoint(4, 2),
                new SKPoint(6, 2),
                new SKPoint(6, 8),
                new SKPoint(4, 8),
            ],
        ],
    };

    JoistLayoutResult layout = JoistTakeoffCalculator.Calculate(measurement, 0);

    AssertEqual("4", layout.Count.ToString(), "cut hole splits middle joist run into two pieces");
    AssertClose(28.0, layout.TotalRawLengthMeters / 0.3048, "joist length should subtract the 2 ft opening on the middle line");
    AssertClose(88.0, layout.AreaMetersSquared / (0.3048 * 0.3048), "joist area should subtract the hole");
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
    AssertClose(16.0, rounded.TotalLengthMeters / 0.3048, "order length rounds the flat 8 ft joist length");
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

static void JoistLengthLabelShowsOrderAndRawLengths()
{
    var measurement = new Measurement
    {
        MType = "area",
        JoistEnabled = true,
        JoistDirectionLocked = true,
        JoistSpacingInches = 12,
        JoistLengthRounding = JoistTakeoffCalculator.RoundingNearestEvenFoot,
        ScaleMetersPerPt = 0.3048,
        Points =
        [
            new SKPoint(0, 0),
            new SKPoint(4.93f, 0),
            new SKPoint(4.93f, 1),
            new SKPoint(0, 1),
        ],
    };

    string label = measurement.Label(0, UnitMode.Imperial);

    AssertTrue(label.Contains("2 pcs @ 6.00 FT order (raw 4.93)", StringComparison.Ordinal), "label shows raw and order length");
    AssertFalse(label.Contains("(2 / 6.00", StringComparison.Ordinal), "label avoids count slash fraction format");
}

static void JoistLengthLabelCanUseStandardFormat()
{
    var measurement = new Measurement
    {
        MType = "area",
        JoistEnabled = true,
        JoistDirectionLocked = true,
        JoistSpacingInches = 12,
        JoistLengthRounding = JoistTakeoffCalculator.RoundingNearestEvenFoot,
        JoistDetailedLabels = false,
        ScaleMetersPerPt = 0.3048,
        Points =
        [
            new SKPoint(0, 0),
            new SKPoint(4.93f, 0),
            new SKPoint(4.93f, 1),
            new SKPoint(0, 1),
        ],
    };

    string label = measurement.Label(0, UnitMode.Imperial);

    AssertTrue(label.Contains("(2 / 6.00)", StringComparison.Ordinal), "standard label keeps old count / length format");
    AssertFalse(label.Contains("raw 4.93", StringComparison.Ordinal), "standard label hides raw details");
}

static void JoistPitchLabelExplainsFlatSlopeAndOrderLengths()
{
    var measurement = new Measurement
    {
        MType = "area",
        JoistEnabled = true,
        JoistDirectionLocked = true,
        JoistSpacingInches = 12,
        JoistPitch = "1:3",
        JoistLengthRounding = JoistTakeoffCalculator.RoundingNearestFoot,
        ScaleMetersPerPt = 0.3048,
        Points =
        [
            new SKPoint(0, 0),
            new SKPoint(11.94f, 0),
            new SKPoint(11.94f, 1),
            new SKPoint(0, 1),
        ],
    };

    string label = measurement.Label(0, UnitMode.Imperial);

    AssertTrue(label.Contains("2 pcs @ 12.00 FT order (flat 11.94, slope 12.59)", StringComparison.Ordinal), "label explains flat, slope, and order length");
}

static void JoistExportUsesVisibleLabelLines()
{
    WithTempJob("Joist Export", job =>
    {
        TakeoffItem item = OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Roof Joists", "#FF0000", "area");
        item.IsJoistTakeoff = true;
        item.Measurements.Add(new Measurement
        {
            MType = "area",
            JoistEnabled = true,
            JoistDirectionLocked = true,
            JoistType = "2x10",
            JoistSpacingInches = 120,
            JoistPitch = "3:12",
            JoistLengthRounding = JoistTakeoffCalculator.RoundingNone,
            ScaleMetersPerPt = 0.3048,
            Points = SimpleJoistAreaPolygon().ToList(),
        });

        string visibleLabel = item.Measurements[0].Label(0, UnitMode.Imperial);
        IReadOnlyList<string> labelLines = PlanSwiftTakeoffExporter.JoistLabelLines(item, 0, UnitMode.Imperial);
        IReadOnlyList<PlanSwiftExportRow> rows = PlanSwiftTakeoffExporter.BuildRows(job, [item], [job.TakeoffsRoot], UnitMode.Imperial);

        AssertEqual(visibleLabel, string.Join("\n", labelLines), "export label helper matches canvas label");
        AssertEqual(labelLines[0], rows.First(row => row.Kind == PlanSwiftExportRowKind.Item).Value, "item row value is first label line");
        AssertTrue(rows.Any(row => row.Kind == PlanSwiftExportRowKind.Note && row.Name == "Pitch 3:12"), "pitch label line exported");
    });
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
        item.JoistDetailedLabels = false;
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
        AssertFalse(loaded.JoistDetailedLabels, "loaded item label format");
        AssertFalse(loaded.Measurements[0].JoistDetailedLabels, "loaded measurement label format");
    });
}

static void JoistPitchAppliesItemProperties()
{
    var item = new TakeoffItem
    {
        MeasurementType = "area",
        IsJoistTakeoff = true,
        JoistPitch = "4/12",
        JoistDetailedLabels = false,
    };
    item.Measurements.Add(new Measurement
    {
        MType = "area",
        Points = SimpleJoistAreaPolygon().ToList(),
    });

    OurPlaneCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);

    AssertEqual("4:12", item.Measurements[0].JoistPitch, "measurement pitch copied");
    AssertFalse(item.Measurements[0].JoistDetailedLabels, "measurement label format copied");
}

static void PageOverlayPersistsThroughSourceRewrites()
{
    WithTempJob("Page Overlay", job =>
    {
        PageInfo basePage = CreatePageItem(job, job.PagesRoot, "S101");
        PageInfo overlayPage = CreatePageItem(job, job.PagesRoot, "S102");

        OurPlaneCoreJobStore.SavePageOverlay(basePage.FolderPath, overlayPage.FolderPath, "#1E88E5", 0.42);
        OurPlaneCoreJobStore.SavePageOverlayTransform(basePage.FolderPath, 12.5, -7.25, 1.2);
        OurPlaneCoreJobStore.SavePageOverlayVisibility(basePage.FolderPath, false);
        OurPlaneCoreJobStore.SavePageHiddenTakeoffs(basePage.FolderPath, ["Walls"]);
        PageInfo loaded = OurPlaneCoreJobStore.TryReadPage(basePage.FolderPath)
            ?? throw new InvalidOperationException("base page missing");
        AssertEqual(overlayPage.FolderPath, loaded.OverlayPageFolder, "overlay page path");
        AssertEqual("#1E88E5", loaded.OverlayColor, "overlay color");
        AssertClose(0.42, loaded.OverlayOpacity, "overlay opacity");
        AssertClose(12.5, loaded.OverlayOffsetXPt, "overlay x offset");
        AssertClose(-7.25, loaded.OverlayOffsetYPt, "overlay y offset");
        AssertClose(1.2, loaded.OverlayScale, "overlay scale");
        AssertFalse(loaded.OverlayVisible, "overlay visibility");
        AssertEqual("Walls", string.Join(",", loaded.HiddenTakeoffs), "hidden takeoffs");

        OurPlaneCoreJobStore.SavePageScale(basePage.FolderPath, 0.3048);
        PageInfo afterScale = OurPlaneCoreJobStore.TryReadPage(basePage.FolderPath)
            ?? throw new InvalidOperationException("base page after scale missing");
        AssertEqual(overlayPage.FolderPath, afterScale.OverlayPageFolder, "overlay survives scale save");
        AssertClose(12.5, afterScale.OverlayOffsetXPt, "overlay x survives scale save");
        AssertClose(-7.25, afterScale.OverlayOffsetYPt, "overlay y survives scale save");
        AssertClose(1.2, afterScale.OverlayScale, "overlay scale survives scale save");
        AssertFalse(afterScale.OverlayVisible, "overlay visibility survives scale save");
        AssertEqual("Walls", string.Join(",", afterScale.HiddenTakeoffs), "hidden takeoffs survive scale save");

        OurPlaneCoreJobStore.ClearPageOverlay(basePage.FolderPath);
        PageInfo cleared = OurPlaneCoreJobStore.TryReadPage(basePage.FolderPath)
            ?? throw new InvalidOperationException("base page after clear missing");
        AssertEqual("", cleared.OverlayPageFolder, "overlay clears");
    });
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

static void ViewportRenderScaleChoosesNextQualityStep()
{
    float[] steps = [0.75f, 1.0f, 1.5f, 2.25f, 3.0f, 4.0f];

    AssertClose(0.75, ViewportRenderPolicy.SelectRenderScale(0.4f, steps), "low zoom clamps to first render step");
    AssertClose(1.5, ViewportRenderPolicy.SelectRenderScale(1.2f, steps), "zoom chooses next higher render step");
    AssertClose(1.5, ViewportRenderPolicy.SelectRenderScale(8.0f, steps), "high zoom stays on responsive render cap");
}

static void ViewportBackgroundDefaultsToOpaqueWhite()
{
    AssertEqual("#FFFFFF", ViewportBackgroundPolicy.NormalizeColor(null), "null falls back to white");
    AssertEqual("#FFFFFF", ViewportBackgroundPolicy.NormalizeColor("   "), "blank falls back to white");
    AssertEqual("#FFFFFF", ViewportBackgroundPolicy.NormalizeColor("not-a-color"), "invalid falls back to white");
}

static void ViewportBackgroundStripsTransparency()
{
    AssertEqual("#ABCDEF", ViewportBackgroundPolicy.NormalizeColor("#00ABCDEF"), "transparent alpha is removed");
    AssertEqual("#ABCDEF", ViewportBackgroundPolicy.NormalizeColor(" #abcdef "), "valid color is canonicalized");
}

static void ViewportBackgroundTintsComfortColors()
{
    AssertFalse(ViewportBackgroundPolicy.ShouldTintRenderedPage("#FFFFFF"), "default white is not tinted");
    AssertTrue(ViewportBackgroundPolicy.ShouldTintRenderedPage("#FFF8E8"), "warm paper tints rendered page");
    AssertTrue(ViewportBackgroundPolicy.ShouldTintRenderedPage("#EFF7ED"), "soft green tints rendered page");
    AssertFalse(ViewportBackgroundPolicy.ShouldTintRenderedPage("#2B2B2B"), "dark edge does not tint rendered page");
}

static void ViewportHighZoomRespectsFastNavigationToggle()
{
    AssertFalse(
        ViewportRenderPolicy.ShouldUseFastNavigationFrame(
            simplifyNavigationRendering: false,
            isFastNavigating: true,
            zoom: ViewportRenderPolicy.HighZoomFastFrameThreshold,
            activePageMeasurementCount: 0,
            hasBlockingInteraction: false),
        "disabled fast navigation should keep full high-zoom frames");

    AssertTrue(
        ViewportRenderPolicy.ShouldUseFastNavigationFrame(
            simplifyNavigationRendering: true,
            isFastNavigating: true,
            zoom: ViewportRenderPolicy.HighZoomFastFrameThreshold,
            activePageMeasurementCount: 0,
            hasBlockingInteraction: false),
        "enabled fast navigation should use fast frames at high zoom");

    AssertFalse(
        ViewportRenderPolicy.ShouldUseFastNavigationFrame(
            simplifyNavigationRendering: true,
            isFastNavigating: true,
            zoom: ViewportRenderPolicy.HighZoomFastFrameThreshold - 0.1f,
            activePageMeasurementCount: 0,
            hasBlockingInteraction: false),
        "lower zoom should stay full frame when no fast-frame trigger is active");
}

static void ViewportFarZoomRespectsFastNavigationToggle()
{
    AssertFalse(
        ViewportRenderPolicy.ShouldUseFastNavigationFrame(
            simplifyNavigationRendering: false,
            isFastNavigating: true,
            zoom: ViewportRenderPolicy.FarZoomFastFrameThreshold,
            activePageMeasurementCount: 0,
            hasBlockingInteraction: false),
        "disabled fast navigation should keep full far-zoom frames");

    AssertTrue(
        ViewportRenderPolicy.ShouldUseFastNavigationFrame(
            simplifyNavigationRendering: true,
            isFastNavigating: true,
            zoom: ViewportRenderPolicy.FarZoomFastFrameThreshold,
            activePageMeasurementCount: 0,
            hasBlockingInteraction: false),
        "enabled fast navigation should use fast frames at far zoom");
}

static void ViewportDensePageRespectsFastNavigationToggle()
{
    AssertFalse(
        ViewportRenderPolicy.ShouldUseFastNavigationFrame(
            simplifyNavigationRendering: false,
            isFastNavigating: true,
            zoom: 1.0f,
            activePageMeasurementCount: ViewportRenderPolicy.DenseNavigationFastFrameThreshold,
            hasBlockingInteraction: false),
        "disabled fast navigation should keep full dense-page frames");

    AssertTrue(
        ViewportRenderPolicy.ShouldUseFastNavigationFrame(
            simplifyNavigationRendering: true,
            isFastNavigating: true,
            zoom: 1.0f,
            activePageMeasurementCount: ViewportRenderPolicy.DenseNavigationFastFrameThreshold,
            hasBlockingInteraction: false),
        "enabled fast navigation should use fast frames for dense pages");
}

static void ViewportEditingBlocksFastNavigationFrame()
{
    AssertFalse(
        ViewportRenderPolicy.ShouldUseFastNavigationFrame(
            simplifyNavigationRendering: true,
            isFastNavigating: true,
            zoom: 6.0f,
            activePageMeasurementCount: ViewportRenderPolicy.DenseNavigationFastFrameThreshold,
            hasBlockingInteraction: true),
        "active edit interaction should keep the full render frame");
}

static void ViewportMeasurementLabelsSurviveDistantZoom()
{
    AssertTrue(
        ViewportRenderPolicy.ShouldDrawMeasurementLabels(
            zoom: ViewportRenderPolicy.MeasurementLabelMinZoom - 0.01f,
            activePageMeasurementCount: 1,
            fastNavigationFrame: false),
        "distant zoom should keep measurement labels controlled by display toggles");

    AssertTrue(
        ViewportRenderPolicy.ShouldDrawMeasurementLabels(
            zoom: ViewportRenderPolicy.MeasurementLabelMinZoom,
            activePageMeasurementCount: 1,
            fastNavigationFrame: false),
        "near zoom should draw labels for small pages");

    AssertTrue(
        ViewportRenderPolicy.ShouldDrawMeasurementLabels(
            zoom: ViewportRenderPolicy.HighZoomFastFrameThreshold,
            activePageMeasurementCount: 1,
            fastNavigationFrame: true),
        "small-page labels should not blink off during fast navigation frames");

    AssertFalse(
        ViewportRenderPolicy.ShouldDrawMeasurementLabels(
            zoom: 2.0f,
            activePageMeasurementCount: ViewportRenderPolicy.DenseMeasurementLabelThreshold + 1,
            fastNavigationFrame: false),
        "very dense pages can still suppress expensive label drawing");
}

static void ViewportMeasurementLodLimitsDenseDetails()
{
    AssertFalse(
        ViewportRenderPolicy.ShouldDrawMeasurementDetails(
            zoom: 2.0f,
            activePageMeasurementCount: ViewportRenderPolicy.DenseMeasurementDetailThreshold + 1,
            fastNavigationFrame: false),
        "dense pages should skip expensive measurement detail layer");
}

static void ViewportLodHidesExpensiveLayersDuringFastFrames()
{
    AssertFalse(
        ViewportRenderPolicy.ShouldDrawSheetOverlay(
            fastNavigationFrame: true,
            isOverlayEditing: false),
        "fast navigation should skip sheet overlay until idle");

    AssertTrue(
        ViewportRenderPolicy.ShouldDrawSheetOverlay(
            fastNavigationFrame: true,
            isOverlayEditing: true),
        "overlay point editing should keep the overlay visible");

    AssertFalse(
        ViewportRenderPolicy.ShouldDrawMeasurementGeometry(
            activePageMeasurementCount: ViewportRenderPolicy.DenseMeasurementGeometryThreshold + 1,
            fastNavigationFrame: true),
        "very dense takeoff pages should skip non-selected measurement geometry during navigation");
}

static List<Measurement> SectionMeasurements(params string[] ids) =>
    ids.Select(id => new Measurement { Id = id }).ToList();

static void AssertSectionOrder(string expected, IReadOnlyList<Measurement> measurements, string message) =>
    AssertEqual(expected, string.Join(",", measurements.Select(measurement => measurement.Id)), message);

static TakeoffItem CreateRootTakeoffItem(OurPlaneCoreJob job, string name) =>
    OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, name, "#FF4444", "line");

static TakeoffItem CreateNestedTakeoffItem(OurPlaneCoreJob job, string parentFolder, string name) =>
    OurPlaneCoreJobStore.CreateTakeoffItem(job, parentFolder, name, "#FF4444", "line");

static string CreateTakeoffFolder(OurPlaneCoreJob job, string name) =>
    OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, name);

static List<TakeoffItem> CreateTakeoffItems(OurPlaneCoreJob job, params string[] names) =>
    names.Select(name => CreateRootTakeoffItem(job, name)).ToList();

static PageInfo CreatePageItem(OurPlaneCoreJob job, string parentFolder, string name)
{
    string sourcePdf = Path.Combine(job.RootPath, "source.pdf");
    if (!File.Exists(sourcePdf))
        File.WriteAllText(sourcePdf, "%PDF-1.4 test");

    return OurPlaneCoreJobStore.CreatePageFromPdf(job, sourcePdf, name, parentFolder);
}

static void AssertTakeoffChildOrder(string parentFolder, string expected, string message)
{
    string actual = string.Join(",",
        OurPlaneCoreJobStore.GetOrderedChildDirectories(parentFolder)
            .Select(OurPlaneCoreJobStore.DisplayName));
    AssertEqual(expected, actual, message);
}

static void AssertPageChildOrder(string parentFolder, string expected, string message)
{
    string actual = string.Join(",",
        OurPlaneCoreJobStore.GetOrderedChildDirectories(parentFolder)
            .Select(OurPlaneCoreJobStore.DisplayName));
    AssertEqual(expected, actual, message);
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
        TryDeleteDirectory(root);
    }
}
