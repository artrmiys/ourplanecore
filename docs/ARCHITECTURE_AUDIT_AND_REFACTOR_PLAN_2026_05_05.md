# Architecture Audit and Refactor Plan - 2026-05-05

This is a deep engineering audit of the current WPF takeoff app after the
recent PDF layer, Layer Trace, tree-state, item-creation, export, AI, and 3D
work. The goal is not a cosmetic cleanup. The goal is to stop recurring small
bugs by reducing hidden state coupling and moving large feature clusters out of
the main window and viewport control in a safe order.

## Current Verification

Command run from `C:\Users\User\Desktop\ourplanecore`:

```powershell
dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false
```

Result:

```text
Build succeeded.
Warnings: 0
Errors: 0
```

The build is clean, so the main problem is not compiler correctness. The main
problem is architectural coupling and missing behavior-level tests. The
temporary `cache\verify_build` output keeps verification away from a running
debug executable that can lock the normal `bin` output.

## First Refactor Applied

After the audit, the first no-behavior refactor steps were applied:

- PDF layer and Layer Trace UI handlers were mechanically split out of
  `MainWindow.xaml.cs` into `MainWindow.PdfLayers.cs`.
- `Controls/PdfViewport.cs` was changed to a partial class, and Layer Trace
  session/probe/trace/overlay code was split into
  `Controls/PdfViewport.LayerTrace.cs`.
- New item vs new folder creation policy was centralized in
  `Models/TakeoffCreationPolicy.cs`, keeping the rule explicit: new takeoff
  items are created at the takeoff root, while folder creation can still use
  the selected/current folder context.
- Display and overlay settings were split out of `MainWindow.xaml.cs` into
  `MainWindow.DisplaySettings.cs`.
- Measurement and page-annotation callbacks were split out of
  `MainWindow.xaml.cs` into `MainWindow.MeasurementCallbacks.cs`.
- Viewport measurement/AI context-menu builders were split out of
  `MainWindow.xaml.cs` into `MainWindow.ViewportContextMenu.cs`.
- PDF export workflow, writer loop, and export drawing helpers were split out
  of `MainWindow.xaml.cs` into `MainWindow.PdfExport.cs`.
- Workspace manager callbacks for Sheet Manager, Takeoff Manager, AI Manager,
  and 3D Manager were split out into `MainWindow.WorkspaceManagers.cs`.
- Estimating setup, estimating window callbacks, estimate selection sync, and
  section property dialogs were split out into `MainWindow.Estimating.cs`.
- 3D Massing right-panel construction was split out into
  `MainWindow.MassingPanel.cs`.
- Pages tree workflow was split out into `MainWindow.PagesTree.cs`, including
  page tabs, page takeoff legend ordering, page move/copy/sort operations, and
  PDF metadata automation.
- Takeoffs tree workflow was first split out of `MainWindow.xaml.cs`, then
  split again into focused `MainWindow.Takeoffs*.cs` partial owners for export,
  creation, active target controls, section rows, properties dialogs, menus,
  node actions, selection helpers, clipboard, and drag/drop. The remaining
  `MainWindow.TakeoffsTree.cs` shell owns selection, context-menu opening,
  mouse selection, and drag arming.
- Shared tree, legend, totals, estimating-row, quantity, and takeoff-default
  helpers were split out into `MainWindow.TreeHelpers.cs`.
- Measurement copy/paste, paste-target resolution, and takeoff autosave helpers
  were split out into `MainWindow.MeasurementClipboard.cs`.
- Viewport scale/tool/context callbacks, AI crop/marker save helpers, marker
  overlay refresh, and context suggestion handlers were split out into
  `MainWindow.ViewportCallbacks.cs`.
- Persisted app settings, theme/background application, side-panel width
  persistence, scale UI, and small input dialogs were split out into
  `MainWindow.Utilities.cs`.
- AI Inbox display, inbox context menu, crop bookmark creation/runs, marker
  filters, and marker-set entry points were split out into
  `MainWindow.AiInbox.cs`.
- 3D Massing build/review workflow, roof/opening review, 3D viewport preview,
  marker rows, and massing preview drawing were split out into
  `MainWindow.MassingWorkflow.cs`.
- AI marker-set management, marker editing, observation actions, AI request
  execution, response/action-draft viewing, and action application were split
  out into `MainWindow.AiActions.cs`.
- Nested clipboard, page-tab, tree-node, display-row, and 3D support types were
  split out into `MainWindow.SupportTypes.cs`.
- Toolbar setup, marker filter initialization, open/new job workflows,
  persisted marker visibility, takeoff loading, measurement-link repair, and
  PDF import were split out into `MainWindow.JobLifecycle.cs`.
- Drawing tool controls, record toggle, snap/ortho state, drawing-target
  confirmation, viewport zoom/scale buttons, and scale presets were split out
  into `MainWindow.ToolControls.cs`.
- `Controls/PdfViewport.cs` was reduced to the viewport shell/public API and
  field ownership. PDF layer rendering, paint orchestration, sheet overlays,
  measurement/annotation/AI drawing, mouse/keyboard input, drawing tools,
  selection/editing, geometry, and view transform helpers were split into
  focused `Controls/PdfViewport.*.cs` partials.

The solution still builds cleanly after these splits.

Current post-split counts, measured with `rg -n "^"` after the current splits:

| File | Lines |
| --- | ---: |
| `MainWindow.xaml.cs` | 356 |
| `MainWindow.PagesTree.cs` | 3,705 |
| `MainWindow.TakeoffsTree.cs` | 329 |
| `MainWindow.TakeoffsProperties.cs` | 521 |
| `MainWindow.TakeoffsDragDrop.cs` | 500 |
| `MainWindow.TakeoffsNodeActions.cs` | 330 |
| `MainWindow.TakeoffsBulkProperties.cs` | 275 |
| `MainWindow.TakeoffsMenus.cs` | 257 |
| `MainWindow.TakeoffsActiveTarget.cs` | 249 |
| `MainWindow.TakeoffsClipboard.cs` | 245 |
| `MainWindow.TakeoffsExport.cs` | 240 |
| `MainWindow.TakeoffsCreation.cs` | 230 |
| `MainWindow.TakeoffSections.cs` | 196 |
| `MainWindow.TakeoffsSelectionHelpers.cs` | 177 |
| `MainWindow.TakeoffsJoists.cs` | 162 |
| `MainWindow.TakeoffsTreeSelection.cs` | 112 |
| `MainWindow.TakeoffsPersistence.cs` | 70 |
| `MainWindow.AiActions.cs` | 2,234 |
| `MainWindow.MassingWorkflow.cs` | 1,920 |
| `MainWindow.TreeHelpers.cs` | 1,338 |
| `MainWindow.JobLifecycle.cs` | 543 |
| `MainWindow.ToolControls.cs` | 338 |
| `MainWindow.SupportTypes.cs` | 194 |
| `MainWindow.AiInbox.cs` | 886 |
| `MainWindow.MeasurementClipboard.cs` | 361 |
| `MainWindow.ViewportCallbacks.cs` | 570 |
| `MainWindow.Utilities.cs` | 381 |
| `MainWindow.PdfLayers.cs` | 314 |
| `MainWindow.PdfExport.cs` | 735 |
| `MainWindow.WorkspaceManagers.cs` | 299 |
| `MainWindow.Estimating.cs` | 834 |
| `MainWindow.MassingPanel.cs` | 308 |
| `MainWindow.DisplaySettings.cs` | 425 |
| `MainWindow.MeasurementCallbacks.cs` | 128 |
| `MainWindow.ViewportContextMenu.cs` | 168 |
| `Controls/PdfViewport.cs` | 811 |
| `Controls/PdfViewport.MeasurementRendering.cs` | 825 |
| `Controls/PdfViewport.SelectionEditing.cs` | 496 |
| `Controls/PdfViewport.Input.cs` | 479 |
| `Controls/PdfViewport.Tools.cs` | 467 |
| `Controls/PdfViewport.Overlays.cs` | 433 |
| `Controls/PdfViewport.LayerTrace.cs` | 423 |
| `Controls/PdfViewport.Layers.cs` | 301 |
| `Controls/PdfViewport.Geometry.cs` | 199 |
| `Controls/PdfViewport.ViewTransform.cs` | 151 |
| `Controls/PdfViewport.Rendering.cs` | 81 |

## Size Metrics

Initial active source metrics before that first split, excluding `bin`, `obj`,
`reference`, `cache`, `docs`, `docs_sources`, and `publish`:

| File | Lines | Audit note |
| --- | ---: | --- |
| `MainWindow.xaml.cs` | 16,943 | Critical risk. This is a god object, not normal WPF code-behind size. |
| `Controls/PdfViewport.cs` | 3,942 | High risk. Rendering, input, PDF layers, trace, selection, drawing, and status are mixed. |
| `Models/SmartMassingDraftService.cs` | 1,887 | Large but more contained than the window. |
| `Models/SmartContextStore.cs` | 1,356 | Large persistence/context module. Needs later IO hardening. |
| `Models/OurPlaneCoreJob.cs` | 1,190 | Core job persistence and page/takeoff operations. Needs tests. |
| `Tools/pdf_layers_helper.py` | 1,103 | Large worker script mixing metadata, rendering, layer toggling, probe, and trace. |
| `MainWindow.xaml` | 935 | Heavy shell: many named controls directly bound to code-behind. |
| `Dialogs/Massing3DWindow.cs` | 857 | Feature dialog. Lower priority than main window and viewport. |
| `Models/SmartLearningStore.cs` | 723 | Persistence-heavy module. |
| `Models/PdfLayerRenderService.cs` | 684 | Python worker bridge, cache, DTOs, process lifetime, and command fallback. |

Approximate code-behind complexity:

- `MainWindow.xaml.cs`: about 96 private fields, about 895 method-like members,
  and 132 event-handler-shaped methods.
- `Controls/PdfViewport.cs`: about 71 private fields and about 202 methods.
- `MainWindow.xaml`: about 252 named XAML elements.
- Catch blocks by file include 68 in `MainWindow.xaml.cs`, 14 in
  `Models/PdfLayerRenderService.cs`, 12 in `Models/OurPlaneCoreJob.cs`, and
  20 in `Models/SmartContextStore.cs`.
- `Dispatcher.InvokeAsync` appears mostly in `MainWindow.xaml.cs` and
  `Controls/PdfViewport.cs`, which increases race risk around selection and UI
  refresh.

## High-Risk Findings

### 1. `MainWindow.xaml.cs` is too large to be reliable

The file currently owns all of these responsibilities:

- job open/create/import/export;
- PDF export drawing;
- pages tree loading, sorting, multi-select, drag/drop, and repair;
- takeoffs tree loading, selection, drag/drop, copy/paste, folder properties,
  item creation, section editing, and autosave;
- viewport tool state and measurement callbacks;
- estimating table and separate estimating window sync;
- sheet manager, takeoff manager, AI manager, and 3D manager tabs;
- PDF layer UI, Layer Trace buttons, layer context menus, and layer AI context;
- AI inbox, crop bookmarks, marker sets, AI request execution, AI action review;
- 3D massing draft generation, preview, review, and object selection;
- app settings, theme, side panel widths, labels, overlays, and display settings.

This makes it easy for an unrelated change to break another workflow. Recent
symptoms such as wrong page navigation, tree collapse, new items going into the
wrong folder, and trace UI state confusion are consistent with this structure.

### 2. Active takeoff state has multiple competing owners

The app currently has several overlapping sources of truth:

- `_activeItem`;
- `_activeTakeoffParentFolder`;
- `TakeoffsTree.SelectedItem`;
- `_viewport.ActiveTakeoffFolder`;
- multi-selection sets for takeoffs and sections;
- current page and page-tabs state.

The recent bug where a new takeoff item was created inside a folder instead of
at the root came from this exact issue: the app reused the current selection as
creation policy. The fix introduced `NewTakeoffItemParentFolder()`, but the
policy still needs to become a first-class state object with tests.

### 3. Tree expansion state is manually preserved and easy to disturb

There are explicit sets for expanded paths and suppress flags:

- `_expandedPageTreePaths`;
- `_expandedTakeoffTreePaths`;
- `_suppressTreeExpansionTracking`;
- tree reload/restore methods.

This is workable short term, but it is fragile because tree rendering,
selection sync, drag/drop, and reload all live in the same giant class. The
tree expansion policy should move into a small controller that can be tested
without the whole window.

### 4. `PdfViewport` still has coupled state even after partial splits

`Controls/PdfViewport.cs` no longer holds all behavior in one file. The first
safe split moved rendering, layer rendering, trace, input, drawing tools,
selection/editing, overlays, geometry, and view transform helpers into focused
partials. The remaining risk is shared private state across those partials:

- `_tool`, `_drawPts`, `_scalePts`, snap/ortho state, and Layer Trace state;
- `_selectedMeasurement`, `_selectedMeasurements`, drag/edit flags, and box
  selection flags;
- `_layers`, `_layerStates`, `_highlightedLayers`, and cached layer render
  state;
- `_zoom`, `_panX`, `_panY`, render scale, and repaint/rerender scheduling.

The next architectural step should be small controller/state extractions where
they can be tested without the WPF control, not another broad file move.

### 5. PDF layer behavior crosses five files without contract tests

PDF layer behavior currently crosses:

- `MainWindow.xaml`;
- `MainWindow.xaml.cs`;
- `Controls/PdfViewport.cs`;
- `Models/PdfLayerRenderService.cs`;
- `Tools/pdf_layers_helper.py`.

That is why bugs like "PDF layer off does nothing" and "trace line does not
highlight the layer" are expensive to reason about. The Python worker protocol
needs command-level tests for:

- `layers`;
- `render`;
- `layerprobe`;
- `layertrace`;
- all-layers-off pixel change;
- layer re-enable restores original render.

### 6. Many catches hide workflow bugs

The code contains many broad or bare `catch` blocks. Some are acceptable for
best-effort cleanup or malformed optional files, but many should at least
surface status, file path, or operation context.

High-priority areas:

- save/write operations;
- PDF render worker failures;
- tree copy/move/drop failures;
- AI request and response file handling;
- app settings and context store write failures.

### 7. Async/UI interleaving is not centralized

The app uses many event handlers, `async void` UI handlers, and
`Dispatcher.InvokeAsync` calls. That is normal at the edge of WPF, but not when
state transitions are spread across the whole main window. Selection and
navigation bugs are likely until state updates are routed through a smaller
state layer.

## Refactor Principle

Do not rewrite the app all at once.

The safe path is:

1. Split by feature with no behavior change.
2. Build after every split.
3. Add a smoke checklist for the workflows that have recently broken.
4. Extract single-purpose state/controllers only after the feature code is
   isolated.
5. Add tests around pure logic and Python worker commands before deeper
   rewrites.

A full rewrite would probably create more bugs than it removes because the app
already contains many working user-facing workflows.

## Target Shape

### Main window partial split

First split `MainWindow.xaml.cs` into partial files while keeping the same
class and behavior:

- `MainWindow.Lifecycle.cs`: constructor wiring, open/new job, settings apply,
  close cleanup.
- `MainWindow.PagesTree.cs`: pages tree load, selection, drag/drop, sort,
  repair, expansion.
- `MainWindow.Takeoffs*.cs`: takeoff tree shell plus export, creation,
  persistence, active target controls, section rows, properties, menus,
  node actions, selection helpers, clipboard, drag/drop, and expansion-related
  selection state.
- `MainWindow.Measurements.cs`: measurement callbacks, clipboard, autosave,
  active takeoff state.
- `MainWindow.PdfLayers.cs`: layer panel, layer visibility, layer trace UI,
  layer manifest/context actions.
- `MainWindow.PdfExport.cs`: export dialog and PDF overlay drawing.
- `MainWindow.Estimates.cs`: estimate table/window sync.
- `MainWindow.WorkspaceTabs.cs`: manager tab refresh and workspace navigation.
- `MainWindow.AiInbox.cs`: inbox, observations, AI requests, AI action draft.
- `MainWindow.Massing.cs`: 3D massing draft/review/preview/object selection.
- `MainWindow.DisplaySettings.cs`: theme, labels, overlay controls, side panel
  widths.

This should not be considered complete architecture. It is the safe first
stage that makes review possible.

### Viewport partial split

This split is now applied with no behavior change:

- `PdfViewport.Rendering.cs`: paint orchestration.
- `PdfViewport.Layers.cs`: Docnet/layer render path, layer state, cached
  layers, highlights, and layer change events.
- `PdfViewport.LayerTrace.cs`: probe, candidate cycling, trace mode, trace
  measurement creation, and trace overlay.
- `PdfViewport.Tools.cs`: ruler/draw/line/area/point tools, scale calibration,
  snap, ortho, record prompts, and drawing cancellation.
- `PdfViewport.SelectionEditing.cs`: hit testing, multi-select, vertex edit,
  measurement move, delete, page matching, and pan clamping.
- `PdfViewport.Overlays.cs`: sheet header, sheet legend, overlay scale, and
  architectural scale labels.
- `PdfViewport.MeasurementRendering.cs`: measurements, annotations, labels,
  joist drawing, AI action previews, AI markers, and in-progress drawing.
- `PdfViewport.Input.cs`: mouse, keyboard, context menu request, cursor, and
  joist-direction capture cancellation.
- `PdfViewport.Geometry.cs`: point/rect/segment/polygon helpers and measurement
  bounds/visibility helpers.
- `PdfViewport.ViewTransform.cs`: zoom, rerender scheduling, screen/PDF
  coordinate conversion, visible rect, pointer status, and color cache.

### New state objects after the split

After no-behavior partial splitting, introduce small state/services:

- `TakeoffCreationPolicy`: decides where new items and folders are created.
- `TakeoffSelectionState`: owns active item, active folder, section selection,
  and viewport target folder sync.
- `TreeExpansionState`: owns expanded page/takeoff paths and restore rules.
- `PageNavigationState`: owns "which page should open" decisions.
- `PdfLayerTraceSession`: owns trace candidate, selected layer, mode, and
  phase.
- `MeasurementChangeCoordinator`: owns add/remove/change persistence and
  refresh scheduling.

These should be small and testable. They should not depend on WPF controls
unless absolutely necessary.

## Priority Refactor Phases

### Phase 0 - Stabilize current behavior

Before moving code:

- keep the build clean;
- record smoke tests for the workflows below;
- do not rename product namespace, executable, or project files;
- do not move generated or reference assets;
- do not mix visual redesign with refactor changes.

Required smoke checklist:

- import a PDF and confirm page and takeoff trees start closed;
- manually expand page/takeoff tree branches and confirm they stay expanded
  after move, edit, selection, and reload;
- create a new takeoff item while a folder is selected and confirm it appears
  at the takeoff root;
- create a new folder while a folder is selected and confirm folder creation
  still respects selected folder context;
- select a page/takeoff such as `2nd` and confirm the correct page opens;
- turn one PDF layer off and all layers off, confirm the bitmap changes;
- turn layers back on, confirm the bitmap restores;
- Layer Trace: click PDF geometry, see layer highlight, Tab cycles candidates,
  click/Enter locks, Tab cycles trace modes, Apply creates measurement;
- move/edit/delete a measurement and confirm totals, tree row, estimate table,
  and autosave update.

### Phase 1 - No-behavior file split

Move coherent blocks into partial files. Do not change logic. Build after each
file split. This gives smaller diffs and makes future bug fixes safer.

Suggested first two splits:

1. Move PDF layer UI methods out of `MainWindow.xaml.cs` into
   `MainWindow.PdfLayers.cs`.
2. Move Layer Trace methods out of `Controls/PdfViewport.cs` into
   `PdfViewport.LayerTrace.cs`.

### Phase 2 - Centralize active takeoff and tree policy

Create one owner for active takeoff and folder creation rules. This directly
targets the recent "new item created in a folder" and "tree keeps collapsing"
bug class.

Acceptance criteria:

- new item creation policy is one method/service with tests;
- folder creation policy is separate from item creation policy;
- tree expansion capture/restore is not scattered across unrelated handlers;
- selection sync uses guard scopes so flags always reset in `finally`.

### Phase 3 - Harden PDF layer worker contracts

Add command-level tests or scripts for the Python worker. Use real sample PDFs
when available, plus a tiny synthetic fallback if needed.

Acceptance criteria:

- `render` output changes when a visible layer is disabled;
- `render` returns to original output after re-enable;
- `layerprobe` returns candidate layer names and bounds near a picked point;
- `layertrace` returns valid line/area/point geometry for each mode;
- worker failures include command, PDF path, page index, and error message.

### Phase 4 - Extract viewport responsibilities

Move layer state and trace session out of `PdfViewport` internals. The viewport
should still draw and handle input, but it should not own every domain decision.

Acceptance criteria:

- Layer Trace session state is testable without WPF;
- selection hit-testing helpers are isolated;
- measurement creation stays explicit and rejectable by the main window;
- no direct takeoff creation policy lives in the viewport.

### Phase 5 - Persistence and autosave hardening

Make saves explicit and observable.

Acceptance criteria:

- broad/bare catches around writes are replaced or documented;
- autosave failures surface to status/UI;
- save methods include the failed file path in the error;
- the app does not silently lose measurements, settings, context, or AI state.

### Phase 6 - AI and 3D separation

The AI inbox and massing features are valuable, but they should not live inside
the same file as page/takeoff tree and drawing logic.

Acceptance criteria:

- AI request execution and review live behind services/dialog controllers;
- massing preview/review lives in its own partial/service boundary;
- main window only wires UI events to feature coordinators.

## Immediate Next Work Items

1. Add a smoke-test checklist file and use it after every refactor step.
2. Split `MainWindow.PdfLayers.cs` from `MainWindow.xaml.cs`.
3. Split `PdfViewport.LayerTrace.cs` from `Controls/PdfViewport.cs`.
4. Build after each split.
5. Add Python worker contract tests for `render`, `layerprobe`, and
   `layertrace`.
6. Extract `TakeoffCreationPolicy` and add tests for root-item vs selected-folder
   behavior.
7. Extract `TreeExpansionState` and add tests for import-closed, user-opened,
   and reload-preserved behavior.

## Refactor Stop Rules

Stop and fix before continuing if any of these happen:

- build warning or error appears;
- selected page opens the wrong PDF page;
- tree expansion state regresses;
- new item creation goes into the wrong folder;
- PDF layer toggle stops changing the bitmap;
- Layer Trace stops highlighting or creates measurements without a valid active
  item;
- measurement totals or autosave stop updating after edit/move/delete.

## Bottom Line

No, a 16,943-line `MainWindow.xaml.cs` is not a healthy long-term shape for
this app. The app still builds and contains a lot of working behavior, so the
right answer is a staged refactor, not a rewrite. The highest value first move
is to split the main window and viewport by feature, then centralize active
takeoff/tree/PDF layer state behind small testable objects.
