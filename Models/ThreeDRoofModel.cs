namespace OurPlanCore;

public sealed class ThreeDRoofGuidePoint
{
    public double PdfX { get; set; }
    public double PdfY { get; set; }
    public double XFeet { get; set; }
    public double YFeet { get; set; }
    public double ZFeet { get; set; }
}

public sealed class ThreeDRoofGuide
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Kind { get; set; } = ThreeDRoofGuideKinds.Ridge;
    public string Label { get; set; } = "";
    public string PageFolder { get; set; } = "";
    public string LevelKey { get; set; } = "";
    public List<ThreeDRoofGuidePoint> RawPoints { get; set; } = [];
    public List<ThreeDRoofGuidePoint> Points { get; set; } = [];
    public double ElevationFeet { get; set; }
    public double PitchRisePerFoot { get; set; }
    public string RoofGroupId { get; set; } = "";
    public string RoofGroupLabel { get; set; } = "";

    // Revit "Defines Slope": this edge contributes a slope plane to the roof
    // envelope. Source of truth for slope; Kind (eave/rake) mirrors it.
    public bool DefinesSlope { get; set; }

    // Revit per-edge overhang: eave projection beyond the wall, in feet.
    // Stored/edited now; envelope projection geometry lands in a later slice.
    public double OverhangFeet { get; set; }
    public string Color { get; set; } = "#8B5CF6";
    public string Status { get; set; } = "guide";
    public string AdjustmentStatus { get; set; } = "";
    public string AdjustmentMessage { get; set; } = "";
}

public sealed class ThreeDRoofVertex
{
    public double XFeet { get; set; }
    public double YFeet { get; set; }
    public double ZFeet { get; set; }
}

public sealed class ThreeDRoofPlane
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Kind { get; set; } = "preview";
    public string Label { get; set; } = "";
    public List<ThreeDRoofVertex> Points { get; set; } = [];
    public string Color { get; set; } = "#D97706";
    public double Opacity { get; set; } = 0.42;
    public string Status { get; set; } = "preview";
    public string Message { get; set; } = "";
    public string RoofGroupId { get; set; } = "";
    public string RoofGroupLabel { get; set; } = "";
    public List<string> SourceGuideIds { get; set; } = [];
}

public sealed class ThreeDRoofIssue
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Severity { get; set; } = "warning";
    public string Kind { get; set; } = "";
    public string Message { get; set; } = "";
    public string PageFolder { get; set; } = "";
    public bool HasPdfPoint { get; set; }
    public double PdfX { get; set; }
    public double PdfY { get; set; }
    public double XFeet { get; set; }
    public double YFeet { get; set; }
    public double ZFeet { get; set; }
    public string Color { get; set; } = "#DC2626";
    public string RoofGroupId { get; set; } = "";
    public string RoofGroupLabel { get; set; } = "";
    public List<string> GuideIds { get; set; } = [];
}

public static class ThreeDRoofGuideKinds
{
    public const string Ridge = "ridge";
    public const string Hip = "hip";
    public const string Valley = "valley";
    public const string Eave = "eave";
    public const string Rake = "rake";
    public const string Pitch = "pitch";

    public static readonly string[] All = [Ridge, Hip, Valley, Eave, Rake, Pitch];

    public static string Normalize(string value)
    {
        string clean = (value ?? "").Trim().ToLowerInvariant().Replace(" ", "_");
        return All.Contains(clean, StringComparer.OrdinalIgnoreCase) ? clean : Ridge;
    }

    public static string Title(string value) =>
        Normalize(value) switch
        {
            Hip => "Hip",
            Valley => "Valley",
            Eave => "Eave",
            Rake => "Rake",
            Pitch => "Pitch",
            _ => "Ridge",
        };

    public static string Color(string value) =>
        Normalize(value) switch
        {
            Hip => "#EF4444",
            Valley => "#2563EB",
            Eave => "#059669",
            Rake => "#F59E0B",
            Pitch => "#7C3AED",
            _ => "#111827",
        };
}
