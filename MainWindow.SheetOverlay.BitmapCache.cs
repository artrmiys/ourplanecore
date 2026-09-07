using System;
using System.Collections.Generic;
using SkiaSharp;

namespace OurPlanCore;

public partial class MainWindow
{
    private sealed class SheetOverlayBitmapCache
    {
        private readonly int _maxEntries;
        private readonly long _maxBytes;
        private readonly object _gate = new();
        private readonly Dictionary<string, Entry> _entries = [];
        private long _clock;
        private long _totalBytes;

        public SheetOverlayBitmapCache(int maxEntries, long maxBytes)
        {
            _maxEntries = Math.Max(1, maxEntries);
            _maxBytes = Math.Max(1, maxBytes);
        }

        public bool TryGet(string key, out Entry? entry)
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out Entry? cached))
                {
                    cached.LastUsed = ++_clock;
                    entry = new Entry(
                        cached.Bitmap.Copy(),
                        cached.WidthPt,
                        cached.HeightPt,
                        cached.OverlayName,
                        cached.EstimatedBytes,
                        cached.LastUsed);
                    return true;
                }
            }

            entry = null;
            return false;
        }

        public void Put(string key, SKBitmap bitmap, float widthPt, float heightPt, string overlayName)
        {
            long estimatedBytes = EstimateBitmapBytes(bitmap);
            if (estimatedBytes <= 0 || estimatedBytes > _maxBytes)
                return;

            SKBitmap copy = bitmap.Copy();
            estimatedBytes = EstimateBitmapBytes(copy);
            if (estimatedBytes <= 0 || estimatedBytes > _maxBytes)
            {
                copy.Dispose();
                return;
            }

            lock (_gate)
            {
                if (_entries.TryGetValue(key, out Entry? existing))
                {
                    _totalBytes -= existing.EstimatedBytes;
                    existing.Bitmap.Dispose();
                    _entries.Remove(key);
                }

                _entries[key] = new Entry(
                    copy,
                    widthPt,
                    heightPt,
                    overlayName,
                    estimatedBytes,
                    ++_clock);
                _totalBytes += estimatedBytes;
                Trim();
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                foreach (Entry entry in _entries.Values)
                    entry.Bitmap.Dispose();

                _entries.Clear();
                _totalBytes = 0;
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
            (long)Math.Max(0, bitmap.RowBytes) * Math.Max(0, bitmap.Height);

        public sealed class Entry
        {
            public Entry(
                SKBitmap bitmap,
                float widthPt,
                float heightPt,
                string overlayName,
                long estimatedBytes,
                long lastUsed)
            {
                Bitmap = bitmap;
                WidthPt = widthPt;
                HeightPt = heightPt;
                OverlayName = overlayName;
                EstimatedBytes = estimatedBytes;
                LastUsed = lastUsed;
            }

            public SKBitmap Bitmap { get; set; }
            public float WidthPt { get; set; }
            public float HeightPt { get; set; }
            public string OverlayName { get; set; }
            public long EstimatedBytes { get; }
            public long LastUsed { get; set; }
        }
    }
}
