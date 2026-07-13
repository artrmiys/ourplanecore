using System.IO;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace OurPlanCore;

public sealed class ThreeDRoofFootprintSource
{
    public TakeoffItem? Item { get; init; }
    public Measurement Measurement { get; init; } = new();
}

public sealed class ThreeDRoofFootprintBuildResult
{
    public List<ThreeDFloorSlab> Slabs { get; } = [];
    public List<ThreeDRoofGuide> Guides { get; } = [];
    public int SourceAreaCount { get; set; }
    public int SkippedNonAreaMeasurements { get; set; }
    public int SkippedNoScaleMeasurements { get; set; }
    public int SkippedShortAreas { get; set; }
}

public static class ThreeDRoofFootprintBuildService
{
    public const string GeneratedStatus = "rf_footprint";
    public const string GeneratedSlabGroupPrefix = "roof_footprint|";
    private const double MetersPerFoot = 0.3048;

    public static ThreeDRoofFootprintBuildResult Build(
        IEnumerable<ThreeDRoofFootprintSource> sources,
        Func<Measurement, double> scaleResolver,
        double elevationFeet,
        double pitchRisePerFoot)
    {
        var result = new ThreeDRoofFootprintBuildResult();
        var areas = new List<ScaledRoofArea>();
        int index = 1;
        foreach (ThreeDRoofFootprintSource source in sources)
        {
            Measurement measurement = source.Measurement;
            if (OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType) != "area")
            {
                result.SkippedNonAreaMeasurements++;
                continue;
            }

            if (measurement.Points.Count < 3)
            {
                result.SkippedShortAreas++;
                continue;
            }

            double scaleMetersPerPt = scaleResolver(measurement);
            if (scaleMetersPerPt <= 0)
            {
                result.SkippedNoScaleMeasurements++;
                continue;
            }

            result.SourceAreaCount++;
            double feetPerPt = scaleMetersPerPt / MetersPerFoot;
            string label = SourceLabel(source, index);
            string color = SourceColor(source);
            string sourcePath = source.Item?.FolderPath ?? measurement.TakeoffFolder;
            List<ThreeDPoint> slabPoints = measurement.Points
                .Select(point => new ThreeDPoint
                {
                    XFeet = point.X * feetPerPt,
                    ZFeet = point.Y * feetPerPt,
                })
                .ToList();

            areas.Add(new ScaledRoofArea(
                label,
                sourcePath,
                measurement.PageFolder,
                measurement.Id,
                color,
                feetPerPt,
                SanitizeRing(slabPoints)));
            index++;
        }

        foreach (IGrouping<string, ScaledRoofArea> pageGroup in areas.GroupBy(area => NormalizePathKey(area.PageFolder)))
        {
            List<ScaledRoofArea> pageAreas = pageGroup.ToList();
            IReadOnlyList<RoofBaseComponent> components = BuildRoofBaseComponents(pageAreas);
            for (int i = 0; i < components.Count; i++)
            {
                RoofBaseComponent component = components[i];
                string label = components.Count == 1 ? component.Label : $"{component.Label} {i + 1}";
                string groupKey = $"{GeneratedSlabGroupPrefix}{component.PageFolder}|{i + 1}";
                result.Slabs.Add(new ThreeDFloorSlab
                {
                    TakeoffName = component.Label,
                    TakeoffFolder = component.SourcePath,
                    MeasurementId = component.MeasurementId,
                    PageFolder = component.PageFolder,
                    ElevationFeet = elevationFeet,
                    ThicknessFeet = 0.08,
                    Label = label,
                    Color = component.Color,
                    GroupKey = groupKey,
                    LevelKey = "roof",
                    Points = component.Points.Select(ClonePoint).ToList(),
                });

                AddBoundaryGuides(
                    result.Guides,
                    component,
                    label,
                    component.SourcePath,
                    elevationFeet,
                    component.FeetPerPt);
            }
        }

        _ = pitchRisePerFoot;
        return result;
    }

    private static IReadOnlyList<RoofBaseComponent> BuildRoofBaseComponents(IReadOnlyList<ScaledRoofArea> areas)
    {
        if (areas.Count == 0)
            return [];

        ScaledRoofArea first = areas[0];
        if (areas.Count == 1)
        {
            return
            [
                new RoofBaseComponent(
                    first.Label,
                    first.SourcePath,
                    first.PageFolder,
                    first.MeasurementId,
                    first.Color,
                    first.FeetPerPt,
                    first.Points.Select(ClonePoint).ToList())
            ];
        }

        if (areas.All(area => IsOrthogonal(area.Points)) &&
            TryBuildOrthogonalUnion(areas, out List<List<ThreeDPoint>> unionLoops))
        {
            string label = CombinedLabel(areas);
            string sourcePath = CombinedSourcePath(areas);
            return unionLoops
                .Select(loop => new RoofBaseComponent(
                    label,
                    sourcePath,
                    first.PageFolder,
                    "",
                    first.Color,
                    first.FeetPerPt,
                    loop))
                .ToList();
        }

        return areas
            .Select(area => new RoofBaseComponent(
                area.Label,
                area.SourcePath,
                area.PageFolder,
                area.MeasurementId,
                area.Color,
                area.FeetPerPt,
                area.Points.Select(ClonePoint).ToList()))
            .ToList();
    }

    private static bool TryBuildOrthogonalUnion(
        IReadOnlyList<ScaledRoofArea> areas,
        out List<List<ThreeDPoint>> loops)
    {
        loops = [];
        List<double> xs = areas
            .SelectMany(area => area.Points.Select(point => point.XFeet))
            .DistinctByTolerance()
            .ToList();
        List<double> zs = areas
            .SelectMany(area => area.Points.Select(point => point.ZFeet))
            .DistinctByTolerance()
            .ToList();
        if (xs.Count < 2 || zs.Count < 2)
            return false;

        xs.Sort();
        zs.Sort();
        bool[,] occupied = new bool[xs.Count - 1, zs.Count - 1];
        for (int ix = 0; ix < xs.Count - 1; ix++)
        {
            for (int iz = 0; iz < zs.Count - 1; iz++)
            {
                var center = new P2((xs[ix] + xs[ix + 1]) / 2.0, (zs[iz] + zs[iz + 1]) / 2.0);
                occupied[ix, iz] = areas.Any(area => PointInPolygon(center, area.Points));
            }
        }

        var edges = new List<Edge>();
        for (int ix = 0; ix < xs.Count - 1; ix++)
        {
            for (int iz = 0; iz < zs.Count - 1; iz++)
            {
                if (!occupied[ix, iz])
                    continue;

                double x0 = xs[ix];
                double x1 = xs[ix + 1];
                double z0 = zs[iz];
                double z1 = zs[iz + 1];
                if (iz == 0 || !occupied[ix, iz - 1])
                    edges.Add(new Edge(new P2(x0, z0), new P2(x1, z0)));
                if (ix == xs.Count - 2 || !occupied[ix + 1, iz])
                    edges.Add(new Edge(new P2(x1, z0), new P2(x1, z1)));
                if (iz == zs.Count - 2 || !occupied[ix, iz + 1])
                    edges.Add(new Edge(new P2(x1, z1), new P2(x0, z1)));
                if (ix == 0 || !occupied[ix - 1, iz])
                    edges.Add(new Edge(new P2(x0, z1), new P2(x0, z0)));
            }
        }

        loops = BuildLoops(edges)
            .Select(CleanLoop)
            .Where(loop => loop.Count >= 3 && Math.Abs(SignedArea(loop)) > 1.0)
            .OrderByDescending(loop => Math.Abs(SignedArea(loop)))
            .ToList();
        if (loops.Count == 0)
            return false;

        for (int i = 0; i < loops.Count; i++)
        {
            if (SignedArea(loops[i]) < 0)
                loops[i].Reverse();
        }

        return true;
    }

    private static List<List<ThreeDPoint>> BuildLoops(List<Edge> edges)
    {
        var unused = new List<Edge>(edges);
        var loops = new List<List<ThreeDPoint>>();
        while (unused.Count > 0)
        {
            Edge first = unused[0];
            unused.RemoveAt(0);
            var loop = new List<P2> { first.Start, first.End };
            P2 current = first.End;

            while (unused.Count > 0 && Distance(current, loop[0]) > 0.001)
            {
                int nextIndex = unused.FindIndex(edge => Distance(edge.Start, current) <= 0.001);
                if (nextIndex < 0)
                    break;

                Edge next = unused[nextIndex];
                unused.RemoveAt(nextIndex);
                loop.Add(next.End);
                current = next.End;
            }

            if (loop.Count >= 4 && Distance(loop[0], loop[^1]) <= 0.001)
                loop.RemoveAt(loop.Count - 1);

            loops.Add(loop.Select(point => new ThreeDPoint { XFeet = point.X, ZFeet = point.Z }).ToList());
        }

        return loops;
    }

    private static List<ThreeDPoint> CleanLoop(IReadOnlyList<ThreeDPoint> points)
    {
        List<ThreeDPoint> clean = CleanPoints(points);
        bool changed;
        do
        {
            changed = false;
            for (int i = 0; i < clean.Count && clean.Count >= 3; i++)
            {
                ThreeDPoint previous = clean[(i - 1 + clean.Count) % clean.Count];
                ThreeDPoint current = clean[i];
                ThreeDPoint next = clean[(i + 1) % clean.Count];
                if (IsCollinear(previous, current, next))
                {
                    clean.RemoveAt(i);
                    changed = true;
                    break;
                }
            }
        } while (changed);

        return clean;
    }

    private static List<ThreeDPoint> CleanPoints(IEnumerable<ThreeDPoint> points)
    {
        var clean = new List<ThreeDPoint>();
        foreach (ThreeDPoint point in points)
        {
            if (clean.Count == 0 || Distance(clean[^1], point) > 0.05)
                clean.Add(ClonePoint(point));
        }

        if (clean.Count > 1 && Distance(clean[0], clean[^1]) <= 0.05)
            clean.RemoveAt(clean.Count - 1);

        return clean;
    }

    // A roughly-drawn roof area is often a self-touching scribble. A clean
    // roof is impossible from a non-simple ring, so fall back to its convex
    // hull - always a valid polygon that yields a sensible hip roof.
    private static List<ThreeDPoint> SanitizeRing(IReadOnlyList<ThreeDPoint> points)
    {
        List<ThreeDPoint> clean = CleanPoints(points);
        if (clean.Count < 4 || !RingSelfIntersects(clean))
            return clean;

        List<ThreeDPoint> hull = ConvexHull(clean);
        return hull.Count >= 3 ? hull : clean;
    }

    private static bool RingSelfIntersects(IReadOnlyList<ThreeDPoint> points)
    {
        int n = points.Count;
        for (int i = 0; i < n; i++)
        {
            ThreeDPoint a1 = points[i];
            ThreeDPoint a2 = points[(i + 1) % n];
            for (int j = i + 1; j < n; j++)
            {
                if (j == i || (j + 1) % n == i || (i + 1) % n == j)
                    continue;

                if (SegmentsCross(a1, a2, points[j], points[(j + 1) % n]))
                    return true;
            }
        }

        return false;
    }

    private static bool SegmentsCross(ThreeDPoint a, ThreeDPoint b, ThreeDPoint c, ThreeDPoint d)
    {
        double d1 = Orient(c, d, a);
        double d2 = Orient(c, d, b);
        double d3 = Orient(a, b, c);
        double d4 = Orient(a, b, d);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
               ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    private static double Orient(ThreeDPoint p, ThreeDPoint q, ThreeDPoint r) =>
        (q.XFeet - p.XFeet) * (r.ZFeet - p.ZFeet) -
        (q.ZFeet - p.ZFeet) * (r.XFeet - p.XFeet);

    private static List<ThreeDPoint> ConvexHull(IReadOnlyList<ThreeDPoint> points)
    {
        List<ThreeDPoint> sorted = points
            .OrderBy(p => p.XFeet)
            .ThenBy(p => p.ZFeet)
            .ToList();
        if (sorted.Count < 3)
            return sorted;

        var hull = new List<ThreeDPoint>();
        foreach (ThreeDPoint p in sorted) // lower
        {
            while (hull.Count >= 2 && Orient(hull[^2], hull[^1], p) <= 0)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }

        int lower = hull.Count + 1;
        for (int i = sorted.Count - 2; i >= 0; i--) // upper
        {
            ThreeDPoint p = sorted[i];
            while (hull.Count >= lower && Orient(hull[^2], hull[^1], p) <= 0)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }

        hull.RemoveAt(hull.Count - 1);
        return hull;
    }

    private static bool IsOrthogonal(IReadOnlyList<ThreeDPoint> points)
    {
        for (int i = 0; i < points.Count; i++)
        {
            ThreeDPoint a = points[i];
            ThreeDPoint b = points[(i + 1) % points.Count];
            if (Math.Abs(a.XFeet - b.XFeet) > 0.05 && Math.Abs(a.ZFeet - b.ZFeet) > 0.05)
                return false;
        }

        return true;
    }

    private static bool IsCollinear(ThreeDPoint a, ThreeDPoint b, ThreeDPoint c)
    {
        double abx = b.XFeet - a.XFeet;
        double abz = b.ZFeet - a.ZFeet;
        double bcx = c.XFeet - b.XFeet;
        double bcz = c.ZFeet - b.ZFeet;
        return Math.Abs(abx * bcz - abz * bcx) <= 0.001;
    }

    private static void AddBoundaryGuides(
        List<ThreeDRoofGuide> guides,
        RoofBaseComponent component,
        string label,
        string sourcePath,
        double elevationFeet,
        double feetPerPt)
    {
        IReadOnlyList<ThreeDPoint> points = component.Points;
        for (int i = 0; i < points.Count; i++)
        {
            int next = (i + 1) % points.Count;
            ThreeDPoint a = points[i];
            ThreeDPoint b = points[next];
            if (Distance(a, b) < 0.05)
                continue;

            var guide = new ThreeDRoofGuide
            {
                Kind = ThreeDRoofGuideKinds.Rake,
                Label = $"{label} edge {i + 1}",
                PageFolder = component.PageFolder,
                ElevationFeet = elevationFeet,
                LevelKey = "roof",
                DefinesSlope = false,
                PitchRisePerFoot = 0,
                Color = ThreeDRoofGuideKinds.Color(ThreeDRoofGuideKinds.Rake),
                Status = GeneratedStatus,
                Points =
                [
                    RoofPoint(a, feetPerPt),
                    RoofPoint(b, feetPerPt),
                ],
                RawPoints =
                [
                    RoofPoint(a, feetPerPt),
                    RoofPoint(b, feetPerPt),
                ],
            };
            guide.AdjustmentMessage = $"Generated from roof base {label}. Source: {sourcePath}";
            guides.Add(guide);
        }
    }

    public static IEnumerable<ThreeDRoofFootprintSource> SourcesFromItems(IEnumerable<TakeoffItem> items)
    {
        foreach (TakeoffItem item in items)
        foreach (Measurement measurement in item.Measurements)
        {
            yield return new ThreeDRoofFootprintSource
            {
                Item = item,
                Measurement = measurement,
            };
        }
    }

    public static bool IsAreaTakeoff(TakeoffItem item) =>
        OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "area";

    public static bool IsLineTakeoff(TakeoffItem item) =>
        OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "line";

    public static bool IsRoofFootprintCandidate(OurPlanCoreJob job, TakeoffItem item)
    {
        if (!IsAreaTakeoff(item))
            return false;

        bool underSqfts = SourceParts(job, item).Any(part =>
            Normalize(part) is "sqft" or "sqfts" or "sft" or "sf");
        string name = Normalize(item.Name);
        bool exactRoofName = name is "rf" or "roof" or "roof area" or "roof sqft" or "roof sq ft";
        bool roofInSqfts = underSqfts && Regex.IsMatch(name, @"\b(rf|roof)\b", RegexOptions.IgnoreCase);
        return exactRoofName || roofInSqfts;
    }

    private static ThreeDRoofGuidePoint RoofPoint(ThreeDPoint point, double feetPerPt) =>
        new()
        {
            XFeet = point.XFeet,
            ZFeet = point.ZFeet,
            PdfX = feetPerPt > 0 ? point.XFeet / feetPerPt : 0,
            PdfY = feetPerPt > 0 ? point.ZFeet / feetPerPt : 0,
        };

    private static string SourceLabel(ThreeDRoofFootprintSource source, int index)
    {
        if (!string.IsNullOrWhiteSpace(source.Item?.Name))
            return source.Item.Name.Trim();
        if (!string.IsNullOrWhiteSpace(source.Measurement.Name))
            return source.Measurement.Name.Trim();
        return $"RF area {index}";
    }

    private static string SourceColor(ThreeDRoofFootprintSource source) =>
        !string.IsNullOrWhiteSpace(source.Item?.Color)
            ? source.Item.Color
            : !string.IsNullOrWhiteSpace(source.Measurement.Color)
                ? source.Measurement.Color
                : "#D97706";

    private static string CombinedLabel(IReadOnlyList<ScaledRoofArea> areas) =>
        areas.Count == 1
            ? areas[0].Label
            : $"Roof Base ({areas.Count} areas)";

    private static string CombinedSourcePath(IReadOnlyList<ScaledRoofArea> areas) =>
        string.Join("|", areas.Select(area => area.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase));

    private static IEnumerable<string> SourceParts(OurPlanCoreJob job, TakeoffItem item)
    {
        if (string.IsNullOrWhiteSpace(item.FolderPath))
            yield break;

        string relative;
        try
        {
            relative = Path.GetRelativePath(job.TakeoffsRoot, item.FolderPath);
        }
        catch
        {
            yield break;
        }

        string current = job.TakeoffsRoot;
        foreach (string part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (string.IsNullOrWhiteSpace(part) || part == "." || part == "..")
                continue;

            current = Path.Combine(current, part);
            yield return OurPlanCoreJobStore.DisplayName(current);
        }
    }

    private static string Normalize(string value) =>
        (value ?? "").Trim().ToLowerInvariant();

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

    private static ThreeDPoint ClonePoint(ThreeDPoint point) =>
        new() { XFeet = point.XFeet, ZFeet = point.ZFeet };

    private static bool PointInPolygon(P2 point, IReadOnlyList<ThreeDPoint> polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            ThreeDPoint a = polygon[i];
            ThreeDPoint b = polygon[j];
            if (DistanceToSegment(point, new P2(a.XFeet, a.ZFeet), new P2(b.XFeet, b.ZFeet)) <= 0.05)
                return true;

            bool crosses = a.ZFeet > point.Z != b.ZFeet > point.Z &&
                           point.X < (b.XFeet - a.XFeet) * (point.Z - a.ZFeet) / ((b.ZFeet - a.ZFeet) + 0.0000001) + a.XFeet;
            if (crosses)
                inside = !inside;
        }

        return inside;
    }

    private static double SignedArea(IReadOnlyList<ThreeDPoint> points)
    {
        double area = 0;
        for (int i = 0; i < points.Count; i++)
        {
            ThreeDPoint a = points[i];
            ThreeDPoint b = points[(i + 1) % points.Count];
            area += a.XFeet * b.ZFeet - b.XFeet * a.ZFeet;
        }

        return area / 2.0;
    }

    private static double Distance(ThreeDPoint a, ThreeDPoint b)
    {
        double dx = b.XFeet - a.XFeet;
        double dz = b.ZFeet - a.ZFeet;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    private static double Distance(P2 a, P2 b)
    {
        double dx = b.X - a.X;
        double dz = b.Z - a.Z;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    private static double DistanceToSegment(P2 point, P2 a, P2 b)
    {
        double dx = b.X - a.X;
        double dz = b.Z - a.Z;
        double len2 = dx * dx + dz * dz;
        if (len2 <= 0.000001)
            return Distance(point, a);

        double t = ((point.X - a.X) * dx + (point.Z - a.Z) * dz) / len2;
        t = Math.Clamp(t, 0, 1);
        return Distance(point, new P2(a.X + dx * t, a.Z + dz * t));
    }

    private sealed record ScaledRoofArea(
        string Label,
        string SourcePath,
        string PageFolder,
        string MeasurementId,
        string Color,
        double FeetPerPt,
        List<ThreeDPoint> Points);

    private sealed record RoofBaseComponent(
        string Label,
        string SourcePath,
        string PageFolder,
        string MeasurementId,
        string Color,
        double FeetPerPt,
        List<ThreeDPoint> Points);

    private readonly record struct Edge(P2 Start, P2 End);

    private readonly record struct P2(double X, double Z);
}

file static class ThreeDRoofFootprintEnumerableExtensions
{
    public static IEnumerable<double> DistinctByTolerance(this IEnumerable<double> values)
    {
        var result = new List<double>();
        foreach (double value in values.OrderBy(value => value))
        {
            if (result.Count == 0 || Math.Abs(result[^1] - value) > 0.05)
                result.Add(value);
        }

        return result;
    }
}
