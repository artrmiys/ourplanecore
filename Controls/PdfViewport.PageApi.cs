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

    public bool IsPdfLayerTraceEnabled => _pdfLayerTraceEnabled;
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
        _zoom = Math.Clamp(state.Zoom, ZoomMin, ZoomMax);
        _panX = state.PanX;
        _panY = state.PanY;
        ClampPanToPage();
        ScheduleRerenderForZoom(force: false);
        RequestRepaint();
    }

    public void LoadPage(
        string pdfPath,
        int pageIndex = 0,
        string pageFolder = "",
        IReadOnlyList<PdfLayerInfo>? cachedLayers = null,
        ViewState? restoreView = null)
    {
        _pdfPath    = pdfPath;
        _pdfIndex   = pageIndex;
        _pageFolder = pageFolder;
        _cachedLayers = cachedLayers;

        bool hadVisibleBitmap = _pageBitmap != null;
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
        _activePdfLayerTraceLayer = null;
        _activePdfLayerTraceLayerName = "";
        ClearPdfLayerTraceSession();
        ResetPdfSnapCache();
        _usingLayerRenderer = false;
        _pendingLayerRender = null;
        _layerRenderVersion++;
        _pendingDocnetRender = null;
        _docnetRenderVersion++;
        QueuePdfSnapPointLoad(force: true);

        string loadedStatus = $"Loaded: {Path.GetFileName(pdfPath)}  page {pageIndex + 1}";
        QueueLayerRender(
            resetLayerStates: true,
            renderScale: ViewportRenderPolicy.InstantPagePreviewRenderScale,
            statusAfter: loadedStatus,
            fireLayersAfter: true,
            restoreView: restoreView,
            fitAfter: !restoreView.HasValue);

        PostStatus($"Rendering: {Path.GetFileName(pdfPath)}  page {pageIndex + 1}");
        RequestRepaint();

        // Fire layers event
        FireLayersChanged();
    }

}
