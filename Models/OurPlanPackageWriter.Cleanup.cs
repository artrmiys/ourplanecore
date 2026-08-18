using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace OurPlanCore;

public static partial class OurPlanPackageWriter
{
    private static readonly TimeSpan StalePackageArtifactAge = TimeSpan.FromDays(2);

    private static void ScavengeStalePackageArtifacts(string targetPath)
    {
        string directory = Path.GetDirectoryName(targetPath) ?? ".";
        string targetName = Path.GetFileName(targetPath);
        DateTime cutoff = DateTime.UtcNow - StalePackageArtifactAge;
        try
        {
            var candidateDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.GetFullPath(directory),
                PublishStagingDirectory(targetPath),
            };
            foreach (string candidateDirectory in candidateDirectories.Where(Directory.Exists))
            foreach (string candidate in Directory.EnumerateFiles(
                         candidateDirectory,
                         $".{targetName}.*.tmp",
                         SearchOption.TopDirectoryOnly))
            {
                if (!TryClassifyOwnedArtifact(candidate, targetName, out bool rollback) ||
                    File.GetLastWriteTimeUtc(candidate) > cutoff ||
                    (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                // A crash rollback may be the only copy of a cloud writer's version.
                // Normal saves delete their verified rollback synchronously; an orphaned
                // rollback has unknown ownership and is never safe to auto-delete.
                if (rollback)
                    continue;
                TryDeleteExclusive(candidate);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn(ex, $"Could not prune stale package artifacts beside '{targetPath}'.");
        }
    }

    private static string PublishStagingDirectory(string targetPath)
    {
        string targetDirectory = Path.GetDirectoryName(targetPath) ?? ".";
        try
        {
            string stagingRoot = Path.Combine(AppIdentity.LocalRoot, "package-publish-staging");
            string targetVolume = Path.GetPathRoot(Path.GetFullPath(targetPath)) ?? "";
            string stagingVolume = Path.GetPathRoot(Path.GetFullPath(stagingRoot)) ?? "";
            if (!string.IsNullOrWhiteSpace(targetVolume) &&
                targetVolume.Equals(stagingVolume, StringComparison.OrdinalIgnoreCase))
            {
                string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                        Path.GetFullPath(targetPath).ToUpperInvariant())))
                    .ToLowerInvariant()[..32];
                return Path.Combine(stagingRoot, identity);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            AppLog.Warn(ex, "Could not resolve local package publish staging; using the destination folder.");
        }
        return Path.GetFullPath(targetDirectory);
    }

    private static void TryDeleteEmptyPublishStaging(string directory)
    {
        try
        {
            string root = Path.GetFullPath(Path.Combine(AppIdentity.LocalRoot, "package-publish-staging"));
            string candidate = Path.GetFullPath(directory);
            if (Path.GetDirectoryName(candidate)?.Equals(root, StringComparison.OrdinalIgnoreCase) == true &&
                Directory.Exists(candidate) &&
                !Directory.EnumerateFileSystemEntries(candidate).Any())
            {
                Directory.Delete(candidate);
            }
        }
        catch
        {
            // Another save or cleanup pass can remove an empty scoped staging folder later.
        }
    }

    private static bool TryClassifyOwnedArtifact(
        string path,
        string targetName,
        out bool rollback)
    {
        rollback = false;
        string name = Path.GetFileName(path);
        string prefix = $".{targetName}.";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string remainder = name[prefix.Length..];
        const string rollbackSuffix = ".rollback.tmp";
        const string tempSuffix = ".tmp";
        string token;
        if (remainder.EndsWith(rollbackSuffix, StringComparison.OrdinalIgnoreCase))
        {
            rollback = true;
            token = remainder[..^rollbackSuffix.Length];
        }
        else if (remainder.EndsWith(tempSuffix, StringComparison.OrdinalIgnoreCase))
        {
            token = remainder[..^tempSuffix.Length];
        }
        else
        {
            return false;
        }
        return Guid.TryParseExact(token, "N", out _);
    }

    private static void TryDeleteExclusive(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.Delete,
                1,
                FileOptions.DeleteOnClose);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn(ex, $"Could not remove stale package artifact '{path}'.");
        }
    }
}
