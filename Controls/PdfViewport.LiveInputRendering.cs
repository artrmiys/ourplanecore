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
    // ── Draw in-progress line / area ──────────────────────────────────────────

    private void DrawInProgress(SKCanvas canvas)
    {
        DrawPdfLayerTraceOverlay(canvas);

        using var tempPaint = new SKPaint
        {
            Color       = TempColor,
            StrokeWidth = ScreenToPdfDistance(2f),
            IsAntialias = true,
            Style       = SKPaintStyle.Stroke,
        };
        using var dotPaint = new SKPaint
        {
            Color       = TempColor,
            IsAntialias = true,
            Style       = SKPaintStyle.Fill,
        };

        float r = ScreenToPdfDistance(4f * PointSizeScaleFactor());

        foreach (var p in _drawPts)
            canvas.DrawCircle(p, r, dotPaint);

        if (_drawPts.Count >= 2)
        {
            for (int i = 1; i < _drawPts.Count; i++)
                canvas.DrawLine(_drawPts[i - 1], _drawPts[i], tempPaint);
        }

        // Rubber-band
        if (_drawPts.Count > 0 && _rubberEnd.HasValue)
        {
            using var rubber = new SKPaint
            {
                Color       = TempColor,
                StrokeWidth = ScreenToPdfDistance(1f),
                IsAntialias = true,
                Style       = SKPaintStyle.Stroke,
                PathEffect  = SKPathEffect.CreateDash([ScreenToPdfDistance(4f), ScreenToPdfDistance(4f)], 0),
            };
            if (ShouldPreviewAsBox())
                canvas.DrawRect(NormalizeRect(_drawPts[0], _rubberEnd.Value), rubber);
            else
                canvas.DrawLine(_drawPts[^1], _rubberEnd.Value, rubber);
        }

        DrawLiveRecordLengthLabels(canvas);

        if (_joistDirectionMeasurement != null)
        {
            using var joistPaint = new SKPaint
            {
                Color = new SKColor(0x00, 0x96, 0x88),
                StrokeWidth = ScreenToPdfDistance(2.2f),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                PathEffect = SKPathEffect.CreateDash([ScreenToPdfDistance(7f), ScreenToPdfDistance(4f)], 0),
            };
            using var joistDot = new SKPaint
            {
                Color = new SKColor(0x00, 0x96, 0x88),
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };
            float jr = ScreenToPdfDistance(5f);
            foreach (SKPoint point in _joistDirectionPts)
                canvas.DrawCircle(point, jr, joistDot);
            if (_joistDirectionPts.Count == 1 && _joistDirectionRubberEnd.HasValue)
                canvas.DrawLine(_joistDirectionPts[0], _joistDirectionRubberEnd.Value, joistPaint);
        }

        // Scale line
        if (_scalePts.Count >= 1)
        {
            using var scPaint = new SKPaint
            {
                Color       = ScaleClr,
                StrokeWidth = ScreenToPdfDistance(2f),
                IsAntialias = true,
                Style       = SKPaintStyle.Stroke,
            };
            using var scDot = new SKPaint
            {
                Color       = ScaleClr,
                IsAntialias = true,
                Style       = SKPaintStyle.Fill,
            };
            float sr = ScreenToPdfDistance(6f);
            foreach (var p in _scalePts)
                canvas.DrawCircle(p, sr, scDot);
            if (_scalePts.Count == 2)
                canvas.DrawLine(_scalePts[0], _scalePts[1], scPaint);
        }

        if (_snapPreview.HasValue)
        {
            SKPoint point = _snapPreview.Value;
            bool isPdfSnap = _snapPreviewKind.StartsWith("pdf-", StringComparison.OrdinalIgnoreCase);
            SKColor snapColor = isPdfSnap ? new SKColor(0x00, 0x78, 0xD4) : new SKColor(0xE5, 0x39, 0x35);
            float half = ScreenToPdfDistance(SnapMarkerScreenPx);
            var rect = new SKRect(
                point.X - half,
                point.Y - half,
                point.X + half,
                point.Y + half);
            using var snapFill = new SKPaint
            {
                Color = snapColor.WithAlpha(80),
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };
            using var snapStroke = new SKPaint
            {
                Color = snapColor,
                StrokeWidth = ScreenToPdfDistance(2f),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
            };
            if (string.Equals(_snapPreviewKind, "intersection", StringComparison.OrdinalIgnoreCase))
            {
                canvas.DrawCircle(point, half, snapFill);
                canvas.DrawCircle(point, half, snapStroke);
                canvas.DrawLine(point.X - half, point.Y, point.X + half, point.Y, snapStroke);
                canvas.DrawLine(point.X, point.Y - half, point.X, point.Y + half, snapStroke);
            }
            else if (string.Equals(_snapPreviewKind, "midpoint", StringComparison.OrdinalIgnoreCase))
            {
                using var diamond = new SKPath();
                diamond.MoveTo(point.X, point.Y - half);
                diamond.LineTo(point.X + half, point.Y);
                diamond.LineTo(point.X, point.Y + half);
                diamond.LineTo(point.X - half, point.Y);
                diamond.Close();
                canvas.DrawPath(diamond, snapFill);
                canvas.DrawPath(diamond, snapStroke);
            }
            else
            {
                canvas.DrawRect(rect, snapFill);
                canvas.DrawRect(rect, snapStroke);
            }

            string labelKind = _snapPreviewKind.ToLowerInvariant() switch
            {
                "midpoint" => "mid",
                "intersection" => "int",
                "pdf-corner" => "pdf corner",
                "pdf-point" => "pdf point",
                "pdf-line" => "pdf line",
                "pdf-overlay-corner" => "overlay corner",
                "pdf-overlay-point" => "overlay point",
                "pdf-overlay-line" => "overlay line",
                _ => "end",
            };
            string label = $"{labelKind} {point.X:F0},{point.Y:F0}";
            float textSize = ScreenToPdfDistance(10f);
            using var snapText = new SKPaint
            {
                Color = snapColor,
                IsAntialias = true,
                TextSize = textSize,
            };
            canvas.DrawText(label, point.X + half * 1.6f, point.Y - half * 1.2f, snapText);
        }

        if (_boxSelecting)
        {
            float safeZoom = Math.Max(_zoom, 0.001f);
            SKRect rect = NormalizeRect(_boxSelectStartPdf, _boxSelectEndPdf);
            using var fill = new SKPaint
            {
                Color = new SKColor(0x1E, 0x88, 0xE5, 32),
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };
            using var stroke = new SKPaint
            {
                Color = new SKColor(0x1E, 0x88, 0xE5),
                StrokeWidth = 1.5f / safeZoom,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                PathEffect = SKPathEffect.CreateDash([8f / safeZoom, 4f / safeZoom], 0),
            };
            canvas.DrawRect(rect, fill);
            canvas.DrawRect(rect, stroke);
        }

        DrawAiCropNoteSelectionOverlay(canvas);
    }

    private void DrawLiveRecordLengthLabels(SKCanvas canvas)
    {
        if (_tool == ViewerTool.Ruler)
        {
            DrawLiveRulerLengthLabel(canvas);
            return;
        }

        if (_tool is not (ViewerTool.Line or ViewerTool.Area) ||
            _drawPts.Count == 0 ||
            ScaleMetersPerPt <= 0)
        {
            return;
        }

        IReadOnlyList<SKPoint> points = LiveRecordLengthPoints();
        if (points.Count < 2)
            return;

        double totalMeters = 0;
        for (int i = 1; i < points.Count; i++)
        {
            SKPoint start = points[i - 1];
            SKPoint end = points[i];
            float lengthPt = MeasurementGeometry.Distance(start, end);
            if (lengthPt <= ViewportConstants.ZeroLengthEpsilon)
                continue;

            totalMeters += lengthPt * ScaleMetersPerPt;
            if (PdfToScreenDistance(lengthPt) >= 34f)
                DrawLiveRecordText(canvas, SegmentLengthLabelPoint(start, end), FormatLiveFeet(lengthPt * ScaleMetersPerPt), centered: true);
        }

        if (totalMeters <= 0)
            return;

        SKPoint anchor = points[^1];
        float offset = ScreenToPdfDistance(10f);
        DrawLiveRecordText(canvas, new SKPoint(anchor.X + offset, anchor.Y - offset), $"total {FormatLiveFeet(totalMeters)}", centered: false);
    }

    private void DrawLiveRulerLengthLabel(SKCanvas canvas)
    {
        if (_drawPts.Count == 0 ||
            !_rubberEnd.HasValue ||
            ScaleMetersPerPt <= 0)
        {
            return;
        }

        SKPoint start = _drawPts[0];
        SKPoint end = _rubberEnd.Value;
        float lengthPt = MeasurementGeometry.Distance(start, end);
        if (lengthPt <= ViewportConstants.ZeroLengthEpsilon)
            return;

        DrawLiveRecordText(
            canvas,
            SegmentLengthLabelPoint(start, end),
            FormatLiveFeet(lengthPt * ScaleMetersPerPt),
            centered: true);
    }

    private IReadOnlyList<SKPoint> LiveRecordLengthPoints()
    {
        if (ShouldPreviewAsBox() && _rubberEnd.HasValue)
            return BoxMeasurementPoints(_drawPts[0], _rubberEnd.Value, closeForLine: true);

        var points = _drawPts.ToList();
        if (_rubberEnd.HasValue)
            points.Add(_rubberEnd.Value);
        return points;
    }

    private SKPoint SegmentLengthLabelPoint(SKPoint start, SKPoint end)
    {
        float midX = (start.X + end.X) / 2f;
        float midY = (start.Y + end.Y) / 2f;
        float dx = end.X - start.X;
        float dy = end.Y - start.Y;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length <= ViewportConstants.ZeroLengthEpsilon)
            return new SKPoint(midX, midY);

        float offset = ScreenToPdfDistance(8f);
        return new SKPoint(midX - dy / length * offset, midY + dx / length * offset);
    }

    private void DrawLiveRecordText(SKCanvas canvas, SKPoint pos, string text, bool centered)
    {
        float safeZoom = Math.Max(_zoom, 0.001f);
        using var textPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 76),
            TextSize = 9f / safeZoom,
            IsAntialias = true,
            Typeface = LabelTypeface,
        };

        float width = textPaint.MeasureText(text);
        float x = centered ? pos.X - width / 2f : pos.X;
        float y = centered
            ? pos.Y - (textPaint.FontMetrics.Ascent + textPaint.FontMetrics.Descent) / 2f
            : pos.Y;
        canvas.DrawText(text, x, y, textPaint);
    }

    private static string FormatLiveFeet(double meters)
    {
        double feet = meters / 0.3048;
        string format = feet >= 100 ? "0" : feet >= 10 ? "0.#" : "0.##";
        return $"{feet.ToString(format, CultureInfo.InvariantCulture)} ft";
    }
}
