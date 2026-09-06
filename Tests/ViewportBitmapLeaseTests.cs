using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using OurPlanCore.Controls;
using SkiaSharp;

internal static class ViewportBitmapLeaseTests
{
    public static void CachedImagesSharePixelsAndSurviveEviction()
    {
        VerifyCache("ViewportBitmapCache");
        VerifyCache("LayerRenderBitmapCache");
    }

    // Also used by the external before/after proof against an existing production DLL.
    internal static void VerifyCache(string cacheName)
    {
        using var source = CreatePattern();
        using var expected = source.Copy();
        using var replacement = CreatePattern();
        using var cache = new CacheProbe(cacheName);
        cache.Put("original", source);
        SKBitmap master = cache.Master("original");
        using SKBitmap firstLease = cache.Get("original");
        using SKBitmap secondLease = cache.Get("original");
        using SKImage image = SKImage.FromBitmap(firstLease)
            ?? throw new InvalidOperationException("Native image creation failed.");

        Check(firstLease.GetPixels() == master.GetPixels(), "Cache acquisition copied the master pixels.");
        Check(PixelPointer(image) == firstLease.GetPixels(),
            cacheName + ": SKImage.FromBitmap(cached lease) copied the complete bitmap.");
        using (SKImage masterImage = SKImage.FromBitmap(master))
            Check(PixelPointer(masterImage) == master.GetPixels(),
                cacheName + ": cache master still requires a native image copy.");

        // Put must own its snapshot while leaving the caller's mutable bitmap alone.
        Check(!source.IsImmutable, "Caching unexpectedly froze the caller-owned bitmap.");
        source.Erase(SKColors.Magenta);
        Check(firstLease.GetPixel(0, 0) == expected.GetPixel(0, 0),
            "Mutating the caller changed cached pixels.");
        AssertPaintMatches(expected, image);

        // The native image keeps the first lease's pixel ref alive after its wrapper
        // is disposed. Eviction must also wait for the second independent lease.
        firstLease.Dispose();
        cache.Put("replacement", replacement);
        Check(!cache.Contains("original"), "The one-entry cache did not evict the old entry.");
        Check(master.Handle != IntPtr.Zero, "Eviction disposed pixels still owned by an image/lease.");
        Check(PixelPointer(image) == secondLease.GetPixels(), "The surviving image lost its shared pixels.");
        AssertPaintMatches(expected, image);

        secondLease.Dispose();
        Check(master.Handle != IntPtr.Zero, "The native image did not retain the final lease's pixels.");
        AssertPaintMatches(expected, image);
        image.Dispose();
        Check(master.Handle == IntPtr.Zero, "The evicted master outlived its last image and lease.");
        Check(source.Handle != IntPtr.Zero, "Lease cleanup disposed the caller's bitmap.");
    }

    private static SKBitmap CreatePattern()
    {
        var bitmap = new SKBitmap(23, 17, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var ink = new SKPaint { Color = SKColors.DarkBlue, StrokeWidth = 1.25f, IsAntialias = true };
        canvas.DrawLine(0, 0, 22, 16, ink);
        ink.Color = SKColors.OrangeRed;
        canvas.DrawCircle(8, 9, 4, ink);
        return bitmap;
    }

    private static IntPtr PixelPointer(SKImage image)
    {
        using SKPixmap pixels = image.PeekPixels()
            ?? throw new InvalidOperationException("Raster image pixels are unavailable.");
        return pixels.GetPixels();
    }

    private static void AssertPaintMatches(SKBitmap expected, SKImage actual)
    {
        foreach (SKFilterQuality quality in new[] { SKFilterQuality.None, SKFilterQuality.Low, SKFilterQuality.Medium })
        {
            using var reference = new SKBitmap(71, 53, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var result = new SKBitmap(reference.Info);
            using var referenceCanvas = new SKCanvas(reference);
            using var resultCanvas = new SKCanvas(result);
            using var paint = new SKPaint { FilterQuality = quality, IsAntialias = false };
            var source = new SKRect(1.25f, 2.5f, 21.5f, 16f);
            var destination = new SKRect(2, 1, 69, 51);
            referenceCanvas.Clear(SKColors.Transparent);
            resultCanvas.Clear(SKColors.Transparent);
            referenceCanvas.DrawBitmap(expected, source, destination, paint);
            resultCanvas.DrawImage(actual, source, destination, paint);
            referenceCanvas.Flush();
            resultCanvas.Flush();
            byte[] before = Bytes(reference), after = Bytes(result);
            Check(before.AsSpan().SequenceEqual(after), quality + ": sharing native pixels changed sampled output.");
        }
    }

    private static byte[] Bytes(SKBitmap bitmap)
    {
        var result = new byte[bitmap.RowBytes * bitmap.Height];
        Marshal.Copy(bitmap.GetPixels(), result, 0, result.Length);
        return result;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class CacheProbe : IDisposable
    {
        private readonly object _cache;
        private readonly MethodInfo _put;
        private readonly MethodInfo _get;
        private readonly IDictionary _entries;
        private readonly bool _layers;

        public CacheProbe(string cacheName)
        {
            Type type = typeof(PdfViewport).GetNestedType(cacheName, BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Missing production cache " + cacheName);
            _layers = cacheName == "LayerRenderBitmapCache";
            _cache = Activator.CreateInstance(type, [1, 256_000_000L])!;
            _put = type.GetMethods().Single(method => method.Name == "Put" &&
                method.GetParameters().Length == (_layers ? 7 : 5));
            _get = type.GetMethod("TryGet")!;
            _entries = (IDictionary)type.GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(_cache)!;
        }

        public void Put(string key, SKBitmap bitmap)
        {
            object[] args = _layers
                ? [key, key, 23f, 17f, 1f, bitmap, Array.Empty<PdfLayer>()]
                : [key, 23f, 17f, 1f, bitmap];
            _put.Invoke(_cache, args);
        }

        public SKBitmap Get(string key)
        {
            object?[] args = [key, null];
            Check((bool)_get.Invoke(_cache, args)!, "Expected production cache hit: " + key);
            return Bitmap(args[1]!);
        }

        public SKBitmap Master(string key) => Bitmap(_entries[key]!);
        public bool Contains(string key) => _entries.Contains(key);

        public void Dispose()
        {
            foreach (object entry in _entries.Values)
                entry.GetType().GetMethod("Retire")!.Invoke(entry, null);
            _entries.Clear();
        }

        private static SKBitmap Bitmap(object entry) =>
            (SKBitmap)entry.GetType().GetProperty("Bitmap")!.GetValue(entry)!;
    }
}
