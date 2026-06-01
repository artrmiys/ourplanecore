using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private static readonly ViewportBitmapCache DocnetRenderCache = new(maxEntries: 24, maxBytes: 1_250_000_000);
    private static readonly object DocnetPreviewPrefetchGate = new();
    private static readonly HashSet<string> DocnetPreviewPrefetchInFlight = [];

    private sealed record CachedBitmapRender(
        float WidthPt,
        float HeightPt,
        float BitmapScale,
        SKBitmap Bitmap);

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
