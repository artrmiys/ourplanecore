using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace OurPlanCore.Controls;

public sealed class EstimatingWindowRow
{
    public string Item { get; init; } = "";
    public string Page { get; init; } = "";
    public string Sections { get; init; } = "";
    public string Quantity { get; init; } = "";
    public string Unit { get; init; } = "";
    public string UnitPrice { get; init; } = "";
    public string Cost { get; init; } = "";
    public string Notes { get; init; } = "";
    public TakeoffItem? Takeoff { get; init; }
    public Measurement? Measurement { get; init; }
}

public sealed class EstimatingWindow : Window
{
    private readonly Func<string, bool, IReadOnlyList<EstimatingWindowRow>> _loadRows;
    private readonly Action<IReadOnlyList<EstimatingWindowRow>> _selectRowsOnCanvas;
    private readonly Action<EstimatingWindowRow> _goToRowPage;
    private readonly Action<EstimatingWindowRow> _openRowProperties;
    private readonly TextBox _filterBox;
    private readonly CheckBox _currentSheetOnlyBox;
    private readonly DataGrid _grid;
    private readonly TextBlock _statusText;
    private readonly Button _selectButton;
    private readonly Button _pageButton;
    private readonly Button _propertiesButton;
    private IReadOnlyList<EstimatingWindowRow> _rows = [];

    public EstimatingWindow(
        Func<string, bool, IReadOnlyList<EstimatingWindowRow>> loadRows,
        Action<IReadOnlyList<EstimatingWindowRow>> selectRowsOnCanvas,
        Action<EstimatingWindowRow> goToRowPage,
        Action<EstimatingWindowRow> openRowProperties)
    {
        _loadRows = loadRows;
        _selectRowsOnCanvas = selectRowsOnCanvas;
        _goToRowPage = goToRowPage;
        _openRowProperties = openRowProperties;

        Title = "Estimating";
        Width = 1120;
        Height = 680;
        MinWidth = 820;
        MinHeight = 460;

        var root = new DockPanel { Margin = new Thickness(8) };
        Content = root;

        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        var rightActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        DockPanel.SetDock(rightActions, Dock.Right);
        toolbar.Children.Add(rightActions);

        _currentSheetOnlyBox = new CheckBox
        {
            Content = "Current sheet",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            ToolTip = "Show only measurements on the active sheet",
        };
        _currentSheetOnlyBox.Checked += (_, _) => RefreshRows();
        _currentSheetOnlyBox.Unchecked += (_, _) => RefreshRows();
        rightActions.Children.Add(_currentSheetOnlyBox);

        _selectButton = WindowButton("Select", "Select selected estimate rows on the canvas", SelectSelectedRowsOnCanvas);
        _pageButton = WindowButton("Page", "Open the first selected estimate row's sheet", GoToSelectedRowPage);
        _propertiesButton = WindowButton("Props", "Edit the selected estimate row", OpenSelectedRowProperties);
        rightActions.Children.Add(_selectButton);
        rightActions.Children.Add(_pageButton);
        rightActions.Children.Add(_propertiesButton);
        rightActions.Children.Add(WindowButton("Refresh", "Reload rows from the current job", RefreshRows));
        rightActions.Children.Add(WindowButton("Copy", "Copy selected rows, or all rows when nothing is selected", CopyRows));

        _filterBox = new TextBox
        {
            MinWidth = 260,
            MinHeight = 24,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Filter by item, section, page, or notes",
        };
        _filterBox.TextChanged += (_, _) => RefreshRows();
        toolbar.Children.Add(_filterBox);

        _statusText = new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 11,
        };
        DockPanel.SetDock(_statusText, Dock.Bottom);
        root.Children.Add(_statusText);

        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserResizeRows = false,
            CanUserSortColumns = true,
            EnableColumnVirtualization = true,
            EnableRowVirtualization = true,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow,
        };
        _grid.Columns.Add(TextColumn("Item / Section", nameof(EstimatingWindowRow.Item), 240));
        _grid.Columns.Add(TextColumn("Type / Page", nameof(EstimatingWindowRow.Page), 145));
        _grid.Columns.Add(TextColumn("Sec", nameof(EstimatingWindowRow.Sections), 62));
        _grid.Columns.Add(TextColumn("Qty", nameof(EstimatingWindowRow.Quantity), 95));
        _grid.Columns.Add(TextColumn("Unit", nameof(EstimatingWindowRow.Unit), 64));
        _grid.Columns.Add(TextColumn("Price", nameof(EstimatingWindowRow.UnitPrice), 82));
        _grid.Columns.Add(TextColumn("Cost", nameof(EstimatingWindowRow.Cost), 92));
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Notes",
            Binding = new Binding(nameof(EstimatingWindowRow.Notes)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            MinWidth = 220,
        });
        _grid.SelectionChanged += (_, _) => UpdateActionButtons();
        _grid.MouseDoubleClick += (_, _) => GoToSelectedRowPage();
        root.Children.Add(_grid);

        Loaded += (_, _) =>
        {
            RefreshRows();
            _filterBox.Focus();
        };
    }

    public void RefreshRows()
    {
        _rows = _loadRows(_filterBox.Text.Trim(), _currentSheetOnlyBox.IsChecked == true);
        _grid.ItemsSource = _rows;
        UpdateActionButtons();
    }

    private static Button WindowButton(string text, string tooltip, Action action)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 56,
            Padding = new Thickness(7, 3, 7, 3),
            Margin = new Thickness(0, 0, 5, 0),
            ToolTip = tooltip,
        };
        button.Click += (_, _) => action();
        return button;
    }

    private static DataGridTextColumn TextColumn(string header, string path, double width) =>
        new()
        {
            Header = header,
            Binding = new Binding(path),
            Width = width,
        };

    private List<EstimatingWindowRow> SelectedRows() =>
        _grid.SelectedItems
            .OfType<EstimatingWindowRow>()
            .ToList();

    private void UpdateActionButtons()
    {
        List<EstimatingWindowRow> selected = SelectedRows();
        bool hasSelection = selected.Count > 0;
        _selectButton.IsEnabled = hasSelection;
        _pageButton.IsEnabled = hasSelection;
        _propertiesButton.IsEnabled = selected.Count == 1 && selected[0].Takeoff != null;
        _statusText.Text = $"{_rows.Count} row(s) | {selected.Count} selected";
    }

    private void SelectSelectedRowsOnCanvas()
    {
        List<EstimatingWindowRow> selected = SelectedRows();
        if (selected.Count > 0)
            _selectRowsOnCanvas(selected);
    }

    private void GoToSelectedRowPage()
    {
        if (SelectedRows().FirstOrDefault() is { } row)
            _goToRowPage(row);
    }

    private void OpenSelectedRowProperties()
    {
        if (SelectedRows() is [EstimatingWindowRow row] && row.Takeoff != null)
            _openRowProperties(row);
    }

    private void CopyRows()
    {
        IReadOnlyList<EstimatingWindowRow> rows = SelectedRows();
        if (rows.Count == 0)
            rows = _rows;
        if (rows.Count == 0)
            return;

        var sb = new StringBuilder();
        sb.AppendLine("Item\tType/Page\tSec\tQty\tUnit\tPrice\tCost\tNotes");
        foreach (EstimatingWindowRow row in rows)
        {
            sb.AppendLine(string.Join(
                "\t",
                CleanCell(row.Item),
                CleanCell(row.Page),
                CleanCell(row.Sections),
                CleanCell(row.Quantity),
                CleanCell(row.Unit),
                CleanCell(row.UnitPrice),
                CleanCell(row.Cost),
                CleanCell(row.Notes)));
        }

        Clipboard.SetText(sb.ToString());
        _statusText.Text = $"Copied {rows.Count} row(s).";
    }

    private static string CleanCell(string value) =>
        value.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
}
