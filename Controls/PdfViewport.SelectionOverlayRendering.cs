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
    private const int TextBoxLayoutCacheLimit = 512;
    private readonly Dictionary<TextBoxLayoutCacheKey, TextBoxLayout> _textBoxLayoutCache = [];

    private readonly record struct TextBoxLayoutCacheKey(
        string Text,
        float FontSize,
        float Pad,
        float LabelScale,
        float LabelDivisor);

    private readonly record struct TextBoxLayout(
        string[] Lines,
        float Width,
        float LineHeight,
        float TextHeight,
        float PdfPad,
        float TextSize);

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

        bool drawOnlySelectedCountVertices =
            m.MType == "point" &&
            _selectedMeasurementVertexIndices.TryGetValue(m, out HashSet<int>? selectedCountVertices) &&
            selectedCountVertices.Count > 0;

        foreach (MeasurementVertexRef vertex in MeasurementVertices(m))
        {
            bool vertexSelected = ReferenceEquals(_selectedMeasurement, m) &&
                                  vertex.GlobalIndex == _selectedVertexIndex ||
                                  IsMeasurementVertexSelected(m, vertex.GlobalIndex);
            if (drawOnlySelectedCountVertices && !vertexSelected)
                continue;

            var rect = SKRect.Create(vertex.Point.X - radius, vertex.Point.Y - radius, radius * 2, radius * 2);
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

        if (!IsScreenTextAnchorNearViewport(pdfPos))
            return;

        float safeZoom = Math.Max(_zoom, 0.001f);
        float labelScale = ClampOverlayUserScale(MeasurementLabelScale);
        // When ScaleMeasurementLabelsWithPage is on, labels live in PDF space (relative to fit zoom)
        // so they grow/shrink with page zoom. Otherwise dividing by _zoom keeps screen size constant.
        float labelDivisor = ScaleMeasurementLabelsWithPage
            ? Math.Max(CurrentFitZoom(), 0.001f)
            : safeZoom;
        TextBoxLayout layout = ResolveTextBoxLayout(lines, fontSize, pad, labelScale, labelDivisor, textColor);
        if (layout.Lines.Length == 0)
            return;

        using var textPaint = new SKPaint
        {
            Color       = textColor,
            TextSize    = layout.TextSize,
            IsAntialias = true,
            Typeface    = LabelTypeface,
        };

        SKRect bg = centered
            ? new SKRect(
                pdfPos.X - layout.Width / 2f - layout.PdfPad,
                pdfPos.Y - layout.TextHeight / 2f - layout.PdfPad,
                pdfPos.X + layout.Width / 2f + layout.PdfPad,
                pdfPos.Y + layout.TextHeight / 2f + layout.PdfPad)
            : new SKRect(
                pdfPos.X + layout.PdfPad,
                pdfPos.Y - layout.TextHeight - layout.PdfPad,
                pdfPos.X + layout.Width + layout.PdfPad * 3,
                pdfPos.Y + layout.PdfPad);
        if (!IsPdfRectNearViewport(bg))
            return;

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
        float baseline = bg.Top + layout.PdfPad - textPaint.FontMetrics.Ascent;
        foreach (string line in layout.Lines)
        {
            float textX = centered ? bg.Left + layout.PdfPad : pdfPos.X + layout.PdfPad * 1.5f;
            canvas.DrawText(line, textX, baseline, textPaint);
            baseline += layout.LineHeight;
        }
    }

    private TextBoxLayout ResolveTextBoxLayout(
        IReadOnlyList<string> lines,
        float fontSize,
        float pad,
        float labelScale,
        float labelDivisor,
        SKColor textColor)
    {
        string text = NormalizeTextBoxLines(lines);
        if (text.Length == 0)
            return new TextBoxLayout([], 0, 0, 0, 0, 0);

        var key = new TextBoxLayoutCacheKey(
            text,
            MathF.Round(fontSize, 3),
            MathF.Round(pad, 3),
            MathF.Round(labelScale, 3),
            MathF.Round(labelDivisor, 3));
        if (_textBoxLayoutCache.TryGetValue(key, out TextBoxLayout cached))
            return cached;

        string[] cleanLines = text.Split('\n');
        float textSize = fontSize * labelScale / labelDivisor;
        using var textPaint = new SKPaint
        {
            Color = textColor,
            TextSize = textSize,
            IsAntialias = true,
            Typeface = LabelTypeface,
        };

        float width = 0;
        foreach (string line in cleanLines)
            width = Math.Max(width, textPaint.MeasureText(line));
        float lineHeight = textSize * 1.22f;
        float pdfPad = pad * labelScale / labelDivisor;
        var layout = new TextBoxLayout(
            cleanLines,
            width,
            lineHeight,
            lineHeight * cleanLines.Length,
            pdfPad,
            textSize);

        if (_textBoxLayoutCache.Count >= TextBoxLayoutCacheLimit)
            _textBoxLayoutCache.Clear();
        _textBoxLayoutCache[key] = layout;
        return layout;
    }

    private static string NormalizeTextBoxLines(IReadOnlyList<string> lines)
    {
        return string.Join(
            '\n',
            lines
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim()));
    }

    private bool IsScreenTextAnchorNearViewport(SKPoint pdfPos)
    {
        float sx = (pdfPos.X - _panX) * _zoom;
        float sy = (pdfPos.Y - _panY) * _zoom;
        const float margin = 360f;
        return sx >= -margin &&
               sy >= -margin &&
               sx <= ViewportCanvasWidth + margin &&
               sy <= ViewportCanvasHeight + margin;
    }

    private bool IsPdfRectNearViewport(SKRect rect)
    {
        SKRect visible = GetVisiblePdfRect(360f);
        return rect.Left <= visible.Right &&
               rect.Right >= visible.Left &&
               rect.Top <= visible.Bottom &&
               rect.Bottom >= visible.Top;
    }

    private static string ColorHex(SKColor color) =>
        $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
}
