using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace OurPlanCore;

public partial class MainWindow
{
    private const double DefaultExcelMacroStripTopFraction = 0.5;

    private void ApplyExcelMacroStripFraction()
    {
        double fraction = NormalizeExcelMacroStripFraction(
            _settings.ExcelMacroStripTopFraction);
        RightCommandStripRow.Height = new GridLength(fraction, GridUnitType.Star);
        ExcelMacroStripRow.Height =
            new GridLength(1.0 - fraction, GridUnitType.Star);
    }

    private void ExcelMacroStripSplitter_DragCompleted(
        object sender,
        DragCompletedEventArgs e)
    {
        double available =
            RightCommandStripRow.ActualHeight + ExcelMacroStripRow.ActualHeight;
        if (!double.IsFinite(available) || available <= 0)
            return;

        double fraction = NormalizeExcelMacroStripFraction(
            RightCommandStripRow.ActualHeight / available);
        ApplyExcelMacroStripFraction(fraction);
        if (Math.Abs(_settings.ExcelMacroStripTopFraction - fraction) < 0.005)
            return;

        _settings.ExcelMacroStripTopFraction = fraction;
        SaveAppSettings();
    }

    private void ApplyExcelMacroStripModuleVisibility(bool enabled)
    {
        ExcelMacroStripSplitter.Visibility =
            enabled ? Visibility.Visible : Visibility.Collapsed;
        ExcelMacroStripRow.MinHeight = enabled ? 140 : 0;
        if (enabled)
        {
            ApplyExcelMacroStripFraction();
            return;
        }

        RightCommandStripRow.Height = new GridLength(1, GridUnitType.Star);
        ExcelMacroStripRow.Height = new GridLength(0);
    }

    private void ApplyExcelMacroStripFraction(double fraction)
    {
        fraction = NormalizeExcelMacroStripFraction(fraction);
        RightCommandStripRow.Height = new GridLength(fraction, GridUnitType.Star);
        ExcelMacroStripRow.Height =
            new GridLength(1.0 - fraction, GridUnitType.Star);
    }

    private static double NormalizeExcelMacroStripFraction(double fraction)
    {
        if (!double.IsFinite(fraction))
            return DefaultExcelMacroStripTopFraction;
        return Math.Clamp(fraction, 0.2, 0.8);
    }
}
