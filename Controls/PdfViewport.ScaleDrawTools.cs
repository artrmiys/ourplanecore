using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Docnet.Core;
using Docnet.Core.Models;
using OurPlaneCore;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private void AddTwoPointAnnotation(SKPoint pdf, string kind)
    {
        _drawPts.Add(pdf);
        if (_drawPts.Count < 2)
        {
            RequestRepaint();
            PostRecordPrompt();
            return;
        }

        FinalizeAnnotation(kind);
    }
    private void HandleJoistDirectionClick(SKPoint pdf)
    {
        if (_joistDirectionMeasurement == null)
            return;

        _joistDirectionPts.Add(pdf);
        if (_joistDirectionPts.Count == 1)
        {
            _joistDirectionRubberEnd = pdf;
            PostStatus("Joist direction: click the second point.");
            RequestRepaint();
            return;
        }

        SKPoint start = _joistDirectionPts[0];
        SKPoint end = _joistDirectionPts[1];
        Measurement area = _joistDirectionMeasurement;
        CancelJoistDirectionCapture();
        JoistDirectionCaptured?.Invoke(area, start, end);
        RequestRepaint();
    }

    private void HandleScaleClick(SKPoint pdf)
    {
        _scalePts.Add(pdf);
        if (_scalePts.Count == 2)
        {
            float dist = MeasurementGeometry.Distance(_scalePts[1], _scalePts[0]);  // PDF points
            if (dist < 1.0f)
            {
                MessageBox.Show(
                    "Calibration distance is too short, please pick two distinct points.",
                    "Set Scale",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                _scalePts.Clear();
                RequestRepaint();
                PostStatus("Scale was not changed: pick two distinct calibration points.");
                return;
            }

            const double PT_M = ViewportConstants.PdfPointMeters;
            double lengthAtOneEighthFt = dist * PT_M * 96 / 0.3048;
            double lengthAtQuarterFt = dist * PT_M * 48 / 0.3048;
            string hint =
                $"Measured {dist:F1} pt on PDF\n" +
                $"(At 1:100 в‰€ {dist * PT_M * 100:F3} m  |  1:50 в‰€ {dist * PT_M * 50:F3} m)\n\n" +
                $"(At 1/8\" = 1'0\" = {lengthAtOneEighthFt:F2} ft  |  1/4\" = 1'0\" = {lengthAtQuarterFt:F2} ft)\n\n" +
                "Enter real distance in feet:";
            hint =
                $"Measured {dist:F1} pt on PDF\n" +
                $"(At 1/8\" = 1'0\" = {lengthAtOneEighthFt:F2} ft  |  1/4\" = 1'0\" = {lengthAtQuarterFt:F2} ft)\n\n" +
                "Enter real distance in feet:";

            var dlg = new ScaleInputDialog(hint);
            if (dlg.ShowDialog() == true && dlg.Value > 0)
            {
                ScaleMetersPerPt = dlg.Value / dist;
                ScaleChanged?.Invoke(ScaleMetersPerPt);
                PostStatus($"Scale set: {PdfSheetMetadataService.FormatImperialScale(ScaleMetersPerPt)}");
            }

            _scalePts.Clear();
            RequestRepaint();
        }
        else
        {
            RequestRepaint();
            PostStatus("Scale: click the second point of a known distance.");
        }
    }

    private void FinalizeDrawing()
    {
        if (_drawPts.Count == 0) return;
        if (!EnsureScaleForLinearArea())
            return;

        if (_tool == ViewerTool.Line  && _drawPts.Count < 2) { CancelDrawing(); return; }
        if (_tool == ViewerTool.Area  && _drawPts.Count < 3) { CancelDrawing(); return; }
        List<SKPoint> points = CleanFinalizePoints(_drawPts, closeArea: _tool == ViewerTool.Area);
        if (_tool == ViewerTool.Area && points.Count < 3) { CancelDrawing(); return; }

        var m = new Measurement
        {
            MType      = _tool.ToString().ToLower(),
            Points     = points,
            Color      = ActiveColor,
            CountSymbol = _tool == ViewerTool.Point ? CountDisplaySymbol.Normalize(ActiveCountSymbol) : CountDisplaySymbol.Circle,
            PageFolder = _pageFolder,
            TakeoffFolder = ActiveTakeoffFolder,
            ScaleMetersPerPt = ScaleMetersPerPt,
        };
        _measurements.Add(m);
        _measurementSet.Add(m);
        IndexMeasurementByPage(m);
        PushAddedMeasurementsUndo([m], $"remove added {EntryTitle(m.MType)}");
        _drawPts.Clear();
        _rubberEnd = null;
        SetSnapPreview(null);
        ClearEdgeSnapPreview();
        RequestRepaint();
        PostStatus($"Added {EntryTitle(m.MType)}  {m.Label(ScaleMetersPerPt, UnitMode)}");
        MeasurementAdded?.Invoke(m);
        PostRecordPrompt();
    }

    private void FinalizeAnnotation(string kind)
    {
        if (_drawPts.Count < 2)
            return;

        string normalizedKind = OurPlaneCoreJobStore.NormalizePageAnnotationKind(kind);
        var annotation = new PageAnnotation
        {
            Kind = normalizedKind,
            Points = _drawPts.Take(2).ToList(),
            Color = normalizedKind == "dimension"
                ? "#1565C0"
                : normalizedKind == "highlight"
                    ? "#FFC107"
                    : ActiveAnnotationColor,
            StrokeWidth = normalizedKind == "dimension" ? RulerStrokeWidthPx() : ActiveAnnotationStrokeWidth,
            PageFolder = _pageFolder,
            ScaleMetersPerPt = ScaleMetersPerPt,
        };
        _annotations.Add(annotation);
        PushAddedAnnotationsUndo([annotation], $"remove added {ToolTitle(normalizedKind)} markup");
        _drawPts.Clear();
        _rubberEnd = null;
        SetSnapPreview(null);
        RequestRepaint();
        PostStatus(normalizedKind == "dimension"
            ? $"Added dimension markup: {AnnotationLabel(annotation)}."
            : $"Added {ToolTitle(normalizedKind)} markup.");
        PageAnnotationAdded?.Invoke(annotation);
        PostRecordPrompt();
    }

    private void FinalizeAreaAnnotation()
    {
        if (_drawPts.Count < 3)
        {
            CancelDrawing();
            PostStatus("Area annotation cancelled.");
            return;
        }
        List<SKPoint> points = CleanFinalizePoints(_drawPts, closeArea: true);
        if (points.Count < 3)
        {
            CancelDrawing();
            PostStatus("Area annotation cancelled.");
            return;
        }

        var annotation = new PageAnnotation
        {
            Kind = "area",
            Points = points,
            Color = ActiveAnnotationColor,
            StrokeWidth = ActiveAnnotationStrokeWidth,
            PageFolder = _pageFolder,
            ScaleMetersPerPt = ScaleMetersPerPt,
        };
        _annotations.Add(annotation);
        PushAddedAnnotationsUndo([annotation], "remove added Area annotation");
        _drawPts.Clear();
        _rubberEnd = null;
        SetSnapPreview(null);
        RequestRepaint();
        PostStatus("Added Area annotation.");
        PageAnnotationAdded?.Invoke(annotation);
        PostRecordPrompt();
    }

    private List<SKPoint> CleanFinalizePoints(IReadOnlyList<SKPoint> points, bool closeArea)
    {
        var clean = points.ToList();
        while (clean.Count >= 2 &&
               PdfToScreenDistance(MeasurementGeometry.Distance(clean[^1], clean[^2])) <= 3f)
        {
            clean.RemoveAt(clean.Count - 1);
        }

        if (closeArea)
        {
            while (clean.Count >= 4 &&
                   PdfToScreenDistance(MeasurementGeometry.Distance(clean[^1], clean[0])) <= 14f)
            {
                clean.RemoveAt(clean.Count - 1);
            }
        }

        return clean;
    }

    private void AddNoteAnnotation(SKPoint pdf)
    {
        string? text = PageAnnotationTextRequested?.Invoke(
            "Note text:",
            "",
            "Sheet Note");
        if (string.IsNullOrWhiteSpace(text))
        {
            PostStatus("Note cancelled.");
            return;
        }

        var annotation = new PageAnnotation
        {
            Kind = "note",
            Text = text.Trim(),
            Points = DefaultNoteBounds(pdf),
            Color = ActiveAnnotationColor,
            StrokeWidth = ActiveAnnotationStrokeWidth,
            PageFolder = _pageFolder,
            ScaleMetersPerPt = ScaleMetersPerPt,
        };
        _annotations.Add(annotation);
        PushAddedAnnotationsUndo([annotation], "remove added Note markup");
        ClearAnnotationSelection();
        SelectAnnotation(annotation, -1);
        RequestRepaint();
        PostStatus("Added Note markup. Drag body to move; blue handles reshape; orange handle rotates/scales.");
        PageAnnotationAdded?.Invoke(annotation);
        PostRecordPrompt();
    }

    public PageAnnotation AddNoteAnnotationAt(
        SKPoint pdf,
        string text,
        string color = "#F9A825",
        float widthScreenPx = 340f,
        float heightScreenPx = 190f)
    {
        string clean = string.IsNullOrWhiteSpace(text) ? "Note" : text.Trim();
        var annotation = new PageAnnotation
        {
            Kind = "note",
            Text = clean,
            Points = NoteBounds(pdf, widthScreenPx, heightScreenPx),
            Color = string.IsNullOrWhiteSpace(color) ? "#F9A825" : color,
            StrokeWidth = ActiveAnnotationStrokeWidth,
            PageFolder = _pageFolder,
            ScaleMetersPerPt = ScaleMetersPerPt,
        };
        _annotations.Add(annotation);
        PushAddedAnnotationsUndo([annotation], "remove added AI Note markup");
        ClearAnnotationSelection();
        SelectAnnotation(annotation, -1);
        RequestRepaint();
        PostStatus("Added AI Note markup. Drag body to move; blue handles reshape; orange handle rotates/scales.");
        PageAnnotationAdded?.Invoke(annotation);
        return annotation;
    }

    private List<SKPoint> DefaultNoteBounds(SKPoint origin)
    {
        return NoteBounds(origin, 190f, 78f);
    }

    private List<SKPoint> NoteBounds(SKPoint origin, float widthScreenPx, float heightScreenPx)
    {
        float width = ScreenToPdfDistance(Math.Clamp(widthScreenPx, 120f, 620f));
        float height = ScreenToPdfDistance(Math.Clamp(heightScreenPx, 70f, 440f));
        float right = origin.X + width;
        float bottom = origin.Y + height;
        if (_pdfW > 0 && right > _pdfW)
        {
            origin.X = Math.Max(0, _pdfW - width);
            right = Math.Min(_pdfW, origin.X + width);
        }
        if (_pdfH > 0 && bottom > _pdfH)
        {
            origin.Y = Math.Max(0, _pdfH - height);
            bottom = Math.Min(_pdfH, origin.Y + height);
        }

        return
        [
            origin,
            new SKPoint(right, origin.Y),
            new SKPoint(right, bottom),
            new SKPoint(origin.X, bottom),
        ];
    }

    private void CompleteOrCancelDrawing()
    {
        if (IsMissingScaleForLinearArea())
        {
            EnsureScaleForLinearArea();
            return;
        }

        if (_tool == ViewerTool.Line && _drawPts.Count >= 2)
        {
            FinalizeDrawing();
            return;
        }

        if (_tool == ViewerTool.Area && _drawPts.Count >= 3)
        {
            FinalizeDrawing();
            return;
        }

        if (_tool == ViewerTool.AreaCut && !BoxModeEnabled)
        {
            if (_drawPts.Count >= 3)
            {
                FinalizeAreaCutPolygon();
                return;
            }

            CancelDrawing(clearSelection: false);
            PostStatus("Cut cancelled.");
            return;
        }

        if (_tool == ViewerTool.DrawArea)
        {
            if (_drawPts.Count >= 3)
            {
                FinalizeAreaAnnotation();
                return;
            }

            CancelDrawing();
            PostStatus("Area annotation cancelled.");
            return;
        }

        CancelDrawing(clearSelection: _tool != ViewerTool.AreaCut);
        PostStatus("Cancelled.");
    }
}
