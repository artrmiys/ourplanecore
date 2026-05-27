using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OurPlaneCore;

public static class PdfImportSourceFinder
{
    public static IReadOnlyList<string> FindPdfFilesRecursive(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return [];

        string root = Path.GetFullPath(folderPath);
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive,
                AttributesToSkip = 0,
            };

            return Directory.EnumerateFiles(root, "*.pdf", options)
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => Path.GetRelativePath(root, path), StringComparer.OrdinalIgnoreCase)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn(ex, $"Find PDF files failed for {root}");
            return [];
        }
    }
}
