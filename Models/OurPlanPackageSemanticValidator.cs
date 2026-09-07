using System.IO;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace OurPlanCore;

internal static class OurPlanPackageSemanticValidator
{
    private const long MaxDataXmlBytes = 8L * 1024 * 1024;
    private const long MaxMeasurementsJsonBytes = 32L * 1024 * 1024;
    private const long MaxOtherStructuredDataBytes = 64L * 1024 * 1024;

    private static readonly HashSet<string> PageJsonNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "annotations.json",
        "layers.json",
        "sheet_overlays.json",
        "source.json",
        "source_pdf.json",
        "takeoff_layers.json",
    };

    private static readonly HashSet<string> TakeoffJsonNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "folder_properties.json",
        "measurements.json",
    };

    private static readonly HashSet<string> SettingsJsonNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "beam_annotation.json",
        "excel_macro_export.json",
        "folder_template.json",
        "modules.json",
        "page_sort.json",
        "raster_dpi_presets.json",
        "sheet_metadata.json",
        "takeoff_templates.json",
    };

    private static readonly HashSet<string> LearningJsonNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "learned_rules.json",
        "project_learning_summary.json",
    };

    private static readonly HashSet<string> LearningJsonLineNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "marker_feedback.jsonl",
        "project_reviews.jsonl",
        "sheet_feedback.jsonl",
    };

    private static readonly HashSet<string> AiRecordDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "actions",
        "crop_bookmarks",
        "marker_sets",
        "markers",
        "requests",
    };

    public static void Validate(IReadOnlyList<OurPlanPackageSourceFile> files)
    {
        foreach (OurPlanPackageSourceFile file in files)
        {
            if (IsAuthoritativeDataXml(file.LogicalPath))
            {
                ValidateStructuredFileLength(file.LogicalPath, file.Length);
                ValidateDataXml(file);
            }
            else if (IsAuthoritativeJson(file.LogicalPath) && IsOwnedStructuredLocation(file))
            {
                ValidateStructuredFileLength(file.LogicalPath, file.Length);
                ValidateJson(file);
            }
            else if (IsAuthoritativeJsonLines(file.LogicalPath))
            {
                ValidateStructuredFileLength(file.LogicalPath, file.Length);
                ValidateJsonLines(file);
            }
        }
    }

    // Called as soon as a package manifest is read so hostile declarations are
    // rejected before extraction allocates or writes the structured object.
    internal static void ValidateManifest(IReadOnlyList<OurPlanPackageFileManifest> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        foreach (OurPlanPackageFileManifest file in files)
        {
            if (IsStructuredDataPath(file.Path))
                ValidateStructuredFileLength(file.Path, file.Length);
        }
    }

    internal static void ValidateStructuredFileLength(string logicalPath, long length)
    {
        long limit = Path.GetFileName(logicalPath).Equals(
                "Data.xml",
                StringComparison.OrdinalIgnoreCase)
            ? MaxDataXmlBytes
            : Path.GetFileName(logicalPath).Equals(
                "measurements.json",
                StringComparison.OrdinalIgnoreCase)
                ? MaxMeasurementsJsonBytes
                : MaxOtherStructuredDataBytes;
        if (length < 0 || length > limit)
        {
            throw new OurPlanPackageValidationException(
                $"Project structured data '{logicalPath}' exceeds the safe " +
                $"{limit / (1024 * 1024)} MB limit.");
        }
    }

    private static bool IsStructuredDataPath(string logicalPath) =>
        IsAuthoritativeDataXml(logicalPath) ||
        IsAuthoritativeJson(logicalPath) ||
        IsAuthoritativeJsonLines(logicalPath);

    private static bool IsAuthoritativeDataXml(string logicalPath)
    {
        string[] parts = Segments(logicalPath);
        return parts.Length == 1 &&
               parts[0].Equals("Data.xml", StringComparison.OrdinalIgnoreCase) ||
               parts.Length >= 2 &&
               parts[^1].Equals("Data.xml", StringComparison.OrdinalIgnoreCase) &&
               (parts[0].Equals("Pages", StringComparison.OrdinalIgnoreCase) ||
                parts[0].Equals("Takeoffs", StringComparison.OrdinalIgnoreCase));
    }

    // A package is also a lossless container for user/vendor attachments. Only
    // JSON owned and read as live project state by OurPlanCore is required to
    // parse. Unknown JSON remains opaque and is protected by the manifest hash.
    private static bool IsAuthoritativeJson(string logicalPath)
    {
        string[] parts = Segments(logicalPath);
        if (parts.Length == 0 ||
            !Path.GetExtension(parts[^1]).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (parts.Length == 1)
            return parts[0].Equals("bookmarks.json", StringComparison.OrdinalIgnoreCase);

        string first = parts[0];
        string name = parts[^1];
        if (first.Equals("Pages", StringComparison.OrdinalIgnoreCase))
        {
            return parts.Length >= 3 &&
                   PageJsonNames.Contains(name) &&
                   !IsRebuildableRasterSnap(logicalPath);
        }
        if (first.Equals("Takeoffs", StringComparison.OrdinalIgnoreCase))
            return parts.Length >= 3 && TakeoffJsonNames.Contains(name);
        if (first.Equals("3D_Context", StringComparison.OrdinalIgnoreCase))
        {
            return parts.Length == 2 &&
                   name.Equals("walls_model.json", StringComparison.OrdinalIgnoreCase);
        }
        if (!first.Equals("AI_Context", StringComparison.OrdinalIgnoreCase))
            return false;

        return IsAuthoritativeAiJson(parts, name);
    }

    private static bool IsOwnedStructuredLocation(OurPlanPackageSourceFile file)
    {
        string[] parts = Segments(file.LogicalPath);
        if (parts.Length < 2)
            return true;
        string parent = Path.GetDirectoryName(file.FullPath) ?? "";
        if (parts[0].Equals("Pages", StringComparison.OrdinalIgnoreCase))
        {
            return parts[^1].Equals("source.json", StringComparison.OrdinalIgnoreCase) ||
                   File.Exists(Path.Combine(parent, "source.json"));
        }
        if (!parts[0].Equals("Takeoffs", StringComparison.OrdinalIgnoreCase))
            return true;
        if (parts[^1].Equals("measurements.json", StringComparison.OrdinalIgnoreCase))
            return OurPlanCoreJobStore.IsTakeoffItemFolder(parent);
        return File.Exists(Path.Combine(parent, "Data.xml"));
    }

    private static bool IsAuthoritativeAiJson(string[] parts, string name)
    {
        if (parts.Length == 2)
        {
            return name.Equals("project.json", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("takeoff_rules_used.json", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("sheet_metadata_crop_template.json", StringComparison.OrdinalIgnoreCase);
        }
        if (parts.Length == 3)
        {
            string area = parts[1];
            if (area.Equals("settings", StringComparison.OrdinalIgnoreCase))
                return SettingsJsonNames.Contains(name);
            if (area.Equals("learning", StringComparison.OrdinalIgnoreCase))
                return LearningJsonNames.Contains(name);
            if (area.Equals("exports", StringComparison.OrdinalIgnoreCase))
                return name.Equals("markers_context.json", StringComparison.OrdinalIgnoreCase);
            if (area.Equals("materials", StringComparison.OrdinalIgnoreCase))
                return name.Equals("materials_unique_by_page.json", StringComparison.OrdinalIgnoreCase);
            if (area.Equals("3d_massing", StringComparison.OrdinalIgnoreCase))
                return name.Equals("model.json", StringComparison.OrdinalIgnoreCase);
            if (area.Equals("responses", StringComparison.OrdinalIgnoreCase))
            {
                return !name.EndsWith(".openai.raw.json", StringComparison.OrdinalIgnoreCase);
            }
            return AiRecordDirectories.Contains(area);
        }
        return false;
    }

    private static bool IsAuthoritativeJsonLines(string logicalPath)
    {
        string[] parts = Segments(logicalPath);
        if (parts.Length == 2 &&
            parts[0].Equals("AI_Context", StringComparison.OrdinalIgnoreCase))
        {
            return parts[1].Equals("observations.jsonl", StringComparison.OrdinalIgnoreCase);
        }
        return parts.Length == 3 &&
               parts[0].Equals("AI_Context", StringComparison.OrdinalIgnoreCase) &&
               parts[1].Equals("learning", StringComparison.OrdinalIgnoreCase) &&
               LearningJsonLineNames.Contains(parts[2]);
    }

    private static string[] Segments(string logicalPath) =>
        logicalPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static void ValidateJsonLines(OurPlanPackageSourceFile file)
    {
        try
        {
            BoundedJsonStream.ValidateJsonLines(file.FullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new OurPlanPackageValidationException(
                $"Project data is damaged and was not published: {file.LogicalPath}. {ex.Message}",
                ex);
        }
    }

    private static bool IsRebuildableRasterSnap(string logicalPath)
    {
        string normalized = logicalPath.Replace('\\', '/');
        return normalized.StartsWith("Pages/", StringComparison.OrdinalIgnoreCase) &&
               normalized.EndsWith("/raster/snap.json", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateDataXml(OurPlanPackageSourceFile file)
    {
        try
        {
            using var stream = new FileStream(
                file.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.SequentialScan);
            using XmlReader reader = XmlReader.Create(
                stream,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    IgnoreComments = false,
                    IgnoreWhitespace = false,
                    MaxCharactersInDocument = 64L * 1024 * 1024,
                });
            reader.MoveToContent();
            if (!reader.LocalName.Equals("Item", StringComparison.OrdinalIgnoreCase))
            {
                throw new XmlException("The root element is not Item.");
            }
            while (reader.Read())
            {
                // Stream through the complete document so malformed trailing XML is rejected.
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            throw new OurPlanPackageValidationException(
                $"Project metadata is damaged and was not published: {file.LogicalPath}. {ex.Message}",
                ex);
        }
    }

    private static void ValidateJson(OurPlanPackageSourceFile file)
    {
        try
        {
            BoundedJsonStream.ValidateDocument(file.FullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new OurPlanPackageValidationException(
                $"Project data is damaged and was not published: {file.LogicalPath}. {ex.Message}",
                ex);
        }
    }
}
