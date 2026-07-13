using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OurPlanCore.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    private async void BtnImportPlanSwiftJob_Click(object sender, RoutedEventArgs e)
    {
        await RunAsyncUiHandler(
            ImportPlanSwiftJobAsync,
            "Import PlanSwift job failed.",
            "Import PlanSwift Job");
    }

    private async void BtnImportPlanSwiftToCurrentJob_Click(object sender, RoutedEventArgs e)
    {
        await RunAsyncUiHandler(
            ImportPlanSwiftToCurrentJobAsync,
            "Import PlanSwift to current job failed.",
            "Import PlanSwift to Current Job");
    }

    private async Task ImportPlanSwiftJobAsync()
    {
        string defaultDestination = Directory.Exists(_settings.JobsRootPath)
            ? _settings.JobsRootPath
            : SampleJobService.DefaultJobsRoot;

        var dialog = new PlanSwiftImportDialog(defaultDestination)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        PlanSwiftImportOptions options = dialog.ImportOptions;
        PlanSwiftImportResult result;
        using (ShowBusyOverlay("Importing PlanSwift job..."))
        {
            await WaitForBusyOverlayRenderAsync();
            TxtStatus.Text = "Importing PlanSwift job. The source folder is read-only.";
            result = await Task.Run(() => PlanSwiftProjectImporter.Import(options));
        }

        string destinationParent = Path.GetDirectoryName(result.DestinationJobPath) ?? options.DestinationParentPath;
        _settings.JobsRootPath = destinationParent;
        AppSettingsStore.AddJobsRoot(_settings, destinationParent);
        SaveAppSettings();

        OpenJob(result.DestinationJobPath);

        string reportPath = PlanSwiftImportReportPath(result.DestinationJobPath);
        TxtStatus.Text =
            $"Imported job: {result.PagesImported} page(s), " +
            $"{result.TakeoffFoldersImported} takeoff folder(s), " +
            $"{result.TakeoffItemsImported} takeoff item(s), " +
            $"{result.MeasurementsImported} measurement(s). Report: {reportPath}";
        ShowPlanSwiftImportResult(result, reportPath);
    }

    private async Task ImportPlanSwiftToCurrentJobAsync()
    {
        if (_currentJob == null)
        {
            PostStatusInfo("Open or create a job before importing PlanSwift into the current job.");
            return;
        }

        string currentJobPath = _currentJob.RootPath;
        var dialog = new PlanSwiftImportDialog(
            currentJobPath,
            importIntoCurrentJob: true,
            currentJobName: _currentJob.Name)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        PlanSwiftImportOptions options = dialog.ImportOptions;
        PlanSwiftImportResult result;
        using (ShowBusyOverlay("Importing PlanSwift job into current job..."))
        {
            await WaitForBusyOverlayRenderAsync();
            TxtStatus.Text = "Importing PlanSwift job into the current job. The source folder is read-only.";
            result = await Task.Run(() => PlanSwiftProjectImporter.Import(options));
        }

        OpenJob(currentJobPath);

        string reportPath = PlanSwiftImportReportPath(result.DestinationJobPath);
        TxtStatus.Text =
            $"Imported PlanSwift into current job: {result.PagesImported} page(s), " +
            $"{result.TakeoffFoldersImported} takeoff folder(s), " +
            $"{result.TakeoffItemsImported} takeoff item(s), " +
            $"{result.MeasurementsImported} measurement(s). " +
            $"Placed under {PlanSwiftImportOptions.DefaultCurrentJobImportFolderName}. Report: {reportPath}";
        ShowPlanSwiftImportResult(result, reportPath);
    }

    private void ShowPlanSwiftImportResult(PlanSwiftImportResult result, string reportPath)
    {
        var window = new Window
        {
            Title = "PlanSwift Import Complete",
            Width = 640,
            Height = 420,
            MinWidth = 520,
            MinHeight = 320,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResizeWithGrip,
        };

        var root = new DockPanel { Margin = new Thickness(12) };
        window.Content = root;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var openReport = new Button
        {
            Content = "Open Report",
            MinWidth = 96,
            Margin = new Thickness(0, 0, 6, 0),
            IsEnabled = File.Exists(reportPath),
        };
        var close = new Button { Content = "Close", MinWidth = 82, IsDefault = true };
        buttons.Children.Add(openReport);
        buttons.Children.Add(close);

        var summary = new TextBlock
        {
            Text = BuildPlanSwiftImportSummary(result, reportPath),
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
        };
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = summary,
        };
        root.Children.Add(scroll);

        openReport.Click += (_, _) => OpenPlanSwiftImportReport(reportPath);
        close.Click += (_, _) => window.Close();
        window.ShowDialog();
    }

    private static string BuildPlanSwiftImportSummary(PlanSwiftImportResult result, string reportPath)
    {
        var lines = new[]
        {
            $"Source: {result.SourceJobPath}",
            $"Destination: {result.DestinationJobPath}",
            "",
            $"Pages imported: {result.PagesImported}",
            $"Takeoff folders imported: {result.TakeoffFoldersImported}",
            $"Takeoff items imported: {result.TakeoffItemsImported}",
            $"Measurements imported: {result.MeasurementsImported}",
            $"Warnings: {result.Warnings}",
            "",
            $"Report: {reportPath}",
            "",
        }.ToList();

        if (result.Messages.Count > 0)
        {
            lines.Add("Warnings preview:");
            lines.AddRange(result.Messages.Take(20).Select(message => $"- {message}"));
            if (result.Messages.Count > 20)
                lines.Add($"- ... {result.Messages.Count - 20} more warnings");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void OpenPlanSwiftImportReport(string reportPath)
    {
        if (!File.Exists(reportPath))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = reportPath,
            UseShellExecute = true,
        });
    }

    private static string PlanSwiftImportReportPath(string destinationJobPath) =>
        Path.Combine(destinationJobPath, "import_reports", "planswift_import_report.md");
}
