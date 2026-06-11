namespace OurPlaneCore;

// One offset companion of a multiline takeoff item: every line drawn on the
// owner item also produces a parallel line in the companion item, shifted
// by Meters to the chosen side of the drawing direction.
public sealed class MultiLineOffsetConfig
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#2196F3";
    public double Meters { get; set; } = 0.1;
    public bool RightSide { get; set; }
    public string CompanionFolder { get; set; } = "";
}
