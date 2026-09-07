# Pages Sort, Metadata Buttons, and Scale Shortcuts - 2026-07-14

## Confirmed final behavior

- `F3` keeps its existing viewport Snap behavior.
- `Ctrl+F3` keeps PDF Snap.
- `F4` opens one manual Set Scale prompt. If multiple sheet rows are selected
  in the Pages tree, the entered scale is applied to every selected sheet;
  otherwise it applies to the active sheet.
- `F5` opens the manual floating Name / Scale window for the active sheet.
- Hiding the Sheet Manager workspace in Settings no longer disables the base
  Pages metadata actions or the manual Name / Scale window.
- The bottom Pages row is aligned with the Takeoffs action row and contains:
  `New Folder`, `Name`, `Scale`, `Name+Scale`.
- `Name`, `Scale`, and `Name+Scale` run the existing PDF metadata analysis for
  the selected sheet or selected Pages folder. They show the review table
  before applying changes; bulk metadata is not applied silently.
- The `F1` shortcut overlay and Command Palette describe `F4` and `F5`.

## Sort A/S scope and recovery

- The parameterless Sort A/S action now resolves the selected Pages folder and
  sorts only that folder. With no selected folder, the prior Pages-root scope
  remains available.
- The accidental global Sort A/S operation in job
  `89. 1 Croton Point_Scott_Probuild` was restored from the pre-switch snapshot
  and app log.
- Recovery result: `123` sheets restored, `654` measurement page links rebased,
  `154` sheets validated, with no unreadable/missing PDFs and no missing
  measurement references.
- Recovery snapshot: `.snapshots/20260714_173807_before_sort_repair`.
- The `95` sheets imported on 2026-07-14 from `Drawings.pdf` and
  `Drawings (1).pdf` were consolidated under the Pages folder `new`.
  The move placed `62` additional sheets there; final validation found all
  `95` in `new`, none outside it, and no broken PDF or measurement links.
- Move snapshot: `.snapshots/20260714_200027_before_move_today_to_new`.

## Code ownership

- `MainWindow.PagesOrganization.cs`: selected-folder Sort A/S scope.
- `MainWindow.PagesScale.cs`: multi-sheet manual scale prompt and persistence.
- `MainWindow.Shortcuts.cs`: global `F4` and `F5` routing.
- `MainWindow.PageSetup.cs`: manual Name / Scale window, independent of Sheet
  Manager workspace visibility.
- `MainWindow.PagesPdfMetadata.cs`: reviewed Name/Scale PDF automation,
  independent of Sheet Manager workspace visibility.
- `MainWindow.Modules.cs`: module visibility applies to the Sheet Manager
  workspace, not to base Pages metadata commands.
- `MainWindow.xaml`: bottom Pages action row and `F1` shortcut overlay.
- `MainWindow.CommandPalette.cs`: F4/F5 command descriptions.
- `Models/SampleJobGuideBuilder.cs`: shortcut guide text.
- `Tests/TakeoffsTreeRegressionTests.cs` and `Tests/ModuleFeatureTests.cs`:
  shortcut, button, selection, and module-decoupling regression guards.

## Verification and deployment

Commands used:

```powershell
dotnet build .\ourplancore.sln --no-restore
dotnet run --project .\Tests\OurPlanCore.Tests.csproj --no-build
dotnet publish .\ourplancore.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none
```

- Build: `0` warnings, `0` errors.
- Regression suite: `462/462` passed.
- Deployed executable:
  `C:\Users\User\Desktop\updates\OurPlanCore\ourplancore.exe`.
- Executable size: `173,665,039` bytes.
- SHA256:
  `69AC606CE0C9D07CE6A41E52525691617232EE75D2694EA4FEE1C724DFE42E6D`.
- Shortcut target and working directory both resolve to the packaged update
  folder.
- Packaged launch validation: process alive, no `ERROR` after the last
  `Application startup.` marker, with `Loaded takeoffs` and `Viewport` signals.

## Git checkpoints and commits

- Checkpoint before shortcut work:
  `checkpoint/f4-f5-page-shortcuts-before-20260714`.
- Checkpoint before module decoupling:
  `checkpoint/page-metadata-buttons-before-module-decouple-20260714`.
- `ca1dd94 Scope A/S sort to selected folder`.
- `861e5a8 Add page scale shortcuts and quick buttons`.
- `ae10bd2 Keep page metadata actions available`.
