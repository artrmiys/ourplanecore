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
}
