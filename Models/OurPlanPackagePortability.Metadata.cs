using System.IO;

namespace OurPlanCore;

internal static partial class OurPlanPackagePortability
{
    // Only the metadata file's classification is shared for one scan/rewrite.
    // Every actual reference still runs the existing path validation. This is
    // deliberately not a cache of filesystem access or containment decisions.
    private sealed record PortableMetadataContext(
        string Path,
        bool IsAi,
        bool IsThreeD,
        bool IsBookmarks,
        bool IsMeasurements,
        bool IsAnnotations);

    private static PortableMetadataContext ClassifyMetadataFile(string metadataPath)
    {
        string? relative = ProjectRelativeMetadataPath(metadataPath);
        const string aiPrefix = "AI_Context/";
        bool isAi = relative?.StartsWith(aiPrefix, StringComparison.OrdinalIgnoreCase) == true &&
                    IsAiPortableRelativeMetadata(relative[aiPrefix.Length..]);
        string name = Path.GetFileName(metadataPath);
        return new PortableMetadataContext(
            metadataPath,
            isAi,
            relative?.Equals("3D_Context/walls_model.json", StringComparison.OrdinalIgnoreCase) == true,
            name.Equals("bookmarks.json", StringComparison.OrdinalIgnoreCase),
            name.Equals("measurements.json", StringComparison.OrdinalIgnoreCase),
            name.Equals("annotations.json", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPortablePathProperty(PortableMetadataContext metadata, string propertyName) =>
        PathPropertyNames.Contains(propertyName) ||
        metadata.IsAi &&
        (propertyName.Equals("page_folder", StringComparison.OrdinalIgnoreCase) ||
         propertyName.Equals("crop_path", StringComparison.OrdinalIgnoreCase) ||
         propertyName.Equals("layer_manifest_path", StringComparison.OrdinalIgnoreCase) ||
         propertyName.Equals("raw_response_path", StringComparison.OrdinalIgnoreCase) ||
         propertyName.Equals("root_path", StringComparison.OrdinalIgnoreCase)) ||
        metadata.IsBookmarks &&
        (propertyName.Equals("page_folder", StringComparison.OrdinalIgnoreCase) ||
         propertyName.Equals("crop_image_path", StringComparison.OrdinalIgnoreCase)) ||
        metadata.IsThreeD &&
        (propertyName.Equals("TakeoffFolder", StringComparison.OrdinalIgnoreCase) ||
         propertyName.Equals("PageFolder", StringComparison.OrdinalIgnoreCase)) ||
        metadata.IsMeasurements &&
        propertyName.Equals("page_folder", StringComparison.OrdinalIgnoreCase) ||
        metadata.IsAnnotations &&
        propertyName.Equals("page_folder", StringComparison.OrdinalIgnoreCase);

    private static string ReferenceFolder(
        PortableMetadataContext metadata,
        string metadataFolder,
        string destinationRoot,
        string propertyName) =>
        metadata.IsThreeD ||
        metadata.IsBookmarks ||
        metadata.IsAi &&
        (propertyName.Equals("page_folder", StringComparison.OrdinalIgnoreCase) ||
         propertyName.Equals("layer_manifest_path", StringComparison.OrdinalIgnoreCase) ||
         propertyName.Equals("root_path", StringComparison.OrdinalIgnoreCase)) ||
        metadata.IsMeasurements &&
        propertyName.Equals("page_folder", StringComparison.OrdinalIgnoreCase)
            ? destinationRoot
            : metadata.IsAi
                ? Path.Combine(destinationRoot, "AI_Context")
            : metadataFolder;
}
