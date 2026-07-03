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

    // The single-flight guard that marks a request "running" lives only in
    // memory, so a request left "running" on disk is a crash artifact: the app
    // died mid-call and the status was never advanced to done/failed. Such a
    // request is not re-runnable from the Inbox (IsRunnableAiStatus rejects
    // "running"), so it becomes permanently stuck. On job load nothing is
    // actually in flight yet, so any persisted "running" is stale — flip it to
    // "failed" so the user can retry it.
    public static int ResetStuckRunningRequests(string jobRoot)
    {
        string requestsDir = Path.Combine(ContextRoot(jobRoot), "requests");
        if (!Directory.Exists(requestsDir))
            return 0;

        int reset = 0;
        foreach (string file in Directory.EnumerateFiles(requestsDir, "*.json"))
        {
            try
            {
                SmartAiRequest? request = LoadJson<SmartAiRequest>(file);
                if (request == null ||
                    !request.Status.Equals("running", StringComparison.OrdinalIgnoreCase))
                    continue;

                request.Status = "failed";
                request.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
                IoUtil.WriteAllTextAtomic(file, JsonSerializer.Serialize(request, JsonOptions));
                reset++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // A single unreadable request file must not block job load.
            }
        }

        return reset;
    }

    // ArchiveStaleRequestFiles deliberately leaves crops alone because a crop
    // can be referenced by a request, an action draft, a marker, or a bookmark.
    // The consequence is that crops/ only ever grows. This reclaims the safe
    // subset: crop images older than keepDays whose file name is referenced by
    // no surviving JSON under any of the folders that can point at a crop. The
    // reference test is a plain substring scan of the file names, so it does not
    // depend on knowing every schema field that may carry a crop path, and it
    // errs toward keeping a crop whenever there is any doubt.
    public static int PruneOrphanCrops(string jobRoot, int keepDays = 60)
    {
        string contextRoot = ContextRoot(jobRoot);
        string cropsDir = Path.Combine(contextRoot, "crops");
        if (!Directory.Exists(cropsDir))
            return 0;

        DateTime cutoff = DateTime.UtcNow.AddDays(-Math.Max(7, keepDays));

        var referenceText = new System.Text.StringBuilder();
        foreach (string refDir in new[]
                 {
                     "requests", "responses", "actions", "markers",
                     "marker_sets", "crop_bookmarks", "exports",
                     Path.Combine("archive", "requests"),
                     Path.Combine("archive", "responses"),
                 })
        {
            string dir = Path.Combine(contextRoot, refDir);
            if (!Directory.Exists(dir))
                continue;

            foreach (string file in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    referenceText.Append(File.ReadAllText(file)).Append('\n');
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Treat an unreadable reference file as "might reference a
                    // crop" by simply skipping it; crops stay untouched.
                }
            }
        }

        string references = referenceText.ToString();
        int pruned = 0;
        foreach (string crop in Directory.EnumerateFiles(cropsDir, "*", SearchOption.AllDirectories))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(crop) >= cutoff)
                    continue;

                string name = Path.GetFileName(crop);
                if (references.Contains(name, StringComparison.OrdinalIgnoreCase))
                    continue;

                File.Delete(crop);
                pruned++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Skip anything locked or unreadable; try again next load.
            }
        }

        return pruned;
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
