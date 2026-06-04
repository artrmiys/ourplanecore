using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private EdgeSnapCandidate? _edgeSnapCandidate;
    private EdgeSnapPreview? _edgeSnapPreview;
    private int _edgeSnapCycleMode;

    private const int EdgeSnapModeVertices = 0;
    private const int EdgeSnapModeSingleEdge = 1;
    private const int EdgeSnapModeAdjacentEdges = 2;
    private const int EdgeSnapModeContour = 3;
    private const int EdgeSnapModePolylineAll = 4;
    private const int EdgeSnapModePolylineEverything = 5;
    private const int EdgeSnapModeCount = 6;
    private const float PdfSnapEndpointTolerancePt = 0.75f;
    private const float PdfSnapBridgeToleranceMinPt = 1.0f;
    private const float PdfSnapBridgeToleranceMaxPt = 240.0f;
    private const float PdfSnapDirectionalBridgeFactor = 3.0f;
    private const float PdfSnapDirectionalMinAlignment = 0.94f;
    private const float PdfSnapDirectionalLateralFactor = 0.35f;
    private const int PdfSnapMaxChainSegments = 256;

    private bool UpdateEdgeSnapPreview(SKPoint rawPdf, bool resetModeForNewCandidate = true)
    {
        if (!CanUseEdgeSnapPreview())
        {
            ClearEdgeSnapPreview();
            return false;
        }

        if (!TryFindEdgeSnapCandidate(rawPdf, out EdgeSnapCandidate? candidate) || candidate == null)
        {
            ClearEdgeSnapPreview();
            return false;
        }

        if (!IsSameEdgeSnapCandidate(_edgeSnapCandidate, candidate))
        {
            if (resetModeForNewCandidate)
                _edgeSnapCycleMode = EdgeSnapModeVertices;
        }

        _edgeSnapCandidate = candidate;
        if (_edgeSnapCycleMode == EdgeSnapModeVertices)
        {
            _edgeSnapPreview = null;
            RequestRepaint();
            return true;
        }

        _edgeSnapPreview = BuildEdgeSnapPreview(candidate, _edgeSnapCycleMode);
        SetSnapPreview(null);
        RequestRepaint();
        return true;
    }

    private bool TryCycleEdgeSnapPreview()
    {
        if (!_lastPointerPdf.HasValue || !CanUseEdgeSnapPreview())
            return false;

        _edgeSnapCycleMode = NextEdgeSnapCycleMode();
        bool found = UpdateEdgeSnapPreview(_lastPointerPdf.Value, resetModeForNewCandidate: false);
        if (found)
            PostStatus($"Edge Snap: {EdgeSnapModeTitle(_edgeSnapCycleMode)}. Tab cycles vertices / edge / edges / contour / polyline all / polyline everything.");
        return found;
    }

    private int NextEdgeSnapCycleMode()
    {
        if (_edgeSnapCycleMode == EdgeSnapModeVertices &&
            _tool == ViewerTool.Area &&
            _edgeSnapCandidate is { SourceKind: "pdf", Closed: true } &&
            _edgeSnapCandidate.Contour.Count >= 3)
        {
            return EdgeSnapModeContour;
        }

        return (_edgeSnapCycleMode + 1) % EdgeSnapModeCount;
    }

    private bool TryCommitEdgeSnapPreview(SKPoint rawPdf)
    {
        if (!UpdateEdgeSnapPreview(rawPdf) ||
            _edgeSnapCycleMode == EdgeSnapModeVertices ||
            _edgeSnapPreview == null)
        {
            return false;
        }

        List<SKPoint> points = _edgeSnapPreview.Points.Select(ClonePoint).ToList();
        if (_tool == ViewerTool.Line)
        {
            if (_edgeSnapPreview.ClosedContour && IsClosedEdgeSnapContourMode(_edgeSnapPreview.Mode) && points.Count >= 3)
                points.Add(points[0]);
            if (points.Count < 2)
                return false;

            _drawPts.Clear();
            _drawPts.AddRange(points);
            ClearEdgeSnapPreview();
            FinalizeDrawing();
            return true;
        }

        if (_tool == ViewerTool.Area)
        {
            if (points.Count < 2)
                return false;

            bool finalizeClosedContour =
                _edgeSnapPreview.ClosedContour &&
                IsClosedEdgeSnapContourMode(_edgeSnapPreview.Mode) &&
                points.Count >= 3;

            _drawPts.Clear();
            _drawPts.AddRange(points);
            ClearEdgeSnapPreview();
            if (finalizeClosedContour)
                FinalizeDrawing();
            else
            {
                _rubberEnd = points[^1];
                RequestRepaint();
                PostRecordPrompt();
            }
            return true;
        }

        return false;
    }

    private bool CanUseEdgeSnapPreview() =>
        SnapEnabled &&
        CanPrepareEdgeSnapPreviewForTab();

    private bool CanPrepareEdgeSnapPreviewForTab() =>
        _pageBitmap != null &&
        _drawPts.Count == 0 &&
        !BoxModeEnabled &&
        !_pdfLayerTraceEnabled &&
        !IsMissingScaleForLinearArea() &&
        _tool is ViewerTool.Line or ViewerTool.Area;

    private void ClearEdgeSnapPreview()
    {
        if (_edgeSnapCandidate == null && _edgeSnapPreview == null && _edgeSnapCycleMode == EdgeSnapModeVertices)
            return;

        _edgeSnapCandidate = null;
        _edgeSnapPreview = null;
        _edgeSnapCycleMode = EdgeSnapModeVertices;
        RequestRepaint();
    }

    private bool TryFindEdgeSnapCandidate(SKPoint rawPdf, out EdgeSnapCandidate? candidate)
    {
        candidate = null;
        float tolerance = ScreenToPdfDistance(SnapToleranceScreenPx);
        SKRect searchRect = SKRect.Create(
            rawPdf.X - tolerance,
            rawPdf.Y - tolerance,
            tolerance * 2f,
            tolerance * 2f);

        float best = tolerance;
        bool found = false;
        foreach (ViewportMeasurementSegmentCandidate segment in ActivePageMeasurementSegmentsNear(searchRect))
        {
            float distance = DistanceToSegment(rawPdf, segment.Start, segment.End);
            if (distance > best ||
                !TryResolveEdgeSnapSegment(segment.Measurement, segment.Start, segment.End, out var resolved) ||
                resolved == null)
            {
                continue;
            }

            best = distance;
            candidate = resolved with { Distance = distance };
            found = true;
        }

        if (!found &&
            TryFindPdfEdgeSnapCandidate(rawPdf, best, out EdgeSnapCandidate? pdfCandidate) &&
            pdfCandidate != null)
        {
            candidate = pdfCandidate;
            found = true;
        }

        return found;
    }

    private bool TryResolveEdgeSnapSegment(
        Measurement measurement,
        SKPoint start,
        SKPoint end,
        out EdgeSnapCandidate? candidate)
    {
        candidate = null;
        if (measurement.MType is not "line" and not "area")
            return false;

        if (TryResolveEdgeSnapSegment(measurement, measurement.Points, measurement.MType == "area", start, end, out candidate))
            return true;

        if (measurement.MType != "area")
            return false;

        foreach (List<SKPoint> hole in measurement.Holes)
            if (TryResolveEdgeSnapSegment(measurement, hole, closed: true, start, end, out candidate))
                return true;

        return false;
    }

    private bool TryResolveEdgeSnapSegment(
        Measurement measurement,
        IReadOnlyList<SKPoint> contour,
        bool closed,
        SKPoint start,
        SKPoint end,
        out EdgeSnapCandidate? candidate)
    {
        candidate = null;
        int segmentCount = closed ? contour.Count : Math.Max(0, contour.Count - 1);
        if (segmentCount <= 0)
            return false;

        for (int i = 0; i < segmentCount; i++)
        {
            SKPoint a = contour[i];
            SKPoint b = contour[(i + 1) % contour.Count];
            if (!SamePointPair(a, b, start, end))
                continue;

            candidate = new EdgeSnapCandidate(
                "takeoff",
                measurement,
                contour.ToList(),
                closed,
                i,
                0);
            return true;
        }

        return false;
    }

    private EdgeSnapPreview BuildEdgeSnapPreview(EdgeSnapCandidate candidate, int mode)
    {
        List<SKPoint> points = mode switch
        {
            EdgeSnapModeAdjacentEdges => BuildAdjacentEdgeSnapPoints(candidate),
            EdgeSnapModeContour or EdgeSnapModePolylineAll or EdgeSnapModePolylineEverything => candidate.Contour.Select(ClonePoint).ToList(),
            _ => BuildSingleEdgeSnapPoints(candidate),
        };

        string label = mode switch
        {
            EdgeSnapModeAdjacentEdges => "edges",
            EdgeSnapModeContour => candidate.Closed ? "contour" : "polyline",
            EdgeSnapModePolylineAll => "polyline all",
            EdgeSnapModePolylineEverything => "polyline everything",
            _ => "edge",
        };
        if (candidate.SourceKind == "pdf")
            label = "pdf " + label;
        return new EdgeSnapPreview(points, mode, candidate.Closed, label);
    }

    private bool TryFindPdfEdgeSnapCandidate(SKPoint rawPdf, float tolerance, out EdgeSnapCandidate? candidate)
    {
        candidate = null;
        if (!PdfSnapEnabled)
            return false;

        if (!IsPdfSnapCacheCurrent())
        {
            QueuePdfSnapPointLoad(force: false);
            return false;
        }

        bool preferClosedBoundary = _tool == ViewerTool.Area || IsClosedEdgeSnapContourMode(_edgeSnapCycleMode);
        PdfSnapBoundaryMode boundaryMode = BoundaryModeForEdgeSnapMode(_edgeSnapCycleMode);
        float bridgeTolerancePt = PdfSnapBridgeTolerancePt();
        float searchTolerance = preferClosedBoundary
            ? Math.Max(tolerance, bridgeTolerancePt)
            : tolerance;
        IReadOnlyList<PdfGeometrySnapSegmentHit> hits = _pdfSnapIndex.FindSegments(rawPdf, searchTolerance);
        if (hits.Count == 0)
            return false;

        int candidateLimit = preferClosedBoundary ? 32 : 12;
        foreach (PdfGeometrySnapSegmentHit hit in RankPdfEdgeSnapSegmentHits(hits, preferClosedBoundary, bridgeTolerancePt).Take(candidateLimit))
        {
            candidate = BuildPdfEdgeSnapCandidate(hit.Segment, hit.DistancePt, hit.Index, preferClosedBoundary, boundaryMode, bridgeTolerancePt);
            if (candidate != null)
                return true;
        }

        return false;
    }

    private EdgeSnapCandidate? BuildPdfEdgeSnapCandidate(
        PdfGeometrySnapSegment selectedSegment,
        float distance,
        int selectedIndex,
        bool preferClosedBoundary,
        PdfSnapBoundaryMode boundaryMode,
        float bridgeTolerancePt)
    {
        IReadOnlyList<PdfGeometrySnapSegment> segments = _pdfSnapIndex.Segments;
        if (selectedIndex < 0)
        {
            return new EdgeSnapCandidate(
                "pdf",
                null,
                [ClonePoint(selectedSegment.Start), ClonePoint(selectedSegment.End)],
                false,
                0,
                distance);
        }

        List<SKPoint> contour = BuildPdfSnapContourCore(
            segments,
            selectedIndex,
            bridgeTolerancePt,
            preferClosedBoundary,
            boundaryMode,
            out bool closed,
            out int segmentIndex);
        if (contour.Count < 2)
            return null;

        return new EdgeSnapCandidate(
            "pdf",
            null,
            contour,
            closed,
            segmentIndex,
            distance);
    }

    private static IEnumerable<PdfGeometrySnapSegmentHit> RankPdfEdgeSnapSegmentHits(
        IReadOnlyList<PdfGeometrySnapSegmentHit> hits,
        bool preferClosedBoundary,
        float bridgeTolerancePt)
    {
        if (!preferClosedBoundary)
            return hits.OrderBy(hit => hit.DistancePt);

        float usefulLength = Math.Clamp(bridgeTolerancePt * 0.22f, 10f, 36f);
        return hits.OrderBy(hit => PdfEdgeSnapSegmentHitScore(hit, usefulLength));
    }

    private static float PdfEdgeSnapSegmentHitScore(PdfGeometrySnapSegmentHit hit, float usefulLength)
    {
        float shortPenalty = hit.LengthPt >= usefulLength ? 0 : (usefulLength - hit.LengthPt) * 2.5f;
        float axisPenalty = PdfEdgeSnapSegmentIsAxisAligned(hit.Segment) ? 0 : usefulLength;
        return hit.DistancePt + shortPenalty + axisPenalty;
    }

    private static bool PdfEdgeSnapSegmentIsAxisAligned(PdfGeometrySnapSegment segment)
    {
        float dx = Math.Abs(segment.End.X - segment.Start.X);
        float dy = Math.Abs(segment.End.Y - segment.Start.Y);
        return dx <= 2f || dy <= 2f;
    }

    private static int FindPdfSnapSegmentIndex(
        IReadOnlyList<PdfGeometrySnapSegment> segments,
        PdfGeometrySnapSegment selected)
    {
        for (int i = 0; i < segments.Count; i++)
        {
            PdfGeometrySnapSegment segment = segments[i];
            if (SamePointPair(segment.Start, segment.End, selected.Start, selected.End))
                return i;
        }

        return -1;
    }

    private float PdfSnapBridgeTolerancePt() =>
        Math.Clamp(
            ScreenToPdfDistance((float)PdfSnapBridgeToleranceScreenPx),
            PdfSnapBridgeToleranceMinPt,
            PdfSnapBridgeToleranceMaxPt);

    private static List<SKPoint> BuildPdfSnapContour(
        IReadOnlyList<PdfGeometrySnapSegment> segments,
        int selectedIndex,
        float bridgeTolerancePt,
        bool preferClosedBoundary,
        out bool closed,
        out int selectedSegmentIndex)
    {
        return BuildPdfSnapContourCore(
            segments,
            selectedIndex,
            bridgeTolerancePt,
            preferClosedBoundary,
            PdfSnapBoundaryMode.Safe,
            out closed,
            out selectedSegmentIndex);
    }

    private static List<SKPoint> BuildPdfSnapContourCore(
        IReadOnlyList<PdfGeometrySnapSegment> segments,
        int selectedIndex,
        float bridgeTolerancePt,
        bool preferClosedBoundary,
        PdfSnapBoundaryMode boundaryMode,
        out bool closed,
        out int selectedSegmentIndex)
    {
        closed = false;
        selectedSegmentIndex = 0;
        if (selectedIndex < 0 || selectedIndex >= segments.Count)
            return [];

        bool selectedLooksLikeDoorSymbol = preferClosedBoundary &&
            PdfSnapGeometrySegmentLooksLikeInteriorDoorCandidate(segments, selectedIndex, bridgeTolerancePt);
        if (preferClosedBoundary &&
            TryBuildPdfSnapBoundaryContour(
                segments,
                selectedIndex,
                bridgeTolerancePt,
                boundaryMode,
                out List<SKPoint> boundary,
                out int boundarySegmentIndex))
        {
            if (!selectedLooksLikeDoorSymbol ||
                PdfSnapDoorSelectedBoundaryLooksLikeExterior(boundary, segments, selectedIndex, bridgeTolerancePt))
            {
                closed = true;
                selectedSegmentIndex = boundarySegmentIndex;
                return boundary;
            }
        }

        if (selectedLooksLikeDoorSymbol)
        {
            return [];
        }

        PdfGeometrySnapSegment selected = segments[selectedIndex];
        var points = new List<SKPoint>
        {
            ClonePoint(selected.Start),
            ClonePoint(selected.End),
        };
        var used = new HashSet<int> { selectedIndex };
        float targetStrokeWidth = Math.Max(0f, selected.StrokeWidth);

        while (!closed &&
               used.Count < PdfSnapMaxChainSegments &&
               TryFindUniqueConnectedPdfSnapSegment(
                   segments,
                   used,
                   points[0],
                   points[1],
                   bridgeTolerancePt,
                   targetStrokeWidth,
                   out int nextIndex,
                   out SKPoint matchedPoint,
                   out SKPoint otherPoint))
        {
            used.Add(nextIndex);
            if (points.Count >= 3 && PdfSnapSameEndpoint(otherPoint, points[^1], bridgeTolerancePt))
            {
                closed = true;
                break;
            }

            if (!PdfSnapSameEndpoint(matchedPoint, points[0], PdfSnapEndpointTolerancePt))
            {
                points.Insert(0, ClonePoint(matchedPoint));
                selectedSegmentIndex++;
            }
            points.Insert(0, ClonePoint(otherPoint));
            selectedSegmentIndex++;
        }

        while (!closed &&
               used.Count < PdfSnapMaxChainSegments &&
               TryFindUniqueConnectedPdfSnapSegment(
                   segments,
                   used,
                   points[^1],
                   points[^2],
                   bridgeTolerancePt,
                   targetStrokeWidth,
                   out int nextIndex,
                   out SKPoint matchedPoint,
                   out SKPoint otherPoint))
        {
            used.Add(nextIndex);
            if (points.Count >= 3 && PdfSnapSameEndpoint(otherPoint, points[0], bridgeTolerancePt))
            {
                if (!PdfSnapSameEndpoint(matchedPoint, points[^1], PdfSnapEndpointTolerancePt))
                    points.Add(ClonePoint(matchedPoint));
                closed = true;
                break;
            }

            if (!PdfSnapSameEndpoint(matchedPoint, points[^1], PdfSnapEndpointTolerancePt))
                points.Add(ClonePoint(matchedPoint));
            points.Add(ClonePoint(otherPoint));
        }

        selectedSegmentIndex = Math.Clamp(selectedSegmentIndex, 0, Math.Max(0, points.Count - 2));
        return points;
    }

    private static bool TryFindUniqueConnectedPdfSnapSegment(
        IReadOnlyList<PdfGeometrySnapSegment> segments,
        HashSet<int> used,
        SKPoint endpoint,
        SKPoint previousPoint,
        float bridgeTolerancePt,
        float targetStrokeWidth,
        out int segmentIndex,
        out SKPoint matchedPoint,
        out SKPoint otherPoint)
    {
        segmentIndex = -1;
        matchedPoint = default;
        otherPoint = default;

        if (!TryPdfSnapDirection(previousPoint, endpoint, out float dirX, out float dirY))
            return false;

        float endpointTolerance = Math.Max(PdfSnapEndpointTolerancePt, bridgeTolerancePt);
        float endpointToleranceSq = endpointTolerance * endpointTolerance;
        float directionalTolerance = Math.Min(
            PdfSnapBridgeToleranceMaxPt,
            Math.Max(endpointTolerance, bridgeTolerancePt * PdfSnapDirectionalBridgeFactor));
        float lateralTolerance = Math.Max(
            PdfSnapEndpointTolerancePt * 2f,
            bridgeTolerancePt * PdfSnapDirectionalLateralFactor);
        var candidates = new List<PdfSnapConnectedSegmentCandidate>();

        for (int i = 0; i < segments.Count; i++)
        {
            if (used.Contains(i))
                continue;

            PdfGeometrySnapSegment segment = segments[i];
            float strokeWidth = Math.Max(0f, segment.StrokeWidth);
            ConsiderPdfSnapEndpoint(i, segment.Start, segment.End, strokeWidth);
            ConsiderPdfSnapEndpoint(i, segment.End, segment.Start, strokeWidth);
        }

        int matches = candidates.Count;
        if (matches == 0)
            return false;

        candidates.Sort((left, right) => left.Score.CompareTo(right.Score));
        if (matches > 1 && PdfSnapCandidatesAreAmbiguous(candidates[0], candidates[1], bridgeTolerancePt))
            return false;

        PdfSnapConnectedSegmentCandidate best = candidates[0];
        segmentIndex = best.SegmentIndex;
        matchedPoint = best.MatchedPoint;
        otherPoint = best.OtherPoint;
        return true;

        void ConsiderPdfSnapEndpoint(int index, SKPoint candidateEndpoint, SKPoint candidateOtherPoint, float strokeWidth)
        {
            float endpointDistanceSq = DistanceSquared(candidateEndpoint, endpoint);
            bool endpointMatch = endpointDistanceSq <= endpointToleranceSq;
            PdfSnapDirectionalOffset(endpoint, candidateEndpoint, dirX, dirY, out float along, out float lateral);
            float alignment = PdfSnapDirectionAlignment(candidateEndpoint, candidateOtherPoint, dirX, dirY);
            if (endpointMatch && alignment < -0.25f)
                return;

            if (!endpointMatch)
            {
                if (along <= 0 ||
                    along > directionalTolerance ||
                    lateral > lateralTolerance ||
                    alignment < PdfSnapDirectionalMinAlignment)
                {
                    return;
                }
            }

            float endpointDistance = MathF.Sqrt(endpointDistanceSq);
            float directionPenalty = (1f - Math.Max(0f, alignment)) * endpointTolerance * 0.35f;
            float strokePenalty = PdfSnapStrokeWidthPenalty(strokeWidth, targetStrokeWidth, endpointTolerance);
            float score = endpointMatch
                ? endpointDistance + lateral * 3f + directionPenalty
                : along + lateral * 4f + (1f - alignment) * directionalTolerance * 2f;
            score += strokePenalty;

            candidates.Add(new PdfSnapConnectedSegmentCandidate(
                index,
                candidateEndpoint,
                candidateOtherPoint,
                score,
                lateral,
                alignment));
        }
    }

    private static float PdfSnapStrokeWidthPenalty(float candidateStrokeWidth, float targetStrokeWidth, float endpointTolerance)
    {
        if (targetStrokeWidth <= 0.25f)
            return 0f;

        float deficit = Math.Max(0f, targetStrokeWidth - Math.Max(0f, candidateStrokeWidth));
        return deficit * Math.Max(2f, endpointTolerance * 0.25f);
    }

    private static bool PdfSnapCandidatesAreAmbiguous(
        PdfSnapConnectedSegmentCandidate best,
        PdfSnapConnectedSegmentCandidate next,
        float bridgeTolerancePt)
    {
        float scoreTie = Math.Max(0.5f, Math.Min(bridgeTolerancePt * 0.08f, 6f));
        if (Math.Abs(next.Score - best.Score) > scoreTie)
            return false;

        if (Math.Abs(next.Alignment - best.Alignment) > 0.08f)
            return false;

        float lateralTie = Math.Max(0.5f, Math.Min(bridgeTolerancePt * 0.05f, 4f));
        return Math.Abs(next.LateralDistance - best.LateralDistance) <= lateralTie;
    }

    private static bool TryPdfSnapDirection(
        SKPoint previousPoint,
        SKPoint endpoint,
        out float dirX,
        out float dirY)
    {
        float dx = endpoint.X - previousPoint.X;
        float dy = endpoint.Y - previousPoint.Y;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length <= ViewportConstants.GeometryEpsilon)
        {
            dirX = 0;
            dirY = 0;
            return false;
        }

        dirX = dx / length;
        dirY = dy / length;
        return true;
    }

    private static void PdfSnapDirectionalOffset(
        SKPoint origin,
        SKPoint point,
        float dirX,
        float dirY,
        out float along,
        out float lateral)
    {
        float dx = point.X - origin.X;
        float dy = point.Y - origin.Y;
        along = dx * dirX + dy * dirY;
        lateral = Math.Abs(dx * dirY - dy * dirX);
    }

    private static float PdfSnapDirectionAlignment(SKPoint start, SKPoint end, float dirX, float dirY)
    {
        float dx = end.X - start.X;
        float dy = end.Y - start.Y;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length <= ViewportConstants.GeometryEpsilon)
            return 0f;

        return Math.Clamp((dx * dirX + dy * dirY) / length, -1f, 1f);
    }

    private static List<SKPoint> BuildSingleEdgeSnapPoints(EdgeSnapCandidate candidate)
    {
        int n = candidate.Contour.Count;
        int i = Math.Clamp(candidate.SegmentIndex, 0, Math.Max(0, n - 1));
        return
        [
            ClonePoint(candidate.Contour[i]),
            ClonePoint(candidate.Contour[(i + 1) % n]),
        ];
    }

    private static List<SKPoint> BuildAdjacentEdgeSnapPoints(EdgeSnapCandidate candidate)
    {
        int n = candidate.Contour.Count;
        int i = Math.Clamp(candidate.SegmentIndex, 0, Math.Max(0, n - 1));
        if (candidate.Closed)
        {
            int previous = (i - 1 + n) % n;
            int next = (i + 1) % n;
            int afterNext = (i + 2) % n;
            return
            [
                ClonePoint(candidate.Contour[previous]),
                ClonePoint(candidate.Contour[i]),
                ClonePoint(candidate.Contour[next]),
                ClonePoint(candidate.Contour[afterNext]),
            ];
        }

        int from = Math.Max(0, i - 1);
        int to = Math.Min(n - 1, i + 2);
        var points = new List<SKPoint>();
        for (int index = from; index <= to; index++)
            points.Add(ClonePoint(candidate.Contour[index]));
        return points;
    }

    private void DrawEdgeSnapPreview(SKCanvas canvas)
    {
        if (_edgeSnapPreview == null || _edgeSnapPreview.Points.Count < 2)
            return;

        float stroke = ScreenToPdfDistance(3.2f);
        using var path = new SKPath();
        path.MoveTo(_edgeSnapPreview.Points[0]);
        for (int i = 1; i < _edgeSnapPreview.Points.Count; i++)
            path.LineTo(_edgeSnapPreview.Points[i]);
        if (_edgeSnapPreview.ClosedContour && IsClosedEdgeSnapContourMode(_edgeSnapPreview.Mode))
            path.Close();

        using var glow = new SKPaint
        {
            Color = new SKColor(0x00, 0x78, 0xD4, 72),
            StrokeWidth = stroke * 3f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };
        using var line = new SKPaint
        {
            Color = new SKColor(0x00, 0x78, 0xD4),
            StrokeWidth = stroke,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };
        using var dot = new SKPaint
        {
            Color = new SKColor(0x00, 0x78, 0xD4),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };

        canvas.DrawPath(path, glow);
        canvas.DrawPath(path, line);
        float radius = ScreenToPdfDistance(3.5f);
        foreach (SKPoint point in _edgeSnapPreview.Points)
            canvas.DrawCircle(point, radius, dot);

        DrawEdgeSnapLabel(canvas, _edgeSnapPreview);
    }

    private void DrawEdgeSnapLabel(SKCanvas canvas, EdgeSnapPreview preview)
    {
        SKPoint anchor = preview.Points[0];
        float textSize = ScreenToPdfDistance(10f);
        using var text = new SKPaint
        {
            Color = new SKColor(0x00, 0x78, 0xD4),
            IsAntialias = true,
            TextSize = textSize,
        };
        canvas.DrawText(preview.Label, anchor.X + ScreenToPdfDistance(8f), anchor.Y - ScreenToPdfDistance(8f), text);
    }

    private static SKPoint ClonePoint(SKPoint point) => new(point.X, point.Y);

    private static string EdgeSnapModeTitle(int mode) => mode switch
    {
        EdgeSnapModeSingleEdge => "edge",
        EdgeSnapModeAdjacentEdges => "edges",
        EdgeSnapModeContour => "contour",
        EdgeSnapModePolylineAll => "polyline all",
        EdgeSnapModePolylineEverything => "polyline everything",
        _ => "vertices",
    };

    private static bool IsClosedEdgeSnapContourMode(int mode) =>
        mode is EdgeSnapModeContour or EdgeSnapModePolylineAll or EdgeSnapModePolylineEverything;

    private static PdfSnapBoundaryMode BoundaryModeForEdgeSnapMode(int mode) => mode switch
    {
        EdgeSnapModePolylineAll => PdfSnapBoundaryMode.All,
        EdgeSnapModePolylineEverything => PdfSnapBoundaryMode.Everything,
        _ => PdfSnapBoundaryMode.Safe,
    };

    private static bool IsSameEdgeSnapCandidate(EdgeSnapCandidate? current, EdgeSnapCandidate next)
    {
        if (current == null ||
            !string.Equals(current.SourceKind, next.SourceKind, StringComparison.Ordinal) ||
            !string.Equals(current.Measurement?.Id ?? "", next.Measurement?.Id ?? "", StringComparison.Ordinal) ||
            current.SegmentIndex != next.SegmentIndex ||
            current.Closed != next.Closed)
        {
            return false;
        }

        if (!TryGetEdgeSnapSegment(current, out SKPoint currentStart, out SKPoint currentEnd) ||
            !TryGetEdgeSnapSegment(next, out SKPoint nextStart, out SKPoint nextEnd))
        {
            return false;
        }

        return SamePointPair(currentStart, currentEnd, nextStart, nextEnd);
    }

    private static bool TryGetEdgeSnapSegment(EdgeSnapCandidate candidate, out SKPoint start, out SKPoint end)
    {
        start = default;
        end = default;
        if (candidate.Contour.Count < 2)
            return false;

        int i = Math.Clamp(candidate.SegmentIndex, 0, Math.Max(0, candidate.Contour.Count - 1));
        int next = i + 1;
        if (next >= candidate.Contour.Count)
        {
            if (!candidate.Closed)
                return false;
            next = 0;
        }

        start = candidate.Contour[i];
        end = candidate.Contour[next];
        return true;
    }

    private static bool SamePointPair(SKPoint a, SKPoint b, SKPoint start, SKPoint end) =>
        EdgeSamePoint(a, start) && EdgeSamePoint(b, end) ||
        EdgeSamePoint(a, end) && EdgeSamePoint(b, start);

    private static bool EdgeSamePoint(SKPoint left, SKPoint right) =>
        DistanceSquared(left, right) <= ViewportConstants.GeometryEpsilon;

    private static bool PdfSnapSameEndpoint(SKPoint left, SKPoint right, float tolerancePt) =>
        DistanceSquared(left, right) <= tolerancePt * tolerancePt;

    private static bool PdfSnapSameEndpointSquared(SKPoint left, SKPoint right, double toleranceSq) =>
        DistanceSquared(left, right) <= toleranceSq;

    private sealed record EdgeSnapCandidate(
        string SourceKind,
        Measurement? Measurement,
        IReadOnlyList<SKPoint> Contour,
        bool Closed,
        int SegmentIndex,
        float Distance);

    private enum PdfSnapBoundaryMode
    {
        Safe,
        All,
        Everything,
    }

    private readonly record struct PdfSnapConnectedSegmentCandidate(
        int SegmentIndex,
        SKPoint MatchedPoint,
        SKPoint OtherPoint,
        float Score,
        float LateralDistance,
        float Alignment);

    private sealed record EdgeSnapPreview(
        IReadOnlyList<SKPoint> Points,
        int Mode,
        bool ClosedContour,
        string Label);
}
