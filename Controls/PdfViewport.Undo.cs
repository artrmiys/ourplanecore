using System;
using System.Collections.Generic;
using System.Linq;
using OurPlanCore;
using SkiaSharp;

namespace OurPlanCore.Controls;

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
            .Where(measurement => _measurementSet.Contains(measurement))
            .Select(measurement => new MeasurementPointUndo(
                measurement,
                measurement.Points.ToList(),
                CloneHoles(measurement.Holes),
                measurement.JoistDirectionDegrees,
                CloneExtraJoists(measurement.ExtraJoists)))
            .ToList();
        var annotationSnapshots = annotations
            .Where(annotation => _annotations.Contains(annotation))
            .Select(annotation => new AnnotationPointUndo(annotation, annotation.Points.ToList(), annotation.Text))
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
        PushMeasurementUndoSnapshot(
            measurement,
            beforePoints,
            beforeHoles,
            CloneExtraJoists(measurement.ExtraJoists),
            status,
            key);
    }

    private void PushMeasurementUndoSnapshot(
        Measurement measurement,
        IReadOnlyList<SKPoint> beforePoints,
        IReadOnlyList<IReadOnlyList<SKPoint>> beforeHoles,
        IReadOnlyList<JoistExtraSegment> beforeExtraJoists,
        string status,
        string key = "")
    {
        if (_applyingViewportUndo || !_measurementSet.Contains(measurement))
            return;

        PushUndoAction(new ViewportUndoAction(
            key,
            status,
            [new MeasurementPointUndo(
                measurement,
                beforePoints.ToList(),
                CloneHoles(beforeHoles),
                measurement.JoistDirectionDegrees,
                CloneExtraJoists(beforeExtraJoists))],
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
            [new AnnotationPointUndo(annotation, beforePoints.ToList(), annotation.Text)],
            [],
            [],
            [],
            []), coalesce: false);
    }

    private void PushAnnotationTextUndoSnapshot(PageAnnotation annotation, string beforeText, string status)
    {
        if (_applyingViewportUndo || !_annotations.Contains(annotation))
            return;

        PushUndoAction(new ViewportUndoAction(
            "",
            status,
            [],
            [new AnnotationPointUndo(annotation, annotation.Points.ToList(), beforeText)],
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
        PushMeasurementUndoSnapshots(
            beforePoints,
            beforeHoles,
            new Dictionary<Measurement, List<JoistExtraSegment>>(),
            status,
            key);
    }

    private void PushMeasurementUndoSnapshots(
        IReadOnlyDictionary<Measurement, List<SKPoint>> beforePoints,
        IReadOnlyDictionary<Measurement, List<List<SKPoint>>> beforeHoles,
        IReadOnlyDictionary<Measurement, List<JoistExtraSegment>> beforeExtraJoists,
        string status,
        string key = "")
    {
        if (_applyingViewportUndo || beforePoints.Count == 0)
            return;

        var snapshots = beforePoints
            .Where(pair => _measurementSet.Contains(pair.Key))
            .Select(pair => new MeasurementPointUndo(
                pair.Key,
                pair.Value.ToList(),
                beforeHoles.TryGetValue(pair.Key, out var holes)
                    ? CloneHoles(holes)
                    : CloneHoles(pair.Key.Holes),
                pair.Key.JoistDirectionDegrees,
                beforeExtraJoists.TryGetValue(pair.Key, out var extraJoists)
                    ? CloneExtraJoists(extraJoists)
                    : CloneExtraJoists(pair.Key.ExtraJoists)))
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
            new Dictionary<Measurement, double>(),
            new Dictionary<Measurement, List<JoistExtraSegment>>(),
            annotationBeforePoints,
            status,
            key);
    }

    private void PushGeometryUndoSnapshotFromOriginals(
        IReadOnlyDictionary<Measurement, List<SKPoint>> measurementBeforePoints,
        IReadOnlyDictionary<Measurement, List<List<SKPoint>>> measurementBeforeHoles,
        IReadOnlyDictionary<Measurement, double> measurementBeforeJoistDirections,
        IReadOnlyDictionary<PageAnnotation, List<SKPoint>> annotationBeforePoints,
        string status,
        string key = "")
    {
        PushGeometryUndoSnapshotFromOriginals(
            measurementBeforePoints,
            measurementBeforeHoles,
            measurementBeforeJoistDirections,
            new Dictionary<Measurement, List<JoistExtraSegment>>(),
            annotationBeforePoints,
            status,
            key);
    }

    private void PushGeometryUndoSnapshotFromOriginals(
        IReadOnlyDictionary<Measurement, List<SKPoint>> measurementBeforePoints,
        IReadOnlyDictionary<Measurement, List<List<SKPoint>>> measurementBeforeHoles,
        IReadOnlyDictionary<Measurement, double> measurementBeforeJoistDirections,
        IReadOnlyDictionary<Measurement, List<JoistExtraSegment>> measurementBeforeExtraJoists,
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
            .Where(pair => _measurementSet.Contains(pair.Key))
            .Select(pair => new MeasurementPointUndo(
                pair.Key,
                pair.Value.ToList(),
                measurementBeforeHoles.TryGetValue(pair.Key, out var holes)
                    ? CloneHoles(holes)
                    : CloneHoles(pair.Key.Holes),
                measurementBeforeJoistDirections.TryGetValue(pair.Key, out double direction)
                    ? direction
                    : pair.Key.JoistDirectionDegrees,
                measurementBeforeExtraJoists.TryGetValue(pair.Key, out var extraJoists)
                    ? CloneExtraJoists(extraJoists)
                    : CloneExtraJoists(pair.Key.ExtraJoists)))
            .ToList();
        var annotationSnapshots = annotationBeforePoints
            .Where(pair => _annotations.Contains(pair.Key))
            .Select(pair => new AnnotationPointUndo(pair.Key, pair.Value.ToList(), pair.Key.Text))
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

    public void RegisterAddedMeasurementsUndo(IEnumerable<Measurement> added, string status)
    {
        PushAddedMeasurementsUndo(added, status);
    }

    private void PushAddedMeasurementsUndo(IEnumerable<Measurement> added, string status)
    {
        if (_applyingViewportUndo)
            return;

        var indexByMeasurement = new Dictionary<Measurement, int>();
        for (int i = 0; i < _measurements.Count; i++)
            indexByMeasurement.TryAdd(_measurements[i], i);

        var seen = new HashSet<Measurement>();
        var snapshots = new List<MeasurementPresenceUndo>();
        foreach (Measurement measurement in added)
        {
            if (!seen.Add(measurement) ||
                !_measurementSet.Contains(measurement) ||
                !indexByMeasurement.TryGetValue(measurement, out int index))
            {
                continue;
            }

            snapshots.Add(new MeasurementPresenceUndo(measurement, index));
        }
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

        var indexByMeasurement = new Dictionary<Measurement, int>();
        for (int i = 0; i < _measurements.Count; i++)
            indexByMeasurement.TryAdd(_measurements[i], i);

        var seen = new HashSet<Measurement>();
        var snapshots = new List<MeasurementPresenceUndo>();
        foreach (Measurement measurement in removed)
        {
            if (!seen.Add(measurement) ||
                !_measurementSet.Contains(measurement) ||
                !indexByMeasurement.TryGetValue(measurement, out int index))
            {
                continue;
            }

            snapshots.Add(new MeasurementPresenceUndo(measurement, index));
        }
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

    private void PushMixedMeasurementUndo(
        IReadOnlyDictionary<Measurement, List<SKPoint>> beforePoints,
        IReadOnlyDictionary<Measurement, List<List<SKPoint>>> beforeHoles,
        IReadOnlyDictionary<Measurement, int> removedMeasurements,
        IReadOnlyList<Measurement> addedMeasurements,
        string status,
        string key = "")
    {
        PushMixedMeasurementUndo(
            beforePoints,
            beforeHoles,
            new Dictionary<Measurement, List<JoistExtraSegment>>(),
            removedMeasurements,
            addedMeasurements,
            status,
            key);
    }

    private void PushMixedMeasurementUndo(
        IReadOnlyDictionary<Measurement, List<SKPoint>> beforePoints,
        IReadOnlyDictionary<Measurement, List<List<SKPoint>>> beforeHoles,
        IReadOnlyDictionary<Measurement, List<JoistExtraSegment>> beforeExtraJoists,
        IReadOnlyDictionary<Measurement, int> removedMeasurements,
        IReadOnlyList<Measurement> addedMeasurements,
        string status,
        string key = "")
    {
        if (_applyingViewportUndo)
            return;

        var pointSnapshots = beforePoints
            .Where(pair => _measurementSet.Contains(pair.Key))
            .Select(pair => new MeasurementPointUndo(
                pair.Key,
                pair.Value.ToList(),
                beforeHoles.TryGetValue(pair.Key, out var holes)
                    ? CloneHoles(holes)
                    : CloneHoles(pair.Key.Holes),
                pair.Key.JoistDirectionDegrees,
                beforeExtraJoists.TryGetValue(pair.Key, out var extraJoists)
                    ? CloneExtraJoists(extraJoists)
                    : CloneExtraJoists(pair.Key.ExtraJoists)))
            .ToList();

        var addedSnapshots = addedMeasurements
            .Where(measurement => _measurementSet.Contains(measurement))
            .Select(measurement => new MeasurementPresenceUndo(measurement, _measurements.IndexOf(measurement)))
            .Where(snapshot => snapshot.Index >= 0)
            .ToList();

        var removedSnapshots = removedMeasurements
            .Select(pair => new MeasurementPresenceUndo(pair.Key, pair.Value))
            .ToList();

        if (pointSnapshots.Count == 0 && addedSnapshots.Count == 0 && removedSnapshots.Count == 0)
            return;

        PushUndoAction(new ViewportUndoAction(
            key,
            status,
            pointSnapshots,
            [],
            addedSnapshots,
            [],
            removedSnapshots,
            []), coalesce: false);
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
            var removedAddedMeasurements = action.AddedMeasurements
                .Select(added => added.Target)
                .Where(target => _measurementSet.Contains(target))
                .Distinct()
                .ToList();
            if (removedAddedMeasurements.Count > 0)
            {
                var removedSet = new HashSet<Measurement>(removedAddedMeasurements);
                _measurements.RemoveAll(measurement => removedSet.Contains(measurement));
                foreach (Measurement measurement in removedAddedMeasurements)
                {
                    _measurementSet.Remove(measurement);
                    RemoveMeasurementFromPageIndex(measurement);
                    ForgetMeasurementState(measurement);
                }

                if (removedAddedMeasurements.Any(measurement =>
                        ReferenceEquals(_selectedMeasurement, measurement) ||
                        _selectedMeasurements.Contains(measurement)))
                {
                    ClearSelection();
                }

                NotifyMeasurementsRemoved(removedAddedMeasurements);
                changed = true;
            }

            foreach (AnnotationPresenceUndo added in action.AddedAnnotations)
            {
                if (!_annotations.Remove(added.Target))
                    continue;

                if (ReferenceEquals(_selectedAnnotation, added.Target) ||
                    _selectedAnnotations.Contains(added.Target))
                {
                    ClearAnnotationSelection();
                }
                PageAnnotationRemoved?.Invoke(added.Target);
                changed = true;
            }

            var restoredMeasurements = new List<Measurement>();
            foreach (MeasurementPresenceUndo removed in action.RemovedMeasurements.OrderBy(snapshot => snapshot.Index))
            {
                if (_measurementSet.Contains(removed.Target))
                    continue;

                _measurements.Insert(Math.Clamp(removed.Index, 0, _measurements.Count), removed.Target);
                _measurementSet.Add(removed.Target);
                IndexMeasurementByPage(removed.Target);
                restoredMeasurements.Add(removed.Target);
                changed = true;
            }
            NotifyMeasurementsAdded(restoredMeasurements);

            foreach (AnnotationPresenceUndo removed in action.RemovedAnnotations)
            {
                if (_annotations.Contains(removed.Target))
                    continue;

                _annotations.Insert(Math.Clamp(removed.Index, 0, _annotations.Count), removed.Target);
                PageAnnotationAdded?.Invoke(removed.Target);
                changed = true;
            }

            var changedMeasurements = new List<Measurement>();
            foreach (MeasurementPointUndo snapshot in action.MeasurementPoints)
            {
                if (!_measurementSet.Contains(snapshot.Target))
                    continue;

                RestorePoints(snapshot.Target.Points, snapshot.Points);
                RestoreHoles(snapshot.Target.Holes, snapshot.Holes);
                snapshot.Target.JoistDirectionDegrees = snapshot.JoistDirectionDegrees;
                RestoreExtraJoists(snapshot.Target.ExtraJoists, snapshot.ExtraJoists);
                PruneMeasurementVertexSelection(snapshot.Target);
                changedMeasurements.Add(snapshot.Target);
                changed = true;
            }
            NotifyMeasurementsChanged(changedMeasurements);

            foreach (AnnotationPointUndo snapshot in action.AnnotationPoints)
            {
                if (!_annotations.Contains(snapshot.Target))
                    continue;

                RestorePoints(snapshot.Target.Points, snapshot.Points);
                snapshot.Target.Text = snapshot.Text;
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

    private void NotifyMeasurementsAdded(IReadOnlyList<Measurement> measurements)
    {
        if (measurements.Count == 0)
            return;

        InvalidateActivePageMeasurementSpatialIndex();
        if (measurements.Count == 1 || MeasurementsAdded == null)
        {
            foreach (Measurement measurement in measurements)
                MeasurementAdded?.Invoke(measurement);
            return;
        }

        MeasurementsAdded.Invoke(measurements);
    }

    private void NotifyMeasurementsRemoved(IReadOnlyList<Measurement> measurements)
    {
        if (measurements.Count == 0)
            return;

        InvalidateActivePageMeasurementSpatialIndex();
        if (measurements.Count == 1 || MeasurementsRemoved == null)
        {
            foreach (Measurement measurement in measurements)
                MeasurementRemoved?.Invoke(measurement);
            return;
        }

        MeasurementsRemoved.Invoke(measurements);
    }

    private void NotifyMeasurementsChanged(IReadOnlyList<Measurement> measurements)
    {
        if (measurements.Count == 0)
            return;

        InvalidateActivePageMeasurementSpatialIndex();
        if (measurements.Count == 1 || MeasurementsChanged == null)
        {
            foreach (Measurement measurement in measurements)
                MeasurementChanged?.Invoke(measurement);
            return;
        }

        MeasurementsChanged.Invoke(measurements);
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

    private sealed record MeasurementPointUndo(
        Measurement Target,
        List<SKPoint> Points,
        List<List<SKPoint>> Holes,
        double JoistDirectionDegrees,
        List<JoistExtraSegment> ExtraJoists);

    private sealed record AnnotationPointUndo(PageAnnotation Target, List<SKPoint> Points, string Text);

    private sealed record MeasurementPresenceUndo(Measurement Target, int Index);

    private sealed record AnnotationPresenceUndo(PageAnnotation Target, int Index);
}
