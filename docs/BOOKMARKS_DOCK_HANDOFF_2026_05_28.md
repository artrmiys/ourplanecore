# Bookmarks Dock Handoff - 2026-05-28

## Scope

User issue: Bookmarks still felt impossible to detach/return in the left Pages
panel. The first implementation worked mechanically, but the control was a
small fourth button in the `Tabs / Detach / Tile M2` row and the docked panel
only had a tiny `x` return button.

This pass makes the Bookmarks dock control explicit and reversible:

- `Bookmarks` is now its own full-width button below `Tabs / Detach / Tile M2`.
- A small state dot on the right mirrors the `Tile M2` pattern and shows whether
  Bookmarks is currently docked below Pages.
- The docked Bookmarks panel now has a visible `Tab` button that returns the
  panel to the normal left-side tabs.
- Returning from docked mode selects the `Bookmarks` tab, so the user can see
  where it went.
- Status text reports `Bookmarks docked below Pages.` and
  `Bookmarks returned to the Pages tabs.`

## Implementation

Code commit: `3f15285 Fix bookmarks dock return`.

Touched files:

- `MainWindow.xaml`
  - Restored the first quick-action row to three columns.
  - Added `BtnToggleBookmarksDock` as a separate Bookmarks button.
  - Kept `BtnDockBookmarksBelowPages` as the small state/toggle dot.
  - Changed the docked-panel return control from `x` to `Tab`.
- `MainWindow.Bookmarks.cs`
  - Added `BtnToggleBookmarksDock_Click`.
  - Made the dock close handler robust even if the toggle state is already off.
  - Re-selects `_bookmarksTab` when returning Bookmarks to tabs.
- `Tests/TakeoffsTreeRegressionTests.cs`
  - Updated the Bookmarks wiring regression to require the separate button,
    toggle dot, dock host, `Tab` return button, state toggle handler, tab
    reselection, and existing `BK` shortcut path.

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
- Size: `176,553,489` bytes (`168.37 MB`)
- SHA256:
  `1C27F168D0CD550ED321D7FA57F696B9D307C1C2490E97A371804D80F9F30B19`
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
