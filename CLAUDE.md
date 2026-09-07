# OurPlanCore — context for Claude Code

Read [AGENTS.md](AGENTS.md) first. It is the repository instruction source for
scope, coding, settings, tests, backup and release validation. Do not maintain
a second set of those rules here.

## Start from the selected version

This checkout contains the isolated **2.2.7 Preview** application. Its published
application commit is `42c44b0`. The user's older stable installation and earlier
Preview remain separate; the Desktop source junction is not this checkout.
Documentation commits after that application commit do not create a new EXE.

Read [docs/README.md](docs/README.md), then choose one route:

| Question | Source |
| --- | --- |
| Current behavior and limits | [Current status](docs/CURRENT_OURPLANECORE_STATUS.md) |
| Architecture, persistence, integrations | [Project context](docs/PROJECT_CONTEXT.md) |
| Installed release, tests and real-project performance | [Strategy evidence](docs/STRATEGY_APP_EVIDENCE_2026_09_06.md) |
| Next work and acceptance | [Improvement plan](docs/70-architecture-refactor/IMPROVEMENT_PLAN_2026_09_06.md) and [master plan](docs/OURPLANCORE_MASTER_REMEDIATION_AND_RELEASE_PLAN.md) |
| Keyboard defaults and customization | [Shortcuts](docs/60-ux-ui/KEYBOARD_SHORTCUTS.md) |
| Older rationale | [Development log](docs/DEVELOPMENT_LOG.md), then the dated topic handoff |

## Verify the relevant code path

The application is WPF on Windows x64. New projects are `.ourplan` packages
opened through private working copies; existing folder projects retain their
format. `MainWindow` uses many focused partial files. The root partial is not
the old multi-thousand-line implementation described in June handoffs.

Look at the implementation before repeating a default, path or counter:

- Runtime identity and profile: `Models/AppIdentity.cs`.
- Open/create/save: `MainWindow.OpenImportMenu.cs`, `MainWindow.JobPicker.cs`,
  `MainWindow.ProjectPackage.cs` and its partials.
- Package persistence and working copies: `Models/OurPlanPackage*.cs`.
- Protected project data: `Models/Storage/DataFileReader.cs`; global settings
  do not all have the same recovery contract yet.
- Measurement overlays and raster caches: `Controls/PdfViewport*.cs`.
- AI: requests are optional online operations; inspect the actual active
  runner and review workflow. Do not describe all project work as offline or
  disabled legacy massing as a working feature.
- Regression harness: `Tests/Program.cs`. Use `dotnet run` for this console
  harness; a successful `dotnet test` discovery is not evidence that it ran.

The user-facing Knowledge Base is a separate MkDocs repository. Its program
guide describes operator actions; internal audits and real project evidence
remain internal. Follow the KB's own instructions when updating that site.
