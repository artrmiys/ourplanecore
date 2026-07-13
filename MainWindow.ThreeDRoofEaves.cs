using SkiaSharp;

namespace OurPlanCore;

public partial class MainWindow
{
    private const double RoofDraftMinimumPdfLength = 3.0;
    private const double RoofDraftMinimumGuideFraction = 0.015;

    private void StartThreeDRoofEaveGuideMode()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "3D Roof: open a job first.";
            return;
        }

        if (_threeDRoofGuides.Count == 0)
        {
            TxtStatus.Text = "3D Roof: create Roof Base first, then draw eave segments.";
            return;
        }

        SetThreeDRoofEdgeSelectMode(enabled: false);
        _viewport.SetThreeDRoofMode(enabled: true, ThreeDRoofGuideKinds.Eave);
        TxtStatus.Text = "3D Roof Draw Eave: click approximate start/end along a roof base edge. The segment will snap and split that edge.";
    }

    private void OnThreeDRoofGuideAdded(string kind, IReadOnlyList<SKPoint> points)
    {
        if (ThreeDRoofGuideKinds.Normalize(kind) != ThreeDRoofGuideKinds.Eave)
        {
            TxtStatus.Text = "3D Roof: Draw Eave only creates slope-defining eave segments on the roof base.";
            return;
        }

        if (points.Count < 2)
            return;

        if (!TryApplyDraftEaveSegment(points[0], points[^1], out ThreeDRoofGuide? eaveGuide) ||
            eaveGuide == null)
        {
            TxtStatus.Text = "3D Roof: drawn eave segment did not land close enough to a roof base edge.";
            return;
        }

        _selectedThreeDRoofGuideIds.Clear();
        _selectedThreeDRoofGuideIds.Add(eaveGuide.Id);
        _selectedThreeDRoofGuideId = eaveGuide.Id;
        BuildThreeDRoofPreview();
        TxtStatus.Text = $"3D Roof: snapped drawn segment to {eaveGuide.Label}, pitch {PitchLabel(eaveGuide.PitchRisePerFoot)}.";
        LogThreeD($"Drawn eave segment snapped to roof base and split edge: {eaveGuide.Label}, pitch {PitchLabel(eaveGuide.PitchRisePerFoot)}.");
    }

    private bool TryApplyDraftEaveSegment(SKPoint start, SKPoint end, out ThreeDRoofGuide? eaveGuide)
    {
        eaveGuide = null;
        if (Distance(start.X, start.Y, end.X, end.Y) < RoofDraftMinimumPdfLength)
            return false;

        if (!TryFindBestRoofGuideForDraftLine(start, end, out RoofGuideDraftSnap snap))
            return false;

        int index = _threeDRoofGuides.FindIndex(guide => string.Equals(guide.Id, snap.Guide.Id, StringComparison.Ordinal));
        if (index < 0)
            return false;

        List<ThreeDRoofGuide> replacement = SplitRoofGuideForEave(snap.Guide, snap.StartT, snap.EndT, out eaveGuide);
        if (eaveGuide == null)
            return false;

        _threeDRoofGuides.RemoveAt(index);
        _threeDRoofGuides.InsertRange(index, replacement);
        RefreshThreeDRoofGuideOverlay();
        return true;
    }

    private bool TryFindBestRoofGuideForDraftLine(SKPoint start, SKPoint end, out RoofGuideDraftSnap snap)
    {
        snap = default;
        ThreeDRoofGuide? bestGuide = null;
        double bestScore = double.PositiveInfinity;
        double bestStartT = 0;
        double bestEndT = 0;

        foreach (ThreeDRoofGuide guide in _threeDRoofGuides.Where(IsDraftEaveSnapCandidate))
        {
            ThreeDRoofGuidePoint a = guide.Points[0];
            ThreeDRoofGuidePoint b = guide.Points[^1];
            double guideLength = Distance(a.PdfX, a.PdfY, b.PdfX, b.PdfY);
            if (guideLength < RoofDraftMinimumPdfLength)
                continue;

            double parallel = SegmentParallelScore(start, end, a, b);
            if (parallel < 0.55)
                continue;

            double d0 = DistanceToSegment(start.X, start.Y, a.PdfX, a.PdfY, b.PdfX, b.PdfY);
            double d1 = DistanceToSegment(end.X, end.Y, a.PdfX, a.PdfY, b.PdfX, b.PdfY);
            double maxDistance = Math.Max(d0, d1);
            double tolerance = Math.Clamp(guideLength * 0.08, 12.0, 36.0);
            if (maxDistance > tolerance)
                continue;

            double t0 = ProjectParameter(start, a, b);
            double t1 = ProjectParameter(end, a, b);
            double span = Math.Abs(t1 - t0);
            if (span < RoofDraftMinimumGuideFraction || span * guideLength < RoofDraftMinimumPdfLength)
                continue;

            double score = maxDistance + (1 - parallel) * tolerance;
            if (score >= bestScore)
                continue;

            bestScore = score;
            bestGuide = guide;
            bestStartT = Math.Min(t0, t1);
            bestEndT = Math.Max(t0, t1);
        }

        if (bestGuide == null)
            return false;

        snap = new RoofGuideDraftSnap(bestGuide, bestStartT, bestEndT);
        return true;
    }

    private bool IsDraftEaveSnapCandidate(ThreeDRoofGuide guide)
    {
        if (guide.Points.Count < 2)
            return false;

        if (_currentPage != null && !IsSamePageFolder(guide.PageFolder, _currentPage.FolderPath))
            return false;

        return string.Equals(guide.Status, ThreeDRoofFootprintBuildService.GeneratedStatus, StringComparison.OrdinalIgnoreCase);
    }

    private List<ThreeDRoofGuide> SplitRoofGuideForEave(
        ThreeDRoofGuide source,
        double startT,
        double endT,
        out ThreeDRoofGuide? eaveGuide)
    {
        eaveGuide = null;
        var parts = new List<ThreeDRoofGuide>();
        ThreeDRoofGuidePoint a = source.Points[0];
        ThreeDRoofGuidePoint b = source.Points[^1];
        double pitch = ResolveThreeDRoofPitchRisePerFoot();
        double t0 = Math.Clamp(Math.Min(startT, endT), 0, 1);
        double t1 = Math.Clamp(Math.Max(startT, endT), 0, 1);

        if (t1 - t0 < RoofDraftMinimumGuideFraction)
            return parts;

        ThreeDRoofGuidePoint p0 = InterpolateRoofGuidePoint(a, b, t0);
        ThreeDRoofGuidePoint p1 = InterpolateRoofGuidePoint(a, b, t1);
        if (t0 > RoofDraftMinimumGuideFraction)
        {
            parts.Add(CreateSplitRoofGuide(
                source,
                a,
                p0,
                ThreeDRoofGuideKinds.Rake,
                0,
                "before drawn eave"));
        }

        eaveGuide = CreateSplitRoofGuide(
            source,
            p0,
            p1,
            ThreeDRoofGuideKinds.Eave,
            pitch,
            "drawn eave");
        parts.Add(eaveGuide);

        if (1 - t1 > RoofDraftMinimumGuideFraction)
        {
            parts.Add(CreateSplitRoofGuide(
                source,
                p1,
                b,
                ThreeDRoofGuideKinds.Rake,
                0,
                "after drawn eave"));
        }

        return parts;
    }

    private static ThreeDRoofGuide CreateSplitRoofGuide(
        ThreeDRoofGuide source,
        ThreeDRoofGuidePoint start,
        ThreeDRoofGuidePoint end,
        string kind,
        double pitchRisePerFoot,
        string note)
    {
        string cleanKind = ThreeDRoofGuideKinds.Normalize(kind);
        string title = ThreeDRoofGuideKinds.Title(cleanKind);
        return new ThreeDRoofGuide
        {
            Kind = cleanKind,
            Label = $"{title} - {source.Label}",
            PageFolder = source.PageFolder,
            LevelKey = source.LevelKey,
            ElevationFeet = source.ElevationFeet,
            PitchRisePerFoot = cleanKind == ThreeDRoofGuideKinds.Eave ? pitchRisePerFoot : 0,
            Color = ThreeDRoofGuideKinds.Color(cleanKind),
            DefinesSlope = cleanKind == ThreeDRoofGuideKinds.Eave,
            OverhangFeet = source.OverhangFeet,
            Status = source.Status,
            AdjustmentStatus = "snapped",
            AdjustmentMessage = $"Split from {source.Label}: {note}.",
            Points = [CloneRoofGuidePoint(start), CloneRoofGuidePoint(end)],
            RawPoints = [CloneRoofGuidePoint(start), CloneRoofGuidePoint(end)],
        };
    }

    private static ThreeDRoofGuidePoint InterpolateRoofGuidePoint(
        ThreeDRoofGuidePoint a,
        ThreeDRoofGuidePoint b,
        double t) =>
        new()
        {
            PdfX = a.PdfX + (b.PdfX - a.PdfX) * t,
            PdfY = a.PdfY + (b.PdfY - a.PdfY) * t,
            XFeet = a.XFeet + (b.XFeet - a.XFeet) * t,
            ZFeet = a.ZFeet + (b.ZFeet - a.ZFeet) * t,
        };

    private static double ProjectParameter(SKPoint point, ThreeDRoofGuidePoint a, ThreeDRoofGuidePoint b)
    {
        double dx = b.PdfX - a.PdfX;
        double dy = b.PdfY - a.PdfY;
        double len2 = dx * dx + dy * dy;
        if (len2 <= 0.000001)
            return 0;

        double t = ((point.X - a.PdfX) * dx + (point.Y - a.PdfY) * dy) / len2;
        return Math.Clamp(t, 0, 1);
    }

    private static double SegmentParallelScore(SKPoint start, SKPoint end, ThreeDRoofGuidePoint a, ThreeDRoofGuidePoint b)
    {
        double ux = end.X - start.X;
        double uy = end.Y - start.Y;
        double vx = b.PdfX - a.PdfX;
        double vy = b.PdfY - a.PdfY;
        double uLen = Math.Sqrt(ux * ux + uy * uy);
        double vLen = Math.Sqrt(vx * vx + vy * vy);
        if (uLen <= 0.000001 || vLen <= 0.000001)
            return 0;

        return Math.Abs((ux * vx + uy * vy) / (uLen * vLen));
    }

    private readonly record struct RoofGuideDraftSnap(ThreeDRoofGuide Guide, double StartT, double EndT);
}
