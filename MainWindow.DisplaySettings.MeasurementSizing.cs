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

    private void TxtRulerStrokeWidth_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;

        ApplyRulerStrokeWidthFromText();
        e.Handled = true;
    }

    private void TxtRulerStrokeWidth_LostFocus(object sender, RoutedEventArgs e) =>
        ApplyRulerStrokeWidthFromText();

    private void ApplyRulerStrokeWidthFromText()
    {
        string raw = TxtRulerStrokeWidth.Text.Trim().Replace(",", ".", StringComparison.Ordinal);
        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double width) ||
            width < 0.5 ||
            width > 6.0)
        {
            TxtRulerStrokeWidth.Text = _settings.ViewportRulerStrokeWidth.ToString("0.##", CultureInfo.InvariantCulture);
            TxtStatus.Text = "Ruler thickness must be 0.5 - 6 px.";
            return;
        }

        SetRulerStrokeWidth(width);
    }

    private void SetRulerStrokeWidth(double width)
    {
        _settings.ViewportRulerStrokeWidth = NormalizeRulerStrokeWidth(width);
        ApplyDisplaySettingsToViewport();
        SaveAppSettings();
        _viewport.InvalidateVisual();
        TxtStatus.Text = $"Ruler thickness: {_settings.ViewportRulerStrokeWidth:0.##}px.";
    }

    private void TxtPdfSnapBridgeTolerance_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;

        ApplyPdfSnapBridgeToleranceFromText();
        e.Handled = true;
    }

    private void TxtPdfSnapBridgeTolerance_LostFocus(object sender, RoutedEventArgs e) =>
        ApplyPdfSnapBridgeToleranceFromText();

    private void ApplyPdfSnapBridgeToleranceFromText()
    {
        string raw = TxtPdfSnapBridgeTolerance.Text.Trim().Replace(",", ".", StringComparison.Ordinal);
        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double pixels) ||
            pixels < AppSettingsStore.ViewportPdfSnapBridgeToleranceMinPx ||
            pixels > AppSettingsStore.ViewportPdfSnapBridgeToleranceMaxPx)
        {
            TxtPdfSnapBridgeTolerance.Text = _settings.ViewportPdfSnapBridgeTolerancePx.ToString("0.#", CultureInfo.InvariantCulture);
            TxtStatus.Text =
                $"PDF Snap bridge must be {AppSettingsStore.ViewportPdfSnapBridgeToleranceMinPx:0} - {AppSettingsStore.ViewportPdfSnapBridgeToleranceMaxPx:0} px.";
            return;
        }

        SetPdfSnapBridgeTolerance(pixels);
    }

    private void SetPdfSnapBridgeTolerance(double pixels)
    {
        _settings.ViewportPdfSnapBridgeTolerancePx = NormalizePdfSnapBridgeTolerance(pixels);
        ApplyDisplaySettingsToViewport();
        SaveAppSettings();
        _viewport.InvalidateVisual();
        TxtStatus.Text = $"PDF Snap bridge radius: {_settings.ViewportPdfSnapBridgeTolerancePx:0.#}px.";
    }

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

    private void SldRulerThickness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || _isApplyingSettings)
            return;

        _settings.ViewportRulerStrokeWidth = NormalizeRulerStrokeWidth(e.NewValue);
        _viewport.RulerStrokeWidth = _settings.ViewportRulerStrokeWidth;
        TxtRulerStrokeWidth.Text = _settings.ViewportRulerStrokeWidth.ToString("0.##", CultureInfo.InvariantCulture);
        _viewport.InvalidateVisual();
        _viewportScaleDirty = true;
        TxtStatus.Text = $"Ruler thickness: {_settings.ViewportRulerStrokeWidth:0.##}px.";
    }

    private void SldPdfSnapBridgeTolerance_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || _isApplyingSettings)
            return;

        _settings.ViewportPdfSnapBridgeTolerancePx = NormalizePdfSnapBridgeTolerance(e.NewValue);
        _viewport.PdfSnapBridgeToleranceScreenPx = _settings.ViewportPdfSnapBridgeTolerancePx;
        TxtPdfSnapBridgeTolerance.Text = _settings.ViewportPdfSnapBridgeTolerancePx.ToString("0.#", CultureInfo.InvariantCulture);
        _viewport.InvalidateVisual();
        _viewportScaleDirty = true;
        TxtStatus.Text = $"PDF Snap bridge radius: {_settings.ViewportPdfSnapBridgeTolerancePx:0.#}px.";
    }

    private void TxtZoomWheelFactor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;

        ApplyZoomWheelFactorFromText();
        e.Handled = true;
    }

    private void TxtZoomWheelFactor_LostFocus(object sender, RoutedEventArgs e) =>
        ApplyZoomWheelFactorFromText();

    private void ApplyZoomWheelFactorFromText()
    {
        string raw = TxtZoomWheelFactor.Text.Trim().TrimEnd('x', 'X', '×').Replace(",", ".", StringComparison.Ordinal);
        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double factor) ||
            factor < AppSettingsStore.ViewportZoomWheelFactorMin ||
            factor > AppSettingsStore.ViewportZoomWheelFactorMax)
        {
            TxtZoomWheelFactor.Text = _settings.ViewportZoomWheelFactor.ToString("0.##", CultureInfo.InvariantCulture);
            TxtStatus.Text =
                $"Wheel zoom step must be {AppSettingsStore.ViewportZoomWheelFactorMin:0.##} - {AppSettingsStore.ViewportZoomWheelFactorMax:0.##}x.";
            return;
        }

        SetZoomWheelFactor(factor);
    }

    private void SetZoomWheelFactor(double factor)
    {
        _settings.ViewportZoomWheelFactor = NormalizeZoomWheelFactor(factor);
        ApplyDisplaySettingsToViewport();
        SaveAppSettings();
        TxtStatus.Text = $"Mouse-wheel zoom step: {_settings.ViewportZoomWheelFactor:0.##}x per notch.";
    }

    private void SldZoomWheelFactor_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || _isApplyingSettings)
            return;

        _settings.ViewportZoomWheelFactor = NormalizeZoomWheelFactor(e.NewValue);
        _viewport.ZoomWheelFactor = _settings.ViewportZoomWheelFactor;
        TxtZoomWheelFactor.Text = _settings.ViewportZoomWheelFactor.ToString("0.##", CultureInfo.InvariantCulture);
        _viewportScaleDirty = true;
        TxtStatus.Text = $"Mouse-wheel zoom step: {_settings.ViewportZoomWheelFactor:0.##}x per notch.";
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
            PostStatusWarning("Enter a line thickness value from 0.25 to 4.0.");
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
            PostStatusWarning("Enter a point size value from 0.25 to 4.0.");
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

    private static double NormalizeRulerStrokeWidth(double width)
    {
        if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
            return 1.0;

        return Math.Clamp(width, 0.5, 6.0);
    }

    private static double NormalizePdfSnapBridgeTolerance(double pixels) =>
        AppSettingsStore.NormalizePdfSnapBridgeTolerancePx(pixels);

    private static double NormalizePointScale(double scale)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            return 1.0;

        return Math.Clamp(scale, 0.25, 4.00);
    }

    private static double NormalizeZoomWheelFactor(double factor)
    {
        if (double.IsNaN(factor) || double.IsInfinity(factor) || factor <= 1.0)
            return AppSettingsStore.ViewportZoomWheelFactorDefault;

        return Math.Clamp(
            factor,
            AppSettingsStore.ViewportZoomWheelFactorMin,
            AppSettingsStore.ViewportZoomWheelFactorMax);
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
