using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SkiaSharp;

namespace OurPlaneCore;

public sealed record SheetOverlayAutoFitResult(
    bool Ok,
    double OffsetXPt,
    double OffsetYPt,
    double OverlayScale,
    int MatchedSamples,
    int SampleCount,
    double Confidence,
    string Message);

public static class SheetOverlayAutoFitService
{
    private const int MaxBaseSegments = 220;
    private const int MaxOverlaySegments = 180;
    private const int MaxOverlaySamples = 180;
    private const float MinimumSegmentLengthPt = 18f;
    private const float MaximumCandidateScale = 4.0f;
    private const float MinimumCandidateScale = 0.25f;
    private const float MaximumAngleDeltaRadians = 5.0f * MathF.PI / 180.0f;
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
        List<AutoFitSample> samples = BuildOverlaySamples(overlaySnap, overlaySegments);
        if (samples.Count < MinimumSamples)
        {
            result = Failed("Overlay auto fit needs more shared points or line endpoints.");
            return false;
        }

        var baseIndex = new PdfSnapPointIndex(baseSnap.Points, baseSnap.Segments);
        AutoFitCandidate best = default;
        bool found = false;

        void Consider(float offsetXPt, float offsetYPt, float scale)
        {
            if (!IsCandidateScale(scale))
                return;

            AutoFitCandidate candidate = ScoreCandidate(samples, baseIndex, offsetXPt, offsetYPt, scale);
            if (!found || candidate.Score > best.Score)
            {
                best = candidate;
                found = true;
            }
        }

        Consider(0, 0, 1);
        ConsiderBoundsCandidate(baseSnap, overlaySnap, Consider);

        foreach (PdfGeometrySnapSegment overlay in overlaySegments)
        {
            float overlayLength = SegmentLength(overlay);
            float overlayAngle = UndirectedAngle(overlay);
            SKPoint overlayMid = SegmentMidpoint(overlay);

            foreach (PdfGeometrySnapSegment baseSegment in baseSegments)
            {
                float angleDelta = UndirectedAngleDelta(overlayAngle, UndirectedAngle(baseSegment));
                if (angleDelta > MaximumAngleDeltaRadians)
                    continue;

                float scale = SegmentLength(baseSegment) / overlayLength;
                if (!IsCandidateScale(scale))
                    continue;

                SKPoint baseMid = SegmentMidpoint(baseSegment);
                Consider(
                    baseMid.X - overlayMid.X * scale,
                    baseMid.Y - overlayMid.Y * scale,
                    scale);
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
            best.MatchedSamples,
            best.SampleCount,
            best.Confidence,
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

            string key = $"{MathF.Round(point.X * 2) / 2:0.##}|{MathF.Round(point.Y * 2) / 2:0.##}";
            if (!seen.Add(key))
                return;

            samples.Add(new AutoFitSample(point, weight));
        }

        foreach (PdfGeometrySnapPoint point in snap.Points
                     .OrderBy(point => string.Equals(point.Kind, "pdf-corner", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                     .Take(MaxOverlaySamples / 3))
        {
            Add(point.Point, string.Equals(point.Kind, "pdf-corner", StringComparison.OrdinalIgnoreCase) ? 1.35f : 1.0f);
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
        Action<float, float, float> consider)
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
            scale);
    }

    private static AutoFitCandidate ScoreCandidate(
        IReadOnlyList<AutoFitSample> samples,
        PdfSnapPointIndex baseIndex,
        float offsetXPt,
        float offsetYPt,
        float scale)
    {
        float weightedTotal = 0;
        float score = 0;
        int matched = 0;

        foreach (AutoFitSample sample in samples)
        {
            weightedTotal += sample.Weight;
            SKPoint mapped = new(
                offsetXPt + sample.Point.X * scale,
                offsetYPt + sample.Point.Y * scale);
            if (!baseIndex.TryFind(mapped, MatchTolerancePt, out PdfGeometrySnapPoint snap))
                continue;

            float distance = Distance(mapped, snap.Point);
            float quality = Math.Clamp(1.0f - distance / MatchTolerancePt, 0, 1);
            score += sample.Weight * quality;
            matched++;
        }

        float confidence = weightedTotal <= 0 ? 0 : score / weightedTotal;
        return new AutoFitCandidate(offsetXPt, offsetYPt, scale, score, matched, samples.Count, confidence);
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

    private static SheetOverlayAutoFitResult Failed(string message) =>
        new(false, 0, 0, 1, 0, 0, 0, message);

    private static string BuildSuccessMessage(AutoFitCandidate candidate) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "Overlay auto fit: {0}/{1} geometry samples matched, confidence {2:0}%, scale {3:0.###}x.",
            candidate.MatchedSamples,
            candidate.SampleCount,
            candidate.Confidence * 100,
            candidate.Scale);

    private static bool IsCandidateScale(float scale) =>
        IsFinite(scale) && scale >= MinimumCandidateScale && scale <= MaximumCandidateScale;

    private static float SegmentLength(PdfGeometrySnapSegment segment) =>
        Distance(segment.Start, segment.End);

    private static SKPoint SegmentMidpoint(PdfGeometrySnapSegment segment) =>
        new((segment.Start.X + segment.End.X) * 0.5f, (segment.Start.Y + segment.End.Y) * 0.5f);

    private static float UndirectedAngle(PdfGeometrySnapSegment segment)
    {
        float angle = MathF.Atan2(segment.End.Y - segment.Start.Y, segment.End.X - segment.Start.X);
        if (angle < 0)
            angle += MathF.PI;
        if (angle >= MathF.PI)
            angle -= MathF.PI;
        return angle;
    }

    private static float UndirectedAngleDelta(float first, float second)
    {
        float delta = MathF.Abs(first - second);
        return Math.Min(delta, MathF.PI - delta);
    }

    private static SKPoint RectCenter(SKRect rect) =>
        new((rect.Left + rect.Right) * 0.5f, (rect.Top + rect.Bottom) * 0.5f);

    private static float Distance(SKPoint left, SKPoint right)
    {
        float dx = left.X - right.X;
        float dy = left.Y - right.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static bool IsFinite(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value);

    private readonly record struct AutoFitSample(SKPoint Point, float Weight);

    private readonly record struct AutoFitCandidate(
        float OffsetXPt,
        float OffsetYPt,
        float Scale,
        float Score,
        int MatchedSamples,
        int SampleCount,
        float Confidence);
}
