using System;
using System.Collections.Generic;
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

        List<SimilarSymbolMatch> matches = session!.FindMatches(0.6f, includeRotations: false, CancellationToken.None);

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

        List<SimilarSymbolMatch> plain = session!.FindMatches(0.6f, includeRotations: false, CancellationToken.None);
        List<SimilarSymbolMatch> rotated = session.FindMatches(0.6f, includeRotations: true, CancellationToken.None);

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

        List<SimilarSymbolMatch> matches = session!.FindMatches(0.6f, includeRotations: true, CancellationToken.None);

        AssertTrue(matches.Count == 1, $"expected only the template instance, got {matches.Count}");
        AssertTrue(NearCenter(matches[0], 60, 50), "the single match must sit on the template instance");
        AssertTrue(matches[0].Score > 0.9f,
            $"self-match score should be near 1.0, got {matches[0].Score:0.00}");
    }

    private static SimilarSymbolMatchSession? CreateSession(SKBitmap page)
    {
        (int x, int y) = (TemplateOrigin.X, TemplateOrigin.Y);
        var templateRect = new SKRectI(x - 3, y - 3, x + SymbolWidth + 3, y + SymbolHeight + 3);
        SimilarSymbolMatchSession? session = SimilarSymbolMatchSession.TryCreate(page, templateRect, out string error);
        AssertTrue(session != null, $"session creation failed: {error}");
        return session;
    }

    private static (int X, int Y) TemplateOrigin;

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

    // Asymmetric glyph: box + one diagonal + a dot in the top-left corner so
    // rotated copies do not match the upright template by accident.
    private static void DrawSymbol(SKCanvas canvas, SKPaint stroke, SKPaint fill, int x, int y)
    {
        canvas.DrawRect(new SKRect(x, y, x + SymbolWidth, y + SymbolHeight), stroke);
        canvas.DrawLine(x, y + SymbolHeight, x + SymbolWidth, y, stroke);
        canvas.DrawCircle(x + 5, y + 5, 2.5f, fill);
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
