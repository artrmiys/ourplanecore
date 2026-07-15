using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace OurPlanCore.Controls;

public static class SheetOverlayRenderer
{
    private sealed record LegendLayout(
        SKRect Bounds,
        float OverlayScale,
        float Pad,
        float TitleHeight,
        float BaseRowSize,
        float RowHeight,
        float RowScale,
        float TitleSize,
        float RowSize,
        int Columns,
        int RowsPerColumn,
        float ColumnGap,
        IReadOnlyList<float> ColumnWidths);

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
        if (!TryCreateLegendLayout(
                entries,
                visibleLeft,
                visibleTop,
                visibleRight,
                visibleBottom,
                pageLeft,
                pageTop,
                pageRight,
                pageBottom,
                anchor,
                overlayScale,
                out LegendLayout layout))
        {
            return false;
        }

        bounds = layout.Bounds;
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
        if (!TryCreateLegendLayout(
                entries,
                visibleLeft,
                visibleTop,
                visibleRight,
                visibleBottom,
                pageLeft,
                pageTop,
                pageRight,
                pageBottom,
                anchor,
                overlayScale,
                out LegendLayout layout))
        {
            return;
        }

        float x = layout.Bounds.Left;
        float y = layout.Bounds.Top;

        using var titlePaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = layout.TitleSize,
            IsAntialias = true,
            Typeface = OverlayUiBoldTypeface,
        };
        using var textPaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = layout.RowSize,
            IsAntialias = true,
            Typeface = OverlayUiTypeface,
        };
        using var mutedPaint = new SKPaint
        {
            Color = new SKColor(0x44, 0x44, 0x44, 235),
            TextSize = layout.RowSize,
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

        canvas.DrawRect(layout.Bounds, bgPaint);
        canvas.DrawRect(layout.Bounds, borderPaint);
        string title = entries.Count > 1 ? $"Legend ({entries.Count})" : "Legend";
        canvas.DrawText(title, x + layout.Pad, y + layout.Pad - titlePaint.FontMetrics.Ascent, titlePaint);

        canvas.Save();
        canvas.ClipRect(layout.Bounds);
        for (int i = 0; i < entries.Count; i++)
        {
            SheetLegendEntry entry = entries[i];
            int column = i / layout.RowsPerColumn;
            int row = i % layout.RowsPerColumn;
            float columnLeft = LegendColumnLeft(layout, column);
            float columnRight = columnLeft + layout.ColumnWidths[column];
            float rowY = y + layout.Pad + layout.TitleHeight + row * layout.RowHeight;
            SKColor color = ParseColor(entry.Color, SKColors.Red);

            float baseline = rowY - textPaint.FontMetrics.Ascent;
            float glyphSize = 16f * layout.OverlayScale * layout.RowScale;
            float glyphLeft = columnLeft;
            float glyphTop = rowY + Math.Max(
                2f * layout.OverlayScale,
                (layout.RowHeight - glyphSize) / 2f);
            var glyphBox = new SKRect(glyphLeft, glyphTop, glyphLeft + glyphSize, glyphTop + glyphSize);
            MeasurementGlyph.DrawSkia(canvas, entry.GlyphKind, color, glyphBox);

            float nameLeft = glyphLeft + glyphSize + 6f * layout.OverlayScale * layout.RowScale;
            float qtyRight = columnRight;
            float availableTextWidth = Math.Max(1f, columnRight - nameLeft);
            string nameValue = (entry.Name ?? "").Trim();
            string quantityValue = (entry.Quantity ?? "").Trim();
            float qtyGap = quantityValue.Length > 0 ? 4f * layout.OverlayScale : 0f;
            float preferredNameWidth = textPaint.MeasureText(nameValue);
            float preferredQtyWidth = mutedPaint.MeasureText(quantityValue);
            float qtyWidth = Math.Min(
                preferredQtyWidth,
                Math.Max(
                    0f,
                    availableTextWidth - Math.Min(preferredNameWidth, 24f) - qtyGap));
            float nameWidth = Math.Max(1f, availableTextWidth - qtyWidth - qtyGap);
            string name = FitText(nameValue, textPaint, nameWidth);
            string qty = FitText(quantityValue, mutedPaint, Math.Max(1f, qtyWidth));
            canvas.DrawText(name, nameLeft, baseline, textPaint);
            if (qty.Length > 0)
                canvas.DrawText(qty, qtyRight - mutedPaint.MeasureText(qty), baseline, mutedPaint);
            if (entry.Details is { Count: > 0 } details)
            {
                float detailBaseline = baseline + layout.BaseRowSize * layout.RowScale * 1.08f;
                foreach (string detail in details.Take(6))
                {
                    canvas.DrawText(
                        FitText(detail, mutedPaint, availableTextWidth),
                        nameLeft,
                        detailBaseline,
                        mutedPaint);
                    detailBaseline += layout.BaseRowSize * layout.RowScale * 1.08f;
                }
            }
        }
        canvas.Restore();
    }

    private static bool TryCreateLegendLayout(
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
        out LegendLayout layout)
    {
        layout = null!;
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
        float baseTitleSize = 12f * overlayScale;
        float baseRowSize = 11f * overlayScale;
        int maxDetailLines = Math.Max(0, entries.Max(entry => entry.Details?.Count ?? 0));
        float baseRowHeight = 16f * overlayScale * (1 + Math.Min(maxDetailLines, 6) * 0.82f);
        float titleHeight = 18f * overlayScale;
        float maxBoxWidth = availableWidth - margin * 2;
        float maxBoxHeight = availableHeight - margin * 2;
        float contentHeight = Math.Max(baseRowHeight, maxBoxHeight - pad * 2 - titleHeight);
        float columnGap = 10f * overlayScale;
        float minColumnWidth = 170f * overlayScale;
        int maxColumns = Math.Max(1, Math.Min(entries.Count, (int)(maxBoxWidth / minColumnWidth)));
        float minimumReadableRowHeight = Math.Max(8f * overlayScale, baseRowHeight * 0.58f);
        int columns = ChooseLegendColumnCount(
            entries.Count,
            maxColumns,
            minimumReadableRowHeight,
            contentHeight);
        int rowsPerColumn = Math.Max(1, (int)Math.Ceiling(entries.Count / (double)columns));
        float rawRowHeight = Math.Min(baseRowHeight, contentHeight / rowsPerColumn);
        float rowScale = Math.Clamp(rawRowHeight / baseRowHeight, 0.58f, 1f);
        float rowHeight = Math.Max(8f * overlayScale, rawRowHeight);
        float titleSize = baseTitleSize * Math.Clamp(rowScale, 0.75f, 1f);
        float rowSize = baseRowSize * rowScale;

        using var titlePaint = new SKPaint
        {
            TextSize = titleSize,
            Typeface = OverlayUiBoldTypeface,
        };
        using var textPaint = new SKPaint
        {
            TextSize = rowSize,
            Typeface = OverlayUiTypeface,
        };

        float[] desiredColumnWidths = MeasureLegendColumnWidths(
            entries,
            columns,
            rowsPerColumn,
            overlayScale,
            rowScale,
            textPaint);
        string title = entries.Count > 1 ? $"Legend ({entries.Count})" : "Legend";
        float measuredBoxWidth = pad * 2 + columnGap * (columns - 1) + desiredColumnWidths.Sum();
        float minimumBoxWidth = Math.Max(180f * overlayScale, columns * 220f * overlayScale);
        float preferredBoxWidth = Math.Max(
            Math.Max(measuredBoxWidth, minimumBoxWidth),
            titlePaint.MeasureText(title) + pad * 2);
        float boxWidth = Math.Min(maxBoxWidth, preferredBoxWidth);
        float availableColumnWidth = Math.Max(
            1f,
            boxWidth - pad * 2 - columnGap * (columns - 1));
        float[] columnWidths = FitLegendColumnWidths(desiredColumnWidths, availableColumnWidth);
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

        layout = new LegendLayout(
            new SKRect(position.X, position.Y, position.X + boxWidth, position.Y + boxHeight),
            overlayScale,
            pad,
            titleHeight,
            baseRowSize,
            rowHeight,
            rowScale,
            titleSize,
            rowSize,
            columns,
            rowsPerColumn,
            columnGap,
            columnWidths);
        return true;
    }

    private static int ChooseLegendColumnCount(
        int entryCount,
        int maxColumns,
        float minimumReadableRowHeight,
        float contentHeight)
    {
        int columns = 1;
        for (int candidate = 1; candidate <= maxColumns; candidate++)
        {
            int candidateRows = (int)Math.Ceiling(entryCount / (double)candidate);
            columns = candidate;
            if (candidateRows * minimumReadableRowHeight <= contentHeight)
                break;
        }

        return columns;
    }

    private static float[] MeasureLegendColumnWidths(
        IReadOnlyList<SheetLegendEntry> entries,
        int columns,
        int rowsPerColumn,
        float overlayScale,
        float rowScale,
        SKPaint textPaint)
    {
        float minimumContentWidth = 154f * overlayScale;
        float glyphSize = 16f * overlayScale * rowScale;
        float nameGap = 6f * overlayScale * rowScale;
        float quantityGap = 4f * overlayScale;
        var widths = Enumerable.Repeat(minimumContentWidth, columns).ToArray();

        for (int i = 0; i < entries.Count; i++)
        {
            SheetLegendEntry entry = entries[i];
            int column = Math.Min(columns - 1, i / rowsPerColumn);
            float nameWidth = textPaint.MeasureText((entry.Name ?? "").Trim());
            float quantityWidth = textPaint.MeasureText((entry.Quantity ?? "").Trim());
            float primaryWidth = glyphSize + nameGap + nameWidth;
            if (quantityWidth > 0)
                primaryWidth += quantityGap + quantityWidth;

            float detailWidth = entry.Details?
                .Take(6)
                .Select(detail => textPaint.MeasureText((detail ?? "").Trim()))
                .DefaultIfEmpty(0f)
                .Max() ?? 0f;
            float requiredWidth = Math.Max(primaryWidth, glyphSize + nameGap + detailWidth);
            widths[column] = Math.Max(widths[column], requiredWidth);
        }

        return widths;
    }

    private static float[] FitLegendColumnWidths(float[] desiredWidths, float availableWidth)
    {
        var fitted = new float[desiredWidths.Length];
        float desiredTotal = desiredWidths.Sum();
        if (desiredTotal <= availableWidth)
        {
            float extra = (availableWidth - desiredTotal) / desiredWidths.Length;
            for (int i = 0; i < desiredWidths.Length; i++)
                fitted[i] = desiredWidths[i] + extra;
            return fitted;
        }

        var remaining = Enumerable.Range(0, desiredWidths.Length).ToList();
        float remainingWidth = availableWidth;
        while (remaining.Count > 0)
        {
            float equalShare = remainingWidth / remaining.Count;
            List<int> naturallyFitting = remaining
                .Where(index => desiredWidths[index] <= equalShare)
                .ToList();
            if (naturallyFitting.Count == 0)
            {
                float activeDesiredTotal = remaining.Sum(index => desiredWidths[index]);
                foreach (int index in remaining)
                    fitted[index] = remainingWidth * desiredWidths[index] / activeDesiredTotal;
                break;
            }

            foreach (int index in naturallyFitting)
            {
                fitted[index] = desiredWidths[index];
                remainingWidth -= desiredWidths[index];
                remaining.Remove(index);
            }
        }

        return fitted;
    }

    private static float LegendColumnLeft(LegendLayout layout, int column)
    {
        float left = layout.Bounds.Left + layout.Pad;
        for (int index = 0; index < column; index++)
            left += layout.ColumnWidths[index] + layout.ColumnGap;
        return left;
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
