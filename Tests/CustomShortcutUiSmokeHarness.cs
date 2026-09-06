using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OurPlanCore;
using OurPlanCore.Controls;
using SkiaSharp;

internal static partial class CustomShortcutUiSmokeHarness
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static int Run(string[] args)
    {
        if (args.Length < 2 || !File.Exists(args[1])) throw new ArgumentException("shortcut-ui-smoke requires a real .ourplan package.");
        string root = Path.Combine(Path.GetTempPath(), "opc-shortcuts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string copy = Path.Combine(root, "project.ourplan");
        File.Copy(args[1], copy);
        string originalHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[1])));
        Environment.SetEnvironmentVariable("OURPLANCORE_PROFILE_ROOT", Path.Combine(root, "profile"));
        Environment.SetEnvironmentVariable("OURPLANCORE_SETTINGS_PATH", Path.Combine(root, "settings.json"));
        Environment.SetEnvironmentVariable(SmartContextStore.GlobalRootEnvironmentVariable, Path.Combine(root, "global"));
        AppSettingsStore.Save(new AppSettings { LastJobPath = copy, JobsRootPath = root, AutoCleanRasterCacheOnClose = false, BackgroundJobWarmupEnabled = false });
        int result = 2;
        var thread = new Thread(() =>
        {
            try { result = RunUi(copy, root); }
            catch (Exception ex) { Console.WriteLine(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        Check(originalHash == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[1]))), "Original package hash changed.");
        Console.WriteLine(root);
        return result;
    }

    private static int RunUi(string copy, string root)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/ourplancore;component/Resources/AppResources.xaml") });
        typeof(App).GetProperty("StartupProjectPath", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, copy);
        var main = new MainWindow(); app.MainWindow = main;
        int result = 2;
        var checks = new List<string>();
        main.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await Wait(() => Get<OurPlanCoreJob?>(main, "_currentJob") != null && Get<PageInfo?>(main, "_currentPage") != null);
                OurPlanCoreJob job = Get<OurPlanCoreJob>(main, "_currentJob");
                List<TakeoffItem> items = Get<List<TakeoffItem>>(main, "_takeoffItems");
                Check(items.Sum(item => item.Measurements.Count) >= 500, "A real populated project is required.");
                Measurement[] candidates = items.SelectMany(item => item.Measurements).Where(measurement =>
                    measurement.Points.Count >= 3 && Directory.Exists(measurement.PageFolder)).ToArray();
                Measurement source = candidates.First();
                PageInfo page = OurPlanCoreJobStore.TryReadPage(source.PageFolder)!;
                Call(main, "OpenPageInActiveTab", page);
                await Wait(() => Get<PageInfo?>(main, "_currentPage")?.FolderPath == page.FolderPath);
                var viewport = Get<PdfViewport>(main, "_viewport");
                var before = Snapshot(items);

                Call(main, "SelectWorkspaceTab", "SettingsManager");
                await Wait(() => Get<ListBox?>(main, "_settingsCategoryList") != null);
                Get<ListBox>(main, "_settingsCategoryList").SelectedItem = "Keyboard Shortcuts";
                Click(main, "Open Keyboard Shortcuts...");
                var dialog = Get<KeyboardShortcutSettingsDialog>(main, "_keyboardShortcutDialog");
                await WaitForVisual(dialog);
                var catalog = Get<Dictionary<string, KeyboardCommandDefinition>>(main, "_keyboardCommands");
                Check(catalog.Count >= 180, "Catalog must include the full palette, controls and tree editing commands.");
                foreach (string id in new[] { "edit.mirrorHorizontal", "edit.mirrorVertical", "pages.copy", "takeoffs.copy", "pages.undoSort", "roof.ridge", "overlay.fineScaleUp" })
                    Check(catalog.ContainsKey(id), "Missing command: " + id);
                await SetBinding(dialog, catalog["edit.mirrorHorizontal"], Key.F10);
                await SetBinding(dialog, catalog["edit.mirrorVertical"], Key.F11);
                await SetBinding(dialog, catalog["edit.undo"], Key.F12);
                Click(dialog, "Save global default");
                Check(File.Exists(KeyboardShortcutStore.GlobalPath), "Save global button must persist the actual profile.");
                var persisted = KeyboardShortcutStore.Parse(File.ReadAllText(KeyboardShortcutStore.GlobalPath));
                foreach (var (id, key) in new[] { ("edit.mirrorHorizontal", "F10"), ("edit.mirrorVertical", "F11"), ("edit.undo", "F12") })
                    Check(persisted.Overrides.TryGetValue(id, out var keys) && keys.SequenceEqual(new[] { key }), "Capture/Assign/Save must persist " + id);
                await Capture(dialog, Path.Combine(root, "shortcut-editor.png"));
                File.WriteAllText(Path.Combine(root, "command-catalog.json"), JsonSerializer.Serialize(catalog.Values.OrderBy(command => command.Id), new JsonSerializerOptions { WriteIndented = true }));
                dialog.Close(); Call(main, "SelectWorkspaceTab", "MainView"); main.Activate();
                await WaitForVisual(viewport); viewport.Focus();

                viewport.SelectMeasurements([source]); SKPoint[] points = source.Points.ToArray();
                Press(viewport, Key.F10); Check(!points.SequenceEqual(source.Points), "F10 must horizontally mirror existing real geometry.");
                SKPoint[] mirrored = source.Points.ToArray();
                Press(viewport, Key.Back); Check(mirrored.SequenceEqual(source.Points), "Removed Backspace alias must not execute legacy Undo.");
                Press(viewport, Key.F12); Check(points.SequenceEqual(source.Points), "Assigned F12 Undo restores original geometry.");
                viewport.SelectMeasurements([source]); Press(viewport, Key.F11);
                Check(!points.SequenceEqual(source.Points), "F11 must vertically mirror existing real geometry.");
                Press(viewport, Key.F12); Check(points.SequenceEqual(source.Points), "Vertical mirror Undo restores real geometry.");
                checks.Add("Real settings dialog assigns F10/F11 mirroring and F12 Undo; original Backspace binding is suppressed.");

                var text = new TextBox();
                var typing = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(main), 0, Key.F10)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = text };
                bool consumed = (bool)Call(main, "HandleCustomKeyboardShortcut", main, typing)!;
                Check(!consumed, "Text controls must retain their keyboard input.");
                Press((TreeView)main.FindName("PagesTree"), Key.F10);
                Check(points.SequenceEqual(source.Points), "Viewport assignment must not transform geometry while a tree owns input.");
                checks.Add("Typing and Pages focus do not trigger viewport editing assignments.");
                CheckRowAndStaleSurfaceSafety(main);
                checks.Add("Row button closures cannot be bound or invoked as global shortcuts; catalog refresh discards stale control references.");

                Measurement other = candidates.First(measurement => measurement.PageFolder != source.PageFolder);
                PageInfo otherPage = OurPlanCoreJobStore.TryReadPage(other.PageFolder)!;
                var detached = new DetachedSheetWindow(job, otherPage, items, Get<AppSettings>(main, "_settings"), UnitMode.Imperial) { Owner = main };
                Call(main, "ConfigureDetachedSheetWindow", detached, UnitMode.Imperial);
                Get<List<DetachedSheetWindow>>(main, "_detachedSheetWindows").Add(detached);
                detached.Show(); await WaitForVisual(detached.Viewport);
                detached.Viewport.SelectMeasurements([other]); SKPoint[] otherBefore = other.Points.ToArray();
                Press(detached.Viewport, Key.F10);
                Check(!otherBefore.SequenceEqual(other.Points), "Detached hotkey must edit the focused detached sheet.");
                Check(points.SequenceEqual(source.Points), "Detached mirror must not touch main-sheet selection.");
                Press(detached.Viewport, Key.F12); Check(otherBefore.SequenceEqual(other.Points), "Detached Undo restores original geometry.");
                await Capture(detached, Path.Combine(root, "detached-after-shortcut-undo.png"));
                detached.Close();
                checks.Add("Same assigned keys edit and undo the focused detached sheet without changing the main sheet.");
                await CheckModalBoundary(main, source);
                checks.Add("A modal dialog executes only its own assigned control; hidden workspace shortcuts, typing, Enter and Esc remain outside the custom route.");
                await CheckVisibleSettingsRecovery(main, root);
                checks.Add("Locked global settings show a visible recovery message; the actual Recover settings -> Retry UI preserves bytes and reloads the assignments.");

                Call(main, "LoadCustomKeyboardShortcuts");
                Check(Get<KeyboardShortcutConfiguration>(main, "_customShortcuts").Overrides["edit.mirrorHorizontal"].Single() == "F10", "Reload must retain assigned keys.");
                viewport.IsReadOnlyMode = true; Press(viewport, Key.F10);
                Check(points.SequenceEqual(source.Points), "Read-only blocks assigned mirror.");
                viewport.IsReadOnlyMode = false;
                Check(Snapshot(items).OrderBy(pair => pair.Key).SequenceEqual(before.OrderBy(pair => pair.Key)), "Every existing measurement must remain unchanged after all Undo cases.");
                Call(main, "FlushPendingAutosave");
                await Task.Delay(500);
                Check(Snapshot(TakeoffStore.LoadTakeoffItems(job)).OrderBy(pair => pair.Key).SequenceEqual(before.OrderBy(pair => pair.Key)), "Saving and reloading must preserve every measurement.");
                checks.Add($"Persistence, read-only and all {before.Count} original measurements passed after Save/reload. Catalog: {catalog.Count} commands.");
                await Capture(main, Path.Combine(root, "real-main-after-shortcut-undo.png"));
                result = 0;
            }
            catch (Exception ex) { checks.Add("FAILED: " + ex); }
            finally
            {
                File.WriteAllText(Path.Combine(root, "report.json"), JsonSerializer.Serialize(new { Passed = result == 0, Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
                foreach (string check in checks) Console.WriteLine(check);
                foreach (Window window in app.Windows.Cast<Window>().Where(window => window != main).ToList()) window.Close();
                main.Close(); app.Shutdown(); Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }
        }, DispatcherPriority.ApplicationIdle);
        main.Show(); Dispatcher.Run(); return result;
    }

    private static Dictionary<string, string> Snapshot(IEnumerable<TakeoffItem> items) => items.SelectMany(item => item.Measurements.Select((measurement, index) =>
        new KeyValuePair<string, string>(item.FolderPath + "|" + index, JsonSerializer.Serialize(new
        { measurement.MType, measurement.PageFolder, measurement.ScaleMetersPerPt, Points = measurement.Points.Select(point => new[] { point.X, point.Y }),
          Holes = measurement.Holes.Select(hole => hole.Select(point => new[] { point.X, point.Y })), measurement.JoistDirectionDegrees }))))
        .ToDictionary(pair => pair.Key, pair => pair.Value);
    private static async Task SetBinding(KeyboardShortcutSettingsDialog dialog, KeyboardCommandDefinition command, Key key)
    {
        dialog.SelectPickedCommand(command);
        await WaitForVisual(dialog);
        Check(Get<DataGrid>(dialog, "_grid").SelectedItem != null, "Selected command must be available in the real settings grid.");
        var capture = Get<TextBox>(dialog, "_gesture"); capture.Focus(); Press(capture, key);
        Check(capture.Text == key.ToString(), "The visible capture box must receive the pressed shortcut.");
        Click(dialog, "Assign");
    }
    private static void Click(Window window, string content) => Logical(window).OfType<Button>().Single(button => button.Content?.ToString() == content)
        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static IEnumerable<DependencyObject> Logical(DependencyObject root)
    { yield return root; foreach (object child in LogicalTreeHelper.GetChildren(root)) if (child is DependencyObject element) foreach (var nested in Logical(element)) yield return nested; }
    private static void Press(UIElement target, Key key)
    {
        PresentationSource source = PresentationSource.FromVisual(target) ?? throw new InvalidOperationException("Keyboard target is not attached to a visible presentation source: " + target.GetType().Name);
        var preview = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(preview);
        if (!preview.Handled) target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
            { RoutedEvent = Keyboard.KeyDownEvent });
    }
    private static object? Call(object target, string method, params object?[] arguments) => target.GetType().GetMethods(Private)
        .Single(candidate => candidate.Name == method && candidate.GetParameters().Length == arguments.Length).Invoke(target, arguments);
    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private)!.SetValue(target, value);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Wait(Func<bool> ready)
    { DateTime deadline = DateTime.UtcNow.AddMinutes(3); while (!ready()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Project load timed out."); await Task.Delay(100); } }
    private static async Task WaitForVisual(FrameworkElement element)
    {
        await Wait(() => element.IsVisible && PresentationSource.FromVisual(element) != null);
        element.UpdateLayout();
        await element.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }
    private static async Task Capture(FrameworkElement element, string path)
    {
        await WaitForVisual(element);
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)element.ActualWidth), Math.Max(1, (int)element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); png.Save(file);
    }
}
