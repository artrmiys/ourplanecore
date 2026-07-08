using System;
using System.Collections.Generic;
using System.Linq;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void QueueSheetLegendAutoSortSweep()
    {
        if (_currentJob == null)
            return;

        string jobRoot = _currentJob.RootPath;
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (_currentJob == null ||
                    !string.Equals(_currentJob.RootPath, jobRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                RunSheetLegendAutoSortSweep();
            }),
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void RunSheetLegendAutoSortSweep()
    {
        if (_currentJob == null)
            return;

        int cleared = 0;
        foreach (PageInfo page in CollectPagesUnder(_currentJob.PagesRoot))
        {
            if (IsPageLegendManual(page) || page.LegendTakeoffOrder.Count == 0)
                continue;

            List<TakeoffItem> pageTakeoffs = TakeoffsForPage(page.FolderPath).ToList();
            if (pageTakeoffs.Count <= 1)
                continue;

            List<string> storedOrder = page.LegendTakeoffOrder
                .Select(NormalizeTakeoffLegendOrderKey)
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (storedOrder.Count == 0)
                continue;

            List<string> autoOrder = AutoOrderTakeoffs(pageTakeoffs)
                .Select(TakeoffLegendOrderKey)
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!IsStoredOrderCompatibleWithAuto(storedOrder, autoOrder))
                continue;

            ClearPageLegendOrder(page);
            cleared++;
        }

        if (cleared <= 0)
            return;

        RefreshPagesTakeoffIndicators();
        RefreshSheetLegend();
    }

    private static bool IsStoredOrderCompatibleWithAuto(
        IReadOnlyList<string> storedOrder,
        IReadOnlyList<string> autoOrder)
    {
        if (storedOrder.Count == 0 || storedOrder.Count > autoOrder.Count)
            return false;

        int storedIndex = 0;
        foreach (string autoKey in autoOrder)
        {
            if (string.Equals(storedOrder[storedIndex], autoKey, StringComparison.OrdinalIgnoreCase))
                storedIndex++;
            if (storedIndex >= storedOrder.Count)
                return true;
        }

        return false;
    }

    private void ClearPageLegendOrder(PageInfo page)
    {
        page.LegendTakeoffOrder = [];
        page.LegendTakeoffOrderMode = "auto";
        if (_currentPage != null && IsSamePageFolder(_currentPage.FolderPath, page.FolderPath))
        {
            _currentPage.LegendTakeoffOrder = [];
            _currentPage.LegendTakeoffOrderMode = "auto";
        }
        OurPlaneCoreJobStore.SavePageLegendTakeoffOrder(page.FolderPath, [], "auto");
    }
}
