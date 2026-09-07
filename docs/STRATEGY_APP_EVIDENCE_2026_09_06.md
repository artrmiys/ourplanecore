# OurPlanCore: стратегия → код → проверенные результаты

Срез: **2026-09-06, поставлена отдельная 2.2.7-preview / 42c44b0 / .NET 9**.
Это итоговая матрица приложения по §10 пользовательской стратегии от 2026-09-05.
Она не закрывает общесистемные P01/P04/P07/P09, Pay, E-Wood, облачный backup
или будущие фазы [master plan](OURPLANCORE_MASTER_REMEDIATION_AND_RELEASE_PLAN.md).
Настраиваемые shortcuts включены по отдельному пользовательскому запросу.

[Текущий handoff](PROJECT_CONTEXT.md) описывает устройство и ограничения;
[следующие девять улучшений](70-architecture-refactor/IMPROVEMENT_PLAN_2026_09_06.md)
задают дальнейшую очередь. Все пути ниже относительны к текущему рабочему комплекту;
внешние evidence-файлы не являются публичными release assets.

## Точная identity доказательств

| Объект проверки | Версия / результат | Граница доказательства |
|---|---|---|
| Финальный установленный EXE | `42c44b057eb5b9d22c4919fc94c840632964155b`, ProductVersion 2.2.7-preview; SHA256 `94DF767F5D51C07D7A603749B50C97F98384E0024EA6692913128D6ED7821A41`. [Delivery](../../delivery-227.json), [QA](../../QA-REPORT-227.md). | Отдельные folder/shortcut/profile. Stable 2.2.5, его updates/ярлык и исходный checkout не заменены. |
| Финальная .NET 9 интеграция | [Build](../../profile-final-227-build.log): 0 warnings / 0 errors; [C#](../../profile-final-227-full-tests.log): **807/807**, [процесс](../../profile-final-227-test-process.json): exit 0. [Python](../../integration-227-python-tests.log): **29/29**. | Четыре последних C# теста проверяют profile marker. Python helpers после их проверки не изменялись. Не складывать результаты разных запусков. |
| Финальный render-код до profile fix | `ae2a8ee`; [native all-pages verification](../../bench/native-227-westminster-render-final/verification.json): **214/214 листов**, 371 takeoff, 3589 measurements, Passed, exit 0. EXE SHA256 `BF97990EA67214958B6E175DBCD5110615FA5DD6EB8A4BC3475B8CF013CBE620`. | Это предыдущий EXE, не 42c. Render/save/shortcuts после него не менялись; 42c меняет marker resolver и его тесты. Native startup → confirmed paint: 24.06 s. |
| Обычный финальный запуск через .lnk | [Fresh runtime](../../runtime-227.json): Passed, LaunchedViaShortcut, правильный отдельный профиль 2.2.7, Westminster/371 takeoff, FreshErrors=0 после нового startup; [completion](../../final-delivery-completion-227.json). | Исторический PID 19576 оставлен пользователю; для него не заявляется exit 0 и не предполагается, что PID всё ещё существует. Этот запуск доказывает настоящий compressed EXE без test-profile override. |
| Отдельная .NET 10 копия | `69031529`, SDK 10.0.400/runtime 10.0.11; build 0/0, 791/791 C#, 29 Python, PDF/Skia/OCR/WPF/native PASS. | [Compatibility gate](../../platform-evidence/compatibility-result.json): **MigrationGatePassed=false**, StableRuntimeMigrated=false из-за полного Excel smoke. 807 тестов на этот spike не переносить. |
| Сохранённый baseline | 2.2.6-preview / `3f290bd`; [его QA](../../QA-REPORT.md), [data-safety scope](DATA_SAFETY_PREVIEW_2026_09_06.md). | База сравнения производительности. Старый пользовательский процесс не закрывался. Промежуточный marker launch затронул старый Preview profile; это исключает утверждение «весь старый профиль неизменён». |

## Матрица выполненных app-пакетов

| Пакет | Подтверждённый результат | Оставшееся ограничение |
|---|---|---|
| **P02: OurPlanCore backup/rollback** | [Restore report](../../rollback-evidence/ROLLBACK-VERIFICATION.md): файлы первоначального backup совпали с manifest; восстановленная 2.2.5 открыла копию Westminster 214/371/3589, сохранила её и завершилась exit 0. Исходный проект, settings, shortcut и updates проверены SHA256 на момент этого restore. | Это проверка отката приложения, не всех систем и не внешнего облачного хранилища. Metadata snapshots не заменяют полный backup. |
| **P03a: data safety** | [Typed readers](../Models/Storage/DataFileReader.cs), [tests](../Tests/DataSafetyTests.cs), [native safety result](../../bench/native-227-westminster-render-final/data-safety.json). Текущие регрессии входят в 807/807; native safety proof относится к ae2a8ee. | General AppSettings/SettingsPresetStore protection остаётся Phase 4. Защиту нельзя считать общей только по одному reader или зелёному suite. |
| **P03b: .NET 10 compatibility** | [Матрица](../../platform-evidence/PLATFORM-COMPATIBILITY.md): эксперимент выполнен на отдельной копии, включая реальный OCR из bundle и полный native проект. | **Миграция заблокирована Excel**: A3 одинаково отказывает на .NET 9, .NET 10 и прямом COM; structural smoke тоже отказал. Процедуры существуют, причина не установлена. Это не доказанная регрессия .NET 10. |
| **Security перед P05** | [Safe paths](../Models/SafeJobPathResolver.cs), [operation journal](../Models/Storage/JobOperationJournal.cs), containment/device-name/junction/AI-attachment/recovery cases входят в общий C# suite. | Metadata optimization сохраняет разрешение и reparse checks. Read-only барьер common paste проверен отдельно в P06. |
| **P05: скорость и качество на реальной работе** | [Финальное сравнение](../../PERFORMANCE-COMPARISON-227-FINAL.md): 12 завершённых последовательных запусков, по 3 на версию и проект. Woodlands 84/17/168; Westminster 214/371/3589. Сохранение, страницы, деревья, opacity и экспорт проверены. Native отдельный запуск прошёл все 214 листов. | Детальные границы и неоднородные показатели ниже. Нельзя писать «всё быстрее» или «память больше не растёт». |
| **P06: paste, Undo, main/detached** | [Отчёт](../../P06-CLIPBOARD-REPORT.md), [финальный повтор P06](../../clipboard-final227-proof.json): 17/17 UI, затем новый процесс открыл сохранённый реальный пакет с точными 371/3589; оба exit 0. [Common paste](../MainWindow.MeasurementClipboard.cs), [Undo](../MainWindow.MeasurementClipboard.Undo.cs), [Same/New/Cancel dialog](../Dialogs/MeasurementPasteModeDialog.cs). | Этот P06 UI повтор выполнен на более ранней интеграции 2.2.7 до последних render/profile правок; его binary identity сохранена в evidence. Это не повтор 17 UI кейсов на 42c. Позднейшие изменения не переписывали clipboard workflow. |
| **Пользовательские shortcuts** | Main/detached, typing/focus/modal, locked/corrupt recovery, assignment/conflicts, global/job, import/export, Save/reload проверены. [Полный evidence](../../artifacts/shortcuts/README.md). Финальный установленный редактор 42c показан через реальный shortcut: 605 отображённых команд, 0 overrides, выбранная строка видна. | Ранее 613 команд/25 категорий были измерены в другой UI-конфигурации; динамический каталог не обязан иметь один размер. Это не полная accessibility-приёмка. |
| **P08: инструкции приложения** | Обновлены [AGENTS](../AGENTS.md), текущий handoff, исторические пометки и единая очередь улучшений. | Документационный аудит не реализует Phase 4–8 и не меняет приложение. Облачная/общесистемная часть стратегии отдельно. |

## P05: что ускорено и что осталось

[Harness](../Tests/RealProjectPerformanceHarness.cs) использует заполненные страницы
и сохранённые данные, не пустой проект. Сравнение baseline 3f290bd с финальным
render-кодом включает одинаковые настройки и по три завершённых запуска.
Свежий процесс/профиль не означает очищенный файловый кеш ОС.

| Медиана или указанный агрегат | Westminster baseline → final | Woodlands baseline → final |
|---|---:|---:|
| Job-window open → first paint | 72.98 → 18.06 s | 12.20 → 8.52 s |
| Paint median / p95 | 239.5 / 358 → 244.5 / 330.5 ms | 198 / 213.75 → 193 / 207 ms |
| Takeoffs reload + layout | 4059.79 → 3966.91 ms | 1120.35 → 1121.95 ms |
| Pages expand-all | 95.31 → 113.14 ms | 82.16 → 80.32 ms |
| Manual Save | 128.04 → 8.97 s | 8.15 → 1.15 s |
| Normal Close | 120.64 → 2.73 s | 7.79 → 0.83 s |
| Sampled private peak | 2178.71 → 2226.77 MiB | 1728.76 → 1687.64 MiB |

Save/Close во всех трёх candidate-запусках быстрее любого соответствующего baseline.
Для Westminster median paint +2.1%, Pages expand-all +18.7%, private peak +2.2%;
sampled working set и OS working-set peak ниже baseline. Это измеренные
ограничения, не диагноз утечки. Общий memory budget остаётся будущей работой.

Open-метрика test host исключает process startup и self-extraction; native
24.06 s указаны отдельно. Память опрашивалась каждые 100 ms до Close, поэтому
пик самого Close не измерен. Paint: 36 наблюдений Westminster и 6 Woodlands.
PDF проверяет видимые measurements: Westminster 583 видимых из 802 выбранных,
Woodlands 168/168. Это не экспорт всех 3589 измерений Westminster.

Сохранённые объекты baseline/candidate совпали. В старом Westminster оба варианта
добавляют три ранее отсутствующих Joist default-поля на 370 объектах; прежние
значения и порядок сохранены. Поэтому не заявляется побайтная неизменность
всего исходного старого ZIP. Hidden measurements остаются сохранёнными.

Исправления имеют отдельные воспроизведения:

- [Per-file metadata context](../Models/OurPlanPackagePortability.Metadata.cs)
  убирает повторную классификацию файла на каждом JSON-свойстве.
  [Стек и аудит Save](../../SAVE-PATH-AUDIT.md); real path checks сохранены.
- [Immutable bitmap leases](../Controls/PdfViewport.RenderCache.cs):
  [native pointer/lifetime proof](../../bitmap-lease-proof/README.md) — старые
  cache paths 2/2 FAIL, новые 2/2 PASS; None/Low/Medium совпадают побайтно,
  eviction сохраняет пиксели до последнего image/lease. Один диагностический
  run улучшил pan, но отдельно не доказал снижение общего private peak.
- [Repaint scheduling](../Controls/PdfViewport.cs) и acknowledgment в
  [OnPaintSurface](../Controls/PdfViewport.Rendering.cs):
  [WPF diagnostic](../../REPAINT-SCHEDULING-DIAGNOSTIC.md) — потерянный trailing
  request на старом DLL 10/10 FAIL; итоговые 4 сценария и 10 повторов PASS.
  100 запросов до paint дают ровно один кадр; RenderTargetBitmap между
  изменениями не теряет последний цвет. Тогдашний suite 803/803 относится
  к render-интеграции; текущие 807/807 включают ещё четыре profile tests.
- Зум 400% реального фрагмента: после остановки прежде менялись
  156751/770000 пикселей, после sampling fix — 0.
  [Production test](../Tests/ViewportZoomSamplingTests.cs);
  отдельные исходные изображения находятся в evidence `zoom-baseline-real`
  и `zoom-fixed-real`.

## Compressed EXE и отдельный профиль

При первом обычном запуске ae2a8ee marker искался в bundle-каталоге, потому что
IncludeAllContentForSelfExtract меняет AppContext.BaseDirectory. Запуск затронул
старый Preview profile; QA фиксирует проверенные последствия, а не обещает его
полную неизменность. [AppIdentity](../Models/AppIdentity.cs) теперь предпочитает
marker рядом с реальным Environment.ProcessPath и сохраняет fallback для
dotnet host. [Четыре regression tests](../Tests/AppIdentityPreviewProfileTests.cs)
вошли в текущие 807/807.

Первый фиксированный запуск был остановлен free-space guard. Только завершённые
тестовые native-профили были скопированы в архив, каждый файл сверен SHA256,
затем точные временные исходники удалены:
[archive manifest](../../native-profile-archives227.json). После этого финальный
обычный .lnk запуск 42c прошёл с правильным профилем и свежим логом без ошибок.
Старые updates и пользовательские процессы не заменялись и не закрывались.

## Повторение проверок

Из текущего source, последовательно и в новых изолированных профилях:

```powershell
dotnet build .\ourplancore.sln -c Release
dotnet run --project .\Tests\OurPlanCore.Tests.csproj -c Release --no-build
```

Это console harness, не стандартный `dotnet test` discovery. Режимы
`real-work-perf`, `clipboard-ui-smoke`, `shortcut-ui-smoke`,
`data-safety-ui-smoke` зарегистрированы в [Program](../Tests/Program.cs).
Перед запуском прочитать usage конкретного harness: команды создают копии,
нуждаются в месте на диске и реальных исходных данных. Не запускать параллельно
общие builds/UI/performance и не использовать пользовательский живой профиль.

Зелёные тесты не заменяют hash/shortcut/profile/fresh-log проверку установленного
compressed EXE. Финальная доставка уже подтверждена ссылками выше; следующий
релиз должен получить собственную identity и свежую проверку.
