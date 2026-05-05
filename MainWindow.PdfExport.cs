using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SmartTakeoffs.Controls;
using SkiaSharp;

namespace SmartTakeoffs;

public partial class MainWindow
{
    private async void BtnExportPdf_Click(object sender, RoutedEventArgs e)
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

        var initiallySelected = InitialPdfExportSelection(allPages);
        var dialog = new PdfExportDialog(
            allPages,
            initiallySelected,
            includeMeasurements: true,
            includeLegend: _settings.ShowSheetLegend)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        var selectedFolders = dialog.Rows
            .Where(row => row.IsSelected)
            .Select(row => NormalizePathForCompare(row.PageFolder))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pages = allPages
            .Where(page => selectedFolders.Contains(NormalizePathForCompare(page.FolderPath)))
            .ToList();
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
            TxtStatus.Text = $"Exporting {pages.Count} sheet(s) to PDF...";
            var options = new PdfSheetExportOptions(dialog.IncludeMeasurements, dialog.IncludeAnnotations, dialog.IncludeLegend, _viewport.UnitMode, _settings.SheetLegendAnchor);
            string outputPath = save.FileName;
            (bool ok, string error) = await Task.Run(() => TryExportPdfSheets(pages, outputPath, options));
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

    private sealed record PdfSheetExportOptions(
        bool IncludeMeasurements,
        bool IncludeAnnotations,
        bool IncludeLegend,
        UnitMode UnitMode,
        string LegendAnchor);

    private (bool Ok, string Error) TryExportPdfSheets(
        IReadOnlyList<PageInfo> pages,
        string outputPath,
        PdfSheetExportOptions options)
    {
        try
        {
            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            using var stream = File.Create(outputPath);
            using var document = SKDocument.CreatePdf(stream);
            if (document == null)
                return (false, "Could not create PDF document.");

            foreach (PageInfo page in pages)
            {
                if (!File.Exists(page.PdfPath))
                    return (false, $"Source PDF not found for sheet '{page.Name}': {page.PdfPath}");

                var layerStates = page.PdfLayers
                    .GroupBy(layer => layer.Number)
                    .ToDictionary(group => group.Key, group => group.First().IsOn);
                if (!PdfLayerRenderService.TryRender(
                        page.PdfPath,
                        page.PdfPage,
                        renderScale: 2.0,
                        layerStates,
                        highlightedLayers: [],
                        page.PdfLayersCached ? page.PdfLayers : null,
                        out PdfLayerRenderResult render,
                        out string renderError))
                {
                    return (false, $"Could not render sheet '{page.Name}': {renderError}");
                }

                using SKBitmap? bitmap = SKBitmap.Decode(render.ImageBytes);
                if (bitmap == null)
                    return (false, $"Could not decode rendered sheet '{page.Name}'.");

                SKCanvas canvas = document.BeginPage(render.WidthPt, render.HeightPt);
                canvas.Clear(SKColors.White);
                canvas.DrawBitmap(bitmap, new SKRect(0, 0, render.WidthPt, render.HeightPt));

                var pageItems = OrderedTakeoffsForPage(page).ToList();
                if (options.IncludeMeasurements)
                    DrawPdfExportMeasurements(canvas, pageItems, page, options.UnitMode);
                if (options.IncludeAnnotations)
                    DrawPdfExportAnnotations(canvas, SmartTakeoffsJobStore.LoadPageAnnotations(page.FolderPath), page.ScaleMetersPerPt, options.UnitMode);
                if (options.IncludeLegend)
                    DrawPdfExportLegend(canvas, render.WidthPt, render.HeightPt, pageItems, page, options);

                document.EndPage();
            }

            document.Close();
            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
