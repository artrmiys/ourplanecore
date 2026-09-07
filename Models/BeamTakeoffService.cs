using System;
using System.Globalization;
using SkiaSharp;

namespace OurPlanCore;

public static class BeamTakeoffService
{
    public const string DefaultNamePrefix = "Beam";

    public static SKPoint DimensionOffset(SKPoint start, SKPoint end, SKPoint count, float distance)
    {
        SKPoint delta = end - start;
        float length = MathF.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
        if (length <= 0 || !float.IsFinite(distance))
            return SKPoint.Empty;
        SKPoint normal = new(-delta.Y / length, delta.X / length);
        SKPoint centerToCount = count - new SKPoint((start.X + end.X) / 2, (start.Y + end.Y) / 2);
        if (normal.X * centerToCount.X + normal.Y * centerToCount.Y > 0)
            normal = new SKPoint(-normal.X, -normal.Y);
        return new SKPoint(normal.X * distance, normal.Y * distance);
    }

    public static double RoundOrderLengthFeet(double feet)
    {
        if (!double.IsFinite(feet) || feet <= 0)
            return 0;

        const double epsilon = 1e-9;
        return feet > 8
            ? Math.Ceiling((feet - epsilon) / 2.0) * 2.0
            : Math.Ceiling(feet - epsilon);
    }

    public static string FormatOrderLengthFeet(double feet)
    {
        double rounded = RoundOrderLengthFeet(feet);
        return rounded <= 0
            ? "0"
            : rounded.ToString("0", CultureInfo.InvariantCulture);
    }

    public static string BuildDefaultCountName(
        string? folderPrefix,
        string sizeText,
        out int editablePrefixLength)
    {
        string prefix = string.IsNullOrWhiteSpace(folderPrefix)
            ? DefaultNamePrefix
            : JoinPrefix(folderPrefix.Trim(), DefaultNamePrefix);
        string cleanSize = string.IsNullOrWhiteSpace(sizeText) ? "0" : sizeText.Trim();
        string name = $"{prefix} {cleanSize}";
        editablePrefixLength = prefix.Length;
        return name;
    }

    private static string JoinPrefix(string folderPrefix, string baseName)
    {
        if (baseName.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
            return baseName;

        return folderPrefix.EndsWith(" ", StringComparison.Ordinal) ||
               folderPrefix.EndsWith("-", StringComparison.Ordinal) ||
               folderPrefix.EndsWith("_", StringComparison.Ordinal)
            ? folderPrefix + baseName
            : $"{folderPrefix} {baseName}";
    }
}
