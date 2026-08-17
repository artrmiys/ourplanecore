using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace OurPlanCore;

internal sealed record PageDeleteUndoRequest(string SourcePath, bool IsPage);

internal sealed record PageDeleteUndoEntry(
    string OriginalPath,
    string TrashPath,
    int OriginalSiblingIndex,
    bool IsPage,
    IReadOnlyList<PageSourceSnapshot> PageSources);

internal sealed record PageDeleteUndoBatch(
    string JobRoot,
    string TrashRoot,
    IReadOnlyList<PageDeleteUndoEntry> Entries,
    string StatusName);

internal sealed record PageDeleteRestoreEntry(
    string OriginalPath,
    string RestoredPath,
    int OriginalSiblingIndex,
    bool IsPage);

internal static class PageDeleteUndoService
{
    public static PageDeleteUndoBatch MoveToTrash(
        OurPlanCoreJob job,
        IReadOnlyList<PageDeleteUndoRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(requests);

        List<PageDeleteUndoRequest> valid = NormalizeRequests(job, requests);
        if (valid.Count == 0)
            return new PageDeleteUndoBatch(job.RootPath, "", [], "page");

        Dictionary<string, int> siblingIndexes = BuildSiblingIndexes(valid);
        string trashRoot = CreateTrashRoot(job);
        var moved = new List<PageDeleteUndoEntry>();
        try
        {
            for (int i = 0; i < valid.Count; i++)
            {
                PageDeleteUndoRequest request = valid[i];
                string trashPath = UniqueTrashPath(trashRoot, request.SourcePath, i);
                List<PageSourceSnapshot> pageSources = PageStore.CollectPageSources(request.SourcePath);
                JobWriteAccess.Demand(request.SourcePath, "move a deleted page to undo trash");
                JobWriteAccess.Demand(trashPath, "create page undo trash");
                Directory.Move(request.SourcePath, trashPath);
                moved.Add(new PageDeleteUndoEntry(
                    request.SourcePath,
                    trashPath,
                    siblingIndexes[request.SourcePath],
                    request.IsPage,
                    pageSources));
            }
        }
        catch
        {
            RestoreMovedEntries(moved);
            DeleteEmptyTrashRoot(trashRoot);
            throw;
        }

        string statusName = valid.Count == 1
            ? OurPlanCoreJobStore.DisplayName(moved[0].TrashPath)
            : $"{valid.Count} page/folder items";
        return new PageDeleteUndoBatch(job.RootPath, trashRoot, moved, statusName);
    }

    public static IReadOnlyList<PageDeleteRestoreEntry> Restore(
        OurPlanCoreJob job,
        PageDeleteUndoBatch batch)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(batch);

        if (!SamePath(job.RootPath, batch.JobRoot))
            throw new InvalidOperationException("The deleted Pages item belongs to another job.");
        if (batch.Entries.Count == 0)
            return [];
        if (batch.Entries.Any(entry => !Directory.Exists(entry.TrashPath)))
            throw new IOException("The saved Pages undo data is incomplete. Nothing was overwritten.");

        List<RestoreTarget> targets = batch.Entries
            .Select(entry => ResolveRestoreTarget(job, entry))
            .ToList();
        if (targets.Select(target => target.TargetPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Count)
            throw new IOException("Two deleted Pages items resolve to the same restore path. Nothing was overwritten.");

        RestoreTarget? collision = targets.FirstOrDefault(target => Directory.Exists(target.TargetPath));
        if (collision != null)
        {
            throw new IOException(
                $"Cannot restore '{OurPlanCoreJobStore.DisplayName(collision.Entry.TrashPath)}' because " +
                $"'{collision.TargetPath}' already exists. Rename or move the existing item and try Ctrl+Z again.");
        }

        var restored = new List<PageDeleteRestoreEntry>();
        try
        {
            foreach (RestoreTarget target in targets)
            {
                Directory.CreateDirectory(target.TargetParent);
                JobWriteAccess.Demand(target.Entry.TrashPath, "restore a deleted page");
                JobWriteAccess.Demand(target.TargetPath, "restore a deleted page");
                Directory.Move(target.Entry.TrashPath, target.TargetPath);
                var restoredEntry = new PageDeleteRestoreEntry(
                    target.Entry.OriginalPath,
                    target.TargetPath,
                    target.Entry.OriginalSiblingIndex,
                    target.Entry.IsPage);
                restored.Add(restoredEntry);
                if (!SamePath(target.Entry.OriginalPath, target.TargetPath) && target.Entry.PageSources.Count > 0)
                    PageStore.RewritePageSources(target.TargetPath, target.Entry.PageSources);
            }

            RestoreSiblingOrder(restored);
            DeleteEmptyTrashRoot(batch.TrashRoot);
            return restored;
        }
        catch
        {
            MoveRestoredEntriesBack(restored, batch);
            throw;
        }
    }

    private static List<PageDeleteUndoRequest> NormalizeRequests(
        OurPlanCoreJob job,
        IEnumerable<PageDeleteUndoRequest> requests)
    {
        string pagesRoot = Normalize(job.PagesRoot);
        var distinct = requests
            .Where(request => !string.IsNullOrWhiteSpace(request.SourcePath))
            .Select(request => request with { SourcePath = Normalize(request.SourcePath) })
            .Where(request => Directory.Exists(request.SourcePath))
            .Where(request => IsSameOrDescendant(pagesRoot, request.SourcePath) && !SamePath(pagesRoot, request.SourcePath))
            .GroupBy(request => request.SourcePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(request => request.SourcePath.Length)
            .ToList();

        var roots = new List<PageDeleteUndoRequest>();
        foreach (PageDeleteUndoRequest request in distinct)
        {
            if (roots.Any(root => IsSameOrDescendant(root.SourcePath, request.SourcePath)))
                continue;
            roots.Add(request);
        }

        return roots;
    }

    private static Dictionary<string, int> BuildSiblingIndexes(IEnumerable<PageDeleteUndoRequest> requests)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, PageDeleteUndoRequest> group in requests.GroupBy(
                     request => Path.GetDirectoryName(request.SourcePath) ?? "",
                     StringComparer.OrdinalIgnoreCase))
        {
            List<string> siblings = OurPlanCoreJobStore.GetOrderedChildDirectories(group.Key).ToList();
            foreach (PageDeleteUndoRequest request in group)
            {
                int index = siblings.FindIndex(path => SamePath(path, request.SourcePath));
                result[request.SourcePath] = index >= 0 ? index : siblings.Count;
            }
        }

        return result;
    }

    private static RestoreTarget ResolveRestoreTarget(OurPlanCoreJob job, PageDeleteUndoEntry entry)
    {
        string originalParent = Path.GetDirectoryName(entry.OriginalPath) ?? "";
        string targetParent = Directory.Exists(originalParent) &&
                              IsSameOrDescendant(job.PagesRoot, originalParent)
            ? originalParent
            : job.PagesRoot;
        string leaf = Path.GetFileName(entry.OriginalPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(leaf))
            leaf = entry.IsPage ? "Restored Page" : "Restored Folder";
        return new RestoreTarget(entry, targetParent, Path.Combine(targetParent, leaf));
    }

    private static void RestoreSiblingOrder(IReadOnlyList<PageDeleteRestoreEntry> restored)
    {
        foreach (IGrouping<string, PageDeleteRestoreEntry> group in restored.GroupBy(
                     entry => Path.GetDirectoryName(entry.RestoredPath) ?? "",
                     StringComparer.OrdinalIgnoreCase))
        {
            HashSet<string> restoredPaths = group
                .Select(entry => entry.RestoredPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            List<string> ordered = OurPlanCoreJobStore.GetOrderedChildDirectories(group.Key)
                .Where(path => !restoredPaths.Contains(path))
                .ToList();

            foreach (PageDeleteRestoreEntry entry in group.OrderBy(entry => entry.OriginalSiblingIndex))
            {
                int index = Math.Clamp(entry.OriginalSiblingIndex, 0, ordered.Count);
                ordered.Insert(index, entry.RestoredPath);
            }

            for (int i = 0; i < ordered.Count; i++)
                OurPlanCoreJobStore.SetOrderIndex(ordered[i], i + 1);
        }
    }

    private static string CreateTrashRoot(OurPlanCoreJob job)
    {
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture);
        string suffix = Guid.NewGuid().ToString("N")[..8];
        string root = Path.Combine(job.RootPath, ".undo", "pages", $"{stamp}_{suffix}");
        JobWriteAccess.Demand(root, "create page undo trash");
        Directory.CreateDirectory(root);
        return root;
    }

    private static string UniqueTrashPath(string trashRoot, string sourcePath, int index)
    {
        string leaf = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(leaf))
            leaf = "page";
        string name = $"{index + 1:D3}_{OurPlanCoreJobStore.SanitizeName(leaf, 80)}";
        return OurPlanCoreJobStore.UniqueDirectoryPath(Path.Combine(trashRoot, name));
    }

    private static void RestoreMovedEntries(IEnumerable<PageDeleteUndoEntry> moved)
    {
        foreach (PageDeleteUndoEntry entry in moved.Reverse())
        {
            if (!Directory.Exists(entry.TrashPath) || Directory.Exists(entry.OriginalPath))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(entry.OriginalPath) ?? "");
            Directory.Move(entry.TrashPath, entry.OriginalPath);
        }
    }

    private static void MoveRestoredEntriesBack(
        IEnumerable<PageDeleteRestoreEntry> restored,
        PageDeleteUndoBatch batch)
    {
        foreach (PageDeleteRestoreEntry restoredEntry in restored.Reverse())
        {
            PageDeleteUndoEntry original = batch.Entries.First(entry =>
                SamePath(entry.OriginalPath, restoredEntry.OriginalPath));
            if (!Directory.Exists(restoredEntry.RestoredPath) || Directory.Exists(original.TrashPath))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(original.TrashPath) ?? batch.TrashRoot);
            Directory.Move(restoredEntry.RestoredPath, original.TrashPath);
        }
    }

    private static void DeleteEmptyTrashRoot(string trashRoot)
    {
        if (!string.IsNullOrWhiteSpace(trashRoot) &&
            Directory.Exists(trashRoot) &&
            !Directory.EnumerateFileSystemEntries(trashRoot).Any())
        {
            Directory.Delete(trashRoot);
        }
    }

    private static bool IsSameOrDescendant(string root, string path)
    {
        string normalizedRoot = Normalize(root);
        string normalizedPath = Normalize(path);
        return SamePath(normalizedRoot, normalizedPath) ||
               normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private sealed record RestoreTarget(
        PageDeleteUndoEntry Entry,
        string TargetParent,
        string TargetPath);
}
