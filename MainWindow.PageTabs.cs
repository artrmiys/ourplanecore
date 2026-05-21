using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OurPlaneCore.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private const int MaxBatchSheetOpenCount = 64;
    private string _pagePreviewPrefetchJobRoot = "";
    private IReadOnlyList<PageInfo> _pagePreviewPrefetchPages = Array.Empty<PageInfo>();

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
            .Select(entry => OurPlaneCoreJobStore.TryReadPage(entry.SourcePath))
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
            : OurPlaneCoreJobStore.TryReadPage(tab.PageFolder);

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
        return distinct.Take(MaxBatchSheetOpenCount).ToList();
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
            : OurPlaneCoreJobStore.TryReadPage(tab.PageFolder);

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
        using PageOpenTrace? trace = BeginPageOpenTrace(page.Name);
        PageInfo viewportPage = OurPlaneCoreJobStore.TryReadPage(page.FolderPath) ?? page;
        _currentPage = viewportPage;
        _currentPdfPath = viewportPage.PdfPath;
        TxtStatusPage.Text = viewportPage.Name;
        _viewport.ScaleMetersPerPt = viewportPage.ScaleMetersPerPt;
        UpdateScaleUi(viewportPage.ScaleMetersPerPt);
        IReadOnlyList<TakeoffItem> scaledItems = ApplyScaleToCurrentPageMeasurements(viewportPage.ScaleMetersPerPt);
        trace?.Mark("read+scale");
        _viewport.LoadPage(
            viewportPage.PdfPath,
            viewportPage.PdfPage,
            viewportPage.FolderPath,
            viewportPage.PdfLayersCached ? viewportPage.PdfLayers : null,
            restoreView);
        trace?.Mark("decode");
        QueueNearbyPagePreviewPrefetch(viewportPage);
        ApplyViewportPageTakeoffVisibility(viewportPage);
        LoadSheetOverlay(viewportPage);
        _viewport.SetPageAnnotations(OurPlaneCoreJobStore.LoadPageAnnotations(viewportPage.FolderPath));
        ApplyRulerVisibilityToViewport();
        RefreshAiMarkersOverlay();
        RefreshThreeDRoofGuideOverlay();
        SelectPageTreeNodeSilently(viewportPage.FolderPath);
        _settings.LastPageFolder = viewportPage.FolderPath;
        if (_currentJob != null)
            _settings.LastJobPath = _currentJob.RootPath;
        SaveAppSettings();
        trace?.Mark("overlays+settings");

        bool autoLoaded = _currentJob == null && _takeoffItems.Count == 0 && TryAutoLoad();
        if (!autoLoaded)
            RefreshLoadedPageTakeoffVisuals(viewportPage.FolderPath, scaledItems);
        trace?.Mark("takeoff-refresh");
        RefreshFloatingPageSetup(viewportPage.FolderPath);
        ShowDuplicateSheetMeasurementHint(viewportPage);
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
            return OurPlaneCoreJobStore.DisplayName(pageFolder);

        try
        {
            return Path.GetRelativePath(_currentJob.PagesRoot, pageFolder);
        }
        catch
        {
            return OurPlaneCoreJobStore.DisplayName(pageFolder);
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
        if (_currentJob == null || string.IsNullOrWhiteSpace(activePage.FolderPath))
            return;

        IReadOnlyList<PageInfo> pages = CachedPagesForPreviewPrefetch();
        int activeIndex = -1;
        for (int i = 0; i < pages.Count; i++)
        {
            if (string.Equals(pages[i].FolderPath, activePage.FolderPath, StringComparison.OrdinalIgnoreCase))
            {
                activeIndex = i;
                break;
            }
        }

        if (activeIndex < 0)
            return;

        QueuePreviewPrefetchAt(pages, activeIndex + 1);
        QueuePreviewPrefetchAt(pages, activeIndex - 1);
        QueuePreviewPrefetchAt(pages, activeIndex + 2);
    }

    private IReadOnlyList<PageInfo> CachedPagesForPreviewPrefetch()
    {
        if (_currentJob == null)
            return Array.Empty<PageInfo>();

        if (string.Equals(_pagePreviewPrefetchJobRoot, _currentJob.RootPath, StringComparison.OrdinalIgnoreCase))
            return _pagePreviewPrefetchPages;

        _pagePreviewPrefetchJobRoot = _currentJob.RootPath;
        _pagePreviewPrefetchPages = Directory.Exists(_currentJob.PagesRoot)
            ? Directory.EnumerateFiles(_currentJob.PagesRoot, "source.json", SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .Where(folder => !string.IsNullOrWhiteSpace(folder))
                .Select(folder => OurPlaneCoreJobStore.TryReadPage(folder!))
                .Where(page => page != null && File.Exists(page.PdfPath))
                .Cast<PageInfo>()
                .OrderBy(page => page.FolderPath, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : Array.Empty<PageInfo>();
        return _pagePreviewPrefetchPages;
    }

    private static void QueuePreviewPrefetchAt(IReadOnlyList<PageInfo> pages, int index)
    {
        if (index < 0 || index >= pages.Count)
            return;

        PageInfo page = pages[index];
        PdfViewport.PrefetchPagePreview(page.PdfPath, page.PdfPage);
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

    private ContextMenu BuildPageTabContextMenu(PageTabState tab)
    {
        var menu = new ContextMenu();
        menu.Items.Add(MakeMenuItem("Detach Tab to Window", _currentJob != null, () => OpenPageTabsInDetachedWindows([tab], tileOnSecondMonitor: false)));
        menu.Items.Add(MakeMenuItem("Tile Tab on Monitor 2", _currentJob != null, () => OpenPageTabsInDetachedWindows([tab], tileOnSecondMonitor: true)));

        int tabCount = Math.Min(MaxBatchSheetOpenCount, _pageTabs.Count);
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem($"Detach All {tabCount} Tabs to Windows", _currentJob != null && _pageTabs.Count > 0, () => OpenPageTabsInDetachedWindows(_pageTabs.ToList(), tileOnSecondMonitor: false)));
        menu.Items.Add(MakeMenuItem($"Tile All {tabCount} Tabs on Monitor 2", _currentJob != null && _pageTabs.Count > 0, () => OpenPageTabsInDetachedWindows(_pageTabs.ToList(), tileOnSecondMonitor: true)));

        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Close Tab", true, () => ClosePageTab(tab)));
        return menu;
    }
}
