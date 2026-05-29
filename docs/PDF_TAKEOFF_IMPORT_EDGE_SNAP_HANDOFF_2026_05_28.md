# PDF Takeoff Import, Edge Snap, Sheet Overlay Cache - 2026-05-28

## Status

- Code commit: `eae0c21 Add PDF takeoff import and edge snap`.
- Follow-up commit: `c795a43 Add PDF takeoff import preview`.
- Follow-up commit: `2158bd0 Fix PDF takeoff preview counts`.
- Safety checkpoint before the risky slice: `checkpoint/before-sheet-overlay-cache-edge-pdf-scope-20260528-1940`.
- Safety checkpoint before the preview follow-up: `checkpoint/before-pdf-takeoff-import-preview-20260528-1950`.
- Safety checkpoint before the preview count fix: `checkpoint/before-pdf-takeoff-preview-count-fix-20260528-2052`.
- Deployed package: `%USERPROFILE%\Desktop\updates\OurPlaneCore\ourplanecore.exe`.
- Deployed exe size: `176,580,211` bytes.
- SHA256: `2459FBFAC0E6A71C8A7DA07C75F0723651AA0E5BE7621D00D9EA5882258340DF`.
- Rollback file kept: `ourplanecore.exe.bak` (`417,528,789` bytes, the previous unsqueezed package from the same feature build).
- Desktop shortcut target and working directory were verified against the update package folder.

## What Changed

### PDF Takeoffs Import

The app now has a first-class PDF takeoff import command:

- Job ribbon: `PDF Takeoffs` button next to the PlanSwift import button.
- Page tab Add group: import button near `Add Pages`.
- Sheet Manager toolbar: import button near `Import PDF(s)`, so it sits with the PDF import scope workflow.
- Open/Import menu: `Import PDF Takeoffs from Folder...`.
- Command palette id: `file.importPdfTakeoffs`.

Import behavior:

- The user selects a folder; PDFs are scanned recursively.
- The scan is read-only first. Before writing job files, the user sees a confirmation preview with PDFs found, pages to import, measurement count, destination buckets, and the largest type/color groups.
- If the user cancels the preview, no pages, folders, takeoff items, or measurements are created.
- If the PDFs contain no supported annotation geometry, the app reports that clearly instead of throwing an import failure.
- The preview `Takeoff items to create` count matches the real import behavior: groups are counted per PDF folder, not once globally across all PDFs.
- Imported pages are created under the selected Pages scope, inside a `from pdf` bucket.
- Imported takeoff items are created under the selected Takeoffs scope, inside a `from pdf` bucket.
- Each PDF gets its own folder, and takeoff items are grouped by annotation kind plus color, for example `Line #E52237`, `Area #6AD928`, `Point #0000FF`.
- Page scale is read from PDF annotation `/Measure` data when available and saved through the normal page scale store.
- A per-job markdown import report is written under `import_reports/pdf_takeoff_import_yyyyMMdd_HHmmss.md`.

Supported PDF annotation objects in this pass:

- `/Line` via `/L`.
- `/PolyLine` and `/Polygon` via `/Vertices`.
- `/Circle` as a point from the annotation rectangle center.
- Stroke/fill colors are normalized as `#RRGGBB`.

This is annotation/vector import, not OCR and not raster recognition.

### Edge Snap

Viewport snap now supports snapping to existing takeoff edges:

- Works with normal Snap enabled.
- Applies in Line and Area tools.
- Activates when no drawing point is already in progress.
- Uses the active page measurement spatial index, so it follows existing page-local measurements.
- Hover near an existing area/line segment highlights the candidate edge.
- `Tab` cycles selection:
  - single edge,
  - adjacent edges,
  - whole contour or full polyline.
- Click commits the highlighted edge set into the active drawing tool.

For closed area contours, whole-contour mode can finalize the new area directly.

### Sheet Overlay Clarity

Sheet overlay rendering now has a persisted tinted PNG cache:

- New cache owner: `Models/SheetOverlayRenderCache.cs`.
- Default root: `%LOCALAPPDATA%\OurPlaneCore\render-cache\sheet-overlay`.
- Optional override: `OURPLANECORE_SHEET_OVERLAY_CACHE_ROOT`.
- Cache key includes overlay PDF path, modified time, length, page index, render scale, tint color, opacity, and PDF layer state key.
- `MainWindow.SheetOverlay.cs` reads the cache before rendering and writes after tinting.
- Cache hits log `Sheet overlay cache hit; base=...; overlay=...`.

The goal is that repeated underlayment/overlay views reuse the already tinted bitmap instead of rerendering and retinting on every open.

## Real Sample Verification

User-provided Seton sample PDFs were scanned with the new helper and then through the C# import service.

| PDF | Pages | Measurements | Scale found |
| --- | ---: | ---: | --- |
| `framing.pdf` | 10 | 260 | `0.01693334688 m/pt` |
| `interior.pdf` | 6 | 34 | `0.01693334688 m/pt` |
| `roof+eve and rake+SQFT.pdf` | 9 | 142 | `0.01693334688 m/pt` |
| `siding+exterior.pdf` | 4 | 62 | `0.01693334688 m/pt` |
| `walls+gables+windows and doors.pdf` | 7 | 171 | `0.01693334688 m/pt` |
| Total | 36 | 669 |  |

Temp job storage smoke:

- Imported pages: `36`.
- Grouped takeoff items: `47`.
- Saved measurements: `669`.
- Reload confirmed: `loadedItems=47`, `loadedMeasurements=669`.

Preview count check after the follow-up fix:

| PDF | Takeoff items to create | Measurements |
| --- | ---: | ---: |
| `framing.pdf` | 13 | 260 |
| `interior.pdf` | 3 | 34 |
| `roof+eve and rake+SQFT.pdf` | 14 | 142 |
| `siding+exterior.pdf` | 5 | 62 |
| `walls+gables+windows and doors.pdf` | 12 | 171 |
| Total | 47 | 669 |

## Verification Commands

Passed:

```powershell
python -m py_compile .\Tools\pdf_layers_helper.py
dotnet build .\ourplanecore.sln
dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build
git diff --check
```

Results:

- Build: `0 Warning(s)`, `0 Error(s)`.
- Tests: `248/248 tests passed`.
- Conflict-marker scan: no findings.
- `git diff --check`: no whitespace errors; only existing CRLF conversion warnings.

Follow-up preview iteration verification:

```powershell
dotnet build .\ourplanecore.sln
dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build
git diff --check
```

Results:

- Build: `0 Warning(s)`, `0 Error(s)`.
- Tests: `248/248 tests passed`.
- Conflict-marker scan on touched files: no findings.
- `git diff --check`: no whitespace errors; only existing CRLF conversion warnings.

Preview count fix verification used the same build/test commands and a direct Seton PDF scan. The scan confirmed `47` takeoff items to create and `669` measurements, matching the temp job import smoke.

## Package Verification

Compressed publish command:

```powershell
dotnet publish .\ourplanecore.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o .\publish\ourplanecore-working-single-20260528-1944-compressed
```

Final compressed publish for the preview follow-up:

```powershell
dotnet publish .\ourplanecore.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o .\publish\ourplanecore-working-single-20260528-1958-compressed
```

Final compressed publish for the preview count fix:

```powershell
dotnet publish .\ourplanecore.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o .\publish\ourplanecore-working-single-20260528-2058-compressed
```

Packaged app launch check:

- Initial test launch PID: `13032`.
- Final preview-follow-up test launch PID: `16608`.
- Final preview-count-fix test launch PID: `17752`.
- Log file: `%APPDATA%\OurPlaneCore\logs\app-20260528.log`.
- Final startup marker line: `547`.
- `ERROR` entries after that marker: `0`.
- Startup tail included `Loaded takeoffs tree with 358 item(s)`.
- The test process was closed after verification so the deployed exe is not locked.

## Caveats / Next Safe Steps

- PDF takeoff import currently covers standard annotation geometry. If a PDF only has flattened/raster drawings or non-annotation markups, it will need a separate trace/vector extraction pass.
- Color-based type selection is intentionally conservative: it creates grouped editable takeoff items by kind and color, instead of guessing user-specific trade names.
- Edge snap is only active when normal Snap is enabled and there is no in-progress drawing point. This avoids changing the existing click-by-click drawing behavior mid-segment.
- Sheet overlay cache improves repeat overlay opens. First-time overlay render still pays the normal render cost.
