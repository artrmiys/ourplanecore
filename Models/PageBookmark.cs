namespace OurPlaneCore;

public sealed class PageBookmark
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string PageFolder { get; set; } = "";
    public string PageName { get; set; } = "";
    public float Zoom { get; set; }
    public float PanX { get; set; }
    public float PanY { get; set; }
    public string CreatedAtUtc { get; set; } = "";
    public string UpdatedAtUtc { get; set; } = "";
}
