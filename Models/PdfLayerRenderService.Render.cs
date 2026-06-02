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
                VisibleLayers = cachedLayers?.Select(LayerDto.FromInfo).ToList(),
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
            AddCachedRender(cacheKey, result);
            if (!hasClip && PdfPreviewRenderCache.IsCleanRenderRequest(pdfPath, pageIndex, renderScale, layerStates, highlightedLayers))
                PdfPreviewRenderCache.TryWriteCleanRender(pdfPath, pageIndex, (float)renderScale, result);
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
                VisibleLayers = cachedLayers?.Select(LayerDto.FromInfo).ToList(),
                Clip = hasClip ? RectDto.FromSKRect(clipRect!.Value) : null,
            };

            if (!TryInvokePrefetchWorker("render", request, out RenderResponse? response, out error) &&
                !TryRunFileCommand("render", request, inputPath, outputPath, out response, out error))
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
                PdfPreviewRenderCache.TryWriteCleanRender(pdfPath, pageIndex, (float)renderScale, result);
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
        SKRect? clipRect = null)
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

        return sb.ToString();
    }

    private static bool IsUsableClip(SKRect? clipRect) =>
        clipRect.HasValue &&
        clipRect.Value.Width > 0.1f &&
        clipRect.Value.Height > 0.1f;
}
