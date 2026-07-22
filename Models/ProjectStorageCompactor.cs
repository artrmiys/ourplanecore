using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OurPlanCore;

public enum ProjectStorageCompactionStatus
{
    Compacted,
    AlreadyCompact,
    SkippedInvalidJson,
    SkippedMissing,
    SkippedChangedSincePreview,
    Failed,
}

public sealed record ProjectStorageCompactionCandidate(
    string FullPath,
    string RelativePath,
    long CurrentBytes,
    long CompactBytes,
    long PotentialSavingsBytes,
    long LastWriteUtcTicks);

public sealed record ProjectStorageCompactionIssue(
    string RelativePath,
    string Reason);

public sealed class ProjectStorageCompactionPlan
{
    public required string JobRoot { get; init; }

    public required IReadOnlyList<ProjectStorageCompactionCandidate> Candidates { get; init; }

    public required IReadOnlyList<ProjectStorageCompactionIssue> SkippedFiles { get; init; }

    public IReadOnlyList<ProjectStorageCompactionCandidate> Files => Candidates;

    public IReadOnlyList<ProjectStorageCompactionIssue> Warnings => SkippedFiles;

    public long PotentialSavingsBytes => Candidates.Sum(item => item.PotentialSavingsBytes);

    public long TotalPotentialSavingsBytes => PotentialSavingsBytes;

    public int CompactableFileCount => Candidates.Count(item => item.PotentialSavingsBytes > 0);
}

public sealed record ProjectStorageCompactionFileResult(
    string RelativePath,
    ProjectStorageCompactionStatus Status,
    long BytesBefore,
    long BytesAfter,
    long BytesSaved,
    string Message);

public sealed class ProjectStorageCompactionResult
{
    public required string JobRoot { get; init; }

    public required IReadOnlyList<ProjectStorageCompactionFileResult> Files { get; init; }

    public int CompactedFileCount => Files.Count(file => file.Status == ProjectStorageCompactionStatus.Compacted);

    public int CompactedFiles => CompactedFileCount;

    public long BytesSaved => Files.Sum(file => file.BytesSaved);

    public IReadOnlyList<ProjectStorageCompactionFileResult> Errors => Files
        .Where(file => file.Status is ProjectStorageCompactionStatus.Failed or
            ProjectStorageCompactionStatus.SkippedInvalidJson)
        .ToList();

    public bool HasFailures => Files.Any(file => file.Status == ProjectStorageCompactionStatus.Failed);
}

/// <summary>
/// Removes JSON formatting whitespace from raster snap indexes. The compactor
/// never deletes sources or raster images and never writes outside
/// Pages/**/raster/snap.json.
/// </summary>
public static class ProjectStorageCompactor
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    private static readonly JsonWriterOptions CompactWriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    public static ProjectStorageCompactionPlan BuildPlan(OurPlanCoreJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return BuildPlan(job.RootPath);
    }

    public static ProjectStorageCompactionPlan BuildPlan(string jobRoot)
    {
        string root = ResolveExistingRoot(jobRoot);
        var candidates = new List<ProjectStorageCompactionCandidate>();
        var skipped = new List<ProjectStorageCompactionIssue>();

        foreach (string path in EnumerateEligibleSnapFiles(root, skipped))
        {
            string relative = Relative(root, path);
            try
            {
                CompactJsonSnapshot snapshot = ReadCompactSnapshot(path);
                candidates.Add(new ProjectStorageCompactionCandidate(
                    path,
                    relative,
                    snapshot.CurrentBytes,
                    snapshot.CompactBytes,
                    Math.Max(0, snapshot.CurrentBytes - snapshot.CompactBytes),
                    snapshot.LastWriteUtcTicks));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                skipped.Add(new ProjectStorageCompactionIssue(relative, ex.Message));
            }
        }

        return new ProjectStorageCompactionPlan
        {
            JobRoot = root,
            Candidates = candidates
                .OrderBy(candidate => candidate.RelativePath, PathComparer)
                .ToList(),
            SkippedFiles = skipped
                .OrderBy(issue => issue.RelativePath, PathComparer)
                .ToList(),
        };
    }

    public static ProjectStorageCompactionResult Execute(ProjectStorageCompactionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        string root = ResolveExistingRoot(plan.JobRoot);
        var results = plan.SkippedFiles
            .Select(issue => new ProjectStorageCompactionFileResult(
                issue.RelativePath,
                ProjectStorageCompactionStatus.SkippedInvalidJson,
                0,
                0,
                0,
                issue.Reason))
            .ToList();

        foreach (ProjectStorageCompactionCandidate candidate in plan.Candidates)
            results.Add(CompactCandidate(root, candidate));

        return new ProjectStorageCompactionResult
        {
            JobRoot = root,
            Files = results
                .OrderBy(file => file.RelativePath, PathComparer)
                .ToList(),
        };
    }

    public static ProjectStorageCompactionResult Execute(string jobRoot) =>
        Execute(BuildPlan(jobRoot));

    public static ProjectStorageCompactionPlan Preview(OurPlanCoreJob job) => BuildPlan(job);

    public static ProjectStorageCompactionPlan Preview(string jobRoot) => BuildPlan(jobRoot);

    public static ProjectStorageCompactionResult Compact(ProjectStorageCompactionPlan plan) => Execute(plan);

    public static ProjectStorageCompactionResult Compact(string jobRoot) => Execute(jobRoot);

    private static ProjectStorageCompactionFileResult CompactCandidate(
        string root,
        ProjectStorageCompactionCandidate candidate)
    {
        string path;
        try
        {
            path = Path.GetFullPath(candidate.FullPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failed(candidate.RelativePath, candidate.CurrentBytes, ex.Message);
        }

        if (!IsEligibleSnapPath(root, path))
            return Failed(candidate.RelativePath, candidate.CurrentBytes, "Path is outside Pages/**/raster/snap.json.");

        if (!File.Exists(path))
        {
            return new ProjectStorageCompactionFileResult(
                candidate.RelativePath,
                ProjectStorageCompactionStatus.SkippedMissing,
                candidate.CurrentBytes,
                0,
                0,
                "File no longer exists.");
        }

        try
        {
            FileInfo before = new(path);
            if (before.Length != candidate.CurrentBytes ||
                before.LastWriteTimeUtc.Ticks != candidate.LastWriteUtcTicks)
            {
                return Changed(candidate, before.Length);
            }

            CompactJsonSnapshot snapshot = ReadCompactSnapshot(path, includeText: true);
            if (snapshot.CurrentBytes != candidate.CurrentBytes ||
                snapshot.LastWriteUtcTicks != candidate.LastWriteUtcTicks)
            {
                return Changed(candidate, snapshot.CurrentBytes);
            }

            if (snapshot.CompactBytes >= snapshot.CurrentBytes)
            {
                return new ProjectStorageCompactionFileResult(
                    candidate.RelativePath,
                    ProjectStorageCompactionStatus.AlreadyCompact,
                    snapshot.CurrentBytes,
                    snapshot.CurrentBytes,
                    0,
                    "JSON is already compact.");
            }

            FileInfo immediatelyBeforeWrite = new(path);
            if (immediatelyBeforeWrite.Length != snapshot.CurrentBytes ||
                immediatelyBeforeWrite.LastWriteTimeUtc.Ticks != snapshot.LastWriteUtcTicks)
            {
                return Changed(candidate, immediatelyBeforeWrite.Length);
            }

            JobWriteAccess.Demand(path, "compact raster snap JSON");
            IoUtil.WriteAllTextAtomic(path, snapshot.CompactText!);

            long bytesAfter = new FileInfo(path).Length;
            return new ProjectStorageCompactionFileResult(
                candidate.RelativePath,
                ProjectStorageCompactionStatus.Compacted,
                snapshot.CurrentBytes,
                bytesAfter,
                Math.Max(0, snapshot.CurrentBytes - bytesAfter),
                "Formatting whitespace removed; JSON data preserved.");
        }
        catch (JobWriteDeniedException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            return new ProjectStorageCompactionFileResult(
                candidate.RelativePath,
                ProjectStorageCompactionStatus.SkippedInvalidJson,
                candidate.CurrentBytes,
                candidate.CurrentBytes,
                0,
                ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failed(candidate.RelativePath, candidate.CurrentBytes, ex.Message);
        }
    }

    private static ProjectStorageCompactionFileResult Changed(
        ProjectStorageCompactionCandidate candidate,
        long currentBytes) =>
        new(
            candidate.RelativePath,
            ProjectStorageCompactionStatus.SkippedChangedSincePreview,
            candidate.CurrentBytes,
            currentBytes,
            0,
            "File changed after preview; analyze again before compacting.");

    private static ProjectStorageCompactionFileResult Failed(
        string relativePath,
        long currentBytes,
        string message) =>
        new(
            relativePath,
            ProjectStorageCompactionStatus.Failed,
            currentBytes,
            currentBytes,
            0,
            message);

    private static CompactJsonSnapshot ReadCompactSnapshot(string path, bool includeText = false)
    {
        long writeTicksBefore = File.GetLastWriteTimeUtc(path).Ticks;
        using var input = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        long currentBytes = input.Length;
        using JsonDocument document = JsonDocument.Parse(input);
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output, CompactWriterOptions))
            document.RootElement.WriteTo(writer);

        long writeTicksAfter = File.GetLastWriteTimeUtc(path).Ticks;
        if (writeTicksBefore != writeTicksAfter || new FileInfo(path).Length != currentBytes)
            throw new IOException("File changed while it was being analyzed.");

        byte[] compactBytes = output.ToArray();
        return new CompactJsonSnapshot(
            currentBytes,
            compactBytes.LongLength,
            writeTicksAfter,
            includeText ? Encoding.UTF8.GetString(compactBytes) : null);
    }

    private static IEnumerable<string> EnumerateEligibleSnapFiles(
        string root,
        List<ProjectStorageCompactionIssue> skipped)
    {
        string pagesRoot = Path.Combine(root, "Pages");
        if (!Directory.Exists(pagesRoot))
            yield break;

        var pending = new Stack<string>();
        pending.Push(pagesRoot);
        while (pending.Count > 0)
        {
            string folder = pending.Pop();
            string[] files;
            string[] children;
            try
            {
                files = Directory.GetFiles(folder);
                children = Directory.GetDirectories(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped.Add(new ProjectStorageCompactionIssue(Relative(root, folder), ex.Message));
                continue;
            }

            foreach (string file in files)
            {
                string fullPath = Path.GetFullPath(file);
                if (IsEligibleSnapPath(root, fullPath))
                    yield return fullPath;
            }

            foreach (string child in children)
            {
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                        pending.Push(child);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped.Add(new ProjectStorageCompactionIssue(Relative(root, child), ex.Message));
                }
            }
        }
    }

    private static bool IsEligibleSnapPath(string root, string path)
    {
        string pagesRoot = Path.GetFullPath(Path.Combine(root, "Pages"));
        string fullPath = Path.GetFullPath(path);
        if (!IsDescendant(pagesRoot, fullPath) ||
            !Path.GetFileName(fullPath).Equals(
                RasterSheetCacheService.SnapIndexName,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string? rasterFolder = Path.GetDirectoryName(fullPath);
        return !string.IsNullOrWhiteSpace(rasterFolder) &&
            Path.GetFileName(rasterFolder).Equals(
                RasterSheetCacheService.CacheFolderName,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDescendant(string root, string path)
    {
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveExistingRoot(string jobRoot)
    {
        if (string.IsNullOrWhiteSpace(jobRoot))
            throw new ArgumentException("Job root is required.", nameof(jobRoot));

        string root = Path.GetFullPath(jobRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);
        return root;
    }

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path);

    private sealed record CompactJsonSnapshot(
        long CurrentBytes,
        long CompactBytes,
        long LastWriteUtcTicks,
        string? CompactText);
}
