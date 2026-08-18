using System;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace OurPlanCore;

public partial class MainWindow
{
    private void QueueAiContextMaintenance(OurPlanCoreJob job)
    {
        string root = job.RootPath;
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (_currentJob == null || !SameJobPath(_currentJob.RootPath, root) ||
                    !IsCurrentJobWritable || !IsModuleEnabled(ModuleId.Ai))
                {
                    return;
                }
                _aiContextMaintenanceTask = Task.Run(() => RunAiContextMaintenance(root));
            }),
            DispatcherPriority.ContextIdle);
    }

    private static void RunAiContextMaintenance(string root)
    {
        using IDisposable? writeActivity = JobFileWriteActivity.TryBeginBackgroundWriteForProjectPath(
            root,
            TimeSpan.FromSeconds(10));
        if (writeActivity == null)
            return;
        try
        {
            int reset = SmartContextStore.ResetStuckRunningRequests(root);
            if (reset > 0)
                AppLog.Info($"AI context maintenance: reset {reset} stuck request(s) to failed.");
            (int archived, int failed) = SmartContextStore.ArchiveStaleRequestFiles(root);
            if (archived > 0 || failed > 0)
            {
                AppLog.Info(
                    $"AI context maintenance: archived {archived} stale request/response file(s), {failed} failed.");
            }
            int prunedCrops = SmartContextStore.PruneOrphanCrops(root);
            if (prunedCrops > 0)
                AppLog.Info($"AI context maintenance: pruned {prunedCrops} orphaned crop image(s).");
        }
        catch (JobWriteDeniedException ex)
        {
            AppLog.Info($"AI context maintenance stopped after write access changed: {ex.Message}");
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, "AI context maintenance failed.");
        }
    }
}
