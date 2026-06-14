using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

namespace OurPlaneCore;

// Offline "count similar symbols": the user boxes one symbol on the rendered
// sheet and the matcher finds every visually similar occurrence on the page.
// No network, no ML runtime — plan drawings are thin dark linework on white,
// so binarized bit-parallel matching is both fast and robust:
//
//   1. Binarize the page raster to 1bpp "ink" rows packed into ulongs.
//   2. Dilate ink by one pixel so hairline strokes tolerate ±1 px offsets.
//   3. Score each candidate window with a strict agreement:
//        score = min(|T ∧ dilate(P)| / |T|,          template coverage
//                    |P ∧ dilate(T)| / |P|,          window precision
//                    sqrt(min(|T|,|P|) / max(|T|,|P|)),   ink-amount ratio
//                    fine 7x7 ink-profile match)         symbol layout
//      If unrelated plan ink lands inside the candidate window, a focused
//      score can ignore ink outside dilate(T), but only when template coverage
//      is already strong and with a small penalty.
//      where T is template ink and P is window ink. Dilation makes the two
//      overlap ratios saturate on small dense shapes (a solid blob covers
//      any outline), so the ink-amount ratio keeps solid-vs-outline and
//      thick-vs-thin pairs apart. 1.0 = pixel-perfect.
//   4. A per-row band ink prefilter (word-granular sliding sums) rejects the
//      overwhelmingly white sheet before any window is scored.
//   5. Non-maximum suppression keeps the best hit per symbol location.
//
// Coordinates in/out are PIXELS of the bitmap handed to TryCreate; the caller
// owns the pixel<->PDF mapping. The session reads the bitmap once in
// TryCreate, so FindMatches can re-run on background threads with different
// thresholds without touching the bitmap again.
public sealed record SimilarSymbolMatch(
    int CenterX,
    int CenterY,
    float Score,
    int RotationDegrees,
    bool Mirrored = false,
    float TemplateCoverage = 1f,
    float WindowPrecision = 1f,
    float InkRatio = 1f,
    float ProfileScore = 1f,
    float ProjectionScore = 1f,
    float StrokeScore = 1f,
    bool UsedFocusedScore = false);

public sealed class SimilarSymbolMatchSession
{
    private const int MaxTemplateSide = 96;      // downsample until template fits
    private const int InkLumaThreshold = 176;
    private const int MinTemplateInk = 12;
    private const int TemplateAutoTightenPadding = 2;
    private const float EdgeRelaxedInsetRatio = 0.08f;
    private const int EdgeRelaxedMaxInset = 8;
    private const float EdgeRelaxedScoreMultiplier = 0.93f;
    private const float PeripheralNoiseMaxTotalShare = 0.35f;
    private const float PeripheralNoiseMaxCoreRatio = 0.85f;
    private const float TemplateMajorComponentMinShare = 0.28f;
    private const float TemplateSecondMajorMinLargestRatio = 0.55f;
    private const float LooseTemplateSelectionAreaRatio = 6.0f;
    private const float LooseTemplateSelectionSideRatio = 2.5f;
    private const float FocusedWindowScoreMultiplier = 0.98f;
    private const float FocusedWindowMinTemplateCoverage = 0.90f;
    private const float FocusedWindowCoreInsetRatio = 0.12f;
    private const int FocusedWindowCoreMaxInset = 8;
    private const int FocusedWindowMaxCoreExtraInk = 8;
    private const float FocusedWindowMaxCoreExtraShare = 0.04f;
    private const int FocusedWindowMaxTotalExtraInk = 48;
    private const float FocusedWindowMaxTotalExtraShare = 0.22f;
    private const int StrokeProfileBucketCount = 4;
    private const int MinStrokeProfilePairs = 6;
    private const float StrokeProfileMismatchWeight = 0.75f;

    private readonly int _width;
    private readonly int _height;
    private readonly int _wordsPerRow;           // includes one zero pad word
    private readonly ulong[] _ink;
    private readonly ulong[] _dilated;
    private readonly int _factor;                // full-res px per matching px
    private readonly TemplateVariant[] _variants; // 0/90/180/270 degrees

    public int DownsampleFactor => _factor;
    public int TemplateInkPixels => _variants[0].InkCount;
    public string TemplateWarning { get; }

    private readonly record struct SimilarWindowScore(
        float Score,
        float TemplateCoverage,
        float WindowPrecision,
        float InkRatio,
        float ProfileScore,
        float ProjectionScore,
        float StrokeScore,
        bool UsedFocusedScore)
    {
        public static SimilarWindowScore Zero { get; } = new(0f, 0f, 0f, 0f, 0f, 0f, 0f, false);
    }

    private SimilarSymbolMatchSession(
        int width,
        int height,
        int wordsPerRow,
        ulong[] ink,
        ulong[] dilated,
        int factor,
        TemplateVariant[] variants,
        string templateWarning)
    {
        _width = width;
        _height = height;
        _wordsPerRow = wordsPerRow;
        _ink = ink;
        _dilated = dilated;
        _factor = factor;
        _variants = variants;
        TemplateWarning = templateWarning;
    }

    public static SimilarSymbolMatchSession? TryCreate(SKBitmap page, SKRectI templateRect, out string error)
    {
        error = "";
        if (page == null || page.Width <= 0 || page.Height <= 0)
        {
            error = "No rendered page raster is available.";
            return null;
        }

        var rect = SKRectI.Intersect(templateRect, new SKRectI(0, 0, page.Width, page.Height));
        if (rect.Width < 6 || rect.Height < 6)
        {
            error = "The selected box is too small.";
            return null;
        }

        if (!TryFindTemplateInkBounds(page, rect, out SKRectI templateInkBounds))
        {
            error = "The selected box contains no linework to match.";
            return null;
        }

        int factor = TemplateDownsampleFactor(templateInkBounds);

        bool[] inkPixels = BinarizeDownsampled(page, factor, out int width, out int height);

        int wordsPerRow = (width + 63) / 64 + 1;
        var ink = PackRows(inkPixels, width, height, wordsPerRow);
        var dilated = DilateOnePixel(ink, height, wordsPerRow);

        var scaledRect = new SKRectI(
            rect.Left / factor,
            rect.Top / factor,
            Math.Min(width, (rect.Right + factor - 1) / factor),
            Math.Min(height, (rect.Bottom + factor - 1) / factor));
        bool[,] template = PrepareTemplate(ExtractTemplate(inkPixels, width, scaledRect));
        if (CountInk(template) < MinTemplateInk)
        {
            error = "The selected box contains no linework to match.";
            return null;
        }

        var variants = new List<TemplateVariant>();
        AddRotatedVariants(variants, template, mirrored: false);
        AddRotatedVariants(variants, MirrorHorizontal(template), mirrored: true);

        string templateWarning = BuildTemplateQualityWarning(
            template,
            factor,
            scaledRect.Width,
            scaledRect.Height);
        return new SimilarSymbolMatchSession(
            width,
            height,
            wordsPerRow,
            ink,
            dilated,
            factor,
            variants.ToArray(),
            templateWarning);
    }

    public List<SimilarSymbolMatch> FindMatches(float minScore, bool includeRotations, CancellationToken cancellationToken) =>
        FindMatches(minScore, includeRotations, includeMirrored: false, cancellationToken);

    public List<SimilarSymbolMatch> FindMatches(
        float minScore,
        bool includeRotations,
        bool includeMirrored,
        CancellationToken cancellationToken)
    {
        minScore = Math.Clamp(minScore, 0.05f, 1f);
        var all = new List<SimilarSymbolMatch>();
        IEnumerable<TemplateVariant> variants = _variants.Where(variant =>
            (includeRotations || variant.RotationDegrees == 0) &&
            (includeMirrored || !variant.Mirrored));
        foreach (TemplateVariant variant in variants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FindVariantMatches(variant, minScore, all, cancellationToken);
        }

        return SuppressNonMaxima(all);
    }

    private void FindVariantMatches(
        TemplateVariant template, float minScore, List<SimilarSymbolMatch> sink, CancellationToken cancellationToken)
    {
        int tw = template.Width;
        int th = template.Height;
        if (tw > _width || th > _height)
            return;

        int[] bandInk = BuildBandInkSums(th);
        int maxY = _height - th;
        int maxX = _width - tw;
        int templateInk = template.InkCount;
        int relaxedInk = template.EdgeRelaxed?.InkCount ?? templateInk;
        int lowerPrefilterInk = Math.Min(templateInk, relaxedInk);
        int upperPrefilterInk = Math.Max(templateInk, relaxedInk);
        // The ink-ratio term needs sqrt(|P|/|T|-ish) >= minScore, i.e. window
        // ink within [|T|*s^2, |T|/s^2]; widen with slack because the band
        // prefilter over-counts at word granularity.
        float ratioBound = minScore * minScore;
        int minWindowInk = (int)(lowerPrefilterInk * ratioBound * 0.5f);
        int maxWindowInk = (int)(upperPrefilterInk / ratioBound * 1.5f);

        var gate = new object();
        var options = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = MatcherMaxDegreeOfParallelism(),
        };
        Parallel.For(0, maxY + 1, options, () => new List<SimilarSymbolMatch>(),
            (y, _, local) =>
            {
                if ((y & 31) == 0)
                    cancellationToken.ThrowIfCancellationRequested();

                int bandRow = y * (_wordsPerRow - 1);
                for (int x = 0; x <= maxX; x++)
                {
                    int w0 = x >> 6;
                    int wLast = (x + tw - 1) >> 6;
                    int approxInk = 0;
                    for (int w = w0; w <= wLast; w++)
                        approxInk += bandInk[bandRow + w];
                    if (approxInk < minWindowInk)
                        continue;
                    // approxInk over-counts whole edge words. A dense plan line
                    // near the symbol can look too dark at word granularity, so
                    // confirm upper-bound rejects with the exact window ink.
                    if (approxInk - 128 > maxWindowInk &&
                        CountWindowInk(template, x, y) > maxWindowInk &&
                        CountFocusedWindowInk(template, x, y) > maxWindowInk)
                    {
                        continue;
                    }

                    SimilarWindowScore score = ScoreWindow(template, x, y, minScore);
                    if (score.Score < minScore && template.EdgeRelaxed != null)
                    {
                        TemplateVariant relaxed = template.EdgeRelaxed;
                        SimilarWindowScore relaxedScore = ScoreWindow(
                            relaxed,
                            x + relaxed.OffsetX,
                            y + relaxed.OffsetY,
                            minScore);
                        float adjustedScore = relaxedScore.Score * EdgeRelaxedScoreMultiplier;
                        if (relaxedScore.Score >= minScore && adjustedScore > score.Score)
                            score = relaxedScore with { Score = adjustedScore };
                    }

                    if (score.Score >= minScore)
                        local.Add(new SimilarSymbolMatch(
                            (x + tw / 2) * _factor + _factor / 2,
                            (y + th / 2) * _factor + _factor / 2,
                            score.Score,
                            template.RotationDegrees,
                            template.Mirrored,
                            score.TemplateCoverage,
                            score.WindowPrecision,
                            score.InkRatio,
                            score.ProfileScore,
                            score.ProjectionScore,
                            score.StrokeScore,
                            score.UsedFocusedScore));
                }

                return local;
            },
            local =>
            {
                if (local.Count == 0)
                    return;
                lock (gate)
                    sink.AddRange(local);
            });
    }

    private SimilarWindowScore ScoreWindow(TemplateVariant template, int x, int y, float minScore)
    {
        int shift = x & 63;
        int w0 = x >> 6;
        int words = template.ShiftWords[shift];
        ulong[] tInk = template.ShiftedInk[shift];
        ulong[] tDilated = template.ShiftedDilated[shift];
        ulong[] masks = template.WindowMasks[shift];
        ulong[] coreMasks = template.CoreWindowMasks[shift];
        ulong[] gridColumnMasks = template.GridColumnMasks[shift];
        ulong[] projectionColumnMasks = template.ProjectionColumnMasks[shift];
        Span<int> windowGrid = stackalloc int[TemplateVariant.GridCellCount];
        Span<int> windowRowProjection = stackalloc int[TemplateVariant.ProjectionBandCount];
        Span<int> windowColumnProjection = stackalloc int[TemplateVariant.ProjectionBandCount];
        Span<int> focusedWindowGrid = stackalloc int[TemplateVariant.GridCellCount];
        Span<int> focusedWindowRowProjection = stackalloc int[TemplateVariant.ProjectionBandCount];
        Span<int> focusedWindowColumnProjection = stackalloc int[TemplateVariant.ProjectionBandCount];
        Span<int> windowStrokeProfile = stackalloc int[StrokeProfileBucketCount];
        Span<int> focusedWindowStrokeProfile = stackalloc int[StrokeProfileBucketCount];
        Span<ulong> previousWindowRow = stackalloc ulong[words];
        Span<ulong> previousFocusedWindowRow = stackalloc ulong[words];
        Span<ulong> currentWindowRow = stackalloc ulong[words];
        Span<ulong> currentFocusedWindowRow = stackalloc ulong[words];

        int matchedTemplate = 0;
        int matchedWindow = 0;
        int windowInk = 0;
        int focusedWindowInk = 0;
        int focusedExtraCoreInk = 0;
        for (int row = 0; row < template.Height; row++)
        {
            int pageBase = (y + row) * _wordsPerRow + w0;
            int templateBase = row * words;
            int gridRow = Math.Min(TemplateVariant.GridSide - 1, row * TemplateVariant.GridSide / template.Height);
            int gridBase = gridRow * TemplateVariant.GridSide;
            int projectionRow = Math.Min(
                TemplateVariant.ProjectionBandCount - 1,
                row * TemplateVariant.ProjectionBandCount / template.Height);
            int rowInk = 0;
            int focusedRowInk = 0;
            bool coreRow = row >= template.CoreTop && row < template.CoreBottom;
            currentWindowRow.Clear();
            currentFocusedWindowRow.Clear();
            for (int k = 0; k < words; k++)
            {
                ulong pageInkWord = _ink[pageBase + k];
                ulong templateDilatedWord = tDilated[templateBase + k];
                ulong focusedInkWord = pageInkWord & templateDilatedWord & masks[k];
                ulong maskedInkWord = pageInkWord & masks[k];
                currentWindowRow[k] = maskedInkWord;
                currentFocusedWindowRow[k] = focusedInkWord;
                if (coreRow)
                    focusedExtraCoreInk += BitOperations.PopCount(
                        pageInkWord & ~templateDilatedWord & masks[k] & coreMasks[k]);
                matchedTemplate += BitOperations.PopCount(tInk[templateBase + k] & _dilated[pageBase + k]);
                matchedWindow += BitOperations.PopCount(pageInkWord & templateDilatedWord);
                int maskedInk = BitOperations.PopCount(maskedInkWord);
                int focusedInk = BitOperations.PopCount(focusedInkWord);
                windowInk += maskedInk;
                rowInk += maskedInk;
                focusedWindowInk += focusedInk;
                focusedRowInk += focusedInk;
                for (int col = 0; col < TemplateVariant.GridSide; col++)
                {
                    windowGrid[gridBase + col] += BitOperations.PopCount(
                        pageInkWord & gridColumnMasks[col * words + k]);
                    focusedWindowGrid[gridBase + col] += BitOperations.PopCount(
                        focusedInkWord & gridColumnMasks[col * words + k]);
                }
                for (int col = 0; col < TemplateVariant.ProjectionBandCount; col++)
                {
                    windowColumnProjection[col] += BitOperations.PopCount(
                        pageInkWord & projectionColumnMasks[col * words + k]);
                    focusedWindowColumnProjection[col] += BitOperations.PopCount(
                        focusedInkWord & projectionColumnMasks[col * words + k]);
                }
            }
            windowRowProjection[projectionRow] += rowInk;
            focusedWindowRowProjection[projectionRow] += focusedRowInk;
            AddStrokeProfile(currentWindowRow, previousWindowRow, windowStrokeProfile);
            AddStrokeProfile(currentFocusedWindowRow, previousFocusedWindowRow, focusedWindowStrokeProfile);
            currentWindowRow.CopyTo(previousWindowRow);
            currentFocusedWindowRow.CopyTo(previousFocusedWindowRow);
        }

        if (windowInk <= 0 || template.InkCount <= 0)
            return SimilarWindowScore.Zero;

        float templateCoverage = matchedTemplate / (float)template.InkCount;
        float windowPrecision = matchedWindow / (float)windowInk;
        float inkRatio = MathF.Sqrt(
            Math.Min(template.InkCount, windowInk) / (float)Math.Max(template.InkCount, windowInk));
        float profileScore = GridProfileScore(template.GridInkCounts, windowGrid, template.InkCount, windowInk);
        float rowProjectionScore = ProfileScore(
            template.RowProjectionInkCounts,
            windowRowProjection,
            template.InkCount,
            windowInk);
        float columnProjectionScore = ProfileScore(
            template.ColumnProjectionInkCounts,
            windowColumnProjection,
            template.InkCount,
            windowInk);
        float projectionScore = Math.Min(rowProjectionScore, columnProjectionScore);
        float strokeScore = StrokeProfileScore(
            template.StrokeProfileCounts,
            template.StrokeProfileTotal,
            windowStrokeProfile,
            SumProfile(windowStrokeProfile));
        float strictScore = Math.Min(
            Math.Min(Math.Min(templateCoverage, windowPrecision), inkRatio),
            Math.Min(Math.Min(profileScore, projectionScore), strokeScore));
        int focusedExtraInk = windowInk - focusedWindowInk;
        var strict = new SimilarWindowScore(
            strictScore,
            templateCoverage,
            windowPrecision,
            inkRatio,
            profileScore,
            projectionScore,
            strokeScore,
            UsedFocusedScore: false);
        if (strictScore >= minScore ||
            templateCoverage < FocusedWindowMinTemplateCoverage ||
            focusedWindowInk <= 0 ||
            focusedWindowInk >= windowInk ||
            focusedExtraCoreInk > FocusedWindowCoreExtraLimit(template.InkCount) ||
            focusedExtraInk > FocusedWindowTotalExtraLimit(template.InkCount))
        {
            return strict;
        }

        float focusedInkRatio = MathF.Sqrt(
            Math.Min(template.InkCount, focusedWindowInk) / (float)Math.Max(template.InkCount, focusedWindowInk));
        float focusedProfileScore = GridProfileScore(
            template.GridInkCounts,
            focusedWindowGrid,
            template.InkCount,
            focusedWindowInk);
        float focusedRowProjectionScore = ProfileScore(
            template.RowProjectionInkCounts,
            focusedWindowRowProjection,
            template.InkCount,
            focusedWindowInk);
        float focusedColumnProjectionScore = ProfileScore(
            template.ColumnProjectionInkCounts,
            focusedWindowColumnProjection,
            template.InkCount,
            focusedWindowInk);
        float focusedProjectionScore = Math.Min(focusedRowProjectionScore, focusedColumnProjectionScore);
        float focusedStrokeScore = StrokeProfileScore(
            template.StrokeProfileCounts,
            template.StrokeProfileTotal,
            focusedWindowStrokeProfile,
            SumProfile(focusedWindowStrokeProfile));
        float focusedScore = Math.Min(
            Math.Min(templateCoverage, focusedInkRatio),
            Math.Min(Math.Min(focusedProfileScore, focusedProjectionScore), focusedStrokeScore));
        float adjustedFocusedScore = focusedScore * FocusedWindowScoreMultiplier;
        if (adjustedFocusedScore <= strictScore)
            return strict;

        return new SimilarWindowScore(
            adjustedFocusedScore,
            templateCoverage,
            windowPrecision,
            focusedInkRatio,
            focusedProfileScore,
            focusedProjectionScore,
            focusedStrokeScore,
            UsedFocusedScore: true);
    }

    private int CountWindowInk(TemplateVariant template, int x, int y)
    {
        int shift = x & 63;
        int w0 = x >> 6;
        int words = template.ShiftWords[shift];
        ulong[] masks = template.WindowMasks[shift];
        int windowInk = 0;

        for (int row = 0; row < template.Height; row++)
        {
            int pageBase = (y + row) * _wordsPerRow + w0;
            for (int k = 0; k < words; k++)
                windowInk += BitOperations.PopCount(_ink[pageBase + k] & masks[k]);
        }

        return windowInk;
    }

    private int CountFocusedWindowInk(TemplateVariant template, int x, int y)
    {
        int shift = x & 63;
        int w0 = x >> 6;
        int words = template.ShiftWords[shift];
        ulong[] tDilated = template.ShiftedDilated[shift];
        ulong[] masks = template.WindowMasks[shift];
        int focusedInk = 0;

        for (int row = 0; row < template.Height; row++)
        {
            int pageBase = (y + row) * _wordsPerRow + w0;
            int templateBase = row * words;
            for (int k = 0; k < words; k++)
                focusedInk += BitOperations.PopCount(_ink[pageBase + k] & tDilated[templateBase + k] & masks[k]);
        }

        return focusedInk;
    }

    private static float GridProfileScore(
        ReadOnlySpan<int> templateGrid,
        ReadOnlySpan<int> windowGrid,
        int templateInk,
        int windowInk) =>
        ProfileScore(templateGrid, windowGrid, templateInk, windowInk);

    private static float ProfileScore(
        ReadOnlySpan<int> templateProfile,
        ReadOnlySpan<int> windowProfile,
        int templateInk,
        int windowInk)
    {
        if (templateInk <= 0 || windowInk <= 0)
            return 0f;

        float diff = 0f;
        for (int i = 0; i < templateProfile.Length; i++)
        {
            float templateShare = templateProfile[i] / (float)templateInk;
            float windowShare = windowProfile[i] / (float)windowInk;
            diff += MathF.Abs(templateShare - windowShare);
        }

        return Math.Clamp(1f - diff * 0.5f, 0f, 1f);
    }

    private static float StrokeProfileScore(
        ReadOnlySpan<int> templateProfile,
        int templatePairs,
        ReadOnlySpan<int> windowProfile,
        int windowPairs)
    {
        if (templatePairs < MinStrokeProfilePairs)
            return 1f;
        if (windowPairs <= 0)
            return 0f;

        float diff = 0f;
        for (int i = 0; i < templateProfile.Length; i++)
        {
            float templateShare = templateProfile[i] / (float)templatePairs;
            float windowShare = windowProfile[i] / (float)windowPairs;
            diff += MathF.Abs(templateShare - windowShare);
        }

        return Math.Clamp(1f - diff * StrokeProfileMismatchWeight, 0f, 1f);
    }

    private static void AddStrokeProfile(
        ReadOnlySpan<ulong> row,
        ReadOnlySpan<ulong> previousRow,
        Span<int> profile)
    {
        for (int k = 0; k < row.Length; k++)
        {
            ulong current = row[k];
            if (current == 0)
                continue;

            profile[0] += BitOperations.PopCount(current & (current << 1));
            if (k > 0 &&
                (current & 1UL) != 0 &&
                (row[k - 1] & (1UL << 63)) != 0)
            {
                profile[0]++;
            }

            ulong previous = previousRow[k];
            profile[1] += BitOperations.PopCount(current & previous);

            ulong previousShiftedLeft = (previous << 1) |
                                        (k > 0 ? previousRow[k - 1] >> 63 : 0UL);
            ulong previousShiftedRight = (previous >> 1) |
                                         (k + 1 < previousRow.Length ? previousRow[k + 1] << 63 : 0UL);
            profile[2] += BitOperations.PopCount(current & previousShiftedLeft);
            profile[3] += BitOperations.PopCount(current & previousShiftedRight);
        }
    }

    private static int SumProfile(ReadOnlySpan<int> profile)
    {
        int total = 0;
        for (int i = 0; i < profile.Length; i++)
            total += profile[i];
        return total;
    }

    private static int[] BuildStrokeProfile(bool[,] template)
    {
        int width = template.GetLength(0);
        int height = template.GetLength(1);
        var profile = new int[StrokeProfileBucketCount];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!template[x, y])
                    continue;

                if (x + 1 < width && template[x + 1, y])
                    profile[0]++;
                if (y + 1 < height && template[x, y + 1])
                    profile[1]++;
                if (x + 1 < width && y + 1 < height && template[x + 1, y + 1])
                    profile[2]++;
                if (x > 0 && y + 1 < height && template[x - 1, y + 1])
                    profile[3]++;
            }
        }

        return profile;
    }

    // Sliding vertical sums of per-word ink popcounts: bandInk[y][w] = ink
    // pixels in word column w across rows y..y+bandHeight-1. Coarse but O(1)
    // per candidate and only ~2 MB for a large sheet.
    private int[] BuildBandInkSums(int bandHeight)
    {
        int dataWords = _wordsPerRow - 1;
        int bandRows = _height - bandHeight + 1;
        var band = new int[bandRows * dataWords];
        var column = new int[dataWords];

        for (int y = 0; y < bandHeight; y++)
        {
            int rowBase = y * _wordsPerRow;
            for (int w = 0; w < dataWords; w++)
                column[w] += BitOperations.PopCount(_ink[rowBase + w]);
        }
        Array.Copy(column, 0, band, 0, dataWords);

        for (int y = 1; y < bandRows; y++)
        {
            int removeBase = (y - 1) * _wordsPerRow;
            int addBase = (y + bandHeight - 1) * _wordsPerRow;
            int bandBase = y * dataWords;
            for (int w = 0; w < dataWords; w++)
            {
                column[w] += BitOperations.PopCount(_ink[addBase + w]) -
                             BitOperations.PopCount(_ink[removeBase + w]);
                band[bandBase + w] = column[w];
            }
        }

        return band;
    }

    private List<SimilarSymbolMatch> SuppressNonMaxima(List<SimilarSymbolMatch> matches)
    {
        if (matches.Count <= 1)
            return matches;

        int radius = Math.Max(4, (int)(Math.Max(_variants[0].Width, _variants[0].Height) * 0.6f)) * _factor;
        long radiusSq = (long)radius * radius;
        var accepted = new List<SimilarSymbolMatch>();
        foreach (SimilarSymbolMatch candidate in matches.OrderByDescending(m => m.Score))
        {
            bool overlaps = false;
            foreach (SimilarSymbolMatch kept in accepted)
            {
                long dx = candidate.CenterX - kept.CenterX;
                long dy = candidate.CenterY - kept.CenterY;
                if (dx * dx + dy * dy <= radiusSq)
                {
                    overlaps = true;
                    break;
                }
            }
            if (!overlaps)
                accepted.Add(candidate);
        }

        return accepted;
    }

    private static int MatcherMaxDegreeOfParallelism()
    {
        int reserve = Environment.ProcessorCount <= 4 ? 1 : 2;
        return Math.Max(1, Environment.ProcessorCount - reserve);
    }

    // ── Raster preparation ───────────────────────────────────────────────────

    private static int TemplateDownsampleFactor(SKRectI templateInkBounds)
    {
        int inkWidth = Math.Max(0, templateInkBounds.Width) + TemplateAutoTightenPadding * 2;
        int inkHeight = Math.Max(0, templateInkBounds.Height) + TemplateAutoTightenPadding * 2;
        int factor = 1;
        while (Math.Max(inkWidth, inkHeight) / factor > MaxTemplateSide)
            factor *= 2;
        return factor;
    }

    private static bool TryFindTemplateInkBounds(SKBitmap page, SKRectI rect, out SKRectI inkBounds)
    {
        inkBounds = default;
        SKBitmap source = page;
        SKBitmap? converted = null;
        if (page.ColorType != SKColorType.Bgra8888 && page.ColorType != SKColorType.Rgba8888)
        {
            converted = page.Copy(SKColorType.Bgra8888);
            source = converted ?? page;
        }

        try
        {
            int width = rect.Width;
            int height = rect.Height;
            var selectionInk = new bool[width, height];
            bool hasInk = false;
            unsafe
            {
                byte* basePtr = (byte*)source.GetPixels().ToPointer();
                int rowBytes = source.RowBytes;
                bool bgra = source.ColorType != SKColorType.Rgba8888;
                for (int y = rect.Top; y < rect.Bottom; y++)
                {
                    byte* row = basePtr + y * rowBytes;
                    for (int x = rect.Left; x < rect.Right; x++)
                    {
                        if (!IsInkPixel(row + x * 4, bgra))
                            continue;

                        selectionInk[x - rect.Left, y - rect.Top] = true;
                        hasInk = true;
                    }
                }
            }

            if (!hasInk)
                return false;

            List<TemplateComponent> components = ReadTemplateComponents(selectionInk);
            List<TemplateComponent> noise = FindPeripheralTemplateNoiseComponents(components);
            List<TemplateComponent> boundsComponents = noise.Count == 0
                ? components
                : components.Where(component => !noise.Contains(component)).ToList();
            if (boundsComponents.Sum(component => component.Pixels.Count) < MinTemplateInk)
                boundsComponents = components;

            inkBounds = TemplateComponentBounds(boundsComponents, width, rect.Left, rect.Top);
            return true;
        }
        finally
        {
            converted?.Dispose();
        }
    }

    private static bool[] BinarizeDownsampled(SKBitmap page, int factor, out int width, out int height)
    {
        SKBitmap source = page;
        SKBitmap? converted = null;
        if (page.ColorType != SKColorType.Bgra8888 && page.ColorType != SKColorType.Rgba8888)
        {
            converted = page.Copy(SKColorType.Bgra8888);
            source = converted ?? page;
        }

        try
        {
            width = source.Width / factor;
            height = source.Height / factor;
            var ink = new bool[width * height];
            unsafe
            {
                byte* basePtr = (byte*)source.GetPixels().ToPointer();
                int rowBytes = source.RowBytes;
                bool bgra = source.ColorType != SKColorType.Rgba8888;
                for (int sy = 0; sy < height * factor; sy++)
                {
                    byte* row = basePtr + sy * rowBytes;
                    int outRow = (sy / factor) * width;
                    for (int sx = 0; sx < width * factor; sx++)
                    {
                        byte* px = row + sx * 4;
                        if (IsInkPixel(px, bgra))
                            ink[outRow + sx / factor] = true;
                    }
                }
            }

            return ink;
        }
        finally
        {
            converted?.Dispose();
        }
    }

    private static unsafe bool IsInkPixel(byte* px, bool bgra)
    {
        int b = bgra ? px[0] : px[2];
        int g = px[1];
        int r = bgra ? px[2] : px[0];
        int luma = (r * 299 + g * 587 + b * 114) / 1000;
        return luma < InkLumaThreshold;
    }

    private static ulong[] PackRows(bool[] pixels, int width, int height, int wordsPerRow)
    {
        var packed = new ulong[height * wordsPerRow];
        for (int y = 0; y < height; y++)
        {
            int rowBase = y * wordsPerRow;
            int pixelBase = y * width;
            for (int x = 0; x < width; x++)
            {
                if (pixels[pixelBase + x])
                    packed[rowBase + (x >> 6)] |= 1UL << (x & 63);
            }
        }

        return packed;
    }

    private static ulong[] DilateOnePixel(ulong[] ink, int height, int wordsPerRow)
    {
        var dilated = new ulong[ink.Length];
        for (int y = 0; y < height; y++)
        {
            int rowBase = y * wordsPerRow;
            int above = y > 0 ? rowBase - wordsPerRow : rowBase;
            int below = y < height - 1 ? rowBase + wordsPerRow : rowBase;
            for (int w = 0; w < wordsPerRow - 1; w++)
            {
                ulong vertical = ink[rowBase + w] | ink[above + w] | ink[below + w];
                ulong left = w > 0 ? ink[rowBase + w - 1] >> 63 : 0;
                ulong right = ink[rowBase + w + 1] << 63;
                dilated[rowBase + w] = vertical | (vertical << 1) | (vertical >> 1) | left | right;
            }
        }

        return dilated;
    }

    private static bool[,] ExtractTemplate(bool[] pixels, int width, SKRectI rect)
    {
        var template = new bool[rect.Width, rect.Height];
        for (int y = 0; y < rect.Height; y++)
        {
            int rowBase = (rect.Top + y) * width + rect.Left;
            for (int x = 0; x < rect.Width; x++)
                template[x, y] = pixels[rowBase + x];
        }

        return template;
    }

    private static bool[,] AutoTightenTemplate(bool[,] source)
    {
        int width = source.GetLength(0);
        int height = source.GetLength(1);
        int minX = width;
        int minY = height;
        int maxX = -1;
        int maxY = -1;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!source[x, y])
                    continue;

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        if (maxX < minX || maxY < minY)
            return source;

        minX = Math.Max(0, minX - TemplateAutoTightenPadding);
        minY = Math.Max(0, minY - TemplateAutoTightenPadding);
        maxX = Math.Min(width - 1, maxX + TemplateAutoTightenPadding);
        maxY = Math.Min(height - 1, maxY + TemplateAutoTightenPadding);
        if (minX == 0 && minY == 0 && maxX == width - 1 && maxY == height - 1)
            return source;

        int tightWidth = maxX - minX + 1;
        int tightHeight = maxY - minY + 1;
        var tight = new bool[tightWidth, tightHeight];
        for (int y = 0; y < tightHeight; y++)
            for (int x = 0; x < tightWidth; x++)
                tight[x, y] = source[x + minX, y + minY];
        return tight;
    }

    private static bool[,] PrepareTemplate(bool[,] source)
    {
        bool[,] tight = AutoTightenTemplate(source);
        bool[,] cleaned = RemovePeripheralTemplateNoise(tight);
        return ReferenceEquals(cleaned, tight)
            ? tight
            : AutoTightenTemplate(cleaned);
    }

    private static bool[,] RemovePeripheralTemplateNoise(bool[,] source)
    {
        int width = source.GetLength(0);
        int height = source.GetLength(1);
        List<TemplateComponent> components = ReadTemplateComponents(source);

        List<TemplateComponent> remove = FindPeripheralTemplateNoiseComponents(components);
        if (remove.Count == 0)
            return source;

        var cleaned = new bool[width, height];
        Array.Copy(source, cleaned, source.Length);
        foreach (TemplateComponent component in remove)
        {
            foreach (int pixel in component.Pixels)
            {
                int x = pixel % width;
                int y = pixel / width;
                cleaned[x, y] = false;
            }
        }

        return CountInk(cleaned) >= MinTemplateInk ? cleaned : source;
    }

    private static List<TemplateComponent> FindPeripheralTemplateNoiseComponents(List<TemplateComponent> components)
    {
        if (components.Count <= 1)
            return new List<TemplateComponent>();

        int totalInk = components.Sum(component => component.Pixels.Count);
        int largestCore = components
            .Where(component => !component.TouchesBorder)
            .Select(component => component.Pixels.Count)
            .DefaultIfEmpty(0)
            .Max();
        if (largestCore < MinTemplateInk)
            return new List<TemplateComponent>();

        return components
            .Where(component =>
                component.TouchesBorder &&
                (component.Pixels.Count <= totalInk * PeripheralNoiseMaxTotalShare ||
                 component.Pixels.Count <= largestCore * PeripheralNoiseMaxCoreRatio))
            .ToList();
    }

    private static SKRectI TemplateComponentBounds(
        IReadOnlyList<TemplateComponent> components,
        int componentWidth,
        int offsetX,
        int offsetY)
    {
        if (components.Count == 0)
            return new SKRectI(offsetX, offsetY, offsetX, offsetY);

        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;
        foreach (TemplateComponent component in components)
        {
            foreach (int pixel in component.Pixels)
            {
                int x = pixel % componentWidth;
                int y = pixel / componentWidth;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return new SKRectI(offsetX + minX, offsetY + minY, offsetX + maxX + 1, offsetY + maxY + 1);
    }

    private static string BuildTemplateQualityWarning(
        bool[,] template,
        int downsampleFactor,
        int selectionWidth,
        int selectionHeight)
    {
        var warnings = new List<string>();
        string clusterWarning = BuildTemplateClusterWarning(template);
        if (!string.IsNullOrWhiteSpace(clusterWarning))
            warnings.Add(clusterWarning);
        string looseWarning = BuildLooseTemplateSelectionWarning(template, selectionWidth, selectionHeight);
        if (!string.IsNullOrWhiteSpace(looseWarning))
            warnings.Add(looseWarning);
        if (downsampleFactor > 1)
        {
            warnings.Add(
                $"Template note: selected box was downsampled {downsampleFactor}x before matching; tighter selection gives sharper results.");
        }

        return string.Join(Environment.NewLine, warnings);
    }

    private static string BuildLooseTemplateSelectionWarning(bool[,] template, int selectionWidth, int selectionHeight)
    {
        int templateWidth = template.GetLength(0);
        int templateHeight = template.GetLength(1);
        if (templateWidth <= 0 ||
            templateHeight <= 0 ||
            selectionWidth <= templateWidth ||
            selectionHeight <= templateHeight)
        {
            return "";
        }

        float areaRatio = selectionWidth * selectionHeight / (float)(templateWidth * templateHeight);
        float sideRatio = Math.Max(
            selectionWidth / (float)templateWidth,
            selectionHeight / (float)templateHeight);
        if (areaRatio < LooseTemplateSelectionAreaRatio && sideRatio < LooseTemplateSelectionSideRatio)
            return "";

        return "Template note: selected box has large whitespace; matcher tightened to the detected symbol before scanning.";
    }

    private static string BuildTemplateClusterWarning(bool[,] template)
    {
        List<TemplateComponent> components = ReadTemplateComponents(template)
            .OrderByDescending(component => component.Pixels.Count)
            .ToList();
        if (components.Count <= 1)
            return "";

        int totalInk = components.Sum(component => component.Pixels.Count);
        int majorMinInk = Math.Max(MinTemplateInk, (int)MathF.Ceiling(totalInk * TemplateMajorComponentMinShare));
        List<TemplateComponent> major = components
            .Where(component => component.Pixels.Count >= majorMinInk)
            .ToList();
        if (major.Count <= 1)
            return "";

        if (major[1].Pixels.Count < major[0].Pixels.Count * TemplateSecondMajorMinLargestRatio)
            return "";

        return "Template note: multiple separate ink clusters detected; review candidates carefully.";
    }

    private static List<TemplateComponent> ReadTemplateComponents(bool[,] source)
    {
        int width = source.GetLength(0);
        int height = source.GetLength(1);
        var visited = new bool[width * height];
        var components = new List<TemplateComponent>();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                if (!source[x, y] || visited[index])
                    continue;

                components.Add(ReadTemplateComponent(source, visited, x, y));
            }
        }

        return components;
    }

    private static TemplateComponent ReadTemplateComponent(bool[,] source, bool[] visited, int startX, int startY)
    {
        int width = source.GetLength(0);
        int height = source.GetLength(1);
        var component = new TemplateComponent();
        var stack = new Stack<int>();
        stack.Push(startY * width + startX);

        while (stack.Count > 0)
        {
            int pixel = stack.Pop();
            if (visited[pixel])
                continue;

            int x = pixel % width;
            int y = pixel / width;
            if (!source[x, y])
                continue;

            visited[pixel] = true;
            component.Pixels.Add(pixel);
            component.TouchesBorder |= x == 0 || y == 0 || x == width - 1 || y == height - 1;

            for (int dy = -1; dy <= 1; dy++)
            {
                int ny = y + dy;
                if (ny < 0 || ny >= height)
                    continue;

                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                        continue;

                    int nx = x + dx;
                    if (nx < 0 || nx >= width)
                        continue;

                    int next = ny * width + nx;
                    if (!visited[next] && source[nx, ny])
                        stack.Push(next);
                }
            }
        }

        return component;
    }

    private static bool[,] RotateClockwise(bool[,] source)
    {
        int w = source.GetLength(0);
        int h = source.GetLength(1);
        var rotated = new bool[h, w];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                rotated[h - 1 - y, x] = source[x, y];
        return rotated;
    }

    private static bool[,] MirrorHorizontal(bool[,] source)
    {
        int w = source.GetLength(0);
        int h = source.GetLength(1);
        var mirrored = new bool[w, h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                mirrored[w - 1 - x, y] = source[x, y];
        return mirrored;
    }

    private static void AddRotatedVariants(List<TemplateVariant> variants, bool[,] source, bool mirrored)
    {
        bool[,] current = source;
        for (int degrees = 0; degrees <= 270; degrees += 90)
        {
            if (degrees > 0)
                current = RotateClockwise(current);
            variants.Add(TemplateVariant.Build(current, degrees, mirrored));
        }
    }

    private static TemplateVariant? TryBuildEdgeRelaxedVariant(bool[,] template, int rotationDegrees, bool mirrored)
    {
        int width = template.GetLength(0);
        int height = template.GetLength(1);
        int insetX = EdgeRelaxedInset(width);
        int insetY = EdgeRelaxedInset(height);
        if (insetX <= 0 && insetY <= 0)
            return null;

        int innerWidth = width - insetX * 2;
        int innerHeight = height - insetY * 2;
        if (innerWidth < 6 || innerHeight < 6)
            return null;

        var inner = new bool[innerWidth, innerHeight];
        for (int y = 0; y < innerHeight; y++)
            for (int x = 0; x < innerWidth; x++)
                inner[x, y] = template[x + insetX, y + insetY];

        if (CountInk(inner) < MinTemplateInk)
            return null;

        return TemplateVariant.Build(
            inner,
            rotationDegrees,
            mirrored,
            insetX,
            insetY,
            buildEdgeRelaxed: false);
    }

    private static int EdgeRelaxedInset(int side)
    {
        int maximum = Math.Min(EdgeRelaxedMaxInset, (side - 6) / 2);
        if (maximum < 2)
            return 0;

        int inset = (int)MathF.Round(side * EdgeRelaxedInsetRatio);
        return Math.Clamp(inset, 2, maximum);
    }

    private static int FocusedWindowCoreInset(int side)
    {
        int maximum = Math.Min(FocusedWindowCoreMaxInset, (side - 6) / 2);
        if (maximum < 2)
            return 0;

        int inset = (int)MathF.Round(side * FocusedWindowCoreInsetRatio);
        return Math.Clamp(inset, 2, maximum);
    }

    private static int FocusedWindowCoreExtraLimit(int templateInk) =>
        Math.Max(
            FocusedWindowMaxCoreExtraInk,
            (int)MathF.Ceiling(templateInk * FocusedWindowMaxCoreExtraShare));

    private static int FocusedWindowTotalExtraLimit(int templateInk) =>
        Math.Max(
            FocusedWindowMaxTotalExtraInk,
            (int)MathF.Ceiling(templateInk * FocusedWindowMaxTotalExtraShare));

    private static int CountInk(bool[,] template)
    {
        int count = 0;
        foreach (bool pixel in template)
            if (pixel)
                count++;
        return count;
    }

    private sealed class TemplateComponent
    {
        public List<int> Pixels { get; } = new();
        public bool TouchesBorder { get; set; }
    }

    // Pre-shifted packed copies of one template rotation: ShiftedInk[s] holds
    // the template rows shifted s bits right so candidate x with (x & 63) == s
    // compares word-aligned against the page. WindowMasks[s] selects exactly
    // the window's bits inside those words for exact window ink counts.
    private sealed class TemplateVariant
    {
        public const int GridSide = 7;
        public const int GridCellCount = GridSide * GridSide;
        public const int ProjectionBandCount = 12;

        public required int Width;
        public required int Height;
        public required int InkCount;
        public required int[] GridInkCounts;
        public required int[] RowProjectionInkCounts;
        public required int[] ColumnProjectionInkCounts;
        public required int[] StrokeProfileCounts;
        public required int StrokeProfileTotal;
        public required int RotationDegrees;
        public required bool Mirrored;
        public required int OffsetX;
        public required int OffsetY;
        public required int[] ShiftWords;
        public required ulong[][] ShiftedInk;
        public required ulong[][] ShiftedDilated;
        public required ulong[][] WindowMasks;
        public required ulong[][] CoreWindowMasks;
        public required ulong[][] GridColumnMasks;
        public required ulong[][] ProjectionColumnMasks;
        public required int CoreTop;
        public required int CoreBottom;
        public TemplateVariant? EdgeRelaxed { get; init; }

        public static TemplateVariant Build(
            bool[,] template,
            int rotationDegrees,
            bool mirrored,
            int offsetX = 0,
            int offsetY = 0,
            bool buildEdgeRelaxed = true)
        {
            int width = template.GetLength(0);
            int height = template.GetLength(1);
            int baseWords = (width + 63) / 64;

            var inkRows = new ulong[height * (baseWords + 1)];
            var gridInkCounts = new int[GridCellCount];
            var rowProjectionInkCounts = new int[ProjectionBandCount];
            var columnProjectionInkCounts = new int[ProjectionBandCount];
            var strokeProfileCounts = BuildStrokeProfile(template);
            int strokeProfileTotal = SumProfile(strokeProfileCounts);
            int inkCount = 0;
            for (int y = 0; y < height; y++)
            {
                int gridRow = Math.Min(GridSide - 1, y * GridSide / height);
                int rowProjection = Math.Min(ProjectionBandCount - 1, y * ProjectionBandCount / height);
                for (int x = 0; x < width; x++)
                {
                    if (!template[x, y])
                        continue;
                    inkRows[y * (baseWords + 1) + (x >> 6)] |= 1UL << (x & 63);
                    inkCount++;
                    int gridCol = Math.Min(GridSide - 1, x * GridSide / width);
                    gridInkCounts[gridRow * GridSide + gridCol]++;
                    int columnProjection = Math.Min(ProjectionBandCount - 1, x * ProjectionBandCount / width);
                    rowProjectionInkCounts[rowProjection]++;
                    columnProjectionInkCounts[columnProjection]++;
                }
            }
            ulong[] dilatedRows = DilateOnePixel(inkRows, height, baseWords + 1);

            var shiftWords = new int[64];
            var shiftedInk = new ulong[64][];
            var shiftedDilated = new ulong[64][];
            var windowMasks = new ulong[64][];
            var coreWindowMasks = new ulong[64][];
            var gridColumnMasks = new ulong[64][];
            var projectionColumnMasks = new ulong[64][];
            int coreInsetX = FocusedWindowCoreInset(width);
            int coreInsetY = FocusedWindowCoreInset(height);
            for (int s = 0; s < 64; s++)
            {
                int words = (s + width + 63) / 64;
                shiftWords[s] = words;
                shiftedInk[s] = ShiftRows(inkRows, height, baseWords + 1, words, s);
                shiftedDilated[s] = ShiftRows(dilatedRows, height, baseWords + 1, words, s);
                windowMasks[s] = BuildWindowMask(width, words, s);
                coreWindowMasks[s] = BuildWindowMask(width, words, s, coreInsetX, width - coreInsetX);
                gridColumnMasks[s] = BuildGridColumnMasks(width, words, s);
                projectionColumnMasks[s] = BuildProjectionColumnMasks(width, words, s);
            }

            return new TemplateVariant
            {
                Width = width,
                Height = height,
                InkCount = inkCount,
                GridInkCounts = gridInkCounts,
                RowProjectionInkCounts = rowProjectionInkCounts,
                ColumnProjectionInkCounts = columnProjectionInkCounts,
                StrokeProfileCounts = strokeProfileCounts,
                StrokeProfileTotal = strokeProfileTotal,
                RotationDegrees = rotationDegrees,
                Mirrored = mirrored,
                OffsetX = offsetX,
                OffsetY = offsetY,
                ShiftWords = shiftWords,
                ShiftedInk = shiftedInk,
                ShiftedDilated = shiftedDilated,
                WindowMasks = windowMasks,
                CoreWindowMasks = coreWindowMasks,
                GridColumnMasks = gridColumnMasks,
                ProjectionColumnMasks = projectionColumnMasks,
                CoreTop = coreInsetY,
                CoreBottom = height - coreInsetY,
                EdgeRelaxed = buildEdgeRelaxed
                    ? TryBuildEdgeRelaxedVariant(template, rotationDegrees, mirrored)
                    : null,
            };
        }

        private static ulong[] ShiftRows(ulong[] rows, int height, int sourceWordsPerRow, int targetWords, int shift)
        {
            var shifted = new ulong[height * targetWords];
            for (int y = 0; y < height; y++)
            {
                int sourceBase = y * sourceWordsPerRow;
                int targetBase = y * targetWords;
                for (int k = 0; k < targetWords; k++)
                {
                    ulong value = k < sourceWordsPerRow ? rows[sourceBase + k] : 0;
                    ulong result = shift == 0 ? value : value << shift;
                    if (shift != 0 && k > 0 && k - 1 < sourceWordsPerRow)
                        result |= rows[sourceBase + k - 1] >> (64 - shift);
                    shifted[targetBase + k] = result;
                }
            }

            return shifted;
        }

        private static ulong[] BuildWindowMask(int width, int words, int shift, int startX = 0, int endX = -1)
        {
            var mask = new ulong[words];
            endX = endX < 0 ? width : endX;
            startX = Math.Clamp(startX, 0, width);
            endX = Math.Clamp(endX, startX, width);
            for (int x = shift + startX; x < shift + endX; x++)
                mask[x >> 6] |= 1UL << (x & 63);
            return mask;
        }

        private static ulong[] BuildGridColumnMasks(int width, int words, int shift)
        {
            var masks = new ulong[GridSide * words];
            for (int x = 0; x < width; x++)
            {
                int col = Math.Min(GridSide - 1, x * GridSide / width);
                int shiftedX = shift + x;
                masks[col * words + (shiftedX >> 6)] |= 1UL << (shiftedX & 63);
            }

            return masks;
        }

        private static ulong[] BuildProjectionColumnMasks(int width, int words, int shift)
        {
            var masks = new ulong[ProjectionBandCount * words];
            for (int x = 0; x < width; x++)
            {
                int col = Math.Min(ProjectionBandCount - 1, x * ProjectionBandCount / width);
                int shiftedX = shift + x;
                masks[col * words + (shiftedX >> 6)] |= 1UL << (shiftedX & 63);
            }

            return masks;
        }
    }
}
