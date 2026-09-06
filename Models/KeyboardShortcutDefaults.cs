namespace OurPlanCore;

/// <summary>Defaults describe the existing handlers; unmodified profiles keep using those handlers.</summary>
public static class KeyboardShortcutDefaults
{
    public static readonly IReadOnlyDictionary<string, string[]> Keys = new Dictionary<string, string[]>
    {
        ["file.open"] = ["Ctrl+O"], ["file.openRecent"] = ["Ctrl+Shift+O"],
        ["file.save"] = ["Ctrl+S"], ["file.saveAs"] = ["Ctrl+Shift+S"],
        ["help.shortcuts"] = ["F1"], ["help.commands"] = ["Ctrl+Shift+P"],
        ["view.fit"] = ["F"], ["view.zoomIn"] = ["Ctrl+OemPlus", "Ctrl+Add"],
        ["view.zoomOut"] = ["Ctrl+OemMinus", "Ctrl+Subtract"], ["view.addBookmark"] = ["B, K"],
        ["view.collapseTrees"] = ["OemMinus", "Subtract"],
        ["tool.select"] = ["E"], ["tool.pan"] = ["V"], ["tool.scale"] = ["S"],
        ["tool.ruler"] = ["R"], ["tool.highlight"] = ["H"], ["tool.drawLine"] = ["D"],
        ["tool.note"] = ["N"], ["tool.count"] = ["P"], ["tool.line"] = ["L"],
        ["tool.area"] = ["A"], ["tool.joistArea"] = ["J"], ["tool.beam"] = ["B"],
        ["tool.openings"] = ["O"], ["tool.areaCut"] = ["X"], ["tool.toggleRecord"] = ["Space"],
        ["tool.toggleSnap"] = ["F3"], ["tool.togglePdfSnap"] = ["Ctrl+F3"],
        ["tool.toggleOrtho"] = ["F8"], ["tool.toggleBox"] = ["F9"],
        ["edit.copyMeasurements"] = ["Ctrl+C"], ["edit.pasteMeasurements"] = ["Ctrl+V"],
        ["edit.mergeMeasurements"] = ["Ctrl+M"], ["edit.splitMeasurements"] = ["Ctrl+Shift+M"],
        ["edit.undo"] = ["Ctrl+Z", "Back"], ["edit.selectAll"] = ["Ctrl+A"],
        ["edit.delete"] = ["Delete"], ["edit.rename"] = ["F2"],
        ["drawing.complete"] = ["C"], ["drawing.cancel"] = ["Escape"],
        ["drawing.cycleTrace"] = ["Tab"], ["drawing.advanceTrace"] = ["Enter"],
        ["pages.setScale"] = ["F4"], ["pages.nameScaleSetup"] = ["F5"],
        ["takeoffs.newItem"] = ["T"],
        ["pages.copy"] = ["Ctrl+C"], ["pages.cut"] = ["Ctrl+X"], ["pages.paste"] = ["Ctrl+V"],
        ["pages.duplicate"] = ["Ctrl+D"], ["pages.moveUp"] = ["Ctrl+Up"], ["pages.moveDown"] = ["Ctrl+Down"],
        ["pages.undoDelete"] = ["Ctrl+Z"], ["pages.rename"] = ["F2"], ["pages.delete"] = ["Delete"],
        ["pages.clearSelection"] = ["Escape"],
        ["takeoffs.copy"] = ["Ctrl+C"], ["takeoffs.cut"] = ["Ctrl+X"], ["takeoffs.paste"] = ["Ctrl+V"],
        ["takeoffs.duplicate"] = ["Ctrl+D"], ["takeoffs.moveUp"] = ["Ctrl+Up"], ["takeoffs.moveDown"] = ["Ctrl+Down"],
        ["takeoffs.undoDelete"] = ["Ctrl+Z"], ["takeoffs.rename"] = ["F2"], ["takeoffs.delete"] = ["Delete"],
        ["takeoffs.showSections"] = ["Ctrl+Enter"],
        ["bookmarks.open"] = ["Enter"], ["bookmarks.delete"] = ["Delete"], ["inbox.open"] = ["Enter"],
        ["roof.ridge"] = ["R"], ["roof.hip"] = ["H"], ["roof.valley"] = ["V"],
        ["roof.eave"] = ["E"], ["roof.rake"] = ["K"], ["roof.pitch"] = ["P"],
        ["roof.cancel"] = ["Escape"], ["roof.undo"] = ["Back", "Ctrl+Z"],
        ["overlay.left"] = ["Ctrl+Alt+Left"], ["overlay.right"] = ["Ctrl+Alt+Right"],
        ["overlay.up"] = ["Ctrl+Alt+Up"], ["overlay.down"] = ["Ctrl+Alt+Down"],
        ["overlay.scaleUp"] = ["Ctrl+Alt+OemPlus", "Ctrl+Alt+Add"],
        ["overlay.scaleDown"] = ["Ctrl+Alt+OemMinus", "Ctrl+Alt+Subtract"],
        ["overlay.rotateLeft"] = ["Ctrl+Alt+OemOpenBrackets"], ["overlay.rotateRight"] = ["Ctrl+Alt+OemCloseBrackets"],
        ["overlay.reset"] = ["Ctrl+Alt+D0", "Ctrl+Alt+NumPad0"],
        ["overlay.fineLeft"] = ["Ctrl+Alt+Shift+Left"], ["overlay.fineRight"] = ["Ctrl+Alt+Shift+Right"],
        ["overlay.fineUp"] = ["Ctrl+Alt+Shift+Up"], ["overlay.fineDown"] = ["Ctrl+Alt+Shift+Down"],
        ["overlay.fineScaleUp"] = ["Ctrl+Alt+Shift+OemPlus", "Ctrl+Alt+Shift+Add"],
        ["overlay.fineScaleDown"] = ["Ctrl+Alt+Shift+OemMinus", "Ctrl+Alt+Shift+Subtract"],
        ["overlay.fineRotateLeft"] = ["Ctrl+Alt+Shift+OemOpenBrackets"], ["overlay.fineRotateRight"] = ["Ctrl+Alt+Shift+OemCloseBrackets"],
        ["overlay.fineReset"] = ["Ctrl+Alt+Shift+D0", "Ctrl+Alt+Shift+NumPad0"],
    };

    public static IReadOnlyList<string> For(string id) => Keys.TryGetValue(id, out string[]? keys) ? keys : [];

    public static KeyboardCommandContext ContextFor(string id) => id switch
    {
        "pages.copy" or "pages.cut" or "pages.paste" or "pages.duplicate" or "pages.moveUp" or
        "pages.moveDown" or "pages.undoDelete" or "pages.rename" or "pages.delete" or "pages.clearSelection" => KeyboardCommandContext.Pages,
        "takeoffs.copy" or "takeoffs.cut" or "takeoffs.paste" or "takeoffs.duplicate" or "takeoffs.moveUp" or
        "takeoffs.moveDown" or "takeoffs.undoDelete" or "takeoffs.rename" or "takeoffs.delete" or "takeoffs.showSections" => KeyboardCommandContext.Takeoffs,
        "view.fit" or "view.zoomIn" or "view.zoomOut" or "edit.copyMeasurements" or "edit.pasteMeasurements" or
        "edit.undo" or "edit.selectAll" or "edit.delete" or "edit.rename" => KeyboardCommandContext.Viewport,
        "tool.drawLine" or "tool.toggleRecord" => KeyboardCommandContext.Workspace,
        "bookmarks.open" or "bookmarks.delete" => KeyboardCommandContext.Bookmarks,
        "inbox.open" => KeyboardCommandContext.Inbox,
        _ when id.StartsWith("roof.", StringComparison.Ordinal) => KeyboardCommandContext.Roof,
        _ when id.StartsWith("overlay.", StringComparison.Ordinal) => KeyboardCommandContext.Overlay,
        _ when id.StartsWith("tool.", StringComparison.Ordinal) || id.StartsWith("drawing.", StringComparison.Ordinal) => KeyboardCommandContext.Viewport,
        _ => KeyboardCommandContext.Workspace,
    };
}
