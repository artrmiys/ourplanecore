using OurPlanCore;

internal sealed class FakeJobLeaseClock : IJobLeaseClock
{
    public FakeJobLeaseClock(DateTimeOffset utcNow)
    {
        UtcNow = utcNow.ToUniversalTime();
    }

    public DateTimeOffset UtcNow { get; private set; }

    public void Advance(TimeSpan elapsed) => UtcNow = UtcNow.Add(elapsed);
}

internal sealed class FakeJobLeaseRuntime : IJobLeaseRuntime
{
    public FakeJobLeaseRuntime(
        string machineName,
        int processId,
        string instanceId,
        DateTimeOffset processStartedAtUtc,
        string appVersion = "test")
    {
        MachineName = machineName;
        ProcessId = processId;
        InstanceId = instanceId;
        ProcessStartedAtUtc = processStartedAtUtc.ToUniversalTime();
        AppVersion = appVersion;
    }

    public string MachineName { get; }
    public int ProcessId { get; }
    public string InstanceId { get; }
    public DateTimeOffset ProcessStartedAtUtc { get; }
    public string AppVersion { get; }
}

internal sealed class FakeJobLeaseProcessProbe : IJobLeaseProcessProbe
{
    private readonly Dictionary<int, JobLeaseProcessProbeResult> _results = [];

    public int CallCount { get; private set; }

    public void SetRunning(int processId, DateTimeOffset processStartedAtUtc) =>
        _results[processId] = new JobLeaseProcessProbeResult(
            JobLeaseProcessState.Running,
            processStartedAtUtc.ToUniversalTime());

    public void SetNotRunning(int processId) =>
        _results[processId] = new JobLeaseProcessProbeResult(JobLeaseProcessState.NotRunning);

    public void SetUnknown(int processId) =>
        _results[processId] = new JobLeaseProcessProbeResult(JobLeaseProcessState.Unknown);

    public JobLeaseProcessProbeResult Probe(int processId)
    {
        CallCount++;
        return _results.GetValueOrDefault(
            processId,
            new JobLeaseProcessProbeResult(JobLeaseProcessState.Unknown));
    }
}

internal sealed class FakeJobLeaseScheduler : IJobLeaseScheduler
{
    private Action? _callback;

    public int ScheduleCount { get; private set; }
    public int StopCount { get; private set; }
    public TimeSpan LastDelay { get; private set; }
    public bool HasScheduledCallback => _callback != null;
    public bool IsDisposed { get; private set; }

    public void Schedule(TimeSpan delay, Action callback)
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(FakeJobLeaseScheduler));
        ScheduleCount++;
        LastDelay = delay;
        _callback = callback;
    }

    public void Stop()
    {
        StopCount++;
        _callback = null;
    }

    public void Fire()
    {
        Action callback = _callback ?? throw new InvalidOperationException("No job lease heartbeat is scheduled.");
        _callback = null;
        callback();
    }

    public void Dispose()
    {
        IsDisposed = true;
        _callback = null;
    }
}

internal sealed class InMemoryJobLeaseStore : IJobLeaseStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, JobLeaseInfo> _leases =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _heldGuards =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _unreadable =
        new(StringComparer.OrdinalIgnoreCase);

    public bool ForceGuardBusy { get; set; }
    public int WriteCount { get; private set; }
    public int DeleteCount { get; private set; }

    public JobLeaseReadResult Read(string jobRoot)
    {
        lock (_sync)
        {
            if (_unreadable.TryGetValue(jobRoot, out string? error))
                return new JobLeaseReadResult(JobLeaseReadStatus.Unreadable, null, error);
            return _leases.TryGetValue(jobRoot, out JobLeaseInfo? lease)
                ? new JobLeaseReadResult(JobLeaseReadStatus.Valid, lease.Clone())
                : new JobLeaseReadResult(JobLeaseReadStatus.Missing, null);
        }
    }

    public JobLeaseGuardResult TryAcquireGuard(string jobRoot)
    {
        lock (_sync)
        {
            if (ForceGuardBusy || !_heldGuards.Add(jobRoot))
                return new JobLeaseGuardResult(JobLeaseGuardStatus.Busy, null, "guard busy");
            return new JobLeaseGuardResult(
                JobLeaseGuardStatus.Acquired,
                new MemoryGuard(this, jobRoot));
        }
    }

    public void Write(string jobRoot, JobLeaseInfo lease)
    {
        lock (_sync)
        {
            RequireGuard(jobRoot);
            WriteCount++;
            _unreadable.Remove(jobRoot);
            _leases[jobRoot] = lease.Clone();
        }
    }

    public void Delete(string jobRoot)
    {
        lock (_sync)
        {
            RequireGuard(jobRoot);
            DeleteCount++;
            _unreadable.Remove(jobRoot);
            _leases.Remove(jobRoot);
        }
    }

    public void ReplaceExternally(string jobRoot, JobLeaseInfo lease)
    {
        lock (_sync)
        {
            _unreadable.Remove(jobRoot);
            _leases[jobRoot] = lease.Clone();
        }
    }

    public void MarkUnreadable(string jobRoot, string error = "sharing violation")
    {
        lock (_sync)
            _unreadable[jobRoot] = error;
    }

    public JobLeaseInfo? Snapshot(string jobRoot)
    {
        lock (_sync)
            return _leases.TryGetValue(jobRoot, out JobLeaseInfo? lease) ? lease.Clone() : null;
    }

    private void RequireGuard(string jobRoot)
    {
        if (!_heldGuards.Contains(jobRoot))
            throw new InvalidOperationException("Lease mutation requires the exclusive guard.");
    }

    private void ReleaseGuard(string jobRoot)
    {
        lock (_sync)
            _heldGuards.Remove(jobRoot);
    }

    private sealed class MemoryGuard : IJobLeaseGuard
    {
        private InMemoryJobLeaseStore? _owner;
        private readonly string _jobRoot;

        public MemoryGuard(InMemoryJobLeaseStore owner, string jobRoot)
        {
            _owner = owner;
            _jobRoot = jobRoot;
        }

        public void Dispose()
        {
            InMemoryJobLeaseStore? owner = Interlocked.Exchange(ref _owner, null);
            owner?.ReleaseGuard(_jobRoot);
        }
    }
}
