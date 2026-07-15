using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OurPlanCore;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SheetMetadataImportPolicy
{
    // Migration-only policy that reproduces the current import behaviour.
    LegacyAutoApply,
    AnalyzeOnly,
    Preview,
    AutoApplyHighConfidence,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SheetMetadataDetectorMode
{
    Legacy,
    PreciseV2,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SheetMetadataConfidence
{
    Low,
    Medium,
    High,
}

// Deterministic policy shared by PDF sheet-name, suffix, and scale workflows.
// BuildDefault intentionally remains legacy-compatible; PreciseV2 is opt-in.
public sealed class SheetMetadataConfig
{
    public const int CurrentSchemaVersion = 2;
    public const string LegacyPresetName = "Legacy";
    public const string PreciseV2PresetName = "Precise v2";
    public static IReadOnlyList<string> PresetNames { get; } =
        [LegacyPresetName, PreciseV2PresetName];

    public int SchemaVersion { get; set; }
    public string PresetName { get; set; } = LegacyPresetName;

    // Detection and application are independent. In particular, Legacy + Preview
    // must use the proven legacy detector without silently switching algorithms.
    public SheetMetadataDetectorMode DetectorMode { get; set; }
    public SheetMetadataImportPolicy ImportPolicy { get; set; }

    public bool PreserveExistingManualName { get; set; }
    public bool PreserveExistingManualSuffix { get; set; }
    public bool PreserveExistingManualScale { get; set; }
    public bool PreserveArbitraryExistingMultiTokenSuffix { get; set; }

    public bool EnableSheetIndexEvidence { get; set; }
    public bool EnableTitleBlockLabelEvidence { get; set; }
    public bool EnableTitleBlockEvidence { get; set; }
    public bool EnableTitleBlockScaleEvidence { get; set; }
    public bool EnableBodyEvidence { get; set; }
    public bool AllowScaleInference { get; set; }

    public SheetMetadataConfidence MinimumRenameConfidence { get; set; }
    public SheetMetadataConfidence MinimumSuffixConfidence { get; set; }
    public SheetMetadataConfidence MinimumScaleConfidence { get; set; }

    public List<string> ScaleCapableSuffixes { get; set; } = [];
    public List<string> NoScaleSuffixes { get; set; } = [];
    public List<string> NoScaleTerminalTokens { get; set; } = [];
    public List<string> CompoundSuffixes { get; set; } = [];
    public List<SheetSuffixRule> SuffixRules { get; set; } = [];
    public List<SheetMetadataLabelOverride> SheetLabelOverrides { get; set; } = [];

    public SheetMetadataConfig Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        PresetName = PresetName ?? LegacyPresetName,
        DetectorMode = DetectorMode,
        ImportPolicy = ImportPolicy,
        PreserveExistingManualName = PreserveExistingManualName,
        PreserveExistingManualSuffix = PreserveExistingManualSuffix,
        PreserveExistingManualScale = PreserveExistingManualScale,
        PreserveArbitraryExistingMultiTokenSuffix = PreserveArbitraryExistingMultiTokenSuffix,
        EnableSheetIndexEvidence = EnableSheetIndexEvidence,
        EnableTitleBlockLabelEvidence = EnableTitleBlockLabelEvidence,
        EnableTitleBlockEvidence = EnableTitleBlockEvidence,
        EnableTitleBlockScaleEvidence = EnableTitleBlockScaleEvidence,
        EnableBodyEvidence = EnableBodyEvidence,
        AllowScaleInference = AllowScaleInference,
        MinimumRenameConfidence = MinimumRenameConfidence,
        MinimumSuffixConfidence = MinimumSuffixConfidence,
        MinimumScaleConfidence = MinimumScaleConfidence,
        ScaleCapableSuffixes = ScaleCapableSuffixes?.ToList() ?? [],
        NoScaleSuffixes = NoScaleSuffixes?.ToList() ?? [],
        NoScaleTerminalTokens = NoScaleTerminalTokens?.ToList() ?? [],
        CompoundSuffixes = CompoundSuffixes?.ToList() ?? [],
        SuffixRules = SuffixRules?.OfType<SheetSuffixRule>().Select(rule => rule.Clone()).ToList() ?? [],
        SheetLabelOverrides = SheetLabelOverrides?.OfType<SheetMetadataLabelOverride>().Select(item => item.Clone()).ToList() ?? [],
    };

    public static SheetMetadataConfig BuildDefault() => BuildLegacy();

    public static SheetMetadataConfig BuildLegacy() => new()
    {
        SchemaVersion = CurrentSchemaVersion,
        PresetName = LegacyPresetName,
        DetectorMode = SheetMetadataDetectorMode.Legacy,
        ImportPolicy = SheetMetadataImportPolicy.LegacyAutoApply,
        PreserveExistingManualName = false,
        PreserveExistingManualSuffix = false,
        PreserveExistingManualScale = false,
        PreserveArbitraryExistingMultiTokenSuffix = false,
        EnableSheetIndexEvidence = false,
        EnableTitleBlockLabelEvidence = true,
        EnableTitleBlockEvidence = true,
        EnableTitleBlockScaleEvidence = true,
        EnableBodyEvidence = true,
        AllowScaleInference = true,
        MinimumRenameConfidence = SheetMetadataConfidence.Low,
        MinimumSuffixConfidence = SheetMetadataConfidence.Low,
        MinimumScaleConfidence = SheetMetadataConfidence.Low,
        ScaleCapableSuffixes = BuildLegacyScaleCapableSuffixes(),
        NoScaleSuffixes = BuildLegacyNoScaleSuffixes(),
        NoScaleTerminalTokens = BuildDefaultNoScaleTerminalTokens(),
        CompoundSuffixes = BuildLegacyCompoundSuffixes(),
        SuffixRules = SheetMetadataSuffixCatalog.BuildLegacy(),
        SheetLabelOverrides = [],
    };

    public static SheetMetadataConfig BuildPreciseV2() => new()
    {
        SchemaVersion = CurrentSchemaVersion,
        PresetName = PreciseV2PresetName,
        DetectorMode = SheetMetadataDetectorMode.PreciseV2,
        ImportPolicy = SheetMetadataImportPolicy.Preview,
        PreserveExistingManualName = true,
        PreserveExistingManualSuffix = true,
        PreserveExistingManualScale = true,
        PreserveArbitraryExistingMultiTokenSuffix = true,
        EnableSheetIndexEvidence = true,
        EnableTitleBlockLabelEvidence = true,
        EnableTitleBlockEvidence = true,
        EnableTitleBlockScaleEvidence = true,
        EnableBodyEvidence = true,
        AllowScaleInference = false,
        MinimumRenameConfidence = SheetMetadataConfidence.Medium,
        MinimumSuffixConfidence = SheetMetadataConfidence.Medium,
        MinimumScaleConfidence = SheetMetadataConfidence.High,
        ScaleCapableSuffixes = BuildPreciseScaleCapableSuffixes(),
        NoScaleSuffixes = BuildPreciseNoScaleSuffixes(),
        NoScaleTerminalTokens = BuildDefaultNoScaleTerminalTokens(),
        CompoundSuffixes = BuildPreciseCompoundSuffixes(),
        SuffixRules = SheetMetadataSuffixCatalog.BuildPreciseV2(),
        SheetLabelOverrides = [],
    };

    public static SheetMetadataConfig BuildPreset(string? name) =>
        string.Equals(name, PreciseV2PresetName, StringComparison.OrdinalIgnoreCase)
            ? BuildPreciseV2()
            : BuildLegacy();

    public bool HasSameBehaviorAs(SheetMetadataConfig other)
    {
        SheetMetadataConfig left = UpgradeForCurrentSchema(this).Clone();
        SheetMetadataConfig right = UpgradeForCurrentSchema(other).Clone();
        left.PresetName = "behavior-compare";
        right.PresetName = "behavior-compare";
        return string.Equals(
            JsonSerializer.Serialize(left),
            JsonSerializer.Serialize(right),
            StringComparison.Ordinal);
    }

    public static SheetMetadataConfig UpgradeForCurrentSchema(SheetMetadataConfig? config)
    {
        if (config == null || config.SchemaVersion <= 0)
            return BuildDefault();

        int sourceSchemaVersion = config.SchemaVersion;
        SheetMetadataConfig upgraded = config.Clone();
        upgraded.PresetName = string.IsNullOrWhiteSpace(upgraded.PresetName)
            ? "Custom"
            : upgraded.PresetName.Trim();
        if (sourceSchemaVersion < 2)
        {
            upgraded.DetectorMode = string.Equals(
                upgraded.PresetName,
                PreciseV2PresetName,
                StringComparison.OrdinalIgnoreCase)
                ? SheetMetadataDetectorMode.PreciseV2
                : SheetMetadataDetectorMode.Legacy;
            upgraded.EnableTitleBlockLabelEvidence = upgraded.EnableTitleBlockEvidence;
            upgraded.EnableTitleBlockScaleEvidence = upgraded.EnableTitleBlockEvidence;
        }

        SheetMetadataConfig fallback = upgraded.DetectorMode == SheetMetadataDetectorMode.PreciseV2
            ? BuildPreciseV2()
            : BuildLegacy();
        bool migrateMissingCollections = sourceSchemaVersion < 2;
        upgraded.ScaleCapableSuffixes = NormalizeSuffixes(
            SelectCollection(upgraded.ScaleCapableSuffixes, fallback.ScaleCapableSuffixes, migrateMissingCollections));
        upgraded.NoScaleSuffixes = NormalizeSuffixes(
            SelectCollection(upgraded.NoScaleSuffixes, fallback.NoScaleSuffixes, migrateMissingCollections));
        upgraded.NoScaleTerminalTokens = NormalizeSuffixes(
            SelectCollection(upgraded.NoScaleTerminalTokens, fallback.NoScaleTerminalTokens, migrateMissingCollections));
        upgraded.CompoundSuffixes = NormalizeSuffixes(
            SelectCollection(upgraded.CompoundSuffixes, fallback.CompoundSuffixes, migrateMissingCollections));
        upgraded.SuffixRules = upgraded.SuffixRules
            .OfType<SheetSuffixRule>()
            .Select(rule => rule.Clone())
            .OrderBy(rule => rule.Priority)
            .ToList();
        if (migrateMissingCollections && upgraded.SuffixRules.Count == 0)
            upgraded.SuffixRules = fallback.SuffixRules.Select(rule => rule.Clone()).ToList();
        upgraded.SheetLabelOverrides = upgraded.SheetLabelOverrides
            .OfType<SheetMetadataLabelOverride>()
            .Select(item => item.Clone())
            .ToList();
        upgraded.SchemaVersion = CurrentSchemaVersion;
        return upgraded;
    }

    public static SheetMetadataConfig Upgrade(SheetMetadataConfig? config) =>
        UpgradeForCurrentSchema(config);

    public bool IsScaleCapableSuffix(string? suffix) =>
        ContainsSuffix(ScaleCapableSuffixes, suffix);

    public bool IsNoScaleSuffix(string? suffix) =>
        ContainsSuffix(NoScaleSuffixes, suffix);

    public bool IsCompoundSuffix(string? suffix) =>
        ContainsSuffix(CompoundSuffixes, suffix);

    public bool ShouldPreserveExistingSuffix(string? suffix)
    {
        string normalized = NormalizeSuffix(suffix);
        return IsCompoundSuffix(normalized) ||
               (PreserveArbitraryExistingMultiTokenSuffix && normalized.Contains(' '));
    }

    // A matched rule is authoritative. Otherwise exact no-scale entries win,
    // then exact scale-capable entries, then the editable terminal-token gate.
    public bool ShouldSkipScaleSuffix(string? suffix, bool? matchedRuleSkipScale = null)
    {
        if (matchedRuleSkipScale.HasValue)
            return matchedRuleSkipScale.Value;
        if (IsNoScaleSuffix(suffix))
            return true;
        if (IsScaleCapableSuffix(suffix))
            return false;

        string terminal = NormalizeSuffix(suffix)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault() ?? "";
        return ContainsSuffix(NoScaleTerminalTokens, terminal);
    }

    private static bool ContainsSuffix(IEnumerable<string>? values, string? suffix)
    {
        string normalized = NormalizeSuffix(suffix);
        return normalized.Length > 0 &&
               (values ?? []).Any(value =>
                   string.Equals(NormalizeSuffix(value), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> SelectCollection(
        IEnumerable<string>? values,
        IEnumerable<string> fallback,
        bool migrateMissingCollections)
    {
        List<string> materialized = values?.ToList() ?? [];
        return migrateMissingCollections && materialized.Count == 0 ? fallback : materialized;
    }

    private static List<string> NormalizeSuffixes(IEnumerable<string>? values)
    {
        List<string> normalized = (values ?? [])
            .Select(NormalizeSuffix)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return normalized;
    }

    private static string NormalizeSuffix(string? value) =>
        string.Join(" ", (value ?? "")
            .Trim()
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static List<string> BuildLegacyScaleCapableSuffixes() =>
    [
        "1st", "2nd", "3rd", "4th", "5th", "6th", "7th", "8th",
        "rf", "f", "b", "sec", "el", "u", "v", "wt", "ft", "sv", "sw", "shw",
        "fr n", "df", "wt pl", "fl pl", "u sc", "elev sec", "str sec", "d sec",
    ];

    private static List<string> BuildLegacyNoScaleSuffixes() =>
        ["d", "n", "sc", "t", "w d sc", "f d", "wd d", "jamb d"];

    private static List<string> BuildDefaultNoScaleTerminalTokens() =>
        ["d", "n", "sc", "t"];

    private static List<string> BuildLegacyCompoundSuffixes() =>
    [
        "fr n", "wt pl", "fl pl", "u sc", "elev sec", "str sec", "d sec",
        "w d sc", "f d", "wd d", "jamb d",
    ];

    private static List<string> BuildPreciseScaleCapableSuffixes() =>
    [
        "1st", "2nd", "3rd", "4th", "5th", "6th", "7th", "8th",
        "rf", "f", "b", "sec", "el", "u", "v", "wt", "ft", "sv", "sw", "shw",
        "df", "wt pl", "fl pl", "elev sec", "str sec", "d sec",
        "b rcp", "1st rcp", "2nd rcp", "3rd rcp", "4th rcp", "5th rcp",
        "6th rcp", "7th rcp", "8th rcp", "rf rcp",
    ];

    private static List<string> BuildPreciseNoScaleSuffixes() =>
    [
        "d", "n", "sc", "t", "fr n", "u sc",
        "w d sc", "f d", "wd d", "jamb d",
    ];

    private static List<string> BuildPreciseCompoundSuffixes() =>
    [
        "fr n", "wt pl", "fl pl", "u sc", "elev sec", "str sec", "d sec",
        "w d sc", "f d", "wd d", "jamb d",
        "b rcp", "1st rcp", "2nd rcp", "3rd rcp", "4th rcp", "5th rcp",
        "6th rcp", "7th rcp", "8th rcp", "rf rcp",
    ];
}

// Active holder used by detector, preview, import, and apply workflows.
public static class SheetMetadataRulesService
{
    public static SheetMetadataConfig Active { get; private set; } =
        SheetMetadataConfig.BuildDefault();

    public static void Install(SheetMetadataConfig? config) =>
        Active = SheetMetadataConfig.UpgradeForCurrentSchema(config).Clone();
}
