using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace OurPlanCore.Controls;

public sealed class MassingOpeningReviewRow
{
    public bool Keep { get; set; } = true;
    public string SourceMarkerId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Page { get; set; } = "";
    public int WallIndex { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Confidence { get; set; }
    public string Notes { get; set; } = "";

    public static MassingOpeningReviewRow FromOpening(SmartMassingOpening opening, int index) =>
        new()
        {
            Keep = !string.Equals(opening.Status, "rejected", StringComparison.OrdinalIgnoreCase),
            SourceMarkerId = string.IsNullOrWhiteSpace(opening.SourceMarkerId)
                ? $"opening_{index + 1}"
                : opening.SourceMarkerId,
            Type = string.IsNullOrWhiteSpace(opening.Type) ? "opening" : opening.Type,
            Page = opening.Page,
            WallIndex = opening.WallIndex,
            X = opening.Center.X,
            Y = opening.Center.Y,
            Z = opening.Center.Z,
            Width = opening.Width,
            Height = opening.Height,
            Confidence = opening.Confidence,
            Notes = opening.Notes,
        };
}

public sealed class MassingOpeningsReviewDialog : Window
{
    private readonly DataGrid _grid;
    public ObservableCollection<MassingOpeningReviewRow> Rows { get; }
    public List<SmartMassingOpening> ReviewedOpenings { get; private set; }

    public MassingOpeningsReviewDialog(IReadOnlyList<SmartMassingOpening> openings)
    {
        Rows = new ObservableCollection<MassingOpeningReviewRow>(
            openings.Select(MassingOpeningReviewRow.FromOpening));
        ReviewedOpenings = CloneOpenings(openings);

        Title = "Review Opening Projections";
        Width = 1180;
        Height = 620;
        MinWidth = 920;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;

        var root = new DockPanel { Margin = new Thickness(12) };

        var description = new TextBlock
        {
            Text = "Review projected door/window/opening markers. Kept rows become reviewed draft openings; unchecked rows are saved as rejected evidence.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };
        DockPanel.SetDock(description, Dock.Top);
        root.Children.Add(description);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var keepAll = new Button { Content = "Keep All", MinWidth = 82, Margin = new Thickness(0, 0, 6, 0) };
        var rejectAll = new Button { Content = "Reject All", MinWidth = 82, Margin = new Thickness(0, 0, 18, 0) };
        var save = new Button { Content = "Save Reviewed Openings", MinWidth = 164, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", MinWidth = 78, IsCancel = true };
        buttons.Children.Add(keepAll);
        buttons.Children.Add(rejectAll);
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsReadOnly = false,
            ItemsSource = Rows,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow,
        };
        _grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Keep",
            Binding = new Binding(nameof(MassingOpeningReviewRow.Keep))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            },
            Width = 58,
        });
        _grid.Columns.Add(TextColumn("Source Marker", nameof(MassingOpeningReviewRow.SourceMarkerId), 160, true));
        _grid.Columns.Add(TextColumn("Type", nameof(MassingOpeningReviewRow.Type), 92, false));
        _grid.Columns.Add(TextColumn("Wall", nameof(MassingOpeningReviewRow.WallIndex), 64, false));
        _grid.Columns.Add(TextColumn("X", nameof(MassingOpeningReviewRow.X), 76, false));
        _grid.Columns.Add(TextColumn("Y", nameof(MassingOpeningReviewRow.Y), 76, false));
        _grid.Columns.Add(TextColumn("Z", nameof(MassingOpeningReviewRow.Z), 76, false));
        _grid.Columns.Add(TextColumn("Width", nameof(MassingOpeningReviewRow.Width), 82, false));
        _grid.Columns.Add(TextColumn("Height", nameof(MassingOpeningReviewRow.Height), 82, false));
        _grid.Columns.Add(TextColumn("Confidence", nameof(MassingOpeningReviewRow.Confidence), 94, false));
        _grid.Columns.Add(TextColumn("Page", nameof(MassingOpeningReviewRow.Page), 150, true));
        _grid.Columns.Add(TextColumn("Notes", nameof(MassingOpeningReviewRow.Notes), 360, false));
        root.Children.Add(_grid);

        keepAll.Click += (_, _) => SetAllRows(true);
        rejectAll.Click += (_, _) => SetAllRows(false);
        save.Click += (_, _) => SaveReviewedOpenings();

        Content = root;
    }

    private static DataGridTextColumn TextColumn(string header, string property, double width, bool readOnly) =>
        new()
        {
            Header = header,
            Binding = new Binding(property)
            {
                Mode = readOnly ? BindingMode.OneWay : BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            },
            Width = width,
            IsReadOnly = readOnly,
        };

    private void SetAllRows(bool keep)
    {
        foreach (MassingOpeningReviewRow row in Rows)
            row.Keep = keep;
        _grid.Items.Refresh();
    }

    private void SaveReviewedOpenings()
    {
        _grid.CommitEdit(DataGridEditingUnit.Cell, true);
        _grid.CommitEdit(DataGridEditingUnit.Row, true);

        var reviewed = new List<SmartMassingOpening>();
        foreach (MassingOpeningReviewRow row in Rows)
        {
            string type = NormalizeType(row.Type);
            if (row.WallIndex < 0)
            {
                ShowValidation($"Opening '{row.SourceMarkerId}' must have a wall index of 0 or greater.");
                return;
            }

            if (!IsFinite(row.X) || !IsFinite(row.Y) || !IsFinite(row.Z))
            {
                ShowValidation($"Opening '{row.SourceMarkerId}' has an invalid center coordinate.");
                return;
            }

            if (!IsPositive(row.Width) || !IsPositive(row.Height))
            {
                ShowValidation($"Opening '{row.SourceMarkerId}' must have positive width and height.");
                return;
            }

            reviewed.Add(new SmartMassingOpening
            {
                Status = row.Keep ? "reviewed" : "rejected",
                Type = type,
                SourceMarkerId = row.SourceMarkerId.Trim(),
                Page = row.Page.Trim(),
                WallIndex = row.WallIndex,
                Center = new SmartMassingVertex
                {
                    X = Math.Round(row.X, 3),
                    Y = Math.Round(row.Y, 3),
                    Z = Math.Round(row.Z, 3),
                    SourceMarkerId = row.SourceMarkerId.Trim(),
                },
                Width = Math.Round(row.Width, 3),
                Height = Math.Round(row.Height, 3),
                Confidence = Clamp01(row.Confidence),
                Notes = row.Notes.Trim(),
            });
        }

        ReviewedOpenings = reviewed;
        DialogResult = true;
    }

    private static string NormalizeType(string type)
    {
        type = (type ?? "").Trim().ToLowerInvariant();
        return type is "door" or "window" or "opening" ? type : "opening";
    }

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool IsPositive(double value) =>
        IsFinite(value) && value > 0;

    private static double Clamp01(double value) =>
        !IsFinite(value) ? 0 : Math.Max(0, Math.Min(1, value));

    private static void ShowValidation(string message)
    {
        MessageBox.Show(
            message,
            "Review Opening Projections",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static List<SmartMassingOpening> CloneOpenings(IReadOnlyList<SmartMassingOpening> openings) =>
        openings
            .Select(opening => new SmartMassingOpening
            {
                Status = opening.Status,
                Type = opening.Type,
                SourceMarkerId = opening.SourceMarkerId,
                Page = opening.Page,
                WallIndex = opening.WallIndex,
                Center = new SmartMassingVertex
                {
                    X = opening.Center.X,
                    Y = opening.Center.Y,
                    Z = opening.Center.Z,
                    SourceMarkerId = opening.Center.SourceMarkerId,
                },
                Width = opening.Width,
                Height = opening.Height,
                Confidence = opening.Confidence,
                Notes = opening.Notes,
            })
            .ToList();
}
