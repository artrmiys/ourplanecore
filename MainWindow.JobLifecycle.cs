using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SmartTakeoffs.Controls;

namespace SmartTakeoffs;

public partial class MainWindow
{
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
            TxtScaleRatio.Text = PdfSheetMetadataService.FormatImperialScale(scale);
            ratio = 0;
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
}
