using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OurPlanCore;
using OurPlanCore.Controls;

internal static class ViewportRepaintSchedulingTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void VisualChangeAfterPaintBeforeQueueResetGetsTrailingFrame() => RunInWindow(async viewport =>
    {
        bool injected = false;
        int framesAtChange = 0;
        var queued = typeof(PdfViewport).GetField("_repaintQueued", PrivateInstance)!;
        DispatcherHookEventHandler hook = (_, _) =>
        {
            int frames = ViewportPerformanceRecorder.CapturePaintFrameCursor();
            if (injected || frames == 0 || !(bool)queued.GetValue(viewport)!)
                return;

            // Observe a real SKElement paint, then make an ordinary public UI
            // change while its queued Render-priority completion is pending.
            // The test never writes private scheduling or paint state.
            injected = true;
            framesAtChange = frames;
            viewport.ViewBackgroundColor = "#00AA00";
        };

        viewport.Dispatcher.Hooks.OperationCompleted += hook;
        try
        {
            viewport.ViewBackgroundColor = "#AA0000";
            await WaitForFramesAsync(2);
            Check(injected, "The dispatcher did not exercise paint-before-queue-reset ordering.");
            Check(ViewportPerformanceRecorder.CapturePaintFrameCursor() > framesAtChange,
                "A visual change after the first paint was lost while the queue reset was pending.");
        }
        finally { viewport.Dispatcher.Hooks.OperationCompleted -= hook; }
    });

    public static void RepaintBurstStaysCoalescedAndBecomesIdle() => RunInWindow(async viewport =>
    {
        for (int i = 0; i < 100; i++)
            viewport.ViewBackgroundColor = i % 2 == 0 ? "#AA0000" : "#00AA00";

        await WaitForFramesAsync(1);
        await Task.Delay(100);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        int frames = ViewportPerformanceRecorder.CapturePaintFrameCursor();
        Check(frames == 1, $"100 requests before painting produced {frames} frames instead of one consumed burst.");
        Check(!(bool)typeof(PdfViewport).GetField("_repaintQueued", PrivateInstance)!.GetValue(viewport)!,
            "Repaint queue must become idle after a bounded burst.");
    });

    public static void CrossThreadRepaintIsDispatchedAndPainted() => RunInWindow(async viewport =>
    {
        MethodInfo request = typeof(PdfViewport).GetMethod("RequestRepaint", PrivateInstance)!;
        await Task.Run(() => request.Invoke(viewport, [false]));
        await WaitForFramesAsync(1);
        Check(ViewportPerformanceRecorder.CapturePaintFrameCursor() >= 1,
            "A repaint request from a worker thread did not reach the WPF surface.");
    });

    public static void OffscreenSnapshotBetweenRequestsKeepsLatestColor() => RunInWindow(async viewport =>
    {
        viewport.PageBackgroundColor = "#AA0000";
        _ = SnapshotCenterPixel(viewport);
        viewport.PageBackgroundColor = "#00AA00";
        await WaitForFramesAsync(1);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        byte[] pixel = SnapshotCenterPixel(viewport);
        Check(pixel[0] == 0 && pixel[1] == 170 && pixel[2] == 0 && pixel[3] == 255,
            $"An offscreen snapshot between requests lost the latest green surface: BGRA={string.Join(',', pixel)}.");
    });

    private static byte[] SnapshotCenterPixel(PdfViewport viewport)
    {
        int width = Math.Max(1, (int)viewport.ActualWidth);
        int height = Math.Max(1, (int)viewport.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(viewport);
        byte[] pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(width / 2, height / 2, 1, 1), pixel, 4, 0);
        return pixel;
    }

    private static async Task WaitForFramesAsync(int minimum)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(2);
        while (ViewportPerformanceRecorder.CapturePaintFrameCursor() < minimum && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }

    private static void RunInWindow(Func<PdfViewport, Task> test)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var viewport = new PdfViewport();
                window = new Window
                {
                    Title = "Viewport repaint regression test", Width = 360, Height = 260,
                    Content = viewport, ShowInTaskbar = false,
                };
                window.Loaded += async (_, _) =>
                {
                    try
                    {
                        await Task.Delay(100);
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        ViewportPerformanceRecorder.BeginRun("isolated-wpf-ordering", "repaint-scheduling");
                        await test(viewport);
                    }
                    catch (Exception ex) { error = ex; }
                    finally
                    {
                        ViewportPerformanceRecorder.EndRun();
                        window.Close();
                        Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    }
                };
                window.Show();
                Dispatcher.Run();
            }
            catch (Exception ex) { error = ex; window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(15)))
            throw new TimeoutException("WPF repaint scheduling regression test timed out.");
        if (error != null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
