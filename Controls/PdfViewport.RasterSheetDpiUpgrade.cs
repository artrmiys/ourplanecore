using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private bool TryUpgradeRasterSheetToReadyDpiForCurrentZoom()
    {
        if (!_usingRasterSheetRender ||
            _usingRasterSheetOverviewRender ||
            _rasterSheetSource?.Enabled != true ||
            RasterSheetCacheService.IsSourceImageRaster(_rasterSheetSource) ||
            _pdfLayersLoadedForPage ||
            _usingLayerRenderer ||
            _bitmapScale <= 0 ||
            !ViewportRenderPolicy.ShouldUseDetailRender(_zoom, _bitmapScale))
        {
            return false;
        }

        if (_isFastNavigating)
            return true;

        PageInfo page = CurrentRasterSheetPageInfo();
        int currentDpi = RasterSheetCacheService.RenderScaleToDpi(_bitmapScale);
        int targetDpi = ReadyRasterSheetDpiForCurrentZoom(page, currentDpi);
        if (targetDpi > currentDpi &&
            TryApplyReadyRasterSheetDpi(page, targetDpi))
        {
            return true;
        }

        return QueueRasterSheetDpiBuildForCurrentZoom(page, currentDpi, targetDpi);
    }

    private bool TryApplyResponsiveRasterSheetDpiForCurrentZoom()
    {
        if (!ShouldUseResponsiveRasterSheetDpiForCurrentZoom(_rasterSheetSource))
            return false;

        PageInfo page = CurrentRasterSheetPageInfo();
        int currentDpi = RasterSheetCacheService.RenderScaleToDpi(_bitmapScale);
        int targetDpi = TargetRasterSheetDpiForCurrentZoom();
        if (_usingRasterSheetRender && currentDpi == targetDpi)
            return false;

        if (TryApplyReadyRasterSheetDpi(page, targetDpi))
            return true;

        if (TryApplyReadyRasterSheetDpiAtOrAbove(page, targetDpi, currentDpi))
            return true;

        if (_usingRasterSheetRender &&
            currentDpi >= targetDpi &&
            currentDpi <= ViewportRenderPolicy.RasterSheetDisplayMaxDpi)
        {
            return false;
        }

        if (!QueueRasterSheetDpiBuildForCurrentZoom(page, currentDpi, targetDpi))
            return false;

        TrySwitchRasterSheetToFastPreviewForNavigation();
        return true;
    }

    private bool TryPrepareRasterSheetBitmapForImmediateRepaint()
    {
        if (!_usingRasterSheetRender ||
            _rasterSheetSource?.Enabled != true ||
            _pdfLayersLoadedForPage ||
            _usingLayerRenderer ||
            _bitmapScale <= 0)
        {
            return false;
        }

        if (_zoom < ViewportRenderPolicy.RasterSheetDisplayExitZoom)
            return TrySwitchRasterSheetToFastPreviewForLowZoom(requestRepaint: false, requireCachedBitmap: true);

        if (_usingRasterSheetOverviewRender ||
            RasterSheetCacheService.IsSourceImageRaster(_rasterSheetSource) ||
            _zoom < ViewportRenderPolicy.RasterSheetDisplayMinZoom)
        {
            return false;
        }

        int currentDpi = RasterSheetCacheService.RenderScaleToDpi(_bitmapScale);
        int targetDpi = TargetRasterSheetDpiForCurrentZoom();
        if (targetDpi <= 0)
            return false;

        PageInfo page = CurrentRasterSheetPageInfo();
        if (currentDpi <= targetDpi)
            return TryApplyReadyRasterSheetDpiAtOrAboveFromMemory(page, targetDpi, currentDpi);

        return TryApplyReadyRasterSheetDpiFromMemory(page, targetDpi) ||
               TryApplyReadyRasterSheetDpiAtOrAboveFromMemory(page, targetDpi, currentDpi);
    }

    private bool ShouldUseResponsiveRasterSheetDpiForCurrentZoom(RasterSheetSource? rasterSheet) =>
        ShouldUseResponsiveRasterSheetDpiForZoom(rasterSheet, _zoom);

    private bool ShouldUseResponsiveRasterSheetDpiForView(RasterSheetSource? rasterSheet, ViewState? view) =>
        ShouldUseResponsiveRasterSheetDpiForZoom(rasterSheet, view?.Zoom ?? _zoom);

    private bool ShouldUseResponsiveRasterSheetDpiForZoom(RasterSheetSource? rasterSheet, float zoom) =>
        rasterSheet?.Enabled == true &&
        !RasterSheetCacheService.IsSourceImageRaster(rasterSheet) &&
        !_pdfLayersLoadedForPage &&
        !_usingLayerRenderer &&
        zoom >= ViewportRenderPolicy.RasterSheetDisplayMinZoom &&
        TargetRasterSheetDpiForZoom(zoom) > 0 &&
        RasterSheetCacheService.RenderScaleToDpi(rasterSheet.RenderScale) != TargetRasterSheetDpiForZoom(zoom);

    private int TargetRasterSheetDpiForCurrentZoom() =>
        TargetRasterSheetDpiForZoom(_zoom);

    private static int TargetRasterSheetDpiForZoom(float zoom) =>
        ViewportRenderPolicy.SelectRasterSheetDisplayDpi(zoom);

    private bool TryApplyReadyResponsiveRasterSheetDpiForPageOpen(
        RasterSheetSource? rasterSheet,
        ViewState? restoreView,
        bool fitAfter)
    {
        if (!ShouldUseResponsiveRasterSheetDpiForView(rasterSheet, restoreView))
            return false;

        int targetDpi = TargetRasterSheetDpiForZoom(restoreView?.Zoom ?? _zoom);
        int currentDpi = RasterSheetCacheService.RenderScaleToDpi(rasterSheet!.RenderScale);
        PageInfo page = CurrentRasterSheetPageInfo(rasterSheet);
        return TryApplyReadyRasterSheetDpi(page, targetDpi, restoreView, fitAfter) ||
               TryApplyReadyRasterSheetDpiAtOrAbove(page, targetDpi, currentDpi, restoreView, fitAfter);
    }

    private bool QueueResponsiveRasterSheetDpiBuildForPageOpen(
        RasterSheetSource? rasterSheet,
        ViewState? restoreView)
    {
        if (!ShouldUseResponsiveRasterSheetDpiForView(rasterSheet, restoreView))
            return false;

        int currentDpi = RasterSheetCacheService.RenderScaleToDpi(rasterSheet!.RenderScale);
        int targetDpi = TargetRasterSheetDpiForZoom(restoreView?.Zoom ?? _zoom);
        PageInfo page = CurrentRasterSheetPageInfo(rasterSheet);
        return QueueRasterSheetDpiBuildForCurrentZoom(page, currentDpi, targetDpi);
    }

    private int ReadyRasterSheetDpiForCurrentZoom(PageInfo page, int currentDpi)
    {
        int desiredDpi = DesiredRasterSheetDpiForCurrentZoom(currentDpi);
        if (desiredDpi <= currentDpi)
            return 0;

        IReadOnlyList<int> readyDpis = RasterSheetCacheService.ReadyReadableRasterDpis(page);
        int smallestReadyAtOrAboveDesired = 0;
        int largestReadyAboveCurrent = 0;
        foreach (int readyDpi in readyDpis)
        {
            if (readyDpi <= currentDpi)
                continue;
            if (readyDpi > ViewportRenderPolicy.RasterSheetDisplayMaxDpi)
                continue;

            if (readyDpi > largestReadyAboveCurrent)
                largestReadyAboveCurrent = readyDpi;

            if (readyDpi >= desiredDpi &&
                (smallestReadyAtOrAboveDesired == 0 || readyDpi < smallestReadyAtOrAboveDesired))
            {
                smallestReadyAtOrAboveDesired = readyDpi;
            }
        }

        return smallestReadyAtOrAboveDesired > 0
            ? smallestReadyAtOrAboveDesired
            : largestReadyAboveCurrent;
    }

    private int DesiredRasterSheetDpiForCurrentZoom(int currentDpi)
    {
        int targetDpi = TargetRasterSheetDpiForCurrentZoom();
        if (targetDpi <= currentDpi)
            return 0;

        return targetDpi;
    }

    private bool TryApplyReadyRasterSheetDpi(PageInfo page, int targetDpi) =>
        TryApplyReadyRasterSheetDpi(page, targetDpi, CaptureViewState(), fitAfter: false);

    private bool TryApplyReadyRasterSheetDpiFromMemory(PageInfo page, int targetDpi)
    {
        float targetScale = RasterSheetCacheService.RasterDpiToRenderScale(targetDpi);
        if (!RasterSheetCacheService.TryGetReadyReadableRasterSource(page, targetScale, out RasterSheetSource? readySource) ||
            readySource == null)
        {
            return false;
        }

        ViewState currentView = CaptureViewState();
        if (!TryApplyRasterSheetRender(
                _pdfPath,
                _pdfIndex,
                _pageFolder,
                readySource,
                currentView,
                fitAfter: false,
                preferOverview: false,
                requireCachedBitmap: true,
                out _))
        {
            return false;
        }

        _rasterSheetSource = readySource.Clone();
        PostStatus($"Raster sheet {targetDpi} DPI: {Path.GetFileName(_pdfPath)}  page {_pdfIndex + 1}");
        AppLog.Info(
            $"Viewport raster DPI immediate repaint prepared; dpi={targetDpi}; " +
            $"page='{_pageFolder}'; pdf='{Path.GetFileName(_pdfPath)}'; pdfPage={_pdfIndex + 1}");
        return true;
    }

    private bool TryApplyReadyRasterSheetDpiAtOrAbove(PageInfo page, int targetDpi, int currentDpi)
    {
        int readyDpi = SelectReadyRasterSheetDpiAtOrAbove(page, targetDpi, currentDpi);
        return readyDpi > 0 && TryApplyReadyRasterSheetDpi(page, readyDpi);
    }

    private bool TryApplyReadyRasterSheetDpiAtOrAbove(
        PageInfo page,
        int targetDpi,
        int currentDpi,
        ViewState? restoreView,
        bool fitAfter)
    {
        int readyDpi = SelectReadyRasterSheetDpiAtOrAbove(page, targetDpi, currentDpi);
        return readyDpi > 0 && TryApplyReadyRasterSheetDpi(page, readyDpi, restoreView, fitAfter);
    }

    private bool TryApplyReadyRasterSheetDpiAtOrAboveFromMemory(PageInfo page, int targetDpi, int currentDpi)
    {
        int readyDpi = SelectReadyRasterSheetDpiAtOrAbove(page, targetDpi, currentDpi);
        return readyDpi > 0 && TryApplyReadyRasterSheetDpiFromMemory(page, readyDpi);
    }

    private int SelectReadyRasterSheetDpiAtOrAbove(PageInfo page, int targetDpi, int currentDpi)
    {
        int selectedDpi = 0;
        foreach (int readyDpi in RasterSheetCacheService.ReadyReadableRasterDpis(page))
        {
            if (readyDpi < targetDpi)
                continue;
            if (readyDpi > ViewportRenderPolicy.RasterSheetDisplayMaxDpi)
                continue;
            if (_usingRasterSheetRender && readyDpi == currentDpi)
                continue;
            if (selectedDpi == 0 || readyDpi < selectedDpi)
                selectedDpi = readyDpi;
        }

        return selectedDpi;
    }

    private bool TryApplyReadyRasterSheetDpi(
        PageInfo page,
        int targetDpi,
        ViewState? restoreView,
        bool fitAfter)
    {
        float targetScale = RasterSheetCacheService.RasterDpiToRenderScale(targetDpi);
        if (!RasterSheetCacheService.TryEnableReadyReadableRaster(
                page,
                targetScale,
                out RasterSheetBuildResult result) ||
            result.Source == null)
        {
            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                AppLog.Warn(
                    $"Viewport ready raster DPI upgrade failed; dpi={targetDpi}; error='{result.Error}'; " +
                    $"page='{_pageFolder}'; pdf='{Path.GetFileName(_pdfPath)}'; pdfPage={_pdfIndex + 1}");
            }

            return false;
        }

        return ApplyRasterSheetDpiUpgradeResult(result.Source, targetDpi, "ready", restoreView, fitAfter, out _);
    }

    private bool QueueRasterSheetDpiBuildForCurrentZoom(PageInfo page, int currentDpi, int targetDpi)
    {
        if (targetDpi <= 0 ||
            targetDpi > ViewportRenderPolicy.RasterSheetDisplayMaxDpi ||
            targetDpi == currentDpi ||
            currentDpi >= targetDpi && currentDpi <= ViewportRenderPolicy.RasterSheetDisplayMaxDpi ||
            string.IsNullOrWhiteSpace(page.FolderPath) ||
            string.IsNullOrWhiteSpace(page.PdfPath) ||
            !Directory.Exists(page.FolderPath) ||
            !File.Exists(page.PdfPath))
        {
            return false;
        }

        float targetScale = RasterSheetCacheService.RasterDpiToRenderScale(targetDpi);
        if (RasterSheetCacheService.HasReadyReadableRaster(page, targetScale))
            return TryApplyReadyRasterSheetDpi(page, targetDpi);

        string rebuildKey = RasterSheetDpiUpgradeKey(_pdfPath, _pdfIndex, _pageFolder, targetDpi);
        lock (_rasterSheetRebuildGate)
        {
            if (!_rasterSheetRebuildsInFlight.Add(rebuildKey))
                return true;
        }

        AppLog.Info(
            $"Viewport raster DPI build queued; dpi={targetDpi}; currentDpi={currentDpi}; " +
            $"page='{_pageFolder}'; pdf='{Path.GetFileName(_pdfPath)}'; pdfPage={_pdfIndex + 1}");
        PostStatus($"Raster sheet preparing {targetDpi} DPI: {Path.GetFileName(_pdfPath)}  page {_pdfIndex + 1}");
        _ = BuildRasterSheetDpiUpgradeForCurrentPageAsync(rebuildKey, page, targetDpi);
        return true;
    }

    private async Task BuildRasterSheetDpiUpgradeForCurrentPageAsync(
        string rebuildKey,
        PageInfo queuedPage,
        int targetDpi)
    {
        try
        {
            if (!IsCurrentPageRasterTarget(queuedPage.PdfPath, queuedPage.PdfPage, queuedPage.FolderPath))
                return;

            await RasterSheetRefreshPrefetchSemaphore.WaitAsync().ConfigureAwait(false);
            RasterSheetBuildResult result;
            try
            {
                if (!IsCurrentPageRasterTarget(queuedPage.PdfPath, queuedPage.PdfPage, queuedPage.FolderPath))
                    return;

                PageInfo buildPage = OurPlaneCoreJobStore.TryReadPage(queuedPage.FolderPath) ?? queuedPage;
                if (buildPage.RasterSheet?.Enabled != true)
                    return;

                float targetScale = RasterSheetCacheService.RasterDpiToRenderScale(targetDpi);
                result = await Task.Run(() => RasterSheetCacheService.BuildAndEnable(buildPage, targetScale))
                    .ConfigureAwait(false);
            }
            finally
            {
                RasterSheetRefreshPrefetchSemaphore.Release();
            }

            if (!result.Ok || result.Source == null)
            {
                AppLog.Warn(
                    $"Viewport raster DPI upgrade failed; dpi={targetDpi}; error='{result.Error}'; " +
                    $"page='{queuedPage.FolderPath}'; pdf='{Path.GetFileName(queuedPage.PdfPath)}'; pdfPage={queuedPage.PdfPage + 1}");
                return;
            }

            AppLog.Info(
                $"Viewport raster DPI upgrade built; dpi={targetDpi}; reused={result.Reused}; " +
                $"page='{queuedPage.FolderPath}'; pdf='{Path.GetFileName(queuedPage.PdfPath)}'; pdfPage={queuedPage.PdfPage + 1}");
            WarmRasterSheetBitmapCache(queuedPage, result.Source);

            await Dispatcher.InvokeAsync(() =>
            {
                if (!IsCurrentPageRasterTarget(queuedPage.PdfPath, queuedPage.PdfPage, queuedPage.FolderPath) ||
                    _pdfLayersLoadedForPage ||
                    _usingLayerRenderer ||
                    !ShouldUseRasterSheetForCurrentZoom() ||
                    targetDpi != TargetRasterSheetDpiForCurrentZoom())
                {
                    return;
                }

                if (ApplyRasterSheetDpiUpgradeResult(result.Source, targetDpi, "built", out string applyReason))
                    return;

                AppLog.Warn(
                    $"Viewport raster DPI upgrade apply skipped; dpi={targetDpi}; reason='{applyReason}'; " +
                    $"page='{queuedPage.FolderPath}'; pdf='{Path.GetFileName(queuedPage.PdfPath)}'; pdfPage={queuedPage.PdfPage + 1}");
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport raster DPI upgrade crashed for {queuedPage.Name}");
        }
        finally
        {
            lock (_rasterSheetRebuildGate)
                _rasterSheetRebuildsInFlight.Remove(rebuildKey);
        }
    }

    private bool ApplyRasterSheetDpiUpgradeResult(
        RasterSheetSource source,
        int targetDpi,
        string sourceKind,
        out string applyReason) =>
        ApplyRasterSheetDpiUpgradeResult(
            source,
            targetDpi,
            sourceKind,
            CaptureViewState(),
            fitAfter: false,
            out applyReason);

    private bool ApplyRasterSheetDpiUpgradeResult(
        RasterSheetSource source,
        int targetDpi,
        string sourceKind,
        ViewState? restoreView,
        bool fitAfter,
        out string applyReason)
    {
        _rasterSheetSource = source.Clone();
        if (TryApplyRasterSheetRender(
                _pdfPath,
                _pdfIndex,
                _pageFolder,
                _rasterSheetSource,
                restoreView,
                fitAfter,
                out applyReason))
        {
            PostStatus($"Raster sheet {targetDpi} DPI: {Path.GetFileName(_pdfPath)}  page {_pdfIndex + 1}");
            AppLog.Info(
                $"Viewport raster DPI upgrade applied; source='{sourceKind}'; dpi={targetDpi}; " +
                $"page='{_pageFolder}'; pdf='{Path.GetFileName(_pdfPath)}'; pdfPage={_pdfIndex + 1}");
            RequestRepaint();
            return true;
        }

        return false;
    }

    private PageInfo CurrentRasterSheetPageInfo(RasterSheetSource? rasterSheet = null) => new()
    {
        Name = string.IsNullOrWhiteSpace(_pageFolder) ? $"Page {_pdfIndex + 1}" : Path.GetFileName(_pageFolder),
        FolderPath = _pageFolder,
        PdfPath = _pdfPath,
        PdfPage = _pdfIndex,
        PdfLayersCached = _cachedLayers != null,
        PdfLayers = _cachedLayers ?? Array.Empty<PdfLayerInfo>(),
        RasterSheet = rasterSheet?.Clone() ?? _rasterSheetSource?.Clone(),
    };

    private static string RasterSheetDpiUpgradeKey(string pdfPath, int pageIndex, string pageFolder, int targetDpi) =>
        $"{RasterSheetRebuildKey(pdfPath, pageIndex, pageFolder)}|dpi:{targetDpi}";
}
