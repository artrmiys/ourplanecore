using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

public partial class MainWindow
{
    private async void BtnExportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open or create a job before PDF export.";
            return;
        }

        var allPages = CollectPagesUnder(_currentJob.PagesRoot).ToList();
        if (allPages.Count == 0)
        {
            TxtStatus.Text = "No PDF sheets to export.";
            return;
        }

        var initiallySelected = InitialPdfExportSelection(allPages);
        var dialog = new PdfExportDialog(
            allPages,
            initiallySelected,
            includeMeasurements: true,
            includeLegend: _settings.ShowSheetLegend)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        var selectedFolders = dialog.Rows
            .Where(row => row.IsSelected)
            .Select(row => NormalizePathForCompare(row.PageFolder))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pages = allPages
            .Where(page => selectedFolders.Contains(NormalizePathForCompare(page.FolderPath)))
            .ToList();
        if (pages.Count == 0)
        {
            TxtStatus.Text = "No sheets selected for PDF export.";
            return;
        }

        var save = new SaveFileDialog
        {
            Title = "Export PDF",
            Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
            FileName = $"{SafeFileName(_currentJob.Name)}_sheets.pdf",
            InitialDirectory = _currentJob.RootPath,
            AddExtension = true,
            DefaultExt = ".pdf",
        };
        if (save.ShowDialog(this) != true)
            return;

        Button? button = sender as Button;
        try
        {
            if (button != null) button.IsEnabled = false;
            SaveCurrentPageScale();
            SaveCurrentPageAnnotations();
            TxtStatus.Text = $"Exporting {pages.Count} sheet(s) to PDF...";
            var options = new PdfSheetExportOptions(dialog.IncludeMeasurements, dialog.IncludeAnnotations, dialog.IncludeLegend, _viewport.UnitMode, _settings.SheetLegendAnchor);
            string outputPath = save.FileName;
            (bool ok, string error) = await Task.Run(() => TryExportPdfSheets(pages, outputPath, options));
            if (!ok)
            {
                MessageBox.Show(error, "Export PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtStatus.Text = "PDF export failed.";
                return;
            }

            TxtStatus.Text = $"Exported PDF ({pages.Count} sheet(s)) -> {outputPath}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"PDF export failed:\n{ex.Message}", "Export PDF",
                            MessageBoxButton.OK, MessageBoxImage.Error);
            TxtStatus.Text = "PDF export failed.";
        }
        finally
        {
            if (button != null) button.IsEnabled = true;
        }
    }

    private ISet<string> InitialPdfExportSelection(IReadOnlyList<PageInfo> allPages)
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (PagesTree.SelectedItem is TreeViewItem selectedItem)
        {
            foreach (PageInfo page in GetPagesForMetadata(selectedItem))
                selected.Add(page.FolderPath);
        }

        if (selected.Count == 0 && _currentPage != null)
            selected.Add(_currentPage.FolderPath);

        selected.RemoveWhere(path => allPages.All(page => !IsSamePageFolder(path, page.FolderPath)));
        return selected;
    }

    private sealed record PdfSheetExportOptions(
        bool IncludeMeasurements,
        bool IncludeAnnotations,
        bool IncludeLegend,
        UnitMode UnitMode,
        string LegendAnchor);

    private (bool Ok, string Error) TryExportPdfSheets(
        IReadOnlyList<PageInfo> pages,
        string outputPath,
        PdfSheetExportOptions options)
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

            foreach (PageInfo page in pages)
            {
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

                var pageItems = OrderedTakeoffsForPage(page).ToList();
                if (options.IncludeMeasurements)
                    DrawPdfExportMeasurements(canvas, pageItems, page, options.UnitMode);
                if (options.IncludeAnnotations)
                    DrawPdfExportAnnotations(canvas, OurPlaneCoreJobStore.LoadPageAnnotations(page.FolderPath), page.ScaleMetersPerPt, options.UnitMode);
                if (options.IncludeLegend)
                    DrawPdfExportLegend(canvas, render.WidthPt, render.HeightPt, pageItems, page, options);

                document.EndPage();
            }

            document.Close();
            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private void DrawPdfExportMeasurements(
        SKCanvas canvas,
        IReadOnlyList<TakeoffItem> pageItems,
        PageInfo page,
        UnitMode unitMode)
    {
        foreach (TakeoffItem item in pageItems)
        {
            SKColor color = ParseSkColor(item.Color, SKColors.Red);
            foreach (Measurement measurement in MeasurementsForTakeoffOnPage(item, page.FolderPath))
                DrawPdfExportMeasurement(canvas, measurement, color, page.ScaleMetersPerPt, unitMode);
        }
    }

    private static void DrawPdfExportAnnotations(
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
                SKRect rect = NormalizeSkRect(start, end);
                canvas.DrawRect(rect, stroke);
                continue;
            }

            canvas.DrawLine(start, end, stroke);
            if (kind == "arrow")
            {
                DrawPdfExportArrowHead(canvas, start, end, stroke);
                continue;
            }

            if (kind == "dimension")
            {
                DrawPdfExportDimensionTicks(canvas, start, end, stroke);
                double scale = annotation.ScaleMetersPerPt > 0
                    ? annotation.ScaleMetersPerPt
                    : pageScaleMetersPerPt;
                string label = string.IsNullOrWhiteSpace(annotation.Text)
                    ? FormatAnnotationLength(start, end, scale, unitMode)
                    : annotation.Text;
                DrawPdfExportMeasurementLabel(
                    canvas,
                    new SKPoint((start.X + end.X) / 2f, (start.Y + end.Y) / 2f),
                    label,
                    color,
                    centered: true);
            }
        }
    }

    private static void DrawPdfExportArrowHead(SKCanvas canvas, SKPoint start, SKPoint end, SKPaint stroke)
    {
        float dx = end.X - start.X;
        float dy = end.Y - start.Y;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length <= 0.001f)
            return;

        float ux = dx / length;
        float uy = dy / length;
        float px = -uy;
        float py = ux;
        const float arrow = 8.0f;
        SKPoint left = new(end.X - ux * arrow + px * arrow * 0.45f, end.Y - uy * arrow + py * arrow * 0.45f);
        SKPoint right = new(end.X - ux * arrow - px * arrow * 0.45f, end.Y - uy * arrow - py * arrow * 0.45f);
        canvas.DrawLine(end, left, stroke);
        canvas.DrawLine(end, right, stroke);
    }

    private static void DrawPdfExportDimensionTicks(SKCanvas canvas, SKPoint start, SKPoint end, SKPaint stroke)
    {
        float dx = end.X - start.X;
        float dy = end.Y - start.Y;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length <= 0.001f)
            return;

        float px = -dy / length;
        float py = dx / length;
        const float tick = 5.5f;
        canvas.DrawLine(start.X - px * tick, start.Y - py * tick, start.X + px * tick, start.Y + py * tick, stroke);
        canvas.DrawLine(end.X - px * tick, end.Y - py * tick, end.X + px * tick, end.Y + py * tick, stroke);
    }

    private static SKRect NormalizeSkRect(SKPoint a, SKPoint b) =>
        new(
            Math.Min(a.X, b.X),
            Math.Min(a.Y, b.Y),
            Math.Max(a.X, b.X),
            Math.Max(a.Y, b.Y));

    private static string FormatAnnotationLength(SKPoint start, SKPoint end, double scaleMetersPerPt, UnitMode unitMode)
    {
        float dx = end.X - start.X;
        float dy = end.Y - start.Y;
        double lengthPt = Math.Sqrt(dx * dx + dy * dy);
        return scaleMetersPerPt > 0
            ? Units.FormatLength(lengthPt * scaleMetersPerPt, unitMode)
            : $"{lengthPt:F1} pt";
    }

    private static void DrawPdfExportMeasurement(
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
            using var path = new SKPath();
            path.MoveTo(measurement.Points[0]);
            for (int i = 1; i < measurement.Points.Count; i++)
                path.LineTo(measurement.Points[i]);
            path.Close();

            using var fill = new SKPaint
            {
                IsAntialias = true,
                Color = color.WithAlpha(38),
                Style = SKPaintStyle.Fill,
            };
            canvas.DrawPath(path, fill);
            canvas.DrawPath(path, stroke);
            DrawPdfExportJoistLayout(canvas, measurement, color);
            DrawPdfExportMeasurementLabel(canvas, MeasurementLabelPoint(measurement), measurement.Label(pageScaleMetersPerPt, unitMode), color, centered: true);
            return;
        }

        if (type == "line" && measurement.Points.Count >= 2)
        {
            for (int i = 1; i < measurement.Points.Count; i++)
                canvas.DrawLine(measurement.Points[i - 1], measurement.Points[i], stroke);
            DrawPdfExportMeasurementLabel(canvas, measurement.Points[^1], measurement.Label(pageScaleMetersPerPt, unitMode), color, centered: false);
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

        if (Math.Abs(area) < 0.001)
        {
            float avgX = measurement.Points.Average(point => point.X);
            float avgY = measurement.Points.Average(point => point.Y);
            return new SKPoint(avgX, avgY);
        }

        double factor = 1.0 / (3.0 * area);
        return new SKPoint((float)(x * factor), (float)(y * factor));
    }

    private static void DrawPdfExportMeasurementLabel(
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

    private static void DrawPdfExportJoistLayout(SKCanvas canvas, Measurement measurement, SKColor color)
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

    private void DrawPdfExportLegend(
        SKCanvas canvas,
        float width,
        float height,
        IReadOnlyList<TakeoffItem> pageItems,
        PageInfo page,
        PdfSheetExportOptions options)
    {
        var entries = pageItems
            .Select(item =>
            {
                var measurements = MeasurementsForTakeoffOnPage(item, page.FolderPath).ToList();
                return measurements.Count == 0
                    ? null
                    : new SheetLegendEntry(
                        item.Color,
                        item.Name,
                        SheetLegendQuantityTextForPage(item, measurements, page, options.UnitMode),
                        SheetLegendTypeTitle(item),
                        SheetLegendTypeSign(item),
                        []);
            })
            .Where(entry => entry != null)
            .Cast<SheetLegendEntry>()
            .ToList();
        if (entries.Count == 0)
            return;

        float scale = (float)Math.Clamp(_settings.SheetLegendScale, 0.65, 2.0) * 2f;
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
            DrawPdfLegendSignIcon(canvas, entry.Sign, new SKRect(signX, rowY + 1.8f * scale, signX + sign, rowY + 1.8f * scale + sign), textPaint.Color);
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

    private static void DrawPdfLegendSignIcon(SKCanvas canvas, string sign, SKRect box, SKColor color)
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

        if (sign.Contains("○", StringComparison.Ordinal))
            canvas.DrawOval(box, stroke);
        if (sign.Contains("□", StringComparison.Ordinal))
            canvas.DrawRect(box, stroke);
        if (sign.Contains("╱", StringComparison.Ordinal))
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

    private static (float X, float Y) LegendBoxOrigin(float pageWidth, float pageHeight, float boxWidth, float boxHeight, string anchor)
    {
        const float margin = 18.0f;
        string normalized = NormalizeSheetLegendAnchor(anchor);
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
            return SKColor.Parse(NormalizeTakeoffColor(value));
        }
        catch
        {
            return fallback;
        }
    }

}
