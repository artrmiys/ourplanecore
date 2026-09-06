using System.Text.Json;
using System.Windows.Input;

namespace OurPlanCore;

public enum KeyboardCommandContext { Workspace, Viewport, Pages, Takeoffs, Bookmarks, Inbox, Surface, Roof, Overlay }

public sealed record KeyboardCommandDefinition(
    string Id, string Title, string Category, KeyboardCommandContext Context,
    IReadOnlyList<string> Defaults, string Description = "");

/// <summary>Only overrides are stored. An empty list explicitly removes the old keys.</summary>
public sealed class KeyboardShortcutConfiguration
{
    public int Version { get; set; } = 1;
    public bool InheritGlobal { get; set; }
    public Dictionary<string, List<string>> Overrides { get; set; } = new(StringComparer.Ordinal);
    public List<KeyboardCommandDefinition> PickedCommands { get; set; } = [];

    public static KeyboardShortcutConfiguration BuildDefault() => new();

    public KeyboardShortcutConfiguration Clone() => new()
    {
        Version = Version,
        InheritGlobal = InheritGlobal,
        Overrides = Overrides.ToDictionary(pair => pair.Key, pair => pair.Value.ToList(), StringComparer.Ordinal),
        PickedCommands = [.. PickedCommands],
    };

    public IReadOnlyList<string> Effective(KeyboardCommandDefinition command) =>
        Overrides.TryGetValue(command.Id, out List<string>? keys) ? keys : command.Defaults;

    public void Validate()
    {
        if (Version != 1 || Overrides == null || PickedCommands == null || Overrides.Count > 10000)
            throw new JsonException("Unsupported or invalid keyboard shortcut settings.");
        foreach (var (id, keys) in Overrides)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 1024 || keys == null || keys.Count > 8)
                throw new JsonException("Invalid command or shortcut list.");
            for (int index = 0; index < keys.Count; index++)
            {
                if (!KeyboardShortcutGesture.TryNormalize(keys[index], out string normalized, out string error))
                    throw new JsonException(error);
                keys[index] = normalized;
            }
        }
        if (PickedCommands.Any(command => command == null || string.IsNullOrWhiteSpace(command.Id) ||
            !command.Id.StartsWith("surface:", StringComparison.Ordinal) || command.Id.Length > 1024 ||
            command.Title == null || command.Category == null || command.Defaults == null || !Enum.IsDefined(command.Context) ||
            command.Id.StartsWith("surface:window:", StringComparison.Ordinal) && !command.Id.Contains('|') ||
            command.Id.StartsWith("surface:context:", StringComparison.Ordinal) && !command.Id["surface:context:".Length..].Contains('/')))
            throw new JsonException("Invalid picked command.");
    }

    public void ValidateConflicts(IEnumerable<KeyboardCommandDefinition> commands)
    {
        KeyboardCommandDefinition[] catalog = commands.ToArray();
        foreach (KeyboardCommandDefinition command in catalog.Where(command => Overrides.ContainsKey(command.Id)))
        foreach (string key in Effective(command))
        {
            var conflicts = FindConflicts(command, key, catalog);
            if (conflicts.Count > 0)
                throw new InvalidOperationException($"{KeyboardShortcutGesture.Display(key)} conflicts: {command.Title} / {conflicts[0].Title}. Assign a different key or remove the conflict first.");
        }
    }

    public static bool ContextsOverlap(KeyboardCommandContext left, KeyboardCommandContext right) =>
        left == right || left is KeyboardCommandContext.Workspace or KeyboardCommandContext.Surface ||
        right is KeyboardCommandContext.Workspace or KeyboardCommandContext.Surface ||
        (left is KeyboardCommandContext.Viewport or KeyboardCommandContext.Roof or KeyboardCommandContext.Overlay) &&
        (right is KeyboardCommandContext.Viewport or KeyboardCommandContext.Roof or KeyboardCommandContext.Overlay);

    public IReadOnlyList<KeyboardCommandDefinition> FindConflicts(
        KeyboardCommandDefinition selected, string gesture, IEnumerable<KeyboardCommandDefinition> commands)
    {
        if (!KeyboardShortcutGesture.TryNormalize(gesture, out string normalized, out _)) return [];
        return commands.Where(command => command.Id != selected.Id && CommandScopesOverlap(selected, command) &&
            Effective(command).Any(value => KeyboardShortcutGesture.Conflicts(value, normalized))).ToArray();
    }

    private static bool CommandScopesOverlap(KeyboardCommandDefinition left, KeyboardCommandDefinition right)
    {
        static string? WindowScope(string id) => id.StartsWith("surface:window:", StringComparison.Ordinal) && id.Contains('|')
            ? id[..id.IndexOf('|')] : null;
        string? leftWindow = WindowScope(left.Id), rightWindow = WindowScope(right.Id);
        if (leftWindow != null || rightWindow != null) return leftWindow == rightWindow;
        return ContextsOverlap(left.Context, right.Context);
    }
}

public static class KeyboardShortcutGesture
{
    // Only the app's existing global text-safe combinations (plus function keys)
    // can intercept an editor. Plain letters, Space/Enter and native editing chords stay local.
    public static bool CanRunWhileTyping(Key key, ModifierKeys modifiers) =>
        key is >= Key.F1 and <= Key.F24 ||
        modifiers == ModifierKeys.Control && (key is Key.O or Key.S) ||
        modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && (key is Key.O or Key.S or Key.P);

    public static string FromKey(Key key, ModifierKeys modifiers)
    {
        string prefix = ((modifiers & ModifierKeys.Control) != 0 ? "Ctrl+" : "") +
            ((modifiers & ModifierKeys.Alt) != 0 ? "Alt+" : "") +
            ((modifiers & ModifierKeys.Shift) != 0 ? "Shift+" : "") +
            ((modifiers & ModifierKeys.Windows) != 0 ? "Win+" : "");
        return prefix + key;
    }

    public static bool TryNormalize(string? value, out string normalized, out string error)
    {
        normalized = "";
        error = "Press a key, optionally with Ctrl, Alt or Shift.";
        if (string.IsNullOrWhiteSpace(value) || value.Length > 180) return false;
        string[] parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length > 3) { error = "Use at most three keys in a sequence."; return false; }
        var chords = new List<string>();
        foreach (string raw in parts)
        {
            string text = raw.Replace("++", "+OemPlus", StringComparison.Ordinal)
                .Replace("+-", "+OemMinus", StringComparison.Ordinal);
            if (text == "-") text = "OemMinus";
            if (text == "+") text = "OemPlus";
            string[] tokens = text.Split('+', StringSplitOptions.TrimEntries);
            ModifierKeys modifiers = ModifierKeys.None;
            foreach (string token in tokens[..^1])
            {
                ModifierKeys modifier = token.ToLowerInvariant() switch
                { "ctrl" or "control" => ModifierKeys.Control, "shift" => ModifierKeys.Shift,
                  "alt" => ModifierKeys.Alt, "win" or "windows" => ModifierKeys.Windows, _ => ModifierKeys.None };
                if (modifier == ModifierKeys.None || (modifiers & modifier) != 0) return false;
                modifiers |= modifier;
            }
            string keyText = tokens[^1].ToLowerInvariant() switch { "esc" => "Escape", "backspace" => "Back", _ => tokens[^1] };
            if (keyText.Length == 1 && char.IsDigit(keyText[0])) keyText = "D" + keyText;
            if (!Enum.TryParse(keyText, true, out Key key) || !Enum.IsDefined(key) ||
                key is Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed or
                Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
                return false;
            if ((modifiers & ModifierKeys.Windows) != 0 || modifiers == ModifierKeys.Alt && (key is Key.F4 or Key.Tab or Key.Escape) ||
                modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && key == Key.Delete ||
                modifiers == ModifierKeys.Control && key == Key.Escape)
            { error = "This combination is reserved by Windows."; return false; }
            chords.Add(FromKey(key, modifiers));
        }
        normalized = string.Join(", ", chords);
        error = "";
        return true;
    }

    public static bool Conflicts(string left, string right)
    {
        if (!TryNormalize(left, out string a, out _) || !TryNormalize(right, out string b, out _)) return false;
        return a == b || a.StartsWith(b + ", ", StringComparison.Ordinal) || b.StartsWith(a + ", ", StringComparison.Ordinal);
    }

    public static string Display(string gesture) => gesture.Replace("OemPlus", "+", StringComparison.Ordinal)
        .Replace("OemMinus", "-", StringComparison.Ordinal).Replace("Back", "Backspace", StringComparison.Ordinal)
        .Replace("Escape", "Esc", StringComparison.Ordinal).Replace("OemOpenBrackets", "[", StringComparison.Ordinal)
        .Replace("OemCloseBrackets", "]", StringComparison.Ordinal);
}
