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
    private readonly Dictionary<Measurement, JoistLayoutResult> _paintJoistLayoutCache = [];

    private void ClearPaintJoistLayoutCache() =>
        _paintJoistLayoutCache.Clear();

    private void DrawJoistLayout(SKCanvas canvas, Measurement m, SKColor color, bool drawLabels)
    {
        if (!m.JoistEnabled)
            return;

        JoistLayoutResult layout = PaintJoistLayout(m);
        if (!layout.HasScale || layout.Count == 0)
            return;

        using var joistStroke = new SKPaint
        {
            Color = color.WithAlpha(220),
            StrokeWidth = ScreenToPdfDistance(1.15f * MeasurementStrokeScaleFactor()),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
        };
        foreach (JoistSegment segment in layout.Segments)
        {
            canvas.DrawLine(segment.Start, segment.End, joistStroke);
        }

        if (drawLabels)
            DrawJoistLayoutLabels(canvas, m, layout, GetVisiblePdfRect());
    }

    private void DrawJoistLayoutLabels(SKCanvas canvas, Measurement measurement, SKRect visiblePdf)
    {
        DrawJoistLayoutLabels(canvas, measurement, PaintJoistLayout(measurement), visiblePdf);
    }

    private void DrawJoistLayoutLabels(
        SKCanvas canvas,
        Measurement measurement,
        JoistLayoutResult layout,
        SKRect visiblePdf)
    {
        if (!ShouldDrawJoistSegmentLabels(measurement))
            return;

        if (!layout.HasScale || layout.Count == 0 || layout.Count > 180)
            return;

        foreach (JoistSegment segment in layout.Segments)
        {
            string label = JoistTakeoffCalculator.FormatSegmentLength(segment, UnitMode);
            SKPoint mid = new(
                (segment.Start.X + segment.End.X) / 2f,
                (segment.Start.Y + segment.End.Y) / 2f);
            if (!RectContains(visiblePdf, mid))
                continue;

            DrawScreenTextBox(
                canvas,
                mid,
                [label],
                SKColors.Black.WithAlpha(220),
                SKColors.White.WithAlpha(190),
                SKColors.Transparent,
                JoistSegmentLabelFontScreenPx,
                2f,
                centered: true);
        }
    }

    private JoistLayoutResult PaintJoistLayout(Measurement measurement)
    {
        if (_paintJoistLayoutCache.TryGetValue(measurement, out JoistLayoutResult? layout) &&
            layout != null)
        {
            return layout;
        }

        layout = JoistTakeoffCalculator.Calculate(measurement, ScaleMetersPerPt);
        _paintJoistLayoutCache[measurement] = layout;
        return layout;
    }

    private bool ShouldDrawJoistSegmentLabels(Measurement measurement) =>
        ShouldDrawJoistLabels() && measurement.JoistEnabled && measurement.JoistShowLabels;
}
