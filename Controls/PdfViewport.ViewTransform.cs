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
using OurPlaneCore;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private void ApplyZoom(float factor, float screenX, float screenY)
    {
        float newZoom = Math.Clamp(_zoom * factor, ZoomMin, ZoomMax);
        if (Math.Abs(newZoom - _zoom) < ViewportConstants.GeometryEpsilon) return;

        BeginFastNavigation();
        // Keep the PDF point under cursor fixed
        float pdfX = screenX / _zoom + _panX;
        float pdfY = screenY / _zoom + _panY;
        _zoom  = newZoom;
        _panX  = pdfX - screenX / _zoom;
        _panY  = pdfY - screenY / _zoom;
        ScheduleRerenderForZoom(force: false);
        MaybeRequestSheetOverlayRenderScaleRefresh();
        RequestRepaint();
        NotifyZoomChanged();
    }

    private void NotifyZoomChanged() =>
        ZoomChanged?.Invoke(_zoom);

    private void SetLastPointerPdf(SKPoint? point)
    {
        if (point.HasValue &&
            _lastPointerPdf.HasValue &&
            Math.Abs(point.Value.X - _lastPointerPdf.Value.X) < 0.05f &&
            Math.Abs(point.Value.Y - _lastPointerPdf.Value.Y) < 0.05f)
        {
            return;
        }

        _lastPointerPdf = point;
        PointerPdfChanged?.Invoke(_lastPointerPdf);
    }

    private void BeginFastNavigation()
    {
        _isFastNavigating = true;
        _lastFastNavigationAt = DateTime.UtcNow;
        _rasterSheetQualityRestoreVersion++;
        _rasterSheetQualityRestoreQueuedVersion = 0;
        PausePreviewPrefetchFor(ViewportRenderPolicy.PreviewPrefetchNavigationQuietMs);
        QueueCurrentRasterSheetMotionWarmup();
        _navigationIdleTimer.Stop();
        _navigationIdleTimer.Start();
    }

    public TimeSpan NavigationQuietDelay(TimeSpan quietWindow)
    {
        if (quietWindow <= TimeSpan.Zero || _lastFastNavigationAt == DateTime.MinValue)
            return TimeSpan.Zero;

        if (_isFastNavigating)
            return quietWindow;

        TimeSpan idle = DateTime.UtcNow - _lastFastNavigationAt;
        if (idle < TimeSpan.Zero || idle >= quietWindow)
            return TimeSpan.Zero;

        return quietWindow - idle;
    }

    private void EndFastNavigation()
    {
        if (!_isFastNavigating)
            return;

        if (ShouldDeferFastNavigationEndForPointer())
        {
            _navigationIdleTimer.Stop();
            _navigationIdleTimer.Start();
            return;
        }

        _navigationIdleTimer.Stop();
        _isFastNavigating = false;
        MaybeRequestSheetOverlayRenderScaleRefresh();
        PageInfo rasterPage = CurrentRasterSheetPageInfo();
        int targetDpi = TargetRasterSheetDpiForCurrentZoom();
        if (ShouldHoldHeavyRasterSheetDpiAfterMotion(targetDpi))
        {
            QueueRasterSheetQualityRestoreAfterMotion(rasterPage);
            if (TryHoldHeavyRasterSheetDpiForRecentMotion(
                    rasterPage,
                    RasterSheetCacheService.RenderScaleToDpi(_bitmapScale),
                    targetDpi))
            {
                QueueDetailRenderOverRasterSheetIfNeeded(force: false);
                RequestRepaint();
                return;
            }
        }

        if (TryApplyReadyRasterSheetForCurrentZoom())
        {
            QueueDetailRenderOverRasterSheetIfNeeded(force: false);
            RequestRepaint();
            return;
        }

        // Every idle branch must end with a detail request: a queued raster
        // DPI upgrade takes seconds to build, and without a tile request the
        // view stays blurry the whole time even though a sharp visible tile
        // is 10-500ms away.
        if (!TryUpgradeRasterSheetToReadyDpiForCurrentZoom())
            QueueDetailRenderIfNeeded(force: false);
        else
            QueueDetailRenderOverRasterSheetIfNeeded(force: false);
        RequestRepaint();
    }

    private bool ShouldDeferFastNavigationEndForPointer()
    {
        if (!_dragStart.HasValue)
            return false;

        return Mouse.MiddleButton == MouseButtonState.Pressed ||
               Mouse.RightButton == MouseButtonState.Pressed ||
               _tool == ViewerTool.Pan &&
               Mouse.LeftButton == MouseButtonState.Pressed;
    }

    private bool IsFastNavigationFrame(int activeMeasurementCount)
    {
        bool hasInteractiveMotion =
            _isFastNavigating ||
            _draggingMeasurement ||
            _draggingVertex ||
            _draggingAnnotation ||
            _draggingAnnotationVertex ||
            _draggingTransformScale ||
            _draggingTransformRotate ||
            _boxSelecting ||
            _aiCropNoteDragging ||
            IsSheetOverlayPointEditing;

        bool hasBlockingInteraction =
            _drawPts.Count > 0 ||
            _scalePts.Count > 0 ||
            _joistDirectionMeasurement != null;

        return ViewportRenderPolicy.ShouldUseFastNavigationFrame(
            SimplifyNavigationRendering,
            hasInteractiveMotion,
            _zoom,
            activeMeasurementCount,
            hasBlockingInteraction);
    }

    private float CurrentRenderScale()
    {
        if (_zoom <= 0)
            return 1.0f;

        if (_zoom < ViewportRenderPolicy.FarZoomFastFrameThreshold)
            return ViewportRenderPolicy.InitialPagePreviewRenderScale;

        return ViewportRenderPolicy.SelectRenderScale(_zoom, RenderScaleSteps, _pdfW, _pdfH);
    }

    private float CurrentBaseRenderScale()
    {
        float desired = CurrentRenderScale();
        return ViewportRenderPolicy.ShouldUseDetailRender(_zoom, _bitmapScale)
            ? Math.Min(desired, ViewportRenderPolicy.ResponsiveMinRenderScale)
            : desired;
    }

    private void RerenderForZoomIfNeeded(bool force)
    {
        if (string.IsNullOrWhiteSpace(_pdfPath) || _pdfW <= 0 || _pdfH <= 0)
            return;

        if (_showingPreviousPageDuringSwitch && !_usingLayerRenderer)
            return;

        if (_usingRasterSheetRender)
        {
            if (_usingRasterSheetOverviewRender &&
                ShouldUseRasterSheetForCurrentZoom() &&
                TryApplyReadyRasterSheetForCurrentZoom())
            {
                QueueDetailRenderOverRasterSheetIfNeeded(force);
                return;
            }

            if (TrySwitchRasterSheetToFastPreviewForLowZoom())
                return;

            if (TryApplyResponsiveRasterSheetDpiForCurrentZoom())
            {
                QueueDetailRenderOverRasterSheetIfNeeded(force);
                return;
            }

            if (TryUpgradeRasterSheetToReadyDpiForCurrentZoom())
            {
                QueueDetailRenderOverRasterSheetIfNeeded(force);
                return;
            }

            QueueDetailRenderIfNeeded(force);
            return;
        }

        if (TryApplyReadyRasterSheetForCurrentZoom())
        {
            QueueDetailRenderOverRasterSheetIfNeeded(force);
            return;
        }

        if (_zoom < ViewportRenderPolicy.ZoomRefreshMinZoom)
        {
            QueueLowZoomBitmapDowngradeIfNeeded();
            return;
        }

        bool needsDetailRender = ViewportRenderPolicy.ShouldUseDetailRender(_zoom, _bitmapScale);
        if (needsDetailRender)
        {
            QueueDetailRenderIfNeeded(force);
            if (ViewportRenderPolicy.ShouldSkipFullRefreshDuringDetail(_bitmapScale))
                return;
            if (!force && _isFastNavigating)
                return;
        }

        float desired = CurrentBaseRenderScale();
        if (!force && _renderedScale > 0)
        {
            float ratio = desired / _renderedScale;
            if (ratio > 0.72f && ratio < 1.38f)
                return;
        }

        if (_usingLayerRenderer)
        {
            bool cacheOnlyBaseRefresh = ViewportRenderPolicy.ShouldPreferDetailRenderOverFullRefresh(_zoom, _bitmapScale);
            QueueLayerRender(
                resetLayerStates: false,
                renderScale: desired,
                allowImmediateCache: !cacheOnlyBaseRefresh,
                allowLiveRender: !cacheOnlyBaseRefresh);
            return;
        }

        if (ViewportRenderPolicy.ShouldPreferDetailRenderOverFullRefresh(_zoom, _bitmapScale))
            return;

        try
        {
            QueueDocnetRender(desired);
        }
        catch (Exception ex)
        {
            PostStatus($"Render error: {ex.Message}");
        }
    }

    private void QueueDetailRenderOverRasterSheetIfNeeded(bool force)
    {
        // Detail tiles are the sharpness layer for the visible region: they may
        // paint over any pdf-backed base — the raster sheet, its low-dpi
        // overview, or a plain preview bitmap while a higher-dpi sheet is still
        // building. That build takes seconds; a visible tile takes ~10-500ms,
        // so gating tiles on the finished raster read as multi-second blur on
        // first deep zoom. Only image-sourced rasters and the PDF-layer
        // renderer manage their own sharpness.
        if ((_usingRasterSheetRender &&
             _rasterSheetSource != null &&
             RasterSheetCacheService.IsSourceImageRaster(_rasterSheetSource)) ||
            _pdfLayersLoadedForPage ||
            _usingLayerRenderer ||
            !ViewportRenderPolicy.ShouldUseDetailRender(_zoom, _bitmapScale))
        {
            DetailRenderDiag(
                "raster-guard" +
                (_pdfLayersLoadedForPage || _usingLayerRenderer ? "-layers" : "") +
                (!ViewportRenderPolicy.ShouldUseDetailRender(_zoom, _bitmapScale) ? "-nodetail" : ""));
            return;
        }

        QueueDetailRenderIfNeeded(force);
    }

    private void ScheduleRerenderForZoom(bool force)
    {
        if (string.IsNullOrWhiteSpace(_pdfPath) || _pdfW <= 0 || _pdfH <= 0)
            return;

        if (_usingRasterSheetRender)
        {
            if (_zoom < ViewportRenderPolicy.RasterSheetDisplayExitZoom)
            {
                _zoomRerenderForce = true;
                _zoomRerenderTimer.Stop();
                _zoomRerenderTimer.Start();
                return;
            }

            _zoomRerenderForce = _zoomRerenderForce || force;
            _zoomRerenderTimer.Stop();
            _zoomRerenderTimer.Start();
            return;
        }

        if (_zoom < ViewportRenderPolicy.ZoomRefreshMinZoom)
        {
            if (ShouldRequestLowZoomBitmapDowngrade())
            {
                _zoomRerenderForce = _zoomRerenderForce || force;
                _zoomRerenderTimer.Stop();
                _zoomRerenderTimer.Start();
            }
            return;
        }

        if (ShouldUseRasterSheetForCurrentZoom() && _rasterSheetSource?.Enabled == true)
        {
            _zoomRerenderForce = _zoomRerenderForce || force;
            _zoomRerenderTimer.Stop();
            _zoomRerenderTimer.Start();
            return;
        }

        if (!force && _renderedScale > 0)
        {
            float desired = CurrentBaseRenderScale();
            float ratio = desired / _renderedScale;
            if (!ViewportRenderPolicy.ShouldUseDetailRender(_zoom, _bitmapScale) &&
                !ViewportRenderPolicy.ShouldUseZoomRefreshRender(_zoom, _bitmapScale) &&
                ratio > 0.72f &&
                ratio < 1.38f)
            {
                return;
            }
        }

        _zoomRerenderForce = _zoomRerenderForce || force;
        _zoomRerenderTimer.Stop();
        _zoomRerenderTimer.Start();
    }

    private bool ShouldRequestLowZoomBitmapDowngrade()
    {
        if (_pageBitmap == null || _bitmapScale <= 0)
            return false;

        return ViewportRenderPolicy.ShouldPreferLowerScalePageBitmapForNavigation(
            _zoom,
            _bitmapScale,
            CurrentBaseRenderScale());
    }

    private void QueueLowZoomBitmapDowngradeIfNeeded()
    {
        if (!ShouldRequestLowZoomBitmapDowngrade())
            return;

        float desired = CurrentBaseRenderScale();
        if (_usingLayerRenderer)
        {
            QueueLayerRender(
                resetLayerStates: false,
                renderScale: desired,
                allowImmediateCache: true,
                allowLiveRender: true,
                allowMemoryBitmap: true);
            return;
        }

        QueueDocnetRender(desired);
    }

    private SKPoint ScreenToPdf(float sx, float sy)
        => new(sx / _zoom + _panX, sy / _zoom + _panY);

    private SKPoint PdfToScreen(SKPoint point) =>
        new((point.X - _panX) * _zoom, (point.Y - _panY) * _zoom);

    public Point PdfToViewPoint(float pdfX, float pdfY)
    {
        SKPoint canvas = PdfToScreen(new SKPoint(pdfX, pdfY));
        double actualWidth = Math.Max(ActualWidth, 0.001);
        double actualHeight = Math.Max(ActualHeight, 0.001);
        double scaleX = ViewportCanvasWidth > 0 ? ViewportCanvasWidth / actualWidth : 1.0;
        double scaleY = ViewportCanvasHeight > 0 ? ViewportCanvasHeight / actualHeight : 1.0;
        return new Point(canvas.X / Math.Max(scaleX, 0.001), canvas.Y / Math.Max(scaleY, 0.001));
    }

    private float ScreenToPdfDistance(float pixels) => pixels / _zoom;

    private float PdfToScreenDistance(float pts) => pts * _zoom;

    private SKRect GetVisiblePdfRect(float screenPadding = 64f)
    {
        float safeZoom = Math.Max(_zoom, 0.001f);
        float pad = screenPadding / safeZoom;
        float visibleW = ViewportCanvasWidth / safeZoom;
        float visibleH = ViewportCanvasHeight / safeZoom;
        return new SKRect(
            _panX - pad,
            _panY - pad,
            _panX + visibleW + pad,
            _panY + visibleH + pad);
    }

    private SKColor GetCachedColor(string hex, SKColor fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return fallback;

        if (_colorCache.TryGetValue(hex, out SKColor color))
            return color;

        color = ParseColor(hex, fallback);
        _colorCache[hex] = color;
        return color;
    }

    private static SKColor ParseColor(string hex, SKColor fallback)
    {
        try
        {
            return SKColor.Parse(hex);
        }
        catch
        {
            return fallback;
        }
    }

}
