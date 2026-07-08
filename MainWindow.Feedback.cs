using System;
using System.Windows.Threading;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void PostStatusInfo(string message) =>
        PostStatusMessage(message);

    private void PostStatusWarning(string message) =>
        PostStatusMessage($"Warning: {message}");

    private void PostStatusMessage(string message)
    {
        string clean = NormalizeStatusMessage(message);
        if (string.IsNullOrWhiteSpace(clean))
            return;

        if (Dispatcher.CheckAccess())
        {
            TxtStatus.Text = clean;
            return;
        }

        Dispatcher.BeginInvoke(new Action(() => TxtStatus.Text = clean), DispatcherPriority.Background);
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
