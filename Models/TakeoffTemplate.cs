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
    public const int CurrentBuiltInVersion = 3;

    public int BuiltInVersion { get; set; }
    public TakeoffTemplate Template { get; set; } = BuildDefaultTemplate();

    public TakeoffTemplateConfig Clone()
    {
        return new TakeoffTemplateConfig
        {
            BuiltInVersion = BuiltInVersion,
            Template = (Template ?? BuildDefaultTemplate()).Clone(),
        };
    }

    public static TakeoffTemplateConfig BuildDefault() =>
        new()
        {
            BuiltInVersion = CurrentBuiltInVersion,
            Template = BuildDefaultTemplate(),
        };

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
                    Area("rf x", "#37474F"),
                    Area("rf mtl x", "#546E7A"),
                    Area("roof", "#263238"),
                    Area("overframe x", "#78909C")),
                Folder("units",
                    Line("Unit", "#607D8B")),
                Folder("walls",
                    WallPreset("corners"),
                    WallPreset("unit"),
                    WallPreset("ext"),
                    WallPreset("cor"),
                    WallPreset("corr"),
                    WallPreset("dem"),
                    WallPreset("parapet"),
                    WallPreset("shaft"),
                    WallPreset("furring"),
                    WallPreset("2x4 x"),
                    WallPreset("2x6 x"),
                    WallPreset("2x8 x"),
                    WallPreset("2x4 half"),
                    WallPreset("2x6 half"),
                    WallFloor("basement foor walls"),
                    WallFloor("1st floor walls"),
                    WallFloor("2nd floor walls"),
                    WallFloor("3rd floor walls"),
                    WallFloor("4th floor walls"),
                    WallFloor("5th floor walls"),
                    WallFloor("shaft walls")),
                Folder("gables",
                    Area("gable", "#8D6E63"),
                    Folder("gable trusses",
                        Area("gable truss", "#6D4C41")),
                    Folder("gable stick",
                        Area("gable stick", "#5D4037"))),
                Folder("parapets",
                    Line("parapet", "#7E57C2")),
                Folder("trussheel",
                    Line("Truss Heel", "#00ACC1"),
                    Line("Eve Heel", "#26C6DA")),
                Folder("openings",
                    Point("Window", "#43A047"),
                    Point("Door", "#2E7D32"),
                    Line("Header", "#558B2F")),
                Folder("eves rakes",
                    Folder("eves",
                        Line("Eve", "#039BE5"),
                        Line("Eave", "#0288D1")),
                    Folder("rakes",
                        Line("Rake", "#0277BD")),
                    Line("Returns", "#01579B")),
                Folder("roof misc",
                    Line("Ridge", "#5E35B1"),
                    Line("Valley", "#512DA8"),
                    Line("Hip", "#4527A0"),
                    Line("Flashing", "#3949AB"),
                    Area("Roof Sheathing", "#1E88E5"),
                    Area("Gable Sheathing", "#1976D2"),
                    Line("Roof Types", "#303F9F")),
                Folder("framing",
                    Line("Blocking for Drywall", "#2196F3"),
                    Line("Blocking for Trusses", "#1E88E5"),
                    Line("Ribbon Board", "#1976D2"),
                    Line("Rim Board", "#0D47A1"),
                    Line("Blocking", "#00BCD4"),
                    Line("Ledger", "#009688"),
                    Line("1x3 Cross Blocking", "#3F51B5"),
                    Line("Plate", "#FF9800"),
                    Line("Frame", "#795548"),
                    Line("Post", "#8BC34A"),
                    Line("Beam", "#7CB342"),
                    Line("Joist", "#689F38"),
                    Line("Stair", "#558B2F"),
                    Line("Subfloor", "#33691E"),
                    Line("Bracing", "#00897B"),
                    Line("Bolts", "#00796B"),
                    Line("Screws", "#00695C"),
                    Line("Steel Beam Web Fillers", "#004D40"),
                    FramingFloor("1st floor framing"),
                    FramingFloor("2nd floor framing"),
                    FramingFloor("3rd floor framing"),
                    FramingFloor("4th floor framing"),
                    FramingFloor("5th floor framing"),
                    FramingFloor("loft framing"),
                    Folder("roof framing",
                        Line("Ridge", "#5E35B1"),
                        Line("Header", "#512DA8"),
                        Line("Hip", "#4527A0"),
                        Line("Valley", "#3949AB"),
                        Line("Dormer", "#303F9F"),
                        Line("Overframes", "#283593"),
                        Line("Dbl Rafters", "#1A237E"),
                        Line("Trpl Rafters", "#3F51B5"),
                        Line("Canopy", "#2196F3"),
                        Area("Roof Sheathing", "#1976D2"))),
                Folder("shear walls - holdowns - ties",
                    Line("Shear Wall", "#FDD835"),
                    Point("Holddown", "#FBC02D"),
                    Point("Tie", "#F9A825")),
                Folder("siding",
                    Area("Siding", "#8D6E63"),
                    Line("Siding Trim", "#795548")),
                Folder("trims",
                    Line("Exterior Trim", "#6D4C41"),
                    Line("Interior Trim", "#5D4037"),
                    Line("Base", "#4E342E"),
                    Line("Casing", "#3E2723"),
                    Line("Crown", "#A1887F")),
                Folder("drywalls",
                    Area("Drywall", "#90A4AE"),
                    Line("Drywall Trim", "#78909C")),
            ],
        };
    }

    private static TakeoffTemplateNode WallFloor(string name) =>
        Folder(name,
            WallPreset("corners"),
            WallPreset("unit"),
            WallPreset("ext"),
            WallPreset("cor"),
            WallPreset("corr"),
            WallPreset("dem"),
            WallPreset("parapet"),
            WallPreset("shaft"),
            WallPreset("furring"),
            WallPreset("2x4 x"),
            WallPreset("2x6 x"),
            WallPreset("2x8 x"),
            WallPreset("2x4 half"),
            WallPreset("2x6 half"));

    private static TakeoffTemplateNode WallPreset(string name) =>
        Line(name, WallPresetColor(name));

    private static string WallPresetColor(string name) =>
        TryWallPresetColor(name, out string color) ? color : "#FF4444";

    internal static bool TryWallPresetColor(string name, out string color)
    {
        color = name.Trim().ToLowerInvariant() switch
        {
            "corners" => "#FF1744",
            "unit" => "#00B8D4",
            "ext" => "#2E7D32",
            "cor" => "#FF9100",
            "corr" => "#651FFF",
            "dem" => "#2962FF",
            "parapet" => "#C51162",
            "shaft" => "#00C853",
            "furring" => "#6D4C41",
            "2x4 x" => "#D500F9",
            "2x6 x" => "#FFD600",
            "2x8 x" => "#00BFA5",
            "2x4 half" => "#FF6D00",
            "2x6 half" => "#0091EA",
            _ => "",
        };
        return color.Length > 0;
    }

    private static TakeoffTemplateNode FramingFloor(string name) =>
        Folder(name,
            Line("Post", "#8BC34A"),
            Line("Beam", "#7CB342"),
            Line("Joist", "#689F38"),
            Line("Stair", "#558B2F"),
            Line("Subfloor", "#33691E"),
            Line("Rim Board", "#0D47A1"),
            Line("Ribbon Board", "#1976D2"),
            Line("Blocking", "#00BCD4"),
            Line("Blocking for Drywall", "#2196F3"),
            Line("Blocking for Trusses", "#1E88E5"),
            Line("1x3 Cross Blocking", "#3F51B5"),
            Line("Ledger", "#009688"),
            Line("Plate", "#FF9800"),
            Line("Frame", "#795548"),
            Line("Bracing", "#00897B"),
            Line("Bolts", "#00796B"),
            Line("Screws", "#00695C"),
            Line("Steel Beam Web Fillers", "#004D40"));

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

    private static TakeoffTemplateNode Point(string name, string color) =>
        Item(name, "point", color);

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
/// Global (cross-job) persistence for takeoff templates. The editable config
/// lives under SmartContextStore.GlobalRoot/presets; the old list file remains
/// for backward compatibility with earlier template UI builds.
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
        if (clone.BuiltInVersion < TakeoffTemplateConfig.CurrentBuiltInVersion)
        {
            int previousBuiltInVersion = clone.BuiltInVersion;
            MergeBuiltInDefaults(clone.Template.Roots, TakeoffTemplateConfig.BuildDefaultTemplate().Roots);
            if (previousBuiltInVersion < 3)
                ApplyBuiltInWallPaletteUpgrade(clone.Template.Roots);
            clone.BuiltInVersion = TakeoffTemplateConfig.CurrentBuiltInVersion;
        }
        NormalizeNodeIds(clone.Template.Roots);
        return clone;
    }

    private static void MergeBuiltInDefaults(
        List<TakeoffTemplateNode> targetNodes,
        IEnumerable<TakeoffTemplateNode> defaultNodes)
    {
        foreach (TakeoffTemplateNode defaultNode in defaultNodes)
        {
            TakeoffTemplateNode? existing = targetNodes.FirstOrDefault(candidate =>
                candidate.IsFolder == defaultNode.IsFolder &&
                string.Equals(candidate.Name, defaultNode.Name, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                targetNodes.Add(defaultNode.Clone());
                continue;
            }

            if (existing.IsFolder)
                MergeBuiltInDefaults(existing.Children, defaultNode.Children);
        }
    }

    private static void ApplyBuiltInWallPaletteUpgrade(IEnumerable<TakeoffTemplateNode> roots)
    {
        foreach (TakeoffTemplateNode node in roots)
        {
            if (node.IsFolder &&
                string.Equals(node.Name, "walls", StringComparison.OrdinalIgnoreCase))
            {
                ApplyWallPalette(node);
            }
            else
            {
                ApplyBuiltInWallPaletteUpgrade(node.Children);
            }
        }
    }

    private static void ApplyWallPalette(TakeoffTemplateNode folder)
    {
        foreach (TakeoffTemplateNode child in folder.Children)
        {
            if (child.IsFolder)
            {
                ApplyWallPalette(child);
                continue;
            }

            if (TakeoffTemplateConfig.TryWallPresetColor(child.Name, out string color))
                child.Color = color;
        }
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
