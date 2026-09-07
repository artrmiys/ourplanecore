using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using SkiaSharp;

namespace OurPlanCore;

public static partial class SheetOverlayRenderCache
{
    private static SKBitmap? TryReadRaw(CachePaths paths, CacheMetadata metadata)
    {
        try
        {
            if (!File.Exists(paths.RawPath) ||
                !string.Equals(metadata.RawFormat, RawFormatVersion, StringComparison.Ordinal) ||
                metadata.PixelWidth <= 0 ||
                metadata.PixelHeight <= 0)
            {
                return null;
            }

            long pixelCount = (long)metadata.PixelWidth * metadata.PixelHeight;
            long byteCount = pixelCount * RawBytesPerPixel;
            if (pixelCount <= 0 || pixelCount > MaxPixels || byteCount > int.MaxValue)
                return null;

            var info = new FileInfo(paths.RawPath);
            if (!info.Exists || info.Length != byteCount)
            {
                TryDeleteRaw(paths);
                return null;
            }

            var bitmap = new SKBitmap(new SKImageInfo(
                metadata.PixelWidth,
                metadata.PixelHeight,
                SKColorType.Bgra8888,
                SKAlphaType.Premul));
            if (!TryReadBitmapRawFile(paths.RawPath, bitmap))
            {
                bitmap.Dispose();
                TryDeleteRaw(paths);
                return null;
            }

            return bitmap;
        }
        catch
        {
            TryDeleteRaw(paths);
            return null;
        }
    }

    private static void TryDeleteRaw(CachePaths paths)
    {
        try
        {
            if (File.Exists(paths.RawPath))
                File.Delete(paths.RawPath);
        }
        catch { }
    }

    private static void TryWriteRawSidecar(
        CachePaths paths,
        SKBitmap bitmap,
        CacheMetadata metadata,
        bool rewriteMetadata)
    {
        try
        {
            Directory.CreateDirectory(paths.DirectoryPath);
            string tempRaw = paths.RawPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            if (!TryWriteBitmapRawFile(tempRaw, bitmap, out int pixelWidth, out int pixelHeight))
                return;

            File.Move(tempRaw, paths.RawPath, overwrite: true);

            metadata.PixelWidth = pixelWidth;
            metadata.PixelHeight = pixelHeight;
            metadata.RawFormat = RawFormatVersion;
            if (rewriteMetadata)
                File.WriteAllText(paths.MetadataPath, JsonSerializer.Serialize(metadata, JsonOptions));
        }
        catch
        {
            TryDeleteRaw(paths);
        }
    }

    private static bool TryReadBitmapRawFile(string rawPath, SKBitmap bitmap)
    {
        int pixelWidth = bitmap.Width;
        int pixelHeight = bitmap.Height;
        int tightRowBytes = pixelWidth * RawBytesPerPixel;
        IntPtr pixels = bitmap.GetPixels();
        if (pixels == IntPtr.Zero || tightRowBytes <= 0)
            return false;

        int rowsPerChunk = RowsPerRawChunk(tightRowBytes, pixelHeight);
        byte[] buffer = new byte[tightRowBytes * rowsPerChunk];
        using var stream = new FileStream(rawPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1024 * 1024);
        for (int y = 0; y < pixelHeight; y += rowsPerChunk)
        {
            int rows = Math.Min(rowsPerChunk, pixelHeight - y);
            int byteCount = rows * tightRowBytes;
            stream.ReadExactly(buffer.AsSpan(0, byteCount));
            if (bitmap.RowBytes == tightRowBytes)
            {
                Marshal.Copy(buffer, 0, IntPtr.Add(pixels, y * tightRowBytes), byteCount);
                continue;
            }

            for (int row = 0; row < rows; row++)
            {
                Marshal.Copy(
                    buffer,
                    row * tightRowBytes,
                    IntPtr.Add(pixels, (y + row) * bitmap.RowBytes),
                    tightRowBytes);
            }
        }

        return true;
    }

    private static bool TryWriteBitmapRawFile(
        string rawPath,
        SKBitmap bitmap,
        out int pixelWidth,
        out int pixelHeight)
    {
        pixelWidth = bitmap.Width;
        pixelHeight = bitmap.Height;
        if (pixelWidth <= 0 || pixelHeight <= 0 || !IsBitmapCacheable(bitmap))
            return false;

        long byteCount = (long)pixelWidth * pixelHeight * RawBytesPerPixel;
        if (byteCount <= 0 || byteCount > int.MaxValue)
            return false;

        try
        {
            using SKBitmap? normalized = NeedsRawNormalization(bitmap) ? CopyAsBgraPremul(bitmap) : null;
            SKBitmap source = normalized ?? bitmap;
            IntPtr pixels = source.GetPixels();
            if (pixels == IntPtr.Zero)
                return false;

            int tightRowBytes = pixelWidth * RawBytesPerPixel;
            int rowsPerChunk = RowsPerRawChunk(tightRowBytes, pixelHeight);
            byte[] buffer = new byte[tightRowBytes * rowsPerChunk];
            using var stream = new FileStream(rawPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 1024);
            for (int y = 0; y < pixelHeight; y += rowsPerChunk)
            {
                int rows = Math.Min(rowsPerChunk, pixelHeight - y);
                int byteCountInChunk = rows * tightRowBytes;
                if (source.RowBytes == tightRowBytes)
                {
                    Marshal.Copy(IntPtr.Add(pixels, y * tightRowBytes), buffer, 0, byteCountInChunk);
                }
                else
                {
                    for (int row = 0; row < rows; row++)
                    {
                        Marshal.Copy(
                            IntPtr.Add(pixels, (y + row) * source.RowBytes),
                            buffer,
                            row * tightRowBytes,
                            tightRowBytes);
                    }
                }

                stream.Write(buffer.AsSpan(0, byteCountInChunk));
            }

            return true;
        }
        catch
        {
            try
            {
                if (File.Exists(rawPath))
                    File.Delete(rawPath);
            }
            catch { }
            return false;
        }
    }

    private static int RowsPerRawChunk(int tightRowBytes, int pixelHeight) =>
        Math.Max(1, Math.Min(pixelHeight, (4 * 1024 * 1024) / Math.Max(1, tightRowBytes)));

    private static bool NeedsRawNormalization(SKBitmap bitmap) =>
        bitmap.ColorType != SKColorType.Bgra8888 ||
        bitmap.AlphaType != SKAlphaType.Premul ||
        bitmap.RowBytes < bitmap.Width * RawBytesPerPixel;

    private static SKBitmap CopyAsBgraPremul(SKBitmap bitmap)
    {
        var normalized = new SKBitmap(new SKImageInfo(
            bitmap.Width,
            bitmap.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul));
        using var canvas = new SKCanvas(normalized);
        using var paint = new SKPaint
        {
            BlendMode = SKBlendMode.Src,
            FilterQuality = SKFilterQuality.None,
            IsAntialias = false,
        };
        canvas.DrawBitmap(bitmap, 0, 0, paint);
        canvas.Flush();
        return normalized;
    }
}
