using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace OurPlaneCore;

public static class OpenAiResponseParser
{
    public static string ExtractOutputText(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            var parts = new List<string>();
            CollectOutputText(doc.RootElement, parts);
            return string.Join(Environment.NewLine, parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }
        catch
        {
            return "";
        }
    }

    public static string ExtractError(string json, string fallback)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out JsonElement error) &&
                error.ValueKind == JsonValueKind.Object &&
                error.TryGetProperty("message", out JsonElement message))
            {
                return message.GetString() ?? fallback;
            }
        }
        catch
        {
            // Fall through to the HTTP reason phrase.
        }

        return fallback;
    }

    public static string ExtractString(string json, string propertyName)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(propertyName, out JsonElement value)
                ? value.GetString() ?? ""
                : "";
        }
        catch
        {
            return "";
        }
    }

    public static string ExtractIncompleteError(string json)
    {
        string reason = ExtractIncompleteReason(json);
        return string.IsNullOrWhiteSpace(reason)
            ? "OpenAI response was incomplete. See raw response JSON."
            : $"OpenAI response was incomplete ({reason}). See raw response JSON.";
    }

    private static string ExtractIncompleteReason(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("incomplete_details", out JsonElement details) &&
                details.ValueKind == JsonValueKind.Object &&
                details.TryGetProperty("reason", out JsonElement reason))
            {
                return reason.GetString() ?? "";
            }
        }
        catch
        {
            return "";
        }

        return "";
    }

    private static void CollectOutputText(JsonElement element, List<string> parts)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("type", out JsonElement type) &&
                string.Equals(type.GetString(), "output_text", StringComparison.Ordinal) &&
                element.TryGetProperty("text", out JsonElement text))
            {
                parts.Add(text.GetString() ?? "");
            }

            foreach (JsonProperty prop in element.EnumerateObject())
                CollectOutputText(prop.Value, parts);
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
                CollectOutputText(child, parts);
        }
    }
}
