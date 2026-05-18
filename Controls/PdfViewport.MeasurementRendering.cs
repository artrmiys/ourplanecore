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

    private void DrawPageAnnotations(SKCanvas canvas, SKRect visiblePdf)
    {
        foreach (PageAnnotation annotation in _annotations)
        {
            if (!IsAnnotationVisibleOnActivePage(annotation) ||
                annotation.Points.Count < 2)
            {
                continue;
            }

            bool selected = IsAnnotationSelected(annotation);
            if (!selected && !PointsVisible(annotation.Points, visiblePdf))
                continue;

            DrawPageAnnotation(canvas, annotation, selected);
            if (selected && !_renderNavigationFastFrame)
            {
                DrawAnnotationSelectionBounds(canvas, annotation);
                DrawAnnotationSelectionHandles(canvas, annotation);
            }
        }
    }

    private void DrawPageAnnotation(SKCanvas canvas, PageAnnotation annotation, bool selected)
    {
        string kind = OurPlaneCoreJobStore.NormalizePageAnnotationKind(annotation.Kind);
        SKColor color = GetCachedColor(annotation.Color, new SKColor(0x15, 0x65, 0xC0));
        using var stroke = new SKPaint
        {
            Color = color,
            StrokeWidth = AnnotationStrokeWidth(annotation, selected),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };

        SKPoint start = annotation.Points[0];
        SKPoint end = annotation.Points[1];
        if (kind == "note")
        {
            DrawNoteAnnotation(canvas, annotation, color, selected);
            return;
        }

        if (kind == "rectangle")
        {
            if (annotation.Points.Count >= 4)
            {
                using var path = new SKPath();
                path.MoveTo(annotation.Points[0]);
                for (int i = 1; i < annotation.Points.Count; i++)
                    path.LineTo(annotation.Points[i]);
                path.Close();
                canvas.DrawPath(path, stroke);
            }
            else
            {
                canvas.DrawRect(NormalizeRect(start, end), stroke);
            }
            return;
        }

        if (kind == "cloud")
        {
            DrawCloudAnnotation(canvas, annotation, stroke);
            return;
        }

        if (kind == "area")
        {
            DrawAreaAnnotation(canvas, annotation, color, stroke);
            return;
        }

        canvas.DrawLine(start, end, stroke);
        if (kind == "arrow")
        {
            AnnotationGlyphRenderer.DrawArrowHead(canvas, start, end, stroke, ScreenToPdfDistance(9f));
            return;
        }

        if (kind == "dimension" && !_renderNavigationFastFrame)
        {
            AnnotationGlyphRenderer.DrawDimensionTicks(canvas, start, end, stroke, ScreenToPdfDistance(6f));
            DrawScreenTextBox(
                canvas,
                new SKPoint((start.X + end.X) / 2f, (start.Y + end.Y) / 2f),
                [AnnotationLabel(annotation)],
                SKColors.White,
                SKColors.Black.WithAlpha(185),
                color,
                MeasurementLabelFontScreenPx,
                MeasurementLabelPaddingScreenPx,
                centered: true);
        }
    }

    private float AnnotationStrokeWidth(PageAnnotation annotation, bool selected)
    {
        double value = annotation.StrokeWidth is >= 0.75 and <= 12.0
            ? annotation.StrokeWidth
            : 1.8;
        if (selected)
            value += 0.9;
        return ScreenToPdfDistance((float)Math.Clamp(value, 0.75, 12.9));
    }

    private void DrawAreaAnnotation(SKCanvas canvas, PageAnnotation annotation, SKColor color, SKPaint stroke)
    {
        if (annotation.Points.Count < 3)
            return;

        using var fill = new SKPaint
        {
            Color = color.WithAlpha(55),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using SKPath path = BuildClosedAnnotationPath(annotation.Points);
        canvas.DrawPath(path, fill);
        canvas.DrawPath(path, stroke);
    }

    private void DrawCloudAnnotation(SKCanvas canvas, PageAnnotation annotation, SKPaint stroke)
    {
        IReadOnlyList<SKPoint> corners = AnnotationRectangleCorners(annotation.Points);
        if (corners.Count < 4 ||
            !TryGetAnnotationLocalFrame(corners, out SKMatrix localToPdf, out float width, out float height))
        {
            return;
        }

        using var saved = new SKAutoCanvasRestore(canvas, true);
        canvas.Concat(ref localToPdf);
        using SKPath path = BuildCloudPath(width, height);
        canvas.DrawPath(path, stroke);
    }

    private static SKPath BuildCloudPath(float width, float height)
    {
        var path = new SKPath();
        if (width <= ViewportConstants.ZeroLengthEpsilon ||
            height <= ViewportConstants.ZeroLengthEpsilon)
        {
            return path;
        }

        float bump = Math.Clamp(Math.Min(width, height) / 7f, 5f, Math.Max(5f, Math.Min(width, height) / 3f));
        path.MoveTo(bump, 0);
        AddCloudEdge(path, bump, 0, width - bump, 0, 0, -bump);
        AddCloudEdge(path, width, bump, width, height - bump, bump, 0);
        AddCloudEdge(path, width - bump, height, bump, height, 0, bump);
        AddCloudEdge(path, 0, height - bump, 0, bump, -bump, 0);
        path.Close();
        return path;
    }

    private static void AddCloudEdge(
        SKPath path,
        float startX,
        float startY,
        float endX,
        float endY,
        float controlOffsetX,
        float controlOffsetY)
    {
        float length = MathF.Sqrt(MathF.Pow(endX - startX, 2) + MathF.Pow(endY - startY, 2));
        int segments = Math.Max(2, (int)MathF.Ceiling(length / 42f));
        for (int i = 1; i <= segments; i++)
        {
            float t0 = (i - 1f) / segments;
            float t1 = i / (float)segments;
            float mid = (t0 + t1) / 2f;
            var control = new SKPoint(
                startX + (endX - startX) * mid + controlOffsetX,
                startY + (endY - startY) * mid + controlOffsetY);
            var end = new SKPoint(
                startX + (endX - startX) * t1,
                startY + (endY - startY) * t1);
            path.QuadTo(control, end);
        }
    }

    private void DrawTransformOverlay(SKCanvas canvas)
    {
        if (!TryGetTransformBounds(out SKRect selectedBounds))
            return;

        SKRect outer = TransformHandleBounds(selectedBounds);
        float safeZoom = Math.Max(_zoom, 0.001f);
        SKColor orange = new(0xF4, 0x9B, 0x24);
        using var stroke = new SKPaint
        {
            Color = orange.WithAlpha(190),
            StrokeWidth = 1.4f / safeZoom,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            PathEffect = SKPathEffect.CreateDash([7f / safeZoom, 5f / safeZoom], 0),
        };
        using var handleFill = new SKPaint
        {
            Color = orange.WithAlpha(150),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var handleStroke = new SKPaint
        {
            Color = orange.WithAlpha(235),
            StrokeWidth = 1.2f / safeZoom,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };

        canvas.DrawRect(outer, stroke);

        float handle = 8f / safeZoom;
        foreach ((TransformHandleKind kind, SKPoint point) in TransformHandlePoints(outer))
        {
            if (kind == TransformHandleKind.Rotate)
            {
                SKPoint topMid = new((outer.Left + outer.Right) / 2f, outer.Top);
                canvas.DrawLine(topMid, point, handleStroke);
                canvas.DrawCircle(point, handle * 0.68f, handleFill);
                canvas.DrawCircle(point, handle * 0.68f, handleStroke);
                continue;
            }

            var rect = SKRect.Create(point.X - handle / 2f, point.Y - handle / 2f, handle, handle);
            canvas.DrawRoundRect(rect, 1.5f / safeZoom, 1.5f / safeZoom, handleFill);
            canvas.DrawRoundRect(rect, 1.5f / safeZoom, 1.5f / safeZoom, handleStroke);
        }
    }

    private void DrawAnnotationSelectionBounds(SKCanvas canvas, PageAnnotation annotation)
    {
        if (annotation.Points.Count == 0)
            return;

        SKRect bounds = PointsBounds(annotation.Points);
        bounds.Inflate(ScreenToPdfDistance(6f), ScreenToPdfDistance(6f));
        using var stroke = new SKPaint
        {
            Color = SKColors.DodgerBlue,
            StrokeWidth = ScreenToPdfDistance(1.3f),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            PathEffect = SKPathEffect.CreateDash([ScreenToPdfDistance(5f), ScreenToPdfDistance(4f)], 0),
        };
        canvas.DrawRect(bounds, stroke);
    }

    private void DrawAnnotationSelectionHandles(SKCanvas canvas, PageAnnotation annotation)
    {
        if (annotation.Points.Count == 0)
            return;

        float radius = ScreenToPdfDistance(5f);
        using var fill = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var activeFill = new SKPaint
        {
            Color = TempColor,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var stroke = new SKPaint
        {
            Color = SKColors.DodgerBlue,
            StrokeWidth = ScreenToPdfDistance(1.5f),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };

        for (int i = 0; i < annotation.Points.Count; i++)
        {
            SKPoint point = annotation.Points[i];
            var rect = SKRect.Create(point.X - radius, point.Y - radius, radius * 2, radius * 2);
            canvas.DrawRect(rect, i == _selectedAnnotationVertexIndex ? activeFill : fill);
            canvas.DrawRect(rect, stroke);
        }
    }

    private string AnnotationLabel(PageAnnotation annotation)
    {
        if (!string.IsNullOrWhiteSpace(annotation.Text))
            return annotation.Text;

        if (annotation.Points.Count < 2)
            return "";

        SKPoint start = annotation.Points[0];
        SKPoint end = annotation.Points[1];
        float lengthPt = MeasurementGeometry.Distance(start, end);
        double scale = annotation.ScaleMetersPerPt > 0
            ? annotation.ScaleMetersPerPt
            : ScaleMetersPerPt;
        return AnnotationGlyphRenderer.FormatLength(lengthPt, (float)scale, UnitMode);
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

    private void DrawJoistLayout(SKCanvas canvas, Measurement m, SKColor color, bool drawLabels)
    {
        if (!m.JoistEnabled)
            return;

        JoistLayoutResult layout = JoistTakeoffCalculator.Calculate(m, ScaleMetersPerPt);
        if (!layout.HasScale || layout.Count == 0)
            return;

        using var joistStroke = new SKPaint
        {
            Color = color.WithAlpha(220),
            StrokeWidth = ScreenToPdfDistance(1.15f * MeasurementStrokeScaleFactor()),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
        };
        foreach (JoistSegment segment in layout.Segments)
        {
            canvas.DrawLine(segment.Start, segment.End, joistStroke);
        }

        if (drawLabels)
            DrawJoistLayoutLabels(canvas, m);
    }

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

    private void DrawJoistLayoutLabels(SKCanvas canvas, Measurement measurement)
    {
        if (!ShouldDrawJoistLabels() || !measurement.JoistEnabled || !measurement.JoistShowLabels)
            return;

        JoistLayoutResult layout = JoistTakeoffCalculator.Calculate(measurement, ScaleMetersPerPt);
        if (!layout.HasScale || layout.Count == 0 || layout.Count > 180)
            return;

        foreach (JoistSegment segment in layout.Segments)
        {
            string label = JoistTakeoffCalculator.FormatSegmentLength(segment, UnitMode);
            SKPoint mid = new(
                (segment.Start.X + segment.End.X) / 2f,
                (segment.Start.Y + segment.End.Y) / 2f);
            DrawScreenTextBox(
                canvas,
                mid,
                [label],
                SKColors.Black.WithAlpha(220),
                SKColors.White.WithAlpha(190),
                SKColors.Transparent,
                JoistSegmentLabelFontScreenPx,
                2f,
                centered: true);
        }
    }

    private void DrawSelectionBounds(SKCanvas canvas, Measurement m)
    {
        if (m.Points.Count == 0) return;

        SKRect bounds = RawMeasurementBounds(m);
        bounds.Inflate(ScreenToPdfDistance(6f), ScreenToPdfDistance(6f));
        using var stroke = new SKPaint
        {
            Color = SKColors.DodgerBlue,
            StrokeWidth = ScreenToPdfDistance(1.5f),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            PathEffect = SKPathEffect.CreateDash([ScreenToPdfDistance(6f), ScreenToPdfDistance(4f)], 0),
        };
        canvas.DrawRect(bounds, stroke);
    }

    private void DrawSelectionHandles(SKCanvas canvas, Measurement m)
    {
        if (m.Points.Count == 0) return;

        float radius = ScreenToPdfDistance(5f);
        using var fill = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var activeFill = new SKPaint
        {
            Color = TempColor,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var stroke = new SKPaint
        {
            Color = SKColors.DodgerBlue,
            StrokeWidth = ScreenToPdfDistance(1.5f),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };

        foreach (MeasurementVertexRef vertex in MeasurementVertices(m))
        {
            var rect = SKRect.Create(vertex.Point.X - radius, vertex.Point.Y - radius, radius * 2, radius * 2);
            bool vertexSelected = ReferenceEquals(_selectedMeasurement, m) &&
                                  vertex.GlobalIndex == _selectedVertexIndex ||
                                  IsMeasurementVertexSelected(m, vertex.GlobalIndex);
            canvas.DrawRect(rect, vertexSelected ? activeFill : fill);
            canvas.DrawRect(rect, stroke);
        }
    }

    private bool ShouldDrawMeasurementHandles(Measurement measurement) =>
        ReferenceEquals(measurement, _selectedMeasurement) ||
        IsMeasurementSelected(measurement) && CanEditMeasurementVertices(measurement);

    private void DrawLabel(SKCanvas canvas, SKPoint pos, string text, string hexColor)
    {
        if (string.IsNullOrEmpty(text)) return;

        string[] lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
            return;

        DrawScreenTextBox(
            canvas,
            pos,
            lines,
            SKColors.White,
            SKColors.Black.WithAlpha(180),
            GetCachedColor(hexColor, SKColors.DodgerBlue),
            MeasurementLabelFontScreenPx,
            MeasurementLabelPaddingScreenPx,
            centered: false);
    }

    private void DrawScreenTextBox(
        SKCanvas canvas,
        SKPoint pdfPos,
        IReadOnlyList<string> lines,
        SKColor textColor,
        SKColor backgroundColor,
        SKColor borderColor,
        float fontSize,
        float pad,
        bool centered)
    {
        if (lines.Count == 0)
            return;

        string[] cleanLines = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim())
            .ToArray();
        if (cleanLines.Length == 0)
            return;

        float safeZoom = Math.Max(_zoom, 0.001f);
        float labelScale = ClampOverlayUserScale(MeasurementLabelScale);
        // When ScaleMeasurementLabelsWithPage is on, labels live in PDF space (relative to fit zoom)
        // so they grow/shrink with page zoom. Otherwise dividing by _zoom keeps screen size constant.
        float labelDivisor = ScaleMeasurementLabelsWithPage
            ? Math.Max(CurrentFitZoom(), 0.001f)
            : safeZoom;
        using var textPaint = new SKPaint
        {
            Color       = textColor,
            TextSize    = fontSize * labelScale / labelDivisor,
            IsAntialias = true,
            Typeface    = LabelTypeface,
        };

        float width = 0;
        foreach (string line in cleanLines)
            width = Math.Max(width, textPaint.MeasureText(line));
        float lineHeight = textPaint.TextSize * 1.22f;
        float textHeight = lineHeight * cleanLines.Length;
        float pdfPad = pad * labelScale / labelDivisor;
        SKRect bg = centered
            ? new SKRect(
                pdfPos.X - width / 2f - pdfPad,
                pdfPos.Y - textHeight / 2f - pdfPad,
                pdfPos.X + width / 2f + pdfPad,
                pdfPos.Y + textHeight / 2f + pdfPad)
            : new SKRect(
                pdfPos.X + pdfPad,
                pdfPos.Y - textHeight - pdfPad,
                pdfPos.X + width + pdfPad * 3,
                pdfPos.Y + pdfPad);

        using var bgPaint = new SKPaint
        {
            Color = backgroundColor,
            Style = SKPaintStyle.Fill,
        };
        using var borderPaint = new SKPaint
        {
            Color       = borderColor,
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = 1f / labelDivisor,
        };
        float radius = 3f / labelDivisor;
        canvas.DrawRoundRect(bg, radius, radius, bgPaint);
        if (borderColor.Alpha > 0)
            canvas.DrawRoundRect(bg, radius, radius, borderPaint);
        float baseline = bg.Top + pdfPad - textPaint.FontMetrics.Ascent;
        foreach (string line in cleanLines)
        {
            float textX = centered ? bg.Left + pdfPad : pdfPos.X + pdfPad * 1.5f;
            canvas.DrawText(line, textX, baseline, textPaint);
            baseline += lineHeight;
        }
    }

    private void DrawAiActionDraftPreview(SKCanvas canvas, SKRect visiblePdf)
    {
        if (_aiActionDraftPreview == null || _aiActionDraftPreview.Actions.Count == 0)
            return;

        using var stroke = new SKPaint
        {
            Color = new SKColor(0x00, 0xBC, 0xD4, 230),
            StrokeWidth = ScreenToPdfDistance(2.5f),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            PathEffect = SKPathEffect.CreateDash([ScreenToPdfDistance(10f), ScreenToPdfDistance(5f)], 0),
        };
        using var fill = new SKPaint
        {
            Color = new SKColor(0x00, 0xBC, 0xD4, 42),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var dotFill = new SKPaint
        {
            Color = new SKColor(0x00, 0xBC, 0xD4, 235),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var dotStroke = new SKPaint
        {
            Color = SKColors.White,
            StrokeWidth = ScreenToPdfDistance(1.5f),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };

        foreach (SmartAiAction action in _aiActionDraftPreview.Actions)
        {
            if (!ActionMatchesPreviewPage(action))
                continue;

            var points = new List<SKPoint>();
            foreach (SmartAiActionPoint point in action.Points)
                points.Add(new SKPoint(point.X, point.Y));

            if (points.Count == 0 || !PointsVisible(points, visiblePdf))
                continue;

            bool isArea = action.MeasurementType.Equals("area", StringComparison.OrdinalIgnoreCase) ||
                          action.Type.Contains("area", StringComparison.OrdinalIgnoreCase);
            bool isPoint = action.MeasurementType.Equals("point", StringComparison.OrdinalIgnoreCase) ||
                           action.Type.Contains("point", StringComparison.OrdinalIgnoreCase);

            if (isArea && points.Count >= 3)
            {
                using var path = new SKPath();
                path.MoveTo(points[0]);
                for (int i = 1; i < points.Count; i++)
                    path.LineTo(points[i]);
                path.Close();
                canvas.DrawPath(path, fill);
                canvas.DrawPath(path, stroke);
                DrawAiActionDots(canvas, points, dotFill, dotStroke);
                DrawLabel(canvas, Centroid(points), AiActionLabel(action), "#00BCD4");
            }
            else if (!isPoint && points.Count >= 2)
            {
                using var path = new SKPath();
                path.MoveTo(points[0]);
                for (int i = 1; i < points.Count; i++)
                    path.LineTo(points[i]);
                canvas.DrawPath(path, stroke);
                DrawAiActionDots(canvas, points, dotFill, dotStroke);
                DrawLabel(canvas, points[^1], AiActionLabel(action), "#00BCD4");
            }
            else
            {
                DrawAiActionDots(canvas, points, dotFill, dotStroke);
                DrawLabel(canvas, points[0], AiActionLabel(action), "#00BCD4");
            }
        }
    }

    private void DrawNoteAnnotation(SKCanvas canvas, PageAnnotation annotation, SKColor color, bool selected)
    {
        IReadOnlyList<SKPoint> corners = AnnotationRectangleCorners(annotation.Points);
        if (corners.Count < 4)
            return;

        using var fill = new SKPaint
        {
            Color = new SKColor(0xFF, 0xF8, 0xC6, 235),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var stroke = new SKPaint
        {
            Color = color,
            StrokeWidth = ScreenToPdfDistance(selected ? 2.4f : 1.5f),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };
        using SKPath path = BuildClosedAnnotationPath(corners);
        canvas.DrawPath(path, fill);
        canvas.DrawPath(path, stroke);

        DrawNoteText(canvas, annotation, corners);
    }

    private void DrawNoteText(SKCanvas canvas, PageAnnotation annotation, IReadOnlyList<SKPoint> corners)
    {
        if (!TryGetAnnotationLocalFrame(corners, out SKMatrix localToPdf, out float width, out float height))
            return;

        float pad = ScreenToPdfDistance(7f);
        SKRect textRect = new(pad, pad, width - pad, height - pad);
        if (textRect.Width <= 1 || textRect.Height <= 1)
            return;

        using var saved = new SKAutoCanvasRestore(canvas, true);
        canvas.Concat(ref localToPdf);
        using var textPaint = new SKPaint
        {
            Color = SKColors.Black.WithAlpha(220),
            TextSize = ScreenToPdfDistance(11f),
            IsAntialias = true,
            Typeface = OverlayUiTypeface,
        };
        float lineHeight = textPaint.TextSize * 1.22f;
        int maxLines = Math.Max(1, (int)(textRect.Height / lineHeight));
        IReadOnlyList<string> lines = WrapNoteText(annotation.Text, textPaint, textRect.Width, maxLines);
        float baseline = textRect.Top - textPaint.FontMetrics.Ascent;
        foreach (string line in lines)
        {
            if (baseline + textPaint.FontMetrics.Descent > textRect.Bottom)
                break;

            canvas.DrawText(line, textRect.Left, baseline, textPaint);
            baseline += lineHeight;
        }
    }

    private static SKPath BuildClosedAnnotationPath(IReadOnlyList<SKPoint> points)
    {
        var path = new SKPath();
        if (points.Count == 0)
            return path;

        path.MoveTo(points[0]);
        for (int i = 1; i < points.Count; i++)
            path.LineTo(points[i]);
        path.Close();
        return path;
    }

    private static bool TryGetAnnotationLocalFrame(
        IReadOnlyList<SKPoint> corners,
        out SKMatrix localToPdf,
        out float width,
        out float height)
    {
        localToPdf = SKMatrix.CreateIdentity();
        width = 0;
        height = 0;
        if (corners.Count < 4)
            return false;

        SKPoint origin = corners[0];
        SKPoint xAxis = new(corners[1].X - origin.X, corners[1].Y - origin.Y);
        SKPoint yAxis = new(corners[3].X - origin.X, corners[3].Y - origin.Y);
        width = MeasurementGeometry.Distance(corners[0], corners[1]);
        height = MeasurementGeometry.Distance(corners[0], corners[3]);
        if (width <= ViewportConstants.ZeroLengthEpsilon ||
            height <= ViewportConstants.ZeroLengthEpsilon)
        {
            return false;
        }

        localToPdf = new SKMatrix
        {
            ScaleX = xAxis.X / width,
            SkewX = yAxis.X / height,
            TransX = origin.X,
            SkewY = xAxis.Y / width,
            ScaleY = yAxis.Y / height,
            TransY = origin.Y,
            Persp0 = 0,
            Persp1 = 0,
            Persp2 = 1,
        };
        return true;
    }

    private static IReadOnlyList<string> WrapNoteText(string text, SKPaint paint, float maxWidth, int maxLines)
    {
        text = string.IsNullOrWhiteSpace(text) ? "Note" : text.Trim();
        var result = new List<string>();
        foreach (string paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            string current = "";
            foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = string.IsNullOrWhiteSpace(current) ? word : $"{current} {word}";
                if (paint.MeasureText(candidate) <= maxWidth || string.IsNullOrWhiteSpace(current))
                {
                    current = candidate;
                    continue;
                }

                result.Add(current);
                if (result.Count >= maxLines)
                    return result;
                current = word;
            }

            if (!string.IsNullOrWhiteSpace(current))
                result.Add(current);
            if (result.Count >= maxLines)
                return result;
        }

        return result.Count == 0 ? ["Note"] : result;
    }

    private bool ActionMatchesPreviewPage(SmartAiAction action)
    {
        if (string.IsNullOrWhiteSpace(action.Page) || string.IsNullOrWhiteSpace(_aiActionDraftPreviewPage))
            return true;

        return string.Equals(action.Page, _aiActionDraftPreviewPage, StringComparison.OrdinalIgnoreCase);
    }

    private void DrawAiActionDots(SKCanvas canvas, IReadOnlyList<SKPoint> points, SKPaint fill, SKPaint stroke)
    {
        float radius = ScreenToPdfDistance(4.5f);
        foreach (SKPoint point in points)
        {
            canvas.DrawCircle(point, radius, fill);
            canvas.DrawCircle(point, radius, stroke);
        }
    }

    private static string AiActionLabel(SmartAiAction action)
    {
        string label = string.IsNullOrWhiteSpace(action.Label) ? action.Type : action.Label;
        if (action.Confidence > 0)
            label += $" ({action.Confidence:P0})";
        return $"AI: {label}";
    }

    private void DrawAiMarkers(SKCanvas canvas, SKRect visiblePdf)
    {
        if (_aiMarkers.Count == 0)
            return;

        using var fill = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var stroke = new SKPaint
        {
            Color = SKColors.White.WithAlpha(245),
            StrokeWidth = ScreenToPdfDistance(1.8f),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };
        using var accent = new SKPaint
        {
            StrokeWidth = ScreenToPdfDistance(2.2f),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };

        foreach (SmartAiMarker marker in _aiMarkers)
        {
            SKPoint point = new(marker.PdfPoint.X, marker.PdfPoint.Y);
            if (!PointVisible(point, visiblePdf))
                continue;

            SKColor color = AiMarkerColor(marker);
            fill.Color = color.WithAlpha(220);
            accent.Color = color.WithAlpha(245);

            float size = ScreenToPdfDistance(7f);
            using var markerPath = new SKPath();
            markerPath.MoveTo(point.X, point.Y - size);
            markerPath.LineTo(point.X + size, point.Y);
            markerPath.LineTo(point.X, point.Y + size);
            markerPath.LineTo(point.X - size, point.Y);
            markerPath.Close();

            canvas.DrawPath(markerPath, fill);
            canvas.DrawPath(markerPath, stroke);

            if (marker.SampleKind.Equals("negative", StringComparison.OrdinalIgnoreCase) ||
                marker.SampleKind.Equals("ignore", StringComparison.OrdinalIgnoreCase))
            {
                float cross = size * 0.72f;
                canvas.DrawLine(point.X - cross, point.Y - cross, point.X + cross, point.Y + cross, accent);
                canvas.DrawLine(point.X + cross, point.Y - cross, point.X - cross, point.Y + cross, accent);
            }
            else
            {
                canvas.DrawCircle(point, ScreenToPdfDistance(2.2f), stroke);
            }

            DrawLabel(
                canvas,
                new SKPoint(point.X + size * 1.6f, point.Y - size * 1.6f),
                AiMarkerLabel(marker),
                ColorHex(color));
        }
    }

    private bool PointVisible(SKPoint point, SKRect visiblePdf)
    {
        float margin = ScreenToPdfDistance(24f);
        return point.X >= visiblePdf.Left - margin &&
               point.X <= visiblePdf.Right + margin &&
               point.Y >= visiblePdf.Top - margin &&
               point.Y <= visiblePdf.Bottom + margin;
    }

    private static SKColor AiMarkerColor(SmartAiMarker marker)
    {
        if (marker.SampleKind.Equals("ignore", StringComparison.OrdinalIgnoreCase))
            return new SKColor(0xEF, 0x6C, 0x00);
        if (marker.SampleKind.Equals("negative", StringComparison.OrdinalIgnoreCase))
            return new SKColor(0xD3, 0x2F, 0x2F);

        string type = marker.Type ?? "";
        if (type.Contains("height", StringComparison.OrdinalIgnoreCase))
            return new SKColor(0x7B, 0x1F, 0xA2);
        if (type.Contains("window", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("door", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("opening", StringComparison.OrdinalIgnoreCase))
            return new SKColor(0x19, 0x76, 0xD2);
        if (type.Contains("roof", StringComparison.OrdinalIgnoreCase))
            return new SKColor(0x2E, 0x7D, 0x32);
        if (type.Contains("corner", StringComparison.OrdinalIgnoreCase))
            return new SKColor(0x00, 0x96, 0x88);

        return new SKColor(0x00, 0xBC, 0xD4);
    }

    private static string AiMarkerLabel(SmartAiMarker marker)
    {
        string label = marker.Type switch
        {
            "exterior_corner" => "ext corner",
            "interior_corner" => "int corner",
            "wall_height_sample" => "height",
            "window_sample" => "window",
            "door_sample" => "door",
            "opening_sample" => "opening",
            "roof_note" => "roof note",
            "roof_edge_sample" => "roof edge",
            "dimension_text_sample" => "dimension",
            "ignore_area" => "ignore",
            _ => string.IsNullOrWhiteSpace(marker.Type) ? "marker" : marker.Type.Replace('_', ' '),
        };

        if (marker.SampleKind.Equals("negative", StringComparison.OrdinalIgnoreCase))
            label = $"not {label}";
        else if (marker.SampleKind.Equals("ignore", StringComparison.OrdinalIgnoreCase))
            label = "ignore";

        return $"M: {label}";
    }

    private static string ColorHex(SKColor color) =>
        $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";

    private static bool PointsVisible(IReadOnlyList<SKPoint> points, SKRect visiblePdf)
    {
        SKRect bounds = PointsBounds(points);
        return bounds.Left <= visiblePdf.Right &&
               bounds.Right >= visiblePdf.Left &&
               bounds.Top <= visiblePdf.Bottom &&
               bounds.Bottom >= visiblePdf.Top;
    }

    private static SKRect PointsBounds(IReadOnlyList<SKPoint> points)
    {
        if (points.Count == 0)
            return SKRect.Empty;

        float left = points[0].X;
        float right = points[0].X;
        float top = points[0].Y;
        float bottom = points[0].Y;
        for (int i = 1; i < points.Count; i++)
        {
            SKPoint point = points[i];
            left = Math.Min(left, point.X);
            right = Math.Max(right, point.X);
            top = Math.Min(top, point.Y);
            bottom = Math.Max(bottom, point.Y);
        }

        return new SKRect(left, top, right, bottom);
    }

    // ── Draw in-progress line / area ──────────────────────────────────────────

    private void DrawInProgress(SKCanvas canvas)
    {
        DrawPdfLayerTraceOverlay(canvas);

        using var tempPaint = new SKPaint
        {
            Color       = TempColor,
            StrokeWidth = ScreenToPdfDistance(2f),
            IsAntialias = true,
            Style       = SKPaintStyle.Stroke,
        };
        using var dotPaint = new SKPaint
        {
            Color       = TempColor,
            IsAntialias = true,
            Style       = SKPaintStyle.Fill,
        };

        float r = ScreenToPdfDistance(4f * PointSizeScaleFactor());

        foreach (var p in _drawPts)
            canvas.DrawCircle(p, r, dotPaint);

        if (_drawPts.Count >= 2)
        {
            for (int i = 1; i < _drawPts.Count; i++)
                canvas.DrawLine(_drawPts[i - 1], _drawPts[i], tempPaint);
        }

        // Rubber-band
        if (_drawPts.Count > 0 && _rubberEnd.HasValue)
        {
            using var rubber = new SKPaint
            {
                Color       = TempColor,
                StrokeWidth = ScreenToPdfDistance(1f),
                IsAntialias = true,
                Style       = SKPaintStyle.Stroke,
                PathEffect  = SKPathEffect.CreateDash([ScreenToPdfDistance(4f), ScreenToPdfDistance(4f)], 0),
            };
            if (ShouldPreviewAsBox())
                canvas.DrawRect(NormalizeRect(_drawPts[0], _rubberEnd.Value), rubber);
            else
                canvas.DrawLine(_drawPts[^1], _rubberEnd.Value, rubber);
        }

        DrawLiveRecordLengthLabels(canvas);

        if (_joistDirectionMeasurement != null)
        {
            using var joistPaint = new SKPaint
            {
                Color = new SKColor(0x00, 0x96, 0x88),
                StrokeWidth = ScreenToPdfDistance(2.2f),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                PathEffect = SKPathEffect.CreateDash([ScreenToPdfDistance(7f), ScreenToPdfDistance(4f)], 0),
            };
            using var joistDot = new SKPaint
            {
                Color = new SKColor(0x00, 0x96, 0x88),
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };
            float jr = ScreenToPdfDistance(5f);
            foreach (SKPoint point in _joistDirectionPts)
                canvas.DrawCircle(point, jr, joistDot);
            if (_joistDirectionPts.Count == 1 && _joistDirectionRubberEnd.HasValue)
                canvas.DrawLine(_joistDirectionPts[0], _joistDirectionRubberEnd.Value, joistPaint);
        }

        // Scale line
        if (_scalePts.Count >= 1)
        {
            using var scPaint = new SKPaint
            {
                Color       = ScaleClr,
                StrokeWidth = ScreenToPdfDistance(2f),
                IsAntialias = true,
                Style       = SKPaintStyle.Stroke,
            };
            using var scDot = new SKPaint
            {
                Color       = ScaleClr,
                IsAntialias = true,
                Style       = SKPaintStyle.Fill,
            };
            float sr = ScreenToPdfDistance(6f);
            foreach (var p in _scalePts)
                canvas.DrawCircle(p, sr, scDot);
            if (_scalePts.Count == 2)
                canvas.DrawLine(_scalePts[0], _scalePts[1], scPaint);
        }

        if (_snapPreview.HasValue)
        {
            SKPoint point = _snapPreview.Value;
            bool isPdfSnap = _snapPreviewKind.StartsWith("pdf-", StringComparison.OrdinalIgnoreCase);
            SKColor snapColor = isPdfSnap ? new SKColor(0x00, 0x78, 0xD4) : new SKColor(0xE5, 0x39, 0x35);
            float half = ScreenToPdfDistance(SnapMarkerScreenPx);
            var rect = new SKRect(
                point.X - half,
                point.Y - half,
                point.X + half,
                point.Y + half);
            using var snapFill = new SKPaint
            {
                Color = snapColor.WithAlpha(80),
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };
            using var snapStroke = new SKPaint
            {
                Color = snapColor,
                StrokeWidth = ScreenToPdfDistance(2f),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
            };
            if (string.Equals(_snapPreviewKind, "intersection", StringComparison.OrdinalIgnoreCase))
            {
                canvas.DrawCircle(point, half, snapFill);
                canvas.DrawCircle(point, half, snapStroke);
                canvas.DrawLine(point.X - half, point.Y, point.X + half, point.Y, snapStroke);
                canvas.DrawLine(point.X, point.Y - half, point.X, point.Y + half, snapStroke);
            }
            else if (string.Equals(_snapPreviewKind, "midpoint", StringComparison.OrdinalIgnoreCase))
            {
                using var diamond = new SKPath();
                diamond.MoveTo(point.X, point.Y - half);
                diamond.LineTo(point.X + half, point.Y);
                diamond.LineTo(point.X, point.Y + half);
                diamond.LineTo(point.X - half, point.Y);
                diamond.Close();
                canvas.DrawPath(diamond, snapFill);
                canvas.DrawPath(diamond, snapStroke);
            }
            else
            {
                canvas.DrawRect(rect, snapFill);
                canvas.DrawRect(rect, snapStroke);
            }

            string labelKind = _snapPreviewKind.ToLowerInvariant() switch
            {
                "midpoint" => "mid",
                "intersection" => "int",
                "pdf-corner" => "pdf corner",
                "pdf-point" => "pdf point",
                "pdf-line" => "pdf line",
                "pdf-overlay-corner" => "overlay corner",
                "pdf-overlay-point" => "overlay point",
                "pdf-overlay-line" => "overlay line",
                _ => "end",
            };
            string label = $"{labelKind} {point.X:F0},{point.Y:F0}";
            float textSize = ScreenToPdfDistance(10f);
            using var snapText = new SKPaint
            {
                Color = snapColor,
                IsAntialias = true,
                TextSize = textSize,
            };
            canvas.DrawText(label, point.X + half * 1.6f, point.Y - half * 1.2f, snapText);
        }

        if (_boxSelecting)
        {
            float safeZoom = Math.Max(_zoom, 0.001f);
            SKRect rect = NormalizeRect(_boxSelectStartPdf, _boxSelectEndPdf);
            using var fill = new SKPaint
            {
                Color = new SKColor(0x1E, 0x88, 0xE5, 32),
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };
            using var stroke = new SKPaint
            {
                Color = new SKColor(0x1E, 0x88, 0xE5),
                StrokeWidth = 1.5f / safeZoom,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                PathEffect = SKPathEffect.CreateDash([8f / safeZoom, 4f / safeZoom], 0),
            };
            canvas.DrawRect(rect, fill);
            canvas.DrawRect(rect, stroke);
        }

        DrawAiCropNoteSelectionOverlay(canvas);
    }

    private void DrawLiveRecordLengthLabels(SKCanvas canvas)
    {
        if (_tool == ViewerTool.Ruler)
        {
            DrawLiveRulerLengthLabel(canvas);
            return;
        }

        if (_tool is not (ViewerTool.Line or ViewerTool.Area) ||
            _drawPts.Count == 0 ||
            ScaleMetersPerPt <= 0)
        {
            return;
        }

        IReadOnlyList<SKPoint> points = LiveRecordLengthPoints();
        if (points.Count < 2)
            return;

        double totalMeters = 0;
        for (int i = 1; i < points.Count; i++)
        {
            SKPoint start = points[i - 1];
            SKPoint end = points[i];
            float lengthPt = MeasurementGeometry.Distance(start, end);
            if (lengthPt <= ViewportConstants.ZeroLengthEpsilon)
                continue;

            totalMeters += lengthPt * ScaleMetersPerPt;
            if (PdfToScreenDistance(lengthPt) >= 34f)
                DrawLiveRecordText(canvas, SegmentLengthLabelPoint(start, end), FormatLiveFeet(lengthPt * ScaleMetersPerPt), centered: true);
        }

        if (totalMeters <= 0)
            return;

        SKPoint anchor = points[^1];
        float offset = ScreenToPdfDistance(10f);
        DrawLiveRecordText(canvas, new SKPoint(anchor.X + offset, anchor.Y - offset), $"total {FormatLiveFeet(totalMeters)}", centered: false);
    }

    private void DrawLiveRulerLengthLabel(SKCanvas canvas)
    {
        if (_drawPts.Count == 0 ||
            !_rubberEnd.HasValue ||
            ScaleMetersPerPt <= 0)
        {
            return;
        }

        SKPoint start = _drawPts[0];
        SKPoint end = _rubberEnd.Value;
        float lengthPt = MeasurementGeometry.Distance(start, end);
        if (lengthPt <= ViewportConstants.ZeroLengthEpsilon)
            return;

        DrawLiveRecordText(
            canvas,
            SegmentLengthLabelPoint(start, end),
            FormatLiveFeet(lengthPt * ScaleMetersPerPt),
            centered: true);
    }

    private IReadOnlyList<SKPoint> LiveRecordLengthPoints()
    {
        if (ShouldPreviewAsBox() && _rubberEnd.HasValue)
            return BoxMeasurementPoints(_drawPts[0], _rubberEnd.Value, closeForLine: true);

        var points = _drawPts.ToList();
        if (_rubberEnd.HasValue)
            points.Add(_rubberEnd.Value);
        return points;
    }

    private SKPoint SegmentLengthLabelPoint(SKPoint start, SKPoint end)
    {
        float midX = (start.X + end.X) / 2f;
        float midY = (start.Y + end.Y) / 2f;
        float dx = end.X - start.X;
        float dy = end.Y - start.Y;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length <= ViewportConstants.ZeroLengthEpsilon)
            return new SKPoint(midX, midY);

        float offset = ScreenToPdfDistance(8f);
        return new SKPoint(midX - dy / length * offset, midY + dx / length * offset);
    }

    private void DrawLiveRecordText(SKCanvas canvas, SKPoint pos, string text, bool centered)
    {
        float safeZoom = Math.Max(_zoom, 0.001f);
        using var textPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 76),
            TextSize = 9f / safeZoom,
            IsAntialias = true,
            Typeface = LabelTypeface,
        };

        float width = textPaint.MeasureText(text);
        float x = centered ? pos.X - width / 2f : pos.X;
        float y = centered
            ? pos.Y - (textPaint.FontMetrics.Ascent + textPaint.FontMetrics.Descent) / 2f
            : pos.Y;
        canvas.DrawText(text, x, y, textPaint);
    }

    private static string FormatLiveFeet(double meters)
    {
        double feet = meters / 0.3048;
        string format = feet >= 100 ? "0" : feet >= 10 ? "0.#" : "0.##";
        return $"{feet.ToString(format, CultureInfo.InvariantCulture)} ft";
    }

}
