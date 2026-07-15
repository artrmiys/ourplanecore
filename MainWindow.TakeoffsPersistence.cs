using System;
using System.IO;
using System.Windows;

namespace OurPlanCore;

public partial class MainWindow
{
    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "No job open - nothing to save.";
            return;
        }
        if (!EnsureCurrentJobWritable("save changes"))
            return;

        try
        {
            FlushTakeoffAutosaves();
            SaveCurrentPageScale();
            SaveCurrentPageAnnotations();
            foreach (var item in _takeoffItems)
            {
                EnsureTakeoffItemFolder(item);
                OurPlanCoreJobStore.SaveTakeoffItem(item);
            }

            string? snapshotPath = SaveJobRecoverySnapshot("manual_save");
            string snapshotText = string.IsNullOrWhiteSpace(snapshotPath)
                ? ""
                : $" Snapshot: {Path.GetRelativePath(_currentJob.RootPath, snapshotPath)}";
            TxtStatus.Text = $"Saved takeoffs -> {_currentJob.TakeoffsRoot}.{snapshotText}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Save failed:\n{ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnAddObservation_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open or create a job before adding observations.";
            return;
        }

        string defaultText = _currentPage == null
            ? ""
            : $"Page {_currentPage.Name}: ";
        string? text = ShowInputDialog("Observation:", "Add Observation", defaultText);
        if (string.IsNullOrWhiteSpace(text))
            return;

        try
        {
            var observation = SmartContextStore.AddManualObservation(_currentJob, _currentPage, text);
            TxtStatus.Text = $"Saved observation {observation.Id} -> {_currentJob.AIContextRoot}";
            LoadObservationsInbox();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot save observation:\n{ex.Message}", "Add Observation",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
