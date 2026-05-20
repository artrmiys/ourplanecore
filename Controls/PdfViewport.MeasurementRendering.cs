using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Docnet.Core;
using Docnet.Core.Models;
using OurPlaneCore;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    // ── Draw finalized measurements ───────────────────────────────────────────

    private IReadOnlyList<Measurement> DrawMeasurements(
        SKCanvas canvas,
        SKRect visiblePdf,
        IReadOnlyList<Measurement> activeMeasurements)
    {
        bool drawDetails = ViewportRenderPolicy.ShouldDrawMeasurementDetails(
            _zoom,
            activeMeasurements.Count,
            _renderNavigationFastFrame) &&
            !ShouldReduceOverlayDetailForInteraction();
        bool drawGeometry = ViewportRenderPolicy.ShouldDrawMeasurementGeometry(
            activeMeasurements.Count,
            _renderNavigationFastFrame);

        IReadOnlyList<Measurement> renderCandidates = VisibleMeasurementCandidates(visiblePdf);
        List<Measurement>? visibleMeasurements = null;
        foreach (var m in renderCandidates)
        {
            bool selected = IsMeasurementSelected(m);
            if (!drawGeometry && !selected)
                continue;
            if (!selected && !IsMeasurementVisible(m, visiblePdf))
                continue;

            visibleMeasurements ??= new List<Measurement>(Math.Min(renderCandidates.Count, 256));
            visibleMeasurements.Add(m);
            DrawMeasurement(canvas, m, selected, drawLabels: false, drawDetails: drawDetails);
            if (!_renderNavigationFastFrame && selected && !ReferenceEquals(m, _selectedMeasurement))
                DrawSelectionBounds(canvas, m);
            if (!_renderNavigationFastFrame && ShouldDrawMeasurementHandles(m))
                DrawSelectionHandles(canvas, m);
        }

        return visibleMeasurements is not null
            ? visibleMeasurements
            : Array.Empty<Measurement>();
    }

    private void DrawMeasurement(SKCanvas canvas, Measurement m, bool selected, bool drawLabels, bool drawDetails = true)
    {
        SKColor color = GetCachedColor(m.Color, SKColors.Red);
        float strokeScale = MeasurementStrokeScaleFactor();
        using var stroke = new SKPaint
        {
            Color       = color,
            StrokeWidth = ScreenToPdfDistance((selected ? 3f : 2f) * strokeScale),
            IsAntialias = true,
            Style       = SKPaintStyle.Stroke,
        };
        using var fill = new SKPaint
        {
            Color       = color.WithAlpha(180),
            IsAntialias = true,
            Style       = SKPaintStyle.Fill,
        };

        var pts = m.Points;
        float pointSizeScale = PointSizeScaleFactor();

        switch (m.MType)
        {
            case "point":
                foreach (var p in pts)
                    DrawCountPoint(canvas, p, color.WithAlpha(180), m.CountSymbol, pointSizeScale);
                if (drawLabels && pts.Count > 0 && ShouldDrawMeasurementLabel("point"))
                    DrawLabel(canvas, pts[^1], m.Label(ScaleMetersPerPt, UnitMode), m.Color);
                break;

            case "line" when pts.Count >= 2:
                using (var path = new SKPath())
                {
                path.MoveTo(pts[0]);
                for (int i = 1; i < pts.Count; i++) path.LineTo(pts[i]);
                canvas.DrawPath(path, stroke);
                }
                float pr = ScreenToPdfDistance(3f * pointSizeScale);
                foreach (var p in pts)
                    canvas.DrawCircle(p, pr, fill);
                if (drawLabels && ShouldDrawMeasurementLabel("line"))
                    DrawLabel(canvas, pts[^1], m.Label(ScaleMetersPerPt, UnitMode), m.Color);
                break;

            case "area" when pts.Count >= 3:
                using (var poly = BuildAreaPath(m))
                {
                using (var fillTrans = fill.Clone())
                {
                    fillTrans.Color = fillTrans.Color.WithAlpha(AreaFillAlpha());
                    canvas.DrawPath(poly, fillTrans);
                }
                using (var areaStroke = stroke.Clone())
                {
                    areaStroke.StrokeWidth =
                        ScreenToPdfDistance((selected ? 3f : 2f) * strokeScale * AreaEdgeScaleFactor());
                    canvas.DrawPath(poly, areaStroke);
                }
                }
                if (drawDetails)
                    DrawJoistLayout(canvas, m, color, drawLabels);
                var cen = Centroid(pts);
                if (drawLabels && ShouldDrawMeasurementLabel("area"))
                    DrawLabel(canvas, cen, m.Label(ScaleMetersPerPt, UnitMode), m.Color);
                break;
        }
    }

    private void DrawCountPoint(SKCanvas canvas, SKPoint point, SKColor color, string countSymbol, float pointSizeScale)
    {
        float size = ScreenToPdfDistance(10f * pointSizeScale);
        var box = new SKRect(
            point.X - size / 2f,
            point.Y - size / 2f,
            point.X + size / 2f,
            point.Y + size / 2f);
        MeasurementGlyph.DrawSkia(canvas, MeasurementGlyph.CountKind(countSymbol), color, box);
    }

    private void DrawMeasurementLabels(
        SKCanvas canvas,
        SKRect visiblePdf,
        IReadOnlyList<Measurement> activeMeasurements,
        IReadOnlyList<Measurement>? visibleMeasurements)
    {
        if (!ViewportRenderPolicy.ShouldDrawMeasurementLabels(
                _zoom,
                activeMeasurements.Count,
                _renderNavigationFastFrame) ||
            ShouldReduceOverlayDetailForInteraction())
        {
            return;
        }

        IReadOnlyList<Measurement> labelMeasurements =
            visibleMeasurements ?? VisibleMeasurements(visiblePdf);
        foreach (var measurement in labelMeasurements)
        {
            DrawMeasurementTopLabels(canvas, measurement);
        }
    }

    private IReadOnlyList<Measurement> VisibleMeasurements(SKRect visiblePdf)
    {
        IReadOnlyList<Measurement> candidates = VisibleMeasurementCandidates(visiblePdf);
        var visibleMeasurements = new List<Measurement>(Math.Min(candidates.Count, 256));
        foreach (Measurement measurement in candidates)
        {
            if (IsMeasurementSelected(measurement) || IsMeasurementVisible(measurement, visiblePdf))
                visibleMeasurements.Add(measurement);
        }

        return visibleMeasurements;
    }

    private IReadOnlyList<Measurement> VisibleMeasurementCandidates(SKRect visiblePdf)
    {
        SKRect searchRect = visiblePdf;
        float padding = ViewportRenderPolicy.VisibleGeometryPaddingPdf(_zoom);
        searchRect.Inflate(padding, padding);
        IReadOnlyList<Measurement> candidates = ActivePageMeasurementsNear(searchRect);
        if (_selectedMeasurements.Count == 0)
            return candidates;

        List<Measurement>? merged = null;
        var seen = new HashSet<Measurement>(candidates);
        foreach (Measurement selected in GetSelectedMeasurements())
        {
            if (!seen.Add(selected))
                continue;

            merged ??= candidates.ToList();
            merged.Add(selected);
        }

        return merged ?? candidates;
    }

    private void DrawMeasurementTopLabels(SKCanvas canvas, Measurement measurement)
    {
        bool isJoistArea = measurement.MType == "area" && measurement.JoistEnabled;
        if (isJoistArea)
            DrawJoistLayoutLabels(canvas, measurement);

        var points = measurement.Points;
        switch (measurement.MType)
        {
            case "point" when points.Count > 0 && ShouldDrawMeasurementLabel("point"):
            case "line" when points.Count >= 2 && ShouldDrawMeasurementLabel("line"):
                DrawLabel(canvas, points[^1], measurement.Label(ScaleMetersPerPt, UnitMode), measurement.Color);
                break;
            case "area" when points.Count >= 3:
                // For a joist area the centroid label is the joist summary, so it
                // follows the Joist toggle; a plain area follows the Area toggle.
                if (isJoistArea ? ShouldDrawJoistLabels() : ShouldDrawMeasurementLabel("area"))
                    DrawLabel(canvas, Centroid(points), measurement.Label(ScaleMetersPerPt, UnitMode), measurement.Color);
                break;
        }
    }

    private static SKPath BuildAreaPath(Measurement measurement)
    {
        var path = new SKPath
        {
            FillType = SKPathFillType.EvenOdd,
        };
        AddClosedContour(path, measurement.Points);
        foreach (var hole in measurement.Holes)
            AddClosedContour(path, hole);
        return path;
    }

    private static void AddClosedContour(SKPath path, IReadOnlyList<SKPoint> points)
    {
        if (points.Count < 3)
            return;

        path.MoveTo(points[0]);
        for (int i = 1; i < points.Count; i++)
            path.LineTo(points[i]);
        path.Close();
    }

    private bool ShouldDrawMeasurementLabel(string measurementType)
    {
        if (!ShowMeasurementLabels)
            return false;

        return measurementType switch
        {
            "point" => ShowCountLabels,
            "area" => ShowAreaLabels,
            _ => ShowLineLabels,
        };
    }

    private bool ShouldReduceOverlayDetailForInteraction() =>
        _aiCropNoteSelecting ||
        _boxSelecting ||
        _drawPts.Count > 0 ||
        _scalePts.Count > 0 ||
        _draggingVertex ||
        _draggingMeasurement ||
        _draggingAnnotationVertex ||
        _draggingAnnotation ||
        _draggingTransformScale ||
        _draggingTransformRotate ||
        _joistDirectionMeasurement != null;
    private float MeasurementStrokeScaleFactor() =>
        (float)Math.Clamp(MeasurementStrokeScale, 0.25, 4.0);

    private float AreaEdgeScaleFactor() =>
        (float)Math.Clamp(AreaEdgeScale, 0.25, 4.0);

    private byte AreaFillAlpha() =>
        (byte)Math.Clamp((int)Math.Round(Math.Clamp(AreaFillOpacity, 0.0, 1.0) * 255.0), 0, 255);

    private float PointSizeScaleFactor() =>
        (float)Math.Clamp(PointSizeScale, 0.25, 4.0);

    private bool ShouldDrawJoistLabels() =>
        ShowMeasurementLabels && ShowJoistLabels;

}
