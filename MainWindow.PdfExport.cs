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

        var dialog = new PdfExportDialog(
            allPages,
            InitialPdfExportSelection(allPages),
            includeMeasurements: true,
            includeLegend: _settings.ShowSheetLegend)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

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
            SaveCurrentPageScale();
            SaveCurrentPageAnnotations();
            TxtStatus.Text = $"Exporting {pages.Count} sheet(s) to PDF with white paper...";

            var options = new PdfExportOptions(
                dialog.IncludeMeasurements,
                dialog.IncludeAnnotations,
                dialog.IncludeLegend,
                _viewport.UnitMode,
                _settings.SheetLegendAnchor,
                _settings.SheetLegendScale);
            string outputPath = save.FileName;
            var exportPages = BuildPdfExportPages(pages);
            (bool ok, string error) = await Task.Run(() =>
                PdfExporter.TryExport(exportPages, outputPath, options, DrawPdfExportSheetOverlay));
            if (!ok)
            {
                MessageBox.Show(error, "Export PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtStatus.Text = "PDF export failed.";
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
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (PagesTree.SelectedItem is TreeViewItem selectedItem)
        {
            foreach (PageInfo page in GetPagesForMetadata(selectedItem))
                selected.Add(page.FolderPath);
        }

        if (selected.Count == 0 && _currentPage != null)
            selected.Add(_currentPage.FolderPath);

        selected.RemoveWhere(path => allPages.All(page => !IsSamePageFolder(path, page.FolderPath)));
        return selected;
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
                OurPlaneCoreJobStore.LoadPageAnnotations(page.FolderPath)))
            .ToList();
}
