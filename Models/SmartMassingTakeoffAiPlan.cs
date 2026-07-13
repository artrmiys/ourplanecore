using System.Text.Json.Serialization;

namespace OurPlanCore;

public sealed class SmartMassingTakeoffAiPlan
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    [JsonPropertyName("roof_type")]
    public string RoofType { get; set; } = "unknown";

    [JsonPropertyName("level_spacing_feet")]
    public double LevelSpacingFeet { get; set; }

    [JsonPropertyName("assignments")]
    public List<SmartMassingTakeoffAiAssignment> Assignments { get; set; } = [];

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = [];
}

public sealed class SmartMassingTakeoffAiAssignment
{
    [JsonPropertyName("takeoff_id")]
    public string TakeoffId { get; set; } = "";

    [JsonPropertyName("folder_path")]
    public string FolderPath { get; set; } = "";

    [JsonPropertyName("role")]
    public string Role { get; set; } = "unknown";

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";
}
