using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    private void BtnAddJoists_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireModule(ModuleId.AdvancedTakeoffTools, "Add Joists"))
            return;

        if (!TryResolveSelectedJoistTakeoff(out TakeoffItem item, out string message))
        {
            TxtStatus.Text = message;
            return;
        }

        AddJoistsToAllAreas(item);
    }

    private void AddJoistsToAllAreas(TakeoffItem item)
    {
        if (!EnsureCurrentJobWritable("add joists to the selected Joist Area"))
            return;

        if (!item.IsJoistArea)
        {
            TxtStatus.Text = "Select a Joist Area takeoff before using Add Joists.";
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
            TxtStatus.Text = $"Add Joists refreshed {areas.Count} Area segment(s); {pending} still need their own direction.";
            BeginNextPendingJoistDirectionCapture(item);
            return;
        }

        JoistLayoutSummary summary = JoistTakeoffCalculator.Summarize(areas, _viewport.ScaleMetersPerPt);
        string endStatus = item.JoistAddEndJoist
            ? "Add End Joist is applied to every Area segment."
            : "Add End Joist is off for every Area segment.";
        TxtStatus.Text = $"Joists refreshed for all {areas.Count} Area segment(s) in {item.Name}: " +
                         $"{JoistTakeoffCalculator.FormatSummary(summary, _viewport.UnitMode)}. {endStatus}";
    }

    private bool TryResolveSelectedJoistTakeoff(out TakeoffItem item, out string message)
    {
        IReadOnlyList<Measurement> selected = _viewport.GetSelectedMeasurements();
        if (selected.Count > 0)
        {
            List<TakeoffItem> selectedItems = selected
                .Select(FindTakeoffItemForMeasurement)
                .Where(candidate => candidate != null)
                .Select(candidate => candidate!)
                .Distinct()
                .ToList();
            if (selectedItems.Count == 1 && selectedItems[0].IsJoistArea)
            {
                item = selectedItems[0];
                message = "";
                return true;
            }

            item = null!;
            message = "Select Area segments from one Joist Area takeoff before using Add Joists.";
            return false;
        }

        TakeoffItem? treeItem = TakeoffsTree.SelectedItem switch
        {
            TreeViewItem { Tag: TakeoffItem takeoff } => takeoff,
            TreeViewItem { Tag: TakeoffMeasurementNode node } => node.Item,
            _ => null,
        };
        TakeoffItem? candidate = treeItem ?? _activeItem;
        if (candidate?.IsJoistArea == true)
        {
            item = candidate;
            message = "";
            return true;
        }

        item = null!;
        message = "Select a Joist Area takeoff or one of its Area segments before using Add Joists.";
        return false;
    }
}
