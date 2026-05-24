using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private void AddBeamMeasurementPoint(SKPoint pdf)
    {
        _drawPts.Add(pdf);
        if (_drawPts.Count < 2)
        {
            RequestRepaint();
            PostRecordPrompt();
            return;
        }

        FinalizeBeamMeasurement();
    }

    private void FinalizeBeamMeasurement()
    {
        if (_drawPts.Count < 2)
            return;

        SKPoint start = _drawPts[0];
        SKPoint end = _drawPts[1];
        float lengthPt = MeasurementGeometry.Distance(start, end);
        if (lengthPt <= ViewportConstants.ZeroLengthEpsilon)
        {
            CancelDrawing();
            PostStatus("Beam cancelled: measured length is too short.");
            return;
        }

        PageAnnotation annotation = AddDimensionAnnotationFromPoints(start, end);
        double lengthFeet = lengthPt * ScaleMetersPerPt / 0.3048;
        double orderLengthFeet = BeamTakeoffService.RoundOrderLengthFeet(lengthFeet);
        string orderLengthText = BeamTakeoffService.FormatOrderLengthFeet(lengthFeet);
        SKPoint countPoint = BeamCountPoint(start, end);

        _drawPts.Clear();
        _rubberEnd = null;
        SetSnapPreview(null);
        RequestRepaint();
        PostStatus($"Beam measured: {lengthFeet:0.##} ft, order size {orderLengthText}. Create Count item.");
        PageAnnotationAdded?.Invoke(annotation);
        BeamMeasurementCompleted?.Invoke(new BeamMeasurementRequest(
            start,
            end,
            countPoint,
            lengthFeet,
            orderLengthFeet,
            orderLengthText,
            _pageFolder));
    }

    private PageAnnotation AddDimensionAnnotationFromPoints(SKPoint start, SKPoint end)
    {
        var annotation = new PageAnnotation
        {
            Kind = "dimension",
            Points = new List<SKPoint> { start, end },
            Color = "#1565C0",
            StrokeWidth = ActiveAnnotationStrokeWidth,
            PageFolder = _pageFolder,
            ScaleMetersPerPt = ScaleMetersPerPt,
        };
        _annotations.Add(annotation);
        PushAddedAnnotationsUndo([annotation], "remove added Ruler markup");
        return annotation;
    }

    private SKPoint BeamCountPoint(SKPoint start, SKPoint end)
    {
        SKPoint labelPoint = SegmentLengthLabelPoint(start, end);
        float offsetX = ScreenToPdfDistance(18f);
        float offsetY = ScreenToPdfDistance(18f);
        bool horizontal = Math.Abs(end.X - start.X) >= Math.Abs(end.Y - start.Y);
        SKPoint point = horizontal
            ? new SKPoint(labelPoint.X - offsetX, labelPoint.Y - offsetY)
            : new SKPoint(labelPoint.X - offsetX, labelPoint.Y + offsetY);
        return ClampPdfPointToPage(point);
    }

    private SKPoint ClampPdfPointToPage(SKPoint point)
    {
        if (_pdfW <= 0 || _pdfH <= 0)
            return point;

        return new SKPoint(
            Math.Clamp(point.X, 0, _pdfW),
            Math.Clamp(point.Y, 0, _pdfH));
    }

    public Measurement AddCountMeasurementAt(SKPoint pdf)
    {
        var measurement = new Measurement
        {
            MType = "point",
            Points = [ClampPdfPointToPage(pdf)],
            Color = ActiveColor,
            CountSymbol = CountDisplaySymbol.Normalize(ActiveCountSymbol),
            PageFolder = _pageFolder,
            TakeoffFolder = ActiveTakeoffFolder,
            ScaleMetersPerPt = ScaleMetersPerPt,
        };
        _measurements.Add(measurement);
        _measurementSet.Add(measurement);
        IndexMeasurementByPage(measurement);
        PushAddedMeasurementsUndo([measurement], "remove added Count mark");
        ClearSelection();
        SelectMeasurement(measurement, -1);
        RequestRepaint();
        PostStatus($"Added Count mark  {measurement.Label(ScaleMetersPerPt, UnitMode)}");
        MeasurementAdded?.Invoke(measurement);
        return measurement;
    }
}
