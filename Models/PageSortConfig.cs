using System;
using System.Collections.Generic;
using System.Linq;

namespace OurPlaneCore;

// Editable rules that drive the two page-sort features:
//  • ArchStruct -> "Sort A/S"      (Arch / Struct / Others by first letter + filename)
//  • Suffix     -> "Sort D/Sec/WT" (top-order, detection-order, suffix -> folder)
//
// Defaults built here reproduce the previously hard-coded behaviour exactly.
public sealed class ArchStructRule
{
    // "FirstLetter"  -> Match is a single letter compared to the page name's first letter
    // "FileKeyword"  -> Match is a substring searched in the source PDF file name
    public string Kind { get; set; } = "FirstLetter";
    public string Match { get; set; } = "";
    public string Target { get; set; } = "Arch"; // Arch | Struct | Others

    public ArchStructRule Clone() => new() { Kind = Kind, Match = Match, Target = Target };
}

public sealed class SuffixRule
{
    public string Suffix { get; set; } = "";
    public string FirstLetter { get; set; } = ""; // "" = any first letter
    // Folder display name to move into, or "top" to keep at the scope root.
    public string Target { get; set; } = "units";

    public SuffixRule Clone() => new() { Suffix = Suffix, FirstLetter = FirstLetter, Target = Target };
}

public sealed class PageSortConfig
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; set; }

    // ── A/S ──────────────────────────────────────────────────────────────
    public bool ArchStructDashToOthers { get; set; } = true; // name ends with '-' => Others
    public List<ArchStructRule> ArchStructRules { get; set; } = [];

    // ── D/Sec/WT ─────────────────────────────────────────────────────────
    public List<string> SuffixTopOrder { get; set; } = [];      // float to scope root, in this order
    public List<string> SuffixDetectionOrder { get; set; } = []; // suffix recognition priority
    public List<SuffixRule> SuffixRules { get; set; } = [];

    public PageSortConfig Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        ArchStructDashToOthers = ArchStructDashToOthers,
        ArchStructRules = ArchStructRules?.Select(r => r.Clone()).ToList() ?? [],
        SuffixTopOrder = SuffixTopOrder?.ToList() ?? [],
        SuffixDetectionOrder = SuffixDetectionOrder?.ToList() ?? [],
        SuffixRules = SuffixRules?.Select(r => r.Clone()).ToList() ?? [],
    };

    public static PageSortConfig BuildDefault() => new()
    {
        SchemaVersion = CurrentSchemaVersion,
        ArchStructDashToOthers = true,
        ArchStructRules =
        [
            new() { Kind = "FirstLetter", Match = "a", Target = "Arch" },
            new() { Kind = "FirstLetter", Match = "s", Target = "Struct" },
            new() { Kind = "FirstLetter", Match = "m", Target = "Others" },
            new() { Kind = "FirstLetter", Match = "p", Target = "Others" },
            new() { Kind = "FirstLetter", Match = "e", Target = "Others" },
            new() { Kind = "FirstLetter", Match = "c", Target = "Others" },
            new() { Kind = "FileKeyword", Match = "struct", Target = "Struct" },
            new() { Kind = "FileKeyword", Match = "arch", Target = "Arch" },
            new() { Kind = "FileKeyword", Match = "mep", Target = "Others" },
            new() { Kind = "FileKeyword", Match = "mechanical", Target = "Others" },
            new() { Kind = "FileKeyword", Match = "plumbing", Target = "Others" },
            new() { Kind = "FileKeyword", Match = "electrical", Target = "Others" },
            new() { Kind = "FileKeyword", Match = "civil", Target = "Others" },
        ],
        SuffixTopOrder = ["v", "wt", "ft", "sv", "sw"],
        SuffixDetectionOrder = ["sec", "wt", "ft", "sv", "sw", "u", "d", "f", "v"],
        SuffixRules =
        [
            new() { Suffix = "d", FirstLetter = "s", Target = "details struct" },
            new() { Suffix = "d", FirstLetter = "a", Target = "details arch" },
            new() { Suffix = "f", FirstLetter = "a", Target = "finish" },
            new() { Suffix = "u", FirstLetter = "", Target = "units" },
            new() { Suffix = "sec", FirstLetter = "", Target = "sections" },
        ],
    };

    public static PageSortConfig UpgradeForCurrentSchema(PageSortConfig? config)
    {
        PageSortConfig upgraded = config?.Clone() ?? BuildDefault();
        upgraded.ArchStructRules ??= [];
        upgraded.SuffixTopOrder ??= [];
        upgraded.SuffixDetectionOrder ??= [];
        upgraded.SuffixRules ??= [];

        if (upgraded.SchemaVersion < 2)
        {
            AddSuffixDetection(upgraded, "f");
            AddSuffixRule(upgraded, "f", "a", "finish");
        }

        if (upgraded.SchemaVersion < 3)
        {
            foreach (string letter in new[] { "m", "p", "e", "c" })
                AddArchStructRule(upgraded, "FirstLetter", letter, "Others");
            foreach (string keyword in new[] { "mep", "mechanical", "plumbing", "electrical", "civil" })
                AddArchStructRule(upgraded, "FileKeyword", keyword, "Others");
        }

        upgraded.SchemaVersion = CurrentSchemaVersion;
        return upgraded;
    }

    private static void AddArchStructRule(PageSortConfig config, string kind, string match, string target)
    {
        bool exists = config.ArchStructRules.Any(rule =>
            string.Equals(rule.Kind, kind, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(rule.Match, match, StringComparison.OrdinalIgnoreCase));
        if (!exists)
            config.ArchStructRules.Add(new ArchStructRule { Kind = kind, Match = match, Target = target });
    }

    private static void AddSuffixDetection(PageSortConfig config, string suffix)
    {
        if (!config.SuffixDetectionOrder.Contains(suffix, StringComparer.OrdinalIgnoreCase))
            config.SuffixDetectionOrder.Add(suffix);
    }

    private static void AddSuffixRule(PageSortConfig config, string suffix, string firstLetter, string target)
    {
        bool exists = config.SuffixRules.Any(rule =>
            string.Equals(rule.Suffix, suffix, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(rule.FirstLetter, firstLetter, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(rule.Target, target, StringComparison.OrdinalIgnoreCase));
        if (!exists)
            config.SuffixRules.Add(new SuffixRule { Suffix = suffix, FirstLetter = firstLetter, Target = target });
    }

    // Distinct non-"top" folder names referenced by suffix rules (so they get
    // pre-created exactly like the old hard-coded four).
    public IReadOnlyList<string> SuffixTargetFolderNames() =>
        SuffixRules
            .Select(r => (r.Target ?? "").Trim())
            .Where(t => t.Length > 0 && !string.Equals(t, "top", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

// Holds the active page-sort rules so the static name-parsing helpers and the
// instance classifiers all read the same edited/installed config.
public static class PageSortRulesService
{
    public static PageSortConfig Active { get; private set; } = PageSortConfig.BuildDefault();

    public static void Install(PageSortConfig? cfg) =>
        Active = PageSortConfig.UpgradeForCurrentSchema(cfg);
}
