using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SkiaSharp;


namespace OurPlaneCore;

public static partial class SmartMassingDraftService
{
    // Opening projection onto massing footprints.

    private static void AddOpenings(
        OurPlaneCoreJob job,
        SmartMassingDraft draft,
        IReadOnlyList<SmartAiMarker> allMarkers,
        IReadOnlyList<SmartAiMarker> openingMarkers)
    {
        if (openingMarkers.Count == 0)
            return;

        if (draft.Footprints.All(footprint => footprint.Points.Count < 3))
        {
            draft.UnresolvedQuestions.Add("Add exterior_corner markers before opening samples can be projected to walls.");
            return;
        }
        foreach (SmartAiMarker marker in openingMarkers)
        {
            SmartMassingFootprint? footprint = FootprintForMarkerLevel(draft, marker);
            if (footprint == null)
                continue;

            if (!TryResolveMassingTransform(job, footprint, allMarkers, out SKPoint origin, out double scaleMetersPerPt, out bool useFeet))
            {
                draft.UnresolvedQuestions.Add($"Could not resolve footprint transform for opening sample projection on level {footprint.Level}.");
                continue;
            }

            double baseZ = DisplayBaseElevation(draft, footprint);
            double wallHeight = DisplayWallHeight(draft, footprint);
            SmartMassingPoint point = ToMassingPoint(marker, origin, scaleMetersPerPt, useFeet);
            if (!TryProjectPointToFootprintWall(footprint, point, out int wallIndex, out SmartMassingPoint projected, out double wallLength))
                continue;

            string type = OpeningType(marker);
            OpeningDimensions(type, wallHeight, wallLength, out double width, out double height, out double bottom);
            double centerZ = Math.Min(wallHeight - height / 2, bottom + height / 2);
            centerZ = baseZ + Math.Max(height / 2, centerZ);

            draft.Openings.Add(new SmartMassingOpening
            {
                Status = "draft",
                Level = footprint.Level,
                Type = type,
                SourceMarkerId = marker.Id,
                Page = marker.Page,
                WallIndex = wallIndex,
                Center = new SmartMassingVertex
                {
                    X = projected.X,
                    Y = projected.Y,
                    Z = Math.Round(centerZ, 3),
                    SourceMarkerId = marker.Id,
                },
                Width = Math.Round(width, 3),
                Height = Math.Round(height, 3),
                Confidence = 0.28,
                Notes = "Projected from reviewed opening sample marker to nearest footprint wall. Position and size need review.",
            });
        }

        if (draft.Openings.Count > 0)
            draft.Assumptions.Add("Opening samples are projected to nearest footprint walls as visual placeholders.");
    }

    private static bool TryProjectPointToFootprintWall(
        SmartMassingFootprint footprint,
        SmartMassingPoint point,
        out int wallIndex,
        out SmartMassingPoint projected,
        out double wallLength)
    {
        wallIndex = -1;
        projected = new SmartMassingPoint();
        wallLength = 0;
        double bestDistanceSq = double.MaxValue;

        for (int i = 0; i < footprint.Points.Count; i++)
        {
            SmartMassingPoint start = footprint.Points[i];
            SmartMassingPoint end = footprint.Points[(i + 1) % footprint.Points.Count];
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double lengthSq = dx * dx + dy * dy;
            if (lengthSq <= 0.0001)
                continue;

            double t = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSq;
            t = Math.Max(0, Math.Min(1, t));
            double x = start.X + dx * t;
            double y = start.Y + dy * t;
            double distanceSq = ((point.X - x) * (point.X - x)) + ((point.Y - y) * (point.Y - y));
            if (distanceSq >= bestDistanceSq)
                continue;

            bestDistanceSq = distanceSq;
            wallIndex = i;
            wallLength = Math.Sqrt(lengthSq);
            projected = new SmartMassingPoint
            {
                X = Math.Round(x, 3),
                Y = Math.Round(y, 3),
                SourceMarkerId = point.SourceMarkerId,
            };
        }

        return wallIndex >= 0;
    }

    private static string OpeningType(SmartAiMarker marker)
    {
        if (MarkerTypeEquals(marker, "window_sample"))
            return "window";
        if (MarkerTypeEquals(marker, "door_sample"))
            return "door";
        return "opening";
    }

    private static void OpeningDimensions(
        string type,
        double wallTopZ,
        double wallLength,
        out double width,
        out double height,
        out double bottom)
    {
        double usableWall = Math.Max(1, wallTopZ);
        double usableLength = Math.Max(1, wallLength);
        if (type == "door")
        {
            height = Math.Min(usableWall * 0.82, usableWall - usableWall * 0.04);
            width = Math.Min(usableLength * 0.22, usableWall * 0.38);
            bottom = 0;
            return;
        }

        if (type == "window")
        {
            height = usableWall * 0.32;
            width = Math.Min(usableLength * 0.24, usableWall * 0.48);
            bottom = usableWall * 0.38;
            return;
        }

        height = usableWall * 0.42;
        width = Math.Min(usableLength * 0.25, usableWall * 0.52);
        bottom = usableWall * 0.24;
    }
}
