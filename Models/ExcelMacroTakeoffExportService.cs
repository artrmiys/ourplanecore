using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;

namespace OurPlanCore;

public sealed record ExcelMacroTakeoffExportResult(
    bool Success,
    string Message,
    int RowCount = 0,
    string WrittenRange = "",
    string SelectedRange = "",
    string MacroName = "");

public static class ExcelMacroTakeoffExportService
{
    private const int ExportColumnCount = 3;
    private const int MacroSelectionColumnCount = 2;
    private const int XlFormulas = -4123;
    private const int XlPart = 2;
    private const int XlByRows = 1;
    private const int XlPrevious = 2;

    public static ExcelMacroTakeoffExportResult ExportAndRun(
        IReadOnlyList<ExcelMacroPayloadRow> rows,
        ExcelMacroExportActionConfig action)
    {
        if (rows.Count == 0)
            return Failure("No rows were prepared for Excel.");

        if (!TryValidate(action, out string validationError))
            return Failure(validationError);

        if (!TryGetRunningExcel(out object? excelObject, out string excelError))
            return Failure(excelError);

        try
        {
            dynamic excel = excelObject!;
            dynamic? workbook = FindWorkbook(excel, action.WorkbookName, out string openNames);
            if (workbook == null)
            {
                string suffix = string.IsNullOrWhiteSpace(openNames)
                    ? ""
                    : $" Open workbooks: {openNames}.";
                return Failure(
                    $"Open '{action.WorkbookName}' in Excel before running {action.Label}.{suffix}");
            }

            dynamic sheet;
            try
            {
                sheet = workbook.Worksheets[action.SheetName];
            }
            catch
            {
                return Failure(
                    $"Workbook '{action.WorkbookName}' does not contain sheet '{action.SheetName}'.");
            }

            workbook.Activate();
            sheet.Activate();

            int startRow = FindNextStartRow(sheet, action);
            int writeColumn = ColumnNumber(action.WriteStartColumn);
            int endRow = startRow + rows.Count - 1;
            int endWriteColumn = writeColumn + ExportColumnCount - 1;
            int maxRows = Convert.ToInt32(sheet.Rows.Count, CultureInfo.InvariantCulture);
            if (endRow > maxRows)
                return Failure($"Excel sheet has no room for {rows.Count} row(s) from row {startRow}.");

            dynamic firstCell = sheet.Cells[startRow, writeColumn];
            dynamic lastCell = sheet.Cells[endRow, endWriteColumn];
            dynamic writeRange = sheet.Range[firstCell, lastCell];
            writeRange.Value2 = BuildValueMatrix(rows);

            ApplyFloorHeaderFormatting(sheet, rows, startRow, writeColumn, endWriteColumn);

            dynamic selectionFirst = sheet.Cells[startRow, writeColumn];
            dynamic selectionLast = sheet.Cells[
                endRow,
                writeColumn + MacroSelectionColumnCount - 1];
            dynamic selection = sheet.Range[selectionFirst, selectionLast];
            selection.Select();

            string workbookMacroName =
                $"'{Convert.ToString(workbook.Name, CultureInfo.InvariantCulture)?.Replace("'", "''", StringComparison.Ordinal)}'!{action.MacroName}";
            excel.Run(workbookMacroName);

            string writtenRange =
                $"{CellAddress(startRow, writeColumn)}:{CellAddress(endRow, endWriteColumn)}";
            string selectedRange =
                $"{CellAddress(startRow, writeColumn)}:" +
                $"{CellAddress(endRow, writeColumn + MacroSelectionColumnCount - 1)}";
            string message =
                $"{action.Label}: wrote {rows.Count} row(s) to {writtenRange}, " +
                $"selected {selectedRange}, ran {action.MacroName}.";
            AppLog.Info($"Excel macro export completed. Workbook={action.WorkbookName}; Sheet={action.SheetName}; {message}");
            return new ExcelMacroTakeoffExportResult(
                true,
                message,
                rows.Count,
                writtenRange,
                selectedRange,
                action.MacroName);
        }
        catch (COMException ex)
        {
            AppLog.Error(ex, $"Excel macro export failed for {action.Id}");
            return Failure(
                $"Excel stopped the {action.Label} export or macro: {ex.Message} " +
                "Any source rows already written were left in place.");
        }
        catch (RuntimeBinderException ex)
        {
            AppLog.Error(ex, $"Excel macro binding failed for {action.Id}");
            return Failure($"Excel automation failed for {action.Label}: {ex.Message}");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, $"Excel macro export failed for {action.Id}");
            return Failure($"Excel export failed for {action.Label}: {ex.Message}");
        }
    }

    internal static object[,] BuildValueMatrix(IReadOnlyList<ExcelMacroPayloadRow> rows)
    {
        object[,] values = new object[rows.Count, ExportColumnCount];
        for (int index = 0; index < rows.Count; index++)
        {
            ExcelMacroPayloadRow row = rows[index];
            values[index, 0] = row.IsFloorHeader &&
                               int.TryParse(
                                   row.Name,
                                   NumberStyles.Integer,
                                   CultureInfo.InvariantCulture,
                                   out int floor)
                ? floor
                : CleanCell(row.Name);
            values[index, 1] = row.Value.HasValue ? row.Value.Value : "";
            values[index, 2] = CleanCell(row.Unit);
        }
        return values;
    }

    internal static int ColumnNumber(string column)
    {
        int value = 0;
        foreach (char c in (column ?? "").Trim().ToUpperInvariant())
        {
            if (c is < 'A' or > 'Z')
                return 0;
            value = checked(value * 26 + (c - 'A' + 1));
        }
        return value;
    }

    internal static string ColumnName(int column)
    {
        var chars = new Stack<char>();
        int value = Math.Max(1, column);
        while (value > 0)
        {
            value--;
            chars.Push((char)('A' + value % 26));
            value /= 26;
        }
        return new string(chars.ToArray());
    }

    private static int FindNextStartRow(dynamic sheet, ExcelMacroExportActionConfig action)
    {
        int startColumn = ColumnNumber(action.ScanStartColumn);
        int endColumn = ColumnNumber(action.ScanEndColumn);
        int maxRows = Convert.ToInt32(sheet.Rows.Count, CultureInfo.InvariantCulture);
        dynamic first = sheet.Cells[action.StartRow, startColumn];
        dynamic last = sheet.Cells[maxRows, endColumn];
        dynamic scanRange = sheet.Range[first, last];
        object? found = scanRange.Find(
            "*",
            Missing.Value,
            XlFormulas,
            XlPart,
            XlByRows,
            XlPrevious,
            false,
            Missing.Value,
            Missing.Value);
        if (found == null)
            return action.StartRow;

        dynamic lastUsed = found;
        int lastUsedRow = Convert.ToInt32(lastUsed.Row, CultureInfo.InvariantCulture);
        return Math.Max(
            action.StartRow,
            lastUsedRow + Math.Max(0, action.BlankRowsBetween) + 1);
    }

    private static dynamic? FindWorkbook(dynamic excel, string workbookName, out string openNames)
    {
        var names = new List<string>();
        int count = Convert.ToInt32(excel.Workbooks.Count, CultureInfo.InvariantCulture);
        for (int index = 1; index <= count; index++)
        {
            dynamic workbook = excel.Workbooks[index];
            string name = Convert.ToString(workbook.Name, CultureInfo.InvariantCulture) ?? "";
            if (name.Length > 0)
                names.Add(name);
            if (string.Equals(name, workbookName, StringComparison.OrdinalIgnoreCase))
            {
                openNames = string.Join(", ", names);
                return workbook;
            }
        }

        openNames = string.Join(", ", names);
        return null;
    }

    private static void ApplyFloorHeaderFormatting(
        dynamic sheet,
        IReadOnlyList<ExcelMacroPayloadRow> rows,
        int startRow,
        int startColumn,
        int endColumn)
    {
        for (int index = 0; index < rows.Count; index++)
        {
            if (!rows[index].IsFloorHeader)
                continue;
            dynamic first = sheet.Cells[startRow + index, startColumn];
            dynamic last = sheet.Cells[startRow + index, endColumn];
            dynamic range = sheet.Range[first, last];
            range.Font.Bold = true;
        }
    }

    private static bool TryValidate(
        ExcelMacroExportActionConfig action,
        out string error)
    {
        error = "";
        int scanStart = ColumnNumber(action.ScanStartColumn);
        int scanEnd = ColumnNumber(action.ScanEndColumn);
        int writeStart = ColumnNumber(action.WriteStartColumn);
        if (scanStart <= 0 || scanEnd < scanStart)
            error = $"{action.Label}: scan columns are invalid.";
        else if (writeStart <= 0)
            error = $"{action.Label}: write column is invalid.";
        else if (action.StartRow < 1)
            error = $"{action.Label}: start row must be 1 or greater.";
        else if (string.IsNullOrWhiteSpace(action.WorkbookName) ||
                 string.IsNullOrWhiteSpace(action.SheetName) ||
                 string.IsNullOrWhiteSpace(action.MacroName))
            error = $"{action.Label}: workbook, sheet, and macro names are required.";
        return error.Length == 0;
    }

    private static bool TryGetRunningExcel(out object? excelObject, out string error)
    {
        excelObject = null;
        error = "";
        try
        {
            CLSIDFromProgID("Excel.Application", out Guid clsid);
            GetActiveObject(ref clsid, IntPtr.Zero, out excelObject);
            return excelObject != null;
        }
        catch (COMException)
        {
            error =
                "Open Excel and TemplateCom.xlsm, then run the Excel action again. " +
                "OurPlanCore will use the already-open workbook and will not close it.";
            return false;
        }
    }

    private static string CellAddress(int row, int column) =>
        $"{ColumnName(column)}{row}";

    private static string CleanCell(string value) =>
        (value ?? "")
        .Replace('\t', ' ')
        .Replace('\r', ' ')
        .Replace('\n', ' ')
        .Trim();

    private static ExcelMacroTakeoffExportResult Failure(string message) =>
        new(false, message);

    [DllImport("ole32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void CLSIDFromProgID(string progId, out Guid clsid);

    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(
        ref Guid clsid,
        IntPtr reserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object excelObject);
}
