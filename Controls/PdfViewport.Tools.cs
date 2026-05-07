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
            case ViewerTool.Line when BoxModeEnabled:
            case ViewerTool.Area when BoxModeEnabled:
                AddBoxMeasurementPoint(pdf);
                break;
            case ViewerTool.Line:
            case ViewerTool.Area:
                _drawPts.Add(pdf);
                RequestRepaint();
                PostRecordPrompt();
                break;
            case ViewerTool.AreaCut:
                AddAreaCutPoint(pdf);
                break;
        }
    }

    private void AddBoxMeasurementPoint(SKPoint pdf)
    {
        _drawPts.Add(pdf);
        if (_drawPts.Count < 2)
        {
            RequestRepaint();
            PostRecordPrompt();
            return;
        }

        SKPoint first = _drawPts[0];
        _drawPts.Clear();
        _drawPts.AddRange(BoxMeasurementPoints(first, pdf, closeForLine: _tool == ViewerTool.Line));
        FinalizeDrawing();
    }

    private void AddAreaCutPoint(SKPoint pdf)
    {
        if (_drawPts.Count == 0)
        {
            if (!TryResolveAreaCutTarget(pdf, out Measurement target, out string status))
            {
                PostStatus(status);
                return;
            }

            _areaCutMeasurement = target;
            SelectMeasurement(target, -1);
            _drawPts.Add(pdf);
            RequestRepaint();
            PostRecordPrompt();
            return;
        }

        if (BoxModeEnabled)
        {
            ApplyAreaCutBox(_drawPts[0], pdf);
            return;
        }

        _drawPts.Add(pdf);
        RequestRepaint();
        PostRecordPrompt();
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

    private void ApplyAreaCutBox(SKPoint first, SKPoint second)
    {
        SKRect rect = NormalizeRect(first, second);
        float minSize = ScreenToPdfDistance(4f);
        if (rect.Width < minSize || rect.Height < minSize)
        {
            _drawPts.Clear();
            _rubberEnd = null;
            PostStatus("Area Cut: box is too small.");
            RequestRepaint();
            return;
        }

        SKPoint center = new((rect.Left + rect.Right) / 2f, (rect.Top + rect.Bottom) / 2f);
        Measurement? target = _areaCutMeasurement;
        if (target == null || !_measurements.Contains(target) || !PointInMeasurementFill(target, center))
        {
            TryResolveAreaCutTarget(center, out target, out _);
        }

        if (target == null)
        {
            _drawPts.Clear();
            _rubberEnd = null;
            _areaCutMeasurement = null;
            PostStatus("Area Cut: draw the cut box inside a selected Area.");
            RequestRepaint();
            return;
        }

        var hole = BoxMeasurementPoints(first, second, closeForLine: false);
        ApplyAreaCutHole(target, hole, "box");
    }

    private void FinalizeAreaCutPolygon()
    {
        if (_drawPts.Count < 3)
        {
            CancelDrawing(clearSelection: false);
            PostStatus("Area Cut cancelled.");
            return;
        }

        Measurement? target = _areaCutMeasurement;
        if (target == null || !_measurements.Contains(target))
            TryResolveAreaCutTarget(Centroid(_drawPts), out target, out _);

        if (target == null)
        {
            _drawPts.Clear();
            _rubberEnd = null;
            _areaCutMeasurement = null;
            PostStatus("Area Cut: draw the cut polygon inside a selected Area.");
            RequestRepaint();
            return;
        }

        ApplyAreaCutHole(target, _drawPts.ToList(), "polygon");
    }

    private void ApplyAreaCutHole(Measurement target, List<SKPoint> hole, string shapeName)
    {
        if (!CanApplyAreaCut(target, hole, out string error))
        {
            _drawPts.Clear();
            _rubberEnd = null;
            PostStatus(error);
            RequestRepaint();
            return;
        }

        List<SKPoint> beforePoints = target.Points.ToList();
        List<List<SKPoint>> beforeHoles = CloneHoles(target.Holes);
        target.Holes.Add(hole);
        PushMeasurementUndoSnapshot(target, beforePoints, beforeHoles, "cut area hole", "area-cut");
        _drawPts.Clear();
        _rubberEnd = null;
        _areaCutMeasurement = null;
        SelectMeasurement(target, -1);
        MeasurementChanged?.Invoke(target);
        RequestRepaint();
        PostStatus($"Area Cut: subtracted {shapeName}. New area {target.Label(ScaleMetersPerPt, UnitMode)}.");
        PostRecordPrompt();
    }

    private bool TryResolveAreaCutTarget(SKPoint point, out Measurement target, out string status)
    {
        target = null!;
        status = "";
        if (_selectedMeasurement is { MType: "area" } selected &&
            IsMeasurementOnActivePage(selected) &&
            PointInMeasurementFill(selected, point))
        {
            target = selected;
            return true;
        }

        for (int i = _measurements.Count - 1; i >= 0; i--)
        {
            Measurement candidate = _measurements[i];
            if (candidate.MType == "area" &&
                IsMeasurementOnActivePage(candidate) &&
                PointInMeasurementFill(candidate, point))
            {
                target = candidate;
                return true;
            }
        }

        status = "Area Cut: click inside the Area you want to cut.";
        return false;
    }

    private static bool CanApplyAreaCut(Measurement target, IReadOnlyList<SKPoint> hole, out string error)
    {
        error = "";
        if (target.MType != "area" || target.Points.Count < 3)
        {
            error = "Area Cut: select an Area first.";
            return false;
        }

        if (hole.Count < 3)
        {
            error = "Area Cut: draw a larger box.";
            return false;
        }

        foreach (SKPoint point in hole)
        {
            if (!PointInMeasurementFill(target, point))
            {
                error = "Area Cut: cut box must stay inside the Area and outside existing holes.";
                return false;
            }
        }

        if (PolygonEdgesIntersect(hole, target.Points))
        {
            error = "Area Cut: cut box cannot cross the Area edge.";
            return false;
        }

        foreach (var existingHole in target.Holes)
        {
            if (existingHole.Count >= 3 && PolygonEdgesIntersect(hole, existingHole))
            {
                error = "Area Cut: cut box cannot overlap an existing hole.";
                return false;
            }
        }

        return true;
    }

    private static List<SKPoint> BoxMeasurementPoints(SKPoint first, SKPoint second, bool closeForLine)
    {
        SKRect rect = NormalizeRect(first, second);
        var points = new List<SKPoint>
        {
            new(rect.Left, rect.Top),
            new(rect.Right, rect.Top),
            new(rect.Right, rect.Bottom),
            new(rect.Left, rect.Bottom),
        };
        if (closeForLine)
            points.Add(points[0]);
        return points;
    }

    private static bool PolygonEdgesIntersect(IReadOnlyList<SKPoint> left, IReadOnlyList<SKPoint> right)
    {
        foreach ((SKPoint a, SKPoint b) in PolygonEdges(left))
        foreach ((SKPoint c, SKPoint d) in PolygonEdges(right))
        {
            if (SegmentsIntersect(a, b, c, d))
                return true;
        }

        return false;
    }

    private static IEnumerable<(SKPoint Start, SKPoint End)> PolygonEdges(IReadOnlyList<SKPoint> points)
    {
        if (points.Count < 2)
            yield break;

        for (int i = 1; i < points.Count; i++)
            yield return (points[i - 1], points[i]);
        if (points.Count > 2)
            yield return (points[^1], points[0]);
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
        IndexMeasurementByPage(m);
        PushAddedMeasurementsUndo([m], $"remove added {EntryTitle(m.MType)}");
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
            PostStatus("Area Cut cancelled.");
            return;
        }

        CancelDrawing(clearSelection: _tool != ViewerTool.AreaCut);
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

                if (BoxModeEnabled)
                {
                    PostStatus(_drawPts.Count == 0
                        ? $"Line Box: click the first corner.{modes}"
                        : $"Line Box: click the opposite corner to create a rectangular perimeter.{modes}");
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

                if (BoxModeEnabled)
                {
                    PostStatus(_drawPts.Count == 0
                        ? $"Area Box: click the first corner.{modes}"
                        : $"Area Box: click the opposite corner to create a rectangular area.{modes}");
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
            case ViewerTool.AreaCut:
                if (BoxModeEnabled)
                {
                    PostStatus(_drawPts.Count == 0
                        ? $"Area Cut Box: click inside the selected Area at the first cut-box corner.{modes}"
                        : $"Area Cut Box: click the opposite cut-box corner.{modes}");
                }
                else
                {
                    PostStatus(_drawPts.Count switch
                    {
                        0 => $"Area Cut: click the first polygon point inside the selected Area.{modes}",
                        1 => $"Area Cut: click the next polygon point.{modes}",
                        2 => $"Area Cut: click at least one more point, then C / Esc / double-click to finish.{modes}",
                        _ => $"Area Cut: click next point, or C / Esc / double-click to finish.{modes}",
                    });
                }
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
        if (BoxModeEnabled)
            modes.Add("Box");
        return modes.Count == 0 ? "" : $" [{string.Join(", ", modes)}]";
    }

    private static string ToolTitle(string type) =>
        type switch
        {
            "point" => "Count",
            "line" => "Line",
            "area" => "Area",
            "areacut" => "Area Cut",
            "dimension" => "Ruler",
            "arrow" => "Arrow",
            "rectangle" => "Box",
            "select" => "Select",
            _ => type,
        };

    private static string EntryTitle(string type) =>
        type == "point" ? "Count mark" : $"{ToolTitle(type)} section";

    private void CancelDrawing(bool clearSelection = true)
    {
        _drawPts.Clear();
        _scalePts.Clear();
        _rubberEnd = null;
        _boxSelecting = false;
        _areaCutMeasurement = null;
        SetSnapPreview(null);
        if (_draggingVertex && IsMouseCaptured)
            ReleaseMouseCapture();
        if (clearSelection)
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

        if (ShouldPreviewAsBox())
            return rawPdf;

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

    private bool ShouldPreviewAsBox() =>
        _tool == ViewerTool.DrawRect ||
        _tool == ViewerTool.AreaCut && BoxModeEnabled ||
        BoxModeEnabled && _tool is (ViewerTool.Line or ViewerTool.Area);

    private bool TryGetOrthoAnchor(out SKPoint anchor)
    {
        if (_joistDirectionMeasurement != null && _joistDirectionPts.Count > 0)
        {
            anchor = _joistDirectionPts[^1];
            return true;
        }

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
        float length = MeasurementGeometry.Distance(point, anchor);
        if (length <= ViewportConstants.ZeroLengthEpsilon)
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
        float tolerance = ScreenToPdfDistance(SnapToleranceScreenPx);
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
            if (i == _drawPts.Count - 1 && _tool is ViewerTool.Line or ViewerTool.Area or ViewerTool.AreaCut)
                continue;

            Consider(_drawPts[i], "endpoint");
        }
        ConsiderPolyline(_drawPts, _tool == ViewerTool.Area || _tool == ViewerTool.AreaCut && !BoxModeEnabled, includeEndpoints: false);

        foreach (SKPoint point in _scalePts)
            Consider(point, "endpoint");

        foreach (Measurement measurement in _measurements)
        {
            if (!IsMeasurementOnActivePage(measurement))
                continue;

            if (measurement.MType is "line" or "area")
            {
                ConsiderPolyline(measurement.Points, measurement.MType == "area", includeEndpoints: true);
                if (measurement.MType == "area")
                    foreach (var hole in measurement.Holes)
                        ConsiderPolyline(hole, closed: true, includeEndpoints: true);
            }
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
        DistanceSquared(left.Start, right.Start) <= ViewportConstants.GeometryEpsilon ||
        DistanceSquared(left.Start, right.End) <= ViewportConstants.GeometryEpsilon ||
        DistanceSquared(left.End, right.Start) <= ViewportConstants.GeometryEpsilon ||
        DistanceSquared(left.End, right.End) <= ViewportConstants.GeometryEpsilon;

    private sealed record SnapSegment(SKPoint Start, SKPoint End);

}
