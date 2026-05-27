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

        string helpersSource = ReadRepoFile("MainWindow.TreeHelpers.cs");
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

    public static void TreeSearchBulkVisibilityAndViewportMarkupSelectionAreWired()
    {
        string xaml = ReadRepoFile("MainWindow.xaml");
        string treeSearch = ReadRepoFile("MainWindow.TreeSearch.cs");
        string takeoffsClipboard = ReadRepoFile("MainWindow.TakeoffsClipboard.cs");
        string pageLegend = ReadRepoFile("MainWindow.PageTakeoffLegend.cs");
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

    public static void PageTakeoffLayersAndAltVertexModeAreWired()
    {
        string pageLegend = ReadRepoFile("MainWindow.PageTakeoffLegend.cs");
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
