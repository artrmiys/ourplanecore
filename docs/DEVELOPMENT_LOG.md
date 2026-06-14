# Development Log

## 2026-06-14 Perf: machine-adaptive page prefetch (kill the page-switch tail)

- Profiled real page switching with the viewport page-stress smoke against the live 389-page
  job. Median open/return ~54-65 ms (fine) but a long tail: first-open up to 659 ms, re-open
  up to 597 ms — those stalls are what reads as "not instant". An already-warm tab paints in
  ~6 ms (the target).
- Existing nearby-page prefetch was deliberately conservative ("without raising render
  concurrency"): radius 1, directional 3, and `NearbyPageCleanRenderPrefetchRadius = 0` (no
  sharp prefetch). That ceiling predates the parallel prefetch worker pool, which now runs
  prefetch on its own processes, separate from the interactive render worker.
- Made the radii machine-adaptive via `HasSpareRenderCapacity` (cores>=8 && RAM>=24 GB):
  preview/readable radius 1->2, directional 3->6/5, and clean-render 0->1 so the immediate
  neighbour is pre-rendered **sharp** (crisp instantly, no blur->sharp flash). Small machines
  unchanged.
- Measured before/after on the same spread smoke (the harder case — wasted prefetch for
  far-apart samples): re-open tail 597->63 ms, first-open tail 659->338 ms, new-tab 351->58 ms,
  tab-activate 363->40 ms. Opens got faster, not slower (pool absorbs it). 0 failures.
- Verified: build 0/0, tests 340/340 (guards updated for the adaptive values).

## 2026-06-14 GPU viewport (SKGLElement) investigated and rejected

- Tried migrating `PdfViewport : SKElement` → `SKGLElement` (GPU/VRAM) on an isolated
  worktree. Build failed: SkiaSharp.Views.WPF 2.88.8 ships **no** GPU element
  (`SKGLElement`/`SKPaintGLSurfaceEventArgs` absent from the assembly — verified by
  inspecting the nuget DLL). WPF composes via DirectX, so SkiaSharp has no first-party
  WPF GPU control; the only route is a hand-rolled GL host + D3DImage interop (large,
  driver-fragile). Worktree/branch discarded.
- More importantly it would target the wrong layer: the live-log slow frames (445-744 ms)
  are `cache=False` **PyMuPDF** renders, while Skia paint frames are already 0-8 ms. The
  bottleneck is PDF render + its serialization (addressed by the prefetch pool below), not
  CPU rasterization. GPU would accelerate an already-instant layer. Not pursued.

## 2026-06-14 Perf: parallel detail-prefetch worker pool (instant deep-zoom fill)

- Profiled the *non-zero* render frames in the live log: ~all frames are 0-8 ms, but a
  tail of `kind=detail-prefetch` tiles ran 445-744 ms each **back-to-back** (one `detail`
  then 3-4 `detail-prefetch` over ~2 s) at zoom 1.6-4.0. That serial tile-fill is the
  "not instant" feeling when drawing/panning at deep zoom.
- Root cause: the 4 prefetch clip tiles fan out concurrently in C# but then queue on two
  size-1 gates — `ViewportRenderPolicy.DetailRenderPrefetchConcurrency = 1` and a single
  persistent PyMuPDF *prefetch* worker process.
- Fix (both widened, machine-adaptive `clamp(cores/3, 1, 4)`; =1 on small boxes, 4 on this
  12-thread box):
  - `ViewportRenderPolicy.DetailRenderPrefetchConcurrency` is now adaptive (sizes the C#
    `DetailTilePrefetchSemaphore`).
  - `PdfLayerRenderService` prefetch worker → a **pool** of persistent processes
    (`PrefetchPoolSlots`/`PrefetchFreeSlots` + per-slot process arrays, `EnsurePrefetchSlot`/
    `ResetPrefetchSlot`, pooled `TryInvokePrefetchWorkerAsync`). Primary/Detail workers are
    left exactly as-is. Pool is prewarmed in parallel and drained on `StopWorker`.
  - Extracted the shared line protocol into `ExchangeWithWorkerAsync` and the spawn into
    `StartWorkerProcess` so the single-worker and pooled paths can't drift.
  - Net: the deep-zoom tile fan-out renders in parallel on idle cores — ~540 ms to fill a
    screen instead of ~2 s, no extra UI-thread work.
- Verified: build 0/0; Tests harness 340/340 (added a guard for the pool; also fixed three
  guards that pinned literals from the prior decode + adaptive-budget commits, which hadn't
  been Tests-validated yet). Debug-only; not deployed (user working in the deployed build).
- Still-open biggest lever: viewport is `SKElement` (CPU Skia) — RTX 3060 / 12 GB VRAM idle;
  GPU backend (SKGLElement) needs a live visual pass, deferred until the machine is free.

## 2026-06-14 Perf: RAM-adaptive cache budgets (use the big machine)

- User has 64 GB RAM + RTX 3060 12 GB; earlier "0.1 GB free" was my misread of
  FreePhysicalMemory (standby cache) — Task-Manager Available was 53.8 GB. Plenty
  of RAM. Goal: let big machines keep more sheets/tiles hot, small machines unchanged.
- `PdfViewport.RenderCache.cs`: raised the upper caps on the four ratio-bound bitmap
  cache budgets (Docnet / persisted-preview / raster-sheet / layer) to 1.79-2.56 GB and
  the entry counts (128/768/128/96) so the RAM-adaptive byte budget is the real limit.
  Ratios and minimums unchanged → 8-16 GB machines still land on the old small budgets.
- `PdfLayerRenderService.cs`: the Python render cache used fixed consts. Made
  `MaxRenderCacheBytes` and `MaxRenderCacheEntryBytes` RAM-adaptive via
  `ResolveRenderCacheRamBudget`. The per-entry cap (was a hard 96 MB) now scales to
  384 MB on big-RAM boxes: a full-page large-sheet raster at high dpi (~150 MB raw)
  used to exceed 96 MB and be rejected from cache → re-rendered on every view; it now
  caches. Min stays 96 MB / 768 MB so small machines are unaffected.
- Debug-only this pass (user actively working in the deployed exe); not deployed, no
  live launch (would double-write the live job). Build 0/0.
- Still-open biggest lever: viewport is `SKElement` (CPU Skia) — RTX 3060 / 12 GB VRAM
  idle; GPU backend (SKGLElement) deferred to an idle profiling pass.

## 2026-06-14 Perf: parallel, allocation-light raw-render decode

- Raw PyMuPDF renders (BGR/BGRA bytes) were converted to SKBitmap by a
  single-threaded per-pixel loop into a full-image intermediate `byte[]` before
  one Marshal.Copy. Two identical copies existed (detail tiles + main raster).
- Now `PdfLayerRenderService.CreateBitmapFromRawRender` writes straight into the
  bitmap's pixel buffer (no intermediate buffer -> lower peak RAM + less GC) and,
  for rasters >= 256 rows, spreads the per-row copy across all cores via
  Parallel.For. Output is byte-identical. `PdfViewport.DecodePdfLayerRenderBitmap`
  now delegates to it (one implementation).
- Debug-only verification this pass (user actively working); not deployed.
- Bigger levers identified for later (need a profiling pass while idle): the
  viewport is `SKElement` (CPU Skia, UI thread) — the RTX 3060 / VRAM is unused;
  GPU backend (SKGLElement) is the largest win. Cache RAM budgets are clamped
  conservatively but free RAM was near zero, so deferred.

## 2026-06-14 Hotfix: deep-zoom measurement freeze (raster quality-restore spin)

- Symptom: app froze (UI thread pegged ~100% of one core, logging stopped) when
  zooming to ~600%+ on a raster-sheet page and drawing measurements. Captured
  live from the hung process via dotnet-trace; the hot stack was a self-re-posting
  dispatcher loop `RestoreRasterSheetQualityAfterMotionAsync` ->
  `QueueRasterSheetQualityRestoreAfterMotion` -> ... with zero delay.
- Root cause: the restore delay was computed from raw wall-clock idle
  (`now - _lastFastNavigationAt`) while the "should hold heavy DPI" check used a
  fast-nav-aware idle (zero while `_isFastNavigating`). With a held pointer the
  raw idle grows past the 450ms quiet window (delay -> 0) while the hold check
  still says "hold" (target DPI > 144 at deep zoom), so the restore re-queued
  itself every dispatcher tick -> busy spin.
- Fix: single shared `RasterSheetMotionIdle()` feeds both the hold check and the
  restore delay, so a held/active fast-nav state yields a 450ms re-poll instead
  of a 0ms spin. `PdfViewport.RasterSheetDpiUpgrade.cs`.

## 2026-06-11/12 v2.0.0–v2.2.1: Features, Hardening, Rafters, Warmup Opt-In

Full detail: `docs/SESSION_2026_06_11_V2_SUMMARY.md`. Highlights:

- Multiline line takeoffs (up to 2 auto-offset companion lines), 4 new count
  marker shapes, Export PDF folder tree with current-sheet highlight, per-sheet
  export from the pages tree context menu.
- v2.0.0 hardening after a 5-dimension audit: atomic Data.xml writes, job-relative
  paths in measurements.json, autosave/undo ordering fixes, render hot-path
  de-LINQing, OpenAI retries, log pruning, AI_Context archiving, real versioning
  (csproj + title + startup log).
- v2.1.0: offline "Count Similar" symbol matcher (bit-parallel, threshold dialog
  with live ghost preview, optional online AI double-check).
- v2.2.0: rafters on 3D roof faces — per-face pick or whole roof, slope-corrected
  lumber-rounded lengths, walls trim to the rafter underside.
- v2.2.1: perf regression triage — the June-10 whole-job warmup had shipped for
  the first time with these deploys and competed with interactive sharpening;
  it is now opt-in (Settings -> Defaults, default OFF), worker timeout restored
  to 30s, work-zoom warmup yields between DPI steps.
- Tests 303 -> 309/309; two brittle source-scan tests fixed.

## 2026-06-06 Low-Zoom Raster Paint Lag Pass

- Fixed a remaining sheet-open lag where fit/low-zoom page opens could apply a
  high-DPI readable raster sheet, including 300/400 DPI full-page bitmaps, even
  though the user was viewing the whole sheet.
- Ordinary readable raster sheets now skip full-raster page-open at low zoom and
  use the fast PDF preview until the current zoom reaches the raster display
  threshold. When zooming back out, they switch back to preview instead of
  continuing to paint the heavy full-sheet bitmap.
- Source-image PlanSwift rasters keep their existing behavior: fast small
  source images may stay visible, and oversized source images can still use
  their overview raster.
- Nearby/job-open warmup no longer decodes full readable raster bitmaps for
  ordinary PDF-derived pages. It keeps cheap previews hot and only prefetches
  source-image fast-open/overview bitmaps, so the first seconds after opening a
  job no longer compete with large 200/400 DPI bitmap decodes that low-zoom
  viewing will not use.
- This directly targets the observed packaged runtime log line where a
  `5.556x` raster sheet at `zoom=0.172` produced a `181ms` slow frame with
  `page:169ms`.

## 2026-06-06 Page-Switch Preview and Stale Detail Render Pass

- Changed cold page-switch preview rendering to try the reusable PyMuPDF worker
  before Docnet, keeping Docnet as fallback. This targets stress runs where a
  missing preview cache could spend up to about one second in a 0.35-scale
  Docnet preview before the sheet became ready.
- When a page switch or render clear makes an in-flight clipped detail render
  stale, the viewport now cancels the detail worker instead of letting that
  obsolete render compete with the next sheet. Stale detail completions are
  discarded before they are recorded as useful render samples.
- Navigation idle now coalesces clipped detail rendering instead of starting it
  immediately, reducing the chance that a short pan/zoom burst waits behind a
  detail worker startup while still sharpening after the user stops.
- Clipped detail rendering now also waits for a real navigation quiet window
  after pan/zoom before starting its worker, so the delayed sharp upgrade cannot
  land inside a short zoom/pan sequence and hold the UI.
- Deferred page-open follow-up work (overlays, annotations, settings save,
  takeoff visual refresh, floating setup/hints) now also waits for the viewport
  navigation quiet window and rechecks the active page, so opening a sheet and
  immediately zooming/panning is not interrupted by tree/legend/UI refreshes.
- Automated viewport stress runs now skip the stale recovery `MessageBox` and
  overwrite their own lock, so a killed hidden run does not block the next
  measurement before the first sheet opens.

## 2026-06-06 Sheet Render Sharpness Pass

- Shifted the viewport sharpness path toward visible clipped detail rendering
  instead of higher whole-sheet raster DPI.
- `ViewportRenderPolicy` now starts detail rendering earlier:
  `DetailRenderMinZoom = 0.75`, `DetailRenderMinScaleGain = 1.04`,
  `DetailRenderCoalesceDelayMs = 120`, and
  `PageSwitchDetailRenderDelayMs = 100`.
- Raised the interactive clipped-detail scale caps so High/Max can actually
  sharpen work zooms and raster-sheet views: High can render visible clips up
  to `4x`, Max up to `6x`, while the existing pixel-budget caps still bound
  oversized clips.
- Reduced navigation idle before sharp detail work from `420ms` to `260ms` and
  made detail render start immediately after navigation idle / delayed sharp
  upgrade paths instead of adding another coalesce wait.
- Verification: `dotnet build .\ourplanecore.sln` passed with `0` warnings /
  `0` errors; `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj
  --no-build` passed `290/290` tests.
- Render benchmark after the change on the Mallory fixture:
  full-sheet PNG averaged `1957.1ms`, visible clip raw averaged `675.9ms`,
  clip cache hit was `0.0ms`, so visible rendering stayed `65.5%` faster than
  full-sheet rendering.

## 2026-06-06 Job Move Autorepair / Detail Legend Sort

- Implemented automatic repair for non-empty stale measurement `page_folder`
  values when a whole job folder is moved. The loader now resolves page links by
  exact path first, then by the suffix after the real `Pages` path segment under
  the current job root, before falling back to unique leaf / legacy `Page N`
  matching.
- Updated the sheet/detail reference comparer so takeoffs like `1/S5.5`,
  `2/S5.5`, `4/S5.10`, and `13/S5.10` sort by sheet number parts first and
  detail number second. This feeds page legend order and takeoff tree sorting.
- Fixed the live page legend/export path to respect saved manual legend order;
  new takeoffs not in the saved order append by the same auto-sort rules.
- Verification: `dotnet build .\ourplanecore.sln` passed with `0` warnings /
  `0` errors; `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj
  --no-build` passed `290/290` tests.
- Published compressed single-file Release to
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe` and verified
  runtime from the packaged exe. Latest log section showed
  `Loaded takeoffs tree with 981 item(s)`, viewport activity on the moved
  Commerce job, and `0` errors after `Application startup.`.

## 2026-06-06 Docs Cleanup / Next Task Handoff

- Repaired the moved Commerce job data in place before this docs update:
  `4520 / 4520` stale measurement `page_folder` values were rewritten from the
  old absolute job root to the current Desktop job root by preserving the suffix
  after `\Pages\`; missing/empty page links after repair were `0`.
- Packaged runtime verification for that job passed from
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`: latest log
  section showed `Loaded takeoffs tree with 933 item(s)`, viewport activity,
  and `0` errors after the latest `Application startup.` marker.
- Wrote the next implementation handoff:
  `docs/00-start-here/NEXT_TASK_JOB_MOVE_AUTOREPAIR_AND_SHEET_RENDER_PERF_2026_06_06.md`.
  It covers whole-job-move autodetect for `page_folder` repair and the next
  measured strategy for sheet render speed/clarity.
- Audited markdown organization and restored the root `docs/` folder to
  canonical files only. Moved:
  - `docs/JOB_CREATION_AND_STORAGE_FLOW_2026_06_05.md` ->
    `docs/20-import-pages-metadata/JOB_CREATION_AND_STORAGE_FLOW_2026_06_05.md`;
  - `docs/SHEET_RENDERING_ANALYSIS_AND_INSTANT_STRATEGY.md` ->
    `docs/10-performance-render/SHEET_RENDERING_ANALYSIS_AND_INSTANT_STRATEGY_2026_06_04.md`;
  - `docs/CHANGELOG_2026-06-06_rotation-zoom-ribbon.md` ->
    `docs/90-archive-prompts/CHANGELOG_2026-06-06_rotation-zoom-ribbon.md`.
- Added the markdown audit note:
  `docs/00-start-here/DOCS_AUDIT_2026_06_06.md`.
- Updated `docs/README.md`, `docs/OURPLANECORE_TASK_ROADMAP.md`, and
  `docs/CURRENT_OURPLANECORE_STATUS.md` so the current top priority is no
  longer hidden behind older UX/3D tasks.

## 2026-06-02 Takeoff Tree Page Navigation / v2 Release

- Stabilized takeoff-tree page navigation:
  - section/count row selection and moves now jump to the row's real
    `Measurement.PageFolder`;
  - whole takeoff moves/reorders keep the current viewport page stable;
  - Takeoffs-tree reveal no longer selects linked `PageTakeoffNode` rows in the
    Pages tree, preventing takeoff drag from opening sheets such as `a502`.
- Added heavy smoke coverage for the real `DropTakeoffPosition(...)` drag/drop
  reorder path and kept the section/count row page-jump smoke separate.
- Related commits:
  `d1e55e8 Fix takeoff section page jumps`,
  `6c3b249 Keep page stable when moving takeoffs`,
  `3746b67 Stop takeoff drag from opening linked pages`.
- Verification passed:
  `dotnet build .\ourplanecore.sln` (`0 warnings / 0 errors`),
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build`
  (`272/272`), and the viewport/tree-ops stress smoke on the Croton Point job.
- Published v2 to
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore-v2.exe`, kept the
  old `ourplanecore.exe` fallback, kept `ourplanecore-v2.exe.bak`, and pointed
  `C:\Users\User\Desktop\OurPlaneCore.lnk` to v2.
- Publish/target SHA256:
  `195C35F7C83271B203F237E78F4698D1F00A557144AED18DC5975F3F28239182`.
- Packaged v2 startup log check passed with `0` errors after the latest
  `Application startup.` and showed `Loaded takeoffs tree` plus `Viewport`
  render activity.
- Detailed handoff:
  `docs/30-takeoffs-measurements/TAKEOFF_TREE_PAGE_JUMP_AND_V2_RELEASE_2026_06_02.md`.

## 2026-05-31 Refactor / UX Stabilization Handoff

- Fixed the visible command-tab clipping issue from the prior UX pass by
  containing the selected highlight so the right side of the button chrome no
  longer gets cut off.
- Continued safe no-behavior refactoring to reduce large-file risk:
  - split `MainWindow.ThreeDWalls`, `MainWindow.ThreeDRoof`, and
    `MainWindow.PageTakeoffLegend` into focused partial owners;
  - extracted app/window resource dictionaries from `App.xaml` and
    `MainWindow.xaml`;
  - split `SmartContextStore`, `PlanSwiftProjectImporter`,
    `PdfLayerRenderService`, and `SmartLearningStore` into model / workflow /
    IO / protocol owners.
- Updated source-wiring regression tests after the partial splits.
- Verification passed:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  (`0 warnings / 0 errors`),
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj /p:OutDir=.\cache\test_run\ /p:UseAppHost=false`
  (`250/250`), `git diff --check`, and conflict/TODO/NotImplemented scan.
- Published and deployed the compressed single-file exe to
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`, SHA256
  `C2B11CE908FFFDAED6AA094EADA7E8AE78B33715490CFE0EA6C151B2D2331286`.
- Shortcut target and working directory were restored to the update folder.
- Packaged launch validation had `0` errors after the latest
  `Application startup.` and loaded takeoffs. A fresh `Viewport` log line did
  not appear in the short hidden validation launch, so the next session should
  do a normal visible shortcut smoke by opening a sheet.
- Code commits:
  `c3abc9a Split large UI and context surfaces` and
  `9b71181 Split import render and learning stores`.
- Detailed handoff:
  `docs/70-architecture-refactor/REFACTOR_UX_HANDOFF_2026_05_31.md`.

## 2026-05-30 Blank Job / Blank Sheet

- Added a no-PDF job creation path and blank sheet creation inside normal
  OurPlaneCore jobs.
- Existing PDF job creation remains folder-first and recursive. The new route
  is explicit:
  - Open Job dialog: `Blank job`;
  - `Open / Import`: `Blank Job...`;
  - Page tab, Pages side panel, Pages tree context menu, and command palette:
    `Blank Sheet`.
- Blank sheets create an internal generated `*.blank.pdf` under the job
  `sources` folder and still write normal `Data.xml`, `source.json`, and
  `source_pdf.json`. This keeps viewport tabs, scale, measurements, export, and
  thumbnails on the existing page contract.
- Blank sheet defaults to a landscape `36 in x 24 in` PDF page and starts
  unscaled; the user can rename/scale it through existing Page Setup.
- Optimization check:
  - today's app log still shows viewport PyMuPDF/layer render as the primary
    visible lag source, with repeated `Viewport slow layer render` entries and
    a packaged validation run showing `Viewport slow layer render 1057ms`;
  - no render-pipeline optimization was mixed into this feature change.
- Verification passed:
  `dotnet build .\ourplanecore.sln` (`0 warnings / 0 errors`),
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build`
  (`250/250`), compressed single-file publish/deploy to
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`, SHA256
  `D9B24F614A91D80A35F340A06823BF06C114B11659697C379EAF9FC40E296A0C`, shortcut
  target/workdir check, and packaged launch log check with `0` errors after
  the latest `Application startup.`.
- Code commit: `5a2eb9e Add blank job and sheet`.
- Detailed handoff:
  `docs/20-import-pages-metadata/BLANK_JOB_BLANK_SHEET_HANDOFF_2026_05_30.md`.

## 2026-05-30 Detail Reference Sorting

- Added detail reference sorting for takeoff names like `14/S502` and
  `13/S101`.
- Rule:
  - sort first by the sheet after `/` (`S101`, `S502`);
  - then sort by the detail number before `/`;
  - apply only to that detail-reference shape;
  - keep natural name sorting for normal takeoff names.
- Applied to live sheet legend/page takeoff auto order and Takeoffs tree child
  sorting. Pages tree sorting was not changed.
- Verification passed:
  `dotnet build .\ourplanecore.sln` (`0 warnings / 0 errors`),
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build`
  (`249/249`), compressed package deploy, SHA256
  `494E5ECF70EDBF33B72813BC999697643FAD7F4C03A77AE628EF569243F52DDF`, shortcut
  check, and packaged launch log check with `0` errors after the latest
  `Application startup.`.
- Code commit: `3bc8b93 Sort detail refs by sheet`.

## 2026-05-28 PDF Full Render Cache

- Extended the persisted PyMuPDF cache from only the first `0.35` preview to
  bounded clean rerenders up to render scale `2.25`.
- The cache is clean-only: hidden PDF layer states and highlighted layers do
  not use it. Unknown layer metadata also does not bypass worker discovery.
- Cache bounds:
  - max render scale: `2.25`;
  - max estimated rendered pixels: `30,000,000`;
  - max PNG bytes per cached render: `96,000,000`;
  - existing total cache pruning still applies.
- Added cache-hit application before queueing the Python worker for non-reset
  clean layer renders, with log line `Viewport PyMuPDF render cache hit`.
- Verification passed: `git diff --check`, conflict/TODO scan,
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  (`0 warnings / 0 errors`),
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj` (`244/244`),
  compressed single-file publish/deploy to
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`, SHA256
  `BB5AE3C191CC79283BA9407271DE16EEF6E1C3E987D0F06B446C7C0E0BBC85F6`,
  shortcut target/workdir check, and two packaged launch log checks with `0`
  errors after the latest `Application startup.`.
- Checkpoint before code:
  `checkpoint/before-full-render-cache-20260528-184340`.
- Code commit: `7b03854 Cache clean PDF rerenders`.
- Detailed handoff:
  `docs/10-performance-render/PDF_FULL_RENDER_CACHE_HANDOFF_2026_05_28.md`.

## 2026-05-28 Underlayment / Sheet Overlay Clarity

- Sharpened the sheet overlay/underlay viewport path in
  `Controls/PdfViewport.SheetOverlay.cs`.
- The overlay bitmap no longer uses `SKFilterQuality.Low` during fast
  navigation. Fast frames now use `Medium`, settled frames use `High`, and
  bitmap antialiasing is disabled for crisper reference linework.
- Added regression coverage:
  `TakeoffsTreeRegressionTests.SheetOverlayRenderingUsesSharperSampling`.
- Verification passed: `git diff --check`, conflict/TODO scan,
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  (`0 warnings / 0 errors`),
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj` (`243/243`),
  compressed single-file publish/deploy to
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`, SHA256
  `9DCFEF64D6F8F75C62BEE38A7850981F9550102FFE1DCBC328CBAC8BEE756211`,
  shortcut target/workdir check, and packaged launch log check with `0` errors
  after the latest `Application startup.`.
- Code commit: `efb0bd1 Sharpen sheet overlay rendering`.
- Detailed handoff:
  `docs/10-performance-render/UNDERLAYMENT_CLARITY_HANDOFF_2026_05_28.md`.

## 2026-05-28 PDF Render Performance Status

- Verified the current post-fix render performance state without code changes.
- Confirmed from code and logs that persisted PyMuPDF preview cache hits are
  happening, but refresh/full-scale PyMuPDF renders still run afterward and are
  now the main visible lag source on Caretta repeat opens.
- Confirmed the remaining bottlenecks:
  - `WorkerSemaphore = new(1, 1)` still serializes all PyMuPDF worker traffic;
  - hidden-layer renders still reopen a fresh `fitz.Document`;
  - layer discovery fallback remains, but checked Caretta pages already have
    cached empty layer metadata, so it is not the main repeat-open cost there.
- Verification passed: `git diff --check`, conflict/TODO scan, and
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  (`0 warnings / 0 errors`).
- Detailed status:
  `docs/10-performance-render/PDF_RENDER_PERF_STATUS_2026_05_28.md`.

## 2026-05-28 PDF Inline Render Round Trip

- Added a distribution-safe inline PNG path for bounded PyMuPDF render images:
  C# requests `inline_image=true` with a `3,000,000` pixel cap, Python returns
  `image_base64` for small/medium renders, and C# decodes that before falling
  back to the old temp-file `page.png` path.
- Kept the old temp PNG path for large renders and unexpected helper behavior,
  avoiding named pipes, shared memory, new dependencies, or local machine
  assumptions.
- Added regression coverage for the portable inline protocol and fallback
  wiring.
- Verification passed:
  `.\Tools\python\python.exe -m py_compile .\Tools\pdf_layers_helper.py`,
  `git diff --check`, `dotnet build .\ourplanecore.sln`
  (`0 warnings / 0 errors`),
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build`
  (`242/242`), direct helper inline/fallback smoke, compressed single-file
  publish/deploy to
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`, SHA256
  `FDE0FCF63BF5C3B7555DE5C4DB9C0C0CC1190B1E7341BE11DED326E547A26EDE`,
  and packaged viewport smoke with `0` errors after the latest
  `Application startup.`.
- Git checkpoint before the change:
  `checkpoint/before-inline-png-render-20260528-175239`.
- Code commit: `a6d55e0 Inline bounded PDF render images`.
- Detailed handoff:
  `docs/10-performance-render/PDF_INLINE_RENDER_HANDOFF_2026_05_28.md`.

## 2026-05-28 Page Open UI Performance

- Added a git checkpoint before the risky page-open performance change:
  `checkpoint/before-page-open-ui-perf-20260528-173336` at `24ffaa7`.
- Reduced synchronous work in `LoadPageIntoViewport`: the method now reuses the
  `PageInfo` already loaded by `LoadPageFromTab` instead of re-reading
  `source.json`, then loads the viewport and applies takeoff/page visibility
  immediately.
- Deferred slower follow-up work to background dispatcher priority behind a
  stale-page guard: nearby preview prefetch, sheet overlay, page annotations,
  ruler/AI/3D overlays, Pages tree selection, settings save, takeoff visual
  refresh, floating page setup, and duplicate sheet measurement hints.
- Added regression coverage that locks down the immediate/deferred split and
  prevents the duplicate page metadata read from returning.
- Verification passed: `git diff --check`, `dotnet build .\ourplanecore.sln`
  (`0 warnings / 0 errors`),
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build`
  (`241/241`), compressed single-file publish/deploy to
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`, SHA256
  `F5C3FFCA2255AFA91982F751A644E09954EBC191281C1DFA538D36E9EF58F0A6`,
  and packaged launch log check with `0` errors after the latest
  `Application startup.`.
- Code commit: `e0b9539 Defer page open UI refresh work`.
- Detailed handoff:
  `docs/10-performance-render/PAGE_OPEN_UI_PERF_HANDOFF_2026_05_28.md`.

## 2026-05-28 PDF Preview Cache

- Added a persisted clean PyMuPDF preview cache for the first low-scale page
  image. The cache stores PNG plus metadata under
  `%LOCALAPPDATA%\OurPlaneCore\render-cache\pymupdf-preview`, keyed by source
  PDF path, modified time, length, page index, and preview scale.
- Wired `LoadPage` to apply a cached preview before queueing the normal
  PyMuPDF refresh render. The refresh render still runs, so layer state,
  discovery, snap reload, and normal quality rerender behavior remain
  unchanged.
- Kept Docnet out of the visible first-frame path; this preserves the Caretta
  green/purple/black artifact fix.
- Added regression coverage for cache round-trip/invalidation and source wiring
  that requires the cache to be applied before `QueueLayerRender(...)`.
- Verification passed: `git diff --check`, `dotnet build .\ourplanecore.sln`
  (`0 warnings / 0 errors`),
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build`
  (`240/240`), compressed single-file publish/deploy to
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`, SHA256
  `3A9BB509B994FA219D2F8F5C620149A148C6BB962AABC2EAE11F0B4CF7236EB1`,
  and packaged launch log check with `0` errors after the latest
  `Application startup.`.
- Code commit: `a6e00a4 Add persisted PDF preview cache`.
- Detailed handoff:
  `docs/10-performance-render/PDF_PREVIEW_CACHE_HANDOFF_2026_05_28.md`.

## 2026-05-28 Bookmarks Dock Return

- Fixed the left Pages panel Bookmarks dock control without adding a separate
  large button. The normal tab now reads `Bkm` and has a small circle toggle
  directly in the tab header.
- In docked mode, the docked Bookmarks header has the same small circle control;
  clicking it returns Bookmarks to the tab list. Returning from docked mode
  selects the `Bkm` tab so the user can see where it went.
- Hid the Bookmarks list column headers `Name`, `Page`, and `View` while keeping
  the row values.
- Kept the `BK` shortcut regression covered through the existing dual-layout
  shortcut path and expanded the source regression for the compact dock circle,
  no-large-button rule, tab reselection, hidden column headers, and status
  messages.
- Verification passed: `git diff --check`, `dotnet build .\ourplanecore.sln`
  (`0 warnings / 0 errors`),
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build`
  (`238/238`), compressed single-file publish/deploy to
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`, SHA256
  `60FF6304A298F73621C126265CA04C128CB8DE6413DE9B2B993C55B1C5284312`,
  and packaged launch log check with `0` errors after the latest
  `Application startup.`.
- Commits: `3f15285 Fix bookmarks dock return`,
  `922c474 Move bookmarks dock toggle into tab`.
- Detailed handoff:
  `docs/30-takeoffs-measurements/BOOKMARKS_DOCK_HANDOFF_2026_05_28.md`.

## 2026-05-27 Open Jobs, PDF Import, Render, and Sheet Naming

- Added Open Jobs folder removal near `Manage folders`; this removes a root
  folder from the saved list only and does not delete disk files.
- Changed new job creation to select the PDF folder first, then ask for the job
  name, and added recursive PDF discovery for both new jobs and
  `Import PDF Folder to Current Job...`.
- Fixed Caretta PDF rendering artifacts by making the first visible page render
  use clean PyMuPDF instead of Docnet. Docnet remains only as fallback, and
  queued Docnet renders are invalidated after a clean PyMuPDF frame applies.
- Kept page open responsive by rendering a clean low-scale PyMuPDF preview first
  (`0.35`) and then rerendering at normal quality.
- Improved sheet metadata extraction so prominent title-block sheet ids like
  `G001`, `AR001`, and `A451` win over Revit/file-path footer text such as
  `A26`; unknown pages no longer auto-rename to `-`.
- Verification passed:
  `.\Tools\python\python.exe -m py_compile .\Tools\pdf_layers_helper.py`,
  `dotnet build .\ourplanecore.sln` (`0 warnings / 0 errors`),
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build`
  (`235/235`), compressed single-file publish/deploy to
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`, SHA256
  `2C1DF76A22D41B9B60A4E58745D3AD36615E61EE019D2355728AD81F9DFB565A`, and
  packaged launch log check with `0` errors and `DocnetSlowCount=0`.
- Commits:
  `b0ad839 Add job folder removal`,
  `0678432 Import PDFs from job folders`,
  `8d60fc2 Fix PDF render and sheet naming`,
  `da31009 Use clean PDF preview on page open`.
- Detailed handoff:
  `docs/20-import-pages-metadata/OPEN_JOBS_PDF_IMPORT_RENDER_HANDOFF_2026_05_27.md`.

## 2026-05-24 Beam, Openings, and Current-Job PlanSwift Import

- Added the `Beam` takeoff workflow next to `J Area`. `B` starts Beam mode,
  the user measures two endpoints, the app leaves a dimension/Ruler-style
  annotation, opens Count creation with the measured/order size appended after
  a space, and places the first Count mark with a larger offset from the
  dimension label.
- Added `Openings` mode. `O` starts Openings, the user measures a rectangle,
  the app leaves width and height dimension annotations, opens Count creation
  with a size-only name like `3.0x4.2`, and places the first Count mark in the
  rectangle center.
- Added current-job PlanSwift import through
  `Open / Import -> Import PlanSwift to Current Job...`. This keeps the
  existing separate-job `PlanSwift` converter unchanged, but the new path
  imports PlanSwift pages into `Pages\01. planswift` and PlanSwift takeoffs
  into `Takeoffs\01. planswift` inside the currently open job.
- Added regression coverage for Beam rounding/name selection, Openings
  one-decimal size-only names, and current-job PlanSwift bucket placement.
- Detailed handoff:
  `docs/30-takeoffs-measurements/TAKEOFF_TOOLS_AND_PLANSWIFT_IMPORT_HANDOFF_2026_05_24.md`.
- Commits:
  `ff3e5c1 Add Beam count workflow`,
  `a9fe52e Add Openings count workflow`,
  `41cf7dc Add current job PlanSwift import`.
- Verification passed:
  `dotnet build .\ourplanecore.sln` with 0 warnings and 0 errors, and
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build` with
  `229/229` tests passed.
- Update package refreshed as a compressed single-file build at
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`, SHA256
  `ACAE2B528D60558AC5575655DEB200F8369AC47D93592B4AF0C80968FE478E01`.
  Desktop shortcut still targets the update package, and packaged-exe launch
  verification passed with `0` errors after the latest `Application startup`.

## 2026-05-15 Ruler, Notes, Takeoffs Bulk Controls, Search

- Changed Ruler hide-on-sheet to snapshot behavior. Pressing the Ruler dot now
  marks the currently visible ruler annotations on the active sheet as hidden;
  ruler annotations created afterward stay visible. Turning the dot off shows
  all ruler annotations again, and turning it on again hides the current set.
- Added multi-select/delete support for sheet markups in the viewport. Ruler
  and Note annotations can be selected with a box or Ctrl-add selection and
  deleted together with `Delete`.
- Added a Count default-shape control beside the Count tool. New Count takeoffs
  can start as circle, cross, or square; copied takeoffs keep their stored
  Count symbol because the property remains on the takeoff item and
  measurements.
- Changed new takeoff color fallback to generate vivid random colors, avoiding
  colors already used on the current sheet and avoiding black/gray. The manual
  preset color menu remains available.
- Made the right Takeoffs tree handle group deletion from keyboard `Delete`
  through preview key handling and a multi-selection fallback anchor.
- Added bulk hide/show actions for selected linked takeoffs in the left Pages
  tree page legend rows.
- Added search boxes above the Pages tree and Takeoffs tree. Pages search
  filters by sheet/folder name; Takeoffs search filters by takeoff/folder/row
  name and expands matching ancestors.
- Fixed the startup error caused by the new Takeoffs search wrapper. The
  estimating setup now moves the full `TakeoffsTreeHost` into the right-side
  Takeoffs tab instead of trying to re-parent the already-parented
  `TakeoffsTree`.
- Verification passed:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  with 0 warnings and 0 errors, and
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj /p:OutDir=.\cache\test_run\ /p:UseAppHost=false`
  with `194/194` tests passed.
- Update package workflow passed: `194/194` tests during packaging, Release
  publish to `publish\ourplanecore-working-single-20260515-0341`, copied to
  `C:\Users\User\Desktop\updates\OurPlaneCore`, Desktop shortcut retargeted to
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`, SHA256
  `6DD3494D331869A9BF80137CCFCAADDAC7EF9092B295199CAA825DA2AC56DCB4`.
- Manual launch check passed from the update package: PID `15064` stayed alive
  and responsive after startup, loaded the real job Takeoffs tree with 516
  item(s), and no new `Unhandled UI exception` was logged.

## 2026-05-14 Takeoffs Tree Large-Job Smoke Optimization

- Expanded the Takeoffs tree smoke into a large temporary job: 160 Pages,
  300 measured takeoffs, 120 bulk-copy takeoffs, and 3 measurements per
  generated takeoff.
- Found the mass-copy bottleneck in the left Pages linked-takeoff refresh, not
  in filesystem copy. Copying 120 nodes spent only about 300 ms in file work,
  while rebuilding linked rows for all 160 sheets cost about 6.7 seconds.
- Added deferred Pages linked-takeoff refresh for large copy/move updates.
  Big copy operations now update the right Takeoffs tree immediately and mark
  many touched Pages rows dirty; the current/opened sheet refreshes immediately,
  and other page-linked rows rebuild when the sheet is selected or expanded.
- Changed active-takeoff selection refresh to repaint touched Pages rows only.
  Selection no longer rebuilds page-linked child nodes because no measurement
  data changes on selection.
- Updated `RevealPagesForTakeoffItems(...)` so normal takeoff selection no
  longer expands every measured sheet for that takeoff. It still keeps linked
  rows and selection state correct, but avoids growing the visible Pages tree
  during ordinary clicks.
- Enabled the page-measurement lookup for large tree refresh paths and kept the
  broader disabled move/reorder fast refresh guarded off.
- Latest large smoke passed:
  `powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\ui_takeoffs_tree_drag_smoke.ps1 -TimeoutSeconds 240 -KeepAppOpen`.
  Result: 160 pages, 423 takeoff items, selection average 8.5 ms, max 38 ms,
  folder create 96 ms, bulk copy 120 nodes in 3647 ms, bulk Pages refresh
  58 ms, estimate refresh 121 ms.
- Verification passed: `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj`
  (`193/193`), `git diff --check` with only LF/CRLF warnings, no conflict/
  `NotImplementedException` scan hits, and
  `dotnet build .\ourplanecore.sln /p:RestoreIgnoreFailedSources=true /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  with 0 warnings and 0 errors.
- Release publish is staged at
  `publish\ourplanecore-working-single-20260514-2322`, SHA256
  `2A9D30222CC8FB64E16FF07FCF1C1047EF75D52C0049C0AA8EF213BFBA8BB123`.
  It was copied to `C:\Users\User\Desktop\updates\OurPlaneCore` after the
  running update exe closed, and the Desktop shortcut was retargeted to that
  update package.

## 2026-05-14 Takeoffs Tree Selection and Copy Performance

- Investigated the 2-3 second lag when clicking in the right Takeoffs tree and
  the left Pages linked-takeoff tree.
- Root cause: ordinary takeoff selection was doing data-refresh work even
  though no data changed. `TakeoffsTree_SelectedItemChanged(...)` rebuilt page
  takeoff indicators for every sheet through `RefreshPagesTakeoffIndicators()`,
  then `UpdateTotalDisplay()` rebuilt the full estimate table.
- Kept the previous risky fast tree refresh disabled:
  `FastTakeoffsTreeRefreshEnabled = false`. The safe copy/move/delete paths
  still use the proven full refresh or targeted post-data-change refreshes.
- Changed ordinary selection to refresh only page rows touched by the previous
  or newly selected takeoff via
  `RefreshPageTakeoffIndicatorsForActiveChange(...)`.
- Changed `UpdateTotalDisplay(...)` so selection-only updates can skip
  `RefreshEstimateTable()`; estimate rows still refresh on measurement,
  property, page, and takeoff data changes.
- Follow-up root cause for the user's mass-copy/folder-create complaint:
  takeoff copy/paste and duplicate were still calling `LoadTakeoffsForJob()`
  after storage copy, forcing a full right-tree rebuild and full post-load
  totals/Pages refresh.
- Added a safer incremental copy refresh:
  `TryApplyTakeoffStructureCopyFast(...)` reads only the copied folder/item
  subtrees, appends those new UI nodes, registers the path index, updates
  `_takeoffItems`, then refreshes only the affected Pages rows plus sheet
  legend and estimate data. The storage copy path itself is unchanged.
- Added regression coverage:
  `takeoff tree regression selection uses targeted ui refresh` and
  `takeoff tree regression copy uses incremental tree refresh`.
- Expanded the app-side smoke to cover move in/out, folder create, and bulk
  copy. The smoke opens a temporary job in the app and verified creating a
  folder in 26 ms and copying 60 takeoff nodes into a folder in 890 ms.
- Verification passed:
  `dotnet build .\ourplanecore.sln` (0 warnings, 0 errors),
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj` (`193/193`),
  `.\run-takeoffs-tree-smoke.cmd -TimeoutSeconds 60`, and the update-package
  workflow (`193/193` during package build).
- Latest package: Release publish to
  `publish\ourplanecore-working-single-20260514-2215`, update-folder
  replacement at `C:\Users\User\Desktop\updates\OurPlaneCore`, final Desktop
  shortcut retarget to the update package, and SHA256
  `4A553A76684B7759F0A664CED1CDB970F234145110C11D17A0FE89121424250D`.

## 2026-05-14 Copy and Duplicate Keep Visible Names

- Changed the shared node-copy storage path so copy/paste and duplicate keep
  the exact visible page/takeoff name. The app may still create a unique hidden
  folder path on disk, but `Data.xml`, `PageInfo.Name`, and `TakeoffItem.Name`
  stay one-for-one with the original name.
- Removed the old `- Copy` / `- Copy 2` display-name generation from
  `Models/Storage/NodeStore.cs`.
- The fix applies to Pages copy/paste, Page duplicate, Takeoffs copy/paste,
  Takeoffs duplicate, and drag/drop copy paths that use the shared
  `CopyNode(...)` route.
- Extended regression coverage so repeated copies of the same Page or Takeoff
  use different hidden folders but still display the same exact name in the
  program, with no `Copy`, no `(2)`, and no visible suffix.
- Cleaned the currently active job
  `C:\Users\User\Desktop\Takeof_desctop\76. NP United residences_Bliffert`:
  removed existing visible `Copy` suffixes from 25 live `Data.xml` records
  under `Pages` and `Takeoffs` (8 Pages, 17 Takeoffs), without renaming hidden
  folders. Backup of the changed files:
  `C:\tmp\ourplanecore-visible-name-cleanup-20260514-184934`.
- Fixed the remaining measurement-paste path: choosing `No = create new copied
  takeoff items` now preserves the source takeoff name instead of appending
  `Copy`.
- After the follow-up repro, cleaned 9 newly created visible `Copy` suffixes
  from live Takeoffs in the active job. Backup:
  `C:\tmp\ourplanecore-visible-name-cleanup-20260514-185647`.
- Verification passed:
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj` (`191/191`), then
  update-package workflow with build 0 warnings / 0 errors, `190/190` tests,
  Release publish to `publish\ourplanecore-working-single-20260514-1850`,
  update-folder replacement at `C:\Users\User\Desktop\updates\OurPlaneCore`,
  final Desktop shortcut retarget to the update package, and SHA256
  `1CC8ED751F3FA7BC4900BF2112EC5A593EF9110D72115103012A619D83564D4F`.
- Latest package after the measurement-paste fix: build 0 warnings / 0 errors,
  tests `191/191`, Release publish to
  `publish\ourplanecore-working-single-20260514-1857`, update-folder
  replacement at `C:\Users\User\Desktop\updates\OurPlaneCore`, final Desktop
  shortcut retarget to the update package, and SHA256
  `539BA9283E16834CA26BB0FD6562E67958BDA6D03578E9EBD8F4A237F952FFF2`.

## 2026-05-14 Ruler Visibility and Count Display Symbols

- Added a sheet-level Ruler visibility dot that behaves like the page-linked
  takeoff visibility dot: filled means visible on the current sheet, empty
  means all Ruler markups on that sheet are hidden.
- The Ruler visibility state is persisted in page metadata and is respected by
  viewport rendering, selection/editing, and PDF export.
- Added Count display symbols: Circle, Cross, and Square. The canonical values
  live in `Models/CountDisplaySymbol.cs`, with persistence on both
  `TakeoffItem.CountSymbol` and `Measurement.CountSymbol`.
- Count display can be changed from the right Takeoffs tree item menu,
  Takeoffs section/count row menu, left Pages linked-takeoff row menu, and the
  viewport measurement context menu. Multi-selected Count rows or canvas
  measurements are updated together.
- The chosen Count symbol is rendered consistently in the viewport, right
  Takeoffs tree, left Pages linked rows, sheet legend overlay, PDF legend, and
  exported PDF measurement marks.
- Added storage regression coverage:
  `count display symbol persists on takeoff and measurements`.
- The Desktop shortcut now targets the packaged update build at
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`, with working
  directory `C:\Users\User\Desktop\updates\OurPlaneCore`.
- Detailed handoff:
  `docs/30-takeoffs-measurements/RULER_AND_COUNT_DISPLAY_HANDOFF_2026_05_14.md`.
- Verification and package refresh passed:
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj` (`190/190`), then
  the update-package workflow with `dotnet build .\ourplanecore.sln`
  (0 warnings, 0 errors), `190/190` tests, Release publish to
  `publish\ourplanecore-working-single-20260514-1539`, update-folder
  replacement at `C:\Users\User\Desktop\updates\OurPlaneCore`, final Desktop
  shortcut retarget to the update package, and SHA256
  `1E738BC6F87009A2749475E1F62B882B55DAC6D6C902D80FFAB9D9A6B5A368DE`.

## 2026-05-13 Takeoffs Tree Stale RF UI Fix

- Investigated RF takeoffs that appeared outside folders in the open UI even though they had already moved on disk under `Takeoffs\sqfts`.
- Root cause: stale in-memory Takeoffs tree rows pointed at old folder paths that no longer existed, so drag/drop built invalid payloads and appeared to do nothing.
- Added stale row detection/reload in `MainWindow.TakeoffsSelectionHelpers.cs` and wired it into Takeoffs mouse-down and drag-start paths.
- Hardened Takeoffs drag cleanup so aborted drags reset state and clear drop cues.
- Added regression checks for stale-row reload and drag-state reset behavior.
- Detailed handoff: `docs/30-takeoffs-measurements/TAKEOFF_TREE_STALE_RF_UI_FIX_2026_05_13.md`.
- Verification passed: isolated build/test (`185/185`), then normal Debug build/test (`185/185`) after closing the running app.

## 2026-05-13 New Job Flow and Crop Note Output Fix

- Changed `New Job` so it uses the existing job-folder context before asking
  for a parent folder. It now checks the selected Job Picker root, selected or
  recent job parent, current job parent, and saved job roots.
- After a new job is created and opened, the app immediately opens the existing
  `Import PDF(s)` picker so the user can load PDFs without a second manual
  command.
- Diagnosed `AI crop here -> note` responses with placeholder text. The crop
  images were saved correctly, but the raw OpenAI response was `incomplete`
  because `max_output_tokens` was consumed by reasoning before any
  `output_text` was produced.
- Updated `OpenAiRequestRunner` to send low reasoning effort for reasoning
  models, give crop-note requests a larger output budget, and treat
  incomplete/no-text Responses API results as failed instead of saving fake
  `done` responses.
- Added `OpenAiResponseParser` plus regression coverage for normal
  `output_text` extraction and `max_output_tokens` incomplete responses.
- Detailed handoff:
  `docs/20-import-pages-metadata/JOB_CREATION_AND_CROP_NOTE_HANDOFF_2026_05_13.md`.
- Verification:
  - `dotnet build .\ourplanecore.sln` passed with 0 warnings and 0 errors;
  - `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj` passed
    `182/182`.

## 2026-05-07 Report Builder Template View

- Kept the colored takeoff glyph icons in the Takeoffs tree, viewport legend,
  and PDF legend. Removed only the old text-style type prefixes from display
  labels, so rows now say `Line`, `Area`, `Count`, or `Joist` without extra
  square/diagonal text markers.
- Removed the temporary `Excel Blocks` workflow. It was the wrong direction for
  this task because it created takeoff items instead of building the editable
  report surface.
- Added a separate `4 Report Builder` workspace tab. It is independent from all
  export commands; CSV/TXT/Excel/PDF export behavior is unchanged.
- `Report Builder` reads
  `Desktop\03_Excel_Templates_Macros\Templates\TemplateCom.xlsm`, sheet
  `Detailed Frame List`, directly from the workbook package even when Excel has
  the file open.
- Added `ReportTemplateService`:
  - shows the Excel-like report table with columns `A-H` and `J-L`;
  - uses the template's column widths as the first sizing pass;
  - loads rows from the template so rows `1-10` form the initial header area;
  - highlights header/table-header/section/yellow input-block rows separately;
  - keeps cells editable in the app so mapping rules can be added gradually.
- Added regression tests for loading a synthetic Detailed Frame List workbook
  and for reading the local `TemplateCom.xlsm` when it exists.
- Traced the real Excel `A3_Walls_Calc_AllGroup` macro on a copied
  `TemplateCom.xlsm` using the sample wall source:
  `1 / corners 22 EA / ext 2x6 9.00 207.38 FT / corr 2x6 9.00 212.14 FT /
  dem 2x6 9.00 168.43 FT`.
- Recreated the first A3 wall block in `Report Builder`:
  - source rows are selected in `J:K` just like the macro selection;
  - `Apply Walls` writes the same target block values observed from Excel:
    `Q38=22`, `O40=9,00`, `P40=207,38`, `T40=212,14`, `W40=168,43`;
  - the table now shows through `AB` so the wall output columns are visible.
- Added a regression test that applies the A3 wall block rule and asserts those
  exact target cells.
- Verification:
  - `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
    passed with 0 warnings and 0 errors;
  - `dotnet .\Tests\cache\verify_build\OurPlaneCore.Tests.dll` passed `99/99`;
  - `git diff --check` exited with code 0, with only Git LF-to-CRLF warnings.
- Published and launched the working desktop shortcut build from
  `publish\ourplanecore-working-single-20260507-1530\ourplanecore.exe`.

## 2026-05-07 Record Mode, Live Dimensions, and Joist Area Shortcut

- Hardened Record mode so the active takeoff target stays locked while Record
  is on. Accidental clicks on another takeoff, section, page-linked takeoff, or
  estimating row no longer switch the recording target; the status text tells
  the user to stop Record first.
- Added viewport cursor guidance: while the pointer is over a rendered sheet,
  the canvas draws very faint horizontal/vertical guide rays through the cursor.
- Expanded takeoff color presets and changed automatic new-takeoff color
  selection to prefer the least-used visible color, avoiding immediate reuse of
  the active color when possible.
- Added faint live dimension text while drawing:
  - Line and Area Record show per-segment ft labels and a live total while the
    rubber-band point is moving;
  - Ruler now shows the current endpoint-to-endpoint distance on the temporary
    ruler line before the second click.
- Added a toolbar `J Area` button beside `Area`.
  - `J Area` creates a new Area takeoff item with joist layout enabled, then
    starts Area Record immediately.
  - Default joist settings for this quick path are `Round Up Foot` and
    `Detailed area label` off.
  - Hotkey `J` invokes the same J Area tool from the viewport; Command Palette
    also lists `J Area Tool`.
- Preserved joist Area behavior through measurement copy/paste:
  - copying an Area with joists now carries both the measurement's joist state
    and the source takeoff item's joist settings;
  - pasting into `new takeoff item(s)` creates a new Area item with the same
    joist settings instead of downgrading the pasted shape to a plain Area.
- Fixed top-menu TXT export so it exports the full job takeoff root instead of
  only the currently selected/first tree item. Excel export still uses the
  existing selected-root behavior; no segment/edge details were added to
  CSV/TXT/XLSX export.
- Added `Export to Current Excel`:
  - the Takeoffs export menu and Takeoff Manager toolbar can write the selected
    takeoff folder/item directly into an already open Excel workbook;
  - the command uses Excel's active workbook and active cell as the insertion
    point, writes `Name | Value | Unit` rows downward from that cell, formats
    group headers bold, and does not auto-save the workbook;
  - it requires an existing Excel instance and reports a clear status if Excel,
    a workbook, or an active worksheet cell is not available.
- Published the working desktop shortcut build to:
  `publish\ourplanecore-working-single-20260507-1530\ourplanecore.exe`.
- Verification:
  - `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
    passed with 0 warnings and 0 errors;
  - `dotnet test .\Tests\OurPlaneCore.Tests.csproj --no-restore` exited with
    code 0;
  - `git diff --check` exited with code 0, with only Git LF-to-CRLF warnings.

## 2026-05-07 Viewport Paper Rendering, Export Paper Color, and PDF Snap

- Hardened sheet opening/switching so the viewport does not flash or remain
  transparent while a page render is pending:
  - `9707cb3` hardened the viewport page background path;
  - `878a709` stabilized page paper rendering;
  - the viewport now keeps an opaque paper underlay visible during blank,
    loading, and page-switch states.
- Added a real page/background display control block:
  - `214a657` added display controls for the area around the sheet and for the
    sheet paper/background itself;
  - paper presets include white plus darker gray/black options for eye comfort;
  - this affects viewport comfort only, not takeoff geometry.
- Added regression coverage for sheet opening and navigation:
  - `d5b5d32` added a viewport page stress smoke runner that opens project
    sheets, revisits sheets, and opens sheets in additional tabs;
  - real-job smoke was run against
    `71. Mallory View_Rid` / `Pages\00. imported\Struct\A35`.
- Forced exported PDFs to use white paper:
  - `7e6fb4a` makes PDF export render with paper white regardless of the
    user's viewport paper/background comfort color;
  - export remains suitable for client/print output even if the app viewport is
    set to gray/black paper.
- Added a separate `PDF Snap` mode:
  - `12e331f` added the separate toolbar toggle, Command Palette command, and
    `Ctrl+F3` hotkey;
  - the normal `Snap` mode still snaps to existing takeoff/markup geometry;
  - `PDF Snap` is independent and can be turned on/off without changing the
    selected takeoff item or the regular snap toggle.
- Extended `PDF Snap` to sheet overlay PDFs:
  - `5bb75df` passes the sheet overlay PDF source into the viewport snap cache;
  - overlay PDF snap points are transformed through the saved overlay
    offset/scale so they land in the active sheet coordinate system;
  - snap labels distinguish `overlay corner` / `overlay point`.
- Extended `PDF Snap` from point-only behavior to line geometry:
  - `daa19ca` makes `Tools/pdf_layers_helper.py` return PDF vector segments in
    addition to endpoints/corners;
  - `Models/PdfGeometrySnapService.cs` now indexes both snap points and snap
    segments;
  - hovering near the middle of a vector PDF line snaps to the closest point on
    that line, with labels such as `pdf line` or `overlay line`;
  - point/corner priority still wins when the cursor is on an actual PDF
    endpoint/corner.
- Current limitation:
  - this is vector PDF snap, not raster/image snap;
  - if a sheet or overlay is a scanned bitmap inside a PDF, there may be no PDF
    line/corner objects to snap to;
  - raster/scan support would need a separate `Image Snap` mode based on pixel
    edge/intersection detection, with its own performance controls.
- Verification:
  - `python -m py_compile Tools\pdf_layers_helper.py` passed;
  - `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
    passed with 0 warnings and 0 errors;
  - `dotnet .\Tests\cache\verify_build\OurPlaneCore.Tests.dll` passed 92/92;
  - `git diff --check` passed with only Git LF-to-CRLF warnings;
  - helper checks on real `A35` returned vector snap points and segments for
    both the active sheet and its overlay;
  - `run-viewport-zoom-smoke.cmd` passed on a copied Mallory `A35` sheet with
    repeated zoom in/out and middle-button pan cycles.
- Rollback:
  - export paper only: `git revert 7e6fb4a`;
  - page stress smoke only: `git revert d5b5d32`;
  - page/background controls only: `git revert 214a657`;
  - page paper rendering hardening: `git revert 878a709 9707cb3`;
  - PDF Snap base mode: `git revert 12e331f`;
  - PDF Snap overlay support: `git revert 5bb75df`;
  - PDF Snap line-geometry support: `git revert daa19ca`;
  - full block: `git revert daa19ca 5bb75df 12e331f 7e6fb4a 878a709 214a657 d5b5d32 9707cb3`.

## 2026-05-06 Viewport Performance and Sheet Visibility Toggles

- Recent viewport performance commits:
  - `cf3cca3` improved zoom responsiveness while panning/zooming real PDF
    pages;
  - `10fbd3a` capped high-zoom render detail so very close zoom does not keep
    requesting unnecessarily dense page rasters;
  - `e401f49` added fast navigation behavior that skips expensive drawing
    paths while the user is actively zooming or panning;
  - `d7f6d45` added cached page renders so opening/switching sheets can reuse
    existing preview/full render work instead of starting blank every time;
  - `bd746bb` added sheet/takeoff visibility toggles and finalized the latest
    user-facing visibility behavior;
  - `fe17b74` fixed the follow-up label regression: line/area/joist summary
    labels no longer disappear because of zoom distance or fast-frame state,
    disabled fast pan/zoom now really keeps full frames, sheet legend/header
    overlays no longer blink off during navigation, and overlay rows now have
    the same show/hide dot as sheet-linked takeoffs.
- The active PDF viewport no longer shows a transparent empty sheet while a new
  page render is pending. It keeps a cached or instant low-detail preview ready
  first, and when it must keep the previous page for a moment it draws a subtle
  white veil instead of mixing old takeoffs with the next sheet.
- Sheet overlay behavior now has a saved on/off state:
  - each page `source.json` can persist `overlay_visible`;
  - overlay rows in the expanded Pages tree show hidden state and expose
    `Hide Overlay` / `Show Overlay` from the context menu;
  - hidden overlays are not drawn in the viewport and are skipped by PDF export;
  - overlay bitmap cache access is lock-protected because viewport overlay
    loading can render in the background.
- Sheet-linked takeoffs in the expanded Pages tree now have a small visibility
  dot to the left of the row:
  - filled dot means the takeoff is visible on that sheet;
  - empty dot means the takeoff is hidden on that sheet;
  - clicking the dot toggles only that sheet, not the real takeoff item;
  - the context menu also exposes `Hide on This Sheet` / `Show on This Sheet`.
- Hidden takeoffs are persisted per page as `hidden_takeoffs` in `source.json`.
  The viewport, sheet legend, and PDF export all respect the hidden list, and
  the viewport clears or updates canvas selection when a selected takeoff is
  hidden.
- Tests were extended so page source rewrites preserve overlay visibility and
  hidden takeoff lists across scale/overlay/layer/source mutations.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.
- Regression runner:
  `dotnet .\Tests\cache\verify_build\OurPlaneCore.Tests.dll` passed 85/85.
- Whitespace/conflict check:
  `git diff --check` passed with only Git LF-to-CRLF warnings.
- Real-job smoke:
  `run-viewport-zoom-smoke.cmd` passed on a copied Mallory `A35` sheet with
  repeated zoom in/out and middle-button pan cycles; UI stayed responsive.
- Rollback for only the latest visibility toggle block:
  `git revert bd746bb`
- Rollback for the label regression follow-up:
  `git revert fe17b74`
- Rollback for the full recent viewport performance and visibility block:
  `git revert fe17b74 bd746bb d7f6d45 e401f49 10fbd3a cf3cca3`

## 2026-05-06 Takeoffs Tree Refactor Block

- Completed a no-behavior split of the oversized takeoffs workflow into focused
  `MainWindow.Takeoffs*.cs` partial owners. `MainWindow.TakeoffsTree.cs` is now
  a 329-line shell for selection, context-menu opening, mouse selection, and
  drag arming.
- New and existing takeoffs ownership after this block:
  - `MainWindow.TakeoffsExport.cs`: takeoff CSV/TXT/XLSX export;
  - `MainWindow.TakeoffsCreation.cs`: new item/folder and auto-create flows;
  - `MainWindow.TakeoffsPersistence.cs`: save and observation actions;
  - `MainWindow.TakeoffsActiveTarget.cs`: active target panel and commands;
  - `MainWindow.TakeoffSections.cs`: section rows, menus, and ordering;
  - `MainWindow.TakeoffsJoists.cs`: joist direction capture;
  - `MainWindow.TakeoffsProperties.cs` and
    `MainWindow.TakeoffsBulkProperties.cs`: item/folder properties dialogs;
  - `MainWindow.TakeoffsMenus.cs`: context-menu builders;
  - `MainWindow.TakeoffsNodeActions.cs`: rename/delete/move/sort node actions;
  - `MainWindow.TakeoffsSelectionHelpers.cs`: takeoff and section multi-select
    helpers;
  - `MainWindow.TakeoffsClipboard.cs`: keyboard shortcuts and copy/cut/paste;
  - `MainWindow.TakeoffsDragDrop.cs`: node reorder, section drag/drop, and drop
    cue status.
- Commits in this block:
  `c4d3d39`, `48c3880`, `d11eb6c`, `45f7451`, `adc741a`, `65cd520`,
  `255bfef`, `6bdad29`, `7b8edff`, `0b6b666`, `ffe3a38`, `38fab8c`,
  `a9cd0aa`.
- Rollback for the code refactor block:
  `git revert a9cd0aa 38fab8c ffe3a38 0b6b666 7b8edff 6bdad29 255bfef 65cd520 adc741a 45f7451 d11eb6c 48c3880 c4d3d39`
- Verification after each code slice:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors,
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj` passed 77/77, and
  `git diff --check` passed with only Git's LF-to-CRLF warning.

## 2026-05-06 Overlay Alignment and Sheet-Linked Tree Fixes

- Added sheet-to-sheet overlay support for active pages:
  - page metadata now stores overlay sheet folder, color, opacity, X/Y offset,
    and scale;
  - the PDF viewport renders the overlay underneath takeoffs and markups;
  - PDF export includes the same overlay transform so exported sheets match the
    viewport.
- Added overlay transform controls:
  - right-click overlay rows expose move, scale, reset, color, clear, and
    numeric transform editing;
  - `Edit Overlay by Points` starts a viewport alignment workflow where the
    first point pair moves the overlay and the second point pair scales it
    around the first matched point.
- Updated the left Pages tree behavior:
  - overlay rows now stay below the sheet-linked takeoff rows instead of first;
  - linked takeoff rows are deduped by takeoff folder path;
  - selecting a linked takeoff row no longer starts a recursive selection sync
    between the Pages tree and Takeoffs tree.
- Added joist-area refinements:
  - roof pitch such as `3:12` applies slope length;
  - area cut holes subtract from joist layout;
  - detailed vs standard joist label format is persisted per takeoff.
- Verification:
  `dotnet build .\ourplanecore.sln` passed with 0 warnings and 0 errors.
- Regression runner:
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build` passed
  64/64.

## 2026-05-05 Roadmap Implementation Slice

- Follow-up slice after commit `ee7d8ff`:
  - Snap v2 now includes intersection snap candidates across existing
    measurements, current in-progress geometry, and page markups. Intersection
    snaps use a distinct `int x,y` canvas preview.
  - Page markups (`Ruler`, draw line, arrow, and box) are selectable in the
    `Select` tool. Users can drag the markup body to move it, drag blue handles
    to reshape endpoints/corners, press `Delete`, or right-click and delete the
    markup. Edits persist through `annotations.json`.
  - Selected measurements and page markups now show a subtle orange transform
    area behind the blue selected bounds. The orange area has live corner scale
    handles and a top rotate handle for direct canvas editing.
  - The main tool strip now docks at the bottom, with selection edit controls
    grouped there: horizontal mirror, vertical mirror, rotate slider, and scale
    slider. The transform controls enable only when a canvas selection exists.
  - Verification passed again:
    `dotnet build .\ourplanecore.sln` with 0 warnings/errors, and
    `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build --no-restore`
    passed 30/30.

- Added a lightweight no-NuGet regression runner under `Tests/` and wired it
  into `ourplanecore.sln`. The runner currently covers 20 fast checks across
  measurements, takeoff totals, sheet metadata, app settings/recent jobs, and
  the new job recovery service.
- Added `Models/JobRecoveryService.cs` plus `MainWindow.JobRecovery.cs`:
  - each opened job gets a `.~lock` marker;
  - stale lock markers trigger a recovery prompt on next open;
  - manual saves and job switches create metadata snapshots under
    `.snapshots/`;
  - snapshots copy `Data.xml`, Pages metadata, Takeoffs metadata, and
    `measurements.json`, while skipping source PDFs, rendered images, AI crop
    images, and build output;
  - old snapshots are pruned to a bounded history.
- Hardened Estimating for larger jobs: the embedded estimate list now uses WPF
  virtualization/recycling and has a sticky summary footer showing visible
  item count, section/count row count, and visible cost total when priced rows
  are present.
- Added the next Snap v2 slice in `Controls/PdfViewport.Tools.cs` and
  `Controls/PdfViewport.MeasurementRendering.cs`: snap tolerance remains in
  screen pixels, midpoint candidates are included alongside endpoints, endpoint
  and midpoint snap glyphs differ visually, and the canvas shows a compact
  `end/mid x,y` coordinate label while snapping.
- Fed reviewed opening projection feedback into future Auto Roof prompts. The
  roof recognition request now includes recent accepted/rejected
  `3d_opening_projection_review` records so future detection can learn from the
  user's opening review outcomes.
- Tightened secondary Count wording: count commands now say `count marks`, and
  Line/Area geometry status/tooltips use `vertices` instead of ambiguous
  `point(s)` where the text is not referring to PDF coordinates.
- Added Joist Area roof pitch support for sloped length takeoff:
  - Joist Properties now include `Pitch (rise:run)` with examples like `3:12`;
  - accepted inputs include `3:12`, `3/12`, `3 in 12`, and a single rise value
    such as `3` as shorthand for `3:12`;
  - blank or `0:12` stays flat;
  - generated joist length is multiplied by the slope factor before per-joist
    order-length rounding, so totals, labels, estimating, PDF export, and
    PlanSwift export use sloped length.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\\cache\\verify_build\\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.
- Regression runner:
  `dotnet .\Tests\cache\verify_build\OurPlaneCore.Tests.dll` passed 30/30.

## 2026-05-05 Architecture Audit and Refactor Plan

- Created and validated local Codex skills under `C:\Users\User\.codex\skills`
  for the major recurring workstreams in this repo: refactor, bugcheck, PDF
  layers/trace/export, Pages/Takeoffs trees, measurements, AI/massing, docs
  handoffs, Bluebeam-style UX, preview UI mockups, PlanSwift spec mapping, sheet
  metadata, and parallel-agent coordination.
- Updated `AGENTS.md` with explicit `$ourplanecore-*` skill routing so future
  agents can load the right reusable workflow before editing.
- Added development guardrails to `AGENTS.md`: new C# file size limits,
  `MainWindow.xaml.cs` and `MainWindow.*.cs` growth limits, XAML size targets,
  method-size limits, and rules for choosing focused services/controls/partials
  instead of quick patches that grow oversized files.
- Audited the current WPF app structure after the recent PDF layer, Layer Trace,
  tree-state, item-creation, export, AI, and 3D work.
- Recorded the main architecture risks in
  `docs/ARCHITECTURE_AUDIT_AND_REFACTOR_PLAN_2026_05_05.md`:
  - `MainWindow.xaml.cs` started at 16,943 lines and is the primary god-object
    risk;
  - `Controls/PdfViewport.cs` is 3,942 lines and mixes rendering, input,
    drawing, selection, PDF layers, trace, overlays, and status;
  - active takeoff state, tree expansion state, and PDF layer state have too
    many implicit owners;
  - the safe refactor path is staged partial splits, smoke tests, then small
    state/controller extractions.
- Applied the first no-behavior split:
  - moved PDF Layers and Layer Trace UI handlers from `MainWindow.xaml.cs` into
    new partial file `MainWindow.PdfLayers.cs`.
- Continued the staged refactor after a git checkpoint:
  - changed `Controls/PdfViewport.cs` to a partial class and moved Layer Trace
    session/probe/trace/overlay code into `Controls/PdfViewport.LayerTrace.cs`;
  - `Controls/PdfViewport.cs` is now 3,581 lines, and
    `Controls/PdfViewport.LayerTrace.cs` is 370 lines;
  - added `Models/TakeoffCreationPolicy.cs` so new item vs new folder placement
    is explicit and not hidden inside tree-selection UI code.
- Split display and overlay settings into `MainWindow.DisplaySettings.cs`.
- Split measurement/page-annotation callbacks into
  `MainWindow.MeasurementCallbacks.cs`.
- Split viewport measurement/AI context-menu builders into
  `MainWindow.ViewportContextMenu.cs`.
- Split PDF export workflow and writer loop into `MainWindow.PdfExport.cs`.
- Moved PDF export drawing helpers into the same `MainWindow.PdfExport.cs`
  partial file.
- Corrected current post-split line counts with `rg -n "^"` because the earlier
  quick count under-reported physical file lines. Current counts are tracked in
  `docs/ARCHITECTURE_AUDIT_AND_REFACTOR_PLAN_2026_05_05.md`; the main window is
  now 17,554 lines, and `MainWindow.PdfExport.cs` is 735 lines.
- Split workspace manager callbacks for Sheet Manager, Takeoff Manager, AI
  Manager, and 3D Manager into `MainWindow.WorkspaceManagers.cs`.
  `MainWindow.xaml.cs` is now 17,271 lines, and the new partial file is
  299 lines.
- Split estimating setup, estimating window callbacks, estimate selection sync,
  and section property dialogs into `MainWindow.Estimating.cs`.
  `MainWindow.xaml.cs` is now 16,456 lines, and the new partial file is
  834 lines.
- Split 3D Massing right-panel construction into `MainWindow.MassingPanel.cs`.
  `MainWindow.xaml.cs` is now 16,161 lines, and the new partial file is
  308 lines.
- Split Pages tree workflow into `MainWindow.PagesTree.cs`, including page
  tabs, page takeoff legend ordering, page move/copy/sort operations, and PDF
  metadata automation. `MainWindow.xaml.cs` is now 12,483 lines, and the new
  partial file is 3,705 lines.
- Split Takeoffs tree workflow into `MainWindow.TakeoffsTree.cs`, including
  item/folder creation, takeoff export, active target controls, section rows,
  properties dialogs, drag/drop, copy/paste, and multi-select.
  `MainWindow.xaml.cs` is now 8,936 lines, and the new partial file is
  3,569 lines.
- Split shared tree, legend, totals, estimating-row, quantity, and
  takeoff-default helpers into `MainWindow.TreeHelpers.cs`.
  `MainWindow.xaml.cs` is now 7,614 lines, and the new partial file is
  1,338 lines.
- Split measurement copy/paste, paste-target resolution, and takeoff autosave
  helpers into `MainWindow.MeasurementClipboard.cs`.
  `MainWindow.xaml.cs` is now 7,268 lines, and the new partial file is
  361 lines.
- Split viewport scale/tool/context callbacks, AI crop/marker save helpers,
  marker overlay refresh, and context suggestion handlers into
  `MainWindow.ViewportCallbacks.cs`. `MainWindow.xaml.cs` is now 6,717 lines,
  and the new partial file is 570 lines.
- Split persisted app settings, theme/background application, side-panel width
  persistence, scale UI, and small input dialogs into `MainWindow.Utilities.cs`.
  `MainWindow.xaml.cs` is now 6,351 lines, and the new partial file is
  381 lines.
- Split AI Inbox display, inbox context menu, crop bookmark creation/runs,
  marker filters, and marker-set entry points into `MainWindow.AiInbox.cs`.
  `MainWindow.xaml.cs` is now 5,486 lines, and the new partial file is
  886 lines.
- Split 3D Massing build/review workflow, roof/opening review, 3D viewport
  preview, marker rows, and massing preview drawing into
  `MainWindow.MassingWorkflow.cs`. `MainWindow.xaml.cs` is now 3,589 lines,
  and the new partial file is 1,920 lines.
- Split AI marker-set management, marker editing, observation actions, AI
  request execution, response/action-draft viewing, and action application into
  `MainWindow.AiActions.cs`. `MainWindow.xaml.cs` is now 1,376 lines, and the
  new partial file is 2,234 lines.
- Split nested clipboard, page-tab, tree-node, display-row, and 3D support
  types into `MainWindow.SupportTypes.cs`. `MainWindow.xaml.cs` is now
  1,202 lines, and the new partial file is 194 lines.
- Split toolbar setup, marker filter initialization, open/new job workflows,
  persisted marker visibility, takeoff loading, measurement-link repair, and
  PDF import into `MainWindow.JobLifecycle.cs`. `MainWindow.xaml.cs` is now
  679 lines, and the new partial file is 543 lines.
- Split drawing tool controls, record toggle, snap/ortho state, drawing-target
  confirmation, viewport zoom/scale buttons, and scale presets into
  `MainWindow.ToolControls.cs`. `MainWindow.xaml.cs` is now 356 lines, and the
  new partial file is 338 lines.
- Fixed Layer Trace probing for PDF layers whose PyMuPDF UI layer number is
  `0`. The probe path now keeps named layer candidates instead of dropping
  `layer == 0`, and the C# DTO path accepts named candidates with zero-valued
  layer numbers.
- Reworked Layer Trace interaction into a temporary focus mode:
  - enabling Layer Trace ghosts the current PDF page without changing the
    user's real layer checkbox states;
  - moving the cursor probes PDF layer geometry in the background, highlights
    the current hit candidate, and allows Tab cycling when multiple candidates
    overlap;
  - clicking or pressing Enter locks the current candidate and temporarily
    renders only that selected PDF layer until the trace is committed or
    cancelled;
  - Esc unlocks the current trace selection first, then exits Layer Trace on
    the next press.
- Added `Tools/pdf_layer_trace_smoke.py`, which creates a tiny synthetic
  layered PDF and verifies helper contracts for layer render toggling,
  `layerprobe`, and `layertrace` full/edge/all-edges/point modes.
- Split `Controls/PdfViewport.cs` into focused partial files for layer
  rendering, paint orchestration, sheet overlays, measurement/annotation/AI
  rendering, mouse/keyboard input, drawing tools, selection/editing, geometry,
  and view-transform helpers. `Controls/PdfViewport.cs` is now 811 lines; the
  largest extracted viewport partial is
  `Controls/PdfViewport.MeasurementRendering.cs` at 825 lines.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.

## 2026-05-04 PDF Export Dialog Button Fix

- Fixed the PDF export dialog selection handling:
  - selected sheet paths are now normalized before comparison, so the dialog
    does not accidentally open with no selected rows when the same folder path
    has a different string form;
  - export rows now notify when their checkbox changes;
  - the dialog commits the active DataGrid checkbox edit before the `Export`
    button checks selected rows.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.

## 2026-05-04 PDF Export Legend and Label Rules

- Updated PDF export overlay behavior:
  - exported sheet legend now uses the configured sheet legend size multiplied
    by `2x`;
  - export-only measurement labels are now drawn for Line and Area
    measurements;
  - Count measurements still export their count marks but do not draw count
    labels;
  - export labels use the page/measurement scale fallback and the selected
    export unit mode.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.

## 2026-05-04 Remove Excess Bold Text

- Removed bold/semibold typography from the app shell and manager tables so the
  interface does not feel visually heavy:
  - `DataGridColumnHeader` now uses normal text;
  - selected command/workspace tabs keep color/accent-line state but no longer
    change to semibold;
  - `Pages`, `Takeoffs`, `AI Inbox`, total/status labels, manager group labels,
    active-target labels, and dynamic tree row states now use normal text;
  - dialog headers in marker sets, OpenAI settings, PDF export, 3D window, and
    takeoff folder properties now use normal text.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.

## 2026-05-04 Tab Density Correction

- Corrected the visual hierarchy pass after checking the app shell density:
  - increased the compact top command tab height from `48-54` to `60-66` so
    the `Job` / `Open` / `Folders` / `PDF` command row is not clipped;
  - reduced workspace-tab typography from `13px` semibold to `11px` normal,
    with only selected tabs using semibold;
  - reduced workspace-tab padding so `Main View`, `Sheet Manager`,
    `Takeoff Manager`, `AI Manager`, and `3D` no longer dominate the page;
  - changed manager buttons from semibold `12px` to normal `11px`;
  - softened manager group labels from extra-bold to semibold.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.

## 2026-05-04 Deeper Workspace Usability Pass

- Rechecked navigation/command-bar research before changing the shell again:
  - top-level workspaces should be few, flat, and clearly labeled;
  - selected tabs need a stronger visual connection to their content;
  - command bars should keep high-value actions visible and secondary actions
    grouped;
  - dense tables need row/header hierarchy so users can scan across columns.
- Updated `App.xaml` with a stronger hierarchy system:
  - selected command/workspace tab templates now draw an accent line;
  - manager command bars now have a full-width band style;
  - manager group labels are now visible section chips;
  - commit actions have a separate green style (`ManagerCommitButton`);
  - manager tables use alternating row color and hidden row headers.
- Updated `MainWindow.xaml` workspace tabs to show numbered labels:
  `1 Main View`, `2 Sheet Manager`, `3 Takeoff Manager`, `4 AI Manager`,
  `5 3D`.
- Added stable `Tag` keys for the workspace tabs so code and command-palette
  navigation no longer depend on visible tab text.
- Added tooltips to manager-tab buttons so short commands are discoverable
  without adding explanatory paragraphs to the app.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.

## 2026-05-04 Workspace Visual Hierarchy Pass

- Researched Fluent/WPF guidance before styling:
  - Fluent layout guidance emphasizes spacing/proximity for grouping and visual
    hierarchy;
  - Fluent typography guidance emphasizes clear typographic hierarchy for
    scannability;
  - Windows spacing/density guidance supports compact sizing for information
    rich applications;
  - WPF `TabControl`/`TabItem` styling is the correct customization point for
    selected-tab and content-page visuals.
- Added shared visual resources in `App.xaml`:
  - accent brushes (`AccentBrush`, hover/pressed/foreground);
  - toolbar/manager band brushes;
  - `ManagerButton`, `ManagerPrimaryButton`, `ManagerSubtleButton`;
  - `ManagerGroupLabel`, `ManagerToolbar`, `ManagerSurface`;
  - `CommandTabItem` and `WorkspaceTabItem`;
  - denser, clearer `DataGrid` row/header defaults.
- Applied the new styles to `MainWindow.xaml`:
  - top command tabs and workspace tabs now have distinct visual treatment;
  - manager toolbars use grouped labels such as `PDF`, `Metadata`, `Item`,
    `Batch`, `Markers`, and `Review`;
  - primary actions such as `Auto Name`, `Name+Scale`, `Set Active`, `Run AI`,
    `Build 3D Draft`, and `Accept 3D` now use accent styling.
- Extended `ApplyTheme` so the new accent/manager brushes update in light and
  dark themes.

## 2026-05-04 Workspace Manager Tabs

- Wrapped the main work area in top-level workspace tabs:
  `Main View`, `Sheet Manager`, `Takeoff Manager`, `AI Manager`, and `3D`.
- `Main View` keeps the existing canvas, Pages panel, Takeoffs panel, and
  collapsed AI Inbox intact so drawing behavior stays stable.
- Added `Sheet Manager` as a persistent sheet table using the same
  `PdfMetadataPreviewRow` shape as Auto Name / Auto Scale review:
  - columns for current page, proposed name, proposed scale, label, suffix,
    title, source, confidence, reason, warnings, and Rename/Scale checkboxes;
  - actions for Refresh, Analyze, Auto Name, Auto Scale, Name+Scale,
    Apply Checked, Open Sheet, and Open JSON;
  - analysis and apply reuse the existing PDF metadata services and learning
    feedback path instead of creating a parallel rename/scale flow.
- Added `Takeoff Manager` as a full-width takeoff item table with Set Active,
  Properties, Open Estimating, New Item, and Export CSV actions.
- Added `AI Manager` as a full-width AI inbox table with Refresh, Open Details,
  Go to Page, Run AI, Marker Sets, and Export Markers actions.
- Added a `3D` manager tab with draft/build/open actions and a text summary
  tied to the existing 3D massing draft model.
- Completed a button/function pass so each workspace owns the relevant command
  group:
  - `Sheet Manager`: PDF import/export, metadata analyze/name/scale, AI Fill,
    apply checked, open sheet/json, page sorting/repair/folders;
  - `Takeoff Manager`: save, item/folder creation, active target, properties,
    estimating, tree automation, and CSV/TXT/Excel exports;
  - `AI Manager`: AI settings, observations, selected/batch AI runs, marker
    sets, marker export, and 3D draft handoff;
  - `3D`: draft build, 3D from takeoffs, detached viewport, JSON, roof/opening
    review, and accept.
- Added `docs/60-ux-ui/WORKSPACE_TAB_COMMAND_MAP.md` as the durable command ownership
  map for the new workspace tabs.

## 2026-05-04 Full UX Shell Cleanup Block

- AI Inbox now starts collapsed (`InboxRow` 30px, splitter hidden) so the PDF
  canvas keeps vertical space on launch.
- AI Inbox header now keeps only the frequent actions visible (`Run AI`,
  `+ Add`) and moves lower-frequency batch/marker/3D actions under `More`.
- The right Takeoffs/Estimating workspace default width increased from 220px
  to 300px, with a wider minimum, so active target and estimate controls are
  less cramped.
- Estimating `ListView` now explicitly allows horizontal and vertical scroll.
  The side tab remains a quick view.
- Added `Dialogs/EstimatingWindow.cs`, a modeless full Estimating window with
  filter, current-sheet toggle, sortable virtualized `DataGrid`, Select/Page/
  Props actions, Refresh, and Copy.
- The Estimating side tab now has an `Open` button that opens or activates the
  full window. Both views use the same estimate row source.
- The active target bar now exposes only `Record` and `More`; secondary
  actions (`Props`, `Find`, sheet targets, previous/next target) live behind
  `More`.
- Pages and AI Inbox context menus now use submenus for lower-frequency
  command groups instead of one long flat list.

## 2026-05-04 UX / Design Research Audit

- Added `docs/60-ux-ui/UX_DESIGN_RESEARCH_AUDIT_2026_05_04.md` as the current
  design-risk review after the compact top command bar, Display tab reorg, and
  toolbar duplicate cleanup.
- Findings are prioritized as P0/P1/P2 and focus on what is uncomfortable or
  likely to break in daily use: overloaded main shell, cramped Estimating tab,
  crowded AI Inbox header, long context menus, `MainWindow.xaml.cs` growth,
  active-target bar density, Record workflow ambiguity, tree scalability, and
  future detached-window placement.
- The audit references current repo files and checked official PlanSwift/WPF
  sources for digitizer options, page tabs/windows, estimating workflow,
  scale behavior, WPF virtualization, collection views, and modal/modeless
  window lifecycle.

## 2026-05-04 Per-Overlay Scale-With-Page Toggles and Display Tab Reorg

- Split `ScaleSheetOverlaysWithPage` into three independent toggles. Previously
  one global flag affected only the legend; now each overlay has its own
  `Scale w/ page` checkbox and they all default off (screen-constant size):
  - `ScaleMeasurementLabelsWithPage` вЂ” value labels on line/area/count, joist
    segment labels, AI markers, AI action draft preview labels;
  - `ScaleSheetOverlaysWithPage` вЂ” sheet legend overlay (existing flag, kept
    as-is so the JSON setting stays compatible);
  - `ScaleSheetHeaderWithPage` вЂ” top sheet scale/size header overlay.
- Persistence: added two new `bool` fields to `Models/AppSettingsStore.cs`
  (`ScaleMeasurementLabelsWithPage`, `ScaleSheetHeaderWithPage`).
- `Controls/PdfViewport.cs`:
  - replaced `SheetZoomOverlayScale()` with the parameterized
    `SheetZoomOverlayScale(bool enabled)`;
  - `LegendOverlayScale()` and `HeaderOverlayScale()` both call the new helper
    using their respective per-overlay flag;
  - `DrawScreenTextBox` now picks the divisor based on
    `ScaleMeasurementLabelsWithPage` вЂ” `safeZoom` (screen-constant) when off,
    `CurrentFitZoom()` (PDF-space, normalized to fit) when on. The change is
    applied to `TextSize`, padding, border stroke, and corner radius so the
    label box stays self-consistent in either mode.
- `MainWindow.xaml.cs`:
  - `DisplaySetting_Click`, `SyncDisplaySettingsControls`,
    `ApplyDisplaySettingsToViewport`, and `ApplySheetOverlaySettings` now read
    and push the two new flags through to `PdfViewport`;
  - added `SetMeasurementLabelsScaleWithPage` and `SetSheetHeaderScaleWithPage`
    setter helpers (mirror of the existing `SetSheetOverlaysScaleWithPage`);
  - the right-click viewport overlay menu (`AddSheetOverlayMenuItems`) now
    shows three independent checkable items instead of one.
- Reorganized the top `Display` tab into five semantic groups, one concept per
  group, so settings for different overlays no longer share a column. The
  previous layout mixed measurement-label settings and the sheet-header
  `Scale Label` button inside the `Legend` group:
  1. `Values` вЂ” `All` / `Line ft` / `Area sf` / `Count ea` (visibility);
  2. `Label` вЂ” value label `Size` `TextBox` + `Set` + `в–ѕ Presets` popup +
     `Scale w/ page`;
  3. `Legend` вЂ” `Show` / `Size` / `Pos` / `Scale w/ page`;
  4. `Header` вЂ” `Size` / `Scale w/ page` (formerly `Scale Label` inside
     `Legend`);
  5. `View` вЂ” `ft/sf` / `BG` / `Dark`.
- Added `BtnLabelSizePresets_Click` so the value-label `Size` input gains the
  same `Small / Normal / Large / XL / XXL / Custom` popup that the legend and
  header `Size` buttons already use, via the shared `ShowOverlaySizePopup`
  helper.
- Tooltips were added on every checkbox/button/textblock in the `Display` tab
  so the meaning of every short label (e.g. `Scale w/ page`, `BG`, `Pos`) is
  discoverable on hover.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\\cache\\verify_build\\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.
## 2026-05-04 Compact Top Command Bar and Display Settings Stabilization

- Reworked the top `Main` / `Display` area into a compact command bar:
  - reduced top tab height and command padding;
  - grouped controls as `Job`, `PDF`, `Values`, `Legend`, and `View`;
  - shortened labels such as `Open`, `Folders`, `Import`, `Export`, `Name`,
    `Scale`, `BG`, and `ft/sf`.
- Removed duplicated command surfaces that were making the UI feel crowded:
  - `Open Job`, `Jobs`, and `New Job` were removed from the older drawing
    toolbar because the top `Main` tab owns job actions now;
  - `Import PDF`, `Export PDF`, and the old `PDF Auto` expander were removed
    from the left Pages panel because the top `Main` tab owns PDF actions now.
- Replaced the viewport value-label `S / M / L` buttons with a numeric
  `MeasurementLabelScale` input in the top `Display` tab. The user can now set
  exact values from `0.5` to `3.0`, for example `0.5`, `1.0`, or `1.35`.
- Added validation and save wiring for that numeric value-label scale through
  `BtnMeasurementLabelApply_Click`, `TxtMeasurementLabelScale_KeyDown`,
  `TxtMeasurementLabelScale_LostFocus`, and `ApplyMeasurementLabelScaleFromText`.
- Hardened the two dark-theme toggles (`BtnDarkTheme` and
  `BtnDisplayDarkTheme`) so programmatic synchronization does not recursively
  trigger the theme handlers or duplicate settings writes.
- Ran a quick XAML conflict audit:
  - no duplicated `x:Name` entries found in `MainWindow.xaml`;
  - old duplicated `PDF Auto` / side-panel import-export command names are gone;
  - build stayed clean after the toolbar and settings changes.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\\cache\\verify_build\\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.

## 2026-05-04 (UI/UX overhaul, follow-ups)

- Finished the remaining UI/UX polish from the glyph pass:
  - Count / Line / Area radio buttons now use the shared `MeasurementGlyph`
    pack beside their labels, and `ApplyTheme` rebuilds those labels so glyph
    colour follows light/dark foreground resources;
  - Takeoffs tree row-state styling now separates signals by channel:
    background is reserved for measured-on-page or multi-select state, while
    the active takeoff target is a left accent bar plus semibold text. This
    keeps active + on-page + selection visible together instead of one
    background hiding the others.
- Glyph language v2: dropped the separate colored swatch entirely. The
  measurement glyph itself is now drawn filled in the takeoff color with a
  darker stroke (perceptual `color в€’ 60` per channel) and reads cleanly on
  any panel background. `BuildTakeoffSwatchGlyph` is now a thin wrapper that
  just builds the glyph; the `Grid` + filled `Rectangle` fallback is removed.
  Tree row, page-takeoff legend row, active-takeoff bar, and on-canvas sheet
  legend all show only the glyph вЂ” no double colour blocks. Glyph sizes
  bumped (tree: 14в†’16 inactive / 16в†’18 active; page row: 12в†’14 / 14в†’16) to
  give the new outlined-and-filled glyph room to breathe.
- The on-canvas sheet legend (`DrawSheetLegendOverlay`) now passes the
  takeoff `SKColor` directly to `DrawLegendSignIcon`/`MeasurementGlyph.DrawSkia`
  instead of `textPaint.Color`. The previous separate `swatchRect` (filled
  square + darker border) is removed; the row layout collapses to
  `[glyph] [name] ... [qty]`.
- Joist calculator fixes:
  - `FormatLegendNumber` previously force-replaced `.` with `,` after
    formatting under `InvariantCulture`. This produced European-style
    decimals (`8,5 FT`) in joist legend lines while the rest of the app
    showed dots (`8.5'`). The replacement is removed; joist legend output
    is now consistent with the rest of the formatting in the app.
  - `RoundUpToNextEvenFoot` (used by `Nearest 2 Feet` rounding) added `+1`
    before the parity check, so an exactly even raw length such as `8.0 ft`
    rounded up to `10.0 ft` instead of staying at `8.0`. The unconditional
    `+1` is removed; the function now ceils to the smallest even foot
    `в‰Ґ` the raw length, which matches the rule's plain reading and the
    sibling `Nearest Even Foot` behaviour.

## 2026-05-04 (UI/UX overhaul)

- Introduced `Controls/MeasurementGlyph.cs` вЂ” a single source of truth for the
  takeoff-type icon language. Renders identical glyphs for the Takeoffs tree
  (WPF), the page-takeoff legend rows (WPF), the active-takeoff bar (WPF), and
  the on-canvas sheet legend (Skia). Replaces three divergent icon
  implementations: the diagonal-line WPF Canvas in `CreateMeasurementTypeIcon`,
  the diagonal-line Skia `DrawLegendSignIcon`, and the toolbar emoji captions.
- New glyph language: Line = horizontal segment with dot endpoints (reads as
  "ruler"); Area = rounded rectangle with soft fill; Joist = rounded rectangle
  with three parallel bars; Point/Count = filled circle (Count gets a white
  centre dot). Both renderers compute glyph stroke/padding from the same
  formulas, so the on-canvas legend visually matches the tree.
- `SetTreeItemHeader` rewritten to remove duplicate active-state markers. The
  old layout stacked four left-hand markers (4Г—18 blue bar + 16Г—16 colour ring +
  15Г—15 type icon + bold name = ~35вЂ“40 px before the name). New layout: one
  composite swatch (colored rounded square + glyph in a contrast colour
  computed from perceived luminance) + name + ledger-style right-aligned total
  (Consolas/Cascadia Mono with `MinWidth=56`). Active state now uses only a
  3-px left accent bar drawn via `TreeViewItem.BorderBrush/BorderThickness` +
  semibold name. The inline `[Type]` text and the duplicate ring/icon/bar are
  gone.
- `BuildPageTakeoffHeader` (per-page legend rows under each page in the Pages
  tree) gets the same layout вЂ” composite swatch + index `N.` + ledger qty.
- Row-state colour flags moved to themeable resources in `App.xaml`:
  `RowOnPageBrush`, `RowActiveBrush`, `RowMultiSelectBrush`, `RowDropOkBrush`,
  `RowDropBadBrush`, `RowFlagForegroundBrush`, `RowActiveAccentBrush`. Each gets
  a paired light/dark variant set by `ApplyTheme`. Old hard-coded
  `Color.FromRgb(...)` literals in `RefreshTakeoffTreeStyles` removed.
- `ToggleButton` style now has an `IsChecked=True` trigger that switches
  background to `ControlActiveBackgroundBrush` and bolds the label, so users
  can actually see whether `Snap`, `Ortho`, `Imperial`, or `Dark` is on. Same
  trigger added to the toolbar `ToggleButtonStyleKey` style.
- New `ToolRadio` style for `RadioButton` so drawing tools form a real radio
  group. `BtnPan/BtnSelect/BtnScale/BtnPoint/BtnLine/BtnArea` converted from
  `Button` to `RadioButton` with `GroupName=DrawingTool`. `_toolBtns` retyped
  to `Dictionary<string, RadioButton>`, and `ApplyToolSelection` now sets
  `IsChecked` instead of swapping `ToolBtn`/`ToolBtnActive` styles. WPF's
  built-in radio behaviour replaces the manual style-swap.
- Toolbar emoji removed from button captions (`Open Job`, `New Job`, `Pan`,
  `Scale`, `Line`, `Area` вЂ” previously had `рџ“‚`, `пј‹`, `вњ‹`, `рџ“ђ`, `в•±`, `в–­`),
  `TxtJobName` switched from `Italic` to `SemiBold` so the active job name is
  scannable.
- Sheet-legend overlay (`DrawSheetLegendOverlay`) gets typeface fallback chain
  (`Segoe UI` в†’ `Inter` в†’ default), border darkened from `#404040` to `#303030`
  for stronger contrast on light PDFs, and the swatch border is now a
  per-colour darker variant (`fill - 60` per channel) so saturated colours no
  longer have an invisible same-colour border. `DrawLegendSignIcon` delegates
  to `MeasurementGlyph.DrawSkia`.
- Active-takeoff target bar (`ActiveTakeoffTargetBar`) gets an 18Г—18 glyph host
  to the left of the name, populated via `BuildTakeoffSwatchGlyph`, so the
  recording target advertises its type visually.
- Misc theme polish: `PanelHeader` border padding `6,5` в†’ `8,6`; scrollbar
  thumb default colour upgraded from `#A0A0A0` to `#888888` for stronger
  contrast against the light track.

## 2026-05-04

- Restored joist direction semantics to the estimator-facing meaning: the
  two-click direction line is parallel to the generated joists. The calculator
  now uses that vector as the joist run direction and spaces candidate lines
  across the perpendicular span.
- Added joist selection diagnostics to the status bar, including generated
  piece count, spacing span, O.C. spacing, candidate line count, and active
  scale, so under-counts can be checked directly from the selected area.
- Kept on-canvas measurement labels at a stable screen-space size while
  restoring the previous full line display. Joist group labels are no longer
  capped, ellipsized, or collapsed into `+N more`; value labels are now drawn
  in a screen-space pass after resetting the Skia matrix, so zoom cannot scale
  the text.

## 2026-05-03

- Added PlanSwift-style joist layout for area takeoffs:
  - any Area takeoff item can be changed to `Joist layout` from item
    Properties or the `Use Area As Joists...` context action;
  - properties store joist type, O.C. spacing in inches, direction angle,
    length calculation (`None`, `Nearest Foot`, `Nearest Even Foot`,
    `Nearest 2 Feet`), and label visibility;
  - joist direction can now be set from any selected Line measurement on the
    current sheet through the item context menu or the Properties button, so
    users do not need to type degrees manually;
  - per-joist labels are optional and default off, while the area label shows a
    compact joist summary such as `27 / 8'` or `27 / 8' avg`;
  - follow-up: joist direction is now captured as a two-point line parallel to
    the generated joists after drawing/selecting the target area, and the
    direction is stored on that area section so different areas can keep
    different joist directions;
  - direction capture now starts directly from generation without OK/No modal
    prompts: the user is put into a two-click direction mode on the sheet;
  - joist quantities now require a locked direction per area section; unready
    areas show `set direction` and are not counted with a
    default 0-degree direction;
  - joist generation now includes the far boundary joist when the area width is
    not an exact multiple of O.C. spacing, avoiding an under-count at the last
    edge;
  - canvas joist labels can expand into a PlanSwift-like list with grouped
    piece counts by rounded length, while sheet legends intentionally show
    joist takeoffs as plain Area entries with area quantity only;
  - added shared takeoff signs across UI displays: line `в•±`, count `в—‹`, area
    `в–Ў`, and joist area `в–Ўв•±` in trees, linked sheet rows, section rows,
    legends, PDF legend output, active target status, and estimating rows;
  - the calculator clips parallel joist lines to each area polygon, rounds each
    joist length by the selected method, and uses the ordered joist length as
    the item/section quantity;
  - viewport overlays, PDF export overlays, sheet legends, estimate quantities,
    CSV/export rows, job persistence, and legacy project JSON persistence now
    understand joist area items as length-based takeoffs.
- Added a detached 3D viewport window for the massing workflow:
  - `3D Massing` now has an `Open 3D Window` button next to the existing draft
    controls, and the Command Palette exposes the same action;
  - the modeless window has its own WPF `Viewport3D`, Fit/Iso/Top/Front
    controls, mouse orbit/zoom, and a source-marker list;
  - the scene renders the saved massing draft when present, falls back to a
    transient marker-built draft when possible, and still shows all saved AI
    marker points so placed source points are visible outside the right panel.
- Added first-pass multi-level 3D marker support:
  - `exterior_corner`, `wall_height_sample`, and opening markers can now carry
    `level=...`, `z=...`, and `height=...` in marker value/notes;
  - `model.json` footprints store `level`, `base_elevation`, and height, and
    openings store their source level;
  - the embedded and detached 3D previews render stacked footprint levels using
    absolute vertical elevations.
- Added first-pass 3D generation from measured takeoffs:
  - the `3D Massing` tab now has `3D From Takeoffs`, with a matching Command
    Palette action;
  - it searches `Takeoffs/Walls`, `Takeoffs/Areas`, `Floors`, `Slabs`, and
    `Sqft` folders, treats child folders such as `1st`, `2nd`, and `3rd` as
    stacked levels, and builds footprint polygons from scaled Area/SQFT
    measurements first, then falls back to scaled Line wall measurements;
  - wall height is parsed from takeoff item names/notes such as
    `ext 2x6 9.0`, `height=9 ft`, or `9 ft`;
  - default level spacing now seeds plates at `0`, `10`, `20`, `30`, etc. feet
    and seeds the roof at the last level plus the same spacing; the spacing
    prompts before build and is saved in app settings.
- Created a pre-change Git savepoint before UX/new-window roadmap edits:
  `2a07c79 Add selection clipboard and UX roadmap`.
- Reviewed `docs/60-ux-ui/UX_AND_NEW_WINDOWS_IMPLEMENTATION_PROMPT.md` against
  PlanSwift and WPF sources, then added a research review section that:
  - marks stale/overlapping tasks such as Count rename and Record rewrite;
  - recommends splitting JobPicker into recent-jobs first and thumbnails second;
  - recommends extracting shared controls/view-models before detached 3D and
    Estimating windows;
  - documents modal/modeless WPF window lifecycle risks;
  - revises the implementation order so high-risk Record behavior changes are
    last and gated by a product decision.

- Added a PlanSwift-style `Select` viewport tool:
  - toolbar button `Select` and hotkey `E`;
  - left-button drag draws a selection box on the current sheet;
  - selected measurements show stronger geometry plus selection bounds/handles;
  - `Ctrl+Click` toggles individual measurements into/out of the selection;
  - `Ctrl+A` selects all measurements on the active sheet;
  - `Delete` removes all selected measurements.
- Added measurement clipboard workflow:
  - `Ctrl+C` copies the selected measurement set;
  - `Ctrl+V` pastes copied measurements to the active sheet;
  - viewport right-click menu has Copy/Paste measurement actions;
  - paste asks whether to reuse the same takeoff items/values or create copied
    takeoff items under the current Takeoffs folder;
  - pasted measurements get new IDs, target the current sheet, use the current
    sheet scale when available, and are positioned at the cursor/right-click
    point by moving the copied set's center to that point.
- Made `Select` the default non-recording tool, including the startup state and
  the tool returned to when Record is turned off.
- Extended body drag so dragging one measurement inside a multi-selection moves
  the selected group together while still saving each affected item on release.
- Fixed Count/point group move after box selection: when multiple measurements
  are selected, clicking a selected point starts group body-drag instead of
  collapsing into single-point vertex edit.
- Added a stronger edit-target cue for the active right-side Takeoffs item:
  amber row highlight, left accent marker, larger outlined color swatch, and
  semibold item name.
- Added a compact active-target bar above the right Takeoffs tree with item
  name, type, current total, and quick `Find`, `Props`, and `Record` actions;
  item context menus now have `Set Active Target`, and the Command Palette
  exposes the same active-target actions.
- Extended that active-target bar with previous/next target switching and an
  active-sheet quantity beside the overall item total, so the current sheet
  value is visible even when the Takeoffs tree stays collapsed.
- Added a `Sheet` target action that cycles through takeoffs measured on the
  active sheet in the same order as the sheet legend, with a matching Command
  Palette action.
- Changed the target bar `Sheet` action into a picker menu: it lists all
  takeoffs measured on the active sheet in legend order, shows their
  sheet-local quantity, marks the active target, and keeps `Next Sheet Target`
  available inside the menu and Command Palette.
- Added `Shift` range multi-select to the Pages tree, the Takeoffs tree, and
  the linked takeoff rows shown under expanded sheets. `Ctrl` still toggles one
  row at a time, and `Ctrl+Shift` adds a range to the current selection.
- Preserved existing multi-selection when mouse-down starts on an already
  selected row in Pages, Takeoffs, or linked sheet takeoff rows, so group
  drag/drop starts with the whole selected set.
- Added group sibling reorder for selected Pages and Takeoffs rows: context
  menus now show `Move N Up/Down`, `Ctrl+Up` / `Ctrl+Down` moves the selected
  block, cross-parent selections stay disabled, and relative order is
  preserved.
- Anchored the Pages and Takeoffs tree views to the left by disabling
  horizontal TreeView scrolling and resetting horizontal offset after
  `BringIntoView`, preventing long nested rows from shifting the side panels.
- Wired sheet measurement selection back to the right Takeoffs tree: selecting
  one or more measurements on the canvas selects the real takeoff item rows,
  so they can be dragged/copied/moved into takeoff folders.
- Wired right Takeoffs selection back to the left Pages tree: selecting an item
  or folder expands measured sheets and highlights the linked page-side takeoff
  rows where the selected takeoffs appear.
- Added `Shift` range and `Ctrl+Click` multi-select for right-side
  section/count child rows. Their context menu now supports grouped
  `Select N on Canvas`, grouped `Move N Up/Down`, and grouped delete, with
  `Ctrl+Up` / `Ctrl+Down` moving the selected section/count block inside the
  same takeoff item while preserving relative order.
- Added drag/drop transfer for selected section/count rows: dropping onto
  another takeoff item of the same measurement type moves the selected
  measurements into that item, and holding `Ctrl` copies them with fresh
  measurement IDs and the target item's color.
- Added explicit drop feedback for section/count row transfers: valid takeoff
  item targets get a green cue, invalid targets get a red cue, and the status
  text explains type blocks such as line rows being dropped onto count items.
- Fixed right-tree multi-select to drive canvas selection as a full group:
  selecting several takeoff items/folders now selects every matching
  measurement on the active sheet instead of being overwritten by the last
  clicked takeoff item.
- Polished Takeoffs selection sync: selecting a folder, including the root
  Takeoffs folder, now treats every nested takeoff item as the current
  selection for active-sheet canvas highlighting, and clicking an already
  selected section/count row re-syncs the selected row group to the canvas.
- Added bulk Takeoffs item properties from the right-tree context menu:
  selected items can receive a shared color, notes, and a shared unit price
  when all selected items have the same measurement type. Folder context menus
  can apply the same bulk edit to nested takeoff items.
- Added section/count row bulk notes and multi-row page jump polish: selected
  child rows can replace notes together, `Go to First Page` works for a group,
  and existing grouped select/move/delete behavior remains intact.
- Extended takeoff folder defaults: folder properties now include a default
  unit price, default item notes, and a default name prefix in addition to
  type/color. New takeoff items created under that folder inherit the nearest
  configured defaults from the folder chain.
- Cleaned up right-side Record/target wording: the toolbar and active-target
  bar now show the measurement type being recorded, section/count row messages
  use `Count`/`Section` wording more consistently, and starting a drawing tool
  without a matching active target asks before creating a new takeoff target.
- Hardened measurement paste edge cases: pasting Line/Area measurements onto a
  sheet without scale now either confirms reuse of the copied measurement scale
  or blocks when no scale exists, pasted measurements are rebased to the active
  sheet, and the exact pasted section/count rows are selected in the right tree
  after paste.
- Upgraded the Estimating tab from a passive table into a small workflow
  surface: it now supports extended row selection, a current-sheet filter,
  action buttons for canvas selection/page jump/properties, group selection
  from estimate rows, and sheet-scoped item quantities/costs.
- Persisted side panel widths: the Pages and Takeoffs splitter positions are
  saved in app settings after drag/close and restored on startup, reducing
  left/right panel drift between sessions.
- Improved sheet legend order feedback: dragging linked takeoff rows now shows
  the dragged count, above/below target, and pending legend position in the
  status bar; after drop it reports the final legend position range.
- Advanced the PDF metadata learning pipeline: project learned rules are now
  applied before global learned rules, Pages context menus separate project vs
  global learned-rule review, and metadata preview rows include a `Why` column
  explaining proposed name/scale decisions and learned-conflict auto-apply
  blocks. The same project/global normalization is used for direct analysis,
  resolved-source PDF matching, and AI fallback response apply.
- Wired multi-selected linked takeoff rows to canvas selection: selecting
  multiple linked rows under a sheet selects all matching sheet measurements,
  and the context menu changes to `Select N Linked Takeoffs`.
- Added group up/down movement for multi-selected linked takeoff rows in
  sheet-local legend order, available from the context menu and `Ctrl+Up` /
  `Ctrl+Down` while preserving the selected rows' relative order.
- Extended linked-row drag/drop so dragging one selected linked takeoff row
  moves the full selected block in that sheet's legend order while preserving
  the block's relative order.
- Updated the sheet legend overlay so long legends render all entries by
  adapting into multiple columns and smaller row sizing instead of hiding rows
  behind a `more` line.
- Added PlanSwift-style takeoff data export:
  - `Export TXT` writes selected/all takeoffs as header blocks plus
    tab-separated item/value/unit rows;
  - `Export Excel` writes the same rows to a standalone `.xlsx` sheet in
    columns `J:K:L` starting at `J10`, matching the old Python UI export
    shape without requiring a new NuGet package.
- Upgraded Open Job workflow for multiple job roots: settings now store
  `JobsRootPaths`, the main `Open Job` button opens the internal JobPicker,
  `Jobs` adds/switches root folders, and the picker can filter jobs by root.
- Added `Export PDF` from the Pages panel and Command Palette. It opens a sheet
  selection dialog, can include measurement overlays and the sheet legend, and
  writes a multi-page PDF using the existing PDF renderer plus Skia PDF output.
- Side panel widths now save not only on splitter drag/close but also on panel
  width changes, so left/right expansion is persisted more aggressively.
- Verified with
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`.

## 2026-05-02

Implemented and pushed a set of OurPlaneCore workflow improvements:

- Set up Git/GitHub workflow for `C:\Users\User\Desktop\ourplanecore` and pushed the current `main` branch.
- Added PlanSwift-style Record workflow improvements for Count, Line, Area, and Scale.
- Added the first PlanSwift-style Snap and Ortho digitizer modes:
  - toolbar toggles for Snap and Ortho;
  - `F3` toggles Snap;
  - `F8` toggles Ortho;
  - Snap magnetizes to existing app-created takeoff vertices and shows a red
    square preview before click;
  - Ortho constrains Line, Area, and Scale input to 90/45-degree axes, with
    `Shift` temporarily toggling the constraint.
- Added the first PlanSwift-style page tab workflow:
  - selecting a page opens or reuses the active viewport tab;
  - page context menus include `Open in New Tab`;
  - tabs can be closed from the tab strip;
  - switching tabs preserves each tab's zoom and pan.
- Made PDF Auto Rename / Auto Scale visible in the left Pages panel:
  - `Auto Name`;
  - `Auto Scale`;
  - `Name+Scale`;
  - `AI Fill` for GPT metadata fallback queueing.
- Updated AI key lookup so the OpenAI runner checks both process-level and
  Windows user-level `OPENAI_API_KEY`.
- Added PlanSwift-style automatic folder creation based on
  `C:\Users\User\Desktop\Python\XML_ENDS\planswift_ui_v2.py` and
  `planswift_UI_manager.py`:
  - `Models/PlanSwiftFolderTemplateService.cs` carries the COM/EWP Pages folder
    templates and Standard/EWP Takeoffs trees;
  - the left Pages panel and Pages folder context menu can create standard page
    folders under the selected folder/root;
  - the right Takeoffs panel and Takeoffs folder/root context menus can create
    the standard takeoff tree under the selected folder/root;
  - `From Pages` scans CAPS-style page/folder names and creates matching
    takeoff top folders with the standard subfolder tree.
- Added a persisted `Auto` / `COM` / `EWP` folder-template mode selector so the
  user can override job-name auto detection for both Pages and Takeoffs
  template creation.
- Hardened page tabs after page tree mutations:
  - open tabs rebase their page folder path after rename, move, and cut/paste;
  - the active tab reloads after the Pages tree refresh instead of being closed.
- Changed user-facing Count labels so the UI says `Count` instead of exposing the internal `point` measurement type.
- Added page/takeoff cross-highlighting:
  - pages show takeoff count badges;
  - unscaled pages show an `unscaled` badge;
  - selecting a page highlights matching takeoffs;
  - selecting a takeoff highlights measured pages.
- Added item and section estimating rows with quantity, unit, unit price, cost, and CSV export fields.
- Added section actions from the Estimating table:
  - Go to Page;
  - Select on Canvas;
  - Rename;
  - Delete;
  - Properties.
- Added canvas-to-estimate selection sync:
  - selecting a measurement on the canvas selects the matching Estimating row;
  - selecting a section/count row focuses that measurement on the canvas.
- Added persistent section/count properties:
  - `Measurement.Name`;
  - `Measurement.Notes`;
  - JSON save/load in `measurements.json`;
  - notes export in CSV.
- Moved the Estimating list into a separate right-side `Estimating` workspace tab.
- Added an Estimating quick filter for item, section, page, type, and notes text.
- Added a visible `Notes` column and widened the main Estimating columns.
- Updated handoff docs:
  - `docs/CURRENT_OURPLANECORE_STATUS.md`;
  - `docs/PROJECT_CONTEXT.md`.

Continued Takeoffs tree workflow:

- Added copy, cut, paste, and duplicate context actions for takeoff items and
  folders.
- Added `Ctrl+C`, `Ctrl+X`, and `Ctrl+V` shortcuts for the selected Takeoffs
  tree node.
- Added drag/drop movement for takeoff items and folders into takeoff folders;
  holding `Ctrl` during drop copies instead of moves.
- Copied takeoffs preserve measurements, unit pricing, section names, notes,
  and folder metadata while regenerating measurement IDs for the new copy.
- Added true multi-select behavior to the right Takeoffs tree:
  - `Ctrl+Click` toggles takeoff items/folders into the current selection;
  - dragging a selected group moves it into a takeoff folder;
  - holding `Ctrl` during the drop copies the selected group instead;
  - context menu and shortcuts now support copy, cut, duplicate, paste, and
    delete for selected takeoff nodes;
  - selection normalization prevents double-moving a child when its parent
    folder is already selected.
- Added right-panel section/count editing under each Takeoffs item:
  - completed sections/count marks appear as child rows below their takeoff
    item;
  - child rows support Properties, Rename, Go to Page, Select on Canvas,
    Move Up, Move Down, and Delete;
  - canvas selection also syncs back to the matching child row.
- Added Takeoff Item Properties from the right tree:
  - item name, color, unit price, and notes are edited in one dialog;
  - item notes are persisted in `Data.xml`;
  - changing item color updates existing measurement colors for that item.

Continued drawing/editing and AI tools:

- Added a PlanSwift-style sheet header overlay in the PDF viewport:
  - the visible top of the sheet shows architectural scale text when it matches
    a common imperial preset, for example `Scale: 1/8" = 1' 0"`;
  - the right side of the header shows sheet size from PDF points in inches,
    for example `36.00 x 24.00`;
  - if no scale is set, the overlay shows `Scale: not set`.
- Added canvas right-click edit actions for selected measurements:
  - Properties;
  - Rename;
  - Delete;
  - Insert Vertex Here;
  - Remove Nearest Vertex.
- Vertex insert/remove actions update the existing measurement, refresh totals,
  and queue autosave like drag-handle edits.
- Tightened missing-scale drawing behavior:
  - Line and Area Record now require sheet scale before the first point can be
    placed;
  - the viewport blocks unscaled Line/Area geometry and tells the user to use
    Scale or PDF Auto Scale first;
  - Count remains available without scale.
- AI Assist requests now save visual crop PNGs into `AI_Context/crops` and
  record the crop path plus PDF crop coordinates in the project AI context.
- AI Assist request actions now create structured pending request JSON files in
  `AI_Context/requests` so a future OCR/LLM worker has a direct queue to read.
- Added explicit `Save AI crop here` / `Save measurement AI crop` commands for
  creating context evidence without changing estimating data.
- Added AI Inbox review actions:
  - double-click or Enter opens full observation details;
  - right-click can open details, jump to the matching page, open the linked
    crop PNG, open the crop folder, open request JSON, open project context, or
    refresh the Inbox;
  - `F5` refreshes the Inbox.
- Added manual AI response capture from Inbox:
  - responses are saved to `AI_Context/responses`;
  - the matching request JSON status is updated to `done`;
  - the full observation dialog shows request/response details;
  - the Inbox preview shows the request status prefix.
- Added per-sheet PDF layer manifests:
  - pages with detected PDF layers now write a separate `layers.json` beside
    `source.json`;
  - the file records the source PDF, page index/page number, generation time,
    layer count, and visible layer list;
  - stale `layers.json` files are removed when a page source is rewritten
    without layer metadata.
- Wired PDF layer metadata into AI request records:
  - pending request JSON now includes `page_folder`, `layer_manifest_path`,
    `layer_count`, and the visible `layers` list when a page has layer data;
  - AI Inbox can open the matching page `layers.json` file from the request
    context menu.
- Added the first real AI runner:
  - AI Inbox has `Run AI` / `Run AI Request`;
  - the runner reads `OPENAI_API_KEY` and optional
    `OURPLANECORE_OPENAI_MODEL` from the environment;
  - selected or next pending request JSON is sent to OpenAI with crop PNG and
    layer context;
  - model output is saved to `AI_Context/responses`, the request status becomes
    `done` or `failed`, and raw provider JSON is saved beside the response.
- Added action-draft extraction for AI responses:
  - `AI_Context/actions/{requestId}.json` is created after automatic or manual
    AI response capture;
  - fenced JSON from the response is parsed into reviewable action records with
    type, label, page, measurement type, confidence, notes, and PDF points;
  - AI Inbox can open the action draft JSON, but the app does not apply drafted
    geometry automatically yet.
- Added canvas preview for AI action drafts:
  - AI Inbox has `Preview Action Draft` and `Clear Action Preview`;
  - draft points render on the PDF canvas as dashed cyan line/area/point
    overlays;
  - preview remains read-only and does not create measurements.
- Added apply support for reviewed AI action drafts:
  - AI Inbox has `Apply Action Draft`;
  - applying confirms with the user, then creates real line/area/point
    measurements from draft PDF points;
  - a matching active takeoff item is reused, otherwise an AI-colored takeoff
    item is created;
  - created measurements are saved, totals/page badges refresh, and the draft
    records `applied_measurement_ids` with status `applied`.
- Added the first AI marker capture MVP:
  - viewport right-click has `Save AI marker here`;
  - measurement right-click has `Save measurement as AI marker`;
  - the marker dialog captures marker type, sample kind, optional value, and
    note;
  - each marker saves crop evidence under `AI_Context/crops` and structured
    marker JSON under `AI_Context/markers`;
  - markers reload per sheet and render on the PDF canvas with distinct overlay
    colors;
  - AI Inbox shows marker records and can open the marker JSON file.
- Added the first AI marker review workflow:
  - AI Inbox has marker type and sample-kind filters;
  - marker rows can edit type, sample kind, value, and note;
  - marker rows can delete the active marker JSON from overlay/Inbox while
    keeping crop evidence and the append-only observation log.
- Added the first AI marker organization/export workflow:
  - AI Inbox has `Set` and `Export` actions;
  - `Set` saves the current marker type/sample filter as a marker set under
    `AI_Context/marker_sets`;
  - `Sets...` and the AI Inbox context menu can apply saved marker sets back
    to the filters, rename sets, delete set JSON, or open set JSON;
  - `Export` writes the current visible filtered markers plus marker sets into
    `AI_Context/exports/markers_context.json`;
  - marker context menus can hide the selected marker type from the canvas
    overlay and restore all marker types;
  - hidden marker types are now persisted per job in
    `AI_Context/project.json`.
- Added the first crop-bookmark batch workflow:
  - AI Inbox crop/marker rows can be saved as bookmarks under
    `AI_Context/crop_bookmarks`;
  - the Inbox has `Run New`, which sends only bookmarks with `status=new` to
    OpenAI;
  - each bookmark records request id, response id, action draft id, result
    summary, processed time, and `done` / `failed` status;
  - the OpenAI prompt for `crop_bookmark_request` includes the exported
    `markers_context.json` when available.
- Added the first 3D massing data service:
  - `Models/SmartMassingDraftService.cs` defines the draft JSON model for
    `AI_Context/3d_massing/model.json`;
  - the service can build a draft from `exterior_corner`, `wall_height_sample`,
    `roof_note`, and `roof_edge_sample` markers;
  - the draft records source marker ids, assumptions, unresolved questions,
    approximate footprint points, wall height, and roof notes.
- Added the next 3D Massing UI slice:
  - the placeholder `3D Massing` tab now has a source-marker review table;
  - each row shows marker role, type, page, PDF point, draft point, and status;
  - selected rows can jump to the source sheet, open marker JSON, or open crop
    evidence;
  - the tab now includes a lightweight top-down footprint preview;
  - selecting a source marker highlights the matching draft point in the
    preview and shows marker value/note, PDF point/rect, crop status, and JSON
    path in a detail panel;
  - `Jump` opens the source sheet and centers the viewport on the marker PDF
    point where possible;
  - this remains a reviewable 2D/text/table workflow, not an orbit/3D viewer.
- Added the first roof-modeling slice for 3D Massing:
  - `SmartMassingRoof` now stores reviewable `guides` in
    `AI_Context/3d_massing/model.json`;
  - roof notes can infer basic `gable`, `hip`, `shed`, and `low_slope` roof
    types plus pitch text;
  - the service creates eave outline, ridge/hip-ridge, shed slope-arrow, or
    low-slope cap guides from reviewed markers and footprint bounds, with an
    explicit roof-axis candidate instead of a ridge when type is still unknown;
  - the `3D Massing` preview draws those roof guides over the footprint for
    quick review;
  - this is still a draft guide, not a roof solver or accepted BIM geometry.
- Added the first Auto Roof recognition slice:
  - the `3D Massing` tab now has `Auto Roof`, which queues a
    `roof_recognition_request`;
  - the request saves a large current-sheet/marker-bounds crop and attaches
    nearby marker evidence crops when available;
  - `OpenAiRequestRunner` has a roof-specific prompt that asks only for
    reviewable `roof_note`, `roof_edge_sample`, `ridge_sample`,
    `valley_sample`, `roof_high_edge`, `roof_low_edge`, and
    `overhang_sample` candidates;
  - the action review dialog can be reused in marker mode, so accepted Auto
    Roof candidates become normal `ai_marker` records instead of takeoff
    measurements;
  - accepted roof markers still require `Build 3D Draft` before they affect
    `AI_Context/3d_massing/model.json`.
- Added editable roof review:
  - `SmartMassingRoof` now stores review status, reviewed timestamp, and review
    notes;
  - `SmartMassingRoofGuide` now stores review status;
  - the `3D Massing` tab has `Review Roof`;
  - `Dialogs/RoofReviewDialog.cs` lets the user edit roof type, pitch,
    confidence, notes, review notes, and guide rows;
  - guide rows can be kept/rejected, and kind/label/confidence/points/notes can
    be edited before saving;
  - saving writes reviewed roof state back to `AI_Context/3d_massing/model.json`
    and refreshes the preview/summary.
- Added the first actual 3D shell preview:
  - `SmartMassingRoof` now stores derived `planes`;
  - `SmartMassingDraft` now stores whole-draft reviewed timestamp and review
    notes;
  - `SmartMassingDraftService.SaveDraft` refreshes derived geometry before
    writing `model.json`;
  - gable/ridge/axis guides generate two roof surface candidates;
  - shed/high-low guides generate a sloped roof plane;
  - low-slope/unknown fallback generates a cap plane;
  - the `3D Massing` tab now embeds WPF `Viewport3D` and renders floor,
    extruded walls, and roof planes;
  - Fit/Iso/Top/Front camera controls and mouse orbit/zoom are wired;
  - `Accept 3D` marks the whole draft as reviewed AI context without creating
    estimating quantities.
- Added the next 3D review slice:
  - `window_sample`, `door_sample`, and `opening_sample` markers now become
    draft openings in `AI_Context/3d_massing/model.json` with source marker id,
    nearest wall index, projected center point, approximate width/height,
    confidence, and notes;
  - the WPF `3D Massing` preview now renders projected opening rectangles and
    source/opening marker pins;
  - floor, wall, roof, opening, and pin geometry now carries source metadata;
  - clicking 3D geometry highlights the selected object, updates status/details
    text, and selects the first linked source marker row when available;
  - `Review Openings` opens an editable keep/reject grid for projected
    openings, saving kept rows as `reviewed` and unchecked rows as `rejected`
    evidence in `model.json`;
  - opening review outcomes are appended to project/global marker feedback
    learning as `event_type=3d_opening_projection_review`;
  - `Accept 3D` now writes a timestamped accepted-draft snapshot under
    `AI_Context/3d_massing/snapshots`;
  - hip roof type / `hip_ridge` guide drafts now generate four reviewable roof
    plane candidates from ridge/guide and footprint bounds.
- Added the first PDF-first Auto Rename / Auto Scale implementation slice:
  - `Tools/pdf_layers_helper.py` now has a `sheetmeta` action in CLI and worker
    modes;
  - `sheetmeta` reuses existing PyMuPDF text/word/layer infrastructure and
    returns sheet label, sheet key, title, suffix, skip-scale, title/body scale
    candidates, selected scale, scale ratio, meters-per-PDF-point, page size,
    layers, warnings, and rename candidate;
  - `Models/PdfSheetMetadataService.cs` calls the helper and normalizes
    metadata for the WPF app;
  - `Models/OurPlaneCoreJob.cs` can now read/write per-page
    `source_pdf.json`;
  - Pages context menus now expose Analyze PDF Metadata, Auto Rename from PDF,
    Auto Scale from PDF, Auto Rename + Scale from PDF, Open `source_pdf.json`,
    and Capture Final Learning Snapshot;
  - auto apply is review-gated through a WPF preview grid where rename and
    scale can be checked/unchecked per page; obvious same-folder rename
    conflicts are shown as warnings and are not checked by default;
  - accepted/apply/manual snapshot outcomes are written into
    `SmartLearningStore`;
  - if an imported source PDF is missing, the metadata service can search the
    E-Wood source folder pattern and match pages by sheet key.
- Added GPT/image fallback workflow for unresolved PDF sheet metadata:
  - Pages context menus now include `Queue GPT Metadata Fallback`;
  - fallback saves a bottom/title-block crop PNG into `AI_Context/crops`;
  - fallback creates a `pdf_sheet_metadata_fallback` request in
    `AI_Context/requests` with deterministic metadata, page/layer context, and
    a strict JSON response prompt;
  - `OpenAiRequestRunner` now uses a sheet-metadata-specific prompt for this
    request type;
  - AI Inbox shows these requests as `Sheet Meta`;
  - after a response is saved, AI Inbox can run `Apply Sheet Metadata Response`,
    parse the JSON response into `source_pdf.json`, and open the same preview
    grid before rename/scale apply;
  - fallback queueing skips pages that already have a non-failed
    `pdf_sheet_metadata_fallback` request.
- Added learning-based confidence hints for PDF metadata preview:
  - `SmartLearningStore` compares proposed metadata against global
    accepted/corrected/manual records;
  - preview rows show `Confidence`;
  - repeated supporting records can raise confidence to `learned-medium` or
    `learned-high`;
  - learned conflicts add a warning and keep rename/scale unchecked by default.
- Added learned-rule distillation:
  - final learning snapshot now writes project and global `learned_rules.json`;
  - repeated title-token/suffix outcomes with enough support become explicit
    rules with support count, confidence, skip-scale vote, and common scale.
- Connected distilled learned rules back into detection:
  - if deterministic metadata is missing suffix or scale, global learned rules
    can fill those fields based on matching title tokens;
  - preview warnings call out when a learned rule was applied.
- Added learned-rule review controls:
  - distilled rules now carry an `enabled` flag;
  - disabled rules are ignored by future PDF metadata detection;
  - regenerated project/global `learned_rules.json` preserves previously
    disabled rules by stable rule id;
  - Pages context menus now include `Review Learned Rules...` for the global
    rule set.

Verified after the latest changes:

```powershell
dotnet build .\ourplanecore.sln
```

Result:

```text
Build succeeded.
Warnings: 0
Errors: 0
```

Read-only State Str structural PDF diagnostic:

```text
S-100 FOUNDATION PLAN              -> s100 f    -> 1/8" = 1'0"
S-101 SECOND FLOOR FRAMING PLAN    -> s101 2nd  -> 1/8" = 1'0"
S-104 ROOF FRAMING PLAN            -> s104 rf   -> 1/8" = 1'0"
S-500 TYPICAL WOOD FRAMING DETAILS -> s500 d    -> no scale
S-503 WOOD TRUSS SECTIONS          -> s503 sec  -> 3/8" = 1'0"
```

Latest pushed commits:

```text
5e3ac57 Show estimate notes column
3d1ded4 Add estimating quick filter
e5b5874 Move estimating list into workspace tab
06e51b5 Add section notes properties
4be08bb Use count-specific workflow labels
ba8bf22 Sync canvas and estimate section selection
```

## Queued Tasks

- Continue PlanSwift-style manual takeoff workflow:
  tighten right-side Takeoffs editing, item properties, section management,
  and drawing/editing affordances before changing the Estimating tab further.
- Continue the AI marker training workflow from
  `docs/50-3d-roof-ai/AI_MARKER_TRAINING_IDEAS.md`: first-slice `Find Similar From Marker`,
  auto-created candidate crop bookmarks, and failed-bookmark retry controls are
  in place; next add cross-sheet batch search and a dedicated bulk marker
  review panel.
- Add the lightweight 3D massing viewer idea from
  `docs/50-3d-roof-ai/AI_3D_MASSING_VIEWER_IDEAS.md`: use exterior corner markers, wall
  height samples, and roof notes to build a simple separate-tab 3D draft for
  visual review, not BIM-grade modeling. The data service and placeholder
  `3D Massing` tab are in place, including source-marker jump/JSON/crop
  actions, a top-down footprint preview, selected-marker highlighting, and
  evidence details. The draft now also includes roof guide overlays for basic
  ridge/hip/shed/low-slope/unknown-axis review plus explicit
  `ridge_sample`, `valley_sample`, `roof_high_edge`, `roof_low_edge`, and
  `overhang_sample` marker support. `Auto Roof` can now queue reviewable roof
  marker candidates and save accepted candidates as markers. Editable roof
  review, simple roof planes, a WPF 3D shell preview, and `Accept 3D` are also
  in place. The 3D preview now also supports object/source selection, marker
  pins, projected opening rectangles, and `Review Openings` keep/reject editing
  from window/door/opening markers; next improve complex valley/multi-roof plane
  generation, include opening feedback in future prompts, and add a visible
  snapshot/history picker if accepted snapshots need comparison.
- Continue hardening the PDF-first Auto Rename / Auto Scale workflow:
  add more review details for learned-rule conflicts and per-project rule
  scope; the first global rule enable/disable UI is in place.
- Add the self-learning feedback loop for Auto Rename / Auto Scale:
  `SmartLearningStore` now defines per-project and global JSONL storage for
  detector proposals, user corrections, final manual page state, and project
  learning summaries. Future preview/apply UI should append accepted,
  corrected, rejected, and manual-final records.
- Provider configuration UI for API key/model selection is in place; continue
  hardening the model list and status messaging as needed.
- Add automatic request processing and better general failed-request retry
  controls beyond crop bookmarks.
- Harden the SmartTrace review UI with source links and per-row canvas focus.
- Continue hardening Count wording in remaining secondary dialogs.

## 2026-05-02 Parallel Agent Merge Review

Integrated the three parallel Codex agent slices into the shared worktree:

- Tab 1 `Find Similar From Marker`: AI marker Inbox rows can queue
  `find_similar_marker_request`; OpenAI prompt context includes marker crop,
  source marker JSON, exported marker context, and page/layer context.
- Tab 2 crop bookmark retry/auto-new: `Retry Failed` processes only failed
  bookmarks; successful action drafts can create guarded new candidate
  bookmarks while preserving `Run New` as `status=new` only.
- Tab 3 SmartTrace review UI: `Review Action Draft` supports accept/reject,
  target takeoff selection, preview, and apply-only-accepted geometry with
  review indices recorded in the draft JSON.
- Added nearby-sheet context for `Find Similar From Marker`: the request now
  saves a wider crop around the source marker under `AI_Context/crops`, stores
  it in `context_crop_paths`, and sends it to OpenAI alongside the marker crop.
- Added marker candidate feedback learning: reviewing a `Find Similar From
  Marker` action draft appends accepted/rejected rows to
  `AI_Context/learning/marker_feedback.jsonl` and the global learning folder;
  later marker prompts include recent feedback for the same source marker or
  marker type.
- Added feedback-aware marker context export: `markers_context.json` now
  includes recent `marker_feedback` records plus `marker_quality` summaries
  with accepted/rejected/applied counts and average confidence.
- Added visible marker quality in AI Inbox: marker rows now append compact
  feedback text such as accepted/rejected/applied counts and average confidence
  to the row preview.
- Second-wave shared changes were also present and integrated: toolbar
  `AI Settings`, takeoff folder `Folder Properties...`, and the `3D Massing`
  draft panel.
- Integration checks: no conflict markers, no hardcoded OpenAI test key,
  all XAML event handlers resolved, `dotnet build .\ourplanecore.sln` passed
  with 0 warnings and 0 errors, and a short `dotnet run --project
  .\ourplanecore.csproj --no-build` startup smoke test stayed alive.

## 2026-05-02 3D Massing Review Slice

- Extended `SmartMassingDraftService` so `window_sample`, `door_sample`, and
  `opening_sample` markers become draft `openings` in
  `AI_Context/3d_massing/model.json` with source marker id, nearest wall index,
  projected center point, approximate type-based width/height, confidence, and
  review notes.
- Extended the WPF `3D Massing` preview with projected opening rectangles,
  source/opening marker pins, and object metadata for floor, wall, roof,
  opening, and pin geometry.
- Added first-pass 3D hit selection: clicking 3D geometry highlights the
  selected object, updates the status/details line, and selects the first
  linked source marker row when one exists so existing `Jump`, `Marker JSON`,
  and `Crop` actions can be used.
- Added `Review Openings`, an editable review grid for projected openings. The
  user can keep/reject rows, edit type, wall index, center point, width/height,
  confidence, and notes. Kept rows save as `reviewed`; unchecked rows save as
  `rejected` evidence.
- Added opening projection feedback learning: each `Review Openings` save
  appends accepted/rejected records to project/global `marker_feedback.jsonl`
  with `event_type=3d_opening_projection_review`.
- Added accepted 3D snapshots: `Accept 3D` writes a timestamped copy of
  `model.json` under `AI_Context/3d_massing/snapshots` after marking the draft
  reviewed.
- Added first-pass hip roof surface generation: `hip` roof type or `hip_ridge`
  guides create four reviewable `hip_roof_plane` candidates instead of the
  generic two-plane ridge fallback.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\\cache\\verify_build\\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors. Normal `bin\\Debug` build output was
  still locked by the currently running `OurPlaneCore` process.

## 2026-05-02 Editing, Layer AI, and Page Sorting Fixes

- Fixed canvas measurement editing friction:
  - blue vertex handles now have a larger hit target;
  - dragging a handle still moves one vertex;
  - dragging the measurement body now moves the whole line/area/count
    measurement instead of only selecting it and doing nothing.
- Added right-click PDF layer AI context:
  - each layer row/checkbox/highlight checkbox has `Save Layer Info for AI`;
  - `Queue AI Request for This Layer` creates a pending
    `pdf_layer_ai_request`;
  - saved context includes selected layer number/name/visible/highlight state,
    all cached page layers, and `layers.json` when available.
- Added visible page sorting:
  - `Page Setup` now has `Sort A/S`;
  - Pages context menus also expose `Sort A/S into Arch/Struct`;
  - A sheets move to `Pages/00. imported/Arch`, S sheets to
    `Pages/00. imported/Struct`, trailing `-` names to `Pages/--------others`,
    and those folders are sorted A-Z after the move.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\\cache\\verify_build\\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors. Conflict-marker/hardcoded-key scan found
  no active repo secrets or merge markers.

## 2026-05-02 Job3 Measurement Visibility and Left Panel Follow-Up

- Confirmed `C:\Users\User\Desktop\check\test\JoB#3` still has saved
  measurement JSON under `Takeoffs`; the regression was display/edit flow, not
  data loss.
- Hardened measurement-to-page matching:
  - job open repairs saved `PageFolder` references by current page path or
    unique page folder name;
  - page/folder move and `Sort A/S` now rebase measurement `PageFolder`
    references immediately;
  - viewport page filtering now compares normalized paths case-insensitively
    instead of raw strings.
- Added `Repair Links` to the left `Page Setup` panel so an already-open job can
  reconnect stale measurement page links without reopening the job.
- Improved edit dragging:
  - existing measurements can be grabbed in any tool mode when no new drawing is
    in progress;
  - body/vertex drag repaints continuously but saves/recalculates once on mouse
    release, avoiding tree/table rebuilds on every tiny mouse move.
- Improved open-job behavior: if no specific page is requested, the app opens
  the last page from that job or the first available sheet, so a project no
  longer opens into an empty viewport that looks like missing measurements.
- Left Pages panel now groups controls into expandable `Page Setup`, `PDF Auto`,
  and `PDF Layers` sections. `Sort A/S`, `Auto Folders`, and `Repair Links` are
  visible in the expanded `Page Setup` section.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\\cache\\verify_build\\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.

## 2026-05-02 Job2 Legacy Page Repair and Drag Follow-Up

- Confirmed `C:\Users\User\Desktop\check\test\JoB#2` has saved measurements in
  the Takeoffs tree, but their `page_folder` values still pointed to legacy
  import folders `Pages\Page 2` and `Pages\Page 3`, which no longer exist after
  sheet auto-naming.
- Extended measurement link repair so old `Page N` references can map to the
  current unique page whose `source.json` PDF page index is `N - 1`. For the
  current Job2 data this resolves:
  - `Page 2` -> `Pages\s100 f`;
  - `Page 3` -> `Pages\s101 2nd`.
- Hardened vertex/body drag again: movement now uses mouse screen delta divided
  by current zoom from the original drag start, so the handle/measurement follows
  the cursor instead of relying on repeated absolute PDF-point recalculation.
- Hardened edit entry again: selected measurements are hit-tested first with a
  larger grab radius, all vertex/body hit targets are larger, and clicking an
  existing measurement cancels any accidental in-progress Line/Area input before
  starting the edit drag.
- Fixed the canvas/tree selection feedback loop that could interfere with drag:
  viewport selection now updates the Takeoffs tree section row without letting
  the tree handler call `SelectSectionOnCanvas` / `FocusMeasurement` back into
  the viewport during the same mouse action.
- Hardened drag capture further: edit drag no longer depends on
  `MouseMove.LeftButton == Pressed`, capture starts before firing selection
  events, and main-window Estimate/Takeoffs sync is skipped while the left
  mouse button is down.
- Replaced remaining measurement/page raw string comparisons in the main window
  with normalized path comparison so left-page badges, right-takeoff
  highlights, scale propagation, and "Select on Canvas" use the same page
  matching rule as the viewport.
- `Repair Links` and job-open repair now report unresolved stale page links in
  the status text when a measurement still points to a missing page that cannot
  be matched safely.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\\cache\\verify_build\\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.

## 2026-05-03 Measurement Repair and Editing Final Postmortem

- User confirmed the final repair worked: previously missing measurements became
  visible again and line/vertex editing started working.
- Final confirmed root causes:
  - missing canvas measurements were saved in `Takeoffs`, but their
    `Measurement.PageFolder` no longer matched the renamed page folder;
  - `JoB#2` specifically used stale `Pages\Page 2` / `Pages\Page 3` references
    after sheets had been auto-named to `s100 f` / `s101 2nd`;
  - initial edit fixes improved hit testing but drag was still interrupted by
    mouse capture / selection-sync behavior between the viewport, Estimate
    table, and Takeoffs tree;
  - relying on transient `MouseMove.LeftButton == Pressed` was unsafe during
    captured WPF/Skia mouse movement.
- Final durable fixes:
  - job open and `Repair Links` repair stale measurement links;
  - legacy `Page N` links map to a unique current `source.json` PDF page index;
  - viewport and main-window code use normalized page-folder comparison;
  - `Repair Links` is directly visible under `Import PDF`, next to `Sort A/S`,
    and is also available in the Pages context menu;
  - edit drag starts capture before selection events, continues until
    mouse-up/lost-capture, and does not depend on per-move left-button state;
  - main-window Estimate/Takeoffs selection sync is skipped while the left mouse
    button is down so right-side UI cannot re-enter viewport focus during drag.
- Added the detailed future-handoff file:
  `docs/30-takeoffs-measurements/MEASUREMENT_PAGE_LINK_AND_EDITING_POSTMORTEM.md`.
- Regression rule for future agents: if measurements exist in the right tree but
  not on the canvas, inspect `measurements.json` `page_folder` before debugging
  drawing transforms or `Data.xml`.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\\cache\\verify_build\\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors after the working fix.

## 2026-05-03 Best Practices Addendum and Quick UX Cleanup

- Accepted the parallel best-practices research as roadmap guidance, with a
  caveat that its source URLs were not web-verified because WebSearch/WebFetch
  were blocked in that agent sandbox.
- Recorded the new architecture queue in
  `docs/60-ux-ui/UX_AND_NEW_WINDOWS_IMPLEMENTATION_PROMPT.md` and
  `docs/OURPLANECORE_TASK_ROADMAP.md`: shrink/split future
  `MainWindow.xaml.cs` work, AvalonDock as a later spike, Command Palette,
  screen-pixel Snap v2 glyphs, crash-recovery snapshots, HelixToolkit for the
  future 3D viewer extraction, sample/recent onboarding, and CSI/ClosedXML
  estimating hardening.
- Implemented the revised quick visual cleanup slice:
  - added shared light/dark toolbar hover and pressed brushes;
  - kept active tool buttons visually active while hovered/pressed;
  - changed the XAML startup label from `Point` to `Count` so the wrong word no
    longer flashes before constructor normalization;
  - moved Pages/Takeoffs header styling to a shared `PanelHeaderBorder` style;
  - made `PDF Layers` start collapsed so the left panel opens cleaner.

## 2026-05-03 Command Palette First Slice

- Added `Dialogs/CommandPaletteDialog.cs`: searchable command window with
  filter, keyboard navigation, unavailable-command reasons, Enter/double-click
  execution, and Escape cancel.
- Added `MainWindow.CommandPalette.cs` instead of growing
  `MainWindow.xaml.cs`. The partial builds command metadata and dispatches to
  existing handlers for File, View, Tools, Edit, Pages, PDF Layers, Takeoffs,
  AI, and 3D Massing actions.
- Wired `Ctrl+Shift+P` to open the Command Palette and `Ctrl+S` to the existing
  Save handler.
- Command Palette intentionally reuses current command handlers; it does not
  create a second command system yet.

## 2026-05-03 Recent Jobs / JobPicker Lite

- Added recent-job persistence to `Models/AppSettingsStore.cs`. Successful
  `OpenJob` calls now prepend/update an LRU `RecentJobs` list in
  `%APPDATA%\OurPlaneCore\settings.json`.
- Added `Dialogs/JobPickerDialog.cs`: searchable recent/jobs-root picker with
  job name, last-opened time, source, status, path, keyboard navigation, Open,
  Browse Job, Jobs Folder, New Job, and Cancel actions.
- Added `MainWindow.JobPicker.cs` to keep picker/open/new-job workflow out of
  `MainWindow.xaml.cs`.
- Wired `Ctrl+Shift+O` and Command Palette `Open Recent Job` to the picker.
- Preserved current startup behavior: a valid last job still auto-opens. If the
  last job is missing and recent/jobs-root entries exist, the picker is shown.
- Deferred thumbnails, pin/unpin, remove-from-recent, and sample/empty-state
  CTA to later slices so this PR stays independent of PDF rendering.

## 2026-05-03 JobPicker Background Thumbnails

- Added `Models/JobThumbnailService.cs`. It finds the first renderable PDF page
  in a job, uses the existing PDF render service, fits it into a small PNG, and
  saves it under `%APPDATA%\OurPlaneCore\thumbnails\{job-hash}.png`.
- Extended `RecentJobInfo` with `ThumbnailPath`; `AddRecentJob` preserves an
  existing thumbnail path when the LRU row is refreshed.
- After successful `OpenJob`, thumbnail generation is queued on a background
  task. It updates the matching RecentJobs row when the PNG is ready and stays
  silent when a new/empty job has no renderable PDF yet.
- `JobPickerDialog` now has a `Preview` column with a themed placeholder and
  loads thumbnails with `BitmapCacheOption.OnLoad` so PNG files are not locked.

## 2026-05-03 JobPicker Pin and Cleanup

- Extended `RecentJobInfo` with `IsPinned`.
- Added recent-list helpers in `AppSettingsStore`: pin/unpin, remove, and trim
  while preserving pinned rows.
- Added a JobPicker row context menu for `Pin to Recent`, `Unpin from Recent`,
  `Open Folder in Explorer`, and `Remove from Recent`.
- Pinning a jobs-root row creates/updates a RecentJobs entry; removing a row
  removes only the recent-list entry and never deletes the job folder. If the
  job still lives under the active jobs root, the row remains visible as a
  `Jobs Folder` row.

## 2026-05-03 Sample Job Onboarding

- Added `Models/SampleJobService.cs`, which creates a local sample job under
  the configured JobsRoot or `Documents\OurPlaneCore Jobs`.
- Added `OurPlaneCoreJobStore.CreatePageFromPdf` so generated/sample workflows
  can add a single page without going through the multi-page import dialog.
- Added a generated one-page sample PDF plus preloaded line, area, and count
  takeoff items with sample measurements, notes, unit prices, and page scale.
- Added a `Sample Job` action to `JobPickerDialog`; the picker now works as a
  first-run empty state even when the recent/jobs-root list is empty.
- Added `Create Sample Job` to the Command Palette.

## 2026-05-03 Sheet Legend and Suffix Page Sorting

- Added a compact sheet legend overlay in the PDF viewport. The active sheet now
  shows measured takeoff item colors, names, and sheet-local quantities for the
  measurements visible on that page.
- Added a right-click `Legend` toggle on the PDF viewport. The toggle hides or
  shows the sheet legend and persists the choice in app settings.
- Added right-click overlay controls for legend position, legend size, sheet
  scale/size label size, custom size multipliers, and whether those overlays
  scale with page zoom.
- Added `D/Sec/WT` next to `Sort A/S` in the left Pages panel, plus the same
  action in Pages context menus and the Command Palette.
- The new suffix sort preserves the existing A/S sorter and handles the second
  pass separately: `d` pages move to `details struct` or `details arch`,
  `sec` moves to `sections`, `u` moves to `units`, and `v` / `wt` / `ft` /
  `sv` / `sw` pages are moved to the Pages root and ordered at the top.
- Hardened measurement paste and drag-refresh behavior so right-side tree
  refreshes do not re-center the viewport and make the sheet appear to shift
  sideways.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\\cache\\verify_build\\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.

## 2026-05-03 Collapsed Pages and Takeoffs Trees

- Pages and Takeoffs now open collapsed after a job loads, even when the app
  restores the last sheet first.
- Added compact `-` / `+` controls in the Pages and Takeoffs headers to
  collapse or expand the whole tree on demand.
- Takeoff items no longer auto-expand just because they contain section/count
  child rows; explicit section navigation can still expand the needed item.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\\cache\\verify_build\\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.

## 2026-05-03 Page Sheet Takeoff Legend Order

- Each page node in the left Pages tree can now expand to show the real takeoff
  items that have measurements on that sheet.
- Page takeoff child rows are linked to the actual right-side Takeoffs items,
  but they intentionally expose no edit/delete actions for the real takeoff.
- The only sheet-local action is changing that page's takeoff order with
  `Move Up in Legend` / `Move Down in Legend` or `Ctrl+Up` / `Ctrl+Down`.
- The same sheet-local order can now be changed by dragging a linked takeoff
  row above or below another linked takeoff row under the same page.
- During that drag, the target row shows a green before/after insertion cue so
  the legend-order drop target is visible before release.
- The per-page order is saved in the page `source.json` as
  `legend_takeoff_order` and drives only the sheet legend order.
- The active linked takeoff row under a page now gets an edit-mode style cue:
  stronger color swatch, bold label, and active background.
- Selecting a linked takeoff row under a page now also selects that takeoff's
  measurements on the active sheet canvas.
- Selecting a measurement on the canvas now activates its takeoff and selects
  the linked takeoff row under the current page, closing the sync loop.
- Selecting a takeoff item or section in the right Takeoffs tree now also
  selects the matching linked takeoff row under the current page when that
  takeoff has measurements on the active sheet.
- Selecting a takeoff item in the right Takeoffs tree now also selects that
  takeoff's measurements on the active sheet canvas while keeping the item row
  as the active edit target.
- Page and linked-takeoff context menus now include `Sort Sheet Legend A-Z` and
  `Reset Sheet Legend Order` for quick sheet-local legend order cleanup.
- Linked takeoff rows under a page now show their 1-based legend position so
  the saved sheet legend order is visible in the Pages tree.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\\cache\\verify_build\\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.

## 2026-05-08 Export Legend Parity, Auto Routing, Detached Sheets, 3D Marker Selection

- Export sheet legend now uses the same Skia overlay renderer as the viewport
  legend (`Controls/SheetOverlayRenderer.cs`). The intent is visual parity:
  same glyphs, rows, columns, colors, quantities, anchoring, and fit behavior.
- PDF export now also draws the sheet scale / sheet-size block with the same
  shared renderer, so exported sheets include the viewport-style header plus
  the viewport-style legend when legend export is enabled.
- Output sizing remains independent from viewport sizing. Viewport controls
  still drive on-screen legend/header sizes; `PDF Output -> Leg.` drives export
  legend size, and `PDF Output -> Hdr.` drives export scale/sheet-size header
  size. Both export defaults are `0.70x` and both can be edited separately in
  the `0.25x` to `3.0x` range.
- Added `Models/TakeoffAutoRoutingService.cs` for deterministic E-Wood takeoff
  label routing and sorting. Area labels such as `base`, `1st`, `2nd`,
  `deck`, `porch`, `blcny`, `cant`, `flat`, and `rf` route into
  `Takeoffs/sqfts`. Line labels such as `corners`, `ext`, `cor/corr`, `dem`,
  `2x8`, `2x6`, `2x4`, and `half` route into `Takeoffs/walls/{level} floor
  walls` when the active sheet/folder reveals a level.
- The same auto-sort order now applies to new routed items and to sheet-linked
  takeoff rows in the Pages tree. Page and linked-takeoff context menus now
  include `Sort Sheet Legend Auto` while preserving manual order and A-Z sort.
- The left Pages tree now exposes the same legend sorting rules as batch
  actions. Page-folder context menus can apply auto/A-Z/reset legend ordering
  to every sheet under that folder, and multi-selected sheets can be sorted or
  reset together from the page context menu. The actual auto order is shared
  through `TakeoffAutoRoutingService.SortPageLegendItems`, so left legend rows,
  viewport legends, and exported legends use the same label rules.
- Sheet legends now default to live auto ordering in the left Pages tree, the
  viewport legend, and PDF legend export. `Sort Sheet Legend Auto` clears the
  sheet back to auto mode instead of saving another fixed order list. Manual
  moves, drag/drop, and A-Z sort mark the sheet legend as `manual`; otherwise
  new `corners/ext/cor/dem/2x8` and `base/1st/2nd/deck/porch` rows insert in
  the right place without clicking `Legend Auto`.
- The right Takeoffs tree is not auto-sorted during normal creation or editing.
  Auto-routing can still choose the correct folder for a new label, but it no
  longer reorders existing right-side items. Legend/export ordering is computed
  separately so estimate/report/tree workflows keep their existing item order.
- Selected sheet workflows were extended: Pages context menu can open up to
  64 selected sheets in new tabs, detach selected sheets into separate
  read-only sheet windows, or tile those detached windows on monitor 2 when a
  second monitor is available. Detached sheet windows reuse `PdfViewport`,
  measurements, page annotations, hidden takeoff state, and sheet legend data.
- Sheet Manager now exposes the same selected-sheet open workflow from its
  multi-select table: `Open Tabs`, `Detach`, and `Tile M2` operate on the
  selected rows and reuse the same 64-sheet cap. Page tabs also have a context
  menu to detach the current tab or detach/tile all open page tabs. Tiled
  detached windows now shrink to the computed grid cell so a 64-window layout
  stays evenly distributed inside the selected monitor work area instead of
  overflowing because of the default window minimum size.
- The left Pages panel now also has visible `Tabs`, `Detach`, and `Tile M2`
  buttons above the Pages tree, so selected sheets can be opened or detached
  without discovering the right-click context menu first. The Desktop shortcut
  `C:\Users\User\Desktop\OurPlaneCore.lnk` was repointed to the fresh published
  build `publish/ourplanecore-working-single-20260508-1846/ourplanecore.exe`.
- The left Pages tree linked-takeoff rows now ignore saved manual legend order
  and always use the automatic E-Wood legend sort. This makes the left-side
  sheet legend fully automatic even for older jobs that had a stored manual
  legend order.
- 3D Massing marker selection was made more explicit. 3D marker pins are larger
  in both the main Massing preview and the separate 3D window; selected marker
  ids survive preview redraws; and footprint dots / labels in the 2D Massing
  preview can be clicked to select the source marker row.
- 3D Massing from takeoffs now recognizes the auto-routed `Takeoffs/sqfts`
  folder and direct floor-label items under it. Area takeoffs named `1st`,
  `2nd`, `3rd`, etc. now become separate draft footprint levels instead of
  being collapsed into level 1. Wall folders such as `4th floor walls` through
  higher numeric floors also parse into the matching 3D level.
- Takeoff-based 3D drafts now seed roof guides instead of stopping at a generic
  fallback cap. The top footprint creates an eave outline and candidate roof
  axis, which produces reviewable candidate roof planes until roof markers or
  manual roof review refine the roof type/pitch. The Massing summary now also
  shows a simple build system: source takeoffs/markers -> levels -> wall
  extrusion -> roof guides/planes. Takeoff measurement sources are shown as
  `takeoff` sources in the marker/source table instead of looking like missing
  AI marker JSON.
- Roof takeoff linking is now deterministic for common E-Wood folder/item
  names. `sqft`, `sqfts`, `sft`, `sf`, and square-foot variants can provide
  floor plates; `walls`/wall level folders can provide wall footprints; and
  `eave`/`eve`, `rake`, `gable`, and `gables` takeoffs are linked to the top
  footprint by source page or level context. The measured eave/rake/gable lines
  are converted into the same 3D coordinate space as the footprint and shown as
  reviewable roof guides. OpenAI remains a future fallback only for ambiguous
  naming/classification, not a hidden geometry solver.
- Added the first OpenAI-assisted `AI 3D Sort` workflow. It collects compact
  page, takeoff, measurement, folder, scale, and bounds metadata, sends that to
  OpenAI through a strict structured JSON schema, and expects a classification
  plan with roles such as `floor_plate`, `wall`, `eave`, `rake`, `gable`,
  `opening`, `ignore`, plus floor levels and confidence/reason text.
- `AI 3D Sort` is available from both the Massing panel and the `3D` manager.
  The workflow saves the request input, structured plan, and raw OpenAI
  response under `AI_Context/3d_massing/ai_takeoff_sort/` and
  `AI_Context/responses/`, then builds `AI_Context/3d_massing/model.json` with
  the same deterministic takeoff-based 3D draft service.
- The AI boundary is explicit: OpenAI sorts and explains takeoff roles/levels
  only. It does not create coordinates, hidden dimensions, geometry, or trusted
  estimating quantities. Ambiguous folders such as `misc` or combined
  `eve rake` can now be used when the model returns a reviewable plan, while
  the actual 3D model still comes from saved Area/Line measurements.
- Limitations kept explicit: detached sheet windows are viewer windows, not a
  second editing surface. 3D roof/wall generation is still reviewable draft
  geometry, not trusted estimating geometry; the next pass should keep improving
  the simple marker-to-draft workflow instead of hiding decisions in AI.
- Verification:
  `dotnet build .\ourplanecore.sln`
  passed with 0 warnings and 0 errors.
- Verification:
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj`
  passed with 108/108 tests.

## 2026-05-08 Legacy 3D Massing Disabled

- Archived the old 3D/Massing implementation before turning it off. Readable
  `.cs.txt` copies are in `docs/archive/3d_massing_legacy_2026_05_08/`, and
  the behavior map is in `docs/90-archive-prompts/ARCHIVED_3D_MASSING_LOGIC_2026_05_08.md`.
- Removed the visible legacy 3D entry points from AI Manager, AI Inbox,
  command palette, the right-side workspace tabs, and the shared `6 3D`
  workspace tab.
- Replaced the shared `6 3D` tab with a clean embedded WPF `Viewport3D`
  surface and basic camera controls. This viewer does not read
  `AI_Context/3d_massing/model.json` and does not call the old massing draft
  service.
- Left the old implementation files in place as reference for now, but legacy
  handlers return through `StopLegacy3DMassingWorkflow(...)` so stale calls do
  not build drafts, run AI 3D sort, auto-detect roofs, open the old detached
  window, or accept old drafts.
- Blocked saved legacy `roof_recognition_request` AI Inbox actions from
  running, previewing, or reviewing, so the old Auto Roof branch cannot
  re-enter the disabled 3D workflow through existing queued requests.

## 2026-05-08 3D Auto Walls and Slabs

- Added a new `Auto` build path in the right-side `3D` tab and shared `6 3D`
  workspace. It scans saved takeoff folders such as `walls/1st`, `walls/2nd`,
  `walls/3rd`, etc., builds line takeoffs into wall prisms, and stacks levels
  by using each floor's maximum parsed wall height as the next floor base.
- Added sqft slab display. Area takeoffs under `sqft` / `sqfts` are converted
  into horizontal floor plates and placed on the matching level elevation.
- Saved the generated/editable 3D model to `3D_Context/walls_model.json` inside
  the job, then reload it when the job opens so the 3D result does not vanish
  during normal tab refreshes.
- Added a compact 3D wall editor beside the viewport. Clicking a wall fills
  editable `height ft` and `width in` fields; values can be applied to just the
  selected segment or to its whole takeoff group. Level bases are then reflowed
  from the current maximum wall heights.
- Made the 3D viewport easier to orbit by handling drag/zoom from the whole
  viewport surface instead of only when the pointer is directly over geometry.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.
- Verification:
  `dotnet .\Tests\cache\verify_build\OurPlaneCore.Tests.dll`
  passed with 113/113 tests.

## 2026-05-08 3D Slab Mesh and View Controls Fix

- Replaced the simple fan triangulation used for sqft slabs with a dedicated
  ear-clipping polygon triangulator. Concave area takeoffs now render as their
  actual outline instead of drawing long diagonal artifacts from the first
  point to distant points.
- Added validation for crossing/self-intersecting slab outlines. Those are not
  rendered as misleading filled slabs; the 3D log now reports that the area
  point order should be checked.
- Added right-mouse panning to both the full `6 3D` viewport and the smaller
  right-side `3D` viewport. Left drag still orbits, wheel still zooms, and
  `Fit` recenters the target.
- Added a compact 3D log box under the right-side `3D` editor. It records model
  load/save/build messages and slab cleanup/skip messages without covering the
  viewport.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.
- Verification:
  `dotnet .\Tests\cache\verify_build\OurPlaneCore.Tests.dll`
  passed with 115/115 tests.

## 2026-05-08 3D Roof Guide Mode MVP

- Added a new reviewable roof workflow on top of the new `3D_Context` model,
  without re-enabling the archived legacy Massing workflow.
- Added right-click viewport entry under `3D > Roof Mode`, plus roof guide
  types for `Ridge`, `Hip`, `Valley`, `Eave`, `Rake`, and `Pitch`. In Roof
  Mode the normal sheet viewport records two-point guide lines with the same
  snap/ortho behavior used by takeoff drawing.
- Persisted roof guides and preview roof planes in `3D_Context/walls_model.json`
  next to the current wall/slab model. Auto wall/slab rebuilds now preserve
  the saved roof guide state instead of clearing it.
- Added roof controls to the right-side `3D` tab and the shared `6 3D` tab:
  `Roof`, `Build Roof`, and `Clear Roof`. The compact 3D log records saved
  guides and preview-build assumptions.
- Added 3D rendering for roof guide lines and a first preview builder. If a
  ridge guide exists, the preview builds two draft roof planes from the highest
  slab/wall boundary using a default 6:12 slope. If no ridge exists, it shows
  a flat cap preview and logs the missing guide.
- Kept limitations explicit: this is the first guided review surface, not the
  final roof solver. Hip/valley/eave/rake/pitch guides are saved and displayed
  now; the next pass should use them to split true roof planes and resolve
  intersections/collisions.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.
- Verification:
  `dotnet .\Tests\cache\verify_build\OurPlaneCore.Tests.dll`
  passed with 117/117 tests.

## 2026-05-08 3D Roof Guide Cleanup and Issue Markers

- Added deterministic cleanup before `Build Roof`: guide lines are treated as
  user hints, then snapped/straightened where safe. Endpoints can snap to the
  roof boundary, guide intersections, and nearby guide connections; lines
  outside the boundary are clipped when possible.
- Boundary snapping now prefers the boundary edge that matches the guide
  direction, so an eave drawn near a corner snaps to the eave edge instead of
  jumping to the nearest perpendicular side edge.
- Added persisted `RoofIssues` in `3D_Context/walls_model.json`. The cleanup
  reports crossing guide lines, outside-boundary lines, too-short guides, and
  dangling endpoints that do not connect to another guide or boundary.
- Added visible issue markers in the normal viewport and in the 3D viewer.
  Red markers are errors, yellow markers are warnings, and the compact 3D log
  reports how many guides were adjusted and how many issues remain.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.
- Verification:
  `dotnet .\Tests\cache\verify_build\OurPlaneCore.Tests.dll`
  passed with 121/121 tests.

## 2026-05-09 3D Roof Build Safety Fix

- Made `Build Roof` conservative when guides are incomplete or contradictory.
  If cleanup finds red roof-guide issues, the app now keeps the adjusted guide
  lines and visible issue markers, clears preview planes, and does not build
  misleading roof geometry.
- Removed the earlier fake flat-cap fallback when no `Ridge` guide exists.
  Missing ridge data now blocks plane generation with an explicit message
  instead of drawing a roof shape that looks valid but is not solved.
- Added a small `ThreeDRoofBuildService` pipeline so cleanup, blocking, issue
  preservation, and preview generation are testable outside the UI.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.
- Verification:
  `dotnet .\Tests\cache\verify_build\OurPlaneCore.Tests.dll`
  passed with 123/123 tests.

## 2026-05-09 3D Roof Guide Auto-Repair

- Relaxed the previous roof-build block. `Build Roof` now still generates the
  ridge preview planes when non-fatal guide markers remain, so one bad helper
  line no longer makes the whole roof disappear.
- Ridge guides now auto-extend to the roof footprint boundary during cleanup,
  so a ridge line that was not drawn all the way to the edge is completed
  before preview geometry is generated.
- Hip, valley, rake, and pitch guide branches that cross a ridge are trimmed
  to the ridge connection instead of being treated as a fatal crossing.
  Other mid-line guide crossings remain visible as review warnings.
- `rf` / `roof` area takeoffs under `sqfts` are now included in the 3D auto
  model as a `roof` slab at the top elevation. When present, this gives the
  roof preview a real roof footprint boundary instead of relying only on the
  highest floor slab.
- Multi-piece `RF` footprints are kept separate during `Build Roof`. Guide
  lines are assigned to the roof footprint polygon that contains them, or to
  the nearest roof polygon on the same page, so separate roof chunks and
  separate houses in one project do not get merged into one overlapping roof
  boundary.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.
- Verification:
  `dotnet .\Tests\cache\verify_build\OurPlaneCore.Tests.dll`
  passed with 125/125 tests.

## 2026-05-09 3D Auto Roof Button

- Added `Auto Roof` to both the right-side 3D panel and the shared `6 3D`
  workspace toolbar. It is the quick path: use existing `RF` / `roof` area
  takeoffs, create auto ridge guides, build the preview, and save the result.
- Auto roof guides are marked with `Status = auto_roof`, so pressing
  `Auto Roof` again replaces only the previous auto-generated ridge guides and
  leaves manually drawn ridge/hip/valley/eave/rake/pitch guides in place.
- Added `ThreeDRoofAutoGuideService`. It creates one center ridge per
  `RF`/`roof` footprint piece, falls back to the highest sqft slab when no roof
  area exists, and can fall back to wall extents if the model only has walls.
- Updated roof-region assignment so one guide can serve multiple nearby RF
  pieces. The builder now clones and clips that guide per roof piece, which
  lets split RF chunks belong to the same roof while still preventing far-away
  houses from being merged into one roof boundary.
- Verification:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  passed with 0 warnings and 0 errors.
- Verification:
  `dotnet .\Tests\cache\verify_build\OurPlaneCore.Tests.dll`
  passed with 127/127 tests.

## 2026-05-09 PlanSwift Project Import MVP Slice

- Added the first read-only PlanSwift project import path. The importer scans
  PlanSwift `Pages/**/Data.xml` and `Takeoff/**/Data.xml`, reads page GUIDs,
  `ScaleX`/`ScaleY`, `Scale Units`, item classes, colors, `PageGUID`, and
  `DigitizerData`, then creates a new OurPlaneCore job instead of modifying the
  original PlanSwift folder.
- Added `Models/Import/PlanSwiftProjectScanner.cs`,
  `PlanSwiftProjectImporter.cs`, `PlanSwiftImportModels.cs`,
  `PlanSwiftGeometryConverter.cs`, `PlanSwiftPagePdfWriter.cs`, and
  `PlanSwiftXml.cs`.
- Added `Tools/PlanSwiftImportTool` with `scan` and `import` commands so the
  migration can be run before a WPF UI is wired.
- The page path converts PlanSwift page images into single-page PDFs with the
  same bitmap dimensions, allowing existing `source.json`, PDF viewport, and
  `points_pdf` measurement rendering to be reused.
- Added `Tests/PlanSwiftImportTests.cs`, registered it in `Tests/Program.cs`,
  and kept the CLI project out of the WPF app compile in `ourplanecore.csproj`.
- Real scan proof on
  `C:\Program Files (x86)\PlanSwift10\Data\Storages\Local\Jobs\71. Mallory View_Rid`:
  187 pages, 412 measured takeoff items, 1676 measured sections, 18 warnings.
- Smoke imports proved one-page TIFF conversion, full page GUID mapping with
  placeholder pages, real measurement creation, and generated
  `import_reports/planswift_import_report.md`.
- Next queued work: wire `Import > PlanSwift Job...` in WPF, show scanner
  preview counts, let the user pick destination/name, run the importer with
  status, open the new job automatically, and surface the import report.
- Handoff details and exact resume commands are in
  `docs/40-planswift-product/PLANSWIFT_PROJECT_IMPORT_PLAN.md`.
- Verification:
  `dotnet build .\ourplanecore.sln`
  passed with 0 warnings and 0 errors.
- Verification:
  `dotnet C:\Users\User\Desktop\ourplanecore\Tests\bin\Debug\net9.0-windows\OurPlaneCore.Tests.dll`
  passed with 121/121 tests.

## 2026-05-09 Current 3D Status and Joist Area Rotation

- Wrote the current clean 3D roof state into
  `docs/50-3d-roof-ai/THREE_D_ROOF_SYSTEM_MAP.md`: walls, sqft slabs, RF/roof footprints,
  manual roof guides, `Build Roof`, `Auto Roof`, visible roof issues, saved
  `3D_Context/walls_model.json`, and the current limit that roof output is a
  reviewable candidate rather than a trusted final solver.
- Added a Joist Area setting, enabled by default, to rotate the saved joist
  direction when the area is rotated. This keeps joist lines visually aligned
  with the area after viewport rotate operations.
- Added `Joist Properties...` / `Use Area As Joists...` to the viewport
  right-click menu when the clicked measurement is an Area.
- Persisted the rotate-with-area flag on takeoff items and measurements, and
  copied it through measurement clipboard/new takeoff paste paths.
- Updated viewport transform undo so Ctrl+Z restores both the area geometry
  and the previous joist direction angle.
- Verification:
  `dotnet build .\ourplanecore.sln`
  passed with 0 warnings and 0 errors.
- Verification:
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj`
  passed with 135/135 tests.

## 2026-05-10 Takeoffs Tree Move Performance Handoff

- Continued optimizing the right-side Takeoffs tree drag/drop and bulk move
  workflow: fast UI subtree moves, path-indexed tree lookup, stable Pages-style
  drop stripes, targeted selection repaint, skipped pure-reorder legend refresh,
  and batch page-legend rebasing for multi-node moves.
- Latest verification during the slice:
  `dotnet build .\ourplanecore.sln` passed clean and
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj` passed `145/145`.
- Resume details, exact files, next optimization targets, and approval-noise
  note are in `docs/30-takeoffs-measurements/TAKEOFF_TREE_PERFORMANCE_HANDOFF_2026_05_10.md`.

## 2026-05-10 Sheet Notes and Viewport Smoke Handoff

- Paused the current code pass to write down the requested scope before further
  editing: add a sheet `Note` markup tool and smoke-test high-zoom Area/drag
  responsiveness.
- The user clarified that Select itself works normally; the lag investigation
  should focus on high-zoom interaction cost, not a broken Select command.
- Added the `Note` markup tool: toolbar button, `N` shortcut, command palette
  entry, multiline note prompt, wrapped sheet rendering, move/resize, right-click
  `Edit Note...`, `annotations.json` persistence, undo text restore, and PDF
  export rendering.
- Reduced high-zoom live interaction cost by hiding expensive overlay
  labels/details during drag/draw frames and limiting snap intersection
  candidate segments to the pointer search rectangle.
- Verification passed:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`,
  `dotnet .\Tests\cache\verify_build\OurPlaneCore.Tests.dll` (`145/145`),
  and `dotnet build .\ourplanecore.sln`.
- UI smoke opened current LastPage `A1.00-A base`, performed wheel zoom and
  middle-button pan, and confirmed the app stayed responsive. The stock
  `run-viewport-zoom-smoke.cmd` was blocked in this sandbox because it writes
  `%APPDATA%` settings during setup.
- Shortcut `C:\Users\User\Desktop\OurPlaneCore.lnk` was updated to the fresh
  Debug exe.
- Scope, touched files, suspected lag cause, and verification details are in
  `docs/20-import-pages-metadata/SHEET_NOTES_AND_VIEWPORT_SMOKE_HANDOFF_2026_05_10.md`.

## 2026-05-10 Takeoffs Tree Post-Drop Optimization

- Removed the next post-drop cost in the right-side Takeoffs tree: fast
  move/reorder now restores selection silently instead of re-entering the full
  `TakeoffsTree_SelectedItemChanged` path.
- The fast path now updates only moved/active takeoff rows, active target state,
  and, for real folder-path rebases, only page takeoff indicator rows for sheets
  that contain measurements from the moved takeoffs.
- Page takeoff linked-selection keys are rebased with moved takeoff paths, so
  page-side linked highlights do not retain stale source paths after drag/drop.
- Verification passed:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`,
  `dotnet .\Tests\cache\verify_build\OurPlaneCore.Tests.dll` (`145/145`), and
  `dotnet build .\ourplanecore.sln`.
- Shortcut `C:\Users\User\Desktop\OurPlaneCore.lnk` was updated to the fresh
  Debug exe.

## 2026-05-10 Takeoffs Move Smoke and Settings Isolation

- Added an app-side Takeoffs move smoke hook and `run-takeoffs-tree-smoke.cmd`.
  The smoke creates a temp job, opens it through an isolated settings file,
  moves `Smoke Wall B` into `Smoke Target Folder`, moves it back to the
  Takeoffs root, and verifies filesystem plus UI tree state.
- Added `OURPLANECORE_SETTINGS_PATH` so smoke runs do not overwrite the user's
  real `%APPDATA%\OurPlaneCore\settings.json`.
- Made atomic writes use unique temp files and made the global AI project
  registry update non-blocking, so a locked shared index cannot prevent a job
  from opening.
- Verification passed:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`,
  `dotnet .\Tests\cache\verify_build\OurPlaneCore.Tests.dll` (`147/147`), and
  `.\run-takeoffs-tree-smoke.cmd -TimeoutSeconds 45`.

## 2026-05-10 Rotatable Notes and Snap Prefilter

- Made sheet `Note` markups use four persisted corner points for new notes.
  Existing two-point notes stay readable and convert to real corner geometry
  when transformed.
- Rotation/scale/mirror now preserves note/box corners instead of collapsing
  them back to an axis-aligned bounding box.
- Viewport drawing and PDF export render note bodies and wrapped note text in
  the note's transformed local frame, so rotated notes remain visible on sheet
  and in exported PDFs.
- Added a snap prefilter for Line/Area interaction: measurement and markup
  geometry outside the small pointer search rectangle is skipped before midpoint
  and segment-intersection candidates are considered.
- Made measurement visibility culling screen-relative instead of using a fixed
  PDF-point padding. At high zoom, the viewport now keeps roughly the same
  off-screen screen margin instead of drawing a much wider PDF-space region.
- Added throttled AppLog diagnostics for real-job viewport hitches: slow frames
  now include timing buckets for PDF bitmap, overlay, measurements, markups,
  live drawing, labels, and chrome; slow snap searches include candidate and
  skipped measurement/annotation counts.
- Tightened normal snap search so endpoint and midpoint candidates outside the
  pointer search rectangle are ignored before distance checks, and segment
  intersection work starts with a cheap segment-bounds test. This keeps Select,
  Line, and Area hover/placement lighter on dense sheets and large polygons.
- Added an active-page measurement spatial index. Normal Snap, Select box,
  measurement body hit-test, editable vertex hit-test, and Area Cut target
  lookup now query nearby measurement bounds first instead of walking every
  visible takeoff on every pointer action. Measurement add/remove/change paths
  invalidate the index so edits keep current geometry.
- Routed measurement rendering through the same active-page spatial index. At
  high zoom the draw pass now asks for measurements near the visible PDF rect
  plus the configured screen-relative padding instead of walking the full
  active-sheet list before visibility culling. Selected measurements are still
  preserved in the render candidate set.
- Extended the spatial index with measurement vertex and segment cells. Normal
  Snap now pulls nearby vertices/segments directly, and body/vertex hit-testing
  uses nearby geometry candidates before falling back to Area fill checks. This
  avoids walking every segment inside large Line/Area takeoffs when only a tiny
  pointer rectangle is relevant.
- Verification passed:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  and `dotnet .\Tests\cache\verify_build\OurPlaneCore.Tests.dll` (`150/150`).

## 2026-05-10 Future Online / Web Companion Idea

- Captured the future idea of making an online/browser version after the
  Android discussion.
- Preferred direction: build a web companion before attempting a full mobile or
  SaaS replacement.
- First useful slice: browser project/sheet viewer with PDF.js, zoom/pan, and
  read-only takeoff/measurement display.
- Next slice: browser takeoff MVP with Count, Linear, Area, scale handling, and
  save-back to the shared project format.
- Keep the Windows WPF app as the primary production tool while this is
  explored; leave report builder, complex import/export, AI review, and 3D
  massing on desktop until the web surface proves useful.
- Also added the idea to `docs/OURPLANECORE_TASK_ROADMAP.md` under `Future
  Online / Web Companion Idea`.

## 2026-05-11 PDF Output, PlanSwift Import, and Materials Recovery

- Raised all PDF Output scale controls and export clamps to allow values up to
  `10`: Stroke, Point, Label, Legend, and Header. The export dialog and PDF
  renderer now accept the same upper limit instead of silently clamping lower.
- Fixed PlanSwift import page-size normalization for oversized raster sheets:
  imported TIFF/PNG sheet images are written into PDF space using their physical
  DPI size, while measurement coordinates and `ScaleMetersPerPt` are adjusted
  together so measured lengths/areas stay unchanged.
- Added regression coverage for oversized PlanSwift raster imports so a sheet
  such as a 100 x 66.67 raster can normalize back to a 36 x 24 PDF page without
  losing measurement value correctness.
- Added Takeoffs tree regression coverage around disappearing tree items and
  importer-created content, including nested mixed items, corrupt measurement
  recovery, page lookup safety, and exact-only page repair behavior.
- Updated PlanSwift import to skip PlanSwift pages that have no real takeoff
  sections or segment sections with points. Empty sheets no longer enter the
  Pages tree just because they exist in the source job.
- Cleaned the generated Materials Report output so the visible report no longer
  prints the technical quality/input-PDF/detected-schedule summary page.
- Fixed dotted sheet metadata such as `A5.03` and `S2.01`: both the Python PDF
  metadata extractor and C# normalization now preserve the dotted sheet label
  instead of collapsing it to the base label such as `A5`.
- Removed automatic Materials Report creation from both PDF import and
  PlanSwift import. Import still keeps sheet auto naming/scaling behavior where
  applicable, but Materials report generation now happens only from the manual
  `Materials -> Report Sheet` command.
- Verification during the recovered session reached:
  `dotnet build .\ourplanecore.sln`,
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj`, and
  `python -m py_compile Tools\pdf_layers_helper.py`, with the final test count
  reported as `176/176`.

## 2026-05-12 Interrupted Session Recovery

- Recovered the interrupted 2026-05-11/2026-05-12 context from session logs and
  the current working tree. The active uncommitted scope is broad and includes
  PlanSwift import, PDF import, material extraction/reporting, takeoff tree
  regression tests, viewport rendering/indexing, Report Builder, and 3D roof/
  wall work.
- Found a still-running `ourplanecore.exe` process after the interruption and
  closed it before verification so the debug build output would not be locked.
- Current verification after recovery passed:
  `git diff --check`,
  conflict-marker/`NotImplementedException` scan,
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`,
  `dotnet .\Tests\cache\verify_build\OurPlaneCore.Tests.dll` (`176/176`), and
  `python -m py_compile Tools\pdf_layers_helper.py Tools\material_extractor.py`.
- Remaining repo hygiene issue: the working tree still has many untracked
  bundled runtime/tool files under `Tools/python`, `Tools/python_deps`,
  `Tools/tesseract`, and `Tools/PlanSwiftImportTool`. These are referenced by
  the project file for packaging, but should be reviewed before staging so
  generated `bin`/`obj`/`cache` output and bulky runtime payload are handled
  intentionally.

## 2026-05-12 AI Fill Crop Hints and Quick Crop Notes

- Extended Sheet Manager / AI Fill fallback for unresolved sheet metadata. If
  deterministic PDF text/layer extraction cannot find sheet number or scale and
  no saved crop template exists, AI Fill offers to open a representative sheet
  and let the user draw crop boxes for `Sheet #` and `Scale`.
- Added `Dialogs/PdfMetadataCropTemplateDialog.cs`, a WPF image preview dialog
  for drawing reusable crop regions on one sheet.
- Added job-local crop-template persistence in
  `Models/PdfSheetMetadataCropService.cs`. The template is saved under
  `AI_Context/sheet_metadata_crop_template.json` and is applied to every target
  sheet during metadata fallback.
- Metadata fallback requests can now attach separate crop roles:
  `sheet_number`, `scale`, and `title_block`. The prompt tells the model which
  crop to use for `sheet_label` / `sheet_key`, which crop to use for
  `selected_scale_text`, and how to use the title-block crop for title/suffix
  context.
- Kept the old bottom-title-block crop as fallback when no manual crop hints
  exist or the template crop cannot be saved for a page.
- Added visible `Crop Hints` commands in the top PDF toolbar and Sheet Manager
  toolbar.
- Added right-click `AI crop here -> note` for blank sheet context and
  measurement context. It saves a context crop, queues a
  `quick_crop_note_request`, runs it with `gpt-5-mini` when an OpenAI key is
  available, and places a visible `Note` markup next to the clicked crop.
- The quick crop-note prompt preserves readable sheet content: tables/schedules
  as compact Markdown tables, callouts/key notes with line breaks, and
  `[unreadable]` for bad crops.
- Added `PdfViewport.AddNoteAnnotationAt` so AI workflows can create a real
  persisted note annotation without going through the manual note dialog.
- Added `Tests/PdfSheetMetadataCropServiceTests.cs` and registered template
  persistence/usability coverage.
- Detailed handoff:
  `docs/50-3d-roof-ai/AI_FILL_CROP_HINTS_AND_NOTES_HANDOFF_2026_05_12.md`.
- Verification passed:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`
  and `dotnet .\Tests\cache\verify_build\OurPlaneCore.Tests.dll` (`178/178`).
- Manual GUI smoke was not run in this slice because the previous local session
  froze the machine. The next UI check should be small: one known PDF job, one
  AI Fill crop-template save, and one `AI crop here -> note` test.

## 2026-05-12 Future AI Auto Trace / Facade Detection Idea

- Captured the future idea of using the cheapest practical vision model for
  reviewable auto-tracing tasks such as finding facade windows, wall runs,
  openings, doors, repeated labels, and rough area outlines.
- Preferred cost-first model plan:
  `gpt-5.4-nano` for first-pass vision candidate detection,
  optional `gpt-5-nano` for cheapest acceptable mode, and `gpt-5.4-mini` only
  for difficult or low-confidence crops.
- Boundary decision: the model should return structured candidate JSON with
  boxes/polygons/confidence, not trusted final takeoff geometry. Local PDF
  vector geometry, snapping, OpenCV/edge detection, and existing viewport
  tools should refine contours before the user accepts them.
- Added the implementation idea and open questions to
  `docs/OURPLANECORE_TASK_ROADMAP.md` under
  `Future AI Auto Trace / Facade Detection Idea`.

## 2026-05-12 Auto Trace Areas and Walls Spec

- Wrote the full planning spec for a reviewable area/wall/opening trace system:
  `docs/30-takeoffs-measurements/AUTO_TRACE_AREAS_AND_WALLS_SPEC_2026_05_12.md`.
- Scope covers manual trace assist, seeded vector area trace, seeded wall-run
  trace, plan wall area from length x height, facade/elevation area trace,
  opening detection, and cross-sheet batch trace.
- The core design keeps accepted output as normal `Measurement` records while
  introducing review-only `TraceBatch` / `TraceCandidate` data before anything
  is applied.
- The spec separates vector trace, layer trace, raster trace, and AI trace.
  AI is scoped to candidate classification/boxes/rough points; local geometry
  and user review remain responsible for final takeoff geometry.
- Recommended implementation order is candidate/review/apply infrastructure
  first, then vector geometry, seeded area trace, wall trace, raster crop
  trace, AI opening detection, batch trace, and feedback learning.
- Linked the spec from `docs/OURPLANECORE_TASK_ROADMAP.md` under the future AI
  auto trace/facade detection section.

## 2026-05-13 Page Folder Scoped Sort and Notes Move Fix

- Added folder-scoped Pages context-menu organization commands:
  `Sort A/S in This Folder` and `Sort D/Sec/WT in This Folder`.
- Folder-scoped A/S sorting now creates/reuses `Arch`, `Struct`, and
  `--------others` inside the selected Pages folder and moves only sheets under
  that folder/branch.
- Folder-scoped D/Sec/WT sorting now creates/reuses `details struct`,
  `details arch`, `units`, and `sections` inside the selected Pages folder and
  moves only sheets under that folder/branch.
- Fixed page note/markup annotations disappearing after moving a sheet. The
  annotation sidecar still lives beside the page, but load/save now treats the
  current page folder as authoritative instead of trusting stale serialized
  `PageFolder` values inside `annotations.json`.
- Added storage regression coverage:
  `PageAnnotationsFollowMovedPageFolder`.
- Updated the local user-facing package and Desktop shortcut after verification.
- Detailed handoff:
  `docs/20-import-pages-metadata/PAGE_FOLDER_SORT_AND_NOTES_HANDOFF_2026_05_13.md`.
- Verification passed:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`,
  `dotnet .\Tests\cache\verify_build\OurPlaneCore.Tests.dll` (`183/183`),
  then the package-refresh workflow with normal `dotnet build`,
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build`
  (`183/183`), Release publish, update-folder replacement, Desktop shortcut
  refresh, and SHA256 match.

## 2026-05-16 PDF Render and Layers Performance Pass

- Investigated severe page/viewport lag in the live packaged app and compared
  the behavior against public PlanSwift docs. PlanSwift exposes PDF-to-TIF
  conversion with DPI/grayscale options and import-time conversion settings, so
  the practical direction is to keep expensive PDF conversion/analysis explicit
  instead of running it on every page open.
- Removed automatic PDF layer discovery from the normal page-open path.
  `Controls/PdfViewport.Layers.cs` now treats layers as unloaded by default and
  exposes `DiscoverPdfLayersOnDemand()`.
- Added a `Load` button in the PDF Layers tab and wired it through
  `MainWindow.PdfLayers.cs`, so the active page's PDF layers are scanned only
  when the user asks for them.
- Changed PDF import so it no longer scans layer metadata for every imported
  page by default; the import status now explains that PDF layers load on
  demand from the PDF Layers tab.
- Reduced viewport render-cache pressure in
  `Controls/PdfViewport.RenderCache.cs` by bounding the shared docnet bitmap
  cache to 8 entries and about 220 MB.
- Made future Page Tools raster PDF output lighter in
  `Models/PageImageOperationService.cs` by reducing render scale to `1.5f` and
  setting PDF encoding quality to `72`.
- Detailed handoff:
  `docs/10-performance-render/PERFORMANCE_RENDER_AND_LAYERS_HANDOFF_2026_05_16.md`.
- Verification passed:
  `dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false`,
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj /p:OutDir=.\cache\test_run\ /p:UseAppHost=false`,
  then the package-refresh workflow with normal `dotnet build`,
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build`
  (`195/195`), Release publish, update-folder replacement, Desktop shortcut
  retargeting to `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`,
  and SHA256 match
  `826A6231C146B69867BFA2579F2FED7EE4D0655751DCE2B6439B62063518235C`.

## 2026-05-21 3D Roof Render and Desktop Package Pass

- Documented the current 3D roof render state in
  `docs/50-3d-roof-ai/3D_ROOF_RENDER_HANDOFF_2026_05_21.md`.
- Current roof render behavior removes visible internal planar lines by avoiding
  reverse duplicate coplanar roof triangles and drawing extra roof edge bars
  only on outer boundary edges.
- Roof faces now use a dedicated visible color material path with a light
  emissive component, almost-solid opacity, and upward-facing flat normals so
  surfaces do not read as gray or broken under WPF 3D lighting.
- Confirmed the Desktop shortcut target:
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`.
- Verified the fresh packaged app, not only Debug output:
  `dotnet build .\ourplanecore.sln` (`0 warnings / 0 errors`),
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build`
  (`212/212`), compressed single-file Release publish, update-folder
  replacement, shortcut retargeting, SHA256 match
  `DAD7F17E135ED338EB2967F608B5CD27053C0F1B8125E5C9C7038B0AB049168C`,
  and packaged-exe startup log check with no `ERROR` after the last
  `Application startup.` marker.

## 2026-05-22 PDF Import Sheet Metadata Pass

- Improved PDF import auto-name/auto-scale behavior for E-Wood plan sets.
- `Tools/pdf_layers_helper.py` now prefers right/bottom title-block sheet
  number, PDF page labels, and split-PDF filename ids before fallback text.
- Removed the broad global sheet-number fallback that could pick detail callouts
  instead of real sheet ids.
- Sheet names are now normalized to lowercase in both the Python helper and
  `Models/PdfSheetMetadataService.cs`.
- Added stronger suffix and scale handling for level sheets, U/unit/kitchen/bath
  sheets, roofs, elevations, sections, details/profiles, notes, schedules, wall
  types, and floor types.
- Added engineering-scale parsing/formatting for scales like `1" = 20'0"`.
- Added regression coverage for lowercase dotted sheet names and engineering
  scale parsing.
- Current sample job validation:
  `C:\Users\User\Desktop\Takeof_desctop\84. Main Str Hempstead_Neil_EBS\sources`
  with 374 source PDFs produced zero duplicate rename candidates, zero uppercase
  rename candidates, and zero missing scale on scale-capable sheets. The only
  blank label was `Division-06.pdf`, which is not a normal sheet PDF.
- Broader sample audit:
  `E:\---\Work\E-Wood-Work\2.New_Projects` had 502 PDFs / 7403 pages; 30
  spec/manual PDFs were skipped, and 472 plan-like PDFs / 3621 pages were
  scanned. Remaining blanks are mostly scanned/no-text or weak-title-block PDFs
  that need OCR/image fallback rather than looser regex.
- Detailed handoff:
  `docs/20-import-pages-metadata/PDF_IMPORT_SHEET_METADATA_HANDOFF_2026_05_22.md`.
- Verification passed:
  `python -m py_compile .\Tools\pdf_layers_helper.py`,
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj` (`222/222`),
  `dotnet build .\ourplanecore.sln` (`0 warnings / 0 errors`), compressed
  single-file Release publish, update-folder replacement, Desktop shortcut
  retargeting to `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`,
  SHA256 match
  `EB3625C5E3F7F671B7769D2470C742D0A064B5C33AD648466EB4D3C6840796C7`, and
  packaged-exe startup log check with no `ERROR` after the last
  `Application startup.` marker.

## 2026-05-22 PDF Import Shear Wall Rule

- Added shear-wall sheet metadata rule: any sheet where extracted PDF text,
  title, or filename mentions `SHEAR` / `SHEAR WALL` now gets suffix `shw`.
- `shw` is scale-capable for plan/elevation sheets, but schedule/detail shear
  sheets keep `skip_scale`.
- Page sorting now detects suffix `shw` and routes it to folder `shear walls`.
- PageSort config schema upgraded to version `4`, so existing saved PageSort
  presets receive `shw -> shear walls` once, without re-adding it after the user
  edits and saves the current schema.
- Applied to current job
  `C:\Users\User\Desktop\Takeof_desctop\84. Main Str Hempstead_Neil_EBS` after
  snapshot `.snapshots\20260522_181149_before_shear_wall_rules`:
  - `400` pages reanalyzed;
  - `42` sheets renamed/moved to `00. imported\shear walls`;
  - `41` scale fields updated or cleared according to sheet type;
  - remaining `shw` outside `shear walls`: `0`;
  - errors: `0`.
- Verification passed:
  `python -m py_compile .\Tools\pdf_layers_helper.py`,
  direct helper suffix smoke cases,
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj` (`224/224`),
  `dotnet build .\ourplanecore.sln` (`0 warnings / 0 errors`), compressed
  single-file publish/deploy to
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`, SHA256
  `A7A73CFC7B51B6B6AE6D3BD44267BDEF30FDCE2A6B8EE060DF79D617BFDA5632`, and
  packaged-exe startup log check with no `ERROR` after the last
  `Application startup.` marker.

## 2026-05-22 PDF Import Shear False Positive Fix

- Tightened the `shw` suffix rule after the current job showed normal
  structural detail sheets, such as `WOOD FRAMING SECTIONS AND DETAILS`, being
  filed as shear walls only because their body text mentioned shear.
- New behavior:
  - title or filename explicitly mentioning `SHEAR` / `SHEAR WALL` still forces
    suffix `shw`;
  - body text mentioning shear only counts for plan-like sheet titles
    (`plan`, `framing`, `bracing`, `floor`, `roof`, `foundation`);
  - detail and schedule titles do not become `shw` from body text alone.
- Applied repair to current job
  `C:\Users\User\Desktop\Takeof_desctop\84. Main Str Hempstead_Neil_EBS` after
  snapshot `.snapshots\20260522_183456_before_shear_false_positive_repair`:
  - `400` pages rechecked;
  - `33` false `shw` pages renamed/moved back to their proper suffix/folder;
  - current `shw` pages: `9`;
  - current `shw` pages outside `shear walls`: `0`;
  - `WOOD FRAMING ... DETAILS` pages still marked `shw`: `0`;
  - errors: `0`.
- Verification passed:
  direct helper suffix smoke cases,
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build`
  (`224/224`), `dotnet build .\ourplanecore.sln` (`0 warnings / 0 errors`),
  compressed single-file publish/deploy to
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`, SHA256
  `B8506CB2289DE8641CA4F19AA48243D8B3AB5F5F37A2E165DE6963F3E0A07629`.
- Packaged launch validation:
  - hidden verification launch: process alive, `0` errors after latest
    `Application startup`, `Loaded takeoffs` and `Viewport` signals present;
  - visible user launch left running from the update exe: process alive,
    `0` errors after latest `Application startup`, job title loaded.

## 2026-05-22 PDF Import Bracing Shear Correction

- Adjusted the second-pass shear fix after reviewing the current job again:
  `BRACING PLAN` sheets can be legitimate shear wall sheets even when the sheet
  title does not literally say `SHEAR WALL`.
- Final rule:
  - title or filename saying `SHEAR` / `SHEAR WALL` -> `shw`;
  - `BRACING PLAN` with visible shear wall callouts in the PDF body -> `shw`;
  - roof/framing/foundation sheets do not become `shw` from body notes like
    `SEE S6.## SERIES`;
  - detail and schedule titles still do not become `shw` from body text alone.
- Applied repair to current job
  `C:\Users\User\Desktop\Takeof_desctop\84. Main Str Hempstead_Neil_EBS` after
  snapshots:
  `.snapshots\20260522_184353_before_shear_body_only_repair` and
  `.snapshots\20260522_184601_before_restore_bracing_shear_walls`.
- Current job result:
  - total pages checked: `400`;
  - current `shw` pages: `4`;
  - `shw` titles: `CONSTRUCTION BRACING PLAN - PART A/B/C/D`;
  - roof/foundation/plain framing sheets still marked `shw`: `0`.
- Verification passed:
  `python -m py_compile .\Tools\pdf_layers_helper.py`,
  direct helper smoke cases,
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build`
  (`224/224`), `dotnet build .\ourplanecore.sln` (`0 warnings / 0 errors`),
  compressed single-file publish/deploy to
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`, SHA256
  `0A26852D7B4906C3DE64162376A4CE544F80EE3BBB49E9E2C5FD2B3B9B9503CE`.
- Packaged launch validation:
  - hidden launch: process alive, `0` errors after latest `Application startup`,
    `Loaded takeoffs` present;
  - visible launch left running from the update exe: process alive, window
    title `OurPlaneCore — 84. Main Str Hempstead_Neil_EBS`, responding, `0`
    errors after latest `Application startup`.

## 2026-05-22 PDF Import Detail/Finish Folder Rules

- Added structural detail priority for sheet metadata: `S...` sheets with
  `DETAIL`, `DETAILS`, `DEATIL`, or `DETIAL` now force suffix `d` before
  schedule/roof/section rules, so `SECTIONS AND DETAILS` does not file into
  `sections`.
- Added architectural finish/interior rule: `A...` sheets with `FINISH`,
  `FINISHES`, or `INTERIOR` now force suffix `f`.
- Added page-sort default/migration rules:
  - detect suffix `f`;
  - route `a ... f` to `finish`;
  - route M/P/E/C first-letter sheets and MEP/mechanical/plumbing/electrical/
    civil source filenames to `Others`.
- Existing saved PageSort presets are upgraded through
  `PageSortConfig.SchemaVersion`, without re-adding a rule after the user
  removes and saves it under the current schema.
- Pages Tree folder rows now show recursive sheet counts next to each folder.
- Applied the new rules to current job
  `C:\Users\User\Desktop\Takeof_desctop\84. Main Str Hempstead_Neil_EBS` after
  snapshot
  `.snapshots\20260522_180216_before_finish_detail_mep_rules`:
  - `400` pages checked;
  - `46` pages reanalyzed/renamed/moved;
  - `43` `S` details moved to `00. imported\details struct`;
  - `3` `A` finish/interior sheets moved to `00. imported\finish`;
  - remaining wrong `S + details != d`: `0`;
  - remaining wrong `A + finish/interior != f`: `0`.
- Verification passed:
  `python -m py_compile .\Tools\pdf_layers_helper.py`,
  `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build`
  (`224/224`), `dotnet build .\ourplanecore.sln` (`0 warnings / 0 errors`),
  compressed single-file publish/deploy to
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`, SHA256
  `B491A7E15B6CB38EA7225EB4A014E8AF0199162715CC89C7404481FD7CA5CA0F`, and
  packaged-exe startup log check with no `ERROR` after the last
  `Application startup.` marker.
