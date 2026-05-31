using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OurPlaneCore;

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
