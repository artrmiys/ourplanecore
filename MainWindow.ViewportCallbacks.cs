using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

public partial class MainWindow
{
    // Viewport callbacks

    private void OnScaleChanged(double scale)
    {
        if (_currentPage != null)
            _currentPage.ScaleMetersPerPt = scale;
        ApplyScaleToCurrentPageMeasurements(scale);
        SaveCurrentPageScale();
        UpdateScaleUi(scale);
        RefreshFloatingPageSetup(_currentPage?.FolderPath);
        RefreshPagesTakeoffIndicators();
        RefreshAllTotals();
    }

    private void ApplyScaleToCurrentPageMeasurements(double scale)
    {
        if (_currentPage == null || scale <= 0)
            return;

        foreach (var measurement in _takeoffItems.SelectMany(i => i.Measurements))
        {
            if (IsSamePageFolder(measurement.PageFolder, _currentPage.FolderPath))
                measurement.ScaleMetersPerPt = scale;
        }
    }

    private void OnToolChanged(string tool) => SetTool(tool);

    private void OnViewportContextRequested(ViewportContextRequest request)
    {
        if (_currentJob == null || _currentPage == null)
        {
            TxtStatus.Text = "Open a job and page before using AI Assist.";
            return;
        }

        var menu = new ContextMenu
        {
            PlacementTarget = _viewport,
            Placement = PlacementMode.MousePoint,
        };

        string point = $"PDF {request.PdfX:F0}, {request.PdfY:F0}";
        menu.Items.Add(new MenuItem
        {
            Header = request.Measurement != null
                ? $"Measurement AI - {request.Measurement.MType} @ {point}"
                : request.Annotation != null
                    ? $"Markup - {MarkupTitle(request.Annotation)} @ {point}"
                    : $"AI Assist - {_currentPage.Name} @ {point}",
            IsEnabled = false,
        });
        menu.Items.Add(new Separator());

        AddSheetOverlayMenuItems(menu);
        menu.Items.Add(new Separator());

        AddMeasurementClipboardMenuItems(menu, request);
        menu.Items.Add(new Separator());

        if (request.Measurement != null)
        {
            AddMeasurementEditMenuItems(menu, request);
            menu.Items.Add(new Separator());
            AddMeasurementAiMenuItems(menu, request);
        }
        else if (request.Annotation != null)
        {
            AddAnnotationEditMenuItems(menu, request);
            menu.Items.Add(new Separator());
            AddPdfAiMenuItems(menu, request);
        }
        else
        {
            AddPdfAiMenuItems(menu, request);
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Open Project Context", true, OpenProjectContextMarkdown));
        menu.IsOpen = true;
    }

    private void SaveViewportObservation(ViewportContextRequest request, string type, string title, string initialText)
    {
        if (_currentJob == null || _currentPage == null)
            return;

        string? text = ShowMultilineInputDialog(
            $"{title}\nPage: {_currentPage.Name}\nPoint: {request.PdfX:F1}, {request.PdfY:F1}",
            initialText,
            title);
        if (string.IsNullOrWhiteSpace(text))
            return;

        string cropDetails = BuildAiCropDetails(request, type);
        string measurementSummary = request.Measurement != null
            ? FormatMeasurementSummary(request.Measurement)
            : "";
        string details =
            $"{text.Trim()}\n\n" +
            "Context:\n" +
            $"- Page: {_currentPage.Name}\n" +
            $"- PDF point: {request.PdfX:F1}, {request.PdfY:F1}\n" +
            cropDetails;
        if (request.Measurement != null)
            details += $"- Measurement: {measurementSummary}\n";

        var observation = SmartContextStore.AddObservation(_currentJob, _currentPage, type, details);
        if (ShouldQueueAiRequest(type))
        {
            SmartContextStore.AddAiRequest(
                _currentJob,
                _currentPage,
                observation,
                type,
                text.Trim(),
                ExtractAiCropRelativePath(cropDetails),
                measurementSummary);
        }
        TxtStatus.Text = $"Saved {type} {observation.Id} -> {_currentJob.AIContextRoot}";
        LoadObservationsInbox();
    }

    private void SaveAiCropObservation(ViewportContextRequest request)
    {
        if (_currentJob == null || _currentPage == null)
            return;

        string cropDetails = BuildAiCropDetails(request, "crop_context");
        if (string.IsNullOrWhiteSpace(cropDetails))
            return;

        string title = request.Measurement == null ? "AI crop saved" : "Measurement AI crop saved";
        string details =
            $"{title}.\n\n" +
            "Context:\n" +
            $"- Page: {_currentPage.Name}\n" +
            $"- PDF point: {request.PdfX:F1}, {request.PdfY:F1}\n" +
            cropDetails;
        if (request.Measurement != null)
            details += $"- Measurement: {FormatMeasurementSummary(request.Measurement)}\n";

        var observation = SmartContextStore.AddObservation(_currentJob, _currentPage, "crop_context", details);
        TxtStatus.Text = $"Saved AI crop {observation.Id} -> {_currentJob.AIContextRoot}";
        LoadObservationsInbox();
    }

    private void SaveAiMarker(ViewportContextRequest request)
    {
        if (_currentJob == null || _currentPage == null)
            return;

        AiMarkerInput? input = ShowAiMarkerDialog(request);
        if (input == null)
            return;

        string markerType = NormalizeAiMarkerType(input.MarkerType);
        string sampleKind = NormalizeAiMarkerSampleKind(input.SampleKind);
        if (!TrySaveAiCrop(request, $"marker_{markerType}", out string cropPath, out SKRect cropRect, out string error))
        {
            TxtStatus.Text = $"AI marker crop skipped: {error}";
            MessageBox.Show(
                $"Cannot save marker crop:\n{error}",
                "Save AI Marker",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        string measurementSummary = request.Measurement != null
            ? FormatMeasurementSummary(request.Measurement)
            : "";

        var details = new StringBuilder();
        details.AppendLine("AI marker saved.");
        details.AppendLine();
        details.AppendLine("Marker:");
        details.AppendLine($"- Type: {markerType}");
        details.AppendLine($"- Sample kind: {sampleKind}");
        if (!string.IsNullOrWhiteSpace(input.Value))
            details.AppendLine($"- Value: {input.Value.Trim()}");
        if (!string.IsNullOrWhiteSpace(input.Note))
            details.AppendLine($"- Note: {input.Note.Trim()}");
        details.AppendLine();
        details.AppendLine("Context:");
        details.AppendLine($"- Page: {_currentPage.Name}");
        details.AppendLine($"- PDF point: {request.PdfX:F1}, {request.PdfY:F1}");
        details.AppendLine($"- AI crop: {cropPath}");
        details.AppendLine($"- PDF crop: {FormatPdfRect(cropRect)}");
        if (request.Measurement != null)
            details.AppendLine($"- Measurement: {measurementSummary}");

        SmartObservation observation = SmartContextStore.AddObservation(
            _currentJob,
            _currentPage,
            "ai_marker",
            details.ToString());

        SmartAiMarker marker = SmartContextStore.SaveAiMarker(
            _currentJob,
            _currentPage,
            observation,
            markerType,
            sampleKind,
            input.Value,
            input.Note,
            cropPath,
            request.PdfX,
            request.PdfY,
            cropRect.Left,
            cropRect.Top,
            cropRect.Right,
            cropRect.Bottom);

        RefreshAiMarkersOverlay();
        LoadObservationsInbox();
        TxtStatus.Text = $"Saved AI marker {marker.Type} on {_currentPage.Name}.";
    }

    private AiMarkerInput? ShowAiMarkerDialog(ViewportContextRequest request)
    {
        string contextText = $"Page: {_currentPage?.Name ?? ""}   Point: {request.PdfX:F1}, {request.PdfY:F1}";
        return ShowAiMarkerDialog("Save AI Marker", contextText, "", "positive", "", "");
    }

    private AiMarkerInput? ShowAiMarkerDialog(SmartAiMarker marker)
    {
        string contextText = $"Page: {marker.Page}   Point: {marker.PdfPoint.X:F1}, {marker.PdfPoint.Y:F1}";
        return ShowAiMarkerDialog(
            "Edit AI Marker",
            contextText,
            marker.Type,
            marker.SampleKind,
            marker.Value,
            marker.Note);
    }

    private AiMarkerInput? ShowAiMarkerDialog(
        string title,
        string contextText,
        string markerTypeValue,
        string sampleKindValue,
        string value,
        string note)
    {
        var win = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = this,
        };

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock
        {
            Text = contextText,
            Margin = new Thickness(0, 0, 0, 10),
            FontWeight = FontWeights.Normal,
        });

        string markerTypeText = string.IsNullOrWhiteSpace(markerTypeValue) ? AiMarkerTypes[0] : markerTypeValue.Trim();
        panel.Children.Add(new TextBlock { Text = "Marker type", Margin = new Thickness(0, 0, 0, 4) });
        var typeBox = new ComboBox
        {
            ItemsSource = AiMarkerTypes,
            IsEditable = true,
            Text = markerTypeText,
            SelectedItem = AiMarkerTypes.FirstOrDefault(type =>
                string.Equals(type, markerTypeText, StringComparison.OrdinalIgnoreCase)),
            Margin = new Thickness(0, 0, 0, 8),
        };
        typeBox.Text = markerTypeText;
        panel.Children.Add(typeBox);

        string sampleKindText = NormalizeAiMarkerSampleKind(sampleKindValue);
        panel.Children.Add(new TextBlock { Text = "Sample kind", Margin = new Thickness(0, 0, 0, 4) });
        var sampleBox = new ComboBox
        {
            ItemsSource = AiMarkerSampleKinds,
            IsEditable = false,
            SelectedItem = AiMarkerSampleKinds.FirstOrDefault(kind =>
                string.Equals(kind, sampleKindText, StringComparison.OrdinalIgnoreCase)) ?? "positive",
            Margin = new Thickness(0, 0, 0, 8),
        };
        panel.Children.Add(sampleBox);

        panel.Children.Add(new TextBlock { Text = "Value / measurement text", Margin = new Thickness(0, 0, 0, 4) });
        var valueBox = new TextBox { Text = value, Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(valueBox);

        panel.Children.Add(new TextBlock { Text = "Note", Margin = new Thickness(0, 0, 0, 4) });
        var noteBox = new TextBox
        {
            Text = note,
            Height = 76,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        panel.Children.Add(noteBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var ok = new Button { Content = "Save", Width = 78, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 78, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        win.Content = panel;

        AiMarkerInput? result = null;
        ok.Click += (_, _) =>
        {
            string markerType = typeBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(markerType))
            {
                MessageBox.Show("Marker type is required.", title,
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string sampleKind = sampleBox.SelectedItem?.ToString() ?? "positive";
            result = new AiMarkerInput(markerType, sampleKind, valueBox.Text.Trim(), noteBox.Text.Trim());
            win.DialogResult = true;
        };
        win.Loaded += (_, _) => typeBox.Focus();

        return win.ShowDialog() == true ? result : null;
    }

    private static string NormalizeAiMarkerType(string value)
    {
        string normalized = SafeFileNamePart(value).ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "manual_marker" : normalized;
    }

    private static string NormalizeAiMarkerSampleKind(string value)
    {
        if (value.Contains("ignore", StringComparison.OrdinalIgnoreCase))
            return "ignore";
        if (value.Contains("neg", StringComparison.OrdinalIgnoreCase))
            return "negative";
        return "positive";
    }

    private void RefreshAiMarkersOverlay()
    {
        if (_currentJob == null || _currentPage == null)
        {
            _viewport.ClearAiMarkers();
            return;
        }

        try
        {
            var markers = SmartContextStore.LoadAiMarkers(_currentJob)
                .Where(marker => MarkerBelongsToPage(marker, _currentPage, _currentJob))
                .Where(marker => !_hiddenAiMarkerTypes.Contains(marker.Type))
                .ToList();
            _viewport.SetAiMarkers(markers);
        }
        catch
        {
            _viewport.ClearAiMarkers();
        }
    }

    private static bool MarkerBelongsToPage(SmartAiMarker marker, PageInfo page, OurPlaneCoreJob job)
    {
        if (string.Equals(marker.Page, page.Name, StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.IsNullOrWhiteSpace(marker.PageFolder))
            return false;

        string markerFolder = Path.IsPathFullyQualified(marker.PageFolder)
            ? marker.PageFolder
            : Path.Combine(job.RootPath, marker.PageFolder);

        return string.Equals(
            Path.GetFullPath(markerFolder),
            Path.GetFullPath(page.FolderPath),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldQueueAiRequest(string type) =>
        type is "ai_request" or
                "text_read_request" or
                "pending_check" or
                "trace_request" or
                "trace_area_request" or
                "missed_takeoff_check" or
                "measurement_explain_request" or
                "find_similar_request" or
                "find_similar_marker_request" or
                "roof_recognition_request" or
                "measurement_link_request" or
                "crop_bookmark_request" or
                "pdf_sheet_metadata_fallback";

    private string BuildAiCropDetails(ViewportContextRequest request, string type)
    {
        if (!TrySaveAiCrop(request, type, out string relativePath, out SKRect cropRect, out string error))
        {
            if (!string.IsNullOrWhiteSpace(error))
                TxtStatus.Text = $"AI crop skipped: {error}";
            return "";
        }

        return
            $"- AI crop: {relativePath}\n" +
            $"- PDF crop: {FormatPdfRect(cropRect)}\n";
    }

    private bool TrySaveAiCrop(
        ViewportContextRequest request,
        string type,
        out string relativePath,
        out SKRect cropRect,
        out string error)
    {
        relativePath = "";
        cropRect = SKRect.Empty;
        error = "";

        if (_currentJob == null || _currentPage == null)
        {
            error = "No current job/page.";
            return false;
        }

        string cropsRoot = Path.Combine(_currentJob.AIContextRoot, "crops");
        string x = request.PdfX.ToString("F0", CultureInfo.InvariantCulture);
        string y = request.PdfY.ToString("F0", CultureInfo.InvariantCulture);
        string fileName =
            $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{SafeFileNamePart(type)}_{SafeFileNamePart(_currentPage.Name)}_{x}_{y}.png";
        string cropPath = Path.Combine(cropsRoot, fileName);

        bool saved = request.Measurement == null
            ? _viewport.TrySaveContextCrop(request.PdfX, request.PdfY, 240f, cropPath, out cropRect, out error)
            : _viewport.TrySaveMeasurementCrop(request.Measurement, 96f, cropPath, out cropRect, out error);

        if (!saved)
            return false;

        relativePath = Path.GetRelativePath(_currentJob.AIContextRoot, cropPath);
        return true;
    }

    private static string FormatPdfRect(SKRect rect) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "left={0:F1}, top={1:F1}, right={2:F1}, bottom={3:F1}",
            rect.Left,
            rect.Top,
            rect.Right,
            rect.Bottom);

    private static string SafeFileNamePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "context";

        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sb = new StringBuilder(value.Length);
        foreach (char ch in value.Trim())
        {
            if (invalid.Contains(ch))
                sb.Append('_');
            else if (char.IsWhiteSpace(ch))
                sb.Append('_');
            else if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
                sb.Append(ch);
            else
                sb.Append('_');
        }

        string safe = sb.ToString().Trim('_');
        if (safe.Length == 0)
            return "context";
        return safe.Length <= 60 ? safe : safe[..60];
    }

    private static string ExtractAiCropRelativePath(string text)
    {
        foreach (string line in (text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("- AI crop:", StringComparison.OrdinalIgnoreCase))
                continue;

            return trimmed["- AI crop:".Length..].Trim();
        }

        return "";
    }

    private void SuggestTakeoffItemFromContext(ViewportContextRequest request)
    {
        if (_currentJob == null || _currentPage == null)
            return;

        string measurementType = CurrentToolMeasurementType();
        string defaultName = $"{_currentPage.Name} {MeasurementTypeTitle(measurementType)}";
        string? name = ShowInputDialog("Suggested takeoff item name:", defaultName, "Suggest Takeoff Item");
        if (string.IsNullOrWhiteSpace(name))
            return;

        try
        {
            string parentFolder = NewTakeoffItemParentFolder();
            var item = CreateUniqueTakeoffItem(name, "#2196F3", measurementType, parentFolder);
            _takeoffItems.Add(item);
            var parent = FindTakeoffTreeItemByFolder(parentFolder) ?? (ItemsControl)TakeoffsTree;
            var tvi = AddTakeoffTreeItem(item, parent);
            if (parent is TreeViewItem parentTvi)
                parentTvi.IsExpanded = true;
            tvi.IsSelected = true;

            string note =
                $"Suggested takeoff item created: {item.Name}\n\n" +
                "Context:\n" +
                $"- Page: {_currentPage.Name}\n" +
                $"- PDF point: {request.PdfX:F1}, {request.PdfY:F1}\n" +
                BuildAiCropDetails(request, "takeoff_suggestion") +
                $"- Measurement type: {measurementType}";
            SmartContextStore.AddObservation(_currentJob, _currentPage, "takeoff_suggestion", note);
            TxtStatus.Text = $"Created suggested takeoff item: {item.Name}";
        }
        catch (Exception ex)
        {
            ShowOperationError("Suggest Takeoff Item", ex);
        }
    }

    private string FormatMeasurementSummary(Measurement measurement) =>
        $"{measurement.MType}, {measurement.Label(_viewport.ScaleMetersPerPt, _viewport.UnitMode)}, " +
        $"points={measurement.Points.Count}, scale={measurement.ScaleMetersPerPt:G6}, takeoff={measurement.TakeoffFolder}";

    private void OpenProjectContextMarkdown()
    {
        if (_currentJob == null)
            return;

        SmartContextStore.EnsureProjectContext(_currentJob.RootPath, _currentJob.Name);
        string path = Path.Combine(_currentJob.AIContextRoot, "project.md");
        Process.Start(new ProcessStartInfo
        {
            FileName = "notepad.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true,
        });
    }
}
