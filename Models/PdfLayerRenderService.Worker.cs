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
    private enum WorkerRole
    {
        Primary,
        Detail,
        Prefetch,
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
        var result = TryInvokeWorkerAsync<TRequest, TResponse>(action, request, WorkerRole.Primary).GetAwaiter().GetResult();
        response = result.Response;
        error = result.Error;
        return result.Ok;
    }

    private static bool TryInvokeDetailWorker<TRequest, TResponse>(
        string action,
        TRequest request,
        out TResponse? response,
        out string error)
    {
        var result = TryInvokeWorkerAsync<TRequest, TResponse>(action, request, WorkerRole.Detail).GetAwaiter().GetResult();
        response = result.Response;
        error = result.Error;
        return result.Ok;
    }

    private static bool TryInvokePrefetchWorker<TRequest, TResponse>(
        string action,
        TRequest request,
        out TResponse? response,
        out string error)
    {
        var result = TryInvokeWorkerAsync<TRequest, TResponse>(action, request, WorkerRole.Prefetch).GetAwaiter().GetResult();
        response = result.Response;
        error = result.Error;
        return result.Ok;
    }

    private static async Task<(bool Ok, TResponse? Response, string Error)> TryInvokeWorkerAsync<TRequest, TResponse>(
        string action,
        TRequest request,
        WorkerRole role)
    {
        SemaphoreSlim semaphore = WorkerSemaphoreFor(role);
        await semaphore.WaitAsync().ConfigureAwait(false);

        try
        {
            if (!EnsureWorker(role, out string error))
                return (false, default, error);

            string id = Guid.NewGuid().ToString("N");
            var envelope = new WorkerRequest<TRequest>
            {
                Id = id,
                Action = action,
                Request = request,
            };

            StreamWriter input = WorkerInputFor(role)!;
            StreamReader output = WorkerOutputFor(role)!;

            await input
                .WriteLineAsync(JsonSerializer.Serialize(envelope, WorkerJsonOptions))
                .ConfigureAwait(false);
            await input.FlushAsync().ConfigureAwait(false);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            string? line = await output.ReadLineAsync(timeout.Token).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(line))
            {
                ResetWorker(role);
                return (false, default, "PyMuPDF worker stopped unexpectedly.");
            }

            var workerResponse = JsonSerializer.Deserialize<WorkerResponse<TResponse>>(line, WorkerJsonOptions);
            if (workerResponse == null || workerResponse.Id != id)
            {
                ResetWorker(role);
                return (false, default, "PyMuPDF worker returned an invalid response.");
            }

            return (true, workerResponse.Response, "");
        }
        catch (OperationCanceledException ex)
        {
            ResetWorker(role);
            string error = $"PyMuPDF worker {action} timed out.";
            AppLog.Warn(ex, error);
            return (false, default, error);
        }
        catch (Exception ex)
        {
            ResetWorker(role);
            AppLog.Warn(ex, $"PyMuPDF worker {action} failed");
            return (false, default, ex.Message);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static bool EnsureWorker(WorkerRole role, out string error)
    {
        error = "";
        Process? workerProcess = WorkerProcessFor(role);
        StreamWriter? workerInput = WorkerInputFor(role);
        StreamReader? workerOutput = WorkerOutputFor(role);
        if (workerProcess is { HasExited: false } && workerInput != null && workerOutput != null)
            return true;

        ResetWorker(role);

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

        Process? started = Process.Start(psi);
        if (started == null)
        {
            error = "Could not start python.";
            return false;
        }

        started.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                AppLog.Warn($"[{WorkerLogPrefix(role)}] {e.Data}");
        };
        started.BeginErrorReadLine();
        SetWorker(role, started, started.StandardInput, started.StandardOutput);

        return true;
    }

    private static void ResetWorker(WorkerRole role = WorkerRole.Primary)
    {
        Process? process = WorkerProcessFor(role);
        StreamWriter? input = WorkerInputFor(role);
        StreamReader? output = WorkerOutputFor(role);

        try { input?.Dispose(); } catch { }
        try { output?.Dispose(); } catch { }
        try
        {
            if (process is { HasExited: false })
                process.Kill(entireProcessTree: true);
        }
        catch { }
        try { process?.Dispose(); } catch { }

        SetWorker(role, null, null, null);
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

        DetailWorkerSemaphore.Wait();
        try
        {
            ResetWorker(WorkerRole.Detail);
        }
        finally
        {
            DetailWorkerSemaphore.Release();
        }

        PrefetchWorkerSemaphore.Wait();
        try
        {
            ResetWorker(WorkerRole.Prefetch);
        }
        finally
        {
            PrefetchWorkerSemaphore.Release();
        }
    }

    public static void CancelDetailRenderWorker()
    {
        ResetWorker(WorkerRole.Detail);
    }

    /// <summary>
    /// Spawns the persistent Python workers ahead of the first render so the
    /// interpreter/import startup cost (300-1100ms) overlaps app startup
    /// instead of delaying the first interactive detail tile.
    /// </summary>
    public static Task PrewarmWorkersAsync() =>
        Task.Run(() =>
        {
            foreach (WorkerRole role in new[] { WorkerRole.Primary, WorkerRole.Detail, WorkerRole.Prefetch })
            {
                SemaphoreSlim semaphore = WorkerSemaphoreFor(role);
                semaphore.Wait();
                try
                {
                    if (!EnsureWorker(role, out string error) && !string.IsNullOrWhiteSpace(error))
                        AppLog.Warn($"PyMuPDF {WorkerLogPrefix(role)} prewarm failed: {error}");
                }
                catch (Exception ex)
                {
                    AppLog.Warn(ex, $"PyMuPDF {WorkerLogPrefix(role)} prewarm failed");
                }
                finally
                {
                    semaphore.Release();
                }
            }

            AppLog.Info("PyMuPDF workers prewarmed.");
        });

    private static SemaphoreSlim WorkerSemaphoreFor(WorkerRole role) => role switch
    {
        WorkerRole.Detail => DetailWorkerSemaphore,
        WorkerRole.Prefetch => PrefetchWorkerSemaphore,
        _ => WorkerSemaphore,
    };

    private static Process? WorkerProcessFor(WorkerRole role) => role switch
    {
        WorkerRole.Detail => DetailWorkerProcess,
        WorkerRole.Prefetch => PrefetchWorkerProcess,
        _ => WorkerProcess,
    };

    private static StreamWriter? WorkerInputFor(WorkerRole role) => role switch
    {
        WorkerRole.Detail => DetailWorkerInput,
        WorkerRole.Prefetch => PrefetchWorkerInput,
        _ => WorkerInput,
    };

    private static StreamReader? WorkerOutputFor(WorkerRole role) => role switch
    {
        WorkerRole.Detail => DetailWorkerOutput,
        WorkerRole.Prefetch => PrefetchWorkerOutput,
        _ => WorkerOutput,
    };

    private static string WorkerLogPrefix(WorkerRole role) => role switch
    {
        WorkerRole.Detail => "pyhelper-detail",
        WorkerRole.Prefetch => "pyhelper-prefetch",
        _ => "pyhelper",
    };

    private static void SetWorker(WorkerRole role, Process? process, StreamWriter? input, StreamReader? output)
    {
        switch (role)
        {
            case WorkerRole.Detail:
                DetailWorkerProcess = process;
                DetailWorkerInput = input;
                DetailWorkerOutput = output;
                break;
            case WorkerRole.Prefetch:
                PrefetchWorkerProcess = process;
                PrefetchWorkerInput = input;
                PrefetchWorkerOutput = output;
                break;
            default:
                WorkerProcess = process;
                WorkerInput = input;
                WorkerOutput = output;
                break;
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
}
