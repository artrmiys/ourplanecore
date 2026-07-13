using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SkiaSharp;

namespace OurPlanCore;

public sealed record SheetOverlayAutoFitResult(
    bool Ok,
    double OffsetXPt,
    double OffsetYPt,
    double OverlayScale,
    double OverlayRotationDegrees,
    int MatchedSamples,
    int SampleCount,
    double Confidence,
    string Method,
    string Message);

public static class SheetOverlayAutoFitService
{
    private const int MaxBaseSegments = 220;
    private const int MaxOverlaySegments = 180;
    private const int MaxOverlaySamples = 180;
    private const int MaxBasePointCandidates = 120;
    private const int MaxOverlayPointCandidates = 100;
    private const int MaxBaseTripletPointCandidates = 48;
    private const int MaxOverlayTripletPointCandidates = 40;
    private const int MaxBasePointTriplets = 140;
    private const int MaxOverlayPointTriplets = 100;
    private const int MaxBasePointPairs = 140;
    private const int MaxOverlayPointPairs = 100;
    private const float MinimumSegmentLengthPt = 18f;
    private const float MinimumPointPairDistancePt = 32f;
    private const float MinimumPointTripletAreaPt2 = 160f;
    private const float ShapeRatioTolerance = 0.08f;
    private const float ShapeThirdPointTolerancePt = 8.0f;
    private const float MaximumCandidateScale = 4.0f;
    private const float MinimumCandidateScale = 0.25f;
    private const float MatchTolerancePt = 10.0f;
    private const int MinimumSamples = 12;
    private const int MinimumMatchedSamples = 8;
    private const float MinimumConfidence = 0.12f;

    public static bool TryFit(
        PdfGeometrySnapResult baseSnap,
        PdfGeometrySnapResult overlaySnap,
        out SheetOverlayAutoFitResult result)
    {
        result = Failed("Overlay auto fit needs vector linework on both sheets.");

        int baseGeometryCount = baseSnap.Points.Count + baseSnap.Segments.Count;
        int overlayGeometryCount = overlaySnap.Points.Count + overlaySnap.Segments.Count;
        if (baseGeometryCount < MinimumSamples || overlayGeometryCount < MinimumSamples)
            return false;

        List<PdfGeometrySnapSegment> baseSegments = SelectCandidateSegments(baseSnap.Segments, MaxBaseSegments);
        List<PdfGeometrySnapSegment> overlaySegments = SelectCandidateSegments(overlaySnap.Segments, MaxOverlaySegments);
        List<PdfGeometrySnapPoint> basePoints = SelectCandidatePoints(baseSnap.Points, MaxBasePointCandidates);
        List<PdfGeometrySnapPoint> overlayPoints = SelectCandidatePoints(overlaySnap.Points, MaxOverlayPointCandidates);
        List<AutoFitSample> samples = BuildOverlaySamples(overlaySnap, overlaySegments);
        if (samples.Count < MinimumSamples)
        {
            result = Failed("Overlay auto fit needs more shared points or line endpoints.");
            return false;
        }

        var baseIndex = new PdfSnapPointIndex(baseSnap.Points, baseSnap.Segments);
        AutoFitCandidate best = default;
        bool found = false;

        void Consider(float offsetXPt, float offsetYPt, float scale, float rotationDegrees, string method)
        {
            if (!IsCandidateScale(scale))
                return;

            AutoFitCandidate candidate = ScoreCandidate(samples, baseIndex, offsetXPt, offsetYPt, scale, rotationDegrees, method);
            if (!found || candidate.Score > best.Score)
            {
                best = candidate;
                found = true;
            }
        }

        Consider(0, 0, 1, 0, "identity");
        ConsiderBoundsCandidate(baseSnap, overlaySnap, Consider);
        ConsiderShapeTripletCandidates(overlayPoints, basePoints, Consider);
        ConsiderPointPairCandidates(overlayPoints, basePoints, Consider);

        foreach (PdfGeometrySnapSegment overlay in overlaySegments)
        {
            float overlayLength = SegmentLength(overlay);
            float overlayAngle = DirectedAngle(overlay);
            SKPoint overlayMid = SegmentMidpoint(overlay);

            foreach (PdfGeometrySnapSegment baseSegment in baseSegments)
            {
                float scale = SegmentLength(baseSegment) / overlayLength;
                if (!IsCandidateScale(scale))
                    continue;

                float baseAngle = DirectedAngle(baseSegment);
                SKPoint baseMid = SegmentMidpoint(baseSegment);
                ConsiderSegmentPair(overlayMid, baseMid, scale, baseAngle - overlayAngle, Consider);
                ConsiderSegmentPair(overlayMid, baseMid, scale, baseAngle - overlayAngle + MathF.PI, Consider);
            }
        }

        if (!found ||
            best.MatchedSamples < MinimumMatchedSamples ||
            best.Confidence < MinimumConfidence)
        {
            result = Failed("Overlay auto fit could not find enough repeated plan geometry.");
            return false;
        }

        result = new SheetOverlayAutoFitResult(
            true,
            best.OffsetXPt,
            best.OffsetYPt,
            best.Scale,
            best.RotationDegrees,
            best.MatchedSamples,
            best.SampleCount,
            best.Confidence,
            best.Method,
            BuildSuccessMessage(best));
        return true;
    }

    private static List<PdfGeometrySnapSegment> SelectCandidateSegments(
        IReadOnlyList<PdfGeometrySnapSegment> segments,
        int maxCount) =>
        segments
            .Where(segment => SegmentLength(segment) >= MinimumSegmentLengthPt)
            .OrderByDescending(SegmentLength)
            .Take(maxCount)
            .ToList();

    private static List<PdfGeometrySnapPoint> SelectCandidatePoints(
        IReadOnlyList<PdfGeometrySnapPoint> points,
        int maxCount)
    {
        List<PdfGeometrySnapPoint> candidates = points
            .Where(point => IsFinite(point.Point.X) && IsFinite(point.Point.Y))
            .GroupBy(point => PointKey(point.Point), StringComparer.Ordinal)
            .Select(group => group.OrderBy(point => SamplePriority(point.Kind)).First())
            .ToList();

        if (candidates.Count <= maxCount || !TryPointBounds(candidates, out SKRect bounds))
        {
            return candidates
                .OrderBy(point => SamplePriority(point.Kind))
                .ThenBy(point => point.Point.X)
                .ThenBy(point => point.Point.Y)
                .Take(maxCount)
                .ToList();
        }

        return candidates
            .OrderBy(point => SamplePriority(point.Kind))
            .ThenByDescending(point => SpatialSpreadScore(point.Point, bounds))
            .ThenBy(point => point.Point.X)
            .ThenBy(point => point.Point.Y)
            .Take(maxCount)
            .ToList();
    }

    private static List<AutoFitSample> BuildOverlaySamples(
        PdfGeometrySnapResult snap,
        IReadOnlyList<PdfGeometrySnapSegment> overlaySegments)
    {
        var samples = new List<AutoFitSample>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(SKPoint point, float weight)
        {
            if (!IsFinite(point.X) || !IsFinite(point.Y))
                return;

            string key = PointKey(point);
            if (!seen.Add(key))
                return;

            samples.Add(new AutoFitSample(point, weight));
        }

        foreach (PdfGeometrySnapPoint point in snap.Points
                     .OrderBy(point => SamplePriority(point.Kind))
                     .Take(MaxOverlaySamples / 3))
        {
            Add(point.Point, SampleWeight(point.Kind));
        }

        foreach (PdfGeometrySnapSegment segment in overlaySegments)
        {
            Add(segment.Start, 1.25f);
            Add(segment.End, 1.25f);
            Add(SegmentMidpoint(segment), 1.0f);
            if (samples.Count >= MaxOverlaySamples)
                break;
        }

        return samples;
    }

    private static void ConsiderBoundsCandidate(
        PdfGeometrySnapResult baseSnap,
        PdfGeometrySnapResult overlaySnap,
        Action<float, float, float, float, string> consider)
    {
        if (!TryGeometryBounds(baseSnap, out SKRect baseBounds) ||
            !TryGeometryBounds(overlaySnap, out SKRect overlayBounds) ||
            baseBounds.Width <= 1 ||
            baseBounds.Height <= 1 ||
            overlayBounds.Width <= 1 ||
            overlayBounds.Height <= 1)
        {
            return;
        }

        float scale = Math.Min(baseBounds.Width / overlayBounds.Width, baseBounds.Height / overlayBounds.Height);
        if (!IsCandidateScale(scale))
            return;

        SKPoint baseCenter = RectCenter(baseBounds);
        SKPoint overlayCenter = RectCenter(overlayBounds);
        consider(
            baseCenter.X - overlayCenter.X * scale,
            baseCenter.Y - overlayCenter.Y * scale,
            scale,
            0,
            "bounds");
    }

    private static void ConsiderShapeTripletCandidates(
        IReadOnlyList<PdfGeometrySnapPoint> overlayPoints,
        IReadOnlyList<PdfGeometrySnapPoint> basePoints,
        Action<float, float, float, float, string> consider)
    {
        if (overlayPoints.Count < 3 || basePoints.Count < 3)
            return;

        List<PointTripletCandidate> overlayTriplets = BuildPointTriplets(
            overlayPoints.Take(MaxOverlayTripletPointCandidates).ToList(),
            MaxOverlayPointTriplets);
        List<PointTripletCandidate> baseTriplets = BuildPointTriplets(
            basePoints.Take(MaxBaseTripletPointCandidates).ToList(),
            MaxBasePointTriplets);
        if (overlayTriplets.Count == 0 || baseTriplets.Count == 0)
            return;

        foreach (PointTripletCandidate overlayTriplet in overlayTriplets)
        {
            foreach (PointTripletCandidate baseTriplet in baseTriplets)
            {
                if (!HaveSimilarShape(overlayTriplet, baseTriplet))
                    continue;

                ConsiderShapeTriplet(overlayTriplet, baseTriplet, false, consider);
                ConsiderShapeTriplet(overlayTriplet, baseTriplet, true, consider);
            }
        }
    }

    private static List<PointTripletCandidate> BuildPointTriplets(
        IReadOnlyList<PdfGeometrySnapPoint> points,
        int maxCount)
    {
        var triplets = new List<PointTripletCandidate>();
        for (int i = 0; i < points.Count; i++)
        {
            PdfGeometrySnapPoint first = points[i];
            for (int j = i + 1; j < points.Count; j++)
            {
                PdfGeometrySnapPoint second = points[j];
                for (int k = j + 1; k < points.Count; k++)
                {
                    PdfGeometrySnapPoint third = points[k];
                    if (!TryBuildPointTriplet(first, second, third, out PointTripletCandidate triplet))
                        continue;

                    triplets.Add(triplet);
                }
            }
        }

        return triplets
            .OrderByDescending(triplet => triplet.Weight)
            .ThenByDescending(triplet => triplet.NormalizedArea)
            .ThenByDescending(triplet => triplet.LongDistance)
            .Take(maxCount)
            .ToList();
    }

    private static bool TryBuildPointTriplet(
        PdfGeometrySnapPoint first,
        PdfGeometrySnapPoint second,
        PdfGeometrySnapPoint third,
        out PointTripletCandidate triplet)
    {
        float firstSecond = Distance(first.Point, second.Point);
        float firstThird = Distance(first.Point, third.Point);
        float secondThird = Distance(second.Point, third.Point);

        SKPoint a = first.Point;
        SKPoint b = second.Point;
        SKPoint c = third.Point;
        float longDistance = firstSecond;
        float sideOne = firstThird;
        float sideTwo = secondThird;

        if (firstThird > longDistance && firstThird >= secondThird)
        {
            b = third.Point;
            c = second.Point;
            longDistance = firstThird;
            sideOne = firstSecond;
            sideTwo = secondThird;
        }
        else if (secondThird > longDistance)
        {
            a = second.Point;
            b = third.Point;
            c = first.Point;
            longDistance = secondThird;
            sideOne = firstSecond;
            sideTwo = firstThird;
        }

        float area2 = MathF.Abs(Cross(a, b, c));
        if (longDistance < MinimumPointPairDistancePt || area2 < MinimumPointTripletAreaPt2)
        {
            triplet = default;
            return false;
        }

        float shortRatio = Math.Min(sideOne, sideTwo) / longDistance;
        float midRatio = Math.Max(sideOne, sideTwo) / longDistance;
        float normalizedArea = area2 / (longDistance * longDistance);
        float weight = SampleWeight(first.Kind) + SampleWeight(second.Kind) + SampleWeight(third.Kind);
        triplet = new PointTripletCandidate(
            a,
            b,
            c,
            longDistance,
            shortRatio,
            midRatio,
            normalizedArea,
            DirectedAngle(a, b),
            weight);
        return true;
    }

    private static bool HaveSimilarShape(PointTripletCandidate overlayTriplet, PointTripletCandidate baseTriplet) =>
        MathF.Abs(overlayTriplet.ShortRatio - baseTriplet.ShortRatio) <= ShapeRatioTolerance &&
        MathF.Abs(overlayTriplet.MidRatio - baseTriplet.MidRatio) <= ShapeRatioTolerance;

    private static void ConsiderShapeTriplet(
        PointTripletCandidate overlayTriplet,
        PointTripletCandidate baseTriplet,
        bool reverseBasePair,
        Action<float, float, float, float, string> consider)
    {
        float scale = baseTriplet.LongDistance / overlayTriplet.LongDistance;
        if (!IsCandidateScale(scale))
            return;

        SKPoint baseAnchor = reverseBasePair ? baseTriplet.B : baseTriplet.A;
        float baseAngle = reverseBasePair ? baseTriplet.AngleRadians + MathF.PI : baseTriplet.AngleRadians;
        float rotationDegrees = NormalizeRotationDegrees((baseAngle - overlayTriplet.AngleRadians) * 180f / MathF.PI);
        SKPoint mappedAnchor = TransformPoint(overlayTriplet.A, 0, 0, scale, rotationDegrees);
        float offsetX = baseAnchor.X - mappedAnchor.X;
        float offsetY = baseAnchor.Y - mappedAnchor.Y;
        SKPoint mappedThird = TransformPoint(overlayTriplet.C, offsetX, offsetY, scale, rotationDegrees);
        if (Distance(mappedThird, baseTriplet.C) > ShapeThirdPointTolerancePt)
            return;

        consider(offsetX, offsetY, scale, rotationDegrees, "shape points");
    }

    private static void ConsiderPointPairCandidates(
        IReadOnlyList<PdfGeometrySnapPoint> overlayPoints,
        IReadOnlyList<PdfGeometrySnapPoint> basePoints,
        Action<float, float, float, float, string> consider)
    {
        if (overlayPoints.Count < 2 || basePoints.Count < 2)
            return;

        List<PointPairCandidate> overlayPairs = BuildPointPairs(overlayPoints, MaxOverlayPointPairs);
        List<PointPairCandidate> basePairs = BuildPointPairs(basePoints, MaxBasePointPairs);
        if (overlayPairs.Count == 0 || basePairs.Count == 0)
            return;

        foreach (PointPairCandidate overlayPair in overlayPairs)
        {
            foreach (PointPairCandidate basePair in basePairs)
            {
                ConsiderPointPair(overlayPair, basePair, false, consider);
                ConsiderPointPair(overlayPair, basePair, true, consider);
            }
        }
    }

    private static List<PointPairCandidate> BuildPointPairs(
        IReadOnlyList<PdfGeometrySnapPoint> points,
        int maxCount)
    {
        var pairs = new List<PointPairCandidate>();
        for (int i = 0; i < points.Count; i++)
        {
            PdfGeometrySnapPoint first = points[i];
            for (int j = i + 1; j < points.Count; j++)
            {
                PdfGeometrySnapPoint second = points[j];
                float distance = Distance(first.Point, second.Point);
                if (distance < MinimumPointPairDistancePt)
                    continue;

                pairs.Add(new PointPairCandidate(
                    first.Point,
                    second.Point,
                    distance,
                    DirectedAngle(first.Point, second.Point),
                    SampleWeight(first.Kind) + SampleWeight(second.Kind)));
            }
        }

        return pairs
            .OrderByDescending(pair => pair.Weight)
            .ThenByDescending(pair => pair.Distance)
            .Take(maxCount)
            .ToList();
    }

    private static void ConsiderPointPair(
        PointPairCandidate overlayPair,
        PointPairCandidate basePair,
        bool reverseBasePair,
        Action<float, float, float, float, string> consider)
    {
        float scale = basePair.Distance / overlayPair.Distance;
        if (!IsCandidateScale(scale))
            return;

        SKPoint baseAnchor = reverseBasePair ? basePair.B : basePair.A;
        float baseAngle = reverseBasePair ? basePair.AngleRadians + MathF.PI : basePair.AngleRadians;
        float rotationDegrees = NormalizeRotationDegrees((baseAngle - overlayPair.AngleRadians) * 180f / MathF.PI);
        SKPoint mappedAnchor = TransformPoint(overlayPair.A, 0, 0, scale, rotationDegrees);
        consider(
            baseAnchor.X - mappedAnchor.X,
            baseAnchor.Y - mappedAnchor.Y,
            scale,
            rotationDegrees,
            "point pairs");
    }

    private static void ConsiderSegmentPair(
        SKPoint overlayMid,
        SKPoint baseMid,
        float scale,
        float rotationRadians,
        Action<float, float, float, float, string> consider)
    {
        float rotationDegrees = NormalizeRotationDegrees(rotationRadians * 180f / MathF.PI);
        SKPoint mappedMid = TransformPoint(overlayMid, 0, 0, scale, rotationDegrees);
        consider(
            baseMid.X - mappedMid.X,
            baseMid.Y - mappedMid.Y,
            scale,
            rotationDegrees,
            "segments");
    }

    private static AutoFitCandidate ScoreCandidate(
        IReadOnlyList<AutoFitSample> samples,
        PdfSnapPointIndex baseIndex,
        float offsetXPt,
        float offsetYPt,
        float scale,
        float rotationDegrees,
        string method)
    {
        float weightedTotal = 0;
        float score = 0;
        int matched = 0;

        foreach (AutoFitSample sample in samples)
        {
            weightedTotal += sample.Weight;
            SKPoint mapped = TransformPoint(sample.Point, offsetXPt, offsetYPt, scale, rotationDegrees);
            if (!baseIndex.TryFind(mapped, MatchTolerancePt, out PdfGeometrySnapPoint snap))
                continue;

            float distance = Distance(mapped, snap.Point);
            float quality = Math.Clamp(1.0f - distance / MatchTolerancePt, 0, 1);
            score += sample.Weight * quality;
            matched++;
        }

        float confidence = weightedTotal <= 0 ? 0 : score / weightedTotal;
        return new AutoFitCandidate(
            offsetXPt,
            offsetYPt,
            scale,
            NormalizeRotationDegrees(rotationDegrees),
            score,
            matched,
            samples.Count,
            confidence,
            method);
    }

    private static bool TryGeometryBounds(PdfGeometrySnapResult snap, out SKRect bounds)
    {
        float left = float.MaxValue;
        float top = float.MaxValue;
        float right = float.MinValue;
        float bottom = float.MinValue;
        int count = 0;

        void Include(SKPoint point)
        {
            if (!IsFinite(point.X) || !IsFinite(point.Y))
                return;

            left = Math.Min(left, point.X);
            top = Math.Min(top, point.Y);
            right = Math.Max(right, point.X);
            bottom = Math.Max(bottom, point.Y);
            count++;
        }

        foreach (PdfGeometrySnapPoint point in snap.Points)
            Include(point.Point);
        foreach (PdfGeometrySnapSegment segment in snap.Segments)
        {
            Include(segment.Start);
            Include(segment.End);
        }

        bounds = count == 0 ? SKRect.Empty : new SKRect(left, top, right, bottom);
        return count > 0;
    }

    private static bool TryPointBounds(IReadOnlyList<PdfGeometrySnapPoint> points, out SKRect bounds)
    {
        if (points.Count == 0)
        {
            bounds = SKRect.Empty;
            return false;
        }

        float left = points.Min(point => point.Point.X);
        float top = points.Min(point => point.Point.Y);
        float right = points.Max(point => point.Point.X);
        float bottom = points.Max(point => point.Point.Y);
        bounds = new SKRect(left, top, right, bottom);
        return bounds.Width > 1 && bounds.Height > 1;
    }

    private static float SpatialSpreadScore(SKPoint point, SKRect bounds)
    {
        float centerX = (bounds.Left + bounds.Right) * 0.5f;
        float centerY = (bounds.Top + bounds.Bottom) * 0.5f;
        float halfWidth = Math.Max(bounds.Width * 0.5f, 1);
        float halfHeight = Math.Max(bounds.Height * 0.5f, 1);
        float normalizedX = MathF.Abs((point.X - centerX) / halfWidth);
        float normalizedY = MathF.Abs((point.Y - centerY) / halfHeight);
        return MathF.Max(normalizedX, normalizedY);
    }

    private static SheetOverlayAutoFitResult Failed(string message) =>
        new(false, 0, 0, 1, 0, 0, 0, 0, "", message);

    private static string BuildSuccessMessage(AutoFitCandidate candidate) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "Overlay auto fit ({0}): {1}/{2} geometry samples matched, confidence {3:0}%, scale {4:0.###}x, rotation {5:0.###} deg.",
            candidate.Method,
            candidate.MatchedSamples,
            candidate.SampleCount,
            candidate.Confidence * 100,
            candidate.Scale,
            candidate.RotationDegrees);

    private static bool IsCandidateScale(float scale) =>
        IsFinite(scale) && scale >= MinimumCandidateScale && scale <= MaximumCandidateScale;

    private static float SegmentLength(PdfGeometrySnapSegment segment) =>
        Distance(segment.Start, segment.End);

    private static SKPoint SegmentMidpoint(PdfGeometrySnapSegment segment) =>
        new((segment.Start.X + segment.End.X) * 0.5f, (segment.Start.Y + segment.End.Y) * 0.5f);

    private static float DirectedAngle(PdfGeometrySnapSegment segment) =>
        MathF.Atan2(segment.End.Y - segment.Start.Y, segment.End.X - segment.Start.X);

    private static float DirectedAngle(SKPoint start, SKPoint end) =>
        MathF.Atan2(end.Y - start.Y, end.X - start.X);

    private static int SamplePriority(string kind) => kind.ToLowerInvariant() switch
    {
        "raster-junction" => 0,
        "pdf-corner" => 1,
        "raster-corner" => 2,
        "pdf-point" => 3,
        _ => 4,
    };

    private static float SampleWeight(string kind) => kind.ToLowerInvariant() switch
    {
        "raster-junction" => 1.6f,
        "pdf-corner" => 1.35f,
        "raster-corner" => 1.2f,
        _ => 1.0f,
    };

    private static SKPoint TransformPoint(
        SKPoint point,
        float offsetXPt,
        float offsetYPt,
        float scale,
        float rotationDegrees)
    {
        float radians = rotationDegrees * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        float x = point.X * scale;
        float y = point.Y * scale;
        return new SKPoint(
            offsetXPt + x * cos - y * sin,
            offsetYPt + x * sin + y * cos);
    }

    private static float NormalizeRotationDegrees(float degrees)
    {
        if (!IsFinite(degrees))
            return 0;

        float normalized = degrees % 360f;
        if (normalized > 180f)
            normalized -= 360f;
        if (normalized <= -180f)
            normalized += 360f;
        return normalized;
    }

    private static SKPoint RectCenter(SKRect rect) =>
        new((rect.Left + rect.Right) * 0.5f, (rect.Top + rect.Bottom) * 0.5f);

    private static float Distance(SKPoint left, SKPoint right)
    {
        float dx = left.X - right.X;
        float dy = left.Y - right.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static float Cross(SKPoint a, SKPoint b, SKPoint c) =>
        ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));

    private static bool IsFinite(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value);

    private static string PointKey(SKPoint point) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0:0.##}|{1:0.##}",
            MathF.Round(point.X * 2) / 2,
            MathF.Round(point.Y * 2) / 2);

    private readonly record struct AutoFitSample(SKPoint Point, float Weight);

    private readonly record struct PointPairCandidate(
        SKPoint A,
        SKPoint B,
        float Distance,
        float AngleRadians,
        float Weight);

    private readonly record struct PointTripletCandidate(
        SKPoint A,
        SKPoint B,
        SKPoint C,
        float LongDistance,
        float ShortRatio,
        float MidRatio,
        float NormalizedArea,
        float AngleRadians,
        float Weight);

    private readonly record struct AutoFitCandidate(
        float OffsetXPt,
        float OffsetYPt,
        float Scale,
        float RotationDegrees,
        float Score,
        int MatchedSamples,
        int SampleCount,
        float Confidence,
        string Method);
}
