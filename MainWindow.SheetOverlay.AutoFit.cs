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

            bool fitted = SheetOverlayAutoFitService.TryFit(read.BaseSnap, read.OverlaySnap, out SheetOverlayAutoFitResult fit);
            if (!fitted && !read.IsPureRaster)
            {
                SheetOverlayAutoFitReadResult rasterRetry = ReadSheetOverlayAutoFitRasterGeometry(targetPage, overlayPage);
                SheetOverlayAutoFitResult rasterFit = default!;
                if (rasterRetry.Ok &&
                    SheetOverlayAutoFitService.TryFit(rasterRetry.BaseSnap, rasterRetry.OverlaySnap, out rasterFit))
                {
                    read = rasterRetry;
                    fit = rasterFit;
                    fitted = true;
                }
                else if (!rasterRetry.Ok)
                {
                    fit = fit with { Message = $"{fit.Message} Raster fallback: {rasterRetry.Error}" };
                }
                else
                {
                    fit = fit with { Message = $"{fit.Message} Raster fallback also failed: {rasterFit.Message}" };
                }
            }

            if (!fitted)
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
            string status = BuildSheetOverlayAutoFitStatus(read, fit);
            AppLog.Info(
                $"Sheet overlay auto fit applied; base='{latestTarget.FolderPath}'; overlay='{overlayPage.FolderPath}'; " +
                $"source='{read.SourceSummary}'; matched={fit.MatchedSamples}/{fit.SampleCount}; " +
                $"confidence={fit.Confidence:0.###}; scale={fit.OverlayScale:0.###}; rotation={fit.OverlayRotationDegrees:0.###}");
            RefreshPageOverlayState(latestTarget.FolderPath, status);
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
            return SheetOverlayAutoFitReadResult.Success(baseRead, overlayRead);

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

        return SheetOverlayAutoFitReadResult.Success(baseRasterRead, overlayRasterRead);
    }

    private static SheetOverlayAutoFitReadResult ReadSheetOverlayAutoFitRasterGeometry(
        PageInfo basePage,
        PageInfo overlayPage)
    {
        SheetOverlayAutoFitSnapRead baseRead = ReadSheetOverlayAutoFitRasterSnap(basePage);
        if (!baseRead.Ok)
            return SheetOverlayAutoFitReadResult.Failed($"Overlay auto fit raster fallback: base sheet unavailable. {baseRead.Error}");

        SheetOverlayAutoFitSnapRead overlayRead = ReadSheetOverlayAutoFitRasterSnap(overlayPage);
        if (!overlayRead.Ok)
            return SheetOverlayAutoFitReadResult.Failed($"Overlay auto fit raster fallback: overlay sheet unavailable. {overlayRead.Error}");

        return SheetOverlayAutoFitReadResult.Success(baseRead, overlayRead);
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
            return new SheetOverlayAutoFitSnapRead(true, rasterSnap, "", "raster snap index");
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
            return new SheetOverlayAutoFitSnapRead(true, blackSnap, "", "PDF black linework");
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
            return new SheetOverlayAutoFitSnapRead(true, snap, "", "PDF linework");
        }

        string reason = !string.IsNullOrWhiteSpace(error)
            ? error
            : !string.IsNullOrWhiteSpace(blackError)
                ? blackError
                : "not enough PDF linework was found.";
        return new SheetOverlayAutoFitSnapRead(false, new PdfGeometrySnapResult(), reason, "");
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
                return new SheetOverlayAutoFitSnapRead(true, rasterSnap, "", "ready raster image");
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
            return new SheetOverlayAutoFitSnapRead(false, new PdfGeometrySnapResult(), reason, "");
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
                return new SheetOverlayAutoFitSnapRead(true, snap, "", "rendered raster image");
            }

            string reason = !string.IsNullOrWhiteSpace(featureError)
                ? featureError
                : "not enough raster linework was found.";
            return new SheetOverlayAutoFitSnapRead(false, new PdfGeometrySnapResult(), reason, "");
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

    private static string BuildSheetOverlayAutoFitStatus(
        SheetOverlayAutoFitReadResult read,
        SheetOverlayAutoFitResult fit) =>
        read.IsPureRaster
            ? $"Overlay auto fit (raster image, {fit.Method}): {fit.MatchedSamples}/{fit.SampleCount} samples matched, confidence {fit.Confidence * 100:0}%, scale {fit.OverlayScale:0.###}x, rotation {fit.OverlayRotationDegrees:0.###} deg."
            : $"Overlay auto fit ({read.SourceSummary}, {fit.Method}): {fit.MatchedSamples}/{fit.SampleCount} samples matched, confidence {fit.Confidence * 100:0}%, scale {fit.OverlayScale:0.###}x, rotation {fit.OverlayRotationDegrees:0.###} deg.";

    private sealed record SheetOverlayAutoFitReadResult(
        bool Ok,
        PdfGeometrySnapResult BaseSnap,
        PdfGeometrySnapResult OverlaySnap,
        string Error,
        string BaseSource,
        string OverlaySource)
    {
        public static SheetOverlayAutoFitReadResult Failed(string error) =>
            new(false, new PdfGeometrySnapResult(), new PdfGeometrySnapResult(), error, "", "");

        public static SheetOverlayAutoFitReadResult Success(
            SheetOverlayAutoFitSnapRead baseRead,
            SheetOverlayAutoFitSnapRead overlayRead) =>
            new(true, baseRead.Snap, overlayRead.Snap, "", baseRead.Source, overlayRead.Source);

        public bool IsPureRaster =>
            IsRasterSource(BaseSource) && IsRasterSource(OverlaySource);

        public string SourceSummary =>
            string.Equals(BaseSource, OverlaySource, StringComparison.OrdinalIgnoreCase)
                ? BaseSource
                : $"base {BaseSource}, overlay {OverlaySource}";

        private static bool IsRasterSource(string source) =>
            source.Contains("raster", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record SheetOverlayAutoFitSnapRead(
        bool Ok,
        PdfGeometrySnapResult Snap,
        string Error,
        string Source);
}
