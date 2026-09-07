using System;
using System.Linq;
using System.Windows.Input;
using SkiaSharp;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    private Measurement? _dragJoistNote;
    private SKPoint _dragJoistNoteStart;
    private SKPoint _dragJoistNoteBefore;
    private SKPoint _dragJoistNoteOrigin;
    private bool _dragJoistNotePositionWasSet;

    private bool TryBeginJoistNoteDrag(SKPoint pdf)
    {
        if (IsReadOnlyMode || IsSelectionModifierActive() || IsVertexModifierActive() ||
            !ShouldDrawJoistSummaryLabel())
            return false;

        foreach (Measurement area in ActivePageMeasurements().Reverse())
        {
            if (!area.JoistEnabled || !area.JoistMoveNote || area.MType != "area" ||
                !IsMeasurementTakeoffVisible(area) || !JoistNoteBounds(area).Contains(pdf))
                continue;

            SelectMeasurement(area, -1);
            _dragJoistNote = area;
            _dragJoistNoteStart = pdf;
            _dragJoistNoteBefore = new SKPoint(area.JoistNoteOffsetX, area.JoistNoteOffsetY);
            _dragJoistNotePositionWasSet = area.JoistNotePositionSet;
            SKRect bounds = JoistNoteBounds(area);
            _dragJoistNoteOrigin = new SKPoint(bounds.MidX, bounds.MidY) - MeasurementGeometry.Centroid(area.Points);
            CaptureMouse();
            Cursor = Cursors.SizeAll;
            PostStatus("Move joist note: drag the table; Esc cancels, Ctrl+Z undoes.");
            return true;
        }
        return false;
    }

    private SKRect JoistNoteBounds(Measurement area)
    {
        string[] lines = MeasurementLabelText(area, null)
            .Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        float divisor = ScaleMeasurementLabelsWithPage
            ? Math.Max(CurrentFitZoom(), 0.001f) : Math.Max(_zoom, 0.001f);
        TextBoxLayout layout = ResolveTextBoxLayout(lines, MeasurementLabelFontScreenPx,
            MeasurementLabelPaddingScreenPx, ClampOverlayUserScale(MeasurementLabelScale), divisor, SKColors.White);
        SKPoint anchor = area.JoistNoteAnchor();
        if (area.HasJoistNotePosition)
            return new SKRect(anchor.X - layout.Width / 2 - layout.PdfPad, anchor.Y - layout.TextHeight / 2 - layout.PdfPad,
                anchor.X + layout.Width / 2 + layout.PdfPad, anchor.Y + layout.TextHeight / 2 + layout.PdfPad);
        return new SKRect(anchor.X + layout.PdfPad, anchor.Y - layout.TextHeight - layout.PdfPad,
            anchor.X + layout.Width + layout.PdfPad * 3, anchor.Y + layout.PdfPad);
    }

    private bool UpdateJoistNoteDrag(SKPoint pdf)
    {
        if (_dragJoistNote is not { } area)
            return false;
        if (IsReadOnlyMode || !area.JoistMoveNote || !_measurementSet.Contains(area) ||
            !IsMeasurementOnActivePage(area))
            return FinishJoistNoteDrag(cancel: true);

        SKPoint offset = _dragJoistNoteOrigin + pdf - _dragJoistNoteStart;
        SKPoint anchor = ClampPdfPointToPage(MeasurementGeometry.Centroid(area.Points) + offset);
        offset = anchor - MeasurementGeometry.Centroid(area.Points);
        area.JoistNoteOffsetX = offset.X;
        area.JoistNoteOffsetY = offset.Y;
        area.JoistNotePositionSet = true;
        RequestPointerMoveRepaint();
        return true;
    }

    private bool FinishJoistNoteDrag(bool cancel = false)
    {
        if (_dragJoistNote is not { } area)
            return false;
        _dragJoistNote = null;
        SKPoint after = new(area.JoistNoteOffsetX, area.JoistNoteOffsetY);
        bool positionSetAfter = area.JoistNotePositionSet;
        area.JoistNoteOffsetX = _dragJoistNoteBefore.X;
        area.JoistNoteOffsetY = _dragJoistNoteBefore.Y;
        area.JoistNotePositionSet = _dragJoistNotePositionWasSet;
        if (!cancel && (after != _dragJoistNoteBefore || positionSetAfter != _dragJoistNotePositionWasSet))
        {
            PushGeometryUndoSnapshot([area], [], "move joist note");
            area.JoistNoteOffsetX = after.X;
            area.JoistNoteOffsetY = after.Y;
            area.JoistNotePositionSet = positionSetAfter;
            NotifyMeasurementsChanged([area]);
        }
        ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
        RequestRepaint();
        return true;
    }
}
