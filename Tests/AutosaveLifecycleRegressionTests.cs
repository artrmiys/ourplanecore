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
        string access = File.ReadAllText(RepoFile("MainWindow.JobAccess.cs"));
        int acquire = access.IndexOf("leaseService.TryAcquire(jobRoot)", StringComparison.Ordinal);
        int registerWritable = access.IndexOf(
            "TryRegisterPendingAccess(jobRoot, JobAccessMode.Writable",
            acquire,
            StringComparison.Ordinal);
        int installGate = access.IndexOf("JobWriteAccess.RegisterJob(jobRoot, mode)", StringComparison.Ordinal);
        int prepareAccess = lifecycle.IndexOf("TryPrepareJobAccess(normalizedRoot, out pending)", StringComparison.Ordinal);
        int loadTarget = lifecycle.IndexOf(
            "OurPlanCoreJobStore.LoadJob(normalizedRoot, pending.Mode)",
            prepareAccess,
            StringComparison.Ordinal);
        int prepare = lifecycle.IndexOf("PrepareCurrentJobForSwitch()", loadTarget, StringComparison.Ordinal);
        int assign = lifecycle.IndexOf("_currentJob = nextJob;", prepare, StringComparison.Ordinal);

        AssertTrue(
            recovery.Contains("private bool PrepareCurrentJobForSwitch()", StringComparison.Ordinal) &&
            recovery.Contains("if (!TryFlushTakeoffAutosaves(\"switch jobs\"))", StringComparison.Ordinal) &&
            recovery.Contains("return false;", StringComparison.Ordinal),
            "job-switch preparation must be cancellable on autosave failure");
        AssertTrue(acquire >= 0 && registerWritable > acquire, "write lease must be acquired before writable access is registered");
        AssertTrue(installGate >= 0, "pending job access must install the typed write gate");
        AssertTrue(prepareAccess >= 0 && loadTarget > prepareAccess, "lease and write gate must be prepared before target load");
        AssertTrue(prepare > loadTarget, "target job must load under its access mode before closing the current job");
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

    public static void PackageWindowCloseSavesCurrentFileWithoutChoice()
    {
        string bounds = File.ReadAllText(RepoFile("MainWindow.WindowBounds.cs"));
        int closing = bounds.IndexOf("protected override void OnClosing(CancelEventArgs e)", StringComparison.Ordinal);
        int closed = bounds.IndexOf("protected override void OnClosed(EventArgs e)", closing, StringComparison.Ordinal);
        string closePath = bounds[closing..closed];

        AssertTrue(
            closePath.Contains(
                "TrySaveCurrentPackage(\"close OurPlanCore\", showDialog: true)",
                StringComparison.Ordinal),
            "package close must save automatically to the current .ourplan file");
        AssertFalse(
            closePath.Contains("ResolveFailedPackageCheckpointBeforeExit(", StringComparison.Ordinal),
            "package close must not ask the user to choose Save As or local recovery");
    }

    public static void SaveAsUiPaletteAndShortcutExposeOneContextSensitiveAction()
    {
        string xaml = File.ReadAllText(RepoFile("MainWindow.xaml"));
        string exportMenu = File.ReadAllText(RepoFile("MainWindow.ExportMenu.cs"));
        string palette = File.ReadAllText(RepoFile("MainWindow.CommandPalette.cs"));
        string shell = File.ReadAllText(RepoFile("MainWindow.xaml.cs"));
        string package = File.ReadAllText(RepoFile("MainWindow.ProjectPackage.cs"));
        string shortcuts = File.ReadAllText(RepoFile(Path.Combine("Models", "KeyboardShortcutCatalog.cs")));
        int jobGroup = xaml.IndexOf("<!-- GROUP: JOB -->", StringComparison.Ordinal);
        int saveAsButton = xaml.IndexOf("x:Name=\"BtnMainSaveAs\"", jobGroup, StringComparison.Ordinal);
        int pdfGroup = xaml.IndexOf("<!-- GROUP: PDF -->", saveAsButton, StringComparison.Ordinal);
        string button = jobGroup >= 0 && saveAsButton > jobGroup && pdfGroup > saveAsButton
            ? xaml[saveAsButton..pdfGroup]
            : "";

        AssertTrue(
            jobGroup >= 0 && saveAsButton > jobGroup && pdfGroup > saveAsButton,
            "the visible Save As button must stay in the top Main JOB ribbon group");
        AssertTrue(
            button.Contains("Command=\"{x:Static ApplicationCommands.SaveAs}\"", StringComparison.Ordinal) &&
            button.Contains("<TextBlock Text=\"Save As\"", StringComparison.Ordinal) &&
            !button.Contains("MenuChevronIcon", StringComparison.Ordinal),
            "the JOB ribbon must expose one direct Save As command without a format dropdown");
        AssertFalse(
            xaml.Contains("BtnSaveAsMenu_Click", StringComparison.Ordinal) ||
            exportMenu.Contains("AddSaveAsMenuItems", StringComparison.Ordinal) ||
            exportMenu.Contains("One portable file", StringComparison.Ordinal) ||
            exportMenu.Contains("Legacy folder copy", StringComparison.Ordinal),
            "Save As surfaces must not expose separate format choices");
        AssertTrue(
            exportMenu.Contains(
                "MakeMenuItem(\"Save As...\", CanSaveAsCurrentProject, SaveAsCurrentProject)",
                StringComparison.Ordinal),
            "the secondary export menu must route its one Save As row through the context-sensitive command");
        AssertTrue(
            CountOccurrences(palette, "\"file.saveAs\"") == 2 &&
            palette.Contains("\"Save As\"", StringComparison.Ordinal) &&
            palette.Contains("\"Ctrl+Shift+S\"", StringComparison.Ordinal) &&
            palette.Contains("case \"file.saveAs\": SaveAsCurrentProject();", StringComparison.Ordinal),
            "the command palette must define and dispatch exactly one Save As action with its shortcut");
        AssertFalse(
            palette.Contains("file.saveAsOurPlan", StringComparison.Ordinal) ||
            palette.Contains("file.saveLegacyCopy", StringComparison.Ordinal),
            "the command palette must not retain format-specific Save As commands");
        AssertTrue(
            shell.Contains("ApplicationCommands.SaveAs", StringComparison.Ordinal) &&
            shell.Contains("(_, _) => SaveAsCurrentProject()", StringComparison.Ordinal) &&
            shell.Contains("e.CanExecute = CanSaveAsCurrentProject", StringComparison.Ordinal),
            "Ctrl+Shift+S and the ribbon command must share the context-sensitive Save As dispatcher");
        AssertTrue(
            shortcuts.Contains(
                "Item(\"Ctrl+Shift+S\", \"Save the current project to a new location\")",
                StringComparison.Ordinal) &&
            !shortcuts.Contains("Save As OurPlan project", StringComparison.Ordinal),
            "shortcut help must describe one format-preserving Save As action");
        AssertTrue(
            package.Contains("private bool CanSaveAsCurrentProject", StringComparison.Ordinal) &&
            package.Contains("private void SaveAsCurrentProject()", StringComparison.Ordinal),
            "Save As availability and dispatch must have one shared context-sensitive owner");
    }

    public static void LegacySaveAsKeepsFolderFormatAndSwitchesToDestination()
    {
        string package = File.ReadAllText(RepoFile("MainWindow.ProjectPackage.cs"));
        int dispatchStart = package.IndexOf("private void SaveAsCurrentProject()", StringComparison.Ordinal);
        int openDialog = package.IndexOf("private void OpenOurPlanProjectDialog()", dispatchStart, StringComparison.Ordinal);
        string dispatch = dispatchStart >= 0 && openDialog > dispatchStart
            ? package[dispatchStart..openDialog]
            : "";
        int legacyStart = package.IndexOf("private void SaveLegacyFolderAs()", StringComparison.Ordinal);
        int packageCreate = package.IndexOf("private bool CreateNewPackageProject(", legacyStart, StringComparison.Ordinal);
        string legacy = legacyStart >= 0 && packageCreate > legacyStart
            ? package[legacyStart..packageCreate]
            : "";
        int copy = legacy.IndexOf("OurPlanPackageWorkspace.ExportLegacyCopy", StringComparison.Ordinal);
        int switchJob = legacy.IndexOf(
            "OpenJob(destination, copiedPage, currentJobPrepared: true)",
            StringComparison.Ordinal);

        AssertTrue(
            dispatch.Contains("if (HasCurrentPackageSession)", StringComparison.Ordinal) &&
            dispatch.Contains("SaveAsOurPlanProject();", StringComparison.Ordinal) &&
            dispatch.Contains("SaveLegacyFolderAs();", StringComparison.Ordinal),
            "the one Save As command must preserve package projects as packages and folder projects as folders");
        AssertTrue(
            legacy.Contains("string destination = Path.Combine(parent", StringComparison.Ordinal) &&
            !legacy.Contains("OurPlanPackageFormat.Extension", StringComparison.Ordinal) &&
            !legacy.Contains("SaveAsOurPlanProject", StringComparison.Ordinal),
            "folder-project Save As must choose a destination folder without converting it to .ourplan");
        AssertTrue(
            legacy.Contains("TrySaveCurrentJobData(\"save project as\")", StringComparison.Ordinal) &&
            legacy.Contains("PrepareCurrentJobForSwitch()", StringComparison.Ordinal) &&
            copy >= 0 &&
            switchJob > copy,
            "folder-project Save As must persist current data, copy durable files, and then open the copied destination as the active project");
    }

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
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
