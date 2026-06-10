using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Docnet.Core;
using Docnet.Core.Models;
using OurPlaneCore;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    // ═════════════════════════════════════════════════════════════════════════
    // Rendering
    // ═════════════════════════════════════════════════════════════════════════

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        if (UpdateCanvasMetrics(e.Info.Width, e.Info.Height))
            ClampPanToPage();

        Stopwatch frameWatch = Stopwatch.StartNew();
        bool paintedCurrentPage = false;
        var canvas = e.Surface.Canvas;
        canvas.Clear(GetCachedColor(ViewBackgroundColor, SKColors.White));

        if (_pageBitmap == null)
        {
            DrawBlankPageLoadingSurface(canvas, (float)e.Info.Width, (float)e.Info.Height);
            return;
        }

        long pageBitmapMs = 0;
        long overlayMs = 0;
        long measurementMs = 0;
        long markupMs = 0;
        long inProgressMs = 0;
        long labelMs = 0;
        long screenOverlayMs = 0;
        int visibleMeasurementCount = 0;
        IReadOnlyList<Measurement> activeMeasurements = ActivePageMeasurements();
        bool previousFastFrame = _renderNavigationFastFrame;
        _renderNavigationFastFrame = IsFastNavigationFrame(activeMeasurements.Count);
        try
        {
            ClearPaintJoistLayoutCache();
            SKRect visiblePdf = GetVisiblePdfRect();

            // ── PDF page bitmap ───────────────────────────────────────────────────
            // PDF point (px,py) → screen pixel (sx,sy):
            //   sx = (px - panX) * zoom
            //   sy = (py - panY) * zoom
            // bitmap pixel (bx,by) → screen pixel:
            //   sx = bx * zoom/bitmapScale - panX*zoom
            long sectionStart = frameWatch.ElapsedMilliseconds;
            {
                bool detailCoversVisiblePage = DetailRenderCoversVisibleViewForPaint();
                (SKBitmap paintBitmap, float paintScale) = detailCoversVisiblePage
                    ? (_pageBitmap, _bitmapScale)
                    : SelectPageBitmapForPaint();
                if (paintScale <= 0)
                    paintScale = _bitmapScale;

                using var bitmapPaint = new SKPaint
                {
                    IsAntialias = false,
                    FilterQuality = CurrentPageBitmapFilterQuality(),
                };
                // Mip bitmaps are already near screen resolution; bilinear is enough.
                if (paintBitmap != _pageBitmap)
                    bitmapPaint.FilterQuality = SKFilterQuality.Low;

                float visibleW = ViewportCanvasWidth / Math.Max(_zoom, 0.001f);
                float visibleH = ViewportCanvasHeight / Math.Max(_zoom, 0.001f);
                float srcLeft = Math.Clamp(_panX * paintScale, 0, paintBitmap.Width);
                float srcTop = Math.Clamp(_panY * paintScale, 0, paintBitmap.Height);
                float srcRight = Math.Clamp((_panX + visibleW) * paintScale, 0, paintBitmap.Width);
                float srcBottom = Math.Clamp((_panY + visibleH) * paintScale, 0, paintBitmap.Height);

                if (srcRight > srcLeft && srcBottom > srcTop)
                {
                    var src = new SKRect(srcLeft, srcTop, srcRight, srcBottom);
                    var dst = new SKRect(
                        (srcLeft / paintScale - _panX) * _zoom,
                        (srcTop / paintScale - _panY) * _zoom,
                        (srcRight / paintScale - _panX) * _zoom,
                        (srcBottom / paintScale - _panY) * _zoom);
                    DrawPagePaperUnderlay(canvas, dst);
                    if (!detailCoversVisiblePage)
                        canvas.DrawBitmap(paintBitmap, src, dst, bitmapPaint);
                    DrawDetailRenderTile(canvas);
                    DrawPageBackgroundTint(canvas, dst);
                    DrawPdfLayerTraceGhost(canvas, dst);
                    DrawLowZoomLineOverlay(canvas, visiblePdf);
                }
            }
            pageBitmapMs += frameWatch.ElapsedMilliseconds - sectionStart;

            if (_showingPreviousPageDuringSwitch)
            {
                sectionStart = frameWatch.ElapsedMilliseconds;
                DrawPageSwitchLoadingVeil(canvas, (float)e.Info.Width, (float)e.Info.Height);
                screenOverlayMs += frameWatch.ElapsedMilliseconds - sectionStart;
                return;
            }

            // ── Measurement overlay (PDF-point coordinate system) ─────────────────
            {
                var measMtx = SKMatrix.CreateScaleTranslation(
                    _zoom, _zoom, -_panX * _zoom, -_panY * _zoom);
                using var saved = new SKAutoCanvasRestore(canvas, true);
                // Preserve the SKElement DPI matrix; replacing it causes laptop-scale cursor offsets.
                canvas.Concat(ref measMtx);
                sectionStart = frameWatch.ElapsedMilliseconds;
                if (ViewportRenderPolicy.ShouldDrawSheetOverlay(_renderNavigationFastFrame, IsSheetOverlayPointEditing))
                    DrawSheetOverlay(canvas, visiblePdf);
                DrawSheetOverlayEditGuides(canvas);
                DrawCursorGuide(canvas, visiblePdf);
                DrawTransformOverlay(canvas);
                overlayMs += frameWatch.ElapsedMilliseconds - sectionStart;

                sectionStart = frameWatch.ElapsedMilliseconds;
                IReadOnlyList<Measurement> visibleMeasurements = DrawMeasurements(canvas, visiblePdf, activeMeasurements);
                visibleMeasurementCount = visibleMeasurements.Count;
                measurementMs += frameWatch.ElapsedMilliseconds - sectionStart;

                sectionStart = frameWatch.ElapsedMilliseconds;
                DrawPageAnnotations(canvas, visiblePdf);
                DrawAiActionDraftPreview(canvas, visiblePdf);
                DrawAiMarkers(canvas, visiblePdf);
                DrawThreeDRoofGuides(canvas, visiblePdf);
                markupMs += frameWatch.ElapsedMilliseconds - sectionStart;

                sectionStart = frameWatch.ElapsedMilliseconds;
                DrawInProgress(canvas);
                inProgressMs += frameWatch.ElapsedMilliseconds - sectionStart;

                sectionStart = frameWatch.ElapsedMilliseconds;
                DrawMeasurementLabels(canvas, visiblePdf, activeMeasurements, visibleMeasurements);
                labelMs += frameWatch.ElapsedMilliseconds - sectionStart;
            }

            sectionStart = frameWatch.ElapsedMilliseconds;
            DrawSheetHeaderOverlay(canvas, (float)e.Info.Width, (float)e.Info.Height);
            DrawSheetLegendOverlay(canvas, (float)e.Info.Width, (float)e.Info.Height);
            screenOverlayMs += frameWatch.ElapsedMilliseconds - sectionStart;
            paintedCurrentPage = true;
        }
        finally
        {
            ClearPaintJoistLayoutCache();
            frameWatch.Stop();
            if (paintedCurrentPage)
                MarkCurrentPagePainted();
            ReportSlowViewportFrame(
                frameWatch.ElapsedMilliseconds,
                activeMeasurements.Count,
                visibleMeasurementCount,
                pageBitmapMs,
                overlayMs,
                measurementMs,
                markupMs,
                inProgressMs,
                labelMs,
                screenOverlayMs);
            _renderNavigationFastFrame = previousFastFrame;
        }
    }

    private void ReportSlowViewportFrame(
        long elapsedMs,
        int activeMeasurementCount,
        int visibleMeasurementCount,
        long pageBitmapMs,
        long overlayMs,
        long measurementMs,
        long markupMs,
        long inProgressMs,
        long labelMs,
        long screenOverlayMs)
    {
        if (elapsedMs < ViewportRenderPolicy.SlowFrameLogMs)
            return;

        ViewportPerformanceRecorder.RecordSlowFrame(
            _pageFolder,
            _zoom,
            _renderNavigationFastFrame,
            activeMeasurementCount,
            visibleMeasurementCount,
            _renderedScale,
            _sheetOverlayBitmap != null,
            elapsedMs,
            pageBitmapMs,
            overlayMs,
            measurementMs,
            markupMs,
            inProgressMs,
            labelMs,
            screenOverlayMs);

        DateTime now = DateTime.UtcNow;
        if ((now - _lastSlowFrameLogAt).TotalSeconds < 2)
            return;

        _lastSlowFrameLogAt = now;
        AppLog.Info(
            $"Viewport slow frame {elapsedMs}ms; zoom={_zoom:0.###}; fast={_renderNavigationFastFrame}; " +
            $"page='{_pageFolder}'; activeMeasurements={activeMeasurementCount}; visibleMeasurements={visibleMeasurementCount}; " +
            $"renderedScale={_renderedScale:0.###}; overlay={(_sheetOverlayBitmap != null)}; " +
            $"timings=page:{pageBitmapMs} overlay:{overlayMs} measurements:{measurementMs} markups:{markupMs} " +
            $"inProgress:{inProgressMs} labels:{labelMs} chrome:{screenOverlayMs}ms");
    }

    private SKFilterQuality CurrentPageBitmapFilterQuality()
    {
        if (_renderNavigationFastFrame)
            return SKFilterQuality.None;

        if (_usingRasterSheetRender)
        {
            if (ShouldUseFastFarZoomRasterSheetSampling())
                return SKFilterQuality.None;

            return ShouldUseSharperSourceImageRasterSampling()
                ? SKFilterQuality.Medium
                : SKFilterQuality.Low;
        }

        if (_renderNavigationFastFrame || _zoom > _bitmapScale * 1.05f)
            return SKFilterQuality.Low;

        return SKFilterQuality.Medium;
    }

    private bool ShouldUseFastFarZoomRasterSheetSampling() =>
        !ShouldUseSharperSourceImageRasterSampling() &&
        _bitmapScale > 0 &&
        _zoom <= ViewportRenderPolicy.RasterSheetFarZoomFastPaintMaxZoom &&
        _zoom <= _bitmapScale * ViewportRenderPolicy.RasterSheetFarZoomFastPaintMaxScaleRatio;

    private bool ShouldUseSharperSourceImageRasterSampling() =>
        !_renderNavigationFastFrame &&
        RasterSheetCacheService.ShouldUseSourceImageRasterForFastOpen(_rasterSheetSource);

    private void DrawCursorGuide(SKCanvas canvas, SKRect visiblePdf)
    {
        if (!_cursorGuideVisible || !_lastPointerPdf.HasValue || _zoom <= 0)
            return;

        SKPoint point = _lastPointerPdf.Value;
        if (point.X < visiblePdf.Left ||
            point.X > visiblePdf.Right ||
            point.Y < visiblePdf.Top ||
            point.Y > visiblePdf.Bottom)
        {
            return;
        }

        float gap = ScreenToPdfDistance(5f);
        using var guide = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 24),
            StrokeWidth = ScreenToPdfDistance(1f),
            IsAntialias = false,
            Style = SKPaintStyle.Stroke,
        };

        if (point.X - gap > visiblePdf.Left)
            canvas.DrawLine(visiblePdf.Left, point.Y, point.X - gap, point.Y, guide);
        if (point.X + gap < visiblePdf.Right)
            canvas.DrawLine(point.X + gap, point.Y, visiblePdf.Right, point.Y, guide);
        if (point.Y - gap > visiblePdf.Top)
            canvas.DrawLine(point.X, visiblePdf.Top, point.X, point.Y - gap, guide);
        if (point.Y + gap < visiblePdf.Bottom)
            canvas.DrawLine(point.X, point.Y + gap, point.X, visiblePdf.Bottom, guide);
    }

    private static void DrawPageSwitchLoadingVeil(SKCanvas canvas, float width, float height)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 82),
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };
        canvas.DrawRect(new SKRect(0, 0, width, height), paint);
    }

    private void DrawPagePaperUnderlay(SKCanvas canvas, SKRect rect)
    {
        using var paint = new SKPaint
        {
            Color = GetCachedColor(PageBackgroundColor, SKColors.White),
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };
        canvas.DrawRect(rect, paint);
    }

    private void DrawPageBackgroundTint(SKCanvas canvas, SKRect rect)
    {
        byte alpha = ViewportBackgroundPolicy.RenderedPageTintAlpha(PageBackgroundColor);
        if (alpha == 0)
            return;

        using var paint = new SKPaint
        {
            Color = GetCachedColor(PageBackgroundColor, SKColors.White).WithAlpha(alpha),
            Style = SKPaintStyle.Fill,
            BlendMode = SKBlendMode.Multiply,
            IsAntialias = false,
        };
        canvas.DrawRect(rect, paint);
    }

    private void DrawBlankPageLoadingSurface(SKCanvas canvas, float width, float height)
    {
        using var paint = new SKPaint
        {
            Color = GetCachedColor(PageBackgroundColor, SKColors.White),
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };
        canvas.DrawRect(new SKRect(0, 0, width, height), paint);
    }

    private void DrawLowZoomLineOverlay(SKCanvas canvas, SKRect visiblePdf)
    {
        IReadOnlyList<PdfGeometrySnapSegment> segments = LowZoomVisualSegments();
        if (_renderNavigationFastFrame ||
            segments.Count == 0 ||
            _zoom <= 0 ||
            _zoom > 0.55f)
        {
            return;
        }

        SKRect searchRect = visiblePdf;
        searchRect.Inflate(ScreenToPdfDistance(8f), ScreenToPdfDistance(8f));
        using var stroke = new SKPaint
        {
            Color = SKColors.Black.WithAlpha(145),
            StrokeWidth = 1.0f,
            IsAntialias = false,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Square,
        };

        foreach (PdfGeometrySnapSegment segment in segments)
        {
            if (!RectsIntersect(SegmentBounds(segment), searchRect))
                continue;

            canvas.DrawLine(PdfToScreen(segment.Start), PdfToScreen(segment.End), stroke);
        }
    }

    private IReadOnlyList<PdfGeometrySnapSegment> LowZoomVisualSegments()
    {
        if (_usingRasterSheetRender)
            return _rasterSheetVisualSegments;

        return _pdfSnapEnabled && IsPdfSnapCacheCurrent()
            ? _pdfSnapIndex.Segments
            : [];
    }

    private static SKRect SegmentBounds(PdfGeometrySnapSegment segment)
    {
        float left = Math.Min(segment.Start.X, segment.End.X);
        float top = Math.Min(segment.Start.Y, segment.End.Y);
        float right = Math.Max(segment.Start.X, segment.End.X);
        float bottom = Math.Max(segment.Start.Y, segment.End.Y);
        return new SKRect(left, top, right, bottom);
    }

}
