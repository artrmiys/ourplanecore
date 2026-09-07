# Sheet Render Strategy — Master Plan (2026-06-01)

> Исторический документ. Актуальный срез 2026-09-06: [состояние программы](../CURRENT_OURPLANECORE_STATUS.md), [release evidence](../STRATEGY_APP_EVIDENCE_2026_09_06.md) и [код этой области](../../Controls/PdfViewport.Rendering.cs). Старые планы, пути и замеры ниже относятся к дате документа.

Canonical strategy for reworking and finishing PDF sheet rendering in
OurPlaneCore. Supersedes the seed plan
`docs/10-performance-render/INSTANT_PAGE_OPEN_IDEAL_QUALITY_PLAN_2026_06_01.md` (kept for the per-slice
detail; this doc is the authoritative sequencing).

Two non-negotiable user goals:

1. **Instant page open** with a readable first frame.
2. **Crisp text at deep zoom** (up to `ZoomMax = 16`) — the #1 pain point.

Guiding constraint: **improve competently without breaking what works.** Every
phase below is independently shippable, behind a clear rollback, and ordered so
that earlier phases de-risk later ones.

---

## 1. Success criteria (measurable)

- Page switch shows a readable frame in **< ~50 ms** (cache blit, no worker call).
- Sharp/refresh frame for the visible area lands in **< ~250 ms** after switch.
- Text stays crisp (no upscale blur) at **any** zoom up to `16×`.
- Warm pass does **not** make interactive listing lag.
- No regression in: layer toggle, PDF snap, layer trace, sheet overlay, export.
- Packaged-log errors after startup stay at `0`.

---

## 2. How others do it (research synthesis)

The three mature engines converge on the same architecture: **build once, render
the visible region at zoom-matched DPI, cache, and cancel superseded work.**

- **MuPDF / PyMuPDF (our primary engine).** Interpret the page into a
  `DisplayList` **once**, then render it many times at different resolutions via
  a transform matrix + a **clip/scissor rect** (`fz_run_display_list`). A "Store"
  caches reusable blocks. Banded/tiled multi-threaded rendering is the
  performance path. → We should reuse a cached display list and render **clipped
  tiles**, not re-parse the whole page each frame.
- **PDF.js (browser reference viewer).** Renders the canvas at
  `devicePixelRatio` so it is sharp on hi-DPI; **tiles** large/zoomed pages into
  smaller canvases (explicitly for 800 %+ zoom) to bound memory; aborts
  superseded renders with `RenderingCancelledException` instead of letting them
  pile up. → We need zoom-matched DPI, viewport tiling, and hard cancellation.
- **PDFium (our Docnet fallback).** `FPDF_RenderPageBitmapWithMatrix` accepts a
  clip rect + scaling matrix → native support for tiling a partial region;
  `FPDF_RenderPageBitmap_Start` gives progressive pause/resume rendering. → The
  fallback path can also do clipped/tiled rendering, so the strategy is not
  PyMuPDF-only.
- **Tile/LOD viewers (maps, CAD).** Quadtree tiles + level-of-detail pyramid:
  cheap low-res base always visible, high-res tiles streamed for the viewport,
  cached with LRU. → This is the end-state shape of our cache.

Common thread: **the whole sheet is never held at max DPI.** Only a cheap base +
the visible high-DPI region. Our current code violates exactly this (single
whole-sheet raster capped at `2.25×`, then upscaled by the GPU → blur).

---

## 3. Current architecture inventory

### What exists and works (do not rebuild)

- Persisted clean preview cache on disk, keyed by PDF identity + page + scale,
  survives restart. `Models/PdfPreviewRenderCache.cs`.
- `LoadPage` shows a cached preview before the refresh render.
  `Controls/PdfViewport.PageApi.cs:51`.
- Two render engines: PyMuPDF worker (`Tools/pdf_layers_helper.py`,
  `render_data` at `:1823`) + Docnet/PDFium fallback
  (`Controls/PdfViewport.Layers.cs:23`).
- Version counters already abandon stale results: `_layerRenderVersion`,
  `_docnetRenderVersion`, `_pdfSnapLoadVersion` (checked before applying).
- RAM bitmap caches (Docnet 8 / 220 MB; render cache 12).
- Zoom-rerender debounce timer (`ViewportConstants.ZoomRerenderDelayMs = 180`).
- Nearby-page preview prefetch (+1, −1, +2) at Docnet `0.15`
  (`MainWindow.PageTabs.cs:563-565`).

### Confirmed bottlenecks (root causes)

1. **Deep-zoom blur (top pain).** `_pageBitmap` is one whole-sheet raster capped
   at `ResponsiveMaxRenderScale = 2.25` (+ 24 MP whole-sheet pixel budget,
   `ViewportRenderPolicy.cs:11,12,65`). Paint upscales it via
   `canvas.DrawBitmap(src,dst, FilterQuality.Medium)` whenever `_zoom >
   _bitmapScale` (`Rendering.cs:66,85`). Zoom reaches `16×` (`PdfViewport.cs:402`).
2. **Single worker semaphore `(1,1)`.** Render, layers, snap, trace, sheetmeta,
   thumbnail all serialize through one Python process
   (`PdfLayerRenderService.cs:18`, `worker_loop` at `pdf_layers_helper.py:2260`).
3. **Lazy preview fill.** Persisted preview only written on first visit → first
   open of each sheet is slow.
4. **Refresh render after preview hit.** `LoadPage` still queues a full-scale
   layer render after a cache hit (intentional for correctness, but it is the
   biggest measured lag; see `PDF_RENDER_PERF_STATUS_2026_05_28.md`).
5. **Hidden-layer reopen.** `_render_samples_for_states` re-`fitz.open`s and
   tokenizes content streams in pure Python when any layer is off
   (`pdf_layers_helper.py:1741,997`).
6. **PNG round-trip.** Worker encodes PNG → base64 → C# `SKBitmap.Decode`; large
   renders spill to a temp file (`InlineRenderImageMaxPixels = 3_000_000`).

---

## 4. Target architecture (end state)

A **layered viewport** with three render tiers + an async render service:

- **Tier A — Instant base.** Cached raster of the whole sheet at a readable
  medium scale (`0.75`), decoded from disk on switch. Always drawn first; never
  blank. (Builds on existing preview cache.)
- **Tier B — Refresh raster.** Whole-sheet clean render at the fit scale, served
  from a persisted normal-scale cache when possible.
- **Tier C — Detail tiles.** For `_zoom > _bitmapScale`, render only the
  **visible clip rect** at zoom-matched DPI and draw over A/B. LRU tile cache
  keyed by (page, clip-rect-quantized, scale). This is what delivers crisp zoom.
- **Render service.** Interactive lane (Tier B/C for the active page) + a
  **separate low-priority lane** (warm pass, neighbour prefetch, snap,
  thumbnail). Hard **cancellation** of superseded requests (PDF.js pattern).

PyMuPDF worker gains: `clip=rect` rendering, cached display list per page, raw
pixel output option. Docnet path mirrors with `RenderPageBitmapWithMatrix` clip.

---

## 5. Phased step-by-step plan

Each phase: **Goal / Change / Files / Risk / Rollback / Verify / Exit.** Ship in
order; do not start a phase until the previous one is validated in the packaged
log.

### Phase 0 — Baseline instrumentation (no behavior change)

- **Goal:** measure before touching anything; protect against regressions.
- **Change:** ensure render timing + `zoom` + `bitmapScale` + `renderScale` are
  logged on every interactive render (extend existing `ReportSlowPdfRender` /
  `ReportSlowLayerRender`). Add a one-shot "render profile" summary counter.
- **Files:** `Controls/PdfViewport.Layers.cs`, `PdfViewport.Rendering.cs`.
- **Risk:** none (logging only). **Rollback:** revert.
- **Verify:** open a job, switch 10 sheets, zoom in/out; confirm log shows
  scale-vs-zoom so blur cases are visible.
- **Exit:** baseline numbers captured in a dated status note.

### Phase 1 — Crisp-zoom interim (cheap paint-path fix)

- **Goal:** immediately reduce upscale blur without any worker change.
- **Change:** (a) use `SKFilterQuality.High` (or `SKSamplingOptions` cubic) for
  the page-bitmap blit when upscaling; (b) raise/remove the whole-sheet pixel
  budget only for the **visible** render scale selection so the base raster is
  rendered closer to zoom when feasible.
- **Files:** `Controls/PdfViewport.Rendering.cs:66`,
  `Models/ViewportRenderPolicy.cs`.
- **Risk:** low (slightly higher paint cost; bounded). **Rollback:** one-line
  revert of filter quality.
- **Verify:** zoom to ~400–800 %; text noticeably less fuzzy; frame time still
  under slow-frame threshold (`SlowFrameLogMs = 45`).
- **Exit:** subjective sharpness up, no frame-time regression in log.

### Phase 2 — Viewport clip-render (the real crisp-zoom fix) ★ user priority

- **Goal:** true crisp text at any zoom by rendering the visible rect at
  zoom-matched DPI (Tier C).
- **Change:**
  1. Add `clip` (x0,y0,x1,y1 in PDF points) to `render_data`; pass to
     `page.get_pixmap(matrix, clip=fitz.Rect(...))`. Mirror clip support in the
     Docnet path via `RenderPageBitmapWithMatrix`.
  2. New `QueueDetailRender(visibleRect, targetScale)` in the viewport, gated by
     `_zoom > _bitmapScale * k`; pixel budget applied to the **clip**, not the
     sheet; debounced via existing `_zoomRerenderTimer`.
  3. Draw the detail tile over the base in `OnPaint`; fall back to base outside
     the tile and while the detail render is in flight.
  4. Version + cancel superseded detail renders (reuse the `_…Version` pattern).
- **Files:** `Tools/pdf_layers_helper.py` (`render_data`),
  `Models/PdfLayerRenderService.Render.cs` (request DTO + clip),
  `Controls/PdfViewport.Layers.cs` / `PdfViewport.ViewTransform.cs` /
  `PdfViewport.Rendering.cs`.
- **Risk:** medium (new render path + coordinate math). **Rollback:** feature
  flag `detailRenderEnabled`; off → exact current behavior.
- **Verify:** zoom to 8×–16× on a dense arch sheet; dimension text is razor
  sharp; pan re-renders the new region; switching pages cancels stale detail
  renders (no pile-up in log).
- **Exit:** crisp at `16×`; no orphaned renders; flag defaults on after soak.

### Phase 3 — Cancellation / superseding hardening

- **Goal:** guarantee superseded interactive renders are abandoned, never queue
  up behind the single worker (PDF.js `RenderingCancelledException` discipline).
- **Change:** centralize version checks; drop requests whose page/zoom changed
  before they start; ensure the detail lane keeps only the latest pending
  request (coalesce).
- **Files:** `Controls/PdfViewport.Layers.cs`,
  `Models/PdfLayerRenderService.Worker.cs`.
- **Risk:** low–medium. **Rollback:** revert coalescing.
- **Verify:** rapid zoom/scrub; worker queue depth stays ~1; no stale frames.
- **Exit:** stable under fast navigation stress (reuse
  `MainWindow.ViewportPageStressSmoke.cs`).

### Phase 4 — Persisted normal-scale render cache (instant refresh)

- **Goal:** make the post-preview refresh render a disk cache hit, killing the
  #1 measured lag.
- **Change:** extend `PdfPreviewRenderCache` to common refresh scales
  (`1`, `1.5`, fit scale) keyed by PDF identity + page + scale + **layer-state
  hash + highlight-state hash**. Clean state only first (no hidden/highlight).
- **Files:** `Models/PdfPreviewRenderCache.cs`,
  `Models/PdfLayerRenderService.Render.cs`.
- **Risk:** low (additive cache; PyMuPDF stays fallback). **Rollback:** disable
  cache read.
- **Verify:** second open of a sheet logs cache hit at refresh scale; `Viewport
  slow layer render` count drops at `scale=1/1.5`.
- **Exit:** repeat opens = preview hit → refresh hit, no live render.

### Phase 5 — Background warm pass at `0.75` (instant first open)

- **Goal:** first open of every sheet is instant + readable.
- **Change:** on job open, enqueue a low-priority pass that fills the disk
  preview cache at `0.75` (and optionally Phase-4 refresh cache) for all pages;
  idempotent/resumable; skips already-cached; runs on the low-priority lane.
- **Files:** new `Models/SheetRenderWarmupService.cs`, hook from
  `MainWindow.JobLifecycle.cs`.
- **Risk:** medium (background load). **Rollback:** setting to disable warmup.
- **Verify:** open job, wait for warm; first visit to an unseen sheet is instant;
  interactive listing during warm shows no added lag (depends on Phase 6 lane).
- **Exit:** cold-open lag gone; disk within `1.5 GB` cap.

### Phase 6 — Worker concurrency (priority lane → optional 2nd process)

- **Goal:** stop warm/prefetch/snap/thumbnail from blocking interactive render.
- **Change:** (a) priority queue in front of the existing worker that always
  yields to interactive requests; if still contended, (b) a dedicated **second**
  worker process for warm/snap/thumbnail, leaving the primary for interactive.
- **Files:** `Models/PdfLayerRenderService.Worker.cs` (+ new lane).
- **Risk:** medium–high (concurrency, layer-state ordering, memory). **Rollback:**
  collapse back to single lane.
- **Verify:** warm runs while user lists pages → interactive render still < 250 ms;
  no layer-state cross-talk; memory bounded.
- **Exit:** interactive latency independent of background load. (Per prior
  guidance: do **not** jump to a full worker pool without this staged plan.)

### Phase 7 — Python render hot-path speedups

- **Goal:** make each individual render cheaper.
- **Change:** (a) cache a `DisplayList` per (doc, page) and render from it via
  matrix+clip instead of `get_pixmap` from scratch; (b) stop `fitz.open` per
  hidden-layer render — reuse cached doc + native OCG toggling instead of pure-
  Python content-stream tokenizing; (c) return **raw BGRA/PPM** for the hot path
  instead of PNG; raise inline pixel limit for clipped tiles.
- **Files:** `Tools/pdf_layers_helper.py` (`render_data`,
  `_render_samples_for_states`, `_filter_*`), `…Render.cs` (decode raw).
- **Risk:** medium (touches core render + layer correctness). **Rollback:** keep
  PNG path + old hidden-layer path behind a flag.
- **Verify:** render-time telemetry drops; layer toggle still pixel-correct;
  highlight still correct.
- **Exit:** measurable per-render speedup, no visual regression.

### Phase 8 — LOD / tile-cache polish (optional, last)

- **Goal:** smooth very deep zoom + huge sheets at scale.
- **Change:** quadtree tile cache for Tier C (quantized clip tiles, LRU by
  bytes); optional mip base. Only if Phases 2–7 leave gaps.
- **Files:** new tile-cache model + viewport wiring.
- **Risk:** medium. **Rollback:** fall back to single detail tile (Phase 2).
- **Verify:** pan at `16×` reuses cached tiles; memory bounded.
- **Exit:** cached tiles reused on pan; no full re-render at constant zoom.

---

## 6. Cross-cutting decisions

- **Quality knob:** instant/warm scale = **`0.75`** (readable-medium). Decided.
- **Cancellation:** every async render carries a version + page/zoom identity;
  superseded results are dropped, not applied (pattern already present — make it
  uniform).
- **Cache keys:** always PDF full path + mtime + length + page + scale (+ layer/
  highlight hash for non-clean). Reuse existing key builders.
- **Memory budget:** never hold the whole sheet at max DPI. Tier C is viewport-
  sized; RAM caches stay LRU-bounded.
- **Settings surface:** expose warmup on/off, detail-render on/off, and quality
  scale in the "8 Settings" tab (per the editable-rules convention) rather than
  hard-coding.
- **Engine parity:** keep Docnet/PDFium as the fallback for every new path
  (clip render included) so PyMuPDF outages degrade, not break.

---

## 7. Sequencing & dependencies

```
Phase 0 (measure)
   └─> Phase 1 (interim sharpen)         ──┐ ship fast, user-visible win
   └─> Phase 2 (clip-render) ★            ──┤ depends on 0; THE crisp-zoom fix
          └─> Phase 3 (cancellation)        │ hardens 2
Phase 4 (refresh cache) ─────────────────────┤ independent of 1-3
   └─> Phase 5 (warm pass) ──> needs Phase 6 lane to be safe under load
Phase 6 (concurrency) ── enables 5 to run without lag
Phase 7 (python hot-path) ── independent; compounds with all
Phase 8 (LOD tiles) ── optional polish after 2-7
```

Recommended ship order: **0 → 1 → 2 → 3 → 4 → 5 → 6 → 7 → (8)**. Phases 1 and 4
are low-risk quick wins and can interleave; Phase 2 is the user's priority and
should not wait.

---

## 8. Risk register (top items)

| Risk | Phase | Mitigation |
|------|-------|-----------|
| Coordinate math errors in clip render → misaligned tile | 2 | Feature flag; reuse `GetVisiblePdfRect`; visual test vs base |
| Background warm starves interactive render | 5/6 | Low-priority lane (6) before enabling warm (5) |
| Layer-state cross-talk with a 2nd worker | 6 | Per-request layer states; isolate doc cache per process |
| Native OCG toggle changes pixels vs old filter | 7 | Keep old path behind flag; pixel-diff check |
| Raw pixel format mismatch (BGRA vs RGB) | 7 | Explicit color type; unit-render compare |
| Disk cache bloat | 4/5 | Existing `1.5 GB` prune; cap warm scale at `0.75` |

---

## 9. Verification & rollout (per `release-cycle`)

For every phase: `dotnet build` clean (0/0), `git diff --check`, conflict/TODO
scan, run the app, validate the **packaged app log** has `0` errors after
startup, then publish compressed single-file + `.bak` deploy. Git-checkpoint
before each phase; each phase is its own commit/PR so any single phase can be
reverted without unwinding the others.

---

## 10. Related docs

- `docs/10-performance-render/INSTANT_PAGE_OPEN_IDEAL_QUALITY_PLAN_2026_06_01.md` (seed / slice detail)
- `docs/10-performance-render/PDF_RENDER_PERF_STATUS_2026_05_28.md` (prior measured status)
- `docs/10-performance-render/PDF_PREVIEW_CACHE_HANDOFF_2026_05_28.md`
- `docs/10-performance-render/PAGE_OPEN_UI_PERF_HANDOFF_2026_05_28.md`
- `docs/10-performance-render/PDF_INLINE_RENDER_HANDOFF_2026_05_28.md`

## Sources (research)

- MuPDF Explored (display list, clip, Store): https://casper.mupdf.com/docs/mupdf_explored.pdf
- PyMuPDF DisplayList: https://pymupdf.readthedocs.io/en/latest/displaylist.html
- PyMuPDF Images/Pixmap perf: https://pymupdf.readthedocs.io/en/latest/recipes-images.html
- PDF.js tiling for high zoom (issue #6419): https://github.com/mozilla/pdf.js/issues/6419
- PDF.js cancellation (#17354): https://github.com/mozilla/pdf.js/issues/17354
- PDFium RenderPageBitmapWithMatrix clip/tiling: https://pdfium.googlesource.com/pdfium/+/main/public/fpdfview.h
- PDF Studio multithreaded tile factory (6× perf): https://kbpdfstudio.qoppa.com/new-multithreaded-tile-factory-for-up-to-6x-performance-boost-in-rendering-documents/
- Syncfusion smooth scrolling image-heavy PDFs: https://www.syncfusion.com/blogs/post/smooth-scrolling-performance-image-heavy-pdfs
