using System;
using System.IO;
using System.Runtime.InteropServices;
using Docnet.Core;
using Docnet.Core.Models;
using SkiaSharp;

namespace OurPlanCore;

internal enum PageImageOperation
{
    RotateLeft,
    RotateRight,
    Rotate180,
    FlipVertical,
    FlipHorizontal,
    Invert,
    Crop,
}

internal sealed record PageImageOperationResult(
    string OutputPath,
    float OriginalWidthPt,
    float OriginalHeightPt,
    float OutputWidthPt,
    float OutputHeightPt);

internal static class PageImageOperationService
{
    private const float RenderScale = 1.5f;
    private const int PageToolPdfEncodingQuality = 72;
    private static readonly IDocLib _docLib = DocLib.Instance;

    public static PageImageOperationResult RenderOperationToPdf(
        PageInfo page,
        PageImageOperation operation,
        string outputPdfPath,
        SKRect? cropRectPt = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPdfPath) ?? ".");
        using RenderedPage rendered = RenderPage(page);
        using SKBitmap bitmap = CreateOperationBitmap(rendered.Bitmap, rendered.WidthPt, rendered.HeightPt, operation, cropRectPt, out float widthPt, out float heightPt);
        WriteBitmapPdf(bitmap, widthPt, heightPt, outputPdfPath);
        return new PageImageOperationResult(outputPdfPath, rendered.WidthPt, rendered.HeightPt, widthPt, heightPt);
    }

    public static PageImageOperationResult RenderPageToPng(PageInfo page, string outputPngPath, SKRect? cropRectPt = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPngPath) ?? ".");
        using RenderedPage rendered = RenderPage(page);
        using SKBitmap bitmap = CreateOperationBitmap(rendered.Bitmap, rendered.WidthPt, rendered.HeightPt, PageImageOperation.Crop, cropRectPt, out float widthPt, out float heightPt);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 95);
        using FileStream stream = File.Create(outputPngPath);
        data.SaveTo(stream);
        return new PageImageOperationResult(outputPngPath, rendered.WidthPt, rendered.HeightPt, widthPt, heightPt);
    }

    public static PageImageOperationResult RenderRotationToPdf(
        PageInfo page,
        double clockwiseDegrees,
        string outputPdfPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPdfPath) ?? ".");
        using RenderedPage rendered = RenderPage(page);
        using SKBitmap bitmap = CreateRotationBitmap(
            rendered.Bitmap,
            rendered.WidthPt,
            rendered.HeightPt,
            clockwiseDegrees,
            out float widthPt,
            out float heightPt);
        WriteBitmapPdf(bitmap, widthPt, heightPt, outputPdfPath);
        return new PageImageOperationResult(outputPdfPath, rendered.WidthPt, rendered.HeightPt, widthPt, heightPt);
    }

    public static SKPoint TransformPoint(SKPoint point, float widthPt, float heightPt, PageImageOperation operation)
    {
        return operation switch
        {
            PageImageOperation.RotateLeft => new SKPoint(point.Y, widthPt - point.X),
            PageImageOperation.RotateRight => new SKPoint(heightPt - point.Y, point.X),
            PageImageOperation.Rotate180 => new SKPoint(widthPt - point.X, heightPt - point.Y),
            PageImageOperation.FlipVertical => new SKPoint(point.X, heightPt - point.Y),
            PageImageOperation.FlipHorizontal => new SKPoint(widthPt - point.X, point.Y),
            _ => point,
        };
    }

    public static SKPoint TransformRotationPoint(SKPoint point, float widthPt, float heightPt, double clockwiseDegrees)
    {
        SKPoint rotated = RotateAroundCenter(point, widthPt, heightPt, clockwiseDegrees);
        SKRect bounds = RotatedBounds(widthPt, heightPt, clockwiseDegrees);
        return new SKPoint(rotated.X - bounds.Left, rotated.Y - bounds.Top);
    }

    private static RenderedPage RenderPage(PageInfo page)
    {
        using var docReader = _docLib.GetDocReader(page.PdfPath, new PageDimensions(RenderScale));
        using var pageReader = docReader.GetPageReader(page.PdfPage);
        int width = pageReader.GetPageWidth();
        int height = pageReader.GetPageHeight();
        byte[] bytes = pageReader.GetImage();
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);
        return new RenderedPage(bitmap, width / RenderScale, height / RenderScale);
    }

    private static SKBitmap CreateOperationBitmap(
        SKBitmap source,
        float pageWidthPt,
        float pageHeightPt,
        PageImageOperation operation,
        SKRect? cropRectPt,
        out float outputWidthPt,
        out float outputHeightPt)
    {
        SKRect crop = ClampCrop(cropRectPt ?? new SKRect(0, 0, pageWidthPt, pageHeightPt), pageWidthPt, pageHeightPt);
        var sourceRect = new SKRect(
            crop.Left * RenderScale,
            crop.Top * RenderScale,
            crop.Right * RenderScale,
            crop.Bottom * RenderScale);
        int sourceWidth = Math.Max(1, (int)Math.Round(sourceRect.Width));
        int sourceHeight = Math.Max(1, (int)Math.Round(sourceRect.Height));
        bool rotateQuarter = operation is PageImageOperation.RotateLeft or PageImageOperation.RotateRight;
        int outputWidth = rotateQuarter ? sourceHeight : sourceWidth;
        int outputHeight = rotateQuarter ? sourceWidth : sourceHeight;
        outputWidthPt = rotateQuarter ? crop.Height : crop.Width;
        outputHeightPt = rotateQuarter ? crop.Width : crop.Height;

        var output = new SKBitmap(new SKImageInfo(outputWidth, outputHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(output);
        canvas.Clear(SKColors.White);

        ApplyCanvasTransform(canvas, operation, outputWidth, outputHeight);
        canvas.DrawBitmap(source, sourceRect, new SKRect(0, 0, sourceWidth, sourceHeight));
        canvas.Flush();

        if (operation == PageImageOperation.Invert)
            InvertBitmap(output);

        return output;
    }

    private static void ApplyCanvasTransform(SKCanvas canvas, PageImageOperation operation, int outputWidth, int outputHeight)
    {
        switch (operation)
        {
            case PageImageOperation.RotateLeft:
                canvas.Translate(0, outputHeight);
                canvas.RotateDegrees(-90);
                break;
            case PageImageOperation.RotateRight:
                canvas.Translate(outputWidth, 0);
                canvas.RotateDegrees(90);
                break;
            case PageImageOperation.Rotate180:
                canvas.Translate(outputWidth, outputHeight);
                canvas.RotateDegrees(180);
                break;
            case PageImageOperation.FlipVertical:
                canvas.Translate(0, outputHeight);
                canvas.Scale(1, -1);
                break;
            case PageImageOperation.FlipHorizontal:
                canvas.Translate(outputWidth, 0);
                canvas.Scale(-1, 1);
                break;
        }
    }

    private static SKBitmap CreateRotationBitmap(
        SKBitmap source,
        float pageWidthPt,
        float pageHeightPt,
        double clockwiseDegrees,
        out float outputWidthPt,
        out float outputHeightPt)
    {
        SKRect bounds = RotatedBounds(pageWidthPt, pageHeightPt, clockwiseDegrees);
        int outputWidth = Math.Max(1, (int)Math.Ceiling(bounds.Width * RenderScale));
        int outputHeight = Math.Max(1, (int)Math.Ceiling(bounds.Height * RenderScale));
        outputWidthPt = outputWidth / RenderScale;
        outputHeightPt = outputHeight / RenderScale;

        var output = new SKBitmap(new SKImageInfo(outputWidth, outputHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(output);
        canvas.Clear(SKColors.White);
        canvas.Translate(-bounds.Left * RenderScale, -bounds.Top * RenderScale);
        canvas.Translate(pageWidthPt * RenderScale / 2f, pageHeightPt * RenderScale / 2f);
        canvas.RotateDegrees((float)clockwiseDegrees);
        canvas.Translate(-pageWidthPt * RenderScale / 2f, -pageHeightPt * RenderScale / 2f);
        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();
        return output;
    }

    private static SKRect RotatedBounds(float widthPt, float heightPt, double clockwiseDegrees)
    {
        SKPoint[] corners =
        [
            TransformRawCorner(0, 0, widthPt, heightPt, clockwiseDegrees),
            TransformRawCorner(widthPt, 0, widthPt, heightPt, clockwiseDegrees),
            TransformRawCorner(widthPt, heightPt, widthPt, heightPt, clockwiseDegrees),
            TransformRawCorner(0, heightPt, widthPt, heightPt, clockwiseDegrees),
        ];
        return new SKRect(
            corners.Min(point => point.X),
            corners.Min(point => point.Y),
            corners.Max(point => point.X),
            corners.Max(point => point.Y));
    }

    private static SKPoint TransformRawCorner(
        float x,
        float y,
        float widthPt,
        float heightPt,
        double clockwiseDegrees) =>
        RotateAroundCenter(new SKPoint(x, y), widthPt, heightPt, clockwiseDegrees);

    private static SKPoint RotateAroundCenter(SKPoint point, float widthPt, float heightPt, double clockwiseDegrees)
    {
        double radians = clockwiseDegrees * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        double cx = widthPt / 2.0;
        double cy = heightPt / 2.0;
        double dx = point.X - cx;
        double dy = point.Y - cy;
        return new SKPoint(
            (float)(cx + dx * cos - dy * sin),
            (float)(cy + dx * sin + dy * cos));
    }

    private static SKRect ClampCrop(SKRect crop, float widthPt, float heightPt)
    {
        float left = Math.Clamp(crop.Left, 0, Math.Max(0, widthPt - 1));
        float top = Math.Clamp(crop.Top, 0, Math.Max(0, heightPt - 1));
        float right = Math.Clamp(crop.Right, left + 1, widthPt);
        float bottom = Math.Clamp(crop.Bottom, top + 1, heightPt);
        return new SKRect(left, top, right, bottom);
    }

    private static void InvertBitmap(SKBitmap bitmap)
    {
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
        {
            SKColor color = bitmap.GetPixel(x, y);
            bitmap.SetPixel(x, y, new SKColor((byte)(255 - color.Red), (byte)(255 - color.Green), (byte)(255 - color.Blue), color.Alpha));
        }
    }

    private static void WriteBitmapPdf(SKBitmap bitmap, float widthPt, float heightPt, string outputPdfPath)
    {
        using FileStream stream = File.Create(outputPdfPath);
        using SKDocument document = SKDocument.CreatePdf(
            stream,
            new SKDocumentPdfMetadata(SKDocument.DefaultRasterDpi * RenderScale, PageToolPdfEncodingQuality));
        SKCanvas canvas = document.BeginPage(widthPt, heightPt);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(bitmap, new SKRect(0, 0, widthPt, heightPt));
        document.EndPage();
        document.Close();
    }

    private sealed class RenderedPage(SKBitmap bitmap, float widthPt, float heightPt) : IDisposable
    {
        public SKBitmap Bitmap { get; } = bitmap;
        public float WidthPt { get; } = widthPt;
        public float HeightPt { get; } = heightPt;

        public void Dispose() => Bitmap.Dispose();
    }
}
