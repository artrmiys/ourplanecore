using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace OurPlaneCore.Controls;

public sealed class PdfExportPageRow : INotifyPropertyChanged
{
    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public string PageFolder { get; init; } = "";
    public string Name { get; init; } = "";
    public string Source { get; init; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class PdfExportDialog : Window
{
    private readonly ObservableCollection<PdfExportPageRow> _rows;
    private readonly CheckBox _includeMeasurementsBox;
    private readonly CheckBox _includeAnnotationsBox;
    private readonly CheckBox _includeLegendBox;
    private readonly TextBox _measurementStrokeBox;
    private readonly Button _exportButton;
    private readonly List<(CheckBox Box, List<PdfExportPageRow> Rows)> _folderChecks = [];
    private bool _syncingFolderChecks;
    private const double MinMeasurementStrokeScale = 0.25;
    private const double MaxMeasurementStrokeScale = 10.0;
    private static readonly Brush CurrentSheetBrush = new SolidColorBrush(Color.FromRgb(0xDB, 0xEA, 0xFB));
    private static readonly Brush CurrentSheetBorderBrush = new SolidColorBrush(Color.FromRgb(0x64, 0x9E, 0xD8));

    public IReadOnlyList<PdfExportPageRow> Rows => _rows;
    public bool IncludeMeasurements => _includeMeasurementsBox.IsChecked == true;
    public bool IncludeAnnotations => _includeAnnotationsBox.IsChecked == true;
    public bool IncludeLegend => _includeLegendBox.IsChecked == true;
    public double MeasurementStrokeScale => ReadScale(
        _measurementStrokeBox,
        fallback: 1.5,
        MinMeasurementStrokeScale,
        MaxMeasurementStrokeScale);

    public PdfExportDialog(
        IEnumerable<PageInfo> pages,
        ISet<string> initiallySelected,
        bool includeMeasurements = true,
        bool includeAnnotations = true,
        bool includeLegend = true,
        double measurementStrokeScale = 1.5,
        string pagesRoot = "",
        string currentPageFolder = "")
    {
        var selectedFolders = initiallySelected
            .Select(NormalizePathForCompare)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

        _rows = new ObservableCollection<PdfExportPageRow>(
            pages.Select(page => new PdfExportPageRow
            {
                IsSelected = selectedFolders.Contains(NormalizePathForCompare(page.FolderPath)),
                PageFolder = page.FolderPath,
                Name = page.Name,
                Source = System.IO.Path.GetFileName(page.PdfPath),
            }));
        foreach (PdfExportPageRow row in _rows)
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PdfExportPageRow.IsSelected))
                {
                    SyncFolderCheckStates();
                    UpdateExportButton();
                }
            };

        Title = "Export PDF";
        Width = 780;
        Height = 560;
        MinWidth = 620;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        var root = new DockPanel { Margin = new Thickness(12) };
        Content = root;

        var title = new TextBlock
        {
            Text = "Choose sheets to export",
            FontWeight = FontWeights.Normal,
            Margin = new Thickness(0, 0, 0, 8),
        };
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
        };
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        var allButton = new Button { Content = "All", MinWidth = 64, Margin = new Thickness(0, 0, 6, 0) };
        var noneButton = new Button { Content = "None", MinWidth = 64, Margin = new Thickness(0, 0, 12, 0) };
        _includeMeasurementsBox = new CheckBox
        {
            Content = "Measurements",
            IsChecked = includeMeasurements,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        _includeAnnotationsBox = new CheckBox
        {
            Content = "Markups",
            IsChecked = includeAnnotations,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        _includeLegendBox = new CheckBox
        {
            Content = "Legend",
            IsChecked = includeLegend,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        var strokeLabel = new TextBlock
        {
            Text = "Stroke",
            ToolTip = "Exported measurement line thickness.",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
        };
        _measurementStrokeBox = new TextBox
        {
            Width = 52,
            Height = 24,
            Text = ClampScale(
                measurementStrokeScale,
                fallback: 1.5,
                MinMeasurementStrokeScale,
                MaxMeasurementStrokeScale).ToString("0.##", CultureInfo.InvariantCulture),
            ToolTip = "Measurement line thickness multiplier, 0.25 - 10. Press Export to save it.",
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        toolbar.Children.Add(allButton);
        toolbar.Children.Add(noneButton);
        toolbar.Children.Add(_includeMeasurementsBox);
        toolbar.Children.Add(_includeAnnotationsBox);
        toolbar.Children.Add(_includeLegendBox);
        toolbar.Children.Add(strokeLabel);
        toolbar.Children.Add(_measurementStrokeBox);

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

        _exportButton = new Button
        {
            Content = "Export",
            MinWidth = 84,
            IsDefault = true,
            Margin = new Thickness(0, 0, 6, 0),
        };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 84, IsCancel = true };
        buttons.Children.Add(_exportButton);
        buttons.Children.Add(cancelButton);

        var tree = new TreeView
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(tree, ScrollBarVisibility.Disabled);
        root.Children.Add(tree);
        BuildPagesTree(tree, pagesRoot, currentPageFolder);

        allButton.Click += (_, _) => SetAll(true);
        noneButton.Click += (_, _) => SetAll(false);
        _exportButton.Click += (_, _) =>
        {
            if (!_rows.Any(row => row.IsSelected))
            {
                MessageBox.Show("Select at least one sheet.", "Export PDF", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!TryReadScale(_measurementStrokeBox, MinMeasurementStrokeScale, MaxMeasurementStrokeScale, out double strokeScale))
            {
                MessageBox.Show(
                    "Stroke must be a number from 0.25 to 10.",
                    "Export PDF",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                _measurementStrokeBox.Focus();
                _measurementStrokeBox.SelectAll();
                return;
            }

            _measurementStrokeBox.Text = strokeScale.ToString("0.##", CultureInfo.InvariantCulture);
            DialogResult = true;
        };

        Loaded += (_, _) => UpdateExportButton();
    }

    // Pages render as the same folder hierarchy the Pages tree uses, so the
    // selection reads like the job, not a flat path list. Folder checkboxes
    // toggle every sheet below; the currently open sheet is highlighted.
    private void BuildPagesTree(TreeView tree, string pagesRoot, string currentPageFolder)
    {
        string rootPath = NormalizePathForCompare(pagesRoot);
        string current = NormalizePathForCompare(currentPageFolder);
        var folderItems = new Dictionary<string, TreeViewItem>(StringComparer.OrdinalIgnoreCase);
        var folderRows = new Dictionary<string, List<PdfExportPageRow>>(StringComparer.OrdinalIgnoreCase);
        TreeViewItem? currentItem = null;

        foreach (PdfExportPageRow row in _rows)
        {
            string full = NormalizePathForCompare(row.PageFolder);
            string rel = rootPath.Length > 0 &&
                         full.StartsWith(rootPath + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? full[(rootPath.Length + 1)..]
                : System.IO.Path.GetFileName(full);
            string[] segments = rel.Split(
                System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar);

            ItemsControl parent = tree;
            string acc = "";
            for (int i = 0; i < segments.Length - 1; i++)
            {
                acc = acc.Length == 0 ? segments[i] : acc + "\\" + segments[i];
                if (!folderItems.TryGetValue(acc, out TreeViewItem? folderItem))
                {
                    var rowsUnder = new List<PdfExportPageRow>();
                    folderItem = CreateFolderTreeItem(segments[i], rowsUnder);
                    folderItems[acc] = folderItem;
                    folderRows[acc] = rowsUnder;
                    parent.Items.Add(folderItem);
                }
                folderRows[acc].Add(row);
                parent = folderItem;
            }

            bool isCurrent = current.Length > 0 && string.Equals(full, current, StringComparison.OrdinalIgnoreCase);
            TreeViewItem pageItem = CreatePageTreeItem(row, isCurrent);
            parent.Items.Add(pageItem);
            if (isCurrent)
                currentItem = pageItem;
        }

        SyncFolderCheckStates();

        if (currentItem != null)
        {
            Loaded += (_, _) =>
            {
                for (var node = ItemsControl.ItemsControlFromItemContainer(currentItem) as TreeViewItem;
                     node != null;
                     node = ItemsControl.ItemsControlFromItemContainer(node) as TreeViewItem)
                {
                    node.IsExpanded = true;
                }
                currentItem.BringIntoView();
            };
        }
    }

    private TreeViewItem CreateFolderTreeItem(string name, List<PdfExportPageRow> rowsUnder)
    {
        var box = new CheckBox
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        box.Click += (_, _) =>
        {
            if (_syncingFolderChecks)
                return;

            bool select = box.IsChecked == true;
            _syncingFolderChecks = true;
            try
            {
                foreach (PdfExportPageRow row in rowsUnder)
                    row.IsSelected = select;
            }
            finally
            {
                _syncingFolderChecks = false;
            }
            SyncFolderCheckStates();
            UpdateExportButton();
        };
        _folderChecks.Add((box, rowsUnder));

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(box);
        header.Children.Add(new TextBlock
        {
            Text = $"📁 {name}",
            VerticalAlignment = VerticalAlignment.Center,
        });
        var count = new TextBlock
        {
            Foreground = Brushes.Gray,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Bound lazily: rowsUnder keeps filling while the tree builds.
        Loaded += (_, _) => count.Text = rowsUnder.Count == 1 ? "1 sheet" : $"{rowsUnder.Count} sheets";
        header.Children.Add(count);

        return new TreeViewItem
        {
            Header = header,
            IsExpanded = true,
        };
    }

    private TreeViewItem CreatePageTreeItem(PdfExportPageRow row, bool isCurrent)
    {
        var box = new CheckBox
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        box.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(PdfExportPageRow.IsSelected))
        {
            Source = row,
            Mode = BindingMode.TwoWay,
        });

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(box);
        var name = new TextBlock
        {
            Text = row.Name,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal,
        };
        header.Children.Add(name);
        if (isCurrent)
        {
            header.Children.Add(new TextBlock
            {
                Text = "open now",
                Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x5C, 0xA8)),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        if (!string.IsNullOrWhiteSpace(row.Source))
        {
            header.Children.Add(new TextBlock
            {
                Text = row.Source,
                Foreground = Brushes.Gray,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        // Clicking anywhere on the row text toggles the sheet.
        header.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is not System.Windows.Controls.Primitives.ToggleButton)
                row.IsSelected = !row.IsSelected;
        };

        object headerContent = header;
        if (isCurrent)
        {
            headerContent = new Border
            {
                Background = CurrentSheetBrush,
                BorderBrush = CurrentSheetBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 6, 1),
                Child = header,
            };
        }

        return new TreeViewItem
        {
            Header = headerContent,
            IsExpanded = false,
        };
    }

    private void SyncFolderCheckStates()
    {
        if (_syncingFolderChecks)
            return;

        _syncingFolderChecks = true;
        try
        {
            foreach ((CheckBox box, List<PdfExportPageRow> rowsUnder) in _folderChecks)
            {
                int selected = rowsUnder.Count(row => row.IsSelected);
                box.IsChecked = selected == 0 ? false : selected == rowsUnder.Count ? true : null;
            }
        }
        finally
        {
            _syncingFolderChecks = false;
        }
    }

    private void SetAll(bool selected)
    {
        foreach (PdfExportPageRow row in _rows)
            row.IsSelected = selected;
        SyncFolderCheckStates();
        UpdateExportButton();
    }

    private void UpdateExportButton()
    {
        _exportButton.IsEnabled = _rows.Any(row => row.IsSelected);
    }

    private static bool TryReadScale(TextBox box, double min, double max, out double scale)
    {
        string raw = box.Text.Trim().Replace(",", ".", StringComparison.Ordinal);
        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out scale) ||
            double.IsNaN(scale) ||
            double.IsInfinity(scale) ||
            scale < min ||
            scale > max)
        {
            scale = 0;
            return false;
        }

        return true;
    }

    private static double ReadScale(TextBox box, double fallback, double min, double max) =>
        TryReadScale(box, min, max, out double scale)
            ? scale
            : ClampScale(fallback, fallback, min, max);

    private static double ClampScale(double value, double fallback, double min, double max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            return fallback;

        return System.Math.Clamp(value, min, max);
    }

    private static string NormalizePathForCompare(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        try
        {
            return System.IO.Path.GetFullPath(path)
                .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim()
                .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }
    }
}
