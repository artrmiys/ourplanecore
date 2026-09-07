using System.IO;
using System.Windows.Threading;

namespace OurPlanCore;

public partial class MainWindow
{
    private string ProjectDataPathForDisplay(string path)
    {
        if (!HasCurrentPackageSession ||
            string.IsNullOrWhiteSpace(path) ||
            !OurPlanCoreJobStore.IsSameOrDescendant(_currentPackageSession!.WorkspaceRoot, path))
        {
            return path;
        }

        string relative = Path.GetRelativePath(_currentPackageSession.WorkspaceRoot, path);
        return $"{relative} (inside {Path.GetFileName(_currentPackageSession.PackagePath)})";
    }

    private static string FormatPackageBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    private T RunResponsivePackageOperation<T>(Func<T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (Dispatcher.CheckAccess())
            SupersedeAutomaticPackageCheckpoint();
        if (Interlocked.CompareExchange(ref _packageOperationActive, 1, 0) != 0)
            throw new IOException("Another OurPlan project package operation is already running.");
        if (!Dispatcher.CheckAccess())
        {
            try
            {
                return operation();
            }
            finally
            {
                Interlocked.Exchange(ref _packageOperationActive, 0);
            }
        }

        bool wasEnabled = IsEnabled;
        IsEnabled = false;
        var frame = new DispatcherFrame();
        Task<T> task = Task.Run(operation);
        _ = task.ContinueWith(
            _ => Dispatcher.BeginInvoke(
                DispatcherPriority.Send,
                new Action(() => frame.Continue = false)),
            TaskScheduler.Default);
        try
        {
            Dispatcher.PushFrame(frame);
            return task.GetAwaiter().GetResult();
        }
        finally
        {
            IsEnabled = wasEnabled;
            Interlocked.Exchange(ref _packageOperationActive, 0);
        }
    }
}
