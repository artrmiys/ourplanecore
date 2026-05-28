using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OurPlaneCore.Controls;

namespace OurPlaneCore;

public static class PdfPreviewRenderCache
{
    public const string CacheRootEnvironmentVariable = "OURPLANECORE_PDF_PREVIEW_CACHE_ROOT";
    private const int MaxEntries = 512;
    private const long MaxBytes = 1_500_000_000;
    private const float MaxPersistedRenderScale = 2.25f;
    private const long MaxPersistedRenderPixels = 30_000_000;
    private const long MaxPersistedRenderImageBytes = 96_000_000;

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
            return result.WidthPt > 0 && result.HeightPt > 0;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"PyMuPDF render cache read failed for {pdfPath} page {pageIndex + 1}");
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
            PruneBestEffort();
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
        identity = new PreviewCacheIdentity("", 0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(pdfPath) || pageIndex < 0)
            return false;

        var info = new FileInfo(pdfPath);
        if (!info.Exists)
            return false;

        string fullPath = info.FullName;
        identity = new PreviewCacheIdentity(
            fullPath,
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

    private static string CacheRoot()
    {
        string? overrideRoot = Environment.GetEnvironmentVariable(CacheRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overrideRoot))
            return overrideRoot;

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "OurPlaneCore", "render-cache", "pymupdf-preview");
    }

    private static float NormalizeScale(double scale) => (float)Math.Round(scale, 3);

    private static bool IsPersistedRenderScale(double renderScale)
    {
        float scale = NormalizeScale(renderScale);
        return scale >= ViewportRenderPolicy.InstantPagePreviewRenderScale - 0.001f &&
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

    private readonly record struct PreviewCacheIdentity(
        string PdfPath,
        long PdfLastWriteUtcTicks,
        long PdfLength,
        int PageIndex,
        float RenderScale)
    {
        public string Key =>
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
            string.Equals(PdfPath, identity.PdfPath, StringComparison.OrdinalIgnoreCase) &&
            PdfLastWriteUtcTicks == identity.PdfLastWriteUtcTicks &&
            PdfLength == identity.PdfLength &&
            PageIndex == identity.PageIndex &&
            Math.Abs(RenderScale - identity.RenderScale) < 0.001 &&
            WidthPt > 0 &&
            HeightPt > 0;
    }
}
