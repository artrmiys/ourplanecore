using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace OurPlanCore;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SheetMetadataEvidenceField
{
    SheetLabel,
    SheetTitle,
    TitleAndBody,
    DetectorFlags,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SheetMetadataMatchKind
{
    Prefix,
    Exact,
    ContainsAny,
    ContainsAll,
    Regex,
    NumberRange,
    FloorLevel,
    SheetLabelFloor,
    Flag,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SheetMetadataOverrideAction
{
    Keep,
    Set,
    Clear,
}

public sealed class SheetSuffixRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; }
    public SheetMetadataEvidenceField EvidenceField { get; set; }
    public SheetMetadataMatchKind MatchKind { get; set; }
    public string Pattern { get; set; } = "";
    public List<string> Keywords { get; set; } = [];
    public List<string> ExcludedKeywords { get; set; } = [];
    public SheetMetadataEvidenceField? ExclusionEvidenceField { get; set; }
    public List<string> RequiredFlags { get; set; } = [];
    public string SheetPrefix { get; set; } = "";
    public int? MinimumSheetNumber { get; set; }
    public int? MaximumSheetNumber { get; set; }
    public string OutputSuffix { get; set; } = "";
    public SheetMetadataConfidence Confidence { get; set; } = SheetMetadataConfidence.Medium;
    public bool SkipScale { get; set; }

    [JsonIgnore]
    public string KeywordsText
    {
        get => string.Join("; ", Keywords ?? []);
        set => Keywords = SplitValues(value);
    }

    [JsonIgnore]
    public string ExcludedKeywordsText
    {
        get => string.Join("; ", ExcludedKeywords ?? []);
        set => ExcludedKeywords = SplitValues(value);
    }

    [JsonIgnore]
    public string RequiredFlagsText
    {
        get => string.Join("; ", RequiredFlags ?? []);
        set => RequiredFlags = SplitValues(value);
    }

    public SheetSuffixRule Clone() => new()
    {
        Id = Id ?? "",
        Enabled = Enabled,
        Priority = Priority,
        EvidenceField = EvidenceField,
        MatchKind = MatchKind,
        Pattern = Pattern ?? "",
        Keywords = Keywords?.OfType<string>().ToList() ?? [],
        ExcludedKeywords = ExcludedKeywords?.OfType<string>().ToList() ?? [],
        ExclusionEvidenceField = ExclusionEvidenceField,
        RequiredFlags = RequiredFlags?.OfType<string>().ToList() ?? [],
        SheetPrefix = SheetPrefix ?? "",
        MinimumSheetNumber = MinimumSheetNumber,
        MaximumSheetNumber = MaximumSheetNumber,
        OutputSuffix = OutputSuffix ?? "",
        Confidence = Confidence,
        SkipScale = SkipScale,
    };

    internal static List<string> SplitValues(string? value) =>
        (value ?? "")
            .Split([';', ',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim().ToLowerInvariant())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

public sealed class SheetMetadataLabelOverride
{
    public bool Enabled { get; set; } = true;
    public string SourcePdfPattern { get; set; } = "";
    public string SheetLabel { get; set; } = "";
    public string OutputPageName { get; set; } = "";
    public SheetMetadataOverrideAction SuffixAction { get; set; } = SheetMetadataOverrideAction.Keep;
    public string OutputSuffix { get; set; } = "";
    public SheetMetadataOverrideAction ScaleAction { get; set; } = SheetMetadataOverrideAction.Keep;
    public string ScaleText { get; set; } = "";

    public SheetMetadataLabelOverride Clone() => new()
    {
        Enabled = Enabled,
        SourcePdfPattern = SourcePdfPattern ?? "",
        SheetLabel = SheetLabel ?? "",
        OutputPageName = OutputPageName ?? "",
        SuffixAction = SuffixAction,
        OutputSuffix = OutputSuffix ?? "",
        ScaleAction = ScaleAction,
        ScaleText = ScaleText ?? "",
    };
}

public static class SheetMetadataSuffixCatalog
{
    public static List<SheetSuffixRule> BuildLegacy()
    {
        var rules = new List<SheetSuffixRule>();
        void Add(
            string id,
            SheetMetadataEvidenceField field,
            SheetMetadataMatchKind kind,
            string output,
            bool skip,
            string pattern = "",
            string prefix = "",
            string keywords = "",
            string excluded = "",
            SheetMetadataEvidenceField? exclusionField = null,
            string requiredFlags = "",
            int? min = null,
            int? max = null,
            SheetMetadataConfidence confidence = SheetMetadataConfidence.Medium)
        {
            rules.Add(new SheetSuffixRule
            {
                Id = id,
                Priority = (rules.Count + 1) * 10,
                EvidenceField = field,
                MatchKind = kind,
                Pattern = pattern,
                Keywords = SheetSuffixRule.SplitValues(keywords),
                ExcludedKeywords = SheetSuffixRule.SplitValues(excluded),
                ExclusionEvidenceField = exclusionField,
                RequiredFlags = SheetSuffixRule.SplitValues(requiredFlags),
                SheetPrefix = prefix,
                MinimumSheetNumber = min,
                MaximumSheetNumber = max,
                OutputSuffix = output,
                Confidence = confidence,
                SkipScale = skip,
            });
        }

        Add("label-detail", SheetMetadataEvidenceField.SheetLabel, SheetMetadataMatchKind.Prefix, "d", true, prefix: "d");
        Add("label-cover", SheetMetadataEvidenceField.SheetLabel, SheetMetadataMatchKind.Regex, "n", true, pattern: "^(title|cover)$");
        Add("label-schedule", SheetMetadataEvidenceField.SheetLabel, SheetMetadataMatchKind.Prefix, "sc", true, prefix: "sch");
        Add("label-code-note", SheetMetadataEvidenceField.SheetLabel, SheetMetadataMatchKind.Prefix, "n", true, prefix: "cd", excluded: "plan", exclusionField: SheetMetadataEvidenceField.SheetTitle);
        Add("presentation-intentional-blank", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "", true, keywords: "presentation; renderings; perspectives; omitted", confidence: SheetMetadataConfidence.High);
        Add("general-information", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "n", true, keywords: "drawing list; title sheet; accessibility standards", confidence: SheetMetadataConfidence.High);
        Add("interior-finish", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "f", false, prefix: "i", keywords: "finish; finishes");
        Add("rcp-floor", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.FloorLevel, "{floor} rcp", false, prefix: "rc", keywords: "reflected ceiling plan");
        Add("foundation-plan", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "f", false, keywords: "foundation plan");
        Add("struct-s5-s7-detail-sections", SheetMetadataEvidenceField.SheetLabel, SheetMetadataMatchKind.Regex, "d", true, pattern: "^s[567]\\.1(?:00)?$", prefix: "s", confidence: SheetMetadataConfidence.High);
        Add("struct-700-sections", SheetMetadataEvidenceField.SheetLabel, SheetMetadataMatchKind.NumberRange, "sec", false, prefix: "s", excluded: "section", exclusionField: SheetMetadataEvidenceField.SheetTitle, min: 700, max: 799);
        Add("struct-800-details", SheetMetadataEvidenceField.SheetLabel, SheetMetadataMatchKind.NumberRange, "d", true, prefix: "s", min: 800, max: 899);
        Add("struct-notes", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.Regex, "n", true, pattern: "\\bnotes?\\b", prefix: "s");
        Add("fire-rating-note", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "fr n", false, keywords: "life safety; fire rating; fire rated; fire resistance");
        Add("draft-stopping", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "df", false, keywords: "draft stopping", excluded: "ul");
        Add("ul-wall-type", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAll, "wt", true, keywords: "ul; draft stopping|assembl");
        Add("window-door-schedule", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAll, "w d sc", true, keywords: "door schedule; window type|door type");
        Add("finish-specifications-schedule", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAll, "sc", true, keywords: "finish; schedule", confidence: SheetMetadataConfidence.High);
        Add("room-finish-schedule", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAll, "sc", true, keywords: "room finish; schedule");
        Add("accessible-unit-schedule", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAll, "sc", true, keywords: "accessible unit; schedule");
        Add("unit-summary-schedule", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAll, "u sc", true, keywords: "unit; schedule");
        Add("unit-type-schedule", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "u sc", false, keywords: "accessible unit type; unit type", excluded: "plan");
        Add("overall-floor-plan", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "fl pl", false, keywords: "overall floor plan");
        Add("wall-type-plan", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAll, "wt pl", false, keywords: "wall type; plan");
        Add("enlarged-common-area", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "u", false, keywords: "enlarged common area", confidence: SheetMetadataConfidence.High);
        Add("interior-partitions", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "f", false, keywords: "interior partition", confidence: SheetMetadataConfidence.High);
        Add("interior-elevation", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "f", false, keywords: "interior elevation");
        Add("elevator-section", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAll, "elev sec", false, keywords: "elevator; section");
        Add("stair-section", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAll, "str sec", false, keywords: "stair; section");
        Add("arch-300-wall-section", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "sec", false, prefix: "a", keywords: "wall section", min: 300, max: 399, confidence: SheetMetadataConfidence.High);
        Add("wall-section", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "d sec", false, keywords: "wall section");
        Add("arch-interior-detail", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAll, "f", false, prefix: "a", keywords: "interior; detail", confidence: SheetMetadataConfidence.High);
        Add("exterior-assemblies", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "d", true, keywords: "exterior assembl", confidence: SheetMetadataConfidence.High);
        Add("vertical-circulation", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "d", true, keywords: "vertical circulation", confidence: SheetMetadataConfidence.High);
        Add("arch-700-jamb", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "jamb d", true, prefix: "a", keywords: "miscellaneous detail", min: 700, max: 700);
        Add("struct-500-foundation-detail", SheetMetadataEvidenceField.DetectorFlags, SheetMetadataMatchKind.Flag, "f d", true, pattern: "details", prefix: "s", min: 500, max: 500);
        Add("struct-510-512-wood-detail", SheetMetadataEvidenceField.DetectorFlags, SheetMetadataMatchKind.Flag, "wd d", true, pattern: "details", prefix: "s", min: 510, max: 512, confidence: SheetMetadataConfidence.High);
        Add("struct-wood-detail", SheetMetadataEvidenceField.TitleAndBody, SheetMetadataMatchKind.ContainsAny, "wd d", true, prefix: "s", keywords: "wood; framing; joist; stud wall; beam; header; sheathing; holdown; hold down; microlam; lvl; truss", requiredFlags: "details");
        Add("struct-foundation-detail", SheetMetadataEvidenceField.TitleAndBody, SheetMetadataMatchKind.ContainsAny, "f d", true, prefix: "s", keywords: "foundation; footing; slab on grade; engineered fill", requiredFlags: "details");
        Add("struct-shear-schedule", SheetMetadataEvidenceField.DetectorFlags, SheetMetadataMatchKind.Flag, "shw", true, pattern: "shear+schedule", prefix: "s");
        Add("struct-s902-shear", SheetMetadataEvidenceField.SheetLabel, SheetMetadataMatchKind.NumberRange, "shw", true, prefix: "s", min: 902, max: 902, confidence: SheetMetadataConfidence.High);
        Add("shear-detail", SheetMetadataEvidenceField.DetectorFlags, SheetMetadataMatchKind.Flag, "shw", true, pattern: "shear+details");
        Add("shear", SheetMetadataEvidenceField.DetectorFlags, SheetMetadataMatchKind.Flag, "shw", false, pattern: "shear");
        Add("struct-detail", SheetMetadataEvidenceField.DetectorFlags, SheetMetadataMatchKind.Flag, "d", true, pattern: "details", prefix: "s");
        Add("generic-notes-regex", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.Regex, "n", true, pattern: "\\bnotes?\\b");
        Add("generic-index-regex", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.Regex, "n", true, pattern: "\\bindex\\b");
        Add("notes", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "n", true, keywords: "general notes; cover; sheet index; code data; fire separation; garage ventilation; matrices; fixture calculation; ul assemblies; special inspections");
        Add("schedule", SheetMetadataEvidenceField.DetectorFlags, SheetMetadataMatchKind.Flag, "sc", true, pattern: "schedule");
        Add("wall-type", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "wt", true, keywords: "wall type; wall types; partition type; partition types");
        Add("floor-type", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "ft", true, keywords: "floor type; floor types; floor/ceiling; floor-ceiling; floor/clg; floor assembly; floor assemblies");
        Add("arch-finish", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "f", false, prefix: "a", keywords: "finish; finishes");
        Add("site-visit", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "sv", false, keywords: "site visit; survey");
        Add("views", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.Regex, "v", false, pattern: "\\bviews?\\b");
        Add("unit-plan", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.Regex, "u", false, pattern: "\\bunits?\\s+plans?\\b|\\bunit\\b|kitchen|bath");
        Add("unit-label", SheetMetadataEvidenceField.SheetLabel, SheetMetadataMatchKind.Regex, "u", false, pattern: "u\\d+");
        Add("arch-label-floor", SheetMetadataEvidenceField.SheetLabel, SheetMetadataMatchKind.SheetLabelFloor, "{floor}", false, prefix: "a", confidence: SheetMetadataConfidence.High);
        Add("struct-section-detail", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "d", true, prefix: "s", keywords: "section", min: 500, max: 799);
        Add("struct-section", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "sec", false, prefix: "s", keywords: "section");
        Add("floor", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.FloorLevel, "{floor}", false);
        Add("roof", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "rf", false, keywords: "roof");
        Add("elevation", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "el", false, keywords: "elevation");
        Add("struct-500-fallback", SheetMetadataEvidenceField.SheetLabel, SheetMetadataMatchKind.NumberRange, "d", true, prefix: "s", min: 500, max: 699, confidence: SheetMetadataConfidence.Low);
        Add("section", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "sec", false, keywords: "section");
        Add("profile", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "d", true, keywords: "profile; profiles");
        Add("detail", SheetMetadataEvidenceField.DetectorFlags, SheetMetadataMatchKind.Flag, "d", true, pattern: "details");
        Add("foundation", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "f", false, keywords: "foundation");
        Add("basement", SheetMetadataEvidenceField.SheetTitle, SheetMetadataMatchKind.ContainsAny, "b", false, keywords: "basement");
        Add("title-prefix", SheetMetadataEvidenceField.SheetLabel, SheetMetadataMatchKind.Prefix, "t", true, prefix: "t");
        Add("low-number-note", SheetMetadataEvidenceField.SheetLabel, SheetMetadataMatchKind.NumberRange, "n", true, pattern: "g|t|a|s", min: 0, max: 99, confidence: SheetMetadataConfidence.Low);
        Add("arch-200-elevation", SheetMetadataEvidenceField.SheetLabel, SheetMetadataMatchKind.NumberRange, "el", false, prefix: "a", min: 200, max: 299, confidence: SheetMetadataConfidence.Low);
        Add("arch-300-section", SheetMetadataEvidenceField.SheetLabel, SheetMetadataMatchKind.NumberRange, "sec", false, prefix: "a", min: 300, max: 499, confidence: SheetMetadataConfidence.Low);
        Add("arch-500-unit", SheetMetadataEvidenceField.SheetLabel, SheetMetadataMatchKind.NumberRange, "u", false, prefix: "a", min: 500, max: 599, confidence: SheetMetadataConfidence.Low);
        Add("arch-600-detail", SheetMetadataEvidenceField.SheetLabel, SheetMetadataMatchKind.NumberRange, "d", true, prefix: "a", min: 600, max: 799, confidence: SheetMetadataConfidence.Low);
        Add("arch-900-detail", SheetMetadataEvidenceField.SheetLabel, SheetMetadataMatchKind.NumberRange, "d", true, prefix: "a", min: 900, max: 999, confidence: SheetMetadataConfidence.Low);
        Add("struct-100-foundation", SheetMetadataEvidenceField.SheetLabel, SheetMetadataMatchKind.NumberRange, "f", false, prefix: "s", min: 100, max: 199, confidence: SheetMetadataConfidence.Low);
        Add("struct-300-section", SheetMetadataEvidenceField.SheetLabel, SheetMetadataMatchKind.NumberRange, "sec", false, prefix: "s", min: 300, max: 499, confidence: SheetMetadataConfidence.Low);
        return rules;
    }

    public static List<SheetSuffixRule> BuildPreciseV2()
    {
        List<SheetSuffixRule> rules = BuildLegacy();
        foreach (SheetSuffixRule rule in rules)
        {
            if (string.Equals(rule.Id, "rcp-floor", StringComparison.OrdinalIgnoreCase))
                rule.SheetPrefix = "";
            if (rule.EvidenceField == SheetMetadataEvidenceField.TitleAndBody)
                rule.Confidence = SheetMetadataConfidence.Low;
            if (rule.OutputSuffix.Contains(' ') && rule.EvidenceField != SheetMetadataEvidenceField.TitleAndBody)
                rule.Confidence = SheetMetadataConfidence.High;
            if (rule.OutputSuffix is "fr n" or "u sc")
                rule.SkipScale = true;
        }

        var ordered = rules
            .OrderBy(PreciseEvidenceRank)
            .ThenBy(rule => rule.Priority)
            .ToList();
        for (int i = 0; i < ordered.Count; i++)
            ordered[i].Priority = (i + 1) * 10;
        return ordered;
    }

    private static int PreciseEvidenceRank(SheetSuffixRule rule)
    {
        if (rule.MinimumSheetNumber.HasValue &&
            rule.MaximumSheetNumber == rule.MinimumSheetNumber)
            return 0;
        if (rule.EvidenceField == SheetMetadataEvidenceField.SheetLabel &&
            rule.Confidence != SheetMetadataConfidence.Low)
            return 0;
        return rule.EvidenceField switch
        {
            SheetMetadataEvidenceField.SheetTitle => 1,
            SheetMetadataEvidenceField.DetectorFlags => 2,
            SheetMetadataEvidenceField.TitleAndBody => 3,
            _ => 4,
        };
    }
}
