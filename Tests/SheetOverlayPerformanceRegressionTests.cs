using OurPlanCore;
using SkiaSharp;
using System.Collections;
using System.Reflection;

internal static class SheetOverlayPerformanceRegressionTests
{
    public static void CacheAndPaintPolicyAreBounded()
    {
        string main = ReadRepoFile("MainWindow.SheetOverlay.cs");
        string queueMethod = SliceBetween(
            main,
            "private void QueueSheetOverlayLoadForPageOpen(",
            "private bool TryApplyCachedSheetOverlay(");
        string cache = ReadRepoFile("MainWindow.SheetOverlay.BitmapCache.cs");
        string overlayPaint = ReadRepoFile(Path.Combine("Controls", "PdfViewport.SheetOverlay.cs"));
        string rendering = ReadRepoFile(Path.Combine("Controls", "PdfViewport.Rendering.cs"));
        string recorder = ReadRepoFile(Path.Combine("Models", "ViewportPerformanceRecorder.cs"));

        AssertTrue(
            CountOccurrences(queueMethod, "LoadSheetOverlayAsync(") == 1 &&
            queueMethod.IndexOf("TxtStatus.Text = \"Sheet overlay loading...\";", StringComparison.Ordinal) <
            queueMethod.IndexOf("LoadSheetOverlayAsync(", StringComparison.Ordinal) &&
            queueMethod.Contains("keepExistingUntilReady: false", StringComparison.Ordinal),
            "a page-open cache hit must not start a redundant async overlay build and bitmap copy");
        AssertTrue(
            cache.Contains("_entries.Count > _maxEntries || _totalBytes > _maxBytes", StringComparison.Ordinal) &&
            cache.Contains("_totalBytes -= _entries[oldestKey].EstimatedBytes", StringComparison.Ordinal) &&
            cache.Contains("_entries[oldestKey].Bitmap.Dispose()", StringComparison.Ordinal),
            "the in-memory overlay cache must trim by both entry count and native bitmap bytes");
        AssertTrue(
            overlayPaint.Contains("CurrentSheetOverlayFilterQuality()", StringComparison.Ordinal) &&
            overlayPaint.Contains("_sheetOverlayBitmapScale > displayedScale * 1.05f", StringComparison.Ordinal) &&
            overlayPaint.Contains("? SKFilterQuality.Medium", StringComparison.Ordinal) &&
            overlayPaint.Contains(": SKFilterQuality.None", StringComparison.Ordinal),
            "settled overlay minification should smooth jagged linework while navigation and near-native drawing stay crisp");
        AssertTrue(
            rendering.Contains("sheetOverlayPaintMs", StringComparison.Ordinal) &&
            rendering.Contains("sheetOverlay:", StringComparison.Ordinal) &&
            recorder.Contains("SheetOverlayPaintMs", StringComparison.Ordinal) &&
            recorder.Contains("MaxSheetOverlayPaintMs", StringComparison.Ordinal),
            "viewport diagnostics must report sheet-overlay bitmap paint separately from other overlay guides");

        VerifyBitmapCacheOwnershipAndByteEviction();
    }

    private static void VerifyBitmapCacheOwnershipAndByteEviction()
    {
        Type cacheType = typeof(MainWindow).GetNestedType("SheetOverlayBitmapCache", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SheetOverlayBitmapCache type is missing.");
        ConstructorInfo constructor = cacheType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(int), typeof(long)],
            modifiers: null)
            ?? throw new InvalidOperationException("SheetOverlayBitmapCache byte-bounded constructor is missing.");
        MethodInfo put = cacheType.GetMethod("Put", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("SheetOverlayBitmapCache.Put is missing.");
        MethodInfo tryGet = cacheType.GetMethod("TryGet", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("SheetOverlayBitmapCache.TryGet is missing.");
        MethodInfo clear = cacheType.GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("SheetOverlayBitmapCache.Clear is missing.");
        FieldInfo entriesField = cacheType.GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SheetOverlayBitmapCache entries field is missing.");
        FieldInfo totalBytesField = cacheType.GetField("_totalBytes", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SheetOverlayBitmapCache byte counter is missing.");

        object cache = constructor.Invoke([8, 600L]);
        using var first = new SKBitmap(10, 10, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var second = new SKBitmap(10, 10, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var oversized = new SKBitmap(20, 20, SKColorType.Bgra8888, SKAlphaType.Premul);

        put.Invoke(cache, ["first", first, 10f, 10f, "First"]);
        IDictionary entries = (IDictionary)(entriesField.GetValue(cache)
            ?? throw new InvalidOperationException("SheetOverlayBitmapCache entries are unavailable."));
        SKBitmap firstMaster = EntryBitmap(entries["first"]);

        put.Invoke(cache, ["second", second, 10f, 10f, "Second"]);
        AssertTrue(firstMaster.Handle == IntPtr.Zero, "byte eviction must dispose the evicted native bitmap");
        AssertFalse(TryGetAndDisposeCopy(cache, tryGet, "first"), "oldest entry should be evicted over the byte budget");
        AssertTrue(TryGetAndDisposeCopy(cache, tryGet, "second"), "newest entry should remain after byte eviction");
        AssertTrue((long)(totalBytesField.GetValue(cache) ?? -1L) == 400L, "cache byte accounting should match the retained bitmap");

        put.Invoke(cache, ["oversized", oversized, 20f, 20f, "Oversized"]);
        AssertFalse(TryGetAndDisposeCopy(cache, tryGet, "oversized"), "one bitmap larger than the cache budget should not be copied into RAM cache");
        AssertTrue(first.Handle != IntPtr.Zero && second.Handle != IntPtr.Zero, "cache operations must not dispose caller-owned bitmaps");

        SKBitmap secondMaster = EntryBitmap(entries["second"]);
        clear.Invoke(cache, null);
        AssertTrue(secondMaster.Handle == IntPtr.Zero, "cache clear must dispose retained native bitmaps");
        AssertTrue(entries.Count == 0 && (long)(totalBytesField.GetValue(cache) ?? -1L) == 0L, "cache clear must reset entries and byte accounting");
    }

    private static bool TryGetAndDisposeCopy(object cache, MethodInfo tryGet, string key)
    {
        object?[] arguments = [key, null];
        bool hit = (bool)(tryGet.Invoke(cache, arguments) ?? false);
        if (hit)
            EntryBitmap(arguments[1]).Dispose();
        return hit;
    }

    private static SKBitmap EntryBitmap(object? entry) =>
        (SKBitmap)(entry?.GetType().GetProperty("Bitmap", BindingFlags.Instance | BindingFlags.Public)?.GetValue(entry)
            ?? throw new InvalidOperationException("SheetOverlayBitmapCache entry bitmap is unavailable."));

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

    private static string SliceBetween(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        return start >= 0 && end > start
            ? source[start..end]
            : throw new InvalidOperationException($"Could not slice source between '{startMarker}' and '{endMarker}'.");
    }

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string RepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ourplancore.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find ourplancore repo root.");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message) =>
        AssertTrue(!condition, message);
}
