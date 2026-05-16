using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OurPlaneCore.Controls;

public enum JobPickerAction
{
    None,
    OpenSelected,
    BrowseJob,
    BrowseJobsFolder,
    NewJob,
    CreateSample,
}

public sealed record JobPickerItem(
    string Name,
    string Path,
    string ThumbnailPath,
    string LastOpened,
    string Source,
    bool Exists,
    bool IsPinned,
    bool IsRecent,
    string RootPath = "")
{
    public string Status => Exists ? "Ready" : "Missing";
    public string PinStatus => IsPinned ? "Pinned" : "";
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
    private const double FooterButtonHeight = 30;
    private static readonly Thickness FooterButtonGap = new(0, 0, 6, 0);
    private static readonly Thickness FooterButtonPadding = new(10, 4, 10, 4);

    private readonly List<JobPickerItem> _items;
    private readonly Action<string, string, bool>? _pinChanged;
    private readonly Action<string>? _removeRecent;
    private readonly List<string> _jobsRootPaths;
    private readonly TextBox _searchBox;
    private readonly ComboBox _rootFilter;
    private readonly ListView _list;
    private readonly TextBlock _details;
    private readonly Button _openButton;

    public JobPickerAction SelectedAction { get; private set; } = JobPickerAction.None;
    public string SelectedJobPath { get; private set; } = "";
    public string SelectedJobsRootPath { get; private set; } = "";

    public JobPickerDialog(
        IEnumerable<JobPickerItem> items,
        string jobsRootPath,
        Action<string, string, bool>? pinChanged = null,
        Action<string>? removeRecent = null,
        IEnumerable<string>? jobsRootPaths = null)
    {
        _items = items.ToList();
        _pinChanged = pinChanged;
        _removeRecent = removeRecent;
        _jobsRootPaths = NormalizeRoots(jobsRootPaths ?? BuildRootList(jobsRootPath, _items));

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
            Text = _jobsRootPaths.Count == 0
                ? "Recent jobs"
                : $"Recent jobs  |  {_jobsRootPaths.Count} job folder(s)",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryForegroundBrush");
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);

        _rootFilter = new ComboBox
        {
            MinHeight = 26,
            Margin = new Thickness(0, 0, 0, 8),
            ToolTip = "Switch job folder root",
        };
        _rootFilter.Items.Add("All job folders");
        foreach (string rootPath in _jobsRootPaths)
            _rootFilter.Items.Add(rootPath);
        _rootFilter.SelectedIndex = 0;
        if (_jobsRootPaths.Count > 1)
        {
            DockPanel.SetDock(_rootFilter, Dock.Top);
            root.Children.Add(_rootFilter);
        }

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

        _openButton = CreateFooterButton("Open", 72, isDefault: true);
        var browseJobButton = CreateFooterButton("Browse Job...", 98);
        var browseRootButton = CreateFooterButton("Jobs Folder...", 104);
        var newJobButton = CreateFooterButton("New Job...", 88);
        var sampleJobButton = CreateFooterButton("Sample Job", 92);
        var cancelButton = CreateFooterButton("Cancel", 72, isCancel: true, hasTrailingGap: false);
        buttons.Children.Add(_openButton);
        buttons.Children.Add(browseJobButton);
        buttons.Children.Add(browseRootButton);
        buttons.Children.Add(newJobButton);
        buttons.Children.Add(sampleJobButton);
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
                    new GridViewColumn { Header = "Pin", Width = 65, DisplayMemberBinding = new Binding(nameof(JobPickerItem.PinStatus)) },
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
        _rootFilter.SelectionChanged += (_, _) => ApplyFilter();
        _searchBox.PreviewKeyDown += SearchBox_PreviewKeyDown;
        _list.PreviewMouseRightButtonDown += List_PreviewMouseRightButtonDown;
        _list.SelectionChanged += (_, _) => UpdateDetails();
        _list.MouseDoubleClick += (_, _) => AcceptOpen();
        _list.ContextMenu = BuildContextMenu();
        _openButton.Click += (_, _) => AcceptOpen();
        browseJobButton.Click += (_, _) => AcceptAction(JobPickerAction.BrowseJob);
        browseRootButton.Click += (_, _) => AcceptAction(JobPickerAction.BrowseJobsFolder);
        newJobButton.Click += (_, _) => AcceptAction(JobPickerAction.NewJob, ResolveSelectedJobsRootPath());
        sampleJobButton.Click += (_, _) => AcceptAction(JobPickerAction.CreateSample);

        Loaded += (_, _) =>
        {
            ApplyFilter();
            _searchBox.Focus();
        };
    }

    private static Button CreateFooterButton(
        string content,
        double minWidth,
        bool isDefault = false,
        bool isCancel = false,
        bool hasTrailingGap = true)
    {
        return new Button
        {
            Content = content,
            MinWidth = minWidth,
            Height = FooterButtonHeight,
            Padding = FooterButtonPadding,
            IsDefault = isDefault,
            IsCancel = isCancel,
            Margin = hasTrailingGap ? FooterButtonGap : new Thickness(0),
        };
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        menu.Opened += (_, _) =>
        {
            menu.Items.Clear();
            if (_list.SelectedItem is not JobPickerItem item)
                return;

            var pinItem = new MenuItem
            {
                Header = item.IsPinned ? "Unpin from Recent" : "Pin to Recent",
            };
            pinItem.Click += (_, _) => SetPinned(item, !item.IsPinned);
            menu.Items.Add(pinItem);

            var openFolder = new MenuItem
            {
                Header = "Open Folder in Explorer",
                IsEnabled = Directory.Exists(item.Path),
            };
            openFolder.Click += (_, _) => OpenFolderInExplorer(item.Path);
            menu.Items.Add(openFolder);

            menu.Items.Add(new Separator());
            var remove = new MenuItem
            {
                Header = "Remove from Recent",
                IsEnabled = item.IsRecent,
            };
            remove.Click += (_, _) => RemoveFromRecent(item);
            menu.Items.Add(remove);
        };
        return menu;
    }

    private void List_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? current = e.OriginalSource as DependencyObject;
        while (current != null && current is not ListViewItem)
            current = VisualTreeHelper.GetParent(current);

        if (current is ListViewItem item)
            item.IsSelected = true;
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
            ? _items.ToList()
            : _items
                .Where(item =>
                    item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    item.Path.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    item.Source.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (_rootFilter.SelectedIndex > 0 &&
            _rootFilter.SelectedItem is string selectedRoot &&
            !string.IsNullOrWhiteSpace(selectedRoot))
        {
            string rootKey = NormalizePath(selectedRoot);
            visible = visible
                .Where(item => string.Equals(NormalizePath(item.RootPath), rootKey, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        _list.ItemsSource = visible;
        _list.SelectedIndex = visible.Count > 0 ? 0 : -1;
        UpdateDetails();
    }

    private void UpdateDetails()
    {
        if (_list.SelectedItem is not JobPickerItem item)
        {
            _details.Text = "No jobs found. Create a Sample Job, create a New Job, or browse for an existing job.";
            _openButton.IsEnabled = false;
            return;
        }

        _details.Text = item.Exists
            ? $"{item.Path}  |  Right-click to pin or remove from Recent."
            : $"Missing: {item.Path}";
        _openButton.IsEnabled = item.Exists;
    }

    private void AcceptOpen()
    {
        if (_list.SelectedItem is not JobPickerItem item || !item.Exists)
            return;

        SelectedAction = JobPickerAction.OpenSelected;
        SelectedJobPath = item.Path;
        SelectedJobsRootPath = item.RootPath;
        DialogResult = true;
    }

    private void AcceptAction(JobPickerAction action, string selectedJobsRootPath = "")
    {
        SelectedAction = action;
        SelectedJobsRootPath = selectedJobsRootPath;
        DialogResult = true;
    }

    private string ResolveSelectedJobsRootPath()
    {
        if (_rootFilter.SelectedIndex > 0 &&
            _rootFilter.SelectedItem is string selectedRoot &&
            IsUsableRoot(selectedRoot))
        {
            return NormalizePath(selectedRoot);
        }

        if (_list.SelectedItem is JobPickerItem item)
        {
            if (IsUsableRoot(item.RootPath))
                return NormalizePath(item.RootPath);

            string? itemParent = Path.GetDirectoryName(item.Path);
            if (IsUsableRoot(itemParent))
                return NormalizePath(itemParent!);
        }

        if (_jobsRootPaths.Count == 1 && IsUsableRoot(_jobsRootPaths[0]))
            return NormalizePath(_jobsRootPaths[0]);

        string? firstUsableRoot = _jobsRootPaths.FirstOrDefault(IsUsableRoot);
        return firstUsableRoot == null ? "" : NormalizePath(firstUsableRoot);
    }

    private void SetPinned(JobPickerItem item, bool pinned)
    {
        _pinChanged?.Invoke(item.Path, item.Name, pinned);
        ReplaceItem(item, item with
        {
            IsPinned = pinned,
            IsRecent = true,
            Source = item.IsRecent ? item.Source : "Recent",
        });
    }

    private void RemoveFromRecent(JobPickerItem item)
    {
        _removeRecent?.Invoke(item.Path);
        if (item.Exists && IsUnderJobsRoot(item.Path))
        {
            ReplaceItem(item, item with
            {
                IsPinned = false,
                IsRecent = false,
                Source = "Jobs Folder",
            });
            return;
        }

        _items.Remove(item);
        ApplyFilter();
    }

    private void ReplaceItem(JobPickerItem oldItem, JobPickerItem newItem)
    {
        int index = _items.IndexOf(oldItem);
        if (index >= 0)
            _items[index] = newItem;
        ApplyFilter();
        _list.SelectedItem = newItem;
        _list.ScrollIntoView(newItem);
    }

    private void OpenFolderInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot open folder:\n{ex.Message}", "Open Folder",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool IsUnderJobsRoot(string path)
    {
        string candidate = NormalizePath(path);
        foreach (string rootPath in _jobsRootPaths)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
                continue;

            string root = NormalizePath(rootPath) + Path.DirectorySeparatorChar;
            if (candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static IEnumerable<string> BuildRootList(string jobsRootPath, IEnumerable<JobPickerItem> items)
    {
        if (!string.IsNullOrWhiteSpace(jobsRootPath))
            yield return jobsRootPath;
        foreach (JobPickerItem item in items)
            if (!string.IsNullOrWhiteSpace(item.RootPath))
                yield return item.RootPath;
    }

    private static List<string> NormalizeRoots(IEnumerable<string> roots)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            string clean = NormalizePath(root);
            if (seen.Add(clean))
                result.Add(clean);
        }

        return result;
    }

    private static bool IsUsableRoot(string? rootPath) =>
        !string.IsNullOrWhiteSpace(rootPath) && Directory.Exists(rootPath);

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
