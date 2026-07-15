using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace OurPlanCore;

public static partial class PlanSwiftProjectImporter
{
    private static string EnsureRelativeFolder(string root, string relativePath)
    {
        string current = root;
        foreach (string rawSegment in SplitRelativePath(relativePath))
        {
            string segment = PlanSwiftXml.DecodeName(rawSegment);
            current = OurPlanCoreJobStore.EnsureFolder(current, segment);
        }

        return current;
    }

    private static string EnsureImportRootFolder(string root, string requestedName, bool isTakeoffRoot)
    {
        string name = string.IsNullOrWhiteSpace(requestedName)
            ? PlanSwiftImportOptions.DefaultCurrentJobImportFolderName
            : requestedName.Trim();
        string folder = OurPlanCoreJobStore.EnsureFolder(root, name);
        if (isTakeoffRoot)
            OurPlanCoreJobStore.SetProperty(folder, "SmartNodeKind", "folder");
        return folder;
    }

    private static IReadOnlyList<string> SplitRelativePath(string relativePath) =>
        string.IsNullOrWhiteSpace(relativePath)
            ? []
            : relativePath
                .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
                .Where(segment => segment != ".")
                .ToList();

    private static string NormalizeImportRelativePath(string relativePath) =>
        string.Join(Path.DirectorySeparatorChar, SplitRelativePath(relativePath));

    private static string UniqueChildDisplayName(string parent, string requestedName)
    {
        string clean = PlanSwiftXml.DecodeName(requestedName);
        string sanitized = OurPlanCoreJobStore.SanitizeName(clean, 120);
        if (!Directory.Exists(Path.Combine(parent, sanitized)))
            return clean;

        for (int i = 2; ; i++)
        {
            string candidate = $"{clean} ({i})";
            if (!Directory.Exists(Path.Combine(parent, OurPlanCoreJobStore.SanitizeName(candidate, 120))))
                return candidate;
        }
    }

    private static string ResolveDestinationJobName(
        PlanSwiftImportOptions options,
        PlanSwiftProjectManifest manifest)
    {
        string baseName = string.IsNullOrWhiteSpace(options.DestinationJobName)
            ? $"{manifest.JobName} - imported"
            : options.DestinationJobName.Trim();
        string clean = OurPlanCoreJobStore.SanitizeName(baseName, 120);
        if (!Directory.Exists(Path.Combine(options.DestinationParentPath, clean)))
            return clean;

        for (int i = 2; ; i++)
        {
            string candidate = OurPlanCoreJobStore.SanitizeName($"{baseName} ({i})", 120);
            if (!Directory.Exists(Path.Combine(options.DestinationParentPath, candidate)))
                return candidate;
        }
    }

    private static IEnumerable<T> Limit<T>(IReadOnlyList<T> source, int max) =>
        max > 0 ? source.Take(max) : source;

    private static void ValidateOptions(PlanSwiftImportOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SourceJobPath))
            throw new ArgumentException("Source PlanSwift job path is required.", nameof(options));
        if (!Directory.Exists(options.SourceJobPath))
            throw new DirectoryNotFoundException(options.SourceJobPath);
        if (options.ImportIntoExistingJob)
        {
            if (!Directory.Exists(options.DestinationJobPath))
                throw new DirectoryNotFoundException(options.DestinationJobPath);
            return;
        }

        if (string.IsNullOrWhiteSpace(options.DestinationParentPath))
            throw new ArgumentException("Destination parent path is required.", nameof(options));

        JobWriteAccess.Demand(options.DestinationParentPath, "create a PlanSwift import destination");
        Directory.CreateDirectory(options.DestinationParentPath);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                JobWriteAccess.Demand(path, "remove an incomplete PlanSwift import");
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed record ImportedPlanSwiftPage(
        PageInfo Page,
        PlanSwiftPageNormalization Normalization);
}
