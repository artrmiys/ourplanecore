using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OurPlanCore.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    private const int MaxBatchSheetOpenCount = 64;
    private string _pagePreviewWarmupJobRoot = "";
    private string _pageRasterRefreshWarmupJobRoot = "";
    private string _lastNearbyPagePreviewPrefetchFolder = "";
    private int _pageOpenDeferredVersion;
    private PageTabState? _pendingPageTabDrag;
    private Point _pendingPageTabDragStart;

    private void InvalidatePagePreviewPrefetchCache()
    {
        _pagePreviewWarmupJobRoot = "";
        _pageRasterRefreshWarmupJobRoot = "";
        _lastNearbyPagePreviewPrefetchFolder = "";
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

    private void OpenPageByFolder(string folderPath)
    {
        if (_currentPage != null &&
            IsSamePageFolder(_currentPage.FolderPath, folderPath))
        {
            return;
        }

        PageInfo? page = OurPlanCoreJobStore.TryReadPage(folderPath);
        if (page == null)
        {
            TxtStatus.Text = $"Page no longer exists: {OurPlanCoreJobStore.DisplayName(folderPath)}.";
            return;
        }

        OpenPageInActiveTab(page);
    }

    private void OpenPageInNewTab(PageInfo page) =>
        OpenPagesInNewTabs([page], "Pages tree");

    private void OpenSelectedPagesInNewTabs(TreeViewItem anchor)
    {
        OpenPagesInNewTabs(SelectedPagesFromPagesTree(anchor), "Pages tree");
    }

    private void OpenSelectedPagesInDetachedWindows(
        TreeViewItem anchor,
        bool tileOnSecondMonitor,
        bool verticalStack = false)
    {
        if (!RequireModule(ModuleId.DetachedSheets, "Detached Sheets"))
            return;

        OpenPagesInDetachedWindows(
            SelectedPagesFromPagesTree(anchor),
            tileOnSecondMonitor,
            "Pages tree",
            verticalStack);
    }

    private void BtnPagesOpenTabs_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPagesTreeAnchor() is { } anchor)
            OpenSelectedPagesInNewTabs(anchor);
        else
            TxtStatus.Text = "Pages tree: select one or more sheets first.";
    }

    private void BtnPagesDetach_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPagesTreeAnchor() is { } anchor)
            OpenSelectedPagesInDetachedWindows(anchor, false);
        else
            TxtStatus.Text = "Pages tree: select one or more sheets first.";
    }

    private void BtnPagesTileSecondMonitor_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPagesTreeAnchor() is { } anchor)
            OpenSelectedPagesInDetachedWindows(anchor, true, TileM2VerticalLayoutEnabled);
        else
            TxtStatus.Text = "Pages tree: select one or more sheets first.";
    }

    private bool TileM2VerticalLayoutEnabled =>
        BtnPagesTileSecondMonitorVertical?.IsChecked == true;

    private void BtnPagesTileSecondMonitorVertical_Changed(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        TxtStatus.Text = TileM2VerticalLayoutEnabled
            ? "Tile M2 vertical layout is on."
            : "Tile M2 vertical layout is off.";
    }

    private TreeViewItem? SelectedPagesTreeAnchor()
    {
        if (PagesTree.SelectedItem is TreeViewItem selected &&
            GetPagesNodePath(selected) != null &&
            !IsRootPagesNode(selected))
        {
            return selected;
        }

        foreach (TreeViewItem item in EnumeratePageTreeItems())
        {
            string? path = GetPagesNodePath(item);
            if (path != null && _pagesMultiSelection.Contains(path))
                return item;
        }

        return null;
    }

    private IReadOnlyList<PageInfo> SelectedPagesFromPagesTree(TreeViewItem anchor) =>
        GetSelectedPageEntries(anchor)
            .Where(entry => entry.IsPage)
            .Select(entry => OurPlanCoreJobStore.TryReadPage(entry.SourcePath))
            .Where(page => page != null)
            .Cast<PageInfo>()
            .ToList();

    private void OpenPagesInNewTabs(IEnumerable<PageInfo> candidatePages, string sourceLabel)
    {
        IReadOnlyList<PageInfo> pages = DistinctBatchPages(candidatePages, out int totalDistinct);
        if (pages.Count == 0)
        {
            TxtStatus.Text = $"{sourceLabel}: select one or more sheets first.";
            return;
        }

        SaveCurrentPageScale();
        SaveActivePageTabViewState();

        PageTabState? lastTab = null;
        PageInfo? lastPage = null;
        int created = 0;
        int reused = 0;
        foreach (PageInfo page in pages)
        {
            if (FindPageTab(page.FolderPath) is { } existing)
            {
                existing.PageName = page.Name;
                lastTab = existing;
                reused++;
            }
            else
            {
                lastTab = new PageTabState(page.FolderPath, page.Name);
                _pageTabs.Add(lastTab);
                created++;
            }

            lastPage = page;
        }

        if (lastTab != null)
            ActivatePageTab(lastTab, lastPage);
        else
            RefreshPageTabs(_activePageTab);

        string cap = totalDistinct > pages.Count ? $" First {pages.Count} opened." : "";
        TxtStatus.Text = pages.Count == 1
            ? $"{sourceLabel}: opened {pages[0].Name} in a sheet tab."
            : $"{sourceLabel}: opened {pages.Count} sheet tab(s) ({created} new, {reused} already open).{cap}";
    }

    private void OpenPagesInDetachedWindows(
        IEnumerable<PageInfo> candidatePages,
        bool tileOnSecondMonitor,
        string sourceLabel,
        bool verticalStack = false)
    {
        if (!RequireModule(ModuleId.DetachedSheets, sourceLabel))
            return;

        if (_currentJob == null)
        {
            TxtStatus.Text = $"{sourceLabel}: open a job before detaching sheets.";
            return;
        }

        IReadOnlyList<PageInfo> pages = DistinctBatchPages(candidatePages, out int totalDistinct);
        if (pages.Count == 0)
        {
            TxtStatus.Text = $"{sourceLabel}: select one or more sheets first.";
            return;
        }

        UnitMode unitMode = _settings.UnitMode == UnitMode.Metric.ToString()
            ? UnitMode.Metric
            : UnitMode.Imperial;
        var opened = new List<Window>();
        foreach (PageInfo page in pages)
        {
            var window = new DetachedSheetWindow(_currentJob, page, _takeoffItems, _settings, unitMode)
            {
                Owner = this,
            };
            ConfigureDetachedSheetWindow(window, unitMode);
            window.Closed += (_, _) => _detachedSheetWindows.Remove(window);
            _detachedSheetWindows.Add(window);
            opened.Add(window);
            window.Show();
        }

        string target = "";
        if (tileOnSecondMonitor)
            target = DetachedSheetWindowLayout.TileOnSecondMonitorOrPrimary(opened, verticalStack);

        string cap = totalDistinct > pages.Count ? $" First {pages.Count} opened." : "";
        TxtStatus.Text = tileOnSecondMonitor
            ? $"{sourceLabel}: opened and tiled {opened.Count} detached sheet window(s){TileLayoutStatus(verticalStack)} on {target}.{cap}"
            : $"{sourceLabel}: opened {opened.Count} detached sheet window(s).{cap}";
    }

    private static string TileLayoutStatus(bool verticalStack) =>
        verticalStack ? " vertically" : "";

    private void CloseDetachedSheetsForModuleDisable()
    {
        foreach (DetachedSheetWindow window in _detachedSheetWindows.ToList())
            window.Close();
    }

    private void OpenPageTabsInDetachedWindows(IEnumerable<PageTabState> tabs, bool tileOnSecondMonitor)
    {
        var pages = tabs
            .Select(TryReadPageTabPage)
            .Where(page => page != null)
            .Cast<PageInfo>()
            .ToList();

        OpenPagesInDetachedWindows(pages, tileOnSecondMonitor, "Page tabs");
    }

    private static PageInfo? TryReadPageTabPage(PageTabState tab) =>
        string.IsNullOrWhiteSpace(tab.PageFolder)
            ? null
            : OurPlanCoreJobStore.TryReadPage(tab.PageFolder);

    private static IReadOnlyList<PageInfo> DistinctBatchPages(
        IEnumerable<PageInfo> candidatePages,
        out int totalDistinct)
    {
        var distinct = candidatePages
            .Where(page => !string.IsNullOrWhiteSpace(page.FolderPath))
            .GroupBy(page => NormalizePath(page.FolderPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        totalDistinct = distinct.Count;
        return distinct
            .Select(page => OurPlanCoreJobStore.TryReadPage(page.FolderPath))
            .Where(page => page != null)
            .Cast<PageInfo>()
            .Take(MaxBatchSheetOpenCount)
            .ToList();
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
        PageInfo? page = OurPlanCoreJobStore.TryReadPage(tab.PageFolder);
        if (page == null &&
            fallbackPage != null &&
            string.Equals(fallbackPage.FolderPath, tab.PageFolder, StringComparison.OrdinalIgnoreCase))
        {
            page = fallbackPage;
        }

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
        PageOpenTrace? trace = BeginPageOpenTrace(page.Name);
        PageInfo viewportPage = page;
        int deferredVersion = ++_pageOpenDeferredVersion;
        _currentPageAnnotationsLoaded = false;
        _currentPage = viewportPage;
        _currentPdfPath = viewportPage.PdfPath;
        TxtStatusPage.Text = viewportPage.Name;
        _viewport.ScaleMetersPerPt = viewportPage.ScaleMetersPerPt;
        UpdateScaleUi(viewportPage.ScaleMetersPerPt);
        trace?.Mark("read+scale");
        _viewport.LoadPage(
            viewportPage.PdfPath,
            viewportPage.PdfPage,
            viewportPage.FolderPath,
            viewportPage.PdfLayersCached ? viewportPage.PdfLayers : null,
            restoreView,
            viewportPage.RasterSheet,
            viewportPage.OverlayVisible && !string.IsNullOrWhiteSpace(viewportPage.OverlayPageFolder));
        trace?.Mark("decode");
        ApplyViewportPageTakeoffVisibilitySnapshot(viewportPage);
        trace?.Mark("visibility");
        LoadViewportPageAnnotations(viewportPage);
        trace?.Mark("annotations");
        QueueSheetOverlayLoadForPageOpen(viewportPage, restoreView);
        trace?.Mark("overlay-queued");
        _settings.LastPageFolder = viewportPage.FolderPath;
        if (_currentJob != null)
            _settings.LastJobPath = _currentJob.RootPath;
        QueueDeferredPageOpenWork(deferredVersion, viewportPage, trace, restoreView);
    }

    private void QueueDeferredPageOpenWork(
        int deferredVersion,
        PageInfo viewportPage,
        PageOpenTrace? trace,
        PdfViewport.ViewState? restoreView)
    {
        try
        {
            QueueNearbyPagePreviewPrefetchDeferred(deferredVersion, viewportPage);
            trace?.Mark("nearby-prefetch-queued");
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    RunDeferredPageOpenWorkWhenQuiet(
                        deferredVersion,
                        viewportPage,
                        trace,
                        restoreView);
                }),
                System.Windows.Threading.DispatcherPriority.Background);
        }
        catch
        {
            trace?.Dispose();
            throw;
        }
    }

    private async void RunDeferredPageOpenWorkWhenQuiet(
        int deferredVersion,
        PageInfo viewportPage,
        PageOpenTrace? trace,
        PdfViewport.ViewState? restoreView)
    {
        bool handedOff = false;
        try
        {
            if (!IsCurrentPageOpen(deferredVersion, viewportPage.FolderPath))
                return;

            trace?.Mark("deferred-start");
            await WaitForDeferredPageOpenQuietAsync(deferredVersion, viewportPage.FolderPath);
            if (!IsCurrentPageOpen(deferredVersion, viewportPage.FolderPath))
                return;

            trace?.Mark("deferred-quiet");
            handedOff = true;
            RunDeferredPageOpenWork(deferredVersion, viewportPage, trace, restoreView);
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Deferred page open work failed for {viewportPage.Name}");
        }
        finally
        {
            if (!handedOff)
                trace?.Dispose();
        }
    }

    private async Task WaitForDeferredPageOpenQuietAsync(int deferredVersion, string pageFolder)
    {
        TimeSpan quietWindow = TimeSpan.FromMilliseconds(ViewportRenderPolicy.PageOpenDeferredNavigationQuietMs);
        while (IsCurrentPageOpen(deferredVersion, pageFolder))
        {
            TimeSpan delay = _viewport.NavigationQuietDelay(quietWindow);
            if (delay <= TimeSpan.Zero)
                return;

            await Task.Delay(delay);
        }
    }

    private void RunDeferredPageOpenWork(
        int deferredVersion,
        PageInfo viewportPage,
        PageOpenTrace? trace,
        PdfViewport.ViewState? restoreView)
    {
        try
        {
            if (!IsCurrentPageOpen(deferredVersion, viewportPage.FolderPath))
                return;

            if (IsCurrentJobWritable)
            {
                QueueJobPagePreviewWarmupDeferred(deferredVersion, viewportPage);
                QueueJobRasterSheetRefreshWarmupDeferred(deferredVersion, viewportPage);
            }
            ApplyViewportPageTakeoffVisibility(viewportPage);
            RefreshAiMarkersOverlay();
            RefreshThreeDRoofGuideOverlay();
            SyncPageTreeNodeForViewportOpen(viewportPage.FolderPath);
            SaveAppSettings();
            trace?.Mark("overlays+settings");
            if (!IsCurrentPageOpen(deferredVersion, viewportPage.FolderPath))
                return;

            bool autoLoaded = _currentJob == null && _takeoffItems.Count == 0 && TryAutoLoad();
            if (!autoLoaded)
            {
                IReadOnlyList<TakeoffItem> scaledItems = IsCurrentJobWritable
                    ? ApplyScaleToCurrentPageMeasurements(viewportPage.ScaleMetersPerPt)
                    : [];
                RefreshLoadedPageTakeoffVisuals(viewportPage.FolderPath, scaledItems);
            }
            trace?.Mark("takeoff-refresh");
            if (!IsCurrentPageOpen(deferredVersion, viewportPage.FolderPath))
                return;

            RefreshFloatingPageSetup(viewportPage.FolderPath);
            ShowDuplicateSheetMeasurementHint(viewportPage);
            trace?.Mark("floating+hint");
        }
        finally
        {
            trace?.Dispose();
        }
    }

    private bool IsCurrentPageOpen(int deferredVersion, string pageFolder) =>
        deferredVersion == _pageOpenDeferredVersion &&
        _currentPage != null &&
        IsSamePageFolder(_currentPage.FolderPath, pageFolder);

    private void LoadViewportPageAnnotations(PageInfo viewportPage)
    {
        _viewport.SetPageAnnotations(OurPlanCoreJobStore.LoadPageAnnotations(viewportPage.FolderPath));
        _currentPageAnnotationsLoaded = true;
        ApplyRulerVisibilityToViewport();
    }

    private void QueueNearbyPagePreviewPrefetchDeferred(int deferredVersion, PageInfo viewportPage)
    {
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                try
                {
                    if (IsCurrentPageOpen(deferredVersion, viewportPage.FolderPath))
                        QueueNearbyPagePreviewPrefetch(viewportPage);
                }
                catch (Exception ex)
                {
                    AppLog.Warn(ex, $"Nearby page preview prefetch failed for {viewportPage.Name}");
                }
            }),
            System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private void QueueJobPagePreviewWarmupDeferred(int deferredVersion, PageInfo viewportPage)
    {
        // Whole-job sweep is opt-in: on big jobs it competes with interactive
        // sharpening for CPU/disk for many minutes after a job opens.
        if (_currentJob == null || !IsCurrentJobWritable || !_settings.BackgroundJobWarmupEnabled)
            return;

        string jobRoot = _currentJob.RootPath;
        if (string.Equals(_pagePreviewWarmupJobRoot, jobRoot, StringComparison.OrdinalIgnoreCase))
            return;

        _pagePreviewWarmupJobRoot = jobRoot;
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                try
                {
                    if (_currentJob == null ||
                        !string.Equals(_currentJob.RootPath, jobRoot, StringComparison.OrdinalIgnoreCase) ||
                        !IsCurrentPageOpen(deferredVersion, viewportPage.FolderPath))
                    {
                        return;
                    }

                    QueueJobPagePreviewWarmup(viewportPage);
                }
                catch (Exception ex)
                {
                    AppLog.Warn(ex, $"Job page preview warmup failed for {viewportPage.Name}");
                }
            }),
            System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private void QueueJobRasterSheetRefreshWarmupDeferred(int deferredVersion, PageInfo viewportPage)
    {
        if (_currentJob == null || !IsCurrentJobWritable || !_settings.BackgroundJobWarmupEnabled)
            return;

        string jobRoot = _currentJob.RootPath;
        if (string.Equals(_pageRasterRefreshWarmupJobRoot, jobRoot, StringComparison.OrdinalIgnoreCase))
            return;

        _pageRasterRefreshWarmupJobRoot = jobRoot;
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                try
                {
                    if (_currentJob == null ||
                        !string.Equals(_currentJob.RootPath, jobRoot, StringComparison.OrdinalIgnoreCase) ||
                        !IsCurrentPageOpen(deferredVersion, viewportPage.FolderPath))
                    {
                        return;
                    }

                    QueueJobRasterSheetRefreshWarmup(viewportPage);
                }
                catch (Exception ex)
                {
                    AppLog.Warn(ex, $"Job raster sheet refresh warmup failed for {viewportPage.Name}");
                }
            }),
            System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private void ShowDuplicateSheetMeasurementHint(PageInfo page)
    {
        if (_currentJob == null || _takeoffItems.Count == 0)
            return;

        if (CountMeasurementsForPage(page.FolderPath) > 0)
            return;

        string activeName = page.Name.Trim();
        string activeSheetKey = SheetCodeKey(activeName);
        var measuredMatches = EnumeratePageTreeItems()
            .Select(item => item.Tag as PageInfo)
            .Where(candidate => candidate != null &&
                                !IsSamePageFolder(candidate.FolderPath, page.FolderPath) &&
                                IsLikelySameSheet(candidate, activeName, activeSheetKey))
            .Select(candidate => new
            {
                Page = candidate!,
                Count = CountMeasurementsForPage(candidate!.FolderPath),
            })
            .Where(match => match.Count > 0)
            .OrderByDescending(match => match.Count)
            .ThenBy(match => match.Page.Name, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        if (measuredMatches.Count == 0)
            return;

        string targets = string.Join("; ", measuredMatches.Select(match =>
            $"{match.Count} on {RelativePagePath(match.Page.FolderPath)}"));
        TxtStatus.Text = $"No measurements on this sheet copy. Matching measured sheet(s): {targets}.";
    }

    private int CountMeasurementsForPage(string pageFolder) =>
        _takeoffItems.Sum(item => item.Measurements.Count(measurement =>
            IsSamePageFolder(measurement.PageFolder, pageFolder)));

    private static bool IsLikelySameSheet(PageInfo candidate, string activeName, string activeSheetKey)
    {
        if (string.Equals(candidate.Name.Trim(), activeName, StringComparison.OrdinalIgnoreCase))
            return true;

        return activeSheetKey.Length > 0 &&
               string.Equals(SheetCodeKey(candidate.Name), activeSheetKey, StringComparison.OrdinalIgnoreCase);
    }

    private static string SheetCodeKey(string sheetName)
    {
        Match match = Regex.Match(sheetName.Trim(), @"^[A-Za-z]+\d+(?:\.\d+)?");
        return match.Success
            ? match.Value.ToUpperInvariant()
            : "";
    }

    private string RelativePagePath(string pageFolder)
    {
        if (_currentJob == null)
            return OurPlanCoreJobStore.DisplayName(pageFolder);

        try
        {
            return Path.GetRelativePath(_currentJob.PagesRoot, pageFolder);
        }
        catch
        {
            return OurPlanCoreJobStore.DisplayName(pageFolder);
        }
    }

    private void RefreshLoadedPageTakeoffVisuals(string pageFolder, IReadOnlyList<TakeoffItem> scaledItems)
    {
        if (scaledItems.Count > 0)
        {
            bool previousSuppressFocus = _suppressCanvasFocusFromTakeoffSelection;
            _suppressCanvasFocusFromTakeoffSelection = true;
            try
            {
                foreach (TakeoffItem item in scaledItems.Distinct())
                    RefreshTreeItem(item);
            }
            finally
            {
                _suppressCanvasFocusFromTakeoffSelection = previousSuppressFocus;
            }
        }

        using (UsePageMeasurementLookup())
        {
            RefreshPageTakeoffIndicatorsForFolder(pageFolder);
            ApplyTakeoffPageHighlights();
            RefreshSheetLegend();
        }
        if (_estimateCurrentSheetOnlyBox?.IsChecked == true)
            RefreshEstimateTable();
        UpdateTotalDisplay();
    }

    private void QueueNearbyPagePreviewPrefetch(PageInfo activePage)
    {
        if (_currentJob == null || !IsCurrentJobWritable || string.IsNullOrWhiteSpace(activePage.FolderPath))
            return;

        string pagesRoot = _currentJob.PagesRoot;
        string previousPageFolder = _lastNearbyPagePreviewPrefetchFolder;
        _lastNearbyPagePreviewPrefetchFolder = activePage.FolderPath;
        _ = Task.Run(() =>
        {
            try
            {
                QueueNearbyPagePreviewPrefetch(
                    LoadPagesForPreviewPrefetch(pagesRoot),
                    activePage.FolderPath,
                    previousPageFolder);
            }
            catch (Exception ex)
            {
                AppLog.Warn(ex, $"Nearby page preview prefetch background load failed for {activePage.Name}");
            }
        });
    }

    private static void QueueNearbyPagePreviewPrefetch(IReadOnlyList<PageInfo> pages, string activePageFolder, string previousPageFolder)
    {
        int activeIndex = FindPreviewPrefetchPageIndex(pages, activePageFolder);

        if (activeIndex < 0)
            return;

        int direction = FindPreviewPrefetchDirection(pages, activeIndex, previousPageFolder);
        int forwardPreviewRadius = DirectionalPrefetchRadius(
            direction,
            side: 1,
            ViewportRenderPolicy.NearbyPagePreviewPrefetchRadius,
            ViewportRenderPolicy.NearbyPageDirectionalPreviewPrefetchRadius);
        int backwardPreviewRadius = DirectionalPrefetchRadius(
            direction,
            side: -1,
            ViewportRenderPolicy.NearbyPagePreviewPrefetchRadius,
            ViewportRenderPolicy.NearbyPageDirectionalPreviewPrefetchRadius);
        int forwardReadableRadius = DirectionalPrefetchRadius(
            direction,
            side: 1,
            ViewportRenderPolicy.NearbyPageReadableBasePrefetchRadius,
            ViewportRenderPolicy.NearbyPageDirectionalReadableBasePrefetchRadius);
        int backwardReadableRadius = DirectionalPrefetchRadius(
            direction,
            side: -1,
            ViewportRenderPolicy.NearbyPageReadableBasePrefetchRadius,
            ViewportRenderPolicy.NearbyPageDirectionalReadableBasePrefetchRadius);
        int maxOffset = Math.Max(
            Math.Max(forwardPreviewRadius, backwardPreviewRadius),
            Math.Max(
                Math.Max(forwardReadableRadius, backwardReadableRadius),
                ViewportRenderPolicy.NearbyPageCleanRenderPrefetchRadius));

        for (int offset = 1; offset <= maxOffset; offset++)
        {
            if (offset <= forwardPreviewRadius)
                QueuePreviewPrefetchAt(pages, activeIndex + offset);
            if (offset <= backwardPreviewRadius)
                QueuePreviewPrefetchAt(pages, activeIndex - offset);

            if (offset <= forwardReadableRadius)
                QueueReadableBasePrefetchAt(pages, activeIndex + offset);
            if (offset <= backwardReadableRadius)
                QueueReadableBasePrefetchAt(pages, activeIndex - offset);

            if (offset <= ViewportRenderPolicy.NearbyPageCleanRenderPrefetchRadius)
            {
                QueueCleanRenderPrefetchAt(pages, activeIndex + offset);
                QueueCleanRenderPrefetchAt(pages, activeIndex - offset);
            }
        }
    }

    private static int FindPreviewPrefetchDirection(
        IReadOnlyList<PageInfo> pages,
        int activeIndex,
        string previousPageFolder)
    {
        int previousIndex = FindPreviewPrefetchPageIndex(pages, previousPageFolder);
        if (previousIndex < 0 || previousIndex == activeIndex)
            return 0;

        return activeIndex > previousIndex ? 1 : -1;
    }

    private static int DirectionalPrefetchRadius(
        int direction,
        int side,
        int defaultRadius,
        int directionalRadius) =>
        direction == side ? Math.Max(defaultRadius, directionalRadius) : defaultRadius;

    private void QueueJobPagePreviewWarmup(PageInfo activePage)
    {
        if (_currentJob == null || !IsCurrentJobWritable || string.IsNullOrWhiteSpace(activePage.FolderPath))
            return;

        string pagesRoot = _currentJob.PagesRoot;
        _ = Task.Run(() =>
        {
            try
            {
                QueueJobPagePreviewWarmup(
                    LoadPagesForPreviewPrefetch(pagesRoot),
                    activePage.FolderPath);
            }
            catch (Exception ex)
            {
                AppLog.Warn(ex, $"Job page preview warmup background load failed for {activePage.Name}");
            }
        });
    }

    private static void QueueJobPagePreviewWarmup(IReadOnlyList<PageInfo> pages, string activePageFolder)
    {
        if (pages.Count == 0)
            return;

        int activeIndex = FindPreviewPrefetchPageIndex(pages, activePageFolder);

        int previewWarmupCount = ViewportRenderPolicy.SelectJobOpenPreviewWarmupCount(pages.Count);
        int rasterWarmupCount = ViewportRenderPolicy.SelectJobOpenRasterSheetBitmapWarmupCount(pages.Count);
        HashSet<int> rasterWarmupIndexes = BuildLocalPageWarmupOrder(pages.Count, activeIndex)
            .Take(rasterWarmupCount)
            .ToHashSet();
        foreach (int index in BuildPreviewWarmupOrder(pages.Count, activeIndex, previewWarmupCount)
                     .Take(previewWarmupCount))
        {
            bool includeRasterSheetWarmup = rasterWarmupIndexes.Contains(index);
            QueuePreviewPrefetchAt(
                pages,
                index,
                ViewportRenderPolicy.ColdPageSwitchPreviewRenderScale,
                includeRasterSheetWarmup: includeRasterSheetWarmup,
                includeRasterSheetRefresh: false);
        }
    }

    private void QueueJobRasterSheetRefreshWarmup(PageInfo activePage)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(activePage.FolderPath))
            return;

        string pagesRoot = _currentJob.PagesRoot;
        _ = Task.Run(() =>
        {
            try
            {
                QueueJobRasterSheetRefreshWarmup(
                    LoadPagesForPreviewPrefetch(pagesRoot),
                    activePage.FolderPath);
            }
            catch (Exception ex)
            {
                AppLog.Warn(ex, $"Job raster sheet refresh warmup background load failed for {activePage.Name}");
            }
        });
    }

    private static void QueueJobRasterSheetRefreshWarmup(IReadOnlyList<PageInfo> pages, string activePageFolder)
    {
        if (pages.Count == 0)
            return;

        int activeIndex = FindPreviewPrefetchPageIndex(pages, activePageFolder);

        IReadOnlyList<PageInfo> queuedPages = BuildLocalPageWarmupOrder(pages.Count, activeIndex)
            .Take(ViewportRenderPolicy.SelectJobOpenRasterSheetRefreshWarmupCount(pages.Count))
            .Select(index => pages[index])
            .ToList();
        if (queuedPages.Count == 0)
            return;

        QueueJobRasterSheetRefreshWarmup(queuedPages);
    }

    private static void QueueJobRasterSheetRefreshWarmup(IReadOnlyList<PageInfo> pages)
    {
        foreach (PageInfo page in pages)
            PdfViewport.PrefetchRasterSheetRefresh(page);
    }

    private static int FindPreviewPrefetchPageIndex(IReadOnlyList<PageInfo> pages, string activePageFolder)
    {
        for (int i = 0; i < pages.Count; i++)
        {
            if (string.Equals(pages[i].FolderPath, activePageFolder, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static void QueuePreviewPrefetchAt(
        IReadOnlyList<PageInfo> pages,
        int index,
        float renderScale = ViewportRenderPolicy.FastPageSwitchPreviewRenderScale,
        bool includeRasterSheetWarmup = true,
        bool includeRasterSheetRefresh = true)
    {
        if (index < 0 || index >= pages.Count)
            return;

        PageInfo page = pages[index];
        bool hasReadyStaticRaster = StaticRasterPrefetchPolicy.HasReadyPageOpenRaster(page);
        if (!hasReadyStaticRaster)
            PdfViewport.PrefetchPagePreview(page.PdfPath, page.PdfPage, renderScale);

        if (includeRasterSheetWarmup)
        {
            PdfViewport.PrefetchRasterSheetBitmap(page);
            if (!hasReadyStaticRaster)
                PdfViewport.PrefetchRasterSheetWorkZoomBitmaps(page);
        }

        if (includeRasterSheetRefresh)
            PdfViewport.PrefetchRasterSheetRefresh(page);
    }

    private static void QueueReadableBasePrefetchAt(IReadOnlyList<PageInfo> pages, int index)
    {
        if (index < 0 || index >= pages.Count)
            return;

        PageInfo page = pages[index];
        if (StaticRasterPrefetchPolicy.HasReadyPageOpenRaster(page))
            return;

        PdfViewport.PrefetchPagePreview(
            page.PdfPath,
            page.PdfPage,
            ViewportRenderPolicy.ResponsiveMinRenderScale,
            preferCachedRenderImmediately: true);
    }

    private static void QueueCleanRenderPrefetchAt(IReadOnlyList<PageInfo> pages, int index)
    {
        if (index < 0 || index >= pages.Count)
            return;

        PageInfo page = pages[index];
        if (StaticRasterPrefetchPolicy.HasReadyPageOpenRaster(page))
            return;

        PdfViewport.PrefetchCleanLayerRender(
            page.PdfPath,
            page.PdfPage,
            page.PdfLayersCached ? page.PdfLayers : null);
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
        ApplyRulerVisibilityToViewport();
        RefreshFloatingPageSetup();
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
                    ContextMenu = BuildPageTabContextMenu(tab),
                    AllowDrop = true,
                };
                item.PreviewMouseLeftButtonDown += PageTab_PreviewMouseLeftButtonDown;
                item.PreviewMouseLeftButtonUp += PageTab_PreviewMouseLeftButtonUp;
                item.PreviewMouseMove += PageTab_PreviewMouseMove;
                item.DragOver += PageTab_DragOver;
                item.Drop += PageTab_Drop;
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

    private void PageTab_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            FindVisualAncestor<Button>(source) != null)
        {
            _pendingPageTabDrag = null;
            return;
        }

        _pendingPageTabDrag = sender is TabItem { Tag: PageTabState tab } ? tab : null;
        _pendingPageTabDragStart = e.GetPosition(PageTabs);
        e.Handled = _pendingPageTabDrag != null;
    }

    private void PageTab_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TabItem { Tag: PageTabState tab } ||
            !ReferenceEquals(_pendingPageTabDrag, tab))
        {
            return;
        }

        _pendingPageTabDrag = null;
        ActivatePageTab(tab);
        e.Handled = true;
    }

    private void PageTab_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pendingPageTabDrag == null)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _pendingPageTabDrag = null;
            return;
        }

        Point position = e.GetPosition(PageTabs);
        if (Math.Abs(position.X - _pendingPageTabDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _pendingPageTabDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        PageTabState dragged = _pendingPageTabDrag;
        _pendingPageTabDrag = null;
        DragDrop.DoDragDrop(PageTabs, dragged, DragDropEffects.Move);
    }

    private void PageTab_DragOver(object sender, DragEventArgs e) =>
        UpdatePageTabDragEffects(e);

    private void PageTabs_DragOver(object sender, DragEventArgs e) =>
        UpdatePageTabDragEffects(e);

    private void UpdatePageTabDragEffects(DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(PageTabState))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void PageTab_Drop(object sender, DragEventArgs e)
    {
        if (!TryGetDraggedPageTab(e, out PageTabState? dragged) || dragged == null)
            return;

        if (sender is not TabItem { Tag: PageTabState targetTab } targetItem)
        {
            PageTabs_Drop(sender, e);
            return;
        }

        MovePageTab(dragged, targetTab, e.GetPosition(targetItem).X > targetItem.ActualWidth / 2);
        e.Handled = true;
    }

    private void PageTabs_Drop(object sender, DragEventArgs e)
    {
        if (!TryGetDraggedPageTab(e, out PageTabState? dragged) || dragged == null)
            return;

        if (e.OriginalSource is DependencyObject source &&
            FindVisualAncestor<TabItem>(source) != null)
        {
            return;
        }

        DetachPageTabFromDrag(dragged);
        e.Handled = true;
    }

    private static bool TryGetDraggedPageTab(DragEventArgs e, out PageTabState? tab)
    {
        tab = e.Data.GetData(typeof(PageTabState)) as PageTabState;
        if (tab == null)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return false;
        }

        e.Effects = DragDropEffects.Move;
        return true;
    }

    private void MovePageTab(PageTabState dragged, PageTabState target, bool insertAfter)
    {
        int oldIndex = _pageTabs.IndexOf(dragged);
        int targetIndex = _pageTabs.IndexOf(target);
        if (oldIndex < 0 || targetIndex < 0 || ReferenceEquals(dragged, target))
            return;

        SaveActivePageTabViewState();
        _pageTabs.RemoveAt(oldIndex);
        if (oldIndex < targetIndex)
            targetIndex--;

        int insertIndex = insertAfter ? targetIndex + 1 : targetIndex;
        insertIndex = Math.Clamp(insertIndex, 0, _pageTabs.Count);
        _pageTabs.Insert(insertIndex, dragged);
        RefreshPageTabs(_activePageTab);
        TxtStatus.Text = $"Moved sheet tab: {dragged.PageName}.";
    }

    private void DetachPageTabFromDrag(PageTabState tab)
    {
        if (!RequireModule(ModuleId.DetachedSheets, "Detach Sheet"))
            return;

        if (_currentJob == null)
        {
            TxtStatus.Text = "Page tabs: open a job before detaching sheets.";
            return;
        }

        SaveActivePageTabViewState();
        OpenPageTabsInDetachedWindows([tab], tileOnSecondMonitor: false);
        ClosePageTab(tab);
    }

    private static T? FindVisualAncestor<T>(DependencyObject source)
        where T : DependencyObject
    {
        DependencyObject? current = source;
        while (current != null)
        {
            if (current is T match)
                return match;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
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

    private ContextMenu BuildPageTabContextMenu(PageTabState tab)
    {
        var menu = new ContextMenu();
        if (IsModuleEnabled(ModuleId.DetachedSheets))
        {
            menu.Items.Add(MakeMenuItem("Detach Tab to Window", _currentJob != null, () => OpenPageTabsInDetachedWindows([tab], tileOnSecondMonitor: false)));
            menu.Items.Add(MakeMenuItem("Tile Tab on Monitor 2", _currentJob != null, () => OpenPageTabsInDetachedWindows([tab], tileOnSecondMonitor: true)));

            int tabCount = Math.Min(MaxBatchSheetOpenCount, _pageTabs.Count);
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem($"Detach All {tabCount} Tabs to Windows", _currentJob != null && _pageTabs.Count > 0, () => OpenPageTabsInDetachedWindows(_pageTabs.ToList(), tileOnSecondMonitor: false)));
            menu.Items.Add(MakeMenuItem($"Tile All {tabCount} Tabs on Monitor 2", _currentJob != null && _pageTabs.Count > 0, () => OpenPageTabsInDetachedWindows(_pageTabs.ToList(), tileOnSecondMonitor: true)));
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(MakeMenuItem("Close Tab", true, () => ClosePageTab(tab)));
        return menu;
    }
}
