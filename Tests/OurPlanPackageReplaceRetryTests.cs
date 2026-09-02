using OurPlanCore;

internal static partial class OurPlanPackageTests
{
    public static void ReplaceRetryRecognizesOnlySafeWin32Failures()
    {
        int[] retryable =
        [
            unchecked((int)0x80070020),
            unchecked((int)0x80070021),
            unchecked((int)0x80070497),
            unchecked((int)0x80070498),
        ];
        foreach (int hResult in retryable)
        {
            AssertTrue(
                OurPlanPackageWriter.IsTransientReplaceFailure(new IOException("retry", hResult)),
                $"safe replace failure was not retryable: 0x{hResult:X8}");
        }

        int[] permanent =
        [
            unchecked((int)0x80070002),
            unchecked((int)0x80070005),
            unchecked((int)0x80070070),
            unchecked((int)0x80070057),
            unchecked((int)0x80070499),
        ];
        foreach (int hResult in permanent)
        {
            AssertFalse(
                OurPlanPackageWriter.IsTransientReplaceFailure(new IOException("stop", hResult)),
                $"unsafe replace failure was marked retryable: 0x{hResult:X8}");
        }
    }

    public static void ReplaceRetryWaitsForRealTemporaryDestinationLock()
    {
        string root = Path.Combine(Path.GetTempPath(), "ourplan_replace_retry", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string target = Path.Combine(root, "project.ourplan");
        string replacement = Path.Combine(root, "replacement.tmp");
        string backup = Path.Combine(root, "rollback.tmp");
        File.WriteAllText(target, "old package");
        File.WriteAllText(replacement, "new package");

        FileStream blocker = new(target, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var firstAttempt = new ManualResetEventSlim(false);
        Task releaseTask = Task.Run(() =>
        {
            firstAttempt.Wait();
            Thread.Sleep(175);
            blocker.Dispose();
        });

        try
        {
            int retryCount = OurPlanPackageWriter.ExecuteTransientReplaceWithRetry(
                () =>
                {
                    firstAttempt.Set();
                    File.Replace(replacement, target, backup, ignoreMetadataErrors: true);
                },
                () =>
                {
                    AssertTrue(File.Exists(target), "locked target disappeared before retry");
                    AssertTrue(File.Exists(replacement), "replacement disappeared before retry");
                    AssertFalse(File.Exists(backup), "rollback appeared before a successful replace");
                });

            AssertTrue(retryCount > 0, "temporary destination lock did not exercise retry");
            AssertEqual("new package", File.ReadAllText(target), "replacement contents");
            AssertEqual("old package", File.ReadAllText(backup), "rollback contents");
        }
        finally
        {
            firstAttempt.Set();
            blocker.Dispose();
            releaseTask.GetAwaiter().GetResult();
            TryDelete(root);
        }
    }

    public static void ReplaceRetryStopsOnConflictAndHasBoundedBudget()
    {
        int conflictAttempts = 0;
        AssertThrows<OurPlanPackageConflictException>(
            () => OurPlanPackageWriter.ExecuteTransientReplaceWithRetry(
                () =>
                {
                    conflictAttempts++;
                    throw new IOException("temporarily blocked", unchecked((int)0x80070497));
                },
                () => throw new OurPlanPackageConflictException("destination changed"),
                _ => { }),
            "destination conflict must stop replace retry");
        AssertEqual(1, conflictAttempts, "replace attempts before conflict");

        var failure = new IOException("still blocked", unchecked((int)0x80070497));
        int boundedAttempts = 0;
        var waits = new List<TimeSpan>();
        IOException? caught = null;
        try
        {
            OurPlanPackageWriter.ExecuteTransientReplaceWithRetry(
                () =>
                {
                    boundedAttempts++;
                    throw failure;
                },
                () => { },
                waits.Add);
        }
        catch (IOException ex)
        {
            caught = ex;
        }

        AssertTrue(ReferenceEquals(failure, caught), "final replace failure was wrapped or replaced");
        AssertEqual(5, boundedAttempts, "bounded replace attempts");
        AssertEqual(4, waits.Count, "bounded replace waits");
        AssertEqual(TimeSpan.FromMilliseconds(100), waits[0], "first retry delay");
        AssertEqual(TimeSpan.FromMilliseconds(250), waits[1], "second retry delay");
        AssertEqual(TimeSpan.FromMilliseconds(500), waits[2], "third retry delay");
        AssertEqual(TimeSpan.FromMilliseconds(1000), waits[3], "fourth retry delay");
    }

    public static void PackageReadHandleAllowsAtomicReplacement()
    {
        string root = Path.Combine(Path.GetTempPath(), "ourplan_read_share", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string target = Path.Combine(root, "project.ourplan");
        string replacement = Path.Combine(root, "replacement.tmp");
        string backup = Path.Combine(root, "rollback.tmp");
        File.WriteAllText(target, "old package");
        File.WriteAllText(replacement, "new package");

        try
        {
            using FileStream reader = OurPlanPackageArchive.OpenPackageReadStream(target);
            File.Replace(replacement, target, backup, ignoreMetadataErrors: true);
            AssertEqual("new package", File.ReadAllText(target), "target replaced while reader stayed open");
            reader.Position = 0;
            using var text = new StreamReader(reader, leaveOpen: true);
            AssertEqual("old package", text.ReadToEnd(), "open reader kept the original file identity");
        }
        finally
        {
            TryDelete(root);
        }
    }
}
