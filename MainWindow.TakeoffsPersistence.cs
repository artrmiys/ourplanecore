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

        if (!TrySaveCurrentJobData("manual save"))
            return;

        if (HasCurrentPackageSession)
        {
            _currentPackageSession!.HasUnpackagedChanges = true;
            TrySaveCurrentPackage("manual save");
        }
        else
        {
            TxtStatus.Text = $"Saved legacy job -> {_currentJob.TakeoffsRoot}.";
        }
    }

    private bool TrySaveCurrentJobData(string operation)
    {
        if (_currentJob == null)
            return false;

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

            SaveJobRecoverySnapshot("manual_save");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, $"Project data save failed during {operation}.");
            MessageBox.Show($"Save failed during {operation}:\n{ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
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
