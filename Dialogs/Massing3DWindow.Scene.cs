using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace OurPlanCore.Controls;

public sealed partial class Massing3DWindow
{
    private void RenderScene(bool preserveCamera)
    {
        _viewport.Children.Clear();
        _hitInfo.Clear();
        _markerScenePoints.Clear();
        _draftMarkerPoints.Clear();

        SmartMassingDraft? draft = _draft;
        if (draft != null)
            SmartMassingDraftService.RefreshDerivedGeometry(draft);

        List<SmartMassingFootprint> footprints = draft?.Footprints
            .Where(candidate => candidate.Points.Count >= 3)
            .OrderBy(candidate => SmartMassingDraftService.DisplayBaseElevation(draft, candidate))
            .ThenBy(candidate => candidate.Level)
            .ToList() ?? [];
        MassingSceneFrame frame = BuildSceneFrame(draft, footprints);

        var group = new Model3DGroup();
        group.Children.Add(new AmbientLight(Color.FromRgb(88, 88, 88)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(245, 245, 245), new Vector3D(-0.45, -0.85, -0.35)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(135, 165, 195), new Vector3D(0.65, -0.35, 0.55)));

        var bounds = new MassingBounds();
        if (draft != null && footprints.Count > 0)
        {
            foreach (SmartMassingFootprint footprint in footprints)
                AddFootprint(group, bounds, draft, footprint, frame);
            AddRoofPlanes(group, bounds, draft, frame);
            AddOpenings(group, bounds, draft, frame);
            IndexDraftMarkerPoints(draft, footprints, frame);
        }

        BuildMarkerScenePoints(draft, footprints, frame);
        if (_showMarkersBox.IsChecked == true)
            AddMarkers(group, bounds);

        _viewport.Children.Add(new ModelVisual3D { Content = group });

        if (bounds.IsValid)
        {
            _target = bounds.Center;
            _sceneRadius = Math.Max(4, bounds.Radius);
        }
        else
        {
            _target = new Point3D(0, 2, 0);
            _sceneRadius = 8;
        }

        RefreshMarkerRows();
        if (preserveCamera)
            UpdateCamera();
        else
            FitView(resetAngles: true);

        UpdateStatus(draft, footprints);
    }

    private MassingSceneFrame BuildSceneFrame(SmartMassingDraft? draft, IReadOnlyList<SmartMassingFootprint> footprints)
    {
        if (draft != null && footprints.Count > 0 && TryGetDraftBounds(draft, out double minX, out double maxX, out double minY, out double maxY, out double maxZ))
        {
            double span = Math.Max(Math.Max(maxX - minX, maxY - minY), Math.Max(maxZ, 1));
            return new MassingSceneFrame(
                (minX + maxX) / 2,
                (minY + maxY) / 2,
                span,
                PdfCenterX: _markers.Count == 0 ? 0 : _markers.Average(marker => marker.PdfPoint.X),
                PdfCenterY: _markers.Count == 0 ? 0 : _markers.Average(marker => marker.PdfPoint.Y),
                PdfScale: EstimatePdfScale(draft, footprints, span));
        }

        double minPdfX = _markers.Count == 0 ? -100 : _markers.Min(marker => (double)marker.PdfPoint.X);
        double maxPdfX = _markers.Count == 0 ? 100 : _markers.Max(marker => (double)marker.PdfPoint.X);
        double minPdfY = _markers.Count == 0 ? -100 : _markers.Min(marker => (double)marker.PdfPoint.Y);
        double maxPdfY = _markers.Count == 0 ? 100 : _markers.Max(marker => (double)marker.PdfPoint.Y);
        double pdfSpan = Math.Max(Math.Max(maxPdfX - minPdfX, maxPdfY - minPdfY), 1);
        double targetSpan = 28;
        return new MassingSceneFrame(
            0,
            0,
            targetSpan,
            (minPdfX + maxPdfX) / 2,
            (minPdfY + maxPdfY) / 2,
            targetSpan / pdfSpan);
    }

    private void AddFootprint(
        Model3DGroup group,
        MassingBounds bounds,
        SmartMassingDraft draft,
        SmartMassingFootprint footprint,
        MassingSceneFrame frame)
    {
        double baseZ = SmartMassingDraftService.DisplayBaseElevation(draft, footprint);
        double wallTop = SmartMassingDraftService.DisplayWallTopElevation(draft, footprint);
        AddSurface(
            group,
            bounds,
            $"floor_level_{footprint.Level}",
            $"Level {footprint.Level} floor",
            footprint.SourceMarkerIds,
            footprint.Points.Select(point => ToScenePoint(point.X, point.Y, baseZ, frame)).ToList(),
            Color.FromRgb(82, 91, 102),
            0.42);

        for (int i = 0; i < footprint.Points.Count; i++)
        {
            SmartMassingPoint start = footprint.Points[i];
            SmartMassingPoint end = footprint.Points[(i + 1) % footprint.Points.Count];
            List<string> sourceIds = new[] { start.SourceMarkerId, end.SourceMarkerId }
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            AddSurface(
                group,
                bounds,
                $"wall_level_{footprint.Level}_{i + 1}",
                $"Level {footprint.Level} wall {i + 1}",
                sourceIds,
                [
                    ToScenePoint(start.X, start.Y, baseZ, frame),
                    ToScenePoint(end.X, end.Y, baseZ, frame),
                    ToScenePoint(end.X, end.Y, wallTop, frame),
                    ToScenePoint(start.X, start.Y, wallTop, frame),
                ],
                Color.FromRgb(148, 163, 184),
                0.72);
        }
    }

    private void AddRoofPlanes(
        Model3DGroup group,
        MassingBounds bounds,
        SmartMassingDraft draft,
        MassingSceneFrame frame)
    {
        foreach (SmartMassingPlane plane in draft.Roof.Planes)
        {
            if (plane.Points.Count < 3 || string.Equals(plane.Status, "rejected", StringComparison.OrdinalIgnoreCase))
                continue;

            Color color = plane.Kind.Contains("candidate", StringComparison.OrdinalIgnoreCase)
                ? Color.FromRgb(245, 158, 11)
                : Color.FromRgb(71, 123, 156);
            AddSurface(
                group,
                bounds,
                plane.Id,
                string.IsNullOrWhiteSpace(plane.Label) ? plane.Kind : plane.Label,
                plane.SourceMarkerIds,
                plane.Points.Select(point => ToScenePoint(point.X, point.Y, point.Z, frame)).ToList(),
                color,
                plane.Status == "reviewed" ? 0.86 : 0.68);
        }
    }

    private void AddOpenings(
        Model3DGroup group,
        MassingBounds bounds,
        SmartMassingDraft draft,
        MassingSceneFrame frame)
    {
        foreach (SmartMassingOpening opening in draft.Openings)
        {
            SmartMassingFootprint? footprint = FootprintForOpening(draft, opening);
            if (string.Equals(opening.Status, "rejected", StringComparison.OrdinalIgnoreCase) ||
                footprint == null ||
                opening.WallIndex < 0 ||
                opening.WallIndex >= footprint.Points.Count ||
                opening.Width <= 0 ||
                opening.Height <= 0)
            {
                continue;
            }

            SmartMassingPoint start = footprint.Points[opening.WallIndex];
            SmartMassingPoint end = footprint.Points[(opening.WallIndex + 1) % footprint.Points.Count];
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= 0.0001)
                continue;

            double ux = dx / length;
            double uy = dy / length;
            double halfW = Math.Min(opening.Width / 2.0, length / 2.0);
            double halfH = opening.Height / 2.0;
            double zMin = Math.Max(0.03, opening.Center.Z - halfH);
            double zMax = opening.Center.Z + halfH;
            double x1 = opening.Center.X - ux * halfW;
            double y1 = opening.Center.Y - uy * halfW;
            double x2 = opening.Center.X + ux * halfW;
            double y2 = opening.Center.Y + uy * halfW;
            Color color = opening.Type switch
            {
                "door" => Color.FromRgb(250, 204, 21),
                "window" => Color.FromRgb(34, 211, 238),
                _ => Color.FromRgb(168, 85, 247),
            };
            AddSurface(
                group,
                bounds,
                $"opening_{opening.SourceMarkerId}",
                string.IsNullOrWhiteSpace(opening.Type) ? "Opening" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(opening.Type),
                string.IsNullOrWhiteSpace(opening.SourceMarkerId) ? [] : [opening.SourceMarkerId],
                [
                    ToScenePoint(x1, y1, zMin, frame),
                    ToScenePoint(x2, y2, zMin, frame),
                    ToScenePoint(x2, y2, zMax, frame),
                    ToScenePoint(x1, y1, zMax, frame),
                ],
                color,
                0.92);
        }
    }

    private void AddSurface(
        Model3DGroup group,
        MassingBounds bounds,
        string id,
        string label,
        IReadOnlyList<string> sourceMarkerIds,
        IReadOnlyList<Point3D> points,
        Color color,
        double opacity)
    {
        if (points.Count < 3)
            return;

        var mesh = new MeshGeometry3D();
        foreach (Point3D point in points)
        {
            mesh.Positions.Add(point);
            bounds.Include(point);
        }

        for (int i = 1; i < points.Count - 1; i++)
        {
            mesh.TriangleIndices.Add(0);
            mesh.TriangleIndices.Add(i);
            mesh.TriangleIndices.Add(i + 1);
        }

        bool selected = sourceMarkerIds.Any(id => string.Equals(id, _selectedMarkerId, StringComparison.OrdinalIgnoreCase));
        var brush = new SolidColorBrush(selected ? Color.FromRgb(255, 183, 77) : color)
        {
            Opacity = selected ? Math.Min(1, opacity + 0.12) : opacity,
        };
        var material = new DiffuseMaterial(brush);
        var model = new GeometryModel3D(mesh, material)
        {
            BackMaterial = material,
        };
        _hitInfo[model] = new Massing3DHitInfo(id, label, sourceMarkerIds.FirstOrDefault() ?? "");
        group.Children.Add(model);
    }

    private void UpdateStatus(SmartMassingDraft? draft, IReadOnlyList<SmartMassingFootprint> footprints)
    {
        int walls = footprints.Sum(footprint => footprint.Points.Count);
        int planes = draft?.Roof.Planes.Count(plane => !string.Equals(plane.Status, "rejected", StringComparison.OrdinalIgnoreCase)) ?? 0;
        int openings = draft?.Openings.Count(opening => !string.Equals(opening.Status, "rejected", StringComparison.OrdinalIgnoreCase)) ?? 0;
        string source = draft == null
            ? "marker-only preview"
            : string.IsNullOrWhiteSpace(draft.Status) ? "draft" : draft.Status;
        _statusText.Text = $"Scene: {source} | levels: {footprints.Count} | walls: {walls} | roof planes: {planes} | openings: {openings} | markers: {_markerScenePoints.Count}/{_markers.Count}. Drag to orbit, wheel to zoom, click marker/object to select.";
    }

    private double EstimatePdfScale(SmartMassingDraft draft, IReadOnlyList<SmartMassingFootprint> footprints, double fallbackSpan)
    {
        var pairs = footprints.SelectMany(footprint => footprint.Points)
            .Where(point => !string.IsNullOrWhiteSpace(point.SourceMarkerId) && _markersById.ContainsKey(point.SourceMarkerId))
            .Select(point => new
            {
                Draft = point,
                Marker = _markersById[point.SourceMarkerId],
            })
            .ToList();

        List<double> scales = [];
        for (int i = 0; i < pairs.Count; i++)
        {
            for (int j = i + 1; j < pairs.Count; j++)
            {
                double dx = pairs[i].Draft.X - pairs[j].Draft.X;
                double dy = pairs[i].Draft.Y - pairs[j].Draft.Y;
                double modelDistance = Math.Sqrt(dx * dx + dy * dy);
                double pdx = pairs[i].Marker.PdfPoint.X - pairs[j].Marker.PdfPoint.X;
                double pdy = pairs[i].Marker.PdfPoint.Y - pairs[j].Marker.PdfPoint.Y;
                double pdfDistance = Math.Sqrt(pdx * pdx + pdy * pdy);
                if (modelDistance > 0.0001 && pdfDistance > 0.0001)
                    scales.Add(modelDistance / pdfDistance);
            }
        }

        if (scales.Count > 0)
        {
            scales.Sort();
            return scales[scales.Count / 2];
        }

        double minPdfX = _markers.Count == 0 ? -100 : _markers.Min(marker => (double)marker.PdfPoint.X);
        double maxPdfX = _markers.Count == 0 ? 100 : _markers.Max(marker => (double)marker.PdfPoint.X);
        double minPdfY = _markers.Count == 0 ? -100 : _markers.Min(marker => (double)marker.PdfPoint.Y);
        double maxPdfY = _markers.Count == 0 ? 100 : _markers.Max(marker => (double)marker.PdfPoint.Y);
        double pdfSpan = Math.Max(Math.Max(maxPdfX - minPdfX, maxPdfY - minPdfY), 1);
        return Math.Max(0.0001, fallbackSpan / pdfSpan);
    }

    private static bool TryGetDraftBounds(
        SmartMassingDraft draft,
        out double minX,
        out double maxX,
        out double minY,
        out double maxY,
        out double maxZ)
    {
        List<SmartMassingVertex> vertices = [];
        foreach (SmartMassingFootprint footprint in draft.Footprints.Where(footprint => footprint.Points.Count >= 3))
        {
            double baseZ = SmartMassingDraftService.DisplayBaseElevation(draft, footprint);
            double wallTopZ = SmartMassingDraftService.DisplayWallTopElevation(draft, footprint);
            vertices.AddRange(footprint.Points.Select(point => new SmartMassingVertex { X = point.X, Y = point.Y, Z = baseZ }));
            vertices.AddRange(footprint.Points.Select(point => new SmartMassingVertex { X = point.X, Y = point.Y, Z = wallTopZ }));
        }
        vertices.AddRange(draft.Roof.Planes.SelectMany(plane => plane.Points));
        vertices.AddRange(draft.Openings.Select(opening => opening.Center));
        vertices = vertices
            .Where(point => !double.IsNaN(point.X) && !double.IsNaN(point.Y) && !double.IsNaN(point.Z))
            .ToList();
        if (vertices.Count == 0)
        {
            minX = maxX = minY = maxY = maxZ = 0;
            return false;
        }

        minX = vertices.Min(point => point.X);
        maxX = vertices.Max(point => point.X);
        minY = vertices.Min(point => point.Y);
        maxY = vertices.Max(point => point.Y);
        maxZ = Math.Max(1, vertices.Max(point => point.Z));
        return maxX > minX && maxY > minY;
    }

    private static SmartMassingFootprint? FootprintForOpening(SmartMassingDraft draft, SmartMassingOpening opening)
    {
        SmartMassingFootprint? exact = draft.Footprints
            .Where(footprint => footprint.Points.Count >= 3)
            .FirstOrDefault(footprint => footprint.Level == opening.Level);
        return exact ?? draft.Footprints
            .Where(footprint => footprint.Points.Count >= 3)
            .OrderByDescending(footprint => SmartMassingDraftService.DisplayWallTopElevation(draft, footprint))
            .ThenByDescending(footprint => footprint.Level)
            .FirstOrDefault();
    }

    private static Point3D ToScenePoint(double x, double y, double z, MassingSceneFrame frame) =>
        new(x - frame.CenterX, z, y - frame.CenterY);
}
