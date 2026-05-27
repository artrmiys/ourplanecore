using System;
using System.Windows;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void BtnRefreshTakeoffsTree_Click(object sender, RoutedEventArgs e)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before refreshing Takeoffs.";
            return;
        }

        ClearTakeoffSectionDropCue();
        ClearTakeoffPositionDropCue();
        ClearTakeoffFolderDropCue();
        ResetTakeoffsDragState();

        LoadTakeoffsForJob();
        if (!TakeoffsReloadStatusIndicatesFailure(TxtStatus.Text))
            TxtStatus.Text = "Takeoffs tree refreshed.";
    }

    private static bool TakeoffsReloadStatusIndicatesFailure(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return false;

        return status.StartsWith("Takeoffs tree reload failed", StringComparison.OrdinalIgnoreCase) ||
               status.StartsWith("Takeoffs loaded, but", StringComparison.OrdinalIgnoreCase);
    }
}
