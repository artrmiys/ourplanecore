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
            source.Contains("public const int GridSide = 5", StringComparison.Ordinal) &&
            source.Contains("fine 5x5 ink-profile match", StringComparison.Ordinal) &&
            source.Contains("AutoTightenTemplate(ExtractTemplate", StringComparison.Ordinal),
            "Similar matcher should use a fine symbol layout profile and auto-tighten loose selections");
    }

    public static void ViewportRequiresReadableBitmapBeforeSimilarCount()
    {
        string source = File.ReadAllText(Path.Combine("Controls", "PdfViewport.SimilarCount.cs"));

        AssertTrue(
            source.Contains("SimilarCountMinimumBitmapScale = 0.95f", StringComparison.Ordinal) &&
            source.Contains("TryEnsureSimilarCountBitmapReady", StringComparison.Ordinal) &&
            source.Contains("QueueSimilarCountReadableBitmap()", StringComparison.Ordinal),
            "Similar count should guard against matching from a low-resolution preview bitmap");
        AssertTrue(
            source.Contains("if (!TryEnsureSimilarCountBitmapReady(out string status))", StringComparison.Ordinal),
            "BeginSimilarCountSelection should check bitmap readiness before starting the crop interaction");
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
            mainWindow.Contains("excludedIndexes", StringComparison.Ordinal) &&
            mainWindow.Contains("IncludedCenters()", StringComparison.Ordinal) &&
            mainWindow.Contains("SetSimilarCountPreviewMarkers(BuildPreviewMarkers())", StringComparison.Ordinal),
            "Similar Count should keep include/exclude review state and add only included centers");
        AssertTrue(
            dialog.Contains("public event EventHandler? Accepted", StringComparison.Ordinal) &&
            dialog.Contains("public event EventHandler? Cancelled", StringComparison.Ordinal) &&
            mainWindow.Contains("dialog.Show();", StringComparison.Ordinal) &&
            !mainWindow.Contains("ShowDialog() == true", StringComparison.Ordinal),
            "Similar Count dialog should be modeless so the sheet preview remains clickable during review");
    }

    public static void SimilarCountPreviewShowsConfidence()
    {
        string viewport = File.ReadAllText(Path.Combine("Controls", "PdfViewport.SimilarCount.cs"));
        string mainWindow = File.ReadAllText("MainWindow.SimilarCount.cs");
        string dialog = File.ReadAllText(Path.Combine("Dialogs", "SimilarCountDialog.cs"));

        AssertTrue(
            viewport.Contains("float Score = 1f", StringComparison.Ordinal) &&
            viewport.Contains("marker.Score < (float)AppSettingsStore.SimilarCountThresholdDefault", StringComparison.Ordinal),
            "Similar preview markers should carry and visualize match confidence");
        AssertTrue(
            mainWindow.Contains("lastMatches", StringComparison.Ordinal) &&
            mainWindow.Contains("match.Score", StringComparison.Ordinal) &&
            mainWindow.Contains("SimilarCountScanResult", StringComparison.Ordinal),
            "Similar Count should preserve matcher scores through the review flow");
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
            dialog.Contains("NewCandidateCount", StringComparison.Ordinal) &&
            dialog.Contains("ExcludedNewCount", StringComparison.Ordinal),
            "Similar Count scan result should carry new, weak, already-counted, and excluded candidate counts");
        AssertTrue(
            dialog.Contains("_reviewDetailsLabel", StringComparison.Ordinal) &&
            dialog.Contains("ReviewDetails", StringComparison.Ordinal) &&
            dialog.Contains("Already counted", StringComparison.Ordinal) &&
            dialog.Contains("Excluded", StringComparison.Ordinal),
            "Similar Count dialog should explain the review state in a compact details line");
        AssertTrue(
            mainWindow.Contains("WeakSimilarMatchCount()", StringComparison.Ordinal) &&
            mainWindow.Contains("SimilarReviewStatus", StringComparison.Ordinal) &&
            mainWindow.Contains("result.AlreadyCountedCount", StringComparison.Ordinal),
            "Similar Count status text should report weak and already-counted candidates");
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
            mainWindow.Contains("string destinationName = SimilarCountDestinationName(destinationItem)", StringComparison.Ordinal),
            "Similar Count should capture the active point takeoff before the modeless review starts");
        AssertTrue(
            mainWindow.Contains("AddSimilarCountMeasurements(request, included, destinationItem)", StringComparison.Ordinal) &&
            mainWindow.Contains("ResolveSimilarCountDestinationItem(destinationItem)", StringComparison.Ordinal),
            "Similar Count should add reviewed markers to the captured destination takeoff, not a later active item");
        AssertTrue(
            dialog.Contains("Title = $\"Count Similar: {_destinationName}\"", StringComparison.Ordinal) &&
            dialog.Contains("AddButtonText", StringComparison.Ordinal),
            "Similar Count review should show the destination takeoff in the dialog title and add button");
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

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
