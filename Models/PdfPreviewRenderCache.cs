using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OurPlanCore.Controls;

namespace OurPlanCore;

public static class PdfPreviewRenderCache
{
    public const string CacheRootEnvironmentVariable = "OURPLANCORE_PDF_PREVIEW_CACHE_ROOT";
    private const int MaxEntries = 1024;
    private const long MaxBytes = 1_500_000_000;
    private const float MaxPersistedRenderScale = 2.25f;
    private const long MaxPersistedRenderPixels = 30_000_000;
    private const long MaxPersistedRenderImageBytes = 96_000_000;
    private const int MaxMemoryEntries = 96;
    private const long MaxMemoryBytes = 512_000_000;
    private const long MaxMemoryEntryBytes = 96_000_000;
    private const int PdfFingerprintChunkBytes = 64 * 1024;
    private static readonly object MemoryCacheLock = new();
    private static readonly Dictionary<string, MemoryCacheEntry> MemoryCache = [];
    private static long MemoryCacheBytes;
    private static long MemoryCacheClock;
    private static readonly object RelocatedLegacyIndexLock = new();
    private static Dictionary<string, CachePaths>? RelocatedLegacyIndex;
    private static readonly object PruneLock = new();
    private static DateTime LastPruneUtc = DateTime.MinValue;
    private static readonly TimeSpan MinPruneInterval = TimeSpan.FromSeconds(20);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public static bool TryReadCleanPreview(
        string pdfPath,
        int pageIndex,
        float renderScale,
        out PdfLayerRenderResult result) =>
        TryReadCleanRender(pdfPath, pageIndex, renderScale, out result);

    public static bool TryReadCleanRender(
        string pdfPath,
        int pageIndex,
        float renderScale,
        out PdfLayerRenderResult result)
    {
        result = new PdfLayerRenderResult();
        if (!IsPersistedRenderScale(renderScale))
            return false;
        if (!TryBuildCachePaths(pdfPath, pageIndex, renderScale, out CachePaths paths, out PreviewCacheIdentity identity))
            return false;
        if (TryReadMemoryCleanRender(identity.Key, out result))
            return true;

        if (TryReadCleanRenderFromPaths(paths, identity, out result))
            return true;

        if (TryBuildLegacyCachePaths(identity, out CachePaths legacyPaths) &&
            TryReadCleanRenderFromPaths(legacyPaths, identity, out result))
        {
            TryWriteCleanRender(pdfPath, pageIndex, renderScale, result);
            return true;
        }

        if (TryReadRelocatedLegacyCleanRender(identity, out result))
        {
            TryWriteCleanRender(pdfPath, pageIndex, renderScale, result);
            return true;
        }

        return false;
    }

    private static bool TryReadCleanRenderFromPaths(
        CachePaths paths,
        PreviewCacheIdentity identity,
        out PdfLayerRenderResult result)
    {
        result = new PdfLayerRenderResult();
        try
        {
            if (!File.Exists(paths.MetadataPath) || !File.Exists(paths.ImagePath))
                return false;

            PreviewCacheMetadata? metadata = JsonSerializer.Deserialize<PreviewCacheMetadata>(
                File.ReadAllText(paths.MetadataPath),
                JsonOptions);
            if (metadata == null || !metadata.Matches(identity))
            {
                TryDelete(paths);
                return false;
            }

            byte[] imageBytes = File.ReadAllBytes(paths.ImagePath);
            if (imageBytes.Length == 0)
            {
                TryDelete(paths);
                return false;
            }

            result = new PdfLayerRenderResult
            {
                ImageBytes = imageBytes,
                WidthPt = metadata.WidthPt,
                HeightPt = metadata.HeightPt,
                Layers = metadata.Layers
                    .Select(layer => new PdfLayer(layer.Number, layer.Name, layer.IsOn))
                    .ToList(),
                LayersCaptured = metadata.LayersCaptured,
            };
            bool valid = result.WidthPt > 0 && result.HeightPt > 0;
            if (valid)
                AddMemoryCleanRender(identity.Key, result);
            return valid;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"PyMuPDF render cache read failed for {identity.PdfPath} page {identity.PageIndex + 1}");
            TryDelete(paths);
            result = new PdfLayerRenderResult();
            return false;
        }
    }

    public static void TryWriteCleanPreview(
        string pdfPath,
        int pageIndex,
        float renderScale,
        PdfLayerRenderResult result) =>
        TryWriteCleanRender(pdfPath, pageIndex, renderScale, result);

    public static void TryWriteCleanRender(
        string pdfPath,
        int pageIndex,
        float renderScale,
        PdfLayerRenderResult result)
    {
        if (result.ImageBytes.Length == 0 || result.WidthPt <= 0 || result.HeightPt <= 0)
            return;
        if (!IsPersistedRenderScale(renderScale) || !IsRenderSizeCacheable(renderScale, result))
            return;
        if (!TryBuildCachePaths(pdfPath, pageIndex, renderScale, out CachePaths paths, out PreviewCacheIdentity identity))
            return;

        try
        {
            Directory.CreateDirectory(paths.DirectoryPath);
            string tempImage = paths.ImagePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string tempMetadata = paths.MetadataPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

            var metadata = new PreviewCacheMetadata
            {
                PdfPath = identity.PdfPath,
                PdfFingerprint = identity.PdfFingerprint,
                PdfLastWriteUtcTicks = identity.PdfLastWriteUtcTicks,
                PdfLength = identity.PdfLength,
                PageIndex = identity.PageIndex,
                RenderScale = identity.RenderScale,
                WidthPt = result.WidthPt,
                HeightPt = result.HeightPt,
                LayersCaptured = result.LayersCaptured,
                Layers = result.Layers
                    .Select(layer => new PdfLayerInfo
                    {
                        Number = layer.Number,
                        Name = layer.Name,
                        IsOn = layer.IsOn,
                    })
                    .ToList(),
                CreatedUtc = DateTime.UtcNow,
            };

            File.WriteAllBytes(tempImage, result.ImageBytes);
            File.WriteAllText(tempMetadata, JsonSerializer.Serialize(metadata, JsonOptions));
            File.Move(tempImage, paths.ImagePath, overwrite: true);
            File.Move(tempMetadata, paths.MetadataPath, overwrite: true);
            AddMemoryCleanRender(identity.Key, result);
            PruneBestEffortThrottled();
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"PyMuPDF render cache write failed for {pdfPath} page {pageIndex + 1}");
        }
    }

    public static bool IsCleanRenderRequest(
        string pdfPath,
        int pageIndex,
        double renderScale,
        IReadOnlyDictionary<int, bool> layerStates,
        IReadOnlyCollection<int> highlightedLayers)
    {
        return !string.IsNullOrWhiteSpace(pdfPath) &&
               pageIndex >= 0 &&
               layerStates.Count == 0 &&
               highlightedLayers.Count == 0 &&
               IsPersistedRenderScale(renderScale);
    }

    public static bool IsCleanPreviewRequest(
        string pdfPath,
        int pageIndex,
        double renderScale,
        IReadOnlyDictionary<int, bool> layerStates,
        IReadOnlyCollection<int> highlightedLayers)
    {
        return IsCleanRenderRequest(pdfPath, pageIndex, renderScale, layerStates, highlightedLayers) &&
               Math.Abs(renderScale - ViewportRenderPolicy.InstantPagePreviewRenderScale) < 0.001;
    }

    private static bool TryBuildCachePaths(
        string pdfPath,
        int pageIndex,
        float renderScale,
        out CachePaths paths,
        out PreviewCacheIdentity identity)
    {
        paths = new CachePaths("", "", "");
        identity = new PreviewCacheIdentity("", "", 0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(pdfPath) || pageIndex < 0)
            return false;

        var info = new FileInfo(pdfPath);
        if (!info.Exists)
            return false;

        string fullPath = info.FullName;
        string fingerprint = BuildPdfFingerprint(info);
        identity = new PreviewCacheIdentity(
            fullPath,
            fingerprint,
            info.LastWriteTimeUtc.Ticks,
            info.Length,
            pageIndex,
            NormalizeScale(renderScale));
        string hash = Hash(identity.Key);
        string root = CacheRoot();
        string directory = Path.Combine(root, hash[..2]);
        paths = new CachePaths(
            directory,
            Path.Combine(directory, hash + ".png"),
            Path.Combine(directory, hash + ".json"));
        return true;
    }

    private static bool TryBuildLegacyCachePaths(PreviewCacheIdentity identity, out CachePaths paths)
    {
        paths = new CachePaths("", "", "");
        if (string.IsNullOrWhiteSpace(identity.PdfPath))
            return false;

        string hash = Hash(identity.LegacyKey);
        string root = CacheRoot();
        string directory = Path.Combine(root, hash[..2]);
        paths = new CachePaths(
            directory,
            Path.Combine(directory, hash + ".png"),
            Path.Combine(directory, hash + ".json"));
        return true;
    }

    private static bool TryReadRelocatedLegacyCleanRender(
        PreviewCacheIdentity identity,
        out PdfLayerRenderResult result)
    {
        result = new PdfLayerRenderResult();
        Dictionary<string, CachePaths> index = RelocatedLegacyCacheIndex();
        string key = RelocatedLegacyKey(identity);
        if (!index.TryGetValue(key, out CachePaths paths))
            return false;

        return TryReadCleanRenderFromPaths(paths, identity, out result);
    }

    private static Dictionary<string, CachePaths> RelocatedLegacyCacheIndex()
    {
        lock (RelocatedLegacyIndexLock)
        {
            if (RelocatedLegacyIndex != null)
                return RelocatedLegacyIndex;

            RelocatedLegacyIndex = BuildRelocatedLegacyCacheIndex();
            return RelocatedLegacyIndex;
        }
    }

    private static Dictionary<string, CachePaths> BuildRelocatedLegacyCacheIndex()
    {
        var index = new Dictionary<string, CachePaths>(StringComparer.Ordinal);
        try
        {
            string root = CacheRoot();
            if (!Directory.Exists(root))
                return index;

            foreach (string metadataPath in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
            {
                PreviewCacheMetadata? metadata = TryReadMetadata(metadataPath);
                if (metadata == null || !metadata.IsUsable)
                    continue;

                string imagePath = Path.ChangeExtension(metadataPath, ".png");
                if (!File.Exists(imagePath))
                    continue;

                string key = RelocatedLegacyKey(metadata);
                if (string.IsNullOrWhiteSpace(key) || index.ContainsKey(key))
                    continue;

                index[key] = new CachePaths(Path.GetDirectoryName(metadataPath) ?? root, imagePath, metadataPath);
            }
        }
        catch { }

        return index;
    }

    private static PreviewCacheMetadata? TryReadMetadata(string metadataPath)
    {
        try
        {
            return JsonSerializer.Deserialize<PreviewCacheMetadata>(
                File.ReadAllText(metadataPath),
                JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string CacheRoot()
    {
        string? overrideRoot = AppIdentity.GetEnvironmentVariable(CacheRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overrideRoot))
            return overrideRoot;

        return Path.Combine(AppIdentity.LocalRoot, "render-cache", "pymupdf-preview");
    }

    private static float NormalizeScale(double scale) => (float)Math.Round(scale, 3);

    private static bool IsPersistedRenderScale(double renderScale)
    {
        float scale = NormalizeScale(renderScale);
        return scale >= ViewportRenderPolicy.ColdPageSwitchPreviewRenderScale - 0.001f &&
               scale <= MaxPersistedRenderScale + 0.001f;
    }

    private static bool IsRenderSizeCacheable(float renderScale, PdfLayerRenderResult result)
    {
        if (result.ImageBytes.LongLength > MaxPersistedRenderImageBytes)
            return false;

        long widthPx = Math.Max(1, (long)Math.Ceiling(result.WidthPt * renderScale));
        long heightPx = Math.Max(1, (long)Math.Ceiling(result.HeightPt * renderScale));
        return widthPx <= int.MaxValue / Math.Max(1, heightPx) &&
               widthPx * heightPx <= MaxPersistedRenderPixels;
    }

    private static string Hash(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // Shared with DetailTileDiskCache so both caches key by the same PDF identity.
    internal static string BuildPdfFingerprint(FileInfo info)
    {
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            AppendHashText(hash, info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AppendHashText(hash, info.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AppendHashText(hash, info.Name.ToLowerInvariant());

            byte[] buffer = new byte[PdfFingerprintChunkBytes];
            using FileStream stream = info.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read > 0)
                hash.AppendData(buffer.AsSpan(0, read));

            if (stream.Length > PdfFingerprintChunkBytes)
            {
                stream.Seek(Math.Max(0, stream.Length - PdfFingerprintChunkBytes), SeekOrigin.Begin);
                read = stream.Read(buffer, 0, buffer.Length);
                if (read > 0)
                    hash.AppendData(buffer.AsSpan(0, read));
            }

            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        catch
        {
            return string.Join(
                "|",
                info.Name.ToLowerInvariant(),
                info.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
                info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static void AppendHashText(IncrementalHash hash, string value) =>
        hash.AppendData(Encoding.UTF8.GetBytes(value));

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

    private static string RelocatedLegacyKey(PreviewCacheIdentity identity) =>
        string.Join(
            "|",
            Path.GetFileName(identity.PdfPath).ToLowerInvariant(),
            identity.PdfLastWriteUtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            identity.PdfLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
            identity.PageIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            identity.RenderScale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));

    private static string RelocatedLegacyKey(PreviewCacheMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.PdfPath))
            return "";

        return string.Join(
            "|",
            Path.GetFileName(metadata.PdfPath).ToLowerInvariant(),
            metadata.PdfLastWriteUtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            metadata.PdfLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
            metadata.PageIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            metadata.RenderScale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static bool TryReadMemoryCleanRender(string key, out PdfLayerRenderResult result)
    {
        lock (MemoryCacheLock)
        {
            if (MemoryCache.TryGetValue(key, out MemoryCacheEntry? entry))
            {
                entry.LastUsed = ++MemoryCacheClock;
                result = entry.Result;
                return true;
            }
        }

        result = new PdfLayerRenderResult();
        return false;
    }

    private static void AddMemoryCleanRender(string key, PdfLayerRenderResult result)
    {
        long bytes = result.ImageBytes.LongLength;
        if (bytes <= 0 || bytes > MaxMemoryEntryBytes || result.WidthPt <= 0 || result.HeightPt <= 0)
            return;

        lock (MemoryCacheLock)
        {
            if (MemoryCache.TryGetValue(key, out MemoryCacheEntry? existing))
            {
                MemoryCacheBytes -= existing.Bytes;
                existing.Result = result;
                existing.Bytes = bytes;
                existing.LastUsed = ++MemoryCacheClock;
                MemoryCacheBytes += bytes;
                TrimMemoryCache();
                return;
            }

            MemoryCache[key] = new MemoryCacheEntry(result, bytes, ++MemoryCacheClock);
            MemoryCacheBytes += bytes;
            TrimMemoryCache();
        }
    }

    private static void TrimMemoryCache()
    {
        while (MemoryCache.Count > MaxMemoryEntries || MemoryCacheBytes > MaxMemoryBytes)
        {
            string oldestKey = "";
            long oldest = long.MaxValue;
            foreach (var pair in MemoryCache)
            {
                if (pair.Value.LastUsed >= oldest)
                    continue;

                oldest = pair.Value.LastUsed;
                oldestKey = pair.Key;
            }

            if (string.IsNullOrWhiteSpace(oldestKey))
                return;

            MemoryCacheBytes -= MemoryCache[oldestKey].Bytes;
            MemoryCache.Remove(oldestKey);
        }
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

        PruneBestEffort();
    }

    private static void PruneBestEffort()
    {
        try
        {
            string root = CacheRoot();
            if (!Directory.Exists(root))
                return;

            var files = Directory
                .EnumerateFiles(root, "*.png", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .Where(file => file.Exists)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToList();
            long totalBytes = files.Sum(file => file.Length);
            foreach (FileInfo file in files.Skip(MaxEntries))
            {
                totalBytes -= file.Length;
                DeleteCachePair(file.FullName);
            }

            foreach (FileInfo file in files.Take(MaxEntries).Reverse())
            {
                if (totalBytes <= MaxBytes)
                    break;

                totalBytes -= file.Length;
                DeleteCachePair(file.FullName);
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

    private readonly record struct PreviewCacheIdentity(
        string PdfPath,
        string PdfFingerprint,
        long PdfLastWriteUtcTicks,
        long PdfLength,
        int PageIndex,
        float RenderScale)
    {
        public string Key =>
            string.Concat(
                PdfFingerprint,
                "|",
                PageIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "|",
                RenderScale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));

        public string LegacyKey =>
            string.Concat(
                PdfPath.ToLowerInvariant(),
                "|",
                PdfLastWriteUtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "|",
                PdfLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "|",
                PageIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "|",
                RenderScale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
    }

    private sealed class PreviewCacheMetadata
    {
        public string PdfPath { get; set; } = "";
        public string PdfFingerprint { get; set; } = "";
        public long PdfLastWriteUtcTicks { get; set; }
        public long PdfLength { get; set; }
        public int PageIndex { get; set; }
        public float RenderScale { get; set; }
        public float WidthPt { get; set; }
        public float HeightPt { get; set; }
        public bool LayersCaptured { get; set; }
        public List<PdfLayerInfo> Layers { get; set; } = [];
        public DateTime CreatedUtc { get; set; }

        public bool Matches(PreviewCacheIdentity identity) =>
            MatchesPdf(identity) &&
            PdfLastWriteUtcTicks == identity.PdfLastWriteUtcTicks &&
            PdfLength == identity.PdfLength &&
            PageIndex == identity.PageIndex &&
            Math.Abs(RenderScale - identity.RenderScale) < 0.001 &&
            IsUsable;

        public bool IsUsable =>
            WidthPt > 0 &&
            HeightPt > 0;

        private bool MatchesPdf(PreviewCacheIdentity identity)
        {
            if (!string.IsNullOrWhiteSpace(PdfFingerprint))
                return string.Equals(PdfFingerprint, identity.PdfFingerprint, StringComparison.Ordinal);

            if (string.Equals(PdfPath, identity.PdfPath, StringComparison.OrdinalIgnoreCase))
                return true;

            return string.Equals(
                Path.GetFileName(PdfPath),
                Path.GetFileName(identity.PdfPath),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class MemoryCacheEntry
    {
        public MemoryCacheEntry(PdfLayerRenderResult result, long bytes, long lastUsed)
        {
            Result = result;
            Bytes = bytes;
            LastUsed = lastUsed;
        }

        public PdfLayerRenderResult Result { get; set; }
        public long Bytes { get; set; }
        public long LastUsed { get; set; }
    }
}
