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

    // Pages tree workflow moved to MainWindow.PagesTree.cs

    // Takeoffs tree workflow moved to MainWindow.TakeoffsTree.cs

    // Measurement clipboard and autosave callbacks moved to MainWindow.MeasurementClipboard.cs

    // Shared tree and estimate helpers moved to MainWindow.TreeHelpers.cs

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
