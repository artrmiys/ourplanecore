internal static class JobLeaseIntegrationRegressionTests
{
    public static void LegacyRecoveryLockIsNotUsedByMainWindow()
    {
        string source = ReadMainWindowSources();

        AssertFalse(
            source.Contains("QueueOpenedJobRecovery(", StringComparison.Ordinal),
            "MainWindow must not queue the legacy notification-only recovery lock");
        AssertFalse(
            source.Contains("JobRecoveryService.WriteLock(", StringComparison.Ordinal),
            "MainWindow must not write the legacy non-exclusive recovery lock");
    }

    public static void LeaseHeartbeatUsesDurableAtomicWrite()
    {
        string store = File.ReadAllText(RepoFile("Models/JobLeaseFileStore.cs"));
        string io = File.ReadAllText(RepoFile("Models/IoUtil.cs"));

        AssertTrue(
            store.Contains("IoUtil.WriteStreamAtomic(", StringComparison.Ordinal),
            "job lease writes must use the durable atomic stream path");
        AssertFalse(
            store.Contains("IoUtil.WriteAllTextAtomic(LeasePath(jobRoot)", StringComparison.Ordinal),
            "job lease writes must not use the non-durable text path");
        AssertTrue(
            io.Contains("output.Flush(flushToDisk: true);", StringComparison.Ordinal),
            "the durable atomic stream path must flush the lease to disk before replacement");
    }

    public static void WindowCloseReleasesCurrentJobAccess()
    {
        string source = File.ReadAllText(RepoFile("MainWindow.WindowBounds.cs"));
        int onClosed = source.IndexOf("protected override void OnClosed(EventArgs e)", StringComparison.Ordinal);
        int release = source.IndexOf(
            "RunCloseCleanup(CloseCurrentJobAccess, \"release job lease\")",
            onClosed,
            StringComparison.Ordinal);
        int baseClose = source.IndexOf("base.OnClosed(e);", release, StringComparison.Ordinal);

        AssertTrue(onClosed >= 0, "OnClosed must exist");
        AssertTrue(release > onClosed, "OnClosed must release the exact current job access session");
        AssertTrue(baseClose > release, "job access must be released before base close completes");
    }

    public static void ReadOnlyWindowCloseSkipsJobSaves()
    {
        string source = File.ReadAllText(RepoFile("MainWindow.WindowBounds.cs"));
        int onClosing = source.IndexOf("protected override void OnClosing(CancelEventArgs e)", StringComparison.Ordinal);
        int readOnlyExit = source.IndexOf(
            "if (IsCurrentJobReadOnly && !PrepareReadOnlyJobForExit(\"close OurPlanCore\"))",
            onClosing,
            StringComparison.Ordinal);
        int flushGuard = source.IndexOf(
            "if (!IsCurrentJobReadOnly && !FlushTakeoffAutosavesBeforeClose())",
            readOnlyExit,
            StringComparison.Ordinal);
        int pageSaveGuard = source.IndexOf(
            "if (!IsCurrentJobReadOnly && !SaveCurrentPageStateBeforeClose())",
            flushGuard,
            StringComparison.Ordinal);

        AssertTrue(onClosing >= 0 && readOnlyExit > onClosing, "read-only close path must be explicit");
        AssertTrue(flushGuard > readOnlyExit, "read-only close must skip final takeoff persistence");
        AssertTrue(pageSaveGuard > flushGuard, "read-only close must skip scale and annotation persistence");
    }

    public static void ReadOnlyBookmarksBlockMutationsButKeepOpenAvailable()
    {
        string controller = File.ReadAllText(RepoFile("PageBookmarksController.cs"));
        string shell = File.ReadAllText(RepoFile("MainWindow.Bookmarks.cs"));

        int addMethod = controller.IndexOf("private void AddCurrentPageBookmark(bool promptForName)", StringComparison.Ordinal);
        int addGuard = controller.IndexOf("EnsureCanModifyBookmarks(job, \"add a bookmark\")", addMethod, StringComparison.Ordinal);
        int addMutation = controller.IndexOf("_pageBookmarks.Add(bookmark);", addMethod, StringComparison.Ordinal);
        AssertTrue(addMethod >= 0 && addGuard > addMethod && addMutation > addGuard,
            "bookmark add must check writable access before changing the in-memory list");

        int renameMethod = controller.IndexOf("private void BtnBookmarkRename_Click", StringComparison.Ordinal);
        int renameGuard = controller.IndexOf("EnsureCanModifyBookmarks(job, \"rename a bookmark\")", renameMethod, StringComparison.Ordinal);
        int renameMutation = controller.IndexOf("bookmark.Name = dialog.BookmarkName;", renameMethod, StringComparison.Ordinal);
        AssertTrue(renameMethod >= 0 && renameGuard > renameMethod && renameMutation > renameGuard,
            "bookmark rename must check writable access before changing the bookmark");

        int deleteMethod = controller.IndexOf("private void DeleteSelectedBookmark()", StringComparison.Ordinal);
        int deleteGuard = controller.IndexOf("EnsureCanModifyBookmarks(job, \"delete a bookmark\")", deleteMethod, StringComparison.Ordinal);
        int deleteMutation = controller.IndexOf("_pageBookmarks.Remove(bookmark);", deleteMethod, StringComparison.Ordinal);
        AssertTrue(deleteMethod >= 0 && deleteGuard > deleteMethod && deleteMutation > deleteGuard,
            "bookmark delete must check writable access before deleting files or changing the list");

        AssertTrue(
            controller.Contains("JobWriteAccess.Demand(outputPath, \"save a bookmark crop image\")", StringComparison.Ordinal) &&
            controller.Contains("JobWriteAccess.Demand(path, \"delete a bookmark crop image\")", StringComparison.Ordinal),
            "bookmark crop file writes and deletes must cross the job write boundary");
        AssertTrue(
            controller.Contains("_bookmarkAddButton.IsEnabled = canWrite;", StringComparison.Ordinal) &&
            controller.Contains("_bookmarkRenameButton.IsEnabled = canWrite && hasSelection;", StringComparison.Ordinal) &&
            controller.Contains("_bookmarkDeleteButton.IsEnabled = canWrite && hasSelection;", StringComparison.Ordinal) &&
            controller.Contains("_bookmarkOpenButton.IsEnabled = hasSelection;", StringComparison.Ordinal),
            "read-only state must disable bookmark mutations without disabling open/view");
        AssertTrue(
            shell.Contains("canWriteCurrentJob: () => IsCurrentJobWritable", StringComparison.Ordinal) &&
            shell.Contains("ApplyBookmarksJobAccessState", StringComparison.Ordinal) &&
            shell.Contains("_bookmarksController?.ApplyJobAccessState();", StringComparison.Ordinal),
            "MainWindow must expose current lease access to the bookmarks controller and refresh it on demand");
    }

    private static string ReadMainWindowSources() =>
        string.Join(
            "\n",
            Directory.EnumerateFiles(RepoRoot(), "MainWindow*.cs", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(File.ReadAllText));

    private static string RepoFile(string relativePath) => Path.Combine(RepoRoot(), relativePath);

    private static string RepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ourplancore.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("OurPlanCore repository root was not found.");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message) => AssertTrue(!condition, message);
}
