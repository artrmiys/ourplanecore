using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace OurPlanCore;

public sealed class ThreeDWallSpec
{
    public string Label { get; init; } = "";
    public string SourceText { get; init; } = "";
    public string FramingSize { get; init; } = "2x6";
    public int PlyCount { get; init; } = 1;
    public double HeightFeet { get; init; } = ThreeDWallTakeoffBuilder.DefaultWallHeightFeet;
    public double ThicknessInches { get; init; } = ThreeDWallTakeoffBuilder.DefaultWallThicknessInches;
}

public sealed class ThreeDWallSegment
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string TakeoffName { get; init; } = "";
    public string TakeoffFolder { get; set; } = "";
    public string MeasurementId { get; init; } = "";
    public int SegmentIndex { get; init; }
    public string PageFolder { get; set; } = "";
    public double StartXFeet { get; init; }
    public double StartZFeet { get; init; }
    public double EndXFeet { get; init; }
    public double EndZFeet { get; init; }
    public double BaseElevationFeet { get; set; }
    public double HeightFeet { get; set; }
    public double ThicknessInches { get; set; }
    public int PlyCount { get; init; } = 1;
    public string FramingSize { get; init; } = "";
    public string Label { get; init; } = "";
    public string Color { get; init; } = "#78909C";
    public string GroupKey { get; set; } = "";
    public string LevelKey { get; set; } = "";
}

public sealed class ThreeDWallBuildResult
{
    public List<ThreeDWallSegment> Walls { get; } = [];
    public int SkippedNonLineMeasurements { get; set; }
    public int SkippedNoScaleMeasurements { get; set; }
    public int SkippedShortSegments { get; set; }
}

public static partial class ThreeDWallTakeoffBuilder
{
    public const double DefaultWallHeightFeet = 9.1;
    public const double DefaultWallThicknessInches = 6.0;

    private static readonly Regex FramingSizeRegex = new(
        @"(?:\(\s*(?<count>\d+)\s*\)\s*)?(?<stud>\d+)\s*[xX]\s*(?<depth>\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NumericRegex = new(
        @"(?<![A-Za-z])\d+(?:[\.,]\d+)?(?![A-Za-z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ThreeDWallSpec ParseSpec(TakeoffItem item, OurPlanCoreJob? job = null)
    {
        string sourceText = BuildSpecSourceText(item, job);
        Match size = FramingSizeRegex.Match(sourceText);

        int plyCount = 1;
        double thickness = DefaultWallThicknessInches;
        string framingSize = "2x6";
        if (size.Success)
        {
            if (int.TryParse(size.Groups["count"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCount) &&
                parsedCount > 1 &&
                parsedCount <= 8)
            {
                plyCount = parsedCount;
            }

            if (double.TryParse(size.Groups["depth"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double depth) &&
                depth > 0 &&
                depth <= 24)
            {
                thickness = depth * plyCount;
                framingSize = $"{size.Groups["stud"].Value}x{size.Groups["depth"].Value}";
            }
        }

        string withoutSize = size.Success ? FramingSizeRegex.Replace(sourceText, " ") : sourceText;
        double height = ParseWallHeightFeet(withoutSize);
        return new ThreeDWallSpec
        {
            Label = string.IsNullOrWhiteSpace(item.Name) ? "Wall" : item.Name.Trim(),
            SourceText = sourceText,
            FramingSize = framingSize,
            PlyCount = plyCount,
            HeightFeet = height,
            ThicknessInches = thickness,
        };
    }

    public static ThreeDWallBuildResult BuildWalls(
        IEnumerable<TakeoffItem> items,
        OurPlanCoreJob? job,
        Func<Measurement, double> scaleResolver)
    {
        var result = new ThreeDWallBuildResult();
        foreach (TakeoffItem item in items)
        {
            ThreeDWallSpec spec = ParseSpec(item, job);
            foreach (Measurement measurement in item.Measurements)
            {
                if (OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType) != "line" ||
                    measurement.Points.Count < 2)
                {
                    result.SkippedNonLineMeasurements++;
                    continue;
                }

                double scaleMetersPerPt = scaleResolver(measurement);
                if (scaleMetersPerPt <= 0)
                {
                    result.SkippedNoScaleMeasurements++;
                    continue;
                }

                AddMeasurementSegments(result, item, spec, measurement, scaleMetersPerPt);
            }
        }

        return result;
    }

    private static void AddMeasurementSegments(
        ThreeDWallBuildResult result,
        TakeoffItem item,
        ThreeDWallSpec spec,
        Measurement measurement,
        double scaleMetersPerPt)
    {
        double feetPerPt = scaleMetersPerPt / 0.3048;
        for (int i = 1; i < measurement.Points.Count; i++)
        {
            SKPoint a = measurement.Points[i - 1];
            SKPoint b = measurement.Points[i];
            double startX = a.X * feetPerPt;
            double startZ = a.Y * feetPerPt;
            double endX = b.X * feetPerPt;
            double endZ = b.Y * feetPerPt;
            double dx = endX - startX;
            double dz = endZ - startZ;
            if (Math.Sqrt(dx * dx + dz * dz) < 0.05)
            {
                result.SkippedShortSegments++;
                continue;
            }

            result.Walls.Add(new ThreeDWallSegment
            {
                TakeoffName = item.Name,
                TakeoffFolder = item.FolderPath,
                MeasurementId = measurement.Id,
                SegmentIndex = i,
                PageFolder = measurement.PageFolder,
                StartXFeet = startX,
                StartZFeet = startZ,
                EndXFeet = endX,
                EndZFeet = endZ,
                HeightFeet = spec.HeightFeet,
                ThicknessInches = spec.ThicknessInches,
                PlyCount = spec.PlyCount,
                FramingSize = spec.FramingSize,
                Label = spec.Label,
                Color = item.Color,
                GroupKey = item.FolderPath,
            });
        }
    }

    private static double ParseWallHeightFeet(string text)
    {
        double height = DefaultWallHeightFeet;
        foreach (Match match in NumericRegex.Matches(text))
        {
            string value = match.Value.Replace(',', '.');
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) &&
                parsed >= 4 &&
                parsed <= 40)
            {
                height = parsed;
            }
        }

        return height;
    }

    private static string BuildSpecSourceText(TakeoffItem item, OurPlanCoreJob? job)
    {
        var parts = new List<string> { item.Name, item.Notes };
        if (job != null && !string.IsNullOrWhiteSpace(item.FolderPath))
        {
            try
            {
                string relative = Path.GetRelativePath(job.TakeoffsRoot, item.FolderPath);
                if (!relative.StartsWith("..", StringComparison.Ordinal))
                    parts.Add(relative.Replace('\\', ' '));
            }
            catch
            {
            }
        }

        return string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }
}
