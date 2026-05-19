using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OurPlaneCore;

namespace OurPlaneCore.Controls;

public sealed class NewItemDialog : Window
{
    public string ItemName  { get; private set; } = "New Item";
    public string ItemColor { get; private set; } = "#FF4444";
    public string ItemType  { get; private set; } = "line";
    public string ItemCountSymbol { get; private set; } = CountDisplaySymbol.Circle;

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
        string defaultCountSymbol = CountDisplaySymbol.Circle)
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
            DialogResult = true;
        };

        Loaded += (_, _) => { nameBox.Focus(); nameBox.SelectAll(); };
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
