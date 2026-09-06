using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using OurPlanCore;
using OurPlanCore.Controls;

internal static partial class RealProjectPerformanceHarness
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly List<object> Steps = [];
    private static readonly List<string> Failures = [];
    private static string _root = "";
    private static long _peakPrivateBytes, _peakWorkingSet, _osPeakWorkingSet;
    private static readonly object MemoryGate = new();
    private static readonly Stopwatch ColdOpen = new();

    public static int Run(string[] args)
    {
        if (args.Length < 3 || !File.Exists(args[1]) || !args[1].EndsWith(".ourplan", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("real-work-perf <real .ourplan> <new evidence directory>");
        _root = Path.GetFullPath(args[2]);
        if (Directory.Exists(_root)) throw new IOException("Performance evidence directory already exists");
        Directory.CreateDirectory(_root);
        string package = Path.Combine(_root, "project.ourplan");
        File.Copy(args[1], package);
        string inputHash = Hash(args[1]);
        Check(Hash(package) == inputHash, "Copied package does not match the real input");
        string runId = Path.GetFileName(_root);
        string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OurPlanCore Bench", runId);
        if (Directory.Exists(profile)) throw new IOException("Performance profile already exists");
        string originalSettings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OurPlanCore", "settings.json");
        AppSettings settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(originalSettings))!;
        settings.LastJobPath = package;
        settings.JobsRootPath = _root;
        settings.JobsRootPaths = [_root];
        settings.RecentJobs = [];
        settings.AutoCleanRasterCacheOnClose = false;
        settings.BackgroundJobWarmupEnabled = false;
        Environment.SetEnvironmentVariable("OURPLANCORE_PROFILE_ROOT", profile);
        Environment.SetEnvironmentVariable("OURPLANCORE_SETTINGS_PATH", Path.Combine(_root, "settings.json"));
        Environment.SetEnvironmentVariable(SmartContextStore.GlobalRootEnvironmentVariable, Path.Combine(profile, "global"));
        Environment.SetEnvironmentVariable("OURPLANCORE_VIEWPORT_PAGE_STRESS_TARGET_ZOOM", "4");
        Environment.SetEnvironmentVariable("OURPLANCORE_VIEWPORT_PAGE_STRESS_PAN_STEPS", "10");
        AppSettingsStore.Save(settings);
        File.WriteAllText(Path.Combine(_root, "launch.json"), JsonSerializer.Serialize(new
        {
            Source = args[1], InputSHA256 = inputHash, Package = package, Profile = profile,
            Runtime = Environment.Version.ToString(), Assembly = typeof(MainWindow).Assembly.Location,
            AssemblySHA256 = Hash(typeof(MainWindow).Assembly.Location),
            SettingsSHA256 = Hash(Path.Combine(_root, "settings.json")),
            SourceSettingsSHA256 = Hash(originalSettings),
            StartedUtc = DateTime.UtcNow, ProcessId = Environment.ProcessId,
            Config = new { settings.StaticPageRenderEnabled, settings.StaticPageRenderDpi,
                settings.BackgroundJobWarmupEnabled, settings.AutoCleanRasterCacheOnClose },
        }, Json));
        int code = 2;
        using var memory = new Timer(_ => SampleMemory(), null, 0, 100);
        ColdOpen.Start();
        var thread = new Thread(() =>
        {
            try { code = RunUi(package); }
            catch (Exception ex) { Failures.Add(ex.ToString()); Console.WriteLine(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        Check(Hash(args[1]) == inputHash, "Original project changed during performance tests");
        return code;
    }

    private static int RunUi(string package)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("pack://application:,,,/ourplancore;component/Resources/AppResources.xaml") });
        typeof(App).GetProperty("StartupProjectPath", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, package);
        var main = new MainWindow(); app.MainWindow = main;
        int code = 2;
        main.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await Wait(() => Get<OurPlanCoreJob?>(main, "_currentJob") != null && Get<PageInfo?>(main, "_currentPage") != null);
                var job = Get<OurPlanCoreJob>(main, "_currentJob");
                var items = Get<List<TakeoffItem>>(main, "_takeoffItems");
                var pages = ((IEnumerable<PageInfo>)Call(main, "CollectPagesUnder", job.PagesRoot)!).ToList();
                var initial = Get<PageInfo>(main, "_currentPage");
                ViewportPerformanceRecorder.BeginRun(job.RootPath, "real-work-perf");
                await (Task)Call(main, "WaitForViewportPageRenderAsync", initial, 60000)!;
                await (Task)Call(main, "WaitForViewportPagePaintAsync", initial, 60000)!;
                ColdOpen.Stop();
                Console.WriteLine($"PERF first real sheet painted: {ColdOpen.ElapsedMilliseconds} ms");
                Steps.Add(new { Operation = "ColdJobWindowOpenToPaint", Ms = ColdOpen.ElapsedMilliseconds,
                    Scope = "Fresh MainWindow, project extraction, data/trees and first paint; process startup and EXE extraction measured separately" });
                Check(items.Sum(i => i.Measurements.Count) > 100 && pages.Count > 40, "A real populated project is required");
                string modelBefore = ModelDigest(items);
                string[] pageNamesBefore = pages.Select(p => Path.GetRelativePath(job.PagesRoot, p.FolderPath) + "|" + p.Name).ToArray();
                var measured = items.SelectMany(i => i.Measurements).GroupBy(m => m.PageFolder, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var samples = pages.Where(p => measured.ContainsKey(p.FolderPath))
                    .OrderByDescending(p => measured[p.FolderPath]).ThenBy(p => p.Name, StringComparer.Ordinal)
                    .Take(12).ToList();
                foreach (var page in samples)
                {
                    var task = (Task)Call(main, "OpenAndProbePageAsync", page, 60000, "real-populated")!;
                    await task;
                    object probe = task.GetType().GetProperty("Result")!.GetValue(task)!;
                    bool passed = (bool)probe.GetType().GetProperty("Passed")!.GetValue(probe)!;
                    Check(passed, "Sheet probe failed: " + page.Name);
                    Steps.Add(new { Operation = "SheetOpenZoomPan", Page = page.Name, Measurements = measured[page.FolderPath], Probe = probe });
                }
                await WaitStorage();
                Console.WriteLine("PERF tree rebuild and scrolling");
                await ProbeTrees(main);
                await ProbeSaveAndExport(main, samples.Take(3).ToList());
                Check(ModelDigest(Get<List<TakeoffItem>>(main, "_takeoffItems")) == modelBefore,
                    "Measurements or takeoff quantities changed during read/display/save/export scenarios");
                var afterPages = ((IEnumerable<PageInfo>)Call(main, "CollectPagesUnder", job.PagesRoot)!).ToList();
                Check(afterPages.Select(p => Path.GetRelativePath(job.PagesRoot, p.FolderPath) + "|" + p.Name).SequenceEqual(pageNamesBefore),
                    "Visible page names or ordering changed");
                Steps.Add(new { Operation = "DataPreserved", Pages = pages.Count, Takeoffs = items.Count,
                    Measurements = items.Sum(i => i.Measurements.Count), Digest = modelBefore, Names = pageNamesBefore });
                code = Failures.Count == 0 ? 0 : 2;
            }
            catch (Exception ex) { Failures.Add(ex.ToString()); }
            finally
            {
                SampleMemory();
                var performance = ViewportPerformanceRecorder.IsActive ? ViewportPerformanceRecorder.EndRun() : null;
                File.WriteAllText(Path.Combine(_root, "report.json"), JsonSerializer.Serialize(new
                {
                    Passed = code == 0, Steps, Failures, Performance = performance,
                    SampledPeakPrivateBytes = _peakPrivateBytes, SampledPeakWorkingSetBytes = _peakWorkingSet,
                    OSProcessPeakWorkingSetBytes = _osPeakWorkingSet, MemorySamplingIntervalMs = 100,
                    FinishedUtc = DateTime.UtcNow,
                }, Json));
                Console.WriteLine($"REAL WORK PERF {code}: {_root}");
                foreach (string error in Failures) Console.WriteLine(error);
                var close = Stopwatch.StartNew();
                main.Close(); app.Shutdown();
                close.Stop();
                File.WriteAllText(Path.Combine(_root, "close.json"), JsonSerializer.Serialize(new
                { Operation = "CloseAndShutdown", Ms = close.ElapsedMilliseconds, FinishedUtc = DateTime.UtcNow }, Json));
                Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }
        }, DispatcherPriority.ApplicationIdle);
        main.Show(); Dispatcher.Run();
        return code;
    }

    private static string ModelDigest(IEnumerable<TakeoffItem> items) => Convert.ToHexString(SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(items.OrderBy(i => i.FolderPath)
            .Select(i => new { i.Name, i.UnitPrice, i.Notes, i.MeasurementType, Total = i.Total(0),
                Measurements = i.Measurements.OrderBy(m => m.Id) }), Json))));
    private static string Hash(string path)
    { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
    private static void SampleMemory()
    {
        using var process = Process.GetCurrentProcess();
        lock (MemoryGate)
        {
            _peakPrivateBytes = Math.Max(_peakPrivateBytes, process.PrivateMemorySize64);
            _peakWorkingSet = Math.Max(_peakWorkingSet, process.WorkingSet64);
            _osPeakWorkingSet = Math.Max(_osPeakWorkingSet, process.PeakWorkingSet64);
        }
    }
    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Private)!.GetValue(target)!;
    private static object? Call(object target, string method, params object?[] args) => UserFeedbackTests.Call(target, method, args);
    private static void Check(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
    private static async Task Wait(Func<bool> predicate)
    {
        var watch = Stopwatch.StartNew();
        while (!predicate())
        {
            if (watch.Elapsed > TimeSpan.FromMinutes(4)) throw new TimeoutException("Real project did not reach the expected state");
            await Task.Delay(30);
        }
    }
    private static Task WaitStorage() => Wait(() => !JobFileWriteActivity.HasActiveBackgroundWriters && !JobFileWriteActivity.HasActivePackageCheckpoints);
}
