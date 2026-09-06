using System.IO;

namespace OurPlanCore;

internal sealed partial class JobOperationJournal
{
    private void Restore(bool checkConflicts, bool validateConflicts = true)
    {
        var before = _record.Before.Files.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        var after = (_record.After?.Files ?? []).ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        if (checkConflicts && validateConflicts) ValidateUndo(before, after);
        // Validate all original bytes before moving a directory or replacing any metadata.
        foreach (FileRecord entry in before.Values.Where(entry => entry.Metadata))
        {
            string backup = SafeJobPathResolver.ResolveRelative(_directory, entry.Backup);
            if (!File.Exists(backup) || Hash(backup) != entry.Hash)
                throw new InvalidDataException("A recovery backup is missing or has changed: " + entry.Path);
        }

        for (int i = _record.Moves.Count - 1; i >= 0; i--)
        {
            MoveIntent move = _record.Moves[i];
            string source = Resolve(move.Source), destination = Resolve(move.Destination);
            if (!Directory.Exists(destination)) continue; // Intent never executed, or already reversed.
            if (Directory.Exists(source))
                throw new IOException("Recovery cannot replace an occupied source folder: " + move.Source);
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            Directory.Move(destination, source);
        }

        Inventory current = Capture(_root, copyMetadata: false);
        HashSet<string> addedByOperation = after.Keys.Select(ReverseMovedPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (FileRecord entry in current.Files)
        {
            if (before.ContainsKey(entry.Path)) continue;
            if (checkConflicts && !addedByOperation.Contains(entry.Path)) continue;
            PreserveExtraFile(Resolve(entry.Path));
        }

        foreach (string directory in _record.Before.Directories.OrderBy(path => path.Length))
            Directory.CreateDirectory(Resolve(directory));
        foreach (FileRecord entry in before.Values.Where(entry => entry.Metadata))
        {
            if (checkConflicts && after.TryGetValue(entry.Path, out FileRecord? unchanged) && unchanged.Hash == entry.Hash)
                continue;
            string target = Resolve(entry.Path);
            if (File.Exists(target) && Hash(target) == entry.Hash) continue;
            if (File.Exists(target)) PreserveExtraFile(target);
            string backup = SafeJobPathResolver.ResolveRelative(_directory, entry.Backup);
            IoUtil.WriteStreamAtomic(target, output => { using var input = File.OpenRead(backup); input.CopyTo(output); });
        }

        HashSet<string> originalDirectories = _record.Before.Directories.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string relative in current.Directories.OrderByDescending(path => path.Length))
        {
            if (originalDirectories.Contains(relative)) continue;
            string path = Resolve(relative);
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
        }
        OurPlanCoreJobStore.ClearMetadataCache();
    }

    private void ValidateUndo(IReadOnlyDictionary<string, FileRecord> before, IReadOnlyDictionary<string, FileRecord> after)
    {
        if (_record.After == null) throw new InvalidDataException("No completed-operation state is available.");
        foreach (FileRecord entry in after.Values)
        {
            bool changed = !before.TryGetValue(entry.Path, out FileRecord? original) ||
                entry.Hash != original.Hash || entry.Length != original.Length;
            if (!changed) continue;
            string path = Resolve(entry.Path);
            if (!File.Exists(path) || (entry.Metadata && Hash(path) != entry.Hash))
                throw new IOException("This operation cannot be undone because a later edit changed " + entry.Path + ". Its original snapshot is retained.");
        }
        foreach (string removed in before.Keys.Except(after.Keys, StringComparer.OrdinalIgnoreCase))
            if (File.Exists(Resolve(removed)))
                throw new IOException("Undo would overwrite a file created after the operation: " + removed);
    }

    private string ReverseMovedPath(string path)
    {
        for (int i = _record.Moves.Count - 1; i >= 0; i--)
        {
            MoveIntent move = _record.Moves[i];
            if (path.Equals(move.Destination, StringComparison.OrdinalIgnoreCase)) path = move.Source;
            else if (path.StartsWith(move.Destination + "/", StringComparison.OrdinalIgnoreCase))
                path = move.Source + path[move.Destination.Length..];
        }
        return path;
    }

    private void PreserveExtraFile(string path)
    {
        string relative = Relative(path);
        string preserved = Path.Combine(_directory, "displaced", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(preserved)!);
        // Move rather than delete: interrupted imports and later file contents remain recoverable.
        File.Move(path, preserved);
        File.WriteAllText(preserved + ".path.txt", relative);
    }
}
