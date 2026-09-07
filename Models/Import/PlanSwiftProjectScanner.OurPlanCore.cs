using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OurPlanCore;

public static partial class PlanSwiftProjectScanner
{
    private static bool TryScanExistingOurPlanCoreJob(
        string sourceJobPath,
        out PlanSwiftProjectManifest manifest)
    {
        manifest = new PlanSwiftProjectManifest();
        string pagesRoot = Path.Combine(sourceJobPath, "Pages");
        string takeoffsRoot = Path.Combine(sourceJobPath, "Takeoffs");
        if (!File.Exists(Path.Combine(sourceJobPath, "Data.xml")) ||
            !Directory.Exists(pagesRoot) ||
            !Directory.Exists(takeoffsRoot) ||
            !HasOurPlanCoreSignals(pagesRoot, takeoffsRoot))
        {
            return false;
        }

        var warnings = new List<string>();
        IReadOnlyList<PlanSwiftPageRecord> pages = ScanOurPlanCorePages(pagesRoot, warnings);
        IReadOnlyList<PlanSwiftFolderRecord> folders = ScanOurPlanCoreFolders(takeoffsRoot);
        IReadOnlyList<PlanSwiftTakeoffItemRecord> items = ScanOurPlanCoreItems(sourceJobPath, takeoffsRoot, warnings);

        manifest = new PlanSwiftProjectManifest
        {
            SourceJobPath = Path.GetFullPath(sourceJobPath),
            SourceFormat = PlanSwiftSourceFormats.OurPlanCore,
            JobName = OurPlanCoreJobStore.ReadName(sourceJobPath) ??
                Path.GetFileName(sourceJobPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            TakeoffClassCounts = BuildOurPlanCoreClassCounts(folders, items),
            Pages = pages,
            TakeoffFolders = folders,
            TakeoffItems = items,
            Warnings = warnings,
        };
        return true;
    }

    private static bool HasOurPlanCoreSignals(string pagesRoot, string takeoffsRoot) =>
        Directory.EnumerateFiles(pagesRoot, "source.json", SearchOption.AllDirectories).Any() ||
        Directory.EnumerateFiles(takeoffsRoot, "measurements.json", SearchOption.AllDirectories).Any() ||
        Directory.EnumerateFiles(takeoffsRoot, "Data.xml", SearchOption.AllDirectories)
            .Any(path =>
            {
                string folder = Path.GetDirectoryName(path) ?? takeoffsRoot;
                return OurPlanCoreJobStore.IsTakeoffItemFolder(folder);
            });

    private static IReadOnlyList<PlanSwiftPageRecord> ScanOurPlanCorePages(
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

            SourceInfo? source = OurPlanCoreJobStore.ReadSource(folder);
            string pdfPath = source == null ? "" : Path.GetFullPath(Path.Combine(folder, source.Pdf));
            if (source == null)
                warnings.Add($"OurPlanCore page '{item.Name}' has no source.json: {folder}");
            else if (!File.Exists(pdfPath))
                warnings.Add($"OurPlanCore page '{item.Name}' source PDF is missing: {pdfPath}");

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

    private static IReadOnlyList<PlanSwiftFolderRecord> ScanOurPlanCoreFolders(string takeoffsRoot)
    {
        var folders = new List<PlanSwiftFolderRecord>();
        foreach (string dataPath in Directory.EnumerateFiles(takeoffsRoot, "Data.xml", SearchOption.AllDirectories))
        {
            string folder = Path.GetDirectoryName(dataPath) ?? takeoffsRoot;
            if (SamePath(folder, takeoffsRoot) || OurPlanCoreJobStore.IsTakeoffItemFolder(folder))
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

    private static IReadOnlyList<PlanSwiftTakeoffItemRecord> ScanOurPlanCoreItems(
        string sourceJobPath,
        string takeoffsRoot,
        List<string> warnings)
    {
        var items = new List<PlanSwiftTakeoffItemRecord>();
        foreach (string folder in Directory.EnumerateDirectories(takeoffsRoot, "*", SearchOption.AllDirectories))
        {
            TakeoffItem? item = OurPlanCoreJobStore.TryReadTakeoffItem(folder);
            if (item == null)
                continue;

            IReadOnlyList<PlanSwiftSectionRecord> sections =
                BuildOurPlanCoreSections(sourceJobPath, item, warnings);
            items.Add(new PlanSwiftTakeoffItemRecord
            {
                SourceFolder = folder,
                RelativeFolder = RelativePathFromRoot(takeoffsRoot, folder),
                ParentRelativeFolder = ParentRelativePathFromRoot(takeoffsRoot, folder),
                Name = item.Name,
                ClassName = $"OurPlanCore {item.MeasurementType}",
                MeasurementType = item.MeasurementType,
                ColorHex = item.Color,
                OrderIndex = PlanSwiftXml.ParseInt(OurPlanCoreJobStore.ReadProperty(folder, "OrderIndex") ?? ""),
                Sections = sections,
            });
        }

        return items
            .OrderBy(item => item.ParentRelativeFolder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.OrderIndex)
            .ThenBy(item => item.RelativeFolder, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<PlanSwiftSectionRecord> BuildOurPlanCoreSections(
        string sourceJobPath,
        TakeoffItem item,
        List<string> warnings)
    {
        var sections = new List<PlanSwiftSectionRecord>();
        for (int i = 0; i < item.Measurements.Count; i++)
        {
            Measurement measurement = item.Measurements[i];
            string pageGuid = TryReadOurPlanCorePageGuid(measurement.PageFolder);
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
                OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType),
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

    private static string TryReadOurPlanCorePageGuid(string pageFolder)
    {
        if (string.IsNullOrWhiteSpace(pageFolder) || !Directory.Exists(pageFolder))
            return "";

        return PlanSwiftXml.TryReadItem(pageFolder, out PlanSwiftDataItem page)
            ? page.Guid
            : "";
    }

    private static IReadOnlyList<PlanSwiftClassCount> BuildOurPlanCoreClassCounts(
        IReadOnlyList<PlanSwiftFolderRecord> folders,
        IReadOnlyList<PlanSwiftTakeoffItemRecord> items)
    {
        int measurements = items.Sum(item => item.Sections.Count);
        var counts = new List<PlanSwiftClassCount>
        {
            new("OurPlanCore takeoff folder", folders.Count),
            new("OurPlanCore takeoff item", items.Count),
            new("OurPlanCore measurement", measurements),
        };
        return counts.Where(count => count.Count > 0).ToList();
    }
}
