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

        Equal("Z", openings.ScanStartColumn, "Openings scan start");
        Equal("AB", openings.ScanEndColumn, "Openings scan end");
        Equal("Z", openings.WriteStartColumn, "Openings write start");
        Equal(158, openings.StartRow, "Openings first row");
        True(openings.UseFloorHeaders, "Openings must write floor headers");
        Equal("A5_Openings", openings.MacroName, "Openings macro");
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
