# Takeoff Templates Handoff - 2026-06-01

## Current State

Takeoff Templates are editable presets for creating empty takeoff items from the
right-side `Templates` tab and from `8 Settings -> Takeoff Templates`.

Current built-in template version: `5`.

Main implementation files:

- `Models/TakeoffTemplate.cs`
  - template data model
  - built-in default tree
  - global/job persistence
  - built-in version upgrades and cleanup
  - folder routing fallback
- `MainWindow.Templates.cs`
  - right-side Templates tab UI
  - Settings Templates editor UI
  - add/edit/delete/duplicate/reset/save actions
  - double-click template item creation flow
- `MainWindow.SettingsManager.cs`
  - Settings category registration
  - config load on job open
- `Tests/TakeoffTemplateTests.cs`
  - default preset coverage
  - upgrade/migration coverage
  - folder routing coverage

## How To Save Defaults

Use `8 Settings -> Takeoff Templates`.

- `Global`: saves the current template tree as the default for all projects.
- `Job`: saves the current template tree only for the open project.
- `Clear`: removes the current project's job override and goes back to global/default.
- `Reset`: restores the built-in default tree for the editor.

Editing directly in the right-side `Templates` tab also persists:

- If the current project has a job override, edits save to that job override.
- If there is no job override, edits save to the global default.

Safe workflow when editing defaults:

1. Open `8 Settings -> Takeoff Templates`.
2. Edit folders/items/colors/types.
3. Press `Global`.
4. New projects and projects without a job override will use that global default.

## Creation Behavior

Selecting or double-clicking a template item always creates a new takeoff item.
Double-click opens the item settings dialog first, with the name ready to edit,
so the preset can be adjusted before creating the real item.

The destination folder uses the template folder path:

- If every matching folder exists in the real Takeoffs tree, the new item is
  created in that matching folder.
- If any folder segment is missing, creation falls back to Takeoffs root.

This is intentional so bad or missing folders do not create items in the wrong
place.

## Built-In Template Tree

### `sqfts`

Area presets:

- `base`
- `1st`
- `2nd`
- `3rd`
- `4th`
- `5th`
- `6th`
- `7th`
- `8th`
- `deck`
- `porch`
- `blcny`
- `balcony`
- `cant`
- `cantilevered`
- `flat`
- `rf`
- `rf x`
- `rf mtl x`
- `roof`
- `overframe x`

### `walls`

These presets exist in root `walls` and in regular wall floor folders:

- `corners` - point/count preset
- `ext`
- `ext 2x6`
- `ext 2x4`
- `ext 2x8`
- `cor`
- `cor 2x4`
- `cor 2x6`
- `cor 2x8`
- `cor (2) 2x4`
- `cor (2) 2x6`
- `cor (2) 2x8`
- `dem`
- `dem 2x4`
- `dem 2x6`
- `dem 2x8`
- `dem (2) 2x4`
- `dem (2) 2x6`
- `dem (2) 2x8`
- `furring`
- `2x4 x`
- `2x6 x`
- `2x8 x`
- `2x4 half`
- `2x6 half`

Regular wall folders:

- `basement foor walls`
- `1st floor walls`
- `2nd floor walls`
- `3rd floor walls`
- `4th floor walls`
- `5th floor walls`

`walls/shaft walls` only contains:

- `shaft 1st`
- `shaft 2nd`
- `shaft 3rd`
- `shaft 4th`
- `shaft 5th`

Removed from wall presets:

- `unit`
- `corr`
- `parapet`
- generic `shaft`

### `gables`

- `gable` - area preset
- `gable trusses/gable truss` - area preset
- `gable stick/gable stick` - area preset

### `parapets`

- `prpt 0.0 0.0 0.0` - line preset

### `trussheel`

- `Truss Heel`
- `Eve Heel`

### `openings`

- `Window` - point/count preset
- `Door` - point/count preset
- `Header` - line preset

### `eves rakes`

- `eves/Eve`
- `eves/Eave`
- `rakes/Rake`
- `Returns`

### `roof misc`

- `Ridge`
- `Valley`
- `Hip`
- `Flashing`
- `Roof Sheathing` - area preset
- `Gable Sheathing` - area preset
- `Roof Types`

### `framing`

Only these 9 presets are kept:

- `Blocking for Drywall`
- `Blocking for Trusses`
- `Ribbon Board`
- `Rim Board`
- `Blocking`
- `Ledger`
- `1x3 Cross Blocking`
- `Plate`
- `Frame`

Removed from built-in templates:

- root `units`
- `shear walls - holdowns - ties`
- `siding`
- `trims`
- `drywalls`
- nested framing floor folders
- `framing/roof framing`
- framing extras like `Post`, `Beam`, `Joist`, `Stair`, `Subfloor`,
  `Bracing`, `Bolts`, `Screws`, `Steel Beam Web Fillers`, roof framing items

## Persistence

Global config path:

```text
SmartContextStore.GlobalRoot/presets/takeoff_templates.json
```

Job override path:

```text
<job>/AI_Context/settings/takeoff_templates.json
```

Resolve order:

1. Job override if present.
2. Global config if present.
3. Built-in default.

## Migration Rules

`TakeoffTemplateConfig.CurrentBuiltInVersion` is currently `5`.

When an old global/job template config is loaded:

- Missing built-in folders/items are merged in.
- Deprecated built-in root folders are removed:
  - `units`
  - `shear walls - holdowns - ties`
  - `siding`
  - `trims`
  - `drywalls`
- Deprecated wall presets are removed:
  - `unit`
  - `corr`
  - `parapet`
  - generic `shaft`
- `corners` is normalized to point/count.
- `parapets` is normalized to `prpt 0.0 0.0 0.0`.
- `framing` is normalized to the 9 approved presets.
- `walls/shaft walls` is normalized to only `shaft 1st` through `shaft 5th`.
- Custom wall items are preserved when they do not collide with known built-in
  names.

Important: if a saved config is already version `5`, migration will not re-run.
Use `Reset` or edit manually if a user intentionally wants to rebuild it from
the built-in default.

## Adding More Presets Later

For built-in defaults:

1. Edit `Models/TakeoffTemplate.cs`.
2. Add names to the correct list:
   - `WallPresetNames`
   - `ShaftWallPresetNames`
   - `FramingPresetNames`
3. Add colors in the matching color helper.
4. Add the item to `BuildDefaultTemplate()` if needed.
5. Bump `CurrentBuiltInVersion`.
6. Extend `Tests/TakeoffTemplateTests.cs`.
7. Run:

```powershell
dotnet build .\ourplanecore.sln
dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build
```

8. Publish compressed and replace:

```powershell
dotnet publish .\ourplanecore.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o .\bin\publish
```

Then copy `bin\publish` into:

```text
C:\Users\User\Desktop\updates\OurPlaneCore
```

Keep `ourplanecore.exe.bak`, and keep the Desktop shortcut pointed to:

```text
C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe
```

Shortcut working directory must be:

```text
C:\Users\User\Desktop\updates\OurPlaneCore
```

## Verification From Last Template Pass

Last committed template-related change:

```text
8a18980 Add dem wall template variants
```

Verification commands passed:

```powershell
dotnet build .\ourplanecore.sln
dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build
```

Result:

```text
0 warnings / 0 errors
254/254 tests passed
```

Packaged exe was refreshed at:

```text
C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe
```

Last packaged exe hash from the final pass:

```text
A85794A3723A4C9E63BD117850915A1445EC4C2BF6AFFFAA2E53C0AEFCABFE78
```

Launch validation passed by checking the app log after the last
`Application startup.` marker:

- process stayed alive
- no `ERROR` entries after startup
- `Loaded takeoffs` or `Viewport` appeared in the startup tail
