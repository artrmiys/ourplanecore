using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OurPlanCore;

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
            MeasurementType = OurPlanCoreJobStore.NormalizeMeasurementType(MeasurementType),
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

public sealed partial class TakeoffTemplateConfig
{
    public const int CurrentBuiltInVersion = 5;
    public const string DefaultTemplateName = "Default";

    internal static readonly IReadOnlyList<string> WallPresetNames =
    [
        "corners",
        "ext",
        "ext 2x6",
        "ext 2x4",
        "ext 2x8",
        "cor",
        "cor 2x4",
        "cor 2x6",
        "cor 2x8",
        "cor (2) 2x4",
        "cor (2) 2x6",
        "cor (2) 2x8",
        "dem",
        "dem 2x4",
        "dem 2x6",
        "dem 2x8",
        "dem (2) 2x4",
        "dem (2) 2x6",
        "dem (2) 2x8",
        "furring",
        "2x4 x",
        "2x6 x",
        "2x8 x",
        "2x4 half",
        "2x6 half",
    ];

    internal static readonly IReadOnlyList<string> ShaftWallPresetNames =
    [
        "shaft 1st",
        "shaft 2nd",
        "shaft 3rd",
        "shaft 4th",
        "shaft 5th",
    ];

    internal static readonly IReadOnlyList<string> FramingPresetNames =
    [
        "Blocking for Drywall",
        "Blocking for Trusses",
        "Ribbon Board",
        "Rim Board",
        "Blocking",
        "Ledger",
        "1x3 Cross Blocking",
        "Plate",
        "Frame",
    ];

    internal static readonly IReadOnlySet<string> DeprecatedRootFolderNames = new HashSet<string>(
        ["units", "shear walls - holdowns - ties", "siding", "trims", "drywalls"],
        StringComparer.OrdinalIgnoreCase);

    internal static readonly IReadOnlySet<string> DeprecatedWallPresetNames = new HashSet<string>(
        ["unit", "corr", "parapet", "shaft"],
        StringComparer.OrdinalIgnoreCase);

    public int BuiltInVersion { get; set; }
    public string ActiveTemplateId { get; set; } = "";
    public List<TakeoffTemplate> Templates { get; set; } = new();
    public TakeoffTemplate Template { get; set; } = BuildDefaultTemplate();

    public TakeoffTemplateConfig Clone()
    {
        var clone = new TakeoffTemplateConfig
        {
            BuiltInVersion = BuiltInVersion,
            ActiveTemplateId = ActiveTemplateId,
            Templates = (Templates ?? new List<TakeoffTemplate>()).Select(template => template.Clone()).ToList(),
            Template = (Template ?? BuildDefaultTemplate()).Clone(),
        };
        clone.EnsureTemplatePresets();
        return clone;
    }

    public static TakeoffTemplateConfig BuildDefault()
    {
        TakeoffTemplate template = BuildDefaultTemplate();
        template.Name = DefaultTemplateName;
        var config = new TakeoffTemplateConfig
        {
            BuiltInVersion = CurrentBuiltInVersion,
            ActiveTemplateId = template.Id,
            Template = template.Clone(),
            Templates = [template],
        };
        config.EnsureTemplatePresets();
        return config;
    }

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
                Folder("walls",
                    WallPreset("corners"),
                    WallPreset("ext"),
                    WallPreset("ext 2x6"),
                    WallPreset("ext 2x4"),
                    WallPreset("ext 2x8"),
                    WallPreset("cor"),
                    WallPreset("cor 2x4"),
                    WallPreset("cor 2x6"),
                    WallPreset("cor 2x8"),
                    WallPreset("cor (2) 2x4"),
                    WallPreset("cor (2) 2x6"),
                    WallPreset("cor (2) 2x8"),
                    WallPreset("dem"),
                    WallPreset("dem 2x4"),
                    WallPreset("dem 2x6"),
                    WallPreset("dem 2x8"),
                    WallPreset("dem (2) 2x4"),
                    WallPreset("dem (2) 2x6"),
                    WallPreset("dem (2) 2x8"),
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
                    ParapetPreset()),
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
                    FramingPreset("Blocking for Drywall"),
                    FramingPreset("Blocking for Trusses"),
                    FramingPreset("Ribbon Board"),
                    FramingPreset("Rim Board"),
                    FramingPreset("Blocking"),
                    FramingPreset("Ledger"),
                    FramingPreset("1x3 Cross Blocking"),
                    FramingPreset("Plate"),
                    FramingPreset("Frame")),
            ],
        };
    }

    private static TakeoffTemplateNode WallFloor(string name) =>
        string.Equals(name, "shaft walls", StringComparison.OrdinalIgnoreCase)
            ? ShaftWallsFolder(name)
            : Folder(name,
                WallPreset("corners"),
                WallPreset("ext"),
                WallPreset("ext 2x6"),
                WallPreset("ext 2x4"),
                WallPreset("ext 2x8"),
                WallPreset("cor"),
                WallPreset("cor 2x4"),
                WallPreset("cor 2x6"),
                WallPreset("cor 2x8"),
                WallPreset("cor (2) 2x4"),
                WallPreset("cor (2) 2x6"),
                WallPreset("cor (2) 2x8"),
                WallPreset("dem"),
                WallPreset("dem 2x4"),
                WallPreset("dem 2x6"),
                WallPreset("dem 2x8"),
                WallPreset("dem (2) 2x4"),
                WallPreset("dem (2) 2x6"),
                WallPreset("dem (2) 2x8"),
                WallPreset("furring"),
                WallPreset("2x4 x"),
                WallPreset("2x6 x"),
                WallPreset("2x8 x"),
                WallPreset("2x4 half"),
                WallPreset("2x6 half"));

    private static TakeoffTemplateNode ShaftWallsFolder(string name) =>
        Folder(name,
            ShaftWallPreset("shaft 1st"),
            ShaftWallPreset("shaft 2nd"),
            ShaftWallPreset("shaft 3rd"),
            ShaftWallPreset("shaft 4th"),
            ShaftWallPreset("shaft 5th"));

    internal static TakeoffTemplateNode WallPreset(string name)
    {
        string color = WallPresetColor(name);
        return string.Equals(name, "corners", StringComparison.OrdinalIgnoreCase)
            ? Point(name, color)
            : Line(name, color);
    }

    internal static TakeoffTemplateNode ShaftWallPreset(string name) =>
        Line(name, ShaftWallPresetColor(name));

    internal static TakeoffTemplateNode FramingPreset(string name) =>
        Line(name, FramingPresetColor(name));

    internal static TakeoffTemplateNode ParapetPreset() =>
        Line("prpt 0.0 0.0 0.0", "#7E57C2");

    private static string WallPresetColor(string name) =>
        TryWallPresetColor(name, out string color) ? color : "#FF4444";

    internal static bool TryWallPresetColor(string name, out string color)
    {
        color = name.Trim().ToLowerInvariant() switch
        {
            "corners" => "#FF1744",
            "ext" => "#2E7D32",
            "ext 2x6" => "#00B8D4",
            "ext 2x4" => "#00C853",
            "ext 2x8" => "#64DD17",
            "cor" => "#FF9100",
            "cor 2x4" => "#FF6D00",
            "cor 2x6" => "#FFD600",
            "cor 2x8" => "#AEEA00",
            "cor (2) 2x4" => "#651FFF",
            "cor (2) 2x6" => "#2962FF",
            "cor (2) 2x8" => "#0091EA",
            "dem" => "#2962FF",
            "dem 2x4" => "#C51162",
            "dem 2x6" => "#D500F9",
            "dem 2x8" => "#AA00FF",
            "dem (2) 2x4" => "#6200EA",
            "dem (2) 2x6" => "#304FFE",
            "dem (2) 2x8" => "#00BFA5",
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

    private static string ShaftWallPresetColor(string name) =>
        name.Trim().ToLowerInvariant() switch
        {
            "shaft 1st" => "#00C853",
            "shaft 2nd" => "#00BFA5",
            "shaft 3rd" => "#00B8D4",
            "shaft 4th" => "#0091EA",
            "shaft 5th" => "#2962FF",
            _ => "#00C853",
        };

    private static string FramingPresetColor(string name) =>
        name.Trim().ToLowerInvariant() switch
        {
            "blocking for drywall" => "#2196F3",
            "blocking for trusses" => "#1E88E5",
            "ribbon board" => "#1976D2",
            "rim board" => "#0D47A1",
            "blocking" => "#00BCD4",
            "ledger" => "#009688",
            "1x3 cross blocking" => "#3F51B5",
            "plate" => "#FF9800",
            "frame" => "#795548",
            _ => "#2196F3",
        };

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

    private static string Dir => AppIdentity.RoamingRoot;

    private static string FilePath => Path.Combine(Dir, "templates.json");

    private static string GlobalConfigPath() =>
        Path.Combine(SmartContextStore.GlobalRoot, "presets", "takeoff_templates.json");

    private static string JobConfigPath(OurPlanCoreJob job) =>
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

    public static TakeoffTemplateConfig? LoadJobOverride(OurPlanCoreJob job) =>
        LoadConfig(JobConfigPath(job));

    public static void SaveJobOverride(OurPlanCoreJob job, TakeoffTemplateConfig config) =>
        SaveConfig(JobConfigPath(job), config);

    public static void ClearJobOverride(OurPlanCoreJob job)
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

    public static TakeoffTemplateConfig ResolveConfig(OurPlanCoreJob? job)
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
        clone.EnsureTemplatePresets();
        TakeoffTemplate defaultTemplate = clone.DefaultTemplate();
        if (defaultTemplate.Roots.Count == 0)
            defaultTemplate.Roots = TakeoffTemplateConfig.BuildDefaultTemplate().Roots;
        if (clone.BuiltInVersion < TakeoffTemplateConfig.CurrentBuiltInVersion)
        {
            int previousBuiltInVersion = clone.BuiltInVersion;
            MergeBuiltInDefaults(defaultTemplate.Roots, TakeoffTemplateConfig.BuildDefaultTemplate().Roots);
            if (previousBuiltInVersion < 4)
                ApplyBuiltInTemplateCleanupV4(defaultTemplate.Roots);
            clone.BuiltInVersion = TakeoffTemplateConfig.CurrentBuiltInVersion;
        }
        foreach (TakeoffTemplate template in clone.Templates)
            NormalizeNodeIds(template.Roots);
        clone.SyncActiveTemplateSnapshot();
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

    private static void ApplyBuiltInTemplateCleanupV4(List<TakeoffTemplateNode> roots)
    {
        roots.RemoveAll(node =>
            node.IsFolder &&
            TakeoffTemplateConfig.DeprecatedRootFolderNames.Contains(node.Name));

        foreach (TakeoffTemplateNode node in roots.ToList())
        {
            if (!node.IsFolder)
                continue;

            if (string.Equals(node.Name, "walls", StringComparison.OrdinalIgnoreCase))
            {
                ApplyWallsTemplateV4(node);
            }
            else if (string.Equals(node.Name, "parapets", StringComparison.OrdinalIgnoreCase))
            {
                ReplaceFolderItems(node, [TakeoffTemplateConfig.ParapetPreset()], keepCustomItems: false);
            }
            else if (string.Equals(node.Name, "framing", StringComparison.OrdinalIgnoreCase))
            {
                ReplaceFolderItems(
                    node,
                    TakeoffTemplateConfig.FramingPresetNames.Select(TakeoffTemplateConfig.FramingPreset),
                    keepCustomItems: false);
            }
            else
            {
                ApplyBuiltInTemplateCleanupV4(node.Children);
            }
        }
    }

    private static void ApplyWallsTemplateV4(TakeoffTemplateNode folder)
    {
        ApplyWallPresetFolderV4(folder);
        foreach (TakeoffTemplateNode child in folder.Children.ToList())
        {
            if (!child.IsFolder)
                continue;

            if (string.Equals(child.Name, "shaft walls", StringComparison.OrdinalIgnoreCase))
                ApplyShaftWallsTemplateV4(child);
            else
                ApplyWallPresetFolderV4(child);
        }
    }

    private static void ApplyWallPresetFolderV4(TakeoffTemplateNode folder)
    {
        folder.Children.RemoveAll(child =>
            !child.IsFolder &&
            TakeoffTemplateConfig.DeprecatedWallPresetNames.Contains(child.Name));

        ReplaceFolderItems(
            folder,
            TakeoffTemplateConfig.WallPresetNames.Select(TakeoffTemplateConfig.WallPreset),
            keepCustomItems: true);
    }

    private static void ApplyShaftWallsTemplateV4(TakeoffTemplateNode folder)
    {
        ReplaceFolderItems(
            folder,
            TakeoffTemplateConfig.ShaftWallPresetNames.Select(TakeoffTemplateConfig.ShaftWallPreset),
            keepCustomItems: false);
    }

    private static void ReplaceFolderItems(
        TakeoffTemplateNode folder,
        IEnumerable<TakeoffTemplateNode> desiredItems,
        bool keepCustomItems)
    {
        var desired = desiredItems.Select(item => item.Clone()).ToList();
        var desiredNames = desired
            .Select(item => item.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingByName = folder.Children
            .Where(child => !child.IsFolder && desiredNames.Contains(child.Name))
            .GroupBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var next = new List<TakeoffTemplateNode>();
        foreach (TakeoffTemplateNode desiredItem in desired)
        {
            if (existingByName.TryGetValue(desiredItem.Name, out TakeoffTemplateNode? existing))
            {
                existing.IsFolder = false;
                existing.MeasurementType = desiredItem.MeasurementType;
                existing.Color = desiredItem.Color;
                existing.CountSymbol = desiredItem.CountSymbol;
                next.Add(existing);
                continue;
            }

            next.Add(desiredItem);
        }

        if (keepCustomItems)
            next.AddRange(folder.Children.Where(child => !child.IsFolder && !desiredNames.Contains(child.Name)));

        if (keepCustomItems)
            next.AddRange(folder.Children.Where(child => child.IsFolder));

        folder.Children.Clear();
        folder.Children.AddRange(next);
    }

    private static void NormalizeNodeIds(IEnumerable<TakeoffTemplateNode> nodes)
    {
        foreach (TakeoffTemplateNode node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
                node.Id = Guid.NewGuid().ToString("N");
            node.MeasurementType = OurPlanCoreJobStore.NormalizeMeasurementType(node.MeasurementType);
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
        TakeoffTemplateConfig writable = (config ?? TakeoffTemplateConfig.BuildDefault()).Clone();
        writable.SyncActiveTemplateSnapshot();
        IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(writable, JsonOptions));
    }
}

public static class TakeoffTemplateRouting
{
    public static string ResolveDestinationFolder(
        OurPlanCoreJob job,
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

            string? next = OurPlanCoreJobStore.GetOrderedChildDirectories(current)
                .Where(path => !OurPlanCoreJobStore.IsTakeoffItemFolder(path))
                .FirstOrDefault(path =>
                    string.Equals(OurPlanCoreJobStore.DisplayName(path), clean, StringComparison.OrdinalIgnoreCase));

            if (next == null)
                return job.TakeoffsRoot;

            current = next;
        }

        return current;
    }
}
