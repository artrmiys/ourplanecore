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
    string Error);

public sealed record RasterSheetBitmapResult(
    SKBitmap Bitmap,
    float WidthPt,
    float HeightPt,
    float BitmapScale,
    string ImagePath);

public static class RasterSheetCacheService
{
    public const float DefaultRenderScale = 200f / 72f;
    public const string CacheFolderName = "raster";
    public const string WorkingImageName = "working.png";
    public const string SnapIndexName = "snap.json";
    public const string ReadableRasterProfile = "readable-raster-v2";
    public const string SourceImageRasterProfile = "source-image-v1";
    public const string ReadableLineBoostProfile = "lineboost-v1";
    public const long SourceImageFastOpenMaxPixels = 18_000_000;

    public static RasterSheetBuildResult BuildAndEnable(PageInfo page, float renderScale = DefaultRenderScale)
    {
        if (string.IsNullOrWhiteSpace(page.FolderPath) || !Directory.Exists(page.FolderPath))
            return Failed("Page folder is missing.");
        if (string.IsNullOrWhiteSpace(page.PdfPath) || !File.Exists(page.PdfPath))
            return Failed($"Source PDF is missing: {page.PdfPath}");
        if (page.PdfPage < 0)
            return Failed("PDF page index is invalid.");

        float scale = Math.Clamp(renderScale, 0.35f, 4.0f);
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

        byte[] workingImageBytes = render.ImageBytes;

        string rasterDir = Path.Combine(page.FolderPath, CacheFolderName);
        Directory.CreateDirectory(rasterDir);
        string imagePath = Path.Combine(rasterDir, WorkingImageName);
        string tempPath = imagePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(tempPath, workingImageBytes);
        File.Move(tempPath, imagePath, overwrite: true);

        var pdfInfo = new FileInfo(page.PdfPath);
        var source = new RasterSheetSource
        {
            Enabled = true,
            Image = Path.GetRelativePath(page.FolderPath, imagePath),
            Format = "png",
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
        using SKImage image = SKImage.FromBitmap(decoded);
        using SKData? data = image.Encode(SKEncodedImageFormat.Png, 100);
        if (data == null || data.Size == 0)
            return Failed("Source image could not be encoded as PNG.");

        string rasterDir = Path.Combine(page.FolderPath, CacheFolderName);
        Directory.CreateDirectory(rasterDir);
        string imagePath = Path.Combine(rasterDir, WorkingImageName);
        string tempPath = imagePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (FileStream output = File.Create(tempPath))
            data.SaveTo(output);
        File.Move(tempPath, imagePath, overwrite: true);

        var pdfInfo = new FileInfo(page.PdfPath);
        var source = new RasterSheetSource
        {
            Enabled = true,
            Image = Path.GetRelativePath(page.FolderPath, imagePath),
            Format = "png",
            RenderProfile = SourceImageRasterProfile,
            RenderScale = widthPt > 0 ? decoded.Width / widthPt : 0,
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

    public static bool TrySetEnabled(PageInfo page, bool enabled, out string error)
    {
        error = "";
        RasterSheetSource? source = page.RasterSheet?.Clone();
        if (source == null || string.IsNullOrWhiteSpace(source.Image))
        {
            error = "No raster cache exists for this sheet.";
            return false;
        }

        source.Enabled = enabled;
        OurPlaneCoreJobStore.SavePageRasterSheet(page.FolderPath, source);
        return true;
    }

    public static string DisplayStatus(PageInfo page)
    {
        RasterSheetSource? source = page.RasterSheet;
        if (source == null || string.IsNullOrWhiteSpace(source.Image))
            return "PDF";

        string imagePath = ResolveImagePath(page.FolderPath, source);
        if (!File.Exists(imagePath))
            return source.Enabled ? "Raster missing" : "Raster off";
        if (!source.Enabled)
            return "Raster off";
        if (IsStale(page.PdfPath, source))
            return "Raster stale";
        if (IsLegacyLineBoost(source))
            return "Raster legacy";

        string scale = source.RenderScale > 0
            ? source.RenderScale.ToString("0.#", CultureInfo.InvariantCulture)
            : "?";
        string profile = string.Equals(source.RenderProfile, ReadableRasterProfile, StringComparison.OrdinalIgnoreCase)
            ? "+readable"
            : string.Equals(source.RenderProfile, SourceImageRasterProfile, StringComparison.OrdinalIgnoreCase)
            ? "+image"
            : "";
        string snap = source.SnapPointCount + source.SnapSegmentCount > 0 ? "+snap" : "";
        return $"Raster {scale}x{profile}{snap}";
    }

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

        double estimatedPixels = source.WidthPt * source.HeightPt * source.RenderScale * source.RenderScale;
        return estimatedPixels > 0 &&
               !double.IsInfinity(estimatedPixels) &&
               !double.IsNaN(estimatedPixels) &&
               estimatedPixels <= SourceImageFastOpenMaxPixels;
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

    private static string ResolveImagePath(string pageFolder, RasterSheetSource source)
    {
        string image = source.Image.Trim();
        return ResolvePagePath(pageFolder, image);
    }

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
