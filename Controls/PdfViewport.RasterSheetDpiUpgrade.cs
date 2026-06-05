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

        return QueueRasterSheetDpiUpgradeForCurrentZoom(page, currentDpi);
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
        int rawDpi = Math.Clamp(
            RasterSheetCacheService.RenderScaleToDpi(Math.Min(_zoom, RasterSheetCacheService.MaxRasterDpi / 72f)),
            72,
            RasterSheetCacheService.MaxRasterDpi);
        if (rawDpi <= currentDpi)
            return 0;

        int[] dpiSteps =
        [
            RasterSheetCacheService.DefaultRasterDpi,
            300,
            RasterSheetCacheService.MaxRasterDpi,
        ];
        foreach (int dpi in dpiSteps)
        {
            if (dpi > currentDpi && dpi >= rawDpi)
                return dpi;
        }

        return currentDpi < RasterSheetCacheService.MaxRasterDpi
            ? RasterSheetCacheService.MaxRasterDpi
            : 0;
    }

    private bool TryApplyReadyRasterSheetDpi(PageInfo page, int targetDpi)
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

        return ApplyRasterSheetDpiUpgradeResult(result.Source, targetDpi, "ready", out _);
    }

    private bool QueueRasterSheetDpiUpgradeForCurrentZoom(PageInfo page, int currentDpi)
    {
        int targetDpi = DesiredRasterSheetDpiForCurrentZoom(currentDpi);
        if (targetDpi <= currentDpi ||
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
            $"Viewport raster DPI upgrade queued; dpi={targetDpi}; currentDpi={currentDpi}; " +
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
                    !ShouldUseRasterSheetForCurrentZoom())
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
        out string applyReason)
    {
        _rasterSheetSource = source.Clone();
        ViewState currentView = CaptureViewState();
        if (TryApplyRasterSheetRender(
                _pdfPath,
                _pdfIndex,
                _pageFolder,
                _rasterSheetSource,
                currentView,
                fitAfter: false,
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

    private PageInfo CurrentRasterSheetPageInfo() => new()
    {
        Name = string.IsNullOrWhiteSpace(_pageFolder) ? $"Page {_pdfIndex + 1}" : Path.GetFileName(_pageFolder),
        FolderPath = _pageFolder,
        PdfPath = _pdfPath,
        PdfPage = _pdfIndex,
        PdfLayersCached = _cachedLayers != null,
        PdfLayers = _cachedLayers ?? Array.Empty<PdfLayerInfo>(),
        RasterSheet = _rasterSheetSource?.Clone(),
    };

    private static string RasterSheetDpiUpgradeKey(string pdfPath, int pageIndex, string pageFolder, int targetDpi) =>
        $"{RasterSheetRebuildKey(pdfPath, pageIndex, pageFolder)}|dpi:{targetDpi}";
}
