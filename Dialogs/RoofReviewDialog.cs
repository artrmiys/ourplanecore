using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SmartTakeoffs.Controls;

public sealed class RoofGuideReviewRow
{
    public bool Keep { get; set; } = true;
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Label { get; set; } = "";
    public double Confidence { get; set; }
    public string Points { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<string> SourceMarkerIds { get; init; } = [];

    public static RoofGuideReviewRow FromGuide(SmartMassingRoofGuide guide) =>
        new()
        {
            Keep = !string.Equals(guide.Status, "rejected", StringComparison.OrdinalIgnoreCase),
            Id = guide.Id,
            Kind = guide.Kind,
            Label = guide.Label,
            Confidence = guide.Confidence,
            Points = FormatPoints(guide.Points),
            Notes = guide.Notes,
            SourceMarkerIds = guide.SourceMarkerIds.ToList(),
        };

    private static string FormatPoints(IReadOnlyList<SmartMassingPoint> points) =>
        string.Join(
            "; ",
            points.Select(point => string.Format(
                CultureInfo.InvariantCulture,
                "{0:0.###}, {1:0.###}",
                point.X,
                point.Y)));
}

public sealed class RoofReviewDialog : Window
{
    private readonly ComboBox _typeBox;
    private readonly TextBox _pitchBox;
    private readonly TextBox _confidenceBox;
    private readonly TextBox _notesBox;
    private readonly TextBox _reviewNotesBox;
    private readonly DataGrid _grid;

    public ObservableCollection<RoofGuideReviewRow> Rows { get; }
    public SmartMassingRoof ReviewedRoof { get; private set; }

    public RoofReviewDialog(SmartMassingRoof roof)
    {
        ReviewedRoof = CloneRoof(roof);
        Rows = new ObservableCollection<RoofGuideReviewRow>(
            roof.Guides.Select(RoofGuideReviewRow.FromGuide));

        Title = "Review Roof Draft";
        Width = 1120;
        Height = 680;
        MinWidth = 880;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;

        var root = new DockPanel { Margin = new Thickness(12) };

        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        header.Children.Add(Label("Type:", 0, 0));
        _typeBox = new ComboBox
        {
            IsEditable = true,
            ItemsSource = new[] { "unknown", "gable", "hip", "shed", "low_slope", "flat", "custom" },
            Text = string.IsNullOrWhiteSpace(roof.Type) ? "unknown" : roof.Type,
            Margin = new Thickness(0, 0, 8, 6),
        };
        Grid.SetRow(_typeBox, 0);
        Grid.SetColumn(_typeBox, 1);
        header.Children.Add(_typeBox);

        header.Children.Add(Label("Confidence:", 0, 2));
        _confidenceBox = new TextBox
        {
            Text = roof.Confidence.ToString("0.##", CultureInfo.InvariantCulture),
            Margin = new Thickness(0, 0, 0, 6),
        };
        Grid.SetRow(_confidenceBox, 0);
        Grid.SetColumn(_confidenceBox, 3);
        header.Children.Add(_confidenceBox);

        header.Children.Add(Label("Pitch:", 1, 0));
        _pitchBox = new TextBox
        {
            Text = roof.Pitch,
            Margin = new Thickness(0, 0, 8, 6),
        };
        Grid.SetRow(_pitchBox, 1);
        Grid.SetColumn(_pitchBox, 1);
        header.Children.Add(_pitchBox);

        header.Children.Add(Label("Notes:", 2, 0));
        _notesBox = new TextBox
        {
            Text = roof.Notes,
            AcceptsReturn = true,
            Height = 76,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 8, 0),
        };
        Grid.SetRow(_notesBox, 2);
        Grid.SetColumn(_notesBox, 1);
        Grid.SetColumnSpan(_notesBox, 3);
        header.Children.Add(_notesBox);

        var reviewLabel = new TextBlock
        {
            Text = "Review Notes:",
            Margin = new Thickness(0, 0, 0, 4),
        };
        DockPanel.SetDock(reviewLabel, Dock.Top);
        root.Children.Add(reviewLabel);

        _reviewNotesBox = new TextBox
        {
            Text = roof.ReviewNotes,
            AcceptsReturn = true,
            Height = 64,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 10),
        };
        DockPanel.SetDock(_reviewNotesBox, Dock.Top);
        root.Children.Add(_reviewNotesBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var keepAll = new Button { Content = "Keep All", MinWidth = 82, Margin = new Thickness(0, 0, 6, 0) };
        var rejectAll = new Button { Content = "Reject All", MinWidth = 82, Margin = new Thickness(0, 0, 18, 0) };
        var save = new Button { Content = "Save Reviewed Roof", MinWidth = 142, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
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
            Binding = new Binding(nameof(RoofGuideReviewRow.Keep))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            },
            Width = 58,
        });
        _grid.Columns.Add(TextColumn("Id", nameof(RoofGuideReviewRow.Id), 150, true));
        _grid.Columns.Add(TextColumn("Kind", nameof(RoofGuideReviewRow.Kind), 116, false));
        _grid.Columns.Add(TextColumn("Label", nameof(RoofGuideReviewRow.Label), 220, false));
        _grid.Columns.Add(TextColumn("Confidence", nameof(RoofGuideReviewRow.Confidence), 92, false));
        _grid.Columns.Add(TextColumn("Points", nameof(RoofGuideReviewRow.Points), 260, false));
        _grid.Columns.Add(TextColumn("Notes", nameof(RoofGuideReviewRow.Notes), 300, false));
        root.Children.Add(_grid);

        keepAll.Click += (_, _) => SetAllRows(true);
        rejectAll.Click += (_, _) => SetAllRows(false);
        save.Click += (_, _) => SaveReviewedRoof();

        Content = root;
    }

    private static TextBlock Label(string text, int row, int column)
    {
        var label = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 6),
        };
        Grid.SetRow(label, row);
        Grid.SetColumn(label, column);
        return label;
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
        foreach (RoofGuideReviewRow row in Rows)
            row.Keep = keep;
        _grid.Items.Refresh();
    }

    private void SaveReviewedRoof()
    {
        _grid.CommitEdit(DataGridEditingUnit.Cell, true);
        _grid.CommitEdit(DataGridEditingUnit.Row, true);

        if (!TryParseConfidence(_confidenceBox.Text, out double confidence))
        {
            MessageBox.Show(
                "Roof confidence must be a number between 0 and 1, or a percent such as 65%.",
                "Review Roof Draft",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var guides = new List<SmartMassingRoofGuide>();
        foreach (RoofGuideReviewRow row in Rows.Where(row => row.Keep))
        {
            if (!TryParsePoints(row.Points, row.SourceMarkerIds.FirstOrDefault() ?? "", out List<SmartMassingPoint> points, out string error))
            {
                MessageBox.Show(
                    $"Cannot save guide '{row.Id}':\n{error}",
                    "Review Roof Draft",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            guides.Add(new SmartMassingRoofGuide
            {
                Id = string.IsNullOrWhiteSpace(row.Id) ? $"roof_guide_{guides.Count + 1}" : row.Id.Trim(),
                Status = "reviewed",
                Kind = string.IsNullOrWhiteSpace(row.Kind) ? "guide" : row.Kind.Trim(),
                Label = row.Label.Trim(),
                Confidence = Clamp01(row.Confidence),
                Points = points,
                SourceMarkerIds = row.SourceMarkerIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Notes = row.Notes.Trim(),
            });
        }

        ReviewedRoof = new SmartMassingRoof
        {
            Status = "reviewed",
            Type = string.IsNullOrWhiteSpace(_typeBox.Text) ? "unknown" : _typeBox.Text.Trim(),
            Pitch = _pitchBox.Text.Trim(),
            Notes = _notesBox.Text.Trim(),
            Confidence = confidence,
            ReviewedAtUtc = DateTime.UtcNow.ToString("O"),
            ReviewNotes = _reviewNotesBox.Text.Trim(),
            SourceMarkerIds = ReviewedRoof.SourceMarkerIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Guides = guides,
        };

        DialogResult = true;
    }

    private static bool TryParseConfidence(string text, out double value)
    {
        value = 0;
        text = (text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
            return true;

        bool isPercent = text.EndsWith('%');
        if (isPercent)
            text = text[..^1].Trim();

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
        {
            return false;
        }

        if (isPercent || value > 1)
            value /= 100.0;

        value = Clamp01(value);
        return true;
    }

    private static bool TryParsePoints(
        string text,
        string sourceMarkerId,
        out List<SmartMassingPoint> points,
        out string error)
    {
        points = [];
        error = "";
        foreach (string segment in (text ?? "").Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string clean = segment.Trim();
            if (clean.Length == 0)
                continue;

            string[] parts = clean
                .Replace("(", "", StringComparison.Ordinal)
                .Replace(")", "", StringComparison.Ordinal)
                .Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                error = $"Point '{clean}' must look like x, y.";
                return false;
            }

            if (!TryParseDouble(parts[0], out double x) || !TryParseDouble(parts[1], out double y))
            {
                error = $"Point '{clean}' has an invalid number.";
                return false;
            }

            points.Add(new SmartMassingPoint
            {
                X = Math.Round(x, 3),
                Y = Math.Round(y, 3),
                SourceMarkerId = sourceMarkerId,
            });
        }

        return true;
    }

    private static bool TryParseDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static double Clamp01(double value) =>
        double.IsNaN(value) || double.IsInfinity(value)
            ? 0
            : Math.Max(0, Math.Min(1, value));

    private static SmartMassingRoof CloneRoof(SmartMassingRoof roof) =>
        new()
        {
            Status = roof.Status,
            Type = roof.Type,
            Pitch = roof.Pitch,
            Notes = roof.Notes,
            Confidence = roof.Confidence,
            ReviewedAtUtc = roof.ReviewedAtUtc,
            ReviewNotes = roof.ReviewNotes,
            SourceMarkerIds = roof.SourceMarkerIds.ToList(),
            Guides = roof.Guides
                .Select(guide => new SmartMassingRoofGuide
                {
                    Id = guide.Id,
                    Status = guide.Status,
                    Kind = guide.Kind,
                    Label = guide.Label,
                    Confidence = guide.Confidence,
                    Points = guide.Points
                        .Select(point => new SmartMassingPoint
                        {
                            X = point.X,
                            Y = point.Y,
                            SourceMarkerId = point.SourceMarkerId,
                        })
                        .ToList(),
                    SourceMarkerIds = guide.SourceMarkerIds.ToList(),
                    Notes = guide.Notes,
                })
                .ToList(),
        };
}
