using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace OurPlanCore;

public sealed class Measurement
{
    private static readonly object HoleWarningLock = new();
    private static readonly HashSet<string> HoleContainmentWarnings = [];

    public string         Id         { get; set; } = Guid.NewGuid().ToString();
    public string         Name       { get; set; } = "";
    public string         Notes      { get; set; } = "";
    public string         MType      { get; set; }  = "line";   // point | line | area
    public List<SKPoint>  Points     { get; set; }  = [];
    public List<List<SKPoint>> Holes { get; set; } = [];
    public string         Color      { get; set; }  = "#FF4444";
    public string         CountSymbol { get; set; } = CountDisplaySymbol.Circle;
    public string         PageFolder { get; set; }  = "";
    public string         TakeoffFolder { get; set; } = "";
    public double         ScaleMetersPerPt { get; set; }
    public bool           JoistEnabled { get; set; }
    public string         JoistType { get; set; } = "";
    public double         JoistSpacingInches { get; set; } = 16;
    public double         JoistDirectionDegrees { get; set; }
    public bool           JoistDirectionLocked { get; set; }
    public bool           JoistDirectionFollowsAreaRotation { get; set; } = true;
    public bool           JoistAddEndJoist { get; set; } = true;
    public bool           JoistStartEdgeEnabled { get; set; } = true;
    public bool           JoistEndEdgeEnabled { get; set; }
    public bool           JoistEdgeOverridesSet { get; set; }
    public string         JoistPitch { get; set; } = "";
    public string         JoistLengthRounding { get; set; } = JoistTakeoffCalculator.RoundingNearestEvenFoot;
    public bool           JoistShowLabels { get; set; }
    public bool           JoistDetailedLabels { get; set; } = true;
    public bool           JoistMoveNote { get; set; }
    public float          JoistNoteOffsetX { get; set; }
    public float          JoistNoteOffsetY { get; set; }
    public bool           JoistNotePositionSet { get; set; }
    public bool HasJoistNotePosition => JoistNotePositionSet || JoistNoteOffsetX != 0 || JoistNoteOffsetY != 0;

    public SKPoint JoistNoteAnchor() => MeasurementGeometry.Centroid(Points) +
        new SKPoint(JoistNoteOffsetX, JoistNoteOffsetY);

    internal Measurement Snapshot()
    {
        var copy = (Measurement)MemberwiseClone();
        copy.Points = Points.ToList();
        copy.Holes = Holes.Select(hole => hole.ToList()).ToList();
        copy.ExtraJoists = ExtraJoists.Select(extra => new JoistExtraSegment
        {
            Id = extra.Id, Start = extra.Start, End = extra.End,
        }).ToList();
        return copy;
    }
    public List<JoistExtraSegment> ExtraJoists { get; set; } = [];

    public double Value(double scaleMetersPerPt)
    {
        double effectiveScale = ScaleMetersPerPt > 0 ? ScaleMetersPerPt : scaleMetersPerPt;
        if (MType == "area" && JoistEnabled && !JoistDirectionLocked)
            return 0;
        if (MType == "area" && JoistEnabled)
            return JoistTakeoffCalculator.Calculate(this, effectiveScale).TotalLengthMeters;

        return MType switch
        {
            "point" => Points.Count,
            "line"  => LineLengthPt() * effectiveScale,
            "area"  => PolygonAreaPt() * effectiveScale * effectiveScale,
            _       => 0,
        };
    }

    public double AreaValue(double scaleMetersPerPt)
    {
        if (MType != "area")
            return 0;

        double effectiveScale = ScaleMetersPerPt > 0 ? ScaleMetersPerPt : scaleMetersPerPt;
        return effectiveScale > 0 ? PolygonAreaPt() * effectiveScale * effectiveScale : 0;
    }

    public string Label(double scaleMetersPerPt, UnitMode unit = UnitMode.Metric)
    {
        double effectiveScale = ScaleMetersPerPt > 0 ? ScaleMetersPerPt : scaleMetersPerPt;
        if (MType == "area" && JoistEnabled && !JoistDirectionLocked)
            return "joists: set direction";
        if (MType == "area" && JoistEnabled)
            return JoistTakeoffCalculator.FormatMeasurementLabel(
                JoistTakeoffCalculator.Calculate(this, effectiveScale),
                unit,
                JoistType,
                JoistDetailedLabels);

        if (effectiveScale <= 0)
            return MType switch
            {
                "point" => $"{Points.Count} ea",
                "line"  => $"{Points.Count - 1} seg",
                "area"  => $"{Points.Count} pts",
                _       => "",
            };

        double v = Value(effectiveScale);
        return MType switch
        {
            "point" => Units.FormatCount(v),
            "line"  => Units.FormatLength(v, unit),
            "area"  => Units.FormatArea(v, unit),
            _       => "",
        };
    }

    private double LineLengthPt()
    {
        double total = 0;
        for (int i = 1; i < Points.Count; i++)
        {
            total += MeasurementGeometry.Distance(Points[i], Points[i - 1]);
        }
        return total;
    }

    private double PolygonAreaPt()
    {
        double area = PolygonAreaPt(Points);
        for (int i = 0; i < Holes.Count; i++)
        {
            var hole = Holes[i];
            WarnIfHoleAppearsOutsidePolygon(hole, i);
            area -= PolygonAreaPt(hole);
        }

        return Math.Max(0, area);
    }

    private void WarnIfHoleAppearsOutsidePolygon(IReadOnlyList<SKPoint> hole, int holeIndex)
    {
        if (Points.Count < 3 || hole.Count < 3)
            return;

        SKPoint centroid = MeasurementGeometry.Centroid(hole);
        if (MeasurementGeometry.PointInPolygon(centroid, Points))
            return;

        string key = $"{Id}:{holeIndex}";
        lock (HoleWarningLock)
        {
            if (!HoleContainmentWarnings.Add(key))
                return;
        }

        AppLog.Warn($"Area hole centroid is outside parent polygon for measurement {Id} ({Name}), hole {holeIndex}.");
    }

    private static double PolygonAreaPt(IReadOnlyList<SKPoint> points)
    {
        int n = points.Count;
        if (n < 3) return 0;
        double area = 0;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            area += points[i].X * points[j].Y;
            area -= points[j].X * points[i].Y;
        }
        return Math.Abs(area) / 2.0;
    }

}

public sealed class JoistExtraSegment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public SKPoint Start { get; set; }
    public SKPoint End { get; set; }
}
