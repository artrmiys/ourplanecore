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
using OurPlanCore;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace OurPlanCore.Controls;

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
            frameWatch.Stop();
            ViewportPerformanceRecorder.RecordPaintFrame(
                _pageFolder,
                _zoom,
                "blank",
                frameWatch.ElapsedMilliseconds,
                frameWatch.ElapsedMilliseconds,
                0);
            return;
        }

        long pageBitmapMs = 0;
        long overlayMs = 0;
        long sheetOverlayPaintMs = 0;
        long measurementMs = 0;
        long markupMs = 0;
        long inProgressMs = 0;
        long labelMs = 0;
        long screenOverlayMs = 0;
        int visibleMeasurementCount = 0;
        IReadOnlyList<Measurement> activeMeasurements = ActivePageMeasurements();
        bool previousFastFrame = _renderNavigationFastFrame;
        _renderNavigationFastFrame = IsFastNavigationFrame(activeMeasurements.Count);
        StaticPageFramePaintResult pageFrame = StaticPageFramePaintResult.Bypassed;
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
            pageFrame = DrawPageFrame(canvas, e.Info, canvas.TotalMatrix, visiblePdf);
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
                if (!pageFrame.IncludesSheetOverlay &&
                    ViewportRenderPolicy.ShouldDrawSheetOverlay(_renderNavigationFastFrame, IsSheetOverlayPointEditing))
                {
                    long sheetOverlayStart = frameWatch.ElapsedMilliseconds;
                    DrawSheetOverlay(canvas, visiblePdf);
                    sheetOverlayPaintMs += frameWatch.ElapsedMilliseconds - sheetOverlayStart;
                }
                DrawSheetOverlayEditGuides(canvas);
                DrawSheetOverlaySelection(canvas);
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
            ViewportPerformanceRecorder.RecordPaintFrame(
                _pageFolder,
                _zoom,
                pageFrame.State,
                frameWatch.ElapsedMilliseconds,
                pageBitmapMs,
                inProgressMs);
            ReportSlowViewportFrame(
                frameWatch.ElapsedMilliseconds,
                activeMeasurements.Count,
                visibleMeasurementCount,
                pageBitmapMs,
                overlayMs,
                sheetOverlayPaintMs,
                measurementMs,
                markupMs,
                inProgressMs,
                labelMs,
                screenOverlayMs,
                pageFrame.State);
            _renderNavigationFastFrame = previousFastFrame;
        }
    }

    private void ReportSlowViewportFrame(
        long elapsedMs,
        int activeMeasurementCount,
        int visibleMeasurementCount,
        long pageBitmapMs,
        long overlayMs,
        long sheetOverlayPaintMs,
        long measurementMs,
        long markupMs,
        long inProgressMs,
        long labelMs,
        long screenOverlayMs,
        string pageFrameState)
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
            sheetOverlayPaintMs,
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
            $"renderedScale={_renderedScale:0.###}; overlay={(_sheetOverlayBitmap != null)}; pageFrame={pageFrameState}; " +
            $"timings=page:{pageBitmapMs} overlay:{overlayMs} sheetOverlay:{sheetOverlayPaintMs} " +
            $"measurements:{measurementMs} markups:{markupMs} " +
            $"inProgress:{inProgressMs} labels:{labelMs} chrome:{screenOverlayMs}ms");
    }

    private SKFilterQuality CurrentPageBitmapFilterQuality()
    {
        // Static raster is pinned and never re-rendered, so sample it for the
        // sharpest still image the interactive path allows:
        //  - motion: Low — cheap and clean; avoids the None nearest-neighbour
        //    shimmer the down-scaled high-res raster shows while zooming/panning.
        //  - at rest, zoomed IN past native resolution: None — nearest-neighbour
        //    keeps edges crisp instead of the soft Medium upscale (no shimmer on
        //    magnification since the image is not being minified).
        //  - at rest, at or below native: Medium — smooth minification.
        if (IsStaticRasterDisplayActive())
        {
            if (_renderNavigationFastFrame)
                return SKFilterQuality.Low;

            return _zoom > _bitmapScale ? SKFilterQuality.None : SKFilterQuality.Medium;
        }

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
        // The black vector overlay already draws these same segments crisply at all
        // zooms; skip the faint low-zoom pass to avoid a redundant second iteration.
        if (ShowBlackVectorOverlay)
            return;

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

    // Crisp black vector linework over the static raster (PlanSwift-style). Uses
    // the already-loaded page snap segments, so it is resolution-independent and
    // triggers no re-render: thin source lines stay razor-sharp at any zoom even
    // though the raster underneath softens above ~200%.
    private void DrawBlackVectorInkOverlay(SKCanvas canvas, SKRect visiblePdf)
    {
        if (!ShowBlackVectorOverlay || _zoom <= 0)
            return;

        IReadOnlyList<PdfGeometrySnapSegment> segments = LowZoomVisualSegments();
        if (segments.Count == 0)
            return;

        // On motion frames of very dense sheets, skip so paging/panning stays smooth.
        if (_renderNavigationFastFrame &&
            segments.Count > ViewportRenderPolicy.BlackVectorOverlayFastFrameSegmentCap)
        {
            return;
        }

        SKRect searchRect = visiblePdf;
        searchRect.Inflate(ScreenToPdfDistance(8f), ScreenToPdfDistance(8f));

        using var path = new SKPath();
        bool any = false;
        foreach (PdfGeometrySnapSegment segment in segments)
        {
            if (!RectsIntersect(SegmentBounds(segment), searchRect))
                continue;

            path.MoveTo(PdfToScreen(segment.Start));
            path.LineTo(PdfToScreen(segment.End));
            any = true;
        }

        if (!any)
            return;

        using var stroke = new SKPaint
        {
            Color = SKColors.Black,
            StrokeWidth = Math.Clamp(_zoom * 0.6f, 1.0f, 3.0f),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };
        canvas.DrawPath(path, stroke);
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
