using System;
using System.IO;
using System.Threading.Tasks;
using SkiaSharp;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    private bool QueueRasterSheetBitmapApplyAfterWarmup(
        string pdfPath,
        int pageIndex,
        string pageFolder,
        RasterSheetSource? rasterSheet,
        bool preferOverview,
        bool allowLowZoomFullRaster = false)
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

        _ = WarmRasterSheetBitmapAndApplyAsync(
            warmKey,
            page,
            rasterSheet.Clone(),
            preferOverview,
            allowLowZoomFullRaster);
        return true;
    }

    private async Task WarmRasterSheetBitmapAndApplyAsync(
        string warmKey,
        PageInfo queuedPage,
        RasterSheetSource rasterSheet,
        bool preferOverview,
        bool allowLowZoomFullRaster)
    {
        RasterSheetBitmapResult preparedBitmap = new(new SKBitmap(), 0, 0, 0, "");
        bool hasPreparedBitmap = false;
        bool preparedBitmapApplied = false;
        try
        {
            hasPreparedBitmap = await Task.Run(() => TryPrepareRasterSheetBitmapForUiApply(
                    queuedPage,
                    rasterSheet,
                    preferOverview,
                    out preparedBitmap))
                .ConfigureAwait(false);
            if (!hasPreparedBitmap)
            {
                await Dispatcher.InvokeAsync(() =>
                    RecoverPageOpenAfterRasterWarmupMiss(queuedPage, "bitmap-prepare-failed"));
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (!IsCurrentPageRasterTarget(queuedPage.PdfPath, queuedPage.PdfPage, queuedPage.FolderPath) ||
                    _pdfLayersLoadedForPage ||
                    _usingLayerRenderer)
                {
                    return;
                }

                if (!ShouldApplyWarmedRasterSheetBitmap(rasterSheet, preferOverview, allowLowZoomFullRaster))
                {
                    RecoverPageOpenAfterRasterWarmupMiss(queuedPage, "apply-conditions-changed");
                    return;
                }

                if (!preferOverview && ShouldUseResponsiveRasterSheetDpiForCurrentZoom(rasterSheet))
                {
                    if (!TryApplyResponsiveRasterSheetDpiForCurrentZoom())
                        RecoverPageOpenAfterRasterWarmupMiss(queuedPage, "responsive-dpi-unavailable");
                    return;
                }

                ViewState applyView = CaptureViewState();
                if (ApplyPreparedRasterSheetBitmap(
                        queuedPage.PdfPath,
                        queuedPage.PdfPage,
                        queuedPage.FolderPath,
                        rasterSheet,
                        preparedBitmap,
                        applyView,
                        fitAfter: false,
                        preferOverview))
                {
                    preparedBitmapApplied = true;
                    PostStatus($"{(preferOverview ? "Raster overview" : "Raster sheet")}: {Path.GetFileName(queuedPage.PdfPath)}  page {queuedPage.PdfPage + 1}");
                    RequestRepaint();
                    return;
                }

                RecoverPageOpenAfterRasterWarmupMiss(queuedPage, "bitmap-apply-rejected");
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport raster sheet bitmap warmup crashed for {queuedPage.Name}");
            try
            {
                await Dispatcher.InvokeAsync(() =>
                    RecoverPageOpenAfterRasterWarmupMiss(queuedPage, "warmup-crashed"));
            }
            catch
            {
                // Dispatcher may be shutting down; the veil recovery is best-effort.
            }
        }
        finally
        {
            if (!preparedBitmapApplied)
                preparedBitmap.Bitmap.Dispose();

            lock (_rasterSheetRebuildGate)
                _rasterSheetRebuildsInFlight.Remove(warmKey);
        }
    }

    // A page open that queued this warmup has no other render feeding it: if the
    // warmup dead-ends while the previous-page veil is still up, OnPaintSurface
    // keeps returning before the measurement overlay and the sheet shows without
    // any takeoffs until the next navigation. Fall back to a live preview render
    // exactly like the docnet page-open path does.
    private void RecoverPageOpenAfterRasterWarmupMiss(PageInfo queuedPage, string reason)
    {
        if (!IsCurrentPageRasterTarget(queuedPage.PdfPath, queuedPage.PdfPage, queuedPage.FolderPath) ||
            !_showingPreviousPageDuringSwitch ||
            _pdfLayersLoadedForPage ||
            _usingLayerRenderer)
        {
            return;
        }

        AppLog.Info(
            $"Viewport raster warmup fallback render; reason='{reason}'; " +
            $"page='{_pageFolder}'; pdf='{Path.GetFileName(_pdfPath)}'; pdfPage={_pdfIndex + 1}");
        ClearPreviousPageBitmapDuringSwitch();
        _showingPreviousPageDuringSwitch = false;
        QueueDocnetRender(
            PageSwitchLivePreviewScale(restoreView: null, fitAfter: false),
            statusAfter: $"Loaded: {Path.GetFileName(_pdfPath)}  page {_pdfIndex + 1}");
        RequestRepaint();
    }

    private bool ApplyPreparedRasterSheetBitmap(
        string pdfPath,
        int pageIndex,
        string pageFolder,
        RasterSheetSource rasterSheet,
        RasterSheetBitmapResult preparedBitmap,
        ViewState applyView,
        bool fitAfter,
        bool preferOverview)
    {
        return ApplyRasterSheetBitmapRender(
            pdfPath,
            pageIndex,
            pageFolder,
            rasterSheet,
            preparedBitmap,
            applyView,
            fitAfter,
            usingOverview: preferOverview);
    }

    private bool ShouldApplyWarmedRasterSheetBitmap(
        RasterSheetSource rasterSheet,
        bool preferOverview,
        bool allowLowZoomFullRaster)
    {
        if (preferOverview)
            return !ShouldUseRasterSheetForCurrentZoom();

        if (ShouldUseRasterSheetForCurrentZoom())
            return true;

        if (RasterSheetCacheService.IsSourceImageRaster(rasterSheet))
            return allowLowZoomFullRaster;

        return allowLowZoomFullRaster &&
               RasterSheetCacheService.RenderScaleToDpi(rasterSheet.RenderScale) <=
               ViewportRenderPolicy.RasterSheetPageOpenImmediateWarmMaxDpi;
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

        warmed |= TryWarmRasterSheetBitmapCache(
            page.FolderPath,
            page.PdfPath,
            source,
            preferOverview: false,
            action: "warmed",
            logFailure: true);

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

    private static bool TryPrepareRasterSheetBitmapForUiApply(
        PageInfo page,
        RasterSheetSource rasterSheet,
        bool preferOverview,
        out RasterSheetBitmapResult preparedBitmap)
    {
        preparedBitmap = new RasterSheetBitmapResult(new SKBitmap(), 0, 0, 0, "");
        if (!WarmRequestedRasterSheetBitmapCache(page, rasterSheet, preferOverview))
            return false;

        return TryGetRasterSheetBitmapCache(
            page.FolderPath,
            page.PdfPath,
            rasterSheet,
            preferOverview,
            out preparedBitmap);
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
