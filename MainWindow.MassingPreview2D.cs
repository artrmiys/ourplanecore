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
using OurPlanCore.Controls;
using SkiaSharp;
using Path = System.IO.Path;

namespace OurPlanCore;

public partial class MainWindow
{
    // 2D massing footprint, roof guide, and source-point preview drawing.

    private void DrawMassingPreview(SmartMassingDraft? draft)
    {
        if (_massingPreviewCanvas == null)
            return;

        _massingPreviewCanvas.Children.Clear();

        List<SmartMassingPoint> points = draft?.Footprints
            .SelectMany(footprint => footprint.Points)
            .Where(point => !double.IsNaN(point.X) && !double.IsNaN(point.Y))
            .ToList() ?? [];

        if (draft == null || points.Count == 0)
        {
            if (_massingPreviewStatusText != null)
            {
                _massingPreviewStatusText.Text = draft == null
                    ? "Build a draft to preview the footprint."
                    : "Draft has no footprint points yet.";
            }
            return;
        }

        double width = _massingPreviewCanvas.ActualWidth > 40 ? _massingPreviewCanvas.ActualWidth : 280;
        double height = _massingPreviewCanvas.ActualHeight > 40 ? _massingPreviewCanvas.ActualHeight : 160;
        double minX = points.Min(point => point.X);
        double maxX = points.Max(point => point.X);
        double minY = points.Min(point => point.Y);
        double maxY = points.Max(point => point.Y);
        if (Math.Abs(maxX - minX) < 0.001)
        {
            minX -= 1;
            maxX += 1;
        }
        if (Math.Abs(maxY - minY) < 0.001)
        {
            minY -= 1;
            maxY += 1;
        }

        const double margin = 20;
        double scale = Math.Min((width - margin * 2) / (maxX - minX), (height - margin * 2) / (maxY - minY));
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            scale = 1;

        Point Project(SmartMassingPoint point)
        {
            double x = margin + (point.X - minX) * scale;
            double y = height - margin - (point.Y - minY) * scale;
            return new Point(x, y);
        }

        Brush footprintFill = new SolidColorBrush(Color.FromArgb(42, 96, 165, 250));
        Brush footprintStroke = new SolidColorBrush(Color.FromRgb(96, 165, 250));
        Brush selectedStroke = new SolidColorBrush(Color.FromRgb(255, 183, 77));
        string selectedMarkerId = _selectedMassingMarkerId;
        if (string.IsNullOrWhiteSpace(selectedMarkerId))
            selectedMarkerId = SelectedMassingMarkerRow()?.MarkerId ?? "";

        List<SmartMassingFootprint> previewFootprints = draft.Footprints
            .Where(footprint => footprint.Points.Count > 0)
            .ToList();

        foreach (SmartMassingFootprint footprint in previewFootprints)
        {
            var polygon = new System.Windows.Shapes.Polygon
            {
                Fill = footprintFill,
                Stroke = footprintStroke,
                StrokeThickness = 1.5,
                Points = new PointCollection(footprint.Points.Select(Project)),
                ToolTip = $"{footprint.Id}: level {footprint.Level}, base {footprint.BaseElevation:F2} {footprint.BaseElevationUnits}, height {footprint.Height:F2} {footprint.HeightUnits}, {footprint.Points.Count} points",
            };
            _massingPreviewCanvas.Children.Add(polygon);
        }

        DrawMassingRoofGuides(draft.Roof.Guides, Project, selectedMarkerId, selectedStroke);

        foreach (SmartMassingFootprint footprint in previewFootprints)
        {
            for (int i = 0; i < footprint.Points.Count; i++)
            {
                SmartMassingPoint point = footprint.Points[i];
                bool selected = !string.IsNullOrWhiteSpace(point.SourceMarkerId) &&
                    string.Equals(point.SourceMarkerId, selectedMarkerId, StringComparison.OrdinalIgnoreCase);
                AddMassingPreviewPoint(Project(point), i + 1, point.SourceMarkerId, selected, selectedStroke);
            }
        }

        if (_massingPreviewStatusText != null)
        {
            string roof = string.IsNullOrWhiteSpace(draft.Roof.Pitch)
                ? draft.Roof.Type
                : $"{draft.Roof.Type} {draft.Roof.Pitch}";
            _massingPreviewStatusText.Text =
                $"{points.Count} footprint pts | {draft.Units} | roof: {roof} ({draft.Roof.Status}) | roof guides: {draft.Roof.Guides.Count} | questions: {draft.UnresolvedQuestions.Count}";
        }
    }

    private void DrawMassingRoofGuides(
        IReadOnlyList<SmartMassingRoofGuide> guides,
        Func<SmartMassingPoint, Point> project,
        string selectedMarkerId,
        Brush selectedStroke)
    {
        if (_massingPreviewCanvas == null || guides.Count == 0)
            return;

        foreach (SmartMassingRoofGuide guide in guides)
        {
            if (string.Equals(guide.Status, "rejected", StringComparison.OrdinalIgnoreCase))
                continue;

            List<Point> points = guide.Points
                .Where(point => !double.IsNaN(point.X) && !double.IsNaN(point.Y))
                .Select(project)
                .ToList();
            if (points.Count == 0)
                continue;

            bool selected = !string.IsNullOrWhiteSpace(selectedMarkerId) &&
                guide.SourceMarkerIds.Contains(selectedMarkerId, StringComparer.OrdinalIgnoreCase);
            Brush stroke = selected ? selectedStroke : new SolidColorBrush(Color.FromRgb(255, 183, 77));
            Brush fill = new SolidColorBrush(Color.FromArgb(32, 255, 183, 77));

            if (guide.Kind is "eave_outline" or "cap" && points.Count >= 3)
            {
                var polygon = new System.Windows.Shapes.Polygon
                {
                    Points = new PointCollection(points),
                    Fill = guide.Kind == "cap" ? fill : Brushes.Transparent,
                    Stroke = stroke,
                    StrokeThickness = selected ? 2.2 : 1.2,
                    StrokeDashArray = new DoubleCollection { 5, 4 },
                    ToolTip = RoofGuideTooltip(guide),
                };
                _massingPreviewCanvas.Children.Add(polygon);
                AddMassingGuideLabel(points[0], guide.Kind == "cap" ? "roof cap" : "eave", stroke, selected);
                continue;
            }

            if (guide.Kind == "slope_arrow" && points.Count >= 2)
            {
                AddMassingGuideLine(points[0], points[^1], stroke, selected, RoofGuideTooltip(guide), dashed: false);
                AddMassingArrowHead(points[^2], points[^1], stroke);
                AddMassingGuideLabel(Midpoint(points[0], points[^1]), "slope", stroke, selected);
                continue;
            }

            if (points.Count >= 2)
            {
                AddMassingGuideLine(points[0], points[^1], stroke, selected, RoofGuideTooltip(guide), dashed: true);
                string label = guide.Kind switch
                {
                    "hip_ridge" => "hip ridge",
                    "axis_candidate" => "roof axis",
                    "valley" => "valley",
                    "roof_edge" => "roof edge",
                    "high_edge" => "high edge",
                    "low_edge" => "low edge",
                    "overhang" => "overhang",
                    _ => "ridge",
                };
                AddMassingGuideLabel(Midpoint(points[0], points[^1]), label, stroke, selected);
            }
        }
    }

    private void AddMassingGuideLine(Point start, Point end, Brush stroke, bool selected, string tooltip, bool dashed)
    {
        if (_massingPreviewCanvas == null)
            return;

        var line = new System.Windows.Shapes.Line
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = end.X,
            Y2 = end.Y,
            Stroke = stroke,
            StrokeThickness = selected ? 2.4 : 1.8,
            ToolTip = tooltip,
        };
        if (dashed)
            line.StrokeDashArray = new DoubleCollection { 6, 4 };
        _massingPreviewCanvas.Children.Add(line);
    }

    private void AddMassingArrowHead(Point start, Point end, Brush fill)
    {
        if (_massingPreviewCanvas == null)
            return;

        double angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        const double size = 8;
        Point p1 = end;
        Point p2 = new(
            end.X - Math.Cos(angle - Math.PI / 6) * size,
            end.Y - Math.Sin(angle - Math.PI / 6) * size);
        Point p3 = new(
            end.X - Math.Cos(angle + Math.PI / 6) * size,
            end.Y - Math.Sin(angle + Math.PI / 6) * size);

        var head = new System.Windows.Shapes.Polygon
        {
            Points = new PointCollection { p1, p2, p3 },
            Fill = fill,
            Stroke = fill,
            StrokeThickness = 1,
        };
        _massingPreviewCanvas.Children.Add(head);
    }

    private void AddMassingGuideLabel(Point point, string text, Brush foreground, bool selected)
    {
        if (_massingPreviewCanvas == null)
            return;

        var label = new TextBlock
        {
            Text = text,
            FontSize = 10,
            FontWeight = FontWeights.Normal,
            Foreground = foreground,
            Background = new SolidColorBrush(Color.FromArgb(170, 20, 20, 20)),
            Padding = new Thickness(3, 1, 3, 1),
        };
        Canvas.SetLeft(label, point.X + 6);
        Canvas.SetTop(label, point.Y + 4);
        _massingPreviewCanvas.Children.Add(label);
    }

    private static Point Midpoint(Point first, Point second) =>
        new((first.X + second.X) / 2, (first.Y + second.Y) / 2);

    private static string RoofGuideTooltip(SmartMassingRoofGuide guide) =>
        $"{guide.Label}\nStatus: {guide.Status}\nKind: {guide.Kind}\nConfidence: {guide.Confidence:P0}\n{guide.Notes}".Trim();

    private void AddMassingPreviewPoint(Point point, int index, string sourceMarkerId, bool selected, Brush selectedStroke)
    {
        if (_massingPreviewCanvas == null)
            return;

        double radius = selected ? 5.5 : 3.8;
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Fill = selected ? selectedStroke : new SolidColorBrush(Color.FromRgb(96, 165, 250)),
            Stroke = selected ? Brushes.White : new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)),
            StrokeThickness = selected ? 1.5 : 1,
            ToolTip = string.IsNullOrWhiteSpace(sourceMarkerId)
                ? $"Footprint point {index}"
                : $"Footprint point {index}: {sourceMarkerId}",
        };
        if (!string.IsNullOrWhiteSpace(sourceMarkerId))
        {
            dot.Cursor = Cursors.Hand;
            dot.MouseLeftButtonDown += (_, e) =>
            {
                SelectMassingMarkerById(sourceMarkerId);
                e.Handled = true;
            };
        }
        Canvas.SetLeft(dot, point.X - radius);
        Canvas.SetTop(dot, point.Y - radius);
        _massingPreviewCanvas.Children.Add(dot);

        var label = new TextBlock
        {
            Text = index.ToString(CultureInfo.InvariantCulture),
            FontSize = 10,
            FontWeight = FontWeights.Normal,
            Foreground = selected ? selectedStroke : PreviewForegroundBrush(),
        };
        if (!string.IsNullOrWhiteSpace(sourceMarkerId))
        {
            label.Cursor = Cursors.Hand;
            label.MouseLeftButtonDown += (_, e) =>
            {
                SelectMassingMarkerById(sourceMarkerId);
                e.Handled = true;
            };
        }
        Canvas.SetLeft(label, point.X + 6);
        Canvas.SetTop(label, point.Y - 10);
        _massingPreviewCanvas.Children.Add(label);
    }

    private Brush PreviewForegroundBrush() =>
        TryFindResource("ControlForegroundBrush") as Brush ?? Brushes.White;
}
