using System;
using System.Collections.Generic;
using System.Linq;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void OnMeasurementAdded(Measurement m)
    {
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
        OurPlaneCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
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
        if (item.IsJoistArea && OurPlaneCoreJobStore.NormalizeMeasurementType(m.MType) == "area")
            BeginJoistDirectionCapture(item, m);
    }

    private void OnMeasurementsAdded(IReadOnlyList<Measurement> measurements)
    {
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
            OurPlaneCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
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
        string measurementType = OurPlaneCoreJobStore.NormalizeMeasurementType(m.MType);

        if (!string.IsNullOrWhiteSpace(m.TakeoffFolder))
        {
            var byFolder = _takeoffItems.FirstOrDefault(i =>
                OurPlaneCoreJobStore.NormalizeMeasurementType(i.MeasurementType) == measurementType &&
                string.Equals(i.FolderPath, m.TakeoffFolder, StringComparison.OrdinalIgnoreCase));
            if (byFolder != null)
            {
                byFolder.MeasurementType = measurementType;
                item = byFolder;
                return true;
            }
        }

        if (_activeItem != null &&
            OurPlaneCoreJobStore.NormalizeMeasurementType(_activeItem.MeasurementType) == measurementType)
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
        foreach (var item in _takeoffItems)
        {
            if (!item.Measurements.Contains(m)) continue;

            if (!string.IsNullOrWhiteSpace(item.FolderPath))
                m.TakeoffFolder = item.FolderPath;
            if (m.ScaleMetersPerPt <= 0)
                m.ScaleMetersPerPt = _viewport.ScaleMetersPerPt;
            OurPlaneCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
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
                OurPlaneCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
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
        SaveCurrentPageAnnotations();
    }

    private string? RequestPageAnnotationText(string prompt, string initial, string title) =>
        ShowMultilineInputDialog(prompt, initial, title);

    private void SaveCurrentPageAnnotations()
    {
        if (_currentPage == null)
            return;

        try
        {
            OurPlaneCoreJobStore.SavePageAnnotations(
                _currentPage.FolderPath,
                _viewport.GetPageAnnotations());
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Annotation save skipped: {ex.Message}";
        }
    }
}
