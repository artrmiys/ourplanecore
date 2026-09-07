using System.IO;

namespace OurPlanCore;

internal sealed class JobLeaseService : IDisposable
{
    public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan DefaultLeaseExpiry = TimeSpan.FromSeconds(30);
    private readonly object _sync = new();
    private readonly IJobLeaseClock _clock;
    private readonly IJobLeaseRuntime _runtime;
    private readonly IJobLeaseProcessProbe _processProbe;
    private readonly IJobLeaseScheduler _scheduler;
    private readonly IJobLeaseStore _store;
    private readonly JobLeaseOptions _options;
    private JobLeaseInfo? _currentLease;
    private bool _disposed;
    internal JobLeaseService(
        IJobLeaseClock clock,
        IJobLeaseRuntime runtime,
        IJobLeaseProcessProbe processProbe,
        IJobLeaseScheduler scheduler,
        IJobLeaseStore store,
        JobLeaseOptions? options = null)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _processProbe = processProbe ?? throw new ArgumentNullException(nameof(processProbe));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? new JobLeaseOptions
        {
            HeartbeatInterval = DefaultHeartbeatInterval,
            LeaseExpiry = DefaultLeaseExpiry,
        };
        _options.Validate();
    }
    public event Action<JobLeaseOwnershipLostEventArgs>? OwnershipLost;

    public TimeSpan HeartbeatInterval => _options.HeartbeatInterval;
    public TimeSpan LeaseExpiry => _options.LeaseExpiry;
    public bool HasLease
    {
        get
        {
            lock (_sync)
                return _currentLease != null;
        }
    }
    public JobLeaseInfo? CurrentLease
    {
        get
        {
            lock (_sync)
                return _currentLease?.Clone();
        }
    }
    public static JobLeaseService CreateDefault()
    {
        IJobLeaseClock clock = SystemJobLeaseClock.Instance;
        return new JobLeaseService(
            clock,
            SystemJobLeaseRuntime.Create(clock),
            SystemJobLeaseProcessProbe.Instance,
            new TimerJobLeaseScheduler(),
            new JobLeaseFileStore());
    }
    public JobLeaseObservation Inspect(string jobRoot)
    {
        if (!TryNormalizeJobRoot(jobRoot, out string normalizedRoot, out string error))
            return UnreadableObservation(jobRoot ?? "", error);

        JobLeaseReadResult read;
        try
        {
            read = _store.Read(normalizedRoot);
        }
        catch (Exception ex)
        {
            return UnreadableObservation(normalizedRoot, ex.Message);
        }
        return Observe(normalizedRoot, read, _clock.UtcNow);
    }
    public JobLeaseAcquireResult TryAcquire(string jobRoot)
    {
        lock (_sync)
        {
            if (_disposed)
                return FailedAcquire(jobRoot, "Job lease service is disposed.");
            if (!TryNormalizeJobRoot(jobRoot, out string normalizedRoot, out string error))
                return FailedAcquire(jobRoot, error);
            if (_currentLease != null && !SamePath(_currentLease.JobRoot, normalizedRoot))
            {
                return FailedAcquire(
                    normalizedRoot,
                    $"Release the current job lease before opening '{normalizedRoot}'.");
            }

            JobLeaseGuardResult guardResult = _store.TryAcquireGuard(normalizedRoot);
            if (guardResult.Status != JobLeaseGuardStatus.Acquired || guardResult.Guard == null)
                return GuardAcquireResult(normalizedRoot, guardResult);

            using (guardResult.Guard)
            {
                try
                {
                    DateTimeOffset now = _clock.UtcNow;
                    JobLeaseReadResult read = _store.Read(normalizedRoot);
                    JobLeaseObservation observation = Observe(normalizedRoot, read, now);
                    if (observation.State == JobLeaseObservationState.Unreadable)
                    {
                        return new JobLeaseAcquireResult(
                            JobLeaseAcquireStatus.Failed,
                            observation,
                            null,
                            observation.Message);
                    }

                    if (observation.State == JobLeaseObservationState.Missing)
                    {
                        long generation = Math.Max(1, (_currentLease?.Generation ?? 0) + 1);
                        JobLeaseInfo lease = CreateLease(normalizedRoot, generation, now);
                        _store.Write(normalizedRoot, lease);
                        SetCurrentLeaseLocked(lease);
                        return AcquiredResult(observation, lease, JobLeaseAcquireStatus.Acquired);
                    }

                    if (observation.State == JobLeaseObservationState.OwnedByCurrentInstance &&
                        observation.Lease != null)
                    {
                        JobLeaseInfo lease = observation.Lease.Clone();
                        lease.Generation = NextGeneration(lease.Generation);
                        lease.HeartbeatAtUtc = now;
                        _store.Write(normalizedRoot, lease);
                        SetCurrentLeaseLocked(lease);
                        return AcquiredResult(observation, lease, JobLeaseAcquireStatus.AlreadyOwned);
                    }

                    return new JobLeaseAcquireResult(
                        JobLeaseAcquireStatus.Conflict,
                        observation,
                        null,
                        observation.Message);
                }
                catch (Exception ex)
                {
                    return FailedAcquire(normalizedRoot, $"Cannot acquire job lease: {ex.Message}");
                }
            }
        }
    }
    public JobLeaseAcquireResult TryTakeOver(string jobRoot, JobLeaseObservation observed)
    {
        ArgumentNullException.ThrowIfNull(observed);
        lock (_sync)
        {
            if (_disposed)
                return FailedAcquire(jobRoot, "Job lease service is disposed.");
            if (!TryNormalizeJobRoot(jobRoot, out string normalizedRoot, out string error))
                return FailedAcquire(jobRoot, error);
            if (_currentLease != null && !SamePath(_currentLease.JobRoot, normalizedRoot))
                return FailedAcquire(normalizedRoot, "Release the current job lease before takeover.");
            if (!SamePath(observed.JobRoot, normalizedRoot) || observed.Lease == null)
                return FailedAcquire(normalizedRoot, "The observed lease does not belong to this job.");
            if (!observed.IsTakeoverAllowed)
            {
                return new JobLeaseAcquireResult(
                    JobLeaseAcquireStatus.Conflict,
                    observed,
                    null,
                    "The observed lease cannot be taken over safely.");
            }

            JobLeaseGuardResult guardResult = _store.TryAcquireGuard(normalizedRoot);
            if (guardResult.Status != JobLeaseGuardStatus.Acquired || guardResult.Guard == null)
                return GuardAcquireResult(normalizedRoot, guardResult);

            using (guardResult.Guard)
            {
                try
                {
                    DateTimeOffset now = _clock.UtcNow;
                    JobLeaseReadResult read = _store.Read(normalizedRoot);
                    JobLeaseObservation current = Observe(normalizedRoot, read, now);
                    if (current.Lease == null ||
                        !MatchesObservedVersion(observed.Lease, current.Lease))
                    {
                        return new JobLeaseAcquireResult(
                            JobLeaseAcquireStatus.Conflict,
                            current,
                            null,
                            "The lease changed after it was observed; takeover was cancelled.");
                    }

                    if (!current.IsTakeoverAllowed)
                    {
                        return new JobLeaseAcquireResult(
                            JobLeaseAcquireStatus.Conflict,
                            current,
                            null,
                            "The lease can no longer be replaced; takeover was cancelled.");
                    }

                    JobLeaseInfo lease = CreateLease(
                        normalizedRoot,
                        NextGeneration(current.Lease.Generation),
                        now);
                    _store.Write(normalizedRoot, lease);
                    SetCurrentLeaseLocked(lease);
                    return AcquiredResult(current, lease, JobLeaseAcquireStatus.Acquired);
                }
                catch (Exception ex)
                {
                    return FailedAcquire(normalizedRoot, $"Cannot take over job lease: {ex.Message}");
                }
            }
        }
    }

    public JobLeaseOperationResult TryRenew()
    {
        JobLeaseOwnershipLostEventArgs? lost = null;
        JobLeaseOperationResult result;
        lock (_sync)
            result = TryRenewLocked(out lost);
        RaiseOwnershipLost(lost);
        return result;
    }

    public JobLeaseOperationResult TryRelease()
    {
        JobLeaseOwnershipLostEventArgs? lost = null;
        JobLeaseOperationResult result;
        lock (_sync)
            result = TryReleaseLocked(out lost);
        RaiseOwnershipLost(lost);
        return result;
    }

    public void StopHeartbeat()
    {
        lock (_sync)
            _scheduler.Stop();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
        }

        TryRelease();
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _currentLease = null;
            _scheduler.Dispose();
        }
    }

    private JobLeaseOperationResult TryRenewLocked(out JobLeaseOwnershipLostEventArgs? lost)
    {
        lost = null;
        if (_disposed)
            return new JobLeaseOperationResult(JobLeaseOperationStatus.Failed, null, "Job lease service is disposed.");
        if (_currentLease == null)
            return new JobLeaseOperationResult(JobLeaseOperationStatus.NoLease, null, "No job lease is held.");

        JobLeaseInfo expected = _currentLease.Clone();
        JobLeaseGuardResult guardResult = _store.TryAcquireGuard(expected.JobRoot);
        if (guardResult.Status != JobLeaseGuardStatus.Acquired || guardResult.Guard == null)
        {
            return HandleRenewalFailureLocked(
                expected,
                guardResult.Status == JobLeaseGuardStatus.Busy
                    ? JobLeaseOperationStatus.Busy
                    : JobLeaseOperationStatus.Failed,
                guardResult.Error,
                out lost);
        }

        using (guardResult.Guard)
        {
            try
            {
                JobLeaseReadResult read = _store.Read(expected.JobRoot);
                if (read.Status == JobLeaseReadStatus.Unreadable)
                {
                    return HandleRenewalFailureLocked(
                        expected,
                        JobLeaseOperationStatus.Failed,
                        read.Error,
                        out lost);
                }
                if (read.Status == JobLeaseReadStatus.Missing || read.Lease == null ||
                    !MatchesObservedVersion(expected, read.Lease))
                {
                    lost = LoseOwnershipLocked(expected, read.Lease, "Job lease ownership changed before heartbeat.");
                    return new JobLeaseOperationResult(JobLeaseOperationStatus.OwnershipLost, null, lost.Message);
                }

                JobLeaseInfo renewed = expected.Clone();
                renewed.Generation = NextGeneration(expected.Generation);
                renewed.HeartbeatAtUtc = _clock.UtcNow;
                _store.Write(expected.JobRoot, renewed);
                SetCurrentLeaseLocked(renewed);
                return new JobLeaseOperationResult(JobLeaseOperationStatus.Succeeded, renewed.Clone(), "Job lease renewed.");
            }
            catch (Exception ex)
            {
                return HandleRenewalFailureLocked(
                    expected,
                    JobLeaseOperationStatus.Failed,
                    $"Cannot renew job lease: {ex.Message}",
                    out lost);
            }
        }
    }

    private JobLeaseOperationResult HandleRenewalFailureLocked(
        JobLeaseInfo expected,
        JobLeaseOperationStatus retryStatus,
        string message,
        out JobLeaseOwnershipLostEventArgs? lost)
    {
        if (IsExpired(expected, _clock.UtcNow))
        {
            string detail = string.IsNullOrWhiteSpace(message) ? "unknown lease-store error" : message;
            lost = LoseOwnershipLocked(
                expected,
                current: null,
                $"Job lease heartbeat could not be confirmed before expiry: {detail}");
            return new JobLeaseOperationResult(
                JobLeaseOperationStatus.OwnershipLost,
                null,
                lost.Message);
        }

        lost = null;
        ScheduleHeartbeatLocked();
        return new JobLeaseOperationResult(retryStatus, expected, message);
    }

    private JobLeaseOperationResult TryReleaseLocked(out JobLeaseOwnershipLostEventArgs? lost)
    {
        lost = null;
        _scheduler.Stop();
        if (_currentLease == null)
            return new JobLeaseOperationResult(JobLeaseOperationStatus.NoLease, null, "No job lease is held.");

        JobLeaseInfo expected = _currentLease.Clone();
        JobLeaseGuardResult guardResult = _store.TryAcquireGuard(expected.JobRoot);
        if (guardResult.Status != JobLeaseGuardStatus.Acquired || guardResult.Guard == null)
        {
            ScheduleHeartbeatLocked();
            return new JobLeaseOperationResult(
                guardResult.Status == JobLeaseGuardStatus.Busy
                    ? JobLeaseOperationStatus.Busy
                    : JobLeaseOperationStatus.Failed,
                expected,
                guardResult.Error);
        }

        using (guardResult.Guard)
        {
            try
            {
                JobLeaseReadResult read = _store.Read(expected.JobRoot);
                if (read.Status == JobLeaseReadStatus.Unreadable)
                {
                    ScheduleHeartbeatLocked();
                    return new JobLeaseOperationResult(JobLeaseOperationStatus.Failed, expected, read.Error);
                }
                if (read.Status == JobLeaseReadStatus.Missing)
                {
                    _currentLease = null;
                    return new JobLeaseOperationResult(JobLeaseOperationStatus.NoLease, null, "Job lease was already absent.");
                }
                if (read.Lease == null || !MatchesObservedVersion(expected, read.Lease))
                {
                    lost = LoseOwnershipLocked(expected, read.Lease, "Job lease ownership changed before release.");
                    return new JobLeaseOperationResult(JobLeaseOperationStatus.OwnershipLost, null, lost.Message);
                }

                _store.Delete(expected.JobRoot);
                _currentLease = null;
                return new JobLeaseOperationResult(JobLeaseOperationStatus.Succeeded, null, "Job lease released.");
            }
            catch (Exception ex)
            {
                ScheduleHeartbeatLocked();
                return new JobLeaseOperationResult(
                    JobLeaseOperationStatus.Failed,
                    expected,
                    $"Cannot release job lease: {ex.Message}");
            }
        }
    }

    private JobLeaseObservation Observe(string jobRoot, JobLeaseReadResult read, DateTimeOffset now)
    {
        if (read.Status == JobLeaseReadStatus.Missing)
        {
            return new JobLeaseObservation(
                jobRoot,
                JobLeaseObservationState.Missing,
                null,
                "The job has no active lease.");
        }
        if (read.Status == JobLeaseReadStatus.Unreadable || read.Lease == null)
            return UnreadableObservation(jobRoot, read.Error);

        JobLeaseInfo lease = read.Lease.Clone();
        if (IsCurrentOwner(lease))
        {
            return new JobLeaseObservation(
                jobRoot,
                JobLeaseObservationState.OwnedByCurrentInstance,
                lease,
                "This application instance owns the job lease.");
        }

        bool expired = IsExpired(lease, now);
        if (!string.Equals(lease.Machine, _runtime.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return new JobLeaseObservation(
                jobRoot,
                expired ? JobLeaseObservationState.Stale : JobLeaseObservationState.ActiveRemote,
                lease,
                expired
                    ? "The remote job lease expired and may be taken over."
                    : "The job is open on another machine.");
        }

        if (lease.SchemaVersion >= JobLeaseInfo.CurrentSchemaVersion &&
            lease.ProcessStartedAtUtc != null)
        {
            JobLeaseProcessProbeResult process = ProbeSafely(lease.ProcessId);
            if (process.State == JobLeaseProcessState.NotRunning)
                return StaleLocal(jobRoot, lease, "The lease process is no longer running.");
            if (process.State == JobLeaseProcessState.Running &&
                process.ProcessStartedAtUtc != null)
            {
                return SameInstant(process.ProcessStartedAtUtc.Value, lease.ProcessStartedAtUtc.Value)
                    ? ActiveLocal(jobRoot, lease)
                    : StaleLocal(jobRoot, lease, "The process ID was reused by a different process.");
            }
        }

        return expired
            ? StaleLocal(jobRoot, lease, "The unverified local lease expired.")
            : ActiveLocal(jobRoot, lease);
    }

    private JobLeaseInfo CreateLease(string jobRoot, long generation, DateTimeOffset now) =>
        new()
        {
            SchemaVersion = JobLeaseInfo.CurrentSchemaVersion,
            JobRoot = jobRoot,
            Machine = _runtime.MachineName,
            ProcessId = _runtime.ProcessId,
            InstanceId = _runtime.InstanceId,
            ProcessStartedAtUtc = _runtime.ProcessStartedAtUtc.ToUniversalTime(),
            AcquiredAtUtc = now.ToUniversalTime(),
            HeartbeatAtUtc = now.ToUniversalTime(),
            AppVersion = _runtime.AppVersion,
            Generation = generation,
        };

    private void SetCurrentLeaseLocked(JobLeaseInfo lease)
    {
        _currentLease = lease.Clone();
        ScheduleHeartbeatLocked();
    }

    private void ScheduleHeartbeatLocked()
    {
        if (!_disposed && _currentLease != null)
            _scheduler.Schedule(_options.HeartbeatInterval, HeartbeatTick);
    }

    private void HeartbeatTick() => TryRenew();

    private JobLeaseOwnershipLostEventArgs LoseOwnershipLocked(
        JobLeaseInfo previous,
        JobLeaseInfo? current,
        string message)
    {
        _currentLease = null;
        _scheduler.Stop();
        return new JobLeaseOwnershipLostEventArgs(previous.Clone(), current?.Clone(), message);
    }

    private void RaiseOwnershipLost(JobLeaseOwnershipLostEventArgs? args)
    {
        if (args == null)
            return;
        try
        {
            OwnershipLost?.Invoke(args);
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, "Job lease ownership-lost handler failed");
        }
    }

    private bool IsCurrentOwner(JobLeaseInfo lease) =>
        lease.SchemaVersion >= JobLeaseInfo.CurrentSchemaVersion &&
        string.Equals(lease.InstanceId, _runtime.InstanceId, StringComparison.Ordinal) &&
        string.Equals(lease.Machine, _runtime.MachineName, StringComparison.OrdinalIgnoreCase) &&
        lease.ProcessId == _runtime.ProcessId &&
        lease.ProcessStartedAtUtc != null &&
        SameInstant(lease.ProcessStartedAtUtc.Value, _runtime.ProcessStartedAtUtc);

    private bool IsExpired(JobLeaseInfo lease, DateTimeOffset now) =>
        now.ToUniversalTime() - lease.EffectiveHeartbeatAtUtc.ToUniversalTime() >= _options.LeaseExpiry;

    private JobLeaseProcessProbeResult ProbeSafely(int processId)
    {
        try
        {
            return _processProbe.Probe(processId);
        }
        catch
        {
            return new JobLeaseProcessProbeResult(JobLeaseProcessState.Unknown);
        }
    }

    private static bool MatchesObservedVersion(JobLeaseInfo expected, JobLeaseInfo actual)
    {
        if (expected.SchemaVersion == 1 || actual.SchemaVersion == 1)
        {
            return expected.SchemaVersion == actual.SchemaVersion &&
                   string.Equals(expected.JobRoot, actual.JobRoot, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(expected.Machine, actual.Machine, StringComparison.OrdinalIgnoreCase) &&
                   expected.ProcessId == actual.ProcessId &&
                   SameNullableInstant(expected.AcquiredAtUtc, actual.AcquiredAtUtc) &&
                   expected.Generation == actual.Generation;
        }

        return SamePath(expected.JobRoot, actual.JobRoot) &&
               string.Equals(expected.InstanceId, actual.InstanceId, StringComparison.Ordinal) &&
               string.Equals(expected.Machine, actual.Machine, StringComparison.OrdinalIgnoreCase) &&
               expected.ProcessId == actual.ProcessId &&
               SameNullableInstant(expected.ProcessStartedAtUtc, actual.ProcessStartedAtUtc) &&
               expected.Generation == actual.Generation;
    }

    private static bool SameNullableInstant(DateTimeOffset? left, DateTimeOffset? right) =>
        left == null ? right == null : right != null && SameInstant(left.Value, right.Value);

    private static bool SameInstant(DateTimeOffset left, DateTimeOffset right) =>
        left.ToUniversalTime().Ticks == right.ToUniversalTime().Ticks;

    private static long NextGeneration(long current) =>
        checked(Math.Max(0, current) + 1);

    private static bool TryNormalizeJobRoot(string? jobRoot, out string normalized, out string error)
    {
        normalized = "";
        error = "";
        if (string.IsNullOrWhiteSpace(jobRoot))
        {
            error = "Job root is missing.";
            return false;
        }

        try
        {
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(jobRoot));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"Invalid job root: {ex.Message}";
            return false;
        }
    }

    private static bool SamePath(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static JobLeaseObservation ActiveLocal(string root, JobLeaseInfo lease) =>
        new(root, JobLeaseObservationState.ActiveLocal, lease, "The job is open in another local process.");

    private static JobLeaseObservation StaleLocal(string root, JobLeaseInfo lease, string message) =>
        new(root, JobLeaseObservationState.Stale, lease, message);

    private static JobLeaseObservation UnreadableObservation(string root, string message) =>
        new(root, JobLeaseObservationState.Unreadable, null, string.IsNullOrWhiteSpace(message) ? "The job lease is unreadable." : message);

    private static JobLeaseAcquireResult AcquiredResult(
        JobLeaseObservation previous,
        JobLeaseInfo lease,
        JobLeaseAcquireStatus status) =>
        new(status, previous, lease.Clone(), status == JobLeaseAcquireStatus.Acquired ? "Job lease acquired." : "Job lease already owned.");

    private static JobLeaseAcquireResult GuardAcquireResult(string root, JobLeaseGuardResult guard) =>
        new(
            guard.Status == JobLeaseGuardStatus.Busy ? JobLeaseAcquireStatus.Busy : JobLeaseAcquireStatus.Failed,
            UnreadableObservation(root, guard.Error),
            null,
            guard.Error);

    private static JobLeaseAcquireResult FailedAcquire(string? root, string message) =>
        new(JobLeaseAcquireStatus.Failed, UnreadableObservation(root ?? "", message), null, message);
}
