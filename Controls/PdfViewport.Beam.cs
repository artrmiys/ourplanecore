using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace OurPlanCore.Controls;

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
            _pageFolder,
            annotation.Id));
    }

    private void AddOpeningMeasurementPoint(SKPoint pdf)
    {
        _drawPts.Add(pdf);
        if (_drawPts.Count < 2)
        {
            RequestRepaint();
            PostRecordPrompt();
            return;
        }

        FinalizeOpeningMeasurement();
    }

    private void FinalizeOpeningMeasurement()
    {
        if (_drawPts.Count < 2)
            return;

        SKPoint first = _drawPts[0];
        SKPoint opposite = _drawPts[1];
        SKRect rect = NormalizeRect(first, opposite);
        if (rect.Width <= ViewportConstants.ZeroLengthEpsilon ||
            rect.Height <= ViewportConstants.ZeroLengthEpsilon)
        {
            CancelDrawing();
            PostStatus("Openings cancelled: measured box is too small.");
            return;
        }

        double widthFeet = rect.Width * ScaleMetersPerPt / 0.3048;
        double heightFeet = rect.Height * ScaleMetersPerPt / 0.3048;
        string sizeText = OpeningTakeoffService.FormatSizeFeet(widthFeet, heightFeet);
        PageAnnotation widthAnnotation = AddDimensionAnnotationFromPoints(
            new SKPoint(rect.Right, rect.Top),
            new SKPoint(rect.Left, rect.Top));
        PageAnnotation heightAnnotation = AddDimensionAnnotationFromPoints(
            new SKPoint(rect.Left, rect.Top),
            new SKPoint(rect.Left, rect.Bottom));
        SKPoint countPoint = new((rect.Left + rect.Right) / 2f, (rect.Top + rect.Bottom) / 2f);

        _drawPts.Clear();
        _rubberEnd = null;
        SetSnapPreview(null);
        RequestRepaint();
        PostStatus($"Openings measured: {sizeText}. Create Count item.");
        PageAnnotationAdded?.Invoke(widthAnnotation);
        PageAnnotationAdded?.Invoke(heightAnnotation);
        OpeningMeasurementCompleted?.Invoke(new OpeningMeasurementRequest(
            first,
            opposite,
            countPoint,
            widthFeet,
            heightFeet,
            sizeText,
            _pageFolder));
    }

    private PageAnnotation AddDimensionAnnotationFromPoints(SKPoint start, SKPoint end)
    {
        var annotation = new PageAnnotation
        {
            Kind = "dimension",
            Points = new List<SKPoint> { start, end },
            Color = "#1565C0",
            StrokeWidth = RulerStrokeWidthPx(),
            PageFolder = _pageFolder,
            ScaleMetersPerPt = ScaleMetersPerPt,
        };
        _annotations.Add(annotation);
        PushAddedAnnotationsUndo([annotation], "remove added Ruler markup");
        return annotation;
    }

    public PageAnnotation? AddBeamAnnotationLine(SKPoint start, SKPoint end, string color)
    {
        if (MeasurementGeometry.Distance(start, end) <= ViewportConstants.ZeroLengthEpsilon)
            return null;

        var annotation = new PageAnnotation
        {
            Kind = "line",
            Points = [start, end],
            Color = BeamAnnotationConfig.NormalizeColor(color),
            StrokeWidth = ActiveAnnotationStrokeWidth,
            PageFolder = _pageFolder,
            ScaleMetersPerPt = ScaleMetersPerPt,
        };
        _annotations.Add(annotation);
        PushAddedAnnotationsUndo([annotation], "remove added Beam line markup");
        RequestRepaint();
        PageAnnotationAdded?.Invoke(annotation);
        return annotation;
    }

    public void OffsetBeamDimension(BeamMeasurementRequest request, double offsetScreenPx)
    {
        PageAnnotation? dimension = _annotations.FirstOrDefault(a => a.Id == request.DimensionId);
        if (dimension == null || IsReadOnlyMode)
            return;

        SKPoint offset = BeamTakeoffService.DimensionOffset(
            request.StartPdf, request.EndPdf, request.CountPointPdf,
            ScreenToPdfDistance((float)BeamAnnotationConfig.NormalizeDimensionOffset(offsetScreenPx)));
        PushAnnotationUndoSnapshot(dimension, dimension.Points.ToList(), "move Beam dimension");
        dimension.Points = [request.StartPdf + offset, request.EndPdf + offset];
        PageAnnotationChanged?.Invoke(dimension);
        RequestRepaint();
    }

    private SKPoint BeamCountPoint(SKPoint start, SKPoint end)
    {
        SKPoint labelPoint = SegmentLengthLabelPoint(start, end);
        float offsetX = ScreenToPdfDistance(36f);
        float offsetY = ScreenToPdfDistance(36f);
        bool horizontal = Math.Abs(end.X - start.X) >= Math.Abs(end.Y - start.Y);
        SKPoint point = horizontal
            ? new SKPoint(labelPoint.X - offsetX, labelPoint.Y - offsetY)
            : new SKPoint(labelPoint.X - offsetX, labelPoint.Y + offsetY);
        return ClampPdfPointToPage(point);
    }

    private void DrawLiveOpeningSizeLabels(SKCanvas canvas)
    {
        if (_drawPts.Count == 0 ||
            !_rubberEnd.HasValue ||
            ScaleMetersPerPt <= 0)
        {
            return;
        }

        SKRect rect = NormalizeRect(_drawPts[0], _rubberEnd.Value);
        if (rect.Width <= ViewportConstants.ZeroLengthEpsilon ||
            rect.Height <= ViewportConstants.ZeroLengthEpsilon)
        {
            return;
        }

        SKPoint topLeft = new(rect.Left, rect.Top);
        SKPoint topRight = new(rect.Right, rect.Top);
        SKPoint bottomLeft = new(rect.Left, rect.Bottom);
        DrawLiveRecordText(
            canvas,
            SegmentLengthLabelPoint(topLeft, topRight),
            FormatLiveFeet(rect.Width * ScaleMetersPerPt),
            centered: true);
        DrawLiveRecordText(
            canvas,
            SegmentLengthLabelPoint(topLeft, bottomLeft),
            FormatLiveFeet(rect.Height * ScaleMetersPerPt),
            centered: true);
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
