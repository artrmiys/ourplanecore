using System.Diagnostics;
using OurPlaneCore;
using SkiaSharp;

// Real-engine render benchmark for the "instant sheets" strategy.
// Opt-in: runs only when OPC_BENCH=1 so it never slows the normal test suite.
// Measures the actual production render path (PdfLayerRenderService → PyMuPDF worker)
// to produce concrete before/after numbers for the +30% goal:
//   FULL  = render the WHOLE sheet (current base / raster-sheet path, PNG transport)
//   CLIP  = render only the VISIBLE region (strategy's clip-render path, raw RGBA)
internal static class RenderPerfBenchmark
{
    public static int Run()
    {
        string pdf = FindSamplePdf();
        Console.WriteLine($"=== Render perf benchmark ===");
        Console.WriteLine($"PDF: {pdf}");

        var layerStates = new Dictionary<int, bool>();
        var highlight = Array.Empty<int>();

        // Warm the PyMuPDF worker + document cache so we measure rendering, not process start / doc open.
        var warm = PdfLayerRenderService.TryRenderAsync(pdf, 0, 0.5, layerStates, highlight, null).GetAwaiter().GetResult();
        if (!warm.Ok)
        {
            Console.Error.WriteLine($"Warmup render failed: {warm.Error}");
            return 1;
        }

        float widthPt = warm.Result.WidthPt;
        float heightPt = warm.Result.HeightPt;
        Console.WriteLine($"Page size: {widthPt:0} x {heightPt:0} pt");
        Console.WriteLine();

        // A realistic deep-zoom: the user reads dimensions at ~zoom 3-4x.
        // FULL renders the whole sheet at that DPI; CLIP renders just the viewport-sized window.
        const float renderScale = 3.0f;
        const float viewportClipPt = 640f; // ~ a viewport window in PDF points at this zoom

        // --- FULL SHEET (current path: whole page, PNG transport) ---------------------
        double fullMs = 0;
        long fullBytes = 0;
        long fullPixels = 0;
        int samples = 3;
        for (int i = 0; i < samples; i++)
        {
            // vary scale slightly to defeat the render cache so we time real renders
            float s = renderScale + i * 0.07f;
            var sw = Stopwatch.StartNew();
            var r = PdfLayerRenderService.TryRenderAsync(pdf, 0, s, layerStates, highlight, null).GetAwaiter().GetResult();
            DecodeAndDiscard(r.Result);
            sw.Stop();
            if (!r.Ok) { Console.Error.WriteLine($"Full render failed: {r.Error}"); return 1; }
            fullMs += sw.Elapsed.TotalMilliseconds;
            fullBytes = r.Result.ImageBytes.LongLength + r.Result.RawImageBytes.LongLength;
            fullPixels = (long)(widthPt * s) * (long)(heightPt * s);
        }
        fullMs /= samples;

        // --- VISIBLE CLIP (strategy path: only what is on screen, raw RGBA) -----------
        double clipMs = 0;
        long clipBytes = 0;
        long clipPixels = 0;
        for (int i = 0; i < samples; i++)
        {
            // walk the clip across the page so each is a fresh cache miss
            float left = Math.Clamp(widthPt * 0.2f + i * 40f, 0, Math.Max(0, widthPt - viewportClipPt));
            float top = Math.Clamp(heightPt * 0.35f + i * 40f, 0, Math.Max(0, heightPt - viewportClipPt));
            var clip = new SKRect(left, top, left + viewportClipPt, top + viewportClipPt);
            var sw = Stopwatch.StartNew();
            var r = PdfLayerRenderService.TryRenderAsync(pdf, 0, renderScale, layerStates, highlight, null, clip).GetAwaiter().GetResult();
            DecodeAndDiscard(r.Result);
            sw.Stop();
            if (!r.Ok) { Console.Error.WriteLine($"Clip render failed: {r.Error}"); return 1; }
            clipMs += sw.Elapsed.TotalMilliseconds;
            clipBytes = r.Result.ImageBytes.LongLength + r.Result.RawImageBytes.LongLength;
            clipPixels = (long)(viewportClipPt * renderScale) * (long)(viewportClipPt * renderScale);
        }
        clipMs /= samples;

        // --- CACHE HIT (reopen / re-pan to a place already rendered) -------------------
        var hitClip = new SKRect(widthPt * 0.2f, heightPt * 0.35f, widthPt * 0.2f + viewportClipPt, heightPt * 0.35f + viewportClipPt);
        PdfLayerRenderService.TryRenderAsync(pdf, 0, renderScale, layerStates, highlight, null, hitClip).GetAwaiter().GetResult();
        var hitSw = Stopwatch.StartNew();
        var hit = PdfLayerRenderService.TryRenderAsync(pdf, 0, renderScale, layerStates, highlight, null, hitClip).GetAwaiter().GetResult();
        DecodeAndDiscard(hit.Result);
        hitSw.Stop();

        double improvement = fullMs > 0 ? (fullMs - clipMs) / fullMs * 100.0 : 0;

        Console.WriteLine($"{"Path",-28}{"avg ms",10}{"MPixels",12}{"transport KB",16}");
        Console.WriteLine(new string('-', 66));
        Console.WriteLine($"{"FULL sheet (PNG)",-28}{fullMs,10:0.0}{fullPixels / 1e6,12:0.0}{fullBytes / 1024.0,16:0}");
        Console.WriteLine($"{"VISIBLE clip (raw)",-28}{clipMs,10:0.0}{clipPixels / 1e6,12:0.0}{clipBytes / 1024.0,16:0}");
        Console.WriteLine($"{"clip CACHE HIT",-28}{hitSw.Elapsed.TotalMilliseconds,10:0.0}");
        Console.WriteLine(new string('-', 66));
        Console.WriteLine($"Visible-region render is {improvement:0.0}% faster than full-sheet render.");
        Console.WriteLine($"Goal (>=30% faster): {(improvement >= 30 ? "PASS" : "FAIL")}");
        return improvement >= 30 ? 0 : 2;
    }

    private static void DecodeAndDiscard(PdfLayerRenderResult render)
    {
        // Include decode cost on the C# side, exactly like the viewport does.
        if (render.HasRawImage)
            return; // raw RGBA needs no PNG decode — that is part of the win
        if (render.ImageBytes.Length == 0)
            return;
        using SKBitmap? bmp = SKBitmap.Decode(render.ImageBytes);
        _ = bmp?.Width;
    }

    private static string FindSamplePdf()
    {
        string root = FindRepoRoot();
        string[] candidates =
        [
            Path.Combine(root, "reference", "examples", "63. Mallory View_National --", "pdf", "Bldng#1.pdf"),
            Path.Combine(root, "reference", "examples", "65. Anne's Village_Eric_Tlumber", "pdf.pdf"),
            Path.Combine(root, "reference", "window_detector_poc", "outputs", "wind_window_points_marked.pdf"),
        ];
        foreach (string c in candidates)
            if (File.Exists(c))
                return c;
        throw new FileNotFoundException("No sample PDF found for the render benchmark.");
    }

    private static string FindRepoRoot()
    {
        string dir = Directory.GetCurrentDirectory();
        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (File.Exists(Path.Combine(dir, "ourplanecore.csproj")))
                return dir;
            string? parent = Directory.GetParent(dir)?.FullName;
            if (string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase))
                break;
            dir = parent ?? "";
        }
        throw new DirectoryNotFoundException("Could not locate ourplanecore repo root.");
    }
}
