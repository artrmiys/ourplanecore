using System;
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
        RefreshActiveTakeoffVisuals();
        QueueTakeoffAutosave(item);
        RefreshPagesTakeoffIndicators();
        RefreshSheetLegend();
        UpdateTotalDisplay();
        if (item.IsJoistArea && OurPlaneCoreJobStore.NormalizeMeasurementType(m.MType) == "area")
            BeginJoistDirectionCapture(item, m);
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
        RefreshPagesTakeoffIndicators();
        RefreshSheetLegend();
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

    private void OnPageAnnotationChanged(PageAnnotation annotation)
    {
        SaveCurrentPageAnnotations();
    }

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
