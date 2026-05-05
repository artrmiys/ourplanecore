using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Docnet.Core;
using Docnet.Core.Models;
using SmartTakeoffs;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace SmartTakeoffs.Controls;

public sealed partial class PdfViewport
{
    private void DrawSheetHeaderOverlay(SKCanvas canvas, float canvasWidth, float canvasHeight)
    {
        if (_pdfW <= 0 || _pdfH <= 0 || _zoom <= 0 || canvasWidth <= 0 || canvasHeight <= 0)
            return;

        float pageLeft = -_panX * _zoom;
        float pageTop = -_panY * _zoom;
        float pageRight = (_pdfW - _panX) * _zoom;
        float pageBottom = (_pdfH - _panY) * _zoom;
        float visibleLeft = Math.Max(0, pageLeft);
        float visibleTop = Math.Max(0, pageTop);
        float visibleRight = Math.Min(canvasWidth, pageRight);
        float visibleBottom = Math.Min(canvasHeight, pageBottom);

        if (visibleRight - visibleLeft < 48 || visibleBottom - visibleTop < 20)
            return;

        float overlayScale = HeaderOverlayScale();
        float fontSize = 13f * overlayScale;
        float padX = 7f * overlayScale;
        float padY = 4f * overlayScale;
        float margin = 8f * overlayScale;

        string scaleText = FormatSheetScale();
        string sheetSizeText = FormatSheetSize();

        SKTypeface monoTypeface = SKTypeface.FromFamilyName("Consolas")
                                   ?? SKTypeface.FromFamilyName("Cascadia Mono")
                                   ?? SKTypeface.Default;
        using var textPaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = fontSize,
            IsAntialias = true,
            Typeface = monoTypeface,
        };
        using var bgPaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(232),
            Style = SKPaintStyle.Fill,
        };
        using var borderPaint = new SKPaint
        {
            Color = new SKColor(0x30, 0x30, 0x30, 220),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
        };

        float lineHeight = textPaint.FontMetrics.Descent - textPaint.FontMetrics.Ascent;
        float boxHeight = lineHeight + padY * 2;
        float y = Math.Max(visibleTop + margin, pageTop + margin);
        y = Math.Min(y, visibleBottom - boxHeight - margin);
        if (y < visibleTop)
            return;

        float leftX = Math.Max(visibleLeft + margin, pageLeft + margin);
        float scaleWidth = textPaint.MeasureText(scaleText);
        float sizeWidth = textPaint.MeasureText(sheetSizeText);
        float availableWidth = visibleRight - visibleLeft - margin * 2;

        if (scaleWidth + sizeWidth + padX * 4 + 28f <= availableWidth)
        {
            DrawHeaderBox(canvas, leftX, y, scaleText, textPaint, bgPaint, borderPaint, padX, padY, lineHeight);

            float rightX = Math.Min(visibleRight - margin - sizeWidth - padX * 2, pageRight - margin - sizeWidth - padX * 2);
            if (rightX > leftX + scaleWidth + padX * 2 + 18f)
                DrawHeaderBox(canvas, rightX, y, sheetSizeText, textPaint, bgPaint, borderPaint, padX, padY, lineHeight);
        }
        else if (scaleWidth + padX * 2 <= availableWidth)
        {
            DrawHeaderBox(canvas, leftX, y, scaleText, textPaint, bgPaint, borderPaint, padX, padY, lineHeight);
        }
    }

    private static void DrawHeaderBox(
        SKCanvas canvas,
        float x,
        float y,
        string text,
        SKPaint textPaint,
        SKPaint bgPaint,
        SKPaint borderPaint,
        float padX,
        float padY,
        float lineHeight)
    {
        float textWidth = textPaint.MeasureText(text);
        var rect = new SKRect(x, y, x + textWidth + padX * 2, y + lineHeight + padY * 2);
        canvas.DrawRect(rect, bgPaint);
        canvas.DrawRect(rect, borderPaint);
        canvas.DrawText(text, x + padX, y + padY - textPaint.FontMetrics.Ascent, textPaint);
    }

    private void DrawSheetLegendOverlay(SKCanvas canvas, float canvasWidth, float canvasHeight)
    {
        if (_sheetLegendEntries.Count == 0 ||
            _pdfW <= 0 ||
            _pdfH <= 0 ||
            _zoom <= 0 ||
            canvasWidth <= 0 ||
            canvasHeight <= 0)
        {
            return;
        }

        float pageLeft = -_panX * _zoom;
        float pageTop = -_panY * _zoom;
        float pageRight = (_pdfW - _panX) * _zoom;
        float pageBottom = (_pdfH - _panY) * _zoom;
        float visibleLeft = Math.Max(0, pageLeft);
        float visibleTop = Math.Max(0, pageTop);
        float visibleRight = Math.Min(canvasWidth, pageRight);
        float visibleBottom = Math.Min(canvasHeight, pageBottom);

        float availableWidth = visibleRight - visibleLeft;
        float availableHeight = visibleBottom - visibleTop;
        float overlayScale = LegendOverlayScale();
        if (availableWidth < Math.Max(96f, 160f * overlayScale) ||
            availableHeight < Math.Max(56f, 90f * overlayScale))
            return;

        float margin = 8f * overlayScale;
        float pad = 8f * overlayScale;
        float baseTitleSize = 12f * overlayScale;
        float baseRowSize = 11f * overlayScale;
        int maxDetailLines = Math.Max(0, _sheetLegendEntries.Max(entry => entry.Details?.Count ?? 0));
        float baseRowHeight = 16f * overlayScale * (1 + Math.Min(maxDetailLines, 6) * 0.82f);
        float titleHeight = 18f * overlayScale;
        float maxBoxWidth = availableWidth - margin * 2;
        float maxBoxHeight = availableHeight - margin * 2;
        float contentHeight = Math.Max(baseRowHeight, maxBoxHeight - pad * 2 - titleHeight);
        float minColumnWidth = 170f * overlayScale;
        int maxColumns = Math.Max(1, Math.Min(_sheetLegendEntries.Count, (int)(maxBoxWidth / minColumnWidth)));
        int columns = 1;
        for (int candidate = 1; candidate <= maxColumns; candidate++)
        {
            int candidateRows = (int)Math.Ceiling(_sheetLegendEntries.Count / (double)candidate);
            if (candidateRows * baseRowHeight <= contentHeight)
            {
                columns = candidate;
                break;
            }

            columns = candidate;
        }

        int rowsPerColumn = Math.Max(1, (int)Math.Ceiling(_sheetLegendEntries.Count / (double)columns));
        float rowHeight = Math.Min(baseRowHeight, contentHeight / rowsPerColumn);
        float rowScale = Math.Clamp(rowHeight / baseRowHeight, 0.58f, 1f);
        rowHeight = Math.Max(8f * overlayScale, rowHeight);
        float titleSize = baseTitleSize * Math.Clamp(rowScale, 0.75f, 1f);
        float rowSize = baseRowSize * rowScale;
        float boxWidth = Math.Min(maxBoxWidth, Math.Max(180f * overlayScale, columns * 220f * overlayScale));
        float boxHeight = Math.Min(maxBoxHeight, pad * 2 + titleHeight + rowsPerColumn * rowHeight);
        SKPoint position = AnchorOverlayBox(
            SheetLegendAnchor,
            boxWidth,
            boxHeight,
            visibleLeft,
            visibleTop,
            visibleRight,
            visibleBottom,
            pageLeft,
            pageTop,
            pageRight,
            pageBottom,
            margin);
        float x = position.X;
        float y = position.Y;

        SKTypeface uiTypeface = SKTypeface.FromFamilyName("Segoe UI")
                                 ?? SKTypeface.FromFamilyName("Inter")
                                 ?? SKTypeface.Default;
        SKTypeface uiBoldTypeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
                                     ?? SKTypeface.FromFamilyName("Inter", SKFontStyle.Bold)
                                     ?? uiTypeface;

        using var titlePaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = titleSize,
            IsAntialias = true,
            Typeface = uiBoldTypeface,
        };
        using var textPaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = rowSize,
            IsAntialias = true,
            Typeface = uiTypeface,
        };
        using var mutedPaint = new SKPaint
        {
            Color = new SKColor(0x44, 0x44, 0x44, 235),
            TextSize = rowSize,
            IsAntialias = true,
            Typeface = uiTypeface,
        };
        using var bgPaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(238),
            Style = SKPaintStyle.Fill,
        };
        using var borderPaint = new SKPaint
        {
            Color = new SKColor(0x30, 0x30, 0x30, 220),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
        };

        var box = new SKRect(x, y, x + boxWidth, y + boxHeight);
        canvas.DrawRect(box, bgPaint);
        canvas.DrawRect(box, borderPaint);
        string title = _sheetLegendEntries.Count > 1
            ? $"Legend ({_sheetLegendEntries.Count})"
            : "Legend";
        canvas.DrawText(title, x + pad, y + pad - titlePaint.FontMetrics.Ascent, titlePaint);

        float columnWidth = (boxWidth - pad * 2) / columns;
        float columnGap = columns > 1 ? 10f * overlayScale : 0f;
        for (int i = 0; i < _sheetLegendEntries.Count; i++)
        {
            SheetLegendEntry entry = _sheetLegendEntries[i];
            int column = i / rowsPerColumn;
            int row = i % rowsPerColumn;
            float columnLeft = x + pad + column * columnWidth;
            float columnRight = Math.Min(x + boxWidth - pad, columnLeft + columnWidth - columnGap);
            float rowY = y + pad + titleHeight + row * rowHeight;
            SKColor color = GetCachedColor(entry.Color, SKColors.Red);

            float baseline = rowY - textPaint.FontMetrics.Ascent;

            // Single colored glyph (no separate colored square).
            float glyphSize = 16f * overlayScale * rowScale;
            float glyphLeft = columnLeft;
            var glyphBox = new SKRect(
                glyphLeft,
                rowY + Math.Max(2f * overlayScale, (rowHeight - glyphSize) / 2f),
                glyphLeft + glyphSize,
                rowY + Math.Max(2f * overlayScale, (rowHeight - glyphSize) / 2f) + glyphSize);
            DrawLegendSignIcon(canvas, entry.Sign, glyphBox, color);

            float nameLeft = glyphLeft + glyphSize + 6f * overlayScale * rowScale;
            float qtyRight = columnRight;
            float qtyWidth = Math.Min(76f * overlayScale * rowScale, columnWidth * 0.38f);
            float nameRight = qtyRight - qtyWidth - 4f * overlayScale;
            string name = FitText(entry.Name, textPaint, Math.Max(24f, nameRight - nameLeft));
            string qty = FitText(entry.Quantity, mutedPaint, Math.Max(24f, qtyWidth));
            canvas.DrawText(name, nameLeft, baseline, textPaint);
            canvas.DrawText(qty, qtyRight - mutedPaint.MeasureText(qty), baseline, mutedPaint);
            if (entry.Details is { Count: > 0 } details)
            {
                float detailBaseline = baseline + baseRowSize * rowScale * 1.08f;
                foreach (string detail in details.Take(6))
                {
                    canvas.DrawText(
                        FitText(detail, mutedPaint, Math.Max(24f, columnRight - nameLeft)),
                        nameLeft,
                        detailBaseline,
                        mutedPaint);
                    detailBaseline += baseRowSize * rowScale * 1.08f;
                }
            }
        }
    }

    private static void DrawLegendSignIcon(SKCanvas canvas, string sign, SKRect box, SKColor color)
    {
        MeasurementGlyph.DrawSkia(canvas, MeasurementGlyph.FromSign(sign), color, box);
    }

    private static string FitText(string text, SKPaint paint, float maxWidth)
    {
        string value = (text ?? "").Trim();
        if (value.Length == 0 || paint.MeasureText(value) <= maxWidth)
            return value;

        const string suffix = "...";
        float suffixWidth = paint.MeasureText(suffix);
        if (suffixWidth >= maxWidth)
            return suffix;

        int keep = value.Length;
        while (keep > 1 && paint.MeasureText(value[..keep]) + suffixWidth > maxWidth)
            keep--;
        return value[..keep].TrimEnd() + suffix;
    }

    private float HeaderOverlayScale() =>
        ClampOverlayUserScale(SheetHeaderScale) * SheetZoomOverlayScale(ScaleSheetHeaderWithPage);

    private float LegendOverlayScale() =>
        ClampOverlayUserScale(SheetLegendScale) * SheetZoomOverlayScale(ScaleSheetOverlaysWithPage);

    private float SheetZoomOverlayScale(bool enabled)
    {
        if (!enabled)
            return 1f;

        float fitZoom = CurrentFitZoom();
        if (fitZoom <= 0)
            return 1f;

        return Math.Clamp(_zoom / fitZoom, 0.35f, 4.0f);
    }

    private float CurrentFitZoom()
    {
        if (_pdfW <= 0 || _pdfH <= 0 || ActualWidth < 2 || ActualHeight < 2)
            return 0;

        return (float)Math.Min(ActualWidth / _pdfW, ActualHeight / _pdfH) * 0.95f;
    }

    private static float ClampOverlayUserScale(double scale)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            return 1f;

        return (float)Math.Clamp(scale, 0.50, 3.00);
    }

    private static SKPoint AnchorOverlayBox(
        string anchor,
        float width,
        float height,
        float visibleLeft,
        float visibleTop,
        float visibleRight,
        float visibleBottom,
        float pageLeft,
        float pageTop,
        float pageRight,
        float pageBottom,
        float margin)
    {
        float minX = Math.Max(visibleLeft + margin, pageLeft + margin);
        float maxX = Math.Min(visibleRight - margin - width, pageRight - margin - width);
        float minY = Math.Max(visibleTop + margin, pageTop + margin);
        float maxY = Math.Min(visibleBottom - margin - height, pageBottom - margin - height);
        if (maxX < minX)
            maxX = minX;
        if (maxY < minY)
            maxY = minY;

        string clean = (anchor ?? "").Trim().ToLowerInvariant();
        float centerX = (minX + maxX) / 2f;
        float centerY = (minY + maxY) / 2f;

        float x = clean switch
        {
            "topcenter" or "bottomcenter" => centerX,
            "topright" or "middleright" or "bottomright" => maxX,
            _ => minX,
        };
        float y = clean switch
        {
            "middleleft" or "middleright" => centerY,
            "bottomleft" or "bottomcenter" or "bottomright" => maxY,
            _ => minY,
        };

        return new SKPoint(Math.Clamp(x, minX, maxX), Math.Clamp(y, minY, maxY));
    }

    private string FormatSheetScale()
    {
        if (ScaleMetersPerPt <= 0)
            return "Scale: not set";

        double ratio = ScaleMetersPerPt / PdfPointMeters;
        string architectural = FormatArchitecturalScale(ratio);
        return string.IsNullOrWhiteSpace(architectural)
            ? $"Scale: 1:{ratio:F0}"
            : $"Scale: {architectural}";
    }

    private string FormatSheetSize()
    {
        double widthIn = _pdfW / 72.0;
        double heightIn = _pdfH / 72.0;
        return $"{widthIn:F2} x {heightIn:F2}";
    }

    private static string FormatArchitecturalScale(double ratio)
    {
        if (ratio <= 0)
            return "";

        (double Ratio, string Label)[] presets =
        [
            (4,   "3\" = 1' 0\""),
            (8,   "1-1/2\" = 1' 0\""),
            (12,  "1\" = 1' 0\""),
            (16,  "3/4\" = 1' 0\""),
            (24,  "1/2\" = 1' 0\""),
            (32,  "3/8\" = 1' 0\""),
            (48,  "1/4\" = 1' 0\""),
            (64,  "3/16\" = 1' 0\""),
            (96,  "1/8\" = 1' 0\""),
            (128, "3/32\" = 1' 0\""),
            (192, "1/16\" = 1' 0\""),
        ];

        foreach (var preset in presets)
        {
            if (Math.Abs(ratio - preset.Ratio) <= 0.25)
                return preset.Label;
        }

        return "";
    }

}
