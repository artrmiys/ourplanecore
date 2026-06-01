using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SkiaSharp;

namespace OurPlaneCore;

internal sealed class ProjectFile
{
    public double         Scale    { get; set; }
    public string         UnitMode { get; set; } = "Metric";
    public List<ItemDto>  Items    { get; set; } = [];

    public sealed class ItemDto
    {
        public string          Id           { get; set; } = "";
        public string          Name         { get; set; } = "";
        public string          Color        { get; set; } = "#FF4444";
        public string          MeasurementType { get; set; } = "line";
        public bool            IsJoistTakeoff { get; set; }
        public string          JoistType { get; set; } = "";
        public double          JoistSpacingInches { get; set; } = 16;
        public double          JoistDirectionDegrees { get; set; }
        public bool            JoistDirectionFollowsAreaRotation { get; set; } = true;
        public bool            JoistAddEndJoist { get; set; } = true;
        public string          JoistPitch { get; set; } = "";
        public string          JoistLengthRounding { get; set; } = JoistTakeoffCalculator.RoundingNearestEvenFoot;
        public bool?           JoistShowLabels { get; set; }
        public bool            JoistShowLabelsExplicit { get; set; }
        public bool?           JoistDetailedLabels { get; set; } = true;
        public List<MeasDto>   Measurements { get; set; } = [];
    }

    public sealed class MeasDto
    {
        public string       Id         { get; set; } = "";
        public string       MType      { get; set; } = "line";
        public string       Color      { get; set; } = "#FF4444";
        public string       PageFolder { get; set; } = "";
        public double       ScaleMetersPerPt { get; set; }
        public double       JoistDirectionDegrees { get; set; }
        public bool         JoistDirectionLocked { get; set; }
        public bool         JoistDirectionFollowsAreaRotation { get; set; } = true;
        public bool         JoistAddEndJoist { get; set; } = true;
        public List<PtDto>  Points     { get; set; } = [];
        public List<List<PtDto>> Holes { get; set; } = [];
    }

    public sealed record PtDto(float X, float Y);

    // ── Path convention: <pdf>.ourplanecore.json ────────────────────────────

    public static string PathFor(string pdfPath) =>
        Path.ChangeExtension(pdfPath, null) + ".ourplanecore.json";

    private static string LegacyPathFor(string pdfPath) =>
        Path.ChangeExtension(pdfPath, null) + ".planswift.json";

    // ── Write ─────────────────────────────────────────────────────────────────

    public static void Save(string pdfPath, double scale, UnitMode unit, IEnumerable<TakeoffItem> items)
    {
        var pf = new ProjectFile { Scale = scale, UnitMode = unit.ToString() };
        foreach (var item in items)
        {
            var dto = new ItemDto
            {
                Id = item.Id,
                Name = item.Name,
                Color = item.Color,
                MeasurementType = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType),
                IsJoistTakeoff = item.IsJoistTakeoff,
                JoistType = item.JoistType,
                JoistSpacingInches = item.JoistSpacingInches,
                JoistDirectionDegrees = item.JoistDirectionDegrees,
                JoistDirectionFollowsAreaRotation = item.JoistDirectionFollowsAreaRotation,
                JoistAddEndJoist = item.JoistAddEndJoist,
                JoistPitch = JoistTakeoffCalculator.NormalizePitch(item.JoistPitch),
                JoistLengthRounding = JoistTakeoffCalculator.NormalizeLengthRounding(item.JoistLengthRounding),
                JoistShowLabels = item.JoistShowLabels,
                JoistShowLabelsExplicit = true,
                JoistDetailedLabels = item.JoistDetailedLabels,
            };
            foreach (var m in item.Measurements)
            {
                var md = new MeasDto
                {
                    Id         = m.Id,
                    MType      = m.MType,
                    Color      = m.Color,
                    PageFolder = m.PageFolder,
                    ScaleMetersPerPt = m.ScaleMetersPerPt,
                    JoistDirectionDegrees = m.JoistDirectionDegrees,
                    JoistDirectionLocked = m.JoistDirectionLocked,
                    JoistDirectionFollowsAreaRotation = m.JoistDirectionFollowsAreaRotation,
                    JoistAddEndJoist = m.JoistAddEndJoist,
                    Points     = m.Points.Select(p => new PtDto(p.X, p.Y)).ToList(),
                    Holes      = m.Holes
                        .Where(hole => hole.Count >= 3)
                        .Select(hole => hole.Select(p => new PtDto(p.X, p.Y)).ToList())
                        .ToList(),
                };
                dto.Measurements.Add(md);
            }
            pf.Items.Add(dto);
        }
        IoUtil.WriteAllTextAtomic(PathFor(pdfPath),
            JsonSerializer.Serialize(pf, new JsonSerializerOptions { WriteIndented = true }));
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    public static (double scale, UnitMode unit, List<TakeoffItem> items) Restore(string pdfPath)
    {
        string path = PathFor(pdfPath);
        if (!File.Exists(path))
        {
            string legacyPath = LegacyPathFor(pdfPath);
            if (!File.Exists(legacyPath)) return (0, OurPlaneCore.UnitMode.Metric, []);
            path = legacyPath;
        }

        ProjectFile? pf;
        try   { pf = JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(path)); }
        catch (Exception ex)
        {
            AppLog.Warn(ex, $"Restore project file failed for {path}");
            return (0, OurPlaneCore.UnitMode.Metric, []);
        }
        if (pf == null) return (0, OurPlaneCore.UnitMode.Metric, []);

        var items = new List<TakeoffItem>();
        foreach (var dto in pf.Items)
        {
            var item = new TakeoffItem
            {
                Id = dto.Id,
                Name = dto.Name,
                Color = dto.Color,
                MeasurementType = OurPlaneCoreJobStore.NormalizeMeasurementType(dto.MeasurementType),
                IsJoistTakeoff = dto.IsJoistTakeoff,
                JoistType = dto.JoistType,
                JoistSpacingInches = dto.JoistSpacingInches > 0 ? dto.JoistSpacingInches : 16,
                JoistDirectionDegrees = dto.JoistDirectionDegrees,
                JoistDirectionFollowsAreaRotation = dto.JoistDirectionFollowsAreaRotation,
                JoistAddEndJoist = dto.JoistAddEndJoist,
                JoistPitch = JoistTakeoffCalculator.NormalizePitch(dto.JoistPitch),
                JoistLengthRounding = JoistTakeoffCalculator.NormalizeLengthRounding(dto.JoistLengthRounding),
                JoistShowLabels = ResolveJoistShowLabels(
                    dto.JoistShowLabels,
                    dto.JoistShowLabelsExplicit,
                    dto.MeasurementType,
                    dto.IsJoistTakeoff),
                JoistDetailedLabels = dto.JoistDetailedLabels ?? true,
            };
            foreach (var md in dto.Measurements)
            {
                item.Measurements.Add(new Measurement
                {
                    Id         = md.Id,
                    MType      = md.MType,
                    Color      = md.Color,
                    PageFolder = md.PageFolder,
                    ScaleMetersPerPt = md.ScaleMetersPerPt > 0 ? md.ScaleMetersPerPt : pf.Scale,
                    JoistDirectionDegrees = md.JoistDirectionDegrees,
                    JoistDirectionLocked = md.JoistDirectionLocked,
                    JoistDirectionFollowsAreaRotation = md.JoistDirectionFollowsAreaRotation,
                    JoistAddEndJoist = md.JoistAddEndJoist,
                    Points     = md.Points.Select(p => new SKPoint(p.X, p.Y)).ToList(),
                    Holes      = md.Holes
                        .Select(hole => hole.Select(p => new SKPoint(p.X, p.Y)).ToList())
                        .Where(hole => hole.Count >= 3)
                        .ToList(),
                });
            }
            OurPlaneCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
            items.Add(item);
        }
        var unit = pf.UnitMode == "Imperial" ? OurPlaneCore.UnitMode.Imperial : OurPlaneCore.UnitMode.Metric;
        return (pf.Scale, unit, items);
    }

    private static bool ResolveJoistShowLabels(
        bool? value,
        bool explicitValue,
        string measurementType,
        bool isJoistTakeoff)
    {
        bool isJoistArea = isJoistTakeoff &&
            OurPlaneCoreJobStore.NormalizeMeasurementType(measurementType) == "area";
        if (value.HasValue)
        {
            if (value.Value || explicitValue)
                return value.Value;

            return isJoistArea && JoistTakeoffDefaults.ShowLabels;
        }

        return isJoistArea && JoistTakeoffDefaults.ShowLabels;
    }
}
