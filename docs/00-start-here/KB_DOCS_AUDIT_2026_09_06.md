# Аудит документации OurPlanCore — 2026-09-06

Статус: **docs-only аудит и исправления локальных материалов**.
Проверенный код приложения: `42c44b0`, отдельная **2.2.7-preview / .NET 9**.
Позднейшие коммиты документации не меняют identity установленного EXE.

[Навигация](../README.md) · [реестр документов](DOCUMENT_REGISTER_2026_09_06.md) ·
[текущий handoff](../PROJECT_CONTEXT.md) ·
[доказательства выпуска](../STRATEGY_APP_EVIDENCE_2026_09_06.md) ·
[следующие улучшения](../70-architecture-refactor/IMPROVEMENT_PLAN_2026_09_06.md).

## Что проверялось и насколько глубоко

Исходная инвентаризация содержит **142 Markdown-файла приложения**:
корневые инструкции, `docs/` и `docs_sources/`. В конце этой работы их **145**:
добавлены план улучшений, этот аудит и реестр. Все документы перечислены и
классифицированы, но **глубокая смысловая проверка всех 142 не заявляется**.

С кодом и сохранёнными отчётами сопоставлены текущие status/context/master,
исторические strategy/architecture/roadmap, release evidence, основные
пользовательские маршруты и выборочные технические владельцы: package/lease/save,
recovery, settings, render/cache/repaint, trees, clipboard/Undo, shortcuts,
AI request/review и Excel export. Для прочих датированных handoffs и старых
proposals выполнена структурная классификация; перед реализацией требуется
новая сверка callers и поведения.

До изменения документов создан проверенный backup.
[Исходная инвентаризация](../../../kb-review-20260906-212506/inventory-before.json)
и [описание snapshot/защищённых объектов](../../../kb-review-20260906-212506/before.json)
сохраняют происхождение файлов. Эти локальные evidence-ссылки относятся к
комплекту работы; при переносе одного Git checkout они могут быть недоступны.

Этот аудит не запускал новых app/Excel/AI сценариев или тяжёлых тестов, не
менял app code, пакеты, рабочие проекты и процессы пользователя. Ни публикация
сайта, ни commit/push не следуют автоматически из наличия этой страницы.

## Найденные расхождения и внесённые исправления

| Находка | Что уточнено | Текущий источник |
|---|---|---|
| Старые Desktop-пути, версии и «следующая задача» выглядели текущими | Current handoff переписан; исходный checkout и stable updates не заменялись, 2.2.7 имеет отдельную поставку. Исторические пути оставлены только как provenance. | [PROJECT_CONTEXT](../PROJECT_CONTEXT.md), [README](../README.md) |
| 803, 807, 791 и native 214 смешивались между этапами | Текущие 807 C# отделены от render-этапа 803, .NET 10 spike 791 и ранних clipboard UI. У каждого результата сохранена область применимости. | [Evidence matrix](../STRATEGY_APP_EVIDENCE_2026_09_06.md) |
| Июльский AI/feature backlog мог подменить принятую стратегию | Июльская стратегия и старый roadmap помечены историческими; master сохраняет полные фазы, новая очередь состоит из девяти конкретных задач. | [Стратегия](../STRATEGY_2026.md), [roadmap](../OURPLANECORE_TASK_ROADMAP.md), [master](../OURPLANCORE_MASTER_REMEDIATION_AND_RELEASE_PLAN.md) |
| Майские размеры файлов и маленький root partial создавали неверное представление об архитектуре | Добавлен текущий delta: MainWindow 202 partials / 68,297 физических строк; PdfViewport 72 / 31,105. Это индикаторы связи состояний, не самостоятельные runtime bugs. | [Architecture audit](../ARCHITECTURE_AUDIT_AND_REFACTOR_PLAN_2026_05_05.md) |
| Сильные утверждения о безусловной сохранности данных | Указаны границы typed readers, write barriers, metadata snapshots и внешнего backup. General AppSettings/SettingsPresetStore остаются отдельной Phase 4. | [Recovery scope](../PROJECT_CONTEXT.md), [пункт 1 плана](../70-architecture-refactor/IMPROVEMENT_PLAN_2026_09_06.md) |
| «Все AI-вызовы с CancellationToken.None» | Исправлено: CTS/token и отмена при потере write access уже существуют; видимые Cancel/progress и typed machine outputs ещё открыты. | [AiRequestActions](../../MainWindow.AiRequestActions.cs#L250), [BusyOverlay](../../MainWindow.BusyOverlay.cs) |
| Любое Save As могло читаться как преобразование в пакет | Пакет остаётся пакетом; legacy Save As создаёт папку-копию. Local recovery и package checkpoint — разные состояния. | [ProjectPackage](../../MainWindow.ProjectPackage.cs#L39), [TakeoffsPersistence](../../MainWindow.TakeoffsPersistence.cs#L9), [JobRecovery](../../MainWindow.JobRecovery.cs#L247) |
| Старые takeoff criteria предлагали переиспользовать item при нажатии инструмента | Старый PlanSwift tools документ теперь исторический: новый tool создаёт item, Record продолжает выбранный. Старые criteria не являются разрешением менять поведение. | [Tools research](../40-planswift-product/PLANSWIFT_TAKEOFF_TOOLS.md), [актуальный flow](../40-planswift-product/PLANSWIFT_USER_FLOW.md) |
| Результаты скорости могли звучать как ускорение всех операций и решение памяти | Сохранены ухудшившиеся соседние показатели и ограничение измерения памяти до Close; исправленные bitmap/repaint дефекты отделены от будущего общего budget. | [Final comparison](../../../PERFORMANCE-COMPARISON-227-FINAL.md) |
| .NET 10 описывался бы как ещё не начатый либо завершённый переход | Эксперимент выполнен отдельно; migration gate не пройден из-за полного Excel smoke, причина VBA/COM не установлена. | [Platform matrix](../../../platform-evidence/PLATFORM-COMPATIBILITY.md) |
| Записанные Excel-строки могли ошибочно считаться автоматически сохранёнными/откаченными | Current Excel не сохраняет workbook; macro export пишет source values до запуска VBA и может оставить частичные изменения после ошибки. | [Active Excel](../../Models/ActiveExcelTakeoffExportService.cs#L38), [macro service](../../Models/ExcelMacroTakeoffExportService.cs#L115) |

Исторические результаты не удалены и не переписаны под нынешние числа.
Этот аудит также не переименовывал тематические каталоги, не удалял user assets
и не объявлял неиспользуемыми функции только по размеру файла или старому плану.

## Точная применимость release evidence

| Проверка | Подтверждённое состояние |
|---|---|
| Основной current source | `42c44b0`: build 0 warnings / 0 errors, 807/807 C#, exit 0. Python helpers: 29/29; после того запуска они не менялись. |
| Финальный render/native этап | `ae2a8ee`: отдельный EXE прошёл 214/214 листов Westminster, 371 takeoff, 3589 measurements и safety checks, exit 0. Это не повтор native suite на 42c. |
| Последнее изменение EXE | 42c исправляет поиск profile marker рядом с фактическим compressed EXE. Обычный запуск через отдельный .lnk подтвердил правильный профиль и свежий лог без ошибок. |
| P06 | 17 WPF сценариев и отдельный reopen с точными 371/3589, оба exit 0, на более ранней 2.2.7 assembly; поздние render/profile правки не превращают это в новый P06 UI run. |
| .NET 10 | Отдельный `69031529`: 791/791 C#, 29 Python, native/PDF/Skia/OCR PASS; полный Excel gate FAIL. Основной runtime не мигрировал. |

Точные SHA, журналы и ограничения находятся в
[evidence matrix](../STRATEGY_APP_EVIDENCE_2026_09_06.md) и
[финальном QA](../../../QA-REPORT-227.md). Старый Preview profile был затронут
ошибочным промежуточным marker launch; нельзя утверждать его полную
неизменность. Последствия и финальная изоляция описаны отдельно.

## Девять оставшихся задач приложения

Все пункты ниже **OPEN**, а не реализованы документационным аудитом.
Полные границы файлов, acceptance criteria и зависимости — в
[едином плане улучшений](../70-architecture-refactor/IMPROVEMENT_PLAN_2026_09_06.md).

| Порядок | Польза и основание | Ключ к приёмке |
|---|---|---|
| 1. Protected settings | `LoadJson`/TryLoad скрывают разницу Missing/Invalid; следующий Save может заменить повреждённые settings. | Typed result, last-good/quarantine, видимый scope, блокирование Apply при invalid override; прежние defaults/Reset/presets/global/job сохраняются. |
| 2. Excel recovery/gate | VBA/COM smoke отказал на обоих runtimes; запись values происходит до macro. | Один disposable workbook/payload проходит A2/A3/SQFT и structural workflow с проверкой фактических строк/формул; отказ имеет backup и stage. |
| 3. Воспроизводимая поставка | Console harness существует, SDK/package locks и CI не завершены. | Чистый checkout, закреплённые зависимости, тестовые timeout/filter/logs, настоящий compressed .lnk launch; runtime migration после Excel gate. |
| 4. Workspace capacity и backup | Реальный launch отказал из-за свободного места; metadata snapshots не покрывают весь проект. | Учёт места, безопасная очистка только закрытых clean copies; отдельный полный manifest и restore-run. |
| 5. Native memory | Независимые cache budgets не покрывают все leases/in-flight/native buffers; утечка не доказана. | Общий ownership/budget без освобождения живых leases; длительный main/detached цикл и измерение после Close. |
| 6. Деревья и поиск | Takeoffs reload около 4 s; UI строит готовые TreeViewItem, поиск обходит дерево при вводе. | Файловый snapshot вне UI, отмена старого поиска, реальные selection/expansion/copy/Undo остаются прежними. |
| 7. Dirty-only Save | Manual Save переписывает все takeoffs, даже после ускорения metadata. | Пропуск неизменённых данных с поколениями dirty state; отказ не теряет pending work; repeated real-project comparison. |
| 8. AI Cancel/progress и typed actions | Отмена в коде уже есть; UI и разбор machine JSON неполны. | Видимые Cancel/N из M, отказ/неполный ответ/смена job не применяют данные; human review остаётся обязательным. |
| 9. Владение состоянием | Большие partials сохраняют общие mutable fields. | Один workflow, например SimilarCount review, получает явные input/state/result и один save/refresh completion; без общей переписки MVVM/XAML. |

Финальное Westminster сравнение: Save 128.04→8.97 s, Close 120.64→2.73 s,
paint p95 358→330.5 ms. Одновременно median paint +2.1%, Pages expand-all
+18.7%, sampled private peak +2.2%. Это конкретные ограничения данной методики;
memory sampling заканчивается перед Close, test-host open не включает startup
процесса/self-extraction. Ни «всё быстрее», ни «утечка подтверждена» из этого
не следует.

## Связь с основной пользовательской KB

Основной пользовательский адрес —
[Knowledge Base](https://artrmiys.github.io/knowledge-base/).
Пользовательский вход:
[OurPlanCore — начало работы](https://artrmiys.github.io/knowledge-base/reference/ourplancore-start/).
Эти адреса задают назначение материалов, **не доказывают публикацию текущих правок**.

Локальные исходники страниц `reference/ourplanecore.md`,
`ourplancore-start.md`, `ourplancore-shortcuts.md`,
`job-creation-storage.md`, `ourplancore-troubleshooting.md` и
`ourplancore-changelog.md` сверялись по пользовательским маршрутам.
Проверены различия new tool/Record, Same/New/Cancel и scale при paste,
Page Flip против зеркала measurements, закреплённый PDF Preview и Esc для pan,
Save/Save As legacy, local recovery, область Project Data Recovery, PNG Prepare
и Raster First, Excel partial writes, AI attachment review/Apply Accepted.

Cross-review выявил оговорки, внесённые в локальный основной гайд:
пустые items после Undo удаляются только без последующих правок; Close с
выбором local recovery не обновляет пакет; AI может включать дополнительные
context crops/JSON и показывает список вложений; модель выбирается через
AI Settings, а не несуществующий bare `OPENAI_MODEL`.
Рабочая переменная override в коде — `OURPLANCORE_OPENAI_MODEL`.

Технические детали реализации остаются в app docs, пользовательские действия —
в KB. Смысл не должен дублироваться двумя конфликтующими инструкциями.
[Локальные site checks](../../../kb-review-20260906-212506/site-audit-final.json)
и [strict build log](../../../kb-review-20260906-212506/kb-build-final.log)
относятся к локальной сборке. Факт публикации устанавливается отдельной
проверкой GitHub Pages/свежей страницы после одобренной поставки.

## Что ещё требует документационной проверки

- Старые proposals, prompts и snapshot-копии остаются историей; реестр не
  гарантирует правильность каждого их утверждения. Например, старый
  `REFACTOR_ACTIONS.md` начинается с предложения добавить уже существующее
  логирование и не должен задавать новую очередь.
- У части архивных материалов встречается испорченная кодировка. Это отдельная
  редакторская задача с восстановлением из исходника; содержание не угадывалось.
- Числа тестов, датированные метрики, названия команд и ссылки на локальные
  evidence нужно проверять при следующем релизе.
- Старые PlanSwift/Bluebeam сравнения не являются свежим конкурентным аудитом.
- Полная accessibility-приёмка, CI discovery и будущие функции из master не
  объявлены готовыми только из-за нового описания.

Новые файлы этого этапа: данный аудит и DOCUMENT_REGISTER; ранее в этой же
работе добавлен IMPROVEMENT_PLAN. Старый PLANSWIFT_TAKEOFF_TOOLS получил только
историческую преамбулу. Файлы добавляются в Git адресно после проверки, без
`git add -A`; публикация и замена приложения не являются частью этого шага.
