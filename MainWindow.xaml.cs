using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using SmartTakeoffs.Controls;

namespace SmartTakeoffs;

public partial class MainWindow : Window
{
    private readonly PdfViewport _viewport;
    private readonly AppSettings _settings = AppSettingsStore.Load();
    private bool _isApplyingSettings;

    private SmartTakeoffsJob? _currentJob;
    private PageInfo? _currentPage;
    private string _currentPdfPath = "";

    private readonly List<TakeoffItem> _takeoffItems = [];
    private TakeoffItem? _activeItem;
    private string _activeTakeoffParentFolder = "";
    private string _activeTool = "pan";
    private PagesClipboard? _pagesClipboard;
    private readonly HashSet<string> _pagesMultiSelection = new(StringComparer.OrdinalIgnoreCase);
    private Point? _pagesDragStart;
    private readonly HashSet<TakeoffItem> _pendingTakeoffAutosaves = [];
    private readonly System.Windows.Threading.DispatcherTimer _takeoffAutosaveTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(500),
    };

    private readonly Dictionary<string, Button> _toolBtns;
    private ToggleButton? _recordButton;
    private ListView? _estimateList;
    private bool _updatingRecordButton;
    private string _lastDrawingTool = "point";
    private bool _inboxExpanded = true;
    private double _inboxExpandedHeight = 170.0;
    private enum PagesClipboardMode { Copy, Cut }
    private sealed record PagesClipboardEntry(string SourcePath, bool IsPage);
    private sealed record PagesClipboard(IReadOnlyList<PagesClipboardEntry> Entries, PagesClipboardMode Mode);

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

    public MainWindow()
    {
        InitializeComponent();

        _viewport = new PdfViewport();
        _viewport.StatusChanged      += msg => TxtStatus.Text = msg;
        _viewport.ScaleChanged       += OnScaleChanged;
        _viewport.ToolChanged        += OnToolChanged;
        _viewport.LayersChanged      += OnLayersChanged;
        _viewport.PdfLayersDiscovered += OnPdfLayersDiscovered;
        _viewport.MeasurementAdded   += OnMeasurementAdded;
        _viewport.MeasurementRemoved += OnMeasurementRemoved;
        _viewport.MeasurementChanged += OnMeasurementChanged;
        _viewport.ContextRequested   += OnViewportContextRequested;
        ViewportHost.Children.Add(_viewport);

        _toolBtns = new Dictionary<string, Button>
        {
            ["pan"]   = BtnPan,
            ["scale"] = BtnScale,
            ["point"] = BtnPoint,
            ["line"]  = BtnLine,
            ["area"]  = BtnArea,
        };
        BtnPoint.Content = "Count";
        BtnPoint.ToolTip = "Count item (P)";
        SetupRecordButton();
        SetupEstimateTable();
        UpdateToolStatus();

        CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, (_, _) => BtnOpen_Click(null!, null!)));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Open, Key.O, ModifierKeys.Control));
        PagesTree.PreviewMouseRightButtonDown += PagesTree_PreviewMouseRightButtonDown;
        PagesTree.PreviewMouseLeftButtonDown += PagesTree_PreviewMouseLeftButtonDown;
        PagesTree.MouseMove += PagesTree_MouseMove;
        PagesTree.DragOver += PagesTree_DragOver;
        PagesTree.Drop += PagesTree_Drop;
        PagesTree.KeyDown += PagesTree_KeyDown;
        PagesTree.AllowDrop = true;
        _takeoffAutosaveTimer.Tick += (_, _) => FlushTakeoffAutosaves();
        TakeoffsTree.ContextMenu = BuildTakeoffsRootContextMenu();
        BtnLayersOn.IsEnabled = false;
        BtnLayersOff.IsEnabled = false;
        BtnLayersClearHi.IsEnabled = false;

        ApplyPersistedSettings();
        Loaded += (_, _) => Dispatcher.InvokeAsync(
            TryOpenLastJobFromSettings,
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    // ── Toolbar ───────────────────────────────────────────────────────────────

    private void BtnOpen_Click(object sender, RoutedEventArgs e)
    {
        string? folder = SelectFolder("Select SmartTakeoffs job folder");
        if (folder == null) return;

        try
        {
            OpenJob(folder);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot open job:\n{ex.Message}", "Open Job",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnOpenJobsFolder_Click(object sender, RoutedEventArgs e)
    {
        string initial = Directory.Exists(_settings.JobsRootPath)
            ? _settings.JobsRootPath
            : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string? root = SelectFolder("Select folder with SmartTakeoffs jobs", initial);
        if (root == null) return;

        _settings.JobsRootPath = root;
        SaveAppSettings();

        var jobs = Directory.EnumerateDirectories(root)
            .Where(IsJobFolder)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (jobs.Count == 0)
        {
            MessageBox.Show("No SmartTakeoffs jobs found in that folder.", "Open Jobs Folder",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string? selected = ShowJobPickerDialog(root, jobs);
        if (selected == null) return;

        try
        {
            OpenJob(selected);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot open job:\n{ex.Message}", "Open Job",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnNewJob_Click(object sender, RoutedEventArgs e)
    {
        string? parent = SelectFolder("Choose parent folder for the new job");
        if (parent == null) return;

        string? name = ShowInputDialog("Job name:", "New Job", "New Job");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            _settings.JobsRootPath = parent;
            SaveAppSettings();
            var job = SmartTakeoffsJobStore.CreateJob(parent, name);
            OpenJob(job.RootPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot create job:\n{ex.Message}", "New Job",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenJob(string rootPath, string? initialPageFolder = null)
    {
        SaveCurrentPageScale();
        _currentJob = SmartTakeoffsJobStore.LoadJob(rootPath);
        _currentPage = null;
        _currentPdfPath = "";
        _pagesClipboard = null;
        _pagesMultiSelection.Clear();
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
        SaveAppSettings();
        if (!string.IsNullOrWhiteSpace(initialPageFolder) && Directory.Exists(initialPageFolder))
            SelectPageByFolder(initialPageFolder);
        ApplyTheme(string.Equals(_settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase), persist: false);
        TxtStatusJob.Text  = _currentJob.Name;
        TxtJobName.Text    = _currentJob.Name;
        TxtStatusPage.Text = "—";
        TxtStatus.Text = $"Loaded job: {_currentJob.Name}";
        LoadObservationsInbox();
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
        _activeItem = null;
        _activeTakeoffParentFolder = _currentJob?.TakeoffsRoot ?? "";

        if (_currentJob == null)
        {
            _viewport.SetMeasurements([]);
            UpdateTotalDisplay();
            return;
        }

        LoadTakeoffChildren(_currentJob.TakeoffsRoot, TakeoffsTree);

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

        RefreshAllTotals();
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

            var created = SmartTakeoffsJobStore.ImportPdf(_currentJob, dlg.FileName, names, destFolder, pdfLayerCache);
            ReloadPagesTree();
            if (created.Count > 0)
                SelectPageByFolder(created[0].FolderPath);
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
            if (PdfLayerRenderService.TryReadVisibleLayers(pdfPath, pageIndex, out var layers, out _))
                cache[pageIndex] = layers;
        }
        return cache;
    }

    private void BtnTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tool)
            SetTool(tool);
    }

    private void SetupRecordButton()
    {
        _recordButton = new ToggleButton
        {
            Content = "Record",
            ToolTip = "Toggle digitizer record mode",
            Padding = new Thickness(8, 2, 8, 2),
            MinWidth = 68,
            Margin = new Thickness(4, 0, 1, 0),
            FontWeight = FontWeights.Bold,
        };
        _recordButton.Checked += (_, _) => OnRecordToggled(on: true);
        _recordButton.Unchecked += (_, _) => OnRecordToggled(on: false);

        int areaIndex = MainToolBar.Items.IndexOf(BtnArea);
        MainToolBar.Items.Insert(areaIndex >= 0 ? areaIndex + 1 : MainToolBar.Items.Count, _recordButton);
    }

    private void SetupEstimateTable()
    {
        TakeoffsPanel.Children.Remove(TakeoffsTree);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 120 });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(150), MinHeight = 80 });

        Grid.SetRow(TakeoffsTree, 0);
        grid.Children.Add(TakeoffsTree);

        var splitter = new GridSplitter
        {
            Height = 4,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
        };
        Grid.SetRow(splitter, 1);
        grid.Children.Add(splitter);

        _estimateList = new ListView
        {
            Margin = new Thickness(2),
            MinHeight = 80,
            View = new GridView
            {
                Columns =
                {
                    new GridViewColumn { Header = "Item", Width = 92, DisplayMemberBinding = new Binding(nameof(EstimateDisplayRow.Item)) },
                    new GridViewColumn { Header = "Type", Width = 52, DisplayMemberBinding = new Binding(nameof(EstimateDisplayRow.Type)) },
                    new GridViewColumn { Header = "Qty", Width = 72, DisplayMemberBinding = new Binding(nameof(EstimateDisplayRow.Quantity)) },
                    new GridViewColumn { Header = "Unit", Width = 40, DisplayMemberBinding = new Binding(nameof(EstimateDisplayRow.Unit)) },
                },
            },
        };
        Grid.SetRow(_estimateList, 2);
        grid.Children.Add(_estimateList);

        TakeoffsPanel.Children.Add(grid);
        RefreshEstimateTable();
    }

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
            SetTool("pan");
    }

    private void SetTool(string tool)
    {
        if (tool is "point" or "line" or "area" && !EnsureDrawingTakeoff(tool))
            return;

        ApplyToolSelection(tool);
    }

    private void ApplyToolSelection(string tool)
    {
        _activeTool = tool;
        if (tool is "point" or "line" or "area")
            _lastDrawingTool = tool;
        _viewport.SetTool(tool);
        foreach (var (t, btn) in _toolBtns)
            btn.Style = t == tool
                ? (Style)FindResource("ToolBtnActive")
                : (Style)FindResource("ToolBtn");
        UpdateRecordButton();
        UpdateToolStatus();
    }

    private void UpdateRecordButton()
    {
        if (_recordButton == null)
            return;

        bool recording = _activeTool is "point" or "line" or "area";
        _updatingRecordButton = true;
        _recordButton.IsChecked = recording;
        _recordButton.Content = recording ? "Record On" : "Record";
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

    private bool EnsureDrawingTakeoff(string tool)
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

        if (_activeItem != null && _activeItem.MeasurementType == mtype)
        {
            _viewport.ActiveColor = _activeItem.Color;
            _viewport.ActiveTakeoffFolder = _activeItem.FolderPath;
            return true;
        }

        string parentFolder = CurrentTakeoffParentFolder();
        string defaultColor = _activeItem?.Color ?? _viewport.ActiveColor;
        var dlg = new NewItemDialog(
            mtype,
            DefaultTakeoffName(mtype),
            lockType: true,
            defaultColor: defaultColor)
        {
            Owner = this,
        };
        if (dlg.ShowDialog() != true)
            return false;

        var newItem = CreateUniqueTakeoffItem(dlg.ItemName, dlg.ItemColor, mtype, parentFolder);
        _takeoffItems.Add(newItem);
        var treeParent = FindTakeoffTreeItemByFolder(parentFolder) ?? (ItemsControl)TakeoffsTree;
        var tvi = AddTakeoffTreeItem(newItem, treeParent);
        if (treeParent is TreeViewItem parentTvi)
            parentTvi.IsExpanded = true;

        tvi.IsSelected = true;
        UpdateTotalDisplay();
        return true;
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

    private void BtnDarkTheme_Checked(object sender, RoutedEventArgs e) =>
        ApplyTheme(dark: true, persist: true);

    private void BtnDarkTheme_Unchecked(object sender, RoutedEventArgs e) =>
        ApplyTheme(dark: false, persist: true);

    private void ReloadPagesTree(string? selectPath = null)
    {
        PagesTree.Items.Clear();
        if (_currentJob == null) return;

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
            IsExpanded = true,
        };
        PagesTree.Items.Add(rootItem);
        FillPagesTree(rootItem.Items, _currentJob.PagesRoot);
        RefreshPagesTakeoffIndicators();

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
                items.Add(new TreeViewItem
                {
                    Header = BuildPageHeader(page),
                    Tag = page,
                });
                continue;
            }

            string name = SmartTakeoffsJobStore.ReadName(dir) ?? Path.GetFileName(dir);
            var folderNode = new PageFolderNode { Name = name, FolderPath = dir };
            var tvi = new TreeViewItem
            {
                Header = $"📁 {name}",
                Tag = folderNode,
                IsExpanded = true,
            };
            items.Add(tvi);
            FillPagesTree(tvi.Items, dir);
        }
    }

    private StackPanel BuildPageHeader(PageInfo page)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = $"  {page.Name}",
            VerticalAlignment = VerticalAlignment.Center,
        });

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

    private IEnumerable<TakeoffItem> TakeoffsForPage(string pageFolder) =>
        _takeoffItems.Where(item => item.Measurements.Any(m =>
            string.Equals(m.PageFolder, pageFolder, StringComparison.OrdinalIgnoreCase)));

    private bool IsPageMeasuredByActiveTakeoff(TreeViewItem item) =>
        _activeItem != null &&
        item.Tag is PageInfo page &&
        _activeItem.Measurements.Any(m =>
            string.Equals(m.PageFolder, page.FolderPath, StringComparison.OrdinalIgnoreCase));

    private void RefreshPagesTakeoffIndicators()
    {
        foreach (TreeViewItem item in EnumeratePageTreeItems())
        {
            if (item.Tag is PageInfo page)
                item.Header = BuildPageHeader(page);
        }
        ApplyPagesMultiSelectionVisuals();
    }

    private void PagesTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem { Tag: PageInfo page }) return;

        SaveCurrentPageScale();
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
            page.PdfLayersCached ? page.PdfLayers : null);
        _settings.LastPageFolder = page.FolderPath;
        if (_currentJob != null)
            _settings.LastJobPath = _currentJob.RootPath;
        SaveAppSettings();

        if (_takeoffItems.Count == 0)
            TryAutoLoad();
        ApplyTakeoffPageHighlights();
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
        foreach (TreeViewItem item in PagesTree.Items)
        {
            if (SelectNodeByFolder(item, folderPath))
                return;
        }
    }

    private static bool SelectNodeByFolder(TreeViewItem item, string folderPath)
    {
        string? itemPath = GetPagesNodePath(item);
        if (itemPath != null &&
            string.Equals(itemPath, folderPath, StringComparison.OrdinalIgnoreCase))
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
        foreach (TreeViewItem item in PagesTree.Items)
        {
            if (SelectPageByFolder(item, folderPath))
                return;
        }
    }

    private static bool SelectPageByFolder(TreeViewItem item, string folderPath)
    {
        if (item.Tag is PageInfo page &&
            string.Equals(page.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase))
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

        string? path = GetPagesNodePath(item);
        if (path != null && !_pagesMultiSelection.Contains(path))
        {
            _pagesMultiSelection.Clear();
            if (!IsRootPagesNode(item))
                _pagesMultiSelection.Add(path);
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

        string? path = GetPagesNodePath(item);
        if (path == null) return;

        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && !IsRootPagesNode(item))
        {
            if (!_pagesMultiSelection.Add(path))
                _pagesMultiSelection.Remove(path);
            item.IsSelected = true;
            ApplyPagesMultiSelectionVisuals();
            e.Handled = true;
            return;
        }

        if (!_pagesMultiSelection.SetEquals([path]))
        {
            _pagesMultiSelection.Clear();
            if (!IsRootPagesNode(item))
                _pagesMultiSelection.Add(path);
            ApplyPagesMultiSelectionVisuals();
        }
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

    private void PagesTree_KeyDown(object sender, KeyEventArgs e)
    {
        if (PagesTree.SelectedItem is not TreeViewItem item) return;

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
            bool isRoot = folder.IsRoot;
            bool canPaste = CanPasteInto(folder.FolderPath);
            bool hasChildren = Directory.Exists(folder.FolderPath) &&
                               Directory.EnumerateDirectories(folder.FolderPath).Any();

            menu.Items.Add(MakeMenuItem("New Folder", true, () => NewPageFolder(item)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Rename Folder", !isRoot, () => RenamePagesNode(item)));
            menu.Items.Add(MakeMenuItem("Delete Folder", !isRoot, () => DeletePagesNode(item)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Copy Folder", !isRoot, () => CopyCutPagesNode(item, PagesClipboardMode.Copy)));
            menu.Items.Add(MakeMenuItem("Cut Folder", !isRoot, () => CopyCutPagesNode(item, PagesClipboardMode.Cut)));
            menu.Items.Add(MakeMenuItem("Paste Into Folder", canPaste, () => PasteIntoSelectedTarget(item)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Move Up", !isRoot && CanMoveSibling(folder.FolderPath, -1), () => MovePagesNode(item, -1)));
            menu.Items.Add(MakeMenuItem("Move Down", !isRoot && CanMoveSibling(folder.FolderPath, 1), () => MovePagesNode(item, 1)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Sort Children A-Z", hasChildren, () => SortFolderChildren(item, descending: false)));
            menu.Items.Add(MakeMenuItem("Sort Children Z-A", hasChildren, () => SortFolderChildren(item, descending: true)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Open in Explorer", true, () => OpenFolderInExplorer(folder.FolderPath)));
        }
        else if (item.Tag is PageInfo page)
        {
            string parent = Path.GetDirectoryName(page.FolderPath) ?? "";
            menu.Items.Add(MakeMenuItem("Rename Page", true, () => RenamePagesNode(item)));
            menu.Items.Add(MakeMenuItem("Delete Page", true, () => DeletePagesNode(item)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Copy Page", true, () => CopyCutPagesNode(item, PagesClipboardMode.Copy)));
            menu.Items.Add(MakeMenuItem("Cut Page", true, () => CopyCutPagesNode(item, PagesClipboardMode.Cut)));
            menu.Items.Add(MakeMenuItem("Paste Into Parent Folder", CanPasteInto(parent), () => PasteIntoSelectedTarget(item)));
            menu.Items.Add(MakeMenuItem("Duplicate Page", true, () => DuplicatePageNode(item)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Move Up", CanMoveSibling(page.FolderPath, -1), () => MovePagesNode(item, -1)));
            menu.Items.Add(MakeMenuItem("Move Down", CanMoveSibling(page.FolderPath, 1), () => MovePagesNode(item, 1)));
            menu.Items.Add(MakeMenuItem("Move to Folder...", true, () => MovePageToFolder(item)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Open Page Folder in Explorer", true, () => OpenFolderInExplorer(page.FolderPath)));
        }

        return menu;
    }

    private static MenuItem MakeMenuItem(string header, bool isEnabled, Action action)
    {
        var item = new MenuItem { Header = header, IsEnabled = isEnabled };
        item.Click += (_, _) => action();
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
            ClearCurrentPageIfAffected(path);
            ReloadPagesTree(renamed);
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
                    ClearCurrentPageIfAffected(source);
                    pasted = SmartTakeoffsJobStore.MoveNode(source, targetFolder);
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
        if (IsRootPagesNode(item)) return;
        string? path = GetPagesNodePath(item);
        if (path == null || !IsPathInsidePagesRoot(path, allowRoot: false)) return;

        try
        {
            if (SmartTakeoffsJobStore.MoveSibling(path, offset))
            {
                ReloadPagesTree(path);
                TxtStatus.Text = offset < 0 ? "Moved up." : "Moved down.";
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
            ClearCurrentPageIfAffected(page.FolderPath);
            string moved = SmartTakeoffsJobStore.MoveNode(page.FolderPath, target);
            ReloadPagesTree(moved);
            TxtStatus.Text = $"Moved page to: {SmartTakeoffsJobStore.DisplayName(target)}";
        }
        catch (Exception ex)
        {
            ShowOperationError("Move to Folder", ex);
        }
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

    private bool CanMoveSibling(string path, int offset)
    {
        string parent = Path.GetDirectoryName(path) ?? "";
        var siblings = SmartTakeoffsJobStore.GetOrderedChildDirectories(parent).ToList();
        int index = siblings.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        int target = index + offset;
        return index >= 0 && target >= 0 && target < siblings.Count;
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
            if (selected)
            {
                item.Background = new SolidColorBrush(Color.FromRgb(205, 226, 255));
                item.Foreground = Brushes.Black;
            }
            else if (IsPageMeasuredByActiveTakeoff(item))
            {
                item.Background = new SolidColorBrush(Color.FromRgb(255, 242, 166));
                item.Foreground = Brushes.Black;
            }
            else
            {
                item.ClearValue(Control.BackgroundProperty);
                item.ClearValue(Control.ForegroundProperty);
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

    private void ShowOperationError(string operation, Exception ex)
    {
        MessageBox.Show(ex.Message, operation, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ── Takeoffs tree ─────────────────────────────────────────────────────────

    private void TakeoffsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem { Tag: TakeoffItem item })
        {
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
            UpdateTotalDisplay();
        }
        else if (e.NewValue is TreeViewItem { Tag: TakeoffFolderNode folder })
        {
            _activeItem = null;
            _activeTakeoffParentFolder = folder.FolderPath;
            _viewport.ActiveTakeoffFolder = "";
            UpdateToolStatus();
            RefreshPagesTakeoffIndicators();
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

        string measurementType = CurrentToolMeasurementType();
        var dlg = new NewItemDialog(measurementType, DefaultTakeoffName(measurementType)) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        string parentFolder = CurrentTakeoffParentFolder();
        var item = CreateUniqueTakeoffItem(dlg.ItemName, dlg.ItemColor, dlg.ItemType, parentFolder);
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

    private string BuildTakeoffCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("RowType,Item,MeasurementType,Total,MeasurementCount,MeasurementId,MeasurementValue,MeasurementLabel,ScaleMetersPerPt,PageFolder,TakeoffFolder");

        foreach (var item in _takeoffItems.Where(i => i.Measurements.Count > 0))
        {
            string itemTypes = string.Join("+", item.Measurements.Select(m => m.MType).Distinct(StringComparer.OrdinalIgnoreCase));
            AppendCsvRow(sb,
                "ItemTotal",
                item.Name,
                itemTypes,
                item.TotalLabel(_viewport.ScaleMetersPerPt, _viewport.UnitMode),
                item.Measurements.Count.ToString(),
                "",
                "",
                "",
                "",
                "",
                item.FolderPath);

            foreach (var measurement in item.Measurements)
            {
                AppendCsvRow(sb,
                    "Measurement",
                    item.Name,
                    measurement.MType,
                    "",
                    "",
                    measurement.Id,
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
                CurrentTakeoffParentFolder());
            item.FolderPath = stored.FolderPath;
        }

        foreach (var measurement in item.Measurements)
            measurement.TakeoffFolder = item.FolderPath;
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

        var rename = new MenuItem { Header = "Rename…" };
        rename.Click += (_, _) => RenameItem(tvi, item);
        menu.Items.Add(rename);

        var moveUp = new MenuItem { Header = "Move Up" };
        moveUp.Click += (_, _) => MoveTakeoffNode(item.FolderPath, -1);
        menu.Items.Add(moveUp);

        var moveDown = new MenuItem { Header = "Move Down" };
        moveDown.Click += (_, _) => MoveTakeoffNode(item.FolderPath, 1);
        menu.Items.Add(moveDown);

        menu.Items.Add(new Separator());

        var delete = new MenuItem { Header = "Delete item + measurements" };
        delete.Click += (_, _) => DeleteItem(tvi, item);
        menu.Items.Add(delete);

        tvi.ContextMenu = menu;
    }

    private void AttachFolderContextMenu(TreeViewItem tvi, TakeoffFolderNode folder)
    {
        var menu = new ContextMenu();

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

        menu.Items.Add(new Separator());

        var rename = new MenuItem { Header = "Rename Folder…" };
        rename.Click += (_, _) => RenameTakeoffFolder(tvi, folder);
        menu.Items.Add(rename);

        var moveUp = new MenuItem { Header = "Move Up" };
        moveUp.Click += (_, _) => MoveTakeoffNode(folder.FolderPath, -1);
        menu.Items.Add(moveUp);

        var moveDown = new MenuItem { Header = "Move Down" };
        moveDown.Click += (_, _) => MoveTakeoffNode(folder.FolderPath, 1);
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

        var delete = new MenuItem { Header = "Delete folder + children" };
        delete.Click += (_, _) => DeleteTakeoffFolder(tvi, folder);
        menu.Items.Add(delete);

        tvi.ContextMenu = menu;
    }

    private ContextMenu BuildTakeoffsRootContextMenu()
    {
        var menu = new ContextMenu();

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
                item.FolderPath = SmartTakeoffsJobStore.RenameNode(item.FolderPath, name);
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

    private void RenameTakeoffFolder(TreeViewItem tvi, TakeoffFolderNode folder)
    {
        string? name = ShowInputDialog("New name:", "Rename Folder", folder.Name);
        if (name == null || name == folder.Name) return;

        try
        {
            string oldPath = folder.FolderPath;
            string newPath = SmartTakeoffsJobStore.RenameNode(folder.FolderPath, name);
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

    private void MoveTakeoffNode(string folderPath, int offset)
    {
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

    // ── Measurement callbacks ─────────────────────────────────────────────────

    private void OnMeasurementAdded(Measurement m)
    {
        if (!TryResolveTakeoffItemForMeasurement(m, out TakeoffItem item))
        {
            _viewport.DeleteMeasurements([m]);
            TxtStatus.Text = $"No {m.MType} takeoff item is active. Select {MeasurementTypeTitle(m.MType)} again to create one.";
            return;
        }

        _activeItem = item;
        EnsureTakeoffItemFolder(item);
        m.TakeoffFolder = item.FolderPath;
        if (m.ScaleMetersPerPt <= 0)
            m.ScaleMetersPerPt = _viewport.ScaleMetersPerPt;
        item.Measurements.Add(m);
        RefreshTreeItem(item);
        QueueTakeoffAutosave(item);
        RefreshPagesTakeoffIndicators();
        UpdateTotalDisplay();
    }

    private bool TryResolveTakeoffItemForMeasurement(Measurement m, out TakeoffItem item)
    {
        string measurementType = SmartTakeoffsJobStore.NormalizeMeasurementType(m.MType);

        if (!string.IsNullOrWhiteSpace(m.TakeoffFolder))
        {
            var byFolder = _takeoffItems.FirstOrDefault(i =>
                i.MeasurementType == measurementType &&
                string.Equals(i.FolderPath, m.TakeoffFolder, StringComparison.OrdinalIgnoreCase));
            if (byFolder != null)
            {
                item = byFolder;
                return true;
            }
        }

        if (_activeItem != null && _activeItem.MeasurementType == measurementType)
        {
            item = _activeItem;
            return true;
        }

        item = null!;
        return false;
    }

    private void OnMeasurementRemoved(Measurement m)
    {
        foreach (var item in _takeoffItems)
        {
            if (item.Measurements.Remove(m))
            {
                RefreshTreeItem(item);
                QueueTakeoffAutosave(item);
            }
        }
        RefreshPagesTakeoffIndicators();
        UpdateTotalDisplay();
    }

    private void OnMeasurementChanged(Measurement m)
    {
        foreach (var item in _takeoffItems)
        {
            if (!item.Measurements.Contains(m)) continue;

            if (!string.IsNullOrWhiteSpace(item.FolderPath))
                m.TakeoffFolder = item.FolderPath;
            if (m.ScaleMetersPerPt <= 0)
                m.ScaleMetersPerPt = _viewport.ScaleMetersPerPt;
            RefreshTreeItem(item);
            QueueTakeoffAutosave(item);
            break;
        }
        UpdateTotalDisplay();
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
        }
    }

    private void RefreshAllTotals()
    {
        RefreshTotalsRecursive(TakeoffsTree);
        RefreshPagesTakeoffIndicators();
        ApplyTakeoffPageHighlights();
        UpdateTotalDisplay();
    }

    private void RefreshTotalsRecursive(ItemsControl parent)
    {
        foreach (TreeViewItem tvi in parent.Items.OfType<TreeViewItem>())
        {
            if (tvi.Tag is TakeoffItem item)
                SetTreeItemHeader(tvi, item);
            else
                RefreshTotalsRecursive(tvi);
        }
    }

    private void ApplyTakeoffPageHighlights()
    {
        foreach (TreeViewItem item in EnumerateTakeoffTreeItems(TakeoffsTree))
        {
            if (item.Tag is TakeoffItem takeoff && IsTakeoffMeasuredOnCurrentPage(takeoff))
            {
                item.Background = new SolidColorBrush(Color.FromRgb(214, 245, 222));
                item.Foreground = Brushes.Black;
            }
            else
            {
                item.ClearValue(Control.BackgroundProperty);
                item.ClearValue(Control.ForegroundProperty);
            }
        }
    }

    private bool IsTakeoffMeasuredOnCurrentPage(TakeoffItem takeoff) =>
        _currentPage != null &&
        takeoff.Measurements.Any(m =>
            string.Equals(m.PageFolder, _currentPage.FolderPath, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<TreeViewItem> EnumerateTakeoffTreeItems(ItemsControl parent)
    {
        foreach (TreeViewItem child in parent.Items.OfType<TreeViewItem>())
        {
            yield return child;
            foreach (TreeViewItem nested in EnumerateTakeoffTreeItems(child))
                yield return nested;
        }
    }

    private void SetTreeItemHeader(TreeViewItem tvi, TakeoffItem item)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        panel.Children.Add(new Border
        {
            Width             = 12,
            Height            = 12,
            CornerRadius      = new CornerRadius(6),
            Background        = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.Color)),
            Margin            = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text              = item.Name,
            VerticalAlignment = VerticalAlignment.Center,
        });

        panel.Children.Add(new TextBlock
        {
            Text              = $"  [{MeasurementTypeTitle(item.MeasurementType)}]",
            Foreground        = Brushes.Gray,
            FontSize          = 10,
            VerticalAlignment = VerticalAlignment.Center,
        });

        string total = item.TotalLabel(_viewport.ScaleMetersPerPt, _viewport.UnitMode);
        panel.Children.Add(new TextBlock
        {
            Text              = $"  {total}",
            Foreground        = Brushes.Gray,
            FontSize          = 10,
            VerticalAlignment = VerticalAlignment.Center,
        });

        tvi.Header = panel;
    }

    private static void SetFolderTreeItemHeader(TreeViewItem tvi, TakeoffFolderNode folder)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = folder.Name,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "  folder",
            Foreground = Brushes.Gray,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
        });
        tvi.Header = panel;
    }

    private string CurrentTakeoffParentFolder()
    {
        if (_currentJob == null)
            return "";

        if (TakeoffsTree.SelectedItem is TreeViewItem { Tag: TakeoffFolderNode folder })
            return folder.FolderPath;

        if (TakeoffsTree.SelectedItem is TreeViewItem { Tag: TakeoffItem item } &&
            !string.IsNullOrWhiteSpace(item.FolderPath))
        {
            return Path.GetDirectoryName(item.FolderPath) ?? _currentJob.TakeoffsRoot;
        }

        if (!string.IsNullOrWhiteSpace(_activeTakeoffParentFolder) &&
            Directory.Exists(_activeTakeoffParentFolder))
        {
            return _activeTakeoffParentFolder;
        }

        return _currentJob.TakeoffsRoot;
    }

    private string CurrentToolMeasurementType() =>
        _activeTool is "point" or "area" ? _activeTool : "line";

    private void UpdateToolStatus()
    {
        string title = _activeTool switch
        {
            "point" => "Count",
            "line" => "Line",
            "area" => "Area",
            "scale" => "Scale",
            _ => "Pan",
        };
        bool recording = _activeTool is "point" or "line" or "area";
        string item = recording && _activeItem != null
            ? $"  |  Item: {_activeItem.Name}"
            : "";
        TxtTool.Text = $"  Tool: {title}  |  Record: {(recording ? "On" : "Off")}{item}";
    }

    private string DefaultTakeoffName(string measurementType)
    {
        string title = MeasurementTypeTitle(measurementType);
        if (_activeItem != null && _activeItem.MeasurementType != measurementType)
            return $"{_activeItem.Name} - {title}";
        if (_currentPage != null)
            return $"{_currentPage.Name} {title}";
        return $"{title} Item";
    }

    private static string MeasurementTypeTitle(string measurementType) =>
        measurementType switch
        {
            "point" => "Count",
            "area" => "Area",
            _ => "Line",
        };

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
            tvi.IsSelected = true;
            tvi.BringIntoView();
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

    private TreeViewItem? FindTakeoffTreeItemByFolder(string folderPath) =>
        FindTakeoffTreeItemByFolder(TakeoffsTree, folderPath);

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
        if (_activeItem == null || _activeItem.Measurements.Count == 0)
        {
            TxtTotal.Text = "Total: —";
            return;
        }
        TxtTotal.Text = $"Total: {_activeItem.TotalLabel(_viewport.ScaleMetersPerPt, _viewport.UnitMode)}";
    }

    private void RefreshEstimateTable()
    {
        if (_estimateList == null)
            return;

        _estimateList.Items.Clear();
        foreach (var item in _takeoffItems.Where(i => i.Measurements.Count > 0))
        {
            _estimateList.Items.Add(new EstimateDisplayRow(
                item.Name,
                MeasurementTypeTitle(item.MeasurementType),
                QuantityText(item),
                UnitText(item.MeasurementType)));
        }
    }

    private string QuantityText(TakeoffItem item)
    {
        string mt = SmartTakeoffsJobStore.NormalizeMeasurementType(item.MeasurementType);
        double value = item.Total(_viewport.ScaleMetersPerPt);
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

    // ── Layers panel ──────────────────────────────────────────────────────────

    private void OnLayersChanged(IReadOnlyList<PdfLayer> layers)
    {
        LayersPanel.Children.Clear();
        BtnLayersOn.IsEnabled = layers.Count > 0;
        BtnLayersOff.IsEnabled = layers.Count > 0;
        BtnLayersClearHi.IsEnabled = layers.Count > 0;
        if (layers.Count == 0)
        {
            LayersPanel.Children.Add(new TextBlock
            {
                Text       = "  No PDF layers detected.",
                Foreground = Brushes.Gray,
                FontSize   = 10,
                Margin     = new Thickness(0, 2, 0, 2),
            });
            return;
        }
        foreach (var layer in layers)
        {
            var row = new DockPanel { Margin = new Thickness(2, 1, 2, 1) };
            var hi = new CheckBox
            {
                Content = "Hi",
                IsChecked = layer.IsHighlighted,
                FontSize = 10,
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = "Highlight this layer",
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(hi, Dock.Right);
            row.Children.Add(hi);

            var cb = new CheckBox
            {
                Content   = layer.Name,
                IsChecked = layer.IsOn,
                FontSize  = 10,
                VerticalAlignment = VerticalAlignment.Center,
            };
            cb.Checked   += (_, _) => _viewport.SetLayerVisible(layer.Number, true);
            cb.Unchecked += (_, _) => _viewport.SetLayerVisible(layer.Number, false);
            hi.Checked += (_, _) => _viewport.SetLayerHighlighted(layer.Number, true);
            hi.Unchecked += (_, _) => _viewport.SetLayerHighlighted(layer.Number, false);
            row.Children.Add(cb);
            LayersPanel.Children.Add(row);
        }
    }

    private void OnPdfLayersDiscovered(IReadOnlyList<PdfLayerInfo> layers)
    {
        if (_currentPage == null || _currentPage.PdfLayersCached)
            return;

        try
        {
            SmartTakeoffsJobStore.SavePageLayerCache(_currentPage.FolderPath, layers);
            TxtStatus.Text = $"Cached {layers.Count} visible PDF layer(s) for this page.";
        }
        catch
        {
            // Layer cache is an optimization; rendering should never depend on saving it.
        }
    }

    private void BtnLayersOn_Click(object sender, RoutedEventArgs e)
    {
        _viewport.SetAllLayers(true);
    }

    private void BtnLayersOff_Click(object sender, RoutedEventArgs e)
    {
        _viewport.SetAllLayers(false);
    }

    private void BtnLayersClearHi_Click(object sender, RoutedEventArgs e)
    {
        _viewport.ClearLayerHighlights();
    }

    // ── Callbacks from viewport ───────────────────────────────────────────────

    private void OnScaleChanged(double scale)
    {
        if (_currentPage != null)
            _currentPage.ScaleMetersPerPt = scale;
        ApplyScaleToCurrentPageMeasurements(scale);
        SaveCurrentPageScale();
        UpdateScaleUi(scale);
        RefreshAllTotals();
    }

    private void ApplyScaleToCurrentPageMeasurements(double scale)
    {
        if (_currentPage == null || scale <= 0)
            return;

        foreach (var measurement in _takeoffItems.SelectMany(i => i.Measurements))
        {
            if (string.Equals(measurement.PageFolder, _currentPage.FolderPath, StringComparison.OrdinalIgnoreCase))
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

        if (request.Measurement == null)
            AddPdfAiMenuItems(menu, request);
        else
            AddMeasurementAiMenuItems(menu, request);

        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Open Project Context", true, OpenProjectContextMarkdown));
        menu.IsOpen = true;
    }

    private void AddPdfAiMenuItems(ContextMenu menu, ViewportContextRequest request)
    {
        menu.Items.Add(MakeMenuItem("Ask AI about this point / area", true, () =>
            SaveViewportObservation(
                request,
                "ai_request",
                "Ask AI",
                "Pending AI request:\nExplain what is important around this point/area on the plan.")));

        menu.Items.Add(MakeMenuItem("Read text near point", true, () =>
            SaveViewportObservation(
                request,
                "text_read_request",
                "Read Text Near Point",
                "Pending OCR request:\nRead and summarize text near this point.")));

        menu.Items.Add(MakeMenuItem("Save observation here", true, () =>
            SaveViewportObservation(
                request,
                "manual",
                "Save Observation",
                "Observation:\n")));

        menu.Items.Add(MakeMenuItem("Add as pending check", true, () =>
            SaveViewportObservation(
                request,
                "pending_check",
                "Pending Check",
                "Pending check:\nVerify this area before final takeoff.")));

        menu.Items.Add(new Separator());

        menu.Items.Add(MakeMenuItem("Suggest takeoff item here", true, () =>
            SuggestTakeoffItemFromContext(request)));

        menu.Items.Add(MakeMenuItem("Trace wall from this point", true, () =>
            SaveViewportObservation(
                request,
                "trace_request",
                "Trace Wall",
                "Pending SmartTrace request:\nTrace wall/linear segment from this point, preview before apply.")));

        menu.Items.Add(MakeMenuItem("Trace closed area", true, () =>
            SaveViewportObservation(
                request,
                "trace_area_request",
                "Trace Closed Area",
                "Pending SmartTrace request:\nTrace closed area from this point, preview before apply.")));

        menu.Items.Add(MakeMenuItem("Check missed takeoffs on this page", true, () =>
            SaveViewportObservation(
                request,
                "missed_takeoff_check",
                "Check Missed Takeoffs",
                "Pending SmartCheck request:\nReview this page for possible missed takeoffs.")));
    }

    private void AddMeasurementAiMenuItems(ContextMenu menu, ViewportContextRequest request)
    {
        Measurement measurement = request.Measurement!;
        menu.Items.Add(MakeMenuItem("Explain measurement", true, () =>
            SaveViewportObservation(
                request,
                "measurement_explain_request",
                "Explain Measurement",
                $"Pending AI request:\nExplain this measurement and whether it matches the selected takeoff item.\n\n{FormatMeasurementSummary(measurement)}")));

        menu.Items.Add(MakeMenuItem("Find similar", true, () =>
            SaveViewportObservation(
                request,
                "find_similar_request",
                "Find Similar",
                $"Pending SmartCheck request:\nFind similar measurements or plan conditions.\n\n{FormatMeasurementSummary(measurement)}")));

        menu.Items.Add(MakeMenuItem("Link to observation", true, () =>
            SaveViewportObservation(
                request,
                "measurement_link_request",
                "Link Measurement",
                $"Pending link request:\nConnect this measurement to a project observation/note.\n\n{FormatMeasurementSummary(measurement)}")));

        menu.Items.Add(MakeMenuItem("Create note from measurement", true, () =>
            SaveViewportObservation(
                request,
                "measurement_note",
                "Measurement Note",
                $"Measurement note:\n{FormatMeasurementSummary(measurement)}")));
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

        string details =
            $"{text.Trim()}\n\n" +
            "Context:\n" +
            $"- Page: {_currentPage.Name}\n" +
            $"- PDF point: {request.PdfX:F1}, {request.PdfY:F1}\n";
        if (request.Measurement != null)
            details += $"- Measurement: {FormatMeasurementSummary(request.Measurement)}\n";

        var observation = SmartContextStore.AddObservation(_currentJob, _currentPage, type, details);
        TxtStatus.Text = $"Saved {type} {observation.Id} -> {_currentJob.AIContextRoot}";
        LoadObservationsInbox();
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
            string parentFolder = CurrentTakeoffParentFolder();
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

    private void BtnImperial_Checked(object sender, RoutedEventArgs e)
    {
        _viewport.UnitMode = UnitMode.Imperial;
        _settings.UnitMode = UnitMode.Imperial.ToString();
        SaveAppSettings();
        RefreshAllTotals();
        _viewport.InvalidateVisual();
    }

    private void BtnImperial_Unchecked(object sender, RoutedEventArgs e)
    {
        _viewport.UnitMode = UnitMode.Metric;
        _settings.UnitMode = UnitMode.Metric.ToString();
        SaveAppSettings();
        RefreshAllTotals();
        _viewport.InvalidateVisual();
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
            BtnImperial.IsChecked = _viewport.UnitMode == UnitMode.Imperial;
            ApplyViewportBackground(_settings.ViewportBackground, persist: false);
            ApplyTheme(string.Equals(_settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase), persist: false);
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    private void TryOpenLastJobFromSettings()
    {
        if (string.IsNullOrWhiteSpace(_settings.LastJobPath) || !Directory.Exists(_settings.LastJobPath))
            return;

        try
        {
            OpenJob(_settings.LastJobPath, initialPageFolder: _settings.LastPageFolder);
            TxtStatus.Text = $"Loaded last job: {_currentJob?.Name}. Select a page to render it.";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Last job could not be opened: {ex.Message}";
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
        BtnDarkTheme.IsChecked = dark;
        BtnDarkTheme.Content = dark ? "Light" : "Dark";

        Color window = dark ? Color.FromRgb(30, 32, 35) : Color.FromRgb(240, 240, 240);
        Color toolbar = dark ? Color.FromRgb(43, 45, 49) : Color.FromRgb(240, 240, 240);
        Color panel = dark ? Color.FromRgb(37, 39, 42) : Color.FromRgb(245, 245, 245);
        Color status = dark ? Color.FromRgb(43, 45, 49) : Color.FromRgb(232, 232, 232);
        Color tree = dark ? Color.FromRgb(31, 33, 36) : Colors.White;
        Brush foreground = new SolidColorBrush(dark ? Color.FromRgb(230, 230, 230) : Color.FromRgb(30, 30, 30));
        UpdateAppBrush("ScrollBarTrackBrush", dark ? Color.FromRgb(45, 47, 52)  : Color.FromRgb(220, 220, 220));
        UpdateAppBrush("ScrollBarThumbBrush", dark ? Color.FromRgb(90, 93, 100) : Color.FromRgb(160, 160, 160));
        UpdateAppBrush("ControlForegroundBrush", dark ? Color.FromRgb(238, 238, 238) : Color.FromRgb(32, 32, 32));
        UpdateAppBrush("ControlBackgroundBrush", dark ? Color.FromRgb(58, 61, 66) : Color.FromRgb(248, 248, 248));
        UpdateAppBrush("ControlBorderBrush", dark ? Color.FromRgb(118, 122, 130) : Color.FromRgb(160, 160, 160));
        UpdateAppBrush("ControlActiveBackgroundBrush", dark ? Color.FromRgb(37, 99, 160) : Color.FromRgb(204, 229, 255));
        UpdateAppBrush("ControlActiveForegroundBrush", Colors.White);

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
        ObservationsListView.Background  = new SolidColorBrush(tree);
        InboxPanel.Background           = new SolidColorBrush(tree);
        InboxHeaderBorder.Background    = new SolidColorBrush(toolbar);
        InboxSplitter.Background        = new SolidColorBrush(status);
        UpdateRecordButton();

        foreach (TextBlock text in FindVisualChildren<TextBlock>(this))
        {
            if (ReferenceEquals(text, TxtScaleInfo)) continue;
            text.Foreground = foreground;
        }

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

    private string? ShowJobPickerDialog(string root, IReadOnlyList<string> jobs)
    {
        var win = new Window
        {
            Title = "Open Job",
            Width = 520,
            Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            Owner = this,
        };

        var panel = new DockPanel { Margin = new Thickness(10) };
        var title = new TextBlock
        {
            Text = root,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        DockPanel.SetDock(title, Dock.Top);
        panel.Children.Add(title);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var open = new Button { Content = "Open", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        buttons.Children.Add(open);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);

        var list = new ListBox
        {
            ItemsSource = jobs.Select(Path.GetFileName).ToList(),
        };
        if (list.Items.Count > 0)
            list.SelectedIndex = 0;
        panel.Children.Add(list);
        win.Content = panel;

        string? result = null;
        void Accept()
        {
            if (list.SelectedIndex < 0) return;
            result = jobs[list.SelectedIndex];
            win.DialogResult = true;
        }

        open.Click += (_, _) => Accept();
        list.MouseDoubleClick += (_, _) => Accept();
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
            TxtInboxToggle.Text    = "▴";
        }
        else
        {
            InboxRow.Height        = new GridLength(_inboxExpandedHeight);
            InboxSplitterRow.Height = new GridLength(4);
            TxtInboxToggle.Text    = "▾";
        }
        _inboxExpanded = !_inboxExpanded;
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
            catch { /* skip malformed lines */ }
        }

        foreach (var obs in list.OrderByDescending(o => o.CreatedAtUtc))
            ObservationsListView.Items.Add(new ObservationDisplayItem(obs));

        int count = list.Count;
        TxtInboxCount.Text    = count.ToString();
        InboxBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private sealed class ObservationDisplayItem
    {
        public string TypeShort   { get; }
        public string BadgeColor  { get; }
        public string Page        { get; }
        public string TextPreview { get; }
        public string TimeDisplay { get; }

        public ObservationDisplayItem(SmartObservation obs)
        {
            Page = obs.Page;

            string raw = (obs.Text ?? "").Replace('\r', ' ').Replace('\n', ' ');
            TextPreview = raw.Length > 120 ? raw[..117] + "…" : raw;

            (TypeShort, BadgeColor) = obs.Type switch
            {
                "ai_request"                    => ("Ask AI",      "#1565C0"),
                "text_read_request"             => ("Read Text",   "#00796B"),
                "pending_check"                 => ("Check",       "#E65100"),
                "trace_request"                 => ("SmartTrace",  "#2E7D32"),
                "trace_area_request"            => ("Trace Area",  "#2E7D32"),
                "missed_takeoff_check"          => ("Missed",      "#C62828"),
                "takeoff_suggestion"            => ("Suggestion",  "#6A1B9A"),
                "measurement_explain_request"   => ("Explain",     "#1565C0"),
                "find_similar_request"          => ("Find Similar","#00796B"),
                "measurement_link_request"      => ("Link",        "#546E7A"),
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
    }

    private sealed record EstimateDisplayRow(
        string Item,
        string Type,
        string Quantity,
        string Unit);
}
