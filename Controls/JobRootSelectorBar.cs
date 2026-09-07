using System;
using System.IO;
using System.Linq;

namespace OurPlanCore.Controls;

public enum JobRootLocationKind
{
    Local,
    Cloud,
    Network,
}

public sealed record JobRootDescriptor(
    string Path,
    string DisplayName,
    string KindLabel,
    string StatusLabel,
    bool Exists);

/// <summary>
/// Static helpers for classifying job-root folders. The old WPF <c>JobRootSelectorBar</c>
/// control was removed when the Open Job dialog moved to inline ring+dot chips
/// (see <c>JobPickerDialog.xaml</c>). These helpers stay because tests and the
/// dialog still call them.
/// </summary>
public static class JobRootSelectorBar
{
    public static JobRootDescriptor DescribeJobRoot(string rootPath)
    {
        string path = NormalizePath(rootPath);
        JobRootLocationKind kind = ClassifyJobRootPath(path);
        bool probeOnDemand = kind == JobRootLocationKind.Network;
        bool exists = probeOnDemand || Directory.Exists(path);
        return new JobRootDescriptor(
            path,
            BuildJobRootDisplayName(path),
            kind.ToString(),
            probeOnDemand ? "Open on demand" : exists ? "Ready" : "Missing",
            exists);
    }

    public static JobRootLocationKind ClassifyJobRootPath(string rootPath)
    {
        string path = rootPath.Trim();
        if (path.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase))
            return JobRootLocationKind.Network;

        string lower = path.ToLowerInvariant();
        string[] cloudTokens =
        [
            "onedrive",
            "dropbox",
            "google drive",
            "googledrive",
            "icloud",
            "sharepoint",
            "box sync",
            "box drive",
        ];
        return cloudTokens.Any(token => lower.Contains(token, StringComparison.OrdinalIgnoreCase))
            ? JobRootLocationKind.Cloud
            : JobRootLocationKind.Local;
    }

    public static string BuildJobRootDisplayName(string rootPath)
    {
        string path = NormalizePath(rootPath);
        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string name = Path.GetFileName(trimmed);
        if (!string.IsNullOrWhiteSpace(name))
            return name;

        string? root = Path.GetPathRoot(path);
        return string.IsNullOrWhiteSpace(root)
            ? path
            : root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
