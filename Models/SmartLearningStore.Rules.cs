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

    private static bool IsReviewedField(SmartSheetLearningRecord record, string fieldOutcome) =>
        record.SchemaVersion < 2 ||
        string.Equals(fieldOutcome, "accepted", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fieldOutcome, "corrected", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fieldOutcome, "manual_final", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<SmartSheetLearningRecord> LatestSheetObservations(
        IReadOnlyList<SmartSheetLearningRecord> records) =>
        records
            .Select((record, index) => new
            {
                Record = record,
                Index = index,
                Key = ObservationKey(record, index),
            })
            .GroupBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(entry => entry.Record.CreatedAtUtc, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(entry => entry.Index)
                .First()
                .Record)
            .ToList();

    private static string ObservationKey(SmartSheetLearningRecord record, int fallbackIndex)
    {
        if (!string.IsNullOrWhiteSpace(record.ObservationKey))
            return record.ObservationKey.Trim();

        string fingerprint = !string.IsNullOrWhiteSpace(record.PdfFingerprint)
            ? record.PdfFingerprint.Trim()
            : !string.IsNullOrWhiteSpace(record.SourcePdf)
                ? record.SourcePdf.Trim().ToLowerInvariant()
                : $"legacy-record-{fallbackIndex}";
        return PdfSheetMetadataPolicy.BuildObservationKey(
            fingerprint,
            record.PdfPage,
            record.DetectorVersion,
            record.DetectorConfigFingerprint);
    }

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
        SmartLearnedRuleSet projectRules = BuildLearnedRuleSet(
            LatestSheetObservations(projectRecords));
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

        IReadOnlyList<SmartSheetLearningRecord> globalRecords = LatestSheetObservations(
            LoadGlobalSheetFeedback());
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
        var candidatesByToken = new Dictionary<string, Dictionary<string, LearnedRuleAccumulator>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (SmartSheetLearningRecord record in records.Where(record =>
                     IsUsefulLearningOutcome(record) &&
                     IsReviewedField(record, record.SuffixOutcome)))
        {
            if (string.IsNullOrWhiteSpace(record.Final.Suffix))
                continue;

            foreach (string token in TitleTokens(record.Final.SheetTitle))
            {
                if (!candidatesByToken.TryGetValue(token, out Dictionary<string, LearnedRuleAccumulator>? bySuffix))
                {
                    bySuffix = new Dictionary<string, LearnedRuleAccumulator>(StringComparer.OrdinalIgnoreCase);
                    candidatesByToken[token] = bySuffix;
                }

                if (!bySuffix.TryGetValue(record.Final.Suffix, out LearnedRuleAccumulator? acc))
                {
                    acc = new LearnedRuleAccumulator(token, record.Final.Suffix);
                    bySuffix[record.Final.Suffix] = acc;
                }

                acc.Support++;
                if (IsReviewedField(record, record.ScaleOutcome))
                {
                    acc.ScaleReviewCount++;
                    if (record.Final.SkipScale)
                        acc.SkipScaleVotes++;
                    if (!string.IsNullOrWhiteSpace(record.Final.ScaleText))
                        acc.ScaleCounts[record.Final.ScaleText] =
                            acc.ScaleCounts.GetValueOrDefault(record.Final.ScaleText) + 1;
                }
            }
        }

        var rules = new List<SmartLearnedRule>();
        foreach ((string token, Dictionary<string, LearnedRuleAccumulator> bySuffix) in candidatesByToken)
        {
            int total = bySuffix.Values.Sum(candidate => candidate.Support);
            LearnedRuleAccumulator winner = bySuffix.Values
                .OrderByDescending(candidate => candidate.Support)
                .ThenBy(candidate => candidate.Suffix, StringComparer.OrdinalIgnoreCase)
                .First();
            double dominance = total > 0 ? winner.Support / (double)total : 0;
            if (winner.Support < 3 || dominance < 0.80)
                continue;

            rules.Add(new SmartLearnedRule
            {
                Enabled = true,
                Id = $"rule_{SafeRulePart(token)}_{SafeRulePart(winner.Suffix)}",
                TitleToken = token,
                Suffix = winner.Suffix,
                SkipScale = winner.ScaleReviewCount > 0 &&
                            winner.SkipScaleVotes > winner.ScaleReviewCount / 2,
                ScaleText = winner.ScaleCounts
                    .OrderByDescending(kvp => kvp.Value)
                    .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault().Key ?? "",
                Support = winner.Support,
                ConflictCount = total - winner.Support,
                Dominance = dominance,
                Confidence = winner.Support >= 8 && dominance >= 0.90 ? "high" : "medium",
            });
        }

        rules = rules
            .OrderByDescending(rule => rule.Support)
            .ThenBy(rule => rule.TitleToken, StringComparer.OrdinalIgnoreCase)
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
        public int ScaleReviewCount { get; set; }
        public Dictionary<string, int> ScaleCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
