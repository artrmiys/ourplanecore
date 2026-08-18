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
        if (!UseLegacyFolderForNewProjects())
        {
            await ImportPlanSwiftJobAsOurPlanAsync(options);
            return;
        }

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

        if (!OpenJob(result.DestinationJobPath))
        {
            TxtStatus.Text = "PlanSwift import completed, but the current job could not be closed safely.";
            return;
        }

        string reportPath = PlanSwiftImportReportPath(result.DestinationJobPath);
        TxtStatus.Text =
            $"Imported job: {result.PagesImported} page(s), " +
            $"{result.TakeoffFoldersImported} takeoff folder(s), " +
            $"{result.TakeoffItemsImported} takeoff item(s), " +
            $"{result.MeasurementsImported} measurement(s). Report: {reportPath}";
        ShowPlanSwiftImportResult(result, reportPath);
    }

    private async Task ImportPlanSwiftJobAsOurPlanAsync(PlanSwiftImportOptions sourceOptions)
    {
        string displayName = string.IsNullOrWhiteSpace(sourceOptions.DestinationJobName)
            ? Path.GetFileName(sourceOptions.SourceJobPath)
            : sourceOptions.DestinationJobName.Trim();
        var saveDialog = CreateOurPlanSaveDialog(displayName);
        if (Directory.Exists(sourceOptions.DestinationParentPath))
            saveDialog.InitialDirectory = sourceOptions.DestinationParentPath;
        if (saveDialog.ShowDialog(this) != true || !PrepareCurrentJobForSwitch())
            return;

        OurPlanManagedWorkspaceReservation? reservation = null;
        OurPlanPackageSession? packageSession = null;
        try
        {
            reservation = OurPlanPackageWorkspace.ReserveManagedWorkspace(displayName);
            PlanSwiftImportOptions managedOptions = ForManagedPlanSwiftImport(sourceOptions, reservation);
            PlanSwiftImportResult result;
            using (ShowBusyOverlay("Importing PlanSwift job into an OurPlan project..."))
            {
                await WaitForBusyOverlayRenderAsync();
                TxtStatus.Text = "Importing PlanSwift job. The source folder is read-only.";
                result = await Task.Run(() => PlanSwiftProjectImporter.Import(managedOptions));
            }

            OurPlanCoreJob managedJob = OurPlanPackageWorkspace.CompleteManagedWorkspace(
                reservation,
                result.DestinationJobPath);
            result = result with { DestinationJobPath = managedJob.RootPath };
            packageSession = RunResponsivePackageOperation(() =>
                OurPlanPackageWriter.SaveAs(
                    managedJob.RootPath,
                    saveDialog.FileName,
                    displayName,
                    overwriteExisting: File.Exists(saveDialog.FileName),
                    projectId: reservation.ProjectId));
            _openingPackageSession = packageSession;
            // Re-enter OpenJob's checkpointed switch preparation after the long import/package write.
            if (!OpenJob(managedJob.RootPath))
            {
                OurPlanPackageWorkspace.MarkSessionClosed(packageSession);
                throw new IOException("The imported OurPlan project could not be opened safely.");
            }
            packageSession.HasUnpackagedChanges = true;
            if (!TrySaveCurrentPackage("PlanSwift import initialization"))
            {
                TxtStatus.Text =
                    $"PlanSwift import completed in the preserved local working copy " +
                    $"({result.PagesImported} page(s)), but the .ourplan file was not updated.";
                return;
            }

            string artifactParent = Path.GetDirectoryName(packageSession.PackagePath) ?? "";
            if (Directory.Exists(artifactParent))
            {
                _settings.JobsRootPath = artifactParent;
                AppSettingsStore.AddJobsRoot(_settings, artifactParent);
                SaveAppSettings();
            }
            string reportPath = PlanSwiftImportReportPath(managedJob.RootPath);
            TxtStatus.Text =
                $"Imported OurPlan project: {result.PagesImported} page(s), " +
                $"{result.TakeoffFoldersImported} takeoff folder(s), " +
                $"{result.TakeoffItemsImported} takeoff item(s), " +
                $"{result.MeasurementsImported} measurement(s).";
            ShowPlanSwiftImportResult(
                result,
                reportPath,
                packageSession.PackagePath,
                Path.GetRelativePath(managedJob.RootPath, reportPath));
        }
        finally
        {
            _openingPackageSession = null;
            if (reservation != null && packageSession == null)
                OurPlanPackageWorkspace.AbandonManagedWorkspace(reservation);
        }
    }

    private static PlanSwiftImportOptions ForManagedPlanSwiftImport(
        PlanSwiftImportOptions source,
        OurPlanManagedWorkspaceReservation reservation) =>
        new()
        {
            SourceJobPath = source.SourceJobPath,
            DestinationParentPath = reservation.ImportParentRoot,
            DestinationJobName = reservation.DisplayName,
            ConvertPageImages = source.ConvertPageImages,
            ImportAllSheetsAndTakeoffFolders = source.ImportAllSheetsAndTakeoffFolders,
            MaxPages = source.MaxPages,
            MaxTakeoffItems = source.MaxTakeoffItems,
            MaxMeasurements = source.MaxMeasurements,
            PortableReportPaths = true,
        };

    private async Task ImportPlanSwiftToCurrentJobAsync()
    {
        if (_currentJob == null)
        {
            PostStatusInfo("Open or create a job before importing PlanSwift into the current job.");
            return;
        }
        if (!EnsureCurrentJobWritable("import PlanSwift data into this job"))
            return;

        OurPlanCoreJob importJob = _currentJob;
        string currentJobPath = importJob.RootPath;
        var dialog = new PlanSwiftImportDialog(
            currentJobPath,
            importIntoCurrentJob: true,
            currentJobName: importJob.Name)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;
        if (!EnsureExpectedJobWritable(importJob, "import PlanSwift data into this job", showDialog: true))
            return;

        PlanSwiftImportOptions options = dialog.ImportOptions;
        if (!SameJobPath(options.DestinationJobPath, currentJobPath))
        {
            PostStatusInfo("PlanSwift import cancelled because its destination no longer matches the current job.");
            return;
        }
        if (!TryFlushTakeoffAutosaves("import PlanSwift data into the current job"))
            return;
        if (!EnsureExpectedJobWritable(importJob, "import PlanSwift data into this job"))
            return;

        PlanSwiftImportResult result;
        using (ShowBusyOverlay("Importing PlanSwift job into current job..."))
        {
            await WaitForBusyOverlayRenderAsync();
            if (!EnsureExpectedJobWritable(importJob, "import PlanSwift data into this job"))
                return;
            TxtStatus.Text = "Importing PlanSwift job into the current job. The source folder is read-only.";
            try
            {
                JobWriteAccess.Demand(currentJobPath, "import PlanSwift data into this job");
                result = await Task.Run(() => PlanSwiftProjectImporter.Import(options));
            }
            catch (JobWriteDeniedException ex)
            {
                TxtStatus.Text = $"PlanSwift import stopped because write access changed: {ex.Message}";
                return;
            }
        }

        if (!EnsureExpectedJobWritable(importJob, "reload the PlanSwift import result"))
            return;
        if (!OpenJob(currentJobPath))
        {
            TxtStatus.Text = "PlanSwift import completed, but the current job could not be reloaded safely.";
            return;
        }

        string reportPath = PlanSwiftImportReportPath(result.DestinationJobPath);
        TxtStatus.Text =
            $"Imported PlanSwift into current job: {result.PagesImported} page(s), " +
            $"{result.TakeoffFoldersImported} takeoff folder(s), " +
            $"{result.TakeoffItemsImported} takeoff item(s), " +
            $"{result.MeasurementsImported} measurement(s). " +
            $"Placed under {PlanSwiftImportOptions.DefaultCurrentJobImportFolderName}. Report: {reportPath}";
        ShowPlanSwiftImportResult(result, reportPath);
    }

    private void ShowPlanSwiftImportResult(
        PlanSwiftImportResult result,
        string reportPath,
        string? displayDestination = null,
        string? displayReportPath = null)
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
            Text = BuildPlanSwiftImportSummary(
                result,
                displayDestination ?? result.DestinationJobPath,
                displayReportPath ?? reportPath),
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

    private static string BuildPlanSwiftImportSummary(
        PlanSwiftImportResult result,
        string destinationPath,
        string reportPath)
    {
        var lines = new[]
        {
            $"Source: {result.SourceJobPath}",
            $"Destination: {destinationPath}",
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
