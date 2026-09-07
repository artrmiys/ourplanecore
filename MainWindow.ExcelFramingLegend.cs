using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OurPlanCore;

public partial class MainWindow
{
    private void BtnExcelFramingLegend_Click(object sender, RoutedEventArgs e) =>
        ToggleExcelFramingLegendPanel();

    private void ToggleExcelFramingLegendPanel()
    {
        if (!RequireModule(ModuleId.ExcelIntegration, "Excel framing legend"))
            return;

        if (ExcelFramingLegendHost.Visibility == Visibility.Visible)
        {
            ExcelFramingLegendHost.Visibility = Visibility.Collapsed;
            return;
        }

        QuickCalcHost.Visibility = Visibility.Collapsed;
        LoadExcelFramingLegendForCurrentJob();
        ExcelFramingLegendHost.Visibility = Visibility.Visible;
        var slide = new TranslateTransform(ExcelFramingLegend.Width, 0);
        ExcelFramingLegendHost.RenderTransform = slide;
        slide.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(
                ExcelFramingLegend.Width,
                0,
                TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new QuadraticEase(),
            });
    }

    private void ExcelFramingLegend_CloseRequested(object sender, EventArgs e) =>
        ExcelFramingLegendHost.Visibility = Visibility.Collapsed;

    private void ExcelFramingLegend_SaveRequested(object sender, EventArgs e)
    {
        if (_currentJob == null)
        {
            ExcelFramingLegend.SetStatus("Open a job before saving its legend.");
            return;
        }
        try
        {
            ExcelFramingLegendStore.Save(
                _currentJob,
                ExcelFramingLegend.LegendText);
            ExcelFramingLegend.SetStatus("Saved for this job.");
        }
        catch (Exception ex)
        {
            ExcelFramingLegend.SetStatus($"Could not save: {ex.Message}");
        }
    }

    private void LoadExcelFramingLegendForCurrentJob()
    {
        if (!IsInitialized)
            return;
        ExcelFramingLegend.LegendText =
            ExcelFramingLegendStore.Load(_currentJob);
        ExcelFramingLegend.SetStatus(
            _currentJob == null
                ? "Open a job to save and use a legend."
                : "Legend loaded for the current job.");
    }

    private string CurrentExcelFramingLegendText() =>
        ExcelFramingLegend.LegendText;
}
