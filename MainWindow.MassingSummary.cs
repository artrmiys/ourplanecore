using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using OurPlaneCore.Controls;
using SkiaSharp;
using Path = System.IO.Path;

namespace OurPlaneCore;

public partial class MainWindow
{
    // 3D massing draft summary text helpers.

    private static string BuildMassingDraftSummary(SmartMassingDraft draft, string path)
    {
        int footprintCount = draft.Footprints.Count;
        int footprintPoints = draft.Footprints.Sum(footprint => footprint.Points.Count);
        string roofSummary = string.IsNullOrWhiteSpace(draft.Roof.Pitch)
            ? $"{draft.Roof.Type} ({draft.Roof.Confidence:P0})"
            : $"{draft.Roof.Type}, pitch {draft.Roof.Pitch} ({draft.Roof.Confidence:P0})";

        var sb = new StringBuilder();
        sb.AppendLine("3D Massing Draft");
        sb.AppendLine($"Path: {path}");
        sb.AppendLine($"Status: {draft.Status}");
        sb.AppendLine($"Units: {draft.Units}");
        sb.AppendLine($"Generated UTC: {draft.GeneratedAtUtc}");
        if (!string.IsNullOrWhiteSpace(draft.ReviewedAtUtc))
            sb.AppendLine($"Reviewed UTC: {draft.ReviewedAtUtc}");
        if (!string.IsNullOrWhiteSpace(draft.ReviewNotes))
            sb.AppendLine($"Review notes: {draft.ReviewNotes}");
        sb.AppendLine();
        sb.AppendLine("Summary");
        sb.AppendLine($"- Footprints: {footprintCount}");
        sb.AppendLine($"- Footprint points: {footprintPoints}");
        sb.AppendLine($"- Openings: {draft.Openings.Count}");
        sb.AppendLine($"- Roof: {roofSummary}");
        sb.AppendLine($"- Roof planes: {draft.Roof.Planes.Count}");
        sb.AppendLine($"- Assumptions: {draft.Assumptions.Count}");
        sb.AppendLine($"- Unresolved questions: {draft.UnresolvedQuestions.Count}");
        sb.AppendLine();
        sb.AppendLine("Build System");
        if (string.Equals(draft.Status, "draft_from_takeoffs", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("- Source: measured takeoff Area/Line items under sqfts, walls, areas, floors, or slabs.");
            sb.AppendLine("- Levels: floor labels and level folders become separate footprints.");
            sb.AppendLine("- Walls: each footprint edge extrudes vertically by the resolved/default wall height.");
            sb.AppendLine("- Roof: top footprint creates eave outline and candidate roof-axis guides until roof markers/review refine it.");
        }
        else
        {
            sb.AppendLine("- Source: reviewed AI markers and their crop/JSON evidence.");
            sb.AppendLine("- Levels: exterior corner markers become footprint levels.");
            sb.AppendLine("- Walls: each footprint edge extrudes vertically by reviewed/default wall height.");
            sb.AppendLine("- Roof: roof markers create reviewable eave, ridge, hip, valley, edge, or slope guides.");
        }

        foreach (SmartMassingFootprint footprint in draft.Footprints)
        {
            sb.AppendLine();
            sb.AppendLine($"Footprint {footprint.Id}");
            sb.AppendLine($"- Level: {footprint.Level}");
            sb.AppendLine($"- Page: {footprint.Page}");
            sb.AppendLine($"- Base elevation: {footprint.BaseElevation:F2} {footprint.BaseElevationUnits}");
            sb.AppendLine($"- Height: {footprint.Height:F2} {footprint.HeightUnits}");
            sb.AppendLine($"- Confidence: {footprint.Confidence:P0}");
            sb.AppendLine($"- Points: {footprint.Points.Count}");
            foreach (SmartMassingPoint point in footprint.Points)
                sb.AppendLine($"  - {point.X:F3}, {point.Y:F3} ({point.SourceMarkerId})");
        }

        sb.AppendLine();
        sb.AppendLine("Roof");
        sb.AppendLine($"- Status: {draft.Roof.Status}");
        sb.AppendLine($"- Type: {draft.Roof.Type}");
        if (draft.Roof.Elevation > 0)
            sb.AppendLine($"- Elevation: {draft.Roof.Elevation:F2} {draft.Roof.ElevationUnits}");
        sb.AppendLine($"- Pitch: {(string.IsNullOrWhiteSpace(draft.Roof.Pitch) ? "unknown" : draft.Roof.Pitch)}");
        sb.AppendLine($"- Confidence: {draft.Roof.Confidence:P0}");
        if (!string.IsNullOrWhiteSpace(draft.Roof.ReviewedAtUtc))
            sb.AppendLine($"- Reviewed UTC: {draft.Roof.ReviewedAtUtc}");
        sb.AppendLine($"- Guides: {draft.Roof.Guides.Count}");
        sb.AppendLine($"- Planes: {draft.Roof.Planes.Count}");
        foreach (SmartMassingRoofGuide guide in draft.Roof.Guides)
        {
            sb.AppendLine($"  - {guide.Kind}: {guide.Label} ({guide.Status}, {guide.Points.Count} pts, {guide.Confidence:P0})");
            if (!string.IsNullOrWhiteSpace(guide.Notes))
                sb.AppendLine($"    {guide.Notes}");
        }
        foreach (SmartMassingPlane plane in draft.Roof.Planes)
        {
            sb.AppendLine($"  - plane {plane.Kind}: {plane.Label} ({plane.Status}, {plane.Points.Count} pts, {plane.Confidence:P0})");
            if (!string.IsNullOrWhiteSpace(plane.Notes))
                sb.AppendLine($"    {plane.Notes}");
        }
        if (!string.IsNullOrWhiteSpace(draft.Roof.Notes))
            sb.AppendLine($"- Notes: {draft.Roof.Notes}");
        if (!string.IsNullOrWhiteSpace(draft.Roof.ReviewNotes))
            sb.AppendLine($"- Review notes: {draft.Roof.ReviewNotes}");

        sb.AppendLine();
        sb.AppendLine("Openings");
        if (draft.Openings.Count == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (SmartMassingOpening opening in draft.Openings)
            {
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "- {0}: {1} ({2}, wall {3}, center {4:0.###}/{5:0.###}/{6:0.###}, {7:0.###} x {8:0.###}, {9:P0})",
                    opening.Type,
                    opening.SourceMarkerId,
                    opening.Status,
                    opening.WallIndex,
                    opening.Center.X,
                    opening.Center.Y,
                    opening.Center.Z,
                    opening.Width,
                    opening.Height,
                    opening.Confidence));
                if (!string.IsNullOrWhiteSpace(opening.Notes))
                    sb.AppendLine($"  {opening.Notes}");
            }
        }

        AppendMassingList(sb, "Assumptions", draft.Assumptions);
        AppendMassingList(sb, "Unresolved Questions", draft.UnresolvedQuestions);
        AppendMassingList(sb, "Source Markers", draft.SourceMarkerIds);
        return sb.ToString();
    }

    private static void AppendMassingList(StringBuilder sb, string title, IReadOnlyList<string> items)
    {
        sb.AppendLine();
        sb.AppendLine(title);
        if (items.Count == 0)
        {
            sb.AppendLine("- none");
            return;
        }

        foreach (string item in items)
            sb.AppendLine($"- {item}");
    }
}
