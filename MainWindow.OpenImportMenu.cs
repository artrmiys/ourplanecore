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
        menu.Items.Add(MakeMenuItem("Recent jobs...", true, ShowRecentJobPicker));
        menu.Items.Add(MakeMenuItem("Open a job from a folder...", true, OpenJobFromFolderDialog));

        // Create a new job.
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuHeader("CREATE A NEW JOB"));
        menu.Items.Add(MakeMenuItem("Blank job - start empty, add sheets later", true, () => CreateBlankJobFromDialog()));
        menu.Items.Add(MakeMenuItem("New job from a folder of PDFs...", true, () => CreateJobFromDialog()));
        menu.Items.Add(MakeMenuItem("Import PDF Takeoffs...", true, () => BtnImportPdfTakeoffs_Click(sender, new RoutedEventArgs())));
        menu.Items.Add(MakeMenuItem("Sample job (demo to explore)", true, CreateSampleJob));

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
