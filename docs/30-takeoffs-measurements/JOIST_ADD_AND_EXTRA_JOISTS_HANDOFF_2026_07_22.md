# Joist Add and Extra Joists Handoff — 2026-07-22

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
