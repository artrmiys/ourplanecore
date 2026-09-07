using System.IO;

namespace OurPlanCore;

internal sealed partial class JobOperationJournal
{
    // Reordering changes only Data.xml. Never snapshot sheet caches or measurements
    // for each folder visited by a recursive wall/detail sort.
    public static JobOperationJournal BeginOrder(string parentFolder, IEnumerable<string> changedFolders)
    {
        string? root = FindJobRoot(parentFolder);
        if (root == null) return new JobOperationJournal();
        if (Current.Value is { } current)
        {
            if (!root.Equals(current._root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A bulk operation cannot switch jobs.");
            return new JobOperationJournal();
        }
        var files = changedFolders.Select(folder => Path.Combine(folder, "Data.xml"))
            .Select(file => Path.GetRelativePath(root, SafeJobPathResolver.ResolveInside(root, file, root)).Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0) return new JobOperationJournal();
        JobWriteAccess.Demand(root, "sort folder children");
        string kind = SafeJobPathResolver.Inside(Path.Combine(root, "Pages"), Path.GetFullPath(parentFolder))
            ? "page-sort" : "operation";
        return new JobOperationJournal(root, "Sort folder children", kind, files);
    }

    private Inventory CaptureScopedFiles(bool copyMetadata)
    {
        var result = new Inventory();
        foreach (string relative in _record.ScopedFiles!)
        {
            string path = Resolve(relative);
            if (!File.Exists(path)) continue;
            var entry = new FileRecord
            {
                Path = relative, Length = new FileInfo(path).Length, Metadata = true, Hash = Hash(path),
            };
            if (copyMetadata)
            {
                entry.Backup = "before/" + result.Files.Count + ".bin";
                string backup = Path.Combine(_directory, entry.Backup);
                File.Copy(path, backup);
                if (Hash(backup) != entry.Hash)
                    throw new IOException("Node order changed while its undo snapshot was being captured.");
            }
            result.Files.Add(entry);
        }
        return result;
    }
}
