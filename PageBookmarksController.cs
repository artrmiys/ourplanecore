using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using OurPlanCore.Controls;
using SkiaSharp;
using WpfPath = System.Windows.Shapes.Path;

namespace OurPlanCore;

internal sealed class PageBookmarksController
{
    private readonly Window _owner;
    private readonly TabControl _pagesSideTabs;
    private readonly ToggleButton _dockToggleButton;
    private readonly ContentControl _dockContentHost;
    private readonly GridSplitter _dockSplitter;
    private readonly DockPanel _dockPanel;
    private readonly RowDefinition _dockSplitterRow;
    private readonly RowDefinition _dockRow;
    private readonly Action<string> _setStatus;
    private readonly Func<OurPlanCoreJob?> _currentJob;
    private readonly Func<bool> _canWriteCurrentJob;
    private readonly Func<PageInfo?> _currentPage;
    private readonly PdfViewport _viewport;
    private readonly Action<PageInfo, PdfViewport.ViewState> _openBookmarkView;
    private readonly List<PageBookmark> _pageBookmarks = [];

    private ListView? _bookmarkList;
    private TextBlock? _bookmarkStatusText;
    private Button? _bookmarkAddButton;
    private Button? _bookmarkOpenButton;
    private Button? _bookmarkImageButton;
    private Button? _bookmarkRenameButton;
    private Button? _bookmarkDeleteButton;
    private ToggleButton? _bookmarksTabDockToggle;
    private TabItem? _bookmarksTab;
    private FrameworkElement? _bookmarkPanel;
    private GridLength _dockRowHeight = new(190);
    private bool _syncingBookmarkSelection;
    private bool _syncingBookmarkDockToggle;
    private bool _bookmarkCropPreviewPress;
    private PageBookmarkRow? _bookmarkSelectionBeforeCropPreview;
    private bool _moduleEnabled = true;
    private bool _wasDockedBeforeModuleDisable;

    public PageBookmarksController(
        Window owner,
        TabControl pagesSideTabs,
        ToggleButton dockToggleButton,
        ContentControl dockContentHost,
        GridSplitter dockSplitter,
        DockPanel dockPanel,
        RowDefinition dockSplitterRow,
        RowDefinition dockRow,
        Action<string> setStatus,
        Func<OurPlanCoreJob?> currentJob,
        Func<bool> canWriteCurrentJob,
        Func<PageInfo?> currentPage,
        PdfViewport viewport,
        Action<PageInfo, PdfViewport.ViewState> openBookmarkView)
    {
        _owner = owner;
        _pagesSideTabs = pagesSideTabs;
        _dockToggleButton = dockToggleButton;
        _dockContentHost = dockContentHost;
        _dockSplitter = dockSplitter;
        _dockPanel = dockPanel;
        _dockSplitterRow = dockSplitterRow;
        _dockRow = dockRow;
        _setStatus = setStatus;
        _currentJob = currentJob;
        _canWriteCurrentJob = canWriteCurrentJob;
        _currentPage = currentPage;
        _viewport = viewport;
        _openBookmarkView = openBookmarkView;
    }

    public void Initialize()
    {
        _dockToggleButton.ToolTip =
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
                    new GridViewColumn { Header = "View", Width = 44, CellTemplate = BuildBookmarkViewCellTemplate() },
                },
            },
        };
        _bookmarkList.SelectionChanged += BookmarkList_SelectionChanged;
        _bookmarkList.MouseDoubleClick += (_, _) => OpenSelectedBookmark();
        _bookmarkList.KeyDown += BookmarkList_KeyDown;

        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        _bookmarkAddButton = BookmarkButton("Add", BtnBookmarkAdd_Click, "Save the current page view or visible crop image as a bookmark");
        toolbar.Children.Add(_bookmarkAddButton);
        _bookmarkOpenButton = BookmarkButton("Open", BtnBookmarkOpen_Click, "Open the selected bookmark view");
        _bookmarkImageButton = BookmarkButton("Image", BtnBookmarkImage_Click, "Open the selected bookmark crop image");
        _bookmarkRenameButton = BookmarkButton("Rename", BtnBookmarkRename_Click, "Rename the selected bookmark");
        _bookmarkDeleteButton = BookmarkButton("Delete", BtnBookmarkDelete_Click, "Delete the selected bookmark");
        toolbar.Children.Add(_bookmarkOpenButton);
        toolbar.Children.Add(_bookmarkImageButton);
        toolbar.Children.Add(_bookmarkRenameButton);
        toolbar.Children.Add(_bookmarkDeleteButton);
        toolbar.Children.Add(BookmarkButton("Refresh", BtnBookmarkRefresh_Click, "Reload bookmarks from the job"));

        _bookmarkStatusText = new TextBlock
        {
            Text = "No bookmarks.",
            FontSize = 10,
            Foreground = Brushes.Gray,
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
        _pagesSideTabs.Items.Add(_bookmarksTab);
        RefreshBookmarkList();
    }

    public void BookmarkDockToggleChanged(object sender)
    {
        if (_syncingBookmarkDockToggle || sender is not ToggleButton toggle)
            return;

        ApplyBookmarksDockMode(toggle.IsChecked == true);
    }

    public void LoadForJob()
    {
        _pageBookmarks.Clear();
        if (!_moduleEnabled)
        {
            RefreshBookmarkList();
            return;
        }

        OurPlanCoreJob? job = _currentJob();
        if (job != null)
            _pageBookmarks.AddRange(OurPlanCoreJobStore.LoadPageBookmarks(job));

        RefreshBookmarkList();
    }

    public void ApplyJobAccessState()
    {
        UpdateBookmarkStatus();
        UpdateBookmarkButtons();
    }

    public void AddFromShortcut()
    {
        if (!_moduleEnabled)
        {
            _setStatus("Bookmarks module is disabled in Settings > Modules.");
            return;
        }

        AddCurrentPageBookmark(promptForName: true);
    }

    public void SetModuleEnabled(bool enabled)
    {
        if (_moduleEnabled == enabled)
            return;

        _moduleEnabled = enabled;
        if (_bookmarksTab == null)
            return;

        if (!enabled)
        {
            _wasDockedBeforeModuleDisable = _dockPanel.Visibility == Visibility.Visible;
            if (ReferenceEquals(_pagesSideTabs.SelectedItem, _bookmarksTab))
                _pagesSideTabs.SelectedIndex = 0;
            _bookmarksTab.Visibility = Visibility.Collapsed;
            _dockPanel.Visibility = Visibility.Collapsed;
            _dockSplitter.Visibility = Visibility.Collapsed;
            _dockSplitterRow.Height = new GridLength(0);
            _dockRow.MinHeight = 0;
            _dockRow.Height = new GridLength(0);
            return;
        }

        _bookmarksTab.Visibility = Visibility.Visible;
        if (_wasDockedBeforeModuleDisable)
            ApplyBookmarksDockMode(docked: true);
        LoadForJob();
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
            Style = (Style)_owner.FindResource("BookmarkDockToggleButton"),
            ToolTip = tooltip,
        };
        toggle.Checked += (_, _) => BookmarkDockToggleChanged(toggle);
        toggle.Unchecked += (_, _) => BookmarkDockToggleChanged(toggle);
        return toggle;
    }

    private void ApplyBookmarksDockMode(bool docked)
    {
        if (_bookmarksTab == null || _bookmarkPanel == null)
            return;

        SetBookmarksDockToggleState(docked);

        if (docked)
        {
            if (_pagesSideTabs.SelectedItem == _bookmarksTab)
                _pagesSideTabs.SelectedIndex = 0;

            if (_pagesSideTabs.Items.Contains(_bookmarksTab))
            {
                _bookmarksTab.Content = null;
                _pagesSideTabs.Items.Remove(_bookmarksTab);
            }

            _dockContentHost.Content = _bookmarkPanel;
            _dockSplitter.Visibility = Visibility.Visible;
            _dockPanel.Visibility = Visibility.Visible;
            _dockSplitterRow.Height = new GridLength(4);
            _dockRow.MinHeight = 120;
            _dockRow.Height = _dockRowHeight.Value > 0
                ? _dockRowHeight
                : new GridLength(190);
            _setStatus("Bookmarks docked below Pages.");
            return;
        }

        if (_dockRow.ActualHeight >= 80)
            _dockRowHeight = new GridLength(_dockRow.ActualHeight);

        _dockContentHost.Content = null;
        if (!_pagesSideTabs.Items.Contains(_bookmarksTab))
        {
            _bookmarksTab.Content = _bookmarkPanel;
            _pagesSideTabs.Items.Add(_bookmarksTab);
        }
        _pagesSideTabs.SelectedItem = _bookmarksTab;

        _dockPanel.Visibility = Visibility.Collapsed;
        _dockSplitter.Visibility = Visibility.Collapsed;
        _dockSplitterRow.Height = new GridLength(0);
        _dockRow.MinHeight = 0;
        _dockRow.Height = new GridLength(0);
        _setStatus("Bookmarks returned to the Pages tabs.");
    }

    private void SetBookmarksDockToggleState(bool docked)
    {
        _syncingBookmarkDockToggle = true;
        try
        {
            if (_bookmarksTabDockToggle != null)
                _bookmarksTabDockToggle.IsChecked = docked;
            _dockToggleButton.IsChecked = docked;
        }
        finally
        {
            _syncingBookmarkDockToggle = false;
        }
    }

    // Bookmarks select exactly like the AI Inbox / trees: the shared
    // TreeRowListItem template paints a solid RowSelectionBrush fill that does
    // not wash out (the default ListView selection renders a near-white band in
    // the dark theme).
    private static Style BuildBookmarkListItemStyle() =>
        (Style)Application.Current.FindResource("TreeRowListItem");

    private static Style BuildHiddenBookmarkColumnHeaderStyle()
    {
        var style = new Style(typeof(GridViewColumnHeader));
        style.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
        style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 0.0));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        return style;
    }

    private DataTemplate BuildBookmarkViewCellTemplate()
    {
        var cell = new FrameworkElementFactory(typeof(Grid));

        var viewText = new FrameworkElementFactory(typeof(TextBlock));
        viewText.SetBinding(TextBlock.TextProperty, new Binding(nameof(PageBookmarkRow.View)));
        viewText.SetBinding(
            UIElement.VisibilityProperty,
            new Binding(nameof(PageBookmarkRow.ViewTextVisibility)));
        viewText.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        viewText.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        cell.AppendChild(viewText);

        var previewButton = new FrameworkElementFactory(typeof(Button));
        previewButton.SetValue(FrameworkElement.WidthProperty, 20.0);
        previewButton.SetValue(FrameworkElement.HeightProperty, 20.0);
        previewButton.SetValue(FrameworkElement.MinWidthProperty, 0.0);
        previewButton.SetValue(Control.PaddingProperty, new Thickness(2));
        previewButton.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
        previewButton.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        previewButton.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        previewButton.SetValue(FrameworkElement.ToolTipProperty, "Preview crop image");
        previewButton.SetValue(FrameworkElement.StyleProperty, Application.Current.FindResource("ToolBtn"));
        previewButton.SetBinding(
            AutomationProperties.NameProperty,
            new Binding(nameof(PageBookmarkRow.Name))
            {
                StringFormat = "Preview crop image for {0}",
            });
        previewButton.SetBinding(
            UIElement.VisibilityProperty,
            new Binding(nameof(PageBookmarkRow.HasCropImage))
            {
                Converter = new BooleanToVisibilityConverter(),
            });
        previewButton.SetBinding(
            FrameworkElement.TagProperty,
            new Binding(nameof(PageBookmarkRow.Bookmark)));
        previewButton.AddHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(BookmarkCropPreviewButton_PreviewMouseLeftButtonDown));
        previewButton.AddHandler(
            Button.ClickEvent,
            new RoutedEventHandler(BookmarkCropPreviewButton_Click));
        previewButton.AddHandler(
            UIElement.LostMouseCaptureEvent,
            new MouseEventHandler(BookmarkCropPreviewButton_LostMouseCapture));

        var icon = new FrameworkElementFactory(typeof(WpfPath));
        icon.SetValue(WpfPath.DataProperty, Application.Current.FindResource("IconCrop"));
        icon.SetValue(FrameworkElement.WidthProperty, 13.0);
        icon.SetValue(FrameworkElement.HeightProperty, 13.0);
        icon.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
        icon.SetValue(WpfPath.StretchProperty, Stretch.Uniform);
        icon.SetValue(WpfPath.StrokeThicknessProperty, 1.35);
        icon.SetValue(WpfPath.StrokeLineJoinProperty, PenLineJoin.Round);
        icon.SetValue(WpfPath.StrokeStartLineCapProperty, PenLineCap.Round);
        icon.SetValue(WpfPath.StrokeEndLineCapProperty, PenLineCap.Round);
        icon.SetBinding(
            WpfPath.StrokeProperty,
            new Binding(nameof(Control.Foreground))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1),
            });
        previewButton.AppendChild(icon);
        cell.AppendChild(previewButton);

        return new DataTemplate
        {
            VisualTree = cell,
        };
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

    private void RefreshBookmarkList(string? selectBookmarkId = null)
    {
        if (_bookmarkList == null)
            return;

        var rows = _pageBookmarks
            .Select((bookmark, index) =>
            {
                bool hasCropImage = HasBookmarkCropImage(bookmark);
                return new PageBookmarkRow(
                    bookmark.Name,
                    BookmarkPageName(bookmark),
                    FormatBookmarkView(bookmark),
                    hasCropImage,
                    hasCropImage ? Visibility.Collapsed : Visibility.Visible,
                    index + 1,
                    bookmark);
            })
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

        UpdateBookmarkStatus();
        UpdateBookmarkButtons();
    }

    private void UpdateBookmarkStatus()
    {
        if (_bookmarkStatusText == null)
            return;

        OurPlanCoreJob? job = _currentJob();
        string text = job == null
            ? "Open a job to use bookmarks."
            : _pageBookmarks.Count == 0
                ? "No bookmarks."
                : $"{_pageBookmarks.Count.ToString(CultureInfo.InvariantCulture)} bookmark(s).";
        if (job != null && !_canWriteCurrentJob())
            text += " Read-only: open/view only.";
        _bookmarkStatusText.Text = text;
    }

    private static string BookmarkPageName(PageBookmark bookmark)
    {
        PageInfo? page = OurPlanCoreJobStore.TryReadPage(bookmark.PageFolder);
        return page?.Name ?? (string.IsNullOrWhiteSpace(bookmark.PageName) ? "Missing" : bookmark.PageName);
    }

    private static string FormatBookmarkView(PageBookmark bookmark)
    {
        if (IsCropImageBookmark(bookmark))
            return "Img";

        return bookmark.Zoom > 0
            ? $"{Math.Round(bookmark.Zoom * 100).ToString(CultureInfo.InvariantCulture)}%"
            : "view";
    }

    private static bool IsCropImageBookmark(PageBookmark? bookmark) =>
        bookmark != null &&
        string.Equals(bookmark.Type, "crop_image", StringComparison.OrdinalIgnoreCase);

    private bool HasBookmarkCropImage(PageBookmark bookmark)
    {
        if (!IsCropImageBookmark(bookmark) || string.IsNullOrWhiteSpace(bookmark.CropImagePath))
            return false;

        string path = BookmarkCropImageFullPath(bookmark);
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }

    private void UpdateBookmarkButtons()
    {
        PageBookmark? selection = SelectedBookmark();
        bool hasSelection = selection != null;
        bool canWrite = _moduleEnabled && _currentJob() != null && _canWriteCurrentJob();
        if (_bookmarkAddButton != null)
            _bookmarkAddButton.IsEnabled = canWrite;
        if (_bookmarkOpenButton != null)
            _bookmarkOpenButton.IsEnabled = hasSelection;
        if (_bookmarkImageButton != null)
            _bookmarkImageButton.IsEnabled = IsCropImageBookmark(selection) &&
                !string.IsNullOrWhiteSpace(selection?.CropImagePath);
        if (_bookmarkRenameButton != null)
            _bookmarkRenameButton.IsEnabled = canWrite && hasSelection;
        if (_bookmarkDeleteButton != null)
            _bookmarkDeleteButton.IsEnabled = canWrite && hasSelection;
    }

    private PageBookmark? SelectedBookmark() =>
        _bookmarkList?.SelectedItem is PageBookmarkRow row ? row.Bookmark : null;

    private void BookmarkList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateBookmarkButtons();
        if (_syncingBookmarkSelection || _bookmarkCropPreviewPress || SelectedBookmark() == null)
            return;

        OpenSelectedBookmark();
    }

    private void BookmarkCropPreviewButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _bookmarkCropPreviewPress = true;
        _bookmarkSelectionBeforeCropPreview = _bookmarkList?.SelectedItem as PageBookmarkRow;
    }

    private void BookmarkCropPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        try
        {
            if (sender is Button { Tag: PageBookmark bookmark })
                OpenBookmarkCropImage(bookmark);
        }
        finally
        {
            RestoreBookmarkSelectionAfterCropPreview();
        }
    }

    private void BookmarkCropPreviewButton_LostMouseCapture(object sender, MouseEventArgs e) =>
        RestoreBookmarkSelectionAfterCropPreview();

    private void RestoreBookmarkSelectionAfterCropPreview()
    {
        if (!_bookmarkCropPreviewPress)
            return;

        _syncingBookmarkSelection = true;
        try
        {
            if (_bookmarkList != null)
                _bookmarkList.SelectedItem = _bookmarkSelectionBeforeCropPreview;
        }
        finally
        {
            _syncingBookmarkSelection = false;
            _bookmarkCropPreviewPress = false;
            _bookmarkSelectionBeforeCropPreview = null;
            UpdateBookmarkButtons();
        }
    }

    private void BookmarkList_KeyDown(object sender, KeyEventArgs e)
    {
        if (IsBookmarkRowButtonSource(e.OriginalSource as DependencyObject))
            return;

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

    private static bool IsBookmarkRowButtonSource(DependencyObject? source)
    {
        for (DependencyObject? current = source; current != null;)
        {
            if (current is ButtonBase)
                return true;

            current = current is Visual
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    private void BtnBookmarkAdd_Click(object sender, RoutedEventArgs e)
    {
        AddCurrentPageBookmark(promptForName: true);
    }

    private void AddCurrentPageBookmark(bool promptForName)
    {
        OurPlanCoreJob? job = _currentJob();
        PageInfo? page = _currentPage();
        if (job == null || page == null)
        {
            _setStatus("Open a page before adding a bookmark.");
            return;
        }
        if (!EnsureCanModifyBookmarks(job, "add a bookmark"))
            return;

        string defaultName = $"{page.Name} view";
        string name = UniqueBookmarkName(defaultName);
        PageBookmarkSaveMode saveMode = PageBookmarkSaveMode.View;
        if (promptForName)
        {
            var dialog = new PageBookmarkDialog(
                "Add Bookmark",
                name,
                showSaveMode: true,
                initialSaveMode: PageBookmarkSaveMode.View)
            {
                Owner = _owner,
            };
            if (dialog.ShowDialog() != true)
                return;
            name = dialog.BookmarkName;
            saveMode = dialog.SaveMode;
        }
        if (!EnsureCanModifyBookmarks(job, "add a bookmark"))
            return;

        PdfViewport.ViewState view = _viewport.CaptureViewState();
        string now = DateTime.UtcNow.ToString("O");
        string id = Guid.NewGuid().ToString("N");
        string cropImagePath = "";
        SKRect cropRect = SKRect.Empty;
        if (saveMode == PageBookmarkSaveMode.CropImage &&
            !TrySaveBookmarkCropImage(job, page, name, id, out cropImagePath, out cropRect, out string cropError))
        {
            _setStatus($"Crop image bookmark failed: {cropError}");
            return;
        }

        var bookmark = new PageBookmark
        {
            Id = id,
            Name = name,
            PageFolder = page.FolderPath,
            PageName = page.Name,
            Type = saveMode == PageBookmarkSaveMode.CropImage ? "crop_image" : "view",
            Zoom = view.Zoom,
            PanX = view.PanX,
            PanY = view.PanY,
            CropImagePath = cropImagePath,
            CropLeft = cropRect.Left,
            CropTop = cropRect.Top,
            CropRight = cropRect.Right,
            CropBottom = cropRect.Bottom,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        _pageBookmarks.Add(bookmark);
        SavePageBookmarks(job);
        RefreshBookmarkList(bookmark.Id);
        string kind = IsCropImageBookmark(bookmark) ? "crop image bookmark" : "bookmark";
        _setStatus(promptForName
            ? $"Added {kind} '{bookmark.Name}'."
            : $"Added {kind} '{bookmark.Name}' ({KeyboardShortcutKeys.DualLayoutDisplay("bk")}).");
    }

    private void BtnBookmarkOpen_Click(object sender, RoutedEventArgs e) =>
        OpenSelectedBookmark();

    private void BtnBookmarkImage_Click(object sender, RoutedEventArgs e)
    {
        PageBookmark? bookmark = SelectedBookmark();
        if (bookmark != null)
            OpenBookmarkCropImage(bookmark);
    }

    private void BtnBookmarkRename_Click(object sender, RoutedEventArgs e)
    {
        PageBookmark? bookmark = SelectedBookmark();
        if (bookmark == null)
            return;
        OurPlanCoreJob? job = _currentJob();
        if (job == null || !EnsureCanModifyBookmarks(job, "rename a bookmark"))
            return;

        var dialog = new PageBookmarkDialog("Rename Bookmark", bookmark.Name)
        {
            Owner = _owner,
        };
        if (dialog.ShowDialog() != true)
            return;
        if (!EnsureCanModifyBookmarks(job, "rename a bookmark"))
            return;

        bookmark.Name = dialog.BookmarkName;
        bookmark.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
        SavePageBookmarks();
        RefreshBookmarkList(bookmark.Id);
        _setStatus($"Renamed bookmark to '{bookmark.Name}'.");
    }

    private void BtnBookmarkDelete_Click(object sender, RoutedEventArgs e) =>
        DeleteSelectedBookmark();

    private void BtnBookmarkRefresh_Click(object sender, RoutedEventArgs e)
    {
        LoadForJob();
        _setStatus("Bookmarks refreshed.");
    }

    private void OpenSelectedBookmark()
    {
        PageBookmark? bookmark = SelectedBookmark();
        if (bookmark != null)
            OpenPageBookmark(bookmark);
    }

    private void OpenPageBookmark(PageBookmark bookmark)
    {
        if (_currentJob() == null)
        {
            _setStatus("Open a job before opening bookmarks.");
            return;
        }

        PageInfo? page = OurPlanCoreJobStore.TryReadPage(bookmark.PageFolder);
        if (page == null)
        {
            _setStatus($"Bookmark page is missing: {bookmark.Name}.");
            return;
        }

        _openBookmarkView(page, new PdfViewport.ViewState(bookmark.Zoom, bookmark.PanX, bookmark.PanY));
        _setStatus($"Opened bookmark '{bookmark.Name}' on {page.Name}.");
    }

    private void DeleteSelectedBookmark()
    {
        PageBookmark? bookmark = SelectedBookmark();
        if (bookmark == null)
            return;
        OurPlanCoreJob? job = _currentJob();
        if (job == null || !EnsureCanModifyBookmarks(job, "delete a bookmark"))
            return;

        MessageBoxResult result = MessageBox.Show(
            _owner,
            $"Delete bookmark '{bookmark.Name}'?",
            "Delete Bookmark",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;
        if (!EnsureCanModifyBookmarks(job, "delete a bookmark"))
            return;

        if (!TryDeleteBookmarkCropImage(bookmark, out string deleteError))
        {
            _setStatus($"Could not delete bookmark: {deleteError}");
            return;
        }
        _pageBookmarks.Remove(bookmark);
        SavePageBookmarks();
        RefreshBookmarkList();
        _setStatus($"Deleted bookmark '{bookmark.Name}'.");
    }

    private bool TrySaveBookmarkCropImage(
        OurPlanCoreJob job,
        PageInfo page,
        string bookmarkName,
        string bookmarkId,
        out string cropImagePath,
        out SKRect cropRect,
        out string error)
    {
        cropImagePath = "";
        cropRect = SKRect.Empty;
        error = "";

        SKRect requestedRect = _viewport.GetVisiblePdfRect();
        if (requestedRect.Width < 1 || requestedRect.Height < 1)
        {
            error = "No visible PDF area is available.";
            return false;
        }
        if (!_viewport.IsPageRenderReady(page.FolderPath))
        {
            error = "The current page is still rendering. Try the bookmark again in a moment.";
            return false;
        }

        string outputPath = BookmarkCropImagePath(job, page, bookmarkName, bookmarkId);
        try
        {
            JobWriteAccess.Demand(outputPath, "save a bookmark crop image");
            if (!_viewport.TrySaveBookmarkCropRect(requestedRect, outputPath, out cropRect, out error))
                return false;
        }
        catch (JobWriteDeniedException ex)
        {
            error = ex.Message;
            return false;
        }

        cropImagePath = outputPath;
        return true;
    }

    private void OpenBookmarkCropImage(PageBookmark bookmark)
    {
        if (!IsCropImageBookmark(bookmark))
        {
            _setStatus("Selected bookmark is not a crop image.");
            return;
        }

        string path = BookmarkCropImageFullPath(bookmark);
        if (string.IsNullOrWhiteSpace(path) ||
            !File.Exists(path) ||
            !ProjectPathSafety.IsSafeImagePath(path))
        {
            _setStatus($"Crop image is missing for bookmark '{bookmark.Name}'.");
            return;
        }

        try
        {
            var dialog = new PageBookmarkCropPreviewDialog(bookmark.Name, path)
            {
                Owner = _owner,
            };
            dialog.ShowDialog();
            _setStatus($"Viewed crop image '{bookmark.Name}'.");
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            NotSupportedException or
            InvalidOperationException or
            FormatException or
            ArgumentException)
        {
            _setStatus($"Could not preview crop image: {ex.Message}");
        }
    }

    private bool TryDeleteBookmarkCropImage(PageBookmark bookmark, out string error)
    {
        error = "";
        if (!IsCropImageBookmark(bookmark))
            return true;

        OurPlanCoreJob? job = _currentJob();
        string path = BookmarkCropImageFullPath(bookmark);
        if (job == null || string.IsNullOrWhiteSpace(path) || !OurPlanCoreJobStore.IsSameOrDescendant(job.RootPath, path))
            return true;

        try
        {
            if (File.Exists(path))
            {
                JobWriteAccess.Demand(path, "delete a bookmark crop image");
                File.Delete(path);
            }
            return true;
        }
        catch (JobWriteDeniedException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn(ex, $"Failed to delete bookmark crop image {path}");
            error = ex.Message;
            return false;
        }
    }

    private string BookmarkCropImageFullPath(PageBookmark bookmark)
    {
        string path = bookmark.CropImagePath?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(path))
            return "";

        OurPlanCoreJob? job = _currentJob();
        return job != null && ProjectPathSafety.TryResolveInside(
            job.RootPath,
            path,
            job.RootPath,
            out string resolved)
            ? resolved
            : "";
    }

    private static string BookmarkCropImagePath(
        OurPlanCoreJob job,
        PageInfo page,
        string bookmarkName,
        string bookmarkId)
    {
        string folder = Path.Combine(job.RootPath, "bookmark_crops");
        string pagePart = OurPlanCoreJobStore.SanitizeName(page.Name, 48);
        string namePart = OurPlanCoreJobStore.SanitizeName(bookmarkName, 48);
        string idPart = string.IsNullOrWhiteSpace(bookmarkId)
            ? Guid.NewGuid().ToString("N")[..8]
            : bookmarkId[..Math.Min(8, bookmarkId.Length)];
        string fileName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{pagePart}_{namePart}_{idPart}.png";
        return Path.Combine(folder, fileName);
    }

    private void SavePageBookmarks()
    {
        OurPlanCoreJob? job = _currentJob();
        if (job != null)
            SavePageBookmarks(job);
    }

    private void SavePageBookmarks(OurPlanCoreJob job)
    {
        OurPlanCoreJobStore.SavePageBookmarks(job, _pageBookmarks);
    }

    private bool EnsureCanModifyBookmarks(OurPlanCoreJob job, string operation)
    {
        if (!_canWriteCurrentJob())
        {
            _setStatus($"'{job.Name}' is open read-only. You cannot {operation}.");
            ApplyJobAccessState();
            return false;
        }

        try
        {
            JobWriteAccess.Demand(PageBookmarkStore.PageBookmarksJsonPath(job), operation);
            return true;
        }
        catch (JobWriteDeniedException ex)
        {
            _setStatus(ex.Message);
            ApplyJobAccessState();
            return false;
        }
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

    private sealed record PageBookmarkRow(
        string Name,
        string Page,
        string View,
        bool HasCropImage,
        Visibility ViewTextVisibility,
        int Order,
        PageBookmark Bookmark);
}
