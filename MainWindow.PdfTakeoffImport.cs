using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OurPlanCore.Controls;
using SkiaSharp;

namespace OurPlanCore;

public partial class MainWindow
{
    private const string PdfTakeoffImportFolderName = "from pdf";

    private sealed record PdfTakeoffImportSource(
        string PdfPath,
        PdfTakeoffAnnotationImportResult Annotations,
        IReadOnlyList<string> PageNames,
        IReadOnlyDictionary<int, double> MetadataScales,
        string ImportPdfPath = "");

    private sealed record PdfTakeoffImportMeasurement(
        PageInfo Page,
        int PageIndex,
        PdfTakeoffAnnotationMeasurement Annotation);

    private sealed record PdfTakeoffImportGroupKey(string Type, string Color);

    private sealed class PdfTakeoffImportRunResult
    {
        public string SourceFolder { get; init; } = "";
        public string PagesFolder { get; set; } = "";
        public string TakeoffsFolder { get; set; } = "";
        public string ReportPath { get; set; } = "";
        public bool Cancelled { get; set; }
        public bool HadSupportedAnnotations { get; set; }
        public int PdfsScanned { get; set; }
        public int PdfsWithSupportedAnnotations { get; set; }
        public int PagesToImport { get; set; }
        public int RulersToImport { get; set; }
        public int RulersImported { get; set; }
        public int MeasurementsToImport { get; set; }
        public int TakeoffItemsToCreate { get; set; }
        public int CleanPdfAnnotationsRemoved { get; set; }
        public int PdfsImported { get; set; }
        public int PagesImported { get; set; }
        public int TakeoffItemsImported { get; set; }
        public int MeasurementsImported { get; set; }
        public string FirstImportedPageFolder { get; set; } = "";
        public OurPlanCoreJob? TargetJob { get; set; }
        public List<string> Messages { get; } = [];
    }

    private sealed class PdfTakeoffImportScanResult
    {
        public PdfTakeoffImportRunResult Run { get; init; } = new();
        public List<PdfTakeoffImportSource> Sources { get; init; } = [];
    }

    private async void BtnImportPdfTakeoffs_Click(object sender, RoutedEventArgs e)
    {
        await RunAsyncUiHandler(
            () => ImportPdfTakeoffsFromFolderAsync(sender),
            "Import PDF takeoffs failed.",
            "Import PDF Takeoffs");
    }

    private async Task ImportPdfTakeoffsFromFolderAsync(object sender)
    {
        OurPlanCoreJob? dialogOriginJob = _currentJob;
        PdfTakeoffImportOptions? options = ShowPdfTakeoffImportDialog();
        if (options == null)
            return;

        if (options.Mode == PdfTakeoffImportMode.ImportIntoCurrentJob && dialogOriginJob == null)
        {
            PostStatusInfo("Open or create a job before importing PDF takeoffs into the current job.");
            return;
        }
        if (options.Mode == PdfTakeoffImportMode.ImportIntoCurrentJob &&
            !EnsureExpectedJobWritable(dialogOriginJob!, "import PDF takeoffs into this job"))
        {
            return;
        }
        OurPlanCoreJob? expectedTargetJob = options.Mode == PdfTakeoffImportMode.ImportIntoCurrentJob
            ? dialogOriginJob
            : null;

        IReadOnlyList<string> pdfPaths = PdfImportSourceFinder.FindPdfFilesRecursive(options.SourceFolder);
        if (pdfPaths.Count == 0)
        {
            PostStatusInfo("No PDF files were found in the selected folder or its subfolders.");
            return;
        }

        var importButton = sender as Button;
        if (importButton != null)
            importButton.IsEnabled = false;

        try
        {
            PdfTakeoffImportRunResult result = await ScanAndImportPdfTakeoffsAsync(options, pdfPaths, expectedTargetJob);
            if (result.TargetJob != null &&
                !EnsureExpectedJobWritable(result.TargetJob, "finish the PDF takeoff import"))
            {
                return;
            }
            if (result.Cancelled || !result.HadSupportedAnnotations)
            {
                TxtStatus.Text = result.HadSupportedAnnotations
                    ? "PDF takeoff import cancelled before writing job files."
                    : "No supported PDF takeoff annotations were found.";
                ShowPdfTakeoffImportResult(result);
                if (result.TargetJob != null)
                    EnsureExpectedJobWritable(result.TargetJob, "finish the PDF takeoff import dialog");
                return;
            }

            if (result.TargetJob != null &&
                HasCurrentPackageSession &&
                SameJobPath(result.TargetJob.RootPath, _currentJob!.RootPath))
            {
                _currentPackageSession!.HasUnpackagedChanges = true;
                if (!TrySaveCurrentPackage("PDF takeoff import"))
                {
                    result.Messages.Add(
                        "The import is preserved in the local working copy, but the .ourplan file was not updated.");
                    ShowPdfTakeoffImportResult(result);
                    return;
                }
            }

            ReloadPagesTree();
            LoadTakeoffsForJob();
            if (!string.IsNullOrWhiteSpace(result.FirstImportedPageFolder))
                SelectPageByFolder(result.FirstImportedPageFolder);

            TxtStatus.Text =
                $"Imported PDF takeoffs: {result.PdfsImported} PDF(s), {result.PagesImported} page(s), " +
                $"{result.RulersImported} ruler(s), {result.TakeoffItemsImported} takeoff item(s), " +
                $"{result.MeasurementsImported} measurement(s). Report: {ProjectDataPathForDisplay(result.ReportPath)}";
            ShowPdfTakeoffImportResult(result);
            if (result.TargetJob != null)
                EnsureExpectedJobWritable(result.TargetJob, "finish the PDF takeoff import dialog");
        }
        finally
        {
            if (importButton != null)
                importButton.IsEnabled = true;
        }
    }

    private PdfTakeoffImportOptions? ShowPdfTakeoffImportDialog()
    {
        string defaultSource = NewPdfImportInitialFolder() ?? "";
        string defaultJobsRoot = Directory.Exists(_settings.JobsRootPath)
            ? _settings.JobsRootPath
            : SampleJobService.DefaultJobsRoot;
        string defaultJobName = Directory.Exists(defaultSource)
            ? PdfTakeoffImportDialog.DefaultJobName(defaultSource)
            : "PDF Takeoffs";
        var dialog = new PdfTakeoffImportDialog(
            defaultSource,
            defaultJobsRoot,
            defaultJobName,
            hasCurrentJob: _currentJob != null,
            currentJobName: _currentJob?.Name ?? "")
        {
            Owner = this,
        };
        return dialog.ShowDialog() == true ? dialog.ImportOptions : null;
    }

    private async Task<PdfTakeoffImportRunResult> ScanAndImportPdfTakeoffsAsync(
        PdfTakeoffImportOptions options,
        IReadOnlyList<string> pdfPaths,
        OurPlanCoreJob? expectedTargetJob)
    {
        var (pagesFolderPreview, takeoffsFolderPreview) = PreviewPdfTakeoffImportDestinations(options);
        var run = new PdfTakeoffImportRunResult
        {
            SourceFolder = options.SourceFolder,
            PagesFolder = pagesFolderPreview,
            TakeoffsFolder = takeoffsFolderPreview,
            PdfsScanned = pdfPaths.Count,
            TargetJob = expectedTargetJob,
        };
        PdfTakeoffImportScanResult scan = await ScanPdfTakeoffSourcesAsync(
            run,
            pdfPaths,
            options,
            expectedTargetJob);
        if (!EnsurePdfTakeoffImportTargetWritable(
                expectedTargetJob,
                scan.Run,
                "continue the PDF takeoff import after scanning"))
        {
            return scan.Run;
        }

        if (scan.Sources.Count == 0)
            return scan.Run;

        scan.Run.HadSupportedAnnotations = true;
        bool confirmed = ConfirmPdfTakeoffImport(scan.Run, scan.Sources, options);
        if (!EnsurePdfTakeoffImportTargetWritable(
                expectedTargetJob,
                scan.Run,
                "continue the PDF takeoff import after confirmation"))
        {
            return scan.Run;
        }
        if (!confirmed)
        {
            scan.Run.Cancelled = true;
            scan.Run.Messages.Add("Import cancelled after preview; no job files were written.");
            return scan.Run;
        }

        var prepared = await PreparePdfTakeoffImportSourcesAsync(
            scan.Sources,
            options,
            scan.Run,
            expectedTargetJob);
        try
        {
            if (scan.Run.Cancelled ||
                !EnsurePdfTakeoffImportTargetWritable(
                    expectedTargetJob,
                    scan.Run,
                    "continue the PDF takeoff import after preparing PDFs"))
            {
                return scan.Run;
            }

            OurPlanCoreJob? targetJob = EnsurePdfTakeoffImportTargetJob(options, expectedTargetJob);
            if (targetJob == null)
            {
                scan.Run.Cancelled = true;
                scan.Run.Messages.Add("Import cancelled before a writable target project became active.");
                return scan.Run;
            }
            scan.Run.TargetJob = targetJob;
            if (!EnsurePdfTakeoffImportTargetWritable(
                    targetJob,
                    scan.Run,
                    "start writing the PDF takeoff import"))
            {
                return scan.Run;
            }

            string pageParent = options.Mode == PdfTakeoffImportMode.ImportIntoCurrentJob
                ? GetSelectedImportFolder()
                : targetJob.PagesRoot;
            string takeoffParent = options.Mode == PdfTakeoffImportMode.ImportIntoCurrentJob
                ? CurrentTakeoffParentFolder()
                : targetJob.TakeoffsRoot;
            if (!EnsurePdfTakeoffImportTargetWritable(
                    targetJob,
                    scan.Run,
                    "create PDF takeoff import folders"))
            {
                return scan.Run;
            }
            string pagesFolder = EnsurePdfTakeoffImportBucket(pageParent, targetJob.PagesRoot);
            string takeoffsFolder = EnsurePdfTakeoffImportBucket(takeoffParent, targetJob.TakeoffsRoot);
            scan.Run.PagesFolder = pagesFolder;
            scan.Run.TakeoffsFolder = takeoffsFolder;

            using (ShowBusyOverlay($"Importing PDF takeoffs from {prepared.Sources.Count} PDF file(s)..."))
            {
                await WaitForBusyOverlayRenderAsync();
                if (!EnsurePdfTakeoffImportTargetWritable(
                        targetJob,
                        scan.Run,
                        "continue writing the PDF takeoff import"))
                {
                    return scan.Run;
                }
                for (int index = 0; index < prepared.Sources.Count; index++)
                {
                    PdfTakeoffImportSource source = prepared.Sources[index];
                    string pdfName = Path.GetFileName(source.PdfPath);
                    BusyOverlayText.Text = $"Importing PDF takeoffs {index + 1}/{prepared.Sources.Count}: {pdfName}";
                    TxtStatus.Text = BusyOverlayText.Text;
                    if (!EnsurePdfTakeoffImportTargetWritable(
                            targetJob,
                            scan.Run,
                            "import PDF takeoffs into the target job"))
                    {
                        return scan.Run;
                    }
                    ImportPdfTakeoffSource(targetJob, source, pagesFolder, takeoffsFolder, options, scan.Run);
                }
            }

            if (!EnsurePdfTakeoffImportTargetWritable(
                    targetJob,
                    scan.Run,
                    "write the PDF takeoff import report"))
            {
                return scan.Run;
            }
            bool portableReport = HasCurrentPackageSession &&
                                  SameJobPath(targetJob.RootPath, _currentPackageSession!.WorkspaceRoot);
            scan.Run.ReportPath = WritePdfTakeoffImportReport(targetJob,
                scan.Run,
                prepared.Sources,
                portableReport);
            return scan.Run;
        }
        finally
        {
            TryDeleteDirectory(prepared.TempRoot);
        }
    }

    private async Task<PdfTakeoffImportScanResult> ScanPdfTakeoffSourcesAsync(
        PdfTakeoffImportRunResult run,
        IReadOnlyList<string> pdfPaths,
        PdfTakeoffImportOptions options,
        OurPlanCoreJob? expectedTargetJob)
    {
        var scanResult = new PdfTakeoffImportScanResult { Run = run };
        bool multiPdf = pdfPaths.Count > 1;
        using (ShowBusyOverlay($"Scanning {pdfPaths.Count} PDF file(s) for takeoff annotations..."))
        {
            await WaitForBusyOverlayRenderAsync();
            if (!EnsurePdfTakeoffImportTargetWritable(
                    expectedTargetJob,
                    run,
                    "continue scanning PDF takeoffs"))
            {
                return scanResult;
            }
            for (int index = 0; index < pdfPaths.Count; index++)
            {
                string pdfPath = pdfPaths[index];
                string pdfName = Path.GetFileName(pdfPath);
                BusyOverlayText.Text = $"Scanning PDF takeoffs {index + 1}/{pdfPaths.Count}: {pdfName}";
                TxtStatus.Text = BusyOverlayText.Text;

                var read = await PdfTakeoffAnnotationImportService.TryReadAsync(pdfPath);
                if (!EnsurePdfTakeoffImportTargetWritable(
                        expectedTargetJob,
                        run,
                        "continue scanning PDF takeoffs"))
                {
                    return scanResult;
                }
                if (!read.Ok)
                {
                    run.Messages.Add($"{pdfName}: scan failed - {read.Error}");
                    continue;
                }

                int selectedCount = SelectedPdfAnnotations(read.Result, options).Count();
                if (selectedCount == 0)
                {
                    run.Messages.Add($"{pdfName}: no supported selected PDF takeoff/ruler annotations found.");
                    continue;
                }

                IReadOnlyList<string> pageNames = BuildPdfTakeoffPageNames(
                    pdfPath,
                    read.Result.PageCount,
                    multiPdf,
                    out IReadOnlyDictionary<int, double> metadataScales);
                scanResult.Sources.Add(new PdfTakeoffImportSource(pdfPath, read.Result, pageNames, metadataScales));
            }
        }

        run.PdfsWithSupportedAnnotations = scanResult.Sources.Count;
        run.PagesToImport = scanResult.Sources.Sum(source => source.Annotations.PageCount);
        run.RulersToImport = scanResult.Sources.Sum(source => source.Annotations.Pages
            .SelectMany(page => page.Measurements)
            .Count(measurement => ShouldImportDimension(measurement, options)));
        run.MeasurementsToImport = scanResult.Sources.Sum(source => source.Annotations.Pages
            .SelectMany(page => page.Measurements)
            .Count(measurement => ShouldImportTakeoff(measurement, options)));
        run.TakeoffItemsToCreate = CountPdfTakeoffImportItems(scanResult.Sources, options);
        return scanResult;
    }

    private async Task<(List<PdfTakeoffImportSource> Sources, string TempRoot)> PreparePdfTakeoffImportSourcesAsync(
        IReadOnlyList<PdfTakeoffImportSource> sources,
        PdfTakeoffImportOptions options,
        PdfTakeoffImportRunResult run,
        OurPlanCoreJob? expectedTargetJob)
    {
        if (!options.RemoveSupportedPdfAnnotations)
            return (sources.ToList(), "");

        string tempRoot = Path.Combine(Path.GetTempPath(), "OurPlanCorePdfTakeoffImport", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var prepared = new List<PdfTakeoffImportSource>();
        using (ShowBusyOverlay($"Creating clean PDF copies for {sources.Count} PDF file(s)..."))
        {
            await WaitForBusyOverlayRenderAsync();
            if (!EnsurePdfTakeoffImportTargetWritable(
                    expectedTargetJob,
                    run,
                    "continue preparing PDF takeoff files"))
            {
                return (prepared, tempRoot);
            }
            for (int index = 0; index < sources.Count; index++)
            {
                PdfTakeoffImportSource source = sources[index];
                string cleanName = $"{Path.GetFileNameWithoutExtension(source.PdfPath)}.clean.pdf";
                string cleanPath = OurPlanCoreJobStore.UniqueFilePath(Path.Combine(tempRoot, cleanName));
                string pdfName = Path.GetFileName(source.PdfPath);
                BusyOverlayText.Text = $"Cleaning PDF annotations {index + 1}/{sources.Count}: {pdfName}";
                TxtStatus.Text = BusyOverlayText.Text;

                var clean = await PdfTakeoffAnnotationImportService.TryCreateCleanCopyAsync(source.PdfPath, cleanPath);
                if (!EnsurePdfTakeoffImportTargetWritable(
                        expectedTargetJob,
                        run,
                        "continue preparing PDF takeoff files"))
                {
                    return (prepared, tempRoot);
                }
                if (!clean.Ok)
                    throw new InvalidOperationException($"{pdfName}: clean PDF copy failed - {clean.Error}");

                run.CleanPdfAnnotationsRemoved += clean.Result.RemovedAnnotations;
                prepared.Add(source with { ImportPdfPath = clean.Result.OutputPath });
            }
        }

        return (prepared, tempRoot);
    }

    private OurPlanCoreJob? EnsurePdfTakeoffImportTargetJob(
        PdfTakeoffImportOptions options,
        OurPlanCoreJob? expectedTargetJob)
    {
        if (options.Mode == PdfTakeoffImportMode.ImportIntoCurrentJob)
            return expectedTargetJob ??
                throw new InvalidOperationException("The PDF takeoff target job is no longer available.");

        string parent = string.IsNullOrWhiteSpace(options.JobsRootPath)
            ? SampleJobService.DefaultJobsRoot
            : options.JobsRootPath;
        Directory.CreateDirectory(parent);
        string jobName = UniquePdfTakeoffJobName(parent, options.JobName);
        options.JobName = jobName;
        _settings.JobsRootPath = parent;
        AppSettingsStore.AddJobsRoot(_settings, parent);
        SaveAppSettings();

        if (!UseLegacyFolderForNewProjects())
        {
            return CreateNewPackageProject(jobName, out OurPlanCoreJob? packageJob, parent)
                ? packageJob
                : null;
        }

        OurPlanCoreJob job = OurPlanCoreJobStore.CreateJob(parent, jobName);
        if (!OpenJob(job.RootPath))
            throw new InvalidOperationException("The new PDF takeoff job could not be opened because the current job has unsaved changes.");

        if (_currentJob == null || !SameJobPath(_currentJob.RootPath, job.RootPath))
            throw new InvalidOperationException("The new PDF takeoff job was created but is no longer the active target job.");
        return _currentJob;
    }

    private bool EnsurePdfTakeoffImportTargetWritable(
        OurPlanCoreJob? targetJob,
        PdfTakeoffImportRunResult run,
        string operation)
    {
        if (targetJob == null || EnsureExpectedJobWritable(targetJob, operation))
            return true;

        run.Cancelled = true;
        run.Messages.Add("Import stopped because the target job changed or became read-only.");
        return false;
    }

    private (string PagesFolder, string TakeoffsFolder) PreviewPdfTakeoffImportDestinations(PdfTakeoffImportOptions options)
    {
        if (options.Mode == PdfTakeoffImportMode.ImportIntoCurrentJob && _currentJob != null)
        {
            return (
                PreviewPdfTakeoffImportBucketPath(GetSelectedImportFolder(), _currentJob.PagesRoot),
                PreviewPdfTakeoffImportBucketPath(CurrentTakeoffParentFolder(), _currentJob.TakeoffsRoot));
        }

        string parent = string.IsNullOrWhiteSpace(options.JobsRootPath)
            ? SampleJobService.DefaultJobsRoot
            : options.JobsRootPath;
        string jobName = UniquePdfTakeoffJobName(parent, options.JobName);
        if (!UseLegacyFolderForNewProjects())
        {
            string packagePath = Path.Combine(
                parent,
                OurPlanCoreJobStore.SanitizeName(jobName, 120) + OurPlanPackageFormat.Extension);
            return (
                $"{packagePath} :: Pages\\{PdfTakeoffImportFolderName}",
                $"{packagePath} :: Takeoffs\\{PdfTakeoffImportFolderName}");
        }

        string jobRoot = Path.Combine(parent, OurPlanCoreJobStore.SanitizeName(jobName, 120));
        return (
            Path.Combine(jobRoot, "Pages", PdfTakeoffImportFolderName),
            Path.Combine(jobRoot, "Takeoffs", PdfTakeoffImportFolderName));
    }

    private static string UniquePdfTakeoffJobName(string parent, string requestedName)
    {
        string baseName = OurPlanCoreJobStore.NormalizeDisplayName(
            string.IsNullOrWhiteSpace(requestedName) ? "PDF Takeoffs" : requestedName,
            120);
        string candidate = baseName;
        for (int i = 2; ; i++)
        {
            string folder = Path.Combine(parent, OurPlanCoreJobStore.SanitizeName(candidate, 120));
            if (!Directory.Exists(folder))
                return candidate;
            candidate = $"{baseName} ({i})";
        }
    }

    private string EnsurePdfTakeoffImportBucket(string parentFolder, string rootFolder)
    {
        string parent = string.IsNullOrWhiteSpace(parentFolder) || !Directory.Exists(parentFolder)
            ? rootFolder
            : parentFolder;
        if (string.Equals(OurPlanCoreJobStore.DisplayName(parent), PdfTakeoffImportFolderName, StringComparison.OrdinalIgnoreCase))
            return parent;

        return OurPlanCoreJobStore.EnsureFolder(parent, PdfTakeoffImportFolderName);
    }

    private static string PreviewPdfTakeoffImportBucketPath(string parentFolder, string rootFolder)
    {
        string parent = string.IsNullOrWhiteSpace(parentFolder) || !Directory.Exists(parentFolder)
            ? rootFolder
            : parentFolder;
        if (string.Equals(OurPlanCoreJobStore.DisplayName(parent), PdfTakeoffImportFolderName, StringComparison.OrdinalIgnoreCase))
            return parent;

        string folderName = OurPlanCoreJobStore.SanitizeName(PdfTakeoffImportFolderName, 120);
        return Path.Combine(parent, folderName);
    }

    private IReadOnlyList<string> BuildPdfTakeoffPageNames(
        string pdfPath,
        int pageCount,
        bool includePdfName,
        out IReadOnlyDictionary<int, double> metadataScales)
    {
        List<string> names = DefaultPageNames(pdfPath, pageCount, includePdfName).ToList();
        var scales = new Dictionary<int, double>();
        for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var page = new PageInfo
            {
                Name = pageIndex < names.Count ? names[pageIndex] : $"Page {pageIndex + 1}",
                PdfPath = pdfPath,
                PdfPage = pageIndex,
            };
            if (!PdfSheetMetadataService.TryAnalyzePage(page, out PdfSheetMetadata metadata, out _))
                continue;

            string proposedName = metadata.ProposedPageName();
            if (!string.IsNullOrWhiteSpace(proposedName))
                names[pageIndex] = proposedName;
            if (metadata.SelectedScaleMetersPerPt > 0)
                scales[pageIndex] = metadata.SelectedScaleMetersPerPt;
        }

        metadataScales = scales;
        return names;
    }

    private static int CountPdfTakeoffImportItems(
        IReadOnlyList<PdfTakeoffImportSource> sources,
        PdfTakeoffImportOptions options) =>
        sources.Sum(source => source.Annotations.Pages
            .SelectMany(page => page.Measurements)
            .Where(measurement => ShouldImportTakeoff(measurement, options))
            .GroupBy(measurement => new PdfTakeoffImportGroupKey(measurement.Type, measurement.Color))
            .Count());

    private bool ConfirmPdfTakeoffImport(
        PdfTakeoffImportRunResult run,
        IReadOnlyList<PdfTakeoffImportSource> sources,
        PdfTakeoffImportOptions options)
    {
        var summary = new StringBuilder();
        summary.AppendLine("PDF Takeoffs preview");
        summary.AppendLine();
        summary.AppendLine($"Mode: {(options.Mode == PdfTakeoffImportMode.CreateNewJob ? "Create new job" : "Import into current job")}");
        summary.AppendLine($"PDFs scanned: {run.PdfsScanned}");
        summary.AppendLine($"PDFs with selected annotations: {run.PdfsWithSupportedAnnotations}");
        summary.AppendLine($"Pages to import: {run.PagesToImport}");
        summary.AppendLine($"Rulers to create: {run.RulersToImport}");
        summary.AppendLine($"Takeoff items to create: {run.TakeoffItemsToCreate}");
        summary.AppendLine($"Takeoff measurements: {run.MeasurementsToImport}");
        summary.AppendLine($"Clean PDF background: {(options.RemoveSupportedPdfAnnotations ? "yes" : "no")}");
        summary.AppendLine();
        summary.AppendLine("Destination:");
        summary.AppendLine($"Pages: {run.PagesFolder}");
        summary.AppendLine($"Takeoffs: {run.TakeoffsFolder}");
        summary.AppendLine();
        summary.AppendLine("Top takeoff groups across PDFs:");
        List<string> previewGroupLines = BuildPdfTakeoffPreviewGroupLines(sources, options).ToList();
        foreach (string line in previewGroupLines.Take(12))
            summary.AppendLine(line);
        int groupCount = previewGroupLines.Count;
        if (groupCount > 12)
            summary.AppendLine($"... {groupCount - 12} more group(s)");
        if (run.Messages.Count > 0)
        {
            summary.AppendLine();
            summary.AppendLine("Scan messages:");
            foreach (string message in run.Messages.Take(5))
                summary.AppendLine("- " + message);
            if (run.Messages.Count > 5)
                summary.AppendLine($"- ... {run.Messages.Count - 5} more");
        }

        summary.AppendLine();
        summary.AppendLine("Continue import?");
        MessageBoxResult result = MessageBox.Show(
            summary.ToString(),
            "Confirm PDF Takeoffs Import",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }

    private static IEnumerable<string> BuildPdfTakeoffPreviewGroupLines(
        IReadOnlyList<PdfTakeoffImportSource> sources,
        PdfTakeoffImportOptions options) =>
        sources
            .SelectMany(source => source.Annotations.Pages)
            .SelectMany(page => page.Measurements)
            .Where(measurement => ShouldImportTakeoff(measurement, options))
            .GroupBy(measurement => new PdfTakeoffImportGroupKey(measurement.Type, measurement.Color))
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.Type, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key.Color, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"- {PdfTakeoffImportItemName(group.Key.Type, group.Key.Color)}: {group.Count()}");

    private void ImportPdfTakeoffSource(
        OurPlanCoreJob targetJob,
        PdfTakeoffImportSource source,
        string pagesFolder,
        string takeoffsFolder,
        PdfTakeoffImportOptions options,
        PdfTakeoffImportRunResult run)
    {
        string pdfDisplayName = Path.GetFileNameWithoutExtension(source.PdfPath);
        string importPdfPath = string.IsNullOrWhiteSpace(source.ImportPdfPath) ? source.PdfPath : source.ImportPdfPath;
        IReadOnlyList<PageInfo> importedPages = OurPlanCoreJobStore.ImportPdf(
            targetJob,
            importPdfPath,
            source.PageNames,
            pagesFolder);
        run.PdfsImported++;
        run.PagesImported += importedPages.Count;
        if (string.IsNullOrWhiteSpace(run.FirstImportedPageFolder) && importedPages.Count > 0)
            run.FirstImportedPageFolder = importedPages[0].FolderPath;

        var importedPageByIndex = importedPages
            .GroupBy(page => page.PdfPage)
            .ToDictionary(group => group.Key, group => group.First());
        var importMeasurements = new List<PdfTakeoffImportMeasurement>();
        foreach (PdfTakeoffAnnotationPage page in source.Annotations.Pages)
        {
            if (!importedPageByIndex.TryGetValue(page.PageIndex, out PageInfo? importedPage))
                continue;

            double pageScale = ResolvePdfTakeoffPageScale(page, source);
            if (pageScale > 0)
            {
                importedPage.ScaleMetersPerPt = pageScale;
                OurPlanCoreJobStore.SavePageScale(importedPage.FolderPath, pageScale);
            }

            if (options.ImportDimensionsAsRulers)
                run.RulersImported += ImportPdfDimensionAnnotations(page, importedPage, pageScale);

            foreach (PdfTakeoffAnnotationMeasurement annotation in page.Measurements.Where(m => ShouldImportTakeoff(m, options)))
                importMeasurements.Add(new PdfTakeoffImportMeasurement(importedPage, page.PageIndex, annotation));
        }

        if (importMeasurements.Count == 0)
            return;

        string pdfTakeoffFolder = OurPlanCoreJobStore.EnsureFolder(
            takeoffsFolder,
            string.IsNullOrWhiteSpace(pdfDisplayName) ? "PDF" : pdfDisplayName);
        foreach (var group in importMeasurements.GroupBy(m => new PdfTakeoffImportGroupKey(m.Annotation.Type, m.Annotation.Color)))
        {
            string itemName = PdfTakeoffImportItemName(group.Key.Type, group.Key.Color);
            TakeoffItem item = OurPlanCoreJobStore.CreateTakeoffItem(
                targetJob,
                pdfTakeoffFolder,
                itemName,
                group.Key.Color,
                group.Key.Type);
            item.Notes = $"Imported from PDF takeoff annotations: {RelativeImportSource(options.SourceFolder, source.PdfPath)}";

            foreach (PdfTakeoffImportMeasurement imported in group)
            {
                item.Measurements.Add(CreatePdfTakeoffMeasurement(
                    imported,
                    item.FolderPath,
                    options.SourceFolder,
                    source.PdfPath));
            }

            OurPlanCoreJobStore.SaveTakeoffItem(item);
            run.TakeoffItemsImported++;
            run.MeasurementsImported += item.Measurements.Count;
        }
    }

    private static double ResolvePdfTakeoffPageScale(PdfTakeoffAnnotationPage page, PdfTakeoffImportSource source)
    {
        if (page.ScaleMPerPt > 0)
            return page.ScaleMPerPt;

        double annotationScale = page.Measurements.Select(measurement => measurement.ScaleMPerPt).FirstOrDefault(scale => scale > 0);
        if (annotationScale > 0)
            return annotationScale;

        return source.MetadataScales.TryGetValue(page.PageIndex, out double metadataScale) ? metadataScale : 0;
    }

    private int ImportPdfDimensionAnnotations(PdfTakeoffAnnotationPage page, PageInfo importedPage, double pageScale)
    {
        List<PageAnnotation> annotations = PageAnnotationStore.LoadPageAnnotations(importedPage.FolderPath);
        int before = annotations.Count;
        foreach (PdfTakeoffAnnotationMeasurement dimension in page.Measurements.Where(IsPdfDimension))
        {
            double scale = dimension.ScaleMPerPt > 0 ? dimension.ScaleMPerPt : pageScale;
            annotations.Add(new PageAnnotation
            {
                Kind = "dimension",
                Color = dimension.Color,
                StrokeWidth = 1.8,
                PageFolder = importedPage.FolderPath,
                ScaleMetersPerPt = scale,
                Points = dimension.Points.Select(point => new SKPoint(point.X, point.Y)).ToList(),
            });
        }

        if (annotations.Count != before)
            PageAnnotationStore.SavePageAnnotations(importedPage.FolderPath, annotations);
        return annotations.Count - before;
    }

    private Measurement CreatePdfTakeoffMeasurement(
        PdfTakeoffImportMeasurement imported,
        string takeoffFolder,
        string sourceFolder,
        string pdfPath)
    {
        string notes = BuildPdfTakeoffMeasurementNotes(imported.Annotation, sourceFolder, pdfPath, imported.PageIndex);
        double scale = imported.Annotation.ScaleMPerPt > 0
            ? imported.Annotation.ScaleMPerPt
            : imported.Page.ScaleMetersPerPt;
        return new Measurement
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(imported.Annotation.Subject)
                ? PdfTakeoffImportItemName(imported.Annotation.Type, imported.Annotation.Color)
                : imported.Annotation.Subject,
            Notes = notes,
            MType = imported.Annotation.Type,
            Color = imported.Annotation.Color,
            CountSymbol = imported.Annotation.Type == "point" ? CountDisplaySymbol.Circle : "",
            PageFolder = imported.Page.FolderPath,
            TakeoffFolder = takeoffFolder,
            ScaleMetersPerPt = scale,
            Points = imported.Annotation.Points.Select(point => new SKPoint(point.X, point.Y)).ToList(),
        };
    }

    private static string BuildPdfTakeoffMeasurementNotes(
        PdfTakeoffAnnotationMeasurement annotation,
        string sourceFolder,
        string pdfPath,
        int pageIndex)
    {
        var parts = new List<string>
        {
            $"Imported from PDF takeoff: {RelativeImportSource(sourceFolder, pdfPath)}",
            $"PDF page: {(pageIndex + 1).ToString(CultureInfo.InvariantCulture)}",
        };
        if (!string.IsNullOrWhiteSpace(annotation.AnnotationId))
            parts.Add($"Annotation: {annotation.AnnotationId}");
        if (!string.IsNullOrWhiteSpace(annotation.SourceSubtype))
            parts.Add($"Subtype: {annotation.SourceSubtype}");
        if (!string.IsNullOrWhiteSpace(annotation.Content))
            parts.Add($"Content: {annotation.Content}");
        return string.Join(Environment.NewLine, parts);
    }

    private static IEnumerable<PdfTakeoffAnnotationMeasurement> SelectedPdfAnnotations(
        PdfTakeoffAnnotationImportResult result,
        PdfTakeoffImportOptions options) =>
        result.Pages
            .SelectMany(page => page.Measurements)
            .Where(measurement => ShouldImportDimension(measurement, options) || ShouldImportTakeoff(measurement, options));

    private static bool ShouldImportDimension(PdfTakeoffAnnotationMeasurement measurement, PdfTakeoffImportOptions options) =>
        options.ImportDimensionsAsRulers && IsPdfDimension(measurement);

    private static bool ShouldImportTakeoff(PdfTakeoffAnnotationMeasurement measurement, PdfTakeoffImportOptions options) =>
        options.ImportTakeoffs && !IsPdfDimension(measurement);

    private static bool IsPdfDimension(PdfTakeoffAnnotationMeasurement measurement) =>
        string.Equals(measurement.Role, "dimension", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(measurement.SourceSubtype, "/Line", StringComparison.OrdinalIgnoreCase);

    private static string PdfTakeoffImportItemName(string type, string color)
    {
        string label = type switch
        {
            "point" => "Point",
            "area" => "Area",
            _ => "Line",
        };
        return $"{label} {color}";
    }

    private static string RelativeImportSource(string sourceFolder, string pdfPath)
    {
        try
        {
            return Path.GetRelativePath(sourceFolder, pdfPath);
        }
        catch
        {
            return pdfPath;
        }
    }

    private static string WritePdfTakeoffImportReport(
        OurPlanCoreJob targetJob,
        PdfTakeoffImportRunResult run,
        IReadOnlyList<PdfTakeoffImportSource> sources,
        bool portablePaths)
    {
        string reportRoot = Path.Combine(targetJob.RootPath, "import_reports");
        JobWriteAccess.Demand(reportRoot, "write a PDF takeoff import report");
        Directory.CreateDirectory(reportRoot);
        string reportPath = Path.Combine(reportRoot, $"pdf_takeoff_import_{DateTime.Now:yyyyMMdd_HHmmss}.md");
        var lines = new List<string>
        {
            "# PDF Takeoff Import Report",
            "",
            $"Source folder: `{(portablePaths ? Path.GetFileName(Path.TrimEndingDirectorySeparator(run.SourceFolder)) : run.SourceFolder)}`",
            $"Pages folder: `{(portablePaths ? Path.GetRelativePath(targetJob.RootPath, run.PagesFolder) : run.PagesFolder)}`",
            $"Takeoffs folder: `{(portablePaths ? Path.GetRelativePath(targetJob.RootPath, run.TakeoffsFolder) : run.TakeoffsFolder)}`",
            "",
            "## Summary",
            "",
            $"- PDFs scanned: {run.PdfsScanned.ToString(CultureInfo.InvariantCulture)}",
            $"- PDFs with selected annotations: {run.PdfsWithSupportedAnnotations.ToString(CultureInfo.InvariantCulture)}",
            $"- Pages previewed for import: {run.PagesToImport.ToString(CultureInfo.InvariantCulture)}",
            $"- Rulers previewed: {run.RulersToImport.ToString(CultureInfo.InvariantCulture)}",
            $"- Takeoff items previewed: {run.TakeoffItemsToCreate.ToString(CultureInfo.InvariantCulture)}",
            $"- Takeoff measurements previewed: {run.MeasurementsToImport.ToString(CultureInfo.InvariantCulture)}",
            $"- Clean PDF annotations removed: {run.CleanPdfAnnotationsRemoved.ToString(CultureInfo.InvariantCulture)}",
            $"- PDFs imported: {run.PdfsImported.ToString(CultureInfo.InvariantCulture)}",
            $"- Pages imported: {run.PagesImported.ToString(CultureInfo.InvariantCulture)}",
            $"- Rulers imported: {run.RulersImported.ToString(CultureInfo.InvariantCulture)}",
            $"- Takeoff items imported: {run.TakeoffItemsImported.ToString(CultureInfo.InvariantCulture)}",
            $"- Measurements imported: {run.MeasurementsImported.ToString(CultureInfo.InvariantCulture)}",
            "",
            "## Imported PDFs",
            "",
        };

        foreach (PdfTakeoffImportSource source in sources)
        {
            int rulerCount = source.Annotations.Pages.Sum(page => page.Measurements.Count(IsPdfDimension));
            int takeoffCount = source.Annotations.Pages.Sum(page => page.Measurements.Count(measurement => !IsPdfDimension(measurement)));
            lines.Add($"- `{RelativeImportSource(run.SourceFolder, source.PdfPath)}`: {source.Annotations.PageCount} page(s), {rulerCount} ruler annotation(s), {takeoffCount} takeoff annotation(s)");
            foreach (var group in source.Annotations.Pages
                         .SelectMany(page => page.Measurements)
                         .Where(measurement => !IsPdfDimension(measurement))
                         .GroupBy(measurement => new PdfTakeoffImportGroupKey(measurement.Type, measurement.Color))
                         .OrderBy(group => group.Key.Type, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(group => group.Key.Color, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add($"  - {PdfTakeoffImportItemName(group.Key.Type, group.Key.Color)}: {group.Count().ToString(CultureInfo.InvariantCulture)}");
            }
        }

        if (run.Messages.Count > 0)
        {
            lines.Add("");
            lines.Add("## Messages");
            lines.Add("");
            foreach (string message in run.Messages.Take(300))
                lines.Add($"- {message}");
            if (run.Messages.Count > 300)
                lines.Add($"- ... {run.Messages.Count - 300} more messages");
        }

        lines.Add("");
        JobWriteAccess.Demand(reportPath, "write a PDF takeoff import report");
        File.WriteAllText(reportPath, string.Join(Environment.NewLine, lines), Encoding.UTF8);
        return reportPath;
    }

    private void ShowPdfTakeoffImportResult(PdfTakeoffImportRunResult result)
    {
        var summary = new StringBuilder();
        if (!result.HadSupportedAnnotations)
        {
            summary.AppendLine("No supported PDF takeoff/ruler annotations were found.");
            summary.AppendLine();
            summary.AppendLine($"PDFs scanned: {result.PdfsScanned}");
        }
        else if (result.Cancelled)
        {
            summary.AppendLine("Import cancelled before writing job files.");
            summary.AppendLine();
            summary.AppendLine($"PDFs with selected annotations: {result.PdfsWithSupportedAnnotations}");
            summary.AppendLine($"Pages previewed: {result.PagesToImport}");
            summary.AppendLine($"Rulers previewed: {result.RulersToImport}");
            summary.AppendLine($"Takeoff items previewed: {result.TakeoffItemsToCreate}");
            summary.AppendLine($"Takeoff measurements previewed: {result.MeasurementsToImport}");
        }
        else
        {
            summary.AppendLine($"Imported PDFs: {result.PdfsImported}");
            summary.AppendLine($"Pages: {result.PagesImported}");
            summary.AppendLine($"Rulers: {result.RulersImported}");
            summary.AppendLine($"Takeoff items: {result.TakeoffItemsImported}");
            summary.AppendLine($"Measurements: {result.MeasurementsImported}");
            summary.AppendLine($"Clean PDF annotations removed: {result.CleanPdfAnnotationsRemoved}");
            summary.AppendLine();
            summary.AppendLine($"Report: {ProjectDataPathForDisplay(result.ReportPath)}");
        }

        if (result.Messages.Count > 0)
        {
            summary.AppendLine();
            summary.AppendLine("Messages:");
            foreach (string message in result.Messages.Take(8))
                summary.AppendLine("- " + message);
            if (result.Messages.Count > 8)
                summary.AppendLine($"- ... {result.Messages.Count - 8} more");
        }

        string title = result.Cancelled || !result.HadSupportedAnnotations
            ? "PDF Takeoffs Import"
            : "PDF Takeoffs Import Complete";
        MessageBox.Show(summary.ToString(), title,
                        MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void TryDeleteDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn(ex, $"Failed to delete temporary PDF takeoff import directory {path}");
        }
    }
}
