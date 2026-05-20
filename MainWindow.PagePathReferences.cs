using System;
using System.Collections.Generic;
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

    private bool UpdatePageReferencesForMovedPath(string oldPath, string newPath) =>
        UpdatePageReferencesForMovedPaths([(oldPath, newPath)]);

    private bool UpdatePageReferencesForMovedPaths(IReadOnlyList<(string OldPath, string NewPath)> moves)
    {
        var normalizedMoves = moves
            .Select(move => (OldPath: NormalizePath(move.OldPath), NewPath: NormalizePath(move.NewPath)))
            .Where(move => !string.Equals(move.OldPath, move.NewPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (normalizedMoves.Count == 0)
            return false;

        foreach (var move in normalizedMoves)
            RebaseExpandedTreePaths(_expandedPageTreePaths, move.OldPath, move.NewPath);

        bool activeAffected = _currentPage != null &&
                              normalizedMoves.Any(move => OurPlaneCoreJobStore.IsSameOrDescendant(move.OldPath, _currentPage.FolderPath));
        bool tabsChanged = false;
        bool measurementsChanged = RebaseMeasurementPageFolderReferences(normalizedMoves);

        foreach (PageTabState tab in _pageTabs)
        {
            if (!TryRebaseMovedPagePath(normalizedMoves, tab.PageFolder, out string rebasedTabFolder))
                continue;

            tab.PageFolder = rebasedTabFolder;
            if (OurPlaneCoreJobStore.TryReadPage(tab.PageFolder) is { } page)
                tab.PageName = page.Name;
            tabsChanged = true;
        }

        if (!string.IsNullOrWhiteSpace(_settings.LastPageFolder) &&
            TryRebaseMovedPagePath(normalizedMoves, _settings.LastPageFolder, out string rebasedLastPageFolder))
        {
            _settings.LastPageFolder = rebasedLastPageFolder;
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

    private bool RebaseMeasurementPageFolderReferences(IReadOnlyList<(string OldPath, string NewPath)> moves)
    {
        if (_currentJob == null || moves.Count == 0)
            return false;

        bool changed = false;
        foreach (TakeoffItem item in _takeoffItems)
        {
            bool itemChanged = false;
            foreach (Measurement measurement in item.Measurements)
            {
                if (string.IsNullOrWhiteSpace(measurement.PageFolder))
                    continue;

                if (!TryRebaseMovedPagePath(moves, measurement.PageFolder, out string rebased))
                    continue;

                measurement.PageFolder = rebased;
                changed = true;
                itemChanged = true;
            }

            if (itemChanged)
                OurPlaneCoreJobStore.SaveTakeoffItem(item);
        }

        return changed;
    }

    private bool TryRebaseMovedPagePath(
        IReadOnlyList<(string OldPath, string NewPath)> moves,
        string path,
        out string rebased)
    {
        string current = NormalizePageReferencePath(path);
        foreach (var move in moves)
        {
            if (!OurPlaneCoreJobStore.IsSameOrDescendant(move.OldPath, current))
                continue;

            rebased = RebaseDescendantPath(move.OldPath, move.NewPath, current);
            return true;
        }

        rebased = current;
        return false;
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
        ApplyRulerVisibilityToViewport();
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
