using System.Linq;

namespace OurPlanCore;

public partial class MainWindow
{
    private void AddJoistsToAllAreas(TakeoffItem item)
    {
        if (!EnsureCurrentJobWritable("add joists to the selected Joist Area"))
            return;

        if (!item.IsJoistArea)
        {
            TxtStatus.Text = "Select a Joist Area takeoff before refreshing regular joists.";
            return;
        }

        List<Measurement> areas = item.Measurements
            .Where(measurement =>
                OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area")
            .Distinct()
            .ToList();
        if (areas.Count == 0)
        {
            TxtStatus.Text = $"{item.Name} has no Area segments to generate.";
            return;
        }

        if (IsRecordTool(_activeTool))
            SetTool("select");
        _viewport.CancelExtraJoistPlacement();
        _pendingJoistDirectionApplyTargets = null;

        // Shared item settings are copied to every Area, while the storage
        // provider intentionally preserves each locked per-Area direction.
        // This also applies Add End Joist to every Area before recalculation.
        OurPlanCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        QueueTakeoffAutosave(item);
        _viewport.SetMeasurements(_takeoffItems.SelectMany(takeoff => takeoff.Measurements));
        RefreshTreeItem(item);
        RefreshActiveTakeoffVisuals();
        RefreshEstimateTable();
        RefreshPagesTakeoffIndicators();
        ApplyTakeoffPageHighlights();
        RefreshSheetLegend();
        UpdateTotalDisplay();

        int pending = areas.Count(area => !area.JoistDirectionLocked);
        if (pending > 0)
        {
            TxtStatus.Text = $"Regular joists refreshed in {areas.Count} Area segment(s); {pending} still need their own direction.";
            BeginNextPendingJoistDirectionCapture(item);
            return;
        }

        JoistLayoutSummary summary = JoistTakeoffCalculator.Summarize(areas, _viewport.ScaleMetersPerPt);
        string endStatus = item.JoistAddEndJoist
            ? "Add End Joist is applied to every Area segment."
            : "Add End Joist is off for every Area segment.";
        TxtStatus.Text = $"Regular joists refreshed for all {areas.Count} Area segment(s) in {item.Name}: " +
                         $"{JoistTakeoffCalculator.FormatSummary(summary, _viewport.UnitMode)}. {endStatus}";
    }
}
