using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OurPlaneCore.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
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
        PageInfo viewportPage = OurPlaneCoreJobStore.TryReadPage(page.FolderPath) ?? page;
        _currentPage = viewportPage;
        _currentPdfPath = viewportPage.PdfPath;
        TxtStatusPage.Text = viewportPage.Name;
        _viewport.ScaleMetersPerPt = viewportPage.ScaleMetersPerPt;
        UpdateScaleUi(viewportPage.ScaleMetersPerPt);
        ApplyScaleToCurrentPageMeasurements(viewportPage.ScaleMetersPerPt);
        RefreshAllTotals();
        _viewport.LoadPage(
            viewportPage.PdfPath,
            viewportPage.PdfPage,
            viewportPage.FolderPath,
            viewportPage.PdfLayersCached ? viewportPage.PdfLayers : null,
            restoreView);
        ApplyViewportPageTakeoffVisibility(viewportPage);
        LoadSheetOverlay(viewportPage);
        _viewport.SetPageAnnotations(OurPlaneCoreJobStore.LoadPageAnnotations(viewportPage.FolderPath));
        RefreshAiMarkersOverlay();
        SelectPageTreeNodeSilently(viewportPage.FolderPath);
        _settings.LastPageFolder = viewportPage.FolderPath;
        if (_currentJob != null)
            _settings.LastJobPath = _currentJob.RootPath;
        SaveAppSettings();

        if (_takeoffItems.Count == 0)
            TryAutoLoad();
        ApplyTakeoffPageHighlights();
        RefreshFloatingPageSetup(viewportPage.FolderPath);
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
}
