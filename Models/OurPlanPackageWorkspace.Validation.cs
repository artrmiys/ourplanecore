using System.IO;

namespace OurPlanCore;

public static partial class OurPlanPackageWorkspace
{
    internal static void ValidateWorkspaceForOpen(
        string workspaceRoot,
        OurPlanPackageManifest? manifest,
        bool requireExactManifestFiles)
    {
        ValidateCanonicalLayout(workspaceRoot);
        IReadOnlyList<OurPlanPackageSourceFile> files =
            OurPlanPackageFileSelector.Collect(workspaceRoot);
        if (requireExactManifestFiles)
        {
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));
            var selectedPaths = files
                .Select(file => file.LogicalPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var manifestPaths = manifest.Files
                .Select(file => file.Path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!selectedPaths.SetEquals(manifestPaths))
            {
                string unexpected = string.Join(
                    ", ",
                    manifestPaths.Except(selectedPaths, StringComparer.OrdinalIgnoreCase).Take(6));
                string missing = string.Join(
                    ", ",
                    selectedPaths.Except(manifestPaths, StringComparer.OrdinalIgnoreCase).Take(6));
                throw new OurPlanPackageValidationException(
                    "The package contains non-durable or unexpected project entries." +
                    (string.IsNullOrWhiteSpace(unexpected) ? "" : $" Rejected: {unexpected}.") +
                    (string.IsNullOrWhiteSpace(missing) ? "" : $" Missing from manifest: {missing}."));
            }
        }

        OurPlanPackageSemanticValidator.Validate(files);
        OurPlanPackagePortability.ValidateExtractedReferences(workspaceRoot);
    }

    private static void ValidateCanonicalLayout(string workspaceRoot)
    {
        string pagesData = Path.Combine(workspaceRoot, "Pages", "Data.xml");
        string takeoffsData = Path.Combine(workspaceRoot, "Takeoffs", "Data.xml");
        if (!File.Exists(pagesData) || !File.Exists(takeoffsData))
        {
            throw new OurPlanPackageValidationException(
                "The package is missing its canonical Pages or Takeoffs project folders.");
        }
    }
}
