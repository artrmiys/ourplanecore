using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OurPlaneCore.Controls;
using SkiaSharp;


namespace OurPlaneCore;

public partial class MainWindow
{
    // AI request execution and sheet-metadata response application.

    private CancellationTokenSource? _activeAiRequestCts;

    private bool CanOpenAiRequestFile(ObservationDisplayItem item) =>
        IsModuleEnabled(ModuleId.Ai) && _currentJob != null && File.Exists(AiRequestPath(item));

    private bool CanAddManualAiResponse(ObservationDisplayItem item) =>
        IsModuleEnabled(ModuleId.Ai) &&
        _currentJob != null &&
        SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id) != null;

    private bool CanRunAiRequest(ObservationDisplayItem item)
    {
        if (!IsModuleEnabled(ModuleId.Ai) || _currentJob == null || _isRunningAiRequest)
            return false;

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id);
        return request != null && IsRunnableAiStatus(request.Status);
    }

    private bool CanApplySheetMetadataResponse(ObservationDisplayItem item)
    {
        if (!IsModuleEnabled(ModuleId.Ai) ||
            !IsModuleEnabled(ModuleId.SheetManager) ||
            _currentJob == null)
            return false;

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id);
        if (request == null ||
            !string.Equals(request.Type, "pdf_sheet_metadata_fallback", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        SmartAiResponse? response = SmartContextStore.LoadAiResponse(_currentJob, request.Id);
        return response != null &&
               string.Equals(response.Status, "done", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(response.OutputText) &&
               TryResolveRequestPage(request, out _);
    }

    private void ApplySheetMetadataResponse(ObservationDisplayItem item)
    {
        if (!RequireModule(ModuleId.Ai, "Apply AI sheet metadata response") ||
            !RequireModule(ModuleId.SheetManager, "Apply AI sheet metadata response"))
            return;

        if (_currentJob == null)
            return;

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id);
        if (request == null)
        {
            TxtStatus.Text = "No AI request JSON exists for this Inbox entry.";
            return;
        }

        if (!TrySaveSheetMetadataFromFallbackResponse(request, out PdfMetadataPageResult? result, out string error) ||
            result?.Metadata == null)
        {
            MessageBox.Show(error, "Apply Sheet Metadata Response", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var rows = BuildPdfMetadataPreviewRows([result], defaultRename: true, defaultScale: true).ToList();
        var dialog = new PdfMetadataPreviewDialog(rows, "Apply Sheet Metadata Response")
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        ApplyPdfMetadataResults(_currentJob, [result], dialog.Rows);
    }

    private bool TrySaveSheetMetadataFromFallbackResponse(
        SmartAiRequest request,
        out PdfMetadataPageResult? result,
        out string error)
    {
        result = null;
        error = "";

        if (!IsModuleEnabled(ModuleId.Ai) || !IsModuleEnabled(ModuleId.SheetManager))
        {
            error = "AI or Sheet Manager is disabled in Settings.";
            return false;
        }

        if (_currentJob == null)
        {
            error = "Open a job before applying sheet metadata.";
            return false;
        }

        if (!TryResolveRequestPage(request, out PageInfo? page) || page == null)
        {
            error = "Could not resolve the page for this sheet metadata response.";
            return false;
        }

        SmartAiResponse? response = SmartContextStore.LoadAiResponse(_currentJob, request.Id);
        if (response == null)
        {
            error = "No AI response exists for this sheet metadata request.";
            return false;
        }

        if (!string.Equals(response.Status, "done", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(response.OutputText))
        {
            error = string.IsNullOrWhiteSpace(response.Error)
                ? $"AI response is not ready ({response.Status})."
                : response.Error;
            return false;
        }

        if (!PdfSheetMetadataService.TryBuildMetadataFromFallbackResponse(
                page,
                request,
                response,
                out PdfSheetMetadata metadata,
                out error,
                _currentJob))
        {
            return false;
        }

        OurPlaneCoreJobStore.WriteSourcePdfMetadata(page.FolderPath, metadata);
        result = new PdfMetadataPageResult(page, true, metadata, "");
        return true;
    }

    private bool TryResolveRequestPage(SmartAiRequest request, out PageInfo? page)
    {
        page = null;
        if (_currentJob == null)
            return false;

        if (!string.IsNullOrWhiteSpace(request.PageFolder))
        {
            string folder = Path.IsPathFullyQualified(request.PageFolder)
                ? request.PageFolder
                : Path.GetFullPath(Path.Combine(_currentJob.RootPath, request.PageFolder));
            page = OurPlaneCoreJobStore.TryReadPage(folder);
            if (page != null)
                return true;
        }

        page = FindPageByName(request.Page);
        return page != null;
    }

    private async Task RunSelectedOrNextAiRequestAsync()
    {
        if (!RequireModule(ModuleId.Ai, "Run AI request"))
            return;

        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before running AI.";
            return;
        }

        if (_isRunningAiRequest)
        {
            TxtStatus.Text = "AI request is already running.";
            return;
        }

        SmartAiRequest? request = null;
        if (SelectedObservationDisplayItem() is { } selected)
        {
            SmartAiRequest? selectedRequest = SmartContextStore.LoadAiRequest(_currentJob, selected.Observation.Id);
            if (selectedRequest != null && IsRunnableAiStatus(selectedRequest.Status))
                request = selectedRequest;
        }

        request ??= SmartContextStore.LoadAiRequests(_currentJob)
            .FirstOrDefault(candidate => IsRunnableAiStatus(candidate.Status));

        if (request == null)
        {
            TxtStatus.Text = "No pending AI request to run.";
            return;
        }

        await RunAiRequestAsync(request);
    }

    private async Task RunAiRequestAsync(ObservationDisplayItem item)
    {
        if (!RequireModule(ModuleId.Ai, "Run AI request"))
            return;

        if (_currentJob == null)
            return;

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id);
        if (request == null)
        {
            TxtStatus.Text = "No AI request JSON exists for this Inbox entry.";
            return;
        }

        await RunAiRequestAsync(request);
    }

    private async Task RunAiRequestAsync(SmartAiRequest request)
    {
        if (!RequireModule(ModuleId.Ai, "Run AI request"))
            return;

        if (_currentJob == null)
            return;

        if (IsRoofRecognitionRequest(request) && StopLegacy3DMassingWorkflow("Auto Roof AI request"))
            return;

        if (_isRunningAiRequest)
        {
            TxtStatus.Text = "AI request is already running.";
            return;
        }

        string apiKey = ReadOpenAiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            TxtStatus.Text = "Set OPENAI_API_KEY in Windows environment, then run AI again.";
            return;
        }

        string model = AppSettingsStore.ResolveOpenAiModel(_settings);
        var runCts = new CancellationTokenSource();
        _activeAiRequestCts = runCts;
        _isRunningAiRequest = true;
        try
        {
            request.Status = "running";
            SmartContextStore.SaveAiRequest(_currentJob, request);
            TxtStatus.Text = $"Running AI request {request.Id}...";
            LoadObservationsInbox();

            SmartAiRunResult result;
            using (ShowBusyOverlay($"Running AI request {request.Id}..."))
            {
                await WaitForBusyOverlayRenderAsync();
                result = await OpenAiRequestRunner.RunAsync(
                    _currentJob,
                    request,
                    apiKey,
                    model,
                    runCts.Token);
            }

            runCts.Token.ThrowIfCancellationRequested();

            if (result.Success)
            {
                SmartAiResponse response = SmartContextStore.SaveAiResponse(
                    _currentJob,
                    request,
                    "done",
                    result.OutputText,
                    "",
                    "openai",
                    result.Model,
                    result.ProviderResponseId,
                    result.RawResponsePath);
                SmartContextStore.SaveAiActionDraftFromResponse(_currentJob, request, response);
                TxtStatus.Text = $"AI response saved for {request.Id}.";
            }
            else
            {
                SmartAiResponse response = SmartContextStore.SaveAiResponse(
                    _currentJob,
                    request,
                    "failed",
                    "",
                    result.Error,
                    "openai",
                    result.Model,
                    result.ProviderResponseId,
                    result.RawResponsePath);
                SmartContextStore.SaveAiActionDraftFromResponse(_currentJob, request, response);
                ReportAiRequestFailure(result.Error);
            }
        }
        catch (OperationCanceledException) when (runCts.IsCancellationRequested)
        {
            SmartContextStore.SaveAiResponse(
                _currentJob,
                request,
                "cancelled",
                "",
                "AI request cancelled because the AI module was disabled.",
                "openai",
                model,
                "",
                "");
            TxtStatus.Text = $"AI request {request.Id} cancelled.";
        }
        catch (Exception ex)
        {
            SmartAiResponse response = SmartContextStore.SaveAiResponse(
                _currentJob,
                request,
                "failed",
                "",
                ex.Message,
                "openai",
                model,
                "",
                "");
            SmartContextStore.SaveAiActionDraftFromResponse(_currentJob, request, response);
            ReportAiRequestFailure(ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_activeAiRequestCts, runCts))
                _activeAiRequestCts = null;
            runCts.Dispose();
            _isRunningAiRequest = false;
            LoadObservationsInbox();
        }
    }

    private void CancelActiveAiWorkForModuleDisable()
    {
        _activeAiRequestCts?.Cancel();
        _viewport.ClearAiMarkers();
        _viewport.ClearAiActionDraftPreview();
        if (_isRunningAiRequest)
            TxtStatus.Text = "Cancelling active AI work...";
    }

    // A failed AI run used to surface only in the status bar, which is easy
    // to miss minutes after pressing the button. Make it explicit.
    private void ReportAiRequestFailure(string error)
    {
        TxtStatus.Text = $"AI request failed: {error}";
        System.Windows.MessageBox.Show(
            $"AI request failed:\n{error}",
            "AI Request",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning);
    }

    private static string ReadOpenAiApiKey()
    {
        return AppSettingsStore.ReadOpenAiApiKey();
    }

    private static bool IsRunnableAiStatus(string status) =>
        string.IsNullOrWhiteSpace(status) ||
        status.Equals("pending", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("cancelled", StringComparison.OrdinalIgnoreCase);
}
