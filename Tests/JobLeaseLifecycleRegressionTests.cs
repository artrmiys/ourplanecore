internal static class JobLeaseLifecycleRegressionTests
{
    public static void CandidateLeaseIsRecheckedAfterOldJobFlush()
    {
        string source = Read("MainWindow.JobLifecycle.cs");
        int candidateLoad = source.IndexOf(
            "OurPlanCoreJobStore.LoadJob(normalizedRoot, pending.Mode)",
            StringComparison.Ordinal);
        int firstConfirm = source.IndexOf("ConfirmPendingJobAccess(pending)", candidateLoad, StringComparison.Ordinal);
        int oldJobFlush = source.IndexOf("PrepareCurrentJobForSwitch()", firstConfirm, StringComparison.Ordinal);
        int secondConfirm = source.IndexOf("ConfirmPendingJobAccess(pending)", oldJobFlush, StringComparison.Ordinal);
        int adopt = source.IndexOf("TryAdoptJobAccess(pending)", secondConfirm, StringComparison.Ordinal);
        int switchJob = source.IndexOf("_currentJob = nextJob;", adopt, StringComparison.Ordinal);

        AssertTrue(candidateLoad >= 0, "candidate job must load behind its registered access boundary");
        AssertTrue(firstConfirm > candidateLoad, "candidate ownership must be checked after loading");
        AssertTrue(oldJobFlush > firstConfirm, "old job must flush only after the first candidate ownership check");
        AssertTrue(secondConfirm > oldJobFlush, "candidate ownership must be rechecked after the old job flush");
        AssertTrue(adopt > secondConfirm && switchJob > adopt,
            "candidate access must still be current before it can replace the active job");
    }

    public static void ReloadAndOwnershipLossFailClosed()
    {
        string lifecycle = Read("MainWindow.JobLifecycle.cs") +
                           Read("MainWindow.JobLifecycle.Maintenance.cs");
        string access = Read("MainWindow.JobAccess.cs");

        AssertTrue(
            lifecycle.Contains("JobAccessMode reloadMode = IsCurrentJobWritable", StringComparison.Ordinal) &&
            lifecycle.Contains("OurPlanCoreJobStore.LoadJob(normalizedRoot, reloadMode)", StringComparison.Ordinal) &&
            lifecycle.Contains("ShowJobOpenError(\"The current job could not be reloaded.\"", StringComparison.Ordinal),
            "same-job reload must derive mode from the live gate and handle load failure");

        int handler = access.IndexOf("private void HandleJobLeaseOwnershipLost(", StringComparison.Ordinal);
        int closeGate = access.IndexOf(
            "JobWriteAccess.SetMode(token, JobAccessMode.ReadOnly);",
            handler,
            StringComparison.Ordinal);
        int cancelAi = access.IndexOf("_activeAiRequestCts?.Cancel();", closeGate, StringComparison.Ordinal);
        int dispatch = access.IndexOf("Dispatcher.BeginInvoke", closeGate, StringComparison.Ordinal);
        AssertTrue(
            access.Contains("JobWriteAccess.GetMode(_currentJob.RootPath) == JobAccessMode.Writable", StringComparison.Ordinal),
            "MainWindow writable checks must derive from the atomic persistence gate");
        AssertTrue(closeGate > handler && cancelAi > closeGate && cancelAi < dispatch,
            "ownership loss must close the gate and cancel AI before deferred UI updates");
        AssertTrue(
            access.Contains("ApplyBookmarksJobAccessState();", StringComparison.Ordinal) &&
            access.Contains("ApplyThreeDReadOnlyState(readOnly);", StringComparison.Ordinal),
            "ownership loss must refresh destructive secondary editors");
    }

    public static void AiContinuationsStopWhenWriteAccessChanges()
    {
        string requests = Read("MainWindow.AiRequestActions.cs");
        string viewport = Read("MainWindow.ViewportCallbacks.cs");
        string lifecycle = Read("MainWindow.JobLifecycle.cs") +
                           Read("MainWindow.JobLifecycle.Maintenance.cs");

        AssertTrue(
            requests.Contains("OurPlanCoreJob runJob = _currentJob;", StringComparison.Ordinal) &&
            requests.Contains("JobWriteAccess.Demand(runJob.RootPath, \"save an AI response\")", StringComparison.Ordinal) &&
            requests.Contains("catch (JobWriteDeniedException ex)", StringComparison.Ordinal),
            "AI request continuation must stay bound to its original job and fail closed");
        AssertTrue(
            viewport.Contains("JobWriteAccess.Demand(runJob.RootPath, \"save an AI crop note response\")", StringComparison.Ordinal) &&
            viewport.Contains("!ReferenceEquals(_currentJob, runJob) || !IsCurrentJobWritable", StringComparison.Ordinal),
            "AI crop-note continuation must not write or mutate a replacement/read-only job");
        AssertTrue(
            lifecycle.Contains("if (IsCurrentJobWritable && IsModuleEnabled(ModuleId.Ai))", StringComparison.Ordinal) &&
            lifecycle.Contains("catch (JobWriteDeniedException ex)", StringComparison.Ordinal),
            "AI maintenance must start writable and stop cleanly if the lease is lost");
    }

    public static void LongAsyncWorkflowsStayBoundToTheirOriginJob()
    {
        string inbox = Read("MainWindow.AiInbox.cs");
        string wallTrace = Read("MainWindow.WallTrace.cs");
        string pdfImport = Read("MainWindow.PdfTakeoffImport.cs");

        int aiCapture = inbox.IndexOf("OurPlanCoreJob runJob = _currentJob;", StringComparison.Ordinal);
        int aiAwait = inbox.IndexOf("await RunAiRequestAsync(request);", aiCapture, StringComparison.Ordinal);
        int aiContinuationGuard = inbox.IndexOf(
            "EnsureExpectedJobWritable(runJob, \"continue AI crop bookmarks after the AI response\")",
            aiAwait,
            StringComparison.Ordinal);
        int aiResponseRead = inbox.IndexOf("SmartContextStore.LoadAiResponse(runJob, request.Id)", aiContinuationGuard, StringComparison.Ordinal);
        AssertTrue(
            aiCapture >= 0 && aiAwait > aiCapture && aiContinuationGuard > aiAwait && aiResponseRead > aiContinuationGuard,
            "AI bookmark continuations must recheck and keep using the originating job after awaiting OpenAI");

        int wallCapture = wallTrace.IndexOf("OurPlanCoreJob originJob = _currentJob;", StringComparison.Ordinal);
        int wallPageCapture = wallTrace.IndexOf("string originPageFolder = area.PageFolder;", wallCapture, StringComparison.Ordinal);
        int wallTraceAwait = wallTrace.IndexOf("await TraceWallCenterlinesWithRasterFallbackAsync(", wallPageCapture, StringComparison.Ordinal);
        int wallContinuationGuard = wallTrace.IndexOf(
            "EnsureWallTraceOriginWritable(originJob, originPageFolder)",
            wallTraceAwait,
            StringComparison.Ordinal);
        int wallMutation = wallTrace.IndexOf("CreateUniqueTakeoffItem(", wallContinuationGuard, StringComparison.Ordinal);
        AssertTrue(
            wallCapture >= 0 && wallPageCapture > wallCapture && wallTraceAwait > wallPageCapture &&
            wallContinuationGuard > wallTraceAwait && wallMutation > wallContinuationGuard,
            "Wall Trace must retain its originating job and sheet through async analysis before creating takeoffs");

        int pdfDialogCapture = pdfImport.IndexOf("OurPlanCoreJob? dialogOriginJob = _currentJob;", StringComparison.Ordinal);
        int pdfTargetCapture = pdfImport.IndexOf(
            "OurPlanCoreJob? targetJob = EnsurePdfTakeoffImportTargetJob(options, expectedTargetJob);",
            pdfDialogCapture,
            StringComparison.Ordinal);
        int pdfTargetGuard = pdfImport.IndexOf(
            "EnsurePdfTakeoffImportTargetWritable(",
            pdfTargetCapture,
            StringComparison.Ordinal);
        int pdfImportCall = pdfImport.IndexOf("ImportPdfTakeoffSource(targetJob,", pdfTargetGuard, StringComparison.Ordinal);
        int pdfReportCall = pdfImport.IndexOf("WritePdfTakeoffImportReport(targetJob,", pdfImportCall, StringComparison.Ordinal);
        AssertTrue(
            pdfDialogCapture >= 0 && pdfTargetCapture > pdfDialogCapture && pdfTargetGuard > pdfTargetCapture &&
            pdfImportCall > pdfTargetGuard && pdfReportCall > pdfImportCall,
            "PDF takeoff import and report writes must remain bound to the captured target job");
    }

    public static void ImportMetadataAndSimilarFlowsRecheckOriginAccess()
    {
        string pdfImport = Read("MainWindow.PdfImport.cs");
        string planSwift = Read("MainWindow.PlanSwiftImport.cs");
        string metadata = Read("MainWindow.PagesPdfMetadata.cs");
        string sheetManager = Read("MainWindow.WorkspaceManagers.cs");
        string similar = Read("MainWindow.SimilarCount.cs");

        AssertTrue(
            pdfImport.Contains("OurPlanCoreJob? expectedJob = null", StringComparison.Ordinal) &&
            pdfImport.Contains("OurPlanCoreJob importJob)", StringComparison.Ordinal) &&
            pdfImport.Contains("OurPlanCoreJobStore.ImportPdf(", StringComparison.Ordinal) &&
            pdfImport.Contains("                        importJob,", StringComparison.Ordinal) &&
            pdfImport.Contains("EnsureExpectedJobWritable(importJob, \"continue PDF raster import\")", StringComparison.Ordinal),
            "normal PDF import must retain and recheck its originating job through raster awaits");
        AssertTrue(
            planSwift.Contains("OurPlanCoreJob importJob = _currentJob;", StringComparison.Ordinal) &&
            planSwift.Contains("EnsureExpectedJobWritable(importJob, \"import PlanSwift data into this job\"", StringComparison.Ordinal) &&
            planSwift.Contains("JobWriteAccess.Demand(currentJobPath, \"import PlanSwift data into this job\")", StringComparison.Ordinal),
            "PlanSwift current-job import must recheck and gate its captured destination");
        AssertTrue(
            metadata.Contains("EnsureExpectedJobWritable(job, \"apply PDF metadata results\"", StringComparison.Ordinal) &&
            metadata.Contains("if (!IsExpectedJobWritable(job))", StringComparison.Ordinal) &&
            sheetManager.Contains("EnsureExpectedJobWritable(job, \"show Sheet Manager analysis results\")", StringComparison.Ordinal),
            "metadata continuations must not apply stale results to another/read-only job");
        AssertTrue(
            similar.Contains("IsExpectedJobWritable(reviewJob);", StringComparison.Ordinal) &&
            similar.Contains("EnsureCurrentJobWritable(\"add similar-count measurements\")", StringComparison.Ordinal),
            "Similar Count acceptance must recheck write access before changing measurements");
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

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
}
