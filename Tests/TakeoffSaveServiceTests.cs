using OurPlanCore;

internal static class TakeoffSaveServiceTests
{
    public static void WriteFailureRetainsDirtyItemAndSchedulesRetry()
    {
        DateTime attempt = new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);
        var scheduler = new FakeScheduler();
        string status = "";
        TakeoffItem item = Item("one");
        TakeoffSaveService service = Service(
            scheduler,
            _ => throw new IOException("disk full"),
            () => attempt,
            value => status = value);

        service.MarkDirty(item);
        TakeoffFlushResult result = service.Flush();

        AssertEqual(1, result.Attempted, "attempted writes");
        AssertEqual(0, result.Saved, "saved writes");
        AssertEqual(1, result.Failed, "failed writes");
        AssertTrue(service.HasPending, "failed item must remain pending");
        AssertEqual(TakeoffSaveState.Failed, service.State, "failed state");
        AssertEqual(attempt, service.LastAttemptUtc, "last attempt");
        AssertEqual<DateTime?>(null, service.LastSuccessfulFlushUtc, "last successful flush");
        AssertContains(service.LastError, "disk full", "stored error");
        AssertContains(status, "remain unsaved", "failure status");
        AssertEqual(TakeoffSaveService.RetryDelay, scheduler.LastDelay, "retry delay");
        AssertTrue(scheduler.HasScheduledCallback, "retry must be scheduled");
    }

    public static void ScheduledRetrySavesRetainedItem()
    {
        var times = new Queue<DateTime>(
        [
            new DateTime(2026, 7, 15, 12, 1, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 15, 12, 1, 2, DateTimeKind.Utc),
        ]);
        var scheduler = new FakeScheduler();
        int writes = 0;
        TakeoffSaveService service = Service(
            scheduler,
            _ =>
            {
                writes++;
                if (writes == 1)
                    throw new IOException("first attempt failed");
            },
            () => times.Dequeue());
        var states = new List<TakeoffSaveState>();
        service.DirtyStateChanged += () => states.Add(service.State);

        service.MarkDirty(Item("retry"));
        service.Flush();
        scheduler.Fire();

        AssertEqual(2, writes, "writer calls");
        AssertFalse(service.HasPending, "retry must clear pending item");
        AssertEqual(TakeoffSaveState.Clean, service.State, "state after retry");
        AssertEqual<DateTime?>(new DateTime(2026, 7, 15, 12, 1, 2, DateTimeKind.Utc), service.LastSuccessfulFlushUtc, "retry success time");
        AssertEqual<string?>(null, service.LastError, "retry clears error");
        AssertSequence(
            states,
            TakeoffSaveState.Dirty,
            TakeoffSaveState.Saving,
            TakeoffSaveState.Failed,
            TakeoffSaveState.Saving,
            TakeoffSaveState.Clean);
    }

    public static void PartialBatchRetriesOnlyFailedItem()
    {
        var scheduler = new FakeScheduler();
        TakeoffItem first = Item("first");
        TakeoffItem second = Item("second");
        TakeoffItem third = Item("third");
        var writes = new Dictionary<string, int>();
        bool failSecond = true;
        TakeoffSaveService service = Service(
            scheduler,
            item =>
            {
                writes[item.Id] = writes.GetValueOrDefault(item.Id) + 1;
                if (ReferenceEquals(item, second) && failSecond)
                    throw new IOException("second failed");
            });

        service.MarkDirty([first, second, third]);
        TakeoffFlushResult firstResult = service.Flush();
        failSecond = false;
        TakeoffFlushResult retryResult = service.Flush();

        AssertEqual(3, firstResult.Attempted, "first batch attempted");
        AssertEqual(2, firstResult.Saved, "first batch saved");
        AssertEqual(1, firstResult.Failed, "first batch failed");
        AssertEqual(1, retryResult.Attempted, "retry batch attempted");
        AssertEqual(1, retryResult.Saved, "retry batch saved");
        AssertEqual(1, writes[first.Id], "first item writes");
        AssertEqual(2, writes[second.Id], "second item writes");
        AssertEqual(1, writes[third.Id], "third item writes");
        AssertEqual(TakeoffSaveState.Clean, service.State, "partial retry state");
    }

    public static void MissingFolderRemainsPendingWithoutResurrection()
    {
        string root = Path.Combine(Path.GetTempPath(), "onc_autosave_" + Guid.NewGuid().ToString("N"));
        string takeoffsRoot = Path.Combine(root, "Takeoffs");
        string itemFolder = Path.Combine(takeoffsRoot, "deleted");
        Directory.CreateDirectory(itemFolder);
        try
        {
            var scheduler = new FakeScheduler();
            int writes = 0;
            var job = new OurPlanCoreJob { Name = "test", RootPath = root };
            var item = new TakeoffItem { Name = "deleted", FolderPath = itemFolder };
            var service = new TakeoffSaveService(
                () => job,
                _ => { },
                _ => writes++,
                Directory.Exists,
                () => new DateTime(2026, 7, 15, 12, 2, 0, DateTimeKind.Utc),
                scheduler);

            service.MarkDirty(item);
            Directory.Delete(itemFolder);
            TakeoffFlushResult result = service.Flush();

            AssertEqual(0, result.DroppedMissing, "dropped missing count");
            AssertEqual(1, result.Failed, "missing folder failures");
            AssertEqual(0, writes, "writer calls for missing folder");
            AssertFalse(Directory.Exists(itemFolder), "autosave must not recreate deleted folder");
            AssertTrue(service.HasPending, "unavailable folder must remain pending");
            AssertTrue(scheduler.HasScheduledCallback, "unavailable folder must retry");
            AssertEqual(1, service.DiscardUnavailableItems(), "explicit unavailable discard count");
            AssertFalse(service.HasPending, "explicit discard must clear unavailable item");
            AssertFalse(scheduler.HasScheduledCallback, "explicit discard must stop retry");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    public static void FailedAttemptPreservesPreviousSuccessfulTimestamp()
    {
        DateTime success = new(2026, 7, 15, 12, 3, 0, DateTimeKind.Utc);
        DateTime failure = success.AddSeconds(10);
        var times = new Queue<DateTime>([success, failure]);
        bool shouldFail = false;
        TakeoffItem item = Item("timestamp");
        TakeoffSaveService service = Service(
            new FakeScheduler(),
            _ =>
            {
                if (shouldFail)
                    throw new IOException("write failed");
            },
            () => times.Dequeue());

        service.MarkDirty(item);
        service.Flush();
        shouldFail = true;
        service.MarkDirty(item);
        service.Flush();

        AssertEqual<DateTime?>(failure, service.LastAttemptUtc, "failed attempt time");
        AssertEqual<DateTime?>(success, service.LastSuccessfulFlushUtc, "preserved success time");
        AssertEqual(TakeoffSaveState.Failed, service.State, "timestamp failure state");
    }

    public static void EmptyFlushDoesNotInventTimestamps()
    {
        var scheduler = new FakeScheduler();
        int clockCalls = 0;
        int writes = 0;
        TakeoffSaveService service = Service(
            scheduler,
            _ => writes++,
            () =>
            {
                clockCalls++;
                return DateTime.UtcNow;
            });

        TakeoffFlushResult result = service.Flush();

        AssertTrue(result.Success, "empty flush result");
        AssertEqual(0, clockCalls, "empty flush clock calls");
        AssertEqual(0, writes, "empty flush writer calls");
        AssertEqual<DateTime?>(null, service.LastAttemptUtc, "empty last attempt");
        AssertEqual<DateTime?>(null, service.LastSuccessfulFlushUtc, "empty last success");
        AssertEqual(TakeoffSaveState.Clean, service.State, "empty flush state");
    }

    public static void MissingCurrentJobKeepsPendingItem()
    {
        OurPlanCoreJob? currentJob = new() { Name = "test", RootPath = "C:\\autosave-test" };
        var scheduler = new FakeScheduler();
        int writes = 0;
        var service = new TakeoffSaveService(
            () => currentJob,
            _ => { },
            _ => writes++,
            _ => true,
            () => new DateTime(2026, 7, 15, 12, 5, 0, DateTimeKind.Utc),
            scheduler);

        service.MarkDirty(Item("job-switch"));
        currentJob = null;
        TakeoffFlushResult result = service.Flush();

        AssertFalse(result.Success, "flush without current job");
        AssertEqual(1, result.Failed, "missing job failure count");
        AssertEqual(0, writes, "missing job writer calls");
        AssertTrue(service.HasPending, "missing job must retain dirty item");
        AssertEqual(TakeoffSaveState.Failed, service.State, "missing job state");
        AssertTrue(scheduler.HasScheduledCallback, "missing job must retry");
    }

    public static void SwitchedJobCannotFlushPreviousJobItem()
    {
        var firstJob = new OurPlanCoreJob { Name = "first", RootPath = "C:\\autosave-first" };
        var secondJob = new OurPlanCoreJob { Name = "second", RootPath = "C:\\autosave-second" };
        OurPlanCoreJob currentJob = firstJob;
        var scheduler = new FakeScheduler();
        int writes = 0;
        var service = new TakeoffSaveService(
            () => currentJob,
            _ => { },
            _ => writes++,
            _ => true,
            () => new DateTime(2026, 7, 15, 12, 6, 0, DateTimeKind.Utc),
            scheduler);

        service.MarkDirty(new TakeoffItem
        {
            Name = "old-job-item",
            FolderPath = "C:\\autosave-first\\Takeoffs\\old-job-item",
        });
        currentJob = secondJob;
        TakeoffFlushResult result = service.Flush();

        AssertFalse(result.Success, "cross-job flush result");
        AssertEqual(0, writes, "cross-job writer calls");
        AssertTrue(service.HasPending, "cross-job item must remain pending");
        AssertContains(result.Error, "another job", "cross-job error");
    }

    public static void PathOutsideTakeoffsRootCannotBeWritten()
    {
        var scheduler = new FakeScheduler();
        int writes = 0;
        TakeoffSaveService service = Service(scheduler, _ => writes++);
        var item = new TakeoffItem
        {
            Name = "outside",
            FolderPath = "C:\\outside-job\\takeoff",
        };

        service.MarkDirty(item);
        TakeoffFlushResult result = service.Flush();

        AssertFalse(result.Success, "outside path result");
        AssertEqual(0, writes, "outside path writer calls");
        AssertTrue(service.HasPending, "outside path must remain pending");
        AssertContains(result.Error, "outside the current job", "outside path error");
    }

    public static void StatusTextNeverClaimsSavedOutsideClean()
    {
        DateTime previousSuccess = new(2026, 7, 15, 12, 4, 0, DateTimeKind.Utc);
        foreach (TakeoffSaveState state in new[]
                 {
                     TakeoffSaveState.Dirty,
                     TakeoffSaveState.Saving,
                     TakeoffSaveState.Failed,
                 })
        {
            string text = MainWindow.FormatTakeoffSaveStatus(state, 2, previousSuccess);
            AssertFalse(text.Contains("Saved", StringComparison.OrdinalIgnoreCase), $"{state} status must not claim Saved");
        }

        AssertContains(
            MainWindow.FormatTakeoffSaveStatus(TakeoffSaveState.Failed, 2, previousSuccess),
            "2 pending",
            "failed status count");
    }

    private static TakeoffSaveService Service(
        FakeScheduler scheduler,
        Action<TakeoffItem> writer,
        Func<DateTime>? utcNow = null,
        Action<string>? setStatus = null)
    {
        var job = new OurPlanCoreJob { Name = "test", RootPath = "C:\\autosave-test" };
        return new TakeoffSaveService(
            () => job,
            setStatus ?? (_ => { }),
            writer,
            _ => true,
            utcNow ?? (() => DateTime.UtcNow),
            scheduler);
    }

    private static TakeoffItem Item(string name) =>
        new() { Name = name, FolderPath = $"C:\\autosave-test\\Takeoffs\\{name}" };

    private static void AssertSequence(IReadOnlyList<TakeoffSaveState> actual, params TakeoffSaveState[] expected)
    {
        AssertEqual(expected.Length, actual.Count, "state transition count");
        for (int index = 0; index < expected.Length; index++)
            AssertEqual(expected[index], actual[index], $"state transition {index}");
    }

    private static void AssertContains(string? actual, string expected, string message) =>
        AssertTrue(actual?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true, message);

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message) => AssertTrue(!condition, message);

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected '{expected}', got '{actual}'");
    }

    private sealed class FakeScheduler : ITakeoffSaveScheduler
    {
        private Action? _callback;

        public TimeSpan LastDelay { get; private set; }
        public bool HasScheduledCallback => _callback != null;

        public void Schedule(TimeSpan delay, Action callback)
        {
            LastDelay = delay;
            _callback = callback;
        }

        public void Stop() => _callback = null;

        public void Fire()
        {
            Action callback = _callback ?? throw new InvalidOperationException("No autosave callback is scheduled.");
            _callback = null;
            callback();
        }
    }
}
