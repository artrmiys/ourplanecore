using System;
using System.Collections.Generic;
using SkiaSharp;

namespace OurPlaneCore;

public partial class MainWindow
{
    private sealed class SheetOverlayBitmapCache
    {
        private readonly int _maxEntries;
        private readonly object _gate = new();
        private readonly Dictionary<string, Entry> _entries = [];
        private long _clock;

        public SheetOverlayBitmapCache(int maxEntries)
        {
            _maxEntries = Math.Max(1, maxEntries);
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
                        cached.LastUsed);
                    return true;
                }
            }

            entry = null;
            return false;
        }

        public void Put(string key, SKBitmap bitmap, float widthPt, float heightPt, string overlayName)
        {
            SKBitmap copy = bitmap.Copy();
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out Entry? existing))
                {
                    existing.Bitmap.Dispose();
                    existing.Bitmap = copy;
                    existing.WidthPt = widthPt;
                    existing.HeightPt = heightPt;
                    existing.OverlayName = overlayName;
                    existing.LastUsed = ++_clock;
                    return;
                }

                _entries[key] = new Entry(copy, widthPt, heightPt, overlayName, ++_clock);
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

        public sealed class Entry
        {
            public Entry(SKBitmap bitmap, float widthPt, float heightPt, string overlayName, long lastUsed)
            {
                Bitmap = bitmap;
                WidthPt = widthPt;
                HeightPt = heightPt;
                OverlayName = overlayName;
                LastUsed = lastUsed;
            }

            public SKBitmap Bitmap { get; set; }
            public float WidthPt { get; set; }
            public float HeightPt { get; set; }
            public string OverlayName { get; set; }
            public long LastUsed { get; set; }
        }
    }
}
