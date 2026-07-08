using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace OurPlaneCore;

public sealed record AreaBooleanGeometry(
    List<SKPoint> Points,
    List<List<SKPoint>> Holes);

public static class MeasurementAreaBooleanService
{
    private const float AreaEpsilon = 0.0001f;

    public static bool TrySubtract(
        Measurement area,
        IReadOnlyList<SKPoint> cutter,
        out AreaBooleanGeometry geometry,
        out string error)
    {
        geometry = new AreaBooleanGeometry([], []);
        error = "";
        if (OurPlaneCoreJobStore.NormalizeMeasurementType(area.MType) != "area" ||
            area.Points.Count < 3 ||
            cutter.Count < 3)
        {
            error = "Cut: select an Area and draw a larger cut shape.";
            return false;
        }

        using SKPath areaPath = BuildMeasurementPath(area);
        using SKPath cutterPath = BuildPolygonPath(cutter);
        using var resultPath = new SKPath { FillType = SKPathFillType.EvenOdd };
        if (!areaPath.Op(cutterPath, SKPathOp.Difference, resultPath))
        {
            error = "Cut: area boolean failed.";
            return false;
        }

        return TryReadSingleMeasurementGeometry(resultPath, out geometry, out error, "Cut");
    }

    public static bool TrySubtractAll(
        Measurement area,
        IReadOnlyList<SKPoint> cutter,
        out List<AreaBooleanGeometry> geometries,
        out string error)
    {
        geometries = [];
        error = "";
        if (OurPlaneCoreJobStore.NormalizeMeasurementType(area.MType) != "area" ||
            area.Points.Count < 3 ||
            cutter.Count < 3)
        {
            error = "Cut: select an Area and draw a larger cut shape.";
            return false;
        }

        using SKPath areaPath = BuildMeasurementPath(area);
        using SKPath cutterPath = BuildPolygonPath(cutter);
        using var resultPath = new SKPath { FillType = SKPathFillType.EvenOdd };
        if (!areaPath.Op(cutterPath, SKPathOp.Difference, resultPath))
        {
            error = "Cut: area boolean failed.";
            return false;
        }

        return TryReadMeasurementGeometries(resultPath, out geometries, out error, "Cut");
    }

    public static bool TryUnion(
        Measurement first,
        Measurement second,
        out AreaBooleanGeometry geometry,
        out string error)
    {
        geometry = new AreaBooleanGeometry([], []);
        error = "";
        if (OurPlaneCoreJobStore.NormalizeMeasurementType(first.MType) != "area" ||
            OurPlaneCoreJobStore.NormalizeMeasurementType(second.MType) != "area")
        {
            error = "Merge: Area union needs Area measurements.";
            return false;
        }

        if (!SamePage(first.PageFolder, second.PageFolder) ||
            !CompatibleScales(first.ScaleMetersPerPt, second.ScaleMetersPerPt))
        {
            error = "Merge: Area union needs the same page and compatible scale.";
            return false;
        }

        using SKPath firstPath = BuildMeasurementPath(first);
        using SKPath secondPath = BuildMeasurementPath(second);
        using var resultPath = new SKPath { FillType = SKPathFillType.EvenOdd };
        if (!firstPath.Op(secondPath, SKPathOp.Union, resultPath))
        {
            error = "Merge: Area union failed.";
            return false;
        }

        return TryReadSingleMeasurementGeometry(resultPath, out geometry, out error, "Merge");
    }

    /// <summary>
    /// Folds two or more area measurements with one Skia path op
    /// (Union / Intersect / Difference / Xor). Fold order is the list order,
    /// so for Difference the first area is the base and the rest are cutters.
    /// </summary>
    public static bool TryCombine(
        IReadOnlyList<Measurement> areas,
        SKPathOp op,
        out List<AreaBooleanGeometry> geometries,
        out string error)
    {
        geometries = [];
        if (!ValidateCombineInput(areas, out error))
            return false;

        SKPath result = BuildMeasurementPath(areas[0]);
        try
        {
            for (int i = 1; i < areas.Count; i++)
            {
                using SKPath other = BuildMeasurementPath(areas[i]);
                if (!TryOp(ref result, other, op, out error))
                    return false;
            }

            return TryReadMeasurementGeometries(result, out geometries, out error, "Combine");
        }
        finally
        {
            result.Dispose();
        }
    }

    /// <summary>
    /// Removes double counting between overlapping areas: the first area keeps
    /// priority, every later area gets everything already covered by earlier
    /// areas subtracted. Entry 0 is always null (unchanged); an empty list
    /// means the area was completely covered and should be removed.
    /// </summary>
    public static bool TryRemoveOverlap(
        IReadOnlyList<Measurement> areas,
        out List<List<AreaBooleanGeometry>?> trimmedPerArea,
        out string error)
    {
        trimmedPerArea = [];
        if (!ValidateCombineInput(areas, out error))
            return false;

        var results = new List<List<AreaBooleanGeometry>?> { null };
        SKPath covered = BuildMeasurementPath(areas[0]);
        try
        {
            for (int i = 1; i < areas.Count; i++)
            {
                using SKPath current = BuildMeasurementPath(areas[i]);
                using var trimmed = new SKPath();
                if (!current.Op(covered, SKPathOp.Difference, trimmed))
                {
                    error = "Combine: area boolean failed.";
                    return false;
                }

                if (!TryReadMeasurementGeometriesAllowEmpty(trimmed, out List<AreaBooleanGeometry> geoms, out error, "Combine"))
                    return false;

                results.Add(geoms);
                if (!TryOp(ref covered, current, SKPathOp.Union, out error))
                    return false;
            }
        }
        finally
        {
            covered.Dispose();
        }

        trimmedPerArea = results;
        return true;
    }

    /// <summary>
    /// Splits overlapping areas into disjoint pieces: each area keeps only its
    /// exclusive part (may be empty), and the region covered by two or more
    /// areas is returned separately as the shared geometry.
    /// </summary>
    public static bool TryDivide(
        IReadOnlyList<Measurement> areas,
        out List<List<AreaBooleanGeometry>> exclusivePerArea,
        out List<AreaBooleanGeometry> sharedGeometries,
        out string error)
    {
        exclusivePerArea = [];
        sharedGeometries = [];
        if (!ValidateCombineInput(areas, out error))
            return false;

        var paths = new List<SKPath>();
        SKPath? shared = null;
        try
        {
            foreach (Measurement area in areas)
                paths.Add(BuildMeasurementPath(area));

            var exclusives = new List<List<AreaBooleanGeometry>>();
            for (int i = 0; i < paths.Count; i++)
            {
                SKPath exclusive = new();
                exclusive.AddPath(paths[i]);
                exclusive.FillType = paths[i].FillType;
                try
                {
                    for (int j = 0; j < paths.Count; j++)
                    {
                        if (j == i)
                            continue;
                        if (!TryOp(ref exclusive, paths[j], SKPathOp.Difference, out error))
                            return false;
                    }

                    if (!TryReadMeasurementGeometriesAllowEmpty(exclusive, out List<AreaBooleanGeometry> geoms, out error, "Combine"))
                        return false;
                    exclusives.Add(geoms);
                }
                finally
                {
                    exclusive.Dispose();
                }
            }

            // Shared region = every point covered by at least two areas,
            // built as the union of all pairwise intersections.
            shared = new SKPath();
            for (int i = 0; i < paths.Count; i++)
            {
                for (int j = i + 1; j < paths.Count; j++)
                {
                    using var pairIntersection = new SKPath();
                    if (!paths[i].Op(paths[j], SKPathOp.Intersect, pairIntersection))
                    {
                        error = "Combine: area boolean failed.";
                        return false;
                    }

                    if (!TryOp(ref shared, pairIntersection, SKPathOp.Union, out error))
                        return false;
                }
            }

            if (!TryReadMeasurementGeometriesAllowEmpty(shared, out List<AreaBooleanGeometry> sharedGeoms, out error, "Combine"))
                return false;

            if (sharedGeoms.Count == 0)
            {
                error = "Combine: the selected areas do not overlap.";
                return false;
            }

            exclusivePerArea = exclusives;
            sharedGeometries = sharedGeoms;
            return true;
        }
        finally
        {
            shared?.Dispose();
            foreach (SKPath path in paths)
                path.Dispose();
        }
    }

    private static bool TryOp(ref SKPath target, SKPath other, SKPathOp op, out string error)
    {
        error = "";
        var combined = new SKPath();
        if (!target.Op(other, op, combined))
        {
            combined.Dispose();
            error = "Combine: area boolean failed.";
            return false;
        }

        target.Dispose();
        target = combined;
        return true;
    }

    private static bool ValidateCombineInput(IReadOnlyList<Measurement> areas, out string error)
    {
        error = "";
        if (areas.Count < 2)
        {
            error = "Combine: select two or more Area measurements first.";
            return false;
        }

        foreach (Measurement area in areas)
        {
            if (OurPlaneCoreJobStore.NormalizeMeasurementType(area.MType) != "area" || area.Points.Count < 3)
            {
                error = "Combine: only Area measurements can be combined.";
                return false;
            }
        }

        // Same page is enough: sheet geometry lives in shared PDF points, and
        // areas on one sheet share one calibration even though each measurement
        // stores its own ScaleMetersPerPt copy.
        Measurement first = areas[0];
        for (int i = 1; i < areas.Count; i++)
        {
            if (!SamePage(first.PageFolder, areas[i].PageFolder))
            {
                error = "Combine: areas must be on the same page.";
                return false;
            }
        }

        return true;
    }

    private static bool TryReadSingleMeasurementGeometry(
        SKPath path,
        out AreaBooleanGeometry geometry,
        out string error,
        string operation)
    {
        geometry = new AreaBooleanGeometry([], []);
        error = "";

        if (!TryReadMeasurementGeometries(path, out List<AreaBooleanGeometry> geometries, out error, operation))
            return false;

        if (geometries.Count != 1)
        {
            error = $"{operation}: result has multiple separate area islands.";
            return false;
        }

        geometry = geometries[0];
        return geometry.Points.Count >= 3;
    }

    private static bool TryReadMeasurementGeometries(
        SKPath path,
        out List<AreaBooleanGeometry> geometries,
        out string error,
        string operation)
    {
        if (!TryReadMeasurementGeometriesAllowEmpty(path, out geometries, out error, operation))
            return false;

        if (geometries.Count == 0)
        {
            error = $"{operation}: result area is empty.";
            return false;
        }

        return true;
    }

    private static bool TryReadMeasurementGeometriesAllowEmpty(
        SKPath path,
        out List<AreaBooleanGeometry> geometries,
        out string error,
        string operation)
    {
        geometries = [];
        error = "";

        List<List<SKPoint>> contours = ReadPathContours(path)
            .Select(CleanPolygon)
            .Where(contour => contour.Count >= 3 && Math.Abs(SignedArea(contour)) > AreaEpsilon)
            .ToList();
        if (contours.Count == 0)
            return true;

        List<ContourInfo> infos = contours
            .Select((points, index) => new ContourInfo(index, points, Math.Abs(SignedArea(points)), ContainmentDepth(points, contours)))
            .ToList();

        List<ContourInfo> outers = infos
            .Where(info => info.Depth % 2 == 0)
            .OrderByDescending(info => info.Area)
            .ToList();
        if (outers.Count == 0)
        {
            error = $"{operation}: result area is empty.";
            return false;
        }

        var supportedIndexes = new HashSet<int>(outers.Select(info => info.Index));
        var output = new List<AreaBooleanGeometry>();
        foreach (ContourInfo outer in outers)
        {
            List<ContourInfo> holeInfos = infos
                .Where(info => info.Depth == outer.Depth + 1 && PointInside(info.Points, outer.Points))
                .OrderByDescending(info => info.Area)
                .ToList();

            foreach (ContourInfo hole in holeInfos)
                supportedIndexes.Add(hole.Index);

            output.Add(new AreaBooleanGeometry(
                Orient(outer.Points, clockwise: false),
                holeInfos.Select(info => Orient(info.Points, clockwise: true)).ToList()));
        }

        if (infos.Any(info => !supportedIndexes.Contains(info.Index)))
        {
            error = $"{operation}: result has nested holes that cannot be stored safely.";
            return false;
        }

        geometries = output
            .Where(item => item.Points.Count >= 3)
            .OrderByDescending(item => Math.Abs(SignedArea(item.Points)))
            .ToList();
        return true;
    }

    private static SKPath BuildMeasurementPath(Measurement measurement)
    {
        var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        AddClosedContour(path, measurement.Points);
        foreach (List<SKPoint> hole in measurement.Holes)
            AddClosedContour(path, hole);
        return path;
    }

    private static SKPath BuildPolygonPath(IReadOnlyList<SKPoint> points)
    {
        var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        AddClosedContour(path, CleanPolygon(points));
        return path;
    }

    private static void AddClosedContour(SKPath path, IReadOnlyList<SKPoint> points)
    {
        if (points.Count < 3)
            return;

        path.MoveTo(points[0]);
        for (int i = 1; i < points.Count; i++)
            path.LineTo(points[i]);
        path.Close();
    }

    private static List<List<SKPoint>> ReadPathContours(SKPath path)
    {
        var contours = new List<List<SKPoint>>();
        List<SKPoint>? current = null;
        using SKPath.RawIterator iterator = path.CreateRawIterator();
        var points = new SKPoint[4];
        while (true)
        {
            SKPathVerb verb = iterator.Next(points);
            switch (verb)
            {
                case SKPathVerb.Move:
                    if (current is { Count: > 0 })
                        contours.Add(current);
                    current = [ClonePoint(points[0])];
                    break;
                case SKPathVerb.Line:
                    current ??= [];
                    AddDistinctPoint(current, points[1]);
                    break;
                case SKPathVerb.Close:
                    if (current is { Count: > 0 })
                    {
                        contours.Add(current);
                        current = null;
                    }
                    break;
                case SKPathVerb.Done:
                    if (current is { Count: > 0 })
                        contours.Add(current);
                    return contours;
            }
        }
    }

    private static int ContainmentDepth(IReadOnlyList<SKPoint> contour, IReadOnlyList<List<SKPoint>> allContours)
    {
        SKPoint probe = MeasurementGeometry.Centroid(contour);
        int depth = 0;
        float contourArea = Math.Abs(SignedArea(contour));
        foreach (List<SKPoint> candidate in allContours)
        {
            if (ReferenceEquals(contour, candidate))
                continue;
            if (Math.Abs(SignedArea(candidate)) <= contourArea + AreaEpsilon)
                continue;

            if (PointInside(probe, candidate))
                depth++;
        }

        return depth;
    }

    private static bool PointInside(IReadOnlyList<SKPoint> inner, IReadOnlyList<SKPoint> outer) =>
        PointInside(MeasurementGeometry.Centroid(inner), outer);

    private static bool PointInside(SKPoint point, IReadOnlyList<SKPoint> polygon) =>
        MeasurementGeometry.PointInPolygon(point, polygon);

    private static List<SKPoint> Orient(IReadOnlyList<SKPoint> points, bool clockwise)
    {
        var result = points.Select(ClonePoint).ToList();
        bool isClockwise = SignedArea(result) < 0;
        if (isClockwise != clockwise)
            result.Reverse();
        return result;
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
                if (Math.Abs(Cross(previous, current, next)) <= AreaEpsilon &&
                    MeasurementGeometry.DistanceToSegment(current, previous, next) <= AreaEpsilon)
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

    private static float SignedArea(IReadOnlyList<SKPoint> polygon)
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

    private static float Cross(SKPoint a, SKPoint b, SKPoint c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static void AddDistinctPoint(List<SKPoint> points, SKPoint point)
    {
        if (points.Count == 0 || !SamePoint(points[^1], point))
            points.Add(ClonePoint(point));
    }

    private static bool SamePoint(SKPoint left, SKPoint right) =>
        MeasurementGeometry.DistanceSquared(left, right) <= AreaEpsilon;

    private static SKPoint ClonePoint(SKPoint point) =>
        new(point.X, point.Y);

    private static bool SamePage(string first, string second) =>
        string.Equals(first ?? "", second ?? "", StringComparison.OrdinalIgnoreCase);

    private static bool CompatibleScales(double first, double second) =>
        first <= 0 ||
        second <= 0 ||
        Math.Abs(first - second) <= 0.000000001;

    private sealed record ContourInfo(int Index, List<SKPoint> Points, float Area, int Depth);
}
