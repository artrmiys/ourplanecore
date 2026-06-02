using OurPlaneCore;
using SkiaSharp;
using System.Reflection;

internal static class TakeoffsTreeRegressionTests
{
    public static void JobSaveDoesNotWriteLegacyPdfSidecar()
    {
        string source = ReadRepoFile("MainWindow.TakeoffsPersistence.cs");
        AssertFalse(
            source.Contains("ProjectFile.Save(", StringComparison.Ordinal),
            "job save must not write legacy PDF sidecar files");
    }

    public static void JobPageLoadGatesLegacyAutoLoad()
    {
        string pageTabs = ReadRepoFile("MainWindow.PageTabs.cs");
        AssertTrue(
            pageTabs.Contains(
                "_currentJob == null && _takeoffItems.Count == 0 && TryAutoLoad()",
                StringComparison.Ordinal),
            "page load must only run legacy PDF auto-load when no job is open");

        string jobLifecycle = ReadRepoFile("MainWindow.JobLifecycle.cs");
        AssertTrue(
            jobLifecycle.Contains(
                "if (_currentJob != null || string.IsNullOrWhiteSpace(_currentPdfPath))",
                StringComparison.Ordinal),
            "TryAutoLoad must refuse to run while a job is open");
    }

    public static void PageOpenDefersHeavyUiWork()
    {
        string pageTabs = ReadRepoFile("MainWindow.PageTabs.cs");
        string loadMethod = SliceMethod(pageTabs, "private void LoadPageIntoViewport(PageInfo page, PdfViewport.ViewState? restoreView)");
        string queueMethod = SliceMethod(pageTabs, "private void QueueDeferredPageOpenWork(");
        string deferredMethod = SliceMethod(pageTabs, "private void RunDeferredPageOpenWork(");
        string prefetchMethod = SliceMethod(pageTabs, "private void QueueNearbyPagePreviewPrefetchDeferred(");
        string nearbyPrefetchMethod = SliceMethod(pageTabs, "private void QueueNearbyPagePreviewPrefetch(PageInfo activePage)");
        string queuePreviewPrefetchAtMethod = SliceMethod(pageTabs, "private static void QueuePreviewPrefetchAt(");
        string queueCleanRenderPrefetchAtMethod = SliceMethod(pageTabs, "private static void QueueCleanRenderPrefetchAt(");

        AssertFalse(
            loadMethod.Contains("TryReadPage(page.FolderPath", StringComparison.Ordinal),
            "page open must not re-read source.json after LoadPageFromTab already loaded the page");

        int loadPage = loadMethod.IndexOf("_viewport.LoadPage(", StringComparison.Ordinal);
        int visibility = loadMethod.IndexOf("ApplyViewportPageTakeoffVisibility(viewportPage)", StringComparison.Ordinal);
        int deferred = loadMethod.IndexOf("QueueDeferredPageOpenWork", StringComparison.Ordinal);
        AssertTrue(
            loadPage >= 0 && visibility > loadPage && deferred > visibility,
            "page open should load the viewport, apply takeoff visibility, then defer slower UI refresh work");

        AssertFalse(
            loadMethod.Contains("LoadSheetOverlay(", StringComparison.Ordinal) ||
            loadMethod.Contains("LoadPageAnnotations(", StringComparison.Ordinal) ||
            loadMethod.Contains("RefreshLoadedPageTakeoffVisuals(", StringComparison.Ordinal) ||
            loadMethod.Contains("SaveAppSettings();", StringComparison.Ordinal),
            "page open should not run overlays, annotations, takeoff tree refresh, or settings save in the immediate path");
        AssertTrue(
            loadMethod.Contains("TryApplyCachedSheetOverlay(viewportPage)", StringComparison.Ordinal),
            "page open should restore cached sheet overlays immediately without starting a heavy overlay render");

        AssertTrue(
            queueMethod.Contains("Dispatcher.BeginInvoke", StringComparison.Ordinal) &&
            queueMethod.Contains("DispatcherPriority.Background", StringComparison.Ordinal) &&
            queueMethod.Contains("RunDeferredPageOpenWork", StringComparison.Ordinal),
            "slow page-open follow-up work should be scheduled at background dispatcher priority");

        AssertTrue(
            deferredMethod.Contains("IsCurrentPageOpen(deferredVersion, viewportPage.FolderPath)", StringComparison.Ordinal) &&
            deferredMethod.Contains("QueueNearbyPagePreviewPrefetchDeferred(deferredVersion, viewportPage)", StringComparison.Ordinal) &&
            deferredMethod.Contains("LoadSheetOverlay(_currentPage ?? viewportPage)", StringComparison.Ordinal) &&
            deferredMethod.Contains("OurPlaneCoreJobStore.LoadPageAnnotations(viewportPage.FolderPath)", StringComparison.Ordinal) &&
            deferredMethod.Contains("RefreshLoadedPageTakeoffVisuals(viewportPage.FolderPath, scaledItems)", StringComparison.Ordinal) &&
            deferredMethod.Contains("SaveAppSettings();", StringComparison.Ordinal),
            "deferred page-open work should keep the previous follow-up operations behind a stale-page guard");

        AssertTrue(
            prefetchMethod.Contains("DispatcherPriority.ContextIdle", StringComparison.Ordinal) &&
            prefetchMethod.Contains("IsCurrentPageOpen(deferredVersion, viewportPage.FolderPath)", StringComparison.Ordinal) &&
            prefetchMethod.Contains("QueueNearbyPagePreviewPrefetch(viewportPage)", StringComparison.Ordinal),
            "nearby preview prefetch should be queued after page-open critical work and guarded against stale pages");
        AssertTrue(
            nearbyPrefetchMethod.Contains("CachedPagesForPreviewPrefetch()", StringComparison.Ordinal) &&
            nearbyPrefetchMethod.Contains("ViewportRenderPolicy.NearbyPagePreviewPrefetchRadius", StringComparison.Ordinal) &&
            nearbyPrefetchMethod.Contains("QueuePreviewPrefetchAt(pages, activeIndex + offset)", StringComparison.Ordinal) &&
            nearbyPrefetchMethod.Contains("QueuePreviewPrefetchAt(pages, activeIndex - offset)", StringComparison.Ordinal) &&
            nearbyPrefetchMethod.Contains("ViewportRenderPolicy.NearbyPageCleanRenderPrefetchRadius", StringComparison.Ordinal) &&
            queuePreviewPrefetchAtMethod.Contains("PrefetchPagePreview", StringComparison.Ordinal) &&
            queueCleanRenderPrefetchAtMethod.Contains("PrefetchCleanLayerRender", StringComparison.Ordinal),
            "nearby page prefetch should warm cheap previews around the active sheet and clean renders only for the closest neighbors");
    }

    public static void ProgrammaticPageSelectionOpensViewportDirectly()
    {
        string pagesTree = ReadRepoFile("MainWindow.PagesTree.cs");
        string pageTabs = ReadRepoFile("MainWindow.PageTabs.cs");
        string selectMethod = SliceMethod(pagesTree, "private void SelectPageByFolder(string folderPath)");
        string openMethod = SliceMethod(pageTabs, "private void OpenPageByFolder(string folderPath)");

        AssertTrue(
            selectMethod.Contains("OpenPageByFolder(folderPath)", StringComparison.Ordinal),
            "programmatic page selection should not rely only on TreeView SelectedItemChanged to open the viewport");
        AssertTrue(
            selectMethod.Contains("selected || OurPlaneCoreJobStore.IsPageFolder(folderPath)", StringComparison.Ordinal),
            "programmatic page selection should still open a valid page when the row is hidden or already selected");
        AssertTrue(
            openMethod.Contains("OurPlaneCoreJobStore.TryReadPage(folderPath)", StringComparison.Ordinal) &&
            openMethod.Contains("OpenPageInActiveTab(page)", StringComparison.Ordinal),
            "direct programmatic page open should read the selected page and load it through the normal page tab path");
        AssertTrue(
            openMethod.Contains("IsSamePageFolder(_currentPage.FolderPath, folderPath)", StringComparison.Ordinal),
            "direct page-open fallback should avoid duplicate reloads when the selection event already opened the page");
    }

    public static void PageTreeClickOpensViewportDirectly()
    {
        string pagesTree = ReadRepoFile("MainWindow.PagesTree.cs");
        string resources = ReadRepoFile("Resources/AppControlResources.xaml");
        string clickMethod = SliceMethod(pagesTree, "private void PagesTree_PreviewMouseLeftButtonDown");
        string openMethod = SliceMethod(pagesTree, "private void SelectPageTreeItemAndOpenIfPage");

        AssertTrue(
            clickMethod.Contains("SelectPageTreeItemAndOpenIfPage(item)", StringComparison.Ordinal),
            "page-tree mouse clicks should not rely only on WPF SelectedItemChanged to open the viewport");
        AssertTrue(
            openMethod.Contains("item.Tag is not PageInfo page", StringComparison.Ordinal) &&
            openMethod.Contains("SelectPagesTreeItemSilently(item)", StringComparison.Ordinal) &&
            openMethod.Contains("OpenPageInActiveTab(page)", StringComparison.Ordinal),
            "direct page-tree click open should select the row and load the clicked sheet through the normal page tab path");
        AssertTrue(
            pagesTree.Contains("ItemsControl.ContainerFromElement(PagesTree, source)", StringComparison.Ordinal) &&
            pagesTree.Contains("Background = Brushes.Transparent", StringComparison.Ordinal),
            "page-tree hit testing should resolve clicks from the full row/header surface, not only text glyphs");
        string hitTestMethod = SliceMethod(pagesTree, "private TreeViewItem? FindPagesTreeItemFromSource");
        int ancestorIndex = hitTestMethod.IndexOf("FindAncestor<TreeViewItem>(source)", StringComparison.Ordinal);
        int containerIndex = hitTestMethod.IndexOf("ItemsControl.ContainerFromElement(PagesTree, source)", StringComparison.Ordinal);
        AssertTrue(
            ancestorIndex >= 0 && containerIndex > ancestorIndex,
            "nested sheet hit testing must prefer the nearest TreeViewItem; ContainerFromElement can resolve a child sheet click to its parent folder");
        AssertTrue(
            resources.Contains("<Setter Property=\"HorizontalContentAlignment\" Value=\"Stretch\"/>", StringComparison.Ordinal),
            "tree rows should stretch their content so blank row area remains clickable");

        int shiftBranch = clickMethod.IndexOf("if ((modifiers & ModifierKeys.Shift)", StringComparison.Ordinal);
        int ctrlBranch = clickMethod.IndexOf("if ((modifiers & ModifierKeys.Control)", shiftBranch, StringComparison.Ordinal);
        int plainBranch = clickMethod.IndexOf("_pagesMultiSelection.Clear();", ctrlBranch, StringComparison.Ordinal);
        AssertTrue(
            shiftBranch >= 0 && ctrlBranch > shiftBranch && plainBranch > ctrlBranch,
            "page-tree click handler should keep distinct shift, control, and plain-click branches");
        string shiftBlock = clickMethod[shiftBranch..ctrlBranch];
        string ctrlBlock = clickMethod[ctrlBranch..plainBranch];
        string plainBlock = clickMethod[plainBranch..];
        AssertTrue(
            shiftBlock.Contains("SelectPagesRange(_pagesRangeAnchorPath, path, additive)", StringComparison.Ordinal) &&
            shiftBlock.Contains("item.IsSelected = true", StringComparison.Ordinal) &&
            !shiftBlock.Contains("SelectPagesTreeItemSilently(item)", StringComparison.Ordinal) &&
            !shiftBlock.Contains("SelectPageTreeItemAndOpenIfPage(item)", StringComparison.Ordinal),
            "Shift range selection should use normal WPF selection and must not direct-open or suppress the selected item event");
        AssertTrue(
            ctrlBlock.Contains("item.IsSelected = true", StringComparison.Ordinal) &&
            !ctrlBlock.Contains("SelectPagesTreeItemSilently(item)", StringComparison.Ordinal) &&
            !ctrlBlock.Contains("SelectPageTreeItemAndOpenIfPage(item)", StringComparison.Ordinal),
            "Ctrl page selection should use normal WPF selection and must not direct-open or suppress the selected item event");
        AssertTrue(
            plainBlock.Contains("SelectPageTreeItemAndOpenIfPage(item)", StringComparison.Ordinal),
            "plain page clicks should still directly open the clicked sheet");

        AssertTrue(
            pagesTree.Contains("private bool HasActivePagesMultiSelection() => _pagesMultiSelection.Count > 1;", StringComparison.Ordinal) &&
            pagesTree.Contains("private void SyncPageTreeNodeForViewportOpen", StringComparison.Ordinal),
            "deferred viewport-to-tree sync should have a guard that preserves active page multi-selection");
        string pageTabs = ReadRepoFile("MainWindow.PageTabs.cs");
        string deferredMethod = SliceMethod(pageTabs, "private void RunDeferredPageOpenWork(");
        AssertTrue(
            deferredMethod.Contains("SyncPageTreeNodeForViewportOpen(viewportPage.FolderPath)", StringComparison.Ordinal) &&
            !deferredMethod.Contains("SelectPageTreeNodeSilently(viewportPage.FolderPath)", StringComparison.Ordinal),
            "deferred page-open sync must not overwrite active page multi-selection");
    }

    public static void PageReloadInvalidatesPreviewPrefetchCache()
    {
        string pagesTree = ReadRepoFile("MainWindow.PagesTree.cs");
        string pageTabs = ReadRepoFile("MainWindow.PageTabs.cs");

        AssertTrue(
            SliceMethod(pagesTree, "private void ReloadPagesTree")
                .Contains("InvalidatePagePreviewPrefetchCache();", StringComparison.Ordinal),
            "page-tree reload should invalidate cached prefetch page lists after import, rename, move, or delete");
        AssertTrue(
            pageTabs.Contains("private void InvalidatePagePreviewPrefetchCache()", StringComparison.Ordinal) &&
            pageTabs.Contains("_pagePreviewPrefetchPages = Array.Empty<PageInfo>();", StringComparison.Ordinal),
            "preview prefetch cache invalidation should clear the cached page list");
    }

    public static void SectionSelectionKeyHandlesLegacyUnfiledItem()
    {
        Type mainWindowType = typeof(MainWindow);
        Type nodeType = mainWindowType.GetNestedType("TakeoffMeasurementNode", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("TakeoffMeasurementNode type missing");
        MethodInfo method = mainWindowType.GetMethod(
                "TakeoffSectionSelectionKey",
                BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("TakeoffSectionSelectionKey method missing");

        var item = new TakeoffItem
        {
            Id = "legacy-item",
            Name = "Legacy Item",
            FolderPath = "",
        };
        var measurement = new Measurement
        {
            Id = "measurement-1",
        };
        object node = Activator.CreateInstance(nodeType, item, measurement)
            ?? throw new InvalidOperationException("Cannot create TakeoffMeasurementNode");

        string key = (string)(method.Invoke(null, [node])
            ?? throw new InvalidOperationException("TakeoffSectionSelectionKey returned null"));

        AssertEqual("legacy:legacy-item|measurement-1", key, "legacy section key");
    }

    public static void JobLoadBuildsTakeoffsBeforeClearingTree()
    {
        string source = ReadRepoFile("MainWindow.JobLifecycle.cs");
        string loadMethod = SliceMethod(source, "private void LoadTakeoffsForJob()");
        int buildIndex = loadMethod.IndexOf("BuildTakeoffChildren(_currentJob.TakeoffsRoot, loadedItems)", StringComparison.Ordinal);
        int clearIndex = buildIndex < 0
            ? -1
            : loadMethod.IndexOf("_takeoffItems.Clear();", buildIndex, StringComparison.Ordinal);

        AssertTrue(buildIndex >= 0, "takeoffs reload must stage tree nodes before swapping UI");
        AssertTrue(clearIndex > buildIndex, "takeoffs reload must not clear the existing tree before staging succeeds");
        AssertTrue(
            loadMethod.Contains("keeping existing tree", StringComparison.Ordinal),
            "takeoffs reload failure should keep the existing tree visible");
    }

    public static void PageClearDoesNotClearLoadedTakeoffs()
    {
        string source = ReadRepoFile("MainWindow.PagesUtilities.cs");
        string method = SliceMethod(source, "private void ClearCurrentPageIfAffected(string affectedPath)");

        AssertTrue(
            method.Contains("_currentPage = null;", StringComparison.Ordinal) &&
            method.Contains("_viewport.ClearPage();", StringComparison.Ordinal),
            "affected page cleanup should still clear the active page viewport");
        AssertFalse(
            method.Contains("_takeoffItems.Clear();", StringComparison.Ordinal) ||
            method.Contains("TakeoffsTree.Items.Clear();", StringComparison.Ordinal) ||
            method.Contains("ResetTakeoffTreeItemIndex();", StringComparison.Ordinal),
            "affected page cleanup must not erase the loaded takeoff tree/session state");
    }

    public static void TakeoffSectionMenusAreBuiltLazily()
    {
        string source = ReadRepoFile("MainWindow.TakeoffSections.cs");
        AssertTrue(
            source.Contains("TakeoffSection_ContextMenuOpening", StringComparison.Ordinal),
            "takeoff section menus should be built on open");
        AssertFalse(
            source.Contains("tvi.ContextMenu = BuildTakeoffSectionContextMenu", StringComparison.Ordinal),
            "takeoff section menus must not be built while loading tree rows");
    }

    public static void JoistDirectionCanBeResetFromSectionMenu()
    {
        string sections = ReadRepoFile("MainWindow.TakeoffSections.cs");
        string normalizedSections = sections.Replace("\r\n", "\n");
        AssertTrue(
            sections.Contains("\"Set / Reset Joist Direction\"", StringComparison.Ordinal) &&
            normalizedSections.Contains("\"Set / Reset Joist Direction\",\n            isAreaSection", StringComparison.Ordinal) &&
            sections.Contains("SetJoistDirectionForSection(item, measurement)", StringComparison.Ordinal) &&
            sections.Contains("\"Set Direction for All Areas\"", StringComparison.Ordinal) &&
            sections.Contains("SetJoistDirectionForAllAreas(item, measurement)", StringComparison.Ordinal),
            "area section context menu should expose a direct joist direction reset command");

        string joists = ReadRepoFile("MainWindow.TakeoffsJoists.cs");
        AssertTrue(
            joists.Contains("private void SetJoistDirectionForSection", StringComparison.Ordinal) &&
            joists.Contains("private void SetJoistDirectionForAllAreas", StringComparison.Ordinal) &&
            joists.Contains("_pendingJoistDirectionApplyTargets", StringComparison.Ordinal) &&
            joists.Contains("item.JoistDirectionDegrees = directionDegrees", StringComparison.Ordinal) &&
            joists.Contains("_takeoffSectionMultiSelection.Contains(TakeoffSectionSelectionKey(node))", StringComparison.Ordinal) &&
            joists.Contains("OpenPageInActiveTab(page)", StringComparison.Ordinal) &&
            joists.Contains("BeginJoistDirectionCapture(item, area)", StringComparison.Ordinal) &&
            joists.Contains("Dispatcher.InvokeAsync(StartCapture)", StringComparison.Ordinal),
            "section reset command should open the area sheet and restart two-point direction capture");
        AssertFalse(
            joists.Contains("area.JoistDirectionLocked = false;", StringComparison.Ordinal),
            "starting a reset should not clear the existing joist direction before new points are captured");

        string viewportCommands = ReadRepoFile(Path.Combine("Controls", "PdfViewport.ViewCommands.cs"));
        AssertTrue(
            viewportCommands.Contains("public bool BeginJoistDirectionCapture", StringComparison.Ordinal) &&
            viewportCommands.Contains("Joist direction was not started", StringComparison.Ordinal),
            "viewport should report whether joist direction capture actually started");

        string viewportMenu = ReadRepoFile("MainWindow.ViewportContextMenu.cs");
        AssertTrue(
            viewportMenu.Contains("\"Set / Reset Joist Direction\"", StringComparison.Ordinal) &&
            viewportMenu.Contains("SetJoistDirectionForSection(item, measurement)", StringComparison.Ordinal) &&
            viewportMenu.Contains("\"Set Direction for All Areas\"", StringComparison.Ordinal) &&
            viewportMenu.Contains("SetJoistDirectionForAllAreas(item, measurement)", StringComparison.Ordinal),
            "viewport area context menu should expose direct joist direction reset");

        string takeoffMenu = ReadRepoFile("MainWindow.TakeoffsMenus.cs");
        AssertTrue(
            takeoffMenu.Contains("\"Set Direction for All Areas\"", StringComparison.Ordinal) &&
            takeoffMenu.Contains("SetJoistDirectionForAllAreasFromSelectedLine(tvi, item)", StringComparison.Ordinal),
            "takeoff item context menu should expose all-area joist direction");
    }

    public static void SettingsManagerFolderTemplateEditsAutoPersist()
    {
        string source = ReadRepoFile("MainWindow.SettingsManager.cs");
        AssertTrue(
            source.Contains("private void PersistFolderTemplateEditorChange", StringComparison.Ordinal) &&
            source.Contains("SettingsPresetStore.SaveGlobal(_ftConfig)", StringComparison.Ordinal) &&
            source.Contains("InstallWorkingProviders();", StringComparison.Ordinal),
            "settings manager should have a shared global autosave path for folder template edits");
        AssertTrue(
            source.Contains("SyncPageFolders(); PersistFolderTemplateEditorChange", StringComparison.Ordinal) &&
            source.Contains("PersistFolderTemplateEditorChange($\"Auto Tree", StringComparison.Ordinal),
            "page folder and auto tree edit actions should autosave as the global default");
    }

    public static void TakeoffTemplatePresetsAndCollapsedDepthAreWired()
    {
        string source = ReadRepoFile("MainWindow.Templates.cs");
        AssertTrue(
            source.Contains("AddTakeoffTemplatePreset", StringComparison.Ordinal) &&
            source.Contains("_takeoffTemplateConfig.AddTemplateCopy", StringComparison.Ordinal) &&
            source.Contains("SelectTakeoffTemplatePresetFromCombo", StringComparison.Ordinal),
            "takeoff template settings should expose named template preset creation and switching");
        AssertTrue(
            source.Contains("BuildTemplateTreeItem(root, tree, allowCreate, depth: 0)", StringComparison.Ordinal) &&
            source.Contains("IsExpanded = depth == 0", StringComparison.Ordinal) &&
            source.Contains("BuildTemplateTreeItem(child, ownerTree, allowCreate, depth + 1)", StringComparison.Ordinal),
            "takeoff template tree should expand top-level folders while nested folders start collapsed");
    }

    public static void FastRefreshDisabledForDataSafety()
    {
        string source = ReadRepoFile("MainWindow.TakeoffsTreeFastRefresh.cs");
        AssertTrue(
            source.Contains("private static readonly bool FastTakeoffsTreeRefreshEnabled = false;", StringComparison.Ordinal),
            "fast takeoffs tree refresh must stay disabled until the data-loss regression is covered by UI tests");
    }

    public static void TakeoffSelectionUsesTargetedUiRefresh()
    {
        string treeSource = ReadRepoFile("MainWindow.TakeoffsTree.cs");
        string selectionMethod = SliceMethod(treeSource, "private void TakeoffsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)");

        AssertFalse(
            selectionMethod.Contains("RefreshPagesTakeoffIndicators();", StringComparison.Ordinal),
            "plain takeoff selection must not rebuild every page-linked takeoff row");
        AssertTrue(
            selectionMethod.Contains("RefreshPageTakeoffIndicatorsForActiveChange(previousActiveTakeoffFolder, selectedTakeoffs)", StringComparison.Ordinal) &&
            selectionMethod.Contains("RefreshPageTakeoffIndicatorsForActiveChange(previousActiveTakeoffFolder, [node.Item])", StringComparison.Ordinal),
            "takeoff selection should refresh only the pages touched by previous/new active takeoffs");
        AssertTrue(
            selectionMethod.Contains("UpdateTotalDisplay(refreshEstimate: false)", StringComparison.Ordinal),
            "takeoff selection should not rebuild the full estimate table when measurements did not change");
        AssertFalse(
            selectionMethod.Contains("RefreshActiveTakeoffVisuals();", StringComparison.Ordinal),
            "plain takeoff selection must not rebuild every takeoff tree row");
        AssertTrue(
            selectionMethod.Contains("RefreshActiveTakeoffVisualsForPaths(", StringComparison.Ordinal),
            "plain takeoff selection should repaint only the previous and current active rows");

        string pagesSource = ReadRepoFile("MainWindow.PagesTree.cs");
        AssertTrue(
            pagesSource.Contains("private void RefreshPageTakeoffIndicatorsForActiveChange(", StringComparison.Ordinal) &&
            pagesSource.Contains("RefreshPageTreeRowsByFolderKeys(pageFolders", StringComparison.Ordinal),
            "targeted selection refresh should repaint touched page rows without rebuilding linked takeoff nodes");

        string helpersSource = ReadRepoFile("MainWindow.EstimateRows.cs");
        AssertTrue(
            helpersSource.Contains("private void UpdateTotalDisplay(bool refreshEstimate = true)", StringComparison.Ordinal) &&
            helpersSource.Contains("if (refreshEstimate)", StringComparison.Ordinal),
            "total display should allow selection-only updates without rebuilding estimates");
    }

    public static void TakeoffCopyUsesIncrementalTreeRefresh()
    {
        string clipboardSource = ReadRepoFile("MainWindow.TakeoffsClipboard.cs");
        string duplicateMethod = SliceMethod(clipboardSource, "private void DuplicateTakeoffNode(TreeViewItem item)");
        string dropMethod = SliceMethod(clipboardSource, "private void RunTakeoffDrop(");

        AssertTrue(
            duplicateMethod.Contains("TryApplyTakeoffStructureCopyFast(changed)", StringComparison.Ordinal),
            "duplicate should append copied takeoff nodes to the existing tree before falling back to a full reload");
        AssertTrue(
            dropMethod.Contains("TryApplyTakeoffStructureCopyFast(changed, timings)", StringComparison.Ordinal),
            "copy/paste should append copied takeoff nodes to the existing tree before falling back to a full reload");
        AssertTrue(
            dropMethod.Contains("CopyNodesPreserveDisplayName(copyEntries, targetFolder)", StringComparison.Ordinal),
            "copy/paste should copy selected nodes in one batch so order-index writes do not rescan the target folder for every node");

        string fastSource = ReadRepoFile("MainWindow.TakeoffsTreeFastRefresh.cs");
        string copyMethod = SliceMethod(fastSource, "private bool TryApplyTakeoffStructureCopyFast(");
        AssertTrue(
            copyMethod.Contains("parent.Items.Add(node);", StringComparison.Ordinal) &&
            copyMethod.Contains("RegisterTakeoffTreeItemSubtree(node);", StringComparison.Ordinal) &&
            copyMethod.Contains("_takeoffItems.Add(item);", StringComparison.Ordinal),
            "copy fast path should add only the new copied UI subtrees and model items");
        AssertFalse(
            copyMethod.Contains("FastTakeoffsTreeRefreshEnabled", StringComparison.Ordinal),
            "copy fast path must not re-enable the broader move/reorder fast refresh that is disabled for data safety");

        string deferredSource = ReadRepoFile("MainWindow.PagesTreeDeferredRefresh.cs");
        AssertTrue(
            fastSource.Contains("RefreshPageTakeoffIndicatorsForFoldersOrDefer(pageFolders)", StringComparison.Ordinal) &&
            deferredSource.Contains("MaxImmediatePageTakeoffIndicatorRefreshCount", StringComparison.Ordinal) &&
            deferredSource.Contains("MarkPageTakeoffIndicatorsDirty(pageFolders)", StringComparison.Ordinal),
            "large copy refresh should defer page-linked row rebuilds instead of rebuilding every sheet synchronously");
    }

    public static void StaleDragRowReloadsTakeoffsTree()
    {
        string treeSource = ReadRepoFile("MainWindow.TakeoffsTree.cs");
        string dragSource = ReadRepoFile("MainWindow.TakeoffsDragDrop.cs");
        string selectionSource = ReadRepoFile("MainWindow.TakeoffsSelectionHelpers.cs");

        AssertTrue(
            treeSource.Contains("TryRefreshStaleTakeoffTreeNode(item)", StringComparison.Ordinal),
            "mouse down on a stale takeoff row should refresh the tree before arming drag");
        AssertTrue(
            dragSource.Contains("TryRefreshStaleTakeoffTreeNode(item)", StringComparison.Ordinal),
            "drag start must also guard against stale takeoff rows");
        AssertTrue(
            selectionSource.Contains("Takeoffs tree row referenced missing path", StringComparison.Ordinal) &&
            selectionSource.Contains("LoadTakeoffsForJob();", StringComparison.Ordinal) &&
            selectionSource.Contains("TakeoffMeasurementNode node => node.Item.FolderPath", StringComparison.Ordinal),
            "stale takeoff rows should reload from disk for item, folder, and section rows");
    }

    public static void TakeoffDragStateResetsOnRelease()
    {
        string source = ReadRepoFile("MainWindow.TakeoffsDragDrop.cs");
        string mouseMove = SliceMethod(source, "private void TakeoffsTree_MouseMove(object sender, MouseEventArgs e)");

        AssertTrue(
            mouseMove.Contains("e.LeftButton != MouseButtonState.Pressed", StringComparison.Ordinal) &&
            mouseMove.Contains("ResetTakeoffsDragState();", StringComparison.Ordinal),
            "takeoffs drag state must reset when the mouse is released before a drag starts");
        AssertTrue(
            source.Contains("private void DoTakeoffsDragDrop(object payload, DragDropEffects effects)", StringComparison.Ordinal) &&
            source.Contains("finally", StringComparison.Ordinal) &&
            source.Contains("ClearTakeoffFolderDropCue();", StringComparison.Ordinal),
            "takeoffs drag/drop should always clear cues and drag state");
    }

    public static void LoadTakeoffItemsKeepsNestedMixedTreeItems()
    {
        WithTempJob("Nested Takeoffs", job =>
        {
            string wallsFolder = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "Walls");
            string firstFloorFolder = OurPlaneCoreJobStore.CreateTakeoffFolder(job, wallsFolder, "1st Floor");
            string assemblyFolder = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "Assemblies");

            TakeoffItem rootItem = CreateMeasuredTakeoffItem(job, job.TakeoffsRoot, "Root Line", "line");
            TakeoffItem nestedWallItem = CreateMeasuredTakeoffItem(job, firstFloorFolder, "Exterior Walls", "line");
            TakeoffItem nestedAreaItem = CreateMeasuredTakeoffItem(job, assemblyFolder, "Deck Area", "area");

            IReadOnlyList<TakeoffItem> loaded = OurPlaneCoreJobStore.LoadTakeoffItems(job);
            string loadedNames = string.Join(",", loaded.Select(item => item.Name).OrderBy(name => name));

            AssertEqual("3", loaded.Count.ToString(), "loaded takeoff item count");
            AssertEqual("Deck Area,Exterior Walls,Root Line", loadedNames, "loaded takeoff names");
            AssertTrue(ContainsFolder(loaded, rootItem.FolderPath), "root takeoff item should load");
            AssertTrue(ContainsFolder(loaded, nestedWallItem.FolderPath), "nested wall takeoff item should load");
            AssertTrue(ContainsFolder(loaded, nestedAreaItem.FolderPath), "nested area takeoff item should load");
            AssertFalse(loaded.Any(item => string.Equals(item.Name, "Walls", StringComparison.OrdinalIgnoreCase)), "plain folder must not load as an item");
            AssertFalse(loaded.Any(item => string.Equals(item.Name, "1st Floor", StringComparison.OrdinalIgnoreCase)), "nested plain folder must not load as an item");
            AssertTrue(loaded.All(item => item.Measurements.Count == 1), "loaded items should keep saved measurements");
            AssertTrue(loaded.All(item => Directory.Exists(item.FolderPath)), "loaded items should keep real folder paths");
        });
    }

    public static void LoadTakeoffItemsKeepsSiblingsWhenMeasurementsJsonIsCorrupt()
    {
        WithTempJob("Corrupt Takeoff Sibling", job =>
        {
            string wallsFolder = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "Walls");
            TakeoffItem goodBefore = CreateMeasuredTakeoffItem(job, wallsFolder, "Good Before", "line");
            TakeoffItem corrupt = CreateMeasuredTakeoffItem(job, wallsFolder, "Corrupt Item", "line");
            TakeoffItem goodAfter = CreateMeasuredTakeoffItem(job, wallsFolder, "Good After", "line");

            string corruptMeasurementsPath = Path.Combine(corrupt.FolderPath, "measurements.json");
            File.WriteAllText(corruptMeasurementsPath, "{ bad json");
            _ = OurPlaneCoreJobStore.DrainCorruptJsonFiles();

            IReadOnlyList<TakeoffItem> loaded = OurPlaneCoreJobStore.LoadTakeoffItems(job);
            IReadOnlyList<string> quarantined = OurPlaneCoreJobStore.DrainCorruptJsonFiles();

            AssertEqual("3", loaded.Count.ToString(), "corrupt item should not drop sibling items");
            AssertTrue(ContainsFolder(loaded, goodBefore.FolderPath), "first sibling should stay visible");
            AssertTrue(ContainsFolder(loaded, corrupt.FolderPath), "corrupt measurement item should stay visible");
            AssertTrue(ContainsFolder(loaded, goodAfter.FolderPath), "second sibling should stay visible");
            AssertEqual("1", ItemByFolder(loaded, goodBefore.FolderPath).Measurements.Count.ToString(), "first sibling measurements");
            AssertEqual("0", ItemByFolder(loaded, corrupt.FolderPath).Measurements.Count.ToString(), "corrupt item should load empty measurements");
            AssertEqual("1", ItemByFolder(loaded, goodAfter.FolderPath).Measurements.Count.ToString(), "second sibling measurements");
            AssertFalse(File.Exists(corruptMeasurementsPath), "corrupt measurements should be moved away");
            AssertEqual("1", Directory.GetFiles(corrupt.FolderPath, "measurements.json.corrupt-*").Length.ToString(), "corrupt measurements quarantine file count");
            AssertTrue(quarantined.Any(path => path.Contains("measurements.json", StringComparison.OrdinalIgnoreCase)), "corrupt measurements should be reported");
        });
    }

    public static void PageMeasurementLookupEnabledForLargeTreeRefresh()
    {
        string source = ReadRepoFile("MainWindow.PageMeasurementIndex.cs");
        AssertTrue(
            source.Contains("private static readonly bool PageMeasurementLookupEnabled = true;", StringComparison.Ordinal),
            "page measurement lookup should stay enabled so large jobs do not scan every takeoff for every page row");
        AssertTrue(
            source.Contains("_pageMeasurementLookup = null;", StringComparison.Ordinal),
            "page measurement lookup must remain scoped so stale measurement data is not reused after edits");
    }

    public static void PagesDropUsesBatchMoveAndSilentRefresh()
    {
        string actions = ReadRepoFile("MainWindow.PagesNodeActions.cs");
        string drag = ReadRepoFile("MainWindow.PagesDragDrop.cs");
        string references = ReadRepoFile("MainWindow.PagePathReferences.cs");
        string flushMethod = SliceMethod(drag, "private void FlushPendingPagesTreeDropRefresh()");

        AssertTrue(
            actions.Contains("OurPlaneCoreJobStore.MoveNodes(validEntries.Select(entry => entry.SourcePath), targetFolder)", StringComparison.Ordinal) &&
            drag.Contains("OurPlaneCoreJobStore.MoveNodes(moveEntries.Select(entry => entry.SourcePath), targetParent)", StringComparison.Ordinal) &&
            drag.Contains("OurPlaneCoreJobStore.MoveNodes(moveEntries.Select(entry => entry.SourcePath), root)", StringComparison.Ordinal),
            "Pages cut/drop should batch filesystem moves instead of moving each selected page/folder separately");
        AssertTrue(
            references.Contains("private bool UpdatePageReferencesForMovedPaths", StringComparison.Ordinal) &&
            references.Contains("RebaseMeasurementPageFolderReferences(normalizedMoves)", StringComparison.Ordinal),
            "Pages bulk moves should rebase page refs in one pass");
        AssertTrue(
            flushMethod.Contains("ReloadPagesTree(selectPath, selectSilently: true)", StringComparison.Ordinal) &&
            !flushMethod.Contains("PagesTree.UpdateLayout()", StringComparison.Ordinal) &&
            !flushMethod.Contains("PagesTree.Items.Refresh()", StringComparison.Ordinal),
            "Pages drop refresh should not synchronously relayout or open the moved sheet through selection change");
    }

    public static void PageRepairDoesNotLeafRebaseNonEmptyReferences()
    {
        string source = ReadRepoFile("MainWindow.JobLifecycle.cs");
        string repairMethod = SliceMethod(source, "private int RepairMeasurementPageFolderReferences()");
        string nonEmptyBranch = repairMethod[
            repairMethod.IndexOf("string oldPath = NormalizePageReferencePath(measurement.PageFolder);", StringComparison.Ordinal)..];

        AssertTrue(
            nonEmptyBranch.Contains("if (!pagesByPath.TryGetValue(oldPath, out matchedPage))", StringComparison.Ordinal),
            "non-empty PageFolder repair must require an exact page path match");
        AssertFalse(
            nonEmptyBranch.Contains("ResolveMeasurementPage(\r\n                    oldPath", StringComparison.Ordinal) ||
            nonEmptyBranch.Contains("ResolveMeasurementPage(\n                    oldPath", StringComparison.Ordinal),
            "non-empty PageFolder repair must not move measurements by leaf-name fallback");
    }

    public static void TreeDragUsesMouseDownAnchor()
    {
        string pagesTree = ReadRepoFile("MainWindow.PagesTree.cs");
        string pagesDrag = ReadRepoFile("MainWindow.PagesDragDrop.cs");

        AssertTrue(
            pagesTree.Contains("_pagesDragItem = item;", StringComparison.Ordinal) &&
            pagesTree.Contains("_pagesDragArmed = CanArmPagesTreeDrag(item, e.OriginalSource as DependencyObject);", StringComparison.Ordinal),
            "pages drag must remember the row under the mouse before WPF selection changes");
        AssertTrue(
            pagesDrag.Contains("(_pagesDragItem ?? PagesTree.SelectedItem) is not TreeViewItem item", StringComparison.Ordinal),
            "pages drag must start from the mouse-down row, not a stale selected row");
    }

    public static void NestedTreeRowsResolveToOwningDropTargets()
    {
        string pagesDrag = ReadRepoFile("MainWindow.PagesDragDrop.cs");
        string pagesActions = ReadRepoFile("MainWindow.PagesNodeActions.cs");
        string takeoffsDrag = ReadRepoFile("MainWindow.TakeoffsDragDrop.cs");
        string takeoffsClipboard = ReadRepoFile("MainWindow.TakeoffsClipboard.cs");

        AssertTrue(
            pagesDrag.Contains("ResolvePagesClipboardDropTarget", StringComparison.Ordinal) &&
            pagesDrag.Contains("PageTakeoffNode node => FindPageTreeItemByFolder(node.Page.FolderPath) ?? targetItem", StringComparison.Ordinal) &&
            pagesDrag.Contains("PageOverlayNode overlay => FindPageTreeItemByFolder(overlay.Page.FolderPath) ?? targetItem", StringComparison.Ordinal),
            "pages drag/drop must treat nested legend/overlay rows as their owning sheet");
        AssertTrue(
            pagesActions.Contains("PageTakeoffNode node => Path.GetDirectoryName(node.Page.FolderPath)", StringComparison.Ordinal) &&
            pagesActions.Contains("PageOverlayNode overlay => Path.GetDirectoryName(overlay.Page.FolderPath)", StringComparison.Ordinal),
            "pages paste target lookup must not reject nested sheet rows");
        AssertTrue(
            takeoffsDrag.Contains("ResolveTakeoffsClipboardDropTarget", StringComparison.Ordinal) &&
            takeoffsDrag.Contains("TakeoffMeasurementNode node => FindTakeoffTreeItemByFolder(node.Item.FolderPath) ?? targetItem", StringComparison.Ordinal),
            "takeoffs drag/drop must treat nested measurement rows as their owning takeoff item");
        AssertTrue(
            takeoffsClipboard.Contains("TakeoffMeasurementNode node => Path.GetDirectoryName(node.Item.FolderPath)", StringComparison.Ordinal),
            "takeoffs paste target lookup must not send nested measurement-row drops to root");
    }

    public static void MeasurementPasteNewTakeoffKeepsSourceName()
    {
        string clipboard = ReadRepoFile("MainWindow.MeasurementClipboard.cs");
        AssertTrue(
            clipboard.Contains("MeasurementPasteTargetDisplayName(entry.SourceTakeoffName, measurementType)", StringComparison.Ordinal),
            "measurement paste into new takeoff should route through the exact-name helper");
        AssertFalse(
            clipboard.Contains("SourceTakeoffName} Copy", StringComparison.Ordinal) ||
            clipboard.Contains("SourceTakeoffName + \" Copy\"", StringComparison.Ordinal),
            "measurement paste into new takeoff must not append Copy to the source takeoff name");
        AssertFalse(
            clipboard.Contains("MeasurementTypeTitle(measurementType)} Paste", StringComparison.Ordinal),
            "measurement paste fallback name must not append Paste as a visible suffix");
    }

    public static void TakeoffsTreeRefreshButtonIsWired()
    {
        string xaml = ReadRepoFile("MainWindow.xaml");
        string commands = ReadRepoFile("MainWindow.TakeoffsCommands.cs");
        string palette = ReadRepoFile("MainWindow.CommandPalette.cs");

        AssertTrue(
            xaml.Contains("x:Name=\"BtnRefreshTakeoffsTree\"", StringComparison.Ordinal) &&
            xaml.Contains("Click=\"BtnRefreshTakeoffsTree_Click\"", StringComparison.Ordinal) &&
            xaml.Contains("ToolTip=\"Refresh Takeoffs tree\"", StringComparison.Ordinal),
            "Takeoffs tree header must expose an R refresh button like the Pages tree");
        AssertTrue(
            commands.Contains("LoadTakeoffsForJob();", StringComparison.Ordinal) &&
            commands.Contains("ClearTakeoffPositionDropCue();", StringComparison.Ordinal) &&
            commands.Contains("ClearTakeoffFolderDropCue();", StringComparison.Ordinal) &&
            commands.Contains("Takeoffs tree refreshed.", StringComparison.Ordinal),
            "Takeoffs refresh button should use the existing safe reload path and clear drag cues");
        AssertTrue(
            palette.Contains("\"takeoffs.refresh\"", StringComparison.Ordinal) &&
            palette.Contains("BtnRefreshTakeoffsTree_Click(this, new RoutedEventArgs())", StringComparison.Ordinal),
            "Command Palette should expose the Takeoffs refresh action");
    }

    public static void BookmarksDockPanelAndShortcutAreWired()
    {
        string xaml = ReadRepoFile("MainWindow.xaml");
        string mainWindowResources = ReadRepoFile("Resources/MainWindowResources.xaml");
        string bookmarks = ReadRepoFile("MainWindow.Bookmarks.cs");
        string shortcuts = ReadRepoFile("MainWindow.Shortcuts.cs");

        AssertTrue(
            mainWindowResources.Contains("x:Key=\"BookmarkDockToggleButton\"", StringComparison.Ordinal) &&
            xaml.Contains("x:Name=\"BtnDockBookmarksBelowPages\"", StringComparison.Ordinal) &&
            xaml.Contains("Checked=\"BookmarkDockToggle_Changed\"", StringComparison.Ordinal) &&
            xaml.Contains("x:Name=\"BookmarksDockContentHost\"", StringComparison.Ordinal) &&
            !xaml.Contains("x:Name=\"BtnToggleBookmarksDock\"", StringComparison.Ordinal),
            "left panel must expose Bookmarks docking as a compact circle, not a separate large button");
        AssertTrue(
            bookmarks.Contains("ApplyBookmarksDockMode", StringComparison.Ordinal) &&
            bookmarks.Contains("BuildBookmarksTabHeader", StringComparison.Ordinal) &&
            bookmarks.Contains("Text = \"Bkm\"", StringComparison.Ordinal) &&
            bookmarks.Contains("CreateBookmarkDockToggle", StringComparison.Ordinal) &&
            bookmarks.Contains("SetBookmarksDockToggleState(docked)", StringComparison.Ordinal) &&
            bookmarks.Contains("PagesSideTabs.Items.Remove(_bookmarksTab)", StringComparison.Ordinal) &&
            bookmarks.Contains("BookmarksDockContentHost.Content = _bookmarkPanel", StringComparison.Ordinal) &&
            bookmarks.Contains("PagesSideTabs.SelectedItem = _bookmarksTab", StringComparison.Ordinal) &&
            bookmarks.Contains("Bookmarks returned to the Pages tabs.", StringComparison.Ordinal) &&
            bookmarks.Contains("ColumnHeaderContainerStyle = BuildHiddenBookmarkColumnHeaderStyle()", StringComparison.Ordinal),
            "Bookmarks tab content must move into and back out of the docked panel without duplicating the list");
        AssertTrue(
            shortcuts.Contains("string.Equals(sequence, \"bk\"", StringComparison.Ordinal) &&
            shortcuts.Contains("AddBookmarkFromShortcut();", StringComparison.Ordinal),
            "global BK sequence must invoke AddBookmarkFromShortcut");
    }

    public static void TreeSearchBulkVisibilityAndViewportMarkupSelectionAreWired()
    {
        string xaml = ReadRepoFile("MainWindow.xaml");
        string treeSearch = ReadRepoFile("MainWindow.TreeSearch.cs");
        string takeoffsClipboard = ReadRepoFile("MainWindow.TakeoffsClipboard.cs");
        string pageLegend = ReadPageTakeoffLegendSources();
        string viewportSelectionState = ReadRepoFile("Controls/PdfViewport.SelectionState.cs");
        string viewportMeasurementApi = ReadRepoFile("Controls/PdfViewport.MeasurementApi.cs");

        AssertTrue(
            xaml.Contains("PagesTreeSearchBox", StringComparison.Ordinal) &&
            xaml.Contains("TakeoffsTreeSearchBox", StringComparison.Ordinal) &&
            treeSearch.Contains("ApplyPagesTreeSearchFilter", StringComparison.Ordinal) &&
            treeSearch.Contains("ApplyTakeoffsTreeSearchFilter", StringComparison.Ordinal),
            "Pages and Takeoffs trees must expose search boxes with filter handlers");
        AssertTrue(
            takeoffsClipboard.Contains("FirstSelectedTakeoffTreeItem", StringComparison.Ordinal) &&
            takeoffsClipboard.Contains("DeleteTakeoffNodes(selected)", StringComparison.Ordinal),
            "Takeoffs Delete key must fall back to the multi-selection anchor");
        AssertTrue(
            pageLegend.Contains("SetSelectedPageTakeoffVisibility", StringComparison.Ordinal) &&
            pageLegend.Contains("Hide {selectedCount} Selected on This Sheet", StringComparison.Ordinal) &&
            pageLegend.Contains("Show {selectedCount} Selected on This Sheet", StringComparison.Ordinal),
            "page linked takeoff menu must support bulk hide and show");
        AssertTrue(
            viewportSelectionState.Contains("SetSelectedAnnotations", StringComparison.Ordinal) &&
            viewportSelectionState.Contains("DeletePageAnnotations(selected)", StringComparison.Ordinal) &&
            viewportMeasurementApi.Contains("HideVisibleRulerAnnotationsOnActivePage", StringComparison.Ordinal) &&
            viewportMeasurementApi.Contains("ShowAllRulerAnnotationsOnActivePage", StringComparison.Ordinal),
            "viewport markups must support multi-select delete and snapshot-based ruler hiding");
    }

    public static void TakeoffFolderRandomColorsAreWired()
    {
        string menus = ReadRepoFile("MainWindow.TakeoffsMenus.cs");
        string colors = ReadRepoFile("MainWindow.TakeoffsRandomColors.cs");

        AssertTrue(
            menus.Contains("Random Colors for Items", StringComparison.Ordinal) &&
            menus.Contains("RandomizeTakeoffItemColors(tvi)", StringComparison.Ordinal),
            "takeoff folder context menu must expose random colors for nested takeoff items");
        AssertTrue(
            colors.Contains("DistinctColorableTakeoffItems(TakeoffItemsForSelection(anchor))", StringComparison.Ordinal) &&
            colors.Contains("measurement.Color = color", StringComparison.Ordinal) &&
            colors.Contains("OurPlaneCoreJobStore.SaveTakeoffItem(item)", StringComparison.Ordinal) &&
            colors.Contains("_viewport.RefreshMeasurementDisplay()", StringComparison.Ordinal),
            "random takeoff colors must update item color, measurement colors, persisted sidecars, and viewport paint");
    }

    public static void PageTakeoffLayersAndAltVertexModeAreWired()
    {
        string pageLegend = ReadPageTakeoffLegendSources();
        AssertTrue(
            pageLegend.Contains("RenameLinkedPageTakeoff", StringComparison.Ordinal) &&
            pageLegend.Contains("LayerOrderedTakeoffsForPage", StringComparison.Ordinal) &&
            pageLegend.Contains("Move Backward", StringComparison.Ordinal) &&
            pageLegend.Contains("Move Forward", StringComparison.Ordinal) &&
            pageLegend.Contains("PageTakeoffLayerOrderStore.Save", StringComparison.Ordinal),
            "left page takeoff menu must support rename plus draw-layer forward/back independent of legend");

        string viewportInput = ReadRepoFile("Controls/PdfViewport.Input.cs");
        string selectionEditing = ReadRepoFile("Controls/PdfViewport.SelectionEditing.cs");
        string boxSelection = ReadRepoFile("Controls/PdfViewport.BoxSelection.cs");
        string hitTesting = ReadRepoFile("Controls/PdfViewport.HitTesting.cs");
        string areaCut = ReadRepoFile("Controls/PdfViewport.AreaCutTools.cs");
        AssertTrue(
            viewportInput.Contains("BeginVertexBoxSelection(pdf)", StringComparison.Ordinal) &&
            selectionEditing.Contains("TryHitSelectedMeasurementSelectedVertex(pdf", StringComparison.Ordinal) &&
            selectionEditing.Contains("TryHitVertexOnSelectedMeasurement(pdf", StringComparison.Ordinal) &&
            selectionEditing.Contains("if (IsVertexModifierActive() &&", StringComparison.Ordinal) &&
            hitTesting.Contains("TryHitSelectedVertexOnMeasurement", StringComparison.Ordinal) &&
            viewportInput.Contains("Cursor = Cursors.Cross", StringComparison.Ordinal) &&
            !viewportInput.Contains("Cursors.SizeAll", StringComparison.Ordinal) &&
            boxSelection.Contains("Alt-click or Alt-box handles to toggle", StringComparison.Ordinal) &&
            !boxSelection.Contains("Alt+Ctrl", StringComparison.Ordinal) &&
            !boxSelection.Contains("Alt+Shift", StringComparison.Ordinal),
            "selected measurement handles must support direct hot-grip drag while Alt remains the vertex selection modifier");

        AssertTrue(
            areaCut.Contains("CutLinePiecesByPolygon", StringComparison.Ordinal) &&
            areaCut.Contains("CloneLineMeasurement", StringComparison.Ordinal) &&
            areaCut.Contains("PushMixedMeasurementUndo", StringComparison.Ordinal) &&
            areaCut.Contains("NotifyMeasurementsRemoved(removedLines)", StringComparison.Ordinal) &&
            areaCut.Contains("NotifyMeasurementsAdded(addedLines)", StringComparison.Ordinal),
            "Cut tool must apply the same box/polygon gesture to Area holes and Line eraser pieces");

        string rendering = ReadRepoFile("Controls/PdfViewport.MeasurementRendering.cs");
        string pdfExporter = ReadRepoFile("Models/PdfExporter.cs");
        AssertTrue(
            rendering.Contains("LayerOrderedMeasurements", StringComparison.Ordinal) &&
            rendering.Contains("\"area\" => 0", StringComparison.Ordinal) &&
            pdfExporter.Contains("MeasurementLayers ?? input.Takeoffs", StringComparison.Ordinal),
            "viewport and PDF export must draw takeoffs by layer order with areas behind by default");

        WithTempJob("takeoff_layers_store", job =>
        {
            string pageFolder = Path.Combine(job.PagesRoot, "A1");
            Directory.CreateDirectory(pageFolder);
            PageTakeoffLayerOrderStore.Save(pageFolder, ["walls", "areas", "walls", ""]);
            AssertEqual(
                "walls,areas",
                string.Join(",", PageTakeoffLayerOrderStore.Load(pageFolder)),
                "takeoff layer sidecar should persist distinct order");
            });
    }

    public static void DenseViewportLabelsKeepJoistAndSelectedLabels()
    {
        string rendering = ReadRepoFile("Controls/PdfViewport.MeasurementRendering.cs");
        string joistRendering = ReadRepoFile("Controls/PdfViewport.JoistRendering.cs");
        string pdfExporter = ReadRepoFile("Models/PdfExporter.Measurements.cs");
        string drawLabels = SliceMethod(rendering, "private void DrawMeasurementLabels(");
        string denseFilter = SliceMethod(rendering, "private bool ShouldDrawDenseMeasurementLabel(");
        string topLabels = SliceMethod(rendering, "private void DrawMeasurementTopLabels(");
        AssertTrue(
            drawLabels.Contains("drawAllLabels || ShouldDrawDenseMeasurementLabel(measurement)", StringComparison.Ordinal) &&
            denseFilter.Contains("IsMeasurementSelected(measurement)", StringComparison.Ordinal) &&
            denseFilter.Contains("measurement.JoistEnabled", StringComparison.Ordinal) &&
            denseFilter.Contains("ShouldDrawJoistSummaryLabel()", StringComparison.Ordinal) &&
            topLabels.Contains("isJoistArea ? ShouldDrawJoistSummaryLabel() : ShouldDrawMeasurementLabel(\"area\")", StringComparison.Ordinal),
            "dense viewport label suppression must still allow selected and joist summary labels to render");
        AssertFalse(
            denseFilter.Contains("measurement.JoistShowLabels", StringComparison.Ordinal),
            "dense viewport joist summary labels must not depend on the per-joist segment label toggle");
        AssertTrue(
            joistRendering.Contains("ShouldDrawJoistSegmentLabels(measurement)", StringComparison.Ordinal) &&
            joistRendering.Contains("measurement.JoistShowLabels", StringComparison.Ordinal),
            "per-joist segment labels, not the joist summary label, must obey the Label each joist item toggle");
        AssertTrue(
            pdfExporter.Contains("ShouldExportJoistSummaryLabel(options)", StringComparison.Ordinal) &&
            pdfExporter.Contains("options.ShowMeasurementLabels", StringComparison.Ordinal),
            "PDF export must keep joist summary labels separate from per-joist segment labels");
    }

    public static void PageTakeoffSelectionSyncsTakeoffsTree()
    {
        string pagesTree = ReadRepoFile("MainWindow.PagesTree.cs");
        string pageLegend = ReadRepoFile("MainWindow.PageTakeoffLegend.ContextMenu.cs");
        string navigation = ReadRepoFile("MainWindow.TakeoffSelectionNavigation.cs");

        AssertTrue(
            pagesTree.Contains("SyncTakeoffsTreeSelectionFromPageTakeoffs(node, fallbackToAnchor: false)", StringComparison.Ordinal) &&
            pagesTree.Contains("SyncTakeoffsTreeSelectionFromPageTakeoffs(node, fallbackToAnchor: true)", StringComparison.Ordinal),
            "Pages tree linked-takeoff clicks must sync Shift/Ctrl and single selection into the Takeoffs tree");
        AssertTrue(
            pageLegend.Contains("private IReadOnlyList<PageTakeoffNode> SyncTakeoffsTreeSelectionFromPageTakeoffs(", StringComparison.Ordinal) &&
            pageLegend.Contains("ActivateTakeoffItem(anchor.Takeoff)", StringComparison.Ordinal) &&
            pageLegend.Contains("SelectTakeoffItemsSilently(selectedTakeoffs, anchor.Takeoff)", StringComparison.Ordinal),
            "linked page takeoff selection must reuse the real Takeoffs-tree selection state");
        AssertTrue(
            navigation.Contains("ExpandTakeoffFolderAncestorsWithoutTracking(focusNode)", StringComparison.Ordinal),
            "silent Takeoffs-tree selection should reveal selected takeoffs inside folders");
    }

    private static string ReadPageTakeoffLegendSources() =>
        string.Concat(
            ReadRepoFile("MainWindow.PageTakeoffLegend.cs"),
            ReadRepoFile("MainWindow.PageTakeoffLegend.ContextMenu.cs"),
            ReadRepoFile("MainWindow.PageTakeoffLegend.DragDrop.cs"),
            ReadRepoFile("MainWindow.PageTakeoffLegend.MoveSort.cs"),
            ReadRepoFile("MainWindow.PageTakeoffLegend.Visibility.cs"));

    private static string ReadPdfLayerRenderServiceSources() =>
        string.Concat(
            ReadRepoFile("Models/PdfLayerRenderService.cs"),
            ReadRepoFile("Models/PdfLayerRenderService.Layers.cs"),
            ReadRepoFile("Models/PdfLayerRenderService.Protocol.cs"),
            ReadRepoFile("Models/PdfLayerRenderService.Render.cs"),
            ReadRepoFile("Models/PdfLayerRenderService.Worker.cs"),
            ReadRepoFile("Models/PdfLayerRenderResults.cs"));

    public static void SheetOverlayPersistedCacheIsWired()
    {
        string overlay = ReadRepoFile("MainWindow.SheetOverlay.cs");
        string cache = ReadRepoFile("Models/SheetOverlayRenderCache.cs");

        AssertTrue(
            overlay.Contains("SheetOverlayRenderCache.TryRead", StringComparison.Ordinal) &&
            overlay.Contains("Sheet overlay cache hit", StringComparison.Ordinal) &&
            overlay.Contains("SheetOverlayRenderCache.TryWrite", StringComparison.Ordinal),
            "sheet overlay rendering must read persisted cache before rendering and write it after tinting");
        AssertTrue(
            cache.Contains("OURPLANECORE_SHEET_OVERLAY_CACHE_ROOT", StringComparison.Ordinal) &&
            cache.Contains("render-cache", StringComparison.Ordinal) &&
            cache.Contains("sheet-overlay", StringComparison.Ordinal) &&
            cache.Contains("LayerStateKey", StringComparison.Ordinal),
            "sheet overlay cache must be portable and keyed by source PDF identity, render state, tint, opacity, and layers");
    }

    public static void PdfTakeoffImportCommandIsWired()
    {
        string xaml = ReadRepoFile("MainWindow.xaml");
        string menu = ReadRepoFile("MainWindow.OpenImportMenu.cs");
        string palette = ReadRepoFile("MainWindow.CommandPalette.cs");
        string importer = ReadRepoFile("MainWindow.PdfTakeoffImport.cs");
        string dialog = ReadRepoFile("Dialogs/PdfTakeoffImportDialog.cs");
        string options = ReadRepoFile("Models/PdfTakeoffImportOptions.cs");
        string service = ReadRepoFile("Models/PdfTakeoffAnnotationImportService.cs");
        string helper = ReadRepoFile("Tools/pdf_layers_helper.py");

        AssertTrue(
            xaml.Contains("Content=\"PDF Takeoffs\"", StringComparison.Ordinal) &&
            xaml.Contains("Click=\"BtnImportPdfTakeoffs_Click\"", StringComparison.Ordinal),
            "toolbar must expose the PDF Takeoffs import button beside the PDF/PlanSwift import surface");
        AssertTrue(
            menu.Contains("Import PDF Takeoffs...", StringComparison.Ordinal) &&
            palette.Contains("\"file.importPdfTakeoffs\"", StringComparison.Ordinal) &&
            palette.Contains("BtnImportPdfTakeoffs_Click(this, new RoutedEventArgs())", StringComparison.Ordinal),
            "Open/Import menu and command palette must expose PDF Takeoffs import without requiring an already-open job");
        AssertTrue(
            importer.Contains("PdfTakeoffImportFolderName = \"from pdf\"", StringComparison.Ordinal) &&
            importer.Contains("PdfTakeoffAnnotationImportService.TryReadAsync", StringComparison.Ordinal) &&
            importer.Contains("PdfTakeoffImportGroupKey(m.Annotation.Type, m.Annotation.Color)", StringComparison.Ordinal) &&
            importer.Contains("OurPlaneCoreJobStore.SavePageScale", StringComparison.Ordinal) &&
            importer.Contains("PdfSheetMetadataService.TryAnalyzePage", StringComparison.Ordinal) &&
            importer.Contains("pdf_takeoff_import_", StringComparison.Ordinal),
            "PDF takeoff import should bucket pages/takeoffs, group by type/color, preserve scale/page names, and write a markdown report");
        AssertTrue(
            importer.Contains("PreviewPdfTakeoffImportBucketPath", StringComparison.Ordinal) &&
            importer.Contains("ConfirmPdfTakeoffImport", StringComparison.Ordinal) &&
            importer.Contains("Import cancelled after preview; no job files were written.", StringComparison.Ordinal) &&
            importer.Contains("No supported PDF takeoff/ruler annotations were found.", StringComparison.Ordinal),
            "PDF takeoff import must scan first, show a confirmation preview, and avoid writing job files when cancelled or empty");
        AssertTrue(
            importer.Contains("CountPdfTakeoffImportItems", StringComparison.Ordinal) &&
            importer.Contains("sources.Sum(source => source.Annotations.Pages", StringComparison.Ordinal) &&
            importer.Contains("Takeoff items to create", StringComparison.Ordinal) &&
            importer.Contains("Top takeoff groups across PDFs", StringComparison.Ordinal),
            "PDF takeoff import preview count must match the per-PDF takeoff items the import will actually create");
        AssertTrue(
            options.Contains("CreateNewJob", StringComparison.Ordinal) &&
            options.Contains("ImportIntoCurrentJob", StringComparison.Ordinal) &&
            dialog.Contains("Create new job from PDF takeoffs", StringComparison.Ordinal) &&
            dialog.Contains("Import into current job", StringComparison.Ordinal),
            "PDF takeoff import must default to creating a new job and keep current-job import as an explicit mode");
        AssertTrue(
            importer.Contains("Kind = \"dimension\"", StringComparison.Ordinal) &&
            importer.Contains("PageAnnotationStore.SavePageAnnotations", StringComparison.Ordinal) &&
            importer.Contains("TryCreateCleanCopyAsync", StringComparison.Ordinal) &&
            importer.Contains("Clean PDF annotations removed", StringComparison.Ordinal),
            "PDF dimensions must import as ruler annotations and supported source annotations must be removable from the imported PDF background");
        AssertTrue(
            service.Contains("\"pdftakeoffs\"", StringComparison.Ordinal) &&
            service.Contains("\"pdftakeoffclean\"", StringComparison.Ordinal) &&
            service.Contains("Role = NormalizeRole", StringComparison.Ordinal) &&
            helper.Contains("pdf_takeoff_annotations_data", StringComparison.Ordinal) &&
            helper.Contains("pdf_takeoff_clean_copy_data", StringComparison.Ordinal) &&
            helper.Contains("role = \"dimension\"", StringComparison.Ordinal),
            "PDF takeoff annotation extraction and clean-copy creation must use the existing PyMuPDF worker protocol");
    }

    public static void ViewportEdgeSnapCommandIsWired()
    {
        string edge = ReadRepoFile("Controls/PdfViewport.EdgeSnap.cs");
        string input = ReadRepoFile("Controls/PdfViewport.Input.cs");
        string live = ReadRepoFile("Controls/PdfViewport.LiveInputRendering.cs");

        AssertTrue(
            edge.Contains("TryFindEdgeSnapCandidate", StringComparison.Ordinal) &&
            edge.Contains("ActivePageMeasurementSegmentsNear", StringComparison.Ordinal) &&
            edge.Contains("BuildAdjacentEdgeSnapPoints", StringComparison.Ordinal) &&
            edge.Contains("ClosedContour", StringComparison.Ordinal),
            "edge snap should search existing measurement segments and support edge/adjacent/contour previews");
        AssertTrue(
            input.Contains("TryCommitEdgeSnapPreview(rawPdf)", StringComparison.Ordinal) &&
            input.Contains("UpdateEdgeSnapPreview(rawPointerPdf)", StringComparison.Ordinal) &&
            input.Contains("key == Key.Tab && TryCycleEdgeSnapPreview()", StringComparison.Ordinal),
            "edge snap must hook hover, click commit, and Tab cycle in the viewport input path");
        AssertTrue(
            live.Contains("DrawEdgeSnapPreview(canvas)", StringComparison.Ordinal) &&
            edge.Contains("FinalizeDrawing();", StringComparison.Ordinal),
            "edge snap preview should render on the canvas and commit through normal measurement finalization");
    }

    public static void ViewportCountHotGripsAndTightHitTestAreWired()
    {
        string constants = ReadRepoFile(Path.Combine("Models", "ViewportConstants.cs"));
        string viewport = ReadRepoFile("Controls/PdfViewport.cs");
        string vertexSelection = ReadRepoFile("Controls/PdfViewport.VertexSelection.cs");
        string selectionEditing = ReadRepoFile("Controls/PdfViewport.SelectionEditing.cs");
        string input = ReadRepoFile("Controls/PdfViewport.Input.cs");
        string boxSelection = ReadRepoFile("Controls/PdfViewport.BoxSelection.cs");
        string overlayRendering = ReadRepoFile("Controls/PdfViewport.SelectionOverlayRendering.cs");

        AssertTrue(
            constants.Contains("public const float VertexHitRadiusScreen = 10f;", StringComparison.Ordinal) &&
            constants.Contains("public const float MeasurementHitRadiusScreen = 8f;", StringComparison.Ordinal) &&
            viewport.Contains("SelectedVertexHitToleranceScreenPx = 12f", StringComparison.Ordinal) &&
            viewport.Contains("SelectedMeasurementHitToleranceScreenPx = 10f", StringComparison.Ordinal),
            "viewport hit halo should stay close to the visible grip size at low zoom");

        AssertTrue(
            vertexSelection.Contains("measurement.MType is \"point\" or \"line\" or \"area\"", StringComparison.Ordinal) &&
            vertexSelection.Contains("DeletesWholeCountMeasurement", StringComparison.Ordinal) &&
            vertexSelection.Contains("PushMixedMeasurementUndo", StringComparison.Ordinal) &&
            vertexSelection.Contains("NotifyMeasurementsRemoved(removedMeasurements)", StringComparison.Ordinal),
            "Count measurements must participate in vertex editing and delete whole empty Count measurements");

        int pointHotGrip = selectionEditing.IndexOf("pointVertexMeasurement.MType == \"point\"", StringComparison.Ordinal);
        int bodyMove = selectionEditing.IndexOf("TryHitSelectedMeasurement(pdf, out Measurement selectedMeasurement)", StringComparison.Ordinal);
        AssertTrue(pointHotGrip >= 0 && bodyMove > pointHotGrip, "Count point hot grip must win before body move");
        AssertTrue(
            input.Contains("pointVertexMeasurement.MType == \"point\"", StringComparison.Ordinal) &&
            input.Contains("Cursor = Cursors.Cross", StringComparison.Ordinal) &&
            !input.Contains("Cursors.SizeAll", StringComparison.Ordinal),
            "cursor should use a simple cross for direct Count point drag");
        AssertTrue(
            overlayRendering.Contains("drawOnlySelectedCountVertices", StringComparison.Ordinal) &&
            overlayRendering.Contains("m.MType == \"point\"", StringComparison.Ordinal) &&
            overlayRendering.Contains("if (drawOnlySelectedCountVertices && !vertexSelected)", StringComparison.Ordinal),
            "Count vertex selection should not draw every Count handle as selected once a point subset exists");
        AssertTrue(
            boxSelection.Contains("Count, Line, or Area object", StringComparison.Ordinal),
            "Alt vertex selection guidance should include Count objects");
    }

    public static void PdfSnapDuplicateLoadGuardIsWired()
    {
        string pdfSnap = ReadRepoFile("Controls/PdfViewport.PdfSnap.cs");

        AssertTrue(
            pdfSnap.Contains("_pdfSnapInProgressCacheKey", StringComparison.Ordinal) &&
            pdfSnap.Contains("string.Equals(_pdfSnapInProgressCacheKey, cacheKey", StringComparison.Ordinal) &&
            pdfSnap.Contains("_pdfSnapInProgressCacheKey = cacheKey;", StringComparison.Ordinal) &&
            pdfSnap.Contains("_pdfSnapInProgressCacheKey = \"\";", StringComparison.Ordinal),
            "current sheet PDF Snap loads should skip duplicate in-flight cache keys");
        AssertTrue(
            pdfSnap.Contains("_overlayPdfSnapInProgressCacheKey", StringComparison.Ordinal) &&
            pdfSnap.Contains("string.Equals(_overlayPdfSnapInProgressCacheKey, cacheKey", StringComparison.Ordinal) &&
            pdfSnap.Contains("_overlayPdfSnapInProgressCacheKey = cacheKey;", StringComparison.Ordinal) &&
            pdfSnap.Contains("_overlayPdfSnapInProgressCacheKey = \"\";", StringComparison.Ordinal),
            "overlay PDF Snap loads should skip duplicate in-flight cache keys");
    }

    public static void PdfPreviewRenderCacheIsWiredBeforeLayerRender()
    {
        string pageApi = ReadRepoFile("Controls/PdfViewport.PageApi.cs");
        string layers = ReadRepoFile("Controls/PdfViewport.Layers.cs");
        string service = ReadPdfLayerRenderServiceSources();
        string cache = ReadRepoFile("Models/PdfPreviewRenderCache.cs");

        int cacheApply = pageApi.IndexOf("TryApplyPersistedPreviewRender", StringComparison.Ordinal);
        int queueRender = pageApi.IndexOf("QueueLayerRender(", StringComparison.Ordinal);
        AssertTrue(
            cacheApply >= 0 && queueRender > cacheApply,
            "persisted clean preview cache should be applied before queueing the PyMuPDF refresh render");
        AssertTrue(
            layers.Contains("TryApplyPersistedPreviewRender", StringComparison.Ordinal) &&
            layers.Contains("PdfPreviewRenderCache.TryReadCleanPreview", StringComparison.Ordinal) &&
            layers.Contains("ApplyInitialPreviewView", StringComparison.Ordinal) &&
            layers.Contains("Viewport PyMuPDF preview cache hit", StringComparison.Ordinal),
            "viewport should read and apply cached clean PyMuPDF previews without using Docnet");
        AssertTrue(
            service.Contains("PdfPreviewRenderCache.IsCleanRenderRequest", StringComparison.Ordinal) &&
            service.Contains("PdfPreviewRenderCache.TryWriteCleanRender", StringComparison.Ordinal),
            "successful clean PyMuPDF renders should populate the persisted render cache");
        AssertTrue(
            cache.Contains("CacheRootEnvironmentVariable", StringComparison.Ordinal) &&
            cache.Contains("LastWriteTimeUtc.Ticks", StringComparison.Ordinal) &&
            cache.Contains("File.Move(tempImage, paths.ImagePath, overwrite: true)", StringComparison.Ordinal),
            "preview cache should be keyed by source identity and written atomically through temp files");
    }

    public static void PdfPageOpenUsesDocnetPreviewOnCacheMiss()
    {
        string pageApi = ReadRepoFile("Controls/PdfViewport.PageApi.cs");
        string layers = ReadRepoFile("Controls/PdfViewport.Layers.cs");
        string renderCache = ReadRepoFile("Controls/PdfViewport.RenderCache.cs");
        string policy = ReadRepoFile("Models/ViewportRenderPolicy.cs");

        int cacheApply = pageApi.IndexOf("TryApplyPersistedPreviewRender", StringComparison.Ordinal);
        int cacheBranch = pageApi.IndexOf("if (previewCacheHit)", StringComparison.Ordinal);
        int fullCacheFallback = pageApi.IndexOf("TryApplyPersistedDefaultCleanRender", StringComparison.Ordinal);
        int docnetFallback = pageApi.IndexOf("QueueDocnetRender(", StringComparison.Ordinal);
        int status = pageApi.IndexOf("PostStatus(previewCacheHit", StringComparison.Ordinal);
        AssertTrue(
            cacheApply >= 0 &&
            cacheBranch > cacheApply &&
            fullCacheFallback > cacheBranch &&
            docnetFallback > fullCacheFallback &&
            status > docnetFallback,
            "page open should try persisted clean render cache before queueing a fast Docnet preview fallback");
        AssertTrue(
            pageApi.Contains("queueLayerAfter: true", StringComparison.Ordinal) &&
            pageApi.Contains("resetLayerStates: true", StringComparison.Ordinal) &&
            pageApi.Contains("fireLayersAfter: true", StringComparison.Ordinal),
            "cache-miss Docnet preview should still queue the normal layer render continuation");
        AssertTrue(
            pageApi.Contains("allowImmediateCache: false", StringComparison.Ordinal) &&
            layers.Contains("bool allowImmediateCache = true", StringComparison.Ordinal) &&
            layers.Contains("allowImmediateCache && TryApplyPersistedCleanLayerRender(request)", StringComparison.Ordinal),
            "page open should not synchronously decode a second clean render cache hit after applying the instant preview");
        AssertTrue(
            pageApi.Contains("ClearPreviousPageBitmapDuringSwitch();", StringComparison.Ordinal) &&
            pageApi.Contains("ViewportRenderPolicy.FastPageSwitchPreviewRenderScale", StringComparison.Ordinal) &&
            policy.Contains("FastPageSwitchPreviewRenderScale = 0.15f", StringComparison.Ordinal),
            "cache-miss page switches should not keep showing the old sheet while the lightweight preview renders");
        AssertTrue(
            layers.Contains("private bool TryApplyPersistedDefaultCleanRender", StringComparison.Ordinal) &&
            layers.Contains("TryApplyPersistedCleanLayerRender(request)", StringComparison.Ordinal),
            "page open should be able to synchronously apply a persisted clean full render before falling back to Docnet");
        AssertTrue(
            renderCache.Contains("ViewportRenderPolicy.FastPageSwitchPreviewRenderScale", StringComparison.Ordinal) &&
            renderCache.Contains("Task.Delay(75)", StringComparison.Ordinal),
            "nearby sheet prefetch should warm the same lightweight preview cache used by page switching");
    }

    public static void PdfFullScaleRenderCacheIsWiredBeforeWorker()
    {
        string layers = ReadRepoFile("Controls/PdfViewport.Layers.cs");
        string service = ReadPdfLayerRenderServiceSources();
        string cache = ReadRepoFile("Models/PdfPreviewRenderCache.cs");

        int applyCache = layers.IndexOf("TryApplyPersistedCleanLayerRender(request)", StringComparison.Ordinal);
        int assignPending = layers.IndexOf("_pendingLayerRender = request", StringComparison.Ordinal);
        AssertTrue(
            applyCache >= 0 && assignPending > applyCache,
            "clean full-scale render cache should be applied before queueing the PyMuPDF worker");
        AssertTrue(
            layers.Contains("Viewport PyMuPDF render cache hit", StringComparison.Ordinal) &&
            layers.Contains("!render.LayersCaptured && _cachedLayers == null", StringComparison.Ordinal),
            "full render cache hits should be logged and must not bypass unknown layer discovery");
        AssertFalse(
            layers.Contains("if (request.ResetLayerStates)\r\n            return false", StringComparison.Ordinal) ||
            layers.Contains("if (request.ResetLayerStates)\n            return false", StringComparison.Ordinal),
            "initial reset layer renders should be allowed to use captured clean render cache hits");
        AssertTrue(
            layers.Contains("if (request.ResetLayerStates)", StringComparison.Ordinal) &&
            layers.Contains("_layerStates.Clear();", StringComparison.Ordinal),
            "cached clean render hits should rebuild reset layer state from cached layer metadata");
        AssertTrue(
            service.Contains("LayersCaptured = true", StringComparison.Ordinal) &&
            service.Contains("PdfPreviewRenderCache.TryWriteCleanRender", StringComparison.Ordinal),
            "PyMuPDF render results should persist clean renders with captured layer metadata");
        AssertTrue(
            cache.Contains("MaxPersistedRenderScale", StringComparison.Ordinal) &&
            cache.Contains("MaxPersistedRenderPixels", StringComparison.Ordinal) &&
            cache.Contains("MaxPersistedRenderImageBytes", StringComparison.Ordinal) &&
            cache.Contains("IsCleanRenderRequest", StringComparison.Ordinal),
            "persisted full render cache should be bounded and limited to clean render requests");
    }

    public static void PdfLayerRenderUsesPortableInlineImageProtocol()
    {
        string service = ReadPdfLayerRenderServiceSources();
        string helper = ReadRepoFile(Path.Combine("Tools", "pdf_layers_helper.py"));

        AssertTrue(
            service.Contains("InlineImage = true", StringComparison.Ordinal) &&
            service.Contains("InlineRenderImageMaxPixels", StringComparison.Ordinal) &&
            service.Contains("InlineRenderImageMaxPixels = 24_000_000", StringComparison.Ordinal) &&
            service.Contains("MaxRenderCacheEntries = 96", StringComparison.Ordinal) &&
            service.Contains("MaxRenderCacheBytes = 768_000_000", StringComparison.Ordinal) &&
            service.Contains("ImageBase64", StringComparison.Ordinal) &&
            service.Contains("Convert.FromBase64String(response.ImageBase64)", StringComparison.Ordinal),
            "C# layer rendering should request RAM-sized bounded inline PNG data and decode image_base64 responses");
        AssertTrue(
            service.Contains("File.ReadAllBytes(response.Image)", StringComparison.Ordinal) &&
            service.Contains("PyMuPDF did not produce a rendered image.", StringComparison.Ordinal),
            "C# layer rendering should keep the old temp-file image fallback for large renders and older helpers");
        AssertFalse(
            service.Contains("if (!File.Exists(response.Image))", StringComparison.Ordinal),
            "render responses must not require a temp image file when inline image data is present");

        AssertTrue(
            helper.Contains("import base64", StringComparison.Ordinal) &&
            helper.Contains("def _render_image_payload", StringComparison.Ordinal) &&
            helper.Contains("base.tobytes(\"png\")", StringComparison.Ordinal) &&
            helper.Contains("\"image_base64\"", StringComparison.Ordinal) &&
            helper.Contains("base.save(image_path)", StringComparison.Ordinal),
            "Python helper should return inline PNG data for bounded renders and fall back to the existing PNG file path");
    }

    public static void PdfSheetMetadataHandlesRotatedBottomTitleBlock()
    {
        string helper = ReadRepoFile(Path.Combine("Tools", "pdf_layers_helper.py"));

        AssertTrue(
            helper.Contains("in_bottom_large_title", StringComparison.Ordinal) &&
            helper.Contains("def _extract_rotated_bottom_title", StringComparison.Ordinal) &&
            helper.Contains("str(w[4]).strip().lower().rstrip(\":\") == \"title\"", StringComparison.Ordinal) &&
            helper.Contains("return _clean_sheet_title(\" \".join(str(w[4]) for w in ordered))", StringComparison.Ordinal),
            "sheet metadata helper should detect large rotated sheet labels and titles in bottom title blocks");
        AssertFalse(
            helper.Contains("if not sheet_label:\r\n        skip_scale = True", StringComparison.Ordinal) ||
            helper.Contains("if not sheet_label:\n        skip_scale = True", StringComparison.Ordinal),
            "missing sheet label must not force Auto Scale off when a usable PDF scale was detected");
    }

    public static void PdfDetailClipRenderIsWired()
    {
        string rendering = ReadRepoFile("Controls/PdfViewport.Rendering.cs");
        string renderCache = ReadRepoFile("Controls/PdfViewport.RenderCache.cs");
        string detail = ReadRepoFile("Controls/PdfViewport.DetailRender.cs");
        string detailPrefetch = ReadRepoFile("Controls/PdfViewport.DetailPrefetch.cs");
        string transform = ReadRepoFile("Controls/PdfViewport.ViewTransform.cs");
        string viewport = ReadRepoFile("Controls/PdfViewport.cs");
        string pageApi = ReadRepoFile("Controls/PdfViewport.PageApi.cs");
        string layers = ReadRepoFile("Controls/PdfViewport.Layers.cs");
        string policy = ReadRepoFile("Models/ViewportRenderPolicy.cs");
        string service = ReadPdfLayerRenderServiceSources();
        string helper = ReadRepoFile(Path.Combine("Tools", "pdf_layers_helper.py"));

        AssertTrue(
            policy.Contains("DetailRenderEnabled = true", StringComparison.Ordinal) &&
            policy.Contains("CurrentResponsiveMaxRenderScale", StringComparison.Ordinal) &&
            policy.Contains("new RenderQuality(3.0f, 192_000_000f", StringComparison.Ordinal) &&
            policy.Contains("new RenderQuality(4.0f, 320_000_000f", StringComparison.Ordinal) &&
            policy.Contains("ZoomRefreshMinZoom = 0.30f", StringComparison.Ordinal) &&
            policy.Contains("DetailRenderMinZoom = 1.0f", StringComparison.Ordinal) &&
            policy.Contains("DetailRenderMaxScale = 16.0f", StringComparison.Ordinal) &&
            policy.Contains("SelectDetailRenderScale", StringComparison.Ordinal) &&
            policy.Contains("ShouldUseZoomRefreshRender", StringComparison.Ordinal) &&
            policy.Contains("ShouldSkipFullRefreshDuringDetail", StringComparison.Ordinal) &&
            policy.Contains("DetailRenderPaddingScreenPxForZoom", StringComparison.Ordinal) &&
            policy.Contains("DetailRenderMaxPixels", StringComparison.Ordinal) &&
            policy.Contains("DetailRenderPrefetchEnabled = true", StringComparison.Ordinal) &&
            policy.Contains("DetailRenderPrefetchMinZoom = 4.0f", StringComparison.Ordinal) &&
            policy.Contains("DetailRenderPrefetchConcurrency = 1", StringComparison.Ordinal) &&
            policy.Contains("DetailRenderMaxPaintTiles = 2", StringComparison.Ordinal) &&
            policy.Contains("ShouldUseDetailRenderPrefetch", StringComparison.Ordinal),
            "viewport policy should cap full-sheet renders separately from viewport-sized detail renders");
        AssertTrue(
            pageApi.Contains("TryApplyPersistedPreviewRender", StringComparison.Ordinal) &&
            pageApi.Contains("renderScale: CurrentBaseRenderScale()", StringComparison.Ordinal) &&
            layers.Contains("QueueInitialLayerDiscoveryOrRender", StringComparison.Ordinal) &&
            layers.Contains("CurrentRenderScale()", StringComparison.Ordinal),
            "interactive page opens should show the cheap preview first, then queue a capped base refresh and clipped detail instead of leaving the sheet blurry");
        AssertTrue(
            rendering.Contains("SKFilterQuality.High", StringComparison.Ordinal) &&
            rendering.Contains("DetailRenderCoversVisibleViewForPaint()", StringComparison.Ordinal) &&
            rendering.Contains("DrawDetailRenderTile(canvas)", StringComparison.Ordinal),
            "paint should use sharper upscale sampling and skip the heavy base bitmap when a high-DPI detail tile covers the view");
        AssertFalse(
            rendering.Contains("SKFilterQuality.Low", StringComparison.Ordinal),
            "main PDF bitmap paint must not switch to low-quality sampling while panning at high zoom");
        AssertTrue(
            transform.Contains("ViewportRenderPolicy.ShouldUseDetailRender(_zoom, _bitmapScale)", StringComparison.Ordinal) &&
            transform.Contains("bool needsDetailRender", StringComparison.Ordinal) &&
            transform.Contains("_zoom < ViewportRenderPolicy.ZoomRefreshMinZoom", StringComparison.Ordinal) &&
            transform.Contains("ViewportRenderPolicy.ShouldUseZoomRefreshRender(_zoom, _bitmapScale)", StringComparison.Ordinal) &&
            transform.Contains("QueueDetailRenderIfNeeded(force)", StringComparison.Ordinal) &&
            transform.Contains("ViewportRenderPolicy.ShouldSkipFullRefreshDuringDetail(_bitmapScale)", StringComparison.Ordinal) &&
            transform.Contains("QueueDetailRenderIfNeeded(force: false)", StringComparison.Ordinal),
            "zoom and pan idle should refresh blurry previews before scheduling detail renders for deep zoom");
        AssertTrue(
            pageApi.Contains("QueueLayerRender(", StringComparison.Ordinal) &&
            pageApi.Contains("allowImmediateCache: false", StringComparison.Ordinal) &&
            pageApi.Contains("BeginPageSwitchDetailRenderHold()", StringComparison.Ordinal) &&
            detail.Contains("ShouldHoldDetailRender(force)", StringComparison.Ordinal) &&
            detail.Contains("QueueDetailRenderAfterHold()", StringComparison.Ordinal) &&
            detail.Contains("QueueDetailRenderIfNeeded(force: true)", StringComparison.Ordinal) &&
            policy.Contains("PageSwitchDetailRenderDelayMs = 320", StringComparison.Ordinal),
            "cached preview page opens should schedule capped base work immediately and hold clipped high-detail briefly until the page switch settles");
        AssertTrue(
            viewport.Contains("_navigationIdleTimer.Tick", StringComparison.Ordinal) &&
            viewport.Contains("EndFastNavigation();", StringComparison.Ordinal),
            "navigation idle should run the real idle path so clipped detail renders are scheduled after wheel zoom");
        AssertTrue(
            transform.Contains("if (ViewportRenderPolicy.ShouldSkipFullRefreshDuringDetail(_bitmapScale))", StringComparison.Ordinal),
            "deep zoom should skip expensive full-sheet refresh once a normal 1x base bitmap exists");
        AssertTrue(
            detail.Contains("private sealed record DetailRenderRequest", StringComparison.Ordinal) &&
            detail.Contains("_activeDetailRender", StringComparison.Ordinal) &&
            detail.Contains("DetailRequestCoversCurrentView(_activeDetailRender, request.RenderScale)", StringComparison.Ordinal) &&
            detail.Contains("DetailRequestCoversCurrentView(_pendingDetailRender, request.RenderScale)", StringComparison.Ordinal) &&
            detail.Contains("IsSameDetailRequest(_activeDetailRender, request)", StringComparison.Ordinal) &&
            detail.Contains("_detailRenderVersion + 1", StringComparison.Ordinal) &&
            detail.Contains("private sealed class DetailRenderTile", StringComparison.Ordinal) &&
            detail.Contains("MaxDetailRenderTileEntries = 64", StringComparison.Ordinal) &&
            detail.Contains("ResolveViewportRamBudget(2_400_000_000L, 4_800_000_000L, 0.07)", StringComparison.Ordinal) &&
            detail.Contains("TrimDetailRenderTiles", StringComparison.Ordinal) &&
            detail.Contains("request.ClipRect", StringComparison.Ordinal) &&
            detail.Contains("PdfLayerRenderService.TryRenderAsync", StringComparison.Ordinal) &&
            detail.Contains("Task.Run(() => SKBitmap.Decode(renderResult.Result.ImageBytes))", StringComparison.Ordinal) &&
            detail.Contains("ReportViewportRenderProfile", StringComparison.Ordinal) &&
            detail.Contains("ViewportRenderPolicy.DetailRenderPaddingScreenPxForZoom(_zoom)", StringComparison.Ordinal) &&
            detail.Contains("QueueAdjacentDetailRenderPrefetch", StringComparison.Ordinal) &&
            detail.Contains("DetailTilePrefetchSemaphore", StringComparison.Ordinal) &&
            detailPrefetch.Contains("QueueAdjacentDetailRenderPrefetchFromTile", StringComparison.Ordinal) &&
            detailPrefetch.Contains("ShouldUseDetailRenderPrefetch(_zoom, _isFastNavigating)", StringComparison.Ordinal) &&
            detailPrefetch.Contains("PrefetchDetailRenderTileAsync", StringComparison.Ordinal) &&
            detailPrefetch.Contains("TryRenderDedicatedProcessAsync", StringComparison.Ordinal) &&
            detailPrefetch.Contains("IsCurrentDetailPrefetchRequest", StringComparison.Ordinal) &&
            detailPrefetch.Contains("DetailRenderTileCoversRect", StringComparison.Ordinal) &&
            detailPrefetch.Contains("detail-prefetch", StringComparison.Ordinal) &&
            detail.Contains("DrawDetailRenderTileBitmap", StringComparison.Ordinal) &&
            detail.Contains("IntersectionArea", StringComparison.Ordinal) &&
            detail.Contains("ViewportRenderPolicy.DetailRenderMaxPaintTiles", StringComparison.Ordinal) &&
            detail.Contains("ClearDetailRender()", StringComparison.Ordinal),
            "viewport should own versioned clipped detail render requests, cache multiple decoded tiles in RAM, prefetch adjacent work-zoom clips, and decode them off the UI path");
        AssertTrue(
            layers.Contains("Task.Run(() => SKBitmap.Decode(renderResult.Result.ImageBytes))", StringComparison.Ordinal) &&
            layers.Contains("ApplyLayerRenderResult(completion.Result, completion.Request.ResetLayerStates, decodedBitmap)", StringComparison.Ordinal),
            "full layer render PNG decode should be done before UI-thread bitmap application");
        AssertTrue(
            layers.Contains("TryApplyLayerBitmapCache(request, out bool exactLayerCacheHit)", StringComparison.Ordinal) &&
            layers.Contains("ShouldPreserveDetailDuringLayerRender(request)", StringComparison.Ordinal) &&
            layers.Contains("CacheLayerBitmapRender(completion.Request)", StringComparison.Ordinal) &&
            layers.Contains("CacheLayerBitmapRender(completion.Request, completion.Result, decodedBitmap)", StringComparison.Ordinal) &&
            layers.Contains("PdfLayerRenderResult render,", StringComparison.Ordinal) &&
            layers.Contains("layer-memory-best", StringComparison.Ordinal) &&
            layers.Contains("layer-memory", StringComparison.Ordinal) &&
            layers.Contains("ShouldKeepHighScaleLayerBitmapInCache", StringComparison.Ordinal) &&
            layers.Contains("ApplyLayerRenderMetadataOnly", StringComparison.Ordinal) &&
            layers.Contains("layer-memory-deferred", StringComparison.Ordinal) &&
            renderCache.Contains("LayerRenderBitmapCache", StringComparison.Ordinal) &&
            renderCache.Contains("TryGetBest", StringComparison.Ordinal) &&
            renderCache.Contains("LayerRenderBitmapCacheSignature", StringComparison.Ordinal) &&
            renderCache.Contains("ResolveLayerBitmapCacheBudgetBytes", StringComparison.Ordinal) &&
            renderCache.Contains("PrefetchCleanLayerRender", StringComparison.Ordinal) &&
            renderCache.Contains("CleanRenderPrefetchSemaphore", StringComparison.Ordinal) &&
            renderCache.Contains("24_000_000_000L", StringComparison.Ordinal),
            "decoded full-sheet PyMuPDF bitmaps should be reused from a large RAM cache before rerendering, including best-scale fallback, clean prefetch, and completed stale high-zoom renders");
        AssertTrue(
            service.Contains("TryRenderDedicatedProcessAsync", StringComparison.Ordinal),
            "background clean render prefetch should use dedicated helper processes instead of blocking the interactive worker");
        AssertTrue(
            service.Contains("public RectDto? Clip { get; set; }", StringComparison.Ordinal) &&
            service.Contains("Clip = hasClip ? RectDto.FromSKRect", StringComparison.Ordinal) &&
            service.Contains("response.Clip?.ToSKRect()", StringComparison.Ordinal) &&
            service.Contains("bool invoked = hasClip", StringComparison.Ordinal) &&
            service.Contains("TryInvokeDetailWorker(\"render\", request", StringComparison.Ordinal) &&
            service.Contains("TryRenderDedicatedProcessAsync", StringComparison.Ordinal) &&
            service.Contains("clipRect = null", StringComparison.Ordinal) &&
            service.Contains("Clip = hasClip ? RectDto.FromSKRect(clipRect!.Value) : null", StringComparison.Ordinal) &&
            service.Contains("DetailWorkerSemaphore", StringComparison.Ordinal) &&
            service.Contains("WorkerJsonOptions", StringComparison.Ordinal) &&
            service.Contains("JsonSerializer.Serialize(envelope, WorkerJsonOptions)", StringComparison.Ordinal) &&
            service.Contains("!hasClip && PdfPreviewRenderCache.IsCleanRenderRequest", StringComparison.Ordinal),
            "layer render protocol should pass clip rectangles through a dedicated persistent worker without polluting the persisted whole-sheet cache or sending multiline JSON to the line-based worker");
        AssertTrue(
            helper.Contains("raw_clip = req.get(\"clip\")", StringComparison.Ordinal) &&
            helper.Contains("page.get_pixmap(matrix=matrix, clip=clip, alpha=False)", StringComparison.Ordinal) &&
            helper.Contains("\"clip\": clip_payload", StringComparison.Ordinal),
            "Python helper should render the requested visible PDF clip with PyMuPDF");
    }

    public static void SheetOverlayRenderingUsesSharperSampling()
    {
        string source = ReadRepoFile(Path.Combine("Controls", "PdfViewport.SheetOverlay.cs"));
        string method = SliceMethod(source, "private void DrawSheetOverlay(SKCanvas canvas, SKRect visiblePdf)");

        AssertTrue(
            method.Contains("SKFilterQuality.High", StringComparison.Ordinal) &&
            method.Contains("SKFilterQuality.Medium", StringComparison.Ordinal),
            "sheet overlay underlay should use medium/high sampling instead of low-quality blur");
        AssertFalse(
            method.Contains("SKFilterQuality.Low", StringComparison.Ordinal),
            "sheet overlay underlay should not switch to low-quality sampling during navigation");
        AssertTrue(
            method.Contains("IsAntialias = false", StringComparison.Ordinal),
            "bitmap sheet overlays should avoid antialias softening");
    }

    public static void ViewportStressSmokeCanExerciseHighZoomPan()
    {
        string source = ReadRepoFile("MainWindow.ViewportPageStressSmoke.cs");

        AssertTrue(
            source.Contains("OURPLANECORE_VIEWPORT_PAGE_STRESS_TARGET_ZOOM", StringComparison.Ordinal) &&
            source.Contains("OURPLANECORE_VIEWPORT_PAGE_STRESS_PAN_STEPS", StringComparison.Ordinal) &&
            source.Contains("ReadEnvironmentFloat", StringComparison.Ordinal) &&
            source.Contains("RestoreViewState(new PdfViewport.ViewState(targetZoom", StringComparison.Ordinal),
            "viewport stress smoke must support hidden absolute zoom and pan checks for 350% regressions");
    }

    public static void PagesTreeSelectedSheetScaleMenuIsWired()
    {
        string commands = ReadRepoFile("MainWindow.PagesCommands.cs");
        string scale = ReadRepoFile("MainWindow.PagesScale.cs");
        string callbacks = ReadRepoFile("MainWindow.ViewportCallbacks.cs");

        AssertTrue(
            commands.Contains("SetSelectedPagesScaleFromContext(item)", StringComparison.Ordinal) &&
            commands.Contains("Set Scale for {selectedPageCount} Selected", StringComparison.Ordinal),
            "page context menu must expose Set Scale for single and multi-selected sheets");
        AssertTrue(
            scale.Contains("SelectedPagesFromPagesTree(anchor)", StringComparison.Ordinal) &&
            scale.Contains("PdfSheetMetadataService.TryParseScaleMetersPerPt", StringComparison.Ordinal) &&
            scale.Contains("OurPlaneCoreJobStore.SavePageScale", StringComparison.Ordinal) &&
            scale.Contains("WriteFloatingPageSetupMetadata", StringComparison.Ordinal) &&
            scale.Contains("ApplyScaleToPageMeasurements", StringComparison.Ordinal) &&
            scale.Contains("FlushTakeoffAutosaves", StringComparison.Ordinal),
            "scale menu must parse, persist metadata, update measurements, and flush changed takeoffs");
        AssertTrue(
            callbacks.Contains("private IReadOnlyList<TakeoffItem> ApplyScaleToPageMeasurements", StringComparison.Ordinal),
            "page-scale updates should reuse a page-scoped measurement scale helper");
    }

    public static void ViewportRenderingPreservesDpiMatrix()
    {
        string rendering = ReadRepoFile("Controls/PdfViewport.Rendering.cs");
        AssertTrue(
            rendering.Contains("canvas.Concat(ref measMtx)", StringComparison.Ordinal),
            "measurement overlay must compose with the SKElement DPI matrix instead of replacing it");
        AssertFalse(
            rendering.Contains("canvas.SetMatrix(measMtx)", StringComparison.Ordinal),
            "measurement overlay must not drop the SKElement DPI matrix on high-DPI laptop displays");
    }

    public static void PdfExportDefaultsMeasurementsOnForMeasuredSheets()
    {
        string source = ReadRepoFile("MainWindow.PdfExport.cs");
        AssertTrue(
            source.Contains("DefaultPdfExportIncludeMeasurements(allPages, initialSelection)", StringComparison.Ordinal),
            "PDF export dialog must not use the persisted Measurements flag directly");
        AssertTrue(
            source.Contains("PageHasVisibleExportMeasurements(page)", StringComparison.Ordinal),
            "PDF export must turn Measurements on when selected sheets contain visible takeoffs");
        AssertFalse(
            source.Contains("includeMeasurements: _settings.PdfExportIncludeMeasurements", StringComparison.Ordinal),
            "PDF export must not silently open with Measurements unchecked from stale settings");

        string dialogSource = ReadRepoFile("Dialogs/PdfExportDialog.cs");
        AssertTrue(
            dialogSource.Contains("public double MeasurementStrokeScale", StringComparison.Ordinal),
            "PDF export dialog must expose measurement stroke scale");
        AssertTrue(
            source.Contains("MeasurementStrokeScale: dialog.MeasurementStrokeScale", StringComparison.Ordinal) ||
            source.Contains("dialog.MeasurementStrokeScale,", StringComparison.Ordinal),
            "PDF export options must use the dialog stroke scale immediately");
    }

    private static string SliceMethod(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException($"Method not found: {signature}");

        int nextMethod = source.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        return nextMethod < 0 ? source[start..] : source[start..nextMethod];
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

    private static TakeoffItem CreateMeasuredTakeoffItem(
        OurPlaneCoreJob job,
        string parentFolder,
        string name,
        string measurementType)
    {
        TakeoffItem item = OurPlaneCoreJobStore.CreateTakeoffItem(job, parentFolder, name, "#FF4444", measurementType);
        item.Measurements.Add(new Measurement
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            MType = measurementType,
            Color = "#FF4444",
            PageFolder = "",
            TakeoffFolder = item.FolderPath,
            ScaleMetersPerPt = 0.3048,
            Points = MeasurementPoints(measurementType),
        });
        OurPlaneCoreJobStore.SaveTakeoffItem(item);
        return item;
    }

    private static List<SKPoint> MeasurementPoints(string measurementType)
    {
        if (measurementType == "area")
        {
            return
            [
                new SKPoint(0, 0),
                new SKPoint(10, 0),
                new SKPoint(10, 10),
                new SKPoint(0, 10),
            ];
        }

        return
        [
            new SKPoint(0, 0),
            new SKPoint(10, 0),
        ];
    }

    private static bool ContainsFolder(IEnumerable<TakeoffItem> items, string folderPath) =>
        items.Any(item => IsSamePath(item.FolderPath, folderPath));

    private static TakeoffItem ItemByFolder(IEnumerable<TakeoffItem> items, string folderPath) =>
        items.FirstOrDefault(item => IsSamePath(item.FolderPath, folderPath))
        ?? throw new InvalidOperationException($"Takeoff item not loaded: {folderPath}");

    private static bool IsSamePath(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static void WithTempJob(string name, Action<OurPlaneCoreJob> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "opc_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            OurPlaneCoreJob job = OurPlaneCoreJobStore.CreateJob(root, name);
            action(job);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private static string RepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ourplanecore.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find ourplanecore repo root.");
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
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"{message}: expected '{expected}', got '{actual}'");
    }
}
