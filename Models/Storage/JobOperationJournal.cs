using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Collections.Concurrent;

namespace OurPlanCore;

/// <summary>Durable before-images and move intents for recoverable bulk mutations.</summary>
internal sealed partial class JobOperationJournal : IDisposable
{
    private static readonly AsyncLocal<JobOperationJournal?> Current = new();
    private static readonly ConcurrentDictionary<string, byte> ActiveRoots = new(StringComparer.OrdinalIgnoreCase);
    private readonly JobOperationJournal? _parent;
    private readonly bool _owns;
    private bool _finished;
    private string _root = "";
    private string _directory = "";
    private OperationRecord _record = new();
    private IDisposable? _activity;
    private bool _registered;
    public Action? AfterRollback { get; set; }
    public static bool IsBusyForCaller(string root) => ActiveRoots.ContainsKey(root) &&
        !string.Equals(Current.Value?._root, root, StringComparison.OrdinalIgnoreCase);
    internal static Action<string>? FailureInjectionForTests { get; set; }
    internal static void AbandonForTests()
    {
        if (Current.Value is { } operation)
        {
            operation._activity?.Dispose();
            ActiveRoots.TryRemove(operation._root, out _);
        }
        Current.Value = null; FailureInjectionForTests = null;
    }

    private JobOperationJournal() { }

    public static JobOperationJournal BeginForPath(string path, string label, string kind = "operation")
    {
        string? root = FindJobRoot(path);
        if (root == null) return new JobOperationJournal();
        if (kind == "page-sort" && !SafeJobPathResolver.Inside(Path.Combine(root, "Pages"), Path.GetFullPath(path)))
            kind = "operation";
        return Begin(root, label, kind);
    }

    public static JobOperationJournal Begin(string root, string label, string kind = "operation")
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (Current.Value is { } current)
        {
            if (!root.Equals(current._root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A bulk operation cannot switch jobs.");
            return new JobOperationJournal();
        }
        JobWriteAccess.Demand(root, label);
        return new JobOperationJournal(root, label, kind);
    }

    private JobOperationJournal(string root, string label, string kind)
    {
        _root = root;
        _owns = true;
        _parent = Current.Value;
        _activity = JobFileWriteActivity.BeginBulkWrite();
        try
        {
        if (!ActiveRoots.TryAdd(root, 0)) throw new IOException("Another bulk operation is still running for this project.");
        _registered = true;
        if (HasPending(root)) throw new IOException("Reopen the project to recover its interrupted operation before making another change.");
        _directory = SafeJobPathResolver.ResolveRelative(root, ".undo/operations/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_directory, "before"));
        _record = new OperationRecord { Label = label, Kind = kind, StartedUtc = DateTime.UtcNow };
        _record.Before = Capture(root, copyMetadata: true);
        _record.State = "pending";
        Persist(); // A durable manifest exists before the first user-data mutation.
        IoUtil.WriteAllTextAtomic(PendingPath(root), Path.GetFileName(_directory));
        Current.Value = this;
        FailureInjectionForTests?.Invoke("prepared");
        }
        catch
        {
            _activity.Dispose(); Current.Value = _parent;
            if (_registered) ActiveRoots.TryRemove(root, out _);
            throw;
        }
    }

    public static void RecordMove(string source, string destination)
    {
        if (Current.Value is not { } operation) return;
        string a = operation.Relative(source);
        string b = operation.Relative(destination);
        operation._record.Moves.Add(new MoveIntent { Source = a, Destination = b });
        operation.Persist(); // Intent precedes Directory.Move, including crash between those calls.
        FailureInjectionForTests?.Invoke("before-move");
    }

    public void Commit()
    {
        if (!_owns || _finished) return;
        FailureInjectionForTests?.Invoke("before-commit");
        _record.After = Capture(_root, copyMetadata: false);
        _record.State = "committed";
        Persist();
        ClearPending();
        _finished = true;
    }

    public void Dispose()
    {
        if (!_owns) return;
        bool restored = false;
        try
        {
            if (!_finished)
            {
                Restore(checkConflicts: false);
                _record.State = "rolled-back";
                Persist();
                ClearPending();
                restored = true;
            }
        }
        finally { Current.Value = _parent; _activity?.Dispose(); ActiveRoots.TryRemove(_root, out _); }
        if (restored) AfterRollback?.Invoke();
    }

    public static void RecoverPending(string root)
    {
        if (Current.Value != null) return;
        if (!HasPending(root)) return;
        string id = File.ReadAllText(SafeJobPathResolver.ResolveRelative(root, ".undo/operations/pending-operation.txt")).Trim();
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid active-operation reference.");
        string manifest = SafeJobPathResolver.ResolveRelative(root, ".undo/operations/" + id + "/operation.json");
        JobOperationJournal operation = Load(root, manifest);
        if (operation._record.State is "pending" or "undo-pending")
        {
            JobWriteAccess.Demand(root, "recover interrupted project operation");
            operation.Restore(checkConflicts: operation._record.State == "undo-pending", validateConflicts: false);
            operation._record.State = "recovered";
            operation.Persist();
            AppLog.Warn($"Recovered interrupted project operation: {operation._record.Label}");
        }
        operation.ClearPending();
    }

    public static bool HasPending(string root) => File.Exists(PendingPath(root));
    private static string PendingPath(string root) => Path.Combine(root, ".undo", "operations", "pending-operation.txt");
    private void ClearPending()
    {
        string path = SafeJobPathResolver.ResolveRelative(_root, ".undo/operations/pending-operation.txt");
        if (File.Exists(path) && File.ReadAllText(path).Trim() == Path.GetFileName(_directory)) File.Delete(path);
    }

    public static bool HasUndo(string root, string? kind = null) =>
        LastCommitted(root, kind) != null;

    public static string UndoLast(string root, string? kind = null)
    {
        if (Current.Value != null) throw new InvalidOperationException("Finish the active operation before undo.");
        if (HasPending(root) || ActiveRoots.ContainsKey(root))
            throw new IOException("Finish or recover the pending operation before undo.");
        JobWriteAccess.Demand(root, "undo project operation");
        using var activity = JobFileWriteActivity.BeginBulkWrite();
        JobOperationJournal operation = LastCommitted(root, kind) ?? throw new InvalidOperationException("No saved operation is available to undo.");
        operation.ValidateUndo(operation._record.Before.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase),
            (operation._record.After?.Files ?? []).ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase));
        operation._record.State = "undo-pending";
        operation.Persist();
        IoUtil.WriteAllTextAtomic(PendingPath(root), Path.GetFileName(operation._directory));
        FailureInjectionForTests?.Invoke("before-undo");
        operation.Restore(checkConflicts: true, validateConflicts: false);
        operation._record.State = "undone";
        operation.Persist();
        operation.ClearPending();
        return operation._record.Label;
    }

    private static JobOperationJournal? LastCommitted(string root, string? kind)
    {
        foreach (string path in ManifestPaths(root).OrderByDescending(File.GetCreationTimeUtc))
        {
            JobOperationJournal operation = Load(root, path);
            if (operation._record.State == "committed" && (kind == null || operation._record.Kind == kind)) return operation;
        }
        return null;
    }

    private static IEnumerable<string> ManifestPaths(string root)
    {
        string folder = SafeJobPathResolver.ResolveRelative(root, ".undo/operations");
        if (!Directory.Exists(folder)) return [];
        return Directory.EnumerateDirectories(folder).Select(dir => Path.Combine(dir, "operation.json")).Where(File.Exists).ToArray();
    }

    private static JobOperationJournal Load(string root, string manifest)
    {
        manifest = SafeJobPathResolver.ResolveInside(root, manifest, root);
        if (new FileInfo(manifest).Length > 64 * 1024 * 1024)
            throw new InvalidDataException("The recovery manifest is too large.");
        var record = JsonSerializer.Deserialize<OperationRecord>(File.ReadAllText(manifest)) ?? throw new InvalidDataException("Invalid recovery manifest.");
        if (record.Version != 1) throw new InvalidDataException("Unsupported recovery manifest version.");
        var operation = new JobOperationJournal { _root = Path.GetFullPath(root), _directory = Path.GetDirectoryName(manifest)!, _record = record };
        // Validate every reference before performing even the first restoration step.
        foreach (FileRecord entry in record.Before.Files.Concat(record.After?.Files ?? []))
        {
            _ = operation.Resolve(entry.Path);
            if (!string.IsNullOrEmpty(entry.Backup))
                _ = SafeJobPathResolver.ResolveRelative(operation._directory, entry.Backup);
        }
        foreach (string dir in record.Before.Directories.Concat(record.After?.Directories ?? [])) _ = operation.Resolve(dir);
        foreach (MoveIntent move in record.Moves) { _ = operation.Resolve(move.Source); _ = operation.Resolve(move.Destination); }
        return operation;
    }

    private Inventory Capture(string root, bool copyMetadata)
    {
        var result = new Inventory();
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            foreach (FileSystemInfo child in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                if ((child.Attributes & FileAttributes.ReparsePoint) != 0 && !OurPlanReparsePointPolicy.IsAllowedCloudItem(child))
                    throw new InvalidDataException("Cannot journal a project containing a symbolic link or junction.");
                string relative = Relative(child.FullName);
                if (Excluded(relative)) continue;
                if (child is DirectoryInfo)
                {
                    result.Directories.Add(relative); pending.Push(child.FullName); continue;
                }
                if (child is not FileInfo file) continue;
                bool metadata = IsMetadata(file.Name);
                var entry = new FileRecord { Path = relative, Length = file.Length, Metadata = metadata };
                if (metadata)
                {
                    entry.Hash = Hash(file.FullName);
                    if (copyMetadata)
                    {
                        entry.Backup = "before/" + result.Files.Count + ".bin";
                        File.Copy(file.FullName, Path.Combine(_directory, entry.Backup));
                        if (Hash(Path.Combine(_directory, entry.Backup)) != entry.Hash)
                            throw new IOException("A project file changed while its recovery snapshot was being captured.");
                        if (Path.GetExtension(file.Name).Equals(".json", StringComparison.OrdinalIgnoreCase))
                        {
                            using JsonDocument validated = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, entry.Backup)));
                        }
                    }
                }
                result.Files.Add(entry);
            }
        }
        return result;
    }

    private void Persist() => IoUtil.WriteAllTextAtomic(Path.Combine(_directory, "operation.json"), JsonSerializer.Serialize(_record));
    private string Relative(string path)
    {
        string full = Path.GetFullPath(path);
        if (!SafeJobPathResolver.Inside(_root, full)) throw new InvalidDataException("Operation path escapes the job.");
        return Path.GetRelativePath(_root, full).Replace('\\', '/');
    }
    private string Resolve(string relative)
    {
        string path = SafeJobPathResolver.ResolveRelative(_root, relative);
        if (Excluded(relative) || path.Equals(_root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A recovery entry targets reserved project storage.");
        return path;
    }
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    private static bool IsMetadata(string name) => new[] { ".json", ".xml", ".jsonl", ".txt" }.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
    private static bool Excluded(string relative)
    {
        string first = relative.Split('/')[0];
        return first.Equals(".undo", StringComparison.OrdinalIgnoreCase) || first.Equals(".snapshots", StringComparison.OrdinalIgnoreCase) ||
            first.StartsWith(".~", StringComparison.OrdinalIgnoreCase) || first.StartsWith(".ourplan", StringComparison.OrdinalIgnoreCase);
    }
    private static string? FindJobRoot(string path)
    {
        string? dir = Path.GetFullPath(path);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "Pages")) && Directory.Exists(Path.Combine(dir, "Takeoffs"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    internal sealed class OperationRecord
    {
        public int Version { get; set; } = 1;
        public string Label { get; set; } = "";
        public string Kind { get; set; } = "operation";
        public string State { get; set; } = "preparing";
        public DateTime StartedUtc { get; set; }
        public Inventory Before { get; set; } = new();
        public Inventory? After { get; set; }
        public List<MoveIntent> Moves { get; set; } = [];
    }
    internal sealed class Inventory
    {
        public List<string> Directories { get; set; } = [];
        public List<FileRecord> Files { get; set; } = [];
    }
    internal sealed class FileRecord
    {
        public string Path { get; set; } = "";
        public long Length { get; set; }
        public bool Metadata { get; set; }
        public string Hash { get; set; } = "";
        public string Backup { get; set; } = "";
    }
    internal sealed class MoveIntent
    {
        public string Source { get; set; } = "";
        public string Destination { get; set; } = "";
    }
}
