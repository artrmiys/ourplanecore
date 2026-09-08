using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Threading;
using OurPlanCore;

internal static partial class TreeSortUiHarness
{
    private static async Task VerifyAutosaveCollisions(MainWindow main, List<object> checks, ModalWatch dialogs)
    {
        await WaitForStorage(main);
        OurPlanCoreJob job = Field<OurPlanCoreJob>(main, "_currentJob");
        string[] measurements = Signature(main);
        string fixture = OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "QA checkpoint collisions");
        foreach (string name in new[] { "2x4 9", "2x6 9", "corr 9", "ext 9" })
            OurPlanCoreJobStore.CreateTakeoffItem(job, fixture, name, "#FF0000", "line");
        Call(main, "LoadTakeoffsForJob");
        CheckSignatures(measurements, Signature(main), "Fixture reload preserved the measurement baseline");
        SelectNode(main, fixture);

        string[] unsorted = Order(fixture);
        int journals = JournalCount(job);
        await WithCheckpoint(main, "Walls sort waits and runs once", () => Call(main, "SortTakeoffsWalls"), checks, dialogs);
        string[] sorted = Order(fixture);
        Check(!unsorted.SequenceEqual(sorted), "Wall sort actually changed the fixture order");
        Check(JournalCount(job) == journals + 1, "Wall sort creates one journal after waiting");

        journals = JournalCount(job);
        await WithCheckpoint(main, "Same-folder drop waits and rejects reentry", () => Drop(main, sorted[0], sorted[1], true), checks, dialogs,
            duringWait: () =>
            {
                string[] nestedOrder = Order(fixture);
                var metadata = nestedOrder.ToDictionary(path => Path.Combine(path, "Data.xml"),
                    path => File.ReadAllBytes(Path.Combine(path, "Data.xml")));
                Check(JobFileWriteActivity.HasActivePackageCheckpoints, "Nested command runs while the checkpoint remains active");
                Drop(main, sorted[0], sorted[1], true);
                Check(nestedOrder.SequenceEqual(Order(fixture)), "Reentrant drop leaves the order unchanged during the checkpoint");
                Check(metadata.All(file => file.Value.SequenceEqual(File.ReadAllBytes(file.Key))),
                    "Reentrant drop leaves every sibling metadata file byte-identical during the checkpoint");
            });
        string[] reordered = Order(fixture);
        Check(reordered.SequenceEqual(new[] { sorted[1], sorted[0] }.Concat(sorted.Skip(2))), "Drop executes once with the requested order");
        Check(JournalCount(job) == journals, "Same-folder reorder preserves existing history semantics");

        journals = JournalCount(job);
        await WithCheckpoint(main, "No-op drop keeps pending autosave scheduled", () => Drop(main, sorted[0], sorted[1], true), checks, dialogs);
        Check(JournalCount(job) == journals && reordered.SequenceEqual(Order(fixture)), "No-op drop leaves data and history unchanged");
        Check(Field<DispatcherTimer?>(main, "_packageAutosaveTimer")?.IsEnabled == true,
            "No-op after waiting re-arms automatic save for remaining local changes");

        string measured = Field<List<TakeoffItem>>(main, "_takeoffItems").First(item => item.Measurements.Count > 0).FolderPath;
        string oldParent = Path.GetDirectoryName(measured)!;
        string destination = OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "QA checkpoint destination");
        journals = JournalCount(job);
        await WithCheckpoint(main, "Cross-folder measured move waits and runs once", () => Move(main, measured, destination), checks, dialogs);
        string moved = Directory.GetDirectories(destination).Single();
        Check(!Directory.Exists(measured) && Path.GetFileName(moved) == Path.GetFileName(measured), "Cross-folder move is complete without a duplicate");
        Check(JournalCount(job) == journals + 1, "Cross-folder move has exactly one journal");
        TakeoffItem liveMoved = Field<List<TakeoffItem>>(main, "_takeoffItems").Single(item => item.FolderPath == moved);
        Check(liveMoved.Measurements.Count > 0 && liveMoved.Measurements.All(measurement => measurement.TakeoffFolder == moved),
            "Moved item and its measurements point to their new takeoff folder");
        string[] normalized = Field<List<TakeoffItem>>(main, "_takeoffItems").SelectMany(item => item.Measurements)
            .Select(measurement =>
            {
                Measurement snapshot = measurement.Snapshot();
                if (snapshot.TakeoffFolder == moved) snapshot.TakeoffFolder = measured;
                return JsonSerializer.Serialize(snapshot);
            }).OrderBy(json => json, StringComparer.Ordinal).ToArray();
        CheckSignatures(measurements, normalized, "Concurrent move preserved every other field, including measurement IDs and page links");
        Move(main, moved, oldParent);
        Check(Directory.Exists(measured), "Measured takeoff returned to its original path");
        CheckSignatures(measurements, Signature(main), "Moving back restored every complete measurement snapshot");

        await VerifyInvalidatedEdit(main, fixture, job, checks, dialogs);
        await VerifyActualAutosave(main, fixture, checks, dialogs);
        CheckSignatures(measurements, Signature(main), "All checkpoint collision scenarios preserved measurement snapshots");
    }

    private static void CheckSignatures(string[] expected, string[] actual, string label)
    {
        if (expected.SequenceEqual(actual)) return;
        string? missing = expected.Except(actual).FirstOrDefault();
        if (missing == null) throw new InvalidOperationException($"{label}: measurement counts {expected.Length} -> {actual.Length}");
        using JsonDocument old = JsonDocument.Parse(missing);
        string id = old.RootElement.GetProperty("Id").GetString()!;
        string? changed = actual.FirstOrDefault(json =>
        {
            using JsonDocument candidate = JsonDocument.Parse(json);
            return candidate.RootElement.GetProperty("Id").GetString() == id;
        });
        if (changed == null) throw new InvalidOperationException($"{label}: missing measurement {id}; counts {expected.Length} -> {actual.Length}");
        using JsonDocument current = JsonDocument.Parse(changed);
        string[] fields = old.RootElement.EnumerateObject()
            .Where(property => !current.RootElement.TryGetProperty(property.Name, out JsonElement value) || value.GetRawText() != property.Value.GetRawText())
            .Select(property => property.Name).ToArray();
        throw new InvalidOperationException($"{label}: measurement {id}; changed fields [{string.Join(", ", fields)}]; counts {expected.Length} -> {actual.Length}");
    }

    private static async Task WithCheckpoint(MainWindow main, string label, Action command,
        List<object> checks, ModalWatch dialogs, Action? duringWait = null, Action? afterCommand = null)
    {
        await WaitForStorage(main);
        OurPlanPackageSession session = Field<OurPlanPackageSession>(main, "_currentPackageSession");
        session.HasUnpackagedChanges = true;
        using JobFileWriteActivity.PackageCheckpointScope checkpoint = JobFileWriteActivity.BeginPackageCheckpoint();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcherServiced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Set(main, "_packageAutosaveTask", completion.Task);
        bool responded = false;
        bool nestedInvoked = false;
        Exception? dispatcherFailure = null;
        int originalModalCount = dialogs.Titles.Count;
        var tick = new DispatcherTimer(DispatcherPriority.Send, main.Dispatcher) { Interval = TimeSpan.FromMilliseconds(30) };
        tick.Tick += (_, _) =>
        {
            responded = true;
            try
            {
                if (!nestedInvoked && duringWait != null)
                {
                    nestedInvoked = true;
                    duringWait();
                }
            }
            catch (Exception ex) { dispatcherFailure = ex; }
            finally { dispatcherServiced.TrySetResult(); }
        };
        tick.Start();
        Task release = Task.Run(async () =>
        {
            try
            {
                await dispatcherServiced.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await Task.Delay(30);
            }
            finally
            {
                checkpoint.Dispose();
                completion.TrySetResult();
            }
        });
        var timer = Stopwatch.StartNew();
        try
        {
            command();
            afterCommand?.Invoke();
            Check(completion.Task.IsCompleted, label + ": command must wait for the checkpoint");
            Check(responded, label + ": dispatcher remained responsive while waiting");
            Check(duringWait == null || nestedInvoked, label + ": nested callback executed during the wait");
            Check(dispatcherFailure == null, label + ": nested callback failed: " + dispatcherFailure);
            Check(main.IsEnabled, label + ": window is enabled after waiting");
            Check(dialogs.Titles.Count == originalModalCount, label + ": unexpected modal dialog");
            Check(!JobFileWriteActivity.HasActivePackageCheckpoints, label + ": checkpoint was released");
            await main.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            checks.Add(new { Operation = label, Milliseconds = timer.ElapsedMilliseconds, ResponsiveDispatcher = responded, ModalCount = 0 });
        }
        finally
        {
            tick.Stop();
            await release;
            if (ReferenceEquals(Field<Task?>(main, "_packageAutosaveTask"), completion.Task)) Set(main, "_packageAutosaveTask", null);
        }
    }

    private static async Task VerifyInvalidatedEdit(MainWindow main, string fixture, OurPlanCoreJob job,
        List<object> checks, ModalWatch dialogs)
    {
        string[] before = Order(fixture);
        int journals = JournalCount(job);
        try
        {
            await WithCheckpoint(main, "Project switch during wait cancels the original command",
                () => Drop(main, before[0], before[1], true), checks, dialogs,
                duringWait: () => Set(main, "_currentJob", new OurPlanCoreJob { Name = job.Name, RootPath = job.RootPath }),
                afterCommand: () => Set(main, "_currentJob", job));
            Check(before.SequenceEqual(Order(fixture)) && JournalCount(job) == journals, "Changed project identity cancels without writes");
        }
        finally { Set(main, "_currentJob", job); }

        string measured = Field<List<TakeoffItem>>(main, "_takeoffItems").First(item => item.Measurements.Count > 0).FolderPath;
        string path = Path.Combine(measured, "measurements.json");
        byte[] original = File.ReadAllBytes(path);
        try
        {
            await WithCheckpoint(main, "Read protection acquired during wait cancels the command",
                () => Drop(main, before[0], before[1], true), checks, dialogs,
                duringWait: () =>
                {
                    using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    _ = TakeoffStore.ReadMeasurements(measured);
                });
            Check(DataFileReader.IsProtected(job.RootPath), "Read protection really became active during the wait");
            Check(before.SequenceEqual(Order(fixture)) && JournalCount(job) == journals, "Protected project cancels without writes");
            Check(original.SequenceEqual(File.ReadAllBytes(path)), "Read-protected measurement bytes remain unchanged");
        }
        finally
        {
            if (DataFileReader.IsProtected(path)) DataFileReader.RestoreOrRetry(path);
        }

        using (JobFileWriteActivity.BeginPackageCheckpoint())
        {
            bool refused = false;
            try { using IDisposable invalid = JobFileWriteActivity.BeginBulkWrite(); }
            catch (IOException) { refused = true; }
            Check(refused, "Direct model writes remain forbidden inside a checkpoint");
        }
        checks.Add(new { Operation = "Model write guard remains enforced", Passed = true });
    }

    private static async Task VerifyActualAutosave(MainWindow main, string fixture, List<object> checks, ModalWatch dialogs)
    {
        await WaitForStorage(main);
        Call(main, "FlushTakeoffAutosaves");
        OurPlanPackageSession session = Field<OurPlanPackageSession>(main, "_currentPackageSession");
        session.HasUnpackagedChanges = true;
        Task save = (Task)Call(main, "RunAutomaticPackageCheckpointAsync", session)!;
        Set(main, "_packageAutosaveTask", save);
        try
        {
            await WaitUntil(() => save.IsCompleted || JobFileWriteActivity.HasActivePackageCheckpoints, "Actual autosave start");
            Check(!save.IsCompleted && JobFileWriteActivity.HasActivePackageCheckpoints, "Real package writer is active before the drop");
            string[] before = Order(fixture);
            int journals = JournalCount(Field<OurPlanCoreJob>(main, "_currentJob"));
            int modals = dialogs.Titles.Count;
            var timer = Stopwatch.StartNew();
            Drop(main, before[0], before[1], true);
            Check(save.IsCompleted, "Drop waited for actual automatic save to finish");
            Check(Order(fixture).SequenceEqual(new[] { before[1], before[0] }.Concat(before.Skip(2))), "Actual-save overlap executes the requested drop exactly once");
            Check(JournalCount(Field<OurPlanCoreJob>(main, "_currentJob")) == journals, "Actual-save overlap preserves same-folder history semantics");
            Check(dialogs.Titles.Count == modals, "Actual autosave overlap produces no dialog");
            _ = OurPlanPackageArchive.ReadManifest(session.PackagePath, verifyObjects: true);
            Check(!Field<bool>(main, "_packageAutosaveBlocked"), "Actual autosave has no permanent failure");
            checks.Add(new { Operation = "Real populated package autosave plus UI reorder", Milliseconds = timer.ElapsedMilliseconds, PackageValidated = true, ModalCount = 0 });
        }
        finally
        {
            await save;
            if (ReferenceEquals(Field<Task?>(main, "_packageAutosaveTask"), save)) Set(main, "_packageAutosaveTask", null);
        }
    }

    private static async Task WaitForStorage(MainWindow main)
    {
        Call(main, "SupersedeAutomaticPackageCheckpoint");
        await WaitUntil(() => !JobFileWriteActivity.HasActiveBackgroundWriters &&
                              !JobFileWriteActivity.HasActivePackageCheckpoints &&
                              Field<int>(main, "_packageOperationActive") == 0, "Storage idle");
    }

    private static async Task WaitUntil(Func<bool> condition, string label)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(35);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException(label);
            await Task.Delay(25);
        }
    }

    private static string[] Order(string parent) => OurPlanCoreJobStore.GetOrderedChildDirectories(parent).ToArray();
    private static int JournalCount(OurPlanCoreJob job)
    {
        string path = Path.Combine(job.RootPath, ".undo", "operations");
        return Directory.Exists(path) ? Directory.GetDirectories(path).Length : 0;
    }
    private static void SelectNode(MainWindow main, string path)
    {
        var node = (TreeViewItem?)Call(main, "FindTakeoffTreeItemByFolder", path);
        Check(node != null, "Tree node exists: " + path);
        node!.IsSelected = true;
    }
    private static void Drop(MainWindow main, string path, string target, bool after)
    {
        var node = (TreeViewItem?)Call(main, "FindTakeoffTreeItemByFolder", target);
        Check(node != null, "Drop target exists: " + target);
        Call(main, "DropTakeoffPosition", ClipboardPayload(path), node, after);
    }
    private static object ClipboardPayload(string path)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic;
        Type entry = typeof(MainWindow).GetNestedType("TakeoffsClipboardEntry", flags)!;
        Type mode = typeof(MainWindow).GetNestedType("TakeoffsClipboardMode", flags)!;
        Type payload = typeof(MainWindow).GetNestedType("TakeoffsClipboard", flags)!;
        var entries = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(entry))!;
        entries.Add(Activator.CreateInstance(entry, path, OurPlanCoreJobStore.IsTakeoffItemFolder(path)));
        return Activator.CreateInstance(payload, entries, Enum.Parse(mode, "Cut"))!;
    }
    private static void Set(object target, string name, object? value) => target.GetType()
        .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(target, value);

    // Only watches native dialogs on this harness's STA thread, never the user's app.
    private sealed class ModalWatch : IDisposable
    {
        private readonly DispatcherTimer _timer;
        public List<string> Titles { get; } = [];
        public ModalWatch()
        {
            uint thread = GetCurrentThreadId();
            _timer = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromMilliseconds(30) };
            _timer.Tick += (_, _) => EnumThreadWindows(thread, (window, _) =>
            {
                var name = new StringBuilder(256);
                GetClassName(window, name, name.Capacity);
                if (name.ToString() != "#32770" || !IsWindowVisible(window)) return true;
                GetWindowText(window, name, name.Capacity);
                Titles.Add(name.ToString());
                PostMessage(window, 0x0111, new IntPtr(1), IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
            _timer.Start();
        }
        public void Dispose() => _timer.Stop();
        private delegate bool WindowVisitor(IntPtr window, IntPtr data);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, WindowVisitor visitor, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int size);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    }
}
