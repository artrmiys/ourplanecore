using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

public partial class MainWindow
{
    private const int SheetOverlayAutoFitMinimumGeometry = 12;

    private async void AutoFitSheetOverlay(PageInfo page)
    {
        PageInfo? targetPage = OurPlaneCoreJobStore.TryReadPage(page.FolderPath);
        if (targetPage == null)
        {
            TxtStatus.Text = "Overlay auto fit: sheet source is missing.";
            return;
        }

        if (string.IsNullOrWhiteSpace(targetPage.OverlayPageFolder) || !targetPage.OverlayVisible)
        {
            TxtStatus.Text = "Set and show a sheet overlay before using Auto Fit.";
            return;
        }

        PageInfo? overlayPage = OurPlaneCoreJobStore.TryReadPage(targetPage.OverlayPageFolder);
        if (overlayPage == null)
        {
            TxtStatus.Text = "Overlay auto fit: overlay sheet source is missing.";
            return;
        }

        TxtStatus.Text = "Overlay auto fit: reading matching plan geometry and raster features...";
        try
        {
            SheetOverlayAutoFitReadResult read = await Task.Run(() =>
                ReadSheetOverlayAutoFitGeometry(targetPage, overlayPage));
            if (!read.Ok)
            {
                TxtStatus.Text = read.Error;
                return;
            }

            if (!SheetOverlayAutoFitService.TryFit(read.BaseSnap, read.OverlaySnap, out SheetOverlayAutoFitResult fit))
            {
                TxtStatus.Text = fit.Message;
                return;
            }

            PageInfo? latestTarget = OurPlaneCoreJobStore.TryReadPage(targetPage.FolderPath);
            if (latestTarget == null ||
                string.IsNullOrWhiteSpace(latestTarget.OverlayPageFolder) ||
                !SameFolder(latestTarget.OverlayPageFolder, overlayPage.FolderPath))
            {
                TxtStatus.Text = "Overlay auto fit skipped: overlay changed while fitting.";
                return;
            }

            OurPlaneCoreJobStore.SavePageOverlayTransform(
                latestTarget.FolderPath,
                fit.OffsetXPt,
                fit.OffsetYPt,
                fit.OverlayScale,
                fit.OverlayRotationDegrees);
            if (OurPlaneCoreJobStore.TryReadPage(latestTarget.FolderPath) is { } updatedTarget)
                SyncReciprocalSheetOverlay(updatedTarget);
            RefreshPageOverlayState(latestTarget.FolderPath, fit.Message);
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Sheet overlay auto fit failed for {targetPage.Name}");
            TxtStatus.Text = $"Overlay auto fit failed: {ex.Message}";
        }
    }

    private static SheetOverlayAutoFitReadResult ReadSheetOverlayAutoFitGeometry(
        PageInfo basePage,
        PageInfo overlayPage)
    {
        SheetOverlayAutoFitSnapRead baseRead = ReadSheetOverlayAutoFitSnap(basePage);
        SheetOverlayAutoFitSnapRead overlayRead = ReadSheetOverlayAutoFitSnap(overlayPage);
        if (baseRead.Ok && overlayRead.Ok)
            return new SheetOverlayAutoFitReadResult(true, baseRead.Snap, overlayRead.Snap, "");

        SheetOverlayAutoFitSnapRead baseRasterRead = baseRead.Ok
            ? baseRead
            : ReadSheetOverlayAutoFitRasterSnap(basePage);
        if (!baseRasterRead.Ok)
            return SheetOverlayAutoFitReadResult.Failed(
                $"Overlay auto fit: base sheet geometry unavailable. {baseRead.Error} Raster fallback: {baseRasterRead.Error}");

        SheetOverlayAutoFitSnapRead overlayRasterRead = overlayRead.Ok
            ? overlayRead
            : ReadSheetOverlayAutoFitRasterSnap(overlayPage);
        if (!overlayRasterRead.Ok)
            return SheetOverlayAutoFitReadResult.Failed(
                $"Overlay auto fit: overlay geometry unavailable. {overlayRead.Error} Raster fallback: {overlayRasterRead.Error}");

        return new SheetOverlayAutoFitReadResult(true, baseRasterRead.Snap, overlayRasterRead.Snap, "");
    }

    private static SheetOverlayAutoFitSnapRead ReadSheetOverlayAutoFitSnap(PageInfo page)
    {
        if (RasterSheetCacheService.TryReadSnapIndex(
                page.FolderPath,
                page.PdfPath,
                page.RasterSheet,
                out PdfGeometrySnapResult rasterSnap,
                out _) &&
            HasAutoFitGeometry(rasterSnap))
        {
            return new SheetOverlayAutoFitSnapRead(true, rasterSnap, "");
        }

        IReadOnlyList<PdfLayerInfo>? layers = page.PdfLayersCached ? page.PdfLayers : null;
        if (PdfGeometrySnapService.TryReadSnapPoints(
                page.PdfPath,
                page.PdfPage,
                layers,
                blackOnly: true,
                out PdfGeometrySnapResult blackSnap,
                out string blackError) &&
            HasAutoFitGeometry(blackSnap))
        {
            return new SheetOverlayAutoFitSnapRead(true, blackSnap, "");
        }

        if (PdfGeometrySnapService.TryReadSnapPoints(
                page.PdfPath,
                page.PdfPage,
                layers,
                blackOnly: false,
                out PdfGeometrySnapResult snap,
                out string error) &&
            HasAutoFitGeometry(snap))
        {
            return new SheetOverlayAutoFitSnapRead(true, snap, "");
        }

        string reason = !string.IsNullOrWhiteSpace(error)
            ? error
            : !string.IsNullOrWhiteSpace(blackError)
                ? blackError
                : "not enough PDF linework was found.";
        return new SheetOverlayAutoFitSnapRead(false, new PdfGeometrySnapResult(), reason);
    }

    private static SheetOverlayAutoFitSnapRead ReadSheetOverlayAutoFitRasterSnap(PageInfo page)
    {
        if (RasterSheetCacheService.TryReadReady(
                page.FolderPath,
                page.PdfPath,
                page.RasterSheet,
                out RasterSheetBitmapResult raster,
                out string rasterError))
        {
            using SKBitmap bitmap = raster.Bitmap;
            if (SheetOverlayRasterFeatureService.TryExtractSnap(
                    bitmap,
                    raster.WidthPt,
                    raster.HeightPt,
                    out PdfGeometrySnapResult rasterSnap,
                    out string featureError) &&
                HasAutoFitGeometry(rasterSnap))
            {
                return new SheetOverlayAutoFitSnapRead(true, rasterSnap, "");
            }

            rasterError = string.IsNullOrWhiteSpace(featureError)
                ? "not enough raster linework was found."
                : featureError;
        }

        if (!TryRenderSheetOverlayAutoFitRaster(page, out SKBitmap? renderedBitmap, out float widthPt, out float heightPt, out string renderError) ||
            renderedBitmap == null)
        {
            string reason = !string.IsNullOrWhiteSpace(rasterError)
                ? rasterError
                : renderError;
            return new SheetOverlayAutoFitSnapRead(false, new PdfGeometrySnapResult(), reason);
        }

        using (renderedBitmap)
        {
            if (SheetOverlayRasterFeatureService.TryExtractSnap(
                    renderedBitmap,
                    widthPt,
                    heightPt,
                    out PdfGeometrySnapResult snap,
                    out string featureError) &&
                HasAutoFitGeometry(snap))
            {
                return new SheetOverlayAutoFitSnapRead(true, snap, "");
            }

            string reason = !string.IsNullOrWhiteSpace(featureError)
                ? featureError
                : "not enough raster linework was found.";
            return new SheetOverlayAutoFitSnapRead(false, new PdfGeometrySnapResult(), reason);
        }
    }

    private static bool TryRenderSheetOverlayAutoFitRaster(
        PageInfo page,
        out SKBitmap? bitmap,
        out float widthPt,
        out float heightPt,
        out string error)
    {
        bitmap = null;
        widthPt = 0;
        heightPt = 0;
        error = "";

        IReadOnlyList<PdfLayerInfo>? layers = page.PdfLayersCached ? page.PdfLayers : null;
        Dictionary<int, bool> layerStates = (layers ?? [])
            .GroupBy(layer => layer.Number)
            .ToDictionary(group => group.Key, group => group.First().IsOn);

        if (!PdfLayerRenderService.TryRender(
                page.PdfPath,
                page.PdfPage,
                renderScale: 1.0,
                layerStates,
                highlightedLayers: [],
                layers,
                clipRect: null,
                allowRawFullPage: false,
                preferRawFilePayload: true,
                out PdfLayerRenderResult render,
                out error))
        {
            return false;
        }

        bitmap = render.HasRawImage
            ? PdfLayerRenderService.CreateBitmapFromRawRender(render)
            : SKBitmap.Decode(render.ImageBytes);
        if (bitmap == null)
        {
            error = "rendered raster could not be decoded.";
            return false;
        }

        widthPt = render.WidthPt;
        heightPt = render.HeightPt;
        return true;
    }

    private static bool HasAutoFitGeometry(PdfGeometrySnapResult snap) =>
        snap.Points.Count + snap.Segments.Count >= SheetOverlayAutoFitMinimumGeometry;

    private sealed record SheetOverlayAutoFitReadResult(
        bool Ok,
        PdfGeometrySnapResult BaseSnap,
        PdfGeometrySnapResult OverlaySnap,
        string Error)
    {
        public static SheetOverlayAutoFitReadResult Failed(string error) =>
            new(false, new PdfGeometrySnapResult(), new PdfGeometrySnapResult(), error);
    }

    private sealed record SheetOverlayAutoFitSnapRead(
        bool Ok,
        PdfGeometrySnapResult Snap,
        string Error);
}
