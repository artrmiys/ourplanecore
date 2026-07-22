internal static class ViewportStaticFrameCacheRegressionTests
{
    public static void StaticRasterAndBlackVectorAreRetainedDuringDigitizing()
    {
        string cache = ReadRepoFile(Path.Combine(
            "Controls",
            "PdfViewport.StaticPageFrameCache.cs"));
        string rendering = ReadRepoFile(Path.Combine(
            "Controls",
            "PdfViewport.Rendering.cs"));
        string pdfSnap = ReadRepoFile(Path.Combine(
            "Controls",
            "PdfViewport.PdfSnap.cs"));
        string detached = ReadRepoFile(Path.Combine(
            "Dialogs",
            "DetachedSheetWindow.cs"));
        string drawPageFrame = SliceBetween(
            cache,
            "private StaticPageFramePaintResult DrawPageFrame(",
            "private void DrawPageBitmapAndStaticOverlays(");
        string drawStaticBackground = SliceBetween(
            cache,
            "private void DrawPageBitmapAndStaticOverlays(",
            "private bool CanUseStaticPageFrameCache(");

        AssertTrue(
            cache.Contains("IsStaticRasterDisplayActive()", StringComparison.Ordinal) &&
            cache.Contains("DrawStaticPageFrameBitmap(canvas)", StringComparison.Ordinal) &&
            cache.Contains("canvas.ResetMatrix()", StringComparison.Ordinal) &&
            cache.Contains("frameCanvas.SetMatrix(rootMatrix)", StringComparison.Ordinal),
            "the retained page frame must be static-raster-only and replayed 1:1 under the original DPI matrix");
        AssertTrue(
            drawStaticBackground.Contains("DrawBlackVectorInkOverlay(canvas, visiblePdf)", StringComparison.Ordinal) &&
            drawPageFrame.Contains("DrawPageBitmapAndStaticOverlays(frameCanvas, visiblePdf)", StringComparison.Ordinal) &&
            drawPageFrame.Contains("DrawSheetOverlay(frameCanvas, visiblePdf)", StringComparison.Ordinal) &&
            drawPageFrame.IndexOf("DrawPageBitmapAndStaticOverlays(frameCanvas, visiblePdf)", StringComparison.Ordinal) <
            drawPageFrame.IndexOf("DrawSheetOverlay(frameCanvas, visiblePdf)", StringComparison.Ordinal) &&
            rendering.Contains("!pageFrame.IncludesSheetOverlay", StringComparison.Ordinal),
            "black vector and sheet overlay must be retained in their original order without double painting");
        AssertTrue(
            cache.Contains("!_pdfLayerTraceEnabled", StringComparison.Ordinal) &&
            cache.Contains("!IsSheetOverlayPointEditing", StringComparison.Ordinal) &&
            cache.Contains("!_draggingSheetOverlay", StringComparison.Ordinal),
            "PDF trace and editable sheet overlays must stay on the original dynamic path");
        AssertTrue(
            cache.Contains("bool allowDetailRender = !IsStaticRasterDisplayActive()", StringComparison.Ordinal) &&
            cache.Contains("if (allowDetailRender)", StringComparison.Ordinal),
            "the pinned static raster must not bake stale live detail tiles into the retained frame");
        AssertTrue(
            cache.Contains("candidate.ReadyToDraw", StringComparison.Ordinal) &&
            cache.Contains("_staticPageFrameAllocationRetryAfterUtc", StringComparison.Ordinal) &&
            cache.Contains("ReportStaticPageFrameFailure(ex)", StringComparison.Ordinal),
            "retained frame allocation failures must fall back safely instead of escaping from OnPaint");
        AssertTrue(
            pdfSnap.Contains("_rasterSheetVisualContentGeneration++", StringComparison.Ordinal) &&
            cache.Contains("_rasterSheetVisualContentGeneration", StringComparison.Ordinal),
            "asynchronously loaded black vector segments must invalidate a previously blank retained frame");
        AssertTrue(
            detached.Contains("_viewport.ShowBlackVectorOverlay = settings.BlackVectorOverlayEnabled", StringComparison.Ordinal),
            "detached sheets must preserve the same black PDF vector overlay setting");
    }

    public static void PointerPreviewUsesOneTrailingSixtyFpsCadence()
    {
        string input = ReadRepoFile(Path.Combine("Controls", "PdfViewport.Input.cs"));
        string snap = ReadRepoFile(Path.Combine("Controls", "PdfViewport.DigitizerSnap.cs"));
        string edge = ReadRepoFile(Path.Combine("Controls", "PdfViewport.EdgeSnap.cs"));
        string policy = ReadRepoFile(Path.Combine("Models", "ViewportRenderPolicy.cs"));

        AssertTrue(
            policy.Contains("PointerMoveRepaintMinIntervalMs = 16", StringComparison.Ordinal) &&
            input.Contains("SchedulePointerMoveRepaint(cadence - elapsed)", StringComparison.Ordinal) &&
            input.Contains("DispatcherPriority.Render", StringComparison.Ordinal),
            "raw pointer events must coalesce to a render-priority cadence with a trailing frame");
        AssertTrue(
            input.Contains("deferPreviewRepaint: true", StringComparison.Ordinal) &&
            snap.Contains("RequestInputPreviewRepaint(deferRepaint)", StringComparison.Ordinal) &&
            edge.Contains("RequestInputPreviewRepaint(deferRepaint)", StringComparison.Ordinal),
            "rubber-band, snap, and edge previews must share the throttled pointer repaint path");

        string rubberBlock = SliceBetween(
            input,
            "// Rubber-band",
            "if (IsMissingScaleForLinearArea())");
        AssertFalse(
            rubberBlock.Contains("RequestRepaint()", StringComparison.Ordinal),
            "active Area rubber-band movement must not bypass pointer cadence with a full repaint");
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

    private static string SliceBetween(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        return start >= 0 && end > start
            ? source[start..end]
            : throw new InvalidOperationException($"Could not slice source between '{startMarker}' and '{endMarker}'.");
    }

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

    private static void AssertFalse(bool condition, string message) =>
        AssertTrue(!condition, message);
}
