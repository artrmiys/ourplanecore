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
using OurPlanCore;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    // в”Ђв”Ђ Undo в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

    public void UndoLast()
    {
        if (TryCancelPendingSheetOverlayTransformForUndo())
            return;

        if (_drawPts.Count > 0)
        {
            _drawPts.RemoveAt(_drawPts.Count - 1);
            _rubberEnd = _drawPts.Count > 0 ? _rubberEnd : null;
            if (_drawPts.Count == 0)
                _areaCutMeasurement = null;
            RequestRepaint();
            if (_drawPts.Count > 0)
                PostRecordPrompt();
            else
                PostStatus("Undo: drawing cleared.");
            return;
        }

        if (TryUndoLastViewportAction())
            return;

        PostStatus("Nothing to undo on this page.");
    }

    // Remove specific measurements without firing MeasurementRemoved events
    // (caller handles model cleanup; this just keeps the render list consistent)
    public void DeleteMeasurements(IEnumerable<Measurement> toRemove)
    {
        foreach (var m in toRemove.ToList())
        {
            _measurements.Remove(m);
            _measurementSet.Remove(m);
            RemoveMeasurementFromPageIndex(m);
            ForgetMeasurementState(m);
        }
        if (_selectedMeasurement == null && _selectedMeasurements.Count > 0)
            _selectedMeasurement = _selectedMeasurements.LastOrDefault();
        if (_selectedMeasurement == null)
            _selectedVertexIndex = -1;
        RequestRepaint();
    }

    public bool DeletePageAnnotation(PageAnnotation annotation)
    {
        return DeletePageAnnotations([annotation]);
    }

    public bool DeletePageAnnotations(IEnumerable<PageAnnotation> annotations)
    {
        var removed = annotations
            .Where(annotation => _annotations.Contains(annotation))
            .Distinct()
            .ToList();
        if (removed.Count == 0)
            return false;

        PushRemovedAnnotationsUndo(removed, $"restore {removed.Count} deleted markup(s)");
        foreach (PageAnnotation annotation in removed)
            _annotations.Remove(annotation);

        if (removed.Any(annotation =>
                ReferenceEquals(_selectedAnnotation, annotation) ||
                _selectedAnnotations.Contains(annotation)))
        {
            ClearAnnotationSelection();
        }

        RequestRepaint();
        PublishTransformSelectionChanged();
        PostStatus(removed.Count == 1
            ? $"Deleted {ToolTitle(removed[0].Kind)} markup."
            : $"Deleted {removed.Count} selected markups.");
        foreach (PageAnnotation annotation in removed)
            PageAnnotationRemoved?.Invoke(annotation);
        return true;
    }

    public bool UpdatePageAnnotationText(PageAnnotation annotation, string text)
    {
        if (!_annotations.Contains(annotation) || !IsAnnotationOnActivePage(annotation))
            return false;

        string clean = (text ?? "").Trim();
        if (string.Equals(annotation.Text, clean, StringComparison.Ordinal))
            return false;

        string beforeText = annotation.Text;
        PushAnnotationTextUndoSnapshot(annotation, beforeText, "edit markup text");
        annotation.Text = clean;
        RequestRepaint();
        PostStatus($"Updated {ToolTitle(annotation.Kind)} markup text.");
        PageAnnotationChanged?.Invoke(annotation);
        return true;
    }

    public bool InsertMeasurementVertex(Measurement measurement, float pdfX, float pdfY)
    {
        if (!_measurementSet.Contains(measurement) ||
            !IsMeasurementOnActivePage(measurement) ||
            measurement.MType is not ("line" or "area"))
        {
            return false;
        }

        var point = new SKPoint(pdfX, pdfY);
        int insertIndex = measurement.Points.Count;

        if (measurement.Points.Count >= 2)
        {
            float bestDistance = float.PositiveInfinity;
            for (int i = 1; i < measurement.Points.Count; i++)
            {
                float distance = DistanceToSegment(point, measurement.Points[i - 1], measurement.Points[i]);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    insertIndex = i;
                }
            }

            if (measurement.MType == "area" && measurement.Points.Count > 2)
            {
                float closingDistance = DistanceToSegment(point, measurement.Points[^1], measurement.Points[0]);
                if (closingDistance < bestDistance)
                    insertIndex = measurement.Points.Count;
            }
        }

        List<SKPoint> beforePoints = measurement.Points.ToList();
        measurement.Points.Insert(insertIndex, point);
        PushMeasurementUndoSnapshot(measurement, beforePoints, $"insert {ToolTitle(measurement.MType)} vertex");
        SelectMeasurement(measurement, insertIndex);
        NotifyMeasurementsChanged([measurement]);
        RequestRepaint();
        PostStatus($"Inserted {ToolTitle(measurement.MType)} vertex {insertIndex + 1}.");
        return true;
    }

    public bool RemoveNearestMeasurementVertex(Measurement measurement, float pdfX, float pdfY)
    {
        if (!_measurementSet.Contains(measurement) ||
            !IsMeasurementOnActivePage(measurement) ||
            measurement.MType is not ("line" or "area") ||
            measurement.Points.Count == 0)
        {
            return false;
        }

        int minimumPoints = measurement.MType == "area" ? 3 : 2;
        if (measurement.Points.Count <= minimumPoints)
        {
            PostStatus($"{ToolTitle(measurement.MType)} needs at least {minimumPoints} vertices.");
            return false;
        }

        var point = new SKPoint(pdfX, pdfY);
        int removeIndex = 0;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < measurement.Points.Count; i++)
        {
            float distance = DistanceSquared(point, measurement.Points[i]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                removeIndex = i;
            }
        }

        PushMeasurementUndoSnapshot(measurement, measurement.Points.ToList(), $"remove {ToolTitle(measurement.MType)} vertex");
        measurement.Points.RemoveAt(removeIndex);
        SelectMeasurement(measurement, Math.Min(removeIndex, measurement.Points.Count - 1));
        NotifyMeasurementsChanged([measurement]);
        RequestRepaint();
        PostStatus($"Removed {ToolTitle(measurement.MType)} vertex {removeIndex + 1}.");
        return true;
    }
}
