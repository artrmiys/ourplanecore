using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OurPlanCore.Controls;

public sealed class ColorSwatchPicker : WrapPanel
{
    private readonly List<(string Hex, Border Swatch)> _swatches = [];
    private string? _customColor;
    private string _selectedColor;

    public ColorSwatchPicker(string selectedColor)
    {
        _selectedColor = BeamAnnotationConfig.NormalizeColor(selectedColor);
        if (!IsPreset(_selectedColor))
            _customColor = _selectedColor;

        Margin = new Thickness(0, 0, 0, 2);
        IsEnabledChanged += (_, _) => Opacity = IsEnabled ? 1.0 : 0.45;
        RebuildSwatches();
    }

    public event Action<string>? SelectedColorChanged;

    public string SelectedColor => _selectedColor;

    public void SetSelectedColor(string color)
    {
        string normalized = BeamAnnotationConfig.NormalizeColor(color);
        bool needsRebuild = !IsPreset(normalized) &&
                            !string.Equals(_customColor, normalized, StringComparison.OrdinalIgnoreCase);
        _selectedColor = normalized;
        if (!IsPreset(normalized))
            _customColor = normalized;

        if (needsRebuild)
            RebuildSwatches();
        else
            RefreshSelection();
    }

    private void RebuildSwatches()
    {
        Children.Clear();
        _swatches.Clear();

        foreach (AnnotationColorChoice choice in Choices())
            AddSwatch(choice);

        RefreshSelection();
    }

    private IEnumerable<AnnotationColorChoice> Choices()
    {
        foreach (AnnotationColorChoice choice in AnnotationColorPalette.Presets)
            yield return choice;

        if (!string.IsNullOrWhiteSpace(_customColor) && !IsPreset(_customColor))
            yield return new AnnotationColorChoice("Saved color", _customColor);
    }

    private void AddSwatch(AnnotationColorChoice choice)
    {
        string hex = choice.Hex;
        var swatch = new Border
        {
            Width = 22,
            Height = 22,
            Margin = new Thickness(2),
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(2),
            Cursor = Cursors.Hand,
            ToolTip = choice.Label,
        };
        AutomationProperties.SetName(swatch, $"{choice.Label} color");
        swatch.MouseLeftButtonUp += (_, _) => SelectFromPointer(hex);
        _swatches.Add((hex, swatch));
        Children.Add(swatch);
    }

    private void SelectFromPointer(string color)
    {
        if (!IsEnabled)
            return;

        _selectedColor = color;
        RefreshSelection();
        SelectedColorChanged?.Invoke(_selectedColor);
    }

    private void RefreshSelection()
    {
        Brush ring = Application.Current?.Resources["ControlForegroundBrush"] as Brush ?? Brushes.White;
        foreach ((string hex, Border swatch) in _swatches)
        {
            swatch.BorderBrush = string.Equals(hex, _selectedColor, StringComparison.OrdinalIgnoreCase)
                ? ring
                : Brushes.Transparent;
        }
    }

    private static bool IsPreset(string color) =>
        AnnotationColorPalette.Presets.Any(choice =>
            string.Equals(choice.Hex, color, StringComparison.OrdinalIgnoreCase));
}
