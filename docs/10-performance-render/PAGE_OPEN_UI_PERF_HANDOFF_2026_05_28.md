# Page Open UI Performance Handoff - 2026-05-28

## Current Status

Implemented the next page-open performance slice after the persisted PyMuPDF
preview cache: the immediate `LoadPageIntoViewport` path now does only the work
needed to switch the active sheet and start the viewport load. Slower UI refresh
work is deferred to background dispatcher priority and guarded against stale
sheet switches.

Git checkpoint before the risky change:

- `checkpoint/before-page-open-ui-perf-20260528-173336`
- Checkpoint target: `24ffaa7 Document PDF preview cache`

Code commit:

- `e0b9539 Defer page open UI refresh work`

## Scope

The immediate page-open path still does the behavior-critical work:

- accepts the `PageInfo` already loaded by `LoadPageFromTab`;
- sets `_currentPage`, `_currentPdfPath`, status text, viewport scale, and scale
  UI;
- applies current page measurement scale;
- calls `_viewport.LoadPage(...)`;
- applies takeoff/page visibility immediately after the viewport page load;
- updates in-memory last page/job settings.

The immediate path no longer does:

- duplicate `OurPlaneCoreJobStore.TryReadPage(page.FolderPath)`;
- nearby page preview prefetch and first-job `source.json` scan;
- sheet overlay load;
- page annotations disk load;
- ruler, AI marker, and 3D roof guide overlay refresh;
- Pages tree silent selection;
- `SaveAppSettings()`;
- loaded takeoff visual/tree refresh;
- floating page setup refresh;
- duplicate sheet measurement hint.

Those follow-up operations now run through:

- `QueueDeferredPageOpenWork(...)`
- `RunDeferredPageOpenWork(...)`
- `IsCurrentPageOpen(...)`

The stale guard uses `_pageOpenDeferredVersion` plus the active page folder. If
the user switches sheets before the deferred work runs, the old work returns
without applying stale overlays or refreshes.

## Files

- `MainWindow.PageTabs.cs`
  - Removed the duplicate page metadata disk read from `LoadPageIntoViewport`.
  - Added `_pageOpenDeferredVersion`.
  - Split slower follow-up work into background dispatcher work.
  - Kept `ApplyViewportPageTakeoffVisibility(viewportPage)` in the immediate
    path so takeoff/layer visibility is correct on the first visible frame.
- `Tests/Program.cs`
  - Registered the new regression.
- `Tests/TakeoffsTreeRegressionTests.cs`
  - Added `PageOpenDefersHeavyUiWork`.
  - The test locks down no duplicate `TryReadPage`, viewport load before
    deferred work, background dispatcher scheduling, and the stale-guarded
    deferred operations.

## Verification

Commands run from `C:\Users\User\Desktop\ourplanecore`:

```powershell
git diff --check
dotnet build .\ourplanecore.sln
dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File C:\Users\User\.codex\skills\ourplanecore-update-package\scripts\update-ourplanecore-package.ps1
dotnet publish .\ourplanecore.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o .\bin\publish
```

Results:

- `git diff --check`: pass; only CRLF normalization warnings.
- `dotnet build`: pass, `0 warnings / 0 errors`.
- Regression tests: pass, `241/241 tests passed`.
- Standard package script: pass, but produced an uncompressed single-file exe
  first (`417,459,157` bytes, SHA256
  `9FB0A3108E8EBA08261C01BB5205DEA785F7ABA773175AA97F871F4DD5078EBC`).
- Final compressed publish: pass.

Final deployed package:

- Path: `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`
- Size: `176,924,808` bytes
- SHA256:
  `F5C3FFCA2255AFA91982F751A644E09954EBC191281C1DFA538D36E9EF58F0A6`
- `ourplanecore.exe.bak`: created/preserved from the intermediate uncompressed
  package, size `417,459,157` bytes.
- Desktop shortcut target:
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`
- Desktop shortcut working directory:
  `C:\Users\User\Desktop\updates\OurPlaneCore`

Packaged app validation:

- Launched deployed exe from the update package with hidden smoke process.
- Process was alive after 20 seconds.
- Checked `%APPDATA%\OurPlaneCore\logs\app-20260528.log` after the latest
  `Application startup.` marker.
- Error count after that marker: `0`.
- `Loaded takeoffs` signal: present.
- `Viewport` signal: present.
- Stopped the validation process after the log check.

## Remaining Performance Work

Still not changed in this slice:

- global `WorkerSemaphore = new(1, 1)` for PyMuPDF worker traffic;
- temp PNG round-trip between Python and C# was partially reduced later on
  2026-05-28 for bounded renders; see
  `docs/10-performance-render/PDF_INLINE_RENDER_HANDOFF_2026_05_28.md`;
- `fitz.Document` recreation when hidden layers are applied;
- `Bitmap.Copy()` on Docnet in-memory cache hits;
- first-job full `source.json` tree scan still exists, but it now runs in the
  deferred background-priority page-open work instead of before the first frame.

This slice improves perceived sheet switching by shortening the synchronous UI
path. It does not remove the total cost of overlays, tree refresh, settings
save, or prefetch; it moves them behind the first viewport load and prevents
stale deferred work from touching a newer active sheet.

## Notes

- The unrelated untracked file
  `Assets/ourplanecore.ico.bak_20260522_132816` was not touched.
