# OurPlaneCore Task Roadmap

This is a **historical feature map and backlog**, retained for product context.
Its older paths, counts and priority labels do not select the current release,
template source or implementation order. Current authority is the
[technical handoff](PROJECT_CONTEXT.md), [master plan](OURPLANCORE_MASTER_REMEDIATION_AND_RELEASE_PLAN.md)
and [2026-09-06 improvement plan](70-architecture-refactor/IMPROVEMENT_PLAN_2026_09_06.md).

Current delta: separate **2.2.7-preview / 42c44b0 / .NET 9**, build 0/0,
807/807 C# and 29/29 Python. Data-safety/bulk-operation guards, common paste
with explicit Same/New/Cancel and Undo, custom shortcuts, and measured
save/render fixes are delivered. [Evidence](STRATEGY_APP_EVIDENCE_2026_09_06.md)
distinguishes final shortcut launch from earlier real-project UI/native runs.

Next priorities are protected general settings, the unresolved Excel gate,
reproducible release/runtime checks, workspace capacity and backup, shared
native memory accounting, responsive trees, dirty-only Save, visible AI
cancellation/typed actions, and one state-owner extraction. The .NET 10 spike
was performed; the main runtime has not migrated. Historical "next" items
below require current caller/evidence checks before being scheduled.

## Historical Feature Inventory

### Core App

- WPF app targets `net9.0-windows`.
- Jobs can be created, opened, remembered, and reopened.
- App remembers last job, last page, unit mode, theme, viewport background, and
  folder-template mode.
- Dark theme has broad control styling so panels and text stay readable.
- Toolbar buttons now have shared hover/pressed theme brushes, active tool
  buttons keep a stable active color on hover, Pages/Takeoffs headers use one
  themed header style, and the PDF Layers panel starts collapsed so the left
  panel is less noisy.
- Command Palette first slice is wired on `Ctrl+Shift+P` through
  `Dialogs/CommandPaletteDialog.cs` and `MainWindow.CommandPalette.cs`. It
  searches existing app commands, shows unavailable reasons, dispatches to
  existing handlers, and adds `Ctrl+S` Save binding.
- Recent Jobs / JobPicker lite is wired through
  `Dialogs/JobPickerDialog.cs` and `MainWindow.JobPicker.cs`. Recent jobs are
  stored in `%APPDATA%\OurPlaneCore\settings.json`, `Ctrl+Shift+O` opens the
  picker, the picker filters jobs and can browse/open/create, and thumbnails
  are intentionally deferred.
- JobPicker background thumbnails are wired through
  `Models/JobThumbnailService.cs`. After `OpenJob`, the app renders the first
  available PDF page to `%APPDATA%\OurPlaneCore\thumbnails` and stores the PNG
  path on the matching RecentJobs entry.
- JobPicker recent-list cleanup is wired: right-click rows can pin/unpin, open
  the job folder in Explorer, or remove a row from Recent. Pinned rows are
  preserved when the recent list is trimmed.
- JobPicker first-run onboarding is wired: the picker can open even with an
  empty list, offers `Sample Job`, and the sample flow creates a local job with
  a generated PDF page plus preloaded line/area/count takeoffs and
  measurements.
- PDF drawing coordinates were corrected for DPI/exe rendering by aligning
  Skia drawing with WPF DIP input.

### Pages and PDF

- PDF import creates page folders and stores page source metadata in
  `source.json`.
- Pages appear in the left Pages tree and can be selected.
- Page folders/pages support rename, delete, copy/cut/paste, duplicate, move,
  sort, and drag/drop organization.
- `Sort A/S` is directly visible under `Import PDF` and moves A sheets to Arch,
  S sheets to Struct, and trailing `-` sheets to others.
- `Repair Links` is directly visible under `Import PDF`, next to `Sort A/S`, and
  also exists in the Pages context menu as `Repair Measurement Links`.
- Page tabs exist above the viewport: selecting a sheet opens/reuses the active
  tab, `Open in New Tab` opens another tab, tabs close, and zoom/pan is
  remembered per tab.
- Page tabs survive page/folder rename, move, and cut/paste by rebasing paths.
- Saved measurement page references are repaired on job open and rebased during
  page/folder moves or `Sort A/S`; `Repair Links` can run the same repair from
  the left panel. Legacy `Page N` measurement links can also be remapped after
  Auto Name by matching the old import number to a unique current PDF page
  index. Page badges, takeoff highlights, scale propagation, and section-row
  canvas selection use the same normalized page-folder compare.
- Root cause / regression checklist is documented in
  `docs/30-takeoffs-measurements/MEASUREMENT_PAGE_LINK_AND_EDITING_POSTMORTEM.md`.
- PDF pages render in a SkiaSharp viewport.
- PDF layers are read through `Tools/pdf_layers_helper.py`, cached, shown in
  the left PDF Layers panel, and persisted in per-page `layers.json`.

### Page and Takeoff Folder Templates

- Pages can auto-create standard PlanSwift page folder sets from the old Python
  reference workflow.
- Takeoffs can auto-create the standard PlanSwift takeoff folder tree under the
  selected folder/root.
- Takeoffs can create top folders from CAPS-style names found in Pages, then add
  the standard subfolder tree under each.
- Folder templates support persisted `Auto` / `COM` / `EWP` mode selection.

### Takeoff and Measurement Workflow

- Count/Line/Area/Scale/Pan tools exist.
- User-facing wording says `Count` instead of internal `point`.
- Count/Line/Area tool selection creates or selects a matching takeoff item.
- Mixing geometry types inside one takeoff item is blocked by workflow.
- Line/Area can finish by double-click, Esc, or C when enough points exist.
- Right-button drag pans the sheet; right-click no longer stops recording.
- Ctrl+Z and Backspace remove the last in-progress point before removing a
  completed measurement.
- Snap mode toggles from toolbar or `F3`, magnetizes to existing app-created
  endpoints, midpoints, and intersections, and shows distinct end/mid/int
  previews.
- Ortho mode toggles from toolbar or `F8`, constrains Line/Area/Scale to 90/45
  degrees, and `Shift` temporarily toggles the constraint.
- Canvas right-click edit actions support properties, rename, delete, insert
  vertex, and remove nearest vertex.
- Page markup tools (`Ruler`, draw line, arrow, box) create lightweight sheet
  markups saved in `annotations.json`. In `Select`, these markups can be
  selected, moved, reshaped with handles, deleted with `Delete`, or deleted
  from the right-click menu.
- Selected measurements and markups can be transformed from the bottom toolbar:
  mirror horizontal/vertical, rotate by slider, or scale by slider. The same
  selection shows a subtle orange transform area behind the blue selection
  bounds, with live corner scale handles and a top rotate handle.
- Canvas selection/editing supports larger vertex hit targets, direct
  whole-measurement body drag, and drag saves/recalculates on mouse release
  instead of rebuilding the tree/table on every mouse move. Vertex/body drag
  uses screen delta from the original mouse-down point for stable movement.
- Confirmed edit-drag fix: capture starts before selection events, continues
  until mouse-up/lost-capture, and right-side Estimate/Takeoffs sync is skipped
  while the left mouse button is down.
- PlanSwift-style measurement box selection is in place through the default
  `Select` tool: left-drag selects measurements on the current sheet,
  `Ctrl+Click` toggles individual measurements, `Ctrl+A` selects the sheet,
  `Delete` removes the selected set, and body-drag moves a selected group
  together, including Count/point selections.
- Measurement copy/paste is in place: `Ctrl+C` copies selected measurements and
  `Ctrl+V` / viewport context menu pastes them onto the active sheet, either
  into the same takeoff items/values or into newly created copied takeoff
  items. Paste is cursor anchored by moving the copied set's center to the
  current cursor/right-click point.

### Scale, Units, and Rendering State

- Scale can be set by ratio or by drawing a calibration line.
- Scale is stored per page and per measurement.
- The viewport shows a PlanSwift-style sheet header with scale and sheet size.
- Metric/imperial unit toggle updates totals.
- Pages without scale show an `unscaled` badge.
- Line/Area Record is blocked until the sheet has scale; Count can still be
  recorded without scale.

### Takeoffs Tree and Estimating

- Takeoff folders and items appear in the right Takeoffs tree.
- Takeoff folders have a `Folder Properties...` dialog for display name,
  notes, default color, and default measurement type. Folder notes/defaults are
  saved in each folder's `folder_properties.json`; notes/defaults show in
  tooltips/status text, and defaults seed new takeoff items created under that
  folder.
- Takeoff items support properties for name, color, unit price, and notes.
- Area takeoff items can be used as Joist Area takeoffs with joist type,
  O.C. spacing, direction, roof pitch such as `3:12`, length rounding, and
  optional per-joist labels. Pitch is applied as a slope factor before
  order-length rounding so joist totals reflect sloped length.
- Takeoff items/folders support copy, cut, paste, duplicate, delete, drag/drop,
  and Ctrl-based multi-select.
- Takeoff items expand to completed section/count child rows.
- Section/count child rows support Properties, Rename, Go to Page, Select on
  Canvas, Move Up, Move Down, and Delete.
- Selecting a takeoff highlights measured sheets; selecting a sheet highlights
  matching takeoffs.
- Estimating tab lists item rows and per-section rows with quantity, unit,
  price, cost, page, section, notes, and quick filter.
- CSV export includes unit price, cost, section name, section index, notes, and
  `ScaleMetersPerPt`.

### PDF Auto Rename and Auto Scale

- `Tools/pdf_layers_helper.py sheetmeta` extracts deterministic PDF sheet
  metadata.
- `PdfSheetMetadataService` normalizes metadata and writes `source_pdf.json`.
- Pages context menus and the visible `PDF Auto` panel expose Analyze, Auto
  Name, Auto Scale, Name+Scale, AI Fill, and learning actions.
- Auto Rename / Auto Scale apply is review-gated in a preview grid.
- E-Wood source resolver can match missing imported source PDFs by sheet key.
- GPT/image fallback can queue title-block crop requests and apply JSON
  responses through the same preview grid.
- AI Fill crop hints are in place: unresolved sheets can prompt for a
  representative sheet, the user can draw reusable `Sheet #` and `Scale`
  crop regions, and job-local `AI_Context/sheet_metadata_crop_template.json`
  drives role-specific fallback crops across the sheet set.
- Learning records, distilled learned rules, confidence hints, conflict
  warnings, and global learned-rule enable/disable review are in place.

### AI Inbox, SmartTrace, and Markers

- AI context folders are created per job.
- Right-click AI Assist can save plan/measurement crops and observations.
- Right-click AI Assist can run `AI crop here -> note`: it saves a context
  crop, runs `quick_crop_note_request` with `gpt-5-mini` when configured, and
  creates a visible persisted sheet `Note` from the model output.
- AI request JSON files are queued under `AI_Context/requests`.
- AI Inbox can open details, jump to page, open crops, open request JSON, open
  layer manifests, run AI, save manual AI responses, and refresh.
- OpenAI runner reads `OPENAI_API_KEY` from process env and Windows user env.
- Toolbar `AI Settings` shows OpenAI key found/missing status without revealing
  the secret, saves/clears the key only in the Windows user environment, and
  saves the AI workflow model in `%APPDATA%\OurPlaneCore\settings.json`.
- AI responses create action drafts under `AI_Context/actions`.
- Action drafts can be previewed as dashed overlays and applied after user
  confirmation.
- AI marker capture MVP is done: markers can be saved from the sheet or
  measurement context, with type/sample/value/note, crop evidence, marker JSON,
  canvas overlay, and AI Inbox entry.
- AI marker review basics are in place: AI Inbox can filter markers by
  type/sample kind, edit marker type/sample/value/note, and delete active marker
  JSON from the overlay/Inbox while keeping crop evidence and the observation
  log.
- AI marker organization basics are in place: current Inbox filters can be
  saved as marker sets under `AI_Context/marker_sets`, marker types can be
  hidden/shown on the canvas overlay and persisted per job, saved marker sets
  can be applied, renamed, deleted, or opened as JSON, and visible filtered
  markers can be exported to `AI_Context/exports/markers_context.json` with
  marker sets, feedback records, and marker quality summaries.
- Crop bookmarks are in place: Inbox crop/marker entries can be bookmarked
  under `AI_Context/crop_bookmarks`, then `Run New` sends only bookmarks with
  `status=new` to OpenAI, records response/action draft ids, and marks each
  bookmark `done` or `failed`.
- Crop bookmark retry and guarded rediscovery are wired: `Retry Failed`
  reprocesses only `status=failed` bookmarks, successful bookmark drafts can
  create new `status=new` candidate bookmarks from page/point actions, and
  duplicate/depth guards prevent rerunning `done` work or rediscovering the
  same crop loop.
- Marker-assisted `Find Similar From Marker` is available from AI marker Inbox
  rows. It queues `find_similar_marker_request`, saves a wider nearby-sheet
  context crop around the source marker, sends marker crop plus nearby crop,
  marker JSON / exported marker context / page-layer context, and saves only
  reviewable action drafts. Reviewed accepted/rejected candidates are appended
  to `AI_Context/learning/marker_feedback.jsonl` and reused as prompt context
  on later marker searches. AI Inbox marker rows show a compact feedback
  summary such as accepted/rejected/applied counts and average confidence.
- SmartTrace action review has a first real review UI: `Review Action Draft`
  lets the user accept/reject individual actions, pick compatible target
  takeoff items or new AI items, preview selected actions, apply only valid
  accepted actions, and record accepted/rejected/applied indices in the draft
  JSON.

### Future AI Auto Trace / Facade Detection Idea

- Keep this as a future reviewable workflow, not a hidden automatic takeoff.
- Full planning spec:
  `docs/30-takeoffs-measurements/AUTO_TRACE_AREAS_AND_WALLS_SPEC_2026_05_12.md`.
- Target use cases:
  - find all windows on an elevation/facade sheet;
  - find exterior/interior wall runs from a plan crop;
  - detect repeated openings, doors, labels, and facade bays;
  - propose area outlines or wall polylines for user review.
- Cost-first model strategy:
  - use `gpt-5.4-nano` as the default first-pass vision model for small crops
    and high-volume candidate detection;
  - optionally allow `gpt-5-nano` as the cheapest legacy/cost mode when quality
    is acceptable;
  - escalate only difficult/low-confidence crops to `gpt-5.4-mini`;
  - avoid expensive frontier models for routine window/wall candidate scans.
- Architecture direction:
  - crop or tile the sheet into bounded image regions;
  - ask the model for structured JSON candidates, not final measurements;
  - return candidate boxes/polygons with type, confidence, and notes;
  - use local PDF vector geometry, snapping, OpenCV/edge detection, and
    existing viewport geometry tools to refine contours;
  - show all results as dashed preview candidates before the user accepts them
    into takeoff items or sheet notes.
- Example candidate output shape:

```json
{
  "items": [
    {
      "type": "window",
      "confidence": 0.86,
      "box": { "left": 120.5, "top": 88.0, "right": 168.0, "bottom": 142.0 },
      "notes": "repeated facade window"
    }
  ]
}
```

- Open questions before implementation:
  - whether to start with facade windows or plan walls;
  - how large each crop/tile should be for cost and accuracy;
  - how to batch low-cost requests without freezing the WPF UI;
  - what confidence threshold should auto-create only review candidates versus
    require manual crop selection.
- Recommended first slice from the spec: build the trace candidate/review/apply
  pipeline before adding heavy vector, raster, or AI detection. That means
  `TraceBatch` / `TraceCandidate` models, review overlay/dialog, duplicate
  warnings, and applying accepted candidates into normal measurements.

- 3D Massing first slice is wired: `Build 3D` / `Build 3D Draft` calls
  `SmartMassingDraftService.SaveDraftFromMarkers`, saves
  `AI_Context/3d_massing/model.json`, and shows a readable `3D Massing`
  workspace tab with footprint points, openings, roof summary, assumptions,
  unresolved questions, and source marker ids. The tab also has a top-down
  footprint preview, source-marker evidence links, selected-marker
  highlighting, and first-pass roof guide overlays.
- Auto Roof recognition first slice is wired: the `3D Massing` tab can queue a
  `roof_recognition_request`, save a large sheet/marker-bounds crop, send
  nearby marker evidence crops to OpenAI, and convert reviewed accepted roof
  candidates into regular `ai_marker` records for the next `Build 3D Draft`.
- Editable roof review is wired: `Review Roof` opens a dialog for roof
  type/pitch/confidence/notes plus guide keep/edit points, then saves reviewed
  roof status back into `AI_Context/3d_massing/model.json`.
- Simple derived 3D geometry is wired: reviewed/draft roof guides generate
  `roof.planes`, and the `3D Massing` tab renders a lightweight WPF 3D shell
  preview with floor, walls, roof planes, Fit/Iso/Top/Front controls, and
  mouse orbit/zoom.
- Hip roof plane generation has a first useful slice: `hip` roof type or
  `hip_ridge` guides create four reviewable roof plane candidates from the
  ridge/guide and footprint bounds.
- 3D preview source linking is wired: clicking floor/wall/roof/opening/pin
  geometry highlights the selected 3D object, updates status/details text, and
  selects the first linked source marker row when available.
- Opening projection is wired: `window_sample`, `door_sample`, and
  `opening_sample` markers are saved into `model.json` as draft wall openings
  with source marker id, nearest wall index, center point, approximate
  width/height, confidence, and notes; the 3D preview shows them as colored
  wall rectangles and marker pins.
- Opening review is wired: `Review Openings` lets the user keep/edit/reject
  projected openings; kept rows are saved as reviewed draft openings and
  unchecked rows are preserved as rejected evidence.
- Opening review feeds learning: reviewed/rejected projected openings are
  appended to project/global `marker_feedback.jsonl` with
  `event_type=3d_opening_projection_review`.
- `Accept 3D` can mark the whole massing draft as reviewed AI context without
  creating takeoff quantities, and now writes a timestamped snapshot JSON under
  `AI_Context/3d_massing/snapshots`.

## Historical Next Tasks

### Historical Top Priority - Moved Jobs and Sheet Render Speed

Detailed handoff:

`docs/00-start-here/NEXT_TASK_JOB_MOVE_AUTOREPAIR_AND_SHEET_RENDER_PERF_2026_06_06.md`

1. Add whole-job-move autodetect to measurement page-link repair:
   - extend `MainWindow.JobLifecycle.cs`
     `RepairMeasurementPageFolderReferences()`;
   - when a stale `page_folder` contains a `\Pages\` segment, preserve the
     suffix after `Pages` and resolve it under the current job's `Pages` root;
   - accept only existing page folders with `source.json`;
   - run on job open and through the existing `Repair Links` command.
2. Continue sheet render acceleration from measured evidence:
   - first capture packaged-app render logs and `OPC_BENCH=1` baseline;
   - prioritize visible clip/detail render and interactive queue priority;
   - keep low-res whole-sheet proxy for instant first frame;
   - do not solve blur by only raising whole-sheet raster DPI;
   - preserve the recent raster warm-cache shape:
     `build/enable raster -> warm decoded bitmap cache -> refresh/apply on UI`.

### 0. Best-Practices Architecture Queue

- Split future `MainWindow.xaml.cs` work into partials/services/controls before
  adding more large features to the main code-behind.
- Spike AvalonDock only after the small UX windows are stable: dock/floating
  Pages, Takeoffs, AI Inbox, and viewer panels, with layout JSON stored under
  `%APPDATA%\OurPlaneCore\layouts\`.
- Done: add Command Palette (`Ctrl+Shift+P`) over existing commands for better
  discoverability without expanding the toolbar.
- Next: enrich the command registry with new detached-window actions as those
  windows are added.
- Done: Snap v2 keeps tolerance in screen pixels, adds endpoint/midpoint and
  intersection snap candidates, distinct glyphs, and compact canvas coordinate
  labels.
- Done: add per-job snapshots/crash recovery with `.snapshots/`, a `.~lock`
  marker, stale-lock recovery prompt, and bounded metadata snapshot history.
- For the future 3D viewer extraction, prefer HelixToolkit.Wpf or
  HelixToolkit.SharpDX instead of growing custom WPF 3D.
- Done: add Recent Jobs / JobPicker lite base workflow.
- Done: add background PDF thumbnails as a separate non-blocking service.
- Done: add pin/unpin and remove-from-recent actions for manual cleanup.
- Done: add sample job / empty-state CTA now that the picker has recent jobs,
  thumbnails, and cleanup controls.
- Done: add sticky Estimating summary footer and large-grid ListView
  virtualization/recycling.
- Next: add CSI MasterFormat estimating templates and richer Excel/ClosedXML
  export.

### 1. Manual Takeoffs Workflow

- Extend takeoff folder defaults into more creation paths where useful.
- Improve takeoff item/folder properties and metadata editing beyond the first
  folder sidecar slice.
- Improve section/count management in the right panel.
- Tighten drawing/editing prompts and active Record state.
- Finish Count-specific wording in remaining secondary dialogs.

### 2. AI Marker Review and Organization

- Add a visible bulk marker review panel if marker count becomes large.

### 3. Marker-Assisted Find Similar Hardening

- Add cross-sheet batch search across selected or all sheets.

### 4. SmartTrace Review UI Hardening

- Add clearer source links from each reviewed action back to response/request
  evidence.
- Add better canvas focus/preview tools for a selected review-row action.
- Add confidence/status filtering if action drafts become large.

### 5. PDF Auto Rename / Auto Scale Hardening

- Show richer learned-rule conflict details.
- Add retry/clear controls for saved AI Fill crop hints when a job has multiple
  title-block layouts.
- Add failed-request retry controls.
- Add automatic request processing queue.

### 6. 3D Massing First Slice

- `SmartMassingDraftService` defines and can save
  `AI_Context/3d_massing/model.json`.
- The draft builder uses exterior corner markers plus one wall height value and
  records assumptions/unresolved questions.
- Done: add a UI command to build the draft from the current job.
- Done: add a placeholder `3D Massing` tab that lists assumptions, unresolved
  questions, footprint points, openings, roof summary, and source markers.
- Done: add source-marker evidence links, selected-marker details, and a
  top-down footprint preview.
- Done: add reviewable roof guides for eave outline, ridge/hip ridge, shed
  slope arrow, low-slope cap, and unknown roof-axis overlays.
- Done: add explicit `ridge_sample`, `valley_sample`, `roof_high_edge`,
  `roof_low_edge`, and `overhang_sample` marker support for reviewable roof
  guide drafting.
- Done: add `Auto Roof` recognition as a reviewable roof-marker candidate
  workflow. Accepted candidates become markers only after user review.
- Done: add editable roof review before treating roof geometry as accepted.
- Done: convert reviewed guides into simple roof planes.
- Done: add a lightweight WPF 3D shell preview.
- Done: add `Accept 3D` reviewed-state saving for the whole draft.
- Done: add first-pass object/source selection in the 3D preview.
- Done: project window/door/opening markers onto likely wall faces and render
  them as reviewable opening rectangles in 3D.
- Done: add `Review Openings` accept/reject/edit workflow for projected
  openings.
- Done: record opening review outcomes into marker feedback learning.
- Done: save accepted 3D draft snapshots after `Accept 3D`.
- Done: add first-pass four-plane hip roof generation for `hip` /
  `hip_ridge` drafts.
- Done: include reviewed opening-projection feedback in future Auto Roof
  detection prompts.
- Next: improve complex valley/multi-roof plane generation.

### 7. Advanced Scale

- Support separate horizontal and vertical scale.

### 8. PlanSwift Parity

- Move closer to Properties-first workflow instead of tool-button-first feel.
- Show quantities in page/list context as well as Takeoffs/Estimating.
- Add arc drawing.
- Add specialty takeoff tools.
- Add undocked takeoff windows after page tabs are stable.

### 9. Product and Settings Decisions

- Decide final public product name.
- Decide whether to rename internal `OurPlaneCore` namespace/settings paths.

### 10. Future Online / Web Companion Idea

- Treat an online/browser version as a future companion path, not a near-term
  replacement for the Windows WPF app.
- Start with a web viewer: open project sheets in a browser, render PDFs with
  PDF.js, support zoom/pan, and show existing takeoff/measurement data.
- Add web takeoff only after the viewer proves useful: Count, Linear, Area,
  scale handling, and save-back to the same project format.
- Keep heavy desktop workflows in the Windows app at first: report builder,
  complex PDF/project import, AI review, 3D roof/massing, and full export.
- Preferred architecture when this becomes active: shared C# project/domain
  models, ASP.NET Core API, browser canvas/SVG overlay over PDF.js, and simple
  storage before adding accounts, cloud storage, permissions, or multi-user
  sync.

## Historical Recommended Next Implementation

Continue with the highest-leverage review hardening:

1. Keep the measurement repair/editing postmortem as the first regression
   checklist for canvas visibility/edit bugs.
2. For 3D Massing, include reviewed/rejected opening feedback in future OpenAI
   prompts where it can improve detection.
3. Improve complex valley/multi-roof plane generation from reviewed roof
   guides.
4. Add a visible snapshot/history picker if multiple accepted 3D snapshots
   need comparison.
5. Add cross-sheet batch search for marker-assisted `Find Similar` after the
   current 3D review loop is stable.
