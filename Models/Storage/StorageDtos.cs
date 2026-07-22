using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OurPlanCore;

// Envelope for measurements.json. The file has historically been a bare JSON
// array of MeasurementDto, which leaves nowhere to record a format version.
// This wrapper adds one so a future breaking change has a migration hook. The
// reader accepts both shapes (see TakeoffStore.ParseMeasurementDtos); the
// writer still emits the bare array until this reader is broadly deployed, so
// an older build never mistakes a versioned file for corruption.
internal sealed class MeasurementsFileDto
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("measurements")]
    public List<MeasurementDto> Measurements { get; set; } = new();
}

internal sealed class MeasurementDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("mtype")]
    public string MType { get; set; } = "line";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";

    [JsonPropertyName("points_pdf")]
    public List<PointDto> PointsPdf { get; set; } = [];

    [JsonPropertyName("holes_pdf")]
    public List<List<PointDto>> HolesPdf { get; set; } = [];

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#FF4444";

    [JsonPropertyName("count_symbol")]
    public string CountSymbol { get; set; } = "";

    [JsonPropertyName("page_folder")]
    public string PageFolder { get; set; } = "";

    [JsonPropertyName("scale_m_per_pt")]
    public double ScaleMetersPerPt { get; set; }

    [JsonPropertyName("joist_direction_degrees")]
    public double JoistDirectionDegrees { get; set; }

    [JsonPropertyName("joist_direction_locked")]
    public bool JoistDirectionLocked { get; set; }

    [JsonPropertyName("joist_direction_follows_area_rotation")]
    public bool JoistDirectionFollowsAreaRotation { get; set; } = true;

    [JsonPropertyName("joist_add_end_joist")]
    public bool JoistAddEndJoist { get; set; } = true;

    [JsonPropertyName("extra_joists")]
    public List<JoistExtraSegmentDto> ExtraJoists { get; set; } = [];
}

internal sealed class JoistExtraSegmentDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("start_pdf")]
    public PointDto StartPdf { get; set; } = new(0, 0);

    [JsonPropertyName("end_pdf")]
    public PointDto EndPdf { get; set; } = new(0, 0);
}

internal sealed class PageAnnotationDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "line";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#1565C0";

    [JsonPropertyName("stroke_width")]
    public double StrokeWidth { get; set; }

    [JsonPropertyName("page_folder")]
    public string PageFolder { get; set; } = "";

    [JsonPropertyName("scale_m_per_pt")]
    public double ScaleMetersPerPt { get; set; }

    [JsonPropertyName("hidden")]
    public bool Hidden { get; set; }

    [JsonPropertyName("points_pdf")]
    public List<PointDto> PointsPdf { get; set; } = [];
}

internal sealed class PageBookmarkDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("page_folder")]
    public string PageFolder { get; set; } = "";

    [JsonPropertyName("page_name")]
    public string PageName { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("zoom")]
    public float Zoom { get; set; }

    [JsonPropertyName("pan_x")]
    public float PanX { get; set; }

    [JsonPropertyName("pan_y")]
    public float PanY { get; set; }

    [JsonPropertyName("crop_image_path")]
    public string CropImagePath { get; set; } = "";

    [JsonPropertyName("crop_left")]
    public float CropLeft { get; set; }

    [JsonPropertyName("crop_top")]
    public float CropTop { get; set; }

    [JsonPropertyName("crop_right")]
    public float CropRight { get; set; }

    [JsonPropertyName("crop_bottom")]
    public float CropBottom { get; set; }

    [JsonPropertyName("created_at_utc")]
    public string CreatedAtUtc { get; set; } = "";

    [JsonPropertyName("updated_at_utc")]
    public string UpdatedAtUtc { get; set; } = "";
}

internal sealed record PointDto(float X, float Y);
