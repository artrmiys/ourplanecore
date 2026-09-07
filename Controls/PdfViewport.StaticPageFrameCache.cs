using System;
using System.Runtime.CompilerServices;
using SkiaSharp;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    private const long StaticPageFrameMaxPixels = 40_000_000;

    private SKBitmap? _staticPageFrameBitmap;
    private StaticPageFrameKey _staticPageFrameKey;
    private bool _staticPageFrameKeyValid;
    private DateTime _lastStaticPageFrameWarningAt;
    private DateTime _staticPageFrameAllocationRetryAfterUtc;

    private StaticPageFramePaintResult DrawPageFrame(
        SKCanvas canvas,
        SKImageInfo canvasInfo,
        SKImageInfo rawInfo,
        SKMatrix rootMatrix,
        SKRect visiblePdf)
    {
        // The frame bitmap must match the raw surface pixel size, not e.Info:
        // with IgnorePixelScaling the canvas is in WPF units while rootMatrix
        // carries the display scale, and the replay blits under ResetMatrix
        // (device pixels). A WPF-unit bitmap on a 125-150% laptop display
        // crops the right/bottom of the sheet.
        if (!CanUseStaticPageFrameCache(rawInfo))
        {
            DrawPageBitmapAndStaticOverlays(canvas, visiblePdf);
            return StaticPageFramePaintResult.Bypassed;
        }

        StaticPageFrameKey key = BuildStaticPageFrameKey(canvasInfo, rootMatrix);
        if (_staticPageFrameKeyValid &&
            key == _staticPageFrameKey &&
            _staticPageFrameBitmap != null)
        {
            try
            {
                DrawStaticPageFrameBitmap(canvas);
                return new StaticPageFramePaintResult("hit", IncludesSheetOverlay(key));
            }
            catch (Exception ex)
            {
                ClearStaticPageFrameCache();
                ReportStaticPageFrameFailure(ex);
                DrawPageBitmapAndStaticOverlays(canvas, visiblePdf);
                return StaticPageFramePaintResult.Bypassed;
            }
        }

        // A wheel/pan gesture changes the view transform every frame. Building a
        // retained frame there only adds an extra copy; build once motion ends.
        if (_isFastNavigating)
        {
            DrawPageBitmapAndStaticOverlays(canvas, visiblePdf);
            return StaticPageFramePaintResult.Bypassed;
        }

        if (!EnsureStaticPageFrameBitmap(rawInfo))
        {
            DrawPageBitmapAndStaticOverlays(canvas, visiblePdf);
            return StaticPageFramePaintResult.Bypassed;
        }

        try
        {
            using var frameCanvas = new SKCanvas(_staticPageFrameBitmap!);
            frameCanvas.Clear(GetCachedColor(ViewBackgroundColor, SKColors.White));
            frameCanvas.SetMatrix(rootMatrix);
            DrawPageBitmapAndStaticOverlays(frameCanvas, visiblePdf);

            bool includesSheetOverlay = ShouldIncludeSheetOverlayInStaticPageFrame();
            if (includesSheetOverlay)
            {
                var overlayMatrix = SKMatrix.CreateScaleTranslation(
                    _zoom,
                    _zoom,
                    -_panX * _zoom,
                    -_panY * _zoom);
                using var saved = new SKAutoCanvasRestore(frameCanvas, true);
                frameCanvas.Concat(ref overlayMatrix);
                DrawSheetOverlay(frameCanvas, visiblePdf);
            }

            frameCanvas.Flush();
            // SelectPageBitmapForPaint may create a mip while building the frame;
            // capture the final state so the next pointer repaint is a true hit.
            _staticPageFrameKey = BuildStaticPageFrameKey(canvasInfo, rootMatrix);
            _staticPageFrameKeyValid = true;
            DrawStaticPageFrameBitmap(canvas);
            return new StaticPageFramePaintResult("miss", includesSheetOverlay);
        }
        catch (Exception ex)
        {
            _staticPageFrameKeyValid = false;
            ReportStaticPageFrameFailure(ex);
            DrawPageBitmapAndStaticOverlays(canvas, visiblePdf);
            return StaticPageFramePaintResult.Bypassed;
        }
    }

    private void DrawPageBitmapAndStaticOverlays(SKCanvas canvas, SKRect visiblePdf)
    {
        // Static raster mode is intentionally one pinned bitmap. Ignore any stale
        // detail tiles left from an earlier live-render state instead of baking a
        // transient tile into the retained frame without an invalidation key.
        bool allowDetailRender = !IsStaticRasterDisplayActive();
        bool detailCoversVisiblePage = allowDetailRender && DetailRenderCoversVisibleViewForPaint();
        (SKBitmap paintBitmap, float paintScale) = detailCoversVisiblePage
            ? (_pageBitmap!, _bitmapScale)
            : SelectPageBitmapForPaint();
        if (paintScale <= 0)
            paintScale = _bitmapScale;

        using var bitmapPaint = new SKPaint
        {
            IsAntialias = false,
            FilterQuality = CurrentPageBitmapFilterQuality(),
        };
        if (paintBitmap != _pageBitmap)
            bitmapPaint.FilterQuality = SKFilterQuality.Low;

        float visibleW = ViewportCanvasWidth / Math.Max(_zoom, 0.001f);
        float visibleH = ViewportCanvasHeight / Math.Max(_zoom, 0.001f);
        float srcLeft = Math.Clamp(_panX * paintScale, 0, paintBitmap.Width);
        float srcTop = Math.Clamp(_panY * paintScale, 0, paintBitmap.Height);
        float srcRight = Math.Clamp((_panX + visibleW) * paintScale, 0, paintBitmap.Width);
        float srcBottom = Math.Clamp((_panY + visibleH) * paintScale, 0, paintBitmap.Height);
        if (srcRight <= srcLeft || srcBottom <= srcTop)
            return;

        var src = new SKRect(srcLeft, srcTop, srcRight, srcBottom);
        var dst = new SKRect(
            (srcLeft / paintScale - _panX) * _zoom,
            (srcTop / paintScale - _panY) * _zoom,
            (srcRight / paintScale - _panX) * _zoom,
            (srcBottom / paintScale - _panY) * _zoom);
        DrawPagePaperUnderlay(canvas, dst);
        if (!detailCoversVisiblePage)
            canvas.DrawBitmap(paintBitmap, src, dst, bitmapPaint);
        if (allowDetailRender)
            DrawDetailRenderTile(canvas);
        DrawPageBackgroundTint(canvas, dst);
        DrawPdfLayerTraceGhost(canvas, dst);
        DrawLowZoomLineOverlay(canvas, visiblePdf);
        // The optional black PDF vector overlay is deliberately retained with the
        // raster: it stays crisp at deep zoom without rebuilding on Area movement.
        DrawBlackVectorInkOverlay(canvas, visiblePdf);
    }

    private bool CanUseStaticPageFrameCache(SKImageInfo rawInfo) =>
        IsStaticRasterDisplayActive() &&
        _pageBitmap != null &&
        !_showingPreviousPageDuringSwitch &&
        !_pdfLayerTraceEnabled &&
        !IsSheetOverlayPointEditing &&
        !_draggingSheetOverlay &&
        !_sheetOverlayTransformPreviewActive &&
        rawInfo.Width > 0 &&
        rawInfo.Height > 0 &&
        (long)rawInfo.Width * rawInfo.Height <= StaticPageFrameMaxPixels;

    private bool ShouldIncludeSheetOverlayInStaticPageFrame() =>
        _sheetOverlayBitmap != null &&
        ViewportRenderPolicy.ShouldDrawSheetOverlay(
            _renderNavigationFastFrame,
            IsSheetOverlayPointEditing);

    private static bool IncludesSheetOverlay(StaticPageFrameKey key) =>
        key.SheetOverlayBitmapIdentity != 0 && key.SheetOverlayVisible;

    private bool EnsureStaticPageFrameBitmap(SKImageInfo rawInfo)
    {
        if (_staticPageFrameBitmap is { } bitmap &&
            bitmap.Width == rawInfo.Width &&
            bitmap.Height == rawInfo.Height &&
            bitmap.ReadyToDraw)
        {
            return true;
        }

        if (DateTime.UtcNow < _staticPageFrameAllocationRetryAfterUtc)
            return false;

        ClearStaticPageFrameCache();
        try
        {
            var info = new SKImageInfo(
                rawInfo.Width,
                rawInfo.Height,
                SKColorType.Bgra8888,
                SKAlphaType.Premul);
            var candidate = new SKBitmap(info);
            if (!candidate.ReadyToDraw)
            {
                candidate.Dispose();
                throw new InvalidOperationException(
                    $"Could not allocate a {rawInfo.Width}x{rawInfo.Height} static page frame.");
            }

            _staticPageFrameBitmap = candidate;
            _staticPageFrameAllocationRetryAfterUtc = DateTime.MinValue;
            return true;
        }
        catch (Exception ex)
        {
            _staticPageFrameBitmap?.Dispose();
            _staticPageFrameBitmap = null;
            _staticPageFrameAllocationRetryAfterUtc = DateTime.UtcNow.AddSeconds(5);
            ReportStaticPageFrameFailure(ex);
            return false;
        }
    }

    private void DrawStaticPageFrameBitmap(SKCanvas canvas)
    {
        using var saved = new SKAutoCanvasRestore(canvas, true);
        canvas.ResetMatrix();
        using var paint = new SKPaint
        {
            IsAntialias = false,
            FilterQuality = SKFilterQuality.None,
        };
        canvas.DrawBitmap(_staticPageFrameBitmap!, 0, 0, paint);
    }

    private StaticPageFrameKey BuildStaticPageFrameKey(SKImageInfo canvasInfo, SKMatrix rootMatrix) => new(
        _pageBitmapGeneration,
        RuntimeHelpers.GetHashCode(_pageBitmap!),
        canvasInfo.Width,
        canvasInfo.Height,
        rootMatrix.ScaleX,
        rootMatrix.SkewX,
        rootMatrix.TransX,
        rootMatrix.SkewY,
        rootMatrix.ScaleY,
        rootMatrix.TransY,
        rootMatrix.Persp0,
        rootMatrix.Persp1,
        rootMatrix.Persp2,
        _pdfW,
        _pdfH,
        _zoom,
        _panX,
        _panY,
        _bitmapScale,
        _renderNavigationFastFrame,
        ViewBackgroundColor ?? "",
        PageBackgroundColor ?? "",
        ShowBlackVectorOverlay,
        _rasterSheetVisualContentGeneration,
        RuntimeHelpers.GetHashCode(_rasterSheetVisualSegments),
        _rasterSheetVisualSegments.Count,
        _sheetOverlayBitmap == null ? 0 : RuntimeHelpers.GetHashCode(_sheetOverlayBitmap),
        ShouldIncludeSheetOverlayInStaticPageFrame(),
        _sheetOverlayWidthPt,
        _sheetOverlayHeightPt,
        _sheetOverlayOffsetXPt,
        _sheetOverlayOffsetYPt,
        _sheetOverlayScale,
        _sheetOverlayRotationDegrees,
        _sheetOverlayBitmapScale,
        _pageBitmapMipGeneration,
        _pageBitmapMipFactor,
        _pageBitmapMip == null ? 0 : RuntimeHelpers.GetHashCode(_pageBitmapMip));

    private void ClearStaticPageFrameCache()
    {
        _staticPageFrameKeyValid = false;
        _staticPageFrameBitmap?.Dispose();
        _staticPageFrameBitmap = null;
        _staticPageFrameAllocationRetryAfterUtc = DateTime.MinValue;
    }

    private void ReportStaticPageFrameFailure(Exception ex)
    {
        DateTime now = DateTime.UtcNow;
        if ((now - _lastStaticPageFrameWarningAt).TotalSeconds < 5)
            return;

        _lastStaticPageFrameWarningAt = now;
        AppLog.Warn(ex, "Viewport static page frame cache failed; using direct paint.");
    }

    private readonly record struct StaticPageFrameKey(
        long PageBitmapGeneration,
        int PageBitmapIdentity,
        int CanvasWidth,
        int CanvasHeight,
        float RootScaleX,
        float RootSkewX,
        float RootTransX,
        float RootSkewY,
        float RootScaleY,
        float RootTransY,
        float RootPersp0,
        float RootPersp1,
        float RootPersp2,
        float PageWidthPt,
        float PageHeightPt,
        float Zoom,
        float PanX,
        float PanY,
        float BitmapScale,
        bool FastFrame,
        string ViewBackground,
        string PageBackground,
        bool BlackVectorOverlay,
        int VisualContentGeneration,
        int VisualSegmentIdentity,
        int VisualSegmentCount,
        int SheetOverlayBitmapIdentity,
        bool SheetOverlayVisible,
        float SheetOverlayWidthPt,
        float SheetOverlayHeightPt,
        float SheetOverlayOffsetXPt,
        float SheetOverlayOffsetYPt,
        float SheetOverlayScale,
        float SheetOverlayRotationDegrees,
        float SheetOverlayBitmapScale,
        long PageMipGeneration,
        float PageMipFactor,
        int PageMipIdentity);

    private readonly record struct StaticPageFramePaintResult(
        string State,
        bool IncludesSheetOverlay)
    {
        public static StaticPageFramePaintResult Bypassed => new("bypass", false);
    }
}
