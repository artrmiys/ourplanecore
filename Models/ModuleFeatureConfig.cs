using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace OurPlaneCore;

public enum ModuleId
{
    Annotations,
    PdfLayers,
    SheetOverlay,
    SheetManager,
    TakeoffManager,
    AdvancedTakeoffTools,
    TakeoffAutomation,
    TakeoffTemplates,
    Estimating,
    ReportBuilder,
    Materials,
    Ai,
    ThreeD,
    Bookmarks,
    QuickCalculator,
    PdfOutput,
    DetachedSheets,
    ExcelIntegration,
}

public sealed record ModuleFeatureDefinition(
    ModuleId Id,
    string Group,
    string Name,
    string Description,
    bool DefaultEnabled);

public static class ModuleFeatureCatalog
{
    private static readonly ReadOnlyCollection<ModuleFeatureDefinition> Definitions =
        Array.AsReadOnly(new[]
        {
            new ModuleFeatureDefinition(ModuleId.Annotations, "Productivity", "Annotations", "Drawing and review annotations on sheets.", true),
            new ModuleFeatureDefinition(ModuleId.PdfLayers, "Documents", "PDF Layers", "PDF layer visibility, highlighting, and layer trace tools.", true),
            new ModuleFeatureDefinition(ModuleId.SheetOverlay, "Documents", "Sheet Overlay", "Overlay and align one sheet with another sheet.", true),
            new ModuleFeatureDefinition(ModuleId.SheetManager, "Documents", "Sheet Manager", "Manage sheet names, metadata, scale, raster, and page properties.", true),
            new ModuleFeatureDefinition(ModuleId.TakeoffManager, "Takeoff", "Takeoff Manager", "Manage takeoff folders, items, sections, and properties.", true),
            new ModuleFeatureDefinition(ModuleId.AdvancedTakeoffTools, "Takeoff", "Advanced Takeoff Tools", "Advanced tracing, matching, combine, beam, opening, and editing tools.", true),
            new ModuleFeatureDefinition(ModuleId.TakeoffAutomation, "Takeoff", "Takeoff Automation", "Automated takeoff creation and page-to-takeoff workflows.", true),
            new ModuleFeatureDefinition(ModuleId.TakeoffTemplates, "Takeoff", "Takeoff Templates", "Editable takeoff tree and item templates.", true),
            new ModuleFeatureDefinition(ModuleId.Estimating, "Takeoff", "Estimating", "Estimate tables, quantities, pricing, and estimating workspace.", true),
            new ModuleFeatureDefinition(ModuleId.ReportBuilder, "Output", "Report Builder", "Build structured reports from job and takeoff data.", true),
            new ModuleFeatureDefinition(ModuleId.Materials, "Output", "Materials", "Material extraction and material output workflows.", true),
            new ModuleFeatureDefinition(ModuleId.Ai, "Assistance", "AI", "AI Inbox, assisted takeoff actions, markers, and review workflows.", true),
            new ModuleFeatureDefinition(ModuleId.ThreeD, "Assistance", "3D", "3D model, massing, roof, wall, and framing workflows.", true),
            new ModuleFeatureDefinition(ModuleId.Bookmarks, "Productivity", "Bookmarks", "Sheet and crop bookmarks for quick navigation.", true),
            new ModuleFeatureDefinition(ModuleId.QuickCalculator, "Productivity", "Quick Calculator", "Calculator and feet-inch conversion panel.", true),
            new ModuleFeatureDefinition(ModuleId.PdfOutput, "Output", "PDF Output", "PDF export, sheet output, and print-ready deliverables.", true),
            new ModuleFeatureDefinition(ModuleId.DetachedSheets, "Documents", "Detached Sheets", "Open and manage sheets in detached windows.", true),
            new ModuleFeatureDefinition(ModuleId.ExcelIntegration, "Output", "Excel Integration", "Spreadsheet-style output and Excel integration workflows.", true),
        });

    public static IReadOnlyList<ModuleFeatureDefinition> All => Definitions;

    public static ModuleFeatureDefinition Definition(ModuleId id) => Get(id);

    public static ModuleFeatureDefinition Get(ModuleId id)
    {
        foreach (ModuleFeatureDefinition definition in Definitions)
        {
            if (definition.Id == id)
                return definition;
        }

        throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown module id.");
    }

    public static bool TryGet(ModuleId id, out ModuleFeatureDefinition? definition)
    {
        foreach (ModuleFeatureDefinition candidate in Definitions)
        {
            if (candidate.Id != id)
                continue;

            definition = candidate;
            return true;
        }

        definition = null;
        return false;
    }
}

public sealed class ModuleFeatureConfig
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; }

    public Dictionary<string, bool> States { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public static ModuleFeatureConfig BuildDefault()
    {
        var config = new ModuleFeatureConfig
        {
            SchemaVersion = CurrentSchemaVersion,
        };

        foreach (ModuleFeatureDefinition definition in ModuleFeatureCatalog.All)
            config.States[definition.Id.ToString()] = definition.DefaultEnabled;

        return config;
    }

    public ModuleFeatureConfig Clone()
    {
        var clone = new ModuleFeatureConfig
        {
            SchemaVersion = SchemaVersion,
            States = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
        };

        if (States != null)
        {
            foreach ((string key, bool value) in States)
                clone.States[key] = value;
        }

        return clone;
    }

    public ModuleFeatureConfig UpgradeForCurrentSchema() =>
        UpgradeForCurrentSchema(this);

    public static ModuleFeatureConfig UpgradeForCurrentSchema(ModuleFeatureConfig? config)
    {
        ModuleFeatureConfig upgraded = config?.Clone() ?? BuildDefault();
        var normalizedStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        if (upgraded.States != null)
        {
            foreach ((string key, bool value) in upgraded.States)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                string normalizedKey = TryNormalizeModuleKey(key, out ModuleId id)
                    ? id.ToString()
                    : key.Trim();
                normalizedStates[normalizedKey] = value;
            }
        }

        foreach (ModuleFeatureDefinition definition in ModuleFeatureCatalog.All)
            normalizedStates.TryAdd(definition.Id.ToString(), definition.DefaultEnabled);

        upgraded.SchemaVersion = CurrentSchemaVersion;
        upgraded.States = normalizedStates;
        return upgraded;
    }

    public bool IsEnabled(ModuleId id)
    {
        if (States != null && States.TryGetValue(id.ToString(), out bool enabled))
            return enabled;

        if (States != null)
        {
            foreach ((string key, bool value) in States)
            {
                if (TryNormalizeModuleKey(key, out ModuleId storedId) && storedId == id)
                    return value;
            }
        }

        return ModuleFeatureCatalog.Get(id).DefaultEnabled;
    }

    public void SetEnabled(ModuleId id, bool enabled)
    {
        States ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        States[id.ToString()] = enabled;
    }

    private static bool TryNormalizeModuleKey(string key, out ModuleId id) =>
        Enum.TryParse(key.Trim(), ignoreCase: true, out id) &&
        Enum.IsDefined(typeof(ModuleId), id);
}
