using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using OurPlanCore.Controls;
using SkiaSharp;
using Path = System.IO.Path;

namespace OurPlanCore;

public partial class MainWindow
{
    // 3D massing draft build workflows from markers, takeoffs, and AI sort.

    private void BuildMassingDraftFromMarkers()
    {
        if (StopLegacy3DMassingWorkflow("Build 3D Draft"))
            return;

        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before building a 3D draft.";
            return;
        }

        try
        {
            SmartMassingDraft draft = SmartMassingDraftService.SaveDraftFromMarkers(_currentJob);
            string path = SmartMassingDraftService.ModelPath(_currentJob);
            RefreshMassingDraftPanel(draft, path);
            if (_rightWorkspaceTabs != null && _massingTab != null)
                _rightWorkspaceTabs.SelectedItem = _massingTab;

            string summary = BuildMassingDraftSummary(draft, path);
            TxtStatus.Text = $"Saved 3D massing draft -> {Path.GetRelativePath(_currentJob.RootPath, path)}";
            MessageBox.Show(summary, "Build 3D Draft", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowOperationError("Build 3D Draft", ex);
        }
    }

    private void BuildMassingDraftFromWallTakeoffs()
    {
        if (StopLegacy3DMassingWorkflow("3D From Takeoffs"))
            return;

        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before building a 3D draft from takeoffs.";
            return;
        }

        double currentLevelSpacing = _settings.MassingLevelSpacingFeet > 0
            ? _settings.MassingLevelSpacingFeet
            : SmartMassingDraftService.DefaultLevelSpacingFeet;
        string? rawLevelSpacing = ShowInputDialog(
            "Default level spacing and roof step, feet (1st=0, 2nd=+spacing, roof=last+spacing):",
            currentLevelSpacing.ToString("G", CultureInfo.InvariantCulture),
            "3D From Takeoffs");
        if (rawLevelSpacing == null)
            return;
        if (!double.TryParse(rawLevelSpacing.Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out double levelSpacingFeet) ||
            levelSpacingFeet <= 0 ||
            levelSpacingFeet > 40)
        {
            PostStatusWarning("Enter a level spacing value between 1 and 40 feet.");
            return;
        }

        try
        {
            _settings.MassingLevelSpacingFeet = levelSpacingFeet;
            SaveAppSettings();

            SmartMassingDraft draft = SmartMassingDraftService.SaveDraftFromWallTakeoffs(_currentJob, levelSpacingFeet);
            string path = SmartMassingDraftService.ModelPath(_currentJob);
            RefreshMassingDraftPanel(draft, path);
            if (_rightWorkspaceTabs != null && _massingTab != null)
                _rightWorkspaceTabs.SelectedItem = _massingTab;

            string summary = BuildMassingDraftSummary(draft, path);
            TxtStatus.Text = $"Saved 3D draft from takeoffs -> {Path.GetRelativePath(_currentJob.RootPath, path)}";
            MessageBox.Show(summary, "3D From Takeoffs", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowOperationError("3D From Takeoffs", ex);
        }
    }

    private async Task BuildMassingDraftFromAiSortedTakeoffsAsync()
    {
        if (StopLegacy3DMassingWorkflow("AI 3D Sort"))
            return;

        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before running AI 3D Sort.";
            return;
        }

        string apiKey = AppSettingsStore.ReadOpenAiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            PostStatusWarning("OPENAI_API_KEY is missing. Save it in AI Settings before running AI 3D Sort.");
            return;
        }

        double currentLevelSpacing = _settings.MassingLevelSpacingFeet > 0
            ? _settings.MassingLevelSpacingFeet
            : SmartMassingDraftService.DefaultLevelSpacingFeet;
        string? rawLevelSpacing = ShowInputDialog(
            "Default level spacing and roof step, feet (OpenAI sorts roles/levels only):",
            currentLevelSpacing.ToString("G", CultureInfo.InvariantCulture),
            "AI 3D Sort");
        if (rawLevelSpacing == null)
            return;
        if (!double.TryParse(rawLevelSpacing.Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out double levelSpacingFeet) ||
            levelSpacingFeet <= 0 ||
            levelSpacingFeet > 40)
        {
            PostStatusWarning("Enter a level spacing value between 1 and 40 feet.");
            return;
        }

        try
        {
            _settings.MassingLevelSpacingFeet = levelSpacingFeet;
            SaveAppSettings();
            string model = AppSettingsStore.ResolveOpenAiModel(_settings);
            TxtStatus.Text = $"AI 3D Sort running with {model}...";

            SmartMassingAiTakeoffBuildResult result;
            using (ShowBusyOverlay($"AI 3D Sort running with {model}..."))
            {
                await WaitForBusyOverlayRenderAsync();
                result = await SmartMassingTakeoffAiPlanner.BuildDraftAsync(
                    _currentJob,
                    levelSpacingFeet,
                    apiKey,
                    model,
                    CancellationToken.None);
            }
            if (!result.Success || result.Draft == null)
            {
                TxtStatus.Text = $"AI 3D Sort failed: {result.Error}";
                MessageBox.Show(
                    $"AI 3D Sort failed:\n{result.Error}\n\nInput: {result.InputPath}\nRaw: {result.RawResponsePath}",
                    "AI 3D Sort",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string path = SmartMassingDraftService.ModelPath(_currentJob);
            RefreshMassingDraftPanel(result.Draft, path);
            Refresh3dManagerSummary();
            if (_rightWorkspaceTabs != null && _massingTab != null)
                _rightWorkspaceTabs.SelectedItem = _massingTab;

            string summary = BuildMassingDraftSummary(result.Draft, path);
            TxtStatus.Text = $"AI 3D Sort saved draft -> {Path.GetRelativePath(_currentJob.RootPath, path)}";
            MessageBox.Show(
                $"{summary}\nAI sort files:\n- Input: {result.InputPath}\n- Plan: {result.PlanPath}\n- Raw: {result.RawResponsePath}",
                "AI 3D Sort",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowOperationError("AI 3D Sort", ex);
        }
    }
}
