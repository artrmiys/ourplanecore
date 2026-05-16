using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private bool CanSortPageLegends(IEnumerable<PageInfo> pages) =>
        pages.Any(CanSortPageLegend);

    private static bool HasCustomPageLegendOrders(IEnumerable<PageInfo> pages) =>
        pages.Any(HasCustomPageLegendOrder);

    private void SortPageLegendsAuto(IReadOnlyList<PageInfo> pages)
    {
        int sorted = 0;
        foreach (PageInfo page in DistinctLegendPages(pages))
        {
            var ordered = AutoOrderTakeoffs(TakeoffsForPage(page.FolderPath));
            if (ordered.Count <= 1)
                continue;

            ClearPageLegendOrder(page);
            sorted++;
        }

        FinishPageLegendBatch(sorted, "live auto sorting");
    }

    private void SortPageLegendsByName(IReadOnlyList<PageInfo> pages)
    {
        int sorted = 0;
        foreach (PageInfo page in DistinctLegendPages(pages))
        {
            var ordered = TakeoffsForPage(page.FolderPath)
                .OrderBy(takeoff => takeoff.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(takeoff => MeasurementTypeTitle(takeoff.MeasurementType), StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (ordered.Count <= 1)
                continue;

            SavePageLegendOrder(page, ordered);
            sorted++;
        }

        FinishPageLegendBatch(sorted, "A-Z");
    }

    private void ResetPageLegendOrders(IReadOnlyList<PageInfo> pages)
    {
        int reset = 0;
        foreach (PageInfo page in DistinctLegendPages(pages))
        {
            if (page.LegendTakeoffOrder.Count == 0)
                continue;

            ClearPageLegendOrder(page);
            reset++;
        }

        FinishPageLegendBatch(reset, "default auto rules");
    }

    private void FinishPageLegendBatch(int changedCount, string ruleName)
    {
        if (changedCount <= 0)
        {
            TxtStatus.Text = $"No sheet legends needed {ruleName}.";
            return;
        }

        RefreshPagesTakeoffIndicators();
        RefreshSheetLegend();
        TxtStatus.Text = changedCount == 1
            ? $"Applied {ruleName} to 1 sheet legend."
            : $"Applied {ruleName} to {changedCount} sheet legends.";
    }

    private IReadOnlyList<PageInfo> LegendPagesInFolder(string folderPath) =>
        DistinctLegendPages(CollectPagesUnder(folderPath));

    private IReadOnlyList<PageInfo> SelectedLegendPages(TreeViewItem anchor)
    {
        var pages = new List<PageInfo>();
        foreach (PagesClipboardEntry entry in GetSelectedPageEntries(anchor))
        {
            if (entry.IsPage)
            {
                if (OurPlaneCoreJobStore.TryReadPage(entry.SourcePath) is { } selectedPage)
                    pages.Add(selectedPage);
            }
            else
            {
                pages.AddRange(CollectPagesUnder(entry.SourcePath));
            }
        }

        if (pages.Count == 0 && anchor.Tag is PageInfo anchorPage)
            pages.Add(anchorPage);

        return DistinctLegendPages(pages);
    }

    private static IReadOnlyList<PageInfo> DistinctLegendPages(IEnumerable<PageInfo> pages) =>
        pages
            .Where(page => !string.IsNullOrWhiteSpace(page.FolderPath))
            .GroupBy(page => NormalizePath(page.FolderPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
}
