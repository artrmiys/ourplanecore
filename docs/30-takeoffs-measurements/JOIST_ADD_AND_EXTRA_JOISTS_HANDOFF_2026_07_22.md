# Joist Add and Extra Joists Handoff — 2026-07-22

## 2026-08-03/04 follow-up delivery

This section is the current contract for the follow-up batch. It supersedes
older package hashes and test totals later in this document without changing
the original July workflow history.

### Per-Area boundary controls

- Selecting a Joist Area Segment in Select mode shows two 17-screen-pixel
  checkbox controls inside its orange frame. Their centers use one fixed
  14-screen-pixel inset: both share `bounds.Left + inset`, while their vertical
  positions are `bounds.Top + inset` and `bounds.Bottom - inset`.
- The controls do not search for a wider interior position. This guarantees
  that every Area uses the same left-corner placement, exact vertical
  alignment, and equal distance from the orange frame.
- The upper checkbox maps to the visually upper joist boundary, or the
  visually left boundary when the joist direction is near vertical. The lower
  checkbox maps to the opposite boundary. Drawing and click hit-testing share
  the same `TopJoistEdgeControlSide` mapping, so their states cannot be
  reversed.
- A click stores `JoistStartEdgeEnabled`, `JoistEndEdgeEnabled`, and
  `JoistEdgeOverridesSet` on that Area only. It recalculates the Area, saves
  through the normal measurement callback, and creates a `Ctrl+Z` undo step.
- Without a local override, `ResolveEdgeJoists` keeps the start side on and
  takes the far side from the takeoff/measurement `JoistAddEndJoist` setting.
  Once either checkbox is used, that Area keeps its own two-side state across
  regular Joist refreshes and persistence round trips.
- Each enabled boundary joist is positioned at the real Area boundary but
  copies the flat endpoints/length from the nearest usable regular joist on
  that side. It therefore remains the same full length as the last regular
  joist even on a slightly skewed Area edge. If no single usable regular joist
  exists, the calculator falls back to the clipped boundary geometry.

Primary ownership:

- UI/render/click mapping: `Controls/PdfViewport.JoistEdgeControls.cs`.
- Layout and matching-length rules:
  `Models/JoistTakeoffCalculator.cs` and
  `Models/JoistTakeoffCalculator.Edges.cs`.
- Stored per-Area flags: `Models/Measurement.cs` and the current/legacy storage
  DTO paths.
- Regression coverage: `Tests/JoistExtraModelTests.cs`.

### Existing Extra Joist editing

- An existing Extra Joist is a direct viewport selection target. Left-clicking
  near it selects it; dragging translates the complete line while
  `TryClipExtraJoist` keeps the result inside the owning filled Area interval.
- `Delete` removes the selected Extra Joist before considering deletion of its
  owning Area or cut regions. Both movement and deletion use normal undo and
  measurement-change notifications.
- The selected extra retains the regular selected-line treatment. An
  unselected Extra Joist receives the separate glow described below, making it
  distinguishable without changing regular Joists.

Primary ownership:

- Selection, drag, clip, delete:
  `Controls/PdfViewport.ExtraJoists.cs`.
- Mouse lifecycle: `Controls/PdfViewport.Input.cs`.
- Delete precedence: `Controls/PdfViewport.SelectionState.cs`.
- Rendering: `Controls/PdfViewport.JoistRendering.cs`.

### Configurable Extra glow and PDF parity

- `AppSettings.ExtraJoistGlowIntensity` is normalized to `[0, 1]`. Its default
  is the previous visual alpha `145/255`, shown as approximately 57 percent in
  the UI.
- `Viewport > LINES & AREA > Extra` provides a 0-100 slider and numeric field.
  Changes repaint the main viewport immediately, update every detached sheet,
  and persist through the normal app-settings store.
- The glow is applied only when `segment.IsExtra && !selectedExtra`. Intensity
  zero removes it, and regular Joists are not affected.
- `PDF Output > INCLUDE > Extra glow` is an independent persisted visibility
  checkbox, on by default. PDF rendering uses the shared Viewport intensity
  and draws its vector halo only under Extra Joist segments.

Primary ownership:

- Settings model/normalization: `Models/AppSettingsStore.cs`.
- Viewport settings UI:
  `MainWindow.DisplaySettings.ExtraJoistGlow.cs`.
- Main/detached propagation: `MainWindow.DisplaySettings.cs`,
  `MainWindow.DetachedSheets.cs`, and `Dialogs/DetachedSheetWindow.cs`.
- PDF option/UI: `MainWindow.OutputSettings.cs`, `MainWindow.PdfExport.cs`, and
  `Models/PdfExporter.cs`.
- PDF renderer: `Models/PdfExporter.Measurements.cs`.

### Excel separation for Extra Joists

- `ExcelFramingExportPlanner` no longer concatenates regular and Extra length
  groups before emitting rows.
- The regular block contains descending `(quantity / length)` rows followed by
  `<joist name> <spacing>`. If extras exist, a second descending group follows
  and closes with `Extra <joist name> <spacing>`.
- Joist framing export still intentionally skips the Sum macro. The separate
  block makes Extra quantities visible in Excel without creating a second
  OurPlanCore takeoff item.

Regression ownership:
`Tests/ExcelFramingExportTests.cs::PlannerBuildsGroupedJoistMacroInputWithoutSum`.

### Beam companion annotation line

- The Beam dialog can optionally retain a normal page line annotation through
  the same two points used for the Beam measurement. The existing blue Beam
  dimension remains unchanged.
- Built-in default: disabled, red (`#FF0000`). Both the dialog and `8 Settings`
  show the shared annotation color swatches, including a saved custom color,
  instead of exposing a hex-code text box.
- `8 Settings` provides Reset, Save global default, Save as this job, and Clear
  job override. Effective resolution is job override, then global preset, then
  the disabled/red built-in default. Each Beam dialog starts from that resolved
  setting and can change the choice for the Beam being created.
- The retained line is a regular `PageAnnotation` with `Kind = "line"`, normal
  undo/persistence callbacks, and the normalized selected color.

Primary ownership:

- Config/provider: `Models/BeamAnnotationConfig.cs`.
- Global/per-job persistence: `Models/SettingsPresetStore.cs`.
- Settings editor: `MainWindow.SettingsManager.BeamAnnotation.cs`.
- Beam dialog/tool path: `Dialogs/NewItemDialog.cs` and
  `MainWindow.BeamTool.cs`.
- Shared visual picker: `Controls/ColorSwatchPicker.cs` and
  `Models/AnnotationColorPalette.cs`.
- Annotation creation: `Controls/PdfViewport.Beam.cs`.
- Regression coverage: `Tests/BeamAnnotationConfigTests.cs`.

### Overlay Fit by two points startup fix

- Every `Fit by two points` entry now routes through
  `BeginSheetOverlayPointEditWhenReady` instead of calling the viewport before
  the overlay has loaded.
- The ready path opens the target page when needed and waits until
  `HasSheetOverlayBinding` matches the exact target folder, overlay page folder,
  and active overlay ID. Only then does it call
  `_viewport.BeginSheetOverlayPointEdit()`.
- This fixes the previous no-op where the command appeared not to launch while
  the overlay bitmap/binding was still being applied.

Primary ownership: `MainWindow.SheetOverlay.cs`,
`Controls/PdfViewport.SheetOverlay.cs`, and
`Tests/SheetOverlayPropertiesRegressionTests.cs`.

### Commits and final proof

- `1c08f64` - direct Extra editing, per-Area edge state, Excel Extra block,
  Beam companion line/presets, and Overlay point-fit ready path.
- `32540d8` - robust boundary generation, Extra visual distinction, and Beam
  annotation-style color swatches.
- `18ee96e` - boundary joists copy the nearest regular joist length.
- `56f77cc`, `5ca7f2e` - checkbox vertical/left alignment refinements.
- `b86a374` - fixed visual upper/lower checkbox-to-boundary mapping.
- `aa1215e` - persisted Extra glow intensity and PDF visibility/parity.
- Final build: `dotnet build .\ourplancore.sln`, 0 warnings, 0 errors.
- Full regression harness: `667/667 tests passed`.
- Installed compressed single-file EXE:
  `C:\Users\User\Desktop\updates\OurPlanCore\ourplancore.exe`.
- Installed size: `174,463,146` bytes.
- Installed/published SHA-256:
  `19885FAF05925380ECC9F130FC0EFE9E8912E74A345E0B60D0FF755C2BF3CE70`.
- Desktop shortcut target and working directory both point to the installed
  update package.
- Fresh runtime session began at `2026-08-03T22:10:46-03:00`, remained alive
  and responsive, loaded 160 takeoff items and the viewport, and had 0 `ERROR`
  entries after its last `Application startup.` marker.
- Rollback:
  `ourplancore.exe.bak-20260803-2156-pre-aa1215e`.

## Result

The desktop WPF app now treats a Joist Area takeoff as one item containing
multiple independent Area Segments:

- Selecting one Area Segment and running
  `Refresh Regular Joists in All Area Segments` refreshes every Area Segment in
  the owning Joist Takeoff.
- Every locked segment keeps its own saved joist direction. The command does
  not replace all directions with the selected segment's angle.
- The takeoff-level `Add End Joist` value is copied to every Area Segment
  before layout calculation. When enabled, each segment gets its own far-edge
  joist.
- Segments without a locked direction are opened and prompted one at a time,
  including segments on other sheets.

## Extra Joist interaction

- `Start Extra Joists Mode (D)` is available in the Joist takeoff, Area
  Segment, and viewport context menus. Joist commands are intentionally absent
  from the main toolbar so regular refresh and manual extras stay distinct.
- Plain `D` starts the same mode when exactly one Joist Area Segment is
  selected, and pressing `D` again turns the active mode off. Without that
  selection, `D` keeps its previous Draw Line behavior.
- The mode is continuous. A bright white/yellow ghost joist stays parallel to
  the selected segment's saved direction and follows the raw mouse position.
- The ghost is clipped to the filled local interval of the selected area.
  Outside the area or inside a cutout is invalid and does not end the command.
- Every valid left click stores one joist and immediately keeps the ghost ready
  for another placement. The mode continues until `D` or `Esc`; global `Esc`
  also works before the viewport receives focus. Each click has its own
  `Ctrl+Z` undo step.
- Right-click near an existing extra and choose `Delete Nearest Extra Joist`;
  the delete is also undoable.
- Extra joists belong only to the selected Area Segment and remain inside the
  same Joist Takeoff item. Duplicates at the same location are allowed.

## Data, totals, labels, and export

- `Measurement.ExtraJoists` stores explicit two-point
  `JoistExtraSegment` records with stable IDs.
- The normal joist layout remains generated from spacing and direction. Extras
  use explicit endpoints, the owning area's scale and pitch, and the same
  per-piece length rounding.
- Item count, ordered LF, current-sheet estimating quantity, and total value
  include regular and extra joists.
- On-canvas labels and joist legend lines list regular length groups first,
  then the literal separator `Extra`, then extra length groups.
- Takeoff export writes all normal Area Segment blocks first, one `Extra`
  separator, and the aggregated extra groups last. It remains one takeoff row
  or item, not a second takeoff.

## Persistence and editing safety

- Extras round-trip through `measurements.json` and the legacy `ProjectFile`
  sidecar format.
- Clipboard paste and Ctrl-drag copy deep-copy endpoints and allocate new IDs.
- Whole-measurement move, rotate, mirror, and scale transform explicit extra
  endpoints. Undo snapshots include extras.
- Area Cut assigns each extra by midpoint to exactly one surviving piece and
  drops it only when its midpoint is cut away.
- Area Union/Intersect collects extras from consumed areas, deduplicates stable
  IDs, and assigns each extra to one resulting area. These operations are
  undo-safe.
- Moving/coalescing Area measurements between takeoffs preserves and
  deduplicates extras.

## Main ownership

- Model/calculation: `Models/Measurement.cs`,
  `Models/JoistTakeoffCalculator.cs`,
  `Models/JoistTakeoffCalculator.Extras.cs`.
- Storage/export: `Models/Storage/StorageDtos.cs`,
  `Models/Storage/TakeoffStore.cs`, `Models/ProjectFile.cs`,
  `Models/PlanSwiftTakeoffExporter.cs`.
- Viewport workflow: `Controls/PdfViewport.ExtraJoists.cs` plus the focused
  input, rendering, transform, undo, Area Cut, and Area Combine partials.
- Main-window commands: `MainWindow.TakeoffsJoistGeneration.cs`,
  `MainWindow.TakeoffsExtraJoists.cs`, `MainWindow.Shortcuts.cs`, and the three
  context-menu partials.
- Regression coverage: `Tests/JoistExtraModelTests.cs`.

## Verification and installed package

- Build: `dotnet build .\ourplancore.sln` — 0 warnings, 0 errors.
- Full regression run:
  `dotnet run --project .\Tests\OurPlanCore.Tests.csproj --no-build` —
  575/575 passed.
- Feature commits:
  - `26cdbb9` — `Add Joist area extra workflow`.
  - `6e1d8ed` — `Add Extra Joist D shortcut`.
  - `2f418f8` — `Keep Extra Joists placement active`.
- Installed compressed single-file package:
  `C:\Users\User\Desktop\updates\OurPlanCore\ourplancore.exe`.
- ProductVersion:
  `2.2.3+2f418f8b07df7266369baad208a3c3d7c51f1ac3`.
- Size: `174,282,183` bytes.
- SHA-256:
  `2002E138953EBB9E442FF155522A206144173FFFAC063208B9BF35C9843FD310`.
- Desktop shortcut target and working directory both point to the installed
  `updates\OurPlanCore` package.
- Runtime proof used a process-only settings override and `Sample Job`, leaving
  the operator's real settings untouched. The fresh log session starting at
  `2026-07-22T07:42:00-03:00` stayed alive, loaded 3 takeoff items, rendered the
  viewport, and contained 0 `ERROR` entries. The app then closed normally with
  `Application exit 0`, and its Sample Job lease was released.
- Preserved rollbacks:
  - `ourplancore.exe.bak` (unchanged),
  - `ourplancore.exe.bak-20260722-before-joist-extra`,
  - `ourplancore.exe.bak-20260722-before-extra-d-shortcut`,
  - `ourplancore.exe.bak-20260722-before-extra-continuous-mode`.

## Other work completed the same day

Point Split was completed first in commit `5b24718` (`Fix Point split
workflow`). It supports moving a whole Count section or only selected Count
markers, gives selected marker vertices precedence over a stale whole-section
selection, preserves metadata and point order, updates every source owner, and
uses the normal save/reload path. Its focused scenarios remain part of the
575-test green suite.

## Explicit boundary

This slice does not add new PlanSwift-import classification for standalone
manual Joist Line records. Existing OurPlanCore extras persist and export as
specified above; importing legacy PlanSwift manual doubles into
`Measurement.ExtraJoists` is a separate future task.
