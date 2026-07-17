using System.Security.Cryptography;
using System.Text.Json;
using OurPlanCore;

internal sealed class TakeoffFolderSyncOptions
{
    public string SourceJobPath { get; init; } = "";
    public string TargetJobPath { get; init; } = "";
    public string SourceTakeoffFolder { get; init; } = "";
    public string TargetTakeoffFolder { get; init; } = "";
    public string BackupPath { get; init; } = "";
    public bool Apply { get; init; }
}

internal sealed record TakeoffFolderSyncResult(
    bool Applied,
    int Items,
    int TargetMeasurements,
    int Measurements,
    int ReusedMeasurements,
    int AddedMeasurements,
    int Holes,
    int HiddenMeasurements,
    int Pages,
    int HiddenPages,
    string BackupPath);

internal static partial class TakeoffFolderSync
{
    public static TakeoffFolderSyncResult Run(TakeoffFolderSyncOptions options)
    {
        string sourceJobPath = RequireDirectory(options.SourceJobPath, "source job");
        string targetJobPath = RequireDirectory(options.TargetJobPath, "target job");
        string backupPath = Path.GetFullPath(options.BackupPath);
        EnsureDistinctJobs(sourceJobPath, targetJobPath);
        EnsureBackupIsOutsideJobs(sourceJobPath, targetJobPath, backupPath);

        using HeldJobGuards guards = HoldJobGuards(sourceJobPath, targetJobPath);
        EnsurePhysicalJobTrees(sourceJobPath, targetJobPath);

        JobAccessSessionToken sourceAccess = default;
        JobAccessSessionToken targetAccess = default;
        try
        {
            sourceAccess = JobWriteAccess.RegisterJob(sourceJobPath, JobAccessMode.ReadOnly);
            targetAccess = JobWriteAccess.RegisterJob(targetJobPath, JobAccessMode.ReadOnly);
            OurPlanCoreJob sourceJob =
                OurPlanCoreJobStore.LoadJob(sourceJobPath, JobAccessMode.ReadOnly);
            OurPlanCoreJob targetJob =
                OurPlanCoreJobStore.LoadJob(targetJobPath, JobAccessMode.ReadOnly);
            string sourceFolder = ResolveInside(
                sourceJob.TakeoffsRoot,
                options.SourceTakeoffFolder,
                "source takeoff folder");
            string targetFolder = ResolveInside(
                targetJob.TakeoffsRoot,
                options.TargetTakeoffFolder,
                "target takeoff folder");
            RequireDirectory(sourceFolder, "source takeoff folder");
            RequireDirectory(targetFolder, "target takeoff folder");

            SyncPlan plan = BuildPlan(sourceJob, targetJob, sourceFolder, targetFolder);
            if (!options.Apply)
                return plan.ToResult(applied: false, backupPath);

            if (Directory.Exists(backupPath) || File.Exists(backupPath))
                throw new InvalidOperationException($"Backup path already exists: {backupPath}");

            List<BackupFile> backupFiles =
                CreateBackup(plan, targetJob, targetFolder, backupPath);
            try
            {
                targetAccess = ReplaceAccess(
                    targetAccess,
                    targetJobPath,
                    JobAccessMode.Writable);
                ApplyPlan(plan);
                targetAccess = ReplaceAccess(
                    targetAccess,
                    targetJobPath,
                    JobAccessMode.ReadOnly);
                VerifyAppliedPlan(plan, targetJob, targetFolder);
            }
            catch (Exception applyException)
            {
                targetAccess = ReplaceAccess(
                    targetAccess,
                    targetJobPath,
                    JobAccessMode.Writable);
                Exception? rollbackException = TryRestoreBackup(backupFiles);
                if (rollbackException != null)
                {
                    throw new InvalidOperationException(
                        $"Takeoff sync failed and rollback also failed. Apply: {applyException.Message} " +
                        $"Rollback: {rollbackException.Message}",
                        applyException);
                }

                throw new InvalidOperationException(
                    $"Takeoff sync failed; affected files were restored from {backupPath}. " +
                    applyException.Message,
                    applyException);
            }

            return plan.ToResult(applied: true, backupPath);
        }
        finally
        {
            CloseAccess(ref targetAccess);
            CloseAccess(ref sourceAccess);
        }
    }

    private static SyncPlan BuildPlan(
        OurPlanCoreJob sourceJob,
        OurPlanCoreJob targetJob,
        string sourceFolder,
        string targetFolder)
    {
        IReadOnlyList<TakeoffItem> allSourceItems =
            OurPlanCoreJobStore.LoadTakeoffItems(sourceJob);
        IReadOnlyList<TakeoffItem> allTargetItems =
            OurPlanCoreJobStore.LoadTakeoffItems(targetJob);
        Dictionary<string, TakeoffItem> sourceItems =
            CollectItems(allSourceItems, sourceFolder);
        Dictionary<string, TakeoffItem> targetItems =
            CollectItems(allTargetItems, targetFolder);
        if (sourceItems.Count == 0)
            throw new InvalidOperationException($"No takeoff items found under {sourceFolder}.");

        string[] missingTargets = sourceItems.Keys
            .Where(key => !targetItems.ContainsKey(key))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingTargets.Length > 0)
        {
            throw new InvalidOperationException(
                "Target takeoff items are missing: " + string.Join(", ", missingTargets));
        }

        string[] extraTargets = targetItems.Keys
            .Where(key => !sourceItems.ContainsKey(key))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (extraTargets.Length > 0)
        {
            throw new InvalidOperationException(
                "Target takeoff folder has unmatched items: " + string.Join(", ", extraTargets));
        }

        var replacements = new List<ItemReplacement>();
        var sourceMeasurementIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hiddenSourceIdsByTargetPage =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var affectedTargetPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pageMappings =
            new Dictionary<string, MappedPage>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> targetIdsOutsideFolder =
            ValidateTargetMeasurementIds(allTargetItems, targetFolder);
        int targetMeasurements = 0;
        int reusedMeasurements = 0;

        foreach ((string relativeItemPath, TakeoffItem sourceItem) in sourceItems)
        {
            TakeoffItem targetItem = targetItems[relativeItemPath];
            ValidateCompatibleItems(sourceItem, targetItem, relativeItemPath);
            ValidateMeasurementFile(sourceItem, requireMeasurements: true);
            ValidateMeasurementFile(targetItem, requireMeasurements: true);
            if (sourceItem.Measurements.Count < targetItem.Measurements.Count)
            {
                throw new InvalidOperationException(
                    $"Staged item would reduce measurement count for {relativeItemPath}: " +
                    $"{targetItem.Measurements.Count} -> {sourceItem.Measurements.Count}.");
            }

            RequireRollbackFile(targetItem.FolderPath, "Data.xml");
            RequireRollbackFile(targetItem.FolderPath, "measurements.json");
            targetMeasurements += targetItem.Measurements.Count;
            var existingByIdentity = BuildMeasurementIdentityMap(targetJob, targetItem);
            foreach (Measurement existing in targetItem.Measurements)
            {
                RequirePageInsideJob(targetJob, existing.PageFolder);
                affectedTargetPages.Add(Path.GetFullPath(existing.PageFolder));
            }

            var mappedMeasurements = new List<Measurement>();
            foreach (Measurement measurement in sourceItem.Measurements)
            {
                string sourcePageFolder = measurement.PageFolder;
                string originalSourceId = NormalizeId(measurement.Id);
                MappedPage mappedPage = RequireMappedTargetPage(
                    sourceJob,
                    targetJob,
                    sourcePageFolder,
                    pageMappings);
                bool sourceWasHidden =
                    ContainsId(mappedPage.SourcePage.HiddenMeasurements, originalSourceId);
                string identity = MeasurementIdentity(sourceJob, measurement);
                if (existingByIdentity.TryGetValue(identity, out Measurement? existing))
                {
                    measurement.Id = existing.Id;
                    reusedMeasurements++;
                }

                measurement.PageFolder = mappedPage.TargetFolder;
                measurement.TakeoffFolder = targetItem.FolderPath;
                mappedMeasurements.Add(measurement);
                affectedTargetPages.Add(mappedPage.TargetFolder);
                AddId(sourceMeasurementIds, measurement.Id);

                if (sourceWasHidden)
                {
                    if (!hiddenSourceIdsByTargetPage.TryGetValue(
                            mappedPage.TargetFolder,
                            out HashSet<string>? hiddenIds))
                    {
                        hiddenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        hiddenSourceIdsByTargetPage[mappedPage.TargetFolder] = hiddenIds;
                    }

                    AddId(hiddenIds, measurement.Id);
                }
            }

            if (mappedMeasurements.Count == 0 ||
                existingByIdentity.Values.Any(existing =>
                    !mappedMeasurements.Any(candidate =>
                        string.Equals(
                            NormalizeId(candidate.Id),
                            NormalizeId(existing.Id),
                            StringComparison.OrdinalIgnoreCase))))
            {
                throw new InvalidOperationException(
                    $"Every existing target measurement must have a stable staged match: {relativeItemPath}.");
            }

            replacements.Add(new ItemReplacement(targetItem, mappedMeasurements));
        }

        int measurements = replacements.Sum(pair => pair.Measurements.Count);
        if (sourceMeasurementIds.Count != measurements)
        {
            throw new InvalidOperationException(
                "Staged measurements contain a missing or duplicate ID; sync was cancelled.");
        }
        if (sourceMeasurementIds.Overlaps(targetIdsOutsideFolder))
        {
            throw new InvalidOperationException(
                "A staged measurement ID collides with a measurement outside the target folder.");
        }
        if (reusedMeasurements != targetMeasurements)
        {
            throw new InvalidOperationException(
                "The sync would remove or replace existing measurement identities; sync was cancelled.");
        }

        var pageHiddenUpdates =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string targetPageFolder in affectedTargetPages)
        {
            PageInfo targetPage = OurPlanCoreJobStore.TryReadPage(targetPageFolder)
                ?? throw new InvalidOperationException(
                    $"Target measurement page is not readable: {targetPageFolder}");
            var finalHidden = targetPage.HiddenMeasurements
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (hiddenSourceIdsByTargetPage.TryGetValue(
                    targetPageFolder,
                    out HashSet<string>? sourceHidden))
            {
                finalHidden.UnionWith(sourceHidden);
            }

            if (!finalHidden.SetEquals(targetPage.HiddenMeasurements))
            {
                pageHiddenUpdates[targetPageFolder] = finalHidden
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }

        int holes = replacements.Sum(pair => pair.Measurements.Sum(measurement => measurement.Holes.Count));
        int hiddenMeasurements = hiddenSourceIdsByTargetPage.Values.Sum(ids => ids.Count);
        return new SyncPlan(
            replacements,
            pageHiddenUpdates,
            sourceMeasurementIds,
            targetMeasurements,
            measurements,
            reusedMeasurements,
            measurements - reusedMeasurements,
            holes,
            hiddenMeasurements,
            affectedTargetPages.Count);
    }

    private static Dictionary<string, TakeoffItem> CollectItems(
        IEnumerable<TakeoffItem> allItems,
        string folder)
    {
        var items = new Dictionary<string, TakeoffItem>(StringComparer.OrdinalIgnoreCase);
        foreach (TakeoffItem item in allItems)
        {
            if (!IsInside(folder, item.FolderPath))
                continue;

            string relative = Path.GetRelativePath(folder, item.FolderPath);
            if (!items.TryAdd(relative, item))
                throw new InvalidOperationException($"Duplicate takeoff item path: {relative}");
        }

        return items;
    }

    private static MappedPage RequireMappedTargetPage(
        OurPlanCoreJob sourceJob,
        OurPlanCoreJob targetJob,
        string sourcePageFolder,
        IDictionary<string, MappedPage> cache)
    {
        string normalizedSourcePage = Path.GetFullPath(sourcePageFolder);
        if (cache.TryGetValue(normalizedSourcePage, out MappedPage? cached))
            return cached;
        if (!IsInside(sourceJob.PagesRoot, sourcePageFolder))
        {
            throw new InvalidOperationException(
                $"Measurement page is outside the expected Pages root: {sourcePageFolder}");
        }

        string relativePage = Path.GetRelativePath(sourceJob.PagesRoot, sourcePageFolder);
        string targetPage = ResolveInside(targetJob.PagesRoot, relativePage, "target page");
        PageInfo sourcePage = OurPlanCoreJobStore.TryReadPage(sourcePageFolder)
            ?? throw new InvalidOperationException(
                $"Source measurement page is not readable: {sourcePageFolder}");
        PageInfo targetPageInfo = OurPlanCoreJobStore.TryReadPage(targetPage)
            ?? throw new InvalidOperationException(
                $"Mapped target page does not exist: {targetPage}");
        if (!string.Equals(
                sourcePage.Name,
                targetPageInfo.Name,
                StringComparison.OrdinalIgnoreCase) ||
            !NearlyEqual(sourcePage.ScaleMetersPerPt, targetPageInfo.ScaleMetersPerPt) ||
            !PdfPageDimensionsMatch(sourcePage, targetPageInfo))
        {
            throw new InvalidOperationException(
                $"Mapped source and target page metadata do not match: {relativePage}");
        }

        var mapped = new MappedPage(targetPage, sourcePage, targetPageInfo);
        cache[normalizedSourcePage] = mapped;
        return mapped;
    }

    private static List<BackupFile> CreateBackup(
        SyncPlan plan,
        OurPlanCoreJob targetJob,
        string targetFolder,
        string backupPath)
    {
        Directory.CreateDirectory(backupPath);
        var files = new List<BackupFile>();
        foreach (string original in Directory.EnumerateFiles(
                     targetFolder,
                     "*",
                     SearchOption.AllDirectories))
        {
            string relative = Path.Combine(
                "Takeoffs",
                Path.GetRelativePath(targetJob.TakeoffsRoot, original));
            files.Add(CaptureBackupState(original, backupPath, relative));
        }

        foreach (string pageFolder in plan.PageHiddenUpdates.Keys)
        {
            string sourceJson = Path.Combine(pageFolder, "source.json");
            string sourceRelative = Path.Combine(
                "Pages",
                Path.GetRelativePath(targetJob.PagesRoot, sourceJson));
            files.Add(CaptureBackupState(
                sourceJson,
                backupPath,
                sourceRelative,
                required: true));

            string layersJson = OurPlanCoreJobStore.PageLayersJsonPath(pageFolder);
            string layersRelative = Path.Combine(
                "Pages",
                Path.GetRelativePath(targetJob.PagesRoot, layersJson));
            files.Add(CaptureBackupState(
                layersJson,
                backupPath,
                layersRelative,
                required: false));
        }

        var manifest = new
        {
            created_at_utc = DateTimeOffset.UtcNow,
            target_job = targetJob.RootPath,
            target_takeoff_folder = targetFolder,
            items = plan.Replacements.Count,
            target_measurements = plan.TargetMeasurements,
            measurements = plan.Measurements,
            reused_measurements = plan.ReusedMeasurements,
            added_measurements = plan.AddedMeasurements,
            holes = plan.Holes,
            hidden_measurements = plan.HiddenMeasurements,
            files = files.Select(file => new
            {
                original = file.OriginalPath,
                backup = file.BackupPath,
                sha256 = file.Sha256,
                existed = file.Existed,
            }),
        };
        File.WriteAllText(
            Path.Combine(backupPath, "repair_manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        return files;
    }

    private static BackupFile CaptureBackupState(
        string originalPath,
        string backupRoot,
        string relativePath,
        bool required = true)
    {
        if (!File.Exists(originalPath))
        {
            if (required)
                throw new FileNotFoundException("Required backup source is missing.", originalPath);
            return new BackupFile(originalPath, null, null, Existed: false);
        }

        string backupFile = ResolveInside(backupRoot, relativePath, "backup file");
        Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);
        string expectedSha256 = Sha256(originalPath);
        File.Copy(originalPath, backupFile, overwrite: false);
        string copiedSha256 = Sha256(backupFile);
        if (!string.Equals(expectedSha256, copiedSha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"Backup verification failed for {originalPath}.");
        return new BackupFile(originalPath, backupFile, expectedSha256, Existed: true);
    }

    private static void ApplyPlan(SyncPlan plan)
    {
        foreach (ItemReplacement replacement in plan.Replacements)
        {
            replacement.Target.Measurements.Clear();
            replacement.Target.Measurements.AddRange(replacement.Measurements);
            OurPlanCoreJobStore.SaveTakeoffItem(replacement.Target);
        }

        foreach ((string pageFolder, IReadOnlyList<string> hiddenIds) in plan.PageHiddenUpdates)
            OurPlanCoreJobStore.SavePageHiddenMeasurements(pageFolder, hiddenIds);
    }

    private static void VerifyAppliedPlan(
        SyncPlan plan,
        OurPlanCoreJob targetJob,
        string targetFolder)
    {
        Dictionary<string, TakeoffItem> reloadedItems = CollectItems(
            OurPlanCoreJobStore.LoadTakeoffItems(targetJob),
            targetFolder);
        if (reloadedItems.Count != plan.Replacements.Count)
            throw new InvalidOperationException("Reloaded takeoff item count does not match the sync plan.");

        int actualMeasurements = reloadedItems.Values.Sum(item => item.Measurements.Count);
        int actualHoles = reloadedItems.Values.Sum(
            item => item.Measurements.Sum(measurement => measurement.Holes.Count));
        if (actualMeasurements != plan.Measurements || actualHoles != plan.Holes)
        {
            throw new InvalidOperationException(
                "Reloaded measurement or hole count does not match the sync plan.");
        }

        foreach (ItemReplacement replacement in plan.Replacements)
        {
            string relativeItemPath = Path.GetRelativePath(
                targetFolder,
                replacement.Target.FolderPath);
            if (!reloadedItems.TryGetValue(relativeItemPath, out TakeoffItem? reloadedItem))
            {
                throw new InvalidOperationException(
                    $"Reloaded takeoff item is missing: {relativeItemPath}");
            }

            VerifyReloadedItem(replacement, reloadedItem);
        }

        var actualIds = reloadedItems.Values
            .SelectMany(item => item.Measurements)
            .Select(measurement => NormalizeId(measurement.Id))
            .ToArray();
        if (actualIds.Any(string.IsNullOrWhiteSpace) ||
            actualIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != actualIds.Length ||
            !actualIds.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(plan.SourceMeasurementIds))
            throw new InvalidOperationException("Reloaded measurement IDs do not match the staged source.");

        foreach ((string pageFolder, IReadOnlyList<string> hiddenIds) in plan.PageHiddenUpdates)
        {
            PageInfo page = OurPlanCoreJobStore.TryReadPage(pageFolder)
                ?? throw new InvalidOperationException($"Updated page did not reload: {pageFolder}");
            if (!page.HiddenMeasurements.ToHashSet(StringComparer.OrdinalIgnoreCase)
                    .SetEquals(hiddenIds))
            {
                throw new InvalidOperationException(
                    $"Reloaded hidden measurements do not match for page: {pageFolder}");
            }
        }
    }

    private static Exception? TryRestoreBackup(IEnumerable<BackupFile> backupFiles)
    {
        try
        {
            foreach (BackupFile file in backupFiles)
            {
                if (!file.Existed)
                {
                    if (File.Exists(file.OriginalPath))
                    {
                        JobWriteAccess.Demand(file.OriginalPath, "restore takeoff sync backup");
                        File.Delete(file.OriginalPath);
                    }
                    continue;
                }

                File.Copy(file.BackupPath!, file.OriginalPath, overwrite: true);
                if (!string.Equals(
                        Sha256(file.OriginalPath),
                        file.Sha256!,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException(
                        $"Rollback verification failed for {file.OriginalPath}.");
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static string RequireDirectory(string path, string label)
    {
        string full = Path.GetFullPath(path);
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException($"{label} not found: {full}");
        return full;
    }

    private static void RequireRollbackFile(string itemFolder, string fileName)
    {
        string path = Path.Combine(itemFolder, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Target item cannot be repaired safely because {fileName} is missing.",
                path);
        }
    }

    private static void VerifyReloadedItem(
        ItemReplacement replacement,
        TakeoffItem reloadedItem)
    {
        var expectedById = replacement.Measurements.ToDictionary(
            measurement => NormalizeId(measurement.Id),
            StringComparer.OrdinalIgnoreCase);
        if (reloadedItem.Measurements.Count != expectedById.Count)
        {
            throw new InvalidOperationException(
                $"Reloaded measurement count does not match for {reloadedItem.FolderPath}.");
        }

        foreach (Measurement actual in reloadedItem.Measurements)
        {
            string id = NormalizeId(actual.Id);
            if (!expectedById.TryGetValue(id, out Measurement? expected) ||
                !MeasurementContentMatches(expected, actual))
            {
                throw new InvalidOperationException(
                    $"Reloaded measurement does not match the staged source: {actual.Id}");
            }
        }
    }

    private static string ResolveInside(string root, string relative, string label)
    {
        if (Path.IsPathRooted(relative))
            throw new InvalidOperationException($"{label} must be relative: {relative}");
        string fullRoot = Path.GetFullPath(root);
        string full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!IsInside(fullRoot, full))
            throw new InvalidOperationException($"{label} escapes its root: {relative}");
        return full;
    }

    private static bool IsInside(string root, string candidate)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        string fullCandidate = Path.GetFullPath(candidate);
        return string.Equals(fullRoot, fullCandidate, StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(
                   fullRoot + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureDistinctJobs(string sourceJobPath, string targetJobPath)
    {
        if (string.Equals(
                CanonicalizePathForComparison(sourceJobPath),
                CanonicalizePathForComparison(targetJobPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Source and target jobs must be different.");
        }
    }

    private static void EnsureBackupIsOutsideJobs(
        string sourceJobPath,
        string targetJobPath,
        string backupPath)
    {
        string canonicalBackup = CanonicalizePathForComparison(backupPath);
        if (IsInside(CanonicalizePathForComparison(sourceJobPath), canonicalBackup) ||
            IsInside(CanonicalizePathForComparison(targetJobPath), canonicalBackup))
        {
            throw new InvalidOperationException(
                "Backup path must be outside both the source and target job folders.");
        }
    }

    private static bool ContainsId(IEnumerable<string> ids, string value)
    {
        string normalized = NormalizeId(value);
        return !string.IsNullOrWhiteSpace(normalized) &&
               ids.Select(NormalizeId).Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    private static void AddId(ISet<string> ids, string value)
    {
        string normalized = NormalizeId(value);
        if (!string.IsNullOrWhiteSpace(normalized))
            ids.Add(normalized);
    }

    private static string NormalizeId(string? value) => (value ?? "").Trim();

    private static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed record ItemReplacement(
        TakeoffItem Target,
        IReadOnlyList<Measurement> Measurements);

    private sealed record BackupFile(
        string OriginalPath,
        string? BackupPath,
        string? Sha256,
        bool Existed);

    private sealed record SyncPlan(
        IReadOnlyList<ItemReplacement> Replacements,
        IReadOnlyDictionary<string, IReadOnlyList<string>> PageHiddenUpdates,
        IReadOnlySet<string> SourceMeasurementIds,
        int TargetMeasurements,
        int Measurements,
        int ReusedMeasurements,
        int AddedMeasurements,
        int Holes,
        int HiddenMeasurements,
        int Pages)
    {
        public TakeoffFolderSyncResult ToResult(bool applied, string backupPath) =>
            new(
                applied,
                Replacements.Count,
                TargetMeasurements,
                Measurements,
                ReusedMeasurements,
                AddedMeasurements,
                Holes,
                HiddenMeasurements,
                Pages,
                PageHiddenUpdates.Count,
                backupPath);
    }
}
