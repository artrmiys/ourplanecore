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

public sealed record PdfExportOptions(
    bool IncludeMeasurements,
    bool IncludeAnnotations,
    bool IncludeLegend,
    UnitMode UnitMode,
    string LegendAnchor,
    double LegendScale,
    double HeaderScale,
    bool ShowMeasurementLabels,
    bool ShowLineLabels,
    bool ShowAreaLabels,
    bool ShowCountLabels,
    double MeasurementStrokeScale,
    double PointSizeScale,
    double MeasurementLabelScale);

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
    public const string ExportPaperColorHex = "#FFFFFF";
    private static readonly SKColor ExportPaperColor = SKColors.White;
    private const float BaseExportLabelScale = 1.5f;

    public static (bool Ok, string Error) TryExport(
        IReadOnlyList<PdfExportPageInput> pages,
        string outputPath,
        PdfExportOptions options,
        PdfSheetOverlayExportRenderer? overlayRenderer = null)
    {
        string? tempPath = null;
        bool committed = false;
        var warnings = new List<string>();
        try
        {
            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            string outputDirectory = string.IsNullOrWhiteSpace(dir)
                ? Directory.GetCurrentDirectory()
                : dir;
            tempPath = Path.Combine(
                outputDirectory,
                $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");

            using (var stream = File.Create(tempPath))
            {
                using var document = SKDocument.CreatePdf(stream);
                if (document == null)
                    return (false, "Could not create PDF document.");

                foreach (PdfExportPageInput input in pages)
                {
                    PageInfo page = input.Page;
                    if (!TryRenderExportPage(page, warnings, out ExportRenderedPage renderedPage, out string renderError))
                        return (false, renderError);

                    using (renderedPage)
                    {
                        SKCanvas canvas = document.BeginPage(renderedPage.WidthPt, renderedPage.HeightPt);
                        canvas.Clear(ExportPaperColor);
                        DrawExportPaperUnderlay(canvas, renderedPage.WidthPt, renderedPage.HeightPt);
                        canvas.DrawBitmap(renderedPage.Bitmap, new SKRect(0, 0, renderedPage.WidthPt, renderedPage.HeightPt));

                        if (overlayRenderer != null)
                        {
                            (bool overlayOk, string overlayError) = overlayRenderer(canvas, page, renderedPage.WidthPt, renderedPage.HeightPt);
                            if (!overlayOk)
                            {
                                string warning = $"Overlay skipped on '{page.Name}': {overlayError}";
                                warnings.Add(warning);
                                AppLog.Warn($"Skipping overlay during PDF export for '{page.Name}': {overlayError}");
                            }
                        }

                    if (options.IncludeMeasurements)
                        DrawMeasurements(canvas, input.Takeoffs, page, options);
                    if (options.IncludeAnnotations)
                        DrawAnnotations(canvas, input.Annotations, page.ScaleMetersPerPt, options);
                        if (options.IncludeLegend)
                        {
                            DrawSheetHeader(canvas, renderedPage.WidthPt, renderedPage.HeightPt, page, options);
                            DrawLegend(canvas, renderedPage.WidthPt, renderedPage.HeightPt, input.Takeoffs, page, options);
                        }

                        document.EndPage();
                    }
                }

                document.Close();
            }
            CommitExportFile(tempPath, outputPath);
            committed = true;
            return (true, FormatExportWarnings(warnings));
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "PDF export failed.");
            return (false, ex.Message);
        }
        finally
        {
            if (!committed &&
                !string.IsNullOrWhiteSpace(tempPath) &&
                File.Exists(tempPath))
            {
                try { File.Delete(tempPath); }
                catch (Exception cleanupEx)
                {
                    AppLog.Warn(cleanupEx, $"Could not remove failed PDF export temp file: {tempPath}");
                }
            }
        }
    }

    private static bool TryRenderExportPage(
        PageInfo page,
        List<string> warnings,
        out ExportRenderedPage renderedPage,
        out string error)
    {
        renderedPage = ExportRenderedPage.Empty();
        error = "";

        if (!File.Exists(page.PdfPath))
        {
            error = $"Source PDF not found for sheet '{page.Name}': {page.PdfPath}";
            return false;
        }

        string layerError = "";
        var layerStates = page.PdfLayers
            .GroupBy(layer => layer.Number)
            .ToDictionary(group => group.Key, group => group.First().IsOn);
        if (PdfLayerRenderService.TryRender(
                page.PdfPath,
                page.PdfPage,
                renderScale: 2.0,
                layerStates,
                highlightedLayers: [],
                page.PdfLayersCached ? page.PdfLayers : null,
                out PdfLayerRenderResult render,
                out string renderError))
        {
            SKBitmap? bitmap = SKBitmap.Decode(render.ImageBytes);
            if (bitmap != null)
            {
                renderedPage = new ExportRenderedPage(bitmap, render.WidthPt, render.HeightPt);
                return true;
            }

            layerError = "layer renderer returned an unreadable image.";
        }
        else
        {
            layerError = string.IsNullOrWhiteSpace(renderError)
                ? "layer renderer returned no image."
                : renderError;
        }

        if (TryRenderExportPageWithDocnet(page, out renderedPage, out string docnetError))
        {
            string warning = $"Layer render fallback on '{page.Name}': {layerError}";
            warnings.Add(warning);
            AppLog.Warn(warning);
            return true;
        }

        error = $"Could not render sheet '{page.Name}': {layerError}; Docnet fallback failed: {docnetError}";
        return false;
    }

    private static bool TryRenderExportPageWithDocnet(
        PageInfo page,
        out ExportRenderedPage renderedPage,
        out string error)
    {
        renderedPage = ExportRenderedPage.Empty();
        error = "";

        try
        {
            const float renderScale = 2.0f;
            using var docReader = DocLib.Instance.GetDocReader(page.PdfPath, new PageDimensions(renderScale));
            using var pageReader = docReader.GetPageReader(page.PdfPage);

            int bitmapWidth = pageReader.GetPageWidth();
            int bitmapHeight = pageReader.GetPageHeight();
            byte[] bytes = pageReader.GetImage();

            var info = new SKImageInfo(bitmapWidth, bitmapHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            var bitmap = new SKBitmap(info);
            Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);

            renderedPage = new ExportRenderedPage(
                bitmap,
                bitmapWidth / renderScale,
                bitmapHeight / renderScale);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            AppLog.Warn(ex, $"Docnet PDF export fallback failed for {page.PdfPath} page {page.PdfPage}");
            return false;
        }
    }

    private static void CommitExportFile(string tempPath, string outputPath)
    {
        if (File.Exists(outputPath))
        {
            string backupPath = tempPath + ".bak";
            File.Replace(tempPath, outputPath, backupPath, ignoreMetadataErrors: true);
            try
            {
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
            }
            catch (Exception ex)
            {
                AppLog.Warn(ex, $"Could not remove PDF export backup file: {backupPath}");
            }
            return;
        }

        File.Move(tempPath, outputPath);
    }

    private static string FormatExportWarnings(IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
            return "";

        string text = string.Join(Environment.NewLine, warnings.Take(8));
        if (warnings.Count > 8)
            text += $"{Environment.NewLine}...and {warnings.Count - 8} more.";
        return text;
    }

    private sealed class ExportRenderedPage(SKBitmap bitmap, float widthPt, float heightPt) : IDisposable
    {
        public static ExportRenderedPage Empty() => new(new SKBitmap(), 0, 0);
        public SKBitmap Bitmap { get; } = bitmap;
        public float WidthPt { get; } = widthPt;
        public float HeightPt { get; } = heightPt;

        public void Dispose() => Bitmap.Dispose();
    }

    private static void DrawExportPaperUnderlay(SKCanvas canvas, float widthPt, float heightPt)
    {
        using var paint = new SKPaint
        {
            Color = ExportPaperColor,
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };
        canvas.DrawRect(new SKRect(0, 0, widthPt, heightPt), paint);
    }

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

    private static void DrawAnnotations(
        SKCanvas canvas,
        IReadOnlyList<PageAnnotation> annotations,
        double pageScaleMetersPerPt,
        PdfExportOptions options)
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
                StrokeWidth = ExportAnnotationStrokeWidth(annotation),
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
            };

            SKPoint start = annotation.Points[0];
            SKPoint end = annotation.Points[1];
            if (kind == "note")
            {
                DrawNoteAnnotation(canvas, annotation, color, options);
                continue;
            }

            if (kind == "rectangle")
            {
                using SKPath path = BuildClosedAnnotationPath(AnnotationRectangleCorners(annotation.Points));
                canvas.DrawPath(path, stroke);
                continue;
            }

            if (kind == "cloud")
            {
                DrawCloudAnnotation(canvas, annotation, stroke);
                continue;
            }

            if (kind == "area")
            {
                if (annotation.Points.Count < 3)
                    continue;

                using var fill = new SKPaint
                {
                    IsAntialias = true,
                    Color = color.WithAlpha(55),
                    Style = SKPaintStyle.Fill,
                };
                using SKPath path = BuildClosedAnnotationPath(annotation.Points);
                canvas.DrawPath(path, fill);
                canvas.DrawPath(path, stroke);
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
                    ? FormatAnnotationLength(start, end, scale, options.UnitMode)
                    : annotation.Text;
                DrawMeasurementLabel(
                    canvas,
                    new SKPoint((start.X + end.X) / 2f, (start.Y + end.Y) / 2f),
                    label,
                    color,
                    centered: true,
                    ExportLabelScale(options));
            }
        }
    }

    private static float ExportAnnotationStrokeWidth(PageAnnotation annotation)
    {
        double value = annotation.StrokeWidth is >= 0.75 and <= 12.0
            ? annotation.StrokeWidth
            : 1.8;
        return (float)Math.Clamp(value * 0.75, 0.75, 9.0);
    }

    private static void DrawCloudAnnotation(SKCanvas canvas, PageAnnotation annotation, SKPaint stroke)
    {
        IReadOnlyList<SKPoint> corners = AnnotationRectangleCorners(annotation.Points);
        if (corners.Count < 4 ||
            !TryGetAnnotationLocalFrame(corners, out SKMatrix localToPdf, out float width, out float height))
        {
            return;
        }

        using var saved = new SKAutoCanvasRestore(canvas, true);
        canvas.Concat(ref localToPdf);
        using SKPath path = BuildCloudPath(width, height);
        canvas.DrawPath(path, stroke);
    }

    private static SKPath BuildCloudPath(float width, float height)
    {
        var path = new SKPath();
        if (width <= 0 || height <= 0)
            return path;

        float bump = Math.Clamp(Math.Min(width, height) / 7f, 5f, Math.Max(5f, Math.Min(width, height) / 3f));
        path.MoveTo(bump, 0);
        AddCloudEdge(path, bump, 0, width - bump, 0, 0, -bump);
        AddCloudEdge(path, width, bump, width, height - bump, bump, 0);
        AddCloudEdge(path, width - bump, height, bump, height, 0, bump);
        AddCloudEdge(path, 0, height - bump, 0, bump, -bump, 0);
        path.Close();
        return path;
    }

    private static void AddCloudEdge(
        SKPath path,
        float startX,
        float startY,
        float endX,
        float endY,
        float controlOffsetX,
        float controlOffsetY)
    {
        float length = MathF.Sqrt(MathF.Pow(endX - startX, 2) + MathF.Pow(endY - startY, 2));
        int segments = Math.Max(2, (int)MathF.Ceiling(length / 42f));
        for (int i = 1; i <= segments; i++)
        {
            float t0 = (i - 1f) / segments;
            float t1 = i / (float)segments;
            float mid = (t0 + t1) / 2f;
            var control = new SKPoint(
                startX + (endX - startX) * mid + controlOffsetX,
                startY + (endY - startY) * mid + controlOffsetY);
            var end = new SKPoint(
                startX + (endX - startX) * t1,
                startY + (endY - startY) * t1);
            path.QuadTo(control, end);
        }
    }

    private static void DrawNoteAnnotation(
        SKCanvas canvas,
        PageAnnotation annotation,
        SKColor color,
        PdfExportOptions options)
    {
        IReadOnlyList<SKPoint> corners = AnnotationRectangleCorners(annotation.Points);
        if (corners.Count < 4)
            return;

        using var fill = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0xFF, 0xF8, 0xC6, 235),
            Style = SKPaintStyle.Fill,
        };
        using var stroke = new SKPaint
        {
            IsAntialias = true,
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.15f * ExportLabelScale(options),
        };
        using SKPath path = BuildClosedAnnotationPath(corners);
        canvas.DrawPath(path, fill);
        canvas.DrawPath(path, stroke);

        float labelScale = ExportLabelScale(options);
        float pad = 4.5f * labelScale;
        if (!TryGetAnnotationLocalFrame(corners, out SKMatrix localToPdf, out float width, out float height))
            return;

        var textRect = new SKRect(pad, pad, width - pad, height - pad);
        if (textRect.Width <= 1 || textRect.Height <= 1)
            return;

        using var saved = new SKAutoCanvasRestore(canvas, true);
        canvas.Concat(ref localToPdf);
        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.Black.WithAlpha(220),
            TextSize = 8.0f * labelScale,
            Typeface = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default,
        };
        float lineHeight = textPaint.TextSize * 1.22f;
        int maxLines = Math.Max(1, (int)(textRect.Height / lineHeight));
        IReadOnlyList<string> lines = WrapAnnotationText(annotation.Text, textPaint, textRect.Width, maxLines);
        float baseline = textRect.Top - textPaint.FontMetrics.Ascent;
        foreach (string line in lines)
        {
            if (baseline + textPaint.FontMetrics.Descent > textRect.Bottom)
                break;

            canvas.DrawText(line, textRect.Left, baseline, textPaint);
            baseline += lineHeight;
        }
    }

    private static IReadOnlyList<SKPoint> AnnotationRectangleCorners(IReadOnlyList<SKPoint> points)
    {
        if (points.Count >= 4)
            return points.Take(4).ToList();

        if (points.Count < 2)
            return points;

        SKRect rect = MeasurementGeometry.NormalizeRect(points[0], points[1]);
        return
        [
            new SKPoint(rect.Left, rect.Top),
            new SKPoint(rect.Right, rect.Top),
            new SKPoint(rect.Right, rect.Bottom),
            new SKPoint(rect.Left, rect.Bottom),
        ];
    }

    private static SKPath BuildClosedAnnotationPath(IReadOnlyList<SKPoint> points)
    {
        var path = new SKPath();
        if (points.Count == 0)
            return path;

        path.MoveTo(points[0]);
        for (int i = 1; i < points.Count; i++)
            path.LineTo(points[i]);
        path.Close();
        return path;
    }

    private static bool TryGetAnnotationLocalFrame(
        IReadOnlyList<SKPoint> corners,
        out SKMatrix localToPdf,
        out float width,
        out float height)
    {
        localToPdf = SKMatrix.CreateIdentity();
        width = 0;
        height = 0;
        if (corners.Count < 4)
            return false;

        SKPoint origin = corners[0];
        SKPoint xAxis = new(corners[1].X - origin.X, corners[1].Y - origin.Y);
        SKPoint yAxis = new(corners[3].X - origin.X, corners[3].Y - origin.Y);
        width = MeasurementGeometry.Distance(corners[0], corners[1]);
        height = MeasurementGeometry.Distance(corners[0], corners[3]);
        if (width <= ViewportConstants.ZeroLengthEpsilon ||
            height <= ViewportConstants.ZeroLengthEpsilon)
        {
            return false;
        }

        localToPdf = new SKMatrix
        {
            ScaleX = xAxis.X / width,
            SkewX = yAxis.X / height,
            TransX = origin.X,
            SkewY = xAxis.Y / width,
            ScaleY = yAxis.Y / height,
            TransY = origin.Y,
            Persp0 = 0,
            Persp1 = 0,
            Persp2 = 1,
        };
        return true;
    }

    private static IReadOnlyList<string> WrapAnnotationText(string text, SKPaint paint, float maxWidth, int maxLines)
    {
        text = string.IsNullOrWhiteSpace(text) ? "Note" : text.Trim();
        var result = new List<string>();
        foreach (string paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            string current = "";
            foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = string.IsNullOrWhiteSpace(current) ? word : $"{current} {word}";
                if (paint.MeasureText(candidate) <= maxWidth || string.IsNullOrWhiteSpace(current))
                {
                    current = candidate;
                    continue;
                }

                result.Add(current);
                if (result.Count >= maxLines)
                    return result;
                current = word;
            }

            if (!string.IsNullOrWhiteSpace(current))
                result.Add(current);
            if (result.Count >= maxLines)
                return result;
        }

        return result.Count == 0 ? ["Note"] : result;
    }

    private static string FormatAnnotationLength(SKPoint start, SKPoint end, double scaleMetersPerPt, UnitMode unitMode)
    {
        float lengthPt = MeasurementGeometry.Distance(start, end);
        return AnnotationGlyphRenderer.FormatLength(lengthPt, (float)scaleMetersPerPt, unitMode);
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
                Color = color.WithAlpha(38),
                Style = SKPaintStyle.Fill,
            };
            canvas.DrawPath(path, fill);
            canvas.DrawPath(path, stroke);
            DrawJoistLayout(canvas, measurement, color, options, drawSegments: true, drawLabels: false);
            return;
        }

        if (type == "line" && measurement.Points.Count >= 2)
        {
            for (int i = 1; i < measurement.Points.Count; i++)
                canvas.DrawLine(measurement.Points[i - 1], measurement.Points[i], stroke);
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
            if (ShouldExportMeasurementLabel(measurement.MType, options))
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

    private static float ExportPointScale(PdfExportOptions options) =>
        (float)Math.Clamp(options.PointSizeScale, 0.25, AppSettingsStore.PdfExportScaleMax);

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

        if (!drawLabels || !measurement.JoistShowLabels || layout.Count > 180)
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
                    [],
                    TakeoffGlyphKind(item));
            })
            .Where(entry => entry != null)
            .Cast<SheetLegendEntry>()
            .ToList();
        SheetOverlayRenderer.DrawLegend(
            canvas,
            entries,
            0,
            0,
            width,
            height,
            0,
            0,
            width,
            height,
            options.LegendAnchor,
            (float)Math.Clamp(options.LegendScale, 0.25, AppSettingsStore.PdfExportScaleMax));
    }

    private static void DrawSheetHeader(
        SKCanvas canvas,
        float width,
        float height,
        PageInfo page,
        PdfExportOptions options)
    {
        SheetOverlayRenderer.DrawHeader(
            canvas,
            0,
            0,
            width,
            height,
            0,
            0,
            width,
            height,
            FormatSheetScale(page.ScaleMetersPerPt),
            FormatSheetSize(width, height),
            (float)Math.Clamp(options.HeaderScale, 0.25, AppSettingsStore.PdfExportScaleMax));
    }

    private static string FormatSheetScale(double scaleMetersPerPt)
    {
        if (scaleMetersPerPt <= 0)
            return "Scale: not set";

        string scale = PdfSheetMetadataService.FormatImperialScale(scaleMetersPerPt);
        return string.IsNullOrWhiteSpace(scale)
            ? "Scale: not set"
            : $"Scale: {scale}";
    }

    private static string FormatSheetSize(float widthPt, float heightPt)
    {
        double widthIn = widthPt / 72.0;
        double heightIn = heightPt / 72.0;
        return $"{widthIn:F2} x {heightIn:F2}";
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

    private static MeasurementGlyphKind TakeoffGlyphKind(TakeoffItem item) =>
        MeasurementGlyph.Parse(
            OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType),
            joist: item.IsJoistArea,
            countSymbol: item.CountSymbol);

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
