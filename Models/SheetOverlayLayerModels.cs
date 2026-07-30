using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OurPlanCore;

public sealed class SheetOverlayLayerInfo
{
    public string Id { get; init; } = "";
    public string SourcePageFolder { get; init; } = "";
    public bool IsVisible { get; init; } = true;
    public string Color { get; init; } = "#E53935";
    public double Opacity { get; init; } = 1.0;
    public double OffsetXPt { get; init; }
    public double OffsetYPt { get; init; }
    public double Scale { get; init; } = 1.0;
    public double RotationDegrees { get; init; }

    public SheetOverlayLayerInfo Clone() =>
        new()
        {
            Id = Id,
            SourcePageFolder = SourcePageFolder,
            IsVisible = IsVisible,
            Color = Color,
            Opacity = Opacity,
            OffsetXPt = OffsetXPt,
            OffsetYPt = OffsetYPt,
            Scale = Scale,
            RotationDegrees = RotationDegrees,
        };
}

public sealed class SheetOverlayLayerCollection
{
    public string ActiveOverlayId { get; init; } = "";
    public IReadOnlyList<SheetOverlayLayerInfo> Layers { get; init; } = [];

    public SheetOverlayLayerInfo? ActiveLayer =>
        Layers.FirstOrDefault(layer =>
            string.Equals(layer.Id, ActiveOverlayId, StringComparison.OrdinalIgnoreCase))
        ?? Layers.LastOrDefault();

    public SheetOverlayLayerCollection Clone() =>
        new()
        {
            ActiveOverlayId = ActiveOverlayId,
            Layers = Layers.Select(layer => layer.Clone()).ToList(),
        };
}

internal sealed class SheetOverlayLayerManifest
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("active_overlay_id")]
    public string ActiveOverlayId { get; set; } = "";

    [JsonPropertyName("layers")]
    public List<SheetOverlayLayerEntry> Layers { get; set; } = [];
}

internal sealed class SheetOverlayLayerEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("source_page_folder")]
    public string SourcePageFolder { get; set; } = "";

    [JsonPropertyName("visible")]
    public bool IsVisible { get; set; } = true;

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#E53935";

    [JsonPropertyName("opacity")]
    public double Opacity { get; set; } = 1.0;

    [JsonPropertyName("offset_x_pt")]
    public double OffsetXPt { get; set; }

    [JsonPropertyName("offset_y_pt")]
    public double OffsetYPt { get; set; }

    [JsonPropertyName("scale")]
    public double Scale { get; set; } = 1.0;

    [JsonPropertyName("rotation_deg")]
    public double RotationDegrees { get; set; }
}
