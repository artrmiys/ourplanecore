# F1, Viewport, Overlay, and Project Storage Handoff — 2026-07-22

## Outcome

This slice completes the requested F1 shortcut refresh, removes redundant work
from static-sheet navigation and sheet overlays, adds a safe Project Storage
audit/compact surface, and fixes Cut Area clipping for Extra Joists. The
release version is `2.2.4`.

The permanent executable URL is:

`https://github.com/artrmiys/ourplanecore/releases/latest/download/ourplancore.exe`

The GitHub release pipeline must upload the asset as exactly
`ourplancore.exe`, mark the new release as latest, and verify that URL against
the local release SHA-256 before the release is considered complete.

## F1 shortcut guide

- `Models/KeyboardShortcutCatalog.cs` is the canonical catalog used by the F1
  overlay.
- The overlay is modal to application shortcuts and scrollable, so a long map
  cannot extend outside the window.
- The guide includes context-sensitive commands, including plain `D` for
  continuous Extra Joists while one Joist Area Segment is selected, and its
  normal Draw Line meaning in other contexts.
- Commands that are not reachable from current input routing are not advertised
  as working shortcuts.

## Cut Area and Extra Joists

The previous Cut Area path assigned each persisted Extra Joist to one result by
its midpoint and copied the original endpoints unchanged. An extra crossing an
inner hole could therefore remain drawn through the removed fill, disappear if
its midpoint was cut, or stay on only one side of a through cut.

The corrected path clips the original finite segment against every resulting
`AreaBooleanGeometry` before the source Area is changed or replaced:

- an inner hole creates the two filled pieces on either side;
- a through cut distributes the pieces to both new Area measurements;
- an edge cut trims the touched endpoint;
- a fully removed extra produces no persisted piece;
- one surviving piece keeps the source ID, while additional pieces receive
  unique IDs so later merge/combine cannot deduplicate them accidentally;
- interval midpoints are classified with the same Skia EvenOdd fill geometry,
  so a tangent contact with a hole vertex does not shift intersection parity.

Regular joists still follow their existing contract: they are not persisted as
segments and are recalculated from each resulting Area geometry. Extra
endpoints are persisted, so clipping them once fixes viewport drawing, labels,
totals, takeoff export, and PDF/PlanSwift export together. Existing mixed undo,
autosave, current JSON, and legacy ProjectFile paths continue to snapshot and
serialize the resulting list.

## Static-sheet performance

The original static-raster path still launched nearby Docnet preview, readable
preview, clean layer render, and work-zoom warmup jobs. Large jobs therefore
kept many decoded full-sheet bitmaps alive even though the visible page was
already pinned to one raster.

The final policy is conservative:

- a ready page-open static raster suppresses redundant live PDF and work-zoom
  prefetch;
- its saved raster bitmap is still warmed;
- missing, stale, disabled, non-page-open, or insufficient-resolution rasters
  retain the live fallback;
- an active raster within 95% of target is accepted, matching the viewport;
- a non-active side variant suppresses fallback only at the exact effective
  target DPI, because that is the variant the build path can immediately reuse;
- PDF Layers always wins and retains its live render path;
- source-image overview metadata counts only when its file exists;
- the viewport and prefetch policy share the same 96 MP effective-DPI clamp,
  including raster-less lazy builds;
- decoded full-sheet raster cache accounting is capped at 256–768 MB instead
  of growing to 2.56 GB on high-memory machines.

The 768 MB figure is a cache-accounting ceiling, not a strict process-memory
limit: temporary bitmap copies and active zero-copy leases can briefly exceed
it, and other viewport caches have separate budgets.

## Sheet overlay integration

- A synchronous overlay cache hit no longer starts a redundant async reload
  and deep bitmap copy.
- The native overlay bitmap cache is bounded to 8 entries and an adaptive
  96–384 MB budget.
- Overlay sampling follows the current page scale instead of blindly choosing
  the most expensive path.
- Overlay bitmap decode and paint metrics remain available to the viewport
  smoke report.
- Overlay state remains page-owned; no overlay PDF or transform data moved into
  the viewport cache.

## Project Storage settings

The `8 Settings` workspace now contains `Project Storage`.

`Analyze project` is read-only and reports:

- portable canonical project data;
- rebuildable page raster data;
- recovery history;
- exact unreferenced duplicate source candidates;
- sources not referenced by current/recovery page metadata;
- sources requiring review because metadata or filesystem traversal was
  incomplete;
- external page dependencies that make the project non-portable;
- formatting-only `snap.json` savings.

The analyzer hashes source files only inside equal-size groups. Every source
referenced by current pages or recovery snapshots is protected. If any page
metadata or filesystem branch cannot be read, duplicate savings are disabled
and unreferenced sources move to `SourceNeedsReview`.

`Preview safe compact` builds an exact plan. `Compact snap.json...` requires
write access and explicit confirmation, then removes JSON formatting whitespace
only from valid `Pages/**/raster/snap.json` files. It never deletes source PDFs,
raster images, recovery history, takeoffs, measurements, or folders.

Safety boundaries:

- lexical containment plus repeated reparse/junction checks;
- cancellation on job switch;
- preview timestamp, length, and SHA-256 validation;
- the same atomic per-path lock used by the normal raster snap writer;
- a second content/path check inside that lock before atomic replacement;
- changed, missing, inaccessible, invalid, and unsafe paths are reported as
  skipped issues instead of overwritten.

## Real project analysis

All runs below were read-only. No real job was compacted.

| Project | Files | Total | References | `snap.json` savings | Duplicate savings | Warnings |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 116. Primrose Community_Bliffert | 1,266 | 818,733,154 B | 384 | 198,712,542 B | 0 B | 0 |
| 118. Meadowview_Bliffert_review RUSH | 157 | 71,346,265 B | 56 | 16,238,475 B | 0 B | 0 |
| 115. Reeve Drive Aptm_Bliffert | 1,916 | 625,435,467 B | 420 | 74 B | 0 B | 0 |

The earlier broad Reeve estimate treated referenced equal files as removable.
The hardened analyzer correctly reports zero duplicate savings because current
or recovery metadata still references those files.

## Repeatable viewport evidence

The same copied 241-page Carillon job was exercised before and after the
static-prefetch/cache change. Both runs opened 30 sampled pages, returned to 6
pages, opened and returned through 5 tabs, exercised pan/zoom, checked 3 overlay
pages, and passed every opacity probe.

| Metric | Before | After |
| --- | ---: | ---: |
| Working set | 3,275 MB | 1,264 MB |
| Initial page settle | 691 ms | 635 ms |
| Worst complete step | 1,303 ms | 962 ms |
| Clean-render prefetch decodes | 59 | 4 |
| Overlay checks | 3 | 3 |
| Render cache hit rate | 100% | 100% |

Reports:

- `cache/perf_baseline/debug-apphost-after-carillon-overlay.json`
- `cache/perf_baseline/debug-apphost-after-carillon-overlay-prefetch-cap.json`

The smoke launcher now resolves a relative report path against the repository
root even when a direct packaged/AppHost executable uses another working
directory. It also isolates settings and explicitly controls static raster DPI,
static-mode disable, and black-vector options.

## Verification

- Debug solution build: `0 warnings / 0 errors`.
- Full C# regression harness: `592/592` passed. The added cases cover a hole
  split, tangent contact, through-cut distribution, edge trimming, untouched
  ID/endpoints, and full removal.
- Carillon viewport/overlay stress: PASS.
- Storage analysis was run read-only against Primrose, Meadowview, and Reeve.
- A copied Meadowview job compacted 13/13 eligible `snap.json` files, saved
  16,238,475 bytes, reported 0 issues, and passed semantic JSON equality for
  every file. The verified temporary copy was then removed.
- The installed v2.2.4 startup segment in `app-20260722-1.log` contains
  `Loaded takeoffs` and `Viewport`, contains `0 ERROR`, and ends with
  `Application exit 0`.
- A packaged smoke using a copied Meadowview job opened 14 pages, returned to 6
  samples, exercised 5 new tabs, passed every opacity probe, reported 100%
  render-cache hits, 0 slow frames, 1,116 MB working set, 251 ms initial page
  settle, and a 1,070 ms worst complete step. Report:
  `cache/perf_baseline/packaged-v2.2.4-88a1f7f-meadowview.json`.

## Ownership

- F1 catalog: `Models/KeyboardShortcutCatalog.cs`.
- Page prefetch orchestration: `MainWindow.PageTabs.cs`.
- Static readiness and effective DPI: `Models/StaticRasterPrefetchPolicy.cs`.
- Bitmap budgets and prefetch workers: `Controls/PdfViewport.RenderCache.cs`.
- Static lazy/apply workflow: `Controls/PdfViewport.RasterSheetDpiUpgrade.cs`.
- Overlay cache/paint: `Controls/PdfViewport.SheetOverlay.cs` and
  `Models/SheetOverlayRenderCache.cs`.
- Storage model/analyzer/compactor: `Models/ProjectStorageModels.cs`,
  `Models/ProjectStorageAnalyzer.cs`, and `Models/ProjectStorageCompactor.cs`.
- Storage UI: `MainWindow.SettingsManager.ProjectStorage.cs`.
- Shared atomic writer: `Models/IoUtil.cs`.
- Extra Joist finite clip: `Models/JoistTakeoffCalculator.Extras.cs`.
- Cut Area distribution: `Controls/PdfViewport.AreaCutTools.cs`.
- Release automation: `Tools/release/Publish-OurPlanCoreRelease.ps1`.

## Release gate

Completed on 2026-07-22:

- source commit:
  `88a1f7f1d7f982bcef0868372809e80f425a5fb0`;
- public latest tag:
  `ourplancore-v2.2.4-20260722-88a1f7f`;
- release page:
  `https://github.com/artrmiys/ourplanecore/releases/tag/ourplancore-v2.2.4-20260722-88a1f7f`;
- installed compressed single-file ProductVersion:
  `2.2.4+88a1f7f1d7f982bcef0868372809e80f425a5fb0`;
- EXE length: `171,809,721` bytes;
- EXE SHA-256:
  `73DF3B45D9A6A8D19A6BF264D199F5CF239ABE647D0C60E8054466710697B469`;
- sanitized release template SHA-256:
  `A8AEE59CA9D125213317B413416C14E325F0254BDE7FAA8128E4D9B364C0DADF`;
- local rollback preserved as
  `ourplancore.exe.bak-20260722-153952-2002e138953e`;
- Desktop shortcut target and working directory both point to
  `Desktop\updates\OurPlanCore`;
- GitHub release is latest, non-draft, and non-prerelease;
- GitHub asset metadata and independent anonymous pinned/latest downloads all
  match the installed EXE length and SHA-256.

Permanent application URL:

`https://github.com/artrmiys/ourplanecore/releases/latest/download/ourplancore.exe`
