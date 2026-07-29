using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace OurPlanCore;

public partial class MainWindow
{
    private async void BtnExcelMacroAll_Click(object sender, RoutedEventArgs e) =>
        await RunExcelMacroBatchAsync();

    private async Task RunExcelMacroBatchAsync()
    {
        if (!RequireModule(ModuleId.ExcelIntegration, "Excel ALL"))
            return;
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open or create a job before running Excel ALL.";
            return;
        }

        IReadOnlyList<string> selectedRoots = SelectedCurrentExcelExportRoots();
        ExcelMacroBatchScopeResult scope = ExcelMacroBatchPlanner.ResolveScope(
            _currentJob,
            _takeoffItems,
            selectedRoots,
            _excelMacroExportConfig);
        if (!scope.Success)
        {
            TxtStatus.Text = scope.Message;
            MessageBox.Show(
                scope.Message,
                "Excel ALL",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var planned = new List<(
            ExcelMacroExportActionConfig Action,
            ExcelMacroPayloadResult Payload)>();
        var skipped = new List<string>();
        using (ShowBusyOverlay("Preparing Excel ALL..."))
        {
            await WaitForBusyOverlayRenderAsync();
            SaveCurrentPageScale();
            foreach (string actionId in ExcelMacroBatchPlanner.ActionIds(_excelMacroExportConfig))
            {
                ExcelMacroExportActionConfig action =
                    _excelMacroExportConfig.Action(actionId);
                ExcelMacroPayloadResult payload = ExcelMacroPayloadBuilder.Build(
                    _currentJob,
                    _takeoffItems,
                    [scope.RootPath],
                    _viewport.ScaleMetersPerPt,
                    _excelMacroExportConfig,
                    actionId);
                if (payload.Success)
                    planned.Add((action, payload));
                else
                    skipped.Add($"{action.Label}: {payload.Message}");
            }
        }

        if (planned.Count == 0)
        {
            string message =
                $"{scope.Message}{Environment.NewLine}" +
                "No configured export folders with measured rows were found.";
            TxtStatus.Text = message;
            MessageBox.Show(
                message,
                "Excel ALL",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        List<string> warnings = planned
            .SelectMany(entry => entry.Payload.Warnings.Select(
                warning => $"{entry.Action.Label}: {warning}"))
            .ToList();
        if (warnings.Count > 0 && !ConfirmExcelBatchWarnings(warnings))
        {
            TxtStatus.Text = "Excel ALL cancelled before writing to Excel.";
            return;
        }

        var completed = new List<string>();
        ExcelMacroTakeoffExportResult? failure = null;
        string failedLabel = "";
        for (int index = 0; index < planned.Count; index++)
        {
            (ExcelMacroExportActionConfig action, ExcelMacroPayloadResult payload) =
                planned[index];
            using (ShowBusyOverlay(
                       $"Excel ALL {index + 1}/{planned.Count}: {action.Label}..."))
            {
                await WaitForBusyOverlayRenderAsync();
                ExcelMacroTakeoffExportResult result =
                    ExcelMacroTakeoffExportService.ExportAndRun(payload.Rows, action);
                if (!result.Success)
                {
                    failure = result;
                    failedLabel = action.Label;
                    break;
                }
                completed.Add(action.Label);
            }
        }

        string summary = BuildExcelBatchSummary(
            scope,
            completed,
            skipped,
            failedLabel,
            failure?.Message);
        TxtStatus.Text = summary.Replace(Environment.NewLine, " ");
        MessageBox.Show(
            summary,
            "Excel ALL",
            MessageBoxButton.OK,
            failure == null ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private bool ConfirmExcelBatchWarnings(IReadOnlyList<string> warnings)
    {
        string warningText = string.Join(
            Environment.NewLine,
            warnings.Take(10).Select(warning => $"- {warning}"));
        if (warnings.Count > 10)
            warningText += $"{Environment.NewLine}- ...and {warnings.Count - 10} more.";
        return MessageBox.Show(
            $"{warningText}{Environment.NewLine}{Environment.NewLine}" +
            "Continue Excel ALL with the remaining rows?",
            "Excel ALL - skipped rows",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private static string BuildExcelBatchSummary(
        ExcelMacroBatchScopeResult scope,
        IReadOnlyList<string> completed,
        IReadOnlyList<string> skipped,
        string failedLabel,
        string? failureMessage)
    {
        var lines = new List<string> { scope.Message };
        if (completed.Count > 0)
            lines.Add($"Completed: {string.Join(" -> ", completed)}.");
        if (skipped.Count > 0)
        {
            lines.Add("Skipped:");
            lines.AddRange(skipped.Take(7).Select(item => $"- {item}"));
            if (skipped.Count > 7)
                lines.Add($"- ...and {skipped.Count - 7} more.");
        }
        if (!string.IsNullOrWhiteSpace(failureMessage))
        {
            lines.Add($"Stopped at {failedLabel}: {failureMessage}");
            lines.Add("Later actions were not run.");
        }
        return string.Join(Environment.NewLine, lines);
    }
}
