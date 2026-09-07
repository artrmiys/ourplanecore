# Takeoff Tools and PlanSwift Current-Job Import Handoff - 2026-05-24

## Current Status

Current packaged build:

- Deployed exe:
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`
- Final SHA256:
  `ACAE2B528D60558AC5575655DEB200F8369AC47D93592B4AF0C80968FE478E01`
- Final compressed single-file size:
  `175353802` bytes
- Desktop shortcut target:
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`
- Desktop shortcut working directory:
  `C:\Users\User\Desktop\updates\OurPlaneCore`

Relevant commits:

- `ff3e5c1 Add Beam count workflow`
- `a9fe52e Add Openings count workflow`
- `41cf7dc Add current job PlanSwift import`

Latest verification:

- `dotnet build .\ourplanecore.sln`: `0 warnings / 0 errors`.
- `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build`:
  `229/229` tests passed.
- Release compressed single-file publish completed and was copied into
  `C:\Users\User\Desktop\updates\OurPlaneCore`.
- Existing rollback file was preserved:
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe.bak`.
- Packaged exe launch check passed: process stayed alive, latest log segment
  after `Application startup.` had `0` errors, and the log contained loaded
  takeoffs / viewport signals.

## Beam Tool

User-facing behavior:

- Tool button: bottom takeoff toolbar, next to `J Area`.
- Hotkey: `B`.
- Workflow:
  1. Click first endpoint.
  2. Click second endpoint.
  3. The app leaves a dimension/Ruler-style annotation on the sheet.
  4. The app opens the Count item creation dialog.
  5. The measured/order size is appended to the Count name after a space.
  6. After dialog confirmation, the first Count mark is placed automatically.
- Count dialog selection:
  - editable name prefix is selected;
  - the trailing size suffix stays outside the selection.
- Beam order-size rounding:
  - measured length `<= 8 ft` rounds up to the next whole foot;
  - measured length `> 8 ft` rounds up to the next even 2-foot size.
- Beam Count mark offset:
  - offset is based on `36` screen pixels converted to PDF coordinates;
  - horizontal beams place the mark left/up from the dimension label;
  - vertical beams place the mark left/down from the dimension label;
  - the final point is clamped to the page.

Primary implementation files:

- `Controls/PdfViewport.cs`
  - added `BeamMeasurementRequest`;
  - added `ViewerTool.Beam`;
  - added `BeamMeasurementCompleted`.
- `Controls/PdfViewport.Beam.cs`
  - owns the 2-click beam measurement workflow;
  - creates the dimension annotation;
  - computes the Count point offset;
  - emits `BeamMeasurementCompleted`.
- `Models/BeamTakeoffService.cs`
  - owns Beam size rounding and default Count name construction.
- `MainWindow.BeamTool.cs`
  - receives the viewport request;
  - opens `NewItemDialog`;
  - creates the Count takeoff item;
  - places the first Count measurement.
- `Controls/PdfViewport.Input.cs`
  - maps `B` to `beam`.
- `MainWindow.CommandPalette.cs`
  - exposes Beam command with `B`.

Regression coverage:

- `beam length rounds up below and above eight feet`
- `beam default name keeps size suffix outside selection`

## Openings Tool

User-facing behavior:

- Tool button: bottom takeoff toolbar after `Beam`.
- Hotkey: `O`.
- Workflow:
  1. Click first rectangle corner.
  2. Click opposite rectangle corner.
  3. The app leaves width and height dimension annotations on the sheet.
  4. The app opens the Count item creation dialog.
  5. The Count name is strictly the measured opening size, for example
     `3.0x4.2`.
  6. The text cursor is placed at the end of the size string.
  7. After dialog confirmation, the first Count mark is placed at the center
     of the measured rectangle.
- Size format:
  - width and height are in feet;
  - both values are rounded/formatted to one decimal place;
  - separator is lowercase `x`;
  - no default prefix is added.

Primary implementation files:

- `Models/OpeningTakeoffService.cs`
  - owns one-decimal `widthxheight` formatting;
  - returns size-only default Count names.
- `Controls/PdfViewport.cs`
  - added `OpeningMeasurementRequest`;
  - added `ViewerTool.Openings`;
  - added `OpeningMeasurementCompleted`.
- `Controls/PdfViewport.Beam.cs`
  - also owns the Openings 2-corner measurement workflow;
  - creates width and height dimension annotations;
  - sends the Count point at rectangle center.
- `Controls/PdfViewport.LiveInputRendering.cs`
  - draws live width/height labels while measuring Openings.
- `Dialogs/NewItemDialog.cs`
  - added optional initial caret placement so Openings can put the cursor at
    the end while Beam can still select only the editable prefix.
- `MainWindow.BeamTool.cs`
  - handles `OpeningMeasurementCompleted`;
  - creates the size-only Count item;
  - places the center Count measurement.
- `Controls/PdfViewport.Input.cs`
  - maps `O` to `openings`.
- `MainWindow.CommandPalette.cs`
  - exposes Openings command with `O`.

Regression coverage:

- `opening size formats one decimal`
- `opening default name is size only`

## PlanSwift Import Into Current Job

User-facing behavior:

- Existing `PlanSwift` top button is unchanged:
  - it still converts a read-only PlanSwift job into a new OurPlaneCore job.
- New option:
  - `Open / Import` -> `Import PlanSwift to Current Job...`
  - command palette: `Import PlanSwift to Current Job`.
- Requirements:
  - an OurPlaneCore job must already be open;
  - source must be a PlanSwift job folder;
  - current-job import rejects an existing OurPlaneCore job as the source.
- Import destination inside the current job:
  - pages go under `Pages\01. planswift`;
  - takeoff folders/items go under `Takeoffs\01. planswift`;
  - existing current-job Pages/Takeoffs stay in place.
- After import:
  - the app reloads the current job from disk;
  - the imported pages are visible in the left Pages tree;
  - imported takeoffs are visible in the right Takeoffs tree;
  - import report remains under `<job>\import_reports`.

Primary implementation files:

- `Models/Import/PlanSwiftImportModels.cs`
  - added `DestinationJobPath`;
  - added `ImportRootFolderName`;
  - default current-job bucket name is `01. planswift`;
  - added `ImportIntoExistingJob`.
- `Models/Import/PlanSwiftProjectImporter.cs`
  - split the import body into `ImportManifestIntoJob(...)`;
  - new-job import still passes `job.PagesRoot` and `job.TakeoffsRoot`;
  - current-job import passes the bucket folders under `01. planswift`;
  - takeoff import uses the supplied takeoff root, so right-tree items land
    below `Takeoffs\01. planswift`;
  - page import uses the supplied page root, so imported sheets land below
    `Pages\01. planswift`.
- `Dialogs/PlanSwiftImportDialog.cs`
  - added current-job mode;
  - hides destination/job-name fields in current-job mode;
  - shows the destination summary;
  - keeps scan/import confirmation flow.
- `MainWindow.PlanSwiftImport.cs`
  - added `BtnImportPlanSwiftToCurrentJob_Click(...)`;
  - opens current-job dialog mode;
  - runs import with `DestinationJobPath = _currentJob.RootPath`;
  - reloads the same job after import.
- `MainWindow.OpenImportMenu.cs`
  - added `Import PlanSwift to Current Job...`.
- `MainWindow.CommandPalette.cs`
  - added command palette entry and execution route.

Regression coverage:

- `planswift import into current job uses planswift buckets`
  - creates a current job with existing page/takeoff data;
  - imports a synthetic PlanSwift job into that current job;
  - verifies imported page lives under `Pages\01. planswift`;
  - verifies imported takeoff lives under `Takeoffs\01. planswift`;
  - verifies existing page/takeoff remain outside those buckets;
  - verifies imported measurement page/takeoff folder bindings point at the
    imported bucket paths.

## Notes For Next Work

- The new current-job import intentionally reuses the existing PlanSwift
  scanner/importer behavior instead of adding a parallel importer.
- `PlanSwiftProjectImporter.Import(...)` now has two destination shapes:
  new job and existing job. Future import changes should verify both paths.
- If the desired bucket name changes later, update
  `PlanSwiftImportOptions.DefaultCurrentJobImportFolderName` and the
  current-job import test together.
- The untracked file `Assets/ourplanecore.ico.bak_20260522_132816` existed
  before this handoff work and was not included in these commits.
