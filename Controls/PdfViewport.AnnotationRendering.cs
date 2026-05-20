using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Docnet.Core;
using Docnet.Core.Models;
using OurPlaneCore;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private void DrawPageAnnotations(SKCanvas canvas, SKRect visiblePdf)
    {
        foreach (PageAnnotation annotation in _annotations)
        {
            if (!IsAnnotationVisibleOnActivePage(annotation) ||
                annotation.Points.Count < 2)
            {
                continue;
            }

            bool selected = IsAnnotationSelected(annotation);
            if (!selected && !PointsVisible(annotation.Points, visiblePdf))
                continue;

            DrawPageAnnotation(canvas, annotation, selected);
            if (selected && !_renderNavigationFastFrame)
            {
                DrawAnnotationSelectionBounds(canvas, annotation);
                DrawAnnotationSelectionHandles(canvas, annotation);
            }
        }
    }

    private void DrawPageAnnotation(SKCanvas canvas, PageAnnotation annotation, bool selected)
    {
        string kind = OurPlaneCoreJobStore.NormalizePageAnnotationKind(annotation.Kind);
        SKColor color = GetCachedColor(annotation.Color, new SKColor(0x15, 0x65, 0xC0));
        using var stroke = new SKPaint
        {
            Color = color,
            StrokeWidth = AnnotationStrokeWidth(annotation, selected),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };

        SKPoint start = annotation.Points[0];
        SKPoint end = annotation.Points[1];
        if (kind == "note")
        {
            DrawNoteAnnotation(canvas, annotation, color, selected);
            return;
        }

        if (kind == "rectangle")
        {
            if (annotation.Points.Count >= 4)
            {
                using var path = new SKPath();
                path.MoveTo(annotation.Points[0]);
                for (int i = 1; i < annotation.Points.Count; i++)
                    path.LineTo(annotation.Points[i]);
                path.Close();
                canvas.DrawPath(path, stroke);
            }
            else
            {
                canvas.DrawRect(NormalizeRect(start, end), stroke);
            }
            return;
        }

        if (kind == "cloud")
        {
            DrawCloudAnnotation(canvas, annotation, stroke);
            return;
        }

        if (kind == "area")
        {
            DrawAreaAnnotation(canvas, annotation, color, stroke);
            return;
        }

        canvas.DrawLine(start, end, stroke);
        if (kind == "arrow")
        {
            AnnotationGlyphRenderer.DrawArrowHead(canvas, start, end, stroke, ScreenToPdfDistance(9f));
            return;
        }

        if (kind == "dimension" && !_renderNavigationFastFrame)
        {
            AnnotationGlyphRenderer.DrawDimensionTicks(canvas, start, end, stroke, ScreenToPdfDistance(6f));
            DrawScreenTextBox(
                canvas,
                new SKPoint((start.X + end.X) / 2f, (start.Y + end.Y) / 2f),
                [AnnotationLabel(annotation)],
                SKColors.White,
                SKColors.Black.WithAlpha(185),
                color,
                MeasurementLabelFontScreenPx,
                MeasurementLabelPaddingScreenPx,
                centered: true);
        }
    }

    private float AnnotationStrokeWidth(PageAnnotation annotation, bool selected)
    {
        double value = annotation.StrokeWidth is >= 0.75 and <= 12.0
            ? annotation.StrokeWidth
            : 1.8;
        if (selected)
            value += 0.9;
        return ScreenToPdfDistance((float)Math.Clamp(value, 0.75, 12.9));
    }

    private void DrawAreaAnnotation(SKCanvas canvas, PageAnnotation annotation, SKColor color, SKPaint stroke)
    {
        if (annotation.Points.Count < 3)
            return;

        using var fill = new SKPaint
        {
            Color = color.WithAlpha(55),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using SKPath path = BuildClosedAnnotationPath(annotation.Points);
        canvas.DrawPath(path, fill);
        canvas.DrawPath(path, stroke);
    }

    private void DrawCloudAnnotation(SKCanvas canvas, PageAnnotation annotation, SKPaint stroke)
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
        if (width <= ViewportConstants.ZeroLengthEpsilon ||
            height <= ViewportConstants.ZeroLengthEpsilon)
        {
            return path;
        }

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

    private void DrawTransformOverlay(SKCanvas canvas)
    {
        if (!TryGetTransformBounds(out SKRect selectedBounds))
            return;

        SKRect outer = TransformHandleBounds(selectedBounds);
        float safeZoom = Math.Max(_zoom, 0.001f);
        SKColor orange = new(0xF4, 0x9B, 0x24);
        using var stroke = new SKPaint
        {
            Color = orange.WithAlpha(190),
            StrokeWidth = 1.4f / safeZoom,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            PathEffect = SKPathEffect.CreateDash([7f / safeZoom, 5f / safeZoom], 0),
        };
        using var handleFill = new SKPaint
        {
            Color = orange.WithAlpha(150),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var handleStroke = new SKPaint
        {
            Color = orange.WithAlpha(235),
            StrokeWidth = 1.2f / safeZoom,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };

        canvas.DrawRect(outer, stroke);

        float handle = 8f / safeZoom;
        foreach ((TransformHandleKind kind, SKPoint point) in TransformHandlePoints(outer))
        {
            if (kind == TransformHandleKind.Rotate)
            {
                SKPoint topMid = new((outer.Left + outer.Right) / 2f, outer.Top);
                canvas.DrawLine(topMid, point, handleStroke);
                canvas.DrawCircle(point, handle * 0.68f, handleFill);
                canvas.DrawCircle(point, handle * 0.68f, handleStroke);
                continue;
            }

            var rect = SKRect.Create(point.X - handle / 2f, point.Y - handle / 2f, handle, handle);
            canvas.DrawRoundRect(rect, 1.5f / safeZoom, 1.5f / safeZoom, handleFill);
            canvas.DrawRoundRect(rect, 1.5f / safeZoom, 1.5f / safeZoom, handleStroke);
        }
    }

    private void DrawAnnotationSelectionBounds(SKCanvas canvas, PageAnnotation annotation)
    {
        if (annotation.Points.Count == 0)
            return;

        SKRect bounds = PointsBounds(annotation.Points);
        bounds.Inflate(ScreenToPdfDistance(6f), ScreenToPdfDistance(6f));
        using var stroke = new SKPaint
        {
            Color = SKColors.DodgerBlue,
            StrokeWidth = ScreenToPdfDistance(1.3f),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            PathEffect = SKPathEffect.CreateDash([ScreenToPdfDistance(5f), ScreenToPdfDistance(4f)], 0),
        };
        canvas.DrawRect(bounds, stroke);
    }

    private void DrawAnnotationSelectionHandles(SKCanvas canvas, PageAnnotation annotation)
    {
        if (annotation.Points.Count == 0)
            return;

        float radius = ScreenToPdfDistance(5f);
        using var fill = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var activeFill = new SKPaint
        {
            Color = TempColor,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var stroke = new SKPaint
        {
            Color = SKColors.DodgerBlue,
            StrokeWidth = ScreenToPdfDistance(1.5f),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };

        for (int i = 0; i < annotation.Points.Count; i++)
        {
            SKPoint point = annotation.Points[i];
            var rect = SKRect.Create(point.X - radius, point.Y - radius, radius * 2, radius * 2);
            canvas.DrawRect(rect, i == _selectedAnnotationVertexIndex ? activeFill : fill);
            canvas.DrawRect(rect, stroke);
        }
    }

    private string AnnotationLabel(PageAnnotation annotation)
    {
        if (!string.IsNullOrWhiteSpace(annotation.Text))
            return annotation.Text;

        if (annotation.Points.Count < 2)
            return "";

        SKPoint start = annotation.Points[0];
        SKPoint end = annotation.Points[1];
        float lengthPt = MeasurementGeometry.Distance(start, end);
        double scale = annotation.ScaleMetersPerPt > 0
            ? annotation.ScaleMetersPerPt
            : ScaleMetersPerPt;
        return AnnotationGlyphRenderer.FormatLength(lengthPt, (float)scale, UnitMode);
    }
    private void DrawNoteAnnotation(SKCanvas canvas, PageAnnotation annotation, SKColor color, bool selected)
    {
        IReadOnlyList<SKPoint> corners = AnnotationRectangleCorners(annotation.Points);
        if (corners.Count < 4)
            return;

        using var fill = new SKPaint
        {
            Color = new SKColor(0xFF, 0xF8, 0xC6, 235),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var stroke = new SKPaint
        {
            Color = color,
            StrokeWidth = ScreenToPdfDistance(selected ? 2.4f : 1.5f),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };
        using SKPath path = BuildClosedAnnotationPath(corners);
        canvas.DrawPath(path, fill);
        canvas.DrawPath(path, stroke);

        DrawNoteText(canvas, annotation, corners);
    }

    private void DrawNoteText(SKCanvas canvas, PageAnnotation annotation, IReadOnlyList<SKPoint> corners)
    {
        if (!TryGetAnnotationLocalFrame(corners, out SKMatrix localToPdf, out float width, out float height))
            return;

        float pad = ScreenToPdfDistance(7f);
        SKRect textRect = new(pad, pad, width - pad, height - pad);
        if (textRect.Width <= 1 || textRect.Height <= 1)
            return;

        using var saved = new SKAutoCanvasRestore(canvas, true);
        canvas.Concat(ref localToPdf);
        using var textPaint = new SKPaint
        {
            Color = SKColors.Black.WithAlpha(220),
            TextSize = ScreenToPdfDistance(11f),
            IsAntialias = true,
            Typeface = OverlayUiTypeface,
        };
        float lineHeight = textPaint.TextSize * 1.22f;
        int maxLines = Math.Max(1, (int)(textRect.Height / lineHeight));
        IReadOnlyList<string> lines = WrapNoteText(annotation.Text, textPaint, textRect.Width, maxLines);
        float baseline = textRect.Top - textPaint.FontMetrics.Ascent;
        foreach (string line in lines)
        {
            if (baseline + textPaint.FontMetrics.Descent > textRect.Bottom)
                break;

            canvas.DrawText(line, textRect.Left, baseline, textPaint);
            baseline += lineHeight;
        }
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

    private static IReadOnlyList<string> WrapNoteText(string text, SKPaint paint, float maxWidth, int maxLines)
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
}
