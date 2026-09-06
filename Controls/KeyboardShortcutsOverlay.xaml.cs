using System.Windows.Controls;

namespace OurPlanCore.Controls;

public partial class KeyboardShortcutsOverlay : UserControl
{
    public KeyboardShortcutsOverlay()
    {
        InitializeComponent();
        DataContext = new { Columns = KeyboardShortcutCatalog.Columns };
    }

    internal void RefreshAssignments(KeyboardShortcutConfiguration config, IEnumerable<KeyboardCommandDefinition> commands)
    {
        if (config.Overrides.Count == 0)
        {
            DataContext = new { Columns = KeyboardShortcutCatalog.Columns };
            return;
        }
        var sections = commands.Where(command => config.Effective(command).Count > 0)
            .GroupBy(command => command.Category).OrderBy(group => group.Key)
            .Select(group => new KeyboardShortcutHelpSection(group.Key,
                group.Select(command => new KeyboardShortcutHelpItem(
                    string.Join(" / ", config.Effective(command).Select(KeyboardShortcutGesture.Display)),
                    command.Title, command.Context.ToString())).ToArray())).ToList();
        sections.AddRange(KeyboardShortcutCatalog.Columns.SelectMany(column => column.Sections)
            .Where(section => section.Title is "SELECTION MODIFIERS" or "DIALOGS & LISTS"));
        DataContext = new { Columns = sections.Chunk(Math.Max(1, (sections.Count + 3) / 4))
            .Select(chunk => new KeyboardShortcutHelpColumn(chunk)).ToArray() };
    }
}
