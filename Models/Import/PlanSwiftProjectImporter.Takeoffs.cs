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
    private static void ImportTakeoffs(
        PlanSwiftImportOptions options,
        PlanSwiftProjectManifest manifest,
        OurPlanCoreJob job,
        string takeoffsRoot,
        IReadOnlyDictionary<string, ImportedPlanSwiftPage> pageByGuid,
        Dictionary<string, TakeoffItem> importedTakeoffsBySource,
        List<string> messages,
        ref int importedItems,
        ref int importedMeasurements)
    {
        foreach (PlanSwiftFolderRecord folder in manifest.TakeoffFolders)
        {
            string parent = EnsureRelativeFolder(takeoffsRoot, folder.ParentRelativeFolder);
            string importedFolder = OurPlanCoreJobStore.EnsureFolder(parent, folder.Name);
            OurPlanCoreJobStore.SetProperty(importedFolder, "SmartNodeKind", "folder");
            if (folder.OrderIndex > 0)
                OurPlanCoreJobStore.SetOrderIndex(importedFolder, folder.OrderIndex);
        }

        IEnumerable<PlanSwiftTakeoffItemRecord> items = Limit(manifest.TakeoffItems, options.MaxTakeoffItems);
        foreach (PlanSwiftTakeoffItemRecord item in items)
        {
            if (options.MaxMeasurements > 0 && importedMeasurements >= options.MaxMeasurements)
                break;

            string parent = EnsureRelativeFolder(takeoffsRoot, item.ParentRelativeFolder);
            string itemName = UniqueChildDisplayName(parent, item.Name);
            TakeoffItem imported = OurPlanCoreJobStore.CreateTakeoffItem(
                job,
                parent,
                itemName,
                item.ColorHex,
                item.MeasurementType);
            if (item.OrderIndex > 0)
                OurPlanCoreJobStore.SetOrderIndex(imported.FolderPath, item.OrderIndex);
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

            OurPlanCoreJobStore.SaveTakeoffItem(imported);
        }
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
}
