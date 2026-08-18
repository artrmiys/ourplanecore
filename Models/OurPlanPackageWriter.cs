using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace OurPlanCore;

public static partial class OurPlanPackageWriter
{
    private static readonly HashSet<string> AlreadyCompressedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".7z", ".avi", ".docx", ".gif", ".gz", ".jpeg", ".jpg", ".mov", ".mp3", ".mp4",
        ".pdf", ".png", ".rar", ".webp", ".xlsx", ".xlsm", ".zip",
    };

    public static OurPlanPackageSession Create(
        string workspaceRoot,
        string packagePath,
        string displayName,
        string? projectId = null,
        string? revisionId = null)
    {
        string target = NormalizeTarget(packagePath);
        if (File.Exists(target))
            throw new IOException($"The destination already exists: {target}");

        string cleanProjectId = NormalizeGuid(projectId, "project");
        string cleanRevisionId = NormalizeGuid(revisionId, "revision");
        var context = new PublishContext(
            cleanProjectId,
            cleanRevisionId,
            ParentRevisionId: "",
            CreatedUtc: DateTime.UtcNow.ToString("O"),
            DisplayName: CleanDisplayName(displayName),
            ExpectedRevisionId: null,
            ExpectedFingerprint: null,
            AllowExistingDestination: false,
            BaseManifest: null,
            SourceSession: null);
        PublishOutcome outcome = Publish(workspaceRoot, target, context);
        var session = new OurPlanPackageSession
        {
            PackagePath = target,
            WorkspaceRoot = Path.GetFullPath(workspaceRoot),
            ProjectId = cleanProjectId,
            DisplayName = context.DisplayName,
            BaseRevisionId = outcome.Result.RevisionId,
            BaseFingerprint = outcome.Fingerprint,
            HasUnpackagedChanges = false,
        };
        OurPlanPackageWorkspace.PersistSavedBase(
            session,
            outcome.Manifest,
            outcome.Fingerprint,
            outcome.FileStates);
        return session;
    }

    public static OurPlanPackageSaveResult Save(OurPlanPackageSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        OurPlanPackageManifest current = OurPlanPackageArchive.ReadManifest(
            session.PackagePath,
            verifyObjects: false);
        if (!current.ProjectId.Equals(session.ProjectId, StringComparison.OrdinalIgnoreCase))
            throw new OurPlanPackageConflictException("The project file identity changed after it was opened.");
        if (!current.RevisionId.Equals(session.BaseRevisionId, StringComparison.OrdinalIgnoreCase))
            throw new OurPlanPackageConflictException("The project file was changed elsewhere after it was opened.");
        OurPlanPackageFingerprint currentFingerprint = OurPlanPackageFingerprint.Read(session.PackagePath);
        if (currentFingerprint != session.BaseFingerprint)
        {
            current = OurPlanPackageArchive.ReadManifest(session.PackagePath, verifyObjects: true);
            if (!current.ProjectId.Equals(session.ProjectId, StringComparison.OrdinalIgnoreCase) ||
                !current.RevisionId.Equals(session.BaseRevisionId, StringComparison.OrdinalIgnoreCase) ||
                !OurPlanPackageWorkspace.ManifestMatchesSessionBase(session, current))
            {
                throw new OurPlanPackageConflictException(
                    "The project file fingerprint and revision changed after it was opened.");
            }
            session.BaseFingerprint = currentFingerprint;
        }

        string revisionId = Guid.NewGuid().ToString("N");
        var context = new PublishContext(
            session.ProjectId,
            revisionId,
            session.BaseRevisionId,
            current.CreatedUtc,
            CleanDisplayName(session.DisplayName),
            session.BaseRevisionId,
            session.BaseFingerprint,
            AllowExistingDestination: true,
            BaseManifest: current,
            SourceSession: session);
        PublishOutcome outcome = Publish(session.WorkspaceRoot, session.PackagePath, context);
        OurPlanPackageWorkspace.PersistSavedBase(
            session,
            outcome.Manifest,
            outcome.Fingerprint,
            outcome.FileStates);
        return outcome.Result;
    }

    public static OurPlanPackageSession SaveAs(
        string workspaceRoot,
        string packagePath,
        string displayName,
        bool overwriteExisting,
        OurPlanPackageSession? sourceSession = null,
        string? projectId = null)
    {
        string target = NormalizeTarget(packagePath);
        string workspace = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        if (sourceSession != null &&
            !workspace.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceSession.WorkspaceRoot)),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The source package session does not own this working folder.",
                nameof(sourceSession));
        }
        DestinationState state = CaptureSaveAsDestination(target, overwriteExisting);
        string cleanProjectId = NormalizeGuid(projectId, "project");
        string revisionId = Guid.NewGuid().ToString("N");
        string parentRevisionId = sourceSession != null &&
                                  sourceSession.ProjectId.Equals(cleanProjectId, StringComparison.OrdinalIgnoreCase)
            ? sourceSession.BaseRevisionId
            : "";
        string createdUtc = DateTime.UtcNow.ToString("O");
        if (sourceSession != null &&
            OurPlanPackageArchive.TryReadManifest(
                sourceSession.PackagePath,
                out OurPlanPackageManifest? sourceManifest) &&
            sourceManifest!.ProjectId.Equals(cleanProjectId, StringComparison.OrdinalIgnoreCase))
        {
            createdUtc = sourceManifest.CreatedUtc;
        }
        var context = new PublishContext(
            cleanProjectId,
            revisionId,
            ParentRevisionId: parentRevisionId,
            CreatedUtc: createdUtc,
            DisplayName: CleanDisplayName(displayName),
            ExpectedRevisionId: state.RevisionId,
            ExpectedFingerprint: state.Fingerprint,
            AllowExistingDestination: state.Exists,
            BaseManifest: null,
            SourceSession: sourceSession,
            ExpectedContentSha256: state.ContentSha256);
        PublishOutcome outcome = Publish(workspace, target, context);
        var session = new OurPlanPackageSession
        {
            PackagePath = target,
            WorkspaceRoot = workspace,
            ProjectId = cleanProjectId,
            DisplayName = context.DisplayName,
            BaseRevisionId = outcome.Result.RevisionId,
            BaseFingerprint = outcome.Fingerprint,
            HasUnpackagedChanges = false,
        };
        bool claimTransferred = false;
        try
        {
            if (sourceSession != null)
            {
                OurPlanPackageWorkspace.TransferWorkspaceClaim(sourceSession, session);
                claimTransferred = true;
            }
            OurPlanPackageWorkspace.PersistSavedBase(
                session,
                outcome.Manifest,
                outcome.Fingerprint,
                outcome.FileStates);
        }
        catch
        {
            if (claimTransferred && sourceSession != null)
            {
                try
                {
                    OurPlanPackageWorkspace.TransferWorkspaceClaim(session, sourceSession);
                    OurPlanPackageWorkspace.MarkDirty(sourceSession);
                }
                catch (Exception recoveryError)
                {
                    AppLog.Warn(recoveryError, "Could not return the workspace claim after Save As failed.");
                }
            }
            throw;
        }
        return session;
    }

    private static PublishOutcome Publish(
        string workspaceRoot,
        string targetPath,
        PublishContext context)
    {
        using JobFileWriteActivity.PackageCheckpointScope checkpoint =
            JobFileWriteActivity.BeginPackageCheckpoint();
        if (checkpoint.HadActiveWriters)
        {
            throw new IOException(
                "A background project writer is still active. Wait for it to finish, then save again.");
        }

        string workspace = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        EnsureTargetOutsideWorkspace(workspace, targetPath);
        ScavengeStalePackageArtifacts(targetPath);
        DestinationState initialDestination = CaptureExpectedDestination(targetPath, context);
        IReadOnlyList<OurPlanPackageSourceFile> sourceFiles = OurPlanPackageFileSelector.Collect(workspace);
        OurPlanPackageSemanticValidator.Validate(sourceFiles);
        OurPlanPackagePortability.ValidateExtractedReferences(workspace);
        using OurPlanPackagePortability.PortableSourceSet portableSources =
            OurPlanPackagePortability.CreatePortableSourceSet(workspace, sourceFiles);
        List<HashedSourceFile> hashedFiles = HashStableFiles(
            sourceFiles,
            portableSources,
            context.BaseManifest,
            context.SourceSession);
        var manifest = new OurPlanPackageManifest
        {
            ProjectId = context.ProjectId,
            RevisionId = context.RevisionId,
            ParentRevisionId = context.ParentRevisionId,
            DisplayName = context.DisplayName,
            CreatedUtc = context.CreatedUtc,
            SavedUtc = DateTime.UtcNow.ToString("O"),
            Files = hashedFiles.Select(file => new OurPlanPackageFileManifest
            {
                Path = file.Source.LogicalPath,
                ObjectSha256 = file.Sha256,
                Length = file.ContentLength,
                LastWriteUtcTicks = file.ManifestLastWriteUtcTicks,
            }).ToList(),
        };
        OurPlanPackageArchive.ValidateManifest(manifest);

        if (context.BaseManifest != null && ManifestsHaveSameContent(context.BaseManifest, manifest))
        {
            EnsureWorkspaceUnchanged(workspace, hashedFiles);
            EnsureDestinationUnchanged(targetPath, initialDestination);
            OurPlanPackageSaveResult result = new(
                targetPath,
                context.BaseManifest.RevisionId,
                context.BaseManifest.Files.Count,
                context.BaseManifest.Files.Select(file => file.ObjectSha256)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                context.BaseManifest.Files.Sum(file => file.Length),
                new FileInfo(targetPath).Length);
            return BuildOutcome(
                result,
                context.BaseManifest,
                hashedFiles,
                initialDestination.Fingerprint ?? OurPlanPackageFingerprint.Read(targetPath));
        }

        string? directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        string stagingDirectory = PublishStagingDirectory(targetPath);
        Directory.CreateDirectory(stagingDirectory);
        string tempPath = Path.Combine(
            stagingDirectory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            WriteArchive(tempPath, manifest, hashedFiles);
            OurPlanPackageArchive.ReadManifest(tempPath, verifyObjects: true);
            EnsureWorkspaceUnchanged(workspace, hashedFiles);
            EnsureDestinationUnchanged(targetPath, initialDestination);
            ReplaceAtomically(tempPath, targetPath, initialDestination, stagingDirectory);
            OurPlanPackageFingerprint publishedFingerprint = ValidatePublishedTarget(
                targetPath,
                manifest);

            long sourceBytes = manifest.Files.Sum(file => file.Length);
            OurPlanPackageSaveResult result = new(
                targetPath,
                manifest.RevisionId,
                manifest.Files.Count,
                manifest.Files.Select(file => file.ObjectSha256).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                sourceBytes,
                new FileInfo(targetPath).Length);
            TryDeleteEmptyPublishStaging(stagingDirectory);
            return BuildOutcome(result, manifest, hashedFiles, publishedFingerprint);
        }
        catch
        {
            TryDeleteTemp(tempPath);
            TryDeleteEmptyPublishStaging(stagingDirectory);
            throw;
        }
    }

    private static void WriteArchive(
        string tempPath,
        OurPlanPackageManifest manifest,
        IReadOnlyList<HashedSourceFile> files)
    {
        using var output = new FileStream(
            tempPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.SequentialScan);
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (IGrouping<string, HashedSourceFile> group in files
                         .GroupBy(file => file.Sha256, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                HashedSourceFile source = group.First();
                CompressionLevel level = CompressionFor(source.Source.LogicalPath);
                ZipArchiveEntry entry = archive.CreateEntry(
                    OurPlanPackageFormat.ObjectEntryName(group.Key),
                    level);
                entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using Stream entryStream = entry.Open();
                using var input = new FileStream(
                    source.ContentPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    1024 * 1024,
                    FileOptions.SequentialScan);
                input.CopyTo(entryStream, 1024 * 1024);
            }

            ZipArchiveEntry manifestEntry = archive.CreateEntry(
                OurPlanPackageFormat.ManifestEntryName,
                CompressionLevel.Fastest);
            manifestEntry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using Stream manifestStream = manifestEntry.Open();
            JsonSerializer.Serialize(manifestStream, manifest, OurPlanPackageArchive.JsonOptions);
        }
        output.Flush(flushToDisk: true);
    }

    private static List<HashedSourceFile> HashStableFiles(
        IReadOnlyList<OurPlanPackageSourceFile> sourceFiles,
        OurPlanPackagePortability.PortableSourceSet portableSources,
        OurPlanPackageManifest? baseManifest,
        OurPlanPackageSession? sourceSession)
    {
        Dictionary<string, OurPlanPackageFileManifest> previous = (baseManifest?.Files ?? [])
            .ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var result = new List<HashedSourceFile>(sourceFiles.Count);
        foreach (OurPlanPackageSourceFile source in sourceFiles)
        {
            OurPlanLocalFileStamp before = OurPlanLocalFileStamp.Read(source.FullPath);
            if (before.Length != source.Length ||
                before.LastWriteUtcTicks != source.LastWriteUtcTicks)
            {
                throw new IOException(
                    $"Project file changed before it could be packed: {source.LogicalPath}. Save again.");
            }

            string contentPath = portableSources.ContentPath(source);
            bool contentOverride = portableSources.IsOverride(source);
            var contentInfo = new FileInfo(contentPath);
            long contentLength = contentInfo.Length;
            if (!contentOverride && sourceSession != null &&
                OurPlanPackageWorkspace.TryGetReusableObjectHash(
                    sourceSession,
                    source.LogicalPath,
                    before,
                    out string cachedSha256,
                    out long cachedManifestTicks) &&
                (!previous.TryGetValue(source.LogicalPath, out OurPlanPackageFileManifest? cachedPrior) ||
                 cachedPrior.Length == contentLength &&
                 cachedPrior.ObjectSha256.Equals(cachedSha256, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(new HashedSourceFile(
                    source,
                    contentPath,
                    contentLength,
                    cachedSha256,
                    cachedManifestTicks,
                    before,
                    cachedSha256));
                continue;
            }

            using var stream = new FileStream(
                contentPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.SequentialScan);
            string sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            OurPlanLocalFileStamp after = OurPlanLocalFileStamp.Read(source.FullPath);
            bool stable = before.IsStrong
                ? before.SameGeneration(after)
                : before.Length == after.Length &&
                  before.LastWriteUtcTicks == after.LastWriteUtcTicks;
            if (!stable)
            {
                throw new IOException(
                    $"Project file changed while it was being packed: {source.LogicalPath}. Save again.");
            }
            string workspaceSha256 = contentOverride
                ? HashFile(source.FullPath)
                : sha256;
            OurPlanLocalFileStamp finalWorkspaceStamp = OurPlanLocalFileStamp.Read(source.FullPath);
            bool workspaceStillStable = after.IsStrong
                ? after.SameGeneration(finalWorkspaceStamp)
                : after.Length == finalWorkspaceStamp.Length &&
                  after.LastWriteUtcTicks == finalWorkspaceStamp.LastWriteUtcTicks;
            if (!workspaceStillStable)
            {
                throw new IOException(
                    $"Project file changed while it was being packed: {source.LogicalPath}. Save again.");
            }
            long manifestTicks = previous.TryGetValue(source.LogicalPath, out OurPlanPackageFileManifest? prior) &&
                                 prior.Length == contentLength &&
                                 prior.ObjectSha256.Equals(sha256, StringComparison.OrdinalIgnoreCase) &&
                                 CanReusePreviousTimestamp(source.LogicalPath)
                ? prior.LastWriteUtcTicks
                : source.LastWriteUtcTicks;
            result.Add(new HashedSourceFile(
                source,
                contentPath,
                contentLength,
                sha256,
                manifestTicks,
                finalWorkspaceStamp,
                workspaceSha256));
        }
        return result;
    }

    private static bool ManifestsHaveSameContent(
        OurPlanPackageManifest previous,
        OurPlanPackageManifest current)
    {
        if (!previous.DisplayName.Equals(current.DisplayName, StringComparison.Ordinal) ||
            previous.Files.Count != current.Files.Count)
        {
            return false;
        }

        for (int index = 0; index < previous.Files.Count; index++)
        {
            OurPlanPackageFileManifest left = previous.Files[index];
            OurPlanPackageFileManifest right = current.Files[index];
            if (!left.Path.Equals(right.Path, StringComparison.Ordinal) ||
                !left.ObjectSha256.Equals(right.ObjectSha256, StringComparison.OrdinalIgnoreCase) ||
                left.Length != right.Length || left.LastWriteUtcTicks != right.LastWriteUtcTicks)
            {
                return false;
            }
        }
        return true;
    }

    private static bool CanReusePreviousTimestamp(string logicalPath)
    {
        string extension = Path.GetExtension(logicalPath);
        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return false;
        return !logicalPath.Split('/').Any(part =>
            part.Equals(RasterSheetCacheService.CacheFolderName, StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsureWorkspaceUnchanged(
        string workspace,
        IReadOnlyList<HashedSourceFile> original)
    {
        IReadOnlyList<OurPlanPackageSourceFile> current = OurPlanPackageFileSelector.Collect(workspace);
        if (current.Count != original.Count)
            throw new IOException("The project changed while it was being packed. Save again.");

        for (int index = 0; index < original.Count; index++)
        {
            HashedSourceFile before = original[index];
            OurPlanPackageSourceFile after = current[index];
            if (!before.Source.LogicalPath.Equals(after.LogicalPath, StringComparison.OrdinalIgnoreCase) ||
                before.Source.Length != after.Length)
            {
                throw new IOException(
                    $"The project changed while it was being packed: {before.Source.LogicalPath}. Save again.");
            }

            OurPlanLocalFileStamp stamp = OurPlanLocalFileStamp.Read(after.FullPath);
            bool stable = before.StableStamp.IsStrong
                ? before.StableStamp.SameGeneration(stamp)
                : before.StableStamp.Length == stamp.Length &&
                  before.StableStamp.LastWriteUtcTicks == stamp.LastWriteUtcTicks &&
                  HashFile(after.FullPath).Equals(
                      before.WorkspaceSha256,
                      StringComparison.OrdinalIgnoreCase);
            if (!stable)
                throw new IOException(
                    $"The project changed while it was being packed: {before.Source.LogicalPath}. Save again.");
        }
    }

    private static string HashFile(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static PublishOutcome BuildOutcome(
        OurPlanPackageSaveResult result,
        OurPlanPackageManifest manifest,
        IReadOnlyList<HashedSourceFile> files,
        OurPlanPackageFingerprint fingerprint) =>
        new(
            result,
            manifest,
            files.ToDictionary(
                file => file.Source.LogicalPath,
                file => new OurPlanSavedWorkspaceFileState(
                    file.StableStamp,
                    file.WorkspaceSha256),
                StringComparer.OrdinalIgnoreCase),
            fingerprint);

    private static CompressionLevel CompressionFor(string path) =>
        AlreadyCompressedExtensions.Contains(Path.GetExtension(path))
            ? CompressionLevel.NoCompression
            : CompressionLevel.Fastest;

    private static string NormalizeTarget(string packagePath)
    {
        string fullPath = Path.GetFullPath(OurPlanPackageFormat.EnsureExtension(packagePath.Trim()));
        if (!OurPlanPackageFormat.HasPackageExtension(fullPath))
            throw new ArgumentException("An .ourplan destination is required.", nameof(packagePath));
        return fullPath;
    }

    private static string NormalizeGuid(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Guid.NewGuid().ToString("N");
        if (!Guid.TryParse(value, out Guid parsed))
            throw new ArgumentException($"Invalid {label} identifier.", nameof(value));
        return parsed.ToString("N");
    }

    private static string CleanDisplayName(string value)
    {
        string clean = string.IsNullOrWhiteSpace(value) ? "Untitled Project" : value.Trim();
        return clean.Length <= 200 ? clean : clean[..200];
    }

    private static void EnsureTargetOutsideWorkspace(string workspace, string targetPath)
    {
        string prefix = workspace + Path.DirectorySeparatorChar;
        if (targetPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Save the .ourplan file outside its private working folder.");
    }

    private static void TryDeleteTemp(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup; the original package was never replaced.
        }
    }

    private sealed record HashedSourceFile(
        OurPlanPackageSourceFile Source,
        string ContentPath,
        long ContentLength,
        string Sha256,
        long ManifestLastWriteUtcTicks,
        OurPlanLocalFileStamp StableStamp,
        string WorkspaceSha256);

    private sealed record PublishOutcome(
        OurPlanPackageSaveResult Result,
        OurPlanPackageManifest Manifest,
        IReadOnlyDictionary<string, OurPlanSavedWorkspaceFileState> FileStates,
        OurPlanPackageFingerprint Fingerprint);

    private sealed record PublishContext(
        string ProjectId,
        string RevisionId,
        string ParentRevisionId,
        string CreatedUtc,
        string DisplayName,
        string? ExpectedRevisionId,
        OurPlanPackageFingerprint? ExpectedFingerprint,
        bool AllowExistingDestination,
        OurPlanPackageManifest? BaseManifest,
        OurPlanPackageSession? SourceSession,
        string? ExpectedContentSha256 = null);

    private sealed record DestinationState(
        bool Exists,
        OurPlanPackageFingerprint? Fingerprint,
        string? RevisionId,
        OurPlanPackageManifest? Manifest,
        string? ContentSha256 = null);
}
