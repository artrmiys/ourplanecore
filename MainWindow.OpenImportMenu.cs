using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace OurPlanCore;

public partial class MainWindow
{
    private void BtnOpenImportMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        bool hasJob = _currentJob != null;

        // Open an existing job.
        menu.Items.Add(MakeMenuHeader("OPEN A JOB"));
        menu.Items.Add(MakeMenuItem("Recent projects...", true, ShowRecentJobPicker));
        menu.Items.Add(MakeMenuItem("Open OurPlan project (.ourplan)...", true, OpenOurPlanProjectDialog));
        menu.Items.Add(MakeMenuItem("Open legacy project folder...", true, OpenJobFromFolderDialog));

        // Create a new job.
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuHeader("CREATE A NEW JOB"));
        menu.Items.Add(MakeMenuItem("Blank OurPlan project - start empty", true, () => CreateBlankJobFromDialog(forceOurPlan: true)));
        menu.Items.Add(MakeMenuItem("New OurPlan project from a folder of PDFs...", true, () => CreateJobFromDialog(forceOurPlan: true)));
        menu.Items.Add(MakeMenuItem("Import PDF Takeoffs...", true, () => BtnImportPdfTakeoffs_Click(sender, new RoutedEventArgs())));
        menu.Items.Add(MakeMenuItem("Sample job (demo to explore)", true, () => CreateSampleJob()));
        menu.Items.Add(MakeSubmenu("Create legacy folder project",
            MakeMenuItem("Blank legacy project...", true, () => CreateLegacyBlankJobFromDialog()),
            MakeMenuItem("Legacy project from PDFs...", true, () => CreateLegacyJobFromDialog())));

        // Import into the job that's already open.
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeSubmenu("Import into the current job",
            MakeMenuItem("PDF file(s)...", hasJob, () => BtnImport_Click(sender, new RoutedEventArgs())),
            MakeMenuItem("Folder of PDFs...", hasJob, () => BtnImportPdfFolder_Click(sender, new RoutedEventArgs())),
            MakeMenuItem("PlanSwift project...", hasJob, () => BtnImportPlanSwiftToCurrentJob_Click(sender, new RoutedEventArgs()))));

        // Manage where jobs live.
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Manage job folders...", true, OpenJobFromJobsRootDialog));

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
