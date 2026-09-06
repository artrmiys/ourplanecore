# OurPlanCore: выполнение стратегии 2026-09-05

Срез: **2026-09-06, исходный код отдельной 2.2.7-preview проверен; результат установки фиксируется в финальном QA**.

Авторитетный порядок и критерии результата: §10 заметки
`$env:USERPROFILE\Documents\0.Obsidian\wiki\Work\System-Strategy-2026-09-05.md`.
Детальные обязательства приложения остаются в
[действующем master plan](OURPLANCORE_MASTER_REMEDIATION_AND_RELEASE_PLAN.md).
Эта страница связывает проверенные результаты с пакетами стратегии; она не
создаёт вторую очередь и не объявляет будущие фазы master plan завершёнными.

Здесь рассматривается только приложение: P02, P03, проверка security перед P05,
P05, P06 и app-часть P08. P01/P04/P07/P09, Pay, E-Wood, облачное резервирование,
перенос файлов и изменения памяти этой работой не закрываются. Настраиваемые
горячие клавиши — дополнительный пользовательский запрос текущего кандидата.

## Версии и маршруты

| Роль | Подтверждённое состояние |
|---|---|
| Стабильная установленная версия | `2.2.5+5d46e11`; прежние `Desktop\updates\OurPlanCore` и `OurPlanCore.lnk` сохранены. |
| Предыдущий проверенный Preview | `2.2.6-preview+3f290bd`; отдельный `Desktop\OurPlanCore Preview 2.2.6`, его действующий процесс и профиль сохраняются. [Поведение и ограничения](DATA_SAFETY_PREVIEW_2026_09_06.md), [QA](../../QA-REPORT.md), [identity](../../delivery.json). |
| Текущий source | Корень этого checkout (`../` относительно данной страницы); изменения после `3f290bd` находятся в отдельной рабочей версии. Сборка, диагностика и изменения ведутся здесь, а не через старый Desktop source-путь. |
| Целевой выпуск | **2.2.7-preview** в новой versioned-папке и отдельном профиле/ярлыке. Версия исходников 2.2.7-preview. Окончательный commit, compressed publish, SHA, ярлык и проверка установленного EXE фиксируются в `../../delivery-227.json` и `../../QA-REPORT-227.md`; эта страница сама по себе не подтверждает установку. |
| Платформенный эксперимент | Отдельный `../../platform-net10`, commit `69031529`; отдельная установленная `2.2.6-net10-preview`. Он не заменяет основной .NET 9 candidate. |
| Доказательства | Каталог `../../` рядом с `source`: локальные журналы, JSON и снимки. Эти материалы не являются публичными release assets. |

Для текущей задачи явное указание пользователя сохранять старую программу имеет
приоритет над обычными release-командами master plan/AGENTS. Не заменять старые
updates, ярлык, ассоциации файлов или живую 2.2.6. Не создавать public release.
Оба Excel-шаблона для нового пакета копировать из текущей сохранённой установленной
папки; исторический путь шаблона в baseline старого master plan не выбирать заново.

## Матрица пакетов

| Пакет | Статус на момент среза | Код / подтверждение | Незакрытая приёмка |
|---|---|---|---|
| **P02, только OurPlanCore** | **PASS: откат проверен запуском** | [Отчёт восстановления](../../rollback-evidence/ROLLBACK-VERIFICATION.md), [результат](../../rollback-evidence/rollback-result.json): три файла из первоначального бекапа совпали с манифестом; восстановленная 2.2.5 открыла копию реального проекта 214 листов / 371 позиция / 3589 измерений, сохранилась и завершилась exit 0. Все 371 объекта измерений побайтно прежние; исходный проект, настройки, ярлык и updates сохранили SHA256. | Общесистемный P02, восстановление Pay и внешнее backup-хранилище этим не проверены. Этот запуск не является сравнением скорости. |
| **P03a, защита данных** | **Интегрированный source PASS** | [DataFileReader](../Models/Storage/DataFileReader.cs), [DataSafetyTests](../Tests/DataSafetyTests.cs), [WPF smoke](../Tests/DataSafetyUiSmokeHarness.cs). Общий набор 2.2.7: **803/803 C#**; Python **29/29**. | Повторная проверка готового EXE, реальная защита повреждённых данных и bulk-операции фиксируются в `../../bench/native-227-westminster-render-final/data-safety.json` и финальном QA. |
| **P03b, отдельная .NET 10 копия** | **Эксперимент выполнен; migration gate НЕ пройден** | [Матрица совместимости](../../platform-evidence/PLATFORM-COMPATIBILITY.md), [JSON](../../platform-evidence/compatibility-result.json): build 0/0, 791/791 C#, 29 Python, PDF/Skia, реальный OCR из установленного bundle, WPF, compressed publish и отдельный EXE на реальном проекте PASS. | Полный Excel smoke не проходит: A3 одинаково отказывает на исходном .NET 9 и .NET 10; structural smoke также отказал. VBA-процедуры присутствуют, причина не установлена. Основной runtime не мигрировал; повторить Excel gates после отдельного исправления причины. |
| **Security перед P05** | **Повторный общий набор PASS** | [SafeJobPathResolver](../Models/SafeJobPathResolver.cs), [DataSafetyTests](../Tests/DataSafetyTests.cs), [JobOperationJournal](../Models/Storage/JobOperationJournal.cs): containment, unsafe IDs/device names, junctions, AI attachments и recovery. Проверки вошли в 803/803. | Оптимизация metadata не удаляет проверки разрешения путей и границ проекта. Read-only барьер общей вставки также проверен в P06. |
| **P05, реальная скорость и качество** | **12 последовательных сравнений PASS** | [Harness](../Tests/RealProjectPerformanceHarness.cs) и [операции](../Tests/RealProjectPerformanceHarness.Operations.cs): 3 повтора каждого варианта для Woodlands (84/17/168) и Westminster (214/371/3589). Причина Save: повторные filesystem probes на каждом JSON-свойстве; [per-file metadata context](../Models/OurPlanPackagePortability.Metadata.cs) сохраняет проверки путей. [Аудит](../../SAVE-PATH-AUDIT.md). Зум 400% реального фрагмента: после остановки ранее менялись 156751/770000 пикселей, теперь 0; [production test](../Tests/ViewportZoomSamplingTests.cs), `../../zoom-baseline-real` и `../../zoom-fixed-real`. | Строгая сверка данных, медианы, PDF и ограничения: `../../PERFORMANCE-COMPARISON-227-FINAL.md/json`. ColdJobWindowOpenToPaint исключает процесс/self-extraction; memory peak измеряется до Close. Native 214-листовой прогон фиксируется отдельно в финальном QA. |
| **P06, единая вставка и небольшой UI** | **WPF + отдельный reopen PASS; итоговый выпуск pending** | [Общий отчёт](../../P06-CLIPBOARD-REPORT.md), [common paste](../MainWindow.MeasurementClipboard.cs), [Undo](../MainWindow.MeasurementClipboard.Undo.cs), [диалог трёх действий](../Dialogs/MeasurementPasteModeDialog.cs), [реальный WPF harness](../Tests/MeasurementClipboardUiSmokeHarness.cs). На копии большого проекта воспроизведены read-only mutation, пустые новые takeoffs после Undo и остатки в detached viewport; [candidate2](../../archived-ui-runs/68fce756bcbc44e788e6d4f5ef512235/report.json) прошёл 17 проверок. [Новый процесс](../../archived-ui-runs/736f142674044952aa18cb2e834cfc02/report.json) открыл сохранённый пакет и подтвердил точное сохранение 371 позиции / 3589 измерений; оба процесса штатно завершились. | Доказательства перенесены в архив E: с проверкой каждого SHA256; [манифесты и удаление только двух временных C-копий](../../p06-final-archive-result.json). Финальный повтор на 2.2.7 также прошёл: 17/17, затем отдельный процесс reopen с точными 371/3589; оба exit 0. Логи `../../clipboard-final227.log` и журнал финального reopen рядом с ним. Установленный пакет проверяется отдельно. Реализованный common paste не дублируется новым глобальным coordinator. |
| **Горячие клавиши** | **Реальные UI и сохранение PASS** | **8 Settings → Keyboard Shortcuts → Open Keyboard Shortcuts...**: defaults сохранены, поиск/категории, assign/remove/reset, конфликты, global/job, import/export и выбор команды с UI. [Инструкция и доказательства](../../artifacts/shortcuts/README.md), [редактор](../Dialogs/KeyboardShortcutSettingsDialog.cs), [store](../Models/KeyboardShortcutStore.cs), [WPF harness](../Tests/CustomShortcutUiSmokeHarness.cs). Каталог Westminster: 613 команд; main/detached mirror/Undo, typing/focus/modal, locked/corrupt recovery и Save/reload прошли. | Тестовые F10/F11/F12 остаются в изолированном тестовом профиле. Последующий ScrollIntoView выбранной строки скомпилирован 0/0; визуальная приёмка установленного редактора входит в финальный QA. |
| **P08, только инструкции приложения** | **Инструкции обновлены** | [AGENTS](../AGENTS.md) указывает действующий console harness и приоритет отдельного Preview. Эта страница связывает strategy → app master → source/evidence. | Общая очистка Desktop/KB и будущие master-фазы не выполнялись. Окончательную доставку подтверждает внешний QA с точной версией, SHA, PID и свежим логом. |

## Повторение проверок

Из текущего source, без параллельных сборок в общие `bin/obj`:

```powershell
dotnet build .\ourplancore.sln
dotnet run --project .\Tests\OurPlanCore.Tests.csproj --no-build
dotnet run --project .\Tests\OurPlanCore.Tests.csproj --no-build -- data-safety-ui-smoke
dotnet run --project .\Tests\OurPlanCore.Tests.csproj --no-build -- feedback-ui-smoke
```

Реальные команды `real-work-perf`, `clipboard-ui-smoke` и `shortcut-ui-smoke`
зарегистрированы в [Tests/Program.cs](../Tests/Program.cs); аргументы и создание
изолированных копий задаёт соответствующий harness. Перед запуском проверить его
usage и выбрать новый evidence-каталог. Запуски UI и измерения производительности
координировать, чтобы чужая нагрузка не стала частью сравнения.

Финальное доказательство доставки 2.2.7 должно связать commit, product version,
SHA256 publish/installed, новый shortcut target/working directory, отдельный
profile, точный PID/путь процесса и свежий runtime log после его startup.
Зелёный console harness не заменяет проверку готового EXE на реальной работе.

Незакрытые master Phase 4–8 сохраняют свои прежние критерии. В частности,
настройки правил, CI/test discovery, общий memory budget, data-bound trees,
архитектурное разбиение и полная accessibility-приёмка не становятся выполненными
от одного узкого исправления или нового редактора горячих клавиш.

Проверки текущей интеграции: `../../render-final-ack-227-full-tests.log` — 803/803; `../../integration-227-python-tests.log` — 29/29; `../../render-final-ack-227-build.log` — 0 warnings / 0 errors.

## Дополнительные исправления, подтверждённые финальными проверками

При приёмке 3e68340 выявлены редкие задержки показа и всплески памяти на большом
проекте. Исходная сравнительная таблица сохранена отдельно; она не подменяет
новую `PERFORMANCE-COMPARISON-227-FINAL.md/json` после исправлений.

[Кеш растров](../Controls/PdfViewport.RenderCache.cs) теперь помечает свои
неизменяемые master/lease bitmaps как immutable. Это устраняет копирование целого
листа внутри SKImage.FromBitmap при отрисовке. Исходный bitmap вызывающей стороны
и перерисовываемый static frame не замораживаются. [Native pointer/lifetime proof](../../bitmap-lease-proof/README.md):
обе проверки на старом коде FAIL, после исправления 2/2 PASS; изображения
побайтно одинаковы для None/Low/Medium, eviction освобождает пиксели только после
последнего image/lease. Один диагностический прогон подтвердил ускорение pan,
но сам по себе не доказал снижение общего пика памяти.

[Очередь repaint](../Controls/PdfViewport.cs) сохраняет запрос, пришедший после
фактического paint, но до сброса флага очереди. Частые запросы по-прежнему
объединяются; пропущенный запрос вызывает один завершающий repaint.
[WPF proof](../../REPAINT-SCHEDULING-DIAGNOSTIC.md): старый DLL 10/10 FAIL,
новый 4/4 сценария и 10/10 повторов PASS. Общая проверка после обоих изменений —
803/803; источник логов указан выше. Окончательное влияние на соседние сценарии
и память проверять по финальному сравнению и QA, а не по одному контрольному запуску.
Начало OnPaintSurface подтверждает обработку уже накопленных запросов: 100 запросов дают ровно один кадр. Запрос после начала paint сохраняет завершающий кадр. Отдельная проверка RenderTargetBitmap между двумя изменениями сохраняет последний цвет; все четыре WPF-сценария и 10 повторов гонки PASS.

## Отдельный профиль сжатого EXE

При обычном запуске ae2a8ee через ярлык обнаружен неверный поиск marker в каталоге распаковки bundle. Resolver AppIdentity теперь предпочитает marker рядом с фактическим Environment.ProcessPath и сохраняет fallback для dotnet-host. Проверки текущей интеграции: ../../profile-final-227-build.log — 0 warnings / 0 errors; ../../profile-final-227-full-tests.log — 807/807. Код рендера, сохранения и shortcuts относительно ae2a8ee не менялся. Прежние performance/native 214 доказательства сохраняют точную identity ae2a8ee; новый обычный запуск через ярлык, правильный профиль и установленный редактор фиксируются во внешнем QA и runtime-227.json после публикации.
