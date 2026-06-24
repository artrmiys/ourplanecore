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
        AssertTrue(result.MatchedSamples >= 12, "overlay fit should verify against several geometry samples");
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

    private static PdfGeometrySnapResult BuildPlanSnap(float scale, float offsetX, float offsetY)
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
            .Select(segment => Transform(segment, scale, offsetX, offsetY))
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

    private static PdfGeometrySnapSegment Transform(
        PdfGeometrySnapSegment segment,
        float scale,
        float offsetX,
        float offsetY) =>
        Segment(
            offsetX + segment.Start.X * scale,
            offsetY + segment.Start.Y * scale,
            offsetX + segment.End.X * scale,
            offsetY + segment.End.Y * scale);

    private static PdfGeometrySnapSegment Segment(float x0, float y0, float x1, float y1) =>
        new(new SKPoint(x0, y0), new SKPoint(x1, y1), "pdf-line");

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
