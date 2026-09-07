using System;
using System.Collections.Generic;
using System.Linq;

namespace OurPlanCore;

// Keeps optional AI fallback aligned with the same editable policy used by the
// deterministic PDF detector. The fallback remains review-only evidence.
public static class SheetMetadataPromptPolicy
{
    public static IEnumerable<string> BuildPromptLines(SheetMetadataConfig? source = null)
    {
        SheetMetadataConfig config = SheetMetadataConfig.UpgradeForCurrentSchema(
            source ?? SheetMetadataRulesService.Active);

        yield return $"Active sheet-metadata preset: {config.PresetName}.";
        yield return "Apply the first enabled suffix rule by ascending Priority. Strong explicit evidence wins over body text and numeric fallback.";
        yield return $"No-scale suffixes: {Join(config.NoScaleSuffixes)}.";
        yield return $"Scale-capable suffixes: {Join(config.ScaleCapableSuffixes)}.";
        yield return $"Known compound suffixes: {Join(config.CompoundSuffixes)}.";
        yield return "NTS means skip scale. AS NOTED must stay empty unless one unambiguous scale is visible in the supplied sheet evidence. Never invent a scale.";
        yield return "For exact overrides, Full page name is final. Suffix Set/Clear is valid only when Full page name is blank.";

        foreach (SheetSuffixRule rule in config.SuffixRules
                     .Where(rule => rule.Enabled)
                     .OrderBy(rule => rule.Priority)
                     .Take(80))
        {
            string matcher = RuleMatcher(rule);
            string suffix = string.IsNullOrWhiteSpace(rule.OutputSuffix)
                ? "<intentional blank>"
                : rule.OutputSuffix.Trim();
            yield return $"- [{rule.Priority}] {rule.EvidenceField}/{rule.MatchKind}: {matcher} -> {suffix}; skip_scale={rule.SkipScale}; confidence={rule.Confidence}.";
        }

        foreach (SheetMetadataLabelOverride item in config.SheetLabelOverrides.Where(item => item.Enabled))
        {
            string name = string.IsNullOrWhiteSpace(item.OutputPageName) ? "<unchanged>" : item.OutputPageName.Trim();
            string suffix = string.IsNullOrWhiteSpace(item.OutputSuffix) ? "<blank>" : item.OutputSuffix.Trim();
            string scale = string.IsNullOrWhiteSpace(item.ScaleText) ? "<none>" : item.ScaleText.Trim();
            yield return $"- Exact override: pdf='{item.SourcePdfPattern}', label='{item.SheetLabel}' -> name='{name}', suffix_action={item.SuffixAction}, suffix='{suffix}', scale_action={item.ScaleAction}, scale='{scale}'.";
        }
    }

    private static string RuleMatcher(SheetSuffixRule rule)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(rule.SheetPrefix))
            parts.Add($"prefix={rule.SheetPrefix.Trim()}");
        if (rule.MinimumSheetNumber.HasValue || rule.MaximumSheetNumber.HasValue)
            parts.Add($"number={rule.MinimumSheetNumber?.ToString() ?? "*"}..{rule.MaximumSheetNumber?.ToString() ?? "*"}");
        if (!string.IsNullOrWhiteSpace(rule.Pattern))
            parts.Add($"pattern={rule.Pattern.Trim()}");
        if (rule.Keywords.Count > 0)
            parts.Add($"keywords={Join(rule.Keywords)}");
        if (rule.ExcludedKeywords.Count > 0)
            parts.Add($"exclude={Join(rule.ExcludedKeywords)}");
        if (rule.RequiredFlags.Count > 0)
            parts.Add($"required_flags={Join(rule.RequiredFlags)}");
        return parts.Count == 0 ? "configured match" : string.Join("; ", parts);
    }

    private static string Join(IEnumerable<string>? values) =>
        string.Join(", ", (values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()));
}
