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
        bool simplifyAreaPaint = ViewportRenderPolicy.ShouldUseSimplifiedAreaPaint(
            _zoom,
            activeMeasurements.Count,
            _renderNavigationFastFrame);

        IReadOnlyList<Measurement> renderCandidates = LayerOrderedMeasurements(
            VisibleMeasurementCandidates(visiblePdf));
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
            DrawMeasurement(canvas, m, selected, drawLabels: false, drawDetails: drawDetails, simplifyAreaPaint: simplifyAreaPaint && !selected);
            if (!_renderNavigationFastFrame && selected && !ReferenceEquals(m, _selectedMeasurement))
                DrawSelectionBounds(canvas, m);
            if (!_renderNavigationFastFrame && ShouldDrawMeasurementHandles(m))
                DrawSelectionHandles(canvas, m);
        }

        return visibleMeasurements is not null
            ? visibleMeasurements
            : Array.Empty<Measurement>();
    }

    private void DrawMeasurement(
        SKCanvas canvas,
        Measurement m,
        bool selected,
        bool drawLabels,
        bool drawDetails = true,
        bool simplifyAreaPaint = false)
    {
        SKColor color = GetCachedColor(m.Color, SKColors.Red);
        float strokeScale = MeasurementStrokeScaleFactor();
        using var stroke = new SKPaint
        {
            Color       = color,
            StrokeWidth = ScreenToPdfDistance((selected ? 3f : 2f) * strokeScale),
            IsAntialias = !simplifyAreaPaint,
            Style       = SKPaintStyle.Stroke,
        };
        using var fill = new SKPaint
        {
            Color       = color.WithAlpha(180),
            IsAntialias = !simplifyAreaPaint,
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
                    if (!simplifyAreaPaint)
                    {
                        using var fillTrans = fill.Clone();
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

    // Runs once per paint frame, so it must not allocate LINQ chains or
    // re-normalize folder paths per measurement. Rank lookups are memoized per
    // raw folder string; an already-ordered input (the common case) returns
    // as-is without any allocation.
    private IReadOnlyList<Measurement> LayerOrderedMeasurements(IReadOnlyList<Measurement> measurements)
    {
        if (measurements.Count <= 1)
            return measurements;

        bool sorted = true;
        long previousKey = MeasurementLayerSortKey(measurements[0]);
        for (int i = 1; i < measurements.Count; i++)
        {
            long key = MeasurementLayerSortKey(measurements[i]);
            if (key < previousKey)
            {
                sorted = false;
                break;
            }
            previousKey = key;
        }
        if (sorted)
            return measurements;

        var items = new Measurement[measurements.Count];
        var keys = new long[measurements.Count];
        for (int i = 0; i < measurements.Count; i++)
        {
            items[i] = measurements[i];
            // Original index in the low bits keeps the sort stable.
            keys[i] = (MeasurementLayerSortKey(items[i]) << 20) | (uint)(i & 0xFFFFF);
        }
        Array.Sort(keys, items);
        return items;
    }

    private long MeasurementLayerSortKey(Measurement measurement) =>
        (long)TakeoffLayerRank(measurement) * 4 + DefaultMeasurementLayerRank(measurement);

    // Memoizes normalized-folder rank lookups; cleared when the layer order
    // changes (SetTakeoffLayerOrder).
    private readonly Dictionary<string, int> _takeoffLayerRankByRawFolder = new(StringComparer.OrdinalIgnoreCase);

    private void InvalidateTakeoffLayerRankCache() =>
        _takeoffLayerRankByRawFolder.Clear();

    private int TakeoffLayerRank(Measurement measurement)
    {
        string folder = measurement.TakeoffFolder;
        if (string.IsNullOrWhiteSpace(folder))
            return int.MaxValue / 2;

        if (_takeoffLayerRankByRawFolder.TryGetValue(folder, out int cached))
            return cached;

        int rank = _takeoffLayerRanks.TryGetValue(NormalizePageFolderForCompare(folder), out int found)
            ? found
            : int.MaxValue / 2;
        _takeoffLayerRankByRawFolder[folder] = rank;
        return rank;
    }

    private static int DefaultMeasurementLayerRank(Measurement measurement) =>
        OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) switch
        {
            "area" => 0,
            "line" => 1,
            "point" => 2,
            _ => 1,
        };

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
        if (ShouldReduceOverlayDetailForInteraction())
        {
            return;
        }

        bool drawAllLabels =
            !_renderNavigationFastFrame &&
            ViewportRenderPolicy.ShouldDrawMeasurementLabels(
                _zoom,
                activeMeasurements.Count,
                _renderNavigationFastFrame);
        IReadOnlyList<Measurement> labelMeasurements =
            visibleMeasurements ?? VisibleMeasurements(visiblePdf);
        foreach (var measurement in labelMeasurements)
        {
            if (drawAllLabels || ShouldDrawDenseMeasurementLabel(measurement))
                DrawMeasurementTopLabels(canvas, measurement, visiblePdf);
        }
    }

    private bool ShouldDrawDenseMeasurementLabel(Measurement measurement)
    {
        if (IsMeasurementSelected(measurement))
            return true;

        return measurement.MType == "area" &&
               measurement.JoistEnabled &&
               ShouldDrawJoistSummaryLabel();
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
            if (!IsMeasurementOnActivePage(selected))
                continue;

            if (!seen.Add(selected))
                continue;

            merged ??= candidates.ToList();
            merged.Add(selected);
        }

        return merged ?? candidates;
    }

    private void DrawMeasurementTopLabels(SKCanvas canvas, Measurement measurement, SKRect visiblePdf)
    {
        bool isJoistArea = measurement.MType == "area" && measurement.JoistEnabled;
        JoistLayoutResult? joistLayout = null;
        if (isJoistArea)
        {
            joistLayout = PaintJoistLayout(measurement);
            DrawJoistLayoutLabels(canvas, measurement, joistLayout, visiblePdf);
        }

        var points = measurement.Points;
        switch (measurement.MType)
        {
            case "point" when points.Count > 0 && ShouldDrawMeasurementLabel("point"):
            case "line" when points.Count >= 2 && ShouldDrawMeasurementLabel("line"):
                if (RectContains(visiblePdf, points[^1]))
                    DrawLabel(canvas, points[^1], MeasurementLabelText(measurement, null), measurement.Color);
                break;
            case "area" when points.Count >= 3:
                if (isJoistArea ? ShouldDrawJoistSummaryLabel() : ShouldDrawMeasurementLabel("area"))
                {
                    SKPoint center = Centroid(points);
                    if (RectContains(visiblePdf, center))
                        DrawLabel(canvas, center, MeasurementLabelText(measurement, joistLayout), measurement.Color);
                }
                break;
        }
    }

    private string MeasurementLabelText(Measurement measurement, JoistLayoutResult? joistLayout)
    {
        if (measurement.MType != "area" || !measurement.JoistEnabled)
            return measurement.Label(ScaleMetersPerPt, UnitMode);

        if (!measurement.JoistDirectionLocked)
            return "joists: set direction";

        return JoistTakeoffCalculator.FormatMeasurementLabel(
            joistLayout ?? PaintJoistLayout(measurement),
            UnitMode,
            measurement.JoistType,
            measurement.JoistDetailedLabels);
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

    private bool ShouldDrawJoistSummaryLabel() =>
        ShowMeasurementLabels && ShowAreaLabels && ShowJoistLabels;

}
