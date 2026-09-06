using System.Windows;

namespace OurPlanCore;

public partial class MainWindow
{
    private void OpenProjectDataRecovery()
    {
        if (_currentJob == null) return;
        if (_takeoffSaveService.HasPending && !_takeoffSaveService.Flush().Success)
        {
            PostStatusWarning("Pending changes could not be saved. Keep this project open and resolve the save failure before reloading data.");
            return;
        }
        if (DataFileReader.Issues(_currentJob.RootPath).Count == 0)
        {
            PostStatusInfo("No protected data files in this project.");
            return;
        }
        string root = _currentJob.RootPath;
        var dialog = new ProjectDataRecoveryDialog(root) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Changed) OpenJob(root);
        UpdateStatusBarSegments();
    }
}
