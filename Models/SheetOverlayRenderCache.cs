using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkiaSharp;

namespace OurPlanCore;

public static partial class SheetOverlayRenderCache
{
    public const string CacheRootEnvironmentVariable = "OURPLANCORE_SHEET_OVERLAY_CACHE_ROOT";
    private const string TintStyleVersion = "bright-v3-opacity";

    private const int MaxEntries = 256;
    private const long MaxBytes = 800_000_000;
    private const long MaxPixels = 48_000_000;
    private const long MaxPngBytes = 96_000_000;
    private const int RawBytesPerPixel = 4;
    private const string RawFormatVersion = "bgra8888-premul-v1";
    private const int PdfFingerprintChunkBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public static bool TryRead(
        PageInfo page,
        PageInfo overlayPage,
        float renderScale,
        out SKBitmap? bitmap,
        out float widthPt,
        out float heightPt)
    {
        bitmap = null;
        widthPt = 0;
        heightPt = 0;

        if (!TryBuildCachePaths(page, overlayPage, renderScale, out CachePaths paths, out CacheIdentity identity))
            return false;

        try
        {
            if (!File.Exists(paths.MetadataPath) ||
                (!File.Exists(paths.ImagePath) && !File.Exists(paths.RawPath)))
            {
                return TryReadRelocatedCache(identity, paths, out bitmap, out widthPt, out heightPt);
            }

            CacheMetadata? metadata = JsonSerializer.Deserialize<CacheMetadata>(
                File.ReadAllText(paths.MetadataPath),
                JsonOptions);
            if (metadata == null || !metadata.Matches(identity))
            {
                TryDelete(paths);
                return TryReadRelocatedCache(identity, paths, out bitmap, out widthPt, out heightPt);
            }

            SKBitmap? decoded = TryReadRaw(paths, metadata);
            if (decoded == null)
            {
                if (!File.Exists(paths.ImagePath))
                {
                    TryDelete(paths);
                    return false;
                }

                decoded = SKBitmap.Decode(paths.ImagePath);
                if (decoded != null)
                    TryWriteRawSidecar(paths, decoded, metadata, rewriteMetadata: true);
            }

            if (decoded == null)
            {
                TryDelete(paths);
                return false;
            }

            bitmap = decoded;
            widthPt = metadata.WidthPt;
            heightPt = metadata.HeightPt;
            TryTouch(paths.ImagePath);
            TryTouch(paths.MetadataPath);
            TryTouch(paths.RawPath);
            return widthPt > 0 && heightPt > 0;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Sheet overlay cache read failed for {overlayPage.PdfPath} page {overlayPage.PdfPage + 1}");
            TryDelete(paths);
            bitmap?.Dispose();
            bitmap = null;
            return false;
        }
    }

    private static bool TryReadRelocatedCache(
        CacheIdentity identity,
        CachePaths targetPaths,
        out SKBitmap? bitmap,
        out float widthPt,
        out float heightPt)
    {
        bitmap = null;
        widthPt = 0;
        heightPt = 0;

        try
        {
            string root = CacheRoot();
            if (!Directory.Exists(root))
                return false;

            foreach (string metadataPath in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
            {
                if (string.Equals(metadataPath, targetPaths.MetadataPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                CacheMetadata? metadata;
                try
                {
                    metadata = JsonSerializer.Deserialize<CacheMetadata>(
                        File.ReadAllText(metadataPath),
                        JsonOptions);
                }
                catch
                {
                    continue;
                }

                if (metadata == null || !metadata.Matches(identity))
                    continue;

                string imagePath = Path.ChangeExtension(metadataPath, ".png");
                string rawPath = Path.ChangeExtension(metadataPath, ".bgra");
                if (!File.Exists(imagePath) && !File.Exists(rawPath))
                    continue;

                var sourcePaths = new CachePaths(
                    Path.GetDirectoryName(metadataPath) ?? "",
                    imagePath,
                    metadataPath,
                    rawPath);
                SKBitmap? decoded = TryReadRaw(sourcePaths, metadata);
                if (decoded == null && File.Exists(imagePath))
                {
                    decoded = SKBitmap.Decode(imagePath);
                    if (decoded != null)
                        TryWriteRawSidecar(sourcePaths, decoded, metadata, rewriteMetadata: true);
                }

                if (decoded == null)
                    continue;

                bitmap = decoded;
                widthPt = metadata.WidthPt;
                heightPt = metadata.HeightPt;
                TryTouch(imagePath);
                TryTouch(metadataPath);
                TryTouch(rawPath);
                PromoteRelocatedCache(metadataPath, imagePath, rawPath, targetPaths);
                return widthPt > 0 && heightPt > 0;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Sheet overlay relocated cache read failed for {identity.OverlayPdfPath} page {identity.OverlayPageIndex + 1}");
            bitmap?.Dispose();
            bitmap = null;
        }

        return false;
    }

    private static void PromoteRelocatedCache(
        string metadataPath,
        string imagePath,
        string rawPath,
        CachePaths targetPaths)
    {
        try
        {
            Directory.CreateDirectory(targetPaths.DirectoryPath);
            if (File.Exists(imagePath) && !File.Exists(targetPaths.ImagePath))
                File.Copy(imagePath, targetPaths.ImagePath, overwrite: false);
            if (File.Exists(rawPath) && !File.Exists(targetPaths.RawPath))
                File.Copy(rawPath, targetPaths.RawPath, overwrite: false);
            if (!File.Exists(targetPaths.MetadataPath))
                File.Copy(metadataPath, targetPaths.MetadataPath, overwrite: false);
        }
        catch
        {
        }
    }

    public static void TryWrite(
        PageInfo page,
        PageInfo overlayPage,
        float renderScale,
        SKBitmap bitmap,
        float widthPt,
        float heightPt)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0 || widthPt <= 0 || heightPt <= 0)
            return;
        if (!IsBitmapCacheable(bitmap))
            return;
        if (!TryBuildCachePaths(page, overlayPage, renderScale, out CachePaths paths, out CacheIdentity identity))
            return;

        try
        {
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData? data = image.Encode(SKEncodedImageFormat.Png, 100);
            if (data == null || data.Size <= 0 || data.Size > MaxPngBytes)
                return;

            Directory.CreateDirectory(paths.DirectoryPath);
            string tempImage = paths.ImagePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string tempMetadata = paths.MetadataPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string tempRaw = paths.RawPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

            var metadata = new CacheMetadata
            {
                OverlayPdfPath = identity.OverlayPdfPath,
                OverlayPdfFingerprint = identity.OverlayPdfFingerprint,
                OverlayPdfLastWriteUtcTicks = identity.OverlayPdfLastWriteUtcTicks,
                OverlayPdfLength = identity.OverlayPdfLength,
                OverlayPageIndex = identity.OverlayPageIndex,
                RenderScale = identity.RenderScale,
                Color = identity.Color,
                LayerStateKey = identity.LayerStateKey,
                WidthPt = widthPt,
                HeightPt = heightPt,
                CreatedUtc = DateTime.UtcNow,
            };

            using (FileStream stream = File.Create(tempImage))
                data.SaveTo(stream);
            if (TryWriteBitmapRawFile(tempRaw, bitmap, out int pixelWidth, out int pixelHeight))
            {
                metadata.PixelWidth = pixelWidth;
                metadata.PixelHeight = pixelHeight;
                metadata.RawFormat = RawFormatVersion;
                File.Move(tempRaw, paths.RawPath, overwrite: true);
            }
            File.WriteAllText(tempMetadata, JsonSerializer.Serialize(metadata, JsonOptions));
            File.Move(tempImage, paths.ImagePath, overwrite: true);
            File.Move(tempMetadata, paths.MetadataPath, overwrite: true);
            PruneBestEffort();
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Sheet overlay cache write failed for {overlayPage.PdfPath} page {overlayPage.PdfPage + 1}");
        }
    }

    private static bool TryBuildCachePaths(
        PageInfo page,
        PageInfo overlayPage,
        float renderScale,
        out CachePaths paths,
        out CacheIdentity identity)
    {
        paths = new CachePaths("", "", "", "");
        identity = new CacheIdentity("", "", 0, 0, 0, 0, "", "");

        if (string.IsNullOrWhiteSpace(overlayPage.PdfPath) || overlayPage.PdfPage < 0)
            return false;

        var info = new FileInfo(overlayPage.PdfPath);
        if (!info.Exists)
            return false;

        string fingerprint = BuildPdfFingerprint(info);
        identity = new CacheIdentity(
            info.FullName,
            fingerprint,
            info.LastWriteTimeUtc.Ticks,
            info.Length,
            overlayPage.PdfPage,
            NormalizeScale(renderScale),
            NormalizeColor(page.OverlayColor),
            LayerStateKey(overlayPage.PdfLayers));
        string hash = Hash(identity.Key);
        string directory = Path.Combine(CacheRoot(), hash[..2]);
        paths = new CachePaths(
            directory,
            Path.Combine(directory, hash + ".png"),
            Path.Combine(directory, hash + ".json"),
            Path.Combine(directory, hash + ".bgra"));
        return true;
    }

    private static string CacheRoot()
    {
        string? overrideRoot = AppIdentity.GetEnvironmentVariable(CacheRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overrideRoot))
            return overrideRoot;

        return Path.Combine(AppIdentity.LocalRoot, "render-cache", "sheet-overlay");
    }

    private static bool IsBitmapCacheable(SKBitmap bitmap) =>
        bitmap.Width <= int.MaxValue / Math.Max(1, bitmap.Height) &&
        (long)bitmap.Width * bitmap.Height <= MaxPixels;

    private static string BuildPdfFingerprint(FileInfo info)
    {
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            AppendHashText(hash, info.Length.ToString(CultureInfo.InvariantCulture));
            AppendHashText(hash, info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture));
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
                info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                info.Length.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AppendHashText(IncrementalHash hash, string value) =>
        hash.AppendData(Encoding.UTF8.GetBytes(value));

    private static float NormalizeScale(double scale) => (float)Math.Round(scale, 3);

    private static string NormalizeColor(string? color) =>
        string.IsNullOrWhiteSpace(color) ? "#E53935" : color.Trim().ToUpperInvariant();

    private static string LayerStateKey(IReadOnlyList<PdfLayerInfo> layers) =>
        layers.Count == 0
            ? "no-layers"
            : string.Join(
                ';',
                layers
                    .OrderBy(layer => layer.Number)
                    .Select(layer => $"{layer.Number}:{layer.IsOn}:{layer.Name}"));

    private static string Hash(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryTouch(string path)
    {
        try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); }
        catch { }
    }

    private static void TryDelete(CachePaths paths)
    {
        try
        {
            if (File.Exists(paths.ImagePath))
                File.Delete(paths.ImagePath);
            if (File.Exists(paths.RawPath))
                File.Delete(paths.RawPath);
            if (File.Exists(paths.MetadataPath))
                File.Delete(paths.MetadataPath);
        }
        catch { }
    }

    private static void PruneBestEffort()
    {
        try
        {
            string root = CacheRoot();
            if (!Directory.Exists(root))
                return;

            var entries = Directory
                .EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
                .Select(BuildCacheEntry)
                .Where(entry => entry.Exists)
                .OrderByDescending(entry => entry.LastWriteTimeUtc)
                .ToList();
            long totalBytes = entries.Sum(entry => entry.SizeBytes);
            foreach (CacheEntryInfo entry in entries.Skip(MaxEntries))
            {
                totalBytes -= entry.SizeBytes;
                DeleteCachePair(entry.ImagePath);
            }

            foreach (CacheEntryInfo entry in entries.Take(MaxEntries).Reverse())
            {
                if (totalBytes <= MaxBytes)
                    break;

                totalBytes -= entry.SizeBytes;
                DeleteCachePair(entry.ImagePath);
            }
        }
        catch { }
    }

    private static CacheEntryInfo BuildCacheEntry(string metadataPath)
    {
        string imagePath = Path.ChangeExtension(metadataPath, ".png");
        string rawPath = Path.ChangeExtension(metadataPath, ".bgra");
        long sizeBytes = FileLength(metadataPath) + FileLength(imagePath) + FileLength(rawPath);
        DateTime lastWrite = new[]
            {
                LastWriteUtc(metadataPath),
                LastWriteUtc(imagePath),
                LastWriteUtc(rawPath),
            }
            .Max();
        return new CacheEntryInfo(metadataPath, imagePath, rawPath, sizeBytes, lastWrite, sizeBytes > 0);
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
            string rawPath = Path.ChangeExtension(imagePath, ".bgra");
            if (File.Exists(rawPath))
                File.Delete(rawPath);
        }
        catch { }
    }

    private static long FileLength(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static DateTime LastWriteUtc(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private readonly record struct CachePaths(
        string DirectoryPath,
        string ImagePath,
        string MetadataPath,
        string RawPath);

    private readonly record struct CacheEntryInfo(
        string MetadataPath,
        string ImagePath,
        string RawPath,
        long SizeBytes,
        DateTime LastWriteTimeUtc,
        bool Exists);

    private readonly record struct CacheIdentity(
        string OverlayPdfPath,
        string OverlayPdfFingerprint,
        long OverlayPdfLastWriteUtcTicks,
        long OverlayPdfLength,
        int OverlayPageIndex,
        float RenderScale,
        string Color,
        string LayerStateKey)
    {
        public string Key =>
            string.Concat(
                OverlayPdfFingerprint,
                "|",
                OverlayPdfLastWriteUtcTicks.ToString(CultureInfo.InvariantCulture),
                "|",
                OverlayPdfLength.ToString(CultureInfo.InvariantCulture),
                "|",
                OverlayPageIndex.ToString(CultureInfo.InvariantCulture),
                "|",
                RenderScale.ToString("0.###", CultureInfo.InvariantCulture),
                "|",
                TintStyleVersion,
                "|",
                Color,
                "|",
                LayerStateKey);
    }

    private sealed class CacheMetadata
    {
        public string OverlayPdfPath { get; set; } = "";
        public string OverlayPdfFingerprint { get; set; } = "";
        public long OverlayPdfLastWriteUtcTicks { get; set; }
        public long OverlayPdfLength { get; set; }
        public int OverlayPageIndex { get; set; }
        public float RenderScale { get; set; }
        public string Color { get; set; } = "";
        public string LayerStateKey { get; set; } = "";
        public float WidthPt { get; set; }
        public float HeightPt { get; set; }
        public int PixelWidth { get; set; }
        public int PixelHeight { get; set; }
        public string RawFormat { get; set; } = "";
        public DateTime CreatedUtc { get; set; }

        public bool Matches(CacheIdentity identity) =>
            MatchesPdf(identity) &&
            OverlayPdfLastWriteUtcTicks == identity.OverlayPdfLastWriteUtcTicks &&
            OverlayPdfLength == identity.OverlayPdfLength &&
            OverlayPageIndex == identity.OverlayPageIndex &&
            Math.Abs(RenderScale - identity.RenderScale) < 0.001 &&
            string.Equals(Color, identity.Color, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(LayerStateKey, identity.LayerStateKey, StringComparison.Ordinal) &&
            WidthPt > 0 &&
            HeightPt > 0;

        private bool MatchesPdf(CacheIdentity identity)
        {
            if (!string.IsNullOrWhiteSpace(OverlayPdfFingerprint))
                return string.Equals(OverlayPdfFingerprint, identity.OverlayPdfFingerprint, StringComparison.Ordinal);

            if (string.Equals(OverlayPdfPath, identity.OverlayPdfPath, StringComparison.OrdinalIgnoreCase))
                return true;

            return string.Equals(
                Path.GetFileName(OverlayPdfPath),
                Path.GetFileName(identity.OverlayPdfPath),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
