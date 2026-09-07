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

        IReadOnlyList<string> selectedRoots = SelectedExcelAllExportRoots();
        AppLog.Info(
            $"Excel ALL UI scope candidates. Count={selectedRoots.Count}; " +
            $"Roots={string.Join(" | ", selectedRoots)}.");
        ExcelMacroBatchScopeResult scope = ExcelMacroBatchPlanner.ResolveScope(
            _currentJob,
            _takeoffItems,
            selectedRoots,
            _excelMacroExportConfig);
        if (!scope.Success)
        {
            AppLog.Warn($"Excel ALL scope rejected: {scope.Message}");
            TxtStatus.Text = scope.Message;
            MessageBox.Show(
                scope.Message,
                "Excel ALL",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        AppLog.Info($"Excel ALL scope accepted. Root={scope.RootPath}; {scope.Message}");

        var planned = new List<(
            ExcelMacroExportActionConfig Action,
            ExcelMacroPayloadResult Payload)>();
        var skipped = new List<string>();
        ExcelFramingExportPlan framingPlan =
            ExcelFramingExportPlan.Failure("Framing export is disabled in Settings.");
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
            if (_excelMacroExportConfig.Framing.IncludeInAll)
            {
                framingPlan = ExcelFramingExportPlanner.Build(
                    _currentJob,
                    _takeoffItems,
                    scope.RootPath,
                    _viewport.ScaleMetersPerPt,
                    _excelMacroExportConfig.Framing);
                if (!framingPlan.Success)
                    skipped.Add($"Framing: {framingPlan.Message}");
            }
        }

        if (planned.Count == 0 && !framingPlan.Success)
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
        warnings.AddRange(
            framingPlan.Warnings.Select(warning => $"Framing: {warning}"));
        if (warnings.Count > 0 && !ConfirmExcelBatchWarnings(warnings))
        {
            TxtStatus.Text = "Excel ALL cancelled before writing to Excel.";
            return;
        }

        var completed = new List<string>();
        string? failureMessage = null;
        string failedLabel = "";
        int totalSteps = planned.Count + (framingPlan.Success ? 1 : 0);
        int completedSteps = 0;
        for (int index = 0; index < planned.Count; index++)
        {
            (ExcelMacroExportActionConfig action, ExcelMacroPayloadResult payload) =
                planned[index];
            using (ShowBusyOverlay(
                       $"Excel ALL {completedSteps + 1}/{totalSteps}: {action.Label}..."))
            {
                await WaitForBusyOverlayRenderAsync();
                ExcelMacroTakeoffExportResult result =
                    ExcelMacroTakeoffExportService.ExportAndRun(payload.Rows, action);
                if (!result.Success)
                {
                    failureMessage = result.Message;
                    failedLabel = action.Label;
                    break;
                }
                completed.Add(action.Label);
                completedSteps++;
            }
        }

        if (failureMessage == null && framingPlan.Success)
        {
            using (ShowBusyOverlay(
                       $"Excel ALL {completedSteps + 1}/{totalSteps}: Framing..."))
            {
                await WaitForBusyOverlayRenderAsync();
                ExcelFramingExportResult framingResult =
                    ExcelFramingExportService.Export(
                        framingPlan,
                        _excelMacroExportConfig.Framing,
                        CurrentExcelFramingLegendText());
                if (framingResult.Success)
                {
                    completed.Add("Framing");
                }
                else
                {
                    failedLabel = "Framing";
                    failureMessage = framingResult.Message;
                }
            }
        }

        string summary = BuildExcelBatchSummary(
            scope,
            completed,
            skipped,
            failedLabel,
            failureMessage);
        TxtStatus.Text = summary.Replace(Environment.NewLine, " ");
        MessageBox.Show(
            summary,
            "Excel ALL",
            MessageBoxButton.OK,
            failureMessage == null ? MessageBoxImage.Information : MessageBoxImage.Warning);
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
