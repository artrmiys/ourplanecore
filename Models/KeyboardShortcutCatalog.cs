using System.Collections.Generic;

namespace OurPlanCore;

public sealed record KeyboardShortcutHelpItem(string Gesture, string Action, string Note = "");

public sealed record KeyboardShortcutHelpSection(
    string Title,
    IReadOnlyList<KeyboardShortcutHelpItem> Items);

public sealed record KeyboardShortcutHelpColumn(
    IReadOnlyList<KeyboardShortcutHelpSection> Sections);

/// <summary>
/// User-facing inventory for the F1 help surface. Entries describe shortcuts
/// that can actually be reached through the current input routing.
/// </summary>
public static class KeyboardShortcutCatalog
{
    public static IReadOnlyList<KeyboardShortcutHelpColumn> Columns { get; } =
    [
        new(
        [
            Section("GLOBAL",
                Item("F1", "Show / close this shortcut guide"),
                Item("Esc", "Close help or cancel the active action"),
                Item("Ctrl+O", "Open / import a job"),
                Item("Ctrl+Shift+O", "Open Recent Jobs"),
                Item("Ctrl+S", "Save the current job"),
                Item("Ctrl+Shift+S", "Save As OurPlan project"),
                Item("Ctrl+M", "Merge selected measurement segments"),
                Item("Ctrl+Shift+M", "Split measurements / Count marks"),
                Item("Ctrl+Shift+P", "Open Command Palette"),
                Item("Space", "Start / stop Record"),
                Item("T", "Create a new takeoff item"),
                Item("B, K", "Add a bookmark", "Press in sequence within 0.9 sec"),
                Item("F4", "Set scale on selected sheet(s)"),
                Item("F5", "Open Name / Scale"),
                Item("-", "Collapse Pages and Takeoffs trees")),
            Section("DRAWING TOOLS",
                Item("V", "Pan"),
                Item("E", "Select"),
                Item("S", "Scale"),
                Item("R", "Ruler"),
                Item("H", "Highlighter"),
                Item("D", "Draw Line / Extra Joists", "One selected Joist Area: toggle continuous mode; D or Esc exits"),
                Item("B", "Beam"),
                Item("O", "Openings"),
                Item("N", "Note"),
                Item("P", "Count"),
                Item("L", "Line"),
                Item("A", "Area"),
                Item("J", "Joist Area"),
                Item("X", "Area Cut"))
        ]),
        new(
        [
            Section("VIEWPORT & DRAWING",
                Item("C", "Complete the current drawing"),
                Item("F", "Fit sheet to viewport"),
                Item("F2", "Rename the selected object's takeoff"),
                Item("F3", "Toggle takeoff-point Snap"),
                Item("Ctrl+F3", "Toggle PDF vector Snap"),
                Item("F8", "Toggle Ortho"),
                Item("Shift", "Temporarily force Ortho", "While drawing or editing"),
                Item("F9", "Toggle Box mode"),
                Item("Ctrl++", "Zoom in"),
                Item("Ctrl+-", "Zoom out"),
                Item("Ctrl+Z / Backspace", "Undo viewport action / last point"),
                Item("Ctrl+A", "Select all objects on active sheet"),
                Item("Ctrl+C", "Copy selected measurements / markups"),
                Item("Ctrl+V", "Paste at the pointer"),
                Item("Delete", "Delete the selected object")),
            Section("SELECTION MODIFIERS",
                Item("Ctrl", "Add to / toggle selection"),
                Item("Ctrl+Shift", "Remove from selection"),
                Item("Alt", "Select vertices / handles", "Click or box-select"))
        ]),
        new(
        [
            Section("PAGES TREE",
                Item("Esc", "Clear the Pages selection"),
                Item("Ctrl+C / X / V", "Copy / cut / paste"),
                Item("Ctrl+D", "Duplicate with the exact name"),
                Item("Ctrl+Up / Down", "Move page, folder, or legend row"),
                Item("Ctrl+Z", "Restore the last Pages deletion"),
                Item("F2", "Rename page or folder"),
                Item("Delete", "Delete selected page(s) / folder(s)"),
                Item("Shift+click", "Select a range"),
                Item("Ctrl+click", "Toggle one row"),
                Item("Ctrl+Shift+click", "Add a range")),
            Section("TAKEOFFS TREE",
                Item("Ctrl+C / X / V", "Copy / cut / paste"),
                Item("Ctrl+D", "Duplicate with the exact name"),
                Item("Ctrl+Up / Down", "Move item, folder, or section"),
                Item("Ctrl+Enter", "Select section measurements on canvas"),
                Item("Ctrl+Z", "Restore the last takeoff-tree deletion"),
                Item("F2", "Rename item, folder, or section"),
                Item("Delete", "Delete selected node(s)"),
                Item("Shift / Ctrl+click", "Range / toggle selection"),
                Item("Alt+drag", "Start marquee selection from a row")),
            Section("BOOKMARKS & AI INBOX",
                Item("Enter", "Open the selected bookmark / AI entry"),
                Item("Delete", "Delete the selected bookmark"))
        ]),
        new(
        [
            Section("SHEET OVERLAY",
                Item("Ctrl+Alt+drag", "Move the overlay"),
                Item("Ctrl+Alt+Arrows", "Nudge the overlay"),
                Item("Ctrl+Alt++ / -", "Scale the overlay"),
                Item("Ctrl+Alt+[ / ]", "Rotate the overlay"),
                Item("Ctrl+Alt+0", "Reset overlay transform"),
                Item("+ Shift", "Use fine movement / scale / rotation")),
            Section("3D ROOF GUIDE MODE",
                Item("R", "Ridge guide"),
                Item("H", "Hip guide"),
                Item("V", "Valley guide"),
                Item("E", "Eave guide"),
                Item("K", "Rake guide"),
                Item("P", "Pitch guide"),
                Item("Backspace / Ctrl+Z", "Remove the last guide point"),
                Item("Esc", "Cancel guide / leave Roof mode")),
            Section("LAYER TRACE",
                Item("Tab", "Cycle edge snap or trace mode / candidate", "After Layer Trace is enabled from its toolbar"),
                Item("Enter", "Advance the trace workflow"),
                Item("Esc", "Cancel trace step / leave Layer Trace")),
            Section("DIALOGS & LISTS",
                Item("Enter", "Accept / open the selected result"),
                Item("Esc", "Close the active dialog"),
                Item("Up / Down", "Navigate palette / picker results"))
        ])
    ];

    public static IEnumerable<KeyboardShortcutHelpItem> AllItems()
    {
        foreach (KeyboardShortcutHelpColumn column in Columns)
        foreach (KeyboardShortcutHelpSection section in column.Sections)
        foreach (KeyboardShortcutHelpItem item in section.Items)
            yield return item;
    }

    private static KeyboardShortcutHelpSection Section(
        string title,
        params KeyboardShortcutHelpItem[] items) => new(title, items);

    private static KeyboardShortcutHelpItem Item(
        string gesture,
        string action,
        string note = "") => new(gesture, action, note);
}
