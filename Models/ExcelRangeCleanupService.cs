using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace OurPlanCore;

internal sealed record ExcelRowDeletionRange(int FirstRow, int LastRow)
{
    public int RowCount => LastRow - FirstRow + 1;
}

internal sealed record ExcelRangeCleanupResult(
    int ZeroRowsDeleted,
    int BlankRowsDeleted,
    int BlankPassCount);

internal static class ExcelRangeCleanupService
{
    internal const string DeleteZeroRowsMacroName =
        "B_DeleteZeroRowsOnlyIn_AtoH";

    private const int FirstCleanupColumn = 1;
    private const int LastCleanupColumn = 8;
    private const int ZeroGuardColumnOffset = 2;
    private const int XlCalculationManual = -4135;
    private const int XlShiftUp = -4162;
    private const int MaxBlankPasses = 31;

    internal static bool CanReplaceMacro(string? macroName) =>
        string.Equals(
            macroName?.Trim(),
            DeleteZeroRowsMacroName,
            StringComparison.OrdinalIgnoreCase);

    internal static ExcelRangeCleanupResult CleanZeroAndBlankRows(
        dynamic excel,
        dynamic sheet,
        string selectedRangeAddress)
    {
        dynamic selectedRange = sheet.Range[selectedRangeAddress];
        int selectedFirstColumn = Convert.ToInt32(
            selectedRange.Column,
            CultureInfo.InvariantCulture);
        int selectedColumnCount = Convert.ToInt32(
            selectedRange.Columns.Count,
            CultureInfo.InvariantCulture);
        int selectedLastColumn = selectedFirstColumn + selectedColumnCount - 1;
        if (selectedFirstColumn > LastCleanupColumn ||
            selectedLastColumn < FirstCleanupColumn)
        {
            throw new InvalidOperationException(
                $"Cleanup range '{selectedRangeAddress}' does not intersect columns A:H.");
        }

        int firstRow = Convert.ToInt32(
            selectedRange.Row,
            CultureInfo.InvariantCulture);
        int rowCount = Convert.ToInt32(
            selectedRange.Rows.Count,
            CultureInfo.InvariantCulture);
        if (firstRow < 1 || rowCount < 1)
            throw new InvalidOperationException("Excel cleanup range has no rows.");

        object originalCalculation = excel.Calculation;
        bool originalEnableEvents = Convert.ToBoolean(
            excel.EnableEvents,
            CultureInfo.InvariantCulture);
        bool originalScreenUpdating = Convert.ToBoolean(
            excel.ScreenUpdating,
            CultureInfo.InvariantCulture);
        try
        {
            excel.ScreenUpdating = false;
            excel.Calculation = XlCalculationManual;
            excel.EnableEvents = false;

            object? values = ReadCleanupValues(sheet, firstRow, rowCount);
            IReadOnlyList<ExcelRowDeletionRange> zeroRanges =
                PlanZeroRowDeletions(
                    values,
                    firstRow,
                    rowCount,
                    LastCleanupColumn);
            int zeroRowsDeleted = DeleteRanges(sheet, zeroRanges);

            int blankRowsDeleted = 0;
            int blankPassCount = 0;
            for (int pass = 1; pass <= MaxBlankPasses; pass++)
            {
                blankPassCount = pass;
                values = ReadCleanupValues(sheet, firstRow, rowCount);
                IReadOnlyList<ExcelRowDeletionRange> blankRanges =
                    PlanBlankRowDeletions(
                        values,
                        firstRow,
                        rowCount,
                        LastCleanupColumn);
                if (blankRanges.Count == 0)
                    break;
                blankRowsDeleted += DeleteRanges(sheet, blankRanges);
            }

            return new ExcelRangeCleanupResult(
                zeroRowsDeleted,
                blankRowsDeleted,
                blankPassCount);
        }
        finally
        {
            RestoreExcelState(
                excel,
                originalCalculation,
                originalEnableEvents,
                originalScreenUpdating);
        }
    }

    internal static IReadOnlyList<ExcelRowDeletionRange> PlanZeroRowDeletions(
        object? values,
        int firstRow,
        int rowCount,
        int columnCount)
    {
        var rows = new List<int>();
        for (int rowOffset = 0; rowOffset < rowCount; rowOffset++)
        {
            object? value = MatrixValue(
                values,
                rowOffset,
                ZeroGuardColumnOffset,
                rowCount,
                columnCount);
            if (IsNumericZero(value))
                rows.Add(firstRow + rowOffset);
        }
        return GroupContiguousRows(rows);
    }

    internal static IReadOnlyList<ExcelRowDeletionRange> PlanBlankRowDeletions(
        object? values,
        int firstRow,
        int rowCount,
        int columnCount)
    {
        var ranges = new List<ExcelRowDeletionRange>();
        int runStartOffset = -1;
        for (int rowOffset = 0; rowOffset <= rowCount; rowOffset++)
        {
            bool blank = rowOffset < rowCount &&
                         IsVisualBlankRow(
                             values,
                             rowOffset,
                             rowCount,
                             columnCount);
            if (blank)
            {
                if (runStartOffset < 0)
                    runStartOffset = rowOffset;
                continue;
            }

            if (runStartOffset >= 0 && rowOffset - runStartOffset > 1)
            {
                ranges.Add(new ExcelRowDeletionRange(
                    firstRow + runStartOffset,
                    firstRow + rowOffset - 2));
            }
            runStartOffset = -1;
        }
        return ranges;
    }

    private static object? ReadCleanupValues(
        dynamic sheet,
        int firstRow,
        int rowCount)
    {
        dynamic firstCell = sheet.Cells[firstRow, FirstCleanupColumn];
        dynamic lastCell = sheet.Cells[
            firstRow + rowCount - 1,
            LastCleanupColumn];
        dynamic range = sheet.Range[firstCell, lastCell];
        return range.Value2;
    }

    private static int DeleteRanges(
        dynamic sheet,
        IReadOnlyList<ExcelRowDeletionRange> ranges)
    {
        int deleted = 0;
        for (int index = ranges.Count - 1; index >= 0; index--)
        {
            ExcelRowDeletionRange range = ranges[index];
            dynamic firstCell = sheet.Cells[
                range.FirstRow,
                FirstCleanupColumn];
            dynamic lastCell = sheet.Cells[
                range.LastRow,
                LastCleanupColumn];
            dynamic deleteRange = sheet.Range[firstCell, lastCell];
            deleteRange.Delete(XlShiftUp);
            deleted += range.RowCount;
        }
        return deleted;
    }

    private static IReadOnlyList<ExcelRowDeletionRange> GroupContiguousRows(
        IReadOnlyList<int> rows)
    {
        if (rows.Count == 0)
            return [];

        var ranges = new List<ExcelRowDeletionRange>();
        int first = rows[0];
        int last = first;
        for (int index = 1; index < rows.Count; index++)
        {
            int row = rows[index];
            if (row == last + 1)
            {
                last = row;
                continue;
            }
            ranges.Add(new ExcelRowDeletionRange(first, last));
            first = row;
            last = row;
        }
        ranges.Add(new ExcelRowDeletionRange(first, last));
        return ranges;
    }

    private static bool IsVisualBlankRow(
        object? values,
        int rowOffset,
        int rowCount,
        int columnCount)
    {
        for (int columnOffset = 0; columnOffset < columnCount; columnOffset++)
        {
            object? value = MatrixValue(
                values,
                rowOffset,
                columnOffset,
                rowCount,
                columnCount);
            if (value is ErrorWrapper)
                return false;
            if (value == null)
                continue;
            if (value is string text &&
                text.Replace('\u00A0', ' ').Trim().Length == 0)
            {
                continue;
            }
            return false;
        }
        return true;
    }

    private static bool IsNumericZero(object? value)
    {
        if (value == null || value is ErrorWrapper)
            return false;
        if (value is string text)
        {
            string normalized = text.Replace('\u00A0', ' ').Trim();
            if (normalized.Length == 0)
                return false;
            return TryParseDouble(normalized, out double parsed) && parsed == 0d;
        }

        return Type.GetTypeCode(value.GetType()) switch
        {
            TypeCode.Byte or
            TypeCode.SByte or
            TypeCode.UInt16 or
            TypeCode.UInt32 or
            TypeCode.UInt64 or
            TypeCode.Int16 or
            TypeCode.Int32 or
            TypeCode.Int64 or
            TypeCode.Decimal or
            TypeCode.Double or
            TypeCode.Single =>
                Convert.ToDouble(value, CultureInfo.InvariantCulture) == 0d,
            _ => false,
        };
    }

    private static bool TryParseDouble(string value, out double parsed) =>
        double.TryParse(
            value,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.CurrentCulture,
            out parsed) ||
        double.TryParse(
            value,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out parsed);

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

    private static void RestoreExcelState(
        dynamic excel,
        object originalCalculation,
        bool originalEnableEvents,
        bool originalScreenUpdating)
    {
        TryRestore(
            () => excel.EnableEvents = originalEnableEvents,
            "EnableEvents");
        TryRestore(
            () => excel.Calculation = originalCalculation,
            "Calculation");
        TryRestore(
            () => excel.ScreenUpdating = originalScreenUpdating,
            "ScreenUpdating");
    }

    private static void TryRestore(Action restore, string setting)
    {
        try
        {
            restore();
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Could not restore Excel {setting} after fast cleanup");
        }
    }
}
