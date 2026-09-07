using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace OurPlanCore;

internal static class PageImageBitmapDecoder
{
    public static SKBitmap? Decode(string imagePath) =>
        SKBitmap.Decode(imagePath) ?? DecodeWithWpf(imagePath);

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
