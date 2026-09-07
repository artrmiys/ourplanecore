using System.Collections.Generic;
using System.Linq;
using OurPlanCore;
using SkiaSharp;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    private readonly List<CutRegionClipboardTemplate> _holeClipboard = [];
    private SKPoint? _measurementCutRegionClipboardAnchor;
    private Measurement? _pendingMixedPasteExplicitTarget;
    private bool _pendingMixedCutRegionPaste;
    private readonly List<CutRegionRef> _completedMixedPasteCutRegions = [];
    private readonly List<CutRegionPasteReservation> _reservedMixedCutRegionPaste = [];
    private readonly HashSet<Measurement> _measurementClipboardSourceMeasurements = [];

    public int CutRegionClipboardCount => _holeClipboard.Count;

    internal bool CopyCurrentMeasurementAndCutRegionSelection()
    {
        IReadOnlyList<Measurement> measurements = GetSelectedMeasurements();
        IReadOnlyList<CutRegionRef> cutRegions = SelectedTransformCutRegions();
        if (measurements.Count == 0 && cutRegions.Count == 0)
            return false;

        if (measurements.Count > 0)
            CopyMeasurementsRequested?.Invoke(measurements);

        _measurementClipboardSourceMeasurements.Clear();
        foreach (Measurement measurement in measurements)
            _measurementClipboardSourceMeasurements.Add(measurement);
        _holeClipboard.Clear();
        foreach (CutRegionRef cutRegion in cutRegions)
        {
            _holeClipboard.Add(new CutRegionClipboardTemplate(
                cutRegion.Parent.Id,
                cutRegion.Parent.Holes[cutRegion.HoleIndex]
                    .Select(point => new SKPoint(point.X, point.Y))
                    .ToList()));
        }

        var anchorPoints = measurements
            .SelectMany(MeasurementClipboardAnchorPoints)
            .Concat(_holeClipboard.SelectMany(entry => entry.Points))
            .ToList();
        _measurementCutRegionClipboardAnchor = anchorPoints.Count == 0
            ? null
            : new SKPoint(anchorPoints.Min(point => point.X), anchorPoints.Min(point => point.Y));

        if (_holeClipboard.Count > 0 && measurements.Count > 0)
            MarkMixedMeasurementCutRegionClipboardCurrent();
        else if (_holeClipboard.Count > 0)
            MarkCutRegionClipboardCurrent();
        else
            MarkMeasurementClipboardCurrent();

        PostStatus(measurements.Count > 0 && _holeClipboard.Count > 0
            ? $"Copied {measurements.Count} measurement(s) and {_holeClipboard.Count} cutout(s) as one bundle."
            : _holeClipboard.Count > 0
                ? $"Copied {_holeClipboard.Count} cutout(s)."
                : $"Copied {measurements.Count} measurement(s).");
        return true;
    }

    private static IEnumerable<SKPoint> MeasurementClipboardAnchorPoints(Measurement measurement)
    {
        foreach (SKPoint point in measurement.Points)
            yield return point;
        foreach (var hole in measurement.Holes)
            foreach (SKPoint point in hole)
                yield return point;
        foreach (JoistExtraSegment extra in measurement.ExtraJoists)
        {
            yield return extra.Start;
            yield return extra.End;
        }
    }

    public bool PasteCurrentMeasurementAndCutRegionClipboard(SKPoint? atPdf)
    {
        if (IsCutRegionClipboardCurrent)
            return PasteCutRegions(atPdf);

        if (IsMixedMeasurementCutRegionClipboardCurrent)
        {
            _pendingMixedCutRegionPaste = true;
            _pendingMixedPasteExplicitTarget = ExplicitSelectedPasteTarget();
            _reservedMixedCutRegionPaste.Clear();
            _completedMixedPasteCutRegions.Clear();
            PasteMeasurementsRequested?.Invoke(atPdf);
            return true;
        }

        PasteMeasurementsRequested?.Invoke(atPdf);
        return true;
    }

    internal bool TryGetMixedClipboardPasteOffset(SKPoint? atPdf, out SKPoint offset)
    {
        offset = default;
        if (!IsMixedMeasurementCutRegionClipboardCurrent ||
            !_measurementCutRegionClipboardAnchor.HasValue ||
            !atPdf.HasValue)
        {
            return false;
        }

        SKPoint source = _measurementCutRegionClipboardAnchor.Value;
        offset = new SKPoint(atPdf.Value.X - source.X, atPdf.Value.Y - source.Y);
        return true;
    }

    internal bool TryPreflightPendingMixedCutRegionPaste(
        SKPoint pasteOffset,
        out string failureStatus)
    {
        failureStatus = "";
        if (!_pendingMixedCutRegionPaste || !IsMixedMeasurementCutRegionClipboardCurrent)
            return true;

        _reservedMixedCutRegionPaste.Clear();
        if (CutRegionSelectionService.TryResolvePasteBundle(
                _holeClipboard,
                pasteOffset,
                _pendingMixedPasteExplicitTarget,
                ActivePageMeasurements(),
                excluded: _measurementClipboardSourceMeasurements,
                out var reservations,
                out string error))
        {
            _reservedMixedCutRegionPaste.AddRange(reservations);
            return true;
        }

        _pendingMixedCutRegionPaste = false;
        _pendingMixedPasteExplicitTarget = null;
        failureStatus =
            $"Paste cancelled: {error} No measurements or cutouts were added. " +
            "Select the intended destination Area explicitly, then paste again. Clipboard was kept.";
        return false;
    }

    internal bool ValidatePendingMixedCutRegionPasteReservation(out string failureStatus)
    {
        failureStatus = "";
        if (!_pendingMixedCutRegionPaste || !IsMixedMeasurementCutRegionClipboardCurrent)
            return true;

        HashSet<Measurement> activeMeasurements = ActivePageMeasurements().ToHashSet();
        bool valid = _reservedMixedCutRegionPaste.Count == _holeClipboard.Count &&
                     _reservedMixedCutRegionPaste.Count > 0 &&
                     _reservedMixedCutRegionPaste.All(reservation =>
                         activeMeasurements.Contains(reservation.Target) &&
                         reservation.Target.MType == "area" &&
                         CutRegionSelectionService.FitsInsideOuterBoundary(
                             reservation.Points,
                             reservation.Target.Points));
        if (valid)
            return true;

        CancelPendingMixedCutRegionPaste();
        failureStatus =
            "Paste cancelled: the reserved destination Area changed before paste. " +
            "No measurements or cutouts were added. Select the intended Area and paste again. Clipboard was kept.";
        return false;
    }

    internal void CancelPendingMixedCutRegionPaste()
    {
        _pendingMixedCutRegionPaste = false;
        _pendingMixedPasteExplicitTarget = null;
        _reservedMixedCutRegionPaste.Clear();
        _completedMixedPasteCutRegions.Clear();
    }

    internal bool CompletePendingMixedCutRegionPaste(
        IReadOnlyList<Measurement> addedMeasurements,
        out int pastedCutouts,
        out string resultStatus)
    {
        pastedCutouts = 0;
        resultStatus = "";
        if (!_pendingMixedCutRegionPaste || !IsMixedMeasurementCutRegionClipboardCurrent)
            return false;

        _pendingMixedCutRegionPaste = false;
        _completedMixedPasteCutRegions.Clear();
        if (_reservedMixedCutRegionPaste.Count != _holeClipboard.Count ||
            _reservedMixedCutRegionPaste.Count == 0)
        {
            CancelPendingMixedCutRegionPaste();
            throw new InvalidOperationException(
                "Mixed paste reservation was lost before the bundle could be committed.");
        }

        var beforePoints = _reservedMixedCutRegionPaste
            .Select(reservation => reservation.Target)
            .Distinct()
            .ToDictionary(target => target, target => target.Points.ToList());
        var beforeHoles = _reservedMixedCutRegionPaste
            .Select(reservation => reservation.Target)
            .Distinct()
            .ToDictionary(target => target, target => CloneHoles(target.Holes));
        int undoDepthBefore = _undoStack.Count;
        try
        {
            foreach (CutRegionPasteReservation reservation in _reservedMixedCutRegionPaste)
            {
                int holeIndex = reservation.Target.Holes.Count;
                reservation.Target.Holes.Add(reservation.Points);
                _completedMixedPasteCutRegions.Add(new CutRegionRef(reservation.Target, holeIndex));
            }

            NotifyMeasurementsChanged(beforePoints.Keys.ToList());
            pastedCutouts = _reservedMixedCutRegionPaste.Count;
            resultStatus = $" Attached {pastedCutouts} cutout(s) to destination Area.";
            PushMixedMeasurementUndo(
                beforePoints,
                beforeHoles,
                new Dictionary<Measurement, int>(),
                addedMeasurements,
                "remove pasted measurement and cutout bundle",
                "mixed-cutout-paste");
        }
        catch (Exception ex)
        {
            foreach ((Measurement target, List<List<SKPoint>> holes) in beforeHoles)
                target.Holes = CloneHoles(holes);
            if (_undoStack.Count > undoDepthBefore)
                _undoStack.RemoveRange(undoDepthBefore, _undoStack.Count - undoDepthBefore);
            CancelPendingMixedCutRegionPaste();
            try
            {
                NotifyMeasurementsChanged(beforePoints.Keys.ToList());
            }
            catch (Exception rollbackNotifyEx)
            {
                throw new AggregateException(
                    "Mixed paste failed and the restored destination Areas could not be queued for persistence.",
                    ex,
                    rollbackNotifyEx);
            }

            throw;
        }
        _pendingMixedPasteExplicitTarget = null;
        _reservedMixedCutRegionPaste.Clear();
        return true;
    }

    internal void RestoreCompletedMixedPasteSelection(IReadOnlyList<Measurement> pastedMeasurements)
    {
        if (_completedMixedPasteCutRegions.Count == 0)
            return;

        SetMixedMeasurementSelection(
            pastedMeasurements,
            _completedMixedPasteCutRegions.ToList(),
            pastedMeasurements.LastOrDefault());
        _completedMixedPasteCutRegions.Clear();
    }

    private bool PasteCutRegions(SKPoint? atPdf)
    {
        if (_holeClipboard.Count == 0)
            return false;

        SKPoint source = _measurementCutRegionClipboardAnchor ??
                         new SKPoint(
                             _holeClipboard.SelectMany(entry => entry.Points).Min(point => point.X),
                             _holeClipboard.SelectMany(entry => entry.Points).Min(point => point.Y));
        SKPoint target = atPdf ?? source;
        var offset = new SKPoint(target.X - source.X, target.Y - source.Y);
        if (!CutRegionSelectionService.TryResolvePasteBundle(
                _holeClipboard,
                offset,
                ExplicitSelectedPasteTarget(),
                ActivePageMeasurements(),
                excluded: null,
                out var resolved,
                out string error))
        {
            PostStatus($"Paste cutout: {error}");
            return true;
        }

        var targets = resolved.Select(reservation => reservation.Target).Distinct().ToList();
        var beforePoints = targets.ToDictionary(area => area, area => area.Points.ToList());
        var beforeHoles = targets.ToDictionary(area => area, area => CloneHoles(area.Holes));
        var selected = new List<CutRegionRef>();
        foreach (CutRegionPasteReservation reservation in resolved)
        {
            int holeIndex = reservation.Target.Holes.Count;
            reservation.Target.Holes.Add(reservation.Points);
            selected.Add(new CutRegionRef(reservation.Target, holeIndex));
        }

        PushMeasurementUndoSnapshots(beforePoints, beforeHoles, "paste cutout bundle", "cut-region-paste");
        SetMixedMeasurementSelection([], selected, null);
        NotifyMeasurementsChanged(targets);
        RequestRepaint();
        PostStatus($"Pasted {selected.Count} cutout(s); parent Area contour was not copied.");
        return true;
    }

    private Measurement? ExplicitSelectedPasteTarget()
    {
        var selectedAreas = GetSelectedMeasurements()
            .Where(measurement =>
                measurement.MType == "area" &&
                measurement.Points.Count >= 3 &&
                !_measurementClipboardSourceMeasurements.Contains(measurement) &&
                IsMeasurementOnActivePage(measurement))
            .ToList();
        return selectedAreas.Count == 1 ? selectedAreas[0] : null;
    }
}
