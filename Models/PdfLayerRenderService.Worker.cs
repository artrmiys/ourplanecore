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
using OurPlanCore.Controls;
using SkiaSharp;

namespace OurPlanCore;

public static partial class PdfLayerRenderService
{
    private enum WorkerRole
    {
        Primary,
        Detail,
        Prefetch,
    }

    // The viewport's "render profile" elapsed mixes semaphore queue wait and the
    // python roundtrip into one number, which made multi-second cold previews
    // unattributable. Log the split whenever a worker call is slow enough to
    // matter so queue starvation and slow renders stay distinguishable.
    private const int SlowWorkerProfileLogMs = 150;

    private static void ReportWorkerProfile(string worker, string action, long queueMs, long execMs)
    {
        if (queueMs + execMs >= SlowWorkerProfileLogMs)
            AppLog.Info($"PyMuPDF worker profile; worker={worker}; action={action}; queue={queueMs}ms; exec={execMs}ms");
    }

    internal static bool TryInvokeHelper<TRequest, TResponse>(
        string action,
        TRequest request,
        out TResponse? response,
        out string error)
    {
        response = default;
        error = "";

        string tempDir = Path.Combine(Path.GetTempPath(), "OurPlanCore", Guid.NewGuid().ToString("N"));
        string inputPath = Path.Combine(tempDir, "input.json");
        string outputPath = Path.Combine(tempDir, "output.json");

        try
        {
            Directory.CreateDirectory(tempDir);
            if (TryInvokeWorker(action, request, out response, out error))
                return true;

            string workerError = error;
            if (string.Equals(action, "sheetmeta_batch", StringComparison.Ordinal))
            {
                AppLog.Warn(
                    $"PyMuPDF sheetmeta_batch worker failed; slow file-command replay disabled; " +
                    $"worker_error={workerError}");
                error = workerError;
                return false;
            }

            bool fallbackOk = TryRunFileCommand(action, request, inputPath, outputPath, out response, out error);
            return fallbackOk;
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

    // The dedicated detail worker has the warmest PyMuPDF doc/display-list cache,
    // so an idle detail worker is always the first choice. But it is a single
    // process: when the user pans at deep zoom, visible tiles arrive faster than
    // one render, and queueing them serially behind the in-flight tile is what
    // reads as lingering blur. If the detail worker is busy and the prefetch pool
    // has a free slot, overflow the tile to the pool so neighboring visible tiles
    // render in parallel. When the pool is saturated too, fall back to the
    // original serial detail queue rather than competing with prefetch fan-out.
    private static bool TryInvokeDetailWorker<TRequest, TResponse>(
        string action,
        TRequest request,
        out TResponse? response,
        out string error)
    {
        (bool Ok, TResponse? Response, string Error) result;
        if (DetailWorkerSemaphore.Wait(0))
        {
            var execWatch = Stopwatch.StartNew();
            try
            {
                result = InvokeWorkerCoreAsync<TRequest, TResponse>(action, request, WorkerRole.Detail)
                    .GetAwaiter().GetResult();
            }
            finally
            {
                DetailWorkerSemaphore.Release();
            }

            ReportWorkerProfile(WorkerLogPrefix(WorkerRole.Detail), action, 0, execWatch.ElapsedMilliseconds);
        }
        else if (PrefetchPoolSlots.CurrentCount > 0)
        {
            result = TryInvokePrefetchWorkerAsync<TRequest, TResponse>(action, request).GetAwaiter().GetResult();
        }
        else
        {
            result = TryInvokeWorkerAsync<TRequest, TResponse>(action, request, WorkerRole.Detail)
                .GetAwaiter().GetResult();
        }

        response = result.Response;
        error = result.Error;
        return result.Ok;
    }

    // Full-page renders (cold previews, sharp base upgrades) share the primary
    // worker with sheetmeta/layers/snap/import-annotation batches, all FIFO on
    // WorkerSemaphore(1,1). Right after an import that queue can hold seconds of
    // metadata work, which showed up as 3s+ "preview-pymupdf" frames for a tiny
    // 0.2-scale preview. If the primary worker is busy and the prefetch pool has
    // an idle slot, render there instead (a pool slot's colder doc cache is far
    // cheaper than the queue wait); when the pool is saturated too, fall back to
    // the primary queue as before.
    private static bool TryInvokeRenderWorker<TRequest, TResponse>(
        string action,
        TRequest request,
        out TResponse? response,
        out string error)
    {
        (bool Ok, TResponse? Response, string Error) result;
        if (WorkerSemaphore.Wait(0))
        {
            var execWatch = Stopwatch.StartNew();
            try
            {
                result = InvokeWorkerCoreAsync<TRequest, TResponse>(action, request, WorkerRole.Primary)
                    .GetAwaiter().GetResult();
            }
            finally
            {
                WorkerSemaphore.Release();
            }

            ReportWorkerProfile(WorkerLogPrefix(WorkerRole.Primary), action, 0, execWatch.ElapsedMilliseconds);
        }
        else if (PrefetchPoolSlots.CurrentCount > 0)
        {
            result = TryInvokePrefetchWorkerAsync<TRequest, TResponse>(action, request).GetAwaiter().GetResult();
        }
        else
        {
            result = TryInvokeWorkerAsync<TRequest, TResponse>(action, request, WorkerRole.Primary)
                .GetAwaiter().GetResult();
        }

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
        var result = TryInvokePrefetchWorkerAsync<TRequest, TResponse>(action, request).GetAwaiter().GetResult();
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
        var queueWatch = Stopwatch.StartNew();
        await semaphore.WaitAsync().ConfigureAwait(false);
        long queueMs = queueWatch.ElapsedMilliseconds;

        var execWatch = Stopwatch.StartNew();
        try
        {
            return await InvokeWorkerCoreAsync<TRequest, TResponse>(action, request, role).ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
            ReportWorkerProfile(WorkerLogPrefix(role), action, queueMs, execWatch.ElapsedMilliseconds);
        }
    }

    // One request against a single-worker role. The caller must already hold
    // that role's semaphore (TryInvokeWorkerAsync, or the detail overflow path
    // that grabbed it with Wait(0)).
    private static async Task<(bool Ok, TResponse? Response, string Error)> InvokeWorkerCoreAsync<TRequest, TResponse>(
        string action,
        TRequest request,
        WorkerRole role)
    {
        try
        {
            if (!EnsureWorker(role, out string error))
                return (false, default, error);

            var exchange = await ExchangeWithWorkerAsync<TRequest, TResponse>(
                WorkerInputFor(role)!, WorkerOutputFor(role)!, action, request).ConfigureAwait(false);
            if (exchange.Reset)
                ResetWorker(role);
            return (exchange.Ok, exchange.Response, exchange.Error);
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
    }

    // Pooled variant of the prefetch worker: one of PrefetchWorkerPoolSize persistent
    // processes serves each call, so the fan-out of detail-prefetch tiles renders in
    // parallel instead of queuing behind a single worker. Shares the line-based protocol
    // (ExchangeWithWorkerAsync) with the single-worker path above.
    private static async Task<(bool Ok, TResponse? Response, string Error)> TryInvokePrefetchWorkerAsync<TRequest, TResponse>(
        string action,
        TRequest request)
    {
        var queueWatch = Stopwatch.StartNew();
        await PrefetchPoolSlots.WaitAsync().ConfigureAwait(false);
        long queueMs = queueWatch.ElapsedMilliseconds;
        int slot = PrefetchFreeSlots.TryPop(out int popped) ? popped : 0;

        var execWatch = Stopwatch.StartNew();
        try
        {
            if (!EnsurePrefetchSlot(slot, out string error))
                return (false, default, error);

            var exchange = await ExchangeWithWorkerAsync<TRequest, TResponse>(
                PrefetchWorkerInputs[slot]!, PrefetchWorkerOutputs[slot]!, action, request).ConfigureAwait(false);
            if (exchange.Reset)
                ResetPrefetchSlot(slot);
            return (exchange.Ok, exchange.Response, exchange.Error);
        }
        catch (OperationCanceledException ex)
        {
            ResetPrefetchSlot(slot);
            string error = $"PyMuPDF prefetch worker {action} timed out.";
            AppLog.Warn(ex, error);
            return (false, default, error);
        }
        catch (Exception ex)
        {
            ResetPrefetchSlot(slot);
            AppLog.Warn(ex, $"PyMuPDF prefetch worker {action} failed");
            return (false, default, ex.Message);
        }
        finally
        {
            PrefetchFreeSlots.Push(slot);
            PrefetchPoolSlots.Release();
            ReportWorkerProfile($"pyhelper-prefetch{slot}", action, queueMs, execWatch.ElapsedMilliseconds);
        }
    }

    // One request/response round-trip over a worker's stdio. Returns Reset=true when the
    // worker looks broken (no/garbled reply) so the caller can recycle that specific worker.
    private static async Task<(bool Ok, TResponse? Response, string Error, bool Reset)> ExchangeWithWorkerAsync<TRequest, TResponse>(
        StreamWriter input,
        StreamReader output,
        string action,
        TRequest request)
    {
        string id = Guid.NewGuid().ToString("N");
        var envelope = new WorkerRequest<TRequest>
        {
            Id = id,
            Action = action,
            Request = request,
        };

        await input
            .WriteLineAsync(JsonSerializer.Serialize(envelope, WorkerJsonOptions))
            .ConfigureAwait(false);
        await input.FlushAsync().ConfigureAwait(false);

        // Interactive actions keep the proven 30s bound. A user-started metadata
        // batch may intentionally cover up to 128 pages in one warm document.
        double timeoutSeconds = string.Equals(action, "sheetmeta_batch", StringComparison.Ordinal)
            ? 60
            : 30;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        for (int ignoredLines = 0; ignoredLines < 8; ignoredLines++)
        {
            string? line = await output.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
                return (false, default, "PyMuPDF worker stopped unexpectedly.", true);

            try
            {
                var workerResponse = JsonSerializer.Deserialize<WorkerResponse<TResponse>>(line, WorkerJsonOptions);
                if (workerResponse != null && workerResponse.Id == id)
                    return (true, workerResponse.Response, "", false);
                if (workerResponse != null)
                    return (false, default, "PyMuPDF worker response id mismatch.", true);
            }
            catch (JsonException)
            {
                // MuPDF can write a one-line parser diagnostic to stdout before
                // the protocol response. Ignore only a small bounded number.
            }
        }

        return (false, default, "PyMuPDF worker returned no matching response.", true);
    }

    private static bool EnsureWorker(WorkerRole role, out string error)
    {
        Process? workerProcess = WorkerProcessFor(role);
        StreamWriter? workerInput = WorkerInputFor(role);
        StreamReader? workerOutput = WorkerOutputFor(role);
        if (workerProcess is { HasExited: false } && workerInput != null && workerOutput != null)
        {
            error = "";
            return true;
        }

        ResetWorker(role);
        if (!StartWorkerProcess(WorkerLogPrefix(role), out Process? started, out StreamWriter? input, out StreamReader? output, out error))
            return false;

        SetWorker(role, started, input, output);
        return true;
    }

    private static bool EnsurePrefetchSlot(int slot, out string error)
    {
        if (PrefetchWorkerProcesses[slot] is { HasExited: false } &&
            PrefetchWorkerInputs[slot] != null &&
            PrefetchWorkerOutputs[slot] != null)
        {
            error = "";
            return true;
        }

        ResetPrefetchSlot(slot);
        if (!StartWorkerProcess($"pyhelper-prefetch{slot}", out Process? started, out StreamWriter? input, out StreamReader? output, out error))
            return false;

        PrefetchWorkerProcesses[slot] = started;
        PrefetchWorkerInputs[slot] = input;
        PrefetchWorkerOutputs[slot] = output;
        return true;
    }

    // Spawns one persistent `worker`-mode python helper and wires up its stdio. Shared by
    // the single-worker roles and every prefetch pool slot.
    private static bool StartWorkerProcess(
        string logPrefix,
        out Process? process,
        out StreamWriter? input,
        out StreamReader? output,
        out string error)
    {
        process = null;
        input = null;
        output = null;
        error = "";

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
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
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
                AppLog.Warn($"[{logPrefix}] {e.Data}");
        };
        started.BeginErrorReadLine();

        process = started;
        input = started.StandardInput;
        output = started.StandardOutput;
        return true;
    }

    private static void ResetWorker(WorkerRole role = WorkerRole.Primary)
    {
        KillWorkerProcess(WorkerProcessFor(role), WorkerInputFor(role), WorkerOutputFor(role));
        SetWorker(role, null, null, null);
    }

    private static void ResetPrefetchSlot(int slot)
    {
        KillWorkerProcess(PrefetchWorkerProcesses[slot], PrefetchWorkerInputs[slot], PrefetchWorkerOutputs[slot]);
        PrefetchWorkerProcesses[slot] = null;
        PrefetchWorkerInputs[slot] = null;
        PrefetchWorkerOutputs[slot] = null;
    }

    private static void KillWorkerProcess(Process? process, StreamWriter? input, StreamReader? output)
    {
        try { input?.Dispose(); } catch { }
        try { output?.Dispose(); } catch { }
        try
        {
            if (process is { HasExited: false })
                process.Kill(entireProcessTree: true);
        }
        catch { }
        try { process?.Dispose(); } catch { }
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

        // Drain the whole prefetch pool: take every permit so no render is mid-flight,
        // then recycle each slot's process.
        for (int i = 0; i < PrefetchWorkerPoolSize; i++)
            PrefetchPoolSlots.Wait();
        try
        {
            for (int slot = 0; slot < PrefetchWorkerPoolSize; slot++)
                ResetPrefetchSlot(slot);
        }
        finally
        {
            for (int i = 0; i < PrefetchWorkerPoolSize; i++)
                PrefetchPoolSlots.Release();
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
            foreach (WorkerRole role in new[] { WorkerRole.Primary, WorkerRole.Detail })
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

            // Spin up every prefetch pool slot in parallel so their python startup
            // overlaps each other and the rest of app launch.
            Parallel.For(0, PrefetchWorkerPoolSize, _ =>
            {
                PrefetchPoolSlots.Wait();
                int slot = PrefetchFreeSlots.TryPop(out int popped) ? popped : 0;
                try
                {
                    if (!EnsurePrefetchSlot(slot, out string error) && !string.IsNullOrWhiteSpace(error))
                        AppLog.Warn($"PyMuPDF pyhelper-prefetch{slot} prewarm failed: {error}");
                }
                catch (Exception ex)
                {
                    AppLog.Warn(ex, $"PyMuPDF pyhelper-prefetch{slot} prewarm failed");
                }
                finally
                {
                    PrefetchFreeSlots.Push(slot);
                    PrefetchPoolSlots.Release();
                }
            });

            AppLog.Info("PyMuPDF workers prewarmed.");
        });

    // Primary/Detail are single persistent workers; Prefetch is a pool handled separately
    // (PrefetchWorkerProcesses/Inputs/Outputs + EnsurePrefetchSlot/ResetPrefetchSlot).
    private static SemaphoreSlim WorkerSemaphoreFor(WorkerRole role) => role switch
    {
        WorkerRole.Detail => DetailWorkerSemaphore,
        _ => WorkerSemaphore,
    };

    private static Process? WorkerProcessFor(WorkerRole role) => role switch
    {
        WorkerRole.Detail => DetailWorkerProcess,
        _ => WorkerProcess,
    };

    private static StreamWriter? WorkerInputFor(WorkerRole role) => role switch
    {
        WorkerRole.Detail => DetailWorkerInput,
        _ => WorkerInput,
    };

    private static StreamReader? WorkerOutputFor(WorkerRole role) => role switch
    {
        WorkerRole.Detail => DetailWorkerOutput,
        _ => WorkerOutput,
    };

    private static string WorkerLogPrefix(WorkerRole role) => role switch
    {
        WorkerRole.Detail => "pyhelper-detail",
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
