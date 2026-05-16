using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace OurPlaneCore;

public static class BundledToolPathResolver
{
    private const int MaxExtractSearchDepth = 5;

    public static string ResolveFile(string relativePath, IEnumerable<string>? extraCandidates = null)
    {
        foreach (string candidate in EnumerateDirectCandidates(relativePath, extraCandidates))
        {
            string full = TryFullPath(candidate);
            if (full.Length > 0 && File.Exists(full))
                return full;
        }

        foreach (string root in EnumerateBundleExtractRoots())
        {
            string match = FindUnderRoot(root, relativePath);
            if (match.Length > 0)
                return match;
        }

        return "";
    }

    private static IEnumerable<string> EnumerateDirectCandidates(
        string relativePath,
        IEnumerable<string>? extraCandidates)
    {
        string baseDirectory = AppContext.BaseDirectory;
        string? executableDirectory = Path.GetDirectoryName(Environment.ProcessPath);
        var candidates = new List<string> { relativePath };
        if (extraCandidates != null)
            candidates.AddRange(extraCandidates);

        foreach (string candidate in candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate)))
        {
            if (Path.IsPathRooted(candidate))
            {
                yield return candidate;
                continue;
            }

            yield return Path.Combine(baseDirectory, candidate);
            if (!string.IsNullOrWhiteSpace(executableDirectory))
                yield return Path.Combine(executableDirectory, candidate);
        }
    }

    private static IEnumerable<string> EnumerateBundleExtractRoots()
    {
        string? configuredRoot = Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR");
        if (!string.IsNullOrWhiteSpace(configuredRoot) && Directory.Exists(configuredRoot))
            yield return configuredRoot;

        string tempNetRoot = Path.Combine(Path.GetTempPath(), ".net");
        if (!Directory.Exists(tempNetRoot))
            yield break;

        string processName = Path.GetFileNameWithoutExtension(
            Environment.ProcessPath ??
            Process.GetCurrentProcess().ProcessName ??
            "ourplanecore");
        string appRoot = Path.Combine(tempNetRoot, processName);
        if (Directory.Exists(appRoot))
            yield return appRoot;

        foreach (string directory in SafeEnumerateDirectories(tempNetRoot)
                     .OrderByDescending(SafeLastWriteUtc)
                     .Take(20))
        {
            yield return directory;
        }
    }

    private static string FindUnderRoot(string root, string relativePath)
    {
        string direct = TryFullPath(Path.Combine(root, relativePath));
        if (direct.Length > 0 && File.Exists(direct))
            return direct;

        string fileName = Path.GetFileName(relativePath);
        string normalizedRelative = NormalizeForSuffix(relativePath);
        foreach (string match in SafeEnumerateFiles(root, fileName, MaxExtractSearchDepth))
        {
            string normalizedMatch = NormalizeForSuffix(match);
            if (normalizedMatch.EndsWith(normalizedRelative, StringComparison.OrdinalIgnoreCase))
                return match;
        }

        return "";
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root, string fileName, int maxDepth)
    {
        var pending = new Stack<(string Directory, int Depth)>();
        pending.Push((root, 0));
        while (pending.Count > 0)
        {
            (string directory, int depth) = pending.Pop();
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, fileName);
            }
            catch
            {
                continue;
            }

            foreach (string file in files)
                yield return file;

            if (depth >= maxDepth)
                continue;

            foreach (string child in SafeEnumerateDirectories(directory))
                pending.Push((child, depth + 1));
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root).ToList();
        }
        catch
        {
            return [];
        }
    }

    private static DateTime SafeLastWriteUtc(string path)
    {
        try
        {
            return Directory.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static string TryFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return "";
        }
    }

    private static string NormalizeForSuffix(string path) =>
        path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
}
