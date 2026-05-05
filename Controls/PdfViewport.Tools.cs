using System;
using System.Collections.Generic;
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
    // ═════════════════════════════════════════════════════════════════════════
    // Drawing tool logic
    // ═════════════════════════════════════════════════════════════════════════

    private void HandleLeftClick(SKPoint pdf)
    {
        if (!EnsureScaleForLinearArea())
            return;

        switch (_tool)
        {
            case ViewerTool.Scale:
                HandleScaleClick(pdf);
                break;
            case ViewerTool.Ruler:
                AddTwoPointAnnotation(pdf, "dimension");
                break;
            case ViewerTool.DrawLine:
                AddTwoPointAnnotation(pdf, "line");
                break;
            case ViewerTool.DrawArrow:
                AddTwoPointAnnotation(pdf, "arrow");
                break;
            case ViewerTool.DrawRect:
                AddTwoPointAnnotation(pdf, "rectangle");
                break;
            case ViewerTool.Point:
                _drawPts.Add(pdf);
                FinalizeDrawing();
                break;
            case ViewerTool.Line:
            case ViewerTool.Area:
                _drawPts.Add(pdf);
                RequestRepaint();
                PostRecordPrompt();
                break;
        }
    }

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
            float dx = _scalePts[1].X - _scalePts[0].X;
            float dy = _scalePts[1].Y - _scalePts[0].Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);  // PDF points

            const double PT_M = 25.4 / 72.0 / 1000.0;
            double lengthAtOneEighthFt = dist * PT_M * 96 / 0.3048;
            double lengthAtQuarterFt = dist * PT_M * 48 / 0.3048;
            string hint =
                $"Measured {dist:F1} pt on PDF\n" +
                $"(At 1:100 ≈ {dist * PT_M * 100:F3} m  |  1:50 ≈ {dist * PT_M * 50:F3} m)\n\n" +
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

        var m = new Measurement
        {
            MType      = _tool.ToString().ToLower(),
            Points     = new List<SKPoint>(_drawPts),
            Color      = ActiveColor,
            PageFolder = _pageFolder,
            TakeoffFolder = ActiveTakeoffFolder,
            ScaleMetersPerPt = ScaleMetersPerPt,
        };
        _measurements.Add(m);
        _drawPts.Clear();
        _rubberEnd = null;
        SetSnapPreview(null);
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
            Color = normalizedKind == "dimension" ? "#1565C0" : ActiveColor,
            PageFolder = _pageFolder,
            ScaleMetersPerPt = ScaleMetersPerPt,
        };
        _annotations.Add(annotation);
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

        CancelDrawing();
        PostStatus("Cancelled.");
    }

    private bool EnsureScaleForLinearArea()
    {
        if (!IsMissingScaleForLinearArea())
            return true;

        if (_drawPts.Count > 0 || _rubberEnd.HasValue || _snapPreview.HasValue)
        {
            _drawPts.Clear();
            _rubberEnd = null;
            SetSnapPreview(null);
            RequestRepaint();
        }

        PostScaleRequiredStatus();
        return false;
    }

    private bool IsMissingScaleForLinearArea() =>
        _tool is ViewerTool.Line or ViewerTool.Area or ViewerTool.Ruler && ScaleMetersPerPt <= 0;

    private void PostScaleRequiredStatus()
    {
        string tool = _tool switch
        {
            ViewerTool.Area => "Area",
            ViewerTool.Ruler => "Ruler",
            _ => "Line",
        };
        string mode = _tool == ViewerTool.Ruler ? "markup" : "Record";
        PostStatus($"{tool} {mode} blocked: set sheet scale first with Scale or PDF Auto Scale. Count and drawing markups can be recorded without scale.");
    }

    private void PostRecordPrompt()
    {
        string modes = DigitizerModeSuffix();
        switch (_tool)
        {
            case ViewerTool.Point:
                PostStatus($"Count Record: click each item to add a count. Turn Record off for Pan.{modes}");
                break;
            case ViewerTool.Line:
                if (IsMissingScaleForLinearArea())
                {
                    PostScaleRequiredStatus();
                    break;
                }

                PostStatus(_drawPts.Count switch
                {
                    0 => $"Line Record: click the first point.{modes}",
                    1 => $"Line Record: click the next point. Backspace/Ctrl+Z undo.{modes}",
                    _ => $"Line Record: click next point, or Esc / C / double-click to finish.{modes}",
                });
                break;
            case ViewerTool.Area:
                if (IsMissingScaleForLinearArea())
                {
                    PostScaleRequiredStatus();
                    break;
                }

                PostStatus(_drawPts.Count switch
                {
                    0 => $"Area Record: click the first corner.{modes}",
                    1 => $"Area Record: click the next corner. Backspace/Ctrl+Z undo.{modes}",
                    2 => $"Area Record: click at least one more corner, then finish.{modes}",
                    _ => $"Area Record: click next corner, or Esc / C / double-click to finish.{modes}",
                });
                break;
            case ViewerTool.Scale:
                PostStatus(_scalePts.Count == 0
                    ? $"Scale: click the first point of a known distance.{modes}"
                    : $"Scale: click the second point of a known distance.{modes}");
                break;
            case ViewerTool.Ruler:
                if (IsMissingScaleForLinearArea())
                {
                    PostScaleRequiredStatus();
                    break;
                }

                PostStatus(_drawPts.Count == 0
                    ? $"Ruler: click the first endpoint.{modes}"
                    : $"Ruler: click the second endpoint to place the dimension label.{modes}");
                break;
            case ViewerTool.DrawLine:
                PostStatus(_drawPts.Count == 0
                    ? $"Draw line: click the first endpoint.{modes}"
                    : $"Draw line: click the second endpoint.{modes}");
                break;
            case ViewerTool.DrawArrow:
                PostStatus(_drawPts.Count == 0
                    ? $"Arrow: click the tail point.{modes}"
                    : $"Arrow: click the arrow head point.{modes}");
                break;
            case ViewerTool.DrawRect:
                PostStatus(_drawPts.Count == 0
                    ? $"Box: click the first corner.{modes}"
                    : $"Box: click the opposite corner.{modes}");
                break;
            case ViewerTool.Select:
                PostStatus("Select: left-drag a box to select measurements. Ctrl+click toggles, Ctrl+C copies, Ctrl+V pastes.");
                break;
        }
    }

    private string DigitizerModeSuffix()
    {
        var modes = new List<string>();
        if (SnapEnabled)
            modes.Add("Snap F3");
        if (OrthoEnabled)
            modes.Add("Ortho F8");
        return modes.Count == 0 ? "" : $" [{string.Join(", ", modes)}]";
    }

    private static string ToolTitle(string type) =>
        type switch
        {
            "point" => "Count",
            "line" => "Line",
            "area" => "Area",
            "dimension" => "Ruler",
            "arrow" => "Arrow",
            "rectangle" => "Box",
            "select" => "Select",
            _ => type,
        };

    private static string EntryTitle(string type) =>
        type == "point" ? "Count mark" : $"{ToolTitle(type)} section";

    private void CancelDrawing()
    {
        _drawPts.Clear();
        _scalePts.Clear();
        _rubberEnd = null;
        _boxSelecting = false;
        SetSnapPreview(null);
        if (_draggingVertex && IsMouseCaptured)
            ReleaseMouseCapture();
        ClearSelection();
        RequestRepaint();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════════════════

    private SKPoint ResolveDigitizerPoint(SKPoint rawPdf, bool updatePreview)
    {
        if (SnapEnabled && TryFindSnapPoint(rawPdf, out SKPoint snapped, out string snapKind))
        {
            if (updatePreview)
                SetSnapPreview(snapped, snapKind);
            return snapped;
        }

        if (updatePreview)
            SetSnapPreview(null);

        return TryGetOrthoAnchor(out SKPoint anchor) && IsOrthoActive()
            ? ApplyOrtho(anchor, rawPdf)
            : rawPdf;
    }

    private bool IsOrthoActive()
    {
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        return OrthoEnabled ^ shift;
    }

    private static bool IsSelectionModifierActive() =>
        (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != ModifierKeys.None;

    private bool TryGetOrthoAnchor(out SKPoint anchor)
    {
        if (_tool is ViewerTool.Line or ViewerTool.Area or ViewerTool.Ruler or ViewerTool.DrawLine or ViewerTool.DrawArrow or ViewerTool.DrawRect &&
            _drawPts.Count > 0)
        {
            anchor = _drawPts[^1];
            return true;
        }

        if (_tool == ViewerTool.Scale && _scalePts.Count > 0)
        {
            anchor = _scalePts[^1];
            return true;
        }

        anchor = default;
        return false;
    }

    private static SKPoint ApplyOrtho(SKPoint anchor, SKPoint point)
    {
        float dx = point.X - anchor.X;
        float dy = point.Y - anchor.Y;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length <= 0.001f)
            return point;

        float angle = MathF.Atan2(dy, dx);
        float step = MathF.PI / 4f;
        float snappedAngle = MathF.Round(angle / step) * step;
        return new SKPoint(
            anchor.X + MathF.Cos(snappedAngle) * length,
            anchor.Y + MathF.Sin(snappedAngle) * length);
    }

    private bool TryFindSnapPoint(SKPoint rawPdf, out SKPoint snapped, out string snapKind)
    {
        float tolerance = SnapToleranceScreenPx / Math.Max(_zoom, 0.001f);
        float best = tolerance * tolerance;
        SKPoint bestPoint = default;
        string bestKind = "";
        bool found = false;
        var segments = new List<SnapSegment>();

        void Consider(SKPoint candidate, string kind)
        {
            float distance = DistanceSquared(rawPdf, candidate);
            if (distance >= best)
                return;

            best = distance;
            bestPoint = candidate;
            bestKind = kind;
            found = true;
        }

        void ConsiderPolyline(IReadOnlyList<SKPoint> points, bool closed, bool includeEndpoints)
        {
            if (includeEndpoints)
            {
                foreach (SKPoint point in points)
                    Consider(point, "endpoint");
            }

            for (int i = 1; i < points.Count; i++)
            {
                Consider(Midpoint(points[i - 1], points[i]), "midpoint");
                segments.Add(new SnapSegment(points[i - 1], points[i]));
            }

            if (closed && points.Count > 2)
            {
                Consider(Midpoint(points[^1], points[0]), "midpoint");
                segments.Add(new SnapSegment(points[^1], points[0]));
            }
        }

        for (int i = 0; i < _drawPts.Count; i++)
        {
            if (i == _drawPts.Count - 1 && _tool is ViewerTool.Line or ViewerTool.Area)
                continue;

            Consider(_drawPts[i], "endpoint");
        }
        ConsiderPolyline(_drawPts, _tool == ViewerTool.Area, includeEndpoints: false);

        foreach (SKPoint point in _scalePts)
            Consider(point, "endpoint");

        foreach (Measurement measurement in _measurements)
        {
            if (!IsMeasurementOnActivePage(measurement))
                continue;

            if (measurement.MType is "line" or "area")
                ConsiderPolyline(measurement.Points, measurement.MType == "area", includeEndpoints: true);
            else
                foreach (SKPoint point in measurement.Points)
                    Consider(point, "endpoint");
        }

        foreach (PageAnnotation annotation in _annotations)
        {
            if (!IsAnnotationOnActivePage(annotation) || annotation.Points.Count < 2)
                continue;

            ConsiderPolyline(AnnotationSnapPoints(annotation), closed: false, includeEndpoints: true);
        }

        for (int i = 0; i < segments.Count; i++)
        {
            for (int j = i + 1; j < segments.Count; j++)
            {
                SnapSegment a = segments[i];
                SnapSegment b = segments[j];
                if (SharesEndpoint(a, b))
                    continue;

                if (TrySegmentIntersectionPoint(a.Start, a.End, b.Start, b.End, out SKPoint intersection))
                    Consider(intersection, "intersection");
            }
        }

        snapped = bestPoint;
        snapKind = bestKind;
        return found;
    }

    private void SetSnapPreview(SKPoint? point, string kind = "")
    {
        bool changed = (_snapPreview.HasValue != point.HasValue) ||
                       (_snapPreview.HasValue && point.HasValue &&
                        DistanceSquared(_snapPreview.Value, point.Value) > 0.001f) ||
                       !string.Equals(_snapPreviewKind, kind, StringComparison.OrdinalIgnoreCase);
        _snapPreview = point;
        _snapPreviewKind = point.HasValue ? kind : "";
        if (changed)
            RequestRepaint();
    }

    private static SKPoint Midpoint(SKPoint a, SKPoint b) =>
        new((a.X + b.X) / 2f, (a.Y + b.Y) / 2f);

    private static IReadOnlyList<SKPoint> AnnotationSnapPoints(PageAnnotation annotation)
    {
        if (annotation.Points.Count < 2)
            return annotation.Points;

        string kind = OurPlaneCoreJobStore.NormalizePageAnnotationKind(annotation.Kind);
        if (kind != "rectangle")
            return annotation.Points.Take(2).ToList();

        if (annotation.Points.Count >= 4)
            return annotation.Points.Append(annotation.Points[0]).ToList();

        SKRect rect = NormalizeRect(annotation.Points[0], annotation.Points[1]);
        return
        [
            new SKPoint(rect.Left, rect.Top),
            new SKPoint(rect.Right, rect.Top),
            new SKPoint(rect.Right, rect.Bottom),
            new SKPoint(rect.Left, rect.Bottom),
            new SKPoint(rect.Left, rect.Top),
        ];
    }

    private static bool SharesEndpoint(SnapSegment left, SnapSegment right) =>
        DistanceSquared(left.Start, right.Start) <= 0.0001f ||
        DistanceSquared(left.Start, right.End) <= 0.0001f ||
        DistanceSquared(left.End, right.Start) <= 0.0001f ||
        DistanceSquared(left.End, right.End) <= 0.0001f;

    private sealed record SnapSegment(SKPoint Start, SKPoint End);

}
