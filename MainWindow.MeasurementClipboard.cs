using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using OurPlanCore.Controls;
using SkiaSharp;

namespace OurPlanCore;

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

        _viewport.MarkMeasurementClipboardCurrent();
        var itemByMeasurement = BuildTakeoffItemByMeasurementLookup();
        var entries = new List<MeasurementClipboardEntry>();
        foreach (Measurement measurement in unique)
        {
            itemByMeasurement.TryGetValue(measurement, out TakeoffItem? item);
            item ??= FindTakeoffItemByFolder(measurement.TakeoffFolder, measurement.MType);
            entries.Add(new MeasurementClipboardEntry(
                OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType),
                measurement.Name,
                measurement.Notes,
                measurement.Color,
                measurement.CountSymbol,
                measurement.Points.Select(p => new SKPoint(p.X, p.Y)).ToList(),
                measurement.Holes
                    .Select(hole => (IReadOnlyList<SKPoint>)hole.Select(p => new SKPoint(p.X, p.Y)).ToList())
                    .ToList(),
                measurement.PageFolder,
                measurement.ScaleMetersPerPt,
                item?.FolderPath ?? measurement.TakeoffFolder,
                item?.Name ?? "",
                item?.Color ?? measurement.Color,
                item?.CountSymbol ?? "",
                item?.UnitPrice ?? 0,
                item?.Notes ?? "",
                CaptureMeasurementJoistClipboard(measurement),
                CaptureTakeoffJoistClipboard(item, measurement)));
        }

        _measurementClipboard = new MeasurementClipboard(entries);
        TxtStatus.Text = $"Copied {entries.Count} measurement(s). Paste uses the copied set's top-left corner as the cursor anchor.";
    }

    private void PasteMeasurementsFromClipboard(SKPoint? pasteAtPdf) =>
        PasteMeasurementsFromClipboardInto(_viewport, _currentPage, pasteAtPdf);

    // Paste targets the sheet the request came from: the main viewport pastes
    // to the current page, a detached window pastes to ITS page.
    private void PasteMeasurementsFromClipboardInto(PdfViewport viewport, PageInfo? page, SKPoint? pasteAtPdf)
    {
        if (_currentJob == null || page == null)
        {
            viewport.CancelPendingMixedCutRegionPaste();
            TxtStatus.Text = "Open a job and sheet before pasting measurements.";
            return;
        }

        if (_measurementClipboard == null || _measurementClipboard.Entries.Count == 0)
        {
            viewport.CancelPendingMixedCutRegionPaste();
            TxtStatus.Text = "No copied measurements to paste.";
            return;
        }

        if (!ConfirmMeasurementPasteScale(_measurementClipboard, page))
        {
            viewport.CancelPendingMixedCutRegionPaste();
            return;
        }

        SKPoint pasteOffset = CalculateMeasurementPasteOffset(viewport, _measurementClipboard.Entries, pasteAtPdf);
        if (!viewport.TryPreflightPendingMixedCutRegionPaste(pasteOffset, out string preflightFailure))
        {
            TxtStatus.Text = preflightFailure;
            return;
        }
        MeasurementPasteMode? pasteMode = PromptMeasurementPasteMode(_measurementClipboard.Entries.Count);
        if (pasteMode == null)
        {
            viewport.CancelPendingMixedCutRegionPaste();
            return;
        }
        if (!viewport.ValidatePendingMixedCutRegionPasteReservation(out string reservationFailure))
        {
            TxtStatus.Text = reservationFailure;
            return;
        }

        PdfViewport.ViewState viewBeforePaste = viewport.CaptureViewState();
        var pasted = new List<Measurement>();
        var pastedNodes = new List<TakeoffMeasurementNode>();
        var changedItems = new HashSet<TakeoffItem>();
        var createdTargets = new Dictionary<string, TakeoffItem>(StringComparer.OrdinalIgnoreCase);
        bool pasteCommitted = false;
        try
        {
            foreach (MeasurementClipboardEntry entry in _measurementClipboard.Entries)
            {
                TakeoffItem target = ResolveMeasurementPasteTarget(entry, pasteMode.Value, createdTargets);
                EnsureTakeoffItemFolder(target);

                Measurement measurement = CloneClipboardMeasurement(entry, target, pasteOffset, page);
                target.Measurements.Add(measurement);
                pasted.Add(measurement);
                pastedNodes.Add(new TakeoffMeasurementNode(target, measurement));
                changedItems.Add(target);
            }

            viewport.SetMeasurements(_takeoffItems.SelectMany(item => item.Measurements), clearUndoStack: false);
            bool mixedPasteHandled = viewport.CompletePendingMixedCutRegionPaste(
                pasted,
                out int pastedCutouts,
                out string cutoutStatus);
            if (!mixedPasteHandled)
                viewport.RegisterAddedMeasurementsUndo(pasted, $"remove pasted {pasted.Count} measurement(s)");
            pasteCommitted = true;

            QueueTakeoffAutosave(changedItems);
            bool previousSuppressFocus = _suppressCanvasFocusFromTakeoffSelection;
            _suppressCanvasFocusFromTakeoffSelection = true;
            try
            {
                foreach (TakeoffItem item in changedItems)
                    RefreshTreeItem(item);
            }
            finally
            {
                _suppressCanvasFocusFromTakeoffSelection = previousSuppressFocus;
            }

            viewport.RestoreViewState(viewBeforePaste);
            RefreshOtherViewportsAfterPaste(viewport);
            SelectTakeoffSectionNodesSilently(pastedNodes);
            if (ReferenceEquals(viewport, _viewport))
                SelectTakeoffSectionMeasurementsOnCanvas(pastedNodes);
            else
                SelectMeasurementsInDetachedSheetsByPage(pasted);
            if (mixedPasteHandled)
                viewport.RestoreCompletedMixedPasteSelection(pasted);
            using (UsePageMeasurementLookup())
            {
                RefreshPageTakeoffIndicatorsForFolder(page.FolderPath);
                ApplyTakeoffPageHighlights();
                RefreshSheetLegend();
            }
            UpdateTotalDisplay();

            string modeLabel = pasteMode.Value == MeasurementPasteMode.SameTakeoffs
                ? "same takeoff item(s)"
                : "new takeoff item(s)";
            TxtStatus.Text = $"Pasted {pasted.Count} measurement(s) to {page.Name} into {modeLabel}." +
                             (mixedPasteHandled
                                 ? cutoutStatus
                                 : pastedCutouts > 0
                                     ? $" Attached {pastedCutouts} cutout(s)."
                                     : "");
        }
        catch (Exception ex)
        {
            Exception reported = ex;
            if (!pasteCommitted)
            {
                try
                {
                    Exception? rollbackFailure = RollBackUncommittedMeasurementPaste(
                        viewport,
                        pastedNodes,
                        createdTargets.Values,
                        viewBeforePaste);
                    if (rollbackFailure != null)
                    {
                        reported = new AggregateException(
                            "Paste failed and one or more provisional takeoff folders could not be moved to recovery storage.",
                            ex,
                            rollbackFailure);
                    }
                }
                catch (Exception rollbackEx)
                {
                    reported = new AggregateException(
                        "Paste failed and its in-memory rollback also encountered an error.",
                        ex,
                        rollbackEx);
                }
            }
            viewport.CancelPendingMixedCutRegionPaste();
            ShowOperationError("Paste Measurements", reported);
        }
    }

    private void PasteMeasurementsFromClipboard() =>
        PasteMeasurementsFromClipboard(null);

    // A paste changes the shared measurement model, so every other viewport
    // (main + all detached sheets) must re-read it.
    private void RefreshOtherViewportsAfterPaste(PdfViewport sourceViewport)
    {
        if (!ReferenceEquals(sourceViewport, _viewport))
        {
            _viewport.SetMeasurements(
                _takeoffItems.SelectMany(item => item.Measurements),
                clearUndoStack: false);
        }

        UnitMode unitMode = _settings.UnitMode == UnitMode.Metric.ToString()
            ? UnitMode.Metric
            : UnitMode.Imperial;
        foreach (DetachedSheetWindow window in _detachedSheetWindows.ToList())
        {
            if (!ReferenceEquals(window.Viewport, sourceViewport))
                RefreshDetachedTakeoffDisplay(window, unitMode);
        }
    }

    private bool ConfirmMeasurementPasteScale(MeasurementClipboard clipboard, PageInfo page)
    {
        var scaledEntries = clipboard.Entries
            .Where(entry => MeasurementTypeRequiresScale(entry.MeasurementType))
            .ToList();
        if (scaledEntries.Count == 0 || page.ScaleMetersPerPt > 0)
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
        string normalized = OurPlanCoreJobStore.NormalizeMeasurementType(measurementType);
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
        string measurementType = OurPlanCoreJobStore.NormalizeMeasurementType(entry.MeasurementType);
        if (mode == MeasurementPasteMode.SameTakeoffs)
        {
            TakeoffItem? sourceItem = FindTakeoffItemByFolder(entry.SourceTakeoffFolder, measurementType);
            if (sourceItem != null)
                return sourceItem;
        }

        string key = MeasurementClipboardTargetKey(entry);
        if (createdTargets.TryGetValue(key, out TakeoffItem? created))
            return created;

        string baseName = MeasurementPasteTargetDisplayName(entry.SourceTakeoffName, measurementType);
        string color = IsValidWpfColor(entry.SourceTakeoffColor)
            ? entry.SourceTakeoffColor
            : entry.MeasurementColor;
        var target = CreateUniqueTakeoffItem(baseName, color, measurementType, NewTakeoffItemParentFolder());
        if (measurementType == "point")
            target.CountSymbol = MeasurementClipboardTakeoffCountSymbol(entry);
        target.UnitPrice = entry.SourceTakeoffUnitPrice;
        target.Notes = entry.SourceTakeoffNotes;
        ApplyTakeoffJoistClipboard(target, entry.SourceTakeoffJoist, measurementType);
        createdTargets[key] = target;
        _takeoffItems.Add(target);

        ItemsControl parent = FindTakeoffTreeItemByFolder(Path.GetDirectoryName(target.FolderPath) ?? "") ?? (ItemsControl)TakeoffsTree;
        AddTakeoffTreeItem(target, parent);
        if (parent is TreeViewItem parentTvi)
            parentTvi.IsExpanded = true;

        return target;
    }

    private Exception? RollBackUncommittedMeasurementPaste(
        PdfViewport viewport,
        IReadOnlyList<TakeoffMeasurementNode> pastedNodes,
        IEnumerable<TakeoffItem> createdTargets,
        PdfViewport.ViewState viewBeforePaste)
    {
        foreach (TakeoffMeasurementNode node in pastedNodes)
            node.Item.Measurements.Remove(node.Measurement);

        var created = createdTargets.Distinct().ToHashSet();
        Exception? folderRollbackFailure = MoveUncommittedTakeoffFoldersToRecovery(created);
        foreach (TakeoffItem item in created)
        {
            if (FindTakeoffTreeItem(item) is { } treeItem)
                RemoveTreeItem(treeItem);
            _takeoffItems.Remove(item);
        }

        foreach (TakeoffItem item in pastedNodes.Select(node => node.Item).Distinct())
        {
            if (!created.Contains(item))
                RefreshTreeItem(item);
        }

        viewport.SetMeasurements(_takeoffItems.SelectMany(item => item.Measurements), clearUndoStack: false);
        viewport.RestoreViewState(viewBeforePaste);
        return folderRollbackFailure;
    }

    private Exception? MoveUncommittedTakeoffFoldersToRecovery(IReadOnlyCollection<TakeoffItem> created)
    {
        if (created.Count == 0)
            return null;
        if (_currentJob == null)
            return new InvalidOperationException("The job closed before provisional takeoff folders could be recovered.");

        string trashRoot;
        try
        {
            trashRoot = CreateTakeoffUndoTrashRoot(_currentJob);
        }
        catch (Exception ex)
        {
            return ex;
        }

        var failures = new List<Exception>();
        int index = 0;
        foreach (TakeoffItem item in created)
        {
            string sourcePath = NormalizePath(item.FolderPath);
            try
            {
                if (!Directory.Exists(sourcePath))
                    continue;
                if (!OurPlanCoreJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, sourcePath) ||
                    string.Equals(
                        sourcePath,
                        NormalizePath(_currentJob.TakeoffsRoot),
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Refusing to recover provisional takeoff folder outside the active job: {sourcePath}");
                }

                string trashPath = UniqueTakeoffUndoTrashPath(trashRoot, sourcePath, index++);
                JobWriteAccess.Demand(sourcePath, "roll back a provisional takeoff item");
                JobWriteAccess.Demand(trashPath, "recover a provisional takeoff item");
                Directory.Move(sourcePath, trashPath);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        return failures.Count switch
        {
            0 => null,
            1 => failures[0],
            _ => new AggregateException("Several provisional takeoff folders could not be recovered.", failures),
        };
    }

    private static string MeasurementPasteTargetDisplayName(string sourceTakeoffName, string measurementType) =>
        string.IsNullOrWhiteSpace(sourceTakeoffName)
            ? MeasurementTypeTitle(measurementType)
            : sourceTakeoffName.Trim();

    private Measurement CloneClipboardMeasurement(
        MeasurementClipboardEntry entry,
        TakeoffItem target,
        SKPoint pasteOffset,
        PageInfo page)
    {
        string measurementType = OurPlanCoreJobStore.NormalizeMeasurementType(entry.MeasurementType);
        double scale = page.ScaleMetersPerPt > 0
            ? page.ScaleMetersPerPt
            : entry.ScaleMetersPerPt;

        return new Measurement
        {
            MType = measurementType,
            Name = entry.MeasurementName,
            Notes = entry.MeasurementNotes,
            Color = target.Color,
            CountSymbol = measurementType == "point"
                ? ResolveMeasurementClipboardCountSymbol(entry, target)
                : CountDisplaySymbol.Circle,
            Points = entry.Points.Select(p => new SKPoint(p.X + pasteOffset.X, p.Y + pasteOffset.Y)).ToList(),
            Holes = entry.Holes
                .Select(hole => hole.Select(p => new SKPoint(p.X + pasteOffset.X, p.Y + pasteOffset.Y)).ToList())
                .Where(hole => hole.Count >= 3)
                .ToList(),
            PageFolder = page.FolderPath,
            TakeoffFolder = target.FolderPath,
            ScaleMetersPerPt = scale,
            JoistEnabled = entry.MeasurementJoist.Enabled,
            JoistType = entry.MeasurementJoist.JoistType,
            JoistSpacingInches = entry.MeasurementJoist.SpacingInches > 0 ? entry.MeasurementJoist.SpacingInches : 16,
            JoistDirectionDegrees = entry.MeasurementJoist.DirectionDegrees,
            JoistDirectionLocked = entry.MeasurementJoist.DirectionLocked,
            JoistDirectionFollowsAreaRotation = entry.MeasurementJoist.DirectionFollowsAreaRotation,
            JoistAddEndJoist = entry.MeasurementJoist.AddEndJoist,
            JoistStartEdgeEnabled = entry.MeasurementJoist.StartEdgeEnabled,
            JoistEndEdgeEnabled = entry.MeasurementJoist.EndEdgeEnabled,
            JoistEdgeOverridesSet = entry.MeasurementJoist.EdgeOverridesSet,
            JoistPitch = JoistTakeoffCalculator.NormalizePitch(entry.MeasurementJoist.Pitch),
            JoistLengthRounding = JoistTakeoffCalculator.NormalizeLengthRounding(entry.MeasurementJoist.LengthRounding),
            JoistShowLabels = entry.MeasurementJoist.ShowLabels,
            JoistDetailedLabels = entry.MeasurementJoist.DetailedLabels,
            JoistMoveNote = entry.MeasurementJoist.MoveNote,
            JoistNoteOffsetX = entry.MeasurementJoist.NoteOffsetX,
            JoistNoteOffsetY = entry.MeasurementJoist.NoteOffsetY,
            JoistNotePositionSet = entry.MeasurementJoist.NotePositionSet,
            ExtraJoists = entry.MeasurementJoist.ExtraJoists
                .Select(extra => new JoistExtraSegment
                {
                    Id = Guid.NewGuid().ToString(),
                    Start = new SKPoint(extra.Start.X + pasteOffset.X, extra.Start.Y + pasteOffset.Y),
                    End = new SKPoint(extra.End.X + pasteOffset.X, extra.End.Y + pasteOffset.Y),
                })
                .ToList(),
        };
    }

    private static string ResolveMeasurementClipboardCountSymbol(MeasurementClipboardEntry entry, TakeoffItem target)
    {
        if (!string.IsNullOrWhiteSpace(entry.MeasurementCountSymbol))
            return CountDisplaySymbol.Normalize(entry.MeasurementCountSymbol);
        if (!string.IsNullOrWhiteSpace(target.CountSymbol))
            return CountDisplaySymbol.Normalize(target.CountSymbol);
        return MeasurementClipboardTakeoffCountSymbol(entry);
    }

    private static string MeasurementClipboardTakeoffCountSymbol(MeasurementClipboardEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.SourceTakeoffCountSymbol))
            return CountDisplaySymbol.Normalize(entry.SourceTakeoffCountSymbol);
        if (!string.IsNullOrWhiteSpace(entry.MeasurementCountSymbol))
            return CountDisplaySymbol.Normalize(entry.MeasurementCountSymbol);
        return CountDisplaySymbol.Circle;
    }

    private SKPoint CalculateMeasurementPasteOffset(
        PdfViewport viewport,
        IReadOnlyList<MeasurementClipboardEntry> entries,
        SKPoint? pasteAtPdf)
    {
        if (viewport.TryGetMixedClipboardPasteOffset(pasteAtPdf, out SKPoint mixedOffset))
            return mixedOffset;

        if (!pasteAtPdf.HasValue || !TryGetClipboardBounds(entries, out SKRect bounds))
            return new SKPoint(0, 0);

        var sourceAnchor = new SKPoint(bounds.Left, bounds.Top);
        return new SKPoint(
            pasteAtPdf.Value.X - sourceAnchor.X,
            pasteAtPdf.Value.Y - sourceAnchor.Y);
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
        foreach (JoistExtraClipboard extra in entry.MeasurementJoist.ExtraJoists)
        {
            yield return extra.Start;
            yield return extra.End;
        }
    }

    private static string MeasurementClipboardTargetKey(MeasurementClipboardEntry entry)
    {
        string source = string.IsNullOrWhiteSpace(entry.SourceTakeoffFolder)
            ? $"{entry.SourceTakeoffName}|{entry.MeasurementType}|{entry.SourceTakeoffColor}|joist:{entry.SourceTakeoffJoist.Enabled}"
            : entry.SourceTakeoffFolder;
        return source.Trim();
    }

    private static MeasurementJoistClipboard CaptureMeasurementJoistClipboard(Measurement measurement) =>
        new(
            measurement.JoistEnabled,
            measurement.JoistType,
            measurement.JoistSpacingInches,
            measurement.JoistDirectionDegrees,
            measurement.JoistDirectionLocked,
            measurement.JoistDirectionFollowsAreaRotation,
            measurement.JoistAddEndJoist,
            measurement.JoistStartEdgeEnabled,
            measurement.JoistEndEdgeEnabled,
            measurement.JoistEdgeOverridesSet,
            JoistTakeoffCalculator.NormalizePitch(measurement.JoistPitch),
            JoistTakeoffCalculator.NormalizeLengthRounding(measurement.JoistLengthRounding),
            measurement.JoistShowLabels,
            measurement.JoistDetailedLabels,
            measurement.ExtraJoists
                .Select(extra => new JoistExtraClipboard(
                    new SKPoint(extra.Start.X, extra.Start.Y),
                    new SKPoint(extra.End.X, extra.End.Y)))
                .ToList(),
            measurement.JoistMoveNote, measurement.JoistNoteOffsetX, measurement.JoistNoteOffsetY, measurement.JoistNotePositionSet);

    private static TakeoffJoistClipboard CaptureTakeoffJoistClipboard(TakeoffItem? item, Measurement measurement)
    {
        if (item != null)
        {
            return new TakeoffJoistClipboard(
                item.IsJoistArea,
                item.JoistType,
                item.JoistSpacingInches,
                item.JoistDirectionDegrees,
                item.JoistDirectionFollowsAreaRotation,
                item.JoistAddEndJoist,
                JoistTakeoffCalculator.NormalizePitch(item.JoistPitch),
                JoistTakeoffCalculator.NormalizeLengthRounding(item.JoistLengthRounding),
                item.JoistShowLabels,
                item.JoistDetailedLabels,
                item.JoistMoveNote);
        }

        return new TakeoffJoistClipboard(
            measurement.JoistEnabled,
            measurement.JoistType,
            measurement.JoistSpacingInches,
            measurement.JoistDirectionDegrees,
            measurement.JoistDirectionFollowsAreaRotation,
            measurement.JoistAddEndJoist,
            JoistTakeoffCalculator.NormalizePitch(measurement.JoistPitch),
            JoistTakeoffCalculator.NormalizeLengthRounding(measurement.JoistLengthRounding),
            measurement.JoistShowLabels,
            measurement.JoistDetailedLabels,
            measurement.JoistMoveNote);
    }

    private static void ApplyTakeoffJoistClipboard(
        TakeoffItem target,
        TakeoffJoistClipboard joist,
        string measurementType)
    {
        bool canBeJoist = OurPlanCoreJobStore.NormalizeMeasurementType(measurementType) == "area";
        target.IsJoistTakeoff = canBeJoist && joist.Enabled;
        target.JoistType = joist.JoistType;
        target.JoistSpacingInches = joist.SpacingInches > 0 ? joist.SpacingInches : 16;
        target.JoistDirectionDegrees = joist.DirectionDegrees;
        target.JoistDirectionFollowsAreaRotation = joist.DirectionFollowsAreaRotation;
        target.JoistAddEndJoist = joist.AddEndJoist;
        target.JoistPitch = JoistTakeoffCalculator.NormalizePitch(joist.Pitch);
        target.JoistLengthRounding = JoistTakeoffCalculator.NormalizeLengthRounding(joist.LengthRounding);
        target.JoistShowLabels = joist.ShowLabels;
        target.JoistDetailedLabels = joist.DetailedLabels;
        target.JoistMoveNote = joist.MoveNote;
    }

    private TakeoffItem? FindTakeoffItemForMeasurement(Measurement measurement)
    {
        TakeoffItem? item = _takeoffItems.FirstOrDefault(i => i.Measurements.Contains(measurement));
        if (item != null)
            return item;

        return FindTakeoffItemByFolder(measurement.TakeoffFolder, measurement.MType);
    }

    private Dictionary<Measurement, TakeoffItem> BuildTakeoffItemByMeasurementLookup()
    {
        var lookup = new Dictionary<Measurement, TakeoffItem>();
        foreach (TakeoffItem item in _takeoffItems)
        foreach (Measurement measurement in item.Measurements)
            lookup.TryAdd(measurement, item);
        return lookup;
    }

    private TakeoffItem? FindTakeoffItemByFolder(string? folderPath, string? measurementType = null)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return null;

        string normalizedType = string.IsNullOrWhiteSpace(measurementType)
            ? ""
            : OurPlanCoreJobStore.NormalizeMeasurementType(measurementType);

        return _takeoffItems.FirstOrDefault(item =>
            string.Equals(item.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(normalizedType) ||
             OurPlanCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == normalizedType));
    }

    private void QueueTakeoffAutosave(TakeoffItem item)
        => _takeoffSaveService.MarkDirty(item);

    private void QueueTakeoffAutosave(IEnumerable<TakeoffItem> items)
        => _takeoffSaveService.MarkDirty(items);

    private TakeoffFlushResult FlushTakeoffAutosaves()
    {
        TakeoffFlushResult result = _takeoffSaveService.Flush();
        if (!result.Success)
            throw new IOException(TakeoffAutosaveFailureMessage("continue", result));
        return result;
    }

    private bool TryFlushTakeoffAutosaves(string operation, bool showDialog = true)
    {
        TakeoffFlushResult result = _takeoffSaveService.Flush();
        if (result.Success)
            return true;

        string message = TakeoffAutosaveFailureMessage(operation, result);
        AppLog.Warn(message);
        TxtStatus.Text = message;
        if (showDialog)
        {
            MessageBox.Show(
                message,
                "Unsaved Takeoff Changes",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        return false;
    }

    private static string TakeoffAutosaveFailureMessage(string operation, TakeoffFlushResult result)
    {
        string detail = string.IsNullOrWhiteSpace(result.Error) ? "Unknown write failure." : result.Error;
        return $"Cannot {operation}: {result.Failed} takeoff item(s) remain pending. {detail}";
    }

    internal void FlushPendingAutosave() =>
        FlushTakeoffAutosaves();
}
