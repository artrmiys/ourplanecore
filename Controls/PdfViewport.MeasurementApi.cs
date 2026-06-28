using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Docnet.Core;
using Docnet.Core.Models;
using OurPlaneCore;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    // Bulk-load measurements restored from a saved file
    public void LoadMeasurements(IEnumerable<Measurement> measurements)
    {
        SetMeasurements(measurements);
    }

    public void SetHiddenTakeoffFolders(IEnumerable<string> takeoffFolders)
    {
        _hiddenTakeoffFolders.Clear();
        foreach (string folder in takeoffFolders)
        {
            if (!string.IsNullOrWhiteSpace(folder))
                _hiddenTakeoffFolders.Add(NormalizePageFolderForCompare(folder));
        }

        InvalidateHiddenTakeoffFolderCache();
        PruneHiddenMeasurementSelection();
        RequestRepaint();
    }

    public void SetHiddenMeasurementIds(IEnumerable<string> measurementIds)
    {
        _hiddenMeasurementIds.Clear();
        foreach (string measurementId in measurementIds)
        {
            string clean = (measurementId ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(clean))
                _hiddenMeasurementIds.Add(clean);
        }

        InvalidateHiddenTakeoffFolderCache();
        PruneHiddenMeasurementSelection();
        RequestRepaint();
    }

    public void SetTakeoffLayerOrder(IEnumerable<string> takeoffFolders)
    {
        _takeoffLayerRanks.Clear();
        int index = 0;
        foreach (string folder in takeoffFolders)
        {
            if (string.IsNullOrWhiteSpace(folder))
                continue;

            string key = NormalizePageFolderForCompare(folder);
            if (!_takeoffLayerRanks.ContainsKey(key))
                _takeoffLayerRanks[key] = index++;
        }

        InvalidateTakeoffLayerRankCache();
        RequestRepaint();
    }

    public bool HideRulerAnnotations
    {
        get => _hideRulerAnnotations;
        set
        {
            if (_hideRulerAnnotations == value)
                return;

            _hideRulerAnnotations = value;
            PruneHiddenAnnotationSelection();

            RequestRepaint();
        }
    }

    public bool HideVisibleRulerAnnotationsOnActivePage()
    {
        bool changed = false;
        foreach (PageAnnotation annotation in _annotations)
        {
            if (!IsAnnotationOnActivePage(annotation) ||
                !IsRulerAnnotation(annotation) ||
                annotation.Hidden)
            {
                continue;
            }

            annotation.Hidden = true;
            changed = true;
        }

        HideRulerAnnotations = true;
        if (changed)
            RequestRepaint();
        return changed;
    }

    public bool ShowAllRulerAnnotationsOnActivePage()
    {
        bool changed = false;
        foreach (PageAnnotation annotation in _annotations)
        {
            if (!IsAnnotationOnActivePage(annotation) ||
                !IsRulerAnnotation(annotation) ||
                !annotation.Hidden)
            {
                continue;
            }

            annotation.Hidden = false;
            changed = true;
        }

        HideRulerAnnotations = false;
        if (changed)
            RequestRepaint();
        return changed;
    }

    public void FocusMeasurement(Measurement measurement)
    {
        if (!_measurementSet.Contains(measurement))
            return;

        SelectMeasurement(measurement, -1);
        CenterOnMeasurement(measurement);
        RequestRepaint();
        PostStatus($"Selected {EntryTitle(measurement.MType)}. Drag the body to move; drag blue handles to reshape.");
    }

    public void SelectMeasurements(IEnumerable<Measurement> measurements)
    {
        var selected = measurements
            .Where(m => _measurementSet.Contains(m) && IsMeasurementOnActivePage(m))
            .Distinct()
            .ToList();
        SetSelectedMeasurements(selected, selected.LastOrDefault(), -1);
        if (selected.Count > 0)
            PostStatus($"Selected {selected.Count} measurement(s). Ctrl+C copies, Ctrl+V pastes to the active sheet.");
    }

    public void RefreshMeasurementDisplay() =>
        RequestRepaint();

    public void FocusPdfPoint(float pdfX, float pdfY)
    {
        if (!HasViewportCanvasSize || _zoom <= 0)
            return;

        float visibleW = ScreenToPdfDistance(ViewportCanvasWidth);
        float visibleH = ScreenToPdfDistance(ViewportCanvasHeight);
        _panX = pdfX - visibleW / 2f;
        _panY = pdfY - visibleH / 2f;
        ClampPanToPage();
        ScheduleRerenderForZoom(force: false);
        RequestRepaint();
        Focus();
    }
    public void SetMeasurements(IEnumerable<Measurement> measurements, bool clearUndoStack = true)
    {
        _measurements.Clear();
        _measurementSet.Clear();
        _measurementsByPage.Clear();
        InvalidateActivePageMeasurementCache();
        _measurements.AddRange(measurements);
        foreach (Measurement measurement in _measurements)
        {
            _measurementSet.Add(measurement);
            IndexMeasurementByPage(measurement);
        }
        if (clearUndoStack)
            ClearViewportUndoStack();
        ClearSelection();
        RequestRepaint();
    }

    // Adds programmatically generated measurements (e.g. multiline offsets)
    // without resetting viewport state; removable as one undo step.
    public void AddGeneratedMeasurements(IReadOnlyList<Measurement> measurements)
    {
        var added = new List<Measurement>();
        foreach (Measurement measurement in measurements)
        {
            if (measurement == null || !_measurementSet.Add(measurement))
                continue;

            _measurements.Add(measurement);
            IndexMeasurementByPage(measurement);
            added.Add(measurement);
        }

        if (added.Count == 0)
            return;

        PushAddedMeasurementsUndo(added, "remove generated offset lines");
        RequestRepaint();
    }

    public IReadOnlyList<PageAnnotation> GetPageAnnotations() =>
        _annotations
            .Where(annotation => IsAnnotationOnActivePage(annotation))
            .ToList();

    public void SetPageAnnotations(IEnumerable<PageAnnotation> annotations)
    {
        _annotations.Clear();
        _annotations.AddRange(annotations);
        ClearViewportUndoStack();
        ClearAnnotationSelection();
        PublishTransformSelectionChanged();
        RequestRepaint();
    }
}
