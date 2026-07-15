using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace OurPlanCore.Controls;

public enum JobLeaseConflictAction
{
    Cancel,
    OpenReadOnly,
    Retry,
    TakeOver,
}

public sealed record JobLeaseConflictDetails(
    string JobPath,
    string Owner,
    string MachineName,
    int? ProcessId,
    DateTimeOffset? ProcessStartedUtc,
    DateTimeOffset? HeartbeatUtc,
    string AppVersion,
    string InstanceId,
    bool IsStale);

public partial class JobLeaseConflictDialog : Window
{
    private readonly JobLeaseConflictDetails _details;

    public JobLeaseConflictAction SelectedAction { get; private set; } = JobLeaseConflictAction.Cancel;

    public JobLeaseConflictDialog(JobLeaseConflictDetails details)
    {
        _details = details ?? throw new ArgumentNullException(nameof(details));

        InitializeComponent();
        PopulateDetails();
    }

    private void PopulateDetails()
    {
        JobPathText.Text = Display(_details.JobPath);
        OwnerText.Text = Display(_details.Owner);
        MachineText.Text = Display(_details.MachineName);
        ProcessText.Text = _details.ProcessId is int processId
            ? $"PID {processId.ToString(CultureInfo.InvariantCulture)}"
            : "Unknown";
        ProcessStartedText.Text = FormatTimestamp(_details.ProcessStartedUtc);
        HeartbeatText.Text = FormatTimestamp(_details.HeartbeatUtc);
        VersionText.Text = Display(_details.AppVersion);
        InstanceIdText.Text = Display(_details.InstanceId);
        TakeOverButton.IsEnabled = _details.IsStale;
        TakeOverButton.ToolTip = _details.IsStale
            ? "Replace this stale lease after an additional confirmation"
            : "Take Over is disabled while the existing lease is active";
        TakeOverHintText.Text = _details.IsStale
            ? "Take Over replaces this stale lease."
            : "Take Over is available only after the lease becomes stale.";

        if (_details.IsStale)
        {
            LeaseStateText.Text = "STALE LEASE";
            LeaseStateBadge.Background = new SolidColorBrush(Color.FromRgb(45, 49, 42));
            LeaseStateBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(102, 121, 89));
            LeaseStateText.Foreground = new SolidColorBrush(Color.FromRgb(183, 205, 165));
        }
    }

    private static string Display(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();

    private static string FormatTimestamp(DateTimeOffset? utcTimestamp)
    {
        if (utcTimestamp is not DateTimeOffset timestamp)
            return "Unknown";

        return timestamp
            .ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
    }

    private void OpenReadOnlyButton_Click(object sender, RoutedEventArgs e) =>
        Complete(JobLeaseConflictAction.OpenReadOnly);

    private void RetryButton_Click(object sender, RoutedEventArgs e) =>
        Complete(JobLeaseConflictAction.Retry);

    private void TakeOverButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBoxResult confirmation = MessageBox.Show(
            this,
            "Take over this job?\n\n" +
            "The existing owner may still be running. Replacing its lease can cause concurrent writes " +
            "and job corruption if that instance continues to save.\n\n" +
            "Only continue when you are certain the other instance is no longer using this job.",
            "Confirm Risky Take Over",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirmation == MessageBoxResult.Yes)
            Complete(JobLeaseConflictAction.TakeOver);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedAction = JobLeaseConflictAction.Cancel;
        DialogResult = false;
    }

    private void Complete(JobLeaseConflictAction action)
    {
        SelectedAction = action;
        DialogResult = true;
    }
}
