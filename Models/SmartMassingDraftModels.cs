using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SkiaSharp;


namespace OurPlanCore;

public sealed class SmartMassingDraft
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "draft";

    [JsonPropertyName("units")]
    public string Units { get; set; } = "pdf_points";

    [JsonPropertyName("generated_at_utc")]
    public string GeneratedAtUtc { get; set; } = "";

    [JsonPropertyName("reviewed_at_utc")]
    public string ReviewedAtUtc { get; set; } = "";

    [JsonPropertyName("review_notes")]
    public string ReviewNotes { get; set; } = "";

    [JsonPropertyName("source_marker_ids")]
    public List<string> SourceMarkerIds { get; set; } = [];

    [JsonPropertyName("assumptions")]
    public List<string> Assumptions { get; set; } = [];

    [JsonPropertyName("unresolved_questions")]
    public List<string> UnresolvedQuestions { get; set; } = [];

    [JsonPropertyName("footprints")]
    public List<SmartMassingFootprint> Footprints { get; set; } = [];

    [JsonPropertyName("roof")]
    public SmartMassingRoof Roof { get; set; } = new();

    [JsonPropertyName("openings")]
    public List<SmartMassingOpening> Openings { get; set; } = [];
}

public sealed class SmartMassingFootprint
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "footprint_main";

    [JsonPropertyName("level")]
    public int Level { get; set; } = 1;

    [JsonPropertyName("page")]
    public string Page { get; set; } = "";

    [JsonPropertyName("points")]
    public List<SmartMassingPoint> Points { get; set; } = [];

    [JsonPropertyName("base_elevation")]
    public double BaseElevation { get; set; }

    [JsonPropertyName("base_elevation_units")]
    public string BaseElevationUnits { get; set; } = "feet";

    [JsonPropertyName("height")]
    public double Height { get; set; }

    [JsonPropertyName("height_units")]
    public string HeightUnits { get; set; } = "feet";

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("source_marker_ids")]
    public List<string> SourceMarkerIds { get; set; } = [];
}

public sealed class SmartMassingPoint
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("source_marker_id")]
    public string SourceMarkerId { get; set; } = "";
}

public sealed class SmartMassingRoof
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "draft";

    [JsonPropertyName("elevation")]
    public double Elevation { get; set; }

    [JsonPropertyName("elevation_units")]
    public string ElevationUnits { get; set; } = "feet";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "unknown";

    [JsonPropertyName("pitch")]
    public string Pitch { get; set; } = "";

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("reviewed_at_utc")]
    public string ReviewedAtUtc { get; set; } = "";

    [JsonPropertyName("review_notes")]
    public string ReviewNotes { get; set; } = "";

    [JsonPropertyName("source_marker_ids")]
    public List<string> SourceMarkerIds { get; set; } = [];

    [JsonPropertyName("guides")]
    public List<SmartMassingRoofGuide> Guides { get; set; } = [];

    [JsonPropertyName("planes")]
    public List<SmartMassingPlane> Planes { get; set; } = [];
}

public sealed class SmartMassingRoofGuide
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "draft";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("points")]
    public List<SmartMassingPoint> Points { get; set; } = [];

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("source_marker_ids")]
    public List<string> SourceMarkerIds { get; set; } = [];

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";
}

public sealed class SmartMassingOpening
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "draft";

    [JsonPropertyName("level")]
    public int Level { get; set; } = 1;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("source_marker_id")]
    public string SourceMarkerId { get; set; } = "";

    [JsonPropertyName("page")]
    public string Page { get; set; } = "";

    [JsonPropertyName("wall_index")]
    public int WallIndex { get; set; } = -1;

    [JsonPropertyName("center")]
    public SmartMassingVertex Center { get; set; } = new();

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";
}

public sealed class SmartMassingPlane
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "draft";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("points")]
    public List<SmartMassingVertex> Points { get; set; } = [];

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("source_marker_ids")]
    public List<string> SourceMarkerIds { get; set; } = [];

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";
}

public sealed class SmartMassingVertex
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("z")]
    public double Z { get; set; }

    [JsonPropertyName("source_marker_id")]
    public string SourceMarkerId { get; set; } = "";
}
