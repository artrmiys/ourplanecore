using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OurPlanCore;
using OurPlanCore.Controls;
using SkiaSharp;

internal static class FeedbackUiSmokeHarness
{
    public static int Run()
    {
        int result = 2;
        var thread = new Thread(() =>
        {
            try { result = RunUi(); }
            catch (Exception ex) { Console.WriteLine(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }

    private static int RunUi()
    {
        string root = Path.Combine(Path.GetTempPath(), "onc-feedback-ui", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("OURPLANCORE_SETTINGS_PATH", Path.Combine(root, "settings.json"));
        Environment.SetEnvironmentVariable(SmartContextStore.GlobalRootEnvironmentVariable, Path.Combine(root, "global"));
        OurPlanCoreJob job = OurPlanCoreJobStore.CreateJob(root, "User feedback QA");
        PageInfo pageA = OurPlanCoreJobStore.CreateBlankPage(job, "Main sheet", job.PagesRoot);
        PageInfo pageB = OurPlanCoreJobStore.CreateBlankPage(job, "Detached sheet", job.PagesRoot);
        AppSettingsStore.Save(new AppSettings
        {
            LastJobPath = job.RootPath, LastPageFolder = pageA.FolderPath, JobsRootPath = root,
            PdfExportIncludeMeasurements = true, PdfExportIncludeAnnotations = true, PdfExportShowSheetLegend = true,
        });
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/ourplancore;component/Resources/AppResources.xaml"),
        });
        typeof(App).GetProperty("StartupProjectPath", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, job.RootPath);
        var main = new MainWindow();
        app.MainWindow = main;
        int result = 2;
        var checks = new List<string>();

        main.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await Wait(() => Get<OurPlanCoreJob?>(main, "_currentJob")?.RootPath == job.RootPath && Get<PageInfo?>(main, "_currentPage") != null);
                var viewport = Get<PdfViewport>(main, "_viewport");
                await Task.Delay(1200);
                UserFeedbackTests.Set(main, "_beamAnnotationConfig", new BeamAnnotationConfig { KeepLineAnnotation = true, DimensionOffsetPx = 28 });
                var settings = Get<AppSettings>(main, "_settings");
                var items = Get<List<TakeoffItem>>(main, "_takeoffItems");
                var detached = new DetachedSheetWindow(job, pageB, items, settings, UnitMode.Imperial) { Owner = main };
                UserFeedbackTests.Call(main, "ConfigureDetachedSheetWindow", detached, UnitMode.Imperial);
                Get<List<DetachedSheetWindow>>(main, "_detachedSheetWindows").Add(detached);
                detached.Closed += (_, _) => Get<List<DetachedSheetWindow>>(main, "_detachedSheetWindows").Remove(detached);
                detached.Show();
                detached.Viewport.ScaleMetersPerPt = 0.01;
                await Task.Delay(1000);

                CompleteTool(detached.Viewport, "AddBeamMeasurementPoint", new(100, 250), new(450, 250));
                Check(items.Count == 1 && items[0].Measurements.Count == 1, "detached Beam creates one Count");
                Check(items[0].Measurements[0].PageFolder == pageB.FolderPath, "Beam Count stays on detached sheet");
                PageAnnotation line = detached.Viewport.GetPageAnnotations().Single(a => a.Kind == "line");
                PageAnnotation ruler = detached.Viewport.GetPageAnnotations().Single(a => a.Kind == "dimension");
                Check(line.Points[0] == new SKPoint(100, 250), "Beam companion stays on measured points");
                Check(ruler.Points[0].Y != line.Points[0].Y, "Beam ruler is offset");
                checks.Add("Detached Beam dialog, Count page, companion and offset passed");

                CompleteTool(detached.Viewport, "AddOpeningMeasurementPoint", new(600, 250), new(750, 450));
                Check(items.Count == 2 && items[1].Measurements.Single().PageFolder == pageB.FolderPath, "detached Opening Count stays on its sheet");
                checks.Add("Detached Opening dialog and Count passed");
                await Task.Delay(300);
                Capture(detached, Path.Combine(root, "detached-tools.png"));

                viewport.ScaleMetersPerPt = 0.01;
                CompleteTool(viewport, "AddBeamMeasurementPoint", new(150, 350), new(450, 350));
                Check(items.Count == 3 && items[2].Measurements.Single().PageFolder == Get<PageInfo>(main, "_currentPage").FolderPath, "main Beam remains functional");
                checks.Add("Main Beam regression passed");

                await CheckJoistNoteUi(main, detached, job, items, root);
                checks.Add("Joists Properties checkbox, detached note drag, Undo and saved position passed");

                UserFeedbackTests.Set(main, "_detachedSheetNavigationTarget", detached);
                UserFeedbackTests.Call(main, "OpenPdfOutputPreview");
                var preview = Get<PdfOutputPreviewWindow>(main, "_pdfOutputPreview");
                await Wait(() => preview.PdfBytes != null);
                Check(preview.Title.Contains(pageB.Name), "preview opens the current detached sheet");
                byte[] before = SHA256.HashData(preview.PdfBytes!);
                var slider = Get<Slider>(main, "_sldOutputPdfLabel");
                slider.Value = 3.2;
                await Wait(() => preview.PdfBytes != null && !before.SequenceEqual(SHA256.HashData(preview.PdfBytes)));
                checks.Add("Nonmodal current-sheet preview reacts to real slider events");
                Capture(preview, Path.Combine(root, "live-preview.png"));
                File.WriteAllBytes(Path.Combine(root, "preview.pdf"), preview.PdfBytes!);

                var legendBox = Get<CheckBox>(main, "_chkOutputPdfLegend");
                before = SHA256.HashData(preview.PdfBytes!);
                legendBox.IsChecked = false;
                legendBox.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                await Wait(() => !before.SequenceEqual(SHA256.HashData(preview.PdfBytes!)));
                checks.Add("Preview reacts to legend checkbox");
                preview.Close();
                detached.Close();
                result = 0;
            }
            catch (Exception ex)
            {
                checks.Add("FAILED: " + ex);
            }
            finally
            {
                File.WriteAllText(Path.Combine(root, "report.json"), JsonSerializer.Serialize(new { Passed = result == 0, Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine(root);
                foreach (string check in checks) Console.WriteLine(check);
                foreach (Window window in app.Windows.Cast<Window>().Where(w => w != main).ToList()) window.Close();
                main.Close();
                app.Shutdown();
                Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }
        }, DispatcherPriority.ApplicationIdle);
        main.Show();
        Dispatcher.Run();
        return result;
    }

    private static void CompleteTool(PdfViewport viewport, string method, SKPoint start, SKPoint end)
    {
        bool accepted = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            NewItemDialog? dialog = Application.Current.Windows.OfType<NewItemDialog>().FirstOrDefault();
            if (dialog == null) return;
            Button ok = Descendants(dialog).OfType<Button>().Single(b => b.Content?.ToString() == "OK");
            timer.Stop();
            accepted = true;
            ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        timer.Start();
        try
        {
            UserFeedbackTests.Call(viewport, method, start);
            UserFeedbackTests.Call(viewport, method, end);
            Check(accepted, "tool must show its creation dialog");
        }
        finally { timer.Stop(); }
    }

    private static async Task CheckJoistNoteUi(MainWindow main, DetachedSheetWindow detached,
        OurPlanCoreJob job, List<TakeoffItem> items, string root)
    {
        TakeoffItem item = OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "TJI joists", "#23B789", "area");
        item.IsJoistTakeoff = true;
        item.JoistType = "TJI";
        Measurement area = UserFeedbackTests.Area();
        area.Points = [new(250, 600), new(750, 600), new(750, 1100), new(250, 1100)];
        area.PageFolder = detached.Page.FolderPath;
        area.TakeoffFolder = item.FolderPath;
        item.Measurements.Add(area);
        items.Add(item);
        UserFeedbackTests.Call(main, "AddTakeoffTreeItem", item);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        bool checkedBox = false;
        timer.Tick += (_, _) =>
        {
            Window? dialog = Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.Title == "Takeoff Item Properties");
            if (dialog == null) return;
            CheckBox box = Descendants(dialog).OfType<CheckBox>().Single(c => c.Content?.ToString() == "Move joist note");
            Check(box.IsChecked != true, "move note checkbox defaults off");
            box.IsChecked = true;
            checkedBox = true;
            timer.Stop();
            Descendants(dialog).OfType<Button>().Single(b => b.Content?.ToString() == "OK")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        timer.Start();
        try { UserFeedbackTests.Call(main, "EditViewportTakeoffProperties", item); }
        finally { timer.Stop(); }
        Check(checkedBox && item.JoistMoveNote && area.JoistMoveNote, "properties apply movement flag");
        SKRect boxBounds = (SKRect)UserFeedbackTests.Call(detached.Viewport, "JoistNoteBounds", area)!;
        SKPoint start = new(boxBounds.MidX, boxBounds.MidY);
        Check((bool)UserFeedbackTests.Call(detached.Viewport, "TryBeginJoistNoteDrag", start)!, "detached table can drag");
        UserFeedbackTests.Call(detached.Viewport, "UpdateJoistNoteDrag", start + new SKPoint(700, 0));
        UserFeedbackTests.Call(detached.Viewport, "FinishJoistNoteDrag", false);
        Check(Math.Abs(area.JoistNoteAnchor().X - start.X - 700) < 0.01, "detached drag follows the table center");
        detached.Viewport.UndoLast();
        Check(area.JoistNoteOffsetX == 0, "detached refresh preserves note Undo");
        Check((bool)UserFeedbackTests.Call(detached.Viewport, "TryBeginJoistNoteDrag", start)!, "second detached drag");
        UserFeedbackTests.Call(detached.Viewport, "UpdateJoistNoteDrag", start + new SKPoint(700, 0));
        UserFeedbackTests.Call(detached.Viewport, "FinishJoistNoteDrag", false);
        float storedOffset = area.JoistNoteOffsetX;
        OurPlanCoreJobStore.SaveTakeoffItem(item);
        OurPlanCoreJobStore.SaveMeasurements(item.FolderPath, item.Measurements);
        Check(OurPlanCoreJobStore.LoadMeasurements(item.FolderPath).Single().JoistNoteOffsetX == storedOffset, "saved note position");
        await Task.Delay(300);
        Capture(detached, Path.Combine(root, "joist-note-moved.png"));
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

    private static async Task Wait(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("UI smoke wait timed out.");
            await Task.Delay(100);
        }
    }

    private static void Capture(Window window, string path)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Check(bool condition, string message) => UserFeedbackTests.Check(condition, message);
}
