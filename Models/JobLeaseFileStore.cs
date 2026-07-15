using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace OurPlanCore;

internal sealed class JobLeaseFileStore : IJobLeaseStore
{
    public const string LeaseFileName = ".~lock";
    public const string GuardFileName = ".~lock.guard";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static string LeasePath(string jobRoot) =>
        Path.Combine(jobRoot, LeaseFileName);

    public static string GuardPath(string jobRoot) =>
        Path.Combine(jobRoot, GuardFileName);

    public JobLeaseReadResult Read(string jobRoot)
    {
        string path = LeasePath(jobRoot);
        if (!File.Exists(path))
            return new JobLeaseReadResult(JobLeaseReadStatus.Missing, null);

        try
        {
            SerializedLease? stored = JsonSerializer.Deserialize<SerializedLease>(
                File.ReadAllText(path),
                JsonOptions);
            if (stored == null)
                return Unreadable(path, "The lease file is empty.");

            int schemaVersion = stored.SchemaVersion <= 0 ? 1 : stored.SchemaVersion;
            if (schemaVersion is not 1 and not JobLeaseInfo.CurrentSchemaVersion)
                return Unreadable(path, $"Unsupported lease schema {schemaVersion}.");

            DateTimeOffset? legacyCreated = stored.CreatedAtUtc;
            if (schemaVersion == 1 && legacyCreated == null)
                legacyCreated = new DateTimeOffset(File.GetLastWriteTimeUtc(path));

            var lease = new JobLeaseInfo
            {
                SchemaVersion = schemaVersion,
                JobRoot = stored.JobRoot?.Trim() ?? "",
                Machine = stored.Machine?.Trim() ?? "",
                ProcessId = stored.ProcessId,
                InstanceId = stored.InstanceId?.Trim() ?? "",
                ProcessStartedAtUtc = stored.ProcessStartedAtUtc?.ToUniversalTime(),
                AcquiredAtUtc = (stored.AcquiredAtUtc ?? legacyCreated)?.ToUniversalTime(),
                HeartbeatAtUtc = (stored.HeartbeatAtUtc ?? legacyCreated)?.ToUniversalTime(),
                AppVersion = stored.AppVersion?.Trim() ?? "",
                Generation = stored.Generation,
            };

            string validationError = ValidateReadLease(lease);
            return string.IsNullOrWhiteSpace(validationError)
                ? new JobLeaseReadResult(JobLeaseReadStatus.Valid, lease)
                : Unreadable(path, validationError);
        }
        catch (FileNotFoundException)
        {
            return new JobLeaseReadResult(JobLeaseReadStatus.Missing, null);
        }
        catch (DirectoryNotFoundException)
        {
            return new JobLeaseReadResult(JobLeaseReadStatus.Missing, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return Unreadable(path, ex.Message);
        }
    }

    public JobLeaseGuardResult TryAcquireGuard(string jobRoot)
    {
        if (!Directory.Exists(jobRoot))
        {
            return new JobLeaseGuardResult(
                JobLeaseGuardStatus.Failed,
                null,
                $"Job directory does not exist: {jobRoot}");
        }

        string path = GuardPath(jobRoot);
        try
        {
            var stream = new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose | FileOptions.WriteThrough);
            return new JobLeaseGuardResult(
                JobLeaseGuardStatus.Acquired,
                new FileJobLeaseGuard(path, stream));
        }
        catch (IOException ex)
        {
            return new JobLeaseGuardResult(JobLeaseGuardStatus.Busy, null, ex.Message);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or NotSupportedException)
        {
            return new JobLeaseGuardResult(JobLeaseGuardStatus.Failed, null, ex.Message);
        }
    }

    public void Write(string jobRoot, JobLeaseInfo lease)
    {
        if (lease.SchemaVersion != JobLeaseInfo.CurrentSchemaVersion)
            throw new InvalidOperationException("Only schema v2 leases may be written.");

        string validationError = ValidateReadLease(lease);
        if (!string.IsNullOrWhiteSpace(validationError))
            throw new InvalidDataException(validationError);

        IoUtil.WriteAllTextAtomic(LeasePath(jobRoot), JsonSerializer.Serialize(lease, JsonOptions));
    }

    public void Delete(string jobRoot)
    {
        string path = LeasePath(jobRoot);
        if (File.Exists(path))
            File.Delete(path);
    }

    private static JobLeaseReadResult Unreadable(string path, string error) =>
        new(
            JobLeaseReadStatus.Unreadable,
            null,
            $"Cannot read job lease '{path}': {error}");

    private static string ValidateReadLease(JobLeaseInfo lease)
    {
        if (string.IsNullOrWhiteSpace(lease.JobRoot))
            return "Lease job_root is missing.";
        if (string.IsNullOrWhiteSpace(lease.Machine))
            return "Lease machine is missing.";
        if (lease.ProcessId <= 0)
            return "Lease process_id is invalid.";
        if (lease.AcquiredAtUtc == null || lease.HeartbeatAtUtc == null)
            return "Lease timestamps are missing.";

        if (lease.SchemaVersion == 1)
            return "";
        if (string.IsNullOrWhiteSpace(lease.InstanceId))
            return "Lease instance_id is missing.";
        if (lease.ProcessStartedAtUtc == null)
            return "Lease process_started_at_utc is missing.";
        if (string.IsNullOrWhiteSpace(lease.AppVersion))
            return "Lease app_version is missing.";
        if (lease.Generation <= 0)
            return "Lease generation is invalid.";
        return "";
    }

    private sealed class SerializedLease
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("job_root")]
        public string? JobRoot { get; set; }

        [JsonPropertyName("machine")]
        public string? Machine { get; set; }

        [JsonPropertyName("process_id")]
        public int ProcessId { get; set; }

        [JsonPropertyName("instance_id")]
        public string? InstanceId { get; set; }

        [JsonPropertyName("process_started_at_utc")]
        public DateTimeOffset? ProcessStartedAtUtc { get; set; }

        [JsonPropertyName("acquired_at_utc")]
        public DateTimeOffset? AcquiredAtUtc { get; set; }

        [JsonPropertyName("heartbeat_at_utc")]
        public DateTimeOffset? HeartbeatAtUtc { get; set; }

        [JsonPropertyName("created_at_utc")]
        public DateTimeOffset? CreatedAtUtc { get; set; }

        [JsonPropertyName("app_version")]
        public string? AppVersion { get; set; }

        [JsonPropertyName("generation")]
        public long Generation { get; set; }
    }

    private sealed class FileJobLeaseGuard : IJobLeaseGuard
    {
        private readonly string _path;
        private FileStream? _stream;

        public FileJobLeaseGuard(string path, FileStream stream)
        {
            _path = path;
            _stream = stream;
        }

        public void Dispose()
        {
            FileStream? stream = Interlocked.Exchange(ref _stream, null);
            if (stream == null)
                return;

            stream.Dispose();
            try
            {
                if (File.Exists(_path))
                    File.Delete(_path);
            }
            catch
            {
                // DeleteOnClose is authoritative; this only cleans up filesystems
                // that ignore it. A leftover unlocked guard is safe to reopen.
            }
        }
    }
}

internal sealed class SystemJobLeaseClock : IJobLeaseClock
{
    public static SystemJobLeaseClock Instance { get; } = new();

    private SystemJobLeaseClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

internal sealed class SystemJobLeaseRuntime : IJobLeaseRuntime
{
    private SystemJobLeaseRuntime(
        string machineName,
        int processId,
        string instanceId,
        DateTimeOffset processStartedAtUtc,
        string appVersion)
    {
        MachineName = machineName;
        ProcessId = processId;
        InstanceId = instanceId;
        ProcessStartedAtUtc = processStartedAtUtc;
        AppVersion = appVersion;
    }

    public string MachineName { get; }
    public int ProcessId { get; }
    public string InstanceId { get; }
    public DateTimeOffset ProcessStartedAtUtc { get; }
    public string AppVersion { get; }

    public static SystemJobLeaseRuntime Create(IJobLeaseClock clock)
    {
        DateTimeOffset startedAt;
        try
        {
            using Process process = Process.GetCurrentProcess();
            startedAt = process.StartTime.ToUniversalTime();
        }
        catch
        {
            startedAt = clock.UtcNow;
        }

        return new SystemJobLeaseRuntime(
            Environment.MachineName,
            Environment.ProcessId,
            Guid.NewGuid().ToString("N"),
            startedAt,
            global::OurPlanCore.AppVersion.Display);
    }
}

internal sealed class SystemJobLeaseProcessProbe : IJobLeaseProcessProbe
{
    public static SystemJobLeaseProcessProbe Instance { get; } = new();

    private SystemJobLeaseProcessProbe()
    {
    }

    public JobLeaseProcessProbeResult Probe(int processId)
    {
        if (processId <= 0)
            return new JobLeaseProcessProbeResult(JobLeaseProcessState.NotRunning);

        try
        {
            using Process process = Process.GetProcessById(processId);
            if (process.HasExited)
                return new JobLeaseProcessProbeResult(JobLeaseProcessState.NotRunning);
            return new JobLeaseProcessProbeResult(
                JobLeaseProcessState.Running,
                process.StartTime.ToUniversalTime());
        }
        catch (ArgumentException)
        {
            return new JobLeaseProcessProbeResult(JobLeaseProcessState.NotRunning);
        }
        catch (InvalidOperationException)
        {
            return new JobLeaseProcessProbeResult(JobLeaseProcessState.NotRunning);
        }
        catch (Exception ex) when (ex is Win32Exception or UnauthorizedAccessException or NotSupportedException)
        {
            return new JobLeaseProcessProbeResult(JobLeaseProcessState.Unknown);
        }
    }
}

internal sealed class TimerJobLeaseScheduler : IJobLeaseScheduler
{
    private readonly object _gate = new();
    private Timer? _timer;
    private bool _disposed;

    public void Schedule(TimeSpan delay, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _timer?.Dispose();
            _timer = new Timer(_ => callback(), null, delay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
