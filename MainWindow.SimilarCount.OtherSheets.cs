using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

public partial class MainWindow
{
    // One other sheet's matches for the boxed template, captured at the loose
    // floor so threshold changes re-filter instantly without re-rendering.
    private sealed class SimilarSheetSweep
    {
        public required string PageFolder { get; init; }
        public required string PageName { get; init; }
        public required double ScaleMetersPerPt { get; init; }
        public required List<(SKPoint CenterPdf, float Score)> Hits { get; init; }
    }

    private sealed class OtherSheetTextGuide
    {
        public required PdfSimilarTextResult PageText { get; init; }
        public required SKPoint MarkerFromTextOffset { get; init; }
    }

    // Render every other sheet at the same pixel-per-point scale the template
    // was boxed at, then run the template against each. Renders go through the
    // shared render cache; matching uses the same offline matcher. Bounded by
    // SimilarCountMaxSweepSheets so a huge job cannot stall the review.
    private async Task<(List<SimilarSheetSweep> Sweeps, int SkippedOverLimit)> SweepOtherSimilarSheetsAsync(
        OurPlaneCoreJob job,
        ViewportSimilarCountRequest request,
        PdfSimilarTextResult? textResult,
        PdfSimilarTextMatch? textAnchor,
        SimilarSymbolMatchSession session,
        float bitmapScale,
        bool rotations,
        bool mirrored,
        CancellationToken cancellationToken)
    {
        var sweeps = new List<SimilarSheetSweep>();
        if (bitmapScale <= 0)
            return (sweeps, 0);

        List<PageInfo> pages = CollectPagesUnder(job.PagesRoot)
            .Where(page => !IsSamePageFolder(page.FolderPath, request.PageFolder))
            .ToList();

        int skipped = 0;
        if (pages.Count > SimilarCountMaxSweepSheets)
        {
            skipped = pages.Count - SimilarCountMaxSweepSheets;
            pages = pages.Take(SimilarCountMaxSweepSheets).ToList();
        }

        float floor = (float)AppSettingsStore.SimilarCountThresholdMin;
        int textGuidedPages = 0;
        int textGuidedMatches = 0;
        int textGuidedSkippedNoText = 0;
        int textGuidedRejectedByRaster = 0;
        bool useTextGuide = CanUseOtherSheetTextGuide(request, textResult, textAnchor);
        foreach (PageInfo page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(page.PdfPath))
                continue;

            OtherSheetTextGuide? textGuide = null;
            if (useTextGuide)
            {
                textGuide = TryBuildOtherSheetTextGuide(page, textResult!, textAnchor!, request);
                if (textGuide == null)
                {
                    textGuidedSkippedNoText++;
                    continue;
                }
            }

            (bool ok, PdfLayerRenderResult render, _) = await PdfLayerRenderService.TryRenderAsync(
                page.PdfPath,
                page.PdfPage,
                bitmapScale,
                new Dictionary<int, bool>(),
                [],
                page.PdfLayersCached ? page.PdfLayers : null);
            if (!ok || render.ImageBytes.Length == 0 || render.WidthPt <= 0)
                continue;

            using SKBitmap? bitmap = SKBitmap.Decode(render.ImageBytes);
            if (bitmap == null || bitmap.Width <= 0)
                continue;

            double pxPerPt = bitmap.Width / render.WidthPt;
            if (pxPerPt <= 0)
                continue;

            (List<SimilarSymbolMatch> matches, bool textGuided, bool rejectedByRaster) = await Task.Run(
                () => FindOtherSheetSimilarMatches(
                    textGuide,
                    request,
                    session,
                    bitmap,
                    pxPerPt,
                    floor,
                    rotations,
                    mirrored,
                    cancellationToken),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (rejectedByRaster)
                textGuidedRejectedByRaster++;
            if (matches.Count == 0)
                continue;
            if (textGuided)
            {
                textGuidedPages++;
                textGuidedMatches += matches.Count;
            }

            var hits = matches
                .Select(match => (
                    new SKPoint((float)(match.CenterX / pxPerPt), (float)(match.CenterY / pxPerPt)),
                    match.Score))
                .ToList();
            sweeps.Add(new SimilarSheetSweep
            {
                PageFolder = page.FolderPath,
                PageName = page.Name,
                ScaleMetersPerPt = page.ScaleMetersPerPt,
                Hits = hits,
            });
        }

        if (textGuidedPages > 0)
        {
            AppLog.Info(
                $"Similar count all-sheets text-guided raster matches; query='{textResult?.Query}'; sheets={textGuidedPages}; matches={textGuidedMatches}; skippedNoText={textGuidedSkippedNoText}; rejectedByRaster={textGuidedRejectedByRaster}; page='{request.PageFolder}'");
        }

        return (sweeps, skipped);
    }

    private static (List<SimilarSymbolMatch> Matches, bool TextGuided, bool RejectedByRaster) FindOtherSheetSimilarMatches(
        OtherSheetTextGuide? textGuide,
        ViewportSimilarCountRequest request,
        SimilarSymbolMatchSession session,
        SKBitmap bitmap,
        double pxPerPt,
        float floor,
        bool rotations,
        bool mirrored,
        CancellationToken cancellationToken)
    {
        if (textGuide != null)
        {
            List<SimilarSymbolMatch> textGuided = FindOtherSheetTextGuidedRasterMatches(
                textGuide,
                request,
                session,
                pxPerPt,
                floor,
                rotations,
                mirrored,
                cancellationToken);
            if (textGuided.Count > 0)
                return (textGuided, true, false);

            return ([], true, true);
        }

        return (
            session.FindMatchesOnBitmap(bitmap, floor, rotations, mirrored, cancellationToken),
            false,
            false);
    }

    private static bool CanUseOtherSheetTextGuide(
        ViewportSimilarCountRequest request,
        PdfSimilarTextResult? sourceTextResult,
        PdfSimilarTextMatch? sourceTextAnchor) =>
        (request.UseTextCandidateRasterMatches || request.AllowExactTextMatches) &&
        sourceTextResult != null &&
        sourceTextAnchor != null &&
        !string.IsNullOrWhiteSpace(sourceTextResult.Query);

    private static OtherSheetTextGuide? TryBuildOtherSheetTextGuide(
        PageInfo page,
        PdfSimilarTextResult sourceTextResult,
        PdfSimilarTextMatch sourceTextAnchor,
        ViewportSimilarCountRequest request)
    {
        if (!PdfSimilarTextService.TryFindSimilarTextByQuery(
                page.PdfPath,
                page.PdfPage,
                sourceTextResult.Query,
                out PdfSimilarTextResult pageText,
                out _) ||
            pageText.Matches.Count == 0)
        {
            return null;
        }

        SKPoint markerFromTextOffset = request.MarkerCenterPdf is { } marker
            ? new SKPoint(marker.X - sourceTextAnchor.Center.X, marker.Y - sourceTextAnchor.Center.Y)
            : default;
        return new OtherSheetTextGuide
        {
            PageText = pageText,
            MarkerFromTextOffset = markerFromTextOffset,
        };
    }

    private static List<SimilarSymbolMatch> FindOtherSheetTextGuidedRasterMatches(
        OtherSheetTextGuide textGuide,
        ViewportSimilarCountRequest request,
        SimilarSymbolMatchSession session,
        double pxPerPt,
        float floor,
        bool rotations,
        bool mirrored,
        CancellationToken cancellationToken)
    {
        var candidateCenters = textGuide.PageText.Matches
            .Select(match => new SKPoint(
                (float)((match.Center.X + textGuide.MarkerFromTextOffset.X) * pxPerPt),
                (float)((match.Center.Y + textGuide.MarkerFromTextOffset.Y) * pxPerPt)))
            .ToList();
        int radiusPixels = SimilarCountTextCandidateSearchRadiusPixels(request, (float)pxPerPt);
        return session.FindMatchesNearCenters(
            candidateCenters,
            radiusPixels,
            floor,
            rotations,
            mirrored,
            cancellationToken);
    }

    // Add the resolved off-sheet matches to the destination item. These sheets
    // are not open, so there is no per-marker review and no viewport call; the
    // markers persist with each sheet's own page folder and scale.
    private (int Sheets, int Markers) AddOtherSheetSimilarCounts(
        TakeoffItem item,
        IReadOnlyList<(SimilarSheetSweep Sweep, List<SKPoint> Centers)> additions)
    {
        int sheets = 0;
        int markers = 0;
        var touchedFolders = new List<string>();
        foreach ((SimilarSheetSweep sweep, List<SKPoint> centers) in additions)
        {
            var fresh = centers
                .Where(center => !IsSimilarCountDuplicateCenter(item, sweep.PageFolder, center))
                .ToList();
            if (fresh.Count == 0)
                continue;

            foreach (SKPoint center in fresh)
            {
                item.Measurements.Add(new Measurement
                {
                    MType = "point",
                    Points = [center],
                    Color = item.Color,
                    CountSymbol = CountDisplaySymbol.Normalize(item.CountSymbol),
                    PageFolder = sweep.PageFolder,
                    TakeoffFolder = item.FolderPath,
                    ScaleMetersPerPt = sweep.ScaleMetersPerPt,
                });
            }

            sheets++;
            markers += fresh.Count;
            touchedFolders.Add(sweep.PageFolder);
        }

        if (markers == 0)
            return (0, 0);

        OurPlaneCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        RefreshTreeItem(item);
        QueueTakeoffAutosave(item);
        using (UsePageMeasurementLookup())
        {
            RefreshTakeoffRowVisualsForItems(new[] { item });
            foreach (string folder in touchedFolders)
                RefreshPageTakeoffIndicatorsForFolder(folder);
            RefreshSheetLegend();
        }
        UpdateTotalDisplay();
        return (sheets, markers);
    }
}
