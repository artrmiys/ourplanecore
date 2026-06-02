# Takeoff Template Presets

Date: 2026-06-01

## Scope

Only the `Takeoff Templates` editor was changed.

Not changed:
- Page Tree
- Page Folders
- Auto Tree
- From Pages
- automatic takeoff-folder creation

## Behavior

- The existing saved takeoff template is kept and migrated into the named
  template preset `Default`.
- If there is no saved template yet, `Default` uses the same built-in takeoff
  template tree as before.
- `Default` cannot be renamed or deleted.
- In the template tree UI, only top-level folders start expanded. Nested folders
  such as `walls -> 1st floor walls` start collapsed by default.

## Settings UI

Open `Settings -> Takeoff Templates`.

Controls:
- `Template` dropdown switches between `Default` and named templates.
- `New` copies the currently selected template into a new named template.
- `Rename` renames the selected non-default template.
- `Delete` deletes the selected non-default template and returns to `Default`.
- `Global` saves the current template preset set globally.
- `Job` saves the current template preset set as this job's override.
- `Clear` clears this job's override and returns to global/default templates.

## Verification

Commands run:

```powershell
dotnet build .\ourplanecore.sln
dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build
```

Result:
- Build: 0 warnings, 0 errors
- Tests: 257/257 passed
