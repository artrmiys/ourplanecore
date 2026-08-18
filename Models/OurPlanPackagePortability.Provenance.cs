using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OurPlanCore;

internal static partial class OurPlanPackagePortability
{
    private static readonly HashSet<string> PortableLearningJsonLines = new(StringComparer.OrdinalIgnoreCase)
    {
        "AI_Context/learning/sheet_feedback.jsonl",
        "AI_Context/learning/project_reviews.jsonl",
        "AI_Context/learning/marker_feedback.jsonl",
    };

    private static bool TryCreatePortableProvenanceCopy(
        OurPlanPackageSourceFile source,
        string workspaceRoot,
        string stagingRoot,
        out string? stagedPath)
    {
        string logical = source.LogicalPath.Replace('\\', '/');
        bool jsonLines = PortableLearningJsonLines.Contains(logical);
        bool json = logical.Equals(
                        "AI_Context/learning/project_learning_summary.json",
                        StringComparison.OrdinalIgnoreCase) ||
                    logical.Equals(
                        "AI_Context/materials/materials_unique_by_page.json",
                        StringComparison.OrdinalIgnoreCase);
        if (!jsonLines && !json)
        {
            stagedPath = null;
            return false;
        }

        stagedPath = Path.Combine(
            stagingRoot,
            logical.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
        bool changed = jsonLines
            ? WritePortableJsonLines(source.FullPath, stagedPath, workspaceRoot)
            : WritePortableJson(source.FullPath, stagedPath, workspaceRoot);
        if (changed)
            return true;

        File.Delete(stagedPath);
        stagedPath = null;
        return false;
    }

    private static bool WritePortableJson(string source, string destination, string workspaceRoot)
    {
        if (new FileInfo(source).Length > MaxDomRewriteBytes)
        {
            throw new OurPlanPackageValidationException(
                $"Project provenance '{source}' exceeds the safe " +
                $"{MaxDomRewriteBytes / (1024 * 1024)} MB rewrite limit.");
        }
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        JsonNode document = JsonNode.Parse(input)
            ?? throw new OurPlanPackageValidationException($"Project provenance '{source}' is empty.");
        bool changed = ScrubProvenance(document, workspaceRoot);
        if (changed)
            File.WriteAllText(destination, document.ToJsonString(OurPlanCoreJobStore.JsonOptions));
        return changed;
    }

    private static bool WritePortableJsonLines(string source, string destination, string workspaceRoot)
    {
        bool changed = false;
        using var input = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var output = new StreamWriter(destination, append: false, new UTF8Encoding(false));
        string? line;
        while ((line = input.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                output.WriteLine();
                continue;
            }
            JsonNode document = JsonNode.Parse(line)
                ?? throw new OurPlanPackageValidationException($"Project provenance '{source}' has an empty record.");
            bool lineChanged = ScrubProvenance(document, workspaceRoot);
            changed |= lineChanged;
            output.WriteLine(lineChanged ? document.ToJsonString() : line);
        }
        return changed;
    }

    private static bool ScrubProvenance(JsonNode node, string workspaceRoot)
    {
        bool changed = false;
        if (node is JsonObject obj)
        {
            foreach ((string key, JsonNode? value) in obj.ToList())
            {
                if (value is JsonValue scalar && scalar.TryGetValue(out string? text))
                {
                    if (key.Equals("job_root", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(text, ".", StringComparison.Ordinal))
                    {
                        obj[key] = ".";
                        changed = true;
                        continue;
                    }
                    if ((key.Equals("source_pdf", StringComparison.OrdinalIgnoreCase) ||
                         key.Equals("source_path", StringComparison.OrdinalIgnoreCase)) &&
                        !string.IsNullOrWhiteSpace(text))
                    {
                        string portable = PortableProvenancePath(text, workspaceRoot);
                        if (!portable.Equals(text, StringComparison.Ordinal))
                        {
                            obj[key] = portable;
                            changed = true;
                            continue;
                        }
                    }
                }
                if (value != null)
                    changed |= ScrubProvenance(value, workspaceRoot);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? child in array)
            {
                if (child != null)
                    changed |= ScrubProvenance(child, workspaceRoot);
            }
        }
        return changed;
    }

    private static string PortableProvenancePath(string value, string workspaceRoot)
    {
        try
        {
            if (!Path.IsPathRooted(value))
                return value.Replace('\\', '/');
            string absolute = Path.GetFullPath(value);
            return IsInside(absolute, workspaceRoot)
                ? Path.GetRelativePath(workspaceRoot, absolute).Replace('\\', '/')
                : Path.GetFileName(absolute);
        }
        catch
        {
            return Path.GetFileName(value);
        }
    }
}
