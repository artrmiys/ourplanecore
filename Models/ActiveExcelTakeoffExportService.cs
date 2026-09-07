using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;

namespace OurPlanCore;

public sealed record ActiveExcelExportResult(
    bool Success,
    string Message,
    int RowCount = 0,
    int ColumnCount = 0,
    string StartAddress = "",
    string EndAddress = "");

public static class ActiveExcelTakeoffExportService
{
    private const int ColumnCount = 3;

    public static ActiveExcelExportResult ExportRows(IReadOnlyList<PlanSwiftExportRow> rows) =>
        ExportRowsCore(rows, valuesOnly: false);

    public static ActiveExcelExportResult ExportValues(IReadOnlyList<PlanSwiftExportRow> rows) =>
        ExportRowsCore(
            rows.Where(row => row.Kind == PlanSwiftExportRowKind.Item).ToList(),
            valuesOnly: true);

    private static ActiveExcelExportResult ExportRowsCore(
        IReadOnlyList<PlanSwiftExportRow> rows,
        bool valuesOnly)
    {
        if (rows.Count == 0)
            return Failure(valuesOnly ? "No measured values to export." : "No takeoff rows to export.");

        if (!TryGetRunningExcel(out object? excelObject, out string error))
            return Failure(error);

        try
        {
            dynamic excel = excelObject!;
            object? workbook = excel.ActiveWorkbook as object;
            if (workbook == null)
                return Failure("Excel is open, but no workbook is active.");

            object? activeCellObject = excel.ActiveCell as object;
            if (activeCellObject == null)
                return Failure("Excel is open, but no active cell was found.");

            dynamic activeCell = activeCellObject;
            dynamic sheet = activeCell.Worksheet;
            int startRow = Convert.ToInt32(activeCell.Row, CultureInfo.InvariantCulture);
            int startColumn = Convert.ToInt32(activeCell.Column, CultureInfo.InvariantCulture);
            int endRow = startRow + rows.Count - 1;
            int columnCount = valuesOnly ? 1 : ColumnCount;
            int endColumn = startColumn + columnCount - 1;

            object[,] values = valuesOnly ? BuildValuesMatrix(rows) : BuildValueMatrix(rows);
            dynamic firstCell = sheet.Cells[startRow, startColumn];
            dynamic lastCell = sheet.Cells[endRow, endColumn];
            dynamic targetRange = sheet.Range[firstCell, lastCell];
            targetRange.Value2 = values;
            if (!valuesOnly)
                ApplyHeaderFormatting(sheet, rows, startRow, startColumn, endColumn);

            string startAddress = CellAddress(startRow, startColumn);
            string endAddress = CellAddress(endRow, endColumn);
            return new ActiveExcelExportResult(
                true,
                valuesOnly
                    ? $"Exported {rows.Count} value(s) to active Excel range {startAddress}:{endAddress}."
                    : $"Exported {rows.Count} row(s) to active Excel range {startAddress}:{endAddress}.",
                rows.Count,
                columnCount,
                startAddress,
                endAddress);
        }
        catch (COMException ex)
        {
            return Failure($"Excel export failed: {ex.Message}");
        }
        catch (RuntimeBinderException ex)
        {
            return Failure($"Excel export failed: active Excel selection is not a normal worksheet cell. {ex.Message}");
        }
        catch (InvalidCastException ex)
        {
            return Failure($"Excel export failed: {ex.Message}");
        }
    }

    public static object[,] BuildValueMatrix(IReadOnlyList<PlanSwiftExportRow> rows)
    {
        object[,] values = new object[rows.Count, ColumnCount];
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            PlanSwiftExportRow row = rows[rowIndex];
            switch (row.Kind)
            {
                case PlanSwiftExportRowKind.Header:
                case PlanSwiftExportRowKind.Note:
                    values[rowIndex, 0] = CleanCell(row.Name);
                    values[rowIndex, 1] = "";
                    values[rowIndex, 2] = "";
                    break;
                case PlanSwiftExportRowKind.Item:
                    values[rowIndex, 0] = CleanCell(row.Name);
                    values[rowIndex, 1] = ExcelValue(row.Value);
                    values[rowIndex, 2] = CleanCell(row.Unit);
                    break;
                case PlanSwiftExportRowKind.Blank:
                    values[rowIndex, 0] = "";
                    values[rowIndex, 1] = "";
                    values[rowIndex, 2] = "";
                    break;
            }
        }

        return values;
    }

    public static object[,] BuildValuesMatrix(IReadOnlyList<PlanSwiftExportRow> rows)
    {
        IReadOnlyList<PlanSwiftExportRow> items = rows
            .Where(row => row.Kind == PlanSwiftExportRowKind.Item)
            .ToList();
        object[,] values = new object[items.Count, 1];
        for (int rowIndex = 0; rowIndex < items.Count; rowIndex++)
            values[rowIndex, 0] = ExcelValue(items[rowIndex].Value);

        return values;
    }

    private static void ApplyHeaderFormatting(dynamic sheet, IReadOnlyList<PlanSwiftExportRow> rows, int startRow, int startColumn, int endColumn)
    {
        for (int index = 0; index < rows.Count; index++)
        {
            if (rows[index].Kind != PlanSwiftExportRowKind.Header)
                continue;

            int excelRow = startRow + index;
            dynamic first = sheet.Cells[excelRow, startColumn];
            dynamic last = sheet.Cells[excelRow, endColumn];
            dynamic range = sheet.Range[first, last];
            range.Font.Bold = true;
        }
    }

    private static object ExcelValue(string value)
    {
        string clean = CleanCell(value);
        string numeric = clean.Replace(" ", "", StringComparison.Ordinal).Replace(',', '.');
        if (double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            return parsed;

        return clean;
    }

    private static string CleanCell(string value) =>
        (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();

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
            error = "Open Excel, open a workbook, click the destination cell, then run Export to Current Excel again.";
            return false;
        }
    }

    private static string CellAddress(int row, int column) =>
        $"{ColumnName(column)}{row}";

    private static string ColumnName(int column)
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

    private static ActiveExcelExportResult Failure(string message) =>
        new(false, message);

    [DllImport("ole32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void CLSIDFromProgID(string progId, out Guid clsid);

    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(
        ref Guid clsid,
        IntPtr reserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object excelObject);
}
