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
                using var bitmapPaint = new SKPaint
                {
                    IsAntialias = false,
                    FilterQuality = _renderNavigationFastFrame
                        ? SKFilterQuality.Low
                        : _zoom > _bitmapScale * 1.05f
                        ? SKFilterQuality.Low
                        : SKFilterQuality.Medium,
                };

                float visibleW = ViewportCanvasWidth / Math.Max(_zoom, 0.001f);
                float visibleH = ViewportCanvasHeight / Math.Max(_zoom, 0.001f);
                float srcLeft = Math.Clamp(_panX * _bitmapScale, 0, _pageBitmap.Width);
                float srcTop = Math.Clamp(_panY * _bitmapScale, 0, _pageBitmap.Height);
                float srcRight = Math.Clamp((_panX + visibleW) * _bitmapScale, 0, _pageBitmap.Width);
                float srcBottom = Math.Clamp((_panY + visibleH) * _bitmapScale, 0, _pageBitmap.Height);

                if (srcRight > srcLeft && srcBottom > srcTop)
                {
                    var src = new SKRect(srcLeft, srcTop, srcRight, srcBottom);
                    var dst = new SKRect(
                        (srcLeft / _bitmapScale - _panX) * _zoom,
                        (srcTop / _bitmapScale - _panY) * _zoom,
                        (srcRight / _bitmapScale - _panX) * _zoom,
                        (srcBottom / _bitmapScale - _panY) * _zoom);
                    bool detailCoversVisiblePage = DetailRenderCoversVisibleViewForPaint();
                    DrawPagePaperUnderlay(canvas, dst);
                    if (!detailCoversVisiblePage)
                        canvas.DrawBitmap(_pageBitmap, src, dst, bitmapPaint);
                    DrawDetailRenderTile(canvas);
                    DrawPageBackgroundTint(canvas, dst);
                    DrawPdfLayerTraceGhost(canvas, dst);
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
        }
        finally
        {
            ClearPaintJoistLayoutCache();
            frameWatch.Stop();
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

}
