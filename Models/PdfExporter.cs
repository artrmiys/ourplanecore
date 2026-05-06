using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

public sealed record PdfExportOptions(
    bool IncludeMeasurements,
    bool IncludeAnnotations,
    bool IncludeLegend,
    UnitMode UnitMode,
    string LegendAnchor,
    double LegendScale);

public sealed record PdfExportPageInput(
    PageInfo Page,
    IReadOnlyList<PdfExportTakeoffInput> Takeoffs,
    IReadOnlyList<PageAnnotation> Annotations);

public sealed record PdfExportTakeoffInput(
    TakeoffItem Item,
    IReadOnlyList<Measurement> Measurements);

public delegate (bool Ok, string Error) PdfSheetOverlayExportRenderer(
    SKCanvas canvas,
    PageInfo page,
    float pageWidthPt,
    float pageHeightPt);

public static class PdfExporter
{
    public static (bool Ok, string Error) TryExport(
        IReadOnlyList<PdfExportPageInput> pages,
        string outputPath,
        PdfExportOptions options,
        PdfSheetOverlayExportRenderer? overlayRenderer = null)
    {
        try
        {
            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            using var stream = File.Create(outputPath);
            using var document = SKDocument.CreatePdf(stream);
            if (document == null)
                return (false, "Could not create PDF document.");

            foreach (PdfExportPageInput input in pages)
            {
                PageInfo page = input.Page;
                if (!File.Exists(page.PdfPath))
                    return (false, $"Source PDF not found for sheet '{page.Name}': {page.PdfPath}");

                var layerStates = page.PdfLayers
                    .GroupBy(layer => layer.Number)
                    .ToDictionary(group => group.Key, group => group.First().IsOn);
                if (!PdfLayerRenderService.TryRender(
                        page.PdfPath,
                        page.PdfPage,
                        renderScale: 2.0,
                        layerStates,
                        highlightedLayers: [],
                        page.PdfLayersCached ? page.PdfLayers : null,
                        out PdfLayerRenderResult render,
                        out string renderError))
                {
                    return (false, $"Could not render sheet '{page.Name}': {renderError}");
                }

                using SKBitmap? bitmap = SKBitmap.Decode(render.ImageBytes);
                if (bitmap == null)
                    return (false, $"Could not decode rendered sheet '{page.Name}'.");

                SKCanvas canvas = document.BeginPage(render.WidthPt, render.HeightPt);
                canvas.Clear(SKColors.White);
                canvas.DrawBitmap(bitmap, new SKRect(0, 0, render.WidthPt, render.HeightPt));

                if (overlayRenderer != null)
                {
                    (bool overlayOk, string overlayError) = overlayRenderer(canvas, page, render.WidthPt, render.HeightPt);
                    if (!overlayOk)
                        return (false, overlayError);
                }

                if (options.IncludeMeasurements)
                    DrawMeasurements(canvas, input.Takeoffs, page, options.UnitMode);
                if (options.IncludeAnnotations)
                    DrawAnnotations(canvas, input.Annotations, page.ScaleMetersPerPt, options.UnitMode);
                if (options.IncludeLegend)
                    DrawLegend(canvas, render.WidthPt, render.HeightPt, input.Takeoffs, page, options);

                document.EndPage();
            }

            document.Close();
            return (true, "");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "PDF export failed.");
            return (false, ex.Message);
        }
    }

    private static void DrawMeasurements(
        SKCanvas canvas,
        IReadOnlyList<PdfExportTakeoffInput> takeoffs,
        PageInfo page,
        UnitMode unitMode)
    {
        foreach (PdfExportTakeoffInput takeoff in takeoffs)
        {
            SKColor color = ParseSkColor(takeoff.Item.Color, SKColors.Red);
            foreach (Measurement measurement in takeoff.Measurements)
                DrawMeasurement(canvas, measurement, color, page.ScaleMetersPerPt, unitMode);
        }
    }

    private static void DrawAnnotations(
        SKCanvas canvas,
        IReadOnlyList<PageAnnotation> annotations,
        double pageScaleMetersPerPt,
        UnitMode unitMode)
    {
        foreach (PageAnnotation annotation in annotations)
        {
            string kind = OurPlaneCoreJobStore.NormalizePageAnnotationKind(annotation.Kind);
            if (annotation.Points.Count < 2)
                continue;

            SKColor color = ParseSkColor(annotation.Color, new SKColor(0x15, 0x65, 0xC0));
            using var stroke = new SKPaint
            {
                IsAntialias = true,
                Color = color,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.35f,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
            };

            SKPoint start = annotation.Points[0];
            SKPoint end = annotation.Points[1];
            if (kind == "rectangle")
            {
                canvas.DrawRect(MeasurementGeometry.NormalizeRect(start, end), stroke);
                continue;
            }

            canvas.DrawLine(start, end, stroke);
            if (kind == "arrow")
            {
                AnnotationGlyphRenderer.DrawArrowHead(canvas, start, end, stroke, 8.0f);
                continue;
            }

            if (kind == "dimension")
            {
                AnnotationGlyphRenderer.DrawDimensionTicks(canvas, start, end, stroke, 5.5f);
                double scale = annotation.ScaleMetersPerPt > 0
                    ? annotation.ScaleMetersPerPt
                    : pageScaleMetersPerPt;
                string label = string.IsNullOrWhiteSpace(annotation.Text)
                    ? FormatAnnotationLength(start, end, scale, unitMode)
                    : annotation.Text;
                DrawMeasurementLabel(
                    canvas,
                    new SKPoint((start.X + end.X) / 2f, (start.Y + end.Y) / 2f),
                    label,
                    color,
                    centered: true);
            }
        }
    }

    private static string FormatAnnotationLength(SKPoint start, SKPoint end, double scaleMetersPerPt, UnitMode unitMode)
    {
        float lengthPt = MeasurementGeometry.Distance(start, end);
        return AnnotationGlyphRenderer.FormatLength(lengthPt, (float)scaleMetersPerPt, unitMode);
    }

    private static void DrawMeasurement(
        SKCanvas canvas,
        Measurement measurement,
        SKColor color,
        double pageScaleMetersPerPt,
        UnitMode unitMode)
    {
        string type = OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType);
        if (measurement.Points.Count == 0)
            return;

        using var stroke = new SKPaint
        {
            IsAntialias = true,
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.4f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };

        if (type == "area" && measurement.Points.Count >= 3)
        {
            using var path = BuildPdfExportAreaPath(measurement);

            using var fill = new SKPaint
            {
                IsAntialias = true,
                Color = color.WithAlpha(38),
                Style = SKPaintStyle.Fill,
            };
            canvas.DrawPath(path, fill);
            canvas.DrawPath(path, stroke);
            DrawJoistLayout(canvas, measurement, color);
            DrawMeasurementLabel(canvas, MeasurementLabelPoint(measurement), measurement.Label(pageScaleMetersPerPt, unitMode), color, centered: true);
            return;
        }

        if (type == "line" && measurement.Points.Count >= 2)
        {
            for (int i = 1; i < measurement.Points.Count; i++)
                canvas.DrawLine(measurement.Points[i - 1], measurement.Points[i], stroke);
            DrawMeasurementLabel(canvas, measurement.Points[^1], measurement.Label(pageScaleMetersPerPt, unitMode), color, centered: false);
            return;
        }

        using var pointFill = new SKPaint
        {
            IsAntialias = true,
            Color = color,
            Style = SKPaintStyle.Fill,
        };
        using var pointStroke = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 0.9f,
        };
        foreach (SKPoint point in measurement.Points)
        {
            canvas.DrawCircle(point, 3.8f, pointFill);
            canvas.DrawCircle(point, 3.8f, pointStroke);
        }
    }

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
        bool centered)
    {
        if (string.IsNullOrWhiteSpace(label))
            return;

        string[] lines = label
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
            return;

        const float textSize = 7.5f;
        const float padX = 2.8f;
        const float padY = 1.8f;
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
        float left = centered ? point.X - width / 2f - padX : point.X + 4f;
        float top = centered ? point.Y - height / 2f - padY : point.Y + 4f;
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
            StrokeWidth = 0.7f,
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

    private static void DrawJoistLayout(SKCanvas canvas, Measurement measurement, SKColor color)
    {
        if (!measurement.JoistEnabled)
            return;

        JoistLayoutResult layout = JoistTakeoffCalculator.Calculate(measurement, measurement.ScaleMetersPerPt);
        if (!layout.HasScale || layout.Count == 0)
            return;

        using var joistStroke = new SKPaint
        {
            IsAntialias = true,
            Color = color.WithAlpha(225),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 0.85f,
            StrokeCap = SKStrokeCap.Round,
        };
        foreach (JoistSegment segment in layout.Segments)
            canvas.DrawLine(segment.Start, segment.End, joistStroke);

        if (!measurement.JoistShowLabels || layout.Count > 180)
            return;

        using var labelPaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.Black.WithAlpha(220),
            TextSize = 5.2f,
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
            var bg = new SKRect(
                mid.X - bounds.Width / 2f - 1.2f,
                mid.Y - bounds.Height / 2f - 1.2f,
                mid.X + bounds.Width / 2f + 1.2f,
                mid.Y + bounds.Height / 2f + 1.2f);
            canvas.DrawRect(bg, labelBg);
            canvas.DrawText(label, bg.Left + 1.2f, bg.Bottom - 1.2f, labelPaint);
        }
    }

    private static void DrawLegend(
        SKCanvas canvas,
        float width,
        float height,
        IReadOnlyList<PdfExportTakeoffInput> takeoffs,
        PageInfo page,
        PdfExportOptions options)
    {
        var entries = takeoffs
            .Select(takeoff =>
            {
                if (takeoff.Measurements.Count == 0)
                    return null;

                TakeoffItem item = takeoff.Item;
                return new SheetLegendEntry(
                    item.Color,
                    item.Name,
                    SheetLegendQuantityTextForPage(item, takeoff.Measurements, page, options.UnitMode),
                    SheetLegendTypeTitle(item),
                    SheetLegendTypeSign(item),
                    []);
            })
            .Where(entry => entry != null)
            .Cast<SheetLegendEntry>()
            .ToList();
        if (entries.Count == 0)
            return;

        float scale = (float)Math.Clamp(options.LegendScale, 0.65, 2.0) * 2f;
        float textSize = 7.5f * scale;
        int maxDetailLines = Math.Max(0, entries.Max(entry => entry.Details?.Count ?? 0));
        float rowHeight = 11.0f * scale * (1 + Math.Min(maxDetailLines, 6) * 0.82f);
        float titleHeight = 13.0f * scale;
        float padding = 6.0f * scale;
        float swatch = 6.5f * scale;
        float maxBoxWidth = Math.Min(width * 0.58f, 310.0f * scale);
        float columnWidth = Math.Max(120.0f * scale, Math.Min(maxBoxWidth, 190.0f * scale));
        int columns = Math.Max(1, Math.Min(entries.Count, (int)Math.Floor(maxBoxWidth / columnWidth)));
        int rowsPerColumn = Math.Max(1, (int)Math.Ceiling(entries.Count / (double)columns));
        float boxWidth = padding * 2 + columns * columnWidth;
        float boxHeight = padding * 2 + titleHeight + rowsPerColumn * rowHeight;
        (float x, float y) = LegendBoxOrigin(width, height, boxWidth, boxHeight, options.LegendAnchor);

        using var background = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White.WithAlpha(226),
            Style = SKPaintStyle.Fill,
        };
        using var border = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(30, 41, 59, 185),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 0.8f,
        };
        canvas.DrawRoundRect(new SKRect(x, y, x + boxWidth, y + boxHeight), 3, 3, background);
        canvas.DrawRoundRect(new SKRect(x, y, x + boxWidth, y + boxHeight), 3, 3, border);

        using var titlePaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(15, 23, 42),
            TextSize = textSize,
        };
        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(15, 23, 42),
            TextSize = textSize,
        };
        canvas.DrawText(entries.Count > 1 ? $"Legend ({entries.Count})" : "Legend", x + padding, y + padding + textSize, titlePaint);

        for (int i = 0; i < entries.Count; i++)
        {
            int column = i / rowsPerColumn;
            int row = i % rowsPerColumn;
            SheetLegendEntry entry = entries[i];
            float rowX = x + padding + column * columnWidth;
            float rowY = y + padding + titleHeight + row * rowHeight;
            using var swatchPaint = new SKPaint
            {
                IsAntialias = true,
                Color = ParseSkColor(entry.Color, SKColors.Red),
                Style = SKPaintStyle.Fill,
            };
            canvas.DrawRect(new SKRect(rowX, rowY + 2, rowX + swatch, rowY + 2 + swatch), swatchPaint);
            float sign = 7.5f * scale;
            float signX = rowX + swatch + 4 * scale;
            DrawLegendSignIcon(canvas, entry.Sign, new SKRect(signX, rowY + 1.8f * scale, signX + sign, rowY + 1.8f * scale + sign), textPaint.Color);
            string text = $"{entry.Name}  {entry.Quantity}";
            canvas.DrawText(TrimLegendText(text, columnWidth - sign - 7 * scale, textPaint), signX + sign + 4 * scale, rowY + textSize, textPaint);
            if (entry.Details is { Count: > 0 } details)
            {
                float detailY = rowY + textSize * 2.05f;
                foreach (string detail in details.Take(6))
                {
                    canvas.DrawText(
                        TrimLegendText(detail, columnWidth - swatch - 4 * scale, textPaint),
                        signX + sign + 4 * scale,
                        detailY,
                        textPaint);
                    detailY += textSize * 1.15f;
                }
            }
        }
    }

    private static void DrawLegendSignIcon(SKCanvas canvas, string sign, SKRect box, SKColor color)
    {
        using var stroke = new SKPaint
        {
            IsAntialias = true,
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(0.7f, box.Width / 7f),
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };

        if (sign.Contains("в—‹", StringComparison.Ordinal))
            canvas.DrawOval(box, stroke);
        if (sign.Contains("в–Ў", StringComparison.Ordinal))
            canvas.DrawRect(box, stroke);
        if (sign.Contains("в•±", StringComparison.Ordinal))
            canvas.DrawLine(box.Left, box.Bottom, box.Right, box.Top, stroke);
    }

    private static string SheetLegendQuantityTextForPage(
        TakeoffItem item,
        IReadOnlyList<Measurement> measurements,
        PageInfo page,
        UnitMode unitMode)
    {
        string measurementType = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
        double fallbackScale = page.ScaleMetersPerPt;

        if (measurementType == "point")
            return Units.FormatCount(measurements.Sum(measurement => measurement.Points.Count));

        bool hasScale = fallbackScale > 0 || measurements.Any(measurement => measurement.ScaleMetersPerPt > 0);
        if (item.IsJoistArea)
        {
            return hasScale
                ? Units.FormatArea(measurements.Sum(measurement => measurement.AreaValue(fallbackScale)), unitMode)
                : $"{measurements.Sum(measurement => measurement.Points.Count)} pts";
        }

        if (!hasScale)
        {
            if (measurementType == "line")
                return $"{measurements.Sum(measurement => Math.Max(0, measurement.Points.Count - 1))} seg";
            if (measurementType == "area")
                return $"{measurements.Sum(measurement => measurement.Points.Count)} pts";
        }

        double total = measurements.Sum(measurement => measurement.Value(fallbackScale));
        return measurementType switch
        {
            "line" => Units.FormatLength(total, unitMode),
            "area" => Units.FormatArea(total, unitMode),
            _ => Units.FormatCount(total),
        };
    }

    private static string SheetLegendTypeTitle(TakeoffItem item) =>
        item.IsJoistArea ? "Area" : TakeoffTypeTitle(item);

    private static string SheetLegendTypeSign(TakeoffItem item) =>
        item.IsJoistArea ? MeasurementTypeSign("area") : TakeoffTypeSign(item);

    private static string TakeoffTypeTitle(TakeoffItem item) =>
        item.IsJoistArea ? "Joist" : MeasurementTypeTitle(item.MeasurementType);

    private static string MeasurementTypeTitle(string measurementType) =>
        OurPlaneCoreJobStore.NormalizeMeasurementType(measurementType) switch
        {
            "point" => "Count",
            "area" => "Area",
            _ => "Line",
        };

    private static string TakeoffTypeSign(TakeoffItem item) =>
        item.IsJoistArea ? "в–Ўв•±" : MeasurementTypeSign(item.MeasurementType);

    private static string MeasurementTypeSign(string measurementType) =>
        OurPlaneCoreJobStore.NormalizeMeasurementType(measurementType) switch
        {
            "point" => "в—‹",
            "area" => "в–Ў",
            _ => "в•±",
        };

    private static (float X, float Y) LegendBoxOrigin(float pageWidth, float pageHeight, float boxWidth, float boxHeight, string anchor)
    {
        const float margin = 18.0f;
        string normalized = NormalizeLegendAnchor(anchor);
        float x = normalized switch
        {
            "TopCenter" or "Center" or "BottomCenter" => (pageWidth - boxWidth) / 2,
            "TopRight" or "RightCenter" or "BottomRight" => pageWidth - boxWidth - margin,
            _ => margin,
        };
        float y = normalized switch
        {
            "TopLeft" or "TopCenter" or "TopRight" => margin,
            "LeftCenter" or "Center" or "RightCenter" => (pageHeight - boxHeight) / 2,
            _ => pageHeight - boxHeight - margin,
        };
        return (Math.Clamp(x, margin, Math.Max(margin, pageWidth - boxWidth - margin)),
                Math.Clamp(y, margin, Math.Max(margin, pageHeight - boxHeight - margin)));
    }

    private static string NormalizeLegendAnchor(string? anchor)
    {
        string clean = (anchor ?? "").Trim();
        return clean switch
        {
            "TopLeft" or "TopCenter" or "TopRight" or
            "LeftCenter" or "Center" or "RightCenter" or
            "BottomLeft" or "BottomCenter" or "BottomRight" => clean,
            _ => "BottomRight",
        };
    }

    private static string TrimLegendText(string text, float maxWidth, SKPaint paint)
    {
        if (paint.MeasureText(text) <= maxWidth)
            return text;

        const string ellipsis = "...";
        string clean = text.Trim();
        while (clean.Length > 0 && paint.MeasureText(clean + ellipsis) > maxWidth)
            clean = clean[..^1].TrimEnd();
        return clean.Length == 0 ? ellipsis : clean + ellipsis;
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
