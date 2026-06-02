using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

public static partial class PdfLayerRenderService
{
    private static readonly SemaphoreSlim WorkerSemaphore = new(1, 1);
    private static Process? WorkerProcess;
    private static StreamWriter? WorkerInput;
    private static StreamReader? WorkerOutput;
    private static readonly SemaphoreSlim DetailWorkerSemaphore = new(1, 1);
    private static Process? DetailWorkerProcess;
    private static StreamWriter? DetailWorkerInput;
    private static StreamReader? DetailWorkerOutput;
    private static readonly SemaphoreSlim PrefetchWorkerSemaphore = new(1, 1);
    private static Process? PrefetchWorkerProcess;
    private static StreamWriter? PrefetchWorkerInput;
    private static StreamReader? PrefetchWorkerOutput;
    private static readonly object RenderCacheLock = new();
    private static readonly Dictionary<string, PdfLayerRenderResult> RenderCache = [];
    private static readonly Queue<string> RenderCacheOrder = [];
    private static long RenderCacheBytes;
    private const int MaxRenderCacheEntries = 96;
    private const long MaxRenderCacheBytes = 768_000_000;
    private const long MaxRenderCacheEntryBytes = 96_000_000;
    private const int InlineRenderImageMaxPixels = 24_000_000;
    private static readonly object InFlightRenderLock = new();
    private static readonly Dictionary<string, Task<(bool Ok, PdfLayerRenderResult Result, string Error)>> InFlightRenderTasks = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };
    private static readonly JsonSerializerOptions WorkerJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };

    public static Task<(bool Ok, PdfLayerRenderResult Result, string Error)> TryRenderAsync(
        string pdfPath,
        int pageIndex,
        double renderScale,
        IReadOnlyDictionary<int, bool> layerStates,
        IReadOnlyCollection<int> highlightedLayers,
        IReadOnlyList<PdfLayerInfo>? cachedLayers,
        SKRect? clipRect = null)
    {
        string requestKey = BuildRenderCacheKey(
            pdfPath,
            pageIndex,
            renderScale,
            layerStates,
            highlightedLayers,
            cachedLayers,
            clipRect);
        if (TryGetCachedRender(requestKey, out PdfLayerRenderResult cached))
            return Task.FromResult((true, cached, ""));

        lock (InFlightRenderLock)
        {
            if (InFlightRenderTasks.TryGetValue(requestKey, out var inFlight))
                return inFlight;

            var task = Task.Run(() =>
            {
                bool ok = TryRender(
                    pdfPath,
                    pageIndex,
                    renderScale,
                    layerStates,
                    highlightedLayers,
                    cachedLayers,
                    clipRect,
                    out PdfLayerRenderResult result,
                    out string error);
                return (ok, result, error);
            });
            InFlightRenderTasks[requestKey] = task;
            _ = task.ContinueWith(
                _ =>
                {
                    lock (InFlightRenderLock)
                        InFlightRenderTasks.Remove(requestKey);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return task;
        }
    }

    public static Task<(bool Ok, IReadOnlyList<PdfLayerInfo> Layers, string Error)> TryReadVisibleLayersAsync(
        string pdfPath,
        int pageIndex) =>
        Task.Run(() =>
        {
            bool ok = TryReadVisibleLayers(pdfPath, pageIndex, out IReadOnlyList<PdfLayerInfo> layers, out string error);
            return (ok, layers, error);
        });

    public static Task<(bool Ok, PdfLayerTraceResult Result, string Error)> TryTraceLayerAsync(
        string pdfPath,
        int pageIndex,
        int layerNumber,
        string layerName,
        PdfLayerTraceMode mode,
        SKPoint? pickPoint,
        IReadOnlyList<PdfLayerInfo>? cachedLayers) =>
        Task.Run(() =>
        {
            bool ok = TryTraceLayer(
                pdfPath,
                pageIndex,
                layerNumber,
                layerName,
                mode,
                pickPoint,
                cachedLayers,
                out PdfLayerTraceResult result,
                out string error);
            return (ok, result, error);
        });

    public static Task<(bool Ok, PdfLayerProbeResult Result, string Error)> TryProbeLayersAsync(
        string pdfPath,
        int pageIndex,
        SKPoint point,
        IReadOnlyList<PdfLayerInfo>? cachedLayers) =>
        Task.Run(() =>
        {
            bool ok = TryProbeLayers(pdfPath, pageIndex, point, cachedLayers, out PdfLayerProbeResult result, out string error);
            return (ok, result, error);
        });
}
