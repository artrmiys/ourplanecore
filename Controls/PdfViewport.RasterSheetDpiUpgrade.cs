using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    // PlanSwift-style fixed resolution: in static mode ensure the displayed raster
    // is at the user-chosen DPI (default 150, up to 300). Rendered once to the disk
    // cache, then pinned. A legacy raster at a different DPI is migrated once in
    // either direction so the persisted source and displayed bitmap match the
    // effective setting exactly. Static mode suppresses later live DPI swaps.
    private void QueueStaticRasterDpiApplyIfNeeded()
    {
        if (!IsStaticRasterDisplayActive() ||
            _bitmapScale <= 0 ||
            _rasterSheetSource?.Enabled != true ||
            RasterSheetCacheService.IsSourceImageRaster(_rasterSheetSource) ||
            RasterSheetCacheService.IsRasterDpiPinned(_rasterSheetSource) ||
            string.IsNullOrWhiteSpace(_pageFolder) ||
            string.IsNullOrWhiteSpace(_pdfPath))
        {
            return;
        }

        int targetDpi = SafeStaticRasterTargetDpi();
        int currentDpi = RasterSheetCacheService.RenderScaleToDpi(_bitmapScale);
        if (!StaticRasterPrefetchPolicy.RequiresPinnedDpiMigration(currentDpi, targetDpi))
            return;

        PageInfo page = CurrentRasterSheetPageInfo();
        string rebuildKey = $"{RasterSheetDpiUpgradeKey(_pdfPath, _pdfIndex, _pageFolder, targetDpi)}|static";
        lock (_rasterSheetRebuildGate)
        {
            if (!_rasterSheetRebuildsInFlight.Add(rebuildKey))
                return;
        }

        AppLog.Info(
            $"Viewport static raster DPI build queued; dpi={targetDpi}; currentDpi={currentDpi}; " +
            $"page='{_pageFolder}'; pdf='{Path.GetFileName(_pdfPath)}'; pdfPage={_pdfIndex + 1}");
        PostStatus($"Rendering page at {targetDpi} DPI: {Path.GetFileName(_pdfPath)}  page {_pdfIndex + 1}");
        _ = BuildStaticRasterDpiForCurrentPageAsync(rebuildKey, page, targetDpi);
    }

    // Clamp the chosen DPI so a single page bitmap never exceeds the render pixel
    // budget — 300 DPI on an E-size sheet would otherwise allocate ~0.5 GB.
    private int SafeStaticRasterTargetDpi()
    {
        return StaticRasterPrefetchPolicy.ResolveEffectiveTargetDpi(_pdfW, _pdfH);
    }

    private async Task BuildStaticRasterDpiForCurrentPageAsync(
        string rebuildKey,
        PageInfo queuedPage,
        int targetDpi)
    {
        RasterSheetBitmapResult preparedBitmap = new(new SkiaSharp.SKBitmap(), 0, 0, 0, "");
        bool hasPreparedBitmap = false;
        bool preparedBitmapApplied = false;
        try
        {
            if (!IsCurrentPageRasterTarget(queuedPage.PdfPath, queuedPage.PdfPage, queuedPage.FolderPath))
                return;

            await RasterSheetRefreshPrefetchSemaphore.WaitAsync().ConfigureAwait(false);
            RasterSheetBuildResult result;
            try
            {
                if (!IsCurrentPageRasterTarget(queuedPage.PdfPath, queuedPage.PdfPage, queuedPage.FolderPath))
                    return;

                PageInfo buildPage = OurPlanCoreJobStore.TryReadPage(queuedPage.FolderPath) ?? queuedPage;
                if (buildPage.RasterSheet?.Enabled != true ||
                    RasterSheetCacheService.IsRasterDpiPinned(buildPage.RasterSheet))
                    return;

                float targetScale = RasterSheetCacheService.RasterDpiToRenderScale(targetDpi);
                result = await Task.Run(() => RasterSheetCacheService.BuildAndEnable(buildPage, targetScale))
                    .ConfigureAwait(false);
            }
            finally
            {
                RasterSheetRefreshPrefetchSemaphore.Release();
            }

            if (!result.Ok || result.Source == null)
            {
                AppLog.Warn(
                    $"Viewport static raster DPI build failed; dpi={targetDpi}; error='{result.Error}'; " +
                    $"page='{queuedPage.FolderPath}'; pdf='{Path.GetFileName(queuedPage.PdfPath)}'; pdfPage={queuedPage.PdfPage + 1}");
                return;
            }
            RememberReadyRasterSheetSource(queuedPage, targetDpi, result.Source);

            hasPreparedBitmap = TryPrepareRasterSheetBitmapForUiApply(
                queuedPage,
                result.Source,
                preferOverview: false,
                out preparedBitmap);
            if (!hasPreparedBitmap)
                return;

            await Dispatcher.InvokeAsync(() =>
            {
                if (!IsCurrentPageRasterTarget(queuedPage.PdfPath, queuedPage.PdfPage, queuedPage.FolderPath) ||
                    !IsStaticRasterDisplayActive() ||
                    RasterSheetCacheService.IsRasterDpiPinned(_rasterSheetSource) ||
                    targetDpi != SafeStaticRasterTargetDpi() ||
                    !StaticRasterPrefetchPolicy.RequiresPinnedDpiMigration(
                        RasterSheetCacheService.RenderScaleToDpi(_bitmapScale),
                        targetDpi))
                {
                    return;
                }

                if (ApplyPreparedRasterSheetDpiUpgradeResult(
                        result.Source,
                        preparedBitmap,
                        targetDpi,
                        "static-dpi",
                        CaptureViewState(),
                        fitAfter: false))
                {
                    preparedBitmapApplied = true;
                }
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport static raster DPI build crashed for {queuedPage.Name}");
        }
        finally
        {
            if (hasPreparedBitmap && !preparedBitmapApplied)
                preparedBitmap.Bitmap.Dispose();

            lock (_rasterSheetRebuildGate)
                _rasterSheetRebuildsInFlight.Remove(rebuildKey);
        }
    }

    // PlanSwift-style lazy migration: a page that opened with NO raster sheet
    // (older jobs, pages never rastered) still uses the live re-rendering path and
    // looks blurry / re-renders on zoom. In static mode, build its raster once at
    // the chosen DPI in the background, then switch the page to the pinned static
    // bitmap. After this the page opens straight to static on every future visit.
    private void QueueStaticRasterLazyBuildIfNeeded(
        string pdfPath,
        int pageIndex,
        string pageFolder,
        RasterSheetSource? rasterSheet)
    {
        if (!ViewportRenderPolicy.StaticRasterModeEnabled ||
            _usingRasterSheetRender ||
            _usingLayerRenderer ||
            _pdfLayersLoadedForPage ||
            rasterSheet?.Enabled == true ||
            RasterSheetCacheService.IsRasterDpiPinned(rasterSheet) ||
            string.IsNullOrWhiteSpace(pageFolder) ||
            string.IsNullOrWhiteSpace(pdfPath) ||
            !File.Exists(pdfPath))
        {
            return;
        }

        int targetDpi = SafeStaticRasterTargetDpi();
        PageInfo page = new()
        {
            Name = string.IsNullOrWhiteSpace(pageFolder) ? $"Page {pageIndex + 1}" : Path.GetFileName(pageFolder),
            FolderPath = pageFolder,
            PdfPath = pdfPath,
            PdfPage = pageIndex,
            PdfLayersCached = _cachedLayers != null,
            PdfLayers = _cachedLayers ?? Array.Empty<PdfLayerInfo>(),
        };

        string rebuildKey = $"{RasterSheetRebuildKey(pdfPath, pageIndex, pageFolder)}|static-lazy";
        lock (_rasterSheetRebuildGate)
        {
            if (!_rasterSheetRebuildsInFlight.Add(rebuildKey))
                return;
        }

        AppLog.Info(
            $"Viewport static raster lazy build queued; dpi={targetDpi}; " +
            $"page='{pageFolder}'; pdf='{Path.GetFileName(pdfPath)}'; pdfPage={pageIndex + 1}");
        PostStatus($"Rendering page at {targetDpi} DPI: {Path.GetFileName(pdfPath)}  page {pageIndex + 1}");
        _ = BuildStaticRasterLazyForPageAsync(rebuildKey, page, targetDpi);
    }

    private async Task BuildStaticRasterLazyForPageAsync(
        string rebuildKey,
        PageInfo queuedPage,
        int targetDpi)
    {
        RasterSheetBitmapResult preparedBitmap = new(new SkiaSharp.SKBitmap(), 0, 0, 0, "");
        bool hasPreparedBitmap = false;
        bool preparedBitmapApplied = false;
        try
        {
            if (!IsCurrentPageRasterTarget(queuedPage.PdfPath, queuedPage.PdfPage, queuedPage.FolderPath))
                return;

            await RasterSheetRefreshPrefetchSemaphore.WaitAsync().ConfigureAwait(false);
            RasterSheetBuildResult result;
            try
            {
                if (!IsCurrentPageRasterTarget(queuedPage.PdfPath, queuedPage.PdfPage, queuedPage.FolderPath))
                    return;

                PageInfo buildPage = OurPlanCoreJobStore.TryReadPage(queuedPage.FolderPath) ?? queuedPage;
                if (RasterSheetCacheService.IsRasterDpiPinned(buildPage.RasterSheet))
                    return;
                float targetScale = RasterSheetCacheService.RasterDpiToRenderScale(targetDpi);
                result = await Task.Run(() =>
                {
                    RasterSheetBuildResult built = RasterSheetCacheService.BuildAndEnable(buildPage, targetScale);
                    if (built.Ok && built.Source != null)
                    {
                        // Land future opens straight on this raster (static, no responsive downgrade).
                        PageInfo enabledPage = OurPlanCoreJobStore.TryReadPage(buildPage.FolderPath) ?? buildPage;
                        RasterSheetCacheService.TrySetUseAsPageOpenRaster(enabledPage, true, out _, out _);
                    }
                    return built;
                }).ConfigureAwait(false);
            }
            finally
            {
                RasterSheetRefreshPrefetchSemaphore.Release();
            }

            if (!result.Ok || result.Source == null)
            {
                AppLog.Warn(
                    $"Viewport static raster lazy build failed; dpi={targetDpi}; error='{result.Error}'; " +
                    $"page='{queuedPage.FolderPath}'; pdf='{Path.GetFileName(queuedPage.PdfPath)}'; pdfPage={queuedPage.PdfPage + 1}");
                return;
            }

            hasPreparedBitmap = TryPrepareRasterSheetBitmapForUiApply(
                queuedPage,
                result.Source,
                preferOverview: false,
                out preparedBitmap);
            if (!hasPreparedBitmap)
                return;

            await Dispatcher.InvokeAsync(() =>
            {
                if (!IsCurrentPageRasterTarget(queuedPage.PdfPath, queuedPage.PdfPage, queuedPage.FolderPath) ||
                    !ViewportRenderPolicy.StaticRasterModeEnabled ||
                    RasterSheetCacheService.IsRasterDpiPinned(_rasterSheetSource) ||
                    _usingRasterSheetRender ||
                    _usingLayerRenderer ||
                    _pdfLayersLoadedForPage)
                {
                    return;
                }

                if (ApplyPreparedRasterSheetDpiUpgradeResult(
                        result.Source,
                        preparedBitmap,
                        targetDpi,
                        "static-lazy",
                        CaptureViewState(),
                        fitAfter: false))
                {
                    preparedBitmapApplied = true;
                }
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport static raster lazy build crashed for {queuedPage.Name}");
        }
        finally
        {
            if (hasPreparedBitmap && !preparedBitmapApplied)
                preparedBitmap.Bitmap.Dispose();

            lock (_rasterSheetRebuildGate)
                _rasterSheetRebuildsInFlight.Remove(rebuildKey);
        }
    }

    private bool TryUpgradeRasterSheetToReadyDpiForCurrentZoom()
    {
        if (!_usingRasterSheetRender ||
            _usingRasterSheetOverviewRender ||
            _rasterSheetSource?.Enabled != true ||
            RasterSheetCacheService.IsSourceImageRaster(_rasterSheetSource) ||
            RasterSheetCacheService.IsRasterDpiPinned(_rasterSheetSource) ||
            _pdfLayersLoadedForPage ||
            _usingLayerRenderer ||
            _bitmapScale <= 0 ||
            !ViewportRenderPolicy.ShouldUseDetailRender(_zoom, _bitmapScale))
        {
            return false;
        }

        if (_isFastNavigating)
            return true;

        PageInfo page = CurrentRasterSheetPageInfo();
        int currentDpi = RasterSheetCacheService.RenderScaleToDpi(_bitmapScale);
        int targetDpi = DesiredRasterSheetDpiForCurrentZoom(currentDpi);
        if (TryHoldHeavyRasterSheetDpiForRecentMotion(page, currentDpi, targetDpi))
            return true;

        if (targetDpi > currentDpi &&
            TryApplyReadyRasterSheetDpi(page, targetDpi))
        {
            return true;
        }

        return QueueRasterSheetDpiBuildForCurrentZoom(page, currentDpi, targetDpi);
    }

    private bool TryApplyResponsiveRasterSheetDpiForCurrentZoom()
    {
        if (_isFastNavigating &&
            _usingRasterSheetRender &&
            !_usingRasterSheetOverviewRender &&
            _rasterSheetSource?.Enabled == true &&
            !RasterSheetCacheService.IsSourceImageRaster(_rasterSheetSource) &&
            !RasterSheetCacheService.IsRasterDpiPinned(_rasterSheetSource) &&
            !_pdfLayersLoadedForPage &&
            !_usingLayerRenderer &&
            _bitmapScale > 0 &&
            _zoom >= ViewportRenderPolicy.RasterSheetDisplayMinZoom)
        {
            PageInfo navigationPage = CurrentRasterSheetPageInfo();
            int navigationCurrentDpi = RasterSheetCacheService.RenderScaleToDpi(_bitmapScale);
            int navigationTargetDpi = TargetRasterSheetDpiForCurrentZoom();
            if (navigationTargetDpi > 0)
            {
                if (TryApplyNavigationRasterSheetDpiForCurrentZoom(
                        navigationPage,
                        navigationCurrentDpi,
                        navigationTargetDpi))
                {
                    return true;
                }

                if (navigationCurrentDpi <= navigationTargetDpi)
                    return true;
            }
        }

        if (!ShouldUseResponsiveRasterSheetDpiForCurrentZoom(_rasterSheetSource))
            return false;

        PageInfo page = CurrentRasterSheetPageInfo();
        int currentDpi = RasterSheetCacheService.RenderScaleToDpi(_bitmapScale);
        int targetDpi = TargetRasterSheetDpiForCurrentZoom();
        if (TryHoldHeavyRasterSheetDpiForRecentMotion(page, currentDpi, targetDpi))
            return true;

        if (_isFastNavigating &&
            _usingRasterSheetRender &&
            TryApplyNavigationRasterSheetDpiForCurrentZoom(page, currentDpi, targetDpi))
        {
            return true;
        }

        if (_isFastNavigating &&
            _usingRasterSheetRender &&
            currentDpi <= targetDpi)
        {
            return true;
        }

        bool preferLowerDpi = ViewportRenderPolicy.ShouldPreferLowerRasterSheetDpi(currentDpi, targetDpi);
        if (_usingRasterSheetRender && currentDpi == targetDpi)
            return false;

        if (TryApplyReadyRasterSheetDpi(page, targetDpi))
            return true;

        if (TryApplyReadyRasterSheetDpiAtOrAbove(page, targetDpi, currentDpi))
            return true;

        if (_usingRasterSheetRender &&
            currentDpi >= targetDpi &&
            currentDpi <= ViewportRenderPolicy.RasterSheetDisplayMaxDpi &&
            !preferLowerDpi)
        {
            return false;
        }

        if (!QueueRasterSheetDpiBuildForCurrentZoom(page, currentDpi, targetDpi))
            return false;

        TrySwitchRasterSheetToFastPreviewForNavigation();
        return true;
    }

    private bool TryPrepareRasterSheetBitmapForImmediateRepaint()
    {
        // Static raster mode pins the selected page bitmap. Immediate repaint is
        // allowed to reuse it, but must never enter the responsive DPI path: at
        // low zoom that path replaces the pinned target-DPI image with a 72-DPI
        // preview and the static apply immediately raises it again.
        if (IsStaticRasterDisplayActive())
            return false;

        if (!_usingRasterSheetRender ||
            _rasterSheetSource?.Enabled != true ||
            RasterSheetCacheService.IsRasterDpiPinned(_rasterSheetSource) ||
            _pdfLayersLoadedForPage ||
            _usingLayerRenderer ||
            _bitmapScale <= 0)
        {
            return false;
        }

        if (_zoom < ViewportRenderPolicy.RasterSheetDisplayExitZoom)
        {
            return TrySwitchRasterSheetToFastPreviewForLowZoom(requestRepaint: false, requireCachedBitmap: true) ||
                   TryApplyLowZoomRasterSheetDpiFromMemory();
        }

        if (_usingRasterSheetOverviewRender ||
            RasterSheetCacheService.IsSourceImageRaster(_rasterSheetSource) ||
            _zoom < ViewportRenderPolicy.RasterSheetDisplayMinZoom)
        {
            return false;
        }

        int currentDpi = RasterSheetCacheService.RenderScaleToDpi(_bitmapScale);
        int targetDpi = TargetRasterSheetDpiForCurrentZoom();
        if (targetDpi <= 0)
            return false;

        PageInfo page = CurrentRasterSheetPageInfo();
        if (TryHoldHeavyRasterSheetDpiForRecentMotion(page, currentDpi, targetDpi))
            return true;

        if (_isFastNavigating)
        {
            if (TryApplyNavigationRasterSheetDpiForCurrentZoom(page, currentDpi, targetDpi))
                return true;

            if (currentDpi <= targetDpi)
                return false;
        }

        if (currentDpi == targetDpi)
            return false;

        if (currentDpi < targetDpi)
            return TryApplyReadyRasterSheetDpiAtOrAboveFromMemory(page, targetDpi, currentDpi);

        return TryApplyReadyRasterSheetDpiFromMemory(page, targetDpi);
    }

    private bool TryApplyLowZoomRasterSheetDpiFromMemory()
    {
        if (_usingRasterSheetOverviewRender ||
            _rasterSheetSource?.Enabled != true ||
            RasterSheetCacheService.IsSourceImageRaster(_rasterSheetSource) ||
            RasterSheetCacheService.IsRasterDpiPinned(_rasterSheetSource) ||
            _pdfLayersLoadedForPage ||
            _usingLayerRenderer ||
            _bitmapScale <= 0)
        {
            return false;
        }

        int targetDpi = TargetRasterSheetDpiForCurrentZoom();
        int currentDpi = RasterSheetCacheService.RenderScaleToDpi(_bitmapScale);
        if (targetDpi <= 0 || currentDpi <= targetDpi)
            return false;

        return TryApplyReadyRasterSheetDpiFromMemory(
            CurrentRasterSheetPageInfo(),
            targetDpi,
            postStatus: false);
    }

    private bool ShouldUseResponsiveRasterSheetDpiForCurrentZoom(RasterSheetSource? rasterSheet) =>
        ShouldUseResponsiveRasterSheetDpiForZoom(rasterSheet, _zoom);

    private bool ShouldUseResponsiveRasterSheetDpiForView(RasterSheetSource? rasterSheet, ViewState? view) =>
        ShouldUseResponsiveRasterSheetDpiForZoom(rasterSheet, view?.Zoom ?? _zoom);

    private bool ShouldUseResponsiveRasterSheetDpiForZoom(RasterSheetSource? rasterSheet, float zoom) =>
        rasterSheet?.Enabled == true &&
        !RasterSheetCacheService.IsSourceImageRaster(rasterSheet) &&
        !RasterSheetCacheService.IsRasterDpiPinned(rasterSheet) &&
        !_pdfLayersLoadedForPage &&
        !_usingLayerRenderer &&
        zoom >= ViewportRenderPolicy.RasterSheetDisplayMinZoom &&
        TargetRasterSheetDpiForZoom(zoom) > 0 &&
        RasterSheetCacheService.RenderScaleToDpi(rasterSheet.RenderScale) != TargetRasterSheetDpiForZoom(zoom);

    private int TargetRasterSheetDpiForCurrentZoom() =>
        TargetRasterSheetDpiForZoom(_zoom);

    private static int TargetRasterSheetDpiForZoom(float zoom) =>
        ViewportRenderPolicy.SelectRasterSheetDisplayDpi(zoom);

    private void QueueCurrentRasterSheetMotionWarmup()
    {
        if (_rasterSheetSource?.Enabled != true ||
            RasterSheetCacheService.IsSourceImageRaster(_rasterSheetSource) ||
            RasterSheetCacheService.IsRasterDpiPinned(_rasterSheetSource) ||
            _pdfLayersLoadedForPage ||
            _usingLayerRenderer ||
            string.IsNullOrWhiteSpace(_pageFolder) ||
            string.IsNullOrWhiteSpace(_pdfPath))
        {
            return;
        }

        PageInfo page = CurrentRasterSheetPageInfo();
        if (!ShouldQueueRasterSheetMotionWarmup(page))
            return;

        PdfViewport.PrefetchRasterSheetWorkZoomBitmaps(
            page,
            buildMissingDpis: false,
            allowDuringNavigation: true);
    }

    private bool ShouldQueueRasterSheetMotionWarmup(PageInfo page)
    {
        string key = RasterSheetMotionWarmupKey(page);
        if (string.IsNullOrWhiteSpace(key))
            return false;

        DateTime now = DateTime.UtcNow;
        if (string.Equals(key, _lastRasterSheetMotionWarmupKey, StringComparison.OrdinalIgnoreCase) &&
            (now - _lastRasterSheetMotionWarmupAt).TotalMilliseconds <
            ViewportRenderPolicy.RasterSheetMotionWarmupMinIntervalMs)
        {
            return false;
        }

        _lastRasterSheetMotionWarmupKey = key;
        _lastRasterSheetMotionWarmupAt = now;
        return true;
    }

    private static string RasterSheetMotionWarmupKey(PageInfo page)
    {
        RasterSheetSource? rasterSheet = page.RasterSheet;
        if (rasterSheet?.Enabled != true ||
            string.IsNullOrWhiteSpace(page.FolderPath) ||
            string.IsNullOrWhiteSpace(page.PdfPath))
        {
            return "";
        }

        return string.Join(
            '|',
            page.FolderPath,
            page.PdfPath,
            page.PdfPage.ToString(CultureInfo.InvariantCulture),
            rasterSheet.Image,
            rasterSheet.RenderProfile,
            rasterSheet.GeneratedAtUtc,
            rasterSheet.RenderScale.ToString(CultureInfo.InvariantCulture));
    }

    private bool TryHoldHeavyRasterSheetDpiForRecentMotion(
        PageInfo page,
        int currentDpi,
        int targetDpi)
    {
        if (!ShouldHoldHeavyRasterSheetDpiAfterMotion(targetDpi) ||
            _rasterSheetSource?.Enabled != true ||
            RasterSheetCacheService.IsSourceImageRaster(_rasterSheetSource) ||
            RasterSheetCacheService.IsRasterDpiPinned(_rasterSheetSource) ||
            _pdfLayersLoadedForPage ||
            _usingLayerRenderer)
        {
            return false;
        }

        QueueRasterSheetQualityRestoreAfterMotion(page);

        if (!_usingRasterSheetRender || _usingRasterSheetOverviewRender)
            return true;

        if (currentDpi > 0 && currentDpi <= ViewportRenderPolicy.RasterSheetNavigationMaxDpi)
            return true;

        if (TryApplyMotionHoldRasterSheetDpiFromMemory(page, currentDpi, targetDpi))
            return true;

        return TrySwitchRasterSheetToFastPreviewForNavigation(
            allowWorkZoom: true,
            allowAfterMotion: true);
    }

    private bool TryApplyMotionHoldRasterSheetDpiFromMemory(
        PageInfo page,
        int currentDpi,
        int targetDpi)
    {
        int navigationDpi = ViewportRenderPolicy.SelectRasterSheetNavigationDpi(
            _zoom,
            Math.Max(currentDpi, targetDpi),
            targetDpi);
        if (navigationDpi > 0 &&
            navigationDpi < currentDpi &&
            TryApplyReadyRasterSheetDpiFromMemory(page, navigationDpi, postStatus: false))
        {
            return true;
        }

        IReadOnlyList<int> warmupDpis = ViewportRenderPolicy.RasterSheetWorkZoomWarmupDpiSteps;
        for (int i = warmupDpis.Count - 1; i >= 0; i--)
        {
            int fallbackDpi = warmupDpis[i];
            if (fallbackDpi <= 0 ||
                fallbackDpi >= currentDpi ||
                fallbackDpi > ViewportRenderPolicy.RasterSheetNavigationMaxDpi)
            {
                continue;
            }

            if (TryApplyReadyRasterSheetDpiFromMemory(page, fallbackDpi, postStatus: false))
                return true;
        }

        return false;
    }

    private bool ShouldHoldHeavyRasterSheetDpiAfterMotion(int targetDpi)
    {
        if (_lastFastNavigationAt == DateTime.MinValue)
            return false;

        return ViewportRenderPolicy.ShouldHoldRasterSheetQualityAfterNavigation(
            RasterSheetMotionIdle(), targetDpi);
    }

    // While fast navigation is active the effective idle is zero, exactly as the
    // hold decision sees it. Both the "should hold" check and the restore delay
    // below MUST share this value: if the delay were computed from raw wall-clock
    // idle while the hold check used zero, a held pointer (idle grows past the
    // quiet window, yet _isFastNavigating stays true) would yield delay=0 while
    // the restore keeps re-queuing itself, busy-spinning the dispatcher at 100%.
    private TimeSpan RasterSheetMotionIdle()
    {
        if (_isFastNavigating || _lastFastNavigationAt == DateTime.MinValue)
            return TimeSpan.Zero;

        return DateTime.UtcNow - _lastFastNavigationAt;
    }

    private void QueueRasterSheetQualityRestoreAfterMotion(PageInfo page)
    {
        if (page.RasterSheet?.Enabled != true ||
            RasterSheetCacheService.IsRasterDpiPinned(page.RasterSheet) ||
            string.IsNullOrWhiteSpace(page.FolderPath) ||
            string.IsNullOrWhiteSpace(page.PdfPath) ||
            page.PdfPage < 0)
        {
            return;
        }

        int version = _rasterSheetQualityRestoreVersion;
        if (_rasterSheetQualityRestoreQueuedVersion == version)
            return;

        TimeSpan delay = ViewportRenderPolicy.RasterSheetQualityRestoreDelay(RasterSheetMotionIdle());
        _rasterSheetQualityRestoreQueuedVersion = version;
        _ = RestoreRasterSheetQualityAfterMotionAsync(version, page, delay);
    }

    private async Task RestoreRasterSheetQualityAfterMotionAsync(
        int version,
        PageInfo queuedPage,
        TimeSpan delay)
    {
        try
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay).ConfigureAwait(false);

            await Dispatcher.InvokeAsync(() =>
            {
                if (version != _rasterSheetQualityRestoreVersion)
                    return;

                _rasterSheetQualityRestoreQueuedVersion = 0;
                if (!IsCurrentPageRasterTarget(queuedPage.PdfPath, queuedPage.PdfPage, queuedPage.FolderPath))
                    return;

                int targetDpi = TargetRasterSheetDpiForCurrentZoom();
                if (ShouldHoldHeavyRasterSheetDpiAfterMotion(targetDpi))
                {
                    QueueRasterSheetQualityRestoreAfterMotion(queuedPage);
                    return;
                }

                RerenderForZoomIfNeeded(force: false);
                RequestRepaint();
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport raster quality restore after motion failed for {queuedPage.Name}");
        }
    }

    private bool TryApplyNavigationRasterSheetDpiForCurrentZoom(
        PageInfo page,
        int currentDpi,
        int targetDpi)
    {
        if (!_isFastNavigating ||
            !_usingRasterSheetRender ||
            _usingRasterSheetOverviewRender ||
            _rasterSheetSource?.Enabled != true ||
            RasterSheetCacheService.IsSourceImageRaster(_rasterSheetSource) ||
            _pdfLayersLoadedForPage ||
            _usingLayerRenderer)
        {
            return false;
        }

        int navigationDpi = ViewportRenderPolicy.SelectRasterSheetNavigationDpi(_zoom, currentDpi, targetDpi);
        if (navigationDpi <= 0 || navigationDpi >= currentDpi)
            return false;

        if (TryApplyReadyRasterSheetDpiFromMemory(page, navigationDpi, postStatus: false))
            return true;

        IReadOnlyList<int> warmupDpis = ViewportRenderPolicy.RasterSheetWorkZoomWarmupDpiSteps;
        for (int i = warmupDpis.Count - 1; i >= 0; i--)
        {
            int fallbackDpi = warmupDpis[i];
            if (fallbackDpi == navigationDpi ||
                fallbackDpi <= 0 ||
                fallbackDpi >= currentDpi ||
                fallbackDpi > ViewportRenderPolicy.RasterSheetNavigationMaxDpi)
            {
                continue;
            }

            if (TryApplyReadyRasterSheetDpiFromMemory(page, fallbackDpi, postStatus: false))
                return true;
        }

        return TrySwitchRasterSheetToFastPreviewForNavigation(allowWorkZoom: true);
    }

    private int ReadyRasterSheetDpiForCurrentZoom(PageInfo page, int currentDpi)
    {
        int desiredDpi = DesiredRasterSheetDpiForCurrentZoom(currentDpi);
        if (desiredDpi <= currentDpi)
            return 0;

        return TryGetRememberedReadyRasterSheetSource(page, desiredDpi, out _)
            ? desiredDpi
            : 0;
    }

    private int DesiredRasterSheetDpiForCurrentZoom(int currentDpi)
    {
        int targetDpi = TargetRasterSheetDpiForCurrentZoom();
        if (targetDpi <= currentDpi)
            return 0;

        return targetDpi;
    }

    private bool TryApplyReadyRasterSheetDpi(PageInfo page, int targetDpi) =>
        TryApplyReadyRasterSheetDpi(page, targetDpi, CaptureViewState(), fitAfter: false);

    private bool TryApplyReadyRasterSheetDpiFromMemory(PageInfo page, int targetDpi, bool postStatus = true) =>
        TryApplyReadyRasterSheetDpiFromMemory(page, targetDpi, CaptureViewState(), fitAfter: false, postStatus: postStatus);

    private bool TryApplyReadyRasterSheetDpiFromMemory(
        PageInfo page,
        int targetDpi,
        ViewState? restoreView,
        bool fitAfter,
        bool postStatus = true) =>
        QueuePreparedReadyRasterSheetDpiApplyFromMemory(
            page,
            targetDpi,
            restoreView,
            fitAfter,
            postStatus,
            sourceKind: "ready-memory");

    private bool TryApplyReadyRasterSheetDpiAtOrAbove(PageInfo page, int targetDpi, int currentDpi)
    {
        int readyDpi = SelectReadyRasterSheetDpiAtOrAbove(page, targetDpi, currentDpi);
        return readyDpi > 0 && TryApplyReadyRasterSheetDpi(page, readyDpi);
    }

    private bool TryApplyReadyRasterSheetDpiAtOrAbove(
        PageInfo page,
        int targetDpi,
        int currentDpi,
        ViewState? restoreView,
        bool fitAfter)
    {
        int readyDpi = SelectReadyRasterSheetDpiAtOrAbove(page, targetDpi, currentDpi);
        return readyDpi > 0 && TryApplyReadyRasterSheetDpi(page, readyDpi, restoreView, fitAfter);
    }

    private bool TryApplyReadyRasterSheetDpiAtOrAboveFromMemory(PageInfo page, int targetDpi, int currentDpi)
    {
        int readyDpi = SelectReadyRasterSheetDpiAtOrAbove(page, targetDpi, currentDpi);
        return readyDpi > 0 && TryApplyReadyRasterSheetDpiFromMemory(page, readyDpi);
    }

    private bool TryApplyReadyRasterSheetDpiAtOrAboveFromMemory(
        PageInfo page,
        int targetDpi,
        int currentDpi,
        ViewState? restoreView,
        bool fitAfter)
    {
        int readyDpi = SelectReadyRasterSheetDpiAtOrAbove(page, targetDpi, currentDpi);
        return readyDpi > 0 && TryApplyReadyRasterSheetDpiFromMemory(page, readyDpi, restoreView, fitAfter);
    }

    private int SelectReadyRasterSheetDpiAtOrAbove(PageInfo page, int targetDpi, int currentDpi)
    {
        if (currentDpi >= targetDpi)
            return 0;

        if (!TryGetRememberedReadyRasterSheetSource(page, targetDpi, out _))
            return 0;
        if (targetDpi > ViewportRenderPolicy.RasterSheetDisplayMaxDpi)
            return 0;
        if (_usingRasterSheetRender && targetDpi == currentDpi)
            return 0;

        return targetDpi;
    }

    private bool TryApplyReadyRasterSheetDpi(
        PageInfo page,
        int targetDpi,
        ViewState? restoreView,
        bool fitAfter)
    {
        if (TryApplyReadyRasterSheetDpiFromMemory(
                page,
                targetDpi,
                restoreView,
                fitAfter))
        {
            QueueReadyRasterSheetDpiPersistAfterMemoryApply(page, targetDpi);
            return true;
        }

        return false;
    }

    private void QueueReadyRasterSheetDpiPersistAfterMemoryApply(PageInfo page, int targetDpi)
    {
        if (targetDpi <= 0 ||
            string.IsNullOrWhiteSpace(page.FolderPath) ||
            string.IsNullOrWhiteSpace(page.PdfPath))
        {
            return;
        }

        string persistKey = $"{RasterSheetDpiUpgradeKey(page.PdfPath, page.PdfPage, page.FolderPath, targetDpi)}|persist";
        lock (_rasterSheetRebuildGate)
        {
            if (!_rasterSheetRebuildsInFlight.Add(persistKey))
                return;
        }

        _ = PersistReadyRasterSheetDpiAfterMemoryApplyAsync(persistKey, page, targetDpi);
    }

    private async Task PersistReadyRasterSheetDpiAfterMemoryApplyAsync(
        string persistKey,
        PageInfo queuedPage,
        int targetDpi)
    {
        try
        {
            float targetScale = RasterSheetCacheService.RasterDpiToRenderScale(targetDpi);
            await Task.Run(() =>
            {
                PageInfo persistPage = OurPlanCoreJobStore.TryReadPage(queuedPage.FolderPath) ?? queuedPage;
                RasterSheetCacheService.TryEnableReadyReadableRaster(persistPage, targetScale, out _);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport ready raster DPI persist failed for {queuedPage.Name}");
        }
        finally
        {
            lock (_rasterSheetRebuildGate)
                _rasterSheetRebuildsInFlight.Remove(persistKey);
        }
    }

    private bool QueueRasterSheetDpiBuildForCurrentZoom(
        PageInfo page,
        int currentDpi,
        int targetDpi,
        bool allowImmediateReadyApply = true)
    {
        if (targetDpi <= 0 ||
            targetDpi > ViewportRenderPolicy.RasterSheetDisplayMaxDpi ||
            targetDpi == currentDpi ||
            currentDpi > targetDpi &&
            !ViewportRenderPolicy.ShouldPreferLowerRasterSheetDpi(currentDpi, targetDpi) ||
            string.IsNullOrWhiteSpace(page.FolderPath) ||
            string.IsNullOrWhiteSpace(page.PdfPath))
        {
            return false;
        }

        if (allowImmediateReadyApply && TryApplyReadyRasterSheetDpi(page, targetDpi))
        {
            return true;
        }

        if (!allowImmediateReadyApply &&
            TryGetRememberedReadyRasterSheetSource(page, targetDpi, out _))
        {
            return QueueReadyRasterSheetDpiApplyAfterWarmup(page, targetDpi);
        }

        string rebuildKey = RasterSheetDpiUpgradeKey(_pdfPath, _pdfIndex, _pageFolder, targetDpi);
        lock (_rasterSheetRebuildGate)
        {
            if (!_rasterSheetRebuildsInFlight.Add(rebuildKey))
                return true;
        }

        AppLog.Info(
            $"Viewport raster DPI build queued; dpi={targetDpi}; currentDpi={currentDpi}; " +
            $"page='{_pageFolder}'; pdf='{Path.GetFileName(_pdfPath)}'; pdfPage={_pdfIndex + 1}");
        PostStatus($"Raster sheet preparing {targetDpi} DPI: {Path.GetFileName(_pdfPath)}  page {_pdfIndex + 1}");
        _ = BuildRasterSheetDpiUpgradeForCurrentPageAsync(rebuildKey, page, targetDpi);
        return true;
    }

    private bool QueueReadyRasterSheetDpiApplyAfterWarmup(PageInfo page, int targetDpi)
    {
        return QueueReadyRasterSheetDpiApplyAfterWarmup(
            page,
            targetDpi,
            restoreView: null,
            fitAfter: false,
            pageOpenNavigationVersion: 0);
    }

    private bool QueueReadyRasterSheetDpiApplyAfterWarmup(
        PageInfo page,
        int targetDpi,
        ViewState? restoreView,
        bool fitAfter,
        int pageOpenNavigationVersion)
    {
        string rebuildKey = pageOpenNavigationVersion > 0
            ? $"{RasterSheetDpiUpgradeKey(page.PdfPath, page.PdfPage, page.FolderPath, targetDpi)}|open-ready"
            : RasterSheetDpiUpgradeKey(page.PdfPath, page.PdfPage, page.FolderPath, targetDpi);
        lock (_rasterSheetRebuildGate)
        {
            if (!_rasterSheetRebuildsInFlight.Add(rebuildKey))
                return true;
        }

        AppLog.Info(
            $"Viewport ready raster DPI apply queued; dpi={targetDpi}; " +
            $"page='{page.FolderPath}'; pdf='{Path.GetFileName(page.PdfPath)}'; pdfPage={page.PdfPage + 1}");
        _ = ApplyReadyRasterSheetDpiAfterWarmupAsync(
            rebuildKey,
            page,
            targetDpi,
            restoreView,
            fitAfter,
            pageOpenNavigationVersion);
        return true;
    }

    private async Task ApplyReadyRasterSheetDpiAfterWarmupAsync(
        string rebuildKey,
        PageInfo queuedPage,
        int targetDpi,
        ViewState? pageOpenRestoreView = null,
        bool pageOpenFitAfter = false,
        int pageOpenNavigationVersion = 0)
    {
        RasterSheetBitmapResult preparedBitmap = new(new SkiaSharp.SKBitmap(), 0, 0, 0, "");
        bool hasPreparedBitmap = false;
        bool preparedBitmapApplied = false;
        try
        {
            if (!IsCurrentPageRasterTarget(queuedPage.PdfPath, queuedPage.PdfPage, queuedPage.FolderPath))
                return;

            float targetScale = RasterSheetCacheService.RasterDpiToRenderScale(targetDpi);
            RasterSheetBuildResult result = await Task.Run(() =>
            {
                PageInfo buildPage = OurPlanCoreJobStore.TryReadPage(queuedPage.FolderPath) ?? queuedPage;
                return RasterSheetCacheService.TryEnableReadyReadableRaster(buildPage, targetScale, out RasterSheetBuildResult enabled)
                    ? enabled
                    : new RasterSheetBuildResult(false, null, "", "");
            }).ConfigureAwait(false);
            if (!result.Ok || result.Source == null)
                return;
            RememberReadyRasterSheetSource(queuedPage, targetDpi, result.Source);

            hasPreparedBitmap = await Task.Run(() => TryPrepareRasterSheetBitmapForUiApply(
                    queuedPage,
                    result.Source,
                    preferOverview: false,
                    out preparedBitmap))
                .ConfigureAwait(false);
            if (!hasPreparedBitmap)
            {
                AppLog.Warn(
                    $"Viewport ready raster DPI apply skipped; dpi={targetDpi}; reason='bitmap prepare failed'; " +
                    $"page='{queuedPage.FolderPath}'; pdf='{Path.GetFileName(queuedPage.PdfPath)}'; pdfPage={queuedPage.PdfPage + 1}");
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (!IsCurrentPageRasterTarget(queuedPage.PdfPath, queuedPage.PdfPage, queuedPage.FolderPath) ||
                    _pdfLayersLoadedForPage ||
                    _usingLayerRenderer)
                {
                    return;
                }

                string sourceKind = "ready-warmed";
                if (pageOpenNavigationVersion > 0)
                {
                    if (!TryGetPageOpenRasterSheetDpiApplyView(
                            pageOpenRestoreView,
                            pageOpenFitAfter,
                            pageOpenNavigationVersion,
                            out ViewState? applyView,
                            out bool applyFitAfter))
                    {
                        return;
                    }

                    if (ApplyPreparedRasterSheetDpiUpgradeResult(
                            result.Source,
                            preparedBitmap,
                            targetDpi,
                            "ready-warmed-page-open",
                            applyView,
                            applyFitAfter))
                    {
                        preparedBitmapApplied = true;
                        return;
                    }
                }
                else
                {
                    if (!ShouldUseRasterSheetForCurrentZoom() ||
                        targetDpi != TargetRasterSheetDpiForCurrentZoom())
                    {
                        return;
                    }

                    if (ApplyPreparedRasterSheetDpiUpgradeResult(
                            result.Source,
                            preparedBitmap,
                            targetDpi,
                            sourceKind,
                            CaptureViewState(),
                            fitAfter: false))
                    {
                        preparedBitmapApplied = true;
                        return;
                    }
                }
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport ready raster DPI apply crashed for {queuedPage.Name}");
        }
        finally
        {
            if (hasPreparedBitmap && !preparedBitmapApplied)
                preparedBitmap.Bitmap.Dispose();

            lock (_rasterSheetRebuildGate)
                _rasterSheetRebuildsInFlight.Remove(rebuildKey);
        }
    }

    private async Task BuildRasterSheetDpiUpgradeForCurrentPageAsync(
        string rebuildKey,
        PageInfo queuedPage,
        int targetDpi,
        ViewState? pageOpenRestoreView = null,
        bool pageOpenFitAfter = false,
        int pageOpenNavigationVersion = 0)
    {
        RasterSheetBitmapResult preparedBitmap = new(new SkiaSharp.SKBitmap(), 0, 0, 0, "");
        bool hasPreparedBitmap = false;
        bool preparedBitmapApplied = false;
        try
        {
            if (!IsCurrentPageRasterTarget(queuedPage.PdfPath, queuedPage.PdfPage, queuedPage.FolderPath))
                return;

            await RasterSheetRefreshPrefetchSemaphore.WaitAsync().ConfigureAwait(false);
            RasterSheetBuildResult result;
            try
            {
                if (!IsCurrentPageRasterTarget(queuedPage.PdfPath, queuedPage.PdfPage, queuedPage.FolderPath))
                    return;

                PageInfo buildPage = OurPlanCoreJobStore.TryReadPage(queuedPage.FolderPath) ?? queuedPage;
                if (buildPage.RasterSheet?.Enabled != true)
                    return;

                float targetScale = RasterSheetCacheService.RasterDpiToRenderScale(targetDpi);
                result = await Task.Run(() => RasterSheetCacheService.BuildAndEnable(buildPage, targetScale))
                    .ConfigureAwait(false);
            }
            finally
            {
                RasterSheetRefreshPrefetchSemaphore.Release();
            }

            if (!result.Ok || result.Source == null)
            {
                AppLog.Warn(
                    $"Viewport raster DPI upgrade failed; dpi={targetDpi}; error='{result.Error}'; " +
                    $"page='{queuedPage.FolderPath}'; pdf='{Path.GetFileName(queuedPage.PdfPath)}'; pdfPage={queuedPage.PdfPage + 1}");
                return;
            }
            RememberReadyRasterSheetSource(queuedPage, targetDpi, result.Source);

            AppLog.Info(
                $"Viewport raster DPI upgrade built; dpi={targetDpi}; reused={result.Reused}; " +
                $"page='{queuedPage.FolderPath}'; pdf='{Path.GetFileName(queuedPage.PdfPath)}'; pdfPage={queuedPage.PdfPage + 1}");
            hasPreparedBitmap = TryPrepareRasterSheetBitmapForUiApply(
                queuedPage,
                result.Source,
                preferOverview: false,
                out preparedBitmap);
            if (!hasPreparedBitmap)
            {
                AppLog.Warn(
                    $"Viewport raster DPI upgrade apply skipped; dpi={targetDpi}; reason='bitmap prepare failed'; " +
                    $"page='{queuedPage.FolderPath}'; pdf='{Path.GetFileName(queuedPage.PdfPath)}'; pdfPage={queuedPage.PdfPage + 1}");
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (!IsCurrentPageRasterTarget(queuedPage.PdfPath, queuedPage.PdfPage, queuedPage.FolderPath) ||
                    _pdfLayersLoadedForPage ||
                    _usingLayerRenderer)
                {
                    return;
                }

                if (pageOpenNavigationVersion > 0)
                {
                    if (!TryGetPageOpenRasterSheetDpiApplyView(
                            pageOpenRestoreView,
                            pageOpenFitAfter,
                            pageOpenNavigationVersion,
                            out ViewState? applyView,
                            out bool applyFitAfter))
                    {
                        return;
                    }

                    if (ApplyPreparedRasterSheetDpiUpgradeResult(
                            result.Source,
                            preparedBitmap,
                            targetDpi,
                            "built-page-open",
                            applyView,
                            applyFitAfter))
                    {
                        preparedBitmapApplied = true;
                        return;
                    }
                }
                else
                {
                    if (!ShouldUseRasterSheetForCurrentZoom() ||
                        targetDpi != TargetRasterSheetDpiForCurrentZoom())
                    {
                        return;
                    }

                    if (ApplyPreparedRasterSheetDpiUpgradeResult(
                            result.Source,
                            preparedBitmap,
                            targetDpi,
                            "built",
                            CaptureViewState(),
                            fitAfter: false))
                    {
                        preparedBitmapApplied = true;
                        return;
                    }
                }
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Viewport raster DPI upgrade crashed for {queuedPage.Name}");
        }
        finally
        {
            if (hasPreparedBitmap && !preparedBitmapApplied)
                preparedBitmap.Bitmap.Dispose();

            lock (_rasterSheetRebuildGate)
                _rasterSheetRebuildsInFlight.Remove(rebuildKey);
        }
    }

    private bool ApplyRasterSheetDpiUpgradeResult(
        RasterSheetSource source,
        int targetDpi,
        string sourceKind,
        out string applyReason,
        bool requireCachedBitmap = false) =>
        ApplyRasterSheetDpiUpgradeResult(
            source,
            targetDpi,
            sourceKind,
            CaptureViewState(),
            fitAfter: false,
            out applyReason,
            requireCachedBitmap);

    private bool ApplyRasterSheetDpiUpgradeResult(
        RasterSheetSource source,
        int targetDpi,
        string sourceKind,
        ViewState? restoreView,
        bool fitAfter,
        out string applyReason,
        bool requireCachedBitmap = false)
    {
        RasterSheetSource candidate = source.Clone();
        if (TryApplyRasterSheetRender(
                _pdfPath,
                _pdfIndex,
                _pageFolder,
                candidate,
                restoreView,
                fitAfter,
                preferOverview: false,
                requireCachedBitmap,
                out applyReason))
        {
            _rasterSheetSource = candidate.Clone();
            PostStatus($"Raster sheet {targetDpi} DPI: {Path.GetFileName(_pdfPath)}  page {_pdfIndex + 1}");
            AppLog.Info(
                $"Viewport raster DPI upgrade applied; source='{sourceKind}'; dpi={targetDpi}; " +
                $"page='{_pageFolder}'; pdf='{Path.GetFileName(_pdfPath)}'; pdfPage={_pdfIndex + 1}");
            QueueDetailRenderOverRasterSheetIfNeeded(force: false);
            RequestRepaint();
            return true;
        }

        return false;
    }

    private PageInfo CurrentRasterSheetPageInfo(RasterSheetSource? rasterSheet = null) => new()
    {
        Name = string.IsNullOrWhiteSpace(_pageFolder) ? $"Page {_pdfIndex + 1}" : Path.GetFileName(_pageFolder),
        FolderPath = _pageFolder,
        PdfPath = _pdfPath,
        PdfPage = _pdfIndex,
        PdfLayersCached = _cachedLayers != null,
        PdfLayers = _cachedLayers ?? Array.Empty<PdfLayerInfo>(),
        RasterSheet = rasterSheet?.Clone() ?? _rasterSheetSource?.Clone(),
    };

    private static string RasterSheetDpiUpgradeKey(string pdfPath, int pageIndex, string pageFolder, int targetDpi) =>
        $"{RasterSheetRebuildKey(pdfPath, pageIndex, pageFolder)}|dpi:{targetDpi}";
}
