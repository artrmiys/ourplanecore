using System;
using System.IO;
using System.Threading.Tasks;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private bool QueuePreparedReadyRasterSheetDpiApplyFromMemory(
        PageInfo page,
        int targetDpi,
        ViewState? restoreView,
        bool fitAfter,
        bool postStatus,
        string sourceKind)
    {
        if (targetDpi <= 0 ||
            string.IsNullOrWhiteSpace(page.FolderPath) ||
            string.IsNullOrWhiteSpace(page.PdfPath))
        {
            return false;
        }

        float targetScale = RasterSheetCacheService.RasterDpiToRenderScale(targetDpi);
        if (!RasterSheetCacheService.TryGetReadyReadableRasterSource(page, targetScale, out RasterSheetSource? readySource) ||
            readySource == null)
        {
            return false;
        }

        string applyKey = $"{RasterSheetDpiUpgradeKey(page.PdfPath, page.PdfPage, page.FolderPath, targetDpi)}|prepared-memory";
        lock (_rasterSheetRebuildGate)
        {
            if (!_rasterSheetRebuildsInFlight.Add(applyKey))
                return true;
        }

        _ = ApplyPreparedReadyRasterSheetDpiFromMemoryAsync(
            applyKey,
            page,
            readySource.Clone(),
            targetDpi,
            restoreView,
            fitAfter,
            postStatus,
            sourceKind);
        return true;
    }

    private async Task ApplyPreparedReadyRasterSheetDpiFromMemoryAsync(
        string applyKey,
        PageInfo queuedPage,
        RasterSheetSource readySource,
        int targetDpi,
        ViewState? restoreView,
        bool fitAfter,
        bool postStatus,
        string sourceKind)
    {
        RasterSheetBitmapResult preparedBitmap = new(new SKBitmap(), 0, 0, 0, "");
        bool hasPreparedBitmap = false;
        bool preparedBitmapApplied = false;
        try
        {
            hasPreparedBitmap = await Task.Run(() => TryPrepareRasterSheetBitmapForUiApply(
                    queuedPage,
                    readySource,
                    preferOverview: false,
                    out preparedBitmap))
                .ConfigureAwait(false);
            if (!hasPreparedBitmap)
            {
                AppLog.Warn(
                    $"Viewport prepared raster DPI memory apply skipped; dpi={targetDpi}; reason='bitmap prepare failed'; " +
                    $"page='{queuedPage.FolderPath}'; pdf='{Path.GetFileName(queuedPage.PdfPath)}'; pdfPage={queuedPage.PdfPage + 1}");
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (!IsCurrentPageRasterTarget(queuedPage.PdfPath, queuedPage.PdfPage, queuedPage.FolderPath) ||
                    _pdfLayersLoadedForPage ||
                    _usingLayerRenderer ||
                    !ShouldApplyPreparedReadyRasterSheetDpi(targetDpi))
                {
                    return;
                }

                if (ApplyPreparedRasterSheetDpiUpgradeResult(
                        readySource,
                        preparedBitmap,
                        targetDpi,
                        sourceKind,
                        restoreView,
                        fitAfter,
                        postStatus))
                {
                    preparedBitmapApplied = true;
                }
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport prepared raster DPI memory apply crashed for {queuedPage.Name}");
        }
        finally
        {
            if (hasPreparedBitmap && !preparedBitmapApplied)
                preparedBitmap.Bitmap.Dispose();

            lock (_rasterSheetRebuildGate)
                _rasterSheetRebuildsInFlight.Remove(applyKey);
        }
    }

    private bool ShouldApplyPreparedReadyRasterSheetDpi(int targetDpi)
    {
        int currentTargetDpi = TargetRasterSheetDpiForCurrentZoom();
        if (currentTargetDpi <= 0)
            return false;

        if (targetDpi == currentTargetDpi)
            return true;

        if (targetDpi <= ViewportRenderPolicy.RasterSheetNavigationMaxDpi &&
            (_isFastNavigating || ShouldHoldHeavyRasterSheetDpiAfterMotion(currentTargetDpi)))
        {
            return true;
        }

        return false;
    }

    private bool ApplyPreparedRasterSheetDpiUpgradeResult(
        RasterSheetSource source,
        RasterSheetBitmapResult preparedBitmap,
        int targetDpi,
        string sourceKind,
        ViewState? restoreView,
        bool fitAfter,
        bool postStatus = true)
    {
        RasterSheetSource candidate = source.Clone();
        if (!ApplyRasterSheetBitmapRender(
                _pdfPath,
                _pdfIndex,
                _pageFolder,
                candidate,
                preparedBitmap,
                restoreView,
                fitAfter,
                usingOverview: false))
        {
            return false;
        }

        _rasterSheetSource = candidate.Clone();
        if (postStatus)
            PostStatus($"Raster sheet {targetDpi} DPI: {Path.GetFileName(_pdfPath)}  page {_pdfIndex + 1}");
        AppLog.Info(
            $"Viewport raster DPI upgrade applied; source='{sourceKind}'; dpi={targetDpi}; " +
            $"page='{_pageFolder}'; pdf='{Path.GetFileName(_pdfPath)}'; pdfPage={_pdfIndex + 1}");
        RequestRepaint();
        return true;
    }
}
