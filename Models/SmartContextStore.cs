using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartTakeoffs;

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

public static class SmartContextStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string GlobalRoot
    {
        get
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "SmartTakeoffs");
        }
    }

    public static string GlobalRegistryPath => Path.Combine(GlobalRoot, "global_ai_index.jsonl");

    public static SmartProjectContext EnsureProjectContext(string jobRoot, string projectName)
    {
        string contextRoot = ContextRoot(jobRoot);
        Directory.CreateDirectory(contextRoot);
        Directory.CreateDirectory(Path.Combine(contextRoot, "pages"));
        Directory.CreateDirectory(Path.Combine(contextRoot, "crops"));

        string projectJsonPath = Path.Combine(contextRoot, "project.json");
        SmartProjectContext context = LoadProjectContext(projectJsonPath) ?? new SmartProjectContext
        {
            ProjectId = Guid.NewGuid().ToString(),
            CreatedAtUtc = DateTime.UtcNow.ToString("O"),
        };

        context.ProjectName = projectName;
        context.RootPath = Path.GetFullPath(jobRoot);
        context.UpdatedAtUtc = DateTime.UtcNow.ToString("O");

        File.WriteAllText(projectJsonPath, JsonSerializer.Serialize(context, JsonOptions));
        EnsureFile(Path.Combine(contextRoot, "project.md"), $"# {projectName}{Environment.NewLine}{Environment.NewLine}");
        EnsureFile(Path.Combine(contextRoot, "observations.jsonl"), "");
        EnsureFile(Path.Combine(contextRoot, "takeoff_rules_used.json"), "{\n  \"rules\": []\n}\n");
        RegisterProject(context);

        return context;
    }

    public static SmartObservation AddManualObservation(SmartTakeoffsJob job, PageInfo? page, string text) =>
        AddObservation(job, page, "manual", text);

    public static SmartObservation AddObservation(SmartTakeoffsJob job, PageInfo? page, string type, string text)
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

    private static SmartProjectContext? LoadProjectContext(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<SmartProjectContext>(File.ReadAllText(path))
                : null;
        }
        catch
        {
            return null;
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
                catch
                {
                    // Keep the registry usable even if an older line was edited by hand.
                }
            }
        }

        records.Add(record);
        File.WriteAllLines(GlobalRegistryPath, records.ConvertAll(r => JsonSerializer.Serialize(r)));
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

    private static void EnsureFile(string path, string initialContent)
    {
        if (!File.Exists(path))
            File.WriteAllText(path, initialContent);
    }

    private static string ContextRoot(string jobRoot) => Path.Combine(jobRoot, "AI_Context");
}
