using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CSharp.RuntimeBinder;
using System.Runtime.InteropServices;

namespace OurPlanCore;

public sealed record ExcelFramingExportResult(
    bool Success,
    string Message,
    int FramingBlockCount = 0,
    int HeaderBlockCount = 0,
    int CategoryCount = 0);

internal sealed record ExcelFramingBlockResult(
    bool Success,
    string Message,
    int CategoryCount);

public static class ExcelFramingExportService
{
    private const int OutputColumnCount = 8;
    private const int SourceColumnCount = 3;
    private const int XlShiftDown = -4121;
    private const int XlShiftUp = -4162;

    public static ExcelFramingExportResult Export(
        ExcelFramingExportPlan plan,
        ExcelFramingExportConfig config,
        string legendText)
    {
        if (!plan.Success)
            return Failure(plan.Message);
        if (!TryValidate(config, out string error))
            return Failure(error);
        if (!ExcelMacroTakeoffExportService.TryGetRunningExcel(
                out object? excelObject,
                out string excelError))
        {
            return Failure(excelError);
        }
        return ExportWithExcel(plan, config, legendText, excelObject!);
    }

    internal static ExcelFramingExportResult ExportWithExcel(
        ExcelFramingExportPlan plan,
        ExcelFramingExportConfig config,
        string legendText,
        object excelObject)
    {
        if (!plan.Success)
            return Failure(plan.Message);
        if (!TryValidate(config, out string validationError))
            return Failure(validationError);

        string stage = "opening the configured workbook";
        try
        {
            dynamic excel = excelObject;
            dynamic? workbook = ExcelMacroTakeoffExportService.FindWorkbook(
                excel,
                config.WorkbookName,
                out string openNames);
            if (workbook == null)
            {
                string suffix = openNames.Length == 0
                    ? ""
                    : $" Open workbooks: {openNames}.";
                return Failure(
                    $"Open '{config.WorkbookName}' in Excel before running framing export.{suffix}");
            }

            dynamic sheet;
            try
            {
                sheet = workbook.Worksheets[config.SheetName];
            }
            catch
            {
                return Failure(
                    $"Workbook '{config.WorkbookName}' does not contain sheet '{config.SheetName}'.");
            }

            workbook.Activate();
            sheet.Activate();
            IReadOnlyList<LegendRow> legend = ParseLegend(legendText);
            stage = "replacing framing floor blocks";
            ExcelFramingBlockResult framingBlocks = ReplaceFramingBlocks(
                excel,
                workbook,
                sheet,
                plan.FramingTargets,
                config,
                legend);
            if (!framingBlocks.Success)
                return Failure(framingBlocks.Message);

            stage = "replacing wall header blocks";
            ExcelFramingBlockResult headerBlocks = ReplaceHeaderBlocks(
                excel,
                workbook,
                sheet,
                plan.HeaderTargets,
                config,
                legend);
            if (!headerBlocks.Success)
                return Failure(headerBlocks.Message);

            int categoryCount = framingBlocks.CategoryCount + headerBlocks.CategoryCount;
            string message =
                $"Framing: replaced {plan.FramingTargets.Count} framing block(s) and " +
                $"{plan.HeaderTargets.Count} header block(s); ran {categoryCount} category export(s).";
            AppLog.Info(message);
            return new ExcelFramingExportResult(
                true,
                message,
                plan.FramingTargets.Count,
                plan.HeaderTargets.Count,
                categoryCount);
        }
        catch (COMException ex)
        {
            AppLog.Error(ex, $"Excel framing export failed while {stage}");
            return Failure(
                $"Excel stopped while {stage}: {ex.Message} " +
                "Changes already written were left in the open workbook.");
        }
        catch (RuntimeBinderException ex)
        {
            AppLog.Error(ex, $"Excel framing binding failed while {stage}");
            return Failure($"Excel automation failed while {stage}: {ex.Message}");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, $"Excel framing export failed while {stage}");
            return Failure($"Framing export failed while {stage}: {ex.Message}");
        }
    }

    private static ExcelFramingBlockResult ReplaceFramingBlocks(
        dynamic excel,
        dynamic workbook,
        dynamic sheet,
        IReadOnlyList<ExcelFramingTargetPlan> targets,
        ExcelFramingExportConfig config,
        IReadOnlyList<LegendRow> legend)
    {
        int categoryCount = 0;
        foreach (ExcelFramingTargetPlan target in OrderTargetsBottomUp(sheet, targets))
        {
            int headingRow = FindExactTextRow(sheet, target.Heading);
            if (headingRow <= 0)
                return BlockFailure($"Excel framing heading '{target.Heading}' was not found.");
            if (!HeadingColorMatches(sheet, headingRow, config.TargetHeaderColor))
            {
                return BlockFailure(
                    $"Excel heading '{target.Heading}' does not have configured color " +
                    $"{config.TargetHeaderColor}; its block was not changed.");
            }

            int nextHeaderRow = FindNextColoredHeadingRow(
                sheet,
                headingRow,
                config.TargetHeaderColor);
            string nextHeadingText = nextHeaderRow > headingRow
                ? CellText(sheet, nextHeaderRow, 1)
                : "";
            if (nextHeaderRow <= headingRow + 1 || nextHeadingText.Length == 0)
                return BlockFailure($"Could not find the green boundary after '{target.Heading}'.");

            DeleteOutputRange(sheet, headingRow + 1, nextHeaderRow - 1);
            int guardCount = GuardRowCount(target, legend.Count);
            if (guardCount > 0)
                InsertOutputRows(sheet, headingRow + 1, guardCount);
            foreach (ExcelFramingCategoryPlan category in target.Categories
                         .Where(category => !IsDirect(category))
                         .OrderByDescending(category => category.Order))
            {
                WriteCategory(
                    excel,
                    workbook,
                    sheet,
                    headingRow + 1,
                    category,
                    config,
                    legend);
                categoryCount++;
            }

            int boundaryRow = FindExactTextRow(sheet, nextHeadingText);
            if (boundaryRow <= headingRow)
                return BlockFailure($"Could not preserve the boundary after '{target.Heading}'.");
            TrimTrailingBlankOutputRows(sheet, headingRow + 1, boundaryRow);
            foreach (ExcelFramingCategoryPlan category in target.Categories
                         .Where(IsDirect)
                         .OrderBy(category => category.Order))
            {
                boundaryRow = FindExactTextRow(sheet, nextHeadingText);
                if (boundaryRow <= headingRow)
                    return BlockFailure($"Could not preserve {category.Label} target boundary.");
                WriteCategory(
                    excel,
                    workbook,
                    sheet,
                    boundaryRow,
                    category,
                    config,
                    legend);
                categoryCount++;
            }
        }
        return new ExcelFramingBlockResult(true, "", categoryCount);
    }

    private static ExcelFramingBlockResult ReplaceHeaderBlocks(
        dynamic excel,
        dynamic workbook,
        dynamic sheet,
        IReadOnlyList<ExcelFramingTargetPlan> targets,
        ExcelFramingExportConfig config,
        IReadOnlyList<LegendRow> legend)
    {
        int categoryCount = 0;
        foreach (ExcelFramingTargetPlan target in OrderTargetsBottomUp(sheet, targets))
        {
            int wallHeadingRow = FindExactTextRow(sheet, target.Heading);
            if (wallHeadingRow <= 0)
                return BlockFailure($"Excel wall heading '{target.Heading}' was not found.");
            int noteRow = FindNextExactTextRow(
                sheet,
                config.HeaderNoteText,
                wallHeadingRow + 1);
            if (noteRow <= 0)
            {
                return BlockFailure(
                    $"Header block '{config.HeaderNoteText}' after '{target.Heading}' was not found.");
            }
            int headerEndRow = FindHeaderBlockEnd(sheet, noteRow);
            string followingLabel = headerEndRow >= noteRow
                ? CellText(sheet, headerEndRow + 1, 1)
                : "";
            if (headerEndRow < noteRow || followingLabel.Length == 0)
                return BlockFailure($"Could not determine headers under '{target.Heading}'.");

            DeleteOutputRange(sheet, noteRow, headerEndRow);
            int guardCount = GuardRowCount(target, legend.Count);
            if (guardCount > 0)
                InsertOutputRows(sheet, noteRow, guardCount);
            foreach (ExcelFramingCategoryPlan category in target.Categories
                         .OrderByDescending(category => category.Order))
            {
                WriteCategory(
                    excel,
                    workbook,
                    sheet,
                    noteRow,
                    category,
                    config,
                    legend);
                categoryCount++;
            }

            int followingRow = FindNextExactTextRow(
                sheet,
                followingLabel,
                noteRow + 1);
            if (followingRow <= noteRow)
                return BlockFailure($"Could not preserve rows after '{target.Heading}' headers.");
            TrimTrailingBlankOutputRows(sheet, noteRow, followingRow);
        }
        return new ExcelFramingBlockResult(true, "", categoryCount);
    }

    private static ExcelFramingBlockResult BlockFailure(string message) =>
        new(false, message, 0);

    private static IReadOnlyList<ExcelFramingTargetPlan> OrderTargetsBottomUp(
        dynamic sheet,
        IReadOnlyList<ExcelFramingTargetPlan> targets) =>
        targets
            .Select(target => new
            {
                Target = target,
                Row = FindExactTextRow(sheet, target.Heading),
            })
            .OrderByDescending(entry => entry.Row)
            .Select(entry => entry.Target)
            .ToList();

    private static void WriteCategory(
        dynamic excel,
        dynamic workbook,
        dynamic sheet,
        int startRow,
        ExcelFramingCategoryPlan category,
        ExcelFramingExportConfig config,
        IReadOnlyList<LegendRow> legend)
    {
        int sourceColumn = ExcelMacroTakeoffExportService.ColumnNumber(
            config.SourceStartColumn);
        WriteSourceRows(sheet, startRow, sourceColumn, category.Rows);
        int compactCount = category.Rows.Count;
        if (category.UseSum)
        {
            SelectRange(
                sheet,
                startRow,
                sourceColumn,
                startRow + category.Rows.Count - 1,
                sourceColumn + SourceColumnCount - 1);
            ExcelMacroTakeoffExportService.RunWorkbookMacro(
                excel,
                workbook,
                config.SumMacroName);
            compactCount = CountSourceRows(
                sheet,
                startRow,
                sourceColumn,
                category.Rows.Count);
        }

        if (IsDirect(category))
        {
            IReadOnlyList<ExcelFramingInputRow> compactRows = ReadSourceRows(
                sheet,
                startRow,
                sourceColumn,
                compactCount);
            InsertDirectRows(sheet, startRow, compactRows);
            ClearSourceRows(
                sheet,
                startRow,
                sourceColumn,
                Math.Max(category.Rows.Count, compactCount));
            return;
        }

        int selectionCount = compactCount;
        bool supportsLegend = SupportsLegend(category);
        if (supportsLegend && legend.Count > 0)
        {
            WriteLegendRows(sheet, startRow + compactCount, sourceColumn, legend);
            selectionCount += legend.Count;
        }
        if (selectionCount <= 0)
            return;

        SelectRange(
            sheet,
            startRow,
            sourceColumn,
            startRow + selectionCount - 1,
            sourceColumn + 1);
        ExcelMacroTakeoffExportService.RunWorkbookMacro(
            excel,
            workbook,
            category.MacroName);
        ClearSourceRows(
            sheet,
            startRow,
            sourceColumn,
            Math.Max(category.Rows.Count, selectionCount));
    }

    private static bool IsDirect(ExcelFramingCategoryPlan category) =>
        string.Equals(
            category.Mode,
            ExcelFramingCategoryModes.Details,
            StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            category.Mode,
            ExcelFramingCategoryModes.Direct,
            StringComparison.OrdinalIgnoreCase);

    private static int GuardRowCount(
        ExcelFramingTargetPlan target,
        int legendRowCount)
    {
        int maximumSelection = target.Categories
            .Where(category => !IsDirect(category))
            .Select(category =>
                category.Rows.Count +
                (SupportsLegend(category) ? legendRowCount : 0))
            .DefaultIfEmpty(0)
            .Max();
        return maximumSelection > 0 ? maximumSelection + 8 : 0;
    }

    private static bool SupportsLegend(ExcelFramingCategoryPlan category) =>
        string.Equals(
            category.Mode,
            ExcelFramingCategoryModes.Macro,
            StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            category.Mode,
            ExcelFramingCategoryModes.Headers,
            StringComparison.OrdinalIgnoreCase);

    private static void WriteSourceRows(
        dynamic sheet,
        int startRow,
        int startColumn,
        IReadOnlyList<ExcelFramingInputRow> rows)
    {
        object[,] values = new object[rows.Count, SourceColumnCount];
        for (int index = 0; index < rows.Count; index++)
        {
            values[index, 0] = Clean(rows[index].Name);
            values[index, 1] = rows[index].Quantity.HasValue
                ? rows[index].Quantity!.Value
                : "";
            values[index, 2] = Clean(rows[index].Unit);
        }
        dynamic range = sheet.Range[
            sheet.Cells[startRow, startColumn],
            sheet.Cells[startRow + rows.Count - 1, startColumn + SourceColumnCount - 1]];
        range.Value2 = values;
    }

    private static void WriteLegendRows(
        dynamic sheet,
        int startRow,
        int startColumn,
        IReadOnlyList<LegendRow> rows)
    {
        for (int index = 0; index < rows.Count; index++)
        {
            sheet.Cells[startRow + index, startColumn].Value2 = rows[index].First;
            if (rows[index].Second.Length > 0)
                sheet.Cells[startRow + index, startColumn + 1].Value2 = rows[index].Second;
        }
    }

    private static IReadOnlyList<ExcelFramingInputRow> ReadSourceRows(
        dynamic sheet,
        int startRow,
        int startColumn,
        int count)
    {
        var rows = new List<ExcelFramingInputRow>();
        for (int offset = 0; offset < count; offset++)
        {
            string name = CellText(sheet, startRow + offset, startColumn);
            if (name.Length == 0)
                continue;
            object? value = sheet.Cells[startRow + offset, startColumn + 1].Value2;
            double? quantity = TryNumber(value, out double number) ? number : null;
            string unit = CellText(sheet, startRow + offset, startColumn + 2);
            rows.Add(new ExcelFramingInputRow(name, quantity, unit));
        }
        return rows;
    }

    private static void InsertDirectRows(
        dynamic sheet,
        int startRow,
        IReadOnlyList<ExcelFramingInputRow> rows)
    {
        if (rows.Count == 0)
            return;
        dynamic outputRange = sheet.Range[
            sheet.Cells[startRow, 1],
            sheet.Cells[startRow + rows.Count - 1, OutputColumnCount]];
        outputRange.Insert(XlShiftDown);
        for (int index = 0; index < rows.Count; index++)
        {
            ExcelFramingInputRow row = rows[index];
            sheet.Cells[startRow + index, 1].Value2 = row.Name;
            if (row.Quantity.HasValue)
                sheet.Cells[startRow + index, 3].Value2 = row.Quantity.Value;
            sheet.Cells[startRow + index, 4].Value2 = row.Unit;
        }
    }

    private static void InsertOutputRows(
        dynamic sheet,
        int startRow,
        int count)
    {
        if (count <= 0)
            return;
        dynamic range = sheet.Range[
            sheet.Cells[startRow, 1],
            sheet.Cells[startRow + count - 1, OutputColumnCount]];
        range.Insert(XlShiftDown);
    }

    private static void TrimTrailingBlankOutputRows(
        dynamic sheet,
        int contentStartRow,
        int boundaryRow)
    {
        int blankStart = boundaryRow;
        while (blankStart > contentStartRow &&
               IsOutputRowBlank(sheet, blankStart - 1))
        {
            blankStart--;
        }
        if (blankStart < boundaryRow)
            DeleteOutputRange(sheet, blankStart, boundaryRow - 1);
    }

    private static bool IsOutputRowBlank(dynamic sheet, int row)
    {
        for (int column = 1; column <= OutputColumnCount; column++)
        {
            object? value = sheet.Cells[row, column].Value2;
            if (!string.IsNullOrWhiteSpace(
                    Convert.ToString(value, CultureInfo.InvariantCulture)))
            {
                return false;
            }
        }
        return true;
    }

    private static void ClearSourceRows(
        dynamic sheet,
        int startRow,
        int startColumn,
        int count)
    {
        if (count <= 0)
            return;
        dynamic range = sheet.Range[
            sheet.Cells[startRow, startColumn],
            sheet.Cells[startRow + count - 1, startColumn + SourceColumnCount - 1]];
        range.ClearContents();
    }

    private static int CountSourceRows(
        dynamic sheet,
        int startRow,
        int startColumn,
        int maximum)
    {
        int count = 0;
        for (int offset = 0; offset < maximum; offset++)
        {
            if (CellText(sheet, startRow + offset, startColumn).Length == 0)
                break;
            count++;
        }
        return count;
    }

    private static void SelectRange(
        dynamic sheet,
        int startRow,
        int startColumn,
        int endRow,
        int endColumn)
    {
        dynamic range = sheet.Range[
            sheet.Cells[startRow, startColumn],
            sheet.Cells[endRow, endColumn]];
        range.Select();
    }

    private static int FindExactTextRow(dynamic sheet, string text) =>
        FindNextExactTextRow(sheet, text, 1);

    private static int FindNextExactTextRow(
        dynamic sheet,
        string text,
        int startRow)
    {
        int lastRow = LastUsedRow(sheet);
        for (int row = Math.Max(1, startRow); row <= lastRow; row++)
        {
            if (string.Equals(
                    CellText(sheet, row, 1),
                    text.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return row;
            }
        }
        return 0;
    }

    private static int FindNextColoredHeadingRow(
        dynamic sheet,
        int afterRow,
        string color)
    {
        int expected = OleColor(color);
        int lastRow = LastUsedRow(sheet);
        for (int row = afterRow + 1; row <= lastRow; row++)
        {
            int actual = Convert.ToInt32(
                sheet.Cells[row, 1].Interior.Color,
                CultureInfo.InvariantCulture);
            if (actual == expected)
                return row;
        }
        return 0;
    }

    private static int FindHeaderBlockEnd(dynamic sheet, int noteRow)
    {
        int lastRow = Math.Min(LastUsedRow(sheet), noteRow + 100);
        int endRow = noteRow;
        for (int row = noteRow + 1; row <= lastRow; row++)
        {
            string label = CellText(sheet, row, 1);
            if (label.Length == 0)
            {
                endRow = row;
                continue;
            }
            if (label.StartsWith("Ext. Headers", StringComparison.OrdinalIgnoreCase) ||
                label.StartsWith("Int. Headers", StringComparison.OrdinalIgnoreCase))
            {
                endRow = row;
                continue;
            }
            break;
        }
        return endRow;
    }

    private static void DeleteOutputRange(dynamic sheet, int startRow, int endRow)
    {
        if (endRow < startRow)
            return;
        dynamic range = sheet.Range[
            sheet.Cells[startRow, 1],
            sheet.Cells[endRow, OutputColumnCount]];
        range.Delete(XlShiftUp);
    }

    private static bool HeadingColorMatches(dynamic sheet, int row, string color)
    {
        int actual = Convert.ToInt32(
            sheet.Cells[row, 1].Interior.Color,
            CultureInfo.InvariantCulture);
        return actual == OleColor(color);
    }

    private static int LastUsedRow(dynamic sheet)
    {
        dynamic used = sheet.UsedRange;
        int first = Convert.ToInt32(used.Row, CultureInfo.InvariantCulture);
        int count = Convert.ToInt32(used.Rows.Count, CultureInfo.InvariantCulture);
        return first + count - 1;
    }

    private static IReadOnlyList<LegendRow> ParseLegend(string text)
    {
        var rows = new List<LegendRow>();
        foreach (string raw in (text ?? "")
                     .Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Replace('\r', '\n')
                     .Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0)
                continue;
            string[] cells = line.Split('\t', 2);
            rows.Add(new LegendRow(
                Clean(cells[0]),
                cells.Length > 1 ? Clean(cells[1]) : ""));
        }
        return rows;
    }

    private static bool TryValidate(
        ExcelFramingExportConfig config,
        out string error)
    {
        error = "";
        int sourceColumn = ExcelMacroTakeoffExportService.ColumnNumber(
            config.SourceStartColumn);
        if (string.IsNullOrWhiteSpace(config.WorkbookName) ||
            string.IsNullOrWhiteSpace(config.SheetName))
        {
            error = "Framing workbook and worksheet are required.";
        }
        else if (sourceColumn <= 0)
        {
            error = "Framing source column is invalid.";
        }
        else if (string.IsNullOrWhiteSpace(config.SumMacroName))
        {
            error = "Framing Sum VBA macro is required.";
        }
        else
        {
            try
            {
                _ = OleColor(config.TargetHeaderColor);
            }
            catch (FormatException ex)
            {
                error = ex.Message;
            }
        }
        return error.Length == 0;
    }

    private static int OleColor(string color)
    {
        string hex = (color ?? "").Trim().TrimStart('#');
        if (hex.Length != 6 ||
            !int.TryParse(
                hex,
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out int rgb))
        {
            throw new FormatException(
                $"Framing heading color '{color}' must use #RRGGBB.");
        }
        int red = (rgb >> 16) & 0xFF;
        int green = (rgb >> 8) & 0xFF;
        int blue = rgb & 0xFF;
        return red | (green << 8) | (blue << 16);
    }

    private static string CellText(dynamic sheet, int row, int column) =>
        Convert.ToString(
            sheet.Cells[row, column].Value2,
            CultureInfo.InvariantCulture)?.Trim() ?? "";

    private static bool TryNumber(object? value, out double number)
    {
        try
        {
            number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return double.IsFinite(number);
        }
        catch
        {
            number = 0;
            return false;
        }
    }

    private static string Clean(string value) =>
        (value ?? "")
        .Replace('\t', ' ')
        .Replace('\r', ' ')
        .Replace('\n', ' ')
        .Trim();

    private static ExcelFramingExportResult Failure(string message) =>
        new(false, message);

    private sealed record LegendRow(string First, string Second);
}
