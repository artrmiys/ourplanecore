using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace OurPlanCore;

public partial class MainWindow
{
    private void SldLiveInputLabelSize_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || _isApplyingSettings)
            return;

        _settings.ViewportLiveInputLabelSizePx =
            AppSettingsStore.NormalizeLiveInputLabelSize(e.NewValue);
        ApplyLiveInputSettings();
        TxtStatus.Text = $"Live input label size: {_settings.ViewportLiveInputLabelSizePx:0.#} px.";
    }

    private void SldLiveInputLabelOpacity_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || _isApplyingSettings)
            return;

        _settings.ViewportLiveInputLabelOpacity =
            AppSettingsStore.NormalizeLiveInputLabelOpacity(e.NewValue / 100.0);
        ApplyLiveInputSettings();
        TxtStatus.Text = $"Live input label opacity: {_settings.ViewportLiveInputLabelOpacity * 100.0:0}%.";
    }

    private void TxtLiveInputLabelSizeValue_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;

        ApplyLiveInputLabelSizeFromText();
        e.Handled = true;
    }

    private void TxtLiveInputLabelSizeValue_LostFocus(object sender, RoutedEventArgs e) =>
        ApplyLiveInputLabelSizeFromText();

    private void ApplyLiveInputLabelSizeFromText()
    {
        string raw = ViewportLiveInputSettings.SizeText.Trim().TrimEnd('p', 'x')
            .Replace(",", ".", StringComparison.Ordinal);
        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double size) ||
            size < AppSettingsStore.LiveInputLabelSizeMinPx ||
            size > AppSettingsStore.LiveInputLabelSizeMaxPx)
        {
            ViewportLiveInputSettings.SizeText = _settings.ViewportLiveInputLabelSizePx.ToString("0.#", CultureInfo.InvariantCulture);
            TxtStatus.Text = "Live LFT / pitch label size must be 8 - 24 px.";
            return;
        }

        _settings.ViewportLiveInputLabelSizePx = AppSettingsStore.NormalizeLiveInputLabelSize(size);
        ViewportLiveInputSettings.SizeValue = _settings.ViewportLiveInputLabelSizePx;
        ApplyLiveInputSettings();
        SaveAppSettings();
        _viewportScaleDirty = false;
        TxtStatus.Text = $"Live LFT / pitch label size: {_settings.ViewportLiveInputLabelSizePx:0.#} px.";
    }

    private void TxtLiveInputLabelOpacityValue_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;

        ApplyLiveInputLabelOpacityFromText();
        e.Handled = true;
    }

    private void TxtLiveInputLabelOpacityValue_LostFocus(object sender, RoutedEventArgs e) =>
        ApplyLiveInputLabelOpacityFromText();

    private void ApplyLiveInputLabelOpacityFromText()
    {
        string raw = ViewportLiveInputSettings.OpacityText.Trim().TrimEnd('%')
            .Replace(",", ".", StringComparison.Ordinal);
        double minPercent = AppSettingsStore.LiveInputLabelOpacityMin * 100.0;
        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double percent) ||
            percent < minPercent ||
            percent > 100.0)
        {
            ViewportLiveInputSettings.OpacityText = (_settings.ViewportLiveInputLabelOpacity * 100.0)
                .ToString("0", CultureInfo.InvariantCulture);
            TxtStatus.Text = $"Live LFT / pitch label opacity must be {minPercent:0} - 100%.";
            return;
        }

        _settings.ViewportLiveInputLabelOpacity = AppSettingsStore.NormalizeLiveInputLabelOpacity(percent / 100.0);
        ViewportLiveInputSettings.OpacityValue = _settings.ViewportLiveInputLabelOpacity * 100.0;
        ApplyLiveInputSettings();
        SaveAppSettings();
        _viewportScaleDirty = false;
        TxtStatus.Text = $"Live LFT / pitch label opacity: {_settings.ViewportLiveInputLabelOpacity * 100.0:0}%.";
    }

    private void ApplyLiveInputSettings()
    {
        _viewport.LiveInputLabelSizePx = _settings.ViewportLiveInputLabelSizePx;
        _viewport.LiveInputLabelOpacity = _settings.ViewportLiveInputLabelOpacity;
        ViewportLiveInputSettings.SizeText = _settings.ViewportLiveInputLabelSizePx.ToString("0.#", CultureInfo.InvariantCulture);
        ViewportLiveInputSettings.OpacityText = (_settings.ViewportLiveInputLabelOpacity * 100.0).ToString("0", CultureInfo.InvariantCulture);
        _viewport.InvalidateVisual();
        ApplyLiveDisplayScalesToDetachedSheets();
        _viewportScaleDirty = true;
    }
}
