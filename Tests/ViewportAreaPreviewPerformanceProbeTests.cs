using OurPlanCore;

internal static class ViewportAreaPreviewPerformanceProbeTests
{
    public static void ProbeIsEnvGatedTransientAndUsesPointerCadence()
    {
        string viewport = ReadRepoFile(Path.Combine("Controls", "PdfViewport.AreaPreviewPerformanceProbe.cs"));
        string smoke = ReadRepoFile("MainWindow.ViewportAreaPreviewSmoke.cs");
        string startup = ReadRepoFile("MainWindow.xaml.cs");
        string rendering = ReadRepoFile(Path.Combine("Controls", "PdfViewport.Rendering.cs"));
        string recorder = ReadRepoFile(Path.Combine("Models", "ViewportPerformanceRecorder.cs"));
        string launcher = ReadRepoFile(Path.Combine("Tools", "ui_viewport_area_preview_smoke.ps1"));

        AssertTrue(
            viewport.Contains("AreaPreviewProbeZooms = [4.0f, 5.334f]", StringComparison.Ordinal) &&
            viewport.Contains("WarmAreaPreviewProbePageFrameAsync()", StringComparison.Ordinal) &&
            viewport.Contains("RequestPointerMoveRepaint();", StringComparison.Ordinal) &&
            viewport.Contains("CapturePaintFrameCursor()", StringComparison.Ordinal) &&
            viewport.Contains("SnapshotPaintFramesSince(paintFrameCursor)", StringComparison.Ordinal) &&
            viewport.Contains("finally", StringComparison.Ordinal) &&
            viewport.Contains("_drawPts.AddRange(previousDrawPoints)", StringComparison.Ordinal) &&
            viewport.Contains("SetAreaPreviewProbeView(previousView)", StringComparison.Ordinal) &&
            !viewport.Contains("NotifyZoomChanged", StringComparison.Ordinal) &&
            !viewport.Contains("MaybeRequestSheetOverlayRenderScaleRefresh", StringComparison.Ordinal),
            "Area preview probe must warm the retained frame, exercise both work zooms through pointer cadence, and restore transient state");
        AssertTrue(
            !viewport.Contains("FinalizeDrawing", StringComparison.Ordinal) &&
            !viewport.Contains("MeasurementAdded", StringComparison.Ordinal) &&
            !viewport.Contains("OurPlanCoreJobStore", StringComparison.Ordinal),
            "Area preview performance probe must never finalize or persist a measurement");
        AssertTrue(
            smoke.Contains("IsTruthyEnvironment(ViewportAreaPreviewSmokeEnv)", StringComparison.Ordinal) &&
            smoke.Contains("Path.GetTempPath()", StringComparison.Ordinal) &&
            !smoke.Contains("AI_Context", StringComparison.Ordinal) &&
            startup.IndexOf("TryRunViewportAreaPreviewSmokeAsync", StringComparison.Ordinal) <
            startup.IndexOf("TryRunViewportPageStressSmokeAsync", StringComparison.Ordinal),
            "Area preview smoke must be explicitly env-gated, default its report to temp, and run before the terminating page smoke");
        AssertTrue(
            launcher.Contains("MediaBox [0 0 3024 2160]", StringComparison.Ordinal) &&
            launcher.Contains("StaticPageRenderDpi = 150", StringComparison.Ordinal) &&
            launcher.Contains("BlackVectorOverlayEnabled = $true", StringComparison.Ordinal) &&
            launcher.Contains("OURPLANCORE_SETTINGS_PATH", StringComparison.Ordinal) &&
            launcher.Contains("Remove-Item -LiteralPath $workspace.Root", StringComparison.Ordinal) &&
            !launcher.Contains("SetCursorPos", StringComparison.Ordinal) &&
            !launcher.Contains("mouse_event", StringComparison.Ordinal),
            "the launcher must use an isolated 42x30-inch temp job at 150 DPI with black vectors and no system-mouse automation");
        AssertTrue(
            rendering.Contains("ViewportPerformanceRecorder.RecordPaintFrame(", StringComparison.Ordinal) &&
            recorder.Contains("private static volatile bool _isActive", StringComparison.Ordinal) &&
            recorder.Contains("if (!_isActive)", StringComparison.Ordinal) &&
            recorder.Contains("PageFrameState = pageFrameState", StringComparison.Ordinal) &&
            viewport.Contains("hitRate < AreaPreviewProbeMinimumHitRate", StringComparison.Ordinal) &&
            viewport.Contains("missOrBypassCount > AreaPreviewProbeMaximumMissOrBypassCount", StringComparison.Ordinal) &&
            viewport.Contains("p95ElapsedMs > AreaPreviewProbeMaximumP95ElapsedMs", StringComparison.Ordinal) &&
            viewport.Contains("p95PageMs > AreaPreviewProbeMaximumP95PageMs", StringComparison.Ordinal) &&
            viewport.Contains("targetDpi != 150 || rasterDpi != 150", StringComparison.Ordinal) &&
            viewport.Contains("!ShowBlackVectorOverlay", StringComparison.Ordinal) &&
            viewport.Contains("blackVectorSegmentCount <= 0", StringComparison.Ordinal) &&
            viewport.Contains("_rasterSheetVisualSegments.Count > 0", StringComparison.Ordinal),
            "every active paint must record frame/cache timing while the inactive production path remains a volatile fast exit");
    }

    public static void RecorderCapturesEveryPaintTimingField()
    {
        if (ViewportPerformanceRecorder.IsActive)
            ViewportPerformanceRecorder.EndRun();

        ViewportPerformanceRecorder.BeginRun("probe-job", "area-preview-pure-test");
        ViewportPerformanceRecorder.RecordPaintFrame("page", 4.0f, "miss", 40, 38, 0);
        int cursor = ViewportPerformanceRecorder.CapturePaintFrameCursor();
        ViewportPerformanceRecorder.RecordPaintFrame("page", 4.0f, "miss", 18, 16, 1);
        ViewportPerformanceRecorder.RecordPaintFrame("page", 5.334f, "hit", 6, 4, 0);
        IReadOnlyList<ViewportPaintFrameSample> previewFrames =
            ViewportPerformanceRecorder.SnapshotPaintFramesSince(cursor);
        ViewportPerformanceRun run = ViewportPerformanceRecorder.EndRun();

        AssertTrue(run.TotalPaintFrameCount == 3 && run.PaintFrames.Count == 3,
            "active recorder must retain every sampled paint frame");
        AssertTrue(
            previewFrames.Count == 2 &&
            previewFrames[0].PageFrameState == "miss" &&
            previewFrames[0].ElapsedMs == 18 &&
            previewFrames[0].PageBitmapMs == 16 &&
            previewFrames[0].InProgressMs == 1 &&
            Math.Abs(previewFrames[1].Zoom - 5.334) < 0.0001,
            "paint samples must retain page-frame state, elapsed, page, in-progress, and zoom values");
        AssertTrue(
            run.Summary.PaintFrameCount == 3 &&
            run.Summary.MaxPaintFrameMs == 40 &&
            Math.Abs(run.Summary.AveragePaintFrameMs - 21.33) < 0.001 &&
            run.Summary.PaintFrameCountByState["hit"] == 1,
            "paint frame summary must expose bounded timing and retained-frame state evidence");
    }

    private static string ReadRepoFile(string relativePath) =>
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

        throw new DirectoryNotFoundException("Could not find ourplancore repo root.");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
