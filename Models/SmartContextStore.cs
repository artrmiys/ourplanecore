using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OurPlaneCore;

public static partial class SmartContextStore
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

    // requests/ and responses/ gain a file per AI call and never shrink.
    // Move entries untouched for keepDays into AI_Context/archive/ so the
    // active queue stays small. Crops and actions are left alone: actions may
    // hold drafts awaiting review, and crops can be referenced by either.
    public static (int Archived, int Failed) ArchiveStaleRequestFiles(string jobRoot, int keepDays = 60)
    {
        string contextRoot = ContextRoot(jobRoot);
        DateTime cutoff = DateTime.UtcNow.AddDays(-Math.Max(7, keepDays));
        int archived = 0;
        int failed = 0;
        foreach (string subdir in new[] { "requests", "responses" })
        {
            string source = Path.Combine(contextRoot, subdir);
            if (!Directory.Exists(source))
                continue;

            string target = Path.Combine(contextRoot, "archive", subdir);
            foreach (string file in Directory.EnumerateFiles(source))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) >= cutoff)
                        continue;

                    Directory.CreateDirectory(target);
                    string destination = Path.Combine(target, Path.GetFileName(file));
                    if (File.Exists(destination))
                        destination = StorageSupport.UniqueFilePath(destination);
                    File.Move(file, destination);
                    archived++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failed++;
                }
            }
        }

        return (archived, failed);
    }

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

}
