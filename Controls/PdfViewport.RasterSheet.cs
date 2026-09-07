using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    private void QueueRasterSheetSelfHealIfNeeded(
        string pdfPath,
        int pageIndex,
        string pageFolder,
        IReadOnlyList<PdfLayerInfo>? cachedLayers,
        RasterSheetSource? rasterSheet,
        string rasterSkipReason)
    {
        bool shouldSelfHeal = ShouldSelfHealRasterSheet(rasterSheet, rasterSkipReason);
        if (string.IsNullOrWhiteSpace(pageFolder) ||
            string.IsNullOrWhiteSpace(pdfPath) ||
            !Directory.Exists(pageFolder) ||
            !File.Exists(pdfPath))
        {
            return;
        }

        bool shouldBuildOverview = RasterSheetCacheService.ShouldBuildSourceImageOverview(
            pageFolder,
            pdfPath,
            rasterSheet,
            out string overviewReason);
        if (!shouldSelfHeal && !shouldBuildOverview)
            return;

        string effectiveReason = shouldSelfHeal
            ? rasterSkipReason
            : overviewReason;
        if (string.IsNullOrWhiteSpace(effectiveReason))
            effectiveReason = "raster sheet background refresh";

        string rebuildKey = RasterSheetRebuildKey(pdfPath, pageIndex, pageFolder);
        lock (_rasterSheetRebuildGate)
        {
            if (!_rasterSheetRebuildsInFlight.Add(rebuildKey))
                return;
        }

        AppLog.Info(
            $"Viewport raster sheet self-heal queued; reason='{effectiveReason}'; " +
            $"page='{pageFolder}'; pdf='{Path.GetFileName(pdfPath)}'; pdfPage={pageIndex + 1}");
        _ = RebuildRasterSheetForCurrentPageAsync(
            rebuildKey,
            pdfPath,
            pageIndex,
            pageFolder,
            cachedLayers,
            rasterSheet?.Clone(),
            effectiveReason);
    }

    private async Task RebuildRasterSheetForCurrentPageAsync(
        string rebuildKey,
        string pdfPath,
        int pageIndex,
        string pageFolder,
        IReadOnlyList<PdfLayerInfo>? cachedLayers,
        RasterSheetSource? rasterSheet,
        string rasterSkipReason)
    {
        try
        {
            var page = new PageInfo
            {
                Name = string.IsNullOrWhiteSpace(pageFolder) ? $"Page {pageIndex + 1}" : Path.GetFileName(pageFolder),
                FolderPath = pageFolder,
                PdfPath = pdfPath,
                PdfPage = pageIndex,
                PdfLayersCached = cachedLayers != null,
                PdfLayers = cachedLayers ?? Array.Empty<PdfLayerInfo>(),
                RasterSheet = rasterSheet?.Clone(),
            };

            if (!await WaitForCurrentPageRasterRebuildWindowAsync(
                    pdfPath,
                    pageIndex,
                    pageFolder,
                    rasterSkipReason).ConfigureAwait(false))
            {
                return;
            }

            bool overviewOnly =
                RasterSheetCacheService.ShouldBuildSourceImageOverview(
                    pageFolder,
                    pdfPath,
                    rasterSheet,
                    out _) ||
                (RasterSheetCacheService.IsSourceImageRaster(rasterSheet) &&
                 rasterSkipReason.Contains("overview", StringComparison.OrdinalIgnoreCase));
            await RasterSheetRefreshPrefetchSemaphore.WaitAsync().ConfigureAwait(false);
            RasterSheetBuildResult result;
            try
            {
                if (!await WaitForCurrentPageRasterRebuildWindowAsync(
                        pdfPath,
                        pageIndex,
                        pageFolder,
                        rasterSkipReason).ConfigureAwait(false))
                {
                    return;
                }

                using IDisposable? writeActivity =
                    JobFileWriteActivity.TryBeginBackgroundWriteForProjectPath(pageFolder);
                if (writeActivity == null)
                    return;

                result = await Task.Run(() => overviewOnly
                    ? RasterSheetCacheService.BuildOverviewForExistingSourceImageRaster(page)
                    : BuildReadableRasterSheetPreservingPinnedDpi(page)).ConfigureAwait(false);
            }
            finally
            {
                RasterSheetRefreshPrefetchSemaphore.Release();
            }

            if (!result.Ok || result.Source == null)
            {
                AppLog.Warn(
                    $"Viewport raster sheet self-heal failed; reason='{rasterSkipReason}'; " +
                    $"error='{result.Error}'; page='{pageFolder}'; pdf='{Path.GetFileName(pdfPath)}'; pdfPage={pageIndex + 1}");
                return;
            }

            AppLog.Info(
                $"Viewport raster sheet self-heal built; mode='{(overviewOnly ? "overview" : "full")}'; reason='{rasterSkipReason}'; " +
                $"page='{pageFolder}'; pdf='{Path.GetFileName(pdfPath)}'; pdfPage={pageIndex + 1}");
            WarmRasterSheetBitmapCache(page, result.Source);

            if (!IsCurrentPageRasterTarget(pdfPath, pageIndex, pageFolder))
                return;

            _rasterSheetSource = result.Source.Clone();
            if (_pdfLayersLoadedForPage || _usingLayerRenderer)
                return;

            // Overviews remain a disk/cache maintenance artifact only. Never
            // replace a full native sheet already on screen with one.
            if (overviewOnly)
                return;

            if (!ShouldUseRasterSheetForCurrentZoom())
                return;

            ViewState currentView = CaptureViewState();
            if (TryApplyRasterSheetRender(
                    pdfPath,
                    pageIndex,
                    pageFolder,
                    result.Source,
                    currentView,
                    fitAfter: false,
                    preferOverview: false,
                    requireCachedBitmap: true,
                    out string applyReason))
            {
                PostStatus($"Raster sheet rebuilt: {Path.GetFileName(pdfPath)}  page {pageIndex + 1}");
                RequestRepaint();
            }
            else
            {
                AppLog.Warn(
                    $"Viewport raster sheet self-heal apply skipped; reason='{applyReason}'; " +
                    $"page='{pageFolder}'; pdf='{Path.GetFileName(pdfPath)}'; pdfPage={pageIndex + 1}");
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport raster sheet self-heal crashed for {Path.GetFileName(pdfPath)} page {pageIndex + 1}");
        }
        finally
        {
            lock (_rasterSheetRebuildGate)
                _rasterSheetRebuildsInFlight.Remove(rebuildKey);
        }
    }

    private static RasterSheetBuildResult BuildReadableRasterSheetPreservingPinnedDpi(PageInfo page)
    {
        PageInfo currentPage = OurPlanCoreJobStore.TryReadPage(page.FolderPath) ?? page;
        int pinnedDpi = RasterSheetCacheService.PinnedRasterDpi(currentPage.RasterSheet);
        if (pinnedDpi <= 0)
            return RasterSheetCacheService.BuildAndEnable(currentPage);

        return RasterSheetCacheService.BuildAndEnable(
            currentPage,
            RasterSheetCacheService.RasterDpiToRenderScale(pinnedDpi),
            allowPinnedDpiChange: true,
            pinnedDpiOverride: pinnedDpi);
    }

    private async Task<bool> WaitForCurrentPageRasterRebuildWindowAsync(
        string pdfPath,
        int pageIndex,
        string pageFolder,
        string reason)
    {
        await WaitForPreviewPrefetchQuietWindowAsync().ConfigureAwait(false);
        if (IsCurrentPageRasterTarget(pdfPath, pageIndex, pageFolder))
            return true;

        AppLog.Info(
            $"Viewport raster sheet self-heal skipped stale page; reason='{reason}'; " +
            $"page='{pageFolder}'; pdf='{Path.GetFileName(pdfPath)}'; pdfPage={pageIndex + 1}");
        return false;
    }

    private bool IsCurrentPageRasterTarget(string pdfPath, int pageIndex, string pageFolder) =>
        string.Equals(pdfPath, _pdfPath, StringComparison.OrdinalIgnoreCase) &&
        pageIndex == _pdfIndex &&
        string.Equals(pageFolder, _pageFolder, StringComparison.OrdinalIgnoreCase);

    private static bool ShouldSelfHealRasterSheet(RasterSheetSource? rasterSheet, string reason) =>
        rasterSheet?.Enabled == true &&
        (reason.Contains("legacy lineboost", StringComparison.OrdinalIgnoreCase) ||
         reason.Contains("source PDF changed", StringComparison.OrdinalIgnoreCase) ||
         reason.Contains("image file", StringComparison.OrdinalIgnoreCase) ||
         reason.Contains("page size is invalid", StringComparison.OrdinalIgnoreCase));

    private bool ShouldUseRasterSheetForPageOpen(RasterSheetSource? rasterSheet, ViewState? restoreView, bool fitAfter)
    {
        if (RasterSheetCacheService.IsSourceImageRaster(rasterSheet))
            return true;

        return rasterSheet?.Enabled == true &&
               (RasterSheetCacheService.UseAsPageOpenRaster(rasterSheet) ||
                !IsLowZoomRasterSheetPageOpen(restoreView, fitAfter));
    }

    private static bool IsLowZoomRasterSheetPageOpen(ViewState? restoreView, bool fitAfter)
    {
        if (restoreView.HasValue)
            return restoreView.Value.Zoom < ViewportRenderPolicy.RasterSheetDisplayMinZoom;

        return fitAfter;
    }

    private bool ShouldUseRasterSheetForCurrentZoom() =>
        _zoom >= ViewportRenderPolicy.RasterSheetDisplayMinZoom;

    private bool QueueRasterSheetWorkZoomWarmupForPageOpen(
        string pdfPath,
        int pageIndex,
        string pageFolder,
        RasterSheetSource? rasterSheet,
        bool allowLowZoomFullRasterApply,
        bool buildMissingDpis,
        out bool workZoomWarmupQueued)
    {
        workZoomWarmupQueued = false;
        if (!ShouldWarmRasterSheetForWorkZoomOnPageOpen(pageFolder, pdfPath, rasterSheet))
            return false;
        RasterSheetSource source = rasterSheet!;

        bool queuedApply = false;
        if (ShouldWarmRasterSheetSourceBitmapForPageOpen(source, allowLowZoomFullRasterApply))
        {
            queuedApply = QueueRasterSheetBitmapApplyAfterWarmup(
                pdfPath,
                pageIndex,
                pageFolder,
                source,
                preferOverview: false,
                allowLowZoomFullRaster: true);
        }

        workZoomWarmupQueued = true;
        PdfViewport.PrefetchRasterSheetWorkZoomBitmaps(
            new PageInfo
            {
                Name = string.IsNullOrWhiteSpace(pageFolder) ? $"Page {pageIndex + 1}" : Path.GetFileName(pageFolder),
                FolderPath = pageFolder,
                PdfPath = pdfPath,
                PdfPage = pageIndex,
                PdfLayersCached = _cachedLayers != null,
                PdfLayers = _cachedLayers ?? Array.Empty<PdfLayerInfo>(),
                RasterSheet = source.Clone(),
            },
            buildMissingDpis: buildMissingDpis);
        return queuedApply;
    }

    private static bool ShouldWarmRasterSheetForWorkZoomOnPageOpen(
        string pageFolder,
        string pdfPath,
        RasterSheetSource? rasterSheet)
    {
        if (rasterSheet?.Enabled != true ||
            RasterSheetCacheService.ShouldRebuildForReadableDisplay(pageFolder, pdfPath, rasterSheet, out _))
        {
            return false;
        }

        return true;
    }

    private static bool ShouldWarmRasterSheetSourceBitmapForPageOpen(
        RasterSheetSource rasterSheet,
        bool allowLowZoomFullRasterApply)
    {
        if (RasterSheetCacheService.IsSourceImageRaster(rasterSheet))
            return true;

        return allowLowZoomFullRasterApply &&
               RasterSheetCacheService.RenderScaleToDpi(rasterSheet.RenderScale) <=
               ViewportRenderPolicy.RasterSheetPageOpenImmediateWarmMaxDpi;
    }

    private bool TryApplyReadyRasterSheetForCurrentZoom()
    {
        if (!ShouldUseRasterSheetForCurrentZoom() ||
            _rasterSheetSource?.Enabled != true ||
            _pdfLayersLoadedForPage ||
            _usingLayerRenderer)
        {
            return false;
        }

        if (TryApplyResponsiveRasterSheetDpiForCurrentZoom())
            return true;

        int currentDpi = RasterSheetCacheService.RenderScaleToDpi(_bitmapScale);
        int targetDpi = ViewportRenderPolicy.SelectRasterSheetDisplayDpi(_zoom);
        if (TryHoldHeavyRasterSheetDpiForRecentMotion(CurrentRasterSheetPageInfo(), currentDpi, targetDpi))
            return true;
        if (ShouldSkipStaleRasterSheetSourceApply(currentDpi, targetDpi))
            return false;

        ViewState currentView = CaptureViewState();
        if (TryApplyRasterSheetRender(
                _pdfPath,
                _pdfIndex,
                _pageFolder,
                _rasterSheetSource,
                currentView,
                fitAfter: false,
                preferOverview: false,
                requireCachedBitmap: true,
                out string rasterSkipReason))
        {
            PostStatus($"Raster sheet: {Path.GetFileName(_pdfPath)}  page {_pdfIndex + 1}");
            RequestRepaint();
            return true;
        }

        if (IsRasterSheetBitmapCacheWarmingReason(rasterSkipReason))
        {
            return QueueRasterSheetBitmapApplyAfterWarmup(
                _pdfPath,
                _pdfIndex,
                _pageFolder,
                _rasterSheetSource,
                preferOverview: false);
        }

        QueueRasterSheetSelfHealIfNeeded(
            _pdfPath,
            _pdfIndex,
            _pageFolder,
            _cachedLayers,
            _rasterSheetSource,
            rasterSkipReason);
        return false;
    }

    private bool ShouldSkipStaleRasterSheetSourceApply(int currentDpi, int targetDpi)
    {
        if (!_usingRasterSheetRender ||
            _usingRasterSheetOverviewRender ||
            currentDpi <= 0 ||
            targetDpi <= 0 ||
            _rasterSheetSource?.Enabled != true)
        {
            return false;
        }

        int sourceDpi = RasterSheetCacheService.RenderScaleToDpi(_rasterSheetSource.RenderScale);
        return currentDpi >= targetDpi || sourceDpi <= currentDpi;
    }

    private bool TrySwitchRasterSheetToFastPreviewForLowZoom(
        bool requestRepaint = true,
        bool requireCachedBitmap = false)
    {
        if (!_usingRasterSheetRender ||
            _zoom >= ViewportRenderPolicy.RasterSheetDisplayExitZoom)
        {
            return false;
        }

        if (ShouldKeepRasterSheetAtLowZoom())
            return false;

        ViewState currentView = CaptureViewState();
        if (!_usingRasterSheetOverviewRender &&
            RasterSheetCacheService.HasSourceImageOverview(_rasterSheetSource) &&
            TryApplyRasterSheetRender(
                _pdfPath,
                _pdfIndex,
                _pageFolder,
                _rasterSheetSource,
                currentView,
                fitAfter: false,
                preferOverview: true,
                requireCachedBitmap: true,
                out _))
        {
            PostStatus($"Raster overview: {Path.GetFileName(_pdfPath)}  page {_pdfIndex + 1}");
            if (requestRepaint)
                RequestRepaint();
            return true;
        }

        if (TryApplyPersistedPreviewRender(
                _pdfPath,
                _pdfIndex,
                ViewportRenderPolicy.FastPageSwitchPreviewRenderScale,
                currentView,
                fitAfter: false,
                allowDiskRead: false,
                requestRepaint: false))
        {
            PostStatus($"Fast preview: {Path.GetFileName(_pdfPath)}  page {_pdfIndex + 1}");
            if (requestRepaint)
                RequestRepaint();
            return true;
        }

        if (requireCachedBitmap)
            return false;

        QueueDocnetRender(
            ViewportRenderPolicy.FastPageSwitchPreviewRenderScale,
            currentView,
            fitAfter: false,
            queueLayerAfter: false,
            resetLayerStates: false,
            statusAfter: $"Fast preview: {Path.GetFileName(_pdfPath)}  page {_pdfIndex + 1}",
            fireLayersAfter: false);
        return true;
    }

    private bool TrySwitchRasterSheetToFastPreviewForNavigation(
        bool allowWorkZoom = false,
        bool allowAfterMotion = false)
    {
        if (!_usingRasterSheetRender ||
            !_isFastNavigating && !allowAfterMotion ||
            !allowWorkZoom &&
            _zoom >= ViewportRenderPolicy.FarZoomFastFrameThreshold)
        {
            return false;
        }

        ViewState currentView = CaptureViewState();
        if (!TryApplyPersistedPreviewRender(
                _pdfPath,
                _pdfIndex,
                ViewportRenderPolicy.FastPageSwitchPreviewRenderScale,
                currentView,
                fitAfter: false,
                allowDiskRead: false,
                requestRepaint: false))
        {
            return false;
        }

        PostStatus($"Fast preview: {Path.GetFileName(_pdfPath)}  page {_pdfIndex + 1}");
        RequestRepaint();
        return true;
    }

    private static string RasterSheetRebuildKey(string pdfPath, int pageIndex, string pageFolder) =>
        $"{Path.GetFullPath(pdfPath)}|{pageIndex}|{Path.GetFullPath(pageFolder)}";

    private bool ShouldKeepRasterSheetAtLowZoom() =>
        _rasterSheetSource?.Enabled == true &&
        (RasterSheetCacheService.IsRasterDpiPinned(_rasterSheetSource) ||
         RasterSheetCacheService.IsSourceImageRaster(_rasterSheetSource));
}
