using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace OurPlaneCore;

public static partial class PlanSwiftProjectScanner
{
    private static readonly string[] PageImagePatterns =
    [
        "*.tif",
        "*.tiff",
        "*.png",
        "*.jpg",
        "*.jpeg",
        "*.bmp",
    ];

    public static PlanSwiftProjectManifest Scan(string sourceJobPath)
    {
        if (string.IsNullOrWhiteSpace(sourceJobPath))
            throw new ArgumentException("PlanSwift job path is required.", nameof(sourceJobPath));
        if (!Directory.Exists(sourceJobPath))
            throw new DirectoryNotFoundException(sourceJobPath);

        if (TryScanExistingOurPlaneCoreJob(sourceJobPath, out PlanSwiftProjectManifest existingManifest))
            return existingManifest;

        var warnings = new List<string>();
        IReadOnlyList<PlanSwiftPageRecord> pages = ScanPages(sourceJobPath, warnings);
        string takeoffRoot = ResolveTakeoffRoot(sourceJobPath);
        IReadOnlyList<PlanSwiftClassCount> takeoffClassCounts = ScanTakeoffClassCounts(takeoffRoot);
        IReadOnlyList<PlanSwiftFolderRecord> takeoffFolders = ScanTakeoffFolders(takeoffRoot);
        IReadOnlyList<PlanSwiftTakeoffItemRecord> takeoffItems = ScanTakeoffItems(takeoffRoot, warnings);
        IReadOnlyList<PlanSwiftSegmentRecord> segments = ScanSegments(takeoffRoot, warnings);
        IReadOnlyList<PlanSwiftSourceRecord> estimateItems = ScanSourceRecords(takeoffRoot, "Item");
        IReadOnlyList<PlanSwiftSourceRecord> notes = ScanSourceRecords(takeoffRoot, "Note");
        if (string.IsNullOrWhiteSpace(takeoffRoot))
            warnings.Add("PlanSwift Takeoff folder was not found.");
        AddTakeoffScanDiagnostics(takeoffRoot, takeoffClassCounts, takeoffFolders, takeoffItems, segments, warnings);

        return new PlanSwiftProjectManifest
        {
            SourceJobPath = Path.GetFullPath(sourceJobPath),
            JobName = Path.GetFileName(sourceJobPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            TakeoffClassCounts = takeoffClassCounts,
            Pages = pages,
            TakeoffFolders = takeoffFolders,
            TakeoffItems = takeoffItems,
            Segments = segments,
            EstimateItems = estimateItems,
            Notes = notes,
            Warnings = warnings,
        };
    }

    private static void AddTakeoffScanDiagnostics(
        string takeoffRoot,
        IReadOnlyList<PlanSwiftClassCount> classCounts,
        IReadOnlyList<PlanSwiftFolderRecord> folders,
        IReadOnlyList<PlanSwiftTakeoffItemRecord> items,
        IReadOnlyList<PlanSwiftSegmentRecord> segments,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(takeoffRoot) || classCounts.Count == 0)
            return;

        int measuredSections = items.Sum(item => item.Sections.Count) +
            segments.Sum(segment => segment.Sections.Count);
        if (items.Count == 0 && measuredSections == 0 && folders.Count > 0)
        {
            string classes = string.Join(
                ", ",
                classCounts.Take(8).Select(count => $"{count.ClassName}={count.Count.ToString(CultureInfo.InvariantCulture)}"));
            warnings.Add(
                "PlanSwift Takeoff contains folder/template records but no measured Linear/Area/Count takeoff items. " +
                $"Classes found: {classes}.");
        }
        else if (items.Count > 0 && measuredSections == 0)
        {
            warnings.Add(
                $"PlanSwift takeoff items were found ({items.Count.ToString(CultureInfo.InvariantCulture)}), " +
                "but no visible measured sections with PageGUID and DigitizerData were found.");
        }
    }

    private static IReadOnlyList<PlanSwiftPageRecord> ScanPages(string sourceJobPath, List<string> warnings)
    {
        string pagesRoot = Path.Combine(sourceJobPath, "Pages");
        if (!Directory.Exists(pagesRoot))
        {
            warnings.Add("PlanSwift Pages folder was not found.");
            return [];
        }

        var pages = new List<PlanSwiftPageRecord>();
        foreach (string dataPath in Directory.EnumerateFiles(pagesRoot, "Data.xml", SearchOption.AllDirectories))
        {
            string folder = Path.GetDirectoryName(dataPath) ?? pagesRoot;
            if (!PlanSwiftXml.TryReadItem(folder, out PlanSwiftDataItem item))
                continue;
            if (!string.Equals(item.ClassName, "Page", StringComparison.OrdinalIgnoreCase))
                continue;

            string imagePath = FindPageImage(item);
            if (string.IsNullOrWhiteSpace(imagePath))
                warnings.Add($"Page '{item.Name}' has no image file: {folder}");

            pages.Add(new PlanSwiftPageRecord(
                folder,
                NormalizeRelativePath(Path.GetRelativePath(pagesRoot, folder)),
                NormalizeRelativePath(Path.GetRelativePath(pagesRoot, Path.GetDirectoryName(folder) ?? pagesRoot)),
                item.Name,
                item.Guid,
                imagePath,
                PlanSwiftXml.ParseDouble(item.Property("ScaleX")),
                PlanSwiftXml.ParseDouble(item.Property("ScaleY")),
                item.Property("Scale Units"),
                PlanSwiftXml.ParseInt(item.Property("OrderIndex"))));
        }

        return pages
            .OrderBy(page => page.ParentRelativeFolder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(page => page.OrderIndex)
            .ThenBy(page => page.RelativeFolder, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<PlanSwiftFolderRecord> ScanTakeoffFolders(string takeoffRoot)
    {
        if (string.IsNullOrWhiteSpace(takeoffRoot))
            return [];

        var folders = new List<PlanSwiftFolderRecord>();
        foreach (string dataPath in Directory.EnumerateFiles(takeoffRoot, "Data.xml", SearchOption.AllDirectories))
        {
            string folder = Path.GetDirectoryName(dataPath) ?? takeoffRoot;
            if (SamePath(folder, takeoffRoot))
                continue;
            if (!PlanSwiftXml.TryReadItem(folder, out PlanSwiftDataItem item))
                continue;
            bool isFolder = string.Equals(item.ClassName, "Folder", StringComparison.OrdinalIgnoreCase);
            bool isItemContainer = IsTakeoffItemClass(item.ClassName) &&
                HasChildTakeoffItem(folder) &&
                !HasMeasurementSectionChild(folder);
            if (!isFolder && !isItemContainer)
                continue;

            folders.Add(new PlanSwiftFolderRecord(
                folder,
                RelativePathFromRoot(takeoffRoot, folder),
                ParentRelativePathFromRoot(takeoffRoot, folder),
                item.Name,
                PlanSwiftXml.ParseInt(item.Property("OrderIndex"))));
        }

        return folders
            .OrderBy(folder => folder.ParentRelativeFolder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(folder => folder.OrderIndex)
            .ThenBy(folder => folder.RelativeFolder, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<PlanSwiftTakeoffItemRecord> ScanTakeoffItems(string takeoffRoot, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(takeoffRoot))
            return [];

        var items = new List<PlanSwiftTakeoffItemRecord>();
        foreach (string dataPath in Directory.EnumerateFiles(takeoffRoot, "Data.xml", SearchOption.AllDirectories))
        {
            string folder = Path.GetDirectoryName(dataPath) ?? takeoffRoot;
            if (!PlanSwiftXml.TryReadItem(folder, out PlanSwiftDataItem item))
                continue;
            if (!IsTakeoffItemClass(item.ClassName))
                continue;

            string measurementType = PlanSwiftGeometryConverter.MeasurementTypeFromClass(item.ClassName);
            IReadOnlyList<PlanSwiftSectionRecord> sections = ScanSections(item, measurementType, warnings);
            if (sections.Count == 0 && HasChildTakeoffItem(folder))
                continue;

            items.Add(new PlanSwiftTakeoffItemRecord
            {
                SourceFolder = folder,
                RelativeFolder = RelativePathFromRoot(takeoffRoot, folder),
                ParentRelativeFolder = ParentRelativePathFromRoot(takeoffRoot, folder),
                Name = item.Name,
                ClassName = item.ClassName,
                MeasurementType = measurementType,
                ColorHex = PlanSwiftGeometryConverter.ParsePlanSwiftColor(item.Property("Color")),
                OrderIndex = PlanSwiftXml.ParseInt(item.Property("OrderIndex")),
                Properties = CopyProperties(item),
                Sections = sections,
            });
        }

        return items
            .OrderBy(item => item.ParentRelativeFolder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.OrderIndex)
            .ThenBy(item => item.RelativeFolder, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<PlanSwiftSegmentRecord> ScanSegments(string takeoffRoot, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(takeoffRoot))
            return [];

        var segments = new List<PlanSwiftSegmentRecord>();
        foreach (string dataPath in Directory.EnumerateFiles(takeoffRoot, "Data.xml", SearchOption.AllDirectories))
        {
            string folder = Path.GetDirectoryName(dataPath) ?? takeoffRoot;
            if (!PlanSwiftXml.TryReadItem(folder, out PlanSwiftDataItem item))
                continue;
            if (!string.Equals(item.ClassName, "Segment", StringComparison.OrdinalIgnoreCase))
                continue;

            string sourceParent = Path.GetDirectoryName(folder) ?? takeoffRoot;
            bool hasSourceParent = PlanSwiftXml.TryReadItem(sourceParent, out PlanSwiftDataItem parentItem);
            string fallbackColor = hasSourceParent
                ? PlanSwiftGeometryConverter.ParsePlanSwiftColor(parentItem.Property("Color"), "#666666")
                : "#666666";
            string importParentRelative = hasSourceParent && IsTakeoffItemClass(parentItem.ClassName)
                ? ParentRelativePathFromRoot(takeoffRoot, sourceParent)
                : ParentRelativePathFromRoot(takeoffRoot, folder);

            IReadOnlyList<PlanSwiftSectionRecord> sections = ScanSegmentSections(item, warnings);
            segments.Add(new PlanSwiftSegmentRecord
            {
                SourceFolder = folder,
                RelativeFolder = RelativePathFromRoot(takeoffRoot, folder),
                ParentRelativeFolder = importParentRelative,
                SourceParentRelativeFolder = ParentRelativePathFromRoot(takeoffRoot, folder),
                Name = item.Name,
                ParentName = hasSourceParent ? parentItem.Name : "",
                Guid = item.Guid,
                ColorHex = PlanSwiftGeometryConverter.ParsePlanSwiftColor(item.Property("Color"), fallbackColor),
                OrderIndex = PlanSwiftXml.ParseInt(item.Property("OrderIndex")),
                Properties = CopyProperties(item),
                Sections = sections,
            });
        }

        return segments
            .OrderBy(segment => segment.ParentRelativeFolder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(segment => segment.OrderIndex)
            .ThenBy(segment => segment.RelativeFolder, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<PlanSwiftSectionRecord> ScanSegmentSections(
        PlanSwiftDataItem segment,
        List<string> warnings)
    {
        var sections = new List<PlanSwiftSectionRecord>();
        foreach (string sectionFolder in Directory.EnumerateDirectories(segment.FolderPath))
        {
            if (!PlanSwiftXml.TryReadItem(sectionFolder, out PlanSwiftDataItem section))
                continue;
            if (!string.Equals(section.ClassName, "Segment Section", StringComparison.OrdinalIgnoreCase))
                continue;

            bool visible = PlanSwiftXml.ParseBool(section.Property("Visible"), fallback: true);
            if (!visible)
                continue;

            string pageGuid = PlanSwiftXml.NormalizeGuid(section.Property("PageGUID"));
            string boxMode = section.Property("Box Mode");
            bool closed = PlanSwiftXml.ParseBool(section.Property("Closed"));
            IReadOnlyList<PlanSwiftPoint> points = PlanSwiftGeometryConverter.NormalizeSectionPoints(
                PlanSwiftGeometryConverter.ParseDigitizerPoints(section.Property("DigitizerData")),
                "line",
                boxMode,
                closed);
            if (string.IsNullOrWhiteSpace(pageGuid))
            {
                warnings.Add($"Segment section '{section.Name}' under '{segment.Name}' has no PageGUID.");
                continue;
            }
            if (points.Count == 0)
            {
                warnings.Add($"Segment section '{section.Name}' under '{segment.Name}' has no DigitizerData points.");
                continue;
            }

            sections.Add(new PlanSwiftSectionRecord(
                section.FolderPath,
                section.Name,
                section.Guid,
                pageGuid,
                "line",
                visible,
                points,
                [],
                boxMode,
                closed,
                PlanSwiftXml.ParseInt(section.Property("OrderIndex")))
            {
                Properties = CopyProperties(section),
            });
        }

        return sections
            .OrderBy(section => section.OrderIndex)
            .ThenBy(section => section.SourceFolder, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<PlanSwiftSectionRecord> ScanSections(
        PlanSwiftDataItem takeoffItem,
        string measurementType,
        List<string> warnings)
    {
        var sections = new List<PlanSwiftSectionRecord>();
        foreach (string sectionFolder in Directory.EnumerateDirectories(takeoffItem.FolderPath))
        {
            if (!PlanSwiftXml.TryReadItem(sectionFolder, out PlanSwiftDataItem section))
                continue;
            if (!IsMeasurementSectionClass(section.ClassName))
                continue;

            bool visible = PlanSwiftXml.ParseBool(section.Property("Visible"), fallback: true);
            if (!visible)
                continue;

            string pageGuid = PlanSwiftXml.NormalizeGuid(section.Property("PageGUID"));
            string sectionType = PlanSwiftGeometryConverter.MeasurementTypeFromClass(section.ClassName);
            string sectionMeasurementType = string.IsNullOrWhiteSpace(sectionType) ? measurementType : sectionType;
            string boxMode = section.Property("Box Mode");
            bool closed = PlanSwiftXml.ParseBool(section.Property("Closed"));
            IReadOnlyList<PlanSwiftPoint> points = PlanSwiftGeometryConverter.NormalizeSectionPoints(
                PlanSwiftGeometryConverter.ParseDigitizerPoints(section.Property("DigitizerData")),
                sectionMeasurementType,
                boxMode,
                closed);
            if (string.IsNullOrWhiteSpace(pageGuid))
            {
                warnings.Add($"Section '{section.Name}' under '{takeoffItem.Name}' has no PageGUID.");
                continue;
            }
            if (points.Count == 0)
            {
                warnings.Add($"Section '{section.Name}' under '{takeoffItem.Name}' has no DigitizerData points.");
                continue;
            }

            IReadOnlyList<IReadOnlyList<PlanSwiftPoint>> holes =
                string.Equals(sectionMeasurementType, "area", StringComparison.OrdinalIgnoreCase)
                    ? ScanAreaSubtractHoles(section, pageGuid, warnings)
                    : [];

            sections.Add(new PlanSwiftSectionRecord(
                section.FolderPath,
                section.Name,
                section.Guid,
                pageGuid,
                sectionMeasurementType,
                visible,
                points,
                holes,
                boxMode,
                closed,
                PlanSwiftXml.ParseInt(section.Property("OrderIndex")))
            {
                Properties = CopyProperties(section),
            });
        }

        return sections
            .OrderBy(section => section.OrderIndex)
            .ThenBy(section => section.SourceFolder, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<IReadOnlyList<PlanSwiftPoint>> ScanAreaSubtractHoles(
        PlanSwiftDataItem parentSection,
        string parentPageGuid,
        List<string> warnings)
    {
        var holes = new List<IReadOnlyList<PlanSwiftPoint>>();
        foreach (string holeFolder in Directory.EnumerateDirectories(parentSection.FolderPath))
        {
            if (!PlanSwiftXml.TryReadItem(holeFolder, out PlanSwiftDataItem hole))
                continue;
            if (!string.Equals(hole.ClassName, "Area Subtract Section", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!PlanSwiftXml.ParseBool(hole.Property("Visible"), fallback: true))
                continue;

            string holePageGuid = PlanSwiftXml.NormalizeGuid(hole.Property("PageGUID"));
            if (!string.IsNullOrWhiteSpace(holePageGuid) &&
                !string.Equals(holePageGuid, parentPageGuid, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Subtract section '{hole.Name}' under '{parentSection.Name}' references a different PageGUID.");
            }

            IReadOnlyList<PlanSwiftPoint> points = PlanSwiftGeometryConverter.NormalizeAreaHolePoints(
                PlanSwiftGeometryConverter.ParseDigitizerPoints(hole.Property("DigitizerData")),
                hole.Property("Box Mode"));
            if (points.Count < 3)
            {
                warnings.Add($"Subtract section '{hole.Name}' under '{parentSection.Name}' has no usable area points.");
                continue;
            }

            holes.Add(points);
        }

        return holes;
    }

    private static string FindPageImage(PlanSwiftDataItem page)
    {
        string imageGuid = PlanSwiftXml.NormalizeGuid(page.PropertyGuid("Image"));
        if (!string.IsNullOrWhiteSpace(imageGuid))
        {
            string braced = "{" + imageGuid + "}";
            foreach (string pattern in PageImagePatterns)
            {
                string? match = Directory.EnumerateFiles(page.FolderPath, pattern, SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(file => string.Equals(Path.GetFileNameWithoutExtension(file), braced, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match;
            }
        }

        foreach (string pattern in PageImagePatterns)
        {
            string? match = Directory.EnumerateFiles(page.FolderPath, pattern, SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (match != null)
                return match;
        }

        return "";
    }

    private static string ResolveTakeoffRoot(string sourceJobPath)
    {
        string takeoff = Path.Combine(sourceJobPath, "Takeoff");
        if (Directory.Exists(takeoff))
            return takeoff;

        string takeoffs = Path.Combine(sourceJobPath, "Takeoffs");
        return Directory.Exists(takeoffs) ? takeoffs : "";
    }

    private static bool IsTakeoffItemClass(string className) =>
        string.Equals(className, "Linear", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(className, "Area", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(className, "Count", StringComparison.OrdinalIgnoreCase);

    private static bool IsMeasurementSectionClass(string className) =>
        string.Equals(className, "Linear Section", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(className, "Area Section", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(className, "Count Section", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<PlanSwiftClassCount> ScanTakeoffClassCounts(string takeoffRoot)
    {
        if (string.IsNullOrWhiteSpace(takeoffRoot))
            return [];

        return Directory.EnumerateFiles(takeoffRoot, "Data.xml", SearchOption.AllDirectories)
            .Select(dataPath =>
            {
                string folder = Path.GetDirectoryName(dataPath) ?? takeoffRoot;
                return PlanSwiftXml.TryReadItem(folder, out PlanSwiftDataItem item)
                    ? string.IsNullOrWhiteSpace(item.ClassName) ? "(unknown)" : item.ClassName
                    : "(unreadable)";
            })
            .GroupBy(className => className, StringComparer.OrdinalIgnoreCase)
            .Select(group => new PlanSwiftClassCount(group.Key, group.Count()))
            .OrderByDescending(count => count.Count)
            .ThenBy(count => count.ClassName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<PlanSwiftSourceRecord> ScanSourceRecords(string takeoffRoot, string className)
    {
        if (string.IsNullOrWhiteSpace(takeoffRoot))
            return [];

        var records = new List<PlanSwiftSourceRecord>();
        foreach (string dataPath in Directory.EnumerateFiles(takeoffRoot, "Data.xml", SearchOption.AllDirectories))
        {
            string folder = Path.GetDirectoryName(dataPath) ?? takeoffRoot;
            if (!PlanSwiftXml.TryReadItem(folder, out PlanSwiftDataItem item))
                continue;
            if (!string.Equals(item.ClassName, className, StringComparison.OrdinalIgnoreCase))
                continue;

            records.Add(new PlanSwiftSourceRecord
            {
                SourceFolder = folder,
                RelativeFolder = RelativePathFromRoot(takeoffRoot, folder),
                ParentRelativeFolder = ParentRelativePathFromRoot(takeoffRoot, folder),
                Name = item.Name,
                ClassName = item.ClassName,
                Guid = item.Guid,
                OrderIndex = PlanSwiftXml.ParseInt(item.Property("OrderIndex")),
                Properties = CopyProperties(item),
            });
        }

        return records
            .OrderBy(record => record.ParentRelativeFolder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.OrderIndex)
            .ThenBy(record => record.RelativeFolder, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool HasChildTakeoffItem(string folder) =>
        Directory.EnumerateDirectories(folder)
            .Any(child => PlanSwiftXml.TryReadItem(child, out PlanSwiftDataItem item) &&
                IsTakeoffItemClass(item.ClassName));

    private static bool HasMeasurementSectionChild(string folder) =>
        Directory.EnumerateDirectories(folder)
            .Any(child => PlanSwiftXml.TryReadItem(child, out PlanSwiftDataItem item) &&
                IsMeasurementSectionClass(item.ClassName));

    private static IReadOnlyDictionary<string, string> CopyProperties(PlanSwiftDataItem item) =>
        new Dictionary<string, string>(item.Properties, StringComparer.OrdinalIgnoreCase);

    private static string RelativePathFromRoot(string root, string folder) =>
        SamePath(root, folder)
            ? ""
            : NormalizeRelativePath(Path.GetRelativePath(root, folder));

    private static string ParentRelativePathFromRoot(string root, string folder)
    {
        string parent = Path.GetDirectoryName(folder) ?? root;
        return SamePath(root, folder) || SamePath(root, parent)
            ? ""
            : NormalizeRelativePath(Path.GetRelativePath(root, parent));
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRelativePath(string relativePath) =>
        string.IsNullOrWhiteSpace(relativePath) || relativePath == "."
            ? ""
            : relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
}
