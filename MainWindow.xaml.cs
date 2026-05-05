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
using SmartTakeoffs.Controls;
using SkiaSharp;

namespace SmartTakeoffs;

public partial class MainWindow : Window
{
    private readonly PdfViewport _viewport;
    private readonly AppSettings _settings = AppSettingsStore.Load();
    private bool _isApplyingSettings;
    private bool _isRunningAiRequest;

    private SmartTakeoffsJob? _currentJob;
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
    private enum PagesClipboardMode { Copy, Cut }
    private sealed record PagesClipboardEntry(string SourcePath, bool IsPage);
    private sealed record PagesClipboard(IReadOnlyList<PagesClipboardEntry> Entries, PagesClipboardMode Mode);
    private enum TakeoffsClipboardMode { Copy, Cut }
    private sealed record TakeoffsClipboardEntry(string SourcePath, bool IsItem);
    private sealed record TakeoffsClipboard(IReadOnlyList<TakeoffsClipboardEntry> Entries, TakeoffsClipboardMode Mode);
    private TakeoffsClipboard? _takeoffsClipboard;
    private sealed record BulkTakeoffPropertiesEdit(
        bool ApplyColor,
        string Color,
        bool ApplyUnitPrice,
        double UnitPrice,
        bool ApplyNotes,
        string Notes);
    private sealed record JoistTakeoffEdit(
        bool Enabled,
        string JoistType,
        double SpacingInches,
        double DirectionDegrees,
        string LengthRounding,
        bool ShowLabels);
    private enum MeasurementPasteMode { SameTakeoffs, NewTakeoffs }
    private sealed record MeasurementClipboardEntry(
        string MeasurementType,
        string MeasurementName,
        string MeasurementNotes,
        string MeasurementColor,
        IReadOnlyList<SKPoint> Points,
        string SourcePageFolder,
        double ScaleMetersPerPt,
        string SourceTakeoffFolder,
        string SourceTakeoffName,
        string SourceTakeoffColor,
        double SourceTakeoffUnitPrice,
        string SourceTakeoffNotes);
    private sealed record MeasurementClipboard(IReadOnlyList<MeasurementClipboardEntry> Entries);
    private Point? _takeoffsDragStart;
    private PageTabState? _activePageTab;
    private sealed record TakeoffMeasurementNode(TakeoffItem Item, Measurement Measurement);
    private sealed record TakeoffSectionDrag(IReadOnlyList<TakeoffMeasurementNode> Nodes);
    private sealed record PageTakeoffNode(PageInfo Page, TakeoffItem Takeoff);
    private sealed record PageTakeoffLegendDrag(string PageFolder, IReadOnlyList<string> TakeoffFolders);
    private sealed record AiMarkerInput(string MarkerType, string SampleKind, string Value, string Note);
    private sealed record MarkerSetInput(string Name, string Description);
    private sealed record PdfMetadataPageResult(PageInfo Page, bool Ok, PdfSheetMetadata? Metadata, string Error);
    private static readonly string[] PageSuffixTopOrder = ["v", "wt", "ft", "sv", "sw"];
    private static readonly string[] PageSuffixDetectionOrder = ["sec", "wt", "ft", "sv", "sw", "u", "d", "v"];

    private sealed class PageTabState(string pageFolder, string pageName)
    {
        public string PageFolder { get; set; } = pageFolder;
        public string PageName { get; set; } = pageName;
        public PdfViewport.ViewState? ViewState { get; set; }
    }

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

    // ── Toolbar ───────────────────────────────────────────────────────────────

    private void SetupToolButtonContent()
    {
        Brush glyphBrush = Application.Current.Resources["ControlForegroundBrush"] as Brush
            ?? Brushes.Black;

        BtnPan.Content = "Pan";
        BtnSelect.Content = "Select";
        BtnScale.Content = "Scale";
        BtnRuler.Content = "Ruler";
        BtnDrawLine.Content = "Draw";
        BtnDrawArrow.Content = "Arrow";
        BtnDrawRect.Content = "Box";
        BtnPoint.Content = CreateToolGlyphLabel(MeasurementGlyphKind.Count, "Count", glyphBrush);
        BtnLine.Content = CreateToolGlyphLabel(MeasurementGlyphKind.Line, "Line", glyphBrush);
        BtnArea.Content = CreateToolGlyphLabel(MeasurementGlyphKind.Area, "Area", glyphBrush);
    }

    private static FrameworkElement CreateToolGlyphLabel(
        MeasurementGlyphKind kind,
        string label,
        Brush glyphBrush)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(Controls.MeasurementGlyph.CreateWpf(
            kind,
            glyphBrush,
            14,
            new Thickness(0, 0, 4, 0)));
        row.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    private void InitializeMarkerFilterControls()
    {
        ComboMarkerTypeFilter.Items.Clear();
        ComboMarkerTypeFilter.Items.Add(MarkerTypeFilterAllInbox);
        ComboMarkerTypeFilter.Items.Add(MarkerTypeFilterAllMarkers);
        foreach (string markerType in AiMarkerTypes)
            ComboMarkerTypeFilter.Items.Add(markerType);
        ComboMarkerTypeFilter.SelectedIndex = 0;

        ComboMarkerSampleFilter.Items.Clear();
        ComboMarkerSampleFilter.Items.Add(MarkerSampleFilterAny);
        foreach (string sampleKind in AiMarkerSampleKinds)
            ComboMarkerSampleFilter.Items.Add(sampleKind);
        ComboMarkerSampleFilter.SelectedIndex = 0;
    }

    private void MarkerFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LoadObservationsInbox();
    }

    private void BtnOpenAiSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenAiSettingsDialog(_settings)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        _settings.OpenAiModel = AppSettingsStore.NormalizeOpenAiModel(dialog.SelectedModel);
        SaveAppSettings();

        OpenAiKeyStatus keyStatus = AppSettingsStore.GetOpenAiKeyStatus();
        OpenAiModelStatus modelStatus = AppSettingsStore.GetOpenAiModelStatus(_settings);
        TxtStatus.Text =
            $"OpenAI settings saved. Key: {(keyStatus.Found ? "found" : "missing")} ({keyStatus.Source}); " +
            $"model: {modelStatus.Model} ({modelStatus.Source}).";
    }

    private void BtnOpen_Click(object sender, RoutedEventArgs e)
    {
        ShowRecentJobPicker();
    }

    private void BtnOpenJobsFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenJobFromJobsRootDialog();
    }

    private void BtnNewJob_Click(object sender, RoutedEventArgs e)
    {
        CreateJobFromDialog();
    }

    private void OpenJob(string rootPath, string? initialPageFolder = null)
    {
        SaveCurrentPageScale();
        _currentJob = SmartTakeoffsJobStore.LoadJob(rootPath);
        _currentPage = null;
        _currentPdfPath = "";
        _pagesClipboard = null;
        _takeoffsClipboard = null;
        _measurementClipboard = null;
        _pagesMultiSelection.Clear();
        _pageTakeoffMultiSelection.Clear();
        _takeoffsMultiSelection.Clear();
        _takeoffSectionMultiSelection.Clear();
        _pagesRangeAnchorPath = null;
        _pageTakeoffRangeAnchorKey = null;
        _takeoffsRangeAnchorPath = null;
        _takeoffSectionRangeAnchorKey = null;
        _expandedPageTreePaths.Clear();
        _expandedTakeoffTreePaths.Clear();
        _hiddenAiMarkerTypes.Clear();
        _pageTabs.Clear();
        _activePageTab = null;
        RefreshPageTabs(null);
        _takeoffItems.Clear();
        _activeItem = null;
        _activeTakeoffParentFolder = "";
        TakeoffsTree.Items.Clear();
        _viewport.SetMeasurements([]);
        _viewport.ClearPage();
        Title = $"SmartTakeoffs — {_currentJob.Name}";
        ReloadPagesTree(_currentJob.PagesRoot);
        LoadTakeoffsForJob();
        _settings.LastJobPath = _currentJob.RootPath;
        _settings.JobsRootPath = Path.GetDirectoryName(_currentJob.RootPath) ?? _settings.JobsRootPath;
        AppSettingsStore.AddJobsRoot(_settings, _settings.JobsRootPath);
        AppSettingsStore.AddRecentJob(_settings, _currentJob.RootPath, _currentJob.Name);
        SaveAppSettings();
        QueueRecentJobThumbnailGeneration(_currentJob);
        LoadPersistedMarkerVisibility();
        if (ResolveInitialPageToOpen(initialPageFolder) is { } pageToOpen)
            SelectPageByFolder(pageToOpen);
        ApplyTheme(string.Equals(_settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase), persist: false);
        TxtStatusJob.Text  = _currentJob.Name;
        TxtJobName.Text    = _currentJob.Name;
        TxtStatusPage.Text = _currentPage?.Name ?? "—";
        TxtStatus.Text = BuildMeasurementRepairStatus($"Loaded job: {_currentJob.Name}");
        LoadObservationsInbox();
        RefreshMassingDraftPanel();
        Dispatcher.BeginInvoke(
            new Action(CollapseProjectTreeDisplays),
            System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private string BuildMeasurementRepairStatus(string prefix)
    {
        if (_lastMeasurementPageFolderRepairCount <= 0 && _lastMeasurementPageFolderUnresolvedCount <= 0)
            return prefix;

        var parts = new List<string>();
        if (_lastMeasurementPageFolderRepairCount > 0)
            parts.Add($"repaired {_lastMeasurementPageFolderRepairCount} measurement page link(s)");
        if (_lastMeasurementPageFolderUnresolvedCount > 0)
            parts.Add($"{_lastMeasurementPageFolderUnresolvedCount} unresolved stale page link(s)");

        return $"{prefix}; {string.Join("; ", parts)}.";
    }

    private string? ResolveInitialPageToOpen(string? initialPageFolder)
    {
        if (_currentJob == null)
            return null;

        if (!string.IsNullOrWhiteSpace(initialPageFolder) && Directory.Exists(initialPageFolder))
            return initialPageFolder;

        if (!string.IsNullOrWhiteSpace(_settings.LastPageFolder) &&
            Directory.Exists(_settings.LastPageFolder) &&
            SmartTakeoffsJobStore.IsSameOrDescendant(_currentJob.PagesRoot, _settings.LastPageFolder))
        {
            return _settings.LastPageFolder;
        }

        return CollectPagesUnder(_currentJob.PagesRoot)
            .OrderBy(page => NormalizePath(page.FolderPath), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?.FolderPath;
    }

    private void LoadPersistedMarkerVisibility()
    {
        if (_currentJob == null)
            return;

        _hiddenAiMarkerTypes.Clear();
        foreach (string markerType in SmartContextStore.LoadHiddenMarkerTypes(_currentJob))
            _hiddenAiMarkerTypes.Add(markerType);
    }

    private void SavePersistedMarkerVisibility()
    {
        if (_currentJob == null)
            return;

        SmartContextStore.SaveHiddenMarkerTypes(_currentJob, _hiddenAiMarkerTypes);
    }

    private void TryAutoLoad()
    {
        var (scale, _, items) = ProjectFile.Restore(_currentPdfPath);
        if (items.Count == 0) return;

        // Clear any stale in-session state first
        _takeoffItems.Clear();
        TakeoffsTree.Items.Clear();
        _activeItem = null;

        _takeoffItems.AddRange(items);
        foreach (var item in items)
            AddTakeoffTreeItem(item);

        _viewport.SetMeasurements(items.SelectMany(i => i.Measurements));

        if (scale > 0 && (_currentPage == null || _currentPage.ScaleMetersPerPt <= 0))
        {
            _viewport.ScaleMetersPerPt = scale;
            if (_currentPage != null)
            {
                _currentPage.ScaleMetersPerPt = scale;
                SaveCurrentPageScale();
            }
            const double PT_M = 25.4 / 72.0 / 1000.0;
            double ratio = scale / PT_M;
            TxtScaleRatio.Text = $"{ratio:F0}";
            TxtScaleInfo.Text  = $"≈1:{ratio:F0}";
        }

        // Select first item
        if (TakeoffsTree.Items.Count > 0)
            ((TreeViewItem)TakeoffsTree.Items[0]).IsSelected = true;

        RefreshAllTotals();
        TxtStatus.Text = $"Loaded {items.Count} item(s) from saved project.";
    }

    private void LoadTakeoffsForJob()
    {
        _takeoffItems.Clear();
        TakeoffsTree.Items.Clear();
        _takeoffsRangeAnchorPath = null;
        _takeoffSectionMultiSelection.Clear();
        _takeoffSectionRangeAnchorKey = null;
        _activeItem = null;
        _activeTakeoffParentFolder = _currentJob?.TakeoffsRoot ?? "";

        if (_currentJob == null)
        {
            _lastMeasurementPageFolderRepairCount = 0;
            _lastMeasurementPageFolderUnresolvedCount = 0;
            _expandedTakeoffTreePaths.Clear();
            _viewport.SetMeasurements([]);
            UpdateTotalDisplay();
            return;
        }

        LoadTakeoffChildren(_currentJob.TakeoffsRoot, TakeoffsTree);
        _lastMeasurementPageFolderRepairCount = RepairMeasurementPageFolderReferences();
        RestoreExpandedTreeState(TakeoffsTree, _expandedTakeoffTreePaths, GetTakeoffNodePath);

        _viewport.SetMeasurements(_takeoffItems.SelectMany(i => i.Measurements));

        if (FindFirstTakeoffTreeItem(TakeoffsTree) is { } firstItem)
        {
            firstItem.IsSelected = true;
        }
        else
        {
            _viewport.ActiveColor = "#FF4444";
            _viewport.ActiveTakeoffFolder = "";
        }

        PruneTakeoffsMultiSelection();
        PruneTakeoffSectionMultiSelection();
        ApplyTakeoffPageHighlights();
        RefreshAllTotals();
    }

    private int RepairMeasurementPageFolderReferences()
    {
        _lastMeasurementPageFolderUnresolvedCount = 0;
        if (_currentJob == null)
            return 0;

        List<PageInfo> pages = CollectPagesUnder(_currentJob.PagesRoot).ToList();
        var pagesByPath = pages
            .GroupBy(page => NormalizePath(page.FolderPath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var pagesByLeaf = pages
            .GroupBy(page => Path.GetFileName(NormalizePath(page.FolderPath)), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var pagesByPdfPage = pages
            .GroupBy(page => page.PdfPage)
            .ToDictionary(group => group.Key, group => group.ToList());

        int repaired = 0;
        int unresolved = 0;
        foreach (TakeoffItem item in _takeoffItems)
        {
            bool itemChanged = false;
            foreach (Measurement measurement in item.Measurements)
            {
                if (string.IsNullOrWhiteSpace(measurement.PageFolder))
                    continue;

                string oldPath = NormalizePageReferencePath(measurement.PageFolder);
                PageInfo? matchedPage = null;
                if (pagesByPath.TryGetValue(oldPath, out PageInfo? exactPage))
                {
                    matchedPage = exactPage;
                }
                else
                {
                    string leaf = Path.GetFileName(oldPath);
                    if (!string.IsNullOrWhiteSpace(leaf) &&
                        pagesByLeaf.TryGetValue(leaf, out List<PageInfo>? leafMatches) &&
                        leafMatches.Count == 1)
                    {
                        matchedPage = leafMatches[0];
                    }
                    else if (TryResolveLegacyImportedPage(leaf, pagesByPdfPage, out PageInfo legacyPage))
                    {
                        matchedPage = legacyPage;
                    }
                }

                if (matchedPage == null)
                {
                    if (!Directory.Exists(oldPath))
                        unresolved++;
                    continue;
                }

                if (!IsSamePageFolder(measurement.PageFolder, matchedPage.FolderPath))
                {
                    measurement.PageFolder = matchedPage.FolderPath;
                    if (measurement.ScaleMetersPerPt <= 0)
                        measurement.ScaleMetersPerPt = matchedPage.ScaleMetersPerPt;
                    repaired++;
                    itemChanged = true;
                }
            }

            if (itemChanged)
                SmartTakeoffsJobStore.SaveTakeoffItem(item);
        }

        _lastMeasurementPageFolderUnresolvedCount = unresolved;
        return repaired;
    }

    private static bool TryResolveLegacyImportedPage(
        string leaf,
        Dictionary<int, List<PageInfo>> pagesByPdfPage,
        out PageInfo page)
    {
        page = null!;
        if (string.IsNullOrWhiteSpace(leaf))
            return false;

        Match match = Regex.Match(leaf.Trim(), @"^Page\s+(\d+)$", RegexOptions.IgnoreCase);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out int oneBasedPage) || oneBasedPage <= 0)
            return false;

        if (TryResolveUniquePdfPage(oneBasedPage - 1, pagesByPdfPage, out page))
            return true;

        return TryResolveUniquePdfPage(oneBasedPage, pagesByPdfPage, out page);
    }

    private static bool TryResolveUniquePdfPage(
        int pdfPage,
        Dictionary<int, List<PageInfo>> pagesByPdfPage,
        out PageInfo page)
    {
        page = null!;
        if (!pagesByPdfPage.TryGetValue(pdfPage, out List<PageInfo>? matches) || matches.Count != 1)
            return false;

        page = matches[0];
        return true;
    }

    private void LoadTakeoffChildren(string parentFolder, ItemsControl parent)
    {
        foreach (string folder in SmartTakeoffsJobStore.GetOrderedChildDirectories(parentFolder))
        {
            if (SmartTakeoffsJobStore.TryReadTakeoffItem(folder) is { } item)
            {
                _takeoffItems.Add(item);
                AddTakeoffTreeItem(item, parent);
            }
            else
            {
                var node = new TakeoffFolderNode
                {
                    Name = SmartTakeoffsJobStore.DisplayName(folder),
                    FolderPath = folder,
                };
                var tvi = AddTakeoffFolderTreeItem(node, parent);
                LoadTakeoffChildren(folder, tvi);
            }
        }
    }

    private TreeViewItem AddTakeoffTreeItem(TakeoffItem item) =>
        AddTakeoffTreeItem(item, TakeoffsTree);

    private TreeViewItem AddTakeoffTreeItem(TakeoffItem item, ItemsControl parent)
    {
        var tvi = new TreeViewItem { Tag = item };
        SetTreeItemHeader(tvi, item);
        AttachContextMenu(tvi, item);
        RefreshTakeoffSectionNodes(tvi, item);
        parent.Items.Add(tvi);
        return tvi;
    }

    private TreeViewItem AddTakeoffFolderTreeItem(TakeoffFolderNode node, ItemsControl parent)
    {
        var tvi = new TreeViewItem { Tag = node };
        SetFolderTreeItemHeader(tvi, node);
        AttachFolderContextMenu(tvi, node);
        parent.Items.Add(tvi);
        return tvi;
    }

    private async void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "Import PDF",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new OpenFileDialog
        {
            Title = "Import PDF",
            Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() != true) return;

        int pageCount = _viewport.GetPageCount(dlg.FileName);
        if (pageCount <= 0)
        {
            MessageBox.Show("Could not read any pages from this PDF.", "Import PDF",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string defaultNames = string.Join(Environment.NewLine, Enumerable.Range(1, pageCount).Select(i => $"Page {i}"));
        string? rawNames = ShowMultilineInputDialog(
            $"PDF has {pageCount} page(s). Edit page names, one per line:",
            defaultNames,
            "Page Names");
        if (rawNames == null) return;

        string[] names = rawNames
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToArray();

        if (names.Length != pageCount)
        {
            MessageBox.Show($"Expected {pageCount} page name(s), got {names.Length}.",
                            "Import PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string destFolder = GetSelectedImportFolder();
        Button? importButton = sender as Button;
        try
        {
            if (importButton != null) importButton.IsEnabled = false;
            TxtStatus.Text = "Scanning PDF layer cache...";
            var progress = new Progress<string>(msg => TxtStatus.Text = msg);
            Dictionary<int, IReadOnlyList<PdfLayerInfo>> pdfLayerCache = await Task.Run(
                () => BuildPdfLayerCache(dlg.FileName, pageCount, progress));

            bool hadUserPageExpansion = _expandedPageTreePaths.Count > 0;
            var created = SmartTakeoffsJobStore.ImportPdf(_currentJob, dlg.FileName, names, destFolder, pdfLayerCache);
            ReloadPagesTree();
            if (created.Count > 0)
                SelectPageByFolder(created[0].FolderPath);
            if (!hadUserPageExpansion)
                CollapseTreeAndExpansionState(PagesTree, _expandedPageTreePaths);
            int cachedCount = pdfLayerCache.Count;
            TxtStatus.Text = $"Imported {created.Count} page(s), cached PDF layers for {cachedCount} page(s).";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Import failed:\n{ex.Message}", "Import PDF",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (importButton != null) importButton.IsEnabled = true;
        }
    }

    private static Dictionary<int, IReadOnlyList<PdfLayerInfo>> BuildPdfLayerCache(
        string pdfPath,
        int pageCount,
        IProgress<string>? progress)
    {
        var cache = new Dictionary<int, IReadOnlyList<PdfLayerInfo>>();
        for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            progress?.Report($"Scanning PDF layers {pageIndex + 1}/{pageCount}...");
            if (PdfLayerRenderService.TryReadVisibleLayers(pdfPath, pageIndex, out var layers, out _) &&
                layers.Count > 0)
            {
                cache[pageIndex] = layers;
            }
        }
        return cache;
    }

    // Tool controls

    private void BtnTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement btn && btn.Tag is string tool)
        {
            bool forceNewTakeoff = tool is "point" or "line" or "area" &&
                                   (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            SetTool(tool, forceNewTakeoff);
        }
    }

    private void SetupRecordButton()
    {
        _recordButton = new ToggleButton
        {
            Content = "Record",
            ToolTip = "Start recording into the active takeoff target",
            Padding = new Thickness(8, 2, 8, 2),
            MinWidth = 68,
            Margin = new Thickness(4, 0, 1, 0),
            FontWeight = FontWeights.Normal,
        };
        _recordButton.Checked += (_, _) => OnRecordToggled(on: true);
        _recordButton.Unchecked += (_, _) => OnRecordToggled(on: false);

        int areaIndex = MainToolBar.Items.IndexOf(BtnArea);
        MainToolBar.Items.Insert(areaIndex >= 0 ? areaIndex + 1 : MainToolBar.Items.Count, _recordButton);
    }

    // Estimating setup moved to MainWindow.Estimating.cs

    // Massing workspace panel setup moved to MainWindow.MassingPanel.cs

    // Workspace manager callbacks moved to MainWindow.WorkspaceManagers.cs

    // Estimate selection and section properties moved to MainWindow.Estimating.cs

    private void OnRecordToggled(bool on)
    {
        if (_updatingRecordButton)
            return;

        if (on)
        {
            string tool = _activeTool is "point" or "line" or "area"
                ? _activeTool
                : _lastDrawingTool;
            SetTool(tool);
            if (_activeTool is not ("point" or "line" or "area"))
                UpdateRecordButton();
            return;
        }

        if (_activeTool is "point" or "line" or "area")
            SetTool("select");
    }

    private void SetTool(string tool, bool forceNewTakeoff = false)
    {
        if (tool is "point" or "line" or "area" && !EnsureDrawingTakeoff(tool, forceNewTakeoff))
        {
            SyncToolButtonsToActiveTool();
            return;
        }

        ApplyToolSelection(tool);
    }

    private void ApplyToolSelection(string tool)
    {
        _activeTool = tool;
        if (tool is "point" or "line" or "area")
            _lastDrawingTool = tool;
        _viewport.SetTool(tool);
        foreach (var (t, btn) in _toolBtns)
            btn.IsChecked = t == tool;
        UpdateRecordButton();
        UpdateToolStatus();
    }

    private void SyncToolButtonsToActiveTool()
    {
        foreach (var (t, btn) in _toolBtns)
            btn.IsChecked = t == _activeTool;
        UpdateRecordButton();
        UpdateToolStatus();
    }

    private void UpdateRecordButton()
    {
        if (_recordButton == null)
            return;

        bool recording = _activeTool is "point" or "line" or "area";
        string recordType = recording ? MeasurementTypeTitle(_activeTool) : "";
        _updatingRecordButton = true;
        _recordButton.IsChecked = recording;
        _recordButton.Content = recording ? $"Rec {recordType}" : "Record";
        _recordButton.ToolTip = recording
            ? _activeItem == null
                ? $"Recording {recordType}; no active takeoff target is selected."
                : $"Recording {recordType} into {_activeItem.Name}. Click to stop."
            : "Start recording into the active takeoff target.";
        _recordButton.Background = recording
            ? new SolidColorBrush(Color.FromRgb(196, 32, 32))
            : (Brush)FindResource("ControlBackgroundBrush");
        _recordButton.Foreground = recording
            ? Brushes.White
            : (Brush)FindResource("ControlForegroundBrush");
        _recordButton.BorderBrush = recording
            ? new SolidColorBrush(Color.FromRgb(120, 0, 0))
            : (Brush)FindResource("ControlBorderBrush");
        _updatingRecordButton = false;
    }

    private void BtnSnap_Checked(object sender, RoutedEventArgs e) =>
        SetSnapMode(enabled: true);

    private void BtnSnap_Unchecked(object sender, RoutedEventArgs e) =>
        SetSnapMode(enabled: false);

    private void BtnOrtho_Checked(object sender, RoutedEventArgs e) =>
        SetOrthoMode(enabled: true);

    private void BtnOrtho_Unchecked(object sender, RoutedEventArgs e) =>
        SetOrthoMode(enabled: false);

    private void SetSnapMode(bool enabled)
    {
        if (_updatingConstraintButtons)
            return;

        _viewport.SnapEnabled = enabled;
        UpdateConstraintButtons();
        UpdateToolStatus();
    }

    private void SetOrthoMode(bool enabled)
    {
        if (_updatingConstraintButtons)
            return;

        _viewport.OrthoEnabled = enabled;
        UpdateConstraintButtons();
        UpdateToolStatus();
    }

    private void OnViewportSnapChanged(bool enabled)
    {
        UpdateConstraintButtons();
        UpdateToolStatus();
    }

    private void OnViewportOrthoChanged(bool enabled)
    {
        UpdateConstraintButtons();
        UpdateToolStatus();
    }

    private void UpdateConstraintButtons()
    {
        _updatingConstraintButtons = true;
        try
        {
            BtnSnap.IsChecked = _viewport.SnapEnabled;
            BtnSnap.Content = _viewport.SnapEnabled ? "Snap On" : "Snap";
            BtnOrtho.IsChecked = _viewport.OrthoEnabled;
            BtnOrtho.Content = _viewport.OrthoEnabled ? "Ortho On" : "Ortho";
        }
        finally
        {
            _updatingConstraintButtons = false;
        }
    }

    private bool EnsureDrawingTakeoff(string tool, bool forceNewTakeoff = false)
    {
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "Takeoff Item",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (_currentPage == null)
        {
            MessageBox.Show("Select a page before drawing measurements.", "Takeoff Item",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        string mtype = SmartTakeoffsJobStore.NormalizeMeasurementType(tool);
        if (mtype is "line" or "area" && _currentPage.ScaleMetersPerPt <= 0)
        {
            MessageBox.Show(
                "Set the page scale before drawing Line or Area measurements.",
                "Scale Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (!forceNewTakeoff &&
            _activeItem != null &&
            SmartTakeoffsJobStore.NormalizeMeasurementType(_activeItem.MeasurementType) == mtype)
        {
            _activeItem.MeasurementType = mtype;
            _viewport.ActiveColor = _activeItem.Color;
            _viewport.ActiveTakeoffFolder = _activeItem.FolderPath;
            return true;
        }

        if (!forceNewTakeoff && !ConfirmCreateDrawingTakeoffTarget(mtype))
            return false;

        string parentFolder = NewTakeoffItemParentFolder();
        string defaultColor = ResolveTakeoffFolderDefaultColor(
            parentFolder,
            _activeItem?.Color ?? _viewport.ActiveColor);
        var dlg = new NewItemDialog(
            mtype,
            DefaultTakeoffNameForFolder(mtype, parentFolder),
            lockType: true,
            defaultColor: defaultColor)
        {
            Owner = this,
        };
        if (dlg.ShowDialog() != true)
            return false;

        var newItem = CreateUniqueTakeoffItem(dlg.ItemName, dlg.ItemColor, mtype, parentFolder);
        ApplyTakeoffFolderDefaultsToNewItem(newItem, parentFolder);
        _takeoffItems.Add(newItem);
        var treeParent = FindTakeoffTreeItemByFolder(parentFolder) ?? (ItemsControl)TakeoffsTree;
        var tvi = AddTakeoffTreeItem(newItem, treeParent);
        if (treeParent is TreeViewItem parentTvi)
            parentTvi.IsExpanded = true;

        _activeItem = newItem;
        _activeTakeoffParentFolder = parentFolder;
        _viewport.ActiveColor = newItem.Color;
        _viewport.ActiveTakeoffFolder = newItem.FolderPath;
        tvi.IsSelected = true;
        UpdateToolStatus();
        RefreshActiveTakeoffVisuals();
        UpdateTotalDisplay();
        return true;
    }

    private bool ConfirmCreateDrawingTakeoffTarget(string measurementType)
    {
        string targetType = MeasurementTypeTitle(measurementType);
        string message = _activeItem == null
            ? $"No active takeoff target is selected.\n\nCreate a {targetType} takeoff item before recording?"
            : $"Active target is {_activeItem.Name} ({MeasurementTypeTitle(_activeItem.MeasurementType)}).\n\n{targetType} recording needs a {targetType} takeoff item. Create a separate target?";

        return MessageBox.Show(
            message,
            "Create Takeoff Target",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    private void BtnFit_Click(object sender, RoutedEventArgs e)    => _viewport.ZoomFit();
    private void BtnZoomIn_Click(object sender, RoutedEventArgs e)  => _viewport.ZoomIn();
    private void BtnZoomOut_Click(object sender, RoutedEventArgs e) => _viewport.ZoomOut();

    private void BtnSetScale_Click(object sender, RoutedEventArgs e) => ApplyScaleFromEntry();

    private void TxtScaleRatio_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return || e.Key == Key.Enter)
            ApplyScaleFromEntry();
    }

    private void ApplyScaleFromEntry()
    {
        const double PT_M = 25.4 / 72.0 / 1000.0;
        if (!double.TryParse(TxtScaleRatio.Text.Replace(",", "."),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double ratio)
            || ratio <= 0)
        {
            MessageBox.Show("Enter a valid number, e.g. 100 for 1:100",
                            "Scale", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _viewport.ScaleMetersPerPt = PT_M * ratio;
        if (_currentPage != null)
            _currentPage.ScaleMetersPerPt = _viewport.ScaleMetersPerPt;
        ApplyScaleToCurrentPageMeasurements(_viewport.ScaleMetersPerPt);
        SaveCurrentPageScale();
        UpdateScaleUi(_viewport.ScaleMetersPerPt);
        TxtStatus.Text = $"Scale set: 1:{ratio:F0}  (1pt = {_viewport.ScaleMetersPerPt:F6} m)";
        RefreshAllTotals();
    }

    private void BtnScalePresets_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };

        menu.Items.Add(new MenuItem { Header = "── Metric ──", IsEnabled = false });
        foreach (var (label, ratio) in MetricPresets)
            AddPresetItem(menu, label, ratio);

        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "── Imperial ──", IsEnabled = false });
        foreach (var (label, ratio) in ImperialPresets)
            AddPresetItem(menu, label, ratio);

        menu.IsOpen = true;
    }

    private void AddPresetItem(ContextMenu menu, string label, double ratio)
    {
        var mi = new MenuItem { Header = label };
        mi.Click += (_, _) =>
        {
            TxtScaleRatio.Text = $"{ratio:F0}";
            ApplyScaleFromEntry();
        };
        menu.Items.Add(mi);
    }

    // ── Pages tree ────────────────────────────────────────────────────────────

    private void BtnViewportBg_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
        AddViewportBgItem(menu, "White", "#FFFFFF");
        AddViewportBgItem(menu, "Light gray", "#F2F2F2");
        AddViewportBgItem(menu, "Warm paper", "#FFF8E8");
        AddViewportBgItem(menu, "Dark gray", "#2B2B2B");
        menu.IsOpen = true;
    }

    private void AddViewportBgItem(ContextMenu menu, string label, string color)
    {
        var mi = new MenuItem { Header = label, IsCheckable = true, IsChecked = _settings.ViewportBackground == color };
        mi.Click += (_, _) => ApplyViewportBackground(color, persist: true);
        menu.Items.Add(mi);
    }

    private void ComboFolderTemplateMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingSettings)
            return;

        string mode = ComboFolderTemplateMode.SelectedIndex switch
        {
            1 => "COM",
            2 => "EWP",
            _ => "AUTO",
        };
        _settings.FolderTemplateMode = mode;
        SaveAppSettings();
        TxtStatus.Text = $"Folder template mode: {mode}.";
    }

    private void BtnDarkTheme_Checked(object sender, RoutedEventArgs e) =>
        ApplyThemeFromToggle(dark: true);

    private void BtnDarkTheme_Unchecked(object sender, RoutedEventArgs e) =>
        ApplyThemeFromToggle(dark: false);

    private void ApplyThemeFromToggle(bool dark)
    {
        if (_isApplyingSettings)
            return;

        ApplyTheme(dark, persist: true);
    }

    private void ReloadPagesTree(string? selectPath = null)
    {
        PagesTree.Items.Clear();
        _pageTakeoffMultiSelection.Clear();
        _pageTakeoffRangeAnchorKey = null;
        if (_currentJob == null)
        {
            _expandedPageTreePaths.Clear();
            return;
        }

        var rootNode = new PageFolderNode
        {
            Name = "Pages",
            FolderPath = _currentJob.PagesRoot,
            IsRoot = true,
        };
        var rootItem = new TreeViewItem
        {
            Header = "📁 Pages",
            Tag = rootNode,
            IsExpanded = false,
        };
        PagesTree.Items.Add(rootItem);
        FillPagesTree(rootItem.Items, _currentJob.PagesRoot);
        RefreshPagesTakeoffIndicators();
        RestoreExpandedTreeState(PagesTree, _expandedPageTreePaths, GetPagesNodePath);

        if (!string.IsNullOrWhiteSpace(selectPath))
            SelectNodeByFolder(selectPath);
        else
            rootItem.IsSelected = true;
        PrunePagesMultiSelection();
        ApplyPagesMultiSelectionVisuals();
    }

    private void FillPagesTree(ItemCollection items, string folder)
    {
        if (!Directory.Exists(folder)) return;

        foreach (string dir in SmartTakeoffsJobStore.GetOrderedChildDirectories(folder))
        {
            PageInfo? page = SmartTakeoffsJobStore.TryReadPage(dir);
            if (page != null)
            {
                var pageItem = new TreeViewItem
                {
                    Header = BuildPageHeader(page),
                    Tag = page,
                    IsExpanded = false,
                };
                RebuildPageTakeoffNodes(pageItem, page);
                items.Add(pageItem);
                continue;
            }

            string name = SmartTakeoffsJobStore.ReadName(dir) ?? Path.GetFileName(dir);
            var folderNode = new PageFolderNode { Name = name, FolderPath = dir };
            var tvi = new TreeViewItem
            {
                Header = $"📁 {name}",
                Tag = folderNode,
                IsExpanded = false,
            };
            items.Add(tvi);
            FillPagesTree(tvi.Items, dir);
        }
    }

    private void BtnCollapsePagesTree_Click(object sender, RoutedEventArgs e) =>
        SetProjectTreeExpanded(PagesTree, false, "Pages tree collapsed.");

    private void BtnExpandPagesTree_Click(object sender, RoutedEventArgs e) =>
        SetProjectTreeExpanded(PagesTree, true, "Pages tree expanded.");

    private void BtnCollapseTakeoffsTree_Click(object sender, RoutedEventArgs e) =>
        SetProjectTreeExpanded(TakeoffsTree, false, "Takeoffs tree collapsed.");

    private void BtnExpandTakeoffsTree_Click(object sender, RoutedEventArgs e) =>
        SetProjectTreeExpanded(TakeoffsTree, true, "Takeoffs tree expanded.");

    private void SetProjectTreeExpanded(ItemsControl tree, bool isExpanded, string statusText)
    {
        SetTreeItemsExpanded(tree, isExpanded);
        if (ReferenceEquals(tree, PagesTree))
            CaptureExpandedTreeState(PagesTree, _expandedPageTreePaths, GetPagesNodePath);
        else if (ReferenceEquals(tree, TakeoffsTree))
            CaptureExpandedTreeState(TakeoffsTree, _expandedTakeoffTreePaths, GetTakeoffNodePath);
        TxtStatus.Text = statusText;
    }

    private void CollapseProjectTreeDisplays()
    {
        CollapseTreeAndExpansionState(PagesTree, _expandedPageTreePaths);
        CollapseTreeAndExpansionState(TakeoffsTree, _expandedTakeoffTreePaths);
    }

    private static void SetTreeItemsExpanded(ItemsControl parent, bool isExpanded)
    {
        foreach (TreeViewItem item in parent.Items.OfType<TreeViewItem>())
        {
            item.IsExpanded = isExpanded;
            SetTreeItemsExpanded(item, isExpanded);
        }
    }

    private void CollapseTreeAndExpansionState(ItemsControl tree, HashSet<string> expandedPaths)
    {
        SetTreeItemsExpanded(tree, false);
        expandedPaths.Clear();
    }

    private static void RebaseExpandedTreePaths(HashSet<string> expandedPaths, string oldPath, string newPath)
    {
        string? oldKey = ExpansionPathKey(oldPath);
        string? newKey = ExpansionPathKey(newPath);
        if (oldKey == null ||
            newKey == null ||
            string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase) ||
            expandedPaths.Count == 0)
        {
            return;
        }

        var rebased = new List<(string OldKey, string NewKey)>();
        foreach (string expandedPath in expandedPaths)
        {
            if (!SmartTakeoffsJobStore.IsSameOrDescendant(oldKey, expandedPath))
                continue;

            rebased.Add((expandedPath, ExpansionPathKey(RebaseDescendantPath(oldKey, newKey, expandedPath))!));
        }

        foreach (var (oldExpandedKey, newExpandedKey) in rebased)
        {
            expandedPaths.Remove(oldExpandedKey);
            expandedPaths.Add(newExpandedKey);
        }
    }

    private void RestoreExpandedTreeState(
        ItemsControl tree,
        HashSet<string> expandedPaths,
        Func<TreeViewItem, string?> getPath)
    {
        WithTreeExpansionTrackingSuppressed(() =>
            RestoreExpandedTreeStateCore(tree, expandedPaths, getPath));
    }

    private static void RestoreExpandedTreeStateCore(
        ItemsControl parent,
        HashSet<string> expandedPaths,
        Func<TreeViewItem, string?> getPath)
    {
        foreach (TreeViewItem item in parent.Items.OfType<TreeViewItem>())
        {
            string? key = ExpansionPathKey(getPath(item));
            if (key != null && expandedPaths.Contains(key))
                item.IsExpanded = true;

            RestoreExpandedTreeStateCore(item, expandedPaths, getPath);
        }
    }

    private static void CaptureExpandedTreeState(
        ItemsControl tree,
        HashSet<string> expandedPaths,
        Func<TreeViewItem, string?> getPath)
    {
        expandedPaths.Clear();
        CaptureExpandedTreeStateCore(tree, expandedPaths, getPath);
    }

    private static void CaptureExpandedTreeStateCore(
        ItemsControl parent,
        HashSet<string> expandedPaths,
        Func<TreeViewItem, string?> getPath)
    {
        foreach (TreeViewItem item in parent.Items.OfType<TreeViewItem>())
        {
            if (item.IsExpanded && ExpansionPathKey(getPath(item)) is { } key)
                expandedPaths.Add(key);

            CaptureExpandedTreeStateCore(item, expandedPaths, getPath);
        }
    }

    private void PagesTreeItem_Expanded(object sender, RoutedEventArgs e) =>
        TrackTreeExpansion(e, _expandedPageTreePaths, GetPagesNodePath, expanded: true);

    private void PagesTreeItem_Collapsed(object sender, RoutedEventArgs e) =>
        TrackTreeExpansion(e, _expandedPageTreePaths, GetPagesNodePath, expanded: false);

    private void TakeoffsTreeItem_Expanded(object sender, RoutedEventArgs e) =>
        TrackTreeExpansion(e, _expandedTakeoffTreePaths, GetTakeoffNodePath, expanded: true);

    private void TakeoffsTreeItem_Collapsed(object sender, RoutedEventArgs e) =>
        TrackTreeExpansion(e, _expandedTakeoffTreePaths, GetTakeoffNodePath, expanded: false);

    private void TrackTreeExpansion(
        RoutedEventArgs e,
        HashSet<string> expandedPaths,
        Func<TreeViewItem, string?> getPath,
        bool expanded)
    {
        if (_suppressTreeExpansionTracking || e.OriginalSource is not TreeViewItem item)
            return;

        string? key = ExpansionPathKey(getPath(item));
        if (key == null)
            return;

        if (expanded)
            expandedPaths.Add(key);
        else
            expandedPaths.Remove(key);
    }

    private void WithTreeExpansionTrackingSuppressed(Action action)
    {
        bool previous = _suppressTreeExpansionTracking;
        _suppressTreeExpansionTracking = true;
        try
        {
            action();
        }
        finally
        {
            _suppressTreeExpansionTracking = previous;
        }
    }

    private void ExpandTreeItemAndAncestorsWithoutTracking(TreeViewItem item)
    {
        WithTreeExpansionTrackingSuppressed(() =>
            ExpandTreeItemAndAncestors(item));
    }

    private static string? ExpansionPathKey(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : NormalizePathForCompare(path);

    private StackPanel BuildPageHeader(PageInfo page)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = $"  {page.Name}",
            VerticalAlignment = VerticalAlignment.Center,
        });

        if (page.ScaleMetersPerPt <= 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "  unscaled",
                Foreground = Brushes.Firebrick,
                FontSize = 10,
                FontWeight = FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        var takeoffs = TakeoffsForPage(page.FolderPath).ToList();
        if (takeoffs.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"  {takeoffs.Count} takeoff{(takeoffs.Count == 1 ? "" : "s")}",
                Foreground = Brushes.Gray,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
            });
            panel.ToolTip = string.Join(Environment.NewLine, takeoffs.Select(t => t.Name));
        }

        return panel;
    }

    private void RebuildPageTakeoffNodes(TreeViewItem pageItem, PageInfo page)
    {
        pageItem.Items.Clear();
        IReadOnlyList<TakeoffItem> orderedTakeoffs = OrderedTakeoffsForPage(page);
        for (int index = 0; index < orderedTakeoffs.Count; index++)
        {
            TakeoffItem takeoff = orderedTakeoffs[index];
            var node = new PageTakeoffNode(page, takeoff);
            var child = new TreeViewItem
            {
                Header = BuildPageTakeoffHeader(page, takeoff, index),
                Tag = node,
            };
            child.ContextMenu = BuildPageTakeoffContextMenu(node);
            pageItem.Items.Add(child);
        }
    }

    private FrameworkElement BuildPageTakeoffHeader(PageInfo page, TakeoffItem takeoff, int legendIndex)
    {
        bool isActive = IsActivePageTakeoff(page, takeoff);
        var secondaryBrush = (Brush)Application.Current.Resources["SecondaryForegroundBrush"]
            ?? new SolidColorBrush(Color.FromRgb(128, 128, 128));
        Brush swatchBrush = BrushFromHex(takeoff.Color, Brushes.Gray);

        var dock = new DockPanel { LastChildFill = true };

        var pageMeasurements = MeasurementsForTakeoffOnPage(takeoff, page.FolderPath).ToList();
        if (pageMeasurements.Count > 0)
        {
            var qty = new TextBlock
            {
                Text              = SheetLegendQuantityText(takeoff, pageMeasurements),
                Foreground        = secondaryBrush,
                FontSize          = 10,
                FontFamily        = new FontFamily("Consolas, Cascadia Mono, Segoe UI"),
                Margin            = new Thickness(8, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment     = TextAlignment.Right,
                MinWidth          = 56,
            };
            DockPanel.SetDock(qty, Dock.Right);
            dock.Children.Add(qty);
        }

        var indexText = new TextBlock
        {
            Text              = $"{legendIndex + 1}.",
            Width             = 22,
            TextAlignment     = TextAlignment.Right,
            Foreground        = secondaryBrush,
            FontSize          = 10,
            Margin            = new Thickness(2, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var swatchHost = BuildTakeoffSwatchGlyph(takeoff, swatchBrush, isActive ? 16 : 14);
        swatchHost.Margin = new Thickness(0, 0, 6, 0);

        var nameRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        nameRow.Children.Add(indexText);
        nameRow.Children.Add(swatchHost);
        nameRow.Children.Add(new TextBlock
        {
            Text              = takeoff.Name,
            FontWeight        = FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming      = TextTrimming.CharacterEllipsis,
        });
        dock.Children.Add(nameRow);

        dock.ToolTip =
            $"Legend position: {legendIndex + 1}" + Environment.NewLine +
            "Linked to the real Takeoffs item. Use Move Up/Down here only to change this sheet's legend order.";
        return dock;
    }

    private IEnumerable<TakeoffItem> TakeoffsForPage(string pageFolder) =>
        _takeoffItems.Where(item => item.Measurements.Any(m =>
            IsSamePageFolder(m.PageFolder, pageFolder)));

    private IEnumerable<Measurement> MeasurementsForTakeoffOnPage(TakeoffItem item, string pageFolder) =>
        item.Measurements.Where(measurement => IsSamePageFolder(measurement.PageFolder, pageFolder));

    private IReadOnlyList<TakeoffItem> OrderedTakeoffsForPage(PageInfo page)
    {
        var takeoffs = TakeoffsForPage(page.FolderPath).ToList();
        if (takeoffs.Count <= 1)
            return takeoffs;

        var byKey = takeoffs
            .GroupBy(TakeoffLegendOrderKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<TakeoffItem>();

        foreach (string storedKey in page.LegendTakeoffOrder.Select(NormalizeTakeoffLegendOrderKey))
        {
            if (string.IsNullOrWhiteSpace(storedKey) || !byKey.TryGetValue(storedKey, out TakeoffItem? takeoff))
                continue;
            if (!used.Add(storedKey))
                continue;

            ordered.Add(takeoff);
        }

        ordered.AddRange(takeoffs
            .Where(takeoff => !used.Contains(TakeoffLegendOrderKey(takeoff)))
            .OrderBy(takeoff => MeasurementTypeTitle(takeoff.MeasurementType), StringComparer.OrdinalIgnoreCase)
            .ThenBy(takeoff => takeoff.Name, StringComparer.OrdinalIgnoreCase));

        return ordered;
    }

    private string TakeoffLegendOrderKey(TakeoffItem item) =>
        NormalizeTakeoffLegendOrderKey(item.FolderPath);

    private string NormalizeTakeoffLegendOrderKey(string value)
    {
        string clean = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clean))
            return "";

        if (_currentJob != null && Path.IsPathFullyQualified(clean))
        {
            string full = NormalizePath(clean);
            if (SmartTakeoffsJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, full))
                clean = Path.GetRelativePath(_currentJob.TakeoffsRoot, full);
        }

        return clean.Replace('\\', '/').Trim('/');
    }

    private void SavePageLegendOrder(PageInfo page, IReadOnlyList<TakeoffItem> orderedTakeoffs)
    {
        var order = orderedTakeoffs
            .Select(TakeoffLegendOrderKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        page.LegendTakeoffOrder = order;
        if (_currentPage != null && IsSamePageFolder(_currentPage.FolderPath, page.FolderPath))
            _currentPage.LegendTakeoffOrder = order.ToList();
        SmartTakeoffsJobStore.SavePageLegendTakeoffOrder(page.FolderPath, order);
    }

    private void RebasePageLegendTakeoffOrderReferences(string oldPath, string newPath)
    {
        RebaseExpandedTreePaths(_expandedTakeoffTreePaths, oldPath, newPath);

        if (_currentJob == null)
            return;

        string oldKey = NormalizeTakeoffLegendOrderKey(oldPath);
        string newKey = NormalizeTakeoffLegendOrderKey(newPath);
        if (string.IsNullOrWhiteSpace(oldKey) ||
            string.IsNullOrWhiteSpace(newKey) ||
            string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (PageInfo page in CollectPagesUnder(_currentJob.PagesRoot))
        {
            if (page.LegendTakeoffOrder.Count == 0)
                continue;

            var updated = page.LegendTakeoffOrder
                .Select(key => RebaseTakeoffLegendOrderKey(key, oldKey, newKey))
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            bool changed = updated.Count != page.LegendTakeoffOrder.Count ||
                           updated.Where((key, index) => !string.Equals(
                               key,
                               NormalizeTakeoffLegendOrderKey(page.LegendTakeoffOrder[index]),
                               StringComparison.OrdinalIgnoreCase)).Any();
            if (!changed)
                continue;

            page.LegendTakeoffOrder = updated;
            SmartTakeoffsJobStore.SavePageLegendTakeoffOrder(page.FolderPath, updated);
            if (_currentPage != null && IsSamePageFolder(_currentPage.FolderPath, page.FolderPath))
                _currentPage.LegendTakeoffOrder = updated.ToList();
        }
    }

    private string RebaseTakeoffLegendOrderKey(string key, string oldPrefix, string newPrefix)
    {
        string clean = NormalizeTakeoffLegendOrderKey(key);
        if (string.Equals(clean, oldPrefix, StringComparison.OrdinalIgnoreCase))
            return newPrefix;

        string prefix = oldPrefix.TrimEnd('/') + "/";
        if (clean.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return newPrefix.TrimEnd('/') + clean[(prefix.Length - 1)..];

        return clean;
    }

    private void PageTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingPageTabs || !ReferenceEquals(e.OriginalSource, PageTabs))
            return;

        if (PageTabs.SelectedItem is TabItem { Tag: PageTabState tab })
            ActivatePageTab(tab);
    }

    private void OpenPageInActiveTab(PageInfo page)
    {
        if (FindPageTab(page.FolderPath) is { } existing)
        {
            ActivatePageTab(existing, page);
            return;
        }

        SaveCurrentPageScale();
        SaveActivePageTabViewState();

        PageTabState? tab = SelectedPageTab();
        if (tab == null)
        {
            tab = new PageTabState(page.FolderPath, page.Name);
            _pageTabs.Add(tab);
        }
        else
        {
            tab.PageFolder = page.FolderPath;
            tab.PageName = page.Name;
            tab.ViewState = null;
        }

        LoadPageFromTab(tab, page);
    }

    private void OpenPageInNewTab(PageInfo page)
    {
        if (FindPageTab(page.FolderPath) is { } existing &&
            !ReferenceEquals(existing, _activePageTab))
        {
            ActivatePageTab(existing, page);
            return;
        }

        var tab = new PageTabState(page.FolderPath, page.Name);
        _pageTabs.Add(tab);
        ActivatePageTab(tab, page);
    }

    private void ActivatePageTab(PageTabState tab, PageInfo? fallbackPage = null)
    {
        if (ReferenceEquals(tab, _activePageTab) &&
            _currentPage != null &&
            string.Equals(_currentPage.FolderPath, tab.PageFolder, StringComparison.OrdinalIgnoreCase))
        {
            RefreshPageTabs(tab);
            return;
        }

        SaveCurrentPageScale();
        SaveActivePageTabViewState();
        LoadPageFromTab(tab, fallbackPage);
    }

    private void LoadPageFromTab(PageTabState tab, PageInfo? fallbackPage = null)
    {
        PageInfo? page = fallbackPage != null &&
                         string.Equals(fallbackPage.FolderPath, tab.PageFolder, StringComparison.OrdinalIgnoreCase)
            ? fallbackPage
            : SmartTakeoffsJobStore.TryReadPage(tab.PageFolder);

        if (page == null)
        {
            _pageTabs.Remove(tab);
            if (ReferenceEquals(tab, _activePageTab))
                _activePageTab = null;
            RefreshPageTabs(_activePageTab);
            TxtStatus.Text = "Page tab closed because the page no longer exists.";
            return;
        }

        tab.PageName = page.Name;
        _activePageTab = tab;
        RefreshPageTabs(tab);
        LoadPageIntoViewport(page, tab.ViewState);
    }

    private void LoadPageIntoViewport(PageInfo page, PdfViewport.ViewState? restoreView)
    {
        _currentPage = page;
        _currentPdfPath = page.PdfPath;
        TxtStatusPage.Text = page.Name;
        _viewport.ScaleMetersPerPt = page.ScaleMetersPerPt;
        UpdateScaleUi(page.ScaleMetersPerPt);
        ApplyScaleToCurrentPageMeasurements(page.ScaleMetersPerPt);
        RefreshAllTotals();
        _viewport.LoadPage(
            page.PdfPath,
            page.PdfPage,
            page.FolderPath,
            page.PdfLayersCached ? page.PdfLayers : null,
            restoreView);
        _viewport.SetPageAnnotations(SmartTakeoffsJobStore.LoadPageAnnotations(page.FolderPath));
        RefreshAiMarkersOverlay();
        SelectPageTreeNodeSilently(page.FolderPath);
        _settings.LastPageFolder = page.FolderPath;
        if (_currentJob != null)
            _settings.LastJobPath = _currentJob.RootPath;
        SaveAppSettings();

        if (_takeoffItems.Count == 0)
            TryAutoLoad();
        ApplyTakeoffPageHighlights();
    }

    private void ClosePageTab(PageTabState tab)
    {
        int index = _pageTabs.IndexOf(tab);
        if (index < 0)
            return;

        bool wasActive = ReferenceEquals(tab, _activePageTab);
        if (wasActive)
            SaveCurrentPageScale();

        _pageTabs.RemoveAt(index);
        if (!wasActive)
        {
            RefreshPageTabs(_activePageTab);
            return;
        }

        _activePageTab = null;
        if (_pageTabs.Count > 0)
        {
            int nextIndex = Math.Min(index, _pageTabs.Count - 1);
            LoadPageFromTab(_pageTabs[nextIndex]);
            return;
        }

        RefreshPageTabs(null);
        _currentPage = null;
        _currentPdfPath = "";
        TxtStatusPage.Text = "—";
        UpdateScaleUi(0);
        _viewport.ClearPage();
        TxtStatus.Text = "Closed page tab.";
    }

    private void SaveActivePageTabViewState()
    {
        if (_activePageTab == null || _currentPage == null)
            return;

        if (!string.Equals(_activePageTab.PageFolder, _currentPage.FolderPath, StringComparison.OrdinalIgnoreCase))
            return;

        _activePageTab.ViewState = _viewport.CaptureViewState();
    }

    private PageTabState? SelectedPageTab() =>
        PageTabs.SelectedItem is TabItem { Tag: PageTabState tab } ? tab : _activePageTab;

    private PageTabState? FindPageTab(string pageFolder) =>
        _pageTabs.FirstOrDefault(tab =>
            string.Equals(tab.PageFolder, pageFolder, StringComparison.OrdinalIgnoreCase));

    private void RefreshPageTabs(PageTabState? selected)
    {
        _updatingPageTabs = true;
        try
        {
            PageTabs.Items.Clear();
            TabItem? selectedItem = null;
            foreach (PageTabState tab in _pageTabs)
            {
                var item = new TabItem
                {
                    Header = BuildPageTabHeader(tab),
                    Tag = tab,
                    ToolTip = tab.PageFolder,
                };
                item.SetResourceReference(Control.ForegroundProperty, "ControlForegroundBrush");
                item.SetResourceReference(Control.BackgroundProperty, "ControlBackgroundBrush");
                item.SetResourceReference(Control.BorderBrushProperty, "ControlBorderBrush");
                PageTabs.Items.Add(item);
                if (ReferenceEquals(tab, selected))
                    selectedItem = item;
            }

            PageTabs.Visibility = _pageTabs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            PageTabs.SelectedItem = selectedItem;
        }
        finally
        {
            _updatingPageTabs = false;
        }
    }

    private StackPanel BuildPageTabHeader(PageTabState tab)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = tab.PageName,
            MaxWidth = 160,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var close = new Button
        {
            Content = "x",
            Width = 18,
            Height = 18,
            Padding = new Thickness(0),
            Margin = new Thickness(6, 0, 0, 0),
            ToolTip = "Close tab",
        };
        close.Click += (_, e) =>
        {
            e.Handled = true;
            ClosePageTab(tab);
        };
        panel.Children.Add(close);
        return panel;
    }

    private void SelectPageTreeNodeSilently(string pageFolder)
    {
        _syncingPageTreeSelection = true;
        try
        {
            SelectNodeByFolder(pageFolder);
        }
        finally
        {
            _syncingPageTreeSelection = false;
        }
    }

    private void RemovePageTabsForAffectedPath(string affectedPath)
    {
        bool changed = false;
        for (int i = _pageTabs.Count - 1; i >= 0; i--)
        {
            PageTabState tab = _pageTabs[i];
            if (!SmartTakeoffsJobStore.IsSameOrDescendant(affectedPath, tab.PageFolder))
                continue;

            if (ReferenceEquals(tab, _activePageTab))
                _activePageTab = null;
            _pageTabs.RemoveAt(i);
            changed = true;
        }

        if (changed)
            RefreshPageTabs(_activePageTab);
    }

    private bool UpdatePageReferencesForMovedPath(string oldPath, string newPath)
    {
        string oldFull = NormalizePath(oldPath);
        string newFull = NormalizePath(newPath);
        RebaseExpandedTreePaths(_expandedPageTreePaths, oldFull, newFull);
        bool activeAffected = _currentPage != null &&
                              SmartTakeoffsJobStore.IsSameOrDescendant(oldFull, _currentPage.FolderPath);
        bool tabsChanged = false;
        bool measurementsChanged = RebaseMeasurementPageFolderReferences(oldFull, newFull);

        foreach (PageTabState tab in _pageTabs)
        {
            if (!SmartTakeoffsJobStore.IsSameOrDescendant(oldFull, tab.PageFolder))
                continue;

            tab.PageFolder = RebaseDescendantPath(oldFull, newFull, tab.PageFolder);
            if (SmartTakeoffsJobStore.TryReadPage(tab.PageFolder) is { } page)
                tab.PageName = page.Name;
            tabsChanged = true;
        }

        if (!string.IsNullOrWhiteSpace(_settings.LastPageFolder) &&
            SmartTakeoffsJobStore.IsSameOrDescendant(oldFull, _settings.LastPageFolder))
        {
            _settings.LastPageFolder = RebaseDescendantPath(oldFull, newFull, _settings.LastPageFolder);
            SaveAppSettings();
        }

        if (activeAffected)
        {
            _currentPage = null;
            _currentPdfPath = "";
        }

        if (tabsChanged)
            RefreshPageTabs(_activePageTab);
        if (measurementsChanged)
        {
            _viewport.SetMeasurements(_takeoffItems.SelectMany(item => item.Measurements));
            RefreshPagesTakeoffIndicators();
            RefreshEstimateTable();
        }

        return activeAffected;
    }

    private bool RebaseMeasurementPageFolderReferences(string oldFull, string newFull)
    {
        if (_currentJob == null)
            return false;

        bool changed = false;
        foreach (TakeoffItem item in _takeoffItems)
        {
            bool itemChanged = false;
            foreach (Measurement measurement in item.Measurements)
            {
                if (string.IsNullOrWhiteSpace(measurement.PageFolder))
                    continue;

                string current = NormalizePageReferencePath(measurement.PageFolder);
                if (!SmartTakeoffsJobStore.IsSameOrDescendant(oldFull, current))
                    continue;

                measurement.PageFolder = RebaseDescendantPath(oldFull, newFull, current);
                changed = true;
                itemChanged = true;
            }

            if (itemChanged)
                SmartTakeoffsJobStore.SaveTakeoffItem(item);
        }

        return changed;
    }

    private void ReloadActivePageTabAfterPathChange(bool shouldReload)
    {
        if (!shouldReload || _activePageTab == null)
            return;

        if (Directory.Exists(_activePageTab.PageFolder))
        {
            LoadPageFromTab(_activePageTab);
            return;
        }

        _activePageTab = null;
        RefreshPageTabs(null);
        _viewport.ClearPage();
        TxtStatusPage.Text = "—";
    }

    private static string RebaseDescendantPath(string oldRoot, string newRoot, string path)
    {
        string relative = Path.GetRelativePath(oldRoot, NormalizePath(path));
        return relative == "."
            ? newRoot
            : Path.GetFullPath(Path.Combine(newRoot, relative));
    }

    private string NormalizePageReferencePath(string path)
    {
        if (_currentJob != null && !Path.IsPathFullyQualified(path))
            path = Path.Combine(_currentJob.RootPath, path);

        return NormalizePath(path);
    }

    private bool IsPageMeasuredByActiveTakeoff(TreeViewItem item) =>
        _activeItem != null &&
        item.Tag is PageInfo page &&
        _activeItem.Measurements.Any(m =>
            IsSamePageFolder(m.PageFolder, page.FolderPath));

    private bool IsActivePageTakeoffNode(TreeViewItem item) =>
        item.Tag is PageTakeoffNode node &&
        IsActivePageTakeoff(node.Page, node.Takeoff);

    private bool IsActivePageTakeoff(PageInfo page, TakeoffItem takeoff) =>
        _activeItem != null &&
        string.Equals(_activeItem.FolderPath, takeoff.FolderPath, StringComparison.OrdinalIgnoreCase) &&
        takeoff.Measurements.Any(measurement => IsSamePageFolder(measurement.PageFolder, page.FolderPath));

    private void RefreshPagesTakeoffIndicators()
    {
        foreach (TreeViewItem item in EnumeratePageTreeItems())
        {
            if (item.Tag is PageInfo page)
            {
                bool wasExpanded = item.IsExpanded;
                item.Header = BuildPageHeader(page);
                RebuildPageTakeoffNodes(item, page);
                item.IsExpanded = wasExpanded;
            }
        }
        ApplyPagesMultiSelectionVisuals();
    }

    private void PagesTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_syncingPageTreeSelection)
            return;

        if (e.NewValue is TreeViewItem { Tag: PageInfo page })
        {
            OpenPageInActiveTab(page);
        }
        else if (e.NewValue is TreeViewItem { Tag: PageTakeoffNode node })
        {
            SelectLinkedPageTakeoff(node);
        }
    }

    private string GetSelectedImportFolder()
    {
        if (_currentJob == null)
            throw new InvalidOperationException("No job is open.");

        if (PagesTree.SelectedItem is TreeViewItem tvi)
        {
            if (tvi.Tag is PageFolderNode folder)
                return folder.FolderPath;
            if (tvi.Tag is PageInfo page)
                return Path.GetDirectoryName(page.FolderPath) ?? _currentJob.PagesRoot;
        }

        return SmartTakeoffsJobStore.DefaultImportFolder(_currentJob);
    }

    private void SelectNodeByFolder(string folderPath)
    {
        WithTreeExpansionTrackingSuppressed(() =>
        {
            foreach (TreeViewItem item in PagesTree.Items)
            {
                if (SelectNodeByFolder(item, folderPath))
                    return;
            }
        });
    }

    private static bool SelectNodeByFolder(TreeViewItem item, string folderPath)
    {
        string? itemPath = GetPagesNodePath(item);
        if (itemPath != null &&
            IsSamePageFolder(itemPath, folderPath))
        {
            item.IsSelected = true;
            item.BringIntoView();
            return true;
        }

        foreach (TreeViewItem child in item.Items)
        {
            if (SelectNodeByFolder(child, folderPath))
            {
                item.IsExpanded = true;
                return true;
            }
        }

        return false;
    }

    private void SelectPageByFolder(string folderPath)
    {
        WithTreeExpansionTrackingSuppressed(() =>
        {
            foreach (TreeViewItem item in PagesTree.Items)
            {
                if (SelectPageByFolder(item, folderPath))
                    return;
            }
        });
    }

    private static bool SelectPageByFolder(TreeViewItem item, string folderPath)
    {
        if (item.Tag is PageInfo page &&
            IsSamePageFolder(page.FolderPath, folderPath))
        {
            item.IsSelected = true;
            item.BringIntoView();
            return true;
        }

        foreach (TreeViewItem child in item.Items)
        {
            if (SelectPageByFolder(child, folderPath))
            {
                item.IsExpanded = true;
                return true;
            }
        }

        return false;
    }

    private void PagesTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is not { } item)
            return;

        if (item.Tag is PageTakeoffNode pageTakeoff)
        {
            string key = PageTakeoffSelectionKey(pageTakeoff);
            if (!_pageTakeoffMultiSelection.Contains(key))
            {
                _pageTakeoffMultiSelection.Clear();
                _pageTakeoffMultiSelection.Add(key);
                _pageTakeoffRangeAnchorKey = key;
                _pagesMultiSelection.Clear();
                ApplyPagesMultiSelectionVisuals();
            }

            item.Focus();
            item.IsSelected = true;
            item.ContextMenu = BuildPageTakeoffContextMenu(pageTakeoff);
            e.Handled = true;
            return;
        }

        string? path = GetPagesNodePath(item);
        if (path == null)
        {
            _pagesMultiSelection.Clear();
            _pageTakeoffMultiSelection.Clear();
            ApplyPagesMultiSelectionVisuals();
        }
        else if (!_pagesMultiSelection.Contains(path))
        {
            _pagesMultiSelection.Clear();
            _pageTakeoffMultiSelection.Clear();
            if (!IsRootPagesNode(item))
                _pagesMultiSelection.Add(path);
            _pagesRangeAnchorPath = path;
            ApplyPagesMultiSelectionVisuals();
        }

        item.Focus();
        item.IsSelected = true;
        item.ContextMenu = BuildPagesContextMenu(item);
    }

    private void PagesTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pagesDragStart = e.GetPosition(PagesTree);
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is not { } item)
            return;

        if (item.Tag is PageTakeoffNode pageTakeoff)
        {
            HandlePageTakeoffNodeMultiSelect(item, pageTakeoff, e);
            return;
        }

        string? path = GetPagesNodePath(item);
        if (path == null)
        {
            _pagesMultiSelection.Clear();
            _pageTakeoffMultiSelection.Clear();
            ApplyPagesMultiSelectionVisuals();
            return;
        }

        ModifierKeys modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None &&
            _pagesMultiSelection.Count > 1 &&
            _pagesMultiSelection.Contains(path))
        {
            item.IsSelected = true;
            ApplyPagesMultiSelectionVisuals();
            return;
        }

        if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift && !IsRootPagesNode(item))
        {
            bool additive = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            SelectPagesRange(_pagesRangeAnchorPath, path, additive);
            _pagesRangeAnchorPath = path;
            _pageTakeoffMultiSelection.Clear();
            item.IsSelected = true;
            ApplyPagesMultiSelectionVisuals();
            e.Handled = true;
            return;
        }

        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control && !IsRootPagesNode(item))
        {
            if (!_pagesMultiSelection.Add(path))
                _pagesMultiSelection.Remove(path);
            _pagesRangeAnchorPath = path;
            _pageTakeoffMultiSelection.Clear();
            item.IsSelected = true;
            ApplyPagesMultiSelectionVisuals();
            e.Handled = true;
            return;
        }

        _pagesMultiSelection.Clear();
        if (!IsRootPagesNode(item))
            _pagesMultiSelection.Add(path);
        _pagesRangeAnchorPath = path;
        _pageTakeoffMultiSelection.Clear();
        ApplyPagesMultiSelectionVisuals();
    }

    private void HandlePageTakeoffNodeMultiSelect(TreeViewItem item, PageTakeoffNode node, MouseButtonEventArgs e)
    {
        string key = PageTakeoffSelectionKey(node);
        ModifierKeys modifiers = Keyboard.Modifiers;
        _pagesMultiSelection.Clear();

        if (modifiers == ModifierKeys.None &&
            _pageTakeoffMultiSelection.Count > 1 &&
            _pageTakeoffMultiSelection.Contains(key))
        {
            item.IsSelected = true;
            ApplyPagesMultiSelectionVisuals();
            return;
        }

        if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            bool additive = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            SelectPageTakeoffRange(_pageTakeoffRangeAnchorKey, key, node.Page.FolderPath, additive);
            _pageTakeoffRangeAnchorKey = key;
            item.IsSelected = true;
            ApplyPagesMultiSelectionVisuals();
            Dispatcher.InvokeAsync(() => SelectSelectedPageTakeoffMeasurementsOnCanvas(node));
            e.Handled = true;
            return;
        }

        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (!_pageTakeoffMultiSelection.Add(key))
                _pageTakeoffMultiSelection.Remove(key);
            _pageTakeoffRangeAnchorKey = key;
            item.IsSelected = true;
            ApplyPagesMultiSelectionVisuals();
            Dispatcher.InvokeAsync(() => SelectSelectedPageTakeoffMeasurementsOnCanvas(node));
            e.Handled = true;
            return;
        }

        _pageTakeoffMultiSelection.Clear();
        _pageTakeoffMultiSelection.Add(key);
        _pageTakeoffRangeAnchorKey = key;
        ApplyPagesMultiSelectionVisuals();
    }

    private void PagesTree_MouseMove(object sender, MouseEventArgs e)
    {
        if (_pagesDragStart == null || e.LeftButton != MouseButtonState.Pressed)
            return;
        if (PagesTree.SelectedItem is not TreeViewItem item)
            return;
        if (IsRootPagesNode(item))
            return;

        Point pos = e.GetPosition(PagesTree);
        if (Math.Abs(pos.X - _pagesDragStart.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _pagesDragStart.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (item.Tag is PageTakeoffNode pageTakeoff)
        {
            var takeoffFolders = SelectedPageTakeoffNodes(pageTakeoff, fallbackToAnchor: true)
                .Select(node => node.Takeoff.FolderPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (takeoffFolders.Count == 0)
                return;

            var legendPayload = new PageTakeoffLegendDrag(pageTakeoff.Page.FolderPath, takeoffFolders);
            DragDrop.DoDragDrop(PagesTree, legendPayload, DragDropEffects.Move);
            _pagesDragStart = null;
            return;
        }

        var entries = GetSelectedPageEntries(item);
        if (entries.Count == 0)
            return;

        var payload = new PagesClipboard(entries, PagesClipboardMode.Cut);
        DragDrop.DoDragDrop(PagesTree, payload, DragDropEffects.Move | DragDropEffects.Copy);
        _pagesDragStart = null;
    }

    private void PagesTree_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        if (e.Data.GetData(typeof(PageTakeoffLegendDrag)) is PageTakeoffLegendDrag legendDrag)
        {
            TreeViewItem? legendTargetItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
            if (CanDropPageTakeoffLegend(legendDrag, legendTargetItem))
            {
                e.Effects = DragDropEffects.Move;
                UpdatePageTakeoffLegendDropCue(legendDrag, legendTargetItem!, e.GetPosition(legendTargetItem));
            }
            else
            {
                ClearPageTakeoffLegendDropCue();
            }
            e.Handled = true;
            return;
        }

        ClearPageTakeoffLegendDropCue();
        if (e.Data.GetData(typeof(PagesClipboard)) is not PagesClipboard payload)
        {
            e.Handled = true;
            return;
        }

        TreeViewItem? targetItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        string? targetFolder = targetItem == null ? _currentJob?.PagesRoot : GetPasteTargetFolder(targetItem);
        bool copy = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;

        if (CanDropInto(payload, targetFolder, copy ? PagesClipboardMode.Copy : PagesClipboardMode.Cut))
            e.Effects = copy ? DragDropEffects.Copy : DragDropEffects.Move;
        e.Handled = true;
    }

    private void PagesTree_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(PageTakeoffLegendDrag)) is PageTakeoffLegendDrag legendDrag)
        {
            TreeViewItem? legendTargetItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
            if (legendTargetItem != null)
                DropPageTakeoffLegend(legendDrag, legendTargetItem, e.GetPosition(legendTargetItem));
            ClearPageTakeoffLegendDropCue();
            e.Handled = true;
            return;
        }

        if (e.Data.GetData(typeof(PagesClipboard)) is not PagesClipboard payload)
            return;

        TreeViewItem? targetItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        string? targetFolder = targetItem == null ? _currentJob?.PagesRoot : GetPasteTargetFolder(targetItem);
        PagesClipboardMode mode = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey
            ? PagesClipboardMode.Copy
            : PagesClipboardMode.Cut;
        if (!CanDropInto(payload, targetFolder, mode))
            return;

        RunDrop(payload, targetFolder!, mode);
        e.Handled = true;
    }

    private void PagesTree_DragLeave(object sender, DragEventArgs e)
    {
        if (!PagesTree.IsMouseOver)
            ClearPageTakeoffLegendDropCue();
    }

    private void PagesTree_KeyDown(object sender, KeyEventArgs e)
    {
        if (PagesTree.SelectedItem is not TreeViewItem item) return;

        if (item.Tag is PageTakeoffNode node)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Up)
            {
                MovePageTakeoffLegendNodes(node, -1);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Down)
            {
                MovePageTakeoffLegendNodes(node, 1);
                e.Handled = true;
            }
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
        {
            CopyCutPagesNode(item, PagesClipboardMode.Copy);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.X)
        {
            CopyCutPagesNode(item, PagesClipboardMode.Cut);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V)
        {
            PasteIntoSelectedTarget(item);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.D)
        {
            DuplicatePageNode(item);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Up)
        {
            MovePagesNodes(item, -1);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Down)
        {
            MovePagesNodes(item, 1);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Delete)
        {
            DeletePagesNode(item);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.F2)
        {
            RenamePagesNode(item);
            e.Handled = true;
        }
    }

    private ContextMenu BuildPagesContextMenu(TreeViewItem item)
    {
        var menu = new ContextMenu();

        if (item.Tag is PageFolderNode folder)
        {
            int selectedCount = PageSelectionCount(item);
            bool isRoot = folder.IsRoot;
            bool canPaste = CanPasteInto(folder.FolderPath);
            bool hasChildren = Directory.Exists(folder.FolderPath) &&
                               Directory.EnumerateDirectories(folder.FolderPath).Any();

            menu.Items.Add(MakeMenuItem("New Folder", true, () => NewPageFolder(item)));
            menu.Items.Add(MakeMenuItem("Rename Folder", !isRoot && selectedCount <= 1, () => RenamePagesNode(item)));
            menu.Items.Add(MakeMenuItem(selectedCount > 1 ? "Delete Selected" : "Delete Folder", !isRoot || selectedCount > 1, () => DeletePagesNode(item)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeSubmenu(
                "Clipboard",
                MakeMenuItem(selectedCount > 1 ? "Copy Selected" : "Copy Folder", !isRoot || selectedCount > 1, () => CopyCutPagesNode(item, PagesClipboardMode.Copy)),
                MakeMenuItem(selectedCount > 1 ? "Cut Selected" : "Cut Folder", !isRoot || selectedCount > 1, () => CopyCutPagesNode(item, PagesClipboardMode.Cut)),
                MakeMenuItem("Paste Into Folder", canPaste, () => PasteIntoSelectedTarget(item))));
            menu.Items.Add(MakeSubmenu(
                "Organize",
                MakeMenuItem("Auto Create Folders", true, () => AutoCreatePageFolders(folder.FolderPath)),
                MakeMenuItem(selectedCount > 1 ? $"Move {selectedCount} Up" : "Move Up", CanMovePagesNodes(item, -1), () => MovePagesNodes(item, -1)),
                MakeMenuItem(selectedCount > 1 ? $"Move {selectedCount} Down" : "Move Down", CanMovePagesNodes(item, 1), () => MovePagesNodes(item, 1)),
                MakeMenuItem("Sort Children A-Z", hasChildren, () => SortFolderChildren(item, descending: false)),
                MakeMenuItem("Sort Children Z-A", hasChildren, () => SortFolderChildren(item, descending: true)),
                MakeMenuItem("Sort A/S into Arch/Struct", true, SortPagesIntoArchStruct),
                MakeMenuItem("Sort D/Sec/WT by Suffix", true, SortPagesBySuffix),
                MakeMenuItem("Repair Measurement Links", true, RepairMeasurementPageLinks)));
            menu.Items.Add(MakeSubmenu(
                "PDF Metadata",
                MakeMenuItem("Analyze PDF Metadata", true, async () => await AnalyzePdfMetadataAsync(item, applyRename: false, applyScale: false)),
                MakeMenuItem("Auto Rename from PDF...", true, async () => await AnalyzePdfMetadataAsync(item, applyRename: true, applyScale: false)),
                MakeMenuItem("Auto Scale from PDF...", true, async () => await AnalyzePdfMetadataAsync(item, applyRename: false, applyScale: true)),
                MakeMenuItem("Auto Rename + Scale from PDF...", true, async () => await AnalyzePdfMetadataAsync(item, applyRename: true, applyScale: true)),
                MakeMenuItem("Queue GPT Metadata Fallback", true, () => QueuePdfMetadataFallback(item))));
            menu.Items.Add(MakeSubmenu(
                "Learning",
                MakeMenuItem("Capture Final Learning Snapshot", true, () => CaptureFinalLearningSnapshot(item)),
                MakeMenuItem("Review Project Learned Rules...", true, ReviewProjectLearnedRules),
                MakeMenuItem("Review Global Learned Rules...", true, ReviewLearnedRules)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Open in Explorer", true, () => OpenFolderInExplorer(folder.FolderPath)));
        }
        else if (item.Tag is PageInfo page)
        {
            int selectedCount = PageSelectionCount(item);
            string parent = Path.GetDirectoryName(page.FolderPath) ?? "";
            menu.Items.Add(MakeMenuItem("Open in New Tab", true, () => OpenPageInNewTab(page)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Rename Page", selectedCount <= 1, () => RenamePagesNode(item)));
            menu.Items.Add(MakeMenuItem(selectedCount > 1 ? "Delete Selected" : "Delete Page", true, () => DeletePagesNode(item)));
            menu.Items.Add(MakeMenuItem("Duplicate Page", selectedCount <= 1, () => DuplicatePageNode(item)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeSubmenu(
                "Clipboard",
                MakeMenuItem(selectedCount > 1 ? "Copy Selected" : "Copy Page", true, () => CopyCutPagesNode(item, PagesClipboardMode.Copy)),
                MakeMenuItem(selectedCount > 1 ? "Cut Selected" : "Cut Page", true, () => CopyCutPagesNode(item, PagesClipboardMode.Cut)),
                MakeMenuItem("Paste Into Parent Folder", CanPasteInto(parent), () => PasteIntoSelectedTarget(item))));
            menu.Items.Add(MakeSubmenu(
                "Organize",
                MakeMenuItem(selectedCount > 1 ? $"Move {selectedCount} Up" : "Move Up", CanMovePagesNodes(item, -1), () => MovePagesNodes(item, -1)),
                MakeMenuItem(selectedCount > 1 ? $"Move {selectedCount} Down" : "Move Down", CanMovePagesNodes(item, 1), () => MovePagesNodes(item, 1)),
                MakeMenuItem("Move to Folder...", selectedCount <= 1, () => MovePageToFolder(item)),
                MakeMenuItem("Sort Sheet Legend A-Z", CanSortPageLegend(page), () => SortPageLegendByName(page)),
                MakeMenuItem("Reset Sheet Legend Order", HasCustomPageLegendOrder(page), () => ResetPageLegendOrder(page)),
                MakeMenuItem("Sort A/S into Arch/Struct", true, SortPagesIntoArchStruct),
                MakeMenuItem("Sort D/Sec/WT by Suffix", true, SortPagesBySuffix),
                MakeMenuItem("Repair Measurement Links", true, RepairMeasurementPageLinks)));
            menu.Items.Add(MakeSubmenu(
                "PDF Metadata",
                MakeMenuItem("Analyze PDF Metadata", true, async () => await AnalyzePdfMetadataAsync(item, applyRename: false, applyScale: false)),
                MakeMenuItem("Auto Rename from PDF...", true, async () => await AnalyzePdfMetadataAsync(item, applyRename: true, applyScale: false)),
                MakeMenuItem("Auto Scale from PDF...", true, async () => await AnalyzePdfMetadataAsync(item, applyRename: false, applyScale: true)),
                MakeMenuItem("Auto Rename + Scale from PDF...", true, async () => await AnalyzePdfMetadataAsync(item, applyRename: true, applyScale: true)),
                MakeMenuItem("Queue GPT Metadata Fallback", true, () => QueuePdfMetadataFallback(item)),
                MakeMenuItem("Open source_pdf.json", File.Exists(SmartTakeoffsJobStore.SourcePdfMetadataPath(page.FolderPath)), () => OpenSourcePdfMetadata(page.FolderPath))));
            menu.Items.Add(MakeSubmenu(
                "Learning",
                MakeMenuItem("Capture Final Learning Snapshot", true, () => CaptureFinalLearningSnapshot(item)),
                MakeMenuItem("Review Project Learned Rules...", true, ReviewProjectLearnedRules),
                MakeMenuItem("Review Global Learned Rules...", true, ReviewLearnedRules)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Open Page Folder in Explorer", true, () => OpenFolderInExplorer(page.FolderPath)));
        }
        else if (item.Tag is PageTakeoffNode node)
        {
            menu = BuildPageTakeoffContextMenu(node);
        }

        return menu;
    }

    private ContextMenu BuildPageTakeoffContextMenu(PageTakeoffNode node)
    {
        var menu = new ContextMenu();
        int selectedCount = SelectedPageTakeoffContextCount(node);
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Select {selectedCount} Linked Takeoffs" : "Select Linked Takeoff",
            true,
            () => SelectLinkedPageTakeoff(node)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Move {selectedCount} Up in Legend" : "Move Up in Legend",
            CanMovePageTakeoffLegendNodes(node, -1),
            () => MovePageTakeoffLegendNodes(node, -1)));
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Move {selectedCount} Down in Legend" : "Move Down in Legend",
            CanMovePageTakeoffLegendNodes(node, 1),
            () => MovePageTakeoffLegendNodes(node, 1)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Sort Sheet Legend A-Z", CanSortPageLegend(node.Page), () => SortPageLegendByName(node.Page, node.Takeoff.FolderPath)));
        menu.Items.Add(MakeMenuItem("Reset Sheet Legend Order", HasCustomPageLegendOrder(node.Page), () => ResetPageLegendOrder(node.Page, node.Takeoff.FolderPath)));
        return menu;
    }

    private int SelectedPageTakeoffContextCount(PageTakeoffNode anchor) =>
        SelectedPageTakeoffNodes(anchor, fallbackToAnchor: true).Count;

    private void SelectLinkedPageTakeoff(PageTakeoffNode node)
    {
        if (_currentPage == null || !IsSamePageFolder(_currentPage.FolderPath, node.Page.FolderPath))
            OpenPageInActiveTab(node.Page);

        var selectedNodes = SelectedPageTakeoffNodes(node, fallbackToAnchor: true);
        SelectTakeoffItem(node.Takeoff);
        SelectPageTakeoffNodeSilently(node.Page.FolderPath, node.Takeoff.FolderPath);
        Dispatcher.InvokeAsync(() => SelectPageTakeoffMeasurementsOnCanvas(selectedNodes, node.Page));
        if (selectedNodes.Count <= 1)
            TxtStatus.Text = $"Linked takeoff selected for {node.Page.Name}: {node.Takeoff.Name}.";
    }

    private void SelectPageTakeoffMeasurementsOnCanvas(PageTakeoffNode node)
    {
        if (_currentPage == null || !IsSamePageFolder(_currentPage.FolderPath, node.Page.FolderPath))
            return;

        SelectTakeoffMeasurementsOnCanvas(node.Takeoff, node.Page.FolderPath, node.Page.Name);
    }

    private void SelectSelectedPageTakeoffMeasurementsOnCanvas(PageTakeoffNode anchor)
    {
        if (_currentPage == null || !IsSamePageFolder(_currentPage.FolderPath, anchor.Page.FolderPath))
            OpenPageInActiveTab(anchor.Page);

        var selectedNodes = SelectedPageTakeoffNodes(anchor, fallbackToAnchor: false);
        SelectPageTakeoffMeasurementsOnCanvas(selectedNodes, anchor.Page);
    }

    private List<PageTakeoffNode> SelectedPageTakeoffNodes(PageTakeoffNode anchor, bool fallbackToAnchor)
    {
        string anchorKey = PageTakeoffSelectionKey(anchor);
        IEnumerable<string> keys = _pageTakeoffMultiSelection.Contains(anchorKey)
            ? _pageTakeoffMultiSelection
            : fallbackToAnchor
                ? [anchorKey]
                : Enumerable.Empty<string>();

        var keySet = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        if (keySet.Count == 0)
            return [];

        return EnumeratePageTreeItems()
            .Select(item => item.Tag as PageTakeoffNode)
            .Where(node => node != null &&
                           IsSamePageFolder(node.Page.FolderPath, anchor.Page.FolderPath) &&
                           keySet.Contains(PageTakeoffSelectionKey(node)))
            .Select(node => node!)
            .ToList();
    }

    private void SelectPageTakeoffMeasurementsOnCanvas(IReadOnlyList<PageTakeoffNode> nodes, PageInfo page)
    {
        if (_currentPage == null || !IsSamePageFolder(_currentPage.FolderPath, page.FolderPath))
            return;

        var measurements = nodes
            .SelectMany(node => MeasurementsForTakeoffOnPage(node.Takeoff, page.FolderPath))
            .Distinct()
            .ToList();

        _syncingViewportSelectionFromTakeoffItem = true;
        try
        {
            _viewport.SelectMeasurements(measurements);
        }
        finally
        {
            _syncingViewportSelectionFromTakeoffItem = false;
        }

        if (measurements.Count == 0)
            TxtStatus.Text = $"No selected takeoff measurements on {page.Name}.";
        else if (nodes.Count <= 1)
            TxtStatus.Text = measurements.Count == 1
                ? $"Selected {nodes[0].Takeoff.Name} measurement on {page.Name}."
                : $"Selected {measurements.Count} {nodes[0].Takeoff.Name} measurements on {page.Name}.";
        else
            TxtStatus.Text = $"Selected {measurements.Count} measurements from {nodes.Count} linked takeoffs on {page.Name}.";
    }

    private void SelectTakeoffMeasurementsOnCanvas(TakeoffItem item, string pageFolder, string pageName)
    {
        var pageMeasurements = MeasurementsForTakeoffOnPage(item, pageFolder).ToList();
        if (pageMeasurements.Count == 0)
            return;

        _syncingViewportSelectionFromTakeoffItem = true;
        try
        {
            _viewport.SelectMeasurements(pageMeasurements);
        }
        finally
        {
            _syncingViewportSelectionFromTakeoffItem = false;
        }

        TxtStatus.Text = pageMeasurements.Count == 1
            ? $"Selected {item.Name} measurement on {pageName}."
            : $"Selected {pageMeasurements.Count} {item.Name} measurements on {pageName}.";
    }

    private void SelectTakeoffSelectionMeasurementsOnCurrentPage(TreeViewItem? anchor)
    {
        if (_currentPage == null || anchor == null || anchor.Tag is TakeoffMeasurementNode)
            return;

        var selectedItems = TakeoffItemsForSelection(anchor);
        if (selectedItems.Count == 0)
            return;

        var pageMeasurements = selectedItems
            .SelectMany(item => MeasurementsForTakeoffOnPage(item, _currentPage.FolderPath))
            .Distinct()
            .ToList();

        _syncingViewportSelectionFromTakeoffItem = true;
        try
        {
            _viewport.SelectMeasurements(pageMeasurements);
        }
        finally
        {
            _syncingViewportSelectionFromTakeoffItem = false;
        }

        if (pageMeasurements.Count == 0)
        {
            TxtStatus.Text = $"No selected takeoff measurements on {_currentPage.Name}.";
        }
        else if (selectedItems.Count == 1)
        {
            TxtStatus.Text = pageMeasurements.Count == 1
                ? $"Selected {selectedItems[0].Name} measurement on {_currentPage.Name}."
                : $"Selected {pageMeasurements.Count} {selectedItems[0].Name} measurements on {_currentPage.Name}.";
        }
        else
        {
            TxtStatus.Text = $"Selected {pageMeasurements.Count} measurements from {selectedItems.Count} takeoffs on {_currentPage.Name}.";
        }
    }

    private bool CanMovePageTakeoffLegendNode(PageTakeoffNode node, int offset)
    {
        IReadOnlyList<TakeoffItem> ordered = OrderedTakeoffsForPage(node.Page);
        int index = IndexOfTakeoff(ordered, node.Takeoff);
        int target = index + offset;
        return index >= 0 && target >= 0 && target < ordered.Count;
    }

    private bool CanMovePageTakeoffLegendNodes(PageTakeoffNode anchor, int offset)
    {
        var selectedNodes = SelectedPageTakeoffNodes(anchor, fallbackToAnchor: true);
        return CanMovePageTakeoffLegendNodes(selectedNodes, anchor.Page, offset);
    }

    private bool CanMovePageTakeoffLegendNodes(IReadOnlyList<PageTakeoffNode> selectedNodes, PageInfo page, int offset)
    {
        if (offset == 0 || selectedNodes.Count == 0)
            return false;

        var ordered = OrderedTakeoffsForPage(page).ToList();
        if (ordered.Count <= 1)
            return false;

        var selectedKeys = selectedNodes
            .Select(node => TakeoffLegendOrderKey(node.Takeoff))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedKeys.Count == 0 || selectedKeys.Count >= ordered.Count)
            return false;

        if (offset < 0)
        {
            for (int i = 1; i < ordered.Count; i++)
            {
                if (selectedKeys.Contains(TakeoffLegendOrderKey(ordered[i])) &&
                    !selectedKeys.Contains(TakeoffLegendOrderKey(ordered[i - 1])))
                    return true;
            }
        }
        else
        {
            for (int i = 0; i < ordered.Count - 1; i++)
            {
                if (selectedKeys.Contains(TakeoffLegendOrderKey(ordered[i])) &&
                    !selectedKeys.Contains(TakeoffLegendOrderKey(ordered[i + 1])))
                    return true;
            }
        }

        return false;
    }

    private void MovePageTakeoffLegendNode(PageTakeoffNode node, int offset)
    {
        var ordered = OrderedTakeoffsForPage(node.Page).ToList();
        int index = IndexOfTakeoff(ordered, node.Takeoff);
        int target = index + offset;
        if (index < 0 || target < 0 || target >= ordered.Count)
            return;

        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        SavePageLegendOrder(node.Page, ordered);
        RefreshPagesTakeoffIndicators();
        RefreshSheetLegend();
        SelectPageTakeoffNodeSilently(node.Page.FolderPath, node.Takeoff.FolderPath);
        TxtStatus.Text = offset < 0
            ? $"Moved {node.Takeoff.Name} up in {node.Page.Name} legend."
            : $"Moved {node.Takeoff.Name} down in {node.Page.Name} legend.";
    }

    private void MovePageTakeoffLegendNodes(PageTakeoffNode anchor, int offset)
    {
        var selectedNodes = SelectedPageTakeoffNodes(anchor, fallbackToAnchor: true);
        if (selectedNodes.Count <= 1)
        {
            MovePageTakeoffLegendNode(anchor, offset);
            return;
        }

        if (!CanMovePageTakeoffLegendNodes(selectedNodes, anchor.Page, offset))
            return;

        var ordered = OrderedTakeoffsForPage(anchor.Page).ToList();
        var selectedKeys = selectedNodes
            .Select(node => TakeoffLegendOrderKey(node.Takeoff))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (offset < 0)
        {
            for (int i = 1; i < ordered.Count; i++)
            {
                string currentKey = TakeoffLegendOrderKey(ordered[i]);
                string previousKey = TakeoffLegendOrderKey(ordered[i - 1]);
                if (selectedKeys.Contains(currentKey) && !selectedKeys.Contains(previousKey))
                    (ordered[i - 1], ordered[i]) = (ordered[i], ordered[i - 1]);
            }
        }
        else
        {
            for (int i = ordered.Count - 2; i >= 0; i--)
            {
                string currentKey = TakeoffLegendOrderKey(ordered[i]);
                string nextKey = TakeoffLegendOrderKey(ordered[i + 1]);
                if (selectedKeys.Contains(currentKey) && !selectedKeys.Contains(nextKey))
                    (ordered[i], ordered[i + 1]) = (ordered[i + 1], ordered[i]);
            }
        }

        SavePageLegendOrder(anchor.Page, ordered);
        RefreshPagesTakeoffIndicators();
        RefreshSheetLegend();
        SelectPageTakeoffNodeSilently(anchor.Page.FolderPath, anchor.Takeoff.FolderPath);
        ApplyPagesMultiSelectionVisuals();
        TxtStatus.Text = offset < 0
            ? $"Moved {selectedNodes.Count} linked takeoffs up in {anchor.Page.Name} legend."
            : $"Moved {selectedNodes.Count} linked takeoffs down in {anchor.Page.Name} legend.";
    }

    private bool CanSortPageLegend(PageInfo page) =>
        TakeoffsForPage(page.FolderPath).Skip(1).Any();

    private static bool HasCustomPageLegendOrder(PageInfo page) =>
        page.LegendTakeoffOrder.Count > 0;

    private void SortPageLegendByName(PageInfo page, string? selectTakeoffFolder = null)
    {
        var ordered = TakeoffsForPage(page.FolderPath)
            .OrderBy(takeoff => takeoff.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(takeoff => MeasurementTypeTitle(takeoff.MeasurementType), StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ordered.Count <= 1)
            return;

        SavePageLegendOrder(page, ordered);
        RefreshPagesTakeoffIndicators();
        RefreshSheetLegend();
        SelectLegendOrderResult(page, selectTakeoffFolder);
        TxtStatus.Text = $"Sorted {page.Name} legend A-Z.";
    }

    private void ResetPageLegendOrder(PageInfo page, string? selectTakeoffFolder = null)
    {
        page.LegendTakeoffOrder = [];
        if (_currentPage != null && IsSamePageFolder(_currentPage.FolderPath, page.FolderPath))
            _currentPage.LegendTakeoffOrder = [];
        SmartTakeoffsJobStore.SavePageLegendTakeoffOrder(page.FolderPath, []);
        RefreshPagesTakeoffIndicators();
        RefreshSheetLegend();
        SelectLegendOrderResult(page, selectTakeoffFolder);
        TxtStatus.Text = $"Reset {page.Name} legend order.";
    }

    private void SelectLegendOrderResult(PageInfo page, string? selectTakeoffFolder)
    {
        if (string.IsNullOrWhiteSpace(selectTakeoffFolder))
            return;

        SelectPageTakeoffNodeSilently(page.FolderPath, selectTakeoffFolder);
    }

    private bool CanDropPageTakeoffLegend(PageTakeoffLegendDrag drag, TreeViewItem? targetItem)
    {
        if (targetItem?.Tag is not PageTakeoffNode targetNode)
            return false;
        if (!IsSamePageFolder(drag.PageFolder, targetNode.Page.FolderPath))
            return false;

        var draggedFolders = drag.TakeoffFolders
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (draggedFolders.Count == 0)
            return false;
        if (draggedFolders.Contains(targetNode.Takeoff.FolderPath, StringComparer.OrdinalIgnoreCase))
            return false;

        IReadOnlyList<TakeoffItem> ordered = OrderedTakeoffsForPage(targetNode.Page);
        return draggedFolders.All(folder => IndexOfTakeoffByFolder(ordered, folder) >= 0) &&
               IndexOfTakeoff(ordered, targetNode.Takeoff) >= 0;
    }

    private void UpdatePageTakeoffLegendDropCue(PageTakeoffLegendDrag drag, TreeViewItem targetItem, Point targetPosition)
    {
        bool dropAfter = IsPageTakeoffLegendDropAfter(targetItem, targetPosition);
        if (ReferenceEquals(_pageTakeoffLegendDropTarget, targetItem) &&
            _pageTakeoffLegendDropAfter == dropAfter)
        {
            return;
        }

        _pageTakeoffLegendDropTarget = targetItem;
        _pageTakeoffLegendDropAfter = dropAfter;
        ApplyPagesMultiSelectionVisuals();
        if (targetItem.Tag is PageTakeoffNode node)
        {
            IReadOnlyList<TakeoffItem> ordered = OrderedTakeoffsForPage(node.Page);
            int targetIndex = IndexOfTakeoff(ordered, node.Takeoff);
            int insertPosition = Math.Clamp(targetIndex + (dropAfter ? 2 : 1), 1, Math.Max(1, ordered.Count));
            string countText = drag.TakeoffFolders.Count == 1 ? "1 linked takeoff" : $"{drag.TakeoffFolders.Count} linked takeoffs";
            TxtStatus.Text = $"Drop {countText} {(dropAfter ? "below" : "above")} {node.Takeoff.Name} | {node.Page.Name} legend position {insertPosition}.";
        }
    }

    private void ClearPageTakeoffLegendDropCue()
    {
        if (_pageTakeoffLegendDropTarget == null)
            return;

        _pageTakeoffLegendDropTarget = null;
        ApplyPagesMultiSelectionVisuals();
    }

    private static bool IsPageTakeoffLegendDropAfter(TreeViewItem targetItem, Point targetPosition) =>
        targetPosition.Y > Math.Max(1.0, targetItem.ActualHeight) / 2.0;

    private void DropPageTakeoffLegend(PageTakeoffLegendDrag drag, TreeViewItem targetItem, Point targetPosition)
    {
        if (!CanDropPageTakeoffLegend(drag, targetItem) ||
            targetItem.Tag is not PageTakeoffNode targetNode)
        {
            return;
        }

        var draggedKeys = drag.TakeoffFolders
            .Select(NormalizeTakeoffLegendOrderKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (draggedKeys.Count == 0)
            return;

        var ordered = OrderedTakeoffsForPage(targetNode.Page).ToList();
        var moved = ordered
            .Where(takeoff => draggedKeys.Contains(TakeoffLegendOrderKey(takeoff)))
            .ToList();
        if (moved.Count == 0)
            return;

        ordered.RemoveAll(takeoff => draggedKeys.Contains(TakeoffLegendOrderKey(takeoff)));
        int targetIndex = IndexOfTakeoff(ordered, targetNode.Takeoff);
        if (targetIndex < 0)
            return;

        bool insertAfter = IsPageTakeoffLegendDropAfter(targetItem, targetPosition);
        int insertIndex = targetIndex + (insertAfter ? 1 : 0);
        insertIndex = Math.Clamp(insertIndex, 0, ordered.Count);
        ordered.InsertRange(insertIndex, moved);

        SavePageLegendOrder(targetNode.Page, ordered);
        RefreshPagesTakeoffIndicators();
        RefreshSheetLegend();
        SelectPageTakeoffNodeSilently(targetNode.Page.FolderPath, moved[0].FolderPath);
        ApplyPagesMultiSelectionVisuals();
        int firstPosition = insertIndex + 1;
        TxtStatus.Text = moved.Count == 1
            ? $"Moved {moved[0].Name} to {targetNode.Page.Name} legend position {firstPosition}."
            : $"Moved {moved.Count} linked takeoffs to {targetNode.Page.Name} legend positions {firstPosition}-{firstPosition + moved.Count - 1}.";
    }

    private static int IndexOfTakeoff(IReadOnlyList<TakeoffItem> takeoffs, TakeoffItem target)
    {
        for (int i = 0; i < takeoffs.Count; i++)
        {
            if (string.Equals(takeoffs[i].FolderPath, target.FolderPath, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static int IndexOfTakeoffByFolder(IReadOnlyList<TakeoffItem> takeoffs, string folderPath)
    {
        for (int i = 0; i < takeoffs.Count; i++)
        {
            if (string.Equals(takeoffs[i].FolderPath, folderPath, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private void SelectPageTakeoffNodeSilently(string pageFolder, string takeoffFolder)
    {
        if (FindPageTakeoffTreeItem(pageFolder, takeoffFolder) is not { } item)
            return;

        _syncingPageTreeSelection = true;
        try
        {
            ExpandTreeItemAndAncestorsWithoutTracking(item);
            item.IsSelected = true;
            item.BringIntoView();
        }
        finally
        {
            _syncingPageTreeSelection = false;
        }
    }

    private TreeViewItem? FindPageTakeoffTreeItem(string pageFolder, string takeoffFolder)
    {
        foreach (TreeViewItem item in EnumeratePageTreeItems())
        {
            if (item.Tag is not PageTakeoffNode node)
                continue;

            if (IsSamePageFolder(node.Page.FolderPath, pageFolder) &&
                string.Equals(node.Takeoff.FolderPath, takeoffFolder, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

    private static void ExpandTreeItemAndAncestors(TreeViewItem item)
    {
        item.IsExpanded = true;
        ItemsControl? parent = ItemsControl.ItemsControlFromItemContainer(item);
        while (parent is TreeViewItem parentItem)
        {
            parentItem.IsExpanded = true;
            parent = ItemsControl.ItemsControlFromItemContainer(parentItem);
        }
    }

    private void TreeView_RequestBringIntoViewKeepLeft(object sender, RequestBringIntoViewEventArgs e)
    {
        if (sender is not TreeView tree)
            return;

        Dispatcher.InvokeAsync(() =>
        {
            foreach (ScrollViewer scrollViewer in FindVisualChildren<ScrollViewer>(tree))
                scrollViewer.ScrollToHorizontalOffset(0);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private static MenuItem MakeMenuItem(string header, bool isEnabled, Action action)
    {
        var item = new MenuItem { Header = header, IsEnabled = isEnabled };
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem MakeSubmenu(string header, params object[] children)
    {
        var item = new MenuItem { Header = header };
        foreach (object child in children)
            item.Items.Add(child);
        return item;
    }

    private void NewPageFolder(TreeViewItem item)
    {
        if (item.Tag is not PageFolderNode folder || !IsPathInsidePagesRoot(folder.FolderPath))
            return;

        string? name = ShowInputDialog("Folder name:", "New Folder", "New Folder");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            string created = SmartTakeoffsJobStore.CreateFolder(folder.FolderPath, name);
            ReloadPagesTree(created);
            TxtStatus.Text = $"Created folder: {SmartTakeoffsJobStore.DisplayName(created)}";
        }
        catch (Exception ex)
        {
            ShowOperationError("New Folder", ex);
        }
    }

    private void RenamePagesNode(TreeViewItem item)
    {
        if (IsRootPagesNode(item)) return;
        string? path = GetPagesNodePath(item);
        if (path == null || !IsPathInsidePagesRoot(path, allowRoot: false)) return;

        string currentName = SmartTakeoffsJobStore.DisplayName(path);
        string? name = ShowInputDialog("New name:", currentName, item.Tag is PageInfo ? "Rename Page" : "Rename Folder");
        if (string.IsNullOrWhiteSpace(name) || name == currentName) return;

        try
        {
            string renamed = SmartTakeoffsJobStore.RenameNode(path, name);
            bool reloadActiveTab = UpdatePageReferencesForMovedPath(path, renamed);
            ReloadPagesTree(renamed);
            ReloadActivePageTabAfterPathChange(reloadActiveTab);
            TxtStatus.Text = $"Renamed to: {SmartTakeoffsJobStore.DisplayName(renamed)}";
        }
        catch (Exception ex)
        {
            ShowOperationError("Rename", ex);
        }
    }

    private void DeletePagesNode(TreeViewItem item)
    {
        var entries = GetSelectedPageEntries(item);
        if (entries.Count == 0) return;

        string message;
        if (entries.Count == 1)
        {
            string path = entries[0].SourcePath;
            bool isPage = entries[0].IsPage;
            bool hasChildren = Directory.EnumerateFileSystemEntries(path).Any();
            string name = SmartTakeoffsJobStore.DisplayName(path);
            message = isPage
                ? $"Delete page '{name}'?"
                : hasChildren
                    ? $"Delete folder '{name}' and everything inside it?"
                    : $"Delete empty folder '{name}'?";
        }
        else
        {
            message = $"Delete {entries.Count} selected page/folder item(s)?";
        }

        var result = MessageBox.Show(message, "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        var deletedNames = entries.Select(e => SmartTakeoffsJobStore.DisplayName(e.SourcePath)).ToList();
        var parents = entries
            .Select(e => Path.GetDirectoryName(e.SourcePath) ?? _currentJob?.PagesRoot ?? "")
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        string? selectAfter = parents.FirstOrDefault() ?? _currentJob?.PagesRoot;
        try
        {
            foreach (var entry in entries)
                ClearCurrentPageIfAffected(entry.SourcePath);

            foreach (var entry in entries)
                DeleteDirectoryToRecycle(entry.SourcePath);

            if (_pagesClipboard != null && entries.Any(e =>
                    _pagesClipboard.Entries.Any(c =>
                        SmartTakeoffsJobStore.IsSameOrDescendant(e.SourcePath, c.SourcePath))))
                _pagesClipboard = null;

            foreach (string parent in parents.Where(Directory.Exists))
                SmartTakeoffsJobStore.NormalizeOrder(parent);
            _pagesMultiSelection.Clear();
            ReloadPagesTree(selectAfter);
            TxtStatus.Text = entries.Count == 1
                ? $"Deleted: {deletedNames[0]}"
                : $"Deleted {entries.Count} items.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Delete", ex);
        }
    }

    private void CopyCutPagesNode(TreeViewItem item, PagesClipboardMode mode)
    {
        var entries = GetSelectedPageEntries(item);
        if (entries.Count == 0) return;

        _pagesClipboard = new PagesClipboard(entries, mode);
        string verb = mode == PagesClipboardMode.Copy ? "Copied" : "Cut";
        TxtStatus.Text = entries.Count == 1
            ? $"{verb}: {SmartTakeoffsJobStore.DisplayName(entries[0].SourcePath)}"
            : $"{verb} {entries.Count} items.";
    }

    private void PasteIntoSelectedTarget(TreeViewItem item)
    {
        string? targetFolder = GetPasteTargetFolder(item);
        if (targetFolder == null) return;
        PasteIntoFolder(targetFolder);
    }

    private void PasteIntoFolder(string targetFolder)
    {
        if (_pagesClipboard == null || !CanPasteInto(targetFolder)) return;

        RunDrop(_pagesClipboard, targetFolder, _pagesClipboard.Mode);
    }

    private void RunDrop(PagesClipboard payload, string targetFolder, PagesClipboardMode mode)
    {
        bool wasCut = mode == PagesClipboardMode.Cut;

        try
        {
            var pastedItems = new List<string>();
            bool reloadActiveTab = false;
            foreach (var entry in payload.Entries)
            {
                string source = entry.SourcePath;
                if (!Directory.Exists(source))
                    continue;
                if (!CanDropInto(new PagesClipboard([entry], mode), targetFolder, mode))
                    continue;

                string pasted;
                if (wasCut)
                {
                    pasted = SmartTakeoffsJobStore.MoveNode(source, targetFolder);
                    reloadActiveTab = UpdatePageReferencesForMovedPath(source, pasted) || reloadActiveTab;
                }
                else
                {
                    pasted = SmartTakeoffsJobStore.CopyNode(source, targetFolder);
                }

                pastedItems.Add(pasted);
            }

            if (wasCut)
                _pagesClipboard = null;
            if (pastedItems.Count == 0)
                return;

            _pagesMultiSelection.Clear();
            foreach (string pasted in pastedItems)
                _pagesMultiSelection.Add(pasted);
            ReloadPagesTree(pastedItems[0]);
            ReloadActivePageTabAfterPathChange(reloadActiveTab);
            TxtStatus.Text = pastedItems.Count == 1
                ? $"{(wasCut ? "Moved" : "Pasted")}: {SmartTakeoffsJobStore.DisplayName(pastedItems[0])}"
                : $"{(wasCut ? "Moved" : "Pasted")} {pastedItems.Count} items.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Paste", ex);
        }
    }

    private void DuplicatePageNode(TreeViewItem item)
    {
        if (item.Tag is not PageInfo page || !IsPathInsidePagesRoot(page.FolderPath, allowRoot: false))
            return;

        try
        {
            string duplicated = SmartTakeoffsJobStore.DuplicatePage(page.FolderPath);
            ReloadPagesTree(duplicated);
            TxtStatus.Text = $"Duplicated page: {SmartTakeoffsJobStore.DisplayName(duplicated)}";
        }
        catch (Exception ex)
        {
            ShowOperationError("Duplicate Page", ex);
        }
    }

    private void MovePagesNode(TreeViewItem item, int offset)
    {
        MovePagesNodes(item, offset);
    }

    private bool CanMovePagesNodes(TreeViewItem item, int offset)
    {
        var paths = GetSelectedPageEntries(item)
            .Select(entry => entry.SourcePath)
            .ToList();
        return SmartTakeoffsJobStore.CanMoveSiblings(paths, offset);
    }

    private void MovePagesNodes(TreeViewItem item, int offset)
    {
        if (IsRootPagesNode(item)) return;
        string? path = GetPagesNodePath(item);
        if (path == null || !IsPathInsidePagesRoot(path, allowRoot: false)) return;

        var entries = GetSelectedPageEntries(item);
        var paths = entries.Select(entry => entry.SourcePath).ToList();
        if (paths.Count == 0)
            return;

        try
        {
            if (SmartTakeoffsJobStore.MoveSiblings(paths, offset))
            {
                _pagesMultiSelection.Clear();
                foreach (string selectedPath in paths)
                    _pagesMultiSelection.Add(selectedPath);

                ReloadPagesTree(paths[0]);
                TxtStatus.Text = paths.Count == 1
                    ? (offset < 0 ? "Moved up." : "Moved down.")
                    : (offset < 0 ? $"Moved {paths.Count} page/folder items up." : $"Moved {paths.Count} page/folder items down.");
            }
        }
        catch (Exception ex)
        {
            ShowOperationError(offset < 0 ? "Move Up" : "Move Down", ex);
        }
    }

    private void SortFolderChildren(TreeViewItem item, bool descending)
    {
        if (item.Tag is not PageFolderNode folder || !IsPathInsidePagesRoot(folder.FolderPath))
            return;

        try
        {
            SmartTakeoffsJobStore.SortChildren(folder.FolderPath, descending);
            ReloadPagesTree(folder.FolderPath);
            TxtStatus.Text = descending ? "Sorted children Z-A." : "Sorted children A-Z.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Sort Children", ex);
        }
    }

    private void MovePageToFolder(TreeViewItem item)
    {
        if (item.Tag is not PageInfo page || _currentJob == null)
            return;

        string? target = SelectFolder("Select destination folder inside Pages", _currentJob.PagesRoot);
        if (target == null) return;
        target = Path.GetFullPath(target);

        if (!IsPathInsidePagesRoot(target) || SmartTakeoffsJobStore.IsPageFolder(target))
        {
            MessageBox.Show("Choose a folder inside the current job's Pages tree.",
                            "Move to Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.Equals(Path.GetDirectoryName(page.FolderPath), target, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            string moved = SmartTakeoffsJobStore.MoveNode(page.FolderPath, target);
            bool reloadActiveTab = UpdatePageReferencesForMovedPath(page.FolderPath, moved);
            ReloadPagesTree(moved);
            ReloadActivePageTabAfterPathChange(reloadActiveTab);
            TxtStatus.Text = $"Moved page to: {SmartTakeoffsJobStore.DisplayName(target)}";
        }
        catch (Exception ex)
        {
            ShowOperationError("Move to Folder", ex);
        }
    }

    private void BtnAutoPageFolders_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "Auto Page Folders",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string baseFolder = CurrentPagesFolderTarget();
        AutoCreatePageFolders(baseFolder);
    }

    private void AutoCreatePageFolders(string baseFolder)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(baseFolder) || !Directory.Exists(baseFolder))
            return;

        string mode = ResolveFolderTemplateMode();
        string modeLabel = FolderTemplateModeLabel(mode);
        string preview = PlanSwiftFolderTemplateService.PreviewNames(
            PlanSwiftFolderTemplateService.PageFolderNames(mode));
        string baseName = SmartTakeoffsJobStore.DisplayName(baseFolder);
        var confirm = MessageBox.Show(
            $"Create standard {modeLabel} page folders under '{baseName}'?\n\n{preview}\n\nExisting folders will be skipped.",
            "Auto Page Folders",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            FolderTemplateResult result = PlanSwiftFolderTemplateService.CreatePageFolders(baseFolder, mode);
            ReloadPagesTree(baseFolder);
            TxtStatus.Text = $"Page folders ({modeLabel}): created {result.Created}, skipped {result.Skipped}, errors {result.Errors}.";
            ShowFolderTemplateErrors("Auto Page Folders", result);
        }
        catch (Exception ex)
        {
            ShowOperationError("Auto Page Folders", ex);
        }
    }

    private void BtnSortPagesArchStruct_Click(object sender, RoutedEventArgs e)
    {
        SortPagesIntoArchStruct();
    }

    private void BtnSortPagesSuffix_Click(object sender, RoutedEventArgs e)
    {
        SortPagesBySuffix();
    }

    private void BtnRepairMeasurementPageLinks_Click(object sender, RoutedEventArgs e)
    {
        RepairMeasurementPageLinks();
    }

    private void RepairMeasurementPageLinks()
    {
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "Repair Measurement Links",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int repaired = RepairMeasurementPageFolderReferences();
        _lastMeasurementPageFolderRepairCount = repaired;
        _viewport.SetMeasurements(_takeoffItems.SelectMany(item => item.Measurements));
        RefreshPagesTakeoffIndicators();
        RefreshEstimateTable();
        RefreshAllTotals();
        TxtStatus.Text = BuildMeasurementRepairStatus(
            repaired > 0
                ? "Repair Links completed"
                : "Repair Links: all resolvable measurement page links already match current pages");
    }

    private void SortPagesIntoArchStruct()
    {
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "Sort A/S Pages",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            string imported = SmartTakeoffsJobStore.EnsureFolder(_currentJob.PagesRoot, "00. imported");
            string arch = SmartTakeoffsJobStore.EnsureFolder(imported, "Arch");
            string struc = SmartTakeoffsJobStore.EnsureFolder(imported, "Struct");
            string others = SmartTakeoffsJobStore.EnsureFolder(_currentJob.PagesRoot, "--------others");

            IReadOnlyList<PageInfo> pages = CollectPagesUnder(_currentJob.PagesRoot)
                .GroupBy(page => NormalizePath(page.FolderPath), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            int movedArch = 0;
            int movedStruct = 0;
            int movedOthers = 0;
            int skipped = 0;
            bool reloadActiveTab = false;
            string? selectAfter = null;

            foreach (PageInfo page in pages)
            {
                string target = ClassifyArchStructPageTarget(page, arch, struc, others);
                if (string.IsNullOrWhiteSpace(target))
                {
                    skipped++;
                    continue;
                }

                string parent = Path.GetDirectoryName(page.FolderPath) ?? "";
                if (string.Equals(parent, target, StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                string oldPath = page.FolderPath;
                string movedPath = SmartTakeoffsJobStore.MoveNode(oldPath, target);
                reloadActiveTab = UpdatePageReferencesForMovedPath(oldPath, movedPath) || reloadActiveTab;
                selectAfter ??= movedPath;

                if (string.Equals(target, arch, StringComparison.OrdinalIgnoreCase))
                    movedArch++;
                else if (string.Equals(target, struc, StringComparison.OrdinalIgnoreCase))
                    movedStruct++;
                else
                    movedOthers++;
            }

            SmartTakeoffsJobStore.SortChildren(arch, descending: false);
            SmartTakeoffsJobStore.SortChildren(struc, descending: false);
            SmartTakeoffsJobStore.SortChildren(others, descending: false);
            ReloadPagesTree(selectAfter ?? imported);
            ReloadActivePageTabAfterPathChange(reloadActiveTab);
            TxtStatus.Text = $"Sort A/S: Arch {movedArch}, Struct {movedStruct}, Others {movedOthers}, skipped {skipped}.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Sort A/S Pages", ex);
        }
    }

    private static string ClassifyArchStructPageTarget(PageInfo page, string arch, string struc, string others)
    {
        string name = (page.Name ?? "").Trim();
        if (name.EndsWith("-", StringComparison.Ordinal))
            return others;

        char first = name.FirstOrDefault(char.IsLetter);
        if (first == 'A' || first == 'a')
            return arch;
        if (first == 'S' || first == 's')
            return struc;

        string sourceName = Path.GetFileName(page.PdfPath);
        if (sourceName.Contains("struct", StringComparison.OrdinalIgnoreCase))
            return struc;
        if (sourceName.Contains("arch", StringComparison.OrdinalIgnoreCase))
            return arch;

        return "";
    }

    private void SortPagesBySuffix()
    {
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "Sort D/Sec/WT Pages",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            string detailsStruct = EnsurePagesRootFolder("details struct");
            string detailsArch = EnsurePagesRootFolder("details arch");
            string units = EnsurePagesRootFolder("units");
            string sections = EnsurePagesRootFolder("sections");

            IReadOnlyList<PageInfo> pages = CollectPagesUnder(_currentJob.PagesRoot)
                .GroupBy(page => NormalizePath(page.FolderPath), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            int movedTop = 0;
            int movedDetailsStruct = 0;
            int movedDetailsArch = 0;
            int movedUnits = 0;
            int movedSections = 0;
            int skipped = 0;
            bool reloadActiveTab = false;
            string? selectAfter = null;

            foreach (PageInfo page in pages)
            {
                string target = ClassifySuffixPageTarget(page, detailsStruct, detailsArch, units, sections);
                if (string.IsNullOrWhiteSpace(target))
                {
                    skipped++;
                    continue;
                }

                string parent = Path.GetDirectoryName(page.FolderPath) ?? "";
                if (string.Equals(parent, target, StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                string oldPath = page.FolderPath;
                string movedPath = SmartTakeoffsJobStore.MoveNode(oldPath, target);
                reloadActiveTab = UpdatePageReferencesForMovedPath(oldPath, movedPath) || reloadActiveTab;
                selectAfter ??= movedPath;

                if (string.Equals(target, _currentJob.PagesRoot, StringComparison.OrdinalIgnoreCase))
                    movedTop++;
                else if (string.Equals(target, detailsStruct, StringComparison.OrdinalIgnoreCase))
                    movedDetailsStruct++;
                else if (string.Equals(target, detailsArch, StringComparison.OrdinalIgnoreCase))
                    movedDetailsArch++;
                else if (string.Equals(target, units, StringComparison.OrdinalIgnoreCase))
                    movedUnits++;
                else if (string.Equals(target, sections, StringComparison.OrdinalIgnoreCase))
                    movedSections++;
            }

            SmartTakeoffsJobStore.SortChildren(detailsStruct, descending: false);
            SmartTakeoffsJobStore.SortChildren(detailsArch, descending: false);
            SmartTakeoffsJobStore.SortChildren(units, descending: false);
            SmartTakeoffsJobStore.SortChildren(sections, descending: false);
            int reorderedTop = ReorderRootSuffixPagesToTop(_currentJob.PagesRoot);

            ReloadPagesTree(selectAfter ?? _currentJob.PagesRoot);
            ReloadActivePageTabAfterPathChange(reloadActiveTab);
            TxtStatus.Text =
                $"Sort D/Sec/WT: top {movedTop}, details struct {movedDetailsStruct}, details arch {movedDetailsArch}, " +
                $"units {movedUnits}, sections {movedSections}, reordered {reorderedTop}, skipped {skipped}.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Sort D/Sec/WT Pages", ex);
        }
    }

    private string EnsurePagesRootFolder(string displayName)
    {
        if (_currentJob == null)
            return "";

        foreach (string child in SmartTakeoffsJobStore.GetOrderedChildDirectories(_currentJob.PagesRoot))
        {
            if (!SmartTakeoffsJobStore.IsPageFolder(child) &&
                string.Equals(SmartTakeoffsJobStore.DisplayName(child), displayName, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return SmartTakeoffsJobStore.EnsureFolder(_currentJob.PagesRoot, displayName);
    }

    private string ClassifySuffixPageTarget(
        PageInfo page,
        string detailsStruct,
        string detailsArch,
        string units,
        string sections)
    {
        if (_currentJob == null)
            return "";

        (string suffix, char first) = DetectPageSuffixSortInfo(page);
        if (PageSuffixTopOrder.Contains(suffix, StringComparer.OrdinalIgnoreCase))
            return _currentJob.PagesRoot;
        if (string.Equals(suffix, "d", StringComparison.OrdinalIgnoreCase) && first == 's')
            return detailsStruct;
        if (string.Equals(suffix, "d", StringComparison.OrdinalIgnoreCase) && first == 'a')
            return detailsArch;
        if (string.Equals(suffix, "u", StringComparison.OrdinalIgnoreCase))
            return units;
        if (string.Equals(suffix, "sec", StringComparison.OrdinalIgnoreCase))
            return sections;
        return "";
    }

    private static (string Suffix, char First) DetectPageSuffixSortInfo(PageInfo page)
    {
        string suffix = AutoSortSuffixFromName(page.Name);
        char first = AutoSortFirstLetter(page.Name);
        PdfSheetMetadata? metadata = null;

        if (string.IsNullOrWhiteSpace(suffix) || first is not ('a' or 's'))
        {
            metadata = SmartTakeoffsJobStore.ReadSourcePdfMetadata(page.FolderPath);
        }

        if (string.IsNullOrWhiteSpace(suffix) && !string.IsNullOrWhiteSpace(metadata?.Suffix))
            suffix = metadata.Suffix.Trim().ToLowerInvariant();

        if (first is not ('a' or 's') && metadata != null)
        {
            string metadataName = $"{metadata.SheetLabel} {metadata.EffectiveSheetKey}";
            first = AutoSortFirstLetter(metadataName);
        }

        return (suffix, first);
    }

    private int ReorderRootSuffixPagesToTop(string pagesRoot)
    {
        var children = SmartTakeoffsJobStore.GetOrderedChildDirectories(pagesRoot).ToList();
        var topPages = new List<string>();
        foreach (string suffix in PageSuffixTopOrder)
        {
            topPages.AddRange(children.Where(child =>
                SmartTakeoffsJobStore.TryReadPage(child) is { } childPage &&
                string.Equals(DetectPageSuffixSortInfo(childPage).Suffix, suffix, StringComparison.OrdinalIgnoreCase)));
        }

        if (topPages.Count == 0)
            return 0;

        var topSet = topPages
            .Select(NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ordered = topPages
            .Concat(children.Where(child => !topSet.Contains(NormalizePath(child))))
            .ToList();

        for (int i = 0; i < ordered.Count; i++)
            SmartTakeoffsJobStore.SetOrderIndex(ordered[i], i);
        return topPages.Count;
    }

    private static char AutoSortFirstLetter(string name)
    {
        foreach (char ch in (name ?? "").Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                return ch;
        }
        return '\0';
    }

    private static string AutoSortSuffixFromName(string name)
    {
        string raw = (name ?? "").Trim().ToLowerInvariant().TrimEnd(' ', '.', '_', '-');
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        string tokenText = Regex.Replace(raw, @"[\s._-]+", " ").Trim();
        foreach (string suffix in PageSuffixDetectionOrder)
        {
            if (Regex.IsMatch(tokenText, $@"(?:^| ){Regex.Escape(suffix)}$"))
                return suffix;
        }

        string compact = Regex.Replace(raw, @"[\s._-]+", "");
        foreach (string suffix in PageSuffixDetectionOrder)
        {
            if (!compact.EndsWith(suffix, StringComparison.Ordinal))
                continue;

            int previousIndex = compact.Length - suffix.Length - 1;
            char previous = previousIndex >= 0 ? compact[previousIndex] : '\0';
            if (previous == '\0' || char.IsDigit(previous))
                return suffix;
        }

        return "";
    }

    private string CurrentPagesFolderTarget()
    {
        if (_currentJob == null)
            return "";

        if (PagesTree.SelectedItem is TreeViewItem { Tag: PageFolderNode folder })
            return folder.FolderPath;

        if (PagesTree.SelectedItem is TreeViewItem { Tag: PageInfo page })
            return Path.GetDirectoryName(page.FolderPath) ?? _currentJob.PagesRoot;

        return _currentJob.PagesRoot;
    }

    private string ResolveFolderTemplateMode() =>
        _currentJob == null
            ? NormalizeFolderTemplateMode(_settings.FolderTemplateMode) switch
            {
                "EWP" => "EWP",
                _ => "COM",
            }
            : PlanSwiftFolderTemplateService.ResolveMode(_currentJob, _settings.FolderTemplateMode);

    private string FolderTemplateModeLabel(string resolvedMode)
    {
        string requested = NormalizeFolderTemplateMode(_settings.FolderTemplateMode);
        return requested == "AUTO" ? $"Auto -> {resolvedMode}" : requested;
    }

    private static string NormalizeFolderTemplateMode(string? mode) =>
        (mode ?? "AUTO").Trim().ToUpperInvariant() switch
        {
            "COM" => "COM",
            "EWP" => "EWP",
            _ => "AUTO",
        };

    private async void BtnAutoRenamePdf_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedPdfAutomationTarget("Auto Name") is { } item)
            await AnalyzePdfMetadataAsync(item, applyRename: true, applyScale: false);
    }

    private async void BtnAutoScalePdf_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedPdfAutomationTarget("Auto Scale") is { } item)
            await AnalyzePdfMetadataAsync(item, applyRename: false, applyScale: true);
    }

    private async void BtnAutoRenameScalePdf_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedPdfAutomationTarget("Auto Name + Scale") is { } item)
            await AnalyzePdfMetadataAsync(item, applyRename: true, applyScale: true);
    }

    private void BtnQueuePdfMetadataFallback_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedPdfAutomationTarget("AI Fill") is { } item)
            QueuePdfMetadataFallback(item);
    }

    private TreeViewItem? GetSelectedPdfAutomationTarget(string title)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before PDF automation.";
            return null;
        }

        if (PagesTree.SelectedItem is TreeViewItem selected &&
            (selected.Tag is PageInfo || selected.Tag is PageFolderNode))
        {
            return selected;
        }

        if (PagesTree.Items.OfType<TreeViewItem>().FirstOrDefault() is { } root)
            return root;

        MessageBox.Show("No PDF pages found.", title, MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }

    private async Task AnalyzePdfMetadataAsync(TreeViewItem item, bool applyRename, bool applyScale)
    {
        if (_currentJob == null)
            return;

        var pages = GetPagesForMetadata(item).ToList();
        if (pages.Count == 0)
        {
            MessageBox.Show("No PDF pages found in this selection.", "PDF Metadata",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SaveCurrentPageScale();
        TxtStatus.Text = $"Analyzing PDF metadata for {pages.Count} page(s)...";

        SmartTakeoffsJob job = _currentJob;
        List<PdfMetadataPageResult> results = await Task.Run(() =>
        {
            var analyzed = new List<PdfMetadataPageResult>();
            foreach (PageInfo page in pages)
            {
                if (PdfSheetMetadataService.TryAnalyzeAndSave(job, page, out var metadata, out string error))
                    analyzed.Add(new PdfMetadataPageResult(page, true, metadata, ""));
                else
                    analyzed.Add(new PdfMetadataPageResult(page, false, null, error));
            }

            return analyzed;
        });

        int okCount = results.Count(result => result.Ok);
        int failCount = results.Count - okCount;
        string operationTitle = applyRename && applyScale
            ? "Auto Rename + Scale from PDF"
            : applyRename
                ? "Auto Rename from PDF"
                : applyScale
                    ? "Auto Scale from PDF"
                    : "Analyze PDF Metadata";

        if (!applyRename && !applyScale)
        {
            ReloadPagesTree(pages[0].FolderPath);
            string message = BuildPdfMetadataSummary(results, includeApplyPreview: false);
            MessageBox.Show(message, "Analyze PDF Metadata", MessageBoxButton.OK,
                            failCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            TxtStatus.Text = $"PDF metadata analyzed: {okCount} OK, {failCount} failed.";
            return;
        }

        string preview = BuildPdfMetadataSummary(results, includeApplyPreview: true);
        if (okCount == 0)
        {
            MessageBox.Show(preview, operationTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtStatus.Text = $"PDF metadata analyze failed for {failCount} page(s).";
            return;
        }

        var rows = BuildPdfMetadataPreviewRows(results, applyRename, applyScale).ToList();
        var dialog = new PdfMetadataPreviewDialog(rows, operationTitle)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            TxtStatus.Text = $"PDF metadata analyzed: {okCount} OK, apply cancelled.";
            return;
        }

        ApplyPdfMetadataResults(job, results, dialog.Rows);
    }

    private void ApplyPdfMetadataResults(
        SmartTakeoffsJob job,
        IReadOnlyList<PdfMetadataPageResult> results,
        IReadOnlyList<PdfMetadataPreviewRow> rows)
    {
        int renamed = 0;
        int scaled = 0;
        int failed = 0;
        string? selectAfter = null;
        var rowsByFolder = rows.ToDictionary(row => NormalizePath(row.PageFolder), StringComparer.OrdinalIgnoreCase);

        foreach (var result in results.Where(result => result.Ok && result.Metadata != null))
        {
            PdfSheetMetadata metadata = result.Metadata!;
            if (!rowsByFolder.TryGetValue(NormalizePath(result.Page.FolderPath), out PdfMetadataPreviewRow? row))
                continue;
            if (!row.ApplyRename && !row.ApplyScale)
                continue;

            string currentPath = result.Page.FolderPath;
            string finalName = SmartTakeoffsJobStore.DisplayName(currentPath);
            double finalScale = result.Page.ScaleMetersPerPt;

            try
            {
                if (row.ApplyScale && metadata.CanApplyScale())
                {
                    finalScale = metadata.SelectedScaleMetersPerPt;
                    SmartTakeoffsJobStore.SavePageScale(currentPath, finalScale);
                    scaled++;
                }

                if (row.ApplyRename)
                {
                    string proposedName = metadata.ProposedPageName();
                    if (!string.IsNullOrWhiteSpace(proposedName) &&
                        !string.Equals(proposedName, finalName, StringComparison.OrdinalIgnoreCase))
                    {
                        string renamedPath = SmartTakeoffsJobStore.RenameNode(currentPath, proposedName);
                        currentPath = renamedPath;
                        finalName = SmartTakeoffsJobStore.DisplayName(renamedPath);
                        renamed++;
                    }
                }

                SmartTakeoffsJobStore.WriteSourcePdfMetadata(currentPath, metadata);
                if (SmartTakeoffsJobStore.TryReadPage(currentPath) is { } finalPage)
                {
                    var finalDecision = PdfSheetMetadataService.FinalDecision(finalPage, metadata, finalName, finalScale);
                    string outcome = (row.ApplyRename && !string.Equals(row.ProposedPageName, finalName, StringComparison.OrdinalIgnoreCase))
                        ? "corrected"
                        : "accepted";
                    SmartLearningStore.AppendSheetFeedback(
                        job,
                        finalPage,
                        PdfSheetMetadataService.BuildLearningRecord(
                            result.Page,
                            metadata,
                            outcome,
                            "User applied PDF metadata preview.",
                            finalDecision));
                }

                selectAfter ??= currentPath;
            }
            catch (Exception ex)
            {
                failed++;
                SmartLearningStore.AppendSheetFeedback(
                    job,
                    result.Page,
                    PdfSheetMetadataService.BuildLearningRecord(
                        result.Page,
                        metadata,
                        "failed_apply",
                        ex.Message));
            }
        }

        _currentPage = null;
        _currentPdfPath = "";
        ReloadPagesTree(selectAfter ?? _currentJob?.PagesRoot);
        TxtStatus.Text = $"PDF metadata applied: {renamed} renamed, {scaled} scaled, {failed} failed.";
    }

    private IEnumerable<PdfMetadataPreviewRow> BuildPdfMetadataPreviewRows(
        IReadOnlyList<PdfMetadataPageResult> results,
        bool defaultRename,
        bool defaultScale)
    {
        foreach (var result in results.Where(result => result.Ok && result.Metadata != null))
        {
            PdfSheetMetadata metadata = result.Metadata!;
            string proposedName = metadata.ProposedPageName();
            bool canRename = !string.IsNullOrWhiteSpace(proposedName) &&
                             !string.Equals(proposedName, result.Page.Name, StringComparison.OrdinalIgnoreCase);
            bool nameConflict = HasPageNameConflict(result.Page.FolderPath, proposedName);
            bool canScale = metadata.CanApplyScale();
            SmartSheetLearningSignal learning = SmartLearningStore.BuildSheetMetadataSignal(metadata);
            bool learnedConflict = string.Equals(learning.Confidence, "learned-conflict", StringComparison.OrdinalIgnoreCase);
            var warnings = metadata.Warnings.ToList();
            if (nameConflict)
                warnings.Add("proposed page name already exists in this folder");
            if (!string.IsNullOrWhiteSpace(learning.Warning))
                warnings.Add(learning.Warning);

            yield return new PdfMetadataPreviewRow
            {
                PageFolder = result.Page.FolderPath,
                CurrentPageName = result.Page.Name,
                SheetLabel = metadata.SheetLabel,
                SheetTitle = metadata.SheetTitle,
                ProposedPageName = proposedName,
                Suffix = metadata.Suffix,
                ProposedScale = canScale ? metadata.EffectiveScaleText : metadata.SkipScale ? "skip" : "",
                Source = metadata.Source,
                Confidence = learning.Confidence,
                Reason = PdfMetadataDecisionReason(metadata, learning, canRename, canScale, nameConflict, learnedConflict),
                Warnings = string.Join("; ", warnings),
                ApplyRename = defaultRename && canRename && !nameConflict && !learnedConflict,
                ApplyScale = defaultScale && canScale && !learnedConflict,
            };
        }
    }

    private static string PdfMetadataDecisionReason(
        PdfSheetMetadata metadata,
        SmartSheetLearningSignal learning,
        bool canRename,
        bool canScale,
        bool nameConflict,
        bool learnedConflict)
    {
        var parts = new List<string>();
        string key = metadata.EffectiveSheetKey;
        if (canRename)
        {
            string suffix = string.IsNullOrWhiteSpace(metadata.Suffix) ? "no suffix" : $"suffix {metadata.Suffix}";
            parts.Add($"name from {metadata.Source}: {metadata.SheetLabel} / {key} / {suffix}");
        }
        else if (nameConflict)
        {
            parts.Add("rename blocked: proposed name already exists");
        }
        else
        {
            parts.Add("rename unchanged");
        }

        if (metadata.SkipScale)
            parts.Add("scale skipped by metadata");
        else if (canScale)
            parts.Add($"scale from '{metadata.EffectiveScaleText}'");
        else
            parts.Add("no usable scale detected");

        if (learnedConflict)
            parts.Add("learned-rule conflict blocks auto apply");
        else if (learning.SupportingRecords > 0)
            parts.Add($"learning support {learning.SupportingRecords}, conflicts {learning.ConflictingRecords}");

        return string.Join(" | ", parts);
    }

    private static bool HasPageNameConflict(string pageFolder, string proposedName)
    {
        if (string.IsNullOrWhiteSpace(proposedName))
            return false;

        string parent = Path.GetDirectoryName(pageFolder) ?? "";
        if (string.IsNullOrWhiteSpace(parent))
            return false;

        string target = Path.Combine(parent, SmartTakeoffsJobStore.SanitizeName(proposedName, 120));
        return !string.Equals(NormalizePath(pageFolder), NormalizePath(target), StringComparison.OrdinalIgnoreCase) &&
               Directory.Exists(target);
    }

    private string BuildPdfMetadataSummary(IReadOnlyList<PdfMetadataPageResult> results, bool includeApplyPreview)
    {
        var sb = new StringBuilder();
        int okCount = results.Count(result => result.Ok);
        int failCount = results.Count - okCount;
        sb.AppendLine($"Pages: {results.Count}, OK: {okCount}, Failed: {failCount}");
        sb.AppendLine();

        foreach (var result in results.Take(30))
        {
            if (!result.Ok || result.Metadata == null)
            {
                sb.AppendLine($"- {result.Page.Name}: failed - {result.Error}");
                continue;
            }

            PdfSheetMetadata metadata = result.Metadata;
            string proposed = metadata.ProposedPageName();
            string scale = metadata.CanApplyScale()
                ? metadata.EffectiveScaleText
                : metadata.SkipScale
                    ? "skip scale"
                    : "no scale";
            string warnings = metadata.Warnings.Count > 0
                ? $" [{string.Join("; ", metadata.Warnings.Take(2))}]"
                : "";
            SmartSheetLearningSignal learning = SmartLearningStore.BuildSheetMetadataSignal(metadata);
            string reason = PdfMetadataDecisionReason(
                metadata,
                learning,
                canRename: !string.IsNullOrWhiteSpace(proposed) &&
                           !string.Equals(proposed, result.Page.Name, StringComparison.OrdinalIgnoreCase),
                canScale: metadata.CanApplyScale(),
                nameConflict: HasPageNameConflict(result.Page.FolderPath, proposed),
                learnedConflict: string.Equals(learning.Confidence, "learned-conflict", StringComparison.OrdinalIgnoreCase));

            sb.AppendLine(includeApplyPreview
                ? $"- {result.Page.Name} -> {proposed}; {scale}; {reason}{warnings}"
                : $"- {result.Page.Name}: {metadata.SheetLabel} {metadata.SheetTitle}; {proposed}; {scale}; {reason}{warnings}");
        }

        if (results.Count > 30)
            sb.AppendLine($"...and {results.Count - 30} more page(s).");

        return sb.ToString().TrimEnd();
    }

    private IReadOnlyList<PageInfo> GetPagesForMetadata(TreeViewItem item)
    {
        if (_currentJob == null)
            return [];

        var paths = new List<string>();
        if (IsRootPagesNode(item))
        {
            paths.Add(_currentJob.PagesRoot);
        }
        else
        {
            var entries = GetSelectedPageEntries(item);
            if (entries.Count > 0)
                paths.AddRange(entries.Select(entry => entry.SourcePath));
            else if (GetPagesNodePath(item) is { } path)
                paths.Add(path);
        }

        return paths
            .SelectMany(CollectPagesUnder)
            .GroupBy(page => NormalizePath(page.FolderPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(page => page.FolderPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IEnumerable<PageInfo> CollectPagesUnder(string path)
    {
        if (!Directory.Exists(path))
            yield break;

        if (SmartTakeoffsJobStore.TryReadPage(path) is { } page)
        {
            yield return page;
            yield break;
        }

        foreach (string child in SmartTakeoffsJobStore.GetOrderedChildDirectories(path))
        {
            foreach (PageInfo pageInfo in CollectPagesUnder(child))
                yield return pageInfo;
        }
    }

    private static void OpenSourcePdfMetadata(string pageFolder)
    {
        string path = SmartTakeoffsJobStore.SourcePdfMetadataPath(pageFolder);
        if (!File.Exists(path))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = "notepad.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true,
        });
    }

    private void CaptureFinalLearningSnapshot(TreeViewItem item)
    {
        if (_currentJob == null)
            return;

        var pages = GetPagesForMetadata(item);
        if (pages.Count == 0)
            return;

        foreach (PageInfo page in pages)
            SmartLearningStore.CaptureManualPageState(_currentJob, page, "End-of-project/manual learning snapshot.");
        SmartSheetLearningSummary summary = SmartLearningStore.SaveProjectSummary(_currentJob);

        MessageBox.Show(
            $"Captured {pages.Count} page state(s)." + Environment.NewLine +
            $"Learning records in this project: {summary.RecordCount}.",
            "Capture Final Learning Snapshot",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        TxtStatus.Text = $"Captured final learning snapshot for {pages.Count} page(s).";
    }

    private void ReviewLearnedRules()
    {
        if (_currentJob != null)
            SmartLearningStore.EnsureLearningStore(_currentJob);

        SmartLearnedRuleSet rules = SmartLearningStore.LoadGlobalLearnedRules();
        if (rules.Rules.Count == 0)
        {
            MessageBox.Show(
                "No learned rules yet. Capture a final learning snapshot after reviewed projects to generate rules.",
                "Review Learned Rules",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new LearnedRulesDialog(rules, "Review Global Learned Rules")
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        SmartLearningStore.SaveGlobalLearnedRules(dialog.RuleSet);
        int enabled = dialog.RuleSet.Rules.Count(rule => rule.Enabled);
        TxtStatus.Text = $"Saved learned rules: {enabled} enabled, {dialog.RuleSet.Rules.Count - enabled} disabled.";
    }

    private void ReviewProjectLearnedRules()
    {
        if (_currentJob == null)
            return;

        SmartLearningStore.EnsureLearningStore(_currentJob);
        SmartLearnedRuleSet rules = SmartLearningStore.LoadProjectLearnedRules(_currentJob);
        if (rules.Rules.Count == 0)
        {
            MessageBox.Show(
                "No project learned rules yet. Capture a final learning snapshot for this project to generate rules.",
                "Review Project Learned Rules",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new LearnedRulesDialog(rules, "Review Project Learned Rules")
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        SmartLearningStore.SaveProjectLearnedRules(_currentJob, dialog.RuleSet);
        int enabled = dialog.RuleSet.Rules.Count(rule => rule.Enabled);
        TxtStatus.Text = $"Saved project learned rules: {enabled} enabled, {dialog.RuleSet.Rules.Count - enabled} disabled.";
    }

    private void QueuePdfMetadataFallback(TreeViewItem item)
    {
        if (_currentJob == null)
            return;

        var pages = GetPagesForMetadata(item).ToList();
        if (pages.Count == 0)
            return;

        int queued = 0;
        int skipped = 0;
        int failed = 0;
        var errors = new List<string>();

        foreach (PageInfo page in pages)
        {
            try
            {
                PdfSheetMetadata? metadata = SmartTakeoffsJobStore.ReadSourcePdfMetadata(page.FolderPath);
                if (metadata == null)
                {
                    PdfSheetMetadataService.TryAnalyzeAndSave(_currentJob, page, out metadata, out _);
                }

                if (!PdfSheetMetadataService.NeedsFallback(metadata))
                {
                    skipped++;
                    continue;
                }

                if (HasExistingMetadataFallbackRequest(_currentJob, page))
                {
                    skipped++;
                    continue;
                }

                string cropsRoot = Path.Combine(_currentJob.AIContextRoot, "crops");
                string fileName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_sheetmeta_{SafeFileNamePart(page.Name)}.png";
                string cropPath = Path.Combine(cropsRoot, fileName);
                if (!PdfSheetMetadataService.TrySaveFallbackCrop(page, cropPath, out SKRect cropRect, out string error))
                {
                    failed++;
                    errors.Add($"{page.Name}: {error}");
                    continue;
                }

                string relativeCrop = Path.GetRelativePath(_currentJob.AIContextRoot, cropPath);
                string observationText =
                    "GPT fallback requested for PDF sheet metadata." + Environment.NewLine +
                    $"- AI crop: {relativeCrop}" + Environment.NewLine +
                    $"- PDF crop: {FormatPdfRect(cropRect)}" + Environment.NewLine +
                    $"- Page folder: {Path.GetRelativePath(_currentJob.RootPath, page.FolderPath)}" + Environment.NewLine +
                    $"- Current page name: {page.Name}" + Environment.NewLine +
                    $"- Current PDF source: {page.PdfPath}" + Environment.NewLine +
                    $"- Current PDF page: {page.PdfPage + 1}" + Environment.NewLine;

                if (metadata != null)
                {
                    observationText +=
                        "- Deterministic metadata JSON:" + Environment.NewLine +
                        JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }) +
                        Environment.NewLine;
                }

                SmartObservation observation = SmartContextStore.AddObservation(
                    _currentJob,
                    page,
                    "pdf_sheet_metadata_fallback",
                    observationText);
                SmartContextStore.AddAiRequest(
                    _currentJob,
                    page,
                    observation,
                    "pdf_sheet_metadata_fallback",
                    BuildPdfMetadataFallbackPrompt(page, metadata),
                    relativeCrop,
                    "Read the sheet title block crop and return sheet metadata JSON only.");
                queued++;
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"{page.Name}: {ex.Message}");
            }
        }

        LoadObservationsInbox();
        string message = $"Queued: {queued}. Skipped: {skipped}. Failed: {failed}.";
        if (errors.Count > 0)
            message += Environment.NewLine + string.Join(Environment.NewLine, errors.Take(5));
        MessageBox.Show(message, "Queue GPT Metadata Fallback",
                        failed > 0 ? MessageBoxButton.OK : MessageBoxButton.OK,
                        failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        TxtStatus.Text = $"Queued GPT metadata fallback for {queued} page(s).";
    }

    private static bool HasExistingMetadataFallbackRequest(SmartTakeoffsJob job, PageInfo page)
    {
        string pageFolder = NormalizePath(page.FolderPath);
        string relativePageFolder = Path.GetRelativePath(job.RootPath, page.FolderPath);
        foreach (SmartAiRequest request in SmartContextStore.LoadAiRequests(job))
        {
            if (!string.Equals(request.Type, "pdf_sheet_metadata_fallback", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(request.Status, "failed", StringComparison.OrdinalIgnoreCase))
                continue;

            string requestFolder = "";
            if (!string.IsNullOrWhiteSpace(request.PageFolder))
            {
                requestFolder = Path.IsPathFullyQualified(request.PageFolder)
                    ? NormalizePath(request.PageFolder)
                    : NormalizePath(Path.Combine(job.RootPath, request.PageFolder));
            }

            if (string.Equals(requestFolder, pageFolder, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(request.PageFolder, relativePageFolder, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildPdfMetadataFallbackPrompt(PageInfo page, PdfSheetMetadata? metadata)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Read this construction sheet title-block crop.");
        sb.AppendLine("Return one fenced JSON block only. Do not include prose outside JSON.");
        sb.AppendLine();
        sb.AppendLine("Required JSON shape:");
        sb.AppendLine("{");
        sb.AppendLine("  \"sheet_label\": \"S-100\",");
        sb.AppendLine("  \"sheet_key\": \"s100\",");
        sb.AppendLine("  \"sheet_title\": \"FOUNDATION PLAN\",");
        sb.AppendLine("  \"suffix\": \"f\",");
        sb.AppendLine("  \"skip_scale\": false,");
        sb.AppendLine("  \"selected_scale_text\": \"1/8\\\" = 1'0\\\"\",");
        sb.AppendLine("  \"confidence\": \"gpt-image-high | gpt-image-medium | gpt-image-low\",");
        sb.AppendLine("  \"warnings\": []");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Naming suffix rules:");
        sb.AppendLine("notes=n skip, schedules=sc skip, details=d skip, foundation=f, first=1st, second=2nd, third=3rd, fourth=4th, roof=rf, sections=sec, unit plans=u, wall/partition types=wt, floor types=ft.");
        sb.AppendLine("Sections must be scale eligible. Details/notes/schedules must skip scale.");
        sb.AppendLine("Allowed scales: 1/32\", 3/64\", 1/16\", 3/32\", 1/10\", 1/8\", 3/16\", 1/4\", 3/8\", 1/2\", 3/4\", 1\", 1-1/2\", 3\" = 1'0\", and 1\" = 1\".");
        sb.AppendLine("If unsure, leave fields empty and add a warning.");
        sb.AppendLine();
        sb.AppendLine($"Current page name: {page.Name}");
        if (metadata != null)
        {
            sb.AppendLine("Deterministic PDF metadata before fallback:");
            sb.AppendLine(JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        }

        return sb.ToString();
    }

    private bool CanPasteInto(string? targetFolder)
    {
        return _pagesClipboard != null && CanDropInto(_pagesClipboard, targetFolder, _pagesClipboard.Mode);
    }

    private bool CanDropInto(PagesClipboard payload, string? targetFolder, PagesClipboardMode mode)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(targetFolder))
            return false;
        if (payload.Entries.Count == 0 || !Directory.Exists(targetFolder))
            return false;
        if (!IsPathInsidePagesRoot(targetFolder) || SmartTakeoffsJobStore.IsPageFolder(targetFolder))
            return false;

        bool hasMovableEntry = false;
        foreach (var entry in payload.Entries)
        {
            if (!Directory.Exists(entry.SourcePath))
                return false;
            if (!IsPathInsidePagesRoot(entry.SourcePath, allowRoot: false))
                return false;
            if (SmartTakeoffsJobStore.IsSameOrDescendant(entry.SourcePath, targetFolder))
                return false;

            if (mode == PagesClipboardMode.Cut)
            {
                string sourceParent = Path.GetDirectoryName(entry.SourcePath) ?? "";
                if (!string.Equals(sourceParent, targetFolder, StringComparison.OrdinalIgnoreCase))
                    hasMovableEntry = true;
            }
        }

        if (mode == PagesClipboardMode.Cut && !hasMovableEntry)
            return false;

        return true;
    }

    private string? GetPasteTargetFolder(TreeViewItem item)
    {
        return item.Tag switch
        {
            PageFolderNode folder => folder.FolderPath,
            PageInfo page => Path.GetDirectoryName(page.FolderPath),
            _ => null,
        };
    }

    private static string? GetPagesNodePath(TreeViewItem item)
    {
        return item.Tag switch
        {
            PageFolderNode folder => folder.FolderPath,
            PageInfo page => page.FolderPath,
            _ => null,
        };
    }

    private static string PageTakeoffSelectionKey(PageTakeoffNode node) =>
        $"{NormalizePath(node.Page.FolderPath)}|{NormalizePath(node.Takeoff.FolderPath)}";

    private static string? GetPageTakeoffSelectionKey(TreeViewItem item) =>
        item.Tag is PageTakeoffNode node ? PageTakeoffSelectionKey(node) : null;

    private void SelectPagesRange(string? anchorPath, string targetPath, bool additive)
    {
        var candidates = EnumerateVisibleTreeItems(PagesTree)
            .Where(item => !IsRootPagesNode(item))
            .Select(item => (Item: item, Key: GetPagesNodePath(item)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .Select(entry => (entry.Item, Key: entry.Key!))
            .ToList();

        SelectRangeKeys(candidates, anchorPath, targetPath, _pagesMultiSelection, additive);
    }

    private void SelectPageTakeoffRange(string? anchorKey, string targetKey, string pageFolder, bool additive)
    {
        var candidates = EnumerateVisibleTreeItems(PagesTree)
            .Where(item => item.Tag is PageTakeoffNode node &&
                           IsSamePageFolder(node.Page.FolderPath, pageFolder))
            .Select(item => (Item: item, Key: GetPageTakeoffSelectionKey(item)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .Select(entry => (entry.Item, Key: entry.Key!))
            .ToList();

        SelectRangeKeys(candidates, anchorKey, targetKey, _pageTakeoffMultiSelection, additive);
    }

    private static void SelectRangeKeys(
        IReadOnlyList<(TreeViewItem Item, string Key)> candidates,
        string? anchorKey,
        string targetKey,
        HashSet<string> selection,
        bool additive)
    {
        int targetIndex = FindRangeKeyIndex(candidates, targetKey);
        if (targetIndex < 0)
            return;

        int anchorIndex = string.IsNullOrWhiteSpace(anchorKey)
            ? -1
            : FindRangeKeyIndex(candidates, anchorKey);
        if (anchorIndex < 0)
            anchorIndex = targetIndex;

        if (!additive)
            selection.Clear();

        int start = Math.Min(anchorIndex, targetIndex);
        int end = Math.Max(anchorIndex, targetIndex);
        for (int i = start; i <= end; i++)
            selection.Add(candidates[i].Key);
    }

    private static int FindRangeKeyIndex(IReadOnlyList<(TreeViewItem Item, string Key)> candidates, string key)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            if (string.Equals(candidates[i].Key, key, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private IReadOnlyList<PagesClipboardEntry> GetSelectedPageEntries(TreeViewItem anchor)
    {
        string? anchorPath = GetPagesNodePath(anchor);
        if (anchorPath == null || IsRootPagesNode(anchor))
            return [];

        IEnumerable<string> paths = _pagesMultiSelection.Contains(anchorPath)
            ? _pagesMultiSelection
            : [anchorPath];

        var entries = paths
            .Where(path => IsPathInsidePagesRoot(path, allowRoot: false))
            .Where(Directory.Exists)
            .Select(path => new PagesClipboardEntry(path, SmartTakeoffsJobStore.IsPageFolder(path)))
            .ToList();

        return NormalizeSelectedEntries(entries);
    }

    private int PageSelectionCount(TreeViewItem anchor) =>
        GetSelectedPageEntries(anchor).Count;

    private static IReadOnlyList<PagesClipboardEntry> NormalizeSelectedEntries(
        IReadOnlyList<PagesClipboardEntry> entries)
    {
        var distinct = entries
            .GroupBy(e => NormalizePath(e.SourcePath), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(e => NormalizePath(e.SourcePath).Length)
            .ToList();

        var result = new List<PagesClipboardEntry>();
        foreach (var entry in distinct)
        {
            if (result.Any(parent => SmartTakeoffsJobStore.IsSameOrDescendant(parent.SourcePath, entry.SourcePath)))
                continue;
            result.Add(entry);
        }

        return result;
    }

    private void PrunePagesMultiSelection()
    {
        _pagesMultiSelection.RemoveWhere(path =>
            !Directory.Exists(path) || !IsPathInsidePagesRoot(path, allowRoot: false));
    }

    private void ApplyPagesMultiSelectionVisuals()
    {
        foreach (TreeViewItem item in EnumeratePageTreeItems())
        {
            string? path = GetPagesNodePath(item);
            bool selected = path != null && !IsRootPagesNode(item) && _pagesMultiSelection.Contains(path);
            string? pageTakeoffKey = GetPageTakeoffSelectionKey(item);
            bool linkedSelected = pageTakeoffKey != null && _pageTakeoffMultiSelection.Contains(pageTakeoffKey);
            item.ClearValue(Control.BorderBrushProperty);
            item.ClearValue(Control.BorderThicknessProperty);

            if (ReferenceEquals(item, _pageTakeoffLegendDropTarget))
            {
                item.Background = new SolidColorBrush(Color.FromRgb(204, 245, 218));
                item.Foreground = Brushes.Black;
                item.FontWeight = FontWeights.Normal;
                item.BorderBrush = Brushes.SeaGreen;
                item.BorderThickness = _pageTakeoffLegendDropAfter
                    ? new Thickness(0, 0, 0, 2)
                    : new Thickness(0, 2, 0, 0);
            }
            else if (IsActivePageTakeoffNode(item))
            {
                item.Background = new SolidColorBrush(Color.FromRgb(176, 214, 255));
                item.Foreground = Brushes.Black;
                item.FontWeight = FontWeights.Normal;
            }
            else if (selected || linkedSelected)
            {
                item.Background = new SolidColorBrush(Color.FromRgb(205, 226, 255));
                item.Foreground = Brushes.Black;
                item.ClearValue(Control.FontWeightProperty);
            }
            else if (IsPageMeasuredByActiveTakeoff(item))
            {
                item.Background = new SolidColorBrush(Color.FromRgb(255, 242, 166));
                item.Foreground = Brushes.Black;
                item.ClearValue(Control.FontWeightProperty);
            }
            else
            {
                item.ClearValue(Control.BackgroundProperty);
                item.ClearValue(Control.ForegroundProperty);
                item.ClearValue(Control.FontWeightProperty);
            }
        }
    }

    private IEnumerable<TreeViewItem> EnumeratePageTreeItems()
    {
        foreach (TreeViewItem root in PagesTree.Items)
        {
            foreach (TreeViewItem item in EnumeratePageTreeItems(root))
                yield return item;
        }
    }

    private static IEnumerable<TreeViewItem> EnumeratePageTreeItems(TreeViewItem item)
    {
        yield return item;
        foreach (TreeViewItem child in item.Items)
        {
            foreach (TreeViewItem nested in EnumeratePageTreeItems(child))
                yield return nested;
        }
    }

    private static IEnumerable<TreeViewItem> EnumerateVisibleTreeItems(ItemsControl parent)
    {
        foreach (TreeViewItem child in parent.Items.OfType<TreeViewItem>())
        {
            yield return child;
            if (!child.IsExpanded)
                continue;

            foreach (TreeViewItem nested in EnumerateVisibleTreeItems(child))
                yield return nested;
        }
    }

    private static bool IsRootPagesNode(TreeViewItem item) =>
        item.Tag is PageFolderNode { IsRoot: true };

    private bool IsPathInsidePagesRoot(string path, bool allowRoot = true)
    {
        if (_currentJob == null) return false;
        string root = NormalizePath(_currentJob.PagesRoot);
        string full = NormalizePath(path);
        if (allowRoot && string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            return true;
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private void ClearCurrentPageIfAffected(string affectedPath)
    {
        RemovePageTabsForAffectedPath(affectedPath);

        if (_currentPage == null) return;
        if (!SmartTakeoffsJobStore.IsSameOrDescendant(affectedPath, _currentPage.FolderPath))
            return;

        _currentPage = null;
        _currentPdfPath = "";
        _takeoffItems.Clear();
        _activeItem = null;
        TakeoffsTree.Items.Clear();
        _viewport.ClearPage();
    }

    private static void DeleteDirectoryToRecycle(string path)
    {
        FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }

    private static void OpenFolderInExplorer(string folder)
    {
        if (!Directory.Exists(folder)) return;
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{folder}\"",
            UseShellExecute = true,
        });
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsSamePageFolder(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return string.Equals(NormalizePathForCompare(left), NormalizePathForCompare(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePathForCompare(string path)
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

    private void ShowOperationError(string operation, Exception ex)
    {
        MessageBox.Show(ex.Message, operation, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ShowFolderTemplateErrors(string title, FolderTemplateResult result) =>
        ShowFolderTemplateErrors(title, result.ErrorMessages);

    private void ShowFolderTemplateErrors(string title, IReadOnlyList<string> errors)
    {
        if (errors.Count == 0)
            return;

        string message = string.Join(Environment.NewLine, errors.Take(8));
        if (errors.Count > 8)
            message += Environment.NewLine + $"...and {errors.Count - 8} more.";
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ── Takeoffs tree ─────────────────────────────────────────────────────────

    private void TakeoffsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_syncingTakeoffTreeSelection)
            return;

        if (e.NewValue is TreeViewItem selectedNode &&
            GetTakeoffNodePath(selectedNode) is { } selectedPath &&
            (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control &&
            !_takeoffsMultiSelection.Contains(selectedPath))
        {
            _takeoffsMultiSelection.Clear();
            _takeoffSectionMultiSelection.Clear();
            _takeoffsMultiSelection.Add(selectedPath);
            ApplyTakeoffPageHighlights();
        }

        if (e.NewValue is TreeViewItem { Tag: TakeoffItem item })
        {
            _takeoffSectionMultiSelection.Clear();
            item.MeasurementType = SmartTakeoffsJobStore.NormalizeMeasurementType(item.MeasurementType);
            _activeItem           = item;
            _activeTakeoffParentFolder = Path.GetDirectoryName(item.FolderPath) ?? _currentJob?.TakeoffsRoot ?? "";
            _viewport.ActiveColor = item.Color;
            _viewport.ActiveTakeoffFolder = item.FolderPath;
            if (_activeTool is "point" or "line" or "area" && _activeTool != item.MeasurementType)
                ApplyToolSelection(item.MeasurementType);
            else
            UpdateToolStatus();
            RefreshPagesTakeoffIndicators();
            RefreshActiveTakeoffVisuals();
            RevealPagesForTakeoffSelection(e.NewValue as TreeViewItem);
            Dispatcher.InvokeAsync(() => SelectTakeoffSelectionMeasurementsOnCurrentPage(e.NewValue as TreeViewItem));
            UpdateTotalDisplay();
        }
        else if (e.NewValue is TreeViewItem { Tag: TakeoffFolderNode folder })
        {
            _takeoffSectionMultiSelection.Clear();
            _activeItem = null;
            _activeTakeoffParentFolder = folder.FolderPath;
            _viewport.ActiveTakeoffFolder = "";
            TxtStatus.Text = TakeoffFolderStatusText(folder);
            UpdateToolStatus();
            RefreshPagesTakeoffIndicators();
            RefreshActiveTakeoffVisuals();
            RevealPagesForTakeoffSelection(e.NewValue as TreeViewItem);
            Dispatcher.InvokeAsync(() => SelectTakeoffSelectionMeasurementsOnCurrentPage(e.NewValue as TreeViewItem));
            UpdateTotalDisplay();
        }
        else if (e.NewValue is TreeViewItem { Tag: TakeoffMeasurementNode node })
        {
            string sectionKey = TakeoffSectionSelectionKey(node);
            _takeoffsMultiSelection.Clear();
            if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control &&
                !_takeoffSectionMultiSelection.Contains(sectionKey))
            {
                _takeoffSectionMultiSelection.Clear();
                _takeoffSectionMultiSelection.Add(sectionKey);
                _takeoffSectionRangeAnchorKey = sectionKey;
                ApplyTakeoffPageHighlights();
            }

            _activeItem = node.Item;
            _activeTakeoffParentFolder = Path.GetDirectoryName(node.Item.FolderPath) ?? _currentJob?.TakeoffsRoot ?? "";
            _viewport.ActiveColor = node.Item.Color;
            _viewport.ActiveTakeoffFolder = node.Item.FolderPath;
            if (_suppressCanvasFocusFromTakeoffSelection || Mouse.LeftButton == MouseButtonState.Pressed)
            {
                if (_currentPage != null && IsSamePageFolder(_currentPage.FolderPath, node.Measurement.PageFolder))
                    _viewport.SelectMeasurements([node.Measurement]);
            }
            else
            {
                SelectSectionOnCanvas(node.Measurement, suppressTakeoffSync: true);
            }
            UpdateToolStatus();
            RefreshPagesTakeoffIndicators();
            RefreshActiveTakeoffVisuals();
            RevealPagesForTakeoffItems([node.Item], node.Measurement.PageFolder);
            UpdateTotalDisplay();
        }
    }

    private void BtnNewItem_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "New Takeoff Item",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string parentFolder = NewTakeoffItemParentFolder();
        string measurementType = ResolveTakeoffFolderDefaultMeasurementType(
            parentFolder,
            CurrentToolMeasurementType());
        string defaultColor = ResolveTakeoffFolderDefaultColor(parentFolder, "#FF4444");
        var dlg = new NewItemDialog(
            measurementType,
            DefaultTakeoffNameForFolder(measurementType, parentFolder),
            defaultColor: defaultColor)
        {
            Owner = this,
        };
        if (dlg.ShowDialog() != true) return;

        var item = CreateUniqueTakeoffItem(dlg.ItemName, dlg.ItemColor, dlg.ItemType, parentFolder);
        ApplyTakeoffFolderDefaultsToNewItem(item, parentFolder);
        _takeoffItems.Add(item);
        var parent = FindTakeoffTreeItemByFolder(parentFolder) ?? (ItemsControl)TakeoffsTree;
        var tvi = AddTakeoffTreeItem(item, parent);
        if (parent is TreeViewItem parentTvi)
            parentTvi.IsExpanded = true;

        _activeItem           = item;
        _activeTakeoffParentFolder = parentFolder;
        _viewport.ActiveColor = item.Color;
        _viewport.ActiveTakeoffFolder = item.FolderPath;
        tvi.IsSelected        = true;
        UpdateToolStatus();
        RefreshActiveTakeoffVisuals();
        UpdateTotalDisplay();
    }

    private void BtnNewTakeoffFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "New Takeoff Folder",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string parentFolder = CurrentTakeoffParentFolder();
        string? name = ShowInputDialog("Folder name:", "New Takeoff Folder", "New Folder");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            string folderPath = SmartTakeoffsJobStore.CreateTakeoffFolder(_currentJob, parentFolder, name);
            var node = new TakeoffFolderNode
            {
                Name = SmartTakeoffsJobStore.DisplayName(folderPath),
                FolderPath = folderPath,
            };
            var parent = FindTakeoffTreeItemByFolder(parentFolder) ?? (ItemsControl)TakeoffsTree;
            var tvi = AddTakeoffFolderTreeItem(node, parent);
            if (parent is TreeViewItem parentTvi)
                parentTvi.IsExpanded = true;
            tvi.IsSelected = true;
            _activeTakeoffParentFolder = folderPath;
        }
        catch (Exception ex)
        {
            ShowOperationError("New Takeoff Folder", ex);
        }
    }

    private void BtnAutoTakeoffTree_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "Auto Takeoff Tree",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        AutoCreateTakeoffTree(CurrentTakeoffParentFolder());
    }

    private void BtnAutoTakeoffFromPages_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "Takeoff Folders From Pages",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        AutoCreateTakeoffFoldersFromPages(CurrentTakeoffParentFolder());
    }

    private void AutoCreateTakeoffTree(string baseFolder)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(baseFolder) || !Directory.Exists(baseFolder))
            return;

        FlushTakeoffAutosaves();
        string mode = ResolveFolderTemplateMode();
        string modeLabel = FolderTemplateModeLabel(mode);
        string baseName = string.Equals(baseFolder, _currentJob.TakeoffsRoot, StringComparison.OrdinalIgnoreCase)
            ? "Takeoffs"
            : SmartTakeoffsJobStore.DisplayName(baseFolder);
        var confirm = MessageBox.Show(
            $"Create standard {modeLabel} takeoff tree under '{baseName}'?\n\nExisting folders will be skipped.",
            "Auto Takeoff Tree",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            FolderTemplateResult result = PlanSwiftFolderTemplateService.CreateTakeoffTree(baseFolder, mode);
            LoadTakeoffsForJob();
            SelectTakeoffNodeByFolder(baseFolder);
            TxtStatus.Text = $"Takeoff tree ({modeLabel}): created {result.Created}, skipped {result.Skipped}, errors {result.Errors}.";
            ShowFolderTemplateErrors("Auto Takeoff Tree", result);
        }
        catch (Exception ex)
        {
            ShowOperationError("Auto Takeoff Tree", ex);
        }
    }

    private void AutoCreateTakeoffFoldersFromPages(string baseFolder)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(baseFolder) || !Directory.Exists(baseFolder))
            return;

        FlushTakeoffAutosaves();
        string mode = ResolveFolderTemplateMode();
        string modeLabel = FolderTemplateModeLabel(mode);
        IReadOnlyList<string> groupNames = PlanSwiftFolderTemplateService.CollectCapsGroupNames(_currentJob);
        if (groupNames.Count == 0)
        {
            MessageBox.Show("No CAPS page/folder names were found in Pages.", "Takeoff Folders From Pages",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string baseName = string.Equals(baseFolder, _currentJob.TakeoffsRoot, StringComparison.OrdinalIgnoreCase)
            ? "Takeoffs"
            : SmartTakeoffsJobStore.DisplayName(baseFolder);
        string preview = PlanSwiftFolderTemplateService.PreviewNames(groupNames);
        var confirm = MessageBox.Show(
            $"Create {groupNames.Count} top takeoff folder(s) from Pages CAPS names under '{baseName}', then add the standard {modeLabel} tree?\n\n{preview}\n\nExisting folders will be skipped.",
            "Takeoff Folders From Pages",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            CapsTakeoffFolderResult result = PlanSwiftFolderTemplateService.CreateTakeoffFoldersFromCapsPages(_currentJob, baseFolder, mode);
            LoadTakeoffsForJob();
            SelectTakeoffNodeByFolder(baseFolder);
            TxtStatus.Text =
                $"Takeoff folders from Pages ({modeLabel}): top created {result.TopCreated}, top skipped {result.TopSkipped}, " +
                $"sub created {result.SubCreated}, sub skipped {result.SubSkipped}, errors {result.Errors}.";
            ShowFolderTemplateErrors("Takeoff Folders From Pages", result.ErrorMessages);
        }
        catch (Exception ex)
        {
            ShowOperationError("Takeoff Folders From Pages", ex);
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "No job open - nothing to save.";
            return;
        }
        try
        {
            FlushTakeoffAutosaves();
            SaveCurrentPageScale();
            SaveCurrentPageAnnotations();
            foreach (var item in _takeoffItems)
            {
                EnsureTakeoffItemFolder(item);
                SmartTakeoffsJobStore.SaveTakeoffItem(item);
            }

            if (!string.IsNullOrEmpty(_currentPdfPath))
                ProjectFile.Save(_currentPdfPath, _viewport.ScaleMetersPerPt, _viewport.UnitMode, _takeoffItems);

            TxtStatus.Text = $"Saved takeoffs -> {_currentJob.TakeoffsRoot}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Save failed:\n{ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnAddObservation_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open or create a job before adding observations.";
            return;
        }

        string defaultText = _currentPage == null
            ? ""
            : $"Page {_currentPage.Name}: ";
        string? text = ShowInputDialog("Observation:", "Add Observation", defaultText);
        if (string.IsNullOrWhiteSpace(text))
            return;

        try
        {
            var observation = SmartContextStore.AddManualObservation(_currentJob, _currentPage, text);
            TxtStatus.Text = $"Saved observation {observation.Id} -> {_currentJob.AIContextRoot}";
            LoadObservationsInbox();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot save observation:\n{ex.Message}", "Add Observation",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Takeoff item context menu ─────────────────────────────────────────────

    private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open or create a job before exporting.";
            return;
        }

        if (_takeoffItems.Count == 0 || _takeoffItems.All(i => i.Measurements.Count == 0))
        {
            TxtStatus.Text = "No measurements to export.";
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Export Takeoff CSV",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = $"{SafeFileName(_currentJob.Name)}_takeoffs.csv",
            InitialDirectory = _currentJob.RootPath,
            AddExtension = true,
            DefaultExt = ".csv",
        };

        if (dlg.ShowDialog(this) != true)
            return;

        try
        {
            SaveCurrentPageScale();
            File.WriteAllText(dlg.FileName, BuildTakeoffCsv(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            TxtStatus.Text = $"Exported CSV -> {dlg.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed:\n{ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnExportTxt_Click(object sender, RoutedEventArgs e)
    {
        ExportPlanSwiftTakeoffs("txt");
    }

    private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
    {
        ExportPlanSwiftTakeoffs("xlsx");
    }

    private void ExportPlanSwiftTakeoffs(string format)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open or create a job before exporting.";
            return;
        }

        var rows = BuildPlanSwiftExportRows();
        if (rows.Count == 0)
        {
            TxtStatus.Text = "No measured takeoffs to export.";
            return;
        }

        bool excel = string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase);
        var dlg = new SaveFileDialog
        {
            Title = excel ? "Export Takeoffs Excel" : "Export Takeoffs TXT",
            Filter = excel ? "Excel workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*" : "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"{SafeFileName(_currentJob.Name)}_takeoffs.{(excel ? "xlsx" : "txt")}",
            InitialDirectory = _currentJob.RootPath,
            AddExtension = true,
            DefaultExt = excel ? ".xlsx" : ".txt",
        };

        if (dlg.ShowDialog(this) != true)
            return;

        try
        {
            SaveCurrentPageScale();
            if (excel)
            {
                int written = PlanSwiftTakeoffExporter.WriteXlsx(dlg.FileName, rows);
                TxtStatus.Text = $"Exported Excel ({written} row(s)) -> {dlg.FileName}";
            }
            else
            {
                PlanSwiftTakeoffExporter.WriteTxt(dlg.FileName, rows);
                TxtStatus.Text = $"Exported TXT ({rows.Count} row(s)) -> {dlg.FileName}";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed:\n{ex.Message}", excel ? "Export Excel" : "Export TXT",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private IReadOnlyList<PlanSwiftExportRow> BuildPlanSwiftExportRows()
    {
        if (_currentJob == null)
            return [];

        IReadOnlyList<string> roots = SelectedTakeoffExportRoots();
        return PlanSwiftTakeoffExporter.BuildRows(_currentJob, _takeoffItems, roots, _viewport.UnitMode);
    }

    private IReadOnlyList<string> SelectedTakeoffExportRoots()
    {
        if (_currentJob == null)
            return [];

        if (TakeoffsTree.SelectedItem is TreeViewItem anchor)
        {
            var entries = GetSelectedTakeoffEntries(anchor);
            if (entries.Count > 0)
                return entries.Select(entry => entry.SourcePath).ToList();

            return anchor.Tag switch
            {
                TakeoffItem item when Directory.Exists(item.FolderPath) => [item.FolderPath],
                TakeoffFolderNode folder when Directory.Exists(folder.FolderPath) => [folder.FolderPath],
                _ => [_currentJob.TakeoffsRoot],
            };
        }

        return [_currentJob.TakeoffsRoot];
    }

    private string BuildTakeoffCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("RowType,Item,MeasurementType,Total,MeasurementCount,UnitPrice,Cost,MeasurementId,SectionName,SectionNotes,SectionIndex,MeasurementValue,MeasurementLabel,ScaleMetersPerPt,PageFolder,TakeoffFolder");

        foreach (var item in _takeoffItems.Where(i => i.Measurements.Count > 0))
        {
            string itemTypes = item.IsJoistArea
                ? "joist"
                : string.Join("+", item.Measurements.Select(m => m.MType).Distinct(StringComparer.OrdinalIgnoreCase));
            AppendCsvRow(sb,
                "ItemTotal",
                item.Name,
                itemTypes,
                item.TotalLabel(_viewport.ScaleMetersPerPt, _viewport.UnitMode),
                item.Measurements.Count.ToString(),
                item.UnitPrice.ToString("G17", CultureInfo.InvariantCulture),
                CostText(item),
                "",
                "",
                "",
                "",
                "",
                "",
                "",
                "",
                item.FolderPath);

            for (int i = 0; i < item.Measurements.Count; i++)
            {
                Measurement measurement = item.Measurements[i];
                AppendCsvRow(sb,
                    "Measurement",
                    item.Name,
                    measurement.JoistEnabled ? "joist" : measurement.MType,
                    "",
                    "",
                    "",
                    "",
                    measurement.Id,
                    SectionDisplayName(item, measurement, i),
                    measurement.Notes,
                    (i + 1).ToString(CultureInfo.InvariantCulture),
                    measurement.Value(_viewport.ScaleMetersPerPt).ToString("G17", CultureInfo.InvariantCulture),
                    measurement.Label(_viewport.ScaleMetersPerPt, _viewport.UnitMode),
                    measurement.ScaleMetersPerPt.ToString("G17", CultureInfo.InvariantCulture),
                    measurement.PageFolder,
                    measurement.TakeoffFolder);
            }
        }

        return sb.ToString();
    }

    private static void AppendCsvRow(StringBuilder sb, params string[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(CsvEscape(values[i]));
        }
        sb.AppendLine();
    }

    private static string CsvEscape(string value)
    {
        value ??= "";
        if (!value.Contains('"') && !value.Contains(',') && !value.Contains('\n') && !value.Contains('\r'))
            return value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string SafeFileName(string value)
    {
        string name = string.IsNullOrWhiteSpace(value) ? "takeoffs" : value.Trim();
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    private void EnsureTakeoffItemFolder(TakeoffItem item)
    {
        if (_currentJob == null)
            return;

        if (string.IsNullOrWhiteSpace(item.FolderPath) || !Directory.Exists(item.FolderPath))
        {
            var stored = CreateUniqueTakeoffItem(
                item.Name,
                item.Color,
                item.MeasurementType,
                NewTakeoffItemParentFolder());
            item.FolderPath = stored.FolderPath;
        }

        foreach (var measurement in item.Measurements)
            measurement.TakeoffFolder = item.FolderPath;
        SmartTakeoffsJobStore.ApplyTakeoffPropertiesToMeasurements(item);
    }

    private TakeoffItem CreateUniqueTakeoffItem(string name, string color, string measurementType = "line", string? parentFolder = null)
    {
        if (_currentJob == null)
            throw new InvalidOperationException("No job is open.");

        string parent = string.IsNullOrWhiteSpace(parentFolder) ? _currentJob.TakeoffsRoot : parentFolder;
        string baseName = string.IsNullOrWhiteSpace(name) ? "Item" : name.Trim();
        for (int i = 0; i < 1000; i++)
        {
            string candidate = i == 0 ? baseName : $"{baseName} - Copy {i + 1}";
            try
            {
                return SmartTakeoffsJobStore.CreateTakeoffItem(_currentJob, parent, candidate, color, measurementType);
            }
            catch (IOException) when (i < 999)
            {
                // Try the next suffix.
            }
        }

        throw new IOException($"Could not create a unique takeoff item named '{baseName}'.");
    }

    private void AttachContextMenu(TreeViewItem tvi, TakeoffItem item)
    {
        var menu = new ContextMenu();
        int selectedCount = TakeoffSelectionCount(tvi);
        bool singleSelection = selectedCount <= 1;

        var activeTarget = new MenuItem { Header = IsActiveTakeoffItem(item) ? "Active Target" : "Set Active Target" };
        activeTarget.Click += (_, _) => SetActiveTakeoffTarget(tvi, item);
        activeTarget.IsEnabled = singleSelection;
        menu.Items.Add(activeTarget);
        menu.Items.Add(new Separator());

        var properties = new MenuItem { Header = "Properties..." };
        properties.Click += (_, _) => EditTakeoffItemProperties(tvi, item);
        properties.IsEnabled = singleSelection;
        menu.Items.Add(properties);
        menu.Items.Add(MakeMenuItem(
            item.IsJoistArea ? "Joist Properties..." : "Use Area As Joists...",
            singleSelection && SmartTakeoffsJobStore.NormalizeMeasurementType(item.MeasurementType) == "area",
            () => EditTakeoffItemProperties(tvi, item)));
        menu.Items.Add(MakeMenuItem(
            "Generate Joists / Draw Direction",
            singleSelection && SmartTakeoffsJobStore.NormalizeMeasurementType(item.MeasurementType) == "area",
            () => SetJoistDirectionFromSelectedLine(tvi, item)));

        int selectedItemsCount = TakeoffItemsForSelection(tvi).Count;
        menu.Items.Add(MakeMenuItem(
            selectedItemsCount > 1 ? $"Bulk Properties ({selectedItemsCount} Items)..." : "Bulk Properties...",
            selectedItemsCount > 1,
            () => EditSelectedTakeoffProperties(tvi)));

        var rename = new MenuItem { Header = "Rename..." };
        rename.Click += (_, _) => RenameItem(tvi, item);
        rename.IsEnabled = singleSelection;
        menu.Items.Add(rename);

        var newSection = new MenuItem { Header = item.MeasurementType == "point" ? "Add Count" : "New Section" };
        newSection.Click += (_, _) => StartNewSection(tvi, item);
        newSection.IsEnabled = singleSelection;
        menu.Items.Add(newSection);

        var unitPrice = new MenuItem { Header = "Set Unit Price" };
        unitPrice.Click += (_, _) => SetUnitPrice(item);
        unitPrice.IsEnabled = singleSelection;
        menu.Items.Add(unitPrice);

        menu.Items.Add(new Separator());

        menu.Items.Add(MakeMenuItem(selectedCount > 1 ? "Copy Selected" : "Copy Item", true, () => CopyCutTakeoffNode(tvi, TakeoffsClipboardMode.Copy)));
        menu.Items.Add(MakeMenuItem(selectedCount > 1 ? "Cut Selected" : "Cut Item", true, () => CopyCutTakeoffNode(tvi, TakeoffsClipboardMode.Cut)));
        menu.Items.Add(MakeMenuItem(
            "Paste Into Parent Folder",
            CanPasteTakeoffsInto(Path.GetDirectoryName(item.FolderPath)),
            () => PasteIntoSelectedTakeoffTarget(tvi)));
        menu.Items.Add(MakeMenuItem(selectedCount > 1 ? "Duplicate Selected" : "Duplicate Item", true, () => DuplicateTakeoffNode(tvi)));

        menu.Items.Add(new Separator());

        var moveUp = new MenuItem
        {
            Header = selectedCount > 1 ? $"Move {selectedCount} Up" : "Move Up",
            IsEnabled = CanMoveTakeoffNodes(tvi, -1),
        };
        moveUp.Click += (_, _) => MoveTakeoffNodes(tvi, -1);
        menu.Items.Add(moveUp);

        var moveDown = new MenuItem
        {
            Header = selectedCount > 1 ? $"Move {selectedCount} Down" : "Move Down",
            IsEnabled = CanMoveTakeoffNodes(tvi, 1),
        };
        moveDown.Click += (_, _) => MoveTakeoffNodes(tvi, 1);
        menu.Items.Add(moveDown);

        menu.Items.Add(new Separator());

        var delete = new MenuItem { Header = selectedCount > 1 ? "Delete selected takeoffs" : "Delete item + measurements" };
        delete.Click += (_, _) => DeleteTakeoffNodes(tvi);
        menu.Items.Add(delete);

        tvi.ContextMenu = menu;
    }

    private void RefreshTakeoffSectionNodes(TreeViewItem itemNode, TakeoffItem item)
    {
        bool wasExpanded = itemNode.IsExpanded;
        Measurement? selectedMeasurement = (TakeoffsTree.SelectedItem as TreeViewItem)?.Tag is TakeoffMeasurementNode selectedNode
            ? selectedNode.Measurement
            : null;

        itemNode.Items.Clear();
        for (int i = 0; i < item.Measurements.Count; i++)
        {
            Measurement measurement = item.Measurements[i];
            var node = new TakeoffMeasurementNode(item, measurement);
            var sectionTvi = new TreeViewItem { Tag = node };
            SetTakeoffSectionHeader(sectionTvi, item, measurement, i);
            AttachTakeoffSectionContextMenu(sectionTvi, item, measurement);
            itemNode.Items.Add(sectionTvi);
            if (ReferenceEquals(selectedMeasurement, measurement))
                sectionTvi.IsSelected = true;
        }

        itemNode.IsExpanded = wasExpanded;
    }

    private void AttachTakeoffSectionContextMenu(TreeViewItem tvi, TakeoffItem item, Measurement measurement)
    {
        tvi.ContextMenu = BuildTakeoffSectionContextMenu(new TakeoffMeasurementNode(item, measurement));
    }

    private ContextMenu BuildTakeoffSectionContextMenu(TakeoffMeasurementNode anchor)
    {
        TakeoffItem item = anchor.Item;
        Measurement measurement = anchor.Measurement;
        string title = MeasurementEntryTitle(item);
        int selectedCount = SelectedTakeoffSectionNodes(anchor, fallbackToAnchor: true).Count;
        bool singleSelection = selectedCount <= 1;
        var menu = new ContextMenu();
        menu.Items.Add(MakeMenuItem($"{title} Properties...", singleSelection, () => EditSectionProperties(item, measurement)));
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Set Notes for {selectedCount} Rows..." : "Set Notes...",
            true,
            () => EditTakeoffSectionNotes(anchor)));
        menu.Items.Add(MakeMenuItem($"Rename {title}", singleSelection, () => RenameSection(item, measurement)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? "Go to First Page" : "Go to Page",
            SelectedTakeoffSectionNodes(anchor, fallbackToAnchor: true).Any(node => !string.IsNullOrWhiteSpace(node.Measurement.PageFolder)),
            () => GoToTakeoffSectionsPage(anchor)));
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Select {selectedCount} on Canvas" : "Select on Canvas",
            true,
            () => SelectTakeoffSectionMeasurementsOnCanvas(SelectedTakeoffSectionNodes(anchor, fallbackToAnchor: true))));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Move {selectedCount} Up" : "Move Up",
            CanMoveTakeoffSections(anchor, -1),
            () => MoveTakeoffSections(anchor, -1)));
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Move {selectedCount} Down" : "Move Down",
            CanMoveTakeoffSections(anchor, 1),
            () => MoveTakeoffSections(anchor, 1)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem(
            selectedCount > 1 ? $"Delete {selectedCount} {title}s" : $"Delete {title}",
            true,
            () => DeleteTakeoffSections(anchor)));
        return menu;
    }

    private void SetTakeoffSectionHeader(TreeViewItem tvi, TakeoffItem item, Measurement measurement, int index)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(CreateMeasurementTypeIcon(
            measurement.JoistEnabled ? "joist" : SmartTakeoffsJobStore.NormalizeMeasurementType(measurement.MType),
            BrushFromHex(measurement.Color, Brushes.Gray),
            12,
            new Thickness(0, 0, 7, 0)));
        panel.Children.Add(new TextBlock
        {
            Text = SectionDisplayName(item, measurement, index),
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"  {QuantityText(measurement)}",
            Foreground = Brushes.Gray,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
        });

        string page = SectionPageName(measurement);
        if (!string.IsNullOrWhiteSpace(page))
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"  {page}",
                Foreground = Brushes.Gray,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        tvi.Header = panel;
        var tooltip = new StringBuilder();
        tooltip.AppendLine($"{MeasurementEntryTitle(item)} {index + 1}");
        tooltip.AppendLine($"Page: {(string.IsNullOrWhiteSpace(page) ? "unknown" : page)}");
        tooltip.AppendLine($"Quantity: {QuantityText(measurement)}");
        if (!string.IsNullOrWhiteSpace(measurement.Notes))
            tooltip.AppendLine($"Notes: {measurement.Notes}");
        tvi.ToolTip = tooltip.ToString().Trim();
    }

    private void MoveSection(TakeoffItem item, Measurement measurement, int offset)
    {
        MoveTakeoffSections(new TakeoffMeasurementNode(item, measurement), offset);
    }

    private bool CanMoveTakeoffSections(TakeoffMeasurementNode anchor, int offset)
    {
        var selectedNodes = SelectedTakeoffSectionNodes(anchor, fallbackToAnchor: true);
        return CanMoveTakeoffSections(selectedNodes, anchor.Item, offset);
    }

    private bool CanMoveTakeoffSections(IReadOnlyList<TakeoffMeasurementNode> selectedNodes, TakeoffItem item, int offset)
    {
        if (offset == 0 || selectedNodes.Count == 0 || item.Measurements.Count <= 1)
            return false;

        if (selectedNodes.Any(node => !ReferenceEquals(node.Item, item)))
            return false;

        var selectedIds = selectedNodes
            .Select(node => node.Measurement.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedIds.Count == 0 || selectedIds.Count >= item.Measurements.Count)
            return false;

        if (offset < 0)
        {
            for (int i = 1; i < item.Measurements.Count; i++)
            {
                if (selectedIds.Contains(item.Measurements[i].Id) &&
                    !selectedIds.Contains(item.Measurements[i - 1].Id))
                    return true;
            }
        }
        else
        {
            for (int i = 0; i < item.Measurements.Count - 1; i++)
            {
                if (selectedIds.Contains(item.Measurements[i].Id) &&
                    !selectedIds.Contains(item.Measurements[i + 1].Id))
                    return true;
            }
        }

        return false;
    }

    private void MoveTakeoffSections(TakeoffMeasurementNode anchor, int offset)
    {
        var selectedNodes = SelectedTakeoffSectionNodes(anchor, fallbackToAnchor: true);
        if (!CanMoveTakeoffSections(selectedNodes, anchor.Item, offset))
            return;

        var selectedIds = selectedNodes
            .Select(node => node.Measurement.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (offset < 0)
        {
            for (int i = 1; i < anchor.Item.Measurements.Count; i++)
            {
                if (selectedIds.Contains(anchor.Item.Measurements[i].Id) &&
                    !selectedIds.Contains(anchor.Item.Measurements[i - 1].Id))
                {
                    (anchor.Item.Measurements[i - 1], anchor.Item.Measurements[i]) =
                        (anchor.Item.Measurements[i], anchor.Item.Measurements[i - 1]);
                }
            }
        }
        else
        {
            for (int i = anchor.Item.Measurements.Count - 2; i >= 0; i--)
            {
                if (selectedIds.Contains(anchor.Item.Measurements[i].Id) &&
                    !selectedIds.Contains(anchor.Item.Measurements[i + 1].Id))
                {
                    (anchor.Item.Measurements[i], anchor.Item.Measurements[i + 1]) =
                        (anchor.Item.Measurements[i + 1], anchor.Item.Measurements[i]);
                }
            }
        }

        SmartTakeoffsJobStore.SaveTakeoffItem(anchor.Item);
        RefreshTreeItem(anchor.Item);
        RefreshEstimateTable();
        RefreshSheetLegend();
        SelectTakeoffSectionNodesSilently(selectedNodes);
        TxtStatus.Text = selectedNodes.Count == 1
            ? (offset < 0 ? $"Moved {MeasurementEntryTitle(anchor.Item).ToLowerInvariant()} up." : $"Moved {MeasurementEntryTitle(anchor.Item).ToLowerInvariant()} down.")
            : (offset < 0 ? $"Moved {selectedNodes.Count} {MeasurementEntryTitlePlural(selectedNodes)} up." : $"Moved {selectedNodes.Count} {MeasurementEntryTitlePlural(selectedNodes)} down.");
    }

    private void StartNewSection(TreeViewItem tvi, TakeoffItem item)
    {
        if (_currentPage == null)
        {
            MessageBox.Show(
                item.MeasurementType == "point"
                    ? "Select a page before adding a count."
                    : "Select a page before starting a new section.",
                item.MeasurementType == "point" ? "Add Count" : "New Section",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        tvi.IsSelected = true;
        _activeItem = item;
        _viewport.ActiveColor = item.Color;
        _viewport.ActiveTakeoffFolder = item.FolderPath;
        SetTool(item.MeasurementType);
        RefreshActiveTakeoffVisuals();
        if (_activeTool == item.MeasurementType)
            TxtStatus.Text = item.MeasurementType == "point"
                ? $"Add counts for {item.Name}."
                : $"New {MeasurementTypeTitle(item.MeasurementType)} section for {item.Name}.";
    }

    private void SetActiveTakeoffTarget(TreeViewItem? tvi, TakeoffItem item, bool selectCanvasMeasurements = true)
    {
        item.MeasurementType = SmartTakeoffsJobStore.NormalizeMeasurementType(item.MeasurementType);
        _activeItem = item;
        _activeTakeoffParentFolder = Path.GetDirectoryName(item.FolderPath) ?? _currentJob?.TakeoffsRoot ?? "";
        _viewport.ActiveColor = item.Color;
        _viewport.ActiveTakeoffFolder = item.FolderPath;

        if (tvi != null)
        {
            _takeoffsMultiSelection.Clear();
            if (!string.IsNullOrWhiteSpace(item.FolderPath))
                _takeoffsMultiSelection.Add(item.FolderPath);
            tvi.IsSelected = true;
            tvi.BringIntoView();
        }

        if (_activeTool is "point" or "line" or "area" && _activeTool != item.MeasurementType)
            ApplyToolSelection(item.MeasurementType);
        else
            UpdateToolStatus();

        RefreshPagesTakeoffIndicators();
        RefreshActiveTakeoffVisuals();
        RevealPagesForTakeoffItems([item], _currentPage?.FolderPath);
        if (selectCanvasMeasurements)
            Dispatcher.InvokeAsync(() => SelectCurrentPageTakeoffMeasurementsOnCanvas(item));
        UpdateTotalDisplay();
        TxtStatus.Text = $"Active takeoff target: {item.Name}.";
    }

    private void BtnActiveTakeoffRecord_Click(object sender, RoutedEventArgs e)
    {
        if (_activeItem == null)
        {
            TxtStatus.Text = "Select a takeoff item before recording.";
            return;
        }

        if (FindTakeoffTreeItem(_activeItem) is { } tvi)
        {
            StartNewSection(tvi, _activeItem);
            return;
        }

        if (_currentPage == null)
        {
            string measurementType = SmartTakeoffsJobStore.NormalizeMeasurementType(_activeItem.MeasurementType);
            MessageBox.Show(
                measurementType == "point"
                    ? "Select a page before adding a count."
                    : "Select a page before starting a new section.",
                measurementType == "point" ? "Add Count" : "New Section",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        SetActiveTakeoffTarget(null, _activeItem, selectCanvasMeasurements: false);
        SetTool(SmartTakeoffsJobStore.NormalizeMeasurementType(_activeItem.MeasurementType));
    }

    private void BtnActiveTakeoffMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not UIElement target)
            return;

        var menu = new ContextMenu
        {
            PlacementTarget = target,
            Placement = PlacementMode.Bottom,
        };

        menu.Items.Add(MakeMenuItem("Properties...", BtnActiveTakeoffProperties.IsEnabled, () => BtnActiveTakeoffProperties_Click(sender, new RoutedEventArgs())));
        menu.Items.Add(MakeMenuItem("Find in Tree", BtnActiveTakeoffFind.IsEnabled, () => BtnActiveTakeoffFind_Click(sender, new RoutedEventArgs())));
        menu.Items.Add(MakeMenuItem("Sheet Targets...", BtnActiveTakeoffSheetNext.IsEnabled, () => ShowActiveSheetTakeoffTargetMenu(target)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Previous Target", BtnActiveTakeoffPrevious.IsEnabled, () => BtnActiveTakeoffPrevious_Click(sender, new RoutedEventArgs())));
        menu.Items.Add(MakeMenuItem("Next Target", BtnActiveTakeoffNext.IsEnabled, () => BtnActiveTakeoffNext_Click(sender, new RoutedEventArgs())));

        menu.IsOpen = true;
    }

    private void BtnActiveTakeoffFind_Click(object sender, RoutedEventArgs e)
    {
        if (_activeItem == null)
        {
            TxtStatus.Text = "Select a takeoff item first.";
            return;
        }

        if (FindTakeoffTreeItem(_activeItem) is not { } tvi)
        {
            TxtStatus.Text = "Active takeoff item is not visible in the Takeoffs tree.";
            return;
        }

        SelectTakeoffItem(_activeItem);
        SetActiveTakeoffTarget(tvi, _activeItem, selectCanvasMeasurements: false);
    }

    private void BtnActiveTakeoffProperties_Click(object sender, RoutedEventArgs e)
    {
        if (_activeItem == null)
        {
            TxtStatus.Text = "Select a takeoff item first.";
            return;
        }

        if (FindTakeoffTreeItem(_activeItem) is not { } tvi)
        {
            TxtStatus.Text = "Active takeoff item is not visible in the Takeoffs tree.";
            return;
        }

        SetActiveTakeoffTarget(tvi, _activeItem, selectCanvasMeasurements: false);
        EditTakeoffItemProperties(tvi, _activeItem);
    }

    private void BtnActiveTakeoffPrevious_Click(object sender, RoutedEventArgs e) =>
        MoveActiveTakeoffTarget(-1);

    private void BtnActiveTakeoffNext_Click(object sender, RoutedEventArgs e) =>
        MoveActiveTakeoffTarget(1);

    private void BtnActiveTakeoffSheetNext_Click(object sender, RoutedEventArgs e) =>
        ShowActiveSheetTakeoffTargetMenu(BtnActiveTakeoffSheetNext);

    private void MoveActiveTakeoffTarget(int offset)
    {
        var targets = ActiveTakeoffTargetCycleItems();
        if (targets.Count == 0)
        {
            TxtStatus.Text = "No takeoff items are available.";
            return;
        }

        int currentIndex = _activeItem == null
            ? -1
            : targets.FindIndex(IsActiveTakeoffItem);
        int nextIndex = currentIndex < 0
            ? (offset < 0 ? targets.Count - 1 : 0)
            : (currentIndex + offset + targets.Count) % targets.Count;
        TakeoffItem next = targets[nextIndex];
        SetActiveTakeoffTarget(FindTakeoffTreeItem(next), next);
        TxtStatus.Text = $"Active takeoff target {nextIndex + 1}/{targets.Count}: {next.Name}.";
    }

    private List<TakeoffItem> ActiveTakeoffTargetCycleItems() =>
        _takeoffItems
            .Where(item => !string.IsNullOrWhiteSpace(item.FolderPath))
            .ToList();

    private void MoveActiveSheetTakeoffTarget(int offset)
    {
        var targets = ActiveSheetTakeoffTargetCycleItems();
        if (targets.Count == 0)
        {
            TxtStatus.Text = _currentPage == null
                ? "Select a sheet before cycling sheet takeoffs."
                : $"No takeoff items are measured on {_currentPage.Name}.";
            return;
        }

        int currentIndex = _activeItem == null
            ? -1
            : targets.FindIndex(IsActiveTakeoffItem);
        int nextIndex = currentIndex < 0
            ? (offset < 0 ? targets.Count - 1 : 0)
            : (currentIndex + offset + targets.Count) % targets.Count;
        TakeoffItem next = targets[nextIndex];
        SetActiveTakeoffTarget(FindTakeoffTreeItem(next), next);
        TxtStatus.Text = $"Sheet takeoff target {nextIndex + 1}/{targets.Count}: {next.Name}.";
    }

    private List<TakeoffItem> ActiveSheetTakeoffTargetCycleItems() =>
        _currentPage == null
            ? []
            : OrderedTakeoffsForPage(_currentPage).ToList();

    private void ShowActiveSheetTakeoffTargetMenu(UIElement? placementTarget = null)
    {
        var targets = ActiveSheetTakeoffTargetCycleItems();
        if (targets.Count == 0)
        {
            TxtStatus.Text = _currentPage == null
                ? "Select a sheet before choosing sheet takeoffs."
                : $"No takeoff items are measured on {_currentPage.Name}.";
            return;
        }

        var menu = new ContextMenu();
        menu.Items.Add(MakeMenuItem("Next Sheet Target", targets.Count > 1, () => MoveActiveSheetTakeoffTarget(1)));
        menu.Items.Add(new Separator());

        for (int i = 0; i < targets.Count; i++)
        {
            TakeoffItem target = targets[i];
            int index = i;
            string activePrefix = IsActiveTakeoffItem(target) ? "* " : "";
            string quantity = ActiveSheetTakeoffTargetQuantity(target);
            menu.Items.Add(MakeMenuItem(
                $"{activePrefix}{index + 1}. {target.Name} - {quantity}",
                true,
                () => SelectActiveSheetTakeoffTarget(target, index, targets.Count)));
        }

        menu.PlacementTarget = placementTarget ?? BtnActiveTakeoffSheetNext;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void SelectActiveSheetTakeoffTarget(TakeoffItem target, int index, int count)
    {
        SetActiveTakeoffTarget(FindTakeoffTreeItem(target), target);
        TxtStatus.Text = $"Sheet takeoff target {index + 1}/{count}: {target.Name}.";
    }

    private string ActiveSheetTakeoffTargetQuantity(TakeoffItem item)
    {
        if (_currentPage == null)
            return "";

        var measurements = MeasurementsForTakeoffOnPage(item, _currentPage.FolderPath).ToList();
        return measurements.Count == 0
            ? "none on sheet"
            : SheetLegendQuantityText(item, measurements);
    }

    private void SetUnitPrice(TakeoffItem item)
    {
        string? raw = ShowInputDialog(
            $"Unit price per {TakeoffUnitText(item)}:",
            item.UnitPrice > 0 ? item.UnitPrice.ToString("G", CultureInfo.InvariantCulture) : "0",
            "Set Unit Price");
        if (raw == null)
            return;

        if (!double.TryParse(raw.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double price) ||
            price < 0)
        {
            MessageBox.Show("Enter a valid non-negative unit price.", "Set Unit Price",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        item.UnitPrice = price;
        SmartTakeoffsJobStore.SaveTakeoffItem(item);
        RefreshTreeItem(item);
        RefreshEstimateTable();
        RefreshSheetLegend();
        TxtStatus.Text = $"Unit price set for {item.Name}: {price:G}";
    }

    private void EditTakeoffItemProperties(TreeViewItem tvi, TakeoffItem item)
    {
        if (!ShowTakeoffItemPropertiesDialog(
                item,
                out string name,
                out string color,
                out double unitPrice,
                out string notes,
                out JoistTakeoffEdit joistEdit))
        {
            return;
        }

        try
        {
            bool colorChanged = !string.Equals(item.Color, color, StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(item.FolderPath) && Directory.Exists(item.FolderPath) &&
                !string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                string oldPath = item.FolderPath;
                item.FolderPath = SmartTakeoffsJobStore.RenameNode(item.FolderPath, name);
                RebasePageLegendTakeoffOrderReferences(oldPath, item.FolderPath);
                item.Name = SmartTakeoffsJobStore.DisplayName(item.FolderPath);
                foreach (var measurement in item.Measurements)
                    measurement.TakeoffFolder = item.FolderPath;
            }
            else
            {
                item.Name = SmartTakeoffsJobStore.SanitizeName(name, 120);
            }

            item.Color = color;
            item.UnitPrice = unitPrice;
            item.Notes = notes.Trim();
            bool joistChanged =
                item.IsJoistTakeoff != joistEdit.Enabled ||
                !string.Equals(item.JoistType, joistEdit.JoistType, StringComparison.Ordinal) ||
                Math.Abs(item.JoistSpacingInches - joistEdit.SpacingInches) > 0.0001 ||
                Math.Abs(item.JoistDirectionDegrees - joistEdit.DirectionDegrees) > 0.0001 ||
                !string.Equals(
                    JoistTakeoffCalculator.NormalizeLengthRounding(item.JoistLengthRounding),
                    JoistTakeoffCalculator.NormalizeLengthRounding(joistEdit.LengthRounding),
                    StringComparison.OrdinalIgnoreCase) ||
                item.JoistShowLabels != joistEdit.ShowLabels;
            bool wasJoistArea = item.IsJoistArea;
            item.IsJoistTakeoff = joistEdit.Enabled && SmartTakeoffsJobStore.NormalizeMeasurementType(item.MeasurementType) == "area";
            item.JoistType = joistEdit.JoistType.Trim();
            item.JoistSpacingInches = joistEdit.SpacingInches > 0 ? joistEdit.SpacingInches : 16;
            item.JoistDirectionDegrees = joistEdit.DirectionDegrees;
            item.JoistLengthRounding = JoistTakeoffCalculator.NormalizeLengthRounding(joistEdit.LengthRounding);
            item.JoistShowLabels = joistEdit.ShowLabels;
            SmartTakeoffsJobStore.ApplyTakeoffPropertiesToMeasurements(item);
            if (colorChanged)
            {
                foreach (Measurement measurement in item.Measurements)
                    measurement.Color = color;
            }
            if (colorChanged || joistChanged)
                _viewport.SetMeasurements(_takeoffItems.SelectMany(takeoff => takeoff.Measurements));

            SmartTakeoffsJobStore.SaveTakeoffItem(item);
            SetTreeItemHeader(tvi, item);
            RefreshTakeoffSectionNodes(tvi, item);
            RefreshEstimateTable();
            RefreshPagesTakeoffIndicators();
            ApplyTakeoffPageHighlights();
            RefreshSheetLegend();
            UpdateTotalDisplay();
            if (item.IsJoistArea && (!wasJoistArea || item.HasPendingJoistDirections))
                BeginNextPendingJoistDirectionCapture(item);
            TxtStatus.Text = $"Updated takeoff item properties: {item.Name}";
        }
        catch (Exception ex)
        {
            ShowOperationError("Takeoff Item Properties", ex);
        }
    }

    private void SetJoistDirectionFromSelectedLine(TreeViewItem tvi, TakeoffItem item)
    {
        if (SmartTakeoffsJobStore.NormalizeMeasurementType(item.MeasurementType) != "area")
        {
            TxtStatus.Text = "Joist direction can only be set on Area takeoff items.";
            return;
        }

        Measurement? area = SelectedJoistAreaMeasurement(item);
        if (area == null)
        {
            string message = "Select one Area measurement on the sheet first, then run this joist direction command.";
            TxtStatus.Text = message;
            return;
        }

        item.IsJoistTakeoff = true;
        SmartTakeoffsJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        SmartTakeoffsJobStore.SaveTakeoffItem(item);
        _viewport.SetMeasurements(_takeoffItems.SelectMany(takeoff => takeoff.Measurements));
        BeginJoistDirectionCapture(item, area);
    }

    private Measurement? SelectedJoistAreaMeasurement(TakeoffItem item)
    {
        var selected = _viewport.GetSelectedMeasurements()
            .Where(measurement =>
                item.Measurements.Contains(measurement) &&
                SmartTakeoffsJobStore.NormalizeMeasurementType(measurement.MType) == "area")
            .ToList();
        if (selected.Count == 1)
            return selected[0];

        if (_currentPage != null)
        {
            var pageAreas = item.Measurements
                .Where(measurement =>
                    SmartTakeoffsJobStore.NormalizeMeasurementType(measurement.MType) == "area" &&
                    IsSamePageFolder(measurement.PageFolder, _currentPage.FolderPath))
                .ToList();
            if (pageAreas.Count == 1)
                return pageAreas[0];
        }

        return null;
    }

    private void BeginJoistDirectionCapture(TakeoffItem item, Measurement area)
    {
        item.IsJoistTakeoff = true;
        SmartTakeoffsJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        area.JoistDirectionLocked = false;
        _viewport.BeginJoistDirectionCapture(area);
        TxtStatus.Text = $"Joist direction for {item.Name}: draw a two-point line parallel to the joists on the selected area.";
    }

    private void OnJoistDirectionCaptured(Measurement area, SKPoint start, SKPoint end)
    {
        TakeoffItem? item = FindTakeoffItemForMeasurement(area);
        if (item == null)
            return;

        if (!TryDirectionFromPoints(start, end, out double directionDegrees))
        {
            TxtStatus.Text = "Joist direction line is too short.";
            return;
        }

        item.IsJoistTakeoff = true;
        area.JoistDirectionDegrees = directionDegrees;
        area.JoistDirectionLocked = true;
        SmartTakeoffsJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        SmartTakeoffsJobStore.SaveTakeoffItem(item);
        _viewport.SelectMeasurements([area]);
        RefreshTreeItem(item);
        RefreshActiveTakeoffVisuals();
        RefreshEstimateTable();
        RefreshPagesTakeoffIndicators();
        ApplyTakeoffPageHighlights();
        RefreshSheetLegend();
        UpdateTotalDisplay();
        if (BeginNextPendingJoistDirectionCapture(item, area))
            return;

        JoistLayoutResult layout = JoistTakeoffCalculator.Calculate(area, _viewport.ScaleMetersPerPt);
        TxtStatus.Text = $"Joists generated for {item.Name}: direction {directionDegrees:0.#} deg, {JoistTakeoffCalculator.FormatDiagnostics(layout, _viewport.UnitMode)}{FormatJoistScaleSuffix(area)}.";
    }

    private bool BeginNextPendingJoistDirectionCapture(TakeoffItem item, Measurement? skip = null)
    {
        if (_currentPage == null || !item.IsJoistArea)
            return false;

        Measurement? next = item.Measurements.FirstOrDefault(measurement =>
            !ReferenceEquals(measurement, skip) &&
            SmartTakeoffsJobStore.NormalizeMeasurementType(measurement.MType) == "area" &&
            IsSamePageFolder(measurement.PageFolder, _currentPage.FolderPath) &&
            !measurement.JoistDirectionLocked);
        if (next == null)
            return false;

        _viewport.BeginJoistDirectionCapture(next);
        TxtStatus.Text = $"Set joist direction for next area in {item.Name}: click two points parallel to the joists.";
        return true;
    }

    private bool TryGetSelectedLineDirection(out double directionDegrees, out string message)
    {
        directionDegrees = 0;
        Measurement? line = _viewport.GetSelectedMeasurements()
            .FirstOrDefault(measurement =>
                SmartTakeoffsJobStore.NormalizeMeasurementType(measurement.MType) == "line" &&
                measurement.Points.Count >= 2);
        if (line == null)
        {
            message = "Select a Line measurement on the sheet first, then run this joist direction command.";
            return false;
        }

        SKPoint start = line.Points[0];
        SKPoint end = line.Points[^1];
        if (!TryDirectionFromPoints(start, end, out directionDegrees))
        {
            message = "Selected line is too short to define joist direction.";
            return false;
        }

        message = "";
        return true;
    }

    private static bool TryDirectionFromPoints(SKPoint start, SKPoint end, out double directionDegrees)
    {
        float dx = end.X - start.X;
        float dy = end.Y - start.Y;
        if (Math.Sqrt(dx * dx + dy * dy) < 0.001)
        {
            directionDegrees = 0;
            return false;
        }

        directionDegrees = NormalizeJoistDirectionDegrees(Math.Atan2(dy, dx) * 180.0 / Math.PI);
        return true;
    }

    private static double NormalizeJoistDirectionDegrees(double degrees)
    {
        double normalized = degrees % 180.0;
        if (normalized < 0)
            normalized += 180.0;
        return Math.Abs(normalized - 180.0) < 0.0001 ? 0 : normalized;
    }

    private void EditSelectedTakeoffProperties(TreeViewItem anchor)
    {
        var selectedItems = TakeoffItemsForSelection(anchor)
            .Where(item => !string.IsNullOrWhiteSpace(item.FolderPath) && Directory.Exists(item.FolderPath))
            .GroupBy(item => NormalizePath(item.FolderPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (selectedItems.Count == 0)
        {
            TxtStatus.Text = "No takeoff items selected for bulk properties.";
            return;
        }

        if (!ShowBulkTakeoffPropertiesDialog(selectedItems, out BulkTakeoffPropertiesEdit edit))
            return;

        try
        {
            foreach (TakeoffItem selectedItem in selectedItems)
            {
                if (edit.ApplyColor)
                {
                    selectedItem.Color = edit.Color;
                    foreach (Measurement measurement in selectedItem.Measurements)
                        measurement.Color = edit.Color;
                }

                if (edit.ApplyUnitPrice)
                    selectedItem.UnitPrice = edit.UnitPrice;

                if (edit.ApplyNotes)
                    selectedItem.Notes = edit.Notes.Trim();

                SmartTakeoffsJobStore.SaveTakeoffItem(selectedItem);
                RefreshTreeItem(selectedItem);
            }

            if (edit.ApplyColor && _activeItem != null &&
                selectedItems.Any(item => string.Equals(item.FolderPath, _activeItem.FolderPath, StringComparison.OrdinalIgnoreCase)))
            {
                _viewport.ActiveColor = _activeItem.Color;
            }

            RefreshEstimateTable();
            RefreshPagesTakeoffIndicators();
            RefreshActiveTakeoffVisuals();
            RefreshSheetLegend();
            UpdateTotalDisplay();
            SelectTakeoffSelectionMeasurementsOnCurrentPage(anchor);
            TxtStatus.Text = $"Updated bulk properties for {selectedItems.Count} takeoff item(s).";
        }
        catch (Exception ex)
        {
            ShowOperationError("Bulk Takeoff Properties", ex);
        }
    }

    private bool ShowBulkTakeoffPropertiesDialog(
        IReadOnlyList<TakeoffItem> items,
        out BulkTakeoffPropertiesEdit edit)
    {
        string firstColor = NormalizeTakeoffColor(items[0].Color);
        bool sameColor = items.All(item =>
            string.Equals(NormalizeTakeoffColor(item.Color), firstColor, StringComparison.OrdinalIgnoreCase));
        double firstPrice = items[0].UnitPrice;
        bool samePrice = items.All(item => Math.Abs(item.UnitPrice - firstPrice) < 0.0000001);
        string firstNotes = items[0].Notes;
        bool sameNotes = items.All(item => string.Equals(item.Notes, firstNotes, StringComparison.Ordinal));
        var selectedTypes = items
            .Select(item => SmartTakeoffsJobStore.NormalizeMeasurementType(item.MeasurementType))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        bool sameType = selectedTypes.Count == 1;
        string typeText = sameType ? MeasurementTypeTitle(selectedTypes[0]) : "mixed Line/Area/Count";

        edit = new BulkTakeoffPropertiesEdit(false, firstColor, false, firstPrice, false, firstNotes);

        var dialog = new Window
        {
            Title = $"Bulk Takeoff Properties ({items.Count})",
            Owner = this,
            Width = 430,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
        };

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Selected items: {items.Count} | Type: {typeText}",
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 8),
        });

        var applyColorBox = new CheckBox
        {
            Content = sameColor ? "Apply color" : "Apply color (currently mixed)",
            Margin = new Thickness(0, 0, 0, 4),
        };
        panel.Children.Add(applyColorBox);

        string selectedColor = firstColor;
        var colorBox = new TextBox
        {
            Text = selectedColor,
            Width = 90,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsEnabled = false,
        };
        var swatches = new List<Border>();
        var colorPanel = new WrapPanel
        {
            Margin = new Thickness(0, 0, 0, 4),
            IsEnabled = false,
        };
        foreach (var preset in TakeoffColorPresets)
        {
            var swatch = new Border
            {
                Width = 32,
                Height = 22,
                Margin = new Thickness(2),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(preset.Hex)),
                BorderBrush = string.Equals(preset.Hex, selectedColor, StringComparison.OrdinalIgnoreCase)
                    ? Brushes.White
                    : Brushes.Transparent,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(3),
                ToolTip = preset.Label,
                Cursor = Cursors.Hand,
            };
            swatch.MouseLeftButtonDown += (_, _) =>
            {
                selectedColor = preset.Hex;
                colorBox.Text = selectedColor;
                applyColorBox.IsChecked = true;
                foreach (Border border in swatches)
                    border.BorderBrush = Brushes.Transparent;
                swatch.BorderBrush = Brushes.White;
            };
            swatches.Add(swatch);
            colorPanel.Children.Add(swatch);
        }
        panel.Children.Add(colorPanel);
        colorBox.TextChanged += (_, _) => applyColorBox.IsChecked = true;
        panel.Children.Add(colorBox);

        var applyPriceBox = new CheckBox
        {
            Content = sameType
                ? $"Apply unit price per {UnitText(selectedTypes[0])}"
                : "Unit price disabled for mixed Line/Area/Count selection",
            IsEnabled = sameType,
            Margin = new Thickness(0, 12, 0, 4),
        };
        panel.Children.Add(applyPriceBox);
        var priceBox = new TextBox
        {
            Text = samePrice && firstPrice > 0 ? firstPrice.ToString("G", CultureInfo.InvariantCulture) : "0",
            IsEnabled = false,
        };
        if (sameType)
            priceBox.TextChanged += (_, _) => applyPriceBox.IsChecked = true;
        panel.Children.Add(priceBox);

        var applyNotesBox = new CheckBox
        {
            Content = sameNotes ? "Replace notes" : "Replace notes (currently mixed)",
            Margin = new Thickness(0, 12, 0, 4),
        };
        panel.Children.Add(applyNotesBox);
        var notesBox = new TextBox
        {
            Text = sameNotes ? firstNotes : "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 90,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsEnabled = false,
        };
        notesBox.TextChanged += (_, _) => applyNotesBox.IsChecked = true;
        panel.Children.Add(notesBox);

        void RefreshEnabledFields()
        {
            bool applyColor = applyColorBox.IsChecked == true;
            colorPanel.IsEnabled = applyColor;
            colorBox.IsEnabled = applyColor;
            priceBox.IsEnabled = sameType && applyPriceBox.IsChecked == true;
            notesBox.IsEnabled = applyNotesBox.IsChecked == true;
        }

        applyColorBox.Checked += (_, _) => RefreshEnabledFields();
        applyColorBox.Unchecked += (_, _) => RefreshEnabledFields();
        applyPriceBox.Checked += (_, _) => RefreshEnabledFields();
        applyPriceBox.Unchecked += (_, _) => RefreshEnabledFields();
        applyNotesBox.Checked += (_, _) => RefreshEnabledFields();
        applyNotesBox.Unchecked += (_, _) => RefreshEnabledFields();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var ok = new Button { Content = "OK", Width = 78, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 78, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        BulkTakeoffPropertiesEdit result = edit;
        ok.Click += (_, _) =>
        {
            bool applyColor = applyColorBox.IsChecked == true;
            bool applyPrice = sameType && applyPriceBox.IsChecked == true;
            bool applyNotes = applyNotesBox.IsChecked == true;
            if (!applyColor && !applyPrice && !applyNotes)
            {
                MessageBox.Show("Choose at least one property to apply.", "Bulk Takeoff Properties",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string cleanColor = NormalizeTakeoffColor(colorBox.Text);
            if (applyColor && !IsValidWpfColor(cleanColor))
            {
                MessageBox.Show("Enter a valid color like #FF4444.", "Bulk Takeoff Properties",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            double parsedPrice = firstPrice;
            if (applyPrice &&
                (!double.TryParse(priceBox.Text.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out parsedPrice) ||
                 parsedPrice < 0))
            {
                MessageBox.Show("Enter a valid non-negative unit price.", "Bulk Takeoff Properties",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            result = new BulkTakeoffPropertiesEdit(
                applyColor,
                cleanColor,
                applyPrice,
                parsedPrice,
                applyNotes,
                notesBox.Text.Trim());
            dialog.DialogResult = true;
        };

        bool accepted = dialog.ShowDialog() == true;
        if (accepted)
            edit = result;

        return accepted;
    }

    private bool ShowTakeoffItemPropertiesDialog(
        TakeoffItem item,
        out string name,
        out string color,
        out double unitPrice,
        out string notes,
        out JoistTakeoffEdit joistEdit)
    {
        name = item.Name;
        color = NormalizeTakeoffColor(item.Color);
        unitPrice = item.UnitPrice;
        notes = item.Notes;
        joistEdit = new JoistTakeoffEdit(
            item.IsJoistArea,
            item.JoistType,
            item.JoistSpacingInches > 0 ? item.JoistSpacingInches : 16,
            item.JoistDirectionDegrees,
            JoistTakeoffCalculator.NormalizeLengthRounding(item.JoistLengthRounding),
            item.JoistShowLabels);

        var dialog = new Window
        {
            Title = "Takeoff Item Properties",
            Owner = this,
            Width = 500,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
        };

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = "Name:", Margin = new Thickness(0, 0, 0, 4) });
        var nameBox = new TextBox { Text = item.Name };
        panel.Children.Add(nameBox);

        panel.Children.Add(new TextBlock
        {
            Text = $"Type: {MeasurementTypeTitle(item.MeasurementType)}",
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 8, 0, 0),
        });
        bool isAreaTakeoff = SmartTakeoffsJobStore.NormalizeMeasurementType(item.MeasurementType) == "area";
        var joistEnabledBox = new CheckBox
        {
            Content = "Joist layout",
            IsChecked = item.IsJoistArea,
            IsEnabled = isAreaTakeoff,
            Margin = new Thickness(0, 10, 0, 2),
        };
        panel.Children.Add(joistEnabledBox);

        var joistPanel = new Grid
        {
            Margin = new Thickness(18, 0, 0, 6),
            IsEnabled = isAreaTakeoff && joistEnabledBox.IsChecked == true,
        };
        joistPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
        joistPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 5; i++)
            joistPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddLabeledTextBox(joistPanel, 0, "Joist type:", out TextBox joistTypeBox, item.JoistType);
        AddLabeledTextBox(
            joistPanel,
            1,
            "O.C. spacing (in):",
            out TextBox joistSpacingBox,
            (item.JoistSpacingInches > 0 ? item.JoistSpacingInches : 16).ToString("G", CultureInfo.InvariantCulture));
        var directionLabel = new TextBlock
        {
            Text = "Joist direction:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 3, 8, 3),
        };
        Grid.SetRow(directionLabel, 2);
        Grid.SetColumn(directionLabel, 0);
        joistPanel.Children.Add(directionLabel);
        var joistDirectionBox = new TextBox
        {
            Text = item.JoistDirectionDegrees.ToString("G", CultureInfo.InvariantCulture),
            IsReadOnly = true,
            Width = 78,
            ToolTip = "Direction is set by drawing a two-point line parallel to the joists after selecting or drawing an Area.",
        };
        Grid.SetRow(joistDirectionBox, 2);
        Grid.SetColumn(joistDirectionBox, 1);
        joistPanel.Children.Add(joistDirectionBox);

        var roundingLabel = new TextBlock
        {
            Text = "Length calc:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 3, 8, 3),
        };
        Grid.SetRow(roundingLabel, 3);
        Grid.SetColumn(roundingLabel, 0);
        joistPanel.Children.Add(roundingLabel);
        var roundingBox = new ComboBox
        {
            MinWidth = 190,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 3, 0, 3),
        };
        foreach (string rounding in new[]
                 {
                     JoistTakeoffCalculator.RoundingNone,
                     JoistTakeoffCalculator.RoundingNearestFoot,
                     JoistTakeoffCalculator.RoundingNearestEvenFoot,
                     JoistTakeoffCalculator.RoundingNearestTwoFeet,
                 })
        {
            roundingBox.Items.Add(new ComboBoxItem
            {
                Content = JoistTakeoffCalculator.LengthRoundingTitle(rounding),
                Tag = rounding,
            });
        }
        string selectedRounding = JoistTakeoffCalculator.NormalizeLengthRounding(item.JoistLengthRounding);
        for (int i = 0; i < roundingBox.Items.Count; i++)
        {
            if (roundingBox.Items[i] is ComboBoxItem option &&
                string.Equals((string?)option.Tag, selectedRounding, StringComparison.OrdinalIgnoreCase))
            {
                roundingBox.SelectedIndex = i;
                break;
            }
        }
        if (roundingBox.SelectedIndex < 0)
            roundingBox.SelectedIndex = 0;
        Grid.SetRow(roundingBox, 3);
        Grid.SetColumn(roundingBox, 1);
        joistPanel.Children.Add(roundingBox);

        var joistLabelsBox = new CheckBox
        {
            Content = "Label each joist",
            IsChecked = item.JoistShowLabels,
            Margin = new Thickness(0, 3, 0, 3),
            ToolTip = "When off, the area label still shows count / length.",
        };
        Grid.SetRow(joistLabelsBox, 4);
        Grid.SetColumn(joistLabelsBox, 1);
        joistPanel.Children.Add(joistLabelsBox);

        joistEnabledBox.Checked += (_, _) => joistPanel.IsEnabled = isAreaTakeoff;
        joistEnabledBox.Unchecked += (_, _) => joistPanel.IsEnabled = false;
        if (!isAreaTakeoff)
            joistEnabledBox.ToolTip = "Joist layout is available for Area takeoff items.";
        panel.Children.Add(joistPanel);

        panel.Children.Add(new TextBlock { Text = "Color:", Margin = new Thickness(0, 10, 0, 4) });
        string selectedColor = NormalizeTakeoffColor(item.Color);
        var colorBox = new TextBox { Text = selectedColor, Width = 90, HorizontalAlignment = HorizontalAlignment.Left };
        var swatches = new List<Border>();
        var colorPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var preset in TakeoffColorPresets)
        {
            var swatch = new Border
            {
                Width = 32,
                Height = 22,
                Margin = new Thickness(2),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(preset.Hex)),
                BorderBrush = string.Equals(preset.Hex, selectedColor, StringComparison.OrdinalIgnoreCase)
                    ? Brushes.White
                    : Brushes.Transparent,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(3),
                ToolTip = preset.Label,
                Cursor = Cursors.Hand,
            };
            swatch.MouseLeftButtonDown += (_, _) =>
            {
                selectedColor = preset.Hex;
                colorBox.Text = selectedColor;
                foreach (Border border in swatches)
                    border.BorderBrush = Brushes.Transparent;
                swatch.BorderBrush = Brushes.White;
            };
            swatches.Add(swatch);
            colorPanel.Children.Add(swatch);
        }
        panel.Children.Add(colorPanel);
        panel.Children.Add(colorBox);

        var unitPriceLabel = new TextBlock
        {
            Text = $"Unit price per {TakeoffUnitText(item)}:",
            Margin = new Thickness(0, 10, 0, 4),
        };
        panel.Children.Add(unitPriceLabel);
        joistEnabledBox.Checked += (_, _) => unitPriceLabel.Text = $"Unit price per {UnitText("line")}:";
        joistEnabledBox.Unchecked += (_, _) => unitPriceLabel.Text = $"Unit price per {UnitText(item.MeasurementType)}:";
        var priceBox = new TextBox
        {
            Text = item.UnitPrice > 0 ? item.UnitPrice.ToString("G", CultureInfo.InvariantCulture) : "0",
        };
        panel.Children.Add(priceBox);

        panel.Children.Add(new TextBlock { Text = "Notes:", Margin = new Thickness(0, 10, 0, 4) });
        var notesBox = new TextBox
        {
            Text = item.Notes,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 90,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        panel.Children.Add(notesBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var ok = new Button { Content = "OK", Width = 78, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 78, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        string resultName = item.Name;
        string resultColor = selectedColor;
        double resultPrice = item.UnitPrice;
        string resultNotes = item.Notes;
        JoistTakeoffEdit resultJoist = joistEdit;

        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text))
            {
                MessageBox.Show("Name is required.", "Takeoff Item Properties",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string cleanColor = NormalizeTakeoffColor(colorBox.Text);
            if (!IsValidWpfColor(cleanColor))
            {
                MessageBox.Show("Enter a valid color like #FF4444.", "Takeoff Item Properties",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!double.TryParse(priceBox.Text.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedPrice) ||
                parsedPrice < 0)
            {
                MessageBox.Show("Enter a valid non-negative unit price.", "Takeoff Item Properties",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool joistEnabled = isAreaTakeoff && joistEnabledBox.IsChecked == true;
            double joistSpacing = item.JoistSpacingInches > 0 ? item.JoistSpacingInches : 16;
            double joistDirection = item.JoistDirectionDegrees;
            string joistRounding = JoistTakeoffCalculator.RoundingNone;
            if (roundingBox.SelectedItem is ComboBoxItem selectedRoundingItem &&
                selectedRoundingItem.Tag is string selectedRoundingValue)
            {
                joistRounding = JoistTakeoffCalculator.NormalizeLengthRounding(selectedRoundingValue);
            }

            if (joistEnabled)
            {
                if (!double.TryParse(joistSpacingBox.Text.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out joistSpacing) ||
                    joistSpacing <= 0)
                {
                    MessageBox.Show("Enter a valid positive joist spacing.", "Takeoff Item Properties",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (!double.TryParse(joistDirectionBox.Text.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out joistDirection))
                {
                    MessageBox.Show("Enter a valid joist direction angle.", "Takeoff Item Properties",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }

            resultName = nameBox.Text.Trim();
            resultColor = cleanColor;
            resultPrice = parsedPrice;
            resultNotes = notesBox.Text.Trim();
            resultJoist = new JoistTakeoffEdit(
                joistEnabled,
                joistTypeBox.Text.Trim(),
                joistSpacing,
                joistDirection,
                joistRounding,
                joistLabelsBox.IsChecked == true);
            dialog.DialogResult = true;
        };
        dialog.Loaded += (_, _) => { nameBox.Focus(); nameBox.SelectAll(); };

        bool accepted = dialog.ShowDialog() == true;
        if (accepted)
        {
            name = resultName;
            color = resultColor;
            unitPrice = resultPrice;
            notes = resultNotes;
            joistEdit = resultJoist;
        }

        return accepted;
    }

    private static void AddLabeledTextBox(Grid grid, int row, string label, out TextBox textBox, string value)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 3, 8, 3),
        };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        textBox = new TextBox
        {
            Text = value,
            MinWidth = 190,
            Margin = new Thickness(0, 3, 0, 3),
        };
        Grid.SetRow(textBox, row);
        Grid.SetColumn(textBox, 1);
        grid.Children.Add(textBox);
    }

    private static string NormalizeTakeoffColor(string value)
    {
        string trimmed = string.IsNullOrWhiteSpace(value) ? "#FF4444" : value.Trim();
        if (!trimmed.StartsWith('#'))
            trimmed = "#" + trimmed;
        return trimmed;
    }

    private static bool IsValidWpfColor(string value)
    {
        try
        {
            _ = (Color)ColorConverter.ConvertFromString(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Brush BrushFromHex(string value, Brush fallback)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(NormalizeTakeoffColor(value)));
        }
        catch
        {
            return fallback;
        }
    }

    private void AttachFolderContextMenu(TreeViewItem tvi, TakeoffFolderNode folder)
    {
        var menu = new ContextMenu();
        int selectedCount = TakeoffSelectionCount(tvi);
        bool singleSelection = selectedCount <= 1;
        bool canEditFolder = !folder.IsRoot && singleSelection;

        var newFolder = new MenuItem { Header = "New Folder" };
        newFolder.Click += (_, _) =>
        {
            tvi.IsSelected = true;
            BtnNewTakeoffFolder_Click(tvi, new RoutedEventArgs());
        };
        menu.Items.Add(newFolder);

        var newItem = new MenuItem { Header = "New Item" };
        newItem.Click += (_, _) =>
        {
            tvi.IsSelected = true;
            BtnNewItem_Click(tvi, new RoutedEventArgs());
        };
        menu.Items.Add(newItem);

        menu.Items.Add(MakeMenuItem("Auto Create Tree", true, () => AutoCreateTakeoffTree(folder.FolderPath)));
        menu.Items.Add(MakeMenuItem("Create Folders From Pages", true, () => AutoCreateTakeoffFoldersFromPages(folder.FolderPath)));

        menu.Items.Add(new Separator());

        var rename = new MenuItem { Header = "Rename Folder…" };
        menu.Items.Add(MakeMenuItem(selectedCount > 1 ? "Copy Selected" : "Copy Folder", !folder.IsRoot, () => CopyCutTakeoffNode(tvi, TakeoffsClipboardMode.Copy)));
        menu.Items.Add(MakeMenuItem(selectedCount > 1 ? "Cut Selected" : "Cut Folder", !folder.IsRoot, () => CopyCutTakeoffNode(tvi, TakeoffsClipboardMode.Cut)));
        menu.Items.Add(MakeMenuItem("Paste Into Folder", CanPasteTakeoffsInto(folder.FolderPath), () => PasteIntoSelectedTakeoffTarget(tvi)));
        menu.Items.Add(MakeMenuItem(selectedCount > 1 ? "Duplicate Selected" : "Duplicate Folder", !folder.IsRoot, () => DuplicateTakeoffNode(tvi)));

        menu.Items.Add(new Separator());

        menu.Items.Add(MakeMenuItem("Folder Properties...", canEditFolder, () => EditTakeoffFolderProperties(tvi, folder)));
        int nestedTakeoffCount = TakeoffItemsForSelection(tvi).Count;
        menu.Items.Add(MakeMenuItem(
            nestedTakeoffCount > 1 ? $"Bulk Item Properties ({nestedTakeoffCount})..." : "Bulk Item Properties...",
            nestedTakeoffCount > 0,
            () => EditSelectedTakeoffProperties(tvi)));

        rename.Click += (_, _) => RenameTakeoffFolder(tvi, folder);
        rename.IsEnabled = canEditFolder;
        menu.Items.Add(rename);

        var moveUp = new MenuItem
        {
            Header = selectedCount > 1 ? $"Move {selectedCount} Up" : "Move Up",
            IsEnabled = CanMoveTakeoffNodes(tvi, -1),
        };
        moveUp.Click += (_, _) => MoveTakeoffNodes(tvi, -1);
        menu.Items.Add(moveUp);

        var moveDown = new MenuItem
        {
            Header = selectedCount > 1 ? $"Move {selectedCount} Down" : "Move Down",
            IsEnabled = CanMoveTakeoffNodes(tvi, 1),
        };
        moveDown.Click += (_, _) => MoveTakeoffNodes(tvi, 1);
        menu.Items.Add(moveDown);

        menu.Items.Add(new Separator());

        var sortAz = new MenuItem { Header = "Sort Children A-Z" };
        sortAz.Click += (_, _) => SortTakeoffChildren(folder.FolderPath, descending: false);
        menu.Items.Add(sortAz);

        var sortZa = new MenuItem { Header = "Sort Children Z-A" };
        sortZa.Click += (_, _) => SortTakeoffChildren(folder.FolderPath, descending: true);
        menu.Items.Add(sortZa);

        menu.Items.Add(new Separator());

        var open = new MenuItem { Header = "Open in Explorer" };
        open.Click += (_, _) => OpenFolderInExplorer(folder.FolderPath);
        menu.Items.Add(open);

        var delete = new MenuItem
        {
            Header = selectedCount > 1 ? "Delete selected takeoffs" : "Delete folder + children",
            IsEnabled = !folder.IsRoot,
        };
        delete.Click += (_, _) => DeleteTakeoffNodes(tvi);
        menu.Items.Add(delete);

        tvi.ContextMenu = menu;
    }

    private ContextMenu BuildTakeoffsRootContextMenu()
    {
        var menu = new ContextMenu();

        menu.Items.Add(MakeMenuItem(
            "Auto Create Tree",
            _currentJob != null,
            () =>
            {
                if (_currentJob != null)
                    AutoCreateTakeoffTree(_currentJob.TakeoffsRoot);
            }));
        menu.Items.Add(MakeMenuItem(
            "Create Folders From Pages",
            _currentJob != null,
            () =>
            {
                if (_currentJob != null)
                    AutoCreateTakeoffFoldersFromPages(_currentJob.TakeoffsRoot);
            }));

        menu.Items.Add(new Separator());

        menu.Items.Add(MakeMenuItem(
            "Paste Into Root",
            _currentJob != null && CanPasteTakeoffsInto(_currentJob.TakeoffsRoot),
            () =>
            {
                if (_currentJob != null)
                    PasteTakeoffsIntoFolder(_currentJob.TakeoffsRoot);
            }));

        menu.Items.Add(new Separator());

        var sortAz = new MenuItem { Header = "Sort Root A-Z" };
        sortAz.Click += (_, _) =>
        {
            if (_currentJob != null)
                SortTakeoffChildren(_currentJob.TakeoffsRoot, descending: false);
        };
        menu.Items.Add(sortAz);

        var sortZa = new MenuItem { Header = "Sort Root Z-A" };
        sortZa.Click += (_, _) =>
        {
            if (_currentJob != null)
                SortTakeoffChildren(_currentJob.TakeoffsRoot, descending: true);
        };
        menu.Items.Add(sortZa);

        menu.Items.Add(new Separator());

        var open = new MenuItem { Header = "Open Takeoffs in Explorer" };
        open.Click += (_, _) =>
        {
            if (_currentJob != null)
                OpenFolderInExplorer(_currentJob.TakeoffsRoot);
        };
        menu.Items.Add(open);

        return menu;
    }

    private void RenameItem(TreeViewItem tvi, TakeoffItem item)
    {
        string? name = ShowInputDialog("New name:", item.Name, "Rename Item");
        if (name == null || name == item.Name) return;
        try
        {
            if (!string.IsNullOrWhiteSpace(item.FolderPath) && Directory.Exists(item.FolderPath))
            {
                string oldPath = item.FolderPath;
                item.FolderPath = SmartTakeoffsJobStore.RenameNode(item.FolderPath, name);
                RebasePageLegendTakeoffOrderReferences(oldPath, item.FolderPath);
                item.Name = SmartTakeoffsJobStore.DisplayName(item.FolderPath);
                foreach (var measurement in item.Measurements)
                    measurement.TakeoffFolder = item.FolderPath;
                SmartTakeoffsJobStore.SaveTakeoffItem(item);
            }
            else
            {
                item.Name = SmartTakeoffsJobStore.SanitizeName(name, 120);
            }

            SetTreeItemHeader(tvi, item);
            RefreshPagesTakeoffIndicators();
            RefreshSheetLegend();
            UpdateTotalDisplay();
        }
        catch (Exception ex)
        {
            ShowOperationError("Rename Takeoff Item", ex);
        }
    }

    private void DeleteItem(TreeViewItem tvi, TakeoffItem item)
    {
        var res = MessageBox.Show(
            $"Delete \"{item.Name}\" and all its measurements?",
            "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;

        try
        {
            if (!string.IsNullOrWhiteSpace(item.FolderPath) && Directory.Exists(item.FolderPath))
                DeleteDirectoryToRecycle(item.FolderPath);
        }
        catch (Exception ex)
        {
            ShowOperationError("Delete Takeoff Item", ex);
            return;
        }

        _viewport.DeleteMeasurements(item.Measurements);
        _takeoffItems.Remove(item);
        RemoveTreeItem(tvi);

        if (ReferenceEquals(_activeItem, item))
        {
            _activeItem = null;
            SelectFirstTakeoffItem();
        }
        UpdateTotalDisplay();
    }

    private void EditTakeoffFolderProperties(TreeViewItem tvi, TakeoffFolderNode folder)
    {
        if (_currentJob == null || !Directory.Exists(folder.FolderPath))
            return;

        TakeoffFolderProperties properties = TakeoffFolderPropertiesStore.Load(folder.FolderPath);
        var dialog = new TakeoffFolderPropertiesDialog(folder.Name, properties)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            string oldPath = folder.FolderPath;
            string newPath = folder.FolderPath;
            string requestedName = dialog.FolderName.Trim();
            if (!string.IsNullOrWhiteSpace(requestedName) &&
                !string.Equals(requestedName, folder.Name, StringComparison.Ordinal))
            {
                newPath = SmartTakeoffsJobStore.RenameNode(folder.FolderPath, requestedName);
                RebasePageLegendTakeoffOrderReferences(oldPath, newPath);
                foreach (var item in _takeoffItems)
                {
                    if (!SmartTakeoffsJobStore.IsSameOrDescendant(oldPath, item.FolderPath))
                        continue;

                    item.FolderPath = Path.Combine(newPath, Path.GetRelativePath(oldPath, item.FolderPath));
                    foreach (var measurement in item.Measurements)
                        measurement.TakeoffFolder = item.FolderPath;
                }
            }

            var updatedFolder = new TakeoffFolderNode
            {
                Name = SmartTakeoffsJobStore.DisplayName(newPath),
                FolderPath = newPath,
            };
            var updatedProperties = new TakeoffFolderProperties
            {
                DisplayName = updatedFolder.Name,
                Notes = dialog.Notes,
                DefaultColor = dialog.DefaultColor,
                DefaultMeasurementType = dialog.DefaultMeasurementType,
                DefaultUnitPrice = dialog.DefaultUnitPrice,
                DefaultItemNotes = dialog.DefaultItemNotes,
                DefaultNamePrefix = dialog.DefaultNamePrefix,
            };
            TakeoffFolderPropertiesStore.Save(newPath, updatedProperties);

            tvi.Tag = updatedFolder;
            SetFolderTreeItemHeader(tvi, updatedFolder);
            AttachFolderContextMenu(tvi, updatedFolder);
            _activeTakeoffParentFolder = newPath;
            TxtStatus.Text = TakeoffFolderStatusText(updatedFolder);
        }
        catch (Exception ex)
        {
            ShowOperationError("Takeoff Folder Properties", ex);
        }
    }

    private void RenameTakeoffFolder(TreeViewItem tvi, TakeoffFolderNode folder)
    {
        string? name = ShowInputDialog("New name:", "Rename Folder", folder.Name);
        if (name == null || name == folder.Name) return;

        try
        {
            string oldPath = folder.FolderPath;
            string newPath = SmartTakeoffsJobStore.RenameNode(folder.FolderPath, name);
            RebasePageLegendTakeoffOrderReferences(oldPath, newPath);
            folder = new TakeoffFolderNode
            {
                Name = SmartTakeoffsJobStore.DisplayName(newPath),
                FolderPath = newPath,
            };
            tvi.Tag = folder;
            SetFolderTreeItemHeader(tvi, folder);
            AttachFolderContextMenu(tvi, folder);

            foreach (var item in _takeoffItems)
            {
                if (!SmartTakeoffsJobStore.IsSameOrDescendant(oldPath, item.FolderPath))
                    continue;

                item.FolderPath = Path.Combine(newPath, Path.GetRelativePath(oldPath, item.FolderPath));
                foreach (var measurement in item.Measurements)
                    measurement.TakeoffFolder = item.FolderPath;
            }

            _activeTakeoffParentFolder = newPath;
        }
        catch (Exception ex)
        {
            ShowOperationError("Rename Takeoff Folder", ex);
        }
    }

    private void DeleteTakeoffFolder(TreeViewItem tvi, TakeoffFolderNode folder)
    {
        var res = MessageBox.Show(
            $"Delete folder \"{folder.Name}\" and all child takeoffs?",
            "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;

        var removedItems = _takeoffItems
            .Where(i => SmartTakeoffsJobStore.IsSameOrDescendant(folder.FolderPath, i.FolderPath))
            .ToList();

        try
        {
            DeleteDirectoryToRecycle(folder.FolderPath);
        }
        catch (Exception ex)
        {
            ShowOperationError("Delete Takeoff Folder", ex);
            return;
        }

        foreach (var item in removedItems)
        {
            _viewport.DeleteMeasurements(item.Measurements);
            _takeoffItems.Remove(item);
        }
        RemoveTreeItem(tvi);

        if (_activeItem != null && removedItems.Contains(_activeItem))
            SelectFirstTakeoffItem();
        else
            UpdateTotalDisplay();
    }

    private void DeleteTakeoffNodes(TreeViewItem anchor)
    {
        if (_currentJob == null)
            return;

        var entries = GetSelectedTakeoffEntries(anchor);
        if (entries.Count == 0)
            return;

        string message = entries.Count == 1
            ? $"Delete \"{SmartTakeoffsJobStore.DisplayName(entries[0].SourcePath)}\" and all contained measurements?"
            : $"Delete {entries.Count} selected takeoff node(s) and all contained measurements?";
        var res = MessageBox.Show(message, "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes)
            return;

        try
        {
            FlushTakeoffAutosaves();
            var removedItems = _takeoffItems
                .Where(item => entries.Any(entry =>
                    SmartTakeoffsJobStore.IsSameOrDescendant(entry.SourcePath, item.FolderPath)))
                .ToList();

            foreach (var entry in entries)
            {
                if (Directory.Exists(entry.SourcePath))
                    DeleteDirectoryToRecycle(entry.SourcePath);
            }

            foreach (var item in removedItems)
                _takeoffItems.Remove(item);

            if (_activeItem != null && removedItems.Contains(_activeItem))
                _activeItem = null;

            if (_takeoffsClipboard != null && entries.Any(entry =>
                    _takeoffsClipboard.Entries.Any(clip =>
                        SmartTakeoffsJobStore.IsSameOrDescendant(entry.SourcePath, clip.SourcePath))))
                _takeoffsClipboard = null;

            _takeoffsMultiSelection.Clear();
            LoadTakeoffsForJob();
            _viewport.SetMeasurements(_takeoffItems.SelectMany(item => item.Measurements));
            RefreshPagesTakeoffIndicators();
            RefreshEstimateTable();
            UpdateTotalDisplay();
            TxtStatus.Text = entries.Count == 1
                ? $"Deleted: {SmartTakeoffsJobStore.DisplayName(entries[0].SourcePath)}"
                : $"Deleted {entries.Count} takeoff nodes.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Delete Takeoffs", ex);
        }
    }

    private void MoveTakeoffNode(string folderPath, int offset)
    {
        if (FindTakeoffTreeItemByFolder(folderPath) is { } item)
        {
            MoveTakeoffNodes(item, offset);
            return;
        }

        try
        {
            if (!SmartTakeoffsJobStore.MoveSibling(folderPath, offset))
                return;
            LoadTakeoffsForJob();
        }
        catch (Exception ex)
        {
            ShowOperationError("Move Takeoff Node", ex);
        }
    }

    private bool CanMoveTakeoffNodes(TreeViewItem anchor, int offset)
    {
        var paths = GetSelectedTakeoffEntries(anchor)
            .Select(entry => entry.SourcePath)
            .ToList();
        return SmartTakeoffsJobStore.CanMoveSiblings(paths, offset);
    }

    private void MoveTakeoffNodes(TreeViewItem anchor, int offset)
    {
        var entries = GetSelectedTakeoffEntries(anchor);
        var paths = entries.Select(entry => entry.SourcePath).ToList();
        if (paths.Count == 0)
            return;

        try
        {
            if (!SmartTakeoffsJobStore.MoveSiblings(paths, offset))
                return;

            LoadTakeoffsForJob();
            SetTakeoffMultiSelection(paths);
            SelectFirstTakeoffPath(paths);
            TxtStatus.Text = paths.Count == 1
                ? (offset < 0 ? "Moved takeoff node up." : "Moved takeoff node down.")
                : (offset < 0 ? $"Moved {paths.Count} takeoff nodes up." : $"Moved {paths.Count} takeoff nodes down.");
        }
        catch (Exception ex)
        {
            ShowOperationError(offset < 0 ? "Move Takeoff Nodes Up" : "Move Takeoff Nodes Down", ex);
        }
    }

    private void SortTakeoffChildren(string folderPath, bool descending)
    {
        try
        {
            SmartTakeoffsJobStore.SortChildren(folderPath, descending);
            LoadTakeoffsForJob();
        }
        catch (Exception ex)
        {
            ShowOperationError("Sort Takeoffs", ex);
        }
    }

    private void TakeoffsTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is { } item)
        {
            if (item.Tag is TakeoffMeasurementNode sectionNode)
            {
                string key = TakeoffSectionSelectionKey(sectionNode);
                _takeoffsMultiSelection.Clear();
                if (!_takeoffSectionMultiSelection.Contains(key))
                {
                    _takeoffSectionMultiSelection.Clear();
                    _takeoffSectionMultiSelection.Add(key);
                    _takeoffSectionRangeAnchorKey = key;
                    _takeoffsMultiSelection.Clear();
                    ApplyTakeoffPageHighlights();
                }

                item.Focus();
                item.IsSelected = true;
                item.ContextMenu = BuildTakeoffSectionContextMenu(sectionNode);
                e.Handled = true;
                return;
            }

            string? path = GetTakeoffNodePath(item);
            if (path != null)
                _takeoffSectionMultiSelection.Clear();
            if (path != null && !_takeoffsMultiSelection.Contains(path))
            {
                _takeoffsMultiSelection.Clear();
                _takeoffSectionMultiSelection.Clear();
                _takeoffsMultiSelection.Add(path);
                _takeoffsRangeAnchorPath = path;
                ApplyTakeoffPageHighlights();
            }
            if (path != null)
                RevealPagesForTakeoffSelection(item);

            item.Focus();
            item.IsSelected = true;
            RefreshTakeoffNodeContextMenu(item);
            e.Handled = true;
        }
    }

    private void TakeoffsTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is { } item)
        {
            RefreshTakeoffNodeContextMenu(item);
            return;
        }

        TakeoffsTree.ContextMenu = BuildTakeoffsRootContextMenu();
    }

    private void RefreshTakeoffNodeContextMenu(TreeViewItem item)
    {
        switch (item.Tag)
        {
            case TakeoffItem takeoff:
                AttachContextMenu(item, takeoff);
                break;
            case TakeoffFolderNode folder:
                AttachFolderContextMenu(item, folder);
                break;
        }
    }

    private void TakeoffsTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _takeoffsDragStart = e.GetPosition(TakeoffsTree);
        _takeoffsDragItem = null;
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is not { } item)
            return;

        _takeoffsDragItem = item;

        if (item.Tag is TakeoffMeasurementNode sectionNode)
        {
            HandleTakeoffSectionNodeMultiSelect(item, sectionNode, e);
            return;
        }

        string? path = GetTakeoffNodePath(item);
        if (path == null)
            return;

        ModifierKeys modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None &&
            _takeoffsMultiSelection.Count > 1 &&
            _takeoffsMultiSelection.Contains(path))
        {
            _takeoffSectionMultiSelection.Clear();
            item.IsSelected = true;
            ApplyTakeoffPageHighlights();
            RevealPagesForTakeoffSelection(item);
            Dispatcher.InvokeAsync(() => SelectTakeoffSelectionMeasurementsOnCurrentPage(item));
            return;
        }

        if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            bool additive = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            SelectTakeoffsRange(_takeoffsRangeAnchorPath, path, additive);
            _takeoffsRangeAnchorPath = path;
            _takeoffSectionMultiSelection.Clear();
            item.IsSelected = true;
            ApplyTakeoffPageHighlights();
            RevealPagesForTakeoffSelection(item);
            Dispatcher.InvokeAsync(() => SelectTakeoffSelectionMeasurementsOnCurrentPage(item));
            e.Handled = true;
            return;
        }

        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (!_takeoffsMultiSelection.Add(path))
                _takeoffsMultiSelection.Remove(path);
            _takeoffsRangeAnchorPath = path;
            _takeoffSectionMultiSelection.Clear();
            item.IsSelected = true;
            ApplyTakeoffPageHighlights();
            RevealPagesForTakeoffSelection(item);
            Dispatcher.InvokeAsync(() => SelectTakeoffSelectionMeasurementsOnCurrentPage(item));
            e.Handled = true;
            return;
        }

        if (!_takeoffsMultiSelection.SetEquals([path]))
        {
            _takeoffsMultiSelection.Clear();
            _takeoffsMultiSelection.Add(path);
            _takeoffSectionMultiSelection.Clear();
            ApplyTakeoffPageHighlights();
        }
        _takeoffsRangeAnchorPath = path;
        RevealPagesForTakeoffSelection(item);
        Dispatcher.InvokeAsync(() => SelectTakeoffSelectionMeasurementsOnCurrentPage(item));
    }

    private void HandleTakeoffSectionNodeMultiSelect(TreeViewItem item, TakeoffMeasurementNode node, MouseButtonEventArgs e)
    {
        string key = TakeoffSectionSelectionKey(node);
        ModifierKeys modifiers = Keyboard.Modifiers;
        _takeoffsMultiSelection.Clear();

        if (modifiers == ModifierKeys.None &&
            _takeoffSectionMultiSelection.Count > 1 &&
            _takeoffSectionMultiSelection.Contains(key))
        {
            item.IsSelected = true;
            ApplyTakeoffPageHighlights();
            Dispatcher.InvokeAsync(() => SelectTakeoffSectionMeasurementsOnCanvas(SelectedTakeoffSectionNodes(node, fallbackToAnchor: false)));
            return;
        }

        if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            bool additive = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            SelectTakeoffSectionRange(_takeoffSectionRangeAnchorKey, key, node.Item, additive);
            _takeoffSectionRangeAnchorKey = key;
            item.IsSelected = true;
            ApplyTakeoffPageHighlights();
            Dispatcher.InvokeAsync(() => SelectTakeoffSectionMeasurementsOnCanvas(SelectedTakeoffSectionNodes(node, fallbackToAnchor: false)));
            e.Handled = true;
            return;
        }

        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (!_takeoffSectionMultiSelection.Add(key))
                _takeoffSectionMultiSelection.Remove(key);
            _takeoffSectionRangeAnchorKey = key;
            item.IsSelected = true;
            ApplyTakeoffPageHighlights();
            Dispatcher.InvokeAsync(() => SelectTakeoffSectionMeasurementsOnCanvas(SelectedTakeoffSectionNodes(node, fallbackToAnchor: false)));
            e.Handled = true;
            return;
        }

        _takeoffSectionMultiSelection.Clear();
        _takeoffSectionMultiSelection.Add(key);
        _takeoffSectionRangeAnchorKey = key;
        ApplyTakeoffPageHighlights();
        Dispatcher.InvokeAsync(() => SelectTakeoffSectionMeasurementsOnCanvas(SelectedTakeoffSectionNodes(node, fallbackToAnchor: true)));
    }

    private void SelectTakeoffsRange(string? anchorPath, string targetPath, bool additive)
    {
        var candidates = EnumerateVisibleTreeItems(TakeoffsTree)
            .Select(item => (Item: item, Key: GetTakeoffNodePath(item)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .Select(entry => (entry.Item, Key: entry.Key!))
            .ToList();

        SelectRangeKeys(candidates, anchorPath, targetPath, _takeoffsMultiSelection, additive);
    }

    private void TakeoffsTree_MouseMove(object sender, MouseEventArgs e)
    {
        if (_takeoffsDragStart == null || e.LeftButton != MouseButtonState.Pressed)
            return;

        Point pos = e.GetPosition(TakeoffsTree);
        if (Math.Abs(pos.X - _takeoffsDragStart.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _takeoffsDragStart.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if ((_takeoffsDragItem ?? TakeoffsTree.SelectedItem) is not TreeViewItem item)
            return;

        if (item.Tag is TakeoffMeasurementNode sectionNode)
        {
            var nodes = SelectedTakeoffSectionNodes(sectionNode, fallbackToAnchor: true);
            if (nodes.Count == 0)
            {
                _takeoffsDragStart = null;
                _takeoffsDragItem = null;
                return;
            }

            var sectionPayload = new TakeoffSectionDrag(nodes);
            DragDrop.DoDragDrop(TakeoffsTree, sectionPayload, DragDropEffects.Move | DragDropEffects.Copy);
            ClearTakeoffSectionDropCue();
            ClearTakeoffPositionDropCue();
            _takeoffsDragStart = null;
            _takeoffsDragItem = null;
            return;
        }

        var entries = GetSelectedTakeoffEntries(item);
        if (entries.Count == 0)
        {
            _takeoffsDragStart = null;
            _takeoffsDragItem = null;
            return;
        }

        var payload = new TakeoffsClipboard(entries, TakeoffsClipboardMode.Cut);
        DragDrop.DoDragDrop(TakeoffsTree, payload, DragDropEffects.Move | DragDropEffects.Copy);
        ClearTakeoffPositionDropCue();
        _takeoffsDragStart = null;
        _takeoffsDragItem = null;
    }

    private void TakeoffsTree_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        TreeViewItem? targetItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        bool copy = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;
        if (e.Data.GetData(typeof(TakeoffSectionDrag)) is TakeoffSectionDrag sectionDrag)
        {
            ClearTakeoffPositionDropCue();
            bool canDropSection = CanDropTakeoffSections(sectionDrag, targetItem, copy);
            UpdateTakeoffSectionDropCue(sectionDrag, targetItem, copy, canDropSection);
            if (canDropSection)
                e.Effects = copy ? DragDropEffects.Copy : DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        ClearTakeoffSectionDropCue();
        if (e.Data.GetData(typeof(TakeoffsClipboard)) is not TakeoffsClipboard payload)
            return;

        if (TryGetTakeoffPositionDropCue(payload, targetItem, copy, e, out bool after, out bool canDropPosition, out string positionStatus))
        {
            UpdateTakeoffPositionDropCue(targetItem, after, canDropPosition, positionStatus);
            if (canDropPosition)
                e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        ClearTakeoffPositionDropCue();
        string? targetFolder = targetItem == null ? _currentJob?.TakeoffsRoot : GetTakeoffPasteTargetFolder(targetItem);
        if (CanDropTakeoffsInto(payload, targetFolder, copy ? TakeoffsClipboardMode.Copy : TakeoffsClipboardMode.Cut))
            e.Effects = copy ? DragDropEffects.Copy : DragDropEffects.Move;
        e.Handled = true;
    }

    private void TakeoffsTree_Drop(object sender, DragEventArgs e)
    {
        TreeViewItem? targetItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        bool copy = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;
        if (e.Data.GetData(typeof(TakeoffSectionDrag)) is TakeoffSectionDrag sectionDrag)
        {
            ClearTakeoffPositionDropCue();
            if (CanDropTakeoffSections(sectionDrag, targetItem, copy))
                DropTakeoffSections(sectionDrag, targetItem!, copy);
            ClearTakeoffSectionDropCue();
            e.Handled = true;
            return;
        }

        ClearTakeoffSectionDropCue();
        if (e.Data.GetData(typeof(TakeoffsClipboard)) is not TakeoffsClipboard payload)
            return;

        if (TryGetTakeoffPositionDropCue(payload, targetItem, copy, e, out bool after, out bool canDropPosition, out _) &&
            canDropPosition &&
            targetItem != null)
        {
            DropTakeoffPosition(payload, targetItem, after);
            ClearTakeoffPositionDropCue();
            e.Handled = true;
            return;
        }

        ClearTakeoffPositionDropCue();
        string? targetFolder = targetItem == null ? _currentJob?.TakeoffsRoot : GetTakeoffPasteTargetFolder(targetItem);
        TakeoffsClipboardMode mode = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey
            ? TakeoffsClipboardMode.Copy
            : TakeoffsClipboardMode.Cut;
        if (CanDropTakeoffsInto(payload, targetFolder, mode))
            RunTakeoffDrop(payload, targetFolder!, mode);
        e.Handled = true;
    }

    private void TakeoffsTree_DragLeave(object sender, DragEventArgs e)
    {
        if (!TakeoffsTree.IsMouseOver)
        {
            ClearTakeoffSectionDropCue();
            ClearTakeoffPositionDropCue();
        }
    }

    private void TakeoffsTree_KeyDown(object sender, KeyEventArgs e)
    {
        if (TakeoffsTree.SelectedItem is not TreeViewItem item) return;
        if (item.Tag is TakeoffMeasurementNode sectionNode)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Up)
            {
                MoveTakeoffSections(sectionNode, -1);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Down)
            {
                MoveTakeoffSections(sectionNode, 1);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Delete)
            {
                DeleteTakeoffSections(sectionNode);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.F2 &&
                     SelectedTakeoffSectionNodes(sectionNode, fallbackToAnchor: true).Count <= 1)
            {
                RenameSection(sectionNode.Item, sectionNode.Measurement);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter)
            {
                SelectTakeoffSectionMeasurementsOnCanvas(SelectedTakeoffSectionNodes(sectionNode, fallbackToAnchor: true));
                e.Handled = true;
            }
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.C)
        {
            CopyCutTakeoffNode(item, TakeoffsClipboardMode.Copy);
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.X)
        {
            CopyCutTakeoffNode(item, TakeoffsClipboardMode.Cut);
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.V)
        {
            PasteIntoSelectedTakeoffTarget(item);
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.D)
        {
            DuplicateTakeoffNode(item);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Up)
        {
            MoveTakeoffNodes(item, -1);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Down)
        {
            MoveTakeoffNodes(item, 1);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Delete)
        {
            DeleteTakeoffNodes(item);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.F2 && TakeoffSelectionCount(item) <= 1)
        {
            if (item.Tag is TakeoffItem takeoff)
                RenameItem(item, takeoff);
            else if (item.Tag is TakeoffFolderNode folder && !folder.IsRoot)
                RenameTakeoffFolder(item, folder);
            e.Handled = true;
        }
    }

    private void CopyCutTakeoffNode(TreeViewItem item, TakeoffsClipboardMode mode)
    {
        var entries = GetSelectedTakeoffEntries(item);
        if (entries.Count == 0) return;

        _takeoffsClipboard = new TakeoffsClipboard(entries, mode);
        string verb = mode == TakeoffsClipboardMode.Copy ? "Copied" : "Cut";
        TxtStatus.Text = entries.Count == 1
            ? $"{verb}: {SmartTakeoffsJobStore.DisplayName(entries[0].SourcePath)}"
            : $"{verb} {entries.Count} takeoff nodes.";
    }

    private void PasteIntoSelectedTakeoffTarget(TreeViewItem item)
    {
        string? targetFolder = GetTakeoffPasteTargetFolder(item);
        if (targetFolder != null)
            PasteTakeoffsIntoFolder(targetFolder);
    }

    private void PasteTakeoffsIntoFolder(string targetFolder)
    {
        if (_takeoffsClipboard == null || !CanDropTakeoffsInto(_takeoffsClipboard, targetFolder, _takeoffsClipboard.Mode))
            return;

        RunTakeoffDrop(_takeoffsClipboard, targetFolder, _takeoffsClipboard.Mode);
    }

    private void DuplicateTakeoffNode(TreeViewItem item)
    {
        var entries = GetSelectedTakeoffEntries(item);
        if (entries.Count == 0) return;

        try
        {
            FlushTakeoffAutosaves();
            var changed = new List<string>();
            foreach (var entry in entries)
            {
                string? parent = Path.GetDirectoryName(entry.SourcePath);
                if (string.IsNullOrWhiteSpace(parent) ||
                    !CanDropTakeoffsInto(new TakeoffsClipboard([entry], TakeoffsClipboardMode.Copy), parent, TakeoffsClipboardMode.Copy))
                {
                    continue;
                }

                changed.Add(SmartTakeoffsJobStore.CopyNode(entry.SourcePath, parent));
            }

            if (changed.Count == 0)
                return;

            LoadTakeoffsForJob();
            SetTakeoffMultiSelection(changed);
            SelectFirstTakeoffPath(changed);
            TxtStatus.Text = changed.Count == 1
                ? $"Duplicated: {SmartTakeoffsJobStore.DisplayName(changed[0])}"
                : $"Duplicated {changed.Count} takeoff nodes.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Duplicate Takeoffs", ex);
        }
    }

    private void RunTakeoffDrop(TakeoffsClipboard payload, string targetFolder, TakeoffsClipboardMode mode)
    {
        bool wasCut = mode == TakeoffsClipboardMode.Cut;
        try
        {
            FlushTakeoffAutosaves();
            var changed = new List<string>();
            var rebasedLegendPaths = new List<(string OldPath, string NewPath)>();
            foreach (var entry in payload.Entries)
            {
                if (!CanDropTakeoffsInto(new TakeoffsClipboard([entry], mode), targetFolder, mode))
                    continue;

                string changedPath = wasCut
                    ? SmartTakeoffsJobStore.MoveNode(entry.SourcePath, targetFolder)
                    : SmartTakeoffsJobStore.CopyNode(entry.SourcePath, targetFolder);
                changed.Add(changedPath);
                if (wasCut)
                    rebasedLegendPaths.Add((entry.SourcePath, changedPath));
            }

            if (changed.Count == 0)
                return;

            if (wasCut)
                _takeoffsClipboard = null;

            foreach (var (oldPath, newPath) in rebasedLegendPaths)
                RebasePageLegendTakeoffOrderReferences(oldPath, newPath);

            LoadTakeoffsForJob();
            SetTakeoffMultiSelection(changed);
            SelectFirstTakeoffPath(changed);
            TxtStatus.Text = changed.Count == 1
                ? $"{(wasCut ? "Moved" : "Pasted")}: {SmartTakeoffsJobStore.DisplayName(changed[0])}"
                : $"{(wasCut ? "Moved" : "Pasted")} {changed.Count} takeoff nodes.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Takeoff Paste", ex);
        }
    }

    private bool TryGetTakeoffPositionDropCue(
        TakeoffsClipboard payload,
        TreeViewItem? targetItem,
        bool copy,
        DragEventArgs e,
        out bool after,
        out bool canDrop,
        out string status)
    {
        after = false;
        canDrop = false;
        status = "";

        if (copy || _currentJob == null || payload.Entries.Count == 0 || targetItem == null)
            return false;

        string? targetPath = GetTakeoffNodePath(targetItem);
        if (string.IsNullOrWhiteSpace(targetPath) || !Directory.Exists(targetPath))
            return false;

        Point targetPoint = e.GetPosition(targetItem);
        if (targetItem.Tag is TakeoffFolderNode && !IsTakeoffPositionEdgeDrop(targetItem, targetPoint))
            return false;

        after = IsTakeoffPositionDropAfter(targetItem, targetPoint);
        var paths = payload.Entries.Select(entry => entry.SourcePath).ToList();
        canDrop = CanDropTakeoffsToPosition(payload, targetPath, after);
        string targetName = SmartTakeoffsJobStore.DisplayName(targetPath);
        string position = after ? "after" : "before";
        status = canDrop
            ? $"Move {paths.Count} takeoff node(s) {position} {targetName}."
            : $"Cannot reorder here. Drag onto another sibling position in the same folder.";
        return true;
    }

    private bool CanDropTakeoffsToPosition(TakeoffsClipboard payload, string targetPath, bool after)
    {
        if (_currentJob == null || payload.Entries.Count == 0 || string.IsNullOrWhiteSpace(targetPath))
            return false;

        string targetParent = Path.GetDirectoryName(targetPath) ?? "";
        if (string.IsNullOrWhiteSpace(targetParent) ||
            !SmartTakeoffsJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, targetParent) ||
            !Directory.Exists(targetParent))
        {
            return false;
        }

        var paths = payload.Entries.Select(entry => entry.SourcePath).ToList();
        if (paths.Any(path => string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (paths.All(path => string.Equals(Path.GetDirectoryName(path) ?? "", targetParent, StringComparison.OrdinalIgnoreCase)))
            return SmartTakeoffsJobStore.CanMoveSiblingsToPosition(paths, targetPath, after);

        return CanDropTakeoffsInto(payload, targetParent, TakeoffsClipboardMode.Cut);
    }

    private static bool IsTakeoffPositionDropAfter(TreeViewItem item, Point targetPoint) =>
        targetPoint.Y >= TakeoffNodeHeaderDropHeight(item) / 2.0;

    private static bool IsTakeoffPositionEdgeDrop(TreeViewItem item, Point targetPoint)
    {
        double height = TakeoffNodeHeaderDropHeight(item);
        if (targetPoint.Y < 0 || targetPoint.Y > height)
            return false;

        double edge = Math.Min(8.0, Math.Max(5.0, height * 0.25));
        return targetPoint.Y <= edge || targetPoint.Y >= height - edge;
    }

    private static double TakeoffNodeHeaderDropHeight(TreeViewItem item)
    {
        double itemHeight = Math.Max(1.0, item.ActualHeight);
        if (item.Header is FrameworkElement header && header.ActualHeight > 0)
            return Math.Min(itemHeight, Math.Max(18.0, header.ActualHeight + 6.0));

        return Math.Min(itemHeight, 28.0);
    }

    private void UpdateTakeoffPositionDropCue(TreeViewItem? targetItem, bool after, bool canDrop, string status)
    {
        if (ReferenceEquals(_takeoffPositionDropTarget, targetItem) &&
            _takeoffPositionDropAfter == after &&
            _takeoffPositionDropAllowed == canDrop &&
            string.Equals(_takeoffPositionDropStatus, status, StringComparison.Ordinal))
        {
            return;
        }

        _takeoffPositionDropTarget = targetItem;
        _takeoffPositionDropAfter = after;
        _takeoffPositionDropAllowed = canDrop;
        _takeoffPositionDropStatus = status;
        ApplyTakeoffPageHighlights();
        if (!string.IsNullOrWhiteSpace(status))
            TxtStatus.Text = status;
    }

    private void ClearTakeoffPositionDropCue()
    {
        if (_takeoffPositionDropTarget == null && string.IsNullOrEmpty(_takeoffPositionDropStatus))
            return;

        _takeoffPositionDropTarget = null;
        _takeoffPositionDropAfter = false;
        _takeoffPositionDropAllowed = false;
        _takeoffPositionDropStatus = "";
        ApplyTakeoffPageHighlights();
    }

    private void DropTakeoffPosition(TakeoffsClipboard payload, TreeViewItem targetItem, bool after)
    {
        string? targetPath = GetTakeoffNodePath(targetItem);
        if (string.IsNullOrWhiteSpace(targetPath))
            return;

        var paths = payload.Entries.Select(entry => entry.SourcePath).ToList();
        if (paths.Count == 0)
            return;

        try
        {
            FlushTakeoffAutosaves();
            string targetParent = Path.GetDirectoryName(targetPath) ?? "";
            if (string.IsNullOrWhiteSpace(targetParent))
                return;

            var changed = new List<string>();
            var rebasedLegendPaths = new List<(string OldPath, string NewPath)>();
            if (paths.All(path => string.Equals(Path.GetDirectoryName(path) ?? "", targetParent, StringComparison.OrdinalIgnoreCase)))
            {
                if (!SmartTakeoffsJobStore.MoveSiblingsToPosition(paths, targetPath, after))
                    return;
                changed.AddRange(paths);
            }
            else
            {
                foreach (var entry in payload.Entries)
                {
                    if (string.Equals(Path.GetDirectoryName(entry.SourcePath) ?? "", targetParent, StringComparison.OrdinalIgnoreCase))
                    {
                        changed.Add(entry.SourcePath);
                        continue;
                    }

                    if (!CanDropTakeoffsInto(new TakeoffsClipboard([entry], TakeoffsClipboardMode.Cut), targetParent, TakeoffsClipboardMode.Cut))
                        continue;

                    string changedPath = SmartTakeoffsJobStore.MoveNode(entry.SourcePath, targetParent);
                    changed.Add(changedPath);
                    rebasedLegendPaths.Add((entry.SourcePath, changedPath));
                }

                if (changed.Count == 0 ||
                    !SmartTakeoffsJobStore.MoveSiblingsToPosition(changed, targetPath, after))
                {
                    return;
                }

                _takeoffsClipboard = null;
                foreach (var (oldPath, newPath) in rebasedLegendPaths)
                    RebasePageLegendTakeoffOrderReferences(oldPath, newPath);
            }

            LoadTakeoffsForJob();
            SetTakeoffMultiSelection(changed);
            SelectFirstTakeoffPath(changed);
            TxtStatus.Text = changed.Count == 1
                ? $"Moved takeoff node {(after ? "after" : "before")} {SmartTakeoffsJobStore.DisplayName(targetPath)}."
                : $"Moved {changed.Count} takeoff nodes {(after ? "after" : "before")} {SmartTakeoffsJobStore.DisplayName(targetPath)}.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Reorder Takeoffs", ex);
        }
    }

    private bool CanPasteTakeoffsInto(string? targetFolder) =>
        _takeoffsClipboard != null && CanDropTakeoffsInto(_takeoffsClipboard, targetFolder, _takeoffsClipboard.Mode);

    private bool CanDropTakeoffsInto(TakeoffsClipboard payload, string? targetFolder, TakeoffsClipboardMode mode)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(targetFolder) || payload.Entries.Count == 0)
            return false;
        if (!Directory.Exists(targetFolder))
            return false;

        if (!SmartTakeoffsJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, targetFolder) ||
            SmartTakeoffsJobStore.IsTakeoffItemFolder(targetFolder))
            return false;

        bool hasMovableEntry = false;
        foreach (var entry in payload.Entries)
        {
            if (!Directory.Exists(entry.SourcePath))
                return false;
            if (!SmartTakeoffsJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, entry.SourcePath) ||
                string.Equals(entry.SourcePath, _currentJob.TakeoffsRoot, StringComparison.OrdinalIgnoreCase))
                return false;

            if (SmartTakeoffsJobStore.IsSameOrDescendant(entry.SourcePath, targetFolder))
                return false;

            if (mode == TakeoffsClipboardMode.Cut)
            {
                string parent = Path.GetDirectoryName(entry.SourcePath) ?? "";
                if (string.Equals(parent, targetFolder, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            hasMovableEntry = true;
        }

        return mode == TakeoffsClipboardMode.Copy || hasMovableEntry;
    }

    private bool CanDropTakeoffSections(TakeoffSectionDrag payload, TreeViewItem? targetItem, bool copy)
    {
        if (payload.Nodes.Count == 0 || GetTakeoffSectionDropTarget(targetItem) is not { } target)
            return false;

        string targetType = SmartTakeoffsJobStore.NormalizeMeasurementType(target.MeasurementType);
        bool hasMovableNode = false;
        foreach (TakeoffMeasurementNode node in payload.Nodes)
        {
            if (!node.Item.Measurements.Contains(node.Measurement))
                return false;
            if (SmartTakeoffsJobStore.NormalizeMeasurementType(node.Measurement.MType) != targetType ||
                SmartTakeoffsJobStore.NormalizeMeasurementType(node.Item.MeasurementType) != targetType)
                return false;
            if (!ReferenceEquals(node.Item, target))
                hasMovableNode = true;
        }

        return copy || hasMovableNode;
    }

    private string TakeoffSectionDropStatus(TakeoffSectionDrag payload, TreeViewItem? targetItem, bool copy, bool canDrop)
    {
        if (payload.Nodes.Count == 0)
            return "Select section/count rows before dragging.";
        if (targetItem == null)
            return "Drop section/count rows on a takeoff item.";
        if (targetItem.Tag is TakeoffFolderNode)
            return "Drop section/count rows on a takeoff item, not a folder.";
        if (GetTakeoffSectionDropTarget(targetItem) is not { } target)
            return "Drop section/count rows on a takeoff item.";

        string action = copy ? "Copy" : "Move";
        string targetType = SmartTakeoffsJobStore.NormalizeMeasurementType(target.MeasurementType);
        TakeoffMeasurementNode? stale = payload.Nodes.FirstOrDefault(node => !node.Item.Measurements.Contains(node.Measurement));
        if (stale != null)
            return "Selected section/count row no longer exists.";

        TakeoffMeasurementNode? mismatch = payload.Nodes.FirstOrDefault(node =>
            SmartTakeoffsJobStore.NormalizeMeasurementType(node.Measurement.MType) != targetType ||
            SmartTakeoffsJobStore.NormalizeMeasurementType(node.Item.MeasurementType) != targetType);
        if (mismatch != null)
        {
            string sourceType = MeasurementTypeTitle(SmartTakeoffsJobStore.NormalizeMeasurementType(mismatch.Measurement.MType));
            string destinationType = MeasurementTypeTitle(targetType);
            return $"{action} blocked: {sourceType} rows can only drop on {sourceType} takeoff items, not {destinationType}.";
        }

        if (!copy && payload.Nodes.All(node => ReferenceEquals(node.Item, target)))
            return $"Already in {target.Name}. Hold Ctrl while dropping to copy.";

        return canDrop
            ? $"{action} {payload.Nodes.Count} section/count row(s) to {target.Name}."
            : $"Cannot {(copy ? "copy" : "move")} selected section/count rows to {target.Name}.";
    }

    private void UpdateTakeoffSectionDropCue(TakeoffSectionDrag payload, TreeViewItem? targetItem, bool copy, bool canDrop)
    {
        string status = TakeoffSectionDropStatus(payload, targetItem, copy, canDrop);
        if (ReferenceEquals(_takeoffSectionDropTarget, targetItem) &&
            _takeoffSectionDropAllowed == canDrop &&
            string.Equals(_takeoffSectionDropStatus, status, StringComparison.Ordinal))
        {
            return;
        }

        _takeoffSectionDropTarget = targetItem;
        _takeoffSectionDropAllowed = canDrop;
        _takeoffSectionDropStatus = status;
        ApplyTakeoffPageHighlights();
        if (!string.IsNullOrWhiteSpace(status))
            TxtStatus.Text = status;
    }

    private void ClearTakeoffSectionDropCue()
    {
        if (_takeoffSectionDropTarget == null && string.IsNullOrEmpty(_takeoffSectionDropStatus))
            return;

        _takeoffSectionDropTarget = null;
        _takeoffSectionDropAllowed = false;
        _takeoffSectionDropStatus = "";
        ApplyTakeoffPageHighlights();
    }

    private void DropTakeoffSections(TakeoffSectionDrag payload, TreeViewItem targetItem, bool copy)
    {
        if (!CanDropTakeoffSections(payload, targetItem, copy) ||
            GetTakeoffSectionDropTarget(targetItem) is not { } target)
        {
            return;
        }

        FlushTakeoffAutosaves();
        string targetType = SmartTakeoffsJobStore.NormalizeMeasurementType(target.MeasurementType);
        var changedItems = new HashSet<TakeoffItem>();
        var resultingNodes = new List<TakeoffMeasurementNode>();

        foreach (TakeoffMeasurementNode node in payload.Nodes
                     .GroupBy(node => node.Measurement.Id, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            if (copy)
            {
                Measurement copied = CloneMeasurementForTakeoff(node.Measurement, target, targetType);
                target.Measurements.Add(copied);
                changedItems.Add(target);
                resultingNodes.Add(new TakeoffMeasurementNode(target, copied));
                continue;
            }

            if (ReferenceEquals(node.Item, target))
                continue;

            if (!node.Item.Measurements.Remove(node.Measurement))
                continue;

            node.Measurement.TakeoffFolder = target.FolderPath;
            node.Measurement.MType = targetType;
            node.Measurement.Color = target.Color;
            target.Measurements.Add(node.Measurement);
            changedItems.Add(node.Item);
            changedItems.Add(target);
            resultingNodes.Add(new TakeoffMeasurementNode(target, node.Measurement));
        }

        if (resultingNodes.Count == 0)
            return;

        foreach (TakeoffItem changed in changedItems)
        {
            SmartTakeoffsJobStore.SaveTakeoffItem(changed);
            RefreshTreeItem(changed);
        }

        _viewport.LoadMeasurements(_takeoffItems.SelectMany(item => item.Measurements));
        SelectTakeoffSectionNodesSilently(resultingNodes);
        SelectTakeoffSectionMeasurementsOnCanvas(resultingNodes);
        RefreshPagesTakeoffIndicators();
        RefreshSheetLegend();
        RefreshEstimateTable();
        UpdateTotalDisplay();
        TxtStatus.Text = copy
            ? $"Copied {resultingNodes.Count} section/count row(s) to {target.Name}."
            : $"Moved {resultingNodes.Count} section/count row(s) to {target.Name}.";
    }

    private static Measurement CloneMeasurementForTakeoff(Measurement source, TakeoffItem target, string targetType) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            Name = source.Name,
            Notes = source.Notes,
            MType = targetType,
            Points = source.Points.ToList(),
            Color = target.Color,
            PageFolder = source.PageFolder,
            TakeoffFolder = target.FolderPath,
            ScaleMetersPerPt = source.ScaleMetersPerPt,
        };

    private static TakeoffItem? GetTakeoffSectionDropTarget(TreeViewItem? item) =>
        item?.Tag switch
        {
            TakeoffItem target => target,
            TakeoffMeasurementNode node => node.Item,
            _ => null,
        };

    private string? GetTakeoffPasteTargetFolder(TreeViewItem item)
    {
        return item.Tag switch
        {
            TakeoffFolderNode folder => folder.FolderPath,
            TakeoffItem takeoff => Path.GetDirectoryName(takeoff.FolderPath),
            _ => _currentJob?.TakeoffsRoot,
        };
    }

    private IReadOnlyList<TakeoffsClipboardEntry> GetSelectedTakeoffEntries(TreeViewItem anchor)
    {
        string? path = GetTakeoffNodePath(anchor);
        if (path == null || _currentJob == null || string.Equals(path, _currentJob.TakeoffsRoot, StringComparison.OrdinalIgnoreCase))
            return [];

        IEnumerable<string> paths = _takeoffsMultiSelection.Contains(path)
            ? _takeoffsMultiSelection
            : [path];

        var entries = paths
            .Where(Directory.Exists)
            .Where(candidate => _currentJob != null &&
                                SmartTakeoffsJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, candidate) &&
                                !string.Equals(candidate, _currentJob.TakeoffsRoot, StringComparison.OrdinalIgnoreCase))
            .Select(candidate => new TakeoffsClipboardEntry(
                candidate,
                SmartTakeoffsJobStore.IsTakeoffItemFolder(candidate)))
            .ToList();

        return NormalizeSelectedTakeoffEntries(entries);
    }

    private static string? GetTakeoffNodePath(TreeViewItem item) =>
        item.Tag switch
        {
            TakeoffItem takeoff => takeoff.FolderPath,
            TakeoffFolderNode folder => folder.IsRoot ? null : folder.FolderPath,
            _ => null,
        };

    private static string TakeoffSectionSelectionKey(TakeoffMeasurementNode node) =>
        $"{NormalizePath(node.Item.FolderPath)}|{node.Measurement.Id}";

    private static string? GetTakeoffSectionSelectionKey(TreeViewItem item) =>
        item.Tag is TakeoffMeasurementNode node ? TakeoffSectionSelectionKey(node) : null;

    private List<TakeoffMeasurementNode> SelectedTakeoffSectionNodes(TakeoffMeasurementNode anchor, bool fallbackToAnchor)
    {
        string anchorKey = TakeoffSectionSelectionKey(anchor);
        IEnumerable<string> keys = _takeoffSectionMultiSelection.Contains(anchorKey)
            ? _takeoffSectionMultiSelection
            : fallbackToAnchor
                ? [anchorKey]
                : Enumerable.Empty<string>();

        var keySet = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        if (keySet.Count == 0)
            return [];

        return EnumerateTakeoffTreeItems(TakeoffsTree)
            .Select(item => item.Tag as TakeoffMeasurementNode)
            .Where(node => node != null && keySet.Contains(TakeoffSectionSelectionKey(node)))
            .Select(node => node!)
            .ToList();
    }

    private void SelectTakeoffSectionRange(string? anchorKey, string targetKey, TakeoffItem item, bool additive)
    {
        var candidates = EnumerateVisibleTreeItems(TakeoffsTree)
            .Where(treeItem => treeItem.Tag is TakeoffMeasurementNode node && ReferenceEquals(node.Item, item))
            .Select(treeItem => (Item: treeItem, Key: GetTakeoffSectionSelectionKey(treeItem)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .Select(entry => (entry.Item, Key: entry.Key!))
            .ToList();

        SelectRangeKeys(candidates, anchorKey, targetKey, _takeoffSectionMultiSelection, additive);
    }

    private void SelectTakeoffSectionNodesSilently(IReadOnlyList<TakeoffMeasurementNode> nodes)
    {
        _takeoffsMultiSelection.Clear();
        _takeoffSectionMultiSelection.Clear();
        foreach (TakeoffMeasurementNode node in nodes)
            _takeoffSectionMultiSelection.Add(TakeoffSectionSelectionKey(node));

        TreeViewItem? first = nodes
            .Select(node => FindTakeoffSectionTreeItem(TakeoffsTree, node.Measurement))
            .FirstOrDefault(item => item != null);
        if (first != null)
        {
            _syncingTakeoffTreeSelection = true;
            try
            {
                ExpandTreeItemAndAncestorsWithoutTracking(first);
                first.IsSelected = true;
                first.BringIntoView();
            }
            finally
            {
                _syncingTakeoffTreeSelection = false;
            }
        }

        ApplyTakeoffPageHighlights();
    }

    private static IReadOnlyList<TakeoffsClipboardEntry> NormalizeSelectedTakeoffEntries(
        IReadOnlyList<TakeoffsClipboardEntry> entries)
    {
        var distinct = entries
            .GroupBy(e => NormalizePath(e.SourcePath), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(e => NormalizePath(e.SourcePath).Length)
            .ToList();

        var result = new List<TakeoffsClipboardEntry>();
        foreach (var entry in distinct)
        {
            if (result.Any(parent => SmartTakeoffsJobStore.IsSameOrDescendant(parent.SourcePath, entry.SourcePath)))
                continue;
            result.Add(entry);
        }

        return result;
    }

    private int TakeoffSelectionCount(TreeViewItem anchor) =>
        GetSelectedTakeoffEntries(anchor).Count;

    private void SetTakeoffMultiSelection(IEnumerable<string> paths)
    {
        _takeoffsMultiSelection.Clear();
        _takeoffSectionMultiSelection.Clear();
        foreach (string path in paths.Where(Directory.Exists))
            _takeoffsMultiSelection.Add(path);
        ApplyTakeoffPageHighlights();
    }

    private void SelectFirstTakeoffPath(IReadOnlyList<string> paths)
    {
        foreach (string path in paths)
        {
            if (FindTakeoffTreeItemByFolder(path) is { } selected)
            {
                selected.IsSelected = true;
                selected.BringIntoView();
                return;
            }
        }
    }

    private void PruneTakeoffsMultiSelection()
    {
        if (_currentJob == null)
        {
            _takeoffsMultiSelection.Clear();
            return;
        }

        _takeoffsMultiSelection.RemoveWhere(path =>
            !Directory.Exists(path) ||
            !SmartTakeoffsJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, path) ||
            string.Equals(path, _currentJob.TakeoffsRoot, StringComparison.OrdinalIgnoreCase));
    }

    private void PruneTakeoffSectionMultiSelection()
    {
        var validKeys = _takeoffItems
            .SelectMany(item => item.Measurements.Select(measurement => TakeoffSectionSelectionKey(new TakeoffMeasurementNode(item, measurement))))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _takeoffSectionMultiSelection.RemoveWhere(key => !validKeys.Contains(key));
        if (_takeoffSectionRangeAnchorKey != null && !validKeys.Contains(_takeoffSectionRangeAnchorKey))
            _takeoffSectionRangeAnchorKey = null;
    }

    // ── Measurement callbacks ─────────────────────────────────────────────────

    private void CopyMeasurementsToClipboard(IReadOnlyList<Measurement> measurements)
    {
        var unique = measurements
            .Where(m => m != null)
            .Distinct()
            .ToList();
        if (unique.Count == 0)
        {
            TxtStatus.Text = "No measurements selected to copy.";
            return;
        }

        var entries = new List<MeasurementClipboardEntry>();
        foreach (Measurement measurement in unique)
        {
            TakeoffItem? item = FindTakeoffItemForMeasurement(measurement);
            entries.Add(new MeasurementClipboardEntry(
                SmartTakeoffsJobStore.NormalizeMeasurementType(measurement.MType),
                measurement.Name,
                measurement.Notes,
                measurement.Color,
                measurement.Points.Select(p => new SKPoint(p.X, p.Y)).ToList(),
                measurement.PageFolder,
                measurement.ScaleMetersPerPt,
                item?.FolderPath ?? measurement.TakeoffFolder,
                item?.Name ?? "",
                item?.Color ?? measurement.Color,
                item?.UnitPrice ?? 0,
                item?.Notes ?? ""));
        }

        _measurementClipboard = new MeasurementClipboard(entries);
        TxtStatus.Text = $"Copied {entries.Count} measurement(s). Open another sheet and press Ctrl+V or right-click Paste.";
    }

    private void PasteMeasurementsFromClipboard(SKPoint? pasteAtPdf)
    {
        if (_currentJob == null || _currentPage == null)
        {
            TxtStatus.Text = "Open a job and sheet before pasting measurements.";
            return;
        }

        if (_measurementClipboard == null || _measurementClipboard.Entries.Count == 0)
        {
            TxtStatus.Text = "No copied measurements to paste.";
            return;
        }

        if (!ConfirmMeasurementPasteScale(_measurementClipboard))
            return;

        MeasurementPasteMode? pasteMode = PromptMeasurementPasteMode(_measurementClipboard.Entries.Count);
        if (pasteMode == null)
            return;

        try
        {
            PdfViewport.ViewState viewBeforePaste = _viewport.CaptureViewState();
            var pasted = new List<Measurement>();
            var pastedNodes = new List<TakeoffMeasurementNode>();
            var changedItems = new HashSet<TakeoffItem>();
            var createdTargets = new Dictionary<string, TakeoffItem>(StringComparer.OrdinalIgnoreCase);
            SKPoint pasteOffset = CalculateMeasurementPasteOffset(_measurementClipboard.Entries, pasteAtPdf);

            foreach (MeasurementClipboardEntry entry in _measurementClipboard.Entries)
            {
                TakeoffItem target = ResolveMeasurementPasteTarget(entry, pasteMode.Value, createdTargets);
                EnsureTakeoffItemFolder(target);

                Measurement measurement = CloneClipboardMeasurement(entry, target, pasteOffset);
                target.Measurements.Add(measurement);
                pasted.Add(measurement);
                pastedNodes.Add(new TakeoffMeasurementNode(target, measurement));
                changedItems.Add(target);
            }

            bool previousSuppressFocus = _suppressCanvasFocusFromTakeoffSelection;
            _suppressCanvasFocusFromTakeoffSelection = true;
            try
            {
                foreach (TakeoffItem item in changedItems)
                {
                    SmartTakeoffsJobStore.SaveTakeoffItem(item);
                    RefreshTreeItem(item);
                }
            }
            finally
            {
                _suppressCanvasFocusFromTakeoffSelection = previousSuppressFocus;
            }

            _viewport.SetMeasurements(_takeoffItems.SelectMany(item => item.Measurements));
            _viewport.RestoreViewState(viewBeforePaste);
            SelectTakeoffSectionNodesSilently(pastedNodes);
            SelectTakeoffSectionMeasurementsOnCanvas(pastedNodes);
            RefreshEstimateTable();
            RefreshPagesTakeoffIndicators();
            ApplyTakeoffPageHighlights();
            RefreshSheetLegend();
            UpdateTotalDisplay();

            string modeLabel = pasteMode.Value == MeasurementPasteMode.SameTakeoffs
                ? "same takeoff item(s)"
                : "new takeoff item(s)";
            TxtStatus.Text = $"Pasted {pasted.Count} measurement(s) to {_currentPage.Name} into {modeLabel}.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Paste Measurements", ex);
        }
    }

    private void PasteMeasurementsFromClipboard() =>
        PasteMeasurementsFromClipboard(null);

    private bool ConfirmMeasurementPasteScale(MeasurementClipboard clipboard)
    {
        var scaledEntries = clipboard.Entries
            .Where(entry => MeasurementTypeRequiresScale(entry.MeasurementType))
            .ToList();
        if (scaledEntries.Count == 0 || _currentPage?.ScaleMetersPerPt > 0)
            return true;

        if (scaledEntries.Any(entry => entry.ScaleMetersPerPt <= 0))
        {
            MessageBox.Show(
                "Set the active sheet scale before pasting Line or Area measurements. The copied measurements do not have a saved scale to reuse.",
                "Paste Measurements",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        return MessageBox.Show(
            "The active sheet has no scale. Pasted Line/Area measurements will keep the copied measurement scale.\n\nContinue?",
            "Paste Measurements",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private static bool MeasurementTypeRequiresScale(string measurementType)
    {
        string normalized = SmartTakeoffsJobStore.NormalizeMeasurementType(measurementType);
        return normalized is "line" or "area";
    }

    private MeasurementPasteMode? PromptMeasurementPasteMode(int count)
    {
        MessageBoxResult result = MessageBox.Show(
            $"Paste {count} copied measurement(s) to the active sheet?\n\n" +
            "Yes = use the same takeoff items/values.\n" +
            "No = create new copied takeoff items.\n" +
            "Cancel = do nothing.",
            "Paste Measurements",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return result switch
        {
            MessageBoxResult.Yes => MeasurementPasteMode.SameTakeoffs,
            MessageBoxResult.No => MeasurementPasteMode.NewTakeoffs,
            _ => null,
        };
    }

    private TakeoffItem ResolveMeasurementPasteTarget(
        MeasurementClipboardEntry entry,
        MeasurementPasteMode mode,
        Dictionary<string, TakeoffItem> createdTargets)
    {
        string measurementType = SmartTakeoffsJobStore.NormalizeMeasurementType(entry.MeasurementType);
        if (mode == MeasurementPasteMode.SameTakeoffs)
        {
            TakeoffItem? sourceItem = FindTakeoffItemByFolder(entry.SourceTakeoffFolder, measurementType);
            if (sourceItem != null)
                return sourceItem;
        }

        string key = MeasurementClipboardTargetKey(entry);
        if (createdTargets.TryGetValue(key, out TakeoffItem? created))
            return created;

        string baseName = string.IsNullOrWhiteSpace(entry.SourceTakeoffName)
            ? $"{MeasurementTypeTitle(measurementType)} Paste"
            : mode == MeasurementPasteMode.NewTakeoffs
                ? $"{entry.SourceTakeoffName} Copy"
                : entry.SourceTakeoffName;
        string color = IsValidWpfColor(entry.SourceTakeoffColor)
            ? entry.SourceTakeoffColor
            : entry.MeasurementColor;
        var target = CreateUniqueTakeoffItem(baseName, color, measurementType, NewTakeoffItemParentFolder());
        target.UnitPrice = entry.SourceTakeoffUnitPrice;
        target.Notes = entry.SourceTakeoffNotes;
        _takeoffItems.Add(target);

        ItemsControl parent = FindTakeoffTreeItemByFolder(Path.GetDirectoryName(target.FolderPath) ?? "") ?? (ItemsControl)TakeoffsTree;
        AddTakeoffTreeItem(target, parent);
        if (parent is TreeViewItem parentTvi)
            parentTvi.IsExpanded = true;

        createdTargets[key] = target;
        return target;
    }

    private Measurement CloneClipboardMeasurement(MeasurementClipboardEntry entry, TakeoffItem target, SKPoint pasteOffset)
    {
        double scale = _currentPage?.ScaleMetersPerPt > 0
            ? _currentPage.ScaleMetersPerPt
            : entry.ScaleMetersPerPt;

        return new Measurement
        {
            MType = SmartTakeoffsJobStore.NormalizeMeasurementType(entry.MeasurementType),
            Name = entry.MeasurementName,
            Notes = entry.MeasurementNotes,
            Color = target.Color,
            Points = entry.Points.Select(p => new SKPoint(p.X + pasteOffset.X, p.Y + pasteOffset.Y)).ToList(),
            PageFolder = _currentPage?.FolderPath ?? "",
            TakeoffFolder = target.FolderPath,
            ScaleMetersPerPt = scale,
        };
    }

    private static SKPoint CalculateMeasurementPasteOffset(
        IReadOnlyList<MeasurementClipboardEntry> entries,
        SKPoint? pasteAtPdf)
    {
        if (!pasteAtPdf.HasValue || !TryGetClipboardBounds(entries, out SKRect bounds))
            return new SKPoint(0, 0);

        var sourceCenter = new SKPoint(
            (bounds.Left + bounds.Right) / 2f,
            (bounds.Top + bounds.Bottom) / 2f);
        return new SKPoint(
            pasteAtPdf.Value.X - sourceCenter.X,
            pasteAtPdf.Value.Y - sourceCenter.Y);
    }

    private static bool TryGetClipboardBounds(
        IReadOnlyList<MeasurementClipboardEntry> entries,
        out SKRect bounds)
    {
        bounds = SKRect.Empty;
        bool hasPoint = false;
        float left = 0;
        float top = 0;
        float right = 0;
        float bottom = 0;

        foreach (MeasurementClipboardEntry entry in entries)
        {
            foreach (SKPoint point in entry.Points)
            {
                if (!hasPoint)
                {
                    left = right = point.X;
                    top = bottom = point.Y;
                    hasPoint = true;
                    continue;
                }

                left = Math.Min(left, point.X);
                top = Math.Min(top, point.Y);
                right = Math.Max(right, point.X);
                bottom = Math.Max(bottom, point.Y);
            }
        }

        if (!hasPoint)
            return false;

        bounds = new SKRect(left, top, right, bottom);
        return true;
    }

    private static string MeasurementClipboardTargetKey(MeasurementClipboardEntry entry)
    {
        string source = string.IsNullOrWhiteSpace(entry.SourceTakeoffFolder)
            ? $"{entry.SourceTakeoffName}|{entry.MeasurementType}|{entry.SourceTakeoffColor}"
            : entry.SourceTakeoffFolder;
        return source.Trim();
    }

    private TakeoffItem? FindTakeoffItemForMeasurement(Measurement measurement)
    {
        TakeoffItem? item = _takeoffItems.FirstOrDefault(i => i.Measurements.Contains(measurement));
        if (item != null)
            return item;

        return FindTakeoffItemByFolder(measurement.TakeoffFolder, measurement.MType);
    }

    private TakeoffItem? FindTakeoffItemByFolder(string? folderPath, string? measurementType = null)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return null;

        string normalizedType = string.IsNullOrWhiteSpace(measurementType)
            ? ""
            : SmartTakeoffsJobStore.NormalizeMeasurementType(measurementType);

        return _takeoffItems.FirstOrDefault(item =>
            string.Equals(item.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(normalizedType) ||
             SmartTakeoffsJobStore.NormalizeMeasurementType(item.MeasurementType) == normalizedType));
    }

    private void QueueTakeoffAutosave(TakeoffItem item)
    {
        if (_currentJob == null)
            return;

        _pendingTakeoffAutosaves.Add(item);
        _takeoffAutosaveTimer.Stop();
        _takeoffAutosaveTimer.Start();
    }

    private void FlushTakeoffAutosaves()
    {
        _takeoffAutosaveTimer.Stop();
        if (_pendingTakeoffAutosaves.Count == 0)
            return;

        var pending = _pendingTakeoffAutosaves.ToList();
        _pendingTakeoffAutosaves.Clear();
        foreach (var item in pending)
            PersistTakeoffItemQuietly(item);
    }

    private void PersistTakeoffItemQuietly(TakeoffItem item)
    {
        if (_currentJob == null)
            return;

        try
        {
            EnsureTakeoffItemFolder(item);
            SmartTakeoffsJobStore.SaveTakeoffItem(item);
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Autosave skipped: {ex.Message}";
        }
    }

    // ── Tree helpers ──────────────────────────────────────────────────────────

    private void RefreshTreeItem(TakeoffItem item)
    {
        if (FindTakeoffTreeItem(item) is { } tvi)
        {
            SetTreeItemHeader(tvi, item);
            RefreshTakeoffSectionNodes(tvi, item);
        }
    }

    private void RefreshActiveTakeoffVisuals()
    {
        foreach (TreeViewItem tvi in EnumerateTakeoffTreeItems(TakeoffsTree))
        {
            if (tvi.Tag is TakeoffItem item)
                SetTreeItemHeader(tvi, item);
        }

        UpdateActiveTakeoffTargetBar();
        ApplyTakeoffPageHighlights();
    }

    private void RefreshAllTotals()
    {
        RefreshTotalsRecursive(TakeoffsTree);
        RefreshPagesTakeoffIndicators();
        ApplyTakeoffPageHighlights();
        RefreshSheetLegend();
        UpdateTotalDisplay();
    }

    private void RefreshSheetLegend()
    {
        if (_currentPage == null || !_settings.ShowSheetLegend)
        {
            _viewport.SetSheetLegend([]);
            return;
        }

        var entries = OrderedTakeoffsForPage(_currentPage)
            .Select(item =>
            {
                var pageMeasurements = MeasurementsForTakeoffOnPage(item, _currentPage.FolderPath).ToList();
                return pageMeasurements.Count == 0
                    ? null
                    : new SheetLegendEntry(
                        item.Color,
                        item.Name,
                        SheetLegendQuantityText(item, pageMeasurements),
                        SheetLegendTypeTitle(item),
                        SheetLegendTypeSign(item),
                        []);
            })
            .Where(entry => entry != null)
            .Cast<SheetLegendEntry>()
            .ToList();

        _viewport.SetSheetLegend(entries);
    }

    private string SheetLegendQuantityText(TakeoffItem item, IReadOnlyList<Measurement> measurements)
    {
        string measurementType = SmartTakeoffsJobStore.NormalizeMeasurementType(item.MeasurementType);
        double fallbackScale = _currentPage?.ScaleMetersPerPt > 0
            ? _currentPage.ScaleMetersPerPt
            : _viewport.ScaleMetersPerPt;

        if (measurementType == "point")
            return Units.FormatCount(measurements.Sum(measurement => measurement.Points.Count));

        bool hasScale = fallbackScale > 0 || measurements.Any(measurement => measurement.ScaleMetersPerPt > 0);
        if (item.IsJoistArea)
        {
            return hasScale
                ? Units.FormatArea(measurements.Sum(measurement => measurement.AreaValue(fallbackScale)), _viewport.UnitMode)
                : $"{measurements.Sum(measurement => measurement.Points.Count)} pts";
        }

        if (!hasScale)
        {
            if (measurementType == "line")
                return $"{measurements.Sum(measurement => Math.Max(0, measurement.Points.Count - 1))} seg";
            if (measurementType == "area")
                return $"{measurements.Sum(measurement => measurement.Points.Count)} pts";
        }

        double total = measurements.Sum(measurement => measurement.Value(fallbackScale));
        return measurementType switch
        {
            "line" => Units.FormatLength(total, _viewport.UnitMode),
            "area" => Units.FormatArea(total, _viewport.UnitMode),
            _ => Units.FormatCount(total),
        };
    }

    private void RefreshTotalsRecursive(ItemsControl parent)
    {
        foreach (TreeViewItem tvi in parent.Items.OfType<TreeViewItem>())
        {
            if (tvi.Tag is TakeoffItem item)
            {
                SetTreeItemHeader(tvi, item);
                RefreshTakeoffSectionNodes(tvi, item);
            }
            else
            {
                RefreshTotalsRecursive(tvi);
            }
        }
    }

    private void ApplyTakeoffPageHighlights()
    {
        Brush? brushOrNull(string key) => Application.Current.Resources[key] as Brush;
        Brush dropOk      = brushOrNull("RowDropOkBrush")      ?? new SolidColorBrush(Color.FromRgb(204, 245, 218));
        Brush dropBad     = brushOrNull("RowDropBadBrush")     ?? new SolidColorBrush(Color.FromRgb(255, 214, 214));
        Brush multiSel    = brushOrNull("RowMultiSelectBrush") ?? new SolidColorBrush(Color.FromRgb(205, 226, 255));
        Brush onPageBg    = brushOrNull("RowOnPageBrush")      ?? new SolidColorBrush(Color.FromRgb(214, 245, 222));
        Brush rowFg       = brushOrNull("RowFlagForegroundBrush") ?? Brushes.Black;
        Brush activeAccent = brushOrNull("RowActiveAccentBrush")  ?? new SolidColorBrush(Color.FromRgb(31, 82, 166));

        foreach (TreeViewItem item in EnumerateTakeoffTreeItems(TakeoffsTree))
        {
            item.ClearValue(Control.BorderBrushProperty);
            item.ClearValue(Control.BorderThicknessProperty);
            item.ClearValue(Control.FontWeightProperty);

            string? path = GetTakeoffNodePath(item);
            string? sectionKey = GetTakeoffSectionSelectionKey(item);
            bool sectionSelected = sectionKey != null && _takeoffSectionMultiSelection.Contains(sectionKey);
            bool takeoffSelected = path != null && _takeoffsMultiSelection.Contains(path);
            bool isActiveTakeoff = item.Tag is TakeoffItem activeTakeoff && IsActiveTakeoffItem(activeTakeoff);
            bool isMeasuredOnPage = item.Tag is TakeoffItem takeoff && IsTakeoffMeasuredOnCurrentPage(takeoff);
            if (ReferenceEquals(item, _takeoffSectionDropTarget))
            {
                item.Background = _takeoffSectionDropAllowed ? dropOk : dropBad;
                item.Foreground = rowFg;
                item.FontWeight = FontWeights.Normal;
                item.BorderBrush = _takeoffSectionDropAllowed ? Brushes.SeaGreen : Brushes.IndianRed;
                item.BorderThickness = new Thickness(0, 0, 0, 2);
            }
            else if (ReferenceEquals(item, _takeoffPositionDropTarget))
            {
                item.Background = _takeoffPositionDropAllowed ? dropOk : dropBad;
                item.Foreground = rowFg;
                item.FontWeight = FontWeights.Normal;
                item.BorderBrush = _takeoffPositionDropAllowed ? Brushes.SeaGreen : Brushes.IndianRed;
                item.BorderThickness = _takeoffPositionDropAfter
                    ? new Thickness(0, 0, 0, 2)
                    : new Thickness(0, 2, 0, 0);
            }
            else
            {
                if (sectionSelected || takeoffSelected)
                {
                    item.Background = multiSel;
                    item.Foreground = rowFg;
                }
                else if (isMeasuredOnPage)
                {
                    item.Background = onPageBg;
                    item.Foreground = rowFg;
                }
                else
                {
                    item.ClearValue(Control.BackgroundProperty);
                    item.ClearValue(Control.ForegroundProperty);
                }

                if (isActiveTakeoff)
                {
                    item.Foreground = rowFg;
                    item.FontWeight = FontWeights.Normal;
                    item.BorderBrush = activeAccent;
                    item.BorderThickness = new Thickness(3, 0, 0, 0);
                }
            }
        }
    }

    private bool IsTakeoffMeasuredOnCurrentPage(TakeoffItem takeoff) =>
        _currentPage != null &&
        takeoff.Measurements.Any(m =>
            IsSamePageFolder(m.PageFolder, _currentPage.FolderPath));

    private static IEnumerable<TreeViewItem> EnumerateTakeoffTreeItems(ItemsControl parent)
    {
        foreach (TreeViewItem child in parent.Items.OfType<TreeViewItem>())
        {
            yield return child;
            foreach (TreeViewItem nested in EnumerateTakeoffTreeItems(child))
                yield return nested;
        }
    }

    private bool IsActiveTakeoffItem(TakeoffItem item)
    {
        if (_activeItem == null)
            return false;

        if (ReferenceEquals(_activeItem, item))
            return true;

        return !string.IsNullOrWhiteSpace(_activeItem.FolderPath) &&
               !string.IsNullOrWhiteSpace(item.FolderPath) &&
               string.Equals(_activeItem.FolderPath, item.FolderPath, StringComparison.OrdinalIgnoreCase);
    }

    private void SetTreeItemHeader(TreeViewItem tvi, TakeoffItem item)
    {
        bool isActive = IsActiveTakeoffItem(item);
        Brush swatchBrush = BrushFromHex(item.Color, Brushes.Gray);
        var secondaryBrush = (Brush)Application.Current.Resources["SecondaryForegroundBrush"]
            ?? new SolidColorBrush(Color.FromRgb(128, 128, 128));

        // Filled glyph in the takeoff color (no separate color square).
        var swatchHost = BuildTakeoffSwatchGlyph(item, swatchBrush, isActive ? 18 : 16);

        // Quantity goes to the right via DockPanel for ledger-style alignment.
        var dock = new DockPanel { LastChildFill = true, HorizontalAlignment = HorizontalAlignment.Stretch };

        string total = item.TotalLabel(_viewport.ScaleMetersPerPt, _viewport.UnitMode);
        var totalText = new TextBlock
        {
            Text              = total,
            Foreground        = secondaryBrush,
            FontSize          = 10,
            FontFamily        = new FontFamily("Consolas, Cascadia Mono, Segoe UI"),
            Margin            = new Thickness(8, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment     = TextAlignment.Right,
            MinWidth          = 56,
        };
        DockPanel.SetDock(totalText, Dock.Right);
        dock.Children.Add(totalText);

        if (item.Measurements.Count > 0)
        {
            var sectionsText = new TextBlock
            {
                Text              = SectionCountLabel(item),
                Foreground        = secondaryBrush,
                FontSize          = 10,
                Margin            = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(sectionsText, Dock.Right);
            dock.Children.Add(sectionsText);
        }

        var nameRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        swatchHost.Margin = new Thickness(0, 0, 6, 0);
        nameRow.Children.Add(swatchHost);
        nameRow.Children.Add(new TextBlock
        {
            Text              = item.Name,
            FontWeight        = FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming      = TextTrimming.CharacterEllipsis,
        });
        dock.Children.Add(nameRow);

        tvi.Header = dock;
        tvi.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        tvi.ToolTip = TakeoffItemTooltip(item, isActive);
    }

    private static FrameworkElement BuildTakeoffSwatchGlyph(TakeoffItem item, Brush swatchBrush, double size)
    {
        // Glyph drawn in the takeoff color with a darker stroke — no separate
        // colored square, the glyph itself carries the color identity.
        return Controls.MeasurementGlyph.CreateWpf(
            Controls.MeasurementGlyph.Parse(
                SmartTakeoffsJobStore.NormalizeMeasurementType(item.MeasurementType),
                joist: item.IsJoistArea),
            swatchBrush,
            size,
            new Thickness(0));
    }

    private void SetFolderTreeItemHeader(TreeViewItem tvi, TakeoffFolderNode folder)
    {
        TakeoffFolderProperties properties = TakeoffFolderPropertiesStore.Load(folder.FolderPath);
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = folder.Name,
            FontWeight = FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "  folder",
            Foreground = Brushes.Gray,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
        });
        string defaultSummary = TakeoffFolderDefaultSummary(properties);
        if (!string.IsNullOrWhiteSpace(defaultSummary))
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"  default: {defaultSummary}",
                Foreground = Brushes.Gray,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        tvi.Header = panel;
        tvi.ToolTip = TakeoffFolderTooltip(folder, properties);
    }

    private string TakeoffFolderStatusText(TakeoffFolderNode folder)
    {
        TakeoffFolderProperties properties = TakeoffFolderPropertiesStore.Load(folder.FolderPath);
        var parts = new List<string> { $"Folder: {folder.Name}" };
        string defaultSummary = TakeoffFolderDefaultSummary(properties);
        if (!string.IsNullOrWhiteSpace(defaultSummary))
            parts.Add($"default {defaultSummary}");
        if (!string.IsNullOrWhiteSpace(properties.Notes))
            parts.Add($"notes: {OneLinePreview(properties.Notes, 90)}");
        return string.Join(" | ", parts);
    }

    private static string? TakeoffFolderTooltip(TakeoffFolderNode folder, TakeoffFolderProperties properties)
    {
        var lines = new List<string> { folder.Name };
        string defaultSummary = TakeoffFolderDefaultSummary(properties);
        if (!string.IsNullOrWhiteSpace(defaultSummary))
            lines.Add($"Default: {defaultSummary}");
        if (!string.IsNullOrWhiteSpace(properties.Notes))
            lines.Add($"Notes: {properties.Notes}");
        return lines.Count <= 1 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string TakeoffFolderDefaultSummary(TakeoffFolderProperties properties)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(properties.DefaultMeasurementType))
            parts.Add(MeasurementTypeTitle(properties.DefaultMeasurementType));
        if (!string.IsNullOrWhiteSpace(properties.DefaultColor))
            parts.Add(properties.DefaultColor);
        if (properties.DefaultUnitPrice is >= 0)
            parts.Add($"price {properties.DefaultUnitPrice.Value:G}");
        if (!string.IsNullOrWhiteSpace(properties.DefaultNamePrefix))
            parts.Add($"prefix {properties.DefaultNamePrefix}");
        if (!string.IsNullOrWhiteSpace(properties.DefaultItemNotes))
            parts.Add($"item notes: {OneLinePreview(properties.DefaultItemNotes, 32)}");
        return string.Join(", ", parts);
    }

    private static string OneLinePreview(string value, int maxLength)
    {
        string text = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= maxLength ? text : text[..Math.Max(0, maxLength - 3)] + "...";
    }

    private string CurrentTakeoffParentFolder()
    {
        string? selectedFolder = TakeoffsTree.SelectedItem is TreeViewItem { Tag: TakeoffFolderNode folder }
            ? folder.FolderPath
            : null;
        string? selectedItemParentFolder = TakeoffsTree.SelectedItem is TreeViewItem { Tag: TakeoffItem item } &&
            !string.IsNullOrWhiteSpace(item.FolderPath)
                ? Path.GetDirectoryName(item.FolderPath) ?? _currentJob?.TakeoffsRoot
                : null;
        return TakeoffCreationPolicy.NewFolderParentFolder(
            _currentJob,
            selectedFolder,
            selectedItemParentFolder,
            _activeTakeoffParentFolder,
            Directory.Exists);
    }

    private string NewTakeoffItemParentFolder() =>
        TakeoffCreationPolicy.NewItemParentFolder(_currentJob);

    private string ResolveTakeoffFolderDefaultMeasurementType(string folderPath, string fallback)
    {
        string fallbackType = SmartTakeoffsJobStore.NormalizeMeasurementType(fallback);
        foreach (TakeoffFolderProperties properties in EnumerateTakeoffFolderProperties(folderPath))
        {
            if (!string.IsNullOrWhiteSpace(properties.DefaultMeasurementType))
                return SmartTakeoffsJobStore.NormalizeMeasurementType(properties.DefaultMeasurementType);
        }

        return fallbackType;
    }

    private string ResolveTakeoffFolderDefaultColor(string folderPath, string fallback)
    {
        foreach (TakeoffFolderProperties properties in EnumerateTakeoffFolderProperties(folderPath))
        {
            if (!string.IsNullOrWhiteSpace(properties.DefaultColor) &&
                IsValidWpfColor(properties.DefaultColor))
            {
                return properties.DefaultColor;
            }
        }

        return IsValidWpfColor(fallback) ? fallback : "#FF4444";
    }

    private double? ResolveTakeoffFolderDefaultUnitPrice(string folderPath)
    {
        foreach (TakeoffFolderProperties properties in EnumerateTakeoffFolderProperties(folderPath))
        {
            if (properties.DefaultUnitPrice is >= 0)
                return properties.DefaultUnitPrice.Value;
        }

        return null;
    }

    private string ResolveTakeoffFolderDefaultItemNotes(string folderPath)
    {
        foreach (TakeoffFolderProperties properties in EnumerateTakeoffFolderProperties(folderPath))
        {
            if (!string.IsNullOrWhiteSpace(properties.DefaultItemNotes))
                return properties.DefaultItemNotes;
        }

        return "";
    }

    private string ResolveTakeoffFolderDefaultNamePrefix(string folderPath)
    {
        foreach (TakeoffFolderProperties properties in EnumerateTakeoffFolderProperties(folderPath))
        {
            if (!string.IsNullOrWhiteSpace(properties.DefaultNamePrefix))
                return properties.DefaultNamePrefix;
        }

        return "";
    }

    private void ApplyTakeoffFolderDefaultsToNewItem(TakeoffItem item, string parentFolder)
    {
        bool changed = false;
        if (ResolveTakeoffFolderDefaultUnitPrice(parentFolder) is { } unitPrice)
        {
            item.UnitPrice = unitPrice;
            changed = true;
        }

        string defaultNotes = ResolveTakeoffFolderDefaultItemNotes(parentFolder);
        if (!string.IsNullOrWhiteSpace(defaultNotes))
        {
            item.Notes = defaultNotes;
            changed = true;
        }

        if (changed)
            SmartTakeoffsJobStore.SaveTakeoffItem(item);
    }

    private IEnumerable<TakeoffFolderProperties> EnumerateTakeoffFolderProperties(string folderPath)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(folderPath))
            yield break;

        string? current = folderPath;
        while (!string.IsNullOrWhiteSpace(current) &&
               Directory.Exists(current) &&
               SmartTakeoffsJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, current))
        {
            if (TakeoffFolderPropertiesStore.TryLoad(current) != null)
                yield return TakeoffFolderPropertiesStore.Load(current);

            if (string.Equals(current, _currentJob.TakeoffsRoot, StringComparison.OrdinalIgnoreCase))
                yield break;
            current = Path.GetDirectoryName(current);
        }
    }

    private string CurrentToolMeasurementType() =>
        _activeTool is "point" or "area" ? _activeTool : "line";

    private void UpdateToolStatus()
    {
        string title = _activeTool switch
        {
            "point" => MeasurementTypeDisplay("point"),
            "line" => MeasurementTypeDisplay("line"),
            "area" => MeasurementTypeDisplay("area"),
            "select" => "Select",
            "scale" => "Scale",
            "ruler" => "Ruler",
            "drawline" => "Draw Line",
            "drawarrow" => "Arrow",
            "drawrect" => "Box",
            _ => "Pan",
        };
        bool recording = _activeTool is "point" or "line" or "area";
        string item = recording && _activeItem != null
            ? $"  |  Item: {_activeItem.Name}"
            : "";
        TxtTool.Text =
            $"  Tool: {title}  |  Record: {(recording ? "On" : "Off")}" +
            $"  |  Snap: {(_viewport.SnapEnabled ? "On" : "Off")}" +
            $"  |  Ortho: {(_viewport.OrthoEnabled ? "On" : "Off")}{item}";
        UpdateActiveTakeoffTargetBar();
    }

    private void UpdateActiveTakeoffTargetBar()
    {
        if (ActiveTakeoffTargetBar == null)
            return;

        if (_activeItem == null)
        {
            ActiveTakeoffTargetBar.Visibility = Visibility.Collapsed;
            TxtActiveTakeoffTarget.Text = "No active takeoff";
            TxtActiveTakeoffTargetMeta.Text = "";
            ActiveTakeoffTargetGlyphHost.Child = null;
            BtnActiveTakeoffRecord.IsEnabled = false;
            BtnActiveTakeoffMore.IsEnabled = false;
            BtnActiveTakeoffFind.IsEnabled = false;
            BtnActiveTakeoffProperties.IsEnabled = false;
            BtnActiveTakeoffPrevious.IsEnabled = false;
            BtnActiveTakeoffNext.IsEnabled = false;
            BtnActiveTakeoffSheetNext.IsEnabled = false;
            return;
        }

        string measurementType = SmartTakeoffsJobStore.NormalizeMeasurementType(_activeItem.MeasurementType);
        string typeTitle = TakeoffTypeDisplay(_activeItem);
        string total = _activeItem.Measurements.Count == 0
            ? "no measurements"
            : _activeItem.TotalLabel(_viewport.ScaleMetersPerPt, _viewport.UnitMode);
        string sheetTotal = ActiveTakeoffSheetTotalText(_activeItem);
        bool recordingThis = _activeTool == measurementType;

        ActiveTakeoffTargetBar.Visibility = Visibility.Visible;
        TxtActiveTakeoffTarget.Text = _activeItem.Name;
        TxtActiveTakeoffTargetMeta.Text = $"{TakeoffTypeTitle(_activeItem)} | total: {total}{sheetTotal}";
        ActiveTakeoffTargetGlyphHost.Child = BuildTakeoffSwatchGlyph(
            _activeItem, BrushFromHex(_activeItem.Color, Brushes.Gray), 18);
        BtnActiveTakeoffRecord.Content = recordingThis ? $"Recording {typeTitle}" : $"Record {typeTitle}";
        BtnActiveTakeoffRecord.IsEnabled = _currentPage != null;
        BtnActiveTakeoffMore.IsEnabled = true;
        BtnActiveTakeoffRecord.ToolTip = _currentPage == null
            ? "Select a sheet before recording"
            : recordingThis
                ? $"Recording {typeTitle} into {_activeItem.Name}. Click the toolbar Record button to stop."
                : $"Start recording {typeTitle} into {_activeItem.Name}";
        bool hasTreeItem = FindTakeoffTreeItem(_activeItem) != null;
        BtnActiveTakeoffFind.IsEnabled = hasTreeItem;
        BtnActiveTakeoffProperties.IsEnabled = hasTreeItem;
        bool canCycle = ActiveTakeoffTargetCycleItems().Count > 1;
        BtnActiveTakeoffPrevious.IsEnabled = canCycle;
        BtnActiveTakeoffNext.IsEnabled = canCycle;
        int sheetTargetCount = ActiveSheetTakeoffTargetCycleItems().Count;
        BtnActiveTakeoffSheetNext.IsEnabled = sheetTargetCount > 0;
        BtnActiveTakeoffSheetNext.ToolTip = sheetTargetCount > 0
            ? $"Switch through {sheetTargetCount} takeoff item(s) measured on this sheet"
            : "No takeoff items are measured on this sheet yet";
    }

    private string ActiveTakeoffSheetTotalText(TakeoffItem item)
    {
        if (_currentPage == null)
            return "";

        var pageMeasurements = MeasurementsForTakeoffOnPage(item, _currentPage.FolderPath).ToList();
        string pageQuantity = pageMeasurements.Count == 0
            ? "none on sheet"
            : SheetLegendQuantityText(item, pageMeasurements);
        return $" | sheet: {pageQuantity}";
    }

    private string DefaultTakeoffName(string measurementType)
    {
        string title = MeasurementTypeTitle(measurementType);
        if (_activeItem != null &&
            SmartTakeoffsJobStore.NormalizeMeasurementType(_activeItem.MeasurementType) != measurementType)
            return $"{_activeItem.Name} - {title}";
        if (_currentPage != null)
            return $"{_currentPage.Name} {title}";
        return $"{title} Item";
    }

    private string DefaultTakeoffNameForFolder(string measurementType, string parentFolder)
    {
        string baseName = DefaultTakeoffName(measurementType);
        string prefix = ResolveTakeoffFolderDefaultNamePrefix(parentFolder);
        if (string.IsNullOrWhiteSpace(prefix) ||
            baseName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return baseName;
        }

        return prefix.EndsWith(" ", StringComparison.Ordinal) ||
               prefix.EndsWith("-", StringComparison.Ordinal) ||
               prefix.EndsWith("_", StringComparison.Ordinal)
            ? prefix + baseName
            : $"{prefix} {baseName}";
    }

    private static string MeasurementTypeTitle(string measurementType) =>
        SmartTakeoffsJobStore.NormalizeMeasurementType(measurementType) switch
        {
            "point" => "Count",
            "area" => "Area",
            _ => "Line",
        };

    private static string TakeoffTypeTitle(TakeoffItem item) =>
        item.IsJoistArea ? "Joist" : MeasurementTypeTitle(item.MeasurementType);

    private static string MeasurementTypeSign(string measurementType) =>
        SmartTakeoffsJobStore.NormalizeMeasurementType(measurementType) switch
        {
            "point" => "○",
            "area" => "□",
            _ => "╱",
        };

    private static string TakeoffTypeSign(TakeoffItem item) =>
        item.IsJoistArea ? "□╱" : MeasurementTypeSign(item.MeasurementType);

    private static string MeasurementTypeDisplay(string measurementType) =>
        $"{MeasurementTypeSign(measurementType)} {MeasurementTypeTitle(measurementType)}";

    private static string TakeoffTypeDisplay(TakeoffItem item) =>
        $"{TakeoffTypeSign(item)} {TakeoffTypeTitle(item)}";

    private static string SheetLegendTypeTitle(TakeoffItem item) =>
        item.IsJoistArea ? "Area" : TakeoffTypeTitle(item);

    private static string SheetLegendTypeSign(TakeoffItem item) =>
        item.IsJoistArea ? MeasurementTypeSign("area") : TakeoffTypeSign(item);

    private static FrameworkElement CreateTakeoffTypeIcon(TakeoffItem item, double size, Thickness margin) =>
        Controls.MeasurementGlyph.CreateWpf(
            Controls.MeasurementGlyph.Parse(
                SmartTakeoffsJobStore.NormalizeMeasurementType(item.MeasurementType),
                joist: item.IsJoistArea),
            BrushFromHex(item.Color, Brushes.Gray),
            size,
            margin);

    private static FrameworkElement CreateMeasurementTypeIcon(string kind, Brush brush, double size, Thickness margin) =>
        Controls.MeasurementGlyph.CreateWpf(
            Controls.MeasurementGlyph.Parse(SmartTakeoffsJobStore.NormalizeMeasurementType(kind),
                joist: kind.Equals("joist", StringComparison.OrdinalIgnoreCase)),
            brush,
            size,
            margin);

    private string TakeoffUnitText(TakeoffItem item) =>
        item.IsJoistArea ? UnitText("line") : UnitText(item.MeasurementType);

    private string MeasurementUnitText(Measurement measurement) =>
        measurement.JoistEnabled ? UnitText("line") : UnitText(measurement.MType);

    private static string CsvMeasurementType(TakeoffItem item) =>
        item.IsJoistArea ? "joist" : SmartTakeoffsJobStore.NormalizeMeasurementType(item.MeasurementType);

    private TreeViewItem? FindFirstTakeoffTreeItem(ItemsControl parent)
    {
        foreach (TreeViewItem tvi in parent.Items.OfType<TreeViewItem>())
        {
            if (tvi.Tag is TakeoffItem)
                return tvi;

            if (FindFirstTakeoffTreeItem(tvi) is { } found)
                return found;
        }

        return null;
    }

    private bool SelectFirstTakeoffItem()
    {
        if (FindFirstTakeoffTreeItem(TakeoffsTree) is not { } first)
        {
            _activeItem = null;
            _viewport.ActiveColor = "#FF4444";
            _viewport.ActiveTakeoffFolder = "";
            RefreshActiveTakeoffVisuals();
            return false;
        }

        first.IsSelected = true;
        first.BringIntoView();
        return true;
    }

    private void SelectTakeoffItem(TakeoffItem item)
    {
        if (FindTakeoffTreeItem(item) is { } tvi)
        {
            _takeoffsMultiSelection.Clear();
            _takeoffSectionMultiSelection.Clear();
            if (!string.IsNullOrWhiteSpace(item.FolderPath))
                _takeoffsMultiSelection.Add(item.FolderPath);
            ApplyTakeoffPageHighlights();
            tvi.IsSelected = true;
            tvi.BringIntoView();
        }
    }

    private void SelectFirstTakeoffItemSilently(IReadOnlyList<TakeoffItem> items)
    {
        TreeViewItem? first = items
            .Select(FindTakeoffTreeItem)
            .FirstOrDefault(item => item != null);
        if (first == null)
            return;

        _syncingTakeoffTreeSelection = true;
        try
        {
            ExpandTreeItemAndAncestorsWithoutTracking(first);
            first.IsSelected = true;
            first.BringIntoView();
        }
        finally
        {
            _syncingTakeoffTreeSelection = false;
        }
    }

    private IReadOnlyList<TakeoffItem> TakeoffItemsForSelection(TreeViewItem? anchor)
    {
        IReadOnlyList<TakeoffsClipboardEntry> entries = anchor == null
            ? []
            : GetSelectedTakeoffEntries(anchor);

        if (entries.Count == 0)
        {
            return anchor?.Tag switch
            {
                TakeoffItem item => [item],
                TakeoffMeasurementNode node => [node.Item],
                TakeoffFolderNode folder => TakeoffItemsInsideFolder(folder.FolderPath),
                _ => [],
            };
        }

        return _takeoffItems
            .Where(item => entries.Any(entry => SmartTakeoffsJobStore.IsSameOrDescendant(entry.SourcePath, item.FolderPath)))
            .GroupBy(item => NormalizePath(item.FolderPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private IReadOnlyList<TakeoffItem> TakeoffItemsInsideFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return [];

        return _takeoffItems
            .Where(item => SmartTakeoffsJobStore.IsSameOrDescendant(folderPath, item.FolderPath))
            .GroupBy(item => NormalizePath(item.FolderPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private void RevealPagesForTakeoffSelection(TreeViewItem? anchor, string? preferredPageFolder = null)
    {
        RevealPagesForTakeoffItems(TakeoffItemsForSelection(anchor), preferredPageFolder ?? _currentPage?.FolderPath);
    }

    private void RevealPagesForTakeoffItems(IReadOnlyList<TakeoffItem> items, string? preferredPageFolder = null)
    {
        _pageTakeoffMultiSelection.Clear();
        _pagesMultiSelection.Clear();

        var selectedFolders = items
            .Select(item => item.FolderPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedFolders.Count == 0)
        {
            ApplyPagesMultiSelectionVisuals();
            return;
        }

        TreeViewItem? preferredLinked = null;
        foreach (TreeViewItem pageItem in EnumeratePageTreeItems())
        {
            if (pageItem.Tag is not PageInfo page)
                continue;

            var matchedTakeoffs = items
                .Where(item => selectedFolders.Contains(item.FolderPath) &&
                               item.Measurements.Any(measurement => IsSamePageFolder(measurement.PageFolder, page.FolderPath)))
                .ToList();
            if (matchedTakeoffs.Count == 0)
                continue;

            ExpandTreeItemAndAncestorsWithoutTracking(pageItem);
            pageItem.IsExpanded = true;
            bool isPreferredPage = !string.IsNullOrWhiteSpace(preferredPageFolder) &&
                                   IsSamePageFolder(page.FolderPath, preferredPageFolder);

            foreach (TakeoffItem takeoff in matchedTakeoffs)
            {
                _pageTakeoffMultiSelection.Add(PageTakeoffSelectionKey(new PageTakeoffNode(page, takeoff)));
                TreeViewItem? linked = FindPageTakeoffTreeItem(page.FolderPath, takeoff.FolderPath);
                if (isPreferredPage)
                    preferredLinked ??= linked;
            }
        }

        ApplyPagesMultiSelectionVisuals();

        if (preferredLinked == null)
        {
            if (_currentPage != null)
                SelectPageTreeNodeSilently(_currentPage.FolderPath);
            return;
        }

        _syncingPageTreeSelection = true;
        try
        {
            ExpandTreeItemAndAncestorsWithoutTracking(preferredLinked);
            preferredLinked.IsSelected = true;
            preferredLinked.BringIntoView();
        }
        finally
        {
            _syncingPageTreeSelection = false;
        }
    }

    private void SelectTakeoffSectionNode(Measurement measurement)
    {
        if (FindTakeoffSectionTreeItem(TakeoffsTree, measurement) is not { } tvi)
            return;

        _syncingTakeoffTreeSelection = true;
        try
        {
            ExpandTreeItemAndAncestorsWithoutTracking(tvi);
            tvi.IsSelected = true;
            tvi.BringIntoView();
        }
        finally
        {
            _syncingTakeoffTreeSelection = false;
        }
    }

    private TreeViewItem? FindTakeoffTreeItem(TakeoffItem item) =>
        FindTakeoffTreeItem(TakeoffsTree, item);

    private TreeViewItem? FindTakeoffTreeItem(ItemsControl parent, TakeoffItem item)
    {
        foreach (TreeViewItem tvi in parent.Items.OfType<TreeViewItem>())
        {
            if (tvi.Tag is TakeoffItem candidate && ReferenceEquals(candidate, item))
                return tvi;

            if (FindTakeoffTreeItem(tvi, item) is { } found)
                return found;
        }

        return null;
    }

    private TreeViewItem? FindTakeoffSectionTreeItem(ItemsControl parent, Measurement measurement)
    {
        foreach (TreeViewItem tvi in parent.Items.OfType<TreeViewItem>())
        {
            if (tvi.Tag is TakeoffMeasurementNode node && ReferenceEquals(node.Measurement, measurement))
                return tvi;

            if (FindTakeoffSectionTreeItem(tvi, measurement) is { } found)
                return found;
        }

        return null;
    }

    private TreeViewItem? FindTakeoffTreeItemByFolder(string folderPath) =>
        FindTakeoffTreeItemByFolder(TakeoffsTree, folderPath);

    private void SelectTakeoffNodeByFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return;

        if (FindTakeoffTreeItemByFolder(folderPath) is { } item)
        {
            item.IsSelected = true;
            item.IsExpanded = true;
            item.BringIntoView();
        }
    }

    private TreeViewItem? FindTakeoffTreeItemByFolder(ItemsControl parent, string folderPath)
    {
        foreach (TreeViewItem tvi in parent.Items.OfType<TreeViewItem>())
        {
            string? current = tvi.Tag switch
            {
                TakeoffItem item => item.FolderPath,
                TakeoffFolderNode folder => folder.FolderPath,
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(current) &&
                string.Equals(current, folderPath, StringComparison.OrdinalIgnoreCase))
            {
                return tvi;
            }

            if (FindTakeoffTreeItemByFolder(tvi, folderPath) is { } found)
                return found;
        }

        return null;
    }

    private static void RemoveTreeItem(TreeViewItem tvi)
    {
        ItemsControl? parent = ItemsControl.ItemsControlFromItemContainer(tvi);
        parent?.Items.Remove(tvi);
    }

    private void UpdateTotalDisplay()
    {
        RefreshEstimateTable();
        UpdateActiveTakeoffTargetBar();
        if (_activeItem == null || _activeItem.Measurements.Count == 0)
        {
            TxtTotal.Text = "Total: —";
            return;
        }
        TxtTotal.Text = $"Total: {_activeItem.TotalLabel(_viewport.ScaleMetersPerPt, _viewport.UnitMode)}";
    }

    private List<EstimateDisplayRow> BuildEstimateDisplayRows(string filter, bool currentSheetOnly)
    {
        var rows = new List<EstimateDisplayRow>();
        foreach (var item in _takeoffItems.Where(i => i.Measurements.Count > 0))
        {
            var scopedMeasurements = currentSheetOnly
                ? item.Measurements
                    .Where(measurement => _currentPage != null && IsSamePageFolder(measurement.PageFolder, _currentPage.FolderPath))
                    .ToList()
                : item.Measurements.ToList();
            if (scopedMeasurements.Count == 0)
                continue;

            bool itemMatches = EstimateItemMatchesFilter(item, filter);
            var visibleMeasurements = scopedMeasurements
                .Where(m => itemMatches || EstimateMeasurementMatchesFilter(item, m, filter))
                .ToList();
            if (!itemMatches && visibleMeasurements.Count == 0)
                continue;

            rows.Add(new EstimateDisplayRow(
                item.Name,
                currentSheetOnly ? $"{TakeoffTypeDisplay(item)} / {_currentPage?.Name}" : TakeoffTypeDisplay(item),
                scopedMeasurements.Count.ToString(CultureInfo.InvariantCulture),
                currentSheetOnly ? SheetLegendQuantityText(item, scopedMeasurements) : QuantityText(item),
                TakeoffUnitText(item),
                UnitPriceText(item),
                currentSheetOnly ? CostText(item, scopedMeasurements) : CostText(item),
                "",
                item,
                null));
            for (int i = 0; i < item.Measurements.Count; i++)
            {
                Measurement measurement = item.Measurements[i];
                if (!scopedMeasurements.Contains(measurement) || !visibleMeasurements.Contains(measurement))
                    continue;

                rows.Add(new EstimateDisplayRow(
                    $"  {SectionDisplayName(item, measurement, i)}",
                    $"{(measurement.JoistEnabled ? "Joist" : MeasurementTypeSign(measurement.MType))} {SectionPageName(measurement)}".Trim(),
                    "",
                    QuantityText(measurement),
                    MeasurementUnitText(measurement),
                    "",
                    "",
                    measurement.Notes,
                    item,
                    measurement));
            }
        }

        return rows;
    }

    private void RefreshEstimateTable()
    {
        if (_estimateList == null)
            return;

        Measurement? selectedMeasurement = (_estimateList.SelectedItem as EstimateDisplayRow)?.Measurement;
        _syncingEstimateSelection = true;
        try
        {
            string filter = _estimateFilterBox?.Text.Trim() ?? "";
            bool currentSheetOnly = _estimateCurrentSheetOnlyBox?.IsChecked == true && _currentPage != null;
            _estimateList.Items.Clear();
            EstimateDisplayRow? selectedRow = null;
            foreach (var item in _takeoffItems.Where(i => i.Measurements.Count > 0))
            {
                var scopedMeasurements = currentSheetOnly
                    ? item.Measurements
                        .Where(measurement => _currentPage != null && IsSamePageFolder(measurement.PageFolder, _currentPage.FolderPath))
                        .ToList()
                    : item.Measurements.ToList();
                if (scopedMeasurements.Count == 0)
                    continue;

                bool itemMatches = EstimateItemMatchesFilter(item, filter);
                var visibleMeasurements = scopedMeasurements
                    .Where(m => itemMatches || EstimateMeasurementMatchesFilter(item, m, filter))
                    .ToList();
                if (!itemMatches && visibleMeasurements.Count == 0)
                    continue;

                _estimateList.Items.Add(new EstimateDisplayRow(
                    item.Name,
                    currentSheetOnly ? $"{TakeoffTypeDisplay(item)} / {_currentPage?.Name}" : TakeoffTypeDisplay(item),
                    scopedMeasurements.Count.ToString(CultureInfo.InvariantCulture),
                    currentSheetOnly ? SheetLegendQuantityText(item, scopedMeasurements) : QuantityText(item),
                    TakeoffUnitText(item),
                    UnitPriceText(item),
                    currentSheetOnly ? CostText(item, scopedMeasurements) : CostText(item),
                    "",
                    item,
                    null));
                for (int i = 0; i < item.Measurements.Count; i++)
                {
                    Measurement measurement = item.Measurements[i];
                    if (!scopedMeasurements.Contains(measurement) || !visibleMeasurements.Contains(measurement))
                        continue;

                    var row = new EstimateDisplayRow(
                        $"  {SectionDisplayName(item, measurement, i)}",
                        $"{(measurement.JoistEnabled ? "□╱" : MeasurementTypeSign(measurement.MType))} {SectionPageName(measurement)}".Trim(),
                        "",
                        QuantityText(measurement),
                        MeasurementUnitText(measurement),
                        "",
                        "",
                        measurement.Notes,
                        item,
                        measurement);
                    _estimateList.Items.Add(row);
                    if (selectedMeasurement != null && ReferenceEquals(selectedMeasurement, measurement))
                        selectedRow = row;
                }
            }

            if (selectedRow != null)
            {
                _estimateList.SelectedItem = selectedRow;
                _estimateList.ScrollIntoView(selectedRow);
            }
        }
        finally
        {
            _syncingEstimateSelection = false;
        }
    }

    private static bool EstimateItemMatchesFilter(TakeoffItem item, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        return TextContains(item.Name, filter) ||
               TextContains(TakeoffTypeTitle(item), filter);
    }

    private static bool EstimateMeasurementMatchesFilter(TakeoffItem item, Measurement measurement, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        return TextContains(item.Name, filter) ||
               TextContains(MeasurementTypeTitle(measurement.MType), filter) ||
               TextContains(SectionDisplayName(item, measurement, item.Measurements.IndexOf(measurement)), filter) ||
               TextContains(SectionPageName(measurement), filter) ||
               TextContains(measurement.Notes, filter);
    }

    private static bool TextContains(string value, string filter) =>
        value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

    private void DeleteSection(TakeoffItem item, Measurement measurement)
    {
        DeleteTakeoffSections(new TakeoffMeasurementNode(item, measurement));
    }

    private void DeleteTakeoffSections(TakeoffMeasurementNode anchor)
    {
        var selectedNodes = SelectedTakeoffSectionNodes(anchor, fallbackToAnchor: true);
        if (selectedNodes.Count == 0)
            return;

        string entryTitle = selectedNodes.Count == 1
            ? MeasurementEntryTitle(anchor.Item)
            : MeasurementEntryTitlePlural(selectedNodes);
        if (MessageBox.Show(
                selectedNodes.Count == 1
                    ? $"Delete this {entryTitle.ToLowerInvariant()} from {anchor.Item.Name}?"
                    : $"Delete {selectedNodes.Count} selected {entryTitle}?",
                selectedNodes.Count == 1 ? $"Delete {entryTitle}" : "Delete Takeoff Rows",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var removedMeasurements = selectedNodes
            .Select(node => node.Measurement)
            .Distinct()
            .ToList();
        foreach (var group in selectedNodes.GroupBy(node => node.Item))
        {
            foreach (Measurement measurement in group.Select(node => node.Measurement).Distinct())
                group.Key.Measurements.Remove(measurement);

            SmartTakeoffsJobStore.SaveTakeoffItem(group.Key);
            RefreshTreeItem(group.Key);
        }

        _takeoffSectionMultiSelection.Clear();
        _takeoffSectionRangeAnchorKey = null;
        _viewport.DeleteMeasurements(removedMeasurements);
        RefreshPagesTakeoffIndicators();
        ApplyTakeoffPageHighlights();
        RefreshSheetLegend();
        RefreshEstimateTable();
        UpdateTotalDisplay();
        TxtStatus.Text = removedMeasurements.Count == 1
            ? $"Deleted {entryTitle.ToLowerInvariant()} from {anchor.Item.Name}."
            : $"Deleted {removedMeasurements.Count} selected {entryTitle}.";
    }

    private static string SectionDisplayName(TakeoffItem item, Measurement measurement, int index) =>
        string.IsNullOrWhiteSpace(measurement.Name)
            ? DefaultSectionName(item, measurement, index)
            : measurement.Name;

    private static string DefaultSectionName(TakeoffItem item, Measurement measurement) =>
        DefaultSectionName(item, measurement, Math.Max(0, item.Measurements.IndexOf(measurement)));

    private static string DefaultSectionName(TakeoffItem item, Measurement measurement, int index)
    {
        string page = SectionPageName(measurement);
        string entry = SmartTakeoffsJobStore.NormalizeMeasurementType(item.MeasurementType) == "point" ? "Count" : "Section";
        return string.IsNullOrWhiteSpace(page)
            ? $"{entry} {index + 1}"
            : $"{entry} {index + 1} - {page}";
    }

    private static string SectionPageName(Measurement measurement) =>
        string.IsNullOrWhiteSpace(measurement.PageFolder)
            ? ""
            : SmartTakeoffsJobStore.DisplayName(measurement.PageFolder);

    private static string SectionCountLabel(TakeoffItem item) =>
        SmartTakeoffsJobStore.NormalizeMeasurementType(item.MeasurementType) == "point"
            ? item.Measurements.Count == 1 ? "1 count" : $"{item.Measurements.Count} counts"
            : item.Measurements.Count == 1 ? "1 section" : $"{item.Measurements.Count} sections";

    private static string MeasurementEntryTitle(TakeoffItem item) =>
        SmartTakeoffsJobStore.NormalizeMeasurementType(item.MeasurementType) == "point" ? "Count" : "Section";

    private static string MeasurementEntryTitlePlural(IEnumerable<TakeoffMeasurementNode> nodes)
    {
        var types = nodes
            .Select(node => SmartTakeoffsJobStore.NormalizeMeasurementType(node.Item.MeasurementType))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (types.Count == 1)
            return types[0] == "point" ? "counts" : "sections";
        return "section/count rows";
    }

    private static bool CanRemoveMeasurementVertex(Measurement measurement) =>
        measurement.MType switch
        {
            "line" => measurement.Points.Count > 2,
            "area" => measurement.Points.Count > 3,
            _ => false,
        };

    private static string SectionTooltip(TakeoffItem item)
    {
        var lines = new List<string> { SectionCountLabel(item) };
        for (int i = 0; i < item.Measurements.Count; i++)
        {
            Measurement m = item.Measurements[i];
            string page = string.IsNullOrWhiteSpace(m.PageFolder)
                ? "unknown page"
                : SmartTakeoffsJobStore.DisplayName(m.PageFolder);
            string name = string.IsNullOrWhiteSpace(m.Name)
                ? (SmartTakeoffsJobStore.NormalizeMeasurementType(item.MeasurementType) == "point" ? $"Count {i + 1}" : $"Section {i + 1}")
                : m.Name;
            string detail = SmartTakeoffsJobStore.NormalizeMeasurementType(item.MeasurementType) == "point"
                ? "1 count"
                : $"{m.Points.Count} point(s)";
            lines.Add($"{name}: {page}, {detail}");
            if (!string.IsNullOrWhiteSpace(m.Notes))
                lines.Add($"  Notes: {m.Notes}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string? TakeoffItemTooltip(TakeoffItem item, bool isActive)
    {
        var lines = new List<string>();
        if (isActive)
            lines.Add("Active takeoff target");
        if (!string.IsNullOrWhiteSpace(item.Notes))
            lines.Add($"Notes: {item.Notes}");
        if (item.Measurements.Count > 0)
            lines.Add(SectionTooltip(item));

        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private string QuantityText(TakeoffItem item)
    {
        string mt = SmartTakeoffsJobStore.NormalizeMeasurementType(item.MeasurementType);
        double value = item.Total(_viewport.ScaleMetersPerPt);
        if (item.IsJoistArea)
            return QuantityText("line", value);
        return QuantityText(mt, value);
    }

    private string QuantityText(Measurement measurement)
    {
        string mt = SmartTakeoffsJobStore.NormalizeMeasurementType(measurement.MType);
        double value = measurement.Value(_viewport.ScaleMetersPerPt);
        if (measurement.JoistEnabled)
            return QuantityText("line", value);
        return QuantityText(mt, value);
    }

    private string QuantityText(string mt, double value)
    {
        return mt switch
        {
            "line" => _viewport.UnitMode == UnitMode.Imperial
                ? (value / 0.3048).ToString("F2", CultureInfo.InvariantCulture)
                : value.ToString("F2", CultureInfo.InvariantCulture),
            "area" => _viewport.UnitMode == UnitMode.Imperial
                ? (value / 0.0929030).ToString("F2", CultureInfo.InvariantCulture)
                : value.ToString("F2", CultureInfo.InvariantCulture),
            "point" => value.ToString("F0", CultureInfo.InvariantCulture),
            _ => value.ToString("F2", CultureInfo.InvariantCulture),
        };
    }

    private string UnitText(string measurementType)
    {
        string mt = SmartTakeoffsJobStore.NormalizeMeasurementType(measurementType);
        return mt switch
        {
            "line" => _viewport.UnitMode == UnitMode.Imperial ? "ft" : "m",
            "area" => _viewport.UnitMode == UnitMode.Imperial ? "sf" : "m2",
            "point" => "ea",
            _ => "",
        };
    }

    private static string UnitPriceText(TakeoffItem item) =>
        item.UnitPrice > 0 ? item.UnitPrice.ToString("F2", CultureInfo.InvariantCulture) : "";

    private string CostText(TakeoffItem item)
    {
        if (item.UnitPrice <= 0 || item.Measurements.Count == 0)
            return "";

        double quantity = EstimateQuantity(item);
        return (quantity * item.UnitPrice).ToString("F2", CultureInfo.InvariantCulture);
    }

    private string CostText(TakeoffItem item, IReadOnlyList<Measurement> measurements)
    {
        if (item.UnitPrice <= 0 || measurements.Count == 0)
            return "";

        double quantity = EstimateQuantity(item, measurements);
        return (quantity * item.UnitPrice).ToString("F2", CultureInfo.InvariantCulture);
    }

    private double EstimateQuantity(TakeoffItem item)
    {
        string mt = SmartTakeoffsJobStore.NormalizeMeasurementType(item.MeasurementType);
        double value = item.Total(_viewport.ScaleMetersPerPt);
        return mt switch
        {
            _ when item.IsJoistArea && _viewport.UnitMode == UnitMode.Imperial => value / 0.3048,
            "line" when _viewport.UnitMode == UnitMode.Imperial => value / 0.3048,
            "area" when _viewport.UnitMode == UnitMode.Imperial => value / 0.0929030,
            _ => value,
        };
    }

    private double EstimateQuantity(TakeoffItem item, IReadOnlyList<Measurement> measurements)
    {
        string mt = SmartTakeoffsJobStore.NormalizeMeasurementType(item.MeasurementType);
        double fallbackScale = _currentPage?.ScaleMetersPerPt > 0
            ? _currentPage.ScaleMetersPerPt
            : _viewport.ScaleMetersPerPt;
        double value = measurements.Sum(measurement => measurement.Value(fallbackScale));
        return mt switch
        {
            _ when measurements.Any(measurement => measurement.JoistEnabled) && _viewport.UnitMode == UnitMode.Imperial => value / 0.3048,
            "line" when _viewport.UnitMode == UnitMode.Imperial => value / 0.3048,
            "area" when _viewport.UnitMode == UnitMode.Imperial => value / 0.0929030,
            _ => value,
        };
    }

    // Viewport callbacks

    private void OnScaleChanged(double scale)
    {
        if (_currentPage != null)
            _currentPage.ScaleMetersPerPt = scale;
        ApplyScaleToCurrentPageMeasurements(scale);
        SaveCurrentPageScale();
        UpdateScaleUi(scale);
        RefreshPagesTakeoffIndicators();
        RefreshAllTotals();
    }

    private void ApplyScaleToCurrentPageMeasurements(double scale)
    {
        if (_currentPage == null || scale <= 0)
            return;

        foreach (var measurement in _takeoffItems.SelectMany(i => i.Measurements))
        {
            if (IsSamePageFolder(measurement.PageFolder, _currentPage.FolderPath))
                measurement.ScaleMetersPerPt = scale;
        }
    }

    private void OnToolChanged(string tool) => SetTool(tool);

    private void OnViewportContextRequested(ViewportContextRequest request)
    {
        if (_currentJob == null || _currentPage == null)
        {
            TxtStatus.Text = "Open a job and page before using AI Assist.";
            return;
        }

        var menu = new ContextMenu
        {
            PlacementTarget = _viewport,
            Placement = PlacementMode.MousePoint,
        };

        string point = $"PDF {request.PdfX:F0}, {request.PdfY:F0}";
        menu.Items.Add(new MenuItem
        {
            Header = request.Measurement == null
                ? $"AI Assist - {_currentPage.Name} @ {point}"
                : $"Measurement AI - {request.Measurement.MType} @ {point}",
            IsEnabled = false,
        });
        menu.Items.Add(new Separator());

        AddSheetOverlayMenuItems(menu);
        menu.Items.Add(new Separator());

        AddMeasurementClipboardMenuItems(menu, request);
        menu.Items.Add(new Separator());

        if (request.Measurement == null)
        {
            AddPdfAiMenuItems(menu, request);
        }
        else
        {
            AddMeasurementEditMenuItems(menu, request);
            menu.Items.Add(new Separator());
            AddMeasurementAiMenuItems(menu, request);
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Open Project Context", true, OpenProjectContextMarkdown));
        menu.IsOpen = true;
    }

    private void SaveViewportObservation(ViewportContextRequest request, string type, string title, string initialText)
    {
        if (_currentJob == null || _currentPage == null)
            return;

        string? text = ShowMultilineInputDialog(
            $"{title}\nPage: {_currentPage.Name}\nPoint: {request.PdfX:F1}, {request.PdfY:F1}",
            initialText,
            title);
        if (string.IsNullOrWhiteSpace(text))
            return;

        string cropDetails = BuildAiCropDetails(request, type);
        string measurementSummary = request.Measurement != null
            ? FormatMeasurementSummary(request.Measurement)
            : "";
        string details =
            $"{text.Trim()}\n\n" +
            "Context:\n" +
            $"- Page: {_currentPage.Name}\n" +
            $"- PDF point: {request.PdfX:F1}, {request.PdfY:F1}\n" +
            cropDetails;
        if (request.Measurement != null)
            details += $"- Measurement: {measurementSummary}\n";

        var observation = SmartContextStore.AddObservation(_currentJob, _currentPage, type, details);
        if (ShouldQueueAiRequest(type))
        {
            SmartContextStore.AddAiRequest(
                _currentJob,
                _currentPage,
                observation,
                type,
                text.Trim(),
                ExtractAiCropRelativePath(cropDetails),
                measurementSummary);
        }
        TxtStatus.Text = $"Saved {type} {observation.Id} -> {_currentJob.AIContextRoot}";
        LoadObservationsInbox();
    }

    private void SaveAiCropObservation(ViewportContextRequest request)
    {
        if (_currentJob == null || _currentPage == null)
            return;

        string cropDetails = BuildAiCropDetails(request, "crop_context");
        if (string.IsNullOrWhiteSpace(cropDetails))
            return;

        string title = request.Measurement == null ? "AI crop saved" : "Measurement AI crop saved";
        string details =
            $"{title}.\n\n" +
            "Context:\n" +
            $"- Page: {_currentPage.Name}\n" +
            $"- PDF point: {request.PdfX:F1}, {request.PdfY:F1}\n" +
            cropDetails;
        if (request.Measurement != null)
            details += $"- Measurement: {FormatMeasurementSummary(request.Measurement)}\n";

        var observation = SmartContextStore.AddObservation(_currentJob, _currentPage, "crop_context", details);
        TxtStatus.Text = $"Saved AI crop {observation.Id} -> {_currentJob.AIContextRoot}";
        LoadObservationsInbox();
    }

    private void SaveAiMarker(ViewportContextRequest request)
    {
        if (_currentJob == null || _currentPage == null)
            return;

        AiMarkerInput? input = ShowAiMarkerDialog(request);
        if (input == null)
            return;

        string markerType = NormalizeAiMarkerType(input.MarkerType);
        string sampleKind = NormalizeAiMarkerSampleKind(input.SampleKind);
        if (!TrySaveAiCrop(request, $"marker_{markerType}", out string cropPath, out SKRect cropRect, out string error))
        {
            TxtStatus.Text = $"AI marker crop skipped: {error}";
            MessageBox.Show(
                $"Cannot save marker crop:\n{error}",
                "Save AI Marker",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        string measurementSummary = request.Measurement != null
            ? FormatMeasurementSummary(request.Measurement)
            : "";

        var details = new StringBuilder();
        details.AppendLine("AI marker saved.");
        details.AppendLine();
        details.AppendLine("Marker:");
        details.AppendLine($"- Type: {markerType}");
        details.AppendLine($"- Sample kind: {sampleKind}");
        if (!string.IsNullOrWhiteSpace(input.Value))
            details.AppendLine($"- Value: {input.Value.Trim()}");
        if (!string.IsNullOrWhiteSpace(input.Note))
            details.AppendLine($"- Note: {input.Note.Trim()}");
        details.AppendLine();
        details.AppendLine("Context:");
        details.AppendLine($"- Page: {_currentPage.Name}");
        details.AppendLine($"- PDF point: {request.PdfX:F1}, {request.PdfY:F1}");
        details.AppendLine($"- AI crop: {cropPath}");
        details.AppendLine($"- PDF crop: {FormatPdfRect(cropRect)}");
        if (request.Measurement != null)
            details.AppendLine($"- Measurement: {measurementSummary}");

        SmartObservation observation = SmartContextStore.AddObservation(
            _currentJob,
            _currentPage,
            "ai_marker",
            details.ToString());

        SmartAiMarker marker = SmartContextStore.SaveAiMarker(
            _currentJob,
            _currentPage,
            observation,
            markerType,
            sampleKind,
            input.Value,
            input.Note,
            cropPath,
            request.PdfX,
            request.PdfY,
            cropRect.Left,
            cropRect.Top,
            cropRect.Right,
            cropRect.Bottom);

        RefreshAiMarkersOverlay();
        LoadObservationsInbox();
        TxtStatus.Text = $"Saved AI marker {marker.Type} on {_currentPage.Name}.";
    }

    private AiMarkerInput? ShowAiMarkerDialog(ViewportContextRequest request)
    {
        string contextText = $"Page: {_currentPage?.Name ?? ""}   Point: {request.PdfX:F1}, {request.PdfY:F1}";
        return ShowAiMarkerDialog("Save AI Marker", contextText, "", "positive", "", "");
    }

    private AiMarkerInput? ShowAiMarkerDialog(SmartAiMarker marker)
    {
        string contextText = $"Page: {marker.Page}   Point: {marker.PdfPoint.X:F1}, {marker.PdfPoint.Y:F1}";
        return ShowAiMarkerDialog(
            "Edit AI Marker",
            contextText,
            marker.Type,
            marker.SampleKind,
            marker.Value,
            marker.Note);
    }

    private AiMarkerInput? ShowAiMarkerDialog(
        string title,
        string contextText,
        string markerTypeValue,
        string sampleKindValue,
        string value,
        string note)
    {
        var win = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = this,
        };

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock
        {
            Text = contextText,
            Margin = new Thickness(0, 0, 0, 10),
            FontWeight = FontWeights.Normal,
        });

        string markerTypeText = string.IsNullOrWhiteSpace(markerTypeValue) ? AiMarkerTypes[0] : markerTypeValue.Trim();
        panel.Children.Add(new TextBlock { Text = "Marker type", Margin = new Thickness(0, 0, 0, 4) });
        var typeBox = new ComboBox
        {
            ItemsSource = AiMarkerTypes,
            IsEditable = true,
            Text = markerTypeText,
            SelectedItem = AiMarkerTypes.FirstOrDefault(type =>
                string.Equals(type, markerTypeText, StringComparison.OrdinalIgnoreCase)),
            Margin = new Thickness(0, 0, 0, 8),
        };
        typeBox.Text = markerTypeText;
        panel.Children.Add(typeBox);

        string sampleKindText = NormalizeAiMarkerSampleKind(sampleKindValue);
        panel.Children.Add(new TextBlock { Text = "Sample kind", Margin = new Thickness(0, 0, 0, 4) });
        var sampleBox = new ComboBox
        {
            ItemsSource = AiMarkerSampleKinds,
            IsEditable = false,
            SelectedItem = AiMarkerSampleKinds.FirstOrDefault(kind =>
                string.Equals(kind, sampleKindText, StringComparison.OrdinalIgnoreCase)) ?? "positive",
            Margin = new Thickness(0, 0, 0, 8),
        };
        panel.Children.Add(sampleBox);

        panel.Children.Add(new TextBlock { Text = "Value / measurement text", Margin = new Thickness(0, 0, 0, 4) });
        var valueBox = new TextBox { Text = value, Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(valueBox);

        panel.Children.Add(new TextBlock { Text = "Note", Margin = new Thickness(0, 0, 0, 4) });
        var noteBox = new TextBox
        {
            Text = note,
            Height = 76,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        panel.Children.Add(noteBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var ok = new Button { Content = "Save", Width = 78, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 78, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        win.Content = panel;

        AiMarkerInput? result = null;
        ok.Click += (_, _) =>
        {
            string markerType = typeBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(markerType))
            {
                MessageBox.Show("Marker type is required.", title,
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string sampleKind = sampleBox.SelectedItem?.ToString() ?? "positive";
            result = new AiMarkerInput(markerType, sampleKind, valueBox.Text.Trim(), noteBox.Text.Trim());
            win.DialogResult = true;
        };
        win.Loaded += (_, _) => typeBox.Focus();

        return win.ShowDialog() == true ? result : null;
    }

    private static string NormalizeAiMarkerType(string value)
    {
        string normalized = SafeFileNamePart(value).ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "manual_marker" : normalized;
    }

    private static string NormalizeAiMarkerSampleKind(string value)
    {
        if (value.Contains("ignore", StringComparison.OrdinalIgnoreCase))
            return "ignore";
        if (value.Contains("neg", StringComparison.OrdinalIgnoreCase))
            return "negative";
        return "positive";
    }

    private void RefreshAiMarkersOverlay()
    {
        if (_currentJob == null || _currentPage == null)
        {
            _viewport.ClearAiMarkers();
            return;
        }

        try
        {
            var markers = SmartContextStore.LoadAiMarkers(_currentJob)
                .Where(marker => MarkerBelongsToPage(marker, _currentPage, _currentJob))
                .Where(marker => !_hiddenAiMarkerTypes.Contains(marker.Type))
                .ToList();
            _viewport.SetAiMarkers(markers);
        }
        catch
        {
            _viewport.ClearAiMarkers();
        }
    }

    private static bool MarkerBelongsToPage(SmartAiMarker marker, PageInfo page, SmartTakeoffsJob job)
    {
        if (string.Equals(marker.Page, page.Name, StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.IsNullOrWhiteSpace(marker.PageFolder))
            return false;

        string markerFolder = Path.IsPathFullyQualified(marker.PageFolder)
            ? marker.PageFolder
            : Path.Combine(job.RootPath, marker.PageFolder);

        return string.Equals(
            Path.GetFullPath(markerFolder),
            Path.GetFullPath(page.FolderPath),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldQueueAiRequest(string type) =>
        type is "ai_request" or
                "text_read_request" or
                "pending_check" or
                "trace_request" or
                "trace_area_request" or
                "missed_takeoff_check" or
                "measurement_explain_request" or
                "find_similar_request" or
                "find_similar_marker_request" or
                "roof_recognition_request" or
                "measurement_link_request" or
                "crop_bookmark_request" or
                "pdf_sheet_metadata_fallback";

    private string BuildAiCropDetails(ViewportContextRequest request, string type)
    {
        if (!TrySaveAiCrop(request, type, out string relativePath, out SKRect cropRect, out string error))
        {
            if (!string.IsNullOrWhiteSpace(error))
                TxtStatus.Text = $"AI crop skipped: {error}";
            return "";
        }

        return
            $"- AI crop: {relativePath}\n" +
            $"- PDF crop: {FormatPdfRect(cropRect)}\n";
    }

    private bool TrySaveAiCrop(
        ViewportContextRequest request,
        string type,
        out string relativePath,
        out SKRect cropRect,
        out string error)
    {
        relativePath = "";
        cropRect = SKRect.Empty;
        error = "";

        if (_currentJob == null || _currentPage == null)
        {
            error = "No current job/page.";
            return false;
        }

        string cropsRoot = Path.Combine(_currentJob.AIContextRoot, "crops");
        string x = request.PdfX.ToString("F0", CultureInfo.InvariantCulture);
        string y = request.PdfY.ToString("F0", CultureInfo.InvariantCulture);
        string fileName =
            $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{SafeFileNamePart(type)}_{SafeFileNamePart(_currentPage.Name)}_{x}_{y}.png";
        string cropPath = Path.Combine(cropsRoot, fileName);

        bool saved = request.Measurement == null
            ? _viewport.TrySaveContextCrop(request.PdfX, request.PdfY, 240f, cropPath, out cropRect, out error)
            : _viewport.TrySaveMeasurementCrop(request.Measurement, 96f, cropPath, out cropRect, out error);

        if (!saved)
            return false;

        relativePath = Path.GetRelativePath(_currentJob.AIContextRoot, cropPath);
        return true;
    }

    private static string FormatPdfRect(SKRect rect) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "left={0:F1}, top={1:F1}, right={2:F1}, bottom={3:F1}",
            rect.Left,
            rect.Top,
            rect.Right,
            rect.Bottom);

    private static string SafeFileNamePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "context";

        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sb = new StringBuilder(value.Length);
        foreach (char ch in value.Trim())
        {
            if (invalid.Contains(ch))
                sb.Append('_');
            else if (char.IsWhiteSpace(ch))
                sb.Append('_');
            else if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
                sb.Append(ch);
            else
                sb.Append('_');
        }

        string safe = sb.ToString().Trim('_');
        if (safe.Length == 0)
            return "context";
        return safe.Length <= 60 ? safe : safe[..60];
    }

    private static string ExtractAiCropRelativePath(string text)
    {
        foreach (string line in (text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("- AI crop:", StringComparison.OrdinalIgnoreCase))
                continue;

            return trimmed["- AI crop:".Length..].Trim();
        }

        return "";
    }

    private void SuggestTakeoffItemFromContext(ViewportContextRequest request)
    {
        if (_currentJob == null || _currentPage == null)
            return;

        string measurementType = CurrentToolMeasurementType();
        string defaultName = $"{_currentPage.Name} {MeasurementTypeTitle(measurementType)}";
        string? name = ShowInputDialog("Suggested takeoff item name:", defaultName, "Suggest Takeoff Item");
        if (string.IsNullOrWhiteSpace(name))
            return;

        try
        {
            string parentFolder = NewTakeoffItemParentFolder();
            var item = CreateUniqueTakeoffItem(name, "#2196F3", measurementType, parentFolder);
            _takeoffItems.Add(item);
            var parent = FindTakeoffTreeItemByFolder(parentFolder) ?? (ItemsControl)TakeoffsTree;
            var tvi = AddTakeoffTreeItem(item, parent);
            if (parent is TreeViewItem parentTvi)
                parentTvi.IsExpanded = true;
            tvi.IsSelected = true;

            string note =
                $"Suggested takeoff item created: {item.Name}\n\n" +
                "Context:\n" +
                $"- Page: {_currentPage.Name}\n" +
                $"- PDF point: {request.PdfX:F1}, {request.PdfY:F1}\n" +
                BuildAiCropDetails(request, "takeoff_suggestion") +
                $"- Measurement type: {measurementType}";
            SmartContextStore.AddObservation(_currentJob, _currentPage, "takeoff_suggestion", note);
            TxtStatus.Text = $"Created suggested takeoff item: {item.Name}";
        }
        catch (Exception ex)
        {
            ShowOperationError("Suggest Takeoff Item", ex);
        }
    }

    private string FormatMeasurementSummary(Measurement measurement) =>
        $"{measurement.MType}, {measurement.Label(_viewport.ScaleMetersPerPt, _viewport.UnitMode)}, " +
        $"points={measurement.Points.Count}, scale={measurement.ScaleMetersPerPt:G6}, takeoff={measurement.TakeoffFolder}";

    private void OpenProjectContextMarkdown()
    {
        if (_currentJob == null)
            return;

        SmartContextStore.EnsureProjectContext(_currentJob.RootPath, _currentJob.Name);
        string path = Path.Combine(_currentJob.AIContextRoot, "project.md");
        Process.Start(new ProcessStartInfo
        {
            FileName = "notepad.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true,
        });
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    private void ApplyPersistedSettings()
    {
        _isApplyingSettings = true;
        try
        {
            _viewport.UnitMode = _settings.UnitMode == UnitMode.Metric.ToString()
                ? UnitMode.Metric
                : UnitMode.Imperial;
            ComboFolderTemplateMode.SelectedIndex = NormalizeFolderTemplateMode(_settings.FolderTemplateMode) switch
            {
                "COM" => 1,
                "EWP" => 2,
                _ => 0,
            };
            ApplyViewportBackground(_settings.ViewportBackground, persist: false);
            ApplyTheme(string.Equals(_settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase), persist: false);
            ApplyDisplaySettingsToViewport();
            ApplySheetOverlaySettings();
            ApplySidePanelWidths();
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    private void TryOpenLastJobFromSettings()
    {
        if (string.IsNullOrWhiteSpace(_settings.LastJobPath) || !Directory.Exists(_settings.LastJobPath))
        {
            ShowStartupJobPickerIfUseful();
            return;
        }

        try
        {
            OpenJob(_settings.LastJobPath, initialPageFolder: _settings.LastPageFolder);
            TxtStatus.Text = $"Loaded last job: {_currentJob?.Name}. Select a page to render it.";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Last job could not be opened: {ex.Message}";
            ShowStartupJobPickerIfUseful();
        }
    }

    private void SaveCurrentPageScale()
    {
        if (_currentPage == null)
            return;

        _currentPage.ScaleMetersPerPt = _viewport.ScaleMetersPerPt;
        ApplyScaleToCurrentPageMeasurements(_viewport.ScaleMetersPerPt);
        SmartTakeoffsJobStore.SavePageScale(_currentPage.FolderPath, _viewport.ScaleMetersPerPt);
    }

    private void ApplyViewportBackground(string color, bool persist)
    {
        string cleanColor = string.IsNullOrWhiteSpace(color) ? "#FFFFFF" : color;
        _viewport.ViewBackgroundColor = cleanColor;
        ViewportHost.Background = new SolidColorBrush(ParseWpfColor(cleanColor, Colors.White));
        _viewport.InvalidateVisual();

        if (persist)
        {
            _settings.ViewportBackground = cleanColor;
            SaveAppSettings();
        }
    }

    private void ApplyTheme(bool dark, bool persist)
    {
        bool wasApplying = _isApplyingSettings;
        _isApplyingSettings = true;
        try
        {
            BtnDisplayDarkTheme.IsChecked = dark;
            BtnDisplayDarkTheme.Content = dark ? "Light" : "Dark";
        }
        finally
        {
            _isApplyingSettings = wasApplying;
        }

        Color window = dark ? Color.FromRgb(30, 32, 35) : Color.FromRgb(240, 240, 240);
        Color toolbar = dark ? Color.FromRgb(43, 45, 49) : Color.FromRgb(240, 240, 240);
        Color panel = dark ? Color.FromRgb(37, 39, 42) : Color.FromRgb(245, 245, 245);
        Color status = dark ? Color.FromRgb(43, 45, 49) : Color.FromRgb(232, 232, 232);
        Color tree = dark ? Color.FromRgb(31, 33, 36) : Colors.White;
        Color splitter = dark ? Color.FromRgb(68, 72, 78) : Color.FromRgb(204, 204, 204);
        Brush foreground = new SolidColorBrush(dark ? Color.FromRgb(230, 230, 230) : Color.FromRgb(30, 30, 30));
        UpdateAppBrush("WindowBackgroundBrush", window);
        UpdateAppBrush("PanelBackgroundBrush", panel);
        UpdateAppBrush("SurfaceBackgroundBrush", tree);
        UpdateAppBrush("SplitterBrush", splitter);
        UpdateAppBrush("SecondaryForegroundBrush", dark ? Color.FromRgb(176, 179, 184) : Color.FromRgb(102, 102, 102));
        UpdateAppBrush("ScrollBarTrackBrush", dark ? Color.FromRgb(45, 47, 52)  : Color.FromRgb(220, 220, 220));
        UpdateAppBrush("ScrollBarThumbBrush", dark ? Color.FromRgb(90, 93, 100) : Color.FromRgb(160, 160, 160));
        UpdateAppBrush("ControlForegroundBrush", dark ? Color.FromRgb(238, 238, 238) : Color.FromRgb(32, 32, 32));
        UpdateAppBrush("ControlBackgroundBrush", dark ? Color.FromRgb(58, 61, 66) : Color.FromRgb(248, 248, 248));
        UpdateAppBrush("ControlBorderBrush", dark ? Color.FromRgb(118, 122, 130) : Color.FromRgb(160, 160, 160));
        UpdateAppBrush("ControlHoverBackgroundBrush", dark ? Color.FromRgb(72, 76, 82) : Color.FromRgb(232, 232, 232));
        UpdateAppBrush("ControlPressedBackgroundBrush", dark ? Color.FromRgb(86, 91, 98) : Color.FromRgb(208, 208, 208));
        UpdateAppBrush("ControlActiveBackgroundBrush", dark ? Color.FromRgb(37, 99, 160) : Color.FromRgb(204, 229, 255));
        UpdateAppBrush("ControlActiveForegroundBrush", dark ? Colors.White : Color.FromRgb(17, 17, 17));
        UpdateAppBrush("AccentBrush", dark ? Color.FromRgb(90, 160, 235) : Color.FromRgb(37, 99, 166));
        UpdateAppBrush("AccentHoverBrush", dark ? Color.FromRgb(112, 178, 245) : Color.FromRgb(31, 85, 145));
        UpdateAppBrush("AccentPressedBrush", dark ? Color.FromRgb(70, 135, 210) : Color.FromRgb(24, 68, 111));
        UpdateAppBrush("AccentForegroundBrush", Colors.White);
        UpdateAppBrush("ToolbarBandBrush", dark ? Color.FromRgb(45, 48, 54) : Color.FromRgb(236, 239, 243));
        UpdateAppBrush("ManagerHeaderBrush", dark ? Color.FromRgb(50, 56, 66) : Color.FromRgb(232, 238, 246));
        UpdateAppBrush("SubtleButtonBackgroundBrush", dark ? Color.FromRgb(50, 53, 58) : Color.FromRgb(243, 244, 246));
        UpdateAppBrush("DataGridAltRowBrush", dark ? Color.FromRgb(34, 37, 42) : Color.FromRgb(247, 249, 252));
        UpdateAppBrush("CommitBrush", dark ? Color.FromRgb(70, 150, 82) : Color.FromRgb(46, 125, 50));
        UpdateAppBrush("CommitHoverBrush", dark ? Color.FromRgb(84, 168, 96) : Color.FromRgb(39, 109, 44));
        UpdateAppBrush("CommitPressedBrush", dark ? Color.FromRgb(52, 122, 63) : Color.FromRgb(29, 84, 33));

        // Tree row state — theme-aware (paired light/dark variants)
        UpdateAppBrush("RowOnPageBrush",        dark ? Color.FromRgb(34, 64, 46)   : Color.FromRgb(214, 245, 222));
        UpdateAppBrush("RowActiveBrush",        dark ? Color.FromRgb(82, 64, 24)   : Color.FromRgb(255, 236, 190));
        UpdateAppBrush("RowMultiSelectBrush",   dark ? Color.FromRgb(38, 70, 110)  : Color.FromRgb(205, 226, 255));
        UpdateAppBrush("RowDropOkBrush",        dark ? Color.FromRgb(40, 86, 58)   : Color.FromRgb(204, 245, 218));
        UpdateAppBrush("RowDropBadBrush",       dark ? Color.FromRgb(110, 48, 48)  : Color.FromRgb(255, 214, 214));
        UpdateAppBrush("RowFlagForegroundBrush",dark ? Colors.White                : Color.FromRgb(17, 17, 17));
        UpdateAppBrush("RowActiveAccentBrush",  dark ? Color.FromRgb(120, 170, 255): Color.FromRgb(31, 82, 166));
        SetupToolButtonContent();

        Background = new SolidColorBrush(window);
        RootDock.Background = new SolidColorBrush(window);
        MainToolBar.Background = new SolidColorBrush(toolbar);
        MainStatusBar.Background = new SolidColorBrush(status);
        PagesPanel.Background = new SolidColorBrush(panel);
        TakeoffsPanel.Background = new SolidColorBrush(panel);
        PagesTree.Background = new SolidColorBrush(tree);
        TakeoffsTree.Background = new SolidColorBrush(tree);
        PagesTree.Foreground = foreground;
        TakeoffsTree.Foreground = foreground;
        TxtStatus.Foreground = foreground;
        TxtScaleInfo.Foreground = new SolidColorBrush(dark ? Color.FromRgb(138, 180, 248) : Color.FromRgb(0, 85, 204));
        ObservationsListView.Background  = new SolidColorBrush(tree);
        ObservationsListView.Foreground  = foreground;
        if (_estimateList != null)
        {
            _estimateList.Background = new SolidColorBrush(tree);
            _estimateList.Foreground = foreground;
        }
        if (_massingDraftTextBox != null)
        {
            _massingDraftTextBox.Background = new SolidColorBrush(tree);
            _massingDraftTextBox.Foreground = foreground;
        }
        if (_massingMarkerList != null)
        {
            _massingMarkerList.Background = new SolidColorBrush(tree);
            _massingMarkerList.Foreground = foreground;
        }
        if (_massingMarkerDetailsTextBox != null)
        {
            _massingMarkerDetailsTextBox.Background = new SolidColorBrush(tree);
            _massingMarkerDetailsTextBox.Foreground = foreground;
        }
        InboxPanel.Background           = new SolidColorBrush(tree);
        InboxHeaderBorder.Background    = new SolidColorBrush(toolbar);
        InboxSplitter.Background        = new SolidColorBrush(splitter);
        UpdateRecordButton();

        if (persist)
        {
            _settings.Theme = dark ? "Dark" : "Light";
            SaveAppSettings();
        }
    }

    private static void UpdateAppBrush(string key, Color color)
    {
        Application.Current.Resources[key] = new SolidColorBrush(color);
    }

    private void SaveAppSettings()
    {
        if (_isApplyingSettings)
            return;

        AppSettingsStore.Save(_settings);
    }

    private void ApplySidePanelWidths()
    {
        PagesColumn.Width = new GridLength(NormalizePanelWidth(_settings.LeftPanelWidth, 200.0));
        TakeoffsColumn.Width = new GridLength(NormalizePanelWidth(_settings.RightPanelWidth, 220.0));
    }

    private void SidePanelSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        SaveSidePanelWidths();
    }

    private void SidePanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isApplyingSettings || !IsLoaded || !e.WidthChanged)
            return;

        if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) >= 1.0)
            SaveSidePanelWidths();
    }

    private void SaveSidePanelWidths()
    {
        double left = NormalizePanelWidth(PagesColumn.ActualWidth, 200.0);
        double right = NormalizePanelWidth(TakeoffsColumn.ActualWidth, 220.0);
        if (Math.Abs(_settings.LeftPanelWidth - left) < 0.5 &&
            Math.Abs(_settings.RightPanelWidth - right) < 0.5)
        {
            return;
        }

        _settings.LeftPanelWidth = left;
        _settings.RightPanelWidth = right;
        SaveAppSettings();
    }

    private static double NormalizePanelWidth(double width, double fallback)
    {
        if (!double.IsFinite(width) || width < 120.0)
            return fallback;
        return Math.Clamp(width, 120.0, 640.0);
    }

    private static Color ParseWpfColor(string color, Color fallback)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(color);
        }
        catch
        {
            return fallback;
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
                yield return typed;

            foreach (T nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }

    private static string? SelectFolder(string title, string? initialFolder = null)
    {
        var dlg = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false,
        };
        if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder))
            dlg.FolderName = initialFolder;

        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    private void UpdateScaleUi(double scale)
    {
        const double PT_M = 25.4 / 72.0 / 1000.0;
        double ratio = scale > 0 ? scale / PT_M : 0;
        TxtScaleInfo.Text = ratio > 0 ? $"≈1:{ratio:F0}" : "";
        if (ratio > 0) TxtScaleRatio.Text = $"{ratio:F0}";
    }

    private string? ShowInputDialog(string prompt, string initial, string title)
    {
        var win = new Window
        {
            Title                 = title,
            Width                 = 300,
            SizeToContent         = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode            = ResizeMode.NoResize,
            Owner                 = this,
        };
        var panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 6) });
        var tb = new TextBox { Text = initial };
        panel.Children.Add(tb);
        var btns = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin              = new Thickness(0, 8, 0, 0),
        };
        var ok     = new Button { Content = "OK",     Width = 70, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 70, IsCancel  = true };
        btns.Children.Add(ok);
        btns.Children.Add(cancel);
        panel.Children.Add(btns);
        win.Content = panel;

        string? result = null;
        ok.Click += (_, _) => { result = tb.Text.Trim(); win.DialogResult = true; };
        win.Loaded += (_, _) => { tb.Focus(); tb.SelectAll(); };
        return win.ShowDialog() == true ? result : null;
    }

    private string? ShowMultilineInputDialog(string prompt, string initial, string title)
    {
        var win = new Window
        {
            Title = title,
            Width = 420,
            Height = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            Owner = this,
        };
        var panel = new DockPanel { Margin = new Thickness(10) };
        var promptBlock = new TextBlock
        {
            Text = prompt,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        };
        DockPanel.SetDock(promptBlock, Dock.Top);
        panel.Children.Add(promptBlock);

        var btns = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var ok = new Button { Content = "OK", Width = 70, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 70, IsCancel = true };
        btns.Children.Add(ok);
        btns.Children.Add(cancel);
        DockPanel.SetDock(btns, Dock.Bottom);
        panel.Children.Add(btns);

        var tb = new TextBox
        {
            Text = initial,
            AcceptsReturn = true,
            AcceptsTab = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap,
        };
        panel.Children.Add(tb);
        win.Content = panel;

        string? result = null;
        ok.Click += (_, _) => { result = tb.Text; win.DialogResult = true; };
        win.Loaded += (_, _) => { tb.Focus(); tb.SelectAll(); };
        return win.ShowDialog() == true ? result : null;
    }

    private static bool IsJobFolder(string folder) =>
        File.Exists(Path.Combine(folder, "Data.xml")) &&
        Directory.Exists(Path.Combine(folder, "Pages")) &&
        Directory.Exists(Path.Combine(folder, "Takeoffs"));

    // ── AI Inbox ─────────────────────────────────────────────────────────────

    private void BtnToggleInbox_Click(object sender, RoutedEventArgs e)
    {
        if (_inboxExpanded)
        {
            _inboxExpandedHeight = InboxRow.ActualHeight > 30 ? InboxRow.ActualHeight : _inboxExpandedHeight;
            InboxRow.Height        = new GridLength(30);
            InboxSplitterRow.Height = new GridLength(0);
            TxtInboxToggle.Text    = "+";
        }
        else
        {
            InboxRow.Height        = new GridLength(_inboxExpandedHeight);
            InboxSplitterRow.Height = new GridLength(4);
            TxtInboxToggle.Text    = "-";
        }
        _inboxExpanded = !_inboxExpanded;
    }

    private void BtnInboxMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not UIElement target)
            return;

        var menu = new ContextMenu
        {
            PlacementTarget = target,
            Placement = PlacementMode.Bottom,
        };

        menu.Items.Add(MakeMenuItem("Run New Bookmarks", _currentJob != null, () => BtnRunNewBookmarks_Click(sender, new RoutedEventArgs())));
        menu.Items.Add(MakeMenuItem("Retry Failed Bookmarks", _currentJob != null, () => BtnRetryFailedBookmarks_Click(sender, new RoutedEventArgs())));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Create Marker Set", _currentJob != null, () => BtnCreateMarkerSet_Click(sender, new RoutedEventArgs())));
        menu.Items.Add(MakeMenuItem("Manage Marker Sets...", CanManageMarkerSets(), () => BtnManageMarkerSets_Click(sender, new RoutedEventArgs())));
        menu.Items.Add(MakeMenuItem("Export Marker Context", _currentJob != null, () => BtnExportMarkers_Click(sender, new RoutedEventArgs())));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Build 3D Draft", _currentJob != null, () => BtnBuildMassingDraft_Click(sender, new RoutedEventArgs())));

        menu.IsOpen = true;
    }

    private void LoadObservationsInbox()
    {
        ObservationsListView.Items.Clear();

        if (_currentJob == null)
        {
            TxtInboxCount.Text    = "0";
            InboxBadge.Visibility = Visibility.Collapsed;
            return;
        }

        string obsPath = Path.Combine(_currentJob.AIContextRoot, "observations.jsonl");
        if (!File.Exists(obsPath))
        {
            TxtInboxCount.Text    = "0";
            InboxBadge.Visibility = Visibility.Collapsed;
            return;
        }

        var list = new List<SmartObservation>();
        foreach (string line in File.ReadLines(obsPath, System.Text.Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var obs = JsonSerializer.Deserialize<SmartObservation>(line);
                if (obs != null) list.Add(obs);
            }
            catch (JsonException) { /* skip malformed lines */ }
        }

        IReadOnlyList<SmartMarkerFeedbackRecord> markerFeedback = SmartLearningStore.LoadProjectMarkerFeedback(_currentJob);
        var displayItems = new List<ObservationDisplayItem>();
        foreach (var obs in list.OrderByDescending(o => o.CreatedAtUtc))
        {
            SmartAiMarker? marker = null;
            string markerQuality = "";
            if (string.Equals(obs.Type, "ai_marker", StringComparison.OrdinalIgnoreCase))
            {
                marker = SmartContextStore.LoadAiMarker(_currentJob, obs.Id);
                if (marker == null)
                    continue;
                markerQuality = MarkerQualityPreview(marker, markerFeedback);
            }

            if (!ShouldShowInboxObservation(marker))
                continue;

            displayItems.Add(new ObservationDisplayItem(obs, ObservationStatusPrefix(obs), marker, markerQuality));
        }

        foreach (ObservationDisplayItem item in displayItems)
            ObservationsListView.Items.Add(item);

        int count = displayItems.Count;
        TxtInboxCount.Text    = count.ToString();
        InboxBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool ShouldShowInboxObservation(SmartAiMarker? marker)
    {
        string typeFilter = SelectedMarkerTypeFilter();
        string sampleFilter = SelectedMarkerSampleFilter();

        if (marker == null)
        {
            return string.Equals(typeFilter, MarkerTypeFilterAllInbox, StringComparison.Ordinal) &&
                   string.Equals(sampleFilter, MarkerSampleFilterAny, StringComparison.Ordinal);
        }

        return MarkerMatchesCurrentFilters(marker);
    }

    private static string MarkerQualityPreview(
        SmartAiMarker marker,
        IReadOnlyList<SmartMarkerFeedbackRecord> feedback)
    {
        if (feedback.Count == 0)
            return "";

        var relevant = feedback
            .Where(record =>
                string.Equals(record.SourceMarkerId, marker.Id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(record.SourceMarkerType, marker.Type, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (relevant.Count == 0)
            return "";

        int accepted = relevant.Count(record => string.Equals(record.Outcome, "accepted", StringComparison.OrdinalIgnoreCase));
        int rejected = relevant.Count(record => string.Equals(record.Outcome, "rejected", StringComparison.OrdinalIgnoreCase));
        int applied = relevant.Count(record => record.Applied);
        var confidence = relevant
            .Where(record => record.Confidence > 0)
            .Select(record => record.Confidence)
            .ToList();
        string average = confidence.Count == 0
            ? ""
            : $" avg {confidence.Average():P0}";
        return $"fb A{accepted}/R{rejected}/appl{applied}{average}";
    }

    private string SelectedMarkerTypeFilter() =>
        ComboMarkerTypeFilter.SelectedItem?.ToString() ?? MarkerTypeFilterAllInbox;

    private string SelectedMarkerSampleFilter() =>
        ComboMarkerSampleFilter.SelectedItem?.ToString() ?? MarkerSampleFilterAny;

    private string ObservationStatusPrefix(SmartObservation observation)
    {
        if (_currentJob == null)
            return "";

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, observation.Id);
        if (request == null)
            return "";

        return $"[{request.Status}] ";
    }

    private ContextMenu BuildObservationsContextMenu()
    {
        var menu = new ContextMenu();
        menu.Opened += (_, _) =>
        {
            menu.Items.Clear();
            ObservationDisplayItem? selected = SelectedObservationDisplayItem();
            if (selected == null)
            {
                menu.Items.Add(new MenuItem { Header = "No AI Inbox entry selected", IsEnabled = false });
                menu.Items.Add(new Separator());
                menu.Items.Add(MakeMenuItem("Refresh Inbox", true, LoadObservationsInbox));
                return;
            }

            menu.Items.Add(MakeMenuItem("Open Details", true, () => ShowObservationDetailsDialog(selected.Observation)));
            menu.Items.Add(MakeMenuItem("Go to Page", CanGoToObservationPage(selected), () => GoToObservationPage(selected)));
            menu.Items.Add(MakeMenuItem("Run AI Request", CanRunAiRequest(selected), async () => await RunAiRequestAsync(selected)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeSubmenu(
                "Crop",
                MakeMenuItem("Open Crop", CanOpenObservationCrop(selected), () => OpenObservationCrop(selected)),
                MakeMenuItem("Open Crop Folder", CanOpenObservationCrop(selected), () => OpenObservationCropFolder(selected)),
                MakeMenuItem("Bookmark Crop For Batch AI", CanBookmarkObservationCrop(selected), () => BookmarkObservationCrop(selected)),
                MakeMenuItem("Open Bookmark JSON", CanOpenCropBookmarkFile(selected), () => OpenCropBookmarkFile(selected))));
            menu.Items.Add(MakeSubmenu(
                "Marker",
                MakeMenuItem("Edit Marker", CanEditAiMarker(selected), () => EditAiMarker(selected)),
                MakeMenuItem("Delete Marker", CanEditAiMarker(selected), () => DeleteAiMarker(selected)),
                MakeMenuItem("Find Similar From Marker", CanFindSimilarFromMarker(selected), () => QueueFindSimilarFromMarker(selected)),
                MakeMenuItem("Create Marker Set From Filter", _currentJob != null, CreateMarkerSetFromCurrentFilter),
                MakeMenuItem("Manage Marker Sets...", CanManageMarkerSets(), ManageMarkerSets),
                MakeMenuItem("Export Marker Context", _currentJob != null, () => ExportMarkersContext(openAfterExport: true)),
                MakeMenuItem("Hide This Marker Type", CanHideAiMarkerType(selected), () => HideAiMarkerType(selected)),
                MakeMenuItem("Show All Marker Types", _hiddenAiMarkerTypes.Count > 0, ShowAllMarkerTypes)));
            menu.Items.Add(MakeSubmenu(
                "AI Response",
                MakeMenuItem("Add Manual AI Response", CanAddManualAiResponse(selected), () => AddManualAiResponse(selected)),
                MakeMenuItem("Preview Action Draft", CanPreviewAiActionDraft(selected), () => PreviewAiActionDraft(selected)),
                MakeMenuItem("Review Action Draft", CanReviewAiActionDraft(selected), () => ReviewAiActionDraft(selected)),
                MakeMenuItem("Apply Sheet Metadata Response", CanApplySheetMetadataResponse(selected), () => ApplySheetMetadataResponse(selected)),
                MakeMenuItem("Clear Action Preview", _currentJob != null, ClearAiActionDraftPreview)));
            menu.Items.Add(MakeSubmenu(
                "Files",
                MakeMenuItem("Open Request JSON", CanOpenAiRequestFile(selected), () => OpenAiRequestFile(selected)),
                MakeMenuItem("Open Layer JSON", CanOpenLayerManifest(selected), () => OpenLayerManifest(selected)),
                MakeMenuItem("Open Response JSON", CanOpenAiResponseFile(selected), () => OpenAiResponseFile(selected)),
                MakeMenuItem("Open Action Draft JSON", CanOpenAiActionDraftFile(selected), () => OpenAiActionDraftFile(selected)),
                MakeMenuItem("Open Marker JSON", CanOpenAiMarkerFile(selected), () => OpenAiMarkerFile(selected))));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Open Project Context", _currentJob != null, OpenProjectContextMarkdown));
            menu.Items.Add(MakeMenuItem("Refresh Inbox", true, LoadObservationsInbox));
        };
        return menu;
    }

    private void ObservationsListView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OpenSelectedInboxObservation();
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            LoadObservationsInbox();
            e.Handled = true;
        }
    }

    private ObservationDisplayItem? SelectedObservationDisplayItem() =>
        ObservationsListView.SelectedItem as ObservationDisplayItem;

    private void OpenSelectedInboxObservation()
    {
        if (SelectedObservationDisplayItem() is { } selected)
            ShowObservationDetailsDialog(selected.Observation);
    }

    private async void BtnRunAi_Click(object sender, RoutedEventArgs e)
    {
        await RunSelectedOrNextAiRequestAsync();
    }

    private async void BtnRunNewBookmarks_Click(object sender, RoutedEventArgs e)
    {
        await RunNewCropBookmarksAsync();
    }

    private async void BtnRetryFailedBookmarks_Click(object sender, RoutedEventArgs e)
    {
        await RetryFailedCropBookmarksAsync();
    }

    private bool CanBookmarkObservationCrop(ObservationDisplayItem item)
    {
        if (_currentJob == null || !CanOpenObservationCrop(item))
            return false;

        return SmartContextStore.FindCropBookmarkByObservation(_currentJob, item.Observation.Id) == null;
    }

    private void BookmarkObservationCrop(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return;

        SmartAiMarker? marker = SmartContextStore.LoadAiMarker(_currentJob, item.Observation.Id);
        string cropPath = !string.IsNullOrWhiteSpace(marker?.CropPath)
            ? marker.CropPath
            : item.CropRelativePath;
        if (string.IsNullOrWhiteSpace(cropPath))
        {
            TxtStatus.Text = "This Inbox entry has no crop to bookmark.";
            return;
        }

        if (SmartContextStore.FindCropBookmarkByObservation(_currentJob, item.Observation.Id) is { } existing)
        {
            TxtStatus.Text = $"Crop is already bookmarked as {existing.Id}.";
            return;
        }

        string pageFolder = marker?.PageFolder ?? ResolveObservationPageFolder(item.Observation);
        var bookmark = new SmartAiCropBookmark
        {
            SourceObservationId = item.Observation.Id,
            SourceMarkerId = marker?.Id ?? "",
            Page = marker?.Page ?? item.Page,
            PageFolder = pageFolder,
            Type = marker != null ? $"marker:{marker.Type}" : item.Observation.Type,
            CropPath = cropPath,
            Prompt = BuildCropBookmarkPrompt(item.Observation, marker),
            Status = "new",
        };

        SmartContextStore.SaveCropBookmark(_currentJob, bookmark);
        TxtStatus.Text = $"Bookmarked crop {bookmark.Id}; Run New will send it to OpenAI.";
    }

    private async Task RunNewCropBookmarksAsync()
    {
        await RunCropBookmarksAsync(
            "new",
            "No new crop bookmarks to send.",
            "new crop bookmarks");
    }

    private async Task RetryFailedCropBookmarksAsync()
    {
        await RunCropBookmarksAsync(
            "failed",
            "No failed crop bookmarks to retry.",
            "failed crop bookmarks");
    }

    private async Task RunCropBookmarksAsync(string statusFilter, string emptyMessage, string operationLabel)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before running crop bookmarks.";
            return;
        }

        if (_isRunningAiRequest)
        {
            TxtStatus.Text = "AI request is already running.";
            return;
        }

        string apiKey = ReadOpenAiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            TxtStatus.Text = "Set OPENAI_API_KEY in Windows environment, then run crop bookmarks.";
            return;
        }

        var bookmarks = SmartContextStore.LoadCropBookmarks(_currentJob)
            .Where(bookmark => string.Equals(bookmark.Status, statusFilter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(bookmark => bookmark.CreatedAtUtc)
            .ToList();
        if (bookmarks.Count == 0)
        {
            TxtStatus.Text = emptyMessage;
            return;
        }

        int done = 0;
        int failed = 0;
        int generated = 0;
        int skippedCandidates = 0;
        foreach (SmartAiCropBookmark bookmark in bookmarks)
        {
            if (!CropBookmarkFileExists(bookmark))
            {
                bookmark.Status = "failed";
                bookmark.ResultSummary = "Crop file is missing.";
                bookmark.ProcessedAtUtc = DateTime.UtcNow.ToString("O");
                SmartContextStore.SaveCropBookmark(_currentJob, bookmark);
                failed++;
                continue;
            }

            SmartAiRequest request = EnsureCropBookmarkRequest(bookmark);
            bookmark.RequestId = request.Id;
            bookmark.Status = "running";
            SmartContextStore.SaveCropBookmark(_currentJob, bookmark);

            await RunAiRequestAsync(request);

            SmartAiResponse? response = SmartContextStore.LoadAiResponse(_currentJob, request.Id);
            if (response != null && string.Equals(response.Status, "done", StringComparison.OrdinalIgnoreCase))
            {
                bookmark.Status = "done";
                bookmark.ResponseId = response.Id;
                bookmark.ResultSummary = FirstLineOrFallback(response.OutputText, "OpenAI response saved.");
                done++;
            }
            else
            {
                bookmark.Status = "failed";
                bookmark.ResponseId = response?.Id ?? "";
                bookmark.ResultSummary = FirstLineOrFallback(response?.Error ?? "", "OpenAI request failed.");
                failed++;
            }

            if (SmartContextStore.LoadAiActionDraft(_currentJob, request.Id) is { } draft)
            {
                bookmark.ActionDraftId = draft.Id;
                int skipped = 0;
                if (string.Equals(bookmark.Status, "done", StringComparison.OrdinalIgnoreCase))
                    generated += CreateCropBookmarksFromAiCandidates(bookmark, draft, out skipped);
                skippedCandidates += skipped;
            }
            bookmark.ProcessedAtUtc = DateTime.UtcNow.ToString("O");
            SmartContextStore.SaveCropBookmark(_currentJob, bookmark);
        }

        LoadObservationsInbox();
        string generatedSummary = generated > 0 || skippedCandidates > 0
            ? $", {generated} auto-new, {skippedCandidates} candidates skipped"
            : "";
        TxtStatus.Text = $"Processed {operationLabel}: {done} done, {failed} failed, {bookmarks.Count} total{generatedSummary}.";
    }

    private int CreateCropBookmarksFromAiCandidates(
        SmartAiCropBookmark sourceBookmark,
        SmartAiActionDraft draft,
        out int skipped)
    {
        skipped = 0;
        if (_currentJob == null || draft.Actions.Count == 0)
            return 0;

        if (sourceBookmark.CandidateDepth >= MaxAutoCropBookmarkDepth)
        {
            skipped += draft.Actions.Count(action => action.Points.Count > 0);
            return 0;
        }

        int created = 0;
        var existingBookmarks = SmartContextStore.LoadCropBookmarks(_currentJob).ToList();
        for (int i = 0; i < draft.Actions.Count; i++)
        {
            SmartAiAction action = draft.Actions[i];
            List<SKPoint> points = ActionPoints(action);
            if (points.Count == 0 ||
                !TryActionPointCenter(points, out SKPoint center) ||
                !TryResolveActionCandidatePage(action, draft, sourceBookmark, out PageInfo? page) ||
                page == null)
            {
                skipped++;
                continue;
            }

            string candidateKey = CropBookmarkCandidateKey(page, center);
            if (HasNearbyCropBookmarkDuplicate(existingBookmarks, page, center, candidateKey))
            {
                skipped++;
                continue;
            }

            if (!TrySaveActionCandidateCrop(page, points, action, i, out string cropPath, out SKRect cropRect, out string error))
            {
                skipped++;
                continue;
            }

            var bookmark = new SmartAiCropBookmark
            {
                SourceMarkerId = sourceBookmark.SourceMarkerId,
                SourceActionDraftId = draft.Id,
                SourceActionIndex = i,
                Page = page.Name,
                PageFolder = Path.GetRelativePath(_currentJob.RootPath, page.FolderPath),
                Type = AutoCropBookmarkType(action),
                CropPath = cropPath,
                Prompt = BuildAutoCropBookmarkPrompt(sourceBookmark, draft, action, i, cropRect),
                Status = "new",
                AutoCreated = true,
                CandidateDepth = sourceBookmark.CandidateDepth + 1,
                CandidateKey = candidateKey,
                CandidateCenter = new SmartAiActionPoint { X = center.X, Y = center.Y },
                CandidatePoints = points.Select(point => new SmartAiActionPoint { X = point.X, Y = point.Y }).ToList(),
                ResultSummary = "Auto-created from AI action candidate.",
            };

            SmartContextStore.SaveCropBookmark(_currentJob, bookmark);
            existingBookmarks.Add(bookmark);
            created++;
        }

        return created;
    }

    private bool TryResolveActionCandidatePage(
        SmartAiAction action,
        SmartAiActionDraft draft,
        SmartAiCropBookmark sourceBookmark,
        out PageInfo? page)
    {
        page = FindPageByName(action.Page) ??
               FindPageByName(draft.Page) ??
               ResolveBookmarkPage(sourceBookmark);
        return page != null;
    }

    private string CropBookmarkCandidateKey(PageInfo page, SKPoint center)
    {
        string pageKey = _currentJob == null
            ? page.Name
            : Path.GetRelativePath(_currentJob.RootPath, page.FolderPath);
        int bucketX = (int)Math.Round(center.X / AutoCropBookmarkDuplicateTolerancePt);
        int bucketY = (int)Math.Round(center.Y / AutoCropBookmarkDuplicateTolerancePt);
        return $"{pageKey}|{bucketX}|{bucketY}";
    }

    private bool HasNearbyCropBookmarkDuplicate(
        IReadOnlyList<SmartAiCropBookmark> bookmarks,
        PageInfo page,
        SKPoint center,
        string candidateKey)
    {
        foreach (SmartAiCropBookmark bookmark in bookmarks)
        {
            if (!CropBookmarkBelongsToPage(bookmark, page))
                continue;

            if (!string.IsNullOrWhiteSpace(candidateKey) &&
                string.Equals(bookmark.CandidateKey, candidateKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!TryGetCropBookmarkCenter(bookmark, out SKPoint existingCenter))
                continue;

            float dx = existingCenter.X - center.X;
            float dy = existingCenter.Y - center.Y;
            if ((dx * dx) + (dy * dy) <= AutoCropBookmarkDuplicateTolerancePt * AutoCropBookmarkDuplicateTolerancePt)
                return true;
        }

        return false;
    }

    private bool CropBookmarkBelongsToPage(SmartAiCropBookmark bookmark, PageInfo page)
    {
        if (_currentJob != null && !string.IsNullOrWhiteSpace(bookmark.PageFolder))
        {
            string bookmarkFolder = Path.IsPathFullyQualified(bookmark.PageFolder)
                ? bookmark.PageFolder
                : Path.Combine(_currentJob.RootPath, bookmark.PageFolder);
            if (string.Equals(NormalizePath(bookmarkFolder), NormalizePath(page.FolderPath), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return !string.IsNullOrWhiteSpace(bookmark.Page) &&
               string.Equals(bookmark.Page, page.Name, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryGetCropBookmarkCenter(SmartAiCropBookmark bookmark, out SKPoint center)
    {
        center = default;
        if (TryActionPointCenter(bookmark.CandidatePoints.Select(point => new SKPoint(point.X, point.Y)).ToList(), out center))
            return true;

        if (_currentJob != null &&
            !string.IsNullOrWhiteSpace(bookmark.SourceMarkerId) &&
            SmartContextStore.LoadAiMarker(_currentJob, bookmark.SourceMarkerId) is { } marker)
        {
            center = new SKPoint(marker.PdfPoint.X, marker.PdfPoint.Y);
            return true;
        }

        return false;
    }

    private bool TrySaveActionCandidateCrop(
        PageInfo page,
        IReadOnlyList<SKPoint> points,
        SmartAiAction action,
        int actionIndex,
        out string relativePath,
        out SKRect cropPdfRect,
        out string error)
    {
        relativePath = "";
        cropPdfRect = SKRect.Empty;
        error = "";

        SKRect requested = CandidateCropRect(points);
        return TrySavePageCrop(
            page,
            requested,
            "candidate",
            $"{action.Label}_{actionIndex + 1}",
            out relativePath,
            out cropPdfRect,
            out error);
    }

    private bool TrySavePageCrop(
        PageInfo page,
        SKRect requested,
        string type,
        string tag,
        out string relativePath,
        out SKRect cropPdfRect,
        out string error)
    {
        relativePath = "";
        cropPdfRect = SKRect.Empty;
        error = "";

        if (_currentJob == null)
        {
            error = "No current job.";
            return false;
        }

        if (!PdfLayerRenderService.TryRender(
                page.PdfPath,
                page.PdfPage,
                1.50,
                new Dictionary<int, bool>(),
                [],
                page.PdfLayersCached ? page.PdfLayers : null,
                out PdfLayerRenderResult render,
                out error))
        {
            return false;
        }

        using SKBitmap? bitmap = SKBitmap.Decode(render.ImageBytes);
        if (bitmap == null || render.WidthPt <= 0 || render.HeightPt <= 0)
        {
            error = "Rendered PDF image could not be decoded.";
            return false;
        }

        float left = Math.Clamp(requested.Left, 0, render.WidthPt);
        float top = Math.Clamp(requested.Top, 0, render.HeightPt);
        float right = Math.Clamp(requested.Right, 0, render.WidthPt);
        float bottom = Math.Clamp(requested.Bottom, 0, render.HeightPt);
        if (right - left < 1 || bottom - top < 1)
        {
            error = "Requested crop is outside the PDF page.";
            return false;
        }

        float scaleX = bitmap.Width / render.WidthPt;
        float scaleY = bitmap.Height / render.HeightPt;
        int srcLeft = Math.Clamp((int)Math.Floor(left * scaleX), 0, bitmap.Width - 1);
        int srcTop = Math.Clamp((int)Math.Floor(top * scaleY), 0, bitmap.Height - 1);
        int srcRight = Math.Clamp((int)Math.Ceiling(right * scaleX), srcLeft + 1, bitmap.Width);
        int srcBottom = Math.Clamp((int)Math.Ceiling(bottom * scaleY), srcTop + 1, bitmap.Height);

        using var crop = new SKBitmap(srcRight - srcLeft, srcBottom - srcTop);
        using (var canvas = new SKCanvas(crop))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(
                bitmap,
                new SKRectI(srcLeft, srcTop, srcRight, srcBottom),
                new SKRect(0, 0, crop.Width, crop.Height));
        }

        string fileName =
            $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{SafeFileNamePart(type)}_{SafeFileNamePart(page.Name)}_{SafeFileNamePart(tag)}.png";
        string cropPath = Path.Combine(_currentJob.AIContextRoot, "crops", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(cropPath) ?? _currentJob.AIContextRoot);
        using SKData data = crop.Encode(SKEncodedImageFormat.Png, 95);
        using FileStream stream = File.Create(cropPath);
        data.SaveTo(stream);

        cropPdfRect = new SKRect(
            srcLeft / scaleX,
            srcTop / scaleY,
            srcRight / scaleX,
            srcBottom / scaleY);
        relativePath = Path.GetRelativePath(_currentJob.AIContextRoot, cropPath);
        return true;
    }

    private static SKRect CandidateCropRect(IReadOnlyList<SKPoint> points)
    {
        SKRect bounds = PointsBounds(points);
        float centerX = (bounds.Left + bounds.Right) / 2f;
        float centerY = (bounds.Top + bounds.Bottom) / 2f;
        float width = Math.Max(bounds.Width + AutoCropBookmarkPaddingPt * 2, AutoCropBookmarkMinSizePt);
        float height = Math.Max(bounds.Height + AutoCropBookmarkPaddingPt * 2, AutoCropBookmarkMinSizePt);
        return SKRect.Create(centerX - width / 2f, centerY - height / 2f, width, height);
    }

    private static SKRect PointsBounds(IReadOnlyList<SKPoint> points)
    {
        float left = points.Min(point => point.X);
        float top = points.Min(point => point.Y);
        float right = points.Max(point => point.X);
        float bottom = points.Max(point => point.Y);
        return new SKRect(left, top, right, bottom);
    }

    private static bool TryActionPointCenter(IReadOnlyList<SKPoint> points, out SKPoint center)
    {
        center = default;
        var validPoints = points
            .Where(point => !float.IsNaN(point.X) && !float.IsNaN(point.Y))
            .ToList();
        if (validPoints.Count == 0)
            return false;

        SKRect bounds = PointsBounds(validPoints);
        center = new SKPoint((bounds.Left + bounds.Right) / 2f, (bounds.Top + bounds.Bottom) / 2f);
        return true;
    }

    private static string AutoCropBookmarkType(SmartAiAction action)
    {
        string type = string.IsNullOrWhiteSpace(action.Type)
            ? action.MeasurementType
            : action.Type;
        return string.IsNullOrWhiteSpace(type) ? "ai_candidate" : $"candidate:{type.Trim()}";
    }

    private static string BuildAutoCropBookmarkPrompt(
        SmartAiCropBookmark sourceBookmark,
        SmartAiActionDraft draft,
        SmartAiAction action,
        int actionIndex,
        SKRect cropRect)
    {
        var sb = new StringBuilder();
        sb.AppendLine("This crop was auto-created from an AI-discovered action candidate.");
        sb.AppendLine("Review this single crop as a new bookmark. Do not rediscover the source crop or suggest the same candidate again.");
        sb.AppendLine("If this crop confirms a distinct takeoff-relevant candidate, return a JSON action draft with page and points.");
        sb.AppendLine("If it does not confirm a distinct candidate, return JSON with an empty actions array and a short summary.");
        sb.AppendLine();
        sb.AppendLine("Source:");
        sb.AppendLine($"- Source bookmark: {sourceBookmark.Id}");
        sb.AppendLine($"- Source action draft: {draft.Id}");
        sb.AppendLine($"- Source action index: {actionIndex}");
        sb.AppendLine($"- Page: {action.Page}");
        sb.AppendLine($"- Type: {action.Type}");
        sb.AppendLine($"- Label: {action.Label}");
        if (action.Confidence > 0)
            sb.AppendLine($"- Confidence: {action.Confidence:P0}");
        if (!string.IsNullOrWhiteSpace(action.Notes))
            sb.AppendLine($"- Notes: {action.Notes}");
        sb.AppendLine($"- Candidate crop: {FormatPdfRect(cropRect)}");
        sb.AppendLine("- Candidate points:");
        foreach (SmartAiActionPoint point in action.Points)
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  - x={0:F1}, y={1:F1}", point.X, point.Y));
        return sb.ToString();
    }

    private SmartAiRequest EnsureCropBookmarkRequest(SmartAiCropBookmark bookmark)
    {
        if (_currentJob == null)
            throw new InvalidOperationException("Open a job before running crop bookmarks.");

        if (!string.IsNullOrWhiteSpace(bookmark.RequestId) &&
            SmartContextStore.LoadAiRequest(_currentJob, bookmark.RequestId) is { } existing)
        {
            return existing;
        }

        PageInfo? page = ResolveBookmarkPage(bookmark);
        string details =
            "Crop bookmark AI request.\n\n" +
            "Bookmark:\n" +
            $"- Id: {bookmark.Id}\n" +
            $"- Type: {bookmark.Type}\n" +
            $"- Source observation: {bookmark.SourceObservationId}\n" +
            $"- Source marker: {bookmark.SourceMarkerId}\n" +
            $"- AI crop: {bookmark.CropPath}\n\n" +
            bookmark.Prompt;

        SmartObservation observation = SmartContextStore.AddObservation(
            _currentJob,
            page,
            "crop_bookmark_request",
            details);

        return SmartContextStore.AddAiRequest(
            _currentJob,
            page,
            observation,
            "crop_bookmark_request",
            bookmark.Prompt,
            bookmark.CropPath,
            "");
    }

    private PageInfo? ResolveBookmarkPage(SmartAiCropBookmark bookmark)
    {
        if (_currentJob == null)
            return null;

        if (!string.IsNullOrWhiteSpace(bookmark.PageFolder))
        {
            string folder = Path.IsPathFullyQualified(bookmark.PageFolder)
                ? bookmark.PageFolder
                : Path.GetFullPath(Path.Combine(_currentJob.RootPath, bookmark.PageFolder));
            PageInfo? page = SmartTakeoffsJobStore.TryReadPage(folder);
            if (page != null)
                return page;
        }

        return FindPageByName(bookmark.Page);
    }

    private bool CropBookmarkFileExists(SmartAiCropBookmark bookmark)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(bookmark.CropPath))
            return false;

        string path = Path.IsPathFullyQualified(bookmark.CropPath)
            ? bookmark.CropPath
            : Path.GetFullPath(Path.Combine(_currentJob.AIContextRoot, bookmark.CropPath));
        return File.Exists(path);
    }

    private string ResolveObservationPageFolder(SmartObservation observation)
    {
        if (_currentJob == null)
            return "";

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, observation.Id);
        if (request != null && !string.IsNullOrWhiteSpace(request.PageFolder))
            return request.PageFolder;

        PageInfo? page = FindPageByName(observation.Page);
        return page == null ? "" : Path.GetRelativePath(_currentJob.RootPath, page.FolderPath);
    }

    private string BuildCropBookmarkPrompt(SmartObservation observation, SmartAiMarker? marker)
    {
        var sb = new StringBuilder();
        sb.AppendLine("This crop was bookmarked during manual plan review.");
        sb.AppendLine("Analyze it once in the batch pass and record whether it contains takeoff-relevant evidence.");
        sb.AppendLine("If this looks like a reusable detection example, describe what should be searched next.");
        if (marker != null)
        {
            sb.AppendLine();
            sb.AppendLine("Source marker:");
            sb.AppendLine($"- Type: {marker.Type}");
            sb.AppendLine($"- Sample kind: {marker.SampleKind}");
            if (!string.IsNullOrWhiteSpace(marker.Value))
                sb.AppendLine($"- Value: {marker.Value}");
            if (!string.IsNullOrWhiteSpace(marker.Note))
                sb.AppendLine($"- Note: {marker.Note}");
        }

        sb.AppendLine();
        sb.AppendLine("Source observation:");
        sb.AppendLine(observation.Text);
        return sb.ToString();
    }

    private static string FirstLineOrFallback(string text, string fallback)
    {
        string first = (text ?? "")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))
            ?.Trim() ?? "";
        return string.IsNullOrWhiteSpace(first) ? fallback : first;
    }

    private void BtnCreateMarkerSet_Click(object sender, RoutedEventArgs e)
    {
        CreateMarkerSetFromCurrentFilter();
    }

    private void BtnManageMarkerSets_Click(object sender, RoutedEventArgs e)
    {
        ManageMarkerSets();
    }

    private void BtnExportMarkers_Click(object sender, RoutedEventArgs e)
    {
        ExportMarkersContext(openAfterExport: true);
    }

    private void BtnBuildMassingDraft_Click(object sender, RoutedEventArgs e)
    {
        BuildMassingDraftFromMarkers();
    }

    private void BtnDetectRoof_Click(object sender, RoutedEventArgs e)
    {
        QueueRoofRecognitionRequest();
    }

    private void BtnReviewRoof_Click(object sender, RoutedEventArgs e)
    {
        ReviewMassingRoof();
    }

    private void BtnReviewOpenings_Click(object sender, RoutedEventArgs e)
    {
        ReviewMassingOpenings();
    }

    private void BtnAcceptMassingDraft_Click(object sender, RoutedEventArgs e)
    {
        AcceptMassingDraft();
    }

    private void AcceptMassingDraft()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before accepting a 3D draft.";
            return;
        }

        SmartMassingDraft? draft;
        string path = SmartMassingDraftService.ModelPath(_currentJob);
        try
        {
            draft = SmartMassingDraftService.LoadDraft(_currentJob);
        }
        catch (Exception ex)
        {
            ShowOperationError("Accept 3D Draft", ex);
            return;
        }

        if (draft == null)
        {
            TxtStatus.Text = "Build a 3D draft before accepting it.";
            return;
        }

        string warning = string.Equals(draft.Roof.Status, "reviewed", StringComparison.OrdinalIgnoreCase)
            ? "Accept current 3D massing draft as reviewed project context?"
            : "Roof is not marked reviewed yet. Accept current 3D massing draft anyway?";
        if (MessageBox.Show(
                warning + "\n\nThis does not create takeoff quantities.",
                "Accept 3D Draft",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        draft.Status = "reviewed";
        draft.ReviewedAtUtc = DateTime.UtcNow.ToString("O");
        draft.ReviewNotes = "Accepted from the 3D Massing tab as reviewed context. Not a quantity source.";
        AddMassingAssumptionOnce(draft, $"3D draft accepted manually at {draft.ReviewedAtUtc}; use as reviewed AI context, not estimating geometry.");

        try
        {
            SmartMassingDraftService.SaveDraft(_currentJob, draft);
            string snapshotPath = SmartMassingDraftService.SaveSnapshot(_currentJob, draft);
            RefreshMassingDraftPanel(draft, path);
            if (_rightWorkspaceTabs != null && _massingTab != null)
                _rightWorkspaceTabs.SelectedItem = _massingTab;
            TxtStatus.Text =
                $"Accepted 3D draft -> {Path.GetRelativePath(_currentJob.RootPath, path)}; snapshot: {Path.GetRelativePath(_currentJob.RootPath, snapshotPath)}";
        }
        catch (Exception ex)
        {
            ShowOperationError("Accept 3D Draft", ex);
        }
    }

    private void ReviewMassingRoof()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before reviewing roof geometry.";
            return;
        }

        SmartMassingDraft? draft;
        string path = SmartMassingDraftService.ModelPath(_currentJob);
        try
        {
            draft = SmartMassingDraftService.LoadDraft(_currentJob);
        }
        catch (Exception ex)
        {
            ShowOperationError("Review Roof", ex);
            return;
        }

        if (draft == null)
        {
            TxtStatus.Text = "Build a 3D draft before reviewing roof geometry.";
            MessageBox.Show(
                "No 3D massing draft exists yet. Run Build 3D Draft first.",
                "Review Roof",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new RoofReviewDialog(draft.Roof)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
            return;

        draft.Roof = dialog.ReviewedRoof;
        draft.Status = "roof_reviewed";
        AddMassingAssumptionOnce(
            draft,
            $"Roof reviewed manually at {draft.Roof.ReviewedAtUtc}. Rebuild from markers may replace this reviewed roof state.");

        try
        {
            SmartMassingDraftService.SaveDraft(_currentJob, draft);
            RefreshMassingDraftPanel(draft, path);
            if (_rightWorkspaceTabs != null && _massingTab != null)
                _rightWorkspaceTabs.SelectedItem = _massingTab;
            TxtStatus.Text = $"Saved reviewed roof -> {Path.GetRelativePath(_currentJob.RootPath, path)}";
        }
        catch (Exception ex)
        {
            ShowOperationError("Review Roof", ex);
        }
    }

    private void ReviewMassingOpenings()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before reviewing opening projections.";
            return;
        }

        SmartMassingDraft? draft;
        string path = SmartMassingDraftService.ModelPath(_currentJob);
        try
        {
            draft = SmartMassingDraftService.LoadDraft(_currentJob);
        }
        catch (Exception ex)
        {
            ShowOperationError("Review Openings", ex);
            return;
        }

        if (draft == null)
        {
            TxtStatus.Text = "Build a 3D draft before reviewing openings.";
            MessageBox.Show(
                "No 3D massing draft exists yet. Run Build 3D Draft first.",
                "Review Openings",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (draft.Openings.Count == 0)
        {
            TxtStatus.Text = "No projected openings are available for review.";
            MessageBox.Show(
                "No projected door/window/opening markers were found in the current 3D draft.",
                "Review Openings",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new MassingOpeningsReviewDialog(draft.Openings)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
            return;

        draft.Openings = dialog.ReviewedOpenings;
        draft.Status = "openings_reviewed";
        string reviewedAt = DateTime.UtcNow.ToString("O");
        AddMassingAssumptionOnce(
            draft,
            $"Projected openings reviewed manually at {reviewedAt}. Rebuild from markers may replace this reviewed opening state.");

        try
        {
            SmartMassingDraftService.SaveDraft(_currentJob, draft);
            RefreshMassingDraftPanel(draft, path);
            if (_rightWorkspaceTabs != null && _massingTab != null)
                _rightWorkspaceTabs.SelectedItem = _massingTab;
            int kept = draft.Openings.Count(opening => !string.Equals(opening.Status, "rejected", StringComparison.OrdinalIgnoreCase));
            RecordMassingOpeningFeedback(draft);
            TxtStatus.Text = $"Saved reviewed openings ({kept}/{draft.Openings.Count} kept) -> {Path.GetRelativePath(_currentJob.RootPath, path)}";
        }
        catch (Exception ex)
        {
            ShowOperationError("Review Openings", ex);
        }
    }

    private void RecordMassingOpeningFeedback(SmartMassingDraft draft)
    {
        if (_currentJob == null || draft.Openings.Count == 0)
            return;

        Dictionary<string, SmartAiMarker> markersById = SmartContextStore.LoadAiMarkers(_currentJob)
            .Where(marker => !string.IsNullOrWhiteSpace(marker.Id))
            .GroupBy(marker => marker.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < draft.Openings.Count; i++)
        {
            SmartMassingOpening opening = draft.Openings[i];
            bool accepted = !string.Equals(opening.Status, "rejected", StringComparison.OrdinalIgnoreCase);
            markersById.TryGetValue(opening.SourceMarkerId, out SmartAiMarker? marker);

            SmartLearningStore.AppendMarkerFeedback(
                _currentJob,
                new SmartMarkerFeedbackRecord
                {
                    EventType = "3d_opening_projection_review",
                    DraftId = draft.Id,
                    SourceMarkerId = opening.SourceMarkerId,
                    SourceMarkerType = marker?.Type ?? $"{opening.Type}_sample",
                    SourceMarkerSampleKind = marker?.SampleKind ?? "",
                    Outcome = accepted ? "accepted" : "rejected",
                    Applied = accepted,
                    ActionIndex = i,
                    ActionType = "3d_opening_projection",
                    Label = $"{opening.Type} projection on wall {opening.WallIndex}",
                    Page = opening.Page,
                    MeasurementType = "opening",
                    Confidence = opening.Confidence,
                    Points =
                    [
                        new SmartAiActionPoint
                        {
                            X = (float)opening.Center.X,
                            Y = (float)opening.Center.Y,
                        },
                    ],
                    Notes = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} | wall={1}; center={2:0.###},{3:0.###},{4:0.###}; size={5:0.###}x{6:0.###}",
                        opening.Notes.Trim(),
                        opening.WallIndex,
                        opening.Center.X,
                        opening.Center.Y,
                        opening.Center.Z,
                        opening.Width,
                        opening.Height),
                });
        }
    }

    private static void AddMassingAssumptionOnce(SmartMassingDraft draft, string text)
    {
        bool isRoofReview = text.Contains("Roof reviewed manually", StringComparison.OrdinalIgnoreCase);
        bool isAcceptedDraft = text.Contains("3D draft accepted manually", StringComparison.OrdinalIgnoreCase);
        bool isOpeningReview = text.Contains("Projected openings reviewed manually", StringComparison.OrdinalIgnoreCase);
        draft.Assumptions.RemoveAll(item =>
            isRoofReview && item.Contains("Roof reviewed manually", StringComparison.OrdinalIgnoreCase) ||
            isAcceptedDraft && item.Contains("3D draft accepted manually", StringComparison.OrdinalIgnoreCase) ||
            isOpeningReview && item.Contains("Projected openings reviewed manually", StringComparison.OrdinalIgnoreCase));
        draft.Assumptions.Add(text);
    }

    private void QueueRoofRecognitionRequest()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before running Auto Roof.";
            return;
        }

        try
        {
            IReadOnlyList<SmartAiMarker> allMarkers = SmartContextStore.LoadAiMarkers(_currentJob);
            PageInfo? page = _currentPage ?? allMarkers
                .Where(IsRoofRecognitionSourceMarker)
                .Select(ResolveMarkerPage)
                .FirstOrDefault(candidate => candidate != null);

            if (page == null)
            {
                TxtStatus.Text = "Open a sheet or place roof/exterior markers before running Auto Roof.";
                return;
            }

            List<SmartAiMarker> pageMarkers = allMarkers
                .Where(marker => MarkerBelongsToPage(marker, page, _currentJob))
                .Where(IsRoofRecognitionSourceMarker)
                .ToList();

            if (!TrySaveRoofRecognitionCrop(
                    page,
                    pageMarkers,
                    out string cropPath,
                    out SKRect cropRect,
                    out string cropMode,
                    out string error))
            {
                TxtStatus.Text = $"Auto Roof crop skipped: {error}";
                MessageBox.Show(
                    $"Cannot save Auto Roof crop:\n{error}",
                    "Auto Roof",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            List<string> contextCropPaths = RoofRecognitionContextCropPaths(pageMarkers, cropPath);
            string prompt = BuildRoofRecognitionRequestPrompt(
                page,
                pageMarkers,
                cropPath,
                cropRect,
                cropMode,
                contextCropPaths);

            string details =
                "Auto Roof recognition queued.\n\n" +
                "Review mode:\n" +
                "- AI may suggest roof markers only.\n" +
                "- Accepted candidates become AI markers after user review.\n" +
                "- 3D roof geometry is still rebuilt manually with Build 3D Draft.\n\n" +
                "Context:\n" +
                $"- Page: {page.Name}\n" +
                $"- AI crop: {cropPath}\n" +
                $"- PDF crop: {FormatPdfRect(cropRect)}\n" +
                $"- Crop mode: {cropMode}\n" +
                $"- Source roof/footprint markers on page: {pageMarkers.Count}\n" +
                $"- Marker evidence crops attached: {contextCropPaths.Count}\n\n" +
                prompt;

            SmartObservation observation = SmartContextStore.AddObservation(
                _currentJob,
                page,
                "roof_recognition_request",
                details);

            SmartContextStore.AddAiRequest(
                _currentJob,
                page,
                observation,
                "roof_recognition_request",
                prompt,
                cropPath,
                $"Auto Roof source markers: {pageMarkers.Count}",
                contextCropPaths);

            LoadObservationsInbox();
            TxtStatus.Text = $"Queued Auto Roof for {page.Name} with {pageMarkers.Count} source marker(s).";
        }
        catch (Exception ex)
        {
            ShowOperationError("Auto Roof", ex);
        }
    }

    private bool TrySaveRoofRecognitionCrop(
        PageInfo page,
        IReadOnlyList<SmartAiMarker> pageMarkers,
        out string relativePath,
        out SKRect cropRect,
        out string cropMode,
        out string error)
    {
        SKRect requested = RoofRecognitionCropRect(pageMarkers, out cropMode);
        return TrySavePageCrop(
            page,
            requested,
            "roof_recognition",
            cropMode,
            out relativePath,
            out cropRect,
            out error);
    }

    private static SKRect RoofRecognitionCropRect(
        IReadOnlyList<SmartAiMarker> pageMarkers,
        out string cropMode)
    {
        var points = new List<SKPoint>();
        foreach (SmartAiMarker marker in pageMarkers)
        {
            if (marker.PdfRect.Right > marker.PdfRect.Left &&
                marker.PdfRect.Bottom > marker.PdfRect.Top)
            {
                points.Add(new SKPoint(marker.PdfRect.Left, marker.PdfRect.Top));
                points.Add(new SKPoint(marker.PdfRect.Right, marker.PdfRect.Bottom));
            }

            if (!float.IsNaN(marker.PdfPoint.X) && !float.IsNaN(marker.PdfPoint.Y))
                points.Add(new SKPoint(marker.PdfPoint.X, marker.PdfPoint.Y));
        }

        if (points.Count == 0)
        {
            cropMode = "full_page";
            return SKRect.Create(0, 0, RoofRecognitionFullPageSizePt, RoofRecognitionFullPageSizePt);
        }

        cropMode = "marker_bounds";
        SKRect bounds = PointsBounds(points);
        float centerX = (bounds.Left + bounds.Right) / 2f;
        float centerY = (bounds.Top + bounds.Bottom) / 2f;
        float width = Math.Max(bounds.Width + RoofRecognitionContextPaddingPt * 2, RoofRecognitionMinCropSizePt);
        float height = Math.Max(bounds.Height + RoofRecognitionContextPaddingPt * 2, RoofRecognitionMinCropSizePt);
        return SKRect.Create(centerX - width / 2f, centerY - height / 2f, width, height);
    }

    private List<string> RoofRecognitionContextCropPaths(
        IReadOnlyList<SmartAiMarker> pageMarkers,
        string primaryCropPath)
    {
        return pageMarkers
            .Where(marker => IsExplicitRoofRecognitionMarker(marker) || AiMarkerTypeEquals(marker, "exterior_corner"))
            .Select(marker => marker.CropPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(path => !string.Equals(path, primaryCropPath, StringComparison.OrdinalIgnoreCase))
            .Where(AiContextFileExists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }

    private string BuildRoofRecognitionRequestPrompt(
        PageInfo page,
        IReadOnlyList<SmartAiMarker> pageMarkers,
        string cropPath,
        SKRect cropRect,
        string cropMode,
        IReadOnlyList<string> contextCropPaths)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Queue Auto Roof recognition for this construction plan sheet.");
        sb.AppendLine("Return only reviewable roof marker candidates; do not apply geometry or estimate quantities.");
        sb.AppendLine();
        sb.AppendLine("Allowed marker candidate action.type values:");
        foreach (string markerType in RoofRecognitionMarkerTypes)
            sb.AppendLine($"- {markerType}");
        sb.AppendLine();
        sb.AppendLine("Context:");
        sb.AppendLine($"- Page: {page.Name}");
        sb.AppendLine($"- Main roof crop: {cropPath}");
        sb.AppendLine($"- Crop mode: {cropMode}");
        sb.AppendLine($"- PDF crop: {FormatPdfRect(cropRect)}");
        sb.AppendLine($"- Extra marker crop images: {contextCropPaths.Count}");

        if (_currentJob != null)
        {
            string modelPath = SmartMassingDraftService.ModelPath(_currentJob);
            sb.AppendLine(File.Exists(modelPath)
                ? $"- Existing 3D draft: {Path.GetRelativePath(_currentJob.RootPath, modelPath)}"
                : "- Existing 3D draft: none yet");
        }

        sb.AppendLine();
        sb.AppendLine("Known markers on this page:");
        if (pageMarkers.Count == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (SmartAiMarker marker in pageMarkers.Take(80))
            {
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "- {0}: {1} {2} at x={3:F1}, y={4:F1}; value={5}; note={6}",
                    marker.Id,
                    marker.Type,
                    marker.SampleKind,
                    marker.PdfPoint.X,
                    marker.PdfPoint.Y,
                    string.IsNullOrWhiteSpace(marker.Value) ? "-" : marker.Value.Trim(),
                    string.IsNullOrWhiteSpace(marker.Note) ? "-" : marker.Note.Trim()));
            }
        }

        return sb.ToString();
    }

    private static bool IsRoofRecognitionSourceMarker(SmartAiMarker marker) =>
        AiMarkerTypeEquals(marker, "exterior_corner") ||
        AiMarkerTypeEquals(marker, "wall_height_sample") ||
        IsExplicitRoofRecognitionMarker(marker);

    private static bool IsExplicitRoofRecognitionMarker(SmartAiMarker marker) =>
        RoofRecognitionMarkerTypes.Any(type => AiMarkerTypeEquals(marker, type));

    private static bool AiMarkerTypeEquals(SmartAiMarker marker, string type) =>
        string.Equals(marker.Type, type, StringComparison.OrdinalIgnoreCase);

    private void BuildMassingDraftFromMarkers()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before building a 3D draft.";
            return;
        }

        try
        {
            SmartMassingDraft draft = SmartMassingDraftService.SaveDraftFromMarkers(_currentJob);
            string path = SmartMassingDraftService.ModelPath(_currentJob);
            RefreshMassingDraftPanel(draft, path);
            if (_rightWorkspaceTabs != null && _massingTab != null)
                _rightWorkspaceTabs.SelectedItem = _massingTab;

            string summary = BuildMassingDraftSummary(draft, path);
            TxtStatus.Text = $"Saved 3D massing draft -> {Path.GetRelativePath(_currentJob.RootPath, path)}";
            MessageBox.Show(summary, "Build 3D Draft", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowOperationError("Build 3D Draft", ex);
        }
    }

    private void BuildMassingDraftFromWallTakeoffs()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before building a 3D draft from takeoffs.";
            return;
        }

        double currentLevelSpacing = _settings.MassingLevelSpacingFeet > 0
            ? _settings.MassingLevelSpacingFeet
            : SmartMassingDraftService.DefaultLevelSpacingFeet;
        string? rawLevelSpacing = ShowInputDialog(
            "Default level spacing and roof step, feet (1st=0, 2nd=+spacing, roof=last+spacing):",
            currentLevelSpacing.ToString("G", CultureInfo.InvariantCulture),
            "3D From Takeoffs");
        if (rawLevelSpacing == null)
            return;
        if (!double.TryParse(rawLevelSpacing.Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out double levelSpacingFeet) ||
            levelSpacingFeet <= 0 ||
            levelSpacingFeet > 40)
        {
            MessageBox.Show("Enter a level spacing value between 1 and 40 feet.", "3D From Takeoffs", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _settings.MassingLevelSpacingFeet = levelSpacingFeet;
            SaveAppSettings();

            SmartMassingDraft draft = SmartMassingDraftService.SaveDraftFromWallTakeoffs(_currentJob, levelSpacingFeet);
            string path = SmartMassingDraftService.ModelPath(_currentJob);
            RefreshMassingDraftPanel(draft, path);
            if (_rightWorkspaceTabs != null && _massingTab != null)
                _rightWorkspaceTabs.SelectedItem = _massingTab;

            string summary = BuildMassingDraftSummary(draft, path);
            TxtStatus.Text = $"Saved 3D draft from takeoffs -> {Path.GetRelativePath(_currentJob.RootPath, path)}";
            MessageBox.Show(summary, "3D From Takeoffs", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowOperationError("3D From Takeoffs", ex);
        }
    }

    private void OpenMassingDraftJson()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before opening the 3D draft.";
            return;
        }

        OpenJsonFile(SmartMassingDraftService.ModelPath(_currentJob), "3D massing draft JSON is missing.");
    }

    private void OpenMassing3DWindow()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before opening the 3D viewport.";
            return;
        }

        try
        {
            SmartMassingDraft? draft = _currentMassingDraft;
            string path = SmartMassingDraftService.ModelPath(_currentJob);
            if (draft == null && File.Exists(path))
                draft = SmartMassingDraftService.LoadDraft(_currentJob);

            IReadOnlyList<SmartAiMarker> markers = SmartContextStore.LoadAiMarkers(_currentJob);
            if (draft == null && markers.Count > 0)
                draft = SmartMassingDraftService.BuildDraftFromMarkers(_currentJob);
            if (draft != null)
                SmartMassingDraftService.RefreshDerivedGeometry(draft);

            var window = new Massing3DWindow(_currentJob, draft, markers)
            {
                Owner = this,
            };
            window.Show();

            int footprintPoints = draft?.Footprints.Sum(footprint => footprint.Points.Count) ?? 0;
            TxtStatus.Text = $"Opened 3D viewport window with {markers.Count} marker(s) and {footprintPoints} footprint point(s).";
        }
        catch (Exception ex)
        {
            ShowOperationError("Open 3D Viewport", ex);
        }
    }

    private void RefreshMassingDraftPanel(SmartMassingDraft? draft = null, string? path = null)
    {
        if (_massingDraftTextBox == null)
            return;

        if (_currentJob == null)
        {
            _currentMassingDraft = null;
            _massingDraftTextBox.Text = "Open a job, then build a 3D massing draft from reviewed AI markers.";
            if (_massingOpenDraftButton != null)
                _massingOpenDraftButton.IsEnabled = false;
            if (_massingReviewRoofButton != null)
                _massingReviewRoofButton.IsEnabled = false;
            if (_massingReviewOpeningsButton != null)
                _massingReviewOpeningsButton.IsEnabled = false;
            if (_massingAcceptDraftButton != null)
                _massingAcceptDraftButton.IsEnabled = false;
            DrawMassingPreview(null);
            RefreshMassing3DPreview(null);
            RefreshMassingMarkerRows(null);
            return;
        }

        path ??= SmartMassingDraftService.ModelPath(_currentJob);
        if (draft == null && File.Exists(path))
        {
            try
            {
                draft = SmartMassingDraftService.LoadDraft(_currentJob);
            }
            catch
            {
                draft = null;
            }
        }

        bool exists = File.Exists(path);
        if (_massingOpenDraftButton != null)
            _massingOpenDraftButton.IsEnabled = exists;
        if (_massingReviewRoofButton != null)
            _massingReviewRoofButton.IsEnabled = exists && draft != null;
        if (_massingReviewOpeningsButton != null)
            _massingReviewOpeningsButton.IsEnabled = exists && draft != null && draft.Openings.Count > 0;
        if (_massingAcceptDraftButton != null)
            _massingAcceptDraftButton.IsEnabled = exists && draft != null && draft.Footprints.Any(footprint => footprint.Points.Count >= 3);

        _currentMassingDraft = draft;
        if (draft != null)
            SmartMassingDraftService.RefreshDerivedGeometry(draft);
        _massingDraftTextBox.Text = draft == null
            ? $"No 3D massing draft exists yet.\n\nTarget path:\n{path}\n\nUse Build 3D Draft after placing exterior_corner markers."
            : BuildMassingDraftSummary(draft, path);
        DrawMassingPreview(draft);
        RefreshMassing3DPreview(draft);
        RefreshMassingMarkerRows(draft);
    }

    private void RefreshMassing3DPreview(SmartMassingDraft? draft)
    {
        if (_massingViewport3D == null)
            return;

        _massingViewport3D.Children.Clear();
        _massing3DObjectInfo.Clear();

        List<SmartMassingFootprint> footprints = draft?.Footprints
            .Where(footprint => footprint.Points.Count >= 3)
            .OrderBy(footprint => SmartMassingDraftService.DisplayBaseElevation(draft, footprint))
            .ThenBy(footprint => footprint.Level)
            .ToList() ?? [];
        if (draft == null || footprints.Count == 0)
        {
            if (_massingViewportStatusText != null)
                _massingViewportStatusText.Text = draft == null
                    ? "Build a draft to preview the 3D shell."
                    : "Draft has no footprint loop for 3D preview.";
            return;
        }

        SmartMassingDraftService.RefreshDerivedGeometry(draft);
        if (!TryGetMassing3DBounds(draft, out double minX, out double maxX, out double minY, out double maxY, out double maxZ))
        {
            if (_massingViewportStatusText != null)
                _massingViewportStatusText.Text = "Draft bounds are not valid for 3D preview.";
            return;
        }

        double centerX = (minX + maxX) / 2;
        double centerY = (minY + maxY) / 2;
        double spanX = Math.Max(0.001, maxX - minX);
        double spanY = Math.Max(0.001, maxY - minY);
        _massing3DSceneRadius = Math.Max(Math.Max(spanX, spanY), Math.Max(maxZ, 1));
        _massing3DTarget = new Point3D(0, Math.Max(maxZ, 1) / 2, 0);

        var group = new Model3DGroup();
        group.Children.Add(new AmbientLight(Color.FromRgb(92, 92, 92)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(245, 245, 245), new Vector3D(-0.45, -0.8, -0.35)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(130, 160, 190), new Vector3D(0.65, -0.35, 0.55)));

        foreach (SmartMassingFootprint footprint in footprints)
        {
            AddMassingFootprint3D(group, draft, footprint, centerX, centerY);
            AddMassingMarkerPins3D(group, draft, footprint, centerX, centerY);
        }
        AddMassingRoofPlanes3D(group, draft, centerX, centerY);
        AddMassingOpenings3D(group, draft, centerX, centerY);

        _massingViewport3D.Children.Add(new ModelVisual3D { Content = group });
        EnsureMassingCamera();
        FitMassing3DView(resetAngles: false);

        if (_massingViewportStatusText != null)
        {
            int wallCount = footprints.Sum(footprint => footprint.Points.Count);
            int roofPlanes = draft.Roof.Planes.Count(plane => !string.Equals(plane.Status, "rejected", StringComparison.OrdinalIgnoreCase));
            int openings = draft.Openings.Count(opening => !string.Equals(opening.Status, "rejected", StringComparison.OrdinalIgnoreCase));
            _massingViewportStatusText.Text = $"3D shell | levels: {footprints.Count} | walls: {wallCount} | roof planes: {roofPlanes} | openings: {openings} | roof: {draft.Roof.Type} ({draft.Roof.Status})";
        }
    }

    private void AddMassingFootprint3D(
        Model3DGroup group,
        SmartMassingDraft draft,
        SmartMassingFootprint footprint,
        double centerX,
        double centerY)
    {
        double baseZ = SmartMassingDraftService.DisplayBaseElevation(draft, footprint);
        double wallTopZ = SmartMassingDraftService.DisplayWallTopElevation(draft, footprint);
        var floor = footprint.Points
            .Select(point => ToMassing3DPoint(point.X, point.Y, baseZ, centerX, centerY))
            .ToList();
        AddMassingSurface(
            group,
            new Massing3DObjectInfo(
                $"floor_level_{footprint.Level}",
                "floor",
                $"Level {footprint.Level} floor/footprint",
                footprint.SourceMarkerIds,
                "Floor cap generated from exterior corner markers."),
            floor,
            Color.FromRgb(82, 91, 102),
            0.42);

        for (int i = 0; i < footprint.Points.Count; i++)
        {
            SmartMassingPoint start = footprint.Points[i];
            SmartMassingPoint end = footprint.Points[(i + 1) % footprint.Points.Count];
            var sourceIds = new[]
                {
                    start.SourceMarkerId,
                    end.SourceMarkerId,
                }
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            AddMassingSurface(
                group,
                new Massing3DObjectInfo(
                    $"wall_level_{footprint.Level}_{i + 1}",
                    "wall",
                    $"Level {footprint.Level} wall {i + 1}",
                    sourceIds,
                    "Wall face generated by extruding adjacent footprint points."),
                [
                    ToMassing3DPoint(start.X, start.Y, baseZ, centerX, centerY),
                    ToMassing3DPoint(end.X, end.Y, baseZ, centerX, centerY),
                    ToMassing3DPoint(end.X, end.Y, wallTopZ, centerX, centerY),
                    ToMassing3DPoint(start.X, start.Y, wallTopZ, centerX, centerY),
                ],
                Color.FromRgb(148, 163, 184),
                0.72);
        }
    }

    private void AddMassingRoofPlanes3D(
        Model3DGroup group,
        SmartMassingDraft draft,
        double centerX,
        double centerY)
    {
        foreach (SmartMassingPlane plane in draft.Roof.Planes)
        {
            if (string.Equals(plane.Status, "rejected", StringComparison.OrdinalIgnoreCase) ||
                plane.Points.Count < 3)
            {
                continue;
            }

            Color color = plane.Kind.Contains("candidate", StringComparison.OrdinalIgnoreCase)
                ? Color.FromRgb(245, 158, 11)
                : Color.FromRgb(71, 123, 156);
            double opacity = plane.Status == "reviewed" ? 0.86 : 0.68;
            AddMassingSurface(
                group,
                new Massing3DObjectInfo(
                    plane.Id,
                    plane.Kind,
                    plane.Label,
                    plane.SourceMarkerIds,
                    plane.Notes),
                plane.Points.Select(point => ToMassing3DPoint(point.X, point.Y, point.Z, centerX, centerY)).ToList(),
                color,
                opacity);
        }
    }

    private void AddMassingOpenings3D(
        Model3DGroup group,
        SmartMassingDraft draft,
        double centerX,
        double centerY)
    {
        foreach (SmartMassingOpening opening in draft.Openings)
        {
            SmartMassingFootprint? footprint = FootprintForMassingOpening(draft, opening);
            if (string.Equals(opening.Status, "rejected", StringComparison.OrdinalIgnoreCase) ||
                footprint == null ||
                opening.WallIndex < 0 ||
                opening.WallIndex >= footprint.Points.Count ||
                opening.Width <= 0 ||
                opening.Height <= 0)
            {
                continue;
            }

            SmartMassingPoint start = footprint.Points[opening.WallIndex];
            SmartMassingPoint end = footprint.Points[(opening.WallIndex + 1) % footprint.Points.Count];
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= 0.0001)
                continue;

            double ux = dx / length;
            double uy = dy / length;
            double halfW = Math.Min(opening.Width / 2.0, length / 2.0);
            double halfH = opening.Height / 2.0;
            double zMin = Math.Max(0.03, opening.Center.Z - halfH);
            double zMax = opening.Center.Z + halfH;
            double x1 = opening.Center.X - ux * halfW;
            double y1 = opening.Center.Y - uy * halfW;
            double x2 = opening.Center.X + ux * halfW;
            double y2 = opening.Center.Y + uy * halfW;

            Color color = opening.Type switch
            {
                "door" => Color.FromRgb(250, 204, 21),
                "window" => Color.FromRgb(34, 211, 238),
                _ => Color.FromRgb(168, 85, 247),
            };
            AddMassingSurface(
                group,
                new Massing3DObjectInfo(
                    $"opening_{opening.SourceMarkerId}",
                    opening.Type,
                    $"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase(opening.Type)} projection",
                    string.IsNullOrWhiteSpace(opening.SourceMarkerId) ? [] : [opening.SourceMarkerId],
                    opening.Notes),
                [
                    ToMassing3DPoint(x1, y1, zMin, centerX, centerY),
                    ToMassing3DPoint(x2, y2, zMin, centerX, centerY),
                    ToMassing3DPoint(x2, y2, zMax, centerX, centerY),
                    ToMassing3DPoint(x1, y1, zMax, centerX, centerY),
                ],
                color,
                0.92);
        }
    }

    private void AddMassingMarkerPins3D(
        Model3DGroup group,
        SmartMassingDraft draft,
        SmartMassingFootprint footprint,
        double centerX,
        double centerY)
    {
        double baseZ = SmartMassingDraftService.DisplayBaseElevation(draft, footprint);
        double wallHeight = SmartMassingDraftService.DisplayWallHeight(draft, footprint);
        foreach (SmartMassingPoint point in footprint.Points)
        {
            if (string.IsNullOrWhiteSpace(point.SourceMarkerId))
                continue;
            AddMassingPin3D(
                group,
                $"pin_{point.SourceMarkerId}",
                "marker_pin",
                "Exterior corner marker",
                point.SourceMarkerId,
                ToMassing3DPoint(point.X, point.Y, baseZ, centerX, centerY),
                Color.FromRgb(56, 189, 248),
                wallHeight);
        }

        foreach (SmartMassingOpening opening in draft.Openings.Where(opening => opening.Level == footprint.Level))
        {
            if (string.IsNullOrWhiteSpace(opening.SourceMarkerId))
                continue;
            AddMassingPin3D(
                group,
                $"pin_{opening.SourceMarkerId}",
                "opening_pin",
                $"{opening.Type} marker",
                opening.SourceMarkerId,
                ToMassing3DPoint(opening.Center.X, opening.Center.Y, opening.Center.Z, centerX, centerY),
                Color.FromRgb(244, 114, 182),
                wallHeight);
        }
    }

    private void AddMassingPin3D(
        Model3DGroup group,
        string id,
        string kind,
        string label,
        string sourceMarkerId,
        Point3D center,
        Color color,
        double modelHeight)
    {
        double size = Math.Max(0.25, Math.Min(_massing3DSceneRadius * 0.025, Math.Max(0.35, modelHeight * 0.06)));
        var points = new List<Point3D>
        {
            new(center.X - size, center.Y, center.Z - size),
            new(center.X + size, center.Y, center.Z - size),
            new(center.X + size, center.Y, center.Z + size),
            new(center.X - size, center.Y, center.Z + size),
            new(center.X, center.Y + size * 2.8, center.Z),
        };

        AddMassingSurface(
            group,
            new Massing3DObjectInfo(
                id,
                kind,
                label,
                string.IsNullOrWhiteSpace(sourceMarkerId) ? [] : [sourceMarkerId],
                "3D source marker pin."),
            [points[0], points[1], points[4]],
            color,
            0.95);
        AddMassingSurface(
            group,
            new Massing3DObjectInfo(
                $"{id}_b",
                kind,
                label,
                string.IsNullOrWhiteSpace(sourceMarkerId) ? [] : [sourceMarkerId],
                "3D source marker pin."),
            [points[1], points[2], points[4]],
            color,
            0.95);
        AddMassingSurface(
            group,
            new Massing3DObjectInfo(
                $"{id}_c",
                kind,
                label,
                string.IsNullOrWhiteSpace(sourceMarkerId) ? [] : [sourceMarkerId],
                "3D source marker pin."),
            [points[2], points[3], points[4]],
            color,
            0.95);
        AddMassingSurface(
            group,
            new Massing3DObjectInfo(
                $"{id}_d",
                kind,
                label,
                string.IsNullOrWhiteSpace(sourceMarkerId) ? [] : [sourceMarkerId],
                "3D source marker pin."),
            [points[3], points[0], points[4]],
            color,
            0.95);
    }

    private void AddMassingSurface(
        Model3DGroup group,
        Massing3DObjectInfo info,
        IReadOnlyList<Point3D> points,
        Color color,
        double opacity)
    {
        if (points.Count < 3)
            return;

        var mesh = new MeshGeometry3D();
        foreach (Point3D point in points)
            mesh.Positions.Add(point);
        for (int i = 1; i < points.Count - 1; i++)
        {
            mesh.TriangleIndices.Add(0);
            mesh.TriangleIndices.Add(i);
            mesh.TriangleIndices.Add(i + 1);
        }

        bool selected = IsSelectedMassing3DObject(info);
        var brush = new SolidColorBrush(selected ? Color.FromRgb(255, 183, 77) : color)
        {
            Opacity = selected ? Math.Min(1.0, opacity + 0.1) : opacity,
        };
        var material = new DiffuseMaterial(brush);
        var model = new GeometryModel3D(mesh, material)
        {
            BackMaterial = material,
        };
        _massing3DObjectInfo[model] = info;
        group.Children.Add(model);
    }

    private bool IsSelectedMassing3DObject(Massing3DObjectInfo info)
    {
        if (!string.IsNullOrWhiteSpace(_selectedMassing3DObjectId) &&
            string.Equals(_selectedMassing3DObjectId, info.Id, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string selectedMarkerId = SelectedMassingMarkerRow()?.MarkerId ?? "";
        return !string.IsNullOrWhiteSpace(selectedMarkerId) &&
               info.SourceMarkerIds.Contains(selectedMarkerId, StringComparer.OrdinalIgnoreCase);
    }

    private static Point3D ToMassing3DPoint(double x, double y, double z, double centerX, double centerY) =>
        new(x - centerX, z, y - centerY);

    private static bool TryGetMassing3DBounds(
        SmartMassingDraft draft,
        out double minX,
        out double maxX,
        out double minY,
        out double maxY,
        out double maxZ)
    {
        var vertices = new List<SmartMassingVertex>();
        foreach (SmartMassingFootprint footprint in draft.Footprints.Where(footprint => footprint.Points.Count >= 3))
        {
            double baseZ = SmartMassingDraftService.DisplayBaseElevation(draft, footprint);
            double wallTopZ = SmartMassingDraftService.DisplayWallTopElevation(draft, footprint);
            vertices.AddRange(footprint.Points.Select(point => new SmartMassingVertex { X = point.X, Y = point.Y, Z = baseZ }));
            vertices.AddRange(footprint.Points.Select(point => new SmartMassingVertex { X = point.X, Y = point.Y, Z = wallTopZ }));
        }
        vertices.AddRange(draft.Roof.Planes.SelectMany(plane => plane.Points));
        vertices.AddRange(draft.Openings.Select(opening => opening.Center));

        vertices = vertices
            .Where(point =>
                !double.IsNaN(point.X) &&
                !double.IsNaN(point.Y) &&
                !double.IsNaN(point.Z))
            .ToList();
        if (vertices.Count == 0)
        {
            minX = maxX = minY = maxY = maxZ = 0;
            return false;
        }

        minX = vertices.Min(point => point.X);
        maxX = vertices.Max(point => point.X);
        minY = vertices.Min(point => point.Y);
        maxY = vertices.Max(point => point.Y);
        maxZ = Math.Max(1, vertices.Max(point => point.Z));
        return maxX > minX && maxY > minY;
    }

    private static SmartMassingFootprint? FootprintForMassingOpening(SmartMassingDraft draft, SmartMassingOpening opening)
    {
        SmartMassingFootprint? exact = draft.Footprints
            .Where(footprint => footprint.Points.Count >= 3)
            .FirstOrDefault(footprint => footprint.Level == opening.Level);
        return exact ?? draft.Footprints
            .Where(footprint => footprint.Points.Count >= 3)
            .OrderByDescending(footprint => SmartMassingDraftService.DisplayWallTopElevation(draft, footprint))
            .ThenByDescending(footprint => footprint.Level)
            .FirstOrDefault();
    }

    private void EnsureMassingCamera()
    {
        if (_massingViewport3D == null)
            return;

        _massingCamera3D ??= new PerspectiveCamera
        {
            FieldOfView = 42,
            UpDirection = new Vector3D(0, 1, 0),
        };
        _massingViewport3D.Camera = _massingCamera3D;
    }

    private void FitMassing3DView(bool resetAngles)
    {
        if (_massingCamera3D == null)
            EnsureMassingCamera();
        if (_massingCamera3D == null)
            return;

        if (resetAngles)
        {
            _massing3DYaw = -38;
            _massing3DPitch = 28;
        }

        _massing3DDistance = Math.Max(12, _massing3DSceneRadius * 2.65);
        UpdateMassing3DCamera();
    }

    private void SetMassing3DView(double yaw, double pitch)
    {
        _massing3DYaw = yaw;
        _massing3DPitch = Math.Clamp(pitch, -8, 88);
        FitMassing3DView(resetAngles: false);
    }

    private void UpdateMassing3DCamera()
    {
        if (_massingCamera3D == null)
            return;

        double yaw = _massing3DYaw * Math.PI / 180.0;
        double pitch = _massing3DPitch * Math.PI / 180.0;
        double horizontal = _massing3DDistance * Math.Cos(pitch);
        var position = new Point3D(
            _massing3DTarget.X + horizontal * Math.Sin(yaw),
            _massing3DTarget.Y + _massing3DDistance * Math.Sin(pitch),
            _massing3DTarget.Z + horizontal * Math.Cos(yaw));

        _massingCamera3D.Position = position;
        _massingCamera3D.LookDirection = _massing3DTarget - position;
        _massingCamera3D.UpDirection = new Vector3D(0, 1, 0);
    }

    private void MassingViewport3D_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_massingViewport3D == null)
            return;

        Point position = e.GetPosition(_massingViewport3D);
        _massing3DDragStart = position;
        _massing3DMouseDown = position;
        _massing3DMouseMoved = false;
        _massingViewport3D.CaptureMouse();
    }

    private void MassingViewport3D_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_massingViewport3D != null && _massing3DMouseDown != null && !_massing3DMouseMoved)
            TrySelectMassing3DObject(e.GetPosition(_massingViewport3D));

        _massing3DDragStart = null;
        _massing3DMouseDown = null;
        _massingViewport3D?.ReleaseMouseCapture();
    }

    private void MassingViewport3D_MouseMove(object sender, MouseEventArgs e)
    {
        if (_massingViewport3D == null || _massing3DDragStart == null || e.LeftButton != MouseButtonState.Pressed)
            return;

        Point current = e.GetPosition(_massingViewport3D);
        Vector delta = current - _massing3DDragStart.Value;
        if (delta.Length > 2.5)
            _massing3DMouseMoved = true;
        _massing3DDragStart = current;
        _massing3DYaw += delta.X * 0.45;
        _massing3DPitch = Math.Clamp(_massing3DPitch - delta.Y * 0.35, -8, 88);
        UpdateMassing3DCamera();
    }

    private void TrySelectMassing3DObject(Point point)
    {
        if (_massingViewport3D == null)
            return;

        Massing3DObjectInfo? selected = null;
        VisualTreeHelper.HitTest(
            _massingViewport3D,
            null,
            result =>
            {
                if (result is RayHitTestResult ray &&
                    ray.ModelHit is GeometryModel3D model &&
                    _massing3DObjectInfo.TryGetValue(model, out Massing3DObjectInfo? info))
                {
                    selected = info;
                    return HitTestResultBehavior.Stop;
                }

                return HitTestResultBehavior.Continue;
            },
            new PointHitTestParameters(point));

        if (selected != null)
            SelectMassing3DObject(selected);
    }

    private void SelectMassing3DObject(Massing3DObjectInfo info)
    {
        _selectedMassing3DObjectId = info.Id;
        SelectFirstMassingMarker(info.SourceMarkerIds);
        RefreshMassing3DPreview(_currentMassingDraft);

        string sources = info.SourceMarkerIds.Count == 0
            ? "no source marker"
            : string.Join(", ", info.SourceMarkerIds);
        if (_massingViewportStatusText != null)
        {
            _massingViewportStatusText.Text =
                $"Selected: {info.Label} ({info.Kind}) | source: {sources}" +
                (string.IsNullOrWhiteSpace(info.Notes) ? "" : $" | {info.Notes}");
        }

        TxtStatus.Text = info.SourceMarkerIds.Count == 0
            ? $"Selected 3D {info.Kind}: {info.Label}."
            : $"Selected 3D {info.Kind}: {info.Label}; source marker selected in 3D Massing table.";
    }

    private void SelectFirstMassingMarker(IReadOnlyList<string> sourceMarkerIds)
    {
        if (_massingMarkerList == null || sourceMarkerIds.Count == 0)
            return;

        foreach (object item in _massingMarkerList.Items)
        {
            if (item is not MassingMarkerReviewRow row)
                continue;

            if (!sourceMarkerIds.Contains(row.MarkerId, StringComparer.OrdinalIgnoreCase))
                continue;

            _massingMarkerList.SelectedItem = row;
            _massingMarkerList.ScrollIntoView(row);
            return;
        }
    }

    private void MassingViewport3D_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_massingCamera3D == null)
            return;

        double factor = e.Delta > 0 ? 0.88 : 1.14;
        _massing3DDistance = Math.Clamp(_massing3DDistance * factor, 4, Math.Max(20, _massing3DSceneRadius * 16));
        UpdateMassing3DCamera();
    }

    private void DrawMassingPreview(SmartMassingDraft? draft)
    {
        if (_massingPreviewCanvas == null)
            return;

        _massingPreviewCanvas.Children.Clear();

        List<SmartMassingPoint> points = draft?.Footprints
            .SelectMany(footprint => footprint.Points)
            .Where(point => !double.IsNaN(point.X) && !double.IsNaN(point.Y))
            .ToList() ?? [];

        if (draft == null || points.Count == 0)
        {
            if (_massingPreviewStatusText != null)
            {
                _massingPreviewStatusText.Text = draft == null
                    ? "Build a draft to preview the footprint."
                    : "Draft has no footprint points yet.";
            }
            return;
        }

        double width = _massingPreviewCanvas.ActualWidth > 40 ? _massingPreviewCanvas.ActualWidth : 280;
        double height = _massingPreviewCanvas.ActualHeight > 40 ? _massingPreviewCanvas.ActualHeight : 160;
        double minX = points.Min(point => point.X);
        double maxX = points.Max(point => point.X);
        double minY = points.Min(point => point.Y);
        double maxY = points.Max(point => point.Y);
        if (Math.Abs(maxX - minX) < 0.001)
        {
            minX -= 1;
            maxX += 1;
        }
        if (Math.Abs(maxY - minY) < 0.001)
        {
            minY -= 1;
            maxY += 1;
        }

        const double margin = 20;
        double scale = Math.Min((width - margin * 2) / (maxX - minX), (height - margin * 2) / (maxY - minY));
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            scale = 1;

        Point Project(SmartMassingPoint point)
        {
            double x = margin + (point.X - minX) * scale;
            double y = height - margin - (point.Y - minY) * scale;
            return new Point(x, y);
        }

        Brush footprintFill = new SolidColorBrush(Color.FromArgb(42, 96, 165, 250));
        Brush footprintStroke = new SolidColorBrush(Color.FromRgb(96, 165, 250));
        Brush selectedStroke = new SolidColorBrush(Color.FromRgb(255, 183, 77));
        string selectedMarkerId = SelectedMassingMarkerRow()?.MarkerId ?? "";

        List<SmartMassingFootprint> previewFootprints = draft.Footprints
            .Where(footprint => footprint.Points.Count > 0)
            .ToList();

        foreach (SmartMassingFootprint footprint in previewFootprints)
        {
            var polygon = new System.Windows.Shapes.Polygon
            {
                Fill = footprintFill,
                Stroke = footprintStroke,
                StrokeThickness = 1.5,
                Points = new PointCollection(footprint.Points.Select(Project)),
                ToolTip = $"{footprint.Id}: level {footprint.Level}, base {footprint.BaseElevation:F2} {footprint.BaseElevationUnits}, height {footprint.Height:F2} {footprint.HeightUnits}, {footprint.Points.Count} points",
            };
            _massingPreviewCanvas.Children.Add(polygon);
        }

        DrawMassingRoofGuides(draft.Roof.Guides, Project, selectedMarkerId, selectedStroke);

        foreach (SmartMassingFootprint footprint in previewFootprints)
        {
            for (int i = 0; i < footprint.Points.Count; i++)
            {
                SmartMassingPoint point = footprint.Points[i];
                bool selected = !string.IsNullOrWhiteSpace(point.SourceMarkerId) &&
                    string.Equals(point.SourceMarkerId, selectedMarkerId, StringComparison.OrdinalIgnoreCase);
                AddMassingPreviewPoint(Project(point), i + 1, point.SourceMarkerId, selected, selectedStroke);
            }
        }

        if (_massingPreviewStatusText != null)
        {
            string roof = string.IsNullOrWhiteSpace(draft.Roof.Pitch)
                ? draft.Roof.Type
                : $"{draft.Roof.Type} {draft.Roof.Pitch}";
            _massingPreviewStatusText.Text =
                $"{points.Count} footprint pts | {draft.Units} | roof: {roof} ({draft.Roof.Status}) | roof guides: {draft.Roof.Guides.Count} | questions: {draft.UnresolvedQuestions.Count}";
        }
    }

    private void DrawMassingRoofGuides(
        IReadOnlyList<SmartMassingRoofGuide> guides,
        Func<SmartMassingPoint, Point> project,
        string selectedMarkerId,
        Brush selectedStroke)
    {
        if (_massingPreviewCanvas == null || guides.Count == 0)
            return;

        foreach (SmartMassingRoofGuide guide in guides)
        {
            if (string.Equals(guide.Status, "rejected", StringComparison.OrdinalIgnoreCase))
                continue;

            List<Point> points = guide.Points
                .Where(point => !double.IsNaN(point.X) && !double.IsNaN(point.Y))
                .Select(project)
                .ToList();
            if (points.Count == 0)
                continue;

            bool selected = !string.IsNullOrWhiteSpace(selectedMarkerId) &&
                guide.SourceMarkerIds.Contains(selectedMarkerId, StringComparer.OrdinalIgnoreCase);
            Brush stroke = selected ? selectedStroke : new SolidColorBrush(Color.FromRgb(255, 183, 77));
            Brush fill = new SolidColorBrush(Color.FromArgb(32, 255, 183, 77));

            if (guide.Kind is "eave_outline" or "cap" && points.Count >= 3)
            {
                var polygon = new System.Windows.Shapes.Polygon
                {
                    Points = new PointCollection(points),
                    Fill = guide.Kind == "cap" ? fill : Brushes.Transparent,
                    Stroke = stroke,
                    StrokeThickness = selected ? 2.2 : 1.2,
                    StrokeDashArray = new DoubleCollection { 5, 4 },
                    ToolTip = RoofGuideTooltip(guide),
                };
                _massingPreviewCanvas.Children.Add(polygon);
                AddMassingGuideLabel(points[0], guide.Kind == "cap" ? "roof cap" : "eave", stroke, selected);
                continue;
            }

            if (guide.Kind == "slope_arrow" && points.Count >= 2)
            {
                AddMassingGuideLine(points[0], points[^1], stroke, selected, RoofGuideTooltip(guide), dashed: false);
                AddMassingArrowHead(points[^2], points[^1], stroke);
                AddMassingGuideLabel(Midpoint(points[0], points[^1]), "slope", stroke, selected);
                continue;
            }

            if (points.Count >= 2)
            {
                AddMassingGuideLine(points[0], points[^1], stroke, selected, RoofGuideTooltip(guide), dashed: true);
                string label = guide.Kind switch
                {
                    "hip_ridge" => "hip ridge",
                    "axis_candidate" => "roof axis",
                    "valley" => "valley",
                    "roof_edge" => "roof edge",
                    "high_edge" => "high edge",
                    "low_edge" => "low edge",
                    "overhang" => "overhang",
                    _ => "ridge",
                };
                AddMassingGuideLabel(Midpoint(points[0], points[^1]), label, stroke, selected);
            }
        }
    }

    private void AddMassingGuideLine(Point start, Point end, Brush stroke, bool selected, string tooltip, bool dashed)
    {
        if (_massingPreviewCanvas == null)
            return;

        var line = new System.Windows.Shapes.Line
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = end.X,
            Y2 = end.Y,
            Stroke = stroke,
            StrokeThickness = selected ? 2.4 : 1.8,
            ToolTip = tooltip,
        };
        if (dashed)
            line.StrokeDashArray = new DoubleCollection { 6, 4 };
        _massingPreviewCanvas.Children.Add(line);
    }

    private void AddMassingArrowHead(Point start, Point end, Brush fill)
    {
        if (_massingPreviewCanvas == null)
            return;

        double angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        const double size = 8;
        Point p1 = end;
        Point p2 = new(
            end.X - Math.Cos(angle - Math.PI / 6) * size,
            end.Y - Math.Sin(angle - Math.PI / 6) * size);
        Point p3 = new(
            end.X - Math.Cos(angle + Math.PI / 6) * size,
            end.Y - Math.Sin(angle + Math.PI / 6) * size);

        var head = new System.Windows.Shapes.Polygon
        {
            Points = new PointCollection { p1, p2, p3 },
            Fill = fill,
            Stroke = fill,
            StrokeThickness = 1,
        };
        _massingPreviewCanvas.Children.Add(head);
    }

    private void AddMassingGuideLabel(Point point, string text, Brush foreground, bool selected)
    {
        if (_massingPreviewCanvas == null)
            return;

        var label = new TextBlock
        {
            Text = text,
            FontSize = 10,
            FontWeight = FontWeights.Normal,
            Foreground = foreground,
            Background = new SolidColorBrush(Color.FromArgb(170, 20, 20, 20)),
            Padding = new Thickness(3, 1, 3, 1),
        };
        Canvas.SetLeft(label, point.X + 6);
        Canvas.SetTop(label, point.Y + 4);
        _massingPreviewCanvas.Children.Add(label);
    }

    private static Point Midpoint(Point first, Point second) =>
        new((first.X + second.X) / 2, (first.Y + second.Y) / 2);

    private static string RoofGuideTooltip(SmartMassingRoofGuide guide) =>
        $"{guide.Label}\nStatus: {guide.Status}\nKind: {guide.Kind}\nConfidence: {guide.Confidence:P0}\n{guide.Notes}".Trim();

    private void AddMassingPreviewPoint(Point point, int index, string sourceMarkerId, bool selected, Brush selectedStroke)
    {
        if (_massingPreviewCanvas == null)
            return;

        double radius = selected ? 5.5 : 3.8;
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Fill = selected ? selectedStroke : new SolidColorBrush(Color.FromRgb(96, 165, 250)),
            Stroke = selected ? Brushes.White : new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)),
            StrokeThickness = selected ? 1.5 : 1,
            ToolTip = string.IsNullOrWhiteSpace(sourceMarkerId)
                ? $"Footprint point {index}"
                : $"Footprint point {index}: {sourceMarkerId}",
        };
        Canvas.SetLeft(dot, point.X - radius);
        Canvas.SetTop(dot, point.Y - radius);
        _massingPreviewCanvas.Children.Add(dot);

        var label = new TextBlock
        {
            Text = index.ToString(CultureInfo.InvariantCulture),
            FontSize = 10,
            FontWeight = FontWeights.Normal,
            Foreground = selected ? selectedStroke : PreviewForegroundBrush(),
        };
        Canvas.SetLeft(label, point.X + 6);
        Canvas.SetTop(label, point.Y - 10);
        _massingPreviewCanvas.Children.Add(label);
    }

    private Brush PreviewForegroundBrush() =>
        TryFindResource("ControlForegroundBrush") as Brush ?? Brushes.White;

    private void RefreshMassingMarkerRows(SmartMassingDraft? draft)
    {
        if (_massingMarkerList == null)
            return;

        _massingMarkerList.Items.Clear();
        if (_currentJob == null || draft == null)
        {
            UpdateMassingMarkerActionButtons();
            return;
        }

        foreach (MassingMarkerReviewRow row in BuildMassingMarkerRows(draft))
            _massingMarkerList.Items.Add(row);
        UpdateMassingMarkerActionButtons();
    }

    private IReadOnlyList<MassingMarkerReviewRow> BuildMassingMarkerRows(SmartMassingDraft draft)
    {
        if (_currentJob == null)
            return [];

        var roles = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        var draftPoints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var orderedIds = new List<string>();

        void AddMarkerRole(string markerId, string role)
        {
            if (string.IsNullOrWhiteSpace(markerId))
                return;

            markerId = markerId.Trim();
            if (!roles.TryGetValue(markerId, out SortedSet<string>? markerRoles))
            {
                markerRoles = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                roles[markerId] = markerRoles;
            }

            markerRoles.Add(role);
            if (!orderedIds.Contains(markerId, StringComparer.OrdinalIgnoreCase))
                orderedIds.Add(markerId);
        }

        foreach (SmartMassingFootprint footprint in draft.Footprints)
        {
            foreach (string markerId in footprint.SourceMarkerIds)
                AddMarkerRole(markerId, $"Level {footprint.Level} footprint");
            double baseZ = SmartMassingDraftService.DisplayBaseElevation(draft, footprint);
            foreach (SmartMassingPoint point in footprint.Points)
            {
                AddMarkerRole(point.SourceMarkerId, $"Level {footprint.Level} corner");
                draftPoints[point.SourceMarkerId] = $"{point.X:F2}, {point.Y:F2}, z {baseZ:F2}";
            }
        }

        foreach (string markerId in draft.Roof.SourceMarkerIds)
            AddMarkerRole(markerId, "Roof");
        foreach (SmartMassingOpening opening in draft.Openings)
            AddMarkerRole(opening.SourceMarkerId, string.IsNullOrWhiteSpace(opening.Type) ? "Opening" : opening.Type);
        foreach (string markerId in draft.SourceMarkerIds)
            AddMarkerRole(markerId, "Source");

        return orderedIds
            .Select(markerId =>
            {
                SmartAiMarker? marker = SmartContextStore.LoadAiMarker(_currentJob, markerId);
                string role = roles.TryGetValue(markerId, out SortedSet<string>? markerRoles)
                    ? string.Join(", ", markerRoles)
                    : "Source";
                string status = marker == null
                    ? "missing"
                    : _hiddenAiMarkerTypes.Contains(marker.Type)
                        ? "hidden"
                        : marker.SampleKind;

                return new MassingMarkerReviewRow
                {
                    MarkerId = markerId,
                    Role = role,
                    Type = marker?.Type ?? "",
                    Page = marker?.Page ?? "",
                    PdfPoint = marker == null ? "" : $"{marker.PdfPoint.X:F0}, {marker.PdfPoint.Y:F0}",
                    DraftPoint = draftPoints.TryGetValue(markerId, out string? draftPoint) ? draftPoint : "",
                    Status = status,
                    Marker = marker,
                    HasCrop = marker != null && File.Exists(ResolveAiContextPath(marker.CropPath)),
                };
            })
            .ToList();
    }

    private void UpdateMassingMarkerActionButtons()
    {
        MassingMarkerReviewRow? row = SelectedMassingMarkerRow();
        bool hasMarker = row?.Marker != null;
        if (_massingJumpMarkerButton != null)
            _massingJumpMarkerButton.IsEnabled = hasMarker;
        if (_massingOpenMarkerButton != null)
            _massingOpenMarkerButton.IsEnabled = hasMarker;
        if (_massingOpenMarkerCropButton != null)
            _massingOpenMarkerCropButton.IsEnabled = row?.HasCrop == true;
        UpdateMassingMarkerDetails(row);
        DrawMassingPreview(_currentMassingDraft);
        RefreshMassing3DPreview(_currentMassingDraft);
    }

    private void UpdateMassingMarkerDetails(MassingMarkerReviewRow? row)
    {
        if (_massingMarkerDetailsTextBox == null)
            return;

        if (row == null)
        {
            _massingMarkerDetailsTextBox.Text = "Select a source marker to inspect the evidence behind the draft.";
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Marker: {row.MarkerId}");
        sb.AppendLine($"Role: {row.Role}");
        sb.AppendLine($"Status: {row.Status}");
        if (!string.IsNullOrWhiteSpace(row.DraftPoint))
            sb.AppendLine($"Draft point: {row.DraftPoint}");

        if (row.Marker is not { } marker)
        {
            sb.AppendLine("Marker JSON is missing, so this draft source needs review.");
            _massingMarkerDetailsTextBox.Text = sb.ToString();
            return;
        }

        sb.AppendLine($"Type: {marker.Type}");
        sb.AppendLine($"Sample: {marker.SampleKind}");
        sb.AppendLine($"Page: {marker.Page}");
        sb.AppendLine($"PDF point: {marker.PdfPoint.X:F1}, {marker.PdfPoint.Y:F1}");
        if (marker.PdfRect.Right > marker.PdfRect.Left && marker.PdfRect.Bottom > marker.PdfRect.Top)
            sb.AppendLine($"PDF rect: {marker.PdfRect.Left:F1}, {marker.PdfRect.Top:F1}, {marker.PdfRect.Right:F1}, {marker.PdfRect.Bottom:F1}");
        if (!string.IsNullOrWhiteSpace(marker.Value))
            sb.AppendLine($"Value: {marker.Value}");
        if (!string.IsNullOrWhiteSpace(marker.Note))
            sb.AppendLine($"Note: {marker.Note}");
        if (!string.IsNullOrWhiteSpace(marker.CropPath))
        {
            string cropPath = ResolveAiContextPath(marker.CropPath);
            sb.AppendLine(File.Exists(cropPath)
                ? $"Crop: {marker.CropPath}"
                : $"Crop missing: {marker.CropPath}");
        }
        if (_currentJob != null)
            sb.AppendLine($"JSON: {SmartContextStore.AiMarkerPath(_currentJob, marker.Id)}");

        _massingMarkerDetailsTextBox.Text = sb.ToString();
    }

    private MassingMarkerReviewRow? SelectedMassingMarkerRow() =>
        _massingMarkerList?.SelectedItem as MassingMarkerReviewRow;

    private void JumpToSelectedMassingMarker()
    {
        if (_currentJob == null || SelectedMassingMarkerRow()?.Marker is not { } marker)
            return;

        PageInfo? page = ResolveMassingMarkerPage(marker);
        if (page == null)
        {
            TxtStatus.Text = $"Source marker page is missing for {marker.Id}.";
            return;
        }

        SelectPageByFolder(page.FolderPath);
        Dispatcher.BeginInvoke(() =>
        {
            _viewport.FocusPdfPoint(marker.PdfPoint.X, marker.PdfPoint.Y);
        }, System.Windows.Threading.DispatcherPriority.Background);
        TxtStatus.Text = $"Opened source marker {marker.Id} on {page.Name} at PDF {marker.PdfPoint.X:F1}, {marker.PdfPoint.Y:F1}.";
    }

    private void OpenSelectedMassingMarkerJson()
    {
        if (_currentJob == null || SelectedMassingMarkerRow()?.Marker is not { } marker)
            return;

        OpenJsonFile(SmartContextStore.AiMarkerPath(_currentJob, marker.Id), "AI marker JSON is missing.");
    }

    private void OpenSelectedMassingMarkerCrop()
    {
        if (_currentJob == null || SelectedMassingMarkerRow()?.Marker is not { } marker)
            return;

        string path = ResolveAiContextPath(marker.CropPath);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            TxtStatus.Text = "AI marker crop file is missing.";
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }

    private PageInfo? ResolveMassingMarkerPage(SmartAiMarker marker)
    {
        if (_currentJob == null)
            return null;

        if (!string.IsNullOrWhiteSpace(marker.PageFolder))
        {
            string folder = Path.IsPathFullyQualified(marker.PageFolder)
                ? marker.PageFolder
                : Path.GetFullPath(Path.Combine(_currentJob.RootPath, marker.PageFolder));
            PageInfo? page = SmartTakeoffsJobStore.TryReadPage(folder);
            if (page != null)
                return page;
        }

        return FindPageByName(marker.Page);
    }

    private string ResolveAiContextPath(string path)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(path))
            return "";

        return Path.IsPathFullyQualified(path)
            ? path
            : Path.GetFullPath(Path.Combine(_currentJob.AIContextRoot, path));
    }

    private static string BuildMassingDraftSummary(SmartMassingDraft draft, string path)
    {
        int footprintCount = draft.Footprints.Count;
        int footprintPoints = draft.Footprints.Sum(footprint => footprint.Points.Count);
        string roofSummary = string.IsNullOrWhiteSpace(draft.Roof.Pitch)
            ? $"{draft.Roof.Type} ({draft.Roof.Confidence:P0})"
            : $"{draft.Roof.Type}, pitch {draft.Roof.Pitch} ({draft.Roof.Confidence:P0})";

        var sb = new StringBuilder();
        sb.AppendLine("3D Massing Draft");
        sb.AppendLine($"Path: {path}");
        sb.AppendLine($"Status: {draft.Status}");
        sb.AppendLine($"Units: {draft.Units}");
        sb.AppendLine($"Generated UTC: {draft.GeneratedAtUtc}");
        if (!string.IsNullOrWhiteSpace(draft.ReviewedAtUtc))
            sb.AppendLine($"Reviewed UTC: {draft.ReviewedAtUtc}");
        if (!string.IsNullOrWhiteSpace(draft.ReviewNotes))
            sb.AppendLine($"Review notes: {draft.ReviewNotes}");
        sb.AppendLine();
        sb.AppendLine("Summary");
        sb.AppendLine($"- Footprints: {footprintCount}");
        sb.AppendLine($"- Footprint points: {footprintPoints}");
        sb.AppendLine($"- Openings: {draft.Openings.Count}");
        sb.AppendLine($"- Roof: {roofSummary}");
        sb.AppendLine($"- Roof planes: {draft.Roof.Planes.Count}");
        sb.AppendLine($"- Assumptions: {draft.Assumptions.Count}");
        sb.AppendLine($"- Unresolved questions: {draft.UnresolvedQuestions.Count}");

        foreach (SmartMassingFootprint footprint in draft.Footprints)
        {
            sb.AppendLine();
            sb.AppendLine($"Footprint {footprint.Id}");
            sb.AppendLine($"- Level: {footprint.Level}");
            sb.AppendLine($"- Page: {footprint.Page}");
            sb.AppendLine($"- Base elevation: {footprint.BaseElevation:F2} {footprint.BaseElevationUnits}");
            sb.AppendLine($"- Height: {footprint.Height:F2} {footprint.HeightUnits}");
            sb.AppendLine($"- Confidence: {footprint.Confidence:P0}");
            sb.AppendLine($"- Points: {footprint.Points.Count}");
            foreach (SmartMassingPoint point in footprint.Points)
                sb.AppendLine($"  - {point.X:F3}, {point.Y:F3} ({point.SourceMarkerId})");
        }

        sb.AppendLine();
        sb.AppendLine("Roof");
        sb.AppendLine($"- Status: {draft.Roof.Status}");
        sb.AppendLine($"- Type: {draft.Roof.Type}");
        if (draft.Roof.Elevation > 0)
            sb.AppendLine($"- Elevation: {draft.Roof.Elevation:F2} {draft.Roof.ElevationUnits}");
        sb.AppendLine($"- Pitch: {(string.IsNullOrWhiteSpace(draft.Roof.Pitch) ? "unknown" : draft.Roof.Pitch)}");
        sb.AppendLine($"- Confidence: {draft.Roof.Confidence:P0}");
        if (!string.IsNullOrWhiteSpace(draft.Roof.ReviewedAtUtc))
            sb.AppendLine($"- Reviewed UTC: {draft.Roof.ReviewedAtUtc}");
        sb.AppendLine($"- Guides: {draft.Roof.Guides.Count}");
        sb.AppendLine($"- Planes: {draft.Roof.Planes.Count}");
        foreach (SmartMassingRoofGuide guide in draft.Roof.Guides)
        {
            sb.AppendLine($"  - {guide.Kind}: {guide.Label} ({guide.Status}, {guide.Points.Count} pts, {guide.Confidence:P0})");
            if (!string.IsNullOrWhiteSpace(guide.Notes))
                sb.AppendLine($"    {guide.Notes}");
        }
        foreach (SmartMassingPlane plane in draft.Roof.Planes)
        {
            sb.AppendLine($"  - plane {plane.Kind}: {plane.Label} ({plane.Status}, {plane.Points.Count} pts, {plane.Confidence:P0})");
            if (!string.IsNullOrWhiteSpace(plane.Notes))
                sb.AppendLine($"    {plane.Notes}");
        }
        if (!string.IsNullOrWhiteSpace(draft.Roof.Notes))
            sb.AppendLine($"- Notes: {draft.Roof.Notes}");
        if (!string.IsNullOrWhiteSpace(draft.Roof.ReviewNotes))
            sb.AppendLine($"- Review notes: {draft.Roof.ReviewNotes}");

        sb.AppendLine();
        sb.AppendLine("Openings");
        if (draft.Openings.Count == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (SmartMassingOpening opening in draft.Openings)
            {
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "- {0}: {1} ({2}, wall {3}, center {4:0.###}/{5:0.###}/{6:0.###}, {7:0.###} x {8:0.###}, {9:P0})",
                    opening.Type,
                    opening.SourceMarkerId,
                    opening.Status,
                    opening.WallIndex,
                    opening.Center.X,
                    opening.Center.Y,
                    opening.Center.Z,
                    opening.Width,
                    opening.Height,
                    opening.Confidence));
                if (!string.IsNullOrWhiteSpace(opening.Notes))
                    sb.AppendLine($"  {opening.Notes}");
            }
        }

        AppendMassingList(sb, "Assumptions", draft.Assumptions);
        AppendMassingList(sb, "Unresolved Questions", draft.UnresolvedQuestions);
        AppendMassingList(sb, "Source Markers", draft.SourceMarkerIds);
        return sb.ToString();
    }

    private static void AppendMassingList(StringBuilder sb, string title, IReadOnlyList<string> items)
    {
        sb.AppendLine();
        sb.AppendLine(title);
        if (items.Count == 0)
        {
            sb.AppendLine("- none");
            return;
        }

        foreach (string item in items)
            sb.AppendLine($"- {item}");
    }

    private void CreateMarkerSetFromCurrentFilter()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before creating marker sets.";
            return;
        }

        List<SmartAiMarker> markers = LoadMarkersForCurrentFilter(includeHiddenTypes: true);
        if (markers.Count == 0)
        {
            TxtStatus.Text = "No AI markers match the current filters.";
            MessageBox.Show(
                "No active AI markers match the current type/sample filters.",
                "Create Marker Set",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        MarkerSetInput? input = ShowMarkerSetDialog(DefaultMarkerSetName(markers), markers.Count);
        if (input == null)
            return;

        SmartAiMarkerSet set = SmartContextStore.SaveAiMarkerSet(
            _currentJob,
            input.Name,
            input.Description,
            SelectedMarkerTypeFilterForMarkers(),
            SelectedMarkerSampleFilter(),
            markers);
        TxtStatus.Text = $"Saved marker set '{set.Name}' with {set.MarkerCount} markers.";
    }

    private bool CanManageMarkerSets() =>
        _currentJob != null && SmartContextStore.LoadAiMarkerSets(_currentJob).Count > 0;

    private void ManageMarkerSets()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before managing marker sets.";
            return;
        }

        IReadOnlyList<SmartAiMarkerSet> sets = SmartContextStore.LoadAiMarkerSets(_currentJob);
        if (sets.Count == 0)
        {
            TxtStatus.Text = "No marker sets saved yet.";
            MessageBox.Show(
                "Create a marker set from the current marker filters first.",
                "Marker Sets",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new MarkerSetsDialog(sets) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result == null)
            return;

        MarkerSetsDialogResult result = dialog.Result;
        SmartAiMarkerSet set = result.MarkerSet;
        try
        {
            switch (result.Action)
            {
                case MarkerSetsDialogAction.Apply:
                    ApplyMarkerSetFilter(set);
                    TxtStatus.Text = $"Applied marker set '{set.Name}' ({set.MarkerCount} markers).";
                    break;

                case MarkerSetsDialogAction.Rename:
                    set.Name = result.Name;
                    set.Description = result.Description;
                    SmartContextStore.SaveAiMarkerSet(_currentJob, set);
                    TxtStatus.Text = $"Renamed marker set to '{set.Name}'.";
                    break;

                case MarkerSetsDialogAction.Delete:
                    DeleteMarkerSet(set);
                    break;

                case MarkerSetsDialogAction.OpenJson:
                    OpenJsonFile(
                        SmartContextStore.AiMarkerSetPath(_currentJob, set.Id),
                        "Marker set JSON is missing.");
                    break;
            }
        }
        catch (Exception ex)
        {
            ShowOperationError("Marker Sets", ex);
        }
    }

    private void ApplyMarkerSetFilter(SmartAiMarkerSet set)
    {
        string typeFilter = string.IsNullOrWhiteSpace(set.TypeFilter)
            ? MarkerTypeFilterAllMarkers
            : set.TypeFilter.Trim();
        string sampleFilter = string.IsNullOrWhiteSpace(set.SampleKindFilter)
            ? MarkerSampleFilterAny
            : set.SampleKindFilter.Trim();

        if (string.Equals(typeFilter, MarkerTypeFilterAllInbox, StringComparison.OrdinalIgnoreCase))
            typeFilter = MarkerTypeFilterAllMarkers;

        SelectComboValue(ComboMarkerTypeFilter, typeFilter);
        SelectComboValue(ComboMarkerSampleFilter, sampleFilter);

        if (!string.Equals(typeFilter, MarkerTypeFilterAllMarkers, StringComparison.OrdinalIgnoreCase) &&
            _hiddenAiMarkerTypes.Remove(typeFilter))
        {
            SavePersistedMarkerVisibility();
        }

        LoadObservationsInbox();
        RefreshAiMarkersOverlay();
    }

    private void DeleteMarkerSet(SmartAiMarkerSet set)
    {
        if (_currentJob == null)
            return;

        MessageBoxResult answer = MessageBox.Show(
            $"Delete marker set '{set.Name}'?\n\nThis only removes the saved set file. Marker JSON and crop evidence stay in AI_Context.",
            "Delete Marker Set",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        bool deleted = SmartContextStore.DeleteAiMarkerSet(_currentJob, set.Id);
        TxtStatus.Text = deleted
            ? $"Deleted marker set '{set.Name}'."
            : "Marker set JSON was already missing.";
    }

    private static void SelectComboValue(ComboBox combo, string value)
    {
        foreach (object item in combo.Items)
        {
            if (string.Equals(item?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        combo.Items.Add(value);
        combo.SelectedItem = value;
    }

    private void ExportMarkersContext(bool openAfterExport)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before exporting marker context.";
            return;
        }

        List<SmartAiMarker> markers = LoadMarkersForCurrentFilter(includeHiddenTypes: false);
        if (markers.Count == 0)
        {
            TxtStatus.Text = "No visible AI markers match the current filters.";
            MessageBox.Show(
                "No visible AI markers match the current type/sample filters.",
                "Export Marker Context",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        string path = SmartContextStore.ExportAiMarkersContext(
            _currentJob,
            markers,
            _hiddenAiMarkerTypes.ToList(),
            SelectedMarkerTypeFilterForMarkers(),
            SelectedMarkerSampleFilter());

        string relativePath = Path.GetRelativePath(_currentJob.RootPath, path);
        TxtStatus.Text = $"Exported {markers.Count} AI markers -> {relativePath}";
        if (openAfterExport)
            OpenJsonFile(path, "AI marker context export is missing.");
    }

    private List<SmartAiMarker> LoadMarkersForCurrentFilter(bool includeHiddenTypes)
    {
        if (_currentJob == null)
            return [];

        return SmartContextStore.LoadAiMarkers(_currentJob)
            .Where(MarkerMatchesCurrentFilters)
            .Where(marker => includeHiddenTypes || !_hiddenAiMarkerTypes.Contains(marker.Type))
            .OrderBy(marker => marker.Type, StringComparer.OrdinalIgnoreCase)
            .ThenBy(marker => marker.SampleKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(marker => marker.Page, StringComparer.OrdinalIgnoreCase)
            .ThenBy(marker => marker.CreatedAtUtc)
            .ToList();
    }

    private bool MarkerMatchesCurrentFilters(SmartAiMarker marker)
    {
        string typeFilter = SelectedMarkerTypeFilter();
        bool typeMatches =
            string.Equals(typeFilter, MarkerTypeFilterAllInbox, StringComparison.Ordinal) ||
            string.Equals(typeFilter, MarkerTypeFilterAllMarkers, StringComparison.Ordinal) ||
            string.Equals(marker.Type, typeFilter, StringComparison.OrdinalIgnoreCase);
        if (!typeMatches)
            return false;

        string sampleFilter = SelectedMarkerSampleFilter();
        return string.Equals(sampleFilter, MarkerSampleFilterAny, StringComparison.Ordinal) ||
               string.Equals(marker.SampleKind, sampleFilter, StringComparison.OrdinalIgnoreCase);
    }

    private string SelectedMarkerTypeFilterForMarkers()
    {
        string typeFilter = SelectedMarkerTypeFilter();
        return string.Equals(typeFilter, MarkerTypeFilterAllInbox, StringComparison.Ordinal)
            ? MarkerTypeFilterAllMarkers
            : typeFilter;
    }

    private string DefaultMarkerSetName(IReadOnlyCollection<SmartAiMarker> markers)
    {
        string typeFilter = SelectedMarkerTypeFilterForMarkers();
        string sampleFilter = SelectedMarkerSampleFilter();
        string typeLabel = string.Equals(typeFilter, MarkerTypeFilterAllMarkers, StringComparison.Ordinal)
            ? "All Markers"
            : typeFilter;
        if (!string.Equals(sampleFilter, MarkerSampleFilterAny, StringComparison.Ordinal))
            typeLabel += $" - {sampleFilter}";
        return $"{typeLabel} ({markers.Count})";
    }

    private MarkerSetInput? ShowMarkerSetDialog(string defaultName, int markerCount)
    {
        var win = new Window
        {
            Title = "Create Marker Set",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = this,
        };

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Markers: {markerCount}   Type: {SelectedMarkerTypeFilterForMarkers()}   Sample: {SelectedMarkerSampleFilter()}",
            Margin = new Thickness(0, 0, 0, 10),
            FontWeight = FontWeights.Normal,
        });

        panel.Children.Add(new TextBlock { Text = "Set name", Margin = new Thickness(0, 0, 0, 4) });
        var nameBox = new TextBox { Text = defaultName, Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(nameBox);

        panel.Children.Add(new TextBlock { Text = "Description / use", Margin = new Thickness(0, 0, 0, 4) });
        var descriptionBox = new TextBox
        {
            Height = 76,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 8),
        };
        panel.Children.Add(descriptionBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var ok = new Button { Content = "Save", Width = 78, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 78, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        win.Content = panel;
        MarkerSetInput? result = null;
        ok.Click += (_, _) =>
        {
            string name = nameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Marker set name is required.", "Create Marker Set",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            result = new MarkerSetInput(name, descriptionBox.Text.Trim());
            win.DialogResult = true;
        };
        win.Loaded += (_, _) => nameBox.Focus();

        return win.ShowDialog() == true ? result : null;
    }

    private bool CanGoToObservationPage(ObservationDisplayItem item) =>
        _currentJob != null && !string.IsNullOrWhiteSpace(item.Page) && FindPageByName(item.Page) != null;

    private void GoToObservationPage(ObservationDisplayItem item)
    {
        if (FindPageByName(item.Page) is { } page)
            SelectPageByFolder(page.FolderPath);
    }

    private bool CanOpenObservationCrop(ObservationDisplayItem item) =>
        _currentJob != null &&
        !string.IsNullOrWhiteSpace(item.CropRelativePath) &&
        File.Exists(ObservationCropPath(item));

    private void OpenObservationCrop(ObservationDisplayItem item)
    {
        string path = ObservationCropPath(item);
        if (!File.Exists(path))
        {
            TxtStatus.Text = "AI crop file is missing.";
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }

    private void OpenObservationCropFolder(ObservationDisplayItem item)
    {
        string path = ObservationCropPath(item);
        string? folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folder))
            OpenFolderInExplorer(folder);
    }

    private string ObservationCropPath(ObservationDisplayItem item)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(item.CropRelativePath))
            return "";

        return Path.GetFullPath(Path.Combine(_currentJob.AIContextRoot, item.CropRelativePath));
    }

    private bool CanOpenAiMarkerFile(ObservationDisplayItem item) =>
        _currentJob != null && File.Exists(AiMarkerPath(item));

    private bool CanOpenCropBookmarkFile(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return false;

        SmartAiCropBookmark? bookmark = SmartContextStore.FindCropBookmarkByObservation(_currentJob, item.Observation.Id);
        return bookmark != null && File.Exists(SmartContextStore.CropBookmarkPath(_currentJob, bookmark.Id));
    }

    private void OpenCropBookmarkFile(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return;

        SmartAiCropBookmark? bookmark = SmartContextStore.FindCropBookmarkByObservation(_currentJob, item.Observation.Id);
        if (bookmark == null)
        {
            TxtStatus.Text = "Crop bookmark JSON is missing.";
            return;
        }

        OpenJsonFile(
            SmartContextStore.CropBookmarkPath(_currentJob, bookmark.Id),
            "Crop bookmark JSON is missing.");
    }

    private bool CanEditAiMarker(ObservationDisplayItem item) =>
        _currentJob != null && SmartContextStore.LoadAiMarker(_currentJob, item.Observation.Id) != null;

    private void EditAiMarker(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return;

        SmartAiMarker? marker = SmartContextStore.LoadAiMarker(_currentJob, item.Observation.Id);
        if (marker == null)
        {
            TxtStatus.Text = "AI marker JSON is missing.";
            LoadObservationsInbox();
            RefreshAiMarkersOverlay();
            return;
        }

        AiMarkerInput? input = ShowAiMarkerDialog(marker);
        if (input == null)
            return;

        marker.Type = NormalizeAiMarkerType(input.MarkerType);
        marker.SampleKind = NormalizeAiMarkerSampleKind(input.SampleKind);
        marker.Value = input.Value.Trim();
        marker.Note = input.Note.Trim();

        SmartContextStore.SaveAiMarker(_currentJob, marker);
        RefreshAiMarkersOverlay();
        LoadObservationsInbox();
        TxtStatus.Text = $"Updated AI marker {marker.Type}.";
    }

    private void DeleteAiMarker(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return;

        SmartAiMarker? marker = SmartContextStore.LoadAiMarker(_currentJob, item.Observation.Id);
        if (marker == null)
        {
            TxtStatus.Text = "AI marker JSON is already missing.";
            LoadObservationsInbox();
            RefreshAiMarkersOverlay();
            return;
        }

        MessageBoxResult answer = MessageBox.Show(
            $"Delete marker '{marker.Type}' from the active marker set?\n\nCrop evidence and the observation log stay in AI_Context.",
            "Delete AI Marker",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        SmartContextStore.DeleteAiMarker(_currentJob, marker.Id);
        RefreshAiMarkersOverlay();
        LoadObservationsInbox();
        TxtStatus.Text = $"Deleted AI marker {marker.Type}; evidence files were kept.";
    }

    private bool CanHideAiMarkerType(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return false;

        SmartAiMarker? marker = SmartContextStore.LoadAiMarker(_currentJob, item.Observation.Id);
        return marker != null && !_hiddenAiMarkerTypes.Contains(marker.Type);
    }

    private void HideAiMarkerType(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return;

        SmartAiMarker? marker = SmartContextStore.LoadAiMarker(_currentJob, item.Observation.Id);
        if (marker == null)
        {
            TxtStatus.Text = "AI marker JSON is missing.";
            return;
        }

        _hiddenAiMarkerTypes.Add(marker.Type);
        SavePersistedMarkerVisibility();
        RefreshAiMarkersOverlay();
        TxtStatus.Text = $"Hidden AI marker type '{marker.Type}' from the canvas overlay and saved it for this job.";
    }

    private void ShowAllMarkerTypes()
    {
        _hiddenAiMarkerTypes.Clear();
        SavePersistedMarkerVisibility();
        RefreshAiMarkersOverlay();
        TxtStatus.Text = "Showing all AI marker types on the canvas overlay and saved it for this job.";
    }

    private void OpenAiMarkerFile(ObservationDisplayItem item)
    {
        string path = AiMarkerPath(item);
        if (!File.Exists(path))
        {
            TxtStatus.Text = "AI marker JSON is missing.";
            return;
        }

        OpenJsonFile(path, "AI marker JSON is missing.");
    }

    private bool CanFindSimilarFromMarker(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return false;

        SmartAiMarker? marker = SmartContextStore.LoadAiMarker(_currentJob, item.Observation.Id);
        return marker != null && AiContextFileExists(marker.CropPath);
    }

    private void QueueFindSimilarFromMarker(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return;

        SmartAiMarker? marker = SmartContextStore.LoadAiMarker(_currentJob, item.Observation.Id);
        if (marker == null)
        {
            TxtStatus.Text = "AI marker JSON is missing.";
            return;
        }

        if (string.IsNullOrWhiteSpace(marker.CropPath))
        {
            TxtStatus.Text = "AI marker has no crop for Find Similar.";
            return;
        }

        if (!AiContextFileExists(marker.CropPath))
        {
            TxtStatus.Text = "AI marker crop file is missing.";
            return;
        }

        PageInfo? page = ResolveMarkerPage(marker);
        string nearbyContextDetails = BuildFindSimilarNearbyContext(
            marker,
            page,
            out string nearbyCropPath,
            out SKRect nearbyCropRect);
        string prompt = BuildFindSimilarMarkerRequestPrompt(
            marker,
            item.Observation,
            nearbyCropPath,
            nearbyCropRect,
            nearbyContextDetails);
        string details =
            "Find Similar From Marker queued.\n\n" +
            "Source marker:\n" +
            $"- Id: {marker.Id}\n" +
            $"- Type: {marker.Type}\n" +
            $"- Sample kind: {marker.SampleKind}\n" +
            $"- AI crop: {marker.CropPath}\n\n" +
            nearbyContextDetails +
            "\n" +
            prompt;

        SmartObservation observation = SmartContextStore.AddObservation(
            _currentJob,
            page,
            "find_similar_marker_request",
            details);

        SmartContextStore.AddAiRequest(
            _currentJob,
            page,
            observation,
            "find_similar_marker_request",
            prompt,
            marker.CropPath,
            $"Source marker id: {marker.Id}",
            string.IsNullOrWhiteSpace(nearbyCropPath) ? [] : [nearbyCropPath]);

        LoadObservationsInbox();
        TxtStatus.Text = string.IsNullOrWhiteSpace(nearbyCropPath)
            ? $"Queued Find Similar From Marker for {marker.Type} ({marker.Id}); nearby crop was not available."
            : $"Queued Find Similar From Marker for {marker.Type} ({marker.Id}) with nearby sheet context.";
    }

    private PageInfo? ResolveMarkerPage(SmartAiMarker marker)
    {
        if (_currentJob == null)
            return null;

        if (!string.IsNullOrWhiteSpace(marker.PageFolder))
        {
            string folder = Path.IsPathFullyQualified(marker.PageFolder)
                ? marker.PageFolder
                : Path.GetFullPath(Path.Combine(_currentJob.RootPath, marker.PageFolder));
            PageInfo? page = SmartTakeoffsJobStore.TryReadPage(folder);
            if (page != null)
                return page;
        }

        return FindPageByName(marker.Page);
    }

    private bool AiContextFileExists(string path)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(path))
            return false;

        string fullPath = Path.IsPathFullyQualified(path)
            ? path
            : Path.GetFullPath(Path.Combine(_currentJob.AIContextRoot, path));
        return File.Exists(fullPath);
    }

    private string BuildFindSimilarNearbyContext(
        SmartAiMarker marker,
        PageInfo? page,
        out string nearbyCropPath,
        out SKRect nearbyCropRect)
    {
        nearbyCropPath = "";
        nearbyCropRect = SKRect.Empty;

        if (page == null)
            return "Nearby sheet context:\n- Unavailable: marker page could not be resolved.\n";

        SKRect requested = FindSimilarNearbyCropRect(marker);
        if (!TrySavePageCrop(
                page,
                requested,
                "find_similar_nearby",
                $"{marker.Type}_{marker.Id}",
                out nearbyCropPath,
                out nearbyCropRect,
                out string error))
        {
            return
                "Nearby sheet context:\n" +
                $"- Unavailable: {error}\n";
        }

        return
            "Nearby sheet context:\n" +
            $"- Nearby crop: {nearbyCropPath}\n" +
            $"- Nearby PDF crop: {FormatPdfRect(nearbyCropRect)}\n";
    }

    private static SKRect FindSimilarNearbyCropRect(SmartAiMarker marker)
    {
        float centerX = marker.PdfPoint.X;
        float centerY = marker.PdfPoint.Y;
        float width = FindSimilarNearbyContextMinSizePt;
        float height = FindSimilarNearbyContextMinSizePt;

        if (marker.PdfRect.Right > marker.PdfRect.Left && marker.PdfRect.Bottom > marker.PdfRect.Top)
        {
            centerX = (marker.PdfRect.Left + marker.PdfRect.Right) / 2f;
            centerY = (marker.PdfRect.Top + marker.PdfRect.Bottom) / 2f;
            width = Math.Max(
                marker.PdfRect.Right - marker.PdfRect.Left + FindSimilarNearbyContextPaddingPt * 2,
                FindSimilarNearbyContextMinSizePt);
            height = Math.Max(
                marker.PdfRect.Bottom - marker.PdfRect.Top + FindSimilarNearbyContextPaddingPt * 2,
                FindSimilarNearbyContextMinSizePt);
        }

        return SKRect.Create(centerX - width / 2f, centerY - height / 2f, width, height);
    }

    private static string BuildFindSimilarMarkerRequestPrompt(
        SmartAiMarker marker,
        SmartObservation sourceObservation,
        string nearbyCropPath,
        SKRect nearbyCropRect,
        string nearbyContextDetails)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Find similar plan conditions from this saved AI marker.");
        sb.AppendLine("Create reviewable AI action drafts only. Do not apply measurements automatically.");
        sb.AppendLine("Use the marker crop as the visual example and the nearby sheet crop as local context.");
        sb.AppendLine();
        sb.AppendLine("Source marker id:");
        sb.AppendLine(marker.Id);
        sb.AppendLine();
        sb.AppendLine("Marker summary:");
        sb.AppendLine($"- Page: {marker.Page}");
        sb.AppendLine($"- Type: {marker.Type}");
        sb.AppendLine($"- Sample kind: {marker.SampleKind}");
        sb.AppendLine($"- PDF point: {marker.PdfPoint.X:F1}, {marker.PdfPoint.Y:F1}");
        sb.AppendLine($"- PDF crop: left={marker.PdfRect.Left:F1}, top={marker.PdfRect.Top:F1}, right={marker.PdfRect.Right:F1}, bottom={marker.PdfRect.Bottom:F1}");
        sb.AppendLine($"- AI crop: {marker.CropPath}");
        if (!string.IsNullOrWhiteSpace(nearbyCropPath))
        {
            sb.AppendLine($"- Nearby sheet crop: {nearbyCropPath}");
            sb.AppendLine($"- Nearby sheet PDF crop: {FormatPdfRect(nearbyCropRect)}");
        }
        if (!string.IsNullOrWhiteSpace(marker.Value))
            sb.AppendLine($"- Value: {marker.Value}");
        if (!string.IsNullOrWhiteSpace(marker.Note))
            sb.AppendLine($"- Note: {marker.Note}");
        sb.AppendLine();
        sb.Append(nearbyContextDetails.TrimEnd());
        sb.AppendLine();
        sb.AppendLine("Source observation:");
        sb.AppendLine(sourceObservation.Text);
        return sb.ToString();
    }

    private void OpenJsonFile(string path, string missingStatus)
    {
        if (!File.Exists(path))
        {
            TxtStatus.Text = missingStatus;
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "notepad.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true,
        });
    }

    private bool CanOpenAiRequestFile(ObservationDisplayItem item) =>
        _currentJob != null && File.Exists(AiRequestPath(item));

    private bool CanAddManualAiResponse(ObservationDisplayItem item) =>
        _currentJob != null && SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id) != null;

    private bool CanRunAiRequest(ObservationDisplayItem item)
    {
        if (_currentJob == null || _isRunningAiRequest)
            return false;

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id);
        return request != null && IsRunnableAiStatus(request.Status);
    }

    private bool CanApplySheetMetadataResponse(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return false;

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id);
        if (request == null ||
            !string.Equals(request.Type, "pdf_sheet_metadata_fallback", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        SmartAiResponse? response = SmartContextStore.LoadAiResponse(_currentJob, request.Id);
        return response != null &&
               string.Equals(response.Status, "done", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(response.OutputText) &&
               TryResolveRequestPage(request, out _);
    }

    private void ApplySheetMetadataResponse(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return;

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id);
        if (request == null || !TryResolveRequestPage(request, out PageInfo? page) || page == null)
        {
            TxtStatus.Text = "Could not resolve the page for this sheet metadata response.";
            return;
        }

        SmartAiResponse? response = SmartContextStore.LoadAiResponse(_currentJob, request.Id);
        if (response == null)
        {
            TxtStatus.Text = "No AI response exists for this sheet metadata request.";
            return;
        }

        if (!PdfSheetMetadataService.TryBuildMetadataFromFallbackResponse(page, request, response, out PdfSheetMetadata metadata, out string error, _currentJob))
        {
            MessageBox.Show(error, "Apply Sheet Metadata Response", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SmartTakeoffsJobStore.WriteSourcePdfMetadata(page.FolderPath, metadata);
        var result = new PdfMetadataPageResult(page, true, metadata, "");
        var rows = BuildPdfMetadataPreviewRows([result], defaultRename: true, defaultScale: true).ToList();
        var dialog = new PdfMetadataPreviewDialog(rows, "Apply Sheet Metadata Response")
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        ApplyPdfMetadataResults(_currentJob, [result], dialog.Rows);
    }

    private bool TryResolveRequestPage(SmartAiRequest request, out PageInfo? page)
    {
        page = null;
        if (_currentJob == null)
            return false;

        if (!string.IsNullOrWhiteSpace(request.PageFolder))
        {
            string folder = Path.IsPathFullyQualified(request.PageFolder)
                ? request.PageFolder
                : Path.GetFullPath(Path.Combine(_currentJob.RootPath, request.PageFolder));
            page = SmartTakeoffsJobStore.TryReadPage(folder);
            if (page != null)
                return true;
        }

        page = FindPageByName(request.Page);
        return page != null;
    }

    private async Task RunSelectedOrNextAiRequestAsync()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before running AI.";
            return;
        }

        if (_isRunningAiRequest)
        {
            TxtStatus.Text = "AI request is already running.";
            return;
        }

        SmartAiRequest? request = null;
        if (SelectedObservationDisplayItem() is { } selected)
        {
            SmartAiRequest? selectedRequest = SmartContextStore.LoadAiRequest(_currentJob, selected.Observation.Id);
            if (selectedRequest != null && IsRunnableAiStatus(selectedRequest.Status))
                request = selectedRequest;
        }

        request ??= SmartContextStore.LoadAiRequests(_currentJob)
            .FirstOrDefault(candidate => IsRunnableAiStatus(candidate.Status));

        if (request == null)
        {
            TxtStatus.Text = "No pending AI request to run.";
            return;
        }

        await RunAiRequestAsync(request);
    }

    private async Task RunAiRequestAsync(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return;

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id);
        if (request == null)
        {
            TxtStatus.Text = "No AI request JSON exists for this Inbox entry.";
            return;
        }

        await RunAiRequestAsync(request);
    }

    private async Task RunAiRequestAsync(SmartAiRequest request)
    {
        if (_currentJob == null)
            return;

        if (_isRunningAiRequest)
        {
            TxtStatus.Text = "AI request is already running.";
            return;
        }

        string apiKey = ReadOpenAiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            TxtStatus.Text = "Set OPENAI_API_KEY in Windows environment, then run AI again.";
            return;
        }

        string model = AppSettingsStore.ResolveOpenAiModel(_settings);
        _isRunningAiRequest = true;
        try
        {
            request.Status = "running";
            SmartContextStore.SaveAiRequest(_currentJob, request);
            TxtStatus.Text = $"Running AI request {request.Id}...";
            LoadObservationsInbox();

            SmartAiRunResult result = await OpenAiRequestRunner.RunAsync(
                _currentJob,
                request,
                apiKey,
                model,
                CancellationToken.None);

            if (result.Success)
            {
                SmartAiResponse response = SmartContextStore.SaveAiResponse(
                    _currentJob,
                    request,
                    "done",
                    result.OutputText,
                    "",
                    "openai",
                    result.Model,
                    result.ProviderResponseId,
                    result.RawResponsePath);
                SmartContextStore.SaveAiActionDraftFromResponse(_currentJob, request, response);
                TxtStatus.Text = $"AI response saved for {request.Id}.";
            }
            else
            {
                SmartAiResponse response = SmartContextStore.SaveAiResponse(
                    _currentJob,
                    request,
                    "failed",
                    "",
                    result.Error,
                    "openai",
                    result.Model,
                    result.ProviderResponseId,
                    result.RawResponsePath);
                SmartContextStore.SaveAiActionDraftFromResponse(_currentJob, request, response);
                TxtStatus.Text = $"AI request failed: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            SmartAiResponse response = SmartContextStore.SaveAiResponse(
                _currentJob,
                request,
                "failed",
                "",
                ex.Message,
                "openai",
                model,
                "",
                "");
            SmartContextStore.SaveAiActionDraftFromResponse(_currentJob, request, response);
            TxtStatus.Text = $"AI request failed: {ex.Message}";
        }
        finally
        {
            _isRunningAiRequest = false;
            LoadObservationsInbox();
        }
    }

    private static string ReadOpenAiApiKey()
    {
        return AppSettingsStore.ReadOpenAiApiKey();
    }

    private static bool IsRunnableAiStatus(string status) =>
        string.IsNullOrWhiteSpace(status) ||
        status.Equals("pending", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("failed", StringComparison.OrdinalIgnoreCase);

    private bool CanOpenLayerManifest(ObservationDisplayItem item) =>
        TryGetLayerManifestPath(item, out _);

    private void OpenLayerManifest(ObservationDisplayItem item)
    {
        if (!TryGetLayerManifestPath(item, out string path))
        {
            TxtStatus.Text = "Layer JSON is missing for this Inbox entry.";
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "notepad.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true,
        });
    }

    private bool TryGetLayerManifestPath(ObservationDisplayItem item, out string path)
    {
        path = "";
        if (_currentJob == null)
            return false;

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id);
        if (request != null && !string.IsNullOrWhiteSpace(request.LayerManifestPath))
        {
            path = Path.IsPathFullyQualified(request.LayerManifestPath)
                ? request.LayerManifestPath
                : Path.GetFullPath(Path.Combine(_currentJob.RootPath, request.LayerManifestPath));
            return File.Exists(path);
        }

        PageInfo? page = FindPageByName(item.Page);
        if (page == null)
            return false;

        path = SmartTakeoffsJobStore.PageLayersJsonPath(page.FolderPath);
        return File.Exists(path);
    }

    private void OpenAiRequestFile(ObservationDisplayItem item)
    {
        string path = AiRequestPath(item);
        if (!File.Exists(path))
        {
            TxtStatus.Text = "AI request JSON is missing.";
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "notepad.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true,
        });
    }

    private bool CanOpenAiResponseFile(ObservationDisplayItem item) =>
        _currentJob != null && File.Exists(AiResponsePath(item));

    private bool CanOpenAiActionDraftFile(ObservationDisplayItem item) =>
        _currentJob != null && File.Exists(AiActionDraftPath(item));

    private bool CanPreviewAiActionDraft(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return false;

        SmartAiActionDraft? draft = SmartContextStore.LoadAiActionDraft(_currentJob, item.Observation.Id);
        return draft?.Actions.Any(action => action.Points.Count > 0) == true;
    }

    private bool CanApplyAiActionDraft(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return false;

        SmartAiActionDraft? draft = SmartContextStore.LoadAiActionDraft(_currentJob, item.Observation.Id);
        return draft != null &&
               !string.Equals(draft.Status, "applied", StringComparison.OrdinalIgnoreCase) &&
               draft.Actions.Any(action => ValidActionPointCount(action) > 0);
    }

    private bool CanReviewAiActionDraft(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return false;

        SmartAiActionDraft? draft = SmartContextStore.LoadAiActionDraft(_currentJob, item.Observation.Id);
        return draft != null &&
               !string.Equals(draft.Status, "applied", StringComparison.OrdinalIgnoreCase) &&
               draft.Actions.Count > 0;
    }

    private void OpenAiResponseFile(ObservationDisplayItem item)
    {
        string path = AiResponsePath(item);
        if (!File.Exists(path))
        {
            TxtStatus.Text = "AI response JSON is missing.";
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "notepad.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true,
        });
    }

    private void OpenAiActionDraftFile(ObservationDisplayItem item)
    {
        string path = AiActionDraftPath(item);
        if (!File.Exists(path))
        {
            TxtStatus.Text = "AI action draft JSON is missing.";
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "notepad.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true,
        });
    }

    private void PreviewAiActionDraft(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return;

        SmartAiActionDraft? draft = SmartContextStore.LoadAiActionDraft(_currentJob, item.Observation.Id);
        if (draft == null)
        {
            TxtStatus.Text = "AI action draft JSON is missing.";
            return;
        }

        int actionCount = draft.Actions.Count(action => action.Points.Count > 0);
        if (actionCount == 0)
        {
            TxtStatus.Text = "AI action draft has no preview points.";
            return;
        }

        string pageName = !string.IsNullOrWhiteSpace(draft.Page) ? draft.Page : item.Page;
        PageInfo? page = FindPageByName(pageName) ?? FindPageByName(item.Page);
        if (page != null)
        {
            pageName = page.Name;
            SelectPageByFolder(page.FolderPath);
        }

        string previewPageName = pageName;
        Dispatcher.InvokeAsync(() => _viewport.ShowAiActionDraftPreview(draft, previewPageName));
        TxtStatus.Text = $"Previewing {actionCount} AI action draft(s) on {previewPageName}.";
    }

    private void ClearAiActionDraftPreview()
    {
        _viewport.ClearAiActionDraftPreview();
        TxtStatus.Text = "AI action preview cleared.";
    }

    private void ApplyAiActionDraft(ObservationDisplayItem item)
    {
        ReviewAiActionDraft(item);
    }

    private void ReviewAiActionDraft(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return;

        SmartAiActionDraft? draft = SmartContextStore.LoadAiActionDraft(_currentJob, item.Observation.Id);
        if (draft == null)
        {
            TxtStatus.Text = "AI action draft JSON is missing.";
            return;
        }

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id);
        bool isRoofRecognition = IsRoofRecognitionRequest(request);
        var targets = isRoofRecognition
            ? BuildRoofRecognitionTargetOptions()
            : BuildAiActionTargetOptions();
        var rows = BuildAiActionReviewRows(draft, item, targets);
        if (rows.Count == 0)
        {
            TxtStatus.Text = "AI action draft has no actions to review.";
            return;
        }

        var dialog = new AiActionReviewDialog(
            rows,
            targets,
            indices => PreviewAiActionDraftActions(draft, item, indices),
            isRoofRecognition ? "Review Auto Roof Candidates" : "Review Action Draft",
            isRoofRecognition ? "Roof Marker" : "Target Takeoff",
            isRoofRecognition ? "Create Markers" : "Apply Accepted",
            isRoofRecognition
                ? "Select at least one valid roof marker candidate before creating markers."
                : "Select at least one valid action with a target takeoff item before applying.")
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
            return;

        var acceptedRows = dialog.AcceptedRows.ToList();
        draft.AcceptedActionIndices = dialog.AcceptedIndices.ToList();
        draft.RejectedActionIndices = dialog.RejectedIndices.ToList();
        draft.ReviewedAtUtc = DateTime.UtcNow.ToString("O");
        draft.Status = acceptedRows.Count > 0 ? "reviewed" : "reviewed_no_actions";
        SmartContextStore.SaveAiActionDraft(_currentJob, draft);
        RecordMarkerCandidateFeedback(item, draft, rows);

        if (acceptedRows.Count == 0)
        {
            LoadObservationsInbox();
            TxtStatus.Text = "Saved AI action draft review; no valid accepted actions were applied.";
            return;
        }

        if (isRoofRecognition)
            ApplyRoofRecognitionMarkerDraft(item, draft, acceptedRows);
        else
            ApplyReviewedAiActionDraft(item, draft, acceptedRows);
    }

    private static bool IsRoofRecognitionRequest(SmartAiRequest? request) =>
        request != null &&
        string.Equals(request.Type, "roof_recognition_request", StringComparison.OrdinalIgnoreCase);

    private void RecordMarkerCandidateFeedback(
        ObservationDisplayItem item,
        SmartAiActionDraft draft,
        IReadOnlyList<AiActionReviewRow> rows)
    {
        if (_currentJob == null || rows.Count == 0)
            return;

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id);
        if (request == null ||
            !string.Equals(request.Type, "find_similar_marker_request", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string sourceMarkerId = ExtractSourceMarkerId(request.MeasurementSummary);
        if (string.IsNullOrWhiteSpace(sourceMarkerId))
            sourceMarkerId = ExtractSourceMarkerId(request.Prompt);
        SmartAiMarker? sourceMarker = string.IsNullOrWhiteSpace(sourceMarkerId)
            ? null
            : SmartContextStore.LoadAiMarker(_currentJob, sourceMarkerId);

        foreach (AiActionReviewRow row in rows)
        {
            SmartAiAction action = row.Action;
            AiActionTargetOption? target = row.Target;
            bool accepted = row.Accepted;
            SmartLearningStore.AppendMarkerFeedback(
                _currentJob,
                new SmartMarkerFeedbackRecord
                {
                    RequestId = request.Id,
                    ResponseId = draft.ResponseId,
                    DraftId = draft.Id,
                    SourceMarkerId = sourceMarkerId,
                    SourceMarkerType = sourceMarker?.Type ?? "",
                    SourceMarkerSampleKind = sourceMarker?.SampleKind ?? "",
                    Outcome = accepted ? "accepted" : "rejected",
                    Applied = accepted && row.CanApply && target != null,
                    ActionIndex = row.Index,
                    ActionType = action.Type.Trim(),
                    Label = string.IsNullOrWhiteSpace(action.Label) ? row.Label : action.Label.Trim(),
                    Page = row.Page,
                    MeasurementType = row.MeasurementType,
                    Confidence = action.Confidence,
                    Points = action.Points
                        .Select(point => new SmartAiActionPoint { X = point.X, Y = point.Y })
                        .ToList(),
                    Notes = action.Notes.Trim(),
                    TargetId = target?.Id ?? "",
                    TargetName = target?.Name ?? "",
                    TargetMeasurementType = target?.MeasurementType ?? "",
                    TargetCreatesNewItem = target?.CreatesNewItem == true,
                });
        }
    }

    private static string ExtractSourceMarkerId(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        string[] lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();
            const string label = "Source marker id:";
            if (trimmed.Equals(label, StringComparison.OrdinalIgnoreCase))
                return i + 1 < lines.Length ? lines[i + 1].Trim() : "";

            if (trimmed.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                return trimmed[label.Length..].Trim();
        }

        return "";
    }

    private IReadOnlyList<AiActionReviewRow> BuildAiActionReviewRows(
        SmartAiActionDraft draft,
        ObservationDisplayItem item,
        IReadOnlyList<AiActionTargetOption> targets)
    {
        var rows = new List<AiActionReviewRow>();
        for (int i = 0; i < draft.Actions.Count; i++)
        {
            SmartAiAction action = draft.Actions[i];
            string measurementType = NormalizeAiActionMeasurementType(action);
            PageInfo? page = ResolveAiActionPage(action, draft, item);
            List<SKPoint> points = ActionPoints(action);
            bool hasPage = page != null;
            bool hasGeometry = HasValidMeasurementGeometry(measurementType, points);
            string status = hasPage && hasGeometry
                ? "Ready"
                : !hasPage
                    ? "No valid page"
                    : AiActionGeometryStatus(measurementType, points.Count);
            rows.Add(AiActionReviewRow.FromAction(
                i,
                action,
                AiActionPageLabel(page, action, draft, item),
                hasPage && hasGeometry,
                status,
                DefaultAiActionTarget(measurementType, targets)));
        }

        return rows;
    }

    private IReadOnlyList<AiActionTargetOption> BuildAiActionTargetOptions()
    {
        var options = new List<AiActionTargetOption>();
        foreach (string measurementType in new[] { "line", "area", "point" })
        {
            options.Add(new AiActionTargetOption
            {
                Id = $"new:{measurementType}",
                Name = $"New AI {MeasurementTypeTitle(measurementType)} Item",
                MeasurementType = measurementType,
                CreatesNewItem = true,
            });
        }

        options.AddRange(_takeoffItems
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item =>
            {
                string measurementType = SmartTakeoffsJobStore.NormalizeMeasurementType(item.MeasurementType);
                return new AiActionTargetOption
                {
                    Id = item.Id,
                    Name = $"{item.Name} ({MeasurementTypeTitle(measurementType)})",
                    MeasurementType = measurementType,
                    Item = item,
                };
            }));

        return options;
    }

    private static IReadOnlyList<AiActionTargetOption> BuildRoofRecognitionTargetOptions() =>
    [
        new AiActionTargetOption
        {
            Id = "roof-marker:line",
            Name = "Create Line Roof Marker",
            MeasurementType = "line",
            CreatesNewItem = true,
        },
        new AiActionTargetOption
        {
            Id = "roof-marker:point",
            Name = "Create Point Roof Marker",
            MeasurementType = "point",
            CreatesNewItem = true,
        },
    ];

    private AiActionTargetOption? DefaultAiActionTarget(
        string measurementType,
        IReadOnlyList<AiActionTargetOption> targets)
    {
        if (_activeItem != null)
        {
            string activeType = SmartTakeoffsJobStore.NormalizeMeasurementType(_activeItem.MeasurementType);
            if (string.Equals(activeType, measurementType, StringComparison.OrdinalIgnoreCase))
            {
                AiActionTargetOption? active = targets.FirstOrDefault(target =>
                    target.Item != null && ReferenceEquals(target.Item, _activeItem));
                if (active != null)
                    return active;
            }
        }

        return targets.FirstOrDefault(target =>
                   target.Item != null &&
                   string.Equals(target.MeasurementType, measurementType, StringComparison.OrdinalIgnoreCase)) ??
               targets.FirstOrDefault(target =>
                   target.CreatesNewItem &&
                   string.Equals(target.MeasurementType, measurementType, StringComparison.OrdinalIgnoreCase));
    }

    private static string AiActionGeometryStatus(string measurementType, int pointCount) =>
        measurementType switch
        {
            "point" => "Needs at least 1 point",
            "area" => $"Needs 3+ points; has {pointCount}",
            _ => $"Needs 2+ points; has {pointCount}",
        };

    private static string AiActionPageLabel(
        PageInfo? page,
        SmartAiAction action,
        SmartAiActionDraft draft,
        ObservationDisplayItem item)
    {
        if (page != null)
            return page.Name;
        if (!string.IsNullOrWhiteSpace(action.Page))
            return action.Page;
        if (!string.IsNullOrWhiteSpace(draft.Page))
            return draft.Page;
        if (!string.IsNullOrWhiteSpace(item.Page))
            return item.Page;
        return "(missing)";
    }

    private void PreviewAiActionDraftActions(
        SmartAiActionDraft source,
        ObservationDisplayItem item,
        IReadOnlyList<int> indices)
    {
        if (indices.Count == 0)
        {
            TxtStatus.Text = "Select accepted AI actions before previewing.";
            return;
        }

        var actions = indices
            .Where(index => index >= 0 && index < source.Actions.Count)
            .Select(index => source.Actions[index])
            .Where(action => action.Points.Count > 0)
            .ToList();
        if (actions.Count == 0)
        {
            TxtStatus.Text = "Selected AI actions have no preview points.";
            return;
        }

        PageInfo? page = actions
            .Select(action => ResolveAiActionPage(action, source, item))
            .FirstOrDefault(candidate => candidate != null);
        string pageName = page?.Name ?? AiActionPageLabel(null, actions[0], source, item);
        if (page != null)
            SelectPageByFolder(page.FolderPath);

        var previewDraft = new SmartAiActionDraft
        {
            Id = source.Id,
            RequestId = source.RequestId,
            ResponseId = source.ResponseId,
            ProjectId = source.ProjectId,
            Page = source.Page,
            Status = source.Status,
            Summary = source.Summary,
            RawText = source.RawText,
            Actions = actions,
            CreatedAtUtc = source.CreatedAtUtc,
            UpdatedAtUtc = source.UpdatedAtUtc,
        };

        Dispatcher.InvokeAsync(() => _viewport.ShowAiActionDraftPreview(previewDraft, pageName));
        TxtStatus.Text = $"Previewing {actions.Count} selected AI action(s) on {pageName}.";
    }

    private void ApplyReviewedAiActionDraft(
        ObservationDisplayItem item,
        SmartAiActionDraft draft,
        IReadOnlyList<AiActionReviewRow> acceptedRows)
    {
        if (_currentJob == null)
            return;

        var createdByType = new Dictionary<string, TakeoffItem>(StringComparer.OrdinalIgnoreCase);
        var appliedIds = new List<string>();
        var appliedIndices = new List<int>();
        var touchedItems = new HashSet<TakeoffItem>();
        PageInfo? lastPage = null;

        try
        {
            foreach (AiActionReviewRow row in acceptedRows)
            {
                SmartAiAction action = row.Action;
                PageInfo? page = ResolveAiActionPage(action, draft, item);
                if (page == null)
                    continue;

                string measurementType = NormalizeAiActionMeasurementType(action);
                List<SKPoint> points = ActionPoints(action);
                if (!HasValidMeasurementGeometry(measurementType, points))
                    continue;

                TakeoffItem? target = ResolveReviewedAiActionTarget(row, action, measurementType, createdByType);
                if (target == null)
                    continue;

                EnsureTakeoffItemFolder(target);

                var measurement = new Measurement
                {
                    Name = AiActionMeasurementName(action, target),
                    Notes = AiActionMeasurementNotes(action, draft),
                    MType = measurementType,
                    Points = points,
                    Color = target.Color,
                    PageFolder = page.FolderPath,
                    TakeoffFolder = target.FolderPath,
                    ScaleMetersPerPt = page.ScaleMetersPerPt > 0 ? page.ScaleMetersPerPt : _viewport.ScaleMetersPerPt,
                };

                target.Measurements.Add(measurement);
                appliedIds.Add(measurement.Id);
                appliedIndices.Add(row.Index);
                touchedItems.Add(target);
                lastPage = page;
            }

            if (appliedIds.Count == 0)
            {
                draft.Status = "reviewed_no_actions";
                SmartContextStore.SaveAiActionDraft(_currentJob, draft);
                TxtStatus.Text = "AI action draft review saved, but no accepted action matched a valid page, target, and geometry.";
                return;
            }

            draft.Status = "applied";
            draft.AppliedMeasurementIds = draft.AppliedMeasurementIds
                .Concat(appliedIds)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            draft.AppliedActionIndices = draft.AppliedActionIndices
                .Concat(appliedIndices)
                .Distinct()
                .OrderBy(index => index)
                .ToList();
            SmartContextStore.SaveAiActionDraft(_currentJob, draft);

            foreach (TakeoffItem target in touchedItems)
            {
                SmartTakeoffsJobStore.SaveTakeoffItem(target);
                RefreshTreeItem(target);
            }

            _viewport.ClearAiActionDraftPreview();
            _viewport.SetMeasurements(_takeoffItems.SelectMany(takeoff => takeoff.Measurements));
            if (lastPage != null)
                SelectPageByFolder(lastPage.FolderPath);
            if (touchedItems.LastOrDefault() is { } lastItem)
                SelectTakeoffItem(lastItem);

            RefreshEstimateTable();
            RefreshPagesTakeoffIndicators();
            ApplyTakeoffPageHighlights();
            UpdateTotalDisplay();
            LoadObservationsInbox();
            TxtStatus.Text = $"Applied {appliedIds.Count} AI drafted measurement(s).";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot apply AI action draft:\n{ex.Message}", "Apply AI Action Draft",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyRoofRecognitionMarkerDraft(
        ObservationDisplayItem item,
        SmartAiActionDraft draft,
        IReadOnlyList<AiActionReviewRow> acceptedRows)
    {
        if (_currentJob == null)
            return;

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id);
        var existingMarkers = SmartContextStore.LoadAiMarkers(_currentJob).ToList();
        var createdIds = new List<string>();
        var appliedIndices = new List<int>();
        PageInfo? lastPage = null;
        int skippedDuplicates = 0;

        try
        {
            foreach (AiActionReviewRow row in acceptedRows)
            {
                SmartAiAction action = row.Action;
                PageInfo? page = ResolveAiActionPage(action, draft, item);
                if (page == null)
                    continue;

                string measurementType = NormalizeAiActionMeasurementType(action);
                List<SKPoint> points = ActionPoints(action);
                if (!HasValidMeasurementGeometry(measurementType, points))
                    continue;

                string markerType = ResolveRoofRecognitionMarkerType(action);
                List<SKPoint> markerPoints = RoofRecognitionMarkerPoints(markerType, measurementType, points);
                if (markerPoints.Count == 0)
                    continue;

                string cropPath = request?.CropPath ?? "";
                SKRect cropRect = CandidateCropRect(points);
                if (TrySavePageCrop(
                        page,
                        cropRect,
                        "roof_marker_candidate",
                        $"{markerType}_{row.Index + 1}",
                        out string savedCropPath,
                        out SKRect savedCropRect,
                        out _))
                {
                    cropPath = savedCropPath;
                    cropRect = savedCropRect;
                }

                int markerPointIndex = 0;
                foreach (SKPoint point in markerPoints)
                {
                    markerPointIndex++;
                    if (HasNearbyRoofMarkerDuplicate(existingMarkers, page, markerType, point))
                    {
                        skippedDuplicates++;
                        continue;
                    }

                    string value = RoofRecognitionMarkerValue(action, markerType);
                    string note = RoofRecognitionMarkerNote(action, draft, row, markerPointIndex, markerPoints.Count);
                    string details = BuildRoofRecognitionMarkerObservationDetails(
                        request,
                        action,
                        markerType,
                        value,
                        note,
                        cropPath,
                        cropRect,
                        point);

                    SmartObservation observation = SmartContextStore.AddObservation(
                        _currentJob,
                        page,
                        "ai_marker",
                        details);

                    SmartAiMarker marker = SmartContextStore.SaveAiMarker(
                        _currentJob,
                        page,
                        observation,
                        markerType,
                        "positive",
                        value,
                        note,
                        cropPath,
                        point.X,
                        point.Y,
                        cropRect.Left,
                        cropRect.Top,
                        cropRect.Right,
                        cropRect.Bottom);

                    existingMarkers.Add(marker);
                    createdIds.Add(marker.Id);
                    appliedIndices.Add(row.Index);
                    lastPage = page;
                }
            }

            if (createdIds.Count == 0)
            {
                draft.Status = "reviewed_no_actions";
                SmartContextStore.SaveAiActionDraft(_currentJob, draft);
                LoadObservationsInbox();
                TxtStatus.Text = skippedDuplicates > 0
                    ? $"Auto Roof review saved; {skippedDuplicates} duplicate marker candidate(s) were skipped."
                    : "Auto Roof review saved, but no accepted candidate could be converted to a marker.";
                return;
            }

            draft.Status = "applied";
            draft.AppliedActionIndices = draft.AppliedActionIndices
                .Concat(appliedIndices)
                .Distinct()
                .OrderBy(index => index)
                .ToList();
            SmartContextStore.SaveAiActionDraft(_currentJob, draft);

            _viewport.ClearAiActionDraftPreview();
            if (lastPage != null)
                SelectPageByFolder(lastPage.FolderPath);
            RefreshAiMarkersOverlay();
            RefreshMassingDraftPanel();
            LoadObservationsInbox();
            TxtStatus.Text = skippedDuplicates > 0
                ? $"Created {createdIds.Count} reviewed roof marker(s); skipped {skippedDuplicates} duplicate(s). Run Build 3D Draft to update roof guides."
                : $"Created {createdIds.Count} reviewed roof marker(s). Run Build 3D Draft to update roof guides.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Cannot create Auto Roof markers:\n{ex.Message}",
                "Auto Roof",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private string BuildRoofRecognitionMarkerObservationDetails(
        SmartAiRequest? request,
        SmartAiAction action,
        string markerType,
        string value,
        string note,
        string cropPath,
        SKRect cropRect,
        SKPoint point)
    {
        var sb = new StringBuilder();
        sb.AppendLine("AI marker saved from Auto Roof review.");
        sb.AppendLine();
        sb.AppendLine("Marker:");
        sb.AppendLine($"- Type: {markerType}");
        sb.AppendLine("- Sample kind: positive");
        if (!string.IsNullOrWhiteSpace(value))
            sb.AppendLine($"- Value: {value}");
        if (!string.IsNullOrWhiteSpace(note))
            sb.AppendLine($"- Note: {note}");
        sb.AppendLine();
        sb.AppendLine("Source action:");
        if (request != null)
            sb.AppendLine($"- Request: {request.Id}");
        sb.AppendLine($"- Action type: {action.Type}");
        sb.AppendLine($"- Label: {action.Label}");
        if (action.Confidence > 0)
            sb.AppendLine($"- Confidence: {action.Confidence:P0}");
        if (!string.IsNullOrWhiteSpace(action.Notes))
            sb.AppendLine($"- AI notes: {action.Notes.Trim()}");
        sb.AppendLine();
        sb.AppendLine("Context:");
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "- PDF point: {0:F1}, {1:F1}", point.X, point.Y));
        sb.AppendLine($"- AI crop: {cropPath}");
        sb.AppendLine($"- PDF crop: {FormatPdfRect(cropRect)}");
        return sb.ToString();
    }

    private bool HasNearbyRoofMarkerDuplicate(
        IReadOnlyList<SmartAiMarker> existingMarkers,
        PageInfo page,
        string markerType,
        SKPoint point)
    {
        if (_currentJob == null)
            return false;

        foreach (SmartAiMarker marker in existingMarkers)
        {
            if (!AiMarkerTypeEquals(marker, markerType) ||
                !MarkerBelongsToPage(marker, page, _currentJob))
            {
                continue;
            }

            float dx = marker.PdfPoint.X - point.X;
            float dy = marker.PdfPoint.Y - point.Y;
            if ((dx * dx) + (dy * dy) <= RoofRecognitionMarkerDuplicateTolerancePt * RoofRecognitionMarkerDuplicateTolerancePt)
                return true;
        }

        return false;
    }

    private static List<SKPoint> RoofRecognitionMarkerPoints(
        string markerType,
        string measurementType,
        IReadOnlyList<SKPoint> points)
    {
        if (points.Count == 0)
            return [];

        if (string.Equals(markerType, "roof_note", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(measurementType, "point", StringComparison.OrdinalIgnoreCase))
        {
            return [points[0]];
        }

        return DistinctRoofRecognitionPoints(points)
            .Take(8)
            .ToList();
    }

    private static IEnumerable<SKPoint> DistinctRoofRecognitionPoints(IReadOnlyList<SKPoint> points)
    {
        var unique = new List<SKPoint>();
        foreach (SKPoint point in points)
        {
            if (unique.Any(existing =>
                    Math.Abs(existing.X - point.X) < 1f &&
                    Math.Abs(existing.Y - point.Y) < 1f))
            {
                continue;
            }

            unique.Add(point);
            yield return point;
        }
    }

    private static string ResolveRoofRecognitionMarkerType(SmartAiAction action)
    {
        string exact = action.Type.Trim();
        if (RoofRecognitionMarkerTypes.Any(type => string.Equals(type, exact, StringComparison.OrdinalIgnoreCase)))
            return RoofRecognitionMarkerTypes.First(type => string.Equals(type, exact, StringComparison.OrdinalIgnoreCase));

        string text = $"{action.Type} {action.Label} {action.Notes}".ToLowerInvariant();
        foreach (string markerType in RoofRecognitionMarkerTypes)
        {
            if (text.Contains(markerType, StringComparison.OrdinalIgnoreCase))
                return markerType;
        }

        if (text.Contains("valley", StringComparison.OrdinalIgnoreCase))
            return "valley_sample";
        if (text.Contains("high", StringComparison.OrdinalIgnoreCase) && text.Contains("edge", StringComparison.OrdinalIgnoreCase))
            return "roof_high_edge";
        if (text.Contains("low", StringComparison.OrdinalIgnoreCase) && text.Contains("edge", StringComparison.OrdinalIgnoreCase))
            return "roof_low_edge";
        if (text.Contains("overhang", StringComparison.OrdinalIgnoreCase) || text.Contains("eave", StringComparison.OrdinalIgnoreCase))
            return "overhang_sample";
        if (text.Contains("ridge", StringComparison.OrdinalIgnoreCase))
            return "ridge_sample";
        if (text.Contains("edge", StringComparison.OrdinalIgnoreCase))
            return "roof_edge_sample";
        return "roof_note";
    }

    private static string RoofRecognitionMarkerValue(SmartAiAction action, string markerType)
    {
        string label = action.Label.Trim();
        if (!string.IsNullOrWhiteSpace(label) &&
            !string.Equals(label, markerType, StringComparison.OrdinalIgnoreCase))
        {
            return label;
        }

        return "";
    }

    private static string RoofRecognitionMarkerNote(
        SmartAiAction action,
        SmartAiActionDraft draft,
        AiActionReviewRow row,
        int markerPointIndex,
        int markerPointCount)
    {
        var lines = new List<string> { "Created from reviewed Auto Roof candidate." };
        if (!string.IsNullOrWhiteSpace(draft.RequestId))
            lines.Add($"Request: {draft.RequestId}");
        lines.Add($"Action index: {row.Index}");
        if (markerPointCount > 1)
            lines.Add($"Marker point: {markerPointIndex} of {markerPointCount}");
        if (action.Confidence > 0)
            lines.Add($"Confidence: {action.Confidence:P0}");
        if (!string.IsNullOrWhiteSpace(action.Notes))
            lines.Add(action.Notes.Trim());
        return string.Join(Environment.NewLine, lines);
    }

    private TakeoffItem? ResolveReviewedAiActionTarget(
        AiActionReviewRow row,
        SmartAiAction action,
        string measurementType,
        Dictionary<string, TakeoffItem> createdByType)
    {
        AiActionTargetOption? option = row.Target;
        if (option == null ||
            !string.Equals(option.MeasurementType, measurementType, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (option.Item != null)
        {
            string targetType = SmartTakeoffsJobStore.NormalizeMeasurementType(option.Item.MeasurementType);
            return string.Equals(targetType, measurementType, StringComparison.OrdinalIgnoreCase)
                ? option.Item
                : null;
        }

        if (!option.CreatesNewItem)
            return null;

        if (createdByType.TryGetValue(measurementType, out TakeoffItem? existing))
            return existing;

        string name = AiActionTakeoffName(action, measurementType);
        var created = CreateUniqueTakeoffItem(name, "#00BCD4", measurementType, NewTakeoffItemParentFolder());
        _takeoffItems.Add(created);
        var parent = FindTakeoffTreeItemByFolder(Path.GetDirectoryName(created.FolderPath) ?? "") ?? (ItemsControl)TakeoffsTree;
        var tvi = AddTakeoffTreeItem(created, parent);
        if (parent is TreeViewItem parentTvi)
            parentTvi.IsExpanded = true;
        tvi.IsSelected = true;
        createdByType[measurementType] = created;
        return created;
    }

    private TakeoffItem ResolveAiActionTakeoffItem(
        SmartAiAction action,
        string measurementType,
        Dictionary<string, TakeoffItem> createdByType)
    {
        if (_activeItem != null &&
            string.Equals(
                SmartTakeoffsJobStore.NormalizeMeasurementType(_activeItem.MeasurementType),
                measurementType,
                StringComparison.OrdinalIgnoreCase))
        {
            return _activeItem;
        }

        if (createdByType.TryGetValue(measurementType, out TakeoffItem? existing))
            return existing;

        string name = AiActionTakeoffName(action, measurementType);
        var created = CreateUniqueTakeoffItem(name, "#00BCD4", measurementType, NewTakeoffItemParentFolder());
        _takeoffItems.Add(created);
        var parent = FindTakeoffTreeItemByFolder(Path.GetDirectoryName(created.FolderPath) ?? "") ?? (ItemsControl)TakeoffsTree;
        var tvi = AddTakeoffTreeItem(created, parent);
        if (parent is TreeViewItem parentTvi)
            parentTvi.IsExpanded = true;
        tvi.IsSelected = true;
        createdByType[measurementType] = created;
        return created;
    }

    private PageInfo? ResolveAiActionPage(SmartAiAction action, SmartAiActionDraft draft, ObservationDisplayItem item)
    {
        if (FindPageByName(action.Page) is { } actionPage)
            return actionPage;
        if (FindPageByName(draft.Page) is { } draftPage)
            return draftPage;
        if (FindPageByName(item.Page) is { } observationPage)
            return observationPage;
        return _currentPage;
    }

    private static string NormalizeAiActionMeasurementType(SmartAiAction action)
    {
        string value = action.MeasurementType;
        if (string.IsNullOrWhiteSpace(value))
        {
            value = action.Type.Contains("area", StringComparison.OrdinalIgnoreCase)
                ? "area"
                : action.Type.Contains("point", StringComparison.OrdinalIgnoreCase)
                    ? "point"
                    : "line";
        }

        return SmartTakeoffsJobStore.NormalizeMeasurementType(value.Trim().ToLowerInvariant());
    }

    private static List<SKPoint> ActionPoints(SmartAiAction action) =>
        action.Points
            .Select(point => new SKPoint(point.X, point.Y))
            .Where(point => !float.IsNaN(point.X) && !float.IsNaN(point.Y))
            .ToList();

    private static int ValidActionPointCount(SmartAiAction action) =>
        HasValidMeasurementGeometry(NormalizeAiActionMeasurementType(action), ActionPoints(action))
            ? action.Points.Count
            : 0;

    private static bool HasValidMeasurementGeometry(string measurementType, IReadOnlyList<SKPoint> points) =>
        measurementType switch
        {
            "point" => points.Count >= 1,
            "area" => points.Count >= 3,
            _ => points.Count >= 2,
        };

    private static string AiActionTakeoffName(SmartAiAction action, string measurementType)
    {
        string label = string.IsNullOrWhiteSpace(action.Label)
            ? MeasurementTypeTitle(measurementType)
            : action.Label.Trim();
        return $"AI {label}";
    }

    private static string AiActionMeasurementName(SmartAiAction action, TakeoffItem target)
    {
        if (!string.IsNullOrWhiteSpace(action.Label))
            return action.Label.Trim();
        return DefaultSectionName(target, new Measurement { PageFolder = "" }, target.Measurements.Count);
    }

    private static string AiActionMeasurementNotes(SmartAiAction action, SmartAiActionDraft draft)
    {
        var lines = new List<string> { "Created from AI action draft." };
        if (!string.IsNullOrWhiteSpace(draft.RequestId))
            lines.Add($"Request: {draft.RequestId}");
        if (!string.IsNullOrWhiteSpace(action.Type))
            lines.Add($"Action: {action.Type}");
        if (action.Confidence > 0)
            lines.Add($"Confidence: {action.Confidence:P0}");
        if (!string.IsNullOrWhiteSpace(action.Notes))
            lines.Add(action.Notes.Trim());
        return string.Join(Environment.NewLine, lines);
    }

    private void AddManualAiResponse(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return;

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id);
        if (request == null)
        {
            TxtStatus.Text = "No AI request JSON exists for this Inbox entry.";
            return;
        }

        SmartAiResponse? existing = SmartContextStore.LoadAiResponse(_currentJob, request.Id);
        string initial = existing?.OutputText ?? "";
        string? responseText = ShowMultilineInputDialog(
            $"AI response for {item.TypeShort}\nPage: {item.Page}\nRequest: {request.Id}",
            initial,
            "AI Response");
        if (string.IsNullOrWhiteSpace(responseText))
            return;

        SmartAiResponse response = SmartContextStore.SaveAiResponse(_currentJob, request, "done", responseText, "");
        SmartContextStore.SaveAiActionDraftFromResponse(_currentJob, request, response);
        TxtStatus.Text = $"Saved AI response for {request.Id}.";
        LoadObservationsInbox();
    }

    private string AiRequestPath(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return "";

        return Path.Combine(_currentJob.AIContextRoot, "requests", $"{item.Observation.Id}.json");
    }

    private string AiMarkerPath(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return "";

        return SmartContextStore.AiMarkerPath(_currentJob, item.Observation.Id);
    }

    private string AiResponsePath(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return "";

        return Path.Combine(_currentJob.AIContextRoot, "responses", $"{item.Observation.Id}.json");
    }

    private string AiActionDraftPath(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return "";

        return SmartContextStore.AiActionDraftPath(_currentJob, item.Observation.Id);
    }

    private PageInfo? FindPageByName(string pageName)
    {
        if (string.IsNullOrWhiteSpace(pageName))
            return null;

        foreach (TreeViewItem item in PagesTree.Items)
        {
            if (FindPageByName(item, pageName) is { } page)
                return page;
        }

        return null;
    }

    private static PageInfo? FindPageByName(TreeViewItem item, string pageName)
    {
        if (item.Tag is PageInfo page &&
            string.Equals(page.Name, pageName, StringComparison.OrdinalIgnoreCase))
        {
            return page;
        }

        foreach (TreeViewItem child in item.Items)
        {
            if (FindPageByName(child, pageName) is { } found)
                return found;
        }

        return null;
    }

    private void ShowObservationDetailsDialog(SmartObservation observation)
    {
        var display = new ObservationDisplayItem(observation);
        var win = new Window
        {
            Title = $"{display.TypeShort} - {observation.Id}",
            Width = 680,
            Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            Owner = this,
        };

        var panel = new DockPanel { Margin = new Thickness(10) };
        var header = new TextBlock
        {
            Text = $"{display.TypeShort} | Page: {(string.IsNullOrWhiteSpace(display.Page) ? "Unassigned" : display.Page)} | {display.TimeDisplay}",
            FontWeight = FontWeights.Normal,
            Margin = new Thickness(0, 0, 0, 8),
        };
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var goToPage = new Button { Content = "Go to Page", MinWidth = 90, Margin = new Thickness(0, 0, 6, 0), IsEnabled = CanGoToObservationPage(display) };
        var openCrop = new Button { Content = "Open Crop", MinWidth = 90, Margin = new Thickness(0, 0, 6, 0), IsEnabled = CanOpenObservationCrop(display) };
        var openRequest = new Button { Content = "Request JSON", MinWidth = 105, Margin = new Thickness(0, 0, 6, 0), IsEnabled = CanOpenAiRequestFile(display) };
        var runAi = new Button { Content = "Run AI", MinWidth = 82, Margin = new Thickness(0, 0, 6, 0), IsEnabled = CanRunAiRequest(display) };
        var addResponse = new Button { Content = "AI Response", MinWidth = 100, Margin = new Thickness(0, 0, 6, 0), IsEnabled = CanAddManualAiResponse(display) };
        var openContext = new Button { Content = "Project Context", MinWidth = 110, Margin = new Thickness(0, 0, 6, 0), IsEnabled = _currentJob != null };
        var close = new Button { Content = "Close", Width = 78, IsCancel = true };
        TextBox? detailsText = null;
        goToPage.Click += (_, _) => GoToObservationPage(display);
        openCrop.Click += (_, _) => OpenObservationCrop(display);
        openRequest.Click += (_, _) => OpenAiRequestFile(display);
        runAi.Click += async (_, _) =>
        {
            await RunAiRequestAsync(display);
            if (detailsText != null)
                detailsText.Text = ObservationDetailsText(observation);
        };
        addResponse.Click += (_, _) => AddManualAiResponse(display);
        openContext.Click += (_, _) => OpenProjectContextMarkdown();
        close.Click += (_, _) => win.Close();
        buttons.Children.Add(goToPage);
        buttons.Children.Add(openCrop);
        buttons.Children.Add(openRequest);
        buttons.Children.Add(runAi);
        buttons.Children.Add(addResponse);
        buttons.Children.Add(openContext);
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);

        var text = new TextBox
        {
            Text = ObservationDetailsText(observation),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        detailsText = text;
        panel.Children.Add(text);

        win.Content = panel;
        win.Loaded += (_, _) => text.Focus();
        win.ShowDialog();
    }

    private string ObservationDetailsText(SmartObservation observation)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Id: {observation.Id}");
        sb.AppendLine($"Type: {observation.Type}");
        sb.AppendLine($"Page: {observation.Page}");
        sb.AppendLine($"Created UTC: {observation.CreatedAtUtc}");

        if (_currentJob != null && SmartContextStore.LoadAiMarker(_currentJob, observation.Id) is { } marker)
        {
            sb.AppendLine();
            sb.AppendLine("AI Marker");
            sb.AppendLine($"Marker JSON: {SmartContextStore.AiMarkerPath(_currentJob, marker.Id)}");
            sb.AppendLine($"Type: {marker.Type}");
            sb.AppendLine($"Sample kind: {marker.SampleKind}");
            sb.AppendLine($"PDF point: {marker.PdfPoint.X:F1}, {marker.PdfPoint.Y:F1}");
            if (!string.IsNullOrWhiteSpace(marker.Value))
                sb.AppendLine($"Value: {marker.Value}");
            if (!string.IsNullOrWhiteSpace(marker.Note))
                sb.AppendLine($"Note: {marker.Note}");
            if (marker.LayerCount > 0)
                sb.AppendLine($"Layers: {marker.LayerCount}");
        }

        if (_currentJob != null && SmartContextStore.LoadAiRequest(_currentJob, observation.Id) is { } request)
        {
            sb.AppendLine();
            sb.AppendLine("AI Request");
            sb.AppendLine($"Status: {request.Status}");
            sb.AppendLine($"Request JSON: {Path.Combine(_currentJob.AIContextRoot, "requests", $"{request.Id}.json")}");
            if (!string.IsNullOrWhiteSpace(request.CropPath))
                sb.AppendLine($"Crop: {request.CropPath}");
            if (request.LayerCount > 0 || !string.IsNullOrWhiteSpace(request.LayerManifestPath))
            {
                string layerSummary = request.LayerCount == 1 ? "1 layer" : $"{request.LayerCount} layers";
                if (!string.IsNullOrWhiteSpace(request.LayerManifestPath))
                    sb.AppendLine($"Layers: {layerSummary} ({request.LayerManifestPath})");
                else
                    sb.AppendLine($"Layers: {layerSummary}");
            }

            if (SmartContextStore.LoadAiResponse(_currentJob, request.Id) is { } response)
            {
                sb.AppendLine();
                sb.AppendLine("AI Response");
                sb.AppendLine($"Status: {response.Status}");
                if (!string.IsNullOrWhiteSpace(response.OutputText))
                    sb.AppendLine(response.OutputText);
                if (!string.IsNullOrWhiteSpace(response.Error))
                    sb.AppendLine(response.Error);
                if (!string.IsNullOrWhiteSpace(response.Model))
                    sb.AppendLine($"Model: {response.Model}");
                if (!string.IsNullOrWhiteSpace(response.RawResponsePath))
                    sb.AppendLine($"Raw response: {response.RawResponsePath}");
            }

            if (SmartContextStore.LoadAiActionDraft(_currentJob, request.Id) is { } draft)
            {
                sb.AppendLine();
                sb.AppendLine("AI Action Draft");
                sb.AppendLine($"Status: {draft.Status}");
                sb.AppendLine($"Actions: {draft.Actions.Count}");
                sb.AppendLine($"Draft JSON: {SmartContextStore.AiActionDraftPath(_currentJob, request.Id)}");
                if (!string.IsNullOrWhiteSpace(draft.Summary))
                    sb.AppendLine($"Summary: {draft.Summary}");
            }
        }

        sb.AppendLine();
        sb.AppendLine(observation.Text);
        return sb.ToString();
    }

    private sealed class ObservationDisplayItem
    {
        public string TypeShort   { get; }
        public string BadgeColor  { get; }
        public string Page        { get; }
        public string TextPreview { get; }
        public string QualityPreview { get; }
        public string TimeDisplay { get; }
        public string CropRelativePath { get; }
        public SmartObservation Observation { get; }

        public ObservationDisplayItem(
            SmartObservation obs,
            string statusPrefix = "",
            SmartAiMarker? marker = null,
            string markerQuality = "")
        {
            Observation = obs;
            Page = obs.Page;
            CropRelativePath = ExtractAiCropRelativePath(obs.Text);

            string raw = marker != null
                ? FormatMarkerPreview(marker)
                : (obs.Text ?? "").Replace('\r', ' ').Replace('\n', ' ');
            if (!string.IsNullOrWhiteSpace(markerQuality))
                raw = $"{raw} | {markerQuality.Trim()}";
            if (!string.IsNullOrWhiteSpace(statusPrefix))
                raw = statusPrefix + raw;
            QualityPreview = markerQuality;
            TextPreview = raw.Length > 120 ? raw[..117] + "…" : raw;

            (TypeShort, BadgeColor) = obs.Type switch
            {
                "ai_request"                    => ("Ask AI",      "#1565C0"),
                "text_read_request"             => ("Read Text",   "#00796B"),
                "pending_check"                 => ("Check",       "#E65100"),
                "trace_request"                 => ("SmartTrace",  "#2E7D32"),
                "trace_area_request"            => ("Trace Area",  "#2E7D32"),
                "missed_takeoff_check"          => ("Missed",      "#C62828"),
                "crop_context"                  => ("Crop",        "#4E342E"),
                "ai_marker"                     => ("Marker",      "#00897B"),
                "takeoff_suggestion"            => ("Suggestion",  "#6A1B9A"),
                "measurement_explain_request"   => ("Explain",     "#1565C0"),
                "find_similar_request"          => ("Find Similar","#00796B"),
                "find_similar_marker_request"   => ("Find Marker", "#00796B"),
                "roof_recognition_request"      => ("Auto Roof",   "#455A64"),
                "pdf_layer_context"             => ("PDF Layer",   "#37474F"),
                "pdf_layer_ai_request"          => ("Layer AI",    "#37474F"),
                "measurement_link_request"      => ("Link",        "#546E7A"),
                "crop_bookmark_request"         => ("Bookmark",    "#00695C"),
                "pdf_sheet_metadata_fallback"   => ("Sheet Meta",  "#5D4037"),
                "measurement_note"              => ("Note",        "#546E7A"),
                _                               => ("Manual",      "#546E7A"),
            };

            if (DateTime.TryParse(obs.CreatedAtUtc, null,
                    DateTimeStyles.RoundtripKind, out DateTime dt))
                TimeDisplay = dt.ToLocalTime().ToString("MM/dd HH:mm");
            else
                TimeDisplay = obs.CreatedAtUtc.Length > 16
                    ? obs.CreatedAtUtc[..16] : obs.CreatedAtUtc;
        }

        private static string FormatMarkerPreview(SmartAiMarker marker)
        {
            var parts = new List<string> { $"Marker {marker.Type}", marker.SampleKind };
            if (!string.IsNullOrWhiteSpace(marker.Value))
                parts.Add(marker.Value.Trim());
            if (!string.IsNullOrWhiteSpace(marker.Note))
                parts.Add(marker.Note.Trim());
            return string.Join(" | ", parts);
        }

        private static string ExtractAiCropRelativePath(string text)
        {
            foreach (string line in (text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith("- AI crop:", StringComparison.OrdinalIgnoreCase))
                    continue;

                return trimmed["- AI crop:".Length..].Trim();
            }

            return "";
        }
    }

    private sealed record EstimateDisplayRow(
        string Item,
        string Page,
        string Sections,
        string Quantity,
        string Unit,
        string UnitPrice,
        string Cost,
        string Notes,
        TakeoffItem? Takeoff,
        Measurement? Measurement);

    private sealed record TakeoffManagerRow(
        string Name,
        string Type,
        string Sections,
        string Total,
        string Unit,
        string UnitPrice,
        string Cost,
        string Notes,
        string Folder,
        TakeoffItem Item);

    private sealed class MassingMarkerReviewRow
    {
        public string MarkerId { get; init; } = "";
        public string Role { get; init; } = "";
        public string Type { get; init; } = "";
        public string Page { get; init; } = "";
        public string PdfPoint { get; init; } = "";
        public string DraftPoint { get; init; } = "";
        public string Status { get; init; } = "";
        public SmartAiMarker? Marker { get; init; }
        public bool HasCrop { get; init; }
    }

    private sealed record Massing3DObjectInfo(
        string Id,
        string Kind,
        string Label,
        IReadOnlyList<string> SourceMarkerIds,
        string Notes);

    protected override void OnClosed(EventArgs e)
    {
        SaveSidePanelWidths();
        SaveCurrentPageAnnotations();
        base.OnClosed(e);
        FlushTakeoffAutosaves();
        PdfLayerRenderService.StopWorker();
    }
}
