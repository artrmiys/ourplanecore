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
using OurPlanCore;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace OurPlanCore.Controls;

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
            : ResolveAreaCutTargets(cutShape);
        List<Measurement> lineTargets = useSelection
            ? selectedTargets.Where(measurement => measurement.MType == "line").ToList()
            : ActivePageMeasurements()
                .Where(measurement => measurement.MType == "line" && LineTouchesCutShape(measurement, cutShape))
                .ToList();

        var beforeAreaPoints = new Dictionary<Measurement, List<SKPoint>>();
        var beforeAreaHoles = new Dictionary<Measurement, List<List<SKPoint>>>();
        var beforeAreaExtraJoists = new Dictionary<Measurement, List<JoistExtraSegment>>();
        var changedAreas = new List<Measurement>();
        var removedAreaIndexes = new Dictionary<Measurement, int>();
        var removedAreas = new List<Measurement>();
        var addedAreas = new List<Measurement>();
        string firstAreaError = "";
        foreach (Measurement area in areaTargets)
        {
            if (!TryBuildAreaCutGeometries(area, cutShape, out List<AreaBooleanGeometry> geometries, out string error))
            {
                firstAreaError = string.IsNullOrWhiteSpace(firstAreaError) ? error : firstAreaError;
                continue;
            }

            IReadOnlyList<List<JoistExtraSegment>> clippedExtraJoists =
                JoistTakeoffCalculator.ClipExtraJoistsToAreaGeometries(area.ExtraJoists, geometries);

            if (geometries.Count == 1)
            {
                beforeAreaPoints[area] = area.Points.ToList();
                beforeAreaHoles[area] = CloneHoles(area.Holes);
                beforeAreaExtraJoists[area] = CloneExtraJoists(area.ExtraJoists);
                ApplyAreaGeometry(area, geometries[0], clippedExtraJoists[0]);
                changedAreas.Add(area);
                continue;
            }

            int index = _measurements.IndexOf(area);
            if (index < 0)
                continue;

            removedAreaIndexes[area] = index;
            removedAreas.Add(area);
            for (int geometryIndex = 0; geometryIndex < geometries.Count; geometryIndex++)
            {
                addedAreas.Add(CloneAreaMeasurement(
                    area,
                    geometries[geometryIndex],
                    clippedExtraJoists[geometryIndex]));
            }
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

        if (changedAreas.Count == 0 && removedAreas.Count == 0 && removedLines.Count == 0)
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

        foreach (Measurement removed in removedAreas)
        {
            _measurements.Remove(removed);
            _measurementSet.Remove(removed);
            RemoveMeasurementFromPageIndex(removed);
            ForgetMeasurementState(removed);
        }

        foreach (Measurement removed in removedLines)
        {
            _measurements.Remove(removed);
            _measurementSet.Remove(removed);
            RemoveMeasurementFromPageIndex(removed);
            ForgetMeasurementState(removed);
        }

        foreach (Measurement added in addedAreas)
        {
            _measurements.Add(added);
            _measurementSet.Add(added);
            IndexMeasurementByPage(added);
        }

        foreach (Measurement added in addedLines)
        {
            _measurements.Add(added);
            _measurementSet.Add(added);
            IndexMeasurementByPage(added);
        }

        var removedMeasurementIndexes = new Dictionary<Measurement, int>(removedAreaIndexes);
        foreach (var pair in removedLineIndexes)
            removedMeasurementIndexes[pair.Key] = pair.Value;
        var addedMeasurements = addedAreas.Concat(addedLines).ToList();
        PushMixedMeasurementUndo(
            beforeAreaPoints,
            beforeAreaHoles,
            beforeAreaExtraJoists,
            removedMeasurementIndexes,
            addedMeasurements,
            "cut measurements",
            "cut");

        _drawPts.Clear();
        _rubberEnd = null;
        _areaCutMeasurement = null;
        if (addedMeasurements.Count > 0)
            SetSelectedMeasurements(addedMeasurements, addedMeasurements.LastOrDefault(), -1);
        else if (addedLines.Count > 0)
            SetSelectedMeasurements(addedLines, addedLines.LastOrDefault(), -1);
        else
            SetSelectedMeasurements(changedAreas, changedAreas.LastOrDefault(), -1);

        NotifyMeasurementsChanged(changedAreas);
        NotifyMeasurementsRemoved(removedAreas.Concat(removedLines).ToList());
        NotifyMeasurementsAdded(addedMeasurements);
        RequestRepaint();
        PostStatus(FormatCutStatus(shapeName, changedAreas.Count, removedAreas.Count, addedAreas.Count, removedLines.Count, addedLines.Count));
        PostRecordPrompt();
    }

    private List<Measurement> ResolveAreaCutTargets(IReadOnlyList<SKPoint> cutShape)
    {
        if (_areaCutMeasurement != null &&
            _measurementSet.Contains(_areaCutMeasurement) &&
            IsMeasurementOnActivePage(_areaCutMeasurement))
        {
            return [_areaCutMeasurement];
        }

        SKPoint point = Centroid(cutShape.ToList());
        return TryResolveAreaCutTarget(point, out Measurement target, out _)
            ? [target]
            : ResolveAreaCutTargetsByIntersection(cutShape);
    }

    private List<Measurement> ResolveAreaCutTargetsByIntersection(IReadOnlyList<SKPoint> cutShape)
    {
        SKRect bounds = PointsBounds(cutShape);
        IReadOnlyList<Measurement> candidates = ActivePageMeasurementsNear(bounds);
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            Measurement candidate = candidates[i];
            if (candidate.MType == "area" &&
                TryBuildAreaCutGeometry(candidate, cutShape, out _, out _))
            {
                return [candidate];
            }
        }

        return [];
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

    private static bool TryBuildAreaCutGeometry(
        Measurement target,
        IReadOnlyList<SKPoint> cutShape,
        out AreaBooleanGeometry geometry,
        out string error)
    {
        geometry = new AreaBooleanGeometry([], []);
        error = "";
        if (target.MType != "area" || target.Points.Count < 3)
        {
            error = "Cut: select an Area first.";
            return false;
        }

        if (cutShape.Count < 3)
        {
            error = "Cut: draw a larger shape.";
            return false;
        }

        if (CanUseExactAreaCutHole(target, cutShape))
        {
            List<List<SKPoint>> holes = CloneHoles(target.Holes);
            holes.Add(CleanPolygon(cutShape));
            geometry = new AreaBooleanGeometry(target.Points.Select(ClonePoint).ToList(), holes);
            return true;
        }

        // Everything the fast path can't take goes through the boolean subtract:
        // it handles convex AND concave (freehand) cut shapes, interior holes,
        // edge bites, and cuts that overlap or enclose existing holes. The path
        // is built from the outer contour plus every existing hole, so a cutter
        // that crosses existing holes merges with them (their union) instead of
        // double-counting. SkiaSharp path ops do not require a convex cutter, so
        // there is no convexity gate here.
        if (!MeasurementAreaBooleanService.TrySubtract(target, cutShape, out geometry, out error))
            return false;

        // Reject a cut that changed nothing — the cutter missed the area or lies
        // entirely within an existing hole. An effective cut strictly reduces the
        // filled area, so equal-or-greater area means nothing was removed.
        if (!AreaCutReducedFilledArea(target, geometry))
        {
            error = "Cut: no selected Area or Line was touched.";
            return false;
        }

        return geometry.Points.Count >= 3;
    }

    private static bool TryBuildAreaCutGeometries(
        Measurement target,
        IReadOnlyList<SKPoint> cutShape,
        out List<AreaBooleanGeometry> geometries,
        out string error)
    {
        geometries = [];
        error = "";
        if (TryBuildAreaCutGeometry(target, cutShape, out AreaBooleanGeometry single, out string singleError))
        {
            geometries = [single];
            return true;
        }

        // The single subtract failed — usually because the cut splits the area
        // into separate islands. Fall back to the multi-result subtract, which
        // also works for convex and concave (freehand) cut shapes.
        if (!MeasurementAreaBooleanService.TrySubtractAll(target, cutShape, out geometries, out error))
            return false;

        if (geometries.Count < 2)
        {
            error = string.IsNullOrWhiteSpace(singleError) ? error : singleError;
            return false;
        }

        return true;
    }

    private static void ApplyAreaGeometry(Measurement area, AreaBooleanGeometry geometry)
    {
        List<JoistExtraSegment> retainedExtraJoists = area.ExtraJoists
            .Where(extra => PointInAreaGeometry(ExtraJoistMidpoint(extra), geometry))
            .Select(CloneExtraJoist)
            .ToList();
        ApplyAreaGeometry(area, geometry, retainedExtraJoists);
    }

    private static void ApplyAreaGeometry(
        Measurement area,
        AreaBooleanGeometry geometry,
        IReadOnlyList<JoistExtraSegment> extraJoists)
    {
        area.Points.Clear();
        area.Points.AddRange(geometry.Points.Select(ClonePoint));
        area.Holes.Clear();
        area.Holes.AddRange(geometry.Holes.Select(hole => hole.Select(ClonePoint).ToList()));
        RestoreExtraJoists(area.ExtraJoists, extraJoists);
    }

    private static bool PointInAreaGeometry(SKPoint point, AreaBooleanGeometry geometry)
    {
        if (!PointInPolygon(point, geometry.Points))
            return false;

        return !geometry.Holes.Any(hole => hole.Count >= 3 && PointInPolygon(point, hole));
    }

    private static bool CanUseExactAreaCutHole(Measurement target, IReadOnlyList<SKPoint> cutShape)
    {
        if (cutShape.Any(point => !PointInMeasurementFill(target, point)))
            return false;

        if (PolygonEdgesIntersect(cutShape, target.Points))
            return false;

        return !AreaCutOverlapsExistingHole(target, cutShape);
    }

    private static bool AreaCutOverlapsExistingHole(Measurement target, IReadOnlyList<SKPoint> cutShape)
    {
        foreach (var existingHole in target.Holes)
        {
            if (existingHole.Count < 3)
                continue;

            if (PolygonEdgesIntersect(cutShape, existingHole))
                return true;

            SKPoint cutProbe = Centroid(cutShape.ToList());
            if (PointInPolygon(cutProbe, existingHole))
                return true;

            SKPoint holeProbe = Centroid(existingHole);
            if (PointInPolygon(holeProbe, cutShape))
                return true;
        }

        return false;
    }

    // An effective area cut always removes fill, so a boolean result whose
    // filled area (outer minus every hole) is not smaller than the original
    // means the cutter missed the area or fell entirely inside an existing hole.
    private static bool AreaCutReducedFilledArea(Measurement target, AreaBooleanGeometry geometry)
    {
        double before = FilledArea(target.Points, target.Holes);
        double after = FilledArea(geometry.Points, geometry.Holes);
        return before - after > ViewportConstants.GeometryEpsilon;
    }

    private static double FilledArea(IReadOnlyList<SKPoint> outer, IReadOnlyList<List<SKPoint>> holes)
    {
        double area = PolygonArea(outer);
        foreach (List<SKPoint> hole in holes)
            area -= PolygonArea(hole);
        return area;
    }

    private static float SignedPolygonArea(IReadOnlyList<SKPoint> polygon)
    {
        float area = 0;
        for (int i = 0; i < polygon.Count; i++)
        {
            SKPoint a = polygon[i];
            SKPoint b = polygon[(i + 1) % polygon.Count];
            area += a.X * b.Y - b.X * a.Y;
        }

        return area / 2f;
    }

    private static double PolygonArea(IReadOnlyList<SKPoint> polygon) =>
        Math.Abs(SignedPolygonArea(polygon));

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

    private static List<SKPoint> CleanPolygon(IReadOnlyList<SKPoint> points)
    {
        var clean = new List<SKPoint>();
        foreach (SKPoint point in points)
            AddDistinctPoint(clean, point);

        if (clean.Count > 1 && SamePoint(clean[0], clean[^1]))
            clean.RemoveAt(clean.Count - 1);

        if (clean.Count < 3)
            return clean;

        bool removed;
        do
        {
            removed = false;
            for (int i = 0; i < clean.Count && clean.Count >= 3; i++)
            {
                SKPoint previous = clean[(i - 1 + clean.Count) % clean.Count];
                SKPoint current = clean[i];
                SKPoint next = clean[(i + 1) % clean.Count];
                if (Math.Abs(Cross(previous, current, next)) <= ViewportConstants.GeometryEpsilon &&
                    DistanceToSegment(current, previous, next) <= ViewportConstants.GeometryEpsilon)
                {
                    clean.RemoveAt(i);
                    removed = true;
                    break;
                }
            }
        }
        while (removed);

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

    private static Measurement CloneAreaMeasurement(
        Measurement source,
        AreaBooleanGeometry geometry,
        ISet<JoistExtraSegment>? claimedExtraJoists = null)
    {
        List<JoistExtraSegment> extraJoists = source.ExtraJoists
            .Where(extra => PointInAreaGeometry(ExtraJoistMidpoint(extra), geometry))
            .Where(extra => claimedExtraJoists == null || claimedExtraJoists.Add(extra))
            .Select(CloneExtraJoist)
            .ToList();

        return CloneAreaMeasurement(source, geometry, extraJoists);
    }

    private static Measurement CloneAreaMeasurement(
        Measurement source,
        AreaBooleanGeometry geometry,
        IReadOnlyList<JoistExtraSegment> extraJoists)
    {
        return new Measurement
        {
            Name = source.Name,
            Notes = source.Notes,
            MType = "area",
            Points = geometry.Points.Select(ClonePoint).ToList(),
            Holes = geometry.Holes.Select(hole => hole.Select(ClonePoint).ToList()).ToList(),
            Color = source.Color,
            CountSymbol = source.CountSymbol,
            PageFolder = source.PageFolder,
            TakeoffFolder = source.TakeoffFolder,
            ScaleMetersPerPt = source.ScaleMetersPerPt,
            JoistEnabled = source.JoistEnabled,
            JoistType = source.JoistType,
            JoistSpacingInches = source.JoistSpacingInches,
            JoistDirectionDegrees = source.JoistDirectionDegrees,
            JoistDirectionLocked = source.JoistDirectionLocked,
            JoistDirectionFollowsAreaRotation = source.JoistDirectionFollowsAreaRotation,
            JoistAddEndJoist = source.JoistAddEndJoist,
            JoistStartEdgeEnabled = source.JoistStartEdgeEnabled,
            JoistEndEdgeEnabled = source.JoistEndEdgeEnabled,
            JoistEdgeOverridesSet = source.JoistEdgeOverridesSet,
            JoistPitch = source.JoistPitch,
            JoistLengthRounding = source.JoistLengthRounding,
            JoistShowLabels = source.JoistShowLabels,
            JoistDetailedLabels = source.JoistDetailedLabels,
            ExtraJoists = CloneExtraJoists(extraJoists),
        };
    }

    private static string FormatCutStatus(
        string shapeName,
        int areaCount,
        int splitAreaCount,
        int addedAreaCount,
        int removedLineCount,
        int addedLineCount)
    {
        var parts = new List<string>();
        if (areaCount > 0)
            parts.Add($"{areaCount} area hole(s)");
        if (splitAreaCount > 0)
            parts.Add($"{splitAreaCount} area(s) split into {addedAreaCount} segment(s)");
        if (removedLineCount > 0)
            parts.Add($"{removedLineCount} line(s) into {addedLineCount} piece(s)");

        return $"Cut {shapeName}: {string.Join(", ", parts)}.";
    }
}
