using System;
using System.Collections.Generic;
using System.IO;
using Docnet.Core;
using Docnet.Core.Models;
using SkiaSharp;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OurPlanCore;

public static partial class PdfExporter
{
    // Shared by file export and the live preview, including drawing order and warnings.
    private static void DrawExportContent(SKCanvas canvas, PdfExportPageInput input,
        ExportRenderedPage rendered, PdfExportOptions options,
        PdfSheetOverlayExportRenderer? overlayRenderer, List<string> warnings)
    {
        PageInfo page = input.Page;
        canvas.Clear(ExportPaperColor);
        DrawExportPaperUnderlay(canvas, rendered.WidthPt, rendered.HeightPt);
        canvas.DrawBitmap(rendered.Bitmap, new SKRect(0, 0, rendered.WidthPt, rendered.HeightPt));
        if (overlayRenderer != null)
        {
            (bool ok, string error) = overlayRenderer(canvas, page, rendered.WidthPt, rendered.HeightPt);
            if (!ok)
                warnings.Add($"Overlay skipped on '{page.Name}': {error}");
        }
        if (options.IncludeMeasurements)
            DrawMeasurements(canvas, input.MeasurementLayers ?? input.Takeoffs, page, options,
                new SKRect(0, 0, rendered.WidthPt, rendered.HeightPt));
        if (options.IncludeAnnotations)
            DrawAnnotations(canvas, input.Annotations, page.ScaleMetersPerPt, options);
        if (options.IncludeLegend)
        {
            DrawSheetHeader(canvas, rendered.WidthPt, rendered.HeightPt, page, options);
            DrawLegend(canvas, rendered.WidthPt, rendered.HeightPt, input.Takeoffs, page, options);
        }
    }

    public sealed record PreviewFrame(byte[] PdfBytes, BitmapSource Image, string Warning);

    // Used by one serialized background render loop. The source raster is reused while
    // sliders change; the measurements and the resulting PDF are rebuilt each time.
    public sealed class PreviewSession : IDisposable
    {
        private ExportRenderedPage? _source;
        private readonly List<string> _sourceWarnings = [];

        public PreviewFrame Render(PdfExportPageInput input, PdfExportOptions options,
            PdfSheetOverlayExportRenderer? overlayRenderer = null)
        {
            if (_source == null)
            {
                if (!TryRenderExportPage(input.Page, _sourceWarnings, out ExportRenderedPage source, out string error))
                    throw new InvalidOperationException(error);
                _source = source;
            }
            var warnings = new List<string>(_sourceWarnings);
            using var stream = new MemoryStream();
            using (var document = SKDocument.CreatePdf(stream))
            {
                if (document == null)
                    throw new InvalidOperationException("Could not create PDF preview.");
                SKCanvas canvas = document.BeginPage(_source.WidthPt, _source.HeightPt);
                DrawExportContent(canvas, input, _source, options, overlayRenderer, warnings);
                document.EndPage();
                document.Close();
            }
            byte[] bytes = stream.ToArray();
            using var reader = DocLib.Instance.GetDocReader(bytes, new PageDimensions(2800, 2800));
            using var pageReader = reader.GetPageReader(0);
            int width = pageReader.GetPageWidth();
            int height = pageReader.GetPageHeight();
            BitmapSource image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32,
                null, pageReader.GetImage(), width * 4);
            image.Freeze();
            return new PreviewFrame(bytes, image, FormatExportWarnings(warnings));
        }

        public void Dispose()
        {
            _source?.Dispose();
            _source = null;
        }
    }
}
