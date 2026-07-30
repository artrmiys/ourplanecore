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
        string configuredWorkbook = Path.Combine(tempRoot, "TemplateCom.xlsm");
        string tempWorkbook = Path.Combine(tempRoot, "RenamedTemplateComSmoke.xlsm");
        File.Copy(args[1], configuredWorkbook);
        File.Copy(args[1], tempWorkbook);

        object? excelObject = null;
        object? configuredWorkbookObject = null;
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
            configuredWorkbookObject = excel.Workbooks.Open(configuredWorkbook);
            workbookObject = excel.Workbooks.Open(tempWorkbook);
            dynamic workbook = workbookObject;
            sheetObject = workbook.Worksheets["Detailed Frame List"];
            dynamic sheet = sheetObject;
            dynamic configuredSheet =
                ((dynamic)configuredWorkbookObject).Worksheets["Detailed Frame List"];
            string configuredK10Before =
                Convert.ToString(
                    configuredSheet.Range["K10"].Value2,
                    CultureInfo.InvariantCulture) ?? "";

            ExcelMacroExportConfig config = ExcelMacroExportConfig.BuildDefault();
            ExcelMacroExportActionConfig wallsAction =
                config.Action(ExcelMacroExportActionIds.Walls);
            int protectedRowsBefore = CountProtectedRows(sheet, wallsAction);
            True(
                protectedRowsBefore > 0,
                "Disposable workbook has no mandatory Walls output rows to protect.");
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
                    wallsAction,
                    excelObject),
                "J12:L13",
                "A3_Walls_Calc_AllGroup",
                expectedAfterMacro: "B_DeleteZeroRowsOnlyIn_AtoH",
                expectedAfterRange: "A25:H1367",
                expectedProtectedRows: protectedRowsBefore);
            int protectedRowsAfter = CountProtectedRows(sheet, wallsAction);
            True(
                protectedRowsAfter >= protectedRowsBefore,
                $"Mandatory Walls output rows were lost: before={protectedRowsBefore}, " +
                $"after={protectedRowsAfter}.");
            True(
                !RangeContainsText(sheet, "C25:C1367", "__OPC_KEEP_"),
                "A temporary mandatory-row marker remained after Walls cleanup.");

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
            Equal(
                configuredK10Before,
                Convert.ToString(
                    configuredSheet.Range["K10"].Value2,
                    CultureInfo.InvariantCulture) ?? "",
                "configured workbook remains unchanged while renamed workbook is active");

            Console.WriteLine(
                "PASS active renamed-workbook Excel COM smoke with TemplateCom also open: SQFT/Gables/Truss Heel A2, Walls A3+B with mandatory-row preservation, Parapet A4, Openings C+A5, Eve/Rakes A6.");
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
                if (ownsExcelInstance && configuredWorkbookObject != null)
                    ((dynamic)configuredWorkbookObject).Close(false);
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
            ReleaseCom(configuredWorkbookObject);
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
        int expectedPreprocessRuns = 0,
        string expectedAfterMacro = "",
        string expectedAfterRange = "",
        int expectedProtectedRows = 0)
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
        if (!string.Equals(
                result.AfterMacroName,
                expectedAfterMacro,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{expectedMacro}: expected after-macro '{expectedAfterMacro}', " +
                $"got '{result.AfterMacroName}'.");
        }
        if (!string.Equals(
                result.AfterMacroRange,
                expectedAfterRange,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{expectedMacro}: expected after-range '{expectedAfterRange}', " +
                $"got '{result.AfterMacroRange}'.");
        }
        if (result.ProtectedRowCount != expectedProtectedRows)
        {
            throw new InvalidOperationException(
                $"{expectedMacro}: expected {expectedProtectedRows} protected row(s), " +
                $"got {result.ProtectedRowCount}.");
        }
    }

    private static int CountProtectedRows(
        dynamic sheet,
        ExcelMacroExportActionConfig action)
    {
        dynamic range = sheet.Range[action.AfterMacroRange];
        object? values = range.Value2;
        int rowCount = Convert.ToInt32(range.Rows.Count, CultureInfo.InvariantCulture);
        int columnCount = Convert.ToInt32(
            range.Columns.Count,
            CultureInfo.InvariantCulture);
        int count = 0;
        for (int rowOffset = 0; rowOffset < rowCount; rowOffset++)
        {
            bool isProtected = false;
            for (int columnOffset = 0; columnOffset < columnCount; columnOffset++)
            {
                string? value = Convert.ToString(
                    MatrixValue(values, rowOffset, columnOffset, rowCount, columnCount),
                    CultureInfo.InvariantCulture);
                if (!ExcelMacroTakeoffExportService.IsProtectedOutputLabel(
                        value,
                        action.AfterMacroProtectedLabels))
                {
                    continue;
                }
                isProtected = true;
                break;
            }
            if (isProtected)
                count++;
        }
        return count;
    }

    private static bool RangeContainsText(
        dynamic sheet,
        string address,
        string expectedText)
    {
        dynamic range = sheet.Range[address];
        object? values = range.Value2;
        int rowCount = Convert.ToInt32(range.Rows.Count, CultureInfo.InvariantCulture);
        int columnCount = Convert.ToInt32(
            range.Columns.Count,
            CultureInfo.InvariantCulture);
        for (int rowOffset = 0; rowOffset < rowCount; rowOffset++)
        {
            for (int columnOffset = 0; columnOffset < columnCount; columnOffset++)
            {
                string value = Convert.ToString(
                    MatrixValue(values, rowOffset, columnOffset, rowCount, columnCount),
                    CultureInfo.InvariantCulture) ?? "";
                if (value.Contains(expectedText, StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }

    private static object? MatrixValue(
        object? values,
        int zeroBasedRow,
        int zeroBasedColumn,
        int rowCount,
        int columnCount)
    {
        if (values is not Array matrix)
            return rowCount == 1 && columnCount == 1 ? values : null;
        int row = matrix.GetLowerBound(0) + zeroBasedRow;
        int column = matrix.GetLowerBound(1) + zeroBasedColumn;
        return matrix.GetValue(row, column);
    }

    private static void Equal(double expected, double actual, string label)
    {
        if (Math.Abs(expected - actual) > 0.000001)
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
    }

    private static void Equal(int expected, int actual, string label)
    {
        if (expected != actual)
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
