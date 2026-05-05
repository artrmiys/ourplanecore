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
using SmartTakeoffs;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace SmartTakeoffs.Controls;

// ── Simple layer info returned to the UI ─────────────────────────────────────

public sealed record PdfLayer(int Number, string Name, bool IsOn, bool IsHighlighted = false);
public sealed record SheetLegendEntry(
    string Color,
    string Name,
    string Quantity,
    string Type,
    string Sign,
    IReadOnlyList<string>? Details = null);
public sealed record ViewportContextRequest(
    double ScreenX,
    double ScreenY,
    float PdfX,
    float PdfY,
    string PageFolder,
    Measurement? Measurement);

// ── Tool enum ────────────────────────────────────────────────────────────────

public enum ViewerTool { Pan, Select, Scale, Ruler, DrawLine, DrawArrow, DrawRect, Point, Line, Area }
public enum PdfLayerTraceMode { Full, Edge, Point, AllEdges }

// ── Main control ─────────────────────────────────────────────────────────────

public sealed partial class PdfViewport : SKElement
{
    // ── PDF / render state ────────────────────────────────────────────────────
    private static readonly IDocLib _docLib = DocLib.Instance;

    private string  _pdfPath  = "";
    private int     _pdfIndex = 0;

    private SKBitmap? _pageBitmap;
    private float     _pdfW, _pdfH;        // page size in PDF points (1pt = 1/72 in)
    private float     _bitmapScale;         // bitmap pixels per PDF point
    private float     _renderedScale;

    // ── Viewport transform ────────────────────────────────────────────────────
    private float _zoom = 1f;              // screen pixels per PDF point
    private float _panX, _panY;            // PDF-point coord of top-left of screen

    // ── Pan drag ──────────────────────────────────────────────────────────────
    private Point? _dragStart;
    private float  _dragPanX0, _dragPanY0;
    private Point? _rightClickStart;
    private SKPoint? _rightClickPdf;
    private Measurement? _rightClickMeasurement;
    private bool _rightClickMoved;

    // ── Drawing tools ─────────────────────────────────────────────────────────
    private ViewerTool              _tool       = ViewerTool.Select;
    private readonly List<SKPoint>  _drawPts    = [];   // in-progress PDF-space points
    private SKPoint?                _rubberEnd;          // rubber-band endpoint
    private SKPoint?                _snapPreview;
    private SKPoint?                _lastPointerPdf;
    private bool                    _boxSelecting;
    private SKPoint                 _boxSelectStartPdf;
    private SKPoint                 _boxSelectEndPdf;
    private bool                    _boxSelectAdditive;

    // Scale calibration
    private readonly List<SKPoint> _scalePts = [];
    private SmartAiActionDraft? _aiActionDraftPreview;
    private string _aiActionDraftPreviewPage = "";
    private readonly List<SmartAiMarker> _aiMarkers = [];
    private readonly List<SheetLegendEntry> _sheetLegendEntries = [];
    public  double   ScaleMetersPerPt { get; set; } = 0.0;
    public  string   ActiveColor      { get; set; } = "#FF4444";
    public  string   ActiveTakeoffFolder { get; set; } = "";
    public  UnitMode UnitMode         { get; set; } = UnitMode.Imperial;
    public  string   ViewBackgroundColor { get; set; } = "#FFFFFF";
    public  bool     ShowMeasurementLabels { get; set; } = true;
    public  bool     ShowLineLabels { get; set; } = true;
    public  bool     ShowAreaLabels { get; set; } = true;
    public  bool     ShowCountLabels { get; set; }
    public  double   MeasurementLabelScale { get; set; } = 1.0;
    public  string   SheetLegendAnchor { get; set; } = "BottomLeft";
    public  double   SheetLegendScale { get; set; } = 1.0;
    public  double   SheetHeaderScale { get; set; } = 1.0;
    public  bool     ScaleSheetOverlaysWithPage { get; set; } = false;
    public  bool     ScaleMeasurementLabelsWithPage { get; set; } = false;
    public  bool     ScaleSheetHeaderWithPage { get; set; } = false;

    private bool _snapEnabled;
    public bool SnapEnabled
    {
        get => _snapEnabled;
        set
        {
            if (_snapEnabled == value)
                return;

            _snapEnabled = value;
            SetSnapPreview(null);
            SnapChanged?.Invoke(_snapEnabled);
            PostRecordPrompt();
        }
    }

    private bool _orthoEnabled;
    public bool OrthoEnabled
    {
        get => _orthoEnabled;
        set
        {
            if (_orthoEnabled == value)
                return;

            _orthoEnabled = value;
            OrthoChanged?.Invoke(_orthoEnabled);
            PostRecordPrompt();
        }
    }

    // ── Measurements ──────────────────────────────────────────────────────────
    private readonly List<Measurement> _measurements = [];
    private readonly List<PageAnnotation> _annotations = [];
    private string _pageFolder = "";
    private Measurement? _selectedMeasurement;
    private readonly HashSet<Measurement> _selectedMeasurements = [];
    private Measurement? _joistDirectionMeasurement;
    private readonly List<SKPoint> _joistDirectionPts = [];
    private SKPoint? _joistDirectionRubberEnd;
    private int _selectedVertexIndex = -1;
    private bool _draggingVertex;
    private bool _draggingMeasurement;
    private bool _dragMeasurementChanged;
    private Point _dragScreenStart;
    private SKPoint _dragVertexOriginalPoint;
    private List<SKPoint> _dragMeasurementOriginalPoints = [];
    private readonly Dictionary<Measurement, List<SKPoint>> _dragSelectionOriginalPoints = [];
    private readonly Dictionary<int, bool> _layerStates = [];
    private readonly HashSet<int> _highlightedLayers = [];
    private List<PdfLayer> _layers = [];
    private IReadOnlyList<PdfLayerInfo>? _cachedLayers;
    private bool _usingLayerRenderer;
    private bool _pdfLayerTraceEnabled;
    private PdfLayerTraceMode _pdfLayerTraceMode = PdfLayerTraceMode.Full;
    private int? _activePdfLayerTraceLayer;
    private string _activePdfLayerTraceLayerName = "";
    private List<PdfLayerProbeCandidate> _pdfLayerTraceCandidates = [];
    private int _pdfLayerTraceCandidateIndex;
    private bool _pdfLayerTraceChoosingLayer;
    private bool _pdfLayerTraceReadyToApply;
    private SKPoint? _pdfLayerTracePickPoint;
    private int? _pdfLayerTracePreviewLayer;
    private readonly System.Windows.Threading.DispatcherTimer _zoomRerenderTimer;
    private bool _zoomRerenderForce;
    private bool _repaintQueued;
    private bool _isViewDragging;
    private DateTime _lastPointerStatusAt = DateTime.MinValue;
    private readonly Dictionary<string, SKColor> _colorCache = new(StringComparer.OrdinalIgnoreCase);
    private LayerRenderRequest? _pendingLayerRender;
    private bool _layerRenderInProgress;
    private int _layerRenderVersion;

    private sealed record LayerRenderRequest(
        int Version,
        string PdfPath,
        int PdfIndex,
        string PageFolder,
        float RenderScale,
        bool ResetLayerStates,
        Dictionary<int, bool> LayerStates,
        HashSet<int> HighlightedLayers,
        IReadOnlyList<PdfLayerInfo>? CachedLayers,
        string? StatusAfter,
        bool FireLayersAfter);

    private sealed record LayerRenderCompletion(
        LayerRenderRequest Request,
        bool Ok,
        PdfLayerRenderResult Result,
        string Error);

    // ── Events ────────────────────────────────────────────────────────────────
    public event Action<string>?                          StatusChanged;
    public event Action<double>?                          ScaleChanged;
    public event Action<string>?                          ToolChanged;
    public event Action<bool>?                            SnapChanged;
    public event Action<bool>?                            OrthoChanged;
    public event Action<IReadOnlyList<PdfLayer>>?         LayersChanged;
    public event Action<IReadOnlyList<PdfLayerInfo>>?     PdfLayersDiscovered;
    public event Action?                                  PdfLayerTraceStateChanged;
    public event Action<Measurement>?                     MeasurementAdded;
    public event Action<Measurement>?                     MeasurementRemoved;
    public event Action<Measurement>?                     MeasurementChanged;
    public event Action<PageAnnotation>?                   PageAnnotationAdded;
    public event Action<PageAnnotation>?                   PageAnnotationRemoved;
    public event Action<Measurement?>?                    MeasurementSelectionChanged;
    public event Action<IReadOnlyList<Measurement>>?      MeasurementsSelectionChanged;
    public event Action<IReadOnlyList<Measurement>>?      CopyMeasurementsRequested;
    public event Action<SKPoint?>?                        PasteMeasurementsRequested;
    public event Action<ViewportContextRequest>?          ContextRequested;
    public event Action<Measurement, SKPoint, SKPoint>?   JoistDirectionCaptured;

    // ── Constants ─────────────────────────────────────────────────────────────
    private const float ZoomMin    = 0.05f;
    private const float ZoomMax    = 16.0f;
    private const float RenderDpi  = 144f;           // initial render quality (2 px/pt)
    private const double PdfPointMeters = 25.4 / 72.0 / 1000.0;
    private const float SnapToleranceScreenPx = 14f;
    private const float SnapMarkerScreenPx = 8f;
    private const float VertexHitToleranceScreenPx = 24f;
    private const float MeasurementHitToleranceScreenPx = 20f;
    private const float SelectedVertexHitToleranceScreenPx = 32f;
    private const float SelectedMeasurementHitToleranceScreenPx = 28f;
    private const float MeasurementLabelFontScreenPx = 9f;
    private const float MeasurementLabelPaddingScreenPx = 2f;
    private const float JoistSegmentLabelFontScreenPx = 7f;
    private static readonly float[] RenderScaleSteps = [0.75f, 1.00f, 1.50f, 2.25f, 3.00f, 4.00f];

    private static readonly SKColor TempColor = new(0xFF, 0xD7, 0x00);   // yellow
    private static readonly SKColor ScaleClr  = new(0x00, 0xE5, 0xFF);   // cyan

    // ── Constructor ───────────────────────────────────────────────────────────
    public PdfViewport()
    {
        Focusable     = true;
        ClipToBounds  = true;
        IgnorePixelScaling = true;
        // Suppress WPF context menu so right-button is free for pan
        ContextMenu   = null;
        _zoomRerenderTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180),
        };
        _zoomRerenderTimer.Tick += (_, _) =>
        {
            _zoomRerenderTimer.Stop();
            bool force = _zoomRerenderForce;
            _zoomRerenderForce = false;
            RerenderForZoomIfNeeded(force);
            RequestRepaint();
        };
    }

    private void RequestRepaint()
    {
        if (_repaintQueued)
            return;

        _repaintQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
        {
            _repaintQueued = false;
            InvalidateVisual();
        }));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Public API
    // ═════════════════════════════════════════════════════════════════════════

    public readonly record struct ViewState(float Zoom, float PanX, float PanY);

    public ViewState CaptureViewState() => new(_zoom, _panX, _panY);
    public bool IsPdfLayerTraceEnabled => _pdfLayerTraceEnabled;
    public string PdfLayerTraceModeTitle => LayerTraceModeTitle(_pdfLayerTraceMode);
    public string ActivePdfLayerTraceLayerName => _activePdfLayerTraceLayerName;
    public string PdfLayerTracePhaseTitle =>
        _pdfLayerTraceChoosingLayer ? "Choose Layer" :
        _pdfLayerTraceReadyToApply ? "Ready" :
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

        _pageBitmap?.Dispose();
        _pageBitmap = null;
        _drawPts.Clear();
        _scalePts.Clear();
        _rubberEnd = null;
        _boxSelecting = false;
        SetSnapPreview(null);
        _aiMarkers.Clear();
        _annotations.Clear();
        ClearAiActionDraftPreview();
        ClearSelection();
        _layerStates.Clear();
        _highlightedLayers.Clear();
        _layers.Clear();
        _activePdfLayerTraceLayer = null;
        _activePdfLayerTraceLayerName = "";
        ClearPdfLayerTraceSession();
        _usingLayerRenderer = false;
        _pendingLayerRender = null;
        _layerRenderVersion++;

        bool hasPreview = false;
        try
        {
            RenderPageWithDocnet(Math.Clamp(CurrentRenderScale(), 0.50f, 1.25f));
            hasPreview = true;
        }
        catch (Exception ex)
        {
            PostStatus($"Fast PDF preview unavailable: {ex.Message}");
        }

        // Fit after WPF has finished layout
        if (hasPreview)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (restoreView.HasValue)
                    RestoreViewState(restoreView.Value);
                else
                    ZoomFit();
                QueueLayerRender(
                    resetLayerStates: true,
                    renderScale: CurrentRenderScale(),
                    statusAfter: $"Loaded: {Path.GetFileName(pdfPath)}  page {pageIndex + 1}",
                    fireLayersAfter: true);
            });
        }
        else
        {
            if (restoreView.HasValue)
                RestoreViewState(restoreView.Value);
            QueueLayerRender(
                resetLayerStates: true,
                renderScale: CurrentRenderScale(),
                statusAfter: $"Loaded: {Path.GetFileName(pdfPath)}  page {pageIndex + 1}",
                fireLayersAfter: true);
        }
        PostStatus($"Rendering: {Path.GetFileName(pdfPath)}  page {pageIndex + 1}");

        // Fire layers event
        FireLayersChanged();
    }

    public void SetTool(string name)
    {
        _tool = name.ToLower() switch
        {
            "select" => ViewerTool.Select,
            "scale" => ViewerTool.Scale,
            "ruler" => ViewerTool.Ruler,
            "drawline" => ViewerTool.DrawLine,
            "drawarrow" => ViewerTool.DrawArrow,
            "drawrect" => ViewerTool.DrawRect,
            "point" => ViewerTool.Point,
            "line"  => ViewerTool.Line,
            "area"  => ViewerTool.Area,
            _       => ViewerTool.Pan,
        };
        CancelDrawing();
        SetSnapPreview(null);
        UpdateCursor();
        PostRecordPrompt();
    }

    public IReadOnlyList<Measurement> GetSelectedMeasurements() =>
        _selectedMeasurements
            .Where(m => _measurements.Contains(m) && IsMeasurementOnActivePage(m))
            .ToList();

    public void BeginJoistDirectionCapture(Measurement areaMeasurement)
    {
        if (areaMeasurement.MType != "area" || !IsMeasurementOnActivePage(areaMeasurement))
            return;

        _joistDirectionMeasurement = areaMeasurement;
        _joistDirectionPts.Clear();
        _joistDirectionRubberEnd = null;
        CancelDrawing();
        SelectMeasurements([areaMeasurement]);
        PostStatus("Joist direction: click two points parallel to the joists. Esc cancels.");
        RequestRepaint();
    }

    public void ZoomFit()
    {
        if (_pdfW <= 0 || ActualWidth < 2 || ActualHeight < 2) return;
        _zoom = (float)Math.Min(ActualWidth / _pdfW, ActualHeight / _pdfH) * 0.95f;
        _panX = _panY = 0;
        ScheduleRerenderForZoom(force: true);
        RequestRepaint();
    }

    public void ZoomIn()  => ApplyZoom(1.25f, (float)(ActualWidth  / 2), (float)(ActualHeight / 2));
    public void ZoomOut() => ApplyZoom(0.80f, (float)(ActualWidth  / 2), (float)(ActualHeight / 2));

    private bool TrySavePdfCrop(SKRect requestedPdfRect, string outputPath, out SKRect cropPdfRect, out string error)
    {
        cropPdfRect = SKRect.Empty;
        error = "";

        if (_pageBitmap == null || _bitmapScale <= 0 || _pdfW <= 0 || _pdfH <= 0)
        {
            error = "No rendered PDF page is available.";
            return false;
        }

        float left = Math.Clamp(Math.Min(requestedPdfRect.Left, requestedPdfRect.Right), 0, _pdfW);
        float top = Math.Clamp(Math.Min(requestedPdfRect.Top, requestedPdfRect.Bottom), 0, _pdfH);
        float right = Math.Clamp(Math.Max(requestedPdfRect.Left, requestedPdfRect.Right), 0, _pdfW);
        float bottom = Math.Clamp(Math.Max(requestedPdfRect.Top, requestedPdfRect.Bottom), 0, _pdfH);

        if (right - left < 1 || bottom - top < 1)
        {
            error = "Requested crop is outside the PDF page.";
            return false;
        }

        int srcLeft = Math.Clamp((int)Math.Floor(left * _bitmapScale), 0, _pageBitmap.Width - 1);
        int srcTop = Math.Clamp((int)Math.Floor(top * _bitmapScale), 0, _pageBitmap.Height - 1);
        int srcRight = Math.Clamp((int)Math.Ceiling(right * _bitmapScale), srcLeft + 1, _pageBitmap.Width);
        int srcBottom = Math.Clamp((int)Math.Ceiling(bottom * _bitmapScale), srcTop + 1, _pageBitmap.Height);
        int cropWidth = srcRight - srcLeft;
        int cropHeight = srcBottom - srcTop;
        if (cropWidth <= 0 || cropHeight <= 0)
        {
            error = "Requested crop is too small.";
            return false;
        }

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var crop = new SKBitmap(cropWidth, cropHeight);
        using (var canvas = new SKCanvas(crop))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(
                _pageBitmap,
                new SKRectI(srcLeft, srcTop, srcRight, srcBottom),
                new SKRect(0, 0, cropWidth, cropHeight));
        }

        using var image = SKImage.FromBitmap(crop);
        using var data = image.Encode(SKEncodedImageFormat.Png, 92);
        using var stream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        data.SaveTo(stream);

        cropPdfRect = new SKRect(
            srcLeft / _bitmapScale,
            srcTop / _bitmapScale,
            srcRight / _bitmapScale,
            srcBottom / _bitmapScale);
        return true;
    }

    // ── Layer API ─────────────────────────────────────────────────────────────

    private void RenderPageWithDocnet(float renderScale)
    {
        float scale = Math.Clamp(renderScale, 0.20f, 4.0f);
        using var docReader  = _docLib.GetDocReader(_pdfPath, new PageDimensions(scale));
        using var pageReader = docReader.GetPageReader(_pdfIndex);

        int bw = pageReader.GetPageWidth();
        int bh = pageReader.GetPageHeight();
        _pdfW        = bw / scale;
        _pdfH        = bh / scale;
        _bitmapScale = scale;

        byte[] bytes = pageReader.GetImage();

        var info = new SKImageInfo(bw, bh, SKColorType.Bgra8888, SKAlphaType.Premul);
        _pageBitmap?.Dispose();
        _pageBitmap = new SKBitmap(info);
        Marshal.Copy(bytes, 0, _pageBitmap.GetPixels(), bytes.Length);
        _layers = [];
        _usingLayerRenderer = false;
        _renderedScale = scale;
    }

    private bool RenderPageWithLayers(bool resetLayerStates, float renderScale)
    {
        if (!PdfLayerRenderService.TryRender(
                _pdfPath,
                _pdfIndex,
                Math.Clamp(renderScale, 0.20f, 4.0f),
                _layerStates,
                _highlightedLayers,
                _cachedLayers,
                out PdfLayerRenderResult render,
                out string error))
        {
            if (!string.IsNullOrWhiteSpace(error))
                PostStatus($"Layer renderer unavailable: {error}");
            return false;
        }

        return ApplyLayerRenderResult(render, resetLayerStates);
    }

    private bool ApplyLayerRenderResult(PdfLayerRenderResult render, bool resetLayerStates)
    {
        var bitmap = SKBitmap.Decode(render.ImageBytes);
        if (bitmap == null)
        {
            PostStatus("Layer renderer returned an unreadable image.");
            return false;
        }

        bool needsFit = _pdfW <= 0 || _pdfH <= 0;
        _pageBitmap?.Dispose();
        _pageBitmap = bitmap;
        _pdfW = render.WidthPt;
        _pdfH = render.HeightPt;
        _bitmapScale = _pdfW > 0 ? _pageBitmap.Width / _pdfW : RenderDpi / 72f;
        _renderedScale = _bitmapScale;
        if (_cachedLayers == null)
        {
            _cachedLayers = render.Layers
                .Select(layer => new PdfLayerInfo { Number = layer.Number, Name = layer.Name, IsOn = layer.IsOn })
                .ToList();
            PdfLayersDiscovered?.Invoke(_cachedLayers);
        }

        if (resetLayerStates)
        {
            _layerStates.Clear();
            foreach (var layer in render.Layers)
                _layerStates[layer.Number] = layer.IsOn;
        }

        UpdateLayerSnapshot(render.Layers);
        _usingLayerRenderer = true;
        RequestRepaint();
        if (needsFit)
            Dispatcher.InvokeAsync(ZoomFit);
        return true;
    }

    private void QueueLayerRender(
        bool resetLayerStates,
        float renderScale,
        string? statusAfter = null,
        bool fireLayersAfter = false)
    {
        if (string.IsNullOrWhiteSpace(_pdfPath))
            return;

        int version = ++_layerRenderVersion;
        _pendingLayerRender = new LayerRenderRequest(
            version,
            _pdfPath,
            _pdfIndex,
            _pageFolder,
            Math.Clamp(renderScale, 0.20f, 4.0f),
            resetLayerStates,
            new Dictionary<int, bool>(_layerStates),
            EffectiveHighlightedLayers(),
            LayerRenderCachedLayers(),
            statusAfter,
            fireLayersAfter);

        StartNextLayerRender();
    }

    private async void StartNextLayerRender()
    {
        if (_layerRenderInProgress || _pendingLayerRender == null)
            return;

        LayerRenderRequest request = _pendingLayerRender;
        _pendingLayerRender = null;
        _layerRenderInProgress = true;

        LayerRenderCompletion completion = await Task.Run(() =>
        {
            bool ok = PdfLayerRenderService.TryRender(
                request.PdfPath,
                request.PdfIndex,
                request.RenderScale,
                request.LayerStates,
                request.HighlightedLayers,
                request.CachedLayers,
                out PdfLayerRenderResult render,
                out string error);
            return new LayerRenderCompletion(request, ok, render, error);
        });

        _layerRenderInProgress = false;

        if (completion.Request.Version == _layerRenderVersion &&
            string.Equals(completion.Request.PdfPath, _pdfPath, StringComparison.OrdinalIgnoreCase) &&
            completion.Request.PdfIndex == _pdfIndex &&
            string.Equals(completion.Request.PageFolder, _pageFolder, StringComparison.OrdinalIgnoreCase))
        {
            if (completion.Ok)
            {
                if (ApplyLayerRenderResult(completion.Result, completion.Request.ResetLayerStates))
                {
                    if (completion.Request.FireLayersAfter)
                        FireLayersChanged();
                    if (!string.IsNullOrWhiteSpace(completion.Request.StatusAfter))
                        PostStatus(completion.Request.StatusAfter);
                }
            }
            else if (!string.IsNullOrWhiteSpace(completion.Error))
            {
                PostStatus($"Layer render unavailable: {completion.Error}");
            }
        }

        if (_pendingLayerRender != null)
            StartNextLayerRender();
    }

    private void UpdateLayerSnapshot(IEnumerable<PdfLayer> layers)
    {
        _layers = layers
            .Select(layer => new PdfLayer(
                layer.Number,
                layer.Name,
                _layerStates.TryGetValue(layer.Number, out bool on) ? on : layer.IsOn,
                _highlightedLayers.Contains(layer.Number) || _pdfLayerTracePreviewLayer == layer.Number))
            .ToList();
        EnsureActivePdfLayerTraceLayer();
        PublishPdfLayerTraceState();
    }

    public void SetLayerVisible(int configNumber, bool on)
    {
        if (_pageBitmap == null || !_usingLayerRenderer || _layers.Count == 0)
        {
            PostStatus("No PDF layers are available on this page.");
            return;
        }

        _layerStates[configNumber] = on;
        UpdateLayerSnapshot(_layers);
        FireLayersChanged();
        QueueLayerRender(
            resetLayerStates: false,
            renderScale: CurrentRenderScale(),
            statusAfter: $"Layer {(on ? "on" : "off")}: {LayerName(configNumber)}",
            fireLayersAfter: true);

        #if false
        if (_pageBitmap == null) return;
        try
        {
            // PyMuPDF 1.22+ equivalent: set_layer_ui_config(number, 0=on / 1=off)
            // Docnet.Core doesn't expose OCGs directly — reload page after toggling
            // For now: mark dirty and re-render (OCG toggle via PDFium P/Invoke added Phase 2)
            PostStatus("PDF layer toggling is unavailable in the current Docnet.Core renderer.");
        }
        catch (Exception ex) { PostStatus($"Layer error: {ex.Message}"); }
        #endif
    }

    public void SetAllLayers(bool on)
    {
        if (_pageBitmap == null || !_usingLayerRenderer || _layers.Count == 0)
        {
            PostStatus("No PDF layers are available on this page.");
            return;
        }

        foreach (var layer in _layers)
            _layerStates[layer.Number] = on;

        UpdateLayerSnapshot(_layers);
        FireLayersChanged();
        QueueLayerRender(
            resetLayerStates: false,
            renderScale: CurrentRenderScale(),
            statusAfter: $"All PDF layers {(on ? "on" : "off")}.",
            fireLayersAfter: true);

        #if false
        PostStatus("PDF layer toggling is unavailable in the current Docnet.Core renderer.");
        FireLayersChanged();
        #endif
    }

    public void SetLayerHighlighted(int configNumber, bool highlighted)
    {
        if (_pageBitmap == null || !_usingLayerRenderer || _layers.Count == 0)
        {
            PostStatus("No PDF layers are available on this page.");
            return;
        }

        if (highlighted)
            _highlightedLayers.Add(configNumber);
        else
            _highlightedLayers.Remove(configNumber);

        UpdateLayerSnapshot(_layers);
        FireLayersChanged();
        QueueLayerRender(
            resetLayerStates: false,
            renderScale: CurrentRenderScale(),
            statusAfter: $"{(highlighted ? "Highlighted" : "Unhighlighted")} layer: {LayerName(configNumber)}",
            fireLayersAfter: true);
    }

    public void ClearLayerHighlights()
    {
        if (_highlightedLayers.Count == 0)
            return;

        _highlightedLayers.Clear();
        UpdateLayerSnapshot(_layers);
        FireLayersChanged();
        QueueLayerRender(
            resetLayerStates: false,
            renderScale: CurrentRenderScale(),
            statusAfter: "Cleared PDF layer highlights.",
            fireLayersAfter: true);
    }

    // ── Undo ─────────────────────────────────────────────────────────────────

    public void UndoLast()
    {
        if (_drawPts.Count > 0)
        {
            _drawPts.RemoveAt(_drawPts.Count - 1);
            _rubberEnd = _drawPts.Count > 0 ? _rubberEnd : null;
            RequestRepaint();
            if (_drawPts.Count > 0)
                PostRecordPrompt();
            else
                PostStatus("Undo: drawing cleared.");
            return;
        }

        for (int i = _annotations.Count - 1; i >= 0; i--)
        {
            if (IsAnnotationOnActivePage(_annotations[i]))
            {
                PageAnnotation annotation = _annotations[i];
                _annotations.RemoveAt(i);
                RequestRepaint();
                PostStatus($"Undo: removed {ToolTitle(annotation.Kind)} markup.");
                PageAnnotationRemoved?.Invoke(annotation);
                return;
            }
        }

        for (int i = _measurements.Count - 1; i >= 0; i--)
        {
            if (IsMeasurementOnActivePage(_measurements[i]))
            {
                var m = _measurements[i];
                _measurements.RemoveAt(i);
                if (ReferenceEquals(_selectedMeasurement, m))
                    ClearSelection();
                RequestRepaint();
                PostStatus($"Undo: removed {ToolTitle(m.MType)}.");
                MeasurementRemoved?.Invoke(m);
                return;
            }
        }
        PostStatus("Nothing to undo on this page.");
    }

    // Remove specific measurements without firing MeasurementRemoved events
    // (caller handles model cleanup; this just keeps the render list consistent)
    public void DeleteMeasurements(IEnumerable<Measurement> toRemove)
    {
        foreach (var m in toRemove.ToList())
        {
            _measurements.Remove(m);
            _selectedMeasurements.Remove(m);
            if (ReferenceEquals(_selectedMeasurement, m))
                _selectedMeasurement = null;
        }
        if (_selectedMeasurement == null && _selectedMeasurements.Count > 0)
            _selectedMeasurement = _selectedMeasurements.LastOrDefault();
        if (_selectedMeasurement == null)
            _selectedVertexIndex = -1;
        RequestRepaint();
    }

    // Bulk-load measurements restored from a saved file
    public void LoadMeasurements(IEnumerable<Measurement> measurements)
    {
        SetMeasurements(measurements);
    }

    public void FocusMeasurement(Measurement measurement)
    {
        if (!_measurements.Contains(measurement))
            return;

        SelectMeasurement(measurement, -1);
        CenterOnMeasurement(measurement);
        RequestRepaint();
        PostStatus($"Selected {EntryTitle(measurement.MType)}. Drag the body to move; drag blue handles to reshape.");
    }

    public void SelectMeasurements(IEnumerable<Measurement> measurements)
    {
        var selected = measurements
            .Where(m => _measurements.Contains(m) && IsMeasurementOnActivePage(m))
            .Distinct()
            .ToList();
        SetSelectedMeasurements(selected, selected.LastOrDefault(), -1);
        if (selected.Count > 0)
            PostStatus($"Selected {selected.Count} measurement(s). Ctrl+C copies, Ctrl+V pastes to the active sheet.");
    }

    public void FocusPdfPoint(float pdfX, float pdfY)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0 || _zoom <= 0)
            return;

        float visibleW = (float)ActualWidth / _zoom;
        float visibleH = (float)ActualHeight / _zoom;
        _panX = pdfX - visibleW / 2f;
        _panY = pdfY - visibleH / 2f;
        ClampPanToPage();
        ScheduleRerenderForZoom(force: false);
        RequestRepaint();
        Focus();
    }

    public bool InsertMeasurementVertex(Measurement measurement, float pdfX, float pdfY)
    {
        if (!_measurements.Contains(measurement) ||
            !IsMeasurementOnActivePage(measurement) ||
            measurement.MType is not ("line" or "area"))
        {
            return false;
        }

        var point = new SKPoint(pdfX, pdfY);
        int insertIndex = measurement.Points.Count;

        if (measurement.Points.Count >= 2)
        {
            float bestDistance = float.PositiveInfinity;
            for (int i = 1; i < measurement.Points.Count; i++)
            {
                float distance = DistanceToSegment(point, measurement.Points[i - 1], measurement.Points[i]);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    insertIndex = i;
                }
            }

            if (measurement.MType == "area" && measurement.Points.Count > 2)
            {
                float closingDistance = DistanceToSegment(point, measurement.Points[^1], measurement.Points[0]);
                if (closingDistance < bestDistance)
                    insertIndex = measurement.Points.Count;
            }
        }

        measurement.Points.Insert(insertIndex, point);
        SelectMeasurement(measurement, insertIndex);
        MeasurementChanged?.Invoke(measurement);
        RequestRepaint();
        PostStatus($"Inserted {ToolTitle(measurement.MType)} vertex {insertIndex + 1}.");
        return true;
    }

    public bool RemoveNearestMeasurementVertex(Measurement measurement, float pdfX, float pdfY)
    {
        if (!_measurements.Contains(measurement) ||
            !IsMeasurementOnActivePage(measurement) ||
            measurement.MType is not ("line" or "area") ||
            measurement.Points.Count == 0)
        {
            return false;
        }

        int minimumPoints = measurement.MType == "area" ? 3 : 2;
        if (measurement.Points.Count <= minimumPoints)
        {
            PostStatus($"{ToolTitle(measurement.MType)} needs at least {minimumPoints} points.");
            return false;
        }

        var point = new SKPoint(pdfX, pdfY);
        int removeIndex = 0;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < measurement.Points.Count; i++)
        {
            float distance = DistanceSquared(point, measurement.Points[i]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                removeIndex = i;
            }
        }

        measurement.Points.RemoveAt(removeIndex);
        SelectMeasurement(measurement, Math.Min(removeIndex, measurement.Points.Count - 1));
        MeasurementChanged?.Invoke(measurement);
        RequestRepaint();
        PostStatus($"Removed {ToolTitle(measurement.MType)} vertex {removeIndex + 1}.");
        return true;
    }

    public bool TrySaveContextCrop(
        float pdfX,
        float pdfY,
        float radiusPt,
        string outputPath,
        out SKRect cropPdfRect,
        out string error)
    {
        radiusPt = Math.Max(24f, radiusPt);
        var requested = SKRect.Create(pdfX - radiusPt, pdfY - radiusPt, radiusPt * 2, radiusPt * 2);
        return TrySavePdfCrop(requested, outputPath, out cropPdfRect, out error);
    }

    public bool TrySaveMeasurementCrop(
        Measurement measurement,
        float paddingPt,
        string outputPath,
        out SKRect cropPdfRect,
        out string error)
    {
        cropPdfRect = SKRect.Empty;
        if (!_measurements.Contains(measurement) || measurement.Points.Count == 0)
        {
            error = "Measurement is not loaded in the current viewport.";
            return false;
        }

        SKRect bounds = MeasurementBounds(measurement);
        paddingPt = Math.Max(24f, paddingPt);

        float width = Math.Max(bounds.Width + paddingPt * 2, 240f);
        float height = Math.Max(bounds.Height + paddingPt * 2, 240f);
        float centerX = (bounds.Left + bounds.Right) / 2f;
        float centerY = (bounds.Top + bounds.Bottom) / 2f;
        var requested = SKRect.Create(centerX - width / 2f, centerY - height / 2f, width, height);

        return TrySavePdfCrop(requested, outputPath, out cropPdfRect, out error);
    }

    public void SetMeasurements(IEnumerable<Measurement> measurements)
    {
        _measurements.Clear();
        _measurements.AddRange(measurements);
        ClearSelection();
        RequestRepaint();
    }

    public IReadOnlyList<PageAnnotation> GetPageAnnotations() =>
        _annotations
            .Where(annotation => IsAnnotationOnActivePage(annotation))
            .ToList();

    public void SetPageAnnotations(IEnumerable<PageAnnotation> annotations)
    {
        _annotations.Clear();
        _annotations.AddRange(annotations);
        RequestRepaint();
    }

    public void SetSheetLegend(IEnumerable<SheetLegendEntry> entries)
    {
        _sheetLegendEntries.Clear();
        _sheetLegendEntries.AddRange(entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .Take(50));
        RequestRepaint();
    }

    public void ShowAiActionDraftPreview(SmartAiActionDraft draft, string pageName)
    {
        _aiActionDraftPreview = draft;
        _aiActionDraftPreviewPage = pageName;
        RequestRepaint();
    }

    public void ClearAiActionDraftPreview()
    {
        _aiActionDraftPreview = null;
        _aiActionDraftPreviewPage = "";
        RequestRepaint();
    }

    public void SetAiMarkers(IEnumerable<SmartAiMarker> markers)
    {
        _aiMarkers.Clear();
        _aiMarkers.AddRange(markers);
        RequestRepaint();
    }

    public void ClearAiMarkers()
    {
        _aiMarkers.Clear();
        RequestRepaint();
    }

    public void ClearPage()
    {
        _pdfPath = "";
        _pdfIndex = 0;
        _pageFolder = "";
        _cachedLayers = null;
        _zoomRerenderTimer.Stop();
        _zoomRerenderForce = false;
        _isViewDragging = false;
        _pendingLayerRender = null;
        _layerRenderVersion++;
        _pdfLayerTraceEnabled = false;
        _activePdfLayerTraceLayer = null;
        _activePdfLayerTraceLayerName = "";
        ClearPdfLayerTraceSession();
        _pageBitmap?.Dispose();
        _pageBitmap = null;
        _pdfW = _pdfH = 0;
        _drawPts.Clear();
        _scalePts.Clear();
        _rubberEnd = null;
        _boxSelecting = false;
        _aiActionDraftPreview = null;
        _aiActionDraftPreviewPage = "";
        _aiMarkers.Clear();
        _annotations.Clear();
        _sheetLegendEntries.Clear();
        CancelJoistDirectionCapture();
        ClearSelection();
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

    // ═════════════════════════════════════════════════════════════════════════
    // Rendering
    // ═════════════════════════════════════════════════════════════════════════

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(GetCachedColor(ViewBackgroundColor, SKColors.White));

        if (_pageBitmap == null) return;
        SKRect visiblePdf = GetVisiblePdfRect();

        // ── PDF page bitmap ───────────────────────────────────────────────────
        // PDF point (px,py) → screen pixel (sx,sy):
        //   sx = (px - panX) * zoom
        //   sy = (py - panY) * zoom
        // bitmap pixel (bx,by) → screen pixel:
        //   sx = bx * zoom/bitmapScale - panX*zoom
        {
            using var bitmapPaint = new SKPaint
            {
                IsAntialias = true,
                FilterQuality = _isViewDragging ? SKFilterQuality.Low : SKFilterQuality.Medium,
            };

            float visibleW = (float)ActualWidth / Math.Max(_zoom, 0.001f);
            float visibleH = (float)ActualHeight / Math.Max(_zoom, 0.001f);
            float srcLeft = Math.Clamp(_panX * _bitmapScale, 0, _pageBitmap.Width);
            float srcTop = Math.Clamp(_panY * _bitmapScale, 0, _pageBitmap.Height);
            float srcRight = Math.Clamp((_panX + visibleW) * _bitmapScale, 0, _pageBitmap.Width);
            float srcBottom = Math.Clamp((_panY + visibleH) * _bitmapScale, 0, _pageBitmap.Height);

            if (srcRight > srcLeft && srcBottom > srcTop)
            {
                var src = new SKRect(srcLeft, srcTop, srcRight, srcBottom);
                var dst = new SKRect(
                    (srcLeft / _bitmapScale - _panX) * _zoom,
                    (srcTop / _bitmapScale - _panY) * _zoom,
                    (srcRight / _bitmapScale - _panX) * _zoom,
                    (srcBottom / _bitmapScale - _panY) * _zoom);
                canvas.DrawBitmap(_pageBitmap, src, dst, bitmapPaint);
            }
        }

        // ── Measurement overlay (PDF-point coordinate system) ─────────────────
        {
            var measMtx = SKMatrix.CreateScaleTranslation(
                _zoom, _zoom, -_panX * _zoom, -_panY * _zoom);
            using var saved = new SKAutoCanvasRestore(canvas, true);
            canvas.SetMatrix(measMtx);
            DrawMeasurements(canvas, visiblePdf);
            DrawPageAnnotations(canvas, visiblePdf);
            DrawAiActionDraftPreview(canvas, visiblePdf);
            DrawAiMarkers(canvas, visiblePdf);
            DrawInProgress(canvas);
        }

        DrawSheetHeaderOverlay(canvas, (float)e.Info.Width, (float)e.Info.Height);
        DrawSheetLegendOverlay(canvas, (float)e.Info.Width, (float)e.Info.Height);
    }

    private void DrawSheetHeaderOverlay(SKCanvas canvas, float canvasWidth, float canvasHeight)
    {
        if (_pdfW <= 0 || _pdfH <= 0 || _zoom <= 0 || canvasWidth <= 0 || canvasHeight <= 0)
            return;

        float pageLeft = -_panX * _zoom;
        float pageTop = -_panY * _zoom;
        float pageRight = (_pdfW - _panX) * _zoom;
        float pageBottom = (_pdfH - _panY) * _zoom;
        float visibleLeft = Math.Max(0, pageLeft);
        float visibleTop = Math.Max(0, pageTop);
        float visibleRight = Math.Min(canvasWidth, pageRight);
        float visibleBottom = Math.Min(canvasHeight, pageBottom);

        if (visibleRight - visibleLeft < 48 || visibleBottom - visibleTop < 20)
            return;

        float overlayScale = HeaderOverlayScale();
        float fontSize = 13f * overlayScale;
        float padX = 7f * overlayScale;
        float padY = 4f * overlayScale;
        float margin = 8f * overlayScale;

        string scaleText = FormatSheetScale();
        string sheetSizeText = FormatSheetSize();

        SKTypeface monoTypeface = SKTypeface.FromFamilyName("Consolas")
                                   ?? SKTypeface.FromFamilyName("Cascadia Mono")
                                   ?? SKTypeface.Default;
        using var textPaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = fontSize,
            IsAntialias = true,
            Typeface = monoTypeface,
        };
        using var bgPaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(232),
            Style = SKPaintStyle.Fill,
        };
        using var borderPaint = new SKPaint
        {
            Color = new SKColor(0x30, 0x30, 0x30, 220),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
        };

        float lineHeight = textPaint.FontMetrics.Descent - textPaint.FontMetrics.Ascent;
        float boxHeight = lineHeight + padY * 2;
        float y = Math.Max(visibleTop + margin, pageTop + margin);
        y = Math.Min(y, visibleBottom - boxHeight - margin);
        if (y < visibleTop)
            return;

        float leftX = Math.Max(visibleLeft + margin, pageLeft + margin);
        float scaleWidth = textPaint.MeasureText(scaleText);
        float sizeWidth = textPaint.MeasureText(sheetSizeText);
        float availableWidth = visibleRight - visibleLeft - margin * 2;

        if (scaleWidth + sizeWidth + padX * 4 + 28f <= availableWidth)
        {
            DrawHeaderBox(canvas, leftX, y, scaleText, textPaint, bgPaint, borderPaint, padX, padY, lineHeight);

            float rightX = Math.Min(visibleRight - margin - sizeWidth - padX * 2, pageRight - margin - sizeWidth - padX * 2);
            if (rightX > leftX + scaleWidth + padX * 2 + 18f)
                DrawHeaderBox(canvas, rightX, y, sheetSizeText, textPaint, bgPaint, borderPaint, padX, padY, lineHeight);
        }
        else if (scaleWidth + padX * 2 <= availableWidth)
        {
            DrawHeaderBox(canvas, leftX, y, scaleText, textPaint, bgPaint, borderPaint, padX, padY, lineHeight);
        }
    }

    private static void DrawHeaderBox(
        SKCanvas canvas,
        float x,
        float y,
        string text,
        SKPaint textPaint,
        SKPaint bgPaint,
        SKPaint borderPaint,
        float padX,
        float padY,
        float lineHeight)
    {
        float textWidth = textPaint.MeasureText(text);
        var rect = new SKRect(x, y, x + textWidth + padX * 2, y + lineHeight + padY * 2);
        canvas.DrawRect(rect, bgPaint);
        canvas.DrawRect(rect, borderPaint);
        canvas.DrawText(text, x + padX, y + padY - textPaint.FontMetrics.Ascent, textPaint);
    }

    private void DrawSheetLegendOverlay(SKCanvas canvas, float canvasWidth, float canvasHeight)
    {
        if (_sheetLegendEntries.Count == 0 ||
            _pdfW <= 0 ||
            _pdfH <= 0 ||
            _zoom <= 0 ||
            canvasWidth <= 0 ||
            canvasHeight <= 0)
        {
            return;
        }

        float pageLeft = -_panX * _zoom;
        float pageTop = -_panY * _zoom;
        float pageRight = (_pdfW - _panX) * _zoom;
        float pageBottom = (_pdfH - _panY) * _zoom;
        float visibleLeft = Math.Max(0, pageLeft);
        float visibleTop = Math.Max(0, pageTop);
        float visibleRight = Math.Min(canvasWidth, pageRight);
        float visibleBottom = Math.Min(canvasHeight, pageBottom);

        float availableWidth = visibleRight - visibleLeft;
        float availableHeight = visibleBottom - visibleTop;
        float overlayScale = LegendOverlayScale();
        if (availableWidth < Math.Max(96f, 160f * overlayScale) ||
            availableHeight < Math.Max(56f, 90f * overlayScale))
            return;

        float margin = 8f * overlayScale;
        float pad = 8f * overlayScale;
        float baseTitleSize = 12f * overlayScale;
        float baseRowSize = 11f * overlayScale;
        int maxDetailLines = Math.Max(0, _sheetLegendEntries.Max(entry => entry.Details?.Count ?? 0));
        float baseRowHeight = 16f * overlayScale * (1 + Math.Min(maxDetailLines, 6) * 0.82f);
        float titleHeight = 18f * overlayScale;
        float maxBoxWidth = availableWidth - margin * 2;
        float maxBoxHeight = availableHeight - margin * 2;
        float contentHeight = Math.Max(baseRowHeight, maxBoxHeight - pad * 2 - titleHeight);
        float minColumnWidth = 170f * overlayScale;
        int maxColumns = Math.Max(1, Math.Min(_sheetLegendEntries.Count, (int)(maxBoxWidth / minColumnWidth)));
        int columns = 1;
        for (int candidate = 1; candidate <= maxColumns; candidate++)
        {
            int candidateRows = (int)Math.Ceiling(_sheetLegendEntries.Count / (double)candidate);
            if (candidateRows * baseRowHeight <= contentHeight)
            {
                columns = candidate;
                break;
            }

            columns = candidate;
        }

        int rowsPerColumn = Math.Max(1, (int)Math.Ceiling(_sheetLegendEntries.Count / (double)columns));
        float rowHeight = Math.Min(baseRowHeight, contentHeight / rowsPerColumn);
        float rowScale = Math.Clamp(rowHeight / baseRowHeight, 0.58f, 1f);
        rowHeight = Math.Max(8f * overlayScale, rowHeight);
        float titleSize = baseTitleSize * Math.Clamp(rowScale, 0.75f, 1f);
        float rowSize = baseRowSize * rowScale;
        float boxWidth = Math.Min(maxBoxWidth, Math.Max(180f * overlayScale, columns * 220f * overlayScale));
        float boxHeight = Math.Min(maxBoxHeight, pad * 2 + titleHeight + rowsPerColumn * rowHeight);
        SKPoint position = AnchorOverlayBox(
            SheetLegendAnchor,
            boxWidth,
            boxHeight,
            visibleLeft,
            visibleTop,
            visibleRight,
            visibleBottom,
            pageLeft,
            pageTop,
            pageRight,
            pageBottom,
            margin);
        float x = position.X;
        float y = position.Y;

        SKTypeface uiTypeface = SKTypeface.FromFamilyName("Segoe UI")
                                 ?? SKTypeface.FromFamilyName("Inter")
                                 ?? SKTypeface.Default;
        SKTypeface uiBoldTypeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
                                     ?? SKTypeface.FromFamilyName("Inter", SKFontStyle.Bold)
                                     ?? uiTypeface;

        using var titlePaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = titleSize,
            IsAntialias = true,
            Typeface = uiBoldTypeface,
        };
        using var textPaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = rowSize,
            IsAntialias = true,
            Typeface = uiTypeface,
        };
        using var mutedPaint = new SKPaint
        {
            Color = new SKColor(0x44, 0x44, 0x44, 235),
            TextSize = rowSize,
            IsAntialias = true,
            Typeface = uiTypeface,
        };
        using var bgPaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(238),
            Style = SKPaintStyle.Fill,
        };
        using var borderPaint = new SKPaint
        {
            Color = new SKColor(0x30, 0x30, 0x30, 220),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
        };

        var box = new SKRect(x, y, x + boxWidth, y + boxHeight);
        canvas.DrawRect(box, bgPaint);
        canvas.DrawRect(box, borderPaint);
        string title = _sheetLegendEntries.Count > 1
            ? $"Legend ({_sheetLegendEntries.Count})"
            : "Legend";
        canvas.DrawText(title, x + pad, y + pad - titlePaint.FontMetrics.Ascent, titlePaint);

        float columnWidth = (boxWidth - pad * 2) / columns;
        float columnGap = columns > 1 ? 10f * overlayScale : 0f;
        for (int i = 0; i < _sheetLegendEntries.Count; i++)
        {
            SheetLegendEntry entry = _sheetLegendEntries[i];
            int column = i / rowsPerColumn;
            int row = i % rowsPerColumn;
            float columnLeft = x + pad + column * columnWidth;
            float columnRight = Math.Min(x + boxWidth - pad, columnLeft + columnWidth - columnGap);
            float rowY = y + pad + titleHeight + row * rowHeight;
            SKColor color = GetCachedColor(entry.Color, SKColors.Red);

            float baseline = rowY - textPaint.FontMetrics.Ascent;

            // Single colored glyph (no separate colored square).
            float glyphSize = 16f * overlayScale * rowScale;
            float glyphLeft = columnLeft;
            var glyphBox = new SKRect(
                glyphLeft,
                rowY + Math.Max(2f * overlayScale, (rowHeight - glyphSize) / 2f),
                glyphLeft + glyphSize,
                rowY + Math.Max(2f * overlayScale, (rowHeight - glyphSize) / 2f) + glyphSize);
            DrawLegendSignIcon(canvas, entry.Sign, glyphBox, color);

            float nameLeft = glyphLeft + glyphSize + 6f * overlayScale * rowScale;
            float qtyRight = columnRight;
            float qtyWidth = Math.Min(76f * overlayScale * rowScale, columnWidth * 0.38f);
            float nameRight = qtyRight - qtyWidth - 4f * overlayScale;
            string name = FitText(entry.Name, textPaint, Math.Max(24f, nameRight - nameLeft));
            string qty = FitText(entry.Quantity, mutedPaint, Math.Max(24f, qtyWidth));
            canvas.DrawText(name, nameLeft, baseline, textPaint);
            canvas.DrawText(qty, qtyRight - mutedPaint.MeasureText(qty), baseline, mutedPaint);
            if (entry.Details is { Count: > 0 } details)
            {
                float detailBaseline = baseline + baseRowSize * rowScale * 1.08f;
                foreach (string detail in details.Take(6))
                {
                    canvas.DrawText(
                        FitText(detail, mutedPaint, Math.Max(24f, columnRight - nameLeft)),
                        nameLeft,
                        detailBaseline,
                        mutedPaint);
                    detailBaseline += baseRowSize * rowScale * 1.08f;
                }
            }
        }
    }

    private static void DrawLegendSignIcon(SKCanvas canvas, string sign, SKRect box, SKColor color)
    {
        MeasurementGlyph.DrawSkia(canvas, MeasurementGlyph.FromSign(sign), color, box);
    }

    private static string FitText(string text, SKPaint paint, float maxWidth)
    {
        string value = (text ?? "").Trim();
        if (value.Length == 0 || paint.MeasureText(value) <= maxWidth)
            return value;

        const string suffix = "...";
        float suffixWidth = paint.MeasureText(suffix);
        if (suffixWidth >= maxWidth)
            return suffix;

        int keep = value.Length;
        while (keep > 1 && paint.MeasureText(value[..keep]) + suffixWidth > maxWidth)
            keep--;
        return value[..keep].TrimEnd() + suffix;
    }

    private float HeaderOverlayScale() =>
        ClampOverlayUserScale(SheetHeaderScale) * SheetZoomOverlayScale(ScaleSheetHeaderWithPage);

    private float LegendOverlayScale() =>
        ClampOverlayUserScale(SheetLegendScale) * SheetZoomOverlayScale(ScaleSheetOverlaysWithPage);

    private float SheetZoomOverlayScale(bool enabled)
    {
        if (!enabled)
            return 1f;

        float fitZoom = CurrentFitZoom();
        if (fitZoom <= 0)
            return 1f;

        return Math.Clamp(_zoom / fitZoom, 0.35f, 4.0f);
    }

    private float CurrentFitZoom()
    {
        if (_pdfW <= 0 || _pdfH <= 0 || ActualWidth < 2 || ActualHeight < 2)
            return 0;

        return (float)Math.Min(ActualWidth / _pdfW, ActualHeight / _pdfH) * 0.95f;
    }

    private static float ClampOverlayUserScale(double scale)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            return 1f;

        return (float)Math.Clamp(scale, 0.50, 3.00);
    }

    private static SKPoint AnchorOverlayBox(
        string anchor,
        float width,
        float height,
        float visibleLeft,
        float visibleTop,
        float visibleRight,
        float visibleBottom,
        float pageLeft,
        float pageTop,
        float pageRight,
        float pageBottom,
        float margin)
    {
        float minX = Math.Max(visibleLeft + margin, pageLeft + margin);
        float maxX = Math.Min(visibleRight - margin - width, pageRight - margin - width);
        float minY = Math.Max(visibleTop + margin, pageTop + margin);
        float maxY = Math.Min(visibleBottom - margin - height, pageBottom - margin - height);
        if (maxX < minX)
            maxX = minX;
        if (maxY < minY)
            maxY = minY;

        string clean = (anchor ?? "").Trim().ToLowerInvariant();
        float centerX = (minX + maxX) / 2f;
        float centerY = (minY + maxY) / 2f;

        float x = clean switch
        {
            "topcenter" or "bottomcenter" => centerX,
            "topright" or "middleright" or "bottomright" => maxX,
            _ => minX,
        };
        float y = clean switch
        {
            "middleleft" or "middleright" => centerY,
            "bottomleft" or "bottomcenter" or "bottomright" => maxY,
            _ => minY,
        };

        return new SKPoint(Math.Clamp(x, minX, maxX), Math.Clamp(y, minY, maxY));
    }

    private string FormatSheetScale()
    {
        if (ScaleMetersPerPt <= 0)
            return "Scale: not set";

        double ratio = ScaleMetersPerPt / PdfPointMeters;
        string architectural = FormatArchitecturalScale(ratio);
        return string.IsNullOrWhiteSpace(architectural)
            ? $"Scale: 1:{ratio:F0}"
            : $"Scale: {architectural}";
    }

    private string FormatSheetSize()
    {
        double widthIn = _pdfW / 72.0;
        double heightIn = _pdfH / 72.0;
        return $"{widthIn:F2} x {heightIn:F2}";
    }

    private static string FormatArchitecturalScale(double ratio)
    {
        if (ratio <= 0)
            return "";

        (double Ratio, string Label)[] presets =
        [
            (4,   "3\" = 1' 0\""),
            (8,   "1-1/2\" = 1' 0\""),
            (12,  "1\" = 1' 0\""),
            (16,  "3/4\" = 1' 0\""),
            (24,  "1/2\" = 1' 0\""),
            (32,  "3/8\" = 1' 0\""),
            (48,  "1/4\" = 1' 0\""),
            (64,  "3/16\" = 1' 0\""),
            (96,  "1/8\" = 1' 0\""),
            (128, "3/32\" = 1' 0\""),
            (192, "1/16\" = 1' 0\""),
        ];

        foreach (var preset in presets)
        {
            if (Math.Abs(ratio - preset.Ratio) <= 0.25)
                return preset.Label;
        }

        return "";
    }

    // ── Draw finalized measurements ───────────────────────────────────────────

    private void DrawMeasurements(SKCanvas canvas, SKRect visiblePdf)
    {
        foreach (var m in _measurements)
        {
            if (!IsMeasurementOnActivePage(m)) continue;
            bool selected = IsMeasurementSelected(m);
            if (!selected && !IsMeasurementVisible(m, visiblePdf))
                continue;
            DrawMeasurement(canvas, m, selected);
            if (selected && !ReferenceEquals(m, _selectedMeasurement))
                DrawSelectionBounds(canvas, m);
            if (ReferenceEquals(m, _selectedMeasurement))
                DrawSelectionHandles(canvas, m);
        }
    }

    private void DrawMeasurement(SKCanvas canvas, Measurement m, bool selected)
    {
        SKColor color = GetCachedColor(m.Color, SKColors.Red);
        using var stroke = new SKPaint
        {
            Color       = color,
            StrokeWidth = (selected ? 3f : 2f) / _zoom,       // constant screen width regardless of zoom
            IsAntialias = true,
            Style       = SKPaintStyle.Stroke,
        };
        using var fill = new SKPaint
        {
            Color       = color.WithAlpha(180),
            IsAntialias = true,
            Style       = SKPaintStyle.Fill,
        };

        var pts = m.Points;

        switch (m.MType)
        {
            case "point":
                float r = 5f / _zoom;
                foreach (var p in pts)
                    canvas.DrawCircle(p, r, fill);
                if (pts.Count > 0 && ShouldDrawMeasurementLabel("point"))
                    DrawLabel(canvas, pts[^1], m.Label(ScaleMetersPerPt, UnitMode), m.Color);
                break;

            case "line" when pts.Count >= 2:
                using (var path = new SKPath())
                {
                path.MoveTo(pts[0]);
                for (int i = 1; i < pts.Count; i++) path.LineTo(pts[i]);
                canvas.DrawPath(path, stroke);
                }
                float pr = 3f / _zoom;
                foreach (var p in pts)
                    canvas.DrawCircle(p, pr, fill);
                if (ShouldDrawMeasurementLabel("line"))
                    DrawLabel(canvas, pts[^1], m.Label(ScaleMetersPerPt, UnitMode), m.Color);
                break;

            case "area" when pts.Count >= 3:
                using (var poly = new SKPath())
                {
                poly.MoveTo(pts[0]);
                for (int i = 1; i < pts.Count; i++) poly.LineTo(pts[i]);
                poly.Close();
                using (var fillTrans = fill.Clone())
                {
                    fillTrans.Color = fillTrans.Color.WithAlpha(60);
                    canvas.DrawPath(poly, fillTrans);
                }
                canvas.DrawPath(poly, stroke);
                }
                DrawJoistLayout(canvas, m, color);
                var cen = Centroid(pts);
                if (ShouldDrawMeasurementLabel("area"))
                    DrawLabel(canvas, cen, m.Label(ScaleMetersPerPt, UnitMode), m.Color);
                break;
        }
    }

    private void DrawPageAnnotations(SKCanvas canvas, SKRect visiblePdf)
    {
        foreach (PageAnnotation annotation in _annotations)
        {
            if (!IsAnnotationOnActivePage(annotation) ||
                annotation.Points.Count < 2 ||
                !PointsVisible(annotation.Points, visiblePdf))
            {
                continue;
            }

            DrawPageAnnotation(canvas, annotation);
        }
    }

    private void DrawPageAnnotation(SKCanvas canvas, PageAnnotation annotation)
    {
        string kind = SmartTakeoffsJobStore.NormalizePageAnnotationKind(annotation.Kind);
        SKColor color = GetCachedColor(annotation.Color, new SKColor(0x15, 0x65, 0xC0));
        using var stroke = new SKPaint
        {
            Color = color,
            StrokeWidth = 1.8f / _zoom,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };

        SKPoint start = annotation.Points[0];
        SKPoint end = annotation.Points[1];
        if (kind == "rectangle")
        {
            canvas.DrawRect(NormalizeRect(start, end), stroke);
            return;
        }

        canvas.DrawLine(start, end, stroke);
        if (kind == "arrow")
        {
            DrawAnnotationArrowHead(canvas, start, end, stroke, 9f / _zoom);
            return;
        }

        if (kind == "dimension")
        {
            DrawAnnotationDimensionTicks(canvas, start, end, stroke, 6f / _zoom);
            DrawScreenTextBox(
                canvas,
                new SKPoint((start.X + end.X) / 2f, (start.Y + end.Y) / 2f),
                [AnnotationLabel(annotation)],
                SKColors.White,
                SKColors.Black.WithAlpha(185),
                color,
                MeasurementLabelFontScreenPx,
                MeasurementLabelPaddingScreenPx,
                centered: true);
        }
    }

    private void DrawAnnotationArrowHead(SKCanvas canvas, SKPoint start, SKPoint end, SKPaint stroke, float size)
    {
        float dx = end.X - start.X;
        float dy = end.Y - start.Y;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length <= 0.001f)
            return;

        float ux = dx / length;
        float uy = dy / length;
        float px = -uy;
        float py = ux;
        SKPoint left = new(end.X - ux * size + px * size * 0.45f, end.Y - uy * size + py * size * 0.45f);
        SKPoint right = new(end.X - ux * size - px * size * 0.45f, end.Y - uy * size - py * size * 0.45f);
        canvas.DrawLine(end, left, stroke);
        canvas.DrawLine(end, right, stroke);
    }

    private static void DrawAnnotationDimensionTicks(SKCanvas canvas, SKPoint start, SKPoint end, SKPaint stroke, float tick)
    {
        float dx = end.X - start.X;
        float dy = end.Y - start.Y;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length <= 0.001f)
            return;

        float px = -dy / length;
        float py = dx / length;
        canvas.DrawLine(start.X - px * tick, start.Y - py * tick, start.X + px * tick, start.Y + py * tick, stroke);
        canvas.DrawLine(end.X - px * tick, end.Y - py * tick, end.X + px * tick, end.Y + py * tick, stroke);
    }

    private string AnnotationLabel(PageAnnotation annotation)
    {
        if (!string.IsNullOrWhiteSpace(annotation.Text))
            return annotation.Text;

        if (annotation.Points.Count < 2)
            return "";

        SKPoint start = annotation.Points[0];
        SKPoint end = annotation.Points[1];
        float dx = end.X - start.X;
        float dy = end.Y - start.Y;
        double lengthPt = Math.Sqrt(dx * dx + dy * dy);
        double scale = annotation.ScaleMetersPerPt > 0
            ? annotation.ScaleMetersPerPt
            : ScaleMetersPerPt;
        return scale > 0
            ? Units.FormatLength(lengthPt * scale, UnitMode)
            : $"{lengthPt:F1} pt";
    }

    private bool ShouldDrawMeasurementLabel(string measurementType)
    {
        if (!ShowMeasurementLabels)
            return false;

        return measurementType switch
        {
            "point" => ShowCountLabels,
            "area" => ShowAreaLabels,
            _ => ShowLineLabels,
        };
    }

    private void DrawJoistLayout(SKCanvas canvas, Measurement m, SKColor color)
    {
        if (!m.JoistEnabled)
            return;

        JoistLayoutResult layout = JoistTakeoffCalculator.Calculate(m, ScaleMetersPerPt);
        if (!layout.HasScale || layout.Count == 0)
            return;

        using var joistStroke = new SKPaint
        {
            Color = color.WithAlpha(220),
            StrokeWidth = 1.15f / _zoom,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
        };
        bool drawLabels = m.JoistShowLabels && layout.Count <= 180;
        foreach (JoistSegment segment in layout.Segments)
        {
            canvas.DrawLine(segment.Start, segment.End, joistStroke);
            if (!drawLabels)
                continue;

            string label = JoistTakeoffCalculator.FormatSegmentLength(segment, UnitMode);
            SKPoint mid = new(
                (segment.Start.X + segment.End.X) / 2f,
                (segment.Start.Y + segment.End.Y) / 2f);
            DrawScreenTextBox(
                canvas,
                mid,
                [label],
                SKColors.Black.WithAlpha(220),
                SKColors.White.WithAlpha(190),
                SKColors.Transparent,
                JoistSegmentLabelFontScreenPx,
                2f,
                centered: true);
        }
    }

    private void DrawSelectionBounds(SKCanvas canvas, Measurement m)
    {
        if (m.Points.Count == 0) return;

        SKRect bounds = RawMeasurementBounds(m);
        bounds.Inflate(6f / Math.Max(_zoom, 0.001f), 6f / Math.Max(_zoom, 0.001f));
        using var stroke = new SKPaint
        {
            Color = SKColors.DodgerBlue,
            StrokeWidth = 1.5f / _zoom,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            PathEffect = SKPathEffect.CreateDash([6f / _zoom, 4f / _zoom], 0),
        };
        canvas.DrawRect(bounds, stroke);
    }

    private void DrawSelectionHandles(SKCanvas canvas, Measurement m)
    {
        if (m.Points.Count == 0) return;

        float radius = 5f / _zoom;
        using var fill = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var activeFill = new SKPaint
        {
            Color = TempColor,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var stroke = new SKPaint
        {
            Color = SKColors.DodgerBlue,
            StrokeWidth = 1.5f / _zoom,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };

        for (int i = 0; i < m.Points.Count; i++)
        {
            var rect = SKRect.Create(m.Points[i].X - radius, m.Points[i].Y - radius, radius * 2, radius * 2);
            canvas.DrawRect(rect, i == _selectedVertexIndex ? activeFill : fill);
            canvas.DrawRect(rect, stroke);
        }
    }

    private void DrawLabel(SKCanvas canvas, SKPoint pos, string text, string hexColor)
    {
        if (string.IsNullOrEmpty(text)) return;

        string[] lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
            return;

        DrawScreenTextBox(
            canvas,
            pos,
            lines,
            SKColors.White,
            SKColors.Black.WithAlpha(180),
            GetCachedColor(hexColor, SKColors.DodgerBlue),
            MeasurementLabelFontScreenPx,
            MeasurementLabelPaddingScreenPx,
            centered: false);
    }

    private void DrawScreenTextBox(
        SKCanvas canvas,
        SKPoint pdfPos,
        IReadOnlyList<string> lines,
        SKColor textColor,
        SKColor backgroundColor,
        SKColor borderColor,
        float fontSize,
        float pad,
        bool centered)
    {
        if (lines.Count == 0)
            return;

        string[] cleanLines = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim())
            .ToArray();
        if (cleanLines.Length == 0)
            return;

        float safeZoom = Math.Max(_zoom, 0.001f);
        float labelScale = ClampOverlayUserScale(MeasurementLabelScale);
        // When ScaleMeasurementLabelsWithPage is on, labels live in PDF space (relative to fit zoom)
        // so they grow/shrink with page zoom. Otherwise dividing by _zoom keeps screen size constant.
        float labelDivisor = ScaleMeasurementLabelsWithPage
            ? Math.Max(CurrentFitZoom(), 0.001f)
            : safeZoom;
        using var textPaint = new SKPaint
        {
            Color       = textColor,
            TextSize    = fontSize * labelScale / labelDivisor,
            IsAntialias = true,
            Typeface    = SKTypeface.FromFamilyName("Consolas"),
        };

        float width = 0;
        foreach (string line in cleanLines)
            width = Math.Max(width, textPaint.MeasureText(line));
        float lineHeight = textPaint.TextSize * 1.22f;
        float textHeight = lineHeight * cleanLines.Length;
        float pdfPad = pad * labelScale / labelDivisor;
        SKRect bg = centered
            ? new SKRect(
                pdfPos.X - width / 2f - pdfPad,
                pdfPos.Y - textHeight / 2f - pdfPad,
                pdfPos.X + width / 2f + pdfPad,
                pdfPos.Y + textHeight / 2f + pdfPad)
            : new SKRect(
                pdfPos.X + pdfPad,
                pdfPos.Y - textHeight - pdfPad,
                pdfPos.X + width + pdfPad * 3,
                pdfPos.Y + pdfPad);

        using var bgPaint = new SKPaint
        {
            Color = backgroundColor,
            Style = SKPaintStyle.Fill,
        };
        using var borderPaint = new SKPaint
        {
            Color       = borderColor,
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = 1f / labelDivisor,
        };
        float radius = 3f / labelDivisor;
        canvas.DrawRoundRect(bg, radius, radius, bgPaint);
        if (borderColor.Alpha > 0)
            canvas.DrawRoundRect(bg, radius, radius, borderPaint);
        float baseline = bg.Top + pdfPad - textPaint.FontMetrics.Ascent;
        foreach (string line in cleanLines)
        {
            float textX = centered ? bg.Left + pdfPad : pdfPos.X + pdfPad * 1.5f;
            canvas.DrawText(line, textX, baseline, textPaint);
            baseline += lineHeight;
        }
    }

    private SKPoint PdfToScreen(SKPoint point) =>
        new((point.X - _panX) * _zoom, (point.Y - _panY) * _zoom);

    private void DrawAiActionDraftPreview(SKCanvas canvas, SKRect visiblePdf)
    {
        if (_aiActionDraftPreview == null || _aiActionDraftPreview.Actions.Count == 0)
            return;

        using var stroke = new SKPaint
        {
            Color = new SKColor(0x00, 0xBC, 0xD4, 230),
            StrokeWidth = 2.5f / _zoom,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            PathEffect = SKPathEffect.CreateDash([10f / _zoom, 5f / _zoom], 0),
        };
        using var fill = new SKPaint
        {
            Color = new SKColor(0x00, 0xBC, 0xD4, 42),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var dotFill = new SKPaint
        {
            Color = new SKColor(0x00, 0xBC, 0xD4, 235),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var dotStroke = new SKPaint
        {
            Color = SKColors.White,
            StrokeWidth = 1.5f / _zoom,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };

        foreach (SmartAiAction action in _aiActionDraftPreview.Actions)
        {
            if (!ActionMatchesPreviewPage(action))
                continue;

            var points = new List<SKPoint>();
            foreach (SmartAiActionPoint point in action.Points)
                points.Add(new SKPoint(point.X, point.Y));

            if (points.Count == 0 || !PointsVisible(points, visiblePdf))
                continue;

            bool isArea = action.MeasurementType.Equals("area", StringComparison.OrdinalIgnoreCase) ||
                          action.Type.Contains("area", StringComparison.OrdinalIgnoreCase);
            bool isPoint = action.MeasurementType.Equals("point", StringComparison.OrdinalIgnoreCase) ||
                           action.Type.Contains("point", StringComparison.OrdinalIgnoreCase);

            if (isArea && points.Count >= 3)
            {
                using var path = new SKPath();
                path.MoveTo(points[0]);
                for (int i = 1; i < points.Count; i++)
                    path.LineTo(points[i]);
                path.Close();
                canvas.DrawPath(path, fill);
                canvas.DrawPath(path, stroke);
                DrawAiActionDots(canvas, points, dotFill, dotStroke);
                DrawLabel(canvas, Centroid(points), AiActionLabel(action), "#00BCD4");
            }
            else if (!isPoint && points.Count >= 2)
            {
                using var path = new SKPath();
                path.MoveTo(points[0]);
                for (int i = 1; i < points.Count; i++)
                    path.LineTo(points[i]);
                canvas.DrawPath(path, stroke);
                DrawAiActionDots(canvas, points, dotFill, dotStroke);
                DrawLabel(canvas, points[^1], AiActionLabel(action), "#00BCD4");
            }
            else
            {
                DrawAiActionDots(canvas, points, dotFill, dotStroke);
                DrawLabel(canvas, points[0], AiActionLabel(action), "#00BCD4");
            }
        }
    }

    private bool ActionMatchesPreviewPage(SmartAiAction action)
    {
        if (string.IsNullOrWhiteSpace(action.Page) || string.IsNullOrWhiteSpace(_aiActionDraftPreviewPage))
            return true;

        return string.Equals(action.Page, _aiActionDraftPreviewPage, StringComparison.OrdinalIgnoreCase);
    }

    private void DrawAiActionDots(SKCanvas canvas, IReadOnlyList<SKPoint> points, SKPaint fill, SKPaint stroke)
    {
        float radius = 4.5f / _zoom;
        foreach (SKPoint point in points)
        {
            canvas.DrawCircle(point, radius, fill);
            canvas.DrawCircle(point, radius, stroke);
        }
    }

    private static string AiActionLabel(SmartAiAction action)
    {
        string label = string.IsNullOrWhiteSpace(action.Label) ? action.Type : action.Label;
        if (action.Confidence > 0)
            label += $" ({action.Confidence:P0})";
        return $"AI: {label}";
    }

    private void DrawAiMarkers(SKCanvas canvas, SKRect visiblePdf)
    {
        if (_aiMarkers.Count == 0)
            return;

        using var fill = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var stroke = new SKPaint
        {
            Color = SKColors.White.WithAlpha(245),
            StrokeWidth = 1.8f / _zoom,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };
        using var accent = new SKPaint
        {
            StrokeWidth = 2.2f / _zoom,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };

        foreach (SmartAiMarker marker in _aiMarkers)
        {
            SKPoint point = new(marker.PdfPoint.X, marker.PdfPoint.Y);
            if (!PointVisible(point, visiblePdf))
                continue;

            SKColor color = AiMarkerColor(marker);
            fill.Color = color.WithAlpha(220);
            accent.Color = color.WithAlpha(245);

            float size = 7f / _zoom;
            using var markerPath = new SKPath();
            markerPath.MoveTo(point.X, point.Y - size);
            markerPath.LineTo(point.X + size, point.Y);
            markerPath.LineTo(point.X, point.Y + size);
            markerPath.LineTo(point.X - size, point.Y);
            markerPath.Close();

            canvas.DrawPath(markerPath, fill);
            canvas.DrawPath(markerPath, stroke);

            if (marker.SampleKind.Equals("negative", StringComparison.OrdinalIgnoreCase) ||
                marker.SampleKind.Equals("ignore", StringComparison.OrdinalIgnoreCase))
            {
                float cross = size * 0.72f;
                canvas.DrawLine(point.X - cross, point.Y - cross, point.X + cross, point.Y + cross, accent);
                canvas.DrawLine(point.X + cross, point.Y - cross, point.X - cross, point.Y + cross, accent);
            }
            else
            {
                canvas.DrawCircle(point, 2.2f / _zoom, stroke);
            }

            DrawLabel(
                canvas,
                new SKPoint(point.X + size * 1.6f, point.Y - size * 1.6f),
                AiMarkerLabel(marker),
                ColorHex(color));
        }
    }

    private bool PointVisible(SKPoint point, SKRect visiblePdf)
    {
        float margin = 24f / Math.Max(_zoom, 0.001f);
        return point.X >= visiblePdf.Left - margin &&
               point.X <= visiblePdf.Right + margin &&
               point.Y >= visiblePdf.Top - margin &&
               point.Y <= visiblePdf.Bottom + margin;
    }

    private static SKColor AiMarkerColor(SmartAiMarker marker)
    {
        if (marker.SampleKind.Equals("ignore", StringComparison.OrdinalIgnoreCase))
            return new SKColor(0xEF, 0x6C, 0x00);
        if (marker.SampleKind.Equals("negative", StringComparison.OrdinalIgnoreCase))
            return new SKColor(0xD3, 0x2F, 0x2F);

        string type = marker.Type ?? "";
        if (type.Contains("height", StringComparison.OrdinalIgnoreCase))
            return new SKColor(0x7B, 0x1F, 0xA2);
        if (type.Contains("window", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("door", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("opening", StringComparison.OrdinalIgnoreCase))
            return new SKColor(0x19, 0x76, 0xD2);
        if (type.Contains("roof", StringComparison.OrdinalIgnoreCase))
            return new SKColor(0x2E, 0x7D, 0x32);
        if (type.Contains("corner", StringComparison.OrdinalIgnoreCase))
            return new SKColor(0x00, 0x96, 0x88);

        return new SKColor(0x00, 0xBC, 0xD4);
    }

    private static string AiMarkerLabel(SmartAiMarker marker)
    {
        string label = marker.Type switch
        {
            "exterior_corner" => "ext corner",
            "interior_corner" => "int corner",
            "wall_height_sample" => "height",
            "window_sample" => "window",
            "door_sample" => "door",
            "opening_sample" => "opening",
            "roof_note" => "roof note",
            "roof_edge_sample" => "roof edge",
            "dimension_text_sample" => "dimension",
            "ignore_area" => "ignore",
            _ => string.IsNullOrWhiteSpace(marker.Type) ? "marker" : marker.Type.Replace('_', ' '),
        };

        if (marker.SampleKind.Equals("negative", StringComparison.OrdinalIgnoreCase))
            label = $"not {label}";
        else if (marker.SampleKind.Equals("ignore", StringComparison.OrdinalIgnoreCase))
            label = "ignore";

        return $"M: {label}";
    }

    private static string ColorHex(SKColor color) =>
        $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";

    private static bool PointsVisible(IReadOnlyList<SKPoint> points, SKRect visiblePdf)
    {
        SKRect bounds = PointsBounds(points);
        return bounds.Left <= visiblePdf.Right &&
               bounds.Right >= visiblePdf.Left &&
               bounds.Top <= visiblePdf.Bottom &&
               bounds.Bottom >= visiblePdf.Top;
    }

    private static SKRect PointsBounds(IReadOnlyList<SKPoint> points)
    {
        if (points.Count == 0)
            return SKRect.Empty;

        float left = points[0].X;
        float right = points[0].X;
        float top = points[0].Y;
        float bottom = points[0].Y;
        for (int i = 1; i < points.Count; i++)
        {
            SKPoint point = points[i];
            left = Math.Min(left, point.X);
            right = Math.Max(right, point.X);
            top = Math.Min(top, point.Y);
            bottom = Math.Max(bottom, point.Y);
        }

        return new SKRect(left, top, right, bottom);
    }

    // ── Draw in-progress line / area ──────────────────────────────────────────

    private void DrawInProgress(SKCanvas canvas)
    {
        DrawPdfLayerTraceOverlay(canvas);

        using var tempPaint = new SKPaint
        {
            Color       = TempColor,
            StrokeWidth = 2f / _zoom,
            IsAntialias = true,
            Style       = SKPaintStyle.Stroke,
        };
        using var dotPaint = new SKPaint
        {
            Color       = TempColor,
            IsAntialias = true,
            Style       = SKPaintStyle.Fill,
        };

        float r = 4f / _zoom;

        foreach (var p in _drawPts)
            canvas.DrawCircle(p, r, dotPaint);

        if (_drawPts.Count >= 2)
        {
            for (int i = 1; i < _drawPts.Count; i++)
                canvas.DrawLine(_drawPts[i - 1], _drawPts[i], tempPaint);
        }

        // Rubber-band
        if (_drawPts.Count > 0 && _rubberEnd.HasValue)
        {
            using var rubber = new SKPaint
            {
                Color       = TempColor,
                StrokeWidth = 1f / _zoom,
                IsAntialias = true,
                Style       = SKPaintStyle.Stroke,
                PathEffect  = SKPathEffect.CreateDash([4f / _zoom, 4f / _zoom], 0),
            };
            if (_tool == ViewerTool.DrawRect)
                canvas.DrawRect(NormalizeRect(_drawPts[0], _rubberEnd.Value), rubber);
            else
                canvas.DrawLine(_drawPts[^1], _rubberEnd.Value, rubber);
        }

        if (_joistDirectionMeasurement != null)
        {
            using var joistPaint = new SKPaint
            {
                Color = new SKColor(0x00, 0x96, 0x88),
                StrokeWidth = 2.2f / _zoom,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                PathEffect = SKPathEffect.CreateDash([7f / _zoom, 4f / _zoom], 0),
            };
            using var joistDot = new SKPaint
            {
                Color = new SKColor(0x00, 0x96, 0x88),
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };
            float jr = 5f / _zoom;
            foreach (SKPoint point in _joistDirectionPts)
                canvas.DrawCircle(point, jr, joistDot);
            if (_joistDirectionPts.Count == 1 && _joistDirectionRubberEnd.HasValue)
                canvas.DrawLine(_joistDirectionPts[0], _joistDirectionRubberEnd.Value, joistPaint);
        }

        // Scale line
        if (_scalePts.Count >= 1)
        {
            using var scPaint = new SKPaint
            {
                Color       = ScaleClr,
                StrokeWidth = 2f / _zoom,
                IsAntialias = true,
                Style       = SKPaintStyle.Stroke,
            };
            using var scDot = new SKPaint
            {
                Color       = ScaleClr,
                IsAntialias = true,
                Style       = SKPaintStyle.Fill,
            };
            float sr = 6f / _zoom;
            foreach (var p in _scalePts)
                canvas.DrawCircle(p, sr, scDot);
            if (_scalePts.Count == 2)
                canvas.DrawLine(_scalePts[0], _scalePts[1], scPaint);
        }

        if (_snapPreview.HasValue)
        {
            float half = SnapMarkerScreenPx / Math.Max(_zoom, 0.001f);
            var rect = new SKRect(
                _snapPreview.Value.X - half,
                _snapPreview.Value.Y - half,
                _snapPreview.Value.X + half,
                _snapPreview.Value.Y + half);
            using var snapFill = new SKPaint
            {
                Color = new SKColor(0xE5, 0x39, 0x35, 80),
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };
            using var snapStroke = new SKPaint
            {
                Color = new SKColor(0xE5, 0x39, 0x35),
                StrokeWidth = 2f / Math.Max(_zoom, 0.001f),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
            };
            canvas.DrawRect(rect, snapFill);
            canvas.DrawRect(rect, snapStroke);
        }

        if (_boxSelecting)
        {
            float safeZoom = Math.Max(_zoom, 0.001f);
            SKRect rect = NormalizeRect(_boxSelectStartPdf, _boxSelectEndPdf);
            using var fill = new SKPaint
            {
                Color = new SKColor(0x1E, 0x88, 0xE5, 32),
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };
            using var stroke = new SKPaint
            {
                Color = new SKColor(0x1E, 0x88, 0xE5),
                StrokeWidth = 1.5f / safeZoom,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                PathEffect = SKPathEffect.CreateDash([8f / safeZoom, 4f / safeZoom], 0),
            };
            canvas.DrawRect(rect, fill);
            canvas.DrawRect(rect, stroke);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Mouse / keyboard events
    // ═════════════════════════════════════════════════════════════════════════

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        Focus();
        var pos    = e.GetPosition(this);
        float fac  = e.Delta > 0 ? 1.12f : 1f / 1.12f;
        ApplyZoom(fac, (float)pos.X, (float)pos.Y);
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        var pos = e.GetPosition(this);

        if (e.RightButton == MouseButtonState.Pressed && _pageBitmap != null)
        {
            var pdf = ScreenToPdf((float)pos.X, (float)pos.Y);
            _lastPointerPdf = pdf;
            _rightClickStart = pos;
            _rightClickPdf = pdf;
            _rightClickMoved = false;
            _rightClickMeasurement = null;
            if (TryHitMeasurement(pdf, out Measurement measurement))
            {
                _rightClickMeasurement = measurement;
                if (_selectedMeasurements.Contains(measurement))
                    SetSelectedMeasurements(GetSelectedMeasurements(), measurement, -1);
                else
                    SelectMeasurement(measurement, -1);
            }
        }

        if (e.LeftButton == MouseButtonState.Pressed &&
            _pageBitmap != null)
        {
            var pdf = ScreenToPdf((float)pos.X, (float)pos.Y);
            _lastPointerPdf = pdf;
            if (_joistDirectionMeasurement != null)
            {
                HandleJoistDirectionClick(ResolveDigitizerPoint(pdf, updatePreview: true));
                e.Handled = true;
                return;
            }

            if (_pdfLayerTraceEnabled)
            {
                AdvancePdfLayerTrace(pdf);
                e.Handled = true;
                return;
            }

            if (_tool == ViewerTool.Select)
            {
                bool hasInProgressInput = _drawPts.Count > 0 || _scalePts.Count > 0 || _rubberEnd.HasValue;
                if (IsSelectionModifierActive() && TryHitMeasurement(pdf, out Measurement toggled))
                {
                    ToggleMeasurementSelection(toggled);
                    e.Handled = true;
                    return;
                }

                bool preserveSelectionForAdd = IsSelectionModifierActive();
                if (TryBeginMeasurementEdit(pdf, pos, clearSelectionOnMiss: !hasInProgressInput && !preserveSelectionForAdd))
                {
                    if (_draggingVertex || _draggingMeasurement)
                        CaptureMouse();
                    e.Handled = true;
                    return;
                }

                BeginBoxSelection(pdf, additive: IsSelectionModifierActive());
                e.Handled = true;
                return;
            }
        }

        bool isPanButton = e.MiddleButton == MouseButtonState.Pressed
                        || e.RightButton  == MouseButtonState.Pressed
                        || (_tool == ViewerTool.Pan && e.LeftButton == MouseButtonState.Pressed);

        if (isPanButton)
        {
            _dragStart  = pos;
            _dragPanX0  = _panX;
            _dragPanY0  = _panY;
            CaptureMouse();
            _isViewDragging = true;
            e.Handled = true;
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed && _pageBitmap != null)
        {
            if (!EnsureScaleForLinearArea())
            {
                e.Handled = true;
                return;
            }

            var pdf = ResolveDigitizerPoint(ScreenToPdf((float)pos.X, (float)pos.Y), updatePreview: true);
            ClearSelection();

            // Double-click (ClickCount==2) finishes a line/area without adding an extra point.
            // Single-click (ClickCount==1) adds a vertex as usual.
            if (e.ClickCount == 2 && _tool is ViewerTool.Line or ViewerTool.Area)
            {
                FinalizeDrawing();
                e.Handled = true;
                return;
            }

            HandleLeftClick(pdf);
        }
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (_rightClickStart.HasValue &&
            DistanceSquared(_rightClickStart.Value, pos) > 16)
        {
            _rightClickMoved = true;
        }

        if (_draggingVertex &&
            _selectedMeasurement != null &&
            _selectedVertexIndex >= 0)
        {
            SKPoint delta = ScreenDragDeltaToPdf(pos);
            _selectedMeasurement.Points[_selectedVertexIndex] = new SKPoint(
                _dragVertexOriginalPoint.X + delta.X,
                _dragVertexOriginalPoint.Y + delta.Y);
            _dragMeasurementChanged = true;
            PostDragStatus("Dragging vertex", delta);
            RequestRepaint();
            e.Handled = true;
            return;
        }

        if (_draggingMeasurement &&
            _selectedMeasurement != null)
        {
            SKPoint delta = ScreenDragDeltaToPdf(pos);
            if (_dragSelectionOriginalPoints.Count > 0)
            {
                foreach (var (measurement, originalPoints) in _dragSelectionOriginalPoints)
                {
                    for (int i = 0; i < measurement.Points.Count && i < originalPoints.Count; i++)
                    {
                        SKPoint original = originalPoints[i];
                        measurement.Points[i] = new SKPoint(original.X + delta.X, original.Y + delta.Y);
                    }
                }
            }
            else
            {
                for (int i = 0; i < _selectedMeasurement.Points.Count && i < _dragMeasurementOriginalPoints.Count; i++)
                {
                    SKPoint original = _dragMeasurementOriginalPoints[i];
                    _selectedMeasurement.Points[i] = new SKPoint(original.X + delta.X, original.Y + delta.Y);
                }
            }

            _dragMeasurementChanged = true;
            PostDragStatus("Dragging measurement", delta);
            RequestRepaint();
            e.Handled = true;
            return;
        }

        if (_boxSelecting)
        {
            _boxSelectEndPdf = ScreenToPdf((float)pos.X, (float)pos.Y);
            _lastPointerPdf = _boxSelectEndPdf;
            PostBoxSelectionStatus();
            RequestRepaint();
            e.Handled = true;
            return;
        }

        if (_dragStart.HasValue && (
            e.MiddleButton == MouseButtonState.Pressed ||
            e.RightButton  == MouseButtonState.Pressed ||
            (_tool == ViewerTool.Pan && e.LeftButton == MouseButtonState.Pressed)))
        {
            _panX = _dragPanX0 - (float)((pos.X - _dragStart.Value.X) / _zoom);
            _panY = _dragPanY0 - (float)((pos.Y - _dragStart.Value.Y) / _zoom);
            RequestRepaint();
            e.Handled = true;
            return;
        }

        var pointerPdf = ScreenToPdf((float)pos.X, (float)pos.Y);
        _lastPointerPdf = pointerPdf;
        if (_joistDirectionMeasurement != null)
        {
            pointerPdf = ResolveDigitizerPoint(pointerPdf, updatePreview: true);
            _lastPointerPdf = pointerPdf;
            if (_joistDirectionPts.Count > 0)
            {
                _joistDirectionRubberEnd = pointerPdf;
                RequestRepaint();
            }
            PostStatus(_joistDirectionPts.Count == 0
                ? "Joist direction: click the first point."
                : "Joist direction: click the second point.");
            e.Handled = true;
            return;
        }

        if (_pdfLayerTraceEnabled)
        {
            SetSnapPreview(null);
            PostPdfLayerTraceStatus();
            e.Handled = true;
            return;
        }

        if (_pageBitmap != null &&
            _tool is ViewerTool.Scale or ViewerTool.Ruler or ViewerTool.DrawLine or ViewerTool.DrawArrow or ViewerTool.DrawRect or ViewerTool.Point or ViewerTool.Line or ViewerTool.Area &&
            !IsMissingScaleForLinearArea())
        {
            pointerPdf = ResolveDigitizerPoint(pointerPdf, updatePreview: true);
            _lastPointerPdf = pointerPdf;
        }
        else
        {
            SetSnapPreview(null);
        }

        // Rubber-band
        if (_drawPts.Count > 0 && _tool is ViewerTool.Line or ViewerTool.Area or ViewerTool.Ruler or ViewerTool.DrawLine or ViewerTool.DrawArrow or ViewerTool.DrawRect)
        {
            _rubberEnd = pointerPdf;
            RequestRepaint();
        }

        if (IsMissingScaleForLinearArea())
            PostScaleRequiredStatus();
        else
            PostPointerStatus(pointerPdf);

        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        bool showContextMenu = e.ChangedButton == MouseButton.Right &&
                               _rightClickStart.HasValue &&
                               !_rightClickMoved &&
                               _rightClickPdf.HasValue &&
                               _pageBitmap != null;
        Point contextScreen = _rightClickStart ?? e.GetPosition(this);
        SKPoint contextPdf = _rightClickPdf ?? ScreenToPdf((float)contextScreen.X, (float)contextScreen.Y);
        Measurement? contextMeasurement = _rightClickMeasurement;

        if (_draggingVertex || _draggingMeasurement)
        {
            FinishMeasurementDrag();
            e.Handled = true;
            return;
        }

        if (_boxSelecting && e.ChangedButton == MouseButton.Left)
        {
            FinishBoxSelection();
            e.Handled = true;
            return;
        }

        if (_dragStart.HasValue &&
            e.MiddleButton != MouseButtonState.Pressed &&
            e.RightButton  != MouseButtonState.Pressed &&
            e.LeftButton   != MouseButtonState.Pressed)
        {
            _dragStart = null;
            _isViewDragging = false;
            ReleaseMouseCapture();
            RequestRepaint();
        }

        if (e.ChangedButton == MouseButton.Right)
        {
            _rightClickStart = null;
            _rightClickPdf = null;
            _rightClickMeasurement = null;
            _rightClickMoved = false;
        }

        if (showContextMenu)
        {
            ContextRequested?.Invoke(new ViewportContextRequest(
                contextScreen.X,
                contextScreen.Y,
                contextPdf.X,
                contextPdf.Y,
                _pageFolder,
                contextMeasurement));
        }
        e.Handled = true;
    }

    private void CancelJoistDirectionCapture()
    {
        if (_joistDirectionMeasurement == null && _joistDirectionPts.Count == 0)
            return;

        _joistDirectionMeasurement = null;
        _joistDirectionPts.Clear();
        _joistDirectionRubberEnd = null;
        SetSnapPreview(null);
        RequestRepaint();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        FinishMeasurementDrag();
        if (_boxSelecting && Mouse.LeftButton != MouseButtonState.Pressed)
            CancelBoxSelection();
        base.OnLostMouseCapture(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_pdfLayerTraceEnabled && e.Key == Key.Tab)
        {
            if (_pdfLayerTraceChoosingLayer)
                CyclePdfLayerTraceCandidate();
            else
                CyclePdfLayerTraceMode();
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                if (_pdfLayerTraceEnabled)
                {
                    if (_pdfLayerTraceChoosingLayer || _pdfLayerTraceReadyToApply)
                    {
                        ClearPdfLayerTraceSession(keepCandidateLayer: true);
                        PublishPdfLayerTraceState();
                        PostPdfLayerTraceStatus();
                    }
                    else
                    {
                        SetPdfLayerTraceEnabled(false);
                    }
                    e.Handled = true;
                    break;
                }

                if (_joistDirectionMeasurement != null)
                {
                    CancelJoistDirectionCapture();
                    PostStatus("Joist direction cancelled.");
                    e.Handled = true;
                    break;
                }

                CompleteOrCancelDrawing();
                e.Handled = true;
                break;
            case Key.Enter:
                if (_pdfLayerTraceEnabled)
                {
                    AdvancePdfLayerTrace(_lastPointerPdf);
                    e.Handled = true;
                }
                break;
            case Key.T when Keyboard.Modifiers == ModifierKeys.None:
                TogglePdfLayerTraceEnabled();
                e.Handled = true;
                break;
            case Key.C:
                if (Keyboard.Modifiers == ModifierKeys.Control)
                {
                    CopyMeasurementsRequested?.Invoke(GetSelectedMeasurements());
                    e.Handled = true;
                }
                else if (_drawPts.Count > 0 && _tool is ViewerTool.Line or ViewerTool.Area)
                {
                    CompleteOrCancelDrawing();
                    e.Handled = true;
                }
                break;
            case Key.V when Keyboard.Modifiers == ModifierKeys.Control:
                PasteMeasurementsRequested?.Invoke(_lastPointerPdf);
                e.Handled = true;
                break;
            case Key.Delete:
                DeleteSelectedMeasurement();
                e.Handled = true;
                break;
            case Key.F:
                ZoomFit();
                e.Handled = true;
                break;
            case Key.F3:
                SnapEnabled = !SnapEnabled;
                e.Handled = true;
                break;
            case Key.F8:
                OrthoEnabled = !OrthoEnabled;
                e.Handled = true;
                break;
            case Key.Add when Keyboard.Modifiers == ModifierKeys.Control:
            case Key.OemPlus when Keyboard.Modifiers == ModifierKeys.Control:
                ZoomIn(); e.Handled = true;
                break;
            case Key.Subtract when Keyboard.Modifiers == ModifierKeys.Control:
            case Key.OemMinus  when Keyboard.Modifiers == ModifierKeys.Control:
                ZoomOut(); e.Handled = true;
                break;
            case Key.Z when Keyboard.Modifiers == ModifierKeys.Control:
                UndoLast(); e.Handled = true;
                break;
            case Key.A when Keyboard.Modifiers == ModifierKeys.Control:
                SelectAllActivePageMeasurements();
                e.Handled = true;
                break;
            case Key.Back:
                UndoLast(); e.Handled = true;
                break;
            // Tool hotkeys
            case Key.V: ToolChanged?.Invoke("pan");   e.Handled = true; break;
            case Key.E: ToolChanged?.Invoke("select"); e.Handled = true; break;
            case Key.S: ToolChanged?.Invoke("scale"); e.Handled = true; break;
            case Key.R: ToolChanged?.Invoke("ruler"); e.Handled = true; break;
            case Key.D: ToolChanged?.Invoke("drawline"); e.Handled = true; break;
            case Key.B: ToolChanged?.Invoke("drawrect"); e.Handled = true; break;
            case Key.P: ToolChanged?.Invoke("point"); e.Handled = true; break;
            case Key.L: ToolChanged?.Invoke("line");  e.Handled = true; break;
            case Key.A: ToolChanged?.Invoke("area");  e.Handled = true; break;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Drawing tool logic
    // ═════════════════════════════════════════════════════════════════════════

    private void HandleLeftClick(SKPoint pdf)
    {
        if (!EnsureScaleForLinearArea())
            return;

        switch (_tool)
        {
            case ViewerTool.Scale:
                HandleScaleClick(pdf);
                break;
            case ViewerTool.Ruler:
                AddTwoPointAnnotation(pdf, "dimension");
                break;
            case ViewerTool.DrawLine:
                AddTwoPointAnnotation(pdf, "line");
                break;
            case ViewerTool.DrawArrow:
                AddTwoPointAnnotation(pdf, "arrow");
                break;
            case ViewerTool.DrawRect:
                AddTwoPointAnnotation(pdf, "rectangle");
                break;
            case ViewerTool.Point:
                _drawPts.Add(pdf);
                FinalizeDrawing();
                break;
            case ViewerTool.Line:
            case ViewerTool.Area:
                _drawPts.Add(pdf);
                RequestRepaint();
                PostRecordPrompt();
                break;
        }
    }

    private void AddTwoPointAnnotation(SKPoint pdf, string kind)
    {
        _drawPts.Add(pdf);
        if (_drawPts.Count < 2)
        {
            RequestRepaint();
            PostRecordPrompt();
            return;
        }

        FinalizeAnnotation(kind);
    }

    private void HandleJoistDirectionClick(SKPoint pdf)
    {
        if (_joistDirectionMeasurement == null)
            return;

        _joistDirectionPts.Add(pdf);
        if (_joistDirectionPts.Count == 1)
        {
            _joistDirectionRubberEnd = pdf;
            PostStatus("Joist direction: click the second point.");
            RequestRepaint();
            return;
        }

        SKPoint start = _joistDirectionPts[0];
        SKPoint end = _joistDirectionPts[1];
        Measurement area = _joistDirectionMeasurement;
        CancelJoistDirectionCapture();
        JoistDirectionCaptured?.Invoke(area, start, end);
        RequestRepaint();
    }

    private void HandleScaleClick(SKPoint pdf)
    {
        _scalePts.Add(pdf);
        if (_scalePts.Count == 2)
        {
            float dx = _scalePts[1].X - _scalePts[0].X;
            float dy = _scalePts[1].Y - _scalePts[0].Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);  // PDF points

            const double PT_M = 25.4 / 72.0 / 1000.0;
            string hint =
                $"Measured {dist:F1} pt on PDF\n" +
                $"(At 1:100 ≈ {dist * PT_M * 100:F3} m  |  1:50 ≈ {dist * PT_M * 50:F3} m)\n\n" +
                "Enter real distance in metres:";

            var dlg = new ScaleInputDialog(hint);
            if (dlg.ShowDialog() == true && dlg.Value > 0)
            {
                ScaleMetersPerPt = dlg.Value / dist;
                ScaleChanged?.Invoke(ScaleMetersPerPt);
                PostStatus($"Scale set: 1:{ScaleMetersPerPt / PT_M:F0}  (1pt = {ScaleMetersPerPt:F6} m)");
            }

            _scalePts.Clear();
            RequestRepaint();
        }
        else
        {
            RequestRepaint();
            PostStatus("Scale: click the second point of a known distance.");
        }
    }

    private void FinalizeDrawing()
    {
        if (_drawPts.Count == 0) return;
        if (!EnsureScaleForLinearArea())
            return;

        if (_tool == ViewerTool.Line  && _drawPts.Count < 2) { CancelDrawing(); return; }
        if (_tool == ViewerTool.Area  && _drawPts.Count < 3) { CancelDrawing(); return; }

        var m = new Measurement
        {
            MType      = _tool.ToString().ToLower(),
            Points     = new List<SKPoint>(_drawPts),
            Color      = ActiveColor,
            PageFolder = _pageFolder,
            TakeoffFolder = ActiveTakeoffFolder,
            ScaleMetersPerPt = ScaleMetersPerPt,
        };
        _measurements.Add(m);
        _drawPts.Clear();
        _rubberEnd = null;
        SetSnapPreview(null);
        RequestRepaint();
        PostStatus($"Added {EntryTitle(m.MType)}  {m.Label(ScaleMetersPerPt, UnitMode)}");
        MeasurementAdded?.Invoke(m);
        PostRecordPrompt();
    }

    private void FinalizeAnnotation(string kind)
    {
        if (_drawPts.Count < 2)
            return;

        string normalizedKind = SmartTakeoffsJobStore.NormalizePageAnnotationKind(kind);
        var annotation = new PageAnnotation
        {
            Kind = normalizedKind,
            Points = _drawPts.Take(2).ToList(),
            Color = normalizedKind == "dimension" ? "#1565C0" : ActiveColor,
            PageFolder = _pageFolder,
            ScaleMetersPerPt = ScaleMetersPerPt,
        };
        _annotations.Add(annotation);
        _drawPts.Clear();
        _rubberEnd = null;
        SetSnapPreview(null);
        RequestRepaint();
        PostStatus(normalizedKind == "dimension"
            ? $"Added dimension markup: {AnnotationLabel(annotation)}."
            : $"Added {ToolTitle(normalizedKind)} markup.");
        PageAnnotationAdded?.Invoke(annotation);
        PostRecordPrompt();
    }

    private void CompleteOrCancelDrawing()
    {
        if (IsMissingScaleForLinearArea())
        {
            EnsureScaleForLinearArea();
            return;
        }

        if (_tool == ViewerTool.Line && _drawPts.Count >= 2)
        {
            FinalizeDrawing();
            return;
        }

        if (_tool == ViewerTool.Area && _drawPts.Count >= 3)
        {
            FinalizeDrawing();
            return;
        }

        CancelDrawing();
        PostStatus("Cancelled.");
    }

    private bool EnsureScaleForLinearArea()
    {
        if (!IsMissingScaleForLinearArea())
            return true;

        if (_drawPts.Count > 0 || _rubberEnd.HasValue || _snapPreview.HasValue)
        {
            _drawPts.Clear();
            _rubberEnd = null;
            SetSnapPreview(null);
            RequestRepaint();
        }

        PostScaleRequiredStatus();
        return false;
    }

    private bool IsMissingScaleForLinearArea() =>
        _tool is ViewerTool.Line or ViewerTool.Area or ViewerTool.Ruler && ScaleMetersPerPt <= 0;

    private void PostScaleRequiredStatus()
    {
        string tool = _tool switch
        {
            ViewerTool.Area => "Area",
            ViewerTool.Ruler => "Ruler",
            _ => "Line",
        };
        string mode = _tool == ViewerTool.Ruler ? "markup" : "Record";
        PostStatus($"{tool} {mode} blocked: set sheet scale first with Scale or PDF Auto Scale. Count and drawing markups can be recorded without scale.");
    }

    private void PostRecordPrompt()
    {
        string modes = DigitizerModeSuffix();
        switch (_tool)
        {
            case ViewerTool.Point:
                PostStatus($"Count Record: click each item to add a count. Turn Record off for Pan.{modes}");
                break;
            case ViewerTool.Line:
                if (IsMissingScaleForLinearArea())
                {
                    PostScaleRequiredStatus();
                    break;
                }

                PostStatus(_drawPts.Count switch
                {
                    0 => $"Line Record: click the first point.{modes}",
                    1 => $"Line Record: click the next point. Backspace/Ctrl+Z undo.{modes}",
                    _ => $"Line Record: click next point, or Esc / C / double-click to finish.{modes}",
                });
                break;
            case ViewerTool.Area:
                if (IsMissingScaleForLinearArea())
                {
                    PostScaleRequiredStatus();
                    break;
                }

                PostStatus(_drawPts.Count switch
                {
                    0 => $"Area Record: click the first corner.{modes}",
                    1 => $"Area Record: click the next corner. Backspace/Ctrl+Z undo.{modes}",
                    2 => $"Area Record: click at least one more corner, then finish.{modes}",
                    _ => $"Area Record: click next corner, or Esc / C / double-click to finish.{modes}",
                });
                break;
            case ViewerTool.Scale:
                PostStatus(_scalePts.Count == 0
                    ? $"Scale: click the first point of a known distance.{modes}"
                    : $"Scale: click the second point of a known distance.{modes}");
                break;
            case ViewerTool.Ruler:
                if (IsMissingScaleForLinearArea())
                {
                    PostScaleRequiredStatus();
                    break;
                }

                PostStatus(_drawPts.Count == 0
                    ? $"Ruler: click the first endpoint.{modes}"
                    : $"Ruler: click the second endpoint to place the dimension label.{modes}");
                break;
            case ViewerTool.DrawLine:
                PostStatus(_drawPts.Count == 0
                    ? $"Draw line: click the first endpoint.{modes}"
                    : $"Draw line: click the second endpoint.{modes}");
                break;
            case ViewerTool.DrawArrow:
                PostStatus(_drawPts.Count == 0
                    ? $"Arrow: click the tail point.{modes}"
                    : $"Arrow: click the arrow head point.{modes}");
                break;
            case ViewerTool.DrawRect:
                PostStatus(_drawPts.Count == 0
                    ? $"Box: click the first corner.{modes}"
                    : $"Box: click the opposite corner.{modes}");
                break;
            case ViewerTool.Select:
                PostStatus("Select: left-drag a box to select measurements. Ctrl+click toggles, Ctrl+C copies, Ctrl+V pastes.");
                break;
        }
    }

    private string DigitizerModeSuffix()
    {
        var modes = new List<string>();
        if (SnapEnabled)
            modes.Add("Snap F3");
        if (OrthoEnabled)
            modes.Add("Ortho F8");
        return modes.Count == 0 ? "" : $" [{string.Join(", ", modes)}]";
    }

    private static string ToolTitle(string type) =>
        type switch
        {
            "point" => "Count",
            "line" => "Line",
            "area" => "Area",
            "dimension" => "Ruler",
            "arrow" => "Arrow",
            "rectangle" => "Box",
            "select" => "Select",
            _ => type,
        };

    private static string EntryTitle(string type) =>
        type == "point" ? "Count mark" : $"{ToolTitle(type)} section";

    private void CancelDrawing()
    {
        _drawPts.Clear();
        _scalePts.Clear();
        _rubberEnd = null;
        _boxSelecting = false;
        SetSnapPreview(null);
        if (_draggingVertex && IsMouseCaptured)
            ReleaseMouseCapture();
        ClearSelection();
        RequestRepaint();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════════════════

    private SKPoint ResolveDigitizerPoint(SKPoint rawPdf, bool updatePreview)
    {
        if (SnapEnabled && TryFindSnapPoint(rawPdf, out SKPoint snapped))
        {
            if (updatePreview)
                SetSnapPreview(snapped);
            return snapped;
        }

        if (updatePreview)
            SetSnapPreview(null);

        return TryGetOrthoAnchor(out SKPoint anchor) && IsOrthoActive()
            ? ApplyOrtho(anchor, rawPdf)
            : rawPdf;
    }

    private bool IsOrthoActive()
    {
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        return OrthoEnabled ^ shift;
    }

    private static bool IsSelectionModifierActive() =>
        (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != ModifierKeys.None;

    private bool TryGetOrthoAnchor(out SKPoint anchor)
    {
        if (_tool is ViewerTool.Line or ViewerTool.Area or ViewerTool.Ruler or ViewerTool.DrawLine or ViewerTool.DrawArrow or ViewerTool.DrawRect &&
            _drawPts.Count > 0)
        {
            anchor = _drawPts[^1];
            return true;
        }

        if (_tool == ViewerTool.Scale && _scalePts.Count > 0)
        {
            anchor = _scalePts[^1];
            return true;
        }

        anchor = default;
        return false;
    }

    private static SKPoint ApplyOrtho(SKPoint anchor, SKPoint point)
    {
        float dx = point.X - anchor.X;
        float dy = point.Y - anchor.Y;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length <= 0.001f)
            return point;

        float angle = MathF.Atan2(dy, dx);
        float step = MathF.PI / 4f;
        float snappedAngle = MathF.Round(angle / step) * step;
        return new SKPoint(
            anchor.X + MathF.Cos(snappedAngle) * length,
            anchor.Y + MathF.Sin(snappedAngle) * length);
    }

    private bool TryFindSnapPoint(SKPoint rawPdf, out SKPoint snapped)
    {
        float tolerance = SnapToleranceScreenPx / Math.Max(_zoom, 0.001f);
        float best = tolerance * tolerance;
        SKPoint bestPoint = default;
        bool found = false;

        void Consider(SKPoint candidate)
        {
            float distance = DistanceSquared(rawPdf, candidate);
            if (distance >= best)
                return;

            best = distance;
            bestPoint = candidate;
            found = true;
        }

        for (int i = 0; i < _drawPts.Count; i++)
        {
            if (i == _drawPts.Count - 1 && _tool is ViewerTool.Line or ViewerTool.Area)
                continue;

            Consider(_drawPts[i]);
        }

        foreach (SKPoint point in _scalePts)
            Consider(point);

        foreach (Measurement measurement in _measurements)
        {
            if (!IsMeasurementOnActivePage(measurement))
                continue;

            foreach (SKPoint point in measurement.Points)
                Consider(point);
        }

        snapped = bestPoint;
        return found;
    }

    private void SetSnapPreview(SKPoint? point)
    {
        bool changed = (_snapPreview.HasValue != point.HasValue) ||
                       (_snapPreview.HasValue && point.HasValue &&
                        DistanceSquared(_snapPreview.Value, point.Value) > 0.001f);
        _snapPreview = point;
        if (changed)
            RequestRepaint();
    }

    private bool TryBeginMeasurementEdit(SKPoint pdf, Point screen, bool clearSelectionOnMiss)
    {
        if (_selectedMeasurements.Count > 1 &&
            TryHitSelectedMeasurement(pdf, out Measurement groupMeasurement))
        {
            BeginMeasurementMove(groupMeasurement, screen);
            return true;
        }

        if (TryHitSelectedVertex(pdf, out Measurement selectedVertexMeasurement, out int selectedVertexIndex) ||
            TryHitVertex(pdf, out selectedVertexMeasurement, out selectedVertexIndex))
        {
            BeginVertexEdit(selectedVertexMeasurement, selectedVertexIndex, screen);
            return true;
        }

        if (TryHitSelectedMeasurement(pdf, out Measurement selectedMeasurement) ||
            TryHitMeasurement(pdf, out selectedMeasurement))
        {
            BeginMeasurementMove(selectedMeasurement, screen);
            return true;
        }

        if (clearSelectionOnMiss)
        {
            ClearSelection();
            RequestRepaint();
        }

        return false;
    }

    private void BeginVertexEdit(Measurement measurement, int vertexIndex, Point screen)
    {
        ClearInProgressInputForEdit();
        _draggingVertex = true;
        _draggingMeasurement = false;
        _dragMeasurementChanged = false;
        _dragScreenStart = screen;
        _dragVertexOriginalPoint = measurement.Points[vertexIndex];
        _dragSelectionOriginalPoints.Clear();
        CaptureMouse();
        SelectMeasurement(measurement, vertexIndex);
        PostStatus($"Editing {ToolTitle(measurement.MType)} vertex {vertexIndex + 1}. Drag to move.");
    }

    private void BeginMeasurementMove(Measurement measurement, Point screen)
    {
        ClearInProgressInputForEdit();
        _draggingVertex = false;
        _draggingMeasurement = true;
        _dragMeasurementChanged = false;
        _dragScreenStart = screen;
        CaptureMouse();
        if (_selectedMeasurements.Contains(measurement))
            SetSelectedMeasurements(GetSelectedMeasurements(), measurement, -1);
        else
            SelectMeasurement(measurement, -1);

        _dragMeasurementOriginalPoints = measurement.Points.ToList();
        _dragSelectionOriginalPoints.Clear();
        var selected = GetSelectedMeasurements();
        if (selected.Count > 1 && selected.Contains(measurement))
        {
            foreach (Measurement selectedMeasurement in selected)
                _dragSelectionOriginalPoints[selectedMeasurement] = selectedMeasurement.Points.ToList();
        }

        PostStatus(selected.Count > 1
            ? $"Moving {selected.Count} selected measurements."
            : $"Moving {EntryTitle(measurement.MType)}. Drag the body to move; drag blue handles to reshape.");
    }

    private void FinishMeasurementDrag()
    {
        if (!_draggingVertex && !_draggingMeasurement)
            return;

        if (_dragMeasurementChanged && _dragSelectionOriginalPoints.Count > 0)
        {
            foreach (Measurement measurement in _dragSelectionOriginalPoints.Keys.ToList())
                MeasurementChanged?.Invoke(measurement);
        }
        else if (_dragMeasurementChanged && _selectedMeasurement != null)
        {
            MeasurementChanged?.Invoke(_selectedMeasurement);
        }

        _draggingVertex = false;
        _draggingMeasurement = false;
        _dragMeasurementChanged = false;
        _dragMeasurementOriginalPoints.Clear();
        _dragSelectionOriginalPoints.Clear();
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        RequestRepaint();
    }

    private void ClearInProgressInputForEdit()
    {
        if (_drawPts.Count == 0 && _scalePts.Count == 0 && !_rubberEnd.HasValue && !_snapPreview.HasValue)
            return;

        _drawPts.Clear();
        _scalePts.Clear();
        _rubberEnd = null;
        SetSnapPreview(null);
    }

    private SKPoint ScreenDragDeltaToPdf(Point screen)
    {
        float safeZoom = Math.Max(_zoom, 0.001f);
        return new SKPoint(
            (float)((screen.X - _dragScreenStart.X) / safeZoom),
            (float)((screen.Y - _dragScreenStart.Y) / safeZoom));
    }

    private void PostDragStatus(string label, SKPoint delta)
    {
        DateTime now = DateTime.UtcNow;
        if ((now - _lastPointerStatusAt).TotalMilliseconds < 120)
            return;

        _lastPointerStatusAt = now;
        float screenDx = delta.X * _zoom;
        float screenDy = delta.Y * _zoom;
        PostStatus($"{label}: dx={screenDx:F0}px dy={screenDy:F0}px.");
    }

    private void BeginBoxSelection(SKPoint pdf, bool additive)
    {
        ClearInProgressInputForEdit();
        _boxSelecting = true;
        _boxSelectStartPdf = pdf;
        _boxSelectEndPdf = pdf;
        _boxSelectAdditive = additive;
        CaptureMouse();
        PostStatus(additive
            ? "Select: drag box to add measurements to the current selection."
            : "Select: drag box around measurements.");
        RequestRepaint();
    }

    private void FinishBoxSelection()
    {
        if (!_boxSelecting)
            return;

        SKRect rect = NormalizeRect(_boxSelectStartPdf, _boxSelectEndPdf);
        bool tiny = rect.Width * Math.Max(_zoom, 0.001f) < 4f &&
                    rect.Height * Math.Max(_zoom, 0.001f) < 4f;
        _boxSelecting = false;
        if (IsMouseCaptured)
            ReleaseMouseCapture();

        if (tiny)
        {
            if (!_boxSelectAdditive)
                ClearSelection();
            RequestRepaint();
            PostStatus("Select: no box drawn.");
            return;
        }

        var hits = _measurements
            .Where(m => IsMeasurementOnActivePage(m) && MeasurementIntersectsRect(m, rect))
            .ToList();

        if (_boxSelectAdditive)
        {
            var combined = GetSelectedMeasurements().ToList();
            foreach (Measurement hit in hits)
            {
                if (!combined.Contains(hit))
                    combined.Add(hit);
            }
            SetSelectedMeasurements(combined, hits.LastOrDefault() ?? combined.LastOrDefault(), -1);
        }
        else
        {
            SetSelectedMeasurements(hits, hits.LastOrDefault(), -1);
        }

        RequestRepaint();
        PostStatus(hits.Count == 0
            ? "Select: no measurements inside box."
            : $"Selected {GetSelectedMeasurements().Count} measurement(s). Ctrl+C copies, Ctrl+V pastes.");
    }

    private void CancelBoxSelection()
    {
        if (!_boxSelecting)
            return;

        _boxSelecting = false;
        RequestRepaint();
    }

    private void PostBoxSelectionStatus()
    {
        DateTime now = DateTime.UtcNow;
        if ((now - _lastPointerStatusAt).TotalMilliseconds < 120)
            return;

        _lastPointerStatusAt = now;
        SKRect rect = NormalizeRect(_boxSelectStartPdf, _boxSelectEndPdf);
        PostStatus($"Select box: {rect.Width * _zoom:F0}px x {rect.Height * _zoom:F0}px.");
    }

    private bool TryHitSelectedVertex(SKPoint pdf, out Measurement measurement, out int vertexIndex)
    {
        if (_selectedMeasurement != null &&
            IsMeasurementOnActivePage(_selectedMeasurement) &&
            TryHitVertexOnMeasurement(_selectedMeasurement, pdf, SelectedVertexHitToleranceScreenPx, out vertexIndex))
        {
            measurement = _selectedMeasurement;
            return true;
        }

        measurement = null!;
        vertexIndex = -1;
        return false;
    }

    private bool TryHitVertex(SKPoint pdf, out Measurement measurement, out int vertexIndex)
    {
        for (int i = _measurements.Count - 1; i >= 0; i--)
        {
            Measurement m = _measurements[i];
            if (!IsMeasurementOnActivePage(m)) continue;

            if (TryHitVertexOnMeasurement(m, pdf, VertexHitToleranceScreenPx, out vertexIndex))
            {
                measurement = m;
                return true;
            }
        }

        measurement = null!;
        vertexIndex = -1;
        return false;
    }

    private bool TryHitVertexOnMeasurement(Measurement measurement, SKPoint pdf, float screenTolerancePx, out int vertexIndex)
    {
        float tol = screenTolerancePx / Math.Max(_zoom, 0.01f);
        float tolSq = tol * tol;

        for (int p = measurement.Points.Count - 1; p >= 0; p--)
        {
            if (DistanceSquared(pdf, measurement.Points[p]) <= tolSq)
            {
                vertexIndex = p;
                return true;
            }
        }

        vertexIndex = -1;
        return false;
    }

    private bool TryHitSelectedMeasurement(SKPoint pdf, out Measurement measurement)
    {
        for (int i = _measurements.Count - 1; i >= 0; i--)
        {
            Measurement m = _measurements[i];
            if (!_selectedMeasurements.Contains(m) || !IsMeasurementOnActivePage(m))
                continue;

            if (IsMeasurementHit(m, pdf, SelectedMeasurementHitToleranceScreenPx))
            {
                measurement = m;
                return true;
            }
        }

        measurement = null!;
        return false;
    }

    private bool TryHitMeasurement(SKPoint pdf, out Measurement measurement)
    {
        for (int i = _measurements.Count - 1; i >= 0; i--)
        {
            Measurement m = _measurements[i];
            if (!IsMeasurementOnActivePage(m)) continue;

            if (IsMeasurementHit(m, pdf, MeasurementHitToleranceScreenPx))
            {
                measurement = m;
                return true;
            }
        }

        measurement = null!;
        return false;
    }

    private bool IsMeasurementHit(Measurement measurement, SKPoint pdf, float screenTolerancePx)
    {
        float tol = screenTolerancePx / Math.Max(_zoom, 0.01f);

        if (measurement.MType == "point")
            return measurement.Points.Any(p => DistanceSquared(pdf, p) <= tol * tol);

        for (int p = 1; p < measurement.Points.Count; p++)
        {
            if (DistanceToSegment(pdf, measurement.Points[p - 1], measurement.Points[p]) <= tol)
                return true;
        }

        return measurement.MType == "area" &&
               measurement.Points.Count > 2 &&
               DistanceToSegment(pdf, measurement.Points[^1], measurement.Points[0]) <= tol;
    }

    private void SelectMeasurement(Measurement measurement, int vertexIndex)
    {
        SetSelectedMeasurements([measurement], measurement, vertexIndex);
    }

    private void SetSelectedMeasurements(IReadOnlyList<Measurement> measurements, Measurement? primary, int vertexIndex)
    {
        var next = measurements
            .Where(m => _measurements.Contains(m) && IsMeasurementOnActivePage(m))
            .Distinct()
            .ToList();

        Measurement? nextPrimary = primary != null && next.Contains(primary)
            ? primary
            : next.LastOrDefault();

        bool setChanged = _selectedMeasurements.Count != next.Count ||
                          next.Any(m => !_selectedMeasurements.Contains(m));
        bool primaryChanged = !ReferenceEquals(_selectedMeasurement, nextPrimary);
        bool vertexChanged = _selectedVertexIndex != vertexIndex;

        _selectedMeasurements.Clear();
        foreach (Measurement measurement in next)
            _selectedMeasurements.Add(measurement);

        _selectedMeasurement = nextPrimary;
        _selectedVertexIndex = nextPrimary == null ? -1 : vertexIndex;

        if (primaryChanged)
            MeasurementSelectionChanged?.Invoke(nextPrimary);
        if (setChanged)
            MeasurementsSelectionChanged?.Invoke(next);
        if (primaryChanged || setChanged || vertexChanged)
            RequestRepaint();
    }

    private void ToggleMeasurementSelection(Measurement measurement)
    {
        if (!_measurements.Contains(measurement) || !IsMeasurementOnActivePage(measurement))
            return;

        var selected = GetSelectedMeasurements().ToList();
        if (selected.Contains(measurement))
            selected.Remove(measurement);
        else
            selected.Add(measurement);

        SetSelectedMeasurements(selected, selected.Contains(measurement) ? measurement : selected.LastOrDefault(), -1);
        PostStatus(selected.Count == 0
            ? "Selection cleared."
            : $"Selected {selected.Count} measurement(s). Ctrl+C copies, Ctrl+V pastes.");
    }

    private void SelectAllActivePageMeasurements()
    {
        var selected = _measurements
            .Where(IsMeasurementOnActivePage)
            .ToList();
        SetSelectedMeasurements(selected, selected.LastOrDefault(), -1);
        PostStatus(selected.Count == 0
            ? "No measurements on this sheet."
            : $"Selected all {selected.Count} measurement(s) on this sheet.");
    }

    private void CenterOnMeasurement(Measurement measurement)
    {
        if (measurement.Points.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0 || _zoom <= 0)
            return;

        SKRect bounds = MeasurementBounds(measurement);
        float centerX = (bounds.Left + bounds.Right) / 2f;
        float centerY = (bounds.Top + bounds.Bottom) / 2f;
        float visibleW = (float)ActualWidth / _zoom;
        float visibleH = (float)ActualHeight / _zoom;

        _panX = centerX - visibleW / 2f;
        _panY = centerY - visibleH / 2f;
        ClampPanToPage();
        ScheduleRerenderForZoom(force: false);
    }

    private void ClampPanToPage()
    {
        if (_pdfW <= 0 || _pdfH <= 0 || ActualWidth <= 0 || ActualHeight <= 0 || _zoom <= 0)
            return;

        float visibleW = (float)ActualWidth / _zoom;
        float visibleH = (float)ActualHeight / _zoom;
        _panX = Math.Clamp(_panX, 0, Math.Max(0, _pdfW - visibleW));
        _panY = Math.Clamp(_panY, 0, Math.Max(0, _pdfH - visibleH));
    }

    private void ClearSelection()
    {
        bool changed = _selectedMeasurement != null || _selectedMeasurements.Count > 0;
        _selectedMeasurement = null;
        _selectedMeasurements.Clear();
        _selectedVertexIndex = -1;
        _draggingVertex = false;
        _draggingMeasurement = false;
        _dragMeasurementChanged = false;
        _dragMeasurementOriginalPoints.Clear();
        _dragSelectionOriginalPoints.Clear();
        if (changed)
        {
            MeasurementSelectionChanged?.Invoke(null);
            MeasurementsSelectionChanged?.Invoke(Array.Empty<Measurement>());
        }
    }

    private void DeleteSelectedMeasurement()
    {
        var selected = GetSelectedMeasurements();
        if (selected.Count == 0)
            return;

        foreach (Measurement removed in selected)
            _measurements.Remove(removed);
        ClearSelection();
        RequestRepaint();
        PostStatus(selected.Count == 1
            ? $"Deleted {selected[0].MType}."
            : $"Deleted {selected.Count} selected measurements.");
        foreach (Measurement removed in selected)
            MeasurementRemoved?.Invoke(removed);
    }

    private bool IsMeasurementOnActivePage(Measurement measurement) =>
        IsSamePageFolder(measurement.PageFolder, _pageFolder);

    private bool IsAnnotationOnActivePage(PageAnnotation annotation) =>
        IsSamePageFolder(annotation.PageFolder, _pageFolder);

    private bool IsMeasurementSelected(Measurement measurement) =>
        _selectedMeasurements.Contains(measurement);

    private static bool IsSamePageFolder(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return string.Equals(NormalizePageFolderForCompare(left), NormalizePageFolderForCompare(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePageFolderForCompare(string path)
    {
        string trimmed = path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmed.Length == 0)
            return "";

        try
        {
            return Path.GetFullPath(trimmed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return trimmed;
        }
    }

    private static float DistanceSquared(SKPoint a, SKPoint b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static double DistanceSquared(Point a, Point b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static float DistanceToSegment(SKPoint p, SKPoint a, SKPoint b)
    {
        float vx = b.X - a.X;
        float vy = b.Y - a.Y;
        float lenSq = vx * vx + vy * vy;
        if (lenSq <= 0.0001f)
            return MathF.Sqrt(DistanceSquared(p, a));

        float t = ((p.X - a.X) * vx + (p.Y - a.Y) * vy) / lenSq;
        t = Math.Clamp(t, 0f, 1f);
        var projection = new SKPoint(a.X + t * vx, a.Y + t * vy);
        return MathF.Sqrt(DistanceSquared(p, projection));
    }

    private static SKRect NormalizeRect(SKPoint a, SKPoint b) =>
        new(
            Math.Min(a.X, b.X),
            Math.Min(a.Y, b.Y),
            Math.Max(a.X, b.X),
            Math.Max(a.Y, b.Y));

    private static bool RectContains(SKRect rect, SKPoint point) =>
        point.X >= rect.Left &&
        point.X <= rect.Right &&
        point.Y >= rect.Top &&
        point.Y <= rect.Bottom;

    private static bool RectsIntersect(SKRect a, SKRect b) =>
        a.Left <= b.Right &&
        a.Right >= b.Left &&
        a.Top <= b.Bottom &&
        a.Bottom >= b.Top;

    private static bool SegmentIntersectsRect(SKPoint a, SKPoint b, SKRect rect)
    {
        if (RectContains(rect, a) || RectContains(rect, b))
            return true;

        var topLeft = new SKPoint(rect.Left, rect.Top);
        var topRight = new SKPoint(rect.Right, rect.Top);
        var bottomRight = new SKPoint(rect.Right, rect.Bottom);
        var bottomLeft = new SKPoint(rect.Left, rect.Bottom);
        return SegmentsIntersect(a, b, topLeft, topRight) ||
               SegmentsIntersect(a, b, topRight, bottomRight) ||
               SegmentsIntersect(a, b, bottomRight, bottomLeft) ||
               SegmentsIntersect(a, b, bottomLeft, topLeft);
    }

    private static bool SegmentsIntersect(SKPoint a, SKPoint b, SKPoint c, SKPoint d)
    {
        float d1 = Cross(a, b, c);
        float d2 = Cross(a, b, d);
        float d3 = Cross(c, d, a);
        float d4 = Cross(c, d, b);

        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
            ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
        {
            return true;
        }

        const float eps = 0.0001f;
        return Math.Abs(d1) <= eps && PointOnSegment(c, a, b) ||
               Math.Abs(d2) <= eps && PointOnSegment(d, a, b) ||
               Math.Abs(d3) <= eps && PointOnSegment(a, c, d) ||
               Math.Abs(d4) <= eps && PointOnSegment(b, c, d);
    }

    private static float Cross(SKPoint a, SKPoint b, SKPoint c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static bool PointOnSegment(SKPoint p, SKPoint a, SKPoint b) =>
        p.X >= Math.Min(a.X, b.X) - 0.0001f &&
        p.X <= Math.Max(a.X, b.X) + 0.0001f &&
        p.Y >= Math.Min(a.Y, b.Y) - 0.0001f &&
        p.Y <= Math.Max(a.Y, b.Y) + 0.0001f;

    private static bool PointInPolygon(SKPoint point, IReadOnlyList<SKPoint> polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            SKPoint pi = polygon[i];
            SKPoint pj = polygon[j];
            float denom = Math.Abs(pj.Y - pi.Y) < 0.000001f ? 0.000001f : pj.Y - pi.Y;
            bool crosses = (pi.Y > point.Y) != (pj.Y > point.Y) &&
                           point.X < (pj.X - pi.X) * (point.Y - pi.Y) / denom + pi.X;
            if (crosses)
                inside = !inside;
        }

        return inside;
    }

    private void ApplyZoom(float factor, float screenX, float screenY)
    {
        float newZoom = Math.Clamp(_zoom * factor, ZoomMin, ZoomMax);
        if (Math.Abs(newZoom - _zoom) < 0.0001f) return;

        // Keep the PDF point under cursor fixed
        float pdfX = screenX / _zoom + _panX;
        float pdfY = screenY / _zoom + _panY;
        _zoom  = newZoom;
        _panX  = pdfX - screenX / _zoom;
        _panY  = pdfY - screenY / _zoom;
        ScheduleRerenderForZoom(force: false);
        RequestRepaint();
        PostStatus($"Zoom: {_zoom * 100:F0}%");
    }

    private float CurrentRenderScale()
    {
        if (_zoom <= 0)
            return 1.0f;

        float desired = Math.Clamp(_zoom, RenderScaleSteps[0], RenderScaleSteps[^1]);
        foreach (float step in RenderScaleSteps)
        {
            if (desired <= step)
                return step;
        }
        return RenderScaleSteps[^1];
    }

    private void RerenderForZoomIfNeeded(bool force)
    {
        if (string.IsNullOrWhiteSpace(_pdfPath) || _pdfW <= 0 || _pdfH <= 0)
            return;

        float desired = CurrentRenderScale();
        if (!force && _renderedScale > 0)
        {
            float ratio = desired / _renderedScale;
            if (ratio > 0.72f && ratio < 1.38f)
                return;
        }

        if (_usingLayerRenderer)
        {
            QueueLayerRender(resetLayerStates: false, renderScale: desired);
            return;
        }

        try
        {
            RenderPageWithDocnet(desired);
        }
        catch (Exception ex)
        {
            PostStatus($"Render error: {ex.Message}");
        }
    }

    private void ScheduleRerenderForZoom(bool force)
    {
        if (string.IsNullOrWhiteSpace(_pdfPath) || _pdfW <= 0 || _pdfH <= 0)
            return;

        if (!force && _renderedScale > 0)
        {
            float desired = CurrentRenderScale();
            float ratio = desired / _renderedScale;
            if (ratio > 0.72f && ratio < 1.38f)
                return;
        }

        _zoomRerenderForce = _zoomRerenderForce || force;
        _zoomRerenderTimer.Stop();
        _zoomRerenderTimer.Start();
    }

    private void PostPointerStatus(SKPoint p)
    {
        DateTime now = DateTime.UtcNow;
        if ((now - _lastPointerStatusAt).TotalMilliseconds < 50)
            return;

        _lastPointerStatusAt = now;
        string scaleStr = ScaleMetersPerPt > 0
            ? $"  |  1:{ScaleMetersPerPt / (25.4 / 72.0 / 1000.0):F0}"
            : "  |  scale: not set";
        PostStatus($"x={p.X:F1}  y={p.Y:F1} pt  |  zoom: {_zoom * 100:F0}%{scaleStr}");
    }

    private SKPoint ScreenToPdf(float sx, float sy)
        => new(sx / _zoom + _panX, sy / _zoom + _panY);

    private SKRect GetVisiblePdfRect(float screenPadding = 64f)
    {
        float safeZoom = Math.Max(_zoom, 0.001f);
        float pad = screenPadding / safeZoom;
        float visibleW = (float)ActualWidth / safeZoom;
        float visibleH = (float)ActualHeight / safeZoom;
        return new SKRect(
            _panX - pad,
            _panY - pad,
            _panX + visibleW + pad,
            _panY + visibleH + pad);
    }

    private static bool IsMeasurementVisible(Measurement measurement, SKRect visiblePdf)
    {
        if (measurement.Points.Count == 0)
            return false;

        SKRect bounds = MeasurementBounds(measurement);
        return bounds.Left <= visiblePdf.Right &&
               bounds.Right >= visiblePdf.Left &&
               bounds.Top <= visiblePdf.Bottom &&
               bounds.Bottom >= visiblePdf.Top;
    }

    private static bool MeasurementIntersectsRect(Measurement measurement, SKRect rect)
    {
        if (measurement.Points.Count == 0)
            return false;

        SKRect bounds = RawMeasurementBounds(measurement);
        if (!RectsIntersect(bounds, rect))
            return false;

        if (measurement.Points.Any(point => RectContains(rect, point)))
            return true;

        if (measurement.MType == "point")
            return false;

        for (int i = 1; i < measurement.Points.Count; i++)
        {
            if (SegmentIntersectsRect(measurement.Points[i - 1], measurement.Points[i], rect))
                return true;
        }

        if (measurement.MType == "area" && measurement.Points.Count > 2)
        {
            if (SegmentIntersectsRect(measurement.Points[^1], measurement.Points[0], rect))
                return true;

            var center = new SKPoint((rect.Left + rect.Right) / 2f, (rect.Top + rect.Bottom) / 2f);
            return PointInPolygon(center, measurement.Points);
        }

        return false;
    }

    private static SKRect RawMeasurementBounds(Measurement measurement) =>
        PointsBounds(measurement.Points);

    private static SKRect MeasurementBounds(Measurement measurement)
    {
        float left = float.PositiveInfinity;
        float top = float.PositiveInfinity;
        float right = float.NegativeInfinity;
        float bottom = float.NegativeInfinity;

        foreach (var point in measurement.Points)
        {
            left = Math.Min(left, point.X);
            top = Math.Min(top, point.Y);
            right = Math.Max(right, point.X);
            bottom = Math.Max(bottom, point.Y);
        }

        var bounds = new SKRect(left, top, right, bottom);
        bounds.Inflate(96f, 96f);
        return bounds;
    }

    private static SKPoint Centroid(List<SKPoint> pts)
        => new(pts.Average(p => p.X), pts.Average(p => p.Y));

    private SKColor GetCachedColor(string hex, SKColor fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return fallback;

        if (_colorCache.TryGetValue(hex, out SKColor color))
            return color;

        color = ParseColor(hex, fallback);
        _colorCache[hex] = color;
        return color;
    }

    private static SKColor ParseColor(string hex, SKColor fallback)
    {
        try
        {
            return SKColor.Parse(hex);
        }
        catch
        {
            return fallback;
        }
    }

    private void UpdateCursor()
    {
        Cursor = _tool switch
        {
            ViewerTool.Pan => Cursors.Hand,
            ViewerTool.Select => Cursors.Arrow,
            _              => Cursors.Cross,
        };
    }

    private void PostStatus(string msg) => StatusChanged?.Invoke(msg);

    private string LayerName(int layerNumber) =>
        _layers.FirstOrDefault(layer => layer.Number == layerNumber)?.Name ?? $"Layer {layerNumber}";

    private static string LayerTraceModeTitle(PdfLayerTraceMode mode) => mode switch
    {
        PdfLayerTraceMode.Edge => "Edge",
        PdfLayerTraceMode.Point => "Point",
        PdfLayerTraceMode.AllEdges => "All Edges",
        _ => "Full",
    };

    private void FireLayersChanged()
    {
        LayersChanged?.Invoke(_layers);
    }
}
