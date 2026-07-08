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

// в”Ђв”Ђ Simple layer info returned to the UI в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

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
    float OverlayRotationDegrees,
    string Status);
public sealed record BeamMeasurementRequest(
    SKPoint StartPdf,
    SKPoint EndPdf,
    SKPoint CountPointPdf,
    double LengthFeet,
    double OrderLengthFeet,
    string OrderLengthText,
    string PageFolder);
public sealed record OpeningMeasurementRequest(
    SKPoint FirstCornerPdf,
    SKPoint OppositeCornerPdf,
    SKPoint CountPointPdf,
    double WidthFeet,
    double HeightFeet,
    string SizeText,
    string PageFolder);

// в”Ђв”Ђ Tool enum в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

public enum ViewerTool { Pan, Select, Scale, Ruler, Beam, Openings, DrawHighlight, DrawLine, DrawArrow, DrawRect, DrawCloud, DrawArea, Note, Point, Line, Area, AreaCut }
public enum PdfLayerTraceMode { Full, Edge, Point, AllEdges }
public enum ViewportOverlayHitKind { None, SheetLegend, SheetHeader }

// в”Ђв”Ђ Main control в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

public sealed partial class PdfViewport : SKElement
{
    // в”Ђв”Ђ PDF / render state в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
    private static readonly IDocLib _docLib = DocLib.Instance;

    private string  _pdfPath  = "";
    private int     _pdfIndex = 0;
    private RasterSheetSource? _rasterSheetSource;

    private SKBitmap? _pageBitmap;
    private string _pageBitmapPdfPath = "";
    private int _pageBitmapPdfIndex = -1;
    private string _pageBitmapPageFolder = "";
    private long _pageBitmapGeneration;
    private long _pagePaintGeneration;
    private string _pagePaintedPageFolder = "";
    private float     _pdfW, _pdfH;        // page size in PDF points (1pt = 1/72 in)
    private float     _bitmapScale;         // bitmap pixels per PDF point
    private float     _renderedScale;

    // в”Ђв”Ђ Viewport transform в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
    private float _zoom = 1f;              // screen pixels per PDF point
    private float _panX, _panY;            // PDF-point coord of top-left of screen

    // в”Ђв”Ђ Pan drag в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
    private Point? _dragStart;
    private float  _dragPanX0, _dragPanY0;
    private Point? _rightClickStart;
    private SKPoint? _rightClickPdf;
    private Measurement? _rightClickMeasurement;
    private PageAnnotation? _rightClickAnnotation;
    private ViewportOverlayHitKind _rightClickOverlayHit = ViewportOverlayHitKind.None;
    private bool _rightClickMoved;

    // в”Ђв”Ђ Drawing tools в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
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
    private bool                    _boxVertexMode;
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
    public  double   ActiveAnnotationStrokeWidth { get; set; } = 5.0;
    public  double   RulerStrokeWidth { get; set; } = 1.0;
    private double _pdfSnapBridgeToleranceScreenPx = AppSettingsStore.ViewportPdfSnapBridgeToleranceDefaultPx;
    public double PdfSnapBridgeToleranceScreenPx
    {
        get => _pdfSnapBridgeToleranceScreenPx;
        set
        {
            double clean = AppSettingsStore.NormalizePdfSnapBridgeTolerancePx(value);
            if (Math.Abs(_pdfSnapBridgeToleranceScreenPx - clean) < 0.001)
                return;

            _pdfSnapBridgeToleranceScreenPx = clean;
            ClearEdgeSnapPreview();
            RequestRepaint();
        }
    }
    public  string   ActiveTakeoffFolder { get; set; } = "";
    public  string   ActiveCountSymbol { get; set; } = CountDisplaySymbol.Circle;
    public  UnitMode UnitMode         { get; set; } = UnitMode.Imperial;
    public float Zoom => _zoom;
    public SKPoint? LastPointerPdf => _lastPointerPdf;
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
    // Mouse-wheel zoom step (multiplier per notch). Set from settings.
    public  double   ZoomWheelFactor { get; set; } = 2.0;
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

    // в”Ђв”Ђ Measurements в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
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
    private int _measurementVisibilityVersion;
    private ViewportMeasurementSpatialIndex? _activePageMeasurementSpatialIndex;
    private string _activePageMeasurementSpatialIndexPageKey = "";
    private int _activePageMeasurementSpatialIndexVersion = -1;
    private int _activePageMeasurementSpatialIndexGeometryVersion = -1;
    private int _activePageMeasurementSpatialIndexHiddenVersion = -1;
    private int _activePageMeasurementSpatialIndexFolderHash;
    private readonly HashSet<string> _hiddenTakeoffFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hiddenMeasurementIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _takeoffLayerRanks = new(StringComparer.OrdinalIgnoreCase);
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
    private readonly Dictionary<PageAnnotation, List<SKPoint>> _dragAnnotationSelectionOriginalPoints = [];
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
    private IReadOnlyList<PdfGeometrySnapSegment> _rasterSheetVisualSegments = [];
    private readonly object _rasterSheetRebuildGate = new();
    private readonly HashSet<string> _rasterSheetRebuildsInFlight = new(StringComparer.OrdinalIgnoreCase);
    private bool _pdfLayersLoadedForPage;
    private bool _usingLayerRenderer;
    private bool _usingRasterSheetRender;
    private bool _usingRasterSheetOverviewRender;
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
    private DateTime _lastFastNavigationAt = DateTime.MinValue;
    private string _lastRasterSheetMotionWarmupKey = "";
    private DateTime _lastRasterSheetMotionWarmupAt = DateTime.MinValue;
    private int _rasterSheetQualityRestoreVersion;
    private int _rasterSheetQualityRestoreQueuedVersion;
    private DateTime _lastPointerStatusAt = DateTime.MinValue;
    private DateTime _lastPointerRepaintAt = DateTime.MinValue;
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
    private int _persistedPreviewRenderVersion;
    private int _persistedPreviewRenderInFlightVersion;

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
        ViewState? RestoreView,
        bool FitAfter,
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
        IReadOnlyList<PdfLayerInfo>? CachedLayers,
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

    // в”Ђв”Ђ Events в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
    public event Action<string>?                          StatusChanged;
    public event Action<double>?                          ScaleChanged;
    public event Action<float>?                           ZoomChanged;
    public event Action<SKPoint?>?                        PointerPdfChanged;
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
    public event Action<Measurement?>?                    TakeoffRenameRequested;
    public event Action<IReadOnlyList<Measurement>>?      CopyMeasurementsRequested;
    public event Action<SKPoint?>?                        PasteMeasurementsRequested;
    public event Action<ViewportContextRequest>?          ContextRequested;
    public event Action<ViewportAiCropSelectionRequest>?  AiCropNoteSelectionCompleted;
    public event Action<Measurement, SKPoint, SKPoint>?   JoistDirectionCaptured;
    public event Action<BeamMeasurementRequest>?          BeamMeasurementCompleted;
    public event Action<OpeningMeasurementRequest>?       OpeningMeasurementCompleted;
    public event Action<SheetOverlayTransformChange>?     SheetOverlayTransformChanged;
    public event Action<float>?                           SheetOverlayRenderScaleRefreshRequested;

    // в”Ђв”Ђ Constants в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
    private const float ZoomMin    = 0.05f;
    private const float ZoomMax    = 16.0f;
    private const float RenderDpi  = 144f;           // initial render quality (2 px/pt)
    private const double PdfPointMeters = ViewportConstants.PdfPointMeters;
    private const float SnapToleranceScreenPx = ViewportConstants.SnapToleranceScreen;
    private const float SnapMarkerScreenPx = 8f;
    private const float VertexHitToleranceScreenPx = ViewportConstants.VertexHitRadiusScreen;
    private const float MeasurementHitToleranceScreenPx = ViewportConstants.MeasurementHitRadiusScreen;
    private const float SelectedVertexHitToleranceScreenPx = 12f;
    private const float SelectedMeasurementHitToleranceScreenPx = 10f;
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

    // в”Ђв”Ђ Constructor в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
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
            if (ShouldDeferFastNavigationEndForPointer())
            {
                _navigationIdleTimer.Stop();
                _navigationIdleTimer.Start();
                return;
            }

            EndFastNavigation();
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
        ClearDetailRender();
        _showingPreviousPageDuringSwitch = false;
        _usingRasterSheetRender = false;
        _usingRasterSheetOverviewRender = false;
        _pageBitmap?.Dispose();
        _pageBitmap = null;
        ClearSheetOverlay();
        _selectedMeasurementVertexIndices.Clear();
        _dragMeasurementOriginalPoints.Clear();
        _dragMeasurementOriginalHoles.Clear();
        _dragMeasurementVertexOriginalPoints.Clear();
        _dragSelectionOriginalPoints.Clear();
        _dragSelectionOriginalHoles.Clear();
        _dragAnnotationSelectionOriginalPoints.Clear();
        ClearRasterSheetVisualSegments();
        _transformMeasurementOriginalPoints.Clear();
        _transformMeasurementOriginalHoles.Clear();
        _transformMeasurementOriginalJoistDirections.Clear();
        _transformAnnotationOriginalPoints.Clear();
        ClearViewportUndoStack();
    }

    private void RequestRepaint(bool crossThreadRequest = false)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() => RequestRepaint(crossThreadRequest: true)));
            return;
        }

        ViewportPerformanceRecorder.RecordRepaintRequest(_pageFolder, _repaintQueued, crossThreadRequest);
        if (_repaintQueued)
            return;

        PrepareBitmapForImmediateRepaint();
        TryStartPendingSimilarCountSelection();
        if (_repaintQueued)
            return;

        _repaintQueued = true;
        InvalidateVisual();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
        {
            _repaintQueued = false;
        }));
    }

    private void PrepareBitmapForImmediateRepaint()
    {
        try
        {
            TryPrepareRasterSheetBitmapForImmediateRepaint();
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, "Viewport immediate repaint bitmap preparation failed.");
        }
    }

    // в•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђ
    // Public API
    // в•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђ

    private void PostStatus(string msg) => StatusChanged?.Invoke(msg);

}
