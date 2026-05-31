# Refactor / UX Handoff - 2026-05-31

This handoff captures the late 2026-05-30 / early 2026-05-31 OurPlaneCore
work. The pass was conservative: fix the visible command clipping, reduce
large-file risk, keep behavior stable, verify, publish the compressed update
exe, and leave clear next steps.

## Current State

- Branch: `feature/ourcore-design-overhaul`.
- Latest code commit: `9b71181 Split import render and learning stores`.
- Previous same-night refactor commit:
  `c3abc9a Split large UI and context surfaces`.
- Packaged app is current at
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`.
- Desktop shortcut target and working directory both point to the update
  package.
- Latest compressed package SHA256:
  `C2B11CE908FFFDAED6AA094EADA7E8AE78B33715490CFE0EA6C151B2D2331286`.
- Latest package size: `176597407` bytes.
- Existing rollback backup was preserved:
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe.bak`.
- Worktree after commit is clean except the pre-existing untracked file:
  `Assets/ourplanecore.ico.bak_20260522_132816`.

## UX Work Completed

- Fixed the right-side clipping on command/tab buttons.
- Root cause: the selected highlight chrome was wider than the available tab
  slot, so the right edge of the selected state was clipped.
- Fix: narrowed/contained the selected highlight instead of changing behavior.
- The top command layout was also tightened, and the Current Excel Cell command
  was moved into the bottom-strip row where it fits better.

Relevant earlier commits:

- `02206fd Polish OurCore command UI`
- `cc2ba44 Split large workflow partials`

## Refactor Pass 1

Commit: `c3abc9a Split large UI and context surfaces`

Main changes:

- Extracted `MainWindow.xaml` resources into
  `Resources/MainWindowResources.xaml`.
- Extracted global `App.xaml` resources into:
  - `Resources/AppResources.xaml`
  - `Resources/AppControlResources.xaml`
  - `Resources/AppNavigationResources.xaml`
- Split `MainWindow.ThreeDWalls.cs` into shell, rendering, editor, and
  materials partials.
- Split `MainWindow.ThreeDRoof.cs` into shell, build, edge editing, preview
  state, and geometry partials.
- Split `MainWindow.PageTakeoffLegend.cs` into shell, context menu, drag/drop,
  move/sort, and visibility partials.
- Split `Models/SmartContextStore.cs` into context models, infrastructure,
  markers, and requests partials.
- Updated source-wiring tests so they read the new partial owners.

Verification:

- Build: `0 warnings / 0 errors`.
- Tests: `250/250`.
- `git diff --check`: clean.
- Conflict/TODO/NotImplemented scan: clean.
- A compressed update package was deployed, later superseded by the current
  package from `9b71181`.

## Refactor Pass 2

Commit: `9b71181 Split import render and learning stores`

Main changes:

- Split `Models/Import/PlanSwiftProjectImporter.cs` into:
  - `PlanSwiftProjectImporter.cs`
  - `PlanSwiftProjectImporter.Pages.cs`
  - `PlanSwiftProjectImporter.Takeoffs.cs`
  - `PlanSwiftProjectImporter.Segments.cs`
  - `PlanSwiftProjectImporter.Reports.cs`
  - `PlanSwiftProjectImporter.Paths.cs`
- Split `Models/PdfLayerRenderService.cs` into:
  - `PdfLayerRenderService.cs`
  - `PdfLayerRenderService.Render.cs`
  - `PdfLayerRenderService.Layers.cs`
  - `PdfLayerRenderService.Worker.cs`
  - `PdfLayerRenderService.Protocol.cs`
  - `PdfLayerRenderResults.cs`
- Split `Models/SmartLearningStore.cs` into:
  - `SmartLearningModels.cs`
  - `SmartLearningStore.cs`
  - `SmartLearningStore.IO.cs`
  - `SmartLearningStore.Rules.cs`
- Updated PDF render/cache/inline protocol source-wiring tests.

Verification:

- Build:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  passed with `0 warnings / 0 errors`.
- Tests:
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj /p:OutDir=.\cache\test_run\ /p:UseAppHost=false`
  passed with `250/250`.
- `git diff --check`: clean.
- Conflict/TODO/NotImplemented scan: clean.
- Compressed publish:
  `dotnet publish .\ourplanecore.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o .\publish\ourplanecore-compressed-20260530-235511`.
- Published exe and update-folder exe SHA256 matched:
  `C2B11CE908FFFDAED6AA094EADA7E8AE78B33715490CFE0EA6C151B2D2331286`.
- Packaged launch validation:
  - process alive: true;
  - app log: `%APPDATA%\OurPlaneCore\logs\app-20260530.log`;
  - errors after latest `Application startup.`: `0`;
  - `Loaded takeoffs`: true;
  - fresh `Viewport` line: false in the short hidden validation launch.

Viewport note: the hidden validation launch started and loaded takeoffs without
errors, but it did not auto-render a sheet, so no new `Viewport ...` line was
written. Next session should do one visible shortcut smoke test by opening a
sheet and checking the log after the latest `Application startup.`.

## New Ownership Map

- Page linked takeoff context menu:
  `MainWindow.PageTakeoffLegend.ContextMenu.cs`.
- Page takeoff visibility:
  `MainWindow.PageTakeoffLegend.Visibility.cs`.
- Page takeoff move/sort/layer-order commands:
  `MainWindow.PageTakeoffLegend.MoveSort.cs`.
- PDF render/cache/write path:
  `Models/PdfLayerRenderService.Render.cs`.
- PDF layer read/trace/probe path:
  `Models/PdfLayerRenderService.Layers.cs`.
- PyMuPDF worker and fallback command path:
  `Models/PdfLayerRenderService.Worker.cs`.
- PDF render protocol DTOs:
  `Models/PdfLayerRenderService.Protocol.cs`.
- PlanSwift generated segments / joist layout import:
  `Models/Import/PlanSwiftProjectImporter.Segments.cs`.
- Smart learning rule generation:
  `Models/SmartLearningStore.Rules.cs`.

## Remaining Large Production Surfaces

Do not split all of these at once. Pick one owner, read real call paths, then
build/tests/package.

- `MainWindow.xaml` - still large and high risk because named controls are wired
  directly to code-behind.
- `Dialogs/Massing3DWindow.cs` - large dialog, likely split by command/render
  ownership.
- `MainWindow.DisplaySettings.cs` - large display/settings owner.
- `MainWindow.Estimating.cs` - user-facing estimating workflow.
- `MainWindow.AiInbox.cs` - AI inbox workflow.
- `Models/PdfSheetMetadataService.cs` - metadata parsing/persistence.
- `MainWindow.SheetOverlay.cs` - rendering/performance-sensitive.
- `MainWindow.ToolControls.cs` - command and hotkey-adjacent UI.
- `Models/OpenAiRequestRunner.cs` - API runner; keep secrets out of docs/code.
- `MainWindow.ViewportCallbacks.cs` and `Controls/PdfViewport.Input.cs` -
  behavior-sensitive input paths.

## Suggested Next Session

1. Run a normal visible smoke from the Desktop shortcut.
2. Open the last job/page and confirm the sheet renders.
3. Check `%APPDATA%\OurPlaneCore\logs\app-<date>.log` after the latest
   `Application startup.` for `0` errors and a fresh `Viewport` line.
4. Do a focused UX pass on:
   top command tabs, selected highlight clipping, bottom strip, tree search
   boxes, Page Takeoffs context menu, and Bookmarks dock.
5. If continuing refactor, choose one safer target first:
   `PdfSheetMetadataService`, `Massing3DWindow`, or `DisplaySettings`.
6. Keep the done rule: after code changes, build, tests, compressed publish,
   update-folder deploy, shortcut check, launch-log check, then commit touched
   files only.

## Guardrails

- Do not stage `Assets/ourplanecore.ico.bak_20260522_132816` unless explicitly
  requested.
- Do not use `git add -A`.
- Keep visible takeoff names unchanged in copy/paste/duplicate/drag-copy flows.
- For Count defaults, app settings remain source of truth.
- For behavior-changing rules/templates, use the "8 Settings" editable-rule
  pattern: default equals current behavior, plus reset, presets, global default,
  and per-job override.
