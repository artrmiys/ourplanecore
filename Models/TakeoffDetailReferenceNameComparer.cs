using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace OurPlaneCore;

internal sealed class TakeoffDetailReferenceNameComparer : IComparer<string>
{
    public static readonly TakeoffDetailReferenceNameComparer Instance = new();

    private static readonly Regex DetailReferencePattern = new(
        @"^(?<detail>\d+)\s*/\s*(?<prefix>[A-Za-z]+)\s*-?\s*(?<sheet>\d+(?:\.\d+)*)(?<suffix>[A-Za-z]*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private TakeoffDetailReferenceNameComparer()
    {
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x == null) return -1;
        if (y == null) return 1;

        bool leftDetail = TryParse(x, out DetailReferenceSortKey left);
        bool rightDetail = TryParse(y, out DetailReferenceSortKey right);
        if (leftDetail && rightDetail)
            return CompareDetailReferences(left, right);

        return NaturalCompare(x, y);
    }

    public static bool TryParse(string? name, out DetailReferenceSortKey key)
    {
        key = default;
        string clean = (name ?? "").Trim();
        if (clean.Length == 0)
            return false;

        Match match = DetailReferencePattern.Match(clean);
        if (!match.Success ||
            !int.TryParse(match.Groups["detail"].Value, out int detailNumber) ||
            !TryParseSheetNumbers(match.Groups["sheet"].Value, out int[] sheetNumbers))
        {
            return false;
        }

        key = new DetailReferenceSortKey(
            match.Groups["prefix"].Value.ToUpperInvariant(),
            sheetNumbers,
            match.Groups["suffix"].Value.ToUpperInvariant(),
            detailNumber,
            clean);
        return true;
    }

    private static int CompareDetailReferences(DetailReferenceSortKey left, DetailReferenceSortKey right)
    {
        int prefix = string.Compare(left.SheetPrefix, right.SheetPrefix, StringComparison.OrdinalIgnoreCase);
        if (prefix != 0)
            return prefix;

        int sheet = CompareSheetNumbers(left.SheetNumbers, right.SheetNumbers);
        if (sheet != 0)
            return sheet;

        int suffix = string.Compare(left.SheetSuffix, right.SheetSuffix, StringComparison.OrdinalIgnoreCase);
        if (suffix != 0)
            return suffix;

        int detail = left.DetailNumber.CompareTo(right.DetailNumber);
        if (detail != 0)
            return detail;

        return NaturalCompare(left.Original, right.Original);
    }

    private static bool TryParseSheetNumbers(string text, out int[] numbers)
    {
        numbers = [];
        string[] parts = text.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        var parsed = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out parsed[i]))
                return false;
        }

        numbers = parsed;
        return true;
    }

    private static int CompareSheetNumbers(IReadOnlyList<int> left, IReadOnlyList<int> right)
    {
        int count = Math.Min(left.Count, right.Count);
        for (int i = 0; i < count; i++)
        {
            int number = left[i].CompareTo(right[i]);
            if (number != 0)
                return number;
        }

        return left.Count.CompareTo(right.Count);
    }

    private static int NaturalCompare(string x, string y)
    {
        int ix = 0;
        int iy = 0;
        while (ix < x.Length && iy < y.Length)
        {
            char cx = x[ix];
            char cy = y[iy];
            if (char.IsDigit(cx) && char.IsDigit(cy))
            {
                int nx = ReadNumberToken(x, ref ix);
                int ny = ReadNumberToken(y, ref iy);
                int numberCompare = nx.CompareTo(ny);
                if (numberCompare != 0)
                    return numberCompare;
                continue;
            }

            int charCompare = char.ToUpperInvariant(cx).CompareTo(char.ToUpperInvariant(cy));
            if (charCompare != 0)
                return charCompare;
            ix++;
            iy++;
        }

        return x.Length.CompareTo(y.Length);
    }

    private static int ReadNumberToken(string text, ref int index)
    {
        int start = index;
        while (index < text.Length && char.IsDigit(text[index]))
            index++;

        string digits = text[start..index].TrimStart('0');
        if (digits.Length == 0)
            return 0;
        return int.TryParse(digits, out int value) ? value : int.MaxValue;
    }
}

internal readonly record struct DetailReferenceSortKey(
    string SheetPrefix,
    IReadOnlyList<int> SheetNumbers,
    string SheetSuffix,
    int DetailNumber,
    string Original);
