using OurPlanCore;

internal static class JobWriteAccessTests
{
    public static void ReadOnlySessionBlocksAtomicWritesButAllowsLeaseMetadata()
    {
        WithTempRoot(root =>
        {
            JobAccessSessionToken token = JobWriteAccess.RegisterJob(root, JobAccessMode.ReadOnly);
            try
            {
                string denied = Path.Combine(root, "blocked.json");
                JobWriteDeniedException error = Throws<JobWriteDeniedException>(
                    () => IoUtil.WriteAllTextAtomic(denied, "blocked"));

                AssertEqual(JobAccessMode.ReadOnly, error.AccessMode, "denied mode");
                AssertEqual(Path.GetFullPath(root), error.JobRoot, "denied root");
                AssertFalse(File.Exists(denied), "read-only file must not be created");
                AssertFalse(
                    Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories).Any(),
                    "read-only denial must not leave an atomic temp file");

                string lease = Path.Combine(root, ".~lock");
                string guard = Path.Combine(root, ".~lock.guard");
                IoUtil.WriteAllTextAtomic(lease, "lease");
                IoUtil.WriteAllTextAtomic(guard, "guard");
                AssertEqual("lease", File.ReadAllText(lease), "lease metadata write");
                AssertEqual("guard", File.ReadAllText(guard), "lease guard write");
            }
            finally
            {
                JobWriteAccess.Close(token);
            }
        });
    }

    public static void WritesOutsideRegisteredJobRemainAllowed()
    {
        WithTempRoot(root =>
        {
            string outsideRoot = Path.Combine(Path.GetTempPath(), "onc_write_outside_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outsideRoot);
            JobAccessSessionToken token = JobWriteAccess.RegisterJob(root, JobAccessMode.ReadOnly);
            try
            {
                string outside = Path.Combine(outsideRoot, "global.json");
                IoUtil.WriteAllTextAtomic(outside, "global");
                AssertEqual("global", File.ReadAllText(outside), "outside/global write");
            }
            finally
            {
                JobWriteAccess.Close(token);
                Directory.Delete(outsideRoot, recursive: true);
            }
        });
    }

    public static void ClosedSessionBlocksLateWritesAndOldTokenCannotReopen()
    {
        WithTempRoot(root =>
        {
            JobAccessSessionToken first = JobWriteAccess.RegisterJob(root, JobAccessMode.Writable);
            string firstPath = Path.Combine(root, "first.json");
            IoUtil.WriteAllTextAtomic(firstPath, "ok");
            JobWriteAccess.Close(first);

            Throws<InvalidOperationException>(() => JobWriteAccess.SetMode(first, JobAccessMode.Writable));
            Throws<JobWriteDeniedException>(
                () => IoUtil.WriteAllTextAtomic(Path.Combine(root, "late.json"), "late"));

            JobAccessSessionToken second = JobWriteAccess.RegisterJob(
                root + Path.DirectorySeparatorChar,
                JobAccessMode.ReadOnly);
            try
            {
                Throws<InvalidOperationException>(() => JobWriteAccess.SetMode(first, JobAccessMode.Writable));
                Throws<InvalidOperationException>(() => JobWriteAccess.SetMode(second, JobAccessMode.Writable));
                Throws<JobWriteDeniedException>(
                    () => IoUtil.WriteAllTextAtomic(Path.Combine(root, "stale.json"), "stale"));
            }
            finally
            {
                JobWriteAccess.Close(second);
            }
        });
    }

    public static void ReadOnlyLoadDoesNotCreateOrRepairJobStorage()
    {
        WithTempRoot(root =>
        {
            string sentinel = Path.Combine(root, "existing.txt");
            File.WriteAllText(sentinel, "keep");
            string before = TreeFingerprint(root);
            JobAccessSessionToken token = JobWriteAccess.RegisterJob(root, JobAccessMode.ReadOnly);
            try
            {
                OurPlanCoreJob job = OurPlanCoreJobStore.LoadJob(root, JobAccessMode.ReadOnly);

                AssertEqual(Path.GetFileName(root), job.Name, "read-only fallback job name");
                AssertEqual(before, TreeFingerprint(root), "read-only load tree");
                AssertFalse(File.Exists(Path.Combine(root, "Data.xml")), "root Data.xml must not be repaired");
                AssertFalse(Directory.Exists(job.PagesRoot), "Pages must not be created");
                AssertFalse(Directory.Exists(job.TakeoffsRoot), "Takeoffs must not be created");
                AssertFalse(Directory.Exists(job.AIContextRoot), "AI_Context must not be created");

                Throws<JobWriteDeniedException>(() => OurPlanCoreJobStore.LoadJob(root));
                AssertEqual(before, TreeFingerprint(root), "blocked writable load tree");
            }
            finally
            {
                JobWriteAccess.Close(token);
            }
        });
    }

    public static void ReadOnlyPageLoadSkipsSourceRepair()
    {
        WithTempRoot(root =>
        {
            string pages = Path.Combine(root, "Pages");
            string pageFolder = Path.Combine(pages, "A101");
            string pdf = Path.Combine(root, "plan.pdf");
            Directory.CreateDirectory(pageFolder);
            File.WriteAllText(pdf, "placeholder");
            OurPlanCoreJobStore.WriteItemDataXml(pageFolder, "Page", "A101", 1);
            OurPlanCoreJobStore.WriteSourcePdfMetadata(pageFolder, new PdfSheetMetadata
            {
                PdfPath = pdf,
                PageIndex = 0,
                SheetLabel = "A101",
            });

            string sourcePath = Path.Combine(pageFolder, "source.json");
            JobAccessSessionToken token = JobWriteAccess.RegisterJob(root, JobAccessMode.ReadOnly);
            try
            {
                PageInfo? page = OurPlanCoreJobStore.TryReadPage(pageFolder);
                AssertTrue(page == null, "missing source must stay missing in read-only mode");
                AssertFalse(File.Exists(sourcePath), "source.json must not be repaired");
            }
            finally
            {
                JobWriteAccess.Close(token);
            }
        });
    }

    public static void ReadOnlyMutatorsFailBeforeChangingJobTree()
    {
        WithTempRoot(root =>
        {
            string node = Path.Combine(root, "Takeoffs", "Walls");
            Directory.CreateDirectory(node);
            OurPlanCoreJobStore.WriteItemDataXml(node, "Item", "Walls", 1);
            string dataBefore = File.ReadAllText(Path.Combine(node, "Data.xml"));
            var job = new OurPlanCoreJob { Name = "Read Only", RootPath = root };

            JobAccessSessionToken token = JobWriteAccess.RegisterJob(root, JobAccessMode.ReadOnly);
            try
            {
                Throws<JobWriteDeniedException>(() => OurPlanCoreJobStore.RenameNode(node, "Renamed"));
                Throws<JobWriteDeniedException>(() => SmartContextStore.EnsureProjectContext(root, job.Name));
                Throws<JobWriteDeniedException>(() => SettingsPresetStore.SaveJobOverride(job, FolderTemplateConfig.BuildDefault()));

                AssertTrue(Directory.Exists(node), "original node must remain");
                AssertFalse(Directory.Exists(Path.Combine(root, "Takeoffs", "Renamed")), "renamed node must not appear");
                AssertEqual(dataBefore, File.ReadAllText(Path.Combine(node, "Data.xml")), "Data.xml content");
                AssertFalse(Directory.Exists(job.AIContextRoot), "AI/settings folders must not appear");
            }
            finally
            {
                JobWriteAccess.Close(token);
            }
        });
    }

    public static void ReadOnlyMaintenanceAndSnapshotsPreserveJobFiles()
    {
        WithTempRoot(root =>
        {
            string requests = Path.Combine(root, "AI_Context", "requests");
            string crops = Path.Combine(root, "AI_Context", "crops");
            Directory.CreateDirectory(requests);
            Directory.CreateDirectory(crops);
            string request = Path.Combine(requests, "old.json");
            string crop = Path.Combine(crops, "orphan.png");
            File.WriteAllText(request, "{\"status\":\"running\"}");
            File.WriteAllText(crop, "crop");
            File.SetLastWriteTimeUtc(request, DateTime.UtcNow.AddDays(-90));
            File.SetLastWriteTimeUtc(crop, DateTime.UtcNow.AddDays(-90));

            var job = new OurPlanCoreJob { Name = "Read Only", RootPath = root };
            string before = TreeFingerprint(root);
            JobAccessSessionToken token = JobWriteAccess.RegisterJob(root, JobAccessMode.ReadOnly);
            try
            {
                Throws<JobWriteDeniedException>(() => SmartContextStore.ArchiveStaleRequestFiles(root));
                Throws<JobWriteDeniedException>(() => SmartContextStore.ResetStuckRunningRequests(root));
                Throws<JobWriteDeniedException>(() => SmartContextStore.PruneOrphanCrops(root));
                Throws<JobWriteDeniedException>(() => JobRecoveryService.SaveSnapshot(job, "read only"));

                AssertEqual(before, TreeFingerprint(root), "read-only maintenance tree");
                AssertEqual("{\"status\":\"running\"}", File.ReadAllText(request), "request content");
                AssertEqual("crop", File.ReadAllText(crop), "crop content");
                AssertFalse(Directory.Exists(Path.Combine(root, "AI_Context", "archive")), "archive must not be created");
                AssertFalse(Directory.Exists(JobRecoveryService.SnapshotRoot(job)), "snapshot root must not be created");
            }
            finally
            {
                JobWriteAccess.Close(token);
            }
        });
    }

    public static void ReadOnlyRasterAndPageImageServicesPreserveJobFiles()
    {
        WithTempRoot(root =>
        {
            string pageFolder = Path.Combine(root, "Pages", "A100");
            string rasterFolder = Path.Combine(pageFolder, RasterSheetCacheService.CacheFolderName);
            Directory.CreateDirectory(rasterFolder);
            string orphan = Path.Combine(rasterFolder, "working-orphan.tmp");
            File.WriteAllText(orphan, "keep");
            string output = Path.Combine(pageFolder, "page_tools", "output.png");
            var page = new PageInfo { Name = "A100", FolderPath = pageFolder };
            string before = TreeFingerprint(root);

            JobAccessSessionToken token = JobWriteAccess.RegisterJob(root, JobAccessMode.ReadOnly);
            try
            {
                Throws<JobWriteDeniedException>(() => RasterSheetCacheService.CompactCache(page));
                Throws<JobWriteDeniedException>(() => PageImageOperationService.RenderPageToPng(page, output));

                AssertEqual(before, TreeFingerprint(root), "read-only raster/page-image tree");
                AssertEqual("keep", File.ReadAllText(orphan), "raster cache content");
                AssertFalse(File.Exists(output), "page image output must not be created");
            }
            finally
            {
                JobWriteAccess.Close(token);
            }
        });
    }

    public static void DirectStorageMutationsHaveAdjacentWriteDemand()
    {
        string[] mutationTokens =
        [
            "Directory.CreateDirectory(",
            "Directory.Delete(",
            "File.WriteAllBytes(",
            "File.WriteAllText(",
            "File.AppendAllText(",
            "File.Move(",
            "File.Copy(",
            "File.Delete(",
            "File.Create(",
            ".Delete(recursive: true)",
        ];

        foreach (string relativePath in new[]
                 {
                     "Models/RasterSheetCacheService.cs",
                     "Models/SmartContextStore.cs",
                     "Models/JobRecoveryService.cs",
                     "Models/PageImageOperationService.cs",
                 })
        {
            string[] lines = File.ReadAllLines(RepoFile(relativePath));
            for (int index = 0; index < lines.Length; index++)
            {
                if (!mutationTokens.Any(token => lines[index].Contains(token, StringComparison.Ordinal)))
                    continue;

                int previous = index - 1;
                while (previous >= 0 && string.IsNullOrWhiteSpace(lines[previous]))
                    previous--;

                AssertTrue(
                    previous >= 0 && lines[previous].Contains("JobWriteAccess.Demand(", StringComparison.Ordinal),
                    $"{relativePath}:{index + 1} direct mutation must have an adjacent write demand");
            }
        }
    }

    public static void ConcurrentDemandsObserveOneNormalizedMode()
    {
        WithTempRoot(root =>
        {
            JobAccessSessionToken token = JobWriteAccess.RegisterJob(root, JobAccessMode.ReadOnly);
            try
            {
                int denied = 0;
                Parallel.For(0, 200, index =>
                {
                    try
                    {
                        JobWriteAccess.Demand(
                            Path.Combine(root, "nested", index.ToString(), "item.json"),
                            "test concurrent write");
                    }
                    catch (JobWriteDeniedException)
                    {
                        Interlocked.Increment(ref denied);
                    }
                });

                AssertEqual(200, denied, "concurrent denied count");
                AssertEqual(JobAccessMode.ReadOnly, JobWriteAccess.GetMode(Path.Combine(root, "nested")), "normalized mode");
            }
            finally
            {
                JobWriteAccess.Close(token);
            }
        });
    }

    private static void WithTempRoot(Action<string> run)
    {
        string root = Path.Combine(Path.GetTempPath(), "onc_job_access_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        JobWriteAccess.ResetForTests();
        try
        {
            run(root);
        }
        finally
        {
            JobWriteAccess.ResetForTests();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static string TreeFingerprint(string root) =>
        string.Join(
            "\n",
            Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));

    private static string RepoFile(string relativePath) =>
        Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string RepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ourplancore.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("OurPlanCore repository root was not found.");
    }

    private static T Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T error)
        {
            return error;
        }

        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message) =>
        AssertTrue(!condition, message);

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected '{expected}', got '{actual}'.");
    }
}
