using OurPlaneCore;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public static class AnnotationGlyphRenderer
{
    public static void DrawArrowHead(SKCanvas canvas, SKPoint start, SKPoint end, SKPaint paint, float size)
    {
        float length = MeasurementGeometry.Distance(start, end);
        if (length <= 0.001f)
            return;

        float ux = (end.X - start.X) / length;
        float uy = (end.Y - start.Y) / length;
        float px = -uy;
        float py = ux;
        SKPoint left = new(end.X - ux * size + px * size * 0.45f, end.Y - uy * size + py * size * 0.45f);
        SKPoint right = new(end.X - ux * size - px * size * 0.45f, end.Y - uy * size - py * size * 0.45f);
        canvas.DrawLine(end, left, paint);
        canvas.DrawLine(end, right, paint);
    }

    public static void DrawDimensionTicks(SKCanvas canvas, SKPoint start, SKPoint end, SKPaint paint, float tickSize)
    {
        float length = MeasurementGeometry.Distance(start, end);
        if (length <= 0.001f)
            return;

        float px = -(end.Y - start.Y) / length;
        float py = (end.X - start.X) / length;
        canvas.DrawLine(start.X - px * tickSize, start.Y - py * tickSize, start.X + px * tickSize, start.Y + py * tickSize, paint);
        canvas.DrawLine(end.X - px * tickSize, end.Y - py * tickSize, end.X + px * tickSize, end.Y + py * tickSize, paint);
    }

    public static string FormatLength(float pdfPoints, float scaleMetersPerPt, UnitMode units) =>
        scaleMetersPerPt > 0
            ? Units.FormatLength(pdfPoints * scaleMetersPerPt, units)
            : $"{pdfPoints:F1} pt";
}
