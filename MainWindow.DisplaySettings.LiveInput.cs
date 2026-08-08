using System.Windows;

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

    private void ApplyLiveInputSettings()
    {
        _viewport.LiveInputLabelSizePx = _settings.ViewportLiveInputLabelSizePx;
        _viewport.LiveInputLabelOpacity = _settings.ViewportLiveInputLabelOpacity;
        TxtLiveInputLabelSizeValue.Text = $"{_settings.ViewportLiveInputLabelSizePx:0.#} px";
        TxtLiveInputLabelOpacityValue.Text = $"{_settings.ViewportLiveInputLabelOpacity * 100.0:0}%";
        _viewport.InvalidateVisual();
        ApplyLiveDisplayScalesToDetachedSheets();
        _viewportScaleDirty = true;
    }
}
