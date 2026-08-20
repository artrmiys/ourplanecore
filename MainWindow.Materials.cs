using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using OurPlanCore.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    private CancellationTokenSource? _materialsWorkCts;

    private async void BtnMaterialsExtract_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireModule(ModuleId.Materials, "Extract Materials"))
            return;
        await RunAsyncUiHandler(
            ExtractMaterialsAsync,
            "Material extraction failed.",
            "Materials");
    }

    private async void BtnMaterialsCreateReport_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireModule(ModuleId.Materials, "Create Materials Report"))
            return;
        await RunAsyncUiHandler(
            CreateMaterialsReportForCurrentJobAsync,
            "Material report failed.",
            "Materials");
    }

    private void BtnMaterialsRefresh_Click(object sender, RoutedEventArgs e) => RefreshMaterialsManager();

    private void BtnMaterialsOpenJson_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireModule(ModuleId.Materials, "Open Materials JSON"))
            return;
        if (_currentJob == null)
            return;

        OpenJsonFile(MaterialExtractionService.LatestJsonPath(_currentJob), "Materials JSON is missing. Run Extract first.");
    }

    private void BtnMaterialsOpenRowsCsv_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireModule(ModuleId.Materials, "Open Materials CSV"))
            return;
        if (_currentJob == null)
            return;

        OpenFile(MaterialExtractionService.LatestRowsCsvPath(_currentJob), "Materials rows CSV is missing. Run Extract first.");
    }

    private void BtnMaterialsOpenSummaryCsv_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireModule(ModuleId.Materials, "Open Materials Summary"))
            return;
        if (_currentJob == null)
            return;

        OpenFile(MaterialExtractionService.LatestSummaryCsvPath(_currentJob), "Materials summary CSV is missing. Run Extract first.");
    }

    private void BtnMaterialsOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireModule(ModuleId.Materials, "Open Materials Folder"))
            return;
        if (_currentJob == null)
            return;

        OpenFolderInExplorer(MaterialExtractionService.OutputFolder(_currentJob));
    }

    private async Task ExtractMaterialsAsync()
    {
        if (!IsModuleEnabled(ModuleId.Materials))
            return;

        if (_currentJob == null)
        {
            PostStatusInfo("Open or create a job before extracting materials.");
            return;
        }
        if (!EnsureCurrentJobWritable("extract materials for this job"))
            return;

        IReadOnlyList<PageInfo> pages = CollectPagesUnder(_currentJob.PagesRoot).ToList();
        IReadOnlyList<string> pdfs = MaterialExtractionService.UniqueSourcePdfs(pages);
        if (pdfs.Count == 0)
        {
            PostStatusInfo("No source PDF files were found in the current job.");
            return;
        }

        TxtMaterialsSummary.Text = $"Extracting materials from {pdfs.Count} source PDF(s)...";
        TxtStatus.Text = "Materials: extraction running.";
        BtnMaterialsExtract.IsEnabled = false;
        OurPlanCoreJob job = _currentJob;
        using CancellationTokenSource cts = StartMaterialsWork();
        try
        {
            MaterialExtractionRunResult run = await MaterialExtractionService.ExtractAsync(job, pdfs, cts.Token);
            if (!IsModuleEnabled(ModuleId.Materials) ||
                !EnsureExpectedJobWritable(job, "apply material extraction results"))
                return;
            ApplyMaterialsResult(run.Result);
            TxtStatus.Text = $"Materials: {run.Result.Rows.Count} row(s), {run.Result.Schedules.Count} schedule(s).";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            TxtStatus.Text = "Materials: extraction cancelled.";
        }
        finally
        {
            FinishMaterialsWork(cts);
            BtnMaterialsExtract.IsEnabled = true;
        }
    }

    private async Task CreateMaterialsReportForCurrentJobAsync()
    {
        if (!IsModuleEnabled(ModuleId.Materials))
            return;

        if (_currentJob == null)
        {
            PostStatusInfo("Open or create a job before creating a materials report.");
            return;
        }
        if (!EnsureCurrentJobWritable("create a materials report"))
            return;

        IReadOnlyList<PageInfo> pages = CollectPagesUnder(_currentJob.PagesRoot).ToList();
        if (MaterialExtractionService.UniqueSourcePdfs(pages).Count == 0)
        {
            PostStatusInfo("No source PDF files were found in the current job.");
            return;
        }

        await CreateMaterialsReportSheetAsync(pages, "current job");
    }

    private async Task CreateMaterialsReportSheetAsync(
        IReadOnlyList<PageInfo> sourcePages,
        string sourceLabel,
        bool applySheetMetadata = true)
    {
        if (!IsModuleEnabled(ModuleId.Materials) || _currentJob == null || sourcePages.Count == 0)
            return;
        if (!EnsureCurrentJobWritable("create a materials report"))
            return;

        OurPlanCoreJob job = _currentJob;
        using CancellationTokenSource cts = StartMaterialsWork();
        try
        {
            sourcePages = sourcePages
                .Where(page => !MaterialExtractionService.IsGeneratedMaterialsReportPage(page))
                .GroupBy(page => NormalizePath(page.FolderPath), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            if (sourcePages.Count == 0)
                return;

            MaterialReportSheetMetadataSummary metadataSummary = applySheetMetadata
                ? await AnalyzeAndApplySheetMetadataAsync(sourcePages, "Materials")
                : new MaterialReportSheetMetadataSummary(0, 0, 0, 0);
            cts.Token.ThrowIfCancellationRequested();
            if (!EnsureExpectedJobWritable(job, "create a materials report"))
                return;

            string destinationFolder = OurPlanCoreJobStore.EnsureFolder(job.PagesRoot, "00. reports");
            using (ShowBusyOverlay("Extracting materials and creating report sheet..."))
            {
                await WaitForBusyOverlayRenderAsync();
                if (!EnsureExpectedJobWritable(job, "create a materials report"))
                    return;
                BusyOverlayText.Text = "Materials: reading source PDFs, schedules, and OCR pages...";
                TxtStatus.Text = "Materials: creating report sheet.";
                MaterialReportPageResult result = await MaterialReportPageService.CreateReportPagesAsync(
                    job,
                    sourcePages,
                    destinationFolder,
                    cts.Token);
                cts.Token.ThrowIfCancellationRequested();

                ReloadPagesTree();
                if (result.ReportPages.Count > 0)
                    SelectPageByFolder(result.ReportPages[0].FolderPath);

                TxtStatus.Text =
                    $"Materials report created for {sourceLabel}: " +
                    $"{result.Extraction.Rows.Count} row(s), {result.Extraction.Schedules.Count} schedule(s), " +
                    $"metadata {metadataSummary.Renamed} renamed/{metadataSummary.Scaled} scaled/" +
                    $"{metadataSummary.Failed} failed.";
            }

            RefreshMaterialsManager();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            TxtStatus.Text = "Materials: report creation cancelled.";
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Materials report for {sourceLabel} failed");
            TxtStatus.Text = $"Materials report failed for {sourceLabel}: {ex.Message}";
        }
        finally
        {
            FinishMaterialsWork(cts);
        }
    }

    private CancellationTokenSource StartMaterialsWork()
    {
        _materialsWorkCts?.Cancel();
        var cts = new CancellationTokenSource();
        _materialsWorkCts = cts;
        return cts;
    }

    private void FinishMaterialsWork(CancellationTokenSource cts)
    {
        if (ReferenceEquals(_materialsWorkCts, cts))
            _materialsWorkCts = null;
    }

    private void CancelActiveMaterialsWorkForModuleDisable() =>
        _materialsWorkCts?.Cancel();

    private async Task<MaterialReportSheetMetadataSummary> TryApplySheetMetadataAfterPdfImportAsync(
        IReadOnlyList<PageInfo> sourcePages)
    {
        if (_currentJob == null || sourcePages.Count == 0)
            return new MaterialReportSheetMetadataSummary(0, 0, 0, 0);

        SheetMetadataImportPolicy policy = SheetMetadataRulesService.Active.ImportPolicy;
        if (policy == SheetMetadataImportPolicy.ManualOnly)
        {
            const string status =
                "PDF import complete. Automatic metadata analysis is off; use Name, Scale, or Name+Scale when needed.";
            AppLog.Info(
                $"PDF import sheet metadata skipped by ManualOnly policy; sheets={sourcePages.Count}.");
            TxtStatus.Text = status;
            return new MaterialReportSheetMetadataSummary(0, 0, 0, 0);
        }

        try
        {
            MaterialReportSheetMetadataSummary summary = await AnalyzeAndApplySheetMetadataAsync(
                sourcePages,
                "PDF import");
            TxtStatus.Text =
                $"PDF import complete. Metadata: {summary.Renamed} renamed/" +
                $"{summary.Scaled} scaled/{summary.Failed} failed.";
            return summary;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, "PDF import sheet metadata failed");
            TxtStatus.Text = $"PDF import complete. Sheet metadata skipped: {ex.Message}";
            return new MaterialReportSheetMetadataSummary(sourcePages.Count, 0, 0, sourcePages.Count);
        }
    }

    private async Task<MaterialReportSheetMetadataSummary> AnalyzeAndApplySheetMetadataAsync(
        IReadOnlyList<PageInfo> sourcePages,
        string statusPrefix)
    {
        if (_currentJob == null || sourcePages.Count == 0)
            return new MaterialReportSheetMetadataSummary(0, 0, 0, 0);

        SaveCurrentPageScale();
        OurPlanCoreJob job = _currentJob;
        if (!EnsureExpectedJobWritable(job, "analyze and apply sheet metadata"))
            return new MaterialReportSheetMetadataSummary(0, 0, 0, sourcePages.Count);
        await OfferPdfMetadataGuidanceIfNeededAsync(job, sourcePages, statusPrefix);
        if (!EnsureExpectedJobWritable(job, "analyze and apply sheet metadata"))
            return new MaterialReportSheetMetadataSummary(0, 0, 0, sourcePages.Count);

        SheetMetadataImportPolicy policy = SheetMetadataRulesService.Active.ImportPolicy;
        bool persistDuringAnalysis = policy == SheetMetadataImportPolicy.LegacyAutoApply;
        TxtStatus.Text = $"{statusPrefix}: auto naming/scaling source sheets.";

        List<PdfMetadataPageResult> results;
        using (ShowBusyOverlay($"{statusPrefix}: analyzing {sourcePages.Count} sheet(s)..."))
        {
            await WaitForBusyOverlayRenderAsync();
            if (!EnsureExpectedJobWritable(job, "analyze and apply sheet metadata"))
                return new MaterialReportSheetMetadataSummary(0, 0, 0, sourcePages.Count);
            results = await Task.Run(() => AnalyzePdfPages(job, sourcePages, persistDuringAnalysis));
        }
        if (!EnsureExpectedJobWritable(job, "apply sheet metadata results"))
            return new MaterialReportSheetMetadataSummary(results.Count, 0, 0, results.Count);

        if (policy == SheetMetadataImportPolicy.AnalyzeOnly)
        {
            foreach (PdfMetadataPageResult result in results.Where(result => result.Ok && result.Metadata != null))
                OurPlanCoreJobStore.WriteSourcePdfMetadata(result.Page.FolderPath, result.Metadata!);

            return new MaterialReportSheetMetadataSummary(
                results.Count,
                0,
                0,
                results.Count(result => !result.Ok));
        }

        var rows = BuildPdfMetadataPreviewRows(
                results,
                defaultRename: true,
                defaultScale: true,
                highConfidenceOnly: policy == SheetMetadataImportPolicy.AutoApplyHighConfidence)
            .ToList();
        if (rows.Count == 0)
            return new MaterialReportSheetMetadataSummary(
                results.Count,
                0,
                0,
                results.Count(result => !result.Ok));

        bool reviewedByUser = policy is
            SheetMetadataImportPolicy.Preview or
            SheetMetadataImportPolicy.ManualOnly;
        if (reviewedByUser)
        {
            var dialog = new PdfMetadataPreviewDialog(rows, $"{statusPrefix}: Name + Scale Review")
            {
                Owner = this,
            };
            if (dialog.ShowDialog() != true)
            {
                TxtStatus.Text = $"{statusPrefix}: metadata review cancelled; sheets were not changed.";
                return new MaterialReportSheetMetadataSummary(
                    results.Count,
                    0,
                    0,
                    results.Count(result => !result.Ok));
            }
            if (!EnsureExpectedJobWritable(job, "apply reviewed sheet metadata", showDialog: true))
                return new MaterialReportSheetMetadataSummary(results.Count, 0, 0, results.Count);

            rows = dialog.Rows.ToList();
        }

        PdfMetadataApplySummary applied = ApplyPdfMetadataResults(
            job,
            results,
            rows,
            reviewedByUser);
        return new MaterialReportSheetMetadataSummary(
            results.Count,
            applied.Renamed,
            applied.Scaled,
            applied.Failed + results.Count(result => !result.Ok));
    }

    private void RefreshMaterialsManager()
    {
        if (!IsModuleEnabled(ModuleId.Materials))
            return;

        if (_currentJob == null)
        {
            MaterialsGrid.ItemsSource = Array.Empty<MaterialManagerRow>();
            TxtMaterialsSummary.Text = "No job open.";
            return;
        }

        MaterialExtractionResult? result = MaterialExtractionService.TryLoadLatest(_currentJob);
        if (result == null)
        {
            MaterialsGrid.ItemsSource = Array.Empty<MaterialManagerRow>();
            TxtMaterialsSummary.Text = "No material extraction output yet.";
            return;
        }

        ApplyMaterialsResult(result);
    }

    private void ApplyMaterialsResult(MaterialExtractionResult result)
    {
        List<MaterialManagerRow> rows = result.Rows
            .OrderByDescending(row => row.Confidence)
            .ThenBy(row => row.Category)
            .ThenBy(row => row.MaterialFamily)
            .ThenBy(row => row.PdfFile)
            .ThenBy(row => row.PdfPage)
            .Select(MaterialManagerRow.From)
            .ToList();
        MaterialsGrid.ItemsSource = rows;
        TxtMaterialsSummary.Text = BuildMaterialsSummaryText(result);
    }

    private static string BuildMaterialsSummaryText(MaterialExtractionResult result)
    {
        MaterialQualitySummary quality = result.Quality;
        int pages = result.Stats.PagesRead;
        int rows = result.Rows.Count;
        int schedules = result.Schedules.Count;
        int reviewRows = quality.ReviewRows > 0
            ? quality.ReviewRows
            : result.Rows.Count(row => row.ReviewFlags.Count > 0);
        int highConfidence = quality.HighConfidenceRows > 0
            ? quality.HighConfidenceRows
            : result.Rows.Count(row => row.Confidence >= 0.80);
        int takeoffReady = quality.TakeoffReadyRows > 0
            ? quality.TakeoffReadyRows
            : result.Rows.Count(row => row.Confidence >= 0.80 && !row.ReviewFlags.Contains("needs_quantity_or_size_review"));
        string tableStatus = quality.PdfPlumberAvailable ? "tables on" : "tables fallback";
        string ocrStatus = quality.OcrAvailable ? "ocr on" : "ocr off";
        return string.Format(
            CultureInfo.InvariantCulture,
            "Rows {0} | Ready {1} | High {2} | Review {3} | Schedules {4} | Pages {5} | Blank/OCR {6} | {7} | {8}",
            rows,
            takeoffReady,
            highConfidence,
            reviewRows,
            schedules,
            pages,
            quality.BlankPagesWithoutOcr,
            tableStatus,
            ocrStatus);
    }

    private void OpenFile(string path, string missingStatus)
    {
        if (!File.Exists(path))
        {
            TxtStatus.Text = missingStatus;
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }

    private sealed class MaterialManagerRow
    {
        public string Category { get; init; } = "";
        public string Family { get; init; } = "";
        public string Item { get; init; } = "";
        public string Size { get; init; } = "";
        public string Qty { get; init; } = "";
        public string Unit { get; init; } = "";
        public string Sheet { get; init; } = "";
        public string Page { get; init; } = "";
        public string Schedule { get; init; } = "";
        public string Confidence { get; init; } = "";
        public string Flags { get; init; } = "";
        public string Pdf { get; init; } = "";
        public string Source { get; init; } = "";
        public string RawText { get; init; } = "";

        public static MaterialManagerRow From(MaterialExtractionRow row) => new()
        {
            Category = row.Category,
            Family = row.MaterialFamily ?? "",
            Item = row.Item ?? "",
            Size = row.Size ?? "",
            Qty = row.Qty ?? "",
            Unit = row.Unit ?? "",
            Sheet = row.Sheet ?? "",
            Page = row.PdfPage.ToString(CultureInfo.InvariantCulture),
            Schedule = row.ScheduleRef ?? "",
            Confidence = row.Confidence.ToString("0.00", CultureInfo.InvariantCulture),
            Flags = string.Join("; ", row.ReviewFlags),
            Pdf = row.PdfFile,
            Source = row.SourceType,
            RawText = row.RawText,
        };
    }
}
