using SkiaSharp;

namespace OurPlaneCore;

public sealed record PointAlongLineOptions(
    double SpacingInches,
    bool IncludeEndPoint = true);

public sealed record PointAlongLineResult(
    IReadOnlyList<SKPoint> Points,
    double TotalLengthMeters);

public static class PointAlongLineService
{
    private const float MinSegmentLengthPt = 0.001f;
    private const double InchesToMeters = 0.0254;

    public static PointAlongLineResult Generate(
        Measurement line,
        double fallbackScaleMetersPerPt,
        PointAlongLineOptions options)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (OurPlaneCoreJobStore.NormalizeMeasurementType(line.MType) != "line" || line.Points.Count < 2)
            throw new ArgumentException("Point spacing requires a Line measurement with at least two points.", nameof(line));
        if (!double.IsFinite(options.SpacingInches) || options.SpacingInches <= 0)
            throw new ArgumentException("Point spacing must be a positive number of inches.", nameof(options));

        double scaleMetersPerPt = line.ScaleMetersPerPt > 0
            ? line.ScaleMetersPerPt
            : fallbackScaleMetersPerPt;
        if (!double.IsFinite(scaleMetersPerPt) || scaleMetersPerPt <= 0)
            throw new InvalidOperationException("Set the sheet scale before creating Count points along a line.");

        List<LineSegment> segments = BuildSegments(line.Points);
        double totalLengthPt = segments.Sum(segment => segment.LengthPt);
        if (segments.Count == 0 || totalLengthPt <= MinSegmentLengthPt)
            throw new InvalidOperationException("The selected line is too short for point spacing.");

        double spacingPt = options.SpacingInches * InchesToMeters / scaleMetersPerPt;
        if (!double.IsFinite(spacingPt) || spacingPt <= MinSegmentLengthPt)
            throw new ArgumentException("Point spacing is too small for the current sheet scale.", nameof(options));

        var points = new List<SKPoint>();
        for (double distancePt = 0; distancePt < totalLengthPt - MinSegmentLengthPt; distancePt += spacingPt)
            AddDistinct(points, PointAtDistance(segments, distancePt));

        if (options.IncludeEndPoint)
            AddDistinct(points, segments[^1].End);

        return new PointAlongLineResult(points, totalLengthPt * scaleMetersPerPt);
    }

    private static List<LineSegment> BuildSegments(IReadOnlyList<SKPoint> points)
    {
        var segments = new List<LineSegment>();
        for (int i = 1; i < points.Count; i++)
        {
            SKPoint start = points[i - 1];
            SKPoint end = points[i];
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length > MinSegmentLengthPt)
                segments.Add(new LineSegment(start, end, length));
        }

        return segments;
    }

    private static SKPoint PointAtDistance(IReadOnlyList<LineSegment> segments, double distancePt)
    {
        double remaining = Math.Max(0, distancePt);
        foreach (LineSegment segment in segments)
        {
            if (remaining <= segment.LengthPt)
            {
                double t = segment.LengthPt <= MinSegmentLengthPt
                    ? 0
                    : remaining / segment.LengthPt;
                return new SKPoint(
                    (float)(segment.Start.X + (segment.End.X - segment.Start.X) * t),
                    (float)(segment.Start.Y + (segment.End.Y - segment.Start.Y) * t));
            }

            remaining -= segment.LengthPt;
        }

        return segments[^1].End;
    }

    private static void AddDistinct(List<SKPoint> points, SKPoint point)
    {
        if (points.Count == 0 || Distance(points[^1], point) > MinSegmentLengthPt)
            points.Add(point);
    }

    private static double Distance(SKPoint a, SKPoint b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private readonly record struct LineSegment(SKPoint Start, SKPoint End, double LengthPt);
}
