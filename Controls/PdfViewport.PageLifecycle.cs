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
    public void ClearPage()
    {
        _pdfPath = "";
        _pdfIndex = 0;
        _pageFolder = "";
        _cachedLayers = null;
        _rasterSheetSource = null;
        _zoomRerenderTimer.Stop();
        _navigationIdleTimer.Stop();
        _zoomRerenderForce = false;
        _isFastNavigating = false;
        _renderNavigationFastFrame = false;
        _pendingLayerRender = null;
        _layerRenderVersion++;
        _pendingDocnetRender = null;
        _docnetRenderVersion++;
        _pdfLayerTraceEnabled = false;
        _activePdfLayerTraceLayer = null;
        _activePdfLayerTraceLayerName = "";
        ClearPdfLayerTraceSession();
        ResetPdfSnapCache();
        _pageBitmap?.Dispose();
        _pageBitmap = null;
        ClearPageBitmapIdentity();
        ClearSheetOverlay();
        _pdfW = _pdfH = 0;
        _drawPts.Clear();
        _scalePts.Clear();
        _rubberEnd = null;
        _boxSelecting = false;
        _boxVertexMode = false;
        _aiActionDraftPreview = null;
        _aiActionDraftPreviewPage = "";
        _aiMarkers.Clear();
        _annotations.Clear();
        _sheetLegendEntries.Clear();
        ClearViewportUndoStack();
        CancelJoistDirectionCapture();
        ClearSelection();
        PublishTransformSelectionChanged();
        RequestRepaint();
        FireLayersChanged();
        PublishPdfLayerTraceState();
    }

    public int GetPageCount(string pdfPath)
    {
        try
        {
            using var docReader = _docLib.GetDocReader(pdfPath, new PageDimensions(1.0));
            return docReader.GetPageCount();
        }
        catch { return 0; }
    }
}
