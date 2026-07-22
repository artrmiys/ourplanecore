using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    private bool TryStartExtraJoistShortcut()
    {
        if (!TryResolveSelectedExtraJoistTarget(
                out TakeoffItem item,
                out Measurement area,
                out _))
        {
            return false;
        }

        if (!RequireModule(ModuleId.AdvancedTakeoffTools, "Add Extra Joist"))
            return true;

        StartExtraJoistPlacement(item, area);
        return true;
    }

    private void BtnAddExtraJoist_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireModule(ModuleId.AdvancedTakeoffTools, "Add Extra Joist"))
            return;

        if (!TryResolveSelectedExtraJoistTarget(
                out TakeoffItem item,
                out Measurement area,
                out string message))
        {
            TxtStatus.Text = message;
            return;
        }

        StartExtraJoistPlacement(item, area);
    }

    private bool TryResolveSelectedExtraJoistTarget(
        out TakeoffItem item,
        out Measurement area,
        out string message)
    {
        var selected = _viewport.GetSelectedMeasurements().Distinct().ToList();
        if (selected.Count > 0)
        {
            if (selected.Count == 1 &&
                FindTakeoffItemForMeasurement(selected[0]) is { IsJoistArea: true } selectedItem &&
                IsJoistAreaMeasurement(selected[0]))
            {
                item = selectedItem;
                area = selected[0];
                message = "";
                return true;
            }

            item = null!;
            area = null!;
            message = "Select exactly one Area segment from a Joist Area before adding an Extra Joist.";
            return false;
        }

        if (TakeoffsTree.SelectedItem is TreeViewItem { Tag: TakeoffMeasurementNode node } &&
            node.Item.IsJoistArea &&
            IsJoistAreaMeasurement(node.Measurement))
        {
            item = node.Item;
            area = node.Measurement;
            message = "";
            return true;
        }

        TakeoffItem? candidate = TakeoffsTree.SelectedItem is TreeViewItem { Tag: TakeoffItem treeItem }
            ? treeItem
            : _activeItem;
        Measurement? candidateArea = candidate?.IsJoistArea == true
            ? SelectedJoistAreaMeasurement(candidate)
            : null;
        if (candidate != null && candidateArea != null)
        {
            item = candidate;
            area = candidateArea;
            message = "";
            return true;
        }

        item = null!;
        area = null!;
        message = "Select exactly one Area segment from a Joist Area before adding an Extra Joist.";
        return false;
    }

    private static bool IsJoistAreaMeasurement(Measurement measurement) =>
        measurement.JoistEnabled &&
        OurPlanCoreJobStore.NormalizeMeasurementType(measurement.MType) == "area";

    private void StartExtraJoistPlacement(TakeoffItem item, Measurement area)
    {
        if (!EnsureCurrentJobWritable("add an Extra Joist"))
            return;

        if (!item.IsJoistArea ||
            !item.Measurements.Contains(area) ||
            OurPlanCoreJobStore.NormalizeMeasurementType(area.MType) != "area")
        {
            TxtStatus.Text = "Extra Joist requires one Area segment from a Joist Area takeoff.";
            return;
        }

        // Stop Record and any abandoned multi-Area direction operation before
        // entering the one-shot cursor placement mode.
        SetTool("select");
        _pendingJoistDirectionApplyTargets = null;
        OurPlanCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
        if (!area.JoistDirectionLocked)
        {
            TxtStatus.Text = "Set this Area segment's own joist direction before adding an Extra Joist.";
            return;
        }

        QueueTakeoffAutosave(item);

        void BeginPlacement()
        {
            if (!IsCurrentJobWritable || !item.Measurements.Contains(area))
                return;

            _viewport.SetMeasurements(_takeoffItems.SelectMany(takeoff => takeoff.Measurements));
            _viewport.SelectMeasurements([area]);
            _viewport.BeginExtraJoistPlacement(area);
        }

        if (_currentPage == null || !IsSamePageFolder(_currentPage.FolderPath, area.PageFolder))
        {
            PageInfo? page = OurPlanCoreJobStore.TryReadPage(area.PageFolder);
            if (page == null)
            {
                TxtStatus.Text = "Cannot open the sheet for this Joist Area segment.";
                return;
            }

            OpenPageInActiveTab(page);
            Dispatcher.InvokeAsync(BeginPlacement);
            return;
        }

        BeginPlacement();
    }
}
