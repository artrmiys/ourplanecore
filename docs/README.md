# OurPlaneCore Docs - Start Here

This folder is organized so future work starts from a small, stable context
instead of scanning dozens of dated handoffs.

## Read First

- `OURPLANCORE_MASTER_REMEDIATION_AND_RELEASE_PLAN.md` - active authority for
  remediation order, release gates, local update contents, and GitHub Releases.
- `PROJECT_CONTEXT.md` - broad product and architecture context.
- `CURRENT_OURPLANECORE_STATUS.md` - current behavior and known gaps.
- `OURPLANECORE_TASK_ROADMAP.md` - historical feature map; its priority order is
  superseded by the active master remediation plan.
- `DEVELOPMENT_LOG.md` - chronological work log.
- `ARCHITECTURE_AUDIT_AND_REFACTOR_PLAN_2026_05_05.md` - refactor baseline.

## Current Priority Context

- `OURPLANCORE_MASTER_REMEDIATION_AND_RELEASE_PLAN.md`
  - current implementation phases, acceptance gates, rollback rules, and the
    permanent EXE + Excel template + download-note release contract.
- `00-start-here/NEXT_TASK_JOB_MOVE_AUTOREPAIR_AND_SHEET_RENDER_PERF_2026_06_06.md`
  - handoff/history for moved-job `page_folder` repair plus the next
    sheet-render performance strategy; the code-side job-move autorepair is now
    implemented.
- `00-start-here/DOCS_AUDIT_2026_06_06.md`
  - latest markdown inventory and cleanup notes.
- `10-performance-render/OURPLANECORE_SPEED_ACCELERATION_ANALYSIS_2026_06_02.md`
  - latest speed and render acceleration plan.
- `10-performance-render/SHEET_RENDER_STRATEGY_2026_06_01.md`
  - canonical sheet render strategy.
- `10-performance-render/SHEET_RENDERING_ANALYSIS_AND_INSTANT_STRATEGY_2026_06_04.md`
  - root-cause analysis for whole-sheet raster slowness/blur.
- `20-import-pages-metadata/JOB_CREATION_AND_STORAGE_FLOW_2026_06_05.md`
  - current job/page/takeoff storage flow and page-folder link contract.
- `30-takeoffs-measurements/TAKEOFF_TEMPLATE_PRESETS_2026_06_01.md`
  - latest Takeoff Templates preset rollout.
- `30-takeoffs-measurements/EXCEL_MACRO_EXPORT_WORKFLOW_2026_07_29.md`
  - canonical operator and implementation contract for the vertical Excel
    macro strip, `ALL`, Auto Tree root scope, VBA order, and protected Walls
    cleanup.

## Folder Map

- `00-start-here/` - small guide docs and organization notes.
- `10-performance-render/` - PDF render, viewport speed, cache, zoom clarity.
- `20-import-pages-metadata/` - PDF import, sheet names, scale, pages, job open.
- `30-takeoffs-measurements/` - takeoffs tree, measurements, labels, tools.
- `40-planswift-product/` - PlanSwift behavior, product rules, MVP mappings.
- `50-3d-roof-ai/` - 3D roof, massing, AI markers, geometry ideas.
- `60-ux-ui/` - Bluebeam/PlanSwift UI, workspace layout, UX research.
- `70-architecture-refactor/` - audits, refactor plans, parallel-agent notes.
- `90-archive-prompts/` - old prompts, archived plans, historical scratch docs.
- `archive/` - pre-existing archive folder.
- `mockups/` - UI mockup assets/docs.

## Rules For New Docs

1. Put new docs in the matching folder, not in the `docs/` root.
2. Keep only long-lived canonical status files in the root.
3. If a document supersedes another one, write that explicitly at the top.
4. Include exact files/modules and verification commands for handoffs.
5. Do not delete old handoffs; move obsolete ones to `90-archive-prompts/`.

## Git Note

The repo `.gitignore` ignores `*.md`. The active master plan has a narrow
exception; other new markdown files may still need `git add -f <path>` when
they must be committed.
