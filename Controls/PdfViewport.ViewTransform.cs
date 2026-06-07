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
        PostStatus($"Zoom: {_zoom * 100:F0}%");
    }

    private void BeginFastNavigation()
    {
        _isFastNavigating = true;
        _lastFastNavigationAt = DateTime.UtcNow;
        PausePreviewPrefetchFor(ViewportRenderPolicy.PreviewPrefetchNavigationQuietMs);
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

        _navigationIdleTimer.Stop();
        _isFastNavigating = false;
        MaybeRequestSheetOverlayRenderScaleRefresh();
        if (TryApplyReadyRasterSheetForCurrentZoom())
        {
            RequestRepaint();
            return;
        }

        if (!TryUpgradeRasterSheetToReadyDpiForCurrentZoom())
            QueueDetailRenderIfNeeded(force: false);
        RequestRepaint();
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
                return;
            }

            if (TrySwitchRasterSheetToFastPreviewForLowZoom())
                return;

            if (TryApplyResponsiveRasterSheetDpiForCurrentZoom())
                return;

            if (TryUpgradeRasterSheetToReadyDpiForCurrentZoom())
                return;

            QueueDetailRenderIfNeeded(force);
            return;
        }

        if (TryApplyReadyRasterSheetForCurrentZoom())
            return;

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

    private void PostPointerStatus(SKPoint p)
    {
        DateTime now = DateTime.UtcNow;
        if ((now - _lastPointerStatusAt).TotalMilliseconds < 50)
            return;

        _lastPointerStatusAt = now;
        string scaleStr = ScaleMetersPerPt > 0
            ? $"  |  {PdfSheetMetadataService.FormatImperialScale(ScaleMetersPerPt)}"
            : "  |  scale: not set";
        PostStatus($"x={p.X:F1}  y={p.Y:F1} pt  |  zoom: {_zoom * 100:F0}%{scaleStr}");
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
