# PDF Render Performance Status - 2026-05-28

## Current Status

This is a post-fix verification note after these shipped slices:

- persisted clean PyMuPDF preview cache;
- deferred page-open UI work;
- bounded inline PNG render responses with file fallback.

The deployed packaged exe is still:

- `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`
- SHA256:
  `FDE0FCF63BF5C3B7555DE5C4DB9C0C0CC1190B1E7341BE11DED326E547A26EDE`

The working tree was clean except for the unrelated untracked file:

- `Assets/ourplanecore.ico.bak_20260522_132816`

## Verification Run

Commands/checks run from `C:\Users\User\Desktop\ourplanecore`:

```powershell
git diff --check
rg "TODO|throw new NotImplementedException|<<<<<<<|>>>>>>>|=======" -g "!bin/**" -g "!obj/**" -g "!cache/**" -g "!reference/**"
dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false
```

Results:

- `git diff --check`: pass.
- Conflict/TODO scan: no actionable matches.
- Verify build: pass, `0 warnings / 0 errors`.
- Packaged log after the latest `Application startup`: `0` errors.

## Confirmed From Code

### Worker Queue Still Serializes PyMuPDF

File:

- `Models/PdfLayerRenderService.cs`

Evidence:

- `WorkerSemaphore = new(1, 1)` is still active.
- `TryInvokeWorkerAsync(...)` waits on that semaphore before all worker actions.
- Render, layer list, layer trace, layer probe, and PDF snap paths still share
  the same serialized worker lane.

Conclusion:

- This is still the biggest remaining bottleneck when page switching, prefetch,
  snap, layer discovery, or trace requests overlap.

### Preview Cache Is Working

Files:

- `Controls/PdfViewport.PageApi.cs`
- `Controls/PdfViewport.Layers.cs`
- `Models/PdfPreviewRenderCache.cs`

Evidence:

- `LoadPage` tries `TryApplyPersistedPreviewRender(...)` before
  `QueueLayerRender(...)`.
- Runtime log contains real `Viewport PyMuPDF preview cache hit` entries.

Log counts from `%APPDATA%\OurPlaneCore\logs\app-20260528.log`:

- Total `Viewport PyMuPDF preview cache hit`: `7`
- Total `Viewport slow layer render`: `304`
- After latest startup:
  - preview cache hits: `6`
  - slow layer renders: `8`
  - errors: `0`

Conclusion:

- If repeat page open still feels slow, it is not because the persisted preview
  cache is absent. The cached preview is being consumed.

### Refresh/Full-Scale Render Still Runs After Cache Hit

File:

- `Controls/PdfViewport.PageApi.cs`

Evidence:

- After a preview cache hit, `LoadPage` still calls `QueueLayerRender(...)`.
- This is intentional for correctness: layer state, discovery, snap reload, and
  normal-quality render still need to refresh.

Log evidence:

- Cache-hit entries are followed by slow layer render entries.
- Slow render scale distribution in the current log:
  - `scale=1`: `173`
  - `scale=1.5`: `66`
  - `scale=1.917`: `27`
  - `scale=0.35`: `38`

Conclusion:

- The remaining perceived lag is mostly the refresh/full-scale render path, not
  first-preview cache miss.

### Hidden-Layer Reopen Still Exists But Is Lower Priority

File:

- `Tools/pdf_layers_helper.py`

Evidence:

- `_render_samples_for_states(...)` still checks `has_hidden_layers`.
- When any layer is hidden, it opens a fresh `fitz.Document` using
  `fitz.open(pdf_path)`, applies render states, filters page content, renders,
  and closes the document.

Conclusion:

- This affects layer-toggle/hidden-layer workflows, not ordinary page open with
  all layers visible.

### Layer Discovery Is Not The Main Caretta Repeat-Open Cost

File:

- `Tools/pdf_layers_helper.py`

Evidence:

- `render_data(...)` still falls back to discovery when `visible_layers` is
  missing.
- Checked Caretta representative pages:
  - `Bldng#1\Arch\a201.1 rf`: `pdf_layers_cached=True`, `layers_count=0`
  - `sections\a456 d sec`: `pdf_layers_cached=True`, `layers_count=0`

Conclusion:

- The discovery fallback remains a possible first-open cost on uncached pages,
  but it is not the main reason current Caretta repeat opens still log slow
  renders.

## Current Bottleneck Ranking

1. **Full-scale PyMuPDF render after preview cache hit**
   - The app shows cached preview, then still performs refresh render.
   - Logs show most slow renders are `scale=1`, `1.5`, or `1.917`, not only
     preview `0.35`.
2. **Single PyMuPDF worker semaphore**
   - All worker traffic is serialized through one process.
   - This matters most when prefetch/snap/render/trace overlap.
3. **Hidden-layer fitz reopen**
   - Important for layer-toggle users, lower priority for normal page open.
4. **Layer discovery fallback**
   - Still exists, but lower priority for currently checked Caretta pages
     because layer metadata is already cached and empty.

## Recommendation

The next safest performance step for a build that will go to other users is:

1. Add a bounded persisted normal-render cache for clean PyMuPDF renders at the
   common refresh scales, keyed by:
   - source PDF full path or stable source identity;
   - modified time;
   - file length;
   - page index;
   - render scale;
   - layer-state hash;
   - highlight-state hash.
2. Use it only for clean/default states first:
   - no highlighted layers;
   - no hidden layer states;
   - common scales such as `1`, `1.5`, and `1.917`.
3. Keep PyMuPDF refresh available as fallback.
4. Do not start with a worker pool unless there is a clear concurrency plan.

Reason:

- Full-scale cache is lower risk for distribution. It does not change worker
  ordering, PDF layer state mutation, process lifetime, or memory concurrency.
- Worker pool can help more, but it is riskier: it can increase memory pressure,
  expose layer-state ordering bugs, and make failures harder to reproduce on
  other users' machines.

## What Not To Chase First

- Do not prioritize layer discovery for current Caretta repeat-open lag unless
  logs show uncached layer discovery on the specific pages being opened.
- Do not optimize hidden-layer reopen before confirming the user is actively
  toggling/hiding PDF layers.
- Do not remove the refresh render after preview cache hit without a correctness
  replacement; it currently protects normal-quality display and layer/snap state.

## Related Handoffs

- `docs/PDF_PREVIEW_CACHE_HANDOFF_2026_05_28.md`
- `docs/PAGE_OPEN_UI_PERF_HANDOFF_2026_05_28.md`
- `docs/PDF_INLINE_RENDER_HANDOFF_2026_05_28.md`

