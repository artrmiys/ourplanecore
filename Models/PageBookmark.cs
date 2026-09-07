namespace OurPlanCore;

public sealed class PageBookmark
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string PageFolder { get; set; } = "";
    public string PageName { get; set; } = "";
    public string Type { get; set; } = "view";
    public float Zoom { get; set; }
    public float PanX { get; set; }
    public float PanY { get; set; }
    public string CropImagePath { get; set; } = "";
    public float CropLeft { get; set; }
    public float CropTop { get; set; }
    public float CropRight { get; set; }
    public float CropBottom { get; set; }
    public string CreatedAtUtc { get; set; } = "";
    public string UpdatedAtUtc { get; set; } = "";
}
