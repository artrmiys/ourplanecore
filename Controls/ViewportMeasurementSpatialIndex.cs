using System;
using System.Collections.Generic;
using OurPlaneCore;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed class ViewportMeasurementSpatialIndex
{
    private const int TargetCellsAcross = 64;
    private const int MaxCellsPerEntry = 512;
    private const int MaxCellsPerSegment = 256;
    private const int MaxQueryCells = 1024;
    private const float MinimumCellSize = 64f;

    private readonly List<Entry> _entries = [];
    private readonly Dictionary<long, List<int>> _cells = [];
    private readonly List<int> _broadEntries = [];
    private readonly List<VertexEntry> _vertexEntries = [];
    private readonly Dictionary<long, List<int>> _vertexCells = [];
    private readonly List<SegmentEntry> _segmentEntries = [];
    private readonly Dictionary<long, List<int>> _segmentCells = [];
    private readonly List<int> _broadSegments = [];
    private readonly float _cellSize;

    public ViewportMeasurementSpatialIndex(IReadOnlyList<Measurement> measurements)
    {
        SKRect extent = BuildEntries(measurements);
        _cellSize = SelectCellSize(extent);
        BuildCells();
        BuildGeometryCells();
    }

    public IReadOnlyList<Measurement> Query(SKRect rect)
    {
        if (_entries.Count == 0)
            return Array.Empty<Measurement>();

        SKRect normalized = Normalize(rect);
        if (QueryCellCount(normalized) > MaxQueryCells)
            return QueryAll(normalized);

        var entryIndexes = new HashSet<int>();
        foreach (int index in _broadEntries)
            entryIndexes.Add(index);

        int minX = CellX(normalized.Left);
        int maxX = CellX(normalized.Right);
        int minY = CellY(normalized.Top);
        int maxY = CellY(normalized.Bottom);
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (!_cells.TryGetValue(CellKey(x, y), out List<int>? indexes))
                    continue;

                foreach (int index in indexes)
                    entryIndexes.Add(index);
            }
        }

        if (entryIndexes.Count == 0)
            return Array.Empty<Measurement>();

        var ordered = new List<Entry>(entryIndexes.Count);
        foreach (int index in entryIndexes)
        {
            Entry entry = _entries[index];
            if (RectsIntersect(entry.Bounds, normalized))
                ordered.Add(entry);
        }

        if (ordered.Count == 0)
            return Array.Empty<Measurement>();

        ordered.Sort((left, right) => left.Order.CompareTo(right.Order));
        var result = new List<Measurement>(ordered.Count);
        foreach (Entry entry in ordered)
            result.Add(entry.Measurement);
        return result;
    }

    public IReadOnlyList<ViewportMeasurementVertexCandidate> QueryVertices(SKRect rect)
    {
        if (_vertexEntries.Count == 0)
            return Array.Empty<ViewportMeasurementVertexCandidate>();

        SKRect normalized = Normalize(rect);
        var indexes = QueryCellIndexes(normalized, _vertexCells, includeBroadIndexes: null);
        if (indexes.Count == 0)
            return Array.Empty<ViewportMeasurementVertexCandidate>();

        var ordered = new List<VertexEntry>(indexes.Count);
        foreach (int index in indexes)
        {
            VertexEntry entry = _vertexEntries[index];
            if (RectContains(normalized, entry.Point))
                ordered.Add(entry);
        }

        if (ordered.Count == 0)
            return Array.Empty<ViewportMeasurementVertexCandidate>();

        ordered.Sort((left, right) => left.Order.CompareTo(right.Order));
        var result = new List<ViewportMeasurementVertexCandidate>(ordered.Count);
        foreach (VertexEntry entry in ordered)
        {
            result.Add(new ViewportMeasurementVertexCandidate(
                entry.Measurement,
                entry.GlobalIndex,
                entry.Point));
        }

        return result;
    }

    public IReadOnlyList<ViewportMeasurementSegmentCandidate> QuerySegments(SKRect rect)
    {
        if (_segmentEntries.Count == 0)
            return Array.Empty<ViewportMeasurementSegmentCandidate>();

        SKRect normalized = Normalize(rect);
        var indexes = QueryCellIndexes(normalized, _segmentCells, _broadSegments);
        if (indexes.Count == 0)
            return Array.Empty<ViewportMeasurementSegmentCandidate>();

        var ordered = new List<SegmentEntry>(indexes.Count);
        foreach (int index in indexes)
        {
            SegmentEntry entry = _segmentEntries[index];
            if (RectsIntersect(entry.Bounds, normalized))
                ordered.Add(entry);
        }

        if (ordered.Count == 0)
            return Array.Empty<ViewportMeasurementSegmentCandidate>();

        ordered.Sort((left, right) => left.Order.CompareTo(right.Order));
        var result = new List<ViewportMeasurementSegmentCandidate>(ordered.Count);
        foreach (SegmentEntry entry in ordered)
        {
            result.Add(new ViewportMeasurementSegmentCandidate(
                entry.Measurement,
                entry.Start,
                entry.End));
        }

        return result;
    }

    private SKRect BuildEntries(IReadOnlyList<Measurement> measurements)
    {
        float left = float.PositiveInfinity;
        float top = float.PositiveInfinity;
        float right = float.NegativeInfinity;
        float bottom = float.NegativeInfinity;
        bool hasBounds = false;

        for (int i = 0; i < measurements.Count; i++)
        {
            Measurement measurement = measurements[i];
            if (!TryMeasurementBounds(measurement, out SKRect bounds))
                continue;

            _entries.Add(new Entry(measurement, i, bounds));
            IncludeBounds(bounds, ref left, ref top, ref right, ref bottom);
            hasBounds = true;
        }

        return hasBounds ? new SKRect(left, top, right, bottom) : SKRect.Empty;
    }

    private void BuildCells()
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            Entry entry = _entries[i];
            int minX = CellX(entry.Bounds.Left);
            int maxX = CellX(entry.Bounds.Right);
            int minY = CellY(entry.Bounds.Top);
            int maxY = CellY(entry.Bounds.Bottom);
            long cellCount = (long)(maxX - minX + 1) * (maxY - minY + 1);
            if (cellCount > MaxCellsPerEntry)
            {
                _broadEntries.Add(i);
                continue;
            }

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    long key = CellKey(x, y);
                    if (!_cells.TryGetValue(key, out List<int>? indexes))
                    {
                        indexes = [];
                        _cells[key] = indexes;
                    }

                    indexes.Add(i);
                }
            }
        }
    }

    private void BuildGeometryCells()
    {
        int vertexOrder = 0;
        int segmentOrder = 0;
        foreach (Entry entry in _entries)
        {
            AddMeasurementVertices(entry.Measurement, ref vertexOrder);
            AddMeasurementSegments(entry.Measurement, ref segmentOrder);
        }

        for (int i = 0; i < _vertexEntries.Count; i++)
        {
            VertexEntry entry = _vertexEntries[i];
            AddCellIndex(_vertexCells, CellKey(CellX(entry.Point.X), CellY(entry.Point.Y)), i);
        }

        for (int i = 0; i < _segmentEntries.Count; i++)
        {
            SegmentEntry entry = _segmentEntries[i];
            int minX = CellX(entry.Bounds.Left);
            int maxX = CellX(entry.Bounds.Right);
            int minY = CellY(entry.Bounds.Top);
            int maxY = CellY(entry.Bounds.Bottom);
            long cellCount = (long)(maxX - minX + 1) * (maxY - minY + 1);
            if (cellCount > MaxCellsPerSegment)
            {
                _broadSegments.Add(i);
                continue;
            }

            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                    AddCellIndex(_segmentCells, CellKey(x, y), i);
        }
    }

    private void AddMeasurementVertices(Measurement measurement, ref int order)
    {
        int globalIndex = 0;
        for (int i = 0; i < measurement.Points.Count; i++)
            _vertexEntries.Add(new VertexEntry(measurement, globalIndex++, measurement.Points[i], order++));

        foreach (IReadOnlyList<SKPoint> hole in measurement.Holes)
            for (int i = 0; i < hole.Count; i++)
                _vertexEntries.Add(new VertexEntry(measurement, globalIndex++, hole[i], order++));
    }

    private void AddMeasurementSegments(Measurement measurement, ref int order)
    {
        bool isArea = string.Equals(measurement.MType, "area", StringComparison.OrdinalIgnoreCase);
        bool isLine = string.Equals(measurement.MType, "line", StringComparison.OrdinalIgnoreCase);
        if (!isArea && !isLine)
            return;

        AddSegments(measurement, measurement.Points, isArea, ref order);
        if (!isArea)
            return;

        foreach (IReadOnlyList<SKPoint> hole in measurement.Holes)
            AddSegments(measurement, hole, closed: true, ref order);
    }

    private void AddSegments(
        Measurement measurement,
        IReadOnlyList<SKPoint> points,
        bool closed,
        ref int order)
    {
        for (int i = 1; i < points.Count; i++)
            AddSegment(measurement, points[i - 1], points[i], ref order);

        if (closed && points.Count > 2)
            AddSegment(measurement, points[^1], points[0], ref order);
    }

    private void AddSegment(Measurement measurement, SKPoint start, SKPoint end, ref int order)
    {
        SKRect bounds = SegmentBounds(start, end);
        _segmentEntries.Add(new SegmentEntry(measurement, start, end, bounds, order++));
    }

    private HashSet<int> QueryCellIndexes(
        SKRect rect,
        Dictionary<long, List<int>> cells,
        IReadOnlyList<int>? includeBroadIndexes)
    {
        var indexes = new HashSet<int>();
        if (includeBroadIndexes != null)
            foreach (int index in includeBroadIndexes)
                indexes.Add(index);

        int minX = CellX(rect.Left);
        int maxX = CellX(rect.Right);
        int minY = CellY(rect.Top);
        int maxY = CellY(rect.Bottom);
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (!cells.TryGetValue(CellKey(x, y), out List<int>? cellIndexes))
                    continue;

                foreach (int index in cellIndexes)
                    indexes.Add(index);
            }
        }

        return indexes;
    }

    private static void AddCellIndex(Dictionary<long, List<int>> cells, long key, int index)
    {
        if (!cells.TryGetValue(key, out List<int>? indexes))
        {
            indexes = [];
            cells[key] = indexes;
        }

        indexes.Add(index);
    }

    private IReadOnlyList<Measurement> QueryAll(SKRect rect)
    {
        var result = new List<Measurement>();
        foreach (Entry entry in _entries)
        {
            if (RectsIntersect(entry.Bounds, rect))
                result.Add(entry.Measurement);
        }

        return result;
    }

    private long QueryCellCount(SKRect rect)
    {
        int minX = CellX(rect.Left);
        int maxX = CellX(rect.Right);
        int minY = CellY(rect.Top);
        int maxY = CellY(rect.Bottom);
        return (long)(maxX - minX + 1) * (maxY - minY + 1);
    }

    private int CellX(float x) => (int)MathF.Floor(x / _cellSize);

    private int CellY(float y) => (int)MathF.Floor(y / _cellSize);

    private static float SelectCellSize(SKRect extent)
    {
        if (extent.IsEmpty)
            return MinimumCellSize;

        float span = Math.Max(extent.Width, extent.Height);
        if (span <= 0 || float.IsNaN(span) || float.IsInfinity(span))
            return MinimumCellSize;

        return Math.Max(MinimumCellSize, span / TargetCellsAcross);
    }

    private static bool TryMeasurementBounds(Measurement measurement, out SKRect bounds)
    {
        bounds = SKRect.Empty;
        if (measurement.Points.Count == 0)
            return false;

        float left = float.PositiveInfinity;
        float top = float.PositiveInfinity;
        float right = float.NegativeInfinity;
        float bottom = float.NegativeInfinity;
        foreach (SKPoint point in measurement.Points)
            IncludePoint(point, ref left, ref top, ref right, ref bottom);

        foreach (IReadOnlyList<SKPoint> hole in measurement.Holes)
            foreach (SKPoint point in hole)
                IncludePoint(point, ref left, ref top, ref right, ref bottom);

        bounds = new SKRect(left, top, right, bottom);
        return true;
    }

    private static void IncludePoint(
        SKPoint point,
        ref float left,
        ref float top,
        ref float right,
        ref float bottom)
    {
        left = Math.Min(left, point.X);
        top = Math.Min(top, point.Y);
        right = Math.Max(right, point.X);
        bottom = Math.Max(bottom, point.Y);
    }

    private static void IncludeBounds(
        SKRect bounds,
        ref float left,
        ref float top,
        ref float right,
        ref float bottom)
    {
        left = Math.Min(left, bounds.Left);
        top = Math.Min(top, bounds.Top);
        right = Math.Max(right, bounds.Right);
        bottom = Math.Max(bottom, bounds.Bottom);
    }

    private static bool RectsIntersect(SKRect a, SKRect b) =>
        a.Left <= b.Right &&
        a.Right >= b.Left &&
        a.Top <= b.Bottom &&
        a.Bottom >= b.Top;

    private static bool RectContains(SKRect rect, SKPoint point) =>
        point.X >= rect.Left &&
        point.X <= rect.Right &&
        point.Y >= rect.Top &&
        point.Y <= rect.Bottom;

    private static SKRect SegmentBounds(SKPoint start, SKPoint end) =>
        new(
            Math.Min(start.X, end.X),
            Math.Min(start.Y, end.Y),
            Math.Max(start.X, end.X),
            Math.Max(start.Y, end.Y));

    private static SKRect Normalize(SKRect rect)
    {
        float left = Math.Min(rect.Left, rect.Right);
        float right = Math.Max(rect.Left, rect.Right);
        float top = Math.Min(rect.Top, rect.Bottom);
        float bottom = Math.Max(rect.Top, rect.Bottom);
        return new SKRect(left, top, right, bottom);
    }

    private static long CellKey(int x, int y) =>
        ((long)x << 32) ^ (uint)y;

    private readonly record struct Entry(Measurement Measurement, int Order, SKRect Bounds);
    private readonly record struct VertexEntry(Measurement Measurement, int GlobalIndex, SKPoint Point, int Order);
    private readonly record struct SegmentEntry(Measurement Measurement, SKPoint Start, SKPoint End, SKRect Bounds, int Order);
}

public readonly record struct ViewportMeasurementVertexCandidate(
    Measurement Measurement,
    int GlobalIndex,
    SKPoint Point);

public readonly record struct ViewportMeasurementSegmentCandidate(
    Measurement Measurement,
    SKPoint Start,
    SKPoint End);
