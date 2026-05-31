using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OurPlaneCore;

public static partial class SmartContextStore
{
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

}
