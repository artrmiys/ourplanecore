using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartTakeoffs;

public sealed class SmartSheetLearningRecord
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = "";

    [JsonPropertyName("project_name")]
    public string ProjectName { get; set; } = "";

    [JsonPropertyName("job_root")]
    public string JobRoot { get; set; } = "";

    [JsonPropertyName("page")]
    public string Page { get; set; } = "";

    [JsonPropertyName("page_folder")]
    public string PageFolder { get; set; } = "";

    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = "sheet_feedback";

    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("user_outcome")]
    public string UserOutcome { get; set; } = "";

    [JsonPropertyName("source_pdf")]
    public string SourcePdf { get; set; } = "";

    [JsonPropertyName("pdf_page")]
    public int PdfPage { get; set; }

    [JsonPropertyName("detection")]
    public SmartSheetLearningDecision Detection { get; set; } = new();

    [JsonPropertyName("final")]
    public SmartSheetLearningDecision Final { get; set; } = new();

    [JsonPropertyName("layers")]
    public List<PdfLayerInfo> Layers { get; set; } = [];

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = [];

    [JsonPropertyName("note")]
    public string Note { get; set; } = "";

    [JsonPropertyName("created_at_utc")]
    public string CreatedAtUtc { get; set; } = "";
}

public sealed class SmartSheetLearningDecision
{
    [JsonPropertyName("page_name")]
    public string PageName { get; set; } = "";

    [JsonPropertyName("sheet_label")]
    public string SheetLabel { get; set; } = "";

    [JsonPropertyName("sheet_key")]
    public string SheetKey { get; set; } = "";

    [JsonPropertyName("sheet_title")]
    public string SheetTitle { get; set; } = "";

    [JsonPropertyName("suffix")]
    public string Suffix { get; set; } = "";

    [JsonPropertyName("skip_scale")]
    public bool SkipScale { get; set; }

    [JsonPropertyName("scale_text")]
    public string ScaleText { get; set; } = "";

    [JsonPropertyName("scale_m_per_pt")]
    public double ScaleMetersPerPt { get; set; }

    [JsonPropertyName("confidence")]
    public string Confidence { get; set; } = "";
}

public sealed class SmartMarkerFeedbackRecord
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = "";

    [JsonPropertyName("project_name")]
    public string ProjectName { get; set; } = "";

    [JsonPropertyName("job_root")]
    public string JobRoot { get; set; } = "";

    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = "marker_candidate_feedback";

    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = "";

    [JsonPropertyName("response_id")]
    public string ResponseId { get; set; } = "";

    [JsonPropertyName("draft_id")]
    public string DraftId { get; set; } = "";

    [JsonPropertyName("source_marker_id")]
    public string SourceMarkerId { get; set; } = "";

    [JsonPropertyName("source_marker_type")]
    public string SourceMarkerType { get; set; } = "";

    [JsonPropertyName("source_marker_sample_kind")]
    public string SourceMarkerSampleKind { get; set; } = "";

    [JsonPropertyName("outcome")]
    public string Outcome { get; set; } = "";

    [JsonPropertyName("applied")]
    public bool Applied { get; set; }

    [JsonPropertyName("action_index")]
    public int ActionIndex { get; set; }

    [JsonPropertyName("action_type")]
    public string ActionType { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("page")]
    public string Page { get; set; } = "";

    [JsonPropertyName("measurement_type")]
    public string MeasurementType { get; set; } = "";

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("points")]
    public List<SmartAiActionPoint> Points { get; set; } = [];

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";

    [JsonPropertyName("target_id")]
    public string TargetId { get; set; } = "";

    [JsonPropertyName("target_name")]
    public string TargetName { get; set; } = "";

    [JsonPropertyName("target_measurement_type")]
    public string TargetMeasurementType { get; set; } = "";

    [JsonPropertyName("target_creates_new_item")]
    public bool TargetCreatesNewItem { get; set; }

    [JsonPropertyName("created_at_utc")]
    public string CreatedAtUtc { get; set; } = "";
}

public sealed class SmartSheetLearningSummary
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("generated_at_utc")]
    public string GeneratedAtUtc { get; set; } = "";

    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = "";

    [JsonPropertyName("project_name")]
    public string ProjectName { get; set; } = "";

    [JsonPropertyName("job_root")]
    public string JobRoot { get; set; } = "";

    [JsonPropertyName("record_count")]
    public int RecordCount { get; set; }

    [JsonPropertyName("accepted_count")]
    public int AcceptedCount { get; set; }

    [JsonPropertyName("corrected_count")]
    public int CorrectedCount { get; set; }

    [JsonPropertyName("rejected_count")]
    public int RejectedCount { get; set; }

    [JsonPropertyName("manual_final_count")]
    public int ManualFinalCount { get; set; }

    [JsonPropertyName("suffix_counts")]
    public Dictionary<string, int> SuffixCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("scale_counts")]
    public Dictionary<string, int> ScaleCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SmartLearnedRuleSet
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("generated_at_utc")]
    public string GeneratedAtUtc { get; set; } = "";

    [JsonPropertyName("source_record_count")]
    public int SourceRecordCount { get; set; }

    [JsonPropertyName("rules")]
    public List<SmartLearnedRule> Rules { get; set; } = [];
}

public sealed class SmartLearnedRule
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "title_token_suffix";

    [JsonPropertyName("title_token")]
    public string TitleToken { get; set; } = "";

    [JsonPropertyName("suffix")]
    public string Suffix { get; set; } = "";

    [JsonPropertyName("skip_scale")]
    public bool SkipScale { get; set; }

    [JsonPropertyName("scale_text")]
    public string ScaleText { get; set; } = "";

    [JsonPropertyName("support")]
    public int Support { get; set; }

    [JsonPropertyName("confidence")]
    public string Confidence { get; set; } = "";
}

public sealed class SmartSheetLearningSignal
{
    public string Confidence { get; init; } = "";
    public string Warning { get; init; } = "";
    public int SupportingRecords { get; init; }
    public int ConflictingRecords { get; init; }
}

public static class SmartLearningStore
{
    private static readonly JsonSerializerOptions FileJsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions LineJsonOptions = new();

    public static string ProjectLearningRoot(SmartTakeoffsJob job) =>
        Path.Combine(job.AIContextRoot, "learning");

    public static string ProjectSheetFeedbackPath(SmartTakeoffsJob job) =>
        Path.Combine(ProjectLearningRoot(job), "sheet_feedback.jsonl");

    public static string ProjectReviewsPath(SmartTakeoffsJob job) =>
        Path.Combine(ProjectLearningRoot(job), "project_reviews.jsonl");

    public static string ProjectMarkerFeedbackPath(SmartTakeoffsJob job) =>
        Path.Combine(ProjectLearningRoot(job), "marker_feedback.jsonl");

    public static string ProjectLearnedRulesPath(SmartTakeoffsJob job) =>
        Path.Combine(ProjectLearningRoot(job), "learned_rules.json");

    public static string ProjectSummaryPath(SmartTakeoffsJob job) =>
        Path.Combine(ProjectLearningRoot(job), "project_learning_summary.json");

    public static string GlobalLearningRoot =>
        Path.Combine(SmartContextStore.GlobalRoot, "learning");

    public static string GlobalSheetFeedbackPath =>
        Path.Combine(GlobalLearningRoot, "sheet_feedback.jsonl");

    public static string GlobalMarkerFeedbackPath =>
        Path.Combine(GlobalLearningRoot, "marker_feedback.jsonl");

    public static string GlobalLearnedRulesPath =>
        Path.Combine(GlobalLearningRoot, "learned_rules.json");

    public static void EnsureLearningStore(SmartTakeoffsJob job)
    {
        Directory.CreateDirectory(ProjectLearningRoot(job));
        Directory.CreateDirectory(GlobalLearningRoot);
        EnsureFile(ProjectSheetFeedbackPath(job), "");
        EnsureFile(ProjectReviewsPath(job), "");
        EnsureFile(ProjectMarkerFeedbackPath(job), "");
        EnsureFile(ProjectLearnedRulesPath(job), "{\n  \"schema_version\": 1,\n  \"rules\": []\n}\n");
        EnsureFile(GlobalSheetFeedbackPath, "");
        EnsureFile(GlobalMarkerFeedbackPath, "");
        EnsureFile(GlobalLearnedRulesPath, "{\n  \"schema_version\": 1,\n  \"rules\": []\n}\n");
    }

    public static SmartSheetLearningRecord AppendSheetFeedback(
        SmartTakeoffsJob job,
        PageInfo? page,
        SmartSheetLearningRecord record)
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));

        SmartProjectContext context = SmartContextStore.EnsureProjectContext(job.RootPath, job.Name);
        EnsureLearningStore(job);

        string now = DateTime.UtcNow.ToString("O");
        record.Id = string.IsNullOrWhiteSpace(record.Id)
            ? $"learn_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}"
            : record.Id.Trim();
        record.ProjectId = context.ProjectId;
        record.ProjectName = context.ProjectName;
        record.JobRoot = Path.GetFullPath(job.RootPath);
        record.CreatedAtUtc = string.IsNullOrWhiteSpace(record.CreatedAtUtc)
            ? now
            : record.CreatedAtUtc.Trim();

        if (page != null)
            FillPageContext(job, page, record);

        string line = JsonSerializer.Serialize(record, LineJsonOptions);
        File.AppendAllText(ProjectSheetFeedbackPath(job), line + Environment.NewLine);
        File.AppendAllText(GlobalSheetFeedbackPath, line + Environment.NewLine);

        return record;
    }

    public static SmartMarkerFeedbackRecord AppendMarkerFeedback(
        SmartTakeoffsJob job,
        SmartMarkerFeedbackRecord record)
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));

        SmartProjectContext context = SmartContextStore.EnsureProjectContext(job.RootPath, job.Name);
        EnsureLearningStore(job);

        string now = DateTime.UtcNow.ToString("O");
        record.Id = string.IsNullOrWhiteSpace(record.Id)
            ? $"markerfb_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}"
            : record.Id.Trim();
        record.ProjectId = context.ProjectId;
        record.ProjectName = context.ProjectName;
        record.JobRoot = Path.GetFullPath(job.RootPath);
        record.EventType = string.IsNullOrWhiteSpace(record.EventType)
            ? "marker_candidate_feedback"
            : record.EventType.Trim();
        record.Outcome = NormalizeOutcome(record.Outcome);
        record.CreatedAtUtc = string.IsNullOrWhiteSpace(record.CreatedAtUtc)
            ? now
            : record.CreatedAtUtc.Trim();

        string line = JsonSerializer.Serialize(record, LineJsonOptions);
        File.AppendAllText(ProjectMarkerFeedbackPath(job), line + Environment.NewLine);
        File.AppendAllText(GlobalMarkerFeedbackPath, line + Environment.NewLine);
        return record;
    }

    public static SmartSheetLearningRecord CaptureManualPageState(
        SmartTakeoffsJob job,
        PageInfo page,
        string note = "")
    {
        var record = new SmartSheetLearningRecord
        {
            EventType = "manual_page_state",
            Source = "manual",
            UserOutcome = "manual_final",
            Note = note.Trim(),
            Final = new SmartSheetLearningDecision
            {
                PageName = page.Name,
                ScaleMetersPerPt = page.ScaleMetersPerPt,
                Confidence = "manual",
            },
        };

        return AppendSheetFeedback(job, page, record);
    }

    public static IReadOnlyList<SmartSheetLearningRecord> LoadProjectSheetFeedback(SmartTakeoffsJob job) =>
        LoadJsonLines<SmartSheetLearningRecord>(ProjectSheetFeedbackPath(job));

    public static IReadOnlyList<SmartMarkerFeedbackRecord> LoadProjectMarkerFeedback(SmartTakeoffsJob job) =>
        LoadJsonLines<SmartMarkerFeedbackRecord>(ProjectMarkerFeedbackPath(job));

    public static IReadOnlyList<SmartMarkerFeedbackRecord> LoadGlobalMarkerFeedback() =>
        LoadJsonLines<SmartMarkerFeedbackRecord>(GlobalMarkerFeedbackPath);

    public static IReadOnlyList<SmartSheetLearningRecord> LoadGlobalSheetFeedback() =>
        LoadJsonLines<SmartSheetLearningRecord>(GlobalSheetFeedbackPath);

    public static SmartLearnedRuleSet LoadProjectLearnedRules(SmartTakeoffsJob job)
    {
        EnsureLearningStore(job);
        return LoadJson<SmartLearnedRuleSet>(ProjectLearnedRulesPath(job)) ?? new SmartLearnedRuleSet();
    }

    public static SmartLearnedRuleSet LoadGlobalLearnedRules()
    {
        Directory.CreateDirectory(GlobalLearningRoot);
        EnsureFile(GlobalLearnedRulesPath, "{\n  \"schema_version\": 1,\n  \"rules\": []\n}\n");
        return LoadJson<SmartLearnedRuleSet>(GlobalLearnedRulesPath) ?? new SmartLearnedRuleSet();
    }

    public static void SaveProjectLearnedRules(SmartTakeoffsJob job, SmartLearnedRuleSet rules)
    {
        if (rules == null)
            throw new ArgumentNullException(nameof(rules));

        EnsureLearningStore(job);
        rules.GeneratedAtUtc = DateTime.UtcNow.ToString("O");
        string path = ProjectLearnedRulesPath(job);
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(rules, FileJsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(path)}': {ex.Message}", ex);
        }
    }

    public static void SaveGlobalLearnedRules(SmartLearnedRuleSet rules)
    {
        if (rules == null)
            throw new ArgumentNullException(nameof(rules));

        Directory.CreateDirectory(GlobalLearningRoot);
        rules.GeneratedAtUtc = DateTime.UtcNow.ToString("O");
        try
        {
            File.WriteAllText(GlobalLearnedRulesPath, JsonSerializer.Serialize(rules, FileJsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(GlobalLearnedRulesPath)}': {ex.Message}", ex);
        }
    }

    public static SmartSheetLearningSummary SaveProjectSummary(SmartTakeoffsJob job)
    {
        SmartProjectContext context = SmartContextStore.EnsureProjectContext(job.RootPath, job.Name);
        EnsureLearningStore(job);
        IReadOnlyList<SmartSheetLearningRecord> records = LoadProjectSheetFeedback(job);

        var summary = new SmartSheetLearningSummary
        {
            GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
            ProjectId = context.ProjectId,
            ProjectName = context.ProjectName,
            JobRoot = Path.GetFullPath(job.RootPath),
            RecordCount = records.Count,
            AcceptedCount = records.Count(record => IsOutcome(record, "accepted")),
            CorrectedCount = records.Count(record => IsOutcome(record, "corrected", "overrode")),
            RejectedCount = records.Count(record => IsOutcome(record, "rejected")),
            ManualFinalCount = records.Count(record => IsOutcome(record, "manual_final")),
            SuffixCounts = CountValues(records.Select(record => record.Final.Suffix)),
            ScaleCounts = CountValues(records.Select(record => record.Final.ScaleText)),
        };

        string path = ProjectSummaryPath(job);
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(summary, FileJsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(path)}': {ex.Message}", ex);
        }
        SaveLearnedRuleSets(job, records);
        return summary;
    }

    public static SmartSheetLearningSignal BuildSheetMetadataSignal(PdfSheetMetadata metadata)
    {
        IReadOnlyList<SmartSheetLearningRecord> records = LoadGlobalSheetFeedback();
        if (records.Count == 0)
            return new SmartSheetLearningSignal { Confidence = metadata.Confidence };

        string suffix = metadata.Suffix.Trim();
        string title = metadata.SheetTitle.Trim();
        var titleTokens = TitleTokens(title);
        if (titleTokens.Count == 0 && string.IsNullOrWhiteSpace(suffix))
            return new SmartSheetLearningSignal { Confidence = metadata.Confidence };

        int supporting = 0;
        int conflicting = 0;
        foreach (SmartSheetLearningRecord record in records)
        {
            if (!IsUsefulLearningOutcome(record))
                continue;

            bool sameSuffix = !string.IsNullOrWhiteSpace(suffix) &&
                              string.Equals(record.Final.Suffix, suffix, StringComparison.OrdinalIgnoreCase);
            bool detectionWasThisSuffix = !string.IsNullOrWhiteSpace(suffix) &&
                                          string.Equals(record.Detection.Suffix, suffix, StringComparison.OrdinalIgnoreCase);
            bool titleOverlap = HasTitleOverlap(titleTokens, record.Final.SheetTitle) ||
                                HasTitleOverlap(titleTokens, record.Detection.SheetTitle);

            if (sameSuffix && titleOverlap)
                supporting++;
            else if (detectionWasThisSuffix &&
                     !string.IsNullOrWhiteSpace(record.Final.Suffix) &&
                     !string.Equals(record.Final.Suffix, suffix, StringComparison.OrdinalIgnoreCase) &&
                     titleOverlap)
            {
                conflicting++;
            }
        }

        if (conflicting > supporting && conflicting > 0)
        {
            return new SmartSheetLearningSignal
            {
                Confidence = "learned-conflict",
                Warning = $"learning conflict: {conflicting} prior correction(s) disagree with suffix '{suffix}'",
                SupportingRecords = supporting,
                ConflictingRecords = conflicting,
            };
        }

        if (supporting >= 3)
        {
            return new SmartSheetLearningSignal
            {
                Confidence = "learned-high",
                SupportingRecords = supporting,
                ConflictingRecords = conflicting,
            };
        }

        if (supporting > 0)
        {
            return new SmartSheetLearningSignal
            {
                Confidence = "learned-medium",
                SupportingRecords = supporting,
                ConflictingRecords = conflicting,
            };
        }

        return new SmartSheetLearningSignal
        {
            Confidence = string.IsNullOrWhiteSpace(metadata.Confidence) ? "pdf-text" : metadata.Confidence,
            SupportingRecords = supporting,
            ConflictingRecords = conflicting,
        };
    }

    public static void ApplyLearnedRules(PdfSheetMetadata metadata)
    {
        SmartLearnedRuleSet? rules = LoadJson<SmartLearnedRuleSet>(GlobalLearnedRulesPath);
        if (rules == null || rules.Rules.Count == 0 || string.IsNullOrWhiteSpace(metadata.SheetTitle))
            return;

        HashSet<string> titleTokens = TitleTokens(metadata.SheetTitle);
        SmartLearnedRule? rule = rules.Rules
            .Where(candidate => candidate.Enabled && titleTokens.Contains(candidate.TitleToken))
            .OrderByDescending(candidate => candidate.Support)
            .FirstOrDefault();
        if (rule == null)
            return;

        bool applied = false;
        if (string.IsNullOrWhiteSpace(metadata.Suffix) && !string.IsNullOrWhiteSpace(rule.Suffix))
        {
            metadata.Suffix = rule.Suffix;
            metadata.SkipScale = rule.SkipScale;
            applied = true;
        }

        if (string.IsNullOrWhiteSpace(metadata.SelectedScaleText) &&
            !rule.SkipScale &&
            !string.IsNullOrWhiteSpace(rule.ScaleText))
        {
            metadata.SelectedScaleText = rule.ScaleText;
            metadata.ScaleText = rule.ScaleText;
            applied = true;
        }

        if (applied)
        {
            metadata.Warnings.Add(
                $"learned rule applied: token '{rule.TitleToken}', suffix '{rule.Suffix}', support {rule.Support}");
        }
    }

    private static void FillPageContext(SmartTakeoffsJob job, PageInfo page, SmartSheetLearningRecord record)
    {
        record.Page = string.IsNullOrWhiteSpace(record.Page) ? page.Name : record.Page.Trim();
        record.PageFolder = string.IsNullOrWhiteSpace(record.PageFolder)
            ? Path.GetRelativePath(job.RootPath, page.FolderPath)
            : record.PageFolder.Trim();

        SourceInfo? source = SmartTakeoffsJobStore.ReadSource(page.FolderPath);
        record.SourcePdf = string.IsNullOrWhiteSpace(record.SourcePdf)
            ? source?.Pdf ?? page.PdfPath
            : record.SourcePdf.Trim();
        record.PdfPage = record.PdfPage != 0
            ? record.PdfPage
            : source?.Page ?? page.PdfPage;

        if (record.Layers.Count == 0)
        {
            PageLayerManifest? manifest = SmartTakeoffsJobStore.ReadPageLayerManifest(page.FolderPath);
            IEnumerable<PdfLayerInfo> layerSource = manifest?.Layers ?? page.PdfLayers;
            record.Layers = layerSource
                .OrderBy(layer => layer.Number)
                .Select(layer => new PdfLayerInfo
                {
                    Number = layer.Number,
                    Name = layer.Name,
                    IsOn = layer.IsOn,
                })
                .ToList();
        }
    }

    private static IReadOnlyList<T> LoadJsonLines<T>(string path)
    {
        if (!File.Exists(path))
            return [];

        var records = new List<T>();
        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                T? record = JsonSerializer.Deserialize<T>(line);
                if (record != null)
                    records.Add(record);
            }
            catch
            {
                // Keep learning history readable even if a hand-edited row is bad.
            }
        }

        return records;
    }

    private static T? LoadJson<T>(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<T>(File.ReadAllText(path))
                : default;
        }
        catch
        {
            return default;
        }
    }

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

    private static void SaveLearnedRuleSets(SmartTakeoffsJob job, IReadOnlyList<SmartSheetLearningRecord> projectRecords)
    {
        SmartLearnedRuleSet projectRules = BuildLearnedRuleSet(projectRecords);
        string projectRulesPath = ProjectLearnedRulesPath(job);
        PreserveRuleEnabledStates(projectRulesPath, projectRules);
        try
        {
            File.WriteAllText(projectRulesPath, JsonSerializer.Serialize(projectRules, FileJsonOptions));
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
            File.WriteAllText(GlobalLearnedRulesPath, JsonSerializer.Serialize(globalRules, FileJsonOptions));
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

    private static Dictionary<string, int> CountValues(IEnumerable<string> values) =>
        values
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

    private static void EnsureFile(string path, string initialContent)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        if (!File.Exists(path))
        {
            try
            {
                File.WriteAllText(path, initialContent);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException($"Failed to save '{Path.GetFileName(path)}': {ex.Message}", ex);
            }
        }
    }
}
