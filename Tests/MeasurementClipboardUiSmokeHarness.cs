using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OurPlanCore;
using OurPlanCore.Controls;
using SkiaSharp;

internal static class MeasurementClipboardUiSmokeHarness
{
    private static readonly List<string> Checks = [];
    private static readonly List<string> Failures = [];
    private static string _root = "";

    public static int Run(string[] args)
    {
        if (args.Length < 2 || !File.Exists(args[1]) || !args[1].EndsWith(".ourplan", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("clipboard-ui-smoke requires an existing real .ourplan package; it creates its own disposable copy.");
        bool baseline = args.Contains("--baseline");
        int verifyIndex = Array.IndexOf(args, "--verify");
        string? verification = verifyIndex >= 0 ? args[verifyIndex + 1] : null;
        _root = Path.Combine(Path.GetTempPath(), "opc-clipboard", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        string package = Path.Combine(_root, "project.ourplan");
        File.Copy(args[1], package);
        Environment.SetEnvironmentVariable("OURPLANCORE_PROFILE_ROOT", Path.Combine(_root, "profile"));
        Environment.SetEnvironmentVariable("OURPLANCORE_SETTINGS_PATH", Path.Combine(_root, "settings.json"));
        Environment.SetEnvironmentVariable(SmartContextStore.GlobalRootEnvironmentVariable, Path.Combine(_root, "global"));
        AppSettingsStore.Save(new AppSettings
        {
            LastJobPath = package, JobsRootPath = _root,
            AutoCleanRasterCacheOnClose = false, BackgroundJobWarmupEnabled = false,
        });
        int exitCode = 2;
        var thread = new Thread(() =>
        {
            try { exitCode = RunUi(package, baseline, verification); }
            catch (Exception ex) { Console.WriteLine(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        return exitCode;
    }

    private static int RunUi(string package, bool baseline, string? verification)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/ourplancore;component/Resources/AppResources.xaml"),
        });
        typeof(App).GetProperty("StartupProjectPath", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, package);
        var main = new MainWindow(); app.MainWindow = main;
        int result = 2;
        main.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await Wait(() => Get<OurPlanCoreJob?>(main, "_currentJob") != null && Get<PageInfo?>(main, "_currentPage") != null);
                OurPlanCoreJob job = Get<OurPlanCoreJob>(main, "_currentJob");
                var items = Get<List<TakeoffItem>>(main, "_takeoffItems");
                var original = Snapshot(items);
                Check(original.Count >= 500, "This smoke requires a real project with at least 500 measurements.");
                if (verification != null)
                {
                    var expected = JsonSerializer.Deserialize<ReopenExpectation>(File.ReadAllText(verification))!;
                    Check(items.Count == expected.TakeoffCount, "Restart preserved exact takeoff count; no empty pasted items reappeared");
                    CheckSnapshots(expected.Measurements, PortableSnapshot(job, original), "New process package reopen preserves every measurement");
                    Check(Get<List<Measurement>>(Get<PdfViewport>(main, "_viewport"), "_measurements").Count == original.Count, "Reopened viewport render model matches saved measurements");
                    Record($"New process reopened saved .ourplan: {items.Count} takeoffs and {original.Count} measurements exactly preserved.");
                    result = 0;
                    return;
                }
                var pages = items.SelectMany(i => i.Measurements).Select(m => m.PageFolder)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists)
                    .Select(OurPlanCoreJobStore.TryReadPage).OfType<PageInfo>().Take(2).ToArray();
                Check(pages.Length == 2, "Two real measured sheets are required.");
                Call(main, "OpenPageInActiveTab", pages[0]);
                await Wait(() => Get<PageInfo?>(main, "_currentPage")?.FolderPath == pages[0].FolderPath);
                await WaitStorage();
                var viewport = Get<PdfViewport>(main, "_viewport");
                var settings = Get<AppSettings>(main, "_settings");
                var detached = new DetachedSheetWindow(job, pages[1], items, settings, UnitMode.Imperial) { Owner = main };
                Call(main, "ConfigureDetachedSheetWindow", detached, UnitMode.Imperial);
                Get<List<DetachedSheetWindow>>(main, "_detachedSheetWindows").Add(detached);
                detached.Show(); await Task.Delay(800);

                foreach (bool useDetached in new[] { false, true })
                foreach (bool newTakeoffs in new[] { false, true })
                foreach (int count in new[] { 1, 3 })
                {
                    PdfViewport target = useDetached ? detached.Viewport : viewport;
                    PageInfo page = useDetached ? pages[1] : pages[0];
                    await CheckPaste(main, target, page, count, newTakeoffs, baseline);
                }
                Record($"Real project: {items.Count} takeoffs; {original.Count} existing measurements; main and detached sheets {pages[0].Name}, {pages[1].Name}.");
                await CheckCancel(main, viewport, pages[0]);
                if (!baseline)
                {
                    CheckDialogKeyboard(main, pages[0].Name);
                    CheckDialogKeyboard(detached, pages[1].Name);
                    await CheckScaleRules(main, viewport, pages[0]);
                    await CheckBeforeCommitFailure(main, detached.Viewport, pages[1]);
                    await CheckEditedTargetSurvives(main, viewport, pages[0]);
                }
                CheckSnapshots(original, Snapshot(items), "All pre-existing measurements after all paste/Undo cases");
                Call(main, "FlushPendingAutosave"); await WaitStorage();
                CheckSnapshots(original, Snapshot(TakeoffStore.LoadTakeoffItems(job)), "Undo -> Save -> disk reopen");
                Call(main, "LoadTakeoffsForJob");
                CheckSnapshots(original, Snapshot(Get<List<TakeoffItem>>(main, "_takeoffItems")), "Reopened main-window model");
                Record("Undo -> Save -> reload through production TakeoffStore and real main-window load preserves every measurement and quantity.");
                Capture(main, Path.Combine(_root, "real-main-after-undo.png"));
                Capture(detached, Path.Combine(_root, "real-detached-after-undo.png"));
                await CheckReadOnly(main, viewport, pages[0], baseline);
                detached.Close();
                result = Failures.Count == 0 ? 0 : 2;
                File.WriteAllText(Path.Combine(_root, "expected-measurements.json"), JsonSerializer.Serialize(new ReopenExpectation(items.Count, PortableSnapshot(job, original))));
            }
            catch (Exception ex) { Failures.Add(ex.ToString()); }
            finally
            {
                File.WriteAllText(Path.Combine(_root, "report.json"), JsonSerializer.Serialize(new
                {
                    Passed = result == 0, Baseline = baseline, Package = package, Checks, Failures,
                    Assembly = typeof(MainWindow).Assembly.GetName().Version?.ToString(),
                }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine(_root);
                foreach (string line in Failures) Console.WriteLine("FAIL " + line);
                foreach (Window window in app.Windows.Cast<Window>().Where(w => w != main).ToList()) window.Close();
                main.Close(); app.Shutdown();
                Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }
        }, DispatcherPriority.ApplicationIdle);
        main.Show(); Dispatcher.Run();
        return result;
    }

    private static async Task CheckPaste(MainWindow main, PdfViewport viewport, PageInfo page, int count, bool newTakeoffs, bool baseline)
    {
        var items = Get<List<TakeoffItem>>(main, "_takeoffItems");
        var before = Snapshot(items);
        var originalFolders = items.Select(i => i.FolderPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Measurement[] copied = SelectSources(items, count);
        Call(main, "CopyMeasurementsToClipboard", (object)copied);
        SKPoint anchor = new(130, 175);
        SKPoint[] allPoints = copied.SelectMany(m => m.Points.Concat(m.Holes.SelectMany(h => h))
            .Concat(m.ExtraJoists.SelectMany(j => new[] { j.Start, j.End }))).ToArray();
        SKPoint offset = anchor - new SKPoint(allPoints.Min(p => p.X), allPoints.Min(p => p.Y));
        using (var prompt = new PromptResponder(newTakeoffs ? "New takeoffs" : "Same takeoffs", capture: !baseline))
            Call(main, "PasteMeasurementsFromClipboardInto", viewport, page, anchor);
        var added = items.SelectMany(i => i.Measurements).Where(m => !before.ContainsKey(m.Id)).ToArray();
        Check(added.Length == count, "Paste count");
        for (int i = 0; i < copied.Length; i++)
        {
            Measurement source = copied[i], pasted = added[i];
            Check(pasted.PageFolder == page.FolderPath, "Paste stays on requested viewport sheet");
            Check(pasted.Name == source.Name && pasted.Notes == source.Notes, "Exact measurement name and notes");
            Check(pasted.Points.SequenceEqual(source.Points.Select(p => p + offset)), "Top-left cursor anchor preserves geometry");
            Check(pasted.Holes.Count == source.Holes.Count && pasted.ExtraJoists.Count == source.ExtraJoists.Count, "Holes and extra joists survive");
            Check(Math.Abs(pasted.ScaleMetersPerPt - (page.ScaleMetersPerPt > 0 ? page.ScaleMetersPerPt : source.ScaleMetersPerPt)) < 1e-9, "Existing scale policy");
            TakeoffItem sourceItem = items.Single(item => item.Measurements.Contains(source));
            TakeoffItem targetItem = items.Single(item => item.Measurements.Contains(pasted));
            Check(targetItem.Name == sourceItem.Name && targetItem.UnitPrice == sourceItem.UnitPrice && targetItem.Notes == sourceItem.Notes, "Exact visible takeoff name, price and notes");
            Check(newTakeoffs ? targetItem != sourceItem : targetItem == sourceItem, "Selected destination mode");
            Check(Get<List<Measurement>>(viewport, "_measurements").Contains(pasted), "Destination viewport received pasted model");
            Check(Get<List<Measurement>>(Get<PdfViewport>(main, "_viewport"), "_measurements").Contains(pasted), "Main viewport remains synchronized");
        }
        Call(main, "FlushPendingAutosave"); await WaitStorage();
        CheckSnapshots(Snapshot(items), Snapshot(TakeoffStore.LoadTakeoffItems(Get<OurPlanCoreJob>(main, "_currentJob"))), "Paste Save -> Reopen");
        viewport.UndoLast();
        Call(main, "FlushPendingAutosave"); await WaitStorage();
        CheckSnapshots(before, Snapshot(items), "Undo returns original measurements");
        foreach (PdfViewport shown in Get<List<DetachedSheetWindow>>(main, "_detachedSheetWindows").Select(w => w.Viewport)
                     .Append(Get<PdfViewport>(main, "_viewport")))
        {
            if (Get<List<Measurement>>(shown, "_measurements").Any(added.Contains))
                Failures.Add($"Undo {(newTakeoffs ? "new" : "same")} takeoffs leaves pasted measurements in another viewport's render model.");
        }
        var leftovers = items.Where(item => !originalFolders.Contains(item.FolderPath)).ToArray();
        if (leftovers.Length > 0)
        {
            string message = $"Undo of new-takeoff paste left {leftovers.Length} empty takeoff item(s).";
            if (baseline) Failures.Add(message);
            else Check(false, message);
        }
        Record($"Paste + autosave + Undo: {(ReferenceEquals(viewport, Get<PdfViewport>(main, "_viewport")) ? "main" : "detached")}, {(newTakeoffs ? "new" : "same")} takeoffs, {count} measurement(s).");
    }

    private static async Task CheckCancel(MainWindow main, PdfViewport viewport, PageInfo page)
    {
        var items = Get<List<TakeoffItem>>(main, "_takeoffItems");
        var before = Snapshot(items); int folders = items.Count;
        Call(main, "CopyMeasurementsToClipboard", (object)SelectSources(items, 3));
        using (var prompt = new PromptResponder("Cancel"))
            Call(main, "PasteMeasurementsFromClipboardInto", viewport, page, new SKPoint(500, 500));
        CheckSnapshots(before, Snapshot(items), "Cancel leaves model unchanged");
        Check(items.Count == folders, "Cancel creates no takeoffs");
        await WaitStorage(); Record("Cancel changes neither measurements nor takeoff folders.");
    }

    private static async Task CheckBeforeCommitFailure(MainWindow main, PdfViewport viewport, PageInfo page)
    {
        var items = Get<List<TakeoffItem>>(main, "_takeoffItems");
        var before = Snapshot(items); int folders = items.Count;
        PropertyInfo? injection = typeof(MainWindow).GetProperty("BeforeMeasurementPasteCommitForTests", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(injection != null, "A failure can be injected at the actual before-commit boundary");
        Call(main, "CopyMeasurementsToClipboard", (object)SelectSources(items, 3));
        bool reached = false;
        injection!.SetValue(main, (Action)(() => { reached = true; throw new IOException("Injected clipboard interruption"); }));
        try
        {
            using var prompt = new PromptResponder("New takeoffs");
            Call(main, "PasteMeasurementsFromClipboardInto", viewport, page, new SKPoint(500, 500));
        }
        finally { injection.SetValue(main, null); }
        Check(reached, "The failure occurred after creating measurements, before commit");
        CheckSnapshots(before, Snapshot(items), "Pre-commit failure rolls model back");
        Check(items.Count == folders, "Pre-commit failure removes provisional takeoffs");
        CheckSnapshots(before, Snapshot(TakeoffStore.LoadTakeoffItems(Get<OurPlanCoreJob>(main, "_currentJob"))), "Pre-commit failure preserves disk model");
        await WaitStorage(); Record("Injected before-commit failure rolls back model, folders and viewport without duplicating Undo.");
    }

    private static async Task CheckReadOnly(MainWindow main, PdfViewport viewport, PageInfo page, bool baseline)
    {
        var items = Get<List<TakeoffItem>>(main, "_takeoffItems");
        var before = Snapshot(items);
        Call(main, "CopyMeasurementsToClipboard", (object)SelectSources(items, 1));
        int undoBefore = Get<System.Collections.ICollection>(viewport, "_undoStack").Count;
        OurPlanCoreJob job = Get<OurPlanCoreJob>(main, "_currentJob");
        JobAccessSessionToken access = Get<JobAccessSessionToken>(main, "_currentJobAccessToken");
        if (!baseline) JobWriteAccess.SetMode(access, JobAccessMode.ReadOnly);
        Set(main, "_currentJobAccessMode", JobAccessMode.ReadOnly);
        try
        {
            using var prompt = new PromptResponder("Same takeoffs");
            Call(main, "PasteMeasurementsFromClipboardInto", viewport, page, new SKPoint(400, 400));
        }
        finally
        {
            if (!baseline)
            {
                JobWriteAccess.Close(access);
                Set(main, "_currentJobAccessToken", JobWriteAccess.RegisterJob(job.RootPath, JobAccessMode.Writable));
            }
            Set(main, "_currentJobAccessMode", JobAccessMode.Writable);
        }
        if (Snapshot(items).Count != before.Count)
        {
            if (!baseline) Check(false, "Read-only common paste changed the model");
            Failures.Add("REPRODUCED: common paste modifies measurements and Undo while main window reports read-only.");
            viewport.UndoLast(); Call(main, "FlushPendingAutosave");
        }
        else
        {
            Check(Get<System.Collections.ICollection>(viewport, "_undoStack").Count == undoBefore, "Read-only does not create an Undo action");
            Record("Registered read-only common paste rejects before prompt/model/Undo mutation.");
        }
        CheckSnapshots(before, Snapshot(items), "Read-only final model");
        await WaitStorage();
    }

    private static async Task CheckEditedTargetSurvives(MainWindow main, PdfViewport viewport, PageInfo page)
    {
        var items = Get<List<TakeoffItem>>(main, "_takeoffItems");
        var before = items.ToHashSet();
        Call(main, "CopyMeasurementsToClipboard", (object)SelectSources(items, 1));
        using (var prompt = new PromptResponder("New takeoffs"))
            Call(main, "PasteMeasurementsFromClipboardInto", viewport, page, new SKPoint(650, 650));
        TakeoffItem edited = items.Single(item => !before.Contains(item));
        edited.Notes = "Manual edit after paste must survive Undo.";
        viewport.UndoLast(); Call(main, "FlushPendingAutosave"); await WaitStorage();
        Check(items.Contains(edited) && edited.Measurements.Count == 0 && Directory.Exists(edited.FolderPath), "Undo retains later-edited new takeoff");
        Check(TakeoffStore.TryReadTakeoffItem(edited.FolderPath)?.Notes == edited.Notes, "Later edit remains saved");
        // Dispose only this test-created object after verifying the conflict guard.
        object? error = Call(main, "MoveUncommittedTakeoffFoldersToRecovery", (object)new[] { edited });
        Check(error == null, "Disposable conflict-test item moved to recovery");
        items.Remove(edited); Call(main, "LoadTakeoffsForJob");
        Record("Undo preserves later user edits in a newly pasted takeoff; it never deletes that edited item.");
    }

    private static async Task CheckScaleRules(MainWindow main, PdfViewport viewport, PageInfo page)
    {
        var items = Get<List<TakeoffItem>>(main, "_takeoffItems");
        var before = Snapshot(items);
        Measurement source = SelectSources(items, 1).Single();
        double pageScale = page.ScaleMetersPerPt, sourceScale = source.ScaleMetersPerPt;
        Check(sourceScale > 0, "Real scaled geometry is required for the clipboard scale regression");
        try
        {
            foreach (double destinationScale in new[] { sourceScale * 2, 0 })
            {
                page.ScaleMetersPerPt = destinationScale;
                Call(main, "CopyMeasurementsToClipboard", (object)new[] { source });
                using (var prompt = new PromptResponder("Same takeoffs"))
                    Call(main, "PasteMeasurementsFromClipboardInto", viewport, page, null);
                Measurement pasted = items.SelectMany(i => i.Measurements).Single(m => !before.ContainsKey(m.Id));
                Check(pasted.ScaleMetersPerPt == (destinationScale > 0 ? destinationScale : sourceScale), "Target scale wins; otherwise accepted saved source scale is reused");
                Check(pasted.Points.SequenceEqual(source.Points), "Paste without a cursor keeps the original coordinates");
                viewport.UndoLast(); Call(main, "FlushPendingAutosave"); await WaitStorage();
            }
            page.ScaleMetersPerPt = 0;
            source.ScaleMetersPerPt = 0;
            Call(main, "CopyMeasurementsToClipboard", (object)new[] { source });
            source.ScaleMetersPerPt = sourceScale;
            using (var prompt = new PromptResponder("Same takeoffs"))
                Call(main, "PasteMeasurementsFromClipboardInto", viewport, page, null);
            CheckSnapshots(before, Snapshot(items), "Missing sheet and clipboard scale rejects without changing real measurements");
            Record("Existing scale rules: destination scale, explicitly accepted source scale, missing-scale rejection, and coordinate-preserving paste.");
        }
        finally { page.ScaleMetersPerPt = pageScale; source.ScaleMetersPerPt = sourceScale; }
    }

    private static void CheckDialogKeyboard(Window owner, string pageName)
    {
        foreach (string action in new[] { "EnterSame", "EnterNew", "Escape" })
        {
            var dialog = new MeasurementPasteModeDialog(3, pageName) { Owner = owner };
            Exception? failure = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timeout.Tick += (_, _) => { failure = new TimeoutException("Paste dialog key was not handled: " + action); dialog.Close(); };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try
                {
                    Button same = Descendants(dialog).OfType<Button>().Single(b => b.Content?.ToString() == "Same takeoffs");
                    Button fresh = Descendants(dialog).OfType<Button>().Single(b => b.Content?.ToString() == "New takeoffs");
                    Button cancel = Descendants(dialog).OfType<Button>().Single(b => b.Content?.ToString() == "Cancel");
                    Check(same.IsKeyboardFocused && same.IsDefault && cancel.IsCancel, "Initial focus and default/cancel actions");
                    if (action == "EnterNew") { fresh.Focus(); Keyboard.Focus(fresh); }
                    int key = action == "Escape" ? 0x1B : 0x0D;
                    IntPtr handle = new WindowInteropHelper(dialog).Handle;
                    PostMessage(handle, 0x0100, (IntPtr)key, IntPtr.Zero);
                    PostMessage(handle, 0x0101, (IntPtr)key, IntPtr.Zero);
                }
                catch (Exception ex) { failure = ex; dialog.Close(); }
            };
            timer.Start(); timeout.Start();
            bool? accepted;
            try { accepted = dialog.ShowDialog(); }
            finally { timer.Stop(); timeout.Stop(); }
            if (failure != null) throw failure;
            Check(dialog.Owner == owner, "Paste dialog belongs to requesting window");
            Check(action == "Escape" ? accepted != true : accepted == true && dialog.CreateNewTakeoffs == (action == "EnterNew"), "Enter/Escape action: " + action);
        }
        Record($"Explicit paste dialog: default focus, Enter for both actions, Escape and correct {(owner is MainWindow ? "main" : "detached")} owner.");
    }

    private static Measurement[] SelectSources(List<TakeoffItem> items, int count) => items
        .Where(item => item.Measurements.Count > 0)
        .OrderBy(item => item.MeasurementType == "area" ? 0 : item.MeasurementType == "line" ? 1 : 2)
        .Take(count).Select(item => item.Measurements[0]).ToArray();

    private static Dictionary<string, string> Snapshot(IEnumerable<TakeoffItem> items) => items.SelectMany(item => item.Measurements)
        .ToDictionary(m => m.Id, m => JsonSerializer.Serialize(new
        {
            m.Id, m.MType, m.Name, m.Notes, m.Color, m.CountSymbol, m.PageFolder, m.TakeoffFolder, m.ScaleMetersPerPt,
            Points = m.Points.Select(p => new[] { p.X, p.Y }), Holes = m.Holes.Select(h => h.Select(p => new[] { p.X, p.Y })),
            m.JoistEnabled, m.JoistType, m.JoistSpacingInches, m.JoistDirectionDegrees, m.JoistDirectionLocked,
            m.JoistMoveNote, m.JoistNoteOffsetX, m.JoistNoteOffsetY, m.JoistNotePositionSet,
            Extra = m.ExtraJoists.Select(j => new { j.Id, Start = new[] { j.Start.X, j.Start.Y }, End = new[] { j.End.X, j.End.Y } }),
        }));

    private sealed record ReopenExpectation(int TakeoffCount, Dictionary<string, string> Measurements);

    private static Dictionary<string, string> PortableSnapshot(OurPlanCoreJob job, Dictionary<string, string> snapshot) => snapshot.ToDictionary(
        pair => pair.Key, pair =>
        {
            JsonObject value = JsonNode.Parse(pair.Value)!.AsObject();
            foreach (string key in new[] { nameof(Measurement.PageFolder), nameof(Measurement.TakeoffFolder) })
            {
                string? path = value[key]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path))
                    value[key] = Path.GetRelativePath(job.RootPath, path);
            }
            return value.ToJsonString();
        });

    private static void CheckSnapshots(Dictionary<string, string> expected, Dictionary<string, string> actual, string reason) =>
        Check(expected.Count == actual.Count && expected.All(p => actual.TryGetValue(p.Key, out string? value) && p.Value == value), reason);
    private static void Record(string text) { Checks.Add(text); Console.WriteLine("PASS " + text); }
    private static void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static object? Call(object target, string method, params object?[] args) => UserFeedbackTests.Call(target, method, args);
    private static async Task Wait(Func<bool> ready)
    {
        DateTime deadline = DateTime.UtcNow.AddMinutes(4);
        while (!ready()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Real-project UI startup timed out."); await Task.Delay(150); }
    }
    private static Task WaitStorage() => Wait(() => !JobFileWriteActivity.HasActiveBackgroundWriters && !JobFileWriteActivity.HasActivePackageCheckpoints);
    private static void Capture(FrameworkElement element, string path)
    {
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)element.ActualWidth), Math.Max(1, (int)element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); png.Save(file);
    }

    private sealed class PromptResponder : IDisposable
    {
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
        private readonly string _choice;
        private readonly bool _capture;
        public PromptResponder(string choice, bool capture = false)
        {
            _choice = choice; _capture = capture;
            _timer.Tick += (_, _) => Respond(); _timer.Start();
        }
        private void Respond()
        {
            Window? dialog = Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.GetType().Name == "MeasurementPasteModeDialog");
            if (dialog != null)
            {
                if (_capture && !File.Exists(Path.Combine(_root, "paste-dialog.png"))) Capture(dialog, Path.Combine(_root, "paste-dialog.png"));
                Button? button = Descendants(dialog).OfType<Button>().FirstOrDefault(b => b.Content?.ToString() == _choice);
                Check(button != null, "Explicit paste action is visible: " + _choice);
                button!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return;
            }
            EnumWindows((handle, _) =>
            {
                GetWindowThreadProcessId(handle, out uint pid);
                if (pid != Environment.ProcessId) return true;
                var klass = new StringBuilder(128); GetClassName(handle, klass, klass.Capacity);
                if (klass.ToString() != "#32770") return true;
                int command = GetDlgItem(handle, 6) != IntPtr.Zero
                    ? GetDlgItem(handle, 2) == IntPtr.Zero ? 6 : _choice == "Cancel" ? 2 : _choice == "New takeoffs" ? 7 : 6
                    : 1;
                PostMessage(handle, 0x0111, (IntPtr)command, IntPtr.Zero);
                if (command == 1) PostMessage(handle, 0x0010, IntPtr.Zero, IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
        }
        public void Dispose() => _timer.Stop();
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private delegate bool EnumWindowProc(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr hwnd, int id);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
}
