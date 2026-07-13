using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OurPlanCore;

public static partial class PlanSwiftProjectImporter
{
    private static PlanSwiftImportResult ImportExistingOurPlanCoreJob(
        PlanSwiftImportOptions options,
        PlanSwiftProjectManifest manifest)
    {
        string sourceRoot = Path.GetFullPath(options.SourceJobPath);
        string destinationParent = Path.GetFullPath(options.DestinationParentPath);
        if (IsSameOrDescendant(sourceRoot, destinationParent))
            throw new InvalidOperationException("Destination cannot be inside the source job folder.");

        string jobName = ResolveDestinationJobName(options, manifest);
        string destinationRoot = Path.Combine(destinationParent, jobName);
        CopyExistingJobDirectory(sourceRoot, destinationRoot);
        OurPlanCoreJobStore.UpdateItemName(destinationRoot, jobName);
        RebaseExistingJobPaths(destinationRoot, sourceRoot);

        OurPlanCoreJob job = OurPlanCoreJobStore.LoadJob(destinationRoot);
        int importedPages = manifest.Pages.Count;
        int importedItems = manifest.TakeoffItems.Count;
        int importedMeasurements = manifest.TakeoffItems.Sum(item => item.Sections.Count);
        var result = new PlanSwiftImportResult(
            manifest.SourceJobPath,
            job.RootPath,
            importedPages,
            importedItems,
            importedMeasurements,
            manifest.Warnings.Count,
            manifest.Warnings,
            manifest.TakeoffFolders.Count);

        WriteReports(job, manifest, result);
        return result;
    }

    private static void CopyExistingJobDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (string file in Directory.EnumerateFiles(sourceDir))
        {
            if (ShouldSkipExistingJobFile(file))
                continue;

            string target = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, target, overwrite: false);
        }

        foreach (string child in Directory.EnumerateDirectories(sourceDir))
        {
            if (ShouldSkipExistingJobDirectory(child))
                continue;

            string target = Path.Combine(destinationDir, Path.GetFileName(child));
            CopyExistingJobDirectory(child, target);
        }
    }

    private static bool ShouldSkipExistingJobFile(string file) =>
        string.Equals(Path.GetFileName(file), ".~lock", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldSkipExistingJobDirectory(string directory)
    {
        string name = Path.GetFileName(directory);
        return string.Equals(name, ".snapshots", StringComparison.OrdinalIgnoreCase);
    }

    private static void RebaseExistingJobPaths(string destinationRoot, string sourceRoot)
    {
        RebaseMeasurementPageFolders(destinationRoot, sourceRoot);
        RebaseSourcePdfMetadata(destinationRoot, sourceRoot);
    }

    private static void RebaseMeasurementPageFolders(string destinationRoot, string sourceRoot)
    {
        string takeoffsRoot = Path.Combine(destinationRoot, "Takeoffs");
        if (!Directory.Exists(takeoffsRoot))
            return;

        foreach (string measurementsPath in Directory.EnumerateFiles(takeoffsRoot, "measurements.json", SearchOption.AllDirectories))
        {
            string folder = Path.GetDirectoryName(measurementsPath) ?? takeoffsRoot;
            List<Measurement> measurements = OurPlanCoreJobStore.LoadMeasurements(folder);
            bool changed = false;
            foreach (Measurement measurement in measurements)
            {
                string rebased = RebasePath(measurement.PageFolder, sourceRoot, destinationRoot);
                if (string.Equals(rebased, measurement.PageFolder, StringComparison.OrdinalIgnoreCase))
                    continue;

                measurement.PageFolder = rebased;
                changed = true;
            }

            if (changed)
                OurPlanCoreJobStore.SaveMeasurements(folder, measurements);
        }
    }

    private static void RebaseSourcePdfMetadata(string destinationRoot, string sourceRoot)
    {
        string pagesRoot = Path.Combine(destinationRoot, "Pages");
        if (!Directory.Exists(pagesRoot))
            return;

        foreach (string metadataPath in Directory.EnumerateFiles(pagesRoot, "source_pdf.json", SearchOption.AllDirectories))
        {
            string folder = Path.GetDirectoryName(metadataPath) ?? pagesRoot;
            PdfSheetMetadata? metadata = OurPlanCoreJobStore.ReadSourcePdfMetadata(folder);
            if (metadata == null)
                continue;

            string rebased = RebasePath(metadata.PdfPath, sourceRoot, destinationRoot);
            if (string.Equals(rebased, metadata.PdfPath, StringComparison.OrdinalIgnoreCase))
                continue;

            metadata.PdfPath = rebased;
            OurPlanCoreJobStore.WriteSourcePdfMetadata(folder, metadata);
        }
    }

    private static string RebasePath(string value, string sourceRoot, string destinationRoot)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        try
        {
            string fullValue = Path.GetFullPath(value);
            string fullSource = Path.GetFullPath(sourceRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!IsSameOrDescendant(fullSource, fullValue))
                return value;

            string relative = Path.GetRelativePath(fullSource, fullValue);
            return Path.GetFullPath(Path.Combine(destinationRoot, relative));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return value;
        }
    }

    private static bool IsSameOrDescendant(string possibleParent, string possibleChild)
    {
        string parent = Path.GetFullPath(possibleParent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string child = Path.GetFullPath(possibleChild)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(parent, child, StringComparison.OrdinalIgnoreCase) ||
            child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
