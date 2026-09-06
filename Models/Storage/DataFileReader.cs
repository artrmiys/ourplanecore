using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace OurPlanCore;

internal enum DataFileState { Missing, Valid, Corrupt, Unreadable }

internal sealed record DataFileResult<T>(DataFileState State, T? Value, string Path, string Error = "")
{
    public bool IsValid => State == DataFileState.Valid;
}

internal sealed record ProtectedDataFile(string Path, DataFileState State, string Error);

/// <summary>Read failures never grant permission to overwrite an existing document.</summary>
internal static class DataFileReader
{
    private static readonly ConcurrentDictionary<string, ProtectedDataFile> Protected =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Action<string>> RecoveryValidators =
        new(StringComparer.OrdinalIgnoreCase);
    private const string MarkerSuffix = ".read-protected";
    [ThreadStatic] private static string? _recoveryPath;

    public static DataFileResult<T> Read<T>(string path, Func<string, T> parse) where T : class
    {
        path = System.IO.Path.GetFullPath(path);
        LoadMarker(path);
        if (Protected.ContainsKey(path)) RecoveryValidators[path] = json => _ = parse(json);
        try
        {
            string? root = JobWriteAccess.RegisteredRootForPath(path);
            if (root != null) _ = SafeJobPathResolver.ResolveInside(root, path, root);
            // Do not use File.Exists: it also returns false for access and sharing errors.
            string json = File.ReadAllText(path);
            T value = parse(json) ?? throw new JsonException("The document contains null instead of data.");
            return new(DataFileState.Valid, value, path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            if (Protected.TryGetValue(path, out ProtectedDataFile? issue))
                return new(issue.State, null, path, issue.Error);
            return new(DataFileState.Missing, null, path);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidDataException)
        {
            // Quarantine only parse failures, and preserve the existing quarantine contract.
            if (!Protected.ContainsKey(path))
                OurPlanCoreJobStore.QuarantineCorruptJson(path, "Read project data", ex);
            Protect(path, DataFileState.Corrupt, ex.Message);
            RecoveryValidators[path] = json => _ = parse(json);
            return new(DataFileState.Corrupt, null, path, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Protect(path, DataFileState.Unreadable, ex.Message);
            RecoveryValidators[path] = json => _ = parse(json);
            return new(DataFileState.Unreadable, null, path, ex.Message);
        }
    }

    public static DataFileResult<T> ReadJson<T>(string path) where T : class =>
        Read(path, json => JsonSerializer.Deserialize<T>(json) ?? throw new JsonException("Empty document."));

    public static IReadOnlyList<ProtectedDataFile> Issues(string root) =>
        Protected.Values.Where(issue => Inside(root, issue.Path)).OrderBy(issue => issue.Path).ToArray();

    public static bool IsProtected(string path) => FindIssue(path) != null;

    public static void Demand(string path, string operation)
    {
        ProtectedDataFile? issue = FindIssue(path);
        if (issue != null)
            throw new IOException($"Cannot {operation}: '{issue.Path}' is protected after a read failure. Use Project Data Recovery to retry or restore it.");
    }

    private static ProtectedDataFile? FindIssue(string path)
    {
        if (Protected.IsEmpty || path.EndsWith(MarkerSuffix, StringComparison.OrdinalIgnoreCase))
            return null;
        string full = System.IO.Path.GetFullPath(path);
        foreach (ProtectedDataFile issue in Protected.Values)
        {
            if (string.Equals(_recoveryPath, issue.Path, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(full, issue.Path, StringComparison.OrdinalIgnoreCase) ||
                Inside(full, issue.Path) ||
                (string.Equals(System.IO.Path.GetFileName(full), "Data.xml", StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(System.IO.Path.GetDirectoryName(full), System.IO.Path.GetDirectoryName(issue.Path), StringComparison.OrdinalIgnoreCase)))
                return issue;
        }
        return null;
    }

    private static void Protect(string path, DataFileState state, string error)
    {
        var issue = new ProtectedDataFile(path, state, error);
        bool first = Protected.TryAdd(path, issue);
        if (!first) return;
        AppLog.Warn($"Protected project data ({state}): {path}: {error}");
        try
        {
            // Persist the reason across restarts even after the corrupt original was quarantined.
            IoUtil.WriteAllTextAtomic(path + MarkerSuffix, JsonSerializer.Serialize(new { state, error }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn(ex, $"Could not persist read protection for {path}; original data remains untouched.");
        }
    }

    private static void LoadMarker(string path)
    {
        if (Protected.ContainsKey(path) || !File.Exists(path + MarkerSuffix)) return;
        try
        {
            string? root = JobWriteAccess.RegisteredRootForPath(path);
            if (root != null) _ = SafeJobPathResolver.ResolveInside(root, path + MarkerSuffix, root);
            using JsonDocument marker = JsonDocument.Parse(File.ReadAllText(path + MarkerSuffix));
            var state = (DataFileState)marker.RootElement.GetProperty("state").GetInt32();
            Protected.TryAdd(path, new(path, state, marker.RootElement.GetProperty("error").GetString() ?? "Previous read failure."));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            Protected.TryAdd(path, new(path, DataFileState.Unreadable, "Read-protection record is unavailable: " + ex.Message));
        }
    }

    public static void RestoreOrRetry(string path, string? restoreFrom = null)
    {
        path = System.IO.Path.GetFullPath(path);
        if (!Protected.ContainsKey(path)) throw new InvalidOperationException("This file is not protected.");
        string json = File.ReadAllText(restoreFrom ?? path);
        ValidateRecovery(path, json);
        if (RecoveryValidators.TryGetValue(path, out Action<string>? validate)) validate(json);
        string? previous = _recoveryPath;
        _recoveryPath = path;
        try
        {
            JobWriteAccess.Demand(path, "restore protected project data");
            if (restoreFrom != null)
            {
                if (File.Exists(path))
                    File.Copy(path, path + ".before-repair-" + Guid.NewGuid().ToString("N"));
                IoUtil.WriteAllTextAtomic(path, json);
            }
            // Keep a verified copy before any future repair or save can change fields.
            File.Copy(path, path + ".recovered-" + Guid.NewGuid().ToString("N"));
            if (File.Exists(path + MarkerSuffix)) File.Delete(path + MarkerSuffix);
            Protected.TryRemove(path, out _);
            RecoveryValidators.TryRemove(path, out _);
        }
        finally { _recoveryPath = previous; }
    }

    private static void ValidateRecovery(string path, string json)
    {
        string name = System.IO.Path.GetFileName(path).ToLowerInvariant();
        using JsonDocument doc = JsonDocument.Parse(json);
        if (name == "measurements.json")
            _ = TakeoffStore.ParseMeasurementDtos(json);
        else if (name == "source.json")
        {
            SourceInfo? src = JsonSerializer.Deserialize<SourceInfo>(json);
            if (src == null || string.IsNullOrWhiteSpace(src.Pdf) || src.Page < 0)
                throw new JsonException("The restored source must contain a PDF path and valid page index.");
        }
        else if (name is "annotations.json" or "bookmarks.json" && doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new JsonException("The restored document must contain an array.");
    }

    private static bool Inside(string root, string path) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(System.IO.Path.TrimEndingDirectorySeparator(root) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    internal static void ResetForTests() { Protected.Clear(); RecoveryValidators.Clear(); }
}
