using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

public partial class MainWindow
{
    private async void BtnQueuePdfMetadataFallback_Click(object sender, RoutedEventArgs e)
    {
        await RunAsyncUiHandler(
            async () =>
            {
                if (await TryRunSheetManagerPdfMetadataFallbackAsync())
                    return;

                if (GetSelectedPdfAutomationTarget("AI Fill") is { } item)
                    QueuePdfMetadataFallback(item);
            },
            "AI Fill failed.",
            "AI Fill");
    }

    private async void BtnPdfMetadataCropHints_Click(object sender, RoutedEventArgs e)
    {
        await RunAsyncUiHandler(
            () =>
            {
                ConfigurePdfMetadataCropHints();
                return Task.CompletedTask;
            },
            "AI Fill crop hints failed.",
            "AI Fill Crop Hints");
    }

    private async Task<bool> TryRunSheetManagerPdfMetadataFallbackAsync()
    {
        if (WorkspaceTabs.SelectedItem is not TabItem tab ||
            !string.Equals(tab.Tag?.ToString(), "SheetManager", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before PDF automation.";
            return true;
        }

        IReadOnlyList<PageInfo> pages = SheetManagerAiFillPages();

        if (pages.Count == 0)
        {
            PostStatusInfo("No PDF pages found in Sheet Manager.");
            return true;
        }

        if (string.IsNullOrWhiteSpace(ReadOpenAiApiKey()))
        {
            PostStatusWarning("AI Fill needs OPENAI_API_KEY before it can run GPT metadata. Open OpenAI Settings, then run AI Fill again.");
            return true;
        }

        SheetManagerGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        SheetManagerGrid.CommitEdit(DataGridEditingUnit.Row, true);

        PdfMetadataFallbackQueueResult queueResult = QueuePdfMetadataFallback(pages, showMessage: false);
        IReadOnlyList<SmartAiRequest> requests = RunnableMetadataFallbackRequests(_currentJob, pages);

        if (requests.Count == 0)
        {
            ShowSheetManagerAiFillNoRequests(queueResult);
            return true;
        }

        TxtStatus.Text = $"AI Fill running GPT metadata for {requests.Count} sheet(s)...";
        (int Ran, int Saved, List<string> Errors) runResult;
        using (ShowBusyOverlay($"AI Fill running GPT metadata for {requests.Count} sheet(s)..."))
        {
            await WaitForBusyOverlayRenderAsync();
            runResult = await RunAndSaveSheetMetadataFallbackRequestsAsync(requests, queueResult.Errors);
        }

        RefreshSheetManager();

        string summary =
            $"AI Fill complete. Queued: {queueResult.Queued}. Ran: {runResult.Ran}. " +
            $"Updated metadata: {runResult.Saved}. Skipped: {queueResult.Skipped}. " +
            $"Failed: {runResult.Errors.Count}.";
        ShowSheetManagerAiFillErrors(summary, runResult.Errors);

        TxtStatus.Text = summary;
        return true;
    }

    private IReadOnlyList<PageInfo> SheetManagerAiFillPages()
    {
        IReadOnlyList<PageInfo> pages = SelectedSheetManagerPages();
        if (pages.Count > 0)
            return pages;

        return SheetManagerRows()
            .Select(row => OurPlaneCoreJobStore.TryReadPage(row.PageFolder))
            .Where(page => page != null)
            .Cast<PageInfo>()
            .GroupBy(page => NormalizePath(page.FolderPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static IReadOnlyList<SmartAiRequest> RunnableMetadataFallbackRequests(
        OurPlaneCoreJob job,
        IReadOnlyList<PageInfo> pages) =>
        MetadataFallbackRequestsForPages(job, pages)
            .Where(request => MetadataFallbackRequestStillNeeded(job, request, pages))
            .Where(request => IsRunnableAiStatus(request.Status) || HasDoneAiResponse(job, request))
            .GroupBy(request => request.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

    private void ShowSheetManagerAiFillNoRequests(PdfMetadataFallbackQueueResult queueResult)
    {
        string message = BuildPdfMetadataFallbackQueueMessage(queueResult);
        MessageBox.Show(
            message.Length == 0 ? "No GPT metadata fallback was needed for the selected sheets." : message,
            "AI Fill",
            MessageBoxButton.OK,
            queueResult.Failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        TxtStatus.Text = queueResult.Queued > 0
            ? "AI Fill queued metadata fallback, but no runnable request was found."
            : "AI Fill: no GPT metadata fallback needed.";
    }

    private async Task<(int Ran, int Saved, List<string> Errors)> RunAndSaveSheetMetadataFallbackRequestsAsync(
        IReadOnlyList<SmartAiRequest> requests,
        IEnumerable<string> initialErrors)
    {
        int ran = 0;
        int saved = 0;
        var errors = new List<string>(initialErrors);
        OurPlaneCoreJob job = _currentJob!;

        foreach (SmartAiRequest request in requests)
        {
            SmartAiRequest current = SmartContextStore.LoadAiRequest(job, request.Id) ?? request;
            if (IsRunnableAiStatus(current.Status))
            {
                string statusBeforeRun = current.Status;
                await RunAiRequestAsync(current);
                current = SmartContextStore.LoadAiRequest(job, request.Id) ?? current;
                if (!string.Equals(statusBeforeRun, current.Status, StringComparison.OrdinalIgnoreCase) ||
                    HasDoneAiResponse(job, current))
                {
                    ran++;
                }
            }

            if (TrySaveSheetMetadataFromFallbackResponse(current, out _, out string error))
                saved++;
            else if (!string.IsNullOrWhiteSpace(error))
                errors.Add($"{current.Page}: {error}");
        }

        return (ran, saved, errors);
    }

    private static void ShowSheetManagerAiFillErrors(string summary, IReadOnlyList<string> errors)
    {
        if (errors.Count == 0)
            return;

        MessageBox.Show(
            summary + Environment.NewLine + string.Join(Environment.NewLine, errors.Take(6)),
            "AI Fill",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private sealed class PdfMetadataFallbackQueueResult
    {
        public int Queued { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public List<string> Errors { get; } = [];
        public List<SmartAiRequest> Requests { get; } = [];
    }

    private void QueuePdfMetadataFallback(TreeViewItem item)
    {
        if (_currentJob == null)
            return;

        var pages = GetPagesForMetadata(item).ToList();
        if (pages.Count == 0)
            return;

        QueuePdfMetadataFallback(pages);
    }

    private PdfMetadataFallbackQueueResult QueuePdfMetadataFallback(IReadOnlyList<PageInfo> pages, bool showMessage = true)
    {
        var result = new PdfMetadataFallbackQueueResult();
        if (_currentJob == null || pages.Count == 0)
            return result;

        PdfSheetMetadataCropTemplate? cropTemplate = LoadOrOfferPdfMetadataCropTemplate(pages);

        foreach (PageInfo page in pages)
        {
            try
            {
                PdfSheetMetadata? metadata = OurPlaneCoreJobStore.ReadSourcePdfMetadata(page.FolderPath);
                if (metadata == null)
                {
                    PdfSheetMetadataService.TryAnalyzeAndSave(_currentJob, page, out metadata, out _);
                }

                if (!PdfSheetMetadataService.NeedsFallback(metadata))
                {
                    result.Skipped++;
                    continue;
                }

                if (HasExistingMetadataFallbackRequest(_currentJob, page))
                {
                    result.Skipped++;
                    continue;
                }

                string cropsRoot = Path.Combine(_currentJob.AIContextRoot, "crops");
                string filePrefix = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_sheetmeta_{SafeFileNamePart(page.Name)}";
                string cropWarning = "";
                IReadOnlyList<PdfSheetMetadataSavedCrop> savedCrops = [];
                if (PdfSheetMetadataCropService.HasUsableTemplate(cropTemplate) &&
                    !PdfSheetMetadataCropService.TrySaveManualFallbackCrops(
                        page,
                        cropTemplate!,
                        cropsRoot,
                        filePrefix,
                        out savedCrops,
                        out string manualCropError))
                {
                    cropWarning = $"Manual crop hints were skipped: {manualCropError}";
                    savedCrops = [];
                }

                if (savedCrops.Count == 0)
                {
                    string cropPath = Path.Combine(cropsRoot, $"{filePrefix}.png");
                    if (!PdfSheetMetadataService.TrySaveFallbackCrop(page, cropPath, out SKRect cropRect, out string error))
                    {
                        result.Failed++;
                        result.Errors.Add(
                            string.IsNullOrWhiteSpace(cropWarning)
                                ? $"{page.Name}: {error}"
                                : $"{page.Name}: {error}; {cropWarning}");
                        continue;
                    }

                    savedCrops = [new PdfSheetMetadataSavedCrop("title_block", cropPath, cropRect)];
                }

                var promptCrops = savedCrops
                    .Select(crop => (
                        crop.Role,
                        RelativePath: Path.GetRelativePath(_currentJob.AIContextRoot, crop.Path),
                        crop.PdfRect))
                    .ToList();

                if (promptCrops.Count == 0)
                {
                    result.Failed++;
                    result.Errors.Add($"{page.Name}: no AI crop was saved.");
                    continue;
                }

                string relativeCrop = promptCrops[0].RelativePath;
                string observationText =
                    "GPT fallback requested for PDF sheet metadata." + Environment.NewLine +
                    BuildPdfMetadataCropObservation(promptCrops) +
                    $"- Page folder: {Path.GetRelativePath(_currentJob.RootPath, page.FolderPath)}" + Environment.NewLine +
                    $"- Current page name: {page.Name}" + Environment.NewLine +
                    $"- Current PDF source: {page.PdfPath}" + Environment.NewLine +
                    $"- Current PDF page: {page.PdfPage + 1}" + Environment.NewLine;

                if (!string.IsNullOrWhiteSpace(cropWarning))
                    observationText += $"- Crop hint warning: {cropWarning}" + Environment.NewLine;

                if (metadata != null)
                {
                    observationText +=
                        "- Deterministic metadata JSON:" + Environment.NewLine +
                        JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }) +
                        Environment.NewLine;
                }

                SmartObservation observation = SmartContextStore.AddObservation(
                    _currentJob,
                    page,
                    "pdf_sheet_metadata_fallback",
                    observationText);
                SmartAiRequest request = SmartContextStore.AddAiRequest(
                    _currentJob,
                    page,
                    observation,
                    "pdf_sheet_metadata_fallback",
                    BuildPdfMetadataFallbackPrompt(page, metadata, promptCrops),
                    relativeCrop,
                    "Read the sheet metadata crops and return sheet metadata JSON only.",
                    promptCrops.Skip(1).Select(crop => crop.RelativePath));
                result.Requests.Add(request);
                result.Queued++;
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Errors.Add($"{page.Name}: {ex.Message}");
            }
        }

        LoadObservationsInbox();
        if (showMessage)
        {
            MessageBox.Show(
                BuildPdfMetadataFallbackQueueMessage(result),
                "Queue GPT Metadata Fallback",
                MessageBoxButton.OK,
                result.Failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        TxtStatus.Text = $"Queued GPT metadata fallback for {result.Queued} page(s).";
        return result;
    }

    private void ConfigurePdfMetadataCropHints()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before AI Fill crop hints.";
            return;
        }

        PageInfo? page = SelectedSheetManagerPages().FirstOrDefault() ??
                         _currentPage ??
                         SheetManagerAiFillPages().FirstOrDefault();

        if (page == null)
        {
            PostStatusInfo("Open or select a PDF sheet before configuring AI Fill crop hints.");
            return;
        }

        PdfSheetMetadataCropTemplate? template = ShowPdfMetadataCropTemplateDialog(page, showSavedMessage: true);
        if (template != null)
            TxtStatus.Text = $"AI Fill crop hints saved from {page.Name}.";
    }

    private PdfSheetMetadataCropTemplate? LoadOrOfferPdfMetadataCropTemplate(IReadOnlyList<PageInfo> pages)
    {
        if (_currentJob == null)
            return null;

        PdfSheetMetadataCropTemplate? template = PdfSheetMetadataCropService.LoadTemplate(_currentJob);
        if (PdfSheetMetadataCropService.HasUsableTemplate(template))
            return template;

        List<PageInfo> fallbackPages = pages
            .Where(PageNeedsPdfMetadataFallback)
            .ToList();
        if (fallbackPages.Count == 0)
            return null;

        MessageBoxResult answer = MessageBox.Show(
            $"AI Fill still needs help on {fallbackPages.Count} sheet(s)." + Environment.NewLine +
            "Do you want to pick one representative sheet and draw crop boxes for the sheet number and scale?",
            "AI Fill Crop Hints",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return null;

        PageInfo sample = fallbackPages.FirstOrDefault(page =>
            _currentPage != null &&
            IsSamePageFolder(page.FolderPath, _currentPage.FolderPath)) ?? fallbackPages[0];

        return ShowPdfMetadataCropTemplateDialog(sample, showSavedMessage: false);
    }

    private bool PageNeedsPdfMetadataFallback(PageInfo page)
    {
        if (_currentJob == null)
            return false;

        PdfSheetMetadata? metadata = OurPlaneCoreJobStore.ReadSourcePdfMetadata(page.FolderPath);
        if (metadata == null)
            PdfSheetMetadataService.TryAnalyzeAndSave(_currentJob, page, out metadata, out _);

        return PdfSheetMetadataService.NeedsFallback(metadata);
    }

    private PdfSheetMetadataCropTemplate? ShowPdfMetadataCropTemplateDialog(PageInfo page, bool showSavedMessage)
    {
        if (_currentJob == null)
            return null;

        if (!PdfSheetMetadataCropService.TryRenderPage(page, out PdfLayerRenderResult render, out string error))
        {
            PostStatusWarning(error);
            return null;
        }

        var dialog = new PdfMetadataCropTemplateDialog(
            page,
            render,
            PdfSheetMetadataCropService.LoadTemplate(_currentJob))
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
            return null;

        dialog.CropTemplate.SourcePageFolder = Path.GetRelativePath(_currentJob.RootPath, page.FolderPath);
        PdfSheetMetadataCropService.SaveTemplate(_currentJob, dialog.CropTemplate);
        if (showSavedMessage)
        {
            PostStatusInfo($"Saved crop hints for AI Fill: {PdfSheetMetadataCropService.TemplatePath(_currentJob)}");
        }

        return dialog.CropTemplate;
    }

    private static string BuildPdfMetadataCropObservation(
        IReadOnlyList<(string Role, string RelativePath, SKRect PdfRect)> crops)
    {
        var sb = new StringBuilder();
        foreach ((string role, string relativePath, SKRect rect) in crops)
        {
            sb.AppendLine($"- AI crop ({role}): {relativePath}");
            sb.AppendLine($"- PDF crop ({role}): {FormatPdfRect(rect)}");
        }

        return sb.ToString();
    }

    private static string BuildPdfMetadataFallbackQueueMessage(PdfMetadataFallbackQueueResult result)
    {
        string message = $"Queued: {result.Queued}. Skipped: {result.Skipped}. Failed: {result.Failed}.";
        if (result.Errors.Count > 0)
            message += Environment.NewLine + string.Join(Environment.NewLine, result.Errors.Take(5));
        return message;
    }

    private static IReadOnlyList<SmartAiRequest> MetadataFallbackRequestsForPages(
        OurPlaneCoreJob job,
        IReadOnlyList<PageInfo> pages) =>
        SmartContextStore.LoadAiRequests(job)
            .Where(request => string.Equals(request.Type, "pdf_sheet_metadata_fallback", StringComparison.OrdinalIgnoreCase))
            .Where(request => pages.Any(page => MetadataFallbackRequestMatchesPage(job, request, page)))
            .OrderBy(request => request.CreatedAtUtc, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool HasDoneAiResponse(OurPlaneCoreJob job, SmartAiRequest request)
    {
        SmartAiResponse? response = SmartContextStore.LoadAiResponse(job, request.Id);
        return response != null &&
               string.Equals(response.Status, "done", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(response.OutputText);
    }

    private static bool MetadataFallbackRequestStillNeeded(
        OurPlaneCoreJob job,
        SmartAiRequest request,
        IReadOnlyList<PageInfo> pages) =>
        pages.Any(page =>
            MetadataFallbackRequestMatchesPage(job, request, page) &&
            PdfSheetMetadataService.NeedsFallback(OurPlaneCoreJobStore.ReadSourcePdfMetadata(page.FolderPath)));

    private static bool MetadataFallbackRequestMatchesPage(OurPlaneCoreJob job, SmartAiRequest request, PageInfo page)
    {
        string pageFolder = NormalizePath(page.FolderPath);
        string relativePageFolder = Path.GetRelativePath(job.RootPath, page.FolderPath);
        if (!string.IsNullOrWhiteSpace(request.PageFolder))
        {
            string requestFolder = Path.IsPathFullyQualified(request.PageFolder)
                ? NormalizePath(request.PageFolder)
                : NormalizePath(Path.Combine(job.RootPath, request.PageFolder));

            if (string.Equals(requestFolder, pageFolder, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(request.PageFolder, relativePageFolder, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return string.Equals(request.Page, page.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasExistingMetadataFallbackRequest(OurPlaneCoreJob job, PageInfo page)
    {
        string pageFolder = NormalizePath(page.FolderPath);
        string relativePageFolder = Path.GetRelativePath(job.RootPath, page.FolderPath);
        foreach (SmartAiRequest request in SmartContextStore.LoadAiRequests(job))
        {
            if (!string.Equals(request.Type, "pdf_sheet_metadata_fallback", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(request.Status, "failed", StringComparison.OrdinalIgnoreCase))
                continue;

            string requestFolder = "";
            if (!string.IsNullOrWhiteSpace(request.PageFolder))
            {
                requestFolder = Path.IsPathFullyQualified(request.PageFolder)
                    ? NormalizePath(request.PageFolder)
                    : NormalizePath(Path.Combine(job.RootPath, request.PageFolder));
            }

            if (string.Equals(requestFolder, pageFolder, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(request.PageFolder, relativePageFolder, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildPdfMetadataFallbackPrompt(
        PageInfo page,
        PdfSheetMetadata? metadata,
        IReadOnlyList<(string Role, string RelativePath, SKRect PdfRect)> crops)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Read this construction sheet title-block crop.");
        sb.AppendLine("Return one fenced JSON block only. Do not include prose outside JSON.");
        sb.AppendLine();
        sb.AppendLine("Required JSON shape:");
        sb.AppendLine("{");
        sb.AppendLine("  \"sheet_label\": \"S-100\",");
        sb.AppendLine("  \"sheet_key\": \"s100\",");
        sb.AppendLine("  \"sheet_title\": \"FOUNDATION PLAN\",");
        sb.AppendLine("  \"suffix\": \"f\",");
        sb.AppendLine("  \"skip_scale\": false,");
        sb.AppendLine("  \"selected_scale_text\": \"1/8\\\" = 1'0\\\"\",");
        sb.AppendLine("  \"confidence\": \"gpt-image-high | gpt-image-medium | gpt-image-low\",");
        sb.AppendLine("  \"warnings\": []");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Naming suffix rules:");
        sb.AppendLine("notes=n skip, schedules=sc skip, details=d skip, foundation=f, first=1st, second=2nd, third=3rd, fourth=4th, roof=rf, sections=sec, unit plans=u, wall/partition types=wt, floor types=ft.");
        sb.AppendLine("Sections must be scale eligible. Details/notes/schedules must skip scale.");
        sb.AppendLine("Allowed scales: 1/32\", 3/64\", 1/16\", 3/32\", 1/10\", 1/8\", 3/16\", 1/4\", 3/8\", 1/2\", 3/4\", 1\", 1-1/2\", 3\" = 1'0\", and 1\" = 1\".");
        sb.AppendLine("If unsure, leave fields empty and add a warning.");
        sb.AppendLine();
        if (crops.Count > 0)
        {
            sb.AppendLine("Attached crop roles:");
            foreach ((string role, string relativePath, SKRect rect) in crops)
                sb.AppendLine($"- {role}: {relativePath}; {FormatPdfRect(rect)}");
            sb.AppendLine("Use sheet_number for sheet_label/sheet_key. Use scale for selected_scale_text. Use title_block for title/suffix context when available.");
            sb.AppendLine();
        }

        sb.AppendLine($"Current page name: {page.Name}");
        if (metadata != null)
        {
            sb.AppendLine("Deterministic PDF metadata before fallback:");
            sb.AppendLine(JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        }

        return sb.ToString();
    }
}
