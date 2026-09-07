using OurPlanCore;

internal static class OurPlanPackageAutosaveTests
{
    public static void ScheduleCoalescesEventsAndCapsDirtyAge()
    {
        DateTime dirtySince = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);
        DateTime firstDue = OurPlanPackageAutosaveSchedule.CalculateDueUtc(
            dirtySince,
            dirtySince,
            DateTime.MinValue,
            waitForQuietPeriod: true,
            retryDelay: TimeSpan.Zero);
        DateTime laterEvent = dirtySince + TimeSpan.FromSeconds(5);
        DateTime coalescedDue = OurPlanPackageAutosaveSchedule.CalculateDueUtc(
            laterEvent,
            dirtySince,
            DateTime.MinValue,
            waitForQuietPeriod: true,
            retryDelay: TimeSpan.Zero);
        DateTime nearMaximumAge = dirtySince + OurPlanPackageAutosaveSchedule.MaximumDirtyAge -
                                  TimeSpan.FromSeconds(1);
        DateTime cappedDue = OurPlanPackageAutosaveSchedule.CalculateDueUtc(
            nearMaximumAge,
            dirtySince,
            DateTime.MinValue,
            waitForQuietPeriod: true,
            retryDelay: TimeSpan.Zero);

        AssertEqual(dirtySince + OurPlanPackageAutosaveSchedule.QuietPeriod, firstDue, "initial quiet due");
        AssertEqual(laterEvent + OurPlanPackageAutosaveSchedule.QuietPeriod, coalescedDue, "trailing quiet due");
        AssertEqual(dirtySince + OurPlanPackageAutosaveSchedule.MaximumDirtyAge, cappedDue, "maximum dirty age");
    }

    public static void ScheduleLimitsSuccessfulCheckpointFrequencyButRetriesPromptly()
    {
        DateTime lastStarted = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);
        DateTime now = lastStarted + TimeSpan.FromSeconds(20);
        DateTime dirtySince = now;
        DateTime normalDue = OurPlanPackageAutosaveSchedule.CalculateDueUtc(
            now,
            dirtySince,
            lastStarted,
            waitForQuietPeriod: true,
            retryDelay: TimeSpan.Zero);
        TimeSpan retryDelay = OurPlanPackageAutosaveSchedule.FailureRetryDelay(1);
        DateTime retryDue = OurPlanPackageAutosaveSchedule.CalculateDueUtc(
            now,
            dirtySince,
            lastStarted,
            waitForQuietPeriod: false,
            retryDelay);

        AssertEqual(
            lastStarted + OurPlanPackageAutosaveSchedule.MinimumCheckpointInterval,
            normalDue,
            "minimum checkpoint interval");
        AssertEqual(now + retryDelay, retryDue, "failure retry bypasses successful-save interval");
        AssertEqual(TimeSpan.FromSeconds(5), OurPlanPackageAutosaveSchedule.FailureRetryDelay(1), "retry 1");
        AssertEqual(TimeSpan.FromSeconds(15), OurPlanPackageAutosaveSchedule.FailureRetryDelay(2), "retry 2");
        AssertEqual(TimeSpan.FromSeconds(30), OurPlanPackageAutosaveSchedule.FailureRetryDelay(3), "retry 3");
        AssertEqual(TimeSpan.FromMinutes(1), OurPlanPackageAutosaveSchedule.FailureRetryDelay(9), "retry cap");
    }

    public static void MaximumDirtyAgeOverridesRecentCheckpointInterval()
    {
        DateTime dirtySince = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);
        DateTime now = dirtySince + TimeSpan.FromSeconds(115);
        DateTime lastStarted = dirtySince + TimeSpan.FromSeconds(100);

        DateTime due = OurPlanPackageAutosaveSchedule.CalculateDueUtc(
            now,
            dirtySince,
            lastStarted,
            waitForQuietPeriod: true,
            retryDelay: TimeSpan.Zero);

        AssertEqual(
            dirtySince + OurPlanPackageAutosaveSchedule.MaximumDirtyAge,
            due,
            "hard maximum dirty age");
    }

    public static void FailurePolicyRetriesOnlyExplicitTransientErrors()
    {
        AssertTrue(
            MainWindow.ShouldRetryAutomaticPackageCheckpoint(
                new OurPlanPackageTransientException("workspace changed")),
            "explicit transient package failure");
        AssertTrue(
            MainWindow.ShouldRetryAutomaticPackageCheckpoint(
                new IOException("sharing violation", unchecked((int)0x80070020))),
            "sharing violation");
        AssertFalse(
            MainWindow.ShouldRetryAutomaticPackageCheckpoint(
                new OurPlanPackageValidationException("invalid project")),
            "validation failure");
        AssertFalse(
            MainWindow.ShouldRetryAutomaticPackageCheckpoint(
                new UnauthorizedAccessException("access denied")),
            "access failure");
        AssertFalse(
            MainWindow.ShouldRetryAutomaticPackageCheckpoint(
                new IOException("disk full", unchecked((int)0x80070070))),
            "disk full failure");
    }

    public static void SuccessfulSamePathSavePromotesRecoverySession()
    {
        var recovery = new OurPlanPackageRecoveryInfo(
            "project.ourplan",
            "working",
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid().ToString("N"),
            "Recovered",
            OurPlanRecoveryKind.UnsavedChanges,
            DateTime.UtcNow);
        var session = new OurPlanPackageSession
        {
            PackagePath = recovery.PackagePath,
            WorkspaceRoot = recovery.WorkspaceRoot,
            ProjectId = recovery.ProjectId,
            DisplayName = recovery.DisplayName,
            BaseRevisionId = recovery.BaseRevisionId,
            BaseFingerprint = default,
            IsRecoverySession = true,
            RecoveryReason = "unsaved changes",
            AvailableRecoverySessions = [recovery],
        };

        MainWindow.PromoteRecoveredPackageSessionAfterSamePathSave(session);

        AssertFalse(session.IsRecoverySession, "recovery flag after same-path save");
        AssertEqual("", session.RecoveryReason, "recovery reason after same-path save");
        AssertEqual(0, session.AvailableRecoverySessions.Count, "recovery candidates after same-path save");
    }

    public static void PackageCheckpointActivityIsVisibleToAutosavePreflight()
    {
        AssertFalse(JobFileWriteActivity.HasActivePackageCheckpoints, "initial checkpoint state");
        using (JobFileWriteActivity.BeginPackageCheckpoint())
        {
            AssertTrue(JobFileWriteActivity.HasActivePackageCheckpoints, "outer checkpoint state");
            using (JobFileWriteActivity.BeginPackageCheckpoint())
                AssertTrue(JobFileWriteActivity.HasActivePackageCheckpoints, "nested checkpoint state");
            AssertTrue(JobFileWriteActivity.HasActivePackageCheckpoints, "outer checkpoint remains active");
        }
        AssertFalse(JobFileWriteActivity.HasActivePackageCheckpoints, "released checkpoint state");
    }

    public static void WorkspaceChangesWireToSilentSamePathAutosave()
    {
        string watcher = File.ReadAllText(RepoFile("MainWindow.ProjectPackage.Watchers.cs"));
        string autosave = File.ReadAllText(RepoFile("MainWindow.ProjectPackage.Autosave.cs"));
        string close = File.ReadAllText(RepoFile("MainWindow.WindowBounds.cs"));
        string switchJob = File.ReadAllText(RepoFile("MainWindow.JobRecovery.cs"));
        string manual = File.ReadAllText(RepoFile("MainWindow.TakeoffsPersistence.cs"));

        AssertTrue(
            watcher.Contains("ScheduleAutomaticPackageCheckpoint(", StringComparison.Ordinal),
            "workspace watcher does not schedule a package checkpoint");
        AssertFalse(
            watcher.Contains("Press Ctrl+S to update the .ourplan", StringComparison.Ordinal),
            "workspace watcher still describes manual-only package saving");
        AssertTrue(
            autosave.Contains("OurPlanPackageWriter.Save(session)", StringComparison.Ordinal) &&
            autosave.Contains("PackageArtifactStillMatchesSession(session)", StringComparison.Ordinal) &&
            autosave.Contains("generation == Interlocked.Read", StringComparison.Ordinal),
            "automatic checkpoint lacks same-path save or generation validation");
        AssertFalse(
            autosave.Contains("SaveAs", StringComparison.Ordinal) ||
            autosave.Contains("MessageBox.Show", StringComparison.Ordinal),
            "automatic checkpoint must not create another project or show a modal dialog");
        AssertTrue(
            close.Contains("SupersedeAutomaticPackageCheckpoint();", StringComparison.Ordinal) &&
            switchJob.Contains("SupersedeAutomaticPackageCheckpoint();", StringComparison.Ordinal) &&
            manual.Contains("SupersedeAutomaticPackageCheckpoint();", StringComparison.Ordinal),
            "foreground lifecycle operations do not supersede automatic package saving");
        AssertTrue(
            autosave.Contains("_packageAutosaveScheduleEpoch", StringComparison.Ordinal) &&
            autosave.Contains("disableWindow", StringComparison.Ordinal) &&
            autosave.Contains("_packageAutosaveWaitActive", StringComparison.Ordinal),
            "foreground supersede lacks stale-callback and reentrancy protection");
    }

    public static void PriorPackageRollbackSurvivesValidationAndBaseCommit()
    {
        string writer = File.ReadAllText(RepoFile(Path.Combine("Models", "OurPlanPackageWriter.cs")));
        int publish = writer.IndexOf("private static PublishOutcome Publish", StringComparison.Ordinal);
        int replace = writer.IndexOf("ReplaceAtomically(", publish, StringComparison.Ordinal);
        int validate = writer.IndexOf("ValidatePublishedTarget(", replace, StringComparison.Ordinal);
        int outcome = writer.IndexOf("rollbackPath);", validate, StringComparison.Ordinal);
        AssertTrue(
            publish >= 0 && replace > publish && validate > replace && outcome > validate,
            "publish does not carry rollback through final target validation");

        int save = writer.IndexOf("public static OurPlanPackageSaveResult Save", StringComparison.Ordinal);
        int persist = writer.IndexOf("PersistSavedBase(", save, StringComparison.Ordinal);
        int delete = writer.IndexOf("DeletePublishedRollback(outcome.RollbackPath)", persist, StringComparison.Ordinal);
        AssertTrue(
            save >= 0 && persist > save && delete > persist,
            "prior package rollback is deleted before the saved base is committed");
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

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected={expected}, actual={actual}");
    }
}
