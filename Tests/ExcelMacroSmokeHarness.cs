using OurPlanCore;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

internal static class ExcelMacroSmokeHarness
{
    public static int Run(string[] args)
    {
        if (args.Length < 2 || !File.Exists(args[1]))
        {
            Console.Error.WriteLine(
                "Usage: OurPlanCore.Tests excel-macro-smoke <TemplateCom.xlsm>");
            return 2;
        }
        if (Process.GetProcessesByName("EXCEL").Length > 0)
        {
            Console.Error.WriteLine(
                "REFUSED: Excel is already running. Close it or use a separate machine session.");
            return 3;
        }

        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "onc_excel_macro_smoke",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string tempWorkbook = Path.Combine(tempRoot, "TemplateCom.xlsm");
        File.Copy(args[1], tempWorkbook);

        object? excelObject = null;
        object? workbookObject = null;
        try
        {
            Type excelType = Type.GetTypeFromProgID("Excel.Application")
                ?? throw new InvalidOperationException("Microsoft Excel is not installed.");
            excelObject = Activator.CreateInstance(excelType)
                ?? throw new InvalidOperationException("Could not start Microsoft Excel.");
            dynamic excel = excelObject;
            excel.Visible = false;
            excel.DisplayAlerts = false;
            excel.AutomationSecurity = 1; // msoAutomationSecurityLow, disposable local copy only.
            workbookObject = excel.Workbooks.Open(tempWorkbook);
            dynamic workbook = workbookObject;

            ExcelMacroExportConfig config = ExcelMacroExportConfig.BuildDefault();
            AssertSuccess(
                ExcelMacroTakeoffExportService.ExportAndRun(
                    [new ExcelMacroPayloadRow("1st", 100d, "SF")],
                    config.Action(ExcelMacroExportActionIds.Sqft)),
                "J10:L10",
                "A2_SQFT_calc");

            AssertSuccess(
                ExcelMacroTakeoffExportService.ExportAndRun(
                    [
                        new ExcelMacroPayloadRow("1", null, "", IsFloorHeader: true),
                        new ExcelMacroPayloadRow("ext 2x6", 40d, "FT"),
                    ],
                    config.Action(ExcelMacroExportActionIds.Walls)),
                "J12:L13",
                "A3_Walls_Calc_AllGroup");

            AssertSuccess(
                ExcelMacroTakeoffExportService.ExportAndRun(
                    [
                        new ExcelMacroPayloadRow("1", null, "", IsFloorHeader: true),
                        new ExcelMacroPayloadRow("3x4 w", 2d, "EA"),
                    ],
                    config.Action(ExcelMacroExportActionIds.Openings)),
                "Z158:AB159",
                "A5_Openings");

            dynamic sheet = workbook.Worksheets["Detailed Frame List"];
            Equal(100d, Convert.ToDouble(sheet.Range["K10"].Value2, CultureInfo.InvariantCulture), "SQFT source");
            Equal(40d, Convert.ToDouble(sheet.Range["K13"].Value2, CultureInfo.InvariantCulture), "Walls source");
            Equal(2d, Convert.ToDouble(sheet.Range["AA159"].Value2, CultureInfo.InvariantCulture), "Openings source");

            Console.WriteLine(
                "PASS Excel COM smoke: SQFT J10, Walls appended J12, Openings Z158; A2/A3/A5 all ran.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL Excel COM smoke: {ex}");
            return 1;
        }
        finally
        {
            try
            {
                if (workbookObject != null)
                    ((dynamic)workbookObject).Close(false);
            }
            catch
            {
                // Continue closing only the Excel instance created by this harness.
            }
            try
            {
                if (excelObject != null)
                    ((dynamic)excelObject).Quit();
            }
            catch
            {
                // The disposable Excel instance may already be gone after a COM failure.
            }

            ReleaseCom(workbookObject);
            ReleaseCom(excelObject);
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // A delayed Excel file handle can disappear shortly after process exit.
            }
        }
    }

    private static void AssertSuccess(
        ExcelMacroTakeoffExportResult result,
        string expectedRange,
        string expectedMacro)
    {
        if (!result.Success)
            throw new InvalidOperationException(result.Message);
        if (!string.Equals(result.WrittenRange, expectedRange, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{expectedMacro}: expected {expectedRange}, got {result.WrittenRange}.");
        }
        if (!string.Equals(result.MacroName, expectedMacro, StringComparison.Ordinal))
            throw new InvalidOperationException($"{expectedMacro} did not report as executed.");
    }

    private static void Equal(double expected, double actual, string label)
    {
        if (Math.Abs(expected - actual) > 0.000001)
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
    }

    private static void ReleaseCom(object? value)
    {
        try
        {
            if (value != null && Marshal.IsComObject(value))
                Marshal.FinalReleaseComObject(value);
        }
        catch
        {
            // Best effort after closing our own disposable Excel instance.
        }
    }
}
