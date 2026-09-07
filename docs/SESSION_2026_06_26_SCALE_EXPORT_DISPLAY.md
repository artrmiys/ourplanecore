# 2026-06-26 Session: Display Toggles, PDF Labels, Raster Sheets, and Measured Scale Reuse

## Summary

This session fixed several live user workflow issues in the packaged OurPlaneCore app:

- Display tab individual visibility toggles now work independently again.
- PDF export labels are separated so Area / Joist / measurement labels do not stack on top of each other.
- Manual decimal sheet scale entry now matches the Scale tool result.
- Pages tree can now apply the current measured sheet scale to selected sheets without measuring each sheet again.
- The deployed packaged exe was rebuilt and validated from the real update folder.

Latest deployed commit after this session: `8a800af Apply measured sheet scale`.

Packaged app path:

```text
C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe
```

## User-Facing Behavior

### Display Toggles

The Display tab master `All` toggle no longer blocks individual label toggles.

Confirmed behavior:

- Line labels can be hidden or shown independently.
- Area labels can be hidden or shown independently.
- Joist labels can be hidden or shown independently.
- Count labels can be hidden or shown independently.
- Detached sheet windows refresh the same visibility settings as the main viewport.

Key files:

- `MainWindow.DisplaySettings.cs`
- `Dialogs/DetachedSheetWindow.cs`
- `Controls/PdfViewport.MeasurementRendering.cs`

### PDF Export Label Placement

PDF export now tracks occupied label boxes while drawing measurement labels. Area summary labels, joist summary labels, and joist segment labels are offset before drawing so they do not overlap when multiple takeoffs share the same area or similar geometry.

Key files:

- `Models/PdfExporter.cs`
- `Models/PdfExporter.Measurements.cs`
- `Models/PdfExporter.JoistLabelPlacement.cs`

Tests added or updated around:

- Joist export segment label offsets.
- General PDF measurement label offsets.
- Joist display/export label separation.

### Decimal Sheet Scale Entry

The important correction: a manually measured sheet may display a decimal architectural scale such as:

```text
0.287" = 1'0"
```

Before the final fix, typing `0.287` could be interpreted as the wrong reciprocal ratio. The parser now treats bare decimal values under `1.0` as decimal inches per foot:

```text
0.287
0,287
0.287 = 1
0.287 k 1
0.287 к 1
```

All of those now resolve to the same internal scale as:

```text
0.287" = 1'0"
```

The formula is now:

```text
ratio = 12 / decimal_inches_per_foot
scale_m_per_pt = PdfPointMeters * ratio
```

This matches what the Scale tool produces after measuring a real known distance on the sheet.

Key file:

- `Models/PdfSheetMetadataService.cs`

Regression test:

- `Tests/Program.cs`, `PdfScaleParserHandlesDecimalRatioScale`

### Apply Current Measured Scale To Selected Sheets

New Pages tree workflow:

1. Open one sheet.
2. Use the Scale tool to measure/calibrate it.
3. Select other similar sheets in the Pages tree.
4. Right-click.
5. Choose:

```text
Apply Current Sheet Scale to N Selected
```

This applies the current page or viewport `ScaleMetersPerPt` to the selected sheets. It reuses the existing page-scale path, so it persists page scale, writes floating Page Setup metadata, updates page-linked measurements, flushes changed takeoff autosaves, and refreshes totals/indicators.

Key files:

- `MainWindow.PagesCommands.cs`
- `MainWindow.PagesScale.cs`

Regression test:

- `Tests/TakeoffsTreeRegressionTests.cs`, `PagesTreeSelectedSheetScaleMenuIsWired`

## Commits

Relevant commits from this session:

```text
8a800af Apply measured sheet scale
e690322 Keep decimal scale labels
9257e2e Parse bare decimal sheet scales
110332b Separate export labels and display toggles
8259215 Fix display labels and joist export labels
```

`e690322` was superseded by `8a800af` for the decimal-scale workflow: the final behavior is decimal inches per foot, not reciprocal display-only formatting.

## Verification

Final verification after `8a800af`:

```powershell
dotnet build .\ourplanecore.sln
dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build
dotnet publish .\ourplanecore.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o .\publish\ourplanecore-working-single-20260625-225729
```

Results:

```text
Build: 0 warnings, 0 errors
Tests: 393/393 passed
Publish SHA256: B5AC7DE525A736DD4170A5B883377B66A2CA88514E2D4DDB490AEA447D172CD5
Update exe SHA256: B5AC7DE525A736DD4170A5B883377B66A2CA88514E2D4DDB490AEA447D172CD5
Shortcut target: C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe
Shortcut working directory: C:\Users\User\Desktop\updates\OurPlaneCore
Packaged process: alive from update folder
App log after latest Application startup: ERROR count = 0
```

Log checked:

```text
C:\Users\User\AppData\Roaming\OurPlaneCore\logs\app-20260625.log
```

## Notes For Next Agent

- Do not reintroduce reciprocal parsing for bare decimals under `1.0`. In this app, the user expects `0.287` to mean `0.287" = 1'0"` after reading the measured scale from a calibrated sheet.
- If a future UI exposes this more explicitly, label it as `inches per 1 ft` or show the full normalized text after applying.
- The Pages tree command intentionally applies the current measured scale to the selected sheets through `ApplyScaleToPages`; do not bypass that helper because it also updates metadata and existing measurements.
- The user works from the packaged update exe, so code changes are not complete until the update folder is refreshed and the app log is checked.
