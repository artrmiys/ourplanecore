using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

    public static partial class PdfLayerRenderService
    {
    internal static bool TryRender(
        string pdfPath,
        int pageIndex,
        double renderScale,
        IReadOnlyDictionary<int, bool> layerStates,
        IReadOnlyCollection<int> highlightedLayers,
        IReadOnlyList<PdfLayerInfo>? cachedLayers,
        out PdfLayerRenderResult result,
        out string error) =>
        TryRender(
            pdfPath,
            pageIndex,
            renderScale,
            layerStates,
            highlightedLayers,
            cachedLayers,
            null,
            out result,
            out error);

    internal static bool TryRender(
        string pdfPath,
        int pageIndex,
        double renderScale,
        IReadOnlyDictionary<int, bool> layerStates,
        IReadOnlyCollection<int> highlightedLayers,
        IReadOnlyList<PdfLayerInfo>? cachedLayers,
        SKRect? clipRect,
        out PdfLayerRenderResult result,
        out string error) =>
        TryRender(
            pdfPath,
            pageIndex,
            renderScale,
            layerStates,
            highlightedLayers,
            cachedLayers,
            clipRect,
            allowRawFullPage: false,
            out result,
            out error);

    internal static bool TryRender(
        string pdfPath,
        int pageIndex,
        double renderScale,
        IReadOnlyDictionary<int, bool> layerStates,
        IReadOnlyCollection<int> highlightedLayers,
        IReadOnlyList<PdfLayerInfo>? cachedLayers,
        SKRect? clipRect,
        bool allowRawFullPage,
        out PdfLayerRenderResult result,
        out string error) =>
        TryRender(
            pdfPath,
            pageIndex,
            renderScale,
            layerStates,
            highlightedLayers,
            cachedLayers,
            clipRect,
            allowRawFullPage,
            preferRawFilePayload: false,
            out result,
            out error);

    internal static bool TryRender(
        string pdfPath,
        int pageIndex,
        double renderScale,
        IReadOnlyDictionary<int, bool> layerStates,
        IReadOnlyCollection<int> highlightedLayers,
        IReadOnlyList<PdfLayerInfo>? cachedLayers,
        SKRect? clipRect,
        bool allowRawFullPage,
        bool preferRawFilePayload,
        out PdfLayerRenderResult result,
        out string error)
    {
        result = new PdfLayerRenderResult();
        error = "";
        bool hasClip = IsUsableClip(clipRect);
        string cacheKey = BuildRenderCacheKey(
            pdfPath,
            pageIndex,
            renderScale,
            layerStates,
            highlightedLayers,
            cachedLayers,
            hasClip ? clipRect : null,
            allowRawFullPage || preferRawFilePayload);
        if (!preferRawFilePayload && TryGetCachedRender(cacheKey, out result))
            return true;

        string tempDir = Path.Combine(Path.GetTempPath(), "OurPlaneCore", Guid.NewGuid().ToString("N"));
        string inputPath = Path.Combine(tempDir, "input.json");
        string outputPath = Path.Combine(tempDir, "output.json");
        string imagePath = Path.Combine(tempDir, preferRawFilePayload ? "page.raw" : "page.png");

        try
        {
            Directory.CreateDirectory(tempDir);
            var request = new RenderRequest
            {
                Pdf = pdfPath,
                Page = pageIndex,
                Scale = renderScale,
                Image = imagePath,
                InlineImage = !preferRawFilePayload,
                InlineImageMaxPixels = InlineRenderImageMaxPixels,
                InlineRawImage = hasClip || allowRawFullPage,
                InlineRawImageMaxPixels = InlineRawRenderImageMaxPixels,
                RawImageFile = preferRawFilePayload,
                Layers = layerStates.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value),
                Highlight = highlightedLayers.ToList(),
                VisibleLayers = PdfLayersEnabled ? cachedLayers?.Select(LayerDto.FromInfo).ToList() : [],
                Clip = hasClip ? RectDto.FromSKRect(clipRect!.Value) : null,
            };

            bool invoked = hasClip
                ? TryInvokeDetailWorker("render", request, out RenderResponse? response, out error) ||
                  TryRunFileCommand("render", request, inputPath, outputPath, out response, out error)
                : TryInvokeWorker("render", request, out response, out error) ||
                  TryRunFileCommand("render", request, inputPath, outputPath, out response, out error);
            if (!invoked)
                return false;

            if (response == null || !response.Ok)
            {
                error = response?.Error ?? "PyMuPDF did not return a render response.";
                return false;
            }

            if (!TryReadRenderImagePayload(
                    response,
                    out byte[] imageBytes,
                    out byte[] rawImageBytes,
                    out int rawImageWidth,
                    out int rawImageHeight,
                    out int rawImageChannels,
                    out error))
                return false;

            result = new PdfLayerRenderResult
            {
                ImageBytes = imageBytes,
                RawImageBytes = rawImageBytes,
                RawImageWidth = rawImageWidth,
                RawImageHeight = rawImageHeight,
                RawImageChannels = rawImageChannels,
                WidthPt = response.WidthPt,
                HeightPt = response.HeightPt,
                ClipRect = response.Clip?.ToSKRect() ?? (hasClip ? clipRect!.Value : null),
                Layers = response.Layers
                    .Select(l => new PdfLayer(l.Xref, l.Name, l.On, highlightedLayers.Contains(l.Xref)))
                    .ToList(),
                LayersCaptured = true,
            };
            // Raw-file payloads are tens of MB and have their own caller-side
            // caches (sheet overlay); keep them out of the shared render cache.
            if (!preferRawFilePayload)
            {
                AddCachedRender(cacheKey, result);
                if (!hasClip && PdfPreviewRenderCache.IsCleanRenderRequest(pdfPath, pageIndex, renderScale, layerStates, highlightedLayers))
                {
                    if (result.ImageBytes.Length > 0)
                        PdfPreviewRenderCache.TryWriteCleanRender(pdfPath, pageIndex, (float)renderScale, result);
                    else if (result.RawImageBytes.Length > 0)
                        QueueCleanRenderPersistFromRaw(pdfPath, pageIndex, renderScale, result);
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"TryRender failed for {pdfPath} page {pageIndex}");
            error = ex.Message;
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch { }
        }
    }

    public static Task<(bool Ok, PdfLayerRenderResult Result, string Error)> TryRenderDedicatedProcessAsync(
        string pdfPath,
        int pageIndex,
        double renderScale,
        IReadOnlyDictionary<int, bool> layerStates,
        IReadOnlyCollection<int> highlightedLayers,
        IReadOnlyList<PdfLayerInfo>? cachedLayers,
        SKRect? clipRect = null) =>
        Task.Run(() =>
        {
            bool ok = TryRenderDedicatedProcess(
                pdfPath,
                pageIndex,
                renderScale,
                layerStates,
                highlightedLayers,
                cachedLayers,
                clipRect,
                usePrefetchWorker: true,
                out PdfLayerRenderResult result,
                out string error);
            return (ok, result, error);
        });

    public static Task<(bool Ok, PdfLayerRenderResult Result, string Error)> TryRenderIsolatedProcessAsync(
        string pdfPath,
        int pageIndex,
        double renderScale,
        IReadOnlyDictionary<int, bool> layerStates,
        IReadOnlyCollection<int> highlightedLayers,
        IReadOnlyList<PdfLayerInfo>? cachedLayers,
        SKRect? clipRect = null) =>
        Task.Run(() =>
        {
            bool ok = TryRenderDedicatedProcess(
                pdfPath,
                pageIndex,
                renderScale,
                layerStates,
                highlightedLayers,
                cachedLayers,
                clipRect,
                usePrefetchWorker: false,
                out PdfLayerRenderResult result,
                out string error);
            return (ok, result, error);
        });

    private static bool TryRenderDedicatedProcess(
        string pdfPath,
        int pageIndex,
        double renderScale,
        IReadOnlyDictionary<int, bool> layerStates,
        IReadOnlyCollection<int> highlightedLayers,
        IReadOnlyList<PdfLayerInfo>? cachedLayers,
        SKRect? clipRect,
        bool usePrefetchWorker,
        out PdfLayerRenderResult result,
        out string error)
    {
        result = new PdfLayerRenderResult();
        error = "";
        bool hasClip = IsUsableClip(clipRect);
        string cacheKey = BuildRenderCacheKey(
            pdfPath,
            pageIndex,
            renderScale,
            layerStates,
            highlightedLayers,
            cachedLayers,
            hasClip ? clipRect : null);
        if (TryGetCachedRender(cacheKey, out result))
            return true;

        string tempDir = Path.Combine(Path.GetTempPath(), "OurPlaneCore", Guid.NewGuid().ToString("N"));
        string inputPath = Path.Combine(tempDir, "input.json");
        string outputPath = Path.Combine(tempDir, "output.json");
        string imagePath = Path.Combine(tempDir, "page.png");

        try
        {
            Directory.CreateDirectory(tempDir);
            var request = new RenderRequest
            {
                Pdf = pdfPath,
                Page = pageIndex,
                Scale = renderScale,
                Image = imagePath,
                InlineImage = true,
                InlineImageMaxPixels = InlineRenderImageMaxPixels,
                InlineRawImage = hasClip,
                InlineRawImageMaxPixels = InlineRawRenderImageMaxPixels,
                Layers = layerStates.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value),
                Highlight = highlightedLayers.ToList(),
                VisibleLayers = PdfLayersEnabled ? cachedLayers?.Select(LayerDto.FromInfo).ToList() : [],
                Clip = hasClip ? RectDto.FromSKRect(clipRect!.Value) : null,
            };

            RenderResponse? response;
            bool invoked = usePrefetchWorker
                ? TryInvokePrefetchWorker("render", request, out response, out error) ||
                  TryRunFileCommand("render", request, inputPath, outputPath, out response, out error)
                : TryRunFileCommand("render", request, inputPath, outputPath, out response, out error);
            if (!invoked)
                return false;
            if (response == null || !response.Ok)
            {
                error = response?.Error ?? "PyMuPDF did not return a render response.";
                return false;
            }
            if (!TryReadRenderImagePayload(
                    response,
                    out byte[] imageBytes,
                    out byte[] rawImageBytes,
                    out int rawImageWidth,
                    out int rawImageHeight,
                    out int rawImageChannels,
                    out error))
                return false;

            result = new PdfLayerRenderResult
            {
                ImageBytes = imageBytes,
                RawImageBytes = rawImageBytes,
                RawImageWidth = rawImageWidth,
                RawImageHeight = rawImageHeight,
                RawImageChannels = rawImageChannels,
                WidthPt = response.WidthPt,
                HeightPt = response.HeightPt,
                ClipRect = response.Clip?.ToSKRect() ?? (hasClip ? clipRect!.Value : null),
                Layers = response.Layers
                    .Select(l => new PdfLayer(l.Xref, l.Name, l.On, highlightedLayers.Contains(l.Xref)))
                    .ToList(),
                LayersCaptured = true,
            };
            AddCachedRender(cacheKey, result);
            if (!hasClip && PdfPreviewRenderCache.IsCleanRenderRequest(pdfPath, pageIndex, renderScale, layerStates, highlightedLayers))
            {
                if (result.ImageBytes.Length > 0)
                    PdfPreviewRenderCache.TryWriteCleanRender(pdfPath, pageIndex, (float)renderScale, result);
                else if (result.RawImageBytes.Length > 0)
                    QueueCleanRenderPersistFromRaw(pdfPath, pageIndex, renderScale, result);
            }
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"TryRenderDedicatedProcess failed for {pdfPath} page {pageIndex}");
            error = ex.Message;
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch { }
        }
    }

    /// <summary>
    /// Raw-pixel render responses skip the PNG encode on the hot path, but the
    /// persisted clean-render cache stores PNG files — encode and write them in
    /// the background so cold opens still get disk-cache hits later.
    /// </summary>
    private static void QueueCleanRenderPersistFromRaw(
        string pdfPath,
        int pageIndex,
        double renderScale,
        PdfLayerRenderResult result)
    {
        _ = Task.Run(() =>
        {
            try
            {
                using SKBitmap? bitmap = CreateBitmapFromRawRender(result);
                if (bitmap == null)
                    return;

                using SKImage image = SKImage.FromBitmap(bitmap);
                using SKData? data = image.Encode(SKEncodedImageFormat.Png, 85);
                if (data == null || data.Size <= 0)
                    return;

                var persisted = new PdfLayerRenderResult
                {
                    ImageBytes = data.ToArray(),
                    WidthPt = result.WidthPt,
                    HeightPt = result.HeightPt,
                    Layers = result.Layers,
                    LayersCaptured = result.LayersCaptured,
                };
                PdfPreviewRenderCache.TryWriteCleanRender(pdfPath, pageIndex, (float)renderScale, persisted);
            }
            catch (Exception ex)
            {
                AppLog.Warn(ex, $"Clean render PNG persist failed for {pdfPath} page {pageIndex + 1}");
            }
        });
    }

    // Rows below this count don't repay Parallel.For's scheduling overhead, so
    // tiny previews/prewarms decode on the calling thread.
    private const int RawRenderParallelMinRows = 256;

    internal static unsafe SKBitmap? CreateBitmapFromRawRender(PdfLayerRenderResult render)
    {
        int width = render.RawImageWidth;
        int height = render.RawImageHeight;
        int channels = render.RawImageChannels;
        byte[] source = render.RawImageBytes;
        long pixelCount = (long)width * height;
        if (pixelCount <= 0 ||
            pixelCount > int.MaxValue / 4 ||
            channels is not (3 or 4) ||
            source.LongLength != pixelCount * channels)
        {
            return null;
        }

        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        IntPtr destPtr = bitmap.GetPixels();
        if (destPtr == IntPtr.Zero)
        {
            bitmap.Dispose();
            return null;
        }

        // Convert BGR(A) -> BGRA straight into the bitmap's pixel buffer: this
        // drops the full-image intermediate byte[] (lower peak RAM + less GC
        // churn) and, for large rasters, spreads the per-row copy across all
        // cores instead of one thread. Output is byte-identical to the old loop.
        byte* dest = (byte*)destPtr;
        int rowChannels = width * channels;
        if (height >= RawRenderParallelMinRows)
        {
            System.Threading.Tasks.Parallel.For(0, height, y =>
                ConvertRawRenderRow(source, dest, y, width, channels, rowChannels));
        }
        else
        {
            for (int y = 0; y < height; y++)
                ConvertRawRenderRow(source, dest, y, width, channels, rowChannels);
        }

        return bitmap;
    }

    private static unsafe void ConvertRawRenderRow(
        byte[] source,
        byte* dest,
        int y,
        int width,
        int channels,
        int rowChannels)
    {
        fixed (byte* srcRow = &source[y * rowChannels])
        {
            byte* s = srcRow;
            byte* d = dest + (long)y * width * 4;
            for (int x = 0; x < width; x++)
            {
                d[0] = s[2];
                d[1] = s[1];
                d[2] = s[0];
                d[3] = channels == 4 ? s[3] : (byte)255;
                s += channels;
                d += 4;
            }
        }
    }

    private static bool TryReadRenderImagePayload(
        RenderResponse response,
        out byte[] imageBytes,
        out byte[] rawImageBytes,
        out int rawImageWidth,
        out int rawImageHeight,
        out int rawImageChannels,
        out string error)
    {
        imageBytes = [];
        rawImageBytes = [];
        rawImageWidth = 0;
        rawImageHeight = 0;
        rawImageChannels = 0;
        error = "";

        if (!string.IsNullOrWhiteSpace(response.ImageRawFile) && File.Exists(response.ImageRawFile))
        {
            rawImageBytes = File.ReadAllBytes(response.ImageRawFile);
            rawImageWidth = response.ImageRawWidth;
            rawImageHeight = response.ImageRawHeight;
            rawImageChannels = response.ImageRawChannels;
            long expectedRawFile = (long)rawImageWidth * rawImageHeight * rawImageChannels;
            if (rawImageWidth > 0 &&
                rawImageHeight > 0 &&
                rawImageChannels is 3 or 4 &&
                rawImageBytes.LongLength == expectedRawFile)
            {
                return true;
            }

            error = "PyMuPDF returned an invalid raw render image file.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(response.ImageRawBase64))
        {
            try
            {
                rawImageBytes = Convert.FromBase64String(response.ImageRawBase64);
                rawImageWidth = response.ImageRawWidth;
                rawImageHeight = response.ImageRawHeight;
                rawImageChannels = response.ImageRawChannels;
                long expected = (long)rawImageWidth * rawImageHeight * rawImageChannels;
                if (rawImageWidth > 0 &&
                    rawImageHeight > 0 &&
                    rawImageChannels is 3 or 4 &&
                    rawImageBytes.LongLength == expected)
                {
                    return true;
                }

                error = "PyMuPDF returned invalid raw render image dimensions.";
                return false;
            }
            catch (FormatException ex)
            {
                AppLog.Warn(ex, "PyMuPDF returned invalid inline raw render image data");
                error = "PyMuPDF returned invalid inline raw render image data.";
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(response.ImageBase64))
        {
            try
            {
                imageBytes = Convert.FromBase64String(response.ImageBase64);
                return imageBytes.Length > 0;
            }
            catch (FormatException ex)
            {
                AppLog.Warn(ex, "PyMuPDF returned invalid inline render image data");
                error = "PyMuPDF returned invalid inline render image data.";
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(response.Image) || !File.Exists(response.Image))
        {
            error = "PyMuPDF did not produce a rendered image.";
            return false;
        }

        imageBytes = File.ReadAllBytes(response.Image);
        return imageBytes.Length > 0;
    }

    private static bool TryGetCachedRender(string key, out PdfLayerRenderResult result)
    {
        lock (RenderCacheLock)
        {
            if (RenderCache.TryGetValue(key, out result!))
                return true;
        }

        result = new PdfLayerRenderResult();
        return false;
    }

    private static void AddCachedRender(string key, PdfLayerRenderResult result)
    {
        lock (RenderCacheLock)
        {
            if (RenderCache.ContainsKey(key))
                return;

            long bytes = result.ImageBytes.LongLength + result.RawImageBytes.LongLength;
            if (bytes <= 0 || bytes > MaxRenderCacheEntryBytes)
                return;

            RenderCache[key] = result;
            RenderCacheOrder.Enqueue(key);
            RenderCacheBytes += bytes;
            while ((RenderCache.Count > MaxRenderCacheEntries || RenderCacheBytes > MaxRenderCacheBytes) &&
                   RenderCacheOrder.Count > 0)
            {
                string oldKey = RenderCacheOrder.Dequeue();
                if (RenderCache.Remove(oldKey, out PdfLayerRenderResult? removed))
                    RenderCacheBytes -= removed.ImageBytes.LongLength + removed.RawImageBytes.LongLength;
            }
        }
    }

    private static string BuildRenderCacheKey(
        string pdfPath,
        int pageIndex,
        double renderScale,
        IReadOnlyDictionary<int, bool> layerStates,
        IReadOnlyCollection<int> highlightedLayers,
        IReadOnlyList<PdfLayerInfo>? cachedLayers,
        SKRect? clipRect = null,
        bool rawTransport = false)
    {
        var info = new FileInfo(pdfPath);
        var sb = new StringBuilder();
        sb.Append(info.FullName.ToLowerInvariant())
          .Append('|').Append(info.Exists ? info.LastWriteTimeUtc.Ticks : 0)
          .Append('|').Append(info.Exists ? info.Length : 0)
          .Append('|').Append(pageIndex)
          .Append('|').Append(Math.Round(renderScale, 3));

        sb.Append("|layers:");
        foreach (var kvp in layerStates.OrderBy(kvp => kvp.Key))
            sb.Append(kvp.Key).Append('=').Append(kvp.Value ? '1' : '0').Append(';');

        sb.Append("|hi:");
        foreach (int layer in highlightedLayers.OrderBy(v => v))
            sb.Append(layer).Append(';');

        sb.Append("|visible:");
        if (cachedLayers != null)
        {
            foreach (var layer in cachedLayers.OrderBy(l => l.Number))
                sb.Append(layer.Number).Append('=').Append(layer.IsOn ? '1' : '0').Append(':').Append(layer.Name).Append(';');
        }

        if (IsUsableClip(clipRect))
        {
            SKRect clip = clipRect!.Value;
            sb.Append("|clip:")
              .Append(Math.Round(clip.Left, 1)).Append(',')
              .Append(Math.Round(clip.Top, 1)).Append(',')
              .Append(Math.Round(clip.Right, 1)).Append(',')
              .Append(Math.Round(clip.Bottom, 1));
        }

        // Raw-transport results have empty ImageBytes; keep them out of the
        // cache slots consumed by PNG-expecting callers (export, raster build).
        if (rawTransport && !IsUsableClip(clipRect))
            sb.Append("|rawfull");

        return sb.ToString();
    }

    private static bool IsUsableClip(SKRect? clipRect) =>
        clipRect.HasValue &&
        clipRect.Value.Width > 0.1f &&
        clipRect.Value.Height > 0.1f;
}
