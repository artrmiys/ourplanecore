using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace OurPlanCore;

public partial class MainWindow
{
    private int _busyOverlayDepth;
    private Cursor? _busyOverlayPreviousCursor;

    private IDisposable ShowBusyOverlay(string message)
    {
        if (!Dispatcher.CheckAccess())
            return Dispatcher.Invoke(() => ShowBusyOverlay(message));

        _busyOverlayDepth++;
        BusyOverlayText.Text = string.IsNullOrWhiteSpace(message) ? "Working..." : message.Trim();

        if (_busyOverlayDepth == 1)
        {
            _busyOverlayPreviousCursor = Cursor;
            Cursor = Cursors.Wait;
            BusyOverlay.Visibility = Visibility.Visible;
        }

        return new BusyOverlayScope(this);
    }

    private async Task WaitForBusyOverlayRenderAsync()
    {
        await Dispatcher.Yield(DispatcherPriority.Render);
        await Task.Delay(25);
    }

    private void HideBusyOverlay()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(HideBusyOverlay);
            return;
        }

        if (_busyOverlayDepth > 0)
            _busyOverlayDepth--;

        if (_busyOverlayDepth != 0)
            return;

        BusyOverlay.Visibility = Visibility.Collapsed;
        Cursor = _busyOverlayPreviousCursor;
        _busyOverlayPreviousCursor = null;
    }

    private sealed class BusyOverlayScope : IDisposable
    {
        private MainWindow? _owner;

        public BusyOverlayScope(MainWindow owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            MainWindow? owner = _owner;
            if (owner == null)
                return;

            _owner = null;
            owner.HideBusyOverlay();
        }
    }
}
