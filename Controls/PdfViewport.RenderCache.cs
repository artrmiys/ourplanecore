using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    // Entry counts raised in step with the byte budgets above so the byte budget
    // (which is RAM-adaptive) is the real limit; small machines stay byte-bound.
    private static readonly ViewportBitmapCache DocnetRenderCache = new(maxEntries: 128, maxBytes: ResolveDocnetRenderCacheBudgetBytes());
    private static readonly ViewportBitmapCache PersistedPreviewBitmapCache = new(maxEntries: 768, maxBytes: ResolvePersistedPreviewBitmapCacheBudgetBytes());
    private static readonly ViewportBitmapCache RasterSheetBitmapCache = new(maxEntries: 128, maxBytes: ResolveRasterSheetBitmapCacheBudgetBytes());
    private static readonly LayerRenderBitmapCache LayerBitmapCache = new(maxEntries: 96, maxBytes: ResolveLayerBitmapCacheBudgetBytes());
    private static readonly object DocnetPreviewPrefetchGate = new();
    private static readonly HashSet<string> DocnetPreviewPrefetchInFlight = [];
    // Preview prefetch renders on the dedicated prefetch worker pool, so its
    // concurrency mirrors the pool size (clamp(cores/3, 1, 4)); a single-permit
    // gate here left the pool idle and made whole-job warmup serial.
    private static readonly SemaphoreSlim PreviewPrefetchSemaphore = new(
        Math.Clamp(Environment.ProcessorCount / 3, 1, 4),
        Math.Clamp(Environment.ProcessorCount / 3, 1, 4));
    private static readonly SemaphoreSlim LivePreviewRenderSemaphore = new(1, 1);
    private static long PreviewPrefetchPausedUntilUtcTicks;
    private static readonly object CleanRenderPrefetchGate = new();
    private static readonly HashSet<string> CleanRenderPrefetchInFlight = [];
    private static readonly SemaphoreSlim CleanRenderPrefetchSemaphore = new(
        Math.Clamp(Environment.ProcessorCount / 3, 1, 4),
        Math.Clamp(Environment.ProcessorCount / 3, 1, 4));
    private static readonly object RasterSheetRefreshPrefetchGate = new();
    private static readonly HashSet<string> RasterSheetRefreshPrefetchInFlight = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim RasterSheetRefreshPrefetchCadenceSemaphore = new(1, 1);
    private static readonly SemaphoreSlim RasterSheetRefreshPrefetchSemaphore = new(1, 1);
    private static readonly object RasterSheetBitmapPrefetchGate = new();
    private static readonly HashSet<string> RasterSheetBitmapPrefetchInFlight = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim RasterSheetBitmapPrefetchSemaphore = new(
        Math.Clamp(Environment.ProcessorCount / 4, 1, 2),
        Math.Clamp(Environment.ProcessorCount / 4, 1, 2));
    private static readonly object RasterSheetWorkZoomWarmupGate = new();
    private static readonly HashSet<string> RasterSheetWorkZoomWarmupInFlight = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim RasterSheetWorkZoomWarmupSemaphore = new(1, 1);

    // Live PDF caches stay adaptive. The full-sheet raster cache has a lower hard
    // ceiling because a single E-size bitmap can occupy about 100 MB; keeping dozens
    // decoded made large jobs fast once but allowed multi-GB idle working sets.
    private static long ResolveDocnetRenderCacheBudgetBytes() =>
        ResolveViewportRamBudget(192_000_000L, 2_560_000_000L, 0.020);

    private static long ResolvePersistedPreviewBitmapCacheBudgetBytes() =>
        ResolveViewportRamBudget(192_000_000L, 1_792_000_000L, 0.018);

    private static long ResolveRasterSheetBitmapCacheBudgetBytes() =>
        ResolveViewportRamBudget(256_000_000L, 768_000_000L, 0.012);

    private static long ResolveLayerBitmapCacheBudgetBytes() =>
        ResolveViewportRamBudget(256_000_000L, 2_560_000_000L, 0.030);

    private static long ResolveViewportRamBudget(long minimumBytes, long maximumBytes, double targetRatio)
    {
        long availableBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        if (availableBytes <= 0)
            return maximumBytes;

        double desiredBytes = availableBytes * targetRatio;
        if (double.IsNaN(desiredBytes) || desiredBytes <= 0)
            return maximumBytes;

        long budgetBytes = desiredBytes >= long.MaxValue ? maximumBytes : (long)desiredBytes;
        return Math.Clamp(budgetBytes, minimumBytes, maximumBytes);
    }

    private sealed record CachedBitmapRender(
        float WidthPt,
        float HeightPt,
        float BitmapScale,
        SKBitmap Bitmap);

    private sealed record CachedLayerBitmapRender(
        float WidthPt,
        float HeightPt,
        float BitmapScale,
        SKBitmap Bitmap,
        IReadOnlyList<PdfLayer> Layers);

    // Shared-pixel leases: TryGet used to deep-copy the cached bitmap on every
    // hit (30-140 MB memcpy + LOH churn per page apply). Consumers own and
    // Dispose what they receive, so instead of a copy they now get a zero-copy
    // SKBitmap wrapper installed over the cached pixels with a release proc
    // that decrements a lease count. The master bitmap is only disposed once
    // it has been evicted from the cache AND every outstanding lease has been
    // disposed, so consumer ownership semantics are unchanged. Nothing mutates
    // pixels obtained from these caches (verified: the only GetPixels write in
    // the viewport targets freshly decoded bitmaps), so sharing is safe.
    private static class BitmapLease
    {
        // Called under the owning cache's gate.
        public static SKBitmap Acquire(LeaseState state)
        {
            SKBitmap? master = state.Master;
            if (master == null)
                return new SKBitmap();

            var wrapper = new SKBitmap();
            if (!wrapper.InstallPixels(
                    master.Info,
                    master.GetPixels(),
                    master.RowBytes,
                    (_, _) => Release(state),
                    null))
            {
                wrapper.Dispose();
                return master.Copy();
            }

            state.Leases++;
            return wrapper;
        }

        private static void Release(LeaseState state)
        {
            SKBitmap? toDispose = null;
            lock (state.Gate)
            {
                state.Leases--;
                if (state.Evicted && state.Leases <= 0)
                {
                    toDispose = state.Master;
                    state.Master = null;
                }
            }

            toDispose?.Dispose();
        }

        // Called under the owning cache's gate when the entry leaves the cache.
        public static void Retire(LeaseState state)
        {
            state.Evicted = true;
            if (state.Leases <= 0)
            {
                state.Master?.Dispose();
                state.Master = null;
            }
        }
    }

    // Lease bookkeeping for one cached master bitmap. Gate is the owning
    // cache's lock object so release (which may run on a finalizer thread)
    // synchronizes with cache mutation.
    private sealed class LeaseState
    {
        public LeaseState(object gate, SKBitmap master)
        {
            Gate = gate;
            Master = master;
        }

        public object Gate { get; }
        public SKBitmap? Master { get; set; }
        public int Leases { get; set; }
        public bool Evicted { get; set; }
    }

    private sealed class ViewportBitmapCache
    {
        private readonly int _maxEntries;
        private readonly long _maxBytes;
        private readonly object _gate = new();
        private readonly Dictionary<string, CacheEntry> _entries = [];
        private long _clock;
        private long _totalBytes;

        public ViewportBitmapCache(int maxEntries, long maxBytes)
        {
            _maxEntries = Math.Max(1, maxEntries);
            _maxBytes = Math.Max(16_000_000, maxBytes);
        }

        public bool TryGet(string key, out CachedBitmapRender render)
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out CacheEntry? entry))
                {
                    entry.LastUsed = ++_clock;
                    render = new CachedBitmapRender(
                        entry.WidthPt,
                        entry.HeightPt,
                        entry.BitmapScale,
                        entry.AcquireLease());
                    return true;
                }
            }

            render = new CachedBitmapRender(0, 0, 1, new SKBitmap());
            return false;
        }

        public bool Contains(string key)
        {
            lock (_gate)
                return _entries.ContainsKey(key);
        }

        public void Put(string key, DocnetRenderResult render)
        {
            Put(key, render.WidthPt, render.HeightPt, render.BitmapScale, render.Bitmap);
        }

        public void Put(string key, float widthPt, float heightPt, float bitmapScale, SKBitmap bitmap)
        {
            SKBitmap copy = bitmap.Copy();
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out CacheEntry? existing))
                {
                    _totalBytes -= existing.EstimatedBytes;
                    existing.Retire();
                    _entries.Remove(key);
                }

                var entry = new CacheEntry(
                    _gate,
                    widthPt,
                    heightPt,
                    bitmapScale,
                    copy,
                    ++_clock);
                _entries[key] = entry;
                _totalBytes += entry.EstimatedBytes;
                Trim();
            }
        }

        public bool TryPut(string key, float widthPt, float heightPt, float bitmapScale, SKBitmap bitmap)
        {
            long bytes = EstimateBitmapBytes(bitmap);
            if (bytes <= 0 || bytes > _maxBytes)
                return false;

            Put(key, widthPt, heightPt, bitmapScale, bitmap);
            return true;
        }

        private void Trim()
        {
            while (_entries.Count > _maxEntries || _totalBytes > _maxBytes)
            {
                string oldestKey = "";
                long oldest = long.MaxValue;
                foreach (var pair in _entries)
                {
                    if (pair.Value.LastUsed >= oldest)
                        continue;

                    oldest = pair.Value.LastUsed;
                    oldestKey = pair.Key;
                }

                if (string.IsNullOrWhiteSpace(oldestKey))
                    return;

                _totalBytes -= _entries[oldestKey].EstimatedBytes;
                _entries[oldestKey].Retire();
                _entries.Remove(oldestKey);
            }
        }

        private static long EstimateBitmapBytes(SKBitmap bitmap) =>
            (long)Math.Max(0, bitmap.Width) * Math.Max(0, bitmap.Height) * 4;

        private sealed class CacheEntry
        {
            private readonly LeaseState _lease;

            public CacheEntry(object gate, float widthPt, float heightPt, float bitmapScale, SKBitmap bitmap, long lastUsed)
            {
                WidthPt = widthPt;
                HeightPt = heightPt;
                BitmapScale = bitmapScale;
                Bitmap = bitmap;
                _lease = new LeaseState(gate, bitmap);
                EstimatedBytes = EstimateBitmapBytes(bitmap);
                LastUsed = lastUsed;
            }

            public float WidthPt { get; }
            public float HeightPt { get; }
            public float BitmapScale { get; }
            public SKBitmap Bitmap { get; }
            public long EstimatedBytes { get; }
            public long LastUsed { get; set; }

            public SKBitmap AcquireLease() => BitmapLease.Acquire(_lease);

            public void Retire() => BitmapLease.Retire(_lease);
        }
    }

    private sealed class LayerRenderBitmapCache
    {
        private readonly int _maxEntries;
        private readonly long _maxBytes;
        private readonly object _gate = new();
        private readonly Dictionary<string, CacheEntry> _entries = [];
        private long _clock;
        private long _totalBytes;

        public LayerRenderBitmapCache(int maxEntries, long maxBytes)
        {
            _maxEntries = Math.Max(1, maxEntries);
            _maxBytes = Math.Max(256_000_000L, maxBytes);
        }

        public bool TryGet(string key, out CachedLayerBitmapRender render)
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out CacheEntry? entry))
                {
                    render = CopyEntry(entry);
                    return true;
                }
            }

            render = new CachedLayerBitmapRender(0, 0, 1, new SKBitmap(), []);
            return false;
        }

        public bool TryGetBest(string signature, float requestedScale, out CachedLayerBitmapRender render)
        {
            lock (_gate)
            {
                CacheEntry? best = null;
                float minimumUsefulScale = Math.Max(1.0f, requestedScale * 0.70f);
                foreach (CacheEntry entry in _entries.Values)
                {
                    if (!string.Equals(entry.Signature, signature, StringComparison.Ordinal))
                        continue;
                    if (entry.BitmapScale < minimumUsefulScale)
                        continue;
                    if (best == null ||
                        entry.BitmapScale > best.BitmapScale ||
                        Math.Abs(entry.BitmapScale - best.BitmapScale) < 0.01f && entry.LastUsed > best.LastUsed)
                    {
                        best = entry;
                    }
                }

                if (best != null)
                {
                    render = CopyEntry(best);
                    return true;
                }
            }

            render = new CachedLayerBitmapRender(0, 0, 1, new SKBitmap(), []);
            return false;
        }

        public bool Contains(string key)
        {
            lock (_gate)
                return _entries.ContainsKey(key);
        }

        public void Put(
            string key,
            string signature,
            float widthPt,
            float heightPt,
            float bitmapScale,
            SKBitmap bitmap,
            IReadOnlyList<PdfLayer> layers)
        {
            SKBitmap copy = bitmap.Copy();
            long bytes = EstimateBitmapBytes(copy);
            var layerCopy = layers
                .Select(layer => new PdfLayer(layer.Number, layer.Name, layer.IsOn, layer.IsHighlighted))
                .ToList();

            lock (_gate)
            {
                if (_entries.TryGetValue(key, out CacheEntry? existing))
                {
                    _totalBytes -= existing.EstimatedBytes;
                    existing.Retire();
                    _entries.Remove(key);
                }

                _entries[key] = new CacheEntry(
                    _gate,
                    signature,
                    widthPt,
                    heightPt,
                    bitmapScale,
                    copy,
                    layerCopy,
                    bytes,
                    ++_clock);
                _totalBytes += bytes;
                Trim();
            }
        }

        private CachedLayerBitmapRender CopyEntry(CacheEntry entry)
        {
            entry.LastUsed = ++_clock;
            return new CachedLayerBitmapRender(
                entry.WidthPt,
                entry.HeightPt,
                entry.BitmapScale,
                entry.AcquireLease(),
                entry.Layers
                    .Select(layer => new PdfLayer(layer.Number, layer.Name, layer.IsOn, layer.IsHighlighted))
                    .ToList());
        }

        private void Trim()
        {
            while (_entries.Count > _maxEntries || _totalBytes > _maxBytes)
            {
                string oldestKey = "";
                long oldest = long.MaxValue;
                foreach (var pair in _entries)
                {
                    if (pair.Value.LastUsed >= oldest)
                        continue;

                    oldest = pair.Value.LastUsed;
                    oldestKey = pair.Key;
                }

                if (string.IsNullOrWhiteSpace(oldestKey))
                    return;

                _totalBytes -= _entries[oldestKey].EstimatedBytes;
                _entries[oldestKey].Retire();
                _entries.Remove(oldestKey);
            }
        }

        private static long EstimateBitmapBytes(SKBitmap bitmap) =>
            (long)Math.Max(0, bitmap.Width) * Math.Max(0, bitmap.Height) * 4;

        private sealed class CacheEntry
        {
            private readonly LeaseState _lease;

            public CacheEntry(
                object gate,
                string signature,
                float widthPt,
                float heightPt,
                float bitmapScale,
                SKBitmap bitmap,
                IReadOnlyList<PdfLayer> layers,
                long estimatedBytes,
                long lastUsed)
            {
                Signature = signature;
                WidthPt = widthPt;
                HeightPt = heightPt;
                BitmapScale = bitmapScale;
                Bitmap = bitmap;
                _lease = new LeaseState(gate, bitmap);
                Layers = layers;
                EstimatedBytes = estimatedBytes;
                LastUsed = lastUsed;
            }

            public string Signature { get; }
            public float WidthPt { get; }
            public float HeightPt { get; }
            public float BitmapScale { get; }
            public SKBitmap Bitmap { get; }
            public IReadOnlyList<PdfLayer> Layers { get; }
            public long EstimatedBytes { get; }
            public long LastUsed { get; set; }

            public SKBitmap AcquireLease() => BitmapLease.Acquire(_lease);

            public void Retire() => BitmapLease.Retire(_lease);
        }
    }

    private static string DocnetRenderCacheKey(string pdfPath, int pageIndex, float renderScale)
    {
        var info = new FileInfo(pdfPath);
        return string.Join(
            '|',
            info.FullName.ToLowerInvariant(),
            info.Exists ? info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) : "0",
            info.Exists ? info.Length.ToString(CultureInfo.InvariantCulture) : "0",
            pageIndex.ToString(CultureInfo.InvariantCulture),
            Math.Round(renderScale, 3).ToString(CultureInfo.InvariantCulture));
    }

    private static string LayerRenderBitmapCacheKey(LayerRenderRequest request) =>
        LayerRenderBitmapCacheKey(
            request.PdfPath,
            request.PdfIndex,
            request.RenderScale,
            request.LayerStates,
            request.HighlightedLayers,
            request.CachedLayers);

    private static string LayerRenderBitmapCacheSignature(LayerRenderRequest request) =>
        LayerRenderBitmapCacheSignature(
            request.PdfPath,
            request.PdfIndex,
            request.LayerStates,
            request.HighlightedLayers,
            request.CachedLayers);

    private static string LayerRenderBitmapCacheKey(
        string pdfPath,
        int pageIndex,
        float renderScale,
        IReadOnlyDictionary<int, bool> layerStates,
        IReadOnlyCollection<int> highlightedLayers,
        IReadOnlyList<PdfLayerInfo>? cachedLayers)
    {
        var info = new FileInfo(pdfPath);
        var parts = new List<string>
        {
            info.FullName.ToLowerInvariant(),
            info.Exists ? info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) : "0",
            info.Exists ? info.Length.ToString(CultureInfo.InvariantCulture) : "0",
            pageIndex.ToString(CultureInfo.InvariantCulture),
            Math.Round(renderScale, 3).ToString(CultureInfo.InvariantCulture),
            "layers",
        };
        foreach (var pair in layerStates.OrderBy(pair => pair.Key))
            parts.Add($"{pair.Key}={(pair.Value ? 1 : 0)}");

        parts.Add("hi");
        foreach (int layer in highlightedLayers.OrderBy(value => value))
            parts.Add(layer.ToString(CultureInfo.InvariantCulture));

        parts.Add("visible");
        if (cachedLayers != null)
        {
            foreach (PdfLayerInfo layer in cachedLayers.OrderBy(layer => layer.Number))
                parts.Add($"{layer.Number}={(layer.IsOn ? 1 : 0)}:{layer.Name}");
        }

        return string.Join('|', parts);
    }

    private static string LayerRenderBitmapCacheSignature(
        string pdfPath,
        int pageIndex,
        IReadOnlyDictionary<int, bool> layerStates,
        IReadOnlyCollection<int> highlightedLayers,
        IReadOnlyList<PdfLayerInfo>? cachedLayers)
    {
        var info = new FileInfo(pdfPath);
        var parts = new List<string>
        {
            info.FullName.ToLowerInvariant(),
            info.Exists ? info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) : "0",
            info.Exists ? info.Length.ToString(CultureInfo.InvariantCulture) : "0",
            pageIndex.ToString(CultureInfo.InvariantCulture),
            "layers",
        };
        foreach (var pair in layerStates.OrderBy(pair => pair.Key))
            parts.Add($"{pair.Key}={(pair.Value ? 1 : 0)}");

        parts.Add("hi");
        foreach (int layer in highlightedLayers.OrderBy(value => value))
            parts.Add(layer.ToString(CultureInfo.InvariantCulture));

        parts.Add("visible");
        if (cachedLayers != null)
        {
            foreach (PdfLayerInfo layer in cachedLayers.OrderBy(layer => layer.Number))
                parts.Add($"{layer.Number}={(layer.IsOn ? 1 : 0)}:{layer.Name}");
        }

        return string.Join('|', parts);
    }

    public static void PrefetchPagePreview(string pdfPath, int pageIndex) =>
        PrefetchPagePreview(pdfPath, pageIndex, ViewportRenderPolicy.FastPageSwitchPreviewRenderScale);

    public static void PrefetchPagePreview(string pdfPath, int pageIndex, float renderScale) =>
        PrefetchPagePreview(pdfPath, pageIndex, renderScale, preferCachedRenderImmediately: false);

    public static void PrefetchPagePreview(
        string pdfPath,
        int pageIndex,
        float renderScale,
        bool preferCachedRenderImmediately)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || pageIndex < 0)
            return;

        renderScale = Math.Clamp(renderScale, 0.10f, 4.0f);
        string cacheKey;
        try
        {
            cacheKey = DocnetRenderCacheKey(pdfPath, pageIndex, renderScale);
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport preview prefetch skipped for {pdfPath} page {pageIndex + 1}");
            return;
        }

        if (DocnetRenderCache.Contains(cacheKey))
            return;

        lock (DocnetPreviewPrefetchGate)
        {
            if (!DocnetPreviewPrefetchInFlight.Add(cacheKey))
                return;
        }

        _ = PrefetchPagePreviewAsync(pdfPath, pageIndex, renderScale, cacheKey, preferCachedRenderImmediately);
    }

    public static void PrefetchCleanLayerRender(
        string pdfPath,
        int pageIndex,
        IReadOnlyList<PdfLayerInfo>? cachedLayers,
        float renderScale = ViewportRenderPolicy.ResponsiveMinRenderScale)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || pageIndex < 0)
            return;

        renderScale = Math.Clamp(renderScale, ViewportRenderPolicy.ResponsiveMinRenderScale, 2.25f);
        string cacheKey;
        string signature;
        try
        {
            cacheKey = LayerRenderBitmapCacheKey(
                pdfPath,
                pageIndex,
                renderScale,
                EmptyLayerStates,
                EmptyHighlightedLayers,
                cachedLayers);
            signature = LayerRenderBitmapCacheSignature(
                pdfPath,
                pageIndex,
                EmptyLayerStates,
                EmptyHighlightedLayers,
                cachedLayers);
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport clean render prefetch skipped for {pdfPath} page {pageIndex + 1}");
            return;
        }

        if (LayerBitmapCache.Contains(cacheKey))
            return;

        lock (CleanRenderPrefetchGate)
        {
            if (!CleanRenderPrefetchInFlight.Add(cacheKey))
                return;
        }

        _ = PrefetchCleanLayerRenderAsync(pdfPath, pageIndex, renderScale, cachedLayers, cacheKey, signature);
    }

    public static void PrefetchRasterSheetBitmap(PageInfo page)
    {
        if (page.RasterSheet?.Enabled != true ||
            string.IsNullOrWhiteSpace(page.FolderPath) ||
            string.IsNullOrWhiteSpace(page.PdfPath) ||
            page.PdfPage < 0)
        {
            return;
        }

        if (ShouldPrefetchRasterSheetBitmap(page.RasterSheet, preferOverview: true))
            QueueRasterSheetBitmapPrefetch(page, preferOverview: true);

        if (ShouldPrefetchRasterSheetBitmap(page.RasterSheet, preferOverview: false))
            QueueRasterSheetBitmapPrefetch(page, preferOverview: false);
    }

    public static void PrefetchFullRasterSheetBitmap(PageInfo page)
    {
        if (page.RasterSheet?.Enabled != true ||
            string.IsNullOrWhiteSpace(page.FolderPath) ||
            string.IsNullOrWhiteSpace(page.PdfPath) ||
            page.PdfPage < 0)
        {
            return;
        }

        QueueRasterSheetBitmapPrefetch(page, preferOverview: false, interactivePriority: true);
    }

    public static void PrefetchRasterSheetWorkZoomBitmaps(
        PageInfo page,
        bool buildMissingDpis = false,
        bool allowDuringNavigation = false)
    {
        if (page.RasterSheet?.Enabled != true ||
            RasterSheetCacheService.IsSourceImageRaster(page.RasterSheet) ||
            RasterSheetCacheService.IsRasterDpiPinned(page.RasterSheet) ||
            string.IsNullOrWhiteSpace(page.FolderPath) ||
            string.IsNullOrWhiteSpace(page.PdfPath) ||
            page.PdfPage < 0 ||
            !Directory.Exists(page.FolderPath) ||
            !File.Exists(page.PdfPath))
        {
            return;
        }

        string cacheKey = RasterSheetWorkZoomWarmupKey(page, buildMissingDpis, allowDuringNavigation);
        lock (RasterSheetWorkZoomWarmupGate)
        {
            if (!RasterSheetWorkZoomWarmupInFlight.Add(cacheKey))
                return;
        }

        _ = PrefetchRasterSheetWorkZoomBitmapsAsync(page, cacheKey, buildMissingDpis, allowDuringNavigation);
    }

    private static bool ShouldPrefetchRasterSheetBitmap(RasterSheetSource? source, bool preferOverview)
    {
        if (source?.Enabled != true)
            return false;

        if (preferOverview)
            return RasterSheetCacheService.HasSourceImageOverview(source);

        if (!RasterSheetCacheService.IsSourceImageRaster(source))
            return RasterSheetCacheService.UseAsPageOpenRaster(source);

        return RasterSheetCacheService.ShouldUseSourceImageRasterForFastOpen(source) ||
               !RasterSheetCacheService.HasSourceImageOverview(source);
    }

    private static void QueueRasterSheetBitmapPrefetch(
        PageInfo page,
        bool preferOverview,
        bool interactivePriority = false)
    {
        if (!TryBuildRasterSheetBitmapCacheKey(
                page.FolderPath,
                page.PdfPath,
                page.RasterSheet,
                preferOverview,
                out string cacheKey,
                out _))
        {
            return;
        }

        if (RasterSheetBitmapCache.Contains(cacheKey))
            return;

        lock (RasterSheetBitmapPrefetchGate)
        {
            if (!RasterSheetBitmapPrefetchInFlight.Add(cacheKey))
                return;
        }

        _ = PrefetchRasterSheetBitmapAsync(
            page.FolderPath,
            page.PdfPath,
            page.RasterSheet!.Clone(),
            preferOverview,
            cacheKey,
            interactivePriority);
    }

    public static void PrefetchRasterSheetRefresh(PageInfo page)
    {
        // Healthy fixed-DPI pages skip this path through the rebuild predicate.
        // Stale or missing rasters still reach pin-preserving self-heal.
        PageInfo currentPage = OurPlanCoreJobStore.TryReadPage(page.FolderPath) ?? page;
        if (currentPage.RasterSheet?.Enabled != true ||
            string.IsNullOrWhiteSpace(currentPage.FolderPath) ||
            string.IsNullOrWhiteSpace(currentPage.PdfPath) ||
            currentPage.PdfPage < 0)
        {
            return;
        }

        if (!ShouldQueueRasterSheetRefreshPrefetch(currentPage))
            return;

        string cacheKey;
        try
        {
            cacheKey = RasterSheetRefreshPrefetchKey(currentPage);
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport raster refresh prefetch skipped for {currentPage.Name}");
            return;
        }

        lock (RasterSheetRefreshPrefetchGate)
        {
            if (!RasterSheetRefreshPrefetchInFlight.Add(cacheKey))
                return;
        }

        _ = PrefetchRasterSheetRefreshAsync(currentPage.FolderPath, cacheKey);
    }

    private static async Task PrefetchRasterSheetBitmapAsync(
        string pageFolder,
        string pdfPath,
        RasterSheetSource rasterSheet,
        bool preferOverview,
        string cacheKey,
        bool interactivePriority)
    {
        RasterSheetBitmapResult result = new(new SKBitmap(), 0, 0, 0, "");
        try
        {
            if (!interactivePriority)
            {
                await Task.Delay(250).ConfigureAwait(false);
                await WaitForPreviewPrefetchQuietWindowAsync().ConfigureAwait(false);
            }
            await RasterSheetBitmapPrefetchSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (RasterSheetBitmapCache.Contains(cacheKey))
                    return;

                TryWarmRasterSheetBitmapCache(
                    pageFolder,
                    pdfPath,
                    rasterSheet,
                    preferOverview,
                    action: "prefetched",
                    logFailure: false);
            }
            finally
            {
                RasterSheetBitmapPrefetchSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport raster sheet bitmap prefetch failed for {pageFolder}");
        }
        finally
        {
            result.Bitmap.Dispose();
            lock (RasterSheetBitmapPrefetchGate)
                RasterSheetBitmapPrefetchInFlight.Remove(cacheKey);
        }
    }

    private static async Task PrefetchRasterSheetWorkZoomBitmapsAsync(
        PageInfo queuedPage,
        string cacheKey,
        bool buildMissingDpis,
        bool allowDuringNavigation)
    {
        try
        {
            int delayMs = buildMissingDpis || allowDuringNavigation
                ? ViewportRenderPolicy.RasterSheetCurrentWorkZoomBuildDelayMs
                : ViewportRenderPolicy.RasterSheetWorkZoomWarmupDelayMs;
            await Task.Delay(delayMs).ConfigureAwait(false);
            if (!buildMissingDpis && !allowDuringNavigation)
                await WaitForPreviewPrefetchQuietWindowAsync().ConfigureAwait(false);
            await RasterSheetWorkZoomWarmupSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                PageInfo page = OurPlanCoreJobStore.TryReadPage(queuedPage.FolderPath) ?? queuedPage;
                if (page.RasterSheet?.Enabled != true ||
                    RasterSheetCacheService.IsSourceImageRaster(page.RasterSheet) ||
                    RasterSheetCacheService.IsRasterDpiPinned(page.RasterSheet))
                {
                    return;
                }

                IReadOnlyList<int> warmupDpis = buildMissingDpis
                    ? ViewportRenderPolicy.RasterSheetWorkZoomBuildDpiSteps
                    : ViewportRenderPolicy.RasterSheetWorkZoomWarmupDpiSteps;
                foreach (int dpi in warmupDpis)
                {
                    // Each DPI step is seconds of render+encode; re-yield to
                    // user interaction between steps, not just once up front.
                    if (!buildMissingDpis && !allowDuringNavigation)
                        await WaitForPreviewPrefetchQuietWindowAsync().ConfigureAwait(false);
                    WarmRasterSheetWorkZoomDpi(page, dpi, buildMissingDpis);
                }
            }
            finally
            {
                RasterSheetWorkZoomWarmupSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport raster work-zoom warmup failed for {queuedPage.Name}");
        }
        finally
        {
            lock (RasterSheetWorkZoomWarmupGate)
                RasterSheetWorkZoomWarmupInFlight.Remove(cacheKey);
        }
    }

    private static void WarmRasterSheetWorkZoomDpi(PageInfo page, int dpi, bool buildMissingDpis)
    {
        if (dpi <= 0 || dpi > ViewportRenderPolicy.RasterSheetDisplayMaxDpi)
            return;

        float scale = RasterSheetCacheService.RasterDpiToRenderScale(dpi);
        PageInfo currentPage = OurPlanCoreJobStore.TryReadPage(page.FolderPath) ?? page;
        if (currentPage.RasterSheet?.Enabled != true ||
            RasterSheetCacheService.IsRasterDpiPinned(currentPage.RasterSheet))
            return;

        if (!RasterSheetCacheService.HasReadyReadableRaster(currentPage, scale))
        {
            if (!buildMissingDpis)
                return;

            RasterSheetBuildResult build = RasterSheetCacheService.BuildCachePreservingEnabled(currentPage, scale);
            if (!build.Ok)
            {
                AppLog.Warn(
                    $"Viewport raster work-zoom warmup build skipped; dpi={dpi}; error='{build.Error}'; " +
                    $"page='{page.FolderPath}'; pdf='{Path.GetFileName(page.PdfPath)}'; pdfPage={page.PdfPage + 1}");
                return;
            }
        }

        currentPage = OurPlanCoreJobStore.TryReadPage(page.FolderPath) ?? currentPage;
        if (!RasterSheetCacheService.TryGetReadyReadableRasterSource(currentPage, scale, out RasterSheetSource? source) ||
            source == null)
        {
            return;
        }
        RememberReadyRasterSheetSource(currentPage, dpi, source);

        TryWarmRasterSheetBitmapCache(
            currentPage.FolderPath,
            currentPage.PdfPath,
            source,
            preferOverview: false,
            action: $"work-zoom-{dpi}dpi warmed",
            logFailure: false);
    }

    private static bool TryGetRasterSheetBitmapCache(
        string pageFolder,
        string pdfPath,
        RasterSheetSource? rasterSheet,
        bool preferOverview,
        out RasterSheetBitmapResult result)
    {
        result = new RasterSheetBitmapResult(new SKBitmap(), 0, 0, 0, "");
        if (!TryBuildRasterSheetBitmapCacheKey(
                pageFolder,
                pdfPath,
                rasterSheet,
                preferOverview,
                out string cacheKey,
                out string imagePath))
        {
            return false;
        }

        if (!RasterSheetBitmapCache.TryGet(cacheKey, out CachedBitmapRender cached))
            return false;

        result = new RasterSheetBitmapResult(
            cached.Bitmap,
            cached.WidthPt,
            cached.HeightPt,
            cached.BitmapScale,
            imagePath);
        return true;
    }

    private static bool TryPutRasterSheetBitmapCache(
        string pageFolder,
        string pdfPath,
        RasterSheetSource? rasterSheet,
        bool preferOverview,
        RasterSheetBitmapResult result)
    {
        if (!TryBuildRasterSheetBitmapCacheKey(
                pageFolder,
                pdfPath,
                rasterSheet,
                preferOverview,
                out string cacheKey,
                out _))
        {
            return false;
        }

        return RasterSheetBitmapCache.TryPut(
            cacheKey,
            result.WidthPt,
            result.HeightPt,
            result.BitmapScale,
            result.Bitmap);
    }

    private static bool TryBuildRasterSheetBitmapCacheKey(
        string pageFolder,
        string pdfPath,
        RasterSheetSource? rasterSheet,
        bool preferOverview,
        out string cacheKey,
        out string imagePath)
    {
        cacheKey = "";
        imagePath = "";
        if (rasterSheet?.Enabled != true ||
            string.IsNullOrWhiteSpace(pageFolder) ||
            string.IsNullOrWhiteSpace(pdfPath))
        {
            return false;
        }

        string relativeImage = preferOverview ? rasterSheet.OverviewImage : rasterSheet.Image;
        if (string.IsNullOrWhiteSpace(relativeImage))
            return false;

        imagePath = ResolvePageRelativePath(pageFolder, relativeImage);
        var imageInfo = new FileInfo(imagePath);
        if (!imageInfo.Exists)
            return false;

        var pdfInfo = new FileInfo(pdfPath);
        if (!pdfInfo.Exists)
            return false;

        double renderScale = preferOverview ? rasterSheet.OverviewRenderScale : rasterSheet.RenderScale;
        cacheKey = string.Join(
            '|',
            "raster-sheet-bitmap-v1",
            Path.GetFullPath(pageFolder).ToLowerInvariant(),
            pdfInfo.FullName.ToLowerInvariant(),
            pdfInfo.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture),
            pdfInfo.Length.ToString(CultureInfo.InvariantCulture),
            preferOverview ? "overview" : "full",
            imageInfo.FullName.ToLowerInvariant(),
            imageInfo.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture),
            imageInfo.Length.ToString(CultureInfo.InvariantCulture),
            rasterSheet.RenderProfile,
            rasterSheet.GeneratedAtUtc,
            rasterSheet.WidthPt.ToString(CultureInfo.InvariantCulture),
            rasterSheet.HeightPt.ToString(CultureInfo.InvariantCulture),
            renderScale.ToString(CultureInfo.InvariantCulture));
        return true;
    }

    private static string ResolvePageRelativePath(string pageFolder, string path)
    {
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        return Path.GetFullPath(Path.Combine(pageFolder, path));
    }

    private static async Task PrefetchPagePreviewAsync(
        string pdfPath,
        int pageIndex,
        float renderScale,
        string cacheKey,
        bool preferCachedRenderImmediately)
    {
        DocnetRenderResult? render = null;
        SKBitmap? decodedBitmap = null;
        try
        {
            if (preferCachedRenderImmediately &&
                await TryPrefetchPagePreviewFromPersistedCacheAsync(
                    pdfPath,
                    pageIndex,
                    renderScale,
                    cacheKey).ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(ViewportRenderPolicy.PreviewPrefetchDelayMs).ConfigureAwait(false);
            await WaitForPreviewPrefetchQuietWindowAsync().ConfigureAwait(false);
            await PreviewPrefetchSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                await WaitForPreviewPrefetchQuietWindowAsync().ConfigureAwait(false);
                if (DocnetRenderCache.Contains(cacheKey))
                    return;

                bool previewFromPersistedCache = false;
                bool previewFromLiveRenderer = false;
                if (!PdfPreviewRenderCache.TryReadCleanPreview(pdfPath, pageIndex, renderScale, out PdfLayerRenderResult preview))
                {
                    var rendered = await PdfLayerRenderService.TryRenderDedicatedProcessAsync(
                        pdfPath,
                        pageIndex,
                        renderScale,
                        EmptyLayerStates,
                        EmptyHighlightedLayers,
                        cachedLayers: null);
                    if (rendered.Ok)
                    {
                        preview = rendered.Result;
                        previewFromLiveRenderer = true;
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(rendered.Error))
                            AppLog.Warn($"Viewport PyMuPDF preview prefetch unavailable: {rendered.Error}");
                    }
                }
                else
                {
                    previewFromPersistedCache = true;
                }

                if (preview.ImageBytes.Length > 0 && preview.WidthPt > 0 && preview.HeightPt > 0)
                {
                    decodedBitmap = await Task.Run(() => DecodePdfLayerRenderBitmapWithMetrics(
                        "preview-prefetch",
                        "",
                        pdfPath,
                        pageIndex,
                        preview)).ConfigureAwait(false);
                    if (decodedBitmap != null)
                    {
                        float bitmapScale = decodedBitmap.Width / preview.WidthPt;
                        DocnetRenderCache.Put(cacheKey, preview.WidthPt, preview.HeightPt, bitmapScale, decodedBitmap);
                        if (previewFromLiveRenderer)
                            TryWritePrefetchedPreviewCache(pdfPath, pageIndex, renderScale, preview);
                        AppLog.Info(
                            $"Viewport preview prefetched; source='{(previewFromPersistedCache ? "persisted" : "pymupdf")}'; " +
                            $"pdf='{Path.GetFileName(pdfPath)}'; pdfPage={pageIndex + 1}; scale={renderScale:0.###}; bitmapScale={bitmapScale:0.###}");
                        return;
                    }
                }

                render = await Task.Run(() => RenderPageBitmapWithDocnet(pdfPath, pageIndex, renderScale)).ConfigureAwait(false);
                DocnetRenderCache.Put(cacheKey, render);
                TryWritePrefetchedPreviewCache(pdfPath, pageIndex, renderScale, render);
                AppLog.Info(
                    $"Viewport preview prefetched; source='docnet'; pdf='{Path.GetFileName(pdfPath)}'; " +
                    $"pdfPage={pageIndex + 1}; scale={renderScale:0.###}; bitmapScale={render.BitmapScale:0.###}");
            }
            finally
            {
                PreviewPrefetchSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport preview prefetch failed for {Path.GetFileName(pdfPath)} page {pageIndex + 1}");
        }
        finally
        {
            decodedBitmap?.Dispose();
            render?.Bitmap.Dispose();
            lock (DocnetPreviewPrefetchGate)
                DocnetPreviewPrefetchInFlight.Remove(cacheKey);
        }
    }

    private static async Task<bool> TryPrefetchPagePreviewFromPersistedCacheAsync(
        string pdfPath,
        int pageIndex,
        float renderScale,
        string cacheKey)
    {
        SKBitmap? decodedBitmap = null;
        try
        {
            await PreviewPrefetchSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (DocnetRenderCache.Contains(cacheKey))
                    return true;

                if (!PdfPreviewRenderCache.TryReadCleanPreview(pdfPath, pageIndex, renderScale, out PdfLayerRenderResult preview) ||
                    preview.ImageBytes.Length <= 0 ||
                    preview.WidthPt <= 0 ||
                    preview.HeightPt <= 0)
                {
                    return false;
                }

                decodedBitmap = await Task.Run(() => DecodePdfLayerRenderBitmapWithMetrics(
                    "preview-prefetch",
                    "",
                    pdfPath,
                    pageIndex,
                    preview)).ConfigureAwait(false);
                if (decodedBitmap == null)
                    return false;

                float bitmapScale = decodedBitmap.Width / preview.WidthPt;
                DocnetRenderCache.Put(cacheKey, preview.WidthPt, preview.HeightPt, bitmapScale, decodedBitmap);
                AppLog.Info(
                    $"Viewport preview prefetched; source='persisted-urgent'; " +
                    $"pdf='{Path.GetFileName(pdfPath)}'; pdfPage={pageIndex + 1}; " +
                    $"scale={renderScale:0.###}; bitmapScale={bitmapScale:0.###}");
                return true;
            }
            finally
            {
                PreviewPrefetchSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport urgent preview cache prefetch failed for {Path.GetFileName(pdfPath)} page {pageIndex + 1}");
            return false;
        }
        finally
        {
            decodedBitmap?.Dispose();
        }
    }

    private static void PausePreviewPrefetchFor(int milliseconds)
    {
        if (milliseconds <= 0)
            return;

        long pausedUntil = DateTime.UtcNow.AddMilliseconds(milliseconds).Ticks;
        while (true)
        {
            long current = Interlocked.Read(ref PreviewPrefetchPausedUntilUtcTicks);
            if (current >= pausedUntil)
                return;

            if (Interlocked.CompareExchange(
                    ref PreviewPrefetchPausedUntilUtcTicks,
                    pausedUntil,
                    current) == current)
            {
                return;
            }
        }
    }

    private static async Task WaitForPreviewPrefetchQuietWindowAsync()
    {
        while (true)
        {
            long pausedUntil = Interlocked.Read(ref PreviewPrefetchPausedUntilUtcTicks);
            int delayMs = (int)Math.Ceiling((new DateTime(pausedUntil, DateTimeKind.Utc) - DateTime.UtcNow).TotalMilliseconds);
            if (delayMs <= 0)
                return;

            await Task.Delay(Math.Min(delayMs, 500)).ConfigureAwait(false);
        }
    }

    private static void TryWritePrefetchedPreviewCache(
        string pdfPath,
        int pageIndex,
        float renderScale,
        PdfLayerRenderResult result)
    {
        if (result.ImageBytes.Length == 0 || result.WidthPt <= 0 || result.HeightPt <= 0)
            return;

        if (Math.Abs(renderScale - ViewportRenderPolicy.FastPageSwitchPreviewRenderScale) <= 0.001f)
            PdfPreviewRenderCache.TryWriteCleanPreview(pdfPath, pageIndex, renderScale, result);
        else
            PdfPreviewRenderCache.TryWriteCleanRender(pdfPath, pageIndex, renderScale, result);
    }

    private static void TryWritePrefetchedPreviewCache(
        string pdfPath,
        int pageIndex,
        float renderScale,
        DocnetRenderResult render)
    {
        if (render.WidthPt <= 0 ||
            render.HeightPt <= 0 ||
            render.Bitmap.Width <= 0 ||
            render.Bitmap.Height <= 0)
        {
            return;
        }

        try
        {
            using SKImage image = SKImage.FromBitmap(render.Bitmap);
            using SKData? data = image.Encode(SKEncodedImageFormat.Png, 85);
            if (data == null || data.Size <= 0)
                return;

            TryWritePrefetchedPreviewCache(
                pdfPath,
                pageIndex,
                renderScale,
                new PdfLayerRenderResult
                {
                    ImageBytes = data.ToArray(),
                    WidthPt = render.WidthPt,
                    HeightPt = render.HeightPt,
                    Layers = [],
                    LayersCaptured = false,
                });
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport prefetched preview cache write failed for {Path.GetFileName(pdfPath)} page {pageIndex + 1}");
        }
    }

    private static async Task PrefetchCleanLayerRenderAsync(
        string pdfPath,
        int pageIndex,
        float renderScale,
        IReadOnlyList<PdfLayerInfo>? cachedLayers,
        string cacheKey,
        string signature)
    {
        SKBitmap? decodedBitmap = null;
        try
        {
            await Task.Delay(350).ConfigureAwait(false);
            await CleanRenderPrefetchSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (LayerBitmapCache.Contains(cacheKey))
                    return;

                PdfLayerRenderResult render;
                if (!PdfPreviewRenderCache.TryReadCleanRender(pdfPath, pageIndex, renderScale, out render))
                {
                    var rendered = await PdfLayerRenderService.TryRenderDedicatedProcessAsync(
                        pdfPath,
                        pageIndex,
                        renderScale,
                        EmptyLayerStates,
                        EmptyHighlightedLayers,
                        cachedLayers);
                    if (!rendered.Ok)
                    {
                        if (!string.IsNullOrWhiteSpace(rendered.Error))
                            AppLog.Warn($"Viewport clean render prefetch unavailable: {rendered.Error}");
                        return;
                    }

                    render = rendered.Result;
                }

                decodedBitmap = await Task.Run(() => DecodePdfLayerRenderBitmapWithMetrics(
                    "clean-render-prefetch",
                    "",
                    pdfPath,
                    pageIndex,
                    render)).ConfigureAwait(false);
                if (decodedBitmap == null || render.WidthPt <= 0 || render.HeightPt <= 0)
                    return;

                float bitmapScale = decodedBitmap.Width / render.WidthPt;
                LayerBitmapCache.Put(
                    cacheKey,
                    signature,
                    render.WidthPt,
                    render.HeightPt,
                    bitmapScale,
                    decodedBitmap,
                    render.Layers);
                AppLog.Info(
                    $"Viewport clean render prefetched; pdf='{Path.GetFileName(pdfPath)}'; " +
                    $"pdfPage={pageIndex + 1}; scale={renderScale:0.###}; bitmapScale={bitmapScale:0.###}");
            }
            finally
            {
                CleanRenderPrefetchSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport clean render prefetch failed for {Path.GetFileName(pdfPath)} page {pageIndex + 1}");
        }
        finally
        {
            decodedBitmap?.Dispose();
            lock (CleanRenderPrefetchGate)
                CleanRenderPrefetchInFlight.Remove(cacheKey);
        }
    }

    private static async Task PrefetchRasterSheetRefreshAsync(string pageFolder, string cacheKey)
    {
        try
        {
            await Task.Delay(ViewportRenderPolicy.RasterSheetRefreshPrefetchDelayMs).ConfigureAwait(false);
            await WaitForPreviewPrefetchQuietWindowAsync().ConfigureAwait(false);
            await WaitForRasterSheetRefreshPrefetchCadenceAsync().ConfigureAwait(false);
            await RasterSheetRefreshPrefetchSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                await WaitForPreviewPrefetchQuietWindowAsync().ConfigureAwait(false);
                PageInfo? page = OurPlanCoreJobStore.TryReadPage(pageFolder);
                if (page?.RasterSheet?.Enabled != true ||
                    string.IsNullOrWhiteSpace(page.PdfPath) ||
                    !File.Exists(page.PdfPath) ||
                    !Directory.Exists(page.FolderPath))
                {
                    return;
                }

                bool shouldBuildOverview = RasterSheetCacheService.ShouldBuildSourceImageOverview(
                    page.FolderPath,
                    page.PdfPath,
                    page.RasterSheet,
                    out string overviewReason);
                bool shouldRebuild = RasterSheetCacheService.ShouldRebuildForReadableDisplay(
                    page.FolderPath,
                    page.PdfPath,
                    page.RasterSheet,
                    out string rebuildReason);
                if (!shouldBuildOverview && !shouldRebuild)
                    return;

                RasterSheetBuildResult result = await Task.Run(() =>
                    shouldBuildOverview && !shouldRebuild
                        ? RasterSheetCacheService.BuildOverviewForExistingSourceImageRaster(page)
                        : BuildReadableRasterSheetPreservingPinnedDpi(page)).ConfigureAwait(false);
                string reason = shouldRebuild ? rebuildReason : overviewReason;
                if (result.Ok)
                {
                    AppLog.Info(
                        $"Viewport raster refresh prefetched; reason='{reason}'; " +
                        $"page='{page.FolderPath}'; pdf='{Path.GetFileName(page.PdfPath)}'; pdfPage={page.PdfPage + 1}");
                }
                else if (!string.IsNullOrWhiteSpace(result.Error))
                {
                    AppLog.Warn(
                        $"Viewport raster refresh prefetch failed; reason='{reason}'; error='{result.Error}'; " +
                        $"page='{page.FolderPath}'; pdf='{Path.GetFileName(page.PdfPath)}'; pdfPage={page.PdfPage + 1}");
                }
            }
            finally
            {
                RasterSheetRefreshPrefetchSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport raster refresh prefetch crashed for {pageFolder}");
        }
        finally
        {
            lock (RasterSheetRefreshPrefetchGate)
                RasterSheetRefreshPrefetchInFlight.Remove(cacheKey);
        }
    }

    private static async Task WaitForRasterSheetRefreshPrefetchCadenceAsync()
    {
        await RasterSheetRefreshPrefetchCadenceSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Delay(ViewportRenderPolicy.RasterSheetRefreshPrefetchCadenceMs).ConfigureAwait(false);
            await WaitForPreviewPrefetchQuietWindowAsync().ConfigureAwait(false);
        }
        finally
        {
            RasterSheetRefreshPrefetchCadenceSemaphore.Release();
        }
    }

    private static bool ShouldQueueRasterSheetRefreshPrefetch(PageInfo page)
    {
        try
        {
            return RasterSheetCacheService.ShouldBuildSourceImageOverview(
                       page.FolderPath,
                       page.PdfPath,
                       page.RasterSheet,
                       out _) ||
                   RasterSheetCacheService.ShouldRebuildForReadableDisplay(
                       page.FolderPath,
                       page.PdfPath,
                       page.RasterSheet,
                       out _);
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport raster refresh prefetch check failed for {page.Name}");
            return false;
        }
    }

    private static string RasterSheetRefreshPrefetchKey(PageInfo page)
    {
        RasterSheetSource raster = page.RasterSheet ?? new RasterSheetSource();
        return string.Join(
            '|',
            Path.GetFullPath(page.FolderPath).ToLowerInvariant(),
            Path.GetFullPath(page.PdfPath).ToLowerInvariant(),
            page.PdfPage.ToString(CultureInfo.InvariantCulture),
            raster.RenderProfile,
            raster.GeneratedAtUtc,
            raster.OverviewImage,
            raster.OverviewRenderScale.ToString(CultureInfo.InvariantCulture));
    }

    private static string RasterSheetWorkZoomWarmupKey(
        PageInfo page,
        bool buildMissingDpis,
        bool allowDuringNavigation)
    {
        RasterSheetSource raster = page.RasterSheet ?? new RasterSheetSource();
        IReadOnlyList<int> warmupDpis = buildMissingDpis
            ? ViewportRenderPolicy.RasterSheetWorkZoomBuildDpiSteps
            : ViewportRenderPolicy.RasterSheetWorkZoomWarmupDpiSteps;
        return string.Join(
            '|',
            "work-zoom",
            Path.GetFullPath(page.FolderPath).ToLowerInvariant(),
            Path.GetFullPath(page.PdfPath).ToLowerInvariant(),
            page.PdfPage.ToString(CultureInfo.InvariantCulture),
            buildMissingDpis ? "build" : "warm",
            allowDuringNavigation ? "motion" : "quiet",
            raster.RenderProfile,
            raster.GeneratedAtUtc,
            string.Join(",", warmupDpis));
    }

    private static readonly IReadOnlyDictionary<int, bool> EmptyLayerStates =
        new Dictionary<int, bool>();

    private static readonly IReadOnlyCollection<int> EmptyHighlightedLayers =
        Array.Empty<int>();
}
