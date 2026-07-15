# Sheet Metadata Precise v2 Handoff — 2026-07-14

## Результат

Система Auto Name, Auto Scale и суффиксов переведена на единый проверяемый
контракт `Precise v2`. Старый алгоритм сохранён без изменений как встроенный
`Legacy` preset и остаётся безопасным fallback. Улучшенный режим включается
глобальным preset-файлом, а отдельный job может иметь собственный override.

Основной пользовательский путь:

1. Выделить один лист, несколько листов или папку Pages.
2. Нажать `Name`, `Scale` или `Name + Scale`.
3. Проверить строки Preview.
4. Для масштаба явно выбрать `Keep`, `Set` или `Clear`.
5. Только подтверждённые строки меняют page folder, scale и `source_pdf.json`.

Отмена Preview не записывает промежуточную metadata и не меняет job.

## Settings и разрешение конфигурации

Категория `Settings > Auto Rename / Scale` управляет:

- detector mode: `Legacy` или `PreciseV2`;
- import policy: analyze, preview, high-confidence auto apply или legacy apply;
- минимальной confidence отдельно для name, suffix и scale;
- сохранением ручного имени, суффикса и масштаба;
- Drawing List, title-block label/title/scale и body evidence;
- scale-capable, no-scale, terminal и compound suffix lists;
- приоритетными typed suffix rules;
- точными override по PDF pattern + sheet label.

Разрешение preset:

```text
<job>/AI_Context/settings/sheet_metadata.json
    -> global presets/sheet_metadata.json
    -> SheetMetadataConfig.BuildDefault() (Legacy)
```

`BuildDefault()` намеренно воспроизводит прежнее поведение. `BuildPreciseV2()`
использует Preview, сохраняет ручные значения и не разрешает scale inference по
умолчанию.

Для exact override действует строгий контракт:

- `Full page name` является окончательным именем;
- если оно заполнено, `Suffix action` обязан быть `Keep`;
- для `Suffix Set/Clear` поле `Full page name` оставляют пустым;
- `Scale Set/Clear` остаётся независимым от name/suffix;
- более специфичный PDF pattern выигрывает, при одинаковой специфичности
  используется первая строка.

Settings блокирует invalid regex, пустой `Suffix Set`, неразбираемый `Scale Set`,
конфликт Full Name + Suffix Set/Clear и duplicate exact override.

## Evidence и suffix rules

`Precise v2` использует детерминированную иерархию:

1. Exact sheet override.
2. Drawing List / Sheet Index.
3. Явные title-block label, title и scale поля.
4. Prominent title evidence.
5. Body evidence только как более слабый источник.
6. Discipline/number fallback.

Каждое поле сохраняет собственные `source`, `confidence` и `evidence`. Metadata
также содержит detector version, preset и config fingerprint.

В typed catalog закреплены проверенные architectural/structural случаи,
включая compound suffixes, `S5.1/S6.1/S7.1 -> d`, `S902 -> shw`, structural
foundation/wood details, schedules, finish sheets, notes, sections и deliberate
blank presentation sheets. `sec` остаётся scale-capable; details, notes,
schedules и title/text sheets по умолчанию no-scale.

## Scale safety

- `NTS / NOT TO SCALE` сохраняется как явное no-scale решение.
- `AS NOTED` не угадывается и остаётся review candidate.
- Preview уважает editable `MinimumScaleConfidence`.
- `AutoApplyHighConfidence` отдельно требует High confidence.
- Exact Scale Set/Clear имеет приоритет над общим suffix policy.
- Редкие корректные масштабы не ограничены старым preset allowlist.
- C# и Python одинаково понимают decimal forms: `0.287:1`, `0.287`, `0,287`,
  `0.287 = 1`, `k/к/r/to`.
- Preview, Apply, Keep и learning сохраняют точное numeric значение редкого
  масштаба и не snap-ят его к соседнему preset.
- `Keep` сохраняет обнаруженный NTS/no-scale, а не превращает его в пустой
  manual scale.

Применение scale проходит через общий page-scale path: обновляются page source,
viewport, measurements, totals и autosave.

## Learning safety

Learning записывается только после пользовательского review. Detection и final
decision сохраняются раздельно. Observation key включает PDF fingerprint, page,
detector version и config fingerprint, поэтому повторный анализ не раздувает
training data.

Project learning имеет приоритет над global learning, но может заменять только
более слабое evidence. Learned suffix не меняет защищённый title-block/index
scale или NTS. Learned scale синхронно заменяет text, ratio и meters-per-point.
Конфликтные правила не становятся auto rules.

## Владельцы кода

- `Models/SheetMetadataConfig.cs` — schema, presets и editable policy.
- `Models/SheetMetadataSuffixRules.cs` — typed suffix catalog и exact override.
- `Models/PdfSheetMetadataPolicy.cs` — rename/scale apply safety.
- `Models/PdfSheetMetadataService.cs` — metadata parsing/normalization.
- `Models/PdfSheetMetadataService.Learning.cs` — immutable detection/final records.
- `Tools/pdf_layers_helper.py` — Legacy и Precise v2 PDF detector.
- `MainWindow.SettingsManager.SheetMetadata.cs` — Settings editor.
- `MainWindow.PagesPdfMetadata.cs` — Preview и confirmed apply workflow.
- `Dialogs/PdfMetadataPreviewDialog.cs` — bulk review surface.
- `Models/SettingsPresetStore.cs` — global/job precedence.
- `Tests/PdfSheetMetadataWorkflowTests.cs` — workflow safety regressions.
- `Tests/test_pdf_sheet_metadata_precise_v2.py` — detector contract.
- `Tests/SheetMetadataGoldenHarness.cs` — read-only real-PDF golden runner.

## Проверка

Подтверждено на текущем snapshot:

```powershell
dotnet build .\ourplancore.sln
# 0 warnings, 0 errors

dotnet run --project .\Tests\OurPlanCore.Tests.csproj --no-build
# 488/488 passed

python -m unittest .\Tests\test_pdf_sheet_metadata_precise_v2.py
# 24/24 passed

python -m py_compile .\Tools\pdf_layers_helper.py
git diff --check
```

Read-only golden validation:

| PDF set | Pages | Golden failures | Scale candidates | Scale skipped |
| --- | ---: | ---: | ---: | ---: |
| Croton architectural | 53 | 0 | 33 | 20 |
| Avenue combined bid set | 49 | 0 | 27 | 20 |
| Avenue structural | 11 | 0 | 4 | 7 |
| Metro structural | 12 | 0 | 3 | 8 |
| **Total** | **125** | **0** | **67** | **55** |

Golden runner не изменял source PDF или job metadata.

Rollback checkpoint перед изменениями:

```text
checkpoint-before-sheet-metadata-v2-20260714
f3834a0f22d7e1d453f4b436e7504cde1b6e7119
```

## Release status

Код и data validation готовы к релизу. Packaged publish/deploy, global Precise v2
activation, shortcut и startup-log evidence добавляются в этот раздел после
финальной упаковки.
