using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OurPlanCore;
using OurPlanCore.Controls;
using SkiaSharp;

internal static class RepeatDrawingTests
{
    public static void RepeatLineCreatesIndependentScaledSegments() => RunSta(() =>
    {
        var viewport = Viewport();
        var added = new List<Measurement>();
        viewport.MeasurementAdded += added.Add;
        viewport.SetTool("line", repeatDrawing: true);
        Click(viewport, new(10, 20), new(110, 20), new(250, 150), new(250, 300));
        Check(added.Count == 2, "two endpoint pairs create two measurements");
        Check(added.All(m => m.Points.Count == 2 && m.MType == "line"), "each repeated line is an independent segment");
        Check(added.All(m => m.PageFolder == "test-page" && m.TakeoffFolder == "test-takeoff" && m.ScaleMetersPerPt == 0.01), "repeat preserves page, target, and scale");
        Check(added[1].Points[0] == new SKPoint(250, 150), "next segment cannot connect to previous endpoint");
        Click(viewport, new(400, 400), new(400, 400));
        Check(added.Count == 2, "zero-length repeats do not add measurements");
        viewport.UndoLast();
        Check(Get<List<Measurement>>(viewport, "_measurements").Count == 1, "Undo removes only the last repeated segment");
        Check(viewport.IsRepeatDrawingActive, "Undo keeps repeat armed");
    });

    public static void RepeatStopsOnCancelToolChangeReadOnlyAndPageClose() => RunSta(() =>
    {
        var viewport = Viewport();
        int added = 0;
        viewport.MeasurementAdded += _ => added++;
        string? tool = null;
        viewport.ToolChanged += name => tool = name;
        viewport.SetTool("line", repeatDrawing: true);
        Click(viewport, new SKPoint(10, 20));
        Check(viewport.StopRepeatDrawing(), "repeat can stop with one unfinished endpoint");
        Check(!viewport.IsRepeatDrawingActive && tool == "select" && added == 0, "stop cancels unfinished geometry and publishes Select");
        viewport.SetTool("beam", repeatDrawing: true);
        viewport.SetTool("ruler");
        Check(!viewport.IsRepeatDrawingActive, "another tool disables repeat");
        viewport.SetTool("beam", repeatDrawing: true);
        viewport.IsReadOnlyMode = true;
        Check(!viewport.IsRepeatDrawingActive, "read-only ends repeat");
        viewport.SetTool("line", repeatDrawing: true);
        Check(!viewport.IsRepeatDrawingActive, "read-only cannot arm repeat");
        viewport.IsReadOnlyMode = false;
        viewport.SetTool("beam", repeatDrawing: true);
        viewport.ClearPage();
        Check(!viewport.IsRepeatDrawingActive, "closing page ends repeat");
    });

    public static void NormalLineKeepsPolylineCompletionAndScaleGate() => RunSta(() =>
    {
        var viewport = Viewport();
        var added = new List<Measurement>();
        viewport.MeasurementAdded += added.Add;
        viewport.SetTool("line");
        Click(viewport, new(10, 20), new(100, 20), new(100, 100));
        Check(added.Count == 0, "normal Line still waits for explicit completion");
        Call(viewport, "CompleteOrCancelDrawing");
        Check(added.Count == 1 && added[0].Points.Count == 3, "normal Line keeps all polyline vertices");
        viewport.ScaleMetersPerPt = 0;
        viewport.SetTool("line", repeatDrawing: true);
        Click(viewport, new(10, 20), new(100, 20));
        Check(added.Count == 1, "repeat must still require a page scale");
    });

    internal static void CheckUi(MainWindow main, DetachedSheetWindow detached, List<TakeoffItem> items)
    {
        PdfViewport mainViewport = Get<PdfViewport>(main, "_viewport");
        int before = items.Count;
        Get<RadioButton>(main, "BtnBeamRepeat").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(mainViewport.IsRepeatDrawingActive && detached.Viewport.IsRepeatDrawingActive, "Beam repeat button arms both surfaces");
        AcceptNewItem(() => Click(detached.Viewport, new(150, 500), new(550, 500)));
        AcceptNewItem(() => Click(detached.Viewport, new(150, 600), new(650, 600)));
        Check(items.Count == before + 2, "repeat Beam creates consecutive Count items");
        Check(items.Skip(before).All(i => i.Measurements.Single().PageFolder == detached.Page.FolderPath), "both repeated beams stay on detached page");
        Check(mainViewport.IsRepeatDrawingActive && detached.Viewport.IsRepeatDrawingActive, "Beam remains armed after both dialogs");
        Escape(detached.Viewport);
        Check(!mainViewport.IsRepeatDrawingActive && !detached.Viewport.IsRepeatDrawingActive && Get<RadioButton>(main, "BtnBeamRepeat").IsChecked != true, "Esc stops repeat and clears its button on every surface");

        Get<RadioButton>(main, "BtnBeamRepeat").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        int countBeforeCancel = items.Count;
        AcceptNewItem(() => Click(detached.Viewport, new(300, 650), new(700, 650)), accept: false);
        Check(items.Count == countBeforeCancel && !mainViewport.IsRepeatDrawingActive && !detached.Viewport.IsRepeatDrawingActive, "cancelling Beam dialog ends repeat without adding a Count item");

        Get<RadioButton>(main, "BtnBeam").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        AcceptNewItem(() => Click(mainViewport, new(300, 450), new(700, 450)));
        Check(!mainViewport.IsRepeatDrawingActive && Get<string>(main, "_activeTool") == "point", "normal Beam still returns to Count");

        AcceptNewItem(() => Get<RadioButton>(main, "BtnLineRepeat").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)));
        TakeoffItem line = Get<TakeoffItem>(main, "_activeItem");
        Click(detached.Viewport, new(150, 700), new(550, 700), new(150, 800), new(650, 800));
        Check(line.Measurements.Count == 2 && line.Measurements.All(m => m.Points.Count == 2 && m.PageFolder == detached.Page.FolderPath), "Line repeat button creates independent segments on detached sheet");
        Click(detached.Viewport, new SKPoint(150, 900));
        Escape(detached.Viewport);
        Check(line.Measurements.Count == 2 && !detached.Viewport.IsRepeatDrawingActive, "Esc drops unfinished segment without altering completed lines");

        Get<RadioButton>(main, "BtnBeamRepeat").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Call(main, "SetActiveTakeoffTarget", null, items[0], false);
        Check(!mainViewport.IsRepeatDrawingActive && !detached.Viewport.IsRepeatDrawingActive, "selecting a different takeoff stops repeat everywhere");
        Get<RadioButton>(main, "BtnBeamRepeat").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        PageInfo currentPage = Get<PageInfo>(main, "_currentPage");
        PageInfo detachedPage = detached.Page;
        OurPlanCoreJob job = Get<OurPlanCoreJob>(main, "_currentJob");
        AppSettings settings = Get<AppSettings>(main, "_settings");
        detached.ShowPage(job, currentPage, items, settings, UnitMode.Imperial);
        Check(!mainViewport.IsRepeatDrawingActive && !detached.Viewport.IsRepeatDrawingActive, "switching a page stops repeat everywhere");
        detached.ShowPage(job, detachedPage, items, settings, UnitMode.Imperial);
        detached.Viewport.ScaleMetersPerPt = 0.01;
    }

    private static PdfViewport Viewport()
    {
        var viewport = new PdfViewport { ScaleMetersPerPt = 0.01, ActiveTakeoffFolder = "test-takeoff" };
        UserFeedbackTests.Set(viewport, "_pageFolder", "test-page");
        return viewport;
    }

    private static void Click(PdfViewport viewport, params SKPoint[] points)
    {
        foreach (SKPoint point in points) Call(viewport, "HandleLeftClick", point);
    }

    private static void Escape(PdfViewport viewport)
    {
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(viewport), Environment.TickCount, Key.Escape)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        viewport.RaiseEvent(key);
        Check(key.Handled, "real Escape handler must consume the key");
    }

    private static void AcceptNewItem(Action action, bool accept = true)
    {
        bool accepted = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            NewItemDialog? dialog = Application.Current.Windows.OfType<NewItemDialog>().FirstOrDefault();
            if (dialog == null) return;
            timer.Stop();
            accepted = true;
            Button button = Descendants(dialog).OfType<Button>().Single(b => b.Content?.ToString() == (accept ? "OK" : "Cancel"));
            Call(button, "OnClick");
        };
        timer.Start();
        try { action(); Check(accepted, "expected New Item dialog"); }
        finally { timer.Stop(); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (DependencyObject nested in Descendants(child)) yield return nested;
        }
    }

    private static T Get<T>(object target, string field) =>
        (T)target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static object? Call(object target, string method, params object?[] args) => UserFeedbackTests.Call(target, method, args);
    private static void Check(bool condition, string message) => UserFeedbackTests.Check(condition, message);
    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new InvalidOperationException(failure.Message, failure);
    }
}
