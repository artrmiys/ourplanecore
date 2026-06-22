# KB 2026-06-22: Annotation, Pages Selection, Scale Deploy

Status: completed and deployed to the local user package.

Full implementation log:

- `../SESSION_2026_06_22_ANNOTATION_SELECTION_SCALE.md`

## Current Deployed Build

User-facing executable:

```powershell
C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe
```

Source branch:

- `feature/ourcore-design-overhaul`

Included commits:

- `2ac84c6 Add annotation tools and viewport selection fixes`
- `76b6098 Make transform scale factor editable`

Deployed executable:

- Size: `171,521,759` bytes
- SHA256: `B96E698A8724EAFF807843A0DED36DFA567FD3225BA6CAB5A7313D71702B95A8`

Desktop shortcut:

- Target: `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`
- Working directory: `C:\Users\User\Desktop\updates\OurPlaneCore`

Rollback files kept in the update folder:

- `ourplanecore.exe.bak`
- `ourplanecore.exe.bak-20260622-104209`
- `ourplanecore.exe.bak-20260622-104918`

## Deployment Recipe Used

Build:

```powershell
dotnet build .\ourplanecore.sln
```

Regression tests:

```powershell
dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build
```

Compressed single-file publish:

```powershell
dotnet publish .\ourplanecore.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o .\bin\publish
```

Runtime verification:

- Launch the deployed exe from `C:\Users\User\Desktop\updates\OurPlaneCore`.
- Read `%APPDATA%\OurPlaneCore\logs\app-YYYYMMDD.log`.
- Scope the check to the latest `Application startup.` marker.
- Pass condition:
  - process stays alive,
  - no `ERROR` entries after the latest startup marker,
  - viewport/load log lines are present.

## Pages Behavior

`Esc` in the Pages tree now clears Pages selection state:

- normal page/folder multi-selection,
- linked page-takeoff selection,
- range-selection anchors.

Pages folders and Takeoffs folders now show a small folder glyph, so folder rows are visually different from normal page/takeoff rows without relying on emoji text.

Primary files:

- `MainWindow.PagesCommands.cs`
- `MainWindow.PagesTree.cs`
- `MainWindow.TakeoffTreeVisuals.cs`

## Annotation Behavior

Annotation controls now live in the top `Annotation` tab instead of the bottom takeoff tool strip.

The tab contains:

- `Ruler`
- `Highlighter`
- `Draw`
- `Arrow`
- `Box`
- `Cloud`
- `Area`
- `Note`
- annotation style/menu controls

Highlighter is now a separate tool:

- tool id: `drawhighlight`
- viewport enum: `ViewerTool.DrawHighlight`
- hotkey: `H`
- default color: `#FFC107`
- persisted annotation kind: `highlight`

Primary files:

- `MainWindow.xaml`
- `MainWindow.ToolControls.cs`
- `Controls/PdfViewport.cs`
- `Controls/PdfViewport.Tools.cs`
- `Controls/PdfViewport.AnnotationRendering.cs`
- `Models/Storage/PageAnnotationStore.cs`
- `Models/PdfExporter.Annotations.cs`

## Viewport Selection And Rename

Box selection now follows CAD/PlanSwift direction rules:

- left-to-right drag: window select, only fully enclosed objects,
- right-to-left drag: crossing select, touched objects are included.

Viewport rename behavior:

- `F2` renames the owning takeoff for the selected measurement.
- Double-clicking a measurement/segment in Select mode selects it and starts rename for the owning takeoff.
- Rename uses the existing takeoff-tree rename flow, so linked measurements move with the takeoff name.

Primary files:

- `Controls/PdfViewport.BoxSelection.cs`
- `Controls/PdfViewport.Input.cs`
- `MainWindow.ViewportCallbacks.cs`

## Transform Scale Field

The selected-geometry transform `Scale` value is now editable directly in the field that used to show `1x`.

Supported typed input:

- `1.25x`
- `1.25`
- `125%`

Behavior:

- `Enter` applies the typed scale.
- losing focus applies the typed scale.
- `Esc` restores the current value without applying pending text.
- invalid input restores the current value and writes a status message.
- the field uses the same allowed range as the slider: `0.25x` to `3x`.

Primary files:

- `MainWindow.xaml`
- `MainWindow.ToolControls.cs`

## Verification Snapshot

Completed verification for this deploy:

- `dotnet build .\ourplanecore.sln`: `0 warnings`, `0 errors`
- `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build`: `362/362 tests passed`
- packaged app launched from the update folder
- app log checked at `%APPDATA%\OurPlaneCore\logs\app-20260622.log`
- latest startup block had `0` errors and viewport/load log lines

## Quick Manual Smoke

Use this checklist after future edits touching the same surfaces:

1. Select several rows in Pages, press `Esc`, confirm all highlighted Pages rows clear.
2. Confirm folder rows in Pages and Takeoffs show the folder glyph.
3. Open the top `Annotation` tab and confirm annotation tools are there.
4. Pick `Highlighter` or press `H`, draw a highlight, save/reopen, and confirm it remains a highlight.
5. In Select mode, drag a box left-to-right and confirm only fully enclosed objects select.
6. Drag a box right-to-left and confirm touched objects select.
7. Select a measurement in the viewport, press `F2`, and confirm the whole takeoff is renamed.
8. Select geometry, type a scale value such as `125%`, press `Enter`, and confirm the slider and geometry update.
