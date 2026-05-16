using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OurPlaneCore;

public static partial class PlanSwiftProjectScanner
{
    private static bool TryScanExistingOurPlaneCoreJob(
        string sourceJobPath,
        out PlanSwiftProjectManifest manifest)
    {
        manifest = new PlanSwiftProjectManifest();
        string pagesRoot = Path.Combine(sourceJobPath, "Pages");
        string takeoffsRoot = Path.Combine(sourceJobPath, "Takeoffs");
        if (!File.Exists(Path.Combine(sourceJobPath, "Data.xml")) ||
            !Directory.Exists(pagesRoot) ||
            !Directory.Exists(takeoffsRoot) ||
            !HasOurPlaneCoreSignals(pagesRoot, takeoffsRoot))
        {
            return false;
        }

        var warnings = new List<string>();
        IReadOnlyList<PlanSwiftPageRecord> pages = ScanOurPlaneCorePages(pagesRoot, warnings);
        IReadOnlyList<PlanSwiftFolderRecord> folders = ScanOurPlaneCoreFolders(takeoffsRoot);
        IReadOnlyList<PlanSwiftTakeoffItemRecord> items = ScanOurPlaneCoreItems(sourceJobPath, takeoffsRoot, warnings);

        manifest = new PlanSwiftProjectManifest
        {
            SourceJobPath = Path.GetFullPath(sourceJobPath),
            SourceFormat = PlanSwiftSourceFormats.OurPlaneCore,
            JobName = OurPlaneCoreJobStore.ReadName(sourceJobPath) ??
                Path.GetFileName(sourceJobPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            TakeoffClassCounts = BuildOurPlaneCoreClassCounts(folders, items),
            Pages = pages,
            TakeoffFolders = folders,
            TakeoffItems = items,
            Warnings = warnings,
        };
        return true;
    }

    private static bool HasOurPlaneCoreSignals(string pagesRoot, string takeoffsRoot) =>
        Directory.EnumerateFiles(pagesRoot, "source.json", SearchOption.AllDirectories).Any() ||
        Directory.EnumerateFiles(takeoffsRoot, "measurements.json", SearchOption.AllDirectories).Any() ||
        Directory.EnumerateFiles(takeoffsRoot, "Data.xml", SearchOption.AllDirectories)
            .Any(path =>
            {
                string folder = Path.GetDirectoryName(path) ?? takeoffsRoot;
                return OurPlaneCoreJobStore.IsTakeoffItemFolder(folder);
            });

    private static IReadOnlyList<PlanSwiftPageRecord> ScanOurPlaneCorePages(
        string pagesRoot,
        List<string> warnings)
    {
        var pages = new List<PlanSwiftPageRecord>();
        foreach (string dataPath in Directory.EnumerateFiles(pagesRoot, "Data.xml", SearchOption.AllDirectories))
        {
            string folder = Path.GetDirectoryName(dataPath) ?? pagesRoot;
            if (!PlanSwiftXml.TryReadItem(folder, out PlanSwiftDataItem item) ||
                !string.Equals(item.ClassName, "Page", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            SourceInfo? source = OurPlaneCoreJobStore.ReadSource(folder);
            string pdfPath = source == null ? "" : Path.GetFullPath(Path.Combine(folder, source.Pdf));
            if (source == null)
                warnings.Add($"OurPlaneCore page '{item.Name}' has no source.json: {folder}");
            else if (!File.Exists(pdfPath))
                warnings.Add($"OurPlaneCore page '{item.Name}' source PDF is missing: {pdfPath}");

            pages.Add(new PlanSwiftPageRecord(
                folder,
                NormalizeRelativePath(Path.GetRelativePath(pagesRoot, folder)),
                NormalizeRelativePath(Path.GetRelativePath(pagesRoot, Path.GetDirectoryName(folder) ?? pagesRoot)),
                item.Name,
                item.Guid,
                pdfPath,
                source?.ScaleMetersPerPt ?? 0,
                source?.ScaleMetersPerPt ?? 0,
                "M_PER_PT",
                PlanSwiftXml.ParseInt(item.Property("OrderIndex"))));
        }

        return pages
            .OrderBy(page => page.ParentRelativeFolder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(page => page.OrderIndex)
            .ThenBy(page => page.RelativeFolder, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<PlanSwiftFolderRecord> ScanOurPlaneCoreFolders(string takeoffsRoot)
    {
        var folders = new List<PlanSwiftFolderRecord>();
        foreach (string dataPath in Directory.EnumerateFiles(takeoffsRoot, "Data.xml", SearchOption.AllDirectories))
        {
            string folder = Path.GetDirectoryName(dataPath) ?? takeoffsRoot;
            if (SamePath(folder, takeoffsRoot) || OurPlaneCoreJobStore.IsTakeoffItemFolder(folder))
                continue;
            if (!PlanSwiftXml.TryReadItem(folder, out PlanSwiftDataItem item))
                continue;

            folders.Add(new PlanSwiftFolderRecord(
                folder,
                RelativePathFromRoot(takeoffsRoot, folder),
                ParentRelativePathFromRoot(takeoffsRoot, folder),
                item.Name,
                PlanSwiftXml.ParseInt(item.Property("OrderIndex"))));
        }

        return folders
            .OrderBy(folder => folder.ParentRelativeFolder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(folder => folder.OrderIndex)
            .ThenBy(folder => folder.RelativeFolder, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<PlanSwiftTakeoffItemRecord> ScanOurPlaneCoreItems(
        string sourceJobPath,
        string takeoffsRoot,
        List<string> warnings)
    {
        var items = new List<PlanSwiftTakeoffItemRecord>();
        foreach (string folder in Directory.EnumerateDirectories(takeoffsRoot, "*", SearchOption.AllDirectories))
        {
            TakeoffItem? item = OurPlaneCoreJobStore.TryReadTakeoffItem(folder);
            if (item == null)
                continue;

            IReadOnlyList<PlanSwiftSectionRecord> sections =
                BuildOurPlaneCoreSections(sourceJobPath, item, warnings);
            items.Add(new PlanSwiftTakeoffItemRecord
            {
                SourceFolder = folder,
                RelativeFolder = RelativePathFromRoot(takeoffsRoot, folder),
                ParentRelativeFolder = ParentRelativePathFromRoot(takeoffsRoot, folder),
                Name = item.Name,
                ClassName = $"OurPlaneCore {item.MeasurementType}",
                MeasurementType = item.MeasurementType,
                ColorHex = item.Color,
                OrderIndex = PlanSwiftXml.ParseInt(OurPlaneCoreJobStore.ReadProperty(folder, "OrderIndex") ?? ""),
                Sections = sections,
            });
        }

        return items
            .OrderBy(item => item.ParentRelativeFolder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.OrderIndex)
            .ThenBy(item => item.RelativeFolder, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<PlanSwiftSectionRecord> BuildOurPlaneCoreSections(
        string sourceJobPath,
        TakeoffItem item,
        List<string> warnings)
    {
        var sections = new List<PlanSwiftSectionRecord>();
        for (int i = 0; i < item.Measurements.Count; i++)
        {
            Measurement measurement = item.Measurements[i];
            string pageGuid = TryReadOurPlaneCorePageGuid(measurement.PageFolder);
            if (string.IsNullOrWhiteSpace(pageGuid))
            {
                warnings.Add($"Takeoff '{item.Name}' measurement {i + 1} is not linked to a readable source page.");
                continue;
            }
            if (measurement.Points.Count == 0)
            {
                warnings.Add($"Takeoff '{item.Name}' measurement {i + 1} has no points.");
                continue;
            }

            sections.Add(new PlanSwiftSectionRecord(
                item.FolderPath,
                string.IsNullOrWhiteSpace(measurement.Name) ? $"{item.Name} {i + 1}" : measurement.Name,
                string.IsNullOrWhiteSpace(measurement.Id) ? Guid.NewGuid().ToString("N") : measurement.Id,
                pageGuid,
                OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType),
                true,
                measurement.Points.Select(point => new PlanSwiftPoint(point.X, point.Y)).ToList(),
                measurement.Holes
                    .Select(hole => hole.Select(point => new PlanSwiftPoint(point.X, point.Y)).ToList())
                    .Where(hole => hole.Count >= 3)
                    .ToList(),
                "",
                false,
                i + 1));
        }

        return sections;
    }

    private static string TryReadOurPlaneCorePageGuid(string pageFolder)
    {
        if (string.IsNullOrWhiteSpace(pageFolder) || !Directory.Exists(pageFolder))
            return "";

        return PlanSwiftXml.TryReadItem(pageFolder, out PlanSwiftDataItem page)
            ? page.Guid
            : "";
    }

    private static IReadOnlyList<PlanSwiftClassCount> BuildOurPlaneCoreClassCounts(
        IReadOnlyList<PlanSwiftFolderRecord> folders,
        IReadOnlyList<PlanSwiftTakeoffItemRecord> items)
    {
        int measurements = items.Sum(item => item.Sections.Count);
        var counts = new List<PlanSwiftClassCount>
        {
            new("OurPlaneCore takeoff folder", folders.Count),
            new("OurPlaneCore takeoff item", items.Count),
            new("OurPlaneCore measurement", measurements),
        };
        return counts.Where(count => count.Count > 0).ToList();
    }
}
