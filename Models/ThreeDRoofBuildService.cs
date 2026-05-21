namespace OurPlaneCore;

public sealed class ThreeDRoofBuildResult
{
    public List<ThreeDRoofGuide> Guides { get; } = [];
    public List<ThreeDRoofPlane> Planes { get; } = [];
    public List<ThreeDRoofIssue> Issues { get; } = [];
    public List<string> Messages { get; } = [];
    public int AdjustedGuideCount { get; set; }
    public bool PlaneBuildBlocked { get; set; }
}

public static class ThreeDRoofBuildService
{
    public static ThreeDRoofBuildResult Build(ThreeDWallModel model)
    {
        var result = new ThreeDRoofBuildResult();
        result.Guides.AddRange(model.RoofGuides
            .Where(guide => !string.Equals(guide.Status, ThreeDRoofPreviewBuilder.GeneratedSeamStatus, StringComparison.OrdinalIgnoreCase))
            .Select(CloneGuide));

        if (!model.Slabs.Any(slab => string.Equals(slab.LevelKey, "roof", StringComparison.OrdinalIgnoreCase)))
        {
            result.Messages.Add("Create Roof Base from selected area takeoffs before generating the roof.");
            result.PlaneBuildBlocked = true;
            return result;
        }

        if (!model.RoofGuides.Any(ThreeDRoofPreviewBuilder.IsSlopeDefiningGuide))
        {
            result.Messages.Add("Select roof base edges, set Defines Slope, and apply pitch before generating the roof.");
            result.PlaneBuildBlocked = true;
            return result;
        }

        ThreeDRoofPreviewBuildResult preview = ThreeDRoofPreviewBuilder.BuildPreview(model);
        result.Planes.AddRange(preview.Planes);
        result.Guides.AddRange(preview.Guides);
        result.Messages.AddRange(preview.Messages);
        result.PlaneBuildBlocked = result.Planes.Count == 0;
        return result;
    }

    private static ThreeDRoofGuide CloneGuide(ThreeDRoofGuide guide) =>
        new()
        {
            Id = guide.Id,
            Kind = guide.Kind,
            Label = guide.Label,
            PageFolder = guide.PageFolder,
            LevelKey = guide.LevelKey,
            RawPoints = guide.RawPoints.Select(ClonePoint).ToList(),
            Points = guide.Points.Select(ClonePoint).ToList(),
            ElevationFeet = guide.ElevationFeet,
            PitchRisePerFoot = guide.PitchRisePerFoot,
            RoofGroupId = guide.RoofGroupId,
            RoofGroupLabel = guide.RoofGroupLabel,
            DefinesSlope = guide.DefinesSlope,
            OverhangFeet = guide.OverhangFeet,
            Color = guide.Color,
            Status = guide.Status,
            AdjustmentStatus = guide.AdjustmentStatus,
            AdjustmentMessage = guide.AdjustmentMessage,
        };

    private static ThreeDRoofGuidePoint ClonePoint(ThreeDRoofGuidePoint point) =>
        new()
        {
            PdfX = point.PdfX,
            PdfY = point.PdfY,
            XFeet = point.XFeet,
            YFeet = point.YFeet,
            ZFeet = point.ZFeet,
        };
}
