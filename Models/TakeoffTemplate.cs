using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OurPlaneCore;

/// <summary>
/// A node in a saved takeoff template: either a folder (with children) or a
/// leaf takeoff item carrying its measurement type/color. Mirrors the on-disk
/// Takeoffs tree so a template can be re-created in any job.
/// </summary>
public sealed class TakeoffTemplateNode
{
    public string Name { get; set; } = "";
    public bool IsFolder { get; set; } = true;
    public string? MeasurementType { get; set; }
    public string? Color { get; set; }
    public List<TakeoffTemplateNode> Children { get; set; } = new();
}

/// <summary>
/// A named, reusable preset of a takeoff folder/item tree — the OurCore answer
/// to PlanSwift folder templates. Applying it re-creates the tree under the
/// selected Takeoffs folder. Stored globally so it is shared across jobs.
/// </summary>
public sealed class TakeoffTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Template";
    public List<TakeoffTemplateNode> Roots { get; set; } = new();
}

/// <summary>
/// Global (cross-job) persistence for takeoff templates, kept next to the app
/// settings at %APPDATA%\OurPlaneCore\templates.json.
/// </summary>
public static class TakeoffTemplateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OurPlaneCore");

    private static string FilePath => Path.Combine(Dir, "templates.json");

    public static List<TakeoffTemplate> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new List<TakeoffTemplate>();
            string json = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(json))
                return new List<TakeoffTemplate>();
            return JsonSerializer.Deserialize<List<TakeoffTemplate>>(json) ?? new List<TakeoffTemplate>();
        }
        catch
        {
            // Corrupt/unreadable file should never crash the app; start empty.
            return new List<TakeoffTemplate>();
        }
    }

    public static void Save(List<TakeoffTemplate> templates)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string json = JsonSerializer.Serialize(templates ?? new List<TakeoffTemplate>(), JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Best-effort persistence; ignore IO failures so the UI stays responsive.
        }
    }
}
