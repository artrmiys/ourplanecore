using System.Windows;

namespace OurPlanCore;

public partial class MainWindow
{
    // Empty-state Start card actions (shown when no job is open).
    private void StartOpenRecent_Click(object sender, RoutedEventArgs e) => ShowRecentJobPicker();
    private void StartNewBlank_Click(object sender, RoutedEventArgs e) => CreateBlankJobFromDialog();
    private void StartNewFromPdf_Click(object sender, RoutedEventArgs e) => CreateJobFromDialog();
    private void StartSample_Click(object sender, RoutedEventArgs e) => CreateSampleJob();

    // Show the Start card only while there is no open job.
    private void UpdateNoJobOverlay()
    {
        if (NoJobOverlay != null)
            NoJobOverlay.Visibility = _currentJob == null ? Visibility.Visible : Visibility.Collapsed;
    }

    // F1 keyboard shortcuts cheat-sheet.
    private void ToggleShortcutsOverlay()
    {
        if (ShortcutsOverlay == null)
            return;
        foreach (var guide in WalkKeyboardSurface(ShortcutsOverlay).OfType<Controls.KeyboardShortcutsOverlay>())
            guide.RefreshAssignments(_customShortcuts, _keyboardCommands.Values);
        ShortcutsOverlay.Visibility =
            ShortcutsOverlay.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }
}
