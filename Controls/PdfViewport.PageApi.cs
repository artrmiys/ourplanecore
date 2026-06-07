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
using OurPlaneCore;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace OurPlaneCore.Controls;

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
    }

    public void LoadPage(
        string pdfPath,
        int pageIndex = 0,
        string pageFolder = "",
        IReadOnlyList<PdfLayerInfo>? cachedLayers = null,
        ViewState? restoreView = null,
        RasterSheetSource? rasterSheet = null)
    {
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
        ClearDetailRender();
        QueuePdfSnapPointLoad(force: true);
        FireLayersChanged();

        string loadedStatus = $"Loaded: {Path.GetFileName(pdfPath)}  page {pageIndex + 1}";
        string rasterSkipReason = "";
        bool shouldUseRasterSheetForOpen = ShouldUseRasterSheetForPageOpen(rasterSheet, restoreView, fitAfter: !restoreView.HasValue);
        bool responsiveRasterDpiForOpen =
            shouldUseRasterSheetForOpen &&
            restoreView.HasValue &&
            ShouldUseResponsiveRasterSheetDpiForView(rasterSheet, restoreView);
        bool preferRasterOverviewForOpen = ShouldUseRasterSheetOverviewForPageOpen(rasterSheet, restoreView, fitAfter: !restoreView.HasValue);
        if (responsiveRasterDpiForOpen &&
            TryApplyReadyResponsiveRasterSheetDpiForPageOpen(
                rasterSheet,
                restoreView,
                fitAfter: !restoreView.HasValue))
        {
            PostStatus($"Raster sheet: {Path.GetFileName(pdfPath)}  page {pageIndex + 1}");
            RequestRepaint();
            return;
        }
        if (shouldUseRasterSheetForOpen &&
            !responsiveRasterDpiForOpen &&
            TryApplyRasterSheetRender(
                pdfPath,
                pageIndex,
                pageFolder,
                rasterSheet,
                restoreView,
                fitAfter: !restoreView.HasValue,
                preferRasterOverviewForOpen,
                requireCachedBitmap: true,
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
            !responsiveRasterDpiForOpen &&
            IsRasterSheetBitmapCacheWarmingReason(rasterSkipReason))
        {
            QueueRasterSheetBitmapApplyAfterWarmup(
                pdfPath,
                pageIndex,
                pageFolder,
                rasterSheet,
                preferRasterOverviewForOpen);
        }
        if (!shouldUseRasterSheetForOpen)
        {
            QueueRasterSheetWorkZoomWarmupForPageOpen(
                pdfPath,
                pageIndex,
                pageFolder,
                rasterSheet);
        }
        if (responsiveRasterDpiForOpen)
        {
            QueueResponsiveRasterSheetDpiBuildForPageOpen(rasterSheet, restoreView);
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

        float previewScale = ViewportRenderPolicy.FastPageSwitchPreviewRenderScale;
        bool previewCacheHit = TryApplyPersistedPreviewRender(
            pdfPath,
            pageIndex,
            previewScale,
            restoreView,
            fitAfter: !restoreView.HasValue);

        if (previewCacheHit)
        {
            PostStatus(loadedStatus);
            QueueSharpBaseRenderAfterPreview(pdfPath, pageIndex, pageFolder);
        }
        else
        {
            if (hadCurrentPageBitmap && _bitmapScale >= ViewportRenderPolicy.FastPageSwitchPreviewRenderScale * 0.95f)
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

        PostStatus(previewCacheHit
            ? $"Cached preview: {Path.GetFileName(pdfPath)}  page {pageIndex + 1}"
            : $"Rendering: {Path.GetFileName(pdfPath)}  page {pageIndex + 1}");
        QueueRasterSheetSelfHealIfNeeded(
            pdfPath,
            pageIndex,
            pageFolder,
            cachedLayers,
            rasterSheet,
            rasterSkipReason);
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
