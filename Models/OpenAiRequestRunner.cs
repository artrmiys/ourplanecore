using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OurPlaneCore;

public sealed class SmartAiRunResult
{
    public bool Success { get; init; }
    public string OutputText { get; init; } = "";
    public string Error { get; init; } = "";
    public string Model { get; init; } = "";
    public string ProviderResponseId { get; init; } = "";
    public string RawResponsePath { get; init; } = "";
}

public static class OpenAiRequestRunner
{
    private const string Endpoint = "https://api.openai.com/v1/responses";
    public const string DefaultModel = "gpt-5-mini";
    public const string QuickCropNoteModel = "gpt-5-mini";
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private static readonly string[] RoofRecognitionMarkerTypes =
    [
        "roof_note",
        "roof_edge_sample",
        "ridge_sample",
        "valley_sample",
        "roof_high_edge",
        "roof_low_edge",
        "overhang_sample",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private sealed record HttpResponseSnapshot(bool IsSuccess, int StatusCode, string Reason, string Body);

    // One transient failure (network blip, 429, 5xx) used to be terminal for
    // the whole AI request. Retry up to 3 times with growing backoff; user
    // cancellation and 4xx errors are never retried.
    private static async Task<(bool Sent, HttpResponseSnapshot Response, string Error)> SendWithRetriesAsync(
        string requestJson,
        string apiKey,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        string lastError = "";
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, Endpoint)
                {
                    Content = new StringContent(requestJson, Encoding.UTF8, "application/json"),
                };
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

                using HttpResponseMessage response = await _http.SendAsync(httpRequest, cancellationToken);
                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                int statusCode = (int)response.StatusCode;
                bool transient = statusCode == 429 || statusCode >= 500;
                if (!transient || attempt == maxAttempts)
                {
                    return (true,
                        new HttpResponseSnapshot(
                            response.IsSuccessStatusCode,
                            statusCode,
                            response.ReasonPhrase ?? response.StatusCode.ToString(),
                            body),
                        "");
                }

                lastError = $"HTTP {statusCode}";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                lastError = ex.Message;
                if (attempt == maxAttempts)
                {
                    return (false,
                        new HttpResponseSnapshot(false, 0, "", ""),
                        $"OpenAI request failed after {maxAttempts} attempts: {lastError}");
                }
            }

            int delaySeconds = attempt * attempt * 2;
            AppLog.Warn($"OpenAI request attempt {attempt}/{maxAttempts} failed ({lastError}); retrying in {delaySeconds}s.");
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
        }

        return (false, new HttpResponseSnapshot(false, 0, "", ""), lastError);
    }

    public static async Task<SmartAiRunResult> RunAsync(
        OurPlaneCoreJob job,
        SmartAiRequest request,
        string apiKey,
        string model,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new SmartAiRunResult
            {
                Success = false,
                Error = "OPENAI_API_KEY is not set.",
                Model = CleanModel(model),
            };
        }

        string cleanModel = CleanModel(model);
        var content = new List<object>
        {
            new
            {
                type = "input_text",
                text = BuildPrompt(job, request),
            },
        };

        string? cropPath = ResolveContextPath(job, request.CropPath, job.AIContextRoot);
        AddImageContent(content, cropPath, "high");

        foreach (string contextCropPath in request.ContextCropPaths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Where(path => !string.Equals(path, request.CropPath, StringComparison.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            AddImageContent(
                content,
                ResolveContextPath(job, contextCropPath, job.AIContextRoot),
                "high");
        }

        var payload = new Dictionary<string, object?>
        {
            ["model"] = cleanModel,
            ["instructions"] =
                "You are an assistant inside a construction takeoff application. " +
                "Use only the provided plan crop and JSON context. Be concise, " +
                "call out uncertainty, and do not invent dimensions or quantities.",
            ["input"] = new[]
            {
                new
                {
                    role = "user",
                    content,
                },
            },
            ["store"] = false,
            ["max_output_tokens"] = MaxOutputTokensForRequest(request.Type),
        };
        AddReasoningOptions(payload, cleanModel);

        string requestJson = JsonSerializer.Serialize(payload, JsonOptions);
        (bool sent, HttpResponseSnapshot http, string sendError) =
            await SendWithRetriesAsync(requestJson, apiKey, cancellationToken);
        if (!sent)
        {
            return new SmartAiRunResult
            {
                Success = false,
                Error = sendError,
                Model = cleanModel,
            };
        }

        string body = http.Body;
        string rawPath = SaveRawResponse(job, request, body);
        string providerResponseId = OpenAiResponseParser.ExtractString(body, "id");

        if (!http.IsSuccess)
        {
            return new SmartAiRunResult
            {
                Success = false,
                Error = OpenAiResponseParser.ExtractError(body, http.Reason),
                Model = cleanModel,
                ProviderResponseId = providerResponseId,
                RawResponsePath = rawPath,
            };
        }

        string status = OpenAiResponseParser.ExtractString(body, "status");
        if (string.Equals(status, "incomplete", StringComparison.OrdinalIgnoreCase))
        {
            return new SmartAiRunResult
            {
                Success = false,
                Error = OpenAiResponseParser.ExtractIncompleteError(body),
                Model = cleanModel,
                ProviderResponseId = providerResponseId,
                RawResponsePath = rawPath,
            };
        }

        string output = OpenAiResponseParser.ExtractOutputText(body);
        if (string.IsNullOrWhiteSpace(output))
        {
            return new SmartAiRunResult
            {
                Success = false,
                Error = "OpenAI returned no output text. See raw response JSON.",
                Model = cleanModel,
                ProviderResponseId = providerResponseId,
                RawResponsePath = rawPath,
            };
        }

        return new SmartAiRunResult
        {
            Success = true,
            OutputText = output.Trim(),
            Model = cleanModel,
            ProviderResponseId = providerResponseId,
            RawResponsePath = rawPath,
        };
    }

    public static async Task<SmartAiRunResult> RunStructuredJsonAsync(
        OurPlaneCoreJob job,
        string requestId,
        string prompt,
        object jsonSchema,
        string schemaName,
        string instructions,
        string apiKey,
        string model,
        int maxOutputTokens,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new SmartAiRunResult
            {
                Success = false,
                Error = "OPENAI_API_KEY is not set.",
                Model = CleanModel(model),
            };
        }

        string cleanModel = CleanModel(model);
        var payload = new Dictionary<string, object?>
        {
            ["model"] = cleanModel,
            ["instructions"] = string.IsNullOrWhiteSpace(instructions)
                ? "Return only JSON that matches the requested schema."
                : instructions.Trim(),
            ["input"] = new[]
            {
                new
                {
                    role = "user",
                    content = new[]
                    {
                        new
                        {
                            type = "input_text",
                            text = prompt,
                        },
                    },
                },
            },
            ["text"] = new
            {
                format = new
                {
                    type = "json_schema",
                    name = string.IsNullOrWhiteSpace(schemaName) ? "structured_output" : schemaName.Trim(),
                    strict = true,
                    schema = jsonSchema,
                },
            },
            ["store"] = false,
            ["max_output_tokens"] = Math.Clamp(maxOutputTokens, 512, 12000),
        };
        AddReasoningOptions(payload, cleanModel);

        string requestJson = JsonSerializer.Serialize(payload, JsonOptions);
        (bool sent, HttpResponseSnapshot http, string sendError) =
            await SendWithRetriesAsync(requestJson, apiKey, cancellationToken);
        if (!sent)
        {
            return new SmartAiRunResult
            {
                Success = false,
                Error = sendError,
                Model = cleanModel,
            };
        }

        string body = http.Body;
        string rawPath = SaveRawResponse(job, requestId, body);
        string providerResponseId = OpenAiResponseParser.ExtractString(body, "id");

        if (!http.IsSuccess)
        {
            return new SmartAiRunResult
            {
                Success = false,
                Error = OpenAiResponseParser.ExtractError(body, http.Reason),
                Model = cleanModel,
                ProviderResponseId = providerResponseId,
                RawResponsePath = rawPath,
            };
        }

        string status = OpenAiResponseParser.ExtractString(body, "status");
        if (string.Equals(status, "incomplete", StringComparison.OrdinalIgnoreCase))
        {
            return new SmartAiRunResult
            {
                Success = false,
                Error = OpenAiResponseParser.ExtractIncompleteError(body),
                Model = cleanModel,
                ProviderResponseId = providerResponseId,
                RawResponsePath = rawPath,
            };
        }

        string output = OpenAiResponseParser.ExtractOutputText(body);
        if (string.IsNullOrWhiteSpace(output))
        {
            return new SmartAiRunResult
            {
                Success = false,
                Error = "OpenAI returned no output text. See raw response JSON.",
                Model = cleanModel,
                ProviderResponseId = providerResponseId,
                RawResponsePath = rawPath,
            };
        }

        return new SmartAiRunResult
        {
            Success = true,
            OutputText = output.Trim(),
            Model = cleanModel,
            ProviderResponseId = providerResponseId,
            RawResponsePath = rawPath,
        };
    }

    private static string CleanModel(string model) =>
        string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();

    private static int MaxOutputTokensForRequest(string requestType)
    {
        return requestType switch
        {
            "quick_crop_note_request" => 4096,
            "roof_recognition_request" => 4096,
            "pdf_sheet_metadata_fallback" => 3000,
            _ => 2400,
        };
    }

    private static void AddReasoningOptions(Dictionary<string, object?> payload, string model)
    {
        if (!IsReasoningModel(model))
            return;

        payload["reasoning"] = new { effort = "low" };
    }

    private static bool IsReasoningModel(string model)
    {
        string clean = model.Trim();
        return clean.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase) ||
               clean.StartsWith("o1", StringComparison.OrdinalIgnoreCase) ||
               clean.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
               clean.StartsWith("o4", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddImageContent(List<object> content, string? path, string detail)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        string dataUrl = $"data:image/png;base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
        content.Add(new
        {
            type = "input_image",
            image_url = dataUrl,
            detail,
        });
    }

    private static string BuildPrompt(OurPlaneCoreJob job, SmartAiRequest request)
    {
        if (string.Equals(request.Type, "pdf_sheet_metadata_fallback", StringComparison.OrdinalIgnoreCase))
            return BuildPdfSheetMetadataPrompt(job, request);

        if (string.Equals(request.Type, "crop_bookmark_request", StringComparison.OrdinalIgnoreCase))
            return BuildCropBookmarkPrompt(job, request);

        if (string.Equals(request.Type, "find_similar_marker_request", StringComparison.OrdinalIgnoreCase))
            return BuildFindSimilarMarkerPrompt(job, request);

        if (string.Equals(request.Type, "roof_recognition_request", StringComparison.OrdinalIgnoreCase))
            return BuildRoofRecognitionPrompt(job, request);

        if (string.Equals(request.Type, "quick_crop_note_request", StringComparison.OrdinalIgnoreCase))
            return BuildQuickCropNotePrompt(job, request);

        var sb = new StringBuilder();
        sb.AppendLine("Analyze this takeoff request.");
        sb.AppendLine();
        sb.AppendLine("Expected answer:");
        sb.AppendLine("- direct answer first;");
        sb.AppendLine("- visible evidence from the crop;");
        sb.AppendLine("- possible takeoff action or next check;");
        sb.AppendLine("- uncertainty if the crop or layer context is insufficient.");
        sb.AppendLine();
        sb.AppendLine("If you can draft an action for SmartTrace, SmartCheck, or takeoff creation,");
        sb.AppendLine("end with one fenced JSON block using this shape:");
        sb.AppendLine("{");
        sb.AppendLine("  \"summary\": \"short summary\",");
        sb.AppendLine("  \"actions\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"type\": \"trace_line | trace_area | check_note | takeoff_suggestion\",");
        sb.AppendLine("      \"label\": \"short label\",");
        sb.AppendLine("      \"page\": \"page name\",");
        sb.AppendLine("      \"measurement_type\": \"line | area | point\",");
        sb.AppendLine("      \"confidence\": 0.0,");
        sb.AppendLine("      \"notes\": \"why this action is suggested\",");
        sb.AppendLine("      \"points\": [{ \"x\": 0.0, \"y\": 0.0 }]");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        sb.AppendLine("Use PDF point coordinates only when visible enough; otherwise leave points empty.");
        sb.AppendLine();
        sb.AppendLine("Request JSON:");
        sb.AppendLine(JsonSerializer.Serialize(request, JsonOptions));

        string? layerPath = ResolveContextPath(job, request.LayerManifestPath, job.RootPath);
        if (!string.IsNullOrWhiteSpace(layerPath) && File.Exists(layerPath))
        {
            sb.AppendLine();
            sb.AppendLine("Page layers.json:");
            sb.AppendLine(File.ReadAllText(layerPath));
        }

        return sb.ToString();
    }

    private static string BuildQuickCropNotePrompt(OurPlaneCoreJob job, SmartAiRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Read the attached construction-plan crop and create note text for a visible sheet note.");
        sb.AppendLine("Return only the note text. Do not include JSON, explanations, or markdown fences.");
        sb.AppendLine();
        sb.AppendLine("Formatting rules:");
        sb.AppendLine("- Preserve the visible content as closely as possible.");
        sb.AppendLine("- If the crop is a table or schedule, return a compact Markdown table with the same row/column meaning.");
        sb.AppendLine("- If the crop is a callout, key note, dimension note, or title block fragment, keep the line breaks and labels.");
        sb.AppendLine("- If there are abbreviations, keep them exactly as shown unless unreadable.");
        sb.AppendLine("- If the crop is unreadable or empty, return: [unreadable]");
        sb.AppendLine();
        sb.AppendLine("Request:");
        sb.AppendLine(request.Prompt);
        sb.AppendLine();
        sb.AppendLine("Request JSON:");
        sb.AppendLine(JsonSerializer.Serialize(request, JsonOptions));

        string? layerPath = ResolveContextPath(job, request.LayerManifestPath, job.RootPath);
        if (!string.IsNullOrWhiteSpace(layerPath) && File.Exists(layerPath))
        {
            sb.AppendLine();
            sb.AppendLine("Page layers.json:");
            sb.AppendLine(File.ReadAllText(layerPath));
        }

        return sb.ToString();
    }

    private static string BuildRoofRecognitionPrompt(OurPlaneCoreJob job, SmartAiRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Detect reviewable roof marker candidates from a construction-plan sheet crop.");
        sb.AppendLine();
        sb.AppendLine("Review mode rules:");
        sb.AppendLine("- Suggest AI markers only; do not create measurements, quantities, or accepted 3D geometry.");
        sb.AppendLine("- Treat every result as a candidate that the user must review before it becomes a marker.");
        sb.AppendLine("- Prefer fewer high-signal candidates over many weak guesses.");
        sb.AppendLine("- If coordinates are estimated from the crop, keep confidence modest and say that in notes.");
        sb.AppendLine("- If the crop is insufficient, return an empty actions array with a clear summary.");
        sb.AppendLine();
        sb.AppendLine("Allowed roof marker candidate action.type values:");
        foreach (string markerType in RoofRecognitionMarkerTypes)
            sb.AppendLine($"- {markerType}");
        sb.AppendLine();
        sb.AppendLine("Use action shapes like this:");
        sb.AppendLine("- ridge_sample / valley_sample / roof_edge_sample / roof_high_edge / roof_low_edge: measurement_type=line, 2+ PDF points.");
        sb.AppendLine("- overhang_sample: measurement_type=line when edge-like, or point if only one clear location is visible.");
        sb.AppendLine("- roof_note: measurement_type=point, one PDF point near the note, with roof type/pitch text in notes when visible.");
        sb.AppendLine();
        sb.AppendLine("Expected answer:");
        sb.AppendLine("- one short prose summary first;");
        sb.AppendLine("- then exactly one fenced JSON block using this shape:");
        sb.AppendLine("{");
        sb.AppendLine("  \"summary\": \"short summary\",");
        sb.AppendLine("  \"actions\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"type\": \"ridge_sample | valley_sample | roof_edge_sample | roof_high_edge | roof_low_edge | overhang_sample | roof_note\",");
        sb.AppendLine("      \"label\": \"short human label\",");
        sb.AppendLine("      \"page\": \"page name\",");
        sb.AppendLine("      \"measurement_type\": \"line | point\",");
        sb.AppendLine("      \"confidence\": 0.0,");
        sb.AppendLine("      \"notes\": \"visible evidence, roof type/pitch if visible, and uncertainty\",");
        sb.AppendLine("      \"points\": [{ \"x\": 0.0, \"y\": 0.0 }]");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Coordinate rule:");
        sb.AppendLine("- Use PDF point coordinates, not image pixels.");
        sb.AppendLine("- The request prompt contains the PDF crop rectangle; estimate points proportionally inside that rectangle only when visible enough.");
        sb.AppendLine("- Leave points empty instead of inventing coordinates.");
        sb.AppendLine();
        sb.AppendLine("Request prompt:");
        sb.AppendLine(request.Prompt);
        sb.AppendLine();
        sb.AppendLine("Request JSON:");
        sb.AppendLine(JsonSerializer.Serialize(request, JsonOptions));

        sb.AppendLine();
        sb.AppendLine("Attached images:");
        sb.AppendLine(string.IsNullOrWhiteSpace(request.CropPath)
            ? "- Main roof crop: none"
            : $"- Main roof crop: {request.CropPath}");
        foreach (string contextCropPath in request.ContextCropPaths)
            sb.AppendLine($"- Marker/evidence crop: {contextCropPath}");

        string modelPath = SmartMassingDraftService.ModelPath(job);
        if (File.Exists(modelPath))
        {
            sb.AppendLine();
            sb.AppendLine("Existing AI_Context/3d_massing/model.json:");
            sb.AppendLine(File.ReadAllText(modelPath));
        }

        string markerContextPath = Path.Combine(job.AIContextRoot, "exports", "markers_context.json");
        if (File.Exists(markerContextPath))
        {
            sb.AppendLine();
            sb.AppendLine("Current markers_context.json:");
            sb.AppendLine(File.ReadAllText(markerContextPath));
        }

        string? layerPath = ResolveContextPath(job, request.LayerManifestPath, job.RootPath);
        if (!string.IsNullOrWhiteSpace(layerPath) && File.Exists(layerPath))
        {
            sb.AppendLine();
            sb.AppendLine("Page layers.json:");
            sb.AppendLine(File.ReadAllText(layerPath));
        }

        return sb.ToString();
    }

    private static string BuildFindSimilarMarkerPrompt(OurPlaneCoreJob job, SmartAiRequest request)
    {
        SmartAiMarker? marker = FindSourceMarker(job, request);
        var sb = new StringBuilder();
        sb.AppendLine("Find similar construction-plan conditions from a saved AI marker.");
        sb.AppendLine();
        sb.AppendLine("Review mode rules:");
        sb.AppendLine("- Create reviewable AI action drafts only.");
        sb.AppendLine("- Do not apply measurements, quantities, or geometry automatically.");
        sb.AppendLine("- Use the attached marker crop as the visual source example.");
        sb.AppendLine("- Use marker/page/layer context only as supporting evidence.");
        sb.AppendLine("- If a candidate is uncertain, keep confidence low and explain why.");
        sb.AppendLine();
        sb.AppendLine("Expected answer:");
        sb.AppendLine("- short summary of what the marker represents;");
        sb.AppendLine("- similar candidates or reasons none can be identified;");
        sb.AppendLine("- explicit uncertainty and next manual review step;");
        sb.AppendLine("- one fenced JSON block with reviewable actions.");
        sb.AppendLine();
        sb.AppendLine("Action draft JSON shape:");
        sb.AppendLine("{");
        sb.AppendLine("  \"summary\": \"short summary\",");
        sb.AppendLine("  \"actions\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"type\": \"check_note | takeoff_suggestion | trace_line | trace_area\",");
        sb.AppendLine("      \"label\": \"short label\",");
        sb.AppendLine("      \"page\": \"page name\",");
        sb.AppendLine("      \"measurement_type\": \"line | area | point\",");
        sb.AppendLine("      \"confidence\": 0.0,");
        sb.AppendLine("      \"notes\": \"why this is similar and what the user should review\",");
        sb.AppendLine("      \"points\": [{ \"x\": 0.0, \"y\": 0.0 }]");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        sb.AppendLine("Use PDF point coordinates only when visible enough from provided context; otherwise leave points empty.");
        sb.AppendLine();
        sb.AppendLine("Marker request prompt:");
        sb.AppendLine(request.Prompt);
        sb.AppendLine();
        sb.AppendLine("Request JSON:");
        sb.AppendLine(JsonSerializer.Serialize(request, JsonOptions));
        sb.AppendLine();
        sb.AppendLine("Marker crop:");
        sb.AppendLine(string.IsNullOrWhiteSpace(request.CropPath)
            ? "No marker crop path was provided."
            : $"{request.CropPath} (attached as input_image when the file exists)");

        if (request.ContextCropPaths.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Additional nearby sheet context crops:");
            foreach (string contextCropPath in request.ContextCropPaths)
                sb.AppendLine($"- {contextCropPath} (attached as another input_image when the file exists)");
        }

        if (marker != null)
        {
            sb.AppendLine();
            sb.AppendLine("Source marker JSON:");
            sb.AppendLine(JsonSerializer.Serialize(marker, JsonOptions));
            AppendMarkerFeedbackContext(job, marker, sb);
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("Source marker JSON: unavailable; rely on request JSON, marker crop, and marker context.");
        }

        string markerContextPath = Path.Combine(job.AIContextRoot, "exports", "markers_context.json");
        if (File.Exists(markerContextPath))
        {
            sb.AppendLine();
            sb.AppendLine("Current markers_context.json:");
            sb.AppendLine(File.ReadAllText(markerContextPath));
        }

        if (request.LayerCount > 0 || request.Layers.Count > 0 || !string.IsNullOrWhiteSpace(request.LayerManifestPath))
        {
            sb.AppendLine();
            sb.AppendLine("Request page/layer context:");
            sb.AppendLine(JsonSerializer.Serialize(new
            {
                request.Page,
                request.PageFolder,
                request.LayerManifestPath,
                request.LayerCount,
                request.Layers,
            }, JsonOptions));
        }

        string? layerPath = ResolveContextPath(job, request.LayerManifestPath, job.RootPath);
        if (!string.IsNullOrWhiteSpace(layerPath) && File.Exists(layerPath))
        {
            sb.AppendLine();
            sb.AppendLine("Page layers.json:");
            sb.AppendLine(File.ReadAllText(layerPath));
        }

        return sb.ToString();
    }

    private static void AppendMarkerFeedbackContext(OurPlaneCoreJob job, SmartAiMarker marker, StringBuilder sb)
    {
        IReadOnlyList<SmartMarkerFeedbackRecord> feedback = SmartLearningStore.LoadProjectMarkerFeedback(job)
            .Where(record =>
                string.Equals(record.SourceMarkerId, marker.Id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(record.SourceMarkerType, marker.Type, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(record => record.CreatedAtUtc)
            .Take(24)
            .ToList();
        if (feedback.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("Prior marker candidate feedback:");
        foreach (SmartMarkerFeedbackRecord record in feedback)
        {
            string label = string.IsNullOrWhiteSpace(record.Label) ? record.ActionType : record.Label;
            sb.Append("- ");
            sb.Append(record.Outcome);
            if (record.Applied)
                sb.Append(" applied");
            sb.Append($": {label}");
            if (!string.IsNullOrWhiteSpace(record.Page))
                sb.Append($" on {record.Page}");
            if (record.Confidence > 0)
                sb.Append($" ({record.Confidence:P0})");
            if (!string.IsNullOrWhiteSpace(record.Notes))
                sb.Append($" - {record.Notes}");
            sb.AppendLine();
        }
    }

    private static string BuildCropBookmarkPrompt(OurPlaneCoreJob job, SmartAiRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Analyze this bookmarked construction-plan crop.");
        sb.AppendLine();
        sb.AppendLine("Workflow intent:");
        sb.AppendLine("- The user collects crop bookmarks while reviewing sheets.");
        sb.AppendLine("- This request processes only this new bookmarked crop.");
        sb.AppendLine("- Do not reprocess older bookmarks unless they are directly referenced in marker context.");
        sb.AppendLine("- Record what is visible, whether it is useful for takeoff, and what should happen next.");
        sb.AppendLine();
        sb.AppendLine("Expected answer:");
        sb.AppendLine("- direct finding first;");
        sb.AppendLine("- visible evidence from the crop;");
        sb.AppendLine("- whether similar items should be searched later;");
        sb.AppendLine("- uncertainty if the crop is insufficient.");
        sb.AppendLine();
        sb.AppendLine("If you can draft a reviewable action, end with one fenced JSON block using this shape:");
        sb.AppendLine("{");
        sb.AppendLine("  \"summary\": \"short summary\",");
        sb.AppendLine("  \"actions\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"type\": \"trace_line | trace_area | check_note | takeoff_suggestion\",");
        sb.AppendLine("      \"label\": \"short label\",");
        sb.AppendLine("      \"page\": \"page name\",");
        sb.AppendLine("      \"measurement_type\": \"line | area | point\",");
        sb.AppendLine("      \"confidence\": 0.0,");
        sb.AppendLine("      \"notes\": \"why this action is suggested\",");
        sb.AppendLine("      \"points\": [{ \"x\": 0.0, \"y\": 0.0 }]");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        sb.AppendLine("Use PDF point coordinates only when visible enough; otherwise leave points empty.");
        sb.AppendLine();
        sb.AppendLine("Bookmark prompt:");
        sb.AppendLine(request.Prompt);
        sb.AppendLine();
        sb.AppendLine("Request JSON:");
        sb.AppendLine(JsonSerializer.Serialize(request, JsonOptions));

        string markerContextPath = Path.Combine(job.AIContextRoot, "exports", "markers_context.json");
        if (File.Exists(markerContextPath))
        {
            sb.AppendLine();
            sb.AppendLine("Current markers_context.json:");
            sb.AppendLine(File.ReadAllText(markerContextPath));
        }

        string? layerPath = ResolveContextPath(job, request.LayerManifestPath, job.RootPath);
        if (!string.IsNullOrWhiteSpace(layerPath) && File.Exists(layerPath))
        {
            sb.AppendLine();
            sb.AppendLine("Page layers.json:");
            sb.AppendLine(File.ReadAllText(layerPath));
        }

        return sb.ToString();
    }

    private static string BuildPdfSheetMetadataPrompt(OurPlaneCoreJob job, SmartAiRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are reading a construction sheet title block for Auto Rename / Auto Scale.");
        sb.AppendLine("Use only the provided crop and JSON context.");
        sb.AppendLine("Return exactly one fenced JSON block and no extra prose.");
        sb.AppendLine();
        sb.AppendLine("JSON shape:");
        sb.AppendLine("{");
        sb.AppendLine("  \"sheet_label\": \"A-700\",");
        sb.AppendLine("  \"sheet_key\": \"a700\",");
        sb.AppendLine("  \"sheet_title\": \"FINISH PLAN\",");
        sb.AppendLine("  \"suffix\": \"f\",");
        sb.AppendLine("  \"skip_scale\": false,");
        sb.AppendLine("  \"selected_scale_text\": \"1/8\\\" = 1'0\\\"\",");
        sb.AppendLine("  \"confidence\": \"gpt-image-high | gpt-image-medium | gpt-image-low\",");
        sb.AppendLine("  \"warnings\": []");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Suffix rules:");
        sb.AppendLine("- GENERAL NOTES / NOTES -> n, skip scale");
        sb.AppendLine("- SCHEDULE / SCHEDULES -> sc, skip scale");
        sb.AppendLine("- S-sheet DETAILS / DETAIL / DEATIL / S500-S699 typical details -> d, skip scale, even when the title also says SECTION");
        sb.AppendLine("- A-sheet FINISH / FINISHES / INTERIOR -> f");
        sb.AppendLine("- Title/filename saying SHEAR / SHEAR WALL -> shw; BRACING PLAN with visible shear wall callouts -> shw; do not infer shw from roof/framing/foundation notes alone");
        sb.AppendLine("- FIRST/1ST FLOOR -> 1st; SECOND/2ND -> 2nd; THIRD/3RD -> 3rd; FOURTH/4TH -> 4th");
        sb.AppendLine("- ROOF / ROOF FRAMING -> rf");
        sb.AppendLine("- SECTION / SECTIONS -> sec and scale eligible");
        sb.AppendLine("- UNIT PLANS -> u; WALL/PARTITION TYPES -> wt; FLOOR TYPES/FLOOR-CEILING -> ft");
        sb.AppendLine();
        sb.AppendLine("Allowed scales:");
        sb.AppendLine("1/32\" = 1'0\", 3/64\" = 1'0\", 1/16\" = 1'0\", 3/32\" = 1'0\", 1/10\" = 1'0\", 1/8\" = 1'0\", 3/16\" = 1'0\", 1/4\" = 1'0\", 3/8\" = 1'0\", 1/2\" = 1'0\", 3/4\" = 1'0\", 1\" = 1'0\", 1-1/2\" = 1'0\", 3\" = 1'0\", 1\" = 1\".");
        sb.AppendLine("If the title block says AS NOTED and no body scale is visible in the crop, leave selected_scale_text empty and add a warning.");
        sb.AppendLine("If unreadable, leave uncertain fields empty and set confidence to gpt-image-low.");
        sb.AppendLine();
        sb.AppendLine("Request prompt:");
        sb.AppendLine(request.Prompt);
        sb.AppendLine();
        sb.AppendLine("Request JSON:");
        sb.AppendLine(JsonSerializer.Serialize(request, JsonOptions));

        string? layerPath = ResolveContextPath(job, request.LayerManifestPath, job.RootPath);
        if (!string.IsNullOrWhiteSpace(layerPath) && File.Exists(layerPath))
        {
            sb.AppendLine();
            sb.AppendLine("Page layers.json:");
            sb.AppendLine(File.ReadAllText(layerPath));
        }

        return sb.ToString();
    }

    private static SmartAiMarker? FindSourceMarker(OurPlaneCoreJob job, SmartAiRequest request)
    {
        string markerId = ExtractSourceMarkerId(request.MeasurementSummary);
        if (string.IsNullOrWhiteSpace(markerId))
            markerId = ExtractSourceMarkerId(request.Prompt);

        if (!string.IsNullOrWhiteSpace(markerId) &&
            SmartContextStore.LoadAiMarker(job, markerId) is { } marker)
        {
            return marker;
        }

        string? requestCropPath = ResolveContextPath(job, request.CropPath, job.AIContextRoot);
        if (string.IsNullOrWhiteSpace(requestCropPath))
            return null;

        string normalizedRequestCrop = Path.GetFullPath(requestCropPath);
        return SmartContextStore.LoadAiMarkers(job).FirstOrDefault(marker =>
        {
            string? markerCropPath = ResolveContextPath(job, marker.CropPath, job.AIContextRoot);
            return !string.IsNullOrWhiteSpace(markerCropPath) &&
                   string.Equals(
                       Path.GetFullPath(markerCropPath),
                       normalizedRequestCrop,
                       StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string ExtractSourceMarkerId(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        string[] lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();
            const string label = "Source marker id:";
            if (trimmed.Equals(label, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1 < lines.Length ? lines[i + 1].Trim() : "";
            }

            if (trimmed.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                return trimmed[label.Length..].Trim();
        }

        return "";
    }

    private static string? ResolveContextPath(string jobRoot, string value, string basePath)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return Path.IsPathFullyQualified(value)
            ? value
            : Path.GetFullPath(Path.Combine(basePath, value));
    }

    private static string? ResolveContextPath(OurPlaneCoreJob job, string value, string basePath) =>
        ResolveContextPath(job.RootPath, value, basePath);

    private static string SaveRawResponse(OurPlaneCoreJob job, SmartAiRequest request, string body)
    {
        return SaveRawResponse(job, request.Id, body);
    }

    private static string SaveRawResponse(OurPlaneCoreJob job, string requestId, string body)
    {
        string cleanRequestId = string.IsNullOrWhiteSpace(requestId)
            ? $"openai_{DateTime.UtcNow:yyyyMMdd_HHmmss}"
            : requestId.Trim();
        string path = Path.Combine(job.AIContextRoot, "responses", $"{cleanRequestId}.openai.raw.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? job.AIContextRoot);
        IoUtil.WriteAllTextAtomic(path, body);
        return Path.GetRelativePath(job.RootPath, path);
    }

}
