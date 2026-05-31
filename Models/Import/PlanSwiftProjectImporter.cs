using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace OurPlaneCore;

public static partial class PlanSwiftProjectImporter
{
    public static PlanSwiftImportResult Import(PlanSwiftImportOptions options)
    {
        ValidateOptions(options);

        PlanSwiftProjectManifest manifest = PlanSwiftProjectScanner.Scan(options.SourceJobPath);
        if (options.ImportIntoExistingJob)
        {
            if (string.Equals(manifest.SourceFormat, PlanSwiftSourceFormats.OurPlaneCore, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Import into the current job supports PlanSwift source job folders only.");

            OurPlaneCoreJob existingJob = OurPlaneCoreJobStore.LoadJob(options.DestinationJobPath);
            string pageImportRoot = EnsureImportRootFolder(existingJob.PagesRoot, options.ImportRootFolderName, isTakeoffRoot: false);
            string takeoffImportRoot = EnsureImportRootFolder(existingJob.TakeoffsRoot, options.ImportRootFolderName, isTakeoffRoot: true);
            return ImportManifestIntoJob(options, manifest, existingJob, pageImportRoot, takeoffImportRoot);
        }

        if (string.Equals(manifest.SourceFormat, PlanSwiftSourceFormats.OurPlaneCore, StringComparison.OrdinalIgnoreCase))
            return ImportExistingOurPlaneCoreJob(options, manifest);

        string jobName = ResolveDestinationJobName(options, manifest);
        OurPlaneCoreJob job = OurPlaneCoreJobStore.CreateJob(options.DestinationParentPath, jobName);
        return ImportManifestIntoJob(options, manifest, job, job.PagesRoot, job.TakeoffsRoot);
    }

    private static PlanSwiftImportResult ImportManifestIntoJob(
        PlanSwiftImportOptions options,
        PlanSwiftProjectManifest manifest,
        OurPlaneCoreJob job,
        string pagesRoot,
        string takeoffsRoot)
    {
        var messages = manifest.Warnings.ToList();

        string tempRoot = Path.Combine(Path.GetTempPath(), "ourplanecore_planswift_import", Guid.NewGuid().ToString("N"));
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
