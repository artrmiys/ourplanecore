# Excel Macro Export Workflow

Status: implemented and verified on 2026-07-29.

Code checkpoint: `e3ead9f` (`Protect wall cleanup output rows`).

This document is the canonical operator and implementation reference for the
OurPlanCore vertical Excel macro export strip and its `ALL` workflow.

## Быстрый порядок работы

1. Открыть `TemplateCom.xlsm` в Microsoft Excel.
2. Разрешить выполнение VBA macros.
3. Оставить книгу открытой. Она может быть свёрнута и не обязана быть активной.
4. В дереве Takeoffs:
   - для отдельной операции выбрать её папку, например `Walls`; или
   - для `ALL` выбрать корень одного здания либо общий Takeoffs root, если Auto
     Tree расположен прямо в корне.
5. Нажать соответствующую кнопку в нижней половине правой вертикальной панели.
6. Проверить результат и сохранить Excel-книгу вручную.

OurPlanCore находит открытую книгу по точному имени `TemplateCom.xlsm`,
активирует лист `Detailed Frame List`, записывает входные строки и запускает
VBA. Приложение не сохраняет и не закрывает пользовательскую книгу.

## Vertical panel

The former right command strip and separate Excel strip are one 26 px-wide
vertical strip:

- existing quick tools occupy the upper section;
- Excel macro buttons occupy the lower section;
- a horizontal `GridSplitter` resizes the two sections;
- the default split is 50/50;
- the saved split is clamped to 20-80% and restored on startup;
- disabling the Excel Integration module collapses the lower section and
  splitter without removing the upper command strip.

Buttons:

- `ALL` - runs the configured batch sequence;
- `SF` - SQFT;
- `WL` - Walls;
- `GB` - Gables;
- `TH` - Truss Heel;
- `PP` - Parapet;
- `E/R` - Eve / Rakes;
- `OP` - Openings.

The compact layout follows the existing dense PlanSwift/Bluebeam-style command
strip instead of introducing a separate wide panel.

## Selection and scope rules

### Single action

Select one relevant Takeoffs folder or an item inside it, then press that
action's button. The selected scope is scanned recursively, but only items
under the configured action-folder alias are exported.

Recommended selection: the category folder for one building, such as `Walls`
or `Openings`. Individual floor folders do not need to be selected separately.

### ALL under a building folder

For a tree shaped like:

```text
Takeoffs
└── House 1
    ├── SQFT
    ├── Walls
    ├── Gables
    ├── Truss Heel
    ├── Parapet
    ├── Eve Rakes
    └── Openings
```

select `House 1` or any export folder/item inside `House 1`. `ALL` resolves the
shared batch root to `House 1` and scans its configured export folders.

### ALL with Auto Tree directly in Takeoffs root

For a tree shaped like:

```text
Takeoffs
├── SQFT
├── Walls
├── Gables
├── Truss Heel
├── Parapet
├── Eve Rakes
└── Openings
```

`ALL` is supported. Select the Takeoffs root or any one of its export
folders/items. The resolved batch root is the job Takeoffs root, so sibling
export folders participate in the sequence.

### Mixed roots

`ALL` refuses a selection that resolves to more than one measured
building/root. For example, selecting the job Takeoffs root when both `House 1`
and `House 2` contain measured export folders is rejected. Select only one
building or one of its export folders.

Missing, empty, zero-total, or unmatched action folders are reported as skipped.
At least one action must produce rows for the batch to continue.

## Default action contract

| Action | Folder aliases | Input destination | Main VBA |
| --- | --- | --- | --- |
| SQFT | `sqft`, `sqfts` | append in `J:L`, scan `I:N` | `A2_SQFT_calc` |
| Walls | `walls` | append in `J:L`, scan `I:N` | `A3_Walls_Calc_AllGroup` |
| Gables | `gables`, `gable` | append in `J:L`, scan `I:N` | `A2_SQFT_calc` |
| Truss Heel | `trussheel`, `truss heel`, `truss heels` | append in `J:L`, scan `I:N` | `A2_SQFT_calc` |
| Parapet | `parapets`, `parapet` | append in `J:L`, scan `I:N` | `A4_Parapet` |
| Eve / Rakes | `eves rakes`, `eve rakes`, `eaves rakes` | append in `J:L`, scan `I:N` | `A6_Eve_Rakes` |
| Openings | `openings` | append from `Z158` in `Z:AB` | `A5_Openings` |

The ordinary actions find the next vertical insertion point by reading
values/formulas in `I:N`. Formatted but empty rows do not advance the insertion
point. Openings uses its independent `Z:AB` source area starting at row 158.

## ALL sequence

Default order:

```text
SQFT
→ Walls
→ Gables
→ Truss Heel
→ Parapet
→ Eve / Rakes
→ Openings
```

The sequence is editable under `8 Settings > Excel macro actions`. A failed
action stops the batch; later actions are not run. Missing action folders are
skipped and included in the final summary.

## Floor grouping

Walls and Openings write a numeric floor header before each floor group.
Configured floor rules support floors `0` through `5`; built-in aliases include
forms such as `basement`, `1st`, `first`, `2nd`, and `second`.

The folder immediately below the action folder is treated as the floor folder.
Items whose floor folder does not match the effective floor rules are skipped
with a warning.

Openings processing for every exported floor is:

```text
write floor rows to Z:AB
→ select that floor's item range
→ C_SumNearWindowValues
→ after all floors, select the complete input
→ A5_Openings
```

## Ordering rules

Walls is ordered independently inside every floor:

```text
corners
→ ext rows by LF descending
→ cor/corr
→ dem
→ standalone 2x6
→ standalone 2x4
→ remaining rows in source order
```

Eve / Rakes is ordered:

```text
eve/eave rows by LF descending
→ rake rows by LF descending
→ returns
→ remaining rows
```

These rules are selected through the action's editable `Row order` setting.

## Walls cleanup and mandatory rows

Both the single `WL` button and the Walls step inside `ALL` execute:

```text
A3_Walls_Calc_AllGroup
→ identify and protect every mandatory row in A25:H1367
→ select A25:H1367
→ B_DeleteZeroRowsOnlyIn_AtoH
→ restore protected formulas/values at their shifted rows
```

`A1368`, whose initial label is `Gable Walls`, is the boundary and is not part
of the cleanup selection.

Protection is not implemented as a smaller or discontinuous selection. Every
exact normalized occurrence of the configured labels is protected inside the
full cleanup range. Matching is case-insensitive, ignores surrounding/repeated
whitespace and non-breaking spaces, but does not accept unrelated suffix text.

Built-in mandatory labels:

```text
Window Flashing
Sill Flashing
Note: The headers indicated on the plan
Ext. Headers up to 48" (2)
Ext. Headers up to 60" (3)
Ext. Headers up to 72" (3)
Ext. Headers over 72" (3)
Int. Headers (2)
Wall Sheathing
Vapor Barrier
Insulation
Box Sheathing
Tape
Shear Walls
Holdowns
```

Repeated occurrences are all protected on every floor. Before running the
delete macro, the service snapshots column C's formula/value for each protected
row and temporarily places a unique nonzero marker there. The VBA macro can
therefore compact `A:H` without treating that row as a zero row. After VBA
shifts rows upward, the markers are found at their new locations and the exact
formula/value is restored.

Failure behavior:

- if VBA fails, restoration still runs;
- all markers that can be found are restored before an error is reported;
- if any mandatory row disappears, the action fails with an explicit error;
- the workbook remains open and any already-written source rows remain in
  place for review.

## Editable settings

The full behavior contract is editable in `8 Settings > Excel macro actions`.
Global and per-job presets use the standard `SettingsPresetStore` resolution:

```text
job override
→ global default
→ built-in default
```

Relevant fields include:

- workbook and worksheet;
- action-folder aliases;
- scan/write columns and starting row;
- main VBA and per-floor preprocess VBA;
- row order;
- after-macro VBA;
- after-macro range;
- `Always keep rows`, one exact label per line;
- floor aliases `0-5`;
- `ALL sequence`.

The panel provides Reset built-in, Save global default, Save as this job, Use
global for this job, and Apply / Run selected.

Schema version 4 upgrades older saved configurations with the built-in Walls
cleanup macro, range, and mandatory-label list.

## Code ownership

- `MainWindow.xaml`
  - combined right strip, Excel buttons, horizontal splitter.
- `MainWindow.ExcelMacroStrip.cs`
  - split ratio application, drag persistence, module visibility.
- `MainWindow.ExcelMacroExport.cs`
  - single-action selection and execution.
- `MainWindow.ExcelMacroBatch.cs`
  - `ALL` orchestration and stop/skip summary.
- `MainWindow.SettingsManager.ExcelActions.cs`
  - editable action, range, macro, whitelist, order, and floor rules.
- `Models/ExcelMacroExportConfig.cs`
  - defaults, schema migration, aliases, action order, cleanup contract.
- `Models/ExcelMacroBatchPlanner.cs`
  - direct-root/building-root resolution and mixed-root rejection.
- `Models/ExcelMacroPayloadBuilder.cs`
  - recursive role matching, floor grouping, units, ordering.
- `Models/ExcelMacroTakeoffExportService.cs`
  - Excel COM writing, VBA invocation, protection and restoration.
- `Tests/ExcelMacroExportTests.cs`
  - config, scope, ordering, floor and whitelist regressions.
- `Tests/ExcelMacroSmokeHarness.cs`
  - disposable real-workbook Excel COM validation.

## Verification

Implementation checkpoint verification:

- Debug build: `0 warnings / 0 errors`;
- C# regression harness: `647/647`;
- real Excel COM smoke on a disposable copy of the selected
  `TemplateCom.xlsm`;
- SQFT/Gables/Truss Heel `A2`, Walls `A3+B`, Parapet `A4`, Eve/Rakes `A6`,
  and Openings `C+A5` all passed;
- Walls smoke protected 164 mandatory rows and left no temporary marker;
- final Debug runtime: process responsive, `0 ERROR` after the latest
  `Application startup`, with `Loaded takeoffs` and `Viewport` evidence.

Release package details are recorded in `docs/DEVELOPMENT_LOG.md` after local
deployment.
