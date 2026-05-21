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
        var importedTakeoffsBySource = new Dictionary<string, TakeoffItem>(StringComparer.OrdinalIgnoreCase);

        try
        {
            ImportPages(options, manifest, job, tempRoot, pageByGuid, messages, ref importedPages);
            ImportTakeoffs(options, manifest, job, pageByGuid, importedTakeoffsBySource, messages, ref importedItems, ref importedMeasurements);
            ImportSegments(options, manifest, job, pageByGuid, importedTakeoffsBySource, messages, ref importedItems, ref importedMeasurements);
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
        Dictionary<string, TakeoffItem> importedTakeoffsBySource,
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
            importedTakeoffsBySource[NormalizeImportRelativePath(item.RelativeFolder)] = imported;

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
        IReadOnlyDictionary<string, TakeoffItem> importedTakeoffsBySource,
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

            if (TryApplySegmentAsJoistArea(
                    manifest,
                    segment,
                    pageByGuid,
                    importedTakeoffsBySource,
                    messages))
            {
                continue;
            }

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

    private static bool TryApplySegmentAsJoistArea(
        PlanSwiftProjectManifest manifest,
        PlanSwiftSegmentRecord segment,
        IReadOnlyDictionary<string, ImportedPlanSwiftPage> pageByGuid,
        IReadOnlyDictionary<string, TakeoffItem> importedTakeoffsBySource,
        List<string> messages)
    {
        string sourceParentKey = NormalizeImportRelativePath(segment.SourceParentRelativeFolder);
        if (string.IsNullOrWhiteSpace(sourceParentKey) ||
            !importedTakeoffsBySource.TryGetValue(sourceParentKey, out TakeoffItem? areaItem) ||
            OurPlaneCoreJobStore.NormalizeMeasurementType(areaItem.MeasurementType) != "area" ||
            areaItem.Measurements.All(measurement => OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) != "area"))
        {
            return false;
        }

        if (!TryResolveSegmentJoistLayout(segment, pageByGuid, out PlanSwiftJoistSegmentLayout layout))
            return false;

        JoistTakeoffDefaults.ApplyToNewJoistArea(areaItem);
        areaItem.IsJoistTakeoff = true;
        areaItem.JoistDirectionDegrees = layout.DirectionDegrees;
        areaItem.JoistSpacingInches = layout.SpacingInches;
        areaItem.Notes = AppendPlanSwiftSegmentNote(areaItem.Notes, manifest, segment, layout);

        foreach (Measurement measurement in areaItem.Measurements.Where(measurement =>
                     OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area"))
        {
            measurement.JoistEnabled = true;
            measurement.JoistDirectionLocked = true;
            measurement.JoistDirectionDegrees = layout.DirectionDegrees;
            measurement.JoistSpacingInches = layout.SpacingInches;
        }

        OurPlaneCoreJobStore.ApplyTakeoffPropertiesToMeasurements(areaItem);
        OurPlaneCoreJobStore.SaveTakeoffItem(areaItem);
        messages.Add(
            $"Applied PlanSwift segment '{segment.Name}' as joist direction on '{areaItem.Name}' " +
            $"({layout.DirectionDegrees:0.#} deg, {layout.SpacingInches:0.###}\" O.C.).");
        return true;
    }

    private static bool TryResolveSegmentJoistLayout(
        PlanSwiftSegmentRecord segment,
        IReadOnlyDictionary<string, ImportedPlanSwiftPage> pageByGuid,
        out PlanSwiftJoistSegmentLayout layout)
    {
        var lines = new List<PlanSwiftSegmentLine>();
        foreach (PlanSwiftSectionRecord section in segment.Sections)
        {
            if (!pageByGuid.TryGetValue(section.PageGuid, out ImportedPlanSwiftPage? page) ||
                section.Points.Count < 2)
            {
                continue;
            }

            SKPoint start = TransformPoint(section.Points[0], page.Normalization);
            SKPoint end = TransformPoint(section.Points[^1], page.Normalization);
            double length = Distance(start, end);
            if (length <= 0.001)
                continue;

            double direction = NormalizePlanSwiftDirectionDegrees(Math.Atan2(end.Y - start.Y, end.X - start.X) * 180.0 / Math.PI);
            lines.Add(new PlanSwiftSegmentLine(start, end, direction, length, page.Page.ScaleMetersPerPt));
        }

        if (lines.Count == 0)
        {
            layout = default;
            return false;
        }

        PlanSwiftSegmentLine primary = lines.OrderByDescending(line => line.Length).First();
        double spacingInches = TryCalculateSegmentSpacingInches(lines, primary, out double calculatedSpacing)
            ? calculatedSpacing
            : TryReadSegmentSpacingInches(segment, out double propertySpacing)
                ? propertySpacing
                : 16.0;

        layout = new PlanSwiftJoistSegmentLayout(
            primary.DirectionDegrees,
            Math.Clamp(spacingInches, 0.001, 240.0));
        return true;
    }

    private static bool TryCalculateSegmentSpacingInches(
        IReadOnlyList<PlanSwiftSegmentLine> lines,
        PlanSwiftSegmentLine primary,
        out double spacingInches)
    {
        double radians = primary.DirectionDegrees * Math.PI / 180.0;
        double nx = -Math.Sin(radians);
        double ny = Math.Cos(radians);
        var projections = lines
            .Where(line => DirectionDeltaDegrees(line.DirectionDegrees, primary.DirectionDegrees) <= 15.0)
            .Select(line =>
            {
                SKPoint midpoint = new((line.Start.X + line.End.X) / 2f, (line.Start.Y + line.End.Y) / 2f);
                return midpoint.X * nx + midpoint.Y * ny;
            })
            .OrderBy(value => value)
            .ToList();

        var distances = new List<double>();
        for (int i = 1; i < projections.Count; i++)
        {
            double distance = Math.Abs(projections[i] - projections[i - 1]);
            if (distance > 0.001)
                distances.Add(distance);
        }

        if (distances.Count == 0 || primary.ScaleMetersPerPt <= 0)
        {
            spacingInches = 0;
            return false;
        }

        distances.Sort();
        double medianPdfDistance = distances[distances.Count / 2];
        spacingInches = medianPdfDistance * primary.ScaleMetersPerPt / 0.0254;
        return spacingInches > 0.001;
    }

    private static bool TryReadSegmentSpacingInches(PlanSwiftSegmentRecord segment, out double spacingInches)
    {
        foreach (string key in new[] { "Spacing", "Joist Spacing", "O.C.", "OC", "On Center" })
        {
            if (!segment.Properties.TryGetValue(key, out string? value) ||
                string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            Match match = System.Text.RegularExpressions.Regex.Match(value, @"[-+]?\d+(?:\.\d+)?");
            if (match.Success &&
                double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) &&
                parsed > 0)
            {
                spacingInches = parsed;
                return true;
            }
        }

        spacingInches = 0;
        return false;
    }

    private static string AppendPlanSwiftSegmentNote(
        string existingNotes,
        PlanSwiftProjectManifest manifest,
        PlanSwiftSegmentRecord segment,
        PlanSwiftJoistSegmentLayout layout)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(existingNotes))
            parts.Add(existingNotes.Trim());
        parts.Add(
            $"Imported PlanSwift Segment as joist area direction from {Path.GetRelativePath(manifest.SourceJobPath, segment.SourceFolder)} " +
            $"({layout.DirectionDegrees:0.#} deg, {layout.SpacingInches:0.###}\" O.C.).");
        AddPropertyNote(parts, segment, "Type");
        AddPropertyNote(parts, segment, "Default");
        AddPropertyNote(parts, segment, "Joist Length");
        AddPropertyNote(parts, segment, "Pitch");
        return string.Join(Environment.NewLine, parts.Distinct(StringComparer.Ordinal));
    }

    private static double DirectionDeltaDegrees(double a, double b)
    {
        double delta = Math.Abs(NormalizePlanSwiftDirectionDegrees(a) - NormalizePlanSwiftDirectionDegrees(b));
        return delta > 90.0 ? 180.0 - delta : delta;
    }

    private static double NormalizePlanSwiftDirectionDegrees(double degrees)
    {
        double normalized = degrees % 180.0;
        return normalized < 0.0 ? normalized + 180.0 : normalized;
    }

    private static double Distance(SKPoint a, SKPoint b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
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

    private static string NormalizeImportRelativePath(string relativePath) =>
        string.Join(Path.DirectorySeparatorChar, SplitRelativePath(relativePath));

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

    private readonly record struct PlanSwiftJoistSegmentLayout(
        double DirectionDegrees,
        double SpacingInches);

    private readonly record struct PlanSwiftSegmentLine(
        SKPoint Start,
        SKPoint End,
        double DirectionDegrees,
        double Length,
        double ScaleMetersPerPt);

    private sealed record ImportedPlanSwiftPage(
        PageInfo Page,
        PlanSwiftPageNormalization Normalization);
}
