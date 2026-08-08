# Sharp Sheets, Pitch, Value Export, and Viewport Handoff - 2026-08-08

## Status

Implemented, tested, packaged, and installed. This file is the canonical
handoff for the 2026-08-08 sheet-quality, Pitch, Value, and compact Viewport
release.

Implementation commits:

- `a70db69` - `Add sharp sheets pitch and value export`
- `e2a2fc0` - `Align viewport display settings`

## Confirmed behavior contract

### Sharp sheet opening

- An image-backed PlanSwift PNG/TIF sheet opens with its full native raster.
  The viewport no longer intentionally paints the low-resolution overview
  first and swaps to the full image after a delay.
- This is not a blanket hard-coded `150 DPI` replacement. Source-image sheets
  keep their actual prepared native source, including existing 200-DPI
  working rasters.
- Full and overview bitmaps have separate cache identities. The full bitmap is
  warmed with interactive priority for page opening.
- Overview generation/prefetch remains available as background cache
  maintenance and for the existing deliberate low-zoom/navigation paths. An
  overview-only refresh cannot replace a full native sheet already on screen.
- Page opening still preserves the layer renderer path when PDF layers are in
  use; the sharp raster-first rule applies to image-backed raster sheets.

Primary ownership:

- Page-open selection: `Controls/PdfViewport.PageApi.cs`
- Raster application and overview guard:
  `Controls/PdfViewport.RasterSheet.cs`
- Bitmap warming: `Controls/PdfViewport.RasterSheetBitmapCache.cs`
- Full/overview cache and prefetch priority:
  `Controls/PdfViewport.RenderCache.cs`
- Current raster state and drawing:
  `Controls/PdfViewport.cs`, `Controls/PdfViewport.PageApi.cs`, and
  `Controls/PdfViewport.ViewCommands.cs`

### Pitch roof tool

- `Pitch` is located immediately beside `Ruler` in the bottom takeoff toolbar.
- Two clicks define the line. The result is calculated from absolute rise and
  run in page coordinates: `pitch = rise / run * 12`.
- The label is `rise:12`, rounded to at most two decimal places. A vertical
  line is represented as infinite pitch. The live status also shows the angle
  in degrees.
- Pitch does not use the sheet scale, so an unscaled sheet can still be
  measured.
- The result is stored as a normal page annotation with `Kind = "pitch"`.
  It uses the Ruler blue/stroke treatment and participates in annotation undo,
  save/load, detached-sheet rendering, selection rendering, and PDF export.
- The live label uses the shared live-input label size and opacity settings.

Primary ownership:

- Geometry and formatting: `Models/RoofPitchGeometry.cs`
- Tool/input lifecycle: `Controls/PdfViewport.Tools.cs`,
  `Controls/PdfViewport.Input.cs`, and
  `Controls/PdfViewport.ScaleDrawTools.cs`
- Live preview: `Controls/PdfViewport.LiveInputRendering.cs`
- Stored/selected rendering: `Controls/PdfViewport.AnnotationRendering.cs`
- Persistence: `Models/Storage/PageAnnotationStore.cs`
- PDF output: `Models/PdfExporter.Annotations.cs`
- Main/detached command wiring: `MainWindow.ToolButtonSetup.cs`,
  `MainWindow.PageTabs.cs`, `MainWindow.DetachedSheets.cs`, and
  `Dialogs/DetachedSheetWindow.cs`

### Value export

- `Value` is beside `Excel` on the bottom-right export strip.
- Both buttons resolve the same current Takeoffs selection through
  `SelectedCurrentExcelExportRoots()` and build the same ordered export rows
  through `PlanSwiftTakeoffExporter.BuildRows(...)`.
- A selected folder/group is expanded exactly as it is for the normal Excel
  button. The same active Excel workbook and selected starting cell are used.
- `Value` filters the resolved rows to item rows and writes their values into
  one vertical column. It omits takeoff names, folder/group headers, units,
  blank separator rows, and header formatting.
- Numeric-looking values are written as numbers; other values remain cleaned
  text.

Primary ownership:

- Selection and command flow: `MainWindow.TakeoffsExport.cs`
- Active workbook/cell write and value-only matrix:
  `Models/ActiveExcelTakeoffExportService.cs`
- Button placement: `MainWindow.xaml`

### Live LFT and Pitch display settings

- The controls live in `Viewport > LIVE LFT / PITCH`, not in `Display` and not
  as a hidden third row under `LINES & AREA`.
- `LFT size` accepts `8-24 px`; default is `12 px`.
- `Opacity` accepts `15-100%`; default is `75%`.
- Both slider and typed field input are supported. Enter or focus loss commits
  typed values; invalid input restores the saved value and reports the valid
  range.
- Changes repaint the main viewport, propagate to detached sheets, and persist
  through `AppSettingsStore`.
- The live text automatically uses black or white according to the current
  viewport background luminance.

Primary ownership:

- Reusable ribbon control: `Controls/LiveInputRibbonSettings.xaml` and
  `Controls/LiveInputRibbonSettings.xaml.cs`
- Validation, persistence, and propagation:
  `MainWindow.DisplaySettings.LiveInput.cs` and
  `MainWindow.DisplaySettings.cs`
- Stored defaults/ranges: `Models/AppSettingsStore.cs`
- Viewport drawing: `Controls/PdfViewport.LiveInputRendering.cs`
- Detached sheets: `MainWindow.DetachedSheets.cs` and
  `Dialogs/DetachedSheetWindow.cs`

### Compact Viewport and PDF Output layout

Viewport contains four focused groups in this order:

1. `LINES & AREA` - Line, Point, Edge, and Fill in exactly two rows.
2. `LIVE LFT / PITCH` - size and opacity.
3. `RULER / EXTRA` - Ruler thickness and Extra Joist glow in two rows.
4. `PDF SNAP` - bridge tolerance.

Layout rules shared by Viewport and PDF Output:

- horizontal `WrapPanel`
- outer margin `6,1,6,1`
- horizontal scrolling disabled and vertical scrolling available
- maximum ribbon height `176`
- standard group-body height `50`
- compact slider width `76`
- shared numeric input style `RibbonNumericValue`, size `54 x 22`

The shared numeric style is defined in
`Resources/RibbonNumericResources.xaml` and merged from
`Resources/AppResources.xaml`. The live-input settings were extracted into a
small user control so the already-large `MainWindow.xaml` did not grow further.

The dynamic Extra Joist row is installed into
`ViewportExtraJoistSettingsHost`, making `RULER / EXTRA` one block rather than
two separate laptop-width groups.

## Verification evidence

### Build and regression harness

```powershell
dotnet build .\ourplancore.sln
dotnet .\Tests\cache\verify_viewport_ruler_extra\OurPlanCore.Tests.dll
```

Result:

- build: 0 warnings, 0 errors
- tests: `670/670` passed

Regression coverage includes:

- full native source raster on image-backed page open
- no overview replacement of a displayed full sheet
- Pitch geometry, wiring, persistence, and PDF rendering
- Value matrix contents and group-selection parity
- editable live LFT/Pitch controls in Viewport
- absence of the live controls from Display
- two-row `LINES & AREA`
- combined `RULER / EXTRA`
- matching Viewport/PDF Output spacing and numeric input dimensions

### Installed runtime

Installed executable:

`C:\Users\User\Desktop\updates\OurPlanCore\ourplancore.exe`

- package: compressed self-contained win-x64 single file
- size: `174,469,025` bytes
- SHA-256:
  `6C09B0F93963B0CF657C16B6CEB87C8AE8FA197312793F03882CFF2479443575`
- Desktop shortcut target:
  `C:\Users\User\Desktop\updates\OurPlanCore\ourplancore.exe`
- Desktop shortcut working directory:
  `C:\Users\User\Desktop\updates\OurPlanCore`

Final packaged runtime segment began at `2026-08-08T19:05:24-03:00` and showed:

- process alive and responding after 25 seconds
- `Loaded takeoffs tree with 286 item(s)`
- full raster warmed and applied for the current sheet
- viewport raster cache hit with `elapsed=0ms`
- 0 `ERROR` entries after the startup marker

Rollback:

- original `ourplancore.exe.bak` preserved unchanged
- immediately preceding package preserved as
  `ourplancore.exe.pre-viewport-20260808.bak`

`TemplateCom.xlsm` was preserved. No workbook or VBA changes were part of this
release.
