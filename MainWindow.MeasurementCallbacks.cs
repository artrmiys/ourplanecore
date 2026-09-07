using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace OurPlanCore;

public partial class MainWindow
{
    private void OnMeasurementAdded(Measurement m)
    {
        if (RejectReadOnlyViewportMutation("add measurements"))
            return;

        if (!TryResolveTakeoffItemForMeasurement(m, out TakeoffItem item))
        {
            _viewport.DeleteMeasurements([m]);
            TxtStatus.Text = $"No {MeasurementTypeTitle(m.MType)} takeoff item is active. Select {MeasurementTypeTitle(m.MType)} again to create one.";
            return;
        }

        _activeItem = item;
        EnsureTakeoffItemFolder(item);
        m.TakeoffFolder = item.FolderPath;
        if (m.ScaleMetersPerPt <= 0)
            m.ScaleMetersPerPt = _viewport.ScaleMetersPerPt;
        item.Measurements.Add(m);
        OurPlanCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        RefreshTreeItem(item);
        using (UsePageMeasurementLookup())
        {
            RefreshTakeoffRowVisualsForItems(new[] { item });
            RefreshPageTakeoffIndicatorsForFolder(m.PageFolder);
            RefreshSheetLegend();
        }
        QueueTakeoffAutosave(item);
        UpdateTotalDisplay();
        GenerateMultiLineOffsets(item, m);
        if (item.IsJoistArea && OurPlanCoreJobStore.NormalizeMeasurementType(m.MType) == "area")
            BeginJoistDirectionCapture(item, m);
    }

    private void OnMeasurementsAdded(IReadOnlyList<Measurement> measurements)
    {
        if (RejectReadOnlyViewportMutation("add measurements"))
            return;

        if (measurements.Count == 0)
            return;

        var unique = measurements
            .Where(measurement => measurement != null)
            .Distinct()
            .ToList();
        var changedItems = new HashSet<TakeoffItem>();
        var unresolved = new List<Measurement>();
        foreach (Measurement measurement in unique)
        {
            if (!TryResolveTakeoffItemForMeasurement(measurement, out TakeoffItem item))
            {
                unresolved.Add(measurement);
                continue;
            }

            _activeItem = item;
            EnsureTakeoffItemFolder(item);
            measurement.TakeoffFolder = item.FolderPath;
            if (measurement.ScaleMetersPerPt <= 0)
                measurement.ScaleMetersPerPt = _viewport.ScaleMetersPerPt;
            if (!item.Measurements.Contains(measurement))
                item.Measurements.Add(measurement);
            changedItems.Add(item);
        }

        if (unresolved.Count > 0)
            _viewport.DeleteMeasurements(unresolved);

        foreach (TakeoffItem item in changedItems)
        {
            OurPlanCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
            RefreshTreeItem(item);
            QueueTakeoffAutosave(item);
        }

        using (UsePageMeasurementLookup())
        {
            RefreshTakeoffRowVisualsForItems(changedItems);
            foreach (string pageFolder in unique.Select(m => m.PageFolder)
                         .Where(page => !string.IsNullOrWhiteSpace(page))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                RefreshPageTakeoffIndicatorsForFolder(pageFolder);
            }
            RefreshSheetLegend();
        }
        UpdateTotalDisplay();

        if (unresolved.Count > 0 && changedItems.Count == 0)
            TxtStatus.Text = "No matching takeoff item was found for restored measurements.";
    }

    private bool TryResolveTakeoffItemForMeasurement(Measurement m, out TakeoffItem item)
    {
        string measurementType = OurPlanCoreJobStore.NormalizeMeasurementType(m.MType);

        if (!string.IsNullOrWhiteSpace(m.TakeoffFolder))
        {
            // Fast path: nearly every drawn measurement targets the active item,
            // so check it before scanning the full item list.
            if (_activeItem != null &&
                OurPlanCoreJobStore.NormalizeMeasurementType(_activeItem.MeasurementType) == measurementType &&
                string.Equals(_activeItem.FolderPath, m.TakeoffFolder, StringComparison.OrdinalIgnoreCase))
            {
                _activeItem.MeasurementType = measurementType;
                item = _activeItem;
                return true;
            }

            var byFolder = _takeoffItems.FirstOrDefault(i =>
                OurPlanCoreJobStore.NormalizeMeasurementType(i.MeasurementType) == measurementType &&
                string.Equals(i.FolderPath, m.TakeoffFolder, StringComparison.OrdinalIgnoreCase));
            if (byFolder != null)
            {
                byFolder.MeasurementType = measurementType;
                item = byFolder;
                return true;
            }
        }

        if (_activeItem != null &&
            OurPlanCoreJobStore.NormalizeMeasurementType(_activeItem.MeasurementType) == measurementType)
        {
            _activeItem.MeasurementType = measurementType;
            item = _activeItem;
            return true;
        }

        item = null!;
        return false;
    }

    private void OnMeasurementRemoved(Measurement m)
    {
        if (RejectReadOnlyViewportMutation("delete measurements"))
            return;

        foreach (var item in _takeoffItems)
        {
            if (item.Measurements.Remove(m))
            {
                RefreshTreeItem(item);
                QueueTakeoffAutosave(item);
            }
        }
        using (UsePageMeasurementLookup())
        {
            RefreshPageTakeoffIndicatorsForFolder(m.PageFolder);
            RefreshSheetLegend();
        }
        UpdateTotalDisplay();
    }

    private void OnMeasurementsRemoved(IReadOnlyList<Measurement> measurements)
    {
        if (RejectReadOnlyViewportMutation("delete measurements"))
            return;

        if (measurements.Count == 0)
            return;

        var unique = measurements
            .Where(measurement => measurement != null)
            .Distinct()
            .ToList();
        var removedSet = new HashSet<Measurement>(unique);
        var changedItems = new List<TakeoffItem>();
        foreach (TakeoffItem item in _takeoffItems)
        {
            int before = item.Measurements.Count;
            item.Measurements.RemoveAll(measurement => removedSet.Contains(measurement));
            if (item.Measurements.Count != before)
                changedItems.Add(item);
        }

        foreach (TakeoffItem item in changedItems)
        {
            RefreshTreeItem(item);
            QueueTakeoffAutosave(item);
        }

        using (UsePageMeasurementLookup())
        {
            foreach (string pageFolder in unique.Select(m => m.PageFolder)
                         .Where(page => !string.IsNullOrWhiteSpace(page))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                RefreshPageTakeoffIndicatorsForFolder(pageFolder);
            }
            RefreshSheetLegend();
        }
        UpdateTotalDisplay();
    }

    private void OnMeasurementChanged(Measurement m)
    {
        if (RejectReadOnlyViewportMutation("edit measurements"))
            return;

        foreach (var item in _takeoffItems)
        {
            if (!item.Measurements.Contains(m)) continue;

            if (!string.IsNullOrWhiteSpace(item.FolderPath))
                m.TakeoffFolder = item.FolderPath;
            if (m.ScaleMetersPerPt <= 0)
                m.ScaleMetersPerPt = _viewport.ScaleMetersPerPt;
            OurPlanCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
            bool previousSuppressFocus = _suppressCanvasFocusFromTakeoffSelection;
            _suppressCanvasFocusFromTakeoffSelection = true;
            try
            {
                RefreshTreeItem(item);
            }
            finally
            {
                _suppressCanvasFocusFromTakeoffSelection = previousSuppressFocus;
            }
            QueueTakeoffAutosave(item);
            break;
        }
        RefreshEstimateTable();
        RefreshSheetLegend();
        UpdateTotalDisplay();
    }

    private void OnMeasurementsChanged(IReadOnlyList<Measurement> measurements)
    {
        if (RejectReadOnlyViewportMutation("edit measurements"))
            return;

        if (measurements.Count == 0)
            return;

        var unique = measurements
            .Where(measurement => measurement != null)
            .Distinct()
            .ToList();
        if (unique.Count == 0)
            return;

        var itemByMeasurement = BuildTakeoffItemByMeasurementLookup();
        var changedItems = new HashSet<TakeoffItem>();
        foreach (Measurement measurement in unique)
        {
            if (!itemByMeasurement.TryGetValue(measurement, out TakeoffItem? item))
                continue;

            if (!string.IsNullOrWhiteSpace(item.FolderPath))
                measurement.TakeoffFolder = item.FolderPath;
            if (measurement.ScaleMetersPerPt <= 0)
                measurement.ScaleMetersPerPt = _viewport.ScaleMetersPerPt;
            changedItems.Add(item);
        }

        bool previousSuppressFocus = _suppressCanvasFocusFromTakeoffSelection;
        _suppressCanvasFocusFromTakeoffSelection = true;
        try
        {
            foreach (TakeoffItem item in changedItems)
            {
                OurPlanCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
                RefreshTreeItem(item);
                QueueTakeoffAutosave(item);
            }
        }
        finally
        {
            _suppressCanvasFocusFromTakeoffSelection = previousSuppressFocus;
        }

        RefreshEstimateTable();
        RefreshSheetLegend();
        UpdateTotalDisplay();
    }

    private void OnPageAnnotationChanged(PageAnnotation annotation)
    {
        if (RejectReadOnlyViewportMutation("edit markups", reloadAnnotations: true))
            return;

        _currentPageAnnotationsDirty = true;
        TrySaveCurrentPageAnnotationsFromUi();
    }

    private string? RequestPageAnnotationText(string prompt, string initial, string title) =>
        ShowMultilineInputDialog(prompt, initial, title);

    private void SaveCurrentPageAnnotations()
    {
        SaveCurrentPageAnnotationsCore();
        SaveDirtyDetachedPageAnnotations();
    }

    private void SaveCurrentPageAnnotationsCore()
    {
        if (!_currentPageAnnotationsDirty)
            return;

        if (_currentPage == null || !_currentPageAnnotationsLoaded)
            throw new InvalidOperationException("The edited sheet annotations are not attached to a loaded page.");
        if (!IsCurrentJobWritable)
            throw new InvalidOperationException("The current project is read-only; edited sheet annotations remain unsaved.");

        OurPlanCoreJobStore.SavePageAnnotations(
            _currentPage.FolderPath,
            _viewport.GetPageAnnotations());
        _currentPageAnnotationsDirty = false;
    }

    private bool TrySaveCurrentPageAnnotationsFromUi()
    {
        try
        {
            SaveCurrentPageAnnotationsCore();
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, "Page annotation autosave failed; the in-memory annotation state remains dirty.");
            TxtStatus.Text = $"Annotation save failed; changes remain pending: {ex.Message}";
            return false;
        }
    }

    private bool TryFlushCurrentPageAnnotationsForNavigation(string operation)
    {
        try
        {
            SaveCurrentPageAnnotationsCore();
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Page annotation save failed before {operation}.");
            TxtStatus.Text = $"{operation} canceled: annotation changes remain unsaved.";
            MessageBox.Show(
                this,
                $"The current sheet's annotation changes could not be saved, so {operation} was canceled.\n\n" +
                $"Your changes are still held in the open sheet. Retry after resolving the storage problem.\n\n{ex.Message}",
                "Annotation Save Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private bool RejectReadOnlyViewportMutation(string operation, bool reloadAnnotations = false)
    {
        if (!IsCurrentJobReadOnly)
            return false;

        if (reloadAnnotations && _currentPage != null)
        {
            if (!_currentPageAnnotationsDirty)
            {
                _viewport.SetPageAnnotations(
                    OurPlanCoreJobStore.LoadPageAnnotations(_currentPage.FolderPath));
                _currentPageAnnotationsDirty = false;
            }
        }
        else
        {
            LoadTakeoffsForJob();
        }

        TxtStatus.Text = _currentPageAnnotationsDirty
            ? $"Read-only: cannot {operation}; earlier unsaved annotation changes remain in the open sheet."
            : $"Read-only: cannot {operation}.";
        return true;
    }
}
