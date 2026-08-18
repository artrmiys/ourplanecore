using System.IO;
using System.Text.Json;

namespace OurPlanCore;

internal static partial class OurPlanPackagePortability
{
    private static void ValidateAiFileIdentifiers(
        string metadataPath,
        IReadOnlyDictionary<string, string?> rootStrings)
    {
        AiIdentifierSchema? schema = AiIdentifierSchemaFor(metadataPath);
        if (schema == null)
            return;

        foreach (string property in schema.RequiredProperties)
            ValidateAiIdentifier(metadataPath, rootStrings, property, required: true);
        foreach (string property in schema.OptionalProperties)
            ValidateAiIdentifier(metadataPath, rootStrings, property, required: false);

        string fileId = rootStrings.GetValueOrDefault(schema.FileKeyProperty) ?? "";
        string expected = Path.GetFileNameWithoutExtension(metadataPath);
        if (!fileId.Equals(expected, StringComparison.Ordinal))
        {
            throw new OurPlanPackageValidationException(
                $"AI metadata '{metadataPath}' identifier does not match its file name.");
        }
    }

    private static void ValidateAiIdentifier(
        string metadataPath,
        IReadOnlyDictionary<string, string?> rootStrings,
        string property,
        bool required)
    {
        string value = rootStrings.GetValueOrDefault(property) ?? "";
        if (string.IsNullOrWhiteSpace(value))
        {
            if (!required)
                return;
            throw new OurPlanPackageValidationException(
                $"AI metadata '{metadataPath}' is missing '{property}'.");
        }
        if (!SmartContextFileId.IsValid(value))
        {
            throw new OurPlanPackageValidationException(
                $"AI metadata '{metadataPath}' has an unsafe '{property}' identifier.");
        }
    }

    private static void ValidateObservationIdentifiers(string path)
    {
        int currentLine = 0;
        string? pendingProperty = null;
        string? observationId = null;
        void FinishLine()
        {
            if (currentLine == 0)
                return;
            if (!SmartContextFileId.IsValid(observationId ?? ""))
            {
                throw new OurPlanPackageValidationException(
                    $"AI observations '{path}' has an unsafe id on line {currentLine}.");
            }
        }

        BoundedJsonStream.InspectJsonLines(
            path,
            (line, token, value, depth) =>
            {
                if (line != currentLine)
                {
                    FinishLine();
                    currentLine = line;
                    pendingProperty = null;
                    observationId = null;
                }
                if (token == JsonTokenType.PropertyName)
                {
                    pendingProperty = depth == 1 ? value : null;
                    return;
                }
                if (depth == 1 &&
                    pendingProperty?.Equals("id", StringComparison.OrdinalIgnoreCase) == true &&
                    token == JsonTokenType.String)
                {
                    observationId = value;
                }
                pendingProperty = null;
            });
        FinishLine();
    }

    private static AiIdentifierSchema? AiIdentifierSchemaFor(string path)
    {
        string relative = AiContextRelativePath(path);
        if (string.IsNullOrWhiteSpace(relative) ||
            !Path.GetExtension(relative).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        string[] segments = relative
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2)
            return null;
        string parent = segments[0].ToLowerInvariant();
        string fileName = segments[1];
        if (parent == "responses" &&
            fileName.EndsWith(".openai.raw.json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return parent switch
        {
            "requests" => new("id", ["id"], ["observation_id"]),
            "responses" => new("id", ["id", "request_id"], ["observation_id"]),
            "actions" => new("request_id", ["request_id"], ["response_id"]),
            "markers" => new("id", ["id"], ["observation_id"]),
            "marker_sets" => new("id", ["id"], []),
            "crop_bookmarks" => new(
                "id",
                ["id"],
                ["request_id", "source_marker_id", "response_id", "action_draft_id"]),
            _ => null,
        };
    }

    private static string AiContextRelativePath(string path)
    {
        string? projectRelative = ProjectRelativeMetadataPath(path);
        const string prefix = "AI_Context/";
        return projectRelative?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true
            ? projectRelative[prefix.Length..]
            : "";
    }

    private static string ParentSegment(string relativePath) =>
        (Path.GetDirectoryName(relativePath) ?? "")
        .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
        .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
        .LastOrDefault()?
        .ToLowerInvariant() ?? "";

    private sealed record AiIdentifierSchema(
        string FileKeyProperty,
        string[] RequiredProperties,
        string[] OptionalProperties);
}
