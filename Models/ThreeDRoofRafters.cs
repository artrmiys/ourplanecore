using SkiaSharp;

namespace OurPlaneCore;

// Rafter layout on 3D roof planes. Each enabled roof face gets parallel
// rafters running up the slope: direction and pitch come straight from the
// face's fitted plane (Y = a*X + b*Z + c), spacing is measured horizontally
// (o.c. along the level eave), lengths are slope-corrected and rounded to
// lumber lengths by the shared joist calculator. The member's depth extrudes
// below the plane (the plane is the TOP of the rafters), and walls trim to
// the rafter underside via a plumb drop on the roof surface.

// Picked faces persist as plan-space anchor points (face centroids) because
// generated plane ids change on every rebuild; an anchor re-resolves to
// whichever face covers it after regeneration.
public sealed class ThreeDRoofRafterFaceAnchor
{
    public double XFeet { get; set; }
    public double ZFeet { get; set; }
}

public sealed class ThreeDRoofRafterSettings
{
    public const string ModeOff = "off";
    public const string ModeAll = "all";
    public const string ModeFaces = "faces";

    public string RoofGroupId { get; set; } = "";
    public string Mode { get; set; } = ModeOff;
    public double SpacingInches { get; set; } = 16;
    public string MemberSize { get; set; } = "2x10";
    public string LengthRounding { get; set; } = JoistTakeoffCalculator.RoundingNearestEvenFoot;
    public List<ThreeDRoofRafterFaceAnchor> Faces { get; set; } = [];

    public bool IsActive =>
        !string.Equals(Mode, ModeOff, StringComparison.OrdinalIgnoreCase) &&
        (!string.Equals(Mode, ModeFaces, StringComparison.OrdinalIgnoreCase) || Faces.Count > 0);
}

public static class ThreeDRoofRafterMembers
{
    public const double WidthInches = 1.5;

    private static readonly (string Size, double DepthInches)[] Sizes =
    [
        ("2x6", 5.5),
        ("2x8", 7.25),
        ("2x10", 9.25),
        ("2x12", 11.25),
    ];

    public static IReadOnlyList<string> All { get; } = Sizes.Select(entry => entry.Size).ToList();

    public static double DepthInches(string? size)
    {
        string clean = (size ?? "").Trim();
        foreach ((string name, double depth) in Sizes)
            if (string.Equals(name, clean, StringComparison.OrdinalIgnoreCase))
                return depth;
        return 9.25;
    }
}

public sealed record ThreeDRoofRafterBar(
    double X1Feet, double Y1Feet, double Z1Feet,
    double X2Feet, double Y2Feet, double Z2Feet,
    double RawLengthFeet,
    double OrderLengthFeet);

public sealed record ThreeDRoofRafterPlaneLayout(
    string PlaneId,
    string RoofGroupId,
    IReadOnlyList<ThreeDRoofRafterBar> Bars,
    double PitchRisePerFoot,
    double PlaneA, double PlaneB, double PlaneC,
    double PlumbDropFeet);

public static class ThreeDRoofRafterService
{
    private const double MetersPerFoot = 0.3048;
    private const double MinSlopeRisePerFoot = 0.02;

    public static bool IsEnvelopeFace(ThreeDRoofPlane plane) =>
        string.Equals(plane.Kind, "roof_face_envelope", StringComparison.OrdinalIgnoreCase) &&
        plane.Points.Count >= 3;

    // Same fit as ThreeDRoofSurface: first non-degenerate vertex triple.
    public static bool TryFitPlane(IReadOnlyList<ThreeDRoofVertex> points, out double a, out double b, out double c)
    {
        a = b = c = 0;
        if (points.Count < 3)
            return false;

        ThreeDRoofVertex p0 = points[0];
        for (int i = 1; i < points.Count - 1; i++)
        {
            ThreeDRoofVertex p1 = points[i];
            ThreeDRoofVertex p2 = points[i + 1];
            double ux = p1.XFeet - p0.XFeet, uy = p1.YFeet - p0.YFeet, uz = p1.ZFeet - p0.ZFeet;
            double vx = p2.XFeet - p0.XFeet, vy = p2.YFeet - p0.YFeet, vz = p2.ZFeet - p0.ZFeet;
            double nx = uy * vz - uz * vy;
            double ny = uz * vx - ux * vz;
            double nz = ux * vy - uy * vx;
            if (Math.Abs(ny) < 1e-6)
                continue;

            double d = nx * p0.XFeet + ny * p0.YFeet + nz * p0.ZFeet;
            a = -nx / ny;
            b = -nz / ny;
            c = d / ny;
            return true;
        }

        return false;
    }

    public static ThreeDRoofRafterPlaneLayout? ComputeForPlane(ThreeDRoofPlane plane, ThreeDRoofRafterSettings settings)
    {
        if (!IsEnvelopeFace(plane) || !TryFitPlane(plane.Points, out double a, out double b, out double c))
            return null;

        double slope = Math.Sqrt(a * a + b * b);
        if (slope < MinSlopeRisePerFoot)
            return null;

        // Rafters run along the steepest ascent (eave -> ridge); spacing steps
        // perpendicular to that, i.e. horizontally along the level eave line.
        double directionDegrees = Math.Atan2(b, a) * 180.0 / Math.PI;
        var polygon = plane.Points
            .Select(p => new SKPoint((float)p.XFeet, (float)p.ZFeet))
            .ToList();
        string pitch = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{slope * 12.0:0.###}:12");

        JoistLayoutResult layout = JoistTakeoffCalculator.Calculate(
            polygon,
            [],
            MetersPerFoot,
            settings.SpacingInches,
            directionDegrees,
            settings.LengthRounding,
            pitch,
            addEndJoist: true);
        if (!layout.HasScale || layout.Count == 0)
            return null;

        var bars = new List<ThreeDRoofRafterBar>(layout.Segments.Count);
        foreach (JoistSegment segment in layout.Segments)
        {
            double x1 = segment.Start.X, z1 = segment.Start.Y;
            double x2 = segment.End.X, z2 = segment.End.Y;
            bars.Add(new ThreeDRoofRafterBar(
                x1, a * x1 + b * z1 + c, z1,
                x2, a * x2 + b * z2 + c, z2,
                segment.RawLengthMeters / MetersPerFoot,
                segment.OrderLengthFeet));
        }

        // Perpendicular member depth projected to a vertical (plumb) drop:
        // plumb = depth / cos(slope angle) = depth * sqrt(1 + slope^2).
        double depthFeet = ThreeDRoofRafterMembers.DepthInches(settings.MemberSize) / 12.0;
        double plumbDrop = depthFeet * Math.Sqrt(1.0 + slope * slope);

        return new ThreeDRoofRafterPlaneLayout(
            plane.Id,
            plane.RoofGroupId,
            bars,
            slope,
            a, b, c,
            plumbDrop);
    }

    public static IReadOnlyList<ThreeDRoofPlane> ResolvePlanes(
        ThreeDRoofRafterSettings settings,
        IEnumerable<ThreeDRoofPlane> planes)
    {
        var groupPlanes = planes
            .Where(IsEnvelopeFace)
            .Where(plane => string.Equals(plane.RoofGroupId, settings.RoofGroupId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (string.Equals(settings.Mode, ThreeDRoofRafterSettings.ModeAll, StringComparison.OrdinalIgnoreCase))
            return groupPlanes;
        if (!string.Equals(settings.Mode, ThreeDRoofRafterSettings.ModeFaces, StringComparison.OrdinalIgnoreCase))
            return [];

        return groupPlanes
            .Where(plane => settings.Faces.Any(anchor => PlanContains(plane, anchor.XFeet, anchor.ZFeet)))
            .ToList();
    }

    public static (double XFeet, double ZFeet) PlanCentroid(ThreeDRoofPlane plane)
    {
        double x = 0, z = 0;
        foreach (ThreeDRoofVertex vertex in plane.Points)
        {
            x += vertex.XFeet;
            z += vertex.ZFeet;
        }

        int count = Math.Max(1, plane.Points.Count);
        return (x / count, z / count);
    }

    public static bool PlanContains(ThreeDRoofPlane plane, double xFeet, double zFeet)
    {
        var points = plane.Points;
        bool inside = false;
        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
        {
            double xi = points[i].XFeet, zi = points[i].ZFeet;
            double xj = points[j].XFeet, zj = points[j].ZFeet;
            bool crosses = zi > zFeet != zj > zFeet &&
                           xFeet < (xj - xi) * (zFeet - zi) / (zj - zi + 1e-12) + xi;
            if (crosses)
                inside = !inside;
        }

        return inside;
    }

    public static string FormatSummary(
        IReadOnlyList<ThreeDRoofRafterPlaneLayout> layouts,
        ThreeDRoofRafterSettings settings,
        UnitMode unitMode)
    {
        var bars = layouts.SelectMany(layout => layout.Bars).ToList();
        if (bars.Count == 0)
            return "Rafters: none (flat or no faces).";

        double totalOrderFeet = bars.Sum(bar => bar.OrderLengthFeet);
        string total = Units.FormatLength(totalOrderFeet * MetersPerFoot, unitMode);
        var groups = bars
            .GroupBy(bar => bar.OrderLengthFeet)
            .OrderByDescending(group => group.Key)
            .Select(group => unitMode == UnitMode.Imperial
                ? $"{group.Count()} @ {group.Key:0.#} ft"
                : $"{group.Count()} @ {Units.FormatLength(group.Key * MetersPerFoot, unitMode)}")
            .ToList();
        string breakdown = string.Join(", ", groups.Take(8));
        if (groups.Count > 8)
            breakdown += $", +{groups.Count - 8} more";

        return $"Rafters {settings.MemberSize} @{settings.SpacingInches:0.#}\" o.c.: " +
               $"{bars.Count} pcs, {total} ({layouts.Count} face(s)) — {breakdown}";
    }
}
