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
    public static PlanSwiftImportResult Import(PlanSwiftImportOptions options)
    {
        ValidateOptions(options);

        PlanSwiftProjectManifest manifest = PlanSwiftProjectScanner.Scan(options.SourceJobPath);
        if (options.ImportIntoExistingJob)
        {
            if (PlanSwiftSourceFormats.IsOurPlanCore(manifest.SourceFormat))
                throw new InvalidOperationException("Import into the current job supports PlanSwift source job folders only.");

            OurPlanCoreJob existingJob = OurPlanCoreJobStore.LoadJob(options.DestinationJobPath);
            string pageImportRoot = EnsureImportRootFolder(existingJob.PagesRoot, options.ImportRootFolderName, isTakeoffRoot: false);
            string takeoffImportRoot = EnsureImportRootFolder(existingJob.TakeoffsRoot, options.ImportRootFolderName, isTakeoffRoot: true);
            return ImportManifestIntoJob(options, manifest, existingJob, pageImportRoot, takeoffImportRoot);
        }

        if (PlanSwiftSourceFormats.IsOurPlanCore(manifest.SourceFormat))
            return ImportExistingOurPlanCoreJob(options, manifest);

        string jobName = ResolveDestinationJobName(options, manifest);
        OurPlanCoreJob job = OurPlanCoreJobStore.CreateJob(options.DestinationParentPath, jobName);
        return ImportManifestIntoJob(options, manifest, job, job.PagesRoot, job.TakeoffsRoot);
    }

    private static PlanSwiftImportResult ImportManifestIntoJob(
        PlanSwiftImportOptions options,
        PlanSwiftProjectManifest manifest,
        OurPlanCoreJob job,
        string pagesRoot,
        string takeoffsRoot)
    {
        var messages = manifest.Warnings.ToList();

        string tempRoot = Path.Combine(Path.GetTempPath(), "ourplancore_planswift_import", Guid.NewGuid().ToString("N"));
        var pageByGuid = new Dictionary<string, ImportedPlanSwiftPage>(StringComparer.OrdinalIgnoreCase);
        int importedPages = 0;
        int importedItems = 0;
        int importedMeasurements = 0;
        var importedTakeoffsBySource = new Dictionary<string, TakeoffItem>(StringComparer.OrdinalIgnoreCase);

        try
        {
            ImportPages(options, manifest, job, pagesRoot, tempRoot, pageByGuid, messages, ref importedPages);
            ImportTakeoffs(options, manifest, job, takeoffsRoot, pageByGuid, importedTakeoffsBySource, messages, ref importedItems, ref importedMeasurements);
            ImportSegments(options, manifest, job, takeoffsRoot, pageByGuid, importedTakeoffsBySource, messages, ref importedItems, ref importedMeasurements);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }

        var result = new PlanSwiftImportResult(
            manifest.SourceJobPath,
            job.RootPath,
            importedPages,
            importedItems,
            importedMeasurements,
            messages.Count,
            messages,
            manifest.TakeoffFolders.Count);

        WriteReports(job, manifest, result);
        return result;
    }
}
