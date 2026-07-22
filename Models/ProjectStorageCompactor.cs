using System.IO;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;

namespace OurPlanCore;

public enum ProjectStorageCompactionStatus
{
    Compacted,
    AlreadyCompact,
    SkippedInvalidJson,
    SkippedMissing,
    SkippedChangedSincePreview,
    SkippedUnsafePath,
    SkippedInaccessible,
    Failed,
}

public sealed record ProjectStorageCompactionCandidate(
    string FullPath,
    string RelativePath,
    long CurrentBytes,
    long CompactBytes,
    long PotentialSavingsBytes,
    long LastWriteUtcTicks,
    string Sha256);

public sealed record ProjectStorageCompactionIssue(
    string RelativePath,
    string Reason,
    ProjectStorageCompactionStatus Status = ProjectStorageCompactionStatus.SkippedInvalidJson);

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
            ProjectStorageCompactionStatus.SkippedInvalidJson or
            ProjectStorageCompactionStatus.SkippedUnsafePath or
            ProjectStorageCompactionStatus.SkippedInaccessible)
        .ToList();

    public int IssueCount => Files.Count(file => file.Status is not
        (ProjectStorageCompactionStatus.Compacted or ProjectStorageCompactionStatus.AlreadyCompact));

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
        return BuildPlan(job.RootPath, CancellationToken.None);
    }

    public static ProjectStorageCompactionPlan BuildPlan(string jobRoot)
        => BuildPlan(jobRoot, CancellationToken.None);

    public static ProjectStorageCompactionPlan BuildPlan(string jobRoot, CancellationToken cancellationToken)
    {
        string root = ResolveExistingRoot(jobRoot);
        var candidates = new List<ProjectStorageCompactionCandidate>();
        var skipped = new List<ProjectStorageCompactionIssue>();

        foreach (string path in EnumerateEligibleSnapFiles(root, skipped, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = Relative(root, path);
            try
            {
                CompactJsonSnapshot snapshot = ReadCompactSnapshot(path, cancellationToken);
                candidates.Add(new ProjectStorageCompactionCandidate(
                    path,
                    relative,
                    snapshot.CurrentBytes,
                    snapshot.CompactBytes,
                    Math.Max(0, snapshot.CurrentBytes - snapshot.CompactBytes),
                    snapshot.LastWriteUtcTicks,
                    snapshot.Sha256));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                ProjectStorageCompactionStatus status = ex is JsonException
                    ? ProjectStorageCompactionStatus.SkippedInvalidJson
                    : ProjectStorageCompactionStatus.SkippedInaccessible;
                skipped.Add(new ProjectStorageCompactionIssue(relative, ex.Message, status));
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
        => Execute(plan, CancellationToken.None);

    public static ProjectStorageCompactionResult Execute(
        ProjectStorageCompactionPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        string root = ResolveExistingRoot(plan.JobRoot);
        var results = plan.SkippedFiles
            .Select(issue => new ProjectStorageCompactionFileResult(
                issue.RelativePath,
                issue.Status,
                0,
                0,
                0,
                issue.Reason))
            .ToList();

        foreach (ProjectStorageCompactionCandidate candidate in plan.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(CompactCandidate(root, candidate, cancellationToken));
        }

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
        ProjectStorageCompactionCandidate candidate,
        CancellationToken cancellationToken)
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

        if (!IsSafePhysicalSnapPath(root, path, out string unsafeReason))
        {
            return new ProjectStorageCompactionFileResult(
                candidate.RelativePath,
                ProjectStorageCompactionStatus.SkippedUnsafePath,
                candidate.CurrentBytes,
                candidate.CurrentBytes,
                0,
                unsafeReason);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileInfo before = new(path);
            if (before.Length != candidate.CurrentBytes ||
                before.LastWriteTimeUtc.Ticks != candidate.LastWriteUtcTicks)
            {
                return Changed(candidate, before.Length);
            }

            CompactJsonSnapshot snapshot = ReadCompactSnapshot(path, cancellationToken);
            if (snapshot.CurrentBytes != candidate.CurrentBytes ||
                snapshot.LastWriteUtcTicks != candidate.LastWriteUtcTicks ||
                !string.Equals(snapshot.Sha256, candidate.Sha256, StringComparison.Ordinal))
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
            WriteCompactJsonAtomic(
                path,
                snapshot.CurrentBytes,
                snapshot.LastWriteUtcTicks,
                snapshot.Sha256,
                root,
                cancellationToken);

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

    private static CompactJsonSnapshot ReadCompactSnapshot(string path, CancellationToken cancellationToken)
    {
        long writeTicksBefore = File.GetLastWriteTimeUtc(path).Ticks;
        using var input = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        long currentBytes = input.Length;
        string sha256 = HashStream(input, cancellationToken);
        input.Position = 0;
        using JsonDocument document = JsonDocument.Parse(input);
        long compactBytes = CountCompactBytes(document.RootElement);

        long writeTicksAfter = File.GetLastWriteTimeUtc(path).Ticks;
        if (writeTicksBefore != writeTicksAfter || new FileInfo(path).Length != currentBytes)
            throw new IOException("File changed while it was being analyzed.");

        return new CompactJsonSnapshot(
            currentBytes,
            compactBytes,
            writeTicksAfter,
            sha256);
    }

    private static long CountCompactBytes(JsonElement root)
    {
        using var output = new CountingWriteStream();
        using (var writer = new Utf8JsonWriter(output, CompactWriterOptions))
            root.WriteTo(writer);
        return output.BytesWritten;
    }

    private static void WriteCompactJsonAtomic(
        string path,
        long expectedBytes,
        long expectedLastWriteUtcTicks,
        string expectedSha256,
        string root,
        CancellationToken cancellationToken)
    {
        IoUtil.WriteStreamAtomic(path, output =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSafePhysicalSnapPath(root, path, out string unsafeReason))
                throw new IOException(unsafeReason);

            long writeTicksBefore = File.GetLastWriteTimeUtc(path).Ticks;
            using var input = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            if (input.Length != expectedBytes || writeTicksBefore != expectedLastWriteUtcTicks)
                throw new IOException("File changed after preview; analyze again before compacting.");

            string actualSha256 = HashStream(input, cancellationToken);
            if (!string.Equals(actualSha256, expectedSha256, StringComparison.Ordinal))
                throw new IOException("File content changed after preview; analyze again before compacting.");
            input.Position = 0;

            using JsonDocument document = JsonDocument.Parse(input);
            using (var writer = new Utf8JsonWriter(output, CompactWriterOptions))
                document.RootElement.WriteTo(writer);

            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSafePhysicalSnapPath(root, path, out unsafeReason))
                throw new IOException(unsafeReason);
            if (File.GetLastWriteTimeUtc(path).Ticks != writeTicksBefore ||
                new FileInfo(path).Length != expectedBytes)
            {
                throw new IOException("File changed while it was being compacted.");
            }
        });
    }

    private static IEnumerable<string> EnumerateEligibleSnapFiles(
        string root,
        List<ProjectStorageCompactionIssue> skipped,
        CancellationToken cancellationToken)
    {
        string pagesRoot = Path.Combine(root, "Pages");
        if (!Directory.Exists(pagesRoot))
            yield break;
        if ((File.GetAttributes(pagesRoot) & FileAttributes.ReparsePoint) != 0)
        {
            skipped.Add(new ProjectStorageCompactionIssue(
                Relative(root, pagesRoot),
                "Pages is a reparse point; safe compact is disabled for this project.",
                ProjectStorageCompactionStatus.SkippedUnsafePath));
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(pagesRoot);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                string fullPath;
                bool eligible = false;
                try
                {
                    fullPath = Path.GetFullPath(file);
                    if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                    {
                        skipped.Add(new ProjectStorageCompactionIssue(
                            Relative(root, fullPath),
                            "Reparse-point files are not compacted.",
                            ProjectStorageCompactionStatus.SkippedUnsafePath));
                    }
                    else if (IsEligibleSnapPath(root, fullPath))
                    {
                        eligible = true;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped.Add(new ProjectStorageCompactionIssue(
                        Relative(root, file),
                        ex.Message,
                        ProjectStorageCompactionStatus.SkippedInaccessible));
                    continue;
                }

                if (eligible)
                    yield return fullPath;
            }

            foreach (string child in children)
            {
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                        pending.Push(child);
                    else
                    {
                        skipped.Add(new ProjectStorageCompactionIssue(
                            Relative(root, child),
                            "Reparse-point folders are not compacted.",
                            ProjectStorageCompactionStatus.SkippedUnsafePath));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped.Add(new ProjectStorageCompactionIssue(
                        Relative(root, child),
                        ex.Message,
                        ProjectStorageCompactionStatus.SkippedInaccessible));
                }
            }
        }
    }

    private static bool IsSafePhysicalSnapPath(string root, string path, out string reason)
    {
        reason = "";
        if (!IsEligibleSnapPath(root, path))
        {
            reason = "Path is outside Pages/**/raster/snap.json.";
            return false;
        }

        string pagesRoot = Path.GetFullPath(Path.Combine(root, "Pages"));
        string current = Path.GetFullPath(path);
        while (true)
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    reason = $"Reparse-point path is not compacted: {Relative(root, current)}";
                    return false;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                reason = $"Cannot validate compact path: {ex.Message}";
                return false;
            }

            if (PathComparer.Equals(current, pagesRoot))
                return true;

            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) || !IsDescendant(pagesRoot, current))
            {
                reason = "Compact path does not have Pages as its physical ancestor.";
                return false;
            }
            current = parent;
        }
    }

    private static string HashStream(Stream stream, CancellationToken cancellationToken)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[1024 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
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
        string Sha256);

    private sealed class CountingWriteStream : Stream
    {
        public long BytesWritten { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => BytesWritten;
        public override long Position
        {
            get => BytesWritten;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            BytesWritten += count;

        public override void Write(ReadOnlySpan<byte> buffer) =>
            BytesWritten += buffer.Length;

        public override void WriteByte(byte value) =>
            BytesWritten++;
    }
}
