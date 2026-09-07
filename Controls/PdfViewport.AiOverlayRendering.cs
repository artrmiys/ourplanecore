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
using OurPlanCore;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    private void DrawAiActionDraftPreview(SKCanvas canvas, SKRect visiblePdf)
    {
        if (_aiActionDraftPreview == null || _aiActionDraftPreview.Actions.Count == 0)
            return;

        using var stroke = new SKPaint
        {
            Color = new SKColor(0x00, 0xBC, 0xD4, 230),
            StrokeWidth = ScreenToPdfDistance(2.5f),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            PathEffect = SKPathEffect.CreateDash([ScreenToPdfDistance(10f), ScreenToPdfDistance(5f)], 0),
        };
        using var fill = new SKPaint
        {
            Color = new SKColor(0x00, 0xBC, 0xD4, 42),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var dotFill = new SKPaint
        {
            Color = new SKColor(0x00, 0xBC, 0xD4, 235),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var dotStroke = new SKPaint
        {
            Color = SKColors.White,
            StrokeWidth = ScreenToPdfDistance(1.5f),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };

        foreach (SmartAiAction action in _aiActionDraftPreview.Actions)
        {
            if (!ActionMatchesPreviewPage(action))
                continue;

            var points = new List<SKPoint>();
            foreach (SmartAiActionPoint point in action.Points)
                points.Add(new SKPoint(point.X, point.Y));

            if (points.Count == 0 || !PointsVisible(points, visiblePdf))
                continue;

            bool isArea = action.MeasurementType.Equals("area", StringComparison.OrdinalIgnoreCase) ||
                          action.Type.Contains("area", StringComparison.OrdinalIgnoreCase);
            bool isPoint = action.MeasurementType.Equals("point", StringComparison.OrdinalIgnoreCase) ||
                           action.Type.Contains("point", StringComparison.OrdinalIgnoreCase);

            if (isArea && points.Count >= 3)
            {
                using var path = new SKPath();
                path.MoveTo(points[0]);
                for (int i = 1; i < points.Count; i++)
                    path.LineTo(points[i]);
                path.Close();
                canvas.DrawPath(path, fill);
                canvas.DrawPath(path, stroke);
                DrawAiActionDots(canvas, points, dotFill, dotStroke);
                DrawLabel(canvas, Centroid(points), AiActionLabel(action), "#00BCD4");
            }
            else if (!isPoint && points.Count >= 2)
            {
                using var path = new SKPath();
                path.MoveTo(points[0]);
                for (int i = 1; i < points.Count; i++)
                    path.LineTo(points[i]);
                canvas.DrawPath(path, stroke);
                DrawAiActionDots(canvas, points, dotFill, dotStroke);
                DrawLabel(canvas, points[^1], AiActionLabel(action), "#00BCD4");
            }
            else
            {
                DrawAiActionDots(canvas, points, dotFill, dotStroke);
                DrawLabel(canvas, points[0], AiActionLabel(action), "#00BCD4");
            }
        }
    }

    private bool ActionMatchesPreviewPage(SmartAiAction action)
    {
        if (string.IsNullOrWhiteSpace(action.Page) || string.IsNullOrWhiteSpace(_aiActionDraftPreviewPage))
            return true;

        return string.Equals(action.Page, _aiActionDraftPreviewPage, StringComparison.OrdinalIgnoreCase);
    }

    private void DrawAiActionDots(SKCanvas canvas, IReadOnlyList<SKPoint> points, SKPaint fill, SKPaint stroke)
    {
        float radius = ScreenToPdfDistance(4.5f);
        foreach (SKPoint point in points)
        {
            canvas.DrawCircle(point, radius, fill);
            canvas.DrawCircle(point, radius, stroke);
        }
    }

    private static string AiActionLabel(SmartAiAction action)
    {
        string label = string.IsNullOrWhiteSpace(action.Label) ? action.Type : action.Label;
        if (action.Confidence > 0)
            label += $" ({action.Confidence:P0})";
        return $"AI: {label}";
    }

    private void DrawAiMarkers(SKCanvas canvas, SKRect visiblePdf)
    {
        if (_aiMarkers.Count == 0)
            return;

        using var fill = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var stroke = new SKPaint
        {
            Color = SKColors.White.WithAlpha(245),
            StrokeWidth = ScreenToPdfDistance(1.8f),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };
        using var accent = new SKPaint
        {
            StrokeWidth = ScreenToPdfDistance(2.2f),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };

        foreach (SmartAiMarker marker in _aiMarkers)
        {
            SKPoint point = new(marker.PdfPoint.X, marker.PdfPoint.Y);
            if (!PointVisible(point, visiblePdf))
                continue;

            SKColor color = AiMarkerColor(marker);
            fill.Color = color.WithAlpha(220);
            accent.Color = color.WithAlpha(245);

            float size = ScreenToPdfDistance(7f);
            using var markerPath = new SKPath();
            markerPath.MoveTo(point.X, point.Y - size);
            markerPath.LineTo(point.X + size, point.Y);
            markerPath.LineTo(point.X, point.Y + size);
            markerPath.LineTo(point.X - size, point.Y);
            markerPath.Close();

            canvas.DrawPath(markerPath, fill);
            canvas.DrawPath(markerPath, stroke);

            if (marker.SampleKind.Equals("negative", StringComparison.OrdinalIgnoreCase) ||
                marker.SampleKind.Equals("ignore", StringComparison.OrdinalIgnoreCase))
            {
                float cross = size * 0.72f;
                canvas.DrawLine(point.X - cross, point.Y - cross, point.X + cross, point.Y + cross, accent);
                canvas.DrawLine(point.X + cross, point.Y - cross, point.X - cross, point.Y + cross, accent);
            }
            else
            {
                canvas.DrawCircle(point, ScreenToPdfDistance(2.2f), stroke);
            }

            DrawLabel(
                canvas,
                new SKPoint(point.X + size * 1.6f, point.Y - size * 1.6f),
                AiMarkerLabel(marker),
                ColorHex(color));
        }
    }

    private bool PointVisible(SKPoint point, SKRect visiblePdf)
    {
        float margin = ScreenToPdfDistance(24f);
        return point.X >= visiblePdf.Left - margin &&
               point.X <= visiblePdf.Right + margin &&
               point.Y >= visiblePdf.Top - margin &&
               point.Y <= visiblePdf.Bottom + margin;
    }

    private static SKColor AiMarkerColor(SmartAiMarker marker)
    {
        if (marker.SampleKind.Equals("ignore", StringComparison.OrdinalIgnoreCase))
            return new SKColor(0xEF, 0x6C, 0x00);
        if (marker.SampleKind.Equals("negative", StringComparison.OrdinalIgnoreCase))
            return new SKColor(0xD3, 0x2F, 0x2F);

        string type = marker.Type ?? "";
        if (type.Contains("height", StringComparison.OrdinalIgnoreCase))
            return new SKColor(0x7B, 0x1F, 0xA2);
        if (type.Contains("window", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("door", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("opening", StringComparison.OrdinalIgnoreCase))
            return new SKColor(0x19, 0x76, 0xD2);
        if (type.Contains("roof", StringComparison.OrdinalIgnoreCase))
            return new SKColor(0x2E, 0x7D, 0x32);
        if (type.Contains("corner", StringComparison.OrdinalIgnoreCase))
            return new SKColor(0x00, 0x96, 0x88);

        return new SKColor(0x00, 0xBC, 0xD4);
    }

    private static string AiMarkerLabel(SmartAiMarker marker)
    {
        string label = marker.Type switch
        {
            "exterior_corner" => "ext corner",
            "interior_corner" => "int corner",
            "wall_height_sample" => "height",
            "window_sample" => "window",
            "door_sample" => "door",
            "opening_sample" => "opening",
            "roof_note" => "roof note",
            "roof_edge_sample" => "roof edge",
            "dimension_text_sample" => "dimension",
            "ignore_area" => "ignore",
            _ => string.IsNullOrWhiteSpace(marker.Type) ? "marker" : marker.Type.Replace('_', ' '),
        };

        if (marker.SampleKind.Equals("negative", StringComparison.OrdinalIgnoreCase))
            label = $"not {label}";
        else if (marker.SampleKind.Equals("ignore", StringComparison.OrdinalIgnoreCase))
            label = "ignore";

        return $"M: {label}";
    }

    private static bool PointsVisible(IReadOnlyList<SKPoint> points, SKRect visiblePdf)
    {
        SKRect bounds = PointsBounds(points);
        return bounds.Left <= visiblePdf.Right &&
               bounds.Right >= visiblePdf.Left &&
               bounds.Top <= visiblePdf.Bottom &&
               bounds.Bottom >= visiblePdf.Top;
    }

    private static SKRect PointsBounds(IReadOnlyList<SKPoint> points)
    {
        if (points.Count == 0)
            return SKRect.Empty;

        float left = points[0].X;
        float right = points[0].X;
        float top = points[0].Y;
        float bottom = points[0].Y;
        for (int i = 1; i < points.Count; i++)
        {
            SKPoint point = points[i];
            left = Math.Min(left, point.X);
            right = Math.Max(right, point.X);
            top = Math.Min(top, point.Y);
            bottom = Math.Max(bottom, point.Y);
        }

        return new SKRect(left, top, right, bottom);
    }
}
