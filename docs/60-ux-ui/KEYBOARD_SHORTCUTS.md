# Keyboard Shortcuts

Last verified: 2026-09-06 against 2.2.7-preview (`42c44b0`).

These tables describe **original defaults**, using English key labels. Global/job
configuration can change or remove them. F1 help and Command Palette display
configured keys; the settings editor contains the editable command catalog.
Module availability, focus, selection and read-only access still apply.

## Assign or change keys

Open **Settings → Keyboard Shortcuts → Open Keyboard Shortcuts...**.

1. Search by command name, description, category or key; select a row.
2. Focus **New shortcut**, press a combination, then **Assign**. Enable
   **Sequence** for up to three successive keystrokes, such as `B, K`.
3. Review conflicts. Reassigning an occupied combination requires confirmation
   and removes it from conflicting commands in overlapping contexts.
4. Choose **Save global default** or **Save for this job** (requires a writable
   job). **Use global for this job** removes the job override.

Existing keys remain active until Save. **Remove keys** explicitly unbinds the
selected command. **Reset command** and **Reset all to original** restore the
draft's defaults; Save is still required. **Clear capture** only clears the
new-key input. **Import preset...** loads a draft from JSON;
**Export preset...** writes the draft to a separate file.

For geometry mirrors, search `Mirror`:

| Command | Stable ID | Original key |
| --- | --- | --- |
| Mirror selected objects horizontally | `edit.mirrorHorizontal` | Unassigned |
| Mirror selected objects vertically | `edit.mirrorVertical` | Unassigned |

These edit selected measurements in the focused main/detached viewport through
the production transform and Undo path. `Page → Flip` persistently changes the
page image and its associated geometry; it is a separate action.

## Catalog, focus and recovery

- The catalog covers registered commands, workspace controls, Pages/Takeoffs
  context menus and owned application windows, including actions without defaults.
  It is discovered from the current UI: installed acceptance saw 605 entries;
  a test workspace saw 613. These are observed contexts, not a fixed API size
  or proof that the two catalogs are identical.
- **Choose command in app...** hides the editor. Hold Ctrl while clicking to
  navigate tabs/open menus; ordinary click captures the command without executing
  it. Esc returns to the editor. Open an application dialog during picking to
  discover its controls. Data-row buttons are rejected; use the selection's
  context-menu action so a binding cannot retain an old row.
- Tree commands resolve current selection at invocation. Roof and overlay can
  overlap the viewport; custom conflicts are checked across applicable contexts.
  Different dialog scopes can reuse a key.
- In modal dialogs only that dialog's commands run. Text entry, numeric fields,
  combos, normal Enter/Escape behavior and mouse modifiers retain their existing
  semantics. This is command binding, not an OS-wide keyboard remapper.
- Modifier-only combinations and Windows-reserved keys are rejected. Unmodified
  profiles retain legacy handlers rather than rebinding defaults through a
  second command route.
- Unreadable settings show a warning and are protected from overwrite.
  **Recover settings...** offers **Retry reading**, **Restore / import...** or
  confirmed **Reset to original keys**, retaining previous bytes/recovery copies.

Resolution: job override → global → defaults. The global file is
`presets/keyboard_shortcuts.json` under the active profile's smart-context root;
the job file is `AI_Context/settings/keyboard_shortcuts.json`. Preview profiles
are separate from the stable installation; do not hard-code a user's profile path.

## Workspace defaults

| Shortcut | Action |
| --- | --- |
| F1 | Show/close keyboard help; Esc also closes it. |
| Ctrl+O | Open unified project picker. |
| Ctrl+Shift+O | Open project picker with recent projects. |
| Ctrl+S | Save current project data. |
| Ctrl+Shift+S | Save As to a new location, retaining package/folder format. |
| Ctrl+Shift+P | Open Command Palette. |
| Ctrl+M / Ctrl+Shift+M | Merge / split selected measurement segments or Count marks. |
| Space | Start/stop recording into the active takeoff target. |
| T | Create a new takeoff item. |
| B, K | Add Bookmark; successive presses within 0.9 seconds. |
| B | Beam tool after the bookmark-sequence timeout. |
| F4 | Set scale on selected sheets. |
| F5 | Open Name / Scale. |
| - | Collapse Pages and Takeoffs trees; numpad Subtract also works. |

`T` is **New takeoff** in the application window. A legacy isolated viewport
handler also contains `T → Layer Trace`, but workspace routing preempts it.
Use the **Layer Trace** control; do not teach T as a second reachable workspace
default. Similarly, F5 is Name / Scale, not AI Inbox refresh.

## Drawing defaults

| Shortcut | Tool/action |
| --- | --- |
| V / E / S / R | Pan / Select / Scale / Ruler. |
| H / D / N | Highlighter / Draw Line annotation / Note. |
| P / L / A / J | Count / Line / Area / Joist Area. |
| B / O / X | Beam / Openings / Area Cut. |

With one Joist Area selected, **D toggles continuous Extra Joists**; D or Esc
ends it. Repeat Line and Repeat Beam are separate toolbar commands without
original keys; they can be assigned. Esc ends repeat drawing.

## Viewport defaults

| Shortcut | Action |
| --- | --- |
| Esc | Cancel drawing/editing, Layer Trace or roof-guide session. |
| C | Complete current Line/Area/Cut drawing after points exist. |
| Enter | Advance an active PDF Layer Trace session. |
| Tab | Cycle edge-snap preview or active Layer Trace mode/candidate. |
| F | Fit page. |
| F2 | Rename the selected object's takeoff. |
| F3 / Ctrl+F3 | Toggle takeoff-point Snap / PDF Snap. |
| F8 / F9 | Toggle Ortho / Box mode. |
| Ctrl++ / Ctrl+- | Zoom in/out; main and numpad variants work. |
| Ctrl+Z / Backspace | Undo viewport action / last drawing point. |
| Ctrl+A | Select all measurements on active page. |
| Ctrl+C / Ctrl+V | Copy selected measurements/markups / paste at the pointer. |
| Delete | Delete selected overlay, measurement, markup or active handles. |

Mouse modifiers remain gestures: Ctrl adds/toggles selection; Ctrl+Shift removes
from selection; Alt selects vertices/handles. Shift temporarily forces Ortho.

## Pages and Takeoffs defaults

These apply when the corresponding tree has focus.

| Shortcut | Pages | Takeoffs |
| --- | --- | --- |
| Ctrl+C / Ctrl+X / Ctrl+V | Copy/cut/paste pages or folders | Copy/cut/paste items, folders or sections |
| Ctrl+D | Duplicate with exact visible name | Duplicate with exact visible name |
| Ctrl+Up / Ctrl+Down | Move page/folder or page-legend row | Move item/folder/section |
| Ctrl+Z | Restore last Pages deletion | Restore last Takeoffs deletion |
| F2 / Delete | Rename / delete selection | Rename / delete selection |
| Esc | Clear Pages selection | Context-dependent cancel |
| Ctrl+Enter | — | Select section measurements on canvas |

**Undo Last Page Sort** and **Undo Last Page Operation** are separate commands
in Open / Import and the editable catalog, without original keys. Tree Undo
is not the general operation-history command.

## Bookmarks, AI and dialogs

| Shortcut | Action |
| --- | --- |
| Enter | Open selected bookmark or AI Inbox entry in its list. |
| Delete | Delete selected bookmark. |
| Enter / Esc | Accept/run or cancel/close where the active dialog supports it. |
| Up / Down | Navigate Command Palette or Job Picker results. |
| Enter in numeric input | Commit focused scale/display/output value. |

## 3D roof guide defaults

Only while guide mode is active: **R** Ridge, **H** Hip, **V** Valley, **E** Eave,
**K** Rake, **P** Pitch. **Backspace / Ctrl+Z** removes the last guide point;
**Esc** cancels the guide, or disables guide mode when none is in progress.

## Sheet Overlay defaults

| Shortcut | Action on active sheet overlay |
| --- | --- |
| Ctrl+Alt+Left / Right / Up / Down | Move in the indicated direction (hold Ctrl+Alt). |
| Ctrl+Alt++ / Ctrl+Alt+- | Scale up/down, including numpad variants. |
| Ctrl+Alt+[ / Ctrl+Alt+] | Rotate left/right. |
| Ctrl+Alt+0 | Reset transform, main or numpad 0. |
| Add Shift to these combinations | Fine movement/scale/rotation; reset remains reset. |

## Source and verification

- [Defaults and contexts](../../Models/KeyboardShortcutDefaults.cs),
  [F1 catalog](../../Models/KeyboardShortcutCatalog.cs),
  [legacy routing](../../MainWindow.Shortcuts.cs).
- [Custom routing](../../MainWindow.CustomShortcuts.cs),
  [owned-window scope](../../MainWindow.CustomShortcuts.Windows.cs),
  [surface discovery](../../MainWindow.CustomShortcuts.Surfaces.cs).
- [Editor](../../Dialogs/KeyboardShortcutSettingsDialog.cs),
  [persistence/recovery](../../Models/KeyboardShortcutStore.cs),
  [unit checks](../../Tests/CustomKeyboardShortcutTests.cs),
  [real-project WPF checks](../../Tests/CustomShortcutUiSmokeHarness.cs).
- [Local acceptance/how-to evidence](../../../artifacts/shortcuts/README.md).
  These are local artifacts, not public screenshots or a claim that every
  contextual command was individually executed during acceptance.
