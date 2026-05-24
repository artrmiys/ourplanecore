using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void BtnOpenImportMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        bool hasJob = _currentJob != null;

        menu.Items.Add(MakeMenuItem("Open Job / Recent...", true, ShowRecentJobPicker));
        menu.Items.Add(MakeMenuItem("Browse Job Folder...", true, OpenJobFromFolderDialog));
        menu.Items.Add(MakeMenuItem("Jobs Root Folder...", true, OpenJobFromJobsRootDialog));
        menu.Items.Add(MakeMenuItem("New Job...", true, () => CreateJobFromDialog()));
        menu.Items.Add(MakeMenuItem("Sample Job", true, CreateSampleJob));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Import PDF(s) to Current Job...", hasJob, () => BtnImport_Click(sender, new RoutedEventArgs())));
        menu.Items.Add(MakeMenuItem("Import PlanSwift to Current Job...", hasJob, () => BtnImportPlanSwiftToCurrentJob_Click(sender, new RoutedEventArgs())));

        if (sender is Button button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            button.ContextMenu = menu;
        }

        menu.IsOpen = true;
        e.Handled = true;
    }
}
