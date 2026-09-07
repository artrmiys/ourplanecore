using OurPlanCore;
using System.Text.Json;

internal static class JobLeaseServiceTests
{
    private static readonly DateTimeOffset Baseline =
        new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    public static void SchemaV2RoundTripsAllRequiredFields()
    {
        string root = CreateTempRoot();
        try
        {
            var store = new JobLeaseFileStore();
            JobLeaseInfo expected = Lease(
                root,
                "machine-a",
                101,
                "instance-a",
                Baseline.AddMinutes(-1),
                Baseline,
                generation: 7);

            JobLeaseGuardResult guardResult = store.TryAcquireGuard(root);
            AssertEqual(JobLeaseGuardStatus.Acquired, guardResult.Status, "file guard");
            using (guardResult.Guard!)
                store.Write(root, expected);

            string json = File.ReadAllText(JobLeaseFileStore.LeasePath(root));
            using JsonDocument document = JsonDocument.Parse(json);
            foreach (string property in new[]
                     {
                         "job_root",
                         "machine",
                         "process_id",
                         "instance_id",
                         "process_started_at_utc",
                         "acquired_at_utc",
                         "heartbeat_at_utc",
                         "app_version",
                         "generation",
                     })
            {
                AssertTrue(document.RootElement.TryGetProperty(property, out _), $"schema property {property}");
            }

            JobLeaseReadResult read = store.Read(root);
            AssertEqual(JobLeaseReadStatus.Valid, read.Status, "round-trip read");
            AssertEqual(JobLeaseInfo.CurrentSchemaVersion, read.Lease!.SchemaVersion, "schema version");
            AssertEqual(expected.InstanceId, read.Lease.InstanceId, "instance id");
            AssertEqual(expected.Generation, read.Lease.Generation, "generation");
            AssertEqual(expected.ProcessStartedAtUtc, read.Lease.ProcessStartedAtUtc, "process start");
            AssertEqual(expected.HeartbeatAtUtc, read.Lease.HeartbeatAtUtc, "heartbeat");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    public static void LegacyV1LockIsReadCompatible()
    {
        string root = CreateTempRoot();
        try
        {
            var legacy = new Dictionary<string, object?>
            {
                ["schema_version"] = 1,
                ["job_root"] = root,
                ["process_id"] = 202,
                ["machine"] = "legacy-machine",
                ["created_at_utc"] = Baseline.ToString("O"),
            };
            File.WriteAllText(
                JobLeaseFileStore.LeasePath(root),
                JsonSerializer.Serialize(legacy));

            JobLeaseReadResult read = new JobLeaseFileStore().Read(root);

            AssertEqual(JobLeaseReadStatus.Valid, read.Status, "legacy read status");
            AssertEqual(1, read.Lease!.SchemaVersion, "legacy schema");
            AssertEqual("legacy-machine", read.Lease.Machine, "legacy machine");
            AssertEqual(202, read.Lease.ProcessId, "legacy process");
            AssertEqual(Baseline, read.Lease.AcquiredAtUtc, "legacy acquired time");
            AssertEqual(Baseline, read.Lease.HeartbeatAtUtc, "legacy heartbeat time");
            AssertEqual(0L, read.Lease.Generation, "legacy generation");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    public static void ActiveLocalLeaseBlocksSecondInstance()
    {
        string root = MemoryRoot("active-local");
        var clock = new FakeJobLeaseClock(Baseline);
        var probe = new FakeJobLeaseProcessProbe();
        var store = new InMemoryJobLeaseStore();
        FakeJobLeaseRuntime firstRuntime = Runtime("machine-a", 301, "first", Baseline.AddMinutes(-2));
        FakeJobLeaseRuntime secondRuntime = Runtime("machine-a", 302, "second", Baseline.AddMinutes(-1));
        probe.SetRunning(firstRuntime.ProcessId, firstRuntime.ProcessStartedAtUtc);
        using JobLeaseService first = Service(clock, firstRuntime, probe, new FakeJobLeaseScheduler(), store);
        using JobLeaseService second = Service(clock, secondRuntime, probe, new FakeJobLeaseScheduler(), store);

        AssertTrue(first.TryAcquire(root).Success, "first instance acquire");
        JobLeaseAcquireResult blocked = second.TryAcquire(root);

        AssertEqual(JobLeaseAcquireStatus.Conflict, blocked.Status, "second acquire status");
        AssertEqual(JobLeaseObservationState.ActiveLocal, blocked.Observation.State, "local conflict state");
        AssertFalse(blocked.Observation.IsTakeoverAllowed, "active local takeover availability");
        AssertEqual(
            JobLeaseAcquireStatus.Conflict,
            second.TryTakeOver(root, blocked.Observation).Status,
            "active local takeover status");
        AssertEqual("first", store.Snapshot(root)!.InstanceId, "first owner preserved");
    }

    public static void StoppedLocalProcessIsImmediatelyStale()
    {
        string root = MemoryRoot("stopped-local");
        var clock = new FakeJobLeaseClock(Baseline);
        var probe = new FakeJobLeaseProcessProbe();
        var store = new InMemoryJobLeaseStore();
        FakeJobLeaseRuntime runtime = Runtime("machine-a", 402, "current", Baseline.AddMinutes(-1));
        JobLeaseInfo crashed = Lease(
            root,
            "machine-a",
            401,
            "crashed",
            Baseline.AddMinutes(-2),
            Baseline,
            generation: 4);
        probe.SetNotRunning(crashed.ProcessId);
        store.ReplaceExternally(root, crashed);
        using JobLeaseService service = Service(clock, runtime, probe, new FakeJobLeaseScheduler(), store);

        JobLeaseObservation observation = service.Inspect(root);
        JobLeaseAcquireResult takeover = service.TryTakeOver(root, observation);

        AssertEqual(JobLeaseObservationState.Stale, observation.State, "crashed local lease");
        AssertTrue(observation.IsTakeoverAllowed, "crashed lease takeover");
        AssertTrue(takeover.Success, "crashed lease takeover result");
        AssertEqual("current", store.Snapshot(root)!.InstanceId, "crashed lease replacement owner");
        AssertEqual(5L, store.Snapshot(root)!.Generation, "crashed lease replacement generation");
    }

    public static void ReusedLocalProcessIdDoesNotKeepLeaseActive()
    {
        string root = MemoryRoot("pid-reuse");
        var clock = new FakeJobLeaseClock(Baseline);
        var probe = new FakeJobLeaseProcessProbe();
        var store = new InMemoryJobLeaseStore();
        FakeJobLeaseRuntime runtime = Runtime("machine-a", 502, "current", Baseline.AddMinutes(-1));
        JobLeaseInfo previous = Lease(
            root,
            "machine-a",
            501,
            "previous",
            Baseline.AddHours(-1),
            Baseline,
            generation: 2);
        probe.SetRunning(previous.ProcessId, Baseline.AddMinutes(-5));
        store.ReplaceExternally(root, previous);
        using JobLeaseService service = Service(clock, runtime, probe, new FakeJobLeaseScheduler(), store);

        JobLeaseObservation observation = service.Inspect(root);

        AssertEqual(JobLeaseObservationState.Stale, observation.State, "PID reuse state");
        AssertContains(observation.Message, "reused", "PID reuse explanation");
    }

    public static void RemoteLeaseStaysActiveUntilExpiry()
    {
        string root = MemoryRoot("remote-expiry");
        var clock = new FakeJobLeaseClock(Baseline);
        var probe = new FakeJobLeaseProcessProbe();
        var store = new InMemoryJobLeaseStore();
        store.ReplaceExternally(
            root,
            Lease(root, "remote-machine", 601, "remote", Baseline.AddMinutes(-2), Baseline, generation: 3));
        using JobLeaseService service = Service(
            clock,
            Runtime("local-machine", 602, "local", Baseline.AddMinutes(-1)),
            probe,
            new FakeJobLeaseScheduler(),
            store);

        AssertEqual(JobLeaseObservationState.ActiveRemote, service.Inspect(root).State, "fresh remote state");
        clock.Advance(TimeSpan.FromSeconds(9));
        AssertEqual(JobLeaseObservationState.ActiveRemote, service.Inspect(root).State, "remote before expiry");
        clock.Advance(TimeSpan.FromSeconds(1));
        AssertEqual(JobLeaseObservationState.Stale, service.Inspect(root).State, "remote at expiry");
        AssertEqual(0, probe.CallCount, "remote PID must not be probed locally");
    }

    public static void ExplicitTakeoverRejectsActiveRemoteAndReplacesStaleOwner()
    {
        string root = MemoryRoot("active-takeover");
        var clock = new FakeJobLeaseClock(Baseline);
        var store = new InMemoryJobLeaseStore();
        store.ReplaceExternally(
            root,
            Lease(root, "remote-machine", 701, "remote", Baseline.AddMinutes(-2), Baseline, generation: 8));
        using JobLeaseService service = Service(
            clock,
            Runtime("local-machine", 702, "local", Baseline.AddMinutes(-1)),
            new FakeJobLeaseProcessProbe(),
            new FakeJobLeaseScheduler(),
            store);

        JobLeaseObservation observed = service.Inspect(root);
        JobLeaseAcquireResult activeResult = service.TryTakeOver(root, observed);

        AssertEqual(JobLeaseObservationState.ActiveRemote, observed.State, "observed active remote");
        AssertFalse(observed.IsTakeoverAllowed, "active remote takeover availability");
        AssertEqual(JobLeaseAcquireStatus.Conflict, activeResult.Status, "active remote takeover status");
        AssertEqual("remote", store.Snapshot(root)!.InstanceId, "active remote owner preserved");

        clock.Advance(TimeSpan.FromSeconds(10));
        JobLeaseObservation stale = service.Inspect(root);
        JobLeaseAcquireResult staleResult = service.TryTakeOver(root, stale);

        AssertEqual(JobLeaseObservationState.Stale, stale.State, "observed stale remote");
        AssertTrue(stale.IsTakeoverAllowed, "stale remote takeover availability");
        AssertTrue(staleResult.Success, "stale remote takeover result");
        AssertEqual("local", store.Snapshot(root)!.InstanceId, "takeover owner");
        AssertEqual(9L, store.Snapshot(root)!.Generation, "takeover generation");
    }

    public static void TakeoverCasNeverClobbersChangedLease()
    {
        string root = MemoryRoot("takeover-cas");
        var clock = new FakeJobLeaseClock(Baseline);
        var store = new InMemoryJobLeaseStore();
        JobLeaseInfo remote = Lease(
            root,
            "remote-machine",
            801,
            "remote",
            Baseline.AddMinutes(-2),
            Baseline,
            generation: 4);
        store.ReplaceExternally(root, remote);
        using JobLeaseService service = Service(
            clock,
            Runtime("local-machine", 802, "local", Baseline.AddMinutes(-1)),
            new FakeJobLeaseProcessProbe(),
            new FakeJobLeaseScheduler(),
            store);

        clock.Advance(TimeSpan.FromSeconds(10));
        JobLeaseObservation observed = service.Inspect(root);
        AssertEqual(JobLeaseObservationState.Stale, observed.State, "CAS observed stale state");
        remote.Generation++;
        store.ReplaceExternally(root, remote);
        JobLeaseAcquireResult result = service.TryTakeOver(root, observed);

        AssertEqual(JobLeaseAcquireStatus.Conflict, result.Status, "changed lease takeover");
        AssertEqual("remote", store.Snapshot(root)!.InstanceId, "changed owner preserved");
        AssertEqual(5L, store.Snapshot(root)!.Generation, "changed generation preserved");
    }

    public static void HeartbeatRenewsOnlyExactOwnerAndGeneration()
    {
        string root = MemoryRoot("renew-owner");
        var clock = new FakeJobLeaseClock(Baseline);
        var scheduler = new FakeJobLeaseScheduler();
        var store = new InMemoryJobLeaseStore();
        FakeJobLeaseRuntime runtime = Runtime("machine-a", 901, "current", Baseline.AddMinutes(-1));
        using JobLeaseService service = Service(
            clock,
            runtime,
            new FakeJobLeaseProcessProbe(),
            scheduler,
            store);
        AssertTrue(service.TryAcquire(root).Success, "renew test acquire");
        AssertEqual(1L, store.Snapshot(root)!.Generation, "initial generation");

        clock.Advance(TimeSpan.FromSeconds(2));
        scheduler.Fire();

        AssertEqual(2L, store.Snapshot(root)!.Generation, "heartbeat generation");
        AssertEqual(clock.UtcNow, store.Snapshot(root)!.HeartbeatAtUtc, "heartbeat timestamp");
        AssertTrue(scheduler.HasScheduledCallback, "next heartbeat scheduled");

        int lostEvents = 0;
        service.OwnershipLost += _ => lostEvents++;
        store.ForceGuardBusy = true;
        clock.Advance(TimeSpan.FromSeconds(5));
        JobLeaseOperationResult retry = service.TryRenew();
        AssertEqual(JobLeaseOperationStatus.Busy, retry.Status, "busy heartbeat before expiry");
        AssertTrue(service.HasLease, "lease retained before expiry");
        AssertEqual(0, lostEvents, "ownership event before expiry");

        clock.Advance(TimeSpan.FromSeconds(6));
        JobLeaseOperationResult expired = service.TryRenew();
        AssertEqual(JobLeaseOperationStatus.OwnershipLost, expired.Status, "busy heartbeat after expiry");
        AssertFalse(service.HasLease, "lease dropped after unconfirmed expiry");
        AssertFalse(scheduler.HasScheduledCallback, "heartbeat stopped after unconfirmed expiry");
        AssertEqual(1, lostEvents, "ownership event after unconfirmed expiry");
    }

    public static void LostOwnershipStopsHeartbeatAndPreservesNewOwner()
    {
        string root = MemoryRoot("lost-owner");
        var clock = new FakeJobLeaseClock(Baseline);
        var scheduler = new FakeJobLeaseScheduler();
        var store = new InMemoryJobLeaseStore();
        using JobLeaseService service = Service(
            clock,
            Runtime("machine-a", 1001, "current", Baseline.AddMinutes(-1)),
            new FakeJobLeaseProcessProbe(),
            scheduler,
            store);
        int lostEvents = 0;
        service.OwnershipLost += _ => lostEvents++;
        AssertTrue(service.TryAcquire(root).Success, "lost-owner acquire");
        JobLeaseInfo replacement = Lease(
            root,
            "machine-b",
            1002,
            "replacement",
            Baseline.AddMinutes(-1),
            Baseline,
            generation: 2);
        store.ReplaceExternally(root, replacement);

        scheduler.Fire();

        AssertFalse(service.HasLease, "lost owner state");
        AssertFalse(scheduler.HasScheduledCallback, "lost owner heartbeat stopped");
        AssertEqual(1, lostEvents, "ownership lost event count");
        AssertEqual("replacement", store.Snapshot(root)!.InstanceId, "replacement preserved");
    }

    public static void ReleaseNeverDeletesChangedOwner()
    {
        string root = MemoryRoot("release-owner");
        var store = new InMemoryJobLeaseStore();
        using JobLeaseService service = Service(
            new FakeJobLeaseClock(Baseline),
            Runtime("machine-a", 1101, "current", Baseline.AddMinutes(-1)),
            new FakeJobLeaseProcessProbe(),
            new FakeJobLeaseScheduler(),
            store);
        AssertTrue(service.TryAcquire(root).Success, "release test acquire");
        store.ReplaceExternally(
            root,
            Lease(root, "machine-b", 1102, "replacement", Baseline.AddMinutes(-1), Baseline, generation: 2));

        JobLeaseOperationResult result = service.TryRelease();

        AssertEqual(JobLeaseOperationStatus.OwnershipLost, result.Status, "owner-checked release");
        AssertEqual(0, store.DeleteCount, "changed lease delete count");
        AssertEqual("replacement", store.Snapshot(root)!.InstanceId, "release replacement preserved");
    }

    public static void UnreadableLeaseCannotBeSilentlyReplaced()
    {
        string root = MemoryRoot("unreadable");
        var store = new InMemoryJobLeaseStore();
        store.MarkUnreadable(root);
        using JobLeaseService service = Service(
            new FakeJobLeaseClock(Baseline),
            Runtime("machine-a", 1201, "current", Baseline.AddMinutes(-1)),
            new FakeJobLeaseProcessProbe(),
            new FakeJobLeaseScheduler(),
            store);

        JobLeaseAcquireResult result = service.TryAcquire(root);

        AssertEqual(JobLeaseAcquireStatus.Failed, result.Status, "unreadable acquire");
        AssertEqual(JobLeaseObservationState.Unreadable, result.Observation.State, "unreadable state");
        AssertFalse(result.Observation.IsTakeoverAllowed, "unreadable takeover");
        AssertEqual(0, store.WriteCount, "unreadable write count");
    }

    public static void FileGuardIsExclusiveAndReusable()
    {
        string root = CreateTempRoot();
        try
        {
            var store = new JobLeaseFileStore();
            JobLeaseGuardResult first = store.TryAcquireGuard(root);
            AssertEqual(JobLeaseGuardStatus.Acquired, first.Status, "first file guard");
            JobLeaseGuardResult second = store.TryAcquireGuard(root);
            AssertEqual(JobLeaseGuardStatus.Busy, second.Status, "second file guard");
            first.Guard!.Dispose();

            JobLeaseGuardResult third = store.TryAcquireGuard(root);
            AssertEqual(JobLeaseGuardStatus.Acquired, third.Status, "reused file guard");
            third.Guard!.Dispose();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static JobLeaseService Service(
        FakeJobLeaseClock clock,
        FakeJobLeaseRuntime runtime,
        FakeJobLeaseProcessProbe probe,
        FakeJobLeaseScheduler scheduler,
        IJobLeaseStore store) =>
        new(
            clock,
            runtime,
            probe,
            scheduler,
            store,
            new JobLeaseOptions
            {
                HeartbeatInterval = TimeSpan.FromSeconds(2),
                LeaseExpiry = TimeSpan.FromSeconds(10),
            });

    private static FakeJobLeaseRuntime Runtime(
        string machine,
        int processId,
        string instanceId,
        DateTimeOffset startedAt) =>
        new(machine, processId, instanceId, startedAt);

    private static JobLeaseInfo Lease(
        string root,
        string machine,
        int processId,
        string instanceId,
        DateTimeOffset processStartedAt,
        DateTimeOffset heartbeatAt,
        long generation) =>
        new()
        {
            SchemaVersion = JobLeaseInfo.CurrentSchemaVersion,
            JobRoot = root,
            Machine = machine,
            ProcessId = processId,
            InstanceId = instanceId,
            ProcessStartedAtUtc = processStartedAt.ToUniversalTime(),
            AcquiredAtUtc = heartbeatAt.AddMinutes(-1).ToUniversalTime(),
            HeartbeatAtUtc = heartbeatAt.ToUniversalTime(),
            AppVersion = "test",
            Generation = generation,
        };

    private static string MemoryRoot(string name) =>
        Path.GetFullPath(Path.Combine(Path.GetTempPath(), "onc_job_lease_memory", name));

    private static string CreateTempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "onc_job_lease", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return Path.GetFullPath(root);
    }

    private static void DeleteTempRoot(string root)
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch
        {
            // Test cleanup is best-effort; the assertion result is authoritative.
        }
    }

    private static void AssertContains(string actual, string expected, string message) =>
        AssertTrue(actual.Contains(expected, StringComparison.OrdinalIgnoreCase), message);

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
}
