using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private const int PdfSnapBoundaryMaxGridCells = 1_200_000;
    private const int PdfSnapBoundaryMaxContourPoints = 420;
    private const int PdfSnapBoundaryMaxComponentEdges = 12_000;
    private const float PdfSnapBoundaryMinSegmentLengthPt = 1.0f;

    private static bool TryBuildPdfSnapBoundaryContour(
        IReadOnlyList<PdfGeometrySnapSegment> segments,
        int selectedIndex,
        float bridgeTolerancePt,
        PdfSnapBoundaryMode mode,
        out List<SKPoint> contour,
        out int selectedSegmentIndex)
    {
        contour = [];
        selectedSegmentIndex = 0;

        if (!TryCollectPdfSnapBoundaryComponent(
                segments,
                selectedIndex,
                bridgeTolerancePt,
                mode,
                out List<PdfSnapBoundaryTraceSegment> component,
                out PdfGeometrySnapSegment selected))
        {
            return false;
        }

        bool hasWallCore = TryCreatePdfSnapWallCoreComponent(
            component,
            selected,
            bridgeTolerancePt,
            mode,
            out List<PdfSnapBoundaryTraceSegment> wallCoreComponent);
        IReadOnlyList<PdfSnapBoundaryTraceSegment> rasterComponent = SelectPdfSnapRasterBoundaryComponent(
            component,
            wallCoreComponent,
            hasWallCore,
            mode);

        if (TryBuildPdfSnapRasterBoundaryContour(
                rasterComponent,
                selected,
                bridgeTolerancePt,
                out contour,
                out selectedSegmentIndex))
        {
            return true;
        }

        if (hasWallCore || mode != PdfSnapBoundaryMode.Safe)
            return false;

        return TryBuildPdfSnapEnvelopeBoundaryContour(
                   component,
                   selected,
                   bridgeTolerancePt,
                   out contour,
                   out selectedSegmentIndex);
    }

    private static bool TryCollectPdfSnapBoundaryComponent(
        IReadOnlyList<PdfGeometrySnapSegment> segments,
        int selectedIndex,
        float bridgeTolerancePt,
        PdfSnapBoundaryMode mode,
        out List<PdfSnapBoundaryTraceSegment> component,
        out PdfGeometrySnapSegment selected)
    {
        component = [];
        selected = default!;
        if (selectedIndex < 0 || selectedIndex >= segments.Count)
            return false;

        selected = segments[selectedIndex];
        float nodeTolerance = Math.Clamp(Math.Max(PdfSnapEndpointTolerancePt, bridgeTolerancePt * 0.025f), 1f, 2.25f);
        var nodes = new List<SKPoint>();
        var adjacency = new List<List<int>>();
        var nodeGrid = new Dictionary<(int X, int Y), List<int>>();
        var graphEdges = new List<PdfSnapBoundaryGraphEdge>();
        var edgeKeys = new Dictionary<(int A, int B), int>();
        int selectedGraphEdge = -1;

        for (int i = 0; i < segments.Count; i++)
        {
            PdfGeometrySnapSegment segment = segments[i];
            if (MeasurementGeometry.Distance(segment.Start, segment.End) < PdfSnapBoundaryMinSegmentLengthPt)
                continue;

            int a = PdfSnapBoundaryNodeFor(segment.Start, nodeTolerance, nodes, adjacency, nodeGrid);
            int b = PdfSnapBoundaryNodeFor(segment.End, nodeTolerance, nodes, adjacency, nodeGrid);
            int edgeIndex = AddPdfSnapBoundaryGraphEdge(a, b, i, bridge: false, graphEdges, adjacency, edgeKeys);
            if (i == selectedIndex)
                selectedGraphEdge = edgeIndex;
        }

        if (selectedGraphEdge < 0)
            return false;

        AddPdfSnapBoundaryBridgeEdges(nodes, adjacency, graphEdges, edgeKeys, bridgeTolerancePt, mode);

        var componentEdges = ConnectedPdfSnapBoundaryEdges(selectedGraphEdge, graphEdges, adjacency);
        if (componentEdges.Count < 4 || componentEdges.Count > PdfSnapBoundaryMaxComponentEdges)
            return false;

        foreach (int edgeIndex in componentEdges)
        {
            PdfSnapBoundaryGraphEdge edge = graphEdges[edgeIndex];
            component.Add(edge.Bridge
                ? new PdfSnapBoundaryTraceSegment(nodes[edge.A], nodes[edge.B], -1, true)
                : new PdfSnapBoundaryTraceSegment(
                    segments[edge.SegmentIndex].Start,
                    segments[edge.SegmentIndex].End,
                    edge.SegmentIndex,
                    false));
        }

        return component.Count >= 4;
    }

    private static int PdfSnapBoundaryNodeFor(
        SKPoint point,
        float tolerance,
        List<SKPoint> nodes,
        List<List<int>> adjacency,
        Dictionary<(int X, int Y), List<int>> grid)
    {
        int gx = PdfSnapBoundaryGridCoordinate(point.X, tolerance);
        int gy = PdfSnapBoundaryGridCoordinate(point.Y, tolerance);
        float bestDistance = tolerance * tolerance;
        int best = -1;

        for (int y = gy - 1; y <= gy + 1; y++)
        {
            for (int x = gx - 1; x <= gx + 1; x++)
            {
                if (!grid.TryGetValue((x, y), out List<int>? bucket))
                    continue;

                foreach (int nodeIndex in bucket)
                {
                    float distance = DistanceSquared(point, nodes[nodeIndex]);
                    if (distance >= bestDistance)
                        continue;

                    bestDistance = distance;
                    best = nodeIndex;
                }
            }
        }

        if (best >= 0)
            return best;

        int created = nodes.Count;
        nodes.Add(point);
        adjacency.Add([]);
        if (!grid.TryGetValue((gx, gy), out List<int>? cell))
        {
            cell = [];
            grid[(gx, gy)] = cell;
        }

        cell.Add(created);
        return created;
    }

    private static int AddPdfSnapBoundaryGraphEdge(
        int a,
        int b,
        int segmentIndex,
        bool bridge,
        List<PdfSnapBoundaryGraphEdge> graphEdges,
        List<List<int>> adjacency,
        Dictionary<(int A, int B), int> edgeKeys)
    {
        if (a == b)
            return -1;

        var key = a < b ? (a, b) : (b, a);
        if (edgeKeys.TryGetValue(key, out int existing))
            return existing;

        int edgeIndex = graphEdges.Count;
        graphEdges.Add(new PdfSnapBoundaryGraphEdge(a, b, segmentIndex, bridge));
        edgeKeys[key] = edgeIndex;
        adjacency[a].Add(edgeIndex);
        adjacency[b].Add(edgeIndex);
        return edgeIndex;
    }

    private static void AddPdfSnapBoundaryBridgeEdges(
        List<SKPoint> nodes,
        List<List<int>> adjacency,
        List<PdfSnapBoundaryGraphEdge> graphEdges,
        Dictionary<(int A, int B), int> edgeKeys,
        float bridgeTolerancePt,
        PdfSnapBoundaryMode mode)
    {
        float maxBridge = PdfSnapBoundaryGraphBridgeTolerance(bridgeTolerancePt, mode);
        if (maxBridge <= PdfSnapEndpointTolerancePt)
            return;

        var endpoints = Enumerable
            .Range(0, adjacency.Count)
            .Where(index => adjacency[index].Count == 1)
            .ToList();
        if (endpoints.Count < 2)
            return;

        var grid = new Dictionary<(int X, int Y), List<int>>();
        foreach (int nodeIndex in endpoints)
        {
            SKPoint point = nodes[nodeIndex];
            var key = (
                PdfSnapBoundaryGridCoordinate(point.X, maxBridge),
                PdfSnapBoundaryGridCoordinate(point.Y, maxBridge));
            if (!grid.TryGetValue(key, out List<int>? bucket))
            {
                bucket = [];
                grid[key] = bucket;
            }

            bucket.Add(nodeIndex);
        }

        var claimed = new HashSet<int>();
        foreach (int nodeIndex in endpoints)
        {
            if (claimed.Contains(nodeIndex))
                continue;

            if (!TryFindPdfSnapBoundaryBridgeNode(
                    nodeIndex,
                    nodes,
                    adjacency,
                    graphEdges,
                    grid,
                    maxBridge,
                    bridgeTolerancePt,
                    mode,
                    claimed,
                    out int bridgeNode))
            {
                continue;
            }

            AddPdfSnapBoundaryGraphEdge(
                nodeIndex,
                bridgeNode,
                -1,
                bridge: true,
                graphEdges,
                adjacency,
                edgeKeys);
            claimed.Add(nodeIndex);
            claimed.Add(bridgeNode);
        }
    }

    private static bool TryFindPdfSnapBoundaryBridgeNode(
        int nodeIndex,
        List<SKPoint> nodes,
        List<List<int>> adjacency,
        List<PdfSnapBoundaryGraphEdge> graphEdges,
        Dictionary<(int X, int Y), List<int>> grid,
        float maxBridge,
        float bridgeTolerancePt,
        PdfSnapBoundaryMode mode,
        HashSet<int> claimed,
        out int bridgeNode)
    {
        bridgeNode = -1;
        if (!TryPdfSnapBoundaryEndpointDirection(nodeIndex, nodes, adjacency, graphEdges, out float dirX, out float dirY, out float incidentLength))
            return false;

        SKPoint point = nodes[nodeIndex];
        int gx = PdfSnapBoundaryGridCoordinate(point.X, maxBridge);
        int gy = PdfSnapBoundaryGridCoordinate(point.Y, maxBridge);
        float minAlignment = PdfSnapBoundaryBridgeMinAlignment(mode);
        float lateralTolerance = PdfSnapBoundaryBridgeLateralTolerance(bridgeTolerancePt, mode);
        float bestScore = float.PositiveInfinity;
        float nextScore = float.PositiveInfinity;
        int bestNode = -1;

        for (int y = gy - 1; y <= gy + 1; y++)
        {
            for (int x = gx - 1; x <= gx + 1; x++)
            {
                if (!grid.TryGetValue((x, y), out List<int>? bucket))
                    continue;

                foreach (int candidate in bucket)
                    Consider(candidate);
            }
        }

        bridgeNode = bestNode;
        if (bridgeNode < 0)
            return false;

        float ambiguity = Math.Clamp(Math.Max(bridgeTolerancePt * 0.10f, maxBridge * 0.04f), 1.5f, 10f);
        return nextScore - bestScore > ambiguity;

        void Consider(int candidate)
        {
            if (candidate == nodeIndex || claimed.Contains(candidate) || adjacency[candidate].Count != 1)
                return;

            SKPoint other = nodes[candidate];
            float dx = other.X - point.X;
            float dy = other.Y - point.Y;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance <= PdfSnapEndpointTolerancePt || distance > maxBridge)
                return;

            float gapX = dx / distance;
            float gapY = dy / distance;
            float entryAlignment = dirX * gapX + dirY * gapY;
            if (entryAlignment < minAlignment)
                return;

            float lateral = Math.Abs((dx * dirY) - (dy * dirX));
            if (lateral > lateralTolerance)
                return;

            if (!TryPdfSnapBoundaryEndpointDirection(candidate, nodes, adjacency, graphEdges, out float otherDirX, out float otherDirY, out float otherIncidentLength))
                return;

            float exitAlignment = otherDirX * -gapX + otherDirY * -gapY;
            if (exitAlignment < minAlignment)
                return;

            if (!PdfSnapBoundaryBridgeHasSegmentSupport(distance, incidentLength, otherIncidentLength, bridgeTolerancePt, mode))
                return;

            float alignmentPenalty = (1f - Math.Min(entryAlignment, exitAlignment)) * maxBridge * 2.5f;
            float score = distance + (lateral * 3f) + alignmentPenalty;
            if (score < bestScore)
            {
                nextScore = bestScore;
                bestScore = score;
                bestNode = candidate;
                return;
            }

            if (score < nextScore)
                nextScore = score;
        }
    }

    private static float PdfSnapBoundaryGraphBridgeTolerance(float bridgeTolerancePt, PdfSnapBoundaryMode mode)
    {
        float factor = mode switch
        {
            PdfSnapBoundaryMode.Safe => 1.05f,
            PdfSnapBoundaryMode.All => 1.25f,
            PdfSnapBoundaryMode.Everything => 1.45f,
            _ => 1.05f,
        };
        float cap = mode switch
        {
            PdfSnapBoundaryMode.Safe => 72f,
            PdfSnapBoundaryMode.All => 96f,
            PdfSnapBoundaryMode.Everything => 128f,
            _ => 72f,
        };
        return Math.Clamp(bridgeTolerancePt * factor, PdfSnapBridgeToleranceMinPt, Math.Min(cap, PdfSnapBridgeToleranceMaxPt));
    }

    private static float PdfSnapBoundaryBridgeMinAlignment(PdfSnapBoundaryMode mode) =>
        mode == PdfSnapBoundaryMode.Everything ? 0.90f : 0.93f;

    private static float PdfSnapBoundaryBridgeLateralTolerance(float bridgeTolerancePt, PdfSnapBoundaryMode mode)
    {
        float factor = mode == PdfSnapBoundaryMode.Everything ? 0.24f : 0.18f;
        float cap = mode == PdfSnapBoundaryMode.Everything ? 18f : 14f;
        return Math.Clamp(bridgeTolerancePt * factor, 2.5f, cap);
    }

    private static bool PdfSnapBoundaryBridgeHasSegmentSupport(
        float distance,
        float firstIncidentLength,
        float secondIncidentLength,
        float bridgeTolerancePt,
        PdfSnapBoundaryMode mode)
    {
        float shortBridge = Math.Max(bridgeTolerancePt * (mode == PdfSnapBoundaryMode.Everything ? 0.95f : 0.80f), 24f);
        if (distance <= shortBridge)
            return true;

        float requiredIncident = Math.Clamp(distance * 0.35f, 10f, 48f);
        return firstIncidentLength >= requiredIncident && secondIncidentLength >= requiredIncident;
    }

    private static bool TryPdfSnapBoundaryEndpointDirection(
        int nodeIndex,
        List<SKPoint> nodes,
        List<List<int>> adjacency,
        List<PdfSnapBoundaryGraphEdge> graphEdges,
        out float dirX,
        out float dirY,
        out float length)
    {
        dirX = 0;
        dirY = 0;
        length = 0;
        if (adjacency[nodeIndex].Count != 1)
            return false;

        PdfSnapBoundaryGraphEdge edge = graphEdges[adjacency[nodeIndex][0]];
        int otherIndex = edge.A == nodeIndex ? edge.B : edge.A;
        SKPoint point = nodes[nodeIndex];
        SKPoint other = nodes[otherIndex];
        float dx = point.X - other.X;
        float dy = point.Y - other.Y;
        length = MathF.Sqrt(dx * dx + dy * dy);
        if (length <= ViewportConstants.GeometryEpsilon)
            return false;

        dirX = dx / length;
        dirY = dy / length;
        return true;
    }

    private static HashSet<int> ConnectedPdfSnapBoundaryEdges(
        int selectedGraphEdge,
        List<PdfSnapBoundaryGraphEdge> graphEdges,
        List<List<int>> adjacency)
    {
        var componentEdges = new HashSet<int>();
        var visitedNodes = new HashSet<int>();
        var queue = new Queue<int>();
        PdfSnapBoundaryGraphEdge selected = graphEdges[selectedGraphEdge];
        queue.Enqueue(selected.A);
        queue.Enqueue(selected.B);

        while (queue.Count > 0)
        {
            int node = queue.Dequeue();
            if (!visitedNodes.Add(node))
                continue;

            foreach (int edgeIndex in adjacency[node])
            {
                componentEdges.Add(edgeIndex);
                PdfSnapBoundaryGraphEdge edge = graphEdges[edgeIndex];
                int next = edge.A == node ? edge.B : edge.A;
                if (!visitedNodes.Contains(next))
                    queue.Enqueue(next);
            }
        }

        return componentEdges;
    }

    private static bool TryBuildPdfSnapRasterBoundaryContour(
        IReadOnlyList<PdfSnapBoundaryTraceSegment> component,
        PdfGeometrySnapSegment selected,
        float bridgeTolerancePt,
        out List<SKPoint> contour,
        out int selectedSegmentIndex)
    {
        contour = [];
        selectedSegmentIndex = 0;
        SKRect bounds = PdfSnapBoundaryBounds(component);
        if (bounds.Width <= 0 || bounds.Height <= 0) return false;

        float inflate = Math.Max(bridgeTolerancePt * 2f, 12f);
        bounds.Inflate(inflate, inflate);
        float cell = PdfSnapBoundaryCellSize(bounds, bridgeTolerancePt, out int width, out int height);
        if (width < 4 || height < 4) return false;

        bool[] blocked = new bool[width * height];
        int radiusCells = Math.Clamp(
            (int)MathF.Ceiling(Math.Max(bridgeTolerancePt * 0.55f, cell) / cell),
            1,
            32);
        RasterizePdfSnapBoundarySegments(component, bounds, cell, width, height, radiusCells, blocked);

        bool[] outside = FloodPdfSnapBoundaryOutside(width, height, blocked);
        List<PdfSnapBoundaryGridEdge> edges = BuildPdfSnapBoundaryGridEdges(width, height, outside);
        if (edges.Count == 0) return false;

        List<SKPoint> loop = TraceBestPdfSnapBoundaryGridLoop(edges, bounds, cell);
        if (loop.Count < 3) return false;

        float cleanTolerance = Math.Clamp(bridgeTolerancePt * 0.24f, 1.5f, 14f);
        List<SKPoint> rawContour = CleanPdfSnapBoundaryContour(loop, cleanTolerance);
        if (!PdfSnapBoundaryLoopStaysNearSelected(rawContour, selected, bridgeTolerancePt, cell))
            return false;

        List<SKPoint> projectedContour = CleanPdfSnapBoundaryContour(
            ProjectPdfSnapBoundaryPoints(loop, component, bridgeTolerancePt, cell),
            cleanTolerance);
        if (!PdfSnapBoundaryContourHasUsableArea(projectedContour, component, out _))
        {
            projectedContour = CleanPdfSnapBoundaryContour(
                ProjectPdfSnapBoundaryPoints(rawContour, component, bridgeTolerancePt, cell),
                cleanTolerance);
        }

        if (!TryChooseLargestPdfSnapBoundaryContour(
                [projectedContour, rawContour],
                component,
                out contour))
        {
            return false;
        }

        selectedSegmentIndex = FindNearestPdfSnapContourSegmentIndex(contour, selected);
        return true;
    }

    private static float PdfSnapBoundaryCellSize(SKRect bounds, float bridgeTolerancePt, out int width, out int height)
    {
        float cell = Math.Clamp(bridgeTolerancePt * 0.35f, 2f, 10f);
        width = Math.Max(1, (int)MathF.Ceiling(bounds.Width / cell) + 2);
        height = Math.Max(1, (int)MathF.Ceiling(bounds.Height / cell) + 2);

        while ((long)width * height > PdfSnapBoundaryMaxGridCells && cell < 32f)
        {
            cell *= 1.35f;
            width = Math.Max(1, (int)MathF.Ceiling(bounds.Width / cell) + 2);
            height = Math.Max(1, (int)MathF.Ceiling(bounds.Height / cell) + 2);
        }

        return cell;
    }

    private static void RasterizePdfSnapBoundarySegments(
        IReadOnlyList<PdfSnapBoundaryTraceSegment> component,
        SKRect bounds,
        float cell,
        int width,
        int height,
        int radiusCells,
        bool[] blocked)
    {
        foreach (PdfSnapBoundaryTraceSegment segment in component)
        {
            float length = MeasurementGeometry.Distance(segment.Start, segment.End);
            if (length <= ViewportConstants.GeometryEpsilon)
                continue;

            int steps = Math.Max(1, (int)MathF.Ceiling(length / Math.Max(0.75f, cell * 0.5f)));
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                var point = new SKPoint(
                    segment.Start.X + (segment.End.X - segment.Start.X) * t,
                    segment.Start.Y + (segment.End.Y - segment.Start.Y) * t);
                int x = (int)MathF.Round((point.X - bounds.Left) / cell);
                int y = (int)MathF.Round((point.Y - bounds.Top) / cell);
                MarkPdfSnapBoundaryDisk(x, y, radiusCells, width, height, blocked);
            }
        }
    }

    private static void MarkPdfSnapBoundaryDisk(
        int centerX,
        int centerY,
        int radius,
        int width,
        int height,
        bool[] blocked)
    {
        int radiusSq = radius * radius;
        for (int y = centerY - radius; y <= centerY + radius; y++)
        {
            if (y < 0 || y >= height)
                continue;

            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                if (x < 0 || x >= width)
                    continue;

                int dx = x - centerX;
                int dy = y - centerY;
                if (dx * dx + dy * dy <= radiusSq)
                    blocked[(y * width) + x] = true;
            }
        }
    }

    private static bool[] FloodPdfSnapBoundaryOutside(int width, int height, bool[] blocked)
    {
        var outside = new bool[blocked.Length];
        var queue = new Queue<(int X, int Y)>();

        void Enqueue(int x, int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
                return;

            int index = (y * width) + x;
            if (blocked[index] || outside[index])
                return;

            outside[index] = true;
            queue.Enqueue((x, y));
        }

        for (int x = 0; x < width; x++)
        {
            Enqueue(x, 0);
            Enqueue(x, height - 1);
        }

        for (int y = 0; y < height; y++)
        {
            Enqueue(0, y);
            Enqueue(width - 1, y);
        }

        while (queue.Count > 0)
        {
            (int x, int y) = queue.Dequeue();
            Enqueue(x + 1, y);
            Enqueue(x - 1, y);
            Enqueue(x, y + 1);
            Enqueue(x, y - 1);
        }

        return outside;
    }

    private static List<PdfSnapBoundaryGridEdge> BuildPdfSnapBoundaryGridEdges(
        int width,
        int height,
        bool[] outside)
    {
        var edges = new List<PdfSnapBoundaryGridEdge>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (IsOutsideCell(x, y))
                    continue;

                if (IsOutsideCell(x, y - 1))
                    edges.Add(new PdfSnapBoundaryGridEdge(x, y, x + 1, y));
                if (IsOutsideCell(x + 1, y))
                    edges.Add(new PdfSnapBoundaryGridEdge(x + 1, y, x + 1, y + 1));
                if (IsOutsideCell(x, y + 1))
                    edges.Add(new PdfSnapBoundaryGridEdge(x + 1, y + 1, x, y + 1));
                if (IsOutsideCell(x - 1, y))
                    edges.Add(new PdfSnapBoundaryGridEdge(x, y + 1, x, y));
            }
        }

        return edges;

        bool IsOutsideCell(int x, int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
                return true;

            return outside[(y * width) + x];
        }
    }

    private static List<SKPoint> TraceBestPdfSnapBoundaryGridLoop(
        IReadOnlyList<PdfSnapBoundaryGridEdge> edges,
        SKRect bounds,
        float cell)
    {
        var outgoing = new Dictionary<(int X, int Y), List<int>>();
        for (int i = 0; i < edges.Count; i++)
        {
            PdfSnapBoundaryGridEdge edge = edges[i];
            var key = (edge.X0, edge.Y0);
            if (!outgoing.TryGetValue(key, out List<int>? bucket))
            {
                bucket = [];
                outgoing[key] = bucket;
            }

            bucket.Add(i);
        }

        var used = new bool[edges.Count];
        List<SKPoint> best = [];
        double bestArea = 0;

        for (int i = 0; i < edges.Count; i++)
        {
            if (used[i])
                continue;

            List<SKPoint> loop = TracePdfSnapBoundaryGridLoop(i, edges, outgoing, used, bounds, cell);
            double area = Math.Abs(PdfSnapBoundarySignedArea(loop));
            if (loop.Count >= 3 && area > bestArea)
            {
                bestArea = area;
                best = loop;
            }
        }

        return best;
    }

    private static List<SKPoint> TracePdfSnapBoundaryGridLoop(
        int startEdge,
        IReadOnlyList<PdfSnapBoundaryGridEdge> edges,
        Dictionary<(int X, int Y), List<int>> outgoing,
        bool[] used,
        SKRect bounds,
        float cell)
    {
        var vertices = new List<(int X, int Y)>();
        PdfSnapBoundaryGridEdge first = edges[startEdge];
        int currentEdge = startEdge;
        var start = (first.X0, first.Y0);
        var current = start;

        for (int guard = 0; guard <= edges.Count; guard++)
        {
            if (currentEdge < 0 || used[currentEdge])
                return [];

            PdfSnapBoundaryGridEdge edge = edges[currentEdge];
            used[currentEdge] = true;
            vertices.Add((edge.X0, edge.Y0));
            current = (edge.X1, edge.Y1);
            if (current == start)
                break;

            if (!outgoing.TryGetValue(current, out List<int>? candidates))
                return [];

            currentEdge = candidates.FirstOrDefault(index => !used[index], -1);
            if (currentEdge < 0)
                return [];
        }

        if (current != start || vertices.Count < 3)
            return [];

        return vertices
            .Select(vertex => new SKPoint(bounds.Left + (vertex.X * cell), bounds.Top + (vertex.Y * cell)))
            .ToList();
    }

    private static bool TryBuildPdfSnapEnvelopeBoundaryContour(
        IReadOnlyList<PdfSnapBoundaryTraceSegment> component,
        PdfGeometrySnapSegment selected,
        float bridgeTolerancePt,
        out List<SKPoint> contour,
        out int selectedSegmentIndex)
    {
        contour = [];
        selectedSegmentIndex = 0;
        var points = new List<SKPoint>();
        foreach (PdfSnapBoundaryTraceSegment segment in component)
        {
            points.Add(segment.Start);
            points.Add(segment.End);
        }

        List<SKPoint> hull = PdfSnapBoundaryConvexHull(points);
        contour = CleanPdfSnapBoundaryContour(hull, Math.Clamp(bridgeTolerancePt * 0.35f, 2f, 20f));
        if (!PdfSnapBoundaryLoopStaysNearSelected(contour, selected, bridgeTolerancePt, 0f) ||
            !PdfSnapBoundaryContourLooksUsable(contour, component, out _))
            return false;

        selectedSegmentIndex = FindNearestPdfSnapContourSegmentIndex(contour, selected);
        return true;
    }

    private static List<SKPoint> PdfSnapBoundaryConvexHull(List<SKPoint> points)
    {
        var sorted = points
            .DistinctBy(point => (MathF.Round(point.X, 2), MathF.Round(point.Y, 2)))
            .OrderBy(point => point.X)
            .ThenBy(point => point.Y)
            .ToList();
        if (sorted.Count <= 3)
            return sorted;

        var lower = new List<SKPoint>();
        foreach (SKPoint point in sorted)
        {
            while (lower.Count >= 2 && PdfSnapBoundaryCross(lower[^2], lower[^1], point) <= 0)
                lower.RemoveAt(lower.Count - 1);
            lower.Add(point);
        }

        var upper = new List<SKPoint>();
        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            SKPoint point = sorted[i];
            while (upper.Count >= 2 && PdfSnapBoundaryCross(upper[^2], upper[^1], point) <= 0)
                upper.RemoveAt(upper.Count - 1);
            upper.Add(point);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static List<SKPoint> ProjectPdfSnapBoundaryPoints(
        IReadOnlyList<SKPoint> points,
        IReadOnlyList<PdfSnapBoundaryTraceSegment> component,
        float bridgeTolerancePt,
        float cell)
    {
        float search = Math.Max(bridgeTolerancePt * 1.15f, cell * 4f);
        float searchSq = search * search;
        var projected = new List<SKPoint>(points.Count);

        foreach (SKPoint point in points)
        {
            SKPoint bestPoint = point;
            float best = searchSq;
            foreach (PdfSnapBoundaryTraceSegment segment in component)
            {
                SKPoint candidate = ClosestPdfSnapBoundaryPoint(point, segment.Start, segment.End);
                float distance = DistanceSquared(point, candidate);
                if (distance >= best)
                    continue;

                best = distance;
                bestPoint = candidate;
            }

            projected.Add(bestPoint);
        }

        return projected;
    }

    private static List<SKPoint> CleanPdfSnapBoundaryContour(IReadOnlyList<SKPoint> source, float tolerance)
    {
        List<SKPoint> points = RemoveClosePdfSnapBoundaryPoints(source, Math.Max(0.75f, tolerance * 0.5f));
        if (points.Count < 3)
            return points;

        points = RemoveCollinearPdfSnapBoundaryPoints(points, tolerance);
        points = RemoveDuplicatePdfSnapBoundaryPoints(points, Math.Max(1.0f, tolerance * 0.75f));
        float simplifyTolerance = tolerance;
        while (points.Count > PdfSnapBoundaryMaxContourPoints && simplifyTolerance < 48f)
        {
            simplifyTolerance *= 1.45f;
            points = SimplifyClosedPdfSnapBoundaryRdp(points, simplifyTolerance);
            points = RemoveCollinearPdfSnapBoundaryPoints(points, simplifyTolerance);
            points = RemoveDuplicatePdfSnapBoundaryPoints(points, Math.Max(1.0f, simplifyTolerance * 0.75f));
        }

        return points;
    }

    private static List<SKPoint> RemoveClosePdfSnapBoundaryPoints(IReadOnlyList<SKPoint> source, float tolerance)
    {
        var result = new List<SKPoint>();
        float toleranceSq = tolerance * tolerance;
        foreach (SKPoint point in source)
        {
            if (result.Count > 0 && DistanceSquared(result[^1], point) <= toleranceSq)
                continue;
            result.Add(ClonePoint(point));
        }

        if (result.Count > 1 && DistanceSquared(result[0], result[^1]) <= toleranceSq)
            result.RemoveAt(result.Count - 1);
        return result;
    }

    private static List<SKPoint> RemoveCollinearPdfSnapBoundaryPoints(IReadOnlyList<SKPoint> source, float tolerance)
    {
        var points = source.ToList();
        if (points.Count < 4)
            return points;

        bool changed;
        do
        {
            changed = false;
            var next = new List<SKPoint>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                SKPoint previous = points[(i - 1 + points.Count) % points.Count];
                SKPoint current = points[i];
                SKPoint after = points[(i + 1) % points.Count];
                if (points.Count - next.Count > 3 &&
                    DistanceToSegment(current, previous, after) <= tolerance &&
                    PdfSnapBoundaryDot(previous, current, after) >= 0)
                {
                    changed = true;
                    continue;
                }

                next.Add(current);
            }

            points = next;
        }
        while (changed && points.Count >= 4);

        return points;
    }

    private static List<SKPoint> RemoveDuplicatePdfSnapBoundaryPoints(IReadOnlyList<SKPoint> source, float tolerance)
    {
        if (source.Count < 4)
            return source.ToList();

        var result = new List<SKPoint>(source.Count);
        float toleranceSq = tolerance * tolerance;
        foreach (SKPoint point in source)
        {
            if (result.Any(existing => DistanceSquared(existing, point) <= toleranceSq))
                continue;

            result.Add(point);
        }

        return result.Count >= 3 ? result : source.ToList();
    }

    private static List<SKPoint> SimplifyClosedPdfSnapBoundaryRdp(IReadOnlyList<SKPoint> source, float tolerance)
    {
        if (source.Count <= 3)
            return source.ToList();

        var open = source.ToList();
        open.Add(source[0]);
        bool[] keep = new bool[open.Count];
        keep[0] = true;
        keep[^1] = true;
        SimplifyPdfSnapBoundaryRdp(open, 0, open.Count - 1, tolerance, keep);

        var result = new List<SKPoint>();
        for (int i = 0; i < open.Count - 1; i++)
            if (keep[i])
                result.Add(open[i]);
        return result.Count >= 3 ? result : source.ToList();
    }

    private static void SimplifyPdfSnapBoundaryRdp(
        IReadOnlyList<SKPoint> points,
        int start,
        int end,
        float tolerance,
        bool[] keep)
    {
        if (end <= start + 1)
            return;

        float best = 0;
        int bestIndex = -1;
        for (int i = start + 1; i < end; i++)
        {
            float distance = DistanceToSegment(points[i], points[start], points[end]);
            if (distance <= best)
                continue;

            best = distance;
            bestIndex = i;
        }

        if (bestIndex < 0 || best <= tolerance)
            return;

        keep[bestIndex] = true;
        SimplifyPdfSnapBoundaryRdp(points, start, bestIndex, tolerance, keep);
        SimplifyPdfSnapBoundaryRdp(points, bestIndex, end, tolerance, keep);
    }

    private static bool TryChooseLargestPdfSnapBoundaryContour(
        IEnumerable<IReadOnlyList<SKPoint>> candidates,
        IReadOnlyList<PdfSnapBoundaryTraceSegment> component,
        out List<SKPoint> selected)
    {
        selected = [];
        bool projectedCandidate = true;
        foreach (IReadOnlyList<SKPoint> candidate in candidates)
        {
            bool usable = projectedCandidate
                ? PdfSnapBoundaryContourHasUsableArea(candidate, component, out _)
                : PdfSnapBoundaryContourLooksUsable(candidate, component, out _);
            projectedCandidate = false;
            if (!usable)
                continue;

            selected = candidate.Select(ClonePoint).ToList();
            return true;
        }

        return false;
    }

    private static bool PdfSnapBoundaryContourLooksUsable(
        IReadOnlyList<SKPoint> contour,
        IReadOnlyList<PdfSnapBoundaryTraceSegment> component,
        out double area)
    {
        area = 0;
        if (contour.Count < 3)
            return false;
        if (HasDuplicatePdfSnapBoundaryPoints(contour, 1.0f))
            return false;

        SKRect bounds = PdfSnapBoundaryBounds(component);
        area = Math.Abs(PdfSnapBoundarySignedArea(contour));
        double minArea = Math.Max(1_000.0, bounds.Width * bounds.Height * 0.015);
        return area >= minArea;
    }

    private static bool PdfSnapBoundaryContourHasUsableArea(
        IReadOnlyList<SKPoint> contour,
        IReadOnlyList<PdfSnapBoundaryTraceSegment> component,
        out double area)
    {
        area = 0;
        if (contour.Count < 3)
            return false;

        SKRect bounds = PdfSnapBoundaryBounds(component);
        area = Math.Abs(PdfSnapBoundarySignedArea(contour));
        double minArea = Math.Max(1_000.0, bounds.Width * bounds.Height * 0.015);
        return area >= minArea;
    }

    private static bool HasDuplicatePdfSnapBoundaryPoints(IReadOnlyList<SKPoint> contour, float tolerance)
    {
        float toleranceSq = tolerance * tolerance;
        for (int i = 0; i < contour.Count; i++)
        {
            for (int j = i + 1; j < contour.Count; j++)
            {
                if (Math.Abs(i - j) <= 1 || i == 0 && j == contour.Count - 1)
                    continue;
                if (DistanceSquared(contour[i], contour[j]) <= toleranceSq)
                    return true;
            }
        }

        return false;
    }

    private static int FindNearestPdfSnapContourSegmentIndex(
        IReadOnlyList<SKPoint> contour,
        PdfGeometrySnapSegment selected)
    {
        if (contour.Count < 2)
            return 0;

        var midpoint = new SKPoint(
            (selected.Start.X + selected.End.X) * 0.5f,
            (selected.Start.Y + selected.End.Y) * 0.5f);
        float best = float.PositiveInfinity;
        int bestIndex = 0;
        for (int i = 0; i < contour.Count; i++)
        {
            SKPoint start = contour[i];
            SKPoint end = contour[(i + 1) % contour.Count];
            float distance = DistanceToSegment(midpoint, start, end);
            if (distance >= best)
                continue;

            best = distance;
            bestIndex = i;
        }

        return bestIndex;
    }

    private static SKRect PdfSnapBoundaryBounds(IReadOnlyList<PdfSnapBoundaryTraceSegment> component)
    {
        float left = float.PositiveInfinity;
        float top = float.PositiveInfinity;
        float right = float.NegativeInfinity;
        float bottom = float.NegativeInfinity;
        foreach (PdfSnapBoundaryTraceSegment segment in component)
        {
            IncludePointInBounds(segment.Start, ref left, ref top, ref right, ref bottom);
            IncludePointInBounds(segment.End, ref left, ref top, ref right, ref bottom);
        }

        return float.IsInfinity(left) ? SKRect.Empty : new SKRect(left, top, right, bottom);
    }

    private static SKPoint ClosestPdfSnapBoundaryPoint(SKPoint point, SKPoint start, SKPoint end)
    {
        float vx = end.X - start.X;
        float vy = end.Y - start.Y;
        float lengthSq = (vx * vx) + (vy * vy);
        if (lengthSq <= ViewportConstants.GeometryEpsilon)
            return start;

        float t = ((point.X - start.X) * vx + (point.Y - start.Y) * vy) / lengthSq;
        t = Math.Clamp(t, 0f, 1f);
        return new SKPoint(start.X + (vx * t), start.Y + (vy * t));
    }

    private static int PdfSnapBoundaryGridCoordinate(float value, float cellSize) => (int)MathF.Floor(value / Math.Max(0.001f, cellSize));

    private static double PdfSnapBoundarySignedArea(IReadOnlyList<SKPoint> points)
    {
        if (points.Count < 3)
            return 0;

        double area = 0;
        for (int i = 0; i < points.Count; i++)
        {
            SKPoint a = points[i];
            SKPoint b = points[(i + 1) % points.Count];
            area += (a.X * b.Y) - (b.X * a.Y);
        }

        return area / 2.0;
    }

    private static float PdfSnapBoundaryCross(SKPoint a, SKPoint b, SKPoint c) => ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));

    private static float PdfSnapBoundaryDot(SKPoint previous, SKPoint current, SKPoint after) =>
        ((current.X - previous.X) * (after.X - current.X)) + ((current.Y - previous.Y) * (after.Y - current.Y));

    private readonly record struct PdfSnapBoundaryGraphEdge(int A, int B, int SegmentIndex, bool Bridge);
    private readonly record struct PdfSnapBoundaryTraceSegment(SKPoint Start, SKPoint End, int SegmentIndex, bool Bridge);
    private readonly record struct PdfSnapBoundaryGridEdge(int X0, int Y0, int X1, int Y1);
}
