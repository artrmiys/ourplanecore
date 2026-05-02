using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartTakeoffs.Controls;

namespace SmartTakeoffs;

public sealed class PdfLayerRenderResult
{
    public byte[] ImageBytes { get; init; } = [];
    public float WidthPt { get; init; }
    public float HeightPt { get; init; }
    public IReadOnlyList<PdfLayer> Layers { get; init; } = [];
}

public static class PdfLayerRenderService
{
    private static readonly object WorkerLock = new();
    private static Process? WorkerProcess;
    private static StreamWriter? WorkerInput;
    private static StreamReader? WorkerOutput;
    private static readonly object RenderCacheLock = new();
    private static readonly Dictionary<string, PdfLayerRenderResult> RenderCache = [];
    private static readonly Queue<string> RenderCacheOrder = [];
    private const int MaxRenderCacheEntries = 12;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public static bool TryRender(
        string pdfPath,
        int pageIndex,
        double renderScale,
        IReadOnlyDictionary<int, bool> layerStates,
        IReadOnlyCollection<int> highlightedLayers,
        IReadOnlyList<PdfLayerInfo>? cachedLayers,
        out PdfLayerRenderResult result,
        out string error)
    {
        result = new PdfLayerRenderResult();
        error = "";
        string cacheKey = BuildRenderCacheKey(
            pdfPath,
            pageIndex,
            renderScale,
            layerStates,
            highlightedLayers,
            cachedLayers);
        if (TryGetCachedRender(cacheKey, out result))
            return true;

        string tempDir = Path.Combine(Path.GetTempPath(), "SmartTakeoffs", Guid.NewGuid().ToString("N"));
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
                Layers = layerStates.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value),
                Highlight = highlightedLayers.ToList(),
                VisibleLayers = cachedLayers?.Select(LayerDto.FromInfo).ToList(),
            };

            if (!TryInvokeWorker("render", request, out RenderResponse? response, out error) &&
                !TryRunFileCommand("render", request, inputPath, outputPath, out response, out error))
                return false;

            if (response == null || !response.Ok)
            {
                error = response?.Error ?? "PyMuPDF did not return a render response.";
                return false;
            }

            if (!File.Exists(response.Image))
            {
                error = "PyMuPDF did not produce a rendered image.";
                return false;
            }

            result = new PdfLayerRenderResult
            {
                ImageBytes = File.ReadAllBytes(response.Image),
                WidthPt = response.WidthPt,
                HeightPt = response.HeightPt,
                Layers = response.Layers
                    .Select(l => new PdfLayer(l.Xref, l.Name, l.On, highlightedLayers.Contains(l.Xref)))
                    .ToList(),
            };
            AddCachedRender(cacheKey, result);
            return true;
        }
        catch (Exception ex)
        {
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

            RenderCache[key] = result;
            RenderCacheOrder.Enqueue(key);
            while (RenderCache.Count > MaxRenderCacheEntries && RenderCacheOrder.Count > 0)
            {
                string oldKey = RenderCacheOrder.Dequeue();
                RenderCache.Remove(oldKey);
            }
        }
    }

    private static string BuildRenderCacheKey(
        string pdfPath,
        int pageIndex,
        double renderScale,
        IReadOnlyDictionary<int, bool> layerStates,
        IReadOnlyCollection<int> highlightedLayers,
        IReadOnlyList<PdfLayerInfo>? cachedLayers)
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

        return sb.ToString();
    }

    public static bool TryReadVisibleLayers(
        string pdfPath,
        int pageIndex,
        out IReadOnlyList<PdfLayerInfo> layers,
        out string error)
    {
        layers = [];
        error = "";

        string tempDir = Path.Combine(Path.GetTempPath(), "SmartTakeoffs", Guid.NewGuid().ToString("N"));
        string inputPath = Path.Combine(tempDir, "input.json");
        string outputPath = Path.Combine(tempDir, "output.json");

        try
        {
            Directory.CreateDirectory(tempDir);
            var request = new LayerListRequest
            {
                Pdf = pdfPath,
                Page = pageIndex,
            };

            if (!TryInvokeWorker("layers", request, out LayerListResponse? response, out error) &&
                !TryRunFileCommand("layers", request, inputPath, outputPath, out response, out error))
                return false;

            if (response == null || !response.Ok)
            {
                error = response?.Error ?? "PyMuPDF did not return a layer response.";
                return false;
            }

            layers = response.Layers
                .Select(l => new PdfLayerInfo { Number = l.Xref, Name = l.Name, IsOn = l.On })
                .ToList();
            return true;
        }
        catch (Exception ex)
        {
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

    private static bool TryInvokeWorker<TRequest, TResponse>(
        string action,
        TRequest request,
        out TResponse? response,
        out string error)
    {
        response = default;
        error = "";

        lock (WorkerLock)
        {
            try
            {
                if (!EnsureWorker(out error))
                    return false;

                string id = Guid.NewGuid().ToString("N");
                var envelope = new WorkerRequest<TRequest>
                {
                    Id = id,
                    Action = action,
                    Request = request,
                };

                WorkerInput!.WriteLine(JsonSerializer.Serialize(envelope, JsonOptions));
                WorkerInput.Flush();

                var readTask = WorkerOutput!.ReadLineAsync();
                if (!readTask.Wait(30000))
                {
                    ResetWorker();
                    error = $"PyMuPDF worker {action} timed out.";
                    return false;
                }

                string? line = readTask.Result;
                if (string.IsNullOrWhiteSpace(line))
                {
                    ResetWorker();
                    error = "PyMuPDF worker stopped unexpectedly.";
                    return false;
                }

                var workerResponse = JsonSerializer.Deserialize<WorkerResponse<TResponse>>(line, JsonOptions);
                if (workerResponse == null || workerResponse.Id != id)
                {
                    ResetWorker();
                    error = "PyMuPDF worker returned an invalid response.";
                    return false;
                }

                response = workerResponse.Response;
                return true;
            }
            catch (Exception ex)
            {
                ResetWorker();
                error = ex.Message;
                return false;
            }
        }
    }

    private static bool EnsureWorker(out string error)
    {
        error = "";
        if (WorkerProcess is { HasExited: false } && WorkerInput != null && WorkerOutput != null)
            return true;

        ResetWorker();

        string helperPath = ResolveHelperPath();
        if (helperPath.Length == 0)
        {
            error = "PyMuPDF layer helper was not found.";
            return false;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "python",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-u");
        psi.ArgumentList.Add(helperPath);
        psi.ArgumentList.Add("worker");

        WorkerProcess = Process.Start(psi);
        if (WorkerProcess == null)
        {
            error = "Could not start python.";
            return false;
        }

        WorkerInput = WorkerProcess.StandardInput;
        WorkerOutput = WorkerProcess.StandardOutput;
        return true;
    }

    private static void ResetWorker()
    {
        try { WorkerInput?.Dispose(); } catch { }
        try { WorkerOutput?.Dispose(); } catch { }
        try
        {
            if (WorkerProcess is { HasExited: false })
                WorkerProcess.Kill(entireProcessTree: true);
        }
        catch { }
        try { WorkerProcess?.Dispose(); } catch { }

        WorkerInput = null;
        WorkerOutput = null;
        WorkerProcess = null;
    }

    private static bool TryRunFileCommand<TRequest, TResponse>(
        string action,
        TRequest request,
        string inputPath,
        string outputPath,
        out TResponse? response,
        out string error)
    {
        response = default;
        error = "";

        File.WriteAllText(inputPath, JsonSerializer.Serialize(request, JsonOptions));

        string helperPath = ResolveHelperPath();
        if (helperPath.Length == 0)
        {
            error = "PyMuPDF layer helper was not found.";
            return false;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "python",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(helperPath);
        psi.ArgumentList.Add(action);
        psi.ArgumentList.Add(inputPath);
        psi.ArgumentList.Add(outputPath);

        using var process = Process.Start(psi);
        if (process == null)
        {
            error = "Could not start python.";
            return false;
        }

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(30000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            error = $"PyMuPDF {action} timed out.";
            return false;
        }

        if (!File.Exists(outputPath))
        {
            error = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            return false;
        }

        response = JsonSerializer.Deserialize<TResponse>(File.ReadAllText(outputPath), JsonOptions);
        return true;
    }

    private static string ResolveHelperPath()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "Tools", "pdf_layers_helper.py"),
            Path.Combine(AppContext.BaseDirectory, "pdf_layers_helper.py"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Tools", "pdf_layers_helper.py"),
        ];

        foreach (string candidate in candidates)
        {
            string full = Path.GetFullPath(candidate);
            if (File.Exists(full))
                return full;
        }

        return "";
    }

    private sealed class RenderRequest
    {
        public string Pdf { get; set; } = "";
        public int Page { get; set; }
        public double Scale { get; set; }
        public string Image { get; set; } = "";
        public Dictionary<string, bool> Layers { get; set; } = [];
        public List<int> Highlight { get; set; } = [];
        public List<LayerDto>? VisibleLayers { get; set; }
    }

    private sealed class LayerListRequest
    {
        public string Pdf { get; set; } = "";
        public int Page { get; set; }
    }

    private sealed class RenderResponse
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = "";
        public float WidthPt { get; set; }
        public float HeightPt { get; set; }
        public string Image { get; set; } = "";
        public List<LayerDto> Layers { get; set; } = [];
    }

    private sealed class LayerListResponse
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = "";
        public List<LayerDto> Layers { get; set; } = [];
    }

    private sealed class WorkerRequest<TRequest>
    {
        public string Id { get; set; } = "";
        public string Action { get; set; } = "";
        public TRequest? Request { get; set; }
    }

    private sealed class WorkerResponse<TResponse>
    {
        public string Id { get; set; } = "";
        public TResponse? Response { get; set; }
    }

    private sealed class LayerDto
    {
        public int Xref { get; set; }
        public string Name { get; set; } = "";
        public bool On { get; set; }

        public static LayerDto FromInfo(PdfLayerInfo info) => new()
        {
            Xref = info.Number,
            Name = info.Name,
            On = info.IsOn,
        };
    }
}
