using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

public partial class MainWindow
{
    private const string PdfTakeoffImportFolderName = "from pdf";

    private sealed record PdfTakeoffImportSource(
        string PdfPath,
        PdfTakeoffAnnotationImportResult Annotations);

    private sealed record PdfTakeoffImportMeasurement(
        PageInfo Page,
        int PageIndex,
        PdfTakeoffAnnotationMeasurement Annotation);

    private sealed record PdfTakeoffImportGroupKey(string Type, string Color);

    private sealed class PdfTakeoffImportRunResult
    {
        public string SourceFolder { get; init; } = "";
        public string PagesFolder { get; init; } = "";
        public string TakeoffsFolder { get; init; } = "";
        public string ReportPath { get; set; } = "";
        public int PdfsScanned { get; set; }
        public int PdfsImported { get; set; }
        public int PagesImported { get; set; }
        public int TakeoffItemsImported { get; set; }
        public int MeasurementsImported { get; set; }
        public string FirstImportedPageFolder { get; set; } = "";
        public List<string> Messages { get; } = [];
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
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "Import PDF Takeoffs",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string? folder = SelectFolder("Select folder with PDF takeoff annotations", NewPdfImportInitialFolder());
        if (folder == null)
            return;

        IReadOnlyList<string> pdfPaths = PdfImportSourceFinder.FindPdfFilesRecursive(folder);
        if (pdfPaths.Count == 0)
        {
            MessageBox.Show("No PDF files were found in the selected folder or its subfolders.",
                            "Import PDF Takeoffs",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
            return;
        }

        var importButton = sender as Button;
        if (importButton != null)
            importButton.IsEnabled = false;

        try
        {
            PdfTakeoffImportRunResult result = await ScanAndImportPdfTakeoffsAsync(folder, pdfPaths);
            ReloadPagesTree();
            LoadTakeoffsForJob();

            if (!string.IsNullOrWhiteSpace(result.FirstImportedPageFolder))
                SelectPageByFolder(result.FirstImportedPageFolder);

            TxtStatus.Text =
                $"Imported PDF takeoffs: {result.PdfsImported} PDF(s), {result.PagesImported} page(s), " +
                $"{result.TakeoffItemsImported} takeoff item(s), {result.MeasurementsImported} measurement(s). " +
                $"Report: {result.ReportPath}";
            ShowPdfTakeoffImportResult(result);
        }
        finally
        {
            if (importButton != null)
                importButton.IsEnabled = true;
        }
    }

    private async Task<PdfTakeoffImportRunResult> ScanAndImportPdfTakeoffsAsync(string sourceFolder, IReadOnlyList<string> pdfPaths)
    {
        string pageParent = GetSelectedImportFolder();
        string takeoffParent = CurrentTakeoffParentFolder();
        string pagesFolder = EnsurePdfTakeoffImportBucket(pageParent, _currentJob!.PagesRoot);
        string takeoffsFolder = EnsurePdfTakeoffImportBucket(takeoffParent, _currentJob.TakeoffsRoot);
        var run = new PdfTakeoffImportRunResult
        {
            SourceFolder = sourceFolder,
            PagesFolder = pagesFolder,
            TakeoffsFolder = takeoffsFolder,
            PdfsScanned = pdfPaths.Count,
        };
        var sources = new List<PdfTakeoffImportSource>();

        using (ShowBusyOverlay($"Scanning {pdfPaths.Count} PDF file(s) for takeoff annotations..."))
        {
            await WaitForBusyOverlayRenderAsync();
            for (int index = 0; index < pdfPaths.Count; index++)
            {
                string pdfPath = pdfPaths[index];
                string pdfName = Path.GetFileName(pdfPath);
                BusyOverlayText.Text = $"Scanning PDF takeoffs {index + 1}/{pdfPaths.Count}: {pdfName}";
                TxtStatus.Text = BusyOverlayText.Text;

                var scan = await PdfTakeoffAnnotationImportService.TryReadAsync(pdfPath);
                if (!scan.Ok)
                {
                    run.Messages.Add($"{pdfName}: scan failed - {scan.Error}");
                    continue;
                }

                int measurementCount = scan.Result.Pages.Sum(page => page.Measurements.Count);
                if (measurementCount == 0)
                {
                    run.Messages.Add($"{pdfName}: no supported measurement annotations found.");
                    continue;
                }

                sources.Add(new PdfTakeoffImportSource(pdfPath, scan.Result));
            }

            if (sources.Count == 0)
                throw new InvalidOperationException("No supported PDF takeoff annotations were found.");

            for (int index = 0; index < sources.Count; index++)
            {
                PdfTakeoffImportSource source = sources[index];
                string pdfName = Path.GetFileName(source.PdfPath);
                BusyOverlayText.Text = $"Importing PDF takeoffs {index + 1}/{sources.Count}: {pdfName}";
                TxtStatus.Text = BusyOverlayText.Text;
                ImportPdfTakeoffSource(source, pagesFolder, takeoffsFolder, sourceFolder, run);
            }
        }

        run.ReportPath = WritePdfTakeoffImportReport(run, sources);
        return run;
    }

    private string EnsurePdfTakeoffImportBucket(string parentFolder, string rootFolder)
    {
        string parent = string.IsNullOrWhiteSpace(parentFolder) || !Directory.Exists(parentFolder)
            ? rootFolder
            : parentFolder;
        if (string.Equals(OurPlaneCoreJobStore.DisplayName(parent), PdfTakeoffImportFolderName, StringComparison.OrdinalIgnoreCase))
            return parent;

        return OurPlaneCoreJobStore.EnsureFolder(parent, PdfTakeoffImportFolderName);
    }

    private void ImportPdfTakeoffSource(
        PdfTakeoffImportSource source,
        string pagesFolder,
        string takeoffsFolder,
        string sourceFolder,
        PdfTakeoffImportRunResult run)
    {
        string pdfDisplayName = Path.GetFileNameWithoutExtension(source.PdfPath);
        IReadOnlyList<string> pageNames = DefaultPageNames(source.PdfPath, source.Annotations.PageCount, includePdfName: true);
        IReadOnlyList<PageInfo> importedPages = OurPlaneCoreJobStore.ImportPdf(
            _currentJob!,
            source.PdfPath,
            pageNames,
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

            double pageScale = page.ScaleMPerPt > 0
                ? page.ScaleMPerPt
                : page.Measurements.Select(measurement => measurement.ScaleMPerPt).FirstOrDefault(scale => scale > 0);
            if (pageScale > 0)
            {
                importedPage.ScaleMetersPerPt = pageScale;
                OurPlaneCoreJobStore.SavePageScale(importedPage.FolderPath, pageScale);
            }

            foreach (PdfTakeoffAnnotationMeasurement annotation in page.Measurements)
                importMeasurements.Add(new PdfTakeoffImportMeasurement(importedPage, page.PageIndex, annotation));
        }

        if (importMeasurements.Count == 0)
        {
            run.Messages.Add($"{Path.GetFileName(source.PdfPath)}: pages imported, but no measurements were created after page matching.");
            return;
        }

        string pdfTakeoffFolder = OurPlaneCoreJobStore.EnsureFolder(
            takeoffsFolder,
            string.IsNullOrWhiteSpace(pdfDisplayName) ? "PDF" : pdfDisplayName);
        foreach (var group in importMeasurements.GroupBy(m => new PdfTakeoffImportGroupKey(m.Annotation.Type, m.Annotation.Color)))
        {
            string itemName = PdfTakeoffImportItemName(group.Key.Type, group.Key.Color);
            TakeoffItem item = OurPlaneCoreJobStore.CreateTakeoffItem(
                _currentJob!,
                pdfTakeoffFolder,
                itemName,
                group.Key.Color,
                group.Key.Type);
            item.Notes = $"Imported from PDF takeoff annotations: {RelativeImportSource(sourceFolder, source.PdfPath)}";

            foreach (PdfTakeoffImportMeasurement imported in group)
            {
                item.Measurements.Add(CreatePdfTakeoffMeasurement(
                    imported,
                    item.FolderPath,
                    sourceFolder,
                    source.PdfPath));
            }

            OurPlaneCoreJobStore.SaveTakeoffItem(item);
            run.TakeoffItemsImported++;
            run.MeasurementsImported += item.Measurements.Count;
        }
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

    private string WritePdfTakeoffImportReport(PdfTakeoffImportRunResult run, IReadOnlyList<PdfTakeoffImportSource> sources)
    {
        string reportRoot = Path.Combine(_currentJob!.RootPath, "import_reports");
        Directory.CreateDirectory(reportRoot);
        string reportPath = Path.Combine(reportRoot, $"pdf_takeoff_import_{DateTime.Now:yyyyMMdd_HHmmss}.md");
        var lines = new List<string>
        {
            "# PDF Takeoff Import Report",
            "",
            $"Source folder: `{run.SourceFolder}`",
            $"Pages folder: `{run.PagesFolder}`",
            $"Takeoffs folder: `{run.TakeoffsFolder}`",
            "",
            "## Summary",
            "",
            $"- PDFs scanned: {run.PdfsScanned.ToString(CultureInfo.InvariantCulture)}",
            $"- PDFs imported: {run.PdfsImported.ToString(CultureInfo.InvariantCulture)}",
            $"- Pages imported: {run.PagesImported.ToString(CultureInfo.InvariantCulture)}",
            $"- Takeoff items imported: {run.TakeoffItemsImported.ToString(CultureInfo.InvariantCulture)}",
            $"- Measurements imported: {run.MeasurementsImported.ToString(CultureInfo.InvariantCulture)}",
            "",
            "## Imported PDFs",
            "",
        };

        foreach (PdfTakeoffImportSource source in sources)
        {
            int measurementCount = source.Annotations.Pages.Sum(page => page.Measurements.Count);
            lines.Add($"- `{RelativeImportSource(run.SourceFolder, source.PdfPath)}`: {source.Annotations.PageCount} page(s), {measurementCount} measurement annotation(s)");
            foreach (var group in source.Annotations.Pages
                         .SelectMany(page => page.Measurements)
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
        File.WriteAllText(reportPath, string.Join(Environment.NewLine, lines), Encoding.UTF8);
        return reportPath;
    }

    private void ShowPdfTakeoffImportResult(PdfTakeoffImportRunResult result)
    {
        var summary = new StringBuilder();
        summary.AppendLine($"Imported PDFs: {result.PdfsImported}");
        summary.AppendLine($"Pages: {result.PagesImported}");
        summary.AppendLine($"Takeoff items: {result.TakeoffItemsImported}");
        summary.AppendLine($"Measurements: {result.MeasurementsImported}");
        summary.AppendLine();
        summary.AppendLine($"Report: {result.ReportPath}");
        if (result.Messages.Count > 0)
        {
            summary.AppendLine();
            summary.AppendLine("Messages:");
            foreach (string message in result.Messages.Take(8))
                summary.AppendLine("- " + message);
            if (result.Messages.Count > 8)
                summary.AppendLine($"- ... {result.Messages.Count - 8} more");
        }

        MessageBox.Show(summary.ToString(), "PDF Takeoffs Import Complete",
                        MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
