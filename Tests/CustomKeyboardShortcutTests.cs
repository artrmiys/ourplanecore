using System.Reflection;
using System.Text.Json;
using System.Windows.Input;
using OurPlanCore;
using OurPlanCore.Controls;
using SkiaSharp;

internal static class CustomKeyboardShortcutTests
{
    public static void DefaultsStaySparseAndPreserveLegacyKeys()
    {
        var config = KeyboardShortcutConfiguration.BuildDefault();
        Check(config.Overrides.Count == 0, "Default profile must leave every legacy handler active.");
        foreach (var (id, keys) in KeyboardShortcutDefaults.Keys)
            Check(config.Effective(Command(id)).SequenceEqual(keys), "Legacy keys changed for " + id);
        Check(KeyboardShortcutDefaults.For("edit.undo").SequenceEqual(new[] { "Ctrl+Z", "Back" }), "Both existing Undo aliases must remain.");
        Check(KeyboardShortcutDefaults.For("view.addBookmark").Single() == "B, K", "The existing bookmark sequence must remain.");
        Check(KeyboardShortcutDefaults.For("edit.mirrorHorizontal").Count == 0, "Mirror must start unassigned.");
    }

    public static void NormalizesAliasesAndRejectsInvalidGestures()
    {
        foreach (var (input, expected) in new[] { ("control+s", "Ctrl+S"), ("Ctrl++", "Ctrl+OemPlus"),
            ("Ctrl+-", "Ctrl+OemMinus"), ("Alt+Shift+7", "Alt+Shift+D7"), ("b,k", "B, K") })
            Check(KeyboardShortcutGesture.TryNormalize(input, out string actual, out _) && actual == expected, input);
        foreach (string input in new[] { "Ctrl", "LeftShift", "Ctrl+Ctrl+A", "Win+L", "Alt+F4", "A, B, C, D", "", "NoSuchKey" })
            Check(!KeyboardShortcutGesture.TryNormalize(input, out _, out _), "Must reject " + input);
        var normalized = KeyboardShortcutStore.Parse("{\"Version\":1,\"Overrides\":{\"view.zoomIn\":[\"Ctrl++\"]}}");
        Check(normalized.Overrides["view.zoomIn"].Single() == "Ctrl+OemPlus", "Imported keys must execute canonically.");
        Check(!KeyboardShortcutGesture.CanRunWhileTyping(Key.S, ModifierKeys.None), "A reassigned Save letter must not steal typing.");
        Check(!KeyboardShortcutGesture.CanRunWhileTyping(Key.Space, ModifierKeys.None) && !KeyboardShortcutGesture.CanRunWhileTyping(Key.Enter, ModifierKeys.None), "Editor Space and Enter stay local.");
        Check(!KeyboardShortcutGesture.CanRunWhileTyping(Key.C, ModifierKeys.Control), "Native editor Copy stays local.");
        Check(KeyboardShortcutGesture.CanRunWhileTyping(Key.S, ModifierKeys.Control), "Existing global Save still works in text fields.");
    }

    public static void ConflictsRespectFocusAndSequencePrefixes()
    {
        var pages = Command("pages.copy"); var takeoffs = Command("takeoffs.copy");
        var global = new KeyboardCommandDefinition("global", "Global", "Test", KeyboardCommandContext.Workspace, []);
        var config = KeyboardShortcutConfiguration.BuildDefault();
        Check(config.FindConflicts(pages, "Ctrl+C", [pages, takeoffs]).Count == 0, "Different tree focus contexts may share keys.");
        Check(config.FindConflicts(global, "Ctrl+C", [pages, takeoffs, global]).Count == 2, "Global binding must disclose both tree conflicts.");
        Check(KeyboardShortcutGesture.Conflicts("B", "B, K"), "Sequence prefixes need conflict detection.");
        var mirror = new KeyboardCommandDefinition("edit.mirrorHorizontal", "Mirror", "Test", KeyboardCommandContext.Viewport, []);
        var overlay = Command("overlay.left");
        var roof = Command("roof.ridge");
        Check(config.FindConflicts(mirror, "Ctrl+Alt+Left", [mirror, overlay]).Count == 1, "Overlay and viewport commands share one active canvas.");
        Check(config.FindConflicts(mirror, "R", [mirror, roof]).Count == 1, "Non-tool viewport commands can execute in roof mode.");
        config.ValidateConflicts([Command("tool.ruler"), roof]); // Existing defaults intentionally share R in exclusive tool modes.
        var firstDialog = new KeyboardCommandDefinition("surface:window:FirstDialog|name:Apply", "First apply", "Test", KeyboardCommandContext.Surface, ["F7"]);
        var otherDialog = new KeyboardCommandDefinition("surface:window:OtherDialog|name:Apply", "Other apply", "Test", KeyboardCommandContext.Surface, ["F7"]);
        var sameDialog = firstDialog with { Id = "surface:window:FirstDialog|name:Remove" };
        Check(config.FindConflicts(firstDialog, "F7", [firstDialog, otherDialog, global]).Count == 0, "Independent modal windows may share a key.");
        Check(config.FindConflicts(firstDialog, "F7", [firstDialog, sameDialog]).Count == 1, "Commands in the same dialog must detect conflicts.");
        config.Overrides["global"] = ["Ctrl+C"];
        Throws(() => config.ValidateConflicts([pages, takeoffs, global]), "Imported duplicate bindings must not be silently accepted.");
    }

    public static void ResetCloneAndPresetRoundTripKeepExplicitUnbound()
    {
        var original = new KeyboardShortcutConfiguration();
        original.Overrides["file.save"] = [];
        original.Overrides["edit.mirrorHorizontal"] = ["Ctrl+Alt+H"];
        var clone = original.Clone(); clone.Overrides["edit.mirrorHorizontal"][0] = "F10";
        Check(original.Overrides["edit.mirrorHorizontal"].Single() == "Ctrl+Alt+H", "Draft edits must not alter active settings.");
        var restored = KeyboardShortcutStore.Parse(KeyboardShortcutStore.Export(original));
        Check(restored.Effective(Command("file.save")).Count == 0, "Explicitly removed keys must survive persistence.");
        restored.Overrides.Remove("file.save");
        Check(restored.Effective(Command("file.save")).Single() == "Ctrl+S", "Reset restores the old default.");
        Throws(() => KeyboardShortcutStore.Parse("{\"Version\":99}"), "Future versions are protected.");
        Throws(() => KeyboardShortcutStore.Parse("{\"Version\":1,\"Overrides\":null}"), "Null overrides are invalid.");
    }

    public static void MirrorUsesProductionUndoAndReadOnlyGuard()
    {
        RunSta(() =>
        {
            var measurement = new Measurement { MType = "area", PageFolder = "shortcut-test", Points = [new(10, 10), new(80, 20), new(25, 75)] };
            var viewport = new PdfViewport();
            typeof(PdfViewport).GetField("_pageFolder", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(viewport, measurement.PageFolder);
            viewport.SetMeasurements([measurement]); viewport.SelectMeasurements([measurement]);
            SKPoint[] before = measurement.Points.ToArray();
            Check(viewport.ExecuteCustomKeyboardCommand("edit.mirrorHorizontal"), "Mirror command recognized.");
            Check(!before.SequenceEqual(measurement.Points), "Horizontal mirror must change actual geometry.");
            viewport.UndoLast(); Check(before.SequenceEqual(measurement.Points), "Mirror uses production Undo.");
            viewport.SelectMeasurements([measurement]); viewport.ExecuteCustomKeyboardCommand("edit.mirrorVertical");
            Check(!before.SequenceEqual(measurement.Points), "Vertical mirror must change actual geometry.");
            viewport.UndoLast(); Check(before.SequenceEqual(measurement.Points), "Vertical mirror Undo restores all vertices.");
            viewport.IsReadOnlyMode = true; viewport.ExecuteCustomKeyboardCommand("edit.mirrorHorizontal");
            Check(before.SequenceEqual(measurement.Points), "Read-only shortcut cannot modify geometry.");
            Check(!viewport.ExecuteCustomKeyboardCommand("settings.shortcuts"), "Read-only viewport cannot swallow unrelated settings commands.");
        });
    }

    public static void DamagedOrLockedSettingsRecoverWithOriginalBytesRetained()
    {
        string root = Path.Combine(Path.GetTempPath(), "opc-shortcut-settings-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "keyboard_shortcuts.json");
        try
        {
            const string damaged = "{ interrupted shortcut settings";
            File.WriteAllText(path, damaged);
            ThrowsIo(() => KeyboardShortcutStore.Save(path, new()), "A damaged target must not be overwritten, even if another scope was active.");
            Check(DataFileReader.IsProtected(path), "Damaged file must remain protected.");
            Check(Directory.GetFiles(root, "*.corrupt-*").Any(copy => File.ReadAllText(copy) == damaged), "Exact damaged bytes retained in quarantine.");
            KeyboardShortcutStore.Recover(path, reset: true);
            Check(!DataFileReader.IsProtected(path) && KeyboardShortcutStore.Parse(File.ReadAllText(path)).Overrides.Count == 0, "Explicit reset restores original keys.");
            Check(Directory.GetFiles(root, "*.corrupt-*").Any(copy => File.ReadAllText(copy) == damaged), "Reset retains old bytes for rollback.");
            var config = new KeyboardShortcutConfiguration(); config.Overrides["file.save"] = ["Ctrl+Alt+S"];
            KeyboardShortcutStore.Save(path, config); byte[] before = File.ReadAllBytes(path);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                ThrowsIo(() => KeyboardShortcutStore.Save(path, new()), "Locked settings cannot be overwritten.");
            KeyboardShortcutStore.Recover(path);
            Check(before.SequenceEqual(File.ReadAllBytes(path)), "Retry must preserve the full settings document.");
            Check(Directory.GetFiles(root, "*.recovered-*").Any(copy => File.ReadAllBytes(copy).SequenceEqual(before)), "Retry creates a verified rollback copy.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static KeyboardCommandDefinition Command(string id) => new(id, id, "Test", KeyboardShortcutDefaults.ContextFor(id), KeyboardShortcutDefaults.For(id));
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws(Action action, string message)
    {
        try { action(); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return; }
        throw new InvalidOperationException(message);
    }
    private static void ThrowsIo(Action action, string message)
    { try { action(); } catch (IOException) { return; } throw new InvalidOperationException(message); }
    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw failure;
    }
}
