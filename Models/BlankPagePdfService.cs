using System;
using System.IO;
using SkiaSharp;

namespace OurPlaneCore;

internal static class BlankPagePdfService
{
    public const float DefaultWidthPt = 36f * 72f;
    public const float DefaultHeightPt = 24f * 72f;

    public static void WriteBlankPdf(string outputPdfPath, float widthPt = DefaultWidthPt, float heightPt = DefaultHeightPt)
    {
        if (string.IsNullOrWhiteSpace(outputPdfPath))
            throw new ArgumentException("Output PDF path is required.", nameof(outputPdfPath));

        Directory.CreateDirectory(Path.GetDirectoryName(outputPdfPath) ?? ".");
        using FileStream stream = File.Create(outputPdfPath);
        using SKDocument document = SKDocument.CreatePdf(stream);
        SKCanvas canvas = document.BeginPage(
            Math.Max(1f, widthPt),
            Math.Max(1f, heightPt));
        canvas.Clear(SKColors.White);
        document.EndPage();
        document.Close();
    }
}
