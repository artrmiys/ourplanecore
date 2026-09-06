using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OurPlanCore;
using SkiaSharp;

internal static class DataSafetyUiSmokeHarness
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
        if (!thread.Join(TimeSpan.FromMinutes(2)))
        {
            Console.WriteLine("Data safety UI smoke timed out.");
            Environment.Exit(2);
        }
        return result;
    }

    private static int RunUi()
    {
        string root = Path.Combine(Path.GetTempPath(), "opc-data-safety-ui", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("OURPLANCORE_SETTINGS_PATH", Path.Combine(root, "settings.json"));
        Environment.SetEnvironmentVariable("OURPLANCORE_PROFILE_ROOT", Path.Combine(root, "profile"));
        Environment.SetEnvironmentVariable(SmartContextStore.GlobalRootEnvironmentVariable, Path.Combine(root, "global"));
        OurPlanCoreJob job = OurPlanCoreJobStore.CreateJob(root, "Safety UI QA");
        string scope = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Building 1");
        string sibling = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Building 2");
        PageInfo a = OurPlanCoreJobStore.CreateBlankPage(job, "a1 plan", scope);
        PageInfo s = OurPlanCoreJobStore.CreateBlankPage(job, "s1 plan", scope);
        PageInfo other = OurPlanCoreJobStore.CreateBlankPage(job, "a2 plan", sibling);
        SourceInfo src = PageStore.ReadSource(a.FolderPath)!;
        src.LegendTakeoffOrder = ["wall-order"];
        src.LegendTakeoffOrderMode = "manual";
        src.AdditionalData = new() { ["future_field"] = JsonDocument.Parse("42").RootElement.Clone() };
        File.WriteAllText(Path.Combine(a.FolderPath, "source.json"), JsonSerializer.Serialize(src));
        TakeoffItem item = OurPlanCoreJobStore.CreateTakeoffItem(job, "Walls", "#FF0000");
        item.Measurements.Add(new Measurement { MType = "line", PageFolder = a.FolderPath, ScaleMetersPerPt = .01,
            Points = [new SKPoint(100, 100), new SKPoint(250, 200)] });
        OurPlanCoreJobStore.SaveTakeoffItem(item);
        string measurements = Path.Combine(item.FolderPath, "measurements.json");
        AppSettingsStore.Save(new AppSettings { LastJobPath = job.RootPath, LastPageFolder = a.FolderPath, JobsRootPath = root });
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("pack://application:,,,/ourplancore;component/Resources/AppResources.xaml") });
        typeof(App).GetProperty("StartupProjectPath", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, job.RootPath);
        var main = new MainWindow();
        app.MainWindow = main;
        int result = 2;
        var checks = new List<string>();
        main.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await Wait(() => Get<PageInfo?>(main, "_currentPage")?.FolderPath == a.FolderPath);
                byte[] before = File.ReadAllBytes(measurements);
                byte[] siblingBefore = File.ReadAllBytes(Path.Combine(other.FolderPath, "source.json"));
                Call(main, "SortPagesIntoArchStruct", scope);
                string movedA = Path.Combine(scope, "Arch", Path.GetFileName(a.FolderPath));
                string movedS = Path.Combine(scope, "Struct", Path.GetFileName(s.FolderPath));
                Check(Directory.Exists(movedA) && Directory.Exists(movedS), "Real A/S command moves the selected folder's pages");
                Check(siblingBefore.SequenceEqual(File.ReadAllBytes(Path.Combine(other.FolderPath, "source.json"))), "Sibling source remains byte-identical");
                Check(TakeoffStore.LoadMeasurements(item.FolderPath).Single().PageFolder == movedA, "Disk measurement link follows moved page");
                SourceInfo moved = PageStore.ReadSource(movedA)!;
                Check(moved.LegendTakeoffOrder.SequenceEqual(src.LegendTakeoffOrder) && moved.AdditionalData!["future_field"].GetInt32() == 42, "Legend and unknown fields survive actual UI move");
                checks.Add("Scoped A/S, sibling isolation, measurement links, legend and extension metadata passed.");
                Capture(main, Path.Combine(root, "sort-ui.png"));
                Call(main, "UndoLastPageOperation", "page-sort");
                await Wait(() => Get<PageInfo?>(main, "_currentPage") != null && Directory.Exists(a.FolderPath));
                Check(before.SequenceEqual(File.ReadAllBytes(measurements)), "UI reload does not overwrite restored measurements");
                Check(Directory.Exists(s.FolderPath) && !Directory.Exists(movedA), "UI undo restores original page layout");
                Check(PageStore.ReadSource(a.FolderPath)!.LegendTakeoffOrder.SequenceEqual(src.LegendTakeoffOrder), "Undo restores source metadata");
                checks.Add("Real Undo Last Page Sort restores original files and reopens without stale saves.");

                using (var locked = new FileStream(measurements, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    _ = TakeoffStore.ReadMeasurements(item.FolderPath);
                Check(!(bool)typeof(MainWindow).GetMethod("EnsureCurrentJobWritable", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(main, ["edit protected data", false])!, "Protected project rejects UI edits");
                var dialog = new ProjectDataRecoveryDialog(job.RootPath) { Owner = main };
                dialog.Show();
                await Task.Delay(150);
                Capture(dialog, Path.Combine(root, "recovery-ui.png"));
                Button retry = Descendants(dialog).OfType<Button>().Single(button => button.Content?.ToString() == "Retry reading");
                retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(dialog.Changed && !DataFileReader.IsProtected(job.RootPath), "Real recovery Retry validates and unlocks the file");
                Check(before.SequenceEqual(File.ReadAllBytes(measurements)), "Retry leaves original measurement bytes unchanged");
                dialog.Close();
                checks.Add("Recovery dialog shows protected file; Retry validates without altering measurement bytes.");
                result = 0;
            }
            catch (Exception ex) { checks.Add("FAILED: " + ex); }
            finally
            {
                File.WriteAllText(Path.Combine(root, "report.json"), JsonSerializer.Serialize(new { Passed = result == 0, Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine(root);
                foreach (string check in checks) Console.WriteLine(check);
                foreach (Window window in app.Windows.Cast<Window>().Where(window => window != main).ToList()) window.Close();
                main.Close(); app.Shutdown();
                Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }
        }, DispatcherPriority.ApplicationIdle);
        main.Show(); Dispatcher.Run();
        return result;
    }

    private static object? Call(object target, string method, string argument) => target.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic, [typeof(string)])!.Invoke(target, [argument]);
    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Check(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); }
    private static async Task Wait(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("UI readiness timed out."); await Task.Delay(100); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Capture(FrameworkElement element, string path)
    {
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); png.Save(file);
    }
}
