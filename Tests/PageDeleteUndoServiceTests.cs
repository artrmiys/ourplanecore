using OurPlanCore;

internal static class PageDeleteUndoServiceTests
{
    public static void RestoresPagePayloadSourceAndSiblingOrder()
    {
        WithTempJob(job =>
        {
            PageInfo first = OurPlanCoreJobStore.CreateBlankPage(job, "A101", job.PagesRoot);
            PageInfo deleted = OurPlanCoreJobStore.CreateBlankPage(job, "A102", job.PagesRoot);
            PageInfo last = OurPlanCoreJobStore.CreateBlankPage(job, "A103", job.PagesRoot);
            string payloadPath = Path.Combine(deleted.FolderPath, "annotations.json");
            File.WriteAllText(payloadPath, "{\"marker\":\"keep-me\"}");
            string[] originalOrder = OrderedPaths(job.PagesRoot);

            PageDeleteUndoBatch batch = PageDeleteUndoService.MoveToTrash(
                job,
                [new PageDeleteUndoRequest(deleted.FolderPath, true)]);

            AssertFalse(Directory.Exists(deleted.FolderPath), "deleted page should leave the Pages tree");
            AssertTrue(Directory.Exists(batch.Entries.Single().TrashPath), "deleted page should remain in undo trash");
            AssertTrue(Directory.Exists(first.FolderPath) && Directory.Exists(last.FolderPath), "sibling pages should remain untouched");
            OurPlanCoreJobStore.NormalizeOrder(job.PagesRoot);

            IReadOnlyList<PageDeleteRestoreEntry> restored = PageDeleteUndoService.Restore(job, batch);

            AssertEqual(deleted.FolderPath, restored.Single().RestoredPath, "page should return to its exact path");
            AssertTrue(File.Exists(payloadPath), "page payload should be restored");
            AssertEqual("{\"marker\":\"keep-me\"}", File.ReadAllText(payloadPath), "page payload should remain readable");
            PageInfo restoredPage = OurPlanCoreJobStore.TryReadPage(deleted.FolderPath)
                ?? throw new InvalidOperationException("restored page metadata should be readable");
            AssertTrue(File.Exists(restoredPage.PdfPath), "restored page source PDF should resolve");
            AssertSequence(originalOrder, OrderedPaths(job.PagesRoot), "restored page should return to its original position");
            AssertFalse(Directory.Exists(batch.TrashRoot), "successful restore should remove the empty batch trash folder");
        });
    }

    public static void RestoresDeletedFolderWithNestedPages()
    {
        WithTempJob(job =>
        {
            string building = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Building A");
            PageInfo nested = OurPlanCoreJobStore.CreateBlankPage(job, "S101", building);
            string nestedSource = nested.PdfPath;

            PageDeleteUndoBatch batch = PageDeleteUndoService.MoveToTrash(
                job,
                [new PageDeleteUndoRequest(building, false)]);
            AssertFalse(Directory.Exists(building), "deleted folder should leave the Pages tree");

            IReadOnlyList<PageDeleteRestoreEntry> restored = PageDeleteUndoService.Restore(job, batch);

            AssertEqual(building, restored.Single().RestoredPath, "folder should return to its exact path");
            PageInfo restoredNested = OurPlanCoreJobStore.TryReadPage(nested.FolderPath)
                ?? throw new InvalidOperationException("nested page should be restored with its folder");
            AssertEqual(nestedSource, restoredNested.PdfPath, "nested page should keep its source PDF binding");
            AssertTrue(File.Exists(restoredNested.PdfPath), "nested page PDF should still exist");
        });
    }

    public static void CollisionKeepsUndoDataAndDoesNotOverwrite()
    {
        WithTempJob(job =>
        {
            PageInfo deleted = OurPlanCoreJobStore.CreateBlankPage(job, "A201", job.PagesRoot);
            PageDeleteUndoBatch batch = PageDeleteUndoService.MoveToTrash(
                job,
                [new PageDeleteUndoRequest(deleted.FolderPath, true)]);

            Directory.CreateDirectory(deleted.FolderPath);
            string collisionMarker = Path.Combine(deleted.FolderPath, "do-not-overwrite.txt");
            File.WriteAllText(collisionMarker, "existing replacement");

            bool threw = false;
            try
            {
                PageDeleteUndoService.Restore(job, batch);
            }
            catch (IOException ex)
            {
                threw = ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase);
            }

            AssertTrue(threw, "restore should report an existing-path collision");
            AssertEqual("existing replacement", File.ReadAllText(collisionMarker), "restore must not overwrite the replacement item");
            AssertTrue(Directory.Exists(batch.Entries.Single().TrashPath), "undo data should remain available after a collision");
        });
    }

    public static void MissingParentFallsBackToPagesRootAndRewritesSource()
    {
        WithTempJob(job =>
        {
            string originalParent = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Temporary Folder");
            PageInfo deleted = OurPlanCoreJobStore.CreateBlankPage(job, "A301", originalParent);
            PageDeleteUndoBatch batch = PageDeleteUndoService.MoveToTrash(
                job,
                [new PageDeleteUndoRequest(deleted.FolderPath, true)]);
            Directory.Delete(originalParent, recursive: true);

            PageDeleteRestoreEntry restored = PageDeleteUndoService.Restore(job, batch).Single();

            AssertEqual(job.PagesRoot, Path.GetDirectoryName(restored.RestoredPath) ?? "", "missing parent should fall back to Pages root");
            PageInfo restoredPage = OurPlanCoreJobStore.TryReadPage(restored.RestoredPath)
                ?? throw new InvalidOperationException("fallback-restored page metadata should be readable");
            AssertTrue(File.Exists(restoredPage.PdfPath), "fallback restore should rewrite the relative PDF source path");
        });
    }

    public static void PagesUiUsesSingleUndoSlotAndScopedShortcut()
    {
        string root = RepoRoot();
        string undo = File.ReadAllText(Path.Combine(root, "MainWindow.PagesUndo.cs"));
        string actions = File.ReadAllText(Path.Combine(root, "MainWindow.PagesNodeActions.cs"));
        string commands = File.ReadAllText(Path.Combine(root, "MainWindow.PagesCommands.cs"));

        AssertTrue(
            undo.Contains("private PageDeleteUndoBatch? _lastPageDeleteUndo;", StringComparison.Ordinal) &&
            !undo.Contains("List<PageDeleteUndoBatch>", StringComparison.Ordinal),
            "Pages delete undo should keep exactly one batch");
        AssertTrue(
            actions.Contains("MovePageEntriesToUndoTrash(entries)", StringComparison.Ordinal) &&
            actions.Contains("RememberLastPageDelete(undoBatch)", StringComparison.Ordinal),
            "every Pages delete path should retain the selected batch before refreshing the tree");
        AssertTrue(
            commands.Contains("Keyboard.Modifiers == ModifierKeys.Control && key == Key.Z", StringComparison.Ordinal) &&
            commands.Contains("TryUndoLastPageDelete();", StringComparison.Ordinal),
            "Ctrl+Z should be scoped to the focused Pages tree");
        AssertTrue(
            commands.Contains("UndoLastPageDeleteMenuLabel()", StringComparison.Ordinal) &&
            commands.Contains("CanUndoLastPageDelete()", StringComparison.Ordinal),
            "Pages context menus should expose a discoverable undo command");
    }

    private static void WithTempJob(Action<OurPlanCoreJob> action)
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "onc_page_delete_undo", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            OurPlanCoreJob job = OurPlanCoreJobStore.CreateJob(tempRoot, "Undo Job");
            action(job);
        }
        finally
        {
            OurPlanCoreJobStore.ClearMetadataCache();
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string[] OrderedPaths(string parent) =>
        OurPlanCoreJobStore.GetOrderedChildDirectories(parent)
            .Select(Path.GetFullPath)
            .ToArray();

    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static void AssertSequence(IReadOnlyList<string> expected, IReadOnlyList<string> actual, string message)
    {
        if (!expected.SequenceEqual(actual, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{message}: expected '{string.Join("|", expected)}', got '{string.Join("|", actual)}'");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message) =>
        AssertTrue(!condition, message);

    private static void AssertEqual(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{message}: expected '{expected}', got '{actual}'");
    }
}
