using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace OurPlaneCore;

public partial class MainWindow
{
    private enum FeedbackSeverity
    {
        Info,
        Warning,
        Error,
    }

    private DispatcherTimer? _feedbackToastTimer;

    private void PostStatusInfo(string message) =>
        PostStatusMessage(message, FeedbackSeverity.Info);

    private void PostStatusWarning(string message) =>
        PostStatusMessage($"Warning: {message}", FeedbackSeverity.Warning);

    private void PostStatusError(string message) =>
        PostStatusMessage($"Error: {message}", FeedbackSeverity.Error);

    private void PostStatusMessage(string message, FeedbackSeverity severity)
    {
        string clean = NormalizeStatusMessage(message);
        if (string.IsNullOrWhiteSpace(clean))
            return;

        if (Dispatcher.CheckAccess())
        {
            PostStatusMessageOnUi(clean, severity);
            return;
        }

        Dispatcher.BeginInvoke(new Action(() => PostStatusMessageOnUi(clean, severity)), DispatcherPriority.Background);
    }

    private void PostStatusMessageOnUi(string message, FeedbackSeverity severity)
    {
        TxtStatus.Text = message;
        ShowFeedbackToast(message, severity);
    }

    private void ShowFeedbackToast(string message, FeedbackSeverity severity)
    {
        if (!IsInitialized || FeedbackToast == null)
            return;

        Brush severityBrush = FeedbackSeverityBrush(severity);
        FeedbackToast.BorderBrush = severityBrush;
        FeedbackToastSeverityBar.Background = severityBrush;
        FeedbackToastText.Text = message;
        FeedbackToast.Visibility = Visibility.Visible;

        EnsureFeedbackToastTimer();
        _feedbackToastTimer!.Stop();
        _feedbackToastTimer.Interval = severity == FeedbackSeverity.Error
            ? TimeSpan.FromSeconds(7)
            : TimeSpan.FromSeconds(4);
        _feedbackToastTimer.Start();
    }

    private void EnsureFeedbackToastTimer()
    {
        if (_feedbackToastTimer != null)
            return;

        _feedbackToastTimer = new DispatcherTimer();
        _feedbackToastTimer.Tick += (_, _) =>
        {
            _feedbackToastTimer.Stop();
            if (FeedbackToast != null)
                FeedbackToast.Visibility = Visibility.Collapsed;
        };
    }

    private Brush FeedbackSeverityBrush(FeedbackSeverity severity)
    {
        return severity switch
        {
            FeedbackSeverity.Warning => new SolidColorBrush(Color.FromRgb(217, 119, 6)),
            FeedbackSeverity.Error => new SolidColorBrush(Color.FromRgb(220, 38, 38)),
            _ => TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue,
        };
    }

    private static string NormalizeStatusMessage(string message)
    {
        string clean = (message ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        while (clean.Contains("  ", StringComparison.Ordinal))
            clean = clean.Replace("  ", " ", StringComparison.Ordinal);

        return clean;
    }
}
