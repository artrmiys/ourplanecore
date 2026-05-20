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
    double MeasurementLabelScale,
    bool ShowJoistLabels = true,
    double AreaEdgeScale = 1.0,
    double AreaFillOpacity = 0.15);

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

public static partial class PdfExporter
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
}
