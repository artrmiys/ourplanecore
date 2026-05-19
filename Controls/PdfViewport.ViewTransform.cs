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
        RequestRepaint();
        PostStatus($"Zoom: {_zoom * 100:F0}%");
    }

    private void BeginFastNavigation()
    {
        _isFastNavigating = true;
        _navigationIdleTimer.Stop();
        _navigationIdleTimer.Start();
    }

    private void EndFastNavigation()
    {
        if (!_isFastNavigating)
            return;

        _navigationIdleTimer.Stop();
        _isFastNavigating = false;
        RequestRepaint();
    }

    private bool IsFastNavigationFrame(int activeMeasurementCount)
    {
        bool hasBlockingInteraction =
            _draggingMeasurement ||
            _draggingVertex ||
            _draggingAnnotation ||
            _draggingAnnotationVertex ||
            _draggingTransformScale ||
            _draggingTransformRotate ||
            _boxSelecting ||
            _drawPts.Count > 0 ||
            _scalePts.Count > 0 ||
            _joistDirectionMeasurement != null;

        return ViewportRenderPolicy.ShouldUseFastNavigationFrame(
            SimplifyNavigationRendering,
            _isFastNavigating,
            _zoom,
            activeMeasurementCount,
            hasBlockingInteraction);
    }

    private float CurrentRenderScale()
    {
        if (_zoom <= 0)
            return 1.0f;

        return ViewportRenderPolicy.SelectRenderScale(_zoom, RenderScaleSteps, _pdfW, _pdfH);
    }

    private void RerenderForZoomIfNeeded(bool force)
    {
        if (string.IsNullOrWhiteSpace(_pdfPath) || _pdfW <= 0 || _pdfH <= 0)
            return;

        float desired = CurrentRenderScale();
        if (!force && _renderedScale > 0)
        {
            float ratio = desired / _renderedScale;
            if (ratio > 0.72f && ratio < 1.38f)
                return;
        }

        if (_usingLayerRenderer)
        {
            QueueLayerRender(resetLayerStates: false, renderScale: desired);
            return;
        }

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

        if (!force && _renderedScale > 0)
        {
            float desired = CurrentRenderScale();
            float ratio = desired / _renderedScale;
            if (ratio > 0.72f && ratio < 1.38f)
                return;
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

    private SKPoint ScreenToPdf(float sx, float sy)
        => new(sx / _zoom + _panX, sy / _zoom + _panY);

    private SKPoint PdfToScreen(SKPoint point) =>
        new((point.X - _panX) * _zoom, (point.Y - _panY) * _zoom);

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
