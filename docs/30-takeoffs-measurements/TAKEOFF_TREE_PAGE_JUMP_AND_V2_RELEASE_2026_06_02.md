# Takeoff Tree Page Jump Fixes and v2 Release - 2026-06-02

## Scope

This handoff records the June 2, 2026 takeoff-tree/page-navigation stabilization work and the v2 packaged release that followed it.

Main user-facing goal: moving or dragging takeoffs must not randomly open a sheet such as `a502`, while selecting or moving an individual section/count row must still jump to the row's real measurement sheet.

## What Changed

### Page tabs

- Added draggable page tabs for open sheets.
- Dragging a tab between other tabs reorders it.
- Dragging a tab out into free space detaches it into a separate sheet window.
- Fixed tab drag activation so clicking a page tab does not immediately start a bad drag.

Key commits:

- `ecf38e7 Add draggable page tabs`
- `10d67bf Fix page tab drag and tree collapse`

### Sheet/page rendering

- Preserved sharp page rendering during fast page opens.
- Queued sheet overlay work before prefetch to reduce stale/blurred overlay behavior.
- Fixed detail render scale clamp and overlay/takeoff drag sync paths.

Key commits:

- `7e49c82 Keep fast page opens sharp`
- `af3c501 Fix detail render scale clamp`
- `ac923c1 Fix overlay and takeoff drag sync`
- `7f6e9d7 Queue sheet overlays before prefetch`

### Section/count row page jumps

- Fixed section/count row selection so it targets the active row's `Measurement.PageFolder`.
- Multi-select and drag payloads now preserve the active/dragged row as the primary row.
- Moving a section/count row between takeoff items jumps to that row's measurement sheet, by design.
- Added regression coverage so future changes do not silently fall back to "current page" or a random tree-order page.

Key commit:

- `d1e55e8 Fix takeoff section page jumps`

Primary files:

- `MainWindow.Estimating.cs`
- `MainWindow.TakeoffsTree.cs`
- `MainWindow.TakeoffsDragDrop.cs`
- `MainWindow.TakeoffsSelectionHelpers.cs`
- `Tests/TakeoffsTreeRegressionTests.cs`

### Whole takeoff moves must keep the current viewport page

- Fixed move fallback after `LoadTakeoffsForJob()` so it restores moved takeoff selection silently.
- Whole takeoff move up/down, cut/paste move, root-bottom move, and position drop now avoid page-opening selection handlers.
- Active takeoff state is still refreshed after a move; the viewport page is not changed.

Key commit:

- `6c3b249 Keep page stable when moving takeoffs`

Primary files:

- `MainWindow.TakeoffsClipboard.cs`
- `MainWindow.TakeoffsDragDrop.cs`
- `MainWindow.TakeoffsNodeActions.cs`
- `MainWindow.TakeoffsTreeFastRefresh.cs`
- `MainWindow.ViewportTreeOpsSmoke.cs`

### Actual drag/drop takeoff page jump to `a502`

Root cause found after the packaged v2 still showed the bug:

- `RevealPagesForTakeoffItems(...)` selected a linked `PageTakeoffNode` in the left Pages tree while a takeoff was being selected/dragged in the right Takeoffs tree.
- If that Pages tree selection event fired, `SelectLinkedPageTakeoff(...)` could call `OpenPageInActiveTab(node.Page)`.
- In the user's active Croton Point job this often opened `a502`.

Fix:

- Takeoffs-tree reveal now expands and scrolls the linked Pages row into view, but no longer sets `preferredLinked.IsSelected = true`.
- The linked Pages row can still be highlighted through the existing selection sets, but selecting/dragging a takeoff no longer opens that linked sheet.
- Added heavy smoke coverage that calls the real `DropTakeoffPosition(...)` path and fails if the viewport page changes during takeoff drag/drop reorder.

Key commit:

- `3746b67 Stop takeoff drag from opening linked pages`

Primary files:

- `MainWindow.TakeoffSelectionNavigation.cs`
- `MainWindow.ViewportTreeOpsSmoke.cs`
- `Tools/ui_viewport_page_stress_smoke.ps1`
- `Tests/TakeoffsTreeRegressionTests.cs`

## Verification

Source verification:

```powershell
dotnet build .\ourplanecore.sln
dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build
```

Result:

- Build: `0 warnings / 0 errors`
- Tests: `272/272 passed`

Heavy smoke verification:

```powershell
C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\ui_viewport_page_stress_smoke.ps1 -JobPath "C:\Users\User\Desktop\Takeof_desctop\89. 1 Croton Point_Scott_Probuild" -CopyJob -IncludeTreeOps -TimeoutSeconds 300 -PageTimeoutMs 10000 -ReturnCount 6 -TabCount 5 -OpenCount 12 -TargetZoom 3.5 -PanSteps 5
```

Important smoke signals:

- `tree ops takeoff drag/drop: single reorder 66/62 ms`
- `tree ops takeoff sections: move/restore jumped to measurement page in 2039 ms`
- `PASS viewport page stress smoke`

Meaning:

- Whole takeoff drag/drop reorder kept the viewport page stable.
- Section/count row move still jumped to its measurement page.
- Page opens remained nonblank and passed opacity probes.

## v2 Release

Published current `HEAD`:

- Commit: `3746b67`
- Release exe: `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore-v2.exe`
- Old fallback exe kept: `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`
- Existing v2 backup kept: `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore-v2.exe.bak`
- Desktop shortcut: `C:\Users\User\Desktop\OurPlaneCore.lnk`
- Shortcut target: `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore-v2.exe`
- Shortcut working directory: `C:\Users\User\Desktop\updates\OurPlaneCore`

Release publish command:

```powershell
dotnet publish .\ourplanecore.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o .\bin\publish-v2
```

Release verification:

- Publish/target SHA256 matched:
  `195C35F7C83271B203F237E78F4698D1F00A557144AED18DC5975F3F28239182`
- Packaged v2 launch check:
  - process stayed alive after the wait;
  - `0` errors after the latest `Application startup.`;
  - log showed `Loaded takeoffs tree` and `Viewport render`.

Log checked:

```text
C:\Users\User\AppData\Roaming\OurPlaneCore\logs\app-20260602.log
```

## Current Follow-Ups

- If the user still sees a takeoff drag opening a page, test with a visible manual run from `ourplanecore-v2.exe` and specifically drag whole takeoff items, not section/count rows.
- Keep section/count row behavior separate from whole takeoff behavior:
  - section/count row: should jump to the row's measurement page;
  - whole takeoff: should not switch the viewport page during move/reorder.
- The untracked file `Assets/ourplanecore.ico.bak_20260522_132816` remains untouched.
