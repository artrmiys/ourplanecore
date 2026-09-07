# Current OurPlanCore Status

Last verified: **2026-09-06**, application source **2.2.7-preview, `42c44b0`**.
This page describes the current implementation. Dated development reports retain
historical results; an old “missing” list is not the current backlog.

## Version and evidence

| Role | Verified state |
| --- | --- |
| Stable installation | 2.2.5+5d46e11; preserved. |
| Previous isolated Preview | 2.2.6-preview+3f290bd; preserved with its profile. |
| Current isolated Preview | 2.2.7-preview+42c44b057eb5b9d22c4919fc94c840632964155b, .NET 9 x64. |
| Published/installed EXE | 174,644,992 bytes; SHA256 `94DF767F5D51C07D7A603749B50C97F98384E0024EA6692913128D6ED7821A41`. |
| Delivery identity | [delivery-227.json](../../delivery-227.json), [QA report](../../QA-REPORT-227.md), [fresh runtime](../../runtime-227.json). |
| Final integrated checks | 807/807 C# and 29/29 Python; build 0 warnings / 0 errors. Exact logs are linked from the delivery/strategy evidence. |
| Real-work evidence | [Final performance comparison](../../PERFORMANCE-COMPARISON-227-FINAL.md): 12 sequential runs, 3 per variant/project, on 84-sheet and 214-sheet project copies. Native 214-sheet proof has renderer commit ae2a8ee; the final 42c44b0 change fixes compressed-EXE profile marker resolution without changing renderer/store/exporter. |
| .NET 10 | Separate experiment only. Excel migration gates did not pass; the main candidate remains .NET 9. |

The stable update folder, old shortcuts and running previous Preview were not
replaced. This work did not publish a public release or change external Excel
workbooks. Local evidence may contain project-specific information and is not a
set of public KB illustrations.

[Strategy evidence](STRATEGY_APP_EVIDENCE_2026_09_06.md) maps the implemented
slices to the [master plan](OURPLANCORE_MASTER_REMEDIATION_AND_RELEASE_PLAN.md).
Later master phases remain open unless their own acceptance is satisfied.

## Workspace and modules

The core workspace is Pages → PDF viewport → Takeoffs, with an active-target
Record bar, tools and scale/zoom controls. Optional estimating/templates,
bookmarks, layers, overlays and AI panels serve the active sheet. Visible
workspace labels are unnumbered: Main View and Settings form the core pair;
Sheet Manager, Takeoff Manager, Report Builder, Materials, AI Manager and 3D
appear when enabled.

There are **six** top ribbon tabs: Main, Page, Annotation, PDF Output, Viewport,
Display. PDF Output is created at runtime by `InstallOutputSettingsTab`, not
written as a static MainWindow.xaml TabItem. Screen and export appearance have
separate controls. See the [current command map](60-ux-ui/WORKSPACE_TAB_COMMAND_MAP.md).

The [module catalog](../Models/ModuleFeatureConfig.cs) has 18 modules. By default,
Sheet Manager, Takeoff Manager, Report Builder, Materials, AI and 3D are off;
the other 12 are on. Off hides/disables related commands without deleting data.
Modules supports presets, temporary Apply, global/job save and override removal.

## Projects, storage and recovery

- The unified project picker opens `.ourplan` files and existing folder jobs.
  New blank/from-PDF projects use `.ourplan`. Open / Import also offers import
  into the current job, the separate PlanSwift converter, sample creation and
  job-folder management.
- **Save As** is one context-sensitive command (Main ribbon, menu, palette,
  Ctrl+Shift+S). A package remains a package; a folder job remains a folder.
  Folder Save As persists/copies durable content and opens the copied project.
- A package has a managed local working copy and writer ownership. Save/close
  must resolve dirty data, serialization failures and read-only transitions;
  success is not inferred merely from a changed quantity on screen.
- Durable records use validated reads and atomic writes. Missing, malformed,
  incompatible and inaccessible data are distinguished. Protected unreadable
  records must not be silently replaced by empty/default data.
- **Open / Import → Project Data Recovery...** exposes explicit recovery.
  Metadata snapshots/journals and recoverable tree deletions serve different
  purposes; none substitutes for a complete project backup including sources.
- **Undo Last Page Sort** and **Undo Last Page Operation** are explicit operation
  recovery actions. Ctrl+Z in a tree restores its deletion; Ctrl+Z in a viewport
  is geometry Undo. Do not conflate these histories.
- Package-relative paths are rebased within the project boundary. Path traversal,
  unsafe IDs and junction escapes remain rejected. Repair Links uses recorded
  source/page identity where it can resolve a link uniquely; ambiguous legacy
  links still require review, not guessing.
- **Settings → Project Storage** provides read-only size/reference analysis and
  a compact preview. Confirmed compaction reformats valid raster snap JSON; it
  does not delete source PDFs or takeoff records.
- AppIdentity chooses version-specific profile roots, including the marker next
  to the actual compressed EXE. Global presets, app settings and job overrides
  are distinct. Avoid universal historical `%APPDATA%` paths in instructions.

Sources: [project package](../MainWindow.ProjectPackage.cs),
[Open / Import](../MainWindow.OpenImportMenu.cs),
[data reader](../Models/Storage/DataFileReader.cs),
[journal](../Models/Storage/JobOperationJournal.cs),
[identity](../Models/AppIdentity.cs).

## Pages, trees and sheet ownership

- Pages has folders, page tabs, per-page scale, saved image transforms, PDF layers,
  sheet overlays, bookmarks and metadata review. Copy/paste/duplicate preserves
  exact visible page names; unique internal folders do not add `Copy` or `(2)`
  to the UI. Takeoffs follows the same visible-name rule.
- Page tabs remember page/view context. With Detached Sheets enabled, pages open
  in independent windows and Tile M2 can arrange them. Activating a detached
  window makes it the target for Pages navigation until the main canvas is
  activated. Main and detached commands must use their own page/viewport/owner.
- A newly opened job ends with collapsed trees. Rebuild/reload during the session
  preserves tracked user expansion; selecting a page/measurement may expand its
  ancestors. The minus shortcut explicitly collapses both trees.
- Pages/Takeoffs support range/toggle selection, move/copy/cut/paste/duplicate,
  bulk properties and drag/drop. Selection synchronizes with active-sheet
  geometry; selecting a takeoff can reveal its measured pages. Selection and
  recording target are separate states.
- Items contain measurements/sections, metadata, prices and notes. Folder
  defaults propagate to new matching items. Page-linked legend ordering is
  per page; it does not reorder the global takeoff hierarchy.
- Page-relative scale and saved measurement scale are both retained. Count does
  not require scale; scale-dependent drawing is blocked/prompted when missing.
  An unscaled page is visibly identified.

Sources: [tree expansion](../MainWindow.TreeExpansion.cs),
[page tabs](../MainWindow.PageTabs.cs), [detached sheets](../MainWindow.DetachedSheets.cs),
[takeoff properties](../MainWindow.TakeoffsProperties.cs).

## Drawing, editing and repeated work

- Core Count, Line and Area have fixed compatible targets, Properties/Record
  checks, intentional finish/cancel, snap, Ortho, Box and direct editing.
  Count supports **seven** symbols: circle, cross, square, star, triangle,
  diamond, ring. Symbols/colours are shared by viewport, trees, legend and PDF.
- Ruler, Pitch and markup tools are annotation data, separate from takeoff quantities.
  Highlight, Draw Line, Arrow, Box, Cloud, Area and Note support their applicable
  selection/edit/delete workflows.
- Joist Area generates count/LF from spacing, direction, pitch and order-length
  rules. Extra Joists supports continuous D placement in a selected Joist Area.
  **Move joist note** is opt-in in properties; Select-mode table drag saves
  per-area position, supports cancel/Undo and works in detached sheets/PDF output.
- Beam and Openings create Count items from measured dimensions. They work on
  detached pages with that page's scale. **Repeat Beam** remains armed after
  each accepted item; **Repeat Line** creates separate two-point measurements.
  Esc, cancelled Beam dialog, tool/target/page change or read-only transition
  ends repeat. Ordinary Line remains a connected polyline.
- P Line creates Count points along existing Line geometry. Multiline supports
  companion offset lines. Merge/Split routes selected segments to existing/new
  takeoffs; Combine provides Union/Subtract/Intersect/Remove Overlap/Divide.
- Select supports grouped body movement and vertex/handle editing. Transform
  mirrors/rotations/scaling act on selected measurements. Page Rotate/Flip and
  nonzero Level instead create/assign a raster PDF variant and transform that
  page's geometry; they are saved changes, not camera-only view toggles.
- Similar uses offline image/text-guided matching with threshold/review,
  exclusions, rotations/mirroring and optional AI recheck. **Search all sheets
  in this job** is opt-in and scans at most 80 other sheets; status reports skips.
  A current-sheet-only limitation is obsolete, but unlimited exhaustive batch
  recognition is not implemented.

Sources: [Beam/Openings](../MainWindow.BeamTool.cs),
[repeat](../Controls/PdfViewport.RepeatDrawing.cs),
[Similar sweep](../MainWindow.SimilarCount.OtherSheets.cs),
[Joist note editing](../Controls/PdfViewport.JoistNoteEditing.cs).

## Copy/paste

Measurement Copy/Paste uses a common main/detached path. **Paste Measurements**
offers **Same takeoffs**, **New takeoffs**, **Cancel**. Same reuses source items
or recreates missing ones; New creates separate items with the same visible
names/properties. Geometry, holes, notes, symbols and Joist/Extra Joist data
are retained; new measurements receive fresh IDs.

The copied bounds' **upper-left corner**, not center, moves to the pointer/menu
paste location. Geometry keeps its paper-coordinate size; a scaled destination
uses its own scale. An unscaled destination asks to reuse copied measurement
scale; missing usable scale blocks Line/Area paste. There is no additional
placement/physical-size policy selector in this dialog.

Paste preserves viewport position, selects the exact pasted rows, refreshes
other open viewports and participates in one Undo path. Undo removes newly
created empty takeoff items. Cancellation/read-only failure must leave neither
partial pasted data nor stale detached geometry. Mixed measurement/cutout
paste performs preflight/reservation and shares the completed Undo boundary.

Sources: [common paste](../MainWindow.MeasurementClipboard.cs),
[Undo](../MainWindow.MeasurementClipboard.Undo.cs),
[dialog](../Dialogs/MeasurementPasteModeDialog.cs),
[real-project proof](../../P06-CLIPBOARD-REPORT.md).

## PDF, rendering and output

- Vector PDF, scanned/image sheets and prepared raster-first sheets are supported.
  Takeoff Snap and PDF Snap are separate; prepared raster snap indexes support
  black-line snapping. Contour/layer trace still needs review on noisy drawings.
- PDF Layers supports visibility/highlighting/Layer Trace. Sheet Overlay supports
  aligning/translating/scaling/rotating another sheet with opacity and fine steps.
- Raster, PDFium and Python/PyMuPDF layer paths are selected by mode/available
  data. Persistent previews, RAM caches and detail rendering reduce repeated
  work. Cache accounting budgets are not strict process-memory limits.
- The current renderer keeps the same zoom sampling after motion stops, uses
  immutable cached bitmap masters/leases and preserves a repaint requested
  during painting. The final real-work comparison is authoritative; historical
  1-ms synthetic/high-cache figures are not general guarantees.
- **PDF Output → Preview** opens the current main/detached sheet in a nonmodal
  window. Output changes update it live while preserving zoom/pan. Wheel zoom
  anchors to cursor; right-button pan, Fit and Save PDF are available. Esc stops
  active panning; the window close control closes the preview.
  Preview remains pinned to the opened sheet; reopen it for another page.
- Main → Export writes selected/all sheets. Output line/point/edge/fill sizes,
  label categories, legend/header and Meas/Markups/Extra glow settings are separate
  from Viewport/Display screen settings.

Sources: [PDF Output](../MainWindow.OutputSettings.cs),
[preview](../MainWindow.PdfOutputPreview.cs),
[render cache](../Controls/PdfViewport.RenderCache.cs),
[performance evidence](STRATEGY_APP_EVIDENCE_2026_09_06.md).

## Estimating, automation and optional modules

Estimating shows item/section quantities, units, notes, price/cost and a
current-sheet filter. CSV, TXT, Excel and Current Excel export routes exist.
Current Excel writes into an already open workbook; ordinary row export does
not save that workbook automatically. Excel macro actions are a separate,
configurable workflow, not a guarantee that any external workbook passes its
macro gates. See [Excel workflow contract](30-takeoffs-measurements/EXCEL_MACRO_EXPORT_WORKFLOW_2026_07_29.md).

Page Folders, Auto Tree, From Pages and Takeoff Templates provide editable
structure/routing. PDF-first Auto Name/Scale exposes confidence, reasons,
warnings and checked-row application. AI Fill provides an optional external
fallback with stored evidence and crop/layout hints.

Materials extracts local PDF/OCR evidence and report sheets. Report Builder is
TemplateCom-specific. AI Manager queues/runs/reviews crop-based requests;
quantities require accepted action application. Core takeoff is local, while
explicit AI requests send supplied crop/context to OpenAI.

Current 3D uses deterministic wall/roof/per-edge/rafter tools and a viewer.
**Legacy AI massing is archived and disabled**; Build 3D Draft/Accept 3D and its
old roadmap must not be advertised as the active workflow.

## Settings and keyboard customization

The 12 categories are Modules, Page Folders, Auto Tree, From Pages, Takeoff
Templates, Sort A/S, Sort D/Sec/WT, Auto Rename / Scale, Excel Actions,
Project Storage, Keyboard Shortcuts and Defaults.

Save behavior is editor-specific. Modules offers temporary Apply and global/job
Save. Page Folders/Auto Tree immediately save global changes and an existing job
override. Several Defaults controls persist immediately; keyboard edits remain
in a draft until Save. Full job background warmup is off by default.

**Settings → Keyboard Shortcuts → Open Keyboard Shortcuts...** opens a separate
searchable editor: assign/remove/reset, conflicts, sequences, global/job scope,
import/export, UI command picking and protected-file recovery. Original keys
remain defaults. Both measurement mirrors are assignable with no original key.
Typing, mouse modifiers, modal scope and disabled/read-only rules remain intact.

Installed acceptance observed **605 commands / 0 changed**; the test workspace
observed 613. Catalog discovery is contextual. Neither number proves that every
possible UI command was individually executed. See the [shortcut guide](60-ux-ui/KEYBOARD_SHORTCUTS.md)
and [local installed acceptance](../../artifacts/shortcuts/README.md).

## Remaining limits and next work

- `.NET 10` migration is blocked on the separate Excel gate; do not present the
  experiment as the installed main runtime.
- No full PlanSwift XML/assemblies/formula-database parity or separate X/Y page
  scales is claimed. Implemented Joists, Beam/Openings, repeated Line and detached
  sheets must not be listed as entirely missing specialty tools.
- Similar's bounded cross-sheet scan, contour trace, metadata inference and AI
  outputs require review; confidence is not acceptance.
- Whole-process long-session memory, data-bound tree architecture, accessibility
  and later master phases need their own acceptance. Current narrow fixes do not
  close these programs of work.
- Older massing/user-flow research documents preserve product history. Use this
  page and the command map for current availability; use the master plan for
  priorities rather than reviving a superseded “next code step.”

## Documentation maintenance

Update user-facing KB and these two maps when command labels, default keys,
module visibility or save boundaries change. Verify runtime-created controls as
well as XAML; distinguish implemented behavior, tested scenarios and future
contracts. Public screenshots must be sanitized or honestly labelled historical.
This documentation refresh changes no source behavior and requires no new app
build; the release evidence above remains the evidence for that binary.
