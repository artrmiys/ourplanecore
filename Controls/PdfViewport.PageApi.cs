using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Docnet.Core;
using Docnet.Core.Models;
using OurPlanCore;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    public readonly record struct ViewState(float Zoom, float PanX, float PanY);

    public ViewState CaptureViewState() => new(_zoom, _panX, _panY);
    public bool IsPageRenderReady(string pageFolder) =>
        _pageBitmap != null &&
        _bitmapScale > 0 &&
        _pdfW > 0 &&
        _pdfH > 0 &&
        !_showingPreviousPageDuringSwitch &&
        string.Equals(_pageFolder, pageFolder, StringComparison.OrdinalIgnoreCase);

    public bool IsPagePaintReady(string pageFolder) =>
        IsPageRenderReady(pageFolder) &&
        _pageBitmapGeneration > 0 &&
        _pagePaintGeneration == _pageBitmapGeneration &&
        string.Equals(_pagePaintedPageFolder, pageFolder, StringComparison.OrdinalIgnoreCase);

    public bool IsPageDetailRenderReady(string pageFolder) =>
        IsPageRenderReady(pageFolder) &&
        (IsStaticRasterDisplayActive() ||
         !ViewportRenderPolicy.ShouldUseDetailRender(_zoom, _bitmapScale) ||
         DetailRenderCoversVisibleViewForPaint()) &&
        string.Equals(_pageFolder, pageFolder, StringComparison.OrdinalIgnoreCase);

    public bool IsPdfLayerTraceEnabled => _pdfLayerTraceEnabled;
    public bool ArePdfLayersLoaded => _pdfLayersLoadedForPage;
    public bool CanApplyPdfLayerTrace => _pdfLayerTraceEnabled && _pdfLayerTraceReadyToApply;
    public string PdfLayerTraceModeTitle => LayerTraceModeTitle(_pdfLayerTraceMode);
    public string ActivePdfLayerTraceLayerName => _activePdfLayerTraceLayerName;
    public string PdfLayerTracePhaseTitle =>
        _pdfLayerTraceChoosingLayer ? "Choose Layer" :
        _pdfLayerTraceReadyToApply ? "Ready" :
        _pdfLayerTraceCandidates.Count > 0 ? "Hover" :
        "Probe";

    public bool TryRebindCurrentPageFolder(string oldPageFolder, string newPageFolder, string pdfPath, int pdfIndex)
    {
        if (!string.Equals(_pageFolder, oldPageFolder, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(_pdfPath, pdfPath, StringComparison.OrdinalIgnoreCase) ||
            _pdfIndex != pdfIndex)
        {
            return false;
        }

        _pageFolder = newPageFolder;
        if (string.Equals(_sheetOverlayTargetPageFolder, oldPageFolder, StringComparison.OrdinalIgnoreCase))
            _sheetOverlayTargetPageFolder = newPageFolder;
        if (_pageBitmapPdfIndex == pdfIndex &&
            string.Equals(_pageBitmapPdfPath, pdfPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_pageBitmapPageFolder, oldPageFolder, StringComparison.OrdinalIgnoreCase))
        {
            _pageBitmapPageFolder = newPageFolder;
        }

        if (string.Equals(_pagePaintedPageFolder, oldPageFolder, StringComparison.OrdinalIgnoreCase))
            _pagePaintedPageFolder = newPageFolder;

        return true;
    }

    public void RestoreViewState(ViewState state)
    {
        bool hasVisiblePage = _pageBitmap != null && !_showingPreviousPageDuringSwitch;
        bool changedView =
            Math.Abs(state.Zoom - _zoom) > 0.001f ||
            Math.Abs(state.PanX - _panX) * Math.Max(_zoom, 0.001f) > 2f ||
            Math.Abs(state.PanY - _panY) * Math.Max(_zoom, 0.001f) > 2f;
        if (hasVisiblePage && changedView)
            BeginFastNavigation();

        _zoom = Math.Clamp(state.Zoom, ZoomMin, ZoomMax);
        _panX = state.PanX;
        _panY = state.PanY;
        ClampPanToPage();
        ScheduleRerenderForZoom(force: false);
        MaybeRequestSheetOverlayRenderScaleRefresh();
        RequestRepaint();
        NotifyZoomChanged();
    }

    public void LoadPage(
        string pdfPath,
        int pageIndex = 0,
        string pageFolder = "",
        IReadOnlyList<PdfLayerInfo>? cachedLayers = null,
        ViewState? restoreView = null,
        RasterSheetSource? rasterSheet = null,
        bool hasSheetOverlayConfigured = false)
    {
        if (!IsSamePageFolder(_pageFolder, pageFolder))
            StopRepeatDrawing();
        CancelExtraJoistPlacement(postStatus: false);
        CancelTransientNavigationRenderWork();
        BeginFastNavigation();
        BeginPageSwitchDetailRenderHold();

        _pdfPath    = pdfPath;
        _pdfIndex   = pageIndex;
        _pageFolder = pageFolder;
        _cachedLayers = cachedLayers;
        _rasterSheetSource = rasterSheet?.Clone();
        _usingRasterSheetRender = false;
        _usingRasterSheetOverviewRender = false;
        ClearRasterSheetVisualSegments();

        bool hadVisibleBitmap = _pageBitmap != null;
        bool hadCurrentPageBitmap = IsPageBitmapFor(pdfPath, pageIndex, pageFolder);
        _showingPreviousPageDuringSwitch = hadVisibleBitmap;
        ClearSheetOverlay();
        _drawPts.Clear();
        _scalePts.Clear();
        _rubberEnd = null;
        _boxSelecting = false;
        _boxVertexMode = false;
        SetSnapPreview(null);
        _aiMarkers.Clear();
        ClearThreeDRoofGuides();
        _annotations.Clear();
        _sheetLegendEntries.Clear();
        ClearViewportUndoStack();
        ClearAiActionDraftPreview();
        ClearSelection();
        _layerStates.Clear();
        _highlightedLayers.Clear();
        _layers.Clear();
        _pdfLayersLoadedForPage = false;
        _activePdfLayerTraceLayer = null;
        _activePdfLayerTraceLayerName = "";
        ClearPdfLayerTraceSession();
        ResetPdfSnapCache();
        _usingLayerRenderer = false;
        _pendingLayerRender = null;
        _layerRenderVersion++;
        _pendingDocnetRender = null;
        _docnetRenderVersion++;
        _persistedPreviewRenderVersion++;
        ClearDetailRender();
        QueueDetailRenderDocPrewarm();
        QueuePdfSnapPointLoad(force: true);
        FireLayersChanged();

        string loadedStatus = $"Loaded: {Path.GetFileName(pdfPath)}  page {pageIndex + 1}";
        string rasterSkipReason = "";
        bool rasterBitmapWarmupQueuedForOpen = false;
        bool rasterWorkZoomWarmupQueuedForOpen = false;
        bool responsiveRasterDpiWorkQueuedForOpen = false;
        bool rasterFirstForOpen = RasterSheetCacheService.UseAsPageOpenRaster(rasterSheet);
        bool shouldUseRasterSheetForOpen = ShouldUseRasterSheetForPageOpen(rasterSheet, restoreView, fitAfter: !restoreView.HasValue);
        bool responsiveRasterDpiForOpen = ShouldUseResponsiveRasterSheetDpiForPageOpen(
            rasterSheet,
            restoreView,
            fitAfter: !restoreView.HasValue);
        bool skipOversizedRasterSheetForOpen =
            responsiveRasterDpiForOpen &&
            ShouldSkipOversizedRasterSheetForPageOpen(rasterSheet, restoreView, fitAfter: !restoreView.HasValue);
        // Never put the low-resolution source-image overview on screen. A cold
        // sheet keeps the previous sharp frame until its full bitmap is ready;
        // nearby sheets are warmed ahead of navigation.
        const bool preferRasterOverviewForOpen = false;
        if (shouldUseRasterSheetForOpen)
        {
            QueueRasterSheetWorkZoomWarmupForPageOpen(
                pdfPath,
                pageIndex,
                pageFolder,
                rasterSheet,
                allowLowZoomFullRasterApply: false,
                // Static mode pins one fixed DPI and never switches DPI on zoom, so
                // building the 72/100/144 zoom-ladder variants (a python render per
                // missing DPI on every page open) is pure churn — warm only what
                // already exists. The single chosen-DPI build is handled separately
                // by QueueStaticRasterDpiApplyIfNeeded.
                buildMissingDpis:
                    !ViewportRenderPolicy.StaticRasterModeEnabled &&
                    !RasterSheetCacheService.IsRasterDpiPinned(rasterSheet),
                out rasterWorkZoomWarmupQueuedForOpen);
        }

        if (responsiveRasterDpiForOpen)
        {
            responsiveRasterDpiWorkQueuedForOpen = TryApplyReadyResponsiveRasterSheetDpiForPageOpen(
                rasterSheet,
                restoreView,
                fitAfter: !restoreView.HasValue);
            if (!responsiveRasterDpiWorkQueuedForOpen)
            {
                responsiveRasterDpiWorkQueuedForOpen = QueueResponsiveRasterSheetDpiBuildForPageOpen(
                    rasterSheet,
                    restoreView,
                    fitAfter: !restoreView.HasValue);
            }
        }

        if (shouldUseRasterSheetForOpen &&
            !responsiveRasterDpiWorkQueuedForOpen &&
            !skipOversizedRasterSheetForOpen &&
            TryApplyRasterSheetRender(
                pdfPath,
                pageIndex,
                pageFolder,
                rasterSheet,
                restoreView,
                fitAfter: !restoreView.HasValue,
                preferRasterOverviewForOpen,
                requireCachedBitmap: !rasterFirstForOpen,
                out rasterSkipReason))
        {
            PostStatus($"Raster sheet: {Path.GetFileName(pdfPath)}  page {pageIndex + 1}");
            QueueRasterSheetSelfHealIfNeeded(
                pdfPath,
                pageIndex,
                pageFolder,
                cachedLayers,
                rasterSheet,
                rasterSkipReason);
            RequestRepaint();
            return;
        }
        if (shouldUseRasterSheetForOpen &&
            !skipOversizedRasterSheetForOpen &&
            IsRasterSheetBitmapCacheWarmingReason(rasterSkipReason))
        {
            rasterBitmapWarmupQueuedForOpen = QueueRasterSheetBitmapApplyAfterWarmup(
                pdfPath,
                pageIndex,
                pageFolder,
                rasterSheet,
                preferRasterOverviewForOpen);
        }
        if (!shouldUseRasterSheetForOpen)
        {
            rasterBitmapWarmupQueuedForOpen = QueueRasterSheetWorkZoomWarmupForPageOpen(
                pdfPath,
                pageIndex,
                pageFolder,
                rasterSheet,
                allowLowZoomFullRasterApply: hasSheetOverlayConfigured,
                buildMissingDpis:
                    hasSheetOverlayConfigured &&
                    !RasterSheetCacheService.IsRasterDpiPinned(rasterSheet),
                out rasterWorkZoomWarmupQueuedForOpen);
        }
        if (!shouldUseRasterSheetForOpen &&
            RasterSheetCacheService.ShouldRebuildForReadableDisplay(
                pageFolder,
                pdfPath,
                rasterSheet,
                out string deferredRebuildReason))
        {
            rasterSkipReason = deferredRebuildReason;
        }

        float previewScale = PageSwitchPreviewRenderScale(restoreView, fitAfter: !restoreView.HasValue);
        bool preserveSharpSourceFrame =
            shouldUseRasterSheetForOpen &&
            RasterSheetCacheService.IsSourceImageRaster(rasterSheet);
        bool previewCacheHit = !preserveSharpSourceFrame && TryApplyPersistedPreviewRender(
            pdfPath,
            pageIndex,
            previewScale,
            restoreView,
            fitAfter: !restoreView.HasValue,
            allowDiskRead: false);
        if (!previewCacheHit && !preserveSharpSourceFrame)
        {
            QueuePersistedPreviewRenderAfterFirstRepaint(
                pdfPath,
                pageIndex,
                pageFolder,
                previewScale,
                restoreView,
                fitAfter: !restoreView.HasValue,
                queueSharpBaseAfterPreview: !rasterBitmapWarmupQueuedForOpen &&
                                            !responsiveRasterDpiWorkQueuedForOpen &&
                                            !rasterWorkZoomWarmupQueuedForOpen,
                statusAfter: loadedStatus);
        }

        if (previewCacheHit)
        {
            PostStatus(loadedStatus);
            if (!rasterBitmapWarmupQueuedForOpen &&
                !responsiveRasterDpiWorkQueuedForOpen &&
                !rasterWorkZoomWarmupQueuedForOpen)
            {
                QueueSharpBaseRenderAfterPreview(pdfPath, pageIndex, pageFolder);
            }
        }
        else if (rasterBitmapWarmupQueuedForOpen)
        {
            if (hadCurrentPageBitmap)
                _showingPreviousPageDuringSwitch = false;
        }
        else
        {
            if (hadCurrentPageBitmap && _bitmapScale >= previewScale * 0.95f)
                _showingPreviousPageDuringSwitch = false;
            else
                ClearPreviousPageBitmapDuringSwitch();

            QueueDocnetRender(
                PageSwitchLivePreviewScale(restoreView, fitAfter: !restoreView.HasValue),
                restoreView,
                fitAfter: !restoreView.HasValue,
                queueLayerAfter: false,
                resetLayerStates: false,
                statusAfter: loadedStatus,
                fireLayersAfter: false);
        }

        PostStatus((rasterBitmapWarmupQueuedForOpen || responsiveRasterDpiWorkQueuedForOpen) && !previewCacheHit
            ? $"Raster preparing: {Path.GetFileName(pdfPath)}  page {pageIndex + 1}"
            : previewCacheHit
            ? $"Cached preview: {Path.GetFileName(pdfPath)}  page {pageIndex + 1}"
            : $"Rendering: {Path.GetFileName(pdfPath)}  page {pageIndex + 1}");
        QueueRasterSheetSelfHealIfNeeded(
            pdfPath,
            pageIndex,
            pageFolder,
            cachedLayers,
            rasterSheet,
            rasterSkipReason);
        // Static mode: a raster-less page (older jobs, never-rastered pages) just
        // rendered via the live path — build its raster once and pin it, so it too
        // becomes a static image instead of blurry + re-rendering on zoom.
        QueueStaticRasterLazyBuildIfNeeded(pdfPath, pageIndex, pageFolder, rasterSheet);
        RequestRepaint();
    }

    private void ClearPreviousPageBitmapDuringSwitch()
    {
        _pageBitmap?.Dispose();
        _pageBitmap = null;
        _usingRasterSheetRender = false;
        _usingRasterSheetOverviewRender = false;
        ClearRasterSheetVisualSegments();
        ClearPageBitmapIdentity();
        _pdfW = 0;
        _pdfH = 0;
        _bitmapScale = 0;
        _renderedScale = 0;
    }

    private void CancelTransientNavigationRenderWork()
    {
        _zoomRerenderTimer.Stop();
        _navigationIdleTimer.Stop();
        _zoomRerenderForce = false;
        _isFastNavigating = false;
        _renderNavigationFastFrame = false;
    }

}
