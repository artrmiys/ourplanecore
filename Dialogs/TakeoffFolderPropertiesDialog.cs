using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SmartTakeoffs.Controls;

public sealed class TakeoffFolderPropertiesDialog : Window
{
    public string FolderName { get; private set; }
    public string Notes { get; private set; }
    public string DefaultColor { get; private set; }
    public string DefaultMeasurementType { get; private set; }

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

    private sealed record TypeOption(string Label, string Value);

    private static readonly TypeOption[] TypeOptions =
    [
        new("No default", ""),
        new("Line", "line"),
        new("Area", "area"),
        new("Count", "point"),
    ];

    public TakeoffFolderPropertiesDialog(string folderName, TakeoffFolderProperties properties)
    {
        FolderName = folderName;
        Notes = properties.Notes;
        DefaultColor = NormalizeColor(properties.DefaultColor);
        DefaultMeasurementType = NormalizeType(properties.DefaultMeasurementType);

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
                ? FontWeights.Bold
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
