using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SkiaSharp;

namespace OurPlanCore;

public static class ViewportPerformanceRecorder
{
    private const int MaxRenderSamples = 600;
    private const int MaxSlowFrameSamples = 300;
    private const int MaxPaintFrameSamples = 1200;
    private const int MaxQueueSamples = 400;
    private const int MaxDecodeSamples = 300;
    private static readonly object Gate = new();
    private static ViewportPerformanceRun? _activeRun;
    private static volatile bool _isActive;

    public static bool IsActive => _isActive;

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
            _isActive = true;
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
            _isActive = false;
            _activeRun = null;
            run.FinishedUtc = DateTime.UtcNow;
            run.Summary = BuildSummary(run);
            return run;
        }
    }

    public static void RecordPaintFrame(
        string pageFolder,
        float zoom,
        string pageFrameState,
        long elapsedMs,
        long pageBitmapMs,
        long inProgressMs)
    {
        // This runs for every SKElement paint. Keep the normal production path
        // to one volatile read and avoid taking the recorder lock when inactive.
        if (!_isActive)
            return;

        lock (Gate)
        {
            if (_activeRun == null)
                return;

            var sample = new ViewportPaintFrameSample
            {
                PageFolder = pageFolder,
                Zoom = Math.Round(zoom, 4),
                PageFrameState = pageFrameState,
                ElapsedMs = elapsedMs,
                PageBitmapMs = pageBitmapMs,
                InProgressMs = inProgressMs,
                Utc = DateTime.UtcNow,
            };

            _activeRun.TotalPaintFrameCount++;
            if (_activeRun.PaintFrames.Count < MaxPaintFrameSamples)
                _activeRun.PaintFrames.Add(sample);
        }
    }

    public static int CapturePaintFrameCursor()
    {
        if (!_isActive)
            return 0;

        lock (Gate)
            return _activeRun?.PaintFrames.Count ?? 0;
    }

    public static IReadOnlyList<ViewportPaintFrameSample> SnapshotPaintFramesSince(int cursor)
    {
        if (!_isActive)
            return [];

        lock (Gate)
        {
            if (_activeRun == null || cursor < 0 || cursor >= _activeRun.PaintFrames.Count)
                return [];

            return _activeRun.PaintFrames.Skip(cursor).ToArray();
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
        long sheetOverlayPaintMs,
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
                SheetOverlayPaintMs = sheetOverlayPaintMs,
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

    public static void RecordRepaintRequest(string pageFolder, bool alreadyQueued, bool crossThreadRequest)
    {
        lock (Gate)
        {
            if (_activeRun == null)
                return;

            _activeRun.RepaintRequestCount++;
            if (alreadyQueued)
                _activeRun.RepaintCoalescedCount++;
            if (crossThreadRequest)
                _activeRun.CrossThreadRepaintRequestCount++;
        }
    }

    public static void RecordRenderQueue(
        string kind,
        string pageFolder,
        float renderScale,
        bool replacedPending,
        bool renderInProgress)
    {
        lock (Gate)
        {
            if (_activeRun == null)
                return;

            var sample = new ViewportRenderQueueSample
            {
                Kind = kind,
                PageFolder = pageFolder,
                RenderScale = Math.Round(renderScale, 4),
                ReplacedPending = replacedPending,
                RenderInProgress = renderInProgress,
                Utc = DateTime.UtcNow,
            };

            _activeRun.TotalRenderQueueCount++;
            if (replacedPending)
                _activeRun.RenderQueueReplacementCount++;
            if (renderInProgress)
                _activeRun.RenderQueueWhileBusyCount++;
            if (_activeRun.RenderQueues.Count < MaxQueueSamples)
                _activeRun.RenderQueues.Add(sample);
        }
    }

    public static void RecordBitmapDecode(
        string kind,
        string pageFolder,
        string pdfName,
        int pdfPage,
        long elapsedMs,
        bool ok)
    {
        lock (Gate)
        {
            if (_activeRun == null)
                return;

            var sample = new ViewportBitmapDecodeSample
            {
                Kind = kind,
                PageFolder = pageFolder,
                PdfName = pdfName,
                PdfPage = pdfPage,
                ElapsedMs = elapsedMs,
                Ok = ok,
                Utc = DateTime.UtcNow,
            };

            _activeRun.TotalBitmapDecodeCount++;
            if (ok)
                _activeRun.BitmapDecodeSuccessCount++;
            _activeRun.TotalBitmapDecodeMs += elapsedMs;
            if (_activeRun.BitmapDecodes.Count < MaxDecodeSamples)
                _activeRun.BitmapDecodes.Add(sample);
        }
    }

    private static ViewportPerformanceSummary BuildSummary(ViewportPerformanceRun run)
    {
        using Process process = Process.GetCurrentProcess();
        List<ViewportRenderProfileSample> renders = run.RenderProfiles;
        List<ViewportSlowFrameSample> slowFrames = run.SlowFrames;
        List<ViewportPaintFrameSample> paintFrames = run.PaintFrames;
        List<ViewportRenderQueueSample> queues = run.RenderQueues;
        List<ViewportBitmapDecodeSample> decodes = run.BitmapDecodes;

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
            PaintFrameCount = run.TotalPaintFrameCount,
            StoredPaintFrameCount = paintFrames.Count,
            MaxPaintFrameMs = paintFrames.Count == 0 ? 0 : paintFrames.Max(sample => sample.ElapsedMs),
            AveragePaintFrameMs = paintFrames.Count == 0
                ? 0
                : Math.Round(paintFrames.Average(sample => sample.ElapsedMs), 2),
            MaxInProgressPaintMs = paintFrames.Count == 0 ? 0 : paintFrames.Max(sample => sample.InProgressMs),
            MaxRenderMs = renders.Count == 0 ? 0 : renders.Max(sample => sample.ElapsedMs),
            MaxSlowFrameMs = slowFrames.Count == 0 ? 0 : slowFrames.Max(sample => sample.ElapsedMs),
            MaxPageBitmapPaintMs = slowFrames.Count == 0 ? 0 : slowFrames.Max(sample => sample.PageBitmapMs),
            MaxSheetOverlayPaintMs = slowFrames.Count == 0 ? 0 : slowFrames.Max(sample => sample.SheetOverlayPaintMs),
            RepaintRequestCount = run.RepaintRequestCount,
            RepaintCoalescedCount = run.RepaintCoalescedCount,
            RepaintCoalesceRate = run.RepaintRequestCount == 0
                ? 0
                : Math.Round((double)run.RepaintCoalescedCount / run.RepaintRequestCount, 4),
            CrossThreadRepaintRequestCount = run.CrossThreadRepaintRequestCount,
            RenderQueueCount = run.TotalRenderQueueCount,
            StoredRenderQueueCount = queues.Count,
            RenderQueueReplacementCount = run.RenderQueueReplacementCount,
            RenderQueueReplacementRate = run.TotalRenderQueueCount == 0
                ? 0
                : Math.Round((double)run.RenderQueueReplacementCount / run.TotalRenderQueueCount, 4),
            RenderQueueWhileBusyCount = run.RenderQueueWhileBusyCount,
            BitmapDecodeCount = run.TotalBitmapDecodeCount,
            StoredBitmapDecodeCount = decodes.Count,
            BitmapDecodeSuccessCount = run.BitmapDecodeSuccessCount,
            MaxBitmapDecodeMs = decodes.Count == 0 ? 0 : decodes.Max(sample => sample.ElapsedMs),
            AverageBitmapDecodeMs = run.TotalBitmapDecodeCount == 0
                ? 0
                : Math.Round((double)run.TotalBitmapDecodeMs / run.TotalBitmapDecodeCount, 2),
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
            RenderQueueCountByKind = queues
                .GroupBy(sample => sample.Kind, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            BitmapDecodeCountByKind = decodes
                .GroupBy(sample => sample.Kind, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            AverageBitmapDecodeMsByKind = decodes
                .GroupBy(sample => sample.Kind, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => Math.Round(group.Average(sample => sample.ElapsedMs), 2),
                    StringComparer.OrdinalIgnoreCase),
            PaintFrameCountByState = paintFrames
                .GroupBy(sample => sample.PageFrameState, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
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
    public int TotalPaintFrameCount { get; set; }
    public int RepaintRequestCount { get; set; }
    public int RepaintCoalescedCount { get; set; }
    public int CrossThreadRepaintRequestCount { get; set; }
    public int TotalRenderQueueCount { get; set; }
    public int RenderQueueReplacementCount { get; set; }
    public int RenderQueueWhileBusyCount { get; set; }
    public int TotalBitmapDecodeCount { get; set; }
    public int BitmapDecodeSuccessCount { get; set; }
    public long TotalBitmapDecodeMs { get; set; }
    public List<ViewportRenderProfileSample> RenderProfiles { get; set; } = [];
    public List<ViewportSlowFrameSample> SlowFrames { get; set; } = [];
    public List<ViewportPaintFrameSample> PaintFrames { get; set; } = [];
    public List<ViewportRenderQueueSample> RenderQueues { get; set; } = [];
    public List<ViewportBitmapDecodeSample> BitmapDecodes { get; set; } = [];
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
    public int PaintFrameCount { get; set; }
    public int StoredPaintFrameCount { get; set; }
    public long MaxPaintFrameMs { get; set; }
    public double AveragePaintFrameMs { get; set; }
    public long MaxInProgressPaintMs { get; set; }
    public long MaxRenderMs { get; set; }
    public long MaxSlowFrameMs { get; set; }
    public long MaxPageBitmapPaintMs { get; set; }
    public long MaxSheetOverlayPaintMs { get; set; }
    public int RepaintRequestCount { get; set; }
    public int RepaintCoalescedCount { get; set; }
    public double RepaintCoalesceRate { get; set; }
    public int CrossThreadRepaintRequestCount { get; set; }
    public int RenderQueueCount { get; set; }
    public int StoredRenderQueueCount { get; set; }
    public int RenderQueueReplacementCount { get; set; }
    public double RenderQueueReplacementRate { get; set; }
    public int RenderQueueWhileBusyCount { get; set; }
    public int BitmapDecodeCount { get; set; }
    public int StoredBitmapDecodeCount { get; set; }
    public int BitmapDecodeSuccessCount { get; set; }
    public long MaxBitmapDecodeMs { get; set; }
    public double AverageBitmapDecodeMs { get; set; }
    public Dictionary<string, int> RenderCountByKind { get; set; } = [];
    public Dictionary<string, double> AverageRenderMsByKind { get; set; } = [];
    public Dictionary<string, int> RenderQueueCountByKind { get; set; } = [];
    public Dictionary<string, int> BitmapDecodeCountByKind { get; set; } = [];
    public Dictionary<string, double> AverageBitmapDecodeMsByKind { get; set; } = [];
    public Dictionary<string, int> PaintFrameCountByState { get; set; } = [];
    public long WorkingSetMb { get; set; }
    public long ManagedMemoryMb { get; set; }
}

public sealed class ViewportPaintFrameSample
{
    public string PageFolder { get; set; } = "";
    public double Zoom { get; set; }
    public string PageFrameState { get; set; } = "";
    public long ElapsedMs { get; set; }
    public long PageBitmapMs { get; set; }
    public long InProgressMs { get; set; }
    public DateTime Utc { get; set; }
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
    public long SheetOverlayPaintMs { get; set; }
    public long MeasurementMs { get; set; }
    public long MarkupMs { get; set; }
    public long InProgressMs { get; set; }
    public long LabelMs { get; set; }
    public long ScreenOverlayMs { get; set; }
    public DateTime Utc { get; set; }
}

public sealed class ViewportRenderQueueSample
{
    public string Kind { get; set; } = "";
    public string PageFolder { get; set; } = "";
    public double RenderScale { get; set; }
    public bool ReplacedPending { get; set; }
    public bool RenderInProgress { get; set; }
    public DateTime Utc { get; set; }
}

public sealed class ViewportBitmapDecodeSample
{
    public string Kind { get; set; } = "";
    public string PageFolder { get; set; } = "";
    public string PdfName { get; set; } = "";
    public int PdfPage { get; set; }
    public long ElapsedMs { get; set; }
    public bool Ok { get; set; }
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
