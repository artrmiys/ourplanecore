using OurPlaneCore;
using SkiaSharp;

internal static class SheetOverlayAutoFitServiceTests
{
    public static void RecoversScaleAndOffsetFromRepeatedPlanGeometry()
    {
        PdfGeometrySnapResult overlay = BuildPlanSnap(scale: 1.0f, offsetX: 0, offsetY: 0);
        PdfGeometrySnapResult target = BuildPlanSnap(scale: 1.25f, offsetX: 42, offsetY: 67);

        bool ok = SheetOverlayAutoFitService.TryFit(target, overlay, out SheetOverlayAutoFitResult result);

        AssertTrue(ok, result.Message);
        AssertClose(1.25, result.OverlayScale, "overlay fit scale", 0.01);
        AssertClose(42, result.OffsetXPt, "overlay fit x offset", 1.0);
        AssertClose(67, result.OffsetYPt, "overlay fit y offset", 1.0);
        AssertClose(0, result.OverlayRotationDegrees, "overlay fit rotation", 0.25);
        AssertTrue(result.MatchedSamples >= 12, "overlay fit should verify against several geometry samples");
    }

    public static void RecoversRotationFromRepeatedPlanGeometry()
    {
        PdfGeometrySnapResult overlay = BuildPlanSnap(scale: 1.0f, offsetX: 0, offsetY: 0);
        PdfGeometrySnapResult target = BuildPlanSnap(scale: 0.85f, offsetX: 64, offsetY: 31, rotationDegrees: 7.5f);

        bool ok = SheetOverlayAutoFitService.TryFit(target, overlay, out SheetOverlayAutoFitResult result);

        AssertTrue(ok, result.Message);
        AssertClose(0.85, result.OverlayScale, "overlay fit rotated scale", 0.01);
        AssertClose(64, result.OffsetXPt, "overlay fit rotated x offset", 1.0);
        AssertClose(31, result.OffsetYPt, "overlay fit rotated y offset", 1.0);
        AssertClose(7.5, result.OverlayRotationDegrees, "overlay fit rotated angle", 0.3);
        AssertTrue(result.MatchedSamples >= 12, "rotated overlay fit should verify against several geometry samples");
    }

    public static void RecoversRotationFromJunctionPointPairsWithoutSegments()
    {
        PdfGeometrySnapResult overlay = BuildPointOnlyPlanSnap(scale: 1.0f, offsetX: 0, offsetY: 0);
        PdfGeometrySnapResult target = BuildPointOnlyPlanSnap(scale: 0.92f, offsetX: 58, offsetY: 44, rotationDegrees: 11f);

        bool ok = SheetOverlayAutoFitService.TryFit(target, overlay, out SheetOverlayAutoFitResult result);

        AssertTrue(ok, result.Message);
        AssertClose(0.92, result.OverlayScale, "overlay fit point-pair scale", 0.02);
        AssertClose(58, result.OffsetXPt, "overlay fit point-pair x offset", 1.5);
        AssertClose(44, result.OffsetYPt, "overlay fit point-pair y offset", 1.5);
        AssertClose(11, result.OverlayRotationDegrees, "overlay fit point-pair angle", 0.4);
        AssertTrue(result.MatchedSamples >= 12, "point-pair overlay fit should verify against several junction samples");
    }

    public static void RejectsSparseGeometry()
    {
        var target = new PdfGeometrySnapResult
        {
            Segments =
            [
                Segment(0, 0, 100, 0),
                Segment(0, 20, 100, 20),
            ],
        };
        var overlay = new PdfGeometrySnapResult
        {
            Segments =
            [
                Segment(0, 0, 80, 0),
                Segment(0, 20, 80, 20),
            ],
        };

        bool ok = SheetOverlayAutoFitService.TryFit(target, overlay, out _);

        AssertFalse(ok, "overlay auto fit should not apply a transform from sparse geometry");
    }

    private static PdfGeometrySnapResult BuildPlanSnap(
        float scale,
        float offsetX,
        float offsetY,
        float rotationDegrees = 0)
    {
        var raw = new List<PdfGeometrySnapSegment>
        {
            Segment(0, 0, 220, 0),
            Segment(220, 0, 220, 120),
            Segment(220, 120, 0, 120),
            Segment(0, 120, 0, 0),
            Segment(40, 0, 40, 120),
            Segment(95, 0, 95, 120),
            Segment(150, 0, 150, 120),
            Segment(0, 55, 220, 55),
            Segment(0, 88, 220, 88),
            Segment(95, 30, 150, 30),
            Segment(95, 90, 150, 90),
        };

        List<PdfGeometrySnapSegment> segments = raw
            .Select(segment => Transform(segment, scale, offsetX, offsetY, rotationDegrees))
            .ToList();
        List<PdfGeometrySnapPoint> points = segments
            .SelectMany(segment => new[] { segment.Start, segment.End })
            .DistinctBy(point => $"{point.X:0.###}|{point.Y:0.###}")
            .Select(point => new PdfGeometrySnapPoint(point, "pdf-corner"))
            .ToList();

        return new PdfGeometrySnapResult
        {
            Points = points,
            Segments = segments,
        };
    }

    private static PdfGeometrySnapResult BuildPointOnlyPlanSnap(
        float scale,
        float offsetX,
        float offsetY,
        float rotationDegrees = 0)
    {
        List<PdfGeometrySnapPoint> points = RawPlanJunctions()
            .Select(point => new PdfGeometrySnapPoint(
                Transform(point, scale, offsetX, offsetY, rotationDegrees),
                "raster-junction"))
            .ToList();

        return new PdfGeometrySnapResult
        {
            Points = points,
            Segments = [],
        };
    }

    private static List<SKPoint> RawPlanJunctions() =>
    [
        new SKPoint(0, 0),
        new SKPoint(220, 0),
        new SKPoint(220, 120),
        new SKPoint(0, 120),
        new SKPoint(40, 0),
        new SKPoint(40, 55),
        new SKPoint(40, 120),
        new SKPoint(95, 0),
        new SKPoint(95, 55),
        new SKPoint(95, 88),
        new SKPoint(95, 120),
        new SKPoint(150, 0),
        new SKPoint(150, 30),
        new SKPoint(150, 88),
        new SKPoint(150, 120),
        new SKPoint(185, 30),
        new SKPoint(205, 74),
        new SKPoint(220, 55),
        new SKPoint(220, 88),
        new SKPoint(0, 55),
        new SKPoint(0, 88),
    ];

    private static PdfGeometrySnapSegment Transform(
        PdfGeometrySnapSegment segment,
        float scale,
        float offsetX,
        float offsetY,
        float rotationDegrees) =>
        Segment(
            Transform(segment.Start, scale, offsetX, offsetY, rotationDegrees),
            Transform(segment.End, scale, offsetX, offsetY, rotationDegrees));

    private static SKPoint Transform(
        SKPoint point,
        float scale,
        float offsetX,
        float offsetY,
        float rotationDegrees)
    {
        float radians = rotationDegrees * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        float x = point.X * scale;
        float y = point.Y * scale;
        return new SKPoint(
            offsetX + x * cos - y * sin,
            offsetY + x * sin + y * cos);
    }

    private static PdfGeometrySnapSegment Segment(float x0, float y0, float x1, float y1) =>
        new(new SKPoint(x0, y0), new SKPoint(x1, y1), "pdf-line");

    private static PdfGeometrySnapSegment Segment(SKPoint start, SKPoint end) =>
        new(start, end, "pdf-line");

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message) =>
        AssertTrue(!condition, message);

    private static void AssertClose(double expected, double actual, string message, double tolerance)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }
}
