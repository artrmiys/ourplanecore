using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OurPlaneCore.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private sealed record PdfImportPlan(
        string PdfPath,
        int PageCount,
        IReadOnlyList<string> DefaultPageNames)
    {
        public IReadOnlyList<string> PageNames { get; set; } = DefaultPageNames;
    }

    private async void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        await RunAsyncUiHandler(
            () => ImportPdfAsync(sender),
            "Import PDF failed.",
            "Import PDF");
    }

    private async Task ImportPdfAsync(object sender)
    {
        if (_currentJob == null)
        {
            MessageBox.Show("Open or create a job first.", "Import PDF",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new OpenFileDialog
        {
            Title = "Import PDF(s)",
            Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog() != true) return;

        IReadOnlyList<string> pdfPaths = SelectedPdfPaths(dlg);
        if (pdfPaths.Count == 0)
            return;

        IReadOnlyList<PdfImportPlan> plans = BuildPdfImportPlans(pdfPaths, out IReadOnlyList<string> skipped);
        if (plans.Count == 0)
        {
            MessageBox.Show("Could not read any pages from the selected PDF file(s).", "Import PDF",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!ConfirmPdfImportPageNames(plans, skipped))
            return;

        await ImportPdfPlansAsync(plans, skipped, sender);
    }

    private IReadOnlyList<PdfImportPlan> BuildPdfImportPlans(
        IReadOnlyList<string> pdfPaths,
        out IReadOnlyList<string> skipped)
    {
        bool multiPdf = pdfPaths.Count > 1;
        var plans = new List<PdfImportPlan>();
        var skippedList = new List<string>();

        foreach (string pdfPath in pdfPaths)
        {
            int pageCount = _viewport.GetPageCount(pdfPath);
            if (pageCount <= 0)
            {
                skippedList.Add(Path.GetFileName(pdfPath));
                continue;
            }

            plans.Add(new PdfImportPlan(
                pdfPath,
                pageCount,
                DefaultPageNames(pdfPath, pageCount, multiPdf)));
        }

        skipped = skippedList;
        return plans;
    }

    private bool ConfirmPdfImportPageNames(
        IReadOnlyList<PdfImportPlan> plans,
        IReadOnlyList<string> skipped)
    {
        int totalPages = plans.Sum(plan => plan.PageCount);
        string defaultNames = string.Join(Environment.NewLine, plans.SelectMany(plan => plan.DefaultPageNames));
        string prompt = plans.Count == 1
            ? $"PDF has {totalPages} page(s). Edit page names, one per line:"
            : $"Selected {plans.Count} PDF file(s), {totalPages} total page(s). Edit page names, one per line:";

        if (skipped.Count > 0)
            prompt += $"{Environment.NewLine}{Environment.NewLine}Skipped unreadable PDF(s): {string.Join(", ", skipped)}";

        string? rawNames = ShowMultilineInputDialog(prompt, defaultNames, "Page Names");
        if (rawNames == null)
            return false;

        string[] names = rawNames
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToArray();

        if (names.Length != totalPages)
        {
            MessageBox.Show($"Expected {totalPages} page name(s), got {names.Length}.",
                            "Import PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        int offset = 0;
        foreach (PdfImportPlan plan in plans)
        {
            plan.PageNames = names.Skip(offset).Take(plan.PageCount).ToArray();
            offset += plan.PageCount;
        }

        return true;
    }

    private async Task ImportPdfPlansAsync(
        IReadOnlyList<PdfImportPlan> plans,
        IReadOnlyList<string> skipped,
        object sender)
    {
        string destFolder = GetSelectedImportFolder();
        Button? importButton = sender as Button;
        int totalPages = plans.Sum(plan => plan.PageCount);
        var createdPages = new List<PageInfo>();

        try
        {
            if (importButton != null)
                importButton.IsEnabled = false;

            using (ShowBusyOverlay($"Importing {plans.Count} PDF file(s), {totalPages} page(s)..."))
            {
                await WaitForBusyOverlayRenderAsync();
                bool hadUserPageExpansion = _expandedPageTreePaths.Count > 0;

                for (int index = 0; index < plans.Count; index++)
                {
                    PdfImportPlan plan = plans[index];
                    string pdfName = Path.GetFileName(plan.PdfPath);
                    var progress = new Progress<string>(msg =>
                    {
                        string text = $"PDF {index + 1}/{plans.Count}: {msg}";
                        TxtStatus.Text = text;
                        BusyOverlayText.Text = text;
                    });

                    ((IProgress<string>)progress).Report("copying pages...");
                    Dictionary<int, IReadOnlyList<PdfLayerInfo>> pdfLayerCache = [];

                    IReadOnlyList<PageInfo> created = OurPlaneCoreJobStore.ImportPdf(
                        _currentJob!,
                        plan.PdfPath,
                        plan.PageNames,
                        destFolder,
                        pdfLayerCache);
                    createdPages.AddRange(created);
                    BusyOverlayText.Text = $"Imported {pdfName} ({created.Count} page(s)).";
                }

                ReloadPagesTree();
                if (createdPages.Count > 0)
                    SelectPageByFolder(createdPages[0].FolderPath);
                if (!hadUserPageExpansion)
                    CollapseTreeAndExpansionState(PagesTree, _expandedPageTreePaths);
            }

            string skippedText = skipped.Count > 0 ? $" Skipped {skipped.Count} unreadable PDF(s)." : "";
            TxtStatus.Text =
                $"Imported {createdPages.Count} page(s) from {plans.Count} PDF file(s). " +
                $"PDF layers load on demand from the PDF Layers tab.{skippedText}";

            if (createdPages.Count > 0)
                await TryApplySheetMetadataAfterPdfImportAsync(createdPages);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Import PDF failed.");
            MessageBox.Show($"Import failed:\n{ex.Message}", "Import PDF",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (importButton != null)
                importButton.IsEnabled = true;
        }
    }

    private static IReadOnlyList<string> SelectedPdfPaths(OpenFileDialog dialog)
    {
        IEnumerable<string> paths = dialog.FileNames.Length > 0
            ? dialog.FileNames
            : [dialog.FileName];

        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> DefaultPageNames(string pdfPath, int pageCount, bool includePdfName)
    {
        string pdfName = Path.GetFileNameWithoutExtension(pdfPath);
        if (!includePdfName)
            return Enumerable.Range(1, pageCount).Select(i => $"Page {i}").ToList();

        if (pageCount == 1)
            return [string.IsNullOrWhiteSpace(pdfName) ? "Page 1" : pdfName];

        string prefix = string.IsNullOrWhiteSpace(pdfName) ? "PDF" : pdfName.Trim();
        return Enumerable.Range(1, pageCount)
            .Select(i => $"{prefix} - Page {i}")
            .ToList();
    }

    private static Dictionary<int, IReadOnlyList<PdfLayerInfo>> BuildPdfLayerCache(
        string pdfPath,
        int pageCount,
        IProgress<string>? progress)
    {
        var cache = new Dictionary<int, IReadOnlyList<PdfLayerInfo>>();
        for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            progress?.Report($"scanning PDF layers {pageIndex + 1}/{pageCount}...");
            if (PdfLayerRenderService.TryReadVisibleLayers(pdfPath, pageIndex, out var layers, out _) &&
                layers.Count > 0)
            {
                cache[pageIndex] = layers;
            }
        }
        return cache;
    }
}
