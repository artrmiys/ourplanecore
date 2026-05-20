using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OurPlaneCore.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private async void BtnExportPdf_Click(object sender, RoutedEventArgs e)
    {
        await RunAsyncUiHandler(
            () => ExportPdfAsync(sender),
            "PDF export failed.",
            "Export PDF");
    }

    private async Task ExportPdfAsync(object sender)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open or create a job before PDF export.";
            return;
        }

        var allPages = CollectPagesUnder(_currentJob.PagesRoot).ToList();
        if (allPages.Count == 0)
        {
            TxtStatus.Text = "No PDF sheets to export.";
            return;
        }

        AppSettingsStore.NormalizeOutputSettings(_settings);
        ISet<string> initialSelection;
        using (UsePageMeasurementLookup())
            initialSelection = InitialPdfExportSelection(allPages);
        bool includeMeasurements = DefaultPdfExportIncludeMeasurements(allPages, initialSelection);

        var dialog = new PdfExportDialog(
            allPages,
            initialSelection,
            includeMeasurements: includeMeasurements,
            includeAnnotations: _settings.PdfExportIncludeAnnotations,
            includeLegend: _settings.PdfExportShowSheetLegend,
            measurementStrokeScale: _settings.PdfExportMeasurementStrokeScale)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;
        PersistPdfExportDialogOptions(dialog);

        var pages = SelectedPdfExportPages(allPages, dialog);
        if (pages.Count == 0)
        {
            TxtStatus.Text = "No sheets selected for PDF export.";
            return;
        }

        var save = new SaveFileDialog
        {
            Title = "Export PDF",
            Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
            FileName = $"{SafeFileName(_currentJob.Name)}_sheets.pdf",
            InitialDirectory = _currentJob.RootPath,
            AddExtension = true,
            DefaultExt = ".pdf",
        };
        if (save.ShowDialog(this) != true)
            return;

        Button? button = sender as Button;
        try
        {
            if (button != null) button.IsEnabled = false;
            var options = new PdfExportOptions(
                dialog.IncludeMeasurements,
                dialog.IncludeAnnotations,
                dialog.IncludeLegend,
                _viewport.UnitMode,
                _settings.SheetLegendAnchor,
                _settings.PdfExportSheetLegendScale,
                _settings.PdfExportSheetHeaderScale,
                _settings.PdfExportShowMeasurementLabels,
                _settings.PdfExportShowLineLabels,
                _settings.PdfExportShowAreaLabels,
                _settings.PdfExportShowCountLabels,
                dialog.MeasurementStrokeScale,
                _settings.PdfExportPointSizeScale,
                _settings.PdfExportMeasurementLabelScale,
                _settings.PdfExportShowJoistLabels,
                _settings.PdfExportAreaEdgeScale,
                _settings.PdfExportAreaFillOpacity);
            string outputPath = save.FileName;
            (bool ok, string error) exportResult;
            using (ShowBusyOverlay($"Exporting {pages.Count} sheet(s) to PDF..."))
            {
                await WaitForBusyOverlayRenderAsync();
                SaveCurrentPageScale();
                SaveCurrentPageAnnotations();
                TxtStatus.Text = $"Exporting {pages.Count} sheet(s) to PDF with white paper...";

                IReadOnlyList<PdfExportPageInput> exportPages;
                using (UsePageMeasurementLookup())
                    exportPages = BuildPdfExportPages(pages);
                exportResult = await Task.Run(() =>
                    PdfExporter.TryExport(exportPages, outputPath, options, DrawPdfExportSheetOverlay));
            }

            (bool ok, string error) = exportResult;
            if (!ok)
            {
                MessageBox.Show(error, "Export PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtStatus.Text = "PDF export failed.";
                return;
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                MessageBox.Show(
                    $"PDF exported, but some sheets had warnings:\n{error}",
                    "Export PDF",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                TxtStatus.Text = $"Exported PDF ({pages.Count} sheet(s)); warning -> {outputPath}";
                return;
            }

            TxtStatus.Text = $"Exported PDF ({pages.Count} sheet(s)) -> {outputPath}";
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "PDF export failed.");
            MessageBox.Show($"PDF export failed:\n{ex.Message}", "Export PDF",
                            MessageBoxButton.OK, MessageBoxImage.Error);
            TxtStatus.Text = "PDF export failed.";
        }
        finally
        {
            if (button != null) button.IsEnabled = true;
        }
    }

    private ISet<string> InitialPdfExportSelection(IReadOnlyList<PageInfo> allPages)
    {
        var selected = allPages
            .Where(PageHasVisibleExportMeasurements)
            .Select(page => page.FolderPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (selected.Count > 0)
            return selected;

        if (PagesTree.SelectedItem is TreeViewItem selectedItem)
        {
            foreach (PageInfo page in GetPagesForMetadata(selectedItem))
                selected.Add(page.FolderPath);
        }

        if (selected.Count == 0 && _currentPage != null)
            selected.Add(_currentPage.FolderPath);

        selected.RemoveWhere(path => allPages.All(page => !IsSamePageFolder(path, page.FolderPath)));
        if (selected.Count == 0)
        {
            foreach (PageInfo page in allPages)
                selected.Add(page.FolderPath);
        }

        return selected;
    }

    private bool PageHasVisibleExportMeasurements(PageInfo page) =>
        VisibleOrderedTakeoffsForPage(page)
            .Any(item => MeasurementsForTakeoffOnPage(item, page.FolderPath).Any());

    private bool DefaultPdfExportIncludeMeasurements(
        IReadOnlyList<PageInfo> allPages,
        ISet<string> selectedFolders)
    {
        if (_settings.PdfExportIncludeMeasurements)
            return true;

        var selected = selectedFolders
            .Select(NormalizePathForCompare)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Count == 0)
            return false;

        return allPages.Any(page =>
            selected.Contains(NormalizePathForCompare(page.FolderPath)) &&
            PageHasVisibleExportMeasurements(page));
    }

    private void PersistPdfExportDialogOptions(PdfExportDialog dialog)
    {
        _settings.PdfExportIncludeMeasurements = dialog.IncludeMeasurements;
        _settings.PdfExportIncludeAnnotations = dialog.IncludeAnnotations;
        _settings.PdfExportShowSheetLegend = dialog.IncludeLegend;
        _settings.PdfExportMeasurementStrokeScale = dialog.MeasurementStrokeScale;
        SaveAppSettings();
        SyncOutputSettingsControls();
    }

    private static IReadOnlyList<PageInfo> SelectedPdfExportPages(
        IReadOnlyList<PageInfo> allPages,
        PdfExportDialog dialog)
    {
        var selectedFolders = dialog.Rows
            .Where(row => row.IsSelected)
            .Select(row => NormalizePathForCompare(row.PageFolder))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return allPages
            .Where(page => selectedFolders.Contains(NormalizePathForCompare(page.FolderPath)))
            .ToList();
    }

    private IReadOnlyList<PdfExportPageInput> BuildPdfExportPages(IReadOnlyList<PageInfo> pages) =>
        pages
            .Select(page => new PdfExportPageInput(
                page,
                VisibleOrderedTakeoffsForPage(page)
                    .Select(item => new PdfExportTakeoffInput(
                        item,
                        MeasurementsForTakeoffOnPage(item, page.FolderPath).ToList()))
                    .ToList(),
                OurPlaneCoreJobStore.LoadPageAnnotations(page.FolderPath),
                LayerOrderedTakeoffsForPage(page)
                    .Where(item => IsPageTakeoffVisible(page, item))
                    .Select(item => new PdfExportTakeoffInput(
                        item,
                        MeasurementsForTakeoffOnPage(item, page.FolderPath).ToList()))
                    .ToList()))
            .ToList();
}
