using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkiaSharp;

namespace OurPlaneCore;

public static class SheetOverlayRenderCache
{
    public const string CacheRootEnvironmentVariable = "OURPLANECORE_SHEET_OVERLAY_CACHE_ROOT";

    private const int MaxEntries = 256;
    private const long MaxBytes = 800_000_000;
    private const long MaxPixels = 48_000_000;
    private const long MaxPngBytes = 96_000_000;

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
            if (!File.Exists(paths.MetadataPath) || !File.Exists(paths.ImagePath))
                return false;

            CacheMetadata? metadata = JsonSerializer.Deserialize<CacheMetadata>(
                File.ReadAllText(paths.MetadataPath),
                JsonOptions);
            if (metadata == null || !metadata.Matches(identity))
            {
                TryDelete(paths);
                return false;
            }

            SKBitmap? decoded = SKBitmap.Decode(paths.ImagePath);
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

            var metadata = new CacheMetadata
            {
                OverlayPdfPath = identity.OverlayPdfPath,
                OverlayPdfLastWriteUtcTicks = identity.OverlayPdfLastWriteUtcTicks,
                OverlayPdfLength = identity.OverlayPdfLength,
                OverlayPageIndex = identity.OverlayPageIndex,
                RenderScale = identity.RenderScale,
                Color = identity.Color,
                Opacity = identity.Opacity,
                LayerStateKey = identity.LayerStateKey,
                WidthPt = widthPt,
                HeightPt = heightPt,
                CreatedUtc = DateTime.UtcNow,
            };

            using (FileStream stream = File.Create(tempImage))
                data.SaveTo(stream);
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
        paths = new CachePaths("", "", "");
        identity = new CacheIdentity("", 0, 0, 0, 0, "", 0, "");

        if (string.IsNullOrWhiteSpace(overlayPage.PdfPath) || overlayPage.PdfPage < 0)
            return false;

        var info = new FileInfo(overlayPage.PdfPath);
        if (!info.Exists)
            return false;

        identity = new CacheIdentity(
            info.FullName,
            info.LastWriteTimeUtc.Ticks,
            info.Length,
            overlayPage.PdfPage,
            NormalizeScale(renderScale),
            NormalizeColor(page.OverlayColor),
            NormalizeOpacity(page.OverlayOpacity),
            LayerStateKey(overlayPage.PdfLayers));
        string hash = Hash(identity.Key);
        string directory = Path.Combine(CacheRoot(), hash[..2]);
        paths = new CachePaths(
            directory,
            Path.Combine(directory, hash + ".png"),
            Path.Combine(directory, hash + ".json"));
        return true;
    }

    private static string CacheRoot()
    {
        string? overrideRoot = Environment.GetEnvironmentVariable(CacheRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overrideRoot))
            return overrideRoot;

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "OurPlaneCore", "render-cache", "sheet-overlay");
    }

    private static bool IsBitmapCacheable(SKBitmap bitmap) =>
        bitmap.Width <= int.MaxValue / Math.Max(1, bitmap.Height) &&
        (long)bitmap.Width * bitmap.Height <= MaxPixels;

    private static float NormalizeScale(double scale) => (float)Math.Round(scale, 3);

    private static string NormalizeColor(string? color) =>
        string.IsNullOrWhiteSpace(color) ? "#E53935" : color.Trim().ToUpperInvariant();

    private static double NormalizeOpacity(double opacity) =>
        Math.Round(double.IsNaN(opacity) || double.IsInfinity(opacity) || opacity <= 0 ? 0.55 : Math.Clamp(opacity, 0.05, 1.0), 3);

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

    private readonly record struct CacheIdentity(
        string OverlayPdfPath,
        long OverlayPdfLastWriteUtcTicks,
        long OverlayPdfLength,
        int OverlayPageIndex,
        float RenderScale,
        string Color,
        double Opacity,
        string LayerStateKey)
    {
        public string Key =>
            string.Concat(
                OverlayPdfPath.ToLowerInvariant(),
                "|",
                OverlayPdfLastWriteUtcTicks.ToString(CultureInfo.InvariantCulture),
                "|",
                OverlayPdfLength.ToString(CultureInfo.InvariantCulture),
                "|",
                OverlayPageIndex.ToString(CultureInfo.InvariantCulture),
                "|",
                RenderScale.ToString("0.###", CultureInfo.InvariantCulture),
                "|",
                Color,
                "|",
                Opacity.ToString("0.###", CultureInfo.InvariantCulture),
                "|",
                LayerStateKey);
    }

    private sealed class CacheMetadata
    {
        public string OverlayPdfPath { get; set; } = "";
        public long OverlayPdfLastWriteUtcTicks { get; set; }
        public long OverlayPdfLength { get; set; }
        public int OverlayPageIndex { get; set; }
        public float RenderScale { get; set; }
        public string Color { get; set; } = "";
        public double Opacity { get; set; }
        public string LayerStateKey { get; set; } = "";
        public float WidthPt { get; set; }
        public float HeightPt { get; set; }
        public DateTime CreatedUtc { get; set; }

        public bool Matches(CacheIdentity identity) =>
            string.Equals(OverlayPdfPath, identity.OverlayPdfPath, StringComparison.OrdinalIgnoreCase) &&
            OverlayPdfLastWriteUtcTicks == identity.OverlayPdfLastWriteUtcTicks &&
            OverlayPdfLength == identity.OverlayPdfLength &&
            OverlayPageIndex == identity.OverlayPageIndex &&
            Math.Abs(RenderScale - identity.RenderScale) < 0.001 &&
            string.Equals(Color, identity.Color, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(Opacity - identity.Opacity) < 0.001 &&
            string.Equals(LayerStateKey, identity.LayerStateKey, StringComparison.Ordinal) &&
            WidthPt > 0 &&
            HeightPt > 0;
    }
}
