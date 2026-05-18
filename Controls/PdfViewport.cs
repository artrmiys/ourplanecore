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

// ── Simple layer info returned to the UI ─────────────────────────────────────

public sealed record PdfLayer(int Number, string Name, bool IsOn, bool IsHighlighted = false);
public sealed record SheetLegendEntry(
    string Color,
    string Name,
    string Quantity,
    string Type,
    string Sign,
    IReadOnlyList<string>? Details = null,
    MeasurementGlyphKind GlyphKind = MeasurementGlyphKind.Line);
public sealed record ViewportContextRequest(
    double ScreenX,
    double ScreenY,
    float PdfX,
    float PdfY,
    string PageFolder,
    Measurement? Measurement,
    PageAnnotation? Annotation = null,
    ViewportOverlayHitKind OverlayHit = ViewportOverlayHitKind.None);
public sealed record ViewportAiCropSelectionRequest(
    SKRect CropRect,
    SKPoint AnchorPdf,
    string PageFolder);
public sealed record SheetOverlayTransformChange(
    float OffsetXPt,
    float OffsetYPt,
    float OverlayScale,
    string Status);

// ── Tool enum ────────────────────────────────────────────────────────────────

public enum ViewerTool { Pan, Select, Scale, Ruler, DrawLine, DrawArrow, DrawRect, DrawCloud, DrawArea, Note, Point, Line, Area, AreaCut }
public enum PdfLayerTraceMode { Full, Edge, Point, AllEdges }
public enum ViewportOverlayHitKind { None, SheetLegend, SheetHeader }

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
    private PageAnnotation? _rightClickAnnotation;
    private ViewportOverlayHitKind _rightClickOverlayHit = ViewportOverlayHitKind.None;
    private bool _rightClickMoved;

    // ── Drawing tools ─────────────────────────────────────────────────────────
    private ViewerTool              _tool       = ViewerTool.Select;
    private readonly List<SKPoint>  _drawPts    = [];   // in-progress PDF-space points
    private SKPoint?                _rubberEnd;          // rubber-band endpoint
    private SKPoint?                _snapPreview;
    private string                  _snapPreviewKind = "";
    private SKPoint?                _lastPointerPdf;
    private bool                    _cursorGuideVisible;
    private bool                    _boxSelecting;
    private SKPoint                 _boxSelectStartPdf;
    private SKPoint                 _boxSelectEndPdf;
    private bool                    _boxSelectAdditive;
    private bool                    _boxSelectRemove;
    private bool                    _aiCropNoteSelecting;
    private bool                    _aiCropNoteDragging;
    private SKPoint                 _aiCropNoteStartPdf;
    private SKPoint                 _aiCropNoteEndPdf;
    private SKPoint                 _aiCropNoteAnchorPdf;
    private Measurement?            _areaCutMeasurement;

    // Scale calibration
    private readonly List<SKPoint> _scalePts = [];
    private SmartAiActionDraft? _aiActionDraftPreview;
    private string _aiActionDraftPreviewPage = "";
    private readonly List<SmartAiMarker> _aiMarkers = [];
    private readonly List<SheetLegendEntry> _sheetLegendEntries = [];
    public  double   ScaleMetersPerPt { get; set; } = 0.0;
    public  string   ActiveColor      { get; set; } = "#FF4444";
    public  string   ActiveAnnotationColor { get; set; } = "#FF4444";
    public  double   ActiveAnnotationStrokeWidth { get; set; } = 1.8;
    public  string   ActiveTakeoffFolder { get; set; } = "";
    public  string   ActiveCountSymbol { get; set; } = CountDisplaySymbol.Circle;
    public  UnitMode UnitMode         { get; set; } = UnitMode.Imperial;
    private string _viewBackgroundColor = ViewportBackgroundPolicy.DefaultColor;
    public  string   ViewBackgroundColor
    {
        get => _viewBackgroundColor;
        set
        {
            string clean = ViewportBackgroundPolicy.NormalizeColor(value);
            if (string.Equals(_viewBackgroundColor, clean, StringComparison.Ordinal))
                return;

            _viewBackgroundColor = clean;
            RequestRepaint();
        }
    }
    private string _pageBackgroundColor = ViewportBackgroundPolicy.DefaultColor;
    public  string   PageBackgroundColor
    {
        get => _pageBackgroundColor;
        set
        {
            string clean = ViewportBackgroundPolicy.NormalizeColor(value);
            if (string.Equals(_pageBackgroundColor, clean, StringComparison.Ordinal))
                return;

            _pageBackgroundColor = clean;
            RequestRepaint();
        }
    }
    public  bool     ShowMeasurementLabels { get; set; } = true;
    public  bool     ShowLineLabels { get; set; } = true;
    public  bool     ShowAreaLabels { get; set; } = true;
    public  bool     ShowJoistLabels { get; set; } = true;
    public  bool     ShowCountLabels { get; set; }
    public  double   MeasurementLabelScale { get; set; } = 1.0;
    public  double   MeasurementStrokeScale { get; set; } = 1.0;
    public  double   AreaEdgeScale { get; set; } = 1.0;
    public  double   AreaFillOpacity { get; set; } = 0.24;
    public  double   PointSizeScale { get; set; } = 1.0;
    public  string   SheetLegendAnchor { get; set; } = "BottomLeft";
    public  double   SheetLegendScale { get; set; } = 1.0;
    public  double   SheetHeaderScale { get; set; } = 1.0;
    public  bool     ScaleSheetOverlaysWithPage { get; set; } = false;
    public  bool     ScaleMeasurementLabelsWithPage { get; set; } = false;
    public  bool     ScaleSheetHeaderWithPage { get; set; } = false;
    public  bool     SimplifyNavigationRendering { get; set; } = false;

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
    private bool _boxModeEnabled;
    public bool BoxModeEnabled
    {
        get => _boxModeEnabled;
        set
        {
            if (_boxModeEnabled == value)
                return;

            _boxModeEnabled = value;
            BoxModeChanged?.Invoke(_boxModeEnabled);
            PostRecordPrompt();
            RequestRepaint();
        }
    }

    private readonly List<Measurement> _measurements = [];
    private readonly HashSet<Measurement> _measurementSet = [];
    private readonly Dictionary<string, List<Measurement>> _measurementsByPage = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Measurement> _visibleActivePageMeasurements = [];
    private string _visibleActivePageMeasurementsPageKey = "";
    private int _visibleActivePageMeasurementsIndexVersion = -1;
    private int _visibleActivePageMeasurementsHiddenVersion = -1;
    private int _visibleActivePageMeasurementsFolderHash;
    private int _measurementIndexVersion;
    private int _measurementGeometryVersion;
    private int _hiddenTakeoffFoldersVersion;
    private ViewportMeasurementSpatialIndex? _activePageMeasurementSpatialIndex;
    private string _activePageMeasurementSpatialIndexPageKey = "";
    private int _activePageMeasurementSpatialIndexVersion = -1;
    private int _activePageMeasurementSpatialIndexGeometryVersion = -1;
    private int _activePageMeasurementSpatialIndexHiddenVersion = -1;
    private int _activePageMeasurementSpatialIndexFolderHash;
    private readonly HashSet<string> _hiddenTakeoffFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PageAnnotation> _annotations = [];
    private bool _hideRulerAnnotations;
    private string _pageFolder = "";
    private Measurement? _selectedMeasurement;
    private readonly HashSet<Measurement> _selectedMeasurements = [];
    private PageAnnotation? _selectedAnnotation;
    private readonly HashSet<PageAnnotation> _selectedAnnotations = [];
    private int _selectedAnnotationVertexIndex = -1;
    private Measurement? _joistDirectionMeasurement;
    private readonly List<SKPoint> _joistDirectionPts = [];
    private SKPoint? _joistDirectionRubberEnd;
    private int _selectedVertexIndex = -1;
    private readonly Dictionary<Measurement, HashSet<int>> _selectedMeasurementVertexIndices = [];
    private bool _draggingVertex;
    private bool _draggingMeasurement;
    private bool _draggingAnnotationVertex;
    private bool _draggingAnnotation;
    private bool _draggingTransformScale;
    private bool _draggingTransformRotate;
    private bool _dragMeasurementChanged;
    private bool _dragAnnotationChanged;
    private Point _dragScreenStart;
    private SKPoint _dragVertexOriginalPoint;
    private SKPoint _dragAnnotationVertexOriginalPoint;
    private readonly Dictionary<Measurement, Dictionary<int, SKPoint>> _dragMeasurementVertexOriginalPoints = [];
    private List<SKPoint> _dragMeasurementOriginalPoints = [];
    private List<List<SKPoint>> _dragMeasurementOriginalHoles = [];
    private List<SKPoint> _dragAnnotationOriginalPoints = [];
    private readonly Dictionary<Measurement, List<SKPoint>> _dragSelectionOriginalPoints = [];
    private readonly Dictionary<Measurement, List<List<SKPoint>>> _dragSelectionOriginalHoles = [];
    private readonly Dictionary<Measurement, List<SKPoint>> _transformMeasurementOriginalPoints = [];
    private readonly Dictionary<Measurement, List<List<SKPoint>>> _transformMeasurementOriginalHoles = [];
    private readonly Dictionary<Measurement, double> _transformMeasurementOriginalJoistDirections = [];
    private readonly Dictionary<PageAnnotation, List<SKPoint>> _transformAnnotationOriginalPoints = [];
    private readonly List<ViewportUndoAction> _undoStack = [];
    private bool _applyingViewportUndo;
    private SKPoint _transformCenter;
    private float _transformStartDistance;
    private float _transformStartAngle;
    private TransformHandleKind _transformHandle = TransformHandleKind.None;
    private SKRect _transformStartBounds;
    private SKPoint _transformStartPdf;
    private readonly Dictionary<int, bool> _layerStates = [];
    private readonly HashSet<int> _highlightedLayers = [];
    private List<PdfLayer> _layers = [];
    private IReadOnlyList<PdfLayerInfo>? _cachedLayers;
    private bool _usingLayerRenderer;
    private bool _pdfLayerTraceEnabled;
    private PdfLayerTraceMode _pdfLayerTraceMode = PdfLayerTraceMode.Full;
    private int? _activePdfLayerTraceLayer;
    private string _activePdfLayerTraceLayerName = "";
    private bool _pdfLayerTraceLayerExplicitlySelected;
    private List<PdfLayerProbeCandidate> _pdfLayerTraceCandidates = [];
    private int _pdfLayerTraceCandidateIndex;
    private bool _pdfLayerTraceChoosingLayer;
    private bool _pdfLayerTraceReadyToApply;
    private SKPoint? _pdfLayerTracePickPoint;
    private int? _pdfLayerTracePreviewLayer;
    private bool _pdfLayerTraceProbeInProgress;
    private int _pdfLayerTraceProbeVersion;
    private SKPoint? _pendingPdfLayerTraceProbePoint;
    private SKPoint? _lastPdfLayerTraceProbePoint;
    private DateTime _lastPdfLayerTraceProbeAt = DateTime.MinValue;
    private readonly System.Windows.Threading.DispatcherTimer _zoomRerenderTimer;
    private readonly System.Windows.Threading.DispatcherTimer _navigationIdleTimer;
    private bool _zoomRerenderForce;
    private bool _repaintQueued;
    private bool _isFastNavigating;
    private bool _renderNavigationFastFrame;
    private bool _showingPreviousPageDuringSwitch;
    private DateTime _lastPointerStatusAt = DateTime.MinValue;
    private DateTime _lastSlowFrameLogAt = DateTime.MinValue;
    private DateTime _lastSlowRenderLogAt = DateTime.MinValue;
    private DateTime _lastSlowSnapLogAt = DateTime.MinValue;
    private readonly Dictionary<string, SKColor> _colorCache = new(StringComparer.OrdinalIgnoreCase);
    private LayerRenderRequest? _pendingLayerRender;
    private bool _layerRenderInProgress;
    private int _layerRenderVersion;
    private DocnetRenderRequest? _pendingDocnetRender;
    private bool _docnetRenderInProgress;
    private int _docnetRenderVersion;

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

    private sealed record DocnetRenderRequest(
        int Version,
        string PdfPath,
        int PdfIndex,
        string PageFolder,
        float RenderScale,
        ViewState? RestoreView,
        bool FitAfter,
        bool QueueLayerAfter,
        bool ResetLayerStates,
        string? StatusAfter,
        bool FireLayersAfter);

    private sealed record DocnetRenderResult(
        float WidthPt,
        float HeightPt,
        float BitmapScale,
        SKBitmap Bitmap);

    // ── Events ────────────────────────────────────────────────────────────────
    public event Action<string>?                          StatusChanged;
    public event Action<double>?                          ScaleChanged;
    public event Action<string>?                          ToolChanged;
    public event Action<bool>?                            SnapChanged;
    public event Action<bool>?                            OrthoChanged;
    public event Action<bool>?                            BoxModeChanged;
    public event Action<IReadOnlyList<PdfLayer>>?         LayersChanged;
    public event Action<IReadOnlyList<PdfLayerInfo>>?     PdfLayersDiscovered;
    public event Action?                                  PdfLayerTraceStateChanged;
    public event Action<Measurement>?                     MeasurementAdded;
    public event Action<Measurement>?                     MeasurementRemoved;
    public event Action<IReadOnlyList<Measurement>>?      MeasurementsAdded;
    public event Action<IReadOnlyList<Measurement>>?      MeasurementsRemoved;
    public event Action<Measurement>?                     MeasurementChanged;
    public event Action<IReadOnlyList<Measurement>>?      MeasurementsChanged;
    public event Action<PageAnnotation>?                   PageAnnotationAdded;
    public event Action<PageAnnotation>?                   PageAnnotationRemoved;
    public event Action<PageAnnotation>?                   PageAnnotationChanged;
    public event Func<string, string, string, string?>?    PageAnnotationTextRequested;
    public event Action<bool>?                             TransformSelectionChanged;
    public event Action<Measurement?>?                    MeasurementSelectionChanged;
    public event Action<IReadOnlyList<Measurement>>?      MeasurementsSelectionChanged;
    public event Action<IReadOnlyList<Measurement>>?      CopyMeasurementsRequested;
    public event Action<SKPoint?>?                        PasteMeasurementsRequested;
    public event Action<ViewportContextRequest>?          ContextRequested;
    public event Action<ViewportAiCropSelectionRequest>?  AiCropNoteSelectionCompleted;
    public event Action<Measurement, SKPoint, SKPoint>?   JoistDirectionCaptured;
    public event Action<SheetOverlayTransformChange>?     SheetOverlayTransformChanged;

    // ── Constants ─────────────────────────────────────────────────────────────
    private const float ZoomMin    = 0.05f;
    private const float ZoomMax    = 16.0f;
    private const float RenderDpi  = 144f;           // initial render quality (2 px/pt)
    private const double PdfPointMeters = ViewportConstants.PdfPointMeters;
    private const float SnapToleranceScreenPx = ViewportConstants.SnapToleranceScreen;
    private const float SnapMarkerScreenPx = 8f;
    private const float VertexHitToleranceScreenPx = ViewportConstants.VertexHitRadiusScreen;
    private const float MeasurementHitToleranceScreenPx = ViewportConstants.MeasurementHitRadiusScreen;
    private const float SelectedVertexHitToleranceScreenPx = 32f;
    private const float SelectedMeasurementHitToleranceScreenPx = 28f;
    private const float MeasurementLabelFontScreenPx = 9f;
    private const float MeasurementLabelPaddingScreenPx = 2f;
    private const float JoistSegmentLabelFontScreenPx = 7f;
    private static readonly float[] RenderScaleSteps = [0.75f, 1.00f, 1.50f, 2.25f, 3.00f, 4.00f];
    private static readonly SKTypeface LabelTypeface =
        SKTypeface.FromFamilyName("Consolas") ??
        SKTypeface.FromFamilyName("Cascadia Mono") ??
        SKTypeface.Default;
    private static readonly SKTypeface OverlayMonoTypeface =
        SKTypeface.FromFamilyName("Consolas") ??
        SKTypeface.FromFamilyName("Cascadia Mono") ??
        SKTypeface.Default;
    private static readonly SKTypeface OverlayUiTypeface =
        SKTypeface.FromFamilyName("Segoe UI") ??
        SKTypeface.FromFamilyName("Inter") ??
        SKTypeface.Default;
    private static readonly SKTypeface OverlayUiBoldTypeface =
        SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ??
        SKTypeface.FromFamilyName("Inter", SKFontStyle.Bold) ??
        OverlayUiTypeface;

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
            Interval = TimeSpan.FromMilliseconds(ViewportConstants.ZoomRerenderDelayMs),
        };
        _zoomRerenderTimer.Tick += (_, _) =>
        {
            _zoomRerenderTimer.Stop();
            bool force = _zoomRerenderForce;
            _zoomRerenderForce = false;
            RerenderForZoomIfNeeded(force);
            RequestRepaint();
        };
        _navigationIdleTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(ViewportConstants.NavigationIdleMs),
        };
        _navigationIdleTimer.Tick += (_, _) =>
        {
            _navigationIdleTimer.Stop();
            _isFastNavigating = false;
            RequestRepaint();
        };
        Unloaded += PdfViewport_Unloaded;
    }

    private void PdfViewport_Unloaded(object sender, RoutedEventArgs e)
    {
        _zoomRerenderTimer.Stop();
        _navigationIdleTimer.Stop();
        _pendingLayerRender = null;
        _layerRenderVersion++;
        _pendingDocnetRender = null;
        _docnetRenderVersion++;
        _showingPreviousPageDuringSwitch = false;
        _pageBitmap?.Dispose();
        _pageBitmap = null;
        ClearSheetOverlay();
        _selectedMeasurementVertexIndices.Clear();
        _dragMeasurementOriginalPoints.Clear();
        _dragMeasurementOriginalHoles.Clear();
        _dragMeasurementVertexOriginalPoints.Clear();
        _dragSelectionOriginalPoints.Clear();
        _dragSelectionOriginalHoles.Clear();
        _transformMeasurementOriginalPoints.Clear();
        _transformMeasurementOriginalHoles.Clear();
        _transformMeasurementOriginalJoistDirections.Clear();
        _transformAnnotationOriginalPoints.Clear();
        ClearViewportUndoStack();
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

        float previewScale = ViewportRenderPolicy.InitialPagePreviewRenderScale;
        string loadedStatus = $"Loaded: {Path.GetFileName(pdfPath)}  page {pageIndex + 1}";
        string previewCacheKey = DocnetRenderCacheKey(_pdfPath, _pdfIndex, previewScale);
        bool queueLayerAfterPreview = ShouldQueueInitialLayerRender();
        bool hasCachedPreview = DocnetRenderCache.TryGet(previewCacheKey, out CachedBitmapRender cachedPreview);
        if (!hasCachedPreview)
        {
            string instantCacheKey = DocnetRenderCacheKey(
                _pdfPath,
                _pdfIndex,
                ViewportRenderPolicy.InstantPagePreviewRenderScale);
            hasCachedPreview = DocnetRenderCache.TryGet(instantCacheKey, out cachedPreview);
        }

        if (hasCachedPreview)
        {
            ApplyCachedBitmapRender(cachedPreview);
            Dispatcher.InvokeAsync(() =>
            {
                if (restoreView.HasValue)
                    RestoreViewState(restoreView.Value);
                else
                    ZoomFit();
                if (queueLayerAfterPreview)
                {
                    QueueInitialLayerDiscoveryOrRender(
                        resetLayerStates: true,
                        renderScale: CurrentRenderScale(),
                        statusAfter: loadedStatus,
                        fireLayersAfter: true);
                }
                else
                {
                    _showingPreviousPageDuringSwitch = false;
                    PostStatus(loadedStatus);
                    RequestRepaint();
                }
            });
        }
        else
        {
            QueueDocnetRender(
                ViewportRenderPolicy.InstantPagePreviewRenderScale,
                restoreView,
                fitAfter: !restoreView.HasValue,
                queueLayerAfter: queueLayerAfterPreview,
                resetLayerStates: true,
                statusAfter: loadedStatus,
                fireLayersAfter: true);
        }

        PostStatus($"Rendering: {Path.GetFileName(pdfPath)}  page {pageIndex + 1}");
        RequestRepaint();

        // Fire layers event
        FireLayersChanged();
    }

    private bool ShouldQueueInitialLayerRender()
    {
        if (_cachedLayers == null)
            return true;

        return _cachedLayers.Count > 0;
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
            "drawcloud" => ViewerTool.DrawCloud,
            "drawarea" => ViewerTool.DrawArea,
            "note" => ViewerTool.Note,
            "point" => ViewerTool.Point,
            "line"  => ViewerTool.Line,
            "area"  => ViewerTool.Area,
            "joistarea" => ViewerTool.Area,
            "areacut" => ViewerTool.AreaCut,
            _       => ViewerTool.Pan,
        };
        CancelDrawing(clearSelection: _tool != ViewerTool.AreaCut);
        SetSnapPreview(null);
        UpdateCursor();
        PostRecordPrompt();
    }

    public SKRect GetVisiblePdfRect()
    {
        if (_pdfW <= 0 || _pdfH <= 0 || _zoom <= 0 || ActualWidth <= 0 || ActualHeight <= 0)
            return SKRect.Empty;

        var rect = new SKRect(
            _panX,
            _panY,
            _panX + (float)ActualWidth / _zoom,
            _panY + (float)ActualHeight / _zoom);
        float left = Math.Clamp(rect.Left, 0, Math.Max(0, _pdfW - 1));
        float top = Math.Clamp(rect.Top, 0, Math.Max(0, _pdfH - 1));
        float right = Math.Clamp(rect.Right, left + 1, _pdfW);
        float bottom = Math.Clamp(rect.Bottom, top + 1, _pdfH);
        return new SKRect(left, top, right, bottom);
    }

    public IReadOnlyList<Measurement> GetSelectedMeasurements() =>
        _selectedMeasurements
            .Where(m => _measurementSet.Contains(m) && IsMeasurementOnActivePage(m))
            .ToList();

    public bool BeginJoistDirectionCapture(Measurement areaMeasurement)
    {
        if (OurPlaneCoreJobStore.NormalizeMeasurementType(areaMeasurement.MType) != "area")
        {
            PostStatus("Joist direction can only be set on Area measurements.");
            return false;
        }

        if (!_measurementSet.Contains(areaMeasurement))
        {
            PostStatus("Joist direction was not started: Area is not loaded in the viewport.");
            return false;
        }

        if (!IsSamePageFolder(areaMeasurement.PageFolder, _pageFolder))
        {
            PostStatus("Joist direction was not started: open the Area sheet and try again.");
            return false;
        }

        if (!IsMeasurementTakeoffVisible(areaMeasurement))
        {
            PostStatus("Joist direction was not started: this takeoff is hidden on the active sheet.");
            return false;
        }

        _joistDirectionMeasurement = areaMeasurement;
        _joistDirectionPts.Clear();
        _joistDirectionRubberEnd = null;
        CancelDrawing();
        SelectMeasurements([areaMeasurement]);
        PostStatus("Joist direction: click two points parallel to the joists. Esc cancels.");
        RequestRepaint();
        return true;
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


    // ── Undo ─────────────────────────────────────────────────────────────────

    public void UndoLast()
    {
        if (_drawPts.Count > 0)
        {
            _drawPts.RemoveAt(_drawPts.Count - 1);
            _rubberEnd = _drawPts.Count > 0 ? _rubberEnd : null;
            if (_drawPts.Count == 0)
                _areaCutMeasurement = null;
            RequestRepaint();
            if (_drawPts.Count > 0)
                PostRecordPrompt();
            else
                PostStatus("Undo: drawing cleared.");
            return;
        }

        if (TryUndoLastViewportAction())
            return;

        PostStatus("Nothing to undo on this page.");
    }

    // Remove specific measurements without firing MeasurementRemoved events
    // (caller handles model cleanup; this just keeps the render list consistent)
    public void DeleteMeasurements(IEnumerable<Measurement> toRemove)
    {
        foreach (var m in toRemove.ToList())
        {
            _measurements.Remove(m);
            _measurementSet.Remove(m);
            RemoveMeasurementFromPageIndex(m);
            ForgetMeasurementState(m);
        }
        if (_selectedMeasurement == null && _selectedMeasurements.Count > 0)
            _selectedMeasurement = _selectedMeasurements.LastOrDefault();
        if (_selectedMeasurement == null)
            _selectedVertexIndex = -1;
        RequestRepaint();
    }

    public bool DeletePageAnnotation(PageAnnotation annotation)
    {
        return DeletePageAnnotations([annotation]);
    }

    public bool DeletePageAnnotations(IEnumerable<PageAnnotation> annotations)
    {
        var removed = annotations
            .Where(annotation => _annotations.Contains(annotation))
            .Distinct()
            .ToList();
        if (removed.Count == 0)
            return false;

        PushRemovedAnnotationsUndo(removed, $"restore {removed.Count} deleted markup(s)");
        foreach (PageAnnotation annotation in removed)
            _annotations.Remove(annotation);

        if (removed.Any(annotation =>
                ReferenceEquals(_selectedAnnotation, annotation) ||
                _selectedAnnotations.Contains(annotation)))
        {
            ClearAnnotationSelection();
        }

        RequestRepaint();
        PublishTransformSelectionChanged();
        PostStatus(removed.Count == 1
            ? $"Deleted {ToolTitle(removed[0].Kind)} markup."
            : $"Deleted {removed.Count} selected markups.");
        foreach (PageAnnotation annotation in removed)
            PageAnnotationRemoved?.Invoke(annotation);
        return true;
    }

    public bool UpdatePageAnnotationText(PageAnnotation annotation, string text)
    {
        if (!_annotations.Contains(annotation) || !IsAnnotationOnActivePage(annotation))
            return false;

        string clean = (text ?? "").Trim();
        if (string.Equals(annotation.Text, clean, StringComparison.Ordinal))
            return false;

        string beforeText = annotation.Text;
        PushAnnotationTextUndoSnapshot(annotation, beforeText, "edit markup text");
        annotation.Text = clean;
        RequestRepaint();
        PostStatus($"Updated {ToolTitle(annotation.Kind)} markup text.");
        PageAnnotationChanged?.Invoke(annotation);
        return true;
    }

    // Bulk-load measurements restored from a saved file
    public void LoadMeasurements(IEnumerable<Measurement> measurements)
    {
        SetMeasurements(measurements);
    }

    public void SetHiddenTakeoffFolders(IEnumerable<string> takeoffFolders)
    {
        _hiddenTakeoffFolders.Clear();
        foreach (string folder in takeoffFolders)
        {
            if (!string.IsNullOrWhiteSpace(folder))
                _hiddenTakeoffFolders.Add(NormalizePageFolderForCompare(folder));
        }

        InvalidateHiddenTakeoffFolderCache();
        PruneHiddenMeasurementSelection();
        RequestRepaint();
    }

    public bool HideRulerAnnotations
    {
        get => _hideRulerAnnotations;
        set
        {
            if (_hideRulerAnnotations == value)
                return;

            _hideRulerAnnotations = value;
            PruneHiddenAnnotationSelection();

            RequestRepaint();
        }
    }

    public bool HideVisibleRulerAnnotationsOnActivePage()
    {
        bool changed = false;
        foreach (PageAnnotation annotation in _annotations)
        {
            if (!IsAnnotationOnActivePage(annotation) ||
                !IsRulerAnnotation(annotation) ||
                annotation.Hidden)
            {
                continue;
            }

            annotation.Hidden = true;
            changed = true;
        }

        HideRulerAnnotations = true;
        if (changed)
            RequestRepaint();
        return changed;
    }

    public bool ShowAllRulerAnnotationsOnActivePage()
    {
        bool changed = false;
        foreach (PageAnnotation annotation in _annotations)
        {
            if (!IsAnnotationOnActivePage(annotation) ||
                !IsRulerAnnotation(annotation) ||
                !annotation.Hidden)
            {
                continue;
            }

            annotation.Hidden = false;
            changed = true;
        }

        HideRulerAnnotations = false;
        if (changed)
            RequestRepaint();
        return changed;
    }

    public void FocusMeasurement(Measurement measurement)
    {
        if (!_measurementSet.Contains(measurement))
            return;

        SelectMeasurement(measurement, -1);
        CenterOnMeasurement(measurement);
        RequestRepaint();
        PostStatus($"Selected {EntryTitle(measurement.MType)}. Drag the body to move; drag blue handles to reshape.");
    }

    public void SelectMeasurements(IEnumerable<Measurement> measurements)
    {
        var selected = measurements
            .Where(m => _measurementSet.Contains(m) && IsMeasurementOnActivePage(m))
            .Distinct()
            .ToList();
        SetSelectedMeasurements(selected, selected.LastOrDefault(), -1);
        if (selected.Count > 0)
            PostStatus($"Selected {selected.Count} measurement(s). Ctrl+C copies, Ctrl+V pastes to the active sheet.");
    }

    public void RefreshMeasurementDisplay() =>
        RequestRepaint();

    public void FocusPdfPoint(float pdfX, float pdfY)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0 || _zoom <= 0)
            return;

        float visibleW = ScreenToPdfDistance((float)ActualWidth);
        float visibleH = ScreenToPdfDistance((float)ActualHeight);
        _panX = pdfX - visibleW / 2f;
        _panY = pdfY - visibleH / 2f;
        ClampPanToPage();
        ScheduleRerenderForZoom(force: false);
        RequestRepaint();
        Focus();
    }

    public bool InsertMeasurementVertex(Measurement measurement, float pdfX, float pdfY)
    {
        if (!_measurementSet.Contains(measurement) ||
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

        List<SKPoint> beforePoints = measurement.Points.ToList();
        measurement.Points.Insert(insertIndex, point);
        PushMeasurementUndoSnapshot(measurement, beforePoints, $"insert {ToolTitle(measurement.MType)} vertex");
        SelectMeasurement(measurement, insertIndex);
        NotifyMeasurementsChanged([measurement]);
        RequestRepaint();
        PostStatus($"Inserted {ToolTitle(measurement.MType)} vertex {insertIndex + 1}.");
        return true;
    }

    public bool RemoveNearestMeasurementVertex(Measurement measurement, float pdfX, float pdfY)
    {
        if (!_measurementSet.Contains(measurement) ||
            !IsMeasurementOnActivePage(measurement) ||
            measurement.MType is not ("line" or "area") ||
            measurement.Points.Count == 0)
        {
            return false;
        }

        int minimumPoints = measurement.MType == "area" ? 3 : 2;
        if (measurement.Points.Count <= minimumPoints)
        {
            PostStatus($"{ToolTitle(measurement.MType)} needs at least {minimumPoints} vertices.");
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

        PushMeasurementUndoSnapshot(measurement, measurement.Points.ToList(), $"remove {ToolTitle(measurement.MType)} vertex");
        measurement.Points.RemoveAt(removeIndex);
        SelectMeasurement(measurement, Math.Min(removeIndex, measurement.Points.Count - 1));
        NotifyMeasurementsChanged([measurement]);
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

    public bool TrySaveCropRect(
        SKRect requestedPdfRect,
        string outputPath,
        out SKRect cropPdfRect,
        out string error) =>
        TrySavePdfCrop(requestedPdfRect, outputPath, out cropPdfRect, out error);

    public bool TrySaveMeasurementCrop(
        Measurement measurement,
        float paddingPt,
        string outputPath,
        out SKRect cropPdfRect,
        out string error)
    {
        cropPdfRect = SKRect.Empty;
        if (!_measurementSet.Contains(measurement) || measurement.Points.Count == 0)
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

    public void SetMeasurements(IEnumerable<Measurement> measurements, bool clearUndoStack = true)
    {
        _measurements.Clear();
        _measurementSet.Clear();
        _measurementsByPage.Clear();
        InvalidateActivePageMeasurementCache();
        _measurements.AddRange(measurements);
        foreach (Measurement measurement in _measurements)
        {
            _measurementSet.Add(measurement);
            IndexMeasurementByPage(measurement);
        }
        if (clearUndoStack)
            ClearViewportUndoStack();
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
        ClearViewportUndoStack();
        ClearAnnotationSelection();
        PublishTransformSelectionChanged();
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
        ClearSheetOverlay();
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

    private void PostStatus(string msg) => StatusChanged?.Invoke(msg);

}
