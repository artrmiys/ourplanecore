using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

// Offline "count similar symbols": box one symbol -> SimilarSymbolMatcher
// finds every look-alike on the page raster -> the matches become ordinary
// count measurements. Optional checkbox queues an online AI double-check
// through the regular AI Inbox pipeline.
public partial class MainWindow
{
    private void BtnSimilarCount_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null || _currentPage == null)
        {
            TxtStatus.Text = "Count similar: open a job and a sheet first.";
            return;
        }

        _viewport.BeginSimilarCountSelection();
    }

    private void OnSimilarCountSelectionCompleted(ViewportSimilarCountRequest request)
    {
        if (_currentJob == null || _currentPage == null)
            return;

        if (!_viewport.TryCreateSimilarCountSession(
                request.PdfRect, out SimilarSymbolMatchSession? session, out float bitmapScale, out string error) ||
            session == null)
        {
            TxtStatus.Text = $"Count similar: {error}";
            MessageBox.Show($"Count similar symbols:\n{error}", "Count Similar",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var lastCenters = new List<SKPoint>();
        async Task<int> ScanAsync(float threshold, bool rotations, bool mirrored, CancellationToken cancellationToken)
        {
            List<SimilarSymbolMatch> matches = await Task.Run(
                () => session.FindMatches(threshold, rotations, mirrored, cancellationToken),
                cancellationToken);
            var centers = matches
                .Select(match => new SKPoint(match.CenterX / bitmapScale, match.CenterY / bitmapScale))
                .ToList();
            lastCenters.Clear();
            lastCenters.AddRange(centers);
            _viewport.SetSimilarCountPreview(centers);
            return centers.Count;
        }

        var dialog = new SimilarCountDialog(
            ScanAsync,
            (float)_settings.SimilarCountThreshold,
            _settings.SimilarCountRotations,
            _settings.SimilarCountMirrored,
            aiAvailable: !string.IsNullOrWhiteSpace(ReadOpenAiApiKey()))
        {
            Owner = this,
        };
        bool accepted = dialog.ShowDialog() == true;
        _viewport.SetSimilarCountPreview(null);
        if (!accepted)
        {
            TxtStatus.Text = "Count similar cancelled.";
            return;
        }

        _settings.SimilarCountThreshold = dialog.Threshold;
        _settings.SimilarCountRotations = dialog.IncludeRotations;
        _settings.SimilarCountMirrored = dialog.IncludeMirrored;
        SaveAppSettings();

        if (lastCenters.Count == 0)
        {
            TxtStatus.Text = "Count similar: nothing to add.";
            return;
        }

        AddSimilarCountMeasurements(request, lastCenters);
        if (dialog.QueueAiDoubleCheck)
            QueueSimilarCountAiRequest(request, lastCenters.Count);
    }

    private void AddSimilarCountMeasurements(ViewportSimilarCountRequest request, IReadOnlyList<SKPoint> centers)
    {
        TakeoffItem? item =
            _activeItem != null &&
            OurPlaneCoreJobStore.NormalizeMeasurementType(_activeItem.MeasurementType) == "point"
                ? _activeItem
                : null;
        if (item == null)
        {
            string parent = NewTakeoffItemParentFolderForUserCreate();
            TakeoffItem created = CreateUniqueTakeoffItem(
                "Similar Count",
                RandomTakeoffColor(_viewport.ActiveColor),
                "point",
                parent);
            ApplyNewCountSymbolToItemIfNeeded(created, "point");
            LoadTakeoffsForJob();
            item = _takeoffItems.FirstOrDefault(t =>
                string.Equals(t.FolderPath, created.FolderPath, StringComparison.OrdinalIgnoreCase)) ?? created;
            _activeItem = item;
            _viewport.ActiveColor = item.Color;
            _viewport.ActiveTakeoffFolder = item.FolderPath;
            _viewport.ActiveCountSymbol = item.CountSymbol;
            SelectTakeoffNodeByFolder(item.FolderPath);
        }

        var generated = new List<Measurement>(centers.Count);
        foreach (SKPoint center in centers)
        {
            generated.Add(new Measurement
            {
                MType = "point",
                Points = [center],
                Color = item.Color,
                CountSymbol = CountDisplaySymbol.Normalize(item.CountSymbol),
                PageFolder = request.PageFolder,
                TakeoffFolder = item.FolderPath,
                ScaleMetersPerPt = _viewport.ScaleMetersPerPt,
            });
        }

        foreach (Measurement measurement in generated)
            item.Measurements.Add(measurement);
        OurPlaneCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        _viewport.AddGeneratedMeasurements(generated);
        _viewport.SelectMeasurements(generated);
        RefreshTreeItem(item);
        QueueTakeoffAutosave(item);
        using (UsePageMeasurementLookup())
        {
            RefreshTakeoffRowVisualsForItems(new[] { item });
            RefreshPageTakeoffIndicatorsForFolder(request.PageFolder);
            RefreshSheetLegend();
        }
        UpdateTotalDisplay();
        TxtStatus.Text = $"Count similar: added {generated.Count} marker(s) to {item.Name}. They stay selected for review.";
    }

    private void QueueSimilarCountAiRequest(ViewportSimilarCountRequest request, int offlineCount)
    {
        if (_currentJob == null || _currentPage == null)
            return;

        try
        {
            var contextRequest = new ViewportContextRequest(
                0,
                0,
                request.PdfRect.MidX,
                request.PdfRect.MidY,
                request.PageFolder,
                null);
            if (!TrySaveAiCrop(contextRequest, "similar_count", out string cropPath, out SKRect cropRect, out string error,
                    request.PdfRect))
            {
                TxtStatus.Text = $"AI double-check skipped: {error}";
                return;
            }

            string prompt =
                "The attached crop shows ONE instance of a plan symbol. The app's offline matcher counted " +
                offlineCount.ToString(CultureInfo.InvariantCulture) +
                $" occurrences of this symbol on sheet '{_currentPage.Name}'. " +
                "Describe what the symbol most likely is and note anything that could make this count unreliable " +
                "(similar-looking symbols, rotated/mirrored variants, legend entries that should be excluded).";
            string details =
                "Similar-count AI double-check requested." + Environment.NewLine + Environment.NewLine +
                "Context:" + Environment.NewLine +
                $"- Page: {_currentPage.Name}" + Environment.NewLine +
                $"- Offline match count: {offlineCount}" + Environment.NewLine +
                $"- AI crop: {cropPath}" + Environment.NewLine +
                $"- PDF crop: {FormatPdfRect(cropRect)}" + Environment.NewLine;

            SmartObservation observation = SmartContextStore.AddObservation(
                _currentJob, _currentPage, "similar_count_request", details);
            SmartContextStore.AddAiRequest(
                _currentJob, _currentPage, observation, "similar_count_request", prompt, cropPath, "");
            LoadObservationsInbox();
            TxtStatus.Text = $"Count similar: {offlineCount} marker(s) added; AI double-check queued in the AI Inbox.";
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, "Similar count AI request failed to queue.");
            TxtStatus.Text = $"AI double-check skipped: {ex.Message}";
        }
    }
}
