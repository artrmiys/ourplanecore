using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

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
    private readonly Button _exportButton;

    public IReadOnlyList<PdfExportPageRow> Rows => _rows;
    public bool IncludeMeasurements => _includeMeasurementsBox.IsChecked == true;
    public bool IncludeAnnotations => _includeAnnotationsBox.IsChecked == true;
    public bool IncludeLegend => _includeLegendBox.IsChecked == true;

    public PdfExportDialog(
        IEnumerable<PageInfo> pages,
        ISet<string> initiallySelected,
        bool includeMeasurements = true,
        bool includeAnnotations = true,
        bool includeLegend = true)
    {
        var selectedFolders = initiallySelected
            .Select(NormalizePathForCompare)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

        _rows = new ObservableCollection<PdfExportPageRow>(
            pages.Select(page => new PdfExportPageRow
            {
                IsSelected = selectedFolders.Count == 0 || selectedFolders.Contains(NormalizePathForCompare(page.FolderPath)),
                PageFolder = page.FolderPath,
                Name = page.Name,
                Source = System.IO.Path.GetFileName(page.PdfPath),
            }));
        foreach (PdfExportPageRow row in _rows)
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PdfExportPageRow.IsSelected))
                    UpdateExportButton();
            };

        Title = "Export PDF";
        Width = 780;
        Height = 540;
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
        };
        toolbar.Children.Add(allButton);
        toolbar.Children.Add(noneButton);
        toolbar.Children.Add(_includeMeasurementsBox);
        toolbar.Children.Add(_includeAnnotationsBox);
        toolbar.Children.Add(_includeLegendBox);

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

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            ItemsSource = _rows,
            SelectionMode = DataGridSelectionMode.Extended,
        };
        grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Export",
            Binding = new Binding(nameof(PdfExportPageRow.IsSelected)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = 70,
        });
        grid.Columns.Add(new DataGridTextColumn { Header = "Sheet", Binding = new Binding(nameof(PdfExportPageRow.Name)), Width = 180 });
        grid.Columns.Add(new DataGridTextColumn { Header = "PDF", Binding = new Binding(nameof(PdfExportPageRow.Source)), Width = 180 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Path", Binding = new Binding(nameof(PdfExportPageRow.PageFolder)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        root.Children.Add(grid);

        allButton.Click += (_, _) => SetAll(true);
        noneButton.Click += (_, _) => SetAll(false);
        _exportButton.Click += (_, _) =>
        {
            grid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
            grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

            if (!_rows.Any(row => row.IsSelected))
            {
                MessageBox.Show("Select at least one sheet.", "Export PDF", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
        };

        Loaded += (_, _) => UpdateExportButton();
    }

    private void SetAll(bool selected)
    {
        foreach (PdfExportPageRow row in _rows)
            row.IsSelected = selected;
        CollectionViewSource.GetDefaultView(_rows).Refresh();
        UpdateExportButton();
    }

    private void UpdateExportButton()
    {
        _exportButton.IsEnabled = _rows.Any(row => row.IsSelected);
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
