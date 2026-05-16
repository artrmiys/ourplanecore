using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace OurPlaneCore;

public partial class MainWindow
{
    private async void BtnMaterialsExtract_Click(object sender, RoutedEventArgs e)
    {
        await RunAsyncUiHandler(
            ExtractMaterialsAsync,
            "Material extraction failed.",
            "Materials");
    }

    private async void BtnMaterialsCreateReport_Click(object sender, RoutedEventArgs e)
    {
        await RunAsyncUiHandler(
            CreateMaterialsReportForCurrentJobAsync,
            "Material report failed.",
            "Materials");
    }

    private void BtnMaterialsRefresh_Click(object sender, RoutedEventArgs e) => RefreshMaterialsManager();

    private void BtnMaterialsOpenJson_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
            return;

        OpenJsonFile(MaterialExtractionService.LatestJsonPath(_currentJob), "Materials JSON is missing. Run Extract first.");
    }

    private void BtnMaterialsOpenRowsCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
            return;

        OpenFile(MaterialExtractionService.LatestRowsCsvPath(_currentJob), "Materials rows CSV is missing. Run Extract first.");
    }

    private void BtnMaterialsOpenSummaryCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
            return;

        OpenFile(MaterialExtractionService.LatestSummaryCsvPath(_currentJob), "Materials summary CSV is missing. Run Extract first.");
    }

    private void BtnMaterialsOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
            return;

        OpenFolderInExplorer(MaterialExtractionService.OutputFolder(_currentJob));
    }

    private async Task ExtractMaterialsAsync()
    {
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "Materials", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IReadOnlyList<PageInfo> pages = CollectPagesUnder(_currentJob.PagesRoot).ToList();
        IReadOnlyList<string> pdfs = MaterialExtractionService.UniqueSourcePdfs(pages);
        if (pdfs.Count == 0)
        {
            MessageBox.Show("No source PDF files were found in the current job.", "Materials", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        TxtMaterialsSummary.Text = $"Extracting materials from {pdfs.Count} source PDF(s)...";
        TxtStatus.Text = "Materials: extraction running.";
        BtnMaterialsExtract.IsEnabled = false;
        try
        {
            MaterialExtractionRunResult run = await MaterialExtractionService.ExtractAsync(_currentJob, pdfs);
            ApplyMaterialsResult(run.Result);
            TxtStatus.Text = $"Materials: {run.Result.Rows.Count} row(s), {run.Result.Schedules.Count} schedule(s).";
        }
        finally
        {
            BtnMaterialsExtract.IsEnabled = true;
        }
    }

    private async Task CreateMaterialsReportForCurrentJobAsync()
    {
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "Materials", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IReadOnlyList<PageInfo> pages = CollectPagesUnder(_currentJob.PagesRoot).ToList();
        if (MaterialExtractionService.UniqueSourcePdfs(pages).Count == 0)
        {
            MessageBox.Show("No source PDF files were found in the current job.", "Materials", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await CreateMaterialsReportSheetAsync(pages, "current job");
    }

    private async Task CreateMaterialsReportSheetAsync(
        IReadOnlyList<PageInfo> sourcePages,
        string sourceLabel,
        bool applySheetMetadata = true)
    {
        if (_currentJob == null || sourcePages.Count == 0)
            return;

        try
        {
            sourcePages = sourcePages
                .Where(page => !MaterialExtractionService.IsGeneratedMaterialsReportPage(page))
                .GroupBy(page => NormalizePath(page.FolderPath), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            if (sourcePages.Count == 0)
                return;

            string destinationFolder = OurPlaneCoreJobStore.EnsureFolder(_currentJob.PagesRoot, "00. reports");
            using (ShowBusyOverlay("Extracting materials and creating report sheet..."))
            {
                await WaitForBusyOverlayRenderAsync();
                MaterialReportSheetMetadataSummary metadataSummary = applySheetMetadata
                    ? await AnalyzeAndApplySheetMetadataAsync(sourcePages, "Materials")
                    : new MaterialReportSheetMetadataSummary(0, 0, 0, 0);

                BusyOverlayText.Text = "Materials: reading source PDFs, schedules, and OCR pages...";
                TxtStatus.Text = "Materials: creating report sheet.";
                MaterialReportPageResult result = await MaterialReportPageService.CreateReportPagesAsync(
                    _currentJob,
                    sourcePages,
                    destinationFolder);

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
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Materials report for {sourceLabel} failed");
            TxtStatus.Text = $"Materials report failed for {sourceLabel}: {ex.Message}";
        }
    }

    private async Task<MaterialReportSheetMetadataSummary> TryApplySheetMetadataAfterPdfImportAsync(
        IReadOnlyList<PageInfo> sourcePages)
    {
        if (_currentJob == null || sourcePages.Count == 0)
            return new MaterialReportSheetMetadataSummary(0, 0, 0, 0);

        try
        {
            using (ShowBusyOverlay("Auto naming/scaling imported sheets..."))
            {
                await WaitForBusyOverlayRenderAsync();
                MaterialReportSheetMetadataSummary summary = await AnalyzeAndApplySheetMetadataAsync(sourcePages, "PDF import");
                TxtStatus.Text =
                    $"PDF import complete. Metadata: {summary.Renamed} renamed/" +
                    $"{summary.Scaled} scaled/{summary.Failed} failed.";
                return summary;
            }
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
        OurPlaneCoreJob job = _currentJob;
        BusyOverlayText.Text = $"{statusPrefix}: auto naming/scaling {sourcePages.Count} sheet(s)...";
        TxtStatus.Text = $"{statusPrefix}: auto naming/scaling source sheets.";

        List<PdfMetadataPageResult> results = await Task.Run(() =>
        {
            var analyzed = new List<PdfMetadataPageResult>();
            foreach (PageInfo page in sourcePages)
            {
                if (PdfSheetMetadataService.TryAnalyzeAndSave(job, page, out PdfSheetMetadata metadata, out string error))
                    analyzed.Add(new PdfMetadataPageResult(page, true, metadata, ""));
                else
                    analyzed.Add(new PdfMetadataPageResult(page, false, null, error));
            }

            return analyzed;
        });

        var rows = BuildPdfMetadataPreviewRows(results, defaultRename: true, defaultScale: true).ToList();
        if (rows.Count == 0)
            return new MaterialReportSheetMetadataSummary(
                results.Count,
                0,
                0,
                results.Count(result => !result.Ok));

        PdfMetadataApplySummary applied = ApplyPdfMetadataResults(job, results, rows);
        return new MaterialReportSheetMetadataSummary(
            results.Count,
            applied.Renamed,
            applied.Scaled,
            applied.Failed + results.Count(result => !result.Ok));
    }

    private void RefreshMaterialsManager()
    {
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
