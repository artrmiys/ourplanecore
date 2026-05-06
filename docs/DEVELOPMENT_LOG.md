# Development Log

## 2026-05-06 Takeoffs Tree Refactor Block

- Completed a no-behavior split of the oversized takeoffs workflow into focused
  `MainWindow.Takeoffs*.cs` partial owners. `MainWindow.TakeoffsTree.cs` is now
  a 329-line shell for selection, context-menu opening, mouse selection, and
  drag arming.
- New and existing takeoffs ownership after this block:
  - `MainWindow.TakeoffsExport.cs`: takeoff CSV/TXT/XLSX export;
  - `MainWindow.TakeoffsCreation.cs`: new item/folder and auto-create flows;
  - `MainWindow.TakeoffsPersistence.cs`: save and observation actions;
  - `MainWindow.TakeoffsActiveTarget.cs`: active target panel and commands;
  - `MainWindow.TakeoffSections.cs`: section rows, menus, and ordering;
  - `MainWindow.TakeoffsJoists.cs`: joist direction capture;
  - `MainWindow.TakeoffsProperties.cs` and
    `MainWindow.TakeoffsBulkProperties.cs`: item/folder properties dialogs;
  - `MainWindow.TakeoffsMenus.cs`: context-menu builders;
  - `MainWindow.TakeoffsNodeActions.cs`: rename/delete/move/sort node actions;
  - `MainWindow.TakeoffsSelectionHelpers.cs`: takeoff and section multi-select
    helpers;
  - `MainWindow.TakeoffsClipboard.cs`: keyboard shortcuts and copy/cut/paste;
  - `MainWindow.TakeoffsDragDrop.cs`: node reorder, section drag/drop, and drop
    cue status.
- Commits in this block:
  `c4d3d39`, `48c3880`, `d11eb6c`, `45f7451`, `adc741a`, `65cd520`,
  `255bfef`, `6bdad29`, `7b8edff`, `0b6b666`, `ffe3a38`, `38fab8c`,
  `a9cd0aa`.
- Rollback for the code refactor block:
  `git revert a9cd0aa 38fab8c ffe3a38 0b6b666 7b8edff 6bdad29 255bfef 65cd520 adc741a 45f7451 d11eb6c 48c3880 c4d3d39`
- Verification after each code slice:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors,
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj` passed 77/77, and
  `git diff --check` passed with only Git's LF-to-CRLF warning.

## 2026-05-06 Overlay Alignment and Sheet-Linked Tree Fixes

- Added sheet-to-sheet overlay support for active pages:
  - page metadata now stores overlay sheet folder, color, opacity, X/Y offset,
    and scale;
  - the PDF viewport renders the overlay underneath takeoffs and markups;
  - PDF export includes the same overlay transform so exported sheets match the
    viewport.
- Added overlay transform controls:
  - right-click overlay rows expose move, scale, reset, color, clear, and
    numeric transform editing;
  - `Edit Overlay by Points` starts a viewport alignment workflow where the
    first point pair moves the overlay and the second point pair scales it
    around the first matched point.
- Updated the left Pages tree behavior:
  - overlay rows now stay below the sheet-linked takeoff rows instead of first;
  - linked takeoff rows are deduped by takeoff folder path;
  - selecting a linked takeoff row no longer starts a recursive selection sync
    between the Pages tree and Takeoffs tree.
- Added joist-area refinements:
  - roof pitch such as `3:12` applies slope length;
  - area cut holes subtract from joist layout;
  - detailed vs standard joist label format is persisted per takeoff.
- Verification:
  `dotnet build .\ourplanecore.sln` passed with 0 warnings and 0 errors.
- Regression runner:
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build` passed
  64/64.

## 2026-05-05 Roadmap Implementation Slice

- Follow-up slice after commit `ee7d8ff`:
  - Snap v2 now includes intersection snap candidates across existing
    measurements, current in-progress geometry, and page markups. Intersection
    snaps use a distinct `int x,y` canvas preview.
  - Page markups (`Ruler`, draw line, arrow, and box) are selectable in the
    `Select` tool. Users can drag the markup body to move it, drag blue handles
    to reshape endpoints/corners, press `Delete`, or right-click and delete the
    markup. Edits persist through `annotations.json`.
  - Selected measurements and page markups now show a subtle orange transform
    area behind the blue selected bounds. The orange area has live corner scale
    handles and a top rotate handle for direct canvas editing.
  - The main tool strip now docks at the bottom, with selection edit controls
    grouped there: horizontal mirror, vertical mirror, rotate slider, and scale
    slider. The transform controls enable only when a canvas selection exists.
  - Verification passed again:
    `dotnet build .\ourplanecore.sln` with 0 warnings/errors, and
    `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build --no-restore`
    passed 30/30.

- Added a lightweight no-NuGet regression runner under `Tests/` and wired it
  into `ourplanecore.sln`. The runner currently covers 20 fast checks across
  measurements, takeoff totals, sheet metadata, app settings/recent jobs, and
  the new job recovery service.
- Added `Models/JobRecoveryService.cs` plus `MainWindow.JobRecovery.cs`:
  - each opened job gets a `.~lock` marker;
  - stale lock markers trigger a recovery prompt on next open;
  - manual saves and job switches create metadata snapshots under
    `.snapshots/`;
  - snapshots copy `Data.xml`, Pages metadata, Takeoffs metadata, and
    `measurements.json`, while skipping source PDFs, rendered images, AI crop
    images, and build output;
  - old snapshots are pruned to a bounded history.
- Hardened Estimating for larger jobs: the embedded estimate list now uses WPF
  virtualization/recycling and has a sticky summary footer showing visible
  item count, section/count row count, and visible cost total when priced rows
  are present.
- Added the next Snap v2 slice in `Controls/PdfViewport.Tools.cs` and
  `Controls/PdfViewport.MeasurementRendering.cs`: snap tolerance remains in
  screen pixels, midpoint candidates are included alongside endpoints, endpoint
  and midpoint snap glyphs differ visually, and the canvas shows a compact
  `end/mid x,y` coordinate label while snapping.
- Fed reviewed opening projection feedback into future Auto Roof prompts. The
  roof recognition request now includes recent accepted/rejected
  `3d_opening_projection_review` records so future detection can learn from the
  user's opening review outcomes.
- Tightened secondary Count wording: count commands now say `count marks`, and
  Line/Area geometry status/tooltips use `vertices` instead of ambiguous
  `point(s)` where the text is not referring to PDF coordinates.
- Added Joist Area roof pitch support for sloped length takeoff:
  - Joist Properties now include `Pitch (rise:run)` with examples like `3:12`;
  - accepted inputs include `3:12`, `3/12`, `3 in 12`, and a single rise value
    such as `3` as shorthand for `3:12`;
  - blank or `0:12` stays flat;
  - generated joist length is multiplied by the slope factor before per-joist
    order-length rounding, so totals, labels, estimating, PDF export, and
    PlanSwift export use sloped length.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\\cache\\verify_build\\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.
- Regression runner:
  `dotnet .\Tests\cache\verify_build\OurPlaneCore.Tests.dll` passed 30/30.

## 2026-05-05 Architecture Audit and Refactor Plan

- Created and validated local Codex skills under `C:\Users\User\.codex\skills`
  for the major recurring workstreams in this repo: refactor, bugcheck, PDF
  layers/trace/export, Pages/Takeoffs trees, measurements, AI/massing, docs
  handoffs, Bluebeam-style UX, preview UI mockups, PlanSwift spec mapping, sheet
  metadata, and parallel-agent coordination.
- Updated `AGENTS.md` with explicit `$ourplanecore-*` skill routing so future
  agents can load the right reusable workflow before editing.
- Added development guardrails to `AGENTS.md`: new C# file size limits,
  `MainWindow.xaml.cs` and `MainWindow.*.cs` growth limits, XAML size targets,
  method-size limits, and rules for choosing focused services/controls/partials
  instead of quick patches that grow oversized files.
- Audited the current WPF app structure after the recent PDF layer, Layer Trace,
  tree-state, item-creation, export, AI, and 3D work.
- Recorded the main architecture risks in
  `docs/ARCHITECTURE_AUDIT_AND_REFACTOR_PLAN_2026_05_05.md`:
  - `MainWindow.xaml.cs` started at 16,943 lines and is the primary god-object
    risk;
  - `Controls/PdfViewport.cs` is 3,942 lines and mixes rendering, input,
    drawing, selection, PDF layers, trace, overlays, and status;
  - active takeoff state, tree expansion state, and PDF layer state have too
    many implicit owners;
  - the safe refactor path is staged partial splits, smoke tests, then small
    state/controller extractions.
- Applied the first no-behavior split:
  - moved PDF Layers and Layer Trace UI handlers from `MainWindow.xaml.cs` into
    new partial file `MainWindow.PdfLayers.cs`.
- Continued the staged refactor after a git checkpoint:
  - changed `Controls/PdfViewport.cs` to a partial class and moved Layer Trace
    session/probe/trace/overlay code into `Controls/PdfViewport.LayerTrace.cs`;
  - `Controls/PdfViewport.cs` is now 3,581 lines, and
    `Controls/PdfViewport.LayerTrace.cs` is 370 lines;
  - added `Models/TakeoffCreationPolicy.cs` so new item vs new folder placement
    is explicit and not hidden inside tree-selection UI code.
- Split display and overlay settings into `MainWindow.DisplaySettings.cs`.
- Split measurement/page-annotation callbacks into
  `MainWindow.MeasurementCallbacks.cs`.
- Split viewport measurement/AI context-menu builders into
  `MainWindow.ViewportContextMenu.cs`.
- Split PDF export workflow and writer loop into `MainWindow.PdfExport.cs`.
- Moved PDF export drawing helpers into the same `MainWindow.PdfExport.cs`
  partial file.
- Corrected current post-split line counts with `rg -n "^"` because the earlier
  quick count under-reported physical file lines. Current counts are tracked in
  `docs/ARCHITECTURE_AUDIT_AND_REFACTOR_PLAN_2026_05_05.md`; the main window is
  now 17,554 lines, and `MainWindow.PdfExport.cs` is 735 lines.
- Split workspace manager callbacks for Sheet Manager, Takeoff Manager, AI
  Manager, and 3D Manager into `MainWindow.WorkspaceManagers.cs`.
  `MainWindow.xaml.cs` is now 17,271 lines, and the new partial file is
  299 lines.
- Split estimating setup, estimating window callbacks, estimate selection sync,
  and section property dialogs into `MainWindow.Estimating.cs`.
  `MainWindow.xaml.cs` is now 16,456 lines, and the new partial file is
  834 lines.
- Split 3D Massing right-panel construction into `MainWindow.MassingPanel.cs`.
  `MainWindow.xaml.cs` is now 16,161 lines, and the new partial file is
  308 lines.
- Split Pages tree workflow into `MainWindow.PagesTree.cs`, including page
  tabs, page takeoff legend ordering, page move/copy/sort operations, and PDF
  metadata automation. `MainWindow.xaml.cs` is now 12,483 lines, and the new
  partial file is 3,705 lines.
- Split Takeoffs tree workflow into `MainWindow.TakeoffsTree.cs`, including
  item/folder creation, takeoff export, active target controls, section rows,
  properties dialogs, drag/drop, copy/paste, and multi-select.
  `MainWindow.xaml.cs` is now 8,936 lines, and the new partial file is
  3,569 lines.
- Split shared tree, legend, totals, estimating-row, quantity, and
  takeoff-default helpers into `MainWindow.TreeHelpers.cs`.
  `MainWindow.xaml.cs` is now 7,614 lines, and the new partial file is
  1,338 lines.
- Split measurement copy/paste, paste-target resolution, and takeoff autosave
  helpers into `MainWindow.MeasurementClipboard.cs`.
  `MainWindow.xaml.cs` is now 7,268 lines, and the new partial file is
  361 lines.
- Split viewport scale/tool/context callbacks, AI crop/marker save helpers,
  marker overlay refresh, and context suggestion handlers into
  `MainWindow.ViewportCallbacks.cs`. `MainWindow.xaml.cs` is now 6,717 lines,
  and the new partial file is 570 lines.
- Split persisted app settings, theme/background application, side-panel width
  persistence, scale UI, and small input dialogs into `MainWindow.Utilities.cs`.
  `MainWindow.xaml.cs` is now 6,351 lines, and the new partial file is
  381 lines.
- Split AI Inbox display, inbox context menu, crop bookmark creation/runs,
  marker filters, and marker-set entry points into `MainWindow.AiInbox.cs`.
  `MainWindow.xaml.cs` is now 5,486 lines, and the new partial file is
  886 lines.
- Split 3D Massing build/review workflow, roof/opening review, 3D viewport
  preview, marker rows, and massing preview drawing into
  `MainWindow.MassingWorkflow.cs`. `MainWindow.xaml.cs` is now 3,589 lines,
  and the new partial file is 1,920 lines.
- Split AI marker-set management, marker editing, observation actions, AI
  request execution, response/action-draft viewing, and action application into
  `MainWindow.AiActions.cs`. `MainWindow.xaml.cs` is now 1,376 lines, and the
  new partial file is 2,234 lines.
- Split nested clipboard, page-tab, tree-node, display-row, and 3D support
  types into `MainWindow.SupportTypes.cs`. `MainWindow.xaml.cs` is now
  1,202 lines, and the new partial file is 194 lines.
- Split toolbar setup, marker filter initialization, open/new job workflows,
  persisted marker visibility, takeoff loading, measurement-link repair, and
  PDF import into `MainWindow.JobLifecycle.cs`. `MainWindow.xaml.cs` is now
  679 lines, and the new partial file is 543 lines.
- Split drawing tool controls, record toggle, snap/ortho state, drawing-target
  confirmation, viewport zoom/scale buttons, and scale presets into
  `MainWindow.ToolControls.cs`. `MainWindow.xaml.cs` is now 356 lines, and the
  new partial file is 338 lines.
- Fixed Layer Trace probing for PDF layers whose PyMuPDF UI layer number is
  `0`. The probe path now keeps named layer candidates instead of dropping
  `layer == 0`, and the C# DTO path accepts named candidates with zero-valued
  layer numbers.
- Reworked Layer Trace interaction into a temporary focus mode:
  - enabling Layer Trace ghosts the current PDF page without changing the
    user's real layer checkbox states;
  - moving the cursor probes PDF layer geometry in the background, highlights
    the current hit candidate, and allows Tab cycling when multiple candidates
    overlap;
  - clicking or pressing Enter locks the current candidate and temporarily
    renders only that selected PDF layer until the trace is committed or
    cancelled;
  - Esc unlocks the current trace selection first, then exits Layer Trace on
    the next press.
- Added `Tools/pdf_layer_trace_smoke.py`, which creates a tiny synthetic
  layered PDF and verifies helper contracts for layer render toggling,
  `layerprobe`, and `layertrace` full/edge/all-edges/point modes.
- Split `Controls/PdfViewport.cs` into focused partial files for layer
  rendering, paint orchestration, sheet overlays, measurement/annotation/AI
  rendering, mouse/keyboard input, drawing tools, selection/editing, geometry,
  and view-transform helpers. `Controls/PdfViewport.cs` is now 811 lines; the
  largest extracted viewport partial is
  `Controls/PdfViewport.MeasurementRendering.cs` at 825 lines.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.

## 2026-05-04 PDF Export Dialog Button Fix

- Fixed the PDF export dialog selection handling:
  - selected sheet paths are now normalized before comparison, so the dialog
    does not accidentally open with no selected rows when the same folder path
    has a different string form;
  - export rows now notify when their checkbox changes;
  - the dialog commits the active DataGrid checkbox edit before the `Export`
    button checks selected rows.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.

## 2026-05-04 PDF Export Legend and Label Rules

- Updated PDF export overlay behavior:
  - exported sheet legend now uses the configured sheet legend size multiplied
    by `2x`;
  - export-only measurement labels are now drawn for Line and Area
    measurements;
  - Count measurements still export their count marks but do not draw count
    labels;
  - export labels use the page/measurement scale fallback and the selected
    export unit mode.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.

## 2026-05-04 Remove Excess Bold Text

- Removed bold/semibold typography from the app shell and manager tables so the
  interface does not feel visually heavy:
  - `DataGridColumnHeader` now uses normal text;
  - selected command/workspace tabs keep color/accent-line state but no longer
    change to semibold;
  - `Pages`, `Takeoffs`, `AI Inbox`, total/status labels, manager group labels,
    active-target labels, and dynamic tree row states now use normal text;
  - dialog headers in marker sets, OpenAI settings, PDF export, 3D window, and
    takeoff folder properties now use normal text.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.

## 2026-05-04 Tab Density Correction

- Corrected the visual hierarchy pass after checking the app shell density:
  - increased the compact top command tab height from `48-54` to `60-66` so
    the `Job` / `Open` / `Folders` / `PDF` command row is not clipped;
  - reduced workspace-tab typography from `13px` semibold to `11px` normal,
    with only selected tabs using semibold;
  - reduced workspace-tab padding so `Main View`, `Sheet Manager`,
    `Takeoff Manager`, `AI Manager`, and `3D` no longer dominate the page;
  - changed manager buttons from semibold `12px` to normal `11px`;
  - softened manager group labels from extra-bold to semibold.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.

## 2026-05-04 Deeper Workspace Usability Pass

- Rechecked navigation/command-bar research before changing the shell again:
  - top-level workspaces should be few, flat, and clearly labeled;
  - selected tabs need a stronger visual connection to their content;
  - command bars should keep high-value actions visible and secondary actions
    grouped;
  - dense tables need row/header hierarchy so users can scan across columns.
- Updated `App.xaml` with a stronger hierarchy system:
  - selected command/workspace tab templates now draw an accent line;
  - manager command bars now have a full-width band style;
  - manager group labels are now visible section chips;
  - commit actions have a separate green style (`ManagerCommitButton`);
  - manager tables use alternating row color and hidden row headers.
- Updated `MainWindow.xaml` workspace tabs to show numbered labels:
  `1 Main View`, `2 Sheet Manager`, `3 Takeoff Manager`, `4 AI Manager`,
  `5 3D`.
- Added stable `Tag` keys for the workspace tabs so code and command-palette
  navigation no longer depend on visible tab text.
- Added tooltips to manager-tab buttons so short commands are discoverable
  without adding explanatory paragraphs to the app.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.

## 2026-05-04 Workspace Visual Hierarchy Pass

- Researched Fluent/WPF guidance before styling:
  - Fluent layout guidance emphasizes spacing/proximity for grouping and visual
    hierarchy;
  - Fluent typography guidance emphasizes clear typographic hierarchy for
    scannability;
  - Windows spacing/density guidance supports compact sizing for information
    rich applications;
  - WPF `TabControl`/`TabItem` styling is the correct customization point for
    selected-tab and content-page visuals.
- Added shared visual resources in `App.xaml`:
  - accent brushes (`AccentBrush`, hover/pressed/foreground);
  - toolbar/manager band brushes;
  - `ManagerButton`, `ManagerPrimaryButton`, `ManagerSubtleButton`;
  - `ManagerGroupLabel`, `ManagerToolbar`, `ManagerSurface`;
  - `CommandTabItem` and `WorkspaceTabItem`;
  - denser, clearer `DataGrid` row/header defaults.
- Applied the new styles to `MainWindow.xaml`:
  - top command tabs and workspace tabs now have distinct visual treatment;
  - manager toolbars use grouped labels such as `PDF`, `Metadata`, `Item`,
    `Batch`, `Markers`, and `Review`;
  - primary actions such as `Auto Name`, `Name+Scale`, `Set Active`, `Run AI`,
    `Build 3D Draft`, and `Accept 3D` now use accent styling.
- Extended `ApplyTheme` so the new accent/manager brushes update in light and
  dark themes.

## 2026-05-04 Workspace Manager Tabs

- Wrapped the main work area in top-level workspace tabs:
  `Main View`, `Sheet Manager`, `Takeoff Manager`, `AI Manager`, and `3D`.
- `Main View` keeps the existing canvas, Pages panel, Takeoffs panel, and
  collapsed AI Inbox intact so drawing behavior stays stable.
- Added `Sheet Manager` as a persistent sheet table using the same
  `PdfMetadataPreviewRow` shape as Auto Name / Auto Scale review:
  - columns for current page, proposed name, proposed scale, label, suffix,
    title, source, confidence, reason, warnings, and Rename/Scale checkboxes;
  - actions for Refresh, Analyze, Auto Name, Auto Scale, Name+Scale,
    Apply Checked, Open Sheet, and Open JSON;
  - analysis and apply reuse the existing PDF metadata services and learning
    feedback path instead of creating a parallel rename/scale flow.
- Added `Takeoff Manager` as a full-width takeoff item table with Set Active,
  Properties, Open Estimating, New Item, and Export CSV actions.
- Added `AI Manager` as a full-width AI inbox table with Refresh, Open Details,
  Go to Page, Run AI, Marker Sets, and Export Markers actions.
- Added a `3D` manager tab with draft/build/open actions and a text summary
  tied to the existing 3D massing draft model.
- Completed a button/function pass so each workspace owns the relevant command
  group:
  - `Sheet Manager`: PDF import/export, metadata analyze/name/scale, AI Fill,
    apply checked, open sheet/json, page sorting/repair/folders;
  - `Takeoff Manager`: save, item/folder creation, active target, properties,
    estimating, tree automation, and CSV/TXT/Excel exports;
  - `AI Manager`: AI settings, observations, selected/batch AI runs, marker
    sets, marker export, and 3D draft handoff;
  - `3D`: draft build, 3D from takeoffs, detached viewport, JSON, roof/opening
    review, and accept.
- Added `docs/WORKSPACE_TAB_COMMAND_MAP.md` as the durable command ownership
  map for the new workspace tabs.

## 2026-05-04 Full UX Shell Cleanup Block

- AI Inbox now starts collapsed (`InboxRow` 30px, splitter hidden) so the PDF
  canvas keeps vertical space on launch.
- AI Inbox header now keeps only the frequent actions visible (`Run AI`,
  `+ Add`) and moves lower-frequency batch/marker/3D actions under `More`.
- The right Takeoffs/Estimating workspace default width increased from 220px
  to 300px, with a wider minimum, so active target and estimate controls are
  less cramped.
- Estimating `ListView` now explicitly allows horizontal and vertical scroll.
  The side tab remains a quick view.
- Added `Dialogs/EstimatingWindow.cs`, a modeless full Estimating window with
  filter, current-sheet toggle, sortable virtualized `DataGrid`, Select/Page/
  Props actions, Refresh, and Copy.
- The Estimating side tab now has an `Open` button that opens or activates the
  full window. Both views use the same estimate row source.
- The active target bar now exposes only `Record` and `More`; secondary
  actions (`Props`, `Find`, sheet targets, previous/next target) live behind
  `More`.
- Pages and AI Inbox context menus now use submenus for lower-frequency
  command groups instead of one long flat list.

## 2026-05-04 UX / Design Research Audit

- Added `docs/UX_DESIGN_RESEARCH_AUDIT_2026_05_04.md` as the current
  design-risk review after the compact top command bar, Display tab reorg, and
  toolbar duplicate cleanup.
- Findings are prioritized as P0/P1/P2 and focus on what is uncomfortable or
  likely to break in daily use: overloaded main shell, cramped Estimating tab,
  crowded AI Inbox header, long context menus, `MainWindow.xaml.cs` growth,
  active-target bar density, Record workflow ambiguity, tree scalability, and
  future detached-window placement.
- The audit references current repo files and checked official PlanSwift/WPF
  sources for digitizer options, page tabs/windows, estimating workflow,
  scale behavior, WPF virtualization, collection views, and modal/modeless
  window lifecycle.

## 2026-05-04 Per-Overlay Scale-With-Page Toggles and Display Tab Reorg

- Split `ScaleSheetOverlaysWithPage` into three independent toggles. Previously
  one global flag affected only the legend; now each overlay has its own
  `Scale w/ page` checkbox and they all default off (screen-constant size):
  - `ScaleMeasurementLabelsWithPage` вЂ” value labels on line/area/count, joist
    segment labels, AI markers, AI action draft preview labels;
  - `ScaleSheetOverlaysWithPage` вЂ” sheet legend overlay (existing flag, kept
    as-is so the JSON setting stays compatible);
  - `ScaleSheetHeaderWithPage` вЂ” top sheet scale/size header overlay.
- Persistence: added two new `bool` fields to `Models/AppSettingsStore.cs`
  (`ScaleMeasurementLabelsWithPage`, `ScaleSheetHeaderWithPage`).
- `Controls/PdfViewport.cs`:
  - replaced `SheetZoomOverlayScale()` with the parameterized
    `SheetZoomOverlayScale(bool enabled)`;
  - `LegendOverlayScale()` and `HeaderOverlayScale()` both call the new helper
    using their respective per-overlay flag;
  - `DrawScreenTextBox` now picks the divisor based on
    `ScaleMeasurementLabelsWithPage` вЂ” `safeZoom` (screen-constant) when off,
    `CurrentFitZoom()` (PDF-space, normalized to fit) when on. The change is
    applied to `TextSize`, padding, border stroke, and corner radius so the
    label box stays self-consistent in either mode.
- `MainWindow.xaml.cs`:
  - `DisplaySetting_Click`, `SyncDisplaySettingsControls`,
    `ApplyDisplaySettingsToViewport`, and `ApplySheetOverlaySettings` now read
    and push the two new flags through to `PdfViewport`;
  - added `SetMeasurementLabelsScaleWithPage` and `SetSheetHeaderScaleWithPage`
    setter helpers (mirror of the existing `SetSheetOverlaysScaleWithPage`);
  - the right-click viewport overlay menu (`AddSheetOverlayMenuItems`) now
    shows three independent checkable items instead of one.
- Reorganized the top `Display` tab into five semantic groups, one concept per
  group, so settings for different overlays no longer share a column. The
  previous layout mixed measurement-label settings and the sheet-header
  `Scale Label` button inside the `Legend` group:
  1. `Values` вЂ” `All` / `Line ft` / `Area sf` / `Count ea` (visibility);
  2. `Label` вЂ” value label `Size` `TextBox` + `Set` + `в–ѕ Presets` popup +
     `Scale w/ page`;
  3. `Legend` вЂ” `Show` / `Size` / `Pos` / `Scale w/ page`;
  4. `Header` вЂ” `Size` / `Scale w/ page` (formerly `Scale Label` inside
     `Legend`);
  5. `View` вЂ” `ft/sf` / `BG` / `Dark`.
- Added `BtnLabelSizePresets_Click` so the value-label `Size` input gains the
  same `Small / Normal / Large / XL / XXL / Custom` popup that the legend and
  header `Size` buttons already use, via the shared `ShowOverlaySizePopup`
  helper.
- Tooltips were added on every checkbox/button/textblock in the `Display` tab
  so the meaning of every short label (e.g. `Scale w/ page`, `BG`, `Pos`) is
  discoverable on hover.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\\cache\\verify_build\\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.
## 2026-05-04 Compact Top Command Bar and Display Settings Stabilization

- Reworked the top `Main` / `Display` area into a compact command bar:
  - reduced top tab height and command padding;
  - grouped controls as `Job`, `PDF`, `Values`, `Legend`, and `View`;
  - shortened labels such as `Open`, `Folders`, `Import`, `Export`, `Name`,
    `Scale`, `BG`, and `ft/sf`.
- Removed duplicated command surfaces that were making the UI feel crowded:
  - `Open Job`, `Jobs`, and `New Job` were removed from the older drawing
    toolbar because the top `Main` tab owns job actions now;
  - `Import PDF`, `Export PDF`, and the old `PDF Auto` expander were removed
    from the left Pages panel because the top `Main` tab owns PDF actions now.
- Replaced the viewport value-label `S / M / L` buttons with a numeric
  `MeasurementLabelScale` input in the top `Display` tab. The user can now set
  exact values from `0.5` to `3.0`, for example `0.5`, `1.0`, or `1.35`.
- Added validation and save wiring for that numeric value-label scale through
  `BtnMeasurementLabelApply_Click`, `TxtMeasurementLabelScale_KeyDown`,
  `TxtMeasurementLabelScale_LostFocus`, and `ApplyMeasurementLabelScaleFromText`.
- Hardened the two dark-theme toggles (`BtnDarkTheme` and
  `BtnDisplayDarkTheme`) so programmatic synchronization does not recursively
  trigger the theme handlers or duplicate settings writes.
- Ran a quick XAML conflict audit:
  - no duplicated `x:Name` entries found in `MainWindow.xaml`;
  - old duplicated `PDF Auto` / side-panel import-export command names are gone;
  - build stayed clean after the toolbar and settings changes.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\\cache\\verify_build\\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.

## 2026-05-04 (UI/UX overhaul, follow-ups)

- Finished the remaining UI/UX polish from the glyph pass:
  - Count / Line / Area radio buttons now use the shared `MeasurementGlyph`
    pack beside their labels, and `ApplyTheme` rebuilds those labels so glyph
    colour follows light/dark foreground resources;
  - Takeoffs tree row-state styling now separates signals by channel:
    background is reserved for measured-on-page or multi-select state, while
    the active takeoff target is a left accent bar plus semibold text. This
    keeps active + on-page + selection visible together instead of one
    background hiding the others.
- Glyph language v2: dropped the separate colored swatch entirely. The
  measurement glyph itself is now drawn filled in the takeoff color with a
  darker stroke (perceptual `color в€’ 60` per channel) and reads cleanly on
  any panel background. `BuildTakeoffSwatchGlyph` is now a thin wrapper that
  just builds the glyph; the `Grid` + filled `Rectangle` fallback is removed.
  Tree row, page-takeoff legend row, active-takeoff bar, and on-canvas sheet
  legend all show only the glyph вЂ” no double colour blocks. Glyph sizes
  bumped (tree: 14в†’16 inactive / 16в†’18 active; page row: 12в†’14 / 14в†’16) to
  give the new outlined-and-filled glyph room to breathe.
- The on-canvas sheet legend (`DrawSheetLegendOverlay`) now passes the
  takeoff `SKColor` directly to `DrawLegendSignIcon`/`MeasurementGlyph.DrawSkia`
  instead of `textPaint.Color`. The previous separate `swatchRect` (filled
  square + darker border) is removed; the row layout collapses to
  `[glyph] [name] ... [qty]`.
- Joist calculator fixes:
  - `FormatLegendNumber` previously force-replaced `.` with `,` after
    formatting under `InvariantCulture`. This produced European-style
    decimals (`8,5 FT`) in joist legend lines while the rest of the app
    showed dots (`8.5'`). The replacement is removed; joist legend output
    is now consistent with the rest of the formatting in the app.
  - `RoundUpToNextEvenFoot` (used by `Nearest 2 Feet` rounding) added `+1`
    before the parity check, so an exactly even raw length such as `8.0 ft`
    rounded up to `10.0 ft` instead of staying at `8.0`. The unconditional
    `+1` is removed; the function now ceils to the smallest even foot
    `в‰Ґ` the raw length, which matches the rule's plain reading and the
    sibling `Nearest Even Foot` behaviour.

## 2026-05-04 (UI/UX overhaul)

- Introduced `Controls/MeasurementGlyph.cs` вЂ” a single source of truth for the
  takeoff-type icon language. Renders identical glyphs for the Takeoffs tree
  (WPF), the page-takeoff legend rows (WPF), the active-takeoff bar (WPF), and
  the on-canvas sheet legend (Skia). Replaces three divergent icon
  implementations: the diagonal-line WPF Canvas in `CreateMeasurementTypeIcon`,
  the diagonal-line Skia `DrawLegendSignIcon`, and the toolbar emoji captions.
- New glyph language: Line = horizontal segment with dot endpoints (reads as
  "ruler"); Area = rounded rectangle with soft fill; Joist = rounded rectangle
  with three parallel bars; Point/Count = filled circle (Count gets a white
  centre dot). Both renderers compute glyph stroke/padding from the same
  formulas, so the on-canvas legend visually matches the tree.
- `SetTreeItemHeader` rewritten to remove duplicate active-state markers. The
  old layout stacked four left-hand markers (4Г—18 blue bar + 16Г—16 colour ring +
  15Г—15 type icon + bold name = ~35вЂ“40 px before the name). New layout: one
  composite swatch (colored rounded square + glyph in a contrast colour
  computed from perceived luminance) + name + ledger-style right-aligned total
  (Consolas/Cascadia Mono with `MinWidth=56`). Active state now uses only a
  3-px left accent bar drawn via `TreeViewItem.BorderBrush/BorderThickness` +
  semibold name. The inline `[Type]` text and the duplicate ring/icon/bar are
  gone.
- `BuildPageTakeoffHeader` (per-page legend rows under each page in the Pages
  tree) gets the same layout вЂ” composite swatch + index `N.` + ledger qty.
- Row-state colour flags moved to themeable resources in `App.xaml`:
  `RowOnPageBrush`, `RowActiveBrush`, `RowMultiSelectBrush`, `RowDropOkBrush`,
  `RowDropBadBrush`, `RowFlagForegroundBrush`, `RowActiveAccentBrush`. Each gets
  a paired light/dark variant set by `ApplyTheme`. Old hard-coded
  `Color.FromRgb(...)` literals in `RefreshTakeoffTreeStyles` removed.
- `ToggleButton` style now has an `IsChecked=True` trigger that switches
  background to `ControlActiveBackgroundBrush` and bolds the label, so users
  can actually see whether `Snap`, `Ortho`, `Imperial`, or `Dark` is on. Same
  trigger added to the toolbar `ToggleButtonStyleKey` style.
- New `ToolRadio` style for `RadioButton` so drawing tools form a real radio
  group. `BtnPan/BtnSelect/BtnScale/BtnPoint/BtnLine/BtnArea` converted from
  `Button` to `RadioButton` with `GroupName=DrawingTool`. `_toolBtns` retyped
  to `Dictionary<string, RadioButton>`, and `ApplyToolSelection` now sets
  `IsChecked` instead of swapping `ToolBtn`/`ToolBtnActive` styles. WPF's
  built-in radio behaviour replaces the manual style-swap.
- Toolbar emoji removed from button captions (`Open Job`, `New Job`, `Pan`,
  `Scale`, `Line`, `Area` вЂ” previously had `рџ“‚`, `пј‹`, `вњ‹`, `рџ“ђ`, `в•±`, `в–­`),
  `TxtJobName` switched from `Italic` to `SemiBold` so the active job name is
  scannable.
- Sheet-legend overlay (`DrawSheetLegendOverlay`) gets typeface fallback chain
  (`Segoe UI` в†’ `Inter` в†’ default), border darkened from `#404040` to `#303030`
  for stronger contrast on light PDFs, and the swatch border is now a
  per-colour darker variant (`fill - 60` per channel) so saturated colours no
  longer have an invisible same-colour border. `DrawLegendSignIcon` delegates
  to `MeasurementGlyph.DrawSkia`.
- Active-takeoff target bar (`ActiveTakeoffTargetBar`) gets an 18Г—18 glyph host
  to the left of the name, populated via `BuildTakeoffSwatchGlyph`, so the
  recording target advertises its type visually.
- Misc theme polish: `PanelHeader` border padding `6,5` в†’ `8,6`; scrollbar
  thumb default colour upgraded from `#A0A0A0` to `#888888` for stronger
  contrast against the light track.

## 2026-05-04

- Restored joist direction semantics to the estimator-facing meaning: the
  two-click direction line is parallel to the generated joists. The calculator
  now uses that vector as the joist run direction and spaces candidate lines
  across the perpendicular span.
- Added joist selection diagnostics to the status bar, including generated
  piece count, spacing span, O.C. spacing, candidate line count, and active
  scale, so under-counts can be checked directly from the selected area.
- Kept on-canvas measurement labels at a stable screen-space size while
  restoring the previous full line display. Joist group labels are no longer
  capped, ellipsized, or collapsed into `+N more`; value labels are now drawn
  in a screen-space pass after resetting the Skia matrix, so zoom cannot scale
  the text.

## 2026-05-03

- Added PlanSwift-style joist layout for area takeoffs:
  - any Area takeoff item can be changed to `Joist layout` from item
    Properties or the `Use Area As Joists...` context action;
  - properties store joist type, O.C. spacing in inches, direction angle,
    length calculation (`None`, `Nearest Foot`, `Nearest Even Foot`,
    `Nearest 2 Feet`), and label visibility;
  - joist direction can now be set from any selected Line measurement on the
    current sheet through the item context menu or the Properties button, so
    users do not need to type degrees manually;
  - per-joist labels are optional and default off, while the area label shows a
    compact joist summary such as `27 / 8'` or `27 / 8' avg`;
  - follow-up: joist direction is now captured as a two-point line parallel to
    the generated joists after drawing/selecting the target area, and the
    direction is stored on that area section so different areas can keep
    different joist directions;
  - direction capture now starts directly from generation without OK/No modal
    prompts: the user is put into a two-click direction mode on the sheet;
  - joist quantities now require a locked direction per area section; unready
    areas show `set direction` and are not counted with a
    default 0-degree direction;
  - joist generation now includes the far boundary joist when the area width is
    not an exact multiple of O.C. spacing, avoiding an under-count at the last
    edge;
  - canvas joist labels can expand into a PlanSwift-like list with grouped
    piece counts by rounded length, while sheet legends intentionally show
    joist takeoffs as plain Area entries with area quantity only;
  - added shared takeoff signs across UI displays: line `в•±`, count `в—‹`, area
    `в–Ў`, and joist area `в–Ўв•±` in trees, linked sheet rows, section rows,
    legends, PDF legend output, active target status, and estimating rows;
  - the calculator clips parallel joist lines to each area polygon, rounds each
    joist length by the selected method, and uses the ordered joist length as
    the item/section quantity;
  - viewport overlays, PDF export overlays, sheet legends, estimate quantities,
    CSV/export rows, job persistence, and legacy project JSON persistence now
    understand joist area items as length-based takeoffs.
- Added a detached 3D viewport window for the massing workflow:
  - `3D Massing` now has an `Open 3D Window` button next to the existing draft
    controls, and the Command Palette exposes the same action;
  - the modeless window has its own WPF `Viewport3D`, Fit/Iso/Top/Front
    controls, mouse orbit/zoom, and a source-marker list;
  - the scene renders the saved massing draft when present, falls back to a
    transient marker-built draft when possible, and still shows all saved AI
    marker points so placed source points are visible outside the right panel.
- Added first-pass multi-level 3D marker support:
  - `exterior_corner`, `wall_height_sample`, and opening markers can now carry
    `level=...`, `z=...`, and `height=...` in marker value/notes;
  - `model.json` footprints store `level`, `base_elevation`, and height, and
    openings store their source level;
  - the embedded and detached 3D previews render stacked footprint levels using
    absolute vertical elevations.
- Added first-pass 3D generation from measured takeoffs:
  - the `3D Massing` tab now has `3D From Takeoffs`, with a matching Command
    Palette action;
  - it searches `Takeoffs/Walls`, `Takeoffs/Areas`, `Floors`, `Slabs`, and
    `Sqft` folders, treats child folders such as `1st`, `2nd`, and `3rd` as
    stacked levels, and builds footprint polygons from scaled Area/SQFT
    measurements first, then falls back to scaled Line wall measurements;
  - wall height is parsed from takeoff item names/notes such as
    `ext 2x6 9.0`, `height=9 ft`, or `9 ft`;
  - default level spacing now seeds plates at `0`, `10`, `20`, `30`, etc. feet
    and seeds the roof at the last level plus the same spacing; the spacing
    prompts before build and is saved in app settings.
- Created a pre-change Git savepoint before UX/new-window roadmap edits:
  `2a07c79 Add selection clipboard and UX roadmap`.
- Reviewed `docs/UX_AND_NEW_WINDOWS_IMPLEMENTATION_PROMPT.md` against
  PlanSwift and WPF sources, then added a research review section that:
  - marks stale/overlapping tasks such as Count rename and Record rewrite;
  - recommends splitting JobPicker into recent-jobs first and thumbnails second;
  - recommends extracting shared controls/view-models before detached 3D and
    Estimating windows;
  - documents modal/modeless WPF window lifecycle risks;
  - revises the implementation order so high-risk Record behavior changes are
    last and gated by a product decision.

- Added a PlanSwift-style `Select` viewport tool:
  - toolbar button `Select` and hotkey `E`;
  - left-button drag draws a selection box on the current sheet;
  - selected measurements show stronger geometry plus selection bounds/handles;
  - `Ctrl+Click` toggles individual measurements into/out of the selection;
  - `Ctrl+A` selects all measurements on the active sheet;
  - `Delete` removes all selected measurements.
- Added measurement clipboard workflow:
  - `Ctrl+C` copies the selected measurement set;
  - `Ctrl+V` pastes copied measurements to the active sheet;
  - viewport right-click menu has Copy/Paste measurement actions;
  - paste asks whether to reuse the same takeoff items/values or create copied
    takeoff items under the current Takeoffs folder;
  - pasted measurements get new IDs, target the current sheet, use the current
    sheet scale when available, and are positioned at the cursor/right-click
    point by moving the copied set's center to that point.
- Made `Select` the default non-recording tool, including the startup state and
  the tool returned to when Record is turned off.
- Extended body drag so dragging one measurement inside a multi-selection moves
  the selected group together while still saving each affected item on release.
- Fixed Count/point group move after box selection: when multiple measurements
  are selected, clicking a selected point starts group body-drag instead of
  collapsing into single-point vertex edit.
- Added a stronger edit-target cue for the active right-side Takeoffs item:
  amber row highlight, left accent marker, larger outlined color swatch, and
  semibold item name.
- Added a compact active-target bar above the right Takeoffs tree with item
  name, type, current total, and quick `Find`, `Props`, and `Record` actions;
  item context menus now have `Set Active Target`, and the Command Palette
  exposes the same active-target actions.
- Extended that active-target bar with previous/next target switching and an
  active-sheet quantity beside the overall item total, so the current sheet
  value is visible even when the Takeoffs tree stays collapsed.
- Added a `Sheet` target action that cycles through takeoffs measured on the
  active sheet in the same order as the sheet legend, with a matching Command
  Palette action.
- Changed the target bar `Sheet` action into a picker menu: it lists all
  takeoffs measured on the active sheet in legend order, shows their
  sheet-local quantity, marks the active target, and keeps `Next Sheet Target`
  available inside the menu and Command Palette.
- Added `Shift` range multi-select to the Pages tree, the Takeoffs tree, and
  the linked takeoff rows shown under expanded sheets. `Ctrl` still toggles one
  row at a time, and `Ctrl+Shift` adds a range to the current selection.
- Preserved existing multi-selection when mouse-down starts on an already
  selected row in Pages, Takeoffs, or linked sheet takeoff rows, so group
  drag/drop starts with the whole selected set.
- Added group sibling reorder for selected Pages and Takeoffs rows: context
  menus now show `Move N Up/Down`, `Ctrl+Up` / `Ctrl+Down` moves the selected
  block, cross-parent selections stay disabled, and relative order is
  preserved.
- Anchored the Pages and Takeoffs tree views to the left by disabling
  horizontal TreeView scrolling and resetting horizontal offset after
  `BringIntoView`, preventing long nested rows from shifting the side panels.
- Wired sheet measurement selection back to the right Takeoffs tree: selecting
  one or more measurements on the canvas selects the real takeoff item rows,
  so they can be dragged/copied/moved into takeoff folders.
- Wired right Takeoffs selection back to the left Pages tree: selecting an item
  or folder expands measured sheets and highlights the linked page-side takeoff
  rows where the selected takeoffs appear.
- Added `Shift` range and `Ctrl+Click` multi-select for right-side
  section/count child rows. Their context menu now supports grouped
  `Select N on Canvas`, grouped `Move N Up/Down`, and grouped delete, with
  `Ctrl+Up` / `Ctrl+Down` moving the selected section/count block inside the
  same takeoff item while preserving relative order.
- Added drag/drop transfer for selected section/count rows: dropping onto
  another takeoff item of the same measurement type moves the selected
  measurements into that item, and holding `Ctrl` copies them with fresh
  measurement IDs and the target item's color.
- Added explicit drop feedback for section/count row transfers: valid takeoff
  item targets get a green cue, invalid targets get a red cue, and the status
  text explains type blocks such as line rows being dropped onto count items.
- Fixed right-tree multi-select to drive canvas selection as a full group:
  selecting several takeoff items/folders now selects every matching
  measurement on the active sheet instead of being overwritten by the last
  clicked takeoff item.
- Polished Takeoffs selection sync: selecting a folder, including the root
  Takeoffs folder, now treats every nested takeoff item as the current
  selection for active-sheet canvas highlighting, and clicking an already
  selected section/count row re-syncs the selected row group to the canvas.
- Added bulk Takeoffs item properties from the right-tree context menu:
  selected items can receive a shared color, notes, and a shared unit price
  when all selected items have the same measurement type. Folder context menus
  can apply the same bulk edit to nested takeoff items.
- Added section/count row bulk notes and multi-row page jump polish: selected
  child rows can replace notes together, `Go to First Page` works for a group,
  and existing grouped select/move/delete behavior remains intact.
- Extended takeoff folder defaults: folder properties now include a default
  unit price, default item notes, and a default name prefix in addition to
  type/color. New takeoff items created under that folder inherit the nearest
  configured defaults from the folder chain.
- Cleaned up right-side Record/target wording: the toolbar and active-target
  bar now show the measurement type being recorded, section/count row messages
  use `Count`/`Section` wording more consistently, and starting a drawing tool
  without a matching active target asks before creating a new takeoff target.
- Hardened measurement paste edge cases: pasting Line/Area measurements onto a
  sheet without scale now either confirms reuse of the copied measurement scale
  or blocks when no scale exists, pasted measurements are rebased to the active
  sheet, and the exact pasted section/count rows are selected in the right tree
  after paste.
- Upgraded the Estimating tab from a passive table into a small workflow
  surface: it now supports extended row selection, a current-sheet filter,
  action buttons for canvas selection/page jump/properties, group selection
  from estimate rows, and sheet-scoped item quantities/costs.
- Persisted side panel widths: the Pages and Takeoffs splitter positions are
  saved in app settings after drag/close and restored on startup, reducing
  left/right panel drift between sessions.
- Improved sheet legend order feedback: dragging linked takeoff rows now shows
  the dragged count, above/below target, and pending legend position in the
  status bar; after drop it reports the final legend position range.
- Advanced the PDF metadata learning pipeline: project learned rules are now
  applied before global learned rules, Pages context menus separate project vs
  global learned-rule review, and metadata preview rows include a `Why` column
  explaining proposed name/scale decisions and learned-conflict auto-apply
  blocks. The same project/global normalization is used for direct analysis,
  resolved-source PDF matching, and AI fallback response apply.
- Wired multi-selected linked takeoff rows to canvas selection: selecting
  multiple linked rows under a sheet selects all matching sheet measurements,
  and the context menu changes to `Select N Linked Takeoffs`.
- Added group up/down movement for multi-selected linked takeoff rows in
  sheet-local legend order, available from the context menu and `Ctrl+Up` /
  `Ctrl+Down` while preserving the selected rows' relative order.
- Extended linked-row drag/drop so dragging one selected linked takeoff row
  moves the full selected block in that sheet's legend order while preserving
  the block's relative order.
- Updated the sheet legend overlay so long legends render all entries by
  adapting into multiple columns and smaller row sizing instead of hiding rows
  behind a `more` line.
- Added PlanSwift-style takeoff data export:
  - `Export TXT` writes selected/all takeoffs as header blocks plus
    tab-separated item/value/unit rows;
  - `Export Excel` writes the same rows to a standalone `.xlsx` sheet in
    columns `J:K:L` starting at `J10`, matching the old Python UI export
    shape without requiring a new NuGet package.
- Upgraded Open Job workflow for multiple job roots: settings now store
  `JobsRootPaths`, the main `Open Job` button opens the internal JobPicker,
  `Jobs` adds/switches root folders, and the picker can filter jobs by root.
- Added `Export PDF` from the Pages panel and Command Palette. It opens a sheet
  selection dialog, can include measurement overlays and the sheet legend, and
  writes a multi-page PDF using the existing PDF renderer plus Skia PDF output.
- Side panel widths now save not only on splitter drag/close but also on panel
  width changes, so left/right expansion is persisted more aggressively.
- Verified with
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`.

## 2026-05-02

Implemented and pushed a set of OurPlaneCore workflow improvements:

- Set up Git/GitHub workflow for `C:\Users\User\Desktop\ourplanecore` and pushed the current `main` branch.
- Added PlanSwift-style Record workflow improvements for Count, Line, Area, and Scale.
- Added the first PlanSwift-style Snap and Ortho digitizer modes:
  - toolbar toggles for Snap and Ortho;
  - `F3` toggles Snap;
  - `F8` toggles Ortho;
  - Snap magnetizes to existing app-created takeoff vertices and shows a red
    square preview before click;
  - Ortho constrains Line, Area, and Scale input to 90/45-degree axes, with
    `Shift` temporarily toggling the constraint.
- Added the first PlanSwift-style page tab workflow:
  - selecting a page opens or reuses the active viewport tab;
  - page context menus include `Open in New Tab`;
  - tabs can be closed from the tab strip;
  - switching tabs preserves each tab's zoom and pan.
- Made PDF Auto Rename / Auto Scale visible in the left Pages panel:
  - `Auto Name`;
  - `Auto Scale`;
  - `Name+Scale`;
  - `AI Fill` for GPT metadata fallback queueing.
- Updated AI key lookup so the OpenAI runner checks both process-level and
  Windows user-level `OPENAI_API_KEY`.
- Added PlanSwift-style automatic folder creation based on
  `C:\Users\User\Desktop\Python\XML_ENDS\planswift_ui_v2.py` and
  `planswift_UI_manager.py`:
  - `Models/PlanSwiftFolderTemplateService.cs` carries the COM/EWP Pages folder
    templates and Standard/EWP Takeoffs trees;
  - the left Pages panel and Pages folder context menu can create standard page
    folders under the selected folder/root;
  - the right Takeoffs panel and Takeoffs folder/root context menus can create
    the standard takeoff tree under the selected folder/root;
  - `From Pages` scans CAPS-style page/folder names and creates matching
    takeoff top folders with the standard subfolder tree.
- Added a persisted `Auto` / `COM` / `EWP` folder-template mode selector so the
  user can override job-name auto detection for both Pages and Takeoffs
  template creation.
- Hardened page tabs after page tree mutations:
  - open tabs rebase their page folder path after rename, move, and cut/paste;
  - the active tab reloads after the Pages tree refresh instead of being closed.
- Changed user-facing Count labels so the UI says `Count` instead of exposing the internal `point` measurement type.
- Added page/takeoff cross-highlighting:
  - pages show takeoff count badges;
  - unscaled pages show an `unscaled` badge;
  - selecting a page highlights matching takeoffs;
  - selecting a takeoff highlights measured pages.
- Added item and section estimating rows with quantity, unit, unit price, cost, and CSV export fields.
- Added section actions from the Estimating table:
  - Go to Page;
  - Select on Canvas;
  - Rename;
  - Delete;
  - Properties.
- Added canvas-to-estimate selection sync:
  - selecting a measurement on the canvas selects the matching Estimating row;
  - selecting a section/count row focuses that measurement on the canvas.
- Added persistent section/count properties:
  - `Measurement.Name`;
  - `Measurement.Notes`;
  - JSON save/load in `measurements.json`;
  - notes export in CSV.
- Moved the Estimating list into a separate right-side `Estimating` workspace tab.
- Added an Estimating quick filter for item, section, page, type, and notes text.
- Added a visible `Notes` column and widened the main Estimating columns.
- Updated handoff docs:
  - `docs/CURRENT_OURPLANECORE_STATUS.md`;
  - `docs/PROJECT_CONTEXT.md`.

Continued Takeoffs tree workflow:

- Added copy, cut, paste, and duplicate context actions for takeoff items and
  folders.
- Added `Ctrl+C`, `Ctrl+X`, and `Ctrl+V` shortcuts for the selected Takeoffs
  tree node.
- Added drag/drop movement for takeoff items and folders into takeoff folders;
  holding `Ctrl` during drop copies instead of moves.
- Copied takeoffs preserve measurements, unit pricing, section names, notes,
  and folder metadata while regenerating measurement IDs for the new copy.
- Added true multi-select behavior to the right Takeoffs tree:
  - `Ctrl+Click` toggles takeoff items/folders into the current selection;
  - dragging a selected group moves it into a takeoff folder;
  - holding `Ctrl` during the drop copies the selected group instead;
  - context menu and shortcuts now support copy, cut, duplicate, paste, and
    delete for selected takeoff nodes;
  - selection normalization prevents double-moving a child when its parent
    folder is already selected.
- Added right-panel section/count editing under each Takeoffs item:
  - completed sections/count marks appear as child rows below their takeoff
    item;
  - child rows support Properties, Rename, Go to Page, Select on Canvas,
    Move Up, Move Down, and Delete;
  - canvas selection also syncs back to the matching child row.
- Added Takeoff Item Properties from the right tree:
  - item name, color, unit price, and notes are edited in one dialog;
  - item notes are persisted in `Data.xml`;
  - changing item color updates existing measurement colors for that item.

Continued drawing/editing and AI tools:

- Added a PlanSwift-style sheet header overlay in the PDF viewport:
  - the visible top of the sheet shows architectural scale text when it matches
    a common imperial preset, for example `Scale: 1/8" = 1' 0"`;
  - the right side of the header shows sheet size from PDF points in inches,
    for example `36.00 x 24.00`;
  - if no scale is set, the overlay shows `Scale: not set`.
- Added canvas right-click edit actions for selected measurements:
  - Properties;
  - Rename;
  - Delete;
  - Insert Vertex Here;
  - Remove Nearest Vertex.
- Vertex insert/remove actions update the existing measurement, refresh totals,
  and queue autosave like drag-handle edits.
- Tightened missing-scale drawing behavior:
  - Line and Area Record now require sheet scale before the first point can be
    placed;
  - the viewport blocks unscaled Line/Area geometry and tells the user to use
    Scale or PDF Auto Scale first;
  - Count remains available without scale.
- AI Assist requests now save visual crop PNGs into `AI_Context/crops` and
  record the crop path plus PDF crop coordinates in the project AI context.
- AI Assist request actions now create structured pending request JSON files in
  `AI_Context/requests` so a future OCR/LLM worker has a direct queue to read.
- Added explicit `Save AI crop here` / `Save measurement AI crop` commands for
  creating context evidence without changing estimating data.
- Added AI Inbox review actions:
  - double-click or Enter opens full observation details;
  - right-click can open details, jump to the matching page, open the linked
    crop PNG, open the crop folder, open request JSON, open project context, or
    refresh the Inbox;
  - `F5` refreshes the Inbox.
- Added manual AI response capture from Inbox:
  - responses are saved to `AI_Context/responses`;
  - the matching request JSON status is updated to `done`;
  - the full observation dialog shows request/response details;
  - the Inbox preview shows the request status prefix.
- Added per-sheet PDF layer manifests:
  - pages with detected PDF layers now write a separate `layers.json` beside
    `source.json`;
  - the file records the source PDF, page index/page number, generation time,
    layer count, and visible layer list;
  - stale `layers.json` files are removed when a page source is rewritten
    without layer metadata.
- Wired PDF layer metadata into AI request records:
  - pending request JSON now includes `page_folder`, `layer_manifest_path`,
    `layer_count`, and the visible `layers` list when a page has layer data;
  - AI Inbox can open the matching page `layers.json` file from the request
    context menu.
- Added the first real AI runner:
  - AI Inbox has `Run AI` / `Run AI Request`;
  - the runner reads `OPENAI_API_KEY` and optional
    `OURPLANECORE_OPENAI_MODEL` from the environment;
  - selected or next pending request JSON is sent to OpenAI with crop PNG and
    layer context;
  - model output is saved to `AI_Context/responses`, the request status becomes
    `done` or `failed`, and raw provider JSON is saved beside the response.
- Added action-draft extraction for AI responses:
  - `AI_Context/actions/{requestId}.json` is created after automatic or manual
    AI response capture;
  - fenced JSON from the response is parsed into reviewable action records with
    type, label, page, measurement type, confidence, notes, and PDF points;
  - AI Inbox can open the action draft JSON, but the app does not apply drafted
    geometry automatically yet.
- Added canvas preview for AI action drafts:
  - AI Inbox has `Preview Action Draft` and `Clear Action Preview`;
  - draft points render on the PDF canvas as dashed cyan line/area/point
    overlays;
  - preview remains read-only and does not create measurements.
- Added apply support for reviewed AI action drafts:
  - AI Inbox has `Apply Action Draft`;
  - applying confirms with the user, then creates real line/area/point
    measurements from draft PDF points;
  - a matching active takeoff item is reused, otherwise an AI-colored takeoff
    item is created;
  - created measurements are saved, totals/page badges refresh, and the draft
    records `applied_measurement_ids` with status `applied`.
- Added the first AI marker capture MVP:
  - viewport right-click has `Save AI marker here`;
  - measurement right-click has `Save measurement as AI marker`;
  - the marker dialog captures marker type, sample kind, optional value, and
    note;
  - each marker saves crop evidence under `AI_Context/crops` and structured
    marker JSON under `AI_Context/markers`;
  - markers reload per sheet and render on the PDF canvas with distinct overlay
    colors;
  - AI Inbox shows marker records and can open the marker JSON file.
- Added the first AI marker review workflow:
  - AI Inbox has marker type and sample-kind filters;
  - marker rows can edit type, sample kind, value, and note;
  - marker rows can delete the active marker JSON from overlay/Inbox while
    keeping crop evidence and the append-only observation log.
- Added the first AI marker organization/export workflow:
  - AI Inbox has `Set` and `Export` actions;
  - `Set` saves the current marker type/sample filter as a marker set under
    `AI_Context/marker_sets`;
  - `Sets...` and the AI Inbox context menu can apply saved marker sets back
    to the filters, rename sets, delete set JSON, or open set JSON;
  - `Export` writes the current visible filtered markers plus marker sets into
    `AI_Context/exports/markers_context.json`;
  - marker context menus can hide the selected marker type from the canvas
    overlay and restore all marker types;
  - hidden marker types are now persisted per job in
    `AI_Context/project.json`.
- Added the first crop-bookmark batch workflow:
  - AI Inbox crop/marker rows can be saved as bookmarks under
    `AI_Context/crop_bookmarks`;
  - the Inbox has `Run New`, which sends only bookmarks with `status=new` to
    OpenAI;
  - each bookmark records request id, response id, action draft id, result
    summary, processed time, and `done` / `failed` status;
  - the OpenAI prompt for `crop_bookmark_request` includes the exported
    `markers_context.json` when available.
- Added the first 3D massing data service:
  - `Models/SmartMassingDraftService.cs` defines the draft JSON model for
    `AI_Context/3d_massing/model.json`;
  - the service can build a draft from `exterior_corner`, `wall_height_sample`,
    `roof_note`, and `roof_edge_sample` markers;
  - the draft records source marker ids, assumptions, unresolved questions,
    approximate footprint points, wall height, and roof notes.
- Added the next 3D Massing UI slice:
  - the placeholder `3D Massing` tab now has a source-marker review table;
  - each row shows marker role, type, page, PDF point, draft point, and status;
  - selected rows can jump to the source sheet, open marker JSON, or open crop
    evidence;
  - the tab now includes a lightweight top-down footprint preview;
  - selecting a source marker highlights the matching draft point in the
    preview and shows marker value/note, PDF point/rect, crop status, and JSON
    path in a detail panel;
  - `Jump` opens the source sheet and centers the viewport on the marker PDF
    point where possible;
  - this remains a reviewable 2D/text/table workflow, not an orbit/3D viewer.
- Added the first roof-modeling slice for 3D Massing:
  - `SmartMassingRoof` now stores reviewable `guides` in
    `AI_Context/3d_massing/model.json`;
  - roof notes can infer basic `gable`, `hip`, `shed`, and `low_slope` roof
    types plus pitch text;
  - the service creates eave outline, ridge/hip-ridge, shed slope-arrow, or
    low-slope cap guides from reviewed markers and footprint bounds, with an
    explicit roof-axis candidate instead of a ridge when type is still unknown;
  - the `3D Massing` preview draws those roof guides over the footprint for
    quick review;
  - this is still a draft guide, not a roof solver or accepted BIM geometry.
- Added the first Auto Roof recognition slice:
  - the `3D Massing` tab now has `Auto Roof`, which queues a
    `roof_recognition_request`;
  - the request saves a large current-sheet/marker-bounds crop and attaches
    nearby marker evidence crops when available;
  - `OpenAiRequestRunner` has a roof-specific prompt that asks only for
    reviewable `roof_note`, `roof_edge_sample`, `ridge_sample`,
    `valley_sample`, `roof_high_edge`, `roof_low_edge`, and
    `overhang_sample` candidates;
  - the action review dialog can be reused in marker mode, so accepted Auto
    Roof candidates become normal `ai_marker` records instead of takeoff
    measurements;
  - accepted roof markers still require `Build 3D Draft` before they affect
    `AI_Context/3d_massing/model.json`.
- Added editable roof review:
  - `SmartMassingRoof` now stores review status, reviewed timestamp, and review
    notes;
  - `SmartMassingRoofGuide` now stores review status;
  - the `3D Massing` tab has `Review Roof`;
  - `Dialogs/RoofReviewDialog.cs` lets the user edit roof type, pitch,
    confidence, notes, review notes, and guide rows;
  - guide rows can be kept/rejected, and kind/label/confidence/points/notes can
    be edited before saving;
  - saving writes reviewed roof state back to `AI_Context/3d_massing/model.json`
    and refreshes the preview/summary.
- Added the first actual 3D shell preview:
  - `SmartMassingRoof` now stores derived `planes`;
  - `SmartMassingDraft` now stores whole-draft reviewed timestamp and review
    notes;
  - `SmartMassingDraftService.SaveDraft` refreshes derived geometry before
    writing `model.json`;
  - gable/ridge/axis guides generate two roof surface candidates;
  - shed/high-low guides generate a sloped roof plane;
  - low-slope/unknown fallback generates a cap plane;
  - the `3D Massing` tab now embeds WPF `Viewport3D` and renders floor,
    extruded walls, and roof planes;
  - Fit/Iso/Top/Front camera controls and mouse orbit/zoom are wired;
  - `Accept 3D` marks the whole draft as reviewed AI context without creating
    estimating quantities.
- Added the next 3D review slice:
  - `window_sample`, `door_sample`, and `opening_sample` markers now become
    draft openings in `AI_Context/3d_massing/model.json` with source marker id,
    nearest wall index, projected center point, approximate width/height,
    confidence, and notes;
  - the WPF `3D Massing` preview now renders projected opening rectangles and
    source/opening marker pins;
  - floor, wall, roof, opening, and pin geometry now carries source metadata;
  - clicking 3D geometry highlights the selected object, updates status/details
    text, and selects the first linked source marker row when available;
  - `Review Openings` opens an editable keep/reject grid for projected
    openings, saving kept rows as `reviewed` and unchecked rows as `rejected`
    evidence in `model.json`;
  - opening review outcomes are appended to project/global marker feedback
    learning as `event_type=3d_opening_projection_review`;
  - `Accept 3D` now writes a timestamped accepted-draft snapshot under
    `AI_Context/3d_massing/snapshots`;
  - hip roof type / `hip_ridge` guide drafts now generate four reviewable roof
    plane candidates from ridge/guide and footprint bounds.
- Added the first PDF-first Auto Rename / Auto Scale implementation slice:
  - `Tools/pdf_layers_helper.py` now has a `sheetmeta` action in CLI and worker
    modes;
  - `sheetmeta` reuses existing PyMuPDF text/word/layer infrastructure and
    returns sheet label, sheet key, title, suffix, skip-scale, title/body scale
    candidates, selected scale, scale ratio, meters-per-PDF-point, page size,
    layers, warnings, and rename candidate;
  - `Models/PdfSheetMetadataService.cs` calls the helper and normalizes
    metadata for the WPF app;
  - `Models/OurPlaneCoreJob.cs` can now read/write per-page
    `source_pdf.json`;
  - Pages context menus now expose Analyze PDF Metadata, Auto Rename from PDF,
    Auto Scale from PDF, Auto Rename + Scale from PDF, Open `source_pdf.json`,
    and Capture Final Learning Snapshot;
  - auto apply is review-gated through a WPF preview grid where rename and
    scale can be checked/unchecked per page; obvious same-folder rename
    conflicts are shown as warnings and are not checked by default;
  - accepted/apply/manual snapshot outcomes are written into
    `SmartLearningStore`;
  - if an imported source PDF is missing, the metadata service can search the
    E-Wood source folder pattern and match pages by sheet key.
- Added GPT/image fallback workflow for unresolved PDF sheet metadata:
  - Pages context menus now include `Queue GPT Metadata Fallback`;
  - fallback saves a bottom/title-block crop PNG into `AI_Context/crops`;
  - fallback creates a `pdf_sheet_metadata_fallback` request in
    `AI_Context/requests` with deterministic metadata, page/layer context, and
    a strict JSON response prompt;
  - `OpenAiRequestRunner` now uses a sheet-metadata-specific prompt for this
    request type;
  - AI Inbox shows these requests as `Sheet Meta`;
  - after a response is saved, AI Inbox can run `Apply Sheet Metadata Response`,
    parse the JSON response into `source_pdf.json`, and open the same preview
    grid before rename/scale apply;
  - fallback queueing skips pages that already have a non-failed
    `pdf_sheet_metadata_fallback` request.
- Added learning-based confidence hints for PDF metadata preview:
  - `SmartLearningStore` compares proposed metadata against global
    accepted/corrected/manual records;
  - preview rows show `Confidence`;
  - repeated supporting records can raise confidence to `learned-medium` or
    `learned-high`;
  - learned conflicts add a warning and keep rename/scale unchecked by default.
- Added learned-rule distillation:
  - final learning snapshot now writes project and global `learned_rules.json`;
  - repeated title-token/suffix outcomes with enough support become explicit
    rules with support count, confidence, skip-scale vote, and common scale.
- Connected distilled learned rules back into detection:
  - if deterministic metadata is missing suffix or scale, global learned rules
    can fill those fields based on matching title tokens;
  - preview warnings call out when a learned rule was applied.
- Added learned-rule review controls:
  - distilled rules now carry an `enabled` flag;
  - disabled rules are ignored by future PDF metadata detection;
  - regenerated project/global `learned_rules.json` preserves previously
    disabled rules by stable rule id;
  - Pages context menus now include `Review Learned Rules...` for the global
    rule set.

Verified after the latest changes:

```powershell
dotnet build .\ourplanecore.sln
```

Result:

```text
Build succeeded.
Warnings: 0
Errors: 0
```

Read-only State Str structural PDF diagnostic:

```text
S-100 FOUNDATION PLAN              -> s100 f    -> 1/8" = 1'0"
S-101 SECOND FLOOR FRAMING PLAN    -> s101 2nd  -> 1/8" = 1'0"
S-104 ROOF FRAMING PLAN            -> s104 rf   -> 1/8" = 1'0"
S-500 TYPICAL WOOD FRAMING DETAILS -> s500 d    -> no scale
S-503 WOOD TRUSS SECTIONS          -> s503 sec  -> 3/8" = 1'0"
```

Latest pushed commits:

```text
5e3ac57 Show estimate notes column
3d1ded4 Add estimating quick filter
e5b5874 Move estimating list into workspace tab
06e51b5 Add section notes properties
4be08bb Use count-specific workflow labels
ba8bf22 Sync canvas and estimate section selection
```

## Queued Tasks

- Continue PlanSwift-style manual takeoff workflow:
  tighten right-side Takeoffs editing, item properties, section management,
  and drawing/editing affordances before changing the Estimating tab further.
- Continue the AI marker training workflow from
  `docs/AI_MARKER_TRAINING_IDEAS.md`: first-slice `Find Similar From Marker`,
  auto-created candidate crop bookmarks, and failed-bookmark retry controls are
  in place; next add cross-sheet batch search and a dedicated bulk marker
  review panel.
- Add the lightweight 3D massing viewer idea from
  `docs/AI_3D_MASSING_VIEWER_IDEAS.md`: use exterior corner markers, wall
  height samples, and roof notes to build a simple separate-tab 3D draft for
  visual review, not BIM-grade modeling. The data service and placeholder
  `3D Massing` tab are in place, including source-marker jump/JSON/crop
  actions, a top-down footprint preview, selected-marker highlighting, and
  evidence details. The draft now also includes roof guide overlays for basic
  ridge/hip/shed/low-slope/unknown-axis review plus explicit
  `ridge_sample`, `valley_sample`, `roof_high_edge`, `roof_low_edge`, and
  `overhang_sample` marker support. `Auto Roof` can now queue reviewable roof
  marker candidates and save accepted candidates as markers. Editable roof
  review, simple roof planes, a WPF 3D shell preview, and `Accept 3D` are also
  in place. The 3D preview now also supports object/source selection, marker
  pins, projected opening rectangles, and `Review Openings` keep/reject editing
  from window/door/opening markers; next improve complex valley/multi-roof plane
  generation, include opening feedback in future prompts, and add a visible
  snapshot/history picker if accepted snapshots need comparison.
- Continue hardening the PDF-first Auto Rename / Auto Scale workflow:
  add more review details for learned-rule conflicts and per-project rule
  scope; the first global rule enable/disable UI is in place.
- Add the self-learning feedback loop for Auto Rename / Auto Scale:
  `SmartLearningStore` now defines per-project and global JSONL storage for
  detector proposals, user corrections, final manual page state, and project
  learning summaries. Future preview/apply UI should append accepted,
  corrected, rejected, and manual-final records.
- Provider configuration UI for API key/model selection is in place; continue
  hardening the model list and status messaging as needed.
- Add automatic request processing and better general failed-request retry
  controls beyond crop bookmarks.
- Harden the SmartTrace review UI with source links and per-row canvas focus.
- Continue hardening Count wording in remaining secondary dialogs.

## 2026-05-02 Parallel Agent Merge Review

Integrated the three parallel Codex agent slices into the shared worktree:

- Tab 1 `Find Similar From Marker`: AI marker Inbox rows can queue
  `find_similar_marker_request`; OpenAI prompt context includes marker crop,
  source marker JSON, exported marker context, and page/layer context.
- Tab 2 crop bookmark retry/auto-new: `Retry Failed` processes only failed
  bookmarks; successful action drafts can create guarded new candidate
  bookmarks while preserving `Run New` as `status=new` only.
- Tab 3 SmartTrace review UI: `Review Action Draft` supports accept/reject,
  target takeoff selection, preview, and apply-only-accepted geometry with
  review indices recorded in the draft JSON.
- Added nearby-sheet context for `Find Similar From Marker`: the request now
  saves a wider crop around the source marker under `AI_Context/crops`, stores
  it in `context_crop_paths`, and sends it to OpenAI alongside the marker crop.
- Added marker candidate feedback learning: reviewing a `Find Similar From
  Marker` action draft appends accepted/rejected rows to
  `AI_Context/learning/marker_feedback.jsonl` and the global learning folder;
  later marker prompts include recent feedback for the same source marker or
  marker type.
- Added feedback-aware marker context export: `markers_context.json` now
  includes recent `marker_feedback` records plus `marker_quality` summaries
  with accepted/rejected/applied counts and average confidence.
- Added visible marker quality in AI Inbox: marker rows now append compact
  feedback text such as accepted/rejected/applied counts and average confidence
  to the row preview.
- Second-wave shared changes were also present and integrated: toolbar
  `AI Settings`, takeoff folder `Folder Properties...`, and the `3D Massing`
  draft panel.
- Integration checks: no conflict markers, no hardcoded OpenAI test key,
  all XAML event handlers resolved, `dotnet build .\ourplanecore.sln` passed
  with 0 warnings and 0 errors, and a short `dotnet run --project
  .\ourplanecore.csproj --no-build` startup smoke test stayed alive.

## 2026-05-02 3D Massing Review Slice

- Extended `SmartMassingDraftService` so `window_sample`, `door_sample`, and
  `opening_sample` markers become draft `openings` in
  `AI_Context/3d_massing/model.json` with source marker id, nearest wall index,
  projected center point, approximate type-based width/height, confidence, and
  review notes.
- Extended the WPF `3D Massing` preview with projected opening rectangles,
  source/opening marker pins, and object metadata for floor, wall, roof,
  opening, and pin geometry.
- Added first-pass 3D hit selection: clicking 3D geometry highlights the
  selected object, updates the status/details line, and selects the first
  linked source marker row when one exists so existing `Jump`, `Marker JSON`,
  and `Crop` actions can be used.
- Added `Review Openings`, an editable review grid for projected openings. The
  user can keep/reject rows, edit type, wall index, center point, width/height,
  confidence, and notes. Kept rows save as `reviewed`; unchecked rows save as
  `rejected` evidence.
- Added opening projection feedback learning: each `Review Openings` save
  appends accepted/rejected records to project/global `marker_feedback.jsonl`
  with `event_type=3d_opening_projection_review`.
- Added accepted 3D snapshots: `Accept 3D` writes a timestamped copy of
  `model.json` under `AI_Context/3d_massing/snapshots` after marking the draft
  reviewed.
- Added first-pass hip roof surface generation: `hip` roof type or `hip_ridge`
  guides create four reviewable `hip_roof_plane` candidates instead of the
  generic two-plane ridge fallback.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\\cache\\verify_build\\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors. Normal `bin\\Debug` build output was
  still locked by the currently running `OurPlaneCore` process.

## 2026-05-02 Editing, Layer AI, and Page Sorting Fixes

- Fixed canvas measurement editing friction:
  - blue vertex handles now have a larger hit target;
  - dragging a handle still moves one vertex;
  - dragging the measurement body now moves the whole line/area/count
    measurement instead of only selecting it and doing nothing.
- Added right-click PDF layer AI context:
  - each layer row/checkbox/highlight checkbox has `Save Layer Info for AI`;
  - `Queue AI Request for This Layer` creates a pending
    `pdf_layer_ai_request`;
  - saved context includes selected layer number/name/visible/highlight state,
    all cached page layers, and `layers.json` when available.
- Added visible page sorting:
  - `Page Setup` now has `Sort A/S`;
  - Pages context menus also expose `Sort A/S into Arch/Struct`;
  - A sheets move to `Pages/00. imported/Arch`, S sheets to
    `Pages/00. imported/Struct`, trailing `-` names to `Pages/--------others`,
    and those folders are sorted A-Z after the move.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\\cache\\verify_build\\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors. Conflict-marker/hardcoded-key scan found
  no active repo secrets or merge markers.

## 2026-05-02 Job3 Measurement Visibility and Left Panel Follow-Up

- Confirmed `C:\Users\User\Desktop\check\test\JoB#3` still has saved
  measurement JSON under `Takeoffs`; the regression was display/edit flow, not
  data loss.
- Hardened measurement-to-page matching:
  - job open repairs saved `PageFolder` references by current page path or
    unique page folder name;
  - page/folder move and `Sort A/S` now rebase measurement `PageFolder`
    references immediately;
  - viewport page filtering now compares normalized paths case-insensitively
    instead of raw strings.
- Added `Repair Links` to the left `Page Setup` panel so an already-open job can
  reconnect stale measurement page links without reopening the job.
- Improved edit dragging:
  - existing measurements can be grabbed in any tool mode when no new drawing is
    in progress;
  - body/vertex drag repaints continuously but saves/recalculates once on mouse
    release, avoiding tree/table rebuilds on every tiny mouse move.
- Improved open-job behavior: if no specific page is requested, the app opens
  the last page from that job or the first available sheet, so a project no
  longer opens into an empty viewport that looks like missing measurements.
- Left Pages panel now groups controls into expandable `Page Setup`, `PDF Auto`,
  and `PDF Layers` sections. `Sort A/S`, `Auto Folders`, and `Repair Links` are
  visible in the expanded `Page Setup` section.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\\cache\\verify_build\\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.

## 2026-05-02 Job2 Legacy Page Repair and Drag Follow-Up

- Confirmed `C:\Users\User\Desktop\check\test\JoB#2` has saved measurements in
  the Takeoffs tree, but their `page_folder` values still pointed to legacy
  import folders `Pages\Page 2` and `Pages\Page 3`, which no longer exist after
  sheet auto-naming.
- Extended measurement link repair so old `Page N` references can map to the
  current unique page whose `source.json` PDF page index is `N - 1`. For the
  current Job2 data this resolves:
  - `Page 2` -> `Pages\s100 f`;
  - `Page 3` -> `Pages\s101 2nd`.
- Hardened vertex/body drag again: movement now uses mouse screen delta divided
  by current zoom from the original drag start, so the handle/measurement follows
  the cursor instead of relying on repeated absolute PDF-point recalculation.
- Hardened edit entry again: selected measurements are hit-tested first with a
  larger grab radius, all vertex/body hit targets are larger, and clicking an
  existing measurement cancels any accidental in-progress Line/Area input before
  starting the edit drag.
- Fixed the canvas/tree selection feedback loop that could interfere with drag:
  viewport selection now updates the Takeoffs tree section row without letting
  the tree handler call `SelectSectionOnCanvas` / `FocusMeasurement` back into
  the viewport during the same mouse action.
- Hardened drag capture further: edit drag no longer depends on
  `MouseMove.LeftButton == Pressed`, capture starts before firing selection
  events, and main-window Estimate/Takeoffs sync is skipped while the left
  mouse button is down.
- Replaced remaining measurement/page raw string comparisons in the main window
  with normalized path comparison so left-page badges, right-takeoff
  highlights, scale propagation, and "Select on Canvas" use the same page
  matching rule as the viewport.
- `Repair Links` and job-open repair now report unresolved stale page links in
  the status text when a measurement still points to a missing page that cannot
  be matched safely.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\\cache\\verify_build\\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.

## 2026-05-03 Measurement Repair and Editing Final Postmortem

- User confirmed the final repair worked: previously missing measurements became
  visible again and line/vertex editing started working.
- Final confirmed root causes:
  - missing canvas measurements were saved in `Takeoffs`, but their
    `Measurement.PageFolder` no longer matched the renamed page folder;
  - `JoB#2` specifically used stale `Pages\Page 2` / `Pages\Page 3` references
    after sheets had been auto-named to `s100 f` / `s101 2nd`;
  - initial edit fixes improved hit testing but drag was still interrupted by
    mouse capture / selection-sync behavior between the viewport, Estimate
    table, and Takeoffs tree;
  - relying on transient `MouseMove.LeftButton == Pressed` was unsafe during
    captured WPF/Skia mouse movement.
- Final durable fixes:
  - job open and `Repair Links` repair stale measurement links;
  - legacy `Page N` links map to a unique current `source.json` PDF page index;
  - viewport and main-window code use normalized page-folder comparison;
  - `Repair Links` is directly visible under `Import PDF`, next to `Sort A/S`,
    and is also available in the Pages context menu;
  - edit drag starts capture before selection events, continues until
    mouse-up/lost-capture, and does not depend on per-move left-button state;
  - main-window Estimate/Takeoffs selection sync is skipped while the left mouse
    button is down so right-side UI cannot re-enter viewport focus during drag.
- Added the detailed future-handoff file:
  `docs/MEASUREMENT_PAGE_LINK_AND_EDITING_POSTMORTEM.md`.
- Regression rule for future agents: if measurements exist in the right tree but
  not on the canvas, inspect `measurements.json` `page_folder` before debugging
  drawing transforms or `Data.xml`.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\\cache\\verify_build\\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors after the working fix.

## 2026-05-03 Best Practices Addendum and Quick UX Cleanup

- Accepted the parallel best-practices research as roadmap guidance, with a
  caveat that its source URLs were not web-verified because WebSearch/WebFetch
  were blocked in that agent sandbox.
- Recorded the new architecture queue in
  `docs/UX_AND_NEW_WINDOWS_IMPLEMENTATION_PROMPT.md` and
  `docs/OURPLANECORE_TASK_ROADMAP.md`: shrink/split future
  `MainWindow.xaml.cs` work, AvalonDock as a later spike, Command Palette,
  screen-pixel Snap v2 glyphs, crash-recovery snapshots, HelixToolkit for the
  future 3D viewer extraction, sample/recent onboarding, and CSI/ClosedXML
  estimating hardening.
- Implemented the revised quick visual cleanup slice:
  - added shared light/dark toolbar hover and pressed brushes;
  - kept active tool buttons visually active while hovered/pressed;
  - changed the XAML startup label from `Point` to `Count` so the wrong word no
    longer flashes before constructor normalization;
  - moved Pages/Takeoffs header styling to a shared `PanelHeaderBorder` style;
  - made `PDF Layers` start collapsed so the left panel opens cleaner.

## 2026-05-03 Command Palette First Slice

- Added `Dialogs/CommandPaletteDialog.cs`: searchable command window with
  filter, keyboard navigation, unavailable-command reasons, Enter/double-click
  execution, and Escape cancel.
- Added `MainWindow.CommandPalette.cs` instead of growing
  `MainWindow.xaml.cs`. The partial builds command metadata and dispatches to
  existing handlers for File, View, Tools, Edit, Pages, PDF Layers, Takeoffs,
  AI, and 3D Massing actions.
- Wired `Ctrl+Shift+P` to open the Command Palette and `Ctrl+S` to the existing
  Save handler.
- Command Palette intentionally reuses current command handlers; it does not
  create a second command system yet.

## 2026-05-03 Recent Jobs / JobPicker Lite

- Added recent-job persistence to `Models/AppSettingsStore.cs`. Successful
  `OpenJob` calls now prepend/update an LRU `RecentJobs` list in
  `%APPDATA%\OurPlaneCore\settings.json`.
- Added `Dialogs/JobPickerDialog.cs`: searchable recent/jobs-root picker with
  job name, last-opened time, source, status, path, keyboard navigation, Open,
  Browse Job, Jobs Folder, New Job, and Cancel actions.
- Added `MainWindow.JobPicker.cs` to keep picker/open/new-job workflow out of
  `MainWindow.xaml.cs`.
- Wired `Ctrl+Shift+O` and Command Palette `Open Recent Job` to the picker.
- Preserved current startup behavior: a valid last job still auto-opens. If the
  last job is missing and recent/jobs-root entries exist, the picker is shown.
- Deferred thumbnails, pin/unpin, remove-from-recent, and sample/empty-state
  CTA to later slices so this PR stays independent of PDF rendering.

## 2026-05-03 JobPicker Background Thumbnails

- Added `Models/JobThumbnailService.cs`. It finds the first renderable PDF page
  in a job, uses the existing PDF render service, fits it into a small PNG, and
  saves it under `%APPDATA%\OurPlaneCore\thumbnails\{job-hash}.png`.
- Extended `RecentJobInfo` with `ThumbnailPath`; `AddRecentJob` preserves an
  existing thumbnail path when the LRU row is refreshed.
- After successful `OpenJob`, thumbnail generation is queued on a background
  task. It updates the matching RecentJobs row when the PNG is ready and stays
  silent when a new/empty job has no renderable PDF yet.
- `JobPickerDialog` now has a `Preview` column with a themed placeholder and
  loads thumbnails with `BitmapCacheOption.OnLoad` so PNG files are not locked.

## 2026-05-03 JobPicker Pin and Cleanup

- Extended `RecentJobInfo` with `IsPinned`.
- Added recent-list helpers in `AppSettingsStore`: pin/unpin, remove, and trim
  while preserving pinned rows.
- Added a JobPicker row context menu for `Pin to Recent`, `Unpin from Recent`,
  `Open Folder in Explorer`, and `Remove from Recent`.
- Pinning a jobs-root row creates/updates a RecentJobs entry; removing a row
  removes only the recent-list entry and never deletes the job folder. If the
  job still lives under the active jobs root, the row remains visible as a
  `Jobs Folder` row.

## 2026-05-03 Sample Job Onboarding

- Added `Models/SampleJobService.cs`, which creates a local sample job under
  the configured JobsRoot or `Documents\OurPlaneCore Jobs`.
- Added `OurPlaneCoreJobStore.CreatePageFromPdf` so generated/sample workflows
  can add a single page without going through the multi-page import dialog.
- Added a generated one-page sample PDF plus preloaded line, area, and count
  takeoff items with sample measurements, notes, unit prices, and page scale.
- Added a `Sample Job` action to `JobPickerDialog`; the picker now works as a
  first-run empty state even when the recent/jobs-root list is empty.
- Added `Create Sample Job` to the Command Palette.

## 2026-05-03 Sheet Legend and Suffix Page Sorting

- Added a compact sheet legend overlay in the PDF viewport. The active sheet now
  shows measured takeoff item colors, names, and sheet-local quantities for the
  measurements visible on that page.
- Added a right-click `Legend` toggle on the PDF viewport. The toggle hides or
  shows the sheet legend and persists the choice in app settings.
- Added right-click overlay controls for legend position, legend size, sheet
  scale/size label size, custom size multipliers, and whether those overlays
  scale with page zoom.
- Added `D/Sec/WT` next to `Sort A/S` in the left Pages panel, plus the same
  action in Pages context menus and the Command Palette.
- The new suffix sort preserves the existing A/S sorter and handles the second
  pass separately: `d` pages move to `details struct` or `details arch`,
  `sec` moves to `sections`, `u` moves to `units`, and `v` / `wt` / `ft` /
  `sv` / `sw` pages are moved to the Pages root and ordered at the top.
- Hardened measurement paste and drag-refresh behavior so right-side tree
  refreshes do not re-center the viewport and make the sheet appear to shift
  sideways.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\\cache\\verify_build\\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.

## 2026-05-03 Collapsed Pages and Takeoffs Trees

- Pages and Takeoffs now open collapsed after a job loads, even when the app
  restores the last sheet first.
- Added compact `-` / `+` controls in the Pages and Takeoffs headers to
  collapse or expand the whole tree on demand.
- Takeoff items no longer auto-expand just because they contain section/count
  child rows; explicit section navigation can still expand the needed item.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\\cache\\verify_build\\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.

## 2026-05-03 Page Sheet Takeoff Legend Order

- Each page node in the left Pages tree can now expand to show the real takeoff
  items that have measurements on that sheet.
- Page takeoff child rows are linked to the actual right-side Takeoffs items,
  but they intentionally expose no edit/delete actions for the real takeoff.
- The only sheet-local action is changing that page's takeoff order with
  `Move Up in Legend` / `Move Down in Legend` or `Ctrl+Up` / `Ctrl+Down`.
- The same sheet-local order can now be changed by dragging a linked takeoff
  row above or below another linked takeoff row under the same page.
- During that drag, the target row shows a green before/after insertion cue so
  the legend-order drop target is visible before release.
- The per-page order is saved in the page `source.json` as
  `legend_takeoff_order` and drives only the sheet legend order.
- The active linked takeoff row under a page now gets an edit-mode style cue:
  stronger color swatch, bold label, and active background.
- Selecting a linked takeoff row under a page now also selects that takeoff's
  measurements on the active sheet canvas.
- Selecting a measurement on the canvas now activates its takeoff and selects
  the linked takeoff row under the current page, closing the sync loop.
- Selecting a takeoff item or section in the right Takeoffs tree now also
  selects the matching linked takeoff row under the current page when that
  takeoff has measurements on the active sheet.
- Selecting a takeoff item in the right Takeoffs tree now also selects that
  takeoff's measurements on the active sheet canvas while keeping the item row
  as the active edit target.
- Page and linked-takeoff context menus now include `Sort Sheet Legend A-Z` and
  `Reset Sheet Legend Order` for quick sheet-local legend order cleanup.
- Linked takeoff rows under a page now show their 1-based legend position so
  the saved sheet legend order is visible in the Pages tree.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\\cache\\verify_build\\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.
