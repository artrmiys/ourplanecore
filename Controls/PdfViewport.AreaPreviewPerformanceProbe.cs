using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using OurPlanCore;
using SkiaSharp;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    private static readonly float[] AreaPreviewProbeZooms = [4.0f, 5.334f];
    private const int AreaPreviewProbeMoveCount = 24;
    private const double AreaPreviewProbeMinimumHitRate = 0.90;
    private const int AreaPreviewProbeMaximumMissOrBypassCount = 1;
    private const long AreaPreviewProbeMaximumP95ElapsedMs = 33;
    private const long AreaPreviewProbeMaximumP95PageMs = 24;

    internal async Task<AreaPreviewPerformanceProbeResult> RunAreaPreviewPerformanceProbeAsync()
    {
        if (!ViewportPerformanceRecorder.IsActive)
            throw new InvalidOperationException("Area preview performance probe requires an active viewport recorder.");
        if (_pageBitmap == null || _pdfW <= 0 || _pdfH <= 0 || !HasViewportCanvasSize)
            throw new InvalidOperationException("Area preview performance probe requires a ready visible page.");

        ViewState previousView = CaptureViewState();
        ViewerTool previousTool = _tool;
        List<SKPoint> previousDrawPoints = [.. _drawPts];
        SKPoint? previousRubberEnd = _rubberEnd;
        SKPoint? previousSnapPreview = _snapPreview;
        string previousSnapPreviewKind = _snapPreviewKind;
        SKPoint? previousLastPointer = _lastPointerPdf;
        bool previousCursorGuideVisible = _cursorGuideVisible;
        DateTime previousPointerRepaintAt = _lastPointerRepaintAt;
        var results = new List<AreaPreviewZoomProbeResult>(AreaPreviewProbeZooms.Length);

        try
        {
            CancelScheduledPointerMoveRepaint();
            _drawPts.Clear();
            _rubberEnd = null;
            _snapPreview = null;
            _snapPreviewKind = "";
            _cursorGuideVisible = false;

            foreach (float requestedZoom in AreaPreviewProbeZooms)
            {
                SetAreaPreviewProbeView(new ViewState(requestedZoom, previousView.PanX, previousView.PanY));
                await WaitForSettledAreaPreviewProbeViewAsync();
                await WaitForAreaPreviewStaticRasterReadyAsync();
                await WarmAreaPreviewProbePageFrameAsync();

                SKRect visible = GetVisiblePdfRect();
                if (visible.IsEmpty)
                    throw new InvalidOperationException($"Area preview probe has no visible PDF bounds at zoom {requestedZoom:0.###}.");

                _tool = ViewerTool.Area;
                _drawPts.Clear();
                _drawPts.Add(ProbePoint(visible, 0.20f, 0.25f));
                _drawPts.Add(ProbePoint(visible, 0.65f, 0.28f));
                _lastPointerRepaintAt = DateTime.MinValue;

                int paintFrameCursor = ViewportPerformanceRecorder.CapturePaintFrameCursor();
                Stopwatch watch = Stopwatch.StartNew();
                for (int i = 0; i < AreaPreviewProbeMoveCount; i++)
                {
                    float phase = i / (float)Math.Max(1, AreaPreviewProbeMoveCount - 1);
                    float xRatio = 0.30f + 0.48f * phase;
                    float yRatio = 0.68f + 0.08f * MathF.Sin(phase * MathF.PI * 2f);
                    SKPoint pointer = ProbePoint(visible, xRatio, yRatio);
                    _rubberEnd = pointer;
                    _lastPointerPdf = pointer;
                    RequestPointerMoveRepaint();
                    await YieldAreaPreviewProbeFrameAsync();
                }

                watch.Stop();
                IReadOnlyList<ViewportPaintFrameSample> previewFrames =
                    ViewportPerformanceRecorder.SnapshotPaintFramesSince(paintFrameCursor);
                results.Add(BuildAreaPreviewZoomProbeResult(previewFrames, watch.ElapsedMilliseconds));

                _drawPts.Clear();
                _rubberEnd = null;
                RequestPointerMoveRepaint();
                await YieldAreaPreviewProbeFrameAsync();
            }
        }
        finally
        {
            CancelScheduledPointerMoveRepaint();
            _tool = previousTool;
            _drawPts.Clear();
            _drawPts.AddRange(previousDrawPoints);
            _rubberEnd = previousRubberEnd;
            _snapPreview = previousSnapPreview;
            _snapPreviewKind = previousSnapPreviewKind;
            _lastPointerPdf = previousLastPointer;
            _cursorGuideVisible = previousCursorGuideVisible;
            _lastPointerRepaintAt = previousPointerRepaintAt;
            SetAreaPreviewProbeView(previousView);
            await YieldAreaPreviewProbeFrameAsync();
        }

        return new AreaPreviewPerformanceProbeResult(results);
    }

    private AreaPreviewZoomProbeResult BuildAreaPreviewZoomProbeResult(
        IReadOnlyList<ViewportPaintFrameSample> previewFrames,
        long elapsedMs)
    {
        int hitCount = previewFrames.Count(frame =>
            string.Equals(frame.PageFrameState, "hit", StringComparison.OrdinalIgnoreCase));
        int missOrBypassCount = previewFrames.Count(frame =>
            string.Equals(frame.PageFrameState, "miss", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(frame.PageFrameState, "bypass", StringComparison.OrdinalIgnoreCase));
        double hitRate = previewFrames.Count == 0
            ? 0
            : Math.Round((double)hitCount / previewFrames.Count, 4);
        long p95ElapsedMs = Percentile95(previewFrames.Select(frame => frame.ElapsedMs));
        long p95PageMs = Percentile95(previewFrames.Select(frame => frame.PageBitmapMs));
        long p95InProgressMs = Percentile95(previewFrames.Select(frame => frame.InProgressMs));
        bool staticRasterActive = IsStaticRasterDisplayActive();
        int rasterDpi = RasterSheetCacheService.RenderScaleToDpi(_bitmapScale);
        int targetDpi = ViewportRenderPolicy.StaticRasterTargetDpi;
        int blackVectorSegmentCount = _rasterSheetVisualSegments.Count;
        var failures = new List<string>();

        if (!staticRasterActive)
            failures.Add("Static raster display is not active.");
        if (targetDpi != 150 || rasterDpi != 150)
            failures.Add($"Static raster DPI must be exact 150 (target={targetDpi}, current={rasterDpi}).");
        if (!ShowBlackVectorOverlay)
            failures.Add("Black vector overlay is disabled.");
        if (blackVectorSegmentCount <= 0)
            failures.Add("Black vector overlay has no loaded vector segments.");
        if (previewFrames.Count == 0)
            failures.Add("No Area preview paint frames were sampled.");
        if (hitRate < AreaPreviewProbeMinimumHitRate)
            failures.Add($"Retained page-frame hit rate {hitRate:P1} is below 90%.");
        if (missOrBypassCount > AreaPreviewProbeMaximumMissOrBypassCount)
            failures.Add($"Area preview had {missOrBypassCount} page-frame miss/bypass paints (maximum 1).");
        if (p95ElapsedMs > AreaPreviewProbeMaximumP95ElapsedMs)
            failures.Add($"Area preview p95 frame time is {p95ElapsedMs} ms (maximum 33 ms).");
        if (p95PageMs > AreaPreviewProbeMaximumP95PageMs)
            failures.Add($"Area preview p95 page time is {p95PageMs} ms (maximum 24 ms).");

        return new AreaPreviewZoomProbeResult(
            Math.Round(_zoom, 4),
            AreaPreviewProbeMoveCount,
            elapsedMs,
            previewFrames.Count,
            hitCount,
            missOrBypassCount,
            hitRate,
            p95ElapsedMs,
            p95PageMs,
            p95InProgressMs,
            staticRasterActive,
            rasterDpi,
            targetDpi,
            ShowBlackVectorOverlay,
            blackVectorSegmentCount,
            failures);
    }

    private static long Percentile95(IEnumerable<long> values)
    {
        long[] ordered = values.OrderBy(value => value).ToArray();
        if (ordered.Length == 0)
            return 0;

        int index = Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1);
        return ordered[index];
    }

    private async Task WaitForSettledAreaPreviewProbeViewAsync()
    {
        await Task.Delay(ViewportConstants.NavigationIdleMs + 40);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private void SetAreaPreviewProbeView(ViewState state)
    {
        // Diagnostic-only transform: avoid ZoomChanged/sheet-overlay callbacks
        // so an env-gated probe cannot persist anything into the opened job.
        _zoom = Math.Clamp(state.Zoom, ZoomMin, ZoomMax);
        _panX = state.PanX;
        _panY = state.PanY;
        ClampPanToPage();
        RequestRepaint();
    }

    private async Task WaitForAreaPreviewStaticRasterReadyAsync()
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 15000)
        {
            int currentDpi = RasterSheetCacheService.RenderScaleToDpi(_bitmapScale);
            if (IsStaticRasterDisplayActive() &&
                ViewportRenderPolicy.StaticRasterTargetDpi == 150 &&
                currentDpi == 150 &&
                ShowBlackVectorOverlay &&
                _rasterSheetVisualSegments.Count > 0)
            {
                return;
            }

            await Task.Delay(50);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        }
    }

    private async Task WarmAreaPreviewProbePageFrameAsync()
    {
        // First paint builds the retained frame; the second proves the hot path
        // before any rubber-band updates are sampled.
        RequestRepaint();
        await YieldAreaPreviewProbeFrameAsync();
        RequestRepaint();
        await YieldAreaPreviewProbeFrameAsync();
    }

    private async Task YieldAreaPreviewProbeFrameAsync()
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Task.Delay(ViewportRenderPolicy.PointerMoveRepaintMinIntervalMs + 4);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private static SKPoint ProbePoint(SKRect visible, float xRatio, float yRatio) =>
        new(
            visible.Left + visible.Width * xRatio,
            visible.Top + visible.Height * yRatio);
}

public sealed record AreaPreviewPerformanceProbeResult(
    IReadOnlyList<AreaPreviewZoomProbeResult> Zooms)
{
    public bool Passed => Zooms.Count == 2 && Zooms.All(result => result.Passed);
    public double MinimumHitRate => 0.90;
    public int MaximumMissOrBypassCount => 1;
    public long MaximumP95ElapsedMs => 33;
    public long MaximumP95PageMs => 24;
}

public sealed record AreaPreviewZoomProbeResult(
    double Zoom,
    int PreviewUpdates,
    long ElapsedMs,
    int SampledFrameCount,
    int PageFrameHitCount,
    int PageFrameMissOrBypassCount,
    double PageFrameHitRate,
    long P95ElapsedMs,
    long P95PageBitmapMs,
    long P95InProgressMs,
    bool StaticRasterActive,
    int RasterDpi,
    int TargetRasterDpi,
    bool BlackVectorOverlayEnabled,
    int BlackVectorSegmentCount,
    IReadOnlyList<string> Failures)
{
    public bool Passed => Failures.Count == 0;
}
