using SkiaSharp;

internal static class RasterTestPdfFactory
{
    private const float PageWidthPoints = 2592;
    private const float PageHeightPoints = 1728;

    public static string Create(string directory, string fileName = "raster-test-sheet.pdf")
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);
        using FileStream stream = File.Create(path);
        using SKDocument document = SKDocument.CreatePdf(stream);
        SKCanvas canvas = document.BeginPage(PageWidthPoints, PageHeightPoints);
        canvas.Clear(SKColors.White);
        using var major = new SKPaint { Color = SKColors.Black, StrokeWidth = 6, IsAntialias = true };
        using var minor = new SKPaint { Color = SKColors.DarkGray, StrokeWidth = 2, IsAntialias = true };
        canvas.DrawRect(90, 90, PageWidthPoints - 180, PageHeightPoints - 180, major);
        canvas.DrawLine(120, 120, PageWidthPoints - 120, PageHeightPoints - 120, major);
        canvas.DrawLine(PageWidthPoints - 120, 120, 120, PageHeightPoints - 120, major);
        for (int x = 240; x < PageWidthPoints - 200; x += 240)
            canvas.DrawLine(x, 140, x, PageHeightPoints - 140, minor);
        for (int y = 240; y < PageHeightPoints - 200; y += 240)
            canvas.DrawLine(140, y, PageWidthPoints - 140, y, minor);
        document.EndPage();
        document.Close();
        return path;
    }
}
