# Реестр документов OurPlanCore — 2026-09-06

**Структурная инвентаризация, не глубокая смысловая приёмка всех файлов.**
Исходный снимок: 142 Markdown-файла приложения. Текущий реестр: 145, включая
три новых документа этой работы — improvement plan, аудит и этот реестр.
Область: корневые Markdown, Markdown в docs и docs_sources; файлы из bin/obj,
сторонних библиотек и отдельного сайта сюда не включены.

[Навигация](../README.md) · [аудит и границы проверки](KB_DOCS_AUDIT_2026_09_06.md) ·
[исходная машинная инвентаризация](../../../kb-review-20260906-212506/inventory-before.json).
Классы назначены по роли, разделу, заголовкам и выборочному чтению. Только
основной current handoff и перечисленные в аудите сценарии сопоставлялись
с кодом глубоко; классификация старого файла не подтверждает его claims.

## Классы и порядок доверия

| Класс | Как использовать |
|---|---|
| **K — текущие canonical** | Начальная точка текущей задачи; внутри всё равно различать release evidence и планы. Явная задача пользователя выше документа. |
| **H — historical / proposal** | История, идеи, прежние criteria и task prompts. Не исполнять старое «next» без текущей проверки. |
| **R — technical reference** | Детали конкретного механизма; дата/callers/defaults требуют сверки перед изменением поведения. |
| **E — generated / evidence** | Журналы, результаты, source indexes и архивные snapshots. Не все созданы автоматически; класс не означает PASS или актуальность. |

Классы взаимоисключающие. Хронологический или архивный файл может быть полезен
как evidence, но текущую версию и порядок работ устанавливают K-документы.
Все 142 baseline-пути сохранены; «новый» обозначает добавление после снимка,
а не утверждение, что весь файл уже опубликован.

| Класс | Файлов |
|---|---:|
| K | 12 |
| H | 105 |
| R | 8 |
| E | 20 |
| **Всего** | **145** |

## K — Текущие canonical

| Документ | Снимок | Роль / ограничение |
|---|---|---|
| [AGENTS.md](../../AGENTS.md) | baseline | Правила работы и границы изменений; сверены в текущем аудите. |
| [CLAUDE.md](../../CLAUDE.md) | baseline | Короткий вход для агента; subordinate к AGENTS и явной задаче. |
| [docs/00-start-here/DOCUMENT_REGISTER_2026_09_06.md](../00-start-here/DOCUMENT_REGISTER_2026_09_06.md) | новый | Полная структурная инвентаризация; не общий semantic PASS. |
| [docs/00-start-here/KB_DOCS_AUDIT_2026_09_06.md](../00-start-here/KB_DOCS_AUDIT_2026_09_06.md) | новый | Область выполненной сверки, исправления и ограничения. |
| [docs/60-ux-ui/KEYBOARD_SHORTCUTS.md](../60-ux-ui/KEYBOARD_SHORTCUTS.md) | baseline | Текущие назначения, настройка и контексты. |
| [docs/60-ux-ui/WORKSPACE_TAB_COMMAND_MAP.md](../60-ux-ui/WORKSPACE_TAB_COMMAND_MAP.md) | baseline | Текущая карта лент, рабочих вкладок и команд. |
| [docs/70-architecture-refactor/IMPROVEMENT_PLAN_2026_09_06.md](../70-architecture-refactor/IMPROVEMENT_PLAN_2026_09_06.md) | новый | Девять OPEN улучшений с evidence/ownership/acceptance. |
| [docs/CURRENT_OURPLANECORE_STATUS.md](../CURRENT_OURPLANECORE_STATUS.md) | baseline | Текущие функции, выпуск и открытые ограничения. |
| [docs/OURPLANCORE_MASTER_REMEDIATION_AND_RELEASE_PLAN.md](../OURPLANCORE_MASTER_REMEDIATION_AND_RELEASE_PLAN.md) | baseline | Действующие полные фазы; исторический baseline внутри. |
| [docs/PROJECT_CONTEXT.md](../PROJECT_CONTEXT.md) | baseline | Технический handoff, хранение и владельцы операций. |
| [docs/README.md](../README.md) | baseline | Главная навигация app docs и правила достоверности. |
| [docs/STRATEGY_APP_EVIDENCE_2026_09_06.md](../STRATEGY_APP_EVIDENCE_2026_09_06.md) | baseline | Точная связь strategy, source и release evidence. |

## R — Technical reference

| Документ | Снимок | Роль / ограничение |
|---|---|---|
| [docs/00-start-here/SAMPLE_GUIDE_PROJECT.md](../00-start-here/SAMPLE_GUIDE_PROJECT.md) | baseline | Устройство sample job; сценарий перед повторением сверять с кодом. |
| [docs/20-import-pages-metadata/SHEET_METADATA_DETECTION_RULES_2026_06_03.md](../20-import-pages-metadata/SHEET_METADATA_DETECTION_RULES_2026_06_03.md) | baseline | Датированная спецификация детектора; актуальные правила сверять в Settings/code. |
| [docs/30-takeoffs-measurements/DISPLAY_SETTINGS_AND_VIEWPORT_LABELS.md](../30-takeoffs-measurements/DISPLAY_SETTINGS_AND_VIEWPORT_LABELS.md) | baseline | Связи display settings и labels; не замена текущей карте UI. |
| [docs/30-takeoffs-measurements/EXCEL_MACRO_EXPORT_WORKFLOW_2026_07_29.md](../30-takeoffs-measurements/EXCEL_MACRO_EXPORT_WORKFLOW_2026_07_29.md) | baseline | Детали macro workflow; полный Excel gate отдельно остаётся открытым. |
| [docs/30-takeoffs-measurements/TAKEOFF_TEMPLATE_PRESETS_2026_06_01.md](../30-takeoffs-measurements/TAKEOFF_TEMPLATE_PRESETS_2026_06_01.md) | baseline | Датированная reference по template presets; текущие defaults перепроверять. |
| [docs/40-planswift-product/PLANSWIFT_USER_FLOW.md](../40-planswift-product/PLANSWIFT_USER_FLOW.md) | baseline | OurPlanCore flow уточнён 06.09; PlanSwift excerpts исторические. |
| [docs/50-3d-roof-ai/THREE_D_ROOF_SYSTEM_MAP.md](../50-3d-roof-ai/THREE_D_ROOF_SYSTEM_MAP.md) | baseline | Карта roof owners/legacy; availability подтверждает current status. |
| [docs/WALL_TRACE.md](../WALL_TRACE.md) | baseline | Техническая модель Wall Trace и диагностика; не новый release gate. |

## H — Historical / proposal

| Документ | Снимок | Роль / ограничение |
|---|---|---|
| [docs/00-start-here/DOCS_AUDIT_2026_06_06.md](../00-start-here/DOCS_AUDIT_2026_06_06.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/00-start-here/DOCS_ORGANIZATION_2026_06_02.md](../00-start-here/DOCS_ORGANIZATION_2026_06_02.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/00-start-here/NEXT_TASK_JOB_MOVE_AUTOREPAIR_AND_SHEET_RENDER_PERF_2026_06_06.md](../00-start-here/NEXT_TASK_JOB_MOVE_AUTOREPAIR_AND_SHEET_RENDER_PERF_2026_06_06.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/10-performance-render/DETAIL_TILE_STABILITY_DEBUG_HANDOFF_2026_06_02.md](../10-performance-render/DETAIL_TILE_STABILITY_DEBUG_HANDOFF_2026_06_02.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/10-performance-render/F1_VIEWPORT_OVERLAY_PROJECT_STORAGE_2026_07_22.md](../10-performance-render/F1_VIEWPORT_OVERLAY_PROJECT_STORAGE_2026_07_22.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/10-performance-render/HIGH_ZOOM_AREA_RENDER_2026_07_22.md](../10-performance-render/HIGH_ZOOM_AREA_RENDER_2026_07_22.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/10-performance-render/INSTANT_PAGE_OPEN_IDEAL_QUALITY_PLAN_2026_06_01.md](../10-performance-render/INSTANT_PAGE_OPEN_IDEAL_QUALITY_PLAN_2026_06_01.md) | baseline | План/спецификация/идея; наличие текста не доказывает реализацию. |
| [docs/10-performance-render/OURPLANECORE_SPEED_ACCELERATION_ANALYSIS_2026_06_02.md](../10-performance-render/OURPLANECORE_SPEED_ACCELERATION_ANALYSIS_2026_06_02.md) | baseline | План/спецификация/идея; наличие текста не доказывает реализацию. |
| [docs/10-performance-render/PAGE_OPEN_UI_PERF_HANDOFF_2026_05_28.md](../10-performance-render/PAGE_OPEN_UI_PERF_HANDOFF_2026_05_28.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/10-performance-render/PDF_FULL_RENDER_CACHE_HANDOFF_2026_05_28.md](../10-performance-render/PDF_FULL_RENDER_CACHE_HANDOFF_2026_05_28.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/10-performance-render/PDF_INLINE_RENDER_HANDOFF_2026_05_28.md](../10-performance-render/PDF_INLINE_RENDER_HANDOFF_2026_05_28.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/10-performance-render/PDF_PREVIEW_CACHE_HANDOFF_2026_05_28.md](../10-performance-render/PDF_PREVIEW_CACHE_HANDOFF_2026_05_28.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/10-performance-render/PDF_RENDER_PERF_STATUS_2026_05_28.md](../10-performance-render/PDF_RENDER_PERF_STATUS_2026_05_28.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/10-performance-render/PERFORMANCE_RENDER_AND_LAYERS_HANDOFF_2026_05_16.md](../10-performance-render/PERFORMANCE_RENDER_AND_LAYERS_HANDOFF_2026_05_16.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/10-performance-render/RASTER_WORK_ZOOM_HANDOFF_2026_06_07.md](../10-performance-render/RASTER_WORK_ZOOM_HANDOFF_2026_06_07.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/10-performance-render/SHEET_OVERLAY_CLARITY_DEBUG_HANDOFF_2026_06_02.md](../10-performance-render/SHEET_OVERLAY_CLARITY_DEBUG_HANDOFF_2026_06_02.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/10-performance-render/SHEET_RENDER_STRATEGY_2026_06_01.md](../10-performance-render/SHEET_RENDER_STRATEGY_2026_06_01.md) | baseline | Исторический baseline с текущей преамбулой; очередь задаёт новый improvement plan. |
| [docs/10-performance-render/SHEET_RENDERING_ANALYSIS_AND_INSTANT_STRATEGY_2026_06_04.md](../10-performance-render/SHEET_RENDERING_ANALYSIS_AND_INSTANT_STRATEGY_2026_06_04.md) | baseline | Исторический baseline с текущей преамбулой; очередь задаёт новый improvement plan. |
| [docs/10-performance-render/SPEED_NO_BLUR_OVERHAUL_2026_06_10.md](../10-performance-render/SPEED_NO_BLUR_OVERHAUL_2026_06_10.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/10-performance-render/STATIC_RASTER_PAGE_MODE_2026_07_22.md](../10-performance-render/STATIC_RASTER_PAGE_MODE_2026_07_22.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/10-performance-render/UNDERLAYMENT_CLARITY_HANDOFF_2026_05_28.md](../10-performance-render/UNDERLAYMENT_CLARITY_HANDOFF_2026_05_28.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/20-import-pages-metadata/AVENUE_MANUAL_NAMING_MAP_2026_06_03.md](../20-import-pages-metadata/AVENUE_MANUAL_NAMING_MAP_2026_06_03.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/20-import-pages-metadata/BLANK_JOB_BLANK_SHEET_HANDOFF_2026_05_30.md](../20-import-pages-metadata/BLANK_JOB_BLANK_SHEET_HANDOFF_2026_05_30.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/20-import-pages-metadata/JOB_CREATION_AND_CROP_NOTE_HANDOFF_2026_05_13.md](../20-import-pages-metadata/JOB_CREATION_AND_CROP_NOTE_HANDOFF_2026_05_13.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/20-import-pages-metadata/JOB_CREATION_AND_STORAGE_FLOW_2026_06_05.md](../20-import-pages-metadata/JOB_CREATION_AND_STORAGE_FLOW_2026_06_05.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/20-import-pages-metadata/OPEN_JOBS_PDF_IMPORT_RENDER_HANDOFF_2026_05_27.md](../20-import-pages-metadata/OPEN_JOBS_PDF_IMPORT_RENDER_HANDOFF_2026_05_27.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/20-import-pages-metadata/PAGE_FOLDER_SORT_AND_NOTES_HANDOFF_2026_05_13.md](../20-import-pages-metadata/PAGE_FOLDER_SORT_AND_NOTES_HANDOFF_2026_05_13.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/20-import-pages-metadata/PDF_FIRST_AUTO_SHEET_METADATA_PLAN.md](../20-import-pages-metadata/PDF_FIRST_AUTO_SHEET_METADATA_PLAN.md) | baseline | План/спецификация/идея; наличие текста не доказывает реализацию. |
| [docs/20-import-pages-metadata/PDF_IMPORT_SHEET_METADATA_HANDOFF_2026_05_22.md](../20-import-pages-metadata/PDF_IMPORT_SHEET_METADATA_HANDOFF_2026_05_22.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/20-import-pages-metadata/SHEET_METADATA_PRECISE_V2_HANDOFF_2026_07_14.md](../20-import-pages-metadata/SHEET_METADATA_PRECISE_V2_HANDOFF_2026_07_14.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/20-import-pages-metadata/SHEET_NOTES_AND_VIEWPORT_SMOKE_HANDOFF_2026_05_10.md](../20-import-pages-metadata/SHEET_NOTES_AND_VIEWPORT_SMOKE_HANDOFF_2026_05_10.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/30-takeoffs-measurements/AUTO_TRACE_AREAS_AND_WALLS_SPEC_2026_05_12.md](../30-takeoffs-measurements/AUTO_TRACE_AREAS_AND_WALLS_SPEC_2026_05_12.md) | baseline | План/спецификация/идея; наличие текста не доказывает реализацию. |
| [docs/30-takeoffs-measurements/BOOKMARKS_DOCK_HANDOFF_2026_05_28.md](../30-takeoffs-measurements/BOOKMARKS_DOCK_HANDOFF_2026_05_28.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/30-takeoffs-measurements/CUT_EDGE_AND_MERGE_SPLIT_PLAN_2026_06_03.md](../30-takeoffs-measurements/CUT_EDGE_AND_MERGE_SPLIT_PLAN_2026_06_03.md) | baseline | План/спецификация/идея; наличие текста не доказывает реализацию. |
| [docs/30-takeoffs-measurements/JOIST_ADD_AND_EXTRA_JOISTS_HANDOFF_2026_07_22.md](../30-takeoffs-measurements/JOIST_ADD_AND_EXTRA_JOISTS_HANDOFF_2026_07_22.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/30-takeoffs-measurements/KB_ANNOTATION_PAGES_SCALE_DEPLOY_2026_06_22.md](../30-takeoffs-measurements/KB_ANNOTATION_PAGES_SCALE_DEPLOY_2026_06_22.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/30-takeoffs-measurements/KB_DIMENSION_ANNOTATION_ORTHO_2026_07_18.md](../30-takeoffs-measurements/KB_DIMENSION_ANNOTATION_ORTHO_2026_07_18.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/30-takeoffs-measurements/KB_PAGES_SORT_METADATA_SHORTCUTS_2026_07_14.md](../30-takeoffs-measurements/KB_PAGES_SORT_METADATA_SHORTCUTS_2026_07_14.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/30-takeoffs-measurements/KB_TAKEOFF_TREE_PAGE_TREE_SELECTION_2026_06_28.md](../30-takeoffs-measurements/KB_TAKEOFF_TREE_PAGE_TREE_SELECTION_2026_06_28.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/30-takeoffs-measurements/PDF_EXTERIOR_CONTOUR_SNAP_HANDOFF_2026_06_04.md](../30-takeoffs-measurements/PDF_EXTERIOR_CONTOUR_SNAP_HANDOFF_2026_06_04.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/30-takeoffs-measurements/PDF_TAKEOFF_IMPORT_EDGE_SNAP_HANDOFF_2026_05_28.md](../30-takeoffs-measurements/PDF_TAKEOFF_IMPORT_EDGE_SNAP_HANDOFF_2026_05_28.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/30-takeoffs-measurements/PLANSWIFT_JOIST_PDF_EXPORT_HANDOFF_2026_05_22.md](../30-takeoffs-measurements/PLANSWIFT_JOIST_PDF_EXPORT_HANDOFF_2026_05_22.md) | baseline | План/спецификация/идея; наличие текста не доказывает реализацию. |
| [docs/30-takeoffs-measurements/RULER_AND_COUNT_DISPLAY_HANDOFF_2026_05_14.md](../30-takeoffs-measurements/RULER_AND_COUNT_DISPLAY_HANDOFF_2026_05_14.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/30-takeoffs-measurements/SHARP_SHEETS_PITCH_VALUE_VIEWPORT_HANDOFF_2026_08_08.md](../30-takeoffs-measurements/SHARP_SHEETS_PITCH_VALUE_VIEWPORT_HANDOFF_2026_08_08.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/30-takeoffs-measurements/TAKEOFF_TEMPLATES_HANDOFF_2026_06_01.md](../30-takeoffs-measurements/TAKEOFF_TEMPLATES_HANDOFF_2026_06_01.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/30-takeoffs-measurements/TAKEOFF_TOOLS_AND_PLANSWIFT_IMPORT_HANDOFF_2026_05_24.md](../30-takeoffs-measurements/TAKEOFF_TOOLS_AND_PLANSWIFT_IMPORT_HANDOFF_2026_05_24.md) | baseline | План/спецификация/идея; наличие текста не доказывает реализацию. |
| [docs/30-takeoffs-measurements/TAKEOFF_TREE_PAGE_JUMP_AND_V2_RELEASE_2026_06_02.md](../30-takeoffs-measurements/TAKEOFF_TREE_PAGE_JUMP_AND_V2_RELEASE_2026_06_02.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/30-takeoffs-measurements/TAKEOFF_TREE_PERFORMANCE_HANDOFF_2026_05_10.md](../30-takeoffs-measurements/TAKEOFF_TREE_PERFORMANCE_HANDOFF_2026_05_10.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/40-planswift-product/EWOOD_TAKEOFF_AUTO_ROUTING_RESEARCH_2026_05_07.md](../40-planswift-product/EWOOD_TAKEOFF_AUTO_ROUTING_RESEARCH_2026_05_07.md) | baseline | План/спецификация/идея; наличие текста не доказывает реализацию. |
| [docs/40-planswift-product/PLANSWIFT_INTERACTION_MODEL.md](../40-planswift-product/PLANSWIFT_INTERACTION_MODEL.md) | baseline | План/спецификация/идея; наличие текста не доказывает реализацию. |
| [docs/40-planswift-product/PLANSWIFT_MVP_REQUIREMENTS.md](../40-planswift-product/PLANSWIFT_MVP_REQUIREMENTS.md) | baseline | План/спецификация/идея; наличие текста не доказывает реализацию. |
| [docs/40-planswift-product/PLANSWIFT_PROJECT_IMPORT_PLAN.md](../40-planswift-product/PLANSWIFT_PROJECT_IMPORT_PLAN.md) | baseline | План/спецификация/идея; наличие текста не доказывает реализацию. |
| [docs/40-planswift-product/PLANSWIFT_TAKEOFF_TOOLS.md](../40-planswift-product/PLANSWIFT_TAKEOFF_TOOLS.md) | baseline | Исторические criteria; новая преамбула защищает current new-tool/Record semantics. |
| [docs/40-planswift-product/PLANSWIFT_VISUAL_BEHAVIOR.md](../40-planswift-product/PLANSWIFT_VISUAL_BEHAVIOR.md) | baseline | План/спецификация/идея; наличие текста не доказывает реализацию. |
| [docs/40-planswift-product/RU_PLANSWIFT_FLOW_EXPLAINED.md](../40-planswift-product/RU_PLANSWIFT_FLOW_EXPLAINED.md) | baseline | План/спецификация/идея; наличие текста не доказывает реализацию. |
| [docs/40-planswift-product/THIRD_HAND_PRODUCT_VISION.md](../40-planswift-product/THIRD_HAND_PRODUCT_VISION.md) | baseline | План/спецификация/идея; наличие текста не доказывает реализацию. |
| [docs/50-3d-roof-ai/3D_MESH_REVIT_POLISH.md](../50-3d-roof-ai/3D_MESH_REVIT_POLISH.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/50-3d-roof-ai/3D_ROOF_RENDER_HANDOFF_2026_05_21.md](../50-3d-roof-ai/3D_ROOF_RENDER_HANDOFF_2026_05_21.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/50-3d-roof-ai/AI_3D_MASSING_VIEWER_IDEAS.md](../50-3d-roof-ai/AI_3D_MASSING_VIEWER_IDEAS.md) | baseline | План/спецификация/идея; наличие текста не доказывает реализацию. |
| [docs/50-3d-roof-ai/AI_FILL_CROP_HINTS_AND_NOTES_HANDOFF_2026_05_12.md](../50-3d-roof-ai/AI_FILL_CROP_HINTS_AND_NOTES_HANDOFF_2026_05_12.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/50-3d-roof-ai/AI_MARKER_TRAINING_IDEAS.md](../50-3d-roof-ai/AI_MARKER_TRAINING_IDEAS.md) | baseline | План/спецификация/идея; наличие текста не доказывает реализацию. |
| [docs/50-3d-roof-ai/ROOF_GENERATOR_NEXT_IDEAS_2026_05_21.md](../50-3d-roof-ai/ROOF_GENERATOR_NEXT_IDEAS_2026_05_21.md) | baseline | План/спецификация/идея; наличие текста не доказывает реализацию. |
| [docs/50-3d-roof-ai/ROOF_MODELING_IMPLEMENTATION_PROMPT.md](../50-3d-roof-ai/ROOF_MODELING_IMPLEMENTATION_PROMPT.md) | baseline | План/спецификация/идея; наличие текста не доказывает реализацию. |
| [docs/50-3d-roof-ai/ROOF_MODELING_VISION.md](../50-3d-roof-ai/ROOF_MODELING_VISION.md) | baseline | План/спецификация/идея; наличие текста не доказывает реализацию. |
| [docs/50-3d-roof-ai/ROOF_REVIT_WORKFLOW_PLAN.md](../50-3d-roof-ai/ROOF_REVIT_WORKFLOW_PLAN.md) | baseline | План/спецификация/идея; наличие текста не доказывает реализацию. |
| [docs/50-3d-roof-ai/ROOF_STATUS_2026_05_19.md](../50-3d-roof-ai/ROOF_STATUS_2026_05_19.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/50-3d-roof-ai/ROOF_STRAIGHT_SKELETON_HANDOFF_2026_05_22.md](../50-3d-roof-ai/ROOF_STRAIGHT_SKELETON_HANDOFF_2026_05_22.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/60-ux-ui/BLUEBEAM_DESIGN_SYSTEM.md](../60-ux-ui/BLUEBEAM_DESIGN_SYSTEM.md) | baseline | План/спецификация/идея; наличие текста не доказывает реализацию. |
| [docs/60-ux-ui/MANAGERS_DEEP_RESEARCH.md](../60-ux-ui/MANAGERS_DEEP_RESEARCH.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/60-ux-ui/OPEN_JOB_REDESIGN_PLAN.md](../60-ux-ui/OPEN_JOB_REDESIGN_PLAN.md) | baseline | План/спецификация/идея; наличие текста не доказывает реализацию. |
| [docs/60-ux-ui/UX_AND_NEW_WINDOWS_IMPLEMENTATION_PROMPT.md](../60-ux-ui/UX_AND_NEW_WINDOWS_IMPLEMENTATION_PROMPT.md) | baseline | План/спецификация/идея; наличие текста не доказывает реализацию. |
| [docs/60-ux-ui/UX_AUDIT_OURCORE_2026_05_30.md](../60-ux-ui/UX_AUDIT_OURCORE_2026_05_30.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/60-ux-ui/UX_DESIGN_IMPLEMENTATION_PLAN_2026_05_04.md](../60-ux-ui/UX_DESIGN_IMPLEMENTATION_PLAN_2026_05_04.md) | baseline | План/спецификация/идея; наличие текста не доказывает реализацию. |
| [docs/60-ux-ui/UX_DESIGN_PROPOSALS_2026_05_04.md](../60-ux-ui/UX_DESIGN_PROPOSALS_2026_05_04.md) | baseline | План/спецификация/идея; наличие текста не доказывает реализацию. |
| [docs/60-ux-ui/UX_DESIGN_RESEARCH_AUDIT_2026_05_04.md](../60-ux-ui/UX_DESIGN_RESEARCH_AUDIT_2026_05_04.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/60-ux-ui/UX_DESIGN_RESEARCH_DEEPER_2026_05_04.md](../60-ux-ui/UX_DESIGN_RESEARCH_DEEPER_2026_05_04.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/60-ux-ui/UX_UI_AUDIT_2026_07_07.md](../60-ux-ui/UX_UI_AUDIT_2026_07_07.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/70-architecture-refactor/CODEBASE_HEALTH_AUDIT_2026_06_01.md](../70-architecture-refactor/CODEBASE_HEALTH_AUDIT_2026_06_01.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/70-architecture-refactor/DECOMPOSITION_PLAN_2026_07_07.md](../70-architecture-refactor/DECOMPOSITION_PLAN_2026_07_07.md) | baseline | План/спецификация/идея; наличие текста не доказывает реализацию. |
| [docs/70-architecture-refactor/DESKTOP_REFACTOR_UI_HANDOFF_2026_07_08.md](../70-architecture-refactor/DESKTOP_REFACTOR_UI_HANDOFF_2026_07_08.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/70-architecture-refactor/FIX_PLAN_SAVE_UNDO_FEEDBACK_2026_07_07.md](../70-architecture-refactor/FIX_PLAN_SAVE_UNDO_FEEDBACK_2026_07_07.md) | baseline | План/спецификация/идея; наличие текста не доказывает реализацию. |
| [docs/70-architecture-refactor/PARALLEL_AGENT_MERGE_REVIEW.md](../70-architecture-refactor/PARALLEL_AGENT_MERGE_REVIEW.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/70-architecture-refactor/PARALLEL_CODEX_BOARD.md](../70-architecture-refactor/PARALLEL_CODEX_BOARD.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/70-architecture-refactor/PROJECT_CLEANUP_AUDIT_2026_05_08.md](../70-architecture-refactor/PROJECT_CLEANUP_AUDIT_2026_05_08.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/70-architecture-refactor/REFACTOR_UX_HANDOFF_2026_05_31.md](../70-architecture-refactor/REFACTOR_UX_HANDOFF_2026_05_31.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/70-architecture-refactor/TECH_DEBT_AUDIT_2026_07_07.md](../70-architecture-refactor/TECH_DEBT_AUDIT_2026_07_07.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [docs/80-web-version/DATA_MODEL_REFERENCE.md](../80-web-version/DATA_MODEL_REFERENCE.md) | baseline | Proposal веб-версии; не модель поставленного WPF-приложения. |
| [docs/80-web-version/INTERACTION_SPEC.md](../80-web-version/INTERACTION_SPEC.md) | baseline | Proposal веб-версии; не модель поставленного WPF-приложения. |
| [docs/80-web-version/WEB_MVP_PLAN.md](../80-web-version/WEB_MVP_PLAN.md) | baseline | Proposal веб-версии; не модель поставленного WPF-приложения. |
| [docs/90-archive-prompts/ARCHIVED_3D_MASSING_LOGIC_2026_05_08.md](../90-archive-prompts/ARCHIVED_3D_MASSING_LOGIC_2026_05_08.md) | baseline | Архивный prompt/журнал; не запускать как действующую задачу. |
| [docs/90-archive-prompts/CHANGELOG_2026-06-06_rotation-zoom-ribbon.md](../90-archive-prompts/CHANGELOG_2026-06-06_rotation-zoom-ribbon.md) | baseline | Архивный prompt/журнал; не запускать как действующую задачу. |
| [docs/90-archive-prompts/CODEX_TASK_01_OPC_DATAGRID.md](../90-archive-prompts/CODEX_TASK_01_OPC_DATAGRID.md) | baseline | Архивный prompt/журнал; не запускать как действующую задачу. |
| [docs/90-archive-prompts/FIX_AUDIT_PROMPT.md](../90-archive-prompts/FIX_AUDIT_PROMPT.md) | baseline | Архивный prompt/журнал; не запускать как действующую задачу. |
| [docs/95-marketing-content/from-multiposter-2026-07-10/CHANNEL_PLAYBOOK.md](../95-marketing-content/from-multiposter-2026-07-10/CHANNEL_PLAYBOOK.md) | baseline | Черновик контента/маркетинга; не спецификация доступной функции. |
| [docs/95-marketing-content/from-multiposter-2026-07-10/CONTENT_MAP.md](../95-marketing-content/from-multiposter-2026-07-10/CONTENT_MAP.md) | baseline | Черновик контента/маркетинга; не спецификация доступной функции. |
| [docs/95-marketing-content/from-multiposter-2026-07-10/DRAFT_post_02_subbota_ii.md](../95-marketing-content/from-multiposter-2026-07-10/DRAFT_post_02_subbota_ii.md) | baseline | Черновик контента/маркетинга; не спецификация доступной функции. |
| [docs/95-marketing-content/from-multiposter-2026-07-10/DRAFT_post_relaunch.md](../95-marketing-content/from-multiposter-2026-07-10/DRAFT_post_relaunch.md) | baseline | Черновик контента/маркетинга; не спецификация доступной функции. |
| [docs/95-marketing-content/from-multiposter-2026-07-10/POSTS_DRAFTS.md](../95-marketing-content/from-multiposter-2026-07-10/POSTS_DRAFTS.md) | baseline | Черновик контента/маркетинга; не спецификация доступной функции. |
| [docs/95-marketing-content/from-multiposter-2026-07-10/PROMPT_serial_zhizn.md](../95-marketing-content/from-multiposter-2026-07-10/PROMPT_serial_zhizn.md) | baseline | Черновик контента/маркетинга; не спецификация доступной функции. |
| [docs/95-marketing-content/from-multiposter-2026-07-10/REFERENCES_who_to_follow.md](../95-marketing-content/from-multiposter-2026-07-10/REFERENCES_who_to_follow.md) | baseline | Черновик контента/маркетинга; не спецификация доступной функции. |
| [docs/ARCHITECTURE_AUDIT_AND_REFACTOR_PLAN_2026_05_05.md](../ARCHITECTURE_AUDIT_AND_REFACTOR_PLAN_2026_05_05.md) | baseline | Исторический baseline с текущей преамбулой; очередь задаёт новый improvement plan. |
| [docs/OURPLANECORE_TASK_ROADMAP.md](../OURPLANECORE_TASK_ROADMAP.md) | baseline | Исторический baseline с текущей преамбулой; очередь задаёт новый improvement plan. |
| [docs/STRATEGY_2026.md](../STRATEGY_2026.md) | baseline | Исторический baseline с текущей преамбулой; очередь задаёт новый improvement plan. |
| [docs/UX_AUDIT_2026-07.md](../UX_AUDIT_2026-07.md) | baseline | Исторический handoff/исследование; callers, поведение и приоритеты требуют новой сверки. |
| [REFACTOR_ACTIONS.md](../../REFACTOR_ACTIONS.md) | baseline | Старые task prompts; предложения вроде «добавить logging» нельзя считать текущими. |

## E — Generated / evidence

| Документ | Снимок | Роль / ограничение |
|---|---|---|
| [docs/10-performance-render/PERFORMANCE_OPTIMIZATION_ROLLBACK_2026_05_09.md](../10-performance-render/PERFORMANCE_OPTIMIZATION_ROLLBACK_2026_05_09.md) | baseline | Исторический rollback/измерение; не финальные метрики 2.2.7. |
| [docs/30-takeoffs-measurements/MEASUREMENT_PAGE_LINK_AND_EDITING_POSTMORTEM.md](../30-takeoffs-measurements/MEASUREMENT_PAGE_LINK_AND_EDITING_POSTMORTEM.md) | baseline | Разбор конкретного дефекта; не полная текущая приёмка. |
| [docs/30-takeoffs-measurements/TAKEOFF_TREE_STALE_RF_UI_FIX_2026_05_13.md](../30-takeoffs-measurements/TAKEOFF_TREE_STALE_RF_UI_FIX_2026_05_13.md) | baseline | Историческая запись исправления и его проверки. |
| [docs/90-archive-prompts/agent-worktrees-2026-05-02/AGENTS_2026-05-02.md](../90-archive-prompts/agent-worktrees-2026-05-02/AGENTS_2026-05-02.md) | baseline | Архивный snapshot другого worktree; не инструкция действующего checkout. |
| [docs/90-archive-prompts/agent-worktrees-2026-05-02/docs/CURRENT_SMARTTAKEOFFS_STATUS.md](../90-archive-prompts/agent-worktrees-2026-05-02/docs/CURRENT_SMARTTAKEOFFS_STATUS.md) | baseline | Архивный snapshot другого worktree; не инструкция действующего checkout. |
| [docs/90-archive-prompts/agent-worktrees-2026-05-02/docs/PLANSWIFT_INTERACTION_MODEL.md](../90-archive-prompts/agent-worktrees-2026-05-02/docs/PLANSWIFT_INTERACTION_MODEL.md) | baseline | Архивный snapshot другого worktree; не инструкция действующего checkout. |
| [docs/90-archive-prompts/agent-worktrees-2026-05-02/docs/PLANSWIFT_MVP_REQUIREMENTS.md](../90-archive-prompts/agent-worktrees-2026-05-02/docs/PLANSWIFT_MVP_REQUIREMENTS.md) | baseline | Архивный snapshot другого worktree; не инструкция действующего checkout. |
| [docs/90-archive-prompts/agent-worktrees-2026-05-02/docs/PLANSWIFT_TAKEOFF_TOOLS.md](../90-archive-prompts/agent-worktrees-2026-05-02/docs/PLANSWIFT_TAKEOFF_TOOLS.md) | baseline | Архивный snapshot другого worktree; не инструкция действующего checkout. |
| [docs/90-archive-prompts/agent-worktrees-2026-05-02/docs/PLANSWIFT_USER_FLOW.md](../90-archive-prompts/agent-worktrees-2026-05-02/docs/PLANSWIFT_USER_FLOW.md) | baseline | Архивный snapshot другого worktree; не инструкция действующего checkout. |
| [docs/90-archive-prompts/agent-worktrees-2026-05-02/docs/PLANSWIFT_VISUAL_BEHAVIOR.md](../90-archive-prompts/agent-worktrees-2026-05-02/docs/PLANSWIFT_VISUAL_BEHAVIOR.md) | baseline | Архивный snapshot другого worktree; не инструкция действующего checkout. |
| [docs/90-archive-prompts/agent-worktrees-2026-05-02/docs/PROJECT_CONTEXT.md](../90-archive-prompts/agent-worktrees-2026-05-02/docs/PROJECT_CONTEXT.md) | baseline | Архивный snapshot другого worktree; не инструкция действующего checkout. |
| [docs/90-archive-prompts/agent-worktrees-2026-05-02/docs/RU_PLANSWIFT_FLOW_EXPLAINED.md](../90-archive-prompts/agent-worktrees-2026-05-02/docs/RU_PLANSWIFT_FLOW_EXPLAINED.md) | baseline | Архивный snapshot другого worktree; не инструкция действующего checkout. |
| [docs/90-archive-prompts/agent-worktrees-2026-05-02/docs_sources/planswift/README.md](../90-archive-prompts/agent-worktrees-2026-05-02/docs_sources/planswift/README.md) | baseline | Архивный snapshot другого worktree; не инструкция действующего checkout. |
| [docs/DATA_SAFETY_PREVIEW_2026_09_06.md](../DATA_SAFETY_PREVIEW_2026_09_06.md) | baseline | Проверенная область и ограничения предыдущего 2.2.6-preview. |
| [docs/DEVELOPMENT_LOG.md](../DEVELOPMENT_LOG.md) | baseline | Хронологический журнал; каждый результат относится к своей дате. |
| [docs/SESSION_2026_06_11_V2_SUMMARY.md](../SESSION_2026_06_11_V2_SUMMARY.md) | baseline | Отчёт датированной сессии; прежние тесты/метрики не считаются текущими. |
| [docs/SESSION_2026_06_14_PERF_SUMMARY.md](../SESSION_2026_06_14_PERF_SUMMARY.md) | baseline | Отчёт датированной сессии; прежние тесты/метрики не считаются текущими. |
| [docs/SESSION_2026_06_22_ANNOTATION_SELECTION_SCALE.md](../SESSION_2026_06_22_ANNOTATION_SELECTION_SCALE.md) | baseline | Отчёт датированной сессии; прежние тесты/метрики не считаются текущими. |
| [docs/SESSION_2026_06_26_SCALE_EXPORT_DISPLAY.md](../SESSION_2026_06_26_SCALE_EXPORT_DISPLAY.md) | baseline | Отчёт датированной сессии; прежние тесты/метрики не считаются текущими. |
| [docs_sources/planswift/README.md](../../docs_sources/planswift/README.md) | baseline | Индекс исходных reference exports; наличие инструкции не означает наличие всех exports. |

## Как обновлять реестр

При добавлении документа выбирай роль, указывай дату/source и связывай его с
одной canonical-страницей. Не перезаписывай историю новыми test counts.
При переносе файла сохраняй рабочие ссылки; source/evidence в соседнем локальном
комплекте нельзя выдавать за публичные assets.

Основная пользовательская [Knowledge Base](https://artrmiys.github.io/knowledge-base/)
живет в отдельном репозитории. Этот реестр описывает app docs; он не является
перечнем всех страниц сайта и не подтверждает публикацию локальных правок.
