using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OurPlanCore;

/// <summary>
/// One-way, non-destructive migration from the pre-rename application roots.
/// Existing destination files always win and disposable render caches are not
/// copied.
/// </summary>
public static class AppDataMigration
{
    internal const string MarkerFileName = ".app-identity-migration-v1";

    private static readonly string[] CriticalRoamingFiles =
    [
        "settings.json",
        "templates.json",
    ];

    private static readonly string[] CriticalLocalDirectories =
    [
        "presets",
    ];

    private static readonly string[] DeferredRoamingDirectories =
    [
        "thumbnails",
    ];

    private static readonly string[] DeferredLocalFiles =
    [
        "global_ai_index.jsonl",
    ];

    private static readonly string[] DeferredLocalDirectories =
    [
        "learning",
    ];

    /// <summary>
    /// Runs the complete migration. This entry point is primarily useful for
    /// tests and maintenance tools; application startup uses the split phases.
    /// </summary>
    public static void Run()
    {
        TryRun(includeDeferred: true);
    }

    /// <summary>
    /// Copies the small settings and preset files needed by the first window.
    /// </summary>
    public static void RunCritical()
    {
        TryRun(includeDeferred: false);
    }

    /// <summary>
    /// Copies larger thumbnails and learning data after the app is responsive.
    /// </summary>
    public static void RunDeferred()
    {
        TryRun(includeDeferred: true);
    }

    internal static void RunForRoots(string roamingBase, string localBase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roamingBase);
        ArgumentException.ThrowIfNullOrWhiteSpace(localBase);

        RunCore(
            AppIdentity.ProductRoamingRoot(AppIdentity.LegacyProductName, roamingBase),
            AppIdentity.ProductRoamingRoot(AppIdentity.ProductName, roamingBase),
            AppIdentity.ProductLocalRoot(AppIdentity.LegacyProductName, localBase),
            AppIdentity.ProductLocalRoot(AppIdentity.ProductName, localBase),
            includeDeferred: true);
    }

    internal static void RunCriticalForRoots(string roamingBase, string localBase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roamingBase);
        ArgumentException.ThrowIfNullOrWhiteSpace(localBase);

        RunCore(
            AppIdentity.ProductRoamingRoot(AppIdentity.LegacyProductName, roamingBase),
            AppIdentity.ProductRoamingRoot(AppIdentity.ProductName, roamingBase),
            AppIdentity.ProductLocalRoot(AppIdentity.LegacyProductName, localBase),
            AppIdentity.ProductLocalRoot(AppIdentity.ProductName, localBase),
            includeDeferred: false);
    }

    internal static bool ShouldProtectLegacySettingsSave(string settingsPath)
    {
        if (!PathsEqual(settingsPath, Path.Combine(AppIdentity.RoamingRoot, "settings.json")))
            return false;

        return IsLegacySettingsSaveProtectionRequired(
            settingsPath,
            Path.Combine(AppIdentity.LegacyRoamingRoot, "settings.json"),
            Path.Combine(AppIdentity.LocalRoot, MarkerFileName));
    }

    internal static bool IsLegacySettingsSaveProtectionRequired(
        string currentSettingsPath,
        string legacySettingsPath,
        string markerPath) =>
        !File.Exists(currentSettingsPath) &&
        File.Exists(legacySettingsPath) &&
        !File.Exists(markerPath);

    private static void TryRun(bool includeDeferred)
    {
        if (AppIdentity.IsIsolatedPreview) return;
        try
        {
            RunCore(
                AppIdentity.LegacyRoamingRoot,
                AppIdentity.RoamingRoot,
                AppIdentity.LegacyLocalRoot,
                AppIdentity.LocalRoot,
                includeDeferred);
        }
        catch
        {
            // Startup must never fail because legacy data is inaccessible.
            // Without a marker the idempotent migration is retried next time.
        }
    }

    private static void RunCore(
        string sourceRoaming,
        string destinationRoaming,
        string sourceLocal,
        string destinationLocal,
        bool includeDeferred)
    {
        string markerPath = Path.Combine(destinationLocal, MarkerFileName);
        if (File.Exists(markerPath))
            return;

        bool sourceExists = Directory.Exists(sourceRoaming) || Directory.Exists(sourceLocal);
        if (!sourceExists)
            return;

        bool succeeded = true;
        foreach (string fileName in CriticalRoamingFiles)
        {
            string source = Path.Combine(sourceRoaming, fileName);
            string destination = Path.Combine(destinationRoaming, fileName);
            succeeded &= string.Equals(fileName, "settings.json", StringComparison.OrdinalIgnoreCase)
                ? TryCopySettings(source, destination, sourceRoaming, destinationRoaming)
                : TryCopyFileMissing(source, destination);
        }

        foreach (string directoryName in CriticalLocalDirectories)
        {
            succeeded &= TryCopyDirectoryMissing(
                Path.Combine(sourceLocal, directoryName),
                Path.Combine(destinationLocal, directoryName));
        }

        if (!includeDeferred)
            return;

        foreach (string directoryName in DeferredRoamingDirectories)
        {
            succeeded &= TryCopyDirectoryMissing(
                Path.Combine(sourceRoaming, directoryName),
                Path.Combine(destinationRoaming, directoryName));
        }

        foreach (string fileName in DeferredLocalFiles)
        {
            succeeded &= TryCopyFileMissing(
                Path.Combine(sourceLocal, fileName),
                Path.Combine(destinationLocal, fileName));
        }

        foreach (string directoryName in DeferredLocalDirectories)
        {
            succeeded &= TryCopyDirectoryMissing(
                Path.Combine(sourceLocal, directoryName),
                Path.Combine(destinationLocal, directoryName));
        }

        if (succeeded)
            TryWriteMarker(markerPath);
    }

    private static bool TryCopySettings(
        string source,
        string destination,
        string sourceRoaming,
        string destinationRoaming)
    {
        if (!File.Exists(source) || File.Exists(destination))
            return true;

        try
        {
            string json = File.ReadAllText(source);
            string normalized = NormalizeRecentJobThumbnailPaths(json, sourceRoaming, destinationRoaming);
            string? destinationDirectory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            return TryCreateFileMissingAtomic(destination, stream =>
            {
                using var writer = new StreamWriter(
                    stream,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    bufferSize: 4096,
                    leaveOpen: true);
                writer.Write(normalized);
                writer.Flush();
            });
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeRecentJobThumbnailPaths(
        string json,
        string sourceRoaming,
        string destinationRoaming)
    {
        try
        {
            JsonNode? root = JsonNode.Parse(json);
            if (root?["RecentJobs"] is not JsonArray recentJobs)
                return json;

            string sourcePrefix = Path.GetFullPath(sourceRoaming)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string destinationPrefix = Path.GetFullPath(destinationRoaming)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            foreach (JsonNode? node in recentJobs)
            {
                if (node is not JsonObject recent ||
                    recent["ThumbnailPath"]?.GetValue<string>() is not { Length: > 0 } thumbnailPath ||
                    !Path.IsPathFullyQualified(thumbnailPath))
                {
                    continue;
                }

                string fullThumbnailPath = Path.GetFullPath(thumbnailPath);
                if (!IsSameOrDescendant(sourcePrefix, fullThumbnailPath))
                    continue;

                string relative = Path.GetRelativePath(sourcePrefix, fullThumbnailPath);
                recent["ThumbnailPath"] = Path.Combine(destinationPrefix, relative);
            }

            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            // A malformed or hand-edited settings file should still be copied
            // byte-for-byte; AppSettingsStore handles malformed JSON on load.
            return json;
        }
    }

    private static bool TryCopyDirectoryMissing(string source, string destination)
    {
        if (!Directory.Exists(source))
            return true;

        bool succeeded = true;
        try
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.EnumerateFiles(source))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    continue;

                succeeded &= TryCopyFileMissing(file, Path.Combine(destination, Path.GetFileName(file)));
            }

            foreach (string directory in Directory.EnumerateDirectories(source))
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    continue;

                succeeded &= TryCopyDirectoryMissing(
                    directory,
                    Path.Combine(destination, Path.GetFileName(directory)));
            }
        }
        catch
        {
            return false;
        }

        return succeeded;
    }

    private static bool TryCopyFileMissing(string source, string destination)
    {
        if (!File.Exists(source) || File.Exists(destination))
            return true;

        try
        {
            return TryCreateFileMissingAtomic(destination, destinationStream =>
            {
                using var sourceStream = new FileStream(
                    source,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 81920,
                    FileOptions.SequentialScan);
                sourceStream.CopyTo(destinationStream);
            });
        }
        catch
        {
            return false;
        }
    }

    private static void TryWriteMarker(string markerPath)
    {
        try
        {
            _ = TryCreateFileMissingAtomic(markerPath, stream =>
            {
                using var writer = new StreamWriter(
                    stream,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    bufferSize: 1024,
                    leaveOpen: true);
                writer.WriteLine(DateTime.UtcNow.ToString("O"));
                writer.Flush();
            });
        }
        catch
        {
            // Marker failure only means the safe destination-wins migration may
            // be attempted again on the next launch.
        }
    }

    private static bool TryCreateFileMissingAtomic(string destination, Action<FileStream> write)
    {
        if (File.Exists(destination))
            return true;

        string? destinationDirectory = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
            return false;

        Directory.CreateDirectory(destinationDirectory);
        string temporaryPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.migration.tmp");

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 81920,
                       FileOptions.WriteThrough))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, destination, overwrite: false);
            return true;
        }
        catch (IOException) when (File.Exists(destination))
        {
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch
            {
            }
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSameOrDescendant(string root, string candidate)
    {
        if (string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase))
            return true;

        string prefix = root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
