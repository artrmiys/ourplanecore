# OurPlanCore: план следующих улучшений

Срез: **2026-09-06, source `42c44b0`, отдельная 2.2.7-preview**. Все пункты ниже — **OPEN / не реализованы этим аудитом**. Порядок учитывает сохранность работы и наблюдённые ограничения, а не размер старого списка задач. Изменения выполняются отдельными проверяемыми этапами; переписывание приложения целиком не предлагается.

Текущее состояние и границы доказательств: [PROJECT_CONTEXT](../PROJECT_CONTEXT.md), [strategy → evidence](../STRATEGY_APP_EVIDENCE_2026_09_06.md), [финальный QA](../../../QA-REPORT-227.md). Действующий master остаётся [картой полных фаз](../OURPLANCORE_MASTER_REMEDIATION_AND_RELEASE_PLAN.md).

## Как читать основания

- **Наблюдалось** — есть сохранённый результат реального запуска или воспроизведение.
- **Подтверждено кодом** — путь прочитан; новый runtime failure в этом аудите не воспроизводился.
- **Гипотеза** — требует адресного измерения до изменения production.

807/807 C# и 29/29 Python — текущие выполненные проверки, а не доказательство отсутствия всех ошибок. Старые 791/803, 17 clipboard-сценариев, 214-листовые native-прогоны и финальный запуск имеют разные идентичности; их нельзя складывать в один новый общий PASS.

## 1. Защитить правила и настройки от тихого fallback

**Польза:** повреждённый job override не заменит незаметно правила именования, масштаба, сортировки или папок.

**Основание — подтверждено кодом.** `SettingsPresetStore.LoadJson` возвращает одинаковый `null` для отсутствия и ошибки JSON; `Resolve`/`ResolvePageSort` переходят к global/default. `AppSettingsStore.TryLoad` пишет предупреждение в лог и возвращает defaults; последующий `Save` не имеет барьера для исходного повреждённого файла. Это оставшийся Phase 4; новый защищённый store горячих клавиш его не закрывает.

**Владение:** [SettingsPresetStore](../../Models/SettingsPresetStore.cs), [AppSettingsStore](../../Models/AppSettingsStore.cs), `MainWindow.SettingsManager.*.cs`; образец отдельного защищённого store — [KeyboardShortcutStore](../../Models/KeyboardShortcutStore.cs).

**Приёмка:** Missing/Valid/Invalid различимы; last-good и оригинал повреждения сохраняются; effective scope виден в UI. Invalid выбранный override блокирует Apply Auto Name/Scale/Sort до явного решения. Валидные прежние defaults, Reset, presets, global/job save и Apply дают прежний результат. Повреждение, locked file и отказ atomic write проверены на копии проекта; отмена не меняет правила или данные.

**Риск/зависимость:** это изменение поведения при ошибке, требует предварительного согласования точного recovery UI. Не включать одновременно новые таксономии/правила.

## 2. Закрыть Excel gate и сделать отказ макроса восстановимым

**Польза:** не оставлять непонятный частичный экспорт и разблокировать проверенный переход runtime.

**Основание — наблюдалось.** A3_Walls_Calc_AllGroup отказал одинаково на .NET 9, .NET 10 и PowerShell COM; structural smoke отказал на C_SumTheSameValues. Процедуры найдены, причина 0x800A03EC не установлена. В [ExcelMacroTakeoffExportService](../../Models/ExcelMacroTakeoffExportService.cs) запись `Value2` предшествует `RunWorkbookMacro`; COM failure прямо сообщает, что записанные строки оставлены. Это подтверждённая кодом возможность частичной записи, не измерение её объёма в данном smoke и не установленная причина самого VBA отказа.

**Владение:** этот service, [ExcelWorkbookSelectionPolicy](../../Models/ExcelWorkbookSelectionPolicy.cs), [ExcelMacroSmokeHarness](../../Tests/ExcelMacroSmokeHarness.cs), [StructuralExcelMacroSmokeHarness](../../Tests/StructuralExcelMacroSmokeHarness.cs), текущий поставляемый TemplateCom. [Матрица .NET 10](../../../platform-evidence/PLATFORM-COMPATIBILITY.md).

**Приёмка:** один и тот же disposable workbook/hash и payload проходят A2/A3/SQFT и structural workflow; проверены реальные выходные значения/формулы, границы секций и отсутствие новых Excel errors. Перед изменениями рабочего workbook создаётся reviewable backup; отказ имеет точный stage и путь восстановления. Ни одна живая чужая книга/Excel-процесс не закрывается. Исходный TemplateCom и рабочие книги сохраняют контрольные суммы в диагностике.

**Риск/зависимость:** сначала локализовать VBA/COM причину и критерии выходных строк; не считать массовую замену VBA или runtime исправлением без сравнения.

## 3. Сделать сборку и поставку воспроизводимыми

**Польза:** новый компьютер/SDK не меняет незаметно сборку, а зелёный harness означает именно проверенный пакет.

**Основание — подтверждено кодом и артефактами.** Основной target — .NET 9; финальные замеры выполнены на runtime 9.0.3. В корне нет `global.json`, package lock и `.github` CI; [тестовый проект](../../Tests/OurPlanCore.Tests.csproj) — console harness. [Program](../../Tests/Program.cs) исполняет общий список без универсального per-test timeout/filter. Отдельный .NET 10 spike прошёл 791 C# / 29 Python и native/OCR, но `MigrationGatePassed=false` из-за пункта 2. Это исторический spike, не проверка всех 807 текущих тестов на .NET 10.

**Владение:** `ourplancore.csproj`, `Tests/OurPlanCore.Tests.csproj`, `Tests/Program.cs`, `app.manifest`, новая CI/release-конфигурация и package helper.

**Приёмка:** закреплён SDK и зависимости, чистый checkout собирается одинаково; CI выполняет build, C#/Python, проверку зависимостей/лицензий и manifest. Console modes сохранены, есть filter/timeout и отдельные PID/test logs. Отдельный smoke запускает compressed EXE через настоящий ярлык без profile env overrides и проверяет marker/profile/fresh log. Список поддерживаемых Windows соответствует проверенному runtime. Обновление servicing и затем .NET 10 выполняются в изолированном кандидате; перенос основного runtime — только после полного Excel gate.

**Риск/зависимость:** не менять runtime и архитектуру одновременно. Наличие у `app.manifest` Windows 7/8/8.1 GUID не является доказательством поддержки этих ОС.

## 4. Управлять рабочими копиями и явно разделить recovery и backup

**Польза:** большой проект открывается без ручного поиска временных гигабайтов; пользователь понимает, что действительно можно восстановить.

**Основание — наблюдалось и подтверждено кодом.** Финальный обычный запуск был остановлен free-space guard; архивирование двух завершённых native-профилей освободило 5.65 GB. Guard корректно отказал, это не потеря данных. [Workspace](../../Models/OurPlanPackageWorkspace.Support.cs) использует `AppIdentity.LocalRoot`; [prune](../../Models/OurPlanPackageWorkspace.cs) уже есть, с default 90 days/minimum 7. [JobRecoveryService](../../Models/JobRecoveryService.cs) сохраняет 20 metadata snapshots Pages/Takeoffs; это не полная копия PDF, AI context, Excel или настроек. [Package selector](../../Models/OurPlanPackageFileSelector.cs) исключает `.snapshots` и `.undo` из переносимого пакета.

**Владение:** `OurPlanPackageWorkspace.*`, [OurPlanPackageArchive](../../Models/OurPlanPackageArchive.cs), `AppSettingsStore`, Storage/Settings UI и отдельный backup workflow.

**Приёмка:** UI показывает workspace/cache/recovery bytes и свободное место, даёт выбрать каталог новых рабочих копий. Очистка предлагает только подтверждённые closed/clean candidates и сохраняет dirty, recoverable, claimed/live и reparse paths. Перенос/архив сначала копирует и сверяет manifest, затем удаляет точный исходник. Отдельный full-backup manifest перечисляет реально включённые project/settings/template файлы; восстановленная копия открывается новым процессом и проверяет прежние измерения. Никакой silent cleanup живого проекта.

**Риск/зависимость:** location/retention меняются отдельно от формата пакета; уже существующие рабочие копии не перемещаются автоматически. [Native archive evidence](../../../native-profile-archives227.json), [restore proof](../../../rollback-evidence/ROLLBACK-VERIFICATION.md).

## 5. Ввести общий учёт native render memory

**Польза:** предсказуемая память при длинной работе с тяжёлыми листами и несколькими окнами.

**Основание — подтверждено кодом; оставшаяся утечка не доказана.** Четыре независимых бюджета [RenderCache](../../Controls/PdfViewport.RenderCache.cs) не учитывают все in-flight decode/copy, evicted-but-leased masters, viewport mip/frame и библиотеки. Immutable leases и потерянный repaint уже исправлены. В финальном Westminster private peak 2226.77 MiB против 2178.71 (+2.2%), working-set peak ниже baseline. По этому срезу нельзя объявить ни утечку, ни уже существующий общий лимит процесса.

**Владение:** `PdfViewport.RenderCache.cs`, `PdfViewport.StaticPageFrameCache.cs`, `PdfViewport.RasterSheetBitmapCache.cs`, render policy и focused native tests.

**Приёмка:** единый budget tracker учитывает owned/leased/in-flight bytes без двойного счёта; pressure trim не освобождает живые leases. In-flight allowance ограничен и виден в диагностике. Длинный main+detached цикл измеряет managed/native/process bytes по фазам и после Close; после ухода со страниц память стабилизируется. На тех же трёх повторах Westminster p95 не хуже baseline 358 ms, качество/opacity/количество измерений сохранены. Общий process peak публикуется отдельно от cache bytes.

**Риск/зависимость:** сначала трассировка владельцев; уменьшение кеша может ухудшить перелистывание. Не добавлять принудительный GC как замену владению native memory.

## 6. Убрать полные деревья с горячего UI-пути

**Польза:** поиск, раскрытие и загрузка Takeoffs не блокируют ввод на большом проекте.

**Основание — наблюдалось и подтверждено кодом.** Westminster Takeoffs reload+layout остаётся 3966.91 ms; Pages expand-all 113.14 ms против 95.31 (+18.7%). [PagesTree](../../MainWindow.PagesTree.cs) рекурсивно читает файловую систему и создаёт `TreeViewItem`; [JobLifecycle](../../MainWindow.JobLifecycle.cs) добавляет готовые узлы Takeoffs. [TreeSearch](../../MainWindow.TreeSearch.cs) обходит все UI-узлы на каждый TextChanged. Объявленный Recycling в XAML не устраняет создание этих объектов.

**Владение:** `MainWindow.PagesTree.cs`, `MainWindow.JobLifecycle.cs`, `MainWindow.TreeSearch.cs`, `Models/TreeExpansionState.cs`, новые focused tree models/controllers.

**Приёмка:** filesystem snapshot строится вне UI; поиск отменяет устаревшую работу и применяет последний запрос; viewport/input остаются отзывчивы. Реальные Woodlands/Westminster сохраняют имена, порядок, selection, tracked expansion, linked rows и copy/move/Undo. Три повтора сравнивают reload/search/expand и число созданных visual containers; первый срез должен сократить Takeoffs reload минимум на 25% без ухудшения p95 листа.

**Риск/зависимость:** по одному дереву; не менять одновременно модель selection, правила сортировки и мутации файлов.

## 7. Сохранять только действительно изменённые takeoffs

**Польза:** следующий выигрыш ручного Save без ослабления проверки пакета.

**Основание — подтверждено кодом.** [TrySaveCurrentJobData](../../MainWindow.TakeoffsPersistence.cs) после flush сохраняет весь `_takeoffItems`; [TakeoffStore.SaveMeasurements](../../Models/Storage/TakeoffStore.cs) каждый раз сериализует и атомарно заменяет JSON. После уже выполненной metadata-оптимизации Westminster Save остаётся 8.97 s. Это возможность оптимизации, не ошибка сохранения. `FlushTakeoffAutosaves` сейчас бросает исключение при failure; отсутствие проверки его return не означает потерю ошибки.

**Владение:** `MainWindow.TakeoffsPersistence.cs`, autosave service, `Models/Storage/TakeoffStore.cs`, package dirty/checkpoint ownership.

**Приёмка:** точное dirty-generation отслеживание и no-op Save; изменённые данные сохраняются, unchanged measurements objects не переписываются. Fault injection после записи/до package replace сохраняет dirty state и восстановление. Три реальных Save повтора показывают выигрыш минимум 25% относительно 8.97 s при тех же object hashes/семантике. Старые отсутствующие Joist default-поля не объявлять побайтно неизменёнными после их штатной нормализации.

**Риск/зависимость:** безопасность важнее skip-write; сначала покрыть все mutation sources, включая detached/Undo/scale/Excel sync.

## 8. Завершить управляемый AI workflow

**Польза:** пользователь видит прогресс/отмену, а машинные действия не зависят от удачного извлечения JSON из текста.

**Основание — подтверждено кодом.** [AiRequestActions](../../MainWindow.AiRequestActions.cs) уже передаёт cancellation token и проверяет write access; старое утверждение «все CancellationToken.None» неверно. [BusyOverlay](../../MainWindow.BusyOverlay.cs) не предоставляет обычного Cancel/счётчика. Live [RunAsync](../../Models/OpenAiRequestRunner.cs) не задаёт strict schema; [action drafts](../../Models/SmartContextStore.Requests.cs) и [sheet metadata](../../Models/PdfSheetMetadataService.cs) используют CandidateJsonBlocks. Attachment review/containment уже существуют и сохраняются.

**Владение:** перечисленные файлы, `AiAttachmentPolicy`, AI Inbox/review UI и provider test seam.

**Приёмка:** видимые Cancel и N/M; отмена приводит к terminal cancelled и не применяет поздний ответ к другому job. Strict typed output только для машинно интерпретируемых actions/metadata; свободные заметки сохраняют текстовый workflow. Refusal, incomplete, malformed/unknown schema дают явный review/error, никаких автоматических измерений. Offline fake-provider тесты проверяют cancel/retry/job switch; online запросы выполняются только в согласованной предметной проверке.

**Риск/зависимость:** до новых авто-name/scale функций выполнить пункт 1; не расширять доверие к AI и не заменять human review обещанием точности модели.

## 9. Выделять владельцев состояний по одному workflow

**Польза:** меньше повторных рассинхронизаций между viewport, деревьями, estimating и сохранением.

**Основание — подтверждено структурой, не самостоятельный runtime bug.** На этом source `MainWindow*.cs`: 202 файла / 68,297 физических строк; `PdfViewport*.cs`: 72 / 31,105; MainWindow.xaml 2,713. SimilarCount 1,660, WorkspaceManagers 1,603, PdfViewport.Layers 2,304 строк. Partial splits сами по себе не изолируют общий mutable state. Clipboard, readonly, leases и repaint уже имеют адресные проверки — их не надо переписывать заново.

**Владение первого среза:** `MainWindow.SimilarCount.cs` → focused review coordinator; затем общий takeoff mutation/refresh contract и отдельная viewport interaction state. [Исторический architecture audit](../ARCHITECTURE_AUDIT_AND_REFACTOR_PLAN_2026_05_05.md).

**Приёмка:** один выбранный workflow имеет явные input/state/result, один save/refresh completion и отмену устаревших continuations. До/после одинаковы quantities/page links/Undo/main+detached behavior; источник событий и владелец состояния документированы. Каждый extraction сам собирается и проходит relevant real-project smoke; новые файлы/методы укладываются в AGENTS limits. XAML split и удаление legacy функций — отдельные решения после проверки caller/feature gates.

**Риск/зависимость:** после воспроизводимых gates пункта 3. Никаких массовых механических moves только ради уменьшения одного файла.

## Граница этого аудита

Чтение исходников/артефактов и обновление документации не добавляли app code, не запускали новые тяжёлые тесты, Excel, AI или UI. Внешние отчёты доступны относительными ссылками в текущем рабочем комплекте; при переносе repo их необходимо переносить как evidence bundle либо честно отмечать недоступными. Архивные даты, хеши и ограничения не превращаются в текущую проверку автоматически.
