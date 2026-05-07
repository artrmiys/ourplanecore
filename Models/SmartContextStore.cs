using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OurPlaneCore;

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

public static class SmartContextStore
{
    public const string GlobalRootEnvironmentVariable = "OURPLANECORE_GLOBAL_ROOT";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string GlobalRoot
    {
        get
        {
            string configuredRoot = Environment.GetEnvironmentVariable(GlobalRootEnvironmentVariable) ?? "";
            if (!string.IsNullOrWhiteSpace(configuredRoot))
                return Path.GetFullPath(configuredRoot);

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "OurPlaneCore");
        }
    }

    public static string GlobalRegistryPath => Path.Combine(GlobalRoot, "global_ai_index.jsonl");

    public static SmartProjectContext EnsureProjectContext(string jobRoot, string projectName)
    {
        string contextRoot = ContextRoot(jobRoot);
        Directory.CreateDirectory(contextRoot);
        Directory.CreateDirectory(Path.Combine(contextRoot, "pages"));
        Directory.CreateDirectory(Path.Combine(contextRoot, "crops"));
        Directory.CreateDirectory(Path.Combine(contextRoot, "requests"));
        Directory.CreateDirectory(Path.Combine(contextRoot, "responses"));
        Directory.CreateDirectory(Path.Combine(contextRoot, "actions"));
        Directory.CreateDirectory(Path.Combine(contextRoot, "markers"));
        Directory.CreateDirectory(Path.Combine(contextRoot, "marker_sets"));
        Directory.CreateDirectory(Path.Combine(contextRoot, "crop_bookmarks"));
        Directory.CreateDirectory(Path.Combine(contextRoot, "exports"));
        Directory.CreateDirectory(Path.Combine(contextRoot, "learning"));

        string projectJsonPath = ProjectContextPath(jobRoot);
        SmartProjectContext context = LoadProjectContext(projectJsonPath) ?? new SmartProjectContext
        {
            ProjectId = Guid.NewGuid().ToString(),
            CreatedAtUtc = DateTime.UtcNow.ToString("O"),
        };

        context.ProjectName = projectName;
        context.RootPath = Path.GetFullPath(jobRoot);
        context.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
        context.HiddenMarkerTypes = NormalizeMarkerTypeList(context.HiddenMarkerTypes);

        SaveProjectContext(jobRoot, context);
        EnsureFile(Path.Combine(contextRoot, "project.md"), $"# {projectName}{Environment.NewLine}{Environment.NewLine}");
        EnsureFile(Path.Combine(contextRoot, "observations.jsonl"), "");
        EnsureFile(Path.Combine(contextRoot, "takeoff_rules_used.json"), "{\n  \"rules\": []\n}\n");
        EnsureFile(Path.Combine(contextRoot, "learning", "sheet_feedback.jsonl"), "");
        EnsureFile(Path.Combine(contextRoot, "learning", "marker_feedback.jsonl"), "");
        EnsureFile(Path.Combine(contextRoot, "learning", "project_reviews.jsonl"), "");
        EnsureFile(Path.Combine(contextRoot, "learning", "learned_rules.json"), "{\n  \"schema_version\": 1,\n  \"rules\": []\n}\n");
        RegisterProject(context);

        return context;
    }

    public static IReadOnlyList<string> LoadHiddenMarkerTypes(OurPlaneCoreJob job) =>
        EnsureProjectContext(job.RootPath, job.Name).HiddenMarkerTypes.ToList();

    public static void SaveHiddenMarkerTypes(OurPlaneCoreJob job, IEnumerable<string> hiddenMarkerTypes)
    {
        SmartProjectContext context = EnsureProjectContext(job.RootPath, job.Name);
        context.HiddenMarkerTypes = NormalizeMarkerTypeList(hiddenMarkerTypes);
        context.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
        SaveProjectContext(job.RootPath, context);
    }

    public static SmartObservation AddManualObservation(OurPlaneCoreJob job, PageInfo? page, string text) =>
        AddObservation(job, page, "manual", text);

    public static SmartObservation AddObservation(OurPlaneCoreJob job, PageInfo? page, string type, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Observation text is required.", nameof(text));

        SmartProjectContext context = EnsureProjectContext(job.RootPath, job.Name);
        var observation = new SmartObservation
        {
            Id = $"obs_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}",
            ProjectId = context.ProjectId,
            Page = page?.Name ?? "",
            Type = string.IsNullOrWhiteSpace(type) ? "manual" : type.Trim(),
            Text = text.Trim(),
            CreatedAtUtc = DateTime.UtcNow.ToString("O"),
        };

        string contextRoot = ContextRoot(job.RootPath);
        File.AppendAllText(
            Path.Combine(contextRoot, "observations.jsonl"),
            JsonSerializer.Serialize(observation) + Environment.NewLine);

        File.AppendAllText(
            Path.Combine(contextRoot, "project.md"),
            BuildMarkdownObservation(observation));

        return observation;
    }

    public static SmartAiRequest AddAiRequest(
        OurPlaneCoreJob job,
        PageInfo? page,
        SmartObservation observation,
        string type,
        string prompt,
        string cropPath,
        string measurementSummary,
        IEnumerable<string>? contextCropPaths = null)
    {
        SmartProjectContext context = EnsureProjectContext(job.RootPath, job.Name);
        string now = DateTime.UtcNow.ToString("O");
        string pageFolder = "";
        string layerManifestPath = "";
        List<PdfLayerInfo> layers = [];

        if (page != null)
        {
            pageFolder = Path.GetRelativePath(job.RootPath, page.FolderPath);
            string manifestPath = OurPlaneCoreJobStore.PageLayersJsonPath(page.FolderPath);
            PageLayerManifest? manifest = OurPlaneCoreJobStore.ReadPageLayerManifest(page.FolderPath);
            if (manifest != null && File.Exists(manifestPath))
                layerManifestPath = Path.GetRelativePath(job.RootPath, manifestPath);

            IEnumerable<PdfLayerInfo> layerSource = manifest?.Layers ?? page.PdfLayers;
            layers = layerSource
                .OrderBy(layer => layer.Number)
                .Select(layer => new PdfLayerInfo
                {
                    Number = layer.Number,
                    Name = layer.Name,
                    IsOn = layer.IsOn,
                })
                .ToList();
        }

        var request = new SmartAiRequest
        {
            Id = observation.Id,
            ObservationId = observation.Id,
            ProjectId = context.ProjectId,
            Page = page?.Name ?? "",
            PageFolder = pageFolder,
            Type = string.IsNullOrWhiteSpace(type) ? observation.Type : type.Trim(),
            Status = "pending",
            Prompt = prompt.Trim(),
            CropPath = cropPath.Trim(),
            ContextCropPaths = NormalizeRelativePathList(contextCropPaths),
            MeasurementSummary = measurementSummary.Trim(),
            LayerManifestPath = layerManifestPath,
            LayerCount = layers.Count,
            Layers = layers,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        string requestPath = Path.Combine(ContextRoot(job.RootPath), "requests", $"{request.Id}.json");
        try
        {
            IoUtil.WriteAllTextAtomic(requestPath, JsonSerializer.Serialize(request, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(requestPath)}': {ex.Message}", ex);
        }

        return request;
    }

    public static SmartAiMarker SaveAiMarker(
        OurPlaneCoreJob job,
        PageInfo page,
        SmartObservation observation,
        string markerType,
        string sampleKind,
        string value,
        string note,
        string cropPath,
        float pdfX,
        float pdfY,
        float cropLeft,
        float cropTop,
        float cropRight,
        float cropBottom)
    {
        SmartProjectContext context = EnsureProjectContext(job.RootPath, job.Name);
        string now = DateTime.UtcNow.ToString("O");
        string pageFolder = Path.GetRelativePath(job.RootPath, page.FolderPath);
        List<PdfLayerInfo> layers = PageLayers(page);

        var marker = new SmartAiMarker
        {
            Id = observation.Id,
            ObservationId = observation.Id,
            ProjectId = context.ProjectId,
            Page = page.Name,
            PageFolder = pageFolder,
            Type = markerType.Trim(),
            SampleKind = string.IsNullOrWhiteSpace(sampleKind) ? "positive" : sampleKind.Trim(),
            Value = value.Trim(),
            Note = note.Trim(),
            CropPath = cropPath.Trim(),
            PdfPoint = new SmartAiMarkerPoint { X = pdfX, Y = pdfY },
            PdfRect = new SmartAiMarkerRect
            {
                Left = cropLeft,
                Top = cropTop,
                Right = cropRight,
                Bottom = cropBottom,
            },
            LayerCount = layers.Count,
            Layers = layers,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        try
        {
            IoUtil.WriteAllTextAtomic(AiMarkerPath(job, marker.Id), JsonSerializer.Serialize(marker, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(AiMarkerPath(job, marker.Id))}': {ex.Message}", ex);
        }
        return marker;
    }

    private static List<PdfLayerInfo> PageLayers(PageInfo page)
    {
        PageLayerManifest? manifest = OurPlaneCoreJobStore.ReadPageLayerManifest(page.FolderPath);
        IEnumerable<PdfLayerInfo> layerSource = manifest?.Layers ?? page.PdfLayers;
        return layerSource
            .OrderBy(layer => layer.Number)
            .Select(layer => new PdfLayerInfo
            {
                Number = layer.Number,
                Name = layer.Name,
                IsOn = layer.IsOn,
            })
            .ToList();
    }

    public static SmartAiMarker? LoadAiMarker(OurPlaneCoreJob job, string markerId)
    {
        if (string.IsNullOrWhiteSpace(markerId))
            return null;

        return LoadJson<SmartAiMarker>(AiMarkerPath(job, markerId));
    }

    public static void SaveAiMarker(OurPlaneCoreJob job, SmartAiMarker marker)
    {
        if (string.IsNullOrWhiteSpace(marker.Id))
            throw new InvalidOperationException("AI marker id is required.");

        marker.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
        string path = AiMarkerPath(job, marker.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ContextRoot(job.RootPath));
        try
        {
            IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(marker, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(path)}': {ex.Message}", ex);
        }
    }

    public static bool DeleteAiMarker(OurPlaneCoreJob job, string markerId)
    {
        if (string.IsNullOrWhiteSpace(markerId))
            return false;

        string path = AiMarkerPath(job, markerId);
        if (!File.Exists(path))
            return false;

        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to delete '{Path.GetFileName(path)}': {ex.Message}", ex);
        }
    }

    public static IReadOnlyList<SmartAiMarker> LoadAiMarkers(OurPlaneCoreJob job)
    {
        string markersDir = Path.Combine(ContextRoot(job.RootPath), "markers");
        if (!Directory.Exists(markersDir))
            return [];

        return Directory.EnumerateFiles(markersDir, "*.json")
            .Select(LoadJson<SmartAiMarker>)
            .Where(marker => marker != null)
            .Select(marker => marker!)
            .OrderBy(marker => marker.CreatedAtUtc)
            .ToList();
    }

    public static string AiMarkerPath(OurPlaneCoreJob job, string markerId) =>
        Path.Combine(ContextRoot(job.RootPath), "markers", $"{markerId}.json");

    public static SmartAiMarkerSet SaveAiMarkerSet(
        OurPlaneCoreJob job,
        string name,
        string description,
        string typeFilter,
        string sampleKindFilter,
        IReadOnlyList<SmartAiMarker> markers)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Marker set name is required.", nameof(name));

        SmartProjectContext context = EnsureProjectContext(job.RootPath, job.Name);
        string now = DateTime.UtcNow.ToString("O");
        var markerIds = markers
            .Select(marker => marker.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var set = new SmartAiMarkerSet
        {
            Id = $"set_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}",
            ProjectId = context.ProjectId,
            Name = name.Trim(),
            Description = description.Trim(),
            TypeFilter = typeFilter.Trim(),
            SampleKindFilter = sampleKindFilter.Trim(),
            MarkerIds = markerIds,
            MarkerCount = markerIds.Count,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        string path = AiMarkerSetPath(job, set.Id);
        try
        {
            IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(set, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(path)}': {ex.Message}", ex);
        }

        return set;
    }

    public static void SaveAiMarkerSet(OurPlaneCoreJob job, SmartAiMarkerSet set)
    {
        if (string.IsNullOrWhiteSpace(set.Id))
            throw new ArgumentException("Marker set id is required.", nameof(set));
        if (string.IsNullOrWhiteSpace(set.Name))
            throw new ArgumentException("Marker set name is required.", nameof(set));

        SmartProjectContext context = EnsureProjectContext(job.RootPath, job.Name);
        set.SchemaVersion = 1;
        set.ProjectId = context.ProjectId;
        set.Name = set.Name.Trim();
        set.Description = set.Description.Trim();
        set.TypeFilter = set.TypeFilter.Trim();
        set.SampleKindFilter = set.SampleKindFilter.Trim();
        set.MarkerIds = set.MarkerIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        set.MarkerCount = set.MarkerIds.Count;
        if (string.IsNullOrWhiteSpace(set.CreatedAtUtc))
            set.CreatedAtUtc = DateTime.UtcNow.ToString("O");
        set.UpdatedAtUtc = DateTime.UtcNow.ToString("O");

        string path = AiMarkerSetPath(job, set.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ContextRoot(job.RootPath));
        try
        {
            IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(set, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(path)}': {ex.Message}", ex);
        }
    }

    public static IReadOnlyList<SmartAiMarkerSet> LoadAiMarkerSets(OurPlaneCoreJob job)
    {
        string setsDir = Path.Combine(ContextRoot(job.RootPath), "marker_sets");
        if (!Directory.Exists(setsDir))
            return [];

        return Directory.EnumerateFiles(setsDir, "*.json")
            .Select(LoadJson<SmartAiMarkerSet>)
            .Where(set => set != null)
            .Select(set => set!)
            .OrderBy(set => set.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(set => set.CreatedAtUtc)
            .ToList();
    }

    public static string AiMarkerSetPath(OurPlaneCoreJob job, string markerSetId) =>
        Path.Combine(ContextRoot(job.RootPath), "marker_sets", $"{markerSetId}.json");

    public static bool DeleteAiMarkerSet(OurPlaneCoreJob job, string markerSetId)
    {
        if (string.IsNullOrWhiteSpace(markerSetId))
            return false;

        string path = AiMarkerSetPath(job, markerSetId);
        if (!File.Exists(path))
            return false;

        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to delete '{Path.GetFileName(path)}': {ex.Message}", ex);
        }
    }

    public static string ExportAiMarkersContext(
        OurPlaneCoreJob job,
        IReadOnlyList<SmartAiMarker> markers,
        IReadOnlyList<string> hiddenMarkerTypes,
        string typeFilter,
        string sampleKindFilter)
    {
        SmartProjectContext context = EnsureProjectContext(job.RootPath, job.Name);
        var orderedMarkers = markers
            .OrderBy(marker => marker.Type, StringComparer.OrdinalIgnoreCase)
            .ThenBy(marker => marker.SampleKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(marker => marker.Page, StringComparer.OrdinalIgnoreCase)
            .ThenBy(marker => marker.CreatedAtUtc)
            .ToList();
        var feedback = LoadRelevantMarkerFeedback(job, orderedMarkers);

        var export = new SmartAiMarkersContextExport
        {
            ProjectId = context.ProjectId,
            ProjectName = context.ProjectName,
            GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
            TypeFilter = typeFilter.Trim(),
            SampleKindFilter = sampleKindFilter.Trim(),
            HiddenMarkerTypes = hiddenMarkerTypes
                .Where(type => !string.IsNullOrWhiteSpace(type))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(type => type, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            MarkerCount = orderedMarkers.Count,
            Markers = orderedMarkers,
            MarkerSets = LoadAiMarkerSets(job).ToList(),
            MarkerFeedback = feedback,
            MarkerQuality = BuildMarkerQualitySummaries(orderedMarkers, feedback),
        };

        string path = AiMarkersContextExportPath(job);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ContextRoot(job.RootPath));
        try
        {
            IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(export, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(path)}': {ex.Message}", ex);
        }

        return path;
    }

    private static List<SmartMarkerFeedbackRecord> LoadRelevantMarkerFeedback(
        OurPlaneCoreJob job,
        IReadOnlyList<SmartAiMarker> markers)
    {
        if (markers.Count == 0)
            return [];

        var markerIds = markers
            .Select(marker => marker.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var markerTypes = markers
            .Select(marker => marker.Type)
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return SmartLearningStore.LoadProjectMarkerFeedback(job)
            .Where(record =>
                markerIds.Contains(record.SourceMarkerId) ||
                markerTypes.Contains(record.SourceMarkerType))
            .OrderByDescending(record => record.CreatedAtUtc)
            .Take(200)
            .ToList();
    }

    private static List<SmartMarkerQualitySummary> BuildMarkerQualitySummaries(
        IReadOnlyList<SmartAiMarker> markers,
        IReadOnlyList<SmartMarkerFeedbackRecord> feedback)
    {
        var markerCounts = markers
            .GroupBy(marker => MarkerQualityKey(marker.Type, marker.SampleKind), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return feedback
            .GroupBy(record => MarkerQualityKey(record.SourceMarkerType, record.SourceMarkerSampleKind), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                SmartMarkerFeedbackRecord first = group.First();
                string type = first.SourceMarkerType.Trim();
                string sampleKind = first.SourceMarkerSampleKind.Trim();
                var confidence = group
                    .Where(record => record.Confidence > 0)
                    .Select(record => record.Confidence)
                    .ToList();
                return new SmartMarkerQualitySummary
                {
                    MarkerType = type,
                    SampleKind = sampleKind,
                    MarkerCount = markerCounts.TryGetValue(group.Key, out int markerCount) ? markerCount : 0,
                    FeedbackCount = group.Count(),
                    AcceptedCount = group.Count(record => string.Equals(record.Outcome, "accepted", StringComparison.OrdinalIgnoreCase)),
                    RejectedCount = group.Count(record => string.Equals(record.Outcome, "rejected", StringComparison.OrdinalIgnoreCase)),
                    AppliedCount = group.Count(record => record.Applied),
                    AverageConfidence = confidence.Count == 0 ? 0 : confidence.Average(),
                    LastFeedbackAtUtc = group
                        .Select(record => record.CreatedAtUtc)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .OrderByDescending(value => value, StringComparer.Ordinal)
                        .FirstOrDefault() ?? "",
                };
            })
            .OrderBy(summary => summary.MarkerType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(summary => summary.SampleKind, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string MarkerQualityKey(string type, string sampleKind) =>
        $"{type.Trim()}|{sampleKind.Trim()}";

    public static string AiMarkersContextExportPath(OurPlaneCoreJob job) =>
        Path.Combine(ContextRoot(job.RootPath), "exports", "markers_context.json");

    public static SmartAiCropBookmark SaveCropBookmark(OurPlaneCoreJob job, SmartAiCropBookmark bookmark)
    {
        if (string.IsNullOrWhiteSpace(bookmark.CropPath))
            throw new InvalidOperationException("Crop bookmark path is required.");

        SmartProjectContext context = EnsureProjectContext(job.RootPath, job.Name);
        string now = DateTime.UtcNow.ToString("O");
        if (string.IsNullOrWhiteSpace(bookmark.Id))
        {
            bookmark.Id = $"bookmark_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}";
            bookmark.CreatedAtUtc = now;
        }

        bookmark.ProjectId = context.ProjectId;
        bookmark.Status = string.IsNullOrWhiteSpace(bookmark.Status) ? "new" : bookmark.Status.Trim();
        bookmark.Type = string.IsNullOrWhiteSpace(bookmark.Type) ? "crop_bookmark" : bookmark.Type.Trim();
        bookmark.UpdatedAtUtc = now;

        string path = CropBookmarkPath(job, bookmark.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ContextRoot(job.RootPath));
        try
        {
            IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(bookmark, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(path)}': {ex.Message}", ex);
        }

        return bookmark;
    }

    public static SmartAiCropBookmark? LoadCropBookmark(OurPlaneCoreJob job, string bookmarkId)
    {
        if (string.IsNullOrWhiteSpace(bookmarkId))
            return null;

        return LoadJson<SmartAiCropBookmark>(CropBookmarkPath(job, bookmarkId));
    }

    public static IReadOnlyList<SmartAiCropBookmark> LoadCropBookmarks(OurPlaneCoreJob job)
    {
        string bookmarksDir = Path.Combine(ContextRoot(job.RootPath), "crop_bookmarks");
        if (!Directory.Exists(bookmarksDir))
            return [];

        return Directory.EnumerateFiles(bookmarksDir, "*.json")
            .Select(LoadJson<SmartAiCropBookmark>)
            .Where(bookmark => bookmark != null)
            .Select(bookmark => bookmark!)
            .OrderBy(bookmark => bookmark.CreatedAtUtc)
            .ToList();
    }

    public static SmartAiCropBookmark? FindCropBookmarkByObservation(OurPlaneCoreJob job, string observationId)
    {
        if (string.IsNullOrWhiteSpace(observationId))
            return null;

        return LoadCropBookmarks(job).FirstOrDefault(bookmark =>
            string.Equals(bookmark.SourceObservationId, observationId, StringComparison.OrdinalIgnoreCase));
    }

    public static string CropBookmarkPath(OurPlaneCoreJob job, string bookmarkId) =>
        Path.Combine(ContextRoot(job.RootPath), "crop_bookmarks", $"{bookmarkId}.json");

    public static SmartAiRequest? LoadAiRequest(OurPlaneCoreJob job, string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            return null;

        string path = Path.Combine(ContextRoot(job.RootPath), "requests", $"{requestId}.json");
        return LoadJson<SmartAiRequest>(path);
    }

    public static SmartAiResponse? LoadAiResponse(OurPlaneCoreJob job, string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            return null;

        string path = Path.Combine(ContextRoot(job.RootPath), "responses", $"{requestId}.json");
        return LoadJson<SmartAiResponse>(path);
    }

    public static SmartAiActionDraft? LoadAiActionDraft(OurPlaneCoreJob job, string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            return null;

        return LoadJson<SmartAiActionDraft>(AiActionDraftPath(job, requestId));
    }

    public static string AiActionDraftPath(OurPlaneCoreJob job, string requestId) =>
        Path.Combine(ContextRoot(job.RootPath), "actions", $"{requestId}.json");

    public static void SaveAiActionDraft(OurPlaneCoreJob job, SmartAiActionDraft draft)
    {
        draft.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
        string path = AiActionDraftPath(job, draft.RequestId);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ContextRoot(job.RootPath));
        try
        {
            IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(draft, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(path)}': {ex.Message}", ex);
        }
    }

    public static IReadOnlyList<SmartAiRequest> LoadAiRequests(OurPlaneCoreJob job)
    {
        string requestsDir = Path.Combine(ContextRoot(job.RootPath), "requests");
        if (!Directory.Exists(requestsDir))
            return [];

        return Directory.EnumerateFiles(requestsDir, "*.json")
            .Select(LoadJson<SmartAiRequest>)
            .Where(request => request != null)
            .Select(request => request!)
            .OrderBy(request => request.CreatedAtUtc)
            .ToList();
    }

    public static void SaveAiRequest(OurPlaneCoreJob job, SmartAiRequest request)
    {
        request.ContextCropPaths = NormalizeRelativePathList(request.ContextCropPaths);
        request.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
        string path = Path.Combine(ContextRoot(job.RootPath), "requests", $"{request.Id}.json");
        try
        {
            IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(request, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(path)}': {ex.Message}", ex);
        }
    }

    public static SmartAiResponse SaveAiResponse(
        OurPlaneCoreJob job,
        SmartAiRequest request,
        string status,
        string outputText,
        string error,
        string provider = "",
        string model = "",
        string providerResponseId = "",
        string rawResponsePath = "")
    {
        SmartProjectContext context = EnsureProjectContext(job.RootPath, job.Name);
        string now = DateTime.UtcNow.ToString("O");
        var response = LoadAiResponse(job, request.Id) ?? new SmartAiResponse
        {
            Id = request.Id,
            RequestId = request.Id,
            ObservationId = request.ObservationId,
            ProjectId = context.ProjectId,
            CreatedAtUtc = now,
        };

        response.Status = string.IsNullOrWhiteSpace(status) ? "done" : status.Trim();
        response.OutputText = outputText.Trim();
        response.Error = error.Trim();
        response.Provider = provider.Trim();
        response.Model = model.Trim();
        response.ProviderResponseId = providerResponseId.Trim();
        response.RawResponsePath = rawResponsePath.Trim();
        response.UpdatedAtUtc = now;

        request.Status = response.Status;
        request.UpdatedAtUtc = now;

        string contextRoot = ContextRoot(job.RootPath);
        string requestPath = Path.Combine(contextRoot, "requests", $"{request.Id}.json");
        try
        {
            IoUtil.WriteAllTextAtomic(
                requestPath,
                JsonSerializer.Serialize(request, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(requestPath)}': {ex.Message}", ex);
        }

        string responsePath = Path.Combine(contextRoot, "responses", $"{response.Id}.json");
        try
        {
            IoUtil.WriteAllTextAtomic(
                responsePath,
                JsonSerializer.Serialize(response, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(responsePath)}': {ex.Message}", ex);
        }
        File.AppendAllText(
            Path.Combine(contextRoot, "project.md"),
            BuildMarkdownResponse(request, response));

        return response;
    }

    public static SmartAiActionDraft SaveAiActionDraftFromResponse(
        OurPlaneCoreJob job,
        SmartAiRequest request,
        SmartAiResponse response)
    {
        SmartProjectContext context = EnsureProjectContext(job.RootPath, job.Name);
        string now = DateTime.UtcNow.ToString("O");
        SmartAiActionDraft draft = BuildAiActionDraft(context, request, response, now);

        string path = AiActionDraftPath(job, request.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ContextRoot(job.RootPath));
        try
        {
            IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(draft, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(path)}': {ex.Message}", ex);
        }
        return draft;
    }

    private static SmartAiActionDraft BuildAiActionDraft(
        SmartProjectContext context,
        SmartAiRequest request,
        SmartAiResponse response,
        string now)
    {
        var draft = new SmartAiActionDraft
        {
            Id = request.Id,
            RequestId = request.Id,
            ResponseId = response.Id,
            ProjectId = context.ProjectId,
            Page = request.Page,
            RawText = response.OutputText,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        foreach (string candidate in CandidateJsonBlocks(response.OutputText))
        {
            if (TryParseActionDraft(candidate, request, draft))
                break;
        }

        if (string.IsNullOrWhiteSpace(draft.Summary))
            draft.Summary = FirstNonEmptyLine(response.OutputText);

        draft.Status = draft.Actions.Count > 0 ? "needs_review" : "no_actions";
        return draft;
    }

    private static IEnumerable<string> CandidateJsonBlocks(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        int searchAt = 0;
        while (true)
        {
            int fenceStart = text.IndexOf("```", searchAt, StringComparison.Ordinal);
            if (fenceStart < 0)
                break;

            int contentStart = text.IndexOf('\n', fenceStart);
            if (contentStart < 0)
                break;

            int fenceEnd = text.IndexOf("```", contentStart + 1, StringComparison.Ordinal);
            if (fenceEnd < 0)
                break;

            string block = text[(contentStart + 1)..fenceEnd].Trim();
            if (block.StartsWith('{') || block.StartsWith('['))
                yield return block;

            searchAt = fenceEnd + 3;
        }

        string trimmed = text.Trim();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            yield return trimmed;

        int objectStart = text.IndexOf('{');
        int objectEnd = text.LastIndexOf('}');
        if (objectStart >= 0 && objectEnd > objectStart)
            yield return text[objectStart..(objectEnd + 1)];

        int arrayStart = text.IndexOf('[');
        int arrayEnd = text.LastIndexOf(']');
        if (arrayStart >= 0 && arrayEnd > arrayStart)
            yield return text[arrayStart..(arrayEnd + 1)];
    }

    private static bool TryParseActionDraft(string json, SmartAiRequest request, SmartAiActionDraft draft)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                draft.Summary = JsonString(root, "summary");
                if (TryGetProperty(root, "actions", out JsonElement actionsElement) &&
                    actionsElement.ValueKind == JsonValueKind.Array)
                {
                    draft.Actions = ParseActions(actionsElement, request).ToList();
                    return draft.Actions.Count > 0 || !string.IsNullOrWhiteSpace(draft.Summary);
                }

                SmartAiAction? single = ParseAction(root, request);
                if (single != null)
                {
                    draft.Actions = [single];
                    return true;
                }
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                draft.Actions = ParseActions(root, request).ToList();
                return draft.Actions.Count > 0;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static IEnumerable<SmartAiAction> ParseActions(JsonElement actionsElement, SmartAiRequest request)
    {
        foreach (JsonElement actionElement in actionsElement.EnumerateArray())
        {
            SmartAiAction? action = ParseAction(actionElement, request);
            if (action != null)
                yield return action;
        }
    }

    private static SmartAiAction? ParseAction(JsonElement actionElement, SmartAiRequest request)
    {
        if (actionElement.ValueKind != JsonValueKind.Object)
            return null;

        var action = new SmartAiAction
        {
            Type = JsonString(actionElement, "type"),
            Label = JsonString(actionElement, "label"),
            Page = JsonString(actionElement, "page"),
            MeasurementType = JsonString(actionElement, "measurement_type"),
            Confidence = JsonDouble(actionElement, "confidence"),
            Notes = JsonString(actionElement, "notes"),
            Points = ParsePoints(actionElement).ToList(),
        };

        if (string.IsNullOrWhiteSpace(action.Page))
            action.Page = request.Page;
        if (string.IsNullOrWhiteSpace(action.MeasurementType))
            action.MeasurementType = request.Type == "trace_area_request" ? "area" : "line";
        if (string.IsNullOrWhiteSpace(action.Type))
            action.Type = action.MeasurementType == "area" ? "trace_area" : "trace_line";
        if (string.IsNullOrWhiteSpace(action.Label))
            action.Label = action.Type;

        bool hasUsefulContent =
            !string.IsNullOrWhiteSpace(action.Type) ||
            !string.IsNullOrWhiteSpace(action.Notes) ||
            action.Points.Count > 0;

        return hasUsefulContent ? action : null;
    }

    private static IEnumerable<SmartAiActionPoint> ParsePoints(JsonElement actionElement)
    {
        if (!TryGetProperty(actionElement, "points", out JsonElement pointsElement) ||
            pointsElement.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement pointElement in pointsElement.EnumerateArray())
        {
            if (pointElement.ValueKind == JsonValueKind.Object)
            {
                yield return new SmartAiActionPoint
                {
                    X = (float)JsonDouble(pointElement, "x"),
                    Y = (float)JsonDouble(pointElement, "y"),
                };
            }
            else if (pointElement.ValueKind == JsonValueKind.Array)
            {
                var values = pointElement.EnumerateArray().ToList();
                if (values.Count >= 2)
                {
                    yield return new SmartAiActionPoint
                    {
                        X = JsonElementDouble(values[0]),
                        Y = JsonElementDouble(values[1]),
                    };
                }
            }
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (JsonProperty prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string JsonString(JsonElement element, string name) =>
        TryGetProperty(element, name, out JsonElement value)
            ? value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : value.ToString()
            : "";

    private static double JsonDouble(JsonElement element, string name) =>
        TryGetProperty(element, name, out JsonElement value)
            ? JsonElementDouble(value)
            : 0;

    private static float JsonElementDouble(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
            return (float)number;
        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), out number))
        {
            return (float)number;
        }

        return 0;
    }

    private static string FirstNonEmptyLine(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0) ?? "";

    private static SmartProjectContext? LoadProjectContext(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<SmartProjectContext>(File.ReadAllText(path))
                : null;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Load project context failed for {path}");
            return null;
        }
    }

    private static T? LoadJson<T>(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<T>(File.ReadAllText(path))
                : default;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Load JSON failed for {path}");
            return default;
        }
    }

    private static void RegisterProject(SmartProjectContext context)
    {
        Directory.CreateDirectory(GlobalRoot);

        var record = new Dictionary<string, string>
        {
            ["project_id"] = context.ProjectId,
            ["project_name"] = context.ProjectName,
            ["root_path"] = context.RootPath,
            ["updated_at_utc"] = context.UpdatedAtUtc,
        };

        var records = new List<Dictionary<string, string>>();
        if (File.Exists(GlobalRegistryPath))
        {
            foreach (string line in File.ReadLines(GlobalRegistryPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var existing = JsonSerializer.Deserialize<Dictionary<string, string>>(line);
                    if (existing != null &&
                        existing.TryGetValue("project_id", out string? id) &&
                        !string.Equals(id, context.ProjectId, StringComparison.OrdinalIgnoreCase))
                    {
                        records.Add(existing);
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Warn(ex, $"Project registry row could not be read from {GlobalRegistryPath}");
                    // Keep the registry usable even if an older line was edited by hand.
                }
            }
        }

        records.Add(record);
        IoUtil.WriteAllTextAtomic(
            GlobalRegistryPath,
            string.Join(Environment.NewLine, records.ConvertAll(r => JsonSerializer.Serialize(r))) + Environment.NewLine);
    }

    private static string BuildMarkdownObservation(SmartObservation observation)
    {
        string page = string.IsNullOrWhiteSpace(observation.Page) ? "Unassigned page" : observation.Page;
        return
            $"{Environment.NewLine}## {observation.Id}{Environment.NewLine}" +
            $"- Time UTC: {observation.CreatedAtUtc}{Environment.NewLine}" +
            $"- Page: {page}{Environment.NewLine}" +
            $"- Type: {observation.Type}{Environment.NewLine}" +
            $"{Environment.NewLine}{observation.Text}{Environment.NewLine}";
    }

    private static string BuildMarkdownResponse(SmartAiRequest request, SmartAiResponse response)
    {
        string body = response.Status == "done"
            ? response.OutputText
            : response.Error;

        return
            $"{Environment.NewLine}### AI response for {request.Id}{Environment.NewLine}" +
            $"- Time UTC: {response.UpdatedAtUtc}{Environment.NewLine}" +
            $"- Status: {response.Status}{Environment.NewLine}" +
            $"- Type: {request.Type}{Environment.NewLine}" +
            $"{Environment.NewLine}{body}{Environment.NewLine}";
    }

    private static void EnsureFile(string path, string initialContent)
    {
        if (!File.Exists(path))
        {
            try
            {
                IoUtil.WriteAllTextAtomic(path, initialContent);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException($"Failed to save '{Path.GetFileName(path)}': {ex.Message}", ex);
            }
        }
    }

    private static List<string> NormalizeMarkerTypeList(IEnumerable<string>? markerTypes)
    {
        if (markerTypes == null)
            return [];

        return markerTypes
            .Where(markerType => !string.IsNullOrWhiteSpace(markerType))
            .Select(markerType => markerType.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(markerType => markerType, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> NormalizeRelativePathList(IEnumerable<string>? paths)
    {
        if (paths == null)
            return [];

        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void SaveProjectContext(string jobRoot, SmartProjectContext context)
    {
        string projectJsonPath = ProjectContextPath(jobRoot);
        try
        {
            IoUtil.WriteAllTextAtomic(projectJsonPath, JsonSerializer.Serialize(context, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save '{Path.GetFileName(projectJsonPath)}': {ex.Message}", ex);
        }
    }

    private static string ProjectContextPath(string jobRoot) => Path.Combine(ContextRoot(jobRoot), "project.json");

    private static string ContextRoot(string jobRoot) => Path.Combine(jobRoot, "AI_Context");
}
