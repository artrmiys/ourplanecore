using OurPlanCore;

internal static class TakeoffCleanupServiceTests
{
    public static void SafeFinderIncludesOnlyVerifiedZeroRecordItems()
    {
        WithTempJob("Verified Empty Takeoffs", job =>
        {
            TakeoffItem freshEmpty = OurPlanCoreJobStore.CreateTakeoffItem(
                job,
                job.TakeoffsRoot,
                "Fresh empty",
                "#FF4444",
                "line");
            TakeoffItem savedEmpty = CreateSavedTakeoff(job, "Saved empty");
            TakeoffItem zeroPointRecord = CreateSavedTakeoff(job, "Zero-point section");
            zeroPointRecord.Measurements.Add(new Measurement { MType = "line" });
            AssertEqual(0, zeroPointRecord.Measurements[0].Points.Count, "test section should have zero geometry points");
            OurPlanCoreJobStore.SaveTakeoffItem(zeroPointRecord);

            IReadOnlyList<TakeoffItem> result =
                TakeoffCleanupService.FindSafeItemsWithoutMeasurements([freshEmpty, savedEmpty, zeroPointRecord]);

            AssertEqual(2, result.Count, "fresh and saved items with zero measurement records should be included");
            AssertTrue(ReferenceEquals(freshEmpty, result[0]), "the fresh empty item should be returned");
            AssertTrue(ReferenceEquals(savedEmpty, result[1]), "the saved empty item should be returned");
        });
    }

    public static void SafeFinderExcludesCorruptAndPossiblyMissingMeasurementData()
    {
        WithTempJob("Unsafe Empty Takeoffs", job =>
        {
            TakeoffItem validEmpty = CreateSavedTakeoff(job, "Valid empty");

            TakeoffItem corruptSidecar = CreateSavedTakeoff(job, "Corrupt sidecar");
            File.WriteAllText(MeasurementsPath(corruptSidecar), "{ bad json");

            TakeoffItem corruptArtifact = CreateSavedTakeoff(job, "Corrupt artifact");
            File.WriteAllText(
                Path.Combine(corruptArtifact.FolderPath, "measurements.json.corrupt-test"),
                "{ quarantined bad json");

            TakeoffItem missingSidecarWithStoredCount = CreateSavedTakeoff(job, "Missing measured sidecar");
            missingSidecarWithStoredCount.Measurements.Add(new Measurement { MType = "line" });
            OurPlanCoreJobStore.SaveTakeoffItem(missingSidecarWithStoredCount);
            missingSidecarWithStoredCount.Measurements.Clear();
            File.Delete(MeasurementsPath(missingSidecarWithStoredCount));

            IReadOnlyList<TakeoffItem> result = TakeoffCleanupService.FindSafeItemsWithoutMeasurements(
                [validEmpty, corruptSidecar, corruptArtifact, missingSidecarWithStoredCount]);

            AssertEqual(1, result.Count, "unsafe empty-looking items should be excluded");
            AssertTrue(ReferenceEquals(validEmpty, result[0]), "only the verified empty item should remain eligible");
        });
    }

    public static void SafeFinderRejectsAmbiguousMeasurementJson()
    {
        WithTempJob("Ambiguous Empty Json", job =>
        {
            var cases = new[]
            {
                (Name: "Whitespace json", Json: " \r\n\t "),
                (Name: "Null json", Json: "null"),
                (Name: "Empty object json", Json: "{}"),
                (Name: "Envelope without measurements", Json: "{\"schema_version\":1}"),
            };
            var items = new List<TakeoffItem>();
            foreach (var testCase in cases)
            {
                TakeoffItem item = CreateSavedTakeoff(job, testCase.Name);
                File.WriteAllText(MeasurementsPath(item), testCase.Json);
                items.Add(item);
            }

            IReadOnlyList<TakeoffItem> result =
                TakeoffCleanupService.FindSafeItemsWithoutMeasurements(items);

            AssertEqual(0, result.Count, "ambiguous JSON shapes must not prove that a takeoff is empty");
        });
    }

    public static void SafeFinderRejectsJsonMetadataCountConflict()
    {
        WithTempJob("Empty Json Metadata Conflict", job =>
        {
            TakeoffItem conflicting = CreateSavedTakeoff(job, "Stored count conflict");
            conflicting.Measurements.Add(new Measurement { MType = "line" });
            OurPlanCoreJobStore.SaveTakeoffItem(conflicting);
            conflicting.Measurements.Clear();
            File.WriteAllText(MeasurementsPath(conflicting), "[]");

            IReadOnlyList<TakeoffItem> result =
                TakeoffCleanupService.FindSafeItemsWithoutMeasurements([conflicting]);

            AssertEqual(
                0,
                result.Count,
                "valid empty JSON must not override Data.xml MeasurementCount greater than zero");
        });
    }

    public static void SafeFinderExcludesEmptyMultiLineOwnerAndCompanion()
    {
        WithTempJob("Multiline Empty Takeoffs", job =>
        {
            TakeoffItem unrelatedEmpty = CreateSavedTakeoff(job, "Unrelated empty");
            TakeoffItem owner = CreateSavedTakeoff(job, "Multiline owner");
            TakeoffItem companion = CreateSavedTakeoff(job, "Multiline companion");
            owner.MultiLineOffsets.Add(new MultiLineOffsetConfig
            {
                Name = companion.Name,
                CompanionFolder = companion.FolderPath,
                Meters = 0.1,
            });
            OurPlanCoreJobStore.SaveTakeoffItem(owner);

            IReadOnlyList<TakeoffItem> result =
                TakeoffCleanupService.FindSafeItemsWithoutMeasurements([unrelatedEmpty, owner, companion]);

            AssertEqual(1, result.Count, "multiline owner and referenced companion should both be protected");
            AssertTrue(ReferenceEquals(unrelatedEmpty, result[0]), "an unrelated verified empty item should remain eligible");
        });
    }

    public static void BottomButtonUsesGuardedSingleUndoTrashBatch()
    {
        string xaml = File.ReadAllText(RepoFile("MainWindow.xaml"));
        string cleanup = File.ReadAllText(RepoFile("MainWindow.TakeoffsCleanup.cs"));
        int newItem = xaml.IndexOf("x:Name=\"BtnNewTakeoffItem\"", StringComparison.Ordinal);
        int deleteEmpty = xaml.IndexOf("x:Name=\"BtnDeleteEmptyTakeoffs\"", StringComparison.Ordinal);
        int safetySnapshot = cleanup.IndexOf("safePathsBeforeFlush", StringComparison.Ordinal);
        int flush = cleanup.IndexOf("FlushTakeoffAutosaves()", StringComparison.Ordinal);
        int postFlushIntersection = flush < 0
            ? -1
            : cleanup.IndexOf("safePathsBeforeFlush.Contains", flush, StringComparison.Ordinal);

        AssertTrue(newItem >= 0 && deleteEmpty > newItem, "Delete Empty button should be placed after New Item");
        AssertTrue(
            xaml.Contains("Click=\"BtnDeleteEmptyTakeoffs_Click\"", StringComparison.Ordinal),
            "Delete Empty button should be wired");
        AssertTrue(
            cleanup.Contains("IsTakeoffRecordActive()", StringComparison.Ordinal),
            "cleanup must stop before candidate discovery while Record is active");
        AssertTrue(
            cleanup.Contains("TakeoffCleanupService.FindSafeItemsWithoutMeasurements(_takeoffItems)", StringComparison.Ordinal),
            "button must use the disk-safe empty-item finder");
        AssertTrue(
            safetySnapshot >= 0 && safetySnapshot < flush && postFlushIntersection > flush,
            "cleanup must snapshot safe paths before flush and delete only the post-flush intersection");
        AssertEqual(
            1,
            CountOccurrences(cleanup, "MoveTakeoffEntriesToUndoTrash("),
            "all empty takeoffs should be moved in one undo-trash batch");
        AssertEqual(
            1,
            CountOccurrences(cleanup, "PushTakeoffDeleteUndo("),
            "the single cleanup batch should create one undo entry");
    }

    private static TakeoffItem CreateSavedTakeoff(OurPlanCoreJob job, string name)
    {
        TakeoffItem item = OurPlanCoreJobStore.CreateTakeoffItem(
            job,
            job.TakeoffsRoot,
            name,
            "#FF4444",
            "line");
        OurPlanCoreJobStore.SaveTakeoffItem(item);
        return item;
    }

    private static string MeasurementsPath(TakeoffItem item) =>
        Path.Combine(item.FolderPath, "measurements.json");

    private static void WithTempJob(string name, Action<OurPlanCoreJob> action)
    {
        string root = Path.Combine(Path.GetTempPath(), $"ourplancore-takeoff-cleanup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            action(OurPlanCoreJobStore.CreateJob(root, name));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best-effort cleanup must not hide the assertion that failed.
            }
        }
    }

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string RepoFile(string relativePath)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate) && File.Exists(Path.Combine(current.FullName, "ourplancore.csproj")))
                return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException($"Repository file not found: {relativePath}");
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected '{expected}', got '{actual}'");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
