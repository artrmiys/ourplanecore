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

    // Toolbar and job lifecycle moved to MainWindow.JobLifecycle.cs

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
        PdfLayerRenderService.StopWorker();
    }
}
