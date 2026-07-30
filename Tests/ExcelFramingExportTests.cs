using OurPlanCore;
using SkiaSharp;

internal static class ExcelFramingExportTests
{
    public static void DefaultsMatchTemplateAndMacroContract()
    {
        ExcelFramingExportConfig config =
            ExcelMacroExportConfig.BuildDefault().Framing;

        Equal("TemplateCom.xlsm", config.WorkbookName, "workbook");
        Equal("Detailed Frame List", config.SheetName, "worksheet");
        Equal("J", config.SourceStartColumn, "source column");
        Equal("C_SumTheSameValues", config.SumMacroName, "sum macro");
        Equal("#99CC00", config.TargetHeaderColor, "green heading");
        Equal(
            "Roof Frame list",
            config.Floors.Single(rule => rule.IsRoof).FramingHeading,
            "roof framing target");

        ExcelFramingFloorRule second =
            config.Floors.Single(rule => rule.Order == 2);
        Equal("2nd Floor Framing List", second.FramingHeading, "second framing target");
        Equal("1st Floor Walls", second.HeaderWallHeading, "second shifted wall target");
        Equal("2nd Floor Walls", second.SameFloorWallHeading, "second roof wall target");

        ExcelFramingCategoryConfig joists =
            config.Categories.Single(category => category.Id == "joists");
        ExcelFramingCategoryConfig details =
            config.Categories.Single(category => category.Id == "details");
        True(!joists.UseSum, "joists must not run Sum");
        True(!details.UseSum, "details must remain unprocessed source rows");
        Equal("C_JoistsSort", joists.MacroName, "joist macro");
        True(
            config.Categories
                .Where(category => category.Id is not "joists" and not "details")
                .All(category => category.UseSum),
            "processed non-joist defaults run Sum");

        ExcelFramingExportConfig legacy = config.Clone();
        ExcelFramingCategoryConfig legacyDetails =
            legacy.Categories.Single(category => category.Id == "details");
        legacyDetails.UseSum = true;
        legacyDetails.MacroName = "LegacyDetailsMacro";
        ExcelFramingExportConfig upgraded = ExcelFramingExportConfig.Upgrade(
            legacy,
            ExcelFramingExportConfig.BuildDefault(),
            replaceWithDefaults: false);
        ExcelFramingCategoryConfig upgradedDetails =
            upgraded.Categories.Single(category => category.Id == "details");
        True(
            !upgradedDetails.UseSum && upgradedDetails.MacroName.Length == 0,
            "legacy Details settings migrate to source-only J:L behavior");
    }

    public static void PlannerMapsFloorsHeadersRoofAndDetails()
    {
        OurPlanCoreJob job = Job();
        string house = Path.Combine(job.TakeoffsRoot, "House 1");
        IReadOnlyList<TakeoffItem> items =
        [
            PointItem(
                Path.Combine(house, "framing", "2nd floor framing", "posts", "P1"),
                "P1",
                2),
            PointItem(
                Path.Combine(house, "framing", "2nd floor framing", "headers", "ext", "H2"),
                "H2 5",
                2),
            PointItem(
                Path.Combine(house, "framing", "3rd floor framing", "headers", "int", "H3"),
                "H3 6",
                1),
            PointItem(
                Path.Combine(house, "framing", "roof framing", "headers", "ext", "HR"),
                "HR 7",
                3),
            PointItem(
                Path.Combine(house, "framing", "roof framing", "beams", "B1"),
                "(2) 2x10 8",
                1),
            PointItem(
                Path.Combine(house, "framing", "2nd floor framing", "details", "1", "2_A501"),
                "2_A501",
                1),
            PointItem(
                Path.Combine(house, "framing", "2nd floor framing", "details", "2", "1_A401"),
                "1_A401",
                1),
        ];

        ExcelFramingExportPlan plan = ExcelFramingExportPlanner.Build(
            job,
            items,
            house,
            0,
            ExcelMacroExportConfig.BuildDefault().Framing);

        True(plan.Success, plan.Message);
        True(
            plan.FramingTargets.Any(target =>
                target.Heading == "2nd Floor Framing List"),
            "numeric framing target");
        True(
            plan.FramingTargets.Any(target =>
                target.Heading == "Roof Frame list"),
            "roof uses the real roof framing block");
        Equal(
            "1st Floor Walls,2nd Floor Walls,3rd Floor Walls",
            string.Join(
                ",",
                plan.HeaderTargets.Select(target => target.Heading)),
            "headers shift down and roof uses highest occupied same-floor wall");

        ExcelFramingCategoryPlan details = plan.FramingTargets
            .Single(target => target.Heading == "2nd Floor Framing List")
            .Categories.Single(category => category.Id == "details");
        Equal(
            "1_A401,2_A501",
            string.Join(",", details.Rows.Select(row => row.Name)),
            "details sort by sheet then detail");
        ExcelFramingExportPlan detailsOnly =
            ExcelFramingExportPlanner.ForCategory(
                plan,
                ExcelFramingCategoryIds.Details);
        True(detailsOnly.Success, detailsOnly.Message);
        True(
            detailsOnly.FramingTargets
                .SelectMany(target => target.Categories)
                .All(category => category.Id == ExcelFramingCategoryIds.Details),
            "details button plan contains only details");
        True(
            detailsOnly.HeaderTargets.Count == 0,
            "details button does not target wall header blocks");

        ExcelFramingCategoryPlan headers = plan.HeaderTargets
            .Single(target => target.Heading == "1st Floor Walls")
            .Categories.Single();
        Equal("ext H2 5", headers.Rows[0].Name, "ext marker");
    }

    public static void PlannerBuildsGroupedJoistMacroInputWithoutSum()
    {
        OurPlanCoreJob job = Job();
        string house = Path.Combine(job.TakeoffsRoot, "House 1");
        var joist = new TakeoffItem
        {
            FolderPath = Path.Combine(
                house,
                "framing",
                "2nd floor framing",
                "joists",
                "2x10"),
            Name = "2x10",
            MeasurementType = "area",
            IsJoistTakeoff = true,
            JoistSpacingInches = 16,
        };
        joist.Measurements.Add(new Measurement
        {
            MType = "area",
            JoistEnabled = true,
            JoistDirectionLocked = true,
            JoistSpacingInches = 16,
            JoistDirectionDegrees = 0,
            JoistLengthRounding = JoistTakeoffCalculator.RoundingNearestEvenFoot,
            ScaleMetersPerPt = 0.3048,
            Points =
            [
                new SKPoint(0, 0),
                new SKPoint(12, 0),
                new SKPoint(12, 8),
                new SKPoint(0, 8),
            ],
        });

        ExcelFramingExportPlan plan = ExcelFramingExportPlanner.Build(
            job,
            [joist],
            house,
            0,
            ExcelMacroExportConfig.BuildDefault().Framing);

        True(plan.Success, plan.Message);
        ExcelFramingCategoryPlan category =
            plan.FramingTargets.Single().Categories.Single();
        True(!category.UseSum, "joist plan skips Sum");
        True(category.Rows.Count >= 2, "joist plan has pair rows and a closing name");
        True(
            category.Rows.Take(category.Rows.Count - 1)
                .All(row => row.Name.StartsWith("(", StringComparison.Ordinal)),
            "all joist pair rows precede the name");
        Equal("2x10 16\"", category.Rows[^1].Name, "name and spacing close the group");
    }

    public static void AllScopeRecognizesFramingAsOneHouse()
    {
        OurPlanCoreJob job = Job();
        string house1 = Path.Combine(job.TakeoffsRoot, "House 1");
        string house2 = Path.Combine(job.TakeoffsRoot, "House 2");
        TakeoffItem post = PointItem(
            Path.Combine(house1, "framing", "2nd floor framing", "posts", "P1"),
            "P1",
            1);
        TakeoffItem beam = PointItem(
            Path.Combine(house1, "framing", "2nd floor framing", "beams", "B1"),
            "B1",
            1);
        TakeoffItem other = PointItem(
            Path.Combine(house2, "framing", "3rd floor framing", "beams", "B2"),
            "B2",
            1);
        ExcelMacroExportConfig config = ExcelMacroExportConfig.BuildDefault();

        ExcelMacroBatchScopeResult one = ExcelMacroBatchPlanner.ResolveScope(
            job,
            [post, beam, other],
            [post.FolderPath, beam.FolderPath],
            config);
        True(one.Success, one.Message);
        Equal(Path.GetFullPath(house1), Path.GetFullPath(one.RootPath), "one house root");

        ExcelMacroBatchScopeResult mixed = ExcelMacroBatchPlanner.ResolveScope(
            job,
            [post, other],
            [post.FolderPath, other.FolderPath],
            config);
        True(!mixed.Success, "two real houses remain rejected");
    }

    private static OurPlanCoreJob Job() =>
        new()
        {
            Name = "Framing Excel test",
            RootPath = Path.Combine(Path.GetTempPath(), "opc_framing_excel_test"),
        };

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

    private static void True(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Equal(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{message}: expected '{expected}', got '{actual}'");
        }
    }
}
