# Excel Macro Export Workflow

Status: implemented on 2026-07-29; Framing extension verified on 2026-07-30.

Current code checkpoint: `30d296b` (`Add framing Excel macro export`).

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
- `LG` - opens the current job's optional structural legend editor;
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

When `Include framing in ALL` is enabled, Framing runs once after the configured
ordinary action sequence. A missing or empty Framing tree is reported as
skipped; a Framing failure stops the remaining batch.

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

## Framing structural extension

### Legend

`LG` opens a compact text flyout like the calculator panel. Paste one or two
tab-separated columns, one legend entry per line. Blank text is valid, so all
supported structural macros continue to work without a legend.

The legend is job-specific and is saved at:

```text
<job>\AI_Context\settings\excel_framing_legend.txt
```

The saved text is appended to the macro source selection only for structural
categories that support the legend.

### Expected Takeoffs tree

The built-in Framing aliases recognize:

```text
framing
├── 1st floor framing
│   ├── posts
│   ├── beams
│   ├── headers
│   │   ├── ext
│   │   └── int
│   ├── joists
│   ├── details
│   └── stairs
├── 2nd floor framing
├── 3rd floor framing
├── 4th floor framing
├── 5th floor framing
└── roof framing
```

Floor and category aliases are editable, so equivalent lowercase Auto Tree
names can be used without changing code.

### Framing list targets

Normal structural categories replace the output block immediately below the
matching green `#99CC00` heading and before the next green heading:

| Framing source | Excel heading |
| --- | --- |
| 1st floor framing | `1st Floor Framing List` |
| 2nd floor framing | `2nd Floor Framing List` |
| 3rd floor framing | `3rd Floor Framing List` |
| 4th floor framing | `4th Floor Framing List` |
| 5th floor framing | `5th Floor Framing List` |
| roof / loft framing | `Roof Frame list` |

Input is staged from column `J`; generated macro output replaces the target
rows in `A:H`.

Default category contract:

| Category | Preprocess | Main processing |
| --- | --- | --- |
| Posts | `C_SumTheSameValues` | `C_PostsSort` |
| Beams | `C_SumTheSameValues` | `C_BeamsSort` |
| Headers ext/int | `C_SumTheSameValues` | `C_HeadersSort` |
| Joists | none | group by takeoff name, then `C_JoistsSort` |
| Details | `C_SumTheSameValues` | existing sheet-name sort, direct output |
| Stairs | `C_SumTheSameValues` | direct output |

Joists intentionally never run `C_SumTheSameValues`. For every takeoff-name
group the input contract is all `(quantity / length)` rows first, immediately
followed by the joist name/spacing row such as `2x10 16"`.

### Header wall targets

Headers do not remain inside the floor framing-list block. The service locates
the old block beginning with:

```text
Note: The headers indicated on the plan
```

It replaces the full Ext./Int. Headers placeholder area with the
`C_HeadersSort` result and preserves the next workbook section, including
`Wall Sheathing`.

Numeric floor mapping shifts down one wall level:

| Header source | Wall block |
| --- | --- |
| 1st floor framing | `Basement Floor Walls` |
| 2nd floor framing | `1st Floor Walls` |
| 3rd floor framing | `2nd Floor Walls` |
| 4th floor framing | `3rd Floor Walls` |
| 5th floor framing | `4th Floor Walls` |

Roof/loft headers go to the same-floor wall block of the highest occupied
numeric framing floor. For example, when only 2nd and 3rd framing folders have
data, 2nd headers go to `1st Floor Walls`, 3rd headers go to
`2nd Floor Walls`, and roof headers go to `3rd Floor Walls`.

### Editable Framing rules

The Framing editor is part of `8 Settings > Excel macro actions` and exposes:

- inclusion in `ALL`;
- workbook, worksheet, source column, and Framing folder aliases;
- Sum macro, target heading color, and header-note marker;
- floor order/aliases, framing heading, shifted header target, and roof target;
- category aliases, mode, main macro, Sum flag, and order.

It uses the same built-in default, global default, per-job override, Reset,
Save, and Apply pattern as the other Settings categories.

## Code ownership

- `MainWindow.xaml`
  - combined right strip, Excel buttons, horizontal splitter.
- `MainWindow.ExcelMacroStrip.cs`
  - split ratio application, drag persistence, module visibility.
- `MainWindow.ExcelMacroExport.cs`
  - single-action selection and execution.
- `MainWindow.ExcelMacroBatch.cs`
  - `ALL` orchestration, Framing tail step, and stop/skip summary.
- `MainWindow.ExcelFramingLegend.cs`
  - `LG` panel lifecycle and job-specific legend persistence.
- `MainWindow.SettingsManager.ExcelActions.cs`
  - editable action, range, macro, whitelist, order, and floor rules.
- `MainWindow.SettingsManager.ExcelFraming.cs`
  - editable Framing floor/category/target/macro contract.
- `Controls/ExcelFramingLegendPanel.xaml`
  - compact legend text editor.
- `Models/ExcelMacroExportConfig.cs`
  - defaults, schema migration, aliases, action order, cleanup contract.
- `Models/ExcelMacroBatchPlanner.cs`
  - direct-root/building-root resolution and mixed-root rejection.
- `Models/ExcelMacroPayloadBuilder.cs`
  - recursive role matching, floor grouping, units, ordering.
- `Models/ExcelMacroTakeoffExportService.cs`
  - Excel COM writing, VBA invocation, protection and restoration.
- `Models/ExcelFramingExportConfig.cs`
  - built-in Framing defaults and editable floor/category rules.
- `Models/ExcelFramingExportPlanner.cs`
  - Framing tree discovery, category rows, floor/header routing, and Joists.
- `Models/ExcelFramingExportService.cs`
  - structural Excel staging, macro execution, and exact block replacement.
- `Models/ExcelFramingLegendStore.cs`
  - atomic per-job legend persistence.
- `Tests/ExcelMacroExportTests.cs`
  - config, scope, ordering, floor and whitelist regressions.
- `Tests/ExcelMacroSmokeHarness.cs`
  - disposable real-workbook Excel COM validation.
- `Tests/ExcelFramingExportTests.cs`
  - Framing defaults, routing, Joists, aliases, and batch scope regressions.
- `Tests/StructuralExcelMacroSmokeHarness.cs`
  - direct real-workbook structural VBA and full replacement validation.

## Verification

Current implementation verification:

- Debug build: `0 warnings / 0 errors`;
- C# regression harness: `654/654`;
- real Excel COM smoke on a disposable copy of the selected
  `TemplateCom.xlsm`;
- SQFT/Gables/Truss Heel `A2`, Walls `A3+B`, Parapet `A4`, Eve/Rakes `A6`,
  and Openings `C+A5` all passed;
- Walls smoke protected 164 mandatory rows and left no temporary marker;
- structural Sum, Beams, Posts, Headers, grouped Joists, Details, and complete
  Framing/header block replacement all passed;
- original template stayed byte-identical with SHA-256
  `494C41CF4CC6DDB7A4C5D5492328B76ED18DC1E582E0AAE61BEBE5A15DB2569A`;
- final installed runtime: `0 ERROR` after the latest
  `Application startup`, with `Loaded takeoffs` and `Viewport` evidence.

## Local release package

The current locally installed EXE was produced from source commit
`30d296b8b042377f0da35bbaedcb5b5965fe6443`:

- compressed self-contained single-file EXE: `171,931,210` bytes;
- installed path:
  `C:\Users\User\Desktop\updates\OurPlanCore\ourplancore.exe`;
- SHA-256:
  `A1200D906221D2C1D0E47AB24013B5D43C408310CC6A8CE36F74CE77A91437F2`;
- the previous EXE remains available as a timestamped `.bak`;
- `C:\Users\User\Desktop\OurPlanCore.lnk` targets the installed update EXE and
  uses the update directory as its working directory;
- installed runtime validation loaded the Agrace job and emitted
  `Loaded takeoffs` and `Viewport` with `0 ERROR` after the latest startup;
- this delivery updated only the EXE; the workbook and GitHub release were not
  published.
