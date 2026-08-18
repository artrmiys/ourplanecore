using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OurPlanCore;

internal static partial class OurPlanPackagePortability
{
    private const long MaxDomRewriteBytes = 64L * 1024 * 1024;
    private static readonly HashSet<string> PageMetadataFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "source.json",
        "source_pdf.json",
        "layers.json",
        "sheet_overlays.json",
        "annotations.json",
    };

    private static readonly HashSet<string> PathPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "pdf",
        "pdf_path",
        "overlay_page_folder",
        "source_page_folder",
        "image",
        "overview_image",
        "snap_index",
    };

    public static void NormalizeInternalReferences(string workspaceRoot) =>
        RebaseInternalReferences(workspaceRoot, workspaceRoot);

    public static void RebaseInternalReferences(string sourceRoot, string destinationRoot)
    {
        string source = NormalizeRoot(sourceRoot);
        string destination = NormalizeRoot(destinationRoot);
        if (!Directory.Exists(destination))
            return;

        foreach (string path in Directory.EnumerateFiles(
                     destination,
                     "*.json",
                     new EnumerationOptions
                     {
                         RecurseSubdirectories = true,
                         AttributesToSkip = FileAttributes.ReparsePoint,
                         IgnoreInaccessible = false,
                     }).Where(path =>
                         IsPortableMetadataFile(path) &&
                         !IsExcludedWorkspacePath(destination, path)))
        {
            NormalizeMetadataFile(path, source, destination);
        }
    }

    public static void ValidateExtractedReferences(string workspaceRoot)
    {
        string root = NormalizeRoot(workspaceRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Extracted project workspace not found: {root}");

        foreach (string path in MetadataFiles(root))
            ValidateMetadataReferences(path, root);

        string observations = Path.Combine(root, "AI_Context", "observations.jsonl");
        if (File.Exists(observations))
            ValidateObservationIdentifiers(observations);
    }

    public static PortableSourceSet CreatePortableSourceSet(
        string workspaceRoot,
        IReadOnlyList<OurPlanPackageSourceFile> files)
    {
        string root = NormalizeRoot(workspaceRoot);
        string stagingParent = Path.Combine(AppIdentity.LocalRoot, "package-normalization");
        PruneStaleNormalizationStaging(stagingParent);
        string stagingRoot = Path.Combine(stagingParent, Guid.NewGuid().ToString("N"));
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (OurPlanPackageSourceFile file in files)
            {
                if (TryCreatePortableProvenanceCopy(file, root, stagingRoot, out string? provenancePath))
                {
                    overrides[file.LogicalPath] = provenancePath!;
                    continue;
                }
                if (!IsPortableMetadataFile(file.FullPath))
                    continue;
                if (!MetadataNeedsNormalization(file.FullPath, root, root))
                    continue;
                JsonNode document = ParseMetadataDocument(file.FullPath);
                bool changed = NormalizeNode(
                    document,
                    Path.GetDirectoryName(file.FullPath)!,
                    root,
                    root,
                    file.FullPath,
                    writeChanges: true);
                if (!changed)
                    continue;

                string stagedPath = Path.Combine(
                    stagingRoot,
                    file.LogicalPath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
                File.WriteAllText(
                    stagedPath,
                    document.ToJsonString(OurPlanCoreJobStore.JsonOptions));
                overrides[file.LogicalPath] = stagedPath;
            }
            return new PortableSourceSet(stagingRoot, overrides);
        }
        catch
        {
            TryDeleteStaging(stagingRoot, stagingParent);
            throw;
        }
    }

    private static void NormalizeMetadataFile(
        string metadataPath,
        string sourceRoot,
        string destinationRoot) =>
        ProcessMetadataFile(metadataPath, sourceRoot, destinationRoot, writeChanges: true);

    private static void ProcessMetadataFile(
        string metadataPath,
        string sourceRoot,
        string destinationRoot,
        bool writeChanges)
    {
        if (!MetadataNeedsNormalization(metadataPath, sourceRoot, destinationRoot))
            return;
        JsonNode root = ParseMetadataDocument(metadataPath);

        string metadataFolder = Path.GetDirectoryName(metadataPath)!;
        bool changed = NormalizeNode(
            root,
            metadataFolder,
            sourceRoot,
            destinationRoot,
            metadataPath,
            writeChanges);
        if (!writeChanges || !changed)
            return;

        JobWriteAccess.Demand(metadataPath, "normalize portable project paths");
        IoUtil.WriteAllTextAtomic(
            metadataPath,
            root.ToJsonString(OurPlanCoreJobStore.JsonOptions));
    }

    private static JsonNode ParseMetadataDocument(string metadataPath)
    {
        try
        {
            var info = new FileInfo(metadataPath);
            if (info.Length > MaxDomRewriteBytes)
            {
                throw new OurPlanPackageValidationException(
                    $"Project metadata '{metadataPath}' needs path normalization but exceeds the " +
                    $"safe {MaxDomRewriteBytes / (1024 * 1024)} MB rewrite limit.");
            }
            using var stream = new FileStream(
                metadataPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                256 * 1024,
                FileOptions.SequentialScan);
            return JsonNode.Parse(stream)
                ?? throw new JsonException("The metadata document is empty.");
        }
        catch (JsonException ex)
        {
            throw new OurPlanPackageValidationException(
                $"Cannot make malformed project metadata portable: {metadataPath}. {ex.Message}",
                ex);
        }
    }

    private static bool MetadataNeedsNormalization(
        string metadataPath,
        string sourceRoot,
        string destinationRoot)
    {
        string metadataFolder = Path.GetDirectoryName(metadataPath)!;
        string? pendingProperty = null;
        bool inContextCropPaths = false;
        bool needsNormalization = false;
        BoundedJsonStream.InspectDocument(
            metadataPath,
            (token, value, _) =>
            {
                if (token == JsonTokenType.PropertyName)
                {
                    pendingProperty = value;
                    return;
                }
                if (inContextCropPaths)
                {
                    if (token == JsonTokenType.EndArray)
                    {
                        inContextCropPaths = false;
                        return;
                    }
                    if (token == JsonTokenType.Null)
                        return;
                    if (token != JsonTokenType.String)
                    {
                        throw new OurPlanPackageValidationException(
                            $"Project metadata '{metadataPath}' has a non-text context crop path.");
                    }
                    if (string.IsNullOrWhiteSpace(value))
                        return;
                    string contextPortable = PortablePath(
                        value!,
                        metadataFolder,
                        sourceRoot,
                        destinationRoot,
                        metadataPath,
                        "context_crop_paths");
                    needsNormalization |= !contextPortable.Equals(value, StringComparison.Ordinal);
                    return;
                }
                if (pendingProperty == null)
                    return;

                string property = pendingProperty;
                pendingProperty = null;
                if (IsAiPortableMetadata(metadataPath) &&
                    property.Equals("context_crop_paths", StringComparison.OrdinalIgnoreCase))
                {
                    if (token != JsonTokenType.StartArray)
                    {
                        throw new OurPlanPackageValidationException(
                            $"Project metadata '{metadataPath}' has an invalid context crop path list.");
                    }
                    inContextCropPaths = true;
                    return;
                }
                if (!IsPortablePathProperty(metadataPath, property))
                    return;
                if (token == JsonTokenType.Null)
                    return;
                if (token != JsonTokenType.String)
                {
                    throw new OurPlanPackageValidationException(
                        $"Project metadata '{metadataPath}' has a non-text '{property}' path.");
                }
                if (string.IsNullOrWhiteSpace(value))
                    return;
                string portable = IsThreeDModel(metadataPath) &&
                                  property.Equals("TakeoffFolder", StringComparison.OrdinalIgnoreCase)
                    ? PortableCompositePath(
                        value!,
                        metadataFolder,
                        sourceRoot,
                        destinationRoot,
                        metadataPath,
                        property)
                    : PortablePath(
                        value!,
                        metadataFolder,
                        sourceRoot,
                        destinationRoot,
                        metadataPath,
                        property);
                if (IsThreeDModel(metadataPath))
                    portable = portable.Replace(Path.DirectorySeparatorChar, '/');
                needsNormalization |= !portable.Equals(value, StringComparison.Ordinal);
            });
        return needsNormalization;
    }

    private static void ValidateMetadataReferences(string metadataPath, string root)
    {
        string metadataFolder = Path.GetDirectoryName(metadataPath)!;
        string? pendingProperty = null;
        bool inContextCropPaths = false;
        var rootStrings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        BoundedJsonStream.InspectDocument(
            metadataPath,
            (token, value, depth) =>
            {
                if (token == JsonTokenType.PropertyName)
                {
                    pendingProperty = value;
                    return;
                }
                if (inContextCropPaths)
                {
                    if (token == JsonTokenType.EndArray)
                    {
                        inContextCropPaths = false;
                        return;
                    }
                    if (token == JsonTokenType.Null)
                        return;
                    if (token != JsonTokenType.String)
                    {
                        throw new OurPlanPackageValidationException(
                            $"Project metadata '{metadataPath}' has a non-text context crop path.");
                    }
                    if (string.IsNullOrWhiteSpace(value))
                        return;
                    _ = PortablePath(
                        value!,
                        metadataFolder,
                        root,
                        root,
                        metadataPath,
                        "context_crop_paths");
                    return;
                }
                if (pendingProperty == null)
                    return;

                string property = pendingProperty;
                pendingProperty = null;
                if (depth == 1 && token is JsonTokenType.String or JsonTokenType.Null)
                    rootStrings[property] = value;
                if (IsAiPortableMetadata(metadataPath) &&
                    property.Equals("context_crop_paths", StringComparison.OrdinalIgnoreCase))
                {
                    if (token != JsonTokenType.StartArray)
                    {
                        throw new OurPlanPackageValidationException(
                            $"Project metadata '{metadataPath}' has an invalid context crop path list.");
                    }
                    inContextCropPaths = true;
                    return;
                }
                if (!IsPortablePathProperty(metadataPath, property))
                    return;
                if (token == JsonTokenType.Null)
                    return;
                if (token != JsonTokenType.String)
                {
                    throw new OurPlanPackageValidationException(
                        $"Project metadata '{metadataPath}' has a non-text '{property}' path.");
                }
                if (string.IsNullOrWhiteSpace(value))
                    return;

                if (IsThreeDModel(metadataPath) &&
                    property.Equals("TakeoffFolder", StringComparison.OrdinalIgnoreCase))
                {
                    _ = PortableCompositePath(
                        value!,
                        metadataFolder,
                        root,
                        root,
                        metadataPath,
                        property);
                }
                else
                {
                    _ = PortablePath(
                        value!,
                        metadataFolder,
                        root,
                        root,
                        metadataPath,
                        property);
                }
            });
        ValidateAiFileIdentifiers(metadataPath, rootStrings);
    }

    private static bool NormalizeNode(
        JsonNode node,
        string metadataFolder,
        string sourceRoot,
        string destinationRoot,
        string metadataPath,
        bool writeChanges)
    {
        bool changed = false;
        if (node is JsonObject obj)
        {
            bool threeDModel = IsThreeDModel(metadataPath);
            string oldTakeoff = threeDModel ? ReadString(obj, "TakeoffFolder") : "";
            string oldPage = threeDModel ? ReadString(obj, "PageFolder") : "";
            string oldGroup = threeDModel ? ReadString(obj, "GroupKey") : "";
            foreach ((string propertyName, JsonNode? value) in obj.ToList())
            {
                if (IsAiPortableMetadata(metadataPath) &&
                    propertyName.Equals("context_crop_paths", StringComparison.OrdinalIgnoreCase) &&
                    value is JsonArray contextPaths)
                {
                    for (int index = 0; index < contextPaths.Count; index++)
                    {
                        if (contextPaths[index] is null)
                            continue;
                        if (contextPaths[index] is not JsonValue pathScalar ||
                            !pathScalar.TryGetValue(out string? contextPath))
                        {
                            throw new OurPlanPackageValidationException(
                                $"Project metadata '{metadataPath}' has a non-text context crop path.");
                        }
                        if (string.IsNullOrWhiteSpace(contextPath))
                            continue;
                        string portable = PortablePath(
                            contextPath,
                            metadataFolder,
                            sourceRoot,
                            destinationRoot,
                            metadataPath,
                            propertyName);
                        if (writeChanges && !portable.Equals(contextPath, StringComparison.Ordinal))
                        {
                            contextPaths[index] = portable;
                            changed = true;
                        }
                    }
                    continue;
                }

                if (value is JsonValue scalar &&
                    IsPortablePathProperty(metadataPath, propertyName) &&
                    scalar.TryGetValue(out string? pathValue) &&
                    !string.IsNullOrWhiteSpace(pathValue))
                {
                    string portable = threeDModel &&
                                      propertyName.Equals("TakeoffFolder", StringComparison.OrdinalIgnoreCase)
                        ? PortableCompositePath(
                            pathValue,
                            metadataFolder,
                            sourceRoot,
                            destinationRoot,
                            metadataPath,
                            propertyName)
                        : PortablePath(
                            pathValue,
                            metadataFolder,
                            sourceRoot,
                            destinationRoot,
                            metadataPath,
                            propertyName);
                    if (threeDModel)
                        portable = portable.Replace(Path.DirectorySeparatorChar, '/');
                    if (writeChanges && !portable.Equals(pathValue, StringComparison.Ordinal))
                    {
                        obj[propertyName] = portable;
                        changed = true;
                    }
                    continue;
                }

                if (value != null)
                {
                    changed |= NormalizeNode(
                        value,
                        metadataFolder,
                        sourceRoot,
                        destinationRoot,
                        metadataPath,
                        writeChanges);
                }
            }

            if (writeChanges && threeDModel && !string.IsNullOrWhiteSpace(oldGroup))
            {
                string rebasedGroup = ThreeDModelPathPortability.RebaseKnownGroupKey(
                    oldGroup,
                    oldTakeoff,
                    ReadString(obj, "TakeoffFolder"),
                    oldPage,
                    ReadString(obj, "PageFolder"));
                if (!rebasedGroup.Equals(oldGroup, StringComparison.Ordinal))
                {
                    WriteString(obj, "GroupKey", rebasedGroup);
                    changed = true;
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? child in array)
            {
                if (child != null)
                {
                    changed |= NormalizeNode(
                        child,
                        metadataFolder,
                        sourceRoot,
                        destinationRoot,
                        metadataPath,
                        writeChanges);
                }
            }
        }

        return changed;
    }

    private static string PortableCompositePath(
        string value,
        string metadataFolder,
        string sourceRoot,
        string destinationRoot,
        string metadataPath,
        string propertyName) =>
        string.Join(
            "|",
            value.Split('|').Select(part =>
                string.IsNullOrWhiteSpace(part)
                    ? ""
                    : PortablePath(
                        part,
                        metadataFolder,
                        sourceRoot,
                        destinationRoot,
                        metadataPath,
                        propertyName)));

    private static string PortablePath(
        string value,
        string metadataFolder,
        string sourceRoot,
        string destinationRoot,
        string metadataPath,
        string propertyName)
    {
        try
        {
            string referenceFolder = ReferenceFolder(
                metadataPath,
                metadataFolder,
                destinationRoot,
                propertyName);
            string resolved;
            if (Path.IsPathRooted(value))
            {
                string absolute = Path.GetFullPath(value);
                if (IsInside(absolute, sourceRoot))
                {
                    resolved = Path.GetFullPath(Path.Combine(
                        destinationRoot,
                        Path.GetRelativePath(sourceRoot, absolute)));
                }
                else if (IsInside(absolute, destinationRoot))
                {
                    resolved = absolute;
                }
                else
                {
                    throw ExternalReference(metadataPath, propertyName, absolute);
                }
            }
            else
            {
                resolved = Path.GetFullPath(Path.Combine(referenceFolder, value));
            }

            if (!IsInside(resolved, destinationRoot))
                throw ExternalReference(metadataPath, propertyName, resolved);
            return Path.GetRelativePath(referenceFolder, resolved);
        }
        catch (OurPlanPackageValidationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            throw new OurPlanPackageValidationException(
                $"Invalid portable path '{propertyName}' in '{metadataPath}': {ex.Message}",
                ex);
        }
    }

    private static bool IsPortablePathProperty(string metadataPath, string propertyName) =>
        PathPropertyNames.Contains(propertyName) ||
        IsAiPortableMetadata(metadataPath) &&
        (propertyName.Equals("page_folder", StringComparison.OrdinalIgnoreCase) ||
         propertyName.Equals("crop_path", StringComparison.OrdinalIgnoreCase) ||
         propertyName.Equals("layer_manifest_path", StringComparison.OrdinalIgnoreCase) ||
         propertyName.Equals("raw_response_path", StringComparison.OrdinalIgnoreCase) ||
         propertyName.Equals("root_path", StringComparison.OrdinalIgnoreCase)) ||
        Path.GetFileName(metadataPath).Equals("bookmarks.json", StringComparison.OrdinalIgnoreCase) &&
        (propertyName.Equals("page_folder", StringComparison.OrdinalIgnoreCase) ||
         propertyName.Equals("crop_image_path", StringComparison.OrdinalIgnoreCase)) ||
        IsThreeDModel(metadataPath) &&
        (propertyName.Equals("TakeoffFolder", StringComparison.OrdinalIgnoreCase) ||
         propertyName.Equals("PageFolder", StringComparison.OrdinalIgnoreCase)) ||
        Path.GetFileName(metadataPath).Equals("measurements.json", StringComparison.OrdinalIgnoreCase) &&
        propertyName.Equals("page_folder", StringComparison.OrdinalIgnoreCase) ||
        Path.GetFileName(metadataPath).Equals("annotations.json", StringComparison.OrdinalIgnoreCase) &&
        propertyName.Equals("page_folder", StringComparison.OrdinalIgnoreCase);

    private static string ReferenceFolder(
        string metadataPath,
        string metadataFolder,
        string destinationRoot,
        string propertyName) =>
        IsThreeDModel(metadataPath) ||
        Path.GetFileName(metadataPath).Equals("bookmarks.json", StringComparison.OrdinalIgnoreCase) ||
        IsAiPortableMetadata(metadataPath) &&
        (propertyName.Equals("page_folder", StringComparison.OrdinalIgnoreCase) ||
         propertyName.Equals("layer_manifest_path", StringComparison.OrdinalIgnoreCase) ||
         propertyName.Equals("root_path", StringComparison.OrdinalIgnoreCase)) ||
        Path.GetFileName(metadataPath).Equals("measurements.json", StringComparison.OrdinalIgnoreCase) &&
        propertyName.Equals("page_folder", StringComparison.OrdinalIgnoreCase)
            ? destinationRoot
            : IsAiPortableMetadata(metadataPath)
                ? Path.Combine(destinationRoot, "AI_Context")
            : metadataFolder;

    private static IEnumerable<string> MetadataFiles(string root) =>
        Directory.EnumerateFiles(
                root,
                "*.json",
                new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    IgnoreInaccessible = false,
                })
            .Where(path =>
                IsValidatedMetadataFile(path) &&
                !IsExcludedWorkspacePath(root, path));

    private static bool IsPortableMetadataFile(string path) =>
        IsCorePortableMetadataFile(path) ||
        IsAiPortableMetadata(path);

    private static bool IsCorePortableMetadataFile(string path)
    {
        string? relative = ProjectRelativeMetadataPath(path);
        if (string.IsNullOrWhiteSpace(relative))
            return false;
        string[] parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string name = parts[^1];
        if (parts.Length == 1)
            return name.Equals("bookmarks.json", StringComparison.OrdinalIgnoreCase);
        if (parts[0].Equals("Pages", StringComparison.OrdinalIgnoreCase))
        {
            string parent = Path.GetDirectoryName(path)!;
            return parts.Length >= 3 &&
                   PageMetadataFileNames.Contains(name) &&
                   (name.Equals("source.json", StringComparison.OrdinalIgnoreCase) ||
                    File.Exists(Path.Combine(parent, "source.json")));
        }
        if (parts[0].Equals("Takeoffs", StringComparison.OrdinalIgnoreCase))
        {
            return parts.Length >= 3 &&
                   name.Equals("measurements.json", StringComparison.OrdinalIgnoreCase) &&
                   OurPlanCoreJobStore.IsTakeoffItemFolder(Path.GetDirectoryName(path)!);
        }
        return parts.Length == 2 &&
               parts[0].Equals("3D_Context", StringComparison.OrdinalIgnoreCase) &&
               name.Equals("walls_model.json", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ProjectRelativeMetadataPath(string path)
    {
        DirectoryInfo? current = new FileInfo(path).Directory;
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Data.xml")) &&
                Directory.Exists(Path.Combine(current.FullName, "Pages")) &&
                Directory.Exists(Path.Combine(current.FullName, "Takeoffs")))
            {
                return Path.GetRelativePath(current.FullName, path)
                    .Replace(Path.DirectorySeparatorChar, '/');
            }
            current = current.Parent;
        }
        return null;
    }

    private static bool IsValidatedMetadataFile(string path) =>
        IsPortableMetadataFile(path) || AiIdentifierSchemaFor(path) != null;

    private static bool IsAiPortableMetadata(string path)
    {
        string relative = AiContextRelativePath(path);
        if (relative.Equals("project.json", StringComparison.OrdinalIgnoreCase))
            return true;
        string[] segments = relative
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2)
            return false;
        string parent = segments[0].ToLowerInvariant();
        string fileName = segments[1];
        return parent is "requests" or "markers" or "crop_bookmarks" ||
               parent == "responses" &&
               !fileName.EndsWith(".openai.raw.json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsThreeDModel(string metadataPath) =>
        ProjectRelativeMetadataPath(metadataPath)
            ?.Equals("3D_Context/walls_model.json", StringComparison.OrdinalIgnoreCase) == true;

    private static string ReadString(JsonObject obj, string propertyName)
    {
        foreach ((string key, JsonNode? value) in obj)
        {
            if (key.Equals(propertyName, StringComparison.OrdinalIgnoreCase) &&
                value is JsonValue scalar && scalar.TryGetValue(out string? text))
            {
                return text ?? "";
            }
        }
        return "";
    }

    private static void WriteString(JsonObject obj, string propertyName, string value)
    {
        string key = obj.Select(property => property.Key)
            .FirstOrDefault(key => key.Equals(propertyName, StringComparison.OrdinalIgnoreCase)) ??
            propertyName;
        obj[key] = value;
    }

    private static OurPlanPackageValidationException ExternalReference(
        string metadataPath,
        string propertyName,
        string resolved) =>
        new(
            $"Page metadata '{metadataPath}' has an external '{propertyName}' reference: {resolved}. " +
            "Copy the referenced project data inside the job before saving as .ourplan.");

    private static string NormalizeRoot(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsExcludedWorkspacePath(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
        string first = relative.Split('/')[0];
        return first.Equals(".snapshots", StringComparison.OrdinalIgnoreCase) ||
               first.Equals(".undo", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInside(string path, string root)
    {
        string fullPath = Path.GetFullPath(path);
        return fullPath.Equals(root, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteStaging(string stagingRoot, string stagingParent)
    {
        try
        {
            string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stagingRoot));
            string fullParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stagingParent));
            if (Path.GetDirectoryName(fullRoot)?.Equals(fullParent, StringComparison.OrdinalIgnoreCase) == true &&
                Guid.TryParseExact(Path.GetFileName(fullRoot), "N", out _) &&
                Directory.Exists(fullRoot))
            {
                Directory.Delete(fullRoot, recursive: true);
            }
        }
        catch
        {
            // App-owned normalization staging is cleaned again on the next package operation.
        }
    }

    internal sealed class PortableSourceSet : IDisposable
    {
        private readonly string _stagingRoot;
        private readonly IReadOnlyDictionary<string, string> _overrides;

        public PortableSourceSet(
            string stagingRoot,
            IReadOnlyDictionary<string, string> overrides)
        {
            _stagingRoot = stagingRoot;
            _overrides = overrides;
        }

        public string ContentPath(OurPlanPackageSourceFile source) =>
            _overrides.TryGetValue(source.LogicalPath, out string? path)
                ? path
                : source.FullPath;

        public bool IsOverride(OurPlanPackageSourceFile source) =>
            _overrides.ContainsKey(source.LogicalPath);

        public void Dispose() =>
            TryDeleteStaging(
                _stagingRoot,
                Path.Combine(AppIdentity.LocalRoot, "package-normalization"));
    }
}
