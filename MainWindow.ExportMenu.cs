using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace OurPlanCore;

public partial class MainWindow
{
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
        menu.Items.Add(MakeMenuItem("Save As OurPlan Project...", hasJob, SaveAsOurPlanProject));
        menu.Items.Add(MakeMenuItem("Save a Legacy Folder Copy...", hasJob, SaveLegacyFolderCopy));
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
}
