using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Docnet.Core;
using Docnet.Core.Models;
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

public static partial class PdfExporter
{
    private static void DrawAnnotations(
        SKCanvas canvas,
        IReadOnlyList<PageAnnotation> annotations,
        double pageScaleMetersPerPt,
        PdfExportOptions options)
    {
        foreach (PageAnnotation annotation in annotations)
        {
            string kind = OurPlaneCoreJobStore.NormalizePageAnnotationKind(annotation.Kind);
            if (annotation.Points.Count < 2)
                continue;

            SKColor color = ParseSkColor(annotation.Color, new SKColor(0x15, 0x65, 0xC0));
            using var stroke = new SKPaint
            {
                IsAntialias = true,
                Color = color,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = ExportAnnotationStrokeWidth(annotation),
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
            };

            SKPoint start = annotation.Points[0];
            SKPoint end = annotation.Points[1];
            if (kind == "note")
            {
                DrawNoteAnnotation(canvas, annotation, color, options);
                continue;
            }

            if (kind == "rectangle")
            {
                using SKPath path = BuildClosedAnnotationPath(AnnotationRectangleCorners(annotation.Points));
                canvas.DrawPath(path, stroke);
                continue;
            }

            if (kind == "cloud")
            {
                DrawCloudAnnotation(canvas, annotation, stroke);
                continue;
            }

            if (kind == "area")
            {
                if (annotation.Points.Count < 3)
                    continue;

                using var fill = new SKPaint
                {
                    IsAntialias = true,
                    Color = color.WithAlpha(55),
                    Style = SKPaintStyle.Fill,
                };
                using SKPath path = BuildClosedAnnotationPath(annotation.Points);
                canvas.DrawPath(path, fill);
                canvas.DrawPath(path, stroke);
                continue;
            }

            canvas.DrawLine(start, end, stroke);
            if (kind == "arrow")
            {
                AnnotationGlyphRenderer.DrawArrowHead(canvas, start, end, stroke, 8.0f);
                continue;
            }

            if (kind == "dimension")
            {
                AnnotationGlyphRenderer.DrawDimensionTicks(canvas, start, end, stroke, 5.5f);
                double scale = annotation.ScaleMetersPerPt > 0
                    ? annotation.ScaleMetersPerPt
                    : pageScaleMetersPerPt;
                string label = string.IsNullOrWhiteSpace(annotation.Text)
                    ? FormatAnnotationLength(start, end, scale, options.UnitMode)
                    : annotation.Text;
                DrawMeasurementLabel(
                    canvas,
                    new SKPoint((start.X + end.X) / 2f, (start.Y + end.Y) / 2f),
                    label,
                    color,
                    centered: true,
                    ExportLabelScale(options));
            }
        }
    }

    private static float ExportAnnotationStrokeWidth(PageAnnotation annotation)
    {
        double value = annotation.StrokeWidth is >= 0.75 and <= 12.0
            ? annotation.StrokeWidth
            : 1.8;
        return (float)Math.Clamp(value * 0.75, 0.75, 9.0);
    }

    private static void DrawCloudAnnotation(SKCanvas canvas, PageAnnotation annotation, SKPaint stroke)
    {
        IReadOnlyList<SKPoint> corners = AnnotationRectangleCorners(annotation.Points);
        if (corners.Count < 4 ||
            !TryGetAnnotationLocalFrame(corners, out SKMatrix localToPdf, out float width, out float height))
        {
            return;
        }

        using var saved = new SKAutoCanvasRestore(canvas, true);
        canvas.Concat(ref localToPdf);
        using SKPath path = BuildCloudPath(width, height);
        canvas.DrawPath(path, stroke);
    }

    private static SKPath BuildCloudPath(float width, float height)
    {
        var path = new SKPath();
        if (width <= 0 || height <= 0)
            return path;

        float bump = Math.Clamp(Math.Min(width, height) / 7f, 5f, Math.Max(5f, Math.Min(width, height) / 3f));
        path.MoveTo(bump, 0);
        AddCloudEdge(path, bump, 0, width - bump, 0, 0, -bump);
        AddCloudEdge(path, width, bump, width, height - bump, bump, 0);
        AddCloudEdge(path, width - bump, height, bump, height, 0, bump);
        AddCloudEdge(path, 0, height - bump, 0, bump, -bump, 0);
        path.Close();
        return path;
    }

    private static void AddCloudEdge(
        SKPath path,
        float startX,
        float startY,
        float endX,
        float endY,
        float controlOffsetX,
        float controlOffsetY)
    {
        float length = MathF.Sqrt(MathF.Pow(endX - startX, 2) + MathF.Pow(endY - startY, 2));
        int segments = Math.Max(2, (int)MathF.Ceiling(length / 42f));
        for (int i = 1; i <= segments; i++)
        {
            float t0 = (i - 1f) / segments;
            float t1 = i / (float)segments;
            float mid = (t0 + t1) / 2f;
            var control = new SKPoint(
                startX + (endX - startX) * mid + controlOffsetX,
                startY + (endY - startY) * mid + controlOffsetY);
            var end = new SKPoint(
                startX + (endX - startX) * t1,
                startY + (endY - startY) * t1);
            path.QuadTo(control, end);
        }
    }

    private static void DrawNoteAnnotation(
        SKCanvas canvas,
        PageAnnotation annotation,
        SKColor color,
        PdfExportOptions options)
    {
        IReadOnlyList<SKPoint> corners = AnnotationRectangleCorners(annotation.Points);
        if (corners.Count < 4)
            return;

        using var fill = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0xFF, 0xF8, 0xC6, 235),
            Style = SKPaintStyle.Fill,
        };
        using var stroke = new SKPaint
        {
            IsAntialias = true,
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.15f * ExportLabelScale(options),
        };
        using SKPath path = BuildClosedAnnotationPath(corners);
        canvas.DrawPath(path, fill);
        canvas.DrawPath(path, stroke);

        float labelScale = ExportLabelScale(options);
        float pad = 4.5f * labelScale;
        if (!TryGetAnnotationLocalFrame(corners, out SKMatrix localToPdf, out float width, out float height))
            return;

        var textRect = new SKRect(pad, pad, width - pad, height - pad);
        if (textRect.Width <= 1 || textRect.Height <= 1)
            return;

        using var saved = new SKAutoCanvasRestore(canvas, true);
        canvas.Concat(ref localToPdf);
        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.Black.WithAlpha(220),
            TextSize = 8.0f * labelScale,
            Typeface = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default,
        };
        float lineHeight = textPaint.TextSize * 1.22f;
        int maxLines = Math.Max(1, (int)(textRect.Height / lineHeight));
        IReadOnlyList<string> lines = WrapAnnotationText(annotation.Text, textPaint, textRect.Width, maxLines);
        float baseline = textRect.Top - textPaint.FontMetrics.Ascent;
        foreach (string line in lines)
        {
            if (baseline + textPaint.FontMetrics.Descent > textRect.Bottom)
                break;

            canvas.DrawText(line, textRect.Left, baseline, textPaint);
            baseline += lineHeight;
        }
    }

    private static IReadOnlyList<SKPoint> AnnotationRectangleCorners(IReadOnlyList<SKPoint> points)
    {
        if (points.Count >= 4)
            return points.Take(4).ToList();

        if (points.Count < 2)
            return points;

        SKRect rect = MeasurementGeometry.NormalizeRect(points[0], points[1]);
        return
        [
            new SKPoint(rect.Left, rect.Top),
            new SKPoint(rect.Right, rect.Top),
            new SKPoint(rect.Right, rect.Bottom),
            new SKPoint(rect.Left, rect.Bottom),
        ];
    }

    private static SKPath BuildClosedAnnotationPath(IReadOnlyList<SKPoint> points)
    {
        var path = new SKPath();
        if (points.Count == 0)
            return path;

        path.MoveTo(points[0]);
        for (int i = 1; i < points.Count; i++)
            path.LineTo(points[i]);
        path.Close();
        return path;
    }

    private static bool TryGetAnnotationLocalFrame(
        IReadOnlyList<SKPoint> corners,
        out SKMatrix localToPdf,
        out float width,
        out float height)
    {
        localToPdf = SKMatrix.CreateIdentity();
        width = 0;
        height = 0;
        if (corners.Count < 4)
            return false;

        SKPoint origin = corners[0];
        SKPoint xAxis = new(corners[1].X - origin.X, corners[1].Y - origin.Y);
        SKPoint yAxis = new(corners[3].X - origin.X, corners[3].Y - origin.Y);
        width = MeasurementGeometry.Distance(corners[0], corners[1]);
        height = MeasurementGeometry.Distance(corners[0], corners[3]);
        if (width <= ViewportConstants.ZeroLengthEpsilon ||
            height <= ViewportConstants.ZeroLengthEpsilon)
        {
            return false;
        }

        localToPdf = new SKMatrix
        {
            ScaleX = xAxis.X / width,
            SkewX = yAxis.X / height,
            TransX = origin.X,
            SkewY = xAxis.Y / width,
            ScaleY = yAxis.Y / height,
            TransY = origin.Y,
            Persp0 = 0,
            Persp1 = 0,
            Persp2 = 1,
        };
        return true;
    }

    private static IReadOnlyList<string> WrapAnnotationText(string text, SKPaint paint, float maxWidth, int maxLines)
    {
        text = string.IsNullOrWhiteSpace(text) ? "Note" : text.Trim();
        var result = new List<string>();
        foreach (string paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            string current = "";
            foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = string.IsNullOrWhiteSpace(current) ? word : $"{current} {word}";
                if (paint.MeasureText(candidate) <= maxWidth || string.IsNullOrWhiteSpace(current))
                {
                    current = candidate;
                    continue;
                }

                result.Add(current);
                if (result.Count >= maxLines)
                    return result;
                current = word;
            }

            if (!string.IsNullOrWhiteSpace(current))
                result.Add(current);
            if (result.Count >= maxLines)
                return result;
        }

        return result.Count == 0 ? ["Note"] : result;
    }

    private static string FormatAnnotationLength(SKPoint start, SKPoint end, double scaleMetersPerPt, UnitMode unitMode)
    {
        float lengthPt = MeasurementGeometry.Distance(start, end);
        return AnnotationGlyphRenderer.FormatLength(lengthPt, (float)scaleMetersPerPt, unitMode);
    }
}
