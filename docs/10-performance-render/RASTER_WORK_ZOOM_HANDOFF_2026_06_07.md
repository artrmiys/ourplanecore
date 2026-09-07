# Raster Work-Zoom Handoff - 2026-06-07

## User symptom

The user's normal workflow is: open a sheet, immediately zoom to about
100-180%, most often 107%, and pan/work there. The unacceptable behavior was:

- page opened blurry first;
- the user had to wait for the blur to clear on every sheet;
- at 100%+ the page became hard or impossible to pan;
- logs showed repeated raster DPI switching between 144 and 200.

The important working zoom from the live log is `zoom=1.067` / `1.068`.

## Commits Made

- `4732aff Speed up raster viewport refresh`
- `6b384c4 Warm raster work zoom path`
- `3cfa427 Stabilize raster work zoom dpi`

## Main Code Changes

### `Models/ViewportRenderPolicy.cs`

- Expanded lightweight job-open preview warmup to all pages.
- Kept heavy raster refresh and bitmap warmup bounded near the active sheet.
- Tuned deferred page-open work so tree/legend/overlay follow-up waits longer
  after zoom/pan.
- Set the work-zoom raster path to use `144dpi` for 100-180% style zoom.
- Added work-zoom warmup/build DPI lists using only `144dpi`.
- Avoided automatic high-DPI display for this normal work range.

### `MainWindow.PageTabs.cs`

- Moved page-list loading for preview/warmup off the UI thread.
- Removed the old cached prefetch page-list fields.
- Keued lightweight preview warmup active-page-first.
- Kept heavier raster bitmap/refresh warmups bounded so big jobs do not saturate
  the UI during page open.

### `Controls/PdfViewport.RenderCache.cs`

- Increased viewport bitmap cache budgets within bounded limits.
- Added raster work-zoom warmup plumbing:
  `PrefetchRasterSheetWorkZoomBitmaps(...)`.
- Added a separate work-zoom semaphore/in-flight guard.
- Warms/builds only the intended `144dpi` work raster path.
- Prevented non-source raster sources above the display max from being warmed
  into the normal viewport bitmap cache.

### `Controls/PdfViewport.RasterSheet.cs`

- On page open, the active sheet now queues work-zoom warmup directly.
- Avoids immediately decoding stored `200/400dpi` source rasters when `144dpi`
  is the actual work target.
- Keeps source-image raster fast-open behavior separate from PDF-derived raster
  sheets.

### `Controls/PdfViewport.RasterSheetDpiUpgrade.cs`

- Fixed the 144/200 feedback loop.
- The immediate repaint path now:
  - does nothing when current DPI already equals target DPI;
  - only upgrades from a lower DPI to the exact target DPI;
  - downshifts from a higher DPI only to the exact target DPI;
  - no longer treats ready `200dpi` as an acceptable substitute for target
    `144dpi`.
- Ready DPI selection is now exact-target only for the work-zoom path.

### `Controls/PdfViewport.PageApi.cs`

- Page open no longer uses responsive raster DPI selection unless a real
  `restoreView` exists.
- This prevents a newly opened/fitted page from borrowing stale zoom/DPI state
  from the previous page.

### Tests

- Updated `Tests/Program.cs` for `SelectRasterSheetDisplayDpi(1.28f) == 144`.
- Updated `Tests/TakeoffsTreeRegressionTests.cs` to guard:
  - all-page lightweight preview warmup;
  - bounded heavy raster warmup;
  - background page-list loading;
  - work-zoom raster warmup;
  - no automatic 400dpi display path;
  - exact-target DPI behavior so 144 is not replaced by 200.

## Verification Done

Commands run from repo root:

```powershell
dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false
dotnet run --project .\Tests\OurPlaneCore.Tests.csproj
dotnet publish .\ourplanecore.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o .\bin\publish
```

Results:

- build: 0 warnings, 0 errors;
- tests: 290/290 passed;
- compressed single-file publish succeeded;
- update package exe was replaced;
- existing `ourplanecore.exe.bak` was not overwritten;
- Desktop shortcut target and working directory were verified against the update
  package;
- publish/update SHA256 matched.

Final deployed exe:

- size: `176792710` bytes;
- SHA256: `96B47ABCC405BE9BED26CFB4D9868A17A4F343CAE5859543063C893A39312E34`.

Runtime verification:

- packaged app launched as PID `11644`;
- app log after the latest `Application startup.` had `ErrorCount=0`;
- at `zoom=1.067`, render profiles showed `bitmapScale=2`, which is the
  expected `144dpi` work raster;
- `Immediate200=0` after the final hotfix startup check;
- the last check during doc writing did not find PID `11644`, so the app may
  have been closed after verification.

## Log Evidence

Before the hotfix, the log showed repeated alternating entries on the same page:

- `scale=2` / `dpi=144`;
- `scale=2.778` / `dpi=200`;
- same zoom around `1.402` or normal work zoom after page activity.

After the final hotfix startup, the relevant 107% render profiles showed:

- `zoom=1.067`;
- `bitmapScale=2`;
- `renderedScale=2`;
- `targetScale=2`;
- cache hit;
- no immediate `200dpi` repaint.

## Current Repo State

Expected dirty state after the work:

- only untracked `Assets/ourplanecore.ico.bak_20260522_132816`.

This file was present before and was intentionally not touched.

## If Continuing Tomorrow

Start with the user's real workflow, not synthetic high zoom only:

1. Open the packaged app from the Desktop shortcut.
2. Open several sheets in the active job.
3. Immediately zoom to about `107%`.
4. Pan aggressively.
5. Watch the app log after the latest `Application startup.`.

Good behavior:

- no repeated 144/200/144/200 cycle on the same page at the same zoom;
- no `Viewport raster DPI immediate repaint prepared; dpi=200` while the current
  work zoom is about `1.067`;
- render profiles at 107% should stay on `bitmapScale=2`;
- page should pan normally after the first cached raster is applied.

If there is still visible waiting at 107%, the next likely area is not the DPI
loop anymore. Check:

- whether `working-144dpi.webp` is missing for the opened sheet;
- whether decode is still happening on the UI path instead of the warmed bitmap
  cache;
- whether page switch work is still competing with tree/legend/overlay refresh;
- whether the app is opening with a fit zoom first and applying the restored
  zoom only after the preview frame.

Primary files for next pass:

- `Controls/PdfViewport.RasterSheetDpiUpgrade.cs`
- `Controls/PdfViewport.RasterSheet.cs`
- `Controls/PdfViewport.RenderCache.cs`
- `Controls/PdfViewport.PageApi.cs`
- `MainWindow.PageTabs.cs`
- `Models/ViewportRenderPolicy.cs`
- `Tests/TakeoffsTreeRegressionTests.cs`

