# OurPlaneCore Speed Acceleration Analysis - 2026-06-02

Полный разбор текущей производительности OurPlaneCore после коммита
`a77aaed Speed up viewport rendering` и план доведения программы до состояния
"молниеносно": быстрые открытия листов, четкий zoom 250-350%+, отсутствие
фризов при pan/zoom, нормальное использование RAM и предсказуемая работа с
большими импортированными проектами.

Этот документ продолжает и уточняет:

- `docs/10-performance-render/SHEET_RENDER_STRATEGY_2026_06_01.md`
- `docs/10-performance-render/PDF_RENDER_PERF_STATUS_2026_05_28.md`
- `docs/70-architecture-refactor/CODEBASE_HEALTH_AUDIT_2026_06_01.md`

## 0. Короткий вывод

Главная проблема уже не в одном конкретном баге. Производительность сейчас
держится на нескольких разных слоях, и каждый слой надо ускорять отдельно:

1. **Page open**: должен показывать первый читаемый кадр сразу, без ожидания
   live render.
2. **Deep zoom clarity**: текст и линии не должны быть мылом на 250-350% и выше.
   Для этого нужен clipped/tiled detail render, а не большой whole-sheet raster.
3. **Pan/zoom frame time**: во время движения нельзя тратить 50-200 ms на
   перерисовку bitmap или labels.
4. **Render queue**: background prefetch, snap, metadata, thumbnails и active
   page render не должны стоять в одной очереди.
5. **RAM cache**: RAM должна использоваться как рабочий cache, но не как
   бесконтрольная попытка держать каждую страницу в максимальном DPI.
6. **Trees/UI**: выбор takeoff/page не должен сканировать все дерево на каждый
   клик.
7. **Import/metadata**: auto naming/scale и preview warmup должны работать
   батчево, с кешем PDF-документа и без повторного открытия PDF на каждую
   страницу.

Коммит `a77aaed` закрыл часть этого: RAM caches, render request coalescing,
prefetch worker, detail throttling, label/joist/overlay culling, Pages/Takeoffs
indexed sync. Но до идеала еще надо сделать несколько следующих фаз.

## 1. Что уже сделано сегодня

Коммит:

```text
a77aaed Speed up viewport rendering
```

Затронутые зоны:

- `Models/PdfLayerRenderService.cs`
- `Models/PdfLayerRenderService.Render.cs`
- `Models/PdfLayerRenderService.Worker.cs`
- `Models/PdfPreviewRenderCache.cs`
- `Models/ViewportRenderPolicy.cs`
- `Controls/PdfViewport.PageApi.cs`
- `Controls/PdfViewport.Rendering.cs`
- `Controls/PdfViewport.DetailRender.cs`
- `Controls/PdfViewport.DetailPrefetch.cs`
- `Controls/PdfViewport.MeasurementRendering.cs`
- `Controls/PdfViewport.JoistRendering.cs`
- `Controls/PdfViewport.SelectionOverlayRendering.cs`
- `Controls/PdfViewport.SheetOverlay.cs`
- `MainWindow.PageTabs.cs`
- `MainWindow.PagesTree.cs`
- `MainWindow.PagesTreeIndex.cs`
- `MainWindow.PagesSelection.cs`
- `MainWindow.TakeoffSelectionNavigation.cs`
- `MainWindow.PageTakeoffLegend*.cs`
- `Tests/TakeoffsTreeRegressionTests.cs`

Что изменено:

- Одинаковые render-запросы теперь coalesce на уровне
  `PdfLayerRenderService.TryRenderAsync(...)`, чтобы одинаковый PDF/page/scale/
  layer/clip не запускал повторный PyMuPDF render.
- In-memory render cache увеличен:
  - `PdfLayerRenderService`: `MaxRenderCacheEntries = 96`,
    `MaxRenderCacheBytes = 768_000_000`.
  - `PdfPreviewRenderCache`: memory layer `MaxMemoryEntries = 96`,
    `MaxMemoryBytes = 512_000_000`.
- `PdfViewport.RenderCache` уже имеет большой RAM cache:
  - Docnet cache: 48 entries, budget примерно 1.5-4.0 GB.
  - Layer bitmap cache: 320 entries, budget примерно 12-24 GB.
  - Clean render prefetch concurrency bounded.
- Detail render:
  - clipped detail tiles в RAM;
  - max tiles `64`;
  - RAM budget примерно 2.4-4.8 GB;
  - adjacent prefetch снижен до 1 tile;
  - prefetch delay `300 ms`;
  - page-switch detail hold `320 ms`.
- Page open:
  - отменяет старые navigation timers;
  - не запускает forced detail render сразу после preview cache hit;
  - nearby page prefetch разделен на cheap preview и clean render только для
    ближайших соседей.
- Paint path:
  - labels на fast pan/zoom режутся;
  - text-box layout кешируется;
  - joist layout считается один раз за paint pass;
  - sheet overlay рисуется только по видимому clip;
  - page bitmap не рисуется, если high-DPI detail tile уже покрывает viewport.
- Pages/Takeoffs tree:
  - добавлен `MainWindow.PagesTreeIndex.cs`;
  - поиск page row и linked takeoff row идет через индекс, а не полный обход
    всего `PagesTree`;
  - Takeoffs -> Pages sync смотрит только страницы из measurements выбранного
    takeoff.

Проверки после коммита:

```powershell
dotnet build .\ourplanecore.sln
dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build
dotnet publish .\ourplanecore.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o .\bin\publish
```

Результат:

- build: `0 warnings / 0 errors`
- tests: `270/270 tests passed`
- packaged exe:
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`
- SHA256:
  `0633311E16C91C4266E200FF3C4C070E2997E4C66ADF469CF2F47DA0A075FBBE`
- latest runtime smoke:
  - latest startup: `2026-06-02T01:28:06.5524246-03:00`
  - `ERROR` after that marker: `0`
  - process alive/responding during verification: yes
  - `Loaded takeoffs`: yes
  - `Viewport` activity: yes

## 2. Evidence from current log

Log inspected:

```text
%APPDATA%\OurPlaneCore\logs\app-20260602.log
```

Important distinction:

- The **final packaged smoke run** after `a77aaed` has no errors and no slow
  render lines.
- The **whole day log** includes runs before the final patch and is still useful
  to rank bottlenecks.

Whole-day counts from `app-20260602.log`:

```text
Viewport slow frame                      33
Viewport slow layer render               43
Viewport slow docnet render               1
Viewport render profile                 489
Viewport clean render prefetched        168
Viewport PyMuPDF render cache hit       136
Viewport PyMuPDF preview cache hit       23
Viewport detail render unavailable        0
Viewport delayed detail render failed     0
```

Render profile grouping from the same log:

```text
layer             count 211, max 1991 ms, avg 265.0 ms
layer-memory      count 156, max 0 ms,    avg 0.0 ms
preview           count 23,  max 0 ms,    avg 0.0 ms
detail            count 28,  max 1551 ms, avg 645.9 ms
detail-prefetch   count 60,  max 1616 ms, avg 540.3 ms
docnet            count 11,  max 573 ms,  avg 166.5 ms
```

Representative slow lines:

```text
Viewport render profile kind=detail; elapsed=1009ms; cache=False; zoom=2.298; bitmapScale=1; targetScale=1.304
Viewport render profile kind=detail; elapsed=903ms; cache=False; zoom=0.928; bitmapScale=1; targetScale=2.298
Viewport slow layer render 798ms; scale=1.5; layers=148
Viewport slow layer render 1839ms; scale=1.5; layers=434
Viewport slow layer render 1208ms; scale=1; layers=434
Viewport slow frame 215ms; activeMeasurements=0; timings=page:215
```

Interpretation:

- Cache hits are working: `layer-memory` and `preview` are `0 ms`.
- Remaining freezes are from **live render** and **page bitmap paint**, not from
  measurement geometry in those samples.
- Complex layered sheets still cause live PyMuPDF render cost near 1-2 seconds.
- Detail render improves clarity but individual detail jobs are still too slow
  when they have to render/encode/decode a tile live.
- Slow frames with `activeMeasurements=0` prove that not every lag is takeoff
  overlay; some is just bitmap draw/downsample/upscale.

## 3. Why PDF viewers look sharper than us

The user-visible question: "why does a normal PDF viewer stay sharp at any zoom?"

Answer: good PDF viewers do not usually keep one full-sheet raster and upscale
it forever. They render vector PDF content into tiles at the current zoom,
usually close to screen DPI, and cache those tiles. The UI still displays
pixels, but they are freshly rendered pixels for the visible region.

So the target is not "never rasterize". For a WPF/Skia app, practical display is
still bitmap-backed. The target is:

- rasterize only the visible region;
- rasterize at the correct zoom;
- cache aggressively in RAM;
- cancel obsolete renders;
- draw cheap low-res base while high-res tile is arriving;
- never block UI on background render.

Trying to display the whole PDF as pure vector in WPF would mean implementing a
PDF renderer inside the app: fonts, glyphs, transparency, shadings, images,
forms, layers/OCG, clipping, blend modes, overprints, annotations, etc. That is
not the right path. The right path is to use PyMuPDF/PDFium like real viewers:
clip/tile render plus LOD cache.

## 4. Current architecture map

### 4.1 Page open

Entry:

- `MainWindow.PageTabs.cs`
- `Controls/PdfViewport.PageApi.cs`

Current path:

1. `LoadPageIntoViewport(...)` opens selected page into `_viewport`.
2. `PdfViewport.LoadPage(...)` clears transient render state.
3. It tries `TryApplyPersistedPreviewRender(...)` at
   `ViewportRenderPolicy.InstantPagePreviewRenderScale = 0.35`.
4. If preview cache hits, it queues capped base layer render with
   `allowImmediateCache: false`.
5. If not, it can apply persisted clean render at scale `1`, otherwise falls
   back to Docnet preview render.
6. Deferred UI work loads overlay, annotations, visual sync and settings save
   later.

Good:

- first visible frame can be cache-backed;
- page open is no longer doing all UI work synchronously;
- preview and base render are separated.

Still weak:

- cold preview still depends on Docnet and can re-open/re-parse PDF;
- base layer render can still be live and slow on complex layered pages;
- warmup is opportunistic, not yet a full job-level warm service.

### 4.2 PyMuPDF render service

Files:

- `Models/PdfLayerRenderService.cs`
- `Models/PdfLayerRenderService.Render.cs`
- `Models/PdfLayerRenderService.Worker.cs`
- `Tools/pdf_layers_helper.py`

Current shape:

- primary worker;
- detail worker;
- prefetch worker;
- request coalescing;
- bounded RAM result cache;
- inline PNG for bounded renders;
- temp-file fallback;
- clip support via `RenderRequest.Clip`;
- persisted clean render cache for clean states.

Good:

- no longer one single process for absolutely everything;
- repeated identical render can return from memory;
- clean renders persist and can become `0ms` hits.

Still weak:

- every worker lane is still semaphore `1`;
- request cancellation is "drop stale result" but not true in-process abort;
- PNG encode + base64 + decode is still expensive for hot path;
- PyMuPDF helper does not yet use cached `DisplayList` per page;
- hidden-layer/highlight work still does more samples and pixel loops.

### 4.3 Detail render

Files:

- `Controls/PdfViewport.DetailRender.cs`
- `Controls/PdfViewport.DetailPrefetch.cs`
- `Controls/PdfViewport.Rendering.cs`
- `Models/ViewportRenderPolicy.cs`

Current behavior:

- when `_zoom >= _bitmapScale * 1.18`, viewport builds clipped detail request;
- render scale is chosen against current zoom and clip pixel budget;
- detail tile is drawn over base bitmap;
- adjacent tile prefetch only after idle and only at zoom >= 4;
- tiles are RAM-cached and trimmed by count/bytes.

Good:

- this is the core fix for clarity;
- it avoids whole-sheet max-DPI raster;
- it keeps base image visible while detail is rendering.

Still weak:

- detail jobs are still 500-1600 ms in the full day log;
- `DetailRenderMaxPixels` can still produce a large tile;
- prefetch can be useful but must never compete with active movement;
- no true quadtree tile grid yet, so panning may produce less reusable clips
  than a fixed tile grid would.

### 4.4 Paint path

Files:

- `Controls/PdfViewport.Rendering.cs`
- `Controls/PdfViewport.MeasurementRendering.cs`
- `Controls/PdfViewport.SelectionOverlayRendering.cs`
- `Controls/PdfViewport.SheetOverlay.cs`
- `Controls/PdfViewport.JoistRendering.cs`

Current behavior:

- page bitmap is drawn with `SKFilterQuality.High` when upscaling, `Medium`
  otherwise;
- base bitmap is skipped if detail tile covers visible page;
- visible measurements are culled;
- labels are skipped during fast navigation except selected/joist-important
  labels;
- text-box layout is cached;
- joist layout is cached per paint pass;
- sheet overlay is clipped to visible PDF rect.

Good:

- much less overlay work during fast pan/zoom;
- labels and joist calculation are not repeated as aggressively;
- overlay no longer draws full bitmap when only a small visible part is needed.

Still weak:

- slow frame samples show `timings=page:45..215` even with zero measurements;
- this points to bitmap draw/downsample/upscale, not takeoff overlay;
- no mip/overview bitmap cache yet for far zoom;
- no invalidation rectangle strategy yet; every paint still redraws the page
  layer.

### 4.5 RAM caches

Current RAM cache layers:

- `PdfViewport.DocnetRenderCache`
  - 48 entries
  - budget roughly 1.5-4 GB
- `PdfViewport.LayerBitmapCache`
  - 320 entries
  - budget roughly 12-24 GB
- `PdfViewport.DetailRenderTile` cache
  - 64 tile entries
  - budget roughly 2.4-4.8 GB
- `PdfLayerRenderService.RenderCache`
  - 96 entries
  - 768 MB
- `PdfPreviewRenderCache` memory layer
  - 96 entries
  - 512 MB

Important: these are **budgets**, not reservations. Task Manager will not show
10 GB RAM usage until the app actually opens/warms enough pages to fill caches.
This is correct. We want RAM used as cache, not allocated uselessly.

For a 64 GB desktop, the app can reasonably use 10-24 GB under heavy warm/cache
load, but only if:

- cache entries are reusable;
- old entries are LRU-trimmed;
- memory pressure is observed;
- there is a user-facing quality/RAM setting.

### 4.6 Pages/Takeoffs tree

Files:

- `MainWindow.PagesTreeIndex.cs`
- `MainWindow.PagesTree.cs`
- `MainWindow.PagesSelection.cs`
- `MainWindow.TakeoffSelectionNavigation.cs`
- `MainWindow.PageTakeoffLegend*.cs`

Current behavior:

- page item lookup and page-takeoff lookup are indexed;
- targeted refresh avoids full tree scans for common selection sync;
- page takeoff legend rebuild unregisters/registers indexed subtrees.

Good:

- ordinary selection lag should be much lower;
- Pages and Takeoffs selection sync no longer walks the full tree as often.

Still weak:

- full reload paths still exist for import/move/large structural changes;
- WPF `TreeView` is not virtualized like a data grid;
- huge trees still need future model-backed virtualization if they grow enough.

## 5. Bottleneck ranking after current patch

### 1. Live PyMuPDF layer render on complex pages

Evidence:

- `Viewport slow layer render 1839ms; scale=1.5; layers=434`
- `Viewport slow layer render 1208ms; scale=1; layers=434`
- full day max `layer` profile: `1991 ms`

Why it hurts:

- active page render competes with user pan/zoom expectations;
- a 1-2 second live render feels like a broken app even if preview is visible;
- complex layer pages multiply work.

Next fixes:

1. Avoid live base render when a cached clean render or layer-memory best-scale
   render is already good enough.
2. Make worker hot path faster with DisplayList cache and raw pixels.
3. Make layer discovery/render lazy when layer list is known empty or user is not
   using PDF Layers.
4. Prewarm clean render cache during idle/job open.

### 2. Detail render and detail-prefetch are still too slow

Evidence:

- detail max `1551 ms`, avg `645.9 ms`;
- detail-prefetch max `1616 ms`, avg `540.3 ms`.

Why it hurts:

- detail render is the path that fixes zoom clarity;
- if it arrives after 1 second, the user sees blur during work;
- if prefetch competes with active movement, it becomes a freeze source.

Next fixes:

1. Fixed tile grid instead of arbitrary viewport clip, so panning reuses tiles.
2. Raw BGRA/PPM output instead of PNG/base64 for detail tiles.
3. PyMuPDF `DisplayList` per page.
4. Smaller initial detail tile budget at 250-350%, then refine with second tile.
5. More aggressive stale request drop before worker starts.

### 3. Page bitmap paint/downsample frames

Evidence:

- slow frames with `activeMeasurements=0`;
- timing section mostly `page:45..215 ms`;
- examples at zoom `0.286`, `0.45`, `0.888`, `2.758`, etc.

Why it hurts:

- even if all renders are cached, pan/zoom can still hitch;
- painting a large bitmap with high/medium sampling every frame is expensive.

Next fixes:

1. Mip/overview bitmap cache for far zoom and fit zoom.
2. During active navigation use cheaper sampling for base bitmap, then high
   quality after idle.
3. Cache screen-space transformed bitmap for stable viewport until pan/zoom
   changes.
4. Add frame telemetry around `canvas.DrawBitmap` and `DrawDetailRenderTile` to
   prove exact cost.

### 4. Docnet cold preview re-opens PDF

Evidence:

- `Controls/PdfViewport.Layers.cs` uses `_docLib.GetDocReader(...)` inside
  `RenderPageBitmapWithDocnet(...)`;
- several other services also open Docnet reader per operation.

Why it hurts:

- tiny preview scale should be cheap, but PDF parse dominates;
- cold page opens still depend on this path if preview cache is missing.

Next fixes:

1. LRU Docnet document/page render context cache keyed by PDF identity + scale.
2. Prefer PyMuPDF preview warmup for imported projects before first click.
3. Keep Docnet as fallback but avoid repeated open/parse for rapid navigation.

### 5. Worker scheduling is better but not ideal

Current:

- primary worker;
- detail worker;
- prefetch worker;
- each lane still serial.

Why it hurts:

- background warmup can still overlap CPU with interactive render;
- concurrent Python processes can fight for CPU if not scheduled;
- no central priority scheduler yet.

Next fixes:

1. Central `RenderScheduler` with priorities:
   - active page current viewport;
   - active page base refresh;
   - current page detail prefetch;
   - neighboring clean render;
   - thumbnails/metadata/snap.
2. Per-priority queue limits and cancellation.
3. CPU-aware worker count; do not just launch unlimited workers.

### 6. Import/autonaming/autoscale performance and correctness

This is adjacent to speed. A new project import feels broken if:

- page metadata extraction takes too long;
- page names/scales appear late or never;
- preview cache is empty after import;
- folder/tree refresh reloads the whole UI repeatedly.

Next fixes:

1. One batch metadata pass per source PDF, not independent open per page.
2. Cache PyMuPDF document and extracted word blocks once per PDF.
3. Run metadata extraction, preview warmup and source manifest write as one
   import pipeline with progress.
4. Persist "metadata complete" and "preview warm complete" flags per source PDF.
5. After import, refresh Pages tree once, not per page.

## 6. What not to do

### Do not render the whole sheet at max zoom

At zoom 16, a full architectural sheet raster would be enormous. It would:

- explode memory;
- freeze UI during render;
- still get invalidated on layer/highlight changes;
- be worse than clipped tile render.

The correct model is:

```text
whole sheet low/normal base + visible high-DPI tile(s)
```

### Do not simply increase every scale cap

Raising `ResponsiveMaxRenderScale` helps only until the whole sheet pixel budget
hits. It also makes every page switch heavier. The real crispness fix is
viewport detail tiles.

### Do not re-enable broad unsafe tree fast refresh

There is existing guidance that selection lag and bulk copy/paste lag are
separate. Keep targeted refreshes. Do not globally re-enable a broad fast path
that was disabled for data safety.

### Do not hide labels/overlays permanently for speed

The app is a takeoff tool. It must remain usable. Temporary fast-frame
simplification is fine; permanently dropping joist labels, selected labels, or
takeoff overlay information is not.

### Do not use unlimited workers

More processes can make the app slower if they fight for CPU, disk and memory.
Use priority queues and bounded workers.

## 7. Target performance goals

These are the practical goals to judge "молниеносно":

### Page open

- Warm page switch first frame: `< 50 ms`
- Cold page switch first readable frame: `< 150 ms`
- No blank viewport while switching pages
- No `Viewport slow layer render` on repeat opens of recently used pages

### Zoom 250-350%

- First crisp detail tile: `< 250 ms` after idle
- Pan at same zoom: reused tile or new tile `< 250 ms`
- Base bitmap can be soft for a fraction of a second, but detail tile must land
  quickly and visibly replace it

### Paint frame

- Normal frame: `< 16-24 ms`
- Acceptable occasional frame: `< 45 ms`
- Slow frame log count during normal work: near zero

### RAM

- Desktop 64 GB:
  - allow app cache to grow into roughly 10-24 GB during heavy warm use;
  - do not reserve it upfront;
  - trim under pressure.
- Laptop 32 GB:
  - default cache around 6-12 GB;
  - same code path, lower cap.

### Worker queue

- Active render queue depth: `0-1`
- Obsolete detail/base requests dropped before work starts
- Background prefetch never blocks active page render

## 8. Proposed next implementation phases

Each phase should be one commit and one packaged verification.

### Phase A - Performance benchmark harness

Goal:

- Stop guessing. Produce repeatable numbers for page switch, zoom, pan, render,
  cache hit/miss, memory and worker queue.

Changes:

- Add a debug-only/perf-command path that opens a chosen job and replays:
  - switch through N sheets;
  - zoom to 250%, 350%, 800%;
  - pan in 4 directions;
  - toggle a layer if layers exist;
  - record timings.
- Write output to `AI_Context/perf_runs/<timestamp>.json` or `docs/perf_runs/`.

Files:

- `Controls/PdfViewport.*`
- `MainWindow.PageTabs.cs`
- new `Models/ViewportPerformanceRecorder.cs`
- optional script under `scripts/`

Metrics:

- `firstFrameMs`
- `baseRenderMs`
- `detailRenderMs`
- `detailVisibleMs`
- `slowFrameCount`
- `cacheHitRate`
- `ramMb`
- `workerQueueDepth`

Verify:

```powershell
dotnet build .\ourplanecore.sln
dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build
```

Exit:

- one perf file with baseline numbers for Croton and Jordan Lane.

### Phase B - Page paint mip/overview cache

Goal:

- Kill slow frames where `activeMeasurements=0` and `timings=page:45..215`.

Changes:

- Maintain 2-3 downsampled versions of current page bitmap:
  - fit/overview;
  - 0.5x;
  - normal.
- Select source bitmap based on zoom so Skia is not downsampling a large raster
  every frame.
- During fast navigation use cheaper sampling for base bitmap, but keep detail
  tile high quality after idle.

Files:

- `Controls/PdfViewport.Rendering.cs`
- `Controls/PdfViewport.RenderCache.cs`
- possible new `Controls/PdfViewport.MipCache.cs`

Risk:

- visual softness during movement if overdone.

Rollback:

- disable mip selection and return to current `_pageBitmap`.

Verify:

- slow frame page timing drops under 45 ms on zoomed-out and 250-350% pan.

### Phase C - Raw detail tile output

Goal:

- Make detail render fast enough to actually feel crisp while working.

Changes:

- Add PyMuPDF response mode for detail tiles:
  - raw BGRA or PPM bytes;
  - width/height/stride metadata;
  - no PNG encode;
  - no base64 for larger detail tiles if pipe/file binary is used.
- Decode/create `SKBitmap` directly from raw pixels.
- Keep PNG path as fallback.

Files:

- `Tools/pdf_layers_helper.py`
- `Models/PdfLayerRenderService.Protocol.cs`
- `Models/PdfLayerRenderService.Render.cs`
- `Controls/PdfViewport.DetailRender.cs`

Risk:

- pixel format mismatch.

Verify:

- same page/clip visual compare;
- detail render average drops materially from current 500-650 ms range.

### Phase D - PyMuPDF DisplayList cache

Goal:

- Reduce repeated page interpretation cost.

Changes:

- In helper process, maintain page display lists keyed by:
  - PDF identity;
  - page index;
  - layer state signature if needed.
- For clean/default render, render from display list with matrix and clip.
- Keep old `page.get_pixmap(...)` path behind fallback.

Files:

- `Tools/pdf_layers_helper.py`

Risk:

- layer state interactions: display list may need invalidation per OCG state.

Verify:

- clean render and detail render times drop;
- layer toggle still correct.

### Phase E - Fixed tile grid / LOD cache

Goal:

- Reuse detail tiles while panning at the same zoom.

Changes:

- Quantize visible region into fixed PDF-space tile grid per LOD:
  - e.g. 512-1024 screen px tiles;
  - stable keys `(pdf, page, lod, tileX, tileY, layerState)`.
- Draw all visible tiles.
- Queue missing tiles by distance from viewport center.
- Keep base bitmap under missing tiles.

Files:

- new `Controls/PdfViewport.DetailTileGrid.cs`
- `Controls/PdfViewport.DetailRender.cs`
- `Controls/PdfViewport.Rendering.cs`

Risk:

- more complexity; should come after raw output and metrics.

Verify:

- pan at 250-350% reuses existing tiles;
- no repeated render for near-identical clips.

### Phase F - Render scheduler with hard priority

Goal:

- Background work must never block active work.

Changes:

- Introduce `RenderScheduler`:
  - active detail current viewport;
  - active base current page;
  - active layer toggle/highlight;
  - current page prefetch;
  - neighbor prefetch;
  - import/warm/thumbnails/metadata.
- Each queue has max pending count.
- New requests supersede older requests by page/zoom/key.
- Workers pull from scheduler by priority.

Files:

- `Models/PdfLayerRenderService.Worker.cs`
- new `Models/RenderScheduler.cs`
- `Controls/PdfViewport.DetailRender.cs`
- `Controls/PdfViewport.RenderCache.cs`

Risk:

- concurrency bugs; keep the current direct worker path behind a flag until
  tested.

Verify:

- warmup running in background while switching pages still gives active render
  under target.

### Phase G - Full job warmup service

Goal:

- First open of every sheet after import/job open is already cached.

Changes:

- Add warmup service:
  - low priority;
  - resumable;
  - skip existing cache entries;
  - warms `0.35`, `0.75` and clean `1.0` where reasonable;
  - warms metadata and thumbnails together.
- Expose status in UI:
  - "Render cache warming: 18/53"
  - pause/resume.

Files:

- new `Models/SheetRenderWarmupService.cs`
- `MainWindow.JobLifecycle.cs`
- `MainWindow.SettingsManager.*.cs`
- `Controls/PdfViewport.RenderCache.cs`

Risk:

- background CPU/memory pressure if scheduler is not ready.

Verify:

- import a new 50-page project;
- wait for warmup;
- click every page: first frame is instant.

### Phase H - Settings UI for quality/RAM

Goal:

- User can choose performance profile depending on machine.

Settings:

- Quality mode:
  - Balanced
  - High
  - Max
- RAM cache budget:
  - Auto
  - 6 GB
  - 12 GB
  - 24 GB
  - Custom
- Detail render:
  - on/off
  - max tile pixels
  - prefetch on/off
- Warmup:
  - off
  - current job only
  - all recent jobs

Files:

- `Models/AppSettings.cs`
- `Models/ViewportRenderPolicy.cs`
- `MainWindow.SettingsManager.*.cs`

Rule:

- Do not leave these as hidden constants forever. Per project convention, hard
  behavior rules should move to Settings.

### Phase I - Docnet document cache

Goal:

- Make fallback preview cheaper.

Changes:

- LRU cache of Docnet document readers or page render contexts, keyed by:
  - PDF path;
  - mtime;
  - length;
  - scale.
- Dispose on eviction.
- Keep small because Docnet native handles can be sensitive.

Files:

- `Controls/PdfViewport.Layers.cs`
- possibly new `Models/DocnetDocumentCache.cs`

Risk:

- native handle lifecycle.

Verify:

- cold preview render count/time drops.

### Phase J - Layer pipeline optimization

Goal:

- Complex layered sheets no longer make default page navigation slow.

Changes:

- If cached layer metadata says `layers=0`, avoid expensive layer path when
  clean render cache/docnet base is enough.
- If layers exist but user has not opened/toggled PDF Layers, use clean cached
  render first and defer expensive full layer refresh.
- Replace Python content-stream filtering for hidden layers with native OCG
  state where possible.
- Keep highlight path separate and only run when highlight is active.

Files:

- `Tools/pdf_layers_helper.py`
- `Models/PdfLayerRenderService.Render.cs`
- `Controls/PdfViewport.Layers.cs`

Risk:

- layer toggle correctness.

Verify:

- layer toggle pixel behavior unchanged;
- default page navigation on pages with 148/434 layers is no longer 1-2 s.

### Phase K - Import pipeline acceleration

Goal:

- New imported project immediately has names, scales and warm previews.

Changes:

- Batch metadata extraction per PDF:
  - open PDF once;
  - extract words/blocks once per page;
  - detect sheet label/title/scale;
  - write `source_pdf.json` in one pass.
- Parallelize cautiously by source PDF, not by every page.
- Queue render warmup after metadata complete.

Files:

- `Tools/pdf_layers_helper.py`
- `Models/PdfSheetMetadataService.cs`
- `MainWindow.PagesImport*.cs`
- `Models/PdfPreviewRenderCache.cs`

Verify:

- import Croton / Jordan Lane;
- `source_pdf.json` pages have sheet label/scale candidates;
- page tree visible names and scales appear immediately after import;
- first page opens are cache-backed after warmup.

### Phase L - Tree virtualization and batch UI refresh

Goal:

- Huge jobs stay responsive in trees.

Changes:

- Keep index from `MainWindow.PagesTreeIndex.cs`.
- Add page/takeoff view models for virtualized tree/list where WPF TreeView
  becomes too slow.
- Batch UI changes:
  - import;
  - move;
  - copy;
  - delete;
  - color randomization.

Files:

- `MainWindow.PagesTree*.cs`
- `MainWindow.TakeoffsTree*.cs`
- future extracted controls/models.

Verify:

- 1k+ page/takeoff nodes still select/move without full UI freeze.

## 9. Suggested order

Recommended order for the next work:

```text
1. Phase A - benchmark harness
2. Phase B - page paint mip/overview cache
3. Phase C - raw detail tile output
4. Phase D - PyMuPDF DisplayList cache
5. Phase E - fixed detail tile grid
6. Phase F - priority render scheduler
7. Phase G - full job warmup service
8. Phase H - Settings UI for quality/RAM
9. Phase I - Docnet cache
10. Phase J - layer pipeline optimization
11. Phase K - import pipeline acceleration
12. Phase L - tree virtualization/batch UI refresh
```

Reasoning:

- Phase A gives numbers so we stop guessing.
- Phase B attacks visible pan/zoom frame freezes even when render cache is good.
- Phases C-E attack the zoom clarity path directly.
- Phase F is needed before large warmup so background work cannot starve user
  interaction.
- Phase G/H turn RAM and warmup into controlled product behavior.
- Phase I/J/K/L improve cold paths and big-project workflows.

## 10. Verification protocol for every speed phase

For every phase:

```powershell
git diff --check
dotnet build .\ourplanecore.sln
dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build
dotnet publish .\ourplanecore.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o .\bin\publish
```

Then:

1. Replace:
   `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`
2. Keep `.bak`.
3. Retarget shortcut:
   `C:\Users\User\Desktop\OurPlaneCore.lnk`
4. Launch packaged exe.
5. Read log after latest `Application startup.`:
   - process alive;
   - `ERROR = 0`;
   - `Loaded takeoffs`;
   - `Viewport` lines present.

Performance-specific manual checks:

- Open Croton job.
- Switch 20 sheets quickly.
- Zoom to 250%, 350%, 800%.
- Pan in all directions.
- Select multiple takeoffs and verify Pages tree sync.
- Test sheet overlay visible/edit mode.
- Test joist area labels:
  - overall summary label;
  - per-joist labels toggle;
  - selected area label.
- Import a fresh PDF project and confirm auto naming/scale.

## 11. Metrics to add to logs

Current logs are useful, but need more precise metrics:

```text
PageOpen firstFrameMs
PageOpen previewCacheHit
PageOpen baseCacheHit
PageOpen liveBaseRenderQueued
RenderQueue activeDepth
RenderQueue prefetchDepth
RenderQueue canceledBeforeStart
RenderQueue staleDroppedAfterFinish
DetailTile visibleMs
DetailTile renderMs
DetailTile decodeMs
DetailTile source=cache|worker|prefetch
Paint pageBitmapMs
Paint detailTileMs
Paint overlayMs
Paint labelsMs
Cache layerBitmapBytes
Cache detailTileBytes
Cache docnetBytes
Cache persistedMemoryBytes
```

The important metric is not only render time. It is:

```text
time from user action to useful visible result
```

For page open this is first readable frame. For zoom this is first crisp visible
detail tile.

## 12. Concrete file ownership for future work

Render service:

- `Models/PdfLayerRenderService.cs`
- `Models/PdfLayerRenderService.Render.cs`
- `Models/PdfLayerRenderService.Worker.cs`
- `Models/PdfLayerRenderService.Protocol.cs`
- `Tools/pdf_layers_helper.py`

Viewport render and paint:

- `Controls/PdfViewport.PageApi.cs`
- `Controls/PdfViewport.Rendering.cs`
- `Controls/PdfViewport.DetailRender.cs`
- `Controls/PdfViewport.DetailPrefetch.cs`
- `Controls/PdfViewport.RenderCache.cs`
- `Controls/PdfViewport.ViewTransform.cs`
- `Models/ViewportRenderPolicy.cs`

Overlays/measurements:

- `Controls/PdfViewport.MeasurementRendering.cs`
- `Controls/PdfViewport.SelectionOverlayRendering.cs`
- `Controls/PdfViewport.JoistRendering.cs`
- `Controls/PdfViewport.SheetOverlay.cs`

Page open and trees:

- `MainWindow.PageTabs.cs`
- `MainWindow.PagesTree.cs`
- `MainWindow.PagesTreeIndex.cs`
- `MainWindow.PagesSelection.cs`
- `MainWindow.TakeoffSelectionNavigation.cs`
- `MainWindow.PageTakeoffLegend*.cs`

Import/metadata:

- `Models/PdfSheetMetadataService.cs`
- `Tools/pdf_layers_helper.py`
- `MainWindow.PagesImport*.cs`
- `Models/PdfPreviewRenderCache.cs`

Settings:

- `Models/AppSettings.cs`
- `Models/ViewportRenderPolicy.cs`
- `MainWindow.SettingsManager*.cs`

Tests:

- `Tests/TakeoffsTreeRegressionTests.cs`
- `Tests/Program.cs`
- future perf smoke tests/scripts.

## 13. Product behavior target

The app should feel like this:

1. User clicks a sheet.
2. The sheet appears immediately from preview/base cache.
3. If user is at 250-350%, text sharpens within a fraction of a second from
   visible detail tile.
4. User pans; already-rendered tiles stay sharp, missing tiles fill by priority.
5. Background prefetch warms nearby sheets but never freezes the current one.
6. RAM grows while working, because caches are filling.
7. Reopening sheets becomes instant because RAM cache and persisted cache both
   hit.
8. Importing a new PDF starts metadata + preview warmup automatically and shows
   progress.
9. Pages/Takeoffs selection and movement stay responsive because tree refresh is
   indexed and batched.

## 14. Current risk register

| Risk | Why it matters | Mitigation |
| --- | --- | --- |
| Detail tiles still arrive too late | User sees blur at 250-350% | Raw pixels + DisplayList + smaller first tile |
| Background warmup creates CPU contention | User perceives worse lag | Priority scheduler before aggressive warmup |
| RAM caps are hidden constants | User wants to control 32/64 GB machines | Settings UI with Auto/Custom |
| Layer pages still live-render slowly | 148/434 layer sheets hit 800-1800 ms | Lazy layer pipeline + cache/default clean state |
| Mip cache makes navigation too soft | Speed fix can look like blur | Use only during active navigation/far zoom; detail after idle |
| Docnet cache leaks native handles | Fallback cache can destabilize app | Small LRU, explicit dispose, tests |
| Tree indexes go stale | Wrong row selection/sync | Rebuild on full reload; register/unregister subtree on changes |

## 15. Definition of "done" for the next serious speed milestone

The next milestone should be considered done only when all are true:

- Packaged exe is deployed to:
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`
- Desktop shortcut points to update exe.
- App log after latest startup has `ERROR=0`.
- Croton/Jordan Lane manual perf run shows:
  - warm page switch first frame `< 50 ms`;
  - no blank viewport;
  - zoom 250-350% gets crisp detail tile `< 250-350 ms`;
  - pan does not produce repeated `Viewport slow frame > 45ms`;
  - repeat page opens use `layer-memory`/persisted cache;
  - no joist label regression;
  - no sheet overlay regression.
- Regression tests still pass.

## 16. Next recommended immediate task

Do **Phase A + Phase B** next.

Reason:

- The log proves slow frames are currently in `page:` paint even without
  measurements.
- If we only make render faster but paint still takes 100-200 ms, the app will
  still feel frozen.
- A benchmark harness gives proof before/after.
- A mip/overview cache is lower risk than rewriting the worker protocol first.

After that, do **Phase C + D** to make crisp detail tiles arrive much faster.

## 17. Summary for future agents

Do not restart from vague "make it faster". Use this order:

1. Measure page open/zoom/pan with a repeatable harness.
2. Fix paint slow frames with mip/overview cache.
3. Speed clipped detail render with raw pixels.
4. Cache PyMuPDF display lists.
5. Convert detail to fixed LOD tile grid.
6. Add priority render scheduler.
7. Add job warmup and quality/RAM settings.
8. Optimize layer-heavy pages and import pipeline.

Keep every phase separately shippable, separately revertible, tested, and
verified from the packaged app log.
