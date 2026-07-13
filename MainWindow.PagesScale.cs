using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    private void SetSelectedPagesScaleFromContext(TreeViewItem anchor)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before setting sheet scale.";
            return;
        }

        IReadOnlyList<PageInfo> pages = DistinctScalePages(SelectedPagesFromPagesTree(anchor));
        if (pages.Count == 0)
        {
            TxtStatus.Text = "Pages tree: select one or more sheets first.";
            return;
        }

        string initial = CommonPageScaleText(pages);
        string title = pages.Count == 1 ? "Set Page Scale" : "Set Selected Page Scale";
        string prompt = pages.Count == 1
            ? $"Scale for {pages[0].Name} (example: 1/8\" = 1'0\" or 1:96):"
            : $"Scale for {pages.Count} selected sheets (example: 1/8\" = 1'0\" or 1:96):";
        string? raw = ShowInputDialog(prompt, initial, title);
        if (raw == null)
            return;

        if (!PdfSheetMetadataService.TryParseScaleMetersPerPt(raw, out double scaleMetersPerPt))
        {
            MessageBox.Show(
                "Enter an architectural scale, e.g. 1/8\" = 1'0\" or 1:96.",
                "Scale",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ApplyScaleToPages(pages, scaleMetersPerPt);
    }

    private void ApplyCurrentScaleToSelectedPagesFromContext(TreeViewItem anchor)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before applying sheet scale.";
            return;
        }

        double scaleMetersPerPt = CurrentPageScaleMetersPerPt();
        if (scaleMetersPerPt <= 0)
        {
            TxtStatus.Text = "Current sheet has no scale to apply.";
            return;
        }

        IReadOnlyList<PageInfo> pages = DistinctScalePages(SelectedPagesFromPagesTree(anchor));
        if (pages.Count == 0)
        {
            TxtStatus.Text = "Pages tree: select one or more sheets first.";
            return;
        }

        ApplyScaleToPages(pages, scaleMetersPerPt);
    }

    private double CurrentPageScaleMetersPerPt()
    {
        if (_currentPage?.ScaleMetersPerPt > 0)
            return _currentPage.ScaleMetersPerPt;

        return _viewport.ScaleMetersPerPt > 0 ? _viewport.ScaleMetersPerPt : 0;
    }

    private void ApplyScaleToPages(IReadOnlyList<PageInfo> pages, double scaleMetersPerPt)
    {
        if (pages.Count == 0 || scaleMetersPerPt <= 0)
            return;

        var changedItems = new HashSet<TakeoffItem>();
        var affectedPageFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool activePageScaled = false;

        foreach (PageInfo page in pages)
        {
            PageInfo updatedPage = OurPlanCoreJobStore.TryReadPage(page.FolderPath) ?? page;
            OurPlanCoreJobStore.SavePageScale(updatedPage.FolderPath, scaleMetersPerPt);
            WriteFloatingPageSetupMetadata(updatedPage, updatedPage.Name, scaleMetersPerPt);

            page.ScaleMetersPerPt = scaleMetersPerPt;
            updatedPage.ScaleMetersPerPt = scaleMetersPerPt;
            affectedPageFolders.Add(updatedPage.FolderPath);

            foreach (TakeoffItem item in ApplyScaleToPageMeasurements(updatedPage.FolderPath, scaleMetersPerPt))
                changedItems.Add(item);

            if (_currentPage != null && IsSamePageFolder(_currentPage.FolderPath, updatedPage.FolderPath))
            {
                _currentPage.ScaleMetersPerPt = scaleMetersPerPt;
                _viewport.ScaleMetersPerPt = scaleMetersPerPt;
                activePageScaled = true;
            }
        }

        foreach (TakeoffItem item in changedItems)
        {
            RefreshTreeItem(item);
            QueueTakeoffAutosave(item);
        }

        FlushTakeoffAutosaves();
        RefreshPageTakeoffIndicatorsForFolders(affectedPageFolders);
        if (activePageScaled)
        {
            UpdateScaleUi(scaleMetersPerPt);
            RefreshFloatingPageSetup(_currentPage?.FolderPath);
            _viewport.InvalidateVisual();
        }

        RefreshAllTotals();

        string scaleLabel = PdfSheetMetadataService.FormatImperialScale(scaleMetersPerPt);
        TxtStatus.Text = pages.Count == 1
            ? $"Page scale set: {pages[0].Name}, {scaleLabel}."
            : $"Page scale set on {affectedPageFolders.Count} selected sheet(s): {scaleLabel}.";
    }

    private static IReadOnlyList<PageInfo> DistinctScalePages(IEnumerable<PageInfo> pages) =>
        pages
            .Where(page => !string.IsNullOrWhiteSpace(page.FolderPath))
            .GroupBy(page => NormalizePath(page.FolderPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

    private static string CommonPageScaleText(IReadOnlyList<PageInfo> pages)
    {
        if (pages.Count == 0)
            return "";

        double first = pages[0].ScaleMetersPerPt;
        bool same = pages.All(page => Math.Abs(page.ScaleMetersPerPt - first) <= 0.000000001);
        return same ? PdfSheetMetadataService.FormatImperialScale(first) : "";
    }
}
