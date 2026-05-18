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
    // ── A/S ──────────────────────────────────────────────────────────────
    public bool ArchStructDashToOthers { get; set; } = true; // name ends with '-' => Others
    public List<ArchStructRule> ArchStructRules { get; set; } = [];

    // ── D/Sec/WT ─────────────────────────────────────────────────────────
    public List<string> SuffixTopOrder { get; set; } = [];      // float to scope root, in this order
    public List<string> SuffixDetectionOrder { get; set; } = []; // suffix recognition priority
    public List<SuffixRule> SuffixRules { get; set; } = [];

    public PageSortConfig Clone() => new()
    {
        ArchStructDashToOthers = ArchStructDashToOthers,
        ArchStructRules = ArchStructRules.Select(r => r.Clone()).ToList(),
        SuffixTopOrder = [.. SuffixTopOrder],
        SuffixDetectionOrder = [.. SuffixDetectionOrder],
        SuffixRules = SuffixRules.Select(r => r.Clone()).ToList(),
    };

    public static PageSortConfig BuildDefault() => new()
    {
        ArchStructDashToOthers = true,
        ArchStructRules =
        [
            new() { Kind = "FirstLetter", Match = "a", Target = "Arch" },
            new() { Kind = "FirstLetter", Match = "s", Target = "Struct" },
            new() { Kind = "FileKeyword", Match = "struct", Target = "Struct" },
            new() { Kind = "FileKeyword", Match = "arch", Target = "Arch" },
        ],
        SuffixTopOrder = ["v", "wt", "ft", "sv", "sw"],
        SuffixDetectionOrder = ["sec", "wt", "ft", "sv", "sw", "u", "d", "v"],
        SuffixRules =
        [
            new() { Suffix = "d", FirstLetter = "s", Target = "details struct" },
            new() { Suffix = "d", FirstLetter = "a", Target = "details arch" },
            new() { Suffix = "u", FirstLetter = "", Target = "units" },
            new() { Suffix = "sec", FirstLetter = "", Target = "sections" },
        ],
    };

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
        Active = cfg ?? PageSortConfig.BuildDefault();
}
