using OurPlanCore;

internal static class KeyboardShortcutCatalogTests
{
    public static void CoversReachableShortcutContexts()
    {
        List<KeyboardShortcutHelpSection> sections = KeyboardShortcutCatalog.Columns
            .SelectMany(column => column.Sections)
            .ToList();
        List<KeyboardShortcutHelpItem> items = sections
            .SelectMany(section => section.Items)
            .ToList();

        string[] requiredSections =
        [
            "GLOBAL",
            "DRAWING TOOLS",
            "VIEWPORT & DRAWING",
            "SELECTION MODIFIERS",
            "PAGES TREE",
            "TAKEOFFS TREE",
            "SHEET OVERLAY",
            "3D ROOF GUIDE MODE",
            "LAYER TRACE",
            "DIALOGS & LISTS",
        ];

        foreach (string title in requiredSections)
        {
            AssertTrue(
                sections.Any(section => string.Equals(section.Title, title, StringComparison.Ordinal)),
                $"F1 catalog is missing the {title} section");
        }

        AssertTrue(items.Count >= 70, "F1 catalog should cover the full reachable shortcut inventory");
        AssertEntry(items, "Ctrl+Shift+O", "Recent Jobs");
        AssertEntry(items, "Ctrl+Shift+M", "Split measurements");
        AssertEntry(items, "Ctrl+F3", "PDF vector Snap");
        AssertEntry(items, "Ctrl+Alt+0", "Reset overlay transform");
        AssertEntry(items, "Ctrl+Enter", "section measurements");

        KeyboardShortcutHelpItem extraJoists = items.Single(item => item.Gesture == "D");
        AssertTrue(
            extraJoists.Action.Contains("Extra Joists", StringComparison.Ordinal) &&
            extraJoists.Note.Contains("continuous mode", StringComparison.Ordinal),
            "D must explain its selected Joist Area context and continuous mode");

        List<KeyboardShortcutHelpItem> plainT = items
            .Where(item => string.Equals(item.Gesture, "T", StringComparison.Ordinal))
            .ToList();
        AssertTrue(
            plainT.Count == 1 && plainT[0].Action.Contains("new takeoff", StringComparison.OrdinalIgnoreCase),
            "F1 must not advertise the unreachable Layer Trace T route");
    }

    public static void UsesScrollableModalF1Surface()
    {
        string root = FindRepoRoot();
        string mainXaml = File.ReadAllText(Path.Combine(root, "MainWindow.xaml"));
        string overlayXaml = File.ReadAllText(Path.Combine(root, "Controls", "KeyboardShortcutsOverlay.xaml"));
        string shortcuts = File.ReadAllText(Path.Combine(root, "MainWindow.Shortcuts.cs"));

        AssertTrue(
            mainXaml.Contains("<controls:KeyboardShortcutsOverlay/>", StringComparison.Ordinal),
            "MainWindow should host the extracted F1 shortcut surface");
        AssertTrue(
            overlayXaml.Contains("<ScrollViewer", StringComparison.Ordinal) &&
            overlayXaml.Contains("ItemsSource=\"{Binding Columns}\"", StringComparison.Ordinal),
            "F1 should render the catalog in a scrollable surface");
        AssertTrue(
            shortcuts.Contains("Keep F1 modal", StringComparison.Ordinal) &&
            shortcuts.Contains("ShortcutsOverlay is { Visibility: Visibility.Visible }", StringComparison.Ordinal) &&
            shortcuts.Contains("e.Handled = true;", StringComparison.Ordinal),
            "F1 should consume background shortcuts until F1 or Esc closes it");
    }

    private static void AssertEntry(
        IEnumerable<KeyboardShortcutHelpItem> items,
        string gesture,
        string actionFragment)
    {
        AssertTrue(
            items.Any(item =>
                string.Equals(item.Gesture, gesture, StringComparison.Ordinal) &&
                item.Action.Contains(actionFragment, StringComparison.OrdinalIgnoreCase)),
            $"F1 catalog is missing {gesture}: {actionFragment}");
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ourplancore.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
