using System.Collections.Generic;
using SkiaSharp;

namespace OurPlanCore;

/// <summary>
/// Shared coordinate model for the A101 sample floor plan. Both the PDF drawing
/// (SampleJobGuideBuilder) and the preloaded takeoffs (SampleJobService) read these
/// numbers so the drawn walls/doors/windows and the takeoff geometry line up exactly.
/// All values are in PDF points on the 792 x 612 sample sheet.
/// </summary>
internal sealed class SamplePlanGeometry
{
    public static SamplePlanGeometry Instance { get; } = new();

    // Exterior wall outer face.
    public float OuterL => 160f;
    public float OuterT => 140f;
    public float OuterR => 632f;
    public float OuterB => 472f;
    public float Wall => 10f;   // exterior wall thickness
    public float Part => 7f;    // interior partition thickness

    // Interior faces of the exterior wall.
    public float InnerL => OuterL + Wall;
    public float InnerT => OuterT + Wall;
    public float InnerR => OuterR - Wall;
    public float InnerB => OuterB - Wall;

    // Partition centerlines.
    public float MidY => 300f;     // splits top rooms from bottom rooms
    public float TopVx1 => 360f;   // bedroom | bath
    public float TopVx2 => 500f;   // bath | kitchen
    public float BotVx => 400f;    // living | dining

    public sealed record Room(string Name, string Tag, float Cx, float Cy);

    public sealed record Opening(
        SKPoint Center,
        float Width,
        bool Horizontal,
        float WallNear,
        float WallFar,
        bool IsDoor,
        int Swing);

    public IReadOnlyList<Room> Rooms { get; }
    public IReadOnlyList<Opening> Openings { get; }

    // Door / window centers exposed for count takeoffs (kept in sync with Openings).
    public IReadOnlyList<SKPoint> DoorPoints { get; }
    public IReadOnlyList<SKPoint> WindowPoints { get; }

    private SamplePlanGeometry()
    {
        Rooms =
        [
            new("BEDROOM", "150 SF", (InnerL + TopVx1) / 2, (InnerT + MidY) / 2),
            new("BATH", "70 SF", (TopVx1 + TopVx2) / 2, (InnerT + MidY) / 2),
            new("KITCHEN", "120 SF", (TopVx2 + InnerR) / 2, (InnerT + MidY) / 2),
            new("LIVING", "240 SF", (InnerL + BotVx) / 2, (MidY + InnerB) / 2),
            new("DINING", "200 SF", (BotVx + InnerR) / 2, (MidY + InnerB) / 2),
        ];

        float botNear = OuterB - Wall, botFar = OuterB;
        float topNear = OuterT, topFar = OuterT + Wall;
        float leftNear = OuterL, leftFar = OuterL + Wall;
        float rightNear = OuterR - Wall, rightFar = OuterR;
        float midNear = MidY - Part / 2, midFar = MidY + Part / 2;

        Openings =
        [
            // Doors.
            new(new SKPoint(255, (botNear + botFar) / 2), 36, true, botNear, botFar, true, -1),     // front entry
            new(new SKPoint(225, MidY), 30, true, midNear, midFar, true, 1),                        // bedroom <-> living
            new(new SKPoint(545, MidY), 30, true, midNear, midFar, true, 1),                        // kitchen <-> dining
            new(new SKPoint(TopVx1, 205), 28, false, TopVx1 - Part / 2, TopVx1 + Part / 2, true, 1),// bedroom <-> bath
            new(new SKPoint(TopVx2, 205), 26, false, TopVx2 - Part / 2, TopVx2 + Part / 2, true, 1),// bath <-> kitchen
            new(new SKPoint(BotVx, 385), 30, false, BotVx - Part / 2, BotVx + Part / 2, true, -1),  // living <-> dining
            // Windows.
            new(new SKPoint(561, (topNear + topFar) / 2), 48, true, topNear, topFar, false, 0),     // kitchen
            new(new SKPoint(340, (botNear + botFar) / 2), 40, true, botNear, botFar, false, 0),     // living
            new(new SKPoint((leftNear + leftFar) / 2, 225), 48, false, leftNear, leftFar, false, 0),// bedroom
            new(new SKPoint((rightNear + rightFar) / 2, 385), 48, false, rightNear, rightFar, false, 0), // dining
        ];

        var doors = new List<SKPoint>();
        var windows = new List<SKPoint>();
        foreach (Opening o in Openings)
        {
            if (o.IsDoor)
                doors.Add(o.Center);
            else
                windows.Add(o.Center);
        }

        DoorPoints = doors;
        WindowPoints = windows;
    }

    // Convenience polygons in PDF coordinates.
    public SKPoint[] FloorAreaPolygon =>
    [
        new(InnerL, InnerT), new(InnerR, InnerT), new(InnerR, InnerB), new(InnerL, InnerB),
    ];

    public SKPoint[] WallCenterlineLoop
    {
        get
        {
            float c = Wall / 2f;
            return
            [
                new(OuterL + c, OuterT + c), new(OuterR - c, OuterT + c),
                new(OuterR - c, OuterB - c), new(OuterL + c, OuterB - c),
                new(OuterL + c, OuterT + c),
            ];
        }
    }

    public SKPoint[] LivingRoomPolygon =>
    [
        new(InnerL, MidY + Part / 2), new(BotVx - Part / 2, MidY + Part / 2),
        new(BotVx - Part / 2, InnerB), new(InnerL, InnerB),
    ];

    public SKPoint[] RoofFootprintPolygon =>
    [
        new(OuterL, OuterT), new(OuterR, OuterT), new(OuterR, OuterB), new(OuterL, OuterB),
    ];
}
