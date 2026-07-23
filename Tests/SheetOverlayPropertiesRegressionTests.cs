internal static class SheetOverlayPropertiesRegressionTests
{
    public static void SheetOverlayPropertiesPanelExposesSourceAlignmentTransformAndAppearance()
    {
        string xaml = ReadRepoFile(Path.Combine(
            "Controls",
            "SheetOverlayPropertiesPanel.xaml"));
        string panel = ReadRepoFile(Path.Combine(
            "Controls",
            "SheetOverlayPropertiesPanel.xaml.cs"));
        string main = ReadRepoFile("MainWindow.SheetOverlay.Properties.cs");

        AssertContainsAll(
            xaml,
            "TxtSourceSheet",
            "BtnOpenSource",
            "BtnChooseReplace",
            "BtnNextMatch",
            "BtnClear",
            "BtnAutoFit",
            "BtnFindBest",
            "BtnEditPoints",
            "SldOffsetX",
            "SldOffsetY",
            "SldScale",
            "SldRotation",
            "BtnReset",
            "BtnColorRed",
            "BtnColorBlue",
            "BtnColorGreen",
            "BtnColorOrange",
            "BtnColorMagenta",
            "BtnColorGray");
        AssertTrue(
            !xaml.Contains("Opacity", StringComparison.OrdinalIgnoreCase),
            "opacity must stay out of the panel until renderer and persistence semantics agree");
        AssertTrue(
            main.Contains("preserveCenterForScale: e.Component == SheetOverlayTransformComponent.Scale", StringComparison.Ordinal),
            "scale preview must preserve the displayed overlay center");
        AssertTrue(
            panel.Contains("ApplyTransformValues(values, preserveFocusedEditor: true)", StringComparison.Ordinal) &&
            panel.Contains("ReferenceEquals(Keyboard.FocusedElement, editor)", StringComparison.Ordinal),
            "live preview must not rewrite the focused numeric editor while a decimal value is being typed");
    }

    public static void SheetOverlayContextMenusStayCompactAndRouteAdvancedActionsToProperties()
    {
        string menus = ReadRepoFile("MainWindow.SheetOverlay.Menus.cs");

        AssertContainsAll(
            menus,
            "\"Overlay Properties...\"",
            "\"Auto Fit Overlay\"",
            "\"Use This Sheet as Current Overlay\"",
            "\"Clear Current Sheet Overlay\"",
            "\"Hide Overlay\"",
            "\"Show Overlay\"",
            "\"Open Overlay Sheet\"",
            "\"Clear Overlay\"");
        AssertTrue(
            !menus.Contains("Move Left", StringComparison.Ordinal) &&
            !menus.Contains("Move Right", StringComparison.Ordinal) &&
            !menus.Contains("Scale Up", StringComparison.Ordinal) &&
            !menus.Contains("Scale Down", StringComparison.Ordinal) &&
            !menus.Contains("Rotate Left", StringComparison.Ordinal) &&
            !menus.Contains("Rotate Right", StringComparison.Ordinal) &&
            !menus.Contains("Edit Transform...", StringComparison.Ordinal) &&
            !menus.Contains("BuildSheetOverlayAdjustmentMenu", StringComparison.Ordinal),
            "fine transform commands must live in Overlay Properties instead of context menus");
    }

    public static void SheetOverlayTransformPreviewPersistsOnceOnCommit()
    {
        string panel = ReadRepoFile("MainWindow.SheetOverlay.Properties.cs");
        string viewport = ReadRepoFile(Path.Combine(
            "Controls",
            "PdfViewport.SheetOverlaySelection.cs"));
        string previewMethod = SliceBetween(
            viewport,
            "public SheetOverlayTransformSnapshot? PreviewSheetOverlayTransform(",
            "public void CommitSheetOverlayTransformPreview(");
        string commitMethod = SliceBetween(
            viewport,
            "public void CommitSheetOverlayTransformPreview(",
            "public void CancelSheetOverlayTransformPreview(");

        AssertTrue(
            panel.Contains("_viewport.PreviewSheetOverlayTransform(", StringComparison.Ordinal) &&
            panel.Contains("_viewport.CommitSheetOverlayTransformPreview(", StringComparison.Ordinal),
            "the panel must separate live preview from commit");
        AssertTrue(
            !previewMethod.Contains("SheetOverlayTransformChanged?.Invoke", StringComparison.Ordinal) &&
            CountOccurrences(commitMethod, "SheetOverlayTransformChanged?.Invoke") == 1,
            "preview must remain in memory and commit must raise one persistence event");
        AssertTrue(
            viewport.Contains("CancelSheetOverlayTransformPreview", StringComparison.Ordinal) &&
            viewport.Contains("_sheetOverlayTransformPreviewStart", StringComparison.Ordinal),
            "Escape cancellation must restore the saved transform snapshot");
    }

    public static void SheetOverlayActiveFrameUsesRotatedOverlayCorners()
    {
        string viewport = ReadRepoFile(Path.Combine(
            "Controls",
            "PdfViewport.SheetOverlaySelection.cs"));
        string rendering = ReadRepoFile(Path.Combine(
            "Controls",
            "PdfViewport.Rendering.cs"));

        AssertContainsAll(
            viewport,
            "new(0xF4, 0x9B, 0x24)",
            "OverlayLocalToDisplay(new SKPoint(0, 0))",
            "OverlayLocalToDisplay(new SKPoint(width, 0))",
            "OverlayLocalToDisplay(new SKPoint(width, height))",
            "OverlayLocalToDisplay(new SKPoint(0, height))",
            "SheetOverlaySelectionHandle.Scale",
            "SheetOverlaySelectionHandle.Rotation");
        AssertTrue(
            rendering.Contains("DrawSheetOverlaySelection(canvas)", StringComparison.Ordinal),
            "the active frame must be drawn only by the live viewport rendering path");
        AssertTrue(
            !viewport.Contains("IsPointInsideSheetOverlay(pdf)", StringComparison.Ordinal),
            "ordinary clicks inside the overlay rectangle must not steal takeoff selection");
    }

    public static void SheetOverlayLiveTransformBypassesStaticFrameCache()
    {
        string cache = ReadRepoFile(Path.Combine(
            "Controls",
            "PdfViewport.StaticPageFrameCache.cs"));
        string overlay = ReadRepoFile(Path.Combine(
            "Controls",
            "PdfViewport.SheetOverlay.cs"));

        AssertTrue(
            cache.Contains("!_sheetOverlayTransformPreviewActive", StringComparison.Ordinal),
            "live transform preview must not rebuild retained page frames on every slider tick");
        AssertTrue(
            overlay.Contains("_zoom * _sheetOverlayScale", StringComparison.Ordinal),
            "overlay quality refresh must use the effective displayed scale");
    }

    public static void SheetOverlayLivePreviewSurvivesQualityBitmapReplacement()
    {
        string overlay = ReadRepoFile(Path.Combine(
            "Controls",
            "PdfViewport.SheetOverlay.cs"));
        string selection = ReadRepoFile(Path.Combine(
            "Controls",
            "PdfViewport.SheetOverlaySelection.cs"));
        string previewApply = SliceBetween(
            selection,
            "private void ApplySheetOverlayTransformPreview(",
            "private SheetOverlayTransformSnapshot BuildSheetOverlayTransformSnapshot(");
        string refresh = SliceBetween(
            overlay,
            "private void MaybeRequestSheetOverlayRenderScaleRefresh()",
            "private static float InferSheetOverlayBitmapScale(");

        AssertContainsAll(
            overlay,
            "bool preserveTransformPreview",
            "preserveTransformPreview ? liveOffsetXPt : offsetXPt",
            "preserveTransformPreview ? liveOverlayScale : overlayScale");
        AssertTrue(
            !previewApply.Contains("MaybeRequestSheetOverlayRenderScaleRefresh", StringComparison.Ordinal),
            "live slider and handle ticks must defer quality reload until commit");
        AssertTrue(
            refresh.Contains("_sheetOverlayTransformPreviewActive", StringComparison.Ordinal),
            "an already queued bitmap replacement must not start another reload during preview");
    }

    public static void SheetOverlayFrameHonorsReadOnlyPageAndModuleLifecycle()
    {
        string selection = ReadRepoFile(Path.Combine(
            "Controls",
            "PdfViewport.SheetOverlaySelection.cs"));
        string properties = ReadRepoFile("MainWindow.SheetOverlay.Properties.cs");
        string overlay = ReadRepoFile("MainWindow.SheetOverlay.cs");
        string access = ReadRepoFile("MainWindow.JobAccess.cs");
        string modules = ReadRepoFile("MainWindow.Modules.cs");
        string frameDraw = SliceBetween(
            selection,
            "private void DrawSheetOverlaySelection(SKCanvas canvas)",
            "private bool TryGetSheetOverlaySelectionGeometry(");

        AssertContainsAll(
            selection,
            "_sheetOverlaySelectionCanEdit",
            "canEdit && !IsReadOnlyMode",
            "if (!_sheetOverlaySelectionCanEdit || IsReadOnlyMode)");
        AssertTrue(
            frameDraw.IndexOf("if (!_sheetOverlaySelectionCanEdit || IsReadOnlyMode)", StringComparison.Ordinal) <
            frameDraw.IndexOf("float scaleHandleRadius", StringComparison.Ordinal),
            "a read-only orange frame must stop before drawing scale or rotation handles");
        AssertContainsAll(
            properties,
            "canEdit: !IsCurrentJobReadOnly",
            "OnSheetOverlayPageCleared()",
            "ActivateCurrentSheetOverlayFrameAfterBitmapLoad");
        AssertTrue(
            overlay.Contains("ActivateCurrentSheetOverlayFrameAfterBitmapLoad(page);", StringComparison.Ordinal),
            "the orange frame must activate only after the new page overlay bitmap is installed");
        AssertTrue(
            access.Contains("ApplySheetOverlayJobAccessState();", StringComparison.Ordinal),
            "losing the write lease must immediately disable active overlay handles");
        AssertTrue(
            modules.Contains(
                "SetVisible(SheetOverlayPropertiesTab, IsModuleEnabled(ModuleId.SheetOverlay));",
                StringComparison.Ordinal),
            "the Overlay Properties tab must follow Sheet Overlay module visibility");
    }

    private static void AssertContainsAll(string text, params string[] values)
    {
        foreach (string value in values)
        {
            AssertTrue(
                text.Contains(value, StringComparison.Ordinal),
                $"Expected source marker '{value}' was not found.");
        }
    }

    private static string SliceBetween(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        return start >= 0 && end > start
            ? source[start..end]
            : throw new InvalidOperationException(
                $"Could not slice source between '{startMarker}' and '{endMarker}'.");
    }

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

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
