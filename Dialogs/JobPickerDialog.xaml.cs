using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace OurPlaneCore.Controls;

public partial class JobPickerDialog : Window
{
    private readonly List<JobPickerItem> _items;
    private readonly Action<string, string, bool>? _pinChanged;
    private readonly Action<string>? _removeRecent;
    private readonly List<string> _jobsRootPaths;
    private readonly List<JobRootChip> _chips = [];

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

        InitializeComponent();

        BuildSourceChips();
        JobsList.ContextMenu = BuildContextMenu();
        JobsList.PreviewMouseRightButtonDown += JobsList_PreviewMouseRightButtonDown;
        SearchBox.TextChanged += (_, _) => ApplyFilter();
        SearchBox.PreviewKeyDown += SearchBox_PreviewKeyDown;

        Loaded += (_, _) =>
        {
            ApplyFilter();
            SearchBox.Focus();
        };
    }

    // ───────────────────────── source chips ─────────────────────────

    private sealed class JobRootChip : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string Path { get; init; } = "";  // "" = All Jobs
        public string DisplayName { get; init; } = "";
        public string Tooltip { get; init; } = "";

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;

                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private void BuildSourceChips()
    {
        _chips.Clear();
        SourceChipsPanel.Children.Clear();

        AppendChip(new JobRootChip
        {
            Path = "",
            DisplayName = "All Jobs",
            Tooltip = "Show jobs from every configured folder",
        });

        foreach (string rootPath in _jobsRootPaths)
        {
            JobRootDescriptor descriptor = JobRootSelectorBar.DescribeJobRoot(rootPath);
            _chips.Add(new JobRootChip
            {
                Path = descriptor.Path,
                DisplayName = descriptor.DisplayName,
                Tooltip = descriptor.Exists
                    ? descriptor.Path
                    : $"{descriptor.StatusLabel}: {descriptor.Path}",
            });
        }

        foreach (JobRootChip chip in _chips.Skip(1))
            SourceChipsPanel.Children.Add(BuildChipControl(chip));

        // "All Jobs" is selected by default.
        if (_chips.Count > 0)
            _chips[0].IsSelected = true;
    }

    private void AppendChip(JobRootChip chip)
    {
        _chips.Add(chip);
        SourceChipsPanel.Children.Add(BuildChipControl(chip));
    }

    private CheckBox BuildChipControl(JobRootChip chip)
    {
        var box = new CheckBox
        {
            Content = chip.DisplayName,
            ToolTip = chip.Tooltip,
            Style = (Style)FindResource("TopCommandCheckBox"),
            Margin = new Thickness(0, 0, 12, 0),
            DataContext = chip,
            IsChecked = chip.IsSelected,
        };
        box.SetBinding(ToggleButton_IsCheckedProperty(), new Binding(nameof(JobRootChip.IsSelected))
        {
            Mode = BindingMode.TwoWay,
        });
        box.Click += (_, _) =>
        {
            // Radio-style: clicking sets this chip and clears the others.
            foreach (JobRootChip other in _chips)
                other.IsSelected = ReferenceEquals(other, chip);
            ApplyFilter();
        };
        return box;
    }

    private static System.Windows.DependencyProperty ToggleButton_IsCheckedProperty() =>
        System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty;

    // ───────────────────────── filter / view ─────────────────────────

    private void ApplyFilter()
    {
        string query = SearchBox.Text.Trim();
        string selectedRoot = _chips.FirstOrDefault(c => c.IsSelected)?.Path ?? "";

        var filtered = _items
            .Where(item =>
            {
                if (!string.IsNullOrWhiteSpace(selectedRoot) &&
                    !string.Equals(NormalizePath(item.RootPath), selectedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(query))
                    return true;

                return item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       item.Path.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       item.SourceLabel.Contains(query, StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(item => item.GroupOrder)
            .ThenByDescending(item => item.LastOpenedUtc ?? DateTime.MinValue)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var view = new ListCollectionView(filtered);
        if (string.IsNullOrWhiteSpace(query))
        {
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(JobPickerItem.GroupKey)));
        }

        JobsList.ItemsSource = view;
        JobsList.SelectedIndex = filtered.Count > 0 ? 0 : -1;
        UpdateDetails();
    }

    private void UpdateDetails()
    {
        if (JobsList.SelectedItem is not JobPickerItem item)
        {
            DetailsText.Text = _items.Count == 0
                ? "No jobs found. Create a Sample Job, create a New Job, or add a Jobs Folder."
                : "";
            OpenButton.IsEnabled = false;
            return;
        }

        DetailsText.Text = item.Exists
            ? $"{item.Path}  ·  Right-click for pin / unpin / open folder / remove from Recent."
            : $"Missing: {item.Path}";
        OpenButton.IsEnabled = item.Exists;
    }

    private void JobsList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateDetails();

    private void JobsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? current = e.OriginalSource as DependencyObject;
        while (current != null && current is not ListBoxItem)
            current = VisualTreeHelper.GetParent(current);

        if (current is ListBoxItem item)
            item.IsSelected = true;
    }

    private void JobsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        AcceptOpen();

    // ───────────────────────── star (pin) ─────────────────────────

    private void StarButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not JobPickerItem item)
            return;

        TogglePinned(item);
        e.Handled = true;
    }

    private void TogglePinned(JobPickerItem item)
    {
        bool newState = !item.IsPinned;
        _pinChanged?.Invoke(item.Path, item.Name, newState);
        item.IsPinned = newState;
        if (newState)
            item.IsRecent = true;
        ApplyFilter();
        JobsList.SelectedItem = item;
        JobsList.ScrollIntoView(item);
    }

    private void RemoveFromRecent(JobPickerItem item)
    {
        _removeRecent?.Invoke(item.Path);
        if (item.Exists && IsUnderJobsRoot(item.Path))
        {
            item.IsPinned = false;
            item.IsRecent = false;
        }
        else
        {
            _items.Remove(item);
        }

        ApplyFilter();
    }

    // ───────────────────────── context menu ─────────────────────────

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        menu.Opened += (_, _) =>
        {
            menu.Items.Clear();
            if (JobsList.SelectedItem is not JobPickerItem item)
                return;

            var pinItem = new MenuItem
            {
                Header = item.IsPinned ? "Unpin from Recent" : "Pin to Recent",
            };
            pinItem.Click += (_, _) => TogglePinned(item);
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

    // ───────────────────────── footer / shortcuts ─────────────────────────

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

        if (e.Key is not (Key.Down or Key.Up))
            return;

        if (JobsList.Items.Count == 0)
            return;

        int current = JobsList.SelectedIndex < 0 ? 0 : JobsList.SelectedIndex;
        int next = e.Key == Key.Down
            ? Math.Min(JobsList.Items.Count - 1, current + 1)
            : Math.Max(0, current - 1);
        JobsList.SelectedIndex = next;
        if (JobsList.SelectedItem != null)
            JobsList.ScrollIntoView(JobsList.SelectedItem);
        e.Handled = true;
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e) => AcceptOpen();
    private void NewJobButton_Click(object sender, RoutedEventArgs e) =>
        AcceptAction(JobPickerAction.NewJob, ResolveSelectedJobsRootPath());
    private void AddFolderButton_Click(object sender, RoutedEventArgs e) =>
        AcceptAction(JobPickerAction.BrowseJobsFolder);
    private void ManageLink_Click(object sender, MouseButtonEventArgs e) =>
        AcceptAction(JobPickerAction.BrowseJobsFolder);
    private void SampleLink_Click(object sender, MouseButtonEventArgs e) =>
        AcceptAction(JobPickerAction.CreateSample);

    private void AcceptOpen()
    {
        if (JobsList.SelectedItem is not JobPickerItem item || !item.Exists)
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
        string selectedRootPath = _chips.FirstOrDefault(c => c.IsSelected)?.Path ?? "";
        if (!string.IsNullOrWhiteSpace(selectedRootPath) && Directory.Exists(selectedRootPath))
            return NormalizePath(selectedRootPath);

        if (JobsList.SelectedItem is JobPickerItem item)
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

    // ───────────────────────── helpers ─────────────────────────

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
            return Path.GetFullPath(path.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim()
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static void OpenFolderInExplorer(string path)
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
}
