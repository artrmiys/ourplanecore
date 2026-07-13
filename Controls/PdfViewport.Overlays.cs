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
    private void DrawSheetHeaderOverlay(SKCanvas canvas, float canvasWidth, float canvasHeight)
    {
        if (_pdfW <= 0 || _pdfH <= 0 || _zoom <= 0 || canvasWidth <= 0 || canvasHeight <= 0)
            return;

        SKPoint pageTopLeft = PdfToScreen(new SKPoint(0, 0));
        SKPoint pageBottomRight = PdfToScreen(new SKPoint(_pdfW, _pdfH));
        float pageLeft = pageTopLeft.X;
        float pageTop = pageTopLeft.Y;
        float pageRight = pageBottomRight.X;
        float pageBottom = pageBottomRight.Y;
        float visibleLeft = Math.Max(0, pageLeft);
        float visibleTop = Math.Max(0, pageTop);
        float visibleRight = Math.Min(canvasWidth, pageRight);
        float visibleBottom = Math.Min(canvasHeight, pageBottom);

        SheetOverlayRenderer.DrawHeader(
            canvas,
            visibleLeft,
            visibleTop,
            visibleRight,
            visibleBottom,
            pageLeft,
            pageTop,
            pageRight,
            pageBottom,
            FormatSheetScale(),
            FormatSheetSize(),
            HeaderOverlayScale());
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

        SKPoint pageTopLeft = PdfToScreen(new SKPoint(0, 0));
        SKPoint pageBottomRight = PdfToScreen(new SKPoint(_pdfW, _pdfH));
        float pageLeft = pageTopLeft.X;
        float pageTop = pageTopLeft.Y;
        float pageRight = pageBottomRight.X;
        float pageBottom = pageBottomRight.Y;
        float visibleLeft = Math.Max(0, pageLeft);
        float visibleTop = Math.Max(0, pageTop);
        float visibleRight = Math.Min(canvasWidth, pageRight);
        float visibleBottom = Math.Min(canvasHeight, pageBottom);

        SheetOverlayRenderer.DrawLegend(
            canvas,
            _sheetLegendEntries,
            visibleLeft,
            visibleTop,
            visibleRight,
            visibleBottom,
            pageLeft,
            pageTop,
            pageRight,
            pageBottom,
            SheetLegendAnchor,
            LegendOverlayScale());
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
        if (_pdfW <= 0 || _pdfH <= 0 || ViewportCanvasWidth < 2 || ViewportCanvasHeight < 2)
            return 0;

        return Math.Min(ViewportCanvasWidth / _pdfW, ViewportCanvasHeight / _pdfH) * 0.95f;
    }

    private static float ClampOverlayUserScale(double scale)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            return 1f;

        return (float)Math.Clamp(scale, 0.50, 3.00);
    }

    private string FormatSheetScale()
    {
        if (ScaleMetersPerPt <= 0)
            return "Scale: not set";

        string scale = PdfSheetMetadataService.FormatImperialScale(ScaleMetersPerPt);
        return string.IsNullOrWhiteSpace(scale)
            ? "Scale: not set"
            : $"Scale: {scale}";
    }

    private string FormatSheetSize()
    {
        double widthIn = _pdfW / 72.0;
        double heightIn = _pdfH / 72.0;
        return $"{widthIn:F2} x {heightIn:F2}";
    }

    private ViewportOverlayHitKind HitSheetOverlay(Point screen)
    {
        if (_pdfW <= 0 || _pdfH <= 0 || _zoom <= 0 || !HasViewportCanvasSize)
            return ViewportOverlayHitKind.None;

        if (TryGetSheetOverlayViewport(
                ViewportCanvasWidth,
                ViewportCanvasHeight,
                out float visibleLeft,
                out float visibleTop,
                out float visibleRight,
                out float visibleBottom,
                out float pageLeft,
                out float pageTop,
                out float pageRight,
                out float pageBottom))
        {
            var point = new SKPoint((float)screen.X, (float)screen.Y);
            if (SheetOverlayRenderer.TryGetLegendBounds(
                    _sheetLegendEntries,
                    visibleLeft,
                    visibleTop,
                    visibleRight,
                    visibleBottom,
                    pageLeft,
                    pageTop,
                    pageRight,
                    pageBottom,
                    SheetLegendAnchor,
                    LegendOverlayScale(),
                    out SKRect legendBounds) &&
                InflatedHitBounds(legendBounds).Contains(point))
            {
                return ViewportOverlayHitKind.SheetLegend;
            }

            foreach (SKRect headerBounds in SheetOverlayRenderer.GetHeaderBounds(
                         visibleLeft,
                         visibleTop,
                         visibleRight,
                         visibleBottom,
                         pageLeft,
                         pageTop,
                         pageRight,
                         pageBottom,
                         FormatSheetScale(),
                         FormatSheetSize(),
                         HeaderOverlayScale()))
            {
                if (InflatedHitBounds(headerBounds).Contains(point))
                    return ViewportOverlayHitKind.SheetHeader;
            }
        }

        return ViewportOverlayHitKind.None;
    }

    private bool TryGetSheetOverlayViewport(
        float canvasWidth,
        float canvasHeight,
        out float visibleLeft,
        out float visibleTop,
        out float visibleRight,
        out float visibleBottom,
        out float pageLeft,
        out float pageTop,
        out float pageRight,
        out float pageBottom)
    {
        visibleLeft = 0;
        visibleTop = 0;
        visibleRight = 0;
        visibleBottom = 0;
        pageLeft = 0;
        pageTop = 0;
        pageRight = 0;
        pageBottom = 0;

        if (canvasWidth <= 0 || canvasHeight <= 0)
            return false;

        SKPoint pageTopLeft = PdfToScreen(new SKPoint(0, 0));
        SKPoint pageBottomRight = PdfToScreen(new SKPoint(_pdfW, _pdfH));
        pageLeft = pageTopLeft.X;
        pageTop = pageTopLeft.Y;
        pageRight = pageBottomRight.X;
        pageBottom = pageBottomRight.Y;
        visibleLeft = Math.Max(0, pageLeft);
        visibleTop = Math.Max(0, pageTop);
        visibleRight = Math.Min(canvasWidth, pageRight);
        visibleBottom = Math.Min(canvasHeight, pageBottom);
        return visibleRight > visibleLeft && visibleBottom > visibleTop;
    }

    private static SKRect InflatedHitBounds(SKRect bounds)
    {
        bounds.Inflate(4f, 4f);
        return bounds;
    }

}
