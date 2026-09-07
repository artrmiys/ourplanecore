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
using OurPlanCore;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    private bool TrySavePdfCrop(
        SKRect requestedPdfRect,
        string outputPath,
        bool includeMeasurementOverlay,
        out SKRect cropPdfRect,
        out string error)
    {
        cropPdfRect = SKRect.Empty;
        error = "";

        if (_pageBitmap == null || _bitmapScale <= 0 || _pdfW <= 0 || _pdfH <= 0)
        {
            error = "No rendered PDF page is available.";
            return false;
        }

        float left = Math.Clamp(Math.Min(requestedPdfRect.Left, requestedPdfRect.Right), 0, _pdfW);
        float top = Math.Clamp(Math.Min(requestedPdfRect.Top, requestedPdfRect.Bottom), 0, _pdfH);
        float right = Math.Clamp(Math.Max(requestedPdfRect.Left, requestedPdfRect.Right), 0, _pdfW);
        float bottom = Math.Clamp(Math.Max(requestedPdfRect.Top, requestedPdfRect.Bottom), 0, _pdfH);

        if (right - left < 1 || bottom - top < 1)
        {
            error = "Requested crop is outside the PDF page.";
            return false;
        }

        int srcLeft = Math.Clamp((int)Math.Floor(left * _bitmapScale), 0, _pageBitmap.Width - 1);
        int srcTop = Math.Clamp((int)Math.Floor(top * _bitmapScale), 0, _pageBitmap.Height - 1);
        int srcRight = Math.Clamp((int)Math.Ceiling(right * _bitmapScale), srcLeft + 1, _pageBitmap.Width);
        int srcBottom = Math.Clamp((int)Math.Ceiling(bottom * _bitmapScale), srcTop + 1, _pageBitmap.Height);
        int cropWidth = srcRight - srcLeft;
        int cropHeight = srcBottom - srcTop;
        if (cropWidth <= 0 || cropHeight <= 0)
        {
            error = "Requested crop is too small.";
            return false;
        }

        cropPdfRect = new SKRect(
            srcLeft / _bitmapScale,
            srcTop / _bitmapScale,
            srcRight / _bitmapScale,
            srcBottom / _bitmapScale);

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var crop = new SKBitmap(cropWidth, cropHeight);
        using (var canvas = new SKCanvas(crop))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(
                _pageBitmap,
                new SKRectI(srcLeft, srcTop, srcRight, srcBottom),
                new SKRect(0, 0, cropWidth, cropHeight));
            if (includeMeasurementOverlay)
                DrawBookmarkMeasurementSnapshot(canvas, cropPdfRect, _bitmapScale);
        }

        using var image = SKImage.FromBitmap(crop);
        using var data = image.Encode(SKEncodedImageFormat.Png, 92);
        using var stream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        data.SaveTo(stream);

        return true;
    }

    private void DrawBookmarkMeasurementSnapshot(SKCanvas canvas, SKRect cropPdfRect, float outputScale)
    {
        IReadOnlyList<Measurement> activeMeasurements = ActivePageMeasurements();
        if (activeMeasurements.Count == 0 || outputScale <= 0)
            return;

        var transform = SKMatrix.CreateScaleTranslation(
            outputScale,
            outputScale,
            -cropPdfRect.Left * outputScale,
            -cropPdfRect.Top * outputScale);
        using var restore = new SKAutoCanvasRestore(canvas, true);
        canvas.Concat(ref transform);

        ClearPaintJoistLayoutCache();
        try
        {
            IReadOnlyList<Measurement> candidates = LayerOrderedMeasurements(
                VisibleMeasurementCandidates(cropPdfRect));
            var visibleMeasurements = new List<Measurement>(Math.Min(candidates.Count, 256));
            bool drawDetails = ViewportRenderPolicy.ShouldDrawMeasurementDetails(
                _zoom,
                activeMeasurements.Count,
                fastNavigationFrame: false);
            bool simplifyAreaPaint = ViewportRenderPolicy.ShouldUseSimplifiedAreaPaint(
                _zoom,
                activeMeasurements.Count,
                fastNavigationFrame: false);
            foreach (Measurement measurement in candidates)
            {
                if (!IsMeasurementVisible(measurement, cropPdfRect))
                    continue;

                visibleMeasurements.Add(measurement);
                DrawMeasurement(
                    canvas,
                    measurement,
                    selected: false,
                    drawLabels: false,
                    drawDetails,
                    simplifyAreaPaint);
            }

            bool drawAllLabels = ViewportRenderPolicy.ShouldDrawMeasurementLabels(
                _zoom,
                activeMeasurements.Count,
                fastNavigationFrame: false);
            foreach (Measurement measurement in visibleMeasurements)
            {
                bool drawDenseJoistLabel =
                    measurement.MType == "area" &&
                    measurement.JoistEnabled &&
                    ShouldDrawJoistSummaryLabel();
                if (drawAllLabels || drawDenseJoistLabel)
                    DrawMeasurementTopLabels(canvas, measurement, cropPdfRect);
            }
        }
        finally
        {
            ClearPaintJoistLayoutCache();
        }
    }

    public bool TrySaveContextCrop(
        float pdfX,
        float pdfY,
        float radiusPt,
        string outputPath,
        out SKRect cropPdfRect,
        out string error)
    {
        radiusPt = Math.Max(24f, radiusPt);
        var requested = SKRect.Create(pdfX - radiusPt, pdfY - radiusPt, radiusPt * 2, radiusPt * 2);
        return TrySavePdfCrop(requested, outputPath, includeMeasurementOverlay: false, out cropPdfRect, out error);
    }

    public bool TrySaveCropRect(
        SKRect requestedPdfRect,
        string outputPath,
        out SKRect cropPdfRect,
        out string error) =>
        TrySavePdfCrop(requestedPdfRect, outputPath, includeMeasurementOverlay: false, out cropPdfRect, out error);

    public bool TrySaveBookmarkCropRect(
        SKRect requestedPdfRect,
        string outputPath,
        out SKRect cropPdfRect,
        out string error) =>
        TrySavePdfCrop(requestedPdfRect, outputPath, includeMeasurementOverlay: true, out cropPdfRect, out error);

    public bool TrySaveMeasurementCrop(
        Measurement measurement,
        float paddingPt,
        string outputPath,
        out SKRect cropPdfRect,
        out string error)
    {
        cropPdfRect = SKRect.Empty;
        if (!_measurementSet.Contains(measurement) || measurement.Points.Count == 0)
        {
            error = "Measurement is not loaded in the current viewport.";
            return false;
        }

        SKRect bounds = MeasurementBounds(measurement);
        paddingPt = Math.Max(24f, paddingPt);

        float width = Math.Max(bounds.Width + paddingPt * 2, 240f);
        float height = Math.Max(bounds.Height + paddingPt * 2, 240f);
        float centerX = (bounds.Left + bounds.Right) / 2f;
        float centerY = (bounds.Top + bounds.Bottom) / 2f;
        var requested = SKRect.Create(centerX - width / 2f, centerY - height / 2f, width, height);

        return TrySavePdfCrop(requested, outputPath, includeMeasurementOverlay: false, out cropPdfRect, out error);
    }
}
