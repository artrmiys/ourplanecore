using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace OurPlaneCore;

public sealed class PdfSheetMetadataCropTemplate
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("source_page_name")]
    public string SourcePageName { get; set; } = "";

    [JsonPropertyName("source_page_folder")]
    public string SourcePageFolder { get; set; } = "";

    [JsonPropertyName("page_width_pt")]
    public float PageWidthPt { get; set; }

    [JsonPropertyName("page_height_pt")]
    public float PageHeightPt { get; set; }

    [JsonPropertyName("sheet_number_rect")]
    public PdfSheetMetadataCropRegion SheetNumberRect { get; set; } = new();

    [JsonPropertyName("scale_rect")]
    public PdfSheetMetadataCropRegion ScaleRect { get; set; } = new();

    [JsonPropertyName("created_at_utc")]
    public string CreatedAtUtc { get; set; } = "";

    [JsonPropertyName("updated_at_utc")]
    public string UpdatedAtUtc { get; set; } = "";
}

public sealed class PdfSheetMetadataCropRegion
{
    [JsonPropertyName("left")]
    public float Left { get; set; }

    [JsonPropertyName("top")]
    public float Top { get; set; }

    [JsonPropertyName("right")]
    public float Right { get; set; }

    [JsonPropertyName("bottom")]
    public float Bottom { get; set; }
}

public sealed record PdfSheetMetadataSavedCrop(string Role, string Path, SKRect PdfRect);

public static class PdfSheetMetadataCropService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static bool TryRenderPage(
        PageInfo page,
        out PdfLayerRenderResult render,
        out string error)
    {
        return PdfLayerRenderService.TryRender(
            page.PdfPath,
            page.PdfPage,
            1.50,
            new Dictionary<int, bool>(),
            [],
            page.PdfLayersCached ? page.PdfLayers : null,
            out render,
            out error);
    }

    public static string TemplatePath(OurPlaneCoreJob job) =>
        Path.Combine(job.AIContextRoot, "sheet_metadata_crop_template.json");

    public static PdfSheetMetadataCropTemplate? LoadTemplate(OurPlaneCoreJob job)
    {
        string path = TemplatePath(job);
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<PdfSheetMetadataCropTemplate>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Sheet metadata crop template could not be read from {path}");
            return null;
        }
    }

    public static void SaveTemplate(OurPlaneCoreJob job, PdfSheetMetadataCropTemplate template)
    {
        string now = DateTime.UtcNow.ToString("O");
        template.SchemaVersion = 1;
        if (string.IsNullOrWhiteSpace(template.CreatedAtUtc))
            template.CreatedAtUtc = now;
        template.UpdatedAtUtc = now;

        string path = TemplatePath(job);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? job.AIContextRoot);
        try
        {
            IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(template, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(path)}': {ex.Message}", ex);
        }
    }

    public static bool HasUsableTemplate(PdfSheetMetadataCropTemplate? template) =>
        template != null &&
        (IsUsableRegion(template.SheetNumberRect) || IsUsableRegion(template.ScaleRect));

    public static PdfSheetMetadataCropRegion RegionFromRect(SKRect rect) =>
        new()
        {
            Left = Math.Min(rect.Left, rect.Right),
            Top = Math.Min(rect.Top, rect.Bottom),
            Right = Math.Max(rect.Left, rect.Right),
            Bottom = Math.Max(rect.Top, rect.Bottom),
        };

    public static SKRect RectFromRegion(PdfSheetMetadataCropRegion region) =>
        new(
            Math.Min(region.Left, region.Right),
            Math.Min(region.Top, region.Bottom),
            Math.Max(region.Left, region.Right),
            Math.Max(region.Top, region.Bottom));

    public static bool TrySaveManualFallbackCrops(
        PageInfo page,
        PdfSheetMetadataCropTemplate template,
        string cropsRoot,
        string filePrefix,
        out IReadOnlyList<PdfSheetMetadataSavedCrop> crops,
        out string error)
    {
        crops = [];
        error = "";

        if (!HasUsableTemplate(template))
        {
            error = "Sheet metadata crop template has no usable regions.";
            return false;
        }

        if (!TryRenderPage(page, out PdfLayerRenderResult render, out error))
            return false;

        var saved = new List<PdfSheetMetadataSavedCrop>();
        if (IsUsableRegion(template.SheetNumberRect) &&
            TrySaveNamedCrop(render, RectFromRegion(template.SheetNumberRect), cropsRoot, filePrefix, "sheet_number", saved, out error) == false)
        {
            return false;
        }

        if (IsUsableRegion(template.ScaleRect) &&
            TrySaveNamedCrop(render, RectFromRegion(template.ScaleRect), cropsRoot, filePrefix, "scale", saved, out error) == false)
        {
            return false;
        }

        var titleBlockRect = new SKRect(0, render.HeightPt * 0.58f, render.WidthPt, render.HeightPt);
        if (TrySaveNamedCrop(render, titleBlockRect, cropsRoot, filePrefix, "title_block", saved, out _))
        {
            crops = saved;
            return true;
        }

        if (saved.Count == 0)
        {
            error = string.IsNullOrWhiteSpace(error) ? "No metadata fallback crops could be saved." : error;
            return false;
        }

        crops = saved;
        return true;
    }

    public static bool TrySaveTitleBlockCrop(
        PageInfo page,
        string outputPath,
        out SKRect cropPdfRect,
        out string error)
    {
        cropPdfRect = SKRect.Empty;
        if (!TryRenderPage(page, out PdfLayerRenderResult render, out error))
            return false;

        var requested = new SKRect(0, render.HeightPt * 0.58f, render.WidthPt, render.HeightPt);
        return TrySaveCrop(render, requested, outputPath, out cropPdfRect, out error);
    }

    public static bool TrySaveCrop(
        PdfLayerRenderResult render,
        SKRect requestedPdfRect,
        string outputPath,
        out SKRect cropPdfRect,
        out string error)
    {
        cropPdfRect = SKRect.Empty;
        error = "";

        using SKBitmap? bitmap = SKBitmap.Decode(render.ImageBytes);
        if (bitmap == null)
        {
            error = "Rendered PDF image could not be decoded.";
            return false;
        }

        if (render.WidthPt <= 0 || render.HeightPt <= 0)
        {
            error = "Rendered PDF page size is invalid.";
            return false;
        }

        float left = Math.Clamp(Math.Min(requestedPdfRect.Left, requestedPdfRect.Right), 0, render.WidthPt);
        float top = Math.Clamp(Math.Min(requestedPdfRect.Top, requestedPdfRect.Bottom), 0, render.HeightPt);
        float right = Math.Clamp(Math.Max(requestedPdfRect.Left, requestedPdfRect.Right), 0, render.WidthPt);
        float bottom = Math.Clamp(Math.Max(requestedPdfRect.Top, requestedPdfRect.Bottom), 0, render.HeightPt);
        if (right - left < 1 || bottom - top < 1)
        {
            error = "Requested crop is outside the PDF page.";
            return false;
        }

        float scaleX = bitmap.Width / render.WidthPt;
        float scaleY = bitmap.Height / render.HeightPt;
        int srcLeft = Math.Clamp((int)Math.Floor(left * scaleX), 0, bitmap.Width - 1);
        int srcTop = Math.Clamp((int)Math.Floor(top * scaleY), 0, bitmap.Height - 1);
        int srcRight = Math.Clamp((int)Math.Ceiling(right * scaleX), srcLeft + 1, bitmap.Width);
        int srcBottom = Math.Clamp((int)Math.Ceiling(bottom * scaleY), srcTop + 1, bitmap.Height);
        int cropWidth = srcRight - srcLeft;
        int cropHeight = srcBottom - srcTop;
        if (cropWidth <= 0 || cropHeight <= 0)
        {
            error = "Requested crop is too small.";
            return false;
        }

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var crop = new SKBitmap(cropWidth, cropHeight);
        using (var canvas = new SKCanvas(crop))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(
                bitmap,
                new SKRectI(srcLeft, srcTop, srcRight, srcBottom),
                new SKRect(0, 0, cropWidth, cropHeight));
        }

        using SKData data = crop.Encode(SKEncodedImageFormat.Png, 95);
        using FileStream stream = File.Create(outputPath);
        data.SaveTo(stream);

        cropPdfRect = new SKRect(
            srcLeft / scaleX,
            srcTop / scaleY,
            srcRight / scaleX,
            srcBottom / scaleY);
        return true;
    }

    private static bool TrySaveNamedCrop(
        PdfLayerRenderResult render,
        SKRect requestedPdfRect,
        string cropsRoot,
        string filePrefix,
        string role,
        List<PdfSheetMetadataSavedCrop> saved,
        out string error)
    {
        string path = Path.Combine(cropsRoot, $"{filePrefix}_{role}.png");
        if (!TrySaveCrop(render, requestedPdfRect, path, out SKRect rect, out error))
            return false;

        saved.Add(new PdfSheetMetadataSavedCrop(role, path, rect));
        return true;
    }

    private static bool IsUsableRegion(PdfSheetMetadataCropRegion? region)
    {
        if (region == null)
            return false;

        float width = Math.Abs(region.Right - region.Left);
        float height = Math.Abs(region.Bottom - region.Top);
        return width >= 1 && height >= 1 &&
               new[] { region.Left, region.Top, region.Right, region.Bottom }.All(float.IsFinite);
    }
}
