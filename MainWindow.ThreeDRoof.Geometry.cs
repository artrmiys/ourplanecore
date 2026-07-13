using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OurPlanCore.Controls;
using SkiaSharp;

namespace OurPlanCore;

public partial class MainWindow
{
    // 3D roof guide labels, distance geometry, cloning, and pitch labels.

    private static string RelabelRoofGuide(ThreeDRoofGuide guide)
    {
        string title = ThreeDRoofGuideKinds.Title(guide.Kind);
        string label = string.IsNullOrWhiteSpace(guide.Label) ? title : guide.Label.Trim();
        foreach (string kind in ThreeDRoofGuideKinds.All)
        {
            string kindTitle = ThreeDRoofGuideKinds.Title(kind);
            if (label.StartsWith(kindTitle, StringComparison.OrdinalIgnoreCase))
                return title + label[kindTitle.Length..];
        }

        string[] parts = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && int.TryParse(parts[^1], out _))
        {
            foreach (string kind in ThreeDRoofGuideKinds.All)
            {
                string kindTitle = ThreeDRoofGuideKinds.Title(kind);
                if (string.Equals(parts[^2], kindTitle, StringComparison.OrdinalIgnoreCase))
                {
                    parts[^2] = title;
                    return string.Join(" ", parts);
                }
            }
        }

        return $"{title} - {label}";
    }

    private static double DistanceToSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax;
        double dy = by - ay;
        double len2 = dx * dx + dy * dy;
        if (len2 <= 0.000001)
            return Distance(px, py, ax, ay);

        double t = ((px - ax) * dx + (py - ay) * dy) / len2;
        t = Math.Clamp(t, 0, 1);
        return Distance(px, py, ax + dx * t, ay + dy * t);
    }

    private static double SegmentDistance(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
    {
        if (SegmentsIntersect(ax, ay, bx, by, cx, cy, dx, dy))
            return 0;

        return Math.Min(
            Math.Min(DistanceToSegment(ax, ay, cx, cy, dx, dy), DistanceToSegment(bx, by, cx, cy, dx, dy)),
            Math.Min(DistanceToSegment(cx, cy, ax, ay, bx, by), DistanceToSegment(dx, dy, ax, ay, bx, by)));
    }

    private static bool SegmentsIntersect(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
    {
        double o1 = Cross(ax, ay, bx, by, cx, cy);
        double o2 = Cross(ax, ay, bx, by, dx, dy);
        double o3 = Cross(cx, cy, dx, dy, ax, ay);
        double o4 = Cross(cx, cy, dx, dy, bx, by);
        return o1 > 0 != o2 > 0 && o3 > 0 != o4 > 0;
    }

    private static double Cross(double ax, double ay, double bx, double by, double cx, double cy) =>
        (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);

    private static double Distance(double ax, double ay, double bx, double by)
    {
        double dx = bx - ax;
        double dy = by - ay;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static ThreeDRoofGuide CloneThreeDRoofGuide(ThreeDRoofGuide guide) =>
        new()
        {
            Id = guide.Id,
            Kind = guide.Kind,
            Label = guide.Label,
            PageFolder = guide.PageFolder,
            LevelKey = guide.LevelKey,
            ElevationFeet = guide.ElevationFeet,
            Color = guide.Color,
            Status = guide.Status,
            AdjustmentStatus = guide.AdjustmentStatus,
            AdjustmentMessage = guide.AdjustmentMessage,
            RawPoints = guide.RawPoints.Select(CloneRoofGuidePoint).ToList(),
            Points = guide.Points.Select(CloneRoofGuidePoint).ToList(),
            PitchRisePerFoot = guide.PitchRisePerFoot,
            RoofGroupId = guide.RoofGroupId,
            RoofGroupLabel = guide.RoofGroupLabel,
            DefinesSlope = guide.DefinesSlope,
            OverhangFeet = guide.OverhangFeet,
        };

    private static ThreeDRoofPlane CloneThreeDRoofPlane(ThreeDRoofPlane plane) =>
        new()
        {
            Id = plane.Id,
            Kind = plane.Kind,
            Label = plane.Label,
            Color = plane.Color,
            Opacity = plane.Opacity,
            Status = plane.Status,
            Message = plane.Message,
            RoofGroupId = plane.RoofGroupId,
            RoofGroupLabel = plane.RoofGroupLabel,
            SourceGuideIds = plane.SourceGuideIds.ToList(),
            Points = plane.Points
                .Select(point => new ThreeDRoofVertex
                {
                    XFeet = point.XFeet,
                    YFeet = point.YFeet,
                    ZFeet = point.ZFeet,
                })
                .ToList(),
        };

    private static ThreeDRoofIssue CloneThreeDRoofIssue(ThreeDRoofIssue issue) =>
        new()
        {
            Id = issue.Id,
            Severity = issue.Severity,
            Kind = issue.Kind,
            Message = issue.Message,
            PageFolder = issue.PageFolder,
            HasPdfPoint = issue.HasPdfPoint,
            PdfX = issue.PdfX,
            PdfY = issue.PdfY,
            XFeet = issue.XFeet,
            YFeet = issue.YFeet,
            ZFeet = issue.ZFeet,
            Color = issue.Color,
            RoofGroupId = issue.RoofGroupId,
            RoofGroupLabel = issue.RoofGroupLabel,
            GuideIds = issue.GuideIds.ToList(),
        };

    private static ThreeDRoofGuidePoint CloneRoofGuidePoint(ThreeDRoofGuidePoint point) =>
        new()
        {
            PdfX = point.PdfX,
            PdfY = point.PdfY,
            XFeet = point.XFeet,
            YFeet = point.YFeet,
            ZFeet = point.ZFeet,
        };

    private static string PitchLabel(double pitchRisePerFoot)
    {
        double pitch = pitchRisePerFoot > 0
            ? pitchRisePerFoot
            : ThreeDRoofPreviewBuilder.DefaultPitchRisePerFoot;
        return RoofPitchText.Format(pitch);
    }

    private static string RoofGuidePitchLabel(ThreeDRoofGuide guide) =>
        guide.DefinesSlope
            ? $"pitch {PitchLabel(guide.PitchRisePerFoot)}"
            : "no slope";
}
