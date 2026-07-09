using System.Text.Json;
using OurPlaneCore.Models;
using SkiaSharp;

/// <summary>
/// Offline Wall Trace runner for tuning detection defaults on real sheets:
///   dotnet run --project Tests -- walltrace input.json output.json
/// Input: { "segments": [{"x0","y0","x1","y1"}], "polygon": [{"x","y"}],
///          "holes": [[{"x","y"}]], "zones": [{"x0","y0","x1","y1"}],
///          "options": {"min_thickness_pt","max_thickness_pt",
///                      "min_face_length_pt","min_wall_length_pt"} }
/// Output: { "polylines": [[{"x","y"}]] } — same PDF point space.
/// </summary>
internal static class WallTraceHarness
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };

    public static int Run(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("usage: dotnet run --project Tests -- walltrace input.json output.json");
            return 2;
        }

        HarnessInput input = JsonSerializer.Deserialize<HarnessInput>(File.ReadAllText(args[1]), Json)
            ?? throw new InvalidOperationException("empty harness input");

        List<WallCenterlineTracer.Segment> segments = input.Segments
            .Select(s => new WallCenterlineTracer.Segment(new SKPoint(s.X0, s.Y0), new SKPoint(s.X1, s.Y1)))
            .ToList();
        List<SKPoint> polygon = input.Polygon.Select(p => new SKPoint(p.X, p.Y)).ToList();
        IReadOnlyList<IReadOnlyList<SKPoint>>? holes = input.Holes?.Count > 0
            ? input.Holes.Select(h => (IReadOnlyList<SKPoint>)h.Select(p => new SKPoint(p.X, p.Y)).ToList()).ToList()
            : null;

        var options = new WallCenterlineTracer.Options
        {
            MinThicknessPt = input.Options.MinThicknessPt,
            MaxThicknessPt = input.Options.MaxThicknessPt,
            MinFaceLengthPt = input.Options.MinFaceLengthPt,
            MinWallLengthPt = input.Options.MinWallLengthPt,
            DarkFillOnly = input.Options.DarkFillOnly,
            DarkLuminanceMax = input.Options.DarkLuminanceMax,
            BoundaryExclusionPt = input.Options.BoundaryExclusionPt,
            ExcludedZones = input.Zones?.Count > 0
                ? input.Zones.Select(z => new SKRect(z.X0, z.Y0, z.X1, z.Y1)).ToList()
                : null,
            WallFillZones = input.FillZones?.Count > 0
                ? input.FillZones
                    .Select(z => new WallCenterlineTracer.FillZone(new SKRect(z.X0, z.Y0, z.X1, z.Y1), z.Lum))
                    .ToList()
                : null,
        };

        List<SKPoint[]> polylines = WallCenterlineTracer.Trace(segments, polygon, options, holes);

        var output = new HarnessOutput
        {
            Polylines = polylines
                .Select(line => line.Select(p => new PointDto { X = p.X, Y = p.Y }).ToList())
                .ToList(),
        };
        File.WriteAllText(args[2], JsonSerializer.Serialize(output, Json));
        Console.WriteLine($"walltrace: {segments.Count} segments in, {polylines.Count} polylines out");
        return 0;
    }

    private sealed class HarnessInput
    {
        public List<SegmentDto> Segments { get; set; } = [];
        public List<PointDto> Polygon { get; set; } = [];
        public List<List<PointDto>>? Holes { get; set; }
        public List<RectDto>? Zones { get; set; }
        public List<RectDto>? FillZones { get; set; }
        public OptionsDto Options { get; set; } = new();
    }

    private sealed class HarnessOutput
    {
        public List<List<PointDto>> Polylines { get; set; } = [];
    }

    private sealed class SegmentDto
    {
        public float X0 { get; set; }
        public float Y0 { get; set; }
        public float X1 { get; set; }
        public float Y1 { get; set; }
    }

    private sealed class RectDto
    {
        public float X0 { get; set; }
        public float Y0 { get; set; }
        public float X1 { get; set; }
        public float Y1 { get; set; }

        /// <summary>Fill luminance for fill_zones entries; 0 (dark) when omitted.</summary>
        public float Lum { get; set; }
    }

    private sealed class PointDto
    {
        public float X { get; set; }
        public float Y { get; set; }
    }

    private sealed class OptionsDto
    {
        public float MinThicknessPt { get; set; }
        public float MaxThicknessPt { get; set; }
        public float MinFaceLengthPt { get; set; }
        public float MinWallLengthPt { get; set; }
        public bool DarkFillOnly { get; set; }
        public float? DarkLuminanceMax { get; set; }
        public float BoundaryExclusionPt { get; set; }
    }
}
