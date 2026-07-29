using OurPlanCore;
using SkiaSharp;

internal static class ExcelMacroExportTests
{
    public static void DefaultsMatchTemplateComContract()
    {
        ExcelMacroExportConfig config = ExcelMacroExportConfig.BuildDefault();
        ExcelMacroExportActionConfig sqft = config.Action(ExcelMacroExportActionIds.Sqft);
        ExcelMacroExportActionConfig walls = config.Action(ExcelMacroExportActionIds.Walls);
        ExcelMacroExportActionConfig openings = config.Action(ExcelMacroExportActionIds.Openings);

        Equal("I", sqft.ScanStartColumn, "SQFT scan start");
        Equal("N", sqft.ScanEndColumn, "SQFT scan end");
        Equal("J", sqft.WriteStartColumn, "SQFT write start");
        Equal(10, sqft.StartRow, "SQFT first row");
        Equal("A2_SQFT_calc", sqft.MacroName, "SQFT macro");

        Equal("J", walls.WriteStartColumn, "Walls write start");
        True(walls.UseFloorHeaders, "Walls must write floor headers");
        Equal("A3_Walls_Calc_AllGroup", walls.MacroName, "Walls macro");
        Equal(
            "B_DeleteZeroRowsOnlyIn_AtoH",
            walls.AfterMacroName,
            "Walls cleanup macro");
        Equal("A25:H1367", walls.AfterMacroRange, "Walls cleanup range");
        Equal(15, walls.AfterMacroProtectedLabels.Count, "Walls mandatory output labels");

        Equal("Z", openings.ScanStartColumn, "Openings scan start");
        Equal("AB", openings.ScanEndColumn, "Openings scan end");
        Equal("Z", openings.WriteStartColumn, "Openings write start");
        Equal(158, openings.StartRow, "Openings first row");
        True(openings.UseFloorHeaders, "Openings must write floor headers");
        Equal(
            "C_SumNearWindowValues",
            openings.PerFloorPreprocessMacroName,
            "Openings per-floor preprocess macro");
        Equal("A5_Openings", openings.MacroName, "Openings macro");

        Equal(
            "A2_SQFT_calc",
            config.Action(ExcelMacroExportActionIds.Gables).MacroName,
            "Gables macro");
        Equal(
            "A2_SQFT_calc",
            config.Action(ExcelMacroExportActionIds.TrussHeel).MacroName,
            "Truss Heel macro");
        Equal(
            "A4_Parapet",
            config.Action(ExcelMacroExportActionIds.Parapet).MacroName,
            "Parapet macro");
        Equal(
            "A6_Eve_Rakes",
            config.Action(ExcelMacroExportActionIds.EveRakes).MacroName,
            "Eve / Rakes macro");
        Equal(
            ExcelMacroRowOrderModes.WallsStrict,
            walls.RowOrderMode,
            "Walls row order");
        Equal(
            ExcelMacroRowOrderModes.EvesThenRakesByValue,
            config.Action(ExcelMacroExportActionIds.EveRakes).RowOrderMode,
            "Eve / Rakes row order");
        Equal(
            "sqft,walls,gables,truss_heel,parapet,eve_rakes,openings",
            string.Join(",", config.BatchActionOrder),
            "ALL sequence");

        ExcelMacroExportConfig legacy = config.Clone();
        legacy.SchemaVersion = 1;
        legacy.Action(ExcelMacroExportActionIds.Openings).PerFloorPreprocessMacroName = "";
        legacy.Action(ExcelMacroExportActionIds.Walls).RowOrderMode =
            ExcelMacroRowOrderModes.Source;
        legacy.Action(ExcelMacroExportActionIds.Walls).AfterMacroName = "";
        legacy.Action(ExcelMacroExportActionIds.Walls).AfterMacroRange = "";
        legacy.Action(ExcelMacroExportActionIds.Walls).AfterMacroProtectedLabels = [];
        legacy.BatchActionOrder = [];
        ExcelMacroExportConfig upgraded =
            ExcelMacroExportConfig.UpgradeForCurrentSchema(legacy);
        Equal(
            "C_SumNearWindowValues",
            upgraded.Action(ExcelMacroExportActionIds.Openings).PerFloorPreprocessMacroName,
            "schema 1 settings gain the Openings preprocess macro");
        Equal(
            ExcelMacroRowOrderModes.WallsStrict,
            upgraded.Action(ExcelMacroExportActionIds.Walls).RowOrderMode,
            "old settings gain strict wall order");
        Equal(7, upgraded.BatchActionOrder.Count, "old settings gain ALL sequence");
        Equal(
            "B_DeleteZeroRowsOnlyIn_AtoH",
            upgraded.Action(ExcelMacroExportActionIds.Walls).AfterMacroName,
            "old settings gain Walls cleanup macro");
    }

    public static void WallsBuildNumericFloorGroupsAndImperialValues()
    {
        OurPlanCoreJob job = Job();
        string house = Path.Combine(job.TakeoffsRoot, "House 1");
        TakeoffItem first = LineItem(
            Path.Combine(house, "walls", "1st floor walls", "ext 2x6"),
            "ext 2x6",
            lengthPoints: 10);
        TakeoffItem third = LineItem(
            Path.Combine(house, "walls", "3rd floor walls", "int 2x4"),
            "int 2x4",
            lengthPoints: 5);

        ExcelMacroPayloadResult result = ExcelMacroPayloadBuilder.Build(
            job,
            [first, third],
            [house],
            fallbackScaleMetersPerPt: 0.3048,
            ExcelMacroExportConfig.BuildDefault(),
            ExcelMacroExportActionIds.Walls);

        True(result.Success, result.Message);
        Equal("1", result.Rows[0].Name, "first floor marker");
        True(result.Rows[0].IsFloorHeader, "first row is numeric floor header");
        Close(10, result.Rows[1].Value ?? 0, "first wall feet");
        Equal("3", result.Rows[2].Name, "third floor marker");
        Close(5, result.Rows[3].Value ?? 0, "third wall feet");

        object[,] matrix = ExcelMacroTakeoffExportService.BuildValueMatrix(result.Rows);
        True(matrix[0, 0] is int floor && floor == 1, "floor marker must be numeric in Excel");
        Equal("FT", matrix[1, 2]?.ToString() ?? "", "wall audit unit");
    }

    public static void OpeningsUseConfiguredFloorsOneThroughFive()
    {
        OurPlanCoreJob job = Job();
        string house = Path.Combine(job.TakeoffsRoot, "House 2");
        TakeoffItem first = PointItem(
            Path.Combine(house, "openings", "1", "3x4 w"),
            "3x4 w",
            count: 2);
        TakeoffItem fifth = PointItem(
            Path.Combine(house, "openings", "5th floor", "3068 d"),
            "3068 d",
            count: 1);

        ExcelMacroPayloadResult result = ExcelMacroPayloadBuilder.Build(
            job,
            [first, fifth],
            [house],
            fallbackScaleMetersPerPt: 0,
            ExcelMacroExportConfig.BuildDefault(),
            ExcelMacroExportActionIds.Openings);

        True(result.Success, result.Message);
        Equal("1", result.Rows[0].Name, "opening floor 1");
        Equal("3x4 w", result.Rows[1].Name, "opening name");
        Close(2, result.Rows[1].Value ?? 0, "opening count");
        Equal("5", result.Rows[2].Name, "opening floor 5");
        Equal("EA", result.Rows[3].Unit, "opening unit");
    }

    public static void SeparateBuildingFoldersAreRejected()
    {
        OurPlanCoreJob job = Job();
        TakeoffItem house1 = LineItem(
            Path.Combine(job.TakeoffsRoot, "House 1", "walls", "1", "ext"),
            "ext",
            1);
        TakeoffItem house2 = LineItem(
            Path.Combine(job.TakeoffsRoot, "House 2", "walls", "1", "ext"),
            "ext",
            1);

        ExcelMacroPayloadResult result = ExcelMacroPayloadBuilder.Build(
            job,
            [house1, house2],
            [job.TakeoffsRoot],
            fallbackScaleMetersPerPt: 1,
            ExcelMacroExportConfig.BuildDefault(),
            ExcelMacroExportActionIds.Walls);

        True(!result.Success, "two building wall roots must not be mixed");
        True(
            result.Message.Contains("separate", StringComparison.OrdinalIgnoreCase),
            "failure should explain separate export folders");
    }

    public static void PerFloorPreprocessRangesExcludeFloorHeaders()
    {
        IReadOnlyList<ExcelMacroPayloadRow> rows =
        [
            new("1", null, "", IsFloorHeader: true),
            new("7.1x2.2", 1, "EA"),
            new("7.4x2.2", 2, "EA"),
            new("2", null, "", IsFloorHeader: true),
            new("4.1x3.1 d", 1, "EA"),
        ];

        IReadOnlyList<ExcelMacroFloorRange> ranges =
            ExcelMacroTakeoffExportService.BuildPerFloorRanges(rows, 158, 26);

        Equal(2, ranges.Count, "floor range count");
        Equal(1, ranges[0].Floor, "first range floor");
        Equal(159, ranges[0].StartRow, "first range starts after header");
        Equal(160, ranges[0].EndRow, "first range ends before next header");
        Equal(26, ranges[0].StartColumn, "first range starts at Z");
        Equal(28, ranges[0].EndColumn, "preprocess includes Z:AB");
        Equal(2, ranges[1].Floor, "second range floor");
        Equal(162, ranges[1].StartRow, "second range starts after header");
        Equal(162, ranges[1].EndRow, "second range includes its one item");
    }

    public static void AdditionalActionsRouteTheirOwnFolders()
    {
        OurPlanCoreJob job = Job();
        string house = Path.Combine(job.TakeoffsRoot, "House 3");
        TakeoffItem gable = AreaItem(
            Path.Combine(house, "gables", "gable"),
            "gable",
            sidePoints: 10);
        TakeoffItem heel = LineItem(
            Path.Combine(house, "trussheel", "Truss Heel 2"),
            "truss heel 2",
            lengthPoints: 10);
        TakeoffItem parapet = LineItem(
            Path.Combine(house, "parapets", "Parapet 4 3"),
            "parapet 4 3",
            lengthPoints: 20);
        TakeoffItem eve = LineItem(
            Path.Combine(house, "eves rakes", "eves", "Eve 12 main"),
            "eve 12 main",
            lengthPoints: 24);
        TakeoffItem rake = LineItem(
            Path.Combine(house, "eves rakes", "rakes", "Rake 12 main"),
            "rake 12 main",
            lengthPoints: 30);
        IReadOnlyList<TakeoffItem> items = [gable, heel, parapet, eve, rake];
        ExcelMacroExportConfig config = ExcelMacroExportConfig.BuildDefault();

        ExcelMacroPayloadResult gables = ExcelMacroPayloadBuilder.Build(
            job, items, [house], 0.3048, config, ExcelMacroExportActionIds.Gables);
        ExcelMacroPayloadResult heels = ExcelMacroPayloadBuilder.Build(
            job, items, [house], 0.3048, config, ExcelMacroExportActionIds.TrussHeel);
        ExcelMacroPayloadResult parapets = ExcelMacroPayloadBuilder.Build(
            job, items, [house], 0.3048, config, ExcelMacroExportActionIds.Parapet);
        ExcelMacroPayloadResult eveRakes = ExcelMacroPayloadBuilder.Build(
            job, items, [house], 0.3048, config, ExcelMacroExportActionIds.EveRakes);

        True(gables.Success, gables.Message);
        Equal(1, gables.Rows.Count, "Gables row count");
        Equal("gable", gables.Rows[0].Name, "Gables route");
        Equal("SF", gables.Rows[0].Unit, "Gables unit");
        Close(100, gables.Rows[0].Value ?? 0, "Gables square feet");

        True(heels.Success, heels.Message);
        Equal(1, heels.Rows.Count, "Truss Heel row count");
        Equal("truss heel 2", heels.Rows[0].Name, "Truss Heel route");

        True(parapets.Success, parapets.Message);
        Equal(1, parapets.Rows.Count, "Parapet row count");
        Equal("parapet 4 3", parapets.Rows[0].Name, "Parapet route");

        True(eveRakes.Success, eveRakes.Message);
        Equal(2, eveRakes.Rows.Count, "Eve / Rakes row count");
        Equal("eve 12 main", eveRakes.Rows[0].Name, "Eve route");
        Equal("rake 12 main", eveRakes.Rows[1].Name, "Rake route");
    }

    public static void WallsUseStrictPerFloorExportOrder()
    {
        OurPlanCoreJob job = Job();
        string floor = Path.Combine(
            job.TakeoffsRoot,
            "House 1",
            "walls",
            "1st floor walls");
        IReadOnlyList<TakeoffItem> items =
        [
            LineItem(Path.Combine(floor, "2x4 walls"), "2x4 walls", 20),
            LineItem(Path.Combine(floor, "dem 2x4"), "dem 2x4", 12),
            LineItem(Path.Combine(floor, "ext short"), "ext short", 15),
            PointItem(Path.Combine(floor, "corners"), "corners", 8),
            LineItem(Path.Combine(floor, "cor 2x6"), "cor 2x6", 9),
            LineItem(Path.Combine(floor, "ext maximum"), "ext maximum", 40),
            LineItem(Path.Combine(floor, "2x6 walls"), "2x6 walls", 18),
            LineItem(Path.Combine(floor, "misc"), "misc", 5),
        ];

        ExcelMacroPayloadResult result = ExcelMacroPayloadBuilder.Build(
            job,
            items,
            [Path.Combine(job.TakeoffsRoot, "House 1")],
            0.3048,
            ExcelMacroExportConfig.BuildDefault(),
            ExcelMacroExportActionIds.Walls);

        True(result.Success, result.Message);
        Equal(
            "1,corners,ext maximum,ext short,cor 2x6,dem 2x4,2x6 walls,2x4 walls,misc",
            string.Join(",", result.Rows.Select(row => row.Name)),
            "strict wall order");
    }

    public static void EvesAndRakesSortByLfDescending()
    {
        OurPlanCoreJob job = Job();
        string role = Path.Combine(job.TakeoffsRoot, "House 1", "eves rakes");
        IReadOnlyList<TakeoffItem> items =
        [
            LineItem(Path.Combine(role, "rakes", "Rake small"), "rake small", 8),
            LineItem(Path.Combine(role, "eves", "Eve small"), "eve small", 10),
            LineItem(Path.Combine(role, "Returns"), "returns", 3),
            LineItem(Path.Combine(role, "rakes", "Rake maximum"), "rake maximum", 24),
            LineItem(Path.Combine(role, "eves", "Eve maximum"), "eve maximum", 30),
        ];

        ExcelMacroPayloadResult result = ExcelMacroPayloadBuilder.Build(
            job,
            items,
            [Path.Combine(job.TakeoffsRoot, "House 1")],
            0.3048,
            ExcelMacroExportConfig.BuildDefault(),
            ExcelMacroExportActionIds.EveRakes);

        True(result.Success, result.Message);
        Equal(
            "eve maximum,eve small,rake maximum,rake small,returns",
            string.Join(",", result.Rows.Select(row => row.Name)),
            "Eve/Rakes LF order");
    }

    public static void AllScopeUsesOneBuildingAndRejectsMixedRoots()
    {
        OurPlanCoreJob job = Job();
        string house1 = Path.Combine(job.TakeoffsRoot, "House 1");
        string house2 = Path.Combine(job.TakeoffsRoot, "House 2");
        TakeoffItem first = LineItem(
            Path.Combine(house1, "walls", "1", "ext"),
            "ext",
            10);
        TakeoffItem second = LineItem(
            Path.Combine(house2, "walls", "1", "ext"),
            "ext",
            20);
        ExcelMacroExportConfig config = ExcelMacroExportConfig.BuildDefault();

        ExcelMacroBatchScopeResult one = ExcelMacroBatchPlanner.ResolveScope(
            job,
            [first, second],
            [first.FolderPath],
            config);
        True(one.Success, one.Message);
        Equal(
            Path.GetFullPath(house1),
            Path.GetFullPath(one.RootPath),
            "ALL ascends from an item to its building");

        ExcelMacroBatchScopeResult mixed = ExcelMacroBatchPlanner.ResolveScope(
            job,
            [first, second],
            [job.TakeoffsRoot],
            config);
        True(!mixed.Success, "ALL must reject a job-root selection with two buildings");
    }

    public static void CleanupWhitelistUsesExactNormalizedLabels()
    {
        ExcelMacroExportActionConfig walls =
            ExcelMacroExportConfig.BuildDefault().Action(ExcelMacroExportActionIds.Walls);

        True(
            ExcelMacroTakeoffExportService.IsProtectedOutputLabel(
                "Wall\u00A0Sheathing",
                walls.AfterMacroProtectedLabels),
            "NBSP-normalized mandatory label should match");
        True(
            ExcelMacroTakeoffExportService.IsProtectedOutputLabel(
                "  note: the headers indicated on the plan  ",
                walls.AfterMacroProtectedLabels),
            "mandatory label matching should ignore case and outside spaces");
        True(
            !ExcelMacroTakeoffExportService.IsProtectedOutputLabel(
                "Wall Sheathing Extra",
                walls.AfterMacroProtectedLabels),
            "cleanup whitelist should not hide unrelated output rows");
        True(
            ExcelMacroTakeoffExportService.TryValidateRangeAddress(
                "A25:H1367",
                out _),
            "configured cleanup range should be valid");
        True(
            !ExcelMacroTakeoffExportService.TryValidateRangeAddress(
                "H1367:A25",
                out _),
            "reversed cleanup range should be rejected");
    }

    private static OurPlanCoreJob Job() =>
        new()
        {
            Name = "Excel test",
            RootPath = Path.Combine(Path.GetTempPath(), "opc_excel_test"),
        };

    private static TakeoffItem LineItem(
        string path,
        string name,
        float lengthPoints)
    {
        var item = new TakeoffItem
        {
            FolderPath = path,
            Name = name,
            MeasurementType = "line",
        };
        item.Measurements.Add(new Measurement
        {
            MType = "line",
            Points = [new SKPoint(0, 0), new SKPoint(lengthPoints, 0)],
        });
        return item;
    }

    private static TakeoffItem PointItem(
        string path,
        string name,
        int count)
    {
        var item = new TakeoffItem
        {
            FolderPath = path,
            Name = name,
            MeasurementType = "point",
        };
        item.Measurements.Add(new Measurement
        {
            MType = "point",
            Points = Enumerable.Range(0, count)
                .Select(index => new SKPoint(index, index))
                .ToList(),
        });
        return item;
    }

    private static TakeoffItem AreaItem(
        string path,
        string name,
        float sidePoints)
    {
        var item = new TakeoffItem
        {
            FolderPath = path,
            Name = name,
            MeasurementType = "area",
        };
        item.Measurements.Add(new Measurement
        {
            MType = "area",
            Points =
            [
                new SKPoint(0, 0),
                new SKPoint(sidePoints, 0),
                new SKPoint(sidePoints, sidePoints),
                new SKPoint(0, sidePoints),
            ],
        });
        return item;
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Equal(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"{message}: expected '{expected}', got '{actual}'");
    }

    private static void Equal(int expected, int actual, string message)
    {
        if (expected != actual)
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }

    private static void Close(double expected, double actual, string message)
    {
        if (Math.Abs(expected - actual) > 0.000001)
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }
}
