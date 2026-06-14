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
