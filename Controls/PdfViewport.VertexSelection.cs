using System;
using System.Collections.Generic;
using System.Linq;
using OurPlaneCore;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private readonly record struct MeasurementVertexRef(
        int GlobalIndex,
        int HoleIndex,
        int VertexIndex,
        SKPoint Point)
    {
        public bool IsHole => HoleIndex >= 0;
    }

    private void PrepareMeasurementVertexDragSelection(Measurement measurement, int vertexIndex)
    {
        bool keepGroup = IsMeasurementVertexSelected(measurement, vertexIndex) &&
                         SelectedVertexCount() > 0;
        if (keepGroup)
        {
            _selectedMeasurement = measurement;
            _selectedVertexIndex = vertexIndex;
            return;
        }

        SelectMeasurement(measurement, vertexIndex);
    }

    private bool TryToggleMeasurementVertexSelection(SKPoint pdf, bool removeMode)
    {
        if (!TryHitVertex(pdf, out Measurement measurement, out int vertexIndex) ||
            !CanEditMeasurementVertices(measurement))
        {
            return false;
        }

        if (removeMode && !IsMeasurementVertexSelected(measurement, vertexIndex))
        {
            // Shift-clicked a handle that is not selected: nothing to remove.
            PostStatus("Shift-click a selected handle to deselect it; Ctrl-click to select.");
            return true;
        }

        if (!_selectedMeasurements.Contains(measurement))
        {
            SetSelectedMeasurements([measurement], measurement, -1);
        }

        HashSet<int> indices = VertexSelectionSet(measurement, create: true);
        if (removeMode)
            indices.Remove(vertexIndex);
        else
            indices.Add(vertexIndex);
        if (indices.Count == 0)
            _selectedMeasurementVertexIndices.Remove(measurement);

        _selectedMeasurement = measurement;
        _selectedVertexIndex = IsMeasurementVertexSelected(measurement, vertexIndex)
            ? vertexIndex
            : LastSelectedVertexIndex();

        RequestRepaint();
        int selectedCount = SelectedVertexCount();
        PostStatus(selectedCount == 0
            ? $"{EntryTitle(measurement.MType)} selected. Ctrl-click handles to select, Shift-click to deselect."
            : $"Selected {selectedCount} vertex/vertices. Drag a selected handle to move (hold Shift = ortho); Delete removes them.");
        return true;
    }

    private bool TryDeleteSelectedMeasurementVertices()
    {
        var groups = _selectedMeasurementVertexIndices
            .Where(pair => _measurementSet.Contains(pair.Key) &&
                           IsMeasurementOnActivePage(pair.Key) &&
                           CanEditMeasurementVertices(pair.Key))
            .Select(pair => new
            {
                Measurement = pair.Key,
                Indices = pair.Value
                    .Where(index => IsValidMeasurementVertexIndex(pair.Key, index))
                    .Distinct()
                    .OrderByDescending(index => index)
                    .ToList(),
            })
            .Where(group => group.Indices.Count > 0)
            .ToList();
        if (groups.Count == 0)
            return false;

        var removeGroups = groups
            .Where(group => DeletesWholeCountMeasurement(group.Measurement, group.Indices))
            .ToList();
        var editGroups = groups
            .Where(group => !DeletesWholeCountMeasurement(group.Measurement, group.Indices))
            .ToList();

        foreach (var group in editGroups)
        {
            if (!CanDeleteMeasurementVertices(group.Measurement, group.Indices, out string error))
            {
                PostStatus(error);
                return true;
            }
        }

        PushMixedMeasurementUndo(
            editGroups.ToDictionary(group => group.Measurement, group => group.Measurement.Points.ToList()),
            editGroups.ToDictionary(group => group.Measurement, group => CloneHoles(group.Measurement.Holes)),
            removeGroups.ToDictionary(group => group.Measurement, group => _measurements.IndexOf(group.Measurement)),
            [],
            "delete selected vertices",
            "delete-vertices");
        int deletedCount = 0;
        foreach (var group in editGroups)
            deletedCount += DeleteMeasurementVertices(group.Measurement, group.Indices);

        var removedMeasurements = removeGroups
            .Select(group => group.Measurement)
            .Distinct()
            .ToList();
        foreach (Measurement measurement in removedMeasurements)
        {
            deletedCount += measurement.Points.Count;
            _measurements.Remove(measurement);
            _measurementSet.Remove(measurement);
            RemoveMeasurementFromPageIndex(measurement);
            ForgetMeasurementState(measurement);
        }

        NotifyMeasurementsChanged(editGroups.Select(group => group.Measurement).ToList());
        NotifyMeasurementsRemoved(removedMeasurements);

        if (removedMeasurements.Count > 0)
        {
            var removedSet = new HashSet<Measurement>(removedMeasurements);
            var remainingSelection = GetSelectedMeasurements()
                .Where(measurement => !removedSet.Contains(measurement))
                .ToList();
            SetSelectedMeasurements(remainingSelection, remainingSelection.LastOrDefault(), -1);
        }
        else
        {
            ClearMeasurementVertexSelection();
        }
        _selectedVertexIndex = -1;
        _dragMeasurementVertexOriginalPoints.Clear();
        RequestRepaint();
        PostStatus($"Deleted {deletedCount} selected vertex/vertices.");
        return true;
    }

    private static bool DeletesWholeCountMeasurement(Measurement measurement, IReadOnlyList<int> globalIndices)
    {
        if (measurement.MType != "point" || measurement.Points.Count == 0)
            return false;

        var selectedOuterVertices = globalIndices
            .Select(index => TryResolveMeasurementVertex(measurement, index, out MeasurementVertexRef vertex)
                ? vertex
                : (MeasurementVertexRef?)null)
            .Where(vertex => vertex.HasValue && !vertex.Value.IsHole)
            .Select(vertex => vertex!.Value.VertexIndex)
            .Distinct()
            .Count();
        return selectedOuterVertices >= measurement.Points.Count;
    }

    private IEnumerable<Measurement> ActiveVertexMeasurements() =>
        _selectedMeasurementVertexIndices
            .Where(pair => _measurementSet.Contains(pair.Key) &&
                           IsMeasurementOnActivePage(pair.Key) &&
                           pair.Value.Count > 0)
            .Select(pair => pair.Key)
            .ToList();

    private IEnumerable<int> ActiveMeasurementVertexIndices(Measurement measurement)
    {
        if (!_selectedMeasurementVertexIndices.TryGetValue(measurement, out HashSet<int>? indices))
            yield break;

        foreach (int index in indices.OrderBy(index => index))
        {
            if (IsValidMeasurementVertexIndex(measurement, index))
                yield return index;
        }
    }

    private HashSet<int> VertexSelectionSet(Measurement measurement, bool create)
    {
        if (_selectedMeasurementVertexIndices.TryGetValue(measurement, out HashSet<int>? indices))
            return indices;

        if (!create)
            return [];

        indices = [];
        _selectedMeasurementVertexIndices[measurement] = indices;
        return indices;
    }

    private bool IsMeasurementVertexSelected(Measurement measurement, int index) =>
        _selectedMeasurementVertexIndices.TryGetValue(measurement, out HashSet<int>? indices) &&
        indices.Contains(index);

    private int SelectedVertexCount() =>
        _selectedMeasurementVertexIndices.Values.Sum(indices => indices.Count);

    private int LastSelectedVertexIndex()
    {
        HashSet<int>? indices = _selectedMeasurement != null &&
                                _selectedMeasurementVertexIndices.TryGetValue(_selectedMeasurement, out HashSet<int>? selected)
            ? selected
            : null;
        return indices?.LastOrDefault(-1) ?? -1;
    }

    private int DraggedVertexCount() =>
        _dragMeasurementVertexOriginalPoints.Values.Sum(points => points.Count);

    private bool TryGetSelectedVertexDragAnchor(Measurement preferredMeasurement, out Measurement measurement, out int vertexIndex)
    {
        if (TryGetFirstSelectedVertex(preferredMeasurement, out vertexIndex))
        {
            measurement = preferredMeasurement;
            return true;
        }

        foreach (Measurement selectedMeasurement in ActiveVertexMeasurements())
        {
            if (TryGetFirstSelectedVertex(selectedMeasurement, out vertexIndex))
            {
                measurement = selectedMeasurement;
                return true;
            }
        }

        measurement = null!;
        vertexIndex = -1;
        return false;
    }

    private bool TryGetFirstSelectedVertex(Measurement measurement, out int vertexIndex)
    {
        foreach (int index in ActiveMeasurementVertexIndices(measurement))
        {
            vertexIndex = index;
            return true;
        }

        vertexIndex = -1;
        return false;
    }

    private void ClearMeasurementVertexSelection()
    {
        _selectedMeasurementVertexIndices.Clear();
    }

    private void PruneMeasurementVertexSelection(Measurement measurement)
    {
        if (_selectedMeasurementVertexIndices.TryGetValue(measurement, out HashSet<int>? indices))
        {
            indices.RemoveWhere(index => !IsValidMeasurementVertexIndex(measurement, index));
            if (indices.Count == 0)
                _selectedMeasurementVertexIndices.Remove(measurement);
        }

        if (ReferenceEquals(_selectedMeasurement, measurement) &&
            !IsValidMeasurementVertexIndex(measurement, _selectedVertexIndex))
        {
            _selectedVertexIndex = LastSelectedVertexIndex();
        }
    }

    private bool HasEditableMeasurementSelection() =>
        GetSelectedMeasurements().Any(measurement =>
            IsMeasurementOnActivePage(measurement) &&
            CanEditMeasurementVertices(measurement));

    private static bool CanEditMeasurementVertices(Measurement measurement) =>
        measurement.MType is "point" or "line" or "area";

    private static IEnumerable<MeasurementVertexRef> MeasurementVertices(Measurement measurement)
    {
        int globalIndex = 0;
        for (int i = 0; i < measurement.Points.Count; i++)
            yield return new MeasurementVertexRef(globalIndex++, -1, i, measurement.Points[i]);

        for (int h = 0; h < measurement.Holes.Count; h++)
        {
            var hole = measurement.Holes[h];
            for (int i = 0; i < hole.Count; i++)
                yield return new MeasurementVertexRef(globalIndex++, h, i, hole[i]);
        }
    }

    private static bool TryResolveMeasurementVertex(
        Measurement measurement,
        int globalIndex,
        out MeasurementVertexRef vertex)
    {
        foreach (MeasurementVertexRef candidate in MeasurementVertices(measurement))
        {
            if (candidate.GlobalIndex == globalIndex)
            {
                vertex = candidate;
                return true;
            }
        }

        vertex = default;
        return false;
    }

    private static bool IsValidMeasurementVertexIndex(Measurement measurement, int globalIndex) =>
        TryResolveMeasurementVertex(measurement, globalIndex, out _);

    private static SKPoint MeasurementVertexPoint(Measurement measurement, int globalIndex) =>
        TryResolveMeasurementVertex(measurement, globalIndex, out MeasurementVertexRef vertex)
            ? vertex.Point
            : default;

    private static void SetMeasurementVertex(Measurement measurement, int globalIndex, SKPoint point)
    {
        if (!TryResolveMeasurementVertex(measurement, globalIndex, out MeasurementVertexRef vertex))
            return;

        if (vertex.IsHole)
            measurement.Holes[vertex.HoleIndex][vertex.VertexIndex] = point;
        else
            measurement.Points[vertex.VertexIndex] = point;
    }

    private static bool CanDeleteMeasurementVertices(
        Measurement measurement,
        IReadOnlyList<int> globalIndices,
        out string error)
    {
        error = "";
        var vertices = globalIndices
            .Select(index => TryResolveMeasurementVertex(measurement, index, out MeasurementVertexRef vertex)
                ? vertex
                : (MeasurementVertexRef?)null)
            .Where(vertex => vertex.HasValue)
            .Select(vertex => vertex!.Value)
            .ToList();

        int outerDeleteCount = vertices.Count(vertex => !vertex.IsHole);
        int minOuterPoints = measurement.MType switch
        {
            "area" => 3,
            "line" => 2,
            "point" => 1,
            _ => 1,
        };
        if (measurement.Points.Count - outerDeleteCount < minOuterPoints)
        {
            error = measurement.MType == "area"
                ? "Area outer boundary needs at least 3 vertices."
                : measurement.MType == "point"
                    ? "Count needs at least 1 point."
                    : "Line needs at least 2 vertices.";
            return false;
        }

        foreach (var holeGroup in vertices.Where(vertex => vertex.IsHole).GroupBy(vertex => vertex.HoleIndex))
        {
            int holeCount = measurement.Holes[holeGroup.Key].Count;
            int remaining = holeCount - holeGroup.Select(vertex => vertex.VertexIndex).Distinct().Count();
            if (remaining is > 0 and < 3)
            {
                error = "Cut hole needs at least 3 vertices, or select all its vertices to remove the hole.";
                return false;
            }
        }

        return true;
    }

    private static int DeleteMeasurementVertices(Measurement measurement, IReadOnlyList<int> globalIndices)
    {
        var vertices = globalIndices
            .Select(index => TryResolveMeasurementVertex(measurement, index, out MeasurementVertexRef vertex)
                ? vertex
                : (MeasurementVertexRef?)null)
            .Where(vertex => vertex.HasValue)
            .Select(vertex => vertex!.Value)
            .ToList();
        int deletedCount = 0;

        foreach (int index in vertices
                     .Where(vertex => !vertex.IsHole)
                     .Select(vertex => vertex.VertexIndex)
                     .Distinct()
                     .OrderByDescending(index => index))
        {
            measurement.Points.RemoveAt(index);
            deletedCount++;
        }

        foreach (var holeGroup in vertices
                     .Where(vertex => vertex.IsHole)
                     .GroupBy(vertex => vertex.HoleIndex)
                     .OrderByDescending(group => group.Key))
        {
            var hole = measurement.Holes[holeGroup.Key];
            var vertexIndices = holeGroup
                .Select(vertex => vertex.VertexIndex)
                .Distinct()
                .OrderByDescending(index => index)
                .ToList();

            if (vertexIndices.Count == hole.Count)
            {
                measurement.Holes.RemoveAt(holeGroup.Key);
                deletedCount += vertexIndices.Count;
                continue;
            }

            foreach (int index in vertexIndices)
            {
                hole.RemoveAt(index);
                deletedCount++;
            }
        }

        return deletedCount;
    }
}
