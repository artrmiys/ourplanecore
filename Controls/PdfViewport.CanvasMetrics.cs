using System;
using System.Windows;
using OurPlanCore;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    private float _canvasWidth;
    private float _canvasHeight;
    private DateTime _lastCanvasMetricsLogAt = DateTime.MinValue;

    private float ViewportCanvasWidth =>
        _canvasWidth > 0
            ? _canvasWidth
            : CanvasSize.Width > 0
                ? CanvasSize.Width
                : (float)Math.Max(ActualWidth, 0);

    private float ViewportCanvasHeight =>
        _canvasHeight > 0
            ? _canvasHeight
            : CanvasSize.Height > 0
                ? CanvasSize.Height
                : (float)Math.Max(ActualHeight, 0);

    private bool HasViewportCanvasSize => ViewportCanvasWidth > 0 && ViewportCanvasHeight > 0;

    private bool UpdateCanvasMetrics(int width, int height)
    {
        float cleanWidth = Math.Max(0, width);
        float cleanHeight = Math.Max(0, height);
        bool changed = Math.Abs(_canvasWidth - cleanWidth) > 0.5f ||
                       Math.Abs(_canvasHeight - cleanHeight) > 0.5f;

        _canvasWidth = cleanWidth;
        _canvasHeight = cleanHeight;
        LogCanvasMetricsIfScaled();
        return changed;
    }

    private Point ViewPointToCanvas(Point point)
    {
        double actualWidth = ActualWidth;
        double actualHeight = ActualHeight;
        if (actualWidth <= 0 || actualHeight <= 0)
            return point;

        double scaleX = ViewportCanvasWidth > 0 ? ViewportCanvasWidth / actualWidth : 1.0;
        double scaleY = ViewportCanvasHeight > 0 ? ViewportCanvasHeight / actualHeight : 1.0;
        if (Math.Abs(scaleX - 1.0) < 0.0001 && Math.Abs(scaleY - 1.0) < 0.0001)
            return point;

        return new Point(point.X * scaleX, point.Y * scaleY);
    }

    private void LogCanvasMetricsIfScaled()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0 || _canvasWidth <= 0 || _canvasHeight <= 0)
            return;

        double scaleX = _canvasWidth / ActualWidth;
        double scaleY = _canvasHeight / ActualHeight;
        if (Math.Abs(scaleX - 1.0) < 0.01 && Math.Abs(scaleY - 1.0) < 0.01)
            return;

        DateTime now = DateTime.UtcNow;
        if ((now - _lastCanvasMetricsLogAt).TotalSeconds < 5)
            return;

        _lastCanvasMetricsLogAt = now;
        AppLog.Info(
            $"Viewport canvas scale actual={ActualWidth:0.#}x{ActualHeight:0.#}; " +
            $"canvas={_canvasWidth:0.#}x{_canvasHeight:0.#}; " +
            $"scale={scaleX:0.###}x{scaleY:0.###}; " +
            $"ignorePixelScaling={IgnorePixelScaling}");
    }
}
