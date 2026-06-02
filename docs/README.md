# OurPlaneCore Docs - Start Here

This folder is organized so future work starts from a small, stable context
instead of scanning dozens of dated handoffs.

## Read First

- `PROJECT_CONTEXT.md` - broad product and architecture context.
- `CURRENT_OURPLANECORE_STATUS.md` - current behavior and known gaps.
- `OURPLANECORE_TASK_ROADMAP.md` - task roadmap and priorities.
- `DEVELOPMENT_LOG.md` - chronological work log.
- `ARCHITECTURE_AUDIT_AND_REFACTOR_PLAN_2026_05_05.md` - refactor baseline.

## Current Priority Context

- `10-performance-render/OURPLANECORE_SPEED_ACCELERATION_ANALYSIS_2026_06_02.md`
  - latest speed and render acceleration plan.
- `10-performance-render/SHEET_RENDER_STRATEGY_2026_06_01.md`
  - canonical sheet render strategy.
- `30-takeoffs-measurements/TAKEOFF_TEMPLATE_PRESETS_2026_06_01.md`
  - latest Takeoff Templates preset rollout.

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

The repo `.gitignore` ignores `*.md`, so new markdown files may need
`git add -f <path>` when they must be committed.
