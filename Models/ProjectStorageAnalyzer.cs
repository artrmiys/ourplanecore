using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;

namespace OurPlanCore;

public static class ProjectStorageAnalyzer
{
    private const string RecoveryFolderName = ".snapshots";
    private const string SourcesFolderName = "sources";

    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    private static readonly HashSet<string> NonSourceFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Data.xml",
        "desktop.ini",
        "Thumbs.db",
        ".DS_Store",
    };

    public static ProjectStorageAnalysis Analyze(OurPlanCoreJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return Analyze(job.RootPath, CancellationToken.None);
    }

    public static ProjectStorageAnalysis Analyze(OurPlanCoreJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        return Analyze(job.RootPath, cancellationToken);
    }

    public static ProjectStorageAnalysis Analyze(string jobRoot) =>
        Analyze(jobRoot, CancellationToken.None);

    public static ProjectStorageAnalysis Analyze(string jobRoot, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(jobRoot))
            throw new ArgumentException("Job root is required.", nameof(jobRoot));

        string root = Path.GetFullPath(jobRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);

        var warnings = new List<string>();
        IReadOnlyList<FileSnapshot> files = EnumerateFiles(
            root,
            warnings,
            cancellationToken,
            out bool fileScanComplete);
        string sourcesRoot = Path.Combine(root, SourcesFolderName);
        var sourceFiles = files
            .Where(file => IsSourceCandidate(file.FullPath, sourcesRoot))
            .OrderBy(file => Relative(root, file.FullPath), PathComparer)
            .ToList();

        IReadOnlyList<ProjectStorageReference> references = BuildReferences(
            root,
            sourcesRoot,
            files,
            sourceFiles,
            warnings,
            cancellationToken,
            out bool metadataScanComplete);
        bool referenceScanComplete = fileScanComplete && metadataScanComplete;
        var referencedSources = references
            .Where(reference => reference.TargetsJobSource)
            .Select(reference => reference.ResolvedTargetPath)
            .ToHashSet(PathComparer);

        (IReadOnlyList<ProjectStorageDuplicateGroup> duplicateGroups,
            HashSet<string> duplicateFiles,
            IReadOnlyDictionary<string, string> sourceHashes) =
            referenceScanComplete
                ? FindDuplicateSources(root, sourceFiles, referencedSources, warnings, cancellationToken)
                : ([], new HashSet<string>(PathComparer),
                    new Dictionary<string, string>(PathComparer));

        var snapReports = new List<ProjectStorageSnapJsonReport>();
        foreach (FileSnapshot file in files.Where(file =>
                     IsRasterFile(root, file.FullPath) &&
                     Path.GetFileName(file.FullPath).Equals(
                         RasterSheetCacheService.SnapIndexName,
                         StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            snapReports.Add(AnalyzeSnapJson(root, file, warnings));
        }
        snapReports.Sort((left, right) => PathComparer.Compare(left.RelativePath, right.RelativePath));

        var entries = files
            .Select(file => BuildFileEntry(
                root,
                sourcesRoot,
                file,
                referencedSources,
                duplicateFiles,
                sourceHashes,
                referenceScanComplete))
            .OrderBy(file => file.RelativePath, PathComparer)
            .ToList();
        IReadOnlyList<ProjectStorageCategorySummary> categories =
            Enum.GetValues<ProjectStorageCategory>()
                .Select(category => new ProjectStorageCategorySummary(
                    category,
                    entries.Count(file => file.Category == category),
                    entries.Where(file => file.Category == category).Sum(file => file.Length)))
                .ToList();

        return new ProjectStorageAnalysis
        {
            JobRoot = root,
            Files = entries,
            References = references,
            DuplicateGroups = duplicateGroups,
            SnapJsonReports = snapReports,
            Categories = categories,
            Warnings = warnings,
            ReferenceScanComplete = referenceScanComplete,
        };
    }

    private static ProjectStorageFileEntry BuildFileEntry(
        string root,
        string sourcesRoot,
        FileSnapshot file,
        IReadOnlySet<string> referencedSources,
        IReadOnlySet<string> duplicateFiles,
        IReadOnlyDictionary<string, string> sourceHashes,
        bool referenceScanComplete)
    {
        string relative = Relative(root, file.FullPath);
        bool isSource = IsSourceCandidate(file.FullPath, sourcesRoot);
        bool isReferenced = isSource && referencedSources.Contains(file.FullPath);
        ProjectStorageCategory category;

        if (IsInside(file.FullPath, Path.Combine(root, RecoveryFolderName)))
            category = ProjectStorageCategory.RecoveryHistory;
        else if (IsRasterFile(root, file.FullPath))
            category = ProjectStorageCategory.RebuildableRaster;
        else if (isSource && duplicateFiles.Contains(file.FullPath))
            category = ProjectStorageCategory.ExactDuplicateSource;
        else if (isSource && !isReferenced)
            category = referenceScanComplete
                ? ProjectStorageCategory.UnreferencedPageSource
                : ProjectStorageCategory.SourceNeedsReview;
        else if (isSource || IsCanonicalFile(relative))
            category = ProjectStorageCategory.Canonical;
        else
            category = ProjectStorageCategory.Other;

        return new ProjectStorageFileEntry(
            file.FullPath,
            relative,
            file.Length,
            category,
            isReferenced,
            sourceHashes.TryGetValue(file.FullPath, out string? hash) ? hash : "");
    }

    private static IReadOnlyList<ProjectStorageReference> BuildReferences(
        string root,
        string sourcesRoot,
        IReadOnlyList<FileSnapshot> files,
        IReadOnlyList<FileSnapshot> sourceFiles,
        List<string> warnings,
        CancellationToken cancellationToken,
        out bool scanComplete)
    {
        scanComplete = true;
        string pagesRoot = Path.Combine(root, "Pages");
        var references = new List<ProjectStorageReference>();
        var snapshotOriginalRoots = new Dictionary<string, string>(PathComparer);

        foreach (FileSnapshot file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsReferenceMetadata(file.FullPath))
                continue;

            string metadataFolder = Path.GetDirectoryName(file.FullPath) ?? root;
            bool recovery = TryGetRecoveryPageFolder(
                root,
                file.FullPath,
                out string snapshotRoot,
                out string logicalPageFolder);
            ProjectStorageReferenceKind kind;
            if (recovery)
            {
                bool sourceJson = IsSourceJson(file.FullPath);
                kind = sourceJson
                    ? ProjectStorageReferenceKind.RecoverySourceJson
                    : ProjectStorageReferenceKind.RecoverySourcePdfMetadata;
                metadataFolder = logicalPageFolder;
            }
            else if (IsInside(file.FullPath, pagesRoot))
            {
                kind = IsSourceJson(file.FullPath)
                    ? ProjectStorageReferenceKind.CurrentSourceJson
                    : ProjectStorageReferenceKind.CurrentSourcePdfMetadata;
            }
            else
            {
                continue;
            }

            string property = IsSourceJson(file.FullPath) ? "pdf" : "pdf_path";
            if (!TryReadStringProperty(file.FullPath, property, out string rawTarget, out string error))
            {
                scanComplete = false;
                warnings.Add($"Cannot read {Relative(root, file.FullPath)}: {error}");
                continue;
            }

            string originalRoot = "";
            if (recovery)
            {
                if (!snapshotOriginalRoots.TryGetValue(snapshotRoot, out originalRoot!))
                {
                    originalRoot = ReadSnapshotOriginalRoot(snapshotRoot);
                    snapshotOriginalRoots[snapshotRoot] = originalRoot;
                }
            }

            List<string> resolvedTargets = ResolveReferenceTargets(
                    root,
                    sourcesRoot,
                    metadataFolder,
                    rawTarget,
                    originalRoot,
                    sourceFiles)
                .ToList();
            if (resolvedTargets.Count == 0 || !resolvedTargets.Any(File.Exists))
            {
                scanComplete = false;
                warnings.Add(
                    $"Cannot resolve {property} in {Relative(root, file.FullPath)}: {rawTarget}");
            }

            foreach (string target in resolvedTargets)
            {
                bool targetExists = File.Exists(target);
                bool targetsJob = IsInside(target, root);
                references.Add(new ProjectStorageReference(
                    Relative(root, file.FullPath),
                    rawTarget,
                    target,
                    kind,
                    targetExists,
                    IsInside(target, sourcesRoot),
                    targetsJob));
                if (targetExists && !targetsJob)
                {
                    warnings.Add(
                        $"External page source makes the project non-portable: " +
                        $"{Relative(root, file.FullPath)} -> {target}");
                }
            }
        }

        return references
            .DistinctBy(reference =>
                $"{reference.Kind}|{reference.MetadataPath}|{reference.ResolvedTargetPath}",
                PathComparer)
            .OrderBy(reference => reference.MetadataPath, PathComparer)
            .ThenBy(reference => reference.Kind)
            .ThenBy(reference => reference.ResolvedTargetPath, PathComparer)
            .ToList();
    }

    private static IEnumerable<string> ResolveReferenceTargets(
        string root,
        string sourcesRoot,
        string logicalPageFolder,
        string rawTarget,
        string originalJobRoot,
        IReadOnlyList<FileSnapshot> sourceFiles)
    {
        string direct;
        try
        {
            direct = Path.IsPathRooted(rawTarget)
                ? Path.GetFullPath(rawTarget)
                : Path.GetFullPath(Path.Combine(logicalPageFolder, rawTarget));
            if (!string.IsNullOrWhiteSpace(originalJobRoot) && IsInside(direct, originalJobRoot))
            {
                string relocated = Path.GetRelativePath(Path.GetFullPath(originalJobRoot), direct);
                direct = Path.GetFullPath(Path.Combine(root, relocated));
            }
        }
        catch
        {
            yield break;
        }

        yield return direct;
        if (File.Exists(direct))
            yield break;

        string fileName = Path.GetFileName(rawTarget);
        if (string.IsNullOrWhiteSpace(fileName))
            yield break;

        foreach (FileSnapshot candidate in sourceFiles.Where(file =>
                     Path.GetFileName(file.FullPath).Equals(fileName, StringComparison.OrdinalIgnoreCase) &&
                     IsInside(file.FullPath, sourcesRoot)))
        {
            if (!PathComparer.Equals(candidate.FullPath, direct))
                yield return candidate.FullPath;
        }
    }

    private static (IReadOnlyList<ProjectStorageDuplicateGroup> Groups,
        HashSet<string> DuplicateFiles,
        IReadOnlyDictionary<string, string> Hashes) FindDuplicateSources(
        string root,
        IReadOnlyList<FileSnapshot> sourceFiles,
        IReadOnlySet<string> referencedSources,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var hashes = new Dictionary<string, string>(PathComparer);
        foreach (IGrouping<long, FileSnapshot> sameLength in sourceFiles.GroupBy(file => file.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sameLength.Count() < 2)
                continue;

            foreach (FileSnapshot file in sameLength)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var stream = new FileStream(
                        file.FullPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    hashes[file.FullPath] = Convert.ToHexString(
                        HashStream(stream, cancellationToken));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"Cannot hash {Relative(root, file.FullPath)}: {ex.Message}");
                }
            }
        }

        var duplicateFiles = new HashSet<string>(PathComparer);
        var groups = new List<ProjectStorageDuplicateGroup>();
        foreach (IGrouping<string, FileSnapshot> hashGroup in sourceFiles
                     .Where(file => hashes.ContainsKey(file.FullPath))
                     .GroupBy(file => hashes[file.FullPath], StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<FileSnapshot> referenced = hashGroup
                .Where(file => referencedSources.Contains(file.FullPath))
                .OrderBy(file => Relative(root, file.FullPath), PathComparer)
                .ToList();
            List<FileSnapshot> unreferenced = hashGroup
                .Where(file => !referencedSources.Contains(file.FullPath))
                .OrderBy(file => Relative(root, file.FullPath), PathComparer)
                .ToList();
            FileSnapshot retained = referenced.FirstOrDefault() ?? unreferenced[0];
            List<FileSnapshot> duplicates = referenced.Count > 0
                ? unreferenced
                : unreferenced.Skip(1).ToList();
            if (duplicates.Count == 0)
                continue;
            foreach (FileSnapshot duplicate in duplicates)
                duplicateFiles.Add(duplicate.FullPath);

            groups.Add(new ProjectStorageDuplicateGroup(
                hashGroup.Key,
                retained.Length,
                Relative(root, retained.FullPath),
                duplicates.Select(file => Relative(root, file.FullPath)).ToList(),
                duplicates.Sum(file => file.Length)));
        }

        return (
            groups.OrderByDescending(group => group.PotentialSavingsBytes)
                .ThenBy(group => group.RetainedPath, PathComparer)
                .ToList(),
            duplicateFiles,
            hashes);
    }

    private static byte[] HashStream(Stream stream, CancellationToken cancellationToken)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[1024 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read);
        }
        return hash.GetHashAndReset();
    }

    private static ProjectStorageSnapJsonReport AnalyzeSnapJson(
        string root,
        FileSnapshot file,
        List<string> warnings)
    {
        try
        {
            using var stream = new FileStream(
                file.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using JsonDocument document = JsonDocument.Parse(stream);
            long compactBytes = CountCompactJsonBytes(document.RootElement);
            return new ProjectStorageSnapJsonReport(
                Relative(root, file.FullPath),
                file.Length,
                compactBytes,
                Math.Max(0, file.Length - compactBytes),
                true,
                "");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            string error = ex.Message;
            warnings.Add($"Cannot inspect {Relative(root, file.FullPath)}: {error}");
            return new ProjectStorageSnapJsonReport(
                Relative(root, file.FullPath),
                file.Length,
                file.Length,
                0,
                false,
                error);
        }
    }

    private static long CountCompactJsonBytes(JsonElement root)
    {
        using var counter = new CountingWriteStream();
        using (var writer = new Utf8JsonWriter(counter, new JsonWriterOptions
               {
                   Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
               }))
            root.WriteTo(writer);
        return counter.BytesWritten;
    }

    private static bool TryReadStringProperty(
        string path,
        string propertyName,
        out string value,
        out string error)
    {
        value = "";
        error = "";
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using JsonDocument document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = $"Expected a JSON object, found {document.RootElement.ValueKind}.";
                return false;
            }

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (!property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    error = $"Property '{propertyName}' must be a string.";
                    return false;
                }

                value = property.Value.GetString()?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(value))
                {
                    error = $"Property '{propertyName}' is empty.";
                    return false;
                }
                return true;
            }

            error = $"Property '{propertyName}' is missing.";
            return false;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string ReadSnapshotOriginalRoot(string snapshotRoot)
    {
        string manifest = Path.Combine(snapshotRoot, "snapshot_manifest.json");
        return TryReadStringProperty(manifest, "job_root", out string root, out _)
            ? root
            : "";
    }

    private static bool TryGetRecoveryPageFolder(
        string root,
        string metadataPath,
        out string snapshotRoot,
        out string logicalPageFolder)
    {
        snapshotRoot = "";
        logicalPageFolder = "";
        string[] parts = SplitRelative(Relative(root, metadataPath));
        if (parts.Length < 4 ||
            !parts[0].Equals(RecoveryFolderName, StringComparison.OrdinalIgnoreCase) ||
            !parts[2].Equals("Pages", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        snapshotRoot = Path.Combine(root, parts[0], parts[1]);
        string[] pageFolderParts = parts.Skip(3).Take(parts.Length - 4).ToArray();
        logicalPageFolder = pageFolderParts.Length == 0
            ? Path.Combine(root, "Pages")
            : Path.Combine([root, "Pages", .. pageFolderParts]);
        return true;
    }

    private static IReadOnlyList<FileSnapshot> EnumerateFiles(
        string root,
        List<string> warnings,
        CancellationToken cancellationToken,
        out bool scanComplete)
    {
        scanComplete = true;
        var results = new List<FileSnapshot>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string folder = pending.Pop();
            try
            {
                foreach (string file in Directory.EnumerateFiles(folder))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                        {
                            scanComplete = false;
                            warnings.Add($"Skipped reparse-point file: {Relative(root, file)}");
                            continue;
                        }

                        results.Add(new FileSnapshot(Path.GetFullPath(file), new FileInfo(file).Length));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        scanComplete = false;
                        warnings.Add($"Cannot inspect {Relative(root, file)}: {ex.Message}");
                    }
                }

                foreach (string child in Directory.EnumerateDirectories(folder))
                {
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                            pending.Push(child);
                        else
                        {
                            scanComplete = false;
                            warnings.Add($"Skipped reparse-point folder: {Relative(root, child)}");
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        scanComplete = false;
                        warnings.Add($"Cannot inspect {Relative(root, child)}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                scanComplete = false;
                warnings.Add($"Cannot enumerate {Relative(root, folder)}: {ex.Message}");
            }
        }

        return results;
    }

    private static bool IsReferenceMetadata(string path) =>
        IsSourceJson(path) ||
        Path.GetFileName(path).Equals("source_pdf.json", StringComparison.OrdinalIgnoreCase);

    private static bool IsSourceJson(string path) =>
        Path.GetFileName(path).Equals("source.json", StringComparison.OrdinalIgnoreCase);

    private static bool IsSourceCandidate(string path, string sourcesRoot) =>
        IsInside(path, sourcesRoot) && !NonSourceFileNames.Contains(Path.GetFileName(path));

    private static bool IsRasterFile(string root, string path)
    {
        string pagesRoot = Path.Combine(root, "Pages");
        if (!IsInside(path, pagesRoot))
            return false;

        string? folder = Path.GetDirectoryName(path);
        while (!string.IsNullOrWhiteSpace(folder) && IsInside(folder, pagesRoot))
        {
            if (Path.GetFileName(folder).Equals(
                    RasterSheetCacheService.CacheFolderName,
                    StringComparison.OrdinalIgnoreCase))
            {
                string? pageFolder = Directory.GetParent(folder)?.FullName;
                return !string.IsNullOrWhiteSpace(pageFolder) &&
                       File.Exists(Path.Combine(pageFolder, "source.json"));
            }
            folder = Directory.GetParent(folder)?.FullName;
        }

        return false;
    }

    private static bool IsCanonicalFile(string relativePath)
    {
        string[] parts = SplitRelative(relativePath);
        if (parts.Length == 0)
            return false;
        if (parts.Length == 1)
            return parts[0].Equals("Data.xml", StringComparison.OrdinalIgnoreCase);
        if (parts[0].Equals("Pages", StringComparison.OrdinalIgnoreCase) ||
            parts[0].Equals("Takeoffs", StringComparison.OrdinalIgnoreCase) ||
            parts[0].Equals("AI_Context", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return parts[0].Equals(SourcesFolderName, StringComparison.OrdinalIgnoreCase) &&
               parts[^1].Equals("Data.xml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInside(string path, string folder)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            string fullFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            return fullPath.Equals(fullFolder, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(fullFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string Relative(string root, string path)
    {
        try
        {
            return Path.GetRelativePath(root, path);
        }
        catch
        {
            return path;
        }
    }

    private static string[] SplitRelative(string path) =>
        path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

    private sealed record FileSnapshot(string FullPath, long Length);

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
