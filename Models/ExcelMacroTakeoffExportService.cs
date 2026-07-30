using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.CSharp.RuntimeBinder;

namespace OurPlanCore;

public sealed record ExcelMacroTakeoffExportResult(
    bool Success,
    string Message,
    int RowCount = 0,
    string WrittenRange = "",
    string SelectedRange = "",
    string MacroName = "",
    string PreprocessMacroName = "",
    int PreprocessRunCount = 0,
    string AfterMacroName = "",
    string AfterMacroRange = "",
    int ProtectedRowCount = 0);

internal sealed record ExcelMacroFloorRange(
    int Floor,
    int StartRow,
    int EndRow,
    int StartColumn,
    int EndColumn);

internal sealed record ExcelMacroProtectedCellSnapshot(
    string Token,
    bool HasFormula,
    object? Formula,
    object? Value);

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

        return ExportAndRunWithExcel(rows, action, excelObject!);
    }

    internal static ExcelMacroTakeoffExportResult ExportAndRunWithExcel(
        IReadOnlyList<ExcelMacroPayloadRow> rows,
        ExcelMacroExportActionConfig action,
        object excelObject)
    {
        if (rows.Count == 0)
            return Failure("No rows were prepared for Excel.");
        if (!TryValidate(action, out string validationError))
            return Failure(validationError);

        string automationStage = "opening the configured workbook";
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

            automationStage = "finding the next vertical source block";
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
            automationStage = "writing source values";
            writeRange.Value2 = BuildValueMatrix(rows);

            ApplyFloorHeaderFormatting(sheet, rows, startRow, writeColumn, endWriteColumn);

            int preprocessRunCount = 0;
            if (!string.IsNullOrWhiteSpace(action.PerFloorPreprocessMacroName))
            {
                IReadOnlyList<ExcelMacroFloorRange> floorRanges =
                    BuildPerFloorRanges(rows, startRow, writeColumn);
                if (floorRanges.Count == 0)
                {
                    return Failure(
                        $"{action.Label}: per-floor macro '{action.PerFloorPreprocessMacroName}' " +
                        "is configured, but the payload has no floor groups.");
                }

                foreach (ExcelMacroFloorRange floorRange in floorRanges)
                {
                    dynamic floorFirst = sheet.Cells[floorRange.StartRow, floorRange.StartColumn];
                    dynamic floorLast = sheet.Cells[floorRange.EndRow, floorRange.EndColumn];
                    dynamic floorSelection = sheet.Range[floorFirst, floorLast];
                    floorSelection.Select();
                    automationStage =
                        $"running {action.PerFloorPreprocessMacroName} for floor {floorRange.Floor}";
                    RunWorkbookMacro(excel, workbook, action.PerFloorPreprocessMacroName);
                    preprocessRunCount++;
                }
            }

            dynamic selectionFirst = sheet.Cells[startRow, writeColumn];
            dynamic selectionLast = sheet.Cells[
                endRow,
                writeColumn + MacroSelectionColumnCount - 1];
            dynamic selection = sheet.Range[selectionFirst, selectionLast];
            selection.Select();

            automationStage = $"running {action.MacroName}";
            RunWorkbookMacro(excel, workbook, action.MacroName);

            int protectedRowCount = 0;
            if (!string.IsNullOrWhiteSpace(action.AfterMacroName))
            {
                automationStage =
                    $"protecting mandatory rows in {action.AfterMacroRange}";
                IReadOnlyList<ExcelMacroProtectedCellSnapshot> protectedRows =
                    ProtectOutputRows(sheet, action);
                protectedRowCount = protectedRows.Count;
                try
                {
                    dynamic afterRange = sheet.Range[action.AfterMacroRange];
                    afterRange.Select();
                    automationStage = $"running {action.AfterMacroName}";
                    RunWorkbookMacro(excel, workbook, action.AfterMacroName);
                }
                finally
                {
                    automationStage =
                        $"restoring {protectedRowCount} mandatory output row(s)";
                    RestoreProtectedOutputRows(sheet, action, protectedRows);
                }
            }

            string writtenRange =
                $"{CellAddress(startRow, writeColumn)}:{CellAddress(endRow, endWriteColumn)}";
            string selectedRange =
                $"{CellAddress(startRow, writeColumn)}:" +
                $"{CellAddress(endRow, writeColumn + MacroSelectionColumnCount - 1)}";
            string message =
                $"{action.Label}: wrote {rows.Count} row(s) to {writtenRange}, " +
                (preprocessRunCount > 0
                    ? $"ran {action.PerFloorPreprocessMacroName} for {preprocessRunCount} floor(s), "
                    : "") +
                $"selected {selectedRange}, ran {action.MacroName}" +
                (!string.IsNullOrWhiteSpace(action.AfterMacroName)
                    ? $", protected {protectedRowCount} mandatory row(s), " +
                      $"selected {action.AfterMacroRange}, ran {action.AfterMacroName}"
                    : "") +
                ".";
            AppLog.Info($"Excel macro export completed. Workbook={action.WorkbookName}; Sheet={action.SheetName}; {message}");
            return new ExcelMacroTakeoffExportResult(
                true,
                message,
                rows.Count,
                writtenRange,
                selectedRange,
                action.MacroName,
                action.PerFloorPreprocessMacroName,
                preprocessRunCount,
                action.AfterMacroName,
                action.AfterMacroRange,
                protectedRowCount);
        }
        catch (COMException ex)
        {
            AppLog.Error(ex, $"Excel macro export failed for {action.Id}");
            return Failure(
                $"Excel stopped while {automationStage}: {ex.Message} " +
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

    internal static IReadOnlyList<ExcelMacroFloorRange> BuildPerFloorRanges(
        IReadOnlyList<ExcelMacroPayloadRow> rows,
        int startRow,
        int writeColumn)
    {
        var ranges = new List<ExcelMacroFloorRange>();
        for (int index = 0; index < rows.Count; index++)
        {
            if (!rows[index].IsFloorHeader ||
                !int.TryParse(
                    rows[index].Name,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int floor))
            {
                continue;
            }

            int firstItemIndex = index + 1;
            int nextHeaderIndex = firstItemIndex;
            while (nextHeaderIndex < rows.Count && !rows[nextHeaderIndex].IsFloorHeader)
                nextHeaderIndex++;
            int lastItemIndex = nextHeaderIndex - 1;
            if (firstItemIndex <= lastItemIndex)
            {
                ranges.Add(new ExcelMacroFloorRange(
                    floor,
                    startRow + firstItemIndex,
                    startRow + lastItemIndex,
                    writeColumn,
                    writeColumn + ExportColumnCount - 1));
            }
            index = lastItemIndex;
        }
        return ranges;
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

    internal static bool IsProtectedOutputLabel(
        string? value,
        IReadOnlyList<string> protectedLabels)
    {
        string normalized = NormalizeOutputLabel(value);
        if (normalized.Length == 0)
            return false;
        foreach (string label in protectedLabels)
        {
            if (string.Equals(
                    normalized,
                    NormalizeOutputLabel(label),
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    internal static bool TryValidateRangeAddress(string value, out string error)
    {
        error = "";
        Match match = Regex.Match(
            value?.Trim() ?? "",
            @"^\$?(?<c1>[A-Za-z]+)\$?(?<r1>\d+):\$?(?<c2>[A-Za-z]+)\$?(?<r2>\d+)$",
            RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            error = "Enter a rectangular Excel range, for example A25:H1367.";
            return false;
        }

        int firstColumn = ColumnNumber(match.Groups["c1"].Value);
        int lastColumn = ColumnNumber(match.Groups["c2"].Value);
        bool rowsValid =
            int.TryParse(match.Groups["r1"].Value, out int firstRow) &&
            int.TryParse(match.Groups["r2"].Value, out int lastRow) &&
            firstRow >= 1 &&
            lastRow >= firstRow;
        if (firstColumn <= 0 || lastColumn < firstColumn || !rowsValid)
        {
            error = "The Excel cleanup range must run from top-left to bottom-right.";
            return false;
        }
        return true;
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

    internal static dynamic? FindWorkbook(dynamic excel, string workbookName, out string openNames)
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

    private static IReadOnlyList<ExcelMacroProtectedCellSnapshot> ProtectOutputRows(
        dynamic sheet,
        ExcelMacroExportActionConfig action)
    {
        dynamic outputRange = sheet.Range[action.AfterMacroRange];
        int firstRow = Convert.ToInt32(outputRange.Row, CultureInfo.InvariantCulture);
        int rowCount = Convert.ToInt32(outputRange.Rows.Count, CultureInfo.InvariantCulture);
        int columnCount = Convert.ToInt32(
            outputRange.Columns.Count,
            CultureInfo.InvariantCulture);
        object? values = outputRange.Value2;
        var snapshots = new List<ExcelMacroProtectedCellSnapshot>();
        try
        {
            for (int rowOffset = 0; rowOffset < rowCount; rowOffset++)
            {
                bool protectedRow = false;
                for (int columnOffset = 0; columnOffset < columnCount; columnOffset++)
                {
                    object? value = MatrixValue(
                        values,
                        rowOffset,
                        columnOffset,
                        rowCount,
                        columnCount);
                    if (!IsProtectedOutputLabel(
                            Convert.ToString(value, CultureInfo.InvariantCulture),
                            action.AfterMacroProtectedLabels))
                    {
                        continue;
                    }
                    protectedRow = true;
                    break;
                }
                if (!protectedRow)
                    continue;

                dynamic guardCell = sheet.Cells[firstRow + rowOffset, 3];
                bool hasFormula = Convert.ToBoolean(
                    guardCell.HasFormula,
                    CultureInfo.InvariantCulture);
                object? formula = guardCell.Formula;
                object? value2 = guardCell.Value2;
                string token = $"__OPC_KEEP_{Guid.NewGuid():N}__";
                guardCell.Value2 = token;
                snapshots.Add(new ExcelMacroProtectedCellSnapshot(
                    token,
                    hasFormula,
                    formula,
                    value2));
            }
        }
        catch
        {
            RestoreProtectedOutputRows(sheet, action, snapshots);
            throw;
        }
        return snapshots;
    }

    private static void RestoreProtectedOutputRows(
        dynamic sheet,
        ExcelMacroExportActionConfig action,
        IReadOnlyList<ExcelMacroProtectedCellSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
            return;

        dynamic outputRange = sheet.Range[action.AfterMacroRange];
        int firstRow = Convert.ToInt32(outputRange.Row, CultureInfo.InvariantCulture);
        int rowCount = Convert.ToInt32(outputRange.Rows.Count, CultureInfo.InvariantCulture);
        dynamic markerRange = sheet.Range[
            sheet.Cells[firstRow, 3],
            sheet.Cells[firstRow + rowCount - 1, 3]];
        object? markerValues = markerRange.Value2;
        var markerRows = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int rowOffset = 0; rowOffset < rowCount; rowOffset++)
        {
            string marker = Convert.ToString(
                MatrixValue(markerValues, rowOffset, 0, rowCount, 1),
                CultureInfo.InvariantCulture) ?? "";
            if (marker.StartsWith("__OPC_KEEP_", StringComparison.Ordinal))
                markerRows[marker] = firstRow + rowOffset;
        }

        int missingCount = 0;
        foreach (ExcelMacroProtectedCellSnapshot snapshot in snapshots)
        {
            if (!markerRows.TryGetValue(snapshot.Token, out int row))
            {
                missingCount++;
                continue;
            }
            dynamic cell = sheet.Cells[row, 3];
            if (snapshot.HasFormula)
                cell.Formula = snapshot.Formula;
            else
                cell.Value2 = snapshot.Value;
        }
        if (missingCount > 0)
        {
            throw new InvalidOperationException(
                $"{missingCount} mandatory Excel output row(s) disappeared during zero-row cleanup.");
        }
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

    internal static void RunWorkbookMacro(
        dynamic excel,
        dynamic workbook,
        string macroName)
    {
        string workbookName =
            Convert.ToString(workbook.Name, CultureInfo.InvariantCulture) ?? "";
        string qualifiedMacro =
            $"'{workbookName.Replace("'", "''", StringComparison.Ordinal)}'!{macroName}";
        excel.Run(qualifiedMacro);
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
        else if (string.IsNullOrWhiteSpace(action.AfterMacroName) !=
                 string.IsNullOrWhiteSpace(action.AfterMacroRange))
            error = $"{action.Label}: after-macro name and range must be configured together.";
        else if (!string.IsNullOrWhiteSpace(action.AfterMacroRange) &&
                 !TryValidateRangeAddress(action.AfterMacroRange, out string rangeError))
            error = $"{action.Label}: {rangeError}";
        return error.Length == 0;
    }

    internal static bool TryGetRunningExcel(out object? excelObject, out string error)
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

    private static string NormalizeOutputLabel(string? value)
    {
        string normalized = (value ?? "")
            .Replace('\u00A0', ' ')
            .Replace('\t', ' ')
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        while (normalized.Contains("  ", StringComparison.Ordinal))
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
        return normalized;
    }

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
