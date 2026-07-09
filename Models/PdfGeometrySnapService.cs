using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using OurPlaneCore.Models;
using SkiaSharp;

namespace OurPlaneCore;

public sealed record PdfGeometrySnapPoint(SKPoint Point, string Kind, string LayerName = "");
public sealed record PdfGeometrySnapSegment(SKPoint Start, SKPoint End, string Kind, string LayerName = "", float StrokeWidth = 0f);
public readonly record struct PdfGeometrySnapSegmentHit(PdfGeometrySnapSegment Segment, int Index, float DistancePt, float LengthPt);

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
    public IReadOnlyList<PdfGeometrySnapSegment> Segments => _segments;

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

    public bool TryFindSegment(
        SKPoint rawPdf,
        float tolerancePt,
        out PdfGeometrySnapSegment segment,
        out float distancePt)
    {
        segment = default!;
        distancePt = 0;
        if (tolerancePt <= 0 || _segments.Count == 0)
            return false;

        int minX = GridCoordinate(rawPdf.X - tolerancePt);
        int maxX = GridCoordinate(rawPdf.X + tolerancePt);
        int minY = GridCoordinate(rawPdf.Y - tolerancePt);
        int maxY = GridCoordinate(rawPdf.Y + tolerancePt);
        float bestDistance = tolerancePt * tolerancePt;
        bool found = false;
        HashSet<int>? seenSegments = null;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (!_segmentCells.TryGetValue((x, y), out var segmentBucket))
                    continue;

                seenSegments ??= [];
                foreach (int segmentIndex in segmentBucket)
                {
                    if (!seenSegments.Add(segmentIndex))
                        continue;

                    PdfGeometrySnapSegment candidate = _segments[segmentIndex];
                    SKPoint closest = ClosestPointOnSegment(rawPdf, candidate.Start, candidate.End);
                    float distance = DistanceSquared(rawPdf, closest);
                    if (distance >= bestDistance)
                        continue;

                    bestDistance = distance;
                    segment = candidate;
                    found = true;
                }
            }
        }

        distancePt = found ? MathF.Sqrt(bestDistance) : 0;
        return found;
    }

    public IReadOnlyList<PdfGeometrySnapSegmentHit> FindSegments(SKPoint rawPdf, float tolerancePt)
    {
        if (tolerancePt <= 0 || _segments.Count == 0)
            return [];

        int minX = GridCoordinate(rawPdf.X - tolerancePt);
        int maxX = GridCoordinate(rawPdf.X + tolerancePt);
        int minY = GridCoordinate(rawPdf.Y - tolerancePt);
        int maxY = GridCoordinate(rawPdf.Y + tolerancePt);
        float toleranceSq = tolerancePt * tolerancePt;
        var hits = new List<PdfGeometrySnapSegmentHit>();
        HashSet<int>? seenSegments = null;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (!_segmentCells.TryGetValue((x, y), out var segmentBucket))
                    continue;

                seenSegments ??= [];
                foreach (int segmentIndex in segmentBucket)
                {
                    if (!seenSegments.Add(segmentIndex))
                        continue;

                    PdfGeometrySnapSegment segment = _segments[segmentIndex];
                    SKPoint closest = ClosestPointOnSegment(rawPdf, segment.Start, segment.End);
                    float distanceSq = DistanceSquared(rawPdf, closest);
                    if (distanceSq > toleranceSq)
                        continue;

                    float length = MeasurementGeometry.Distance(segment.Start, segment.End);
                    hits.Add(new PdfGeometrySnapSegmentHit(
                        segment,
                        segmentIndex,
                        MathF.Sqrt(distanceSq),
                        length));
                }
            }
        }

        return hits;
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
        "raster-junction" => 1,
        "pdf-point" => 2,
        _ => 3,
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
                        segment.LayerName ?? "",
                        Math.Max(0f, segment.StrokeWidth)))
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

    /// <summary>
    /// Word bounding boxes for one page in the same rotated page space as the
    /// snap segments. Used by Wall Trace to skip label/dimension text noise.
    /// </summary>
    public static Task<(bool Ok, IReadOnlyList<SKRect> Rects, string Error)> TryReadTextRectsAsync(
        string pdfPath,
        int pageIndex) =>
        Task.Run(() => TryReadHelperRects("textrects", pdfPath, pageIndex));

    /// <summary>
    /// Bounding boxes of non-white filled path items (wall poche) with their
    /// fill luminance. Used by Wall Trace to confirm that a centerline runs
    /// through a filled wall body and to tell dark rated walls from light
    /// partition fill.
    /// </summary>
    public static Task<(bool Ok, IReadOnlyList<WallCenterlineTracer.FillZone> Zones, string Error)>
        TryReadWallFillZonesAsync(string pdfPath, int pageIndex) =>
        Task.Run(() =>
        {
            (bool ok, IReadOnlyList<(SKRect Rect, float Lum)> rects, string error) =
                TryReadHelperFillRects(pdfPath, pageIndex);
            IReadOnlyList<WallCenterlineTracer.FillZone> zones = rects
                .Select(r => new WallCenterlineTracer.FillZone(r.Rect, r.Lum))
                .ToList();
            return (ok, zones, error);
        });

    private static (bool Ok, IReadOnlyList<(SKRect Rect, float Lum)> Rects, string Error) TryReadHelperFillRects(
        string pdfPath,
        int pageIndex)
    {
        try
        {
            var request = new PdfTextRectsRequest { Pdf = pdfPath, Page = pageIndex };
            if (!PdfLayerRenderService.TryInvokeHelper("fillrects", request, out PdfTextRectsResponse? response, out string error))
                return (false, [], error);

            if (response == null || !response.Ok)
                return (false, [], response?.Error ?? "PyMuPDF did not return a fillrects response.");

            IReadOnlyList<(SKRect, float)> rects = response.Rects
                .Where(r => IsFinite(r.X0) && IsFinite(r.Y0) && IsFinite(r.X1) && IsFinite(r.Y1))
                .Select(r => (
                    new SKRect(
                        Math.Min(r.X0, r.X1),
                        Math.Min(r.Y0, r.Y1),
                        Math.Max(r.X0, r.X1),
                        Math.Max(r.Y0, r.Y1)),
                    Math.Clamp(r.Lum, 0f, 1f)))
                .ToList();
            return (true, rects, "");
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"TryReadHelperFillRects failed for {pdfPath} page {pageIndex}");
            return (false, [], ex.Message);
        }
    }

    private static (bool Ok, IReadOnlyList<SKRect> Rects, string Error) TryReadHelperRects(
        string action,
        string pdfPath,
        int pageIndex)
    {
        try
        {
            var request = new PdfTextRectsRequest { Pdf = pdfPath, Page = pageIndex };
            if (!PdfLayerRenderService.TryInvokeHelper(action, request, out PdfTextRectsResponse? response, out string error))
                return (false, [], error);

            if (response == null || !response.Ok)
                return (false, [], response?.Error ?? $"PyMuPDF did not return a {action} response.");

            IReadOnlyList<SKRect> rects = response.Rects
                .Where(r => IsFinite(r.X0) && IsFinite(r.Y0) && IsFinite(r.X1) && IsFinite(r.Y1))
                .Select(r => new SKRect(
                    Math.Min(r.X0, r.X1),
                    Math.Min(r.Y0, r.Y1),
                    Math.Max(r.X0, r.X1),
                    Math.Max(r.Y0, r.Y1)))
                .ToList();
            return (true, rects, "");
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"TryReadHelperRects({action}) failed for {pdfPath} page {pageIndex}");
            return (false, [], ex.Message);
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
        [JsonPropertyName("stroke_width")]
        public float StrokeWidth { get; set; }
    }

    private sealed class PdfTextRectsRequest
    {
        public string Pdf { get; set; } = "";
        public int Page { get; set; }
    }

    private sealed class PdfTextRectsResponse
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = "";
        public List<PdfTextRectDto> Rects { get; set; } = [];
    }

    private sealed class PdfTextRectDto
    {
        public float X0 { get; set; }
        public float Y0 { get; set; }
        public float X1 { get; set; }
        public float Y1 { get; set; }

        /// <summary>Fill luminance for fillrects responses; textrects omit it (0).</summary>
        public float Lum { get; set; }
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
