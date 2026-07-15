using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OurPlanCore.Controls;

namespace OurPlanCore;

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
        BtnHighlight.Content = CreateStrokeIconLabel("IconHighlight", "Highlight", glyphBrush);
        BtnDrawLine.Content = CreateStrokeIconLabel("IconDrawLine", "Draw", glyphBrush);
        BtnDrawArrow.Content = CreateStrokeIconLabel("IconArrow", "Arrow", glyphBrush);
        BtnDrawRect.Content = CreateStrokeIconLabel("IconBox", "Box", glyphBrush);
        BtnDrawCloud.Content = CreateStrokeIconLabel("IconCloud", "Cloud", glyphBrush);
        BtnDrawAreaAnnot.Content = CreateStrokeIconLabel("IconAreaFill", "Area", glyphBrush);
        BtnNote.Content = CreateStrokeIconLabel("IconNote", "Note", glyphBrush);
        BtnPoint.Content = CreateToolGlyphLabel(MeasurementGlyphKind.Count, "Count", glyphBrush);
        BtnLine.Content = CreateToolGlyphLabel(MeasurementGlyphKind.Line, "Line", glyphBrush);
        BtnArea.Content = CreateToolGlyphLabel(MeasurementGlyphKind.Area, "Area", glyphBrush);
        BtnJoistArea.Content = CreateToolGlyphLabel(MeasurementGlyphKind.Joist, "J Area", glyphBrush);
        BtnBeam.Content = CreateToolGlyphLabel(MeasurementGlyphKind.Count, "Beam", glyphBrush);
        BtnOpenings.Content = CreateToolGlyphLabel(MeasurementGlyphKind.CountSquare, "Openings", glyphBrush);
        BuildAnnotationStyleControls();
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

    // Icon + label for the Annotation markup tools, using a single-stroke folder
    // /tool geometry from MainWindowResources.xaml (matches the ribbon icon set).
    private FrameworkElement CreateStrokeIconLabel(string geometryKey, string label, Brush glyphBrush)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Geometries live in MainWindowResources.xaml (merged into the Window,
        // not the Application), so look them up via the Window's resource chain.
        if (TryFindResource(geometryKey) is Geometry geometry)
        {
            row.Children.Add(new System.Windows.Shapes.Path
            {
                Data = geometry,
                Width = 14,
                Height = 14,
                Stretch = Stretch.Uniform,
                Stroke = glyphBrush,
                StrokeThickness = 1.35,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Fill = Brushes.Transparent,
                SnapsToDevicePixels = true,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0),
            });
        }
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

    private void BtnBlankJob_Click(object sender, RoutedEventArgs e)
    {
        CreateBlankJobFromDialog();
    }

    private bool OpenJob(string rootPath, string? initialPageFolder = null)
    {
        string normalizedRoot;
        try
        {
            normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        }
        catch (Exception ex)
        {
            ShowJobOpenError("The selected job path is invalid.", ex.Message);
            return false;
        }

        bool reloadCurrent = _currentJob != null && SameJobPath(_currentJob.RootPath, normalizedRoot);
        PendingJobAccess? pending = null;
        JobAccessSessionToken previousToken = default;
        JobLeaseService? previousLease = null;
        OurPlanCoreJob nextJob;

        if (reloadCurrent)
        {
            if (!PrepareCurrentJobForSwitch())
                return false;
            try
            {
                JobAccessMode reloadMode = IsCurrentJobWritable
                    ? JobAccessMode.Writable
                    : JobAccessMode.ReadOnly;
                nextJob = OurPlanCoreJobStore.LoadJob(normalizedRoot, reloadMode);
            }
            catch (Exception ex)
            {
                ShowJobOpenError("The current job could not be reloaded.", ex.Message);
                return false;
            }
        }
        else
        {
            if (!TryPrepareJobAccess(normalizedRoot, out pending) || pending == null)
                return false;
            try
            {
                nextJob = OurPlanCoreJobStore.LoadJob(normalizedRoot, pending.Mode);
            }
            catch (Exception ex)
            {
                pending.Dispose();
                ShowJobOpenError("The selected job could not be loaded.", ex.Message);
                return false;
            }

            if (!ConfirmPendingJobAccess(pending))
                return false;

            if (!PrepareCurrentJobForSwitch())
            {
                pending.Dispose();
                return false;
            }
            if (!ConfirmPendingJobAccess(pending))
                return false;

            previousToken = _currentJobAccessToken;
            previousLease = _currentJobLeaseService;
        }

        if (pending != null && !TryAdoptJobAccess(pending))
        {
            pending.Dispose();
            ShowJobOpenError(
                "Write ownership was lost before the job switch completed.",
                "The previous job remains open. Retry, or open the target read-only.");
            return false;
        }

        _currentJob = nextJob;
        ApplyCurrentJobAccessToUi();
        try
        {
        ApplyFolderTemplateProviders();
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
        _pagesWithHiddenRulers.Clear();
        _pageTabs.Clear();
        _activePageTab = null;
        RefreshPageTabs(null);
        _takeoffItems.Clear();
        _activeItem = null;
        _activeTakeoffParentFolder = "";
        TakeoffsTree.Items.Clear();
        ResetTakeoffTreeItemIndex();
        _viewport.SetMeasurements([]);
        _viewport.ClearPage();
        ApplyRulerVisibilityToViewport();
        RefreshJobHeaderLabels();
        Title = CurrentJobWindowTitle();
        ReloadPagesTree(_currentJob.PagesRoot);
        LoadPageBookmarksForJob();
        LoadTakeoffsForJob();
        LoadThreeDModelForCurrentJob();
        _settings.LastJobPath = _currentJob.RootPath;
        _settings.JobsRootPath = Path.GetDirectoryName(_currentJob.RootPath) ?? _settings.JobsRootPath;
        AppSettingsStore.AddJobsRoot(_settings, _settings.JobsRootPath);
        AppSettingsStore.AddRecentJob(_settings, _currentJob.RootPath, _currentJob.Name);
        SaveAppSettings();
        QueueRecentJobThumbnailGeneration(_currentJob);
        if (IsModuleEnabled(ModuleId.Ai))
            LoadPersistedMarkerVisibility();
        if (!IsTruthyEnvironment(TakeoffsMoveSmokeEnv) &&
            ResolveInitialPageToOpen(initialPageFolder) is { } pageToOpen)
            SelectPageByFolder(pageToOpen);
        QueueSheetLegendAutoSortSweep();
        ReportCorruptJsonFiles();
        ApplyTheme(string.Equals(_settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase), persist: false);
        RefreshJobHeaderLabels();
        UpdateNoJobOverlay();   // a job is now open — hide the empty-state Start card
        TxtStatus.Text = BuildMeasurementRepairStatus($"Loaded job: {_currentJob.Name}");
        if (IsCurrentJobWritable && IsModuleEnabled(ModuleId.Ai))
            LoadObservationsInbox();
        if (IsModuleEnabled(ModuleId.ThreeD))
            RefreshMassingDraftPanel();
        if (IsCurrentJobWritable && IsModuleEnabled(ModuleId.Ai))
        {
            string aiMaintenanceRoot = _currentJob.RootPath;
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    int reset = SmartContextStore.ResetStuckRunningRequests(aiMaintenanceRoot);
                    if (reset > 0)
                        AppLog.Info($"AI context maintenance: reset {reset} stuck 'running' request(s) to failed so they can be retried.");
                    (int archived, int failed) = SmartContextStore.ArchiveStaleRequestFiles(aiMaintenanceRoot);
                    if (archived > 0 || failed > 0)
                        AppLog.Info($"AI context maintenance: archived {archived} stale request/response file(s), {failed} failed.");
                    int prunedCrops = SmartContextStore.PruneOrphanCrops(aiMaintenanceRoot);
                    if (prunedCrops > 0)
                        AppLog.Info($"AI context maintenance: pruned {prunedCrops} orphaned crop image(s).");
                }
                catch (JobWriteDeniedException ex)
                {
                    AppLog.Info($"AI context maintenance stopped after write access changed: {ex.Message}");
                }
                catch (Exception ex)
                {
                    AppLog.Warn(ex, "AI context maintenance failed.");
                }
            });
        }
        Dispatcher.BeginInvoke(
            new Action(CollapseProjectTreeDisplays),
            System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
        finally
        {
            if (!reloadCurrent)
                ReleaseJobAccess(previousToken, previousLease, "switch jobs");
        }
        return true;
    }

    private void ReportCorruptJsonFiles()
    {
        IReadOnlyList<string> corruptFiles = OurPlanCoreJobStore.DrainCorruptJsonFiles();
        if (corruptFiles.Count == 0)
            return;

        string details = string.Join(Environment.NewLine, corruptFiles.Take(8));
        if (corruptFiles.Count > 8)
            details += $"{Environment.NewLine}...and {corruptFiles.Count - 8} more.";

        MessageBox.Show(
            $"Some project JSON files could not be read and were moved aside for manual recovery.{Environment.NewLine}{Environment.NewLine}{details}",
            "Project Data Warning",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
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

    private void RefreshJobHeaderLabels()
    {
        if (_currentJob == null)
            return;

        string displayName = CurrentJobDisplayName();
        TxtStatusJob.Text = displayName;
        TxtJobName.Text = displayName;
        Title = CurrentJobWindowTitle();
        UpdateStatusBarSegments();
        TxtStatusPage.Text = _currentPage?.Name ?? "—";
    }

    private string? ResolveInitialPageToOpen(string? initialPageFolder)
    {
        if (_currentJob == null)
            return null;

        if (!string.IsNullOrWhiteSpace(initialPageFolder) && Directory.Exists(initialPageFolder))
            return initialPageFolder;

        if (!string.IsNullOrWhiteSpace(_settings.LastPageFolder) &&
            Directory.Exists(_settings.LastPageFolder) &&
            OurPlanCoreJobStore.IsSameOrDescendant(_currentJob.PagesRoot, _settings.LastPageFolder))
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

    private bool TryAutoLoad()
    {
        if (_currentJob != null || string.IsNullOrWhiteSpace(_currentPdfPath))
            return false;

        var (scale, _, items) = ProjectFile.Restore(_currentPdfPath);
        if (items.Count == 0) return false;

        // Clear any stale in-session state first
        _takeoffItems.Clear();
        TakeoffsTree.Items.Clear();
        ResetTakeoffTreeItemIndex();
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
            double ratio = scale / ViewportConstants.PdfPointMeters;
            TxtScaleRatio.Text = PdfSheetMetadataService.FormatImperialScale(scale);
            TxtScaleInfo.Text  = $"≈1:{ratio:F0}";
        }

        // Select first item
        if (TakeoffsTree.Items.Count > 0)
            ((TreeViewItem)TakeoffsTree.Items[0]).IsSelected = true;

        RefreshAllTotals();
        TxtStatus.Text = $"Loaded {items.Count} item(s) from saved project.";
        return true;
    }

    private void LoadTakeoffsForJob()
    {
        if (_takeoffSaveService.HasPending &&
            !TryFlushTakeoffAutosaves("reload the Takeoffs tree"))
        {
            return;
        }

        TakeoffsTreeSelectionSnapshot selectionSnapshot = CaptureTakeoffsTreeSelectionState();

        if (_currentJob == null)
        {
            _takeoffItems.Clear();
            TakeoffsTree.Items.Clear();
            ResetTakeoffTreeItemIndex();
            _takeoffsRangeAnchorPath = null;
            _takeoffsMultiSelection.Clear();
            _takeoffSectionMultiSelection.Clear();
            _takeoffSectionRangeAnchorKey = null;
            _activeItem = null;
            _activeTakeoffParentFolder = "";
            _lastMeasurementPageFolderRepairCount = 0;
            _lastMeasurementPageFolderUnresolvedCount = 0;
            _expandedTakeoffTreePaths.Clear();
            _viewport.SetMeasurements([]);
            UpdateTotalDisplay();
            return;
        }

        var loadedItems = new List<TakeoffItem>();
        List<TreeViewItem> rootNodes;
        try
        {
            rootNodes = BuildTakeoffChildren(_currentJob.TakeoffsRoot, loadedItems);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, $"Load takeoffs tree failed for {_currentJob.TakeoffsRoot}; keeping existing tree.");
            TxtStatus.Text = "Takeoffs tree reload failed; existing tree was kept. See app log.";
            return;
        }

        _takeoffItems.Clear();
        _takeoffItems.AddRange(loadedItems);
        TakeoffsTree.Items.Clear();
        ResetTakeoffTreeItemIndex();
        foreach (TreeViewItem node in rootNodes)
            TakeoffsTree.Items.Add(node);
        RebuildTakeoffTreeItemIndex();

        _takeoffsRangeAnchorPath = null;
        _takeoffsMultiSelection.Clear();
        _takeoffSectionMultiSelection.Clear();
        _takeoffSectionRangeAnchorKey = null;
        _activeItem = null;
        _activeTakeoffParentFolder = _currentJob.TakeoffsRoot;

        try
        {
            _lastMeasurementPageFolderRepairCount = IsCurrentJobWritable
                ? RepairMeasurementPageFolderReferences()
                : 0;
            if (!IsCurrentJobWritable)
                _lastMeasurementPageFolderUnresolvedCount = 0;
            RestoreExpandedTreeState(TakeoffsTree, _expandedTakeoffTreePaths, GetTakeoffNodePath);

            _viewport.SetMeasurements(_takeoffItems.SelectMany(i => i.Measurements));

            bool restoredSelection = RestoreTakeoffsTreeSelectionState(selectionSnapshot);
            if (!restoredSelection)
            {
                if (FindFirstTakeoffTreeItem(TakeoffsTree) is { } firstItem)
                {
                    firstItem.IsSelected = true;
                }
                else
                {
                    _viewport.ActiveColor = "#FF4444";
                    _viewport.ActiveTakeoffFolder = "";
                }
            }

            PruneTakeoffsMultiSelection();
            PruneTakeoffSectionMultiSelection();
            ApplyTakeoffPageHighlights();
            ApplyTakeoffsTreeSearchFilter();
            RefreshAllTotals();
            AppLog.Info($"Loaded takeoffs tree with {_takeoffItems.Count} item(s) from {_currentJob.TakeoffsRoot}.");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, $"Takeoffs tree post-load refresh failed for {_currentJob.TakeoffsRoot}.");
            TxtStatus.Text = "Takeoffs loaded, but a refresh step failed. See app log.";
        }
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
        PageInfo? firstPage = pages
            .OrderBy(page => NormalizePath(page.FolderPath), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        int repaired = 0;
        int unresolved = 0;
        foreach (TakeoffItem item in _takeoffItems)
        {
            bool itemChanged = false;
            PageInfo? emptyItemFallback = item.Measurements.Count > 0 &&
                                          item.Measurements.All(measurement => string.IsNullOrWhiteSpace(measurement.PageFolder))
                ? firstPage
                : null;
            for (int index = 0; index < item.Measurements.Count; index++)
            {
                Measurement measurement = item.Measurements[index];
                PageInfo? matchedPage;
                if (string.IsNullOrWhiteSpace(measurement.PageFolder))
                {
                    matchedPage = emptyItemFallback ??
                                  InferNeighborMeasurementPage(
                                      item.Measurements,
                                      index,
                                      _currentJob.PagesRoot,
                                      pagesByPath,
                                      pagesByLeaf,
                                      pagesByPdfPage);
                    if (matchedPage != null)
                    {
                        measurement.PageFolder = matchedPage.FolderPath;
                        if (measurement.ScaleMetersPerPt <= 0)
                            measurement.ScaleMetersPerPt = matchedPage.ScaleMetersPerPt;
                        repaired++;
                        itemChanged = true;
                        AppLog.Info($"Repaired empty measurement PageFolder for {item.Name} -> {matchedPage.FolderPath}");
                        continue;
                    }

                    unresolved++;
                    continue;
                }

                string oldPath = NormalizePageReferencePath(measurement.PageFolder);
                matchedPage = ResolveMeasurementPage(
                    oldPath,
                    _currentJob.PagesRoot,
                    pagesByPath,
                    pagesByLeaf,
                    pagesByPdfPage);
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
                    AppLog.Info($"Repaired measurement PageFolder for {item.Name}: {oldPath} -> {matchedPage.FolderPath}");
                }
            }

            if (itemChanged)
                OurPlanCoreJobStore.SaveTakeoffItem(item);
        }

        _lastMeasurementPageFolderUnresolvedCount = unresolved;
        return repaired;
    }

    private PageInfo? InferNeighborMeasurementPage(
        IReadOnlyList<Measurement> measurements,
        int index,
        string pagesRoot,
        Dictionary<string, PageInfo> pagesByPath,
        Dictionary<string, List<PageInfo>> pagesByLeaf,
        Dictionary<int, List<PageInfo>> pagesByPdfPage)
    {
        for (int i = index - 1; i >= 0; i--)
        {
            if (TryResolveMeasurementPage(measurements[i].PageFolder, pagesRoot, pagesByPath, pagesByLeaf, pagesByPdfPage, out PageInfo page))
                return page;
        }

        for (int i = index + 1; i < measurements.Count; i++)
        {
            if (TryResolveMeasurementPage(measurements[i].PageFolder, pagesRoot, pagesByPath, pagesByLeaf, pagesByPdfPage, out PageInfo page))
                return page;
        }

        return null;
    }

    private bool TryResolveMeasurementPage(
        string? pageFolder,
        string pagesRoot,
        Dictionary<string, PageInfo> pagesByPath,
        Dictionary<string, List<PageInfo>> pagesByLeaf,
        Dictionary<int, List<PageInfo>> pagesByPdfPage,
        out PageInfo page)
    {
        page = null!;
        if (string.IsNullOrWhiteSpace(pageFolder))
            return false;

        PageInfo? resolved = ResolveMeasurementPage(
            NormalizePageReferencePath(pageFolder),
            pagesRoot,
            pagesByPath,
            pagesByLeaf,
            pagesByPdfPage);
        if (resolved == null)
            return false;

        page = resolved;
        return true;
    }

    private static PageInfo? ResolveMeasurementPage(
        string oldPath,
        string pagesRoot,
        Dictionary<string, PageInfo> pagesByPath,
        Dictionary<string, List<PageInfo>> pagesByLeaf,
        Dictionary<int, List<PageInfo>> pagesByPdfPage)
    {
        if (pagesByPath.TryGetValue(oldPath, out PageInfo? exactPage))
            return exactPage;

        if (ResolveMovedJobPage(oldPath, pagesRoot, pagesByPath) is { } movedPage)
            return movedPage;

        string leaf = Path.GetFileName(oldPath);
        if (!string.IsNullOrWhiteSpace(leaf) &&
            pagesByLeaf.TryGetValue(leaf, out List<PageInfo>? leafMatches) &&
            leafMatches.Count == 1)
        {
            return leafMatches[0];
        }

        return TryResolveLegacyImportedPage(leaf, pagesByPdfPage, out PageInfo legacyPage)
            ? legacyPage
            : null;
    }

    private static PageInfo? ResolveMovedJobPage(
        string oldPath,
        string pagesRoot,
        Dictionary<string, PageInfo> pagesByPath)
    {
        if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(pagesRoot))
            return null;

        if (!TryGetSuffixAfterPathSegment(oldPath, "Pages", out string[] suffixSegments) || suffixSegments.Length == 0)
            return null;

        string normalizedPagesRoot = NormalizePath(pagesRoot);
        string candidate = NormalizePath(Path.Combine([normalizedPagesRoot, .. suffixSegments]));
        return pagesByPath.TryGetValue(candidate, out PageInfo? page)
            ? page
            : null;
    }

    private static bool TryGetSuffixAfterPathSegment(string path, string segment, out string[] suffixSegments)
    {
        suffixSegments = [];
        string[] parts = path
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .ToArray();

        for (int i = parts.Length - 2; i >= 0; i--)
        {
            if (!string.Equals(parts[i], segment, StringComparison.OrdinalIgnoreCase))
                continue;

            suffixSegments = parts[(i + 1)..];
            return suffixSegments.Length > 0;
        }

        return false;
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

    private List<TreeViewItem> BuildTakeoffChildren(string parentFolder, List<TakeoffItem> loadedItems)
    {
        var nodes = new List<TreeViewItem>();
        foreach (string folder in OurPlanCoreJobStore.GetOrderedChildDirectories(parentFolder))
        {
            if (OurPlanCoreJobStore.TryReadTakeoffItem(folder) is { } item)
            {
                loadedItems.Add(item);
                nodes.Add(CreateTakeoffTreeItem(item));
            }
            else
            {
                var node = new TakeoffFolderNode
                {
                    Name = OurPlanCoreJobStore.DisplayName(folder),
                    FolderPath = folder,
                };
                var tvi = CreateTakeoffFolderTreeItem(node);
                foreach (TreeViewItem child in BuildTakeoffChildren(folder, loadedItems))
                    tvi.Items.Add(child);
                nodes.Add(tvi);
            }
        }

        return nodes;
    }

    private TreeViewItem AddTakeoffTreeItem(TakeoffItem item) =>
        AddTakeoffTreeItem(item, TakeoffsTree);

    private TreeViewItem AddTakeoffTreeItem(TakeoffItem item, ItemsControl parent)
    {
        var tvi = CreateTakeoffTreeItem(item);
        parent.Items.Add(tvi);
        RegisterTakeoffTreeItemSubtree(tvi);
        return tvi;
    }

    private TreeViewItem CreateTakeoffTreeItem(TakeoffItem item)
    {
        var tvi = new TreeViewItem { Tag = item };
        SetTreeItemHeader(tvi, item);
        AttachContextMenu(tvi, item);
        RefreshTakeoffSectionNodes(tvi, item);
        return tvi;
    }

    private TreeViewItem AddTakeoffFolderTreeItem(TakeoffFolderNode node, ItemsControl parent)
    {
        var tvi = CreateTakeoffFolderTreeItem(node);
        parent.Items.Add(tvi);
        RegisterTakeoffTreeItemSubtree(tvi);
        return tvi;
    }

    private TreeViewItem CreateTakeoffFolderTreeItem(TakeoffFolderNode node)
    {
        var tvi = new TreeViewItem { Tag = node };
        SetFolderTreeItemHeader(tvi, node);
        AttachFolderContextMenu(tvi, node);
        return tvi;
    }

}
