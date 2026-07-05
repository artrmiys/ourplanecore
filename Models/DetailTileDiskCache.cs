using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkiaSharp;

namespace OurPlaneCore;

/// <summary>
/// Disk cache for deep-zoom detail tiles. The RAM tile list is cleared on every
/// page switch, so returning to a page at zoom used to pay the full 450-1700ms
/// live render again for the first sharp tile. Tiles are grid-quantized
/// (BuildStableDetailRenderClip), which makes their clip rects stable cache
/// keys; a disk hit decodes in tens of milliseconds instead.
/// Only clean requests (no layer overrides, no highlights) are persisted, the
/// same rule as PdfPreviewRenderCache.IsCleanRenderRequest.
/// </summary>
public static class DetailTileDiskCache
{
    public const string CacheRootEnvironmentVariable = "OURPLANECORE_DETAIL_TILE_CACHE_ROOT";
    private const int MaxEntries = 384;
    private const long MaxBytes = 1_200_000_000;
    private const long MaxEntryBytes = 48_000_000;
    private static readonly object PruneLock = new();
    private static DateTime LastPruneUtc = DateTime.MinValue;
    private static readonly TimeSpan MinPruneInterval = TimeSpan.FromSeconds(30);
    private static readonly object WriteInFlightGate = new();
    private static readonly HashSet<string> WriteInFlight = new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public static bool IsCacheableRequest(
        IReadOnlyDictionary<int, bool> layerStates,
        IReadOnlyCollection<int> highlightedLayers,
        IReadOnlyList<PdfLayerInfo>? cachedLayers)
    {
        if (layerStates.Count > 0 || highlightedLayers.Count > 0)
            return false;

        // With PDF layers enabled and an OCG list attached, the render can
        // differ from the clean page; keep those out of the clean tile cache.
        return !PdfLayerRenderService.PdfLayersEnabled ||
               cachedLayers == null ||
               cachedLayers.Count == 0;
    }

    public static bool TryRead(
        string pdfPath,
        int pageIndex,
        float renderScale,
        SKRect requestedClip,
        out byte[] imageBytes,
        out SKRect appliedClip)
    {
        imageBytes = [];
        appliedClip = SKRect.Empty;
        if (!TryBuildCachePaths(pdfPath, pageIndex, renderScale, requestedClip, out CachePaths paths, out TileIdentity identity))
            return false;

        try
        {
            if (!File.Exists(paths.MetadataPath) || !File.Exists(paths.ImagePath))
                return false;

            TileMetadata? metadata = JsonSerializer.Deserialize<TileMetadata>(
                File.ReadAllText(paths.MetadataPath),
                JsonOptions);
            if (metadata == null || !metadata.Matches(identity))
            {
                TryDelete(paths);
                return false;
            }

            byte[] bytes = File.ReadAllBytes(paths.ImagePath);
            if (bytes.Length == 0)
            {
                TryDelete(paths);
                return false;
            }

            imageBytes = bytes;
            appliedClip = new SKRect(metadata.ClipLeft, metadata.ClipTop, metadata.ClipRight, metadata.ClipBottom);
            if (appliedClip.Width <= 0 || appliedClip.Height <= 0)
            {
                imageBytes = [];
                TryDelete(paths);
                return false;
            }

            // Refresh the write stamp so LRU pruning keeps recently used tiles.
            try
            {
                File.SetLastWriteTimeUtc(paths.ImagePath, DateTime.UtcNow);
            }
            catch { }
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Detail tile cache read failed for {pdfPath} page {pageIndex + 1}");
            TryDelete(paths);
            imageBytes = [];
            return false;
        }
    }

    /// <summary>
    /// Persists a rendered tile in the background. Uses the render result's PNG
    /// bytes when present, otherwise re-encodes the raw payload off-thread, so
    /// the caller keeps ownership of its decoded bitmap.
    /// </summary>
    public static void QueueWrite(
        string pdfPath,
        int pageIndex,
        float renderScale,
        SKRect requestedClip,
        SKRect appliedClip,
        PdfLayerRenderResult render)
    {
        if (appliedClip.Width <= 0 || appliedClip.Height <= 0)
            return;
        if (render.ImageBytes.Length == 0 && !render.HasRawImage)
            return;
        if (!TryBuildCachePaths(pdfPath, pageIndex, renderScale, requestedClip, out CachePaths paths, out TileIdentity identity))
            return;

        lock (WriteInFlightGate)
        {
            if (!WriteInFlight.Add(identity.Key))
                return;
        }

        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                if (File.Exists(paths.ImagePath) && File.Exists(paths.MetadataPath))
                    return;

                byte[] pngBytes = render.ImageBytes.Length > 0
                    ? render.ImageBytes
                    : EncodeRawRenderToPng(render);
                if (pngBytes.Length == 0 || pngBytes.LongLength > MaxEntryBytes)
                    return;

                Directory.CreateDirectory(paths.DirectoryPath);
                string tempImage = paths.ImagePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                string tempMetadata = paths.MetadataPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                var metadata = new TileMetadata
                {
                    PdfFingerprint = identity.PdfFingerprint,
                    PageIndex = identity.PageIndex,
                    RenderScale = identity.RenderScale,
                    ClipKey = identity.ClipKey,
                    ClipLeft = appliedClip.Left,
                    ClipTop = appliedClip.Top,
                    ClipRight = appliedClip.Right,
                    ClipBottom = appliedClip.Bottom,
                    CreatedUtc = DateTime.UtcNow,
                };

                File.WriteAllBytes(tempImage, pngBytes);
                File.WriteAllText(tempMetadata, JsonSerializer.Serialize(metadata, JsonOptions));
                File.Move(tempImage, paths.ImagePath, overwrite: true);
                File.Move(tempMetadata, paths.MetadataPath, overwrite: true);
                PruneBestEffortThrottled();
            }
            catch (Exception ex)
            {
                AppLog.Warn(ex, $"Detail tile cache write failed for {pdfPath} page {pageIndex + 1}");
            }
            finally
            {
                lock (WriteInFlightGate)
                    WriteInFlight.Remove(identity.Key);
            }
        });
    }

    private static byte[] EncodeRawRenderToPng(PdfLayerRenderResult render)
    {
        using SKBitmap? bitmap = PdfLayerRenderService.CreateBitmapFromRawRender(render);
        if (bitmap == null)
            return [];

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData? data = image.Encode(SKEncodedImageFormat.Png, 85);
        return data == null || data.Size <= 0 ? [] : data.ToArray();
    }

    private static bool TryBuildCachePaths(
        string pdfPath,
        int pageIndex,
        float renderScale,
        SKRect requestedClip,
        out CachePaths paths,
        out TileIdentity identity)
    {
        paths = new CachePaths("", "", "");
        identity = new TileIdentity("", 0, 0, "");
        if (string.IsNullOrWhiteSpace(pdfPath) ||
            pageIndex < 0 ||
            renderScale <= 0 ||
            requestedClip.Width <= 0 ||
            requestedClip.Height <= 0)
        {
            return false;
        }

        var info = new FileInfo(pdfPath);
        if (!info.Exists)
            return false;

        identity = new TileIdentity(
            PdfPreviewRenderCache.BuildPdfFingerprint(info),
            pageIndex,
            (float)Math.Round(renderScale, 3),
            QuantizedClipKey(requestedClip));
        string hash = Hash(identity.Key);
        string root = CacheRoot();
        string directory = Path.Combine(root, hash[..2]);
        paths = new CachePaths(
            directory,
            Path.Combine(directory, hash + ".png"),
            Path.Combine(directory, hash + ".json"));
        return true;
    }

    private static string QuantizedClipKey(SKRect clip) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{Math.Round(clip.Left, 1)},{Math.Round(clip.Top, 1)},{Math.Round(clip.Right, 1)},{Math.Round(clip.Bottom, 1)}");

    private static string CacheRoot()
    {
        string? overrideRoot = Environment.GetEnvironmentVariable(CacheRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overrideRoot))
            return overrideRoot;

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "OurPlaneCore", "render-cache", "detail-tiles");
    }

    private static string Hash(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDelete(CachePaths paths)
    {
        try
        {
            if (File.Exists(paths.ImagePath))
                File.Delete(paths.ImagePath);
            if (File.Exists(paths.MetadataPath))
                File.Delete(paths.MetadataPath);
        }
        catch { }
    }

    private static void PruneBestEffortThrottled()
    {
        lock (PruneLock)
        {
            DateTime now = DateTime.UtcNow;
            if (now - LastPruneUtc < MinPruneInterval)
                return;

            LastPruneUtc = now;
        }

        try
        {
            string root = CacheRoot();
            if (!Directory.Exists(root))
                return;

            var files = new List<FileInfo>();
            foreach (string path in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories))
            {
                var file = new FileInfo(path);
                if (file.Exists)
                    files.Add(file);
            }

            files.Sort(static (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            long totalBytes = 0;
            for (int i = 0; i < files.Count; i++)
            {
                totalBytes += files[i].Length;
                if (i < MaxEntries && totalBytes <= MaxBytes)
                    continue;

                DeleteCachePair(files[i].FullName);
            }
        }
        catch { }
    }

    private static void DeleteCachePair(string imagePath)
    {
        try
        {
            if (File.Exists(imagePath))
                File.Delete(imagePath);
            string metadataPath = Path.ChangeExtension(imagePath, ".json");
            if (File.Exists(metadataPath))
                File.Delete(metadataPath);
        }
        catch { }
    }

    private readonly record struct CachePaths(string DirectoryPath, string ImagePath, string MetadataPath);

    private readonly record struct TileIdentity(
        string PdfFingerprint,
        int PageIndex,
        float RenderScale,
        string ClipKey)
    {
        public string Key =>
            string.Concat(
                PdfFingerprint,
                "|",
                PageIndex.ToString(CultureInfo.InvariantCulture),
                "|",
                RenderScale.ToString("0.###", CultureInfo.InvariantCulture),
                "|",
                ClipKey);
    }

    private sealed class TileMetadata
    {
        public string PdfFingerprint { get; set; } = "";
        public int PageIndex { get; set; }
        public float RenderScale { get; set; }
        public string ClipKey { get; set; } = "";
        public float ClipLeft { get; set; }
        public float ClipTop { get; set; }
        public float ClipRight { get; set; }
        public float ClipBottom { get; set; }
        public DateTime CreatedUtc { get; set; }

        public bool Matches(TileIdentity identity) =>
            string.Equals(PdfFingerprint, identity.PdfFingerprint, StringComparison.Ordinal) &&
            PageIndex == identity.PageIndex &&
            Math.Abs(RenderScale - identity.RenderScale) < 0.001 &&
            string.Equals(ClipKey, identity.ClipKey, StringComparison.Ordinal);
    }
}
