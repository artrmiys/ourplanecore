using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OurPlanCore;

public static partial class SmartLearningStore
{
    private static bool IsOutcome(SmartSheetLearningRecord record, params string[] outcomes) =>
        outcomes.Any(outcome => string.Equals(record.UserOutcome, outcome, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeOutcome(string outcome)
    {
        string clean = string.IsNullOrWhiteSpace(outcome)
            ? "unknown"
            : outcome.Trim().ToLowerInvariant();

        return clean switch
        {
            "accept" => "accepted",
            "accepted" => "accepted",
            "apply" => "accepted",
            "applied" => "accepted",
            "reject" => "rejected",
            "rejected" => "rejected",
            _ => clean,
        };
    }

    private static bool IsUsefulLearningOutcome(SmartSheetLearningRecord record) =>
        IsOutcome(record, "accepted", "corrected", "manual_final") &&
        (!string.IsNullOrWhiteSpace(record.Final.Suffix) ||
         !string.IsNullOrWhiteSpace(record.Final.SheetTitle));

    private static HashSet<string> TitleTokens(string title)
    {
        string[] stop =
        [
            "plan", "plans", "sheet", "sheets", "typical", "project", "drawing",
            "title", "floor", "framing",
        ];
        var stopSet = new HashSet<string>(stop, StringComparer.OrdinalIgnoreCase);
        return title
            .ToLowerInvariant()
            .Split([' ', '-', '_', '/', '\\', '&', ',', '.', ':', ';', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 4 && !stopSet.Contains(token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasTitleOverlap(HashSet<string> tokens, string title)
    {
        if (tokens.Count == 0 || string.IsNullOrWhiteSpace(title))
            return false;
        var other = TitleTokens(title);
        return other.Any(tokens.Contains);
    }

    private static void SaveLearnedRuleSets(OurPlanCoreJob job, IReadOnlyList<SmartSheetLearningRecord> projectRecords)
    {
        SmartLearnedRuleSet projectRules = BuildLearnedRuleSet(projectRecords);
        string projectRulesPath = ProjectLearnedRulesPath(job);
        PreserveRuleEnabledStates(projectRulesPath, projectRules);
        try
        {
            IoUtil.WriteAllTextAtomic(projectRulesPath, JsonSerializer.Serialize(projectRules, FileJsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(projectRulesPath)}': {ex.Message}", ex);
        }

        IReadOnlyList<SmartSheetLearningRecord> globalRecords = LoadGlobalSheetFeedback();
        SmartLearnedRuleSet globalRules = BuildLearnedRuleSet(globalRecords);
        PreserveRuleEnabledStates(GlobalLearnedRulesPath, globalRules);
        try
        {
            IoUtil.WriteAllTextAtomic(GlobalLearnedRulesPath, JsonSerializer.Serialize(globalRules, FileJsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(GlobalLearnedRulesPath)}': {ex.Message}", ex);
        }
    }

    private static SmartLearnedRuleSet BuildLearnedRuleSet(IReadOnlyList<SmartSheetLearningRecord> records)
    {
        var candidates = new Dictionary<string, LearnedRuleAccumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (SmartSheetLearningRecord record in records.Where(IsUsefulLearningOutcome))
        {
            if (string.IsNullOrWhiteSpace(record.Final.Suffix))
                continue;

            foreach (string token in TitleTokens(record.Final.SheetTitle))
            {
                string key = $"{token}|{record.Final.Suffix}";
                if (!candidates.TryGetValue(key, out LearnedRuleAccumulator? acc))
                {
                    acc = new LearnedRuleAccumulator(token, record.Final.Suffix);
                    candidates[key] = acc;
                }

                acc.Support++;
                if (record.Final.SkipScale)
                    acc.SkipScaleVotes++;
                if (!string.IsNullOrWhiteSpace(record.Final.ScaleText))
                    acc.ScaleCounts[record.Final.ScaleText] = acc.ScaleCounts.GetValueOrDefault(record.Final.ScaleText) + 1;
            }
        }

        List<SmartLearnedRule> rules = candidates.Values
            .Where(acc => acc.Support >= 3)
            .OrderByDescending(acc => acc.Support)
            .ThenBy(acc => acc.TitleToken, StringComparer.OrdinalIgnoreCase)
            .Select(acc => new SmartLearnedRule
            {
                Enabled = true,
                Id = $"rule_{SafeRulePart(acc.TitleToken)}_{SafeRulePart(acc.Suffix)}",
                TitleToken = acc.TitleToken,
                Suffix = acc.Suffix,
                SkipScale = acc.SkipScaleVotes > acc.Support / 2,
                ScaleText = acc.ScaleCounts
                    .OrderByDescending(kvp => kvp.Value)
                    .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault().Key ?? "",
                Support = acc.Support,
                Confidence = acc.Support >= 8 ? "high" : "medium",
            })
            .ToList();

        return new SmartLearnedRuleSet
        {
            GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
            SourceRecordCount = records.Count,
            Rules = rules,
        };
    }

    private static void PreserveRuleEnabledStates(string path, SmartLearnedRuleSet newRules)
    {
        SmartLearnedRuleSet? existing = LoadJson<SmartLearnedRuleSet>(path);
        if (existing == null || existing.Rules.Count == 0)
            return;

        var existingById = existing.Rules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Id))
            .GroupBy(rule => rule.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (SmartLearnedRule rule in newRules.Rules)
        {
            if (existingById.TryGetValue(rule.Id, out SmartLearnedRule? oldRule))
                rule.Enabled = oldRule.Enabled;
        }
    }

    private static string SafeRulePart(string value)
    {
        string safe = new(value
            .ToLowerInvariant()
            .Where(ch => char.IsLetterOrDigit(ch) || ch == '_')
            .ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "x" : safe;
    }

    private sealed class LearnedRuleAccumulator(string titleToken, string suffix)
    {
        public string TitleToken { get; } = titleToken;
        public string Suffix { get; } = suffix;
        public int Support { get; set; }
        public int SkipScaleVotes { get; set; }
        public Dictionary<string, int> ScaleCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
