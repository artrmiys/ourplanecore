using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OurPlanCore;

public static partial class SmartLearningStore
{
    private static readonly JsonSerializerOptions FileJsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions LineJsonOptions = new();

    public static string ProjectLearningRoot(OurPlanCoreJob job) =>
        Path.Combine(job.AIContextRoot, "learning");

    public static string ProjectSheetFeedbackPath(OurPlanCoreJob job) =>
        Path.Combine(ProjectLearningRoot(job), "sheet_feedback.jsonl");

    public static string ProjectReviewsPath(OurPlanCoreJob job) =>
        Path.Combine(ProjectLearningRoot(job), "project_reviews.jsonl");

    public static string ProjectMarkerFeedbackPath(OurPlanCoreJob job) =>
        Path.Combine(ProjectLearningRoot(job), "marker_feedback.jsonl");

    public static string ProjectLearnedRulesPath(OurPlanCoreJob job) =>
        Path.Combine(ProjectLearningRoot(job), "learned_rules.json");

    public static string ProjectSummaryPath(OurPlanCoreJob job) =>
        Path.Combine(ProjectLearningRoot(job), "project_learning_summary.json");

    public static string GlobalLearningRoot =>
        Path.Combine(SmartContextStore.GlobalRoot, "learning");

    public static string GlobalSheetFeedbackPath =>
        Path.Combine(GlobalLearningRoot, "sheet_feedback.jsonl");

    public static string GlobalMarkerFeedbackPath =>
        Path.Combine(GlobalLearningRoot, "marker_feedback.jsonl");

    public static string GlobalLearnedRulesPath =>
        Path.Combine(GlobalLearningRoot, "learned_rules.json");

    public static void EnsureLearningStore(OurPlanCoreJob job)
    {
        JobWriteAccess.Demand(ProjectLearningRoot(job), "prepare project learning store");
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
        OurPlanCoreJob job,
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
        OurPlanCoreJob job,
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
        OurPlanCoreJob job,
        PageInfo page,
        string note = "")
    {
        PdfSheetMetadata metadata = OurPlanCoreJobStore.ReadSourcePdfMetadata(page.FolderPath)
            ?? new PdfSheetMetadata
            {
                Source = "manual",
                DetectorVersion = "manual",
                PdfPath = page.PdfPath,
                PageIndex = page.PdfPage,
                PageNumber = page.PdfPage + 1,
                SheetLabel = page.Name,
                SheetKey = PdfSheetMetadataPolicy.ExtractSheetKey(page.Name),
                RenameCandidate = page.Name,
                Confidence = "manual",
            };
        SmartSheetLearningDecision final = PdfSheetMetadataService.FinalDecision(
            page,
            metadata,
            page.Name,
            page.ScaleMetersPerPt);
        string fingerprint = PdfSheetMetadataService.BuildPdfFingerprint(
            string.IsNullOrWhiteSpace(metadata.PdfPath) ? page.PdfPath : metadata.PdfPath);
        string detectorVersion = string.IsNullOrWhiteSpace(metadata.DetectorVersion)
            ? "manual"
            : metadata.DetectorVersion;
        string detectorConfigFingerprint = string.IsNullOrWhiteSpace(metadata.DetectorConfigFingerprint)
            ? PdfSheetMetadataPolicy.ConfigFingerprint(SheetMetadataRulesService.Active)
            : metadata.DetectorConfigFingerprint;
        var record = new SmartSheetLearningRecord
        {
            EventType = "manual_page_state",
            Source = "manual",
            UserOutcome = "manual_final",
            Reviewed = true,
            NameOutcome = "manual_final",
            SuffixOutcome = "manual_final",
            ScaleOutcome = "manual_final",
            DetectorVersion = detectorVersion,
            DetectorConfigFingerprint = detectorConfigFingerprint,
            PdfFingerprint = fingerprint,
            ObservationKey = PdfSheetMetadataPolicy.BuildObservationKey(
                fingerprint,
                page.PdfPage,
                detectorVersion,
                detectorConfigFingerprint),
            Note = note.Trim(),
            SourcePdf = string.IsNullOrWhiteSpace(metadata.PdfPath) ? page.PdfPath : metadata.PdfPath,
            PdfPage = page.PdfPage,
            Final = final,
        };

        return AppendSheetFeedback(job, page, record);
    }

    public static IReadOnlyList<SmartSheetLearningRecord> LoadProjectSheetFeedback(OurPlanCoreJob job) =>
        LoadJsonLines<SmartSheetLearningRecord>(ProjectSheetFeedbackPath(job));

    public static IReadOnlyList<SmartMarkerFeedbackRecord> LoadProjectMarkerFeedback(OurPlanCoreJob job) =>
        LoadJsonLines<SmartMarkerFeedbackRecord>(ProjectMarkerFeedbackPath(job));

    public static IReadOnlyList<SmartMarkerFeedbackRecord> LoadGlobalMarkerFeedback() =>
        LoadJsonLines<SmartMarkerFeedbackRecord>(GlobalMarkerFeedbackPath);

    public static IReadOnlyList<SmartSheetLearningRecord> LoadGlobalSheetFeedback() =>
        LoadJsonLines<SmartSheetLearningRecord>(GlobalSheetFeedbackPath);

    public static SmartLearnedRuleSet LoadProjectLearnedRules(OurPlanCoreJob job)
    {
        if (JobWriteAccess.IsWriteAllowed(job.RootPath))
            EnsureLearningStore(job);
        return LoadJson<SmartLearnedRuleSet>(ProjectLearnedRulesPath(job)) ?? new SmartLearnedRuleSet();
    }

    public static SmartLearnedRuleSet LoadGlobalLearnedRules()
    {
        Directory.CreateDirectory(GlobalLearningRoot);
        EnsureFile(GlobalLearnedRulesPath, "{\n  \"schema_version\": 1,\n  \"rules\": []\n}\n");
        return LoadJson<SmartLearnedRuleSet>(GlobalLearnedRulesPath) ?? new SmartLearnedRuleSet();
    }

    public static void SaveProjectLearnedRules(OurPlanCoreJob job, SmartLearnedRuleSet rules)
    {
        if (rules == null)
            throw new ArgumentNullException(nameof(rules));

        EnsureLearningStore(job);
        rules.GeneratedAtUtc = DateTime.UtcNow.ToString("O");
        string path = ProjectLearnedRulesPath(job);
        try
        {
            IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(rules, FileJsonOptions));
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
            IoUtil.WriteAllTextAtomic(GlobalLearnedRulesPath, JsonSerializer.Serialize(rules, FileJsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(GlobalLearnedRulesPath)}': {ex.Message}", ex);
        }
    }

    public static SmartSheetLearningSummary SaveProjectSummary(OurPlanCoreJob job)
    {
        SmartProjectContext context = SmartContextStore.EnsureProjectContext(job.RootPath, job.Name);
        EnsureLearningStore(job);
        IReadOnlyList<SmartSheetLearningRecord> records = LatestSheetObservations(
            LoadProjectSheetFeedback(job));

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
            SuffixCounts = CountValues(records
                .Where(record => IsReviewedField(record, record.SuffixOutcome))
                .Select(record => record.Final.Suffix)),
            ScaleCounts = CountValues(records
                .Where(record => IsReviewedField(record, record.ScaleOutcome))
                .Select(record => record.Final.ScaleText)),
        };

        string path = ProjectSummaryPath(job);
        try
        {
            IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(summary, FileJsonOptions));
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
        IReadOnlyList<SmartSheetLearningRecord> records = LatestSheetObservations(
            LoadGlobalSheetFeedback());
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
            if (!IsUsefulLearningOutcome(record) ||
                !IsReviewedField(record, record.SuffixOutcome))
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
        ApplyLearnedRules(metadata, LoadJson<SmartLearnedRuleSet>(GlobalLearnedRulesPath), "global");
    }

    public static void ApplyProjectLearnedRules(OurPlanCoreJob job, PdfSheetMetadata metadata)
    {
        ApplyLearnedRules(metadata, LoadProjectLearnedRules(job), "project");
    }

    private static void ApplyLearnedRules(PdfSheetMetadata metadata, SmartLearnedRuleSet? rules, string scope)
    {
        if (rules == null || rules.Rules.Count == 0 || string.IsNullOrWhiteSpace(metadata.SheetTitle))
            return;
        if (string.Equals(scope, "global", StringComparison.OrdinalIgnoreCase) &&
            (metadata.SuffixSource.StartsWith("project-learned", StringComparison.OrdinalIgnoreCase) ||
             metadata.ScaleSource.StartsWith("project-learned", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        HashSet<string> titleTokens = TitleTokens(metadata.SheetTitle);
        SmartLearnedRule? rule = rules.Rules
            .Where(candidate => candidate.Enabled && titleTokens.Contains(candidate.TitleToken))
            .OrderByDescending(candidate => candidate.Support)
            .FirstOrDefault();
        if (rule == null)
            return;

        SheetMetadataConfig config = SheetMetadataRulesService.Active;
        bool projectScope = string.Equals(scope, "project", StringComparison.OrdinalIgnoreCase);
        bool precise = config.DetectorMode != SheetMetadataDetectorMode.Legacy;
        bool protectedSuffix = IsProtectedSheetMetadataEvidence(metadata.SuffixSource);
        bool protectedScale = IsProtectedSheetMetadataEvidence(metadata.ScaleSource);
        bool strongerSuffix = PdfSheetMetadataPolicy.ConfidenceLevel(rule.Confidence) >
                              PdfSheetMetadataPolicy.ConfidenceLevel(metadata.SuffixConfidence);
        bool canReplaceSuffix = projectScope && precise && strongerSuffix &&
                                !protectedSuffix;
        bool strongerScale = PdfSheetMetadataPolicy.ConfidenceLevel(rule.Confidence) >
                             PdfSheetMetadataPolicy.ConfidenceLevel(metadata.ScaleConfidence);
        bool canReplaceScale = projectScope && precise && strongerScale &&
                               !protectedScale;
        bool canFillSuffix = string.IsNullOrWhiteSpace(metadata.Suffix) &&
                             (!precise || !protectedSuffix);
        bool canFillScale = string.IsNullOrWhiteSpace(metadata.SelectedScaleText) &&
                            (!precise || !protectedScale);
        bool applied = false;
        if ((canFillSuffix || canReplaceSuffix) &&
            !string.IsNullOrWhiteSpace(rule.Suffix))
        {
            metadata.Suffix = rule.Suffix;
            metadata.SuffixSource = $"{scope}-learned";
            metadata.SuffixConfidence = rule.Confidence;
            metadata.SuffixEvidence =
                $"title token '{rule.TitleToken}', support {rule.Support}, conflicts {rule.ConflictCount}";
            if (protectedScale && metadata.SelectedScaleMetersPerPt > 0)
                metadata.SuffixScalePolicy = "allow";
            applied = true;
        }

        if (rule.SkipScale && (canFillScale || canReplaceScale))
        {
            metadata.SkipScale = true;
            metadata.SkipReason = $"{scope}-learned no-scale rule";
            metadata.SelectedScaleText = "";
            metadata.ScaleText = "";
            metadata.SelectedScaleRatio = 0;
            metadata.SelectedScaleMetersPerPt = 0;
            metadata.ScaleSource = $"{scope}-learned";
            metadata.ScaleConfidence = rule.Confidence;
            metadata.ScaleEvidence =
                $"title token '{rule.TitleToken}', support {rule.Support}, conflicts {rule.ConflictCount}";
            applied = true;
        }
        else if (!rule.SkipScale &&
                 !string.IsNullOrWhiteSpace(rule.ScaleText) &&
                 (canFillScale || canReplaceScale) &&
                 PdfSheetMetadataService.TryParseScaleMetersPerPt(
                     rule.ScaleText,
                     out double learnedScaleMetersPerPt))
        {
            metadata.SelectedScaleText = rule.ScaleText;
            metadata.ScaleText = rule.ScaleText;
            metadata.SelectedScaleMetersPerPt = learnedScaleMetersPerPt;
            metadata.SelectedScaleRatio = learnedScaleMetersPerPt / ViewportConstants.PdfPointMeters;
            metadata.SkipScale = false;
            metadata.SkipReason = "";
            metadata.SuffixScalePolicy = "allow";
            metadata.ScaleSource = $"{scope}-learned";
            metadata.ScaleConfidence = rule.Confidence;
            metadata.ScaleEvidence =
                $"title token '{rule.TitleToken}', support {rule.Support}, conflicts {rule.ConflictCount}";
            applied = true;
        }

        if (applied)
        {
            metadata.Warnings.Add(
                $"{scope} learned rule applied: token '{rule.TitleToken}', suffix '{rule.Suffix}', support {rule.Support}");
        }
    }

    private static bool IsProtectedSheetMetadataEvidence(string source)
    {
        string clean = (source ?? "")
            .Trim()
            .ToLowerInvariant()
            .Replace('_', '-');
        return clean.Contains("manual-review", StringComparison.Ordinal) ||
               clean.Contains("sheet-override", StringComparison.Ordinal) ||
               clean.Contains("configured-rule", StringComparison.Ordinal) ||
               clean.Contains("sheet-index", StringComparison.Ordinal) ||
               clean.Contains("drawing-index", StringComparison.Ordinal) ||
               clean.Contains("title-block", StringComparison.Ordinal);
    }

    private static void FillPageContext(OurPlanCoreJob job, PageInfo page, SmartSheetLearningRecord record)
    {
        record.Page = string.IsNullOrWhiteSpace(record.Page) ? page.Name : record.Page.Trim();
        record.PageFolder = string.IsNullOrWhiteSpace(record.PageFolder)
            ? Path.GetRelativePath(job.RootPath, page.FolderPath)
            : record.PageFolder.Trim();

        SourceInfo? source = OurPlanCoreJobStore.ReadSource(page.FolderPath);
        record.SourcePdf = string.IsNullOrWhiteSpace(record.SourcePdf)
            ? source?.Pdf ?? page.PdfPath
            : record.SourcePdf.Trim();
        record.PdfPage = record.PdfPage != 0
            ? record.PdfPage
            : source?.Page ?? page.PdfPage;

        if (record.Layers.Count == 0)
        {
            PageLayerManifest? manifest = OurPlanCoreJobStore.ReadPageLayerManifest(page.FolderPath);
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
}
