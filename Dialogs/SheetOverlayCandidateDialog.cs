using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace OurPlaneCore.Controls;

public sealed class SheetOverlayCandidateRow
{
    public int Rank { get; init; }
    public string Current { get; init; } = "";
    public string Sheet { get; init; } = "";
    public string Confidence { get; init; } = "";
    public string Samples { get; init; } = "";
    public string Method { get; init; } = "";
    public string Source { get; init; } = "";
    public string FolderPath { get; init; } = "";
    public SheetOverlayAutoFitCandidateMatch Match { get; init; } = default!;
}

public enum SheetOverlayCandidateAction
{
    UseSelected,
    OpenOverlaySheet,
    EditByPoints,
}

public sealed class SheetOverlayCandidateDialog : Window
{
    private readonly DataGrid _grid;

    public ObservableCollection<SheetOverlayCandidateRow> Rows { get; }
    public SheetOverlayAutoFitCandidateMatch? SelectedMatch { get; private set; }
    public SheetOverlayCandidateAction SelectedAction { get; private set; } = SheetOverlayCandidateAction.UseSelected;

    public SheetOverlayCandidateDialog(
        string baseSheetName,
        IReadOnlyList<SheetOverlayAutoFitCandidateMatch> matches,
        string currentOverlayFolder)
    {
        Title = "Choose Sheet Overlay Candidate";
        Width = 980;
        Height = 560;
        MinWidth = 820;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        Rows = new ObservableCollection<SheetOverlayCandidateRow>(
            matches.Select((match, index) => BuildRow(index, match, currentOverlayFolder)));

        var root = new DockPanel { Margin = new Thickness(12) };

        var header = new TextBlock
        {
            Text = $"Auto-selected overlay candidates for {baseSheetName}",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var useButton = new Button { Content = "Use Selected", MinWidth = 112, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var openButton = new Button { Content = "Use + Open Sheet", MinWidth = 128, Margin = new Thickness(0, 0, 6, 0) };
        var editButton = new Button { Content = "Use + Edit by Points", MinWidth = 142, Margin = new Thickness(0, 0, 18, 0) };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 78, IsCancel = true };
        buttons.Children.Add(useButton);
        buttons.Children.Add(openButton);
        buttons.Children.Add(editButton);
        buttons.Children.Add(cancelButton);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsReadOnly = true,
            ItemsSource = Rows,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
        };
        _grid.Columns.Add(TextColumn("#", nameof(SheetOverlayCandidateRow.Rank), 44));
        _grid.Columns.Add(TextColumn("", nameof(SheetOverlayCandidateRow.Current), 72));
        _grid.Columns.Add(TextColumn("Sheet", nameof(SheetOverlayCandidateRow.Sheet), 230));
        _grid.Columns.Add(TextColumn("Confidence", nameof(SheetOverlayCandidateRow.Confidence), 96));
        _grid.Columns.Add(TextColumn("Samples", nameof(SheetOverlayCandidateRow.Samples), 92));
        _grid.Columns.Add(TextColumn("Method", nameof(SheetOverlayCandidateRow.Method), 120));
        _grid.Columns.Add(TextColumn("Source", nameof(SheetOverlayCandidateRow.Source), 150));
        _grid.Columns.Add(TextColumn("Folder", nameof(SheetOverlayCandidateRow.FolderPath), 260));
        root.Children.Add(_grid);

        useButton.Click += (_, _) => AcceptSelected(SheetOverlayCandidateAction.UseSelected);
        openButton.Click += (_, _) => AcceptSelected(SheetOverlayCandidateAction.OpenOverlaySheet);
        editButton.Click += (_, _) => AcceptSelected(SheetOverlayCandidateAction.EditByPoints);
        _grid.MouseDoubleClick += (_, _) => AcceptSelected(SheetOverlayCandidateAction.UseSelected);
        _grid.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;

            AcceptSelected(SheetOverlayCandidateAction.UseSelected);
            e.Handled = true;
        };
        Loaded += (_, _) =>
        {
            _grid.SelectedIndex = InitialSelectedIndex();
            if (_grid.SelectedItem != null)
                _grid.ScrollIntoView(_grid.SelectedItem);
            _grid.Focus();
        };

        Content = root;
    }

    private int InitialSelectedIndex()
    {
        if (Rows.Count == 0)
            return -1;

        int current = Rows
            .Select((row, index) => (row, index))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.row.Current))
            .Select(pair => pair.index)
            .FirstOrDefault(-1);
        return current >= 0 ? current : 0;
    }

    private static SheetOverlayCandidateRow BuildRow(
        int index,
        SheetOverlayAutoFitCandidateMatch match,
        string currentOverlayFolder) =>
        new()
        {
            Rank = index + 1,
            Current = SameFolder(match.Page.FolderPath, currentOverlayFolder) ? "Current" : "",
            Sheet = match.Page.Name,
            Confidence = string.Format(CultureInfo.InvariantCulture, "{0:0}%", match.Fit.Confidence * 100),
            Samples = string.Format(CultureInfo.InvariantCulture, "{0}/{1}", match.Fit.MatchedSamples, match.Fit.SampleCount),
            Method = match.Fit.Method,
            Source = match.Source,
            FolderPath = match.Page.FolderPath,
            Match = match,
        };

    private static DataGridTextColumn TextColumn(string header, string property, double width) =>
        new()
        {
            Header = header,
            Binding = new Binding(property),
            Width = width,
        };

    private void AcceptSelected(SheetOverlayCandidateAction action)
    {
        if (_grid.SelectedItem is not SheetOverlayCandidateRow row)
            return;

        SelectedMatch = row.Match;
        SelectedAction = action;
        DialogResult = true;
    }

    private static bool SameFolder(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        try
        {
            string normalizedLeft = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedRight = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
