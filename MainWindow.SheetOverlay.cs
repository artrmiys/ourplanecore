using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

public partial class MainWindow
{
    private const string DefaultSheetOverlayColor = "#E53935";
    private const double DefaultSheetOverlayOpacity = 0.82;
    private const double MinimumBrightSheetOverlayOpacity = 0.82;
    private const double SheetOverlayAlphaBoost = 1.85;
    private const string SheetOverlayTintStyleVersion = "bright-v2";
    private readonly SheetOverlayBitmapCache _sheetOverlayBitmapCache = new(maxEntries: 8);
    private int _sheetOverlayLoadVersion;

    private static readonly IReadOnlyList<(string Label, string Hex)> SheetOverlayColors =
    [
        ("Red", "#E53935"),
        ("Blue", "#1E88E5"),
        ("Green", "#43A047"),
        ("Orange", "#FB8C00"),
        ("Magenta", "#D81B60"),
        ("Gray", "#546E7A"),
    ];

    private void SetCurrentSheetOverlay(PageInfo overlayPage)
    {
        if (_currentPage == null)
        {
            TxtStatus.Text = "Open a sheet before setting an overlay.";
            return;
        }

        if (SameFolder(_currentPage.FolderPath, overlayPage.FolderPath))
        {
            TxtStatus.Text = "A sheet cannot overlay itself.";
            return;
        }

        OurPlaneCoreJobStore.SavePageOverlay(
            _currentPage.FolderPath,
            overlayPage.FolderPath,
            CurrentSheetOverlayColor(),
            CurrentSheetOverlayOpacity());
        OurPlaneCoreJobStore.SavePageOverlayVisibility(_currentPage.FolderPath, true);
        SyncReciprocalSheetOverlay(ReadLatestSheetOverlayPage(_currentPage));

        ReloadCurrentSheetOverlay($"Overlay set: {overlayPage.Name}");
    }

    private void ClearCurrentSheetOverlay()
    {
        if (_currentPage == null)
            return;

        ClearReciprocalSheetOverlay(ReadLatestSheetOverlayPage(_currentPage));
        OurPlaneCoreJobStore.ClearPageOverlay(_currentPage.FolderPath);
        if (OurPlaneCoreJobStore.TryReadPage(_currentPage.FolderPath) is { } updated)
            _currentPage = updated;
        _viewport.ClearSheetOverlay();
        RefreshPageOverlayTreeNode(_currentPage);
        TxtStatus.Text = "Sheet overlay cleared.";
    }

    private void SetCurrentSheetOverlayColor(string color)
    {
        if (_currentPage == null || string.IsNullOrWhiteSpace(_currentPage.OverlayPageFolder))
        {
            TxtStatus.Text = "Set a sheet overlay before choosing overlay color.";
            return;
        }

        OurPlaneCoreJobStore.SavePageOverlay(
            _currentPage.FolderPath,
            _currentPage.OverlayPageFolder,
            color,
            CurrentSheetOverlayOpacity());
        SyncReciprocalSheetOverlay(ReadLatestSheetOverlayPage(_currentPage));

        ReloadCurrentSheetOverlay($"Overlay color: {color}");
    }

    private void SetPageSheetOverlayColor(PageInfo page, string color)
    {
        if (string.IsNullOrWhiteSpace(page.OverlayPageFolder))
            return;

        OurPlaneCoreJobStore.SavePageOverlay(
            page.FolderPath,
            page.OverlayPageFolder,
            color,
            page.OverlayOpacity);
        SyncReciprocalSheetOverlay(ReadLatestSheetOverlayPage(page));
        RefreshPageOverlayState(page.FolderPath, $"Overlay color: {color}");
    }

    private void NudgeSheetOverlay(PageInfo page, double dxPt, double dyPt)
    {
        PageInfo latest = ReadLatestSheetOverlayPage(page);
        double nextX = latest.OverlayOffsetXPt + dxPt;
        double nextY = latest.OverlayOffsetYPt + dyPt;
        SetSheetOverlayTransform(
            latest,
            nextX,
            nextY,
            latest.OverlayScale,
            latest.OverlayRotationDegrees,
            $"Overlay moved: X {FormatOverlayNumber(nextX)}, Y {FormatOverlayNumber(nextY)}.");
    }

    private void ScaleSheetOverlay(PageInfo page, double factor)
    {
        PageInfo latest = ReadLatestSheetOverlayPage(page);
        double nextScale = latest.OverlayScale * factor;
        SetSheetOverlayTransform(
            latest,
            latest.OverlayOffsetXPt,
            latest.OverlayOffsetYPt,
            nextScale,
            latest.OverlayRotationDegrees,
            $"Overlay scale: {FormatOverlayNumber(nextScale)}x.");
    }

    private void RotateSheetOverlay(PageInfo page, double deltaDegrees)
    {
        PageInfo latest = ReadLatestSheetOverlayPage(page);
        double nextRotation = NormalizeOverlayRotationDegrees(latest.OverlayRotationDegrees + deltaDegrees);
        SetSheetOverlayTransform(
            latest,
            latest.OverlayOffsetXPt,
            latest.OverlayOffsetYPt,
            latest.OverlayScale,
            nextRotation,
            $"Overlay rotation: {FormatOverlayNumber(nextRotation)} deg.");
    }

    private void EditSheetOverlayTransform(PageInfo page)
    {
        PageInfo latest = ReadLatestSheetOverlayPage(page);
        if (!ShowSheetOverlayTransformDialog(latest, out double xPt, out double yPt, out double scale, out double rotation))
            return;

        SetSheetOverlayTransform(latest, xPt, yPt, scale, rotation, "Overlay transform updated.");
    }

    private void SetSheetOverlayTransform(
        PageInfo page,
        double offsetXPt,
        double offsetYPt,
        double overlayScale,
        double overlayRotationDegrees,
        string status)
    {
        PageInfo latest = ReadLatestSheetOverlayPage(page);
        if (string.IsNullOrWhiteSpace(latest.OverlayPageFolder))
            return;

        OurPlaneCoreJobStore.SavePageOverlayTransform(
            latest.FolderPath,
            offsetXPt,
            offsetYPt,
            overlayScale,
            overlayRotationDegrees);
        SyncReciprocalSheetOverlay(ReadLatestSheetOverlayPage(latest));
        RefreshPageOverlayState(latest.FolderPath, status);
    }

    private static PageInfo ReadLatestSheetOverlayPage(PageInfo page) =>
        OurPlaneCoreJobStore.TryReadPage(page.FolderPath) ?? page;

    private void BeginSheetOverlayPointEdit(PageInfo page)
    {
        if (string.IsNullOrWhiteSpace(page.OverlayPageFolder))
        {
            TxtStatus.Text = "Set a sheet overlay before editing it.";
            return;
        }

        if (_currentPage == null || !SameFolder(_currentPage.FolderPath, page.FolderPath))
            OpenPageInActiveTab(page);

        if (_currentPage == null || !SameFolder(_currentPage.FolderPath, page.FolderPath))
        {
            TxtStatus.Text = "Open the sheet before editing its overlay.";
            return;
        }

        _viewport.BeginSheetOverlayPointEdit();
    }

    private void OnSheetOverlayTransformChanged(SheetOverlayTransformChange change)
    {
        if (_currentPage == null || string.IsNullOrWhiteSpace(_currentPage.OverlayPageFolder))
            return;

        OurPlaneCoreJobStore.SavePageOverlayTransform(
            _currentPage.FolderPath,
            change.OffsetXPt,
            change.OffsetYPt,
            change.OverlayScale,
            change.OverlayRotationDegrees);

        if (OurPlaneCoreJobStore.TryReadPage(_currentPage.FolderPath) is { } updated)
        {
            _currentPage = updated;
            SyncReciprocalSheetOverlay(updated);
            RefreshPageOverlayTreeNode(updated);
        }

        TxtStatus.Text = change.Status;
    }

    private void ClearPageOverlay(PageInfo page)
    {
        ClearReciprocalSheetOverlay(ReadLatestSheetOverlayPage(page));
        OurPlaneCoreJobStore.ClearPageOverlay(page.FolderPath);
        if (_currentPage != null && SameFolder(_currentPage.FolderPath, page.FolderPath))
        {
            if (OurPlaneCoreJobStore.TryReadPage(page.FolderPath) is { } updated)
                _currentPage = updated;
            _viewport.ClearSheetOverlay();
        }

        if (OurPlaneCoreJobStore.TryReadPage(page.FolderPath) is { } refreshed)
            RefreshPageOverlayTreeNode(refreshed);
        else
            RefreshPagesTakeoffIndicators();
        TxtStatus.Text = "Sheet overlay cleared.";
    }

    private void SyncReciprocalSheetOverlay(PageInfo page)
    {
        if (!SheetOverlayReciprocalService.TrySync(page, out string reciprocalPageFolder))
            return;

        if (OurPlaneCoreJobStore.TryReadPage(reciprocalPageFolder) is { } reciprocalPage)
            RefreshPageOverlayTreeNode(reciprocalPage);
    }

    private void ClearReciprocalSheetOverlay(PageInfo page)
    {
        if (!SheetOverlayReciprocalService.TryClear(page, out string reciprocalPageFolder))
            return;

        if (OurPlaneCoreJobStore.TryReadPage(reciprocalPageFolder) is { } reciprocalPage)
            RefreshPageOverlayTreeNode(reciprocalPage);
    }

    private void RefreshPageOverlayState(string pageFolder, string status)
    {
        PageInfo? updated = OurPlaneCoreJobStore.TryReadPage(pageFolder);
        if (updated == null)
            return;

        if (_currentPage != null && SameFolder(_currentPage.FolderPath, pageFolder))
        {
            _currentPage = updated;
            LoadSheetOverlay(updated);
        }

        RefreshPageOverlayTreeNode(updated);
        TxtStatus.Text = status;
    }

    private void OpenSheetOverlaySource(PageInfo page)
    {
        PageInfo latest = ReadLatestSheetOverlayPage(page);
        if (string.IsNullOrWhiteSpace(latest.OverlayPageFolder))
        {
            TxtStatus.Text = "Set a sheet overlay before opening the overlay sheet.";
            return;
        }

        PageInfo? overlayPage = OurPlaneCoreJobStore.TryReadPage(latest.OverlayPageFolder);
        if (overlayPage == null)
        {
            TxtStatus.Text = "Overlay sheet source is missing.";
            return;
        }

        OpenPageInActiveTab(overlayPage);
        TxtStatus.Text = $"Opened overlay sheet: {overlayPage.Name}.";
    }

    private void RefreshPageOverlayTreeNode(PageInfo page)
    {
        if (FindPageTreeItemByFolder(page.FolderPath) is not { } item)
        {
            RefreshPagesTakeoffIndicators();
            return;
        }

        bool wasExpanded = item.IsExpanded;
        item.Tag = page;
        item.Header = BuildPageHeader(page);
        RebuildPageTakeoffNodes(item, page);
        item.IsExpanded = wasExpanded;
        ApplyPagesMultiSelectionVisuals();
        ApplyPagesTreeSearchFilter();
    }

    private void ReloadCurrentSheetOverlay(string status)
    {
        if (_currentPage == null)
            return;

        if (OurPlaneCoreJobStore.TryReadPage(_currentPage.FolderPath) is { } updated)
            _currentPage = updated;

        LoadSheetOverlay(_currentPage);
        RefreshPageOverlayTreeNode(_currentPage);
        TxtStatus.Text = status;
    }

    private void LoadSheetOverlay(
        PageInfo page,
        PdfViewport.ViewState? restoreView = null,
        float? requestedRenderScale = null,
        bool keepExistingUntilReady = false)
    {
        int version = ++_sheetOverlayLoadVersion;
        if (!keepExistingUntilReady)
            _viewport.ClearSheetOverlay();
        if (string.IsNullOrWhiteSpace(page.OverlayPageFolder) || !page.OverlayVisible)
        {
            _viewport.ClearSheetOverlay();
            return;
        }

        float renderScale = SelectSheetOverlayViewportRenderScale(page, restoreView, requestedRenderScale);
        if (!TryBuildSheetOverlayBitmap(
                page,
                renderScale,
                allowRender: false,
                out SKBitmap? bitmap,
                out float widthPt,
                out float heightPt,
                out string overlayName,
                out string error) ||
            bitmap == null)
        {
            _ = LoadSheetOverlayAsync(page, version, renderScale);
            return;
        }

        ApplySheetOverlayBitmapToViewport(page, bitmap, widthPt, heightPt, overlayName, renderScale);
    }

    private void QueueSheetOverlayLoadForPageOpen(PageInfo page, PdfViewport.ViewState? restoreView = null)
    {
        int version = ++_sheetOverlayLoadVersion;
        if (string.IsNullOrWhiteSpace(page.OverlayPageFolder) || !page.OverlayVisible)
        {
            _viewport.ClearSheetOverlay();
            return;
        }

        float renderScale = SelectSheetOverlayPageOpenFirstFrameRenderScale(page, restoreView);
        if (!TryApplyCachedSheetOverlay(page, restoreView, renderScale))
        {
            _viewport.ClearSheetOverlay();
            TxtStatus.Text = "Sheet overlay loading...";
        }
        _ = LoadSheetOverlayAsync(page, version, renderScale);
    }

    private bool TryApplyCachedSheetOverlay(
        PageInfo page,
        PdfViewport.ViewState? restoreView = null,
        float? requestedRenderScale = null)
    {
        if (string.IsNullOrWhiteSpace(page.OverlayPageFolder) || !page.OverlayVisible)
            return false;

        float renderScale = requestedRenderScale is > 0
            ? requestedRenderScale.Value
            : SelectSheetOverlayViewportRenderScale(page, restoreView);
        if (!TryBuildSheetOverlayBitmap(
                page,
                renderScale,
                allowRender: false,
                out SKBitmap? bitmap,
                out float widthPt,
                out float heightPt,
                out string overlayName,
                out _) ||
            bitmap == null)
        {
            return false;
        }

        ApplySheetOverlayBitmapToViewport(page, bitmap, widthPt, heightPt, overlayName, renderScale);
        return true;
    }

    private async Task LoadSheetOverlayAsync(PageInfo page, int version, float renderScale)
    {
        SheetOverlayBuildResult result = await Task.Run(() =>
        {
            bool ok = TryBuildSheetOverlayBitmap(
                page,
                renderScale,
                allowRender: true,
                out SKBitmap? bitmap,
                out float widthPt,
                out float heightPt,
                out string overlayName,
                out string error);
            return new SheetOverlayBuildResult(ok, bitmap, widthPt, heightPt, overlayName, error);
        });

        if (version != _sheetOverlayLoadVersion ||
            _currentPage == null ||
            !SameFolder(_currentPage.FolderPath, page.FolderPath))
        {
            result.Bitmap?.Dispose();
            return;
        }

        PageInfo? latest = OurPlaneCoreJobStore.TryReadPage(page.FolderPath);
        if (latest == null ||
            string.IsNullOrWhiteSpace(latest.OverlayPageFolder) ||
            !latest.OverlayVisible)
        {
            result.Bitmap?.Dispose();
            _viewport.ClearSheetOverlay();
            return;
        }

        if (!SameFolder(latest.OverlayPageFolder, page.OverlayPageFolder))
        {
            result.Bitmap?.Dispose();
            LoadSheetOverlay(latest);
            return;
        }

        if (!result.Ok || result.Bitmap == null)
        {
            TxtStatus.Text = $"Sheet overlay unavailable: {result.Error}";
            return;
        }

        ApplySheetOverlayBitmapToViewport(
            latest,
            result.Bitmap,
            result.WidthPt,
            result.HeightPt,
            result.OverlayName,
            renderScale);
    }

    private void ApplySheetOverlayBitmapToViewport(
        PageInfo page,
        SKBitmap bitmap,
        float widthPt,
        float heightPt,
        string overlayName,
        float renderScale)
    {
        PageInfo? overlayPage = OurPlaneCoreJobStore.TryReadPage(page.OverlayPageFolder);
        _viewport.SetSheetOverlay(
            bitmap,
            widthPt,
            heightPt,
            overlayName,
            (float)page.OverlayOffsetXPt,
            (float)page.OverlayOffsetYPt,
            (float)page.OverlayScale,
            (float)page.OverlayRotationDegrees,
            overlayPage?.PdfPath ?? "",
            overlayPage?.PdfPage ?? 0,
            OverlaySnapLayers(overlayPage),
            bitmapScale: renderScale);
    }

    private static IReadOnlyList<PdfLayerInfo>? OverlaySnapLayers(PageInfo? overlayPage) =>
        overlayPage is { PdfLayersCached: true, PdfLayers.Count: > 0 }
            ? overlayPage.PdfLayers
            : null;

    private void OnSheetOverlayRenderScaleRefreshRequested(float requestedRenderScale)
    {
        if (_currentPage == null ||
            string.IsNullOrWhiteSpace(_currentPage.OverlayPageFolder) ||
            !_currentPage.OverlayVisible)
        {
            return;
        }

        LoadSheetOverlay(_currentPage, requestedRenderScale: requestedRenderScale, keepExistingUntilReady: true);
    }

    private float SelectSheetOverlayViewportRenderScale(
        PageInfo page,
        PdfViewport.ViewState? restoreView = null,
        float? requestedRenderScale = null,
        bool fitAfter = false)
    {
        float zoom = requestedRenderScale is > 0
            ? requestedRenderScale.Value
            : restoreView?.Zoom ?? (fitAfter
                ? ViewportRenderPolicy.SheetOverlayLowZoomRenderScale
                : CurrentViewportZoomForSheetOverlay(page));
        (float widthPt, float heightPt) = ReadSheetOverlaySourceSize(page);
        return ViewportRenderPolicy.SelectSheetOverlayRenderScale(zoom, widthPt, heightPt);
    }

    private float SelectSheetOverlayPageOpenFirstFrameRenderScale(
        PageInfo page,
        PdfViewport.ViewState? restoreView)
    {
        float selected = SelectSheetOverlayViewportRenderScale(page, restoreView, fitAfter: !restoreView.HasValue);
        return Math.Min(selected, ViewportRenderPolicy.SheetOverlayLowZoomRenderScale);
    }

    private float CurrentViewportZoomForSheetOverlay(PageInfo page)
    {
        PdfViewport.ViewState view = _viewport.CaptureViewState();
        if (_viewport.IsPageRenderReady(page.FolderPath))
            return view.Zoom;

        return Math.Max(1.0f, view.Zoom);
    }

    private static (float WidthPt, float HeightPt) ReadSheetOverlaySourceSize(PageInfo page)
    {
        if (string.IsNullOrWhiteSpace(page.OverlayPageFolder))
            return (0, 0);

        PdfSheetMetadata? metadata = OurPlaneCoreJobStore.ReadSourcePdfMetadata(page.OverlayPageFolder);
        if (metadata is not { WidthPt: > 0, HeightPt: > 0 })
            return (0, 0);

        return ((float)metadata.WidthPt, (float)metadata.HeightPt);
    }

    private (bool Ok, string Error) DrawPdfExportSheetOverlay(
        SKCanvas canvas,
        PageInfo page,
        float pageWidthPt,
        float pageHeightPt)
    {
        if (string.IsNullOrWhiteSpace(page.OverlayPageFolder) || !page.OverlayVisible)
            return (true, "");

        SKBitmap? bitmap = null;
        try
        {
            if (!TryBuildSheetOverlayBitmap(
                    page,
                    ViewportRenderPolicy.SheetOverlayExportRenderScale,
                    allowRender: true,
                    out bitmap,
                    out float overlayWidthPt,
                    out float overlayHeightPt,
                    out string overlayName,
                    out string error) ||
                bitmap == null)
            {
                return (false, $"Could not render overlay for sheet '{page.Name}': {error}");
            }

            using var paint = new SKPaint
            {
                IsAntialias = true,
                FilterQuality = SKFilterQuality.Medium,
            };
            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, pageWidthPt, pageHeightPt));
            canvas.Translate((float)page.OverlayOffsetXPt, (float)page.OverlayOffsetYPt);
            canvas.RotateDegrees((float)page.OverlayRotationDegrees);
            canvas.Scale((float)page.OverlayScale);
            canvas.DrawBitmap(bitmap, new SKRect(0, 0, overlayWidthPt, overlayHeightPt), paint);
            canvas.Restore();
            return (true, "");
        }
        finally
        {
            bitmap?.Dispose();
        }
    }

    private bool TryBuildSheetOverlayBitmap(
        PageInfo page,
        float renderScale,
        bool allowRender,
        out SKBitmap? overlayBitmap,
        out float widthPt,
        out float heightPt,
        out string overlayName,
        out string error)
    {
        overlayBitmap = null;
        widthPt = 0;
        heightPt = 0;
        overlayName = "";
        error = "";

        if (string.IsNullOrWhiteSpace(page.OverlayPageFolder))
            return false;

        if (!Directory.Exists(page.OverlayPageFolder))
        {
            error = "overlay sheet folder is missing.";
            return false;
        }

        PageInfo? overlayPage = OurPlaneCoreJobStore.TryReadPage(page.OverlayPageFolder);
        if (overlayPage == null)
        {
            error = "overlay sheet source is missing.";
            return false;
        }

        string cacheKey = BuildSheetOverlayCacheKey(page, overlayPage, renderScale);
        if (_sheetOverlayBitmapCache.TryGet(cacheKey, out SheetOverlayBitmapCache.Entry? cached) &&
            cached != null)
        {
            overlayBitmap = cached.Bitmap;
            widthPt = cached.WidthPt;
            heightPt = cached.HeightPt;
            overlayName = cached.OverlayName;
            return true;
        }

        if (SheetOverlayRenderCache.TryRead(
                page,
                overlayPage,
                renderScale,
                out SKBitmap? persisted,
                out widthPt,
                out heightPt) &&
            persisted != null)
        {
            overlayBitmap = persisted;
            overlayName = overlayPage.Name;
            _sheetOverlayBitmapCache.Put(cacheKey, overlayBitmap, widthPt, heightPt, overlayName);
            AppLog.Info(
                $"Sheet overlay cache hit; base='{page.FolderPath}'; overlay='{overlayPage.FolderPath}'; scale={renderScale:0.###}");
            return true;
        }

        if (!allowRender)
        {
            error = "overlay is not cached yet.";
            return false;
        }

        if (TryBuildSheetOverlayBitmapFromRasterSheet(
                page,
                overlayPage,
                renderScale,
                out overlayBitmap,
                out widthPt,
                out heightPt,
                out overlayName,
                out _) &&
            overlayBitmap != null)
        {
            _sheetOverlayBitmapCache.Put(cacheKey, overlayBitmap, widthPt, heightPt, overlayName);
            return true;
        }

        var layerStates = overlayPage.PdfLayers
            .GroupBy(layer => layer.Number)
            .ToDictionary(group => group.Key, group => group.First().IsOn);

        Stopwatch renderWatch = Stopwatch.StartNew();
        // Raw-file payload skips the ~1-1.5s PNG encode/decode round-trip a
        // full-sheet scale-2 render would otherwise pay.
        if (!PdfLayerRenderService.TryRender(
                overlayPage.PdfPath,
                overlayPage.PdfPage,
                renderScale: renderScale,
                layerStates,
                highlightedLayers: [],
                overlayPage.PdfLayersCached ? overlayPage.PdfLayers : null,
                clipRect: null,
                allowRawFullPage: false,
                preferRawFilePayload: true,
                out PdfLayerRenderResult render,
                out error))
        {
            return false;
        }
        renderWatch.Stop();
        if (renderWatch.ElapsedMilliseconds >= ViewportRenderPolicy.SlowRenderLogMs)
        {
            AppLog.Info(
                $"Sheet overlay render {renderWatch.ElapsedMilliseconds}ms; base='{page.FolderPath}'; " +
                $"overlay='{overlayPage.FolderPath}'; scale={renderScale:0.###}");
        }

        using SKBitmap? sourceBitmap = render.HasRawImage
            ? PdfLayerRenderService.CreateBitmapFromRawRender(render)
            : SKBitmap.Decode(render.ImageBytes);
        if (sourceBitmap == null)
        {
            error = "overlay raster could not be decoded.";
            return false;
        }

        overlayBitmap = BuildTintedSheetOverlayBitmap(sourceBitmap, page.OverlayColor, page.OverlayOpacity);
        widthPt = render.WidthPt;
        heightPt = render.HeightPt;
        overlayName = overlayPage.Name;
        _sheetOverlayBitmapCache.Put(cacheKey, overlayBitmap, widthPt, heightPt, overlayName);
        SheetOverlayRenderCache.TryWrite(page, overlayPage, renderScale, overlayBitmap, widthPt, heightPt);
        return true;
    }

    private bool TryBuildSheetOverlayBitmapFromRasterSheet(
        PageInfo page,
        PageInfo overlayPage,
        float renderScale,
        out SKBitmap? overlayBitmap,
        out float widthPt,
        out float heightPt,
        out string overlayName,
        out string error)
    {
        overlayBitmap = null;
        widthPt = 0;
        heightPt = 0;
        overlayName = "";
        error = "";

        RasterSheetSource? rasterSheet = overlayPage.RasterSheet;
        if (rasterSheet is not { Enabled: true } ||
            string.IsNullOrWhiteSpace(rasterSheet.Image) ||
            rasterSheet.RenderScale + 0.01 < renderScale)
        {
            error = "overlay raster sheet is not ready at the requested scale.";
            return false;
        }

        if (!RasterSheetCacheService.TryReadReady(
                overlayPage.FolderPath,
                overlayPage.PdfPath,
                rasterSheet,
                out RasterSheetBitmapResult result,
                out error))
        {
            return false;
        }

        using SKBitmap sourceBitmap = result.Bitmap;
        if (result.BitmapScale + 0.01f < renderScale)
        {
            error = "overlay raster sheet bitmap scale is below the requested scale.";
            return false;
        }

        using SKBitmap? scaledSourceBitmap = result.BitmapScale > renderScale * 1.05f
            ? ResizeSheetOverlaySourceBitmap(sourceBitmap, result.WidthPt, result.HeightPt, renderScale)
            : null;
        if (result.BitmapScale > renderScale * 1.05f && scaledSourceBitmap == null)
        {
            error = "overlay raster sheet could not be resized to the requested scale.";
            return false;
        }

        SKBitmap tintSource = scaledSourceBitmap ?? sourceBitmap;
        overlayBitmap = BuildTintedSheetOverlayBitmap(tintSource, page.OverlayColor, page.OverlayOpacity);
        widthPt = result.WidthPt;
        heightPt = result.HeightPt;
        overlayName = overlayPage.Name;
        QueueSheetOverlayRenderCacheWrite(page, overlayPage, renderScale, overlayBitmap, widthPt, heightPt);
        AppLog.Info(
            $"Sheet overlay raster cache hit; base='{page.FolderPath}'; overlay='{overlayPage.FolderPath}'; " +
            $"scale={renderScale:0.###}; sourceScale={result.BitmapScale:0.###}; bitmapScale={renderScale:0.###}");
        return true;
    }

    private static SKBitmap? ResizeSheetOverlaySourceBitmap(
        SKBitmap source,
        float widthPt,
        float heightPt,
        float renderScale)
    {
        if (source.Width <= 0 || source.Height <= 0 || widthPt <= 0 || heightPt <= 0 || renderScale <= 0)
            return null;

        int targetWidth = Math.Max(1, (int)Math.Round(widthPt * renderScale));
        int targetHeight = Math.Max(1, (int)Math.Round(heightPt * renderScale));
        if (targetWidth >= source.Width && targetHeight >= source.Height)
            return source.Copy();

        return source.Resize(
            new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul),
            SKFilterQuality.High);
    }

    private static void QueueSheetOverlayRenderCacheWrite(
        PageInfo page,
        PageInfo overlayPage,
        float renderScale,
        SKBitmap bitmap,
        float widthPt,
        float heightPt)
    {
        SKBitmap? snapshot = bitmap.Copy();
        if (snapshot == null)
            return;

        _ = Task.Run(() =>
        {
            using (snapshot)
                SheetOverlayRenderCache.TryWrite(page, overlayPage, renderScale, snapshot, widthPt, heightPt);
        });
    }

    private sealed record SheetOverlayBuildResult(
        bool Ok,
        SKBitmap? Bitmap,
        float WidthPt,
        float HeightPt,
        string OverlayName,
        string Error);

    private static string BuildSheetOverlayCacheKey(PageInfo page, PageInfo overlayPage, float renderScale)
    {
        var info = new FileInfo(overlayPage.PdfPath);
        return string.Join(
            '|',
            Path.GetFileName(info.FullName).ToLowerInvariant(),
            info.Exists ? info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) : "0",
            info.Exists ? info.Length.ToString(CultureInfo.InvariantCulture) : "0",
            overlayPage.PdfPage.ToString(CultureInfo.InvariantCulture),
            Math.Round(renderScale, 3).ToString(CultureInfo.InvariantCulture),
            SheetOverlayTintStyleVersion,
            page.OverlayColor,
            EffectiveSheetOverlayOpacity(page.OverlayOpacity).ToString("0.###", CultureInfo.InvariantCulture),
            string.Join(';', overlayPage.PdfLayers
                .OrderBy(layer => layer.Number)
                .Select(layer => $"{layer.Number}:{layer.IsOn}:{layer.Name}")));
    }

    private void TogglePageOverlayVisibility(PageInfo page)
    {
        bool visible = !page.OverlayVisible;
        OurPlaneCoreJobStore.SavePageOverlayVisibility(page.FolderPath, visible);
        PageInfo updated = OurPlaneCoreJobStore.TryReadPage(page.FolderPath) ?? page;
        if (_currentPage != null && SameFolder(_currentPage.FolderPath, page.FolderPath))
        {
            _currentPage = updated;
            LoadSheetOverlay(updated);
        }

        RefreshPageOverlayTreeNode(updated);
        TxtStatus.Text = visible
            ? $"Sheet overlay shown on {updated.Name}."
            : $"Sheet overlay hidden on {updated.Name}.";
    }

    private static SKBitmap BuildTintedSheetOverlayBitmap(SKBitmap source, string colorHex, double opacity)
    {
        SKColor color = BuildBrightSheetOverlayColor(ParseOverlayColor(colorHex));
        double alphaScale = EffectiveSheetOverlayOpacity(opacity);
        var tinted = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (source.ColorType == SKColorType.Bgra8888)
        {
            // Byte path: the SKColor[] route materializes two pixel arrays plus
            // per-element conversion, which costs hundreds of ms on full-sheet
            // scale-2 bitmaps.
            ReadOnlySpan<byte> src = source.GetPixelSpan();
            byte[] dst = new byte[src.Length];
            for (int i = 0; i + 3 < src.Length; i += 4)
            {
                byte b = src[i];
                byte g = src[i + 1];
                byte r = src[i + 2];
                byte a = src[i + 3];
                int whiteDistance = Math.Max(Math.Max(255 - r, 255 - g), 255 - b);
                int alpha = (int)Math.Round(whiteDistance * alphaScale * (a / 255.0) * SheetOverlayAlphaBoost);
                alpha = Math.Clamp(alpha, 0, 255);
                if (alpha < 3)
                    continue;

                // Destination is premultiplied BGRA.
                dst[i] = (byte)(color.Blue * alpha / 255);
                dst[i + 1] = (byte)(color.Green * alpha / 255);
                dst[i + 2] = (byte)(color.Red * alpha / 255);
                dst[i + 3] = (byte)alpha;
            }

            System.Runtime.InteropServices.Marshal.Copy(dst, 0, tinted.GetPixels(), dst.Length);
            return tinted;
        }

        SKColor[] sourcePixels = source.Pixels;
        SKColor[] tintedPixels = new SKColor[sourcePixels.Length];

        for (int i = 0; i < sourcePixels.Length; i++)
        {
            SKColor pixel = sourcePixels[i];
            int whiteDistance = Math.Max(
                Math.Max(255 - pixel.Red, 255 - pixel.Green),
                255 - pixel.Blue);
            int alpha = (int)Math.Round(whiteDistance * alphaScale * (pixel.Alpha / 255.0) * SheetOverlayAlphaBoost);
            alpha = Math.Clamp(alpha, 0, 255);
            if (alpha >= 3)
                tintedPixels[i] = new SKColor(color.Red, color.Green, color.Blue, (byte)alpha);
        }

        tinted.Pixels = tintedPixels;
        return tinted;
    }

    private string CurrentSheetOverlayColor() =>
        string.IsNullOrWhiteSpace(_currentPage?.OverlayColor)
            ? DefaultSheetOverlayColor
            : _currentPage!.OverlayColor;

    private double CurrentSheetOverlayOpacity()
    {
        double opacity = _currentPage?.OverlayOpacity ?? DefaultSheetOverlayOpacity;
        return double.IsNaN(opacity) || double.IsInfinity(opacity) || opacity <= 0
            ? DefaultSheetOverlayOpacity
            : EffectiveSheetOverlayOpacity(opacity);
    }

    private static double EffectiveSheetOverlayOpacity(double opacity) =>
        Math.Clamp(
            double.IsNaN(opacity) || double.IsInfinity(opacity) || opacity <= 0
                ? DefaultSheetOverlayOpacity
                : Math.Max(opacity, MinimumBrightSheetOverlayOpacity),
            0.05,
            1.0);

    private static SKColor BuildBrightSheetOverlayColor(SKColor color)
    {
        static byte Boost(byte value)
        {
            if (value < 8)
                return 0;

            return (byte)Math.Clamp((int)Math.Round(value * 1.28 + 34), 0, 255);
        }

        return new SKColor(Boost(color.Red), Boost(color.Green), Boost(color.Blue));
    }

    private static SKColor ParseOverlayColor(string colorHex)
    {
        try
        {
            return SKColor.Parse(string.IsNullOrWhiteSpace(colorHex) ? DefaultSheetOverlayColor : colorHex);
        }
        catch
        {
            return SKColor.Parse(DefaultSheetOverlayColor);
        }
    }

    private static bool SameFolder(string left, string right)
    {
        try
        {
            string l = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string r = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(l, r, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

}
