using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

public partial class MainWindow : Window
{
    private readonly PdfViewport _viewport;
    private readonly AppSettings _settings = AppSettingsStore.Load();
    private bool _isApplyingSettings;
    private bool _isRunningAiRequest;
    private bool _updatingTransformSliders;
    private double _lastTransformRotateSliderValue;
    private double _lastTransformScaleSliderValue = 1.0;

    private OurPlaneCoreJob? _currentJob;
    private PageInfo? _currentPage;
    private string _currentPdfPath = "";

    private readonly List<TakeoffItem> _takeoffItems = [];
    private TakeoffItem? _activeItem;
    private string _activeTakeoffParentFolder = "";
    private string _activeTool = "select";
    private bool _updatingLayerTraceUi;
    private PagesClipboard? _pagesClipboard;
    private MeasurementClipboard? _measurementClipboard;
    private readonly HashSet<string> _pagesMultiSelection = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pageTakeoffMultiSelection = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _takeoffsMultiSelection = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _takeoffSectionMultiSelection = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedPageTreePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedTakeoffTreePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hiddenAiMarkerTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<GeometryModel3D, Massing3DObjectInfo> _massing3DObjectInfo = [];
    private readonly List<PageTabState> _pageTabs = [];
    private int _lastMeasurementPageFolderRepairCount;
    private int _lastMeasurementPageFolderUnresolvedCount;
    private Point? _pagesDragStart;
    private string? _pagesRangeAnchorPath;
    private string? _pageTakeoffRangeAnchorKey;
    private string? _takeoffsRangeAnchorPath;
    private string? _takeoffSectionRangeAnchorKey;
    private TreeViewItem? _takeoffsDragItem;
    private readonly HashSet<TakeoffItem> _pendingTakeoffAutosaves = [];
    private readonly System.Windows.Threading.DispatcherTimer _takeoffAutosaveTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(500),
    };

    private readonly Dictionary<string, RadioButton> _toolBtns;
    private ToggleButton? _recordButton;
    private ListView? _estimateList;
    private TextBox? _estimateFilterBox;
    private CheckBox? _estimateCurrentSheetOnlyBox;
    private Button? _estimateSelectButton;
    private Button? _estimateGoToPageButton;
    private Button? _estimatePropertiesButton;
    private Button? _estimateOpenWindowButton;
    private TextBlock? _estimateSummaryText;
    private EstimatingWindow? _estimatingWindow;
    private IReadOnlyList<PdfMetadataPageResult> _sheetManagerMetadataResults = [];
    private TextBox? _massingDraftTextBox;
    private ListView? _massingMarkerList;
    private Canvas? _massingPreviewCanvas;
    private TextBlock? _massingPreviewStatusText;
    private Viewport3D? _massingViewport3D;
    private TextBlock? _massingViewportStatusText;
    private PerspectiveCamera? _massingCamera3D;
    private Point? _massing3DDragStart;
    private Point? _massing3DMouseDown;
    private bool _massing3DMouseMoved;
    private string _selectedMassing3DObjectId = "";
    private TextBox? _massingMarkerDetailsTextBox;
    private Button? _massingOpenDraftButton;
    private Button? _massingReviewRoofButton;
    private Button? _massingReviewOpeningsButton;
    private Button? _massingAcceptDraftButton;
    private Button? _massingJumpMarkerButton;
    private Button? _massingOpenMarkerButton;
    private Button? _massingOpenMarkerCropButton;
    private TabControl? _rightWorkspaceTabs;
    private TabItem? _massingTab;
    private SmartMassingDraft? _currentMassingDraft;
    private Point3D _massing3DTarget = new(0, 0, 0);
    private double _massing3DSceneRadius = 20;
    private double _massing3DDistance = 60;
    private double _massing3DYaw = -38;
    private double _massing3DPitch = 28;
    private bool _syncingEstimateSelection;
    private bool _syncingViewportSelectionFromTakeoffItem;
    private bool _updatingRecordButton;
    private bool _updatingConstraintButtons;
    private bool _updatingPageTabs;
    private bool _syncingPageTreeSelection;
    private bool _syncingTakeoffTreeSelection;
    private bool _suppressCanvasFocusFromTakeoffSelection;
    private bool _suppressTakeoffSelectionFromViewport;
    private bool _suppressTreeExpansionTracking;
    private TreeViewItem? _pageTakeoffLegendDropTarget;
    private bool _pageTakeoffLegendDropAfter;
    private TreeViewItem? _takeoffSectionDropTarget;
    private bool _takeoffSectionDropAllowed;
    private string _takeoffSectionDropStatus = "";
    private TreeViewItem? _takeoffPositionDropTarget;
    private bool _takeoffPositionDropAfter;
    private bool _takeoffPositionDropAllowed;
    private string _takeoffPositionDropStatus = "";
    private string _lastDrawingTool = "point";
    private bool _inboxExpanded = false;
    private double _inboxExpandedHeight = 170.0;
    // Clipboard support types moved to MainWindow.SupportTypes.cs
    private TakeoffsClipboard? _takeoffsClipboard;
    // Takeoff edit and measurement clipboard support types moved to MainWindow.SupportTypes.cs
    private Point? _takeoffsDragStart;
    private PageTabState? _activePageTab;
    // Tree node and metadata support types moved to MainWindow.SupportTypes.cs
    private static readonly string[] PageSuffixTopOrder = ["v", "wt", "ft", "sv", "sw"];
    private static readonly string[] PageSuffixDetectionOrder = ["sec", "wt", "ft", "sv", "sw", "u", "d", "v"];

    // Page tab support type moved to MainWindow.SupportTypes.cs

    // Metric presets — 1:N ratio
    private static readonly (string Label, double Ratio)[] MetricPresets =
    [
        ("1:10",   10),
        ("1:20",   20),
        ("1:25",   25),
        ("1:50",   50),
        ("1:100",  100),
        ("1:200",  200),
        ("1:500",  500),
        ("1:1000", 1000),
    ];

    // Imperial presets — x" = 1'-0"  ⟹  ratio = 12 / x_inches
    private static readonly (string Label, double Ratio)[] ImperialPresets =
    [
        ("3\" = 1'-0\"",     4),
        ("1-1/2\" = 1'-0\"", 8),
        ("1\" = 1'-0\"",     12),
        ("3/4\" = 1'-0\"",   16),
        ("1/2\" = 1'-0\"",   24),
        ("3/8\" = 1'-0\"",   32),
        ("1/4\" = 1'-0\"",   48),
        ("3/16\" = 1'-0\"",  64),
        ("1/8\" = 1'-0\"",   96),
        ("3/32\" = 1'-0\"",  128),
        ("1/16\" = 1'-0\"",  192),
    ];

    private static readonly string[] AiMarkerTypes =
    [
        "exterior_corner",
        "wall_height_sample",
        "window_sample",
        "door_sample",
        "opening_sample",
        "roof_note",
        "roof_edge_sample",
        "ridge_sample",
        "valley_sample",
        "roof_high_edge",
        "roof_low_edge",
        "overhang_sample",
        "dimension_text_sample",
        "ignore_area",
    ];

    private static readonly string[] AiMarkerSampleKinds =
    [
        "positive",
        "negative",
        "ignore",
    ];

    private const string MarkerTypeFilterAllInbox = "All inbox";
    private const string MarkerTypeFilterAllMarkers = "All markers";
    private const string MarkerSampleFilterAny = "Any sample";
    private const int MaxAutoCropBookmarkDepth = 1;
    private const float AutoCropBookmarkDuplicateTolerancePt = 48f;
    private const float AutoCropBookmarkPaddingPt = 96f;
    private const float AutoCropBookmarkMinSizePt = 240f;
    private const float FindSimilarNearbyContextPaddingPt = 360f;
    private const float FindSimilarNearbyContextMinSizePt = 720f;
    private const float RoofRecognitionContextPaddingPt = 540f;
    private const float RoofRecognitionMinCropSizePt = 1440f;
    private const float RoofRecognitionFullPageSizePt = 20000f;
    private const float RoofRecognitionMarkerDuplicateTolerancePt = 32f;
    private static readonly string[] RoofRecognitionMarkerTypes =
    [
        "roof_note",
        "roof_edge_sample",
        "ridge_sample",
        "valley_sample",
        "roof_high_edge",
        "roof_low_edge",
        "overhang_sample",
    ];

    private static readonly (string Label, string Hex)[] TakeoffColorPresets =
    [
        ("Red", "#FF4444"),
        ("Blue", "#2196F3"),
        ("Green", "#4CAF50"),
        ("Orange", "#FF9800"),
        ("Purple", "#9C27B0"),
        ("Cyan", "#00BCD4"),
        ("Yellow", "#FFC107"),
        ("Pink", "#E91E63"),
    ];

    public MainWindow()
    {
        InitializeComponent();

        _viewport = new PdfViewport();
        _viewport.StatusChanged      += msg => TxtStatus.Text = msg;
        _viewport.ScaleChanged       += OnScaleChanged;
        _viewport.ToolChanged        += OnToolChanged;
        _viewport.SnapChanged        += OnViewportSnapChanged;
        _viewport.OrthoChanged       += OnViewportOrthoChanged;
        _viewport.LayersChanged      += OnLayersChanged;
        _viewport.PdfLayersDiscovered += OnPdfLayersDiscovered;
        _viewport.PdfLayerTraceStateChanged += RefreshPdfLayerTraceControls;
        _viewport.MeasurementAdded   += OnMeasurementAdded;
        _viewport.MeasurementRemoved += OnMeasurementRemoved;
        _viewport.MeasurementChanged += OnMeasurementChanged;
        _viewport.MeasurementSelectionChanged += OnViewportMeasurementSelectionChanged;
        _viewport.MeasurementsSelectionChanged += OnViewportMeasurementsSelectionChanged;
        _viewport.PageAnnotationAdded += OnPageAnnotationChanged;
        _viewport.PageAnnotationRemoved += OnPageAnnotationChanged;
        _viewport.PageAnnotationChanged += OnPageAnnotationChanged;
        _viewport.TransformSelectionChanged += OnViewportTransformSelectionChanged;
        _viewport.CopyMeasurementsRequested += CopyMeasurementsToClipboard;
        _viewport.PasteMeasurementsRequested += PasteMeasurementsFromClipboard;
        _viewport.ContextRequested   += OnViewportContextRequested;
        _viewport.JoistDirectionCaptured += OnJoistDirectionCaptured;
        ViewportSurfaceHost.Children.Add(_viewport);

        _toolBtns = new Dictionary<string, RadioButton>
        {
            ["pan"]   = BtnPan,
            ["select"] = BtnSelect,
            ["scale"] = BtnScale,
            ["ruler"] = BtnRuler,
            ["drawline"] = BtnDrawLine,
            ["drawarrow"] = BtnDrawArrow,
            ["drawrect"] = BtnDrawRect,
            ["point"] = BtnPoint,
            ["line"]  = BtnLine,
            ["area"]  = BtnArea,
        };
        SetupToolButtonContent();
        BtnPoint.ToolTip = "Count item (P)";
        SetupRecordButton();
        SetupEstimateTable();
        ApplyToolSelection("select");
        UpdateTransformEditControls();

        CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, (_, _) => BtnOpen_Click(null!, null!)));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Open, Key.O, ModifierKeys.Control));
        CommandBindings.Add(new CommandBinding(OpenRecentJobsCommand, (_, _) => ShowRecentJobPicker()));
        InputBindings.Add(new KeyBinding(OpenRecentJobsCommand, Key.O, ModifierKeys.Control | ModifierKeys.Shift));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_, _) => BtnSave_Click(null!, null!)));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Save, Key.S, ModifierKeys.Control));
        CommandBindings.Add(new CommandBinding(OpenCommandPaletteCommand, (_, _) => ShowCommandPalette()));
        InputBindings.Add(new KeyBinding(OpenCommandPaletteCommand, Key.P, ModifierKeys.Control | ModifierKeys.Shift));
        PagesTree.PreviewMouseRightButtonDown += PagesTree_PreviewMouseRightButtonDown;
        PagesTree.PreviewMouseLeftButtonDown += PagesTree_PreviewMouseLeftButtonDown;
        PagesTree.MouseMove += PagesTree_MouseMove;
        PagesTree.DragOver += PagesTree_DragOver;
        PagesTree.DragLeave += PagesTree_DragLeave;
        PagesTree.Drop += PagesTree_Drop;
        PagesTree.KeyDown += PagesTree_KeyDown;
        PagesTree.RequestBringIntoView += TreeView_RequestBringIntoViewKeepLeft;
        PagesTree.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler(PagesTreeItem_Expanded));
        PagesTree.AddHandler(TreeViewItem.CollapsedEvent, new RoutedEventHandler(PagesTreeItem_Collapsed));
        PagesTree.AllowDrop = true;
        TakeoffsTree.PreviewMouseRightButtonDown += TakeoffsTree_PreviewMouseRightButtonDown;
        TakeoffsTree.PreviewMouseLeftButtonDown += TakeoffsTree_PreviewMouseLeftButtonDown;
        TakeoffsTree.MouseMove += TakeoffsTree_MouseMove;
        TakeoffsTree.DragOver += TakeoffsTree_DragOver;
        TakeoffsTree.DragLeave += TakeoffsTree_DragLeave;
        TakeoffsTree.Drop += TakeoffsTree_Drop;
        TakeoffsTree.KeyDown += TakeoffsTree_KeyDown;
        TakeoffsTree.ContextMenuOpening += TakeoffsTree_ContextMenuOpening;
        TakeoffsTree.RequestBringIntoView += TreeView_RequestBringIntoViewKeepLeft;
        TakeoffsTree.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler(TakeoffsTreeItem_Expanded));
        TakeoffsTree.AddHandler(TreeViewItem.CollapsedEvent, new RoutedEventHandler(TakeoffsTreeItem_Collapsed));
        TakeoffsTree.AllowDrop = true;
        ObservationsListView.MouseDoubleClick += (_, _) => OpenSelectedInboxObservation();
        ObservationsListView.KeyDown += ObservationsListView_KeyDown;
        ObservationsListView.ContextMenu = BuildObservationsContextMenu();
        InitializeMarkerFilterControls();
        _takeoffAutosaveTimer.Tick += (_, _) => FlushTakeoffAutosaves();
        TakeoffsTree.ContextMenu = BuildTakeoffsRootContextMenu();
        BtnLayersOn.IsEnabled = false;
        BtnLayersOff.IsEnabled = false;
        BtnLayersClearHi.IsEnabled = false;
        BtnLayerTraceMode.IsEnabled = false;
        BtnLayerTraceCycle.IsEnabled = false;
        BtnLayerTraceApply.IsEnabled = false;
        BtnViewportLayerTraceToggle.IsEnabled = false;
        BtnViewportLayerTraceCycle.IsEnabled = false;

        ApplyPersistedSettings();
        Loaded += (_, _) => Dispatcher.InvokeAsync(
            TryOpenLastJobFromSettings,
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    // Toolbar and job lifecycle moved to MainWindow.JobLifecycle.cs

    // Tool controls moved to MainWindow.ToolControls.cs

    // Pages tree workflow moved to MainWindow.PagesTree.cs

    // Takeoffs tree workflow moved to MainWindow.TakeoffsTree.cs

    // Measurement clipboard and autosave callbacks moved to MainWindow.MeasurementClipboard.cs

    // Shared tree and estimate helpers moved to MainWindow.TreeHelpers.cs

    // Viewport callbacks moved to MainWindow.ViewportCallbacks.cs

    // Application settings and small utility dialogs moved to MainWindow.Utilities.cs

    // AI Inbox and crop bookmark workflow moved to MainWindow.AiInbox.cs

    // 3D Massing workflow moved to MainWindow.MassingWorkflow.cs

    // AI marker management and action workflows moved to MainWindow.AiActions.cs

    // Display row and massing support types moved to MainWindow.SupportTypes.cs

    protected override void OnClosed(EventArgs e)
    {
        SaveSidePanelWidths();
        SaveCurrentPageAnnotations();
        base.OnClosed(e);
        FlushTakeoffAutosaves();
        ClearJobRecoveryLock();
        PdfLayerRenderService.StopWorker();
    }
}
