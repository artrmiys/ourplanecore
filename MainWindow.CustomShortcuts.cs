using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OurPlanCore.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    private KeyboardShortcutConfiguration _customShortcuts = KeyboardShortcutConfiguration.BuildDefault();
    private readonly Dictionary<string, KeyboardCommandDefinition> _keyboardCommands = new(StringComparer.Ordinal);
    private string _customShortcutSequence = "";
    private DateTime _customShortcutSequenceUtc;
    private bool _customShortcutSettingsLoaded;

    private void LoadCustomKeyboardShortcuts()
    {
        _customShortcuts = KeyboardShortcutStore.Resolve(_currentJob);
        _customShortcutSettingsLoaded = true;
        InstallScopedWindowKeyboardCommands();
        RefreshKeyboardCommandCatalog();
    }

    private void RefreshKeyboardCommandCatalog()
    {
        _keyboardCommands.Clear();
        _keyboardSurfaceTargets.Clear();
        foreach (CommandPaletteItem item in BuildCommandPaletteItems(includeUnavailableModules: true))
        {
            if (item.Id == "takeoffs.activeRecord") continue; // Same action as tool.toggleRecord.
            _keyboardCommands[item.Id] = new(item.Id, item.Title, item.Group,
                KeyboardShortcutDefaults.ContextFor(item.Id), KeyboardShortcutDefaults.For(item.Id), item.Description);
        }
        void Add(string id, string title, string category, KeyboardCommandContext? context = null) =>
            _keyboardCommands[id] = new(id, title, category, context ?? KeyboardShortcutDefaults.ContextFor(id), KeyboardShortcutDefaults.For(id));
        Add("help.shortcuts", "Keyboard shortcut guide", "Help");
        Add("help.commands", "Command palette", "Help");
        Add("settings.shortcuts", "Keyboard shortcuts settings", "Settings");
        Add("view.settings", "Show Settings", "Workspace");
        Add("view.collapseTrees", "Collapse Pages and Takeoffs trees", "View");
        Add("edit.mirrorHorizontal", "Mirror selected objects horizontally", "Edit / Transform", KeyboardCommandContext.Viewport);
        Add("edit.mirrorVertical", "Mirror selected objects vertically", "Edit / Transform", KeyboardCommandContext.Viewport);
        Add("edit.rotateLeft", "Rotate selected objects 90° left", "Edit / Transform", KeyboardCommandContext.Viewport);
        Add("edit.rotateRight", "Rotate selected objects 90° right", "Edit / Transform", KeyboardCommandContext.Viewport);
        Add("edit.undo", "Undo viewport action / last point", "Edit");
        Add("edit.selectAll", "Select all objects on this sheet", "Edit");
        Add("edit.delete", "Delete selected measurements / markups", "Edit");
        Add("edit.rename", "Rename selected object's takeoff", "Edit");
        Add("drawing.complete", "Complete current drawing", "Drawing");
        Add("drawing.cancel", "Cancel active drawing / editing", "Drawing");
        Add("drawing.cycleTrace", "Cycle edge snap / layer trace candidate", "Drawing");
        Add("drawing.advanceTrace", "Advance layer trace", "Drawing");
        Add("tool.toggleBox", "Toggle Box mode", "Tools");
        foreach (string group in new[] { "pages", "takeoffs" })
        {
            string category = group == "pages" ? "Pages tree" : "Takeoffs tree";
            foreach (var (suffix, title) in new (string, string)[] {
                ("copy", "Copy selection"), ("cut", "Cut selection"), ("paste", "Paste"),
                ("duplicate", "Duplicate selection"), ("moveUp", "Move selection up"),
                ("moveDown", "Move selection down"), ("undoDelete", "Restore last deletion"),
                ("rename", "Rename selection"), ("delete", "Delete selection") })
                Add(group + "." + suffix, title, category);
        }
        Add("pages.clearSelection", "Clear Pages selection", "Pages tree");
        Add("takeoffs.showSections", "Select section measurements on canvas", "Takeoffs tree");
        Add("pages.undoOperation", "Undo last page operation", "Pages");
        Add("pages.undoSort", "Undo last page sort", "Pages");
        Add("bookmarks.open", "Open selected bookmark", "Bookmarks");
        Add("bookmarks.delete", "Delete selected bookmark", "Bookmarks");
        Add("inbox.open", "Open selected AI Inbox entry", "AI Inbox");
        foreach (string kind in new[] { "ridge", "hip", "valley", "eave", "rake", "pitch", "cancel", "undo" })
            Add("roof." + kind, "Roof guide: " + kind, "3D roof guide");
        foreach (var (suffix, title) in new (string, string)[] { ("left", "Move left"), ("right", "Move right"),
            ("up", "Move up"), ("down", "Move down"), ("scaleUp", "Scale up"), ("scaleDown", "Scale down"),
            ("rotateLeft", "Rotate left"), ("rotateRight", "Rotate right"), ("reset", "Reset transform") })
        {
            Add("overlay." + suffix, title, "Sheet overlay");
            Add("overlay.fine" + char.ToUpperInvariant(suffix[0]) + suffix[1..], title + " (fine)", "Sheet overlay");
        }
        foreach (KeyboardCommandDefinition command in _customShortcuts.PickedCommands)
            _keyboardCommands.TryAdd(command.Id, command);
        DiscoverKeyboardSurfaceCommands();
        foreach (Window window in Application.Current.Windows)
            if (IsScopedKeyboardWindow(window) && BelongsToKeyboardOwner(window)) DiscoverScopedWindowKeyboardCommands(window);
    }

    private bool HandleCustomKeyboardShortcut(object sender, KeyEventArgs e)
    {
        if (!_customShortcutSettingsLoaded) LoadCustomKeyboardShortcuts();
        if (_customShortcuts.Overrides.Count == 0)
            return false;
        bool textInput = ShouldSkipTakeoffShortcut(e.OriginalSource as DependencyObject);
        KeyboardCommandContext context = ResolveKeyboardContext(sender, e.OriginalSource as DependencyObject);
        string chord = KeyboardShortcutGesture.FromKey(KeyboardShortcutKeys.EffectiveKey(e), Keyboard.Modifiers);
        string sequence = DateTime.UtcNow - _customShortcutSequenceUtc <= GlobalShortcutSequenceTimeout && _customShortcutSequence.Length > 0
            ? _customShortcutSequence + ", " + chord : chord;
        _customShortcutSequenceUtc = DateTime.UtcNow;

        PdfViewport focusedViewport = sender is DetachedSheetWindow detached ? detached.Viewport : _viewport;
        var applicable = _keyboardCommands.Values.Where(command => KeyboardCommandBelongsToInputWindow(command, sender) &&
            IsKeyboardContextApplicable(command, context, focusedViewport) &&
            (!textInput || KeyboardShortcutGesture.CanRunWhileTyping(KeyboardShortcutKeys.EffectiveKey(e), Keyboard.Modifiers) &&
                (command.Id is "file.open" or "file.openRecent" or "file.save" or "file.saveAs" or "help.commands"))).ToArray();
        foreach (KeyboardCommandDefinition command in applicable)
        {
            if (!_customShortcuts.Overrides.TryGetValue(command.Id, out List<string>? bindings)) continue;
            if (!bindings.Any(binding => binding == chord || binding == sequence)) continue;
            ClearGlobalShortcutSequence();
            _customShortcutSequence = "";
            if (!e.IsRepeat) ExecuteCustomKeyboardCommand(command.Id, sender);
            return true;
        }
        string[] overrides = applicable.Where(command => _customShortcuts.Overrides.ContainsKey(command.Id))
            .SelectMany(command => _customShortcuts.Overrides[command.Id]).ToArray();
        if (overrides.Any(binding => binding.StartsWith(sequence + ", ", StringComparison.Ordinal)))
        { _customShortcutSequence = sequence; return true; }
        if (overrides.Any(binding => binding.StartsWith(chord + ", ", StringComparison.Ordinal)))
        { _customShortcutSequence = chord; return true; }
        string[] original = applicable.SelectMany(command => command.Defaults).ToArray();
        _customShortcutSequence = original.Any(binding => binding.StartsWith(sequence + ", ", StringComparison.Ordinal)) ? sequence :
            original.Any(binding => binding.StartsWith(chord + ", ", StringComparison.Ordinal)) ? chord : "";
        // Suppress only the overridden default in its original focus context. All other old routes stay intact.
        return applicable.Any(command => _customShortcuts.Overrides.ContainsKey(command.Id) &&
            command.Defaults.Any(binding => binding == chord || binding == sequence));
    }

    private KeyboardCommandContext ResolveKeyboardContext(object sender, DependencyObject? source)
    {
        if (sender is DetachedSheetWindow) return KeyboardCommandContext.Viewport;
        for (DependencyObject? current = source; current != null; current = GetShortcutParent(current))
        {
            if (ReferenceEquals(current, PagesTree)) return KeyboardCommandContext.Pages;
            if (ReferenceEquals(current, TakeoffsTree)) return KeyboardCommandContext.Takeoffs;
            if (current is PdfViewport) return KeyboardCommandContext.Viewport;
            if (ReferenceEquals(current, ObservationsListView)) return KeyboardCommandContext.Inbox;
            if (ReferenceEquals(current, _bookmarksController?.KeyboardList)) return KeyboardCommandContext.Bookmarks;
        }
        return KeyboardCommandContext.Workspace;
    }

    private static bool IsKeyboardContextApplicable(KeyboardCommandDefinition command, KeyboardCommandContext active, PdfViewport viewport)
    {
        if (command.Context == KeyboardCommandContext.Roof) return active == KeyboardCommandContext.Viewport && viewport.IsKeyboardRoofContext;
        if (command.Context == KeyboardCommandContext.Overlay) return active == KeyboardCommandContext.Viewport && viewport.HasKeyboardSheetOverlay;
        if (viewport.IsKeyboardRoofContext && active == KeyboardCommandContext.Viewport &&
            (command.Id is "drawing.cancel" or "edit.undo" || command.Id.StartsWith("tool.", StringComparison.Ordinal) && command.Context == KeyboardCommandContext.Viewport))
            return false;
        return command.Context is KeyboardCommandContext.Workspace or KeyboardCommandContext.Surface || command.Context == active;
    }

    private void ExecuteCustomKeyboardCommand(string id, object sender)
    {
        PdfViewport viewport = sender is DetachedSheetWindow detached ? detached.Viewport : _viewport;
        if (id is "edit.mirrorHorizontal" or "edit.mirrorVertical" or "edit.rotateLeft" or "edit.rotateRight" &&
            !EnsureCurrentJobWritable("transform selected objects")) return;
        if (id.StartsWith("surface:", StringComparison.Ordinal)) { ExecuteKeyboardSurfaceCommand(id, sender); return; }
        if (viewport.ExecuteCustomKeyboardCommand(id)) return;
        if (ExecuteTreeKeyboardCommand(id)) return;
        switch (id)
        {
            case "help.shortcuts": ToggleShortcutsOverlay(); return;
            case "help.commands": ShowCommandPalette(); return;
            case "settings.shortcuts": ShowKeyboardShortcutSettings(); return;
            case "view.settings": SelectWorkspaceTab("SettingsManager"); return;
            case "view.collapseTrees": CollapseProjectTreeDisplaysWithStatus(); return;
            case "pages.undoOperation": UndoLastPageOperation(); return;
            case "pages.undoSort": UndoLastPageOperation("page-sort"); return;
            case "bookmarks.open": case "bookmarks.delete": _bookmarksController?.ExecuteKeyboardCommand(id); return;
            case "inbox.open": OpenSelectedInboxObservation(); return;
            case "tool.drawLine":
                if (!TryStartExtraJoistShortcut()) SetTool("drawline");
                return;
            case "tool.toggleRecord": BtnActiveTakeoffRecord_Click(this, new RoutedEventArgs()); return;
        }
        CommandPaletteItem? item = BuildCommandPaletteItems().FirstOrDefault(item => item.Id == id);
        if (item?.CanExecute != true)
        {
            TxtStatus.Text = item?.DisabledReason ?? "This command is unavailable in the current workspace.";
            return;
        }
        ExecuteCommandPaletteItem(id);
    }
}
