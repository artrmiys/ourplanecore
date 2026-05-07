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
        Stopwatch frameWatch = Stopwatch.StartNew();
        var canvas = e.Surface.Canvas;
        canvas.Clear(GetCachedColor(ViewBackgroundColor, SKColors.White));

        if (_pageBitmap == null) return;
        bool previousFastFrame = _renderNavigationFastFrame;
        _renderNavigationFastFrame = IsFastNavigationFrame();
        try
        {
            SKRect visiblePdf = GetVisiblePdfRect();

            // ── PDF page bitmap ───────────────────────────────────────────────────
            // PDF point (px,py) → screen pixel (sx,sy):
            //   sx = (px - panX) * zoom
            //   sy = (py - panY) * zoom
            // bitmap pixel (bx,by) → screen pixel:
            //   sx = bx * zoom/bitmapScale - panX*zoom
            {
                using var bitmapPaint = new SKPaint
                {
                    IsAntialias = true,
                    FilterQuality = _renderNavigationFastFrame ? SKFilterQuality.Low : SKFilterQuality.Medium,
                };

                float visibleW = (float)ActualWidth / Math.Max(_zoom, 0.001f);
                float visibleH = (float)ActualHeight / Math.Max(_zoom, 0.001f);
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
                    canvas.DrawBitmap(_pageBitmap, src, dst, bitmapPaint);
                    DrawPdfLayerTraceGhost(canvas, dst);
                }
            }

            if (_showingPreviousPageDuringSwitch)
            {
                DrawPageSwitchLoadingVeil(canvas, (float)e.Info.Width, (float)e.Info.Height);
                return;
            }

            // ── Measurement overlay (PDF-point coordinate system) ─────────────────
            {
                var measMtx = SKMatrix.CreateScaleTranslation(
                    _zoom, _zoom, -_panX * _zoom, -_panY * _zoom);
                using var saved = new SKAutoCanvasRestore(canvas, true);
                canvas.SetMatrix(measMtx);
                if (ViewportRenderPolicy.ShouldDrawSheetOverlay(_renderNavigationFastFrame, IsSheetOverlayPointEditing))
                    DrawSheetOverlay(canvas);
                DrawSheetOverlayEditGuides(canvas);
                if (!_renderNavigationFastFrame)
                    DrawTransformOverlay(canvas);
                DrawMeasurements(canvas, visiblePdf);
                if (!_renderNavigationFastFrame)
                {
                    DrawPageAnnotations(canvas, visiblePdf);
                    DrawAiActionDraftPreview(canvas, visiblePdf);
                    DrawAiMarkers(canvas, visiblePdf);
                }
                DrawInProgress(canvas);
                DrawMeasurementLabels(canvas, visiblePdf);
            }

            DrawSheetHeaderOverlay(canvas, (float)e.Info.Width, (float)e.Info.Height);
            DrawSheetLegendOverlay(canvas, (float)e.Info.Width, (float)e.Info.Height);
        }
        finally
        {
            frameWatch.Stop();
            ReportSlowViewportFrame(frameWatch.ElapsedMilliseconds);
            _renderNavigationFastFrame = previousFastFrame;
        }
    }

    private void ReportSlowViewportFrame(long elapsedMs)
    {
        if (elapsedMs < ViewportRenderPolicy.SlowFrameLogMs)
            return;

        DateTime now = DateTime.UtcNow;
        if ((now - _lastSlowFrameLogAt).TotalSeconds < 2)
            return;

        _lastSlowFrameLogAt = now;
        AppLog.Info(
            $"Viewport slow frame {elapsedMs}ms; zoom={_zoom:0.###}; fast={_renderNavigationFastFrame}; " +
            $"page='{_pageFolder}'; activeMeasurements={ActivePageMeasurements().Count}; renderedScale={_renderedScale:0.###}; " +
            $"overlay={(_sheetOverlayBitmap != null)}");
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

}
