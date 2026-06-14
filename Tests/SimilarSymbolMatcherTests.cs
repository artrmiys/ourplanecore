using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using OurPlaneCore;
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
            Math.Abs(looseSession.TemplateInkPixels - normalSession.TemplateInkPixels) <= 8,
            $"loose whitespace should not change template detail; normal ink {normalSession.TemplateInkPixels}, loose ink {looseSession.TemplateInkPixels}");
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

        settings.SimilarCountThreshold = 0.6;
        settings.SimilarCountRotations = true;
        settings.SimilarCountMirrored = false;
        settings.SimilarCountSettingsVersion = 0;
        AppSettingsStore.NormalizeSimilarCountSettings(settings);

        AssertTrue(
            Math.Abs(settings.SimilarCountThreshold - AppSettingsStore.SimilarCountThresholdDefault) < 0.0001,
            "legacy noisy Similar Count threshold should migrate to the precision default");
        AssertTrue(!settings.SimilarCountRotations, "legacy rotation-on default should migrate to opt-in");
        AssertTrue(!settings.SimilarCountMirrored, "mirrored matching should stay opt-in after migration");
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
    }

    public static void SimilarMatcherUsesFineSymbolProfile()
    {
        string source = File.ReadAllText(Path.Combine("Models", "SimilarSymbolMatcher.cs"));

        AssertTrue(
            source.Contains("public const int GridSide = 7", StringComparison.Ordinal) &&
            source.Contains("fine 7x7 ink-profile match", StringComparison.Ordinal) &&
            source.Contains("PrepareTemplate(ExtractTemplate", StringComparison.Ordinal) &&
            source.Contains("RemovePeripheralTemplateNoise", StringComparison.Ordinal) &&
            source.Contains("BuildTemplateQualityWarning", StringComparison.Ordinal) &&
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
            source.Contains("projectionScore", StringComparison.Ordinal),
            "Similar matcher should score row and column projection profiles for sharper silhouette matching");
        AssertTrue(
            source.Contains("FocusedWindowScoreMultiplier", StringComparison.Ordinal) &&
            source.Contains("FocusedWindowMinTemplateCoverage", StringComparison.Ordinal) &&
            source.Contains("FocusedWindowMaxCoreExtraShare", StringComparison.Ordinal) &&
            source.Contains("CoreWindowMasks", StringComparison.Ordinal) &&
            source.Contains("SimilarWindowScore", StringComparison.Ordinal) &&
            source.Contains("TemplateCoverage", StringComparison.Ordinal) &&
            source.Contains("WindowPrecision", StringComparison.Ordinal) &&
            source.Contains("focusedWindowInk", StringComparison.Ordinal) &&
            source.Contains("focusedExtraCoreInk", StringComparison.Ordinal) &&
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
            source.Contains("SimilarCountMinimumBitmapScale = 1.75f", StringComparison.Ordinal) &&
            source.Contains("SimilarCountRequestedBitmapScale = 2.0f", StringComparison.Ordinal) &&
            source.Contains("TryEnsureSimilarCountBitmapReady", StringComparison.Ordinal) &&
            source.Contains("QueueSimilarCountReadableBitmap()", StringComparison.Ordinal),
            "Similar count should guard against matching from a low-resolution preview bitmap and request a high-resolution search raster");
        AssertTrue(
            source.Contains("if (!TryEnsureSimilarCountBitmapReady(out string status))", StringComparison.Ordinal),
            "BeginSimilarCountSelection should check bitmap readiness before starting the crop interaction");
        AssertTrue(
            source.Contains("_similarCountWaitingForReadableBitmap", StringComparison.Ordinal) &&
            source.Contains("StartWaitingForSimilarCountReadableBitmap", StringComparison.Ordinal) &&
            source.Contains("TryStartPendingSimilarCountSelection", StringComparison.Ordinal) &&
            source.Contains("Selection will start automatically", StringComparison.Ordinal) &&
            source.Contains("_similarCountWaitingPageFolder", StringComparison.Ordinal) &&
            viewport.Contains("TryStartPendingSimilarCountSelection();", StringComparison.Ordinal),
            "Similar count should auto-enter selection after sharpening without requiring a second toolbar click");
        AssertTrue(
            source.Contains("ViewportSimilarCountRequest(SKRect PdfRect, string PageFolder, double ScaleMetersPerPt)", StringComparison.Ordinal) &&
            source.Contains("double scaleMetersPerPt = ScaleMetersPerPt;", StringComparison.Ordinal) &&
            source.Contains("new ViewportSimilarCountRequest(rect, pageFolder, scaleMetersPerPt)", StringComparison.Ordinal),
            "Similar count should capture the scanned sheet scale with the selection request");
        AssertTrue(
            source.Contains("HasCurrentSimilarCountBitmap", StringComparison.Ordinal) &&
            source.Contains("IsPageBitmapFor(_pdfPath, _pdfIndex, _pageFolder)", StringComparison.Ordinal) &&
            source.Contains("_pdfIndex != _similarCountWaitingPdfIndex", StringComparison.Ordinal),
            "Similar count should only start and scan against the current page bitmap");
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
            mainWindow.Contains("templateWarning: session.TemplateWarning", StringComparison.Ordinal),
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

        AssertTrue(
            mainWindow.Contains("manualReviewStatesByCenter", StringComparison.Ordinal) &&
            mainWindow.Contains("SimilarReviewKey", StringComparison.Ordinal) &&
            mainWindow.Contains("SimilarCountReviewKeyQuantumPx = 4", StringComparison.Ordinal) &&
            mainWindow.Contains("ApplyManualSimilarReviewChoices", StringComparison.Ordinal),
            "Similar Count review should store manual include/exclude choices by stable quantized match center");
        AssertTrue(
            normalizedMainWindow.Contains(
                "lastMatches.Clear();\n            lastMatches.AddRange(matches);\n            ApplyDefaultSimilarReviewExclusions();",
                StringComparison.Ordinal) &&
            normalizedMainWindow.Contains(
                "ExcludeWeakSimilarMatches();\n            ExcludeAlreadyCountedSimilarMatches();\n            ApplyManualSimilarReviewChoices();",
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

        AssertTrue(
            normalizedMainWindow.Contains(
                "cancellationToken);\n            cancellationToken.ThrowIfCancellationRequested();\n            lastMatches.Clear();",
                StringComparison.Ordinal),
            "Similar Count should check cancellation before a completed stale scan can replace review candidates");
        AssertTrue(
            dialog.Contains("_scanCts?.Cancel();", StringComparison.Ordinal) &&
            dialog.Contains("catch (OperationCanceledException)", StringComparison.Ordinal),
            "Similar Count dialog should cancel superseded scans and ignore stale cancellation results");
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
            viewport.Contains("bool UsedFocusedScore = false", StringComparison.Ordinal) &&
            viewport.Contains("marker.Score < (float)AppSettingsStore.SimilarCountThresholdDefault", StringComparison.Ordinal),
            "Similar preview markers should carry and visualize match confidence and score diagnostics");
        AssertTrue(
            viewport.Contains("SimilarCountPreviewMarkerHitIndex", StringComparison.Ordinal) &&
            viewport.Contains("TryPostSimilarCountPreviewMarkerStatus", StringComparison.Ordinal) &&
            viewport.Contains("SimilarCountPreviewMarkerStatus", StringComparison.Ordinal) &&
            viewport.Contains("click to exclude", StringComparison.Ordinal) &&
            viewport.Contains("already counted", StringComparison.Ordinal) &&
            viewport.Contains("SimilarCountScorePercent", StringComparison.Ordinal) &&
            viewport.Contains("coverage", StringComparison.Ordinal) &&
            viewport.Contains("precision", StringComparison.Ordinal) &&
            viewport.Contains("ink", StringComparison.Ordinal) &&
            viewport.Contains("layout", StringComparison.Ordinal) &&
            viewport.Contains("SimilarCountLimitLabel", StringComparison.Ordinal) &&
            viewport.Contains("limit", StringComparison.Ordinal) &&
            viewport.Contains("extra ink ignored", StringComparison.Ordinal),
            "Similar preview markers should explain hover status, confidence, review state, click action, and match diagnostics");
        AssertTrue(
            mainWindow.Contains("lastMatches", StringComparison.Ordinal) &&
            mainWindow.Contains("match.Score", StringComparison.Ordinal) &&
            mainWindow.Contains("TemplateCoverage: match.TemplateCoverage", StringComparison.Ordinal) &&
            mainWindow.Contains("WindowPrecision: match.WindowPrecision", StringComparison.Ordinal) &&
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
            dialog.Contains("Already counted", StringComparison.Ordinal) &&
            dialog.Contains("Excluded", StringComparison.Ordinal) &&
            dialog.Contains("result.LimitSummary", StringComparison.Ordinal),
            "Similar Count dialog should explain the review state and weak-match limit summary in a compact details line");
        AssertTrue(
            mainWindow.Contains("WeakSimilarMatchCount()", StringComparison.Ordinal) &&
            mainWindow.Contains("SimilarCountLimitSummary()", StringComparison.Ordinal) &&
            mainWindow.Contains("SimilarCountLimitLabel", StringComparison.Ordinal) &&
            mainWindow.Contains("Weak limit:", StringComparison.Ordinal) &&
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
            dialog.Contains("_ = RunScanAsync();", StringComparison.Ordinal),
            "Similar Count threshold presets should update the slider and rescan the current review");
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
            mainWindow.Contains("ExcludeWeakSimilarMatches()", StringComparison.Ordinal) &&
            normalizedMainWindow.Contains("excludedIndexes.Clear();\n            ExcludeWeakSimilarMatches();", StringComparison.Ordinal),
            "Similar Count should make below-default confidence matches review-only after each scan");
        AssertTrue(
            dialog.Contains("IncludeAllRequested", StringComparison.Ordinal) &&
            dialog.Contains("StrongOnlyRequested", StringComparison.Ordinal) &&
            dialog.Contains("Include all", StringComparison.Ordinal) &&
            dialog.Contains("Strong only", StringComparison.Ordinal),
            "Similar Count review should expose quick actions to include candidates or return to strong matches only");
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
            mainWindow.Contains("TakeoffItem? destinationItem = CurrentSimilarCountDestinationItem()", StringComparison.Ordinal) &&
            mainWindow.Contains("string destinationName = SimilarCountDestinationName(destinationItem)", StringComparison.Ordinal) &&
            mainWindow.Contains("OurPlaneCoreJob reviewJob = _currentJob;", StringComparison.Ordinal) &&
            mainWindow.Contains("PageInfo reviewPage = _currentPage;", StringComparison.Ordinal),
            "Similar Count should capture the active point takeoff before the modeless review starts");
        AssertTrue(
            mainWindow.Contains("AddSimilarCountMeasurements(request, included, destinationItem)", StringComparison.Ordinal) &&
            mainWindow.Contains("ResolveSimilarCountDestinationItem(destinationItem)", StringComparison.Ordinal),
            "Similar Count should add reviewed markers to the captured destination takeoff, not a later active item");
        AssertTrue(
            mainWindow.Contains("original job changed; review was not added", StringComparison.Ordinal) &&
            mainWindow.Contains("QueueSimilarCountAiRequest(reviewJob, reviewPage, request, added, destinationName)", StringComparison.Ordinal) &&
            mainWindow.Contains("AI double-check skipped because the scanned sheet is not open", StringComparison.Ordinal),
            "Similar Count review should not write into a changed job or queue AI context for the wrong sheet");
        AssertTrue(
            mainWindow.Contains("ScaleMetersPerPt = request.ScaleMetersPerPt", StringComparison.Ordinal),
            "Similar Count measurements should keep the scanned sheet scale even if the user switches sheets while reviewing");
        AssertTrue(
            dialog.Contains("Title = $\"Count Similar: {_destinationName}\"", StringComparison.Ordinal) &&
            dialog.Contains("AddButtonText", StringComparison.Ordinal),
            "Similar Count review should show the destination takeoff in the dialog title and add button");
    }

    public static void SimilarCountHandlesSwitchedSheetAddStatus()
    {
        string mainWindow = File.ReadAllText("MainWindow.SimilarCount.cs");
        string normalizedMainWindow = mainWindow.Replace("\r\n", "\n", StringComparison.Ordinal);

        AssertTrue(
            mainWindow.Contains("bool scannedSheetIsOpen = IsSamePageFolder(_currentPage?.FolderPath, request.PageFolder)", StringComparison.Ordinal) &&
            normalizedMainWindow.Contains(
                "_viewport.AddGeneratedMeasurements(generated);\n        if (scannedSheetIsOpen)\n            _viewport.SelectMeasurements(generated);",
                StringComparison.Ordinal),
            "Similar Count should only select generated markers when the scanned sheet is still open");
        AssertTrue(
            mainWindow.Contains("SimilarCountAddedStatus", StringComparison.Ordinal) &&
            mainWindow.Contains("Open the scanned sheet to review them", StringComparison.Ordinal) &&
            mainWindow.Contains("They stay selected for review", StringComparison.Ordinal),
            "Similar Count add status should distinguish open-sheet review from switched-sheet saves");
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
            xaml.Contains("active Count/Beam/Openings takeoff", StringComparison.Ordinal),
            "Similar toolbar tooltip should explain the active destination takeoff behavior");
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

    // Asymmetric glyph: box + one diagonal + a dot in the top-left corner so
    // rotated copies do not match the upright template by accident.
    private static void DrawSymbol(SKCanvas canvas, SKPaint stroke, SKPaint fill, int x, int y)
    {
        canvas.DrawRect(new SKRect(x, y, x + SymbolWidth, y + SymbolHeight), stroke);
        canvas.DrawLine(x, y + SymbolHeight, x + SymbolWidth, y, stroke);
        canvas.DrawCircle(x + 5, y + 5, 2.5f, fill);
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
