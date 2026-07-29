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
        HashSet<int> existingExcelProcessIds = Process
            .GetProcessesByName("EXCEL")
            .Select(process => process.Id)
            .ToHashSet();

        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "onc_excel_macro_smoke",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string tempWorkbook = Path.Combine(tempRoot, "TemplateCom.xlsm");
        File.Copy(args[1], tempWorkbook);

        object? excelObject = null;
        object? workbookObject = null;
        object? sheetObject = null;
        bool ownsExcelInstance = false;
        int excelProcessId = 0;
        try
        {
            Type excelType = Type.GetTypeFromProgID("Excel.Application")
                ?? throw new InvalidOperationException("Microsoft Excel is not installed.");
            excelObject = Activator.CreateInstance(excelType)
                ?? throw new InvalidOperationException("Could not start Microsoft Excel.");
            dynamic excel = excelObject;
            GetWindowThreadProcessId(
                new IntPtr(Convert.ToInt64(excel.Hwnd, CultureInfo.InvariantCulture)),
                out uint createdProcessId);
            excelProcessId = checked((int)createdProcessId);
            if (excelProcessId <= 0 || existingExcelProcessIds.Contains(excelProcessId))
            {
                throw new InvalidOperationException(
                    "Excel COM reused an existing user process; smoke test stopped before opening a workbook.");
            }
            ownsExcelInstance = true;
            excel.Visible = false;
            excel.DisplayAlerts = false;
            excel.AutomationSecurity = 1; // msoAutomationSecurityLow, disposable local copy only.
            workbookObject = excel.Workbooks.Open(tempWorkbook);
            dynamic workbook = workbookObject;

            ExcelMacroExportConfig config = ExcelMacroExportConfig.BuildDefault();
            AssertSuccess(
                ExcelMacroTakeoffExportService.ExportAndRunWithExcel(
                    [new ExcelMacroPayloadRow("1st", 100d, "SF")],
                    config.Action(ExcelMacroExportActionIds.Sqft),
                    excelObject),
                "J10:L10",
                "A2_SQFT_calc");

            AssertSuccess(
                ExcelMacroTakeoffExportService.ExportAndRunWithExcel(
                    [
                        new ExcelMacroPayloadRow("1", null, "", IsFloorHeader: true),
                        new ExcelMacroPayloadRow("ext 2x6", 40d, "FT"),
                    ],
                    config.Action(ExcelMacroExportActionIds.Walls),
                    excelObject),
                "J12:L13",
                "A3_Walls_Calc_AllGroup");

            AssertSuccess(
                ExcelMacroTakeoffExportService.ExportAndRunWithExcel(
                    [
                        new ExcelMacroPayloadRow("1", null, "", IsFloorHeader: true),
                        new ExcelMacroPayloadRow("7.1x2.2", 1d, "EA"),
                        new ExcelMacroPayloadRow("7.4x2.2", 2d, "EA"),
                        new ExcelMacroPayloadRow("2", null, "", IsFloorHeader: true),
                        new ExcelMacroPayloadRow("4.1x3.1 d", 1d, "EA"),
                        new ExcelMacroPayloadRow("4.4x3.2 d", 1d, "EA"),
                    ],
                    config.Action(ExcelMacroExportActionIds.Openings),
                    excelObject),
                "Z158:AB163",
                "A5_Openings",
                expectedPreprocessRuns: 2);

            sheetObject = workbook.Worksheets["Detailed Frame List"];
            dynamic sheet = sheetObject;
            Equal(100d, Convert.ToDouble(sheet.Range["K10"].Value2, CultureInfo.InvariantCulture), "SQFT source");
            Equal(40d, Convert.ToDouble(sheet.Range["K13"].Value2, CultureInfo.InvariantCulture), "Walls source");
            Equal("7.25x2.25", Convert.ToString(sheet.Range["Z159"].Value2, CultureInfo.InvariantCulture) ?? "", "Floor 1 grouped opening");
            Equal(3d, Convert.ToDouble(sheet.Range["AA159"].Value2, CultureInfo.InvariantCulture), "Floor 1 grouped quantity");
            Equal("4.25x3.25 d", Convert.ToString(sheet.Range["Z162"].Value2, CultureInfo.InvariantCulture) ?? "", "Floor 2 grouped opening");
            Equal(2d, Convert.ToDouble(sheet.Range["AA162"].Value2, CultureInfo.InvariantCulture), "Floor 2 grouped quantity");

            Console.WriteLine(
                "PASS Excel COM smoke: SQFT A2, Walls A3, Openings per-floor C_SumNearWindowValues then A5.");
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
                if (ownsExcelInstance && workbookObject != null)
                    ((dynamic)workbookObject).Close(false);
            }
            catch
            {
                // Continue closing only the Excel instance created by this harness.
            }
            try
            {
                if (ownsExcelInstance && excelObject != null)
                    ((dynamic)excelObject).Quit();
            }
            catch
            {
                // The disposable Excel instance may already be gone after a COM failure.
            }

            ReleaseCom(sheetObject);
            ReleaseCom(workbookObject);
            ReleaseCom(excelObject);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            StopOwnedExcelIfStillRunning(excelProcessId, ownsExcelInstance);
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
        string expectedMacro,
        int expectedPreprocessRuns = 0)
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
        if (result.PreprocessRunCount != expectedPreprocessRuns)
        {
            throw new InvalidOperationException(
                $"{expectedMacro}: expected {expectedPreprocessRuns} preprocess run(s), " +
                $"got {result.PreprocessRunCount}.");
        }
    }

    private static void Equal(double expected, double actual, string label)
    {
        if (Math.Abs(expected - actual) > 0.000001)
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
    }

    private static void Equal(string expected, string actual, string label)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"{label}: expected '{expected}', got '{actual}'.");
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

    private static void StopOwnedExcelIfStillRunning(
        int processId,
        bool ownsExcelInstance)
    {
        if (!ownsExcelInstance || processId <= 0)
            return;
        try
        {
            using Process process = Process.GetProcessById(processId);
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (ArgumentException)
        {
            // The owned hidden Excel process already exited normally.
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr hWnd,
        out uint processId);
}
