using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace OurPlanCore.Controls;

public sealed partial class Massing3DWindow
{
    private void IndexDraftMarkerPoints(SmartMassingDraft draft, IReadOnlyList<SmartMassingFootprint> footprints, MassingSceneFrame frame)
    {
        foreach (SmartMassingFootprint footprint in footprints)
        {
            double baseZ = SmartMassingDraftService.DisplayBaseElevation(draft, footprint);
            foreach (SmartMassingPoint point in footprint.Points)
            {
                if (!string.IsNullOrWhiteSpace(point.SourceMarkerId))
                    _draftMarkerPoints[point.SourceMarkerId] = new MarkerDraftPoint(point.X, point.Y, baseZ);
            }
        }

        double roofZ = footprints.Count == 0
            ? 0
            : footprints.Max(footprint => SmartMassingDraftService.DisplayWallTopElevation(draft, footprint));
        foreach (SmartMassingRoofGuide guide in draft.Roof.Guides)
        {
            foreach (SmartMassingPoint point in guide.Points)
            {
                if (!string.IsNullOrWhiteSpace(point.SourceMarkerId))
                    _draftMarkerPoints.TryAdd(point.SourceMarkerId, new MarkerDraftPoint(point.X, point.Y, roofZ));
            }
        }

        foreach (SmartMassingPlane plane in draft.Roof.Planes)
        {
            foreach (SmartMassingVertex point in plane.Points)
            {
                if (!string.IsNullOrWhiteSpace(point.SourceMarkerId))
                    _draftMarkerPoints[point.SourceMarkerId] = new MarkerDraftPoint(point.X, point.Y, point.Z);
            }
        }

        foreach (SmartMassingOpening opening in draft.Openings)
        {
            if (!string.IsNullOrWhiteSpace(opening.SourceMarkerId))
                _draftMarkerPoints[opening.SourceMarkerId] = new MarkerDraftPoint(opening.Center.X, opening.Center.Y, opening.Center.Z);
        }

        foreach ((string markerId, MarkerDraftPoint point) in _draftMarkerPoints)
            _markerScenePoints[markerId] = ToScenePoint(point.X, point.Y, point.Z, frame);
    }

    private void BuildMarkerScenePoints(SmartMassingDraft? draft, IReadOnlyList<SmartMassingFootprint> footprints, MassingSceneFrame frame)
    {
        MarkerDraftPoint? originDraft = null;
        SmartAiMarker? originMarker = null;
        foreach ((string markerId, MarkerDraftPoint draftPoint) in _draftMarkerPoints)
        {
            if (!_markersById.TryGetValue(markerId, out SmartAiMarker? marker))
                continue;

            originDraft = draftPoint;
            originMarker = marker;
            break;
        }

        double wallTop = draft != null && footprints.Count > 0
            ? footprints.Max(footprint => SmartMassingDraftService.DisplayWallTopElevation(draft, footprint))
            : Math.Max(4, frame.ModelSpan * 0.24);

        foreach (SmartAiMarker marker in _markers)
        {
            if (string.IsNullOrWhiteSpace(marker.Id) || _markerScenePoints.ContainsKey(marker.Id))
                continue;

            double x;
            double y;
            if (originDraft != null && originMarker != null && frame.PdfScale > 0)
            {
                x = originDraft.Value.X + (marker.PdfPoint.X - originMarker.PdfPoint.X) * frame.PdfScale;
                y = originDraft.Value.Y + (marker.PdfPoint.Y - originMarker.PdfPoint.Y) * frame.PdfScale;
            }
            else
            {
                x = frame.CenterX + (marker.PdfPoint.X - frame.PdfCenterX) * frame.PdfScale;
                y = frame.CenterY + (marker.PdfPoint.Y - frame.PdfCenterY) * frame.PdfScale;
            }

            double z = MarkerHeight(marker, wallTop);
            _markerScenePoints[marker.Id] = ToScenePoint(x, y, z, frame);
        }
    }

    private void AddMarkers(Model3DGroup group, MassingBounds bounds)
    {
        double size = Math.Max(0.45, Math.Min(_sceneRadius * 0.055, 1.25));
        foreach (SmartAiMarker marker in _markers)
        {
            if (string.IsNullOrWhiteSpace(marker.Id) || !_markerScenePoints.TryGetValue(marker.Id, out Point3D point))
                continue;

            bool selected = string.Equals(marker.Id, _selectedMarkerId, StringComparison.OrdinalIgnoreCase);
            AddBox(
                group,
                bounds,
                point,
                selected ? size * 1.75 : size,
                selected ? Color.FromRgb(255, 183, 77) : MarkerColor(marker),
                selected ? 1.0 : 0.96,
                new Massing3DHitInfo(marker.Id, MarkerLabel(marker), marker.Id));
        }
    }

    private void AddBox(
        Model3DGroup group,
        MassingBounds bounds,
        Point3D center,
        double size,
        Color color,
        double opacity,
        Massing3DHitInfo info)
    {
        double h = size / 2;
        Point3D[] p =
        [
            new(center.X - h, center.Y - h, center.Z - h),
            new(center.X + h, center.Y - h, center.Z - h),
            new(center.X + h, center.Y + h, center.Z - h),
            new(center.X - h, center.Y + h, center.Z - h),
            new(center.X - h, center.Y - h, center.Z + h),
            new(center.X + h, center.Y - h, center.Z + h),
            new(center.X + h, center.Y + h, center.Z + h),
            new(center.X - h, center.Y + h, center.Z + h),
        ];
        int[] triangles =
        [
            0, 1, 2, 0, 2, 3,
            4, 6, 5, 4, 7, 6,
            0, 4, 5, 0, 5, 1,
            3, 2, 6, 3, 6, 7,
            1, 5, 6, 1, 6, 2,
            0, 3, 7, 0, 7, 4,
        ];

        var mesh = new MeshGeometry3D();
        foreach (Point3D point in p)
        {
            mesh.Positions.Add(point);
            bounds.Include(point);
        }

        foreach (int index in triangles)
            mesh.TriangleIndices.Add(index);

        var brush = new SolidColorBrush(color) { Opacity = opacity };
        var material = new DiffuseMaterial(brush);
        var model = new GeometryModel3D(mesh, material)
        {
            BackMaterial = material,
        };
        _hitInfo[model] = info;
        group.Children.Add(model);
    }

    private void RefreshMarkerRows()
    {
        _syncingSelection = true;
        try
        {
            var rows = _markers
                .Select(marker =>
                {
                    _markerScenePoints.TryGetValue(marker.Id, out Point3D point);
                    return new Marker3DRow(
                        marker.Id,
                        MarkerLabel(marker),
                        string.IsNullOrWhiteSpace(marker.Type) ? "(type)" : marker.Type,
                        string.IsNullOrWhiteSpace(marker.Page) ? "(page)" : marker.Page,
                        _markerScenePoints.ContainsKey(marker.Id)
                            ? $"{point.X:F1},{point.Y:F1},{point.Z:F1}"
                            : "-");
                })
                .ToList();

            _markerList.ItemsSource = rows;
            if (!string.IsNullOrWhiteSpace(_selectedMarkerId))
            {
                Marker3DRow? selected = rows.FirstOrDefault(row => string.Equals(row.MarkerId, _selectedMarkerId, StringComparison.OrdinalIgnoreCase));
                if (selected != null)
                    _markerList.SelectedItem = selected;
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private static double MarkerHeight(SmartAiMarker marker, double wallTop)
    {
        string type = marker.Type ?? "";
        if (type.Contains("height", StringComparison.OrdinalIgnoreCase))
            return wallTop;
        if (type.Contains("roof", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("ridge", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("valley", StringComparison.OrdinalIgnoreCase))
        {
            return wallTop + Math.Max(0.35, wallTop * 0.08);
        }
        if (type.Contains("window", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("door", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("opening", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Max(0.5, wallTop * 0.48);
        }

        return 0.18;
    }

    private static Color MarkerColor(SmartAiMarker marker)
    {
        string type = marker.Type ?? "";
        if (type.Contains("corner", StringComparison.OrdinalIgnoreCase))
            return Color.FromRgb(34, 197, 94);
        if (type.Contains("height", StringComparison.OrdinalIgnoreCase))
            return Color.FromRgb(250, 204, 21);
        if (type.Contains("roof", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("ridge", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("valley", StringComparison.OrdinalIgnoreCase))
        {
            return Color.FromRgb(56, 189, 248);
        }
        if (type.Contains("window", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("door", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("opening", StringComparison.OrdinalIgnoreCase))
        {
            return Color.FromRgb(244, 114, 182);
        }

        return Color.FromRgb(45, 212, 191);
    }

    private static string MarkerLabel(SmartAiMarker marker)
    {
        string type = string.IsNullOrWhiteSpace(marker.Type) ? "marker" : marker.Type;
        return string.IsNullOrWhiteSpace(marker.Value)
            ? $"{type} {marker.Id}"
            : $"{type}: {marker.Value}";
    }
}
