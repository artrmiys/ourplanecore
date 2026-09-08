using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OurPlanCore;

internal static partial class TreeSortUiHarness
{
    public static int Run(string[] args)
    {
        string root = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(root);
        string package = Path.Combine(root, "Tree QA.ourplan");
        File.Copy(args[1], package, overwrite: false);
        Environment.SetEnvironmentVariable("OURPLANCORE_PROFILE_ROOT", Path.Combine(root, "profile"));
        Environment.SetEnvironmentVariable("OURPLANCORE_SETTINGS_PATH", Path.Combine(root, "settings.json"));
        Environment.SetEnvironmentVariable(SmartContextStore.GlobalRootEnvironmentVariable, Path.Combine(root, "global"));
        int result = 2;
        var thread = new Thread(() =>
        {
            try { result = RunUi(package, root); }
            catch (Exception ex) { Console.WriteLine(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromMinutes(4))) Environment.Exit(2);
        return result;
    }

    private static int RunUi(string package, string root)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("pack://application:,,,/ourplancore;component/Resources/AppResources.xaml") });
        typeof(App).GetProperty("StartupProjectPath", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, package);
        var main = new MainWindow();
        app.MainWindow = main;
        int result = 2;
        var checks = new List<object>();
        using var modalWatch = new ModalWatch();
        main.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(45);
                while (Field<OurPlanCoreJob?>(main, "_currentJob") == null || Field<PageInfo?>(main, "_currentPage") == null)
                {
                    if (DateTime.UtcNow > deadline) throw new TimeoutException("Project load");
                    await Task.Delay(100);
                }
                OurPlanCoreJob job = Field<OurPlanCoreJob>(main, "_currentJob");
                TreeView tree = (TreeView)main.FindName("TakeoffsTree");
                if (tree.SelectedItem is TreeViewItem selected) selected.IsSelected = false;
                string[] before = Signature(main);
                var originalItems = Field<List<TakeoffItem>>(main, "_takeoffItems").ToArray();
                var timer = Stopwatch.StartNew();
                Call(main, "SortTakeoffsWalls");
                checks.Add(new { Operation = "Walls sort including UI refresh", Milliseconds = timer.ElapsedMilliseconds });
                Check(before.SequenceEqual(Signature(main)), "Sort preserved measurements");
                Check(originalItems.All(item => Field<List<TakeoffItem>>(main, "_takeoffItems").Any(current => ReferenceEquals(item, current))),
                    "Sort reuses takeoff objects without reloading measurements");
                string undo = Path.Combine(job.RootPath, ".undo", "operations");
                long bytes = Directory.GetFiles(undo, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length);
                int count = Directory.GetDirectories(undo).Length;
                timer.Restart(); Call(main, "SortTakeoffsWalls");
                checks.Add(new { Operation = "Repeat walls sort including UI refresh", Milliseconds = timer.ElapsedMilliseconds, UndoBytes = bytes });
                Check(Directory.GetDirectories(undo).Length == count, "Repeat does not create history");
                var item = Field<List<TakeoffItem>>(main, "_takeoffItems").First();
                string source = item.FolderPath;
                string parent = Path.GetDirectoryName(source)!;
                string target = OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "QA move target");
                timer.Restart(); Move(main, source, target);
                checks.Add(new { Operation = "Move into folder through UI command", Milliseconds = timer.ElapsedMilliseconds });
                string moved = Directory.GetDirectories(target).Single();
                timer.Restart(); Move(main, moved, parent);
                checks.Add(new { Operation = "Move back through UI command", Milliseconds = timer.ElapsedMilliseconds });
                Check(before.SequenceEqual(Signature(main)), "Move preserved measurement IDs and page links");
                Check(Directory.Exists(source), "Original item restored");
                await VerifyAutosaveCollisions(main, checks, modalWatch);
                Check(modalWatch.Titles.Count == 0, "No modal dialogs during tree commands: " + string.Join(", ", modalWatch.Titles));
                checks.Add(new { Takeoffs = Field<List<TakeoffItem>>(main, "_takeoffItems").Count, Measurements = before.Length });
                result = 0;
            }
            catch (Exception ex) { checks.Add(new { Failure = ex.ToString() }); }
            finally
            {
                string report = JsonSerializer.Serialize(new { Passed = result == 0, Checks = checks }, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(root, "report.json"), report);
                Console.WriteLine(report);
                main.Close(); app.Shutdown();
                Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }
        }, DispatcherPriority.ApplicationIdle);
        main.Show(); Dispatcher.Run();
        return result;
    }

    private static void Move(MainWindow main, string path, string target)
    {
        const BindingFlags flags = BindingFlags.NonPublic;
        Type entry = typeof(MainWindow).GetNestedType("TakeoffsClipboardEntry", flags)!;
        Type modeType = typeof(MainWindow).GetNestedType("TakeoffsClipboardMode", flags)!;
        Type payloadType = typeof(MainWindow).GetNestedType("TakeoffsClipboard", flags)!;
        var entries = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(entry))!;
        entries.Add(Activator.CreateInstance(entry, path, true));
        object mode = Enum.Parse(modeType, "Cut");
        object payload = Activator.CreateInstance(payloadType, entries, mode)!;
        Call(main, "RunTakeoffDrop", payload, target, mode, null);
    }

    private static string[] Signature(MainWindow main) => Field<List<TakeoffItem>>(main, "_takeoffItems")
        .SelectMany(item => item.Measurements).Select(m => JsonSerializer.Serialize(m.Snapshot())).OrderBy(s => s, StringComparer.Ordinal).ToArray();
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static object? Call(object target, string name, params object?[] args)
    {
        MethodInfo[] matches = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(method => method.Name == name && method.GetParameters().Length == args.Length)
            .Where(method => method.GetParameters().Select((parameter, index) => args[index] == null
                ? !parameter.ParameterType.IsValueType || Nullable.GetUnderlyingType(parameter.ParameterType) != null
                : parameter.ParameterType.IsInstanceOfType(args[index])).All(compatible => compatible))
            .ToArray();
        Check(matches.Length == 1, $"Expected one compatible overload for {name}({args.Length} arguments), found {matches.Length}");
        return matches[0].Invoke(target, args);
    }
    private static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); }
}
