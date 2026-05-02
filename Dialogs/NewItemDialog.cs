using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SmartTakeoffs.Controls;

public sealed class NewItemDialog : Window
{
    public string ItemName  { get; private set; } = "New Item";
    public string ItemColor { get; private set; } = "#FF4444";
    public string ItemType  { get; private set; } = "line";

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
    ];

    public NewItemDialog(string defaultType = "line", string defaultName = "New Item", bool lockType = false, string defaultColor = "#FF4444")
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
            ItemsSource = new[] { "line", "area", "point" },
            SelectedItem = NormalizeType(defaultType),
            IsEnabled = !lockType,
        };
        panel.Children.Add(typeBox);

        panel.Children.Add(new TextBlock { Text = "Color:", Margin = new Thickness(0, 10, 0, 4) });

        string selectedHex = NormalizeColor(defaultColor);
        var swatches = new List<Border>();
        var colorPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };

        foreach (var (label, hex) in Presets)
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
            string hex = Presets[i].Hex;
            swatches[i].MouseLeftButtonDown += (_, _) =>
            {
                selectedHex = hex;
                foreach (var s in swatches) s.BorderBrush = Brushes.Transparent;
                swatches[idx].BorderBrush = Brushes.White;
            };
        }
        int selectedIndex = System.Array.FindIndex(Presets, p => p.Hex == selectedHex);
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
            ItemType     = typeBox.SelectedItem?.ToString() ?? "line";
            DialogResult = true;
        };

        Loaded += (_, _) => { nameBox.Focus(); nameBox.SelectAll(); };
    }

    private static string NormalizeType(string value) =>
        value is "area" or "point" ? value : "line";

    private static string NormalizeColor(string value)
    {
        foreach (var (_, hex) in Presets)
        {
            if (string.Equals(hex, value, System.StringComparison.OrdinalIgnoreCase))
                return hex;
        }
        return Presets[0].Hex;
    }
}
