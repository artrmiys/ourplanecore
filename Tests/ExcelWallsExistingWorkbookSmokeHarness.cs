using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using OurPlanCore;

internal static class ExcelWallsExistingWorkbookSmokeHarness
{
    private sealed record ProtectedFormulaProbe(
        string Label,
        int Row,
        string Formula,
        string FormulaR1C1);

    public static int Run(string[] args)
    {
        if (args.Length < 2 || !File.Exists(args[1]))
        {
            Console.Error.WriteLine(
                "Usage: OurPlanCore.Tests excel-walls-existing-smoke <workbook.xlsm>");
            return 2;
        }

        HashSet<int> existingExcelProcessIds = Process
            .GetProcessesByName("EXCEL")
            .Select(process => process.Id)
            .ToHashSet();
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "onc_excel_walls_existing_smoke",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string configuredPath = Path.Combine(tempRoot, "TemplateCom.xlsm");
        string activePath = Path.Combine(tempRoot, "Agrace_WallsExistingSmoke.xlsm");
        File.Copy(args[1], configuredPath);
        File.Copy(args[1], activePath);

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
            excel.AutomationSecurity = 1;
            configuredWorkbookObject = excel.Workbooks.Open(configuredPath);
            workbookObject = excel.Workbooks.Open(activePath);
            dynamic workbook = workbookObject;
            sheetObject = workbook.Worksheets["Detailed Frame List"];
            dynamic sheet = sheetObject;

            ExcelMacroExportActionConfig walls =
                ExcelMacroExportConfig.BuildDefault().Action(
                    ExcelMacroExportActionIds.Walls);
            int protectedRowsBefore = CountProtectedRows(sheet, walls);
            Ensure(
                protectedRowsBefore > 0,
                "Existing workbook has no mandatory Walls rows to protect.");
            IReadOnlyList<ProtectedFormulaProbe> formulasBefore =
                CaptureProtectedFormulas(sheet, walls);
            Ensure(
                formulasBefore.Count > 0,
                "Existing workbook has no protected formulas to verify.");

            var timer = Stopwatch.StartNew();
            ExcelMacroTakeoffExportResult result =
                ExcelMacroTakeoffExportService.ExportAndRunWithExcel(
                    [
                        new ExcelMacroPayloadRow("1", null, "", IsFloorHeader: true),
                        new ExcelMacroPayloadRow("ext 2x6 9.75", 37.125d, "FT"),
                        new ExcelMacroPayloadRow("cor 2x4 9.25", 19.875d, "FT"),
                        new ExcelMacroPayloadRow("2", null, "", IsFloorHeader: true),
                        new ExcelMacroPayloadRow("2x6 8.75", 11.625d, "FT"),
                    ],
                    walls,
                    excelObject);
            timer.Stop();

            Ensure(result.Success, result.Message);
            Ensure(
                string.Equals(
                    result.MacroName,
                    "A3_Walls_Calc_AllGroup",
                    StringComparison.Ordinal),
                "Walls main macro was not reported as executed.");
            Ensure(
                string.Equals(
                    result.AfterMacroName,
                    ExcelRangeCleanupService.DeleteZeroRowsMacroName,
                    StringComparison.Ordinal),
                "Walls cleanup action was not reported.");
            Ensure(
                timer.Elapsed < TimeSpan.FromSeconds(45),
                $"Walls existing-workbook smoke exceeded 45 seconds: {timer.Elapsed}.");
            Ensure(
                RangeContainsNumber(sheet, result.WrittenRange, 37.125d),
                $"Walls source value was not written to {result.WrittenRange}.");
            Ensure(
                !RangeContainsText(sheet, "C25:C1367", "__OPC_KEEP_"),
                "A temporary mandatory-row marker remained after fast cleanup.");
            int protectedRowsAfter = CountProtectedRows(sheet, walls);
            Ensure(
                protectedRowsAfter >= protectedRowsBefore,
                $"Mandatory Walls rows were lost: before={protectedRowsBefore}, " +
                $"after={protectedRowsAfter}.");
            IReadOnlyList<ProtectedFormulaProbe> formulasAfter =
                CaptureProtectedFormulas(sheet, walls);
            Ensure(
                formulasAfter.All(probe =>
                    !probe.Formula.Contains("#REF!", StringComparison.OrdinalIgnoreCase)),
                "A protected Walls formula contains #REF! after cleanup.");
            bool relativeFormulaMoved = formulasBefore.Any(before =>
                formulasAfter.Any(after =>
                    string.Equals(after.Label, before.Label, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(after.FormulaR1C1, before.FormulaR1C1, StringComparison.Ordinal) &&
                    after.Row != before.Row &&
                    !string.Equals(after.Formula, before.Formula, StringComparison.Ordinal)));
            Ensure(
                relativeFormulaMoved,
                "No moved protected formula preserved its relative R1C1 contract.");

            Console.WriteLine(
                "PASS existing-workbook Walls Excel COM smoke: " +
                $"elapsed={timer.ElapsedMilliseconds}ms; " +
                $"written={result.WrittenRange}; " +
                $"protected={result.ProtectedRowCount}; " +
                $"relativeFormulas={formulasAfter.Count}; " +
                "active renamed workbook won while TemplateCom was also open.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL existing-workbook Walls Excel COM smoke: {ex}");
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
                // Continue closing only the disposable Excel instance.
            }
            try
            {
                if (ownsExcelInstance && excelObject != null)
                    ((dynamic)excelObject).Quit();
            }
            catch
            {
                // The owned Excel instance may already be gone after a COM failure.
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
                count++;
                break;
            }
        }
        return count;
    }

    private static IReadOnlyList<ProtectedFormulaProbe> CaptureProtectedFormulas(
        dynamic sheet,
        ExcelMacroExportActionConfig action)
    {
        dynamic range = sheet.Range[action.AfterMacroRange];
        object? values = range.Value2;
        int firstRow = Convert.ToInt32(range.Row, CultureInfo.InvariantCulture);
        int rowCount = Convert.ToInt32(range.Rows.Count, CultureInfo.InvariantCulture);
        int columnCount = Convert.ToInt32(
            range.Columns.Count,
            CultureInfo.InvariantCulture);
        var probes = new List<ProtectedFormulaProbe>();
        for (int rowOffset = 0; rowOffset < rowCount; rowOffset++)
        {
            string label = "";
            for (int columnOffset = 0; columnOffset < columnCount; columnOffset++)
            {
                string value = Convert.ToString(
                    MatrixValue(values, rowOffset, columnOffset, rowCount, columnCount),
                    CultureInfo.InvariantCulture) ?? "";
                if (!ExcelMacroTakeoffExportService.IsProtectedOutputLabel(
                        value,
                        action.AfterMacroProtectedLabels))
                {
                    continue;
                }
                label = value.Trim();
                break;
            }
            if (label.Length == 0)
                continue;

            dynamic cell = sheet.Cells[firstRow + rowOffset, 3];
            if (!Convert.ToBoolean(cell.HasFormula, CultureInfo.InvariantCulture))
                continue;
            probes.Add(new ProtectedFormulaProbe(
                label,
                firstRow + rowOffset,
                Convert.ToString(cell.Formula, CultureInfo.InvariantCulture) ?? "",
                Convert.ToString(cell.FormulaR1C1, CultureInfo.InvariantCulture) ?? ""));
        }
        return probes;
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

    private static bool RangeContainsNumber(
        dynamic sheet,
        string address,
        double expected)
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
                object? value = MatrixValue(
                    values,
                    rowOffset,
                    columnOffset,
                    rowCount,
                    columnCount);
                if (value != null &&
                    double.TryParse(
                        Convert.ToString(value, CultureInfo.InvariantCulture),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double actual) &&
                    Math.Abs(actual - expected) < 0.000001)
                {
                    return true;
                }
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

    private static void Ensure(bool condition, string message)
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
