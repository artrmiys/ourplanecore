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
            PostStatus("Cut: box is too small.");
            RequestRepaint();
            return;
        }

        ApplyCutShape(BoxMeasurementPoints(first, second, closeForLine: false), "box");
    }

    private void FinalizeAreaCutPolygon()
    {
        if (_drawPts.Count < 3)
        {
            CancelDrawing(clearSelection: false);
            PostStatus("Cut cancelled.");
            return;
        }

        ApplyCutShape(_drawPts.ToList(), "polygon");
    }

    private void ApplyCutShape(IReadOnlyList<SKPoint> cutShape, string shapeName)
    {
        if (cutShape.Count < 3)
        {
            _drawPts.Clear();
            _rubberEnd = null;
            _areaCutMeasurement = null;
            PostStatus("Cut: draw a larger cut shape.");
            RequestRepaint();
            return;
        }

        List<Measurement> selectedTargets = GetSelectedMeasurements()
            .Where(measurement => IsMeasurementOnActivePage(measurement) &&
                                  measurement.MType is "area" or "line")
            .ToList();
        bool useSelection = selectedTargets.Count > 0;

        List<Measurement> areaTargets = useSelection
            ? selectedTargets.Where(measurement => measurement.MType == "area").ToList()
            : ResolveAreaCutTargets(Centroid(cutShape.ToList()));
        List<Measurement> lineTargets = useSelection
            ? selectedTargets.Where(measurement => measurement.MType == "line").ToList()
            : ActivePageMeasurements()
                .Where(measurement => measurement.MType == "line" && LineTouchesCutShape(measurement, cutShape))
                .ToList();

        var beforeAreaPoints = new Dictionary<Measurement, List<SKPoint>>();
        var beforeAreaHoles = new Dictionary<Measurement, List<List<SKPoint>>>();
        var changedAreas = new List<Measurement>();
        string firstAreaError = "";
        foreach (Measurement area in areaTargets)
        {
            if (!CanApplyAreaCut(area, cutShape, out string error))
            {
                firstAreaError = string.IsNullOrWhiteSpace(firstAreaError) ? error : firstAreaError;
                continue;
            }

            beforeAreaPoints[area] = area.Points.ToList();
            beforeAreaHoles[area] = CloneHoles(area.Holes);
            area.Holes.Add(cutShape.Select(point => new SKPoint(point.X, point.Y)).ToList());
            changedAreas.Add(area);
        }

        var removedLineIndexes = new Dictionary<Measurement, int>();
        var removedLines = new List<Measurement>();
        var addedLines = new List<Measurement>();
        foreach (Measurement line in lineTargets)
        {
            List<List<SKPoint>> pieces = CutLinePiecesByPolygon(line.Points, cutShape);
            if (LineCutLeavesOriginal(line.Points, pieces))
                continue;

            int index = _measurements.IndexOf(line);
            if (index < 0)
                continue;

            removedLineIndexes[line] = index;
            removedLines.Add(line);
            foreach (List<SKPoint> piece in pieces)
                addedLines.Add(CloneLineMeasurement(line, piece));
        }

        if (changedAreas.Count == 0 && removedLines.Count == 0)
        {
            _drawPts.Clear();
            _rubberEnd = null;
            _areaCutMeasurement = null;
            PostStatus(string.IsNullOrWhiteSpace(firstAreaError)
                ? "Cut: no selected Area or Line was touched."
                : firstAreaError);
            RequestRepaint();
            return;
        }

        foreach (Measurement removed in removedLines)
        {
            _measurements.Remove(removed);
            _measurementSet.Remove(removed);
            RemoveMeasurementFromPageIndex(removed);
            ForgetMeasurementState(removed);
        }

        foreach (Measurement added in addedLines)
        {
            _measurements.Add(added);
            _measurementSet.Add(added);
            IndexMeasurementByPage(added);
        }

        PushMixedMeasurementUndo(
            beforeAreaPoints,
            beforeAreaHoles,
            removedLineIndexes,
            addedLines,
            "cut measurements",
            "cut");

        _drawPts.Clear();
        _rubberEnd = null;
        _areaCutMeasurement = null;
        if (addedLines.Count > 0)
            SetSelectedMeasurements(addedLines, addedLines.LastOrDefault(), -1);
        else
            SetSelectedMeasurements(changedAreas, changedAreas.LastOrDefault(), -1);

        NotifyMeasurementsChanged(changedAreas);
        NotifyMeasurementsRemoved(removedLines);
        NotifyMeasurementsAdded(addedLines);
        RequestRepaint();
        PostStatus(FormatCutStatus(shapeName, changedAreas.Count, removedLines.Count, addedLines.Count));
        PostRecordPrompt();
    }

    private List<Measurement> ResolveAreaCutTargets(SKPoint point)
    {
        if (_areaCutMeasurement != null &&
            _measurementSet.Contains(_areaCutMeasurement) &&
            IsMeasurementOnActivePage(_areaCutMeasurement))
        {
            return [_areaCutMeasurement];
        }

        return TryResolveAreaCutTarget(point, out Measurement target, out _)
            ? [target]
            : [];
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

        status = "Cut: click inside the Area you want to cut.";
        return false;
    }

    private static bool CanApplyAreaCut(Measurement target, IReadOnlyList<SKPoint> hole, out string error)
    {
        error = "";
        if (target.MType != "area" || target.Points.Count < 3)
        {
            error = "Cut: select an Area first.";
            return false;
        }

        if (hole.Count < 3)
        {
            error = "Cut: draw a larger shape.";
            return false;
        }

        foreach (SKPoint point in hole)
        {
            if (!PointInMeasurementFill(target, point))
            {
                error = "Cut: area cut must stay inside the Area and outside existing holes.";
                return false;
            }
        }

        if (PolygonEdgesIntersect(hole, target.Points))
        {
            error = "Cut: area cut cannot cross the Area edge.";
            return false;
        }

        foreach (var existingHole in target.Holes)
        {
            if (existingHole.Count >= 3 && PolygonEdgesIntersect(hole, existingHole))
            {
                error = "Cut: area cut cannot overlap an existing hole.";
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

    private static bool LineTouchesCutShape(Measurement line, IReadOnlyList<SKPoint> cutShape) =>
        !LineCutLeavesOriginal(line.Points, CutLinePiecesByPolygon(line.Points, cutShape));

    private static List<List<SKPoint>> CutLinePiecesByPolygon(
        IReadOnlyList<SKPoint> points,
        IReadOnlyList<SKPoint> cutShape)
    {
        var pieces = new List<List<SKPoint>>();
        if (points.Count < 2 || cutShape.Count < 3)
            return pieces;

        List<SKPoint>? current = null;
        for (int i = 1; i < points.Count; i++)
        {
            SKPoint start = points[i - 1];
            SKPoint end = points[i];
            if (DistanceSquared(start, end) <= ViewportConstants.GeometryEpsilon)
                continue;

            List<float> cuts = SegmentCutParameters(start, end, cutShape);
            bool keptToSegmentEnd = false;
            for (int c = 1; c < cuts.Count; c++)
            {
                float t0 = cuts[c - 1];
                float t1 = cuts[c];
                if (t1 - t0 <= ViewportConstants.GeometryEpsilon)
                    continue;

                float mid = (t0 + t1) / 2f;
                if (PointInCutShape(PointAt(start, end, mid), cutShape))
                    continue;

                SKPoint pieceStart = PointAt(start, end, t0);
                SKPoint pieceEnd = PointAt(start, end, t1);
                if (current == null || !SamePoint(current[^1], pieceStart))
                {
                    current = [pieceStart];
                    pieces.Add(current);
                }

                AddDistinctPoint(current, pieceEnd);
                keptToSegmentEnd = t1 >= 1f - ViewportConstants.GeometryEpsilon;
            }

            if (!keptToSegmentEnd)
                current = null;
        }

        return pieces
            .Select(CleanLinePiece)
            .Where(piece => piece.Count >= 2 && LinePieceLength(piece) > ViewportConstants.GeometryEpsilon)
            .ToList();
    }

    private static List<float> SegmentCutParameters(SKPoint start, SKPoint end, IReadOnlyList<SKPoint> cutShape)
    {
        var result = new List<float> { 0f, 1f };
        foreach ((SKPoint edgeStart, SKPoint edgeEnd) in PolygonEdges(cutShape))
        {
            if (TrySegmentIntersectionParameter(start, end, edgeStart, edgeEnd, out float t))
                AddDistinctParameter(result, Math.Clamp(t, 0f, 1f));
        }

        result.Sort();
        return result;
    }

    private static bool TrySegmentIntersectionParameter(
        SKPoint start,
        SKPoint end,
        SKPoint edgeStart,
        SKPoint edgeEnd,
        out float t)
    {
        t = 0f;
        float rx = end.X - start.X;
        float ry = end.Y - start.Y;
        float sx = edgeEnd.X - edgeStart.X;
        float sy = edgeEnd.Y - edgeStart.Y;
        float denom = rx * sy - ry * sx;
        if (Math.Abs(denom) <= ViewportConstants.GeometryEpsilon)
            return false;

        float qpx = edgeStart.X - start.X;
        float qpy = edgeStart.Y - start.Y;
        float candidateT = (qpx * sy - qpy * sx) / denom;
        float u = (qpx * ry - qpy * rx) / denom;
        const float eps = ViewportConstants.GeometryEpsilon;
        if (candidateT < -eps || candidateT > 1f + eps || u < -eps || u > 1f + eps)
            return false;

        t = candidateT;
        return true;
    }

    private static bool PointInCutShape(SKPoint point, IReadOnlyList<SKPoint> cutShape)
    {
        if (PointInPolygon(point, cutShape))
            return true;

        foreach ((SKPoint start, SKPoint end) in PolygonEdges(cutShape))
        {
            if (DistanceToSegment(point, start, end) <= ViewportConstants.GeometryEpsilon)
                return true;
        }

        return false;
    }

    private static SKPoint PointAt(SKPoint start, SKPoint end, float t) =>
        new(
            start.X + (end.X - start.X) * t,
            start.Y + (end.Y - start.Y) * t);

    private static void AddDistinctParameter(List<float> values, float value)
    {
        if (values.Any(existing => Math.Abs(existing - value) <= ViewportConstants.GeometryEpsilon))
            return;

        values.Add(value);
    }

    private static void AddDistinctPoint(List<SKPoint> points, SKPoint point)
    {
        if (points.Count == 0 || !SamePoint(points[^1], point))
            points.Add(point);
    }

    private static List<SKPoint> CleanLinePiece(IReadOnlyList<SKPoint> points)
    {
        var clean = new List<SKPoint>();
        foreach (SKPoint point in points)
            AddDistinctPoint(clean, point);
        return clean;
    }

    private static double LinePieceLength(IReadOnlyList<SKPoint> points)
    {
        double total = 0;
        for (int i = 1; i < points.Count; i++)
            total += Math.Sqrt(DistanceSquared(points[i - 1], points[i]));
        return total;
    }

    private static bool SamePoint(SKPoint left, SKPoint right) =>
        DistanceSquared(left, right) <= ViewportConstants.GeometryEpsilon;

    private static bool LineCutLeavesOriginal(IReadOnlyList<SKPoint> original, IReadOnlyList<List<SKPoint>> pieces)
    {
        if (pieces.Count != 1 || pieces[0].Count != original.Count)
            return false;

        for (int i = 0; i < original.Count; i++)
        {
            if (!SamePoint(original[i], pieces[0][i]))
                return false;
        }

        return true;
    }

    private static Measurement CloneLineMeasurement(Measurement source, List<SKPoint> points) =>
        new()
        {
            Name = source.Name,
            Notes = source.Notes,
            MType = "line",
            Points = points.Select(point => new SKPoint(point.X, point.Y)).ToList(),
            Color = source.Color,
            CountSymbol = source.CountSymbol,
            PageFolder = source.PageFolder,
            TakeoffFolder = source.TakeoffFolder,
            ScaleMetersPerPt = source.ScaleMetersPerPt,
        };

    private static string FormatCutStatus(string shapeName, int areaCount, int removedLineCount, int addedLineCount)
    {
        var parts = new List<string>();
        if (areaCount > 0)
            parts.Add($"{areaCount} area hole(s)");
        if (removedLineCount > 0)
            parts.Add($"{removedLineCount} line(s) into {addedLineCount} piece(s)");

        return $"Cut {shapeName}: {string.Join(", ", parts)}.";
    }
}
