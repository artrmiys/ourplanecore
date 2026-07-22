# Static Raster Page Mode Handoff - 2026-07-22

## Status

The source implementation is committed as
`de7dcefde9f2e351e24df011e83a9d379b2b313a` (`Add PlanSwift-style static
raster page mode`) on `feature/ourcore-design-overhaul`.

The source is currently verified, but the installed package is not yet a clean
commit-attributable release:

- commit: `17` files, `630` insertions, `3` deletions;
- restore succeeded for the app, tests, and PlanSwift import tool;
- `dotnet build .\ourplancore.sln --no-restore` passed with
  `0 warnings / 0 errors` after restore;
- the full regression harness passed `558/558` via
  `dotnet run --project .\Tests\OurPlanCore.Tests.csproj --no-build --no-restore`;
- the installed EXE exhibits the new static-raster behavior in real-job logs,
  but it was published before the source commit and its version metadata still
  names the parent commit;
- the latest packaged runtime segment has one real UI exception plus six
  intentional corrupt-fixture test records, so packaged release validation
  must not be marked clean yet.

## Confirmed Behavior

### Activation and fallback

`PdfViewport.IsStaticRasterDisplayActive()` is the central gate. Static display
is active only when all of these are true:

- `ViewportRenderPolicy.StaticRasterModeEnabled` is on;
- a raster sheet is actually being displayed;
- the live layer renderer is not active;
- PDF layers are not loaded for the current page.

Consequences while the gate is active:

- wheel zoom and pan transform the existing bitmap instead of requesting a new
  PDF render;
- detail-tile rendering is suppressed;
- raster motion warmup and DPI-ladder refresh work are suppressed;
- the recurring whole-job raster refresh cadence does not run;
- the page mip cache and fixed-bitmap sampling handle navigation frames.

Turning on `PDF layers` leaves the legacy live PDF-layer pipeline available.
A page that has not obtained a raster yet stays on the dynamic path until the
background lazy build succeeds.

### Static resolution

The Display ribbon exposes `Static image`, `Black vector`, and `DPI`.

- `Static image` defaults to on.
- `Black vector` defaults to off.
- Static DPI defaults to `150` and accepts `72-300` in the UI.
- The values persist through the existing per-user `AppSettingsStore` path.
  No Settings Manager preset or per-job override was added in this change.

For a PDF-derived raster sheet, the target image is built once into the page's
disk raster cache and then pinned. The code accepts a current raster within
about five percent of the selected target and never rebuilds a higher-DPI image
downward. Genuine increases, such as `100 -> 150` or `150 -> 300`, queue one
rebuild.

`SafeStaticRasterTargetDpi()` can lower the effective target below the UI
minimum for an exceptionally large sheet. This is intentional: the responsive
pixel budget wins over the nominal DPI setting to avoid a very large bitmap and
out-of-memory risk.

Pages from older jobs that open without a usable raster queue a background
static lazy build. After success, the page switches to the cached fixed image
and future opens reuse it.

### Black vector overlay

The optional overlay draws the already-loaded black PDF snap segments above the
raster. It does not request a new PDF render, so source linework remains sharp
when the bitmap softens at deep zoom.

Limitations are deliberate:

- scanned/image-only PDFs have no vector segments to draw;
- text, fills, and hatches remain raster content;
- only segments intersecting the visible PDF bounds are drawn;
- a fast-navigation frame skips the overlay when the page exceeds the
  `BlackVectorOverlayFastFrameSegmentCap` safety limit.

## File Ownership

Settings and UI:

- `Models/AppSettingsStore.cs`: persisted defaults, DPI normalization, and
  range constants;
- `Models/ViewportRenderPolicy.cs`: runtime static-mode target and dense-overlay
  safety cap;
- `MainWindow.DisplaySettings.cs`: toggle persistence and viewport application;
- `MainWindow.DisplaySettings.MeasurementSizing.cs`: DPI parsing and apply
  action;
- `MainWindow.xaml`: Display ribbon controls and operator tooltips.

Viewport behavior:

- `Controls/PdfViewport.ViewTransform.cs`: central activation gate and
  zoom/navigation short-circuits;
- `Controls/PdfViewport.RasterSheetDpiUpgrade.cs`: target-DPI application,
  pixel-budget clamp, and lazy migration;
- `Controls/PdfViewport.DetailRender.cs`: detail-render suppression;
- `Controls/PdfViewport.RenderCache.cs`: whole-job refresh suppression;
- `Controls/PdfViewport.Rendering.cs`: fixed-bitmap sampling and black vector
  overlay;
- `Controls/PdfViewport.PageMipCache.cs`: static bitmap mip selection;
- `Controls/PdfViewport.PageApi.cs`: page-open behavior and lazy-build entry;
- `Controls/PdfViewport.Layers.cs`: post-raster-load DPI application;
- `Controls/PdfViewport.ViewCommands.cs`: operator-requested DPI refresh;
- `Controls/PdfViewport.cs`: overlay state.

Regression guard:

- `Tests/TakeoffsTreeRegressionTests.cs` owns
  `StaticRasterModeSuppressesLiveReRenders()`;
- `Tests/Program.cs` registers that case in the custom full harness.

Maintenance note: `Controls/PdfViewport.RasterSheetDpiUpgrade.cs` is now about
`1,290` physical lines, above the repository's `1,000`-line partial-file limit.
Future work in this area should extract the static-DPI/lazy-build responsibility
instead of growing that partial further.

## Installed Package and Runtime Evidence

Package observed during the 2026-07-22 audit:

- path: `Desktop\updates\OurPlanCore\ourplancore.exe`;
- modified: `2026-07-22 00:38:37 -03:00`;
- size: `174,267,111` bytes (compressed single-file range);
- SHA-256:
  `0FC5A7A02FC86E740224C660279E7F8DF9CF8E91B941DC53BB16602A066EEE38`;
- ProductVersion:
  `2.2.3+ea5dc1799040815363ea0721ab3d6c3e301ef749`;
- rollback retained as `ourplancore.exe.bak` from 2026-07-18;
- `Desktop\OurPlanCore.lnk` targets the package and uses its folder as the
  working directory.

The package timestamp is approximately three minutes earlier than commit
`de7dcef` (`00:41:56`). The older ProductVersion is therefore expected. The
runtime proves that the binary contains the feature, but metadata and hash do
not prove that it is a clean publish of `de7dcef`.

Real-job app-log evidence in `app-20260722.log`:

- a pre-publish run queued a static lazy build at `00:28:54` for `150 DPI`;
- the resulting bitmap was warmed and the log recorded
  `source='static-lazy'; dpi=150` at `00:28:55`;
- after the installed package started at `03:50:19`, a second real page queued
  a 150-DPI lazy build at `04:02:10`, warmed the fixed bitmap, and subsequently
  hit that raster cache at `04:02:13`.

The latest startup began at log line `341` (`03:50:19`). The process remained
alive and the segment contains `Loaded takeoffs` and many `Viewport` records.
As of `05:20`, the raw segment contains seven `ERROR` records. Six were written
at `04:20` by the full regression harness while intentionally reading corrupt
fixtures under temporary `onc_tests` folders. The remaining genuine runtime
error occurred at `04:18:09`:

```text
Unhandled UI exception: An item with the same key has already been added.
Key: pages.nameScaleSetup
RebuildSideCommandStrip() -> ApplyModuleAvailability() -> ApplyModuleDraft()
```

The static-raster commit did not modify the Modules/side-command-strip files,
so causation is not established. The error is still a release-gate failure and
must remain visible until separately diagnosed. A future package check also
needs a new startup marker after the test run so fixture logging cannot pollute
the validation segment.

## Workspace Exclusions

Do not fold these pre-existing items into the 2026-07-22 feature commit or a
future documentation commit:

- `Tests/PlanSwiftImportTests.cs` appears modified in `git status`, but its
  working hash equals the staged blob and `git diff` is empty. This is a
  mixed-line-ending/stat-cache condition, not a content change.
- `docs/60-ux-ui/` contains old untracked files dated from May through
  2026-07-07. They are not today's work.

## Remaining Release Checks

1. Investigate and fix or otherwise account for the duplicate
   `pages.nameScaleSetup` command key before claiming a clean packaged run.
2. From a clean source state at `de7dcef`, restore if needed, build with zero
   warnings/errors, run the full harness, and publish the compressed
   single-file package.
3. Preserve the existing rollback; do not overwrite it. Replace the installed
   EXE transactionally and reconfirm the Desktop shortcut target and working
   directory.
4. Launch the packaged EXE, wait for the loaded job/viewport path, and inspect
   only the segment after the newest `Application startup.` marker. Pass
   requires the process alive, no `ERROR`, and both `Loaded takeoffs` and
   `Viewport` evidence.
5. Confirm static mode on a PDF-derived page, a legacy page with no raster, a
   scanned page, and a vector page with `Black vector` enabled. Also confirm
   that enabling `PDF layers` restores the live layer pipeline.
