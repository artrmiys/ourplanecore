using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using OurPlanCore;

internal static class StructuralExcelMacroSmokeHarness
{
    public static int Run(string[] args)
    {
        if (args.Length < 2 || !File.Exists(args[1]))
        {
            Console.Error.WriteLine(
                "Usage: OurPlanCore.Tests structural-excel-macro-smoke <TemplateCom.xlsm>");
            return 2;
        }

        HashSet<int> existingExcelProcessIds = Process
            .GetProcessesByName("EXCEL")
            .Select(process => process.Id)
            .ToHashSet();
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "onc_structural_excel_macro_smoke",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string tempWorkbook = Path.Combine(tempRoot, "TemplateCom.xlsm");
        File.Copy(args[1], tempWorkbook);

        object? excelObject = null;
        object? workbookObject = null;
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
            workbookObject = excel.Workbooks.Open(tempWorkbook);
            dynamic workbook = workbookObject;

            TestSumTheSameValues(excel, workbook);
            TestBeamsWithoutLegend(excel, workbook);
            TestBeamsWithLegend(excel, workbook);
            TestPostsWithoutLegend(excel, workbook);
            TestPostsWithLegend(excel, workbook);
            TestHeadersWithoutLegend(excel, workbook);
            TestHeadersWithLegend(excel, workbook);
            TestJoistGroups(excel, workbook);
            TestFramingBlockExport(excel, workbook);

            Console.WriteLine(
                "PASS structural Excel COM smoke: Sum, Beams, Posts, Headers, and grouped Joists.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL structural Excel COM smoke: {ex}");
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
                // Continue closing only the disposable workbook and Excel instance.
            }
            try
            {
                if (ownsExcelInstance && excelObject != null)
                    ((dynamic)excelObject).Quit();
            }
            catch
            {
                // The owned Excel instance may already have stopped after a COM failure.
            }

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

    private static void TestSumTheSameValues(dynamic excel, dynamic workbook)
    {
        dynamic sheet = AddSheet(workbook, "sum");
        Set(sheet, "J5", "H5");
        Set(sheet, "K5", 2);
        Set(sheet, "L5", "EA");
        Set(sheet, "J6", "H5");
        Set(sheet, "K6", 3);
        Set(sheet, "L6", "EA");
        Set(sheet, "J7", "H5");
        Set(sheet, "K7", 4);
        Set(sheet, "L7", "FT");
        Set(sheet, "J8", "H6");
        Set(sheet, "K8", 1);
        Set(sheet, "L8", "EA");
        Set(sheet, "J9", "1/A501");
        Set(sheet, "K9", 1);
        Set(sheet, "L9", "EA");
        Set(sheet, "J10", "1/A501");
        Set(sheet, "K10", 2);
        Set(sheet, "L10", "EA");

        RunMacro(excel, workbook, sheet, "J5:L10", "C_SumTheSameValues");

        Equal("H5", Text(sheet, "J5"), "Sum row 1 name");
        Equal(5, Number(sheet, "K5"), "Sum row 1 quantity");
        Equal("EA", Text(sheet, "L5"), "Sum row 1 unit");
        Equal("H5", Text(sheet, "J6"), "Sum row 2 name");
        Equal(4, Number(sheet, "K6"), "Sum row 2 quantity");
        Equal("FT", Text(sheet, "L6"), "Sum row 2 unit");
        Equal("H6", Text(sheet, "J7"), "Sum row 3 name");
        Equal(1, Number(sheet, "K7"), "Sum row 3 quantity");
        Equal("EA", Text(sheet, "L7"), "Sum row 3 unit");
        Equal("1/A501", Text(sheet, "J8"), "Sum detail name");
        Equal(3, Number(sheet, "K8"), "Sum detail quantity");
        Equal("EA", Text(sheet, "L8"), "Sum detail unit");
        Blank(sheet, "J9", "Sum cleared tail");
        Console.WriteLine("PASS Sum input J:K:L: duplicate key is Name + Unit; numeric quantities are added.");
    }

    private static void TestBeamsWithoutLegend(dynamic excel, dynamic workbook)
    {
        dynamic sheet = AddSheet(workbook, "beam_plain");
        Set(sheet, "J5", "(2) 2x10 8");
        Set(sheet, "K5", 3);

        RunMacro(excel, workbook, sheet, "J5:K5", "C_BeamsSort");

        Equal("Beam (2)", Text(sheet, "A5"), "Beam plain label");
        Equal("2x10", Text(sheet, "B5"), "Beam plain material");
        Equal(6, Number(sheet, "C5"), "Beam plain multiplied quantity");
        Equal(8, Number(sheet, "D5"), "Beam plain length");
        Blank(sheet, "E5", "Beam plain mark");
        Console.WriteLine("PASS Beams no legend: '(2) 2x10 8' + qty 3 -> Beam (2), 2x10, qty 6, length 8.");
    }

    private static void TestBeamsWithLegend(dynamic excel, dynamic workbook)
    {
        dynamic sheet = AddSheet(workbook, "beam_legend");
        Set(sheet, "J5", "H5 8");
        Set(sheet, "K5", 3);
        Set(sheet, "J6", "H5 - (2) 1 3/4 x 11 7/8 LVL");

        RunMacro(excel, workbook, sheet, "J5:K6", "C_BeamsSort");

        Equal("Beam (2)", Text(sheet, "A5"), "Beam legend label");
        Equal("1 3/4 x 11 7/8 LVL", Text(sheet, "B5"), "Beam legend material");
        Equal(6, Number(sheet, "C5"), "Beam legend multiplied quantity");
        Equal(8, Number(sheet, "D5"), "Beam legend length");
        Equal("H5", Text(sheet, "E5"), "Beam legend mark");
        Blank(sheet, "A6", "Beam legend row skipped");
        Console.WriteLine("PASS Beams legend: code row uses a legend block at the end of the J:K selection.");
    }

    private static void TestPostsWithoutLegend(dynamic excel, dynamic workbook)
    {
        dynamic sheet = AddSheet(workbook, "post_plain");
        Set(sheet, "J5", "(2) 4x6 12");
        Set(sheet, "K5", 3);

        RunMacro(excel, workbook, sheet, "J5:K5", "C_PostsSort");

        Equal("Posts (2)", Text(sheet, "A5"), "Post plain label");
        Equal("4x6", Text(sheet, "B5"), "Post plain material");
        Equal(6, Number(sheet, "C5"), "Post plain multiplied quantity");
        Equal(12, Number(sheet, "D5"), "Post plain length");
        Blank(sheet, "E5", "Post plain mark");
        Console.WriteLine("PASS Posts no legend: '(2) 4x6 12' + qty 3 -> Posts (2), 4x6, qty 6, length 12.");
    }

    private static void TestPostsWithLegend(dynamic excel, dynamic workbook)
    {
        dynamic sheet = AddSheet(workbook, "post_legend");
        Set(sheet, "J5", "P2 12");
        Set(sheet, "K5", 3);
        Set(sheet, "J6", "P2 - (3) 2x6");

        RunMacro(excel, workbook, sheet, "J5:K6", "C_PostsSort");

        Equal("Posts (3)", Text(sheet, "A5"), "Post legend label");
        Equal("2x6", Text(sheet, "B5"), "Post legend material");
        Equal(9, Number(sheet, "C5"), "Post legend multiplied quantity");
        Equal(12, Number(sheet, "D5"), "Post legend length");
        Equal("P2", Text(sheet, "E5"), "Post legend mark");
        Blank(sheet, "A6", "Post legend row skipped");
        Console.WriteLine("PASS Posts legend: code row uses a legend block at the end; explicit length follows the code.");
    }

    private static void TestHeadersWithoutLegend(dynamic excel, dynamic workbook)
    {
        dynamic sheet = AddSheet(workbook, "header_plain");
        Set(sheet, "J5", "(2) 2x10 4");
        Set(sheet, "K5", 3);

        RunMacro(excel, workbook, sheet, "J5:K5", "C_HeadersSort");

        Equal(
            "Note: The headers indicated on the plan",
            Text(sheet, "A5"),
            "Header plain note");
        Equal("Ext. Headers up to 48\" (2)", Text(sheet, "A6"), "Header plain label");
        Equal("2x10", Text(sheet, "B6"), "Header plain material");
        Equal(3, Number(sheet, "G6"), "Header plain static source quantity");
        True(!HasFormula(sheet, "G6"), "Header plain G must be a literal value.");
        True(HasFormula(sheet, "C6"), "Header plain C must remain a formula.");
        True(HasFormula(sheet, "H6"), "Header plain H must remain a formula.");
        Console.WriteLine("PASS Headers no legend: direct material + span builds Note and Ext bucket rows.");
    }

    private static void TestHeadersWithLegend(dynamic excel, dynamic workbook)
    {
        dynamic sheet = AddSheet(workbook, "header_legend");
        Set(sheet, "J5", "ext H5 7");
        Set(sheet, "K5", 3);
        Set(sheet, "J6", "int H6 8");
        Set(sheet, "K6", 2);
        Set(sheet, "J7", "H5 - (3) 1 3/4 x 11 7/8 LVL");
        Set(sheet, "J8", "H6 - (2) 2x10");

        RunMacro(excel, workbook, sheet, "J5:K8", "C_HeadersSort");

        Equal(
            "Note: The headers indicated on the plan",
            Text(sheet, "A5"),
            "Header legend note");
        Equal("Ext. Headers up to 72\" (3)", Text(sheet, "A6"), "Header legend ext label");
        Equal("1 3/4 x 11 7/8 LVL", Text(sheet, "B6"), "Header legend ext material");
        Equal("H5", Text(sheet, "E6"), "Header legend ext mark");
        Equal(3, Number(sheet, "G6"), "Header legend ext source quantity");
        Blank(sheet, "A7", "Header legend spacer");
        Equal("Int. Headers (2)", Text(sheet, "A8"), "Header legend int label");
        Equal("2x10", Text(sheet, "B8"), "Header legend int material");
        Equal("H6", Text(sheet, "E8"), "Header legend int mark");
        Equal(2, Number(sheet, "G8"), "Header legend int source quantity");
        True(!HasFormula(sheet, "G6") && !HasFormula(sheet, "G8"), "Header legend G values must be literal.");
        True(HasFormula(sheet, "C6") && HasFormula(sheet, "H6"), "Header legend ext C/H must be formulas.");
        True(HasFormula(sheet, "C8") && HasFormula(sheet, "H8"), "Header legend int C/H must be formulas.");
        Console.WriteLine("PASS Headers legend: ext/int markers are separate groups with a blank spacer and code marks.");
    }

    private static void TestJoistGroups(dynamic excel, dynamic workbook)
    {
        dynamic sheet = AddSheet(workbook, "joist_groups");
        Set(sheet, "J5", "(2 / 11.2)");
        Set(sheet, "J6", "(3 / 12)");
        Set(sheet, "J7", "(1 / 7.8)");
        Set(sheet, "J8", "11 7/8 TJI 110 24\"");
        Set(sheet, "J9", "(4 / 9.1)");
        Set(sheet, "J10", "2x10 16\"");

        RunMacro(excel, workbook, sheet, "J5:K10", "C_JoistsSort");

        Equal("Joist 24\" o.c.", Text(sheet, "A5"), "Joist group 1 label");
        Equal("11 7/8 TJI 110", Text(sheet, "B5"), "Joist group 1 mark");
        Equal(5, Number(sheet, "C5"), "Joist group 1 combined quantity");
        Equal(12, Number(sheet, "D5"), "Joist group 1 rounded size");
        Equal("Joist 24\" o.c.", Text(sheet, "A6"), "Joist group 1 second label");
        Equal(1, Number(sheet, "C6"), "Joist group 1 second quantity");
        Equal(7, Number(sheet, "D6"), "Joist group 1 second size");
        Blank(sheet, "A7", "Joist group spacer");
        Equal("Joist 16\" o.c.", Text(sheet, "A8"), "Joist group 2 label");
        Equal("2x10", Text(sheet, "B8"), "Joist group 2 mark");
        Equal(4, Number(sheet, "C8"), "Joist group 2 quantity");
        Equal(10, Number(sheet, "D8"), "Joist group 2 rounded size");
        for (int row = 5; row <= 10; row++)
        {
            Blank(sheet, $"J{row}", $"Joist source J{row} cleared");
            Blank(sheet, $"K{row}", $"Joist source K{row} cleared");
        }
        Console.WriteLine(
            "PASS Joists grouped input: pair rows come first, the following name/spacing row closes that group; no Sum macro.");
    }

    private static void TestFramingBlockExport(dynamic excel, dynamic workbook)
    {
        ExcelFramingExportConfig config =
            ExcelMacroExportConfig.BuildDefault().Framing;
        var plan = new ExcelFramingExportPlan(
            true,
            "smoke",
            [
                new ExcelFramingTargetPlan(
                    "2nd Floor Framing List",
                    2,
                    [
                        Category(
                            "posts",
                            "Posts",
                            ExcelFramingCategoryModes.Macro,
                            "C_PostsSort",
                            useSum: true,
                            10,
                            [new ExcelFramingInputRow("P2 12", 3, "EA")]),
                        Category(
                            "beams",
                            "Beams",
                            ExcelFramingCategoryModes.Macro,
                            "C_BeamsSort",
                            useSum: true,
                            20,
                            [new ExcelFramingInputRow("B1 8", 3, "EA")]),
                        Category(
                            "joists",
                            "Joists",
                            ExcelFramingCategoryModes.Joists,
                            "C_JoistsSort",
                            useSum: false,
                            40,
                            [
                                new ExcelFramingInputRow("(2 / 11.2)"),
                                new ExcelFramingInputRow("(3 / 12)"),
                                new ExcelFramingInputRow("2x10 16\""),
                            ]),
                        Category(
                            "details",
                            "Details",
                            ExcelFramingCategoryModes.Details,
                            "",
                            useSum: true,
                            50,
                            [
                                new ExcelFramingInputRow("1/A501", 1, "EA"),
                                new ExcelFramingInputRow("1/A501", 2, "EA"),
                            ]),
                    ]),
            ],
            [
                new ExcelFramingTargetPlan(
                    "1st Floor Walls",
                    2,
                    [
                        Category(
                            "headers",
                            "Headers",
                            ExcelFramingCategoryModes.Headers,
                            "C_HeadersSort",
                            useSum: true,
                            30,
                            [new ExcelFramingInputRow("ext H5 7", 3, "EA")]),
                    ]),
            ],
            []);
        string legend = string.Join(
            Environment.NewLine,
            "P2 - (3) 2x6",
            "B1 - (2) 2x10",
            "H5 - (3) 1 3/4 x 11 7/8 LVL");

        ExcelFramingExportResult result =
            ExcelFramingExportService.ExportWithExcel(
                plan,
                config,
                legend,
                excel);
        True(result.Success, result.Message);

        dynamic sheet = workbook.Worksheets[config.SheetName];
        int framingRow = FindRow(sheet, "2nd Floor Framing List");
        True(framingRow > 0, "Framing target heading remains present.");
        Equal("Posts (3)", Text(sheet, $"A{framingRow + 1}"), "framing post label");
        Equal(9, Number(sheet, $"C{framingRow + 1}"), "framing post quantity");
        int beamRow = FindRow(sheet, "Beam (2)", framingRow + 1, framingRow + 30);
        True(beamRow > 0, "Framing beam output inserted.");
        Equal("B1", Text(sheet, $"E{beamRow}"), "framing beam legend mark");
        int joistRow = FindRow(sheet, "Joist 16\" o.c.", framingRow + 1, framingRow + 40);
        True(joistRow > 0, "Framing joist output inserted.");
        Equal(5, Number(sheet, $"C{joistRow}"), "framing joist grouped quantity");
        int detailRow = FindRow(sheet, "1/A501", framingRow + 1, framingRow + 50);
        if (detailRow == 0)
        {
            for (int row = framingRow; row <= framingRow + 60; row++)
            {
                string label = Text(sheet, $"A{row}");
                if (label.Length > 0)
                {
                    Console.Error.WriteLine(
                        $"FRAMING_DEBUG {row}: {label} | {Text(sheet, $"B{row}")} | " +
                        $"{Text(sheet, $"C{row}")} | {Text(sheet, $"D{row}")}");
                }
            }
        }
        True(detailRow > 0, "Framing detail output inserted.");
        Equal(3, Number(sheet, $"C{detailRow}"), "framing detail Sum quantity");
        int nextFramingRow = FindRow(sheet, "3rd Floor Framing List", framingRow + 1);
        True(
            FindRow(
                sheet,
                "Steel Beam Web Fillers 6x",
                framingRow + 1,
                nextFramingRow - 1) == 0,
            "Old framing placeholder rows were replaced.");

        int wallRow = FindRow(sheet, "1st Floor Walls");
        int noteRow = FindRow(
            sheet,
            config.HeaderNoteText,
            wallRow + 1,
            wallRow + 300);
        True(noteRow > wallRow, "Header note inserted into the shifted wall block.");
        Equal(
            "Ext. Headers up to 72\" (3)",
            Text(sheet, $"A{noteRow + 1}"),
            "header bucket output");
        Equal("H5", Text(sheet, $"E{noteRow + 1}"), "header legend mark");
        True(
            FindRow(sheet, "Wall Sheathing", noteRow + 1, noteRow + 40) > 0,
            "Wall rows after the replaced header block were preserved.");
        Console.WriteLine(
            "PASS framing block export: replaces green framing rows, preserves following wall rows, runs Sum before legend, and writes macro output.");
    }

    private static ExcelFramingCategoryPlan Category(
        string id,
        string label,
        string mode,
        string macro,
        bool useSum,
        int order,
        IReadOnlyList<ExcelFramingInputRow> rows) =>
        new(id, label, mode, macro, useSum, order, rows);

    private static dynamic AddSheet(dynamic workbook, string name)
    {
        dynamic sheet = workbook.Worksheets.Add();
        sheet.Name = name;
        return sheet;
    }

    private static void RunMacro(
        dynamic excel,
        dynamic workbook,
        dynamic sheet,
        string selectionAddress,
        string macroName)
    {
        sheet.Activate();
        sheet.Range[selectionAddress].Select();
        string workbookName =
            Convert.ToString(workbook.Name, CultureInfo.InvariantCulture) ?? "";
        string qualified =
            $"'{workbookName.Replace("'", "''", StringComparison.Ordinal)}'!{macroName}";
        excel.Run(qualified);
    }

    private static void Set(dynamic sheet, string address, object value) =>
        sheet.Range[address].Value2 = value;

    private static string Text(dynamic sheet, string address) =>
        Convert.ToString(sheet.Range[address].Value2, CultureInfo.InvariantCulture) ?? "";

    private static double Number(dynamic sheet, string address) =>
        Convert.ToDouble(sheet.Range[address].Value2, CultureInfo.InvariantCulture);

    private static bool HasFormula(dynamic sheet, string address) =>
        Convert.ToBoolean(sheet.Range[address].HasFormula, CultureInfo.InvariantCulture);

    private static int FindRow(
        dynamic sheet,
        string text,
        int startRow = 1,
        int endRow = 2300)
    {
        for (int row = startRow; row <= endRow; row++)
        {
            if (string.Equals(
                    Text(sheet, $"A{row}"),
                    text,
                    StringComparison.OrdinalIgnoreCase))
            {
                return row;
            }
        }
        return 0;
    }

    private static void Blank(dynamic sheet, string address, string label) =>
        Equal("", Text(sheet, address), label);

    private static void Equal(string expected, string actual, string label)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"{label}: expected '{expected}', got '{actual}'.");
    }

    private static void Equal(double expected, double actual, string label)
    {
        if (Math.Abs(expected - actual) > 0.000001)
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
    }

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

    private static void StopOwnedExcelIfStillRunning(int processId, bool ownsExcelInstance)
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
