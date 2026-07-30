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
            "CmbOverlayLayer",
            "BtnAdd",
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
            "BtnColorGray",
            "BtnColorCustom",
            "SldOpacity",
            "TxtOpacity");
        AssertTrue(
            panel.Contains("OpacityPreviewRequested", StringComparison.Ordinal) &&
            panel.Contains("OpacityCommitRequested", StringComparison.Ordinal) &&
            panel.Contains("SheetOverlayPropertiesCommand.SelectLayer", StringComparison.Ordinal),
            "layer selection and opacity must use explicit preview/commit commands");
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
            "\"Remove Overlay Layer\"",
            "\"Move Layer Up\"",
            "\"Move Layer Down\"",
            "\"Fit by 2 Points\"");
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
        string overlay = ReadViewportSheetOverlaySources();
        string publishMethod = SliceBetween(
            overlay,
            "private void PublishSheetOverlayTransformChange(",
            "private static string BuildSheetOverlayTransformStatus(");

        AssertTrue(
            panel.Contains("_viewport.PreviewSheetOverlayTransform(", StringComparison.Ordinal) &&
            panel.Contains("_viewport.CommitSheetOverlayTransformPreview(", StringComparison.Ordinal),
            "the panel must separate live preview from commit");
        AssertTrue(
            !previewMethod.Contains("SheetOverlayTransformChanged?.Invoke", StringComparison.Ordinal) &&
            CountOccurrences(commitMethod, "CommitSheetOverlayTransformChange(") == 1 &&
            CountOccurrences(publishMethod, "SheetOverlayTransformChanged?.Invoke") == 1,
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
        string hitTest = SliceBetween(
            viewport,
            "private SheetOverlaySelectionHandle HitSheetOverlaySelectionHandle(SKPoint pdf)",
            "private static SKPoint SheetOverlayDisplayCenter(");

        AssertContainsAll(
            viewport,
            "new(0xF4, 0x9B, 0x24)",
            "OverlayLocalToDisplay(new SKPoint(0, 0))",
            "OverlayLocalToDisplay(new SKPoint(width, 0))",
            "OverlayLocalToDisplay(new SKPoint(width, height))",
            "OverlayLocalToDisplay(new SKPoint(0, height))",
            "SheetOverlaySelectionHandle.Move",
            "SheetOverlaySelectionHandle.Scale",
            "SheetOverlaySelectionHandle.Rotation");
        AssertTrue(
            rendering.Contains("DrawSheetOverlaySelection(canvas)", StringComparison.Ordinal),
            "the active frame must be drawn only by the live viewport rendering path");
        AssertTrue(
            hitTest.IndexOf("SheetOverlaySelectionHandle.Rotation", StringComparison.Ordinal) <
            hitTest.IndexOf("SheetOverlaySelectionHandle.Scale", StringComparison.Ordinal) &&
            hitTest.IndexOf("SheetOverlaySelectionHandle.Scale", StringComparison.Ordinal) <
            hitTest.IndexOf("SheetOverlaySelectionHandle.Move", StringComparison.Ordinal) &&
            hitTest.Contains("SKPoint local = OverlayDisplayToLocal(pdf);", StringComparison.Ordinal) &&
            hitTest.Contains("local.X >= 0", StringComparison.Ordinal) &&
            hitTest.Contains("local.Y <= height", StringComparison.Ordinal),
            "rotation and scale handles must win before exact rotated-interior move hit testing");
    }

    public static void SheetOverlayMoveAndUndoUseOneValidatedViewportAction()
    {
        string selection = ReadRepoFile(Path.Combine(
            "Controls",
            "PdfViewport.SheetOverlaySelection.cs"));
        string overlay = ReadViewportSheetOverlaySources();
        string undo = ReadRepoFile(Path.Combine(
            "Controls",
            "PdfViewport.Undo.cs"));
        string undoEditing = ReadRepoFile(Path.Combine(
            "Controls",
            "PdfViewport.UndoEditingApi.cs"));
        string panel = ReadRepoFile(Path.Combine(
            "Controls",
            "SheetOverlayPropertiesPanel.xaml.cs"));
        string main = string.Concat(
            ReadRepoFile("MainWindow.SheetOverlay.cs"),
            ReadRepoFile("MainWindow.SheetOverlay.Layers.cs"));
        string properties = ReadRepoFile("MainWindow.SheetOverlay.Properties.cs");
        string mouseDown = SliceBetween(
            selection,
            "private void SheetOverlaySelection_PreviewMouseLeftButtonDown(",
            "private void SheetOverlaySelection_PreviewMouseMove(");
        string move = SliceBetween(
            selection,
            "private void SheetOverlaySelection_PreviewMouseMove(",
            "private void SheetOverlaySelection_PreviewMouseLeftButtonUp(");
        string undoLast = SliceBetween(
            undoEditing,
            "public void UndoLast()",
            "public void DeleteMeasurements(");

        AssertContainsAll(
            move,
            "SheetOverlaySelectionHandle.Move",
            "start.OffsetXPt + pdf.X - _sheetOverlayHandleStartPointerPdf.X",
            "start.OffsetYPt + pdf.Y - _sheetOverlayHandleStartPointerPdf.Y",
            "start.OverlayScale",
            "start.OverlayRotationDegrees");
        AssertContainsAll(
            selection,
            "CommitSheetOverlayTransformPreview(status)",
            "TryCancelPendingSheetOverlayTransformForUndo",
            "FinishSheetOverlaySelectionHandle(commit: false)");
        AssertTrue(
            mouseDown.IndexOf("Focus();", StringComparison.Ordinal) <
            mouseDown.IndexOf("BeginSheetOverlayTransformPreview()", StringComparison.Ordinal) &&
            mouseDown.IndexOf("BeginSheetOverlayTransformPreview()", StringComparison.Ordinal) <
            mouseDown.IndexOf("CaptureMouse();", StringComparison.Ordinal),
            "overlay handle input must commit the focused editor before starting and capturing a new gesture");
        AssertTrue(
            undoLast.IndexOf("TryCancelPendingSheetOverlayTransformForUndo()", StringComparison.Ordinal) <
            undoLast.IndexOf("_drawPts.Count", StringComparison.Ordinal),
            "Ctrl+Z must cancel an active overlay gesture before undoing unfinished drawing points");
        AssertTrue(
            mouseDown.Contains("IsSheetOverlayDragModifierActive()", StringComparison.Ordinal) &&
            mouseDown.IndexOf("IsSheetOverlayDragModifierActive()", StringComparison.Ordinal) <
            mouseDown.IndexOf("HitSheetOverlaySelectionHandle(pdf)", StringComparison.Ordinal),
            "the orange frame must leave Ctrl+Alt drag to the legacy fine-move input path");
        AssertContainsAll(
            overlay,
            "PushSheetOverlayTransformUndo(",
            "TryCommitSheetOverlayTransform(",
            "_sheetOverlayTargetPageFolder",
            "_sheetOverlaySourcePageFolder",
            "HasPendingSheetOverlayTransformGesture",
            "CancelPendingSheetOverlayTransformGesture(postStatus: false)",
            "PrepareSheetOverlayReload(");
        AssertContainsAll(
            undo,
            "SheetOverlayTransformUndo?",
            "undo.TargetPageFolder",
            "undo.OverlayPageFolder",
            "HasSheetOverlayTransformChanged(current, undo.After)",
            "IsSheetOverlayUndoWaitingForBitmap(action)",
            "PublishSheetOverlayTransformChange(restored, status, postStatus: false)");
        AssertTrue(
            main.Contains(
                "_viewport.PrepareSheetOverlayReload(",
                StringComparison.Ordinal) &&
            main.Contains("page.ActiveOverlayId", StringComparison.Ordinal),
            "same-source async reloads must retain binding identity while their bitmap is unavailable");
        AssertTrue(
            panel.Contains("UndoRequested?.Invoke", StringComparison.Ordinal) &&
            properties.Contains("OverlayPropertiesPanel.UndoRequested +=", StringComparison.Ordinal) &&
            properties.Contains("_viewport.UndoLast();", StringComparison.Ordinal),
            "Ctrl+Z from the properties panel must cancel a pending preview or route into viewport history");
        AssertTrue(
            properties.Contains("change.TargetPageFolder", StringComparison.Ordinal) &&
            properties.Contains("change.OverlayPageFolder", StringComparison.Ordinal),
            "persistence must reject stale overlay undo events after target or source page changes");
    }

    public static void SheetOverlayLiveTransformBypassesStaticFrameCache()
    {
        string cache = ReadRepoFile(Path.Combine(
            "Controls",
            "PdfViewport.StaticPageFrameCache.cs"));
        string overlay = ReadViewportSheetOverlaySources();

        AssertTrue(
            cache.Contains("!_sheetOverlayTransformPreviewActive", StringComparison.Ordinal),
            "live transform preview must not rebuild retained page frames on every slider tick");
        AssertTrue(
            overlay.Contains("_zoom * _sheetOverlayScale", StringComparison.Ordinal),
            "overlay quality refresh must use the effective displayed scale");
    }

    public static void SheetOverlayLivePreviewSurvivesQualityBitmapReplacement()
    {
        string overlay = ReadViewportSheetOverlaySources();
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
            "bool preserveTransformGesture",
            "preserveTransformGesture ? liveOffsetXPt : active.OffsetXPt",
            "preserveTransformGesture ? liveOverlayScale : active.Scale",
            "sameBinding &&",
            "HasPendingSheetOverlayTransformGesture");
        AssertTrue(
            !previewApply.Contains("MaybeRequestSheetOverlayRenderScaleRefresh", StringComparison.Ordinal),
            "live slider and handle ticks must defer quality reload until commit");
        AssertTrue(
            refresh.Contains("_sheetOverlayTransformPreviewActive", StringComparison.Ordinal),
            "an already queued bitmap replacement must not start another reload during preview");
    }

    public static void SheetOverlayAutoFitUsesUndoableTransformGateway()
    {
        string autoFit = ReadRepoFile("MainWindow.SheetOverlay.AutoFit.cs");
        string apply = SliceBetween(
            autoFit,
            "private void ApplySheetOverlayAutoFitResult(",
            "private static SheetOverlayAutoFitRunResult RunSheetOverlayAutoFit(");
        string overlay = ReadRepoFile("MainWindow.SheetOverlay.cs");
        string gateway = SliceBetween(
            overlay,
            "private void SetSheetOverlayTransform(",
            "private static PageInfo ReadLatestSheetOverlayPage(");
        int liveCommitIndex = gateway.IndexOf(
            "_viewport.TryCommitSheetOverlayTransform(",
            StringComparison.Ordinal);
        int fallbackSaveIndex = gateway.IndexOf(
            "OurPlanCoreJobStore.SavePageOverlayTransform(",
            StringComparison.Ordinal);

        AssertTrue(
            apply.Contains("SetSheetOverlayTransform(", StringComparison.Ordinal) &&
            !apply.Contains("SavePageOverlayTransform(", StringComparison.Ordinal),
            "auto fit must route a live matching overlay through the shared undoable transform gateway");
        AssertTrue(
            liveCommitIndex >= 0 &&
            fallbackSaveIndex > liveCommitIndex,
            "the shared gateway must try one viewport undo action before its non-live persistence fallback");
    }

    public static void SheetOverlayQualityRefreshFailureKeepsCurrentBitmap()
    {
        string overlay = ReadRepoFile("MainWindow.SheetOverlay.cs");
        string load = SliceBetween(
            overlay,
            "private void LoadSheetOverlay(",
            "private void QueueSheetOverlayLoadForPageOpen(");
        string queue = SliceBetween(
            overlay,
            "private void QueueSheetOverlayLoadForPageOpen(",
            "private bool TryApplyCachedSheetOverlay(");
        string asyncLoad = SliceBetween(
            overlay,
            "private async Task LoadSheetOverlayAsync(",
            "private void ApplySheetOverlayBitmapToViewport(");
        string failure = SliceBetween(
            asyncLoad,
            "if (!result.Ok || result.Layers.Count == 0)",
            "ApplySheetOverlayBitmapToViewport(");

        AssertTrue(
            load.Contains("keepExistingUntilReady);", StringComparison.Ordinal) &&
            queue.Contains("keepExistingUntilReady: false);", StringComparison.Ordinal),
            "quality reloads must forward keep-existing while first page loads must explicitly clear on failure");
        AssertTrue(
            failure.Contains(
                "_viewport.HasSheetOverlayBinding(",
                StringComparison.Ordinal) &&
            failure.Contains("latest.ActiveOverlayId", StringComparison.Ordinal) &&
            failure.Contains("if (!retainedExistingOverlay)", StringComparison.Ordinal) &&
            failure.IndexOf("if (!retainedExistingOverlay)", StringComparison.Ordinal) <
            failure.IndexOf("_viewport.ClearSheetOverlay();", StringComparison.Ordinal),
            "a failed same-binding quality refresh must retain its already visible overlay bitmap");
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

    public static void SheetOverlayLayersShareViewportExportTreeAndDetachedRendering()
    {
        string viewport = ReadViewportSheetOverlaySources();
        string main = string.Concat(
            ReadRepoFile("MainWindow.SheetOverlay.cs"),
            ReadRepoFile("MainWindow.SheetOverlay.Layers.cs"));
        string detached = ReadRepoFile("MainWindow.SheetOverlay.Detached.cs");
        string detachedHost = ReadRepoFile("MainWindow.DetachedSheets.cs");
        string tree = ReadRepoFile("MainWindow.PageTakeoffLegend.cs");
        string storage = ReadRepoFile(Path.Combine(
            "Models",
            "Storage",
            "SheetOverlayLayerStore.cs"));

        AssertContainsAll(
            viewport,
            "SetSheetOverlayLayers(",
            "_sheetOverlayLayersBelow",
            "_sheetOverlayLayersAbove",
            "DrawSheetOverlayLayer(",
            "PreviewSheetOverlayOpacity(",
            "foreach (SheetOverlayBitmapLayer layer in _sheetOverlayLayersBelow)",
            "foreach (SheetOverlayBitmapLayer layer in _sheetOverlayLayersAbove)",
            "_sheetOverlayId");
        AssertContainsAll(
            main,
            "TryBuildSheetOverlayLayers(",
            "page.OverlayLayers.Where(item => item.IsVisible)",
            "foreach (SheetOverlayLayerInfo layer in page.OverlayLayers.Where(item => item.IsVisible))",
            "SetSheetOverlayLayers(layers, page.ActiveOverlayId, page.FolderPath)");
        AssertContainsAll(
            detached,
            "QueueDetachedSheetOverlays(",
            "TryBuildSheetOverlayLayers(",
            "activeOverlayId: page.ActiveOverlayId",
            "window.Viewport.SetSheetOverlayLayers(");
        AssertContainsAll(
            detachedHost,
            "SheetOverlayRenderScaleRefreshRequested +=",
            "QueueDetachedSheetOverlays(window, page, requestedScale)",
            "QueueDetachedSheetOverlays(window, target)");
        AssertTrue(
            tree.Contains("foreach (SheetOverlayLayerInfo layer in page.OverlayLayers)", StringComparison.Ordinal) &&
            tree.Contains("page.ActiveOverlayId", StringComparison.Ordinal) &&
            tree.Contains("layer.Opacity * 100", StringComparison.Ordinal),
            "Pages tree must expose every overlay layer and mark the active layer");
        AssertContainsAll(
            storage,
            "sheet_overlays.json",
            "FromLegacy(",
            "WriteLegacyOverlayMirror",
            "SetOpacity(",
            "Move(",
            "Remove(");
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

    private static string ReadViewportSheetOverlaySources() =>
        string.Concat(
            ReadRepoFile(Path.Combine("Controls", "PdfViewport.SheetOverlay.cs")),
            ReadRepoFile(Path.Combine("Controls", "PdfViewport.SheetOverlayLayers.cs")));

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
