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
        string pagePreviewWarmup = ReadRepoFile("MainWindow.PagePreviewWarmup.cs");
        string measurementCallbacks = ReadRepoFile("MainWindow.MeasurementCallbacks.cs");
        string loadFromTabMethod = SliceMethod(pageTabs, "private void LoadPageFromTab(");
        string loadMethod = SliceMethod(pageTabs, "private void LoadPageIntoViewport(PageInfo page, PdfViewport.ViewState? restoreView)");
        string loadAnnotationsMethod = SliceMethod(pageTabs, "private void LoadViewportPageAnnotations(");
        string saveAnnotationsMethod = SliceMethod(measurementCallbacks, "private void SaveCurrentPageAnnotations()");
        string distinctBatchPagesMethod = SliceMethod(pageTabs, "private static IReadOnlyList<PageInfo> DistinctBatchPages(");
        string queueMethod = SliceMethod(pageTabs, "private void QueueDeferredPageOpenWork(");
        string deferredQuietMethod = SliceMethod(pageTabs, "private async void RunDeferredPageOpenWorkWhenQuiet(");
        string quietWaitMethod = SliceMethod(pageTabs, "private async Task WaitForDeferredPageOpenQuietAsync(");
        string deferredMethod = SliceMethod(pageTabs, "private void RunDeferredPageOpenWork(");
        string prefetchMethod = SliceMethod(pageTabs, "private void QueueNearbyPagePreviewPrefetchDeferred(");
        string warmupMethod = SliceMethod(pageTabs, "private void QueueJobPagePreviewWarmupDeferred(");
        string warmupRunMethod = SliceMethod(pageTabs, "private static void QueueJobPagePreviewWarmup(IReadOnlyList<PageInfo> pages, string activePageFolder)");
        string rasterWarmupMethod = SliceMethod(pageTabs, "private void QueueJobRasterSheetRefreshWarmupDeferred(");
        string rasterWarmupRunMethod = SliceMethod(pageTabs, "private static void QueueJobRasterSheetRefreshWarmup(IReadOnlyList<PageInfo> pages, string activePageFolder)");
        string rasterWarmupQueueMethod = SliceMethod(pageTabs, "private static void QueueJobRasterSheetRefreshWarmup(IReadOnlyList<PageInfo> pages)");
        string nearbyPrefetchQueueMethod = SliceMethod(pageTabs, "private void QueueNearbyPagePreviewPrefetch(PageInfo activePage)");
        string nearbyPrefetchMethod = SliceMethod(pageTabs, "private static void QueueNearbyPagePreviewPrefetch(IReadOnlyList<PageInfo> pages, string activePageFolder, string previousPageFolder)");
        string loadPagesForPrefetchMethod = SliceMethod(pagePreviewWarmup, "private static IReadOnlyList<PageInfo> LoadPagesForPreviewPrefetch(");
        string queuePreviewPrefetchAtMethod = SliceMethod(pageTabs, "private static void QueuePreviewPrefetchAt(");
        string queueReadableBasePrefetchAtMethod = SliceMethod(pageTabs, "private static void QueueReadableBasePrefetchAt(");
        string queueCleanRenderPrefetchAtMethod = SliceMethod(pageTabs, "private static void QueueCleanRenderPrefetchAt(");
        string policy = ReadRepoFile("Models/ViewportRenderPolicy.cs");

        AssertFalse(
            loadMethod.Contains("TryReadPage(page.FolderPath", StringComparison.Ordinal),
            "page open must not re-read source.json after LoadPageFromTab already loaded the page");
        AssertTrue(
            loadFromTabMethod.Contains("PageInfo? page = OurPlaneCoreJobStore.TryReadPage(tab.PageFolder);", StringComparison.Ordinal) &&
            loadFromTabMethod.Contains("if (page == null &&", StringComparison.Ordinal) &&
            loadFromTabMethod.Contains("page = fallbackPage;", StringComparison.Ordinal),
            "LoadPageFromTab should prefer the latest source.json page snapshot before using a stale fallback page");
        AssertTrue(
            distinctBatchPagesMethod.Contains(".Select(page => OurPlaneCoreJobStore.TryReadPage(page.FolderPath))", StringComparison.Ordinal) &&
            distinctBatchPagesMethod.Contains(".Where(page => page != null)", StringComparison.Ordinal) &&
            distinctBatchPagesMethod.Contains(".Cast<PageInfo>()", StringComparison.Ordinal),
            "batch tab/detached opens should refresh selected pages from source.json before passing raster metadata into viewports");

        int loadPage = loadMethod.IndexOf("_viewport.LoadPage(", StringComparison.Ordinal);
        int annotationsLoad = loadMethod.IndexOf("LoadViewportPageAnnotations(viewportPage)", StringComparison.Ordinal);
        int overlayQueue = loadMethod.IndexOf("QueueSheetOverlayLoadForPageOpen(viewportPage, restoreView)", StringComparison.Ordinal);
        int deferred = loadMethod.IndexOf("QueueDeferredPageOpenWork", StringComparison.Ordinal);
        AssertTrue(
            loadPage >= 0 && annotationsLoad > loadPage && overlayQueue > annotationsLoad && deferred > overlayQueue,
            "page open should load the viewport, restore saved annotations before any page-switch autosave, queue async sheet overlay work, then schedule slower follow-up work");
        AssertTrue(
            loadMethod.Contains("_currentPageAnnotationsLoaded = false", StringComparison.Ordinal) &&
            loadAnnotationsMethod.Contains("OurPlaneCoreJobStore.LoadPageAnnotations(viewportPage.FolderPath)", StringComparison.Ordinal) &&
            loadAnnotationsMethod.Contains("_currentPageAnnotationsLoaded = true", StringComparison.Ordinal) &&
            loadAnnotationsMethod.Contains("ApplyRulerVisibilityToViewport()", StringComparison.Ordinal) &&
            saveAnnotationsMethod.Contains("_currentPage == null || !_currentPageAnnotationsLoaded", StringComparison.Ordinal),
            "page annotation save must be blocked during page switching until annotations.json has been loaded into the viewport");

        AssertFalse(
            loadMethod.Contains("LoadSheetOverlay(", StringComparison.Ordinal) ||
            loadMethod.Contains("ApplyViewportPageTakeoffVisibility(", StringComparison.Ordinal) ||
            loadMethod.Contains("ApplyScaleToCurrentPageMeasurements(", StringComparison.Ordinal) ||
            loadMethod.Contains("RefreshLoadedPageTakeoffVisuals(", StringComparison.Ordinal) ||
            loadMethod.Contains("SaveAppSettings();", StringComparison.Ordinal),
            "page open should not run overlays, takeoff visibility, measurement scale propagation, takeoff tree refresh, or settings save in the immediate path");
        AssertFalse(
            loadMethod.Contains("TryApplyCachedSheetOverlay(viewportPage, restoreView)", StringComparison.Ordinal),
            "page open should not synchronously decode cached sheet overlays before the first viewport frame");

        AssertTrue(
            queueMethod.Contains("Dispatcher.BeginInvoke", StringComparison.Ordinal) &&
            queueMethod.Contains("DispatcherPriority.Background", StringComparison.Ordinal) &&
            queueMethod.Contains("QueueNearbyPagePreviewPrefetchDeferred(deferredVersion, viewportPage)", StringComparison.Ordinal) &&
            queueMethod.Contains("RunDeferredPageOpenWorkWhenQuiet", StringComparison.Ordinal),
            "nearby prefetch should be scheduled before slow page-open follow-up work waits for background quiet");
        AssertTrue(
            deferredQuietMethod.Contains("WaitForDeferredPageOpenQuietAsync", StringComparison.Ordinal) &&
            deferredQuietMethod.Contains("RunDeferredPageOpenWork(deferredVersion, viewportPage, trace, restoreView)", StringComparison.Ordinal) &&
            deferredQuietMethod.Contains("if (!handedOff)", StringComparison.Ordinal) &&
            quietWaitMethod.Contains("ViewportRenderPolicy.PageOpenDeferredNavigationQuietMs", StringComparison.Ordinal) &&
            quietWaitMethod.Contains("_viewport.NavigationQuietDelay(quietWindow)", StringComparison.Ordinal) &&
            policy.Contains("PageOpenDeferredNavigationQuietMs = 1800", StringComparison.Ordinal),
            "deferred page-open work should wait for a viewport navigation quiet window before refreshing settings, tree state, and legends");

        AssertTrue(
            deferredMethod.Contains("IsCurrentPageOpen(deferredVersion, viewportPage.FolderPath)", StringComparison.Ordinal) &&
            deferredMethod.Contains("QueueJobPagePreviewWarmupDeferred(deferredVersion, viewportPage)", StringComparison.Ordinal) &&
            deferredMethod.Contains("QueueJobRasterSheetRefreshWarmupDeferred(deferredVersion, viewportPage)", StringComparison.Ordinal) &&
            deferredMethod.Contains("ApplyViewportPageTakeoffVisibility(viewportPage)", StringComparison.Ordinal) &&
            !deferredMethod.Contains("OurPlaneCoreJobStore.LoadPageAnnotations(viewportPage.FolderPath)", StringComparison.Ordinal) &&
            deferredMethod.Contains("IReadOnlyList<TakeoffItem> scaledItems = ApplyScaleToCurrentPageMeasurements(viewportPage.ScaleMetersPerPt)", StringComparison.Ordinal) &&
            deferredMethod.Contains("RefreshLoadedPageTakeoffVisuals(viewportPage.FolderPath, scaledItems)", StringComparison.Ordinal) &&
            deferredMethod.Contains("SaveAppSettings();", StringComparison.Ordinal),
            "deferred page-open work should keep the previous follow-up operations behind a stale-page guard");
        AssertFalse(
            deferredMethod.Contains("QueueNearbyPagePreviewPrefetchDeferred(deferredVersion, viewportPage)", StringComparison.Ordinal),
            "nearby page prefetch should not wait behind the long deferred page-open quiet window");
        AssertFalse(
            deferredMethod.Contains("LoadSheetOverlay(", StringComparison.Ordinal),
            "deferred page-open work should not restart sheet overlay loading after the immediate async overlay queue");

        AssertTrue(
            prefetchMethod.Contains("DispatcherPriority.ContextIdle", StringComparison.Ordinal) &&
            prefetchMethod.Contains("IsCurrentPageOpen(deferredVersion, viewportPage.FolderPath)", StringComparison.Ordinal) &&
            prefetchMethod.Contains("QueueNearbyPagePreviewPrefetch(viewportPage)", StringComparison.Ordinal),
            "nearby preview prefetch should be queued after page-open critical work and guarded against stale pages");
        AssertTrue(
            warmupMethod.Contains("DispatcherPriority.ContextIdle", StringComparison.Ordinal) &&
            warmupMethod.Contains("_pagePreviewWarmupJobRoot", StringComparison.Ordinal) &&
            warmupMethod.Contains("QueueJobPagePreviewWarmup(viewportPage)", StringComparison.Ordinal) &&
            pageTabs.Contains("Task.Run(() =>", StringComparison.Ordinal) &&
            pageTabs.Contains("LoadPagesForPreviewPrefetch(pagesRoot)", StringComparison.Ordinal) &&
            warmupRunMethod.Contains("BuildPreviewWarmupOrder(pages.Count, activeIndex, previewWarmupCount)", StringComparison.Ordinal) &&
            warmupRunMethod.Contains("ViewportRenderPolicy.SelectJobOpenPreviewWarmupCount(pages.Count)", StringComparison.Ordinal) &&
            warmupRunMethod.Contains("ViewportRenderPolicy.SelectJobOpenRasterSheetBitmapWarmupCount(pages.Count)", StringComparison.Ordinal) &&
            warmupRunMethod.Contains("HashSet<int> rasterWarmupIndexes", StringComparison.Ordinal) &&
            warmupRunMethod.Contains("rasterWarmupIndexes.Contains(index)", StringComparison.Ordinal) &&
            warmupRunMethod.Contains("ViewportRenderPolicy.ColdPageSwitchPreviewRenderScale", StringComparison.Ordinal) &&
            warmupRunMethod.Contains("includeRasterSheetWarmup: includeRasterSheetWarmup", StringComparison.Ordinal) &&
            warmupRunMethod.Contains("includeRasterSheetRefresh: false", StringComparison.Ordinal) &&
            pagePreviewWarmup.Contains("BuildEvenlyDistributedWarmupIndexes", StringComparison.Ordinal) &&
            pagePreviewWarmup.Contains("JobOpenPreviewWarmupPriorityLocalRadius", StringComparison.Ordinal) &&
            pagePreviewWarmup.Contains("BuildLocalPageWarmupOrder(count, activeIndex)", StringComparison.Ordinal) &&
            policy.Contains("JobOpenPreviewWarmupAllPages = true", StringComparison.Ordinal) &&
            policy.Contains("JobOpenPreviewWarmupPriorityLocalRadius = 2", StringComparison.Ordinal) &&
            policy.Contains("JobOpenPreviewWarmupLocalRadius = 8", StringComparison.Ordinal) &&
            policy.Contains("JobOpenPreviewWarmupSpreadAnchorCount = 12", StringComparison.Ordinal) &&
            policy.Contains("JobOpenPreviewWarmupCount = 48", StringComparison.Ordinal) &&
            policy.Contains("JobOpenPreviewWarmupLargeJobCount = 64", StringComparison.Ordinal) &&
            policy.Contains("JobOpenPreviewWarmupHugeJobCount = 96", StringComparison.Ordinal) &&
            policy.Contains("JobOpenRasterSheetBitmapWarmupCount = 12", StringComparison.Ordinal) &&
            policy.Contains("JobOpenRasterSheetBitmapWarmupHugeJobCount = 24", StringComparison.Ordinal) &&
            policy.Contains("SelectJobOpenPreviewWarmupCount", StringComparison.Ordinal),
            "job-open preview warmup should run once per job at idle priority, keep preview work bounded, and limit heavier raster bitmap queues to active-page-first nearby sheets");
        AssertTrue(
            rasterWarmupMethod.Contains("DispatcherPriority.ContextIdle", StringComparison.Ordinal) &&
            rasterWarmupMethod.Contains("_pageRasterRefreshWarmupJobRoot", StringComparison.Ordinal) &&
            rasterWarmupMethod.Contains("QueueJobRasterSheetRefreshWarmup(viewportPage)", StringComparison.Ordinal) &&
            rasterWarmupRunMethod.Contains("BuildLocalPageWarmupOrder(pages.Count, activeIndex)", StringComparison.Ordinal) &&
            rasterWarmupRunMethod.Contains("ViewportRenderPolicy.SelectJobOpenRasterSheetRefreshWarmupCount(pages.Count)", StringComparison.Ordinal) &&
            rasterWarmupRunMethod.Contains("QueueJobRasterSheetRefreshWarmup(queuedPages)", StringComparison.Ordinal) &&
            rasterWarmupQueueMethod.Contains("PdfViewport.PrefetchRasterSheetRefresh(page)", StringComparison.Ordinal) &&
            policy.Contains("JobOpenRasterSheetRefreshWarmupAllPages = true", StringComparison.Ordinal) &&
            policy.Contains("JobOpenRasterSheetRefreshWarmupCount = 12", StringComparison.Ordinal) &&
            policy.Contains("JobOpenRasterSheetRefreshWarmupLargeJobCount = 16", StringComparison.Ordinal) &&
            policy.Contains("JobOpenRasterSheetRefreshWarmupHugeJobCount = 24", StringComparison.Ordinal) &&
            policy.Contains("RasterSheetRefreshPrefetchCadenceMs = 6500", StringComparison.Ordinal),
            "job-open raster warmup should queue stale raster refreshes separately from preview rendering while keeping the heavy refresh queue bounded");
        AssertTrue(
            nearbyPrefetchQueueMethod.Contains("Task.Run", StringComparison.Ordinal) &&
            nearbyPrefetchQueueMethod.Contains("string previousPageFolder = _lastNearbyPagePreviewPrefetchFolder", StringComparison.Ordinal) &&
            nearbyPrefetchQueueMethod.Contains("_lastNearbyPagePreviewPrefetchFolder = activePage.FolderPath", StringComparison.Ordinal) &&
            nearbyPrefetchQueueMethod.Contains("LoadPagesForPreviewPrefetch(pagesRoot)", StringComparison.Ordinal) &&
            loadPagesForPrefetchMethod.Contains("LoadPagesForPreviewPrefetch(pagesRoot, pages)", StringComparison.Ordinal) &&
            pagePreviewWarmup.Contains("OurPlaneCoreJobStore.GetOrderedChildDirectories(folderPath)", StringComparison.Ordinal) &&
            nearbyPrefetchMethod.Contains("ViewportRenderPolicy.NearbyPagePreviewPrefetchRadius", StringComparison.Ordinal) &&
            nearbyPrefetchMethod.Contains("FindPreviewPrefetchDirection(pages, activeIndex, previousPageFolder)", StringComparison.Ordinal) &&
            nearbyPrefetchMethod.Contains("DirectionalPrefetchRadius", StringComparison.Ordinal) &&
            nearbyPrefetchMethod.Contains("ViewportRenderPolicy.NearbyPageDirectionalPreviewPrefetchRadius", StringComparison.Ordinal) &&
            nearbyPrefetchMethod.Contains("ViewportRenderPolicy.NearbyPageDirectionalReadableBasePrefetchRadius", StringComparison.Ordinal) &&
            nearbyPrefetchMethod.Contains("QueuePreviewPrefetchAt(pages, activeIndex + offset)", StringComparison.Ordinal) &&
            nearbyPrefetchMethod.Contains("QueuePreviewPrefetchAt(pages, activeIndex - offset)", StringComparison.Ordinal) &&
            nearbyPrefetchMethod.Contains("ViewportRenderPolicy.NearbyPageReadableBasePrefetchRadius", StringComparison.Ordinal) &&
            nearbyPrefetchMethod.Contains("QueueReadableBasePrefetchAt(pages, activeIndex + offset)", StringComparison.Ordinal) &&
            nearbyPrefetchMethod.Contains("QueueReadableBasePrefetchAt(pages, activeIndex - offset)", StringComparison.Ordinal) &&
            nearbyPrefetchMethod.Contains("ViewportRenderPolicy.NearbyPageCleanRenderPrefetchRadius", StringComparison.Ordinal) &&
            queuePreviewPrefetchAtMethod.Contains("float renderScale = ViewportRenderPolicy.FastPageSwitchPreviewRenderScale", StringComparison.Ordinal) &&
            queuePreviewPrefetchAtMethod.Contains("bool includeRasterSheetWarmup = true", StringComparison.Ordinal) &&
            queuePreviewPrefetchAtMethod.Contains("bool includeRasterSheetRefresh = true", StringComparison.Ordinal) &&
            queuePreviewPrefetchAtMethod.Contains("PrefetchPagePreview(page.PdfPath, page.PdfPage, renderScale)", StringComparison.Ordinal) &&
            queuePreviewPrefetchAtMethod.Contains("if (includeRasterSheetWarmup)", StringComparison.Ordinal) &&
            queuePreviewPrefetchAtMethod.Contains("PrefetchRasterSheetBitmap", StringComparison.Ordinal) &&
            queuePreviewPrefetchAtMethod.Contains("PrefetchRasterSheetWorkZoomBitmaps", StringComparison.Ordinal) &&
            queuePreviewPrefetchAtMethod.Contains("if (includeRasterSheetRefresh)", StringComparison.Ordinal) &&
            queuePreviewPrefetchAtMethod.Contains("PrefetchRasterSheetRefresh", StringComparison.Ordinal) &&
            queueReadableBasePrefetchAtMethod.Contains("ViewportRenderPolicy.ResponsiveMinRenderScale", StringComparison.Ordinal) &&
            queueReadableBasePrefetchAtMethod.Contains("PrefetchPagePreview", StringComparison.Ordinal) &&
            queueReadableBasePrefetchAtMethod.Contains("preferCachedRenderImmediately: true", StringComparison.Ordinal) &&
            policy.Contains("NearbyPageReadableBasePrefetchRadius = HasSpareRenderCapacity ? 2 : 1", StringComparison.Ordinal) &&
            policy.Contains("NearbyPageDirectionalPreviewPrefetchRadius = HasSpareRenderCapacity ? 6 : 3", StringComparison.Ordinal) &&
            policy.Contains("NearbyPageDirectionalReadableBasePrefetchRadius = HasSpareRenderCapacity ? 5 : 3", StringComparison.Ordinal) &&
            policy.Contains("NearbyPageCleanRenderPrefetchRadius = HasSpareRenderCapacity ? 1 : 0", StringComparison.Ordinal) &&
            queueCleanRenderPrefetchAtMethod.Contains("PrefetchCleanLayerRender", StringComparison.Ordinal),
            "nearby page prefetch should warm cheap previews, cached-first readable base bitmaps, and extra sheets in the user's paging direction; capable machines also pre-render the immediate neighbour sharp via the prefetch pool");
    }

    public static void PageTabsSupportDragReorderAndDetach()
    {
        string pageTabs = ReadRepoFile("MainWindow.PageTabs.cs");
        string xaml = ReadRepoFile("MainWindow.xaml");

        AssertTrue(
            xaml.Contains("DragOver=\"PageTabs_DragOver\"", StringComparison.Ordinal) &&
            xaml.Contains("Drop=\"PageTabs_Drop\"", StringComparison.Ordinal) &&
            xaml.Contains("x:Name=\"ViewportHost\"", StringComparison.Ordinal) &&
            xaml.Contains("x:Name=\"ViewportSurfaceHost\"", StringComparison.Ordinal),
            "page tabs control and central viewport space should accept drops for detach");
        AssertTrue(
            pageTabs.Contains("item.PreviewMouseLeftButtonDown += PageTab_PreviewMouseLeftButtonDown", StringComparison.Ordinal) &&
            pageTabs.Contains("item.PreviewMouseLeftButtonUp += PageTab_PreviewMouseLeftButtonUp", StringComparison.Ordinal) &&
            pageTabs.Contains("item.PreviewMouseMove += PageTab_PreviewMouseMove", StringComparison.Ordinal) &&
            pageTabs.Contains("item.Drop += PageTab_Drop", StringComparison.Ordinal) &&
            pageTabs.Contains("DragDrop.DoDragDrop(PageTabs, dragged, DragDropEffects.Move)", StringComparison.Ordinal),
            "page tabs should start a tab drag from the tab item body");
        string tabMouseDown = SliceMethod(pageTabs, "private void PageTab_PreviewMouseLeftButtonDown");
        string tabMouseUp = SliceMethod(pageTabs, "private void PageTab_PreviewMouseLeftButtonUp");
        AssertTrue(
            tabMouseDown.Contains("e.Handled = _pendingPageTabDrag != null", StringComparison.Ordinal) &&
            tabMouseUp.Contains("ActivatePageTab(tab)", StringComparison.Ordinal),
            "page tabs should activate on click release, not mouse down, so starting a drag does not switch pages");
        AssertTrue(
            pageTabs.Contains("MovePageTab(dragged, target", StringComparison.Ordinal) &&
            pageTabs.Contains("_pageTabs.RemoveAt(oldIndex)", StringComparison.Ordinal) &&
            pageTabs.Contains("_pageTabs.Insert(insertIndex, dragged)", StringComparison.Ordinal),
            "dropping a page tab on another tab should reorder the existing tab list");
        AssertTrue(
            pageTabs.Contains("DetachPageTabFromDrag", StringComparison.Ordinal) &&
            pageTabs.Contains("OpenPageTabsInDetachedWindows([tab], tileOnSecondMonitor: false)", StringComparison.Ordinal) &&
            pageTabs.Contains("ClosePageTab(tab)", StringComparison.Ordinal),
            "dropping a page tab on empty tab strip space should detach it and remove the tab");
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
        string syncMethod = SliceMethod(pagesTree, "private void SyncPageTreeNodeForViewportOpen");

        AssertTrue(
            clickMethod.Contains("SelectPageTreeItemAndOpenIfPage(item)", StringComparison.Ordinal),
            "page-tree mouse clicks should not rely only on WPF SelectedItemChanged to open the viewport");
        AssertTrue(
            clickMethod.Contains("FindAncestor<ToggleButton>(e.OriginalSource as DependencyObject) != null", StringComparison.Ordinal) &&
            clickMethod.IndexOf("FindAncestor<ToggleButton>(e.OriginalSource as DependencyObject) != null", StringComparison.Ordinal) <
            clickMethod.IndexOf("SelectPageTreeItemAndOpenIfPage(item)", StringComparison.Ordinal),
            "page-tree expander clicks should bypass row selection/open logic so folders collapse on the first click");
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
        AssertTrue(
            syncMethod.Contains("PagesTree.SelectedItem is TreeViewItem { Tag: PageInfo selectedPage }", StringComparison.Ordinal) &&
            syncMethod.Contains("IsSamePageFolder(selectedPage.FolderPath, pageFolder)", StringComparison.Ordinal),
            "deferred viewport-to-tree sync should not re-expand a user-collapsed folder when the current page is already selected");
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
            pageTabs.Contains("_pagePreviewWarmupJobRoot = \"\";", StringComparison.Ordinal) &&
            pageTabs.Contains("_pageRasterRefreshWarmupJobRoot = \"\";", StringComparison.Ordinal) &&
            !pageTabs.Contains("_pagePreviewPrefetchPages", StringComparison.Ordinal) &&
            pageTabs.Contains("LoadPagesForPreviewPrefetch(pagesRoot)", StringComparison.Ordinal),
            "preview prefetch invalidation should clear job warmup guards while page list loading stays background-only");
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

    public static void TreeMarqueeMultiSelectionIsWired()
    {
        string window = ReadRepoFile("MainWindow.xaml.cs");
        string pagesTree = ReadRepoFile("MainWindow.PagesTree.cs");
        string pagesDragDrop = ReadRepoFile("MainWindow.PagesDragDrop.cs");
        string takeoffsTree = ReadRepoFile("MainWindow.TakeoffsTree.cs");
        string takeoffsDragDrop = ReadRepoFile("MainWindow.TakeoffsDragDrop.cs");
        string marquee = ReadRepoFile("MainWindow.TreeMarqueeSelection.cs");

        AssertTrue(
            window.Contains("PagesTree.PreviewMouseLeftButtonUp += PagesTree_PreviewMouseLeftButtonUp;", StringComparison.Ordinal) &&
            window.Contains("PagesTree.LostMouseCapture += PagesTree_LostMouseCapture;", StringComparison.Ordinal) &&
            window.Contains("TakeoffsTree.PreviewMouseLeftButtonUp += TakeoffsTree_PreviewMouseLeftButtonUp;", StringComparison.Ordinal) &&
            window.Contains("TakeoffsTree.LostMouseCapture += TakeoffsTree_LostMouseCapture;", StringComparison.Ordinal),
            "Pages and Takeoffs trees must finish/cancel marquee selection independently from drag/drop");

        AssertTrue(
            SliceMethod(pagesTree, "private void PagesTree_PreviewMouseLeftButtonDown(")
                .Contains("if (TryBeginPagesTreeMarqueeSelection(e))", StringComparison.Ordinal) &&
            SliceMethod(takeoffsTree, "private void TakeoffsTree_PreviewMouseLeftButtonDown(")
                .Contains("if (TryBeginTakeoffsTreeMarqueeSelection(e))", StringComparison.Ordinal),
            "tree marquee selection should get first chance on mouse down");

        AssertTrue(
            SliceMethod(pagesDragDrop, "private void PagesTree_MouseMove(")
                .Contains("if (UpdatePagesTreeMarqueeSelection(e))", StringComparison.Ordinal) &&
            SliceMethod(takeoffsDragDrop, "private void TakeoffsTree_MouseMove(")
                .Contains("if (UpdateTakeoffsTreeMarqueeSelection(e))", StringComparison.Ordinal),
            "tree marquee selection must update before the existing drag/drop move path starts");

        AssertTrue(
            marquee.Contains("AdornerLayer.GetAdornerLayer(tree)", StringComparison.Ordinal) &&
            marquee.Contains("private sealed class TreeSelectionMarqueeAdorner : Adorner", StringComparison.Ordinal) &&
            marquee.Contains("TreeItemIntersectsSelection(PagesTree, item, selectionRect)", StringComparison.Ordinal) &&
            marquee.Contains("TreeItemIntersectsSelection(TakeoffsTree, item, selectionRect)", StringComparison.Ordinal),
            "marquee selection should draw a visible rectangle and select rows by visible tree-item bounds");

        AssertTrue(
            marquee.Contains("_pagesMultiSelection.Clear();", StringComparison.Ordinal) &&
            marquee.Contains("_pageTakeoffMultiSelection.Clear();", StringComparison.Ordinal) &&
            marquee.Contains("_takeoffsMultiSelection.Clear();", StringComparison.Ordinal) &&
            marquee.Contains("_takeoffSectionMultiSelection.Clear();", StringComparison.Ordinal) &&
            marquee.Contains("item.Tag is TakeoffItem", StringComparison.Ordinal) &&
            marquee.Contains("item.Tag is TakeoffFolderNode { IsRoot: false }", StringComparison.Ordinal),
            "marquee selection should target Pages sheet/folder rows and Takeoffs item/folder rows without mixing section selections");

        AssertTrue(
            marquee.Contains("SelectPagesTreeMarqueeAnchorSilently()", StringComparison.Ordinal) &&
            marquee.Contains("SelectTakeoffsTreeMarqueeAnchorSilently()", StringComparison.Ordinal) &&
            marquee.Contains("ModifierKeys.Alt", StringComparison.Ordinal) &&
            marquee.Contains("ModifierKeys.Control", StringComparison.Ordinal),
            "marquee selection should preserve command anchors and support Alt-start plus Ctrl-additive selection");
    }

    public static void ProjectTreeCollapseAndTakeoffDeleteSelectionAreWired()
    {
        string xaml = ReadRepoFile("MainWindow.xaml");
        string expansion = ReadRepoFile("MainWindow.TreeExpansion.cs");
        string shortcuts = ReadRepoFile("MainWindow.Shortcuts.cs");
        string takeoffsClipboard = ReadRepoFile("MainWindow.TakeoffsClipboard.cs");

        AssertTrue(
            !xaml.Contains("Content=\"2-\"", StringComparison.Ordinal) &&
            !xaml.Contains("BtnCollapseProjectTreesFromPages", StringComparison.Ordinal) &&
            !xaml.Contains("BtnCollapseProjectTreesFromTakeoffs", StringComparison.Ordinal) &&
            xaml.Contains("x:Name=\"BtnCollapsePagesTree\"", StringComparison.Ordinal) &&
            xaml.Contains("Click=\"BtnCollapsePagesTree_Click\"", StringComparison.Ordinal) &&
            xaml.Contains("x:Name=\"BtnCollapseTakeoffsTree\"", StringComparison.Ordinal) &&
            xaml.Contains("Click=\"BtnCollapseTakeoffsTree_Click\"", StringComparison.Ordinal) &&
            xaml.Contains("ToolTip=\"Collapse all Pages nodes\"", StringComparison.Ordinal) &&
            xaml.Contains("ToolTip=\"Collapse all Takeoffs nodes\"", StringComparison.Ordinal),
            "the existing minus buttons should keep their old individual mouse behavior without adding a separate 2- button");

        AssertTrue(
            SliceMethod(expansion, "private void BtnCollapsePagesTree_Click")
                .Contains("SetProjectTreeExpanded(PagesTree, false, \"Pages tree collapsed.\");", StringComparison.Ordinal) &&
            SliceMethod(expansion, "private void BtnCollapseTakeoffsTree_Click")
                .Contains("SetProjectTreeExpanded(TakeoffsTree, false, \"Takeoffs tree collapsed.\");", StringComparison.Ordinal) &&
            expansion.Contains("private void CollapseProjectTreeDisplaysWithStatus()", StringComparison.Ordinal) &&
            expansion.Contains("CollapseProjectTreeDisplays();", StringComparison.Ordinal) &&
            expansion.Contains("Pages and Takeoffs trees collapsed.", StringComparison.Ordinal),
            "mouse minus buttons should collapse their own tree while the shared collapse helper remains available");

        AssertTrue(
            shortcuts.Contains("case Key.Subtract:", StringComparison.Ordinal) &&
            shortcuts.Contains("case Key.OemMinus:", StringComparison.Ordinal) &&
            shortcuts.Contains("CollapseProjectTreeDisplaysWithStatus();", StringComparison.Ordinal),
            "keyboard minus must collapse both project trees through the shared helper");

        AssertTrue(
            takeoffsClipboard.Contains("TryDeleteTakeoffsKeyboardSelection()", StringComparison.Ordinal) &&
            takeoffsClipboard.Contains("TryDeleteSelectedTakeoffNodesFromKeyboard()", StringComparison.Ordinal) &&
            takeoffsClipboard.Contains("TryDeleteSelectedTakeoffSectionsFromKeyboard()", StringComparison.Ordinal) &&
            takeoffsClipboard.Contains("FirstSelectedTakeoffSectionNode()", StringComparison.Ordinal),
            "Takeoffs Delete key should route through the current selection sets instead of only TreeView.SelectedItem");

        AssertTrue(
            SliceMethod(takeoffsClipboard, "private bool TryDeleteSelectedTakeoffNodesFromKeyboard()")
                .Contains("_takeoffsMultiSelection.Contains(selectedPath)", StringComparison.Ordinal) &&
            SliceMethod(takeoffsClipboard, "private bool TryDeleteSelectedTakeoffNodesFromKeyboard()")
                .Contains("FirstSelectedTakeoffTreeItem()", StringComparison.Ordinal) &&
            SliceMethod(takeoffsClipboard, "private bool TryDeleteSelectedTakeoffSectionsFromKeyboard()")
                .Contains("_takeoffSectionMultiSelection.Contains(TakeoffSectionSelectionKey(selectedNode))", StringComparison.Ordinal) &&
            SliceMethod(takeoffsClipboard, "private bool TryDeleteSelectedTakeoffSectionsFromKeyboard()")
                .Contains("FirstSelectedTakeoffSectionNode()", StringComparison.Ordinal),
            "Delete must prefer the visual multi-selection but still fall back to the selected row");
    }

    public static void FastRefreshDisabledForDataSafety()
    {
        string source = ReadRepoFile("MainWindow.TakeoffsTreeFastRefresh.cs");
        string dragSource = ReadRepoFile("MainWindow.TakeoffsDragDrop.cs");
        string clipboardSource = ReadRepoFile("MainWindow.TakeoffsClipboard.cs");
        string actionsSource = ReadRepoFile("MainWindow.TakeoffsNodeActions.cs");
        string orderMethod = SliceMethod(source, "private bool TryRefreshTakeoffTreeParentOrderFast(");
        string structureMethod = SliceMethod(source, "private bool TryApplyTakeoffStructureMoveFast(");
        string fallbackMethod = SliceMethod(source, "private void ReloadTakeoffsForMoveSelection(");
        AssertFalse(
            source.Contains("FastTakeoffsTreeRefreshEnabled = true", StringComparison.Ordinal),
            "broad takeoffs tree structure refresh must not be re-enabled");
        AssertFalse(
            orderMethod.Contains("FastTakeoffsTreeRefreshEnabled", StringComparison.Ordinal),
            "same-parent takeoff reorder should use the targeted existing-item refresh instead of reloading the whole tree");
        AssertFalse(
            structureMethod.Contains("if (!FastTakeoffsTreeRefreshEnabled)", StringComparison.Ordinal) ||
            structureMethod.Contains("LoadTakeoffsForJob();", StringComparison.Ordinal),
            "cross-parent takeoff moves should try a targeted existing-subtree refresh before full reload fallback");
        AssertTrue(
            structureMethod.Contains("UnregisterTakeoffTreeItemSubtree(item)", StringComparison.Ordinal) &&
            structureMethod.Contains("oldParent.Items.Remove(item)", StringComparison.Ordinal) &&
            structureMethod.Contains("RebaseTakeoffTreeItemPath(item, oldPath, newPath)", StringComparison.Ordinal) &&
            structureMethod.Contains("RebaseExpandedTreePaths(_expandedTakeoffTreePaths, oldPath, newPath)", StringComparison.Ordinal) &&
            structureMethod.Contains("RebaseTakeoffRangeAnchorPath(oldPath, newPath)", StringComparison.Ordinal) &&
            structureMethod.Contains("targetControl.Items.Add(item)", StringComparison.Ordinal) &&
            structureMethod.Contains("RegisterTakeoffTreeItemSubtree(item)", StringComparison.Ordinal) &&
            structureMethod.Contains("TryRefreshTakeoffTreeParentOrderFast(", StringComparison.Ordinal),
            "cross-parent takeoff moves should rebase and move existing UI subtrees, then order the target parent");
        AssertTrue(
            fallbackMethod.Contains("SelectFirstTakeoffPathForMoveFast(selectedPaths)", StringComparison.Ordinal) &&
            fallbackMethod.Contains("RefreshFastMoveActiveState(selected, previousActivePath)", StringComparison.Ordinal),
            "move fallback reloads should restore the moved takeoff selection silently without running page-opening selection handlers");
        AssertTrue(
            dragSource.Contains("ReloadTakeoffsForMoveSelection(changed, previousActivePath)", StringComparison.Ordinal) &&
            clipboardSource.Contains("ReloadTakeoffsForMoveSelection(changed, previousActivePath)", StringComparison.Ordinal) &&
            actionsSource.Contains("ReloadTakeoffsForMoveSelection(paths, previousActivePath)", StringComparison.Ordinal),
            "takeoff drag/drop, cut/paste, and move up/down fallbacks should not use raw SelectFirstTakeoffPath after reload");
    }

    public static void TakeoffSelectionUsesTargetedUiRefresh()
    {
        string treeSource = ReadRepoFile("MainWindow.TakeoffsTree.cs");
        string navigationSource = ReadRepoFile("MainWindow.TakeoffSelectionNavigation.cs");
        string selectionMethod = SliceMethod(treeSource, "private void TakeoffsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)");
        string revealMethod = SliceMethod(navigationSource, "private void RevealPagesForTakeoffItems(");
        string scheduleMethod = SliceMethod(treeSource, "private void ScheduleTakeoffSelectionSync(Action action)");
        string scheduledRunMethod = SliceMethod(treeSource, "private void RunScheduledTakeoffSelectionSync(int version, Action action)");

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
        AssertTrue(
            revealMethod.Contains("BringPageTreeItemIntoCenteredView(preferredLinked)", StringComparison.Ordinal) &&
            !revealMethod.Contains("preferredLinked.IsSelected = true", StringComparison.Ordinal) &&
            revealMethod.Contains("_pageTakeoffMultiSelection.Add(PageTakeoffSelectionKey(new PageTakeoffNode(page, takeoff)))", StringComparison.Ordinal),
            "Takeoffs-tree reveal should scroll to and highlight linked Pages rows without selecting PageTakeoffNode and opening its sheet");
        AssertTrue(
            scheduleMethod.Contains("RunScheduledTakeoffSelectionSync(version, action)", StringComparison.Ordinal) &&
            scheduledRunMethod.Contains("_takeoffsDragStart != null && Mouse.LeftButton == MouseButtonState.Pressed", StringComparison.Ordinal) &&
            scheduledRunMethod.Contains("System.Windows.Threading.DispatcherTimer", StringComparison.Ordinal) &&
            scheduledRunMethod.Contains("ResetTakeoffsDragState();", StringComparison.Ordinal),
            "takeoff selection sync should wait out mouse-held drag arming instead of opening a page during drag/drop");

        string pagesSource = ReadRepoFile("MainWindow.PagesTree.cs");
        string activePageRefreshMethod = SliceMethod(pagesSource, "private void RefreshPageTakeoffIndicatorsForActiveChange(");
        string knownPageRefreshMethod = SliceMethod(pagesSource, "private void RefreshKnownPageTakeoffIndicatorsForFolders(");
        AssertTrue(
            pagesSource.Contains("private void RefreshPageTakeoffIndicatorsForActiveChange(", StringComparison.Ordinal) &&
            activePageRefreshMethod.Contains("RefreshKnownPageTakeoffIndicatorsForFolders(pageFolders)", StringComparison.Ordinal),
            "targeted selection refresh should keep the touched page refresh path narrow");
        AssertTrue(
            knownPageRefreshMethod.Contains("FindPageTreeItemByFolderKeyIndexed(key)", StringComparison.Ordinal) &&
            knownPageRefreshMethod.Contains("TryRefreshPageTreeItemFromStore(item, folder, out string refreshedKey)", StringComparison.Ordinal) &&
            knownPageRefreshMethod.Contains("if (rebuiltAny)") &&
            knownPageRefreshMethod.Contains("RebuildPageTreeItemIndex();") &&
            knownPageRefreshMethod.Contains("RefreshPageTreeRowsByFolderKeys(refreshed)", StringComparison.Ordinal),
            "targeted selection refresh should re-read touched page rows so stale folder nodes become sheets without a full reload");

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
            "copy fast path must stay independent from the broader cross-parent move refresh gate");

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
        string estimatingSource = ReadRepoFile("MainWindow.Estimating.cs");
        string selectionSource = ReadRepoFile("MainWindow.TakeoffsSelectionHelpers.cs");
        string treeSource = ReadRepoFile("MainWindow.TakeoffsTree.cs");
        string mouseMove = SliceMethod(source, "private void TakeoffsTree_MouseMove(object sender, MouseEventArgs e)");
        string dropSections = SliceMethod(source, "private void DropTakeoffSections(");
        string sectionTarget = SliceMethod(estimatingSource, "private static Measurement ResolveTakeoffSectionSelectionTarget(");

        AssertTrue(
            mouseMove.Contains("e.LeftButton != MouseButtonState.Pressed", StringComparison.Ordinal) &&
            mouseMove.Contains("ResetTakeoffsDragState();", StringComparison.Ordinal),
            "takeoffs drag state must reset when the mouse is released before a drag starts");
        AssertTrue(
            mouseMove.Contains("CancelPendingTakeoffSelectionSync();", StringComparison.Ordinal) &&
            mouseMove.Contains("DoTakeoffsDragDrop(sectionPayload", StringComparison.Ordinal) &&
            mouseMove.Contains("DoTakeoffsDragDrop(payload", StringComparison.Ordinal),
            "takeoffs drag start must cancel pending click selection sync before drag/drop can navigate");
        AssertTrue(
            mouseMove.Contains("TakeoffSectionNodesWithAnchorFirst(", StringComparison.Ordinal) &&
            dropSections.Contains("SelectTakeoffSectionMeasurementsOnCanvas(resultingNodes, resultingNodes[0])", StringComparison.Ordinal) &&
            !source.Contains("SelectDroppedTakeoffSectionsOnCurrentPage", StringComparison.Ordinal) &&
            estimatingSource.Contains("TakeoffMeasurementNode? primaryNode", StringComparison.Ordinal) &&
            sectionTarget.Contains("ReferenceEquals(node.Measurement, primaryNode.Measurement)", StringComparison.Ordinal) &&
            selectionSource.Contains("private static List<TakeoffMeasurementNode> TakeoffSectionNodesWithAnchorFirst", StringComparison.Ordinal) &&
            treeSource.Contains("SelectTakeoffSectionMeasurementsOnCanvas(SelectedTakeoffSectionNodes(node, fallbackToAnchor: false), node)", StringComparison.Ordinal),
            "section/count row selection and drag/drop should navigate to the active row's measurement page instead of keeping or choosing an unrelated page");
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

    public static void PageTreeRefreshReclassifiesStaleFolderNodes()
    {
        string pagesTree = ReadRepoFile("MainWindow.PagesTree.cs");
        string refreshAll = SliceMethod(pagesTree, "private void RefreshPagesTakeoffIndicators()");
        string refreshOne = SliceMethod(pagesTree, "private void RefreshPageTakeoffIndicatorsForFolder");
        string refreshMany = SliceMethod(pagesTree, "private void RefreshPageTakeoffIndicatorsForFolders");
        string snapshots = SliceMethod(pagesTree, "private void RefreshPageTreePageSnapshots");
        string activeRefresh = SliceMethod(pagesTree, "private void RefreshKnownPageTakeoffIndicatorsForFolders");
        string helper = SliceMethod(pagesTree, "private bool TryRefreshPageTreeItemFromStore");

        AssertTrue(
            helper.Contains("OurPlaneCoreJobStore.TryReadPage(pageFolder)", StringComparison.Ordinal) &&
            helper.Contains("item.Tag = refreshedPage;", StringComparison.Ordinal) &&
            helper.Contains("item.Header = BuildPageHeader(refreshedPage);", StringComparison.Ordinal) &&
            helper.Contains("RebuildPageTakeoffNodes(item, refreshedPage);", StringComparison.Ordinal),
            "page tree partial refresh should re-read source.json and convert stale folder nodes back into page nodes");
        AssertTrue(
            refreshAll.Contains("item.Tag is PageTakeoffNode or PageOverlayNode", StringComparison.Ordinal) &&
            refreshAll.Contains("TryRefreshPageTreeItemFromStore(item, path, out _)", StringComparison.Ordinal) &&
            refreshAll.Contains("if (reloadNeeded)", StringComparison.Ordinal) &&
            refreshAll.Contains("RebuildPageTreeItemIndex();", StringComparison.Ordinal),
            "full indicator refresh should reclassify page/folder nodes and rebuild the index after changing child nodes");
        AssertTrue(
            refreshOne.Contains("FindPageTreeItemByFolder(pageFolder) is not { } item", StringComparison.Ordinal) &&
            refreshOne.Contains("TryRefreshPageTreeItemFromStore(item, pageFolder, out string refreshedKey)", StringComparison.Ordinal) &&
            refreshOne.Contains("ReloadPagesTree(pageFolder, selectSilently: true)", StringComparison.Ordinal) &&
            refreshMany.Contains("TryRefreshPageTreeItemFromStore(item, folder, out string refreshedKey)", StringComparison.Ordinal) &&
            snapshots.Contains("TryRefreshPageTreeItemFromStore(item, page.FolderPath, out string refreshedKey)", StringComparison.Ordinal) &&
            activeRefresh.Contains("TryRefreshPageTreeItemFromStore(item, folder, out string refreshedKey)", StringComparison.Ordinal),
            "targeted page refreshes should not leave a repaired sheet displayed as a folder node");
    }

    public static void PagesEscAndFolderIconsAreWired()
    {
        string pagesCommands = ReadRepoFile("MainWindow.PagesCommands.cs");
        string pagesTree = ReadRepoFile("MainWindow.PagesTree.cs");
        string takeoffVisuals = ReadRepoFile("MainWindow.TakeoffTreeVisuals.cs");

        AssertTrue(
            pagesCommands.Contains("key == Key.Escape", StringComparison.Ordinal) &&
            pagesCommands.Contains("ClearPagesTreeSelectionFromEscape", StringComparison.Ordinal) &&
            pagesCommands.Contains("_pagesMultiSelection.Clear();", StringComparison.Ordinal) &&
            pagesCommands.Contains("_pageTakeoffMultiSelection.Clear();", StringComparison.Ordinal),
            "Pages tree should clear page and linked-takeoff selection on Esc before requiring a selected item");
        AssertTrue(
            pagesTree.Contains("CreateFolderTreeIcon(pageCount > 0)", StringComparison.Ordinal) &&
            takeoffVisuals.Contains("private static FrameworkElement CreateFolderTreeIcon(bool filled)", StringComparison.Ordinal) &&
            takeoffVisuals.Contains("TakeoffFolderHasContent", StringComparison.Ordinal) &&
            takeoffVisuals.Contains("SetFolderTreeItemHeader", StringComparison.Ordinal),
            "Pages and Takeoffs folder rows should share a small folder icon, filled when the folder has content");
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
            references.Contains("RebasePageOverlayReferences(normalizedMoves)", StringComparison.Ordinal),
            "Pages moves should rebase sheet overlay page references in one pass");
        AssertTrue(
            flushMethod.Contains("ReloadPagesTree(selectPath, selectSilently: true)", StringComparison.Ordinal) &&
            !flushMethod.Contains("PagesTree.UpdateLayout()", StringComparison.Ordinal) &&
            !flushMethod.Contains("PagesTree.Items.Refresh()", StringComparison.Ordinal),
            "Pages drop refresh should not synchronously relayout or open the moved sheet through selection change");
    }

    public static void PagesMovedActiveSheetRebindsViewportWithoutReload()
    {
        string references = ReadRepoFile("MainWindow.PagePathReferences.cs");
        string updateMethod = SliceMethod(references, "private bool UpdatePageReferencesForMovedPaths");
        string rebindMethod = SliceMethod(references, "private bool TryRebindCurrentPageAfterMovedPath");
        string viewport = ReadRepoFile("Controls/PdfViewport.PageApi.cs");
        string metadataApply = ReadRepoFile("MainWindow.PagesPdfMetadata.cs");

        AssertTrue(
            updateMethod.Contains("TryRebindCurrentPageAfterMovedPath(normalizedMoves)", StringComparison.Ordinal) &&
            updateMethod.Contains("return reloadActiveTab;", StringComparison.Ordinal),
            "moved active page references should try to rebind the existing viewport before requesting a reload");
        AssertTrue(
            rebindMethod.Contains("_viewport.TryRebindCurrentPageFolder(", StringComparison.Ordinal) &&
            rebindMethod.Contains("_currentPage = rebasedPage;", StringComparison.Ordinal) &&
            !rebindMethod.Contains("_viewport.ClearPage()", StringComparison.Ordinal),
            "active page rebind should update current page state without clearing the visible page");
        AssertTrue(
            viewport.Contains("public bool TryRebindCurrentPageFolder(", StringComparison.Ordinal) &&
            viewport.Contains("_pageFolder = newPageFolder;", StringComparison.Ordinal) &&
            viewport.Contains("_pageBitmapPageFolder = newPageFolder;", StringComparison.Ordinal),
            "viewport should expose a narrow page-folder rebind for same-PDF page moves");
        AssertTrue(
            metadataApply.Contains("UpdatePageReferencesForMovedPath(currentPath, renamedPath)", StringComparison.Ordinal) &&
            !metadataApply.Contains("_currentPage = null;\r\n        _currentPdfPath = \"\";\r\n        ReloadPagesTree();", StringComparison.Ordinal) &&
            !metadataApply.Contains("_currentPage = null;\n        _currentPdfPath = \"\";\n        ReloadPagesTree();", StringComparison.Ordinal),
            "metadata rename apply should rebase page references instead of forcing the current sheet to reopen");
    }

    public static void PdfSheetMetadataLayerDiscoveryRestoresLayerStates()
    {
        // Normalize endings: the helper file may be checked out with CRLF.
        string helper = ReadRepoFile("Tools/pdf_layers_helper.py").Replace("\r\n", "\n", StringComparison.Ordinal);
        int start = helper.IndexOf("def _page_layer_names(", StringComparison.Ordinal);
        int end = helper.IndexOf("\n\ndef _cached_layers(", start, StringComparison.Ordinal);
        AssertTrue(start >= 0 && end > start, "pdf helper page-layer discovery function should be present");
        string pageLayerNames = helper[start..end];

        AssertTrue(
            pageLayerNames.Contains("previous_states", StringComparison.Ordinal) &&
            pageLayerNames.Contains("_set_all_layers(doc, True, doc_key=doc_key)", StringComparison.Ordinal) &&
            pageLayerNames.Contains("finally:", StringComparison.Ordinal) &&
            pageLayerNames.Contains("_set_layer_state(doc, doc_key, layer_id, on)", StringComparison.Ordinal),
            "metadata layer discovery should restore the cached PDF layer states after temporarily enabling layers");
    }

    public static void SheetManagerNameEditsStayCheckedAndDoNotSelectAllOnFocus()
    {
        string workspaceManagers = ReadRepoFile("MainWindow.WorkspaceManagers.cs");
        string previewDialog = ReadRepoFile("Dialogs/PdfMetadataPreviewDialog.cs");
        string metadataTextBox = ReadRepoFile("Controls/PdfMetadataTextBoxBehavior.cs");
        string createTemplate = SliceMethod(workspaceManagers, "private static DataTemplate CreateSheetManagerTextBoxTemplate");
        string textChanged = SliceMethod(workspaceManagers, "private static void SheetManagerTextBox_TextChanged");
        string markMethod = SliceMethod(workspaceManagers, "private void MarkSheetManagerTextRowForApply");
        string restoreMethod = SliceMethod(workspaceManagers, "private void RestoreSheetManagerTextSelection");
        string bulkMethod = SliceMethod(workspaceManagers, "private void ApplySheetManagerTextToSelectedRows");
        string lostFocus = SliceMethod(workspaceManagers, "private static void SheetManagerTextBox_LostKeyboardFocus");
        string refreshMethod = SliceMethod(workspaceManagers, "private void RefreshSheetManager()");
        string rasterRowsMethod = SliceMethod(workspaceManagers, "private bool RefreshSheetManagerRasterRows");
        string previewMouse = SliceMethod(metadataTextBox, "protected override void OnPreviewMouseLeftButtonDown");
        string selectionChanged = SliceMethod(metadataTextBox, "protected override void OnSelectionChanged");
        string clearSelection = SliceMethod(metadataTextBox, "private void ClearWholeSelection");
        string editablePreviewTemplate = SliceMethod(previewDialog, "private static DataTemplate EditableTextTemplate");

        AssertTrue(
            createTemplate.Contains("new FrameworkElementFactory(typeof(PdfMetadataTextBox))", StringComparison.Ordinal) &&
            metadataTextBox.Contains("public sealed class PdfMetadataTextBox : TextBox", StringComparison.Ordinal) &&
            previewMouse.Contains("e.Handled = true;", StringComparison.Ordinal) &&
            previewMouse.Contains("ProtectCaret(caret);", StringComparison.Ordinal) &&
            selectionChanged.Contains("ClearWholeSelection(_protectedCaret);", StringComparison.Ordinal) &&
            clearSelection.Contains("Select(safeCaret, 0);", StringComparison.Ordinal) &&
            !metadataTextBox.Contains("SelectAll", StringComparison.Ordinal),
            "Sheet Manager name/scale cells should take ownership of the first click and place the caret instead of selecting all text");
        AssertTrue(
            previewDialog.Contains("EditableTextColumn(\"Proposed Name\", nameof(PdfMetadataPreviewRow.ProposedPageName)", StringComparison.Ordinal) &&
            previewDialog.Contains("EditableTextColumn(\"Scale\", nameof(PdfMetadataPreviewRow.ProposedScale)", StringComparison.Ordinal) &&
            editablePreviewTemplate.Contains("new FrameworkElementFactory(typeof(PdfMetadataTextBox))", StringComparison.Ordinal) &&
            editablePreviewTemplate.Contains("PdfMetadataPreviewTextBox_TextChanged", StringComparison.Ordinal),
            "Name/Scale preview dialog should use the same protected editable text cells as Sheet Manager");
        AssertTrue(
            textChanged.Contains("owner.MarkSheetManagerTextRowForApply(editedRow, bindingPath, value)", StringComparison.Ordinal) &&
            markMethod.Contains("row.ApplyRename = ShouldApplySheetManagerRename(row, value);", StringComparison.Ordinal) &&
            markMethod.Contains("row.ApplyScale = ShouldApplySheetManagerScale(row, value);", StringComparison.Ordinal),
            "Sheet Manager text edits should immediately mark the edited row for apply");
        AssertTrue(
            textChanged.Contains("owner.RestoreSheetManagerTextSelection(textBox, value, selectionStart, selectionLength)", StringComparison.Ordinal) &&
            restoreMethod.Contains("textBox.Select(start, length);", StringComparison.Ordinal) &&
            bulkMethod.Contains("if (ReferenceEquals(row, editedRow))", StringComparison.Ordinal),
            "Sheet Manager edits should not rebind the actively edited cell or leave its text selected after each key");
        AssertTrue(
            workspaceManagers.Contains("private bool _sheetManagerRefreshPendingAfterEdit;", StringComparison.Ordinal) &&
            refreshMethod.Contains("if (IsSheetManagerTextEditActive())", StringComparison.Ordinal) &&
            refreshMethod.Contains("_sheetManagerRefreshPendingAfterEdit = true;", StringComparison.Ordinal) &&
            lostFocus.Contains("owner.RefreshSheetManager();", StringComparison.Ordinal) &&
            rasterRowsMethod.Contains("if (IsSheetManagerTextEditActive())", StringComparison.Ordinal),
            "Sheet Manager background refreshes should wait until the active name/scale edit loses focus");
    }

    public static void PageRepairUsesMovedJobSuffixForNonEmptyReferences()
    {
        string source = ReadRepoFile("MainWindow.JobLifecycle.cs");
        string repairMethod = SliceMethod(source, "private int RepairMeasurementPageFolderReferences()");
        string nonEmptyBranch = repairMethod[
            repairMethod.IndexOf("string oldPath = NormalizePageReferencePath(measurement.PageFolder);", StringComparison.Ordinal)..];
        string resolver = SliceMethod(source, "private static PageInfo? ResolveMeasurementPage(");
        string movedResolver = SliceMethod(source, "private static PageInfo? ResolveMovedJobPage(");

        AssertTrue(
            nonEmptyBranch.Contains("matchedPage = ResolveMeasurementPage(", StringComparison.Ordinal) &&
            nonEmptyBranch.Contains("_currentJob.PagesRoot", StringComparison.Ordinal),
            "non-empty PageFolder repair should route stale paths through the moved-job resolver");
        int movedIndex = resolver.IndexOf("ResolveMovedJobPage(oldPath, pagesRoot, pagesByPath)", StringComparison.Ordinal);
        int leafIndex = resolver.IndexOf("string leaf = Path.GetFileName(oldPath);", StringComparison.Ordinal);
        AssertTrue(
            movedIndex >= 0 && leafIndex >= 0 && movedIndex < leafIndex,
            "moved-job suffix matching should run before loose leaf-name fallback");
        AssertTrue(
            movedResolver.Contains("TryGetSuffixAfterPathSegment(oldPath, \"Pages\"", StringComparison.Ordinal) &&
            movedResolver.Contains("Path.Combine([normalizedPagesRoot, .. suffixSegments])", StringComparison.Ordinal),
            "moved-job repair should only rebase the suffix after a real Pages path segment");
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

    public static void MeasurementPastePreservesCountSymbol()
    {
        string supportTypes = ReadRepoFile("MainWindow.SupportTypes.cs");
        string clipboard = ReadRepoFile("MainWindow.MeasurementClipboard.cs");
        string copyMethod = SliceMethod(clipboard, "private void CopyMeasurementsToClipboard(");
        string resolveMethod = SliceMethod(clipboard, "private TakeoffItem ResolveMeasurementPasteTarget(");
        string cloneMethod = SliceMethod(clipboard, "private Measurement CloneClipboardMeasurement(");

        AssertTrue(
            supportTypes.Contains("string MeasurementCountSymbol", StringComparison.Ordinal) &&
            supportTypes.Contains("string SourceTakeoffCountSymbol", StringComparison.Ordinal),
            "measurement clipboard entries must carry the Count symbol from both measurement and source takeoff item");
        AssertTrue(
            copyMethod.Contains("measurement.CountSymbol", StringComparison.Ordinal) &&
            copyMethod.Contains("item?.CountSymbol ?? \"\"", StringComparison.Ordinal),
            "copying selected Count measurements must capture the current Count symbol");
        AssertTrue(
            resolveMethod.Contains("target.CountSymbol = MeasurementClipboardTakeoffCountSymbol(entry)", StringComparison.Ordinal),
            "pasting into a new Count takeoff must copy the source takeoff symbol instead of using the current default");
        AssertTrue(
            cloneMethod.Contains("CountSymbol = measurementType == \"point\"", StringComparison.Ordinal) &&
            cloneMethod.Contains("ResolveMeasurementClipboardCountSymbol(entry, target)", StringComparison.Ordinal),
            "pasted Count measurements must keep their source symbol so the viewport does not fall back to a circle");
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
        string viewportSelectionEditing = ReadRepoFile("Controls/PdfViewport.SelectionEditing.cs");
        string viewportInput = ReadRepoFile("Controls/PdfViewport.Input.cs");
        string viewportAnnotationRendering = ReadRepoFile("Controls/PdfViewport.AnnotationRendering.cs");
        string viewportMeasurementApi = ReadRepoFile("Controls/PdfViewport.MeasurementApi.cs");
        string displaySizing = ReadRepoFile("MainWindow.DisplaySettings.MeasurementSizing.cs");
        string settings = ReadRepoFile("Models/AppSettingsStore.cs");

        AssertTrue(
            xaml.Contains("PagesFolderSearchBox", StringComparison.Ordinal) &&
            xaml.Contains("PagesFolderSearchCaseToggle", StringComparison.Ordinal) &&
            xaml.Contains("PagesFolderSearchCaseToggle_Changed", StringComparison.Ordinal) &&
            xaml.Contains("PagesTreeSearchBox", StringComparison.Ordinal) &&
            xaml.Contains("TakeoffsFolderSearchBox", StringComparison.Ordinal) &&
            xaml.Contains("TakeoffsFolderSearchCaseToggle", StringComparison.Ordinal) &&
            xaml.Contains("TakeoffsFolderSearchCaseToggle_Changed", StringComparison.Ordinal) &&
            xaml.Contains("TakeoffsTreeSearchBox", StringComparison.Ordinal) &&
            xaml.Contains("IsChecked=\"True\"", StringComparison.Ordinal) &&
            treeSearch.Contains("PageTreeFolderSearchText", StringComparison.Ordinal) &&
            treeSearch.Contains("PageTreePageSearchText", StringComparison.Ordinal) &&
            treeSearch.Contains("TakeoffTreeFolderSearchText", StringComparison.Ordinal) &&
            treeSearch.Contains("TakeoffTreeItemSearchText", StringComparison.Ordinal) &&
            treeSearch.Contains("FolderSearchComparison", StringComparison.Ordinal) &&
            treeSearch.Contains("StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase", StringComparison.Ordinal) &&
            treeSearch.Contains("ApplyPagesTreeSearchFilter", StringComparison.Ordinal) &&
            treeSearch.Contains("ApplyTakeoffsTreeSearchFilter", StringComparison.Ordinal),
            "Pages and Takeoffs trees must expose split folder/item search boxes with default-on folder case toggles");
        AssertTrue(
            takeoffsClipboard.Contains("FirstSelectedTakeoffTreeItem", StringComparison.Ordinal) &&
            takeoffsClipboard.Contains("TryDeleteSelectedTakeoffNodesFromKeyboard", StringComparison.Ordinal) &&
            takeoffsClipboard.Contains("DeleteTakeoffNodes(fallback)", StringComparison.Ordinal),
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
        AssertTrue(
            xaml.Contains("SldRulerThickness", StringComparison.Ordinal) &&
            xaml.Contains("TxtRulerStrokeWidth", StringComparison.Ordinal) &&
            displaySizing.Contains("NormalizeRulerStrokeWidth", StringComparison.Ordinal) &&
            settings.Contains("ViewportRulerStrokeWidth", StringComparison.Ordinal) &&
            viewportAnnotationRendering.Contains("RulerStrokeWidthPx()", StringComparison.Ordinal),
            "ruler thickness must be a separate persisted Viewport control with a 1px default");
        AssertTrue(
            xaml.Contains("SldPdfSnapBridgeTolerance", StringComparison.Ordinal) &&
            xaml.Contains("TxtPdfSnapBridgeTolerance", StringComparison.Ordinal) &&
            displaySizing.Contains("NormalizePdfSnapBridgeTolerance", StringComparison.Ordinal) &&
            settings.Contains("ViewportPdfSnapBridgeTolerancePx", StringComparison.Ordinal),
            "PDF Snap bridge radius must be a separate persisted Viewport control");
        AssertTrue(
            viewportSelectionEditing.Contains("_dragAnnotationSelectionOriginalPoints", StringComparison.Ordinal) &&
            viewportSelectionEditing.Contains("Moving {selected.Count} selected markups.", StringComparison.Ordinal) &&
            viewportInput.Contains("foreach (var (annotation, originalPoints) in _dragAnnotationSelectionOriginalPoints)", StringComparison.Ordinal),
            "selected ruler/markup annotations must drag as a group instead of collapsing to the primary annotation");
    }

    public static void AnnotationTabHighlighterIsWired()
    {
        string xaml = ReadRepoFile("MainWindow.xaml");
        string mainWindow = ReadRepoFile("MainWindow.xaml.cs");
        string toolControls = ReadRepoFile("MainWindow.ToolControls.cs");
        string viewCommands = ReadRepoFile("Controls/PdfViewport.ViewCommands.cs");
        string viewport = ReadRepoFile("Controls/PdfViewport.cs");
        string tools = ReadRepoFile("Controls/PdfViewport.Tools.cs");
        string rendering = ReadRepoFile("Controls/PdfViewport.AnnotationRendering.cs");
        string store = ReadRepoFile("Models/Storage/PageAnnotationStore.cs");
        string exporter = ReadRepoFile("Models/PdfExporter.Annotations.cs");

        AssertTrue(
            xaml.Contains("<TabItem Header=\"Annotation\">", StringComparison.Ordinal) &&
            xaml.Contains("x:Name=\"BtnHighlight\"", StringComparison.Ordinal) &&
            xaml.Contains("Tag=\"drawhighlight\"", StringComparison.Ordinal),
            "Annotation ribbon tab should expose a visible Highlighter tool");
        AssertTrue(
            mainWindow.Contains("[\"drawhighlight\"] = BtnHighlight", StringComparison.Ordinal) &&
            toolControls.Contains("AddAnnotationToolItem(menu, \"Highlighter\", \"drawhighlight\")", StringComparison.Ordinal),
            "MainWindow should include Highlighter in tool button and annotation menu wiring");
        AssertTrue(
            viewport.Contains("DrawHighlight", StringComparison.Ordinal) &&
            viewCommands.Contains("\"drawhighlight\" => ViewerTool.DrawHighlight", StringComparison.Ordinal) &&
            tools.Contains("AddTwoPointAnnotation(pdf, \"highlight\")", StringComparison.Ordinal),
            "Viewport should route drawhighlight into a separate highlight annotation");
        AssertTrue(
            store.Contains("\"highlight\" or \"highlighter\" => \"highlight\"", StringComparison.Ordinal) &&
            rendering.Contains("DrawHighlightAnnotation", StringComparison.Ordinal) &&
            exporter.Contains("kind == \"highlight\"", StringComparison.Ordinal),
            "Highlight annotations should persist, render, and export separately from area fills");
    }

    public static void ViewportRenameAndCadBoxSelectionAreWired()
    {
        string viewport = ReadRepoFile("Controls/PdfViewport.cs");
        string input = ReadRepoFile("Controls/PdfViewport.Input.cs");
        string callbacks = ReadRepoFile("MainWindow.ViewportCallbacks.cs");
        string mainWindow = ReadRepoFile("MainWindow.xaml.cs");
        string boxSelection = ReadRepoFile("Controls/PdfViewport.BoxSelection.cs");

        AssertTrue(
            viewport.Contains("TakeoffRenameRequested", StringComparison.Ordinal) &&
            input.Contains("case Key.F2", StringComparison.Ordinal) &&
            input.Contains("TryRequestTakeoffRenameAt", StringComparison.Ordinal) &&
            input.Contains("TakeoffRenameRequested?.Invoke", StringComparison.Ordinal),
            "Viewport F2 and double-click should request a takeoff-level rename");
        AssertTrue(
            mainWindow.Contains("_viewport.TakeoffRenameRequested += OnViewportTakeoffRenameRequested", StringComparison.Ordinal) &&
            callbacks.Contains("FindTakeoffItemForMeasurement(measurement)", StringComparison.Ordinal) &&
            callbacks.Contains("RenameItem(tvi, item)", StringComparison.Ordinal),
            "MainWindow should rename the owning takeoff item, not an individual viewport segment");
        AssertTrue(
            boxSelection.Contains("bool selectTouched = _boxSelectEndPdf.X < _boxSelectStartPdf.X;", StringComparison.Ordinal) &&
            boxSelection.Contains("? \"crossing\"", StringComparison.Ordinal) &&
            boxSelection.Contains(": \"inside only\"", StringComparison.Ordinal),
            "Box selection should use CAD direction: right-to-left crossing, left-to-right enclosed");
    }

    public static void TransformScaleSliderLabelIsWired()
    {
        string xaml = ReadRepoFile("MainWindow.xaml");
        string toolControls = ReadRepoFile("MainWindow.ToolControls.cs");

        AssertTrue(
            xaml.Contains("x:Name=\"TxtScaleSelectionFactor\"", StringComparison.Ordinal) &&
            xaml.Contains("KeyDown=\"TxtScaleSelectionFactor_KeyDown\"", StringComparison.Ordinal) &&
            xaml.Contains("LostFocus=\"TxtScaleSelectionFactor_LostFocus\"", StringComparison.Ordinal) &&
            toolControls.Contains("TxtScaleSelectionFactor.Text = FormatTransformScaleLabel(value);", StringComparison.Ordinal) &&
            toolControls.Contains("ApplyTransformScaleTextEntry", StringComparison.Ordinal) &&
            toolControls.Contains("TryParseTransformScaleFactor", StringComparison.Ordinal) &&
            toolControls.Contains("value /= 100.0;", StringComparison.Ordinal),
            "Transform scale field should display slider value and accept typed scale factors like 1.25x or 125%");
    }

    public static void TakeoffFolderRandomColorsAreWired()
    {
        string menus = ReadRepoFile("MainWindow.TakeoffsMenus.cs");
        string colors = ReadRepoFile("MainWindow.TakeoffsRandomColors.cs");
        string visuals = ReadRepoFile("MainWindow.TakeoffTreeVisuals.cs");

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
        AssertTrue(
            visuals.Contains("TakeoffTreeGlyphSize = 14", StringComparison.Ordinal) &&
            visuals.Contains("BuildTakeoffSwatchGlyph(item, swatchBrush, TakeoffTreeGlyphSize)", StringComparison.Ordinal) &&
            !visuals.Contains("isActive ? 18 : 16", StringComparison.Ordinal),
            "Takeoffs tree takeoff symbols should stay the same 14px size for active and inactive rows");
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
        string transform = ReadRepoFile("Controls/PdfViewport.TransformEditing.cs");
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
            areaCut.Contains("TryBuildAreaCutGeometry", StringComparison.Ordinal) &&
            areaCut.Contains("TryBuildAreaCutGeometries", StringComparison.Ordinal) &&
            areaCut.Contains("MeasurementAreaBooleanService.TrySubtract", StringComparison.Ordinal) &&
            areaCut.Contains("MeasurementAreaBooleanService.TrySubtractAll", StringComparison.Ordinal) &&
            areaCut.Contains("AreaCutReducedFilledArea", StringComparison.Ordinal) &&
            areaCut.Contains("CloneLineMeasurement", StringComparison.Ordinal) &&
            areaCut.Contains("CloneAreaMeasurement", StringComparison.Ordinal) &&
            areaCut.Contains("PushMixedMeasurementUndo", StringComparison.Ordinal) &&
            areaCut.Contains("NotifyMeasurementsRemoved(removedAreas.Concat(removedLines).ToList())", StringComparison.Ordinal) &&
            areaCut.Contains("NotifyMeasurementsAdded(addedMeasurements)", StringComparison.Ordinal),
            "Cut tool must apply the same box/polygon gesture to Area bites/holes, through-area splits, and Line eraser pieces");

        AssertTrue(
            transform.Contains("ShouldScaleFromTopLeftAnchor", StringComparison.Ordinal) &&
            transform.Contains("UpdateTopLeftAnchoredScaleDrag", StringComparison.Ordinal) &&
            transform.Contains("new(b.Left, b.Top)", StringComparison.Ordinal) &&
            transform.Contains("TransformHandleKind.ScaleRight", StringComparison.Ordinal) &&
            transform.Contains("TransformHandleKind.ScaleBottom", StringComparison.Ordinal) &&
            transform.Contains("TransformHandleKind.ScaleBottomRight", StringComparison.Ordinal) &&
            transform.Contains("UpdateCenteredScaleDrag", StringComparison.Ordinal),
            "orange transform handles should keep top-left anchored for right/bottom scale paths while preserving legacy centered/left behavior");

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
            rendering.Contains("ShowMeasurementLabels && ShowAreaLabels && ShowJoistLabels", StringComparison.Ordinal),
            "viewport joist summary labels must obey the All, Area, and Joist display toggles");
        AssertTrue(
            joistRendering.Contains("ShouldDrawJoistSegmentLabels(measurement)", StringComparison.Ordinal) &&
            joistRendering.Contains("measurement.JoistShowLabels", StringComparison.Ordinal),
            "per-joist segment labels, not the joist summary label, must obey the Label each joist item toggle");
        AssertTrue(
            pdfExporter.Contains("ShouldExportJoistSummaryLabel(options)", StringComparison.Ordinal) &&
            pdfExporter.Contains("options.ShowMeasurementLabels && options.ShowAreaLabels && options.ShowJoistLabels", StringComparison.Ordinal),
            "PDF export joist summary labels must obey the All, Area, and Joist output toggles");
    }

    public static void DisplayLabelTogglesRefreshDetachedSheets()
    {
        string displaySettings = ReadRepoFile("MainWindow.DisplaySettings.cs");
        string detachedWindow = ReadRepoFile("Dialogs/DetachedSheetWindow.cs");
        string displayClick = SliceMethod(displaySettings, "private void DisplaySetting_Click(");
        string detachedRefresh = SliceMethod(detachedWindow, "public void RefreshTakeoffDisplay(");
        string detachedApply = SliceMethod(detachedWindow, "private void ApplyViewportDisplaySettings(");

        AssertTrue(
            displayClick.Contains("SyncMeasurementLabelMasterForIndividualToggle(sender)", StringComparison.Ordinal) &&
            displayClick.Contains("RefreshDetachedSheetDisplaySettings()", StringComparison.Ordinal),
            "Display label toggles must auto-enable the All label master when needed and refresh detached sheets");
        AssertTrue(
            displaySettings.Contains("ReferenceEquals(sender, ChkDisplayLineLabels)", StringComparison.Ordinal) &&
            displaySettings.Contains("ReferenceEquals(sender, ChkDisplayAreaLabels)", StringComparison.Ordinal) &&
            displaySettings.Contains("ReferenceEquals(sender, ChkDisplayJoistLabels)", StringComparison.Ordinal) &&
            displaySettings.Contains("ReferenceEquals(sender, ChkDisplayCountLabels)", StringComparison.Ordinal),
            "Line, Area, Joist, and Count toggles must be treated as individual label toggles");
        AssertTrue(
            detachedRefresh.Contains("ApplyViewportDisplaySettings(settings, unitMode)", StringComparison.Ordinal) &&
            detachedRefresh.Contains("_viewport.InvalidateVisual()", StringComparison.Ordinal),
            "Detached sheet refresh must apply display settings and repaint immediately");
        AssertTrue(
            detachedApply.Contains("_viewport.ShowMeasurementLabels = settings.ShowMeasurementLabels", StringComparison.Ordinal) &&
            detachedApply.Contains("_viewport.ShowLineLabels = settings.ShowLineLabels", StringComparison.Ordinal) &&
            detachedApply.Contains("_viewport.ShowAreaLabels = settings.ShowAreaLabels", StringComparison.Ordinal) &&
            detachedApply.Contains("_viewport.ShowJoistLabels = settings.ShowJoistLabels", StringComparison.Ordinal) &&
            detachedApply.Contains("_viewport.ShowCountLabels = settings.ShowCountLabels", StringComparison.Ordinal),
            "Detached sheet display settings must include every label visibility flag");
    }

    public static void PageTakeoffSelectionSyncsTakeoffsTree()
    {
        string pagesTree = ReadRepoFile("MainWindow.PagesTree.cs");
        string pagesSelection = ReadRepoFile("MainWindow.PagesSelection.cs");
        string pageLegend = ReadRepoFile("MainWindow.PageTakeoffLegend.ContextMenu.cs");
        string navigation = ReadRepoFile("MainWindow.TakeoffSelectionNavigation.cs");
        string applyVisual = SliceMethod(pagesSelection, "private void ApplyPageTreeItemVisual(");

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
        AssertFalse(
            applyVisual.Contains("IsActivePageTakeoffNode(item)", StringComparison.Ordinal),
            "Page tree linked-takeoff rows should only use selection highlighting when the user selected those linked rows");
        AssertTrue(
            applyVisual.Contains("_pageTakeoffMultiSelection.Count > 0 && IsPageMeasuredByActiveTakeoff(item)", StringComparison.Ordinal),
            "Page row active-takeoff highlighting should clear when ordinary Pages-tree selection clears linked-takeoff selection");
    }

    public static void TakeoffTreeSectionRowsDefaultHiddenAndSettingWired()
    {
        string settings = ReadRepoFile("Models/AppSettingsStore.cs");
        string settingsManager = ReadRepoFile("MainWindow.SettingsManager.cs");
        string sections = ReadRepoFile("MainWindow.TakeoffSections.cs");
        string selectionHelpers = ReadRepoFile("MainWindow.TakeoffsSelectionHelpers.cs");
        string navigation = ReadRepoFile("MainWindow.TakeoffSelectionNavigation.cs");

        AssertTrue(
            settings.Contains("public bool ShowTakeoffSectionsInTree { get; set; } = false;", StringComparison.Ordinal),
            "takeoff section rows must default hidden for compact Takeoffs tree startup");
        AssertTrue(
            settingsManager.Contains("Show section/count rows under takeoffs", StringComparison.Ordinal) &&
            settingsManager.Contains("SetTakeoffSectionRowsVisible(true)", StringComparison.Ordinal) &&
            settingsManager.Contains("SetTakeoffSectionRowsVisible(false)", StringComparison.Ordinal) &&
            settingsManager.Contains("RefreshTakeoffSectionTreeVisibility()", StringComparison.Ordinal),
            "Settings > Defaults must expose the opt-in checkbox and refresh the Takeoffs tree immediately");
        AssertTrue(
            sections.Contains("if (!_settings.ShowTakeoffSectionsInTree)", StringComparison.Ordinal) &&
            sections.Contains("itemNode.Items.Clear();", StringComparison.Ordinal) &&
            sections.Contains("itemNode.IsExpanded = false;", StringComparison.Ordinal),
            "Takeoffs tree item refresh must skip nested measurement rows unless the setting is enabled");
        AssertTrue(
            selectionHelpers.Contains("if (!_settings.ShowTakeoffSectionsInTree)", StringComparison.Ordinal) &&
            selectionHelpers.Contains("SelectTakeoffItemsSilently(items, items[0])", StringComparison.Ordinal) &&
            navigation.Contains("FindTakeoffItemForMeasurement(measurement)", StringComparison.Ordinal),
            "hidden section rows must fall back to selecting their owning takeoff item");
    }

    public static void PageMeasurementVisibilityToggleIsWired()
    {
        string pagesTree = ReadRepoFile("MainWindow.PagesTree.cs");
        string pageTakeoffLegend = ReadRepoFile("MainWindow.PageTakeoffLegend.cs");
        string pageVisibility = ReadRepoFile("MainWindow.PageMeasurementVisibility.cs");
        string pageTakeoffVisibility = ReadRepoFile("MainWindow.PageTakeoffLegend.Visibility.cs");
        string pageTakeoffMenu = ReadRepoFile("MainWindow.PageTakeoffLegend.ContextMenu.cs");
        string viewportApi = ReadRepoFile("Controls/PdfViewport.MeasurementApi.cs");
        string viewportSelection = ReadRepoFile("Controls/PdfViewport.SelectionState.cs");
        string models = ReadRepoFile("Models/OurPlaneCoreJobModels.cs");
        string pageStore = ReadRepoFile("Models/Storage/PageStore.cs");

        AssertTrue(
            pagesTree.Contains("BuildPageMeasurementVisibilityDot(page)", StringComparison.Ordinal) &&
            pagesTree.Contains("HasCurrentPageMeasurements(page)", StringComparison.Ordinal) &&
            pagesTree.Contains("IsPageMeasurementVisibilityToggleSource(e.OriginalSource as DependencyObject)", StringComparison.Ordinal) &&
            pagesTree.Contains("TogglePageMeasurementVisibilitySnapshot(visibilityPage)", StringComparison.Ordinal) &&
            pagesTree.Contains("IsPageMeasurementVisibilityToggleSource(source)", StringComparison.Ordinal),
            "Pages tree sheet rows with measurements must expose a non-dragging dot that toggles all current sheet measurements");
        AssertTrue(
            pageVisibility.Contains("SavePageMeasurementVisibility(page, [], hiddenMeasurements)", StringComparison.Ordinal) &&
            pageVisibility.Contains("SavePageMeasurementVisibility(page, [], [])", StringComparison.Ordinal) &&
            pageVisibility.Contains("Width = 6", StringComparison.Ordinal) &&
            pageVisibility.Contains("Height = 6", StringComparison.Ordinal) &&
            pageVisibility.Contains("private bool HasCurrentPageMeasurements(PageInfo page)", StringComparison.Ordinal) &&
            pageVisibility.Contains("CurrentMeasurementsForPage(page)", StringComparison.Ordinal) &&
            pageVisibility.Contains("New measurements stay visible", StringComparison.Ordinal),
            "sheet dot must be compact, hide a snapshot of current measurement IDs, and clear the snapshot to show all");
        AssertTrue(
            pageTakeoffLegend.Contains("PageTakeoffVisibilityToggleTag", StringComparison.Ordinal) &&
            pageTakeoffLegend.Contains("BuildPageTakeoffVisibilityGlyph", StringComparison.Ordinal) &&
            pageTakeoffLegend.Contains("BuildTakeoffSwatchGlyph(", StringComparison.Ordinal) &&
            pageTakeoffLegend.Contains("Click the symbol to toggle", StringComparison.Ordinal) &&
            pageTakeoffLegend.Contains("PageTakeoffGlyphSize = 14", StringComparison.Ordinal) &&
            pageTakeoffLegend.Contains("PageTakeoffActiveGlyphSize = PageTakeoffGlyphSize", StringComparison.Ordinal) &&
            pageTakeoffLegend.Contains("PageTakeoffGlyphHostSize = PageTakeoffGlyphSize", StringComparison.Ordinal) &&
            pageTakeoffLegend.Contains("PageTakeoffActiveGlyphHostSize = PageTakeoffActiveGlyphSize", StringComparison.Ordinal) &&
            pageTakeoffLegend.Contains("Padding = new Thickness(0)", StringComparison.Ordinal) &&
            pageTakeoffLegend.Contains("FontSize          = 11", StringComparison.Ordinal) &&
            pageTakeoffLegend.Contains("BorderBrush = Brushes.Transparent", StringComparison.Ordinal) &&
            pageTakeoffLegend.Contains("BorderThickness = new Thickness(0)", StringComparison.Ordinal) &&
            !pageTakeoffLegend.Contains("Text              = $\"{legendIndex + 1}.\"", StringComparison.Ordinal) &&
            !pageTakeoffLegend.Contains("BuildPageTakeoffVisibilityDot", StringComparison.Ordinal),
            "linked page takeoff rows must use compact clickable glyphs without a separate colored dot, outline, or left index");
        AssertTrue(
            pagesTree.Contains("IsPageTakeoffVisibilityToggleSource(e.OriginalSource as DependencyObject)", StringComparison.Ordinal) &&
            pagesTree.Contains("TogglePageTakeoffVisibility(visibilityTakeoff.Page, visibilityTakeoff.Takeoff)", StringComparison.Ordinal) &&
            pagesTree.Contains("if (IsPageTakeoffVisibilityToggleSource(source))", StringComparison.Ordinal),
            "clicking the linked-takeoff glyph must toggle sheet visibility without starting selection or drag");
        AssertTrue(
            pageTakeoffVisibility.Contains("PageInfo visibilityPage = _currentPage;", StringComparison.Ordinal) &&
            pageTakeoffVisibility.Contains("_viewport.SetHiddenMeasurementIds(visibilityPage.HiddenMeasurements)", StringComparison.Ordinal),
            "viewport visibility apply must use the current page state so deferred page-open work cannot restore stale hidden IDs");
        AssertTrue(
            pageTakeoffVisibility.Contains("IsMeasurementHiddenByPageSnapshot(page, measurement)", StringComparison.Ordinal) &&
            pageTakeoffVisibility.Contains("RemoveHiddenMeasurementsForTakeoffs(page, [takeoff], hiddenMeasurements)", StringComparison.Ordinal) &&
            pageTakeoffMenu.Contains("RemoveHiddenMeasurementsForTakeoffs(", StringComparison.Ordinal),
            "individual linked-takeoff show must be able to reveal only the selected takeoff after a sheet snapshot hide");
        AssertTrue(
            viewportApi.Contains("public void SetHiddenMeasurementIds", StringComparison.Ordinal) &&
            viewportApi.Contains("InvalidateMeasurementVisibilityCache();", StringComparison.Ordinal) &&
            viewportSelection.Contains("_hiddenMeasurementIds.Contains(measurement.Id.Trim())", StringComparison.Ordinal) &&
            viewportSelection.Contains("if (!HasMeasurementVisibilityFilters())", StringComparison.Ordinal) &&
            viewportSelection.Contains("_measurementVisibilityVersion", StringComparison.Ordinal),
            "viewport drawing, hit-test, and selection paths must respect hidden measurement IDs");
        AssertTrue(
            models.Contains("[JsonPropertyName(\"hidden_measurements\")]", StringComparison.Ordinal) &&
            pageStore.Contains("SavePageHiddenMeasurements", StringComparison.Ordinal) &&
            pageStore.Contains("HiddenMeasurements = NormalizeStringList(hiddenMeasurements)", StringComparison.Ordinal),
            "source.json persistence must keep hidden measurement IDs through page rewrites");
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
            overlay.Contains("SheetOverlayRenderCache.TryWrite", StringComparison.Ordinal) &&
            overlay.Contains("TryBuildSheetOverlayBitmapFromRasterSheet", StringComparison.Ordinal) &&
            overlay.Contains("RasterSheetCacheService.TryReadReady", StringComparison.Ordinal) &&
            overlay.Contains("Sheet overlay raster cache hit", StringComparison.Ordinal) &&
            overlay.Contains("QueueSheetOverlayRenderCacheWrite", StringComparison.Ordinal) &&
            overlay.Contains("MinimumBrightSheetOverlayOpacity = 0.82", StringComparison.Ordinal) &&
            overlay.Contains("SheetOverlayAlphaBoost = 1.85", StringComparison.Ordinal) &&
            overlay.Contains("SheetOverlayTintStyleVersion = \"bright-v2\"", StringComparison.Ordinal) &&
            overlay.Contains("BuildBrightSheetOverlayColor", StringComparison.Ordinal),
            "sheet overlay rendering must read persisted cache, reuse ready raster sheets before PDF rendering, write after tinting, and keep overlays bright");
        AssertTrue(
            overlay.Contains("SKColor[] sourcePixels = source.Pixels", StringComparison.Ordinal) &&
            overlay.Contains("tinted.Pixels = tintedPixels", StringComparison.Ordinal) &&
            !overlay.Contains("source.GetPixel(x, y)", StringComparison.Ordinal) &&
            !overlay.Contains("tinted.SetPixel(x, y", StringComparison.Ordinal),
            "sheet overlay tinting should use a single pixel-array pass instead of per-pixel bitmap calls");
        AssertTrue(
            cache.Contains("OURPLANECORE_SHEET_OVERLAY_CACHE_ROOT", StringComparison.Ordinal) &&
            cache.Contains("render-cache", StringComparison.Ordinal) &&
            cache.Contains("sheet-overlay", StringComparison.Ordinal) &&
            cache.Contains("OverlayPdfFingerprint", StringComparison.Ordinal) &&
            cache.Contains("BuildPdfFingerprint", StringComparison.Ordinal) &&
            cache.Contains("TryReadRelocatedCache", StringComparison.Ordinal) &&
            cache.Contains("PromoteRelocatedCache", StringComparison.Ordinal) &&
            cache.Contains("TintStyleVersion = \"bright-v2\"", StringComparison.Ordinal) &&
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
        string rotationSmoke = ReadRepoFile("Tools/pdf_takeoff_import_rotation_smoke.py");

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
        AssertTrue(
            helper.Contains("_pdf_takeoff_points_from_annot_vertices(annot)", StringComparison.Ordinal) &&
            helper.Contains("_pdf_takeoff_unrotated_page_height(page)", StringComparison.Ordinal) &&
            helper.Contains("_rotate_pdf_takeoff_points_for_page(page, points)", StringComparison.Ordinal) &&
            rotationSmoke.Contains("page.set_rotation(90)", StringComparison.Ordinal) &&
            rotationSmoke.Contains("_assert_points_close", StringComparison.Ordinal),
            "PDF takeoff annotation import must normalize raw annotation geometry into rotated page.rect coordinates");
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
        string currentSnapLoad = SliceMethod(pdfSnap, "private async Task LoadPdfSnapPointsAsync(");

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
        AssertTrue(
            currentSnapLoad.Contains("var rasterSnap = await Task.Run", StringComparison.Ordinal) &&
            currentSnapLoad.Contains("RasterSheetCacheService.TryReadSnapIndex", StringComparison.Ordinal) &&
            currentSnapLoad.Contains("PdfGeometrySnapService.TryReadSnapPointsAsync", StringComparison.Ordinal),
            "current sheet PDF Snap should keep raster snap-index reads off the synchronous page-open path");
    }

    public static void RasterSnapStrictBlackLinesOnlyIsWired()
    {
        string helper = ReadRepoFile(Path.Combine("Tools", "pdf_layers_helper.py"));
        string raster = ReadRepoFile("Models/RasterSheetCacheService.cs");
        string pdfSnap = ReadRepoFile("Controls/PdfViewport.PdfSnap.cs");
        string snapService = ReadRepoFile("Models/PdfGeometrySnapService.cs");

        AssertTrue(
            helper.Contains("black_only = bool(req.get(\"black_only\", False))", StringComparison.Ordinal) &&
            helper.Contains("strict_lines = black_only", StringComparison.Ordinal) &&
            helper.Contains("_snap_drawing_stroke_width", StringComparison.Ordinal) &&
            helper.Contains("\"stroke_width\"", StringComparison.Ordinal) &&
            helper.Contains("if black_only and not _is_snap_drawing_dark(drawing):", StringComparison.Ordinal) &&
            helper.Contains("return _is_dark_pdf_color(drawing.get(\"color\"))", StringComparison.Ordinal),
            "raster snap helper should filter to strict dark/black PDF stroke geometry and keep stroke width");
        AssertTrue(
            helper.Contains("elif strict_lines:\r\n        return", StringComparison.Ordinal) ||
            helper.Contains("elif strict_lines:\n        return", StringComparison.Ordinal),
            "strict raster snap should ignore curves/quads instead of making approximate points");
        AssertTrue(
            helper.Contains("if not strict_lines and len(points) + len(segments) == before_count:", StringComparison.Ordinal),
            "strict raster snap must not synthesize geometry from drawing bounds");
        AssertFalse(
            raster.Contains("blackOnly: false", StringComparison.Ordinal),
            "raster snap cache must not fall back to all PDF vectors when no black linework is found");
        AssertTrue(
            raster.Contains("SnapIndexName = \"snap.json\"", StringComparison.Ordinal) &&
            raster.Contains("StrokeWidth", StringComparison.Ordinal) &&
            raster.Contains("[JsonPropertyName(\"stroke_width\")]", StringComparison.Ordinal) &&
            raster.Contains("snap index contains no geometry", StringComparison.Ordinal) &&
            snapService.Contains("StrokeWidth", StringComparison.Ordinal) &&
            snapService.Contains("[JsonPropertyName(\"stroke_width\")]", StringComparison.Ordinal) &&
            raster.Contains("blackOnly: true", StringComparison.Ordinal) &&
            pdfSnap.Contains("RasterSheetCacheService.TryReadSnapIndex", StringComparison.Ordinal) &&
            pdfSnap.Contains("PDF Snap ready from raster index", StringComparison.Ordinal),
            "viewport should prefer the persisted strict raster snap index before live PDF snap extraction");
        AssertTrue(
            pdfSnap.Contains("out string rasterSnapReason", StringComparison.Ordinal) &&
            pdfSnap.Contains("live PDF", StringComparison.Ordinal),
            "empty raster snap indexes must fall back to live PDF snap extraction instead of blocking contour tracing");
        AssertFalse(
            raster.Contains("TryEncodeReadableWorkingImage", StringComparison.Ordinal) ||
            raster.Contains("BoostPixel", StringComparison.Ordinal),
            "display raster cache images must not be pixel-boosted into blocky square linework");
    }

    public static void RasterSheetRenderSkipsDelayedPdfZoomRefresh()
    {
        string viewport = ReadRepoFile("Controls/PdfViewport.cs");
        string pageApi = ReadRepoFile("Controls/PdfViewport.PageApi.cs");
        string layers = ReadRepoFile("Controls/PdfViewport.Layers.cs");
        string pdfSnap = ReadRepoFile("Controls/PdfViewport.PdfSnap.cs");
        string viewTransform = ReadRepoFile("Controls/PdfViewport.ViewTransform.cs");
        string detailRender = ReadRepoFile("Controls/PdfViewport.DetailRender.cs");
        string rendering = ReadRepoFile("Controls/PdfViewport.Rendering.cs");
        string rasterSheetViewport = ReadRepoFile("Controls/PdfViewport.RasterSheet.cs");
        string rasterSheetDpiUpgrade = ReadRepoFile("Controls/PdfViewport.RasterSheetDpiUpgrade.cs");
        string rasterSheetPageOpenDpi = ReadRepoFile("Controls/PdfViewport.RasterSheetPageOpenDpi.cs");
        string rasterSheetBitmapCache = ReadRepoFile("Controls/PdfViewport.RasterSheetBitmapCache.cs");
        string rasterSheetPreparedApply = ReadRepoFile("Controls/PdfViewport.RasterSheetPreparedApply.cs");
        string rasterSheetReadySourceCache = ReadRepoFile("Controls/PdfViewport.RasterSheetReadySourceCache.cs");
        string renderCache = ReadRepoFile("Controls/PdfViewport.RenderCache.cs");
        string pageTabs = ReadRepoFile("MainWindow.PageTabs.cs");
        string pagesTree = ReadRepoFile("MainWindow.PagesTree.cs");
        string raster = ReadRepoFile("Models/RasterSheetCacheService.cs");
        string policy = ReadRepoFile("Models/ViewportRenderPolicy.cs");
        string xaml = ReadRepoFile("MainWindow.xaml");
        string workspaceManagers = ReadRepoFile("MainWindow.WorkspaceManagers.cs");
        string pdfImport = ReadRepoFile("MainWindow.PdfImport.cs");
        string pagesPdfMetadata = ReadRepoFile("MainWindow.PagesPdfMetadata.cs");
        string previewDialog = ReadRepoFile("Dialogs/PdfMetadataPreviewDialog.cs");
        string buildRasterMethod = SliceMethod(workspaceManagers, "private Task BuildSheetManagerRasterCacheAsync(");
        string buildRasterBackgroundMethod = SliceMethod(workspaceManagers, "private async Task BuildSheetManagerRasterCacheInBackgroundAsync(");
        string prepareRasterMethod = SliceMethod(workspaceManagers, "private async Task PrepareSheetManagerRasterCacheInBackgroundAsync(");
        string compactRasterMethod = SliceMethod(workspaceManagers, "private async Task CompactSheetManagerRasterCacheAsync(");
        string compactRasterBackgroundMethod = SliceMethod(workspaceManagers, "private async Task CompactSheetManagerRasterCacheInBackgroundAsync(");
        string rowRasterMethod = SliceMethod(workspaceManagers, "private async Task SetSheetManagerRasterRowEnabledAsync(");
        string rasterOnMethod = SliceMethod(workspaceManagers, "private async Task SetSheetManagerRasterEnabledAsync(");
        string rasterOnReadyMethod = SliceMethod(workspaceManagers, "private async Task<SheetManagerRasterReadyBatch> EnableSheetManagerReadyRasterPagesAsync(");
        string rasterOnBackgroundMethod = SliceMethod(workspaceManagers, "private async Task EnableMissingSheetManagerRasterOnInBackgroundAsync(");
        string rasterOffMethod = SliceMethod(workspaceManagers, "private async Task SetSheetManagerRasterOffFastAsync(");
        string rasterDpiUpgradeMethod = SliceMethod(rasterSheetDpiUpgrade, "private async Task BuildRasterSheetDpiUpgradeForCurrentPageAsync(");
        string responsiveDpiMethod = SliceMethod(rasterSheetDpiUpgrade, "private bool TryApplyResponsiveRasterSheetDpiForCurrentZoom()");
        string navigationFastPreviewMethod = SliceMethod(rasterSheetViewport, "private bool TrySwitchRasterSheetToFastPreviewForNavigation(");
        string lowZoomFastPreviewMethod = SliceMethod(rasterSheetViewport, "private bool TrySwitchRasterSheetToFastPreviewForLowZoom(");
        string currentZoomDpiQueueMethod = SliceMethod(rasterSheetDpiUpgrade, "private bool QueueRasterSheetDpiBuildForCurrentZoom(");
        string pageOpenReadyDpiMethod = SliceMethod(rasterSheetPageOpenDpi, "private bool TryApplyReadyResponsiveRasterSheetDpiForPageOpen(");
        string pageOpenDpiQueueMethod = SliceMethod(rasterSheetPageOpenDpi, "private bool QueueResponsiveRasterSheetDpiBuildForPageOpen(");
        string pageOpenBuildQueueMethod = SliceMethod(rasterSheetPageOpenDpi, "private bool QueueRasterSheetDpiBuildForPageOpen(");
        string readyDpiWarmApplyMethod = SliceMethod(rasterSheetDpiUpgrade, "private async Task ApplyReadyRasterSheetDpiAfterWarmupAsync(");
        string importedRasterMethod = SliceMethod(pdfImport, "private static RasterSheetBuildResult BuildAndWarmImportedRaster(");

        AssertTrue(
            viewport.Contains("private bool _usingRasterSheetRender;", StringComparison.Ordinal) &&
            viewport.Contains("_rasterSheetRebuildsInFlight", StringComparison.Ordinal) &&
            pageApi.Contains("_usingRasterSheetRender = false;", StringComparison.Ordinal) &&
            layers.Contains("_usingRasterSheetRender = true;", StringComparison.Ordinal),
            "viewport must track when the visible page bitmap is the raster working sheet");
        AssertTrue(
            viewTransform.Contains("if (_usingRasterSheetRender)", StringComparison.Ordinal) &&
            viewTransform.Contains("TrySwitchRasterSheetToFastPreviewForLowZoom()", StringComparison.Ordinal) &&
            viewTransform.Contains("TryApplyResponsiveRasterSheetDpiForCurrentZoom()", StringComparison.Ordinal) &&
            viewTransform.Contains("TryUpgradeRasterSheetToReadyDpiForCurrentZoom()", StringComparison.Ordinal) &&
            viewTransform.Contains("QueueDetailRenderIfNeeded(force)", StringComparison.Ordinal),
            "raster sheet mode should skip full PDF zoom refreshes, use responsive DPI for low work zoom, prefer ready higher-DPI rasters, and only then queue clipped detail renders");
        AssertTrue(
            detailRender.Contains("private void QueueDetailRenderIfNeeded(bool force, bool immediate = false)", StringComparison.Ordinal) &&
            !detailRender.Contains("if (_usingRasterSheetRender)\r\n            return;", StringComparison.Ordinal) &&
            !detailRender.Contains("if (_usingRasterSheetRender)\n            return;", StringComparison.Ordinal) &&
            !detailRender.Contains("_usingRasterSheetRender ||", StringComparison.Ordinal),
            "raster sheet mode must allow delayed clipped PDF detail renders");
        AssertTrue(
            rendering.Contains("FilterQuality = CurrentPageBitmapFilterQuality()", StringComparison.Ordinal) &&
            rendering.Contains("private SKFilterQuality CurrentPageBitmapFilterQuality()", StringComparison.Ordinal) &&
            rendering.Contains("ShouldUseSharperSourceImageRasterSampling()", StringComparison.Ordinal) &&
            rendering.Contains("ShouldUseFastFarZoomRasterSheetSampling()", StringComparison.Ordinal) &&
            rendering.Contains("RasterSheetFarZoomFastPaintMaxZoom", StringComparison.Ordinal) &&
            rendering.Contains("RasterSheetFarZoomFastPaintMaxScaleRatio", StringComparison.Ordinal) &&
            rendering.Contains("SKFilterQuality.Medium", StringComparison.Ordinal) &&
            rendering.Contains("SKFilterQuality.Low", StringComparison.Ordinal) &&
            rendering.Contains("if (_renderNavigationFastFrame)", StringComparison.Ordinal) &&
            rendering.Contains("return SKFilterQuality.None;", StringComparison.Ordinal) &&
            policy.Contains("RasterSheetFarZoomFastPaintMaxZoom = 0.30f", StringComparison.Ordinal) &&
            policy.Contains("RasterSheetFarZoomFastPaintMaxScaleRatio = 0.30f", StringComparison.Ordinal) &&
            !rendering.Contains("_zoom <= _bitmapScale * 1.05f", StringComparison.Ordinal),
            "raster sheet mode should use smoothed still-frame bitmap sampling while allowing cheaper sampling during navigation and heavily downsampled far-zoom paint");
        AssertTrue(
            renderCache.Contains("RasterSheetBitmapCache", StringComparison.Ordinal) &&
            renderCache.Contains("RasterSheetBitmapCache = new(maxEntries: 128", StringComparison.Ordinal) &&
            renderCache.Contains("ResolveRasterSheetBitmapCacheBudgetBytes", StringComparison.Ordinal) &&
            renderCache.Contains("PrefetchRasterSheetBitmap(PageInfo page)", StringComparison.Ordinal) &&
            renderCache.Contains("private static bool ShouldPrefetchRasterSheetBitmap(RasterSheetSource? source, bool preferOverview)", StringComparison.Ordinal) &&
            renderCache.Contains("if (!RasterSheetCacheService.IsSourceImageRaster(source))", StringComparison.Ordinal) &&
            renderCache.Contains("return RasterSheetCacheService.UseAsPageOpenRaster(source);", StringComparison.Ordinal) &&
            renderCache.Contains("RasterSheetCacheService.ShouldUseSourceImageRasterForFastOpen(source)", StringComparison.Ordinal) &&
            rasterSheetBitmapCache.Contains("public static bool WarmRasterSheetBitmapCache(PageInfo page, RasterSheetSource? rasterSheet = null)", StringComparison.Ordinal) &&
            rasterSheetBitmapCache.Contains("QueueRasterSheetBitmapApplyAfterWarmup", StringComparison.Ordinal) &&
            rasterSheetBitmapCache.Contains("WarmRasterSheetBitmapAndApplyAsync", StringComparison.Ordinal) &&
            rasterSheetBitmapCache.Contains("WarmRequestedRasterSheetBitmapCache", StringComparison.Ordinal) &&
            rasterSheetBitmapCache.Contains("TryPrepareRasterSheetBitmapForUiApply", StringComparison.Ordinal) &&
            rasterSheetBitmapCache.Contains("ApplyPreparedRasterSheetBitmap", StringComparison.Ordinal) &&
            rasterSheetBitmapCache.Contains("preparedBitmap.Bitmap.Dispose()", StringComparison.Ordinal) &&
            rasterSheetBitmapCache.Contains("ShouldApplyWarmedRasterSheetBitmap", StringComparison.Ordinal) &&
            rasterSheetBitmapCache.Contains("if (ShouldUseRasterSheetForCurrentZoom())", StringComparison.Ordinal) &&
            rasterSheetBitmapCache.Contains("return RasterSheetCacheService.ShouldUseSourceImageRasterForFastOpen(rasterSheet);", StringComparison.Ordinal) &&
            rasterSheetBitmapCache.Contains("return false;", StringComparison.Ordinal) &&
            !rasterSheetBitmapCache.Contains("requireCachedBitmap: true", StringComparison.Ordinal) &&
            layers.Contains("RasterSheetBitmapCacheWarmingReason", StringComparison.Ordinal) &&
            pageApi.Contains("QueueRasterSheetWorkZoomWarmupForPageOpen", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("ShouldWarmRasterSheetForWorkZoomOnPageOpen", StringComparison.Ordinal) &&
            rasterSheetBitmapCache.Contains("TryWarmRasterSheetBitmapCache", StringComparison.Ordinal) &&
            renderCache.Contains("RasterSheetBitmapPrefetchSemaphore", StringComparison.Ordinal) &&
            renderCache.Contains("TryGetRasterSheetBitmapCache", StringComparison.Ordinal) &&
            renderCache.Contains("TryPutRasterSheetBitmapCache", StringComparison.Ordinal) &&
            renderCache.Contains("TryBuildRasterSheetBitmapCacheKey", StringComparison.Ordinal) &&
            layers.Contains("TryGetRasterSheetBitmapCache", StringComparison.Ordinal) &&
            layers.Contains("TryPutRasterSheetBitmapCache", StringComparison.Ordinal),
            "raster sheet opens should apply decoded bitmaps from RAM, warm cold bitmaps off the UI thread, and prefetch nearby readable rasters as ready page bitmaps only when Raster First is enabled");
        AssertTrue(
            pageTabs.Contains("viewportPage.OverlayVisible && !string.IsNullOrWhiteSpace(viewportPage.OverlayPageFolder)", StringComparison.Ordinal) &&
            pageApi.Contains("bool hasSheetOverlayConfigured = false", StringComparison.Ordinal) &&
            pageApi.Contains("bool rasterFirstForOpen = RasterSheetCacheService.UseAsPageOpenRaster(rasterSheet)", StringComparison.Ordinal) &&
            pageApi.Contains("requireCachedBitmap: !rasterFirstForOpen", StringComparison.Ordinal) &&
            pageApi.Contains("allowLowZoomFullRasterApply: hasSheetOverlayConfigured", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("bool allowLowZoomFullRasterApply", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("ShouldWarmRasterSheetSourceBitmapForPageOpen(source, allowLowZoomFullRasterApply)", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("return allowLowZoomFullRasterApply &&", StringComparison.Ordinal) &&
            rasterSheetBitmapCache.Contains("bool allowLowZoomFullRaster = false", StringComparison.Ordinal) &&
            rasterSheetBitmapCache.Contains("RasterSheetPageOpenImmediateWarmMaxDpi", StringComparison.Ordinal),
            "readable raster sheets should open from the pre-rendered bitmap only when Raster First is enabled while legacy overlay warmup rules stay available for non-raster low-zoom pages");
        AssertTrue(
            policy.Contains("RasterSheetDisplayMinZoom = ZoomRefreshMinZoom", StringComparison.Ordinal) &&
            policy.Contains("RasterSheetDisplayExitZoom = 0.45f", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("ShouldUseRasterSheetForPageOpen", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("return rasterSheet?.Enabled == true &&", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("RasterSheetCacheService.UseAsPageOpenRaster(rasterSheet)", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("!IsLowZoomRasterSheetPageOpen(restoreView, fitAfter)", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("TryApplyReadyRasterSheetForCurrentZoom", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("TrySwitchRasterSheetToFastPreviewForNavigation", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("TrySwitchRasterSheetToFastPreviewForLowZoom", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("RasterSheetCacheService.IsSourceImageRaster(_rasterSheetSource)", StringComparison.Ordinal) &&
            !rasterSheetViewport.Contains("!RasterSheetCacheService.IsSourceImageRaster(_rasterSheetSource)", StringComparison.Ordinal) &&
            viewTransform.Contains("TrySwitchRasterSheetToFastPreviewForLowZoom()", StringComparison.Ordinal) &&
            viewTransform.Contains("TryApplyReadyRasterSheetForCurrentZoom()", StringComparison.Ordinal),
            "ordinary readable raster sheets should keep the preview-first open path unless Raster First is enabled, while later navigation can still swap to lower ready DPI tiers and source-image rasters keep their overview path");
        AssertTrue(
            xaml.Contains("SheetManagerRasterDpiBox", StringComparison.Ordinal) &&
            xaml.Contains("Content=\"Auto\" Tag=\"auto\"", StringComparison.Ordinal) &&
            xaml.Contains("Content=\"150 DPI\"", StringComparison.Ordinal) &&
            xaml.Contains("Content=\"200 DPI\"", StringComparison.Ordinal) &&
            xaml.Contains("Content=\"300 DPI\"", StringComparison.Ordinal) &&
            xaml.Contains("Content=\"400 DPI\"", StringComparison.Ordinal) &&
            xaml.Contains("SheetManagerRasterFormatBox", StringComparison.Ordinal) &&
            xaml.Contains("Content=\"PNG\" Tag=\"png\"", StringComparison.Ordinal) &&
            xaml.Contains("x:Name=\"SheetManagerBuildRasterButton\"", StringComparison.Ordinal) &&
            xaml.Contains("SheetManagerPrepareRasterButton", StringComparison.Ordinal) &&
            xaml.Contains("BtnSheetManagerPrepareRaster_Click", StringComparison.Ordinal) &&
            xaml.Contains("SheetManagerCancelRasterButton", StringComparison.Ordinal) &&
            xaml.Contains("BtnSheetManagerCancelRaster_Click", StringComparison.Ordinal) &&
            xaml.Contains("Content=\"Clean Raster\"", StringComparison.Ordinal) &&
            xaml.Contains("x:Name=\"SheetManagerRasterOnButton\"", StringComparison.Ordinal) &&
            xaml.Contains("x:Name=\"SheetManagerRasterOffButton\"", StringComparison.Ordinal) &&
            xaml.Contains("x:Name=\"SheetManagerRasterFirstOnButton\"", StringComparison.Ordinal) &&
            xaml.Contains("BtnSheetManagerRasterFirstOn_Click", StringComparison.Ordinal) &&
            xaml.Contains("x:Name=\"SheetManagerRasterFirstOffButton\"", StringComparison.Ordinal) &&
            xaml.Contains("BtnSheetManagerRasterFirstOff_Click", StringComparison.Ordinal) &&
            xaml.Contains("x:Name=\"SheetManagerCleanRasterButton\"", StringComparison.Ordinal) &&
            xaml.Contains("BtnSheetManagerCompactRaster_Click", StringComparison.Ordinal) &&
            xaml.Contains("Header=\"Raster Action\"", StringComparison.Ordinal) &&
            xaml.Contains("Header=\"Raster\" Binding=\"{Binding RasterStatus}\" Width=\"180\"", StringComparison.Ordinal) &&
            xaml.Contains("BtnSheetManagerRowRasterPdf_Click", StringComparison.Ordinal) &&
            xaml.Contains("BtnSheetManagerRowRasterAuto_Click", StringComparison.Ordinal) &&
            xaml.Contains("BtnSheetManagerRowRaster150_Click", StringComparison.Ordinal) &&
            xaml.Contains("BtnSheetManagerRowRaster200_Click", StringComparison.Ordinal) &&
            xaml.Contains("BtnSheetManagerRowRaster300_Click", StringComparison.Ordinal) &&
            xaml.Contains("BtnSheetManagerRowRaster400_Click", StringComparison.Ordinal) &&
            xaml.Contains("Content=\"Auto\" MinWidth=\"40\"", StringComparison.Ordinal) &&
            workspaceManagers.Contains("private CancellationTokenSource? _sheetManagerRasterPrepareCts;", StringComparison.Ordinal) &&
            workspaceManagers.Contains("private const int SheetManagerAutoRasterDpi = 0;", StringComparison.Ordinal) &&
            workspaceManagers.Contains("private int SelectedSheetManagerRasterDpi()", StringComparison.Ordinal) &&
            workspaceManagers.Contains("private string SelectedSheetManagerRasterFormat()", StringComparison.Ordinal) &&
            workspaceManagers.Contains("string.Equals(raw.Trim(), \"auto\"", StringComparison.Ordinal) &&
            workspaceManagers.Contains("SheetManagerRasterDpiBox?.SelectedItem is ComboBoxItem", StringComparison.Ordinal) &&
            workspaceManagers.Contains("SheetManagerRasterFormatBox?.SelectedItem is ComboBoxItem", StringComparison.Ordinal) &&
            workspaceManagers.Contains("NormalizeReadableRasterFormat", StringComparison.Ordinal) &&
            workspaceManagers.Contains("EffectiveSheetManagerRasterDpi", StringComparison.Ordinal) &&
            workspaceManagers.Contains("BestReadyReadableRasterDpi(page)", StringComparison.Ordinal) &&
            workspaceManagers.Contains("ReadyReadableRasterDpisByPageFolder(pages)", StringComparison.Ordinal) &&
            workspaceManagers.Contains("readyRasterDpisByPageFolder", StringComparison.Ordinal) &&
            workspaceManagers.Contains("DisplayStatus(page, readyRasterDpisByPageFolder)", StringComparison.Ordinal) &&
            pagesPdfMetadata.Contains("readyRasterDpisByPageFolder = null", StringComparison.Ordinal) &&
            pagesPdfMetadata.Contains("DisplayStatus(result.Page, readyRasterDpisByPageFolder)", StringComparison.Ordinal) &&
            workspaceManagers.Contains("SheetManagerRasterDpiProgressLabel", StringComparison.Ordinal) &&
            workspaceManagers.Contains("ApplySheetManagerRowRasterDpiAsync", StringComparison.Ordinal) &&
            workspaceManagers.Contains("SheetManagerRowFromButton", StringComparison.Ordinal) &&
            workspaceManagers.Contains("SheetManagerPageFromRow", StringComparison.Ordinal) &&
            pagesTree.Contains("private void RefreshPageTreePageSnapshots(IReadOnlyList<PageInfo> pages)", StringComparison.Ordinal) &&
            pagesTree.Contains("private bool TryRefreshPageTreeItemFromStore(TreeViewItem item, string pageFolder", StringComparison.Ordinal) &&
            pagesTree.Contains("OurPlaneCoreJobStore.TryReadPage(pageFolder) is not { } refreshedPage", StringComparison.Ordinal) &&
            pagesTree.Contains("item.Tag = refreshedPage", StringComparison.Ordinal) &&
            pagesTree.Contains("RebuildPageTakeoffNodes(item, refreshedPage)", StringComparison.Ordinal) &&
            pagesTree.Contains("RebuildPageTreeItemIndex()", StringComparison.Ordinal) &&
            workspaceManagers.Contains("SheetManagerBuildRasterButton.IsEnabled = !running", StringComparison.Ordinal) &&
            workspaceManagers.Contains("SheetManagerRasterOnButton.IsEnabled = !running", StringComparison.Ordinal) &&
            workspaceManagers.Contains("SheetManagerRasterOffButton.IsEnabled = !running", StringComparison.Ordinal) &&
            workspaceManagers.Contains("SheetManagerRasterFirstOnButton.IsEnabled = !running", StringComparison.Ordinal) &&
            workspaceManagers.Contains("SheetManagerRasterFirstOffButton.IsEnabled = !running", StringComparison.Ordinal) &&
            workspaceManagers.Contains("SheetManagerCleanRasterButton.IsEnabled = !running", StringComparison.Ordinal) &&
            workspaceManagers.Contains("SheetManagerRasterFormatBox.IsEnabled = !running", StringComparison.Ordinal) &&
            workspaceManagers.Contains("RefreshPageTreePageSnapshots([page])", StringComparison.Ordinal) &&
            workspaceManagers.Contains("RefreshPageTreePageSnapshots(readyBatch.FastPages)", StringComparison.Ordinal) &&
            workspaceManagers.Contains("RefreshPageTreePageSnapshots(pages)", StringComparison.Ordinal) &&
            workspaceManagers.Contains("private void RefreshSheetManagerRasterBackgroundPage(PageInfo page, bool refreshSheetManager, bool reloadCurrentPage)", StringComparison.Ordinal) &&
            workspaceManagers.Contains("RefreshSheetManagerRasterBackgroundPage(page, refreshSheetManager: true, reloadCurrentPage: false)", StringComparison.Ordinal) &&
            workspaceManagers.Contains("RefreshSheetManagerRasterBackgroundPage(page, refreshSheetManager, reloadCurrentPage: true)", StringComparison.Ordinal) &&
            workspaceManagers.Contains("RefreshSheetManagerRasterRow", StringComparison.Ordinal) &&
            workspaceManagers.Contains("private bool RefreshSheetManagerRasterRows(IReadOnlyList<PageInfo> pages)", StringComparison.Ordinal) &&
            workspaceManagers.Contains("if (!RefreshSheetManagerRasterRows(pages))", StringComparison.Ordinal) &&
            workspaceManagers.Contains("RasterSheetCacheService.DisplayStatus(refreshedPage, readyRasterDpisByPageFolder)", StringComparison.Ordinal) &&
            buildRasterMethod.Contains("BuildSheetManagerRasterCacheInBackgroundAsync", StringComparison.Ordinal) &&
            buildRasterMethod.Contains("_sheetManagerRasterBackgroundLabel = \"Build\"", StringComparison.Ordinal) &&
            buildRasterMethod.Contains("SetSheetManagerRasterPrepareRunning(true)", StringComparison.Ordinal) &&
            !buildRasterMethod.Contains("ShowBusyOverlay", StringComparison.Ordinal) &&
            buildRasterBackgroundMethod.Contains("cts.Token.ThrowIfCancellationRequested()", StringComparison.Ordinal) &&
            buildRasterBackgroundMethod.Contains("BuildAndWarmSheetManagerRaster(page, renderScale, rasterFormat)", StringComparison.Ordinal) &&
            buildRasterBackgroundMethod.Contains("RefreshSheetManagerRasterBackgroundPage(page, refreshSheetManager: true, reloadCurrentPage: true)", StringComparison.Ordinal) &&
            buildRasterBackgroundMethod.Contains("SetSheetManagerRasterPrepareRunning(false)", StringComparison.Ordinal) &&
            buildRasterBackgroundMethod.Contains("Sheet Manager Raster Build {rasterDpiLabel} done", StringComparison.Ordinal) &&
            !buildRasterBackgroundMethod.Contains("ShowBusyOverlay", StringComparison.Ordinal) &&
            workspaceManagers.Contains("SheetManagerRasterReadyBatch readyBatch = await EnableSheetManagerReadyRasterPagesAsync(pages, rasterDpi, rasterFormat)", StringComparison.Ordinal) &&
            workspaceManagers.Contains("bool fastRowsRefreshed = true", StringComparison.Ordinal) &&
            workspaceManagers.Contains("readyBatch.MissingPages.Count == 0", StringComparison.Ordinal) &&
            workspaceManagers.Contains("!fastRowsRefreshed", StringComparison.Ordinal) &&
            rasterOnMethod.Contains("QueueSheetManagerRasterOnMissingInBackground(", StringComparison.Ordinal) &&
            !rasterOnMethod.Contains("ShowBusyOverlay", StringComparison.Ordinal) &&
            workspaceManagers.Contains("private void QueueSheetManagerRasterOnMissingInBackground(", StringComparison.Ordinal) &&
            workspaceManagers.Contains("_sheetManagerRasterBackgroundLabel = \"On\"", StringComparison.Ordinal) &&
            workspaceManagers.Contains("EnableMissingSheetManagerRasterOnInBackgroundAsync", StringComparison.Ordinal) &&
            workspaceManagers.Contains("ready {readyBatch.Ready}, queued {readyBatch.MissingPages.Count} missing sheet(s)", StringComparison.Ordinal) &&
            rasterOnReadyMethod.Contains("return await Task.Run(() =>", StringComparison.Ordinal) &&
            rasterOnReadyMethod.Contains("var missingPages = new List<PageInfo>();", StringComparison.Ordinal) &&
            rasterOnReadyMethod.Contains("HasReadyReadableRaster(page, renderScale, rasterFormat)", StringComparison.Ordinal) &&
            rasterOnReadyMethod.Contains("missingPages.Add(page)", StringComparison.Ordinal) &&
            rasterOnReadyMethod.Contains("fastPages.Add(plan.Page)", StringComparison.Ordinal) &&
            rasterOnReadyMethod.Contains("new SheetManagerRasterReadyBatch", StringComparison.Ordinal) &&
            rasterOnReadyMethod.Contains("TryEnableReadyReadableRaster", StringComparison.Ordinal) &&
            !rasterOnReadyMethod.Contains("ShowBusyOverlay", StringComparison.Ordinal) &&
            rasterOnBackgroundMethod.Contains("cts.Token.ThrowIfCancellationRequested()", StringComparison.Ordinal) &&
            rasterOnBackgroundMethod.Contains("BuildAndWarmSheetManagerRaster(page, renderScale, rasterFormat)", StringComparison.Ordinal) &&
            rasterOnBackgroundMethod.Contains("SetSheetManagerRasterPrepareRunning(false)", StringComparison.Ordinal) &&
            rasterOnBackgroundMethod.Contains("ReloadCurrentPageIfRasterChanged(pages)", StringComparison.Ordinal) &&
            rasterOnBackgroundMethod.Contains("Sheet Manager Raster On {rasterDpiLabel} done", StringComparison.Ordinal) &&
            !rasterOnBackgroundMethod.Contains("ShowBusyOverlay", StringComparison.Ordinal) &&
            workspaceManagers.Contains("SetSheetManagerRasterOffFastAsync(pages, refreshSheetManager)", StringComparison.Ordinal) &&
            rasterOffMethod.Contains("Task.Run(", StringComparison.Ordinal) &&
            rasterOffMethod.Contains("TrySetEnabled(page, enabled: false", StringComparison.Ordinal) &&
            rasterOffMethod.Contains("RefreshSheetManagerRasterRows(pages)", StringComparison.Ordinal) &&
            !rasterOffMethod.Contains("ShowBusyOverlay", StringComparison.Ordinal) &&
            workspaceManagers.Contains("private async Task SetSheetManagerRasterFirstAsync(IReadOnlyList<PageInfo> pages, bool enabled)", StringComparison.Ordinal) &&
            workspaceManagers.Contains("TrySetUseAsPageOpenRaster(page, enabled", StringComparison.Ordinal) &&
            workspaceManagers.Contains("Sheet Manager Raster First {mode}: changed {changed}, already {already}, failed {failed}.", StringComparison.Ordinal) &&
            workspaceManagers.Contains("SetSheetManagerRasterRowEnabledAsync", StringComparison.Ordinal) &&
            workspaceManagers.Contains("BtnSheetManagerRowRasterAuto_Click", StringComparison.Ordinal) &&
            workspaceManagers.Contains("Sheet Manager Raster Row", StringComparison.Ordinal) &&
            rowRasterMethod.Contains("BuildAndWarmSheetManagerRaster(page, renderScale, rasterFormat)", StringComparison.Ordinal) &&
            workspaceManagers.Contains("private static RasterSheetBuildResult BuildAndWarmSheetManagerRaster(PageInfo page, float renderScale, string rasterFormat = \"\")", StringComparison.Ordinal) &&
            workspaceManagers.Contains("private static RasterSheetBuildResult PrepareAndWarmSheetManagerRaster(PageInfo page, float renderScale, string rasterFormat = \"\")", StringComparison.Ordinal) &&
            workspaceManagers.Contains("private static void WarmSheetManagerRasterBitmap(PageInfo page, RasterSheetBuildResult result)", StringComparison.Ordinal) &&
            workspaceManagers.Contains("PdfViewport.WarmRasterSheetBitmapCache(page, result.Source)", StringComparison.Ordinal) &&
            pdfImport.Contains("BuildAndWarmImportedRaster(page)", StringComparison.Ordinal) &&
            pdfImport.Contains("private const int ImportedPdfRasterDpi = 150;", StringComparison.Ordinal) &&
            importedRasterMethod.Contains("RasterSheetCacheService.RasterDpiToRenderScale(ImportedPdfRasterDpi)", StringComparison.Ordinal) &&
            importedRasterMethod.Contains("RasterSheetCacheService.BuildAndEnable(page, renderScale)", StringComparison.Ordinal) &&
            importedRasterMethod.Contains("RasterSheetCacheService.TrySetUseAsPageOpenRaster(refreshed, true", StringComparison.Ordinal) &&
            importedRasterMethod.Contains("PdfViewport.WarmRasterSheetBitmapCache(refreshed, warmSource)", StringComparison.Ordinal) &&
            rowRasterMethod.Contains("RefreshSheetManagerRasterRow(row)", StringComparison.Ordinal) &&
            !rowRasterMethod.Contains("ShowBusyOverlay", StringComparison.Ordinal) &&
            workspaceManagers.Contains("if (refreshSheetManager)", StringComparison.Ordinal) &&
            workspaceManagers.Contains("already {already}", StringComparison.Ordinal) &&
            previewDialog.Contains("private string _rasterStatus = \"\";", StringComparison.Ordinal) &&
            previewDialog.Contains("set => SetField(ref _rasterStatus, value ?? \"\");", StringComparison.Ordinal) &&
            workspaceManagers.Contains("TryBlockSheetManagerRasterCommandDuringPrepare", StringComparison.Ordinal) &&
            workspaceManagers.Contains("SetSheetManagerRasterPrepareRunning", StringComparison.Ordinal) &&
            workspaceManagers.Contains("CompactSheetManagerRasterCacheAsync(SelectedSheetManagerPagesForRaster())", StringComparison.Ordinal) &&
            compactRasterMethod.Contains("CompactSheetManagerRasterCacheInBackgroundAsync", StringComparison.Ordinal) &&
            compactRasterMethod.Contains("_sheetManagerRasterBackgroundLabel = \"Cleanup\"", StringComparison.Ordinal) &&
            compactRasterMethod.Contains("SetSheetManagerRasterPrepareRunning(true)", StringComparison.Ordinal) &&
            !compactRasterMethod.Contains("ShowBusyOverlay", StringComparison.Ordinal) &&
            compactRasterBackgroundMethod.Contains("cts.Token.ThrowIfCancellationRequested()", StringComparison.Ordinal) &&
            compactRasterBackgroundMethod.Contains("RasterSheetCacheService.CompactCache(page)", StringComparison.Ordinal) &&
            compactRasterBackgroundMethod.Contains("RefreshSheetManagerRasterBackgroundPage(page, refreshSheetManager: true, reloadCurrentPage: false)", StringComparison.Ordinal) &&
            compactRasterBackgroundMethod.Contains("SetSheetManagerRasterPrepareRunning(false)", StringComparison.Ordinal) &&
            compactRasterBackgroundMethod.Contains("Sheet Manager Raster Cleanup done", StringComparison.Ordinal) &&
            !compactRasterBackgroundMethod.Contains("ShowBusyOverlay", StringComparison.Ordinal) &&
            workspaceManagers.Contains("RasterSheetCacheService.CompactCache(page)", StringComparison.Ordinal) &&
            workspaceManagers.Contains("FormatRasterCacheBytes(deletedBytes)", StringComparison.Ordinal) &&
            workspaceManagers.Contains("PrepareSheetManagerRasterCacheInBackgroundAsync", StringComparison.Ordinal) &&
            workspaceManagers.Contains("Task.Run(", StringComparison.Ordinal) &&
            workspaceManagers.Contains("cts.Token.ThrowIfCancellationRequested()", StringComparison.Ordinal) &&
            workspaceManagers.Contains("string rasterDpiLabel = SheetManagerRasterSelectionLabel(rasterDpi, rasterFormat);", StringComparison.Ordinal) &&
            workspaceManagers.Contains("RasterSheetCacheService.RasterDpiToRenderScale(effectiveDpi)", StringComparison.Ordinal) &&
            workspaceManagers.Contains("PrepareAndWarmSheetManagerRaster(page, renderScale, rasterFormat)", StringComparison.Ordinal) &&
            !prepareRasterMethod.Contains("ReloadCurrentPageIfRasterChanged", StringComparison.Ordinal) &&
            workspaceManagers.Contains("Sheet Manager Raster On {rasterDpiLabel}:", StringComparison.Ordinal) &&
            workspaceManagers.Contains("reused {reused}", StringComparison.Ordinal) &&
            raster.Contains("public const int DefaultRasterDpi = 200;", StringComparison.Ordinal) &&
            raster.Contains("public const int MaxRasterDpi = 400;", StringComparison.Ordinal) &&
            raster.Contains("public static RasterSheetBuildResult BuildCachePreservingEnabled", StringComparison.Ordinal) &&
            raster.Contains("public static bool HasReadyReadableRaster(", StringComparison.Ordinal) &&
            raster.Contains("public static bool TryEnableReadyReadableRaster(", StringComparison.Ordinal) &&
            raster.Contains("public static string NormalizeReadableRasterFormat", StringComparison.Ordinal) &&
            raster.Contains("PngRasterFormat = \"png\"", StringComparison.Ordinal) &&
            raster.Contains("public sealed record RasterSheetCacheCompactResult", StringComparison.Ordinal) &&
            raster.Contains("public static RasterSheetCacheCompactResult CompactCache(PageInfo page)", StringComparison.Ordinal) &&
            raster.Contains("TryCompactActiveRasterImage(page, source", StringComparison.Ordinal) &&
            raster.Contains("IsLegacyPngRasterImage", StringComparison.Ordinal) &&
            raster.Contains("CompactRasterImageNameForLegacyPng", StringComparison.Ordinal) &&
            raster.Contains("OurPlaneCoreJobStore.SavePageRasterSheet(page.FolderPath, compacted)", StringComparison.Ordinal) &&
            raster.Contains("AddReferencedCachePath(page.FolderPath, source.Image", StringComparison.Ordinal) &&
            raster.Contains("IsCompactableCacheFile", StringComparison.Ordinal) &&
            raster.Contains("TryFindReusableReadableRaster(page, scale", StringComparison.Ordinal) &&
            raster.Contains("public static float RasterDpiToRenderScale(int dpi)", StringComparison.Ordinal) &&
            raster.Contains("public static int RenderScaleToDpi(double renderScale)", StringComparison.Ordinal) &&
            raster.Contains("public static string WorkingImageNameForRenderScale", StringComparison.Ordinal) &&
            raster.Contains("Directory.EnumerateFiles(rasterDir, \"working-*dpi.*\")", StringComparison.Ordinal) &&
            raster.Contains("SKWebpEncoderCompression.Lossless", StringComparison.Ordinal) &&
            raster.Contains("CompactWorkingImageName = \"working.webp\"", StringComparison.Ordinal) &&
            raster.Contains("WebpRasterFormat = \"webp\"", StringComparison.Ordinal) &&
            raster.Contains("TryParseWorkingImageDpi", StringComparison.Ordinal) &&
            raster.Contains("TryBuildReusableReadableVariant", StringComparison.Ordinal) &&
            raster.Contains("WorkingImageCandidatesForRenderScale", StringComparison.Ordinal) &&
            raster.Contains("CachedReadableDpiSummary", StringComparison.Ordinal) &&
            raster.Contains("public static int BestReadyReadableRasterDpi(PageInfo page)", StringComparison.Ordinal) &&
            raster.Contains("public static IReadOnlyDictionary<string, IReadOnlyList<int>> ReadyReadableRasterDpisByPageFolder", StringComparison.Ordinal) &&
            raster.Contains("readyDpisByPageFolder.TryGetValue", StringComparison.Ordinal) &&
            raster.Contains("private static IReadOnlyList<int> ReadyReadableRasterDpisFromDisk", StringComparison.Ordinal) &&
            raster.Contains("AppendCachedDpiSummary", StringComparison.Ordinal) &&
            raster.Contains("| ready", StringComparison.Ordinal) &&
            raster.Contains("out bool changed", StringComparison.Ordinal) &&
            raster.Contains("if (!enabled)", StringComparison.Ordinal) &&
            raster.Contains("public static bool UseAsPageOpenRaster(RasterSheetSource? source)", StringComparison.Ordinal) &&
            raster.Contains("public static bool TrySetUseAsPageOpenRaster(", StringComparison.Ordinal) &&
            raster.Contains("source.UseAsPageOpenRaster = useAsPageOpenRaster", StringComparison.Ordinal) &&
            raster.Contains("string first = source.UseAsPageOpenRaster ? \"+first\" : \"\"", StringComparison.Ordinal) &&
            raster.Contains("Reused: true", StringComparison.Ordinal) &&
            !prepareRasterMethod.Contains("ShowBusyOverlay", StringComparison.Ordinal),
            "Sheet Manager raster builds should keep 200 DPI as the default, write compact lossless WebP raster images by default, allow selected sheets to rebuild at 150/200/300/400 DPI as PNG when requested, reuse ready per-DPI variants, and prepare caches in the background without a modal busy overlay");
        AssertTrue(
            raster.Contains("SourceImageRasterProfile = \"source-image-v1\"", StringComparison.Ordinal) &&
            raster.Contains("SourceImageOverviewMaxPixels = 8_000_000", StringComparison.Ordinal) &&
            raster.Contains("SourceImageFastOpenMaxPixels", StringComparison.Ordinal) &&
            raster.Contains("ShouldUseSourceImageRasterForFastOpen", StringComparison.Ordinal) &&
            raster.Contains("estimatedPixels <= SourceImageFastOpenMaxPixels", StringComparison.Ordinal) &&
            raster.Contains("NeedsSourceImageOverview", StringComparison.Ordinal) &&
            pageApi.Contains("ShouldUseRasterSheetForPageOpen(rasterSheet, restoreView", StringComparison.Ordinal) &&
            pageApi.Contains("QueueRasterSheetSelfHealIfNeeded(", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("ShouldUseRasterSheetForPageOpen(RasterSheetSource? rasterSheet", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("ShouldUseRasterSheetOverviewForPageOpen", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("IsLowZoomRasterSheetPageOpen", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("ShouldUseSourceImageRasterForFastOpen(rasterSheet)", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("!RasterSheetCacheService.ShouldUseSourceImageRasterForFastOpen(rasterSheet)", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("ShouldKeepRasterSheetAtLowZoom", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("HasSourceImageOverview(_rasterSheetSource)", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("preferOverview: true", StringComparison.Ordinal) &&
            viewTransform.Contains("_usingRasterSheetOverviewRender", StringComparison.Ordinal) &&
            layers.Contains("raster-sheet-overview", StringComparison.Ordinal) &&
            raster.Contains("OverviewImageName = \"overview.png\"", StringComparison.Ordinal) &&
            raster.Contains("TryReadOverviewReady", StringComparison.Ordinal) &&
            raster.Contains("BuildOverviewForExistingSourceImageRaster", StringComparison.Ordinal) &&
            raster.Contains("CreateSourceImageOverviewBitmap", StringComparison.Ordinal) &&
            raster.Contains("ShouldUpgradeSourceImageOverviewQuality", StringComparison.Ordinal) &&
            raster.Contains("TargetSourceImageOverviewRenderScale", StringComparison.Ordinal) &&
            raster.Contains("source image overview below current quality", StringComparison.Ordinal) &&
            raster.Contains("SKColorFilter.CreateHighContrast", StringComparison.Ordinal) &&
            raster.Contains("PageImageBitmapDecoder.Decode(sourceImagePath)", StringComparison.Ordinal) &&
            raster.Contains("ShouldBuildSourceImageOverview", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("ShouldBuildSourceImageOverview", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("mode='{(overviewOnly ? \"overview\" : \"full\")}'", StringComparison.Ordinal) &&
            rendering.Contains("RasterSheetCacheService.ShouldUseSourceImageRasterForFastOpen(_rasterSheetSource)", StringComparison.Ordinal) &&
            rendering.Contains("!_renderNavigationFastFrame", StringComparison.Ordinal),
            "image-backed PlanSwift PNG/TIF raster sheets should use full source pixels when they are fast enough, reserve overview rasters for oversized low-zoom opens, background-upgrade old caches, switch to full source pixels on zoom, and get sharper still-frame sampling");
        AssertTrue(
            rasterSheetReadySourceCache.Contains("RasterSheetReadySourceCache", StringComparison.Ordinal) &&
            rasterSheetReadySourceCache.Contains("RememberReadyRasterSheetSource", StringComparison.Ordinal) &&
            rasterSheetReadySourceCache.Contains("TryGetRememberedReadyRasterSheetSource", StringComparison.Ordinal) &&
            rasterSheetReadySourceCache.Contains("RasterSheetCacheService.RenderScaleToDpi(source.RenderScale) != targetDpi", StringComparison.Ordinal) &&
            rasterSheetReadySourceCache.Contains("PdfLastWriteUtcTicks", StringComparison.Ordinal) &&
            layers.Contains("RememberReadyRasterSheetSource(pageFolder, pdfPath, pdfIndex, rasterSheet)", StringComparison.Ordinal) &&
            renderCache.Contains("RememberReadyRasterSheetSource(currentPage, dpi, source)", StringComparison.Ordinal),
            "ready raster source discovery should be remembered in RAM so pan/zoom can choose prepared DPI tiers without scanning raster folders on the UI thread");
        AssertTrue(
            rasterSheetDpiUpgrade.Contains("private bool TryUpgradeRasterSheetToReadyDpiForCurrentZoom()", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("private bool TryApplyResponsiveRasterSheetDpiForCurrentZoom()", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("DesiredRasterSheetDpiForCurrentZoom(currentDpi)", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("QueueRasterSheetDpiBuildForCurrentZoom(page, currentDpi, targetDpi)", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("BuildRasterSheetDpiUpgradeForCurrentPageAsync", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("TryEnableReadyReadableRaster(", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("BuildAndEnable(buildPage, targetScale)", StringComparison.Ordinal) &&
            rasterSheetPreparedApply.Contains("TryPrepareRasterSheetBitmapForUiApply", StringComparison.Ordinal) &&
            rasterSheetPreparedApply.Contains("ApplyPreparedRasterSheetDpiUpgradeResult", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("RasterSheetDpiUpgradeKey", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("Dispatcher.InvokeAsync", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("if (_isFastNavigating)", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("TrySwitchRasterSheetToFastPreviewForNavigation()", StringComparison.Ordinal) &&
            policy.Contains("RasterSheetDisplayDpiSteps = [72, 100, 144, 200]", StringComparison.Ordinal) &&
            policy.Contains("SelectRasterSheetDisplayDpi", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("TargetRasterSheetDpiForCurrentZoom()", StringComparison.Ordinal) &&
            rasterSheetPageOpenDpi.Contains("TargetRasterSheetDpiForPageOpen", StringComparison.Ordinal) &&
            rasterSheetPageOpenDpi.Contains("PageOpenZoomForRasterSheet", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("ViewportRenderPolicy.SelectRasterSheetDisplayDpi(zoom)", StringComparison.Ordinal) &&
            policy.Contains("ShouldPreferLowerRasterSheetDpi", StringComparison.Ordinal) &&
            rasterSheetPageOpenDpi.Contains("ShouldUseResponsiveRasterSheetDpiForPageOpen", StringComparison.Ordinal) &&
            rasterSheetPageOpenDpi.Contains("RasterSheetCacheService.UseAsPageOpenRaster(rasterSheet)", StringComparison.Ordinal) &&
            rasterSheetPageOpenDpi.Contains("ShouldSkipOversizedRasterSheetForPageOpen", StringComparison.Ordinal) &&
            rasterSheetPageOpenDpi.Contains("ViewportRenderPolicy.ShouldPreferLowerRasterSheetDpi(currentDpi, targetDpi)", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("targetDpi != TargetRasterSheetDpiForCurrentZoom()", StringComparison.Ordinal) &&
            pageApi.Contains("responsiveRasterDpiForOpen", StringComparison.Ordinal) &&
            pageApi.Contains("skipOversizedRasterSheetForOpen", StringComparison.Ordinal) &&
            pageApi.Contains("responsiveRasterDpiWorkQueuedForOpen", StringComparison.Ordinal) &&
            pageApi.Contains("rasterWorkZoomWarmupQueuedForOpen", StringComparison.Ordinal) &&
            pageApi.Contains("responsiveRasterDpiWorkQueuedForOpen = TryApplyReadyResponsiveRasterSheetDpiForPageOpen", StringComparison.Ordinal) &&
            pageApi.Contains("if (!responsiveRasterDpiWorkQueuedForOpen)", StringComparison.Ordinal) &&
            pageApi.Contains("!responsiveRasterDpiWorkQueuedForOpen &&", StringComparison.Ordinal) &&
            pageApi.Contains("!rasterWorkZoomWarmupQueuedForOpen", StringComparison.Ordinal) &&
            pageApi.Contains("!skipOversizedRasterSheetForOpen", StringComparison.Ordinal) &&
            !pageApi.Contains("shouldUseRasterSheetForOpen &&\r\n            restoreView.HasValue", StringComparison.Ordinal) &&
            !pageApi.Contains("shouldUseRasterSheetForOpen &&\n            restoreView.HasValue", StringComparison.Ordinal) &&
            pageApi.Contains("TryApplyReadyResponsiveRasterSheetDpiForPageOpen", StringComparison.Ordinal) &&
            pageApi.Contains("QueueResponsiveRasterSheetDpiBuildForPageOpen", StringComparison.Ordinal) &&
            !pageApi.Contains("!responsiveRasterDpiForOpen", StringComparison.Ordinal) &&
            pageApi.Contains("queueSharpBaseAfterPreview: !rasterBitmapWarmupQueuedForOpen &&", StringComparison.Ordinal) &&
            !pageApi.Contains("else if (responsiveRasterDpiWorkQueuedForOpen)", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("ShouldUseResponsiveRasterSheetDpiForCurrentZoom(RasterSheetSource? rasterSheet)", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("TryPrepareRasterSheetBitmapForImmediateRepaint()", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("TryApplyReadyRasterSheetDpiFromMemory", StringComparison.Ordinal) &&
            pageOpenReadyDpiMethod.Contains("SelectReadyRasterSheetDpiForPageOpen(page, targetDpi, currentDpi)", StringComparison.Ordinal) &&
            pageOpenReadyDpiMethod.Contains("QueueReadyRasterSheetDpiApplyAfterWarmupForPageOpen", StringComparison.Ordinal) &&
            !pageOpenReadyDpiMethod.Contains("TryApplyReadyRasterSheetDpiFromMemory(page, readyDpi, restoreView, fitAfter)", StringComparison.Ordinal) &&
            !pageOpenReadyDpiMethod.Contains("TryApplyReadyRasterSheetDpi(page, targetDpi", StringComparison.Ordinal) &&
            pageOpenDpiQueueMethod.Contains("QueueReadyRasterSheetDpiApplyAfterWarmupForPageOpen", StringComparison.Ordinal) &&
            pageOpenDpiQueueMethod.Contains("QueueRasterSheetDpiBuildForPageOpen", StringComparison.Ordinal) &&
            rasterSheetPageOpenDpi.Contains("TryGetRememberedReadyRasterSheetSource(page, targetDpi, out _)", StringComparison.Ordinal) &&
            rasterSheetPageOpenDpi.Contains("targetDpi > ViewportRenderPolicy.RasterSheetPageOpenImmediateWarmMaxDpi", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("ready-warmed-page-open", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("built-page-open", StringComparison.Ordinal) &&
            rasterSheetPageOpenDpi.Contains("TryGetPageOpenRasterSheetDpiApplyView", StringComparison.Ordinal) &&
            rasterSheetPageOpenDpi.Contains("_zoom >= ViewportRenderPolicy.RasterSheetDisplayMinZoom", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("QueueReadyRasterSheetDpiApplyAfterWarmup", StringComparison.Ordinal) &&
            readyDpiWarmApplyMethod.Contains("Task.Run", StringComparison.Ordinal) &&
            readyDpiWarmApplyMethod.Contains("TryEnableReadyReadableRaster", StringComparison.Ordinal) &&
            readyDpiWarmApplyMethod.Contains("TryPrepareRasterSheetBitmapForUiApply", StringComparison.Ordinal) &&
            readyDpiWarmApplyMethod.Contains("ApplyPreparedRasterSheetDpiUpgradeResult", StringComparison.Ordinal) &&
            readyDpiWarmApplyMethod.Contains("!preparedBitmapApplied", StringComparison.Ordinal) &&
            !readyDpiWarmApplyMethod.Contains("requireCachedBitmap: true", StringComparison.Ordinal) &&
            readyDpiWarmApplyMethod.Contains("targetDpi != TargetRasterSheetDpiForCurrentZoom()", StringComparison.Ordinal) &&
            rasterSheetPreparedApply.Contains("QueuePreparedReadyRasterSheetDpiApplyFromMemory", StringComparison.Ordinal) &&
            rasterSheetPreparedApply.Contains("ApplyPreparedReadyRasterSheetDpiFromMemoryAsync", StringComparison.Ordinal) &&
            rasterSheetPreparedApply.Contains("TryGetRememberedReadyRasterSheetSource(page, targetDpi", StringComparison.Ordinal) &&
            rasterSheetPreparedApply.Contains("PrepareReadyRasterSheetDpiForUiApply", StringComparison.Ordinal) &&
            rasterSheetPreparedApply.Contains("PrepareReadyRasterSheetDpiForUiApply(queuedPage, readySource, targetDpi)", StringComparison.Ordinal) &&
            !rasterSheetPreparedApply.Contains("RasterSheetCacheService.TryGetReadyReadableRasterSource(page, targetScale", StringComparison.Ordinal) &&
            !rasterSheetPreparedApply.Contains("RasterSheetCacheService.ReadyReadableRasterDpis(page)", StringComparison.Ordinal) &&
            !currentZoomDpiQueueMethod.Contains("RasterSheetCacheService.HasReadyReadableRaster", StringComparison.Ordinal) &&
            !currentZoomDpiQueueMethod.Contains("Directory.Exists(page.FolderPath)", StringComparison.Ordinal) &&
            !currentZoomDpiQueueMethod.Contains("File.Exists(page.PdfPath)", StringComparison.Ordinal) &&
            !pageOpenReadyDpiMethod.Contains("RasterSheetCacheService.ReadyReadableRasterDpis", StringComparison.Ordinal) &&
            !pageOpenBuildQueueMethod.Contains("RasterSheetCacheService.HasReadyReadableRaster", StringComparison.Ordinal) &&
            !pageOpenBuildQueueMethod.Contains("Directory.Exists(page.FolderPath)", StringComparison.Ordinal) &&
            !pageOpenBuildQueueMethod.Contains("File.Exists(page.PdfPath)", StringComparison.Ordinal) &&
            rasterSheetPreparedApply.Contains("ShouldApplyPreparedReadyRasterSheetDpi", StringComparison.Ordinal) &&
            rasterSheetPreparedApply.Contains("preparedBitmap.Bitmap.Dispose()", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("TryApplyReadyRasterSheetDpiFromMemory(", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("QueuePreparedReadyRasterSheetDpiApplyFromMemory(", StringComparison.Ordinal) &&
            !SliceMethod(rasterSheetDpiUpgrade, "private bool TryApplyReadyRasterSheetDpiFromMemory(").Contains("TryApplyRasterSheetRender(", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("QueueReadyRasterSheetDpiPersistAfterMemoryApply(page, targetDpi)", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("PersistReadyRasterSheetDpiAfterMemoryApplyAsync", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("pageOpenNavigationVersion: 0", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("TryApplyReadyRasterSheetDpiAtOrAbove", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("SelectReadyRasterSheetDpiAtOrAbove", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("ViewportRenderPolicy.RasterSheetDisplayMaxDpi", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("if (currentDpi == targetDpi)", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("if (currentDpi < targetDpi)", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("if (currentDpi >= targetDpi)", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("TryApplyLowZoomRasterSheetDpiFromMemory", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("TrySwitchRasterSheetToFastPreviewForLowZoom(requestRepaint: false, requireCachedBitmap: true) ||", StringComparison.Ordinal) &&
            lowZoomFastPreviewMethod.Contains("requireCachedBitmap: true", StringComparison.Ordinal) &&
            lowZoomFastPreviewMethod.Contains("allowDiskRead: false", StringComparison.Ordinal) &&
            lowZoomFastPreviewMethod.Contains("requestRepaint: false", StringComparison.Ordinal) &&
            !lowZoomFastPreviewMethod.Contains("allowDiskRead: !requireCachedBitmap", StringComparison.Ordinal) &&
            layers.Contains("bool requestRepaint = true", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("TryGetRememberedReadyRasterSheetSource(page, desiredDpi, out _)", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("TryGetRememberedReadyRasterSheetSource(page, targetDpi, out _)", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("return TryApplyReadyRasterSheetDpiFromMemory(page, targetDpi);", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("currentDpi >= targetDpi", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("targetDpi > ViewportRenderPolicy.RasterSheetDisplayMaxDpi", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("requireCachedBitmap: true", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("requestRepaint: false, requireCachedBitmap: true", StringComparison.Ordinal) &&
            viewport.Contains("PrepareBitmapForImmediateRepaint()", StringComparison.Ordinal) &&
            raster.Contains("TryGetReadyReadableRasterSource", StringComparison.Ordinal) &&
            renderCache.Contains("public static void PrefetchRasterSheetWorkZoomBitmaps", StringComparison.Ordinal) &&
            renderCache.Contains("bool buildMissingDpis = false", StringComparison.Ordinal) &&
            renderCache.Contains("bool allowDuringNavigation = false", StringComparison.Ordinal) &&
            renderCache.Contains("RasterSheetWorkZoomWarmupDpiSteps", StringComparison.Ordinal) &&
            renderCache.Contains("RasterSheetWorkZoomBuildDpiSteps", StringComparison.Ordinal) &&
            renderCache.Contains("RasterSheetCurrentWorkZoomBuildDelayMs", StringComparison.Ordinal) &&
            renderCache.Contains("if (!RasterSheetCacheService.IsSourceImageRaster(source))", StringComparison.Ordinal) &&
            renderCache.Contains("return false;", StringComparison.Ordinal) &&
            renderCache.Contains("if (!buildMissingDpis)", StringComparison.Ordinal) &&
            renderCache.Contains("BuildCachePreservingEnabled(currentPage, scale)", StringComparison.Ordinal) &&
            renderCache.Contains("work-zoom-{dpi}dpi warmed", StringComparison.Ordinal) &&
            renderCache.Contains("allowDuringNavigation", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("buildMissingDpis: false", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("allowDuringNavigation: true", StringComparison.Ordinal) &&
            !rasterSheetDpiUpgrade.Contains("buildMissingDpis: true", StringComparison.Ordinal) &&
            policy.Contains("RasterSheetWorkZoomWarmupDpis = [72, 100, 144]", StringComparison.Ordinal) &&
            policy.Contains("RasterSheetWorkZoomBuildDpis = [72, 100, 144]", StringComparison.Ordinal) &&
            policy.Contains("RasterSheetNavigationMaxDpi = 144", StringComparison.Ordinal) &&
            policy.Contains("RasterSheetMotionQualityRestoreQuietMs", StringComparison.Ordinal) &&
            policy.Contains("ShouldHoldRasterSheetQualityAfterNavigation", StringComparison.Ordinal) &&
            policy.Contains("SelectRasterSheetNavigationDpi", StringComparison.Ordinal) &&
            pageApi.Contains("if (shouldUseRasterSheetForOpen)", StringComparison.Ordinal) &&
            viewTransform.Contains("QueueCurrentRasterSheetMotionWarmup()", StringComparison.Ordinal) &&
            viewTransform.Contains("QueueRasterSheetQualityRestoreAfterMotion", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("ShouldQueueRasterSheetMotionWarmup(page)", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("RasterSheetMotionWarmupKey(page)", StringComparison.Ordinal) &&
            policy.Contains("RasterSheetMotionWarmupMinIntervalMs = 650", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("ShouldSkipStaleRasterSheetSourceApply(currentDpi, targetDpi)", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("sourceDpi <= currentDpi", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("currentDpi >= targetDpi", StringComparison.Ordinal) &&
            responsiveDpiMethod.IndexOf("TryApplyNavigationRasterSheetDpiForCurrentZoom", StringComparison.Ordinal) <
                responsiveDpiMethod.IndexOf("ShouldUseResponsiveRasterSheetDpiForCurrentZoom", StringComparison.Ordinal) &&
            responsiveDpiMethod.Contains("navigationCurrentDpi <= navigationTargetDpi", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("QueueCurrentRasterSheetMotionWarmup", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("TryHoldHeavyRasterSheetDpiForRecentMotion", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("TryApplyMotionHoldRasterSheetDpiFromMemory", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("!_usingRasterSheetRender || _usingRasterSheetOverviewRender", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("TryHoldHeavyRasterSheetDpiForRecentMotion(CurrentRasterSheetPageInfo(), currentDpi, targetDpi)", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("TryApplyNavigationRasterSheetDpiForCurrentZoom", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("TrySwitchRasterSheetToFastPreviewForNavigation(allowWorkZoom: true)", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("allowWorkZoom = false", StringComparison.Ordinal) &&
            navigationFastPreviewMethod.Contains("allowDiskRead: false", StringComparison.Ordinal) &&
            navigationFastPreviewMethod.Contains("requestRepaint: false", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("RasterSheetWorkZoomWarmupDpiSteps", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("postStatus: false", StringComparison.Ordinal) &&
            rendering.Contains("SKFilterQuality.None", StringComparison.Ordinal) &&
            policy.Contains("RasterSheetDisplayMaxDpi", StringComparison.Ordinal) &&
            rasterSheetBitmapCache.Contains("ShouldUseResponsiveRasterSheetDpiForCurrentZoom(rasterSheet)", StringComparison.Ordinal) &&
            rasterDpiUpgradeMethod.Contains("RasterSheetRefreshPrefetchSemaphore.WaitAsync().ConfigureAwait(false)", StringComparison.Ordinal) &&
            !rasterDpiUpgradeMethod.Contains("WaitForPreviewPrefetchQuietWindowAsync", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("RasterSheetCacheService.RenderScaleToDpi(_bitmapScale)", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("Raster sheet {targetDpi} DPI", StringComparison.Ordinal) &&
            rasterSheetDpiUpgrade.Contains("private PageInfo CurrentRasterSheetPageInfo(RasterSheetSource? rasterSheet = null)", StringComparison.Ordinal),
            "zoomed Raster On sheets should switch only to prepared responsive DPI tiers, keep motion frames on lighter prewarmed raster tiers, avoid stale page-open zoom targets, and avoid automatic 400 DPI display");
        AssertTrue(
            viewport.Contains("private IReadOnlyList<PdfGeometrySnapSegment> _rasterSheetVisualSegments = []", StringComparison.Ordinal) &&
            pdfSnap.Contains("QueueRasterSheetVisualSegmentsLoad", StringComparison.Ordinal) &&
            pdfSnap.Contains("LoadRasterSheetVisualSegmentsAsync", StringComparison.Ordinal) &&
            pdfSnap.Contains("var read = await Task.Run", StringComparison.Ordinal) &&
            pdfSnap.Contains("RasterSheetCacheService.TryReadSnapIndex", StringComparison.Ordinal) &&
            pdfSnap.Contains("version != _rasterSheetVisualSegmentVersion", StringComparison.Ordinal) &&
            pdfSnap.Contains("RequestRepaint();", StringComparison.Ordinal) &&
            layers.Contains("QueueRasterSheetVisualSegmentsLoad(pageFolder, pdfPath, pdfIndex, rasterSheet)", StringComparison.Ordinal) &&
            rendering.Contains("DrawLowZoomLineOverlay(canvas, visiblePdf)", StringComparison.Ordinal) &&
            rendering.Contains("LowZoomVisualSegments()", StringComparison.Ordinal) &&
            rendering.Contains("_renderNavigationFastFrame", StringComparison.Ordinal) &&
            rendering.Contains("_pdfSnapEnabled && IsPdfSnapCacheCurrent()", StringComparison.Ordinal) &&
            rendering.Contains("_pdfSnapIndex.Segments", StringComparison.Ordinal) &&
            rendering.Contains("_zoom > 0.55f", StringComparison.Ordinal),
            "low zoom should overlay already-loaded raster or PDF snap segments on idle frames so thin source lines remain readable below 50% zoom without slowing navigation or starting PDF extraction from paint");
        AssertTrue(
            pageApi.Contains("QueueRasterSheetSelfHealIfNeeded(", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("ShouldSelfHealRasterSheet", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("legacy lineboost", StringComparison.Ordinal) &&
            renderCache.Contains("PrefetchRasterSheetRefresh(PageInfo page)", StringComparison.Ordinal) &&
            renderCache.Contains("RasterSheetRefreshPrefetchSemaphore", StringComparison.Ordinal) &&
            renderCache.Contains("RasterSheetRefreshPrefetchCadenceSemaphore", StringComparison.Ordinal) &&
            renderCache.Contains("RasterSheetRefreshPrefetchDelayMs", StringComparison.Ordinal) &&
            renderCache.Contains("RasterSheetRefreshPrefetchCadenceMs", StringComparison.Ordinal) &&
            renderCache.Contains("WaitForRasterSheetRefreshPrefetchCadenceAsync", StringComparison.Ordinal) &&
            renderCache.Contains("WaitForPreviewPrefetchQuietWindowAsync().ConfigureAwait(false)", StringComparison.Ordinal) &&
            viewTransform.Contains("PausePreviewPrefetchFor(ViewportRenderPolicy.PreviewPrefetchNavigationQuietMs)", StringComparison.Ordinal) &&
            renderCache.Contains("ShouldQueueRasterSheetRefreshPrefetch(page)", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("RasterSheetRefreshPrefetchSemaphore.WaitAsync().ConfigureAwait(false)", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("RasterSheetRefreshPrefetchSemaphore.Release();", StringComparison.Ordinal) &&
            renderCache.Contains("Task.Run(() =>", StringComparison.Ordinal) &&
            renderCache.Contains("ConfigureAwait(false)", StringComparison.Ordinal) &&
            renderCache.Contains("Viewport raster refresh prefetched", StringComparison.Ordinal) &&
            renderCache.Contains("ShouldRebuildForReadableDisplay", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("BuildOverviewForExistingSourceImageRaster(page)", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("BuildAndEnable(page)", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("WaitForCurrentPageRasterRebuildWindowAsync", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("Viewport raster sheet self-heal skipped stale page", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("IsCurrentPageRasterTarget", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("CaptureViewState()", StringComparison.Ordinal) &&
            pageApi.Contains("ShouldRebuildForReadableDisplay", StringComparison.Ordinal) &&
            rasterSheetViewport.Contains("_pdfLayersLoadedForPage || _usingLayerRenderer", StringComparison.Ordinal),
            "legacy or stale raster sheets should rebuild in current-page and prefetch background paths, while old oversized image rasters get overview-only rebuilds, then apply only to the still-current non-layer page");
    }

    public static void PdfSheetMetadataParsesDottedSheetNumbersForSuffixRules()
    {
        string helper = ReadRepoFile("Tools/pdf_layers_helper.py");

        AssertTrue(
            helper.Contains("def _sheet_number_code(sheet_label: str | None) -> int | None:", StringComparison.Ordinal) &&
            helper.Contains("def _sheet_label_floor_suffix(sheet_label: str | None) -> str | None:", StringComparison.Ordinal) &&
            helper.Contains("def _has_schedule_word(value: str | None) -> bool:", StringComparison.Ordinal) &&
            helper.Contains("minor.zfill(2)", StringComparison.Ordinal) &&
            helper.Contains("sheet_num = _sheet_number_code(sheet_label)", StringComparison.Ordinal),
            "PDF metadata helper should parse dotted sheet labels as compact sheet numbers such as A4.50 -> 450");
        AssertTrue(
            helper.Contains("if 900 <= sheet_num <= 999:", StringComparison.Ordinal) &&
            helper.Contains("return \"d\", True", StringComparison.Ordinal),
            "A9 architectural door/window/detail sheets should fall back to detail suffixes instead of notes or blank suffixes when title text is weak");
        AssertFalse(
            helper.Contains("num_match = re.search(r\"(\\d{2,4})\", label)", StringComparison.Ordinal),
            "dotted sheet suffix rules must not read A4.50 as 50 and classify it as a note sheet");
        AssertFalse(
            helper.Contains("\"schedule\" in title", StringComparison.Ordinal),
            "suffix rules must not classify phrases such as 'hardware as scheduled' as schedule sheets");

        PdfImportRasterOptionUsesReadableCacheWording();
    }

    public static void PdfImportRasterOptionUsesReadableCacheWording()
    {
        string import = ReadRepoFile("MainWindow.PdfImport.cs");

        AssertTrue(
            import.Contains("Build readable raster cache and strict black-line snap index", StringComparison.Ordinal),
            "PDF import raster option should describe the current readable cache and strict snap behavior");
        AssertFalse(
            import.Contains("Build raster working sheets (v5)", StringComparison.Ordinal),
            "PDF import raster option should not expose stale internal version wording");
    }

    public static void PdfRasterEdgeSnapPreviewIsWired()
    {
        string edgeSnap = ReadRepoFile("Controls/PdfViewport.EdgeSnap.cs");
        string contour = ReadRepoFile("Controls/PdfViewport.EdgeSnapContour.cs");
        string pdfSnap = ReadRepoFile("Controls/PdfViewport.PdfSnap.cs");
        string snapService = ReadRepoFile("Models/PdfGeometrySnapService.cs");

        AssertTrue(
            snapService.Contains("public IReadOnlyList<PdfGeometrySnapSegment> Segments => _segments;", StringComparison.Ordinal) &&
            snapService.Contains("public bool TryFindSegment(", StringComparison.Ordinal) &&
            snapService.Contains("PdfGeometrySnapSegmentHit", StringComparison.Ordinal) &&
            snapService.Contains("public IReadOnlyList<PdfGeometrySnapSegmentHit> FindSegments(", StringComparison.Ordinal),
            "PDF snap index must expose strict line segments and nearby segment hits for edge preview without re-reading the PDF");
        AssertTrue(
            edgeSnap.Contains("TryFindPdfEdgeSnapCandidate", StringComparison.Ordinal) &&
            edgeSnap.Contains("Math.Max(tolerance, bridgeTolerancePt)", StringComparison.Ordinal) &&
            edgeSnap.Contains("_pdfSnapIndex.FindSegments(rawPdf, searchTolerance)", StringComparison.Ordinal) &&
            edgeSnap.Contains("RankPdfEdgeSnapSegmentHits", StringComparison.Ordinal) &&
            edgeSnap.Contains("preferClosedBoundary", StringComparison.Ordinal) &&
            edgeSnap.Contains("_pdfSnapIndex.Segments", StringComparison.Ordinal) &&
            edgeSnap.Contains("if (!found &&", StringComparison.Ordinal) &&
            edgeSnap.Contains("BuildPdfSnapContour", StringComparison.Ordinal) &&
            edgeSnap.Contains("_tool == ViewerTool.Area", StringComparison.Ordinal),
            "Edge Snap should use ranked loaded PDF/raster snap segments as a second preview source without overriding ordinary takeoff edge snap");
        AssertTrue(
            edgeSnap.Contains("TryFindUniqueConnectedPdfSnapSegment", StringComparison.Ordinal) &&
            edgeSnap.Contains("if (matches > 1 &&", StringComparison.Ordinal) &&
            edgeSnap.Contains("PdfSnapDirectionalBridgeFactor", StringComparison.Ordinal) &&
            edgeSnap.Contains("PdfSnapStrokeWidthPenalty", StringComparison.Ordinal) &&
            edgeSnap.Contains("PdfSnapEndpointTolerancePt", StringComparison.Ordinal),
            "PDF/raster contour preview should continue directional gaps, prefer matching thick strokes, and stop at equally ambiguous branches");
        AssertTrue(
            edgeSnap.Contains("PdfSnapBridgeToleranceScreenPx", StringComparison.Ordinal) &&
            edgeSnap.Contains("ScreenToPdfDistance((float)PdfSnapBridgeToleranceScreenPx)", StringComparison.Ordinal) &&
            edgeSnap.Contains("PdfSnapBridgeToleranceMaxPt", StringComparison.Ordinal) &&
            edgeSnap.Contains("matchedPoint", StringComparison.Ordinal),
            "PDF/raster edge preview should bridge small screen-pixel gaps while preserving the next real segment endpoint");
        AssertTrue(
            edgeSnap.Contains("label = \"pdf \" + label", StringComparison.Ordinal) &&
            pdfSnap.Contains("PDF Snap ready from raster index", StringComparison.Ordinal),
            "PDF/raster edge preview should be visible to the user and prefer the persisted strict raster index");
        AssertTrue(
            edgeSnap.Contains("private int NextEdgeSnapCycleMode()", StringComparison.Ordinal) &&
            edgeSnap.Contains("_tool == ViewerTool.Area", StringComparison.Ordinal) &&
            edgeSnap.Contains("return EdgeSnapModeContour;", StringComparison.Ordinal) &&
            edgeSnap.Contains("EdgeSnapModePolylineAll", StringComparison.Ordinal) &&
            edgeSnap.Contains("EdgeSnapModePolylineEverything", StringComparison.Ordinal) &&
            edgeSnap.Contains("polyline all", StringComparison.Ordinal) &&
            edgeSnap.Contains("polyline everything", StringComparison.Ordinal),
            "Area should jump straight to closed PDF/raster contour mode and then allow more aggressive polyline all/everything cycles");
        AssertFalse(
            edgeSnap.Contains("SnapEnabled = true;", StringComparison.Ordinal) ||
            edgeSnap.Contains("PdfSnapEnabled = true;", StringComparison.Ordinal) ||
            edgeSnap.Contains("PDF Snap loading for edge contour", StringComparison.Ordinal),
            "Tab in Line/Area must not auto-enable Snap or PDF Snap because ordinary area/line edge snap is a separate workflow");
        AssertTrue(
            contour.Contains("TryBuildPdfSnapBoundaryContour", StringComparison.Ordinal) &&
            contour.Contains("TryBuildPdfSnapRasterBoundaryContour", StringComparison.Ordinal) &&
            contour.Contains("TryChooseLargestPdfSnapBoundaryContour", StringComparison.Ordinal) &&
            contour.Contains("bridgeTolerancePt * 0.025f", StringComparison.Ordinal) &&
            contour.Contains("2.25f", StringComparison.Ordinal) &&
            contour.Contains("PdfSnapBoundaryGraphBridgeTolerance", StringComparison.Ordinal) &&
            contour.Contains("PdfSnapBoundaryBridgeMinAlignment", StringComparison.Ordinal) &&
            contour.Contains("ProjectPdfSnapBoundaryPoints", StringComparison.Ordinal),
            "Area PDF/raster contour mode should include a guarded bridge-tolerance-driven probable closed boundary pass while preserving short exterior jog edges");
        string wallCore = ReadRepoFile("Controls/PdfViewport.EdgeSnapWallCore.cs");
        AssertTrue(
            wallCore.Contains("PdfSnapBoundaryMode.All", StringComparison.Ordinal) &&
            wallCore.Contains("PdfSnapBoundaryMode.Everything", StringComparison.Ordinal) &&
            wallCore.Contains("PdfSnapLooksLikeInteriorDoorSymbol", StringComparison.Ordinal) &&
            wallCore.Contains("SelectPdfSnapRasterBoundaryComponent", StringComparison.Ordinal),
            "PDF/raster contour all/everything modes should suppress interior door-like arcs and narrow paired door lines before tracing");
        string door = ReadRepoFile("Controls/PdfViewport.EdgeSnapDoor.cs");
        AssertTrue(
            door.Contains("PdfSnapGeometrySegmentLooksLikeInteriorDoorCandidate", StringComparison.Ordinal) &&
            door.Contains("PdfSnapBoundaryAxisSegmentHasDoorPair", StringComparison.Ordinal) &&
            door.Contains("PdfSnapDoorArcTouchesAxisSegment", StringComparison.Ordinal),
            "PDF/raster contour door detection should keep paired interior door swing symbols out of area contour fallback");
    }

    public static void PdfPreviewRenderCacheIsWiredBeforeLayerRender()
    {
        string pageApi = ReadRepoFile("Controls/PdfViewport.PageApi.cs");
        string layers = ReadRepoFile("Controls/PdfViewport.Layers.cs");
        string persistedPreview = ReadRepoFile("Controls/PdfViewport.PersistedPreview.cs");
        string service = ReadPdfLayerRenderServiceSources();
        string cache = ReadRepoFile("Models/PdfPreviewRenderCache.cs");

        int cacheApply = pageApi.IndexOf("TryApplyPersistedPreviewRender", StringComparison.Ordinal);
        int queueRender = pageApi.IndexOf("QueueDocnetRender(", StringComparison.Ordinal);
        AssertTrue(
            cacheApply >= 0 && queueRender > cacheApply,
            "persisted clean preview cache should be applied before queueing the fast fallback preview render");
        AssertFalse(
            pageApi.Contains("QueueLayerRender(", StringComparison.Ordinal),
            "normal page open must not queue PDF layer render work before the user explicitly loads layers");
        int previewMethod = layers.IndexOf("private void ApplyPreviewBitmapRender", StringComparison.Ordinal);
        int previewMarksNonLayer = previewMethod >= 0
            ? layers.IndexOf("_usingLayerRenderer = false;", previewMethod, StringComparison.Ordinal)
            : -1;
        AssertTrue(
            previewMethod >= 0 && previewMarksNonLayer > previewMethod,
            "cached PyMuPDF previews should stay outside layer-render mode so zoom refreshes do not schedule layer-cache-only work");
        AssertTrue(
            layers.Contains("TryApplyPersistedPreviewRender", StringComparison.Ordinal) &&
            persistedPreview.Contains("PdfPreviewRenderCache.TryReadCleanPreview", StringComparison.Ordinal) &&
            layers.Contains("TryApplyPersistedPreviewRenderScale", StringComparison.Ordinal) &&
            layers.Contains("ViewportRenderPolicy.FastPageSwitchPreviewRenderScale", StringComparison.Ordinal) &&
            layers.Contains("PersistedPreviewBitmapCache.TryGet", StringComparison.Ordinal) &&
            layers.Contains("DocnetRenderCache.TryGet(cacheKey", StringComparison.Ordinal) &&
            layers.Contains("preview-render-memory", StringComparison.Ordinal) &&
            persistedPreview.Contains("PersistedPreviewBitmapCache.Put", StringComparison.Ordinal) &&
            layers.Contains("preview-memory", StringComparison.Ordinal) &&
            layers.Contains("ApplyInitialPreviewView", StringComparison.Ordinal) &&
            layers.Contains("Viewport PyMuPDF preview cache hit", StringComparison.Ordinal),
            "viewport should read and apply cached clean PyMuPDF previews without using Docnet and keep decoded previews hot in RAM");
        AssertTrue(
            service.Contains("PdfPreviewRenderCache.IsCleanRenderRequest", StringComparison.Ordinal) &&
            service.Contains("PdfPreviewRenderCache.TryWriteCleanRender", StringComparison.Ordinal),
            "successful clean PyMuPDF renders should populate the persisted render cache");
        AssertTrue(
            cache.Contains("CacheRootEnvironmentVariable", StringComparison.Ordinal) &&
            cache.Contains("LastWriteTimeUtc.Ticks", StringComparison.Ordinal) &&
            cache.Contains("PreviewCacheIdentity", StringComparison.Ordinal) &&
            cache.Contains("PdfFingerprint", StringComparison.Ordinal) &&
            cache.Contains("RelocatedLegacyCacheIndex", StringComparison.Ordinal) &&
            cache.Contains("ViewportRenderPolicy.ColdPageSwitchPreviewRenderScale - 0.001f", StringComparison.Ordinal) &&
            cache.Contains("File.Move(tempImage, paths.ImagePath, overwrite: true)", StringComparison.Ordinal),
            "preview cache should be keyed by portable source identity, support cold and fast page-switch previews, and write atomically through temp files");
    }

    public static void PdfPageOpenUsesDocnetPreviewOnCacheMiss()
    {
        string pageApi = ReadRepoFile("Controls/PdfViewport.PageApi.cs");
        string layers = ReadRepoFile("Controls/PdfViewport.Layers.cs");
        string persistedPreview = ReadRepoFile("Controls/PdfViewport.PersistedPreview.cs");
        string detail = ReadRepoFile("Controls/PdfViewport.DetailRender.cs");
        string renderCache = ReadRepoFile("Controls/PdfViewport.RenderCache.cs");
        string policy = ReadRepoFile("Models/ViewportRenderPolicy.cs");
        string mainLayers = ReadRepoFile("MainWindow.PdfLayers.cs");
        string loadPageMethod = SliceMethod(pageApi, "public void LoadPage(");
        string previewApplyMethod = SliceMethod(layers, "private bool TryApplyPersistedPreviewRenderScale(");
        string queuedPreviewMethod = SliceMethod(persistedPreview, "private async Task StartPersistedPreviewRenderAfterFirstRepaintAsync(");

        int cacheApply = pageApi.IndexOf("TryApplyPersistedPreviewRender", StringComparison.Ordinal);
        int cacheBranch = pageApi.IndexOf("if (previewCacheHit)", StringComparison.Ordinal);
        int rasterWarmBranch = pageApi.IndexOf("else if (rasterBitmapWarmupQueuedForOpen)", StringComparison.Ordinal);
        int responsiveRasterWork = pageApi.IndexOf("responsiveRasterDpiWorkQueuedForOpen = QueueResponsiveRasterSheetDpiBuildForPageOpen", StringComparison.Ordinal);
        int docnetFallback = pageApi.IndexOf("QueueDocnetRender(", StringComparison.Ordinal);
        int status = pageApi.IndexOf("PostStatus((rasterBitmapWarmupQueuedForOpen", StringComparison.Ordinal);
        AssertTrue(
            cacheApply >= 0 &&
            responsiveRasterWork >= 0 &&
            cacheBranch > cacheApply &&
            rasterWarmBranch > cacheBranch &&
            docnetFallback > rasterWarmBranch &&
            status > docnetFallback,
            "page open should avoid full-clean synchronous decode, let blocking raster bitmap warmups win, then keep a fast lightweight preview fallback while responsive raster DPI warms in the background");
        AssertTrue(
            pageApi.Contains("queueLayerAfter: false", StringComparison.Ordinal) &&
            pageApi.Contains("resetLayerStates: false", StringComparison.Ordinal) &&
            pageApi.Contains("fireLayersAfter: false", StringComparison.Ordinal) &&
            pageApi.Contains("float previewScale = PageSwitchPreviewRenderScale(restoreView, fitAfter: !restoreView.HasValue)", StringComparison.Ordinal) &&
            pageApi.Contains("PageSwitchLivePreviewScale(restoreView, fitAfter: !restoreView.HasValue)", StringComparison.Ordinal) &&
            pageApi.Contains("bool rasterBitmapWarmupQueuedForOpen = false;", StringComparison.Ordinal) &&
            pageApi.Contains("bool rasterWorkZoomWarmupQueuedForOpen = false;", StringComparison.Ordinal) &&
            pageApi.Contains("bool responsiveRasterDpiWorkQueuedForOpen = false;", StringComparison.Ordinal) &&
            pageApi.Contains("rasterBitmapWarmupQueuedForOpen = QueueRasterSheetBitmapApplyAfterWarmup", StringComparison.Ordinal) &&
            pageApi.Contains("!rasterBitmapWarmupQueuedForOpen &&", StringComparison.Ordinal) &&
            pageApi.Contains("!responsiveRasterDpiWorkQueuedForOpen &&", StringComparison.Ordinal) &&
            pageApi.Contains("!rasterWorkZoomWarmupQueuedForOpen", StringComparison.Ordinal) &&
            pageApi.Contains("QueueSharpBaseRenderAfterPreview(pdfPath, pageIndex, pageFolder)", StringComparison.Ordinal) &&
            pageApi.Contains("QueuePersistedPreviewRenderAfterFirstRepaint(", StringComparison.Ordinal) &&
            pageApi.Contains("allowDiskRead: false", StringComparison.Ordinal) &&
            pageApi.Contains("else if (rasterBitmapWarmupQueuedForOpen)", StringComparison.Ordinal) &&
            !pageApi.Contains("else if (responsiveRasterDpiWorkQueuedForOpen)", StringComparison.Ordinal) &&
            pageApi.Contains("(rasterBitmapWarmupQueuedForOpen || responsiveRasterDpiWorkQueuedForOpen) && !previewCacheHit", StringComparison.Ordinal) &&
            layers.Contains("CancelPendingDocnetRenderForAppliedBitmap", StringComparison.Ordinal) &&
            layers.Contains("_docnetRenderVersion++", StringComparison.Ordinal) &&
            pageApi.Contains("Raster preparing:", StringComparison.Ordinal) &&
            pageApi.Contains("ArePdfLayersLoaded => _pdfLayersLoadedForPage", StringComparison.Ordinal) &&
            pageApi.Contains("_pdfLayersLoadedForPage = false", StringComparison.Ordinal) &&
            pageApi.Contains("FireLayersChanged();", StringComparison.Ordinal) &&
            mainLayers.Contains("PDF layers not loaded. Click Load to scan this sheet.", StringComparison.Ordinal),
            "normal page opens should keep PDF layers lazy, and responsive raster-warm page opens should not start a sharp PDF render while still allowing a cheap preview fallback");
        AssertTrue(
            loadPageMethod.Contains("allowDiskRead: false", StringComparison.Ordinal) &&
            loadPageMethod.Contains("QueuePersistedPreviewRenderAfterFirstRepaint(", StringComparison.Ordinal) &&
            previewApplyMethod.Contains("if (!allowDiskRead)", StringComparison.Ordinal) &&
            queuedPreviewMethod.Contains("Dispatcher.Yield", StringComparison.Ordinal) &&
            queuedPreviewMethod.Contains("Task.Run", StringComparison.Ordinal) &&
            queuedPreviewMethod.Contains("IsCurrentPersistedPreviewRenderTarget", StringComparison.Ordinal) &&
            queuedPreviewMethod.Contains("ShouldSkipPersistedPreviewRender", StringComparison.Ordinal) &&
            queuedPreviewMethod.Contains("TryReadPersistedPreviewBitmapForPageOpen", StringComparison.Ordinal) &&
            queuedPreviewMethod.Contains("appliedRenderScale", StringComparison.Ordinal) &&
            persistedPreview.Contains("ShouldPreferReadablePreviewFirst", StringComparison.Ordinal) &&
            persistedPreview.Contains("ViewportRenderPolicy.InitialPagePreviewRenderScale", StringComparison.Ordinal) &&
            persistedPreview.Contains("ViewportRenderPolicy.ColdPageSwitchPreviewRenderScale", StringComparison.Ordinal),
            "page open should keep persisted preview disk reads and PNG decode off the immediate UI-thread path");
        AssertFalse(
            loadPageMethod.Contains("PdfPreviewRenderCache.TryReadCleanPreview", StringComparison.Ordinal) ||
            loadPageMethod.Contains("SKBitmap.Decode", StringComparison.Ordinal),
            "LoadPage itself must not synchronously read or decode persisted preview images before the first repaint");
        AssertTrue(
            layers.Contains("if (_cachedLayers != null)", StringComparison.Ordinal) &&
            layers.Contains("PDF Layers: loading cached page layers...", StringComparison.Ordinal) &&
            layers.Contains("CompleteLayerlessRender(\"PDF Layers loaded.\", fireLayersAfter: true)", StringComparison.Ordinal),
            "manual PDF Layers Load should use cached manifests first and avoid rendering when a PDF has no layers");
        AssertFalse(
            pageApi.Contains("TryApplyHotLayerBitmapForPageOpen", StringComparison.Ordinal) ||
            pageApi.Contains("Cached page:", StringComparison.Ordinal),
            "normal page opens should not prefer full layer bitmaps over lightweight previews when PDF layers are lazy");
        AssertTrue(
            layers.Contains("QueueSharpLayerRenderAfterPreview(", StringComparison.Ordinal) &&
            layers.Contains("QueueSharpBaseRenderAfterPreview(", StringComparison.Ordinal) &&
            layers.Contains("PageSwitchPreviewRenderScale", StringComparison.Ordinal) &&
            layers.Contains("ShouldUseReadablePageSwitchBase", StringComparison.Ordinal) &&
            layers.Contains("ViewportRenderPolicy.ResponsiveMinRenderScale", StringComparison.Ordinal) &&
            layers.Contains("IsCurrentPageDocnetRenderTarget", StringComparison.Ordinal) &&
            layers.Contains("QueueDocnetRender(renderScale)", StringComparison.Ordinal) &&
            layers.Contains("PageSwitchSharpUpgradeDelayMs", StringComparison.Ordinal) &&
            layers.Contains("ShouldDelaySharpLayerUpgrade(deferralCount)", StringComparison.Ordinal) &&
            layers.Contains("PageSwitchSharpUpgradeIdleMs", StringComparison.Ordinal) &&
            layers.Contains("ShouldUseDetailRenderForSharpUpgrade()", StringComparison.Ordinal) &&
            layers.Contains("TryUseDetailRenderOnlyForSharpUpgrade()", StringComparison.Ordinal) &&
            layers.Contains("ShouldUpgradeUnreadablePageSwitchBaseImmediately()", StringComparison.Ordinal) &&
            layers.Contains("HasReadableBaseBitmapForSharpUpgrade()", StringComparison.Ordinal) &&
            layers.Contains("ShouldSkipSharpLayerUpgradeForLowZoom()", StringComparison.Ordinal) &&
            layers.Contains("ShouldSkipQueuedNonFastPreviewForCurrentView(request.RenderScale)", StringComparison.Ordinal) &&
            layers.Contains("CurrentPostPreviewBaseRenderScale()", StringComparison.Ordinal) &&
            layers.Contains("ViewportRenderPolicy.ShouldPreferLowerScalePageBitmapForNavigation", StringComparison.Ordinal) &&
            layers.Contains("PageSwitchSharpUpgradeMinZoom", StringComparison.Ordinal) &&
            layers.Contains("TryQueueCachedReadablePreviewUpgradeForLowZoom", StringComparison.Ordinal) &&
            layers.Contains("queueSharpBaseAfterPreview: false", StringComparison.Ordinal) &&
            layers.Contains("IsCurrentPageRenderTarget", StringComparison.Ordinal) &&
            layers.Contains("allowLiveRender: true", StringComparison.Ordinal) &&
            layers.Contains("ShouldSkipLowerQualityDocnetPreview", StringComparison.Ordinal) &&
            layers.Contains("IsPageBitmapFor(request.PdfPath, request.PdfIndex, request.PageFolder)", StringComparison.Ordinal) &&
            layers.Contains("Viewport skipped lower-quality Docnet preview", StringComparison.Ordinal),
            "explicit layer continuation paths should keep delayed sharp live render safeguards for stale low-scale previews while forcing a readable base behind detail tiles");
        AssertTrue(
            layers.Contains("bool allowImmediateCache = true", StringComparison.Ordinal) &&
            layers.Contains("bool allowLiveRender = true", StringComparison.Ordinal) &&
            layers.Contains("bool allowMemoryBitmap = true", StringComparison.Ordinal) &&
            layers.Contains("allowMemoryBitmap && TryApplyLayerBitmapCache", StringComparison.Ordinal) &&
            layers.Contains("ShouldUseCacheOnlyForAutomaticLayerRender(request)", StringComparison.Ordinal) &&
            layers.Contains("IsAutomaticViewportLayerRender(request)", StringComparison.Ordinal) &&
            layers.Contains("request.StatusAfter.StartsWith(\"Loaded:\", StringComparison.Ordinal)", StringComparison.Ordinal) &&
            layers.Contains("if (!automaticViewportRender || forceDetail)", StringComparison.Ordinal) &&
            layers.Contains("if (!automaticViewportRender || request.RestoreView.HasValue || request.FitAfter || forceDetail)", StringComparison.Ordinal) &&
            layers.Contains("allowLiveRender: false", StringComparison.Ordinal) &&
            layers.Contains("allowMemoryBitmap: true", StringComparison.Ordinal) &&
            layers.Contains("allowImmediateCache && TryApplyPersistedCleanLayerRender(request)", StringComparison.Ordinal) &&
            layers.Contains("CompleteCacheOnlyLayerRender(request)", StringComparison.Ordinal),
            "automatic viewport layer refresh should be able to collapse to cache-only while explicit layer paths keep the full render fallback");
        AssertTrue(
            pageApi.Contains("ClearPreviousPageBitmapDuringSwitch();", StringComparison.Ordinal) &&
            pageApi.Contains("PageSwitchPreviewRenderScale", StringComparison.Ordinal) &&
            layers.Contains("ViewportRenderPolicy.ColdPageSwitchPreviewRenderScale", StringComparison.Ordinal) &&
            layers.Contains("PageSwitchFastPreviewScale", StringComparison.Ordinal) &&
            policy.Contains("FastPageSwitchPreviewRenderScale = 0.35f", StringComparison.Ordinal) &&
            policy.Contains("ColdPageSwitchPreviewRenderScale = 0.20f", StringComparison.Ordinal),
            "cache-miss page switches should use a readable base at restored work zoom and an even cheaper cold preview when the view is fitted");
        AssertTrue(
            layers.Contains("private bool TryApplyPersistedDefaultCleanRender", StringComparison.Ordinal) &&
            layers.Contains("TryApplyPersistedCleanLayerRender(request)", StringComparison.Ordinal),
            "explicit layer refresh paths should still be able to apply a persisted clean full render before falling back to PyMuPDF");
        AssertTrue(
            renderCache.Contains("ViewportRenderPolicy.FastPageSwitchPreviewRenderScale", StringComparison.Ordinal) &&
            renderCache.Contains("ViewportRenderPolicy.PreviewPrefetchDelayMs", StringComparison.Ordinal) &&
            policy.Contains("PreviewPrefetchDelayMs = 100", StringComparison.Ordinal) &&
            policy.Contains("PreviewPrefetchNavigationQuietMs = 1100", StringComparison.Ordinal) &&
            policy.Contains("PreviewPrefetchActiveRenderHoldMs = 3000", StringComparison.Ordinal) &&
            policy.Contains("PreviewPrefetchAfterActiveRenderHoldMs = 750", StringComparison.Ordinal) &&
            renderCache.Contains("PreviewPrefetchPausedUntilUtcTicks", StringComparison.Ordinal) &&
            renderCache.Contains("WaitForPreviewPrefetchQuietWindowAsync", StringComparison.Ordinal) &&
            renderCache.Contains("PausePreviewPrefetchFor", StringComparison.Ordinal) &&
            detail.Contains("PausePreviewPrefetchFor(ViewportRenderPolicy.PreviewPrefetchActiveRenderHoldMs)", StringComparison.Ordinal) &&
            detail.Contains("PausePreviewPrefetchFor(ViewportRenderPolicy.PreviewPrefetchAfterActiveRenderHoldMs)", StringComparison.Ordinal) &&
            layers.Contains("PausePreviewPrefetchFor(ViewportRenderPolicy.PreviewPrefetchNavigationQuietMs)", StringComparison.Ordinal) &&
            layers.Contains("PausePreviewPrefetchFor(ViewportRenderPolicy.PreviewPrefetchActiveRenderHoldMs)", StringComparison.Ordinal) &&
            renderCache.Contains("PreviewPrefetchSemaphore", StringComparison.Ordinal) &&
            renderCache.Contains("LivePreviewRenderSemaphore", StringComparison.Ordinal) &&
            persistedPreview.Contains("_persistedPreviewRenderInFlightVersion = version", StringComparison.Ordinal) &&
            persistedPreview.Contains("WaitForPersistedPreviewRenderBeforeLiveFallbackAsync", StringComparison.Ordinal) &&
            layers.Contains("WaitForPersistedPreviewRenderBeforeLiveFallbackAsync(request)", StringComparison.Ordinal) &&
            policy.Contains("PersistedPreviewLiveFallbackGraceMs = 35", StringComparison.Ordinal) &&
            renderCache.Contains("PrefetchPagePreview(string pdfPath, int pageIndex, float renderScale)", StringComparison.Ordinal) &&
            renderCache.Contains("preferCachedRenderImmediately", StringComparison.Ordinal) &&
            renderCache.Contains("TryPrefetchPagePreviewFromPersistedCacheAsync", StringComparison.Ordinal) &&
            renderCache.Contains("source='persisted-urgent'", StringComparison.Ordinal) &&
            renderCache.Contains("PdfLayerRenderService.TryRenderDedicatedProcessAsync", StringComparison.Ordinal) &&
            renderCache.Contains("DecodePdfLayerRenderBitmapWithMetrics(", StringComparison.Ordinal) &&
            renderCache.Contains("TryWritePrefetchedPreviewCache(pdfPath, pageIndex, renderScale, preview)", StringComparison.Ordinal) &&
            renderCache.Contains("TryWritePrefetchedPreviewCache(pdfPath, pageIndex, renderScale, render)", StringComparison.Ordinal) &&
            renderCache.Contains("PdfPreviewRenderCache.TryWriteCleanPreview", StringComparison.Ordinal) &&
            renderCache.Contains("PdfPreviewRenderCache.TryWriteCleanRender", StringComparison.Ordinal) &&
            renderCache.Contains("Viewport preview prefetched", StringComparison.Ordinal) &&
            layers.Contains("TryWriteDocnetPreviewCache", StringComparison.Ordinal) &&
            layers.Contains("PdfPreviewRenderCache.TryWriteCleanPreview", StringComparison.Ordinal) &&
            layers.Contains("PdfPreviewRenderCache.TryWriteCleanRender", StringComparison.Ordinal) &&
            layers.Contains("SKEncodedImageFormat.Png", StringComparison.Ordinal),
            "nearby sheet prefetch and cold Docnet preview renders should warm the same lightweight persisted preview cache used by page switching");
        AssertTrue(
            layers.Contains("TryRenderFastPreviewForPageSwitchAsync", StringComparison.Ordinal) &&
            layers.Contains("TryRenderPreviewWithDocnetAsync", StringComparison.Ordinal) &&
            layers.Contains("return (docnet, false)", StringComparison.Ordinal) &&
            layers.Contains("TryRenderPreviewWithPyMuPdfAsync", StringComparison.Ordinal) &&
            layers.Contains("return (pymupdf, pymupdf != null)", StringComparison.Ordinal) &&
            layers.Contains("IsPreviewRenderScale", StringComparison.Ordinal) &&
            layers.Contains("ViewportRenderPolicy.FastPageSwitchPreviewRenderScale", StringComparison.Ordinal) &&
            layers.Contains("ViewportRenderPolicy.ColdPageSwitchPreviewRenderScale", StringComparison.Ordinal) &&
            layers.Contains("ViewportRenderPolicy.InitialPagePreviewRenderScale", StringComparison.Ordinal) &&
            layers.Contains("ViewportRenderPolicy.FastPageSwitchPreviewCoalesceMs", StringComparison.Ordinal) &&
            layers.Contains("LivePreviewRenderSemaphore.WaitAsync", StringComparison.Ordinal) &&
            layers.Contains("PdfLayerRenderService.TryRenderAsync", StringComparison.Ordinal) &&
            layers.Contains("StartFastPreviewRenderAsync", StringComparison.Ordinal) &&
            layers.Contains("preview-pymupdf", StringComparison.Ordinal) &&
            layers.Contains("RenderPageBitmapWithDocnet", StringComparison.Ordinal),
            "cold page-switch previews should prefer in-process Docnet, retain PyMuPDF as a fallback, and coalesce stale page switches");
        string fastPreviewMethod = SliceMethod(layers, "private static async Task<(DocnetRenderResult? Render, bool UsedPyMuPdf)> TryRenderFastPreviewForPageSwitchAsync(");
        AssertTrue(
            fastPreviewMethod.IndexOf("TryRenderPreviewWithDocnetAsync(request)", StringComparison.Ordinal) <
            fastPreviewMethod.IndexOf("TryRenderPreviewWithPyMuPdfAsync(request)", StringComparison.Ordinal),
            "live page-switch preview should try Docnet before the slower external PyMuPDF fallback on cold cache misses");
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
            service.Contains("ResolveRenderCacheRamBudget(768_000_000L, 2_560_000_000L, 0.025)", StringComparison.Ordinal) &&
            service.Contains("ImageBase64", StringComparison.Ordinal) &&
            service.Contains("InlineRawImage = hasClip || allowRawFullPage", StringComparison.Ordinal) &&
            service.Contains("QueueCleanRenderPersistFromRaw", StringComparison.Ordinal) &&
            service.Contains("InlineRawRenderImageMaxPixels = 4_000_000", StringComparison.Ordinal) &&
            service.Contains("ImageRawBase64", StringComparison.Ordinal) &&
            service.Contains("Convert.FromBase64String(response.ImageBase64)", StringComparison.Ordinal),
            "C# layer rendering should request bounded raw detail images plus RAM-sized bounded inline PNG fallback data");
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
            helper.Contains("\"image_raw_base64\"", StringComparison.Ordinal) &&
            helper.Contains("base.tobytes(\"png\")", StringComparison.Ordinal) &&
            helper.Contains("\"image_base64\"", StringComparison.Ordinal) &&
            helper.Contains("base.save(image_path)", StringComparison.Ordinal),
            "Python helper should return raw detail image data, inline PNG data for bounded renders, and fall back to the existing PNG file path");
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
        AssertTrue(
            helper.Contains("def _extract_right_title_block_title", StringComparison.Ordinal) &&
            helper.Contains("float(w[2]) - float(w[0]) >= 18.0", StringComparison.Ordinal) &&
            helper.Contains("abs(float(column[0][0]) - x) <= 18.0", StringComparison.Ordinal) &&
            helper.Contains("right_title or", StringComparison.Ordinal),
            "sheet metadata helper should prefer the right-side rotated title block before body text so plan sheets do not inherit note or legend suffixes");
        AssertTrue(
            helper.Contains("if is_struct and \"section\" in title:", StringComparison.Ordinal) &&
            helper.Contains("500 <= sheet_num <= 799", StringComparison.Ordinal) &&
            helper.Contains("return \"d\", True", StringComparison.Ordinal),
            "structural S5/S6/S7 section sheets should classify as unscaled detail sheets");
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
        string rasterDpi = ReadRepoFile("Controls/PdfViewport.RasterSheetDpiUpgrade.cs");
        string rasterPrepared = ReadRepoFile("Controls/PdfViewport.RasterSheetPreparedApply.cs");
        string policy = ReadRepoFile("Models/ViewportRenderPolicy.cs");
        string service = ReadPdfLayerRenderServiceSources();
        string helper = ReadRepoFile(Path.Combine("Tools", "pdf_layers_helper.py"));

        AssertTrue(
            policy.Contains("DetailRenderEnabled = true", StringComparison.Ordinal) &&
            policy.Contains("CurrentResponsiveMaxRenderScale", StringComparison.Ordinal) &&
            policy.Contains("new RenderQuality(3.0f, 160_000_000f", StringComparison.Ordinal) &&
            policy.Contains("new RenderQuality(4.0f, 240_000_000f", StringComparison.Ordinal) &&
            policy.Contains("ZoomRefreshMinZoom = 0.55f", StringComparison.Ordinal) &&
            policy.Contains("DetailRenderMinZoom = 0.75f", StringComparison.Ordinal) &&
            policy.Contains("DetailRenderMinScaleGain = 1.04f", StringComparison.Ordinal) &&
            policy.Contains("DetailRenderMaxScale = 16.0f", StringComparison.Ordinal) &&
            policy.Contains("SelectDetailRenderScale", StringComparison.Ordinal) &&
            policy.Contains("ShouldUseZoomRefreshRender", StringComparison.Ordinal) &&
            policy.Contains("ShouldSkipFullRefreshDuringDetail", StringComparison.Ordinal) &&
            policy.Contains("DetailRenderPaddingScreenPxForZoom", StringComparison.Ordinal) &&
            policy.Contains("DetailRenderMaxPixels", StringComparison.Ordinal) &&
            policy.Contains("DetailRenderPrefetchEnabled = true", StringComparison.Ordinal) &&
            policy.Contains("DetailRenderPrefetchMinZoom = 2.5f", StringComparison.Ordinal) &&
            policy.Contains("DetailRenderPrefetchConcurrency = Math.Clamp(Environment.ProcessorCount / 3, 1, 4)", StringComparison.Ordinal) &&
            policy.Contains("DetailRenderCoalesceDelayMs = 80", StringComparison.Ordinal) &&
            policy.Contains("DetailInteractiveMaxScale", StringComparison.Ordinal) &&
            policy.Contains("DetailRenderMaxPaintTiles = 4", StringComparison.Ordinal) &&
            policy.Contains("DetailRenderStableTileScreenPx", StringComparison.Ordinal) &&
            policy.Contains("DetailRenderStableTileMaxExpansionFactor", StringComparison.Ordinal) &&
            policy.Contains("ShouldUseDetailRenderPrefetch", StringComparison.Ordinal) &&
            policy.Contains("allowDuringNavigationPrefetch", StringComparison.Ordinal),
            "viewport policy should cap full-sheet renders separately from viewport-sized detail renders");
        AssertTrue(
            pageApi.Contains("TryApplyPersistedPreviewRender", StringComparison.Ordinal) &&
            pageApi.Contains("PageSwitchPreviewRenderScale", StringComparison.Ordinal) &&
            pageApi.Contains("_bitmapScale >= previewScale * 0.95f", StringComparison.Ordinal) &&
            layers.Contains("ShouldUseReadablePageSwitchBase", StringComparison.Ordinal) &&
            pageApi.Contains("BeginFastNavigation();", StringComparison.Ordinal) &&
            pageApi.Contains("queueLayerAfter: false", StringComparison.Ordinal) &&
            pageApi.Contains("resetLayerStates: false", StringComparison.Ordinal) &&
            pageApi.Contains("fireLayersAfter: false", StringComparison.Ordinal) &&
            pageApi.Contains("QueueSharpBaseRenderAfterPreview(pdfPath, pageIndex, pageFolder)", StringComparison.Ordinal) &&
            pageApi.Contains("BeginPageSwitchDetailRenderHold();", StringComparison.Ordinal) &&
            layers.Contains("QueueSharpBaseRenderAfterPreview(") &&
            layers.Contains("CurrentRenderScale()", StringComparison.Ordinal),
            "interactive page opens should keep PDF layers lazy, use readable base renders at restored work zoom, then use clipped detail instead of launching an immediate full-sheet layer render");
        AssertTrue(
            rendering.Contains("SKFilterQuality.Low", StringComparison.Ordinal) &&
            rendering.Contains("DetailRenderCoversVisibleViewForPaint()", StringComparison.Ordinal) &&
            rendering.Contains("DrawDetailRenderTile(canvas)", StringComparison.Ordinal),
            "paint should use cheap bitmap sampling during responsive navigation and skip the heavy base bitmap when a high-DPI detail tile covers the view");
        AssertFalse(
            rendering.Contains("SKFilterQuality.High", StringComparison.Ordinal),
            "main PDF bitmap paint must not use high-quality sampling on the interactive path");
        AssertTrue(
            transform.Contains("ViewportRenderPolicy.ShouldUseDetailRender(_zoom, _bitmapScale)", StringComparison.Ordinal) &&
            transform.Contains("_zoom < ViewportRenderPolicy.FarZoomFastFrameThreshold", StringComparison.Ordinal) &&
            transform.Contains("ViewportRenderPolicy.InitialPagePreviewRenderScale", StringComparison.Ordinal) &&
            transform.Contains("QueueLowZoomBitmapDowngradeIfNeeded()", StringComparison.Ordinal) &&
            transform.Contains("ShouldRequestLowZoomBitmapDowngrade()", StringComparison.Ordinal) &&
            transform.Contains("bool needsDetailRender", StringComparison.Ordinal) &&
            transform.Contains("_zoom < ViewportRenderPolicy.ZoomRefreshMinZoom", StringComparison.Ordinal) &&
            transform.Contains("ViewportRenderPolicy.ShouldUseZoomRefreshRender(_zoom, _bitmapScale)", StringComparison.Ordinal) &&
            transform.Contains("ViewportRenderPolicy.ShouldPreferDetailRenderOverFullRefresh(_zoom, _bitmapScale)", StringComparison.Ordinal) &&
            transform.Contains("QueueDetailRenderIfNeeded(force)", StringComparison.Ordinal) &&
            transform.Contains("QueueDetailRenderOverRasterSheetIfNeeded(force)", StringComparison.Ordinal) &&
            transform.Contains("private void QueueDetailRenderOverRasterSheetIfNeeded(bool force)", StringComparison.Ordinal) &&
            transform.Contains("ViewportRenderPolicy.ShouldSkipFullRefreshDuringDetail(_bitmapScale)", StringComparison.Ordinal) &&
            transform.Contains("QueueDetailRenderIfNeeded(force: false)", StringComparison.Ordinal),
            "zoom and pan idle should refresh blurry previews before scheduling detail renders for deep zoom");
        string endFastNavigation = SliceMethod(transform, "private void EndFastNavigation()");
        string requestRepaint = SliceMethod(viewport, "private void RequestRepaint(bool crossThreadRequest = false)");
        AssertTrue(
            endFastNavigation.Contains("QueueDetailRenderIfNeeded(force: false)", StringComparison.Ordinal) &&
            !endFastNavigation.Contains("immediate: true", StringComparison.Ordinal),
            "navigation idle should coalesce clipped detail renders instead of starting them during short pan/zoom bursts");
        AssertTrue(
            transform.Contains("private bool ShouldDeferFastNavigationEndForPointer()", StringComparison.Ordinal) &&
            transform.Contains("Mouse.MiddleButton == MouseButtonState.Pressed", StringComparison.Ordinal) &&
            transform.Contains("Mouse.RightButton == MouseButtonState.Pressed", StringComparison.Ordinal) &&
            transform.Contains("Mouse.LeftButton == MouseButtonState.Pressed", StringComparison.Ordinal) &&
            endFastNavigation.Contains("ShouldDeferFastNavigationEndForPointer()", StringComparison.Ordinal) &&
            endFastNavigation.IndexOf("ShouldDeferFastNavigationEndForPointer()", StringComparison.Ordinal) <
            endFastNavigation.IndexOf("_isFastNavigating = false", StringComparison.Ordinal),
            "high-zoom pan should keep the lighter navigation bitmap until the pan pointer is released");
        AssertTrue(
            requestRepaint.IndexOf("if (_repaintQueued)", StringComparison.Ordinal) <
            requestRepaint.IndexOf("PrepareBitmapForImmediateRepaint()", StringComparison.Ordinal),
            "repaint coalescing should skip immediate raster bitmap preparation when a frame is already queued");
        AssertTrue(
            pageApi.Contains("queueLayerAfter: false", StringComparison.Ordinal) &&
            pageApi.Contains("FireLayersChanged();", StringComparison.Ordinal) &&
            !pageApi.Contains("QueueLayerRender(", StringComparison.Ordinal) &&
            layers.Contains("QueueSharpLayerRenderAfterPreview(", StringComparison.Ordinal) &&
            pageApi.Contains("BeginPageSwitchDetailRenderHold()", StringComparison.Ordinal) &&
            detail.Contains("ShouldHoldDetailRender(force)", StringComparison.Ordinal) &&
            detail.Contains("QueueDetailRenderAfterHold()", StringComparison.Ordinal) &&
            detail.Contains("QueueDetailRenderStart(force || immediate)", StringComparison.Ordinal) &&
            detail.Contains("ViewportRenderPolicy.DetailRenderCoalesceDelayMs", StringComparison.Ordinal) &&
            detail.Contains("DetailRenderNavigationQuietDelay()", StringComparison.Ordinal) &&
            detail.Contains("ViewportRenderPolicy.DetailRenderNavigationQuietMs", StringComparison.Ordinal) &&
            detail.Contains("StartNextDetailRenderAsync()", StringComparison.Ordinal) &&
            detail.Contains("QueueDetailRenderIfNeeded(force: false, immediate: true)", StringComparison.Ordinal) &&
            detail.Contains("!force && _isFastNavigating", StringComparison.Ordinal) &&
            detail.Contains("CurrentViewStillMatchesDetailRequest", StringComparison.Ordinal) &&
            policy.Contains("PageSwitchDetailRenderDelayMs = 100", StringComparison.Ordinal) &&
            policy.Contains("DetailRenderNavigationQuietMs = 240", StringComparison.Ordinal) &&
            policy.Contains("FastPageSwitchPreviewCoalesceMs = 16", StringComparison.Ordinal) &&
            policy.Contains("PageSwitchSharpUpgradeDelayMs = 180", StringComparison.Ordinal) &&
            policy.Contains("PageSwitchSharpUpgradeIdleMs = 500", StringComparison.Ordinal) &&
            policy.Contains("PageSwitchSharpUpgradeMaxDeferrals = 5", StringComparison.Ordinal) &&
            policy.Contains("PageSwitchSharpUpgradeMinZoom = ZoomRefreshMinZoom", StringComparison.Ordinal) &&
            policy.Contains("ShouldSkipFullPageSharpUpgradeAtLowZoom", StringComparison.Ordinal) &&
            policy.Contains("targetBitmapScale > currentBitmapScale * 1.05f", StringComparison.Ordinal) &&
            layers.Contains("TryQueueCachedReadablePreviewUpgradeForLowZoom(pdfPath, pdfIndex, pageFolder)", StringComparison.Ordinal) &&
            layers.Contains("queueSharpBaseAfterPreview: false", StringComparison.Ordinal) &&
            layers.Contains("ShouldSkipQueuedFullPageSharpUpgradeAtLowZoom(scale)", StringComparison.Ordinal) &&
            layers.Contains("ShouldSkipQueuedFullPageSharpUpgradeAtLowZoom(renderScale)", StringComparison.Ordinal) &&
            layers.Contains("ShouldSkipQueuedFullPageSharpUpgradeAtLowZoom(request.RenderScale)", StringComparison.Ordinal) &&
            layers.Contains("private bool ShouldSkipQueuedNonFastPreviewForCurrentView(float renderScale)", StringComparison.Ordinal) &&
            layers.Contains("_usingRasterSheetRender || _usingRasterSheetOverviewRender", StringComparison.Ordinal) &&
            layers.Contains("_rasterSheetSource = rasterSheet?.Clone();", StringComparison.Ordinal) &&
            layers.Contains("QueueDetailRenderOverRasterSheetIfNeeded(force: false)", StringComparison.Ordinal) &&
            layers.Contains("_bitmapScale >= ViewportRenderPolicy.FastPageSwitchPreviewRenderScale * 0.95f", StringComparison.Ordinal) &&
            layers.Contains("ShouldSkipLowerQualityPreviewApply", StringComparison.Ordinal) &&
            layers.Contains("previewBitmapScale >= _bitmapScale * 0.95f", StringComparison.Ordinal) &&
            layers.Contains("!IsFastPreviewRenderScale(request.RenderScale)", StringComparison.Ordinal) &&
            layers.Contains("WaitForPreviewPrefetchQuietWindowAsync().ConfigureAwait(false)", StringComparison.Ordinal) &&
            layers.Contains("_bitmapScale <= 0", StringComparison.Ordinal) &&
            policy.Contains("ShouldPreferLowerScalePageBitmapForNavigation", StringComparison.Ordinal) &&
            policy.Contains("LowZoomBitmapDowngradeRatio", StringComparison.Ordinal),
            "cached preview page opens should keep PDF layers lazy while explicit layer paths retain delayed sharp/detail safeguards and use the zoom-refresh clarity threshold");
        AssertTrue(
            viewport.Contains("_navigationIdleTimer.Tick", StringComparison.Ordinal) &&
            viewport.Contains("ShouldDeferFastNavigationEndForPointer()", StringComparison.Ordinal) &&
            viewport.Contains("EndFastNavigation();", StringComparison.Ordinal),
            "navigation idle should run the real idle path after active pan pointers have been released");
        AssertTrue(
            transform.Contains("if (ViewportRenderPolicy.ShouldSkipFullRefreshDuringDetail(_bitmapScale))", StringComparison.Ordinal),
            "deep zoom should skip expensive full-sheet refresh once a normal 1x base bitmap exists");
        AssertTrue(
            rasterDpi.Contains("QueueDetailRenderOverRasterSheetIfNeeded(force: false)", StringComparison.Ordinal) &&
            rasterPrepared.Contains("QueueDetailRenderOverRasterSheetIfNeeded(force: false)", StringComparison.Ordinal),
            "raster DPI applies should still request clipped detail when the current zoom is sharper than the raster bitmap");
        AssertTrue(
            detail.Contains("private sealed record DetailRenderRequest", StringComparison.Ordinal) &&
            detail.Contains("_activeDetailRender", StringComparison.Ordinal) &&
            detail.Contains("DetailRequestCoversCurrentView(_activeDetailRender, request.RenderScale)", StringComparison.Ordinal) &&
            detail.Contains("DetailRequestCoversCurrentView(_pendingDetailRender, request.RenderScale)", StringComparison.Ordinal) &&
            detail.Contains("IsSameDetailRequest(_activeDetailRender, request)", StringComparison.Ordinal) &&
            detail.Contains("_detailRenderVersion + 1", StringComparison.Ordinal) &&
            detail.Contains("private sealed class DetailRenderTile", StringComparison.Ordinal) &&
            detail.Contains("MaxDetailRenderTileEntries = 32", StringComparison.Ordinal) &&
            detail.Contains("ResolveViewportRamBudget(160_000_000L, 512_000_000L, 0.025)", StringComparison.Ordinal) &&
            detail.Contains("TrimDetailRenderTiles", StringComparison.Ordinal) &&
            detail.Contains("request.ClipRect", StringComparison.Ordinal) &&
            detail.Contains("PdfLayerRenderService.TryRenderAsync", StringComparison.Ordinal) &&
            detail.Contains("PdfLayerRenderService.CancelDetailRenderWorker()", StringComparison.Ordinal) &&
            detail.Contains("DecodePdfLayerRenderBitmapWithMetrics(", StringComparison.Ordinal) &&
            detail.Contains("PdfLayerRenderService.CreateBitmapFromRawRender(render)", StringComparison.Ordinal) &&
            detail.Contains("ReportViewportRenderProfile", StringComparison.Ordinal) &&
            service.Contains("public static void CancelDetailRenderWorker()", StringComparison.Ordinal) &&
            detail.Contains("ViewportRenderPolicy.DetailRenderPaddingScreenPxForZoom(_zoom)", StringComparison.Ordinal) &&
            detail.Contains("BuildStableDetailRenderClip", StringComparison.Ordinal) &&
            detail.Contains("stableScale >= targetScale * 0.92f", StringComparison.Ordinal) &&
            detail.Contains("QueueAdjacentDetailRenderPrefetch", StringComparison.Ordinal) &&
            detail.Contains("request with { AllowDuringNavigationPrefetch = true }", StringComparison.Ordinal) &&
            detail.Contains("DetailTilePrefetchSemaphore", StringComparison.Ordinal) &&
            detailPrefetch.Contains("QueueAdjacentDetailRenderPrefetchFromTile", StringComparison.Ordinal) &&
            detailPrefetch.Contains("AllowDuringNavigationPrefetch: true", StringComparison.Ordinal) &&
            detailPrefetch.Contains("source.AllowDuringNavigationPrefetch", StringComparison.Ordinal) &&
            detailPrefetch.Contains("request.AllowDuringNavigationPrefetch", StringComparison.Ordinal) &&
            detailPrefetch.Contains("PrefetchDetailRenderTileAsync", StringComparison.Ordinal) &&
            detailPrefetch.Contains("TryRenderDedicatedProcessAsync", StringComparison.Ordinal) &&
            detailPrefetch.Contains("IsCurrentDetailPrefetchRequest", StringComparison.Ordinal) &&
            detailPrefetch.Contains("DetailRenderTileCoversRect", StringComparison.Ordinal) &&
            detailPrefetch.Contains("detail-prefetch", StringComparison.Ordinal) &&
            detail.Contains("DrawDetailRenderTileBitmap", StringComparison.Ordinal) &&
            detail.Contains("CurrentDetailTileFilterQuality", StringComparison.Ordinal) &&
            detail.Contains("Math.Abs(scaleRatio - 1f) <= 0.08f", StringComparison.Ordinal) &&
            detail.Contains("return SKFilterQuality.None", StringComparison.Ordinal) &&
            detail.Contains("IntersectionArea", StringComparison.Ordinal) &&
            detail.Contains("ViewportRenderPolicy.DetailRenderMaxPaintTiles", StringComparison.Ordinal) &&
            detail.Contains("ClearDetailRender()", StringComparison.Ordinal),
            "viewport should own versioned clipped detail render requests, cache multiple decoded tiles in RAM, prefetch adjacent work-zoom clips without cancelling already queued neighbors during pan, draw near-1:1 detail tiles without smoothing during pan, and decode them off the UI path");
        string startDetailRender = SliceMethod(detail, "private async Task StartNextDetailRenderAsync()");
        AssertTrue(
            startDetailRender.IndexOf("if (!IsCurrentDetailRequest(request))", StringComparison.Ordinal) <
            startDetailRender.IndexOf("ReportViewportRenderProfile(", StringComparison.Ordinal),
            "stale clipped detail renders should be discarded before they are recorded as useful performance samples");
        AssertTrue(
            layers.Contains("DecodePdfLayerRenderBitmapWithMetrics(", StringComparison.Ordinal) &&
            layers.Contains("ApplyLayerRenderResult(completion.Result, completion.Request.ResetLayerStates, decodedBitmap)", StringComparison.Ordinal),
            "layer render bitmap decode should be done before UI-thread bitmap application and support the raw detail payload");
        AssertTrue(
            layers.Contains("TryApplyLayerBitmapCache(request, out bool exactLayerCacheHit)", StringComparison.Ordinal) &&
            layers.Contains("allowMemoryBitmap && TryApplyLayerBitmapCache", StringComparison.Ordinal) &&
            layers.Contains("ShouldPreserveDetailDuringLayerRender(request)", StringComparison.Ordinal) &&
            layers.Contains("ShouldForceDetailAfterLayerApply", StringComparison.Ordinal) &&
            layers.Contains("request.HighlightedLayers.Count > 0", StringComparison.Ordinal) &&
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
            renderCache.Contains("1_792_000_000L", StringComparison.Ordinal),
            "decoded full-sheet PyMuPDF bitmaps should be reused from a bounded RAM cache before rerendering, including best-scale fallback, clean prefetch, and completed stale high-zoom renders");
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
            service.Contains("ResolvePrefetchWorkerPoolSize() => Math.Clamp(Environment.ProcessorCount / 3, 1, 4)", StringComparison.Ordinal) &&
            service.Contains("PrefetchPoolSlots", StringComparison.Ordinal) &&
            service.Contains("PrefetchFreeSlots", StringComparison.Ordinal) &&
            service.Contains("TryInvokePrefetchWorkerAsync", StringComparison.Ordinal) &&
            service.Contains("EnsurePrefetchSlot", StringComparison.Ordinal) &&
            service.Contains("ExchangeWithWorkerAsync", StringComparison.Ordinal),
            "detail-prefetch tiles should fan out across a machine-sized pool of persistent prefetch workers so deep-zoom tiles render in parallel instead of one at a time");
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
        string policy = ReadRepoFile("Models/ViewportRenderPolicy.cs");
        string main = ReadRepoFile("MainWindow.SheetOverlay.cs");
        string shell = ReadRepoFile("MainWindow.xaml.cs");

        AssertTrue(
            method.Contains("SKFilterQuality.None", StringComparison.Ordinal) &&
            policy.Contains("SheetOverlayLowZoomRenderScale = 1.0f", StringComparison.Ordinal) &&
            policy.Contains("SheetOverlayViewportRenderScale = 2.0f", StringComparison.Ordinal) &&
            policy.Contains("SelectSheetOverlayRenderScale", StringComparison.Ordinal) &&
            policy.Contains("SheetOverlayMaxRenderPixels", StringComparison.Ordinal),
            "sheet overlay underlay should use a cheap 1x overview render, keep a sharp 2x work-zoom render, scale up for zoom, and keep bitmap size bounded without smoothing blur");
        AssertFalse(
            method.Contains("SKFilterQuality.Low", StringComparison.Ordinal) ||
            method.Contains("SKFilterQuality.Medium", StringComparison.Ordinal) ||
            method.Contains("SKFilterQuality.High", StringComparison.Ordinal),
            "sheet overlay underlay should not switch to smoothing filters that blur linework or stall page navigation");
        AssertTrue(
            method.Contains("IsAntialias = false", StringComparison.Ordinal),
            "bitmap sheet overlays should avoid antialias softening");
        AssertTrue(
            source.Contains("public bool HasSheetOverlay => _sheetOverlayBitmap != null", StringComparison.Ordinal),
            "viewport smoke tests need to wait until async sheet overlays are actually applied before exercising pan and zoom");
        AssertTrue(
            source.Contains("SheetOverlayRenderScaleRefreshRequested", StringComparison.Ordinal) &&
            source.Contains("MaybeRequestSheetOverlayRenderScaleRefresh", StringComparison.Ordinal) &&
            source.Contains("_sheetOverlayBitmapScale", StringComparison.Ordinal),
            "sheet overlay should request a sharper cached/rendered bitmap when zoom outruns the current overlay source bitmap");
        AssertTrue(
            main.Contains("SelectSheetOverlayViewportRenderScale", StringComparison.Ordinal) &&
            main.Contains("SelectSheetOverlayPageOpenFirstFrameRenderScale", StringComparison.Ordinal) &&
            main.Contains("ReadSheetOverlaySourceSize", StringComparison.Ordinal) &&
            main.Contains("requestedRenderScale", StringComparison.Ordinal) &&
            main.Contains("fitAfter: !restoreView.HasValue", StringComparison.Ordinal) &&
            main.Contains("ResizeSheetOverlaySourceBitmap", StringComparison.Ordinal) &&
            main.Contains("sourceScale=", StringComparison.Ordinal) &&
            main.Contains("bitmapScale: renderScale", StringComparison.Ordinal) &&
            shell.Contains("SheetOverlayRenderScaleRefreshRequested += OnSheetOverlayRenderScaleRefreshRequested", StringComparison.Ordinal),
            "main window sheet overlay loading should use zoom-aware render scale selection and wire viewport refresh requests");
    }

    public static void SheetOverlayAdjustmentMenusAreExposed()
    {
        string menus = ReadRepoFile("MainWindow.SheetOverlay.Menus.cs");
        string callbacks = ReadRepoFile("MainWindow.ViewportCallbacks.cs");

        AssertTrue(
            menus.Contains("BuildSheetOverlayAdjustmentMenu", StringComparison.Ordinal) &&
            menus.Contains("Auto Select + Fit This Sheet", StringComparison.Ordinal) &&
            menus.Contains("Auto Select + Fit Sheet Overlay", StringComparison.Ordinal) &&
            menus.Contains("Auto Select + Replace Overlay", StringComparison.Ordinal) &&
            menus.Contains("Open Overlay Sheet", StringComparison.Ordinal) &&
            menus.Contains("Hide Overlay", StringComparison.Ordinal) &&
            menus.Contains("Show Overlay", StringComparison.Ordinal) &&
            menus.Contains("Clear Overlay", StringComparison.Ordinal) &&
            menus.Contains("Auto Fit", StringComparison.Ordinal) &&
            menus.Contains("Edit by Points", StringComparison.Ordinal) &&
            menus.Contains("Edit Transform...", StringComparison.Ordinal) &&
            menus.Contains("Move Left 6 pt", StringComparison.Ordinal) &&
            menus.Contains("Move Left 1 pt", StringComparison.Ordinal) &&
            menus.Contains("Scale Up 5%", StringComparison.Ordinal) &&
            menus.Contains("Scale Up 1%", StringComparison.Ordinal) &&
            menus.Contains("Rotate Left 1 deg", StringComparison.Ordinal) &&
            menus.Contains("Rotate Left 0.25 deg", StringComparison.Ordinal) &&
            menus.Contains("Reset Transform", StringComparison.Ordinal),
            "sheet overlay adjustment commands should share one discoverable submenu with coarse and fine transform steps instead of only living on the hidden overlay-node context menu");
        AssertTrue(
            menus.Contains("OpenSheetOverlaySource(page)", StringComparison.Ordinal) &&
            menus.Contains("TogglePageOverlayVisibility(page)", StringComparison.Ordinal) &&
            menus.Contains("ClearPageOverlay(page)", StringComparison.Ordinal),
            "sheet overlay adjustment menus should let the user jump to, hide/show, or clear the selected overlay without hunting for the overlay node");
        AssertTrue(
            menus.Contains("BuildSheetOverlayAdjustmentMenu(candidatePage", StringComparison.Ordinal) &&
            menus.Contains("BuildSheetOverlayAdjustmentMenu(node.Page", StringComparison.Ordinal),
            "page context menus and overlay-node context menus should both expose the same sheet overlay adjustment surface");
        AssertTrue(
            callbacks.Contains("AddCurrentSheetOverlayAdjustmentMenuItems(menu)", StringComparison.Ordinal),
            "viewport right-click should expose current sheet overlay setup or adjustment commands");
    }

    public static void SheetOverlayTransformShortcutsAreWired()
    {
        string input = ReadRepoFile(Path.Combine("Controls", "PdfViewport.Input.cs"));
        string overlay = ReadRepoFile(Path.Combine("Controls", "PdfViewport.SheetOverlay.cs"));

        AssertTrue(
            input.Contains("TryHandleSheetOverlayTransformShortcut(e)", StringComparison.Ordinal),
            "viewport keyboard handling should route current sheet overlay transform shortcuts before ordinary tool hotkeys");
        AssertTrue(
            overlay.Contains("ModifierKeys.Control", StringComparison.Ordinal) &&
            overlay.Contains("ModifierKeys.Alt", StringComparison.Ordinal) &&
            overlay.Contains("Key.Left", StringComparison.Ordinal) &&
            overlay.Contains("Key.Right", StringComparison.Ordinal) &&
            overlay.Contains("Key.Up", StringComparison.Ordinal) &&
            overlay.Contains("Key.Down", StringComparison.Ordinal) &&
            overlay.Contains("Key.OemPlus", StringComparison.Ordinal) &&
            overlay.Contains("Key.OemMinus", StringComparison.Ordinal) &&
            overlay.Contains("Key.OemOpenBrackets", StringComparison.Ordinal) &&
            overlay.Contains("Key.OemCloseBrackets", StringComparison.Ordinal) &&
            overlay.Contains("Key.NumPad0", StringComparison.Ordinal) &&
            overlay.Contains("SheetOverlayTransformChanged?.Invoke", StringComparison.Ordinal),
            "sheet overlay shortcuts should support nudge, scale, rotation, reset, and persist through the existing transform-changed event");
    }

    public static void SheetOverlayTransformDialogHasFineAdjustments()
    {
        string dialog = ReadRepoFile("MainWindow.SheetOverlay.TransformDialog.cs");

        AssertTrue(
            dialog.Contains("AddTransformAdjustmentButtons", StringComparison.Ordinal) &&
            dialog.Contains("X -1", StringComparison.Ordinal) &&
            dialog.Contains("X +1", StringComparison.Ordinal) &&
            dialog.Contains("Y -1", StringComparison.Ordinal) &&
            dialog.Contains("Y +1", StringComparison.Ordinal) &&
            dialog.Contains("Scale -1%", StringComparison.Ordinal) &&
            dialog.Contains("Scale +1%", StringComparison.Ordinal) &&
            dialog.Contains("Rot -0.25", StringComparison.Ordinal) &&
            dialog.Contains("Rot +0.25", StringComparison.Ordinal) &&
            dialog.Contains("Reset", StringComparison.Ordinal),
            "sheet overlay transform dialog should provide fine inline adjustments for post-auto-fit correction");
        AssertTrue(
            dialog.Contains("NormalizeSheetOverlayTransformScale(resultScale)", StringComparison.Ordinal) &&
            dialog.Contains("Math.Clamp(scale, 0.05, 20.0)", StringComparison.Ordinal),
            "sheet overlay transform dialog should normalize saved scale to the same usable range as viewport rendering");
    }

    public static void SheetOverlayMouseDragIsWired()
    {
        string input = ReadRepoFile(Path.Combine("Controls", "PdfViewport.Input.cs"));
        string overlay = ReadRepoFile(Path.Combine("Controls", "PdfViewport.SheetOverlay.cs"));
        string updateMethod = SliceMethod(overlay, "private bool TryUpdateSheetOverlayDrag(");
        string finishMethod = SliceMethod(overlay, "private bool FinishSheetOverlayDrag(");

        AssertTrue(
            input.Contains("TryBeginSheetOverlayDrag(pdf)", StringComparison.Ordinal) &&
            input.Contains("TryUpdateSheetOverlayDrag(ScreenToPdf", StringComparison.Ordinal) &&
            input.Contains("FinishSheetOverlayDrag()", StringComparison.Ordinal) &&
            input.Contains("CancelSheetOverlayDrag()", StringComparison.Ordinal),
            "viewport mouse and keyboard input should route Ctrl+Alt sheet overlay dragging through down, move, up, capture-loss, and Esc paths");
        AssertTrue(
            overlay.Contains("private bool _draggingSheetOverlay", StringComparison.Ordinal) &&
            overlay.Contains("IsSheetOverlayDragModifierActive", StringComparison.Ordinal) &&
            overlay.Contains("ModifierKeys.Control", StringComparison.Ordinal) &&
            overlay.Contains("ModifierKeys.Alt", StringComparison.Ordinal) &&
            overlay.Contains("IsPointInsideSheetOverlay", StringComparison.Ordinal) &&
            overlay.Contains("DrawSheetOverlayDragGuide", StringComparison.Ordinal),
            "sheet overlay drag should require explicit Ctrl+Alt modifiers, hit-test the overlay, and show a drag guide");
        AssertFalse(
            updateMethod.Contains("SheetOverlayTransformChanged?.Invoke", StringComparison.Ordinal) ||
            updateMethod.Contains("ApplySheetOverlayTransform(", StringComparison.Ordinal),
            "sheet overlay mouse move should preview offsets without persisting source.json on every pointer update");
        AssertTrue(
            finishMethod.Contains("ApplySheetOverlayTransform(", StringComparison.Ordinal) &&
            finishMethod.Contains("BuildSheetOverlayTransformStatus(", StringComparison.Ordinal),
            "sheet overlay mouse drag should persist the transform once when the button is released");
    }

    public static void SheetOverlayPointEditUsesPdfSnap()
    {
        string input = ReadRepoFile(Path.Combine("Controls", "PdfViewport.Input.cs"));
        string overlay = ReadRepoFile(Path.Combine("Controls", "PdfViewport.SheetOverlay.cs"));
        string pdfSnap = ReadRepoFile(Path.Combine("Controls", "PdfViewport.PdfSnap.cs"));
        string pointEdit = SliceMethod(overlay, "private bool HandleSheetOverlayPointEditClick(");

        AssertTrue(
            input.Contains("UpdateSheetOverlayPointEditPreview(pointerPdf)", StringComparison.Ordinal) &&
            input.Contains("ClearSheetOverlayPointEditSnapPreview();", StringComparison.Ordinal) &&
            pointEdit.Contains("ResolveSheetOverlaySourceLocalPoint(pdf", StringComparison.Ordinal) &&
            pointEdit.Contains("ResolveSheetOverlayTargetPoint(pdf", StringComparison.Ordinal) &&
            pointEdit.Contains("SKPoint scaleTarget = ResolveSheetOverlayTargetPoint(pdf", StringComparison.Ordinal),
            "sheet overlay point edit should update live snap preview and resolve overlay source clicks and base target clicks through separate snap paths");
        AssertTrue(
            overlay.Contains("private SKPoint? _sheetOverlayPointEditSnapPreview", StringComparison.Ordinal) &&
            overlay.Contains("private void UpdateSheetOverlayPointEditPreview(SKPoint pdf)", StringComparison.Ordinal) &&
            overlay.Contains("TryFindOverlayPdfSnapPoint(pdf, SheetOverlayPointEditSnapTolerancePt()", StringComparison.Ordinal) &&
            overlay.Contains("TryFindBasePdfSnapPoint(pdf, SheetOverlayPointEditSnapTolerancePt()", StringComparison.Ordinal) &&
            overlay.Contains("BuildSheetOverlayPointEditSnapStatus", StringComparison.Ordinal),
            "sheet overlay point edit should snap source points to overlay geometry, target points to sheet geometry, and report the snap kind");
        AssertTrue(
            overlay.Contains("DrawSheetOverlayPointEditSnapPreview(canvas, snapPaint, radius)", StringComparison.Ordinal) &&
            overlay.Contains("SheetOverlayPointEditGuidePointer()", StringComparison.Ordinal) &&
            overlay.Contains("ClearSheetOverlayPointEditSnapPreview()", StringComparison.Ordinal),
            "sheet overlay point edit should draw a live snap target and clear stale preview state");
        AssertTrue(
            pdfSnap.Contains("private bool TryFindBasePdfSnapPoint(", StringComparison.Ordinal) &&
            pdfSnap.Contains("TryFindBasePdfSnapPoint(rawPdf, tolerancePt", StringComparison.Ordinal) &&
            pdfSnap.Contains("!PdfSnapEnabled ||", StringComparison.Ordinal),
            "PDF snap should expose a base-sheet-only helper so overlay point editing cannot snap target clicks back onto the overlay");
    }

    public static void SheetOverlayAsyncLoadUsesFreshPageSnapshot()
    {
        string main = ReadRepoFile("MainWindow.SheetOverlay.cs");
        string queueMethod = SliceMethod(main, "private void QueueSheetOverlayLoadForPageOpen(");
        string asyncMethod = SliceMethod(main, "private async Task LoadSheetOverlayAsync(");

        AssertTrue(
            queueMethod.Contains("TxtStatus.Text = \"Sheet overlay loading...\";", StringComparison.Ordinal),
            "page-open overlay queue should surface a loading state when no cached overlay bitmap is ready yet");
        AssertTrue(
            asyncMethod.Contains("PageInfo? latest = OurPlaneCoreJobStore.TryReadPage(page.FolderPath);", StringComparison.Ordinal) &&
            asyncMethod.Contains("string.IsNullOrWhiteSpace(latest.OverlayPageFolder)", StringComparison.Ordinal) &&
            asyncMethod.Contains("!latest.OverlayVisible", StringComparison.Ordinal) &&
            asyncMethod.Contains("!SameFolder(latest.OverlayPageFolder, page.OverlayPageFolder)", StringComparison.Ordinal) &&
            asyncMethod.Contains("LoadSheetOverlay(latest)", StringComparison.Ordinal) &&
            asyncMethod.Contains("ApplySheetOverlayBitmapToViewport(", StringComparison.Ordinal) &&
            asyncMethod.Contains("latest,", StringComparison.Ordinal),
            "async sheet overlay render should re-read source.json before applying a bitmap so page switches or overlay edits cannot apply stale overlay state");
    }

    public static void SheetOverlayReciprocalCleanupIsWired()
    {
        string main = ReadRepoFile("MainWindow.SheetOverlay.cs");
        string autoFit = ReadRepoFile("MainWindow.SheetOverlay.AutoFit.cs");
        string autoSelect = ReadRepoFile("MainWindow.SheetOverlay.AutoSelect.cs");
        string service = ReadRepoFile(Path.Combine("Models", "SheetOverlayReciprocalService.cs"));

        AssertTrue(
            main.Contains("ClearReciprocalSheetOverlay(ReadLatestSheetOverlayPage(_currentPage))", StringComparison.Ordinal) &&
            main.Contains("ClearReciprocalSheetOverlay(ReadLatestSheetOverlayPage(latest))", StringComparison.Ordinal) &&
            main.Contains("ClearReciprocalSheetOverlay(updated)", StringComparison.Ordinal) &&
            main.Contains("SheetOverlayReciprocalService.TryClear", StringComparison.Ordinal) &&
            !main.Contains("SyncReciprocalSheetOverlay(", StringComparison.Ordinal) &&
            !main.Contains("SheetOverlayReciprocalService.TrySync", StringComparison.Ordinal),
            "setting, clearing, menu transforms, and viewport point-edit transforms should clear stale reciprocal overlays without writing the overlay onto the source sheet");
        AssertTrue(
            autoFit.Contains("ClearReciprocalSheetOverlay(updatedTarget)", StringComparison.Ordinal) &&
            autoSelect.Contains("ClearReciprocalSheetOverlay(selectedTarget)", StringComparison.Ordinal),
            "overlay Auto Fit and Auto Select should leave the chosen overlay only on the active target sheet");
        AssertTrue(
            service.Contains("public static bool TrySync", StringComparison.Ordinal) &&
            service.Contains("public static bool TryClear", StringComparison.Ordinal) &&
            service.Contains("ShouldWriteReciprocal", StringComparison.Ordinal) &&
            service.Contains("string.IsNullOrWhiteSpace(reciprocalPage.OverlayPageFolder)", StringComparison.Ordinal) &&
            service.Contains("SameFolder(reciprocalPage.OverlayPageFolder, basePageFolder)", StringComparison.Ordinal),
            "reciprocal service should retain the legacy repair path while UI code stops writing new reciprocal overlay links");
    }

    public static void SheetOverlayAutoFitCanAutoSelectOverlay()
    {
        string autoFit = ReadRepoFile("MainWindow.SheetOverlay.AutoFit.cs");
        string autoSelect = ReadRepoFile("MainWindow.SheetOverlay.AutoSelect.cs");
        string main = ReadRepoFile("MainWindow.SheetOverlay.cs");
        string menus = ReadRepoFile("MainWindow.SheetOverlay.Menus.cs");
        string service = ReadRepoFile(Path.Combine("Models", "SheetOverlayAutoFitCandidateSearchService.cs"));
        string candidateDialog = ReadRepoFile(Path.Combine("Dialogs", "SheetOverlayCandidateDialog.cs"));

        AssertTrue(
            autoFit.Contains("FindSheetOverlayAutoFitCandidate(job, targetPage)", StringComparison.Ordinal) &&
            autoFit.Contains("Overlay auto fit: searching job sheets for matching plan geometry", StringComparison.Ordinal) &&
            autoFit.Contains("ApplySheetOverlayAutoSelectedFit(targetPage, search)", StringComparison.Ordinal),
            "sheet overlay Auto Fit should invoke auto-select when no overlay is already configured");
        AssertTrue(
            autoSelect.Contains("OurPlaneCoreJobStore.SavePageOverlay(", StringComparison.Ordinal) &&
            autoSelect.Contains("OurPlaneCoreJobStore.SavePageOverlayVisibility(latestTarget.FolderPath, true)", StringComparison.Ordinal) &&
            autoSelect.Contains("Auto-selected overlay", StringComparison.Ordinal) &&
            autoSelect.Contains("MaxSheetOverlayAutoSelectCandidates = 160", StringComparison.Ordinal) &&
            autoSelect.Contains("Take(MaxSheetOverlayAutoSelectCandidates)", StringComparison.Ordinal) &&
            autoSelect.Contains("BuildSheetOverlayAutoFitSearchRank", StringComparison.Ordinal) &&
            autoSelect.Contains("AutoSelectAndFitSheetOverlay(", StringComparison.Ordinal) &&
            autoSelect.Contains("bool replaceExistingOverlay", StringComparison.Ordinal) &&
            autoSelect.Contains("bool skipCurrentOverlay = false", StringComparison.Ordinal) &&
            autoSelect.Contains("Overlay auto select: trying the next matching sheet", StringComparison.Ordinal) &&
            autoSelect.Contains("skipCurrentOverlay ? targetPage.OverlayPageFolder : \"\"", StringComparison.Ordinal) &&
            autoSelect.Contains("nextAfterOverlayFolder", StringComparison.Ordinal) &&
            autoSelect.Contains("TrySelectNextMatch(topMatches, nextAfterOverlayFolder, out match)", StringComparison.Ordinal) &&
            autoSelect.Contains("no alternate similar sheet matched", StringComparison.Ordinal) &&
            autoSelect.Contains("Next overlay candidate", StringComparison.Ordinal) &&
            autoSelect.Contains("Overlay auto select: reselecting the best matching sheet", StringComparison.Ordinal) &&
            autoSelect.Contains("ClearReciprocalSheetOverlay(latestTarget)", StringComparison.Ordinal) &&
            autoSelect.Contains("sheets compared", StringComparison.Ordinal) &&
            autoSelect.Contains("candidates={search.ComparableCount}/{search.CandidateCount}", StringComparison.Ordinal) &&
            autoSelect.Contains("method='{search.Fit.Method}'", StringComparison.Ordinal) &&
            autoSelect.Contains("BuildSheetOverlayAutoSelectAlternativesSummary", StringComparison.Ordinal) &&
            autoSelect.Contains("HasCloseSheetOverlayAutoSelectAlternative", StringComparison.Ordinal) &&
            autoSelect.Contains("close alternative needs review", StringComparison.Ordinal) &&
            autoSelect.Contains("top matches:", StringComparison.Ordinal) &&
            autoSelect.Contains("alternatives='{alternatives}'", StringComparison.Ordinal) &&
            autoSelect.Contains("ChooseSheetOverlayAutoSelectCandidate", StringComparison.Ordinal) &&
            autoSelect.Contains("includeReviewCandidates: true", StringComparison.Ordinal) &&
            autoSelect.Contains("TryRankReviewCandidates", StringComparison.Ordinal) &&
            autoSelect.Contains("no reviewable similar sheet matched", StringComparison.Ordinal) &&
            autoSelect.Contains("SheetOverlayCandidateDialog", StringComparison.Ordinal) &&
            autoSelect.Contains("TrySelectSheetOverlayAutoFitCandidateSearch", StringComparison.Ordinal) &&
            autoSelect.Contains("CandidateReads", StringComparison.Ordinal) &&
            autoSelect.Contains("HandleSheetOverlayCandidatePostAction", StringComparison.Ordinal) &&
            autoSelect.Contains("SheetOverlayCandidateAction.OpenTargetSheet", StringComparison.Ordinal) &&
            autoSelect.Contains("OpenSheetOverlayTarget(latestTarget)", StringComparison.Ordinal) &&
            autoSelect.Contains("Opened target sheet with overlay", StringComparison.Ordinal) &&
            autoSelect.Contains("SheetOverlayCandidateAction.OpenOverlaySheet", StringComparison.Ordinal) &&
            autoSelect.Contains("SheetOverlayCandidateAction.EditTransform", StringComparison.Ordinal) &&
            autoSelect.Contains("EditSheetOverlayTransform(latestTarget)", StringComparison.Ordinal) &&
            autoSelect.Contains("SheetOverlayCandidateAction.EditByPoints", StringComparison.Ordinal),
            "sheet overlay Auto Fit should auto-select a matching sheet, report match quality, allow choosing a ranked candidate without rerunning the search, and support immediate review actions");
        AssertTrue(
            service.Contains("SheetOverlayAutoFitCandidateSearchService", StringComparison.Ordinal) &&
            service.Contains("MinimumAutoSelectConfidence", StringComparison.Ordinal) &&
            service.Contains("MinimumAutoSelectMatchedSamples", StringComparison.Ordinal) &&
            service.Contains("MinimumReviewConfidence", StringComparison.Ordinal) &&
            service.Contains("MinimumReviewMatchedSamples", StringComparison.Ordinal) &&
            service.Contains("TryRankReviewCandidates", StringComparison.Ordinal) &&
            service.Contains("IsAutoSelectable", StringComparison.Ordinal) &&
            service.Contains("SearchRank", StringComparison.Ordinal) &&
            service.Contains("out IReadOnlyList<SheetOverlayAutoFitCandidateMatch> topMatches", StringComparison.Ordinal) &&
            service.Contains("TrySelectNextMatch", StringComparison.Ordinal),
            "overlay auto-select should rank candidates by verified geometry quality with deterministic sheet-order tie breaking, expose alternatives, allow review-only candidates in the chooser, and cycle through ranked matches");
        AssertTrue(
            menus.Contains("\"Auto Select + Fit This Sheet\"", StringComparison.Ordinal) &&
            menus.Contains("\"Auto Select + Fit Sheet Overlay\"", StringComparison.Ordinal) &&
            menus.Contains("\"Auto Select + Replace Overlay\"", StringComparison.Ordinal) &&
            menus.Contains("\"Auto Select + Choose Candidate...\"", StringComparison.Ordinal) &&
            menus.Contains("\"Auto Select + Next Candidate\"", StringComparison.Ordinal) &&
            menus.Contains("ChooseSheetOverlayAutoSelectCandidate(candidatePage)", StringComparison.Ordinal) &&
            menus.Contains("ChooseSheetOverlayAutoSelectCandidate(currentPage)", StringComparison.Ordinal) &&
            menus.Contains("ChooseSheetOverlayAutoSelectCandidate(page)", StringComparison.Ordinal) &&
            menus.Contains("AutoSelectAndFitSheetOverlay(candidatePage, replaceExistingOverlay: true)", StringComparison.Ordinal) &&
            menus.Contains("AutoSelectAndFitSheetOverlay(currentPage, replaceExistingOverlay: false)", StringComparison.Ordinal) &&
            menus.Contains("AutoSelectAndFitSheetOverlay(page, replaceExistingOverlay: true)", StringComparison.Ordinal) &&
            menus.Contains("AutoSelectAndFitSheetOverlay(page, replaceExistingOverlay: true, skipCurrentOverlay: true)", StringComparison.Ordinal),
            "auto-selected overlay fitting must be reachable from page and viewport menus, replace a wrong overlay, choose a ranked match directly, and cycle to the next ranked candidate");
        AssertTrue(
            candidateDialog.Contains("public sealed class SheetOverlayCandidateDialog : Window", StringComparison.Ordinal) &&
            candidateDialog.Contains("DataGrid", StringComparison.Ordinal) &&
            candidateDialog.Contains("Confidence", StringComparison.Ordinal) &&
            candidateDialog.Contains("nameof(SheetOverlayCandidateRow.Review)", StringComparison.Ordinal) &&
            candidateDialog.Contains("BuildReviewLabel", StringComparison.Ordinal) &&
            candidateDialog.Contains("\"Close\"", StringComparison.Ordinal) &&
            candidateDialog.Contains("\"Review\"", StringComparison.Ordinal) &&
            candidateDialog.Contains("match.IsAutoSelectable", StringComparison.Ordinal) &&
            candidateDialog.Contains("nameof(SheetOverlayCandidateRow.Transform)", StringComparison.Ordinal) &&
            candidateDialog.Contains("BuildTransformSummary", StringComparison.Ordinal) &&
            candidateDialog.Contains("Use Selected", StringComparison.Ordinal) &&
            candidateDialog.Contains("Use + Review Overlay", StringComparison.Ordinal) &&
            candidateDialog.Contains("SheetOverlayCandidateAction.OpenTargetSheet", StringComparison.Ordinal) &&
            candidateDialog.Contains("Use + Open Source", StringComparison.Ordinal) &&
            candidateDialog.Contains("Use + Edit Transform", StringComparison.Ordinal) &&
            candidateDialog.Contains("SheetOverlayCandidateAction.EditTransform", StringComparison.Ordinal) &&
            candidateDialog.Contains("Use + Edit by Points", StringComparison.Ordinal) &&
            candidateDialog.Contains("SelectedAction", StringComparison.Ordinal) &&
            candidateDialog.Contains("InitialSelectedIndex", StringComparison.Ordinal) &&
            candidateDialog.Contains("_grid.ScrollIntoView(_grid.SelectedItem)", StringComparison.Ordinal) &&
            candidateDialog.Contains("MouseDoubleClick", StringComparison.Ordinal),
            "ranked overlay candidates should be reviewable in a choose dialog with confidence, transform values, current-overlay focus, quick selection, and direct inspection/edit actions");
        AssertTrue(
            main.Contains("private void OpenSheetOverlaySource(PageInfo page)", StringComparison.Ordinal) &&
            main.Contains("OpenPageInActiveTab(overlayPage)", StringComparison.Ordinal) &&
            main.Contains("Opened overlay sheet: {overlayPage.Name}", StringComparison.Ordinal) &&
            main.Contains("BeginSheetOverlayPointEditWhenReady", StringComparison.Ordinal) &&
            main.Contains("_viewport.HasSheetOverlay", StringComparison.Ordinal),
            "auto-selected overlays should be easy to inspect by jumping directly to the matched overlay sheet or entering point edit once the overlay is ready");
    }

    public static void SheetOverlayAutoFitRasterFallbackIsWired()
    {
        string autoFit = ReadRepoFile("MainWindow.SheetOverlay.AutoFit.cs");
        string feature = ReadRepoFile(Path.Combine("Models", "SheetOverlayRasterFeatureService.cs"));

        AssertTrue(
            autoFit.Contains("ReadSheetOverlayAutoFitRasterSnap(basePage)", StringComparison.Ordinal) &&
            autoFit.Contains("ReadSheetOverlayAutoFitRasterSnap(overlayPage)", StringComparison.Ordinal) &&
            autoFit.Contains("ReadSheetOverlayAutoFitRasterGeometry(targetPage, overlayPage)", StringComparison.Ordinal) &&
            autoFit.Contains("Raster fallback also failed", StringComparison.Ordinal) &&
            autoFit.Contains("RasterSheetCacheService.TryReadReady", StringComparison.Ordinal) &&
            autoFit.Contains("TryRenderSheetOverlayAutoFitRaster", StringComparison.Ordinal) &&
            autoFit.Contains("SheetOverlayRasterFeatureService.TryExtractSnap", StringComparison.Ordinal),
            "sheet overlay Auto Fit should fall back to raster-image line features when PDF vector geometry is unavailable or fails to match");
        AssertTrue(
            autoFit.Contains("BuildSheetOverlayAutoFitStatus", StringComparison.Ordinal) &&
            autoFit.Contains("source='{read.SourceSummary}'", StringComparison.Ordinal) &&
            autoFit.Contains("Overlay auto fit (raster image, {fit.Method})", StringComparison.Ordinal) &&
            autoFit.Contains("Overlay auto fit ({read.SourceSummary}, {fit.Method})", StringComparison.Ordinal),
            "sheet overlay Auto Fit should report whether the accepted match came from PDF geometry or raster-image fallback and which match method was used");
        AssertTrue(
            feature.Contains("BuildInkMap", StringComparison.Ordinal) &&
            feature.Contains("ExtractHorizontalSegments", StringComparison.Ordinal) &&
            feature.Contains("ExtractVerticalSegments", StringComparison.Ordinal) &&
            feature.Contains("MaxFeaturePixels", StringComparison.Ordinal),
            "raster fallback should detect long plan line features with bounded bitmap work");
    }

    public static void ViewportStressSmokeCanExerciseHighZoomPan()
    {
        string source = ReadRepoFile("MainWindow.ViewportPageStressSmoke.cs");
        string treeOps = ReadRepoFile("MainWindow.ViewportTreeOpsSmoke.cs");
        string script = ReadRepoFile(Path.Combine("Tools", "ui_viewport_page_stress_smoke.ps1"));
        string compareScript = ReadRepoFile(Path.Combine("Tools", "compare_viewport_smoke_reports.ps1"));
        string recorder = ReadRepoFile(Path.Combine("Models", "ViewportPerformanceRecorder.cs"));
        string scheduler = ReadRepoFile(Path.Combine("Models", "ViewportRenderScheduler.cs"));
        string detail = ReadRepoFile(Path.Combine("Controls", "PdfViewport.DetailRender.cs"));
        string rendering = ReadRepoFile(Path.Combine("Controls", "PdfViewport.Rendering.cs"));
        string recovery = ReadRepoFile("MainWindow.JobRecovery.cs");

        AssertTrue(
            source.Contains("OURPLANECORE_VIEWPORT_PAGE_STRESS_TARGET_ZOOM", StringComparison.Ordinal) &&
            source.Contains("OURPLANECORE_VIEWPORT_PAGE_STRESS_PAN_STEPS", StringComparison.Ordinal) &&
            source.Contains("OURPLANECORE_VIEWPORT_PAGE_STRESS_OPEN_COUNT", StringComparison.Ordinal) &&
            source.Contains("ReadEnvironmentFloat", StringComparison.Ordinal) &&
            source.Contains("RestoreViewState(new PdfViewport.ViewState(targetZoom", StringComparison.Ordinal) &&
            source.Contains("ZoomExerciseMs", StringComparison.Ordinal) &&
            source.Contains("WaitForViewportSheetOverlayAsync", StringComparison.Ordinal) &&
            source.Contains("Directory.Exists(page.OverlayPageFolder)", StringComparison.Ordinal) &&
            source.Contains("OverlayReadyMs", StringComparison.Ordinal) &&
            source.Contains("IsPageDetailRenderReady", StringComparison.Ordinal) &&
            source.Contains("WaitForViewportDetailRenderAsync", StringComparison.Ordinal) &&
            source.Contains("ZoomDetailReadyMs", StringComparison.Ordinal) &&
            source.Contains("detail-ready", StringComparison.Ordinal) &&
            source.Contains("PostZoomRenderReadyMs", StringComparison.Ordinal) &&
            source.Contains("VisualProbeMs", StringComparison.Ordinal) &&
            script.Contains("max zoom detail", StringComparison.Ordinal) &&
            script.Contains("[switch]$UseVerifyBuild", StringComparison.Ordinal) &&
            script.Contains("if ($UseVerifyBuild -and (Test-Path", StringComparison.Ordinal) &&
            script.Contains("overlay checks", StringComparison.Ordinal),
            "viewport stress smoke must support hidden sampled page opens plus absolute zoom, pan, detail sharpness, sheet overlay waits, and phase timing checks for 350% regressions");
        AssertTrue(
            source.Contains("OURPLANECORE_VIEWPORT_PAGE_STRESS_TREE_OPS", StringComparison.Ordinal) &&
            source.Contains("RunViewportTreeOpsSmoke(report)", StringComparison.Ordinal) &&
            treeOps.Contains("MovePagesDownAndRestore", StringComparison.Ordinal) &&
            treeOps.Contains("MoveTakeoffsDownAndRestore", StringComparison.Ordinal) &&
            treeOps.Contains("DragTakeoffPositionDownAndRestore", StringComparison.Ordinal) &&
            treeOps.Contains("MoveTakeoffSectionAndRestoreWithPageJump", StringComparison.Ordinal) &&
            treeOps.Contains("AssertCurrentPageIsMeasurementPage", StringComparison.Ordinal) &&
            treeOps.Contains("AssertCurrentPageUnchangedForTakeoffMove", StringComparison.Ordinal) &&
            treeOps.Contains("SelectPagesBulkForSmoke", StringComparison.Ordinal) &&
            treeOps.Contains("SelectTakeoffsBulkForSmoke", StringComparison.Ordinal) &&
            treeOps.Contains("PagesSingleSelectionSetMs", StringComparison.Ordinal) &&
            treeOps.Contains("TakeoffsSingleSelectionEventMs", StringComparison.Ordinal) &&
            treeOps.Contains("TakeoffsSingleDragMoveDownMs", StringComparison.Ordinal) &&
            treeOps.Contains("TakeoffsSectionDropPageJumpMs", StringComparison.Ordinal) &&
            treeOps.Contains("TakeoffsBulkSelectionPagesLayoutMs", StringComparison.Ordinal) &&
            treeOps.Contains("OrdersEqual(before, OrderedChildSnapshot(parent))", StringComparison.Ordinal) &&
            script.Contains("[switch]$IncludeTreeOps", StringComparison.Ordinal) &&
            script.Contains("OURPLANECORE_SETTINGS_PATH", StringComparison.Ordinal) &&
            script.Contains("tree ops takeoff drag/drop", StringComparison.Ordinal) &&
            script.Contains("jumped to measurement page", StringComparison.Ordinal) &&
            script.Contains("tree ops takeoffs detail", StringComparison.Ordinal),
            "viewport stress smoke should optionally exercise reversible single/bulk selection and move operations in Pages and Takeoffs trees, including section/count row drops that jump to the measurement page");
        AssertTrue(
            source.Contains("ViewportPerformanceRecorder.BeginRun", StringComparison.Ordinal) &&
            source.Contains("ViewportPerformanceRecorder.EndRun", StringComparison.Ordinal) &&
            source.Contains("AI_Context", StringComparison.Ordinal) &&
            source.Contains("perf_runs", StringComparison.Ordinal),
            "viewport stress smoke must write a default perf report under the job AI_Context");
        AssertTrue(
            recorder.Contains("RecordRenderProfile", StringComparison.Ordinal) &&
            recorder.Contains("RecordSlowFrame", StringComparison.Ordinal) &&
            recorder.Contains("RecordRepaintRequest", StringComparison.Ordinal) &&
            recorder.Contains("RecordRenderQueue", StringComparison.Ordinal) &&
            recorder.Contains("RecordBitmapDecode", StringComparison.Ordinal) &&
            recorder.Contains("CacheHitRate", StringComparison.Ordinal) &&
            recorder.Contains("MaxPageBitmapPaintMs", StringComparison.Ordinal) &&
            recorder.Contains("RepaintCoalesceRate", StringComparison.Ordinal) &&
            recorder.Contains("RenderQueueReplacementRate", StringComparison.Ordinal) &&
            recorder.Contains("MaxBitmapDecodeMs", StringComparison.Ordinal),
            "viewport perf recorder must capture render/cache, repaint, queue, decode, and slow paint metrics");
        AssertTrue(
            compareScript.Contains("BaselinePath", StringComparison.Ordinal) &&
            compareScript.Contains("CurrentPath", StringComparison.Ordinal) &&
            compareScript.Contains("FailOnRegression", StringComparison.Ordinal) &&
            compareScript.Contains("MaxStepMs", StringComparison.Ordinal) &&
            compareScript.Contains("MaxReadyMs", StringComparison.Ordinal) &&
            compareScript.Contains("MaxZoomMs", StringComparison.Ordinal) &&
            compareScript.Contains("CacheHitRate", StringComparison.Ordinal) &&
            compareScript.Contains("RepaintRequestCount", StringComparison.Ordinal) &&
            compareScript.Contains("RenderQueueReplacementCount", StringComparison.Ordinal) &&
            compareScript.Contains("MaxBitmapDecodeMs", StringComparison.Ordinal) &&
            compareScript.Contains("WorkingSetMb", StringComparison.Ordinal) &&
            compareScript.Contains("Regressions:", StringComparison.Ordinal),
            "viewport smoke reports should have a local comparison dashboard that can fail on timing, cache, paint, and memory regressions");
        AssertTrue(
            scheduler.Contains("public sealed class ViewportRenderScheduler", StringComparison.Ordinal) &&
            scheduler.Contains("ViewportRenderPriority", StringComparison.Ordinal) &&
            scheduler.Contains("TryDequeue", StringComparison.Ordinal) &&
            scheduler.Contains("replaceSamePageAndKind", StringComparison.Ordinal),
            "viewport render scheduling should have a small standalone skeleton ready for consolidating pending render queues");
        AssertTrue(
            detail.Contains("ViewportPerformanceRecorder.RecordRenderProfile", StringComparison.Ordinal) &&
            detail.Contains("DecodePdfLayerRenderBitmapWithMetrics", StringComparison.Ordinal) &&
            rendering.Contains("ViewportPerformanceRecorder.RecordSlowFrame", StringComparison.Ordinal),
            "viewport render, decode, and paint paths must feed the perf recorder");
        AssertTrue(
            recovery.Contains("ShouldSuppressAutomatedRecoveryPrompt", StringComparison.Ordinal) &&
            recovery.Contains("ViewportPageStressSmokeEnv", StringComparison.Ordinal) &&
            recovery.Contains("Skipping stale recovery prompt during automation", StringComparison.Ordinal),
            "hidden viewport stress smoke must not block on the stale recovery MessageBox before opening the first sheet");
    }

    public static void PagesTreeSelectedSheetScaleMenuIsWired()
    {
        string commands = ReadRepoFile("MainWindow.PagesCommands.cs");
        string scale = ReadRepoFile("MainWindow.PagesScale.cs");
        string callbacks = ReadRepoFile("MainWindow.ViewportCallbacks.cs");
        string pageSetup = ReadRepoFile("MainWindow.PageSetup.cs");
        string pageSetupWindow = ReadRepoFile("Dialogs/PageSetupWindow.cs");
        string setPage = SliceMethod(pageSetupWindow, "public void SetPage(");
        string selectPageNameText = SliceMethod(pageSetupWindow, "private void SelectPageNameText(");
        string selectScaleText = SliceMethod(pageSetupWindow, "private void SelectScaleText(");
        string userAlreadyPlacedTextFocus = SliceMethod(pageSetupWindow, "private bool UserAlreadyPlacedTextFocus(");

        AssertTrue(
            commands.Contains("SetSelectedPagesScaleFromContext(item)", StringComparison.Ordinal) &&
            commands.Contains("ApplyCurrentScaleToSelectedPagesFromContext(item)", StringComparison.Ordinal) &&
            commands.Contains("Set Scale for {selectedPageCount} Selected", StringComparison.Ordinal),
            "page context menu must expose Set Scale and Apply Current Sheet Scale for single and multi-selected sheets");
        AssertTrue(
            scale.Contains("SelectedPagesFromPagesTree(anchor)", StringComparison.Ordinal) &&
            scale.Contains("CurrentPageScaleMetersPerPt", StringComparison.Ordinal) &&
            scale.Contains("PdfSheetMetadataService.TryParseScaleMetersPerPt", StringComparison.Ordinal) &&
            scale.Contains("OurPlaneCoreJobStore.SavePageScale", StringComparison.Ordinal) &&
            scale.Contains("WriteFloatingPageSetupMetadata", StringComparison.Ordinal) &&
            scale.Contains("ApplyScaleToPageMeasurements", StringComparison.Ordinal) &&
            scale.Contains("FlushTakeoffAutosaves", StringComparison.Ordinal),
            "scale menu must parse, persist metadata, update measurements, and flush changed takeoffs");
        AssertTrue(
            callbacks.Contains("private IReadOnlyList<TakeoffItem> ApplyScaleToPageMeasurements", StringComparison.Ordinal),
            "page-scale updates should reuse a page-scoped measurement scale helper");
        AssertTrue(
            pageSetup.Contains("page.FolderPath", StringComparison.Ordinal) &&
            setPage.Contains("bool preservePageNameEdit = samePage && _pageNameBox.IsKeyboardFocusWithin;", StringComparison.Ordinal) &&
            setPage.Contains("bool preserveScaleEdit = samePage && _scaleBox.IsKeyboardFocusWithin;", StringComparison.Ordinal) &&
            setPage.Contains("if (!preservePageNameEdit)") &&
            setPage.Contains("if (!preserveScaleEdit)") &&
            setPage.Contains("bool selectName = false", StringComparison.Ordinal) &&
            setPage.Contains("if (selectName && IsVisible && !preservePageNameEdit && !preserveScaleEdit)", StringComparison.Ordinal) &&
            pageSetup.Contains("RefreshFloatingPageSetup(appliedPage?.FolderPath, selectName: false)", StringComparison.Ordinal) &&
            pageSetup.Contains("RefreshFloatingPageSetup(pages[targetIndex].FolderPath, selectName: true)", StringComparison.Ordinal) &&
            pageSetup.Contains("PageSetupScaleDisplayText(page)", StringComparison.Ordinal) &&
            pageSetup.Contains("PageSetupScaleStatusText(scaleText, scaleMetersPerPt)", StringComparison.Ordinal) &&
            pageSetup.Contains("manualScaleText = \"\"", StringComparison.Ordinal) &&
            pageSetup.Contains("manualScaleText.Trim()", StringComparison.Ordinal) &&
            pageSetupWindow.Contains("private bool IsSamePage(string pageFolder, int pageIndex)", StringComparison.Ordinal),
            "floating Page Setup refreshes should not overwrite or reselect the active name/scale edit for the same sheet and should preserve manual decimal scale text");
        AssertTrue(
            pageSetupWindow.Contains("private int _selectRequestVersion;", StringComparison.Ordinal) &&
            selectPageNameText.Contains("int requestVersion = ++_selectRequestVersion;", StringComparison.Ordinal) &&
            selectScaleText.Contains("int requestVersion = ++_selectRequestVersion;", StringComparison.Ordinal) &&
            selectPageNameText.Contains("requestVersion != _selectRequestVersion", StringComparison.Ordinal) &&
            selectScaleText.Contains("requestVersion != _selectRequestVersion", StringComparison.Ordinal),
            "floating Page Setup must cancel stale deferred SelectAll callbacks so typing cannot be selected twice");
        AssertTrue(
            pageSetupWindow.Contains("Loaded += (_, _) => SelectPageNameText(force: false);", StringComparison.Ordinal) &&
            setPage.Contains("if (selectName && IsVisible && !preservePageNameEdit && !preserveScaleEdit)", StringComparison.Ordinal) &&
            userAlreadyPlacedTextFocus.Contains("_scaleBox.IsKeyboardFocusWithin", StringComparison.Ordinal) &&
            userAlreadyPlacedTextFocus.Contains("!ReferenceEquals(focusedTextBox, target)", StringComparison.Ordinal) &&
            userAlreadyPlacedTextFocus.Contains("!IsWholeTextSelected(focusedTextBox)", StringComparison.Ordinal),
            "floating Page Setup initial name/scale focus should not steal a user-placed caret before the first edit");
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
            source.Contains("dialog.MeasurementStrokeScale", StringComparison.Ordinal),
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
