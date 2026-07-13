using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OurPlaneCore.Controls;

public sealed class TakeoffFolderPropertiesDialog : Window
{
    public string FolderName { get; private set; }
    public string Notes { get; private set; }
    public string DefaultColor { get; private set; }
    public string DefaultMeasurementType { get; private set; }
    public double? DefaultUnitPrice { get; private set; }
    public string DefaultItemNotes { get; private set; }
    public string DefaultNamePrefix { get; private set; }

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
        new("No default", ""),
        new("Line", "line"),
        new("Area", "area"),
        new("Count", "point"),
    ];

    public TakeoffFolderPropertiesDialog(
        string folderName,
        TakeoffFolderProperties properties,
        bool showEstimatingFields = true)
    {
        FolderName = folderName;
        Notes = properties.Notes;
        DefaultColor = NormalizeColor(properties.DefaultColor);
        DefaultMeasurementType = NormalizeType(properties.DefaultMeasurementType);
        DefaultUnitPrice = properties.DefaultUnitPrice is >= 0 ? properties.DefaultUnitPrice : null;
        DefaultItemNotes = properties.DefaultItemNotes;
        DefaultNamePrefix = properties.DefaultNamePrefix;

        Title = "Takeoff Folder Properties";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var panel = new StackPanel { Margin = new Thickness(12) };

        panel.Children.Add(new TextBlock { Text = "Display name:", Margin = new Thickness(0, 0, 0, 4) });
        var nameBox = new TextBox { Text = string.IsNullOrWhiteSpace(folderName) ? "Takeoff Folder" : folderName };
        panel.Children.Add(nameBox);

        panel.Children.Add(new TextBlock { Text = "Notes:", Margin = new Thickness(0, 10, 0, 4) });
        var notesBox = new TextBox
        {
            Text = properties.Notes,
            MinHeight = 78,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        panel.Children.Add(notesBox);

        panel.Children.Add(new TextBlock { Text = "Default measurement type:", Margin = new Thickness(0, 10, 0, 4) });
        var typeBox = new ComboBox
        {
            ItemsSource = TypeOptions,
            DisplayMemberPath = nameof(TypeOption.Label),
            SelectedValuePath = nameof(TypeOption.Value),
            SelectedValue = DefaultMeasurementType,
        };
        panel.Children.Add(typeBox);

        panel.Children.Add(new TextBlock { Text = "Default name prefix:", Margin = new Thickness(0, 10, 0, 4) });
        var prefixBox = new TextBox
        {
            Text = properties.DefaultNamePrefix,
            ToolTip = "Applied to the suggested name for new takeoff items in this folder.",
        };
        panel.Children.Add(prefixBox);

        var unitPriceBox = new TextBox
        {
            Text = properties.DefaultUnitPrice is >= 0
                ? properties.DefaultUnitPrice.Value.ToString("G", System.Globalization.CultureInfo.InvariantCulture)
                : "",
            ToolTip = "Leave blank to inherit from a parent folder or use no default.",
        };
        if (showEstimatingFields)
        {
            panel.Children.Add(new TextBlock { Text = "Default unit price:", Margin = new Thickness(0, 10, 0, 4) });
            panel.Children.Add(unitPriceBox);
        }

        panel.Children.Add(new TextBlock { Text = "Default notes for new items:", Margin = new Thickness(0, 10, 0, 4) });
        var defaultItemNotesBox = new TextBox
        {
            Text = properties.DefaultItemNotes,
            MinHeight = 70,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ToolTip = "Applied to new takeoff items created in this folder.",
        };
        panel.Children.Add(defaultItemNotesBox);

        panel.Children.Add(new TextBlock { Text = "Default color:", Margin = new Thickness(0, 10, 0, 4) });
        var swatches = new List<Border>();
        var colorPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        string selectedHex = DefaultColor;

        var noDefault = new Button
        {
            Content = "None",
            MinWidth = 52,
            Margin = new Thickness(2),
            Padding = new Thickness(6, 2, 6, 2),
        };
        colorPanel.Children.Add(noDefault);

        foreach (var (label, hex) in Presets)
        {
            var swatch = new Border
            {
                Width = 34,
                Height = 22,
                Margin = new Thickness(2),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(3),
                ToolTip = label,
                Cursor = Cursors.Hand,
            };
            swatches.Add(swatch);
            colorPanel.Children.Add(swatch);
        }

        void RefreshSwatches()
        {
            noDefault.FontWeight = string.IsNullOrWhiteSpace(selectedHex)
                ? FontWeights.Normal
                : FontWeights.Normal;
            foreach (Border swatch in swatches)
                swatch.BorderBrush = Brushes.Transparent;
            int selectedIndex = Array.FindIndex(Presets, preset =>
                string.Equals(preset.Hex, selectedHex, StringComparison.OrdinalIgnoreCase));
            if (selectedIndex >= 0)
                swatches[selectedIndex].BorderBrush = Brushes.White;
        }

        noDefault.Click += (_, _) =>
        {
            selectedHex = "";
            RefreshSwatches();
        };
        for (int i = 0; i < swatches.Count; i++)
        {
            int idx = i;
            string hex = Presets[i].Hex;
            swatches[i].MouseLeftButtonDown += (_, _) =>
            {
                selectedHex = hex;
                RefreshSwatches();
            };
        }
        RefreshSwatches();
        panel.Children.Add(colorPanel);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var ok = new Button { Content = "OK", Width = 76, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 76, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        ok.Click += (_, _) =>
        {
            FolderName = string.IsNullOrWhiteSpace(nameBox.Text)
                ? folderName
                : nameBox.Text.Trim();
            Notes = notesBox.Text.Trim();
            DefaultColor = selectedHex;
            DefaultMeasurementType = typeBox.SelectedValue?.ToString() ?? "";
            if (!showEstimatingFields)
            {
                DefaultUnitPrice = properties.DefaultUnitPrice is >= 0 ? properties.DefaultUnitPrice : null;
            }
            else if (string.IsNullOrWhiteSpace(unitPriceBox.Text))
            {
                DefaultUnitPrice = null;
            }
            else if (double.TryParse(unitPriceBox.Text.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedPrice) &&
                     parsedPrice >= 0)
            {
                DefaultUnitPrice = parsedPrice;
            }
            else
            {
                MessageBox.Show("Enter a valid non-negative default unit price, or leave it blank.",
                                "Takeoff Folder Properties",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                return;
            }

            DefaultItemNotes = defaultItemNotesBox.Text.Trim();
            DefaultNamePrefix = prefixBox.Text.Trim();
            DialogResult = true;
        };

        Content = panel;
        Loaded += (_, _) =>
        {
            nameBox.Focus();
            nameBox.SelectAll();
        };
    }

    private static string NormalizeType(string value) =>
        value is "area" or "point" or "count"
            ? value == "count" ? "point" : value
            : value == "line" ? "line" : "";

    private static string NormalizeColor(string value)
    {
        foreach (var (_, hex) in Presets)
        {
            if (string.Equals(hex, value, StringComparison.OrdinalIgnoreCase))
                return hex;
        }
        return "";
    }
}
