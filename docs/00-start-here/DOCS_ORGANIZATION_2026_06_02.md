# Docs Organization - 2026-06-02

Goal: remove confusion from the flat `docs/` folder by keeping only canonical
start-here files at the root and moving dated handoffs/research/prompts into
topic folders.

Scope:

- Organized: `docs/*.md`
- Left alone: `AGENTS.md`, `CLAUDE.md`, `docs_sources/`, `Tools/**/README.md`,
  `docs/archive/`, `docs/mockups/`
- No deletes.

Root files intentionally kept:

- `docs/README.md`
- `docs/PROJECT_CONTEXT.md`
- `docs/CURRENT_OURPLANECORE_STATUS.md`
- `docs/OURPLANECORE_TASK_ROADMAP.md`
- `docs/DEVELOPMENT_LOG.md`
- `docs/ARCHITECTURE_AUDIT_AND_REFACTOR_PLAN_2026_05_05.md`

## 2026-06-06 Follow-Up Audit

The docs set was checked again after the moved-job page-link repair and render
performance discussion.

Current policy remains unchanged: keep only stable canonical files in
`docs/` root and put active handoffs in topic folders.

Moved during the follow-up cleanup:

| Source | Destination |
| --- | --- |
| `JOB_CREATION_AND_STORAGE_FLOW_2026_06_05.md` | `20-import-pages-metadata/` |
| `SHEET_RENDERING_ANALYSIS_AND_INSTANT_STRATEGY.md` | `10-performance-render/SHEET_RENDERING_ANALYSIS_AND_INSTANT_STRATEGY_2026_06_04.md` |
| `CHANGELOG_2026-06-06_rotation-zoom-ribbon.md` | `90-archive-prompts/` |

New start-here docs:

- `docs/00-start-here/NEXT_TASK_JOB_MOVE_AUTOREPAIR_AND_SHEET_RENDER_PERF_2026_06_06.md`
- `docs/00-start-here/DOCS_AUDIT_2026_06_06.md`

## Folder Policy

| Folder | Use |
| --- | --- |
| `00-start-here/` | Organization notes and small guide docs |
| `10-performance-render/` | PDF rendering, viewport speed, cache, zoom clarity |
| `20-import-pages-metadata/` | PDF import, sheet names, scale, pages, job open |
| `30-takeoffs-measurements/` | Takeoffs, measurements, labels, tool behavior |
| `40-planswift-product/` | PlanSwift specs and product behavior mapping |
| `50-3d-roof-ai/` | 3D roof, AI massing, marker ideas |
| `60-ux-ui/` | UI, UX, Bluebeam layout, workspace commands |
| `70-architecture-refactor/` | Audits, refactor plans, parallel-agent docs |
| `90-archive-prompts/` | Historical prompts and archived scratch docs |

## Move Map

| Source | Destination |
| --- | --- |
| `3D_MESH_REVIT_POLISH.md` | `50-3d-roof-ai/` |
| `3D_ROOF_RENDER_HANDOFF_2026_05_21.md` | `50-3d-roof-ai/` |
| `AI_3D_MASSING_VIEWER_IDEAS.md` | `50-3d-roof-ai/` |
| `AI_FILL_CROP_HINTS_AND_NOTES_HANDOFF_2026_05_12.md` | `50-3d-roof-ai/` |
| `AI_MARKER_TRAINING_IDEAS.md` | `50-3d-roof-ai/` |
| `ARCHIVED_3D_MASSING_LOGIC_2026_05_08.md` | `90-archive-prompts/` |
| `AUTO_TRACE_AREAS_AND_WALLS_SPEC_2026_05_12.md` | `30-takeoffs-measurements/` |
| `BLANK_JOB_BLANK_SHEET_HANDOFF_2026_05_30.md` | `20-import-pages-metadata/` |
| `BLUEBEAM_DESIGN_SYSTEM.md` | `60-ux-ui/` |
| `BOOKMARKS_DOCK_HANDOFF_2026_05_28.md` | `30-takeoffs-measurements/` |
| `CODEBASE_HEALTH_AUDIT_2026_06_01.md` | `70-architecture-refactor/` |
| `CODEX_TASK_01_OPC_DATAGRID.md` | `90-archive-prompts/` |
| `DISPLAY_SETTINGS_AND_VIEWPORT_LABELS.md` | `30-takeoffs-measurements/` |
| `EWOOD_TAKEOFF_AUTO_ROUTING_RESEARCH_2026_05_07.md` | `40-planswift-product/` |
| `FIX_AUDIT_PROMPT.md` | `90-archive-prompts/` |
| `INSTANT_PAGE_OPEN_IDEAL_QUALITY_PLAN_2026_06_01.md` | `10-performance-render/` |
| `JOB_CREATION_AND_CROP_NOTE_HANDOFF_2026_05_13.md` | `20-import-pages-metadata/` |
| `KEYBOARD_SHORTCUTS.md` | `60-ux-ui/` |
| `MANAGERS_DEEP_RESEARCH.md` | `60-ux-ui/` |
| `MEASUREMENT_PAGE_LINK_AND_EDITING_POSTMORTEM.md` | `30-takeoffs-measurements/` |
| `OPEN_JOB_REDESIGN_PLAN.md` | `60-ux-ui/` |
| `OPEN_JOBS_PDF_IMPORT_RENDER_HANDOFF_2026_05_27.md` | `20-import-pages-metadata/` |
| `OURPLANECORE_SPEED_ACCELERATION_ANALYSIS_2026_06_02.md` | `10-performance-render/` |
| `PAGE_FOLDER_SORT_AND_NOTES_HANDOFF_2026_05_13.md` | `20-import-pages-metadata/` |
| `PAGE_OPEN_UI_PERF_HANDOFF_2026_05_28.md` | `10-performance-render/` |
| `PARALLEL_AGENT_MERGE_REVIEW.md` | `70-architecture-refactor/` |
| `PARALLEL_CODEX_BOARD.md` | `70-architecture-refactor/` |
| `PDF_FIRST_AUTO_SHEET_METADATA_PLAN.md` | `20-import-pages-metadata/` |
| `PDF_FULL_RENDER_CACHE_HANDOFF_2026_05_28.md` | `10-performance-render/` |
| `PDF_IMPORT_SHEET_METADATA_HANDOFF_2026_05_22.md` | `20-import-pages-metadata/` |
| `PDF_INLINE_RENDER_HANDOFF_2026_05_28.md` | `10-performance-render/` |
| `PDF_PREVIEW_CACHE_HANDOFF_2026_05_28.md` | `10-performance-render/` |
| `PDF_RENDER_PERF_STATUS_2026_05_28.md` | `10-performance-render/` |
| `PDF_TAKEOFF_IMPORT_EDGE_SNAP_HANDOFF_2026_05_28.md` | `30-takeoffs-measurements/` |
| `PERFORMANCE_OPTIMIZATION_ROLLBACK_2026_05_09.md` | `10-performance-render/` |
| `PERFORMANCE_RENDER_AND_LAYERS_HANDOFF_2026_05_16.md` | `10-performance-render/` |
| `PLANSWIFT_INTERACTION_MODEL.md` | `40-planswift-product/` |
| `PLANSWIFT_JOIST_PDF_EXPORT_HANDOFF_2026_05_22.md` | `30-takeoffs-measurements/` |
| `PLANSWIFT_MVP_REQUIREMENTS.md` | `40-planswift-product/` |
| `PLANSWIFT_PROJECT_IMPORT_PLAN.md` | `40-planswift-product/` |
| `PLANSWIFT_TAKEOFF_TOOLS.md` | `40-planswift-product/` |
| `PLANSWIFT_USER_FLOW.md` | `40-planswift-product/` |
| `PLANSWIFT_VISUAL_BEHAVIOR.md` | `40-planswift-product/` |
| `PROJECT_CLEANUP_AUDIT_2026_05_08.md` | `70-architecture-refactor/` |
| `REFACTOR_UX_HANDOFF_2026_05_31.md` | `70-architecture-refactor/` |
| `ROOF_GENERATOR_NEXT_IDEAS_2026_05_21.md` | `50-3d-roof-ai/` |
| `ROOF_MODELING_IMPLEMENTATION_PROMPT.md` | `50-3d-roof-ai/` |
| `ROOF_MODELING_VISION.md` | `50-3d-roof-ai/` |
| `ROOF_REVIT_WORKFLOW_PLAN.md` | `50-3d-roof-ai/` |
| `ROOF_STATUS_2026_05_19.md` | `50-3d-roof-ai/` |
| `ROOF_STRAIGHT_SKELETON_HANDOFF_2026_05_22.md` | `50-3d-roof-ai/` |
| `RU_PLANSWIFT_FLOW_EXPLAINED.md` | `40-planswift-product/` |
| `RULER_AND_COUNT_DISPLAY_HANDOFF_2026_05_14.md` | `30-takeoffs-measurements/` |
| `SAMPLE_GUIDE_PROJECT.md` | `00-start-here/` |
| `SHEET_NOTES_AND_VIEWPORT_SMOKE_HANDOFF_2026_05_10.md` | `20-import-pages-metadata/` |
| `SHEET_RENDER_STRATEGY_2026_06_01.md` | `10-performance-render/` |
| `TAKEOFF_TEMPLATE_PRESETS_2026_06_01.md` | `30-takeoffs-measurements/` |
| `TAKEOFF_TEMPLATES_HANDOFF_2026_06_01.md` | `30-takeoffs-measurements/` |
| `TAKEOFF_TOOLS_AND_PLANSWIFT_IMPORT_HANDOFF_2026_05_24.md` | `30-takeoffs-measurements/` |
| `TAKEOFF_TREE_PERFORMANCE_HANDOFF_2026_05_10.md` | `30-takeoffs-measurements/` |
| `TAKEOFF_TREE_STALE_RF_UI_FIX_2026_05_13.md` | `30-takeoffs-measurements/` |
| `THIRD_HAND_PRODUCT_VISION.md` | `40-planswift-product/` |
| `THREE_D_ROOF_SYSTEM_MAP.md` | `50-3d-roof-ai/` |
| `UNDERLAYMENT_CLARITY_HANDOFF_2026_05_28.md` | `10-performance-render/` |
| `UX_AND_NEW_WINDOWS_IMPLEMENTATION_PROMPT.md` | `60-ux-ui/` |
| `UX_AUDIT_OURCORE_2026_05_30.md` | `60-ux-ui/` |
| `UX_DESIGN_IMPLEMENTATION_PLAN_2026_05_04.md` | `60-ux-ui/` |
| `UX_DESIGN_PROPOSALS_2026_05_04.md` | `60-ux-ui/` |
| `UX_DESIGN_RESEARCH_AUDIT_2026_05_04.md` | `60-ux-ui/` |
| `UX_DESIGN_RESEARCH_DEEPER_2026_05_04.md` | `60-ux-ui/` |
| `WORKSPACE_TAB_COMMAND_MAP.md` | `60-ux-ui/` |

Rollback: move each file from `Destination/Source` back to `docs/Source`.
