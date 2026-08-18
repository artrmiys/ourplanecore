using System.IO;

namespace OurPlanCore;

internal static partial class OurPlanPackagePortability
{
    private static void PruneStaleNormalizationStaging(string stagingParent)
    {
        if (!Directory.Exists(stagingParent))
            return;
        DateTime cutoff = DateTime.UtcNow.AddDays(-2);
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(
                         stagingParent,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                var info = new DirectoryInfo(directory);
                if (!Guid.TryParseExact(info.Name, "N", out _) ||
                    (info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    info.LastWriteTimeUtc > cutoff)
                {
                    continue;
                }
                try
                {
                    Directory.Delete(info.FullName, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    AppLog.Warn(ex, $"Could not remove stale package normalization data '{info.FullName}'.");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn(ex, "Could not scan package normalization staging for cleanup.");
        }
    }
}
