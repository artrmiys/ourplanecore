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
                    [new ExcelMacroPayloadRow("gable", 120d, "SF")],
                    config.Action(ExcelMacroExportActionIds.Gables),
                    excelObject),
                "J15:L15",
                "A2_SQFT_calc");

            AssertSuccess(
                ExcelMacroTakeoffExportService.ExportAndRunWithExcel(
                    [new ExcelMacroPayloadRow("truss heel 2", 40d, "FT")],
                    config.Action(ExcelMacroExportActionIds.TrussHeel),
                    excelObject),
                "J17:L17",
                "A2_SQFT_calc");

            AssertSuccess(
                ExcelMacroTakeoffExportService.ExportAndRunWithExcel(
                    [new ExcelMacroPayloadRow("parapet 4 3 0.66", 30d, "FT")],
                    config.Action(ExcelMacroExportActionIds.Parapet),
                    excelObject),
                "J19:L19",
                "A4_Parapet");

            AssertSuccess(
                ExcelMacroTakeoffExportService.ExportAndRunWithExcel(
                    [
                        new ExcelMacroPayloadRow("eve 12 main", 24d, "FT"),
                        new ExcelMacroPayloadRow("rake 12 main", 30d, "FT"),
                    ],
                    config.Action(ExcelMacroExportActionIds.EveRakes),
                    excelObject),
                "J21:L22",
                "A6_Eve_Rakes");

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
            True(RangeFormulaContains(sheet, "Y145:AF155", "K15"), "Gables macro linked the Gables source");
            True(RangeFormulaContains(sheet, "Y145:AF155", "K17"), "SQFT macro linked the Truss Heel source");
            Equal(4d, Convert.ToDouble(sheet.Range["O95"].Value2, CultureInfo.InvariantCulture), "Parapet outside height");
            True(CellFormulaContains(sheet.Range["P95"], "K19"), "Parapet length links its source");
            Equal(3d, Convert.ToDouble(sheet.Range["Q95"].Value2, CultureInfo.InvariantCulture), "Parapet inside height");
            Equal(0.66d, Convert.ToDouble(sheet.Range["S95"].Value2, CultureInfo.InvariantCulture), "Parapet top width");
            True(CellFormulaContains(sheet.Range["P135"], "K21"), "Eve length links its source");
            Equal(12d, Convert.ToDouble(sheet.Range["Q135"].Value2, CultureInfo.InvariantCulture), "Eve size");
            Equal("main", Convert.ToString(sheet.Range["T135"].Value2, CultureInfo.InvariantCulture) ?? "", "Eve name");
            True(CellFormulaContains(sheet.Range["V135"], "K22"), "Rake length links its source");
            Equal(12d, Convert.ToDouble(sheet.Range["W135"].Value2, CultureInfo.InvariantCulture), "Rake size");
            Equal("main", Convert.ToString(sheet.Range["Y135"].Value2, CultureInfo.InvariantCulture) ?? "", "Rake name");
            Equal("7.25x2.25", Convert.ToString(sheet.Range["Z159"].Value2, CultureInfo.InvariantCulture) ?? "", "Floor 1 grouped opening");
            Equal(3d, Convert.ToDouble(sheet.Range["AA159"].Value2, CultureInfo.InvariantCulture), "Floor 1 grouped quantity");
            Equal("4.25x3.25 d", Convert.ToString(sheet.Range["Z162"].Value2, CultureInfo.InvariantCulture) ?? "", "Floor 2 grouped opening");
            Equal(2d, Convert.ToDouble(sheet.Range["AA162"].Value2, CultureInfo.InvariantCulture), "Floor 2 grouped quantity");

            Console.WriteLine(
                "PASS Excel COM smoke: SQFT/Gables/Truss Heel A2, Walls A3, Parapet A4, Openings C+A5, Eve/Rakes A6.");
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

    private static bool RangeFormulaContains(
        dynamic sheet,
        string address,
        string sourceAddress)
    {
        object formulas = sheet.Range[address].Formula;
        if (formulas is not object[,] matrix)
            return Convert.ToString(formulas, CultureInfo.InvariantCulture)?
                .Contains(sourceAddress, StringComparison.OrdinalIgnoreCase) == true;

        foreach (object? value in matrix)
        {
            if (Convert.ToString(value, CultureInfo.InvariantCulture)?
                .Contains(sourceAddress, StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }
        }
        return false;
    }

    private static bool CellFormulaContains(dynamic cell, string sourceAddress) =>
        (Convert.ToString(cell.Formula, CultureInfo.InvariantCulture) ?? "")
        .Contains(sourceAddress, StringComparison.OrdinalIgnoreCase);

    private static void True(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
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
