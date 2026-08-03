using System.Collections.Generic;
using System.Linq;

namespace OurPlanCore;

public sealed class TakeoffItem
{
    public string Id { get; init; } = System.Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Item";
    public string Color { get; set; } = "#FF4444";
    public string FolderPath { get; set; } = "";
    public string MeasurementType { get; set; } = "line";
    public string CountSymbol { get; set; } = CountDisplaySymbol.Circle;
    public double UnitPrice { get; set; }
    public string Notes { get; set; } = "";
    public bool IsJoistTakeoff { get; set; }
    public string JoistType { get; set; } = "";
    public double JoistSpacingInches { get; set; } = 16;
    public double JoistDirectionDegrees { get; set; }
    public bool JoistDirectionFollowsAreaRotation { get; set; } = true;
    public bool JoistAddEndJoist { get; set; }
    public string JoistPitch { get; set; } = "";
    public string JoistLengthRounding { get; set; } = JoistTakeoffCalculator.RoundingNearestEvenFoot;
    public bool JoistShowLabels { get; set; }
    public bool JoistShowLabelsUserSet { get; set; }
    public bool JoistDetailedLabels { get; set; } = true;

    public List<Measurement> Measurements { get; } = [];

    // Multiline: offset companions that auto-receive a parallel line each
    // time a line is drawn on this item. Empty for ordinary items.
    public List<MultiLineOffsetConfig> MultiLineOffsets { get; set; } = [];

    public bool IsJoistArea =>
        IsJoistTakeoff && OurPlanCoreJobStore.NormalizeMeasurementType(MeasurementType) == "area";

    public bool HasPendingJoistDirections =>
        IsJoistArea &&
        Measurements.Any(measurement =>
            OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area" &&
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
        string mt = OurPlanCoreJobStore.NormalizeMeasurementType(MeasurementType);
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
