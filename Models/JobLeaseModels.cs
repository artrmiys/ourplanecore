using System.Text.Json.Serialization;

namespace OurPlanCore;

internal sealed class JobLeaseInfo
{
    public const int CurrentSchemaVersion = 2;

    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonPropertyName("job_root")]
    public string JobRoot { get; set; } = "";

    [JsonPropertyName("machine")]
    public string Machine { get; set; } = "";

    [JsonPropertyName("process_id")]
    public int ProcessId { get; set; }

    [JsonPropertyName("instance_id")]
    public string InstanceId { get; set; } = "";

    [JsonPropertyName("process_started_at_utc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ProcessStartedAtUtc { get; set; }

    [JsonPropertyName("acquired_at_utc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? AcquiredAtUtc { get; set; }

    [JsonPropertyName("heartbeat_at_utc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? HeartbeatAtUtc { get; set; }

    [JsonPropertyName("app_version")]
    public string AppVersion { get; set; } = "";

    [JsonPropertyName("generation")]
    public long Generation { get; set; }

    [JsonIgnore]
    public DateTimeOffset EffectiveHeartbeatAtUtc =>
        HeartbeatAtUtc ?? AcquiredAtUtc ?? DateTimeOffset.MinValue;

    [JsonIgnore]
    public string OwnerDisplay => Machine;

    public JobLeaseInfo Clone() =>
        new()
        {
            SchemaVersion = SchemaVersion,
            JobRoot = JobRoot,
            Machine = Machine,
            ProcessId = ProcessId,
            InstanceId = InstanceId,
            ProcessStartedAtUtc = ProcessStartedAtUtc,
            AcquiredAtUtc = AcquiredAtUtc,
            HeartbeatAtUtc = HeartbeatAtUtc,
            AppVersion = AppVersion,
            Generation = Generation,
        };
}

internal enum JobLeaseObservationState
{
    Missing,
    OwnedByCurrentInstance,
    ActiveLocal,
    ActiveRemote,
    Stale,
    Unreadable,
}

internal sealed record JobLeaseObservation(
    string JobRoot,
    JobLeaseObservationState State,
    JobLeaseInfo? Lease,
    string Message)
{
    public string OwnerDisplay => Lease?.Machine ?? "";

    // Take Over is always explicit UI intent, and it is only safe after the
    // competing lease has been proven stale. Active local and remote owners
    // must never be replaced, even after an explicit confirmation.
    public bool IsTakeoverAllowed =>
        Lease != null &&
        State == JobLeaseObservationState.Stale;
}

internal enum JobLeaseAcquireStatus
{
    Acquired,
    AlreadyOwned,
    Conflict,
    Busy,
    Failed,
}

internal sealed record JobLeaseAcquireResult(
    JobLeaseAcquireStatus Status,
    JobLeaseObservation Observation,
    JobLeaseInfo? Lease,
    string Message)
{
    public bool Success =>
        Status is JobLeaseAcquireStatus.Acquired or JobLeaseAcquireStatus.AlreadyOwned;
}

internal enum JobLeaseOperationStatus
{
    Succeeded,
    NoLease,
    Busy,
    OwnershipLost,
    Failed,
}

internal sealed record JobLeaseOperationResult(
    JobLeaseOperationStatus Status,
    JobLeaseInfo? Lease,
    string Message)
{
    public bool Success =>
        Status is JobLeaseOperationStatus.Succeeded or JobLeaseOperationStatus.NoLease;
}

internal sealed record JobLeaseOwnershipLostEventArgs(
    JobLeaseInfo PreviousLease,
    JobLeaseInfo? CurrentOwner,
    string Message);

internal sealed class JobLeaseOptions
{
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan LeaseExpiry { get; init; } = TimeSpan.FromSeconds(30);

    public void Validate()
    {
        if (HeartbeatInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(HeartbeatInterval));
        if (LeaseExpiry <= HeartbeatInterval)
            throw new ArgumentOutOfRangeException(nameof(LeaseExpiry), "Lease expiry must exceed the heartbeat interval.");
    }
}

internal interface IJobLeaseClock
{
    DateTimeOffset UtcNow { get; }
}

internal interface IJobLeaseRuntime
{
    string MachineName { get; }
    int ProcessId { get; }
    string InstanceId { get; }
    DateTimeOffset ProcessStartedAtUtc { get; }
    string AppVersion { get; }
}

internal enum JobLeaseProcessState
{
    Running,
    NotRunning,
    Unknown,
}

internal readonly record struct JobLeaseProcessProbeResult(
    JobLeaseProcessState State,
    DateTimeOffset? ProcessStartedAtUtc = null);

internal interface IJobLeaseProcessProbe
{
    JobLeaseProcessProbeResult Probe(int processId);
}

internal interface IJobLeaseScheduler : IDisposable
{
    void Schedule(TimeSpan delay, Action callback);
    void Stop();
}

internal enum JobLeaseReadStatus
{
    Missing,
    Valid,
    Unreadable,
}

internal sealed record JobLeaseReadResult(
    JobLeaseReadStatus Status,
    JobLeaseInfo? Lease,
    string Error = "");

internal enum JobLeaseGuardStatus
{
    Acquired,
    Busy,
    Failed,
}

internal sealed record JobLeaseGuardResult(
    JobLeaseGuardStatus Status,
    IJobLeaseGuard? Guard,
    string Error = "");

internal interface IJobLeaseGuard : IDisposable
{
}

internal interface IJobLeaseStore
{
    JobLeaseReadResult Read(string jobRoot);
    JobLeaseGuardResult TryAcquireGuard(string jobRoot);
    void Write(string jobRoot, JobLeaseInfo lease);
    void Delete(string jobRoot);
}
