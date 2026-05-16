using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using SkiaSharp;

namespace OurPlaneCore;

public static partial class PlanSwiftProjectImporter
{
    public static PlanSwiftImportResult Import(PlanSwiftImportOptions options)
    {
        ValidateOptions(options);

        PlanSwiftProjectManifest manifest = PlanSwiftProjectScanner.Scan(options.SourceJobPath);
        if (string.Equals(manifest.SourceFormat, PlanSwiftSourceFormats.OurPlaneCore, StringComparison.OrdinalIgnoreCase))
            return ImportExistingOurPlaneCoreJob(options, manifest);

        var messages = manifest.Warnings.ToList();
        string jobName = ResolveDestinationJobName(options, manifest);
        OurPlaneCoreJob job = OurPlaneCoreJobStore.CreateJob(options.DestinationParentPath, jobName);

        string tempRoot = Path.Combine(Path.GetTempPath(), "ourplanecore_planswift_import", Guid.NewGuid().ToString("N"));
        var pageByGuid = new Dictionary<string, ImportedPlanSwiftPage>(StringComparer.OrdinalIgnoreCase);
        int importedPages = 0;
        int importedItems = 0;
        int importedMeasurements = 0;

        try
        {
            ImportPages(options, manifest, job, tempRoot, pageByGuid, messages, ref importedPages);
            ImportTakeoffs(options, manifest, job, pageByGuid, messages, ref importedItems, ref importedMeasurements);
            ImportSegments(options, manifest, job, pageByGuid, messages, ref importedItems, ref importedMeasurements);
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

    private static void ImportPages(
        PlanSwiftImportOptions options,
        PlanSwiftProjectManifest manifest,
        OurPlaneCoreJob job,
        string tempRoot,
        Dictionary<string, ImportedPlanSwiftPage> pageByGuid,
        List<string> messages,
        ref int importedPages)
    {
        HashSet<string> takeoffPageGuids = BuildTakeoffPageGuidSet(manifest);
        IReadOnlyList<PlanSwiftPageRecord> pagesWithTakeoffs = manifest.Pages
            .Where(page => PageHasTakeoffGeometry(page, takeoffPageGuids))
            .ToList();
        int skippedPages = manifest.Pages.Count - pagesWithTakeoffs.Count;
        if (skippedPages > 0)
        {
            messages.Add(
                $"Skipped {skippedPages.ToString(CultureInfo.InvariantCulture)} PlanSwift page(s) " +
                "with no measured takeoff geometry.");
        }

        IEnumerable<PlanSwiftPageRecord> pages = Limit(pagesWithTakeoffs, options.MaxPages);
        foreach (PlanSwiftPageRecord page in pages)
        {
            string parent = EnsureRelativeFolder(job.PagesRoot, page.ParentRelativeFolder);
            string tempPdf = Path.Combine(tempRoot, "pages", $"{Guid.NewGuid():N}.pdf");
            PlanSwiftPageNormalization? imageNormalization = null;
            if (File.Exists(page.ImagePath))
                PlanSwiftPagePdfWriter.TryReadImagePageNormalization(page.ImagePath, out imageNormalization);

            PlanSwiftPageNormalization normalization;
            try
            {
                if (options.ConvertPageImages && File.Exists(page.ImagePath))
                    normalization = PlanSwiftPagePdfWriter.WriteImagePagePdf(page.ImagePath, tempPdf);
                else
                    normalization = PlanSwiftPagePdfWriter.WritePlaceholderPdf(
                        tempPdf,
                        $"PlanSwift page image missing: {page.Name}",
                        imageNormalization);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                messages.Add($"Page '{page.Name}' image conversion failed; blank PDF was created. {ex.Message}");
                normalization = PlanSwiftPagePdfWriter.WritePlaceholderPdf(
                    tempPdf,
                    $"PlanSwift page image failed: {page.Name}",
                    imageNormalization);
            }

            if (!string.IsNullOrWhiteSpace(normalization.Message))
                messages.Add($"Page '{page.Name}': {normalization.Message}");
            if (!PlanSwiftGeometryConverter.HasUniformPageNormalization(normalization))
            {
                messages.Add(
                    $"Page '{page.Name}' image has non-uniform coordinate scale " +
                    $"{normalization.CoordinateScaleX:G6}/{normalization.CoordinateScaleY:G6}; measurement scale uses X scale.");
            }

            double rawScale = PlanSwiftGeometryConverter.ScaleMetersPerPoint(page.ScaleX, page.ScaleUnits);
            double scale = PlanSwiftGeometryConverter.AdjustScaleForPageNormalization(rawScale, normalization);
            if (page.ScaleX > 0 && page.ScaleY > 0 && Math.Abs(page.ScaleX - page.ScaleY) > 0.001)
                messages.Add($"Page '{page.Name}' has different ScaleX/ScaleY values: {page.ScaleX:G17}/{page.ScaleY:G17}.");

            PageInfo imported = OurPlaneCoreJobStore.CreatePageFromPdf(
                job,
                tempPdf,
                page.Name,
                parent,
                pdfPage: 0,
                scaleMetersPerPt: scale);
            if (page.OrderIndex > 0)
                OurPlaneCoreJobStore.SetOrderIndex(imported.FolderPath, page.OrderIndex);
            importedPages++;

            if (!string.IsNullOrWhiteSpace(page.Guid))
                pageByGuid[page.Guid] = new ImportedPlanSwiftPage(imported, normalization);
        }
    }

    private static HashSet<string> BuildTakeoffPageGuidSet(PlanSwiftProjectManifest manifest)
    {
        var pageGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (PlanSwiftSectionRecord section in manifest.TakeoffItems.SelectMany(item => item.Sections))
            AddPageGuid(pageGuids, section.PageGuid);
        foreach (PlanSwiftSectionRecord section in manifest.Segments.SelectMany(segment => segment.Sections))
            AddPageGuid(pageGuids, section.PageGuid);

        return pageGuids;
    }

    private static void AddPageGuid(HashSet<string> pageGuids, string pageGuid)
    {
        string normalized = PlanSwiftXml.NormalizeGuid(pageGuid);
        if (!string.IsNullOrWhiteSpace(normalized))
            pageGuids.Add(normalized);
    }

    private static bool PageHasTakeoffGeometry(PlanSwiftPageRecord page, HashSet<string> takeoffPageGuids)
    {
        string normalized = PlanSwiftXml.NormalizeGuid(page.Guid);
        return !string.IsNullOrWhiteSpace(normalized) && takeoffPageGuids.Contains(normalized);
    }

    private static void ImportTakeoffs(
        PlanSwiftImportOptions options,
        PlanSwiftProjectManifest manifest,
        OurPlaneCoreJob job,
        IReadOnlyDictionary<string, ImportedPlanSwiftPage> pageByGuid,
        List<string> messages,
        ref int importedItems,
        ref int importedMeasurements)
    {
        foreach (PlanSwiftFolderRecord folder in manifest.TakeoffFolders)
        {
            string parent = EnsureRelativeFolder(job.TakeoffsRoot, folder.ParentRelativeFolder);
            string importedFolder = OurPlaneCoreJobStore.EnsureFolder(parent, folder.Name);
            OurPlaneCoreJobStore.SetProperty(importedFolder, "SmartNodeKind", "folder");
            if (folder.OrderIndex > 0)
                OurPlaneCoreJobStore.SetOrderIndex(importedFolder, folder.OrderIndex);
        }

        IEnumerable<PlanSwiftTakeoffItemRecord> items = Limit(manifest.TakeoffItems, options.MaxTakeoffItems);
        foreach (PlanSwiftTakeoffItemRecord item in items)
        {
            if (options.MaxMeasurements > 0 && importedMeasurements >= options.MaxMeasurements)
                break;

            string parent = EnsureRelativeFolder(job.TakeoffsRoot, item.ParentRelativeFolder);
            string itemName = UniqueChildDisplayName(parent, item.Name);
            TakeoffItem imported = OurPlaneCoreJobStore.CreateTakeoffItem(
                job,
                parent,
                itemName,
                item.ColorHex,
                item.MeasurementType);
            if (item.OrderIndex > 0)
                OurPlaneCoreJobStore.SetOrderIndex(imported.FolderPath, item.OrderIndex);
            importedItems++;

            foreach (PlanSwiftSectionRecord section in item.Sections)
            {
                if (options.MaxMeasurements > 0 && importedMeasurements >= options.MaxMeasurements)
                    break;
                if (!pageByGuid.TryGetValue(section.PageGuid, out ImportedPlanSwiftPage? page))
                {
                    messages.Add($"Section '{section.Name}' under '{item.Name}' references a page that was not imported.");
                    continue;
                }

                imported.Measurements.Add(CreateMeasurement(
                    manifest,
                    section,
                    page,
                    imported.FolderPath,
                    item.ColorHex,
                    "Imported from PlanSwift"));
                importedMeasurements++;
            }

            OurPlaneCoreJobStore.SaveTakeoffItem(imported);
        }
    }

    private static void ImportSegments(
        PlanSwiftImportOptions options,
        PlanSwiftProjectManifest manifest,
        OurPlaneCoreJob job,
        IReadOnlyDictionary<string, ImportedPlanSwiftPage> pageByGuid,
        List<string> messages,
        ref int importedItems,
        ref int importedMeasurements)
    {
        foreach (PlanSwiftSegmentRecord segment in manifest.Segments)
        {
            if (segment.Sections.Count == 0)
                continue;
            if (options.MaxTakeoffItems > 0 && importedItems >= options.MaxTakeoffItems)
                break;
            if (options.MaxMeasurements > 0 && importedMeasurements >= options.MaxMeasurements)
                break;

            string parent = EnsureRelativeFolder(job.TakeoffsRoot, segment.ParentRelativeFolder);
            string itemName = UniqueChildDisplayName(parent, SegmentTakeoffName(segment));
            TakeoffItem imported = OurPlaneCoreJobStore.CreateTakeoffItem(
                job,
                parent,
                itemName,
                segment.ColorHex,
                "line");
            imported.Notes = BuildSegmentNotes(manifest, segment);
            importedItems++;

            foreach (PlanSwiftSectionRecord section in segment.Sections)
            {
                if (options.MaxMeasurements > 0 && importedMeasurements >= options.MaxMeasurements)
                    break;
                if (!pageByGuid.TryGetValue(section.PageGuid, out ImportedPlanSwiftPage? page))
                {
                    messages.Add($"Segment section '{section.Name}' under '{segment.Name}' references a page that was not imported.");
                    continue;
                }

                imported.Measurements.Add(CreateMeasurement(
                    manifest,
                    section,
                    page,
                    imported.FolderPath,
                    segment.ColorHex,
                    "Imported from PlanSwift Segment Section"));
                importedMeasurements++;
            }

            OurPlaneCoreJobStore.SaveTakeoffItem(imported);
        }
    }

    private static void WriteReports(
        OurPlaneCoreJob job,
        PlanSwiftProjectManifest manifest,
        PlanSwiftImportResult result)
    {
        string reportRoot = Path.Combine(job.RootPath, "import_reports");
        Directory.CreateDirectory(reportRoot);

        var json = new
        {
            source_job = manifest.SourceJobPath,
            source_format = manifest.SourceFormat,
            destination_job = result.DestinationJobPath,
            pages_found = manifest.Pages.Count,
            takeoff_folders_found = manifest.TakeoffFolders.Count,
            takeoff_items_found = manifest.TakeoffItems.Count,
            takeoff_folders_imported = result.TakeoffFoldersImported,
            sections_found = manifest.TakeoffItems.Sum(item => item.Sections.Count),
            area_subtract_holes_found = manifest.TakeoffItems.Sum(item => item.Sections.Sum(section => section.Holes.Count)),
            segments_found = manifest.Segments.Count,
            segment_items_with_geometry_found = manifest.Segments.Count(segment => segment.Sections.Count > 0),
            segment_sections_found = manifest.Segments.Sum(segment => segment.Sections.Count),
            estimate_items_found = manifest.EstimateItems.Count,
            notes_found = manifest.Notes.Count,
            result.PagesImported,
            result.TakeoffItemsImported,
            result.MeasurementsImported,
            result.Warnings,
            takeoff_class_counts = manifest.TakeoffClassCounts.Select(count => new
            {
                count.ClassName,
                count.Count,
            }),
            pages = manifest.Pages.Select(page => new
            {
                page.Name,
                page.Guid,
                page.RelativeFolder,
                has_image = File.Exists(page.ImagePath),
                page.ScaleX,
                page.ScaleY,
                page.ScaleUnits,
                page.OrderIndex,
            }),
            takeoff_folders = manifest.TakeoffFolders.Select(folder => new
            {
                folder.Name,
                folder.RelativeFolder,
                folder.ParentRelativeFolder,
                folder.OrderIndex,
            }),
            takeoffs = manifest.TakeoffItems.Select(item => new
            {
                item.Name,
                item.RelativeFolder,
                item.MeasurementType,
                item.OrderIndex,
                sections = item.Sections.Count,
                holes = item.Sections.Sum(section => section.Holes.Count),
            }),
            segments = manifest.Segments.Select(segment => new
            {
                segment.Name,
                segment.ParentName,
                segment.RelativeFolder,
                segment.ParentRelativeFolder,
                segment.OrderIndex,
                sections = segment.Sections.Count,
            }),
            messages = result.Messages,
        };

        File.WriteAllText(
            Path.Combine(reportRoot, "planswift_import_manifest.json"),
            JsonSerializer.Serialize(json, OurPlaneCoreJobStore.JsonOptions));

        var sourceMetadata = new
        {
            source_job = manifest.SourceJobPath,
            takeoff_class_counts = manifest.TakeoffClassCounts,
            segments = manifest.Segments.Select(segment => new
            {
                segment.Name,
                segment.ParentName,
                segment.ClassName,
                segment.Guid,
                segment.RelativeFolder,
                segment.ParentRelativeFolder,
                segment.SourceParentRelativeFolder,
                segment.OrderIndex,
                segment.Properties,
                sections = segment.Sections.Select(section => new
                {
                    section.Name,
                    section.Guid,
                    section.PageGuid,
                    section.OrderIndex,
                    points = section.Points.Count,
                }),
            }),
            estimate_items = manifest.EstimateItems,
            notes = manifest.Notes,
        };

        File.WriteAllText(
            Path.Combine(reportRoot, "planswift_source_metadata.json"),
            JsonSerializer.Serialize(sourceMetadata, OurPlaneCoreJobStore.JsonOptions));

        File.WriteAllText(
            Path.Combine(reportRoot, "planswift_import_report.md"),
            BuildReportMarkdown(manifest, result));
    }

    private static string BuildReportMarkdown(PlanSwiftProjectManifest manifest, PlanSwiftImportResult result)
    {
        var lines = new List<string>
        {
            "# PlanSwift Import Report",
            "",
            $"Source: `{manifest.SourceJobPath}`",
            $"Source format: `{manifest.SourceFormat}`",
            $"Destination: `{result.DestinationJobPath}`",
            "",
            "## Summary",
            "",
            $"- Pages found: {manifest.Pages.Count.ToString(CultureInfo.InvariantCulture)}",
            $"- Takeoff folders found: {manifest.TakeoffFolders.Count.ToString(CultureInfo.InvariantCulture)}",
            $"- Takeoff items found: {manifest.TakeoffItems.Count.ToString(CultureInfo.InvariantCulture)}",
            $"- Takeoff folders imported: {result.TakeoffFoldersImported.ToString(CultureInfo.InvariantCulture)}",
            $"- Sections found: {manifest.TakeoffItems.Sum(item => item.Sections.Count).ToString(CultureInfo.InvariantCulture)}",
            $"- Area subtract holes found: {manifest.TakeoffItems.Sum(item => item.Sections.Sum(section => section.Holes.Count)).ToString(CultureInfo.InvariantCulture)}",
            $"- PlanSwift segments found: {manifest.Segments.Count.ToString(CultureInfo.InvariantCulture)}",
            $"- PlanSwift segments with geometry: {manifest.Segments.Count(segment => segment.Sections.Count > 0).ToString(CultureInfo.InvariantCulture)}",
            $"- Segment sections found: {manifest.Segments.Sum(segment => segment.Sections.Count).ToString(CultureInfo.InvariantCulture)}",
            $"- Estimate/material rows preserved: {manifest.EstimateItems.Count.ToString(CultureInfo.InvariantCulture)}",
            $"- Notes preserved: {manifest.Notes.Count.ToString(CultureInfo.InvariantCulture)}",
            $"- Pages imported: {result.PagesImported.ToString(CultureInfo.InvariantCulture)}",
            $"- Takeoff items imported: {result.TakeoffItemsImported.ToString(CultureInfo.InvariantCulture)}",
            $"- Measurements imported: {result.MeasurementsImported.ToString(CultureInfo.InvariantCulture)}",
            $"- Warnings: {result.Warnings.ToString(CultureInfo.InvariantCulture)}",
        };

        if (manifest.TakeoffClassCounts.Count > 0)
        {
            lines.Add("");
            lines.Add("## PlanSwift Takeoff Classes");
            lines.Add("");
            foreach (PlanSwiftClassCount count in manifest.TakeoffClassCounts.Take(20))
                lines.Add($"- {count.ClassName}: {count.Count.ToString(CultureInfo.InvariantCulture)}");
            if (manifest.TakeoffClassCounts.Count > 20)
                lines.Add($"- ... {manifest.TakeoffClassCounts.Count - 20} more classes");
        }

        if (result.Messages.Count > 0)
        {
            lines.Add("");
            lines.Add("## Warnings");
            lines.Add("");
            foreach (string message in result.Messages.Take(200))
                lines.Add($"- {message}");
            if (result.Messages.Count > 200)
                lines.Add($"- ... {result.Messages.Count - 200} more warnings");
        }

        lines.Add("");
        return string.Join(Environment.NewLine, lines);
    }

    private static Measurement CreateMeasurement(
        PlanSwiftProjectManifest manifest,
        PlanSwiftSectionRecord section,
        ImportedPlanSwiftPage importedPage,
        string takeoffFolder,
        string colorHex,
        string sourceLabel)
    {
        PageInfo page = importedPage.Page;
        PlanSwiftPageNormalization normalization = importedPage.Normalization;
        return new Measurement
        {
            Id = string.IsNullOrWhiteSpace(section.Guid) ? Guid.NewGuid().ToString("N") : section.Guid,
            Name = section.Name,
            Notes = $"{sourceLabel}: {Path.GetRelativePath(manifest.SourceJobPath, section.SourceFolder)}",
            MType = section.MeasurementType,
            Color = colorHex,
            PageFolder = page.FolderPath,
            TakeoffFolder = takeoffFolder,
            ScaleMetersPerPt = page.ScaleMetersPerPt,
            Points = section.Points.Select(point => TransformPoint(point, normalization)).ToList(),
            Holes = section.Holes
                .Select(hole => hole.Select(point => TransformPoint(point, normalization)).ToList())
                .Where(hole => hole.Count >= 3)
                .ToList(),
        };
    }

    private static SKPoint TransformPoint(PlanSwiftPoint point, PlanSwiftPageNormalization normalization) =>
        new(
            (float)(point.X * normalization.CoordinateScaleX),
            (float)(point.Y * normalization.CoordinateScaleY));

    private static string SegmentTakeoffName(PlanSwiftSegmentRecord segment)
    {
        string baseName = string.IsNullOrWhiteSpace(segment.ParentName)
            ? segment.Name
            : segment.ParentName;
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "PlanSwift Segment";

        return $"{baseName} - PlanSwift segments";
    }

    private static string BuildSegmentNotes(PlanSwiftProjectManifest manifest, PlanSwiftSegmentRecord segment)
    {
        var parts = new List<string>
        {
            $"Imported generated PlanSwift Segment geometry from {Path.GetRelativePath(manifest.SourceJobPath, segment.SourceFolder)}.",
        };
        AddPropertyNote(parts, segment, "Type");
        AddPropertyNote(parts, segment, "Qty");
        AddPropertyNote(parts, segment, "Default");
        AddPropertyNote(parts, segment, "Joist Length");
        AddPropertyNote(parts, segment, "Pitch");
        AddPropertyNote(parts, segment, "Section Type");
        return string.Join(Environment.NewLine, parts.Distinct(StringComparer.Ordinal));
    }

    private static void AddPropertyNote(List<string> parts, PlanSwiftSegmentRecord segment, string propertyName)
    {
        if (segment.Properties.TryGetValue(propertyName, out string? value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{propertyName}: {value}");
        }
    }

    private static string EnsureRelativeFolder(string root, string relativePath)
    {
        string current = root;
        foreach (string rawSegment in SplitRelativePath(relativePath))
        {
            string segment = PlanSwiftXml.DecodeName(rawSegment);
            current = OurPlaneCoreJobStore.EnsureFolder(current, segment);
        }

        return current;
    }

    private static IReadOnlyList<string> SplitRelativePath(string relativePath) =>
        string.IsNullOrWhiteSpace(relativePath)
            ? []
            : relativePath
                .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
                .Where(segment => segment != ".")
                .ToList();

    private static string UniqueChildDisplayName(string parent, string requestedName)
    {
        string clean = PlanSwiftXml.DecodeName(requestedName);
        string sanitized = OurPlaneCoreJobStore.SanitizeName(clean, 120);
        if (!Directory.Exists(Path.Combine(parent, sanitized)))
            return clean;

        for (int i = 2; ; i++)
        {
            string candidate = $"{clean} ({i})";
            if (!Directory.Exists(Path.Combine(parent, OurPlaneCoreJobStore.SanitizeName(candidate, 120))))
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
        string clean = OurPlaneCoreJobStore.SanitizeName(baseName, 120);
        if (!Directory.Exists(Path.Combine(options.DestinationParentPath, clean)))
            return clean;

        for (int i = 2; ; i++)
        {
            string candidate = OurPlaneCoreJobStore.SanitizeName($"{baseName} ({i})", 120);
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
        if (string.IsNullOrWhiteSpace(options.DestinationParentPath))
            throw new ArgumentException("Destination parent path is required.", nameof(options));

        Directory.CreateDirectory(options.DestinationParentPath);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private sealed record ImportedPlanSwiftPage(
        PageInfo Page,
        PlanSwiftPageNormalization Normalization);
}
