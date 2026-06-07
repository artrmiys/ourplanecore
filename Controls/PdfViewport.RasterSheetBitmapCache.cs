using System;
using System.IO;
using System.Threading.Tasks;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private bool QueueRasterSheetBitmapApplyAfterWarmup(
        string pdfPath,
        int pageIndex,
        string pageFolder,
        RasterSheetSource? rasterSheet,
        bool preferOverview)
    {
        if (rasterSheet?.Enabled != true ||
            string.IsNullOrWhiteSpace(pageFolder) ||
            string.IsNullOrWhiteSpace(pdfPath) ||
            pageIndex < 0)
        {
            return false;
        }

        string warmKey;
        try
        {
            warmKey = $"{RasterSheetRebuildKey(pdfPath, pageIndex, pageFolder)}|bitmap:{(preferOverview ? "overview" : "full")}";
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport raster sheet bitmap warmup skipped for {pageFolder}");
            return false;
        }

        lock (_rasterSheetRebuildGate)
        {
            if (!_rasterSheetRebuildsInFlight.Add(warmKey))
                return true;
        }

        PageInfo page = new()
        {
            Name = string.IsNullOrWhiteSpace(pageFolder) ? $"Page {pageIndex + 1}" : Path.GetFileName(pageFolder),
            FolderPath = pageFolder,
            PdfPath = pdfPath,
            PdfPage = pageIndex,
            PdfLayersCached = _cachedLayers != null,
            PdfLayers = _cachedLayers ?? Array.Empty<PdfLayerInfo>(),
            RasterSheet = rasterSheet.Clone(),
        };

        _ = WarmRasterSheetBitmapAndApplyAsync(warmKey, page, rasterSheet.Clone(), preferOverview);
        return true;
    }

    private async Task WarmRasterSheetBitmapAndApplyAsync(
        string warmKey,
        PageInfo queuedPage,
        RasterSheetSource rasterSheet,
        bool preferOverview)
    {
        try
        {
            bool warmed = await Task.Run(() => WarmRequestedRasterSheetBitmapCache(queuedPage, rasterSheet, preferOverview))
                .ConfigureAwait(false);
            if (!warmed)
                return;

            await Dispatcher.InvokeAsync(() =>
            {
                if (!IsCurrentPageRasterTarget(queuedPage.PdfPath, queuedPage.PdfPage, queuedPage.FolderPath) ||
                    _pdfLayersLoadedForPage ||
                    _usingLayerRenderer)
                {
                    return;
                }

                bool applyAtCurrentView = preferOverview
                    ? !ShouldUseRasterSheetForCurrentZoom()
                    : ShouldUseRasterSheetForCurrentZoom() ||
                      RasterSheetCacheService.IsSourceImageRaster(rasterSheet) &&
                      RasterSheetCacheService.ShouldUseSourceImageRasterForFastOpen(rasterSheet);
                if (!applyAtCurrentView)
                    return;

                if (!preferOverview && ShouldUseResponsiveRasterSheetDpiForCurrentZoom(rasterSheet))
                {
                    TryApplyResponsiveRasterSheetDpiForCurrentZoom();
                    return;
                }

                ViewState applyView = CaptureViewState();
                if (TryApplyRasterSheetRender(
                        queuedPage.PdfPath,
                        queuedPage.PdfPage,
                        queuedPage.FolderPath,
                        rasterSheet,
                        applyView,
                        fitAfter: false,
                        preferOverview,
                        requireCachedBitmap: true,
                        out string applyReason))
                {
                    PostStatus($"{(preferOverview ? "Raster overview" : "Raster sheet")}: {Path.GetFileName(queuedPage.PdfPath)}  page {queuedPage.PdfPage + 1}");
                    RequestRepaint();
                    return;
                }

                if (!IsRasterSheetBitmapCacheWarmingReason(applyReason))
                {
                    QueueRasterSheetSelfHealIfNeeded(
                        queuedPage.PdfPath,
                        queuedPage.PdfPage,
                        queuedPage.FolderPath,
                        queuedPage.PdfLayers,
                        rasterSheet,
                        applyReason);
                }
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport raster sheet bitmap warmup crashed for {queuedPage.Name}");
        }
        finally
        {
            lock (_rasterSheetRebuildGate)
                _rasterSheetRebuildsInFlight.Remove(warmKey);
        }
    }

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

    private static bool WarmRequestedRasterSheetBitmapCache(
        PageInfo page,
        RasterSheetSource rasterSheet,
        bool preferOverview)
    {
        RasterSheetSource source = rasterSheet.Clone();
        if (source.Enabled != true ||
            string.IsNullOrWhiteSpace(page.FolderPath) ||
            string.IsNullOrWhiteSpace(page.PdfPath) ||
            page.PdfPage < 0)
        {
            return false;
        }

        return TryWarmRasterSheetBitmapCache(
            page.FolderPath,
            page.PdfPath,
            source,
            preferOverview,
            action: "warmed",
            logFailure: true);
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
