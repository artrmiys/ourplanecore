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
}
