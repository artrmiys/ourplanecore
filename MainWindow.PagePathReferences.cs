using System;
using System.IO;
using System.Linq;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void RemovePageTabsForAffectedPath(string affectedPath)
    {
        bool changed = false;
        for (int i = _pageTabs.Count - 1; i >= 0; i--)
        {
            PageTabState tab = _pageTabs[i];
            if (!OurPlaneCoreJobStore.IsSameOrDescendant(affectedPath, tab.PageFolder))
                continue;

            if (ReferenceEquals(tab, _activePageTab))
                _activePageTab = null;
            _pageTabs.RemoveAt(i);
            changed = true;
        }

        if (changed)
            RefreshPageTabs(_activePageTab);
    }

    private bool UpdatePageReferencesForMovedPath(string oldPath, string newPath)
    {
        string oldFull = NormalizePath(oldPath);
        string newFull = NormalizePath(newPath);
        RebaseExpandedTreePaths(_expandedPageTreePaths, oldFull, newFull);
        bool activeAffected = _currentPage != null &&
                              OurPlaneCoreJobStore.IsSameOrDescendant(oldFull, _currentPage.FolderPath);
        bool tabsChanged = false;
        bool measurementsChanged = RebaseMeasurementPageFolderReferences(oldFull, newFull);

        foreach (PageTabState tab in _pageTabs)
        {
            if (!OurPlaneCoreJobStore.IsSameOrDescendant(oldFull, tab.PageFolder))
                continue;

            tab.PageFolder = RebaseDescendantPath(oldFull, newFull, tab.PageFolder);
            if (OurPlaneCoreJobStore.TryReadPage(tab.PageFolder) is { } page)
                tab.PageName = page.Name;
            tabsChanged = true;
        }

        if (!string.IsNullOrWhiteSpace(_settings.LastPageFolder) &&
            OurPlaneCoreJobStore.IsSameOrDescendant(oldFull, _settings.LastPageFolder))
        {
            _settings.LastPageFolder = RebaseDescendantPath(oldFull, newFull, _settings.LastPageFolder);
            SaveAppSettings();
        }

        if (activeAffected)
        {
            _currentPage = null;
            _currentPdfPath = "";
        }

        if (tabsChanged)
            RefreshPageTabs(_activePageTab);
        if (measurementsChanged)
        {
            _viewport.SetMeasurements(_takeoffItems.SelectMany(item => item.Measurements));
            RefreshPagesTakeoffIndicators();
            RefreshEstimateTable();
        }

        return activeAffected;
    }

    private bool RebaseMeasurementPageFolderReferences(string oldFull, string newFull)
    {
        if (_currentJob == null)
            return false;

        bool changed = false;
        foreach (TakeoffItem item in _takeoffItems)
        {
            bool itemChanged = false;
            foreach (Measurement measurement in item.Measurements)
            {
                if (string.IsNullOrWhiteSpace(measurement.PageFolder))
                    continue;

                string current = NormalizePageReferencePath(measurement.PageFolder);
                if (!OurPlaneCoreJobStore.IsSameOrDescendant(oldFull, current))
                    continue;

                measurement.PageFolder = RebaseDescendantPath(oldFull, newFull, current);
                changed = true;
                itemChanged = true;
            }

            if (itemChanged)
                OurPlaneCoreJobStore.SaveTakeoffItem(item);
        }

        return changed;
    }

    private void ReloadActivePageTabAfterPathChange(bool shouldReload)
    {
        if (!shouldReload || _activePageTab == null)
            return;

        if (Directory.Exists(_activePageTab.PageFolder))
        {
            LoadPageFromTab(_activePageTab);
            return;
        }

        _activePageTab = null;
        RefreshPageTabs(null);
        _viewport.ClearPage();
        TxtStatusPage.Text = "—";
    }

    private static string RebaseDescendantPath(string oldRoot, string newRoot, string path)
    {
        string relative = Path.GetRelativePath(oldRoot, NormalizePath(path));
        return relative == "."
            ? newRoot
            : Path.GetFullPath(Path.Combine(newRoot, relative));
    }

    private string NormalizePageReferencePath(string path)
    {
        if (_currentJob != null && !Path.IsPathFullyQualified(path))
            path = Path.Combine(_currentJob.RootPath, path);

        return NormalizePath(path);
    }
}
