using System.IO;

namespace OurPlanCore;

internal static partial class NodeStore
{
    internal sealed record SortResult(int FolderCount, IReadOnlyList<string> ChangedParents);

    public static SortResult SortTakeoffTree(string parentFolder, bool walls)
    {
        var parents = Directory.EnumerateDirectories(parentFolder, "*", SearchOption.AllDirectories)
            .Prepend(parentFolder).ToList();
        IComparer<string> comparer = walls ? TakeoffWallNameComparer.Instance : TakeoffDetailSheetNameComparer.Instance;
        return new SortResult(parents.Count, ApplySortedFolders(parentFolder, parents, descending: false, comparer));
    }

    private static IReadOnlyList<string> ApplySortedFolders(string scope, IEnumerable<string> parents, bool descending, IComparer<string> comparer)
    {
        var plans = new List<List<string>>();
        var changed = new List<string>();
        var changedParents = new List<string>();
        foreach (string parent in parents)
        {
            var children = Directory.EnumerateDirectories(parent)
                .OrderBy(OurPlanCoreJobStore.DisplayName, comparer).ToList();
            if (descending) children.Reverse();
            var changedChildren = children.Where((folder, index) => GetOrderIndex(folder) != index + 1).ToList();
            if (changedChildren.Count == 0) continue;
            plans.Add(children);
            changed.AddRange(changedChildren);
            changedParents.Add(parent);
        }
        if (changed.Count == 0) return [];
        // One transaction for the entire user command; undo restores all floors.
        using var operation = JobOperationJournal.BeginOrder(scope, changed);
        foreach (var children in plans) ApplySiblingOrder(children);
        operation.Commit();
        return changedParents;
    }
}
