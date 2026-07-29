using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace OurPlanCore;

public partial class MainWindow
{
    private ExcelMacroExportConfig _excelMacroExportConfig =
        ExcelMacroExportConfig.BuildDefault();

    private async void BtnExcelMacroSqft_Click(object sender, RoutedEventArgs e) =>
        await RunExcelMacroTakeoffActionAsync(ExcelMacroExportActionIds.Sqft);

    private async void BtnExcelMacroWalls_Click(object sender, RoutedEventArgs e) =>
        await RunExcelMacroTakeoffActionAsync(ExcelMacroExportActionIds.Walls);

    private async void BtnExcelMacroOpenings_Click(object sender, RoutedEventArgs e) =>
        await RunExcelMacroTakeoffActionAsync(ExcelMacroExportActionIds.Openings);

    private async Task RunExcelMacroTakeoffActionAsync(string actionId)
    {
        ExcelMacroExportActionConfig action = _excelMacroExportConfig.Action(actionId);
        if (!RequireModule(ModuleId.ExcelIntegration, $"Excel {action.Label}"))
            return;

        if (_currentJob == null)
        {
            TxtStatus.Text = "Open or create a job before exporting to Excel.";
            return;
        }

        IReadOnlyList<string> roots = SelectedCurrentExcelExportRoots();
        if (roots.Count == 0)
        {
            string message =
                $"Select one Takeoffs folder or item for {action.Label}, then press the button again.";
            TxtStatus.Text = message;
            MessageBox.Show(
                message,
                $"Excel {action.Label}",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ExcelMacroPayloadResult payload;
        using (ShowBusyOverlay($"Preparing Excel {action.Label}..."))
        {
            await WaitForBusyOverlayRenderAsync();
            SaveCurrentPageScale();
            payload = ExcelMacroPayloadBuilder.Build(
                _currentJob,
                _takeoffItems,
                roots,
                _viewport.ScaleMetersPerPt,
                _excelMacroExportConfig,
                actionId);
        }

        if (!payload.Success)
        {
            TxtStatus.Text = payload.Message;
            MessageBox.Show(
                payload.Message,
                $"Excel {action.Label}",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (payload.Warnings.Count > 0)
        {
            string warningText = string.Join(
                Environment.NewLine,
                payload.Warnings.Take(8).Select(warning => $"• {warning}"));
            if (payload.Warnings.Count > 8)
                warningText += $"{Environment.NewLine}• ...and {payload.Warnings.Count - 8} more.";

            MessageBoxResult answer = MessageBox.Show(
                $"{warningText}{Environment.NewLine}{Environment.NewLine}" +
                $"Continue with the remaining {action.Label} rows?",
                $"Excel {action.Label} — skipped rows",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                TxtStatus.Text = $"{action.Label} export cancelled before writing to Excel.";
                return;
            }
        }

        ExcelMacroTakeoffExportResult result;
        using (ShowBusyOverlay($"Excel {action.Label}: writing rows and running macro..."))
        {
            await WaitForBusyOverlayRenderAsync();
            result = ExcelMacroTakeoffExportService.ExportAndRun(payload.Rows, action);
        }

        TxtStatus.Text = result.Message;
        if (!result.Success)
        {
            MessageBox.Show(
                result.Message,
                $"Excel {action.Label}",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
