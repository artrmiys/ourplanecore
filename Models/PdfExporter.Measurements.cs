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
    private static void DrawMeasurements(
        SKCanvas canvas,
        IReadOnlyList<PdfExportTakeoffInput> takeoffs,
        PageInfo page,
        PdfExportOptions options)
    {
        var measurements = takeoffs
            .SelectMany(takeoff =>
            {
                SKColor color = ParseSkColor(takeoff.Item.Color, SKColors.Red);
                return takeoff.Measurements.Select(measurement => (Measurement: measurement, Color: color));
            })
            .ToList();

        foreach ((Measurement measurement, SKColor color) in measurements)
            DrawMeasurementGeometry(canvas, measurement, color, options);

        foreach ((Measurement measurement, SKColor color) in measurements)
            DrawMeasurementLabels(canvas, measurement, color, page.ScaleMetersPerPt, options);
    }
    private static void DrawMeasurementGeometry(
        SKCanvas canvas,
        Measurement measurement,
        SKColor color,
        PdfExportOptions options)
    {
        string type = OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType);
        if (measurement.Points.Count == 0)
            return;

        float strokeScale = ExportStrokeScale(options);
        float pointScale = ExportPointScale(options);
        using var stroke = new SKPaint
        {
            IsAntialias = true,
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.4f * strokeScale,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };

        if (type == "area" && measurement.Points.Count >= 3)
        {
            using var path = BuildPdfExportAreaPath(measurement);

            using var fill = new SKPaint
            {
                IsAntialias = true,
                Color = color.WithAlpha(ExportAreaFillAlpha(options)),
                Style = SKPaintStyle.Fill,
            };
            stroke.StrokeWidth = 1.4f * strokeScale * ExportAreaEdgeScale(options);
            canvas.DrawPath(path, fill);
            canvas.DrawPath(path, stroke);
            DrawJoistLayout(canvas, measurement, color, options, drawSegments: true, drawLabels: false);
            return;
        }

        if (type == "line" && measurement.Points.Count >= 2)
        {
            for (int i = 1; i < measurement.Points.Count; i++)
                canvas.DrawLine(measurement.Points[i - 1], measurement.Points[i], stroke);
            DrawLinePointMarkers(canvas, measurement.Points, color, pointScale);
            return;
        }

        foreach (SKPoint point in measurement.Points)
        {
            float size = 7.6f * pointScale;
            var box = new SKRect(
                point.X - size / 2f,
                point.Y - size / 2f,
                point.X + size / 2f,
                point.Y + size / 2f);
            MeasurementGlyph.DrawSkia(canvas, MeasurementGlyph.CountKind(measurement.CountSymbol), color, box);
        }
    }

    private static void DrawMeasurementLabels(
        SKCanvas canvas,
        Measurement measurement,
        SKColor color,
        double pageScaleMetersPerPt,
        PdfExportOptions options)
    {
        string type = OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType);
        if (measurement.Points.Count == 0)
            return;

        if (type == "area" && measurement.Points.Count >= 3)
        {
            DrawJoistLayout(canvas, measurement, color, options, drawSegments: false, drawLabels: true);
            // For a joist area the centroid label is the joist summary, so it
            // follows the Joist toggle; a plain area follows the Area toggle.
            bool showAreaCentroid = measurement.JoistEnabled
                ? ShouldExportJoistLabels(options)
                : ShouldExportMeasurementLabel(measurement.MType, options);
            if (showAreaCentroid)
            {
                DrawMeasurementLabel(
                    canvas,
                    MeasurementLabelPoint(measurement),
                    measurement.Label(pageScaleMetersPerPt, options.UnitMode),
                    color,
                    centered: true,
                    ExportLabelScale(options));
            }
            return;
        }

        if (!ShouldExportMeasurementLabel(measurement.MType, options))
            return;

        DrawMeasurementLabel(
            canvas,
            measurement.Points[^1],
            measurement.Label(pageScaleMetersPerPt, options.UnitMode),
            color,
            centered: false,
            ExportLabelScale(options));
    }

    private static bool ShouldExportMeasurementLabel(string measurementType, PdfExportOptions options)
    {
        if (!options.ShowMeasurementLabels)
            return false;

        return OurPlaneCoreJobStore.NormalizeMeasurementType(measurementType) switch
        {
            "point" => options.ShowCountLabels,
            "area" => options.ShowAreaLabels,
            _ => options.ShowLineLabels,
        };
    }

    private static float ExportStrokeScale(PdfExportOptions options) =>
        (float)Math.Clamp(options.MeasurementStrokeScale, 0.25, AppSettingsStore.PdfExportScaleMax);

    private static float ExportAreaEdgeScale(PdfExportOptions options) =>
        (float)Math.Clamp(options.AreaEdgeScale, 0.25, 4.0);

    private static byte ExportAreaFillAlpha(PdfExportOptions options) =>
        (byte)Math.Clamp((int)Math.Round(Math.Clamp(options.AreaFillOpacity, 0.0, 1.0) * 255.0), 0, 255);

    private static bool ShouldExportJoistLabels(PdfExportOptions options) =>
        options.ShowMeasurementLabels && options.ShowJoistLabels;

    private static float ExportPointScale(PdfExportOptions options) =>
        (float)Math.Clamp(options.PointSizeScale, 0.25, AppSettingsStore.PdfExportScaleMax);

    private static void DrawLinePointMarkers(
        SKCanvas canvas,
        IReadOnlyList<SKPoint> points,
        SKColor color,
        float pointScale)
    {
        using var fill = new SKPaint
        {
            IsAntialias = true,
            Color = color.WithAlpha(180),
            Style = SKPaintStyle.Fill,
        };
        float radius = 3.0f * pointScale;
        foreach (SKPoint point in points)
            canvas.DrawCircle(point, radius, fill);
    }

    private static float ExportLabelScale(PdfExportOptions options) =>
        BaseExportLabelScale * (float)Math.Clamp(options.MeasurementLabelScale, 0.50, AppSettingsStore.PdfExportScaleMax);

    private static SKPath BuildPdfExportAreaPath(Measurement measurement)
    {
        var path = new SKPath
        {
            FillType = SKPathFillType.EvenOdd,
        };
        AddClosedContour(path, measurement.Points);
        foreach (IReadOnlyList<SKPoint> hole in measurement.Holes)
            AddClosedContour(path, hole);
        return path;
    }

    private static void AddClosedContour(SKPath path, IReadOnlyList<SKPoint> points)
    {
        if (points.Count < 3)
            return;

        path.MoveTo(points[0]);
        for (int i = 1; i < points.Count; i++)
            path.LineTo(points[i]);
        path.Close();
    }

    private static SKPoint MeasurementLabelPoint(Measurement measurement)
    {
        if (measurement.Points.Count == 0)
            return default;

        if (measurement.Points.Count < 3)
            return measurement.Points[^1];

        double area = 0;
        double x = 0;
        double y = 0;
        for (int i = 0; i < measurement.Points.Count; i++)
        {
            SKPoint a = measurement.Points[i];
            SKPoint b = measurement.Points[(i + 1) % measurement.Points.Count];
            double cross = a.X * b.Y - b.X * a.Y;
            area += cross;
            x += (a.X + b.X) * cross;
            y += (a.Y + b.Y) * cross;
        }

        if (Math.Abs(area) < ViewportConstants.ZeroLengthEpsilon)
            return MeasurementGeometry.Centroid(measurement.Points);

        double factor = 1.0 / (3.0 * area);
        return new SKPoint((float)(x * factor), (float)(y * factor));
    }

    private static void DrawMeasurementLabel(
        SKCanvas canvas,
        SKPoint point,
        string label,
        SKColor color,
        bool centered,
        float labelScale)
    {
        if (string.IsNullOrWhiteSpace(label))
            return;

        string[] lines = label
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
            return;

        float textSize = 7.5f * labelScale;
        float padX = 2.8f * labelScale;
        float padY = 1.8f * labelScale;
        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White,
            TextSize = textSize,
            Typeface = SKTypeface.FromFamilyName("Consolas") ?? SKTypeface.Default,
        };
        float width = 0;
        foreach (string line in lines)
            width = Math.Max(width, textPaint.MeasureText(line));

        float lineHeight = textSize * 1.22f;
        float height = lineHeight * lines.Length;
        float offset = 4f * labelScale;
        float left = centered ? point.X - width / 2f - padX : point.X + offset;
        float top = centered ? point.Y - height / 2f - padY : point.Y + offset;
        var box = new SKRect(left, top, left + width + padX * 2, top + height + padY * 2);

        using var background = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.Black.WithAlpha(180),
            Style = SKPaintStyle.Fill,
        };
        using var border = new SKPaint
        {
            IsAntialias = true,
            Color = color.WithAlpha(230),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 0.7f * labelScale,
        };
        canvas.DrawRoundRect(box, 2.2f, 2.2f, background);
        canvas.DrawRoundRect(box, 2.2f, 2.2f, border);

        float baseline = top + padY - textPaint.FontMetrics.Ascent;
        foreach (string line in lines)
        {
            canvas.DrawText(line, left + padX, baseline, textPaint);
            baseline += lineHeight;
        }
    }

    private static void DrawJoistLayout(
        SKCanvas canvas,
        Measurement measurement,
        SKColor color,
        PdfExportOptions options,
        bool drawSegments,
        bool drawLabels)
    {
        if (!measurement.JoistEnabled)
            return;

        JoistLayoutResult layout = JoistTakeoffCalculator.Calculate(measurement, measurement.ScaleMetersPerPt);
        if (!layout.HasScale || layout.Count == 0)
            return;

        if (drawSegments)
        {
            using var joistStroke = new SKPaint
            {
                IsAntialias = true,
                Color = color.WithAlpha(225),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 0.85f * ExportStrokeScale(options),
                StrokeCap = SKStrokeCap.Round,
            };
            foreach (JoistSegment segment in layout.Segments)
                canvas.DrawLine(segment.Start, segment.End, joistStroke);
        }

        if (!drawLabels || !ShouldExportJoistLabels(options) || !measurement.JoistShowLabels || layout.Count > 180)
            return;

        float labelScale = ExportLabelScale(options);
        using var labelPaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.Black.WithAlpha(220),
            TextSize = 5.2f * labelScale,
            Typeface = SKTypeface.FromFamilyName("Consolas"),
        };
        using var labelBg = new SKPaint
        {
            Color = SKColors.White.WithAlpha(190),
            Style = SKPaintStyle.Fill,
        };
        foreach (JoistSegment segment in layout.Segments)
        {
            string label = JoistTakeoffCalculator.FormatSegmentLength(segment, UnitMode.Imperial);
            SKPoint mid = new(
                (segment.Start.X + segment.End.X) / 2f,
                (segment.Start.Y + segment.End.Y) / 2f);
            var bounds = new SKRect();
            labelPaint.MeasureText(label, ref bounds);
            float joistPad = 1.2f * labelScale;
            var bg = new SKRect(
                mid.X - bounds.Width / 2f - joistPad,
                mid.Y - bounds.Height / 2f - joistPad,
                mid.X + bounds.Width / 2f + joistPad,
                mid.Y + bounds.Height / 2f + joistPad);
            canvas.DrawRect(bg, labelBg);
            canvas.DrawText(label, bg.Left + joistPad, bg.Bottom - joistPad, labelPaint);
        }
    }
    private static SKColor ParseSkColor(string value, SKColor fallback)
    {
        try
        {
            return SKColor.Parse(NormalizeColor(value));
        }
        catch
        {
            return fallback;
        }
    }

    private static string NormalizeColor(string value)
    {
        string trimmed = string.IsNullOrWhiteSpace(value) ? "#FF4444" : value.Trim();
        return trimmed.StartsWith('#') ? trimmed : "#" + trimmed;
    }
}
