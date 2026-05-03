using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SmartTakeoffs.Controls;

public enum JobPickerAction
{
    None,
    OpenSelected,
    BrowseJob,
    BrowseJobsFolder,
    NewJob,
}

public sealed record JobPickerItem(
    string Name,
    string Path,
    string ThumbnailPath,
    string LastOpened,
    string Source,
    bool Exists)
{
    public string Status => Exists ? "Ready" : "Missing";
    public ImageSource? ThumbnailImage => LoadThumbnail(ThumbnailPath);

    private static ImageSource? LoadThumbnail(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}

public sealed class JobPickerDialog : Window
{
    private readonly IReadOnlyList<JobPickerItem> _items;
    private readonly TextBox _searchBox;
    private readonly ListView _list;
    private readonly TextBlock _details;
    private readonly Button _openButton;

    public JobPickerAction SelectedAction { get; private set; } = JobPickerAction.None;
    public string SelectedJobPath { get; private set; } = "";

    public JobPickerDialog(IEnumerable<JobPickerItem> items, string jobsRootPath)
    {
        _items = items.ToList();

        Title = "Open Job";
        Width = 760;
        Height = 500;
        MinWidth = 600;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        var root = new DockPanel { Margin = new Thickness(12) };
        Content = root;

        var title = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(jobsRootPath)
                ? "Recent jobs"
                : $"Recent jobs  |  {jobsRootPath}",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryForegroundBrush");
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);

        _searchBox = new TextBox
        {
            MinHeight = 28,
            Margin = new Thickness(0, 0, 0, 8),
            ToolTip = "Filter jobs by name or path",
        };
        DockPanel.SetDock(_searchBox, Dock.Top);
        root.Children.Add(_searchBox);

        var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);

        _openButton = new Button { Content = "Open", MinWidth = 78, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var browseJobButton = new Button { Content = "Browse Job...", MinWidth = 104, Margin = new Thickness(0, 0, 6, 0) };
        var browseRootButton = new Button { Content = "Jobs Folder...", MinWidth = 112, Margin = new Thickness(0, 0, 6, 0) };
        var newJobButton = new Button { Content = "New Job...", MinWidth = 92, Margin = new Thickness(0, 0, 6, 0) };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 78, IsCancel = true };
        buttons.Children.Add(_openButton);
        buttons.Children.Add(browseJobButton);
        buttons.Children.Add(browseRootButton);
        buttons.Children.Add(newJobButton);
        buttons.Children.Add(cancelButton);

        _details = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        _details.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryForegroundBrush");
        footer.Children.Add(_details);

        _list = new ListView
        {
            View = new GridView
            {
                Columns =
                {
                    new GridViewColumn { Header = "Preview", Width = 110, CellTemplate = CreateThumbnailTemplate() },
                    new GridViewColumn { Header = "Job", Width = 180, DisplayMemberBinding = new Binding(nameof(JobPickerItem.Name)) },
                    new GridViewColumn { Header = "Last Opened", Width = 135, DisplayMemberBinding = new Binding(nameof(JobPickerItem.LastOpened)) },
                    new GridViewColumn { Header = "Source", Width = 110, DisplayMemberBinding = new Binding(nameof(JobPickerItem.Source)) },
                    new GridViewColumn { Header = "Status", Width = 70, DisplayMemberBinding = new Binding(nameof(JobPickerItem.Status)) },
                    new GridViewColumn { Header = "Path", Width = 360, DisplayMemberBinding = new Binding(nameof(JobPickerItem.Path)) },
                },
            },
        };
        root.Children.Add(_list);

        _searchBox.TextChanged += (_, _) => ApplyFilter();
        _searchBox.PreviewKeyDown += SearchBox_PreviewKeyDown;
        _list.SelectionChanged += (_, _) => UpdateDetails();
        _list.MouseDoubleClick += (_, _) => AcceptOpen();
        _openButton.Click += (_, _) => AcceptOpen();
        browseJobButton.Click += (_, _) => AcceptAction(JobPickerAction.BrowseJob);
        browseRootButton.Click += (_, _) => AcceptAction(JobPickerAction.BrowseJobsFolder);
        newJobButton.Click += (_, _) => AcceptAction(JobPickerAction.NewJob);

        Loaded += (_, _) =>
        {
            ApplyFilter();
            _searchBox.Focus();
        };
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            AcceptOpen();
            e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Down or Key.Up) || _list.Items.Count == 0)
            return;

        int current = _list.SelectedIndex < 0 ? 0 : _list.SelectedIndex;
        int next = e.Key == Key.Down
            ? Math.Min(_list.Items.Count - 1, current + 1)
            : Math.Max(0, current - 1);
        _list.SelectedIndex = next;
        _list.ScrollIntoView(_list.SelectedItem);
        e.Handled = true;
    }

    private static DataTemplate CreateThumbnailTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(FrameworkElement.WidthProperty, 96.0);
        border.SetValue(FrameworkElement.HeightProperty, 64.0);
        border.SetValue(FrameworkElement.MarginProperty, new Thickness(2));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        border.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        border.SetResourceReference(Border.BackgroundProperty, "SurfaceBackgroundBrush");

        var image = new FrameworkElementFactory(typeof(Image));
        image.SetValue(Image.StretchProperty, Stretch.Uniform);
        image.SetValue(FrameworkElement.MarginProperty, new Thickness(3));
        image.SetBinding(Image.SourceProperty, new Binding(nameof(JobPickerItem.ThumbnailImage)));
        border.AppendChild(image);

        return new DataTemplate { VisualTree = border };
    }

    private void ApplyFilter()
    {
        string query = _searchBox.Text.Trim();
        var visible = string.IsNullOrWhiteSpace(query)
            ? _items
            : _items
                .Where(item =>
                    item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    item.Path.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    item.Source.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        _list.ItemsSource = visible;
        _list.SelectedIndex = visible.Count > 0 ? 0 : -1;
        UpdateDetails();
    }

    private void UpdateDetails()
    {
        if (_list.SelectedItem is not JobPickerItem item)
        {
            _details.Text = "No jobs found. Use Browse Job, Jobs Folder, or New Job.";
            _openButton.IsEnabled = false;
            return;
        }

        _details.Text = item.Exists
            ? item.Path
            : $"Missing: {item.Path}";
        _openButton.IsEnabled = item.Exists;
    }

    private void AcceptOpen()
    {
        if (_list.SelectedItem is not JobPickerItem item || !item.Exists)
            return;

        SelectedAction = JobPickerAction.OpenSelected;
        SelectedJobPath = item.Path;
        DialogResult = true;
    }

    private void AcceptAction(JobPickerAction action)
    {
        SelectedAction = action;
        DialogResult = true;
    }
}
