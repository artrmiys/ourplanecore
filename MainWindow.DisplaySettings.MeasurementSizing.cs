using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace OurPlaneCore;

public partial class MainWindow
{
    private bool _viewportScaleDirty;

    private void BtnMeasurementLabelSmall_Click(object sender, RoutedEventArgs e) =>
        SetMeasurementLabelScale(0.75);

    private void BtnMeasurementLabelNormal_Click(object sender, RoutedEventArgs e) =>
        SetMeasurementLabelScale(1.00);

    private void BtnMeasurementLabelLarge_Click(object sender, RoutedEventArgs e) =>
        SetMeasurementLabelScale(1.35);

    private void BtnMeasurementLabelApply_Click(object sender, RoutedEventArgs e) =>
        ApplyMeasurementLabelScaleFromText();

    private void TxtMeasurementLabelScale_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;

        ApplyMeasurementLabelScaleFromText();
        e.Handled = true;
    }

    private void TxtMeasurementLabelScale_LostFocus(object sender, RoutedEventArgs e) =>
        ApplyMeasurementLabelScaleFromText();

    private void ApplyMeasurementLabelScaleFromText()
    {
        string raw = TxtMeasurementLabelScale.Text.Trim().Replace(",", ".", StringComparison.Ordinal);
        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double scale) ||
            scale < 0.50 ||
            scale > 3.00)
        {
            TxtMeasurementLabelScale.Text = _settings.MeasurementLabelScale.ToString("0.##", CultureInfo.InvariantCulture);
            TxtStatus.Text = "Value label size must be 0.5 - 3.0.";
            return;
        }

        SetMeasurementLabelScale(scale);
    }

    private void SetMeasurementLabelScale(double scale)
    {
        _settings.MeasurementLabelScale = NormalizeOverlayScale(scale);
        ApplyDisplaySettingsToViewport();
        SaveAppSettings();
        _viewport.InvalidateVisual();
        TxtStatus.Text = $"Viewport value label size: {_settings.MeasurementLabelScale:0.##}x.";
    }

    private void BtnMeasurementStrokeApply_Click(object sender, RoutedEventArgs e) =>
        ApplyMeasurementStrokeScaleFromText();

    private void TxtMeasurementStrokeScale_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;

        ApplyMeasurementStrokeScaleFromText();
        e.Handled = true;
    }

    private void TxtMeasurementStrokeScale_LostFocus(object sender, RoutedEventArgs e) =>
        ApplyMeasurementStrokeScaleFromText();

    private void ApplyMeasurementStrokeScaleFromText()
    {
        string raw = TxtMeasurementStrokeScale.Text.Trim().Replace(",", ".", StringComparison.Ordinal);
        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double scale) ||
            scale < 0.25 ||
            scale > 4.00)
        {
            TxtMeasurementStrokeScale.Text = _settings.ViewportMeasurementStrokeScale.ToString("0.##", CultureInfo.InvariantCulture);
            TxtStatus.Text = "Viewport line thickness must be 0.25 - 4.0.";
            return;
        }

        SetMeasurementStrokeScale(scale);
    }

    private void SetMeasurementStrokeScale(double scale)
    {
        _settings.ViewportMeasurementStrokeScale = NormalizeStrokeScale(scale);
        ApplyDisplaySettingsToViewport();
        SaveAppSettings();
        _viewport.InvalidateVisual();
        TxtStatus.Text = $"Viewport line thickness: {_settings.ViewportMeasurementStrokeScale:0.##}x.";
    }

    private void BtnMeasurementPointApply_Click(object sender, RoutedEventArgs e) =>
        ApplyMeasurementPointScaleFromText();

    private void TxtMeasurementPointScale_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;

        ApplyMeasurementPointScaleFromText();
        e.Handled = true;
    }

    private void TxtMeasurementPointScale_LostFocus(object sender, RoutedEventArgs e) =>
        ApplyMeasurementPointScaleFromText();

    private void ApplyMeasurementPointScaleFromText()
    {
        string raw = TxtMeasurementPointScale.Text.Trim().Replace(",", ".", StringComparison.Ordinal);
        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double scale) ||
            scale < 0.25 ||
            scale > 4.00)
        {
            TxtMeasurementPointScale.Text = _settings.ViewportPointSizeScale.ToString("0.##", CultureInfo.InvariantCulture);
            TxtStatus.Text = "Viewport point size must be 0.25 - 4.0.";
            return;
        }

        SetMeasurementPointScale(scale);
    }

    private void SetMeasurementPointScale(double scale)
    {
        _settings.ViewportPointSizeScale = NormalizePointScale(scale);
        ApplyDisplaySettingsToViewport();
        SaveAppSettings();
        _viewport.InvalidateVisual();
        TxtStatus.Text = $"Viewport point size: {_settings.ViewportPointSizeScale:0.##}x.";
    }

    private void BtnAreaEdgeApply_Click(object sender, RoutedEventArgs e) =>
        ApplyAreaEdgeScaleFromText();

    private void TxtAreaEdgeScale_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;

        ApplyAreaEdgeScaleFromText();
        e.Handled = true;
    }

    private void TxtAreaEdgeScale_LostFocus(object sender, RoutedEventArgs e) =>
        ApplyAreaEdgeScaleFromText();

    private void ApplyAreaEdgeScaleFromText()
    {
        string raw = TxtAreaEdgeScale.Text.Trim().Replace(",", ".", StringComparison.Ordinal);
        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double scale) ||
            scale < 0.25 ||
            scale > 4.00)
        {
            TxtAreaEdgeScale.Text = _settings.ViewportAreaEdgeScale.ToString("0.##", CultureInfo.InvariantCulture);
            TxtStatus.Text = "Area edge thickness must be 0.25 - 4.0.";
            return;
        }

        _settings.ViewportAreaEdgeScale = Math.Clamp(scale, 0.25, 4.0);
        ApplyDisplaySettingsToViewport();
        SaveAppSettings();
        _viewport.InvalidateVisual();
        TxtStatus.Text = $"Area edge thickness: {_settings.ViewportAreaEdgeScale:0.##}x.";
    }

    private void BtnAreaFillOpacityApply_Click(object sender, RoutedEventArgs e) =>
        ApplyAreaFillOpacityFromText();

    private void TxtAreaFillOpacity_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;

        ApplyAreaFillOpacityFromText();
        e.Handled = true;
    }

    private void TxtAreaFillOpacity_LostFocus(object sender, RoutedEventArgs e) =>
        ApplyAreaFillOpacityFromText();

    private void ApplyAreaFillOpacityFromText()
    {
        string raw = TxtAreaFillOpacity.Text.Trim().TrimEnd('%').Replace(",", ".", StringComparison.Ordinal);
        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double percent) ||
            percent < 0.0 ||
            percent > 100.0)
        {
            TxtAreaFillOpacity.Text = Math.Round(_settings.ViewportAreaFillOpacity * 100.0).ToString("0", CultureInfo.InvariantCulture);
            TxtStatus.Text = "Area fill opacity must be 0 - 100 (%).";
            return;
        }

        _settings.ViewportAreaFillOpacity = Math.Clamp(percent / 100.0, 0.0, 1.0);
        ApplyDisplaySettingsToViewport();
        SaveAppSettings();
        _viewport.InvalidateVisual();
        TxtStatus.Text = $"Area fill opacity: {Math.Round(_settings.ViewportAreaFillOpacity * 100.0):0}%.";
    }

    private void SldLineThickness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || _isApplyingSettings)
            return;

        _settings.ViewportMeasurementStrokeScale = NormalizeStrokeScale(e.NewValue);
        _viewport.MeasurementStrokeScale = _settings.ViewportMeasurementStrokeScale;
        TxtMeasurementStrokeScale.Text = _settings.ViewportMeasurementStrokeScale.ToString("0.##", CultureInfo.InvariantCulture);
        _viewport.InvalidateVisual();
        _viewportScaleDirty = true;
        TxtStatus.Text = $"Viewport line thickness: {_settings.ViewportMeasurementStrokeScale:0.##}x.";
    }

    private void SldPointSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || _isApplyingSettings)
            return;

        _settings.ViewportPointSizeScale = NormalizePointScale(e.NewValue);
        _viewport.PointSizeScale = _settings.ViewportPointSizeScale;
        TxtMeasurementPointScale.Text = _settings.ViewportPointSizeScale.ToString("0.##", CultureInfo.InvariantCulture);
        _viewport.InvalidateVisual();
        _viewportScaleDirty = true;
        TxtStatus.Text = $"Viewport point size: {_settings.ViewportPointSizeScale:0.##}x.";
    }

    private void SldAreaEdge_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || _isApplyingSettings)
            return;

        _settings.ViewportAreaEdgeScale = Math.Clamp(e.NewValue, 0.25, 4.0);
        _viewport.AreaEdgeScale = _settings.ViewportAreaEdgeScale;
        TxtAreaEdgeScale.Text = _settings.ViewportAreaEdgeScale.ToString("0.##", CultureInfo.InvariantCulture);
        _viewport.InvalidateVisual();
        _viewportScaleDirty = true;
        TxtStatus.Text = $"Area edge thickness: {_settings.ViewportAreaEdgeScale:0.##}x.";
    }

    private void SldAreaFill_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || _isApplyingSettings)
            return;

        _settings.ViewportAreaFillOpacity = Math.Clamp(e.NewValue / 100.0, 0.0, 1.0);
        _viewport.AreaFillOpacity = _settings.ViewportAreaFillOpacity;
        TxtAreaFillOpacity.Text = Math.Round(_settings.ViewportAreaFillOpacity * 100.0).ToString("0", CultureInfo.InvariantCulture);
        _viewport.InvalidateVisual();
        _viewportScaleDirty = true;
        TxtStatus.Text = $"Area fill opacity: {Math.Round(_settings.ViewportAreaFillOpacity * 100.0):0}%.";
    }

    private void SldLabelScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || _isApplyingSettings)
            return;

        _settings.MeasurementLabelScale = NormalizeOverlayScale(e.NewValue);
        _viewport.MeasurementLabelScale = _settings.MeasurementLabelScale;
        TxtMeasurementLabelScale.Text = _settings.MeasurementLabelScale.ToString("0.##", CultureInfo.InvariantCulture);
        _viewport.InvalidateVisual();
        _viewportScaleDirty = true;
        TxtStatus.Text = $"Viewport value label size: {_settings.MeasurementLabelScale:0.##}x.";
    }

    private void Slider_CommitSave(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || !_viewportScaleDirty)
            return;

        _viewportScaleDirty = false;
        AppSettingsStore.NormalizeOutputSettings(_settings);
        ApplyDisplaySettingsToViewport();
        SaveAppSettings();
    }

    private void BtnStrokeSizePresets_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not UIElement target)
            return;

        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
        foreach (var (label, scale) in StrokeSizeOptions())
        {
            var item = new MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = Math.Abs(NormalizeStrokeScale(_settings.ViewportMeasurementStrokeScale) - scale) < 0.001,
            };
            item.Click += (_, _) => SetMeasurementStrokeScale(scale);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Custom...", true, PromptMeasurementStrokeScale));
        menu.IsOpen = true;
    }

    private void BtnPointSizePresets_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not UIElement target)
            return;

        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
        foreach (var (label, scale) in PointSizeOptions())
        {
            var item = new MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = Math.Abs(NormalizePointScale(_settings.ViewportPointSizeScale) - scale) < 0.001,
            };
            item.Click += (_, _) => SetMeasurementPointScale(scale);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Custom...", true, PromptMeasurementPointScale));
        menu.IsOpen = true;
    }

    private void PromptMeasurementStrokeScale()
    {
        string? raw = ShowInputDialog(
            "Scale multiplier (0.25 - 4.0):",
            NormalizeStrokeScale(_settings.ViewportMeasurementStrokeScale).ToString("0.##", CultureInfo.InvariantCulture),
            "Line Thickness");
        if (raw == null)
            return;

        if (!double.TryParse(raw.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double scale) ||
            scale < 0.25 ||
            scale > 4.0)
        {
            MessageBox.Show("Enter a value from 0.25 to 4.0.", "Line Thickness", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetMeasurementStrokeScale(scale);
    }

    private void PromptMeasurementPointScale()
    {
        string? raw = ShowInputDialog(
            "Scale multiplier (0.25 - 4.0):",
            NormalizePointScale(_settings.ViewportPointSizeScale).ToString("0.##", CultureInfo.InvariantCulture),
            "Point Size");
        if (raw == null)
            return;

        if (!double.TryParse(raw.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double scale) ||
            scale < 0.25 ||
            scale > 4.0)
        {
            MessageBox.Show("Enter a value from 0.25 to 4.0.", "Point Size", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetMeasurementPointScale(scale);
    }

    private static double NormalizeStrokeScale(double scale)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            return 1.0;

        return Math.Clamp(scale, 0.25, 4.00);
    }

    private static double NormalizePointScale(double scale)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            return 1.0;

        return Math.Clamp(scale, 0.25, 4.00);
    }

    private static IReadOnlyList<(string Label, double Scale)> StrokeSizeOptions() =>
    [
        ("Thin", 0.75),
        ("Normal", 1.00),
        ("Thick", 1.50),
        ("Heavy", 2.00),
        ("XL", 3.00),
    ];

    private static IReadOnlyList<(string Label, double Scale)> PointSizeOptions() =>
    [
        ("Small", 0.65),
        ("Normal", 1.00),
        ("Large", 1.40),
        ("XL", 2.00),
        ("XXL", 3.00),
    ];
}
