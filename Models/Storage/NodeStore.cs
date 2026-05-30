using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace OurPlaneCore;

internal static class NodeStore
{
    private static readonly IComparer<string> NaturalNameComparer = new NaturalStringComparer();

    public static IReadOnlyList<string> GetOrderedChildDirectories(string parentFolder)
    {
        if (!Directory.Exists(parentFolder)) return [];
        return Directory.EnumerateDirectories(parentFolder)
            .OrderBy(GetOrderIndex)
            .ThenBy(OurPlaneCoreJobStore.DisplayName, NaturalNameComparer)
            .ToList();
    }

    public static int GetOrderIndex(string folder)
    {
        string? raw = OurPlaneCoreJobStore.ReadProperty(folder, "OrderIndex");
        return int.TryParse(raw, out int order) ? order : int.MaxValue;
    }

    public static void SetOrderIndex(string folder, int orderIndex) =>
        OurPlaneCoreJobStore.SetProperty(folder, "OrderIndex", orderIndex.ToString());

    public static string RenameNode(string folder, string requestedName)
    {
        string displayName = OurPlaneCoreJobStore.NormalizeDisplayName(requestedName, 120);
        string folderName = OurPlaneCoreJobStore.SanitizeName(displayName, 120);
        string parent = Path.GetDirectoryName(folder)
            ?? throw new InvalidOperationException("Cannot rename a root folder.");
        string target = Path.Combine(parent, folderName);

        if (!string.Equals(folder, target, StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(target))
        {
            throw new IOException($"'{displayName}' already exists in this folder.");
        }

        if (!string.Equals(folder, target, StringComparison.OrdinalIgnoreCase))
            Directory.Move(folder, target);

        OurPlaneCoreJobStore.UpdateItemName(target, displayName);
        return target;
    }

    public static string RenamePageAllowDuplicateName(string folder, string requestedName)
    {
        return RenameNodeAllowDuplicateName(folder, requestedName);
    }

    public static string RenameNodeAllowDuplicateName(string folder, string requestedName)
    {
        string displayName = OurPlaneCoreJobStore.NormalizeDisplayName(requestedName, 120);
        string folderName = OurPlaneCoreJobStore.SanitizeName(displayName, 120);
        string parent = Path.GetDirectoryName(folder)
            ?? throw new InvalidOperationException("Cannot rename a root folder.");
        string desiredTarget = Path.Combine(parent, folderName);
        string target = string.Equals(folder, desiredTarget, StringComparison.OrdinalIgnoreCase)
            ? folder
            : OurPlaneCoreJobStore.UniqueDirectoryPath(desiredTarget);

        if (!string.Equals(folder, target, StringComparison.OrdinalIgnoreCase))
            Directory.Move(folder, target);

        OurPlaneCoreJobStore.UpdateItemName(target, displayName);
        return target;
    }

    public static string CopyNode(string sourcePath, string targetFolder)
    {
        return CopyNodePreserveDisplayName(sourcePath, targetFolder);
    }

    public static string CopyNodePreserveDisplayName(string sourcePath, string targetFolder)
    {
        string displayName = OurPlaneCoreJobStore.DisplayName(sourcePath);
        string destPath = UniqueDestinationPath(targetFolder, displayName);
        return CopyNodeCore(sourcePath, destPath, displayName, GetNextOrderIndex(targetFolder));
    }

    public static IReadOnlyList<string> CopyNodesPreserveDisplayName(IEnumerable<string> sourcePaths, string targetFolder)
    {
        var sources = sourcePaths
            .Where(Directory.Exists)
            .Select(NormalizeFolderPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (sources.Count == 0)
            return [];

        var results = new List<string>();
        int nextOrder = GetNextOrderIndex(targetFolder);
        foreach (string sourcePath in sources)
        {
            string displayName = OurPlaneCoreJobStore.DisplayName(sourcePath);
            string destPath = UniqueDestinationPath(targetFolder, displayName);
            results.Add(CopyNodeCore(sourcePath, destPath, displayName, nextOrder++));
        }

        return results;
    }

    private static string CopyNodeCore(string sourcePath, string destPath, string displayName, int orderIndex)
    {
        var pageSources = PageStore.CollectPageSources(sourcePath);

        CopyDirectory(sourcePath, destPath);
        PageStore.RewritePageSources(destPath, pageSources);
        RegenerateGuidsRecursively(destPath);
        RegenerateMeasurementIdsRecursively(destPath);
        OurPlaneCoreJobStore.UpdateItemName(destPath, displayName);
        SetOrderIndex(destPath, orderIndex);
        return destPath;
    }

    public static string MoveNode(string sourcePath, string targetFolder)
    {
        string sourceParent = Path.GetDirectoryName(sourcePath) ?? "";
        if (string.Equals(sourceParent, targetFolder, StringComparison.OrdinalIgnoreCase))
            return sourcePath;

        int nextOrder = GetNextOrderIndex(targetFolder);
        string destPath = MoveNodeCore(sourcePath, targetFolder, ref nextOrder);
        NormalizeOrder(sourceParent);
        return destPath;
    }

    public static IReadOnlyList<(string SourcePath, string MovedPath)> MoveNodes(
        IEnumerable<string> sourcePaths,
        string targetFolder)
    {
        var sources = sourcePaths
            .Where(Directory.Exists)
            .Select(NormalizeFolderPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (sources.Count == 0)
            return [];

        var results = new List<(string SourcePath, string MovedPath)>();
        var affectedSourceParents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int nextOrder = GetNextOrderIndex(targetFolder);

        foreach (string sourcePath in sources)
        {
            string sourceParent = Path.GetDirectoryName(sourcePath) ?? "";
            if (string.Equals(sourceParent, targetFolder, StringComparison.OrdinalIgnoreCase))
            {
                results.Add((sourcePath, sourcePath));
                continue;
            }

            string movedPath = MoveNodeCore(sourcePath, targetFolder, ref nextOrder);
            if (!string.IsNullOrWhiteSpace(sourceParent))
                affectedSourceParents.Add(sourceParent);
            results.Add((sourcePath, movedPath));
        }

        foreach (string sourceParent in affectedSourceParents)
        {
            if (!string.Equals(sourceParent, targetFolder, StringComparison.OrdinalIgnoreCase))
                NormalizeOrder(sourceParent);
        }

        return results;
    }

    public static string DuplicatePage(string pageFolder)
    {
        if (!OurPlaneCoreJobStore.IsPageFolder(pageFolder))
            throw new InvalidOperationException("Only page nodes can be duplicated.");
        string parent = Path.GetDirectoryName(pageFolder)
            ?? throw new InvalidOperationException("Cannot duplicate this page.");
        return CopyNodePreserveDisplayName(pageFolder, parent);
    }

    public static bool MoveSibling(string folder, int offset)
    {
        string parent = Path.GetDirectoryName(folder) ?? "";
        var siblings = GetOrderedChildDirectories(parent).ToList();
        int index = siblings.FindIndex(p => string.Equals(p, folder, StringComparison.OrdinalIgnoreCase));
        int target = index + offset;
        if (index < 0 || target < 0 || target >= siblings.Count) return false;

        (siblings[index], siblings[target]) = (siblings[target], siblings[index]);
        ApplySiblingOrder(siblings);
        return true;
    }

    public static bool CanMoveSiblings(IEnumerable<string> folders, int offset) =>
        TryBuildSiblingMove(folders, offset, out _);

    public static bool MoveSiblings(IEnumerable<string> folders, int offset)
    {
        if (!TryBuildSiblingMove(folders, offset, out var siblings))
            return false;

        ApplySiblingOrder(siblings);
        return true;
    }

    public static bool CanMoveSiblingsToPosition(IEnumerable<string> folders, string targetFolder, bool after) =>
        TryBuildSiblingPositionMove(folders, targetFolder, after, out _);

    public static bool MoveSiblingsToPosition(IEnumerable<string> folders, string targetFolder, bool after)
    {
        if (!TryBuildSiblingPositionMove(folders, targetFolder, after, out var siblings))
            return false;

        ApplySiblingOrder(siblings);
        return true;
    }

    public static bool MoveSiblingsToEnd(IEnumerable<string> folders, string parentFolder)
    {
        if (!TryBuildSiblingEndMove(folders, parentFolder, out var siblings))
            return false;

        ApplySiblingOrder(siblings);
        return true;
    }

    public static void SortChildren(string parentFolder, bool descending)
    {
        SortChildren(parentFolder, descending, NaturalNameComparer);
    }

    public static void SortTakeoffChildren(string parentFolder, bool descending)
    {
        SortChildren(parentFolder, descending, TakeoffDetailReferenceNameComparer.Instance);
    }

    private static void SortChildren(string parentFolder, bool descending, IComparer<string> displayNameComparer)
    {
        var children = Directory.EnumerateDirectories(parentFolder)
            .OrderBy(OurPlaneCoreJobStore.DisplayName, displayNameComparer)
            .ToList();
        if (descending) children.Reverse();
        ApplySiblingOrder(children);
    }

    public static void NormalizeOrder(string parentFolder)
    {
        if (!Directory.Exists(parentFolder)) return;
        ApplySiblingOrder(GetOrderedChildDirectories(parentFolder));
    }

    public static int GetNextOrderIndex(string parentFolder)
    {
        if (!Directory.Exists(parentFolder)) return 1;

        int max = 0;
        foreach (string dir in Directory.EnumerateDirectories(parentFolder))
        {
            string? raw = OurPlaneCoreJobStore.ReadProperty(dir, "OrderIndex");
            if (int.TryParse(raw, out int order))
                max = Math.Max(max, order);
        }
        return max + 1;
    }

    private static string MoveNodeCore(string sourcePath, string targetFolder, ref int nextOrder)
    {
        string displayName = OurPlaneCoreJobStore.DisplayName(sourcePath);
        string destPath = UniqueDestinationPath(targetFolder, displayName);
        var pageSources = PageStore.CollectPageSources(sourcePath);

        Directory.Move(sourcePath, destPath);
        PageStore.RewritePageSources(destPath, pageSources);
        OurPlaneCoreJobStore.UpdateItemName(destPath, displayName);
        SetOrderIndex(destPath, nextOrder++);
        return destPath;
    }

    private static bool TryBuildSiblingMove(IEnumerable<string> folders, int offset, out List<string> siblings)
    {
        siblings = [];
        if (offset == 0)
            return false;

        var selected = folders
            .Where(Directory.Exists)
            .Select(NormalizeFolderPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selected.Count == 0)
            return false;

        string parent = Path.GetDirectoryName(selected[0]) ?? "";
        if (string.IsNullOrWhiteSpace(parent) ||
            selected.Any(path => !string.Equals(Path.GetDirectoryName(path) ?? "", parent, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var orderedSiblings = GetOrderedChildDirectories(parent)
            .Select(NormalizeFolderPath)
            .ToList();
        siblings = orderedSiblings;
        if (siblings.Count <= 1 || selected.Count >= siblings.Count)
            return false;

        var selectedSet = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedSet.Any(path => !orderedSiblings.Contains(path, StringComparer.OrdinalIgnoreCase)))
            return false;

        bool moved = false;
        if (offset < 0)
        {
            for (int i = 1; i < siblings.Count; i++)
            {
                if (selectedSet.Contains(siblings[i]) && !selectedSet.Contains(siblings[i - 1]))
                {
                    (siblings[i - 1], siblings[i]) = (siblings[i], siblings[i - 1]);
                    moved = true;
                }
            }
        }
        else
        {
            for (int i = siblings.Count - 2; i >= 0; i--)
            {
                if (selectedSet.Contains(siblings[i]) && !selectedSet.Contains(siblings[i + 1]))
                {
                    (siblings[i], siblings[i + 1]) = (siblings[i + 1], siblings[i]);
                    moved = true;
                }
            }
        }

        return moved;
    }

    private static bool TryBuildSiblingPositionMove(
        IEnumerable<string> folders,
        string targetFolder,
        bool after,
        out List<string> siblings)
    {
        siblings = [];

        var selected = folders
            .Where(Directory.Exists)
            .Select(NormalizeFolderPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selected.Count == 0 || !Directory.Exists(targetFolder))
            return false;

        string target = NormalizeFolderPath(targetFolder);
        string parent = Path.GetDirectoryName(selected[0]) ?? "";
        string targetParent = Path.GetDirectoryName(target) ?? "";
        if (string.IsNullOrWhiteSpace(parent) ||
            !string.Equals(parent, targetParent, StringComparison.OrdinalIgnoreCase) ||
            selected.Any(path => !string.Equals(Path.GetDirectoryName(path) ?? "", parent, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var orderedSiblings = GetOrderedChildDirectories(parent)
            .Select(NormalizeFolderPath)
            .ToList();
        var selectedSet = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedSet.Contains(target) ||
            selectedSet.Any(path => !orderedSiblings.Contains(path, StringComparer.OrdinalIgnoreCase)) ||
            !orderedSiblings.Contains(target, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var moving = orderedSiblings
            .Where(path => selectedSet.Contains(path))
            .ToList();
        var remaining = orderedSiblings
            .Where(path => !selectedSet.Contains(path))
            .ToList();
        int targetIndex = remaining.FindIndex(path => string.Equals(path, target, StringComparison.OrdinalIgnoreCase));
        if (targetIndex < 0)
            return false;

        int insertIndex = after ? targetIndex + 1 : targetIndex;
        remaining.InsertRange(insertIndex, moving);
        siblings = remaining;
        return !orderedSiblings.SequenceEqual(siblings, StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryBuildSiblingEndMove(
        IEnumerable<string> folders,
        string parentFolder,
        out List<string> siblings)
    {
        siblings = [];

        var selected = folders
            .Where(Directory.Exists)
            .Select(NormalizeFolderPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selected.Count == 0 || !Directory.Exists(parentFolder))
            return false;

        string parent = NormalizeFolderPath(parentFolder);
        if (selected.Any(path => !string.Equals(Path.GetDirectoryName(path) ?? "", parent, StringComparison.OrdinalIgnoreCase)))
            return false;

        var orderedSiblings = GetOrderedChildDirectories(parent)
            .Select(NormalizeFolderPath)
            .ToList();
        var selectedSet = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedSet.Any(path => !orderedSiblings.Contains(path, StringComparer.OrdinalIgnoreCase)))
            return false;

        var moving = orderedSiblings
            .Where(path => selectedSet.Contains(path))
            .ToList();
        var remaining = orderedSiblings
            .Where(path => !selectedSet.Contains(path))
            .ToList();
        siblings = remaining.Concat(moving).ToList();
        return !orderedSiblings.SequenceEqual(siblings, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeFolderPath(string folder) =>
        Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static void ApplySiblingOrder(IEnumerable<string> orderedFolders)
    {
        int order = 1;
        foreach (string folder in orderedFolders)
            SetOrderIndex(folder, order++);
    }

    private static void RegenerateGuidsRecursively(string folder)
    {
        foreach (string dir in OurPlaneCoreJobStore.EnumerateSelfAndDescendants(folder))
            RegenerateDataXmlGuid(dir);
    }

    private static void RegenerateMeasurementIdsRecursively(string folder)
    {
        foreach (string dir in OurPlaneCoreJobStore.EnumerateSelfAndDescendants(folder))
        {
            string path = TakeoffStore.MeasurementsJsonPath(dir);
            if (!File.Exists(path)) continue;

            try
            {
                var measurements = OurPlaneCoreJobStore.LoadMeasurements(dir);
                foreach (var measurement in measurements)
                    measurement.Id = Guid.NewGuid().ToString();
                OurPlaneCoreJobStore.SaveMeasurements(dir, measurements);
            }
            catch
            {
                // Leave the copied file readable even if legacy measurement JSON is malformed.
            }
        }
    }

    private static void RegenerateDataXmlGuid(string folder)
    {
        string path = Path.Combine(folder, "Data.xml");
        if (!File.Exists(path)) return;

        XDocument doc = XDocument.Load(path);
        XElement? root = doc.Root;
        if (root == null) return;

        string guid = Guid.NewGuid().ToString().ToUpperInvariant();
        root.SetAttributeValue("GUID", guid);
        SetProperty(root, "GUID", guid);
        doc.Save(path);
        // This path loads/saves Data.xml directly; drop the cached copy so the
        // next read picks up the regenerated GUID.
        OurPlaneCoreJobStore.InvalidateMetadataCache(folder);
    }

    private static void SetProperty(XElement root, string propertyName, string value)
    {
        XElement props = root.Element("Properties") ?? new XElement("Properties");
        if (props.Parent == null)
            root.Add(props);

        XElement? prop = props.Elements("Property")
            .FirstOrDefault(p => p.Attribute("Name")?.Value == propertyName);
        if (prop == null)
        {
            prop = new XElement("Property", new XAttribute("Name", propertyName));
            props.Add(prop);
        }

        prop.SetAttributeValue("Value", value);
    }

    private static string UniqueDestinationPath(string targetFolder, string displayName) =>
        OurPlaneCoreJobStore.UniqueDirectoryPath(
            Path.Combine(targetFolder, OurPlaneCoreJobStore.SanitizeName(displayName, 120)));

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (string file in Directory.EnumerateFiles(sourceDir))
        {
            string destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: false);
        }

        foreach (string childDir in Directory.EnumerateDirectories(sourceDir))
        {
            string destChild = Path.Combine(destDir, Path.GetFileName(childDir));
            CopyDirectory(childDir, destChild);
        }
    }

    private sealed class NaturalStringComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            int ix = 0;
            int iy = 0;
            while (ix < x.Length && iy < y.Length)
            {
                char cx = x[ix];
                char cy = y[iy];
                if (char.IsDigit(cx) && char.IsDigit(cy))
                {
                    int nx = ReadNumberToken(x, ref ix);
                    int ny = ReadNumberToken(y, ref iy);
                    int numberCompare = nx.CompareTo(ny);
                    if (numberCompare != 0)
                        return numberCompare;
                    continue;
                }

                int charCompare = char.ToUpperInvariant(cx).CompareTo(char.ToUpperInvariant(cy));
                if (charCompare != 0)
                    return charCompare;
                ix++;
                iy++;
            }

            return x.Length.CompareTo(y.Length);
        }

        private static int ReadNumberToken(string text, ref int index)
        {
            int start = index;
            while (index < text.Length && char.IsDigit(text[index]))
                index++;

            string digits = text[start..index].TrimStart('0');
            if (digits.Length == 0) return 0;
            return int.TryParse(digits, out int value) ? value : int.MaxValue;
        }
    }
}
