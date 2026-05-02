using System.Collections.Generic;
using System.Linq;

namespace SmartTakeoffs;

public sealed class TakeoffItem
{
    public string Id    { get; init; } = System.Guid.NewGuid().ToString();
    public string Name  { get; set; }  = "New Item";
    public string Color { get; set; }  = "#FF4444";
    public string FolderPath { get; set; } = "";
    public string MeasurementType { get; set; } = "line";

    public List<Measurement> Measurements { get; } = [];

    public double Total(double fallbackScaleMetersPerPt) =>
        Measurements.Sum(m => m.Value(fallbackScaleMetersPerPt));

    public string TotalLabel(double fallbackScaleMetersPerPt, UnitMode unit = UnitMode.Metric)
    {
        if (Measurements.Count == 0) return "—";
        double v  = Total(fallbackScaleMetersPerPt);
        string mt = SmartTakeoffsJobStore.NormalizeMeasurementType(MeasurementType);
        return mt switch
        {
            "line"  => Units.FormatLength(v, unit),
            "area"  => Units.FormatArea(v, unit),
            "point" => Units.FormatCount(v),
            _       => fallbackScaleMetersPerPt > 0 ? $"{v:F3}" : "—",
        };
    }
}
