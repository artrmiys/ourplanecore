using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace OurPlanCore;

public sealed record ReportWallSourceRow(int RowNumber, string Label, string Value);

public sealed record ReportWallBlockResult(int FloorGroups, int SourceRows, int Assignments, IReadOnlyList<string> Messages);

public static partial class ReportWallsBlockService
{
    private static readonly Dictionary<string, string> LabelColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ext 2x6"] = "P",
        ["ext 2x4"] = "Q",
        ["ext 2x8"] = "R",
        ["cor 2x8"] = "S",
        ["cor 2x6"] = "T",
        ["cor 2x4"] = "U",
        ["dem 2x8"] = "V",
        ["dem 2x6"] = "W",
        ["dem 2x4"] = "X",
        ["2x8"] = "Y",
        ["2x6"] = "Z",
        ["2x4"] = "AA",
        ["2x3"] = "AB",
        ["2x6 half"] = "Z",
        ["2x4 half"] = "AA",
        ["2x6half"] = "Z",
        ["2x4half"] = "AA",
    };

    public static IReadOnlyList<ReportWallSourceRow> SourceRowsFromReportRows(IEnumerable<ReportBuilderRow> rows) =>
        rows
            .OrderBy(row => row.RowNumber)
            .Where(row => !string.IsNullOrWhiteSpace(row.J))
            .Select(row => new ReportWallSourceRow(row.RowNumber, row.J, row.K))
            .ToList();

    public static ReportWallBlockResult ApplyAllGroups(
        IReadOnlyList<ReportBuilderRow> reportRows,
        IReadOnlyList<ReportWallSourceRow> sourceRows)
    {
        var messages = new List<string>();
        var rowByNumber = reportRows.ToDictionary(row => row.RowNumber);
        int groups = 0;
        int assignments = 0;
        int groupStart = -1;

        for (int index = 0; index < sourceRows.Count; index++)
        {
            if (TryFloorId(sourceRows[index].Label, out int floorId))
            {
                if (groupStart >= 0)
                    assignments += ApplyOneGroup(rowByNumber, sourceRows.Skip(groupStart).Take(index - groupStart).ToList(), messages);
                groupStart = index;
                groups++;
            }
        }

        if (groupStart >= 0)
            assignments += ApplyOneGroup(rowByNumber, sourceRows.Skip(groupStart).ToList(), messages);
        else
            messages.Add("No floor marker 0-5 was found in the selected wall source rows.");

        return new ReportWallBlockResult(groups, sourceRows.Count, assignments, messages);
    }

    private static int ApplyOneGroup(
        IReadOnlyDictionary<int, ReportBuilderRow> rowByNumber,
        IReadOnlyList<ReportWallSourceRow> sourceRows,
        List<string> messages)
    {
        if (sourceRows.Count == 0)
            return 0;
        if (!TryFloorId(sourceRows[0].Label, out int floorId) ||
            !TryFloorConfig(floorId, out int xStart, out int xEnd, out int halfRow, out int cornersRow))
        {
            messages.Add($"Row {sourceRows[0].RowNumber}: wall floor marker must be 0-5.");
            return 0;
        }

        int assignments = 0;
        var xOrder = new List<string>();
        var values = new Dictionary<(string X, string Label), double>();
        var halfValues = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (ReportWallSourceRow row in sourceRows.Skip(1))
        {
            string label = Clean(row.Label);
            if (string.IsNullOrWhiteSpace(label))
                continue;

            if (label is "corner" or "corners")
            {
                if (TryParseNumber(row.Value, out double cornerValue))
                    assignments += SetCell(rowByNumber, cornersRow, "Q", FormatNumber(cornerValue));
                continue;
            }

            if (!TryParseNumber(row.Value, out double quantity))
                continue;

            if (label.Contains("half", StringComparison.OrdinalIgnoreCase))
            {
                if (label.Contains("2x6", StringComparison.OrdinalIgnoreCase))
                    AddValue(halfValues, "2x6half", quantity);
                if (label.Contains("2x4", StringComparison.OrdinalIgnoreCase))
                    AddValue(halfValues, "2x4half", quantity);
                continue;
            }

            if (!TryWallLabelAndX(label, out string labelKey, out string xValue, out double multiplier))
                continue;

            if (!LabelColumns.ContainsKey(labelKey))
                continue;

            AddX(xOrder, xValue);
            AddValue(values, (xValue, labelKey), quantity * multiplier);
        }

        int maxRows = xEnd - xStart + 1;
        var outputRowByX = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < Math.Min(xOrder.Count, maxRows); index++)
        {
            int rowNumber = xStart + index;
            string xValue = xOrder[index];
            assignments += SetCell(rowByNumber, rowNumber, "O", FormatXNumber(xValue));
            outputRowByX[xValue] = rowNumber;
        }

        foreach (((string x, string label), double value) in values)
        {
            if (!outputRowByX.TryGetValue(x, out int targetRow) ||
                !LabelColumns.TryGetValue(label, out string? targetColumn))
            {
                continue;
            }

            assignments += SetCell(rowByNumber, targetRow, targetColumn, FormatNumber(value));
        }

        if (halfValues.TryGetValue("2x6half", out double half2x6))
            assignments += SetCell(rowByNumber, halfRow, "Z", FormatNumber(half2x6));
        if (halfValues.TryGetValue("2x4half", out double half2x4))
            assignments += SetCell(rowByNumber, halfRow, "AA", FormatNumber(half2x4));

        return assignments;
    }

    private static bool TryWallLabelAndX(string label, out string labelKey, out string xValue, out double multiplier)
    {
        labelKey = "";
        xValue = "";
        multiplier = 1;

        Match multiplierMatch = MultiplierRegex().Match(label);
        if (multiplierMatch.Success &&
            double.TryParse(multiplierMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedMultiplier))
        {
            multiplier = parsedMultiplier;
        }

        List<string> sizes = SizeRegex()
            .Matches(label)
            .Select(match => match.Value.ToLowerInvariant())
            .ToList();
        if (sizes.Count != 1)
            return false;

        string prefix = "";
        if (label.StartsWith("corr", StringComparison.OrdinalIgnoreCase) ||
            label.StartsWith("cor", StringComparison.OrdinalIgnoreCase))
            prefix = "cor";
        else if (label.StartsWith("dem", StringComparison.OrdinalIgnoreCase))
            prefix = "dem";
        else if (label.StartsWith("ext", StringComparison.OrdinalIgnoreCase))
            prefix = "ext";

        string size = sizes[0];
        labelKey = string.IsNullOrWhiteSpace(prefix) ? size : $"{prefix} {size}";

        string textWithoutMultiplier = MultiplierRegex().Replace(label, " ");
        string textWithoutSize = SizeRegex().Replace(textWithoutMultiplier, " ");
        MatchCollection numbers = NumberRegex().Matches(textWithoutSize);
        if (numbers.Count == 0)
            return false;

        xValue = NormalizeNumberText(numbers[^1].Groups[1].Value);
        return !string.IsNullOrWhiteSpace(xValue);
    }

    private static bool TryFloorConfig(int floorId, out int xStart, out int xEnd, out int halfRow, out int cornersRow)
    {
        (xStart, xEnd, halfRow, cornersRow) = floorId switch
        {
            0 => (30, 36, 36, 28),
            1 => (40, 49, 49, 38),
            2 => (53, 59, 59, 51),
            3 => (63, 69, 69, 61),
            4 => (73, 79, 79, 71),
            5 => (83, 92, 92, 81),
            _ => (0, 0, 0, 0),
        };
        return xStart > 0;
    }

    private static int SetCell(IReadOnlyDictionary<int, ReportBuilderRow> rows, int rowNumber, string column, string value)
    {
        if (!rows.TryGetValue(rowNumber, out ReportBuilderRow? row))
            return 0;

        row.SetCellValue(column, value);
        return 1;
    }

    private static void AddX(List<string> xOrder, string value)
    {
        if (!xOrder.Contains(value, StringComparer.OrdinalIgnoreCase))
            xOrder.Add(value);
    }

    private static void AddValue<TKey>(Dictionary<TKey, double> values, TKey key, double value)
        where TKey : notnull
    {
        values[key] = values.TryGetValue(key, out double existing)
            ? existing + value
            : value;
    }

    private static bool TryFloorId(string value, out int floorId)
    {
        floorId = -1;
        string clean = Clean(value);
        return int.TryParse(clean, NumberStyles.Integer, CultureInfo.InvariantCulture, out floorId) &&
               floorId is >= 0 and <= 5;
    }

    private static bool TryParseNumber(string value, out double number)
    {
        string clean = NormalizeNumberText(value);
        return double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    private static string NormalizeNumberText(string value) =>
        Clean(value).Replace(',', '.');

    private static string FormatNumber(string value) =>
        TryParseNumber(value, out double number) ? FormatNumber(number) : value;

    private static string FormatNumber(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');

    private static string FormatXNumber(string value) =>
        TryParseNumber(value, out double number)
            ? number.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',')
            : value;

    private static string Clean(string value) =>
        string.Join(" ", (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim().ToLowerInvariant();

    [GeneratedRegex(@"\((\d+)\)")]
    private static partial Regex MultiplierRegex();

    [GeneratedRegex(@"2x(?:3|4|6|8)", RegexOptions.IgnoreCase)]
    private static partial Regex SizeRegex();

    [GeneratedRegex(@"(\d+(?:[\.,]\d*)?)")]
    private static partial Regex NumberRegex();
}
