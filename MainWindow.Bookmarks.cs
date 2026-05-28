using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using OurPlaneCore.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private readonly List<PageBookmark> _pageBookmarks = [];
    private ListView? _bookmarkList;
    private TextBlock? _bookmarkStatusText;
    private Button? _bookmarkOpenButton;
    private Button? _bookmarkRenameButton;
    private Button? _bookmarkDeleteButton;
    private ToggleButton? _bookmarksTabDockToggle;
    private TabItem? _bookmarksTab;
    private FrameworkElement? _bookmarkPanel;
    private GridLength _bookmarksDockRowHeight = new(190);
    private bool _syncingBookmarkSelection;
    private bool _syncingBookmarkDockToggle;

    private void InitializeBookmarksTab()
    {
        if (PagesSideTabs == null)
            return;

        BtnDockBookmarksBelowPages.ToolTip =
            $"Show Bookmarks as a separate panel below Pages; {KeyboardShortcutKeys.DualLayoutDisplay("bk")} adds a bookmark.";

        _bookmarkList = new ListView
        {
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ItemContainerStyle = BuildBookmarkListItemStyle(),
            View = new GridView
            {
                ColumnHeaderContainerStyle = BuildHiddenBookmarkColumnHeaderStyle(),
                Columns =
                {
                    new GridViewColumn { Header = "Name", Width = 94, DisplayMemberBinding = new Binding(nameof(PageBookmarkRow.Name)) },
                    new GridViewColumn { Header = "Page", Width = 58, DisplayMemberBinding = new Binding(nameof(PageBookmarkRow.Page)) },
                    new GridViewColumn { Header = "View", Width = 44, DisplayMemberBinding = new Binding(nameof(PageBookmarkRow.View)) },
                },
            },
        };
        _bookmarkList.SelectionChanged += BookmarkList_SelectionChanged;
        _bookmarkList.MouseDoubleClick += (_, _) => OpenSelectedBookmark();
        _bookmarkList.KeyDown += BookmarkList_KeyDown;

        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        toolbar.Children.Add(BookmarkButton("Add", BtnBookmarkAdd_Click, "Save the current page and zoom as a bookmark"));
        _bookmarkOpenButton = BookmarkButton("Open", BtnBookmarkOpen_Click, "Open the selected bookmark view");
        _bookmarkRenameButton = BookmarkButton("Rename", BtnBookmarkRename_Click, "Rename the selected bookmark");
        _bookmarkDeleteButton = BookmarkButton("Delete", BtnBookmarkDelete_Click, "Delete the selected bookmark");
        toolbar.Children.Add(_bookmarkOpenButton);
        toolbar.Children.Add(_bookmarkRenameButton);
        toolbar.Children.Add(_bookmarkDeleteButton);
        toolbar.Children.Add(BookmarkButton("Refresh", BtnBookmarkRefresh_Click, "Reload bookmarks from the job"));

        _bookmarkStatusText = new TextBlock
        {
            Text = "No bookmarks.",
            FontSize = 10,
            Foreground = System.Windows.Media.Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 2),
        };

        var panel = new DockPanel { Margin = new Thickness(4) };
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(_bookmarkStatusText, Dock.Top);
        panel.Children.Add(toolbar);
        panel.Children.Add(_bookmarkStatusText);
        panel.Children.Add(_bookmarkList);
        _bookmarkPanel = panel;

        _bookmarksTab = new TabItem
        {
            Header = BuildBookmarksTabHeader(),
            Content = panel,
        };
        PagesSideTabs.Items.Add(_bookmarksTab);
        RefreshBookmarkList();
    }

    private FrameworkElement BuildBookmarksTabHeader()
    {
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.Children.Add(new TextBlock
        {
            Text = "Bkm",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
        });

        _bookmarksTabDockToggle = CreateBookmarkDockToggle("Dock Bookmarks below Pages");
        header.Children.Add(_bookmarksTabDockToggle);
        return header;
    }

    private ToggleButton CreateBookmarkDockToggle(string tooltip)
    {
        var toggle = new ToggleButton
        {
            Style = (Style)FindResource("BookmarkDockToggleButton"),
            ToolTip = tooltip,
        };
        toggle.Checked += BookmarkDockToggle_Changed;
        toggle.Unchecked += BookmarkDockToggle_Changed;
        return toggle;
    }

    private void BookmarkDockToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingBookmarkDockToggle || sender is not ToggleButton toggle)
            return;

        ApplyBookmarksDockMode(toggle.IsChecked == true);
    }

    private void ApplyBookmarksDockMode(bool docked)
    {
        if (_bookmarksTab == null || _bookmarkPanel == null)
            return;

        SetBookmarksDockToggleState(docked);

        if (docked)
        {
            if (PagesSideTabs.SelectedItem == _bookmarksTab)
                PagesSideTabs.SelectedIndex = 0;

            if (PagesSideTabs.Items.Contains(_bookmarksTab))
            {
                _bookmarksTab.Content = null;
                PagesSideTabs.Items.Remove(_bookmarksTab);
            }

            BookmarksDockContentHost.Content = _bookmarkPanel;
            BookmarksDockSplitter.Visibility = Visibility.Visible;
            BookmarksDockPanel.Visibility = Visibility.Visible;
            BookmarksDockSplitterRow.Height = new GridLength(4);
            BookmarksDockRow.MinHeight = 120;
            BookmarksDockRow.Height = _bookmarksDockRowHeight.Value > 0
                ? _bookmarksDockRowHeight
                : new GridLength(190);
            TxtStatus.Text = "Bookmarks docked below Pages.";
            return;
        }

        if (BookmarksDockRow.ActualHeight >= 80)
            _bookmarksDockRowHeight = new GridLength(BookmarksDockRow.ActualHeight);

        BookmarksDockContentHost.Content = null;
        if (!PagesSideTabs.Items.Contains(_bookmarksTab))
        {
            _bookmarksTab.Content = _bookmarkPanel;
            PagesSideTabs.Items.Add(_bookmarksTab);
        }
        PagesSideTabs.SelectedItem = _bookmarksTab;

        BookmarksDockPanel.Visibility = Visibility.Collapsed;
        BookmarksDockSplitter.Visibility = Visibility.Collapsed;
        BookmarksDockSplitterRow.Height = new GridLength(0);
        BookmarksDockRow.MinHeight = 0;
        BookmarksDockRow.Height = new GridLength(0);
        TxtStatus.Text = "Bookmarks returned to the Pages tabs.";
    }

    private void SetBookmarksDockToggleState(bool docked)
    {
        _syncingBookmarkDockToggle = true;
        try
        {
            if (_bookmarksTabDockToggle != null)
                _bookmarksTabDockToggle.IsChecked = docked;
            BtnDockBookmarksBelowPages.IsChecked = docked;
        }
        finally
        {
            _syncingBookmarkDockToggle = false;
        }
    }

    private static Style BuildBookmarkListItemStyle()
    {
        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 1, 2, 1)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, System.Windows.Media.Brushes.Transparent));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("ControlForegroundBrush")));

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("ControlHoverBackgroundBrush")));
        style.Triggers.Add(hover);

        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("ControlActiveBackgroundBrush")));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("ControlActiveForegroundBrush")));
        style.Triggers.Add(selected);

        return style;
    }

    private static Style BuildHiddenBookmarkColumnHeaderStyle()
    {
        var style = new Style(typeof(GridViewColumnHeader));
        style.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
        style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 0.0));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        return style;
    }

    private static Button BookmarkButton(string text, RoutedEventHandler click, string tooltip)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(4, 2, 4, 2),
            Margin = new Thickness(0, 0, 4, 4),
            FontSize = 10,
            ToolTip = tooltip,
        };
        button.Click += click;
        return button;
    }

    private void LoadPageBookmarksForJob()
    {
        _pageBookmarks.Clear();
        if (_currentJob != null)
            _pageBookmarks.AddRange(OurPlaneCoreJobStore.LoadPageBookmarks(_currentJob));

        RefreshBookmarkList();
    }

    private void RefreshBookmarkList(string? selectBookmarkId = null)
    {
        if (_bookmarkList == null)
            return;

        var rows = _pageBookmarks
            .Select((bookmark, index) => new PageBookmarkRow(
                bookmark.Name,
                BookmarkPageName(bookmark),
                FormatBookmarkView(bookmark),
                index + 1,
                bookmark))
            .ToList();

        _syncingBookmarkSelection = true;
        try
        {
            _bookmarkList.ItemsSource = rows;
            if (!string.IsNullOrWhiteSpace(selectBookmarkId))
                _bookmarkList.SelectedItem = rows.FirstOrDefault(row =>
                    string.Equals(row.Bookmark.Id, selectBookmarkId, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _syncingBookmarkSelection = false;
        }

        if (_bookmarkStatusText != null)
        {
            _bookmarkStatusText.Text = _currentJob == null
                ? "Open a job to use bookmarks."
                : rows.Count == 0
                    ? "No bookmarks."
                    : $"{rows.Count.ToString(CultureInfo.InvariantCulture)} bookmark(s).";
        }

        UpdateBookmarkButtons();
    }

    private string BookmarkPageName(PageBookmark bookmark)
    {
        PageInfo? page = OurPlaneCoreJobStore.TryReadPage(bookmark.PageFolder);
        return page?.Name ?? (string.IsNullOrWhiteSpace(bookmark.PageName) ? "Missing" : bookmark.PageName);
    }

    private static string FormatBookmarkView(PageBookmark bookmark) =>
        bookmark.Zoom > 0
            ? $"{Math.Round(bookmark.Zoom * 100).ToString(CultureInfo.InvariantCulture)}%"
            : "view";

    private void UpdateBookmarkButtons()
    {
        bool hasSelection = SelectedBookmark() != null;
        if (_bookmarkOpenButton != null)
            _bookmarkOpenButton.IsEnabled = hasSelection;
        if (_bookmarkRenameButton != null)
            _bookmarkRenameButton.IsEnabled = hasSelection;
        if (_bookmarkDeleteButton != null)
            _bookmarkDeleteButton.IsEnabled = hasSelection;
    }

    private PageBookmark? SelectedBookmark() =>
        _bookmarkList?.SelectedItem is PageBookmarkRow row ? row.Bookmark : null;

    private void BookmarkList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateBookmarkButtons();
        if (_syncingBookmarkSelection || SelectedBookmark() == null)
            return;

        OpenSelectedBookmark();
    }

    private void BookmarkList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            OpenSelectedBookmark();
        }
        else if (e.Key == Key.Delete)
        {
            e.Handled = true;
            DeleteSelectedBookmark();
        }
    }

    private void BtnBookmarkAdd_Click(object sender, RoutedEventArgs e)
    {
        AddCurrentPageBookmark(promptForName: true);
    }

    private void AddBookmarkFromShortcut()
    {
        AddCurrentPageBookmark(promptForName: true);
    }

    private void AddCurrentPageBookmark(bool promptForName)
    {
        if (_currentJob == null || _currentPage == null)
        {
            TxtStatus.Text = "Open a page before adding a bookmark.";
            return;
        }

        string defaultName = $"{_currentPage.Name} view";
        string name = UniqueBookmarkName(defaultName);
        if (promptForName)
        {
            var dialog = new PageBookmarkDialog("Add Bookmark", name)
            {
                Owner = this,
            };
            if (dialog.ShowDialog() != true)
                return;
            name = dialog.BookmarkName;
        }

        PdfViewport.ViewState view = _viewport.CaptureViewState();
        string now = DateTime.UtcNow.ToString("O");
        var bookmark = new PageBookmark
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            PageFolder = _currentPage.FolderPath,
            PageName = _currentPage.Name,
            Zoom = view.Zoom,
            PanX = view.PanX,
            PanY = view.PanY,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        _pageBookmarks.Add(bookmark);
        SavePageBookmarks();
        RefreshBookmarkList(bookmark.Id);
        TxtStatus.Text = promptForName
            ? $"Added bookmark '{bookmark.Name}'."
            : $"Added bookmark '{bookmark.Name}' ({KeyboardShortcutKeys.DualLayoutDisplay("bk")}).";
    }

    private void BtnBookmarkOpen_Click(object sender, RoutedEventArgs e) =>
        OpenSelectedBookmark();

    private void BtnBookmarkRename_Click(object sender, RoutedEventArgs e)
    {
        PageBookmark? bookmark = SelectedBookmark();
        if (bookmark == null)
            return;

        var dialog = new PageBookmarkDialog("Rename Bookmark", bookmark.Name)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        bookmark.Name = dialog.BookmarkName;
        bookmark.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
        SavePageBookmarks();
        RefreshBookmarkList(bookmark.Id);
        TxtStatus.Text = $"Renamed bookmark to '{bookmark.Name}'.";
    }

    private void BtnBookmarkDelete_Click(object sender, RoutedEventArgs e) =>
        DeleteSelectedBookmark();

    private void BtnBookmarkRefresh_Click(object sender, RoutedEventArgs e)
    {
        LoadPageBookmarksForJob();
        TxtStatus.Text = "Bookmarks refreshed.";
    }

    private void OpenSelectedBookmark()
    {
        PageBookmark? bookmark = SelectedBookmark();
        if (bookmark != null)
            OpenPageBookmark(bookmark);
    }

    private void OpenPageBookmark(PageBookmark bookmark)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before opening bookmarks.";
            return;
        }

        PageInfo? page = OurPlaneCoreJobStore.TryReadPage(bookmark.PageFolder);
        if (page == null)
        {
            TxtStatus.Text = $"Bookmark page is missing: {bookmark.Name}.";
            return;
        }

        SaveCurrentPageScale();
        SaveActivePageTabViewState();

        PageTabState? tab = FindPageTab(page.FolderPath) ?? SelectedPageTab();
        if (tab == null)
        {
            tab = new PageTabState(page.FolderPath, page.Name);
            _pageTabs.Add(tab);
        }

        tab.PageFolder = page.FolderPath;
        tab.PageName = page.Name;
        tab.ViewState = new PdfViewport.ViewState(bookmark.Zoom, bookmark.PanX, bookmark.PanY);
        LoadPageFromTab(tab, page);
        TxtStatus.Text = $"Opened bookmark '{bookmark.Name}' on {page.Name}.";
    }

    private void DeleteSelectedBookmark()
    {
        PageBookmark? bookmark = SelectedBookmark();
        if (bookmark == null)
            return;

        MessageBoxResult result = MessageBox.Show(
            this,
            $"Delete bookmark '{bookmark.Name}'?",
            "Delete Bookmark",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        _pageBookmarks.Remove(bookmark);
        SavePageBookmarks();
        RefreshBookmarkList();
        TxtStatus.Text = $"Deleted bookmark '{bookmark.Name}'.";
    }

    private void SavePageBookmarks()
    {
        if (_currentJob == null)
            return;

        OurPlaneCoreJobStore.SavePageBookmarks(_currentJob, _pageBookmarks);
    }

    private string UniqueBookmarkName(string baseName)
    {
        string clean = string.IsNullOrWhiteSpace(baseName) ? "Bookmark" : baseName.Trim();
        if (_pageBookmarks.All(bookmark => !string.Equals(bookmark.Name, clean, StringComparison.OrdinalIgnoreCase)))
            return clean;

        for (int i = 2; ; i++)
        {
            string candidate = $"{clean} {i.ToString(CultureInfo.InvariantCulture)}";
            if (_pageBookmarks.All(bookmark => !string.Equals(bookmark.Name, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
    }
}
