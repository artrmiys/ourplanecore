# OurPlanCore — текущий технический контекст

Срез кода: **2026-09-06, 42c44b0, 2.2.7-preview, .NET 9/x64/WPF**.
Это текущий handoff. Майские–июльские журналы описывают историю и не задают
нынешнюю сборку, пути поставки или очередь работ.

## Что читать сначала

1. [AGENTS](../AGENTS.md) — ограничения и правила работы.
2. [Strategy → source → evidence](STRATEGY_APP_EVIDENCE_2026_09_06.md) — что реально проверено.
3. [Следующие девять улучшений](70-architecture-refactor/IMPROVEMENT_PLAN_2026_09_06.md) — порядок, владельцы и приёмка.
4. [Master remediation plan](OURPLANCORE_MASTER_REMEDIATION_AND_RELEASE_PLAN.md) — полные фазы и сохранённые исторические milestones.
5. [Итоговый QA](../../QA-REPORT-227.md) — пакет, ярлык, профиль, UI, ограничения.

## Текущая поставка и разрешённая область

2.2.7 поставлена отдельным Preview, с собственным ярлыком и профилем.
Стабильный updates, его ярлык и исходный рабочий checkout не заменялись.
Во время приёмки сохранены старый пользовательский Preview 2.2.6 и его процесс.
Эти исключения имеют приоритет над общими историческими командами обновления.

Финальный EXE: commit `42c44b057eb5b9d22c4919fc94c840632964155b`,
SHA256 `94DF767F5D51C07D7A603749B50C97F98384E0024EA6692913128D6ED7821A41`.
[Обычный запуск](../../runtime-227.json) подтверждает настоящий новый .lnk,
отдельный профиль, загрузку Westminster/371 takeoff и FreshErrors=0 после
нового Application startup. PID 19576 был оставлен пользователю; это исторический
launch proof, а не обещание, что этот PID существует при чтении документа.
Для оставленного приложения не заявляется exit 0.

Первый обычный запуск обнаружил ошибку profile marker у self-extracting EXE:
AppContext.BaseDirectory указывал в bundle. Она исправлена в
[AppIdentity](../Models/AppIdentity.cs): marker рядом с фактическим process EXE
имеет приоритет; dotnet host использует app-directory fallback. Четыре
[проверки выбора профиля](../Tests/AppIdentityPreviewProfileTests.cs) входят в 807.
Не скрывать последствия первоначального отказа: в прежний preview profile были
добавлены лог/workspace и обновлён RecentJobs timestamp. Исходный пакет и прежние
настройки проверены отдельно; см. QA. Слепой откат живого профиля не выполнялся.

## Проверки и точная применимость

| Срез | Выполнено | Граница |
|---|---|---|
| Текущий source 42c44b0 | Release 0 warnings/0 errors; 807/807 C#; отдельный процесс exit 0 | [build](../../profile-final-227-build.log), [tests](../../profile-final-227-full-tests.log), [process/DLL hash](../../profile-final-227-test-process.json) |
| Python helpers | 29/29 | [лог](../../integration-227-python-tests.log); helpers после этого не менялись |
| Render candidate ae2a8ee | 12 завершённых сравнений: 3 × 2 версии × 2 реальных проекта; native 214/214 + data safety | [сравнение](../../PERFORMANCE-COMPARISON-227-FINAL.md), [native verification](../../bench/native-227-westminster-render-final/verification.json); не приписывать этот EXE срезу 42c |
| P06 clipboard | 17 WPF сценариев + отдельный reopen 371/3589, оба exit 0 | [proof](../../clipboard-final227-proof.json); более ранняя assembly до последних cache/repaint/profile правок |
| Hotkeys | main/detached/focus/modal/readonly/store UI smoke; установленный editor 42c отдельно | [инструкция и evidence](../../artifacts/shortcuts/README.md); не путать 613 исторических команд с 605 в финальном наблюдённом editor |
| .NET 10 spike | 791 C# / 29 Python, native/PDF/Skia/OCR/пакет PASS | [gate](../../platform-evidence/compatibility-result.json): migration=false, полный Excel FAIL; основной runtime не переносился |

Основной harness — console executable, не test-adapter discovery:
`dotnet build .\ourplancore.sln`, затем
`dotnet run --project .\Tests\OurPlanCore.Tests.csproj --no-build`.
Сначала выбрать профиль, evidence-каталог и проверить usage соответствующего
режима в [Program](../Tests/Program.cs). Не запускать параллельно WPF/performance
прогоны или сборки в общие bin/obj. Синтетический regression test полезен для
гонки/ошибки, но не доказывает скорость или качество на реальном проекте.

## Реальные результаты производительности

Изолированные копии: Woodlands 84 листа/17 takeoffs/168 measurements;
Westminster 214/371/3589. Baseline 3f290bd; одинаковые настройки и runtime 9.0.3.

| Westminster, медиана 3 запусков | Baseline | Финальный render candidate |
|---|---:|---:|
| Окно/job до первого кадра |72.98 s|18.06 s|
| Заполненный лист median / p95 |239.5 /358 ms|244.5 /330.5 ms|
| Takeoffs reload+layout |4059.79 ms|3966.91 ms|
| Ручной Save с пакетом |128.04 s|8.97 s|
| Штатный Close |120.64 s|2.73 s|
| Sampled private peak до Close |2178.71 MiB|2226.77 MiB|

Улучшение Save/Close устойчиво во всех трёх повторах. Медиана листа +2.1%,
Pages expand-all +18.7% и private peak +2.2% остаются видимыми ограничениями.
ColdJobWindowOpenToPaint не включает запуск процесса/self-extraction; sampled
peak снимается до Close. Woodlands p95 основан только на 6 наблюдениях.
Старые candidate/REPAINT-DIRTY/diagnostic runs сохранены отдельно и не входят
в финальные медианы. Детали качества/видимости PDF и нормализации Joist defaults
указаны в полном сравнении; общий PASS не означает «все операции быстрее».

## Хранение и восстановление

- Основной переносимый формат — `.ourplan`; legacy folders поддерживаются
  отдельно. [Workspace](../Models/OurPlanPackageWorkspace.cs) проверяет пакет,
  получает claim/lease и открывает управляемую рабочую копию.
- Распаковка и кеши лежат под `AppIdentity.LocalRoot`; выбранный marker/profile
  важен для изоляции. Free-space guard уже существует. Clean closed workspace
  pruning также существует: default 90 days, минимум 7.
- [Typed readers](../Models/Storage/DataFileReader.cs), write access, safe paths,
  operation journal и durable writes защищают критические операции. Не обходить
  их при ускорении и не трактовать Invalid как пустой takeoff.
- [Recovery snapshots](../Models/JobRecoveryService.cs) — metadata Pages/Takeoffs,
  до 20 копий. Это не полный backup PDF/AI/Excel/settings.
  [Package selector](../Models/OurPlanPackageFileSelector.cs) исключает
  `.snapshots` и `.undo`; переносимый пакет не переносит всю историю восстановления.
- Полный внешний backup проверяется manifest и restore-run отдельно:
  [проверка восстановления](../../rollback-evidence/ROLLBACK-VERIFICATION.md).
  [Архивация завершённых native-профилей](../../native-profile-archives227.json)
  сохранила 18,918 файлов / 5.65 GB перед удалением точных временных исходников.

## Владельцы основных потоков

| Область | Точки входа / владельцы |
|---|---|
| Open/save/package/lease | `MainWindow.ProjectPackage*.cs`, `MainWindow.JobLifecycle.cs`, `MainWindow.JobAccess.cs`, `MainWindow.JobRecovery.cs`, `MainWindow.TakeoffsPersistence.cs`, `Models/OurPlanPackage*.cs`, `Models/Storage/` |
| Main/detached measurements | `MainWindow.MeasurementCallbacks.cs`, `MainWindow.DetachedSheets.cs`, `Controls/PdfViewport.*.cs` |
| Paste и Undo | `MainWindow.MeasurementClipboard.cs`, `MainWindow.MeasurementClipboard.Undo.cs`, `Controls/PdfViewport.Undo.cs` |
| Render/cache/paint scheduling | `PdfViewport.Rendering.cs`, `PdfViewport.RenderCache.cs`, `PdfViewport.Layers.cs`, `PdfViewport.cs` в Controls |
| Pages/Takeoffs/search | `MainWindow.PagesTree.cs`, `MainWindow.JobLifecycle.cs`, `MainWindow.TreeSearch.cs`, `MainWindow.Takeoffs*.cs` |
| Rules/settings | `SettingsPresetStore`, `AppSettingsStore`, `MainWindow.SettingsManager.*.cs` |
| Keyboard commands | `MainWindow.CustomShortcuts*.cs`, `Models/KeyboardShortcut*.cs`, `Dialogs/KeyboardShortcutSettingsDialog.cs` |
| AI request/review | `MainWindow.AiRequestActions.cs`, `OpenAiRequestRunner`, `SmartContextStore.Requests`, `AiAttachmentPolicy` |
| Excel | `ExcelMacroTakeoffExportService`, `ExcelWorkbookSelectionPolicy`, `ExcelFramingExportService`, реальные macro harnesses |

Список является картой владения, а не разрешением менять все partials. Перед
правкой уточнить реальных callers и распределение файлов между агентами.

## Что осталось открытым

[Actionable plan](70-architecture-refactor/IMPROVEMENT_PLAN_2026_09_06.md)
определяет следующие задачи. Приоритет — protected settings и Excel gate,
затем воспроизводимая поставка, workspace/backup capacity, общий native budget,
деревья, dirty Save, управляемый AI и один архитектурный workflow за раз.

Подтверждённое отличие от старых документов: AI уже использует cancellation
token и отменяет работу при потере write access; обычный Cancel/progress UI и
строгие schemas машинных ответов ещё не завершены. Autosave flush уже бросает
ошибку при failure. Эти сделанные защиты нельзя повторно заносить как отсутствующие.

Физические размеры текущего кода: MainWindow: 202 файла / 68,297 строк,
PdfViewport: 72 / 31,105; MainWindow.xaml: 2,713. Маленький MainWindow.xaml.cs не означает
маленькое общее состояние. [Майский audit](ARCHITECTURE_AUDIT_AND_REFACTOR_PLAN_2026_05_05.md)
и [июльская стратегия](STRATEGY_2026.md) сохранены как исторические оценки.

Внешние evidence-ссылки `../../` относятся к комплекту этой работы, а не к
публичному portable repo. При передаче комплект переносится вместе с отчётами
или ссылки явно отмечаются как unavailable; локальные пути не превращать
в общий runtime/configuration default.
