using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private static readonly ViewportBitmapCache DocnetRenderCache = new(maxEntries: 48, maxBytes: ResolveDocnetRenderCacheBudgetBytes());
    private static readonly LayerRenderBitmapCache LayerBitmapCache = new(maxEntries: 320, maxBytes: ResolveLayerBitmapCacheBudgetBytes());
    private static readonly object DocnetPreviewPrefetchGate = new();
    private static readonly HashSet<string> DocnetPreviewPrefetchInFlight = [];
    private static readonly object CleanRenderPrefetchGate = new();
    private static readonly HashSet<string> CleanRenderPrefetchInFlight = [];
    private static readonly SemaphoreSlim CleanRenderPrefetchSemaphore = new(
        Math.Clamp(Environment.ProcessorCount / 3, 1, 4),
        Math.Clamp(Environment.ProcessorCount / 3, 1, 4));

    private static long ResolveDocnetRenderCacheBudgetBytes() =>
        ResolveViewportRamBudget(1_500_000_000L, 4_000_000_000L, 0.06);

    private static long ResolveLayerBitmapCacheBudgetBytes() =>
        ResolveViewportRamBudget(12_000_000_000L, 24_000_000_000L, 0.38);

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
                        entry.Bitmap.Copy());
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
            SKBitmap copy = render.Bitmap.Copy();
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out CacheEntry? existing))
                {
                    _totalBytes -= existing.EstimatedBytes;
                    existing.Bitmap.Dispose();
                    existing.WidthPt = render.WidthPt;
                    existing.HeightPt = render.HeightPt;
                    existing.BitmapScale = render.BitmapScale;
                    existing.Bitmap = copy;
                    existing.EstimatedBytes = EstimateBitmapBytes(copy);
                    existing.LastUsed = ++_clock;
                    _totalBytes += existing.EstimatedBytes;
                    Trim();
                    return;
                }

                var entry = new CacheEntry(
                    render.WidthPt,
                    render.HeightPt,
                    render.BitmapScale,
                    copy,
                    ++_clock);
                _entries[key] = entry;
                _totalBytes += entry.EstimatedBytes;
                Trim();
            }
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
                _entries[oldestKey].Bitmap.Dispose();
                _entries.Remove(oldestKey);
            }
        }

        private static long EstimateBitmapBytes(SKBitmap bitmap) =>
            (long)Math.Max(0, bitmap.Width) * Math.Max(0, bitmap.Height) * 4;

        private sealed class CacheEntry
        {
            public CacheEntry(float widthPt, float heightPt, float bitmapScale, SKBitmap bitmap, long lastUsed)
            {
                WidthPt = widthPt;
                HeightPt = heightPt;
                BitmapScale = bitmapScale;
                Bitmap = bitmap;
                EstimatedBytes = EstimateBitmapBytes(bitmap);
                LastUsed = lastUsed;
            }

            public float WidthPt { get; set; }
            public float HeightPt { get; set; }
            public float BitmapScale { get; set; }
            public SKBitmap Bitmap { get; set; }
            public long EstimatedBytes { get; set; }
            public long LastUsed { get; set; }
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
                    existing.Bitmap.Dispose();
                    existing.WidthPt = widthPt;
                    existing.HeightPt = heightPt;
                    existing.BitmapScale = bitmapScale;
                    existing.Signature = signature;
                    existing.Bitmap = copy;
                    existing.Layers = layerCopy;
                    existing.EstimatedBytes = bytes;
                    existing.LastUsed = ++_clock;
                    _totalBytes += bytes;
                    Trim();
                    return;
                }

                _entries[key] = new CacheEntry(
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
                entry.Bitmap.Copy(),
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
                _entries[oldestKey].Bitmap.Dispose();
                _entries.Remove(oldestKey);
            }
        }

        private static long EstimateBitmapBytes(SKBitmap bitmap) =>
            (long)Math.Max(0, bitmap.Width) * Math.Max(0, bitmap.Height) * 4;

        private sealed class CacheEntry
        {
            public CacheEntry(
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
                Layers = layers;
                EstimatedBytes = estimatedBytes;
                LastUsed = lastUsed;
            }

            public string Signature { get; set; }
            public float WidthPt { get; set; }
            public float HeightPt { get; set; }
            public float BitmapScale { get; set; }
            public SKBitmap Bitmap { get; set; }
            public IReadOnlyList<PdfLayer> Layers { get; set; }
            public long EstimatedBytes { get; set; }
            public long LastUsed { get; set; }
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

    public static void PrefetchPagePreview(string pdfPath, int pageIndex)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || pageIndex < 0)
            return;

        float renderScale = ViewportRenderPolicy.FastPageSwitchPreviewRenderScale;
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

        _ = PrefetchPagePreviewAsync(pdfPath, pageIndex, renderScale, cacheKey);
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

    private static async Task PrefetchPagePreviewAsync(
        string pdfPath,
        int pageIndex,
        float renderScale,
        string cacheKey)
    {
        DocnetRenderResult? render = null;
        try
        {
            await Task.Delay(75);
            if (DocnetRenderCache.Contains(cacheKey))
                return;

            render = await Task.Run(() => RenderPageBitmapWithDocnet(pdfPath, pageIndex, renderScale));
            DocnetRenderCache.Put(cacheKey, render);
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport preview prefetch failed for {Path.GetFileName(pdfPath)} page {pageIndex + 1}");
        }
        finally
        {
            render?.Bitmap.Dispose();
            lock (DocnetPreviewPrefetchGate)
                DocnetPreviewPrefetchInFlight.Remove(cacheKey);
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
            await Task.Delay(350);
            await CleanRenderPrefetchSemaphore.WaitAsync();
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

                decodedBitmap = await Task.Run(() => SKBitmap.Decode(render.ImageBytes));
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

    private static readonly IReadOnlyDictionary<int, bool> EmptyLayerStates =
        new Dictionary<int, bool>();

    private static readonly IReadOnlyCollection<int> EmptyHighlightedLayers =
        Array.Empty<int>();
}
