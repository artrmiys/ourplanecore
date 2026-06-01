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
    private void DrawJoistLayout(SKCanvas canvas, Measurement m, SKColor color, bool drawLabels)
    {
        if (!m.JoistEnabled)
            return;

        JoistLayoutResult layout = JoistTakeoffCalculator.Calculate(m, ScaleMetersPerPt);
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
            DrawJoistLayoutLabels(canvas, m);
    }

    private void DrawJoistLayoutLabels(SKCanvas canvas, Measurement measurement)
    {
        if (!ShouldDrawJoistSegmentLabels(measurement))
            return;

        JoistLayoutResult layout = JoistTakeoffCalculator.Calculate(measurement, ScaleMetersPerPt);
        if (!layout.HasScale || layout.Count == 0 || layout.Count > 180)
            return;

        foreach (JoistSegment segment in layout.Segments)
        {
            string label = JoistTakeoffCalculator.FormatSegmentLength(segment, UnitMode);
            SKPoint mid = new(
                (segment.Start.X + segment.End.X) / 2f,
                (segment.Start.Y + segment.End.Y) / 2f);
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

    private bool ShouldDrawJoistSegmentLabels(Measurement measurement) =>
        ShouldDrawJoistLabels() && measurement.JoistEnabled && measurement.JoistShowLabels;
}
