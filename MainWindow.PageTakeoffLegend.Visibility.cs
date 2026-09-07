using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OurPlanCore;

public partial class MainWindow
{
    // Page takeoff visibility, layer order persistence, and path rebasing.

    private bool IsPageTakeoffVisible(PageInfo page, TakeoffItem takeoff)
    {
        string key = TakeoffLegendOrderKey(takeoff);
        if (string.IsNullOrWhiteSpace(key))
            return true;

        if (page.HiddenTakeoffs
            .Select(NormalizeTakeoffLegendOrderKey)
            .Contains(key, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var measurements = MeasurementsForTakeoffOnPage(takeoff, page.FolderPath).ToList();
        return measurements.Count == 0 ||
               measurements.Any(measurement => !IsMeasurementHiddenByPageSnapshot(page, measurement));
    }

    private void TogglePageTakeoffVisibility(PageInfo page, TakeoffItem takeoff)
    {
        string key = TakeoffLegendOrderKey(takeoff);
        if (string.IsNullOrWhiteSpace(key))
            return;

        var hidden = page.HiddenTakeoffs
            .Select(NormalizeTakeoffLegendOrderKey)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        bool currentlyVisible = IsPageTakeoffVisible(page, takeoff);
        bool nowHidden;
        var hiddenMeasurements = page.HiddenMeasurements
            .Select(NormalizeMeasurementVisibilityId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!currentlyVisible)
        {
            hidden.RemoveAll(value => string.Equals(value, key, StringComparison.OrdinalIgnoreCase));
            hiddenMeasurements = RemoveHiddenMeasurementsForTakeoffs(page, [takeoff], hiddenMeasurements);
            nowHidden = false;
        }
        else
        {
            hidden.Add(key);
            nowHidden = true;
        }

        page.HiddenTakeoffs = hidden;
        page.HiddenMeasurements = hiddenMeasurements;
        if (_currentPage != null && IsSamePageFolder(_currentPage.FolderPath, page.FolderPath))
        {
            _currentPage.HiddenTakeoffs = hidden.ToList();
            _currentPage.HiddenMeasurements = hiddenMeasurements.ToList();
        }
        ApplyViewportPageTakeoffVisibility(page);
        OurPlanCoreJobStore.SavePageHiddenTakeoffs(page.FolderPath, hidden);
        OurPlanCoreJobStore.SavePageHiddenMeasurements(page.FolderPath, hiddenMeasurements);
        RefreshPageOverlayTreeNode(page);
        RefreshSheetLegend();
        RefreshDetachedSheetsForPage(page.FolderPath);
        TxtStatus.Text = nowHidden
            ? $"Hidden on {page.Name}: {takeoff.Name}."
            : $"Visible on {page.Name}: {takeoff.Name}.";
    }

    private void ApplyViewportPageTakeoffVisibility(PageInfo page)
    {
        if (_currentPage == null || !IsSamePageFolder(_currentPage.FolderPath, page.FolderPath))
            return;

        PageInfo visibilityPage = _currentPage;
        ApplyViewportPageTakeoffVisibilitySnapshot(visibilityPage);
    }

    private void ApplyViewportPageTakeoffVisibilitySnapshot(PageInfo visibilityPage)
    {
        if (_currentPage == null || !IsSamePageFolder(_currentPage.FolderPath, visibilityPage.FolderPath))
            return;

        var hiddenKeys = visibilityPage.HiddenTakeoffs
            .Select(NormalizeTakeoffLegendOrderKey)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hiddenFolders = _takeoffItems
            .Where(item => hiddenKeys.Contains(TakeoffLegendOrderKey(item)))
            .Select(item => item.FolderPath)
            .ToList();
        _viewport.SetHiddenTakeoffFolders(hiddenFolders);
        _viewport.SetHiddenMeasurementIds(visibilityPage.HiddenMeasurements);
        _viewport.SetTakeoffLayerOrder(LayerOrderedTakeoffsForPage(visibilityPage).Select(item => item.FolderPath));
    }

    private string TakeoffLegendOrderKey(TakeoffItem item) =>
        NormalizeTakeoffLegendOrderKey(item.FolderPath);

    private static bool IsPageLegendManual(PageInfo page) =>
        string.Equals(page.LegendTakeoffOrderMode, "manual", StringComparison.OrdinalIgnoreCase);

    private string NormalizeTakeoffLegendOrderKey(string value)
    {
        string clean = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clean))
            return "";

        if (_currentJob != null && Path.IsPathFullyQualified(clean))
        {
            string full = NormalizePath(clean);
            if (OurPlanCoreJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, full))
                clean = Path.GetRelativePath(_currentJob.TakeoffsRoot, full);
        }

        return clean.Replace('\\', '/').Trim('/');
    }

    private void SavePageLegendOrder(PageInfo page, IReadOnlyList<TakeoffItem> orderedTakeoffs)
    {
        var order = orderedTakeoffs
            .Select(TakeoffLegendOrderKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        page.LegendTakeoffOrder = order;
        page.LegendTakeoffOrderMode = "manual";
        if (_currentPage != null && IsSamePageFolder(_currentPage.FolderPath, page.FolderPath))
        {
            _currentPage.LegendTakeoffOrder = order.ToList();
            _currentPage.LegendTakeoffOrderMode = "manual";
        }
        OurPlanCoreJobStore.SavePageLegendTakeoffOrder(page.FolderPath, order, "manual");
    }

    private void SavePageTakeoffLayerOrder(PageInfo page, IReadOnlyList<TakeoffItem> orderedTakeoffs)
    {
        var order = orderedTakeoffs
            .Select(TakeoffLegendOrderKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        page.TakeoffLayerOrder = order;
        if (_currentPage != null && IsSamePageFolder(_currentPage.FolderPath, page.FolderPath))
            _currentPage.TakeoffLayerOrder = order.ToList();
        PageTakeoffLayerOrderStore.Save(page.FolderPath, order);
    }

    private void RebasePageLegendTakeoffOrderReferences(string oldPath, string newPath)
    {
        RebasePageLegendTakeoffOrderReferences([(oldPath, newPath)]);
    }

    private void RebasePageLegendTakeoffOrderReferences(IReadOnlyList<(string OldPath, string NewPath)> movedPaths)
    {
        if (movedPaths.Count == 0)
            return;

        foreach (var (oldPath, newPath) in movedPaths)
            RebaseExpandedTreePaths(_expandedTakeoffTreePaths, oldPath, newPath);
        RebasePageTakeoffSelectionReferences(movedPaths);

        if (_currentJob == null)
            return;

        var rebases = movedPaths
            .Select(move => (
                OldKey: NormalizeTakeoffLegendOrderKey(move.OldPath),
                NewKey: NormalizeTakeoffLegendOrderKey(move.NewPath)))
            .Where(move =>
                !string.IsNullOrWhiteSpace(move.OldKey) &&
                !string.IsNullOrWhiteSpace(move.NewKey) &&
                !string.Equals(move.OldKey, move.NewKey, StringComparison.OrdinalIgnoreCase))
            .GroupBy(move => move.OldKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderByDescending(move => move.OldKey.Length)
            .ToList();
        if (rebases.Count == 0)
            return;

        foreach (PageInfo page in CollectPagesUnder(_currentJob.PagesRoot))
        {
            if (page.LegendTakeoffOrder.Count > 0)
            {
                var updated = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string key in page.LegendTakeoffOrder)
                {
                    string rebased = RebaseTakeoffLegendOrderKey(key, rebases);
                    if (string.IsNullOrWhiteSpace(rebased) || !seen.Add(rebased))
                        continue;

                    updated.Add(rebased);
                }

                bool changed = updated.Count != page.LegendTakeoffOrder.Count ||
                               updated.Where((key, index) => !string.Equals(
                                   key,
                                   NormalizeTakeoffLegendOrderKey(page.LegendTakeoffOrder[index]),
                                   StringComparison.OrdinalIgnoreCase)).Any();
                if (changed)
                {
                    page.LegendTakeoffOrder = updated;
                    OurPlanCoreJobStore.SavePageLegendTakeoffOrder(page.FolderPath, updated, page.LegendTakeoffOrderMode);
                    if (_currentPage != null && IsSamePageFolder(_currentPage.FolderPath, page.FolderPath))
                    {
                        _currentPage.LegendTakeoffOrder = updated.ToList();
                        _currentPage.LegendTakeoffOrderMode = page.LegendTakeoffOrderMode;
                    }
                }
            }

            if (page.TakeoffLayerOrder.Count == 0)
                continue;

            var updatedLayers = new List<string>();
            var seenLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in page.TakeoffLayerOrder)
            {
                string rebased = RebaseTakeoffLegendOrderKey(key, rebases);
                if (string.IsNullOrWhiteSpace(rebased) || !seenLayers.Add(rebased))
                    continue;

                updatedLayers.Add(rebased);
            }

            bool layerChanged = updatedLayers.Count != page.TakeoffLayerOrder.Count ||
                           updatedLayers.Where((key, index) => !string.Equals(
                               key,
                               NormalizeTakeoffLegendOrderKey(page.TakeoffLayerOrder[index]),
                               StringComparison.OrdinalIgnoreCase)).Any();
            if (!layerChanged)
                continue;

            page.TakeoffLayerOrder = updatedLayers;
            PageTakeoffLayerOrderStore.Save(page.FolderPath, updatedLayers);
            if (_currentPage != null && IsSamePageFolder(_currentPage.FolderPath, page.FolderPath))
                _currentPage.TakeoffLayerOrder = updatedLayers.ToList();
        }
    }

    private void RebasePageTakeoffSelectionReferences(IReadOnlyList<(string OldPath, string NewPath)> movedPaths)
    {
        if (_pageTakeoffMultiSelection.Count == 0 || movedPaths.Count == 0)
            return;

        var rebases = movedPaths
            .Select(move => (
                OldKey: NormalizeSelectionPath(move.OldPath),
                NewKey: NormalizeSelectionPath(move.NewPath)))
            .Where(move =>
                !string.IsNullOrWhiteSpace(move.OldKey) &&
                !string.IsNullOrWhiteSpace(move.NewKey) &&
                !string.Equals(move.OldKey, move.NewKey, StringComparison.OrdinalIgnoreCase))
            .GroupBy(move => move.OldKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderByDescending(move => move.OldKey.Length)
            .ToList();
        if (rebases.Count == 0)
            return;

        var updated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool changed = false;
        foreach (string key in _pageTakeoffMultiSelection)
        {
            int separator = key.IndexOf('|');
            if (separator < 0)
            {
                updated.Add(key);
                continue;
            }

            string pageKey = key[..separator];
            string takeoffKey = key[(separator + 1)..];
            string rebasedTakeoff = RebaseNormalizedPathKey(takeoffKey, rebases);
            string next = $"{pageKey}|{rebasedTakeoff}";
            changed |= !string.Equals(key, next, StringComparison.OrdinalIgnoreCase);
            updated.Add(next);
        }

        if (!changed)
            return;

        _pageTakeoffMultiSelection.Clear();
        foreach (string key in updated)
            _pageTakeoffMultiSelection.Add(key);
    }

    private static string RebaseNormalizedPathKey(
        string key,
        IReadOnlyList<(string OldKey, string NewKey)> rebases)
    {
        foreach (var (oldKey, newKey) in rebases)
        {
            if (string.Equals(key, oldKey, StringComparison.OrdinalIgnoreCase))
                return newKey;

            string prefix = oldKey.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                            Path.DirectorySeparatorChar;
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return newKey.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + key[(prefix.Length - 1)..];
        }

        return key;
    }

    private string RebaseTakeoffLegendOrderKey(
        string key,
        IReadOnlyList<(string OldKey, string NewKey)> rebases)
    {
        string rebased = NormalizeTakeoffLegendOrderKey(key);
        foreach (var (oldKey, newKey) in rebases)
        {
            string next = RebaseTakeoffLegendOrderKey(rebased, oldKey, newKey);
            if (!string.Equals(next, rebased, StringComparison.OrdinalIgnoreCase))
                return next;
        }

        return rebased;
    }

    private string RebaseTakeoffLegendOrderKey(string key, string oldPrefix, string newPrefix)
    {
        string clean = NormalizeTakeoffLegendOrderKey(key);
        if (string.Equals(clean, oldPrefix, StringComparison.OrdinalIgnoreCase))
            return newPrefix;

        string prefix = oldPrefix.TrimEnd('/') + "/";
        if (clean.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return newPrefix.TrimEnd('/') + clean[(prefix.Length - 1)..];

        return clean;
    }

    // Building a full ContextMenu (many MenuItems) for every page-takeoff row
    // up front was a large slice of the per-node cost when a 100+ page job
    // opened. The real menu is built only when the row is actually invoked:
    // the mouse path rebuilds fresh in PagesTree_PreviewMouseRightButtonDown,
}
