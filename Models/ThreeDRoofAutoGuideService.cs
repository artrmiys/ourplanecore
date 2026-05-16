using System.IO;
using System.Text.RegularExpressions;

namespace OurPlaneCore;

public sealed class ThreeDRoofAutoGuideResult
{
    public int RoofRegionCount { get; set; }
    public int EaveGuideCount { get; set; }
    public int ResetGuideCount { get; set; }
    public int SkippedManualRegionCount { get; set; }
    public List<string> Messages { get; } = [];
}

public static partial class ThreeDRoofAutoGuideService
{
    public const string AutoAdjustmentStatus = "auto_roof";
    private const double MinimumGuideLengthFeet = 0.25;
    private const double MinimumOppositeSeparationFeet = 0.5;
    private const double ParallelCrossTolerance = 0.17;

    public static ThreeDRoofAutoGuideResult ApplyAutoEaves(
        IList<ThreeDRoofGuide> guides,
        double pitchRisePerFoot)
    {
        var result = new ThreeDRoofAutoGuideResult();
        double pitch = Math.Clamp(pitchRisePerFoot > 0 ? pitchRisePerFoot : ThreeDRoofPreviewBuilder.DefaultPitchRisePerFoot, 0.001, 4.0);
        var groups = guides
            .Where(IsGeneratedRoofBaseGuide)
            .GroupBy(GuideRegionKey)
            .ToList();

        foreach (IGrouping<string, ThreeDRoofGuide> group in groups)
        {
            List<ThreeDRoofGuide> regionGuides = group.ToList();
            if (regionGuides.Any(IsManualEave))
            {
                result.SkippedManualRegionCount++;
                result.Messages.Add($"Skipped {RegionLabel(regionGuides)} because it already has a manually selected eave.");
                continue;
            }

            List<RoofEdgeCandidate> candidates = regionGuides
                .Select(ToCandidate)
                .Where(candidate => candidate.LengthFeet >= MinimumGuideLengthFeet)
                .ToList();
            if (candidates.Count == 0)
                continue;

            result.RoofRegionCount++;
            foreach (RoofEdgeCandidate candidate in candidates)
            {
                if (ResetToRake(candidate.Guide))
                    result.ResetGuideCount++;
            }

            IReadOnlyList<RoofEdgeCandidate> autoEaves = SelectAutoEaves(candidates);
            foreach (RoofEdgeCandidate candidate in autoEaves)
            {
                MarkAsAutoEave(candidate.Guide, pitch);
                result.EaveGuideCount++;
            }

            result.Messages.Add($"{RegionLabel(regionGuides)}: selected {autoEaves.Count} auto eave edge(s).");
        }

        return result;
    }

    private static bool IsGeneratedRoofBaseGuide(ThreeDRoofGuide guide) =>
        string.Equals(guide.Status, ThreeDRoofFootprintBuildService.GeneratedStatus, StringComparison.OrdinalIgnoreCase) &&
        guide.Points.Count >= 2;

    private static bool IsManualEave(ThreeDRoofGuide guide) =>
        ThreeDRoofGuideKinds.Normalize(guide.Kind) == ThreeDRoofGuideKinds.Eave &&
        !string.Equals(guide.AdjustmentStatus, AutoAdjustmentStatus, StringComparison.OrdinalIgnoreCase);

    private static string GuideRegionKey(ThreeDRoofGuide guide) =>
        $"{NormalizePathKey(guide.PageFolder)}|{BaseLabel(guide.Label)}";

    private static string RegionLabel(IReadOnlyList<ThreeDRoofGuide> guides) =>
        BaseLabel(guides.FirstOrDefault()?.Label ?? "roof base");

    private static string BaseLabel(string label)
    {
        string clean = (label ?? "").Trim();
        Match match = EdgeLabelRegex().Match(clean);
        return match.Success ? match.Groups["base"].Value.Trim() : clean;
    }

    private static bool ResetToRake(ThreeDRoofGuide guide)
    {
        bool changed = ThreeDRoofGuideKinds.Normalize(guide.Kind) != ThreeDRoofGuideKinds.Rake ||
                       Math.Abs(guide.PitchRisePerFoot) > 0.0001 ||
                       string.Equals(guide.AdjustmentStatus, AutoAdjustmentStatus, StringComparison.OrdinalIgnoreCase);

        guide.Kind = ThreeDRoofGuideKinds.Rake;
        guide.Color = ThreeDRoofGuideKinds.Color(ThreeDRoofGuideKinds.Rake);
        guide.PitchRisePerFoot = 0;
        if (string.Equals(guide.AdjustmentStatus, AutoAdjustmentStatus, StringComparison.OrdinalIgnoreCase))
        {
            guide.AdjustmentStatus = "";
            guide.AdjustmentMessage = "";
            guide.Label = RemoveAutoPrefix(guide.Label);
        }

        return changed;
    }

    private static IReadOnlyList<RoofEdgeCandidate> SelectAutoEaves(IReadOnlyList<RoofEdgeCandidate> candidates)
    {
        RoofEdgePair? best = null;
        for (int i = 0; i < candidates.Count; i++)
        for (int j = i + 1; j < candidates.Count; j++)
        {
            RoofEdgeCandidate first = candidates[i];
            RoofEdgeCandidate second = candidates[j];
            double parallel = Math.Abs(Cross(first.UnitX, first.UnitZ, second.UnitX, second.UnitZ));
            if (parallel > ParallelCrossTolerance)
                continue;

            double separation = EdgeSeparation(first, second);
            if (separation < MinimumOppositeSeparationFeet)
                continue;

            double score = Math.Min(first.LengthFeet, second.LengthFeet) * 10.0 +
                           Math.Max(first.LengthFeet, second.LengthFeet) +
                           separation -
                           parallel;
            if (best == null || score > best.Score)
                best = new RoofEdgePair(first, second, score);
        }

        if (best != null)
            return [best.First, best.Second];

        return [candidates.OrderByDescending(candidate => candidate.LengthFeet).First()];
    }

    private static double EdgeSeparation(RoofEdgeCandidate first, RoofEdgeCandidate second)
    {
        double normalX = -first.UnitZ;
        double normalZ = first.UnitX;
        double dx = second.MidX - first.MidX;
        double dz = second.MidZ - first.MidZ;
        return Math.Abs(dx * normalX + dz * normalZ);
    }

    private static void MarkAsAutoEave(ThreeDRoofGuide guide, double pitchRisePerFoot)
    {
        guide.Kind = ThreeDRoofGuideKinds.Eave;
        guide.Color = ThreeDRoofGuideKinds.Color(ThreeDRoofGuideKinds.Eave);
        guide.PitchRisePerFoot = pitchRisePerFoot;
        guide.AdjustmentStatus = AutoAdjustmentStatus;
        guide.AdjustmentMessage = $"Auto Roof selected this boundary edge as a slope-defining eave at {PitchLabel(pitchRisePerFoot)}.";
        if (!guide.Label.StartsWith("Auto Eave - ", StringComparison.OrdinalIgnoreCase))
            guide.Label = $"Auto Eave - {RemoveAutoPrefix(guide.Label)}";
    }

    private static RoofEdgeCandidate ToCandidate(ThreeDRoofGuide guide)
    {
        ThreeDRoofGuidePoint start = guide.Points[0];
        ThreeDRoofGuidePoint end = guide.Points[^1];
        double dx = end.XFeet - start.XFeet;
        double dz = end.ZFeet - start.ZFeet;
        double length = Math.Sqrt(dx * dx + dz * dz);
        double unitX = length > 0.000001 ? dx / length : 1;
        double unitZ = length > 0.000001 ? dz / length : 0;
        return new RoofEdgeCandidate(
            guide,
            length,
            unitX,
            unitZ,
            (start.XFeet + end.XFeet) / 2.0,
            (start.ZFeet + end.ZFeet) / 2.0);
    }

    private static double Cross(double ax, double az, double bx, double bz) =>
        ax * bz - az * bx;

    private static string RemoveAutoPrefix(string label)
    {
        string clean = label ?? "";
        return clean.StartsWith("Auto Eave - ", StringComparison.OrdinalIgnoreCase)
            ? clean["Auto Eave - ".Length..].Trim()
            : clean;
    }

    private static string PitchLabel(double pitchRisePerFoot) =>
        $"{pitchRisePerFoot * 12.0:F1}/12";

    private static string NormalizePathKey(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim();
        }
    }

    [GeneratedRegex(@"^(?<base>.+?)\s+edge\s+\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EdgeLabelRegex();

    private sealed record RoofEdgeCandidate(
        ThreeDRoofGuide Guide,
        double LengthFeet,
        double UnitX,
        double UnitZ,
        double MidX,
        double MidZ);

    private sealed record RoofEdgePair(RoofEdgeCandidate First, RoofEdgeCandidate Second, double Score);
}
