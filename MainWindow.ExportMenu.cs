using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace OurPlanCore;

public partial class MainWindow
{
    private void BtnSaveAsMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not UIElement target)
            return;

        var menu = new ContextMenu
        {
            PlacementTarget = target,
            Placement = PlacementMode.Bottom,
        };
        AddSaveAsMenuItems(menu);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void BtnRightExportMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not UIElement target)
            return;

        bool hasJob = _currentJob != null;
        var menu = new ContextMenu
        {
            PlacementTarget = target,
            Placement = PlacementMode.Top,
        };

        menu.Items.Add(MakeMenuItem("Save", hasJob, () => BtnSave_Click(this, new RoutedEventArgs())));
        AddSaveAsMenuItems(menu);
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Export CSV", hasJob, () => BtnExportCsv_Click(this, new RoutedEventArgs())));
        menu.Items.Add(MakeMenuItem("Export TXT", hasJob, () => BtnExportTxt_Click(this, new RoutedEventArgs())));
        if (IsModuleEnabled(ModuleId.ExcelIntegration))
        {
            menu.Items.Add(MakeMenuItem("Export Excel", hasJob, () => BtnExportExcel_Click(this, new RoutedEventArgs())));
            menu.Items.Add(MakeMenuItem("Export to Current Excel", hasJob, () => BtnExportCurrentExcel_Click(this, new RoutedEventArgs())));
        }
        menu.IsOpen = true;
    }

    private void AddSaveAsMenuItems(ContextMenu menu)
    {
        bool hasJob = _currentJob != null;
        bool canSavePortable = hasJob && (IsCurrentJobWritable || HasCurrentPackageSession);
        MenuItem portable = MakeMenuItem(
            "One portable file (.ourplan)...",
            canSavePortable,
            SaveAsOurPlanProject);
        portable.ToolTip = !hasJob
            ? "Open or create a project first."
            : !canSavePortable
                ? "A read-only legacy folder cannot be packaged from this window."
                : "Creates a new portable file and makes it the active project.";
        ToolTipService.SetShowOnDisabled(portable, true);
        menu.Items.Add(portable);

        MenuItem legacy = MakeMenuItem(
            "Legacy folder copy...",
            hasJob && IsCurrentJobWritable,
            SaveLegacyFolderCopy);
        legacy.ToolTip = !hasJob
            ? "Open or create a project first."
            : !IsCurrentJobWritable
                ? "Legacy folder copies require a writable project."
                : "Creates a compatible folder copy; the current project stays active.";
        ToolTipService.SetShowOnDisabled(legacy, true);
        menu.Items.Add(legacy);
    }
}
