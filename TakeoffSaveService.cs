using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;

namespace OurPlanCore;

internal interface ITakeoffSaveService
{
    bool HasPending { get; }
    DateTime? LastFlushUtc { get; }
    event Action? DirtyStateChanged;
    void MarkDirty(TakeoffItem item);
    void MarkDirty(IEnumerable<TakeoffItem> items);
    void Flush();
}

internal sealed class TakeoffSaveService : ITakeoffSaveService
{
    private readonly Func<OurPlanCoreJob?> _currentJob;
    private readonly Action<string> _setStatus;
    private readonly HashSet<TakeoffItem> _pending = [];
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromMilliseconds(ViewportConstants.AutosaveDebounceMs),
    };

    public TakeoffSaveService(Func<OurPlanCoreJob?> currentJob, Action<string> setStatus)
    {
        _currentJob = currentJob;
        _setStatus = setStatus;
        _timer.Tick += (_, _) => Flush();
    }

    public bool HasPending => _pending.Count > 0;
    public DateTime? LastFlushUtc { get; private set; }
    public event Action? DirtyStateChanged;

    public void MarkDirty(TakeoffItem item)
    {
        if (_currentJob() == null)
            return;

        bool wasPending = HasPending;
        _pending.Add(item);
        _timer.Stop();
        _timer.Start();
        if (!wasPending)
            DirtyStateChanged?.Invoke();
    }

    public void MarkDirty(IEnumerable<TakeoffItem> items)
    {
        if (_currentJob() == null)
            return;

        bool wasPending = HasPending;
        foreach (TakeoffItem item in items)
            _pending.Add(item);

        if (_pending.Count == 0)
            return;

        _timer.Stop();
        _timer.Start();
        if (!wasPending)
            DirtyStateChanged?.Invoke();
    }

    public void Flush()
    {
        _timer.Stop();
        if (_pending.Count == 0)
            return;

        var pending = _pending.ToList();
        _pending.Clear();
        DirtyStateChanged?.Invoke();

        foreach (TakeoffItem item in pending)
            PersistQuietly(item);

        LastFlushUtc = DateTime.UtcNow;
        DirtyStateChanged?.Invoke();
    }

    private void PersistQuietly(TakeoffItem item)
    {
        if (_currentJob() == null)
            return;

        // A deleted item can still sit in the pending-autosave set; recreating
        // its folder here would resurrect it as a ghost next to the undo trash.
        // Silent autosave only writes into folders that still exist.
        if (string.IsNullOrWhiteSpace(item.FolderPath) || !Directory.Exists(item.FolderPath))
        {
            AppLog.Info($"Autosave skipped for missing takeoff folder: {item.FolderPath}");
            return;
        }

        try
        {
            OurPlanCoreJobStore.SaveTakeoffItem(item);
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Autosave failed for {item.FolderPath}");
            _setStatus($"Autosave skipped: {ex.Message}");
        }
    }
}
