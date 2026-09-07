using System.IO;
using System.Text.Json;

namespace OurPlanCore;

internal static partial class OurPlanPackagePortability
{
    public static void ValidateRecoveryReferences(string workspaceRoot)
    {
        string root = NormalizeRoot(workspaceRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Recovery project workspace not found: {root}");

        foreach (string path in MetadataFiles(root))
        {
            try
            {
                ValidateMetadataReferences(path, root);
            }
            catch (JsonException ex)
            {
                AppLog.Warn(ex, $"Recovery metadata is partial and will be handled by its store loader: {path}");
            }
        }

        string observations = Path.Combine(root, "AI_Context", "observations.jsonl");
        if (!File.Exists(observations))
            return;
        try
        {
            ValidateObservationIdentifiers(observations);
        }
        catch (JsonException ex)
        {
            AppLog.Warn(ex, $"Recovery observations are partial and will be handled by the store loader: {observations}");
        }
    }
}
