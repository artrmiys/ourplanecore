using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public static class SheetOverlayRenderer
{
    private static readonly SKTypeface OverlayMonoTypeface =
        SKTypeface.FromFamilyName("Consolas") ??
        SKTypeface.FromFamilyName("Cascadia Mono") ??
        SKTypeface.Default;

    private static readonly SKTypeface OverlayUiTypeface =
        SKTypeface.FromFamilyName("Segoe UI") ??
        SKTypeface.FromFamilyName("Inter") ??
        SKTypeface.Default;

    private static readonly SKTypeface OverlayUiBoldTypeface =
        SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ??
        SKTypeface.FromFamilyName("Inter", SKFontStyle.Bold) ??
        OverlayUiTypeface;

    public static IReadOnlyList<SKRect> GetHeaderBounds(
        float visibleLeft,
        float visibleTop,
        float visibleRight,
        float visibleBottom,
        float pageLeft,
        float pageTop,
        float pageRight,
        float pageBottom,
        string scaleText,
        string sheetSizeText,
        float overlayScale)
    {
        if (visibleRight - visibleLeft < 48 || visibleBottom - visibleTop < 20)
            return [];

        overlayScale = ClampScale(overlayScale);
        float fontSize = 13f * overlayScale;
        float padX = 7f * overlayScale;
        float padY = 4f * overlayScale;
        float margin = 8f * overlayScale;

        using var textPaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = fontSize,
            IsAntialias = true,
            Typeface = OverlayMonoTypeface,
        };

        float lineHeight = textPaint.FontMetrics.Descent - textPaint.FontMetrics.Ascent;
        float boxHeight = lineHeight + padY * 2;
        float y = Math.Max(visibleTop + margin, pageTop + margin);
        y = Math.Min(y, visibleBottom - boxHeight - margin);
        if (y < visibleTop)
            return [];

        float leftX = Math.Max(visibleLeft + margin, pageLeft + margin);
        float scaleWidth = textPaint.MeasureText(scaleText);
        float sizeWidth = textPaint.MeasureText(sheetSizeText);
        float availableWidth = visibleRight - visibleLeft - margin * 2;
        var bounds = new List<SKRect>(2);

        if (scaleWidth + sizeWidth + padX * 4 + 28f <= availableWidth)
        {
            bounds.Add(HeaderBoxBounds(leftX, y, scaleWidth, padX, padY, lineHeight));

            float rightX = Math.Min(
                visibleRight - margin - sizeWidth - padX * 2,
                pageRight - margin - sizeWidth - padX * 2);
            if (rightX > leftX + scaleWidth + padX * 2 + 18f)
                bounds.Add(HeaderBoxBounds(rightX, y, sizeWidth, padX, padY, lineHeight));
        }
        else if (scaleWidth + padX * 2 <= availableWidth)
        {
            bounds.Add(HeaderBoxBounds(leftX, y, scaleWidth, padX, padY, lineHeight));
        }

        return bounds;
    }

    public static bool TryGetLegendBounds(
        IReadOnlyList<SheetLegendEntry> entries,
        float visibleLeft,
        float visibleTop,
        float visibleRight,
        float visibleBottom,
        float pageLeft,
        float pageTop,
        float pageRight,
        float pageBottom,
        string anchor,
        float overlayScale,
        out SKRect bounds)
    {
        bounds = SKRect.Empty;
        if (entries.Count == 0)
            return false;

        float availableWidth = visibleRight - visibleLeft;
        float availableHeight = visibleBottom - visibleTop;
        overlayScale = ClampScale(overlayScale);
        if (availableWidth < Math.Max(96f, 160f * overlayScale) ||
            availableHeight < Math.Max(56f, 90f * overlayScale))
        {
            return false;
        }

        float margin = 8f * overlayScale;
        float pad = 8f * overlayScale;
        float maxDetailLines = Math.Max(0, entries.Max(entry => entry.Details?.Count ?? 0));
        float baseRowHeight = 16f * overlayScale * (1 + Math.Min(maxDetailLines, 6) * 0.82f);
        float titleHeight = 18f * overlayScale;
        float maxBoxWidth = availableWidth - margin * 2;
        float maxBoxHeight = availableHeight - margin * 2;
        float contentHeight = Math.Max(baseRowHeight, maxBoxHeight - pad * 2 - titleHeight);
        float minColumnWidth = 170f * overlayScale;
        int maxColumns = Math.Max(1, Math.Min(entries.Count, (int)(maxBoxWidth / minColumnWidth)));
        int columns = 1;
        for (int candidate = 1; candidate <= maxColumns; candidate++)
        {
            int candidateRows = (int)Math.Ceiling(entries.Count / (double)candidate);
            if (candidateRows * baseRowHeight <= contentHeight)
            {
                columns = candidate;
                break;
            }

            columns = candidate;
        }

        int rowsPerColumn = Math.Max(1, (int)Math.Ceiling(entries.Count / (double)columns));
        float rowHeight = Math.Min(baseRowHeight, contentHeight / rowsPerColumn);
        rowHeight = Math.Max(8f * overlayScale, rowHeight);
        float boxWidth = Math.Min(maxBoxWidth, Math.Max(180f * overlayScale, columns * 220f * overlayScale));
        float boxHeight = Math.Min(maxBoxHeight, pad * 2 + titleHeight + rowsPerColumn * rowHeight);
        SKPoint position = AnchorOverlayBox(
            anchor,
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
        bounds = new SKRect(position.X, position.Y, position.X + boxWidth, position.Y + boxHeight);
        return true;
    }

    public static void DrawHeader(
        SKCanvas canvas,
        float visibleLeft,
        float visibleTop,
        float visibleRight,
        float visibleBottom,
        float pageLeft,
        float pageTop,
        float pageRight,
        float pageBottom,
        string scaleText,
        string sheetSizeText,
        float overlayScale)
    {
        if (visibleRight - visibleLeft < 48 || visibleBottom - visibleTop < 20)
            return;

        overlayScale = ClampScale(overlayScale);
        float fontSize = 13f * overlayScale;
        float padX = 7f * overlayScale;
        float padY = 4f * overlayScale;
        float margin = 8f * overlayScale;

        using var textPaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = fontSize,
            IsAntialias = true,
            Typeface = OverlayMonoTypeface,
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

            float rightX = Math.Min(
                visibleRight - margin - sizeWidth - padX * 2,
                pageRight - margin - sizeWidth - padX * 2);
            if (rightX > leftX + scaleWidth + padX * 2 + 18f)
                DrawHeaderBox(canvas, rightX, y, sheetSizeText, textPaint, bgPaint, borderPaint, padX, padY, lineHeight);
        }
        else if (scaleWidth + padX * 2 <= availableWidth)
        {
            DrawHeaderBox(canvas, leftX, y, scaleText, textPaint, bgPaint, borderPaint, padX, padY, lineHeight);
        }
    }

    public static void DrawLegend(
        SKCanvas canvas,
        IReadOnlyList<SheetLegendEntry> entries,
        float visibleLeft,
        float visibleTop,
        float visibleRight,
        float visibleBottom,
        float pageLeft,
        float pageTop,
        float pageRight,
        float pageBottom,
        string anchor,
        float overlayScale)
    {
        if (entries.Count == 0)
            return;

        float availableWidth = visibleRight - visibleLeft;
        float availableHeight = visibleBottom - visibleTop;
        overlayScale = ClampScale(overlayScale);
        if (availableWidth < Math.Max(96f, 160f * overlayScale) ||
            availableHeight < Math.Max(56f, 90f * overlayScale))
        {
            return;
        }

        float margin = 8f * overlayScale;
        float pad = 8f * overlayScale;
        float baseTitleSize = 12f * overlayScale;
        float baseRowSize = 11f * overlayScale;
        int maxDetailLines = Math.Max(0, entries.Max(entry => entry.Details?.Count ?? 0));
        float baseRowHeight = 16f * overlayScale * (1 + Math.Min(maxDetailLines, 6) * 0.82f);
        float titleHeight = 18f * overlayScale;
        float maxBoxWidth = availableWidth - margin * 2;
        float maxBoxHeight = availableHeight - margin * 2;
        float contentHeight = Math.Max(baseRowHeight, maxBoxHeight - pad * 2 - titleHeight);
        float minColumnWidth = 170f * overlayScale;
        int maxColumns = Math.Max(1, Math.Min(entries.Count, (int)(maxBoxWidth / minColumnWidth)));
        int columns = 1;
        for (int candidate = 1; candidate <= maxColumns; candidate++)
        {
            int candidateRows = (int)Math.Ceiling(entries.Count / (double)candidate);
            if (candidateRows * baseRowHeight <= contentHeight)
            {
                columns = candidate;
                break;
            }

            columns = candidate;
        }

        int rowsPerColumn = Math.Max(1, (int)Math.Ceiling(entries.Count / (double)columns));
        float rowHeight = Math.Min(baseRowHeight, contentHeight / rowsPerColumn);
        float rowScale = Math.Clamp(rowHeight / baseRowHeight, 0.58f, 1f);
        rowHeight = Math.Max(8f * overlayScale, rowHeight);
        float titleSize = baseTitleSize * Math.Clamp(rowScale, 0.75f, 1f);
        float rowSize = baseRowSize * rowScale;
        float boxWidth = Math.Min(maxBoxWidth, Math.Max(180f * overlayScale, columns * 220f * overlayScale));
        float boxHeight = Math.Min(maxBoxHeight, pad * 2 + titleHeight + rowsPerColumn * rowHeight);
        SKPoint position = AnchorOverlayBox(
            anchor,
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

        using var titlePaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = titleSize,
            IsAntialias = true,
            Typeface = OverlayUiBoldTypeface,
        };
        using var textPaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = rowSize,
            IsAntialias = true,
            Typeface = OverlayUiTypeface,
        };
        using var mutedPaint = new SKPaint
        {
            Color = new SKColor(0x44, 0x44, 0x44, 235),
            TextSize = rowSize,
            IsAntialias = true,
            Typeface = OverlayUiTypeface,
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
        string title = entries.Count > 1 ? $"Legend ({entries.Count})" : "Legend";
        canvas.DrawText(title, x + pad, y + pad - titlePaint.FontMetrics.Ascent, titlePaint);

        float columnWidth = (boxWidth - pad * 2) / columns;
        float columnGap = columns > 1 ? 10f * overlayScale : 0f;
        for (int i = 0; i < entries.Count; i++)
        {
            SheetLegendEntry entry = entries[i];
            int column = i / rowsPerColumn;
            int row = i % rowsPerColumn;
            float columnLeft = x + pad + column * columnWidth;
            float columnRight = Math.Min(x + boxWidth - pad, columnLeft + columnWidth - columnGap);
            float rowY = y + pad + titleHeight + row * rowHeight;
            SKColor color = ParseColor(entry.Color, SKColors.Red);

            float baseline = rowY - textPaint.FontMetrics.Ascent;
            float glyphSize = 16f * overlayScale * rowScale;
            float glyphLeft = columnLeft;
            float glyphTop = rowY + Math.Max(2f * overlayScale, (rowHeight - glyphSize) / 2f);
            var glyphBox = new SKRect(glyphLeft, glyphTop, glyphLeft + glyphSize, glyphTop + glyphSize);
            MeasurementGlyph.DrawSkia(canvas, entry.GlyphKind, color, glyphBox);

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

    private static SKRect HeaderBoxBounds(
        float x,
        float y,
        float textWidth,
        float padX,
        float padY,
        float lineHeight) =>
        new(x, y, x + textWidth + padX * 2, y + lineHeight + padY * 2);

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
            "topright" or "middleright" or "bottomright" or "rightcenter" => maxX,
            _ => minX,
        };
        float y = clean switch
        {
            "middleleft" or "middleright" or "leftcenter" or "rightcenter" or "center" => centerY,
            "bottomleft" or "bottomcenter" or "bottomright" => maxY,
            _ => minY,
        };

        return new SKPoint(Math.Clamp(x, minX, maxX), Math.Clamp(y, minY, maxY));
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

    private static float ClampScale(float scale)
    {
        if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0)
            return 1f;

        return Math.Clamp(scale, 0.25f, 6.0f);
    }

    private static SKColor ParseColor(string value, SKColor fallback)
    {
        try
        {
            string clean = string.IsNullOrWhiteSpace(value) ? "#FF4444" : value.Trim();
            return SKColor.Parse(clean.StartsWith('#') ? clean : "#" + clean);
        }
        catch
        {
            return fallback;
        }
    }
}
