using System;
using System.Collections.Generic;
using System.Linq;
using OurPlaneCore;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private const int MaxViewportUndoDepth = 80;

    private void PushGeometryUndoSnapshot(
        IReadOnlyList<Measurement> measurements,
        IReadOnlyList<PageAnnotation> annotations,
        string status,
        string key = "",
        bool coalesce = false)
    {
        if (_applyingViewportUndo ||
            measurements.Count == 0 && annotations.Count == 0)
        {
            return;
        }

        var measurementSnapshots = measurements
            .Where(measurement => _measurements.Contains(measurement))
            .Select(measurement => new MeasurementPointUndo(
                measurement,
                measurement.Points.ToList(),
                CloneHoles(measurement.Holes)))
            .ToList();
        var annotationSnapshots = annotations
            .Where(annotation => _annotations.Contains(annotation))
            .Select(annotation => new AnnotationPointUndo(annotation, annotation.Points.ToList()))
            .ToList();
        PushUndoAction(new ViewportUndoAction(
            key,
            status,
            measurementSnapshots,
            annotationSnapshots,
            [],
            [],
            [],
            []), coalesce);
    }

    private void PushMeasurementUndoSnapshot(
        Measurement measurement,
        IReadOnlyList<SKPoint> beforePoints,
        string status,
        string key = "")
    {
        PushMeasurementUndoSnapshot(
            measurement,
            beforePoints,
            CloneHoles(measurement.Holes),
            status,
            key);
    }

    private void PushMeasurementUndoSnapshot(
        Measurement measurement,
        IReadOnlyList<SKPoint> beforePoints,
        IReadOnlyList<IReadOnlyList<SKPoint>> beforeHoles,
        string status,
        string key = "")
    {
        if (_applyingViewportUndo || !_measurements.Contains(measurement))
            return;

        PushUndoAction(new ViewportUndoAction(
            key,
            status,
            [new MeasurementPointUndo(measurement, beforePoints.ToList(), CloneHoles(beforeHoles))],
            [],
            [],
            [],
            [],
            []), coalesce: false);
    }

    private void PushAnnotationUndoSnapshot(
        PageAnnotation annotation,
        IReadOnlyList<SKPoint> beforePoints,
        string status,
        string key = "")
    {
        if (_applyingViewportUndo || !_annotations.Contains(annotation))
            return;

        PushUndoAction(new ViewportUndoAction(
            key,
            status,
            [],
            [new AnnotationPointUndo(annotation, beforePoints.ToList())],
            [],
            [],
            [],
            []), coalesce: false);
    }

    private void PushMeasurementUndoSnapshots(
        IReadOnlyDictionary<Measurement, List<SKPoint>> beforePoints,
        string status,
        string key = "")
    {
        PushMeasurementUndoSnapshots(beforePoints, new Dictionary<Measurement, List<List<SKPoint>>>(), status, key);
    }

    private void PushMeasurementUndoSnapshots(
        IReadOnlyDictionary<Measurement, List<SKPoint>> beforePoints,
        IReadOnlyDictionary<Measurement, List<List<SKPoint>>> beforeHoles,
        string status,
        string key = "")
    {
        if (_applyingViewportUndo || beforePoints.Count == 0)
            return;

        var snapshots = beforePoints
            .Where(pair => _measurements.Contains(pair.Key))
            .Select(pair => new MeasurementPointUndo(
                pair.Key,
                pair.Value.ToList(),
                beforeHoles.TryGetValue(pair.Key, out var holes)
                    ? CloneHoles(holes)
                    : CloneHoles(pair.Key.Holes)))
            .ToList();
        if (snapshots.Count == 0)
            return;

        PushUndoAction(new ViewportUndoAction(
            key,
            status,
            snapshots,
            [],
            [],
            [],
            [],
            []), coalesce: false);
    }

    private void PushGeometryUndoSnapshotFromOriginals(
        IReadOnlyDictionary<Measurement, List<SKPoint>> measurementBeforePoints,
        IReadOnlyDictionary<PageAnnotation, List<SKPoint>> annotationBeforePoints,
        string status,
        string key = "")
    {
        PushGeometryUndoSnapshotFromOriginals(
            measurementBeforePoints,
            new Dictionary<Measurement, List<List<SKPoint>>>(),
            annotationBeforePoints,
            status,
            key);
    }

    private void PushGeometryUndoSnapshotFromOriginals(
        IReadOnlyDictionary<Measurement, List<SKPoint>> measurementBeforePoints,
        IReadOnlyDictionary<Measurement, List<List<SKPoint>>> measurementBeforeHoles,
        IReadOnlyDictionary<PageAnnotation, List<SKPoint>> annotationBeforePoints,
        string status,
        string key = "")
    {
        if (_applyingViewportUndo ||
            measurementBeforePoints.Count == 0 && annotationBeforePoints.Count == 0)
        {
            return;
        }

        var measurementSnapshots = measurementBeforePoints
            .Where(pair => _measurements.Contains(pair.Key))
            .Select(pair => new MeasurementPointUndo(
                pair.Key,
                pair.Value.ToList(),
                measurementBeforeHoles.TryGetValue(pair.Key, out var holes)
                    ? CloneHoles(holes)
                    : CloneHoles(pair.Key.Holes)))
            .ToList();
        var annotationSnapshots = annotationBeforePoints
            .Where(pair => _annotations.Contains(pair.Key))
            .Select(pair => new AnnotationPointUndo(pair.Key, pair.Value.ToList()))
            .ToList();
        if (measurementSnapshots.Count == 0 && annotationSnapshots.Count == 0)
            return;

        PushUndoAction(new ViewportUndoAction(
            key,
            status,
            measurementSnapshots,
            annotationSnapshots,
            [],
            [],
            [],
            []), coalesce: false);
    }

    private void PushAddedMeasurementsUndo(IEnumerable<Measurement> added, string status)
    {
        if (_applyingViewportUndo)
            return;

        var snapshots = added
            .Where(measurement => _measurements.Contains(measurement))
            .Select(measurement => new MeasurementPresenceUndo(measurement, _measurements.IndexOf(measurement)))
            .ToList();
        if (snapshots.Count == 0)
            return;

        PushUndoAction(new ViewportUndoAction("", status, [], [], snapshots, [], [], []), coalesce: false);
    }

    private void PushAddedAnnotationsUndo(IEnumerable<PageAnnotation> added, string status)
    {
        if (_applyingViewportUndo)
            return;

        var snapshots = added
            .Where(annotation => _annotations.Contains(annotation))
            .Select(annotation => new AnnotationPresenceUndo(annotation, _annotations.IndexOf(annotation)))
            .ToList();
        if (snapshots.Count == 0)
            return;

        PushUndoAction(new ViewportUndoAction("", status, [], [], [], snapshots, [], []), coalesce: false);
    }

    private void PushRemovedMeasurementsUndo(IEnumerable<Measurement> removed, string status)
    {
        if (_applyingViewportUndo)
            return;

        var snapshots = removed
            .Where(measurement => _measurements.Contains(measurement))
            .Select(measurement => new MeasurementPresenceUndo(measurement, _measurements.IndexOf(measurement)))
            .ToList();
        if (snapshots.Count == 0)
            return;

        PushUndoAction(new ViewportUndoAction("", status, [], [], [], [], snapshots, []), coalesce: false);
    }

    private void PushRemovedAnnotationsUndo(IEnumerable<PageAnnotation> removed, string status)
    {
        if (_applyingViewportUndo)
            return;

        var snapshots = removed
            .Where(annotation => _annotations.Contains(annotation))
            .Select(annotation => new AnnotationPresenceUndo(annotation, _annotations.IndexOf(annotation)))
            .ToList();
        if (snapshots.Count == 0)
            return;

        PushUndoAction(new ViewportUndoAction("", status, [], [], [], [], [], snapshots), coalesce: false);
    }

    private void PushUndoAction(ViewportUndoAction action, bool coalesce)
    {
        if (coalesce &&
            _undoStack.Count > 0 &&
            _undoStack[^1].Key == action.Key &&
            SameUndoTargets(_undoStack[^1], action))
        {
            return;
        }

        _undoStack.Add(action);
        if (_undoStack.Count > MaxViewportUndoDepth)
            _undoStack.RemoveAt(0);
    }

    private bool TryUndoLastViewportAction()
    {
        while (_undoStack.Count > 0)
        {
            ViewportUndoAction action = _undoStack[^1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            if (ApplyUndoAction(action))
                return true;
        }

        return false;
    }

    private bool ApplyUndoAction(ViewportUndoAction action)
    {
        bool changed = false;
        _applyingViewportUndo = true;
        try
        {
            foreach (MeasurementPresenceUndo added in action.AddedMeasurements)
            {
                if (!_measurements.Remove(added.Target))
                    continue;

                if (ReferenceEquals(_selectedMeasurement, added.Target))
                    ClearSelection();
                MeasurementRemoved?.Invoke(added.Target);
                changed = true;
            }

            foreach (AnnotationPresenceUndo added in action.AddedAnnotations)
            {
                if (!_annotations.Remove(added.Target))
                    continue;

                if (ReferenceEquals(_selectedAnnotation, added.Target))
                    ClearAnnotationSelection();
                PageAnnotationRemoved?.Invoke(added.Target);
                changed = true;
            }

            foreach (MeasurementPresenceUndo removed in action.RemovedMeasurements)
            {
                if (_measurements.Contains(removed.Target))
                    continue;

                _measurements.Insert(Math.Clamp(removed.Index, 0, _measurements.Count), removed.Target);
                MeasurementAdded?.Invoke(removed.Target);
                changed = true;
            }

            foreach (AnnotationPresenceUndo removed in action.RemovedAnnotations)
            {
                if (_annotations.Contains(removed.Target))
                    continue;

                _annotations.Insert(Math.Clamp(removed.Index, 0, _annotations.Count), removed.Target);
                PageAnnotationAdded?.Invoke(removed.Target);
                changed = true;
            }

            foreach (MeasurementPointUndo snapshot in action.MeasurementPoints)
            {
                if (!_measurements.Contains(snapshot.Target))
                    continue;

                RestorePoints(snapshot.Target.Points, snapshot.Points);
                RestoreHoles(snapshot.Target.Holes, snapshot.Holes);
                PruneMeasurementVertexSelection(snapshot.Target);
                MeasurementChanged?.Invoke(snapshot.Target);
                changed = true;
            }

            foreach (AnnotationPointUndo snapshot in action.AnnotationPoints)
            {
                if (!_annotations.Contains(snapshot.Target))
                    continue;

                RestorePoints(snapshot.Target.Points, snapshot.Points);
                PageAnnotationChanged?.Invoke(snapshot.Target);
                changed = true;
            }
        }
        finally
        {
            _applyingViewportUndo = false;
        }

        if (!changed)
            return false;

        RequestRepaint();
        PublishTransformSelectionChanged();
        PostStatus($"Undo: {action.Status}.");
        return true;
    }

    private static void RestorePoints(List<SKPoint> target, IReadOnlyList<SKPoint> points)
    {
        target.Clear();
        target.AddRange(points);
    }

    private static List<List<SKPoint>> CloneHoles(IEnumerable<IReadOnlyList<SKPoint>> holes) =>
        holes.Select(hole => hole.ToList()).ToList();

    private static void RestoreHoles(List<List<SKPoint>> target, IReadOnlyList<IReadOnlyList<SKPoint>> holes)
    {
        target.Clear();
        target.AddRange(CloneHoles(holes));
    }

    private static bool SameUndoTargets(ViewportUndoAction left, ViewportUndoAction right) =>
        left.MeasurementPoints.Count == right.MeasurementPoints.Count &&
        left.AnnotationPoints.Count == right.AnnotationPoints.Count &&
        left.AddedMeasurements.Count == right.AddedMeasurements.Count &&
        left.AddedAnnotations.Count == right.AddedAnnotations.Count &&
        left.RemovedMeasurements.Count == right.RemovedMeasurements.Count &&
        left.RemovedAnnotations.Count == right.RemovedAnnotations.Count &&
        left.MeasurementPoints.All(snapshot => right.MeasurementPoints.Any(candidate => ReferenceEquals(candidate.Target, snapshot.Target))) &&
        left.AnnotationPoints.All(snapshot => right.AnnotationPoints.Any(candidate => ReferenceEquals(candidate.Target, snapshot.Target))) &&
        left.AddedMeasurements.All(snapshot => right.AddedMeasurements.Any(candidate => ReferenceEquals(candidate.Target, snapshot.Target))) &&
        left.AddedAnnotations.All(snapshot => right.AddedAnnotations.Any(candidate => ReferenceEquals(candidate.Target, snapshot.Target))) &&
        left.RemovedMeasurements.All(snapshot => right.RemovedMeasurements.Any(candidate => ReferenceEquals(candidate.Target, snapshot.Target))) &&
        left.RemovedAnnotations.All(snapshot => right.RemovedAnnotations.Any(candidate => ReferenceEquals(candidate.Target, snapshot.Target)));

    private void ClearViewportUndoStack() =>
        _undoStack.Clear();

    private sealed record ViewportUndoAction(
        string Key,
        string Status,
        List<MeasurementPointUndo> MeasurementPoints,
        List<AnnotationPointUndo> AnnotationPoints,
        List<MeasurementPresenceUndo> AddedMeasurements,
        List<AnnotationPresenceUndo> AddedAnnotations,
        List<MeasurementPresenceUndo> RemovedMeasurements,
        List<AnnotationPresenceUndo> RemovedAnnotations);

    private sealed record MeasurementPointUndo(Measurement Target, List<SKPoint> Points, List<List<SKPoint>> Holes);

    private sealed record AnnotationPointUndo(PageAnnotation Target, List<SKPoint> Points);

    private sealed record MeasurementPresenceUndo(Measurement Target, int Index);

    private sealed record AnnotationPresenceUndo(PageAnnotation Target, int Index);
}
