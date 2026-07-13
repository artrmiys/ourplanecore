using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OurPlanCore;

public sealed class TreeExpansionState
{
    private readonly HashSet<string> _expandedPaths = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _expandedPaths.Count;

    public IReadOnlyList<string> Snapshot() =>
        _expandedPaths.Order(StringComparer.OrdinalIgnoreCase).ToList();

    public void Clear() =>
        _expandedPaths.Clear();

    public bool Add(string? path)
    {
        string? key = NormalizePathKey(path);
        return key != null && _expandedPaths.Add(key);
    }

    public bool Remove(string? path)
    {
        string? key = NormalizePathKey(path);
        return key != null && _expandedPaths.Remove(key);
    }

    public bool Contains(string? path)
    {
        string? key = NormalizePathKey(path);
        return key != null && _expandedPaths.Contains(key);
    }

    public void ReplaceWith(IEnumerable<string?> paths)
    {
        _expandedPaths.Clear();
        foreach (string? path in paths)
            Add(path);
    }

    public void Rebase(string oldPath, string newPath)
    {
        string? oldKey = NormalizePathKey(oldPath);
        string? newKey = NormalizePathKey(newPath);
        if (oldKey == null ||
            newKey == null ||
            string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase) ||
            _expandedPaths.Count == 0)
        {
            return;
        }

        var rebased = new List<(string OldKey, string NewKey)>();
        foreach (string expandedPath in _expandedPaths)
        {
            if (!IsSameOrDescendant(oldKey, expandedPath))
                continue;

            rebased.Add((expandedPath, RebaseDescendantPath(oldKey, newKey, expandedPath)));
        }

        foreach (var (oldExpandedKey, newExpandedKey) in rebased)
        {
            _expandedPaths.Remove(oldExpandedKey);
            _expandedPaths.Add(newExpandedKey);
        }
    }

    public static string? NormalizePathKey(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? null
            : Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string RebaseDescendantPath(string oldRoot, string newRoot, string path)
    {
        if (string.Equals(oldRoot, path, StringComparison.OrdinalIgnoreCase))
            return newRoot;

        string relative = Path.GetRelativePath(oldRoot, path);
        return Path.GetFullPath(Path.Combine(newRoot, relative));
    }

    private static bool IsSameOrDescendant(string possibleParent, string possibleChild)
    {
        string parent = FullPathWithSeparator(possibleParent);
        string child = FullPathWithSeparator(possibleChild);
        return child.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }

    private static string FullPathWithSeparator(string path)
    {
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full + Path.DirectorySeparatorChar;
    }
}
