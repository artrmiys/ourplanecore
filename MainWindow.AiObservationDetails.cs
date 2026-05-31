using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OurPlaneCore.Controls;
using SkiaSharp;


namespace OurPlaneCore;

public partial class MainWindow
{
    // Manual AI responses, AI path helpers, page lookup, and observation details dialog.

    private void AddManualAiResponse(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return;

        SmartAiRequest? request = SmartContextStore.LoadAiRequest(_currentJob, item.Observation.Id);
        if (request == null)
        {
            TxtStatus.Text = "No AI request JSON exists for this Inbox entry.";
            return;
        }

        SmartAiResponse? existing = SmartContextStore.LoadAiResponse(_currentJob, request.Id);
        string initial = existing?.OutputText ?? "";
        string? responseText = ShowMultilineInputDialog(
            $"AI response for {item.TypeShort}\nPage: {item.Page}\nRequest: {request.Id}",
            initial,
            "AI Response");
        if (string.IsNullOrWhiteSpace(responseText))
            return;

        SmartAiResponse response = SmartContextStore.SaveAiResponse(_currentJob, request, "done", responseText, "");
        SmartContextStore.SaveAiActionDraftFromResponse(_currentJob, request, response);
        TxtStatus.Text = $"Saved AI response for {request.Id}.";
        LoadObservationsInbox();
    }

    private string AiRequestPath(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return "";

        return Path.Combine(_currentJob.AIContextRoot, "requests", $"{item.Observation.Id}.json");
    }

    private string AiMarkerPath(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return "";

        return SmartContextStore.AiMarkerPath(_currentJob, item.Observation.Id);
    }

    private string AiResponsePath(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return "";

        return Path.Combine(_currentJob.AIContextRoot, "responses", $"{item.Observation.Id}.json");
    }

    private string AiActionDraftPath(ObservationDisplayItem item)
    {
        if (_currentJob == null)
            return "";

        return SmartContextStore.AiActionDraftPath(_currentJob, item.Observation.Id);
    }

    private PageInfo? FindPageByName(string pageName)
    {
        if (string.IsNullOrWhiteSpace(pageName))
            return null;

        foreach (TreeViewItem item in PagesTree.Items)
        {
            if (FindPageByName(item, pageName) is { } page)
                return page;
        }

        return null;
    }

    private static PageInfo? FindPageByName(TreeViewItem item, string pageName)
    {
        if (item.Tag is PageInfo page &&
            string.Equals(page.Name, pageName, StringComparison.OrdinalIgnoreCase))
        {
            return page;
        }

        foreach (TreeViewItem child in item.Items)
        {
            if (FindPageByName(child, pageName) is { } found)
                return found;
        }

        return null;
    }

    private void ShowObservationDetailsDialog(SmartObservation observation)
    {
        var display = new ObservationDisplayItem(observation);
        var win = new Window
        {
            Title = $"{display.TypeShort} - {observation.Id}",
            Width = 680,
            Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            Owner = this,
        };

        var panel = new DockPanel { Margin = new Thickness(10) };
        var header = new TextBlock
        {
            Text = $"{display.TypeShort} | Page: {(string.IsNullOrWhiteSpace(display.Page) ? "Unassigned" : display.Page)} | {display.TimeDisplay}",
            FontWeight = FontWeights.Normal,
            Margin = new Thickness(0, 0, 0, 8),
        };
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var goToPage = new Button { Content = "Go to Page", MinWidth = 90, Margin = new Thickness(0, 0, 6, 0), IsEnabled = CanGoToObservationPage(display) };
        var openCrop = new Button { Content = "Open Crop", MinWidth = 90, Margin = new Thickness(0, 0, 6, 0), IsEnabled = CanOpenObservationCrop(display) };
        var openRequest = new Button { Content = "Request JSON", MinWidth = 105, Margin = new Thickness(0, 0, 6, 0), IsEnabled = CanOpenAiRequestFile(display) };
        var runAi = new Button { Content = "Run AI", MinWidth = 82, Margin = new Thickness(0, 0, 6, 0), IsEnabled = CanRunAiRequest(display) };
        var addResponse = new Button { Content = "AI Response", MinWidth = 100, Margin = new Thickness(0, 0, 6, 0), IsEnabled = CanAddManualAiResponse(display) };
        var openContext = new Button { Content = "Project Context", MinWidth = 110, Margin = new Thickness(0, 0, 6, 0), IsEnabled = _currentJob != null };
        var close = new Button { Content = "Close", Width = 78, IsCancel = true };
        TextBox? detailsText = null;
        goToPage.Click += (_, _) => GoToObservationPage(display);
        openCrop.Click += (_, _) => OpenObservationCrop(display);
        openRequest.Click += (_, _) => OpenAiRequestFile(display);
        runAi.Click += async (_, _) =>
        {
            await RunAiRequestAsync(display);
            if (detailsText != null)
                detailsText.Text = ObservationDetailsText(observation);
        };
        addResponse.Click += (_, _) => AddManualAiResponse(display);
        openContext.Click += (_, _) => OpenProjectContextMarkdown();
        close.Click += (_, _) => win.Close();
        buttons.Children.Add(goToPage);
        buttons.Children.Add(openCrop);
        buttons.Children.Add(openRequest);
        buttons.Children.Add(runAi);
        buttons.Children.Add(addResponse);
        buttons.Children.Add(openContext);
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);

        var text = new TextBox
        {
            Text = ObservationDetailsText(observation),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        detailsText = text;
        panel.Children.Add(text);

        win.Content = panel;
        win.Loaded += (_, _) => text.Focus();
        win.ShowDialog();
    }

    private string ObservationDetailsText(SmartObservation observation)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Id: {observation.Id}");
        sb.AppendLine($"Type: {observation.Type}");
        sb.AppendLine($"Page: {observation.Page}");
        sb.AppendLine($"Created UTC: {observation.CreatedAtUtc}");

        if (_currentJob != null && SmartContextStore.LoadAiMarker(_currentJob, observation.Id) is { } marker)
        {
            sb.AppendLine();
            sb.AppendLine("AI Marker");
            sb.AppendLine($"Marker JSON: {SmartContextStore.AiMarkerPath(_currentJob, marker.Id)}");
            sb.AppendLine($"Type: {marker.Type}");
            sb.AppendLine($"Sample kind: {marker.SampleKind}");
            sb.AppendLine($"PDF point: {marker.PdfPoint.X:F1}, {marker.PdfPoint.Y:F1}");
            if (!string.IsNullOrWhiteSpace(marker.Value))
                sb.AppendLine($"Value: {marker.Value}");
            if (!string.IsNullOrWhiteSpace(marker.Note))
                sb.AppendLine($"Note: {marker.Note}");
            if (marker.LayerCount > 0)
                sb.AppendLine($"Layers: {marker.LayerCount}");
        }

        if (_currentJob != null && SmartContextStore.LoadAiRequest(_currentJob, observation.Id) is { } request)
        {
            sb.AppendLine();
            sb.AppendLine("AI Request");
            sb.AppendLine($"Status: {request.Status}");
            sb.AppendLine($"Request JSON: {Path.Combine(_currentJob.AIContextRoot, "requests", $"{request.Id}.json")}");
            if (!string.IsNullOrWhiteSpace(request.CropPath))
                sb.AppendLine($"Crop: {request.CropPath}");
            if (request.LayerCount > 0 || !string.IsNullOrWhiteSpace(request.LayerManifestPath))
            {
                string layerSummary = request.LayerCount == 1 ? "1 layer" : $"{request.LayerCount} layers";
                if (!string.IsNullOrWhiteSpace(request.LayerManifestPath))
                    sb.AppendLine($"Layers: {layerSummary} ({request.LayerManifestPath})");
                else
                    sb.AppendLine($"Layers: {layerSummary}");
            }

            if (SmartContextStore.LoadAiResponse(_currentJob, request.Id) is { } response)
            {
                sb.AppendLine();
                sb.AppendLine("AI Response");
                sb.AppendLine($"Status: {response.Status}");
                if (!string.IsNullOrWhiteSpace(response.OutputText))
                    sb.AppendLine(response.OutputText);
                if (!string.IsNullOrWhiteSpace(response.Error))
                    sb.AppendLine(response.Error);
                if (!string.IsNullOrWhiteSpace(response.Model))
                    sb.AppendLine($"Model: {response.Model}");
                if (!string.IsNullOrWhiteSpace(response.RawResponsePath))
                    sb.AppendLine($"Raw response: {response.RawResponsePath}");
            }

            if (SmartContextStore.LoadAiActionDraft(_currentJob, request.Id) is { } draft)
            {
                sb.AppendLine();
                sb.AppendLine("AI Action Draft");
                sb.AppendLine($"Status: {draft.Status}");
                sb.AppendLine($"Actions: {draft.Actions.Count}");
                sb.AppendLine($"Draft JSON: {SmartContextStore.AiActionDraftPath(_currentJob, request.Id)}");
                if (!string.IsNullOrWhiteSpace(draft.Summary))
                    sb.AppendLine($"Summary: {draft.Summary}");
            }
        }

        sb.AppendLine();
        sb.AppendLine(observation.Text);
        return sb.ToString();
    }
}
