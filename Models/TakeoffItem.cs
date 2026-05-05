using System.Collections.Generic;
using System.Linq;

namespace OurPlaneCore;

public sealed class TakeoffItem
{
    public string Id { get; init; } = System.Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Item";
    public string Color { get; set; } = "#FF4444";
    public string FolderPath { get; set; } = "";
    public string MeasurementType { get; set; } = "line";
    public double UnitPrice { get; set; }
    public string Notes { get; set; } = "";
    public bool IsJoistTakeoff { get; set; }
    public string JoistType { get; set; } = "";
    public double JoistSpacingInches { get; set; } = 16;
    public double JoistDirectionDegrees { get; set; }
    public string JoistPitch { get; set; } = "";
    public string JoistLengthRounding { get; set; } = JoistTakeoffCalculator.RoundingNearestEvenFoot;
    public bool JoistShowLabels { get; set; }

    public List<Measurement> Measurements { get; } = [];

    public bool IsJoistArea =>
        IsJoistTakeoff && OurPlaneCoreJobStore.NormalizeMeasurementType(MeasurementType) == "area";

    public bool HasPendingJoistDirections =>
        IsJoistArea &&
        Measurements.Any(measurement =>
            OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area" &&
            !measurement.JoistDirectionLocked);

    public double Total(double fallbackScaleMetersPerPt) =>
        IsJoistArea
            ? HasPendingJoistDirections
                ? 0
                : JoistTakeoffCalculator.Summarize(Measurements, fallbackScaleMetersPerPt).TotalLengthMeters
            : Measurements.Sum(m => m.Value(fallbackScaleMetersPerPt));

    public string TotalLabel(double fallbackScaleMetersPerPt, UnitMode unit = UnitMode.Metric)
    {
        if (Measurements.Count == 0) return "-";
        if (IsJoistArea)
        {
            if (HasPendingJoistDirections)
                return "set direction";

            return JoistTakeoffCalculator.FormatSummary(JoistSummary(fallbackScaleMetersPerPt), unit);
        }

        double v = Total(fallbackScaleMetersPerPt);
        string mt = OurPlaneCoreJobStore.NormalizeMeasurementType(MeasurementType);
        return mt switch
        {
            "line" => Units.FormatLength(v, unit),
            "area" => Units.FormatArea(v, unit),
            "point" => Units.FormatCount(v),
            _ => fallbackScaleMetersPerPt > 0 ? $"{v:F3}" : "-",
        };
    }

    public JoistLayoutSummary JoistSummary(double fallbackScaleMetersPerPt)
    {
        if (!IsJoistArea)
            return new JoistLayoutSummary(false, 0, 0, 0, 0);

        return JoistTakeoffCalculator.Summarize(Measurements, fallbackScaleMetersPerPt);
    }
}
