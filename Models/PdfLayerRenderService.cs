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

public sealed class PdfLayerRenderResult
{
    public byte[] ImageBytes { get; init; } = [];
    public float WidthPt { get; init; }
    public float HeightPt { get; init; }
    public IReadOnlyList<PdfLayer> Layers { get; init; } = [];
}

public sealed class PdfLayerTraceResult
{
    public int Layer { get; init; }
    public string LayerName { get; init; } = "";
    public string Mode { get; init; } = "";
    public IReadOnlyList<PdfLayerTraceMeasurement> Measurements { get; init; } = [];
}

public sealed class PdfLayerTraceMeasurement
{
    public string MType { get; init; } = "";
    public string Name { get; init; } = "";
    public string Notes { get; init; } = "";
    public IReadOnlyList<SKPoint> Points { get; init; } = [];
}

public sealed class PdfLayerProbeResult
{
    public IReadOnlyList<PdfLayerProbeCandidate> Candidates { get; init; } = [];
}

public sealed class PdfLayerProbeCandidate
{
    public int Layer { get; init; }
    public string LayerName { get; init; } = "";
    public float Distance { get; init; }
    public SKRect Bounds { get; init; }
}

public static class PdfLayerRenderService
{
    private static readonly SemaphoreSlim WorkerSemaphore = new(1, 1);
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

    public static Task<(bool Ok, PdfLayerRenderResult Result, string Error)> TryRenderAsync(
        string pdfPath,
        int pageIndex,
        double renderScale,
        IReadOnlyDictionary<int, bool> layerStates,
        IReadOnlyCollection<int> highlightedLayers,
        IReadOnlyList<PdfLayerInfo>? cachedLayers) =>
        Task.Run(() =>
        {
            bool ok = TryRender(
                pdfPath,
                pageIndex,
                renderScale,
                layerStates,
                highlightedLayers,
                cachedLayers,
                out PdfLayerRenderResult result,
                out string error);
            return (ok, result, error);
        });

    public static Task<(bool Ok, IReadOnlyList<PdfLayerInfo> Layers, string Error)> TryReadVisibleLayersAsync(
        string pdfPath,
        int pageIndex) =>
        Task.Run(() =>
        {
            bool ok = TryReadVisibleLayers(pdfPath, pageIndex, out IReadOnlyList<PdfLayerInfo> layers, out string error);
            return (ok, layers, error);
        });

    public static Task<(bool Ok, PdfLayerTraceResult Result, string Error)> TryTraceLayerAsync(
        string pdfPath,
        int pageIndex,
        int layerNumber,
        string layerName,
        PdfLayerTraceMode mode,
        SKPoint? pickPoint,
        IReadOnlyList<PdfLayerInfo>? cachedLayers) =>
        Task.Run(() =>
        {
            bool ok = TryTraceLayer(
                pdfPath,
                pageIndex,
                layerNumber,
                layerName,
                mode,
                pickPoint,
                cachedLayers,
                out PdfLayerTraceResult result,
                out string error);
            return (ok, result, error);
        });

    public static Task<(bool Ok, PdfLayerProbeResult Result, string Error)> TryProbeLayersAsync(
        string pdfPath,
        int pageIndex,
        SKPoint point,
        IReadOnlyList<PdfLayerInfo>? cachedLayers) =>
        Task.Run(() =>
        {
            bool ok = TryProbeLayers(pdfPath, pageIndex, point, cachedLayers, out PdfLayerProbeResult result, out string error);
            return (ok, result, error);
        });

    internal static bool TryRender(
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
            if (PdfPreviewRenderCache.IsCleanPreviewRequest(pdfPath, pageIndex, renderScale, layerStates, highlightedLayers))
                PdfPreviewRenderCache.TryWriteCleanPreview(pdfPath, pageIndex, (float)renderScale, result);
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

    internal static bool TryReadVisibleLayers(
        string pdfPath,
        int pageIndex,
        out IReadOnlyList<PdfLayerInfo> layers,
        out string error)
    {
        layers = [];
        error = "";

        string tempDir = Path.Combine(Path.GetTempPath(), "OurPlaneCore", Guid.NewGuid().ToString("N"));
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
            AppLog.Warn(ex, $"TryReadVisibleLayers failed for {pdfPath} page {pageIndex}");
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

    internal static bool TryTraceLayer(
        string pdfPath,
        int pageIndex,
        int layerNumber,
        string layerName,
        PdfLayerTraceMode mode,
        SKPoint? pickPoint,
        IReadOnlyList<PdfLayerInfo>? cachedLayers,
        out PdfLayerTraceResult result,
        out string error)
    {
        result = new PdfLayerTraceResult();
        error = "";

        try
        {
            var request = new LayerTraceRequest
            {
                Pdf = pdfPath,
                Page = pageIndex,
                Layer = layerNumber,
                LayerName = layerName,
                Mode = LayerTraceModeKey(mode),
                PointX = pickPoint?.X,
                PointY = pickPoint?.Y,
                MaxMeasurements = mode == PdfLayerTraceMode.AllEdges ? 48 : 1,
                VisibleLayers = cachedLayers?.Select(LayerDto.FromInfo).ToList(),
            };

            if (!TryInvokeHelper("layertrace", request, out LayerTraceResponse? response, out error))
                return false;

            if (response == null || !response.Ok)
            {
                error = response?.Error ?? "PyMuPDF did not return a layer trace response.";
                return false;
            }

            result = new PdfLayerTraceResult
            {
                Layer = response.Layer,
                LayerName = response.LayerName,
                Mode = response.Mode,
                Measurements = response.Measurements
                    .Select(m => new PdfLayerTraceMeasurement
                    {
                        MType = m.MType,
                        Name = m.Name,
                        Notes = m.Notes,
                        Points = m.Points.Select(p => new SKPoint(p.X, p.Y)).ToList(),
                    })
                    .ToList(),
            };
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"TryTraceLayer failed for {pdfPath} page {pageIndex} layer {layerNumber}");
            error = ex.Message;
            return false;
        }
    }

    internal static bool TryProbeLayers(
        string pdfPath,
        int pageIndex,
        SKPoint point,
        IReadOnlyList<PdfLayerInfo>? cachedLayers,
        out PdfLayerProbeResult result,
        out string error)
    {
        result = new PdfLayerProbeResult();
        error = "";

        try
        {
            var request = new LayerProbeRequest
            {
                Pdf = pdfPath,
                Page = pageIndex,
                PointX = point.X,
                PointY = point.Y,
                Tolerance = 24,
                MaxCandidates = 12,
                VisibleLayers = cachedLayers?.Select(LayerDto.FromInfo).ToList(),
            };

            if (!TryInvokeHelper("layerprobe", request, out LayerProbeResponse? response, out error))
                return false;

            if (response == null || !response.Ok)
            {
                error = response?.Error ?? "PyMuPDF did not return a layer probe response.";
                return false;
            }

            result = new PdfLayerProbeResult
            {
                Candidates = response.Candidates
                    .Where(candidate => !string.IsNullOrWhiteSpace(candidate.LayerName))
                    .Select(candidate => new PdfLayerProbeCandidate
                    {
                        Layer = candidate.Layer,
                        LayerName = candidate.LayerName,
                        Distance = candidate.Distance,
                        Bounds = new SKRect(
                            candidate.Bounds.X0,
                            candidate.Bounds.Y0,
                            candidate.Bounds.X1,
                            candidate.Bounds.Y1),
                    })
                    .ToList(),
            };
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"TryProbeLayers failed for {pdfPath} page {pageIndex}");
            error = ex.Message;
            return false;
        }
    }

    internal static bool TryInvokeHelper<TRequest, TResponse>(
        string action,
        TRequest request,
        out TResponse? response,
        out string error)
    {
        response = default;
        error = "";

        string tempDir = Path.Combine(Path.GetTempPath(), "OurPlaneCore", Guid.NewGuid().ToString("N"));
        string inputPath = Path.Combine(tempDir, "input.json");
        string outputPath = Path.Combine(tempDir, "output.json");

        try
        {
            Directory.CreateDirectory(tempDir);
            return TryInvokeWorker(action, request, out response, out error) ||
                   TryRunFileCommand(action, request, inputPath, outputPath, out response, out error);
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"TryInvokeHelper {action} failed");
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
        var result = TryInvokeWorkerAsync<TRequest, TResponse>(action, request).GetAwaiter().GetResult();
        response = result.Response;
        error = result.Error;
        return result.Ok;
    }

    private static async Task<(bool Ok, TResponse? Response, string Error)> TryInvokeWorkerAsync<TRequest, TResponse>(
        string action,
        TRequest request)
    {
        await WorkerSemaphore.WaitAsync().ConfigureAwait(false);

        try
        {
            if (!EnsureWorker(out string error))
                return (false, default, error);

            string id = Guid.NewGuid().ToString("N");
            var envelope = new WorkerRequest<TRequest>
            {
                Id = id,
                Action = action,
                Request = request,
            };

            await WorkerInput!
                .WriteLineAsync(JsonSerializer.Serialize(envelope, JsonOptions))
                .ConfigureAwait(false);
            await WorkerInput.FlushAsync().ConfigureAwait(false);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            string? line = await WorkerOutput!.ReadLineAsync(timeout.Token).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(line))
            {
                ResetWorker();
                return (false, default, "PyMuPDF worker stopped unexpectedly.");
            }

            var workerResponse = JsonSerializer.Deserialize<WorkerResponse<TResponse>>(line, JsonOptions);
            if (workerResponse == null || workerResponse.Id != id)
            {
                ResetWorker();
                return (false, default, "PyMuPDF worker returned an invalid response.");
            }

            return (true, workerResponse.Response, "");
        }
        catch (OperationCanceledException ex)
        {
            ResetWorker();
            string error = $"PyMuPDF worker {action} timed out.";
            AppLog.Warn(ex, error);
            return (false, default, error);
        }
        catch (Exception ex)
        {
            ResetWorker();
            AppLog.Warn(ex, $"PyMuPDF worker {action} failed");
            return (false, default, ex.Message);
        }
        finally
        {
            WorkerSemaphore.Release();
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

        string pythonExecutable = BundledPythonRuntime.ResolveExecutable();
        var psi = new ProcessStartInfo
        {
            FileName = pythonExecutable,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        BundledPythonRuntime.ConfigureEnvironment(psi, pythonExecutable);
        psi.ArgumentList.Add("-u");
        psi.ArgumentList.Add(helperPath);
        psi.ArgumentList.Add("worker");

        WorkerProcess = Process.Start(psi);
        if (WorkerProcess == null)
        {
            error = "Could not start python.";
            return false;
        }

        WorkerProcess.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                AppLog.Warn($"[pyhelper] {e.Data}");
        };
        WorkerProcess.BeginErrorReadLine();
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

    public static void StopWorker()
    {
        WorkerSemaphore.Wait();
        try
        {
            ResetWorker();
        }
        finally
        {
            WorkerSemaphore.Release();
        }
    }

    private static bool TryRunFileCommand<TRequest, TResponse>(
        string action,
        TRequest request,
        string inputPath,
        string outputPath,
        out TResponse? response,
        out string error)
    {
        var result = TryRunFileCommandAsync<TRequest, TResponse>(action, request, inputPath, outputPath)
            .GetAwaiter()
            .GetResult();
        response = result.Response;
        error = result.Error;
        return result.Ok;
    }

    private static async Task<(bool Ok, TResponse? Response, string Error)> TryRunFileCommandAsync<TRequest, TResponse>(
        string action,
        TRequest request,
        string inputPath,
        string outputPath)
    {
        await File.WriteAllTextAsync(inputPath, JsonSerializer.Serialize(request, JsonOptions)).ConfigureAwait(false);

        string helperPath = ResolveHelperPath();
        if (helperPath.Length == 0)
            return (false, default, "PyMuPDF layer helper was not found.");

        string pythonExecutable = BundledPythonRuntime.ResolveExecutable();
        var psi = new ProcessStartInfo
        {
            FileName = pythonExecutable,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
        };
        BundledPythonRuntime.ConfigureEnvironment(psi, pythonExecutable);
        psi.ArgumentList.Add(helperPath);
        psi.ArgumentList.Add(action);
        psi.ArgumentList.Add(inputPath);
        psi.ArgumentList.Add(outputPath);

        using var process = Process.Start(psi);
        if (process == null)
            return (false, default, "Could not start python.");

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            string timeoutError = $"PyMuPDF {action} timed out.";
            AppLog.Warn(ex, timeoutError);
            return (false, default, timeoutError);
        }

        string stdout = await stdoutTask.ConfigureAwait(false);
        string stderr = await stderrTask.ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(stderr))
            AppLog.Warn($"[pyhelper] {stderr.Trim()}");

        if (!File.Exists(outputPath))
        {
            string error = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            return (false, default, error);
        }

        TResponse? response = JsonSerializer.Deserialize<TResponse>(
            await File.ReadAllTextAsync(outputPath).ConfigureAwait(false),
            JsonOptions);
        return (true, response, "");
    }

    private static string ResolveHelperPath()
    {
        return BundledToolPathResolver.ResolveFile(
            Path.Combine("Tools", "pdf_layers_helper.py"),
            [
                "pdf_layers_helper.py",
                Path.Combine("..", "..", "..", "Tools", "pdf_layers_helper.py"),
            ]);
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

    private sealed class LayerTraceRequest
    {
        public string Pdf { get; set; } = "";
        public int Page { get; set; }
        public int Layer { get; set; }
        public string LayerName { get; set; } = "";
        public string Mode { get; set; } = "";
        public float? PointX { get; set; }
        public float? PointY { get; set; }
        public int MaxMeasurements { get; set; } = 48;
        public List<LayerDto>? VisibleLayers { get; set; }
    }

    private sealed class LayerProbeRequest
    {
        public string Pdf { get; set; } = "";
        public int Page { get; set; }
        public float PointX { get; set; }
        public float PointY { get; set; }
        public float Tolerance { get; set; } = 24;
        public int MaxCandidates { get; set; } = 12;
        public List<LayerDto>? VisibleLayers { get; set; }
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

    private sealed class LayerTraceResponse
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = "";
        public int Layer { get; set; }
        public string LayerName { get; set; } = "";
        public string Mode { get; set; } = "";
        public List<LayerTraceMeasurementDto> Measurements { get; set; } = [];
    }

    private sealed class LayerProbeResponse
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = "";
        public List<LayerProbeCandidateDto> Candidates { get; set; } = [];
    }

    private sealed class LayerTraceMeasurementDto
    {
        public string MType { get; set; } = "";
        public string Name { get; set; } = "";
        public string Notes { get; set; } = "";
        public List<PointDto> Points { get; set; } = [];
    }

    private sealed class PointDto
    {
        public float X { get; set; }
        public float Y { get; set; }
    }

    private sealed class LayerProbeCandidateDto
    {
        public int Layer { get; set; }
        public string LayerName { get; set; } = "";
        public float Distance { get; set; }
        public RectDto Bounds { get; set; } = new();
    }

    private sealed class RectDto
    {
        public float X0 { get; set; }
        public float Y0 { get; set; }
        public float X1 { get; set; }
        public float Y1 { get; set; }
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

    private static string LayerTraceModeKey(PdfLayerTraceMode mode) => mode switch
    {
        PdfLayerTraceMode.Edge => "edge",
        PdfLayerTraceMode.Point => "point",
        PdfLayerTraceMode.AllEdges => "all_edges",
        _ => "full",
    };
}
