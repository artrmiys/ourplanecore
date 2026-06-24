using OurPlaneCore;
using SkiaSharp;

internal static class SheetOverlayRasterFeatureServiceTests
{
    public static void RasterFeaturesRecoverScaleAndOffset()
    {
        using SKBitmap overlayBitmap = BuildPlanBitmap(280, 190, scale: 1.0f, offsetX: 0, offsetY: 0);
        using SKBitmap targetBitmap = BuildPlanBitmap(430, 320, scale: 1.25f, offsetX: 42, offsetY: 67);

        bool overlayOk = SheetOverlayRasterFeatureService.TryExtractSnap(
            overlayBitmap,
            overlayBitmap.Width,
            overlayBitmap.Height,
            out PdfGeometrySnapResult overlaySnap,
            out string overlayError);
        bool targetOk = SheetOverlayRasterFeatureService.TryExtractSnap(
            targetBitmap,
            targetBitmap.Width,
            targetBitmap.Height,
            out PdfGeometrySnapResult targetSnap,
            out string targetError);
        bool fitOk = SheetOverlayAutoFitService.TryFit(targetSnap, overlaySnap, out SheetOverlayAutoFitResult fit);

        AssertTrue(overlayOk, overlayError);
        AssertTrue(targetOk, targetError);
        AssertTrue(overlaySnap.Segments.Count >= 12, "overlay raster should expose enough line segments");
        AssertTrue(targetSnap.Segments.Count >= 12, "target raster should expose enough line segments");
        AssertTrue(fitOk, fit.Message);
        AssertClose(1.25, fit.OverlayScale, "raster fit scale", 0.03);
        AssertClose(42, fit.OffsetXPt, "raster fit x", 4.0);
        AssertClose(67, fit.OffsetYPt, "raster fit y", 4.0);
        AssertTrue(fit.MatchedSamples >= 12, "raster fit should verify several samples");
    }

    private static SKBitmap BuildPlanBitmap(int width, int height, float scale, float offsetX, float offsetY)
    {
        var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = false,
            StrokeWidth = 2,
            Style = SKPaintStyle.Stroke,
        };

        foreach ((float X0, float Y0, float X1, float Y1) line in PlanLines())
        {
            canvas.DrawLine(
                Transform(line.X0, scale, offsetX),
                Transform(line.Y0, scale, offsetY),
                Transform(line.X1, scale, offsetX),
                Transform(line.Y1, scale, offsetY),
                paint);
        }

        return bitmap;
    }

    private static IReadOnlyList<(float X0, float Y0, float X1, float Y1)> PlanLines() =>
    [
        (0, 0, 220, 0),
        (220, 0, 220, 120),
        (220, 120, 0, 120),
        (0, 120, 0, 0),
        (40, 0, 40, 120),
        (95, 0, 95, 120),
        (150, 0, 150, 120),
        (0, 55, 220, 55),
        (0, 88, 220, 88),
        (95, 30, 150, 30),
        (95, 90, 150, 90),
        (150, 18, 205, 18),
        (150, 102, 205, 102),
    ];

    private static float Transform(float value, float scale, float offset) =>
        value * scale + offset;

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertClose(double expected, double actual, string message, double tolerance)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }
}
