using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace OurPlaneCore.Controls;

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
        if (!ShouldSelfHealRasterSheet(rasterSheet, rasterSkipReason) ||
            string.IsNullOrWhiteSpace(pageFolder) ||
            string.IsNullOrWhiteSpace(pdfPath) ||
            !Directory.Exists(pageFolder) ||
            !File.Exists(pdfPath))
        {
            return;
        }

        string rebuildKey = RasterSheetRebuildKey(pdfPath, pageIndex, pageFolder);
        lock (_rasterSheetRebuildGate)
        {
            if (!_rasterSheetRebuildsInFlight.Add(rebuildKey))
                return;
        }

        AppLog.Info(
            $"Viewport raster sheet self-heal queued; reason='{rasterSkipReason}'; " +
            $"page='{pageFolder}'; pdf='{Path.GetFileName(pdfPath)}'; pdfPage={pageIndex + 1}");
        _ = RebuildRasterSheetForCurrentPageAsync(
            rebuildKey,
            pdfPath,
            pageIndex,
            pageFolder,
            cachedLayers,
            rasterSkipReason);
    }

    private async Task RebuildRasterSheetForCurrentPageAsync(
        string rebuildKey,
        string pdfPath,
        int pageIndex,
        string pageFolder,
        IReadOnlyList<PdfLayerInfo>? cachedLayers,
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
            };

            RasterSheetBuildResult result = await Task.Run(() => RasterSheetCacheService.BuildAndEnable(page));
            if (!result.Ok || result.Source == null)
            {
                AppLog.Warn(
                    $"Viewport raster sheet self-heal failed; reason='{rasterSkipReason}'; " +
                    $"error='{result.Error}'; page='{pageFolder}'; pdf='{Path.GetFileName(pdfPath)}'; pdfPage={pageIndex + 1}");
                return;
            }

            AppLog.Info(
                $"Viewport raster sheet self-heal built; reason='{rasterSkipReason}'; " +
                $"page='{pageFolder}'; pdf='{Path.GetFileName(pdfPath)}'; pdfPage={pageIndex + 1}");

            if (!IsCurrentPageRasterTarget(pdfPath, pageIndex, pageFolder))
                return;

            _rasterSheetSource = result.Source.Clone();
            if (_pdfLayersLoadedForPage || _usingLayerRenderer || !ShouldUseRasterSheetForCurrentZoom())
                return;

            ViewState currentView = CaptureViewState();
            if (TryApplyRasterSheetRender(
                    pdfPath,
                    pageIndex,
                    pageFolder,
                    result.Source,
                    currentView,
                    fitAfter: false,
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

    private bool ShouldUseRasterSheetForPageOpen(ViewState? restoreView, bool fitAfter)
    {
        if (restoreView.HasValue)
            return restoreView.Value.Zoom >= ViewportRenderPolicy.RasterSheetDisplayMinZoom;

        return !fitAfter && ShouldUseRasterSheetForCurrentZoom();
    }

    private bool ShouldUseRasterSheetForCurrentZoom() =>
        _zoom >= ViewportRenderPolicy.RasterSheetDisplayMinZoom;

    private bool TryApplyReadyRasterSheetForCurrentZoom()
    {
        if (!ShouldUseRasterSheetForCurrentZoom() ||
            _rasterSheetSource?.Enabled != true ||
            _pdfLayersLoadedForPage ||
            _usingLayerRenderer)
        {
            return false;
        }

        ViewState currentView = CaptureViewState();
        if (TryApplyRasterSheetRender(
                _pdfPath,
                _pdfIndex,
                _pageFolder,
                _rasterSheetSource,
                currentView,
                fitAfter: false,
                out string rasterSkipReason))
        {
            PostStatus($"Raster sheet: {Path.GetFileName(_pdfPath)}  page {_pdfIndex + 1}");
            RequestRepaint();
            return true;
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

    private bool TrySwitchRasterSheetToFastPreviewForLowZoom()
    {
        if (!_usingRasterSheetRender ||
            _zoom >= ViewportRenderPolicy.RasterSheetDisplayExitZoom)
        {
            return false;
        }

        ViewState currentView = CaptureViewState();
        if (TryApplyPersistedPreviewRender(
                _pdfPath,
                _pdfIndex,
                ViewportRenderPolicy.FastPageSwitchPreviewRenderScale,
                currentView,
                fitAfter: false))
        {
            PostStatus($"Fast preview: {Path.GetFileName(_pdfPath)}  page {_pdfIndex + 1}");
            RequestRepaint();
            return true;
        }

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

    private static string RasterSheetRebuildKey(string pdfPath, int pageIndex, string pageFolder) =>
        $"{Path.GetFullPath(pdfPath)}|{pageIndex}|{Path.GetFullPath(pageFolder)}";
}
