using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

namespace OurPlaneCore;

public sealed class OurPlaneCoreJob
{
    public string Name { get; init; } = "";
    public string RootPath { get; init; } = "";
    public string PagesRoot => Path.Combine(RootPath, "Pages");
    public string TakeoffsRoot => Path.Combine(RootPath, "Takeoffs");
    public string AIContextRoot => Path.Combine(RootPath, "AI_Context");
}

public sealed class PageFolderNode
{
    public string Name { get; init; } = "";
    public string FolderPath { get; init; } = "";
    public bool IsRoot { get; init; }
}

public sealed class TakeoffFolderNode
{
    public string Name { get; init; } = "";
    public string FolderPath { get; init; } = "";
    public bool IsRoot { get; init; }
}

public sealed class PageInfo
{
    public string Name { get; init; } = "";
    public string FolderPath { get; init; } = "";
    public string PdfPath { get; init; } = "";
    public int PdfPage { get; init; }
    public double ScaleMetersPerPt { get; set; }
    public bool PdfLayersCached { get; init; }
    public IReadOnlyList<PdfLayerInfo> PdfLayers { get; init; } = [];
    public List<string> LegendTakeoffOrder { get; set; } = [];
    public string LegendTakeoffOrderMode { get; set; } = "auto";
    public List<string> TakeoffLayerOrder { get; set; } = [];
    public List<string> HiddenTakeoffs { get; set; } = [];
    public List<string> HiddenMeasurements { get; set; } = [];
    public string OverlayPageFolder { get; init; } = "";
    public bool OverlayVisible { get; init; } = true;
    public string OverlayColor { get; init; } = "#E53935";
    public double OverlayOpacity { get; init; } = 0.55;
    public double OverlayOffsetXPt { get; init; }
    public double OverlayOffsetYPt { get; init; }
    public double OverlayScale { get; init; } = 1.0;
    public double OverlayRotationDegrees { get; init; }
    public RasterSheetSource? RasterSheet { get; init; }
}

public sealed class SourceInfo
{
    [JsonPropertyName("pdf")]
    public string Pdf { get; set; } = "";

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("scale_m_per_pt")]
    public double ScaleMetersPerPt { get; set; }

    [JsonPropertyName("pdf_layers_cached")]
    public bool PdfLayersCached { get; set; }

    [JsonPropertyName("pdf_layers")]
    public List<PdfLayerInfo> PdfLayers { get; set; } = [];

    [JsonPropertyName("legend_takeoff_order")]
    public List<string> LegendTakeoffOrder { get; set; } = [];

    [JsonPropertyName("legend_takeoff_order_mode")]
    public string LegendTakeoffOrderMode { get; set; } = "auto";

    [JsonPropertyName("hidden_takeoffs")]
    public List<string> HiddenTakeoffs { get; set; } = [];

    [JsonPropertyName("hidden_measurements")]
    public List<string> HiddenMeasurements { get; set; } = [];

    [JsonPropertyName("overlay_page_folder")]
    public string OverlayPageFolder { get; set; } = "";

    [JsonPropertyName("overlay_visible")]
    public bool OverlayVisible { get; set; } = true;

    [JsonPropertyName("overlay_color")]
    public string OverlayColor { get; set; } = "#E53935";

    [JsonPropertyName("overlay_opacity")]
    public double OverlayOpacity { get; set; } = 0.55;

    [JsonPropertyName("overlay_offset_x_pt")]
    public double OverlayOffsetXPt { get; set; }

    [JsonPropertyName("overlay_offset_y_pt")]
    public double OverlayOffsetYPt { get; set; }

    [JsonPropertyName("overlay_scale")]
    public double OverlayScale { get; set; } = 1.0;

    [JsonPropertyName("overlay_rotation_deg")]
    public double OverlayRotationDegrees { get; set; }

    [JsonPropertyName("raster_sheet")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RasterSheetSource? RasterSheet { get; set; }
}

public sealed class RasterSheetSource
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("use_as_page_open_raster")]
    public bool UseAsPageOpenRaster { get; set; }

    [JsonPropertyName("image")]
    public string Image { get; set; } = "";

    [JsonPropertyName("overview_image")]
    public string OverviewImage { get; set; } = "";

    [JsonPropertyName("format")]
    public string Format { get; set; } = "png";

    [JsonPropertyName("render_profile")]
    public string RenderProfile { get; set; } = "";

    [JsonPropertyName("render_scale")]
    public double RenderScale { get; set; }

    [JsonPropertyName("overview_render_scale")]
    public double OverviewRenderScale { get; set; }

    [JsonPropertyName("width_pt")]
    public double WidthPt { get; set; }

    [JsonPropertyName("height_pt")]
    public double HeightPt { get; set; }

    [JsonPropertyName("pdf_last_write_utc_ticks")]
    public long PdfLastWriteUtcTicks { get; set; }

    [JsonPropertyName("pdf_length")]
    public long PdfLength { get; set; }

    [JsonPropertyName("generated_at_utc")]
    public string GeneratedAtUtc { get; set; } = "";

    [JsonPropertyName("snap_index")]
    public string SnapIndex { get; set; } = "";

    [JsonPropertyName("snap_black_only")]
    public bool SnapBlackOnly { get; set; } = true;

    [JsonPropertyName("snap_point_count")]
    public int SnapPointCount { get; set; }

    [JsonPropertyName("snap_segment_count")]
    public int SnapSegmentCount { get; set; }

    [JsonPropertyName("snap_generated_at_utc")]
    public string SnapGeneratedAtUtc { get; set; } = "";

    // Schema version of the cached snap index. Bumped when the snap geometry
    // coordinate space changes (e.g. applying page /Rotate). Caches written by
    // an older version are treated as outdated so the rotated-page ghost overlay
    // is suppressed and the index is rebuilt. 0 = legacy/unrotated.
    [JsonPropertyName("snap_schema_version")]
    public int SnapSchemaVersion { get; set; }

    public RasterSheetSource Clone() =>
        new()
        {
            Enabled = Enabled,
            UseAsPageOpenRaster = UseAsPageOpenRaster,
            Image = Image,
            OverviewImage = OverviewImage,
            Format = Format,
            RenderProfile = RenderProfile,
            RenderScale = RenderScale,
            OverviewRenderScale = OverviewRenderScale,
            WidthPt = WidthPt,
            HeightPt = HeightPt,
            PdfLastWriteUtcTicks = PdfLastWriteUtcTicks,
            PdfLength = PdfLength,
            GeneratedAtUtc = GeneratedAtUtc,
            SnapIndex = SnapIndex,
            SnapBlackOnly = SnapBlackOnly,
            SnapPointCount = SnapPointCount,
            SnapSegmentCount = SnapSegmentCount,
            SnapGeneratedAtUtc = SnapGeneratedAtUtc,
            SnapSchemaVersion = SnapSchemaVersion,
        };
}

public sealed class PdfLayerInfo
{
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("on")]
    public bool IsOn { get; set; } = true;
}

public sealed class PageLayerManifest
{
    [JsonPropertyName("source_pdf")]
    public string SourcePdf { get; set; } = "";

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("page_number")]
    public int PageNumber { get; set; }

    [JsonPropertyName("generated_at_utc")]
    public string GeneratedAtUtc { get; set; } = "";

    [JsonPropertyName("layer_count")]
    public int LayerCount { get; set; }

    [JsonPropertyName("layers")]
    public List<PdfLayerInfo> Layers { get; set; } = [];
}
