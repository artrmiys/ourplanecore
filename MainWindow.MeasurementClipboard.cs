using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using OurPlaneCore.Controls;
using SkiaSharp;

namespace OurPlaneCore;

public partial class MainWindow
{
    // ── Measurement callbacks ─────────────────────────────────────────────────

    private void CopyMeasurementsToClipboard(IReadOnlyList<Measurement> measurements)
    {
        var unique = measurements
            .Where(m => m != null)
            .Distinct()
            .ToList();
        if (unique.Count == 0)
        {
            TxtStatus.Text = "No measurements selected to copy.";
            return;
        }

        var entries = new List<MeasurementClipboardEntry>();
        foreach (Measurement measurement in unique)
        {
            TakeoffItem? item = FindTakeoffItemForMeasurement(measurement);
            entries.Add(new MeasurementClipboardEntry(
                OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType),
                measurement.Name,
                measurement.Notes,
                measurement.Color,
                measurement.Points.Select(p => new SKPoint(p.X, p.Y)).ToList(),
                measurement.Holes
                    .Select(hole => (IReadOnlyList<SKPoint>)hole.Select(p => new SKPoint(p.X, p.Y)).ToList())
                    .ToList(),
                measurement.PageFolder,
                measurement.ScaleMetersPerPt,
                item?.FolderPath ?? measurement.TakeoffFolder,
                item?.Name ?? "",
                item?.Color ?? measurement.Color,
                item?.UnitPrice ?? 0,
                item?.Notes ?? ""));
        }

        _measurementClipboard = new MeasurementClipboard(entries);
        TxtStatus.Text = $"Copied {entries.Count} measurement(s). Open another sheet and press Ctrl+V or right-click Paste.";
    }

    private void PasteMeasurementsFromClipboard(SKPoint? pasteAtPdf)
    {
        if (_currentJob == null || _currentPage == null)
        {
            TxtStatus.Text = "Open a job and sheet before pasting measurements.";
            return;
        }

        if (_measurementClipboard == null || _measurementClipboard.Entries.Count == 0)
        {
            TxtStatus.Text = "No copied measurements to paste.";
            return;
        }

        if (!ConfirmMeasurementPasteScale(_measurementClipboard))
            return;

        MeasurementPasteMode? pasteMode = PromptMeasurementPasteMode(_measurementClipboard.Entries.Count);
        if (pasteMode == null)
            return;

        try
        {
            PdfViewport.ViewState viewBeforePaste = _viewport.CaptureViewState();
            var pasted = new List<Measurement>();
            var pastedNodes = new List<TakeoffMeasurementNode>();
            var changedItems = new HashSet<TakeoffItem>();
            var createdTargets = new Dictionary<string, TakeoffItem>(StringComparer.OrdinalIgnoreCase);
            SKPoint pasteOffset = CalculateMeasurementPasteOffset(_measurementClipboard.Entries, pasteAtPdf);

            foreach (MeasurementClipboardEntry entry in _measurementClipboard.Entries)
            {
                TakeoffItem target = ResolveMeasurementPasteTarget(entry, pasteMode.Value, createdTargets);
                EnsureTakeoffItemFolder(target);

                Measurement measurement = CloneClipboardMeasurement(entry, target, pasteOffset);
                target.Measurements.Add(measurement);
                pasted.Add(measurement);
                pastedNodes.Add(new TakeoffMeasurementNode(target, measurement));
                changedItems.Add(target);
            }

            bool previousSuppressFocus = _suppressCanvasFocusFromTakeoffSelection;
            _suppressCanvasFocusFromTakeoffSelection = true;
            try
            {
                foreach (TakeoffItem item in changedItems)
                {
                    OurPlaneCoreJobStore.SaveTakeoffItem(item);
                    RefreshTreeItem(item);
                }
            }
            finally
            {
                _suppressCanvasFocusFromTakeoffSelection = previousSuppressFocus;
            }

            _viewport.SetMeasurements(_takeoffItems.SelectMany(item => item.Measurements));
            _viewport.RestoreViewState(viewBeforePaste);
            SelectTakeoffSectionNodesSilently(pastedNodes);
            SelectTakeoffSectionMeasurementsOnCanvas(pastedNodes);
            RefreshEstimateTable();
            RefreshPagesTakeoffIndicators();
            ApplyTakeoffPageHighlights();
            RefreshSheetLegend();
            UpdateTotalDisplay();

            string modeLabel = pasteMode.Value == MeasurementPasteMode.SameTakeoffs
                ? "same takeoff item(s)"
                : "new takeoff item(s)";
            TxtStatus.Text = $"Pasted {pasted.Count} measurement(s) to {_currentPage.Name} into {modeLabel}.";
        }
        catch (Exception ex)
        {
            ShowOperationError("Paste Measurements", ex);
        }
    }

    private void PasteMeasurementsFromClipboard() =>
        PasteMeasurementsFromClipboard(null);

    private bool ConfirmMeasurementPasteScale(MeasurementClipboard clipboard)
    {
        var scaledEntries = clipboard.Entries
            .Where(entry => MeasurementTypeRequiresScale(entry.MeasurementType))
            .ToList();
        if (scaledEntries.Count == 0 || _currentPage?.ScaleMetersPerPt > 0)
            return true;

        if (scaledEntries.Any(entry => entry.ScaleMetersPerPt <= 0))
        {
            MessageBox.Show(
                "Set the active sheet scale before pasting Line or Area measurements. The copied measurements do not have a saved scale to reuse.",
                "Paste Measurements",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        return MessageBox.Show(
            "The active sheet has no scale. Pasted Line/Area measurements will keep the copied measurement scale.\n\nContinue?",
            "Paste Measurements",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private static bool MeasurementTypeRequiresScale(string measurementType)
    {
        string normalized = OurPlaneCoreJobStore.NormalizeMeasurementType(measurementType);
        return normalized is "line" or "area";
    }

    private MeasurementPasteMode? PromptMeasurementPasteMode(int count)
    {
        MessageBoxResult result = MessageBox.Show(
            $"Paste {count} copied measurement(s) to the active sheet?\n\n" +
            "Yes = use the same takeoff items/values.\n" +
            "No = create new copied takeoff items.\n" +
            "Cancel = do nothing.",
            "Paste Measurements",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return result switch
        {
            MessageBoxResult.Yes => MeasurementPasteMode.SameTakeoffs,
            MessageBoxResult.No => MeasurementPasteMode.NewTakeoffs,
            _ => null,
        };
    }

    private TakeoffItem ResolveMeasurementPasteTarget(
        MeasurementClipboardEntry entry,
        MeasurementPasteMode mode,
        Dictionary<string, TakeoffItem> createdTargets)
    {
        string measurementType = OurPlaneCoreJobStore.NormalizeMeasurementType(entry.MeasurementType);
        if (mode == MeasurementPasteMode.SameTakeoffs)
        {
            TakeoffItem? sourceItem = FindTakeoffItemByFolder(entry.SourceTakeoffFolder, measurementType);
            if (sourceItem != null)
                return sourceItem;
        }

        string key = MeasurementClipboardTargetKey(entry);
        if (createdTargets.TryGetValue(key, out TakeoffItem? created))
            return created;

        string baseName = string.IsNullOrWhiteSpace(entry.SourceTakeoffName)
            ? $"{MeasurementTypeTitle(measurementType)} Paste"
            : mode == MeasurementPasteMode.NewTakeoffs
                ? $"{entry.SourceTakeoffName} Copy"
                : entry.SourceTakeoffName;
        string color = IsValidWpfColor(entry.SourceTakeoffColor)
            ? entry.SourceTakeoffColor
            : entry.MeasurementColor;
        var target = CreateUniqueTakeoffItem(baseName, color, measurementType, NewTakeoffItemParentFolder());
        target.UnitPrice = entry.SourceTakeoffUnitPrice;
        target.Notes = entry.SourceTakeoffNotes;
        _takeoffItems.Add(target);

        ItemsControl parent = FindTakeoffTreeItemByFolder(Path.GetDirectoryName(target.FolderPath) ?? "") ?? (ItemsControl)TakeoffsTree;
        AddTakeoffTreeItem(target, parent);
        if (parent is TreeViewItem parentTvi)
            parentTvi.IsExpanded = true;

        createdTargets[key] = target;
        return target;
    }

    private Measurement CloneClipboardMeasurement(MeasurementClipboardEntry entry, TakeoffItem target, SKPoint pasteOffset)
    {
        double scale = _currentPage?.ScaleMetersPerPt > 0
            ? _currentPage.ScaleMetersPerPt
            : entry.ScaleMetersPerPt;

        return new Measurement
        {
            MType = OurPlaneCoreJobStore.NormalizeMeasurementType(entry.MeasurementType),
            Name = entry.MeasurementName,
            Notes = entry.MeasurementNotes,
            Color = target.Color,
            Points = entry.Points.Select(p => new SKPoint(p.X + pasteOffset.X, p.Y + pasteOffset.Y)).ToList(),
            Holes = entry.Holes
                .Select(hole => hole.Select(p => new SKPoint(p.X + pasteOffset.X, p.Y + pasteOffset.Y)).ToList())
                .Where(hole => hole.Count >= 3)
                .ToList(),
            PageFolder = _currentPage?.FolderPath ?? "",
            TakeoffFolder = target.FolderPath,
            ScaleMetersPerPt = scale,
        };
    }

    private static SKPoint CalculateMeasurementPasteOffset(
        IReadOnlyList<MeasurementClipboardEntry> entries,
        SKPoint? pasteAtPdf)
    {
        if (!pasteAtPdf.HasValue || !TryGetClipboardBounds(entries, out SKRect bounds))
            return new SKPoint(0, 0);

        var sourceCenter = new SKPoint(
            (bounds.Left + bounds.Right) / 2f,
            (bounds.Top + bounds.Bottom) / 2f);
        return new SKPoint(
            pasteAtPdf.Value.X - sourceCenter.X,
            pasteAtPdf.Value.Y - sourceCenter.Y);
    }

    private static bool TryGetClipboardBounds(
        IReadOnlyList<MeasurementClipboardEntry> entries,
        out SKRect bounds)
    {
        bounds = SKRect.Empty;
        bool hasPoint = false;
        float left = 0;
        float top = 0;
        float right = 0;
        float bottom = 0;

        foreach (MeasurementClipboardEntry entry in entries)
        {
            foreach (SKPoint point in MeasurementClipboardPoints(entry))
            {
                if (!hasPoint)
                {
                    left = right = point.X;
                    top = bottom = point.Y;
                    hasPoint = true;
                    continue;
                }

                left = Math.Min(left, point.X);
                top = Math.Min(top, point.Y);
                right = Math.Max(right, point.X);
                bottom = Math.Max(bottom, point.Y);
            }
        }

        if (!hasPoint)
            return false;

        bounds = new SKRect(left, top, right, bottom);
        return true;
    }

    private static IEnumerable<SKPoint> MeasurementClipboardPoints(MeasurementClipboardEntry entry)
    {
        foreach (SKPoint point in entry.Points)
            yield return point;
        foreach (var hole in entry.Holes)
            foreach (SKPoint point in hole)
                yield return point;
    }

    private static string MeasurementClipboardTargetKey(MeasurementClipboardEntry entry)
    {
        string source = string.IsNullOrWhiteSpace(entry.SourceTakeoffFolder)
            ? $"{entry.SourceTakeoffName}|{entry.MeasurementType}|{entry.SourceTakeoffColor}"
            : entry.SourceTakeoffFolder;
        return source.Trim();
    }

    private TakeoffItem? FindTakeoffItemForMeasurement(Measurement measurement)
    {
        TakeoffItem? item = _takeoffItems.FirstOrDefault(i => i.Measurements.Contains(measurement));
        if (item != null)
            return item;

        return FindTakeoffItemByFolder(measurement.TakeoffFolder, measurement.MType);
    }

    private TakeoffItem? FindTakeoffItemByFolder(string? folderPath, string? measurementType = null)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return null;

        string normalizedType = string.IsNullOrWhiteSpace(measurementType)
            ? ""
            : OurPlaneCoreJobStore.NormalizeMeasurementType(measurementType);

        return _takeoffItems.FirstOrDefault(item =>
            string.Equals(item.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(normalizedType) ||
             OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == normalizedType));
    }

    private void QueueTakeoffAutosave(TakeoffItem item)
    {
        if (_currentJob == null)
            return;

        _pendingTakeoffAutosaves.Add(item);
        _takeoffAutosaveTimer.Stop();
        _takeoffAutosaveTimer.Start();
    }

    private void FlushTakeoffAutosaves()
    {
        _takeoffAutosaveTimer.Stop();
        if (_pendingTakeoffAutosaves.Count == 0)
            return;

        var pending = _pendingTakeoffAutosaves.ToList();
        _pendingTakeoffAutosaves.Clear();
        foreach (var item in pending)
            PersistTakeoffItemQuietly(item);
    }

    internal void FlushPendingAutosave() =>
        FlushTakeoffAutosaves();

    private void PersistTakeoffItemQuietly(TakeoffItem item)
    {
        if (_currentJob == null)
            return;

        try
        {
            EnsureTakeoffItemFolder(item);
            OurPlaneCoreJobStore.SaveTakeoffItem(item);
        }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Autosave failed for {item.FolderPath}");
            TxtStatus.Text = $"Autosave skipped: {ex.Message}";
        }
    }
}
