using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace OurPlanCore;

public partial class MainWindow
{
    private async void BtnExcelFramingAll_Click(object sender, RoutedEventArgs e) =>
        await RunExcelFramingActionAsync(null);

    private async void BtnExcelFramingPosts_Click(object sender, RoutedEventArgs e) =>
        await RunExcelFramingActionAsync(ExcelFramingCategoryIds.Posts);

    private async void BtnExcelFramingBeams_Click(object sender, RoutedEventArgs e) =>
        await RunExcelFramingActionAsync(ExcelFramingCategoryIds.Beams);

    private async void BtnExcelFramingHeaders_Click(object sender, RoutedEventArgs e) =>
        await RunExcelFramingActionAsync(ExcelFramingCategoryIds.Headers);

    private async void BtnExcelFramingJoists_Click(object sender, RoutedEventArgs e) =>
        await RunExcelFramingActionAsync(ExcelFramingCategoryIds.Joists);

    private async void BtnExcelFramingDetails_Click(object sender, RoutedEventArgs e) =>
        await RunExcelFramingActionAsync(ExcelFramingCategoryIds.Details);

    private async void BtnExcelFramingStairs_Click(object sender, RoutedEventArgs e) =>
        await RunExcelFramingActionAsync(ExcelFramingCategoryIds.Stairs);

    private async Task RunExcelFramingActionAsync(string? categoryId)
    {
        string label = FramingActionLabel(categoryId);
        if (!RequireModule(ModuleId.ExcelIntegration, $"Excel {label}"))
            return;
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open or create a job before exporting framing to Excel.";
            return;
        }

        IReadOnlyList<string> selectedRoots = SelectedExcelAllExportRoots();
        AppLog.Info(
            $"Excel {label} UI scope candidates. Count={selectedRoots.Count}; " +
            $"Roots={string.Join(" | ", selectedRoots)}.");
        ExcelMacroBatchScopeResult scope = ExcelMacroBatchPlanner.ResolveScope(
            _currentJob,
            _takeoffItems,
            selectedRoots,
            _excelMacroExportConfig);
        if (!scope.Success)
        {
            ShowExcelFramingMessage(label, scope.Message, MessageBoxImage.Information);
            return;
        }

        ExcelFramingExportPlan plan;
        using (ShowBusyOverlay($"Preparing Excel {label}..."))
        {
            await WaitForBusyOverlayRenderAsync();
            SaveCurrentPageScale();
            plan = ExcelFramingExportPlanner.Build(
                _currentJob,
                _takeoffItems,
                scope.RootPath,
                _viewport.ScaleMetersPerPt,
                _excelMacroExportConfig.Framing,
                requireIncludeInAll: false);
            if (plan.Success && !string.IsNullOrWhiteSpace(categoryId))
                plan = ExcelFramingExportPlanner.ForCategory(plan, categoryId);
        }

        if (!plan.Success)
        {
            ShowExcelFramingMessage(label, plan.Message, MessageBoxImage.Information);
            return;
        }
        if (plan.Warnings.Count > 0 && !ConfirmExcelBatchWarnings(plan.Warnings))
        {
            TxtStatus.Text = $"Excel {label} cancelled before writing to Excel.";
            return;
        }

        ExcelFramingExportResult result;
        using (ShowBusyOverlay($"Excel {label}: writing and processing..."))
        {
            await WaitForBusyOverlayRenderAsync();
            result = string.IsNullOrWhiteSpace(categoryId)
                ? ExcelFramingExportService.Export(
                    plan,
                    _excelMacroExportConfig.Framing,
                    CurrentExcelFramingLegendText())
                : ExcelFramingExportService.ExportCategory(
                    plan,
                    _excelMacroExportConfig.Framing,
                    CurrentExcelFramingLegendText());
        }

        TxtStatus.Text = result.Message;
        if (!result.Success)
            ShowExcelFramingMessage(label, result.Message, MessageBoxImage.Warning);
    }

    private string FramingActionLabel(string? categoryId)
    {
        if (string.IsNullOrWhiteSpace(categoryId))
            return "Framing ALL";
        return _excelMacroExportConfig.Framing.Categories
            .FirstOrDefault(category => string.Equals(
                category.Id,
                categoryId,
                StringComparison.OrdinalIgnoreCase))
            ?.Label ?? categoryId;
    }

    private void ShowExcelFramingMessage(
        string label,
        string message,
        MessageBoxImage image)
    {
        TxtStatus.Text = message;
        MessageBox.Show(
            message,
            $"Excel {label}",
            MessageBoxButton.OK,
            image);
    }
}
