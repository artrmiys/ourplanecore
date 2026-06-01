using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private static readonly ViewportBitmapCache DocnetRenderCache = new(maxEntries: 24, maxBytes: 1_250_000_000);
    private static readonly LayerRenderBitmapCache LayerBitmapCache = new(maxEntries: 180, maxBytes: 10_500_000_000L);
    private static readonly object DocnetPreviewPrefetchGate = new();
    private static readonly HashSet<string> DocnetPreviewPrefetchInFlight = [];

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
}
