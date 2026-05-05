using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SkiaSharp;

namespace SmartTakeoffs;

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
        public string          JoistLengthRounding { get; set; } = JoistTakeoffCalculator.RoundingNearestEvenFoot;
        public bool            JoistShowLabels { get; set; }
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
        public List<PtDto>  Points     { get; set; } = [];
    }

    public sealed record PtDto(float X, float Y);

    // ── Path convention: <pdf>.smarttakeoffs.json ────────────────────────────

    public static string PathFor(string pdfPath) =>
        Path.ChangeExtension(pdfPath, null) + ".smarttakeoffs.json";

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
                MeasurementType = SmartTakeoffsJobStore.NormalizeMeasurementType(item.MeasurementType),
                IsJoistTakeoff = item.IsJoistTakeoff,
                JoistType = item.JoistType,
                JoistSpacingInches = item.JoistSpacingInches,
                JoistDirectionDegrees = item.JoistDirectionDegrees,
                JoistLengthRounding = JoistTakeoffCalculator.NormalizeLengthRounding(item.JoistLengthRounding),
                JoistShowLabels = item.JoistShowLabels,
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
                    Points     = m.Points.Select(p => new PtDto(p.X, p.Y)).ToList(),
                };
                dto.Measurements.Add(md);
            }
            pf.Items.Add(dto);
        }
        File.WriteAllText(PathFor(pdfPath),
            JsonSerializer.Serialize(pf, new JsonSerializerOptions { WriteIndented = true }));
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    public static (double scale, UnitMode unit, List<TakeoffItem> items) Restore(string pdfPath)
    {
        string path = PathFor(pdfPath);
        if (!File.Exists(path))
        {
            string legacyPath = LegacyPathFor(pdfPath);
            if (!File.Exists(legacyPath)) return (0, SmartTakeoffs.UnitMode.Metric, []);
            path = legacyPath;
        }

        ProjectFile? pf;
        try   { pf = JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(path)); }
        catch { return (0, SmartTakeoffs.UnitMode.Metric, []); }
        if (pf == null) return (0, SmartTakeoffs.UnitMode.Metric, []);

        var items = new List<TakeoffItem>();
        foreach (var dto in pf.Items)
        {
            var item = new TakeoffItem
            {
                Id = dto.Id,
                Name = dto.Name,
                Color = dto.Color,
                MeasurementType = SmartTakeoffsJobStore.NormalizeMeasurementType(dto.MeasurementType),
                IsJoistTakeoff = dto.IsJoistTakeoff,
                JoistType = dto.JoistType,
                JoistSpacingInches = dto.JoistSpacingInches > 0 ? dto.JoistSpacingInches : 16,
                JoistDirectionDegrees = dto.JoistDirectionDegrees,
                JoistLengthRounding = JoistTakeoffCalculator.NormalizeLengthRounding(dto.JoistLengthRounding),
                JoistShowLabels = dto.JoistShowLabels,
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
                    Points     = md.Points.Select(p => new SKPoint(p.X, p.Y)).ToList(),
                });
            }
            SmartTakeoffsJobStore.ApplyTakeoffPropertiesToMeasurements(item);
            items.Add(item);
        }
        var unit = pf.UnitMode == "Imperial" ? SmartTakeoffs.UnitMode.Imperial : SmartTakeoffs.UnitMode.Metric;
        return (pf.Scale, unit, items);
    }
}
