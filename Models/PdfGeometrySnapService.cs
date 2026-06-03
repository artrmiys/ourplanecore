using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SkiaSharp;

namespace OurPlaneCore;

public sealed record PdfGeometrySnapPoint(SKPoint Point, string Kind, string LayerName = "");
public sealed record PdfGeometrySnapSegment(SKPoint Start, SKPoint End, string Kind, string LayerName = "");

public sealed class PdfGeometrySnapResult
{
    public IReadOnlyList<PdfGeometrySnapPoint> Points { get; init; } = [];
    public IReadOnlyList<PdfGeometrySnapSegment> Segments { get; init; } = [];
}

public sealed class PdfSnapPointIndex
{
    public static readonly PdfSnapPointIndex Empty = new([]);

    private const float CellSizePt = 24f;
    private readonly Dictionary<(int X, int Y), List<PdfGeometrySnapPoint>> _pointCells = [];
    private readonly Dictionary<(int X, int Y), List<int>> _segmentCells = [];
    private readonly List<PdfGeometrySnapSegment> _segments = [];
    private int _pointCount;

    public PdfSnapPointIndex(IEnumerable<PdfGeometrySnapPoint> points)
        : this(points, [])
    {
    }

    public PdfSnapPointIndex(IEnumerable<PdfGeometrySnapPoint> points, IEnumerable<PdfGeometrySnapSegment> segments)
    {
        foreach (PdfGeometrySnapPoint point in points)
            AddPoint(point);

        foreach (PdfGeometrySnapSegment segment in segments)
            AddSegment(segment);
    }

    public int Count => _pointCount + _segments.Count;

    public bool TryFind(SKPoint rawPdf, float tolerancePt, out PdfGeometrySnapPoint snap)
    {
        snap = default!;
        if (tolerancePt <= 0 || Count == 0)
            return false;

        int minX = GridCoordinate(rawPdf.X - tolerancePt);
        int maxX = GridCoordinate(rawPdf.X + tolerancePt);
        int minY = GridCoordinate(rawPdf.Y - tolerancePt);
        int maxY = GridCoordinate(rawPdf.Y + tolerancePt);
        float bestDistance = tolerancePt * tolerancePt;
        int bestPriority = int.MaxValue;
        PdfGeometrySnapPoint bestSnap = default!;
        bool found = false;
        HashSet<int>? seenSegments = null;

        void Consider(SKPoint candidate, string kind, string layerName)
        {
            float distance = DistanceSquared(rawPdf, candidate);
            int priority = KindPriority(kind);
            if (distance >= bestDistance && (distance > bestDistance || priority >= bestPriority))
                return;

            bestDistance = distance;
            bestPriority = priority;
            bestSnap = new PdfGeometrySnapPoint(candidate, kind, layerName);
            found = true;
        }

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (_pointCells.TryGetValue((x, y), out var pointBucket))
                {
                    foreach (PdfGeometrySnapPoint candidate in pointBucket)
                        Consider(candidate.Point, candidate.Kind, candidate.LayerName);
                }

                if (!_segmentCells.TryGetValue((x, y), out var segmentBucket))
                    continue;

                seenSegments ??= [];
                foreach (int segmentIndex in segmentBucket)
                {
                    if (!seenSegments.Add(segmentIndex))
                        continue;

                    PdfGeometrySnapSegment segment = _segments[segmentIndex];
                    SKPoint closest = ClosestPointOnSegment(rawPdf, segment.Start, segment.End);
                    Consider(closest, segment.Kind, segment.LayerName);
                }
            }
        }

        snap = bestSnap;
        return found;
    }

    private void AddPoint(PdfGeometrySnapPoint point)
    {
        var key = CellKey(point.Point);
        if (!_pointCells.TryGetValue(key, out var bucket))
        {
            bucket = [];
            _pointCells[key] = bucket;
        }

        bucket.Add(point);
        _pointCount++;
    }

    private void AddSegment(PdfGeometrySnapSegment segment)
    {
        if (DistanceSquared(segment.Start, segment.End) <= 0.001f)
            return;

        int segmentIndex = _segments.Count;
        _segments.Add(segment);

        float length = MeasurementGeometry.Distance(segment.Start, segment.End);
        int steps = Math.Max(1, (int)MathF.Ceiling(length / CellSizePt));
        var cells = new HashSet<(int X, int Y)>();
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            var point = new SKPoint(
                segment.Start.X + (segment.End.X - segment.Start.X) * t,
                segment.Start.Y + (segment.End.Y - segment.Start.Y) * t);
            cells.Add(CellKey(point));
        }

        foreach (var cell in cells)
        {
            if (!_segmentCells.TryGetValue(cell, out var bucket))
            {
                bucket = [];
                _segmentCells[cell] = bucket;
            }

            bucket.Add(segmentIndex);
        }
    }

    private static (int X, int Y) CellKey(SKPoint point) =>
        (GridCoordinate(point.X), GridCoordinate(point.Y));

    private static int GridCoordinate(float value) =>
        (int)MathF.Floor(value / CellSizePt);

    private static float DistanceSquared(SKPoint a, SKPoint b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static SKPoint ClosestPointOnSegment(SKPoint point, SKPoint start, SKPoint end)
    {
        float vx = end.X - start.X;
        float vy = end.Y - start.Y;
        float lengthSq = vx * vx + vy * vy;
        if (lengthSq <= 0.001f)
            return start;

        float t = ((point.X - start.X) * vx + (point.Y - start.Y) * vy) / lengthSq;
        t = Math.Clamp(t, 0f, 1f);
        return new SKPoint(start.X + vx * t, start.Y + vy * t);
    }

    private static int KindPriority(string kind) => kind.ToLowerInvariant() switch
    {
        "pdf-corner" => 0,
        "pdf-point" => 1,
        _ => 2,
    };
}

public static class PdfGeometrySnapService
{
    public static Task<(bool Ok, PdfGeometrySnapResult Result, string Error)> TryReadSnapPointsAsync(
        string pdfPath,
        int pageIndex,
        IReadOnlyList<PdfLayerInfo>? visibleLayers,
        bool blackOnly = false) =>
        Task.Run(() =>
        {
            bool ok = TryReadSnapPoints(pdfPath, pageIndex, visibleLayers, blackOnly, out PdfGeometrySnapResult result, out string error);
            return (ok, result, error);
        });

    internal static bool TryReadSnapPoints(
        string pdfPath,
        int pageIndex,
        IReadOnlyList<PdfLayerInfo>? visibleLayers,
        bool blackOnly,
        out PdfGeometrySnapResult result,
        out string error)
    {
        result = new PdfGeometrySnapResult();
        error = "";

        try
        {
            var request = new PdfSnapRequest
            {
                Pdf = pdfPath,
                Page = pageIndex,
                MaxPoints = 30000,
                BlackOnly = blackOnly,
                VisibleLayers = visibleLayers?.Select(PdfSnapLayerDto.FromInfo).ToList(),
            };

            if (!PdfLayerRenderService.TryInvokeHelper("pdfsnap", request, out PdfSnapResponse? response, out error))
                return false;

            if (response == null || !response.Ok)
            {
                error = response?.Error ?? "PyMuPDF did not return a PDF snap response.";
                return false;
            }

            result = new PdfGeometrySnapResult
            {
                Points = response.Points
                    .Where(point => IsFinite(point.X) && IsFinite(point.Y))
                    .Select(point => new PdfGeometrySnapPoint(
                        new SKPoint(point.X, point.Y),
                        string.IsNullOrWhiteSpace(point.Kind) ? "pdf-point" : point.Kind,
                        point.LayerName ?? ""))
                    .ToList(),
                Segments = response.Segments
                    .Where(segment =>
                        IsFinite(segment.X0) &&
                        IsFinite(segment.Y0) &&
                        IsFinite(segment.X1) &&
                        IsFinite(segment.Y1))
                    .Select(segment => new PdfGeometrySnapSegment(
                        new SKPoint(segment.X0, segment.Y0),
                        new SKPoint(segment.X1, segment.Y1),
                        string.IsNullOrWhiteSpace(segment.Kind) ? "pdf-line" : segment.Kind,
                        segment.LayerName ?? ""))
                    .ToList(),
            };
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"TryReadSnapPoints failed for {pdfPath} page {pageIndex}");
            error = ex.Message;
            return false;
        }
    }

    private static bool IsFinite(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value);

    private sealed class PdfSnapRequest
    {
        public string Pdf { get; set; } = "";
        public int Page { get; set; }
        public int MaxPoints { get; set; } = 30000;
        public bool BlackOnly { get; set; }
        public List<PdfSnapLayerDto>? VisibleLayers { get; set; }
    }

    private sealed class PdfSnapResponse
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = "";
        public List<PdfSnapPointDto> Points { get; set; } = [];
        public List<PdfSnapSegmentDto> Segments { get; set; } = [];
    }

    private sealed class PdfSnapPointDto
    {
        public float X { get; set; }
        public float Y { get; set; }
        public string Kind { get; set; } = "";
        public string LayerName { get; set; } = "";
    }

    private sealed class PdfSnapSegmentDto
    {
        public float X0 { get; set; }
        public float Y0 { get; set; }
        public float X1 { get; set; }
        public float Y1 { get; set; }
        public string Kind { get; set; } = "";
        public string LayerName { get; set; } = "";
    }

    private sealed class PdfSnapLayerDto
    {
        public int Xref { get; set; }
        public string Name { get; set; } = "";
        public bool On { get; set; }

        public static PdfSnapLayerDto FromInfo(PdfLayerInfo info) => new()
        {
            Xref = info.Number,
            Name = info.Name,
            On = info.IsOn,
        };
    }
}
