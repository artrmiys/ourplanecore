# PDF Preview Cache Handoff - 2026-05-28

## Current Status

Implemented the next safe page-open performance step from the
Open Jobs/PDF render handoff: a persisted clean PyMuPDF preview cache for the
first low-scale page image.

This does not bring back Docnet as a visible first frame. The first visible
render remains the clean PyMuPDF path, preserving the Caretta artifact fix.

## Scope

The cache is intentionally narrow:

- Only the first clean PyMuPDF preview render is persisted.
- The persisted preview scale is
  `ViewportRenderPolicy.InstantPagePreviewRenderScale` (`0.35`).
- Cache key identity is source PDF path, source modified time, source length,
  page index, and render scale.
- The cached image is used before queueing the normal PyMuPDF refresh render.
- The refresh render still runs, so layer discovery/state, snap reload, and the
  normal quality rerender behavior stay unchanged.

The cache is not used for:

- normal-quality rerenders;
- highlighted PDF layers;
- non-default layer-state renders;
- Docnet preview frames.

## Implementation

Code commit: `a6e00a4 Add persisted PDF preview cache`.

Touched files:

- `Models/PdfPreviewRenderCache.cs`
  - New persisted cache service.
  - Stores `.png` image plus `.json` metadata under
    `%LOCALAPPDATA%\OurPlaneCore\render-cache\pymupdf-preview`.
  - Supports test override through
    `OURPLANECORE_PDF_PREVIEW_CACHE_ROOT`.
  - Prunes best-effort to `512` preview images / about `1.5 GB`.
- `Models/PdfLayerRenderService.cs`
  - After a successful clean low-scale preview render, writes the preview image
    and metadata to the persisted cache.
- `Controls/PdfViewport.PageApi.cs`
  - Attempts to apply the persisted preview before queueing
    `QueueLayerRender(...)`.
  - Still queues the PyMuPDF refresh render every time.
- `Controls/PdfViewport.Layers.cs`
  - Applies cached preview bytes to the viewport without changing layer state.
  - Applies the initial fit/restored view without scheduling an extra rerender.
  - Logs `Viewport PyMuPDF preview cache hit` when the viewport consumes a
    persisted preview.
- `Tests/Program.cs`
  - Added a round-trip/invalidation test for the persisted preview cache.
- `Tests/TakeoffsTreeRegressionTests.cs`
  - Added source wiring coverage to ensure the cache is applied before the
    PyMuPDF refresh render and no separate Docnet first-frame path is added.

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
- Regression tests: pass, `240/240 tests passed`.
- Publish: pass, compressed single-file output created in `bin\publish`.

Deployed package:

- Path: `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`
- Size: `176,556,908` bytes (`168.38 MB`)
- SHA256:
  `3A9BB509B994FA219D2F8F5C620149A148C6BB962AABC2EAE11F0B4CF7236EB1`
- `ourplanecore.exe.bak`: exists and was preserved.
- Desktop shortcut target:
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`
- Desktop shortcut working directory:
  `C:\Users\User\Desktop\updates\OurPlaneCore`

Packaged app validation:

- Launched deployed exe from the update package.
- Process was alive after the smoke waits.
- Checked `%APPDATA%\OurPlaneCore\logs\app-20260528.log` after the latest
  `Application startup.`
- Error count after that marker: `0`.
- One packaged launch opened Caretta `a201.1 rf` and wrote a real preview cache
  pair:
  `%LOCALAPPDATA%\OurPlaneCore\render-cache\pymupdf-preview\18\18f9fd02e02e10672e5bee01c05a6cd5ebbe61346fb1b6d4e4cb81e43919f722.png`
  plus the matching `.json`.
- Later packaged smoke launches did not auto-open a viewport page, so a runtime
  `Viewport PyMuPDF preview cache hit` log line was not observed in that smoke.
  The read path is covered by the cache round-trip test and source wiring test.

## Remaining Performance Work

Still not changed in this slice:

- global `WorkerSemaphore = new(1, 1)` for PyMuPDF worker traffic;
- temp PNG round-trip between Python and C# was partially reduced later on
  2026-05-28 for bounded renders; see
  `docs/PDF_INLINE_RENDER_HANDOFF_2026_05_28.md`;
- `fitz.Document` recreation when hidden layers are applied;
- `Bitmap.Copy()` on Docnet in-memory cache hits;
- synchronous page-open work in `LoadPageIntoViewport` was partially reduced
  later on 2026-05-28; see
  `docs/PAGE_OPEN_UI_PERF_HANDOFF_2026_05_28.md`;
- first-job full `source.json` tree scan for nearby preview prefetch still
  exists, but the later page-open UI slice moved it behind the first viewport
  load at background dispatcher priority.

The persisted preview cache is the safer first step because it improves repeat
page opens without reintroducing Docnet artifacts or changing layer behavior.
