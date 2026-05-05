using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OurPlaneCore;

public sealed class JobRecoveryLockInfo
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("job_root")]
    public string JobRoot { get; set; } = "";

    [JsonPropertyName("process_id")]
    public int ProcessId { get; set; }

    [JsonPropertyName("machine")]
    public string Machine { get; set; } = "";

    [JsonPropertyName("created_at_utc")]
    public string CreatedAtUtc { get; set; } = "";
}

public static class JobRecoveryService
{
    public const int DefaultMaxSnapshots = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly HashSet<string> SnapshotFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Data.xml",
        "source.json",
        "source_pdf.json",
        "layers.json",
        "annotations.json",
        "measurements.json",
        "folder_properties.json",
    };

    public static string SnapshotRoot(OurPlaneCoreJob job) =>
        Path.Combine(job.RootPath, ".snapshots");

    public static string LockPath(OurPlaneCoreJob job) =>
        Path.Combine(job.RootPath, ".~lock");

    public static string NormalizeSnapshotReason(string? reason)
    {
        string clean = string.IsNullOrWhiteSpace(reason) ? "snapshot" : reason.Trim().ToLowerInvariant();
        clean = new string(clean.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        while (clean.Contains("__", StringComparison.Ordinal))
            clean = clean.Replace("__", "_", StringComparison.Ordinal);
        clean = clean.Trim('_');
        return string.IsNullOrWhiteSpace(clean) ? "snapshot" : clean;
    }

    public static bool ShouldCopySnapshotFile(string path) =>
        SnapshotFileNames.Contains(Path.GetFileName(path));

    public static string SaveSnapshot(
        OurPlaneCoreJob job,
        string reason,
        int maxSnapshots = DefaultMaxSnapshots)
    {
        string snapshotRoot = SnapshotRoot(job);
        Directory.CreateDirectory(snapshotRoot);

        string snapshotName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{NormalizeSnapshotReason(reason)}";
        string snapshotPath = UniqueDirectoryPath(Path.Combine(snapshotRoot, snapshotName));
        Directory.CreateDirectory(snapshotPath);

        CopyIfExists(Path.Combine(job.RootPath, "Data.xml"), Path.Combine(snapshotPath, "Data.xml"));
        CopyMetadataDirectory(job.PagesRoot, Path.Combine(snapshotPath, "Pages"));
        CopyMetadataDirectory(job.TakeoffsRoot, Path.Combine(snapshotPath, "Takeoffs"));
        WriteManifest(job, snapshotPath, reason);
        PruneSnapshots(job, maxSnapshots);
        return snapshotPath;
    }

    public static void WriteLock(OurPlaneCoreJob job)
    {
        var info = new JobRecoveryLockInfo
        {
            JobRoot = job.RootPath,
            ProcessId = Environment.ProcessId,
            Machine = Environment.MachineName,
            CreatedAtUtc = DateTime.UtcNow.ToString("O"),
        };

        File.WriteAllText(LockPath(job), JsonSerializer.Serialize(info, JsonOptions));
    }

    public static bool TryReadLock(OurPlaneCoreJob job, out JobRecoveryLockInfo info)
    {
        info = new JobRecoveryLockInfo();
        string path = LockPath(job);
        if (!File.Exists(path))
            return false;

        try
        {
            info = JsonSerializer.Deserialize<JobRecoveryLockInfo>(File.ReadAllText(path)) ?? new JobRecoveryLockInfo();
            return true;
        }
        catch
        {
            info = new JobRecoveryLockInfo
            {
                JobRoot = job.RootPath,
                CreatedAtUtc = File.GetLastWriteTimeUtc(path).ToString("O"),
            };
            return true;
        }
    }

    public static bool IsStaleLock(JobRecoveryLockInfo info) =>
        info.ProcessId <= 0 || info.ProcessId != Environment.ProcessId || !IsProcessRunning(info.ProcessId);

    public static void ClearLock(OurPlaneCoreJob job)
    {
        string path = LockPath(job);
        if (File.Exists(path))
            File.Delete(path);
    }

    private static void WriteManifest(OurPlaneCoreJob job, string snapshotPath, string reason)
    {
        var manifest = new
        {
            schema_version = 1,
            job_root = job.RootPath,
            job_name = job.Name,
            reason,
            created_at_utc = DateTime.UtcNow.ToString("O"),
            copied = new[] { "Data.xml", "Pages metadata", "Takeoffs metadata and measurements" },
            excluded = new[] { "source PDFs", "rendered images", "AI crop images", "build output" },
        };

        File.WriteAllText(
            Path.Combine(snapshotPath, "snapshot_manifest.json"),
            JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private static void CopyMetadataDirectory(string source, string destination)
    {
        if (!Directory.Exists(source))
            return;

        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            if (ShouldCopySnapshotFile(file))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (string directory in Directory.EnumerateDirectories(source))
        {
            string name = Path.GetFileName(directory);
            if (name.Equals(".snapshots", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("obj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            CopyMetadataDirectory(directory, Path.Combine(destination, name));
        }
    }

    private static void CopyIfExists(string source, string destination)
    {
        if (!File.Exists(source))
            return;

        string? dir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
        File.Copy(source, destination, overwrite: true);
    }

    private static void PruneSnapshots(OurPlaneCoreJob job, int maxSnapshots)
    {
        if (maxSnapshots <= 0)
            return;

        string root = SnapshotRoot(job);
        if (!Directory.Exists(root))
            return;

        var snapshots = Directory.EnumerateDirectories(root)
            .Select(path => new DirectoryInfo(path))
            .OrderByDescending(info => info.Name, StringComparer.OrdinalIgnoreCase)
            .Skip(maxSnapshots)
            .ToList();

        foreach (DirectoryInfo snapshot in snapshots)
            snapshot.Delete(recursive: true);
    }

    private static string UniqueDirectoryPath(string path)
    {
        if (!Directory.Exists(path))
            return path;

        for (int i = 2; ; i++)
        {
            string candidate = $"{path}_{i}";
            if (!Directory.Exists(candidate))
                return candidate;
        }
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }
}
