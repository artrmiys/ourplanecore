using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace OurPlanCore;

// Name comparers behind the Takeoffs-tree "Sort Walls" and "Sort Details"
// commands. Both fall back to natural ordering inside each bucket.

/// <summary>
/// Wall takeoff ordering: ext → corr/cor → dem → bare stud-size names
/// (2x8 before 2x6 before 2x4) → everything else. Matches E-Wood wall naming
/// like "ext 9.09", "corr 2x6 9.09", "dem 2x6 11.15 staggered",
/// "2x4 10.3 furring".
/// </summary>
public sealed class TakeoffWallNameComparer : IComparer<string>
{
    public static readonly TakeoffWallNameComparer Instance = new();

    // Order matters: "corr" must be probed before "cor" so both spellings
    // land in the same bucket. Latin and Cyrillic "x" both appear in names.
    private static readonly (string Prefix, int Rank)[] CategoryPrefixes =
    [
        ("ext", 0),
        ("corr", 1),
        ("cor", 1),
        ("dem", 2),
    ];

    private static readonly Regex StudSizePattern = new(
        @"^(?<w>\d+)\s*[xх]\s*(?<h>\d+(?:[.,]\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private TakeoffWallNameComparer()
    {
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x == null) return -1;
        if (y == null) return 1;

        (int leftRank, double leftSize) = Classify(x);
        (int rightRank, double rightSize) = Classify(y);
        if (leftRank != rightRank)
            return leftRank.CompareTo(rightRank);

        // Bare stud-size bucket: bigger studs first (2x6 above 2x4).
        if (leftRank == 3 && Math.Abs(leftSize - rightSize) > 0.0001)
            return rightSize.CompareTo(leftSize);

        return TakeoffDetailReferenceNameComparer.NaturalCompare(x, y);
    }

    private static (int Rank, double SizeKey) Classify(string name)
    {
        string clean = (name ?? "").Trim().ToLowerInvariant();
        if (clean.Length == 0)
            return (4, 0);

        foreach ((string prefix, int rank) in CategoryPrefixes)
        {
            if (HasWordPrefix(clean, prefix))
                return (rank, 0);
        }

        Match size = StudSizePattern.Match(clean);
        if (size.Success &&
            double.TryParse(size.Groups["h"].Value.Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double height))
        {
            return (3, height);
        }

        return (4, 0);
    }

    private static bool HasWordPrefix(string name, string prefix) =>
        name.StartsWith(prefix, StringComparison.Ordinal) &&
        (name.Length == prefix.Length || !char.IsLetter(name[prefix.Length]));
}

/// <summary>
/// Detail takeoff ordering for names like "1/A501" or "2_A501": group by the
/// sheet after the separator first (natural order, the same way sheets read
/// in the Pages tree), then by the detail part before it. Names without a
/// separator sort after all details.
/// </summary>
public sealed class TakeoffDetailSheetNameComparer : IComparer<string>
{
    public static readonly TakeoffDetailSheetNameComparer Instance = new();

    private static readonly Regex DetailPattern = new(
        @"^\s*(?<detail>[^/_]+?)\s*[/_]\s*(?<sheet>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private TakeoffDetailSheetNameComparer()
    {
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x == null) return -1;
        if (y == null) return 1;

        bool leftIsDetail = TryParse(x, out string leftDetail, out string leftSheet);
        bool rightIsDetail = TryParse(y, out string rightDetail, out string rightSheet);
        if (leftIsDetail != rightIsDetail)
            return leftIsDetail ? -1 : 1;

        if (!leftIsDetail)
            return TakeoffDetailReferenceNameComparer.NaturalCompare(x, y);

        int sheet = TakeoffDetailReferenceNameComparer.NaturalCompare(leftSheet, rightSheet);
        if (sheet != 0)
            return sheet;

        int detail = TakeoffDetailReferenceNameComparer.NaturalCompare(leftDetail, rightDetail);
        return detail != 0 ? detail : TakeoffDetailReferenceNameComparer.NaturalCompare(x, y);
    }

    private static bool TryParse(string name, out string detail, out string sheet)
    {
        detail = "";
        sheet = "";
        Match match = DetailPattern.Match(name ?? "");
        if (!match.Success)
            return false;

        detail = match.Groups["detail"].Value;
        sheet = match.Groups["sheet"].Value;
        return sheet.Length > 0;
    }
}
