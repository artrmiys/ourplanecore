using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using OurPlanCore;

internal static partial class OurPlanPackageTests
{
    public static void NewProjectsDefaultToOurPlanFormat()
    {
        var settings = new AppSettings();
        AssertEqual("OurPlan", settings.NewProjectStorageFormat, "new project storage default");
    }

    public static void RoundTripPreservesDurableDataAndDeduplicatesObjects()
    {
        using var fixture = PackageFixture.Create();
        string projectContextPath = Path.Combine(fixture.Job.AIContextRoot, "project.json");
        byte[] originalProjectContext = File.ReadAllBytes(projectContextPath);
        OurPlanPackageSession session = fixture.CreatePackage();
        AssertSequenceEqual(
            originalProjectContext,
            File.ReadAllBytes(projectContextPath),
            "portable packaging must not mutate live AI project metadata");
        OurPlanPackageManifest manifest = OurPlanPackageArchive.ReadManifest(session.PackagePath);

        int uniqueObjects = manifest.Files
            .Select(file => file.ObjectSha256)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        AssertTrue(uniqueObjects < manifest.Files.Count, "identical logical files should share one object");
        AssertFalse(manifest.Files.Any(file => file.Path.Contains(".snapshots", StringComparison.OrdinalIgnoreCase)),
            "recovery snapshots must be excluded");
        AssertFalse(manifest.Files.Any(file => file.Path.Contains(".undo", StringComparison.OrdinalIgnoreCase)),
            "delete undo trash must be excluded");
        AssertFalse(manifest.Files.Any(file => file.Path.EndsWith("unused.png", StringComparison.OrdinalIgnoreCase)),
            "unused raster cache variants must be excluded");
        AssertTrue(manifest.Files.Any(file => file.Path.EndsWith("active.png", StringComparison.OrdinalIgnoreCase)),
            "active raster cache must be retained");
        AssertTrue(manifest.Files.Any(file => file.Path.Equals("bookmarks.json", StringComparison.OrdinalIgnoreCase)),
            "unknown durable root data must be retained");
        AssertTrue(manifest.Files.Any(file => file.Path.StartsWith("3D_Context/", StringComparison.OrdinalIgnoreCase)),
            "3D project data must be retained");

        string extracted = Path.Combine(fixture.Parent, "extracted");
        OurPlanPackageArchive.Extract(session.PackagePath, extracted);
        foreach (OurPlanPackageFileManifest file in manifest.Files)
        {
            string original = Path.Combine(fixture.Job.RootPath, file.Path.Replace('/', Path.DirectorySeparatorChar));
            string restored = Path.Combine(extracted, file.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!file.Path.Equals("AI_Context/project.json", StringComparison.OrdinalIgnoreCase))
                AssertSequenceEqual(File.ReadAllBytes(original), File.ReadAllBytes(restored), file.Path);
            AssertEqual(file.LastWriteUtcTicks, File.GetLastWriteTimeUtc(restored).Ticks, $"mtime {file.Path}");
        }

        SmartProjectContext? portableContext = JsonSerializer.Deserialize<SmartProjectContext>(
            File.ReadAllText(Path.Combine(extracted, "AI_Context", "project.json")));
        AssertTrue(portableContext != null, "portable AI project metadata should deserialize");
        AssertEqual(".", portableContext!.RootPath, "AI project root should be job-relative in the package");
    }

    public static void PackageCompressionShrinksLargeSnapJsonWithoutChangingIt()
    {
        using var fixture = PackageFixture.Create(includeLargeSnapJson: true);
        byte[] original = File.ReadAllBytes(fixture.ActiveSnapPath);
        OurPlanPackageSession session = fixture.CreatePackage();
        AssertTrue(new FileInfo(session.PackagePath).Length < fixture.SourceBytes,
            "compressible project data should make the package smaller than raw source bytes");

        string extracted = Path.Combine(fixture.Parent, "snap-extracted");
        OurPlanPackageArchive.Extract(session.PackagePath, extracted);
        string relative = Path.GetRelativePath(fixture.Job.RootPath, fixture.ActiveSnapPath);
        AssertSequenceEqual(original, File.ReadAllBytes(Path.Combine(extracted, relative)), "snap json bytes");
    }

    public static void PackageRejectsTraversalPaths()
    {
        string root = NewTempRoot();
        try
        {
            string package = Path.Combine(root, "traversal.ourplan");
            WriteSyntheticPackage(package, "../escape.txt", Encoding.UTF8.GetBytes("x"), correctHash: true);
            AssertThrows<OurPlanPackageValidationException>(
                () => OurPlanPackageArchive.ReadManifest(package),
                "traversal manifest path must be rejected");
            AssertFalse(File.Exists(Path.Combine(root, "escape.txt")), "validation must not extract traversal data");
        }
        finally
        {
            TryDelete(root);
        }
    }

    public static void PackageRejectsObjectHashMismatch()
    {
        string root = NewTempRoot();
        try
        {
            string package = Path.Combine(root, "bad-hash.ourplan");
            WriteSyntheticPackage(package, "Data.xml", Encoding.UTF8.GetBytes("not the declared hash"), correctHash: false);
            AssertThrows<OurPlanPackageValidationException>(
                () => OurPlanPackageArchive.ReadManifest(package),
                "object hash mismatch must be rejected");
        }
        finally
        {
            TryDelete(root);
        }
    }

    public static void PackageSaveDetectsExternalRevisionConflict()
    {
        using var fixture = PackageFixture.Create();
        OurPlanPackageSession session = fixture.CreatePackage();
        string externalRoot = Path.Combine(fixture.Parent, "external-writer-conflict");
        OurPlanPackageArchive.Extract(session.PackagePath, externalRoot);
        File.WriteAllText(Path.Combine(externalRoot, "bookmarks.json"), "{\"items\":[99]}");
        OurPlanPackageManifest manifest = OurPlanPackageArchive.ReadManifest(
            session.PackagePath,
            verifyObjects: false);
        var external = new OurPlanPackageSession
        {
            PackagePath = session.PackagePath,
            WorkspaceRoot = externalRoot,
            ProjectId = manifest.ProjectId,
            DisplayName = manifest.DisplayName,
            BaseRevisionId = manifest.RevisionId,
            BaseFingerprint = OurPlanPackageFingerprint.Read(session.PackagePath),
            HasUnpackagedChanges = true,
        };
        OurPlanPackageWriter.Save(external);
        OurPlanPackageWorkspace.MarkSessionClosed(external);

        AssertThrows<OurPlanPackageConflictException>(
            () => OurPlanPackageWriter.Save(session),
            "a valid external revision must block overwrite");
        AssertTrue(session.BaseRevisionId.Length > 0, "session revision should remain available after conflict");
    }

    public static void UnchangedSaveDoesNotRewritePackage()
    {
        using var fixture = PackageFixture.Create();
        OurPlanPackageSession session = fixture.CreatePackage();
        string revision = session.BaseRevisionId;
        OurPlanPackageFingerprint fingerprint = session.BaseFingerprint;

        string dataXml = Path.Combine(fixture.Job.RootPath, "Data.xml");
        string sameContents = File.ReadAllText(dataXml);
        Thread.Sleep(20);
        File.WriteAllText(dataXml, sameContents);
        OurPlanPackageSaveResult result = OurPlanPackageWriter.Save(session);

        AssertEqual(revision, result.RevisionId, "unchanged content should keep package revision");
        AssertEqual(fingerprint, OurPlanPackageFingerprint.Read(session.PackagePath),
            "unchanged content should not replace the package file");
        AssertFalse(session.HasUnpackagedChanges,
            "portable metadata differences must not leave a successfully saved workspace dirty");
    }

    public static void SameSizeAndTimestampChangeIsNeverSkipped()
    {
        using var fixture = PackageFixture.Create();
        string largePath = Path.Combine(fixture.Job.RootPath, "3D_Context", "large.bin");
        File.WriteAllBytes(largePath, new byte[9 * 1024 * 1024]);
        OurPlanPackageSession session = fixture.CreatePackage();
        string initialRevision = session.BaseRevisionId;
        DateTime originalTimestamp = File.GetLastWriteTimeUtc(largePath);

        using (var stream = new FileStream(largePath, FileMode.Open, FileAccess.Write, FileShare.None))
            stream.WriteByte(0x7f);
        File.SetLastWriteTimeUtc(largePath, originalTimestamp);

        OurPlanPackageSaveResult result = OurPlanPackageWriter.Save(session);
        AssertFalse(result.RevisionId.Equals(initialRevision, StringComparison.OrdinalIgnoreCase),
            "changed large content must create a new revision even when size and mtime match");

        string extracted = Path.Combine(fixture.Parent, "large-extracted");
        OurPlanPackageArchive.Extract(session.PackagePath, extracted);
        AssertEqual(0x7f, (int)File.ReadAllBytes(Path.Combine(extracted, "3D_Context", "large.bin"))[0],
            "changed large content must be stored in the package");
    }

    public static void FailedPackageSaveLeavesPreviousFileByteExact()
    {
        using var fixture = PackageFixture.Create();
        OurPlanPackageSession session = fixture.CreatePackage();
        byte[] before = File.ReadAllBytes(session.PackagePath);

        string page = fixture.PageFolder;
        var externalSource = new SourceInfo
        {
            Pdf = Path.Combine(fixture.Parent, "external.pdf"),
            Page = 0,
        };
        File.WriteAllBytes(externalSource.Pdf, Encoding.UTF8.GetBytes("external"));
        File.WriteAllText(Path.Combine(page, "source.json"), JsonSerializer.Serialize(externalSource));

        AssertThrows<OurPlanPackageValidationException>(
            () => OurPlanPackageWriter.Save(session),
            "external page dependency must block a non-portable package save");
        AssertSequenceEqual(before, File.ReadAllBytes(session.PackagePath), "previous package after failed save");
    }

    public static void PackageRejectsActivePdfInsideExcludedData()
    {
        using var fixture = PackageFixture.Create();
        string excludedPdf = Path.Combine(fixture.Job.RootPath, ".undo", "hidden-source.pdf");
        File.WriteAllText(excludedPdf, "%PDF-1.7 excluded");
        File.WriteAllText(
            Path.Combine(fixture.PageFolder, "source.json"),
            JsonSerializer.Serialize(new SourceInfo
            {
                Pdf = Path.GetRelativePath(fixture.PageFolder, excludedPdf),
                Page = 0,
            }));

        AssertThrows<OurPlanPackageValidationException>(
            () => fixture.CreatePackage(),
            "an active PDF excluded from the archive must block package creation");
    }

    public static void StaleCleanWorkspaceIsReExtractedAfterExternalRevision()
    {
        using var fixture = PackageFixture.Create();
        OurPlanPackageSession created = fixture.CreatePackage();
        var managed = new List<OurPlanPackageSession>();
        try
        {
            OurPlanPackageSession first = OurPlanPackageWorkspace.Open(created.PackagePath);
            managed.Add(first);
            OurPlanPackageWorkspace.MarkSessionClosed(first);

            string externalRoot = Path.Combine(fixture.Parent, "external-writer");
            OurPlanPackageArchive.Extract(created.PackagePath, externalRoot);
            File.WriteAllText(Path.Combine(externalRoot, "bookmarks.json"), "{\"items\":[9]}");
            OurPlanPackageManifest baseManifest = OurPlanPackageArchive.ReadManifest(
                created.PackagePath,
                verifyObjects: false);
            var external = new OurPlanPackageSession
            {
                PackagePath = created.PackagePath,
                WorkspaceRoot = externalRoot,
                ProjectId = baseManifest.ProjectId,
                DisplayName = baseManifest.DisplayName,
                BaseRevisionId = baseManifest.RevisionId,
                BaseFingerprint = OurPlanPackageFingerprint.Read(created.PackagePath),
                HasUnpackagedChanges = true,
            };
            OurPlanPackageWriter.Save(external);

            OurPlanPackageSession reopened = OurPlanPackageWorkspace.Open(created.PackagePath);
            managed.Add(reopened);
            AssertFalse(SamePath(first.WorkspaceRoot, reopened.WorkspaceRoot),
                "an old clean revision must not be reused for a newer package revision");
            AssertTrue(
                File.ReadAllText(Path.Combine(reopened.WorkspaceRoot, "bookmarks.json"))
                    .Contains("[9]", StringComparison.Ordinal),
                "the reopened workspace must contain the newer package data");
            AssertFalse(reopened.AvailableRecoverySessions.Any(info =>
                    SamePath(info.WorkspaceRoot, first.WorkspaceRoot)),
                "an unchanged stale workspace is not an unsaved recovery");
        }
        finally
        {
            CloseAndDeleteManagedWorkspaces(managed);
        }
    }

    public static void DirtyClosedWorkspaceIsAdvertisedButPackageOpensByDefault()
    {
        using var fixture = PackageFixture.Create();
        OurPlanPackageSession created = fixture.CreatePackage();
        var managed = new List<OurPlanPackageSession>();
        try
        {
            OurPlanPackageSession dirty = OurPlanPackageWorkspace.Open(created.PackagePath);
            managed.Add(dirty);
            File.WriteAllText(
                Path.Combine(dirty.WorkspaceRoot, "bookmarks.json"),
                "{\"items\":[77]}");
            dirty.HasUnpackagedChanges = true;
            OurPlanPackageWorkspace.MarkSessionClosed(dirty);

            IReadOnlyList<OurPlanPackageRecoveryInfo> advertised =
                OurPlanPackageWorkspace.FindRecoverySessions(created.PackagePath);
            AssertTrue(advertised.Any(info => SamePath(info.WorkspaceRoot, dirty.WorkspaceRoot)),
                "the dirty closed workspace must be advertised for recovery");

            OurPlanPackageSession canonical = OurPlanPackageWorkspace.Open(created.PackagePath);
            managed.Add(canonical);
            AssertFalse(canonical.IsRecoverySession,
                "normal Open must select package data, not local recovery");
            AssertFalse(SamePath(dirty.WorkspaceRoot, canonical.WorkspaceRoot),
                "normal Open must allocate a separate workspace when the old one is dirty");
            AssertTrue(
                File.ReadAllText(Path.Combine(canonical.WorkspaceRoot, "bookmarks.json"))
                    .Contains("[1,2,3]", StringComparison.Ordinal),
                "normal Open must preserve the package's canonical contents");
            AssertTrue(canonical.AvailableRecoverySessions.Any(info =>
                    SamePath(info.WorkspaceRoot, dirty.WorkspaceRoot)),
                "the canonical session must expose the preserved dirty recovery");
        }
        finally
        {
            CloseAndDeleteManagedWorkspaces(managed);
        }
    }

    public static void SameRevisionPackageCopyUsesSeparateWorkspace()
    {
        using var fixture = PackageFixture.Create();
        OurPlanPackageSession created = fixture.CreatePackage();
        string copiedPackage = Path.Combine(fixture.Parent, "copied-project.ourplan");
        File.Copy(created.PackagePath, copiedPackage);
        var managed = new List<OurPlanPackageSession>();
        try
        {
            OurPlanPackageSession original = OurPlanPackageWorkspace.Open(created.PackagePath);
            managed.Add(original);
            OurPlanPackageWorkspace.MarkSessionClosed(original);

            OurPlanPackageSession copied = OurPlanPackageWorkspace.Open(copiedPackage);
            managed.Add(copied);
            AssertEqual(original.ProjectId, copied.ProjectId, "copied package project identity");
            AssertEqual(original.BaseRevisionId, copied.BaseRevisionId, "copied package revision identity");
            AssertFalse(SamePath(original.WorkspaceRoot, copied.WorkspaceRoot),
                "a different exact package path must never reuse another copy's workspace");
        }
        finally
        {
            CloseAndDeleteManagedWorkspaces(managed);
        }
    }

    public static void ActiveWorkspaceClaimPreventsConcurrentReuse()
    {
        using var fixture = PackageFixture.Create();
        OurPlanPackageSession created = fixture.CreatePackage();
        var managed = new List<OurPlanPackageSession>();
        try
        {
            OurPlanPackageSession first = OurPlanPackageWorkspace.Open(created.PackagePath);
            managed.Add(first);
            OurPlanPackageSession concurrent = OurPlanPackageWorkspace.Open(created.PackagePath);
            managed.Add(concurrent);
            AssertFalse(SamePath(first.WorkspaceRoot, concurrent.WorkspaceRoot),
                "an active exclusive claim must prevent another session from reusing the workspace");
        }
        finally
        {
            CloseAndDeleteManagedWorkspaces(managed);
        }
    }

    public static void PruneCannotRaceAnExclusiveWorkspaceClaim()
    {
        using var fixture = PackageFixture.Create();
        OurPlanPackageSession created = fixture.CreatePackage();
        var managed = new List<OurPlanPackageSession>();
        try
        {
            OurPlanPackageSession session = OurPlanPackageWorkspace.Open(created.PackagePath);
            managed.Add(session);
            OurPlanPackageWorkspace.MarkSessionClosed(session);
            string markerPath = Path.Combine(
                session.WorkspaceRoot,
                OurPlanPackageFormat.WorkspaceMarkerFileName);
            JsonObject marker = JsonNode.Parse(File.ReadAllText(markerPath))?.AsObject()
                ?? throw new InvalidOperationException("workspace marker JSON missing");
            marker["state_updated_utc"] = "2000-01-01T00:00:00.0000000Z";
            File.WriteAllText(markerPath, marker.ToJsonString());

            string guardPath = Path.Combine(
                session.WorkspaceRoot,
                OurPlanPackageFormat.WorkspaceClaimFileName);
            using (var externalClaim = new FileStream(
                       guardPath,
                       FileMode.OpenOrCreate,
                       FileAccess.ReadWrite,
                       FileShare.Delete))
            {
                bool prunedDuringClaim = OurPlanPackageWorkspace.TryPruneCleanClosedWorkspace(
                    session.WorkspaceRoot,
                    DateTime.UtcNow,
                    DateTime.UtcNow);
                AssertFalse(prunedDuringClaim, "prune must not pass an exclusive cross-process claim");
                AssertTrue(Directory.Exists(session.WorkspaceRoot),
                    "claimed workspace must remain in place");
            }

            bool prunedAfterRelease = OurPlanPackageWorkspace.TryPruneCleanClosedWorkspace(
                session.WorkspaceRoot,
                DateTime.UtcNow,
                DateTime.UtcNow);
            AssertTrue(prunedAfterRelease, "clean stale workspace should prune after claim release");
            AssertFalse(Directory.Exists(session.WorkspaceRoot),
                "prune must quarantine and remove the exact workspace");
        }
        finally
        {
            CloseAndDeleteManagedWorkspaces(managed);
        }
    }

    public static void CorruptAndMissingPackageCanOpenPreservedRecovery()
    {
        using var fixture = PackageFixture.Create();
        OurPlanPackageSession created = fixture.CreatePackage();
        var managed = new List<OurPlanPackageSession>();
        try
        {
            OurPlanPackageSession dirty = OurPlanPackageWorkspace.Open(created.PackagePath);
            managed.Add(dirty);
            File.WriteAllText(
                Path.Combine(dirty.WorkspaceRoot, "bookmarks.json"),
                "{\"items\":[88]}");
            dirty.HasUnpackagedChanges = true;
            OurPlanPackageWorkspace.MarkSessionClosed(dirty);

            File.WriteAllText(created.PackagePath, "not a valid package");
            OurPlanPackageRecoveryInfo corruptRecovery =
                OurPlanPackageWorkspace.FindRecoverySessions(created.PackagePath).FirstOrDefault()
                ?? throw new InvalidOperationException("corrupt package recovery was not advertised");
            AssertTrue(
                OurPlanPackageWorkspace.TryOpenRecoverySession(corruptRecovery, out OurPlanPackageSession? recovered) &&
                recovered != null,
                "a corrupt package must allow its preserved local recovery to open");
            managed.Add(recovered!);
            AssertTrue(recovered!.IsRecoverySession, "corrupt package recovery session flag");
            AssertTrue(SamePath(dirty.WorkspaceRoot, recovered.WorkspaceRoot),
                "recovery must open the exact preserved workspace");
            OurPlanPackageWorkspace.MarkSessionClosed(recovered);

            File.Delete(created.PackagePath);
            IReadOnlyList<OurPlanPackageRecoveryInfo> missingRecoveries =
                OurPlanPackageWorkspace.FindRecoverySessions(created.PackagePath);
            AssertTrue(missingRecoveries.Any(info => SamePath(info.WorkspaceRoot, dirty.WorkspaceRoot)),
                "a missing package must retain its exact-path recovery");
            AssertTrue(
                OurPlanPackageWorkspace.TryOpenRecoverySession(
                    missingRecoveries.First(info => SamePath(info.WorkspaceRoot, dirty.WorkspaceRoot)),
                    out OurPlanPackageSession? missingRecovered) && missingRecovered != null,
                "a missing package recovery must remain openable for Save As");
            managed.Add(missingRecovered!);
        }
        finally
        {
            CloseAndDeleteManagedWorkspaces(managed);
        }
    }

    public static void ManagedLegacyCopyDoesNotMutateOriginalJob()
    {
        using var fixture = PackageFixture.Create();
        string sourceDataPath = Path.Combine(fixture.Job.RootPath, "Data.xml");
        XDocument sourceData = XDocument.Load(sourceDataPath);
        XElement sourceRoot = sourceData.Root
            ?? throw new InvalidOperationException("source root Data.xml is empty");
        string originalGuid = sourceRoot.Attribute("GUID")?.Value
            ?? throw new InvalidOperationException("source root GUID is missing");
        sourceRoot.SetAttributeValue("CustomRootAttribute", "preserve-me");
        sourceRoot.Element("Properties")?.Add(
            new XElement(
                "Property",
                new XAttribute("Name", "CustomRootProperty"),
                new XAttribute("Value", "preserve-me-too")));
        sourceData.Save(sourceDataPath);
        byte[] originalBookmarks = File.ReadAllBytes(Path.Combine(fixture.Job.RootPath, "bookmarks.json"));
        OurPlanCoreJob? managedJob = null;
        string projectId = "";
        try
        {
            (managedJob, projectId, _) = OurPlanPackageWorkspace.CreateManagedCopyFromJob(
                fixture.Job.RootPath,
                "Managed Legacy Copy");
            AssertEqual("Managed Legacy Copy", managedJob.Name, "managed copy display name");
            AssertFalse(SamePath(fixture.Job.RootPath, managedJob.RootPath),
                "the managed copy must have an independent private root");
            XDocument managedData = XDocument.Load(Path.Combine(managedJob.RootPath, "Data.xml"));
            XElement managedRoot = managedData.Root
                ?? throw new InvalidOperationException("managed root Data.xml is empty");
            AssertEqual(originalGuid, managedRoot.Attribute("GUID")?.Value ?? "",
                "managed copy root GUID");
            AssertEqual("preserve-me", managedRoot.Attribute("CustomRootAttribute")?.Value ?? "",
                "managed copy custom root attribute");
            AssertTrue(managedRoot.Descendants("Property").Any(property =>
                    property.Attribute("Name")?.Value == "CustomRootProperty" &&
                    property.Attribute("Value")?.Value == "preserve-me-too"),
                "managed copy custom root property");

            File.WriteAllText(
                Path.Combine(managedJob.RootPath, "bookmarks.json"),
                "{\"items\":[999]}");
            AssertSequenceEqual(
                originalBookmarks,
                File.ReadAllBytes(Path.Combine(fixture.Job.RootPath, "bookmarks.json")),
                "legacy source after managed-copy edit");
        }
        finally
        {
            if (managedJob != null)
                TryDeleteManagedWorkspace(projectId, managedJob.RootPath);
        }
    }

    public static void LegacyFolderCopyIsLoadableAndExcludesEphemeralFiles()
    {
        using var fixture = PackageFixture.Create();
        string destination = Path.Combine(fixture.Parent, "legacy-copy");
        OurPlanPackageWorkspace.ExportLegacyCopy(fixture.Job.RootPath, destination);

        OurPlanCoreJob loaded = OurPlanCoreJobStore.LoadJob(destination);
        AssertTrue(File.Exists(Path.Combine(loaded.RootPath, "Data.xml")), "legacy copy Data.xml");
        AssertTrue(File.Exists(Path.Combine(loaded.RootPath, "bookmarks.json")), "legacy copy durable unknown data");
        AssertFalse(Directory.Exists(Path.Combine(loaded.RootPath, ".snapshots")), "legacy copy snapshots excluded");
        AssertFalse(File.Exists(Path.Combine(loaded.RootPath, ".~lock")), "legacy copy lock excluded");
    }

    private static void WriteSyntheticPackage(
        string packagePath,
        string logicalPath,
        byte[] data,
        bool correctHash)
    {
        string hash = correctHash
            ? Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant()
            : new string('0', 64);
        var manifest = new OurPlanPackageManifest
        {
            ProjectId = Guid.NewGuid().ToString("N"),
            RevisionId = Guid.NewGuid().ToString("N"),
            DisplayName = "Synthetic",
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            SavedUtc = DateTime.UtcNow.ToString("O"),
            Files =
            [
                new OurPlanPackageFileManifest
                {
                    Path = logicalPath,
                    ObjectSha256 = hash,
                    Length = data.LongLength,
                    LastWriteUtcTicks = DateTime.UtcNow.Ticks,
                },
            ],
        };

        using var stream = File.Create(packagePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        ZipArchiveEntry objectEntry = archive.CreateEntry(
            OurPlanPackageFormat.ObjectEntryName(hash),
            CompressionLevel.NoCompression);
        using (Stream objectStream = objectEntry.Open())
            objectStream.Write(data);
        ZipArchiveEntry manifestEntry = archive.CreateEntry(
            OurPlanPackageFormat.ManifestEntryName,
            CompressionLevel.Fastest);
        using Stream manifestStream = manifestEntry.Open();
        JsonSerializer.Serialize(manifestStream, manifest);
    }

    private sealed class PackageFixture : IDisposable
    {
        private readonly List<OurPlanPackageSession> _sessions = [];

        private PackageFixture(string parent, OurPlanCoreJob job, string pageFolder, string activeSnapPath)
        {
            Parent = parent;
            Job = job;
            PageFolder = pageFolder;
            ActiveSnapPath = activeSnapPath;
        }

        public string Parent { get; }
        public OurPlanCoreJob Job { get; }
        public string PageFolder { get; }
        public string ActiveSnapPath { get; }
        public long SourceBytes => Directory.EnumerateFiles(Job.RootPath, "*", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length);

        public static PackageFixture Create(bool includeLargeSnapJson = false)
        {
            string parent = NewTempRoot();
            OurPlanCoreJob job = OurPlanCoreJobStore.CreateJob(parent, "Package Fixture");
            File.WriteAllText(Path.Combine(job.RootPath, "bookmarks.json"), "{\"items\":[1,2,3]}");
            Directory.CreateDirectory(Path.Combine(job.RootPath, "3D_Context"));
            File.WriteAllText(Path.Combine(job.RootPath, "3D_Context", "model.json"), "{\"walls\":[]}");

            byte[] pdfBytes = Encoding.UTF8.GetBytes("%PDF-1.7\nbyte-exact-fixture\n%%EOF");
            string sourcePdf = Path.Combine(job.RootPath, "sources", "plans.pdf");
            File.WriteAllBytes(sourcePdf, pdfBytes);
            File.WriteAllBytes(Path.Combine(job.RootPath, "sources", "plans-copy.pdf"), pdfBytes);

            string page = Path.Combine(job.PagesRoot, "A1.01");
            Directory.CreateDirectory(page);
            OurPlanCoreJobStore.WriteItemDataXml(page, "Page", "A1.01", 1);
            string raster = Path.Combine(page, RasterSheetCacheService.CacheFolderName);
            Directory.CreateDirectory(raster);
            string activeImage = Path.Combine(raster, "active.png");
            File.WriteAllBytes(activeImage, [0x89, 0x50, 0x4E, 0x47, 0x01, 0x02]);
            File.WriteAllBytes(Path.Combine(raster, "unused.png"), [0x89, 0x50, 0x4E, 0x47, 0x09]);
            string snap = Path.Combine(raster, "snap.json");
            string snapText = includeLargeSnapJson
                ? "{\"points\":[" + string.Join(',', Enumerable.Repeat("[123.456,789.012]", 80_000)) + "]}"
                : "{\"points\":[[1,2],[3,4]]}";
            File.WriteAllText(snap, snapText);
            var source = new SourceInfo
            {
                Pdf = Path.GetRelativePath(page, sourcePdf),
                Page = 0,
                RasterSheet = new RasterSheetSource
                {
                    Enabled = true,
                    UseAsPageOpenRaster = true,
                    Image = Path.GetRelativePath(page, activeImage),
                    OverviewImage = Path.GetRelativePath(page, activeImage),
                    SnapIndex = Path.GetRelativePath(page, snap),
                },
            };
            File.WriteAllText(Path.Combine(page, "source.json"), JsonSerializer.Serialize(source));

            Directory.CreateDirectory(Path.Combine(job.RootPath, ".snapshots", "old"));
            File.WriteAllText(Path.Combine(job.RootPath, ".snapshots", "old", "Data.xml"), "old");
            Directory.CreateDirectory(Path.Combine(job.RootPath, ".undo"));
            File.WriteAllText(Path.Combine(job.RootPath, ".undo", "deleted.json"), "{}");
            File.WriteAllText(Path.Combine(job.RootPath, ".~lock"), "lease");
            return new PackageFixture(parent, job, page, snap);
        }

        public OurPlanPackageSession CreatePackage()
        {
            OurPlanPackageSession session = OurPlanPackageWriter.Create(
                Job.RootPath,
                Path.Combine(Parent, $"{Guid.NewGuid():N}.ourplan"),
                Job.Name);
            _sessions.Add(session);
            return session;
        }

        public void Dispose()
        {
            foreach (OurPlanPackageSession session in _sessions)
            {
                try
                {
                    if (Directory.Exists(session.WorkspaceRoot))
                        OurPlanPackageWorkspace.MarkSessionClosed(session);
                }
                catch
                {
                    // The fixture may intentionally corrupt or remove its package.
                }
            }
            TryDelete(Parent);
        }
    }

    private static string NewTempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "ourplan_package_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void AssertThrows<T>(Action action, string message) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private static void AssertSequenceEqual(byte[] expected, byte[] actual, string message)
    {
        if (!expected.AsSpan().SequenceEqual(actual))
            throw new InvalidOperationException($"{message}: byte sequences differ");
    }

    private static void AssertEqual<T>(T expected, T actual, string message) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }

    private static void AssertTrue(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool value, string message) => AssertTrue(!value, message);

    private static bool SamePath(string left, string right) =>
        Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static void CloseAndDeleteManagedWorkspaces(
        IEnumerable<OurPlanPackageSession> sessions)
    {
        foreach (IGrouping<string, OurPlanPackageSession> group in sessions.GroupBy(
                     session => Path.GetFullPath(session.WorkspaceRoot),
                     StringComparer.OrdinalIgnoreCase))
        {
            OurPlanPackageSession session = group.Last();
            try
            {
                if (Directory.Exists(session.WorkspaceRoot))
                    OurPlanPackageWorkspace.MarkSessionClosed(session);
            }
            catch
            {
                // Exact test workspace cleanup continues below.
            }
            TryDeleteManagedWorkspace(session.ProjectId, session.WorkspaceRoot);
        }
    }

    private static void TryDeleteManagedWorkspace(string projectId, string workspaceRoot)
    {
        try
        {
            if (!Guid.TryParse(projectId, out Guid parsed))
                return;
            string projectRoot = Path.GetFullPath(Path.Combine(
                AppIdentity.LocalRoot,
                "project-workspaces",
                parsed.ToString("N")));
            string workspace = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
            if (!string.Equals(
                    Path.GetDirectoryName(workspace),
                    projectRoot,
                    StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(workspace).StartsWith("working", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (Directory.Exists(workspace) &&
                (new DirectoryInfo(workspace).Attributes & FileAttributes.ReparsePoint) == 0)
            {
                Directory.Delete(workspace, recursive: true);
            }
            if (Directory.Exists(projectRoot) &&
                !Directory.EnumerateFileSystemEntries(projectRoot).Any())
            {
                Directory.Delete(projectRoot, recursive: false);
            }
        }
        catch
        {
            // Test cleanup is best effort and limited to one validated managed workspace.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Test cleanup only.
        }
    }
}
