using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SkiaSharp;

namespace OurPlaneCore;

public static class JobThumbnailService
{
    private const int ThumbnailWidth = 240;
    private const int ThumbnailHeight = 160;
    private const double RenderScale = 0.30;

    public static string ThumbnailDirectory
    {
        get
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "OurPlaneCore", "thumbnails");
        }
    }

    public static string GetThumbnailPath(string jobPath) =>
        Path.Combine(ThumbnailDirectory, $"{HashJobPath(jobPath)}.png");

    public static string ExistingThumbnailPath(string jobPath)
    {
        string path = GetThumbnailPath(jobPath);
        return File.Exists(path) ? path : "";
    }

    public static bool TryCreateThumbnail(OurPlaneCoreJob job, out string thumbnailPath, out string error)
    {
        thumbnailPath = "";
        error = "";

        try
        {
            PageInfo? page = FindFirstRenderablePage(job.PagesRoot);
            if (page == null)
            {
                error = "No renderable PDF page found in job.";
                return false;
            }

            if (!PdfLayerRenderService.TryRender(
                    page.PdfPath,
                    page.PdfPage,
                    RenderScale,
                    new Dictionary<int, bool>(),
                    [],
                    page.PdfLayersCached ? page.PdfLayers : null,
                    out PdfLayerRenderResult render,
                    out error))
            {
                return false;
            }

            using SKBitmap? source = SKBitmap.Decode(render.ImageBytes);
            if (source == null)
            {
                error = "Rendered PDF image could not be decoded.";
                return false;
            }

            Directory.CreateDirectory(ThumbnailDirectory);
            thumbnailPath = GetThumbnailPath(job.RootPath);
            string tempPath = $"{thumbnailPath}.{Guid.NewGuid():N}.tmp";
            using var thumbnail = CreateFitThumbnail(source);
            using SKData data = thumbnail.Encode(SKEncodedImageFormat.Png, 92);
            using (FileStream stream = File.Create(tempPath))
                data.SaveTo(stream);

            if (File.Exists(thumbnailPath))
                File.Delete(thumbnailPath);
            File.Move(tempPath, thumbnailPath);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static SKBitmap CreateFitThumbnail(SKBitmap source)
    {
        var thumbnail = new SKBitmap(ThumbnailWidth, ThumbnailHeight);
        using var canvas = new SKCanvas(thumbnail);
        canvas.Clear(new SKColor(246, 247, 249));

        float scale = Math.Min(
            (ThumbnailWidth - 12f) / Math.Max(1, source.Width),
            (ThumbnailHeight - 12f) / Math.Max(1, source.Height));
        float width = source.Width * scale;
        float height = source.Height * scale;
        var dest = new SKRect(
            (ThumbnailWidth - width) / 2f,
            (ThumbnailHeight - height) / 2f,
            (ThumbnailWidth + width) / 2f,
            (ThumbnailHeight + height) / 2f);
        var sourceRect = new SKRect(0, 0, source.Width, source.Height);
        using var paint = new SKPaint
        {
            FilterQuality = SKFilterQuality.Medium,
            IsAntialias = true,
        };
        canvas.DrawBitmap(source, sourceRect, dest, paint);
        return thumbnail;
    }

    private static PageInfo? FindFirstRenderablePage(string folder)
    {
        if (!Directory.Exists(folder))
            return null;

        if (OurPlaneCoreJobStore.TryReadPage(folder) is { } page && File.Exists(page.PdfPath))
            return page;

        foreach (string child in OurPlaneCoreJobStore.GetOrderedChildDirectories(folder))
        {
            PageInfo? childPage = FindFirstRenderablePage(child);
            if (childPage != null)
                return childPage;
        }

        return null;
    }

    private static string HashJobPath(string jobPath)
    {
        string normalized = NormalizeJobPath(jobPath).ToUpperInvariant();
        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeJobPath(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
