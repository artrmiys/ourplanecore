using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

public sealed record RasterSheetBuildResult(
    bool Ok,
    RasterSheetSource? Source,
    string ImagePath,
    string Error,
    bool Reused = false);

public sealed record RasterSheetBitmapResult(
    SKBitmap Bitmap,
    float WidthPt,
    float HeightPt,
    float BitmapScale,
    string ImagePath);

public sealed record RasterSheetCacheCompactResult(
    bool Ok,
    int DeletedFiles,
    long DeletedBytes,
    string Error);

public static class RasterSheetCacheService
{
    public const int DefaultRasterDpi = 200;
    public const int MaxRasterDpi = 400;
    public const float DefaultRenderScale = 200f / 72f;
    public const string CacheFolderName = "raster";
    public const string WorkingImageName = "working.png";
    public const string CompactWorkingImageName = "working.webp";
    public const string OverviewImageName = "overview.png";
    public const string SnapIndexName = "snap.json";
    public const string ReadableRasterProfile = "readable-raster-v2";
    public const string SourceImageRasterProfile = "source-image-v1";
    public const string ReadableLineBoostProfile = "lineboost-v1";
    public const string PngRasterFormat = "png";
    public const string WebpRasterFormat = "webp";
    public const long SourceImageOverviewMaxPixels = 8_000_000;
    public const long SourceImageFastOpenMaxPixels = 18_000_000;
    private const float LosslessWebpCompressionEffort = 65f;
    private const double SourceImageOverviewScaleTolerance = 0.92;

    public static RasterSheetBuildResult BuildAndEnable(PageInfo page, float renderScale = DefaultRenderScale)
    {
        if (string.IsNullOrWhiteSpace(page.FolderPath) || !Directory.Exists(page.FolderPath))
            return Failed("Page folder is missing.");
        if (string.IsNullOrWhiteSpace(page.PdfPath) || !File.Exists(page.PdfPath))
            return Failed($"Source PDF is missing: {page.PdfPath}");
        if (page.PdfPage < 0)
            return Failed("PDF page index is invalid.");

        float scale = Math.Clamp(renderScale, 0.35f, MaxRasterDpi / 72f);
        if (TryEnableReadyReadableRaster(page, scale, out RasterSheetBuildResult reused))
            return reused;

        if (!PdfLayerRenderService.TryRender(
                page.PdfPath,
                page.PdfPage,
                scale,
                new Dictionary<int, bool>(),
                [],
                page.PdfLayersCached ? page.PdfLayers : null,
                out PdfLayerRenderResult render,
                out string error))
        {
            return Failed(error.Length == 0 ? "PDF render failed." : error);
        }

        if (render.ImageBytes.Length == 0 || render.WidthPt <= 0 || render.HeightPt <= 0)
            return Failed("PDF render returned an empty image.");

        using SKBitmap? decoded = SKBitmap.Decode(render.ImageBytes);
        if (decoded == null)
            return Failed("Rendered image could not be decoded.");

        byte[] workingImageBytes;
        string workingImageName;
        string workingImageFormat;
        if (TryEncodeLosslessWebp(decoded, out byte[] webpBytes))
        {
            workingImageBytes = webpBytes;
            workingImageName = WorkingImageNameForRenderScale(scale);
            workingImageFormat = WebpRasterFormat;
        }
        else
        {
            workingImageBytes = render.ImageBytes;
            workingImageName = LegacyWorkingImageNameForRenderScale(scale);
            workingImageFormat = PngRasterFormat;
        }

        string rasterDir = Path.Combine(page.FolderPath, CacheFolderName);
        Directory.CreateDirectory(rasterDir);
        string imagePath = Path.Combine(rasterDir, workingImageName);
        string tempPath = imagePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(tempPath, workingImageBytes);
        File.Move(tempPath, imagePath, overwrite: true);

        var pdfInfo = new FileInfo(page.PdfPath);
        var source = new RasterSheetSource
        {
            Enabled = true,
            Image = Path.GetRelativePath(page.FolderPath, imagePath),
            Format = workingImageFormat,
            RenderProfile = ReadableRasterProfile,
            RenderScale = render.WidthPt > 0 ? decoded.Width / render.WidthPt : scale,
            WidthPt = render.WidthPt,
            HeightPt = render.HeightPt,
            PdfLastWriteUtcTicks = pdfInfo.LastWriteTimeUtc.Ticks,
            PdfLength = pdfInfo.Length,
            GeneratedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        };

        TryWriteSnapIndex(page, rasterDir, source, out string snapError);
        if (!string.IsNullOrWhiteSpace(snapError))
            AppLog.Warn($"Raster sheet snap index unavailable for '{page.Name}': {snapError}");

        OurPlaneCoreJobStore.SavePageRasterSheet(page.FolderPath, source);
        return new RasterSheetBuildResult(true, source, imagePath, "");

        static RasterSheetBuildResult Failed(string error) =>
            new(false, null, "", error);
    }

    public static RasterSheetBuildResult BuildCachePreservingEnabled(PageInfo page, float renderScale = DefaultRenderScale)
    {
        RasterSheetSource? original = CurrentRasterSheetSource(page)?.Clone();
        bool originalEnabled = original?.Enabled == true;
        bool restoreOriginal =
            original != null &&
            (CanTrustReadableRasterMetadata(page, original) || IsSourceImageRaster(original));

        RasterSheetBuildResult result = BuildAndEnable(page, renderScale);
        if (!result.Ok)
            return result;

        if (restoreOriginal)
        {
            OurPlaneCoreJobStore.SavePageRasterSheet(page.FolderPath, original);
            return result with { Source = original };
        }

        if (result.Source == null)
            return result;

        RasterSheetSource prepared = result.Source.Clone();
        prepared.Enabled = originalEnabled;
        OurPlaneCoreJobStore.SavePageRasterSheet(page.FolderPath, prepared);
        return result with { Source = prepared };
    }

    public static bool HasReadyReadableRaster(PageInfo page, float renderScale = DefaultRenderScale)
    {
        float scale = Math.Clamp(renderScale, 0.35f, MaxRasterDpi / 72f);
        return TryFindReusableReadableRaster(page, scale, out _, out _);
    }

    public static bool TryEnableReadyReadableRaster(
        PageInfo page,
        float renderScale,
        out RasterSheetBuildResult result)
    {
        result = new RasterSheetBuildResult(false, null, "", "");
        float scale = Math.Clamp(renderScale, 0.35f, MaxRasterDpi / 72f);
        if (!TryFindReusableReadableRaster(page, scale, out RasterSheetSource? source, out string imagePath) ||
            source == null)
        {
            return false;
        }

        string rasterDir = Path.Combine(page.FolderPath, CacheFolderName);
        if (!HasReadySnapIndex(page.FolderPath, source))
        {
            Directory.CreateDirectory(rasterDir);
            TryWriteSnapIndex(page, rasterDir, source, out string snapError);
            if (!string.IsNullOrWhiteSpace(snapError))
                AppLog.Warn($"Raster sheet snap index unavailable for '{page.Name}': {snapError}");
        }

        source.Enabled = true;
        OurPlaneCoreJobStore.SavePageRasterSheet(page.FolderPath, source);
        result = new RasterSheetBuildResult(true, source, imagePath, "", Reused: true);
        return true;
    }

    private static bool TryFindReusableReadableRaster(
        PageInfo page,
        float renderScale,
        out RasterSheetSource? source,
        out string imagePath)
    {
        imagePath = "";
        source = CurrentRasterSheetSource(page);

        if (source != null &&
            IsReusableReadableRaster(page, source, renderScale, out imagePath))
        {
            return true;
        }

        return TryBuildReusableReadableVariant(page, source, renderScale, out source, out imagePath);
    }

    private static RasterSheetSource? CurrentRasterSheetSource(PageInfo page)
    {
        RasterSheetSource? source = page.RasterSheet?.Clone();
        if (source != null ||
            string.IsNullOrWhiteSpace(page.FolderPath) ||
            OurPlaneCoreJobStore.TryReadPage(page.FolderPath) is not { RasterSheet: not null } persistedPage)
        {
            return source;
        }

        return persistedPage.RasterSheet.Clone();
    }

    private static bool TryBuildReusableReadableVariant(
        PageInfo page,
        RasterSheetSource? source,
        float renderScale,
        out RasterSheetSource? reusable,
        out string imagePath)
    {
        reusable = null;
        imagePath = "";
        if (!CanTrustReadableRasterMetadata(page, source))
            return false;

        foreach (string candidateName in WorkingImageCandidatesForRenderScale(renderScale))
        {
            string candidatePath = Path.Combine(page.FolderPath, CacheFolderName, candidateName);
            if (!File.Exists(candidatePath))
                continue;

            using SKBitmap? bitmap = SKBitmap.Decode(candidatePath);
            if (bitmap == null)
                continue;

            double actualScale = source!.WidthPt > 0 ? bitmap.Width / source.WidthPt : 0;
            if (RenderScaleToDpi(actualScale) != RenderScaleToDpi(renderScale))
                continue;

            var pdfInfo = new FileInfo(page.PdfPath);
            RasterSheetSource clone = source.Clone();
            clone.Enabled = true;
            clone.Image = Path.GetRelativePath(page.FolderPath, candidatePath);
            clone.OverviewImage = "";
            clone.Format = RasterFormatForPath(candidatePath);
            clone.RenderProfile = ReadableRasterProfile;
            clone.RenderScale = actualScale;
            clone.PdfLastWriteUtcTicks = pdfInfo.LastWriteTimeUtc.Ticks;
            clone.PdfLength = pdfInfo.Length;
            clone.GeneratedAtUtc = File.GetLastWriteTimeUtc(candidatePath).ToString("O", CultureInfo.InvariantCulture);
            reusable = clone;
            imagePath = candidatePath;
            return true;
        }

        return false;
    }

    public static RasterSheetBuildResult BuildFromImageAndEnable(
        PageInfo page,
        string sourceImagePath,
        double widthPt,
        double heightPt)
    {
        if (string.IsNullOrWhiteSpace(page.FolderPath) || !Directory.Exists(page.FolderPath))
            return Failed("Page folder is missing.");
        if (string.IsNullOrWhiteSpace(page.PdfPath) || !File.Exists(page.PdfPath))
            return Failed($"Source PDF is missing: {page.PdfPath}");
        if (string.IsNullOrWhiteSpace(sourceImagePath) || !File.Exists(sourceImagePath))
            return Failed($"Source image is missing: {sourceImagePath}");
        if (widthPt <= 0 || heightPt <= 0 || double.IsNaN(widthPt) || double.IsNaN(heightPt))
            return Failed("Page image size is invalid.");

        using SKBitmap? decoded = PageImageBitmapDecoder.Decode(sourceImagePath);
        if (decoded == null)
            return Failed("Source image could not be decoded.");

        byte[] workingImageBytes;
        string workingImageName;
        string workingImageFormat;
        if (TryEncodeLosslessWebp(decoded, out byte[] webpBytes))
        {
            workingImageBytes = webpBytes;
            workingImageName = CompactWorkingImageName;
            workingImageFormat = WebpRasterFormat;
        }
        else
        {
            using SKImage image = SKImage.FromBitmap(decoded);
            using SKData? data = image.Encode(SKEncodedImageFormat.Png, 100);
            if (data == null || data.Size == 0)
                return Failed("Source image could not be encoded as PNG.");

            workingImageBytes = data.ToArray();
            workingImageName = WorkingImageName;
            workingImageFormat = PngRasterFormat;
        }

        string rasterDir = Path.Combine(page.FolderPath, CacheFolderName);
        Directory.CreateDirectory(rasterDir);
        string imagePath = Path.Combine(rasterDir, workingImageName);
        string tempPath = imagePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(tempPath, workingImageBytes);
        File.Move(tempPath, imagePath, overwrite: true);

        bool hasOverview = TryWriteSourceImageOverview(
            decoded,
            rasterDir,
            page.FolderPath,
            widthPt,
            out string overviewImage,
            out double overviewRenderScale,
            out string overviewError);
        if (!hasOverview && !string.IsNullOrWhiteSpace(overviewError))
            AppLog.Warn($"Source image overview cache unavailable for '{page.Name}': {overviewError}");

        var pdfInfo = new FileInfo(page.PdfPath);
        var source = new RasterSheetSource
        {
            Enabled = true,
            Image = Path.GetRelativePath(page.FolderPath, imagePath),
            OverviewImage = overviewImage,
            Format = workingImageFormat,
            RenderProfile = SourceImageRasterProfile,
            RenderScale = widthPt > 0 ? decoded.Width / widthPt : 0,
            OverviewRenderScale = overviewRenderScale,
            WidthPt = widthPt,
            HeightPt = heightPt,
            PdfLastWriteUtcTicks = pdfInfo.LastWriteTimeUtc.Ticks,
            PdfLength = pdfInfo.Length,
            GeneratedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            SnapBlackOnly = true,
        };

        OurPlaneCoreJobStore.SavePageRasterSheet(page.FolderPath, source);
        return new RasterSheetBuildResult(true, source, imagePath, "");

        static RasterSheetBuildResult Failed(string error) =>
            new(false, null, "", error);
    }

    public static RasterSheetBuildResult BuildOverviewForExistingSourceImageRaster(PageInfo page)
    {
        if (string.IsNullOrWhiteSpace(page.FolderPath) || !Directory.Exists(page.FolderPath))
            return Failed("Page folder is missing.");
        if (string.IsNullOrWhiteSpace(page.PdfPath) || !File.Exists(page.PdfPath))
            return Failed($"Source PDF is missing: {page.PdfPath}");

        RasterSheetSource? source = page.RasterSheet?.Clone();
        if (!IsSourceImageRaster(source) || source!.WidthPt <= 0 || source.HeightPt <= 0 || source.RenderScale <= 0)
            return Failed("Source image raster is not eligible for overview.");
        if (IsStale(page.PdfPath, source))
            return Failed("Source PDF changed.");

        if (!NeedsSourceImageOverview(source))
        {
            return Failed("Source image overview is not needed.");
        }

        string sourceImagePath = ResolveImagePath(page.FolderPath, source);
        if (!File.Exists(sourceImagePath))
            return Failed("Source image raster file is missing.");
        using SKBitmap? decoded = PageImageBitmapDecoder.Decode(sourceImagePath);
        if (decoded == null)
            return Failed("Source image raster could not be decoded.");

        string rasterDir = Path.Combine(page.FolderPath, CacheFolderName);
        Directory.CreateDirectory(rasterDir);
        bool hasOverview = TryWriteSourceImageOverview(
            decoded,
            rasterDir,
            page.FolderPath,
            source.WidthPt,
            out string overviewImage,
            out double overviewRenderScale,
            out string overviewError);
        if (!hasOverview)
            return Failed(string.IsNullOrWhiteSpace(overviewError)
                ? "Source image overview could not be generated."
                : overviewError);

        source.OverviewImage = overviewImage;
        source.OverviewRenderScale = overviewRenderScale;
        OurPlaneCoreJobStore.SavePageRasterSheet(page.FolderPath, source);
        string overviewPath = ResolvePagePath(page.FolderPath, overviewImage);
        return new RasterSheetBuildResult(true, source, overviewPath, "");

        static RasterSheetBuildResult Failed(string error) =>
            new(false, null, "", error);
    }

    public static bool TrySetEnabled(PageInfo page, bool enabled, out string error)
    {
        return TrySetEnabled(page, enabled, out error, out _);
    }

    public static bool TrySetEnabled(PageInfo page, bool enabled, out string error, out bool changed)
    {
        error = "";
        changed = false;
        RasterSheetSource? source = page.RasterSheet?.Clone();
        if (source == null || string.IsNullOrWhiteSpace(source.Image))
        {
            if (!enabled)
                return true;

            error = "No raster cache exists for this sheet.";
            return false;
        }

        changed = source.Enabled != enabled;
        if (!changed)
            return true;

        source.Enabled = enabled;
        OurPlaneCoreJobStore.SavePageRasterSheet(page.FolderPath, source);
        return true;
    }

    public static RasterSheetCacheCompactResult CompactCache(PageInfo page)
    {
        if (string.IsNullOrWhiteSpace(page.FolderPath) || !Directory.Exists(page.FolderPath))
            return new RasterSheetCacheCompactResult(true, 0, 0, "");

        string rasterDir = Path.Combine(page.FolderPath, CacheFolderName);
        if (!Directory.Exists(rasterDir))
            return new RasterSheetCacheCompactResult(true, 0, 0, "");

        var keepPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        RasterSheetSource? source = CurrentRasterSheetSource(page);
        if (source != null)
        {
            AddReferencedCachePath(page.FolderPath, source.Image, keepPaths);
            AddReferencedCachePath(page.FolderPath, source.OverviewImage, keepPaths);
            AddReferencedCachePath(page.FolderPath, source.SnapIndex, keepPaths);
        }

        int deleted = 0;
        long bytes = 0;
        int failed = 0;
        string firstError = "";
        foreach (string path in Directory.EnumerateFiles(rasterDir).Where(IsCompactableCacheFile).ToList())
        {
            string fullPath = Path.GetFullPath(path);
            if (keepPaths.Contains(fullPath))
                continue;

            try
            {
                var info = new FileInfo(fullPath);
                long length = info.Exists ? info.Length : 0;
                File.Delete(fullPath);
                deleted++;
                bytes += Math.Max(0, length);
            }
            catch (Exception ex)
            {
                failed++;
                if (string.IsNullOrWhiteSpace(firstError))
                    firstError = ex.Message;
            }
        }

        TryDeleteEmptyRasterDirectory(rasterDir);
        if (failed > 0)
        {
            string error = $"{failed.ToString(CultureInfo.InvariantCulture)} raster cache file(s) could not be deleted";
            if (!string.IsNullOrWhiteSpace(firstError))
                error += $": {firstError}";
            return new RasterSheetCacheCompactResult(false, deleted, bytes, error);
        }

        return new RasterSheetCacheCompactResult(true, deleted, bytes, "");
    }

    public static string DisplayStatus(
        PageInfo page,
        IReadOnlyDictionary<string, IReadOnlyList<int>>? readyDpisByPageFolder = null)
    {
        RasterSheetSource? source = page.RasterSheet;
        string cachedDpis = CachedReadableDpiSummary(page, readyDpisByPageFolder);
        if (source == null || string.IsNullOrWhiteSpace(source.Image))
            return AppendCachedDpiSummary("PDF", cachedDpis);

        string imagePath = ResolveImagePath(page.FolderPath, source);
        if (!File.Exists(imagePath))
            return AppendCachedDpiSummary(source.Enabled ? "Raster missing" : "Raster off", cachedDpis);
        if (!source.Enabled)
            return AppendCachedDpiSummary("Raster off", cachedDpis);
        if (IsStale(page.PdfPath, source))
            return "Raster stale";
        if (IsLegacyLineBoost(source))
            return "Raster legacy";

        string scale = source.RenderScale > 0
            ? $"{RenderScaleToDpi(source.RenderScale).ToString(CultureInfo.InvariantCulture)}dpi"
            : "?";
        string profile = string.Equals(source.RenderProfile, ReadableRasterProfile, StringComparison.OrdinalIgnoreCase)
            ? "+readable"
            : string.Equals(source.RenderProfile, SourceImageRasterProfile, StringComparison.OrdinalIgnoreCase)
            ? "+image"
            : "";
        string snap = source.SnapPointCount + source.SnapSegmentCount > 0 ? "+snap" : "";
        return AppendCachedDpiSummary($"Raster {scale}{profile}{snap}", cachedDpis);
    }

    public static string CachedReadableDpiSummary(
        PageInfo page,
        IReadOnlyDictionary<string, IReadOnlyList<int>>? readyDpisByPageFolder = null)
    {
        IReadOnlyList<int> ready = ReadyReadableRasterDpis(page, readyDpisByPageFolder);
        return ready.Count == 0
            ? ""
            : string.Join("/", ready.Select(dpi => dpi.ToString(CultureInfo.InvariantCulture)));
    }

    public static int BestReadyReadableRasterDpi(PageInfo page)
    {
        IReadOnlyList<int> ready = ReadyReadableRasterDpis(page);
        return ready.Count == 0 ? 0 : ready[^1];
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<int>> ReadyReadableRasterDpisByPageFolder(IEnumerable<PageInfo> pages)
    {
        var snapshot = new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (PageInfo page in pages)
        {
            string key = PageFolderSnapshotKey(page.FolderPath);
            if (string.IsNullOrWhiteSpace(key) || snapshot.ContainsKey(key))
                continue;

            snapshot[key] = ReadyReadableRasterDpis(page);
        }

        return snapshot;
    }

    public static IReadOnlyList<int> ReadyReadableRasterDpis(
        PageInfo page,
        IReadOnlyDictionary<string, IReadOnlyList<int>>? readyDpisByPageFolder = null)
    {
        if (readyDpisByPageFolder != null &&
            readyDpisByPageFolder.TryGetValue(PageFolderSnapshotKey(page.FolderPath), out IReadOnlyList<int>? ready))
        {
            return ready;
        }

        return ReadyReadableRasterDpisFromDisk(page);
    }

    private static IReadOnlyList<int> ReadyReadableRasterDpisFromDisk(PageInfo page)
    {
        RasterSheetSource? source = page.RasterSheet;
        if (!CanTrustReadableRasterMetadata(page, source))
            return [];

        string rasterDir = Path.Combine(page.FolderPath, CacheFolderName);
        if (!Directory.Exists(rasterDir))
            return [];

        var ready = new List<int>();
        foreach (string imagePath in Directory.EnumerateFiles(rasterDir, "working-*dpi.*"))
        {
            if (TryParseWorkingImageDpi(Path.GetFileName(imagePath), out int dpi))
                ready.Add(dpi);
        }

        if (File.Exists(Path.Combine(rasterDir, WorkingImageName)))
            ready.Add(DefaultRasterDpi);

        return ready.Distinct().Order().ToList();
    }

    private static string PageFolderSnapshotKey(string pageFolder)
    {
        if (string.IsNullOrWhiteSpace(pageFolder))
            return "";

        try
        {
            return Path.GetFullPath(pageFolder.Trim());
        }
        catch
        {
            return pageFolder.Trim();
        }
    }

    private static void AddReferencedCachePath(string pageFolder, string relativePath, HashSet<string> keepPaths)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return;

        try
        {
            keepPaths.Add(ResolvePagePath(pageFolder, relativePath));
        }
        catch
        {
        }
    }

    private static bool IsCompactableCacheFile(string path)
    {
        string name = Path.GetFileName(path);
        return string.Equals(name, WorkingImageName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, CompactWorkingImageName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, OverviewImageName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, SnapIndexName, StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("working-", StringComparison.OrdinalIgnoreCase) &&
               (name.EndsWith("dpi.png", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("dpi.webp", StringComparison.OrdinalIgnoreCase));
    }

    private static void TryDeleteEmptyRasterDirectory(string rasterDir)
    {
        try
        {
            if (Directory.Exists(rasterDir) && !Directory.EnumerateFileSystemEntries(rasterDir).Any())
                Directory.Delete(rasterDir);
        }
        catch
        {
        }
    }

    private static string AppendCachedDpiSummary(string status, string cachedDpis) =>
        string.IsNullOrWhiteSpace(cachedDpis)
            ? status
            : $"{status} | ready {cachedDpis}";

    public static float RasterDpiToRenderScale(int dpi) =>
        Math.Clamp(dpi, 72, MaxRasterDpi) / 72f;

    public static int RenderScaleToDpi(double renderScale)
    {
        if (double.IsNaN(renderScale) || double.IsInfinity(renderScale) || renderScale <= 0)
            return 0;

        return Math.Max(1, (int)Math.Round(renderScale * 72.0, MidpointRounding.AwayFromZero));
    }

    public static string WorkingImageNameForRenderScale(double renderScale) =>
        $"working-{Math.Clamp(RenderScaleToDpi(renderScale), 1, MaxRasterDpi).ToString(CultureInfo.InvariantCulture)}dpi.webp";

    private static string LegacyWorkingImageNameForRenderScale(double renderScale) =>
        $"working-{Math.Clamp(RenderScaleToDpi(renderScale), 1, MaxRasterDpi).ToString(CultureInfo.InvariantCulture)}dpi.png";

    public static bool IsSourceImageRasterProfile(RasterSheetSource? source) =>
        source != null &&
        string.Equals(source.RenderProfile, SourceImageRasterProfile, StringComparison.OrdinalIgnoreCase);

    public static bool ShouldRebuildForReadableDisplay(
        string pageFolder,
        string pdfPath,
        RasterSheetSource? source,
        out string reason)
    {
        reason = "";
        if (source?.Enabled != true)
            return false;
        if (string.IsNullOrWhiteSpace(source.Image))
        {
            reason = "image path is empty";
            return true;
        }
        if (source.WidthPt <= 0 || source.HeightPt <= 0)
        {
            reason = "page size is invalid";
            return true;
        }
        if (IsStale(pdfPath, source))
        {
            reason = "source PDF changed";
            return true;
        }
        if (IsLegacyLineBoost(source))
        {
            reason = "legacy lineboost raster cache";
            return true;
        }

        string imagePath = ResolveImagePath(pageFolder, source);
        if (!File.Exists(imagePath))
        {
            reason = "image file is missing";
            return true;
        }

        return false;
    }

    public static bool IsSourceImageRaster(RasterSheetSource? source) =>
        source?.Enabled == true &&
        string.Equals(source.RenderProfile, SourceImageRasterProfile, StringComparison.OrdinalIgnoreCase);

    public static bool ShouldUseSourceImageRasterForFastOpen(RasterSheetSource? source)
    {
        if (!IsSourceImageRaster(source) || source!.WidthPt <= 0 || source.HeightPt <= 0 || source.RenderScale <= 0)
            return false;

        double estimatedPixels = EstimateSourceImagePixels(source);
        return IsValidPixelEstimate(estimatedPixels) &&
               estimatedPixels <= SourceImageFastOpenMaxPixels;
    }

    public static bool NeedsSourceImageOverview(RasterSheetSource? source)
    {
        if (!IsSourceImageRaster(source) || source!.WidthPt <= 0 || source.HeightPt <= 0 || source.RenderScale <= 0)
            return false;

        double estimatedPixels = EstimateSourceImagePixels(source);
        return IsValidPixelEstimate(estimatedPixels) &&
               estimatedPixels > SourceImageOverviewMaxPixels;
    }

    public static bool HasSourceImageOverview(RasterSheetSource? source) =>
        IsSourceImageRaster(source) &&
        !string.IsNullOrWhiteSpace(source!.OverviewImage) &&
        source.OverviewRenderScale > 0;

    public static bool ShouldBuildSourceImageOverview(
        string pageFolder,
        string pdfPath,
        RasterSheetSource? source,
        out string reason)
    {
        reason = "";
        if (!IsSourceImageRaster(source))
            return false;
        if (source!.WidthPt <= 0 || source.HeightPt <= 0 || source.RenderScale <= 0)
            return false;
        if (IsStale(pdfPath, source))
            return false;

        string imagePath = ResolveImagePath(pageFolder, source);
        if (!File.Exists(imagePath))
            return false;

        if (!NeedsSourceImageOverview(source))
        {
            return false;
        }

        if (!HasSourceImageOverview(source))
        {
            reason = "source image overview missing";
            return true;
        }

        string overviewPath = ResolvePagePath(pageFolder, source.OverviewImage);
        if (!File.Exists(overviewPath))
        {
            reason = "overview image file is missing";
            return true;
        }

        if (ShouldUpgradeSourceImageOverviewQuality(source))
        {
            reason = "source image overview below current quality";
            return true;
        }

        return false;
    }

    public static bool TryReadSnapIndex(
        string pageFolder,
        string pdfPath,
        RasterSheetSource? source,
        out PdfGeometrySnapResult result,
        out string reason)
    {
        result = new PdfGeometrySnapResult();
        reason = "";
        if (source == null || string.IsNullOrWhiteSpace(source.SnapIndex))
        {
            reason = "snap index is missing";
            return false;
        }
        if (IsStale(pdfPath, source))
        {
            reason = "source PDF changed";
            return false;
        }

        string snapPath = ResolvePagePath(pageFolder, source.SnapIndex);
        if (!File.Exists(snapPath))
        {
            reason = "snap index file is missing";
            return false;
        }

        try
        {
            RasterSheetSnapFile? file = JsonSerializer.Deserialize<RasterSheetSnapFile>(
                File.ReadAllText(snapPath),
                OurPlaneCoreJobStore.JsonOptions);
            if (file == null || !file.Ok)
            {
                reason = file?.Error ?? "snap index file is invalid";
                return false;
            }

            result = new PdfGeometrySnapResult
            {
                Points = file.Points
                    .Where(point => IsFinite(point.X) && IsFinite(point.Y))
                    .Select(point => new PdfGeometrySnapPoint(
                        new SKPoint(point.X, point.Y),
                        string.IsNullOrWhiteSpace(point.Kind) ? "pdf-point" : point.Kind,
                        point.LayerName ?? ""))
                    .ToList(),
                Segments = file.Segments
                    .Where(segment =>
                        IsFinite(segment.X0) &&
                        IsFinite(segment.Y0) &&
                        IsFinite(segment.X1) &&
                        IsFinite(segment.Y1))
                    .Select(segment => new PdfGeometrySnapSegment(
                        new SKPoint(segment.X0, segment.Y0),
                        new SKPoint(segment.X1, segment.Y1),
                        string.IsNullOrWhiteSpace(segment.Kind) ? "pdf-line" : segment.Kind,
                        segment.LayerName ?? "",
                        Math.Max(0f, segment.StrokeWidth)))
                    .ToList(),
            };
            if (result.Points.Count + result.Segments.Count == 0)
            {
                reason = "snap index contains no geometry";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            AppLog.Warn(ex, $"Raster sheet snap index read failed for {snapPath}");
            return false;
        }
    }

    public static bool TryReadReady(
        string pageFolder,
        string pdfPath,
        RasterSheetSource? source,
        out RasterSheetBitmapResult result,
        out string reason)
    {
        result = new RasterSheetBitmapResult(new SKBitmap(), 0, 0, 0, "");
        reason = "";
        if (source == null)
        {
            reason = "not configured";
            return false;
        }
        if (!source.Enabled)
        {
            reason = "disabled";
            return false;
        }
        if (string.IsNullOrWhiteSpace(source.Image))
        {
            reason = "image path is empty";
            return false;
        }
        if (source.WidthPt <= 0 || source.HeightPt <= 0)
        {
            reason = "page size is invalid";
            return false;
        }
        if (IsStale(pdfPath, source))
        {
            reason = "source PDF changed";
            return false;
        }
        if (IsLegacyLineBoost(source))
        {
            reason = "legacy lineboost raster cache";
            return false;
        }

        string imagePath = ResolveImagePath(pageFolder, source);
        if (!File.Exists(imagePath))
        {
            reason = "image file is missing";
            return false;
        }

        SKBitmap? bitmap = SKBitmap.Decode(imagePath);
        if (bitmap == null)
        {
            reason = "image file could not be decoded";
            return false;
        }

        float widthPt = (float)source.WidthPt;
        float heightPt = (float)source.HeightPt;
        float bitmapScale = widthPt > 0 ? bitmap.Width / widthPt : (float)source.RenderScale;
        result = new RasterSheetBitmapResult(bitmap, widthPt, heightPt, bitmapScale, imagePath);
        return true;
    }

    public static bool TryReadOverviewReady(
        string pageFolder,
        string pdfPath,
        RasterSheetSource? source,
        out RasterSheetBitmapResult result,
        out string reason)
    {
        result = new RasterSheetBitmapResult(new SKBitmap(), 0, 0, 0, "");
        reason = "";
        if (!HasSourceImageOverview(source))
        {
            reason = "overview image is missing";
            return false;
        }
        if (source!.WidthPt <= 0 || source.HeightPt <= 0)
        {
            reason = "page size is invalid";
            return false;
        }
        if (IsStale(pdfPath, source))
        {
            reason = "source PDF changed";
            return false;
        }

        string imagePath = ResolvePagePath(pageFolder, source.OverviewImage);
        if (!File.Exists(imagePath))
        {
            reason = "overview image file is missing";
            return false;
        }

        SKBitmap? bitmap = SKBitmap.Decode(imagePath);
        if (bitmap == null)
        {
            reason = "overview image file could not be decoded";
            return false;
        }

        float widthPt = (float)source.WidthPt;
        float heightPt = (float)source.HeightPt;
        float bitmapScale = widthPt > 0 ? bitmap.Width / widthPt : (float)source.OverviewRenderScale;
        result = new RasterSheetBitmapResult(bitmap, widthPt, heightPt, bitmapScale, imagePath);
        return true;
    }

    private static bool IsStale(string pdfPath, RasterSheetSource source)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            return false;

        var pdfInfo = new FileInfo(pdfPath);
        return source.PdfLength > 0 &&
               (source.PdfLength != pdfInfo.Length ||
                source.PdfLastWriteUtcTicks != pdfInfo.LastWriteTimeUtc.Ticks);
    }

    private static bool IsLegacyLineBoost(RasterSheetSource source) =>
        string.Equals(source.RenderProfile, ReadableLineBoostProfile, StringComparison.OrdinalIgnoreCase);

    private static bool CanTrustReadableRasterMetadata(PageInfo page, RasterSheetSource? source) =>
        source != null &&
        !string.IsNullOrWhiteSpace(page.FolderPath) &&
        !string.IsNullOrWhiteSpace(page.PdfPath) &&
        File.Exists(page.PdfPath) &&
        source.WidthPt > 0 &&
        source.HeightPt > 0 &&
        source.PdfLength > 0 &&
        source.PdfLastWriteUtcTicks > 0 &&
        string.Equals(source.RenderProfile, ReadableRasterProfile, StringComparison.OrdinalIgnoreCase) &&
        !IsStale(page.PdfPath, source);

    private static bool IsReusableReadableRaster(
        PageInfo page,
        RasterSheetSource? source,
        float renderScale,
        out string imagePath)
    {
        imagePath = "";
        if (source == null ||
            string.IsNullOrWhiteSpace(page.FolderPath) ||
            string.IsNullOrWhiteSpace(page.PdfPath) ||
            !File.Exists(page.PdfPath) ||
            source.WidthPt <= 0 ||
            source.HeightPt <= 0 ||
            source.PdfLength <= 0 ||
            source.PdfLastWriteUtcTicks <= 0 ||
            string.IsNullOrWhiteSpace(source.Image) ||
            !IsSupportedRasterImageFormat(source.Format, source.Image) ||
            !string.Equals(source.RenderProfile, ReadableRasterProfile, StringComparison.OrdinalIgnoreCase) ||
            IsStale(page.PdfPath, source) ||
            RenderScaleToDpi(source.RenderScale) != RenderScaleToDpi(renderScale))
        {
            return false;
        }

        imagePath = ResolveImagePath(page.FolderPath, source);
        return File.Exists(imagePath);
    }

    private static IEnumerable<string> WorkingImageCandidatesForRenderScale(float renderScale)
    {
        string primary = WorkingImageNameForRenderScale(renderScale);
        yield return primary;
        yield return LegacyWorkingImageNameForRenderScale(renderScale);

        if (RenderScaleToDpi(renderScale) == DefaultRasterDpi &&
            !string.Equals(primary, WorkingImageName, StringComparison.OrdinalIgnoreCase))
        {
            yield return WorkingImageName;
        }
    }

    private static bool TryParseWorkingImageDpi(string? fileName, out int dpi)
    {
        dpi = 0;
        const string prefix = "working-";
        if (string.IsNullOrWhiteSpace(fileName) ||
            !fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (string suffix in new[] { "dpi.webp", "dpi.png" })
        {
            if (!fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;

            string raw = fileName[prefix.Length..^suffix.Length];
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out dpi) &&
                   dpi > 0 &&
                   dpi <= MaxRasterDpi;
        }

        return false;
    }

    private static bool HasReadySnapIndex(string pageFolder, RasterSheetSource source) =>
        source.SnapBlackOnly &&
        !string.IsNullOrWhiteSpace(source.SnapIndex) &&
        File.Exists(ResolvePagePath(pageFolder, source.SnapIndex));

    private static string ResolveImagePath(string pageFolder, RasterSheetSource source)
    {
        string image = source.Image.Trim();
        return ResolvePagePath(pageFolder, image);
    }

    private static bool TryEncodeLosslessWebp(SKBitmap bitmap, out byte[] bytes)
    {
        bytes = [];
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return false;

        using SKPixmap? pixmap = bitmap.PeekPixels();
        if (pixmap == null)
            return false;

        using SKData? data = pixmap.Encode(new SKWebpEncoderOptions(
            SKWebpEncoderCompression.Lossless,
            LosslessWebpCompressionEffort));
        if (data == null || data.Size <= 0)
            return false;

        bytes = data.ToArray();
        return bytes.Length > 0;
    }

    private static bool IsSupportedRasterImageFormat(string format, string imagePath)
    {
        string clean = (format ?? "").Trim().TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(clean))
            clean = RasterFormatForPath(imagePath);

        return string.Equals(clean, PngRasterFormat, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(clean, WebpRasterFormat, StringComparison.OrdinalIgnoreCase);
    }

    private static string RasterFormatForPath(string imagePath)
    {
        string extension = Path.GetExtension(imagePath).TrimStart('.').ToLowerInvariant();
        return string.Equals(extension, WebpRasterFormat, StringComparison.OrdinalIgnoreCase)
            ? WebpRasterFormat
            : PngRasterFormat;
    }

    private static bool TryWriteSourceImageOverview(
        SKBitmap source,
        string rasterDir,
        string pageFolder,
        double widthPt,
        out string overviewImage,
        out double overviewRenderScale,
        out string error)
    {
        overviewImage = "";
        overviewRenderScale = 0;
        error = "";

        long sourcePixels = (long)Math.Max(0, source.Width) * Math.Max(0, source.Height);
        if (sourcePixels <= 0 || sourcePixels <= SourceImageOverviewMaxPixels)
            return false;

        double resizeRatio = Math.Sqrt(SourceImageOverviewMaxPixels / (double)sourcePixels);
        int targetWidth = Math.Max(1, (int)Math.Floor(source.Width * resizeRatio));
        int targetHeight = Math.Max(1, (int)Math.Floor(source.Height * resizeRatio));
        while ((long)targetWidth * targetHeight > SourceImageOverviewMaxPixels)
        {
            if (targetWidth >= targetHeight && targetWidth > 1)
                targetWidth--;
            else if (targetHeight > 1)
                targetHeight--;
            else
                break;
        }

        try
        {
            using SKBitmap? overview = CreateSourceImageOverviewBitmap(source, targetWidth, targetHeight);
            if (overview == null)
            {
                error = "source image resize failed";
                return false;
            }

            using SKImage image = SKImage.FromBitmap(overview);
            using SKData? data = image.Encode(SKEncodedImageFormat.Png, 100);
            if (data == null || data.Size == 0)
            {
                error = "overview image could not be encoded as PNG";
                return false;
            }

            string imagePath = Path.Combine(rasterDir, OverviewImageName);
            string tempPath = imagePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (FileStream output = File.Create(tempPath))
                data.SaveTo(output);
            File.Move(tempPath, imagePath, overwrite: true);

            overviewImage = Path.GetRelativePath(pageFolder, imagePath);
            overviewRenderScale = widthPt > 0 ? overview.Width / widthPt : 0;
            return overviewRenderScale > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static SKBitmap? CreateSourceImageOverviewBitmap(SKBitmap source, int targetWidth, int targetHeight)
    {
        using SKBitmap? resized = source.Resize(
            new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul),
            SKFilterQuality.High);
        if (resized == null)
            return null;

        using SKColorFilter? contrast = SKColorFilter.CreateHighContrast(
            grayscale: false,
            invertStyle: SKHighContrastConfigInvertStyle.NoInvert,
            contrast: 0.28f);
        if (contrast == null)
            return resized.Copy();

        var enhanced = new SKBitmap(
            new SKImageInfo(resized.Width, resized.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(enhanced);
        using var paint = new SKPaint
        {
            ColorFilter = contrast,
            FilterQuality = SKFilterQuality.None,
            IsAntialias = false,
        };
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(resized, 0, 0, paint);
        return enhanced;
    }

    private static double EstimateSourceImagePixels(RasterSheetSource source) =>
        source.WidthPt * source.HeightPt * source.RenderScale * source.RenderScale;

    private static bool ShouldUpgradeSourceImageOverviewQuality(RasterSheetSource source)
    {
        double targetScale = TargetSourceImageOverviewRenderScale(source);
        return targetScale > 0 &&
               source.OverviewRenderScale > 0 &&
               source.OverviewRenderScale < targetScale * SourceImageOverviewScaleTolerance;
    }

    private static double TargetSourceImageOverviewRenderScale(RasterSheetSource source)
    {
        double estimatedPixels = EstimateSourceImagePixels(source);
        if (!IsValidPixelEstimate(estimatedPixels) || source.RenderScale <= 0)
            return 0;

        double resizeRatio = estimatedPixels > SourceImageOverviewMaxPixels
            ? Math.Sqrt(SourceImageOverviewMaxPixels / estimatedPixels)
            : 1.0;
        return source.RenderScale * resizeRatio;
    }

    private static bool IsValidPixelEstimate(double estimatedPixels) =>
        estimatedPixels > 0 &&
        !double.IsInfinity(estimatedPixels) &&
        !double.IsNaN(estimatedPixels);

    private static void TryWriteSnapIndex(
        PageInfo page,
        string rasterDir,
        RasterSheetSource source,
        out string error)
    {
        error = "";
        if (!PdfGeometrySnapService.TryReadSnapPoints(
                page.PdfPath,
                page.PdfPage,
                page.PdfLayersCached ? page.PdfLayers : null,
                blackOnly: true,
                out PdfGeometrySnapResult snap,
                out error))
        {
            return;
        }

        if (snap.Points.Count + snap.Segments.Count == 0)
        {
            error = string.IsNullOrWhiteSpace(error)
                ? "No strict black vector linework was found for snap."
                : error;
        }

        string snapPath = Path.Combine(rasterDir, SnapIndexName);
        var file = new RasterSheetSnapFile
        {
            Ok = true,
            GeneratedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            BlackOnly = true,
            Points = snap.Points
                .Select(point => new RasterSheetSnapPoint
                {
                    X = point.Point.X,
                    Y = point.Point.Y,
                    Kind = point.Kind,
                    LayerName = point.LayerName,
                })
                .ToList(),
            Segments = snap.Segments
                .Select(segment => new RasterSheetSnapSegment
                {
                    X0 = segment.Start.X,
                    Y0 = segment.Start.Y,
                    X1 = segment.End.X,
                    Y1 = segment.End.Y,
                    Kind = segment.Kind,
                    LayerName = segment.LayerName,
                    StrokeWidth = segment.StrokeWidth,
                })
                .ToList(),
        };

        string tempPath = snapPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(file, OurPlaneCoreJobStore.JsonOptions));
        File.Move(tempPath, snapPath, overwrite: true);

        source.SnapIndex = Path.GetRelativePath(page.FolderPath, snapPath);
        source.SnapBlackOnly = true;
        source.SnapPointCount = file.Points.Count;
        source.SnapSegmentCount = file.Segments.Count;
        source.SnapGeneratedAtUtc = file.GeneratedAtUtc;
    }

    private static string ResolvePagePath(string pageFolder, string path)
    {
        string clean = path.Trim();
        return Path.IsPathRooted(clean)
            ? Path.GetFullPath(clean)
            : Path.GetFullPath(Path.Combine(pageFolder, clean));
    }

    private static bool IsFinite(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value);

    private sealed class RasterSheetSnapFile
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = "";
        public bool BlackOnly { get; set; }
        public string GeneratedAtUtc { get; set; } = "";
        public List<RasterSheetSnapPoint> Points { get; set; } = [];
        public List<RasterSheetSnapSegment> Segments { get; set; } = [];
    }

    private sealed class RasterSheetSnapPoint
    {
        public float X { get; set; }
        public float Y { get; set; }
        public string Kind { get; set; } = "";
        public string LayerName { get; set; } = "";
    }

    private sealed class RasterSheetSnapSegment
    {
        public float X0 { get; set; }
        public float Y0 { get; set; }
        public float X1 { get; set; }
        public float Y1 { get; set; }
        public string Kind { get; set; } = "";
        public string LayerName { get; set; } = "";
        [JsonPropertyName("stroke_width")]
        public float StrokeWidth { get; set; }
    }
}
