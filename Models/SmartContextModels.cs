using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OurPlanCore;

public sealed class SmartProjectContext
{
    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = "";

    [JsonPropertyName("project_name")]
    public string ProjectName { get; set; } = "";

    [JsonPropertyName("root_path")]
    public string RootPath { get; set; } = "";

    [JsonPropertyName("created_at_utc")]
    public string CreatedAtUtc { get; set; } = "";

    [JsonPropertyName("updated_at_utc")]
    public string UpdatedAtUtc { get; set; } = "";

    [JsonPropertyName("hidden_marker_types")]
    public List<string> HiddenMarkerTypes { get; set; } = [];
}

public sealed class SmartObservation
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = "";

    [JsonPropertyName("page")]
    public string Page { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "manual";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("created_at_utc")]
    public string CreatedAtUtc { get; set; } = "";
}

public sealed class SmartAiRequest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("observation_id")]
    public string ObservationId { get; set; } = "";

    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = "";

    [JsonPropertyName("page")]
    public string Page { get; set; } = "";

    [JsonPropertyName("page_folder")]
    public string PageFolder { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = "";

    [JsonPropertyName("crop_path")]
    public string CropPath { get; set; } = "";

    [JsonPropertyName("context_crop_paths")]
    public List<string> ContextCropPaths { get; set; } = [];

    [JsonPropertyName("measurement_summary")]
    public string MeasurementSummary { get; set; } = "";

    [JsonPropertyName("layer_manifest_path")]
    public string LayerManifestPath { get; set; } = "";

    [JsonPropertyName("layer_count")]
    public int LayerCount { get; set; }

    [JsonPropertyName("layers")]
    public List<PdfLayerInfo> Layers { get; set; } = [];

    [JsonPropertyName("created_at_utc")]
    public string CreatedAtUtc { get; set; } = "";

    [JsonPropertyName("updated_at_utc")]
    public string UpdatedAtUtc { get; set; } = "";
}

public sealed class SmartAiResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = "";

    [JsonPropertyName("observation_id")]
    public string ObservationId { get; set; } = "";

    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "done";

    [JsonPropertyName("output_text")]
    public string OutputText { get; set; } = "";

    [JsonPropertyName("error")]
    public string Error { get; set; } = "";

    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("provider_response_id")]
    public string ProviderResponseId { get; set; } = "";

    [JsonPropertyName("raw_response_path")]
    public string RawResponsePath { get; set; } = "";

    [JsonPropertyName("created_at_utc")]
    public string CreatedAtUtc { get; set; } = "";

    [JsonPropertyName("updated_at_utc")]
    public string UpdatedAtUtc { get; set; } = "";
}

public sealed class SmartAiActionDraft
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = "";

    [JsonPropertyName("response_id")]
    public string ResponseId { get; set; } = "";

    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = "";

    [JsonPropertyName("page")]
    public string Page { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "no_actions";

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    [JsonPropertyName("raw_text")]
    public string RawText { get; set; } = "";

    [JsonPropertyName("actions")]
    public List<SmartAiAction> Actions { get; set; } = [];

    [JsonPropertyName("applied_measurement_ids")]
    public List<string> AppliedMeasurementIds { get; set; } = [];

    [JsonPropertyName("accepted_action_indices")]
    public List<int> AcceptedActionIndices { get; set; } = [];

    [JsonPropertyName("rejected_action_indices")]
    public List<int> RejectedActionIndices { get; set; } = [];

    [JsonPropertyName("applied_action_indices")]
    public List<int> AppliedActionIndices { get; set; } = [];

    [JsonPropertyName("reviewed_at_utc")]
    public string ReviewedAtUtc { get; set; } = "";

    [JsonPropertyName("created_at_utc")]
    public string CreatedAtUtc { get; set; } = "";

    [JsonPropertyName("updated_at_utc")]
    public string UpdatedAtUtc { get; set; } = "";
}

public sealed class SmartAiAction
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("page")]
    public string Page { get; set; } = "";

    [JsonPropertyName("measurement_type")]
    public string MeasurementType { get; set; } = "";

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";

    [JsonPropertyName("points")]
    public List<SmartAiActionPoint> Points { get; set; } = [];
}

public sealed class SmartAiActionPoint
{
    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }
}

public sealed class SmartAiMarker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("observation_id")]
    public string ObservationId { get; set; } = "";

    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = "";

    [JsonPropertyName("page")]
    public string Page { get; set; } = "";

    [JsonPropertyName("page_folder")]
    public string PageFolder { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("sample_kind")]
    public string SampleKind { get; set; } = "positive";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("note")]
    public string Note { get; set; } = "";

    [JsonPropertyName("crop_path")]
    public string CropPath { get; set; } = "";

    [JsonPropertyName("pdf_point")]
    public SmartAiMarkerPoint PdfPoint { get; set; } = new();

    [JsonPropertyName("pdf_rect")]
    public SmartAiMarkerRect PdfRect { get; set; } = new();

    [JsonPropertyName("layer_count")]
    public int LayerCount { get; set; }

    [JsonPropertyName("layers")]
    public List<PdfLayerInfo> Layers { get; set; } = [];

    [JsonPropertyName("created_at_utc")]
    public string CreatedAtUtc { get; set; } = "";

    [JsonPropertyName("updated_at_utc")]
    public string UpdatedAtUtc { get; set; } = "";
}

public sealed class SmartAiMarkerPoint
{
    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }
}

public sealed class SmartAiMarkerRect
{
    [JsonPropertyName("left")]
    public float Left { get; set; }

    [JsonPropertyName("top")]
    public float Top { get; set; }

    [JsonPropertyName("right")]
    public float Right { get; set; }

    [JsonPropertyName("bottom")]
    public float Bottom { get; set; }
}

public sealed class SmartAiMarkerSet
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("type_filter")]
    public string TypeFilter { get; set; } = "";

    [JsonPropertyName("sample_kind_filter")]
    public string SampleKindFilter { get; set; } = "";

    [JsonPropertyName("marker_count")]
    public int MarkerCount { get; set; }

    [JsonPropertyName("marker_ids")]
    public List<string> MarkerIds { get; set; } = [];

    [JsonPropertyName("created_at_utc")]
    public string CreatedAtUtc { get; set; } = "";

    [JsonPropertyName("updated_at_utc")]
    public string UpdatedAtUtc { get; set; } = "";
}

public sealed class SmartAiMarkersContextExport
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = "";

    [JsonPropertyName("project_name")]
    public string ProjectName { get; set; } = "";

    [JsonPropertyName("generated_at_utc")]
    public string GeneratedAtUtc { get; set; } = "";

    [JsonPropertyName("type_filter")]
    public string TypeFilter { get; set; } = "";

    [JsonPropertyName("sample_kind_filter")]
    public string SampleKindFilter { get; set; } = "";

    [JsonPropertyName("hidden_marker_types")]
    public List<string> HiddenMarkerTypes { get; set; } = [];

    [JsonPropertyName("marker_count")]
    public int MarkerCount { get; set; }

    [JsonPropertyName("markers")]
    public List<SmartAiMarker> Markers { get; set; } = [];

    [JsonPropertyName("marker_sets")]
    public List<SmartAiMarkerSet> MarkerSets { get; set; } = [];

    [JsonPropertyName("marker_feedback")]
    public List<SmartMarkerFeedbackRecord> MarkerFeedback { get; set; } = [];

    [JsonPropertyName("marker_quality")]
    public List<SmartMarkerQualitySummary> MarkerQuality { get; set; } = [];
}

public sealed class SmartMarkerQualitySummary
{
    [JsonPropertyName("marker_type")]
    public string MarkerType { get; set; } = "";

    [JsonPropertyName("sample_kind")]
    public string SampleKind { get; set; } = "";

    [JsonPropertyName("marker_count")]
    public int MarkerCount { get; set; }

    [JsonPropertyName("feedback_count")]
    public int FeedbackCount { get; set; }

    [JsonPropertyName("accepted_count")]
    public int AcceptedCount { get; set; }

    [JsonPropertyName("rejected_count")]
    public int RejectedCount { get; set; }

    [JsonPropertyName("applied_count")]
    public int AppliedCount { get; set; }

    [JsonPropertyName("average_confidence")]
    public double AverageConfidence { get; set; }

    [JsonPropertyName("last_feedback_at_utc")]
    public string LastFeedbackAtUtc { get; set; } = "";
}

public sealed class SmartAiCropBookmark
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = "";

    [JsonPropertyName("source_observation_id")]
    public string SourceObservationId { get; set; } = "";

    [JsonPropertyName("source_marker_id")]
    public string SourceMarkerId { get; set; } = "";

    [JsonPropertyName("source_action_draft_id")]
    public string SourceActionDraftId { get; set; } = "";

    [JsonPropertyName("source_action_index")]
    public int SourceActionIndex { get; set; } = -1;

    [JsonPropertyName("page")]
    public string Page { get; set; } = "";

    [JsonPropertyName("page_folder")]
    public string PageFolder { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("crop_path")]
    public string CropPath { get; set; } = "";

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "new";

    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = "";

    [JsonPropertyName("response_id")]
    public string ResponseId { get; set; } = "";

    [JsonPropertyName("action_draft_id")]
    public string ActionDraftId { get; set; } = "";

    [JsonPropertyName("auto_created")]
    public bool AutoCreated { get; set; }

    [JsonPropertyName("candidate_depth")]
    public int CandidateDepth { get; set; }

    [JsonPropertyName("candidate_key")]
    public string CandidateKey { get; set; } = "";

    [JsonPropertyName("candidate_center")]
    public SmartAiActionPoint CandidateCenter { get; set; } = new();

    [JsonPropertyName("candidate_points")]
    public List<SmartAiActionPoint> CandidatePoints { get; set; } = [];

    [JsonPropertyName("result_summary")]
    public string ResultSummary { get; set; } = "";

    [JsonPropertyName("created_at_utc")]
    public string CreatedAtUtc { get; set; } = "";

    [JsonPropertyName("updated_at_utc")]
    public string UpdatedAtUtc { get; set; } = "";

    [JsonPropertyName("processed_at_utc")]
    public string ProcessedAtUtc { get; set; } = "";
}
