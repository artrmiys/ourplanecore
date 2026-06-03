using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OurPlaneCore.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void OnLayersChanged(IReadOnlyList<PdfLayer> layers)
    {
        LayersPanel.Children.Clear();
        BtnLayersLoad.IsEnabled = _currentPage != null;
        BtnLayersOn.IsEnabled = layers.Count > 0;
        BtnLayersOff.IsEnabled = layers.Count > 0;
        BtnLayersClearHi.IsEnabled = layers.Count > 0;
        bool hasLayers = layers.Count > 0;
        BtnLayerTraceMode.IsEnabled = hasLayers;
        BtnLayerTraceCycle.IsEnabled = hasLayers;
        BtnLayerTraceApply.IsEnabled = hasLayers && _viewport.CanApplyPdfLayerTrace;
        ViewportLayerTraceBar.Visibility = hasLayers ? Visibility.Visible : Visibility.Collapsed;
        BtnViewportLayerTraceToggle.IsEnabled = hasLayers;
        BtnViewportLayerTraceCycle.IsEnabled = hasLayers;
        RefreshPdfLayerTraceControls();
        if (layers.Count == 0)
        {
            LayersPanel.Children.Add(new TextBlock
            {
                Text       = _viewport.ArePdfLayersLoaded
                    ? "  No PDF layers detected."
                    : "  PDF layers not loaded. Click Load to scan this sheet.",
                Foreground = Brushes.Gray,
                FontSize   = 10,
                Margin     = new Thickness(0, 2, 0, 2),
            });
            return;
        }

        foreach (var layer in layers)
        {
            var row = new DockPanel { Margin = new Thickness(2, 1, 2, 1) };
            row.MouseLeftButtonUp += (_, _) => SelectPdfLayerForTrace(layer);
            var hi = new CheckBox
            {
                Content = "Hi",
                IsChecked = layer.IsHighlighted,
                FontSize = 10,
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = "Highlight this layer",
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(hi, Dock.Right);
            row.Children.Add(hi);

            var cb = new CheckBox
            {
                Content   = layer.Name,
                IsChecked = layer.IsOn,
                FontSize  = 10,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Click to select this layer for Layer Trace; checkbox shows/hides it.",
            };
            cb.Checked   += (_, _) => _viewport.SetLayerVisible(layer.Number, true);
            cb.Unchecked += (_, _) => _viewport.SetLayerVisible(layer.Number, false);
            hi.Checked += (_, _) =>
            {
                SelectPdfLayerForTrace(layer);
                _viewport.SetLayerHighlighted(layer.Number, true);
            };
            hi.Unchecked += (_, _) => _viewport.SetLayerHighlighted(layer.Number, false);
            row.ContextMenu = BuildPdfLayerContextMenu(layer);
            cb.ContextMenu = BuildPdfLayerContextMenu(layer);
            hi.ContextMenu = BuildPdfLayerContextMenu(layer);
            row.Children.Add(cb);
            LayersPanel.Children.Add(row);
        }
    }

    private void SelectPdfLayerForTrace(PdfLayer layer)
    {
        _viewport.SetActivePdfLayerTraceLayer(layer);
        RefreshPdfLayerTraceControls();
    }

    private void RefreshPdfLayerTraceControls()
    {
        _updatingLayerTraceUi = true;
        try
        {
            BtnLayerTraceMode.IsChecked = _viewport.IsPdfLayerTraceEnabled;
            BtnLayerTraceCycle.Content = _viewport.PdfLayerTraceModeTitle;
            BtnLayerTraceApply.IsEnabled = BtnLayerTraceMode.IsEnabled && _viewport.CanApplyPdfLayerTrace;
            BtnViewportLayerTraceToggle.IsChecked = _viewport.IsPdfLayerTraceEnabled;
            BtnViewportLayerTraceToggle.Content = _viewport.IsPdfLayerTraceEnabled ? "Layer Trace" : "Manual";
            BtnViewportLayerTraceCycle.Content = _viewport.PdfLayerTraceModeTitle;
            string layerName = _viewport.ActivePdfLayerTraceLayerName;
            TxtLayerTraceStatus.Text = string.IsNullOrWhiteSpace(layerName)
                ? "Select a layer, then enable Layer Trace."
                : $"{_viewport.PdfLayerTracePhaseTitle}: {layerName}. Mode: {_viewport.PdfLayerTraceModeTitle}.";
        }
        finally
        {
            _updatingLayerTraceUi = false;
        }
    }

    private ContextMenu BuildPdfLayerContextMenu(PdfLayer layer)
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem
        {
            Header = $"PDF Layer: {layer.Name}",
            IsEnabled = false,
        });
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Save Layer Info for AI", _currentJob != null && _currentPage != null, () =>
            SavePdfLayerAiContext(layer, createRequest: false)));
        menu.Items.Add(MakeMenuItem("Queue AI Request for This Layer", _currentJob != null && _currentPage != null, () =>
            SavePdfLayerAiContext(layer, createRequest: true)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Use for Layer Trace", true, () => SelectPdfLayerForTrace(layer)));
        menu.Items.Add(MakeMenuItem("Trace Full Layer to Takeoff", true, () => TracePdfLayer(layer, PdfLayerTraceMode.Full)));
        menu.Items.Add(MakeMenuItem("Trace Nearest Edge to Takeoff", true, () => TracePdfLayer(layer, PdfLayerTraceMode.Edge)));
        menu.Items.Add(MakeMenuItem("Trace Point to Takeoff", true, () => TracePdfLayer(layer, PdfLayerTraceMode.Point)));
        menu.Items.Add(MakeMenuItem("Trace All Edges to Takeoff", true, () => TracePdfLayer(layer, PdfLayerTraceMode.AllEdges)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem(layer.IsOn ? "Hide Layer" : "Show Layer", true, () =>
            _viewport.SetLayerVisible(layer.Number, !layer.IsOn)));
        menu.Items.Add(MakeMenuItem(layer.IsHighlighted ? "Clear Highlight" : "Highlight Layer", true, () =>
            _viewport.SetLayerHighlighted(layer.Number, !layer.IsHighlighted)));
        return menu;
    }

    private void TracePdfLayer(PdfLayer layer, PdfLayerTraceMode mode)
    {
        SelectPdfLayerForTrace(layer);
        _viewport.SetPdfLayerTraceMode(mode);
        _viewport.SetPdfLayerTraceEnabled(true);
        _viewport.ApplyActivePdfLayerTraceFromCurrentPointer();
    }

    private void BtnLayerTraceMode_Click(object sender, RoutedEventArgs e)
    {
        if (_updatingLayerTraceUi)
            return;

        SetPdfLayerTraceEnabledFromUi(BtnLayerTraceMode.IsChecked == true);
    }

    private void BtnLayerTraceCycle_Click(object sender, RoutedEventArgs e)
    {
        CyclePdfLayerTraceModeFromUi();
    }

    private void BtnLayerTraceApply_Click(object sender, RoutedEventArgs e)
    {
        _viewport.ApplyActivePdfLayerTraceFromCurrentPointer();
    }

    private void BtnViewportLayerTraceToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_updatingLayerTraceUi)
            return;

        SetPdfLayerTraceEnabledFromUi(BtnViewportLayerTraceToggle.IsChecked == true);
        _viewport.Focus();
    }

    private void BtnViewportLayerTraceCycle_Click(object sender, RoutedEventArgs e)
    {
        CyclePdfLayerTraceModeFromUi();
        _viewport.Focus();
    }

    private void SetPdfLayerTraceEnabledFromUi(bool enabled)
    {
        _viewport.SetPdfLayerTraceEnabled(enabled);
        RefreshPdfLayerTraceControls();
    }

    private void CyclePdfLayerTraceModeFromUi()
    {
        _viewport.CyclePdfLayerTraceMode();
        RefreshPdfLayerTraceControls();
    }

    private void SavePdfLayerAiContext(PdfLayer layer, bool createRequest)
    {
        if (_currentJob == null || _currentPage == null)
        {
            TxtStatus.Text = "Open a job and page before saving PDF layer context.";
            return;
        }

        try
        {
            string layerText = BuildPdfLayerAiContextText(layer);
            SmartObservation observation = SmartContextStore.AddObservation(
                _currentJob,
                _currentPage,
                createRequest ? "pdf_layer_ai_request" : "pdf_layer_context",
                layerText);

            if (createRequest)
            {
                SmartContextStore.AddAiRequest(
                    _currentJob,
                    _currentPage,
                    observation,
                    "pdf_layer_ai_request",
                    BuildPdfLayerAiPrompt(layer),
                    "",
                    layerText);
            }

            LoadObservationsInbox();
            TxtStatus.Text = createRequest
                ? $"Queued AI layer request for '{layer.Name}'."
                : $"Saved PDF layer context for '{layer.Name}' to AI Inbox.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Save PDF Layer Context", ex);
        }
    }

    private string BuildPdfLayerAiContextText(PdfLayer selectedLayer)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PDF layer context saved for AI review.");
        sb.AppendLine();
        sb.AppendLine($"Page: {_currentPage?.Name}");
        sb.AppendLine($"Selected layer: #{selectedLayer.Number} {selectedLayer.Name}");
        sb.AppendLine($"Selected layer visible: {selectedLayer.IsOn}");
        sb.AppendLine($"Selected layer highlighted: {selectedLayer.IsHighlighted}");
        if (_currentJob != null && _currentPage != null)
        {
            string manifestPath = OurPlaneCoreJobStore.PageLayersJsonPath(_currentPage.FolderPath);
            if (File.Exists(manifestPath))
                sb.AppendLine($"Layer manifest: {Path.GetRelativePath(_currentJob.RootPath, manifestPath)}");
        }

        sb.AppendLine();
        sb.AppendLine("All known layers on this page:");
        IReadOnlyList<PdfLayerInfo> knownLayers = LoadCurrentPageLayerInfos();
        if (knownLayers.Count == 0)
        {
            sb.AppendLine("- none cached yet");
        }
        else
        {
            foreach (PdfLayerInfo layer in knownLayers)
                sb.AppendLine($"- #{layer.Number}: {layer.Name} (visible: {layer.IsOn})");
        }

        sb.AppendLine();
        sb.AppendLine("Use this when a PDF layer name or layer visibility carries takeoff meaning, such as framing, dimensions, alternates, grid, tags, notes, or trade-specific hidden information.");
        return sb.ToString().TrimEnd();
    }

    private string BuildPdfLayerAiPrompt(PdfLayer selectedLayer)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Review this saved PDF layer context for takeoff-relevant information.");
        sb.AppendLine("Focus on what the selected layer likely represents and what it may help detect or verify later.");
        sb.AppendLine("Do not apply measurements automatically. Return reviewable notes/actions only.");
        sb.AppendLine();
        sb.AppendLine(BuildPdfLayerAiContextText(selectedLayer));
        return sb.ToString().TrimEnd();
    }

    private IReadOnlyList<PdfLayerInfo> LoadCurrentPageLayerInfos()
    {
        if (_currentPage == null)
            return [];

        PageLayerManifest? manifest = OurPlaneCoreJobStore.ReadPageLayerManifest(_currentPage.FolderPath);
        if (manifest?.Layers.Count > 0)
            return manifest.Layers;

        return _currentPage.PdfLayers;
    }

    private void OnPdfLayersDiscovered(IReadOnlyList<PdfLayerInfo> layers)
    {
        if (_currentPage == null || _currentPage.PdfLayersCached)
            return;

        try
        {
            OurPlaneCoreJobStore.SavePageLayerCache(_currentPage.FolderPath, layers);
            if (layers.Count > 0)
                TxtStatus.Text = $"Cached {layers.Count} visible PDF layer(s) and wrote layers.json for this page.";
        }
        catch
        {
            // Layer cache is an optimization; rendering should never depend on saving it.
        }
    }

    private void BtnLayersOn_Click(object sender, RoutedEventArgs e)
    {
        _viewport.SetAllLayers(true);
    }

    private void BtnLayersLoad_Click(object sender, RoutedEventArgs e)
    {
        _viewport.DiscoverPdfLayersOnDemand();
    }

    private void BtnLayersOff_Click(object sender, RoutedEventArgs e)
    {
        _viewport.SetAllLayers(false);
    }

    private void BtnLayersClearHi_Click(object sender, RoutedEventArgs e)
    {
        _viewport.ClearLayerHighlights();
    }
}
