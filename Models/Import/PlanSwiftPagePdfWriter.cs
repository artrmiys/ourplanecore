using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace OurPlaneCore;

internal static class PlanSwiftPagePdfWriter
{
    public static PlanSwiftPageNormalization WriteImagePagePdf(string imagePath, string outputPdfPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPdfPath) ?? ".");
        using SKBitmap? bitmap = DecodeBitmap(imagePath);
        if (bitmap == null)
            throw new InvalidOperationException($"Could not decode PlanSwift page image '{imagePath}'.");

        PlanSwiftPageNormalization normalization = ReadImagePageNormalization(imagePath, bitmap);
        using FileStream stream = File.Create(outputPdfPath);
        using SKDocument document = SKDocument.CreatePdf(stream);
        SKCanvas canvas = document.BeginPage((float)normalization.WidthPt, (float)normalization.HeightPt);
        canvas.Clear(SKColors.White);
        using SKPaint imagePaint = CreateImagePagePdfPaint();
        canvas.DrawBitmap(
            bitmap,
            new SKRect(0, 0, bitmap.Width, bitmap.Height),
            new SKRect(0, 0, (float)normalization.WidthPt, (float)normalization.HeightPt),
            imagePaint);
        document.EndPage();
        document.Close();
        return normalization;
    }

    public static PlanSwiftPageNormalization WritePlaceholderPdf(
        string outputPdfPath,
        string label,
        PlanSwiftPageNormalization? normalization = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPdfPath) ?? ".");
        normalization ??= PlanSwiftPageNormalization.Default();
        using FileStream stream = File.Create(outputPdfPath);
        using SKDocument document = SKDocument.CreatePdf(stream);
        SKCanvas canvas = document.BeginPage((float)normalization.WidthPt, (float)normalization.HeightPt);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint
        {
            Color = SKColors.DarkGray,
            TextSize = Math.Max(12, (float)Math.Min(normalization.WidthPt, normalization.HeightPt) / 36f),
            IsAntialias = true,
        };
        canvas.DrawText(label, 48, 96, paint);
        document.EndPage();
        document.Close();
        return normalization;
    }

    public static bool TryReadImagePageNormalization(
        string imagePath,
        out PlanSwiftPageNormalization normalization)
    {
        normalization = PlanSwiftPageNormalization.Default();
        try
        {
            BitmapFrame frame = BitmapFrame.Create(
                new Uri(imagePath, UriKind.Absolute),
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            frame.Freeze();
            normalization = PlanSwiftGeometryConverter.NormalizePageImage(
                frame.PixelWidth,
                frame.PixelHeight,
                frame.DpiX,
                frame.DpiY);
            return true;
        }
        catch
        {
            using SKBitmap? bitmap = SKBitmap.Decode(imagePath);
            if (bitmap == null)
                return false;

            normalization = ReadImagePageNormalization(imagePath, bitmap);
            return true;
        }
    }

    private static SKBitmap? DecodeBitmap(string imagePath) =>
        SKBitmap.Decode(imagePath) ?? DecodeWithWpf(imagePath);

    private static PlanSwiftPageNormalization ReadImagePageNormalization(string imagePath, SKBitmap bitmap)
    {
        if (TryReadImagePageNormalizationWithWpf(imagePath, out PlanSwiftPageNormalization normalization))
            return normalization;

        return PlanSwiftGeometryConverter.NormalizePageImage(bitmap.Width, bitmap.Height, 72, 72);
    }

    private static SKPaint CreateImagePagePdfPaint() =>
        new()
        {
            IsAntialias = true,
            FilterQuality = SKFilterQuality.High,
        };

    private static bool TryReadImagePageNormalizationWithWpf(
        string imagePath,
        out PlanSwiftPageNormalization normalization)
    {
        normalization = PlanSwiftPageNormalization.Default();
        try
        {
            BitmapFrame frame = BitmapFrame.Create(
                new Uri(imagePath, UriKind.Absolute),
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            frame.Freeze();
            normalization = PlanSwiftGeometryConverter.NormalizePageImage(
                frame.PixelWidth,
                frame.PixelHeight,
                frame.DpiX,
                frame.DpiY);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static SKBitmap? DecodeWithWpf(string imagePath)
    {
        try
        {
            BitmapFrame frame = BitmapFrame.Create(
                new Uri(imagePath, UriKind.Absolute),
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            BitmapSource source = frame.Format == PixelFormats.Bgra32
                ? frame
                : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            source.Freeze();

            int stride = source.PixelWidth * 4;
            byte[] pixels = new byte[stride * source.PixelHeight];
            source.CopyPixels(pixels, stride, 0);

            var bitmap = new SKBitmap(source.PixelWidth, source.PixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            CopyPixels(pixels, stride, bitmap);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static void CopyPixels(byte[] source, int sourceStride, SKBitmap destination)
    {
        IntPtr dest = destination.GetPixels();
        int destStride = destination.RowBytes;
        int rowBytes = Math.Min(sourceStride, destStride);
        for (int y = 0; y < destination.Height; y++)
        {
            Marshal.Copy(
                source,
                y * sourceStride,
                IntPtr.Add(dest, y * destStride),
                rowBytes);
        }
    }
}
