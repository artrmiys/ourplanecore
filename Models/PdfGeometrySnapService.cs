using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SkiaSharp;

namespace OurPlaneCore;

public sealed record PdfGeometrySnapPoint(SKPoint Point, string Kind, string LayerName = "");

public sealed class PdfGeometrySnapResult
{
    public IReadOnlyList<PdfGeometrySnapPoint> Points { get; init; } = [];
}

public sealed class PdfSnapPointIndex
{
    public static readonly PdfSnapPointIndex Empty = new([]);

    private const float CellSizePt = 24f;
    private readonly Dictionary<(int X, int Y), List<PdfGeometrySnapPoint>> _cells = [];

    public PdfSnapPointIndex(IEnumerable<PdfGeometrySnapPoint> points)
    {
        foreach (PdfGeometrySnapPoint point in points)
        {
            var key = CellKey(point.Point);
            if (!_cells.TryGetValue(key, out var bucket))
            {
                bucket = [];
                _cells[key] = bucket;
            }

            bucket.Add(point);
        }
    }

    public int Count => _cells.Values.Sum(bucket => bucket.Count);

    public bool TryFind(SKPoint rawPdf, float tolerancePt, out PdfGeometrySnapPoint snap)
    {
        snap = default!;
        if (tolerancePt <= 0 || _cells.Count == 0)
            return false;

        int minX = GridCoordinate(rawPdf.X - tolerancePt);
        int maxX = GridCoordinate(rawPdf.X + tolerancePt);
        int minY = GridCoordinate(rawPdf.Y - tolerancePt);
        int maxY = GridCoordinate(rawPdf.Y + tolerancePt);
        float bestDistance = tolerancePt * tolerancePt;
        int bestPriority = int.MaxValue;
        bool found = false;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (!_cells.TryGetValue((x, y), out var bucket))
                    continue;

                foreach (PdfGeometrySnapPoint candidate in bucket)
                {
                    float distance = DistanceSquared(rawPdf, candidate.Point);
                    int priority = KindPriority(candidate.Kind);
                    if (distance >= bestDistance && (distance > bestDistance || priority >= bestPriority))
                        continue;

                    bestDistance = distance;
                    bestPriority = priority;
                    snap = candidate;
                    found = true;
                }
            }
        }

        return found;
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
        IReadOnlyList<PdfLayerInfo>? visibleLayers) =>
        Task.Run(() =>
        {
            bool ok = TryReadSnapPoints(pdfPath, pageIndex, visibleLayers, out PdfGeometrySnapResult result, out string error);
            return (ok, result, error);
        });

    internal static bool TryReadSnapPoints(
        string pdfPath,
        int pageIndex,
        IReadOnlyList<PdfLayerInfo>? visibleLayers,
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
        public List<PdfSnapLayerDto>? VisibleLayers { get; set; }
    }

    private sealed class PdfSnapResponse
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = "";
        public List<PdfSnapPointDto> Points { get; set; } = [];
    }

    private sealed class PdfSnapPointDto
    {
        public float X { get; set; }
        public float Y { get; set; }
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
