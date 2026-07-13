using System;
using System.Threading.Tasks;
using System.Windows;

namespace OurPlanCore;

public partial class MainWindow
{
    private async Task RunAsyncUiHandler(Func<Task> action, string status, string title)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ReportAsyncUiHandlerError(ex, status, title);
        }
    }

    private void ReportAsyncUiHandlerError(Exception ex, string status, string title)
    {
        AppLog.Error(ex, status);
        TxtStatus.Text = status;
        MessageBox.Show($"{status}\n\n{ex.Message}", title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
