using System;
using System.Collections.Generic;
using System.Linq;
using OurPlanCore.Controls;
using SkiaSharp;

namespace OurPlanCore;

public partial class MainWindow
{
    private bool _synchronizingOrthoAcrossViewports;

    private void ConfigureDetachedSheetWindow(DetachedSheetWindow window, UnitMode unitMode)
    {
        PdfViewport viewport = window.Viewport;
        viewport.ActiveAnnotationColor = _annotationColor;
        viewport.ActiveAnnotationStrokeWidth = _annotationStrokeWidth;
        viewport.SnapEnabled = _viewport.SnapEnabled;
        viewport.PdfSnapEnabled = _viewport.PdfSnapEnabled;
        viewport.OrthoEnabled = _viewport.OrthoEnabled;
        viewport.BoxModeEnabled = _viewport.BoxModeEnabled;
        viewport.IsReadOnlyMode = IsCurrentJobReadOnly;
        ApplyDetachedActiveTakeoff(viewport, _activeItem);
        ApplyDetachedTool(window, IsCurrentJobReadOnly ? "select" : _activeTool);

        viewport.StatusChanged += message => TxtStatus.Text = $"{window.Page.Name}: {message}";
        viewport.ToolChanged += tool => ApplyDetachedTool(window, tool);
        viewport.MeasurementAdded += measurement => OnDetachedMeasurementAdded(window, measurement, unitMode);
        viewport.MeasurementsAdded += measurements => OnDetachedMeasurementsAdded(window, measurements, unitMode);
        viewport.MeasurementRemoved += measurement => OnDetachedMeasurementsRemoved(window, [measurement], unitMode);
        viewport.MeasurementsRemoved += measurements => OnDetachedMeasurementsRemoved(window, measurements, unitMode);
        viewport.MeasurementChanged += measurement => OnDetachedMeasurementsChanged(window, [measurement], unitMode);
        viewport.MeasurementsChanged += measurements => OnDetachedMeasurementsChanged(window, measurements, unitMode);
        viewport.ScaleChanged += scale => OnDetachedPageScaleChanged(window, scale);
        viewport.PageAnnotationAdded += _ => SaveDetachedPageAnnotations(window);
        viewport.PageAnnotationRemoved += _ => SaveDetachedPageAnnotations(window);
        viewport.PageAnnotationChanged += _ => SaveDetachedPageAnnotations(window);
        viewport.PageAnnotationTextRequested += RequestPageAnnotationText;
        viewport.OrthoChanged += SynchronizeOrthoAcrossViewports;
        viewport.JoistDirectionCaptured += (area, start, end) => OnDetachedJoistDirectionCaptured(window, area, start, end, unitMode);
    }

    private void OnDetachedPageScaleChanged(DetachedSheetWindow window, double scaleMetersPerPt)
    {
        if (!IsCurrentJobWritable || scaleMetersPerPt <= 0)
            return;

        ApplyScaleToPagesCore([window.Page], scaleMetersPerPt, allowClear: false, updateStatus: true);
    }

    private void SynchronizeOrthoAcrossViewports(bool enabled)
    {
        if (_synchronizingOrthoAcrossViewports)
            return;

        _synchronizingOrthoAcrossViewports = true;
        try
        {
            _viewport.OrthoEnabled = enabled;
            foreach (DetachedSheetWindow window in _detachedSheetWindows.ToList())
                window.Viewport.OrthoEnabled = enabled;
        }
        finally
        {
            _synchronizingOrthoAcrossViewports = false;
        }
    }

    private void RefreshDetachedPageScale(string pageFolder, double scaleMetersPerPt)
    {
        foreach (DetachedSheetWindow window in _detachedSheetWindows.ToList())
        {
            if (IsSamePageFolder(window.Page.FolderPath, pageFolder))
                window.RefreshPageScale(scaleMetersPerPt);
        }
    }

    private void ApplyDetachedActiveTakeoff(PdfViewport viewport, TakeoffItem? item)
    {
        if (item == null)
        {
            viewport.ActiveTakeoffFolder = "";
            viewport.ActiveCountSymbol = _newCountSymbol;
            return;
        }

        viewport.ActiveColor = item.Color;
        viewport.ActiveTakeoffFolder = item.FolderPath;
        viewport.ActiveCountSymbol = item.CountSymbol;
    }

    private void ApplyDetachedTool(DetachedSheetWindow window, string tool)
    {
        string requestedTool = string.IsNullOrWhiteSpace(tool) ? "select" : tool;
        if (IsRecordTool(requestedTool))
        {
            string measurementType = RecordMeasurementType(requestedTool);
            bool joistArea = IsJoistAreaTool(requestedTool);
            if (_activeItem == null || !CanRecordIntoActiveTakeoff(_activeItem, measurementType, joistArea))
            {
                window.Viewport.SetTool("select");
                TxtStatus.Text = $"{window.Page.Name}: select a matching takeoff item in the main Takeoffs tree before drawing in detached view.";
                return;
            }

            ApplyDetachedActiveTakeoff(window.Viewport, _activeItem);
        }

        window.Viewport.SetTool(ViewportToolName(requestedTool));
    }

    private void OnDetachedMeasurementAdded(DetachedSheetWindow window, Measurement measurement, UnitMode unitMode)
    {
        if (!IsCurrentJobWritable)
        {
            RejectDetachedWrite(window, unitMode);
            return;
        }

        if (!TryResolveDetachedTakeoffItem(window.Viewport, measurement, out TakeoffItem item))
        {
            window.Viewport.DeleteMeasurements([measurement]);
            TxtStatus.Text = $"{window.Page.Name}: no matching takeoff item is active for {MeasurementTypeTitle(measurement.MType)}.";
            return;
        }

        _activeItem = item;
        EnsureTakeoffItemFolder(item);
        measurement.PageFolder = window.Page.FolderPath;
        measurement.TakeoffFolder = item.FolderPath;
        if (measurement.ScaleMetersPerPt <= 0)
            measurement.ScaleMetersPerPt = window.Viewport.ScaleMetersPerPt;
        if (!item.Measurements.Contains(measurement))
            item.Measurements.Add(measurement);

        OurPlanCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        RefreshAfterDetachedTakeoffChange(window, [item], [measurement.PageFolder], unitMode);
        if (item.IsJoistArea && OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area")
            BeginDetachedJoistDirectionCapture(window, item, measurement);
    }

    private void OnDetachedMeasurementsAdded(DetachedSheetWindow window, IReadOnlyList<Measurement> measurements, UnitMode unitMode)
    {
        foreach (Measurement measurement in measurements)
            OnDetachedMeasurementAdded(window, measurement, unitMode);
    }

    private void OnDetachedMeasurementsRemoved(DetachedSheetWindow window, IReadOnlyList<Measurement> measurements, UnitMode unitMode)
    {
        if (!IsCurrentJobWritable)
        {
            RejectDetachedWrite(window, unitMode);
            return;
        }

        if (measurements.Count == 0)
            return;

        var removed = measurements.Distinct().ToHashSet();
        var changedItems = new List<TakeoffItem>();
        foreach (TakeoffItem item in _takeoffItems)
        {
            int before = item.Measurements.Count;
            item.Measurements.RemoveAll(removed.Contains);
            if (item.Measurements.Count != before)
                changedItems.Add(item);
        }

        RefreshAfterDetachedTakeoffChange(
            window,
            changedItems,
            measurements.Select(measurement => measurement.PageFolder),
            unitMode);
    }

    private void OnDetachedMeasurementsChanged(DetachedSheetWindow window, IReadOnlyList<Measurement> measurements, UnitMode unitMode)
    {
        if (!IsCurrentJobWritable)
        {
            RejectDetachedWrite(window, unitMode);
            return;
        }

        if (measurements.Count == 0)
            return;

        var changedItems = measurements
            .Select(FindTakeoffItemForMeasurement)
            .Where(item => item != null)
            .Cast<TakeoffItem>()
            .Distinct()
            .ToList();
        foreach (Measurement measurement in measurements)
        {
            TakeoffItem? item = FindTakeoffItemForMeasurement(measurement);
            if (item == null)
                continue;

            measurement.PageFolder = window.Page.FolderPath;
            measurement.TakeoffFolder = item.FolderPath;
            if (measurement.ScaleMetersPerPt <= 0)
                measurement.ScaleMetersPerPt = window.Viewport.ScaleMetersPerPt;
            OurPlanCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        }

        RefreshAfterDetachedTakeoffChange(
            window,
            changedItems,
            measurements.Select(measurement => measurement.PageFolder),
            unitMode);
    }

    private bool TryResolveDetachedTakeoffItem(PdfViewport viewport, Measurement measurement, out TakeoffItem item)
    {
        string measurementType = OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType);
        if (!string.IsNullOrWhiteSpace(viewport.ActiveTakeoffFolder))
        {
            TakeoffItem? byViewport = FindTakeoffItemByFolder(viewport.ActiveTakeoffFolder, measurementType);
            if (byViewport != null)
            {
                byViewport.MeasurementType = measurementType;
                item = byViewport;
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(measurement.TakeoffFolder))
        {
            TakeoffItem? byMeasurement = FindTakeoffItemByFolder(measurement.TakeoffFolder, measurementType);
            if (byMeasurement != null)
            {
                byMeasurement.MeasurementType = measurementType;
                item = byMeasurement;
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

    private void RefreshAfterDetachedTakeoffChange(
        DetachedSheetWindow window,
        IEnumerable<TakeoffItem> changedItems,
        IEnumerable<string?> pageFolders,
        UnitMode unitMode)
    {
        var items = changedItems.Distinct().ToList();
        foreach (TakeoffItem item in items)
        {
            RefreshTreeItem(item);
            QueueTakeoffAutosave(item);
        }

        using (UsePageMeasurementLookup())
        {
            RefreshTakeoffRowVisualsForItems(items);
            foreach (string pageFolder in pageFolders
                         .Where(folder => !string.IsNullOrWhiteSpace(folder))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Cast<string>())
            {
                RefreshPageTakeoffIndicatorsForFolder(pageFolder);
            }

            RefreshSheetLegend();
        }

        window.RefreshTakeoffDisplay(_currentJob!, _takeoffItems, _settings, unitMode);
        _viewport.SetMeasurements(_takeoffItems.SelectMany(takeoff => takeoff.Measurements));
        RefreshEstimateTable();
        UpdateTotalDisplay();
    }

    private void SaveDetachedPageAnnotations(DetachedSheetWindow window)
    {
        if (!IsCurrentJobWritable)
        {
            TxtStatus.Text = $"{window.Page.Name}: read-only; markup changes were not saved.";
            return;
        }

        try
        {
            OurPlanCoreJobStore.SavePageAnnotations(
                window.Page.FolderPath,
                window.Viewport.GetPageAnnotations());
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"{window.Page.Name}: annotation save skipped: {ex.Message}";
        }
    }

    private void BeginDetachedJoistDirectionCapture(DetachedSheetWindow window, TakeoffItem item, Measurement area)
    {
        item.IsJoistTakeoff = true;
        OurPlanCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        if (window.Viewport.BeginJoistDirectionCapture(area))
            TxtStatus.Text = $"{window.Page.Name}: draw a two-point line parallel to joists for {item.Name}.";
    }

    private void OnDetachedJoistDirectionCaptured(
        DetachedSheetWindow window,
        Measurement area,
        SKPoint start,
        SKPoint end,
        UnitMode unitMode)
    {
        if (!IsCurrentJobWritable)
        {
            RejectDetachedWrite(window, unitMode);
            return;
        }

        TakeoffItem? item = FindTakeoffItemForMeasurement(area);
        if (item == null)
            return;

        if (!TryDirectionFromPoints(start, end, out double directionDegrees))
        {
            TxtStatus.Text = $"{window.Page.Name}: joist direction line is too short.";
            return;
        }

        item.IsJoistTakeoff = true;
        item.JoistDirectionDegrees = directionDegrees;
        area.JoistDirectionDegrees = directionDegrees;
        area.JoistDirectionLocked = true;
        OurPlanCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        RefreshAfterDetachedTakeoffChange(window, [item], [area.PageFolder], unitMode);

        JoistLayoutResult layout = JoistTakeoffCalculator.Calculate(area, window.Viewport.ScaleMetersPerPt);
        TxtStatus.Text = $"{window.Page.Name}: joists generated for {item.Name}, direction {directionDegrees:0.#} deg, {JoistTakeoffCalculator.FormatDiagnostics(layout, unitMode)}.";
    }

    private void RejectDetachedWrite(DetachedSheetWindow window, UnitMode unitMode)
    {
        if (_currentJob != null)
        {
            LoadTakeoffsForJob();
            window.RefreshTakeoffDisplay(_currentJob, _takeoffItems, _settings, unitMode);
        }
        TxtStatus.Text = $"{window.Page.Name}: this job is read-only.";
    }
}
