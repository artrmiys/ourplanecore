internal static class AutosaveLifecycleRegressionTests
{
    public static void ExplicitFlushFailureStopsCaller()
    {
        string source = File.ReadAllText(RepoFile("MainWindow.MeasurementClipboard.cs"));
        int flush = source.IndexOf("private TakeoffFlushResult FlushTakeoffAutosaves()", StringComparison.Ordinal);
        int resultCheck = source.IndexOf("if (!result.Success)", flush, StringComparison.Ordinal);
        int exception = source.IndexOf("throw new IOException", resultCheck, StringComparison.Ordinal);

        AssertTrue(flush >= 0, "explicit autosave wrapper must return a typed result");
        AssertTrue(resultCheck > flush, "explicit autosave wrapper must inspect failure");
        AssertTrue(exception > resultCheck, "explicit autosave failure must stop destructive callers");
    }

    public static void ReloadFlushesBeforeReplacingTakeoffInstances()
    {
        string source = File.ReadAllText(RepoFile("MainWindow.JobLifecycle.cs"));
        int load = source.IndexOf("private void LoadTakeoffsForJob()", StringComparison.Ordinal);
        int flush = source.IndexOf("TryFlushTakeoffAutosaves(\"reload the Takeoffs tree\")", load, StringComparison.Ordinal);
        int replace = source.IndexOf("_takeoffItems.Clear();", flush, StringComparison.Ordinal);

        AssertTrue(load >= 0, "takeoffs reload method must exist");
        AssertTrue(flush > load, "takeoffs reload must flush pending objects");
        AssertTrue(replace > flush, "flush guard must run before takeoff instances are replaced");
    }

    public static void JobSwitchStopsWhenCurrentJobCannotFlush()
    {
        string recovery = File.ReadAllText(RepoFile("MainWindow.JobRecovery.cs"));
        string lifecycle = File.ReadAllText(RepoFile("MainWindow.JobLifecycle.cs"));
        int loadTarget = lifecycle.IndexOf("OurPlanCoreJob nextJob = OurPlanCoreJobStore.LoadJob(rootPath);", StringComparison.Ordinal);
        int prepare = lifecycle.IndexOf("if (!PrepareCurrentJobForSwitch())", loadTarget, StringComparison.Ordinal);
        int assign = lifecycle.IndexOf("_currentJob = nextJob;", prepare, StringComparison.Ordinal);

        AssertTrue(
            recovery.Contains("private bool PrepareCurrentJobForSwitch()", StringComparison.Ordinal) &&
            recovery.Contains("if (!TryFlushTakeoffAutosaves(\"switch jobs\"))", StringComparison.Ordinal) &&
            recovery.Contains("return false;", StringComparison.Ordinal),
            "job-switch preparation must be cancellable on autosave failure");
        AssertTrue(loadTarget >= 0 && prepare > loadTarget, "target job must be validated before closing the current job");
        AssertTrue(assign > prepare, "current job assignment must occur only after safe switch preparation");
    }

    public static void WindowCloseIsCanceledWhileTakeoffsRemainPending()
    {
        string bounds = File.ReadAllText(RepoFile("MainWindow.WindowBounds.cs"));
        string shell = File.ReadAllText(RepoFile("MainWindow.xaml.cs"));
        int closing = bounds.IndexOf("protected override void OnClosing(CancelEventArgs e)", StringComparison.Ordinal);
        int flush = bounds.IndexOf("FlushTakeoffAutosavesBeforeClose()", closing, StringComparison.Ordinal);
        int cancel = bounds.IndexOf("e.Cancel = true;", flush, StringComparison.Ordinal);

        AssertTrue(closing >= 0 && flush > closing && cancel > flush, "OnClosing must cancel after failed final flush");
        AssertTrue(bounds.Contains("_takeoffSaveService.Stop();", StringComparison.Ordinal), "OnClosed must stop the autosave scheduler");
        AssertFalse(shell.Contains("FlushTakeoffAutosaves();", StringComparison.Ordinal), "OnClosed must not perform the first final flush");
    }

    private static string RepoFile(string relativePath)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate) && File.Exists(Path.Combine(current.FullName, "ourplancore.csproj")))
                return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException($"Repository file not found: {relativePath}");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message) => AssertTrue(!condition, message);
}
