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
using SmartTakeoffs;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace SmartTakeoffs.Controls;

public sealed partial class PdfViewport
{
    // ═════════════════════════════════════════════════════════════════════════
    // Rendering
    // ═════════════════════════════════════════════════════════════════════════

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(GetCachedColor(ViewBackgroundColor, SKColors.White));

        if (_pageBitmap == null) return;
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
                FilterQuality = _isViewDragging ? SKFilterQuality.Low : SKFilterQuality.Medium,
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

        // ── Measurement overlay (PDF-point coordinate system) ─────────────────
        {
            var measMtx = SKMatrix.CreateScaleTranslation(
                _zoom, _zoom, -_panX * _zoom, -_panY * _zoom);
            using var saved = new SKAutoCanvasRestore(canvas, true);
            canvas.SetMatrix(measMtx);
            DrawMeasurements(canvas, visiblePdf);
            DrawPageAnnotations(canvas, visiblePdf);
            DrawAiActionDraftPreview(canvas, visiblePdf);
            DrawAiMarkers(canvas, visiblePdf);
            DrawInProgress(canvas);
        }

        DrawSheetHeaderOverlay(canvas, (float)e.Info.Width, (float)e.Info.Height);
        DrawSheetLegendOverlay(canvas, (float)e.Info.Width, (float)e.Info.Height);
    }

}
