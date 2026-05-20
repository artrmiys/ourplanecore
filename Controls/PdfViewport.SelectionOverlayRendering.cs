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
    private void DrawSelectionBounds(SKCanvas canvas, Measurement m)
    {
        if (m.Points.Count == 0) return;

        SKRect bounds = RawMeasurementBounds(m);
        bounds.Inflate(ScreenToPdfDistance(6f), ScreenToPdfDistance(6f));
        using var stroke = new SKPaint
        {
            Color = SKColors.DodgerBlue,
            StrokeWidth = ScreenToPdfDistance(1.5f),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            PathEffect = SKPathEffect.CreateDash([ScreenToPdfDistance(6f), ScreenToPdfDistance(4f)], 0),
        };
        canvas.DrawRect(bounds, stroke);
    }

    private void DrawSelectionHandles(SKCanvas canvas, Measurement m)
    {
        if (m.Points.Count == 0) return;

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

        foreach (MeasurementVertexRef vertex in MeasurementVertices(m))
        {
            var rect = SKRect.Create(vertex.Point.X - radius, vertex.Point.Y - radius, radius * 2, radius * 2);
            bool vertexSelected = ReferenceEquals(_selectedMeasurement, m) &&
                                  vertex.GlobalIndex == _selectedVertexIndex ||
                                  IsMeasurementVertexSelected(m, vertex.GlobalIndex);
            canvas.DrawRect(rect, vertexSelected ? activeFill : fill);
            canvas.DrawRect(rect, stroke);
        }
    }

    private bool ShouldDrawMeasurementHandles(Measurement measurement) =>
        ReferenceEquals(measurement, _selectedMeasurement) ||
        IsMeasurementSelected(measurement) && CanEditMeasurementVertices(measurement);

    private void DrawLabel(SKCanvas canvas, SKPoint pos, string text, string hexColor)
    {
        if (string.IsNullOrEmpty(text)) return;

        string[] lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
            return;

        DrawScreenTextBox(
            canvas,
            pos,
            lines,
            SKColors.White,
            SKColors.Black.WithAlpha(180),
            GetCachedColor(hexColor, SKColors.DodgerBlue),
            MeasurementLabelFontScreenPx,
            MeasurementLabelPaddingScreenPx,
            centered: false);
    }

    private void DrawScreenTextBox(
        SKCanvas canvas,
        SKPoint pdfPos,
        IReadOnlyList<string> lines,
        SKColor textColor,
        SKColor backgroundColor,
        SKColor borderColor,
        float fontSize,
        float pad,
        bool centered)
    {
        if (lines.Count == 0)
            return;

        string[] cleanLines = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim())
            .ToArray();
        if (cleanLines.Length == 0)
            return;

        float safeZoom = Math.Max(_zoom, 0.001f);
        float labelScale = ClampOverlayUserScale(MeasurementLabelScale);
        // When ScaleMeasurementLabelsWithPage is on, labels live in PDF space (relative to fit zoom)
        // so they grow/shrink with page zoom. Otherwise dividing by _zoom keeps screen size constant.
        float labelDivisor = ScaleMeasurementLabelsWithPage
            ? Math.Max(CurrentFitZoom(), 0.001f)
            : safeZoom;
        using var textPaint = new SKPaint
        {
            Color       = textColor,
            TextSize    = fontSize * labelScale / labelDivisor,
            IsAntialias = true,
            Typeface    = LabelTypeface,
        };

        float width = 0;
        foreach (string line in cleanLines)
            width = Math.Max(width, textPaint.MeasureText(line));
        float lineHeight = textPaint.TextSize * 1.22f;
        float textHeight = lineHeight * cleanLines.Length;
        float pdfPad = pad * labelScale / labelDivisor;
        SKRect bg = centered
            ? new SKRect(
                pdfPos.X - width / 2f - pdfPad,
                pdfPos.Y - textHeight / 2f - pdfPad,
                pdfPos.X + width / 2f + pdfPad,
                pdfPos.Y + textHeight / 2f + pdfPad)
            : new SKRect(
                pdfPos.X + pdfPad,
                pdfPos.Y - textHeight - pdfPad,
                pdfPos.X + width + pdfPad * 3,
                pdfPos.Y + pdfPad);

        using var bgPaint = new SKPaint
        {
            Color = backgroundColor,
            Style = SKPaintStyle.Fill,
        };
        using var borderPaint = new SKPaint
        {
            Color       = borderColor,
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = 1f / labelDivisor,
        };
        float radius = 3f / labelDivisor;
        canvas.DrawRoundRect(bg, radius, radius, bgPaint);
        if (borderColor.Alpha > 0)
            canvas.DrawRoundRect(bg, radius, radius, borderPaint);
        float baseline = bg.Top + pdfPad - textPaint.FontMetrics.Ascent;
        foreach (string line in cleanLines)
        {
            float textX = centered ? bg.Left + pdfPad : pdfPos.X + pdfPad * 1.5f;
            canvas.DrawText(line, textX, baseline, textPaint);
            baseline += lineHeight;
        }
    }

    private static string ColorHex(SKColor color) =>
        $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
}
