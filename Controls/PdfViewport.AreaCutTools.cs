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
        if (target == null || !_measurementSet.Contains(target) || !PointInMeasurementFill(target, center))
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
        if (target == null || !_measurementSet.Contains(target))
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
        NotifyMeasurementsChanged([target]);
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

        SKRect pointRect = SKRect.Create(point.X, point.Y, 0, 0);
        IReadOnlyList<Measurement> candidates = ActivePageMeasurementsNear(pointRect);
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            Measurement candidate = candidates[i];
            if (candidate.MType == "area" &&
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
}
