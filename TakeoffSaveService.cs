using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;

namespace OurPlanCore;

internal enum TakeoffSaveState
{
    Clean,
    Dirty,
    Saving,
    Failed,
}

internal readonly record struct TakeoffFlushResult(
    int Attempted,
    int Saved,
    int Failed,
    int DroppedMissing,
    string Error)
{
    public bool Success => Failed == 0;
}

internal interface ITakeoffSaveScheduler
{
    void Schedule(TimeSpan delay, Action callback);
    void Stop();
}

internal sealed class DispatcherTakeoffSaveScheduler : ITakeoffSaveScheduler
{
    private readonly DispatcherTimer _timer = new();
    private Action? _callback;

    public DispatcherTakeoffSaveScheduler()
    {
        _timer.Tick += OnTick;
    }

    public void Schedule(TimeSpan delay, Action callback)
    {
        _timer.Stop();
        _timer.Interval = delay;
        _callback = callback;
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _callback = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        Action? callback = _callback;
        _callback = null;
        callback?.Invoke();
    }
}

internal interface ITakeoffSaveService
{
    bool HasPending { get; }
    int PendingCount { get; }
    TakeoffSaveState State { get; }
    DateTime? LastAttemptUtc { get; }
    DateTime? LastSuccessfulFlushUtc { get; }
    DateTime? LastFlushUtc { get; }
    string? LastError { get; }
    event Action? DirtyStateChanged;
    void MarkDirty(TakeoffItem item);
    void MarkDirty(IEnumerable<TakeoffItem> items);
    TakeoffFlushResult Flush();
    int DiscardUnavailableItems();
    void Stop();
}

internal sealed class TakeoffSaveService : ITakeoffSaveService
{
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly Func<OurPlanCoreJob?> _currentJob;
    private readonly Action<string> _setStatus;
    private readonly Action<TakeoffItem> _writeItem;
    private readonly Func<string, bool> _folderExists;
    private readonly Func<DateTime> _utcNow;
    private readonly ITakeoffSaveScheduler _scheduler;
    private readonly Dictionary<TakeoffItem, string> _pending = [];

    public TakeoffSaveService(Func<OurPlanCoreJob?> currentJob, Action<string> setStatus)
        : this(
            currentJob,
            setStatus,
            OurPlanCoreJobStore.SaveTakeoffItem,
            Directory.Exists,
            () => DateTime.UtcNow,
            new DispatcherTakeoffSaveScheduler())
    {
    }

    internal TakeoffSaveService(
        Func<OurPlanCoreJob?> currentJob,
        Action<string> setStatus,
        Action<TakeoffItem> writeItem,
        Func<string, bool> folderExists,
        Func<DateTime> utcNow,
        ITakeoffSaveScheduler scheduler)
    {
        _currentJob = currentJob ?? throw new ArgumentNullException(nameof(currentJob));
        _setStatus = setStatus ?? throw new ArgumentNullException(nameof(setStatus));
        _writeItem = writeItem ?? throw new ArgumentNullException(nameof(writeItem));
        _folderExists = folderExists ?? throw new ArgumentNullException(nameof(folderExists));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
    }

    public bool HasPending => _pending.Count > 0;
    public int PendingCount => _pending.Count;
    public TakeoffSaveState State { get; private set; } = TakeoffSaveState.Clean;
    public DateTime? LastAttemptUtc { get; private set; }
    public DateTime? LastSuccessfulFlushUtc { get; private set; }
    public DateTime? LastFlushUtc => LastSuccessfulFlushUtc;
    public string? LastError { get; private set; }
    public event Action? DirtyStateChanged;

    public void MarkDirty(TakeoffItem item)
    {
        OurPlanCoreJob? currentJob = _currentJob();
        if (currentJob == null)
            return;

        bool added = !_pending.ContainsKey(item);
        _pending[item] = NormalizeRoot(currentJob.RootPath);
        Schedule(ViewportConstants.AutosaveDebounceMs);
        if (State != TakeoffSaveState.Failed)
            State = TakeoffSaveState.Dirty;
        if (added)
            NotifyStateChanged();
    }

    public void MarkDirty(IEnumerable<TakeoffItem> items)
    {
        OurPlanCoreJob? currentJob = _currentJob();
        if (currentJob == null)
            return;

        int before = _pending.Count;
        foreach (TakeoffItem item in items)
            _pending[item] = NormalizeRoot(currentJob.RootPath);

        if (_pending.Count == 0)
            return;

        Schedule(ViewportConstants.AutosaveDebounceMs);
        if (State != TakeoffSaveState.Failed)
            State = TakeoffSaveState.Dirty;
        if (_pending.Count != before)
            NotifyStateChanged();
    }

    public TakeoffFlushResult Flush()
    {
        _scheduler.Stop();
        if (_pending.Count == 0)
            return new TakeoffFlushResult(0, 0, 0, 0, "");

        List<KeyValuePair<TakeoffItem, string>> pending = _pending.ToList();
        LastAttemptUtc = _utcNow();
        State = TakeoffSaveState.Saving;
        NotifyStateChanged();

        OurPlanCoreJob? currentJob;
        try
        {
            currentJob = _currentJob();
        }
        catch (Exception ex)
        {
            return CompleteFailedFlush(pending.Count, 0, ex.Message);
        }

        if (currentJob == null)
            return CompleteFailedFlush(pending.Count, 0, "No job is open for the pending autosave.");

        try
        {
            if (!_folderExists(currentJob.TakeoffsRoot))
                return CompleteFailedFlush(pending.Count, 0, "The current Takeoffs folder is unavailable.");
        }
        catch (Exception ex)
        {
            return CompleteFailedFlush(pending.Count, 0, ex.Message);
        }

        int saved = 0;
        int failed = 0;
        int droppedMissing = 0;
        var errors = new List<string>();
        string currentJobRoot = NormalizeRoot(currentJob.RootPath);
        foreach ((TakeoffItem item, string pendingJobRoot) in pending)
        {
            if (!string.Equals(pendingJobRoot, currentJobRoot, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Pending takeoff belongs to another job: {item.FolderPath}");
                failed++;
                continue;
            }

            if (!IsTakeoffPathInsideRoot(currentJob.TakeoffsRoot, item.FolderPath))
            {
                errors.Add($"Pending takeoff path is outside the current job: {item.FolderPath}");
                failed++;
                continue;
            }

            TakeoffPersistOutcome outcome = TryPersist(item, errors);
            switch (outcome)
            {
                case TakeoffPersistOutcome.Saved:
                    _pending.Remove(item);
                    saved++;
                    break;
                default:
                    failed++;
                    break;
            }
        }

        string error = errors.FirstOrDefault() ?? "";
        if (failed > 0)
            return CompleteFailedFlush(pending.Count, saved, error, droppedMissing, failed);

        if (_pending.Count == 0)
            LastSuccessfulFlushUtc = LastAttemptUtc;
        LastError = null;
        State = _pending.Count == 0 ? TakeoffSaveState.Clean : TakeoffSaveState.Dirty;
        NotifyStateChanged();
        return new TakeoffFlushResult(pending.Count, saved, 0, droppedMissing, "");
    }

    public void Stop() => _scheduler.Stop();

    public int DiscardUnavailableItems()
    {
        List<TakeoffItem> unavailable = _pending.Keys
            .Where(item => IsUnavailable(item.FolderPath))
            .ToList();
        foreach (TakeoffItem item in unavailable)
            _pending.Remove(item);

        if (unavailable.Count == 0)
            return 0;

        if (_pending.Count == 0)
        {
            _scheduler.Stop();
            State = TakeoffSaveState.Clean;
            LastError = null;
        }
        NotifyStateChanged();
        AppLog.Warn($"User explicitly discarded {unavailable.Count} pending takeoff autosave item(s) whose folders were unavailable.");
        return unavailable.Count;
    }

    private TakeoffPersistOutcome TryPersist(TakeoffItem item, List<string> errors)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(item.FolderPath) || !_folderExists(item.FolderPath))
            {
                string error = $"Takeoff folder is unavailable; autosave remains pending: {item.FolderPath}";
                AppLog.Warn(error);
                errors.Add(error);
                return TakeoffPersistOutcome.Failed;
            }

            _writeItem(item);
            if (!_folderExists(item.FolderPath))
                throw new IOException($"Takeoff folder disappeared during autosave: {item.FolderPath}");
            return TakeoffPersistOutcome.Saved;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Autosave failed for {item.FolderPath}");
            errors.Add(ex.Message);
            return TakeoffPersistOutcome.Failed;
        }
    }

    private TakeoffFlushResult CompleteFailedFlush(
        int attempted,
        int saved,
        string error,
        int droppedMissing = 0,
        int? failedOverride = null)
    {
        int failed = failedOverride ?? Math.Max(0, attempted - saved - droppedMissing);
        LastError = string.IsNullOrWhiteSpace(error) ? "Autosave write failed." : error;
        State = TakeoffSaveState.Failed;
        Schedule(RetryDelay.TotalMilliseconds);
        try
        {
            _setStatus($"Autosave failed; {PendingCount} item(s) remain unsaved. {LastError}");
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, "Autosave failure status callback failed.");
        }
        NotifyStateChanged();
        return new TakeoffFlushResult(attempted, saved, failed, droppedMissing, LastError);
    }

    private void Schedule(double milliseconds) =>
        _scheduler.Schedule(TimeSpan.FromMilliseconds(milliseconds), () => Flush());

    private void NotifyStateChanged()
    {
        try
        {
            DirtyStateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, "Autosave state callback failed.");
        }
    }

    private static string NormalizeRoot(string path)
    {
        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return (path ?? "").Trim()
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private bool IsUnavailable(string path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) || !_folderExists(path);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsTakeoffPathInsideRoot(string takeoffsRoot, string itemFolder)
    {
        if (string.IsNullOrWhiteSpace(takeoffsRoot) || string.IsNullOrWhiteSpace(itemFolder))
            return false;

        return OurPlanCoreJobStore.IsSameOrDescendant(takeoffsRoot, itemFolder) &&
               !string.Equals(
                   NormalizeRoot(takeoffsRoot),
                   NormalizeRoot(itemFolder),
                   StringComparison.OrdinalIgnoreCase);
    }

    private enum TakeoffPersistOutcome
    {
        Saved,
        Failed,
    }
}
