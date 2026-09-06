# Repository Guidelines

## Project Structure & Module Organization

This is a Windows WPF takeoff app targeting `.NET 9` and `x64`. The root holds
`App.xaml`, `MainWindow.xaml`, `MainWindow.xaml.cs`, and focused
`MainWindow.*.cs` partials for pages, takeoffs, estimating, PDF export, and
viewport callbacks. Reusable domain and persistence logic lives in `Models/`;
custom controls are in `Controls/`; dialogs are in `Dialogs/`.
`Tools/pdf_layers_helper.py` is copied to build output for PDF layer rendering.
Use `docs/` for active project notes, `docs_sources/` for source research, and
`reference/` for examples and prototypes. Do not edit generated `bin/`, `obj/`,
or published output unless explicitly requested.

For the active **2026-09-06 isolated preview** task, the user's explicit scope
overrides the normal update-folder release steps below: prepare **2.2.7-preview**
in a new versioned folder/profile with a separate shortcut. Preserve stable
`updates\OurPlanCore`, its shortcut, and the existing live 2.2.6 Preview;
do not replace files or stop their processes. No public release is authorized.
Run this task's build/tests from this isolated checkout, not the old Desktop
source path in the general examples below.
See [current strategy evidence and pending gates](docs/STRATEGY_APP_EVIDENCE_2026_09_06.md).

## Codex Skill Routing

Local reusable Codex skills for this repo live under
`C:\Users\User\.codex\skills` and use the `ourplanecore-*` prefix. Use the
smallest matching skill before editing:

- `$ourplanecore-refactor`: partial splits, architecture cleanup, line-count
  reduction, and build-safe commits.
- `$ourplanecore-bugcheck`: regression audits, failing behavior checks,
  conflict/TODO scans, and verification builds.
- `$ourplanecore-pdf-layers`: PDF layer visibility, Layer Trace, PDF geometry
  highlighting/tracing, and PDF export rendering.
- `$ourplanecore-takeoff-trees`: Pages/Takeoffs trees, expansion persistence,
  selection sync, folders, drag/drop, copy/paste, and item creation placement.
- `$ourplanecore-measurements`: measurement drawing/editing, page links, scale,
  labels, clipboard, and estimating sync.
- `$ourplanecore-ai-massing`: AI Inbox, marker training, OpenAI settings,
  crop bookmarks, and reviewable 3D massing drafts.
- `$ourplanecore-docs-handoff`: AGENTS.md, development log, architecture audit,
  current-status docs, handoffs, and implementation prompts.
- `$ourplanecore-ux-bluebeam`: dense Bluebeam/PlanSwift-style production UI,
  workspace tabs, command maps, panels, icons, and status surfaces.
- `$ourplanecore-ui-mockups`: preview-first UI/UX layout mockups and alternate
  WPF screens before production integration.
- `$ourplanecore-planswift-spec`: PlanSwift behavior mapping, MVP rules,
  interaction model, takeoff tools, and Russian workflow explanations.
- `$ourplanecore-sheet-metadata`: PDF-first sheet naming, suffix sorting,
  autoscale, metadata preview, and `source_pdf.json` behavior.
- `$ourplanecore-update-package`: build, test, publish, replace
  `C:\Users\User\Desktop\updates\OurPlanCore`, and refresh the Desktop
  shortcut after a successful local build.
- `$ourplanecore-parallel-agents`: parallel-agent planning and merge review
  when the user explicitly asks for agents or parallel work.

## Build, Test, and Development Commands

Run commands from `C:\Users\User\Desktop\ourplanecore`.

```powershell
dotnet restore .\ourplancore.sln
dotnet build .\ourplancore.sln
dotnet run --project .\ourplancore.csproj
```

`restore` downloads NuGet packages, `build` compiles the app, and `run`
launches it. Avoid parallel builds because WPF outputs under
`obj\Debug\net9.0-windows` can lock.

After any successful local build intended for the user, refresh the local update
folder and point the Desktop shortcut at the packaged update build:
`C:\Users\User\Desktop\updates\OurPlanCore\ourplancore.exe`.
Keep the shortcut working directory set to
`C:\Users\User\Desktop\updates\OurPlanCore`. The shortcut is
`C:\Users\User\Desktop\OurPlanCore.lnk`. If an update-package helper resets
the shortcut to the Debug build, retarget it to the update package before
finishing.

## Editable Rules & the Settings Tab

The "8 Settings" top workspace tab (`Tag="SettingsManager"`) is the canonical
home for any user-editable template or rule. Do not leave behaviour-defining
constants hard-coded — surface them here. Current categories: Page Folders,
Auto Tree, From Pages, Sort A/S, Sort D/Sec/WT, Auto Rename / Scale, Defaults.

Follow the established pattern (reference: `Models/FolderTemplateConfig` +
`PlanSwiftFolderTemplateService` for folders, `Models/PageSortConfig` +
`PageSortRulesService` for page sorting):

1. A serializable config class in `Models/` with `BuildDefault()` that
   reproduces the previous hard-coded behaviour **bit-for-bit**, plus `Clone()`.
2. Persistence via `SettingsPresetStore`: global at
   `SmartContextStore.GlobalRoot/presets/<name>.json`, per-job at
   `<job>/AI_Context/settings/<name>.json`, with
   `Resolve…(job) = job override ?? global ?? default`.
3. App-wide application through a provider/holder the classifier reads, installed
   on job open in `ApplyFolderTemplateProviders()`
   (`MainWindow.SettingsManager.cs`).
4. A Settings category panel in a `MainWindow.SettingsManager.*.cs` partial
   offering: live editor mirroring the exact result, Reset to default, Save
   global default, Save as this job, and Apply (runs the real op on the current
   scope). Confirm the editable surface with the user before building it.

## Release Validation

After deploying to `C:\Users\User\Desktop\updates\OurPlanCore\ourplancore.exe`,
the preferred packaging is the compressed single-file build (~166 MB);
compression is safe and never a launch-failure cause.

```powershell
dotnet publish .\ourplancore.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o .\bin\publish
```

Keep a `ourplancore.exe.bak` rollback; never overwrite an existing `.bak`.

Do **not** judge "did it launch" by window handle/title: `App.xaml.cs` shows a
`MessageBox` titled "OurPlanCore" on unhandled startup exceptions, so a crash
still produces a matching window. Instead launch, wait ~15-30s, and read
`%APPDATA%\OurPlanCore\logs\app-<yyyyMMdd>.log` scoped to the **last**
`Application startup.` line (the file spans every run that day). Pass = process
alive + no `\tERROR\t` after that marker + `Loaded takeoffs`/`Viewport` present.
WPF gotcha: `Slider.ValueChanged` fires during XAML load before the ctor wires
fields — such handlers must early-return `if (!IsInitialized)`.

## Coding Style & Naming Conventions

Use C# with nullable reference types and implicit usings enabled. Match the
existing style: four-space indentation, PascalCase for public types, methods,
and properties, and camelCase for locals and private fields. Keep referenced
XAML names stable. Prefer reusable behavior in `Models/`, `Controls/`, or
focused partials instead of growing `MainWindow.xaml.cs`. Keep the
`OurPlanCore` namespace, `ourplancore` assembly name, and runtime asset paths unchanged
unless a task explicitly scopes a rename.

## Development Guardrails & File Size Limits

Treat these limits as working rules for every code change:
- New C# files should usually stay between 300 and 600 physical lines and must
  not exceed 800 lines. If a new feature needs more, split it into focused
  partials, services, controls, or model helpers before committing it.
- `MainWindow.xaml.cs` must stay below 500 lines. Do not add feature workflow
  code there; add a focused `MainWindow.*.cs` partial or move behavior into
  `Models/`, `Controls/`, or `Dialogs/`.
- New `MainWindow.*.cs` partials and `Controls/*.cs` files must not exceed
  1,000 lines. Existing files already above that limit are refactor targets:
  do not add unrelated code to them, and prefer edits that keep their size
  neutral or reduce it.
- XAML files should stay below 900 lines. When a view grows past that, extract
  reusable controls, templates, styles, or resource dictionaries instead of
  continuing to grow one shell file.
- Methods should normally stay below 80 lines and must not exceed 120 lines
  without a clear reason. Extract parsing, persistence, drawing, or state
  transitions into named helpers instead of nesting large blocks in event
  handlers.
- If a change adds more than about 150 lines to a single file, pause and choose
  an explicit ownership boundary before editing further.

Use competent engineering defaults, not quick patches that make the next task
harder. Keep responsibilities narrow, prefer typed models and existing
persistence APIs over ad hoc strings, avoid duplicated UI logic, avoid hidden
global state, and do not swallow exceptions without surfacing an actionable
status. New dependencies need a clear reason and should match the app's WPF
and Windows deployment model.

## Testing Guidelines

`Tests/OurPlanCore.Tests.csproj` is the existing console regression harness and
is included in `ourplancore.sln`. It runs through `dotnet run`, not test-adapter
discovery. Minimum verification for code changes is:

```powershell
dotnet build .\ourplancore.sln
dotnet run --project .\Tests\OurPlanCore.Tests.csproj --no-build
```

For UI, PDF, scale, measurement, PlanSwift XML, or AI-review behavior, also run
the relevant WPF/real-project harness and validate the packaged app using
disposable copies of populated jobs. Small fixtures and `reference/examples/`
remain useful for targeted regressions, but do not prove real-job performance
or visual quality. Document the scenario, exact build, copied input, output
report and fresh runtime log in the PR or handoff. Add behavioral checks to
the existing harness; available focused modes are routed in `Tests/Program.cs`.

## Commit & Pull Request Guidelines

Recent commits use short imperative messages such as `Split pages tree workflow`
and `Add section rename support`. Follow that pattern: one focused change per
commit, concise verb-first subject, no noisy metadata. Pull requests should
include a summary, verification commands, screenshots for visible UI changes,
linked issue or planning doc when relevant, and notes on any real sample job
used for validation.

## Security & Configuration Tips

Do not commit API keys, tokens, user-specific absolute paths, or local app
settings. OpenAI configuration should stay in user environment variables or
local per-user config, never in tracked docs or source files.
