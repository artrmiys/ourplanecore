using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OurPlanCore;

namespace OurPlanCore.Controls;

// Offset companion requested in the dialog: a parallel line item created
// alongside the main line takeoff (multiline drawing).
public sealed record NewItemOffsetSpec(string Name, string Color, double Meters, bool RightSide);

public sealed class NewItemDialog : Window
{
    public string ItemName  { get; private set; } = "New Item";
    public string ItemColor { get; private set; } = "#FF4444";
    public string ItemType  { get; private set; } = "line";
    public string ItemCountSymbol { get; private set; } = CountDisplaySymbol.Circle;
    public bool MarkSimilarOnCurrentSheet { get; private set; }
    public IReadOnlyList<NewItemOffsetSpec> OffsetLines { get; private set; } = [];

    private static readonly (string Label, string Hex)[] Presets =
    [
        ("Red",    "#FF4444"),
        ("Blue",   "#2196F3"),
        ("Green",  "#4CAF50"),
        ("Orange", "#FF9800"),
        ("Purple", "#9C27B0"),
        ("Cyan",   "#00BCD4"),
        ("Yellow", "#FFC107"),
        ("Pink",   "#E91E63"),
        ("Gray",   "#808080"),
        ("Teal",   "#009688"),
        ("Indigo", "#3F51B5"),
        ("Lime",   "#8BC34A"),
        ("Brown",  "#795548"),
        ("Navy",   "#0D47A1"),
        ("Black",  "#212121"),
    ];

    private sealed record TypeOption(string Label, string Value);

    private static readonly TypeOption[] TypeOptions =
    [
        new("Line", "line"),
        new("Area", "area"),
        new("Count", "point"),
    ];

    private sealed record CountSymbolOption(string Label, string Value);

    private static readonly CountSymbolOption[] CountSymbolOptions =
        CountDisplaySymbol.All
            .Select(symbol => new CountSymbolOption(CountDisplaySymbol.Title(symbol), symbol))
            .ToArray();

    public NewItemDialog(
        string defaultType = "line",
        string defaultName = "New Item",
        bool lockType = false,
        string defaultColor = "#FF4444",
        string defaultCountSymbol = CountDisplaySymbol.Circle,
        int? initialNameSelectionLength = null,
        int? initialNameCaretIndex = null,
        bool showOffsetLines = false,
        bool showSimilarReviewOption = false,
        bool defaultSimilarReview = false,
        string similarReviewOptionText = "Review similar on this sheet",
        UnitMode unitMode = UnitMode.Metric)
    {
        Title                 = "New Takeoff Item";
        Width                 = 320;
        SizeToContent         = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode            = ResizeMode.NoResize;

        var panel = new StackPanel { Margin = new Thickness(12) };

        panel.Children.Add(new TextBlock { Text = "Name:", Margin = new Thickness(0, 0, 0, 4) });
        var nameBox = new TextBox { Text = string.IsNullOrWhiteSpace(defaultName) ? "New Item" : defaultName };
        panel.Children.Add(nameBox);

        panel.Children.Add(new TextBlock { Text = "Measurement type:", Margin = new Thickness(0, 10, 0, 4) });
        var typeBox = new ComboBox
        {
            ItemsSource = TypeOptions,
            DisplayMemberPath = nameof(TypeOption.Label),
            SelectedValuePath = nameof(TypeOption.Value),
            SelectedValue = NormalizeType(defaultType),
            IsEnabled = !lockType,
        };
        panel.Children.Add(typeBox);

        var countSymbolLabel = new TextBlock { Text = "Count display:", Margin = new Thickness(0, 10, 0, 4) };
        panel.Children.Add(countSymbolLabel);
        var countSymbolBox = new ComboBox
        {
            ItemsSource = CountSymbolOptions,
            DisplayMemberPath = nameof(CountSymbolOption.Label),
            SelectedValuePath = nameof(CountSymbolOption.Value),
            SelectedValue = CountDisplaySymbol.Normalize(defaultCountSymbol),
        };
        panel.Children.Add(countSymbolBox);
        void RefreshCountSymbolVisibility()
        {
            Visibility visibility = string.Equals(typeBox.SelectedValue?.ToString(), "point", System.StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
            countSymbolLabel.Visibility = visibility;
            countSymbolBox.Visibility = visibility;
        }
        typeBox.SelectionChanged += (_, _) => RefreshCountSymbolVisibility();
        RefreshCountSymbolVisibility();

        CheckBox? similarReviewBox = null;
        if (showSimilarReviewOption)
        {
            similarReviewBox = new CheckBox
            {
                Content = string.IsNullOrWhiteSpace(similarReviewOptionText)
                    ? "Review similar on this sheet"
                    : similarReviewOptionText.Trim(),
                IsChecked = defaultSimilarReview,
                Margin = new Thickness(0, 10, 0, 0),
                ToolTip = "Open the Similar review for the current sheet after creating this Count item.",
            };
            panel.Children.Add(similarReviewBox);

            void RefreshSimilarReviewVisibility()
            {
                similarReviewBox.Visibility =
                    string.Equals(typeBox.SelectedValue?.ToString(), "point", System.StringComparison.OrdinalIgnoreCase)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            }
            typeBox.SelectionChanged += (_, _) => RefreshSimilarReviewVisibility();
            RefreshSimilarReviewVisibility();
        }

        panel.Children.Add(new TextBlock { Text = "Color:", Margin = new Thickness(0, 10, 0, 4) });

        string selectedHex = SanitizeColor(defaultColor);

        // The caller hands us a per-takeoff random color; keep it as the real
        // default. Surface it as its own "Auto" swatch when it is not one of
        // the presets so the user still sees what is selected and can switch.
        var palette = new List<(string Label, string Hex)>();
        if (!Presets.Any(p => string.Equals(p.Hex, selectedHex, System.StringComparison.OrdinalIgnoreCase)))
            palette.Add(("Auto", selectedHex));
        palette.AddRange(Presets);

        var swatches = new List<Border>();
        var colorPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };

        foreach (var (label, hex) in palette)
        {
            var swatch = new Border
            {
                Width           = 34,
                Height          = 22,
                Margin          = new Thickness(2),
                Background      = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                BorderBrush     = Brushes.Transparent,
                BorderThickness = new Thickness(2),
                CornerRadius    = new CornerRadius(3),
                ToolTip         = label,
                Cursor          = Cursors.Hand,
            };
            swatches.Add(swatch);
            colorPanel.Children.Add(swatch);
        }

        for (int i = 0; i < swatches.Count; i++)
        {
            int   idx = i;
            string hex = palette[i].Hex;
            swatches[i].MouseLeftButtonDown += (_, _) =>
            {
                selectedHex = hex;
                foreach (var s in swatches) s.BorderBrush = Brushes.Transparent;
                swatches[idx].BorderBrush = Brushes.White;
            };
        }
        int selectedIndex = palette.FindIndex(p => string.Equals(p.Hex, selectedHex, System.StringComparison.OrdinalIgnoreCase));
        if (selectedIndex < 0) selectedIndex = 0;
        swatches[selectedIndex].BorderBrush = Brushes.White;   // default selection highlight

        panel.Children.Add(colorPanel);

        // ── Multiline offsets (line items only, opt-in per call site) ────────
        string unitSuffix = unitMode == UnitMode.Imperial ? "ft" : "m";
        var offsetSection = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        offsetSection.Children.Add(new TextBlock
        {
            Text = "Offset lines:",
            ToolTip = "Each line you draw also creates parallel lines at these distances,\n" +
                      "stored as separate takeoff items with their own name and color.",
        });

        var offsetRows = new List<OffsetRow>();
        OffsetRow AddOffsetRow(string defaultRowColor, bool defaultRight)
        {
            var rowPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 0),
            };
            var enable = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            var rowName = new TextBox { Width = 96, Margin = new Thickness(0, 0, 6, 0), IsEnabled = false };
            var dist = new TextBox { Width = 44, Text = "0.1", Margin = new Thickness(0, 0, 2, 0), IsEnabled = false };
            var unitLabel = new TextBlock { Text = unitSuffix, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            var side = new ComboBox
            {
                ItemsSource = new[] { "Left", "Right" },
                SelectedIndex = defaultRight ? 1 : 0,
                Width = 58,
                Margin = new Thickness(0, 0, 6, 0),
                IsEnabled = false,
            };
            var row = new OffsetRow(enable, rowName, dist, side) { ColorHex = defaultRowColor };
            var swatch = new Border
            {
                Width = 24,
                Height = 18,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(defaultRowColor)),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = "Offset line color",
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = false,
            };
            swatch.MouseLeftButtonDown += (_, _) =>
            {
                var colorMenu = new ContextMenu();
                foreach (var (label, hex) in Presets)
                {
                    var option = new MenuItem
                    {
                        Header = label,
                        Icon = new Border
                        {
                            Width = 14,
                            Height = 14,
                            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                            CornerRadius = new CornerRadius(2),
                        },
                    };
                    string chosen = hex;
                    option.Click += (_, _) =>
                    {
                        row.ColorHex = chosen;
                        swatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(chosen));
                    };
                    colorMenu.Items.Add(option);
                }
                colorMenu.PlacementTarget = swatch;
                colorMenu.IsOpen = true;
            };
            enable.Checked += (_, _) =>
            {
                rowName.IsEnabled = dist.IsEnabled = side.IsEnabled = swatch.IsEnabled = true;
                if (string.IsNullOrWhiteSpace(rowName.Text))
                    rowName.Text = $"{nameBox.Text.Trim()} {(side.SelectedIndex == 1 ? "R" : "L")}";
            };
            enable.Unchecked += (_, _) =>
                rowName.IsEnabled = dist.IsEnabled = side.IsEnabled = swatch.IsEnabled = false;

            rowPanel.Children.Add(enable);
            rowPanel.Children.Add(rowName);
            rowPanel.Children.Add(dist);
            rowPanel.Children.Add(unitLabel);
            rowPanel.Children.Add(side);
            rowPanel.Children.Add(swatch);
            offsetSection.Children.Add(rowPanel);
            offsetRows.Add(row);
            return row;
        }

        if (showOffsetLines)
        {
            AddOffsetRow("#2196F3", defaultRight: false);
            AddOffsetRow("#4CAF50", defaultRight: true);
            panel.Children.Add(offsetSection);
        }

        void RefreshOffsetVisibility()
        {
            offsetSection.Visibility =
                string.Equals(typeBox.SelectedValue?.ToString(), "line", System.StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
        typeBox.SelectionChanged += (_, _) => RefreshOffsetVisibility();
        RefreshOffsetVisibility();

        var buttons = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin              = new Thickness(0, 10, 0, 0),
        };
        var ok     = new Button { Content = "OK",     Width = 70, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 70, IsCancel  = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;

        ok.Click += (_, _) =>
        {
            ItemName     = string.IsNullOrWhiteSpace(nameBox.Text) ? "New Item" : nameBox.Text.Trim();
            ItemColor    = selectedHex;
            ItemType     = typeBox.SelectedValue?.ToString() ?? "line";
            ItemCountSymbol = CountDisplaySymbol.Normalize(countSymbolBox.SelectedValue?.ToString());
            MarkSimilarOnCurrentSheet =
                showSimilarReviewOption &&
                similarReviewBox?.IsChecked == true &&
                string.Equals(ItemType, "point", System.StringComparison.OrdinalIgnoreCase);

            OffsetLines = [];
            if (showOffsetLines && offsetSection.Visibility == Visibility.Visible)
            {
                var specs = new List<NewItemOffsetSpec>();
                foreach (OffsetRow row in offsetRows)
                {
                    if (row.Enable.IsChecked != true)
                        continue;

                    string raw = row.Distance.Text.Trim().Replace(',', '.');
                    if (!double.TryParse(raw, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double value) ||
                        value <= 0)
                    {
                        MessageBox.Show(
                            $"Offset distance must be a positive number ({unitSuffix}).",
                            "New Takeoff Item", MessageBoxButton.OK, MessageBoxImage.Information);
                        row.Distance.Focus();
                        row.Distance.SelectAll();
                        return;
                    }

                    bool right = row.Side.SelectedIndex == 1;
                    double meters = unitMode == UnitMode.Imperial ? value * 0.3048 : value;
                    string offsetName = string.IsNullOrWhiteSpace(row.Name.Text)
                        ? $"{ItemName} {(right ? "R" : "L")}"
                        : row.Name.Text.Trim();
                    specs.Add(new NewItemOffsetSpec(offsetName, row.ColorHex, meters, right));
                }
                OffsetLines = specs;
            }

            DialogResult = true;
        };

        Loaded += (_, _) =>
        {
            nameBox.Focus();
            int caretIndex = initialNameCaretIndex.GetValueOrDefault(-1);
            if (caretIndex >= 0 && caretIndex <= nameBox.Text.Length)
                nameBox.Select(caretIndex, 0);
            else if (initialNameSelectionLength is >= 0 and <= int.MaxValue &&
                     initialNameSelectionLength.Value <= nameBox.Text.Length)
            {
                int selectionLength = initialNameSelectionLength.Value;
                nameBox.Select(0, selectionLength);
            }
            else
            {
                nameBox.SelectAll();
            }
        };
    }

    private sealed class OffsetRow(CheckBox enable, TextBox name, TextBox distance, ComboBox side)
    {
        public CheckBox Enable { get; } = enable;
        public TextBox Name { get; } = name;
        public TextBox Distance { get; } = distance;
        public ComboBox Side { get; } = side;
        public string ColorHex { get; set; } = "#2196F3";
    }

    private static string NormalizeType(string value) =>
        value is "area" or "point" or "count" ? (value == "count" ? "point" : value) : "line";

    private static string SanitizeColor(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(value.Trim());
                return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            }
            catch
            {
                // fall through to default
            }
        }
        return Presets[0].Hex;
    }
}
