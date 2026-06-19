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
    private readonly TreeExpansionState _expandedPageTreePaths = new();
    private readonly TreeExpansionState _expandedTakeoffTreePaths = new();
    private readonly HashSet<string> _hiddenAiMarkerTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pagesWithHiddenRulers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<GeometryModel3D, Massing3DObjectInfo> _massing3DObjectInfo = [];
    private readonly List<PageTabState> _pageTabs = [];
    private readonly List<DetachedSheetWindow> _detachedSheetWindows = [];
    private int _lastMeasurementPageFolderRepairCount;
    private int _lastMeasurementPageFolderUnresolvedCount;
    private Point? _pagesDragStart;
    private TreeViewItem? _pagesDragItem;
    private bool _pagesDragArmed;
    private string? _pagesRangeAnchorPath;
    private string? _pageTakeoffRangeAnchorKey;
    private string? _takeoffsRangeAnchorPath;
    private string? _takeoffSectionRangeAnchorKey;
    private TreeViewItem? _takeoffsDragItem;
    private bool _takeoffsDragArmed;
    private readonly HashSet<TakeoffItem> _pendingTakeoffAutosaves = [];
    private readonly System.Windows.Threading.DispatcherTimer _takeoffAutosaveTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(ViewportConstants.AutosaveDebounceMs),
    };
    private readonly System.Windows.Threading.DispatcherTimer _sheetLegendAutoSortTimer = new()
    {
        Interval = TimeSpan.FromMinutes(5),
    };

    private readonly Dictionary<string, RadioButton> _toolBtns;
    private string _annotationColor = "#FF4444";
    private double _annotationStrokeWidth = 5.0;
    private ToggleButton? _recordButton;
    private bool _updatingRulerVisibilityButton;
    private ListView? _estimateList;
    private TabItem? _estimateTab;
    private bool _estimateTableDirty;
    private TextBox? _estimateFilterBox;
    private CheckBox? _estimateCurrentSheetOnlyBox;
    private Button? _estimateSelectButton;
    private Button? _estimateGoToPageButton;
    private Button? _estimatePropertiesButton;
    private Button? _estimateOpenWindowButton;
    private TextBlock? _estimateSummaryText;
    private EstimatingWindow? _estimatingWindow;
    private PageSetupWindow? _pageSetupWindow;
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
    private string _selectedMassingMarkerId = "";
    private TextBox? _massingMarkerDetailsTextBox;
    private Button? _massingOpenDraftButton;
    private Button? _massingReviewRoofButton;
    private Button? _massingReviewOpeningsButton;
    private Button? _massingAcceptDraftButton;
    private Button? _massingJumpMarkerButton;
    private Button? _massingOpenMarkerButton;
    private Button? _massingOpenMarkerCropButton;
    private TabControl? _rightWorkspaceTabs;
    private TabItem? _massingTab = null;
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
    private int _takeoffSelectionSyncVersion;
    private bool _suppressCanvasFocusFromTakeoffSelection;
    private bool _suppressTakeoffSelectionFromViewport;
    private bool _suppressTreeExpansionTracking;
    private TreeViewItem? _pageTakeoffLegendDropTarget;
    private bool _pageTakeoffLegendDropAfter;
    private TreeViewItem? _pagesPositionDropTarget;
    private bool _pagesPositionDropAfter;
    private bool _pagesPositionDropAllowed;
    private string _pagesPositionDropStatus = "";
    private string? _pendingPagesTreeDropRefreshPath;
    private bool _pendingPagesTreeDropReloadActiveTab;
    private int _pendingPagesTreeDropRefreshVersion;
    private TreeViewItem? _takeoffSectionDropTarget;
    private bool _takeoffSectionDropAllowed;
    private string _takeoffSectionDropStatus = "";
    private TreeViewItem? _takeoffPositionDropTarget;
    private bool _takeoffPositionDropAfter;
    private bool _takeoffPositionDropAllowed;
    private string _takeoffPositionDropStatus = "";
    private string _lastDrawingTool = "point";
    private string _newCountSymbol = CountDisplaySymbol.Circle;
    private bool _inboxExpanded = false;
    private double _inboxExpandedHeight = 170.0;
    // Clipboard support types moved to MainWindow.SupportTypes.cs
    private TakeoffsClipboard? _takeoffsClipboard;
    // Takeoff edit and measurement clipboard support types moved to MainWindow.SupportTypes.cs
    private Point? _takeoffsDragStart;
    private PageTabState? _activePageTab;
    // Tree node and metadata support types moved to MainWindow.SupportTypes.cs
    // Page-sort suffix order/rules now live in PageSortConfig (editable in Settings).

    // Page tab support type moved to MainWindow.SupportTypes.cs

    // Metric presets — 1:N ratio
    private static readonly (string Label, double Ratio)[] MetricPresets =
    [
        ("1:1",    1),
        ("1:2",    2),
        ("1:5",    5),
        ("1:10",   10),
        ("1:20",   20),
        ("1:25",   25),
        ("1:30",   30),
        ("1:40",   40),
        ("1:50",   50),
        ("1:75",   75),
        ("1:100",  100),
        ("1:125",  125),
        ("1:150",  150),
        ("1:200",  200),
        ("1:250",  250),
        ("1:500",  500),
        ("1:1000", 1000),
    ];

    // Imperial presets — x" = 1'-0"  ⟹  ratio = 12 / x_inches
    private static readonly (string Label, double Ratio)[] ImperialPresets =
    [
        ("1\" = 1\"",        1),
        ("6\" = 1'-0\"",     2),
        ("4\" = 1'-0\"",     3),
        ("3\" = 1'-0\"",     4),
        ("2\" = 1'-0\"",     6),
        ("1 1/2\" = 1'-0\"", 8),
        ("1 1/4\" = 1'-0\"", 9.6),
        ("1\" = 1'-0\"",     12),
        ("3/4\" = 1'-0\"",   16),
        ("5/8\" = 1'-0\"",   19.2),
        ("1/2\" = 1'-0\"",   24),
        ("3/8\" = 1'-0\"",   32),
        ("1/4\" = 1'-0\"",   48),
        ("3/16\" = 1'-0\"",  64),
        ("1/8\" = 1'-0\"",   96),
        ("3/32\" = 1'-0\"",  128),
        ("1/16\" = 1'-0\"",  192),
        ("1/32\" = 1'-0\"",  384),
        ("1/64\" = 1'-0\"",  768),
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
        ("Gray", "#808080"),
        ("Teal", "#009688"),
        ("Indigo", "#3F51B5"),
        ("Lime", "#8BC34A"),
        ("Brown", "#795548"),
        ("Navy", "#0D47A1"),
        ("Black", "#212121"),
    ];

    public MainWindow()
    {
        InitializeComponent();
        InitializeThreeDViewer();
        _newCountSymbol = CountDisplaySymbol.Normalize(_settings.DefaultCountSymbol);

        _viewport = new PdfViewport
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ActiveAnnotationColor = _annotationColor,
            ActiveAnnotationStrokeWidth = _annotationStrokeWidth,
            ActiveCountSymbol = _newCountSymbol,
        };
        _viewport.StatusChanged      += SetViewportStatus;
        _viewport.ScaleChanged       += OnScaleChanged;
        _viewport.ToolChanged        += OnToolChanged;
        _viewport.SnapChanged        += OnViewportSnapChanged;
        _viewport.PdfSnapChanged     += OnViewportPdfSnapChanged;
        _viewport.OrthoChanged       += OnViewportOrthoChanged;
        _viewport.BoxModeChanged     += OnViewportBoxModeChanged;
        _viewport.LayersChanged      += OnLayersChanged;
        _viewport.PdfLayersDiscovered += OnPdfLayersDiscovered;
        _viewport.PdfLayerTraceStateChanged += RefreshPdfLayerTraceControls;
        _viewport.MeasurementAdded   += OnMeasurementAdded;
        _viewport.MeasurementRemoved += OnMeasurementRemoved;
        _viewport.MeasurementsAdded += OnMeasurementsAdded;
        _viewport.MeasurementsRemoved += OnMeasurementsRemoved;
        _viewport.MeasurementChanged += OnMeasurementChanged;
        _viewport.MeasurementsChanged += OnMeasurementsChanged;
        _viewport.MeasurementSelectionChanged += OnViewportMeasurementSelectionChanged;
        _viewport.MeasurementsSelectionChanged += OnViewportMeasurementsSelectionChanged;
        _viewport.PageAnnotationAdded += OnPageAnnotationChanged;
        _viewport.PageAnnotationRemoved += OnPageAnnotationChanged;
        _viewport.PageAnnotationChanged += OnPageAnnotationChanged;
        _viewport.PageAnnotationTextRequested += RequestPageAnnotationText;
        _viewport.TransformSelectionChanged += OnViewportTransformSelectionChanged;
        _viewport.CopyMeasurementsRequested += CopyMeasurementsToClipboard;
        _viewport.PasteMeasurementsRequested += PasteMeasurementsFromClipboard;
        _viewport.ContextRequested   += OnViewportContextRequested;
        _viewport.AiCropNoteSelectionCompleted += OnAiCropNoteSelectionCompleted;
        _viewport.SimilarCountSelectionCompleted += OnSimilarCountSelectionCompleted;
        _viewport.ThreeDRoofGuideAdded += OnThreeDRoofGuideAdded;
        _viewport.ThreeDRoofGuideSelectionRequested += OnThreeDRoofGuideSelectionRequested;
        _viewport.JoistDirectionCaptured += OnJoistDirectionCaptured;
        _viewport.BeamMeasurementCompleted += OnBeamMeasurementCompleted;
        _viewport.OpeningMeasurementCompleted += OnOpeningMeasurementCompleted;
        _viewport.SheetOverlayTransformChanged += OnSheetOverlayTransformChanged;
        _viewport.SheetOverlayRenderScaleRefreshRequested += OnSheetOverlayRenderScaleRefreshRequested;
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
            ["drawcloud"] = BtnDrawCloud,
            ["drawarea"] = BtnDrawAreaAnnot,
            ["note"] = BtnNote,
            ["point"] = BtnPoint,
            ["line"]  = BtnLine,
            ["area"]  = BtnArea,
            ["joistarea"] = BtnJoistArea,
            ["beam"] = BtnBeam,
            ["openings"] = BtnOpenings,
            ["areacut"] = BtnAreaCut,
        };
        SetupToolButtonContent();
        BtnPan.ToolTip = $"Pan ({KeyboardShortcutKeys.EnglishLayoutDisplay("v")})";
        BtnSelect.ToolTip = $"Select ({KeyboardShortcutKeys.EnglishLayoutDisplay("e")})";
        BtnScale.ToolTip = $"Scale ({KeyboardShortcutKeys.EnglishLayoutDisplay("s")})";
        BtnRuler.ToolTip = $"Ruler ({KeyboardShortcutKeys.EnglishLayoutDisplay("r")})";
        BtnDrawLine.ToolTip = $"Draw line ({KeyboardShortcutKeys.EnglishLayoutDisplay("d")})";
        BtnDrawRect.ToolTip = "Draw box";
        BtnDrawCloud.ToolTip = "Cloud annotation";
        BtnDrawAreaAnnot.ToolTip = "Filled area annotation";
        BtnNote.ToolTip = $"Sheet note ({KeyboardShortcutKeys.EnglishLayoutDisplay("n")})";
        BtnPoint.ToolTip = $"Count item ({KeyboardShortcutKeys.EnglishLayoutDisplay("p")})";
        BtnLine.ToolTip = $"Line item ({KeyboardShortcutKeys.EnglishLayoutDisplay("l")})";
        BtnArea.ToolTip = $"Area item ({KeyboardShortcutKeys.EnglishLayoutDisplay("a")})";
        BtnJoistArea.ToolTip = $"Joist area ({KeyboardShortcutKeys.EnglishLayoutDisplay("j")})";
        BtnBeam.ToolTip = $"Beam: measure length, create Count item, and place first Count mark ({KeyboardShortcutKeys.EnglishLayoutDisplay("b")})";
        BtnOpenings.ToolTip = $"Openings: measure box, create Count item, and place the Count mark in the center ({KeyboardShortcutKeys.EnglishLayoutDisplay("o")})";
        BtnAreaCut.ToolTip = $"Area cut ({KeyboardShortcutKeys.EnglishLayoutDisplay("x")})";
        SetupRecordButton();
        SetupEstimateTable();
        InstallOutputSettingsTab();
        InitializeBookmarksTab();
        InitializeTemplatesTab();
        ApplyToolSelection("select");
        ApplyRulerVisibilityToViewport();
        UpdateDefaultCountSymbolButton();
        UpdateTransformEditControls();

        CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, (_, _) => BtnOpen_Click(null!, null!)));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Open, Key.O, ModifierKeys.Control));
        CommandBindings.Add(new CommandBinding(OpenRecentJobsCommand, (_, _) => ShowRecentJobPicker()));
        InputBindings.Add(new KeyBinding(OpenRecentJobsCommand, Key.O, ModifierKeys.Control | ModifierKeys.Shift));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_, _) => BtnSave_Click(null!, null!)));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Save, Key.S, ModifierKeys.Control));
        CommandBindings.Add(new CommandBinding(OpenCommandPaletteCommand, (_, _) => ShowCommandPalette()));
        InputBindings.Add(new KeyBinding(OpenCommandPaletteCommand, Key.P, ModifierKeys.Control | ModifierKeys.Shift));
        PreviewKeyDown += MainWindow_GlobalPreviewKeyDown;
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
        TakeoffsTree.PreviewKeyDown += TakeoffsTree_KeyDown;
        TakeoffsTree.ContextMenuOpening += TakeoffsTree_ContextMenuOpening;
        TakeoffsTree.RequestBringIntoView += TreeView_RequestBringIntoViewKeepLeft;
        TakeoffsTree.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler(TakeoffsTreeItem_Expanded));
        TakeoffsTree.AddHandler(TreeViewItem.CollapsedEvent, new RoutedEventHandler(TakeoffsTreeItem_Collapsed));
        TakeoffsTree.AllowDrop = true;
        ObservationsListView.MouseDoubleClick += (_, _) => OpenSelectedInboxObservation();
        ObservationsListView.KeyDown += ObservationsListView_KeyDown;
        ObservationsListView.ContextMenu = BuildObservationsContextMenu();
        InitializeMarkerFilterControls();
        InitializeRafterControls();
        _takeoffAutosaveTimer.Tick += (_, _) => FlushTakeoffAutosaves();
        _sheetLegendAutoSortTimer.Tick += (_, _) => RunSheetLegendAutoSortSweep();
        _sheetLegendAutoSortTimer.Start();
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
        Loaded += MainWindow_Loaded;
    }

    private void SetViewportStatus(string msg)
    {
        if (Dispatcher.CheckAccess())
        {
            TxtStatus.Text = msg;
            return;
        }

        Dispatcher.BeginInvoke(new Action(() => TxtStatus.Text = msg));
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            TryOpenLastJobFromSettings();
            await TryRunGuideScreenshotCaptureAsync();
            await TryRunViewportPageStressSmokeAsync();
            await TryRunTakeoffsMoveSmokeAsync();
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
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
        _sheetLegendAutoSortTimer.Stop();
        SaveSidePanelWidths();
        SaveCurrentPageAnnotations();
        base.OnClosed(e);
        FlushTakeoffAutosaves();
        ClearJobRecoveryLock();
        PdfLayerRenderService.StopWorker();
    }
}
