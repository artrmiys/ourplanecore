# Bookmarks Dock Handoff - 2026-05-28

## Scope

User issue: Bookmarks still felt impossible to detach/return in the left Pages
panel. The first implementation worked mechanically, but the control placement
was wrong for the target layout.

Final user-approved direction:

- Do not add a separate large `Bookmarks` button below `Tabs / Detach / Tile M2`.
- Keep the Pages quick-action row compact.
- Put only a small circle toggle directly into the `Bkm` tab header.
- In docked mode, use the small circle in the docked Bookmarks header to return
  Bookmarks to the tab list.
- Hide the Bookmarks list column headers `Name`, `Page`, and `View`.
- Returning from docked mode selects the `Bkm` tab, so the user can see where it
  went.
- Status text reports `Bookmarks docked below Pages.` and
  `Bookmarks returned to the Pages tabs.`

## Implementation

Code commits:

- `3f15285 Fix bookmarks dock return`
- `922c474 Move bookmarks dock toggle into tab`

Touched files:

- `MainWindow.xaml`
  - Restored the first quick-action row to three columns.
  - Removed the separate large `BtnToggleBookmarksDock` button.
  - Added shared `BookmarkDockToggleButton` styling for the small circle.
  - Kept `BtnDockBookmarksBelowPages` as the docked-header circle toggle.
- `MainWindow.Bookmarks.cs`
  - Builds the Bookmarks tab header as `Bkm` plus a small circle toggle.
  - Synchronizes the tab-header and docked-header circle states.
  - Re-selects `_bookmarksTab` when returning Bookmarks to tabs.
  - Hides the `GridView` column header row for `Name`, `Page`, and `View`.
- `Tests/TakeoffsTreeRegressionTests.cs`
  - Updated the Bookmarks wiring regression to require the compact circle
    control, reject the separate large button, require tab reselection, require
    hidden column headers, and keep the existing `BK` shortcut path.

Existing shortcut coverage still verifies:

- English sequence: `bk`
- Russian-layout equivalent is handled through the dual-layout shortcut
  normalization used by `KeyboardShortcutKeys`.

## Verification

Commands run from `C:\Users\User\Desktop\ourplanecore`:

```powershell
git diff --check
dotnet build .\ourplanecore.sln
dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build
dotnet publish .\ourplanecore.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o .\bin\publish
```

Results:

- `git diff --check`: pass; only existing CRLF normalization warnings.
- `dotnet build`: pass, `0 warnings / 0 errors`.
- Regression tests: pass, `238/238 tests passed`.
- Publish: pass, compressed single-file output created in `bin\publish`.

Deployed package:

- Path: `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`
- Size: `176,553,666` bytes (`168.37 MB`)
- SHA256:
  `60FF6304A298F73621C126265CA04C128CB8DE6413DE9B2B993C55B1C5284312`
- `ourplanecore.exe.bak`: exists and was preserved.
- Desktop shortcut target:
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`
- Desktop shortcut working directory:
  `C:\Users\User\Desktop\updates\OurPlaneCore`

Packaged app smoke check:

- Launched deployed exe from the update package.
- Process was alive after 25 seconds.
- Checked `%APPDATA%\OurPlaneCore\logs\app-20260528.log` after the latest
  `Application startup.`
- Error count after that marker: `0`.
- Log contained `Loaded takeoffs` and `Viewport` entries.

## Caveats

- This is a UX/wiring fix, not a Bookmarks storage rewrite.
- The smoke log still shows normal slow PDF render `INFO` lines on the current
  Caretta job. They are not startup errors and are separate from the Bookmarks
  dock control.
- Unrelated untracked file left untouched:
  `Assets/ourplanecore.ico.bak_20260522_132816`.
