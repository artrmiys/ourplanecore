using System.IO;

namespace OurPlanCore;

internal static class JobFileWriteActivity
{
    private static readonly object Gate = new();
    private static int _activeBackgroundWriters;
    private static int _packageCheckpoints;
    private static string _currentJobRoot = "";

    public static IDisposable? TryBeginBackgroundWriteForProjectPath(
        string projectPath,
        TimeSpan? wait = null)
    {
        string path;
        try
        {
            path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectPath));
        }
        catch
        {
            return null;
        }

        lock (Gate)
        {
            DateTime deadline = DateTime.UtcNow + (wait ?? TimeSpan.Zero);
            while (_packageCheckpoints > 0)
            {
                TimeSpan remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero || !Monitor.Wait(Gate, remaining))
                    return null;
            }
            if (string.IsNullOrWhiteSpace(_currentJobRoot) ||
                !(path.Equals(_currentJobRoot, StringComparison.OrdinalIgnoreCase) ||
                  path.StartsWith(_currentJobRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }
            _activeBackgroundWriters++;
            return new Scope(backgroundWriter: true);
        }
    }

    public static void SetCurrentJobRoot(string? jobRoot)
    {
        string normalized = "";
        if (!string.IsNullOrWhiteSpace(jobRoot))
        {
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(jobRoot));
        }
        lock (Gate)
        {
            _currentJobRoot = normalized;
            Monitor.PulseAll(Gate);
        }
    }

    public static PackageCheckpointScope BeginPackageCheckpoint()
    {
        lock (Gate)
        {
            _packageCheckpoints++;
            return new PackageCheckpointScope(_activeBackgroundWriters > 0);
        }
    }

    public static bool HasActiveBackgroundWriters
    {
        get
        {
            lock (Gate)
                return _activeBackgroundWriters > 0;
        }
    }

    public static bool HasActivePackageCheckpoints
    {
        get
        {
            lock (Gate)
                return _packageCheckpoints > 0;
        }
    }

    public sealed class PackageCheckpointScope : IDisposable
    {
        private int _disposed;

        internal PackageCheckpointScope(bool hadActiveWriters) =>
            HadActiveWriters = hadActiveWriters;

        public bool HadActiveWriters { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            lock (Gate)
            {
                _packageCheckpoints--;
                Monitor.PulseAll(Gate);
            }
        }
    }

    private sealed class Scope(bool backgroundWriter) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            if (!backgroundWriter)
                return;
            lock (Gate)
                _activeBackgroundWriters--;
        }
    }
}
