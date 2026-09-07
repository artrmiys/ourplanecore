# Workspace Tab Command Map

Last verified: 2026-09-06 against 2.2.7-preview (`42c44b0`).

This is a map of implemented command surfaces, not a proposal to add every
manager button to the drawing workspace. Visibility follows Settings → Modules;
turning a module off hides its surfaces and blocks its commands without deleting
the project's data. Command IDs/Tag values are more stable than visible labels.

## Command surfaces

| Surface | Purpose |
| --- | --- |
| Main | Open / Import, PlanSwift, PDF Takeoffs, Save As; Export and module-dependent metadata/AI shortcuts. |
| Page | Add sheets; saved page rotate/flip/image/crop operations; naming/origin/close commands. |
| Annotation | Markup tools, width and color, when Annotations is enabled. |
| PDF Output | Export sizing, labels, overlays, include switches and live current-sheet Preview. Created programmatically by `InstallOutputSettingsTab`. |
| Viewport | On-screen measurement sizing, area opacity, ruler/extra appearance and PDF Snap Bridge. |
| Display | Label/legend/header visibility, rendering preferences, units, background and theme. |
| Drawing strip | Tools, repeat Line/Beam, selection transforms, Snap/PDF Snap/Ortho/Box, scale and zoom. |
| Main View | Pages, drawing canvas, Takeoffs, estimating/templates/bookmarks and optional panels. |
| Settings | Persistent rules, modules, defaults and separate Keyboard Shortcuts editor. |
| Command Palette | Searchable major commands, with current configured keys. |
| Keyboard Shortcuts | Editable command catalog including UI/menu actions without original bindings. |

The six top ribbon tabs are Main, Page, Annotation, PDF Output, Viewport, Display.
Annotation/PDF Output are module-dependent. The workspace tab labels are
**unnumbered**; Main View and Settings are always the core pair. Optional manager
workspace tabs appear when their modules are enabled:

| Visible workspace | Stable Tag | Default module state |
| --- | --- | --- |
| Main View | `MainView` | Core |
| Sheet Manager | `SheetManager` | Off |
| Takeoff Manager | `TakeoffManager` | Off |
| Report Builder | `ReportBuilder` | Off |
| Materials | `MaterialsManager` | Off |
| AI Manager | `AiManager` | Off |
| 3D | `3DManager` | Off |
| Settings | `SettingsManager` | Core |

## Main View and detached sheets

Main View keeps Pages on the left, the PDF canvas in the center and Takeoffs on
the right. The active-target bar owns Record; the drawing strip is not its
second home. Core tools are Pan, Select, Scale, Count, Line and Area. Ruler, Pitch and
markups depend on Annotations. Advanced Takeoff Tools adds Similar, P Line,
Joist Area, Beam, Openings, Cut, Merge/Split and Combine. Repeat Line creates
independent two-point segments; Repeat Beam stays armed after each item dialog.

Pages exposes Open / Tile M2, folder templates, New and Setup actions, plus
Pages/PDF Layers/Overlay/Bkm panels. Name, Scale and Name+Scale at the panel
bottom work without Sheet Manager. Copy/duplicate preserves exact visible names.
Takeoffs exposes active item/Record, tree selection, properties, New Folder,
New Item, More, Export and Takeoffs/Estimating/Templates panels.

With Detached Sheets enabled, pages can open in separate windows. Clicking a
detached window makes it the Pages navigation target until the main canvas is
activated. Geometry actions, scale checks, Beam/Openings, repeat, paste, mirrors
and Undo must use that window's page and viewport. Model updates refresh other
open viewports; a redraw alone is not proof of persistent page ownership.

## Main and Page ribbons

Main JOB: **Open / Import**, **PlanSwift**, **PDF Takeoffs**, **Save As**.
Open / Import includes the unified project picker, blank/from-PDF creation,
imports into current job, operation Undo, Project Data Recovery and folder
management. Ctrl+O directly opens the picker. Save As retains the current
project format: `.ourplan` remains a package; a folder job remains a folder.

Main PDF: **Export**; **Name / Scale / Name+Scale** when Sheet Manager is enabled;
**AI Fill / Crop Hints** when their AI functionality is enabled. Metadata
shortcuts reuse the existing review/apply workflow.

| Page group | Controls |
| --- | --- |
| Add | Add Pages, Blank Sheet, PDF Takeoffs |
| Rotate | Left, Right, 180, Level, Batch Rotate |
| Flip | Vertical, Horizontal (saved page-image operation, separate from measurement mirrors) |
| Image | Invert, Copy (rendered PNG file/path), Crop New Page |
| Page | Batch Rename, Set Origin, Offset Origin, Close Page |

Rotate/Flip/Invert and nonzero Level write a new raster PDF variant, replace the
page source and transform relevant page measurements/annotations (except Invert).
Level accepts −45 to 45 degrees; zero fits the view. They are saved image
operations, not temporary camera transforms. Crop creates a separate page from
the visible viewport region.

## Screen appearance and PDF output

Viewport controls on-screen Line/Point/Edge/Fill sizing, Ruler/Extra appearance
and PDF Snap Bridge. Display controls label categories/size/scale-with-page,
legend/header display, Fast pan/zoom, PDF layers, Static image, Black vector,
units, backgrounds and Dark theme.

PDF Output is a **real top ribbon tab**, inserted at runtime. It has:

- Lines & Area: Line, Point, Edge, Fill.
- Labels: All, Line, Area, Joist, Count and Size.
- Overlays: Legend and Header sizing.
- Include: Meas, Markups, Legend, Extra glow.
- Preview: nonmodal current-sheet PDF preview with live output-setting updates,
  cursor-centered wheel zoom, right-button pan, Fit and Save PDF. Esc stops an
  active pan; close the preview using its window close control.

A preview is pinned to the page selected at opening, including the active
detached target. Reopen Preview for another page. It preserves zoom/pan while
settings refresh. Main → Export remains the selected/all-sheet export route.

## Manager workspaces

| Workspace | Implemented workflow |
| --- | --- |
| Sheet Manager | Import/Export PDF, Refresh, Analyze, Auto Name/Auto Scale/Name+Scale, AI Fill, Apply Checked, Open Sheet/JSON, Sort A/S, D/Sec/WT, Repair Links, Auto Folders and raster conversion. Review rows expose proposed/current name and scale, confidence, reasons and warnings. |
| Takeoff Manager | Save, Refresh, Set Active, Properties, Open Estimating, folder/item creation, Auto Tree, From Pages, CSV/TXT/Excel. Item/type/sections/total/unit/price/cost/notes/folder rows. |
| Report Builder | TemplateCom.xlsm table workflow: Reload, Refresh, Apply Walls. This is the COM-template-specific wall workflow, not a generic report designer. |
| Materials | Extract, Report Sheet, Refresh, JSON, Rows CSV, Summary CSV, Folder. Source PDF/page/schedule/confidence/review flags remain evidence, not automatic takeoff quantities. |
| AI Manager | AI Settings, Add/Refresh, Open Details, Go to Page, Run AI, Run New, Retry Failed, Create Set, Marker Sets, Export Markers. Reviewed draft actions require explicit acceptance/application. |
| 3D | Build: Auto/Wall/Roof Base/Select Edge/Generate Roof; Rafters: Pick Faces/Whole Roof/spacing/size; Viewer: Fit/Iso/Top/Front/Reset. Roof guides and per-edge controls support the current deterministic workflow. |

Legacy AI massing commands (Build 3D Draft, 3D From Takeoffs, Review Roof,
Review Openings, Accept 3D) are archived/disabled and are **not** current primary
actions. Historical massing docs describe previous experiments.

## Settings

The 12 category labels in source are:

Modules; Page Folders; Auto Tree; From Pages; Takeoff Templates; Sort A/S;
Sort D/Sec/WT; Auto Rename / Scale; Excel Actions; Project Storage;
Keyboard Shortcuts; Defaults.

Modules has Apply, global/job saves, presets and override removal. Keyboard
Shortcuts opens a separate dialog with explicit draft/Save behavior, conflict
review, reset, import/export and protected-file recovery. See
[Keyboard Shortcuts](KEYBOARD_SHORTCUTS.md).

Do not assume every Settings editor has the same save boundary. Page Folders
and Auto Tree edits immediately save the global template and an existing job
override. Modules Apply is temporary. Several Defaults controls persist on
change; shortcut edits wait for Save. The current editor's buttons/status must
make that boundary clear in documentation.

## Implementation and verification pointers

- [Static shell](../../MainWindow.xaml), [modules](../../MainWindow.Modules.cs),
  [18 module definitions/defaults](../../Models/ModuleFeatureConfig.cs).
- [Runtime PDF Output tab](../../MainWindow.OutputSettings.cs),
  [live preview](../../MainWindow.PdfOutputPreview.cs),
  [detached windows](../../MainWindow.DetachedSheets.cs).
- [Settings categories/persistence](../../MainWindow.SettingsManager.cs),
  [command palette](../../MainWindow.CommandPalette.cs),
  [custom commands](../../MainWindow.CustomShortcuts.cs).
- [Current status/evidence](../CURRENT_OURPLANECORE_STATUS.md).

Verify command reachability, selection, module-off and read-only behavior when
changing a surface. Source lists alone do not prove every button's runtime
acceptance. Do not derive the complete ribbon inventory from static XAML only.
