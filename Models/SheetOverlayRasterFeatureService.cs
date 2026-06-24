using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace OurPlaneCore;

public static class SheetOverlayRasterFeatureService
{
    private const int MaxFeaturePixels = 1_800_000;
    private const float MinimumLineLengthPt = 18f;
    private const int MaxSegments = 700;
    private const int MaxPoints = 1_200;
    private const int AllowedGapPixels = 1;
    private const float MinimumRunFillRatio = 0.62f;

    public static bool TryExtractSnap(
        SKBitmap source,
        float widthPt,
        float heightPt,
        out PdfGeometrySnapResult snap,
        out string error)
    {
        snap = new PdfGeometrySnapResult();
        error = "";

        if (source.Width <= 0 || source.Height <= 0 || widthPt <= 0 || heightPt <= 0)
        {
            error = "raster page size is invalid.";
            return false;
        }

        using SKBitmap? resized = ResizeForFeatureExtraction(source);
        SKBitmap bitmap = resized ?? source;
        byte[] ink = BuildInkMap(bitmap);
        float ptPerX = widthPt / bitmap.Width;
        float ptPerY = heightPt / bitmap.Height;
        int minHorizontalPixels = Math.Max(6, (int)MathF.Ceiling(MinimumLineLengthPt / Math.Max(ptPerX, 0.001f)));
        int minVerticalPixels = Math.Max(6, (int)MathF.Ceiling(MinimumLineLengthPt / Math.Max(ptPerY, 0.001f)));

        List<PdfGeometrySnapSegment> segments = [];
        ExtractHorizontalSegments(bitmap.Width, bitmap.Height, ink, ptPerX, ptPerY, minHorizontalPixels, segments);
        ExtractVerticalSegments(bitmap.Width, bitmap.Height, ink, ptPerX, ptPerY, minVerticalPixels, segments);

        segments = segments
            .Where(segment => SegmentLength(segment) >= MinimumLineLengthPt)
            .GroupBy(SegmentKey)
            .Select(group => group.OrderByDescending(SegmentLength).First())
            .OrderByDescending(SegmentLength)
            .Take(MaxSegments)
            .ToList();

        if (segments.Count < 8)
        {
            error = "not enough long raster linework was found.";
            return false;
        }

        List<PdfGeometrySnapPoint> points = segments
            .SelectMany(segment => new[] { segment.Start, segment.End, SegmentMidpoint(segment) })
            .GroupBy(point => $"{MathF.Round(point.X * 2f) / 2f:0.##}|{MathF.Round(point.Y * 2f) / 2f:0.##}")
            .Select(group => new PdfGeometrySnapPoint(group.First(), "raster-corner"))
            .Take(MaxPoints)
            .ToList();

        snap = new PdfGeometrySnapResult
        {
            Points = points,
            Segments = segments,
        };
        return true;
    }

    private static SKBitmap? ResizeForFeatureExtraction(SKBitmap source)
    {
        long pixels = (long)source.Width * source.Height;
        if (pixels <= MaxFeaturePixels)
            return null;

        double ratio = Math.Sqrt((double)MaxFeaturePixels / pixels);
        int width = Math.Max(1, (int)Math.Round(source.Width * ratio));
        int height = Math.Max(1, (int)Math.Round(source.Height * ratio));
        return source.Resize(
            new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul),
            SKFilterQuality.Medium);
    }

    private static byte[] BuildInkMap(SKBitmap bitmap)
    {
        byte[] ink = new byte[bitmap.Width * bitmap.Height];
        int stride = Math.Max(1, Math.Min(bitmap.Width, bitmap.Height) / 96);
        int darkTotal = 0;
        int sampleCount = 0;

        for (int y = 0; y < bitmap.Height; y += stride)
        {
            for (int x = 0; x < bitmap.Width; x += stride)
            {
                SKColor pixel = bitmap.GetPixel(x, y);
                if (pixel.Alpha < 32)
                    continue;

                darkTotal += Luminance(pixel);
                sampleCount++;
            }
        }

        int average = sampleCount == 0 ? 255 : darkTotal / sampleCount;
        int threshold = Math.Clamp(average - 42, 110, 205);
        for (int y = 0; y < bitmap.Height; y++)
        {
            int row = y * bitmap.Width;
            for (int x = 0; x < bitmap.Width; x++)
            {
                SKColor pixel = bitmap.GetPixel(x, y);
                ink[row + x] = IsInk(pixel, threshold) ? (byte)1 : (byte)0;
            }
        }

        return ink;
    }

    private static bool IsInk(SKColor pixel, int threshold)
    {
        if (pixel.Alpha < 32)
            return false;

        int luma = Luminance(pixel);
        int max = Math.Max(pixel.Red, Math.Max(pixel.Green, pixel.Blue));
        int min = Math.Min(pixel.Red, Math.Min(pixel.Green, pixel.Blue));
        return luma <= threshold || (max - min >= 42 && min <= 190 && luma <= 224);
    }

    private static int Luminance(SKColor pixel) =>
        (int)Math.Round(pixel.Red * 0.299 + pixel.Green * 0.587 + pixel.Blue * 0.114);

    private static void ExtractHorizontalSegments(
        int width,
        int height,
        byte[] ink,
        float ptPerX,
        float ptPerY,
        int minRunPixels,
        List<PdfGeometrySnapSegment> segments)
    {
        for (int y = 0; y < height; y++)
        {
            int x = 0;
            int row = y * width;
            while (x < width)
            {
                while (x < width && ink[row + x] == 0)
                    x++;
                if (x >= width)
                    break;

                int start = x;
                int lastInk = x;
                int inkCount = 0;
                int gap = 0;
                while (x < width && gap <= AllowedGapPixels)
                {
                    if (ink[row + x] != 0)
                    {
                        lastInk = x;
                        inkCount++;
                        gap = 0;
                    }
                    else
                    {
                        gap++;
                    }
                    x++;
                }

                AddHorizontalSegment(start, lastInk, y, inkCount, ptPerX, ptPerY, minRunPixels, segments);
            }
        }
    }

    private static void ExtractVerticalSegments(
        int width,
        int height,
        byte[] ink,
        float ptPerX,
        float ptPerY,
        int minRunPixels,
        List<PdfGeometrySnapSegment> segments)
    {
        for (int x = 0; x < width; x++)
        {
            int y = 0;
            while (y < height)
            {
                while (y < height && ink[y * width + x] == 0)
                    y++;
                if (y >= height)
                    break;

                int start = y;
                int lastInk = y;
                int inkCount = 0;
                int gap = 0;
                while (y < height && gap <= AllowedGapPixels)
                {
                    if (ink[y * width + x] != 0)
                    {
                        lastInk = y;
                        inkCount++;
                        gap = 0;
                    }
                    else
                    {
                        gap++;
                    }
                    y++;
                }

                AddVerticalSegment(x, start, lastInk, inkCount, ptPerX, ptPerY, minRunPixels, segments);
            }
        }
    }

    private static void AddHorizontalSegment(
        int x0,
        int x1,
        int y,
        int inkCount,
        float ptPerX,
        float ptPerY,
        int minRunPixels,
        List<PdfGeometrySnapSegment> segments)
    {
        int span = Math.Max(0, x1 - x0 + 1);
        if (span < minRunPixels || inkCount < span * MinimumRunFillRatio)
            return;

        float yPt = (y + 0.5f) * ptPerY;
        segments.Add(new PdfGeometrySnapSegment(
            new SKPoint((x0 + 0.5f) * ptPerX, yPt),
            new SKPoint((x1 + 0.5f) * ptPerX, yPt),
            "raster-line"));
    }

    private static void AddVerticalSegment(
        int x,
        int y0,
        int y1,
        int inkCount,
        float ptPerX,
        float ptPerY,
        int minRunPixels,
        List<PdfGeometrySnapSegment> segments)
    {
        int span = Math.Max(0, y1 - y0 + 1);
        if (span < minRunPixels || inkCount < span * MinimumRunFillRatio)
            return;

        float xPt = (x + 0.5f) * ptPerX;
        segments.Add(new PdfGeometrySnapSegment(
            new SKPoint(xPt, (y0 + 0.5f) * ptPerY),
            new SKPoint(xPt, (y1 + 0.5f) * ptPerY),
            "raster-line"));
    }

    private static string SegmentKey(PdfGeometrySnapSegment segment)
    {
        bool horizontal = Math.Abs(segment.Start.Y - segment.End.Y) <= Math.Abs(segment.Start.X - segment.End.X);
        return horizontal
            ? $"h|{Quantize(segment.Start.Y, 2f)}|{Quantize(Math.Min(segment.Start.X, segment.End.X), 6f)}|{Quantize(Math.Max(segment.Start.X, segment.End.X), 6f)}"
            : $"v|{Quantize(segment.Start.X, 2f)}|{Quantize(Math.Min(segment.Start.Y, segment.End.Y), 6f)}|{Quantize(Math.Max(segment.Start.Y, segment.End.Y), 6f)}";
    }

    private static int Quantize(float value, float step) =>
        (int)MathF.Round(value / Math.Max(step, 0.001f));

    private static float SegmentLength(PdfGeometrySnapSegment segment)
    {
        float dx = segment.End.X - segment.Start.X;
        float dy = segment.End.Y - segment.Start.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static SKPoint SegmentMidpoint(PdfGeometrySnapSegment segment) =>
        new((segment.Start.X + segment.End.X) * 0.5f, (segment.Start.Y + segment.End.Y) * 0.5f);
}
