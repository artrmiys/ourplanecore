using System;
using System.IO;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private bool ShouldUseResponsiveRasterSheetDpiForPageOpen(
        RasterSheetSource? rasterSheet,
        ViewState? restoreView,
        bool fitAfter)
    {
        if (RasterSheetCacheService.UseAsPageOpenRaster(rasterSheet) ||
            rasterSheet?.Enabled != true ||
            RasterSheetCacheService.IsSourceImageRaster(rasterSheet) ||
            _pdfLayersLoadedForPage ||
            _usingLayerRenderer)
        {
            return false;
        }

        int currentDpi = RasterSheetCacheService.RenderScaleToDpi(rasterSheet.RenderScale);
        int targetDpi = TargetRasterSheetDpiForPageOpen(rasterSheet, restoreView, fitAfter);
        return ViewportRenderPolicy.ShouldPreferLowerRasterSheetDpi(currentDpi, targetDpi);
    }

    private bool ShouldSkipOversizedRasterSheetForPageOpen(
        RasterSheetSource? rasterSheet,
        ViewState? restoreView,
        bool fitAfter)
    {
        if (!ShouldUseResponsiveRasterSheetDpiForPageOpen(rasterSheet, restoreView, fitAfter))
            return false;

        int currentDpi = RasterSheetCacheService.RenderScaleToDpi(rasterSheet!.RenderScale);
        int targetDpi = TargetRasterSheetDpiForPageOpen(rasterSheet, restoreView, fitAfter);
        if (!ViewportRenderPolicy.ShouldPreferLowerRasterSheetDpi(currentDpi, targetDpi))
            return false;

        PageInfo page = CurrentRasterSheetPageInfo(rasterSheet);
        return !TryGetRememberedReadyRasterSheetSource(page, targetDpi, out _);
    }

    private int TargetRasterSheetDpiForPageOpen(
        RasterSheetSource? rasterSheet,
        ViewState? restoreView,
        bool fitAfter) =>
        TargetRasterSheetDpiForZoom(PageOpenZoomForRasterSheet(rasterSheet, restoreView, fitAfter));

    private float PageOpenZoomForRasterSheet(
        RasterSheetSource? rasterSheet,
        ViewState? restoreView,
        bool fitAfter)
    {
        if (restoreView.HasValue)
            return restoreView.Value.Zoom;

        if (!fitAfter)
            return _zoom;

        if (rasterSheet?.WidthPt > 0 &&
            rasterSheet.HeightPt > 0 &&
            ViewportCanvasWidth >= 2 &&
            ViewportCanvasHeight >= 2)
        {
            float fitZoom = Math.Min(
                ViewportCanvasWidth / (float)rasterSheet.WidthPt,
                ViewportCanvasHeight / (float)rasterSheet.HeightPt) * 0.95f;
            return Math.Clamp(fitZoom, ZoomMin, ZoomMax);
        }

        return 0;
    }

    private bool TryApplyReadyResponsiveRasterSheetDpiForPageOpen(
        RasterSheetSource? rasterSheet,
        ViewState? restoreView,
        bool fitAfter)
    {
        if (!ShouldUseResponsiveRasterSheetDpiForPageOpen(rasterSheet, restoreView, fitAfter))
            return false;

        int targetDpi = TargetRasterSheetDpiForPageOpen(rasterSheet, restoreView, fitAfter);
        int currentDpi = RasterSheetCacheService.RenderScaleToDpi(rasterSheet!.RenderScale);
        PageInfo page = CurrentRasterSheetPageInfo(rasterSheet);
        int readyDpi = SelectReadyRasterSheetDpiForPageOpen(page, targetDpi, currentDpi);
        return readyDpi > 0 &&
               QueueReadyRasterSheetDpiApplyAfterWarmupForPageOpen(
                   page,
                   readyDpi,
                   restoreView,
                   fitAfter,
                   _rasterSheetQualityRestoreVersion);
    }

    private bool QueueResponsiveRasterSheetDpiBuildForPageOpen(
        RasterSheetSource? rasterSheet,
        ViewState? restoreView,
        bool fitAfter)
    {
        if (!ShouldUseResponsiveRasterSheetDpiForPageOpen(rasterSheet, restoreView, fitAfter))
            return false;

        int currentDpi = RasterSheetCacheService.RenderScaleToDpi(rasterSheet!.RenderScale);
        int targetDpi = TargetRasterSheetDpiForPageOpen(rasterSheet, restoreView, fitAfter);
        PageInfo page = CurrentRasterSheetPageInfo(rasterSheet);
        int readyDpi = SelectReadyRasterSheetDpiForPageOpen(page, targetDpi, currentDpi);
        if (readyDpi > 0)
        {
            return QueueReadyRasterSheetDpiApplyAfterWarmupForPageOpen(
                page,
                readyDpi,
                restoreView,
                fitAfter,
                _rasterSheetQualityRestoreVersion);
        }

        if (ShouldDeferMissingResponsiveRasterSheetDpiForPageOpen(rasterSheet, restoreView, fitAfter))
        {
            AppLog.Info(
                $"Viewport raster DPI page open build deferred; dpi={targetDpi}; currentDpi={currentDpi}; " +
                $"page='{_pageFolder}'; pdf='{Path.GetFileName(_pdfPath)}'; pdfPage={_pdfIndex + 1}");
            return false;
        }

        return QueueRasterSheetDpiBuildForPageOpen(
            page,
            currentDpi,
            targetDpi,
            restoreView,
            fitAfter,
            _rasterSheetQualityRestoreVersion);
    }

    private bool ShouldDeferMissingResponsiveRasterSheetDpiForPageOpen(
        RasterSheetSource? rasterSheet,
        ViewState? restoreView,
        bool fitAfter)
    {
        if (rasterSheet?.Enabled != true)
            return false;

        return PageOpenZoomForRasterSheet(rasterSheet, restoreView, fitAfter) <
               ViewportRenderPolicy.RasterSheetDisplayMinZoom;
    }

    private int SelectReadyRasterSheetDpiForPageOpen(PageInfo page, int targetDpi, int currentDpi)
    {
        if (targetDpi <= 0 || currentDpi <= 0)
            return 0;

        if (!TryGetRememberedReadyRasterSheetSource(page, targetDpi, out _))
            return 0;
        if (targetDpi > ViewportRenderPolicy.RasterSheetPageOpenImmediateWarmMaxDpi ||
            targetDpi > ViewportRenderPolicy.RasterSheetDisplayMaxDpi)
        {
            return 0;
        }

        return targetDpi;
    }

    private bool QueueRasterSheetDpiBuildForPageOpen(
        PageInfo page,
        int currentDpi,
        int targetDpi,
        ViewState? restoreView,
        bool fitAfter,
        int navigationVersion)
    {
        if (targetDpi <= 0 ||
            targetDpi > ViewportRenderPolicy.RasterSheetDisplayMaxDpi ||
            targetDpi == currentDpi ||
            currentDpi > targetDpi &&
            !ViewportRenderPolicy.ShouldPreferLowerRasterSheetDpi(currentDpi, targetDpi) ||
            string.IsNullOrWhiteSpace(page.FolderPath) ||
            string.IsNullOrWhiteSpace(page.PdfPath))
        {
            return false;
        }

        if (TryGetRememberedReadyRasterSheetSource(page, targetDpi, out _))
        {
            return QueueReadyRasterSheetDpiApplyAfterWarmupForPageOpen(
                page,
                targetDpi,
                restoreView,
                fitAfter,
                navigationVersion);
        }

        string rebuildKey = $"{RasterSheetDpiUpgradeKey(page.PdfPath, page.PdfPage, page.FolderPath, targetDpi)}|open";
        lock (_rasterSheetRebuildGate)
        {
            if (!_rasterSheetRebuildsInFlight.Add(rebuildKey))
                return true;
        }

        AppLog.Info(
            $"Viewport raster DPI page open build queued; dpi={targetDpi}; currentDpi={currentDpi}; " +
            $"page='{_pageFolder}'; pdf='{Path.GetFileName(_pdfPath)}'; pdfPage={_pdfIndex + 1}");
        PostStatus($"Raster sheet preparing {targetDpi} DPI: {Path.GetFileName(_pdfPath)}  page {_pdfIndex + 1}");
        _ = BuildRasterSheetDpiUpgradeForCurrentPageAsync(
            rebuildKey,
            page,
            targetDpi,
            restoreView,
            fitAfter,
            navigationVersion);
        return true;
    }

    private bool QueueReadyRasterSheetDpiApplyAfterWarmupForPageOpen(
        PageInfo page,
        int targetDpi,
        ViewState? restoreView,
        bool fitAfter,
        int navigationVersion)
    {
        return QueueReadyRasterSheetDpiApplyAfterWarmup(
            page,
            targetDpi,
            restoreView,
            fitAfter,
            navigationVersion);
    }

    private bool TryGetPageOpenRasterSheetDpiApplyView(
        ViewState? restoreView,
        bool fitAfter,
        int navigationVersion,
        out ViewState? applyView,
        out bool applyFitAfter)
    {
        if (navigationVersion <= 0 || navigationVersion == _rasterSheetQualityRestoreVersion)
        {
            applyView = restoreView;
            applyFitAfter = fitAfter;
            return true;
        }

        if (_zoom >= ViewportRenderPolicy.RasterSheetDisplayMinZoom)
        {
            applyView = null;
            applyFitAfter = false;
            return false;
        }

        applyView = CaptureViewState();
        applyFitAfter = false;
        return true;
    }
}
