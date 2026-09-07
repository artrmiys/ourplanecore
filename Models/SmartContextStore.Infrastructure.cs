using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OurPlanCore;

public static partial class SmartContextStore
{
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
        try
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn(ex, $"Project registry update skipped for {GlobalRegistryPath}");
        }
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
