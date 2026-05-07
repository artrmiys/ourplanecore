using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private static readonly ViewportBitmapCache DocnetRenderCache = new(maxEntries: 10);

    private sealed record CachedBitmapRender(
        float WidthPt,
        float HeightPt,
        float BitmapScale,
        SKBitmap Bitmap);

    private sealed class ViewportBitmapCache
    {
        private readonly int _maxEntries;
        private readonly object _gate = new();
        private readonly Dictionary<string, CacheEntry> _entries = [];
        private long _clock;

        public ViewportBitmapCache(int maxEntries)
        {
            _maxEntries = Math.Max(1, maxEntries);
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

        public void Put(string key, DocnetRenderResult render)
        {
            SKBitmap copy = render.Bitmap.Copy();
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out CacheEntry? existing))
                {
                    existing.Bitmap.Dispose();
                    existing.WidthPt = render.WidthPt;
                    existing.HeightPt = render.HeightPt;
                    existing.BitmapScale = render.BitmapScale;
                    existing.Bitmap = copy;
                    existing.LastUsed = ++_clock;
                    return;
                }

                _entries[key] = new CacheEntry(
                    render.WidthPt,
                    render.HeightPt,
                    render.BitmapScale,
                    copy,
                    ++_clock);
                Trim();
            }
        }

        private void Trim()
        {
            while (_entries.Count > _maxEntries)
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

                _entries[oldestKey].Bitmap.Dispose();
                _entries.Remove(oldestKey);
            }
        }

        private sealed class CacheEntry
        {
            public CacheEntry(float widthPt, float heightPt, float bitmapScale, SKBitmap bitmap, long lastUsed)
            {
                WidthPt = widthPt;
                HeightPt = heightPt;
                BitmapScale = bitmapScale;
                Bitmap = bitmap;
                LastUsed = lastUsed;
            }

            public float WidthPt { get; set; }
            public float HeightPt { get; set; }
            public float BitmapScale { get; set; }
            public SKBitmap Bitmap { get; set; }
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
}
