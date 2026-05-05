using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SmartTakeoffs;

namespace SmartTakeoffs.Controls;

public enum MarkerSetsDialogAction
{
    None,
    Apply,
    Rename,
    Delete,
    OpenJson,
}

public sealed class MarkerSetsDialogResult
{
    public MarkerSetsDialogAction Action { get; init; }
    public SmartAiMarkerSet MarkerSet { get; init; } = new();
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
}

public sealed class MarkerSetDisplayRow
{
    public MarkerSetDisplayRow(SmartAiMarkerSet set)
    {
        Set = set;
    }

    public SmartAiMarkerSet Set { get; }
    public string Name => Set.Name;
    public string Description => Set.Description;
    public string TypeFilter => Set.TypeFilter;
    public string SampleKindFilter => Set.SampleKindFilter;
    public int MarkerCount => Set.MarkerCount;
    public string UpdatedAt => ShortDate(Set.UpdatedAtUtc);

    private static string ShortDate(string value)
    {
        return DateTime.TryParse(value, out DateTime parsed)
            ? parsed.ToLocalTime().ToString("g")
            : value;
    }
}

public sealed class MarkerSetsDialog : Window
{
    private readonly DataGrid _grid;
    private readonly ObservableCollection<MarkerSetDisplayRow> _rows;

    public MarkerSetsDialogResult? Result { get; private set; }

    public MarkerSetsDialog(IEnumerable<SmartAiMarkerSet> markerSets)
    {
        Title = "Marker Sets";
        Width = 980;
        Height = 520;
        MinWidth = 760;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;

        _rows = new ObservableCollection<MarkerSetDisplayRow>(
            markerSets.Select(set => new MarkerSetDisplayRow(set)));

        var root = new DockPanel { Margin = new Thickness(12) };

        var summary = new TextBlock
        {
            Text = _rows.Count == 0
                ? "No marker sets saved yet."
                : $"Saved marker sets: {_rows.Count}",
            FontWeight = FontWeights.Normal,
            Margin = new Thickness(0, 0, 0, 8),
        };
        DockPanel.SetDock(summary, Dock.Top);
        root.Children.Add(summary);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var apply = new Button { Content = "Apply Filter", MinWidth = 96, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
        var rename = new Button { Content = "Rename...", MinWidth = 86, Margin = new Thickness(0, 0, 6, 0) };
        var delete = new Button { Content = "Delete", MinWidth = 72, Margin = new Thickness(0, 0, 6, 0) };
        var open = new Button { Content = "Open JSON", MinWidth = 86, Margin = new Thickness(0, 0, 18, 0) };
        var close = new Button { Content = "Close", MinWidth = 78, IsCancel = true };
        buttons.Children.Add(apply);
        buttons.Children.Add(rename);
        buttons.Children.Add(delete);
        buttons.Children.Add(open);
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsReadOnly = true,
            ItemsSource = _rows,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
        };
        _grid.Columns.Add(TextColumn("Name", nameof(MarkerSetDisplayRow.Name), 190));
        _grid.Columns.Add(TextColumn("Markers", nameof(MarkerSetDisplayRow.MarkerCount), 70));
        _grid.Columns.Add(TextColumn("Type", nameof(MarkerSetDisplayRow.TypeFilter), 135));
        _grid.Columns.Add(TextColumn("Sample", nameof(MarkerSetDisplayRow.SampleKindFilter), 110));
        _grid.Columns.Add(TextColumn("Updated", nameof(MarkerSetDisplayRow.UpdatedAt), 135));
        _grid.Columns.Add(TextColumn("Description", nameof(MarkerSetDisplayRow.Description), 330));
        _grid.MouseDoubleClick += (_, _) => Complete(MarkerSetsDialogAction.Apply);
        root.Children.Add(_grid);

        apply.Click += (_, _) => Complete(MarkerSetsDialogAction.Apply);
        rename.Click += (_, _) => RenameSelected();
        delete.Click += (_, _) => Complete(MarkerSetsDialogAction.Delete);
        open.Click += (_, _) => Complete(MarkerSetsDialogAction.OpenJson);

        Content = root;
        Loaded += (_, _) =>
        {
            if (_rows.Count > 0)
                _grid.SelectedIndex = 0;
            _grid.Focus();
        };
    }

    private void RenameSelected()
    {
        MarkerSetDisplayRow? row = SelectedRow();
        if (row == null)
            return;

        if (!ShowRenameDialog(row.Set, out string name, out string description))
            return;

        Result = new MarkerSetsDialogResult
        {
            Action = MarkerSetsDialogAction.Rename,
            MarkerSet = row.Set,
            Name = name,
            Description = description,
        };
        DialogResult = true;
    }

    private void Complete(MarkerSetsDialogAction action)
    {
        MarkerSetDisplayRow? row = SelectedRow();
        if (row == null)
            return;

        Result = new MarkerSetsDialogResult
        {
            Action = action,
            MarkerSet = row.Set,
            Name = row.Set.Name,
            Description = row.Set.Description,
        };
        DialogResult = true;
    }

    private MarkerSetDisplayRow? SelectedRow()
    {
        if (_grid.SelectedItem is MarkerSetDisplayRow row)
            return row;

        MessageBox.Show(
            "Select a marker set first.",
            "Marker Sets",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return null;
    }

    private bool ShowRenameDialog(SmartAiMarkerSet set, out string name, out string description)
    {
        name = "";
        description = "";

        var win = new Window
        {
            Title = "Rename Marker Set",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = this,
        };

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = "Name", Margin = new Thickness(0, 0, 0, 4) });
        var nameBox = new TextBox { Text = set.Name, Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(nameBox);

        panel.Children.Add(new TextBlock { Text = "Description / use", Margin = new Thickness(0, 0, 0, 4) });
        var descriptionBox = new TextBox
        {
            Text = set.Description,
            Height = 76,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 10),
        };
        panel.Children.Add(descriptionBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var ok = new Button { Content = "Save", Width = 76, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 76, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text))
            {
                MessageBox.Show(
                    "Marker set name is required.",
                    "Rename Marker Set",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            win.DialogResult = true;
        };
        win.Content = panel;
        win.Loaded += (_, _) => { nameBox.Focus(); nameBox.SelectAll(); };

        if (win.ShowDialog() != true)
            return false;

        name = nameBox.Text.Trim();
        description = descriptionBox.Text.Trim();
        return true;
    }

    private static DataGridTextColumn TextColumn(string header, string property, double width) =>
        new()
        {
            Header = header,
            Binding = new Binding(property),
            Width = width,
            IsReadOnly = true,
        };
}
