using System;
using System.Collections.Generic;
using SkiaSharp;

namespace OurPlaneCore;

public static partial class PdfExporter
{
    private static SKRect PlaceJoistSegmentLabelBox(
        SKPoint start,
        SKPoint end,
        SKPoint anchor,
        SKSize labelSize,
        float collisionPad,
        IReadOnlyList<SKRect> occupiedBoxes)
    {
        SKRect baseBox = CenteredRect(anchor, labelSize);
        if (!OverlapsOccupied(ExpandRect(baseBox, collisionPad), occupiedBoxes))
            return baseBox;

        SKPoint direction = UnitVector(start, end);
        SKPoint normal = new(-direction.Y, direction.X);
        float normalStep = Math.Max(labelSize.Height + collisionPad * 2f, 1f);
        float alongStep = Math.Max(labelSize.Width + collisionPad * 2f, normalStep);
        float segmentLength = Distance(start, end);
        int maxAlongSteps = Math.Clamp((int)Math.Floor(Math.Max(0f, segmentLength - labelSize.Width) / alongStep / 2f), 0, 4);

        SKRect best = baseBox;
        float bestOverlap = OccupiedOverlapArea(ExpandRect(baseBox, collisionPad), occupiedBoxes);
        for (int normalIndex = 0; normalIndex <= 12; normalIndex++)
        {
            foreach (float normalOffset in NormalOffsetSteps(normalIndex, normalStep))
            {
                foreach (float alongOffset in OffsetSteps(maxAlongSteps, alongStep))
                {
                    if (normalIndex == 0 && Math.Abs(normalOffset) < 0.001f && Math.Abs(alongOffset) < 0.001f)
                        continue;

                    SKPoint delta = new(
                        direction.X * alongOffset + normal.X * normalOffset,
                        direction.Y * alongOffset + normal.Y * normalOffset);
                    SKRect candidate = OffsetRect(baseBox, delta.X, delta.Y);
                    SKRect collisionBox = ExpandRect(candidate, collisionPad);
                    if (!OverlapsOccupied(collisionBox, occupiedBoxes))
                        return candidate;

                    float overlap = OccupiedOverlapArea(collisionBox, occupiedBoxes);
                    if (overlap < bestOverlap)
                    {
                        best = candidate;
                        bestOverlap = overlap;
                    }
                }
            }
        }

        return best;
    }

    private static IEnumerable<float> NormalOffsetSteps(int stepIndex, float step)
    {
        if (stepIndex <= 0)
        {
            yield return 0f;
            yield break;
        }

        float offset = step * stepIndex;
        yield return offset;
        yield return -offset;
    }

    private static IEnumerable<float> OffsetSteps(int maxSteps, float step)
    {
        yield return 0f;
        for (int i = 1; i <= maxSteps; i++)
        {
            float offset = step * i;
            yield return offset;
            yield return -offset;
        }
    }

    private static SKRect CenteredRect(SKPoint center, SKSize size) =>
        new(
            center.X - size.Width / 2f,
            center.Y - size.Height / 2f,
            center.X + size.Width / 2f,
            center.Y + size.Height / 2f);

    private static SKRect OffsetRect(SKRect rect, float dx, float dy) =>
        new(rect.Left + dx, rect.Top + dy, rect.Right + dx, rect.Bottom + dy);

    private static SKRect ExpandRect(SKRect rect, float pad) =>
        new(rect.Left - pad, rect.Top - pad, rect.Right + pad, rect.Bottom + pad);

    private static bool OverlapsOccupied(SKRect box, IReadOnlyList<SKRect> occupiedBoxes)
    {
        foreach (SKRect occupied in occupiedBoxes)
        {
            if (RectanglesOverlap(box, occupied))
                return true;
        }

        return false;
    }

    private static float OccupiedOverlapArea(SKRect box, IReadOnlyList<SKRect> occupiedBoxes)
    {
        float area = 0f;
        foreach (SKRect occupied in occupiedBoxes)
        {
            float width = Math.Min(box.Right, occupied.Right) - Math.Max(box.Left, occupied.Left);
            float height = Math.Min(box.Bottom, occupied.Bottom) - Math.Max(box.Top, occupied.Top);
            if (width > 0f && height > 0f)
                area += width * height;
        }

        return area;
    }

    private static bool RectanglesOverlap(SKRect a, SKRect b) =>
        a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;

    private static SKPoint UnitVector(SKPoint start, SKPoint end)
    {
        float dx = end.X - start.X;
        float dy = end.Y - start.Y;
        float length = (float)Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0.001f)
            return new SKPoint(1f, 0f);

        return new SKPoint(dx / length, dy / length);
    }

    private static float Distance(SKPoint start, SKPoint end)
    {
        float dx = end.X - start.X;
        float dy = end.Y - start.Y;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }
}
