using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OurPlaneCore;

/// <summary>
/// A node in a saved takeoff template: either a folder (with children) or a
/// leaf takeoff item carrying its measurement type/color. Mirrors the on-disk
/// Takeoffs tree so a template can be re-created in any job.
/// </summary>
public sealed class TakeoffTemplateNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public bool IsFolder { get; set; } = true;
    public string MeasurementType { get; set; } = "line";
    public string Color { get; set; } = "#FF4444";
    public string CountSymbol { get; set; } = CountDisplaySymbol.Circle;
    public double UnitPrice { get; set; }
    public string Notes { get; set; } = "";
    public List<TakeoffTemplateNode> Children { get; set; } = new();

    public TakeoffTemplateNode Clone()
    {
        return new TakeoffTemplateNode
        {
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id,
            Name = Name,
            IsFolder = IsFolder,
            MeasurementType = OurPlaneCoreJobStore.NormalizeMeasurementType(MeasurementType),
            Color = string.IsNullOrWhiteSpace(Color) ? "#FF4444" : Color,
            CountSymbol = CountDisplaySymbol.Normalize(CountSymbol),
            UnitPrice = UnitPrice,
            Notes = Notes,
            Children = Children.Select(child => child.Clone()).ToList(),
        };
    }
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

    public TakeoffTemplate Clone()
    {
        return new TakeoffTemplate
        {
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id,
            Name = string.IsNullOrWhiteSpace(Name) ? "Template" : Name,
            Roots = Roots.Select(root => root.Clone()).ToList(),
        };
    }
}

public sealed class TakeoffTemplateConfig
{
    public TakeoffTemplate Template { get; set; } = BuildDefaultTemplate();

    public TakeoffTemplateConfig Clone()
    {
        return new TakeoffTemplateConfig
        {
            Template = (Template ?? BuildDefaultTemplate()).Clone(),
        };
    }

    public static TakeoffTemplateConfig BuildDefault() =>
        new() { Template = BuildDefaultTemplate() };

    public static TakeoffTemplate BuildDefaultTemplate()
    {
        return new TakeoffTemplate
        {
            Name = "Default Takeoff Presets",
            Roots =
            [
                Folder("sqfts",
                    Area("base", "#4CAF50"),
                    Area("1st", "#2E7D32"),
                    Area("2nd", "#388E3C"),
                    Area("3rd", "#43A047"),
                    Area("4th", "#66BB6A"),
                    Area("5th", "#81C784"),
                    Area("6th", "#8BC34A"),
                    Area("7th", "#9CCC65"),
                    Area("8th", "#AED581"),
                    Area("deck", "#009688"),
                    Area("porch", "#26A69A"),
                    Area("blcny", "#00ACC1"),
                    Area("balcony", "#00BCD4"),
                    Area("cant", "#5C6BC0"),
                    Area("cantilevered", "#3F51B5"),
                    Area("flat", "#607D8B"),
                    Area("rf", "#455A64"),
                    Area("rf mtl x", "#546E7A")),
                Folder("walls",
                    Line("corners", "#E91E63"),
                    Line("ext", "#FF4444"),
                    Line("cor", "#F06292"),
                    Line("corr", "#EC407A"),
                    Line("dem", "#AB47BC"),
                    Line("2x4 x", "#EF5350"),
                    Line("2x6 x", "#D32F2F"),
                    Line("2x8 x", "#B71C1C"),
                    Line("2x4 half", "#BA68C8"),
                    Line("2x6 half", "#8E24AA")),
                Folder("framing",
                    Line("Blocking for Drywall", "#2196F3"),
                    Line("Blocking for Trusses", "#1E88E5"),
                    Line("Ribbon Board", "#1976D2"),
                    Line("Rim Board", "#0D47A1"),
                    Line("Blocking", "#00BCD4"),
                    Line("Ledger", "#009688"),
                    Line("1x3 Cross Blocking", "#3F51B5"),
                    Line("Plate", "#FF9800"),
                    Line("Frame", "#795548")),
            ],
        };
    }

    private static TakeoffTemplateNode Folder(string name, params TakeoffTemplateNode[] children) =>
        new()
        {
            Name = name,
            IsFolder = true,
            Children = children.ToList(),
        };

    private static TakeoffTemplateNode Line(string name, string color) =>
        Item(name, "line", color);

    private static TakeoffTemplateNode Area(string name, string color) =>
        Item(name, "area", color);

    private static TakeoffTemplateNode Item(string name, string measurementType, string color) =>
        new()
        {
            Name = name,
            IsFolder = false,
            MeasurementType = measurementType,
            Color = color,
            CountSymbol = CountDisplaySymbol.Circle,
        };
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

    private static string GlobalConfigPath() =>
        Path.Combine(SmartContextStore.GlobalRoot, "presets", "takeoff_templates.json");

    private static string JobConfigPath(OurPlaneCoreJob job) =>
        Path.Combine(job.RootPath, "AI_Context", "settings", "takeoff_templates.json");

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

    public static TakeoffTemplateConfig? LoadGlobalConfig() =>
        LoadConfig(GlobalConfigPath());

    public static void SaveGlobalConfig(TakeoffTemplateConfig config) =>
        SaveConfig(GlobalConfigPath(), config);

    public static TakeoffTemplateConfig? LoadJobOverride(OurPlaneCoreJob job) =>
        LoadConfig(JobConfigPath(job));

    public static void SaveJobOverride(OurPlaneCoreJob job, TakeoffTemplateConfig config) =>
        SaveConfig(JobConfigPath(job), config);

    public static void ClearJobOverride(OurPlaneCoreJob job)
    {
        try
        {
            string path = JobConfigPath(job);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best effort
        }
    }

    public static TakeoffTemplateConfig ResolveConfig(OurPlaneCoreJob? job)
    {
        if (job != null && LoadJobOverride(job) is { } jobConfig)
            return Upgrade(jobConfig);
        if (LoadGlobalConfig() is { } globalConfig)
            return Upgrade(globalConfig);
        return TakeoffTemplateConfig.BuildDefault();
    }

    private static TakeoffTemplateConfig Upgrade(TakeoffTemplateConfig config)
    {
        var clone = config.Clone();
        if (clone.Template.Roots.Count == 0)
            clone.Template = TakeoffTemplateConfig.BuildDefaultTemplate();
        NormalizeNodeIds(clone.Template.Roots);
        return clone;
    }

    private static void NormalizeNodeIds(IEnumerable<TakeoffTemplateNode> nodes)
    {
        foreach (TakeoffTemplateNode node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
                node.Id = Guid.NewGuid().ToString("N");
            node.MeasurementType = OurPlaneCoreJobStore.NormalizeMeasurementType(node.MeasurementType);
            node.Color = string.IsNullOrWhiteSpace(node.Color) ? "#FF4444" : node.Color;
            node.CountSymbol = CountDisplaySymbol.Normalize(node.CountSymbol);
            NormalizeNodeIds(node.Children);
        }
    }

    private static TakeoffTemplateConfig? LoadConfig(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            string json = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<TakeoffTemplateConfig>(json);
        }
        catch
        {
            return null;
        }
    }

    private static void SaveConfig(string path, TakeoffTemplateConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(config ?? TakeoffTemplateConfig.BuildDefault(), JsonOptions));
    }
}

public static class TakeoffTemplateRouting
{
    public static string ResolveDestinationFolder(
        OurPlaneCoreJob job,
        IReadOnlyList<string> templateFolderPath)
    {
        if (templateFolderPath.Count == 0)
            return job.TakeoffsRoot;

        string current = job.TakeoffsRoot;
        foreach (string segment in templateFolderPath)
        {
            string clean = segment.Trim();
            if (clean.Length == 0)
                return job.TakeoffsRoot;

            string? next = OurPlaneCoreJobStore.GetOrderedChildDirectories(current)
                .Where(path => !OurPlaneCoreJobStore.IsTakeoffItemFolder(path))
                .FirstOrDefault(path =>
                    string.Equals(OurPlaneCoreJobStore.DisplayName(path), clean, StringComparison.OrdinalIgnoreCase));

            if (next == null)
                return job.TakeoffsRoot;

            current = next;
        }

        return current;
    }
}
