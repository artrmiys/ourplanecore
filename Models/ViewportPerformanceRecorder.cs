using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SkiaSharp;

namespace OurPlaneCore;

public static class ViewportPerformanceRecorder
{
    private const int MaxRenderSamples = 600;
    private const int MaxSlowFrameSamples = 300;
    private static readonly object Gate = new();
    private static ViewportPerformanceRun? _activeRun;

    public static bool IsActive
    {
        get
        {
            lock (Gate)
                return _activeRun != null;
        }
    }

    public static void BeginRun(string jobPath, string scenario)
    {
        lock (Gate)
        {
            _activeRun = new ViewportPerformanceRun
            {
                JobPath = jobPath,
                Scenario = scenario,
                StartedUtc = DateTime.UtcNow,
            };
        }
    }

    public static ViewportPerformanceRun EndRun()
    {
        lock (Gate)
        {
            ViewportPerformanceRun run = _activeRun ?? new ViewportPerformanceRun
            {
                StartedUtc = DateTime.UtcNow,
            };
            _activeRun = null;
            run.FinishedUtc = DateTime.UtcNow;
            run.Summary = BuildSummary(run);
            return run;
        }
    }

    public static void RecordRenderProfile(
        string kind,
        string pageFolder,
        string pdfName,
        int pdfPage,
        float zoom,
        float bitmapScale,
        float renderedScale,
        float targetScale,
        long elapsedMs,
        bool fromCache,
        SKRect? clipRect)
    {
        lock (Gate)
        {
            if (_activeRun == null)
                return;

            var sample = new ViewportRenderProfileSample
            {
                Kind = kind,
                PageFolder = pageFolder,
                PdfName = pdfName,
                PdfPage = pdfPage,
                Zoom = Math.Round(zoom, 4),
                BitmapScale = Math.Round(bitmapScale, 4),
                RenderedScale = Math.Round(renderedScale, 4),
                TargetScale = Math.Round(targetScale, 4),
                ElapsedMs = elapsedMs,
                FromCache = fromCache,
                Clip = clipRect.HasValue ? ViewportRenderClip.FromRect(clipRect.Value) : null,
                Utc = DateTime.UtcNow,
            };

            _activeRun.TotalRenderProfileCount++;
            if (fromCache)
                _activeRun.CacheHitCount++;
            if (_activeRun.RenderProfiles.Count < MaxRenderSamples)
                _activeRun.RenderProfiles.Add(sample);
        }
    }

    public static void RecordSlowFrame(
        string pageFolder,
        float zoom,
        bool fastFrame,
        int activeMeasurementCount,
        int visibleMeasurementCount,
        float renderedScale,
        bool hasOverlay,
        long elapsedMs,
        long pageBitmapMs,
        long overlayMs,
        long measurementMs,
        long markupMs,
        long inProgressMs,
        long labelMs,
        long screenOverlayMs)
    {
        lock (Gate)
        {
            if (_activeRun == null)
                return;

            var sample = new ViewportSlowFrameSample
            {
                PageFolder = pageFolder,
                Zoom = Math.Round(zoom, 4),
                FastFrame = fastFrame,
                ActiveMeasurementCount = activeMeasurementCount,
                VisibleMeasurementCount = visibleMeasurementCount,
                RenderedScale = Math.Round(renderedScale, 4),
                HasOverlay = hasOverlay,
                ElapsedMs = elapsedMs,
                PageBitmapMs = pageBitmapMs,
                OverlayMs = overlayMs,
                MeasurementMs = measurementMs,
                MarkupMs = markupMs,
                InProgressMs = inProgressMs,
                LabelMs = labelMs,
                ScreenOverlayMs = screenOverlayMs,
                Utc = DateTime.UtcNow,
            };

            _activeRun.TotalSlowFrameCount++;
            if (_activeRun.SlowFrames.Count < MaxSlowFrameSamples)
                _activeRun.SlowFrames.Add(sample);
        }
    }

    private static ViewportPerformanceSummary BuildSummary(ViewportPerformanceRun run)
    {
        using Process process = Process.GetCurrentProcess();
        List<ViewportRenderProfileSample> renders = run.RenderProfiles;
        List<ViewportSlowFrameSample> slowFrames = run.SlowFrames;

        return new ViewportPerformanceSummary
        {
            DurationMs = Math.Max(0, (long)(run.FinishedUtc - run.StartedUtc).TotalMilliseconds),
            RenderProfileCount = run.TotalRenderProfileCount,
            StoredRenderProfileCount = renders.Count,
            CacheHitCount = run.CacheHitCount,
            CacheHitRate = run.TotalRenderProfileCount == 0
                ? 0
                : Math.Round((double)run.CacheHitCount / run.TotalRenderProfileCount, 4),
            SlowFrameCount = run.TotalSlowFrameCount,
            StoredSlowFrameCount = slowFrames.Count,
            MaxRenderMs = renders.Count == 0 ? 0 : renders.Max(sample => sample.ElapsedMs),
            MaxSlowFrameMs = slowFrames.Count == 0 ? 0 : slowFrames.Max(sample => sample.ElapsedMs),
            MaxPageBitmapPaintMs = slowFrames.Count == 0 ? 0 : slowFrames.Max(sample => sample.PageBitmapMs),
            RenderCountByKind = renders
                .GroupBy(sample => sample.Kind, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            AverageRenderMsByKind = renders
                .GroupBy(sample => sample.Kind, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => Math.Round(group.Average(sample => sample.ElapsedMs), 2),
                    StringComparer.OrdinalIgnoreCase),
            WorkingSetMb = process.WorkingSet64 / (1024 * 1024),
            ManagedMemoryMb = GC.GetTotalMemory(forceFullCollection: false) / (1024 * 1024),
        };
    }
}

public sealed class ViewportPerformanceRun
{
    public string JobPath { get; set; } = "";
    public string Scenario { get; set; } = "";
    public DateTime StartedUtc { get; set; }
    public DateTime FinishedUtc { get; set; }
    public int TotalRenderProfileCount { get; set; }
    public int CacheHitCount { get; set; }
    public int TotalSlowFrameCount { get; set; }
    public List<ViewportRenderProfileSample> RenderProfiles { get; set; } = [];
    public List<ViewportSlowFrameSample> SlowFrames { get; set; } = [];
    public ViewportPerformanceSummary Summary { get; set; } = new();
}

public sealed class ViewportPerformanceSummary
{
    public long DurationMs { get; set; }
    public int RenderProfileCount { get; set; }
    public int StoredRenderProfileCount { get; set; }
    public int CacheHitCount { get; set; }
    public double CacheHitRate { get; set; }
    public int SlowFrameCount { get; set; }
    public int StoredSlowFrameCount { get; set; }
    public long MaxRenderMs { get; set; }
    public long MaxSlowFrameMs { get; set; }
    public long MaxPageBitmapPaintMs { get; set; }
    public Dictionary<string, int> RenderCountByKind { get; set; } = [];
    public Dictionary<string, double> AverageRenderMsByKind { get; set; } = [];
    public long WorkingSetMb { get; set; }
    public long ManagedMemoryMb { get; set; }
}

public sealed class ViewportRenderProfileSample
{
    public string Kind { get; set; } = "";
    public string PageFolder { get; set; } = "";
    public string PdfName { get; set; } = "";
    public int PdfPage { get; set; }
    public double Zoom { get; set; }
    public double BitmapScale { get; set; }
    public double RenderedScale { get; set; }
    public double TargetScale { get; set; }
    public long ElapsedMs { get; set; }
    public bool FromCache { get; set; }
    public ViewportRenderClip? Clip { get; set; }
    public DateTime Utc { get; set; }
}

public sealed class ViewportSlowFrameSample
{
    public string PageFolder { get; set; } = "";
    public double Zoom { get; set; }
    public bool FastFrame { get; set; }
    public int ActiveMeasurementCount { get; set; }
    public int VisibleMeasurementCount { get; set; }
    public double RenderedScale { get; set; }
    public bool HasOverlay { get; set; }
    public long ElapsedMs { get; set; }
    public long PageBitmapMs { get; set; }
    public long OverlayMs { get; set; }
    public long MeasurementMs { get; set; }
    public long MarkupMs { get; set; }
    public long InProgressMs { get; set; }
    public long LabelMs { get; set; }
    public long ScreenOverlayMs { get; set; }
    public DateTime Utc { get; set; }
}

public sealed class ViewportRenderClip
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Right { get; set; }
    public double Bottom { get; set; }

    public static ViewportRenderClip FromRect(SKRect rect) =>
        new()
        {
            Left = Math.Round(rect.Left, 2),
            Top = Math.Round(rect.Top, 2),
            Right = Math.Round(rect.Right, 2),
            Bottom = Math.Round(rect.Bottom, 2),
        };
}
