using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using OurPlaneCore;
using OurPlaneCore.Controls;
using SkiaSharp;

// Synthetic-raster checks for the offline "count similar symbols" matcher:
// known symbol placements on a white sheet must be found exactly, rotated
// copies only when rotations are enabled, and distractor shapes never.
internal static class SimilarSymbolMatcherTests
{
    private const int SymbolWidth = 44;
    private const int SymbolHeight = 30;

    public static void FindsAllPlainCopies()
    {
        var placements = new[] { (40, 40), (200, 60), (120, 220) };
        using SKBitmap page = BuildPage(placements, rotatedAt: null, withDistractors: true);
        SimilarSymbolMatchSession? session = CreateSession(page);

        List<SimilarSymbolMatch> matches = session!.FindMatches(DefaultThreshold(), includeRotations: false, CancellationToken.None);

        AssertTrue(matches.Count == placements.Length,
            $"expected {placements.Length} matches, got {matches.Count}");
        foreach ((int x, int y) in placements)
        {
            AssertTrue(
                matches.Any(match => NearCenter(match, x, y)),
                $"symbol at ({x},{y}) was not matched");
        }
    }

    public static void FindsRotatedCopyOnlyWhenEnabled()
    {
        var placements = new[] { (40, 40), (200, 60) };
        using SKBitmap page = BuildPage(placements, rotatedAt: (300, 200), withDistractors: false);
        SimilarSymbolMatchSession? session = CreateSession(page);

        List<SimilarSymbolMatch> plain = session!.FindMatches(DefaultThreshold(), includeRotations: false, CancellationToken.None);
        List<SimilarSymbolMatch> rotated = session.FindMatches(DefaultThreshold(), includeRotations: true, CancellationToken.None);

        AssertTrue(plain.Count == placements.Length,
            $"expected {placements.Length} unrotated matches, got {plain.Count}");
        AssertTrue(rotated.Count == placements.Length + 1,
            $"expected {placements.Length + 1} matches with rotations, got {rotated.Count}");
        AssertTrue(
            rotated.Any(match => match.RotationDegrees != 0),
            "the rotated copy should be reported with a non-zero rotation");
    }

    public static void FindsSlightlyScaledCopy()
    {
        var placements = new[] { (40, 40), (200, 60) };
        using SKBitmap page = BuildPage(placements, rotatedAt: null, withDistractors: false);
        using (var canvas = new SKCanvas(page))
        using (var stroke = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            IsAntialias = false,
        })
        using (var fill = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        })
        {
            DrawScaledSymbol(canvas, stroke, fill, 300, 200, 1.04f);
        }

        SimilarSymbolMatchSession? session = CreateSession(page);

        List<SimilarSymbolMatch> matches = session!.FindMatches(
            DefaultThreshold(),
            includeRotations: false,
            CancellationToken.None);

        SimilarSymbolMatch? scaled = matches.FirstOrDefault(match => NearCenter(match, 300, 200));
        AssertTrue(matches.Count == placements.Length + 1,
            $"expected scaled copy plus {placements.Length} exact matches, got {matches.Count}: " +
            string.Join(", ", matches.Select(match => $"{match.CenterX},{match.CenterY}:{match.Score:0.00}/scale={match.ScalePercent}")));
        AssertTrue(scaled != null, "slightly scaled copy should be found at the default precision threshold");
        AssertTrue(scaled!.ScalePercent != 100,
            $"scaled copy should be reported from a scaled template variant, got scale {scaled.ScalePercent}");
    }

    public static void AdaptiveInkModelSeparatesFaintAndColoredInk()
    {
        SimilarInkModel def = SimilarInkModel.Default;
        AssertTrue(!def.IsInk(255, 255, 255), "white is not ink");
        AssertTrue(def.IsInk(0, 0, 0), "black is ink");
        AssertTrue(!def.IsInk(190, 190, 190), "faint gray is not ink under the default cut");

        var faint = new SimilarInkModel(230, SimilarInkModel.DisabledChromaThreshold);
        AssertTrue(faint.IsInk(190, 190, 190), "faint gray is ink once the luma cut is raised");
        AssertTrue(!faint.IsInk(245, 245, 245), "near-white stays background even with a raised cut");

        var colored = new SimilarInkModel(176, 40);
        AssertTrue(colored.IsInk(255, 200, 200), "a light but saturated tint counts as colored ink");
        AssertTrue(!colored.IsInk(250, 248, 249), "a near-white pixel with no real hue stays background");
    }

    public static void FindsFaintGraySymbolCopies()
    {
        var placements = new[] { (40, 40), (200, 60), (120, 220) };
        using SKBitmap page = BuildColoredPage(placements, new SKColor(190, 190, 190));
        SimilarSymbolMatchSession? session = CreateSession(page);

        List<SimilarSymbolMatch> matches = session!.FindMatches(
            DefaultThreshold(),
            includeRotations: false,
            CancellationToken.None);

        AssertTrue(matches.Count == placements.Length,
            $"faint gray symbols should be found via the adaptive ink model, expected {placements.Length}, got {matches.Count}");
        foreach ((int x, int y) in placements)
        {
            AssertTrue(
                matches.Any(match => NearCenter(match, x, y)),
                $"faint gray symbol at ({x},{y}) was missed; the default luma cut alone would skip it");
        }
    }

    public static void RejectsDistractorsAndDedupes()
    {
        var placements = new[] { (60, 50) };
        using SKBitmap page = BuildPage(placements, rotatedAt: null, withDistractors: true);
        SimilarSymbolMatchSession? session = CreateSession(page);

        List<SimilarSymbolMatch> matches = session!.FindMatches(DefaultThreshold(), includeRotations: true, CancellationToken.None);

        AssertTrue(matches.Count == 1, $"expected only the template instance, got {matches.Count}");
        AssertTrue(NearCenter(matches[0], 60, 50), "the single match must sit on the template instance");
        AssertTrue(matches[0].Score > 0.9f,
            $"self-match score should be near 1.0, got {matches[0].Score:0.00}");
    }

    public static void FindsCopiesWithLooseEdgeSelection()
    {
        var placements = new[] { (40, 40), (200, 60), (120, 220) };
        using SKBitmap page = BuildPage(placements, rotatedAt: null, withDistractors: false);
        DrawLooseSelectionEdgeNoise(page, placements[0].Item1, placements[0].Item2);
        SimilarSymbolMatchSession? session = CreateSession(page, leftPad: 8, topPad: 4, rightPad: 4, bottomPad: 4);

        List<SimilarSymbolMatch> matches = session!.FindMatches(DefaultThreshold(), includeRotations: false, CancellationToken.None);

        AssertTrue(matches.Count == placements.Length,
            $"expected {placements.Length} matches from loose edge selection, got {matches.Count}");
        foreach ((int x, int y) in placements)
        {
            AssertTrue(
                matches.Any(match => NearCenter(match, x, y)),
                $"loose selection missed symbol at ({x},{y})");
        }
    }

    public static void TrimsPeripheralSelectionNoiseBeforeMatching()
    {
        var placements = new[] { (40, 40), (200, 60), (120, 220) };
        using SKBitmap cleanPage = BuildPage(placements, rotatedAt: null, withDistractors: false);
        SimilarSymbolMatchSession? cleanSession = CreateSession(cleanPage);

        using SKBitmap noisyPage = BuildPage(placements, rotatedAt: null, withDistractors: false);
        DrawLooseSelectionEdgeNoise(noisyPage, placements[0].Item1, placements[0].Item2);
        SimilarSymbolMatchSession? noisySession = CreateSession(
            noisyPage,
            leftPad: 8,
            topPad: 4,
            rightPad: 4,
            bottomPad: 4);

        AssertTrue(
            Math.Abs(noisySession!.TemplateInkPixels - cleanSession!.TemplateInkPixels) <= 8,
            $"edge selection noise should be trimmed before matching; clean ink {cleanSession.TemplateInkPixels}, noisy ink {noisySession.TemplateInkPixels}");
    }

    public static void TrimsLongPeripheralLineSelectionNoise()
    {
        var placements = new[] { (80, 90), (240, 90), (80, 230), (240, 230) };
        using SKBitmap cleanPage = BuildPage(placements, rotatedAt: null, withDistractors: false);
        SimilarSymbolMatchSession? cleanSession = CreateSession(cleanPage);

        using SKBitmap noisyPage = BuildPage(placements, rotatedAt: null, withDistractors: false);
        DrawPeripheralTemplateNoise(noisyPage, left: 15, top: placements[0].Item2 - 9, width: 180);
        SimilarSymbolMatchSession? noisySession = SimilarSymbolMatchSession.TryCreate(
            noisyPage,
            new SKRectI(
                placements[0].Item1 - 65,
                placements[0].Item2 - 12,
                placements[0].Item1 + SymbolWidth + 8,
                placements[0].Item2 + SymbolHeight + 8),
            out string error);

        AssertTrue(noisySession != null, $"long-line noisy template session creation failed: {error}");
        AssertTrue(
            Math.Abs(noisySession!.TemplateInkPixels - cleanSession!.TemplateInkPixels) <= 12,
            $"long peripheral context line should be trimmed before matching; clean ink {cleanSession.TemplateInkPixels}, noisy ink {noisySession.TemplateInkPixels}");
    }

    public static void LooseWhitespaceSelectionKeepsCentersPrecise()
    {
        var placements = new[] { (40, 40), (200, 60), (120, 220) };
        using SKBitmap page = BuildPage(placements, rotatedAt: null, withDistractors: false);
        SimilarSymbolMatchSession? session = CreateSession(
            page,
            leftPad: 38,
            topPad: 28,
            rightPad: 4,
            bottomPad: 4);

        List<SimilarSymbolMatch> matches = session!.FindMatches(
            DefaultThreshold(),
            includeRotations: false,
            CancellationToken.None);

        AssertTrue(matches.Count == placements.Length,
            $"expected {placements.Length} matches from loose whitespace selection, got {matches.Count}");
        foreach ((int x, int y) in placements)
        {
            AssertTrue(
                matches.Any(match => NearCenter(match, x, y)),
                $"loose whitespace selection shifted marker away from symbol at ({x},{y})");
        }
    }

    public static void WarnsOnMultiSymbolTemplate()
    {
        var placements = new[] { (40, 40), (94, 40), (200, 180) };
        using SKBitmap page = BuildPage(placements, rotatedAt: null, withDistractors: false);
        SimilarSymbolMatchSession? normalSession = CreateSession(page);
        var multiSymbolRect = new SKRectI(
            placements[0].Item1 - 3,
            placements[0].Item2 - 3,
            placements[1].Item1 + SymbolWidth + 3,
            placements[1].Item2 + SymbolHeight + 3);

        SimilarSymbolMatchSession? multiSession = SimilarSymbolMatchSession.TryCreate(
            page,
            multiSymbolRect,
            out string error);

        AssertTrue(string.IsNullOrWhiteSpace(normalSession!.TemplateWarning),
            $"normal one-symbol template should not warn, got '{normalSession.TemplateWarning}'");
        AssertTrue(multiSession != null, $"multi-symbol session creation failed: {error}");
        AssertTrue(
            multiSession!.TemplateWarning.Contains("multiple separate ink clusters", StringComparison.Ordinal),
            $"multi-symbol template should warn, got '{multiSession.TemplateWarning}'");
    }

    public static void KeepsLooseWhitespaceTemplateFullResolution()
    {
        var placements = new[] { (140, 120) };
        using SKBitmap page = BuildPage(placements, rotatedAt: null, withDistractors: false);
        SimilarSymbolMatchSession? normalSession = CreateSession(page);
        var looseRect = new SKRectI(
            placements[0].Item1 - 120,
            placements[0].Item2 - 90,
            placements[0].Item1 + SymbolWidth + 120,
            placements[0].Item2 + SymbolHeight + 90);

        SimilarSymbolMatchSession? looseSession = SimilarSymbolMatchSession.TryCreate(
            page,
            looseRect,
            out string error);

        AssertTrue(string.IsNullOrWhiteSpace(normalSession!.TemplateWarning),
            $"normal one-symbol template should not warn, got '{normalSession.TemplateWarning}'");
        AssertTrue(looseSession != null, $"loose template session creation failed: {error}");
        AssertTrue(looseSession!.DownsampleFactor == 1,
            $"loose whitespace around a small symbol should stay full-resolution, got factor {looseSession.DownsampleFactor}");
        AssertTrue(
            !looseSession.TemplateWarning.Contains("downsampled", StringComparison.Ordinal),
            $"loose whitespace should not warn as downsampled, got '{looseSession.TemplateWarning}'");
        AssertTrue(
            looseSession.TemplateWarning.Contains("tightened", StringComparison.Ordinal),
            $"loose whitespace should warn that the template was tightened, got '{looseSession.TemplateWarning}'");
        AssertTrue(
            Math.Abs(looseSession.TemplateInkPixels - normalSession.TemplateInkPixels) <= 8,
            $"loose whitespace should not change template detail; normal ink {normalSession.TemplateInkPixels}, loose ink {looseSession.TemplateInkPixels}");
    }

    public static void KeepsPeripheralNoiseFromDownsamplingTemplate()
    {
        var placements = new[] { (140, 120) };
        using SKBitmap page = BuildPage(placements, rotatedAt: null, withDistractors: false);
        DrawPeripheralTemplateNoise(page, left: 20, top: 30, width: 140);
        var looseRect = new SKRectI(
            20,
            30,
            placements[0].Item1 + SymbolWidth + 120,
            placements[0].Item2 + SymbolHeight + 90);

        SimilarSymbolMatchSession? session = SimilarSymbolMatchSession.TryCreate(
            page,
            looseRect,
            out string error);

        AssertTrue(session != null, $"noisy loose template session creation failed: {error}");
        AssertTrue(session!.DownsampleFactor == 1,
            $"border-touching peripheral noise should not downsample a small template, got factor {session.DownsampleFactor}");
        AssertTrue(
            !session.TemplateWarning.Contains("downsampled", StringComparison.Ordinal),
            $"cleaned peripheral noise should not warn as downsampled, got '{session.TemplateWarning}'");
    }

    public static void WarnsOnDownsampledLargeTemplate()
    {
        using SKBitmap page = BuildLargeTemplatePage(80, 60, 180, 120);
        SimilarSymbolMatchSession? session = SimilarSymbolMatchSession.TryCreate(
            page,
            new SKRectI(76, 56, 264, 184),
            out string error);

        AssertTrue(session != null, $"large template session creation failed: {error}");
        AssertTrue(session!.DownsampleFactor > 1,
            $"large template should be downsampled, got factor {session.DownsampleFactor}");
        AssertTrue(
            session.TemplateWarning.Contains("downsampled", StringComparison.Ordinal),
            $"downsampled large template should warn, got '{session.TemplateWarning}'");
    }

    public static void KeepsHitsNearAdjacentPlanInk()
    {
        var placements = new[] { (40, 40), (200, 60), (120, 220) };
        using SKBitmap page = BuildPage(placements, rotatedAt: null, withDistractors: false);
        DrawAdjacentWordNoise(page, placements[1].Item1, placements[1].Item2);
        SimilarSymbolMatchSession? session = CreateSession(page);

        List<SimilarSymbolMatch> matches = session!.FindMatches(
            DefaultThreshold(),
            includeRotations: false,
            CancellationToken.None);

        AssertTrue(
            matches.Any(match => NearCenter(match, placements[1].Item1, placements[1].Item2)),
            "adjacent plan ink outside the exact symbol window should not make Similar miss a real copy; " +
            $"matches: {string.Join(", ", matches.Select(match => $"{match.CenterX},{match.CenterY}:{match.Score:0.00}"))}");
    }

    public static void KeepsHitsWithDisconnectedWindowInk()
    {
        var placements = new[] { (40, 40), (200, 60), (120, 220) };
        using SKBitmap page = BuildPage(placements, rotatedAt: null, withDistractors: false);
        DrawDisconnectedWindowNoise(page, placements[1].Item1, placements[1].Item2);
        SimilarSymbolMatchSession? session = CreateSession(page);

        List<SimilarSymbolMatch> matches = session!.FindMatches(
            DefaultThreshold(),
            includeRotations: false,
            CancellationToken.None);

        SimilarSymbolMatch? recovered = matches.FirstOrDefault(match => NearCenter(match, placements[1].Item1, placements[1].Item2));
        AssertTrue(
            recovered != null,
            "disconnected plan ink inside the candidate window should not make Similar miss a real copy; " +
            $"matches: {string.Join(", ", matches.Select(match => $"{match.CenterX},{match.CenterY}:{match.Score:0.00}"))}");
        SimilarSymbolMatch found = recovered!;
        AssertTrue(
            found.UsedFocusedScore &&
            found.TemplateCoverage >= 0.9f &&
            found.ProfileScore > 0f &&
            found.ProjectionScore > 0f,
            "recovered disconnected-ink matches should carry focused-score diagnostics for review hover text");
    }

    public static void FindsMirroredCopyOnlyWhenEnabled()
    {
        var placements = new[] { (40, 40), (200, 60) };
        using SKBitmap page = BuildMirrorSensitivePage(placements, mirroredAt: (300, 200));
        SimilarSymbolMatchSession? session = CreateSession(page);

        List<SimilarSymbolMatch> plain = session!.FindMatches(
            DefaultThreshold(),
            includeRotations: false,
            includeMirrored: false,
            CancellationToken.None);
        List<SimilarSymbolMatch> rotatedOnly = session.FindMatches(
            DefaultThreshold(),
            includeRotations: true,
            includeMirrored: false,
            CancellationToken.None);
        List<SimilarSymbolMatch> mirrored = session.FindMatches(
            DefaultThreshold(),
            includeRotations: false,
            includeMirrored: true,
            CancellationToken.None);

        AssertTrue(plain.Count == placements.Length,
            $"expected {placements.Length} unmirrored matches, got {plain.Count}");
        AssertTrue(rotatedOnly.Count == placements.Length,
            $"rotated-only search should not include mirrored copies, got {rotatedOnly.Count}");
        AssertTrue(mirrored.Count == placements.Length + 1,
            $"expected mirrored search to find {placements.Length + 1} matches, got {mirrored.Count}");
        AssertTrue(mirrored.Any(match => match.Mirrored), "mirrored copy should be reported as mirrored");
    }

    public static void FindsMatchesOnAnotherSheetBitmap()
    {
        var templatePlacements = new[] { (40, 40) };
        using SKBitmap templatePage = BuildPage(templatePlacements, rotatedAt: null, withDistractors: false);
        SimilarSymbolMatchSession? session = CreateSession(templatePage);

        var otherPlacements = new[] { (60, 50), (220, 120), (130, 230) };
        using SKBitmap otherPage = BuildPage(otherPlacements, rotatedAt: null, withDistractors: true);

        List<SimilarSymbolMatch> matches = session!.FindMatchesOnBitmap(
            otherPage,
            DefaultThreshold(),
            includeRotations: false,
            includeMirrored: false,
            CancellationToken.None);

        AssertTrue(matches.Count == otherPlacements.Length,
            $"the boxed template should match copies on another sheet raster, expected {otherPlacements.Length}, got {matches.Count}");
        foreach ((int x, int y) in otherPlacements)
        {
            AssertTrue(
                matches.Any(match => NearCenter(match, x, y)),
                $"other-sheet symbol at ({x},{y}) was missed by FindMatchesOnBitmap");
        }
    }

    public static void TextGuidedNearCentersUseOtherSheetBitmap()
    {
        using SKBitmap templatePage = BuildPage([(40, 40)], rotatedAt: null, withDistractors: false);
        SimilarSymbolMatchSession? session = CreateSession(templatePage);

        using SKBitmap otherPage = BuildPage([(220, 120)], rotatedAt: null, withDistractors: false);
        var candidateCenters = new List<SKPoint>
        {
            new(220 + SymbolWidth / 2f, 120 + SymbolHeight / 2f),
        };

        List<SimilarSymbolMatch> sourceMatches = session!.FindMatchesNearCenters(
            candidateCenters,
            searchRadiusPixels: 12,
            DefaultThreshold(),
            includeRotations: false,
            includeMirrored: false,
            CancellationToken.None);
        List<SimilarSymbolMatch> otherMatches = session.FindMatchesNearCentersOnBitmap(
            otherPage,
            candidateCenters,
            searchRadiusPixels: 12,
            DefaultThreshold(),
            includeRotations: false,
            includeMirrored: false,
            CancellationToken.None);

        AssertTrue(sourceMatches.Count == 0,
            "source sheet should be empty at the other-sheet text candidate window");
        AssertTrue(
            otherMatches.Count == 1 && NearCenter(otherMatches[0], 220, 120),
            $"other-sheet text-guided raster verification should scan the supplied bitmap, got {otherMatches.Count}: " +
            string.Join(", ", otherMatches.Select(match => $"{match.CenterX},{match.CenterY}:{match.Score:0.00}")));
    }

    public static void SimilarCountSearchesAllSheets()
    {
        string matcher = File.ReadAllText(Path.Combine("Models", "SimilarSymbolMatcher.cs"));
        string dialog = File.ReadAllText(Path.Combine("Dialogs", "SimilarCountDialog.cs"));
        string mainWindow = File.ReadAllText("MainWindow.SimilarCount.cs");
        string otherSheets = File.ReadAllText("MainWindow.SimilarCount.OtherSheets.cs");
        string settings = File.ReadAllText(Path.Combine("Models", "AppSettingsStore.cs"));

        AssertTrue(
            matcher.Contains("public List<SimilarSymbolMatch> FindMatchesOnBitmap", StringComparison.Ordinal) &&
            matcher.Contains("public List<SimilarSymbolMatch> FindMatchesNearCentersOnBitmap", StringComparison.Ordinal) &&
            matcher.Contains("private sealed class PageRaster", StringComparison.Ordinal) &&
            matcher.Contains("FindMatchesOn(", StringComparison.Ordinal),
            "matcher should expose a reusable template that can scan any sheet bitmap");
        AssertTrue(
            dialog.Contains("Search all sheets in this job", StringComparison.Ordinal) &&
            dialog.Contains("text-only candidates are reported only", StringComparison.Ordinal) &&
            dialog.Contains("IncludeAllSheets", StringComparison.Ordinal) &&
            dialog.Contains("OtherSheetSummary", StringComparison.Ordinal) &&
            dialog.Contains("AddButtonText(_lastFound, _lastOtherSheetNewCount)", StringComparison.Ordinal) &&
            dialog.Contains("off-sheet to", StringComparison.Ordinal) &&
            dialog.Contains("_allSheetsBox", StringComparison.Ordinal),
            "dialog should offer the all-sheets option, surface the other-sheet summary, and label Add with off-sheet counts");
        AssertTrue(
            mainWindow.Contains("SweepOtherSimilarSheetsAsync", StringComparison.Ordinal) &&
            mainWindow.Contains("OtherSheetAdditions", StringComparison.Ordinal) &&
            mainWindow.Contains("AddOtherSheetSimilarCounts", StringComparison.Ordinal) &&
            mainWindow.Contains("EnsureOtherSheetSweepAsync(threshold, rotations, mirrored", StringComparison.Ordinal) &&
            mainWindow.Contains("string.Format(CultureInfo.InvariantCulture, \"{0:0.000}|{1}|{2}\", threshold, rotations, mirrored)", StringComparison.Ordinal) &&
            mainWindow.Replace("\r\n", "\n").Contains("bitmapScale,\n                    threshold,\n                    rotations", StringComparison.Ordinal) &&
            otherSheets.Contains("TryBuildOtherSheetTextGuide", StringComparison.Ordinal) &&
            otherSheets.Contains("TemplateFromTextOffset", StringComparison.Ordinal) &&
            otherSheets.Contains("FindOtherSheetTextGuidedRasterMatches", StringComparison.Ordinal) &&
            otherSheets.Contains("session.FindMatchesNearCentersOnBitmap", StringComparison.Ordinal) &&
            otherSheets.Contains("textGuideRequired", StringComparison.Ordinal) &&
            otherSheets.Contains("textTemplateFallback", StringComparison.Ordinal) &&
            otherSheets.Contains("skipped text-template fallback auto-add", StringComparison.Ordinal) &&
            otherSheets.Contains("skipped visual-only Beam/Openings sweep", StringComparison.Ordinal) &&
            otherSheets.Contains("Math.Clamp(threshold, (float)AppSettingsStore.SimilarCountThresholdMin, 1f)", StringComparison.Ordinal) &&
            otherSheets.Contains("TryFindSimilarTextByQuery", StringComparison.Ordinal) &&
            otherSheets.Contains("request.UseTextCandidateRasterMatches || request.AllowExactTextMatches", StringComparison.Ordinal) &&
            otherSheets.Contains("Similar count all-sheets text-guided raster matches", StringComparison.Ordinal) &&
            otherSheets.Contains("skippedNoText", StringComparison.Ordinal) &&
            otherSheets.Contains("rejectedByRaster", StringComparison.Ordinal) &&
            otherSheets.Contains("rejectedTextCandidates", StringComparison.Ordinal) &&
            otherSheets.Contains("return ([], true, true);", StringComparison.Ordinal) &&
            mainWindow.Contains("otherSheetTextGuideSkippedSheets", StringComparison.Ordinal) &&
            mainWindow.Contains("otherSheetTextTemplateFallbackSkippedSheets", StringComparison.Ordinal) &&
            mainWindow.Contains("otherSheetTextRejectedCandidates", StringComparison.Ordinal) &&
            mainWindow.Contains("PDF text candidate(s)", StringComparison.Ordinal) &&
            mainWindow.Contains("were not auto-added without a raster match", StringComparison.Ordinal) &&
            mainWindow.Contains("were not auto-added without a usable PDF text guide", StringComparison.Ordinal) &&
            mainWindow.Contains("did not produce a usable raster template", StringComparison.Ordinal) &&
            mainWindow.Contains("Other sheets: no new raster matches.", StringComparison.Ordinal) &&
            mainWindow.Contains("AddMarkerOffset(center)", StringComparison.Ordinal) &&
            mainWindow.Contains("SimilarCountMaxSweepSheets", StringComparison.Ordinal) &&
            mainWindow.Contains("initialThreshold: (float)AppSettingsStore.SimilarCountThresholdDefault", StringComparison.Ordinal) &&
            mainWindow.Contains("initialRotations: request.InitialIncludeRotations", StringComparison.Ordinal) &&
            mainWindow.Contains("initialMirrored: request.InitialIncludeMirrored", StringComparison.Ordinal) &&
            mainWindow.Contains("initialAllSheets: false", StringComparison.Ordinal) &&
            mainWindow.Contains("_settings.SimilarCountThreshold = AppSettingsStore.SimilarCountThresholdDefault", StringComparison.Ordinal) &&
            mainWindow.Contains("_settings.SimilarCountRotations = false", StringComparison.Ordinal) &&
            mainWindow.Contains("_settings.SimilarCountMirrored = false", StringComparison.Ordinal) &&
            mainWindow.Contains("_settings.SimilarCountAllSheets = false", StringComparison.Ordinal) &&
            settings.Contains("settings.SimilarCountThreshold = SimilarCountThresholdDefault", StringComparison.Ordinal) &&
            settings.Contains("settings.SimilarCountRotations = false", StringComparison.Ordinal) &&
            settings.Contains("settings.SimilarCountMirrored = false", StringComparison.Ordinal) &&
            settings.Contains("settings.SimilarCountAllSheets = false", StringComparison.Ordinal),
            "the all-sheets sweep should render other sheets, honor the active threshold, use PDF text as a raster-verified search guide, add their markers, cap the sheet count, and manual Similar defaults should keep heavy options opt-in");
    }

    public static void RejectsNearMissAtPrecisionThreshold()
    {
        var placements = new[] { (40, 40), (200, 60) };
        using SKBitmap page = BuildNearMissPage(placements, nearMissAt: (300, 200));
        SimilarSymbolMatchSession? session = CreateSession(page);

        List<SimilarSymbolMatch> matches = session!.FindMatches(DefaultThreshold(), includeRotations: false, CancellationToken.None);

        AssertTrue(matches.Count == placements.Length,
            $"expected only {placements.Length} exact-shape matches, got {matches.Count}");
        AssertTrue(!matches.Any(match => NearCenter(match, 300, 200)),
            "near-miss symbol with similar ink and box should not pass the precision threshold");
    }

    public static void ScoresStrokeOrientationForNearMiss()
    {
        var placements = new[] { (40, 40), (200, 60) };
        using SKBitmap page = BuildNearMissPage(placements, nearMissAt: (300, 200));
        SimilarSymbolMatchSession? session = CreateSession(page);

        List<SimilarSymbolMatch> looseMatches = session!.FindMatches(
            0.55f,
            includeRotations: false,
            CancellationToken.None);

        SimilarSymbolMatch? clean = looseMatches.FirstOrDefault(match => NearCenter(match, 200, 60));
        SimilarSymbolMatch? nearMiss = looseMatches.FirstOrDefault(match => NearCenter(match, 300, 200));
        AssertTrue(clean != null, "loose scan should keep the clean comparison symbol");
        AssertTrue(nearMiss != null, "loose scan should expose the near miss for stroke diagnostics");
        AssertTrue(clean!.StrokeScore >= 0.98f,
            $"clean symbol stroke orientation should stay near perfect, got {clean.StrokeScore:0.00}");
        AssertTrue(nearMiss!.StrokeScore < clean.StrokeScore - 0.05f,
            $"near miss should lose stroke-orientation confidence; clean {clean.StrokeScore:0.00}, near {nearMiss.StrokeScore:0.00}");
    }

    public static void RejectsCoreOnlyRelaxedNearMissAtPrecisionThreshold()
    {
        var placements = new[] { (40, 40) };
        using SKBitmap page = BuildPage(placements, rotatedAt: null, withDistractors: false);
        using (var canvas = new SKCanvas(page))
        using (var stroke = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            IsAntialias = false,
        })
        using (var fill = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        })
        {
            DrawCoreOnlySymbol(canvas, stroke, fill, 200, 60);
        }

        SimilarSymbolMatchSession? session = CreateSession(page);

        List<SimilarSymbolMatch> matches = session!.FindMatches(
            DefaultThreshold(),
            includeRotations: false,
            CancellationToken.None);

        AssertTrue(matches.Count == placements.Length,
            $"expected only {placements.Length} full-symbol match, got {matches.Count}");
        AssertTrue(!matches.Any(match => NearCenter(match, 200, 60)),
            "core-only near miss should stay below the precision threshold");
    }

    public static void RejectsSymbolWithExtraInteriorMark()
    {
        var placements = new[] { (40, 40) };
        using SKBitmap page = BuildPage(placements, rotatedAt: null, withDistractors: false);
        using (var canvas = new SKCanvas(page))
        using (var stroke = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            IsAntialias = false,
        })
        using (var fill = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        })
        {
            DrawSymbol(canvas, stroke, fill, 200, 60);
            DrawInteriorExtraMark(canvas, fill, 200, 60);
        }

        SimilarSymbolMatchSession? session = CreateSession(page);

        List<SimilarSymbolMatch> matches = session!.FindMatches(
            DefaultThreshold(),
            includeRotations: false,
            CancellationToken.None);

        AssertTrue(matches.Count == placements.Length,
            $"expected only {placements.Length} clean-symbol match, got {matches.Count}");
        AssertTrue(!matches.Any(match => NearCenter(match, 200, 60)),
            "symbol with an extra interior mark should not pass as the clean template");
    }

    public static void RejectsSymbolEmbeddedInDenseSurroundingInk()
    {
        var placements = new[] { (40, 40) };
        using SKBitmap page = BuildPage(placements, rotatedAt: null, withDistractors: false);
        using (var canvas = new SKCanvas(page))
        using (var stroke = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            IsAntialias = false,
        })
        using (var fill = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        })
        {
            DrawSymbol(canvas, stroke, fill, 200, 60);
            DrawHeavySurroundingInk(canvas, fill, 200, 60);
        }

        SimilarSymbolMatchSession? session = CreateSession(page);

        List<SimilarSymbolMatch> matches = session!.FindMatches(
            DefaultThreshold(),
            includeRotations: false,
            CancellationToken.None);

        AssertTrue(matches.Count == placements.Length,
            $"expected only {placements.Length} clean-symbol match, got {matches.Count}: " +
            string.Join(", ", matches.Select(match => $"{match.CenterX},{match.CenterY}:{match.Score:0.00}/focused={match.UsedFocusedScore}")));
        AssertTrue(!matches.Any(match => NearCenter(match, 200, 60)),
            "a symbol embedded in dense surrounding ink should stay below the default precision threshold");
    }

    public static void RejectsSymbolWithHeavyDisconnectedWindowInk()
    {
        var placements = new[] { (40, 40) };
        using SKBitmap page = BuildPage(placements, rotatedAt: null, withDistractors: false);
        using (var canvas = new SKCanvas(page))
        using (var stroke = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            IsAntialias = false,
        })
        using (var fill = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        })
        {
            DrawSymbol(canvas, stroke, fill, 200, 60);
            DrawHeavyDisconnectedWindowInk(canvas, fill, 200, 60);
        }

        SimilarSymbolMatchSession? session = CreateSession(page);

        List<SimilarSymbolMatch> matches = session!.FindMatches(
            DefaultThreshold(),
            includeRotations: false,
            CancellationToken.None);

        AssertTrue(matches.Count == placements.Length,
            $"expected only {placements.Length} clean-symbol match, got {matches.Count}: " +
            string.Join(", ", matches.Select(match => $"{match.CenterX},{match.CenterY}:{match.Score:0.00}/focused={match.UsedFocusedScore}")));
        AssertTrue(!matches.Any(match => NearCenter(match, 200, 60)),
            "heavy disconnected ink inside the candidate window should not be ignored as a focused-score match");
    }

    public static void DefaultSettingsFavorPrecision()
    {
        var settings = new AppSettings();
        AssertTrue(
            Math.Abs(settings.SimilarCountThreshold - AppSettingsStore.SimilarCountThresholdDefault) < 0.0001,
            "new Similar Count settings should start at the precision threshold");
        AssertTrue(
            AppSettingsStore.SimilarCountThresholdDefault >= 0.94,
            "Similar Count precision default should be strict enough to avoid near-symbol false hits");
        AssertTrue(!settings.SimilarCountRotations, "rotations should be opt-in for precise Similar Count");
        AssertTrue(!settings.SimilarCountMirrored, "mirrored search should be opt-in for precise Similar Count");
        AssertTrue(!settings.SimilarCountAllSheets, "all-sheets scan should be opt-in for each Similar Count dialog");

        settings.SimilarCountThreshold = 0.6;
        settings.SimilarCountRotations = true;
        settings.SimilarCountMirrored = false;
        settings.SimilarCountAllSheets = true;
        settings.SimilarCountSettingsVersion = 0;
        AppSettingsStore.NormalizeSimilarCountSettings(settings);

        AssertTrue(
            Math.Abs(settings.SimilarCountThreshold - AppSettingsStore.SimilarCountThresholdDefault) < 0.0001,
            "legacy noisy Similar Count threshold should migrate to the precision default");
        AssertTrue(!settings.SimilarCountRotations, "legacy rotation-on default should migrate to opt-in");
        AssertTrue(!settings.SimilarCountMirrored, "mirrored matching should stay opt-in after migration");
        AssertTrue(!settings.SimilarCountAllSheets, "saved all-sheets scan should not reopen Similar Count into a whole-job scan");
        AssertTrue(
            settings.SimilarCountSettingsVersion == AppSettingsStore.SimilarCountSettingsCurrentVersion,
            "Similar Count settings version should be current after normalization");

        settings.SimilarCountThreshold = 0.6;
        settings.SimilarCountRotations = false;
        settings.SimilarCountMirrored = false;
        settings.SimilarCountSettingsVersion = 0;
        AppSettingsStore.NormalizeSimilarCountSettings(settings);

        AssertTrue(
            Math.Abs(settings.SimilarCountThreshold - AppSettingsStore.SimilarCountThresholdDefault) < 0.0001,
            "legacy low Similar Count threshold should migrate even when rotations were already disabled");

        settings.SimilarCountThreshold = 0.88;
        settings.SimilarCountRotations = false;
        settings.SimilarCountMirrored = false;
        settings.SimilarCountSettingsVersion = 2;
        AppSettingsStore.NormalizeSimilarCountSettings(settings);

        AssertTrue(
            Math.Abs(settings.SimilarCountThreshold - AppSettingsStore.SimilarCountThresholdDefault) < 0.0001,
            "older precision default should migrate to the stricter Similar Count default");

        settings.SimilarCountThreshold = 0.55;
        settings.SimilarCountRotations = true;
        settings.SimilarCountMirrored = true;
        settings.SimilarCountAllSheets = true;
        settings.SimilarCountSettingsVersion = AppSettingsStore.SimilarCountSettingsCurrentVersion;
        AppSettingsStore.NormalizeSimilarCountSettings(settings);

        AssertTrue(
            Math.Abs(settings.SimilarCountThreshold - AppSettingsStore.SimilarCountThresholdDefault) < 0.0001 &&
            !settings.SimilarCountRotations &&
            !settings.SimilarCountMirrored &&
            !settings.SimilarCountAllSheets,
            "current Similar Count settings should not persist loose, rotated, mirrored, or all-sheets scans as the next dialog default");
    }

    public static void SimilarMatcherUsesFineSymbolProfile()
    {
        string source = File.ReadAllText(Path.Combine("Models", "SimilarSymbolMatcher.cs"));

        AssertTrue(
            source.Contains("public const int GridSide = 7", StringComparison.Ordinal) &&
            source.Contains("fine 7x7 ink-profile match", StringComparison.Ordinal) &&
            source.Contains("PrepareTemplate(ExtractTemplate", StringComparison.Ordinal) &&
            source.Contains("RemovePeripheralTemplateNoise", StringComparison.Ordinal) &&
            source.Contains("FindPeripheralTemplateNoiseComponents", StringComparison.Ordinal) &&
            source.Contains("BuildTemplateQualityWarning", StringComparison.Ordinal) &&
            source.Contains("BuildLooseTemplateSelectionWarning", StringComparison.Ordinal) &&
            source.Contains("TryFindTemplateInkBounds", StringComparison.Ordinal) &&
            source.Contains("TemplateDownsampleFactor", StringComparison.Ordinal) &&
            source.Contains("TemplateWarning", StringComparison.Ordinal),
            "Similar matcher should use a fine symbol layout profile, clean loose selections, preserve resolution from ink bounds, and warn on suspicious templates");
        AssertTrue(
            source.Contains("EdgeRelaxedScoreMultiplier = 0.93f", StringComparison.Ordinal),
            "Similar matcher should keep edge-relaxed matches below the default precision threshold");
        AssertTrue(
            source.Contains("ProjectionBandCount = 12", StringComparison.Ordinal) &&
            source.Contains("RowProjectionInkCounts", StringComparison.Ordinal) &&
            source.Contains("ColumnProjectionInkCounts", StringComparison.Ordinal) &&
            source.Contains("ProjectionColumnMasks", StringComparison.Ordinal) &&
            source.Contains("projectionScore", StringComparison.Ordinal) &&
            source.Contains("StrokeProfileCounts", StringComparison.Ordinal) &&
            source.Contains("BuildStrokeProfile", StringComparison.Ordinal) &&
            source.Contains("AddStrokeProfile", StringComparison.Ordinal) &&
            source.Contains("StrokeProfileScore", StringComparison.Ordinal) &&
            source.Contains("ScaleVariantFactors", StringComparison.Ordinal) &&
            source.Contains("ScaleTemplate", StringComparison.Ordinal) &&
            source.Contains("ScaledVariantScoreMultiplier", StringComparison.Ordinal) &&
            source.Contains("ScalePercent", StringComparison.Ordinal) &&
            source.Contains("StrokeScore", StringComparison.Ordinal),
            "Similar matcher should score row/column projection profiles, stroke orientation, and small scale variants for sharper silhouette matching");
        AssertTrue(
            source.Contains("FocusedWindowScoreMultiplier", StringComparison.Ordinal) &&
            source.Contains("FocusedWindowMinTemplateCoverage", StringComparison.Ordinal) &&
            source.Contains("FocusedWindowMaxCoreExtraShare", StringComparison.Ordinal) &&
            source.Contains("FocusedWindowMaxTotalExtraShare", StringComparison.Ordinal) &&
            source.Contains("CoreWindowMasks", StringComparison.Ordinal) &&
            source.Contains("SimilarWindowScore", StringComparison.Ordinal) &&
            source.Contains("TemplateCoverage", StringComparison.Ordinal) &&
            source.Contains("WindowPrecision", StringComparison.Ordinal) &&
            source.Contains("focusedWindowInk", StringComparison.Ordinal) &&
            source.Contains("focusedExtraCoreInk", StringComparison.Ordinal) &&
            source.Contains("focusedExtraInk", StringComparison.Ordinal) &&
            source.Contains("FocusedWindowTotalExtraLimit", StringComparison.Ordinal) &&
            source.Contains("focusedProjectionScore", StringComparison.Ordinal) &&
            source.Contains("UsedFocusedScore", StringComparison.Ordinal) &&
            source.Contains("CountFocusedWindowInk", StringComparison.Ordinal),
            "Similar matcher should recover true symbols from unrelated disconnected edge ink without ignoring extra interior symbol marks");
    }

    public static void ViewportRequiresReadableBitmapBeforeSimilarCount()
    {
        string source = File.ReadAllText(Path.Combine("Controls", "PdfViewport.SimilarCount.cs"));
        string viewport = File.ReadAllText(Path.Combine("Controls", "PdfViewport.cs"));

        AssertTrue(
            source.Contains("SimilarCountFallbackMinimumBitmapScale = 1.75f", StringComparison.Ordinal) &&
            source.Contains("SimilarCountMinimumBitmapScale = 2.0f", StringComparison.Ordinal) &&
            source.Contains("SimilarCountRequestedBitmapScale = 3.5f", StringComparison.Ordinal) &&
            source.Contains("SimilarCountMaxRenderPixels = 120_000_000f", StringComparison.Ordinal) &&
            source.Contains("SimilarCountRequestedBitmapScaleForCurrentPage", StringComparison.Ordinal) &&
            source.Contains("SimilarCountRequiredBitmapScaleForCurrentPage", StringComparison.Ordinal) &&
            source.Contains("IsSimilarCountRasterSheetPath", StringComparison.Ordinal) &&
            source.Contains("ViewportRenderPolicy.RasterSheetDisplayMaxDpi", StringComparison.Ordinal) &&
            source.Contains("TargetRasterSheetDpiForCurrentZoom", StringComparison.Ordinal) &&
            source.Contains("IsSimilarCountBitmapScaleReady", StringComparison.Ordinal) &&
            source.Contains("QueueSimilarCountReadableBitmap(forceSharper: true)", StringComparison.Ordinal) &&
            source.Contains("TryEnsureSimilarCountBitmapReady", StringComparison.Ordinal) &&
            source.Contains("QueueSimilarCountReadableBitmap()", StringComparison.Ordinal),
            "Similar count should guard against matching from a low-resolution preview bitmap and use the reachable raster-sheet DPI cap instead of waiting for an impossible 3.5x raster");
        AssertTrue(
            source.Contains("if (!TryEnsureSimilarCountBitmapReady(out string status))", StringComparison.Ordinal),
            "BeginSimilarCountSelection should check bitmap readiness before starting the crop interaction");
        AssertTrue(
            source.Contains("_similarCountWaitingForReadableBitmap", StringComparison.Ordinal) &&
            source.Contains("StartWaitingForSimilarCountReadableBitmap", StringComparison.Ordinal) &&
            source.Contains("TryStartPendingSimilarCountSelection", StringComparison.Ordinal) &&
            source.Contains("!IsSimilarCountBitmapScaleReady(_bitmapScale, SimilarCountRequiredBitmapScaleForCurrentPage())", StringComparison.Ordinal) &&
            source.Contains("_bitmapScale:0.##}x/{requiredScale:0.##}x", StringComparison.Ordinal) &&
            source.Contains("Selection will start automatically", StringComparison.Ordinal) &&
            source.Contains("_similarCountWaitingPageFolder", StringComparison.Ordinal) &&
            viewport.Contains("TryStartPendingSimilarCountSelection();", StringComparison.Ordinal),
            "Similar count should auto-enter selection after sharpening without requiring a second toolbar click");
        AssertTrue(
            source.Contains("public sealed record ViewportSimilarCountRequest", StringComparison.Ordinal) &&
            source.Contains("IReadOnlyList<SKPoint>? AlreadyCountedCentersPdf = null", StringComparison.Ordinal) &&
            source.Contains("string PdfPath = \"\"", StringComparison.Ordinal) &&
            source.Contains("int PdfPageIndex = -1", StringComparison.Ordinal) &&
            source.Contains("bool AllowExactTextMatches = true", StringComparison.Ordinal) &&
            source.Contains("double scaleMetersPerPt = ScaleMetersPerPt;", StringComparison.Ordinal) &&
            source.Contains("PdfPath: pdfPath", StringComparison.Ordinal) &&
            source.Contains("PdfPageIndex: pdfPageIndex", StringComparison.Ordinal),
            "Similar count should capture the scanned sheet scale with the selection request");
        AssertTrue(
            source.Contains("HasCurrentSimilarCountBitmap", StringComparison.Ordinal) &&
            source.Contains("IsPageBitmapFor(_pdfPath, _pdfIndex, _pageFolder)", StringComparison.Ordinal) &&
            source.Contains("_pdfIndex != _similarCountWaitingPdfIndex", StringComparison.Ordinal),
            "Similar count should only start and scan against the current page bitmap");
    }

    public static void SimilarCountRasterSheetsUseReachableDpiCap()
    {
        MethodInfo? method = typeof(PdfViewport).GetMethod(
            "SimilarCountReachableRasterSheetBitmapScale",
            BindingFlags.NonPublic | BindingFlags.Static);
        AssertTrue(method != null, "Similar Count raster-sheet scale helper should exist");

        object? result = method!.Invoke(null, new object[]
        {
            3.5f,
            2.0f,
            ViewportRenderPolicy.RasterSheetDisplayMaxDpi,
        });
        float scale = (float)(result ?? 0f);
        float expected = RasterSheetCacheService.RasterDpiToRenderScale(ViewportRenderPolicy.RasterSheetDisplayMaxDpi);
        AssertTrue(
            Math.Abs(scale - expected) < 0.001f,
            $"raster-sheet Similar Count should use reachable display DPI cap {expected:0.###}x, got {scale:0.###}x");

        object? lowZoomResult = method.Invoke(null, new object[]
        {
            3.5f,
            2.0f,
            72,
        });
        float lowZoomScale = (float)(lowZoomResult ?? 0f);
        AssertTrue(
            Math.Abs(lowZoomScale - 2.0f) < 0.001f,
            $"low-zoom raster-sheet Similar Count should still require the readable minimum 2.0x, got {lowZoomScale:0.###}x");
    }

    public static void SimilarMatcherCapsLargeSearchRaster()
    {
        string matcher = File.ReadAllText(Path.Combine("Models", "SimilarSymbolMatcher.cs"));
        string mainWindow = File.ReadAllText("MainWindow.SimilarCount.cs");
        MethodInfo? method = typeof(SimilarSymbolMatchSession).GetMethod(
            "SearchDownsampleFactor",
            BindingFlags.NonPublic | BindingFlags.Static);
        AssertTrue(method != null, "Similar matcher search downsample helper should exist");

        int mediumFactor = (int)(method!.Invoke(null, new object[] { 9000, 6000 }) ?? 0);
        int hugeFactor = (int)(method.Invoke(null, new object[] { 12000, 8000 }) ?? 0);

        AssertTrue(
            mediumFactor == 2 && hugeFactor == 4,
            $"large Similar scans should downsample full-page rasters before sliding-window search, got medium={mediumFactor}, huge={hugeFactor}");
        AssertTrue(
            matcher.Contains("MaxSearchRasterPixels", StringComparison.Ordinal) &&
            matcher.Contains("SearchDownsampleFactor(page.Width, page.Height)", StringComparison.Ordinal) &&
            matcher.Contains("(x & 255) == 0", StringComparison.Ordinal) &&
            matcher.Contains("(y & 15) == 0", StringComparison.Ordinal) &&
            matcher.Contains("FindMatchesNearCenters", StringComparison.Ordinal) &&
            matcher.Contains("public int SearchPixels", StringComparison.Ordinal),
            "Similar matcher should cap the search raster independently of template size, expose the active budget, and cancel inside long rows and near-center radius sweeps");
        AssertTrue(
            mainWindow.Contains("Similar count scan started", StringComparison.Ordinal) &&
            mainWindow.Contains("Similar count scan completed", StringComparison.Ordinal) &&
            mainWindow.Contains("searchPixels={session.SearchPixels}", StringComparison.Ordinal),
            "Similar Count runtime logs should report scan duration and the matcher budget");
    }

    public static void FindsFarCopiesOnDownsampledLargeRaster()
    {
        var placements = new[] { (380, 360), (560, 360), (5950, 1640), (6130, 1640) };
        using SKBitmap page = BuildLargePageWithSimilarCopies(placements);
        SimilarSymbolMatchSession? session = SimilarSymbolMatchSession.TryCreate(
            page,
            new SKRectI(
                placements[0].Item1 - 90,
                placements[0].Item2 - 18,
                placements[0].Item1 + SymbolWidth + 10,
                placements[0].Item2 + SymbolHeight + 8),
            out string error);

        AssertTrue(session != null, $"large raster session creation failed: {error}");
        AssertTrue(session!.DownsampleFactor == 2,
            $"large raster should exercise the full-page downsample path, got factor {session.DownsampleFactor}");

        List<SimilarSymbolMatch> matches = session.FindMatches(
            DefaultThreshold(),
            includeRotations: false,
            includeMirrored: false,
            CancellationToken.None);

        AssertTrue(matches.Count == placements.Length,
            $"expected all four identical symbols, including far-side copies, got {matches.Count}: " +
            string.Join(", ", matches.Select(match => $"{match.CenterX},{match.CenterY}:{match.Score:0.00}")));
        foreach ((int x, int y) in placements)
        {
            AssertTrue(
                matches.Any(match => NearCenter(match, x, y)),
                $"large downsampled raster missed identical far-side symbol at ({x},{y})");
        }
    }

    public static void VerifiesTextCandidateWindowsByRaster()
    {
        var placements = new[] { (80, 90), (240, 90), (80, 230), (240, 230) };
        using SKBitmap page = BuildPage(placements, rotatedAt: null, withDistractors: false);
        SimilarSymbolMatchSession? session = CreateSession(page);
        AssertTrue(session != null, "candidate-window session should be created");

        var textCandidateCenters = new List<SKPoint>
        {
            new(placements[0].Item1 + SymbolWidth / 2f + 3, placements[0].Item2 + SymbolHeight / 2f - 2),
            new(placements[1].Item1 + SymbolWidth / 2f - 2, placements[1].Item2 + SymbolHeight / 2f + 3),
            new(placements[2].Item1 + SymbolWidth / 2f + 1, placements[2].Item2 + SymbolHeight / 2f + 1),
            new(placements[3].Item1 + SymbolWidth / 2f - 4, placements[3].Item2 + SymbolHeight / 2f - 1),
            new(345, 245),
        };

        List<SimilarSymbolMatch> matches = session!.FindMatchesNearCenters(
            textCandidateCenters,
            searchRadiusPixels: 18,
            DefaultThreshold(),
            includeRotations: false,
            includeMirrored: false,
            CancellationToken.None);

        AssertTrue(matches.Count == placements.Length,
            $"text candidate raster verification should keep the four real symbols and reject the empty same-mark location, got {matches.Count}");
        foreach ((int x, int y) in placements)
            AssertTrue(matches.Any(match => NearCenter(match, x, y)), $"candidate raster verification missed ({x},{y})");
    }

    public static void TextGuidedRasterCanRecoverMultipleSymbolsPerLabel()
    {
        var placements = new[] { (80, 90), (135, 90), (260, 90) };
        using SKBitmap page = BuildPage(placements, rotatedAt: null, withDistractors: false);
        SimilarSymbolMatchSession? session = CreateSession(page);

        var textCandidateCenters = new List<SKPoint>
        {
            new(135, 105),
            new(282, 105),
        };

        List<SimilarSymbolMatch> matches = session!.FindMatchesNearCenters(
            textCandidateCenters,
            searchRadiusPixels: 44,
            DefaultThreshold(),
            includeRotations: false,
            includeMirrored: false,
            CancellationToken.None);

        AssertTrue(matches.Count == placements.Length,
            $"one repeated text label can point at multiple nearby symbols; expected {placements.Length}, got {matches.Count}: " +
            string.Join(", ", matches.Select(match => $"{match.CenterX},{match.CenterY}:{match.Score:0.00}")));
        foreach ((int x, int y) in placements)
            AssertTrue(matches.Any(match => NearCenter(match, x, y)), $"text-guided raster missed symbol near one label at ({x},{y})");
    }

    public static void ExactTextGuidedRadiusCoversNearbyRepeatedSymbols()
    {
        MethodInfo? method = typeof(MainWindow).GetMethod(
            "SimilarCountTextCandidateSearchRadiusPixels",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: [typeof(ViewportSimilarCountRequest), typeof(float)],
            modifiers: null);
        AssertTrue(method != null, "Similar Count text candidate radius helper should exist");
        MethodInfo? nearbyMethod = typeof(MainWindow).GetMethod(
            "SimilarCountTextCandidateSearchRadiusPixels",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: [typeof(ViewportSimilarCountRequest), typeof(float), typeof(PdfSimilarTextMatch)],
            modifiers: null);
        AssertTrue(nearbyMethod != null, "Similar Count nearby text candidate radius helper should exist");

        var exactTextRequest = new ViewportSimilarCountRequest(
            new SKRect(10, 10, 30, 26),
            "Pages\\sample",
            1,
            AllowExactTextMatches: true,
            UseTextCandidateRasterMatches: false);
        int exactRadius = (int)(method!.Invoke(null, new object[] { exactTextRequest, 2f }) ?? 0);

        AssertTrue(
            exactRadius >= 128,
            $"manual exact-text Similar should search at least 64 PDF pt around each label at 2x, got {exactRadius}px");

        var explicitBeamRadiusRequest = exactTextRequest with
        {
            AllowExactTextMatches = false,
            UseTextCandidateRasterMatches = true,
            TextCandidateSearchRadiusPdf = 24f,
        };
        int explicitRadius = (int)(method.Invoke(null, new object[] { explicitBeamRadiusRequest, 2f }) ?? 0);

        AssertTrue(
            exactRadius > explicitRadius && explicitRadius == 48,
            $"explicit Beam/Openings text radius should stay request-driven at 24 PDF pt/2x, got exact={exactRadius}px explicit={explicitRadius}px");

        var nearbyTextAnchor = new PdfSimilarTextMatch(
            "HDUE3",
            new SKRect(72, 12, 92, 24),
            new SKPoint(82, 18));
        int nearbyRadius = (int)(nearbyMethod!.Invoke(null, new object[]
        {
            exactTextRequest,
            2f,
            nearbyTextAnchor,
        }) ?? 0);

        AssertTrue(
            nearbyRadius > exactRadius && nearbyRadius >= 256,
            $"nearby mark-text Similar should widen raster search enough for opposite-side symbols, got exact={exactRadius}px nearby={nearbyRadius}px");
    }

    public static void NearbyTextGuideDoesNotBecomeTextOnlyMarkers()
    {
        MethodInfo? useRasterMethod = typeof(MainWindow).GetMethod(
            "ShouldUseTextGuidedRasterMatchesForExactText",
            BindingFlags.NonPublic | BindingFlags.Static);
        AssertTrue(useRasterMethod != null, "Similar Count exact text/raster choice helper should exist");
        MethodInfo? usableTextMethod = typeof(MainWindow).GetMethod(
            "IsUsableSimilarTextResult",
            BindingFlags.NonPublic | BindingFlags.Static);
        AssertTrue(usableTextMethod != null, "Similar Count usable text-result guard should exist");

        var oneRaster = new List<SimilarSymbolMatch>
        {
            new(10, 10, 0.98f, 0),
        };
        var twoText = new List<SimilarSymbolMatch>
        {
            new(10, 10, 1f, 0),
            new(80, 10, 1f, 0),
        };

        bool selectedTextUsesRaster = (bool)(useRasterMethod!.Invoke(null, new object[] { oneRaster, twoText, true }) ?? false);
        bool nearbyTextUsesRaster = (bool)(useRasterMethod.Invoke(null, new object[] { oneRaster, twoText, false }) ?? false);

        AssertTrue(
            !selectedTextUsesRaster && nearbyTextUsesRaster,
            "text selected inside the box may fall back to text-only when raster is less complete, but nearby text must stay raster-only");

        var broadTextResult = new PdfSimilarTextResult
        {
            Query = "A",
            Matches = Enumerable.Range(0, 241)
                .Select(index => new PdfSimilarTextMatch(
                    "A",
                    new SKRect(index, 0, index + 1, 1),
                    new SKPoint(index + 0.5f, 0.5f)))
                .ToList(),
        };
        bool broadTextUsable = (bool)(usableTextMethod!.Invoke(null, new object[] { broadTextResult }) ?? true);
        AssertTrue(!broadTextUsable,
            "overly broad PDF text guides should be ignored so Similar does not scan hundreds of weak text windows");

        MethodInfo? selectedTextMethod = typeof(MainWindow).GetMethod(
            "SimilarTextAnchorLooksSelectedText",
            BindingFlags.NonPublic | BindingFlags.Static);
        AssertTrue(selectedTextMethod != null, "Similar Count selected-text guard should exist");

        var incidentalText = new PdfSimilarTextMatch(
            "H6",
            new SKRect(2, 2, 10, 10),
            new SKPoint(6, 6));
        bool incidentalLooksSelected = (bool)(selectedTextMethod!.Invoke(null, new object[]
        {
            incidentalText,
            new ViewportSimilarCountRequest(new SKRect(0, 0, 28, 28), "Pages\\sample", 1),
        }) ?? true);
        bool tightTextLooksSelected = (bool)(selectedTextMethod.Invoke(null, new object[]
        {
            incidentalText,
            new ViewportSimilarCountRequest(new SKRect(0, 0, 11, 11), "Pages\\sample", 1),
        }) ?? false);

        AssertTrue(
            !incidentalLooksSelected && tightTextLooksSelected,
            "small text inside a larger symbol box should guide raster only, while a tight text box can still use exact text matching");

        var repeatedMarkResult = new PdfSimilarTextResult
        {
            Query = "HDUE3",
            Matches = Enumerable.Range(0, 10)
                .Select(index => new PdfSimilarTextMatch(
                    "HDUE3",
                    new SKRect(index * 20, 0, index * 20 + 16, 8),
                    new SKPoint(index * 20 + 8, 4)))
                .ToList(),
        };

        MethodInfo? weakTextCandidatesMethod = typeof(MainWindow).GetMethod(
            "BuildWeakTextCandidateReviewMatches",
            BindingFlags.NonPublic | BindingFlags.Static);
        AssertTrue(weakTextCandidatesMethod != null, "Similar Count weak text-candidate helper should exist");

        var sourceText = new PdfSimilarTextMatch(
            "HDUE3",
            new SKRect(94, 94, 106, 106),
            new SKPoint(100, 100));
        var dualSideTextResult = new PdfSimilarTextResult
        {
            Query = "HDUE3",
            Matches =
            [
                sourceText,
                new PdfSimilarTextMatch(
                    "HDUE3",
                    new SKRect(194, 94, 206, 106),
                    new SKPoint(200, 100)),
            ],
        };
        var markerLeftOfTextRequest = new ViewportSimilarCountRequest(
            new SKRect(64, 104, 76, 116),
            "Pages\\sample",
            1,
            TemplateAnchorPdf: new SKPoint(70, 110));
        var dualSideCandidates = (List<SimilarSymbolMatch>)(weakTextCandidatesMethod!.Invoke(null, new object[]
        {
            dualSideTextResult,
            sourceText,
            markerLeftOfTextRequest,
            1f,
        }) ?? new List<SimilarSymbolMatch>());

        bool hasOriginalOffset = dualSideCandidates.Any(match => match.CenterX == 70 && match.CenterY == 110);
        bool hasMirroredOffset = dualSideCandidates.Any(match => match.CenterX == 130 && match.CenterY == 110);
        bool hasFarOriginalOffset = dualSideCandidates.Any(match => match.CenterX == 170 && match.CenterY == 110);
        bool hasFarMirroredOffset = dualSideCandidates.Any(match => match.CenterX == 230 && match.CenterY == 110);
        AssertTrue(
            hasOriginalOffset &&
            hasMirroredOffset &&
            hasFarOriginalOffset &&
            hasFarMirroredOffset &&
            !dualSideCandidates.Any(match => match.CenterX == 100 && match.CenterY == 100),
            "HDUE3-like text-guided Similar should review marker/text offsets on both sides, not drop back to text-label centers");

        var markerAboveLeftOfTextRequest = new ViewportSimilarCountRequest(
            new SKRect(64, 64, 76, 76),
            "Pages\\sample",
            1,
            TemplateAnchorPdf: new SKPoint(70, 70));
        var quadSideCandidates = (List<SimilarSymbolMatch>)(weakTextCandidatesMethod.Invoke(null, new object[]
        {
            dualSideTextResult,
            sourceText,
            markerAboveLeftOfTextRequest,
            1f,
        }) ?? new List<SimilarSymbolMatch>());

        AssertTrue(
            quadSideCandidates.Any(match => match.CenterX == 70 && match.CenterY == 70) &&
            quadSideCandidates.Any(match => match.CenterX == 130 && match.CenterY == 70) &&
            quadSideCandidates.Any(match => match.CenterX == 70 && match.CenterY == 130) &&
            quadSideCandidates.Any(match => match.CenterX == 130 && match.CenterY == 130) &&
            quadSideCandidates.Any(match => match.CenterX == 170 && match.CenterY == 70) &&
            quadSideCandidates.Any(match => match.CenterX == 230 && match.CenterY == 70) &&
            quadSideCandidates.Any(match => match.CenterX == 170 && match.CenterY == 130) &&
            quadSideCandidates.Any(match => match.CenterX == 230 && match.CenterY == 130),
            "text-guided Similar should try vertical and diagonal mirrored marker/text offsets for mirrored plan sides");

        MethodInfo? toleranceMethod = typeof(MainWindow).GetMethod(
            "SimilarCountTextFallbackTolerancePdf",
            BindingFlags.NonPublic | BindingFlags.Static);
        AssertTrue(toleranceMethod != null, "Similar Count text fallback tolerance helper should exist");

        float nearbyTolerance = (float)(toleranceMethod!.Invoke(null, new object[] { new SKRect(0, 0, 20, 16), false }) ?? 0f);
        AssertTrue(
            nearbyTolerance >= 64f,
            $"nearby repeated text fallback should cover HDUE3 labels about 56 PDF pt from the symbol, got {nearbyTolerance:0.##}");
    }

    public static void AllSheetsTextGuideKeepsManualTemplateOffset()
    {
        MethodInfo? method = typeof(MainWindow).GetMethod(
            "SimilarCountTextTemplateOffset",
            BindingFlags.NonPublic | BindingFlags.Static);
        AssertTrue(method != null, "Similar Count all-sheets text-guide template offset helper should exist");

        var textAnchor = new PdfSimilarTextMatch(
            "HDUE3",
            new SKRect(94, 94, 106, 106),
            new SKPoint(100, 100));
        var manualRequest = new ViewportSimilarCountRequest(
            new SKRect(68, 82, 88, 102),
            "Pages\\sample",
            1);
        SKPoint manualOffset = (SKPoint)(method!.Invoke(null, new object[]
        {
            textAnchor,
            manualRequest,
        }) ?? default(SKPoint));

        AssertTrue(
            Math.Abs(manualOffset.X + 22f) < 0.001f &&
            Math.Abs(manualOffset.Y + 8f) < 0.001f,
            $"manual all-sheets text guide should preserve template center offset from text, got {manualOffset.X:0.###},{manualOffset.Y:0.###}");

        var beamRequest = manualRequest with
        {
            TemplateAnchorPdf = new SKPoint(82, 108),
            MarkerCenterPdf = new SKPoint(72, 116),
        };
        SKPoint templateOffset = (SKPoint)(method.Invoke(null, new object[]
        {
            textAnchor,
            beamRequest,
        }) ?? default(SKPoint));

        AssertTrue(
            Math.Abs(templateOffset.X + 18f) < 0.001f &&
            Math.Abs(templateOffset.Y - 8f) < 0.001f,
            $"Beam/Openings all-sheets text guide should search at the template offset before applying marker offset once, got {templateOffset.X:0.###},{templateOffset.Y:0.###}");
    }

    public static void ViewportStatusUsesUiDispatcher()
    {
        string mainWindow = File.ReadAllText("MainWindow.xaml.cs");

        AssertTrue(
            mainWindow.Contains("_viewport.StatusChanged      += SetViewportStatus", StringComparison.Ordinal) &&
            mainWindow.Contains("private void SetViewportStatus(string msg)", StringComparison.Ordinal) &&
            mainWindow.Contains("Dispatcher.CheckAccess()", StringComparison.Ordinal) &&
            mainWindow.Contains("Dispatcher.BeginInvoke(new Action(() => TxtStatus.Text = msg))", StringComparison.Ordinal),
            "viewport status updates can arrive from render workers and must marshal TxtStatus updates to the UI dispatcher");
    }

    public static void SimilarCountPreviewSupportsReviewBeforeAdd()
    {
        string viewport = File.ReadAllText(Path.Combine("Controls", "PdfViewport.SimilarCount.cs"));
        string mainWindow = File.ReadAllText("MainWindow.SimilarCount.cs");
        string dialog = File.ReadAllText(Path.Combine("Dialogs", "SimilarCountDialog.cs"));

        AssertTrue(
            viewport.Contains("ViewportSimilarCountPreviewMarker", StringComparison.Ordinal) &&
            viewport.Contains("SimilarCountPreviewMarkerToggled", StringComparison.Ordinal) &&
            viewport.Contains("TryToggleSimilarCountPreviewMarker", StringComparison.Ordinal),
            "viewport should expose clickable Similar Count preview markers for review");
        AssertTrue(
            viewport.Contains("PostSimilarCountSelectionStatus", StringComparison.Ordinal) &&
            viewport.Contains("keep it tight around one symbol", StringComparison.Ordinal) &&
            viewport.Contains("drag a larger box", StringComparison.Ordinal),
            "Similar Count selection should guide the user toward a tight one-symbol template while dragging");
        AssertTrue(
            mainWindow.Contains("excludedIndexes", StringComparison.Ordinal) &&
            mainWindow.Contains("IncludedCenters()", StringComparison.Ordinal) &&
            mainWindow.Contains("SetSimilarCountPreviewMarkers(BuildPreviewMarkers(), request.PageFolder)", StringComparison.Ordinal) &&
            mainWindow.Contains("templateWarning: templateWarning", StringComparison.Ordinal),
            "Similar Count should keep include/exclude review state, add only included centers, and pass template warnings into review");
        AssertTrue(
            viewport.Contains("_similarCountPreviewPageFolder", StringComparison.Ordinal) &&
            viewport.Contains("IsSimilarCountPreviewForCurrentPage", StringComparison.Ordinal) &&
            viewport.Contains("pageFolder ?? _pageFolder", StringComparison.Ordinal),
            "Similar Count preview markers should stay bound to the scanned sheet while the modeless review is open");
        AssertTrue(
            dialog.Contains("public event EventHandler? Accepted", StringComparison.Ordinal) &&
            dialog.Contains("public event EventHandler? Cancelled", StringComparison.Ordinal) &&
            dialog.Contains("string templateWarning = \"\"", StringComparison.Ordinal) &&
            dialog.Contains("templateWarning.Trim()", StringComparison.Ordinal) &&
            mainWindow.Contains("dialog.Show();", StringComparison.Ordinal) &&
            !mainWindow.Contains("ShowDialog() == true", StringComparison.Ordinal),
            "Similar Count dialog should be modeless and keep template warnings visible while the sheet preview remains clickable");
    }

    public static void SimilarCountReviewChoicesSurviveRescan()
    {
        string mainWindow = File.ReadAllText("MainWindow.SimilarCount.cs");
        string normalizedMainWindow = mainWindow.Replace("\r\n", "\n", StringComparison.Ordinal);
        int scanIndex = normalizedMainWindow.IndexOf(
            "async Task<SimilarCountScanResult> ScanAsync",
            StringComparison.Ordinal);
        int clearIndex = scanIndex >= 0
            ? normalizedMainWindow.IndexOf("lastMatches.Clear();", scanIndex, StringComparison.Ordinal)
            : -1;
        int addIndex = scanIndex >= 0
            ? normalizedMainWindow.IndexOf("lastMatches.AddRange(matches);", scanIndex, StringComparison.Ordinal)
            : -1;
        int applyIndex = scanIndex >= 0
            ? normalizedMainWindow.IndexOf("ApplyDefaultSimilarReviewExclusions();", scanIndex, StringComparison.Ordinal)
            : -1;

        AssertTrue(
            mainWindow.Contains("manualReviewStatesByCenter", StringComparison.Ordinal) &&
            mainWindow.Contains("SimilarReviewKey", StringComparison.Ordinal) &&
            mainWindow.Contains("SimilarCountReviewKeyQuantumPx = 4", StringComparison.Ordinal) &&
            mainWindow.Contains("ApplyManualSimilarReviewChoices", StringComparison.Ordinal),
            "Similar Count review should store manual include/exclude choices by stable quantized match center");
        AssertTrue(
            scanIndex >= 0 &&
            clearIndex > scanIndex &&
            addIndex > clearIndex &&
            applyIndex > addIndex &&
            normalizedMainWindow.Contains(
                "ExcludeWeakSimilarMatches(includeTextCandidateReviewMatchesByDefault);\n            ExcludeAlreadyCountedSimilarMatches();\n            ApplyManualSimilarReviewChoices();",
                StringComparison.Ordinal),
            "Similar Count rescans should rebuild default weak/duplicate state and then reapply manual choices");
        AssertTrue(
            mainWindow.Contains("manualReviewStatesByCenter[SimilarReviewKey(lastMatches[index])] = include", StringComparison.Ordinal) &&
            mainWindow.Contains("RememberCurrentSimilarReviewChoices(include: true)", StringComparison.Ordinal) &&
            mainWindow.Contains("void ClearManualSimilarReviewChoices() => manualReviewStatesByCenter.Clear();", StringComparison.Ordinal) &&
            mainWindow.Contains("ClearManualSimilarReviewChoices();", StringComparison.Ordinal),
            "Similar Count marker toggles and review quick actions should update stable manual choices");
    }

    public static void SimilarCountIgnoresCancelledStaleScans()
    {
        string mainWindow = File.ReadAllText("MainWindow.SimilarCount.cs");
        string dialog = File.ReadAllText(Path.Combine("Dialogs", "SimilarCountDialog.cs"));
        string normalizedMainWindow = mainWindow.Replace("\r\n", "\n", StringComparison.Ordinal);
        int scanIndex = normalizedMainWindow.IndexOf(
            "async Task<SimilarCountScanResult> ScanAsync",
            StringComparison.Ordinal);
        int staleClearIndex = scanIndex >= 0
            ? normalizedMainWindow.IndexOf("ClearStaleSimilarReviewPreview();", scanIndex, StringComparison.Ordinal)
            : -1;
        int scanStartedLogIndex = scanIndex >= 0
            ? normalizedMainWindow.IndexOf("Similar count scan started", scanIndex, StringComparison.Ordinal)
            : -1;
        int cancellationCheckIndex = normalizedMainWindow.IndexOf(
            "cancellationToken.ThrowIfCancellationRequested();",
            StringComparison.Ordinal);
        int addMatchesIndex = normalizedMainWindow.IndexOf(
            "lastMatches.AddRange(matches);",
            StringComparison.Ordinal);

        AssertTrue(
            normalizedMainWindow.Contains(
                "void ClearStaleSimilarReviewPreview()\n        {\n            lastMatches.Clear();\n            excludedIndexes.Clear();\n            alreadyCountedIndexes.Clear();\n            _viewport.SetSimilarCountPreviewMarkers(null);\n        }",
                StringComparison.Ordinal) &&
            staleClearIndex > scanIndex &&
            staleClearIndex < scanStartedLogIndex,
            "Similar Count should clear stale review candidates and preview markers as soon as a new scan starts");
        AssertTrue(
            cancellationCheckIndex >= 0 &&
            addMatchesIndex > cancellationCheckIndex,
            "Similar Count should check cancellation before a completed stale scan can replace review candidates");
        AssertTrue(
            dialog.Contains("_scanCts?.Cancel();", StringComparison.Ordinal) &&
            dialog.Contains("AllSheetsScanTimeout", StringComparison.Ordinal) &&
            dialog.Contains("if (request.AllSheets)", StringComparison.Ordinal) &&
            dialog.Contains("cts.CancelAfter(AllSheetsScanTimeout)", StringComparison.Ordinal) &&
            dialog.Contains("All-sheets scan stopped after", StringComparison.Ordinal) &&
            dialog.Contains("Current-sheet results stay available", StringComparison.Ordinal) &&
            !dialog.Contains("CurrentSheetScanTimeout", StringComparison.Ordinal) &&
            dialog.Contains("_closed", StringComparison.Ordinal) &&
            dialog.Contains("_scanRequestedWhileRunning", StringComparison.Ordinal) &&
            dialog.Contains("RunLatestScanAsync", StringComparison.Ordinal) &&
            dialog.Contains("ReferenceEquals(_scanCts, cts)", StringComparison.Ordinal) &&
            dialog.Contains("catch (OperationCanceledException)", StringComparison.Ordinal),
            "Similar Count dialog should cancel superseded scans, bound only all-sheets scans, keep current-sheet scans running to completion, and ignore stale cancellation results");
    }

    public static void SimilarCountPreviewShowsConfidence()
    {
        string viewport = File.ReadAllText(Path.Combine("Controls", "PdfViewport.SimilarCount.cs"));
        string mainWindow = File.ReadAllText("MainWindow.SimilarCount.cs");
        string dialog = File.ReadAllText(Path.Combine("Dialogs", "SimilarCountDialog.cs"));

        AssertTrue(
            viewport.Contains("float Score = 1f", StringComparison.Ordinal) &&
            viewport.Contains("float TemplateCoverage = 1f", StringComparison.Ordinal) &&
            viewport.Contains("float WindowPrecision = 1f", StringComparison.Ordinal) &&
            viewport.Contains("int ScalePercent = 100", StringComparison.Ordinal) &&
            viewport.Contains("float StrokeScore = 1f", StringComparison.Ordinal) &&
            viewport.Contains("bool UsedFocusedScore = false", StringComparison.Ordinal) &&
            viewport.Contains("marker.Score < (float)AppSettingsStore.SimilarCountThresholdDefault", StringComparison.Ordinal),
            "Similar preview markers should carry and visualize match confidence and score diagnostics");
        AssertTrue(
            viewport.Contains("SimilarCountPreviewMarkerHitIndex", StringComparison.Ordinal) &&
            viewport.Contains("TryPostSimilarCountPreviewMarkerStatus", StringComparison.Ordinal) &&
            viewport.Contains("SimilarCountPreviewMarkerStatus", StringComparison.Ordinal) &&
            viewport.Contains("click to exclude", StringComparison.Ordinal) &&
            viewport.Contains("already counted", StringComparison.Ordinal) &&
            viewport.Contains("scale {marker.ScalePercent}%", StringComparison.Ordinal) &&
            viewport.Contains("SimilarCountScorePercent", StringComparison.Ordinal) &&
            viewport.Contains("coverage", StringComparison.Ordinal) &&
            viewport.Contains("precision", StringComparison.Ordinal) &&
            viewport.Contains("ink", StringComparison.Ordinal) &&
            viewport.Contains("layout", StringComparison.Ordinal) &&
            viewport.Contains("stroke", StringComparison.Ordinal) &&
            viewport.Contains("SimilarCountLimitLabel", StringComparison.Ordinal) &&
            viewport.Contains("limit", StringComparison.Ordinal) &&
            viewport.Contains("extra ink ignored", StringComparison.Ordinal),
            "Similar preview markers should explain hover status, confidence, review state, click action, and match diagnostics");
        AssertTrue(
            mainWindow.Contains("lastMatches", StringComparison.Ordinal) &&
            mainWindow.Contains("match.Score", StringComparison.Ordinal) &&
            mainWindow.Contains("TemplateCoverage: match.TemplateCoverage", StringComparison.Ordinal) &&
            mainWindow.Contains("WindowPrecision: match.WindowPrecision", StringComparison.Ordinal) &&
            mainWindow.Contains("match.ScalePercent", StringComparison.Ordinal) &&
            mainWindow.Contains("StrokeScore: match.StrokeScore", StringComparison.Ordinal) &&
            mainWindow.Contains("UsedFocusedScore: match.UsedFocusedScore", StringComparison.Ordinal) &&
            mainWindow.Contains("SimilarCountScanResult", StringComparison.Ordinal),
            "Similar Count should preserve matcher scores and diagnostics through the review flow");
        AssertTrue(
            dialog.Contains("ScoreSuffix()", StringComparison.Ordinal) &&
            dialog.Contains("result.MinScore", StringComparison.Ordinal) &&
            dialog.Contains("result.MaxScore", StringComparison.Ordinal),
            "Similar Count dialog should show score range for the current review set");
    }

    public static void SimilarCountReviewSummaryExplainsCandidates()
    {
        string mainWindow = File.ReadAllText("MainWindow.SimilarCount.cs");
        string dialog = File.ReadAllText(Path.Combine("Dialogs", "SimilarCountDialog.cs"));

        AssertTrue(
            dialog.Contains("WeakCount = 0", StringComparison.Ordinal) &&
            dialog.Contains("AlreadyCountedCount = 0", StringComparison.Ordinal) &&
            dialog.Contains("LimitSummary = \"\"", StringComparison.Ordinal) &&
            dialog.Contains("NewCandidateCount", StringComparison.Ordinal) &&
            dialog.Contains("ExcludedNewCount", StringComparison.Ordinal),
            "Similar Count scan result should carry new, weak, already-counted, excluded, and limit-summary diagnostics");
        AssertTrue(
            dialog.Contains("_reviewDetailsLabel", StringComparison.Ordinal) &&
            dialog.Contains("ReviewDetails", StringComparison.Ordinal) &&
            dialog.Contains("Review {result.NewCandidateCount} candidates before Add", StringComparison.Ordinal) &&
            dialog.Contains("Already counted", StringComparison.Ordinal) &&
            dialog.Contains("Excluded", StringComparison.Ordinal) &&
            dialog.Contains("result.LimitSummary", StringComparison.Ordinal),
            "Similar Count dialog should explain the review state, including all-weak candidate sets, and weak-match limit summary in a compact details line");
        AssertTrue(
            mainWindow.Contains("WeakSimilarMatchCount()", StringComparison.Ordinal) &&
            mainWindow.Contains("SimilarCountLimitSummary()", StringComparison.Ordinal) &&
            mainWindow.Contains("SimilarCountLimitLabel", StringComparison.Ordinal) &&
            mainWindow.Contains("Weak limit:", StringComparison.Ordinal) &&
            mainWindow.Contains("Text-guided candidates waiting for review", StringComparison.Ordinal) &&
            mainWindow.Contains("candidate(s) waiting for review", StringComparison.Ordinal) &&
            mainWindow.Contains("SimilarReviewStatus", StringComparison.Ordinal) &&
            mainWindow.Contains("result.LimitSummary", StringComparison.Ordinal) &&
            mainWindow.Contains("result.AlreadyCountedCount", StringComparison.Ordinal),
            "Similar Count status text should report weak candidates, already-counted candidates, and the dominant weak-match limiter");
    }

    public static void SimilarCountThresholdPresetsAreAvailable()
    {
        string dialog = File.ReadAllText(Path.Combine("Dialogs", "SimilarCountDialog.cs"));

        AssertTrue(
            dialog.Contains("StrictThresholdPreset = 0.98", StringComparison.Ordinal) &&
            dialog.Contains("LooseThresholdPreset = 0.82", StringComparison.Ordinal) &&
            dialog.Contains("Content = \"Strict\"", StringComparison.Ordinal) &&
            dialog.Contains("Content = \"Default\"", StringComparison.Ordinal) &&
            dialog.Contains("Content = \"Loose\"", StringComparison.Ordinal),
            "Similar Count dialog should expose Strict, Default, and Loose threshold presets");
        AssertTrue(
            dialog.Contains("ApplyThresholdPreset", StringComparison.Ordinal) &&
            dialog.Contains("AppSettingsStore.SimilarCountThresholdDefault", StringComparison.Ordinal) &&
            dialog.Contains("TimeSpan.FromMilliseconds(650)", StringComparison.Ordinal) &&
            dialog.Contains("_ = RunScanAsync();", StringComparison.Ordinal),
            "Similar Count threshold presets should update the slider and rescan the current review without flooding scans while dragging");
        AssertTrue(
            dialog.Contains("_suppressThresholdScan", StringComparison.Ordinal),
            "Similar Count threshold presets should avoid duplicate slider debounce scans");
    }

    public static void SimilarCountWeakMatchesStartReviewOnly()
    {
        string viewport = File.ReadAllText(Path.Combine("Controls", "PdfViewport.SimilarCount.cs"));
        string mainWindow = File.ReadAllText("MainWindow.SimilarCount.cs");
        string dialog = File.ReadAllText(Path.Combine("Dialogs", "SimilarCountDialog.cs"));
        string normalizedMainWindow = mainWindow.Replace("\r\n", "\n", StringComparison.Ordinal);

        AssertTrue(
            mainWindow.Contains("IsWeakSimilarMatch", StringComparison.Ordinal) &&
            mainWindow.Contains("ExcludeWeakSimilarMatches(bool includeTextCandidatesByDefault)", StringComparison.Ordinal) &&
            normalizedMainWindow.Contains("excludedIndexes.Clear();\n            ExcludeWeakSimilarMatches(includeTextCandidateReviewMatchesByDefault);", StringComparison.Ordinal) &&
            normalizedMainWindow.Contains("ExcludeWeakSimilarMatches(includeTextCandidatesByDefault: false);", StringComparison.Ordinal) &&
            normalizedMainWindow.Contains(
                "void IncludeAllPreviewMarkers(object? sender, EventArgs e)\n        {\n            RememberCurrentSimilarReviewChoices(include: true);\n            ApplyDefaultSimilarReviewExclusions();\n            RefreshPreviewReview();",
                StringComparison.Ordinal),
            "Similar Count should make below-default confidence and text-only fallback matches review-only unless they are explicitly included");
        AssertTrue(
            dialog.Contains("IncludeAllRequested", StringComparison.Ordinal) &&
            dialog.Contains("StrongOnlyRequested", StringComparison.Ordinal) &&
            dialog.Contains("Include candidates", StringComparison.Ordinal) &&
            dialog.Contains("Include review candidates", StringComparison.Ordinal) &&
            dialog.Contains("Include candidates to enable Add", StringComparison.Ordinal) &&
            dialog.Contains("_includeAllButton.IsDefault = !canAdd && canInclude", StringComparison.Ordinal) &&
            dialog.Contains("ResetReviewActionButtons", StringComparison.Ordinal) &&
            dialog.Contains("_includeAllButton.Content = \"Include candidates\"", StringComparison.Ordinal) &&
            dialog.Contains("orange weak review markers", StringComparison.Ordinal) &&
            dialog.Contains("Strong only", StringComparison.Ordinal),
            "Similar Count review should expose quick actions to include candidates, make that action primary while Add is disabled, reset stale include-button state, or return to strong matches only");
        AssertTrue(
            viewport.Contains("canvas.DrawCircle(center, radius, weakerMatch ? weakStroke : excludedStroke)", StringComparison.Ordinal),
            "Excluded weak Similar candidates should keep a distinct low-confidence ring");
    }

    public static void SimilarCountSkipsAlreadyCountedMarkers()
    {
        string viewport = File.ReadAllText(Path.Combine("Controls", "PdfViewport.SimilarCount.cs"));
        string mainWindow = File.ReadAllText("MainWindow.SimilarCount.cs");

        AssertTrue(
            viewport.Contains("bool AlreadyCounted = false", StringComparison.Ordinal) &&
            viewport.Contains("marker.AlreadyCounted", StringComparison.Ordinal) &&
            viewport.Contains("countedStroke", StringComparison.Ordinal),
            "Similar preview markers should visibly distinguish already-counted destination points");
        AssertTrue(
            mainWindow.Contains("alreadyCountedIndexes", StringComparison.Ordinal) &&
            mainWindow.Contains("ExcludeAlreadyCountedSimilarMatches()", StringComparison.Ordinal) &&
            mainWindow.Contains("this marker is already counted", StringComparison.Ordinal),
            "Similar Count review should exclude and lock already-counted matches");
        AssertTrue(
            mainWindow.Contains("private int AddSimilarCountMeasurements", StringComparison.Ordinal) &&
            mainWindow.Contains("IsSimilarCountDuplicateCenter", StringComparison.Ordinal) &&
            mainWindow.Contains("skipped {skipped} already counted", StringComparison.Ordinal),
            "Similar Count Add should defensively skip duplicate centers in the destination takeoff");
    }

    public static void SimilarCountLocksDestinationTakeoff()
    {
        string mainWindow = File.ReadAllText("MainWindow.SimilarCount.cs");
        string dialog = File.ReadAllText(Path.Combine("Dialogs", "SimilarCountDialog.cs"));

        AssertTrue(
            mainWindow.Contains("TakeoffItem? destinationItem = RequestedSimilarCountDestinationItem(request)", StringComparison.Ordinal) &&
            mainWindow.Contains("bool canRenameDestination = destinationItem == null", StringComparison.Ordinal) &&
            mainWindow.Contains("string destinationName = SimilarCountDestinationName(destinationItem, request, textResult)", StringComparison.Ordinal) &&
            mainWindow.Contains("OurPlaneCoreJob reviewJob = _currentJob;", StringComparison.Ordinal) &&
            mainWindow.Contains("PageInfo reviewPage = _currentPage;", StringComparison.Ordinal),
            "Similar Count should capture an explicit Beam/Openings destination, while manual Similar starts a new named item");
        AssertTrue(
            mainWindow.Contains("acceptedDestinationName = dialog.DestinationName", StringComparison.Ordinal) &&
            mainWindow.Contains("AddSimilarCountMeasurements(", StringComparison.Ordinal) &&
            mainWindow.Contains("acceptedDestinationName,", StringComparison.Ordinal) &&
            mainWindow.Contains("out addedItem", StringComparison.Ordinal) &&
            mainWindow.Contains("ResolveOrCreateSimilarCountItem(", StringComparison.Ordinal),
            "Similar Count should add reviewed markers to the dialog-named destination and reuse that item for off-sheet additions");
        AssertTrue(
            mainWindow.Contains("original job changed; review was not added", StringComparison.Ordinal) &&
            mainWindow.Contains("QueueSimilarCountAiRequest(reviewJob, reviewPage, request, added, acceptedDestinationName)", StringComparison.Ordinal) &&
            mainWindow.Contains("AI double-check skipped because the scanned sheet is not open", StringComparison.Ordinal),
            "Similar Count review should not write into a changed job or queue AI context for the wrong sheet");
        AssertTrue(
            mainWindow.Contains("ScaleMetersPerPt = request.ScaleMetersPerPt", StringComparison.Ordinal),
            "Similar Count measurements should keep the scanned sheet scale even if the user switches sheets while reviewing");
        AssertTrue(
            dialog.Contains("Title = $\"Count Similar: {_destinationName}\"", StringComparison.Ordinal) &&
            dialog.Contains("public string DestinationName", StringComparison.Ordinal) &&
            dialog.Contains("Takeoff name:", StringComparison.Ordinal) &&
            dialog.Contains("allowDestinationNameEdit", StringComparison.Ordinal) &&
            dialog.Contains("AddButtonText", StringComparison.Ordinal),
            "Similar Count review should show and edit the destination takeoff name before Add when creating a new item");
    }

    public static void SimilarCountHandlesSwitchedSheetAddStatus()
    {
        string mainWindow = File.ReadAllText("MainWindow.SimilarCount.cs");
        string normalizedMainWindow = mainWindow.Replace("\r\n", "\n", StringComparison.Ordinal);

        AssertTrue(
            mainWindow.Contains("bool scannedSheetIsOpen = IsSamePageFolder(_currentPage?.FolderPath, request.PageFolder)", StringComparison.Ordinal) &&
            normalizedMainWindow.Contains(
                "if (scannedSheetIsOpen)\n        {\n            _viewport.AddGeneratedMeasurements(generated);\n            _viewport.SelectMeasurements(generated);\n        }",
                StringComparison.Ordinal),
            "Similar Count should only inject and select generated markers when the scanned sheet is still open");
        AssertTrue(
            mainWindow.Contains("SimilarCountAddedStatus", StringComparison.Ordinal) &&
            mainWindow.Contains("Open the scanned sheet to review them", StringComparison.Ordinal) &&
            mainWindow.Contains("They stay selected for review", StringComparison.Ordinal),
            "Similar Count add status should distinguish open-sheet review from switched-sheet saves");
    }

    public static void BeamOpeningsCompletionCanLaunchSimilarReview()
    {
        string dialog = File.ReadAllText(Path.Combine("Dialogs", "NewItemDialog.cs"));
        string beamTool = File.ReadAllText("MainWindow.BeamTool.cs");
        string similarCount = File.ReadAllText("MainWindow.SimilarCount.cs");

        AssertTrue(
            dialog.Contains("MarkSimilarOnCurrentSheet", StringComparison.Ordinal) &&
            dialog.Contains("showSimilarReviewOption", StringComparison.Ordinal) &&
            dialog.Contains("Review similar on this sheet", StringComparison.Ordinal),
            "NewItemDialog should expose an opt-in Similar review checkbox only when a caller requests it");
        AssertTrue(
            beamTool.Contains("showSimilarReviewOption: true", StringComparison.Ordinal) &&
            beamTool.Contains("Review similar Beam marks on this sheet", StringComparison.Ordinal) &&
            beamTool.Contains("Review similar Opening marks on this sheet", StringComparison.Ordinal) &&
            beamTool.Contains("StartSimilarCountReview(BuildBeamSimilarCountRequest(request))", StringComparison.Ordinal) &&
            beamTool.Contains("StartSimilarCountReview(BuildOpeningSimilarCountRequest(request))", StringComparison.Ordinal),
            "Beam and Openings completion should optionally open Similar review against the newly created Count item");
        AssertTrue(
            beamTool.Contains("BuildBeamSimilarCountRequest", StringComparison.Ordinal) &&
            beamTool.Contains("BuildOpeningSimilarCountRequest", StringComparison.Ordinal) &&
            beamTool.Contains("[request.CountPointPdf]", StringComparison.Ordinal) &&
            beamTool.Contains("DestinationTakeoffFolderPath: _activeItem?.FolderPath", StringComparison.Ordinal) &&
            beamTool.Contains("DefaultDestinationName: _activeItem?.Name", StringComparison.Ordinal) &&
            beamTool.Contains("PreferNearestRepeatedText: true", StringComparison.Ordinal) &&
            beamTool.Contains("TextCandidateSearchRadiusPdf: textPadding", StringComparison.Ordinal) &&
            beamTool.Contains("IncludeTextCandidatesByDefault: false", StringComparison.Ordinal) &&
            beamTool.Contains("InitialIncludeMirrored: true", StringComparison.Ordinal) &&
            beamTool.Contains("SimilarOpeningPadding", StringComparison.Ordinal),
            "Beam/Openings Similar requests should mark the original measured item as already counted, target the newly created item, use auto text/raster matching tuned for measured objects, and keep unverified text-only fallback review-only");
        AssertTrue(
            similarCount.Contains("private void StartSimilarCountReview", StringComparison.Ordinal) &&
            similarCount.Contains("IsSimilarCountAlreadyCounted", StringComparison.Ordinal) &&
            similarCount.Contains("request.AlreadyCountedCentersPdf", StringComparison.Ordinal) &&
            similarCount.Contains("SimilarCountTextTemplateFallbackRequest", StringComparison.Ordinal) &&
            similarCount.Contains("BuildWeakTextCandidateReviewMatches", StringComparison.Ordinal) &&
            similarCount.Contains("AppendUnverifiedTextCandidateReviewMatches", StringComparison.Ordinal) &&
            similarCount.Contains("Text-guided candidates included; review before Add", StringComparison.Ordinal) &&
            similarCount.Contains("RetryStartSimilarCountReviewAsync", StringComparison.Ordinal) &&
            similarCount.Contains("ShouldRetrySimilarCountReviewReadiness", StringComparison.Ordinal),
            "Similar review should be reusable from Beam/Openings, skip the original measured center, include exact text candidates for review, and retry until the readable raster is ready");
    }

    public static void SimilarTextQueryFindsSplitMarkTokens()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "opc_similar_text_split", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string pdfPath = Path.Combine(tempDir, "split-mark.pdf");
        try
        {
            using (var stream = File.Create(pdfPath))
            using (var document = SKDocument.CreatePdf(stream))
            {
                SKCanvas canvas = document.BeginPage(320, 180);
                canvas.Clear(SKColors.White);
                using var text = new SKPaint
                {
                    Color = SKColors.Black,
                    TextSize = 12,
                    IsAntialias = true,
                    Typeface = SKTypeface.FromFamilyName("Arial"),
                };

                canvas.DrawText("HDUE3", 40, 40, text);
                canvas.DrawText("HDUE", 40, 82, text);
                canvas.DrawText("3", 77, 82, text);
                canvas.DrawText("HDUE3", 190, 40, text);
                canvas.DrawText("HDUE", 190, 82, text);
                canvas.DrawText("3", 227, 82, text);
                document.EndPage();
                document.Close();
            }

            bool ok = PdfSimilarTextService.TryFindSimilarTextByQuery(
                pdfPath,
                0,
                "HDUE3",
                out PdfSimilarTextResult result,
                out string error);

            AssertTrue(ok, $"similar text query should run against synthetic split-mark PDF: {error}");
            AssertTrue(result.Query == "HDUE3", $"normalized split-mark query should be HDUE3, got {result.Query}");
            AssertTrue(
                result.Matches.Count >= 4,
                $"query HDUE3 should return both whole-word and split-token occurrences, got {result.Matches.Count}: " +
                string.Join(", ", result.Matches.Select(match => $"{match.Text}@{match.Center.X:0.#},{match.Center.Y:0.#}")));
            AssertTrue(
                result.Matches.Count(match => string.Equals(match.Text, "HDUE3", StringComparison.OrdinalIgnoreCase)) >= 4,
                "split HDUE + 3 occurrences should be returned as HDUE3 candidates with combined bounds");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch { }
        }
    }

    public static void SimilarCountUsesExactPdfTextWhenAvailable()
    {
        string helper = File.ReadAllText(Path.Combine("Tools", "pdf_layers_helper.py"));
        string service = File.ReadAllText(Path.Combine("Models", "PdfSimilarTextService.cs"));
        string mainWindow = File.ReadAllText("MainWindow.SimilarCount.cs");
        string viewport = File.ReadAllText(Path.Combine("Controls", "PdfViewport.SimilarCount.cs"));
        string beamTool = File.ReadAllText("MainWindow.BeamTool.cs");

        AssertTrue(
            helper.Contains("def similar_text_data", StringComparison.Ordinal) &&
            helper.Contains("_similar_text_key", StringComparison.Ordinal) &&
            helper.Contains("prefer_nearest_repeated_text", StringComparison.Ordinal) &&
            helper.Contains("requested_query = _similar_text_key", StringComparison.Ordinal) &&
            helper.Contains("_similar_text_candidates", StringComparison.Ordinal) &&
            helper.Contains("_similar_text_payload_center_inside", StringComparison.Ordinal) &&
            helper.Contains("_similar_text_nearby_mark_key(key)", StringComparison.Ordinal) &&
            helper.Contains("key_counts", StringComparison.Ordinal) &&
            helper.Contains("distance_sq", StringComparison.Ordinal) &&
            helper.Contains("elif action == \"similartext\"", StringComparison.Ordinal) &&
            helper.Contains("\"similartext\"", StringComparison.Ordinal),
            "PyMuPDF helper should expose a similartext action for exact PDF word matches");
        AssertTrue(
            service.Contains("PdfSimilarTextService", StringComparison.Ordinal) &&
            service.Contains("TryInvokeHelper(", StringComparison.Ordinal) &&
            service.Contains("\"similartext\"", StringComparison.Ordinal) &&
            service.Contains("TryFindSimilarTextByQuery", StringComparison.Ordinal) &&
            service.Contains("PdfSimilarTextMatch", StringComparison.Ordinal) &&
            service.Contains("PreferNearestRepeatedText", StringComparison.Ordinal) &&
            service.Contains("PdfSimilarTextPoint", StringComparison.Ordinal),
            "C# Similar text service should call the helper, normalize word rectangles into PDF centers, and support nearest repeated text for auto Beam/Openings");
        AssertTrue(
            mainWindow.Contains("TryFindSimilarCountText", StringComparison.Ordinal) &&
            mainWindow.Contains("TextSimilarMatch", StringComparison.Ordinal) &&
            mainWindow.Contains("FindTextCandidateRasterMatches", StringComparison.Ordinal) &&
            mainWindow.Contains("FindMatchesNearCenters", StringComparison.Ordinal) &&
            mainWindow.Contains("ShouldUseTextGuidedRasterMatchesForExactText", StringComparison.Ordinal) &&
            mainWindow.Contains("Similar count exact text-raster matches applied", StringComparison.Ordinal) &&
            mainWindow.Contains("textOnlyMatches", StringComparison.Ordinal) &&
            mainWindow.Contains("textAnchorSelectedAsText", StringComparison.Ordinal) &&
            mainWindow.Contains("SimilarTextAnchorLooksSelectedText", StringComparison.Ordinal) &&
            mainWindow.Contains("SimilarTextResultLooksIntentionalSelection", StringComparison.Ordinal) &&
            mainWindow.Contains("SimilarCountNearbyTextCandidateSearchRadiusMultiplier", StringComparison.Ordinal) &&
            mainWindow.Contains("SimilarCountTextCandidateCentersPdf", StringComparison.Ordinal) &&
            mainWindow.Contains("Similar count nearby text guide added weak offset candidates", StringComparison.Ordinal) &&
            mainWindow.Contains("SimilarCountNearbyTextFallbackPaddingMinPdf = 64f", StringComparison.Ordinal) &&
            mainWindow.Contains("SimilarCountExactTextCandidateSearchRadiusMinPdf = 64f", StringComparison.Ordinal) &&
            mainWindow.Contains("SimilarCountMarkerOffset", StringComparison.Ordinal) &&
            mainWindow.Contains("Similar count text matches applied", StringComparison.Ordinal) &&
            mainWindow.Contains("Similar count text-raster candidates applied", StringComparison.Ordinal) &&
            mainWindow.Contains("SimilarCountTextRasterVisualMergeTimeout", StringComparison.Ordinal) &&
            mainWindow.Contains("SimilarCountMaxTextRasterVisualMergeMatches", StringComparison.Ordinal) &&
            mainWindow.Contains("TryFindTextRasterVisualMergeMatchesAsync", StringComparison.Ordinal) &&
            mainWindow.Contains("Similar count text-raster merged full-sheet visual matches", StringComparison.Ordinal) &&
            mainWindow.Contains("text-raster skipped slow full-sheet visual merge", StringComparison.Ordinal) &&
            mainWindow.Contains("text-raster skipped broad full-sheet visual merge", StringComparison.Ordinal) &&
            mainWindow.Contains("AppendDistinctSimilarMatches", StringComparison.Ordinal) &&
            mainWindow.Contains("raster-checked before Add", StringComparison.Ordinal) &&
            mainWindow.Contains("PDF text exact match", StringComparison.Ordinal) &&
            mainWindow.Contains("!request.AllowExactTextMatches && !request.UseTextCandidateRasterMatches", StringComparison.Ordinal) &&
            mainWindow.Contains("request.PreferNearestRepeatedText", StringComparison.Ordinal) &&
            mainWindow.Contains("nearest repeated text fallback used", StringComparison.Ordinal) &&
            mainWindow.Contains("NearestRepeatedTextFallbackLooksIntentional", StringComparison.Ordinal) &&
            mainWindow.Contains("TryFindSimilarCountNearbyRepeatedTextFallback", StringComparison.Ordinal) &&
            mainWindow.Contains("TextCandidateSearchRadiusPdf", StringComparison.Ordinal) &&
            mainWindow.Contains("SimilarCountWeakTextCandidateScore", StringComparison.Ordinal) &&
            mainWindow.Contains("SimilarCountMaxTextCandidateMatches", StringComparison.Ordinal) &&
            mainWindow.Contains("text scan skipped broad query", StringComparison.Ordinal) &&
            mainWindow.Contains("if (usedTextTemplateFallback)", StringComparison.Ordinal) &&
            mainWindow.Contains("Similar count text-template fallback is review-only", StringComparison.Ordinal) &&
            mainWindow.Contains("showing weak text-guided review candidates", StringComparison.Ordinal) &&
            mainWindow.Contains("added weak unverified text review candidates", StringComparison.Ordinal),
            "Similar Count should use exact PDF text for direct text selections, prefer text-guided raster when it recovers equal or more markers, recover loose manual text boxes with a nearest repeated text fallback, and raster-verify text candidates for Beam/Openings and all-sheets scans");
        AssertTrue(
            helper.Contains("nearby_repeated_text_fallback", StringComparison.Ordinal) &&
            helper.Contains("_similar_text_nearby_mark_key", StringComparison.Ordinal) &&
            helper.Contains("letters >= 2 and digits >= 1", StringComparison.Ordinal) &&
            service.Contains("NearbyRepeatedTextFallback", StringComparison.Ordinal) &&
            mainWindow.Contains("nearbyRepeatedTextFallback: true", StringComparison.Ordinal) &&
            mainWindow.Contains("nearbyRepeatedTextFallback: false", StringComparison.Ordinal),
            "Similar should use a nearby repeated mark-like text label as a raster guide for manual and Beam/Openings flows while keeping the helper limited to mark-like labels, not short grid text");
        AssertTrue(
            viewport.Contains("PdfPath: pdfPath", StringComparison.Ordinal) &&
            viewport.Contains("AllowExactTextMatches = true", StringComparison.Ordinal) &&
            viewport.Contains("UseTextCandidateRasterMatches = false", StringComparison.Ordinal) &&
            viewport.Contains("IncludeTextCandidatesByDefault = false", StringComparison.Ordinal) &&
            viewport.Contains("SKPoint? TemplateAnchorPdf = null", StringComparison.Ordinal) &&
            viewport.Contains("SKPoint? MarkerCenterPdf = null", StringComparison.Ordinal) &&
            viewport.Contains("SKRect? TextSearchRectPdf = null", StringComparison.Ordinal) &&
            viewport.Contains("string DestinationTakeoffFolderPath = \"\"", StringComparison.Ordinal) &&
            viewport.Contains("string DefaultDestinationName = \"\"", StringComparison.Ordinal) &&
            viewport.Contains("bool PreferNearestRepeatedText = false", StringComparison.Ordinal) &&
            viewport.Contains("float TextCandidateSearchRadiusPdf = 0f", StringComparison.Ordinal) &&
            viewport.Contains("bool InitialIncludeRotations = false", StringComparison.Ordinal) &&
            viewport.Contains("bool InitialIncludeMirrored = false", StringComparison.Ordinal) &&
            viewport.Contains("PdfPageIndex: pdfPageIndex", StringComparison.Ordinal) &&
            beamTool.Contains("PageInfo page = _currentPage ?? throw", StringComparison.Ordinal) &&
            beamTool.Contains("PdfPath: page.PdfPath", StringComparison.Ordinal) &&
            beamTool.Contains("PdfPageIndex: page.PdfPage", StringComparison.Ordinal) &&
            beamTool.Contains("AllowExactTextMatches: false", StringComparison.Ordinal) &&
            beamTool.Contains("UseTextCandidateRasterMatches: true", StringComparison.Ordinal) &&
            beamTool.Contains("TemplateAnchorPdf:", StringComparison.Ordinal) &&
            beamTool.Contains("MarkerCenterPdf:", StringComparison.Ordinal) &&
            beamTool.Contains("TextSearchRectPdf:", StringComparison.Ordinal) &&
            beamTool.Contains("DestinationTakeoffFolderPath:", StringComparison.Ordinal) &&
            beamTool.Contains("DefaultDestinationName:", StringComparison.Ordinal) &&
            beamTool.Contains("PreferNearestRepeatedText: true", StringComparison.Ordinal) &&
            beamTool.Contains("IncludeTextCandidatesByDefault: false", StringComparison.Ordinal) &&
            beamTool.Contains("InitialIncludeMirrored: true", StringComparison.Ordinal),
            "Similar Count requests should carry the source PDF identity, while Beam/Openings keep geometry/raster matching, preserve marker offset, and target their named item");
    }

    public static void SimilarCountIsExposedAsContextTool()
    {
        string commandPalette = File.ReadAllText("MainWindow.CommandPalette.cs");
        string beamTool = File.ReadAllText("MainWindow.BeamTool.cs");
        string xaml = File.ReadAllText("MainWindow.xaml");

        AssertTrue(
            commandPalette.Contains("tool.similar", StringComparison.Ordinal) &&
            commandPalette.Contains("BtnSimilarCount_Click(this, new RoutedEventArgs())", StringComparison.Ordinal),
            "Similar Count should be exposed in the command palette as a first-class tool");
        AssertTrue(
            xaml.Contains("name the result", StringComparison.Ordinal) &&
            xaml.Contains("IconSimilar", StringComparison.Ordinal) &&
            commandPalette.Contains("name the result", StringComparison.Ordinal),
            "Similar toolbar and command palette copy should explain that manual Similar names a new result item");
        AssertTrue(
            beamTool.Contains("Use Similar to add reviewed matches to this Beam item", StringComparison.Ordinal) &&
            beamTool.Contains("Use Similar to add reviewed matches to this Opening item", StringComparison.Ordinal),
            "Beam and Openings completion should direct the user to continue that item with Similar");
    }

    private static SimilarSymbolMatchSession? CreateSession(
        SKBitmap page,
        int leftPad = 3,
        int topPad = 3,
        int rightPad = 3,
        int bottomPad = 3)
    {
        (int x, int y) = (TemplateOrigin.X, TemplateOrigin.Y);
        var templateRect = new SKRectI(
            x - leftPad,
            y - topPad,
            x + SymbolWidth + rightPad,
            y + SymbolHeight + bottomPad);
        SimilarSymbolMatchSession? session = SimilarSymbolMatchSession.TryCreate(page, templateRect, out string error);
        AssertTrue(session != null, $"session creation failed: {error}");
        return session;
    }

    private static (int X, int Y) TemplateOrigin;

    private static float DefaultThreshold() => (float)AppSettingsStore.SimilarCountThresholdDefault;

    private static bool NearCenter(SimilarSymbolMatch match, int symbolX, int symbolY)
    {
        int expectedX = symbolX + SymbolWidth / 2;
        int expectedY = symbolY + SymbolHeight / 2;
        return Math.Abs(match.CenterX - expectedX) <= 5 && Math.Abs(match.CenterY - expectedY) <= 5;
    }

    private static SKBitmap BuildPage((int X, int Y)[] placements, (int X, int Y)? rotatedAt, bool withDistractors)
    {
        TemplateOrigin = placements[0];
        var bitmap = new SKBitmap(420, 320, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        using var stroke = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            IsAntialias = false,
        };
        using var fill = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        foreach ((int x, int y) in placements)
            DrawSymbol(canvas, stroke, fill, x, y);

        if (rotatedAt is (int rx, int ry))
        {
            canvas.Save();
            canvas.RotateDegrees(90, rx, ry);
            DrawSymbol(canvas, stroke, fill, rx, ry);
            canvas.Restore();
        }

        if (withDistractors)
        {
            // Same bounding box but solid -> very different ink profile.
            canvas.DrawRect(new SKRect(300, 60, 300 + SymbolWidth, 60 + SymbolHeight), fill);
            // Similar ink amount but different layout.
            canvas.DrawCircle(70, 160, 18, stroke);
            canvas.DrawLine(52, 160, 88, 160, stroke);
        }

        return bitmap;
    }

    private static SKBitmap BuildColoredPage((int X, int Y)[] placements, SKColor color)
    {
        TemplateOrigin = placements[0];
        var bitmap = new SKBitmap(420, 320, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        using var stroke = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            IsAntialias = false,
        };
        using var fill = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        foreach ((int x, int y) in placements)
            DrawSymbol(canvas, stroke, fill, x, y);

        return bitmap;
    }

    private static SKBitmap BuildLargePageWithSimilarCopies((int X, int Y)[] placements)
    {
        TemplateOrigin = placements[0];
        var bitmap = new SKBitmap(7000, 2600, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        using var stroke = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            IsAntialias = false,
        };
        using var fill = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        foreach ((int x, int y) in placements)
            DrawSymbol(canvas, stroke, fill, x, y);

        // Simulate a tight-looking selection that accidentally includes a
        // nearby plan line around the local pair. Far copies should still match
        // the symbol itself instead of requiring the same surrounding context.
        canvas.DrawLine(
            placements[0].X - 86,
            placements[0].Y - 11,
            placements[1].X + SymbolWidth + 20,
            placements[0].Y - 11,
            stroke);

        return bitmap;
    }

    private static SKBitmap BuildLargeTemplatePage(int x, int y, int width, int height)
    {
        var bitmap = new SKBitmap(420, 320, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        using var stroke = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            IsAntialias = false,
        };
        using var fill = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        DrawLargeSymbol(canvas, stroke, fill, x, y, width, height);
        return bitmap;
    }

    private static SKBitmap BuildMirrorSensitivePage((int X, int Y)[] placements, (int X, int Y) mirroredAt)
    {
        TemplateOrigin = placements[0];
        var bitmap = new SKBitmap(420, 320, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        using var stroke = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            IsAntialias = false,
        };
        using var fill = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        foreach ((int x, int y) in placements)
            DrawMirrorSensitiveSymbol(canvas, stroke, fill, x, y);

        canvas.Save();
        canvas.Translate(mirroredAt.X + SymbolWidth, mirroredAt.Y);
        canvas.Scale(-1, 1);
        DrawMirrorSensitiveSymbol(canvas, stroke, fill, 0, 0);
        canvas.Restore();

        return bitmap;
    }

    private static SKBitmap BuildNearMissPage((int X, int Y)[] placements, (int X, int Y) nearMissAt)
    {
        TemplateOrigin = placements[0];
        var bitmap = new SKBitmap(420, 320, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        using var stroke = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            IsAntialias = false,
        };
        using var fill = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        foreach ((int x, int y) in placements)
            DrawSymbol(canvas, stroke, fill, x, y);

        DrawNearMissSymbol(canvas, stroke, fill, nearMissAt.X, nearMissAt.Y);
        return bitmap;
    }

    private static void DrawLooseSelectionEdgeNoise(SKBitmap bitmap, int x, int y)
    {
        using var canvas = new SKCanvas(bitmap);
        using var stroke = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            IsAntialias = false,
        };
        canvas.DrawLine(x - 7, y - 3, x - 7, y + SymbolHeight + 3, stroke);
    }

    private static void DrawAdjacentWordNoise(SKBitmap bitmap, int x, int y)
    {
        using var canvas = new SKCanvas(bitmap);
        using var fill = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        canvas.DrawRect(new SKRect(x - 8, y - 8, x - 5, y + SymbolHeight + 12), fill);
        canvas.DrawRect(new SKRect(x + SymbolWidth + 6, y - 8, x + SymbolWidth + 13, y + SymbolHeight + 12), fill);
    }

    private static void DrawDisconnectedWindowNoise(SKBitmap bitmap, int x, int y)
    {
        using var canvas = new SKCanvas(bitmap);
        using var fill = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        canvas.DrawRect(new SKRect(x + 7, y - 3, x + SymbolWidth - 7, y - 2), fill);
    }

    private static void DrawPeripheralTemplateNoise(SKBitmap bitmap, int left, int top, int width)
    {
        using var canvas = new SKCanvas(bitmap);
        using var fill = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        canvas.DrawRect(new SKRect(left, top, left + width, top + 1), fill);
    }

    // Asymmetric glyph: box + one diagonal + a dot in the top-left corner so
    // rotated copies do not match the upright template by accident.
    private static void DrawSymbol(SKCanvas canvas, SKPaint stroke, SKPaint fill, int x, int y)
    {
        canvas.DrawRect(new SKRect(x, y, x + SymbolWidth, y + SymbolHeight), stroke);
        canvas.DrawLine(x, y + SymbolHeight, x + SymbolWidth, y, stroke);
        canvas.DrawCircle(x + 5, y + 5, 2.5f, fill);
    }

    private static void DrawScaledSymbol(SKCanvas canvas, SKPaint stroke, SKPaint fill, int x, int y, float scale)
    {
        canvas.Save();
        canvas.Translate(x, y);
        canvas.Scale(scale);
        DrawSymbol(canvas, stroke, fill, 0, 0);
        canvas.Restore();
    }

    private static void DrawMirrorSensitiveSymbol(SKCanvas canvas, SKPaint stroke, SKPaint fill, int x, int y)
    {
        canvas.DrawLine(x + 2, y, x + 2, y + SymbolHeight, stroke);
        canvas.DrawLine(x + 2, y + 3, x + SymbolWidth - 8, y + 3, stroke);
        canvas.DrawLine(x + 2, y + SymbolHeight - 4, x + 18, y + SymbolHeight - 4, stroke);
        canvas.DrawLine(x + 14, y + 8, x + SymbolWidth - 5, y + SymbolHeight - 8, stroke);
        canvas.DrawCircle(x + 9, y + 23, 3f, fill);
    }

    private static void DrawNearMissSymbol(SKCanvas canvas, SKPaint stroke, SKPaint fill, int x, int y)
    {
        canvas.DrawRect(new SKRect(x, y, x + SymbolWidth, y + SymbolHeight), stroke);
        canvas.DrawLine(x, y, x + SymbolWidth, y + SymbolHeight, stroke);
        canvas.DrawCircle(x + SymbolWidth - 5, y + SymbolHeight - 5, 2.5f, fill);
    }

    private static void DrawInteriorExtraMark(SKCanvas canvas, SKPaint fill, int x, int y)
    {
        canvas.DrawCircle(x + SymbolWidth - 12, y + SymbolHeight - 8, 3f, fill);
    }

    private static void DrawHeavySurroundingInk(SKCanvas canvas, SKPaint fill, int x, int y)
    {
        canvas.DrawRect(new SKRect(x - 2, y - 3, x + SymbolWidth + 2, y - 1), fill);
        canvas.DrawRect(new SKRect(x - 2, y + SymbolHeight + 1, x + SymbolWidth + 2, y + SymbolHeight + 3), fill);
        canvas.DrawRect(new SKRect(x - 3, y + 2, x - 1, y + SymbolHeight - 2), fill);
        canvas.DrawRect(new SKRect(x + SymbolWidth + 1, y + 2, x + SymbolWidth + 3, y + SymbolHeight - 2), fill);
    }

    private static void DrawHeavyDisconnectedWindowInk(SKCanvas canvas, SKPaint fill, int x, int y)
    {
        canvas.DrawRect(new SKRect(x + 3, y + 5, x + 4, y + SymbolHeight - 5), fill);
        canvas.DrawRect(new SKRect(x + 5, y + 7, x + 6, y + SymbolHeight - 7), fill);
        canvas.DrawRect(new SKRect(x + SymbolWidth - 4, y + 5, x + SymbolWidth - 3, y + SymbolHeight - 5), fill);
        canvas.DrawRect(new SKRect(x + SymbolWidth - 6, y + 7, x + SymbolWidth - 5, y + SymbolHeight - 7), fill);
    }

    private static void DrawLargeSymbol(SKCanvas canvas, SKPaint stroke, SKPaint fill, int x, int y, int width, int height)
    {
        canvas.DrawRect(new SKRect(x, y, x + width, y + height), stroke);
        canvas.DrawLine(x, y + height, x + width, y, stroke);
        canvas.DrawLine(x + width / 4f, y, x + width / 4f, y + height, stroke);
        canvas.DrawCircle(x + 16, y + 16, 5f, fill);
    }

    private static void DrawCoreOnlySymbol(SKCanvas canvas, SKPaint stroke, SKPaint fill, int x, int y)
    {
        canvas.DrawLine(x, y + SymbolHeight, x + SymbolWidth, y, stroke);
        canvas.DrawCircle(x + 5, y + 5, 2.5f, fill);
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
