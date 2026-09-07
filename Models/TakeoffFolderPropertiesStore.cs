using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OurPlanCore;

public sealed class TakeoffFolderProperties
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";

    [JsonPropertyName("default_color")]
    public string DefaultColor { get; set; } = "";

    [JsonPropertyName("default_measurement_type")]
    public string DefaultMeasurementType { get; set; } = "";

    [JsonPropertyName("default_unit_price")]
    public double? DefaultUnitPrice { get; set; }

    [JsonPropertyName("default_item_notes")]
    public string DefaultItemNotes { get; set; } = "";

    [JsonPropertyName("default_name_prefix")]
    public string DefaultNamePrefix { get; set; } = "";

    [JsonPropertyName("updated_at_utc")]
    public string UpdatedAtUtc { get; set; } = "";
}

public static class TakeoffFolderPropertiesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string PropertiesPath(string folderPath) =>
        Path.Combine(folderPath, "folder_properties.json");

    public static TakeoffFolderProperties Load(string folderPath)
    {
        TakeoffFolderProperties properties = TryLoad(folderPath) ?? new TakeoffFolderProperties();
        if (string.IsNullOrWhiteSpace(properties.DisplayName))
            properties.DisplayName = Directory.Exists(folderPath)
                ? OurPlanCoreJobStore.DisplayName(folderPath)
                : Path.GetFileName(folderPath);
        properties.DefaultMeasurementType = NormalizeMeasurementType(properties.DefaultMeasurementType);
        properties.DefaultColor = NormalizeColor(properties.DefaultColor);
        return properties;
    }

    public static TakeoffFolderProperties? TryLoad(string folderPath)
    {
        string path = PropertiesPath(folderPath);
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<TakeoffFolderProperties>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Load folder properties failed for {path}");
            return null;
        }
    }

    public static void Save(string folderPath, TakeoffFolderProperties properties)
    {
        JobWriteAccess.Demand(PropertiesPath(folderPath), "save takeoff-folder properties");
        Directory.CreateDirectory(folderPath);
        properties.SchemaVersion = 1;
        properties.DisplayName = string.IsNullOrWhiteSpace(properties.DisplayName)
            ? OurPlanCoreJobStore.DisplayName(folderPath)
            : properties.DisplayName.Trim();
        properties.Notes = properties.Notes.Trim();
        properties.DefaultColor = NormalizeColor(properties.DefaultColor);
        properties.DefaultMeasurementType = NormalizeMeasurementType(properties.DefaultMeasurementType);
        properties.DefaultUnitPrice = properties.DefaultUnitPrice is >= 0 ? properties.DefaultUnitPrice : null;
        properties.DefaultItemNotes = properties.DefaultItemNotes.Trim();
        properties.DefaultNamePrefix = properties.DefaultNamePrefix.Trim();
        properties.UpdatedAtUtc = DateTime.UtcNow.ToString("O");

        string path = PropertiesPath(folderPath);
        try
        {
            IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(properties, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(path)}': {ex.Message}", ex);
        }
    }

    private static string NormalizeMeasurementType(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? ""
            : OurPlanCoreJobStore.NormalizeMeasurementType(value.Trim().ToLowerInvariant());

    private static string NormalizeColor(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length == 0)
            return "";
        return trimmed.StartsWith('#') ? trimmed : "#" + trimmed;
    }
}
