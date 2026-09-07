using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OurPlanCore;

internal static class PdfPreviewInteractionTests
{
    public static void PlainWheelZoomsAroundCursor()
    {
        WithPreview(preview =>
        {
            Canvas surface = Get<Canvas>(preview, "_surface");
            preview.ZoomWheelFactor = 1.6;
            double initialZoom = Get<double>(preview, "_zoom");
            Point cursor = Mouse.GetPosition(surface);
            Point documentPoint = Inverse(Matrix(preview)).Transform(cursor);
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120)
            {
                RoutedEvent = UIElement.PreviewMouseWheelEvent,
            };
            surface.RaiseEvent(wheel);
            Check(wheel.Handled, "plain mouse wheel must be consumed by preview zoom");
            Near(initialZoom * 1.6, Get<double>(preview, "_zoom"), "wheel uses the configured main-window factor");
            Near(cursor, Matrix(preview).Transform(documentPoint), "wheel keeps the document point under the cursor");
            var reverse = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
            {
                RoutedEvent = UIElement.PreviewMouseWheelEvent,
            };
            surface.RaiseEvent(reverse);
            Near(initialZoom, Get<double>(preview, "_zoom"), "opposite wheel direction restores the zoom");
        });
    }

    public static void ZoomPreservesAnchorAndRightDragPansFreely()
    {
        WithPreview(preview =>
        {
            var anchor = new Point(137, 219);
            Point documentPoint = Inverse(Matrix(preview)).Transform(anchor);
            UserFeedbackTests.Call(preview, "Zoom", 1.75, anchor);
            Near(anchor, Matrix(preview).Transform(documentPoint), "toolbar and wheel zoom keep their anchor stable");
            Vector before = Get<Vector>(preview, "_pan");
            Pan(preview, new Point(220, 180), new Point(119, 247));
            Near(before.X - 101, Get<Vector>(preview, "_pan").X, "right drag follows horizontal mouse distance");
            Near(before.Y + 67, Get<Vector>(preview, "_pan").Y, "right drag follows vertical mouse distance");
            Check(!Get<bool>(preview, "_fit") && !Get<bool>(preview, "_panning"), "drag exits fit and mouse-up ends panning");
            Canvas surface = Get<Canvas>(preview, "_surface");
            var down = RightButtonEvent(UIElement.PreviewMouseRightButtonDownEvent);
            surface.RaiseEvent(down);
            Check(down.Handled && surface.IsMouseCaptured && Get<bool>(preview, "_panning"),
                "right-button down starts captured pan through the routed mouse event");
            var up = RightButtonEvent(UIElement.PreviewMouseRightButtonUpEvent);
            surface.RaiseEvent(up);
            Check(up.Handled && !surface.IsMouseCaptured && !Get<bool>(preview, "_panning"),
                "right-button up ends pan and releases mouse capture");
            surface.RaiseEvent(RightButtonEvent(UIElement.PreviewMouseRightButtonDownEvent));
            surface.ReleaseMouseCapture();
            Check(!Get<bool>(preview, "_panning"), "losing mouse capture stops dragging");
            surface.RaiseEvent(RightButtonEvent(UIElement.PreviewMouseRightButtonDownEvent));
            var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(surface)!, Environment.TickCount, Key.Escape)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            };
            surface.RaiseEvent(escape);
            Check(escape.Handled && !surface.IsMouseCaptured && !Get<bool>(preview, "_panning"),
                "Escape ends captured pan without leaving mouse input trapped");
        });
    }

    public static void LiveFrameRefreshPreservesManualView()
    {
        WithPreview(preview =>
        {
            UserFeedbackTests.Call(preview, "Zoom", 2.0, new Point(320, 200));
            Pan(preview, new Point(100, 130), new Point(175, 85));
            Matrix expected = Matrix(preview);
            preview.SetUpdating();
            Check(!Get<Button>(preview, "_save").IsEnabled, "stale preview cannot be saved while updating");
            PdfExporter.PreviewFrame next = Frame(2);
            preview.SetFrame(next, current: false);
            Check(Matrix(preview) == expected, "intermediate frame must preserve current zoom and pan");
            Check(!Get<Button>(preview, "_save").IsEnabled, "intermediate frame is still stale");
            preview.SetFrame(next, current: true);
            Check(Matrix(preview) == expected, "current live frame must preserve current zoom and pan");
            Check(Get<Button>(preview, "_save").IsEnabled && ReferenceEquals(preview.PdfBytes, next.PdfBytes),
                "Save uses the newly completed PDF while preserving the view");
        });
    }

    public static void FitResetsPanAndAllowsDraggingSmallPage()
    {
        WithPreview(preview =>
        {
            Vector initial = Get<Vector>(preview, "_pan");
            Pan(preview, new Point(100, 100), new Point(700, -200));
            Near(initial.X + 600, Get<Vector>(preview, "_pan").X, "fit page can move beyond the viewport horizontally");
            Near(initial.Y - 300, Get<Vector>(preview, "_pan").Y, "fit page can move beyond the viewport vertically");
            Descendants(preview).OfType<Button>().Single(button => button.Content?.ToString() == "Fit")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Get<bool>(preview, "_fit"), "Fit restores automatic fitting");
            Canvas surface = Get<Canvas>(preview, "_surface");
            Image image = Get<Image>(preview, "_image");
            Rect pageBounds = new MatrixTransform(Matrix(preview)).TransformBounds(new Rect(0, 0, image.Source.Width, image.Source.Height));
            Near(surface.ActualWidth / 2, pageBounds.Left + pageBounds.Width / 2, "Fit centers page horizontally");
            Near(surface.ActualHeight / 2, pageBounds.Top + pageBounds.Height / 2, "Fit centers page vertically");
            Check(pageBounds.Left >= -0.01 && pageBounds.Top >= -0.01
                && pageBounds.Right <= surface.ActualWidth + 0.01 && pageBounds.Bottom <= surface.ActualHeight + 0.01,
                "Fit makes the entire page visible");
        });
    }

    internal static Matrix Matrix(PdfOutputPreviewWindow preview) => Get<MatrixTransform>(preview, "_transform").Matrix;

    internal static void Pan(PdfOutputPreviewWindow preview, Point from, Point to)
    {
        UserFeedbackTests.Call(preview, "BeginPan", from);
        try { UserFeedbackTests.Call(preview, "MovePan", to); }
        finally { UserFeedbackTests.Call(preview, "EndPan"); }
    }

    private static Matrix Inverse(Matrix matrix)
    {
        matrix.Invert();
        return matrix;
    }

    private static MouseButtonEventArgs RightButtonEvent(RoutedEvent routedEvent) =>
        new(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right) { RoutedEvent = routedEvent };

    private static PdfExporter.PreviewFrame Frame(byte version)
    {
        var bitmap = BitmapSource.Create(1200, 800, 96, 96, PixelFormats.Bgra32, null, new byte[1200 * 800 * 4], 1200 * 4);
        bitmap.Freeze();
        return new PdfExporter.PreviewFrame([version], bitmap, "");
    }

    private static void WithPreview(Action<PdfOutputPreviewWindow> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            PdfOutputPreviewWindow? preview = null;
            try
            {
                preview = new PdfOutputPreviewWindow("Interaction regression")
                {
                    Width = 800, Height = 600, Left = -10000, Top = -10000,
                    ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
                };
                preview.Show();
                preview.UpdateLayout();
                preview.SetFrame(Frame(1), current: true);
                action(preview);
            }
            catch (Exception ex) { failure = ex; }
            finally { preview?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new InvalidOperationException(failure.Message, failure);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child)) yield return descendant;
        }
    }

    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Check(bool condition, string message) => UserFeedbackTests.Check(condition, message);
    private static void Near(double expected, double actual, string message) =>
        Check(Math.Abs(expected - actual) < 0.001, $"{message}: expected {expected}, got {actual}");
    private static void Near(Point expected, Point actual, string message)
    {
        Near(expected.X, actual.X, message + " X");
        Near(expected.Y, actual.Y, message + " Y");
    }
}
