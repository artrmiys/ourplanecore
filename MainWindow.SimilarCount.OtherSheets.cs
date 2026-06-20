using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

public partial class MainWindow
{
    // One other sheet's matches for the boxed template, captured at the
    // requested threshold so broad scans get narrower as the threshold rises.
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
        public required SKPoint TemplateFromTextOffset { get; init; }
    }

    // Render every other sheet at the same pixel-per-point scale the template
    // was boxed at, then run the template against each. Renders go through the
    // shared render cache; matching uses the same offline matcher. Bounded by
    // SimilarCountMaxSweepSheets so a huge job cannot stall the review.
    private async Task<(
        List<SimilarSheetSweep> Sweeps,
        int SkippedOverLimit,
        int TextGuideSkippedSheets,
        int TextTemplateFallbackSkippedSheets,
        int TextRejectedSheets,
        int TextRejectedCandidates)> SweepOtherSimilarSheetsAsync(
        OurPlaneCoreJob job,
        ViewportSimilarCountRequest request,
        PdfSimilarTextResult? textResult,
        PdfSimilarTextMatch? textAnchor,
        SimilarSymbolMatchSession session,
        float bitmapScale,
        float threshold,
        bool rotations,
        bool mirrored,
        bool textTemplateFallback,
        CancellationToken cancellationToken)
    {
        var sweeps = new List<SimilarSheetSweep>();
        if (bitmapScale <= 0)
            return (sweeps, 0, 0, 0, 0, 0);

        List<PageInfo> pages = CollectPagesUnder(job.PagesRoot)
            .Where(page => !IsSamePageFolder(page.FolderPath, request.PageFolder))
            .ToList();

        int skipped = 0;
        if (pages.Count > SimilarCountMaxSweepSheets)
        {
            skipped = pages.Count - SimilarCountMaxSweepSheets;
            pages = pages.Take(SimilarCountMaxSweepSheets).ToList();
        }

        float minScore = Math.Clamp(threshold, (float)AppSettingsStore.SimilarCountThresholdMin, 1f);
        int textGuidedPages = 0;
        int textGuidedMatches = 0;
        int textGuidedSkippedNoText = 0;
        int textGuidedRejectedByRaster = 0;
        int textGuidedRejectedCandidates = 0;
        bool useTextGuide = CanUseOtherSheetTextGuide(request, textResult, textAnchor);
        bool textGuideRequired = request.UseTextCandidateRasterMatches && !request.AllowExactTextMatches;
        if (textGuideRequired && textTemplateFallback)
        {
            AppLog.Info(
                $"Similar count all-sheets skipped text-template fallback auto-add; sheets={pages.Count}; page='{request.PageFolder}'");
            return (sweeps, skipped, 0, pages.Count, 0, 0);
        }

        if (textGuideRequired && !useTextGuide)
        {
            AppLog.Info(
                $"Similar count all-sheets skipped visual-only Beam/Openings sweep; sheets={pages.Count}; page='{request.PageFolder}'");
            return (sweeps, skipped, pages.Count, 0, 0, 0);
        }

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
                    minScore,
                    rotations,
                    mirrored,
                    cancellationToken),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (rejectedByRaster)
            {
                textGuidedRejectedByRaster++;
                textGuidedRejectedCandidates += textGuide?.PageText.Matches.Count ?? 0;
            }
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

        if (textGuidedPages > 0 || textGuidedSkippedNoText > 0 || textGuidedRejectedByRaster > 0)
        {
            AppLog.Info(
                $"Similar count all-sheets text-guided raster matches; query='{textResult?.Query}'; sheets={textGuidedPages}; matches={textGuidedMatches}; skippedNoText={textGuidedSkippedNoText}; rejectedByRaster={textGuidedRejectedByRaster}; rejectedTextCandidates={textGuidedRejectedCandidates}; page='{request.PageFolder}'");
        }

        return (sweeps, skipped, textGuidedSkippedNoText, 0, textGuidedRejectedByRaster, textGuidedRejectedCandidates);
    }

    private static (List<SimilarSymbolMatch> Matches, bool TextGuided, bool RejectedByRaster) FindOtherSheetSimilarMatches(
        OtherSheetTextGuide? textGuide,
        ViewportSimilarCountRequest request,
        SimilarSymbolMatchSession session,
        SKBitmap bitmap,
        double pxPerPt,
        float minScore,
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
                bitmap,
                pxPerPt,
                minScore,
                rotations,
                mirrored,
                cancellationToken);
            if (textGuided.Count > 0)
                return (textGuided, true, false);

            return ([], true, true);
        }

        return (
            session.FindMatchesOnBitmap(bitmap, minScore, rotations, mirrored, cancellationToken),
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

        if (SimilarCountTextResultTooBroad(pageText))
            return null;

        SKPoint templateFromTextOffset = SimilarCountTextTemplateOffset(sourceTextAnchor, request);
        return new OtherSheetTextGuide
        {
            PageText = pageText,
            TemplateFromTextOffset = templateFromTextOffset,
        };
    }

    private static SKPoint SimilarCountTextTemplateOffset(
        PdfSimilarTextMatch sourceTextAnchor,
        ViewportSimilarCountRequest request)
    {
        SKPoint template = request.TemplateAnchorPdf ?? RectCenter(request.PdfRect);
        return new SKPoint(template.X - sourceTextAnchor.Center.X, template.Y - sourceTextAnchor.Center.Y);
    }

    private static List<SimilarSymbolMatch> FindOtherSheetTextGuidedRasterMatches(
        OtherSheetTextGuide textGuide,
        ViewportSimilarCountRequest request,
        SimilarSymbolMatchSession session,
        SKBitmap bitmap,
        double pxPerPt,
        float minScore,
        bool rotations,
        bool mirrored,
        CancellationToken cancellationToken)
    {
        var candidateCenters = SimilarCountTextCandidateCentersPdf(textGuide.PageText, textGuide.TemplateFromTextOffset)
            .Select(center => new SKPoint(
                (float)(center.X * pxPerPt),
                (float)(center.Y * pxPerPt)))
            .ToList();
        int radiusPixels = SimilarCountTextCandidateSearchRadiusPixels(request, (float)pxPerPt);
        return session.FindMatchesNearCentersOnBitmap(
            bitmap,
            candidateCenters,
            radiusPixels,
            minScore,
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
