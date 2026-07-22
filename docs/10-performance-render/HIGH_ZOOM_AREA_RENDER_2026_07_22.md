# High-Zoom Area Rendering — 2026-07-22

## Outcome

OurPlanCore `2.2.5` removes the full static-sheet resample from every Area
mouse move. The page stays a fixed 150-DPI raster, while the optional black PDF
vector overlay remains crisp and is retained in the same screen frame. Live
measurements, Area rubber-band geometry, snap previews, labels, and cursor UI
continue to render above that retained background.

Permanent application download:

`https://github.com/artrmiys/ourplanecore/releases/latest/download/ourplancore.exe`

## Confirmed Cause

The slow sheet was not waiting for a PDF render, a detail tile, or snap
extraction. Its persisted page raster was a legacy 200-DPI bitmap measuring
`8400 x 6000` pixels even though the current static-sheet target was 150 DPI.
At zooms `2.667`, `4.0`, and `5.334`, the software `SKElement` repainted and
resampled that source on every raw pointer update.

Observed slow frames before the fix were `45–59 ms`; `44–59 ms` of each frame
was the page/background pass. Measurements and in-progress Area geometry were
only `0–1 ms`. The process consumed approximately one CPU core while the mouse
moved. Area rubber-band and snap-preview paths also bypassed the existing
pointer cadence with direct repaint requests.

## Implemented Render Path

### Retained static page frame

`Controls/PdfViewport.StaticPageFrameCache.cs` owns a screen-sized retained
bitmap used only while the static raster display is active. Its order is:

1. fixed page raster and page tint;
2. low-zoom linework or black PDF vector overlay;
3. ordinary Sheet Overlay;
4. live measurements, annotations, Area preview, snaps, labels, and cursor UI.

The retained bitmap is replayed 1:1 in device pixels, so it does not add a
second filtered scale. The original `SKElement` DPI matrix is baked into the
frame and restored for dynamic geometry.

The cache key includes the page bitmap generation and identity, canvas size,
DPI matrix, zoom/pan, page and view colors, mip state, black-vector state and
content generation, and the complete Sheet Overlay transform/bitmap state.
PDF Layer Trace and Sheet Overlay edit/drag modes deliberately stay on the
original dynamic path.

Allocation is guarded. An unavailable/not-ready `SKBitmap` is disposed, logged
with throttling, and retried after a delay while drawing through the previous
direct path. The retained frame is capped at 40 million pixels and is disposed
on page replacement and viewport unload. Static mode ignores stale live detail
tiles instead of baking them into a pinned frame.

### Exact fixed DPI

`StaticRasterPrefetchPolicy.RequiresPinnedDpiMigration()` now treats the
configured DPI as exact. Existing `200 -> 150` and `144 -> 150` rasters migrate
once, while `150 -> 150` remains pinned without a rebuild loop. Upward changes,
such as `150 -> 300`, still work. The existing page-pixel safety clamp remains
in effect for oversized sheets.

This does not add dynamic zoom tiles, live DPI ladders, or background detail
loading. Static mode remains one fixed page raster.

### Pointer cadence

Area/Line/Scale/joist-direction snap and rubber-band previews now share one
16-ms render-priority cadence with a trailing frame. Raw high-rate mouse events
coalesce, but the final pointer position is not lost. Clicks, `Tab`, completion,
and cancellation remain immediate.

### Black PDF overlay

The `Black vector` display setting remains supported. Its loaded PDF segments
are drawn into the retained background after the raster, so deep zoom keeps
black linework crisp without rebuilding it for each Area movement. Async
segment arrival invalidates a previously blank frame. Detached sheet windows
now receive the same black-vector setting as the main viewport.

## Automated Performance Proof

`Tools/ui_viewport_area_preview_smoke.ps1` creates an isolated temporary job
and a vector `42 x 30 in` PDF. At 150 DPI its fixed raster is
`6300 x 4500`. The launcher uses a temporary settings file, moves no system
cursor, finalizes no measurement, writes nothing to a real job, and removes
its temporary workspace.

The in-process probe warms the retained frame, creates transient Area points,
and exercises the normal pointer repaint path at zoom `4.0` and `5.334`. A run
passes only when every zoom has:

- exact current/target raster DPI of 150;
- black vector enabled with real loaded segments;
- retained-frame hit rate at least 90%;
- no more than one miss/bypass;
- p95 full-frame time at most 33 ms;
- p95 page-frame time at most 24 ms.

Final Debug candidate result:

| Zoom | Frame hits | Miss/bypass | p95 frame | p95 page | Raster | Black segments |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 4.000 | 100% | 0 | 1 ms | 0 ms | 150 DPI | 44 |
| 5.334 | 100% | 0 | 1 ms | 0 ms | 150 DPI | 44 |

Report: `cache/perf_baseline/debug-v2.2.5-area-preview.json`.

## Regression and Release Evidence

- Source commit: `ae35c42ed19bad4863a32395ea8c8bdc75e9f974`.
- Release tag: `ourplancore-v2.2.5-20260722-ae35c42`.
- Release page:
  `https://github.com/artrmiys/ourplanecore/releases/tag/ourplancore-v2.2.5-20260722-ae35c42`.
- Release build: `0 warnings / 0 errors`.
- C# regression harness: `597/597` passed.
- Precise sheet metadata Python suite: `24/24` passed.
- Installed ProductVersion:
  `2.2.5+ae35c42ed19bad4863a32395ea8c8bdc75e9f974`.
- Installed compressed single-file size: `171,825,374` bytes.
- Installed and public-latest SHA-256:
  `DCBD30986A276709C671212A90CDDAE73E2A4F6E3297E6F2655A4A5F9CA02109`.
- Rollback:
  `Desktop\updates\OurPlanCore\ourplancore.exe.bak-20260722-184032-73df3b45d9a6`.
- Desktop shortcut target and working directory both point to
  `Desktop\updates\OurPlanCore`.
- Fresh packaged startup begins at line `28284` of
  `%APPDATA%\OurPlanCore\logs\app-20260722-1.log`; the checked segment has
  `0 ERROR`, one `Loaded takeoffs`, and seven `Viewport` records.
- The installed package remained alive as PID `21376` after the 20-second
  runtime check.
- An independent anonymous download from the permanent latest URL reproduced
  both the installed length and SHA-256.

## Ownership

- Retained page frame: `Controls/PdfViewport.StaticPageFrameCache.cs`.
- Static frame integration/timing: `Controls/PdfViewport.Rendering.cs`.
- Pointer cadence: `Controls/PdfViewport.Input.cs`,
  `Controls/PdfViewport.DigitizerSnap.cs`, and
  `Controls/PdfViewport.EdgeSnap.cs`.
- Exact DPI migration: `Models/StaticRasterPrefetchPolicy.cs` and
  `Controls/PdfViewport.RasterSheetDpiUpgrade.cs`.
- Black-vector content invalidation: `Controls/PdfViewport.PdfSnap.cs`.
- Diagnostic recorder/probe: `Models/ViewportPerformanceRecorder.cs`,
  `Controls/PdfViewport.AreaPreviewPerformanceProbe.cs`, and
  `MainWindow.ViewportAreaPreviewSmoke.cs`.
- Isolated launcher: `Tools/ui_viewport_area_preview_smoke.ps1`.

## Maintenance Note

The screen-frame cache trades a bounded viewport-sized bitmap for stable input
latency. A typical 4K surface is about 32 MiB; the hard 40-million-pixel cap is
about 160 MiB. Multiple very large detached viewports can therefore increase
working set, but allocation failure is recoverable and falls back to direct
paint instead of crashing the UI.
