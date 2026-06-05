using System;
using System.IO;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    public static bool WarmRasterSheetBitmapCache(PageInfo page, RasterSheetSource? rasterSheet = null)
    {
        RasterSheetSource? source = rasterSheet?.Clone() ?? page.RasterSheet?.Clone();
        if (source?.Enabled != true ||
            string.IsNullOrWhiteSpace(page.FolderPath) ||
            string.IsNullOrWhiteSpace(page.PdfPath) ||
            page.PdfPage < 0)
        {
            return false;
        }

        bool warmed = false;
        if (RasterSheetCacheService.HasSourceImageOverview(source))
        {
            warmed |= TryWarmRasterSheetBitmapCache(
                page.FolderPath,
                page.PdfPath,
                source,
                preferOverview: true,
                action: "warmed",
                logFailure: false);
        }

        if (!RasterSheetCacheService.IsSourceImageRaster(source) ||
            !RasterSheetCacheService.HasSourceImageOverview(source))
        {
            warmed |= TryWarmRasterSheetBitmapCache(
                page.FolderPath,
                page.PdfPath,
                source,
                preferOverview: false,
                action: "warmed",
                logFailure: true);
        }

        return warmed;
    }

    private static bool TryWarmRasterSheetBitmapCache(
        string pageFolder,
        string pdfPath,
        RasterSheetSource rasterSheet,
        bool preferOverview,
        string action,
        bool logFailure)
    {
        RasterSheetBitmapResult result = new(new SKBitmap(), 0, 0, 0, "");
        try
        {
            if (!TryBuildRasterSheetBitmapCacheKey(
                    pageFolder,
                    pdfPath,
                    rasterSheet,
                    preferOverview,
                    out string cacheKey,
                    out _))
            {
                return false;
            }

            if (RasterSheetBitmapCache.Contains(cacheKey))
                return true;

            string reason;
            bool ok = preferOverview
                ? RasterSheetCacheService.TryReadOverviewReady(pageFolder, pdfPath, rasterSheet, out result, out reason)
                : RasterSheetCacheService.TryReadReady(pageFolder, pdfPath, rasterSheet, out result, out reason);
            if (!ok)
            {
                if (logFailure)
                {
                    AppLog.Warn(
                        $"Viewport raster sheet bitmap warm skipped; mode='{(preferOverview ? "overview" : "full")}'; " +
                        $"reason='{reason}'; page='{pageFolder}'; pdf='{Path.GetFileName(pdfPath)}'");
                }

                return false;
            }

            if (!TryPutRasterSheetBitmapCache(pageFolder, pdfPath, rasterSheet, preferOverview, result))
                return false;

            AppLog.Info(
                $"Viewport raster sheet bitmap {action}; mode='{(preferOverview ? "overview" : "full")}'; " +
                $"page='{pageFolder}'; pdf='{Path.GetFileName(pdfPath)}'; scale={result.BitmapScale:0.###}; image='{result.ImagePath}'");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport raster sheet bitmap warm failed for {pageFolder}");
            return false;
        }
        finally
        {
            result.Bitmap.Dispose();
        }
    }
}
