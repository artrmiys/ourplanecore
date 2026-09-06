using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using OurPlanCore;
using OurPlanCore.Controls;
using SkiaSharp;

internal static class ViewportZoomSamplingTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void MagnifiedRasterDoesNotChangePixelsAfterNavigation() => Sta(() =>
    {
        using var source = new SKBitmap(400, 300);
        using (var canvas = new SKCanvas(source))
        {
            canvas.Clear(SKColors.White);
            using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true, StrokeWidth = 1.2f };
            canvas.DrawLine(50, 50, 330, 215, ink);
            canvas.DrawLine(120, 15, 180, 260, ink);
            ink.Style = SKPaintStyle.Stroke;
            canvas.DrawCircle(205, 145, 57, ink);
        }
        foreach (float zoom in new[] { 2.4f, 4f, 8f })
        {
            var result = Compare(source, 1f, zoom, 150f, 110f, null);
            Check(result.ChangedPixels == 0,
                $"At {zoom * 100}% the same raster changed {result.ChangedPixels} pixels after navigation stopped.");
            Check(result.IntermediatePixels > 100, "magnified diagonal edges need interpolated pixels");
        }
    });

    // Reads an actual project raster and calls the production page paint method.
    // No generated blank page and no independently reimplemented sampling rule.
    public static int RunReal(string raster, string output)
    {
        int result = 2;
        Sta(() =>
        {
            Directory.CreateDirectory(output);
            using var file = File.OpenRead(raster);
            using var source = SKBitmap.Decode(file) ?? throw new InvalidDataException("Cannot decode project raster");
            const float sourceScale = 150f / 72f;
            var evidence = Compare(source, sourceScale, 4f,
                source.Width / sourceScale * 0.53f,
                source.Height / sourceScale * 0.53f, output);
            File.WriteAllText(Path.Combine(output, "zoom-comparison.json"), JsonSerializer.Serialize(new
            {
                Source = raster,
                Width = source.Width,
                Height = source.Height,
                Scale = sourceScale,
                Zoom = 4f,
                evidence.ChangedPixels,
                evidence.IntermediatePixels,
                evidence.NonWhitePixels,
                evidence.TotalPixels,
                Passed = evidence.ChangedPixels == 0 && evidence.NonWhitePixels > 1000,
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"REAL ZOOM: {source.Width}x{source.Height}, changed={evidence.ChangedPixels}/{evidence.TotalPixels}, nonwhite={evidence.NonWhitePixels}");
            result = evidence.ChangedPixels == 0 && evidence.NonWhitePixels > 1000 ? 0 : 1;
        });
        return result;
    }

    private static Comparison Compare(SKBitmap source, float sourceScale, float zoom,
        float centerX, float centerY, string? output)
    {
        bool oldStatic = ViewportRenderPolicy.StaticRasterModeEnabled;
        ViewportRenderPolicy.StaticRasterModeEnabled = true;
        var viewport = new PdfViewport();
        try
        {
            const int width = 1100, height = 700;
            Set(viewport, "_pageBitmap", source);
            Set(viewport, "_bitmapScale", sourceScale);
            Set(viewport, "_usingRasterSheetRender", true);
            Set(viewport, "_pdfW", source.Width / sourceScale);
            Set(viewport, "_pdfH", source.Height / sourceScale);
            Set(viewport, "_zoom", zoom);
            Set(viewport, "_panX", centerX - width / zoom / 2f);
            Set(viewport, "_panY", centerY - height / zoom / 2f);
            Set(viewport, "_canvasWidth", (float)width);
            Set(viewport, "_canvasHeight", (float)height);
            using var moving = Paint(viewport, width, height, true);
            using var settled = Paint(viewport, width, height, false);
            SKColor[] a = moving.Pixels, b = settled.Pixels;
            int changed = 0, intermediate = 0, nonWhite = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) changed++;
                if (b[i].Red > 12 && b[i].Red < 242) intermediate++;
                if (b[i].Red < 242 || b[i].Green < 242 || b[i].Blue < 242) nonWhite++;
            }
            if (output != null)
            {
                Save(moving, Path.Combine(output, "during-navigation.png"));
                Save(settled, Path.Combine(output, "after-navigation.png"));
            }
            return new Comparison(changed, intermediate, nonWhite, a.Length);
        }
        finally
        {
            // Source is owned by the caller, not this temporary viewport.
            Set(viewport, "_pageBitmap", null);
            viewport.ClearPage();
            ViewportRenderPolicy.StaticRasterModeEnabled = oldStatic;
        }
    }

    private static SKBitmap Paint(PdfViewport viewport, int width, int height, bool moving)
    {
        Set(viewport, "_renderNavigationFastFrame", moving);
        Set(viewport, "_isFastNavigating", moving);
        var frame = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(frame);
        canvas.Clear(SKColors.White);
        float zoom = Get<float>(viewport, "_zoom");
        float x = Get<float>(viewport, "_panX"), y = Get<float>(viewport, "_panY");
        typeof(PdfViewport).GetMethod("DrawPageBitmapAndStaticOverlays", Private)!.Invoke(viewport,
            [canvas, new SKRect(x, y, x + width / zoom, y + height / zoom)]);
        canvas.Flush();
        return frame;
    }

    private static void Save(SKBitmap bitmap, string path)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(path);
        data.SaveTo(file);
    }

    private static void Set(PdfViewport viewport, string name, object? value) =>
        typeof(PdfViewport).GetField(name, Private)!.SetValue(viewport, value);
    private static T Get<T>(PdfViewport viewport, string name) =>
        (T)typeof(PdfViewport).GetField(name, Private)!.GetValue(viewport)!;
    private static void Check(bool condition, string error)
    { if (!condition) throw new InvalidOperationException(error); }
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromMinutes(1))) throw new TimeoutException("Raster sampling regression timed out");
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    private sealed record Comparison(int ChangedPixels, int IntermediatePixels, int NonWhitePixels, int TotalPixels);
}
