# OurPlaneCore Job Creation And Storage Flow

> Исторический документ. Актуальный срез 2026-09-06: [состояние программы](../CURRENT_OURPLANECORE_STATUS.md), [release evidence](../STRATEGY_APP_EVIDENCE_2026_09_06.md) и [код этой области](../../MainWindow.ProjectPackage.cs). Старые планы, пути и замеры ниже относятся к дате документа.

Date: 2026-06-05

This document describes the current behavior verified from the codebase. It is a
handoff/prompt for future work on job creation, sheet import, page storage, and
takeoff loading.

## Copy-Paste Prompt For A Future Agent

You are working in `C:\Users\User\Desktop\ourplanecore` on the WPF
OurPlaneCore app. The user wants changes around how a job is created, how sheets
are imported/stored, or how takeoffs attach to sheets.

Before changing behavior, read the real flow first:

- UI entry points: `MainWindow.OpenImportMenu.cs`, `MainWindow.JobPicker.cs`,
  `MainWindow.JobLifecycle.cs`, `MainWindow.PdfImport.cs`,
  `MainWindow.PdfTakeoffImport.cs`, `MainWindow.PlanSwiftImport.cs`.
- Storage layer: `Models/Storage/JobLayout.cs`,
  `Models/Storage/PageStore.cs`, `Models/Storage/TakeoffStore.cs`,
  `Models/Storage/StorageSupport.cs`, `Models/Storage/StorageDtos.cs`.
- Page open/load path: `MainWindow.PagesTree.cs`, `MainWindow.PageTabs.cs`,
  `Controls/PdfViewport.PageApi.cs`, `Controls/PdfViewport.MeasurementApi.cs`,
  `Controls/PdfViewport.SelectionState.cs`.
- Measurement/takeoff write path: `MainWindow.ToolControls.cs`,
  `MainWindow.TakeoffsCreation.cs`, `MainWindow.MeasurementCallbacks.cs`,
  `MainWindow.MeasurementClipboard.cs`, `MainWindow.TakeoffsTree.cs`.

Current rules:

- A job is a directory with `Data.xml`, `sources`, `Pages`, `Takeoffs`, and
  `AI_Context`.
- Imported source PDFs are copied into `<job>\sources`.
- A sheet is a folder under `<job>\Pages`; it becomes a page because it contains
  `source.json`.
- `source.json` is the canonical page load file. It stores the relative PDF
  path, zero-based PDF page index, scale, layer/cache flags, hidden takeoffs,
  overlay data, and raster sheet metadata.
- `source_pdf.json` is optional PDF metadata used for rename/scale/analysis; it
  is not the primary file that opens the sheet.
- A takeoff item is a folder under `<job>\Takeoffs` with `Data.xml` property
  `SmartNodeKind=item`.
- Measurements for a takeoff item are stored in
  `<takeoff item folder>\measurements.json`.
- Each measurement stores `page_folder`. That field is the link from a takeoff
  measurement back to the sheet folder.
- On job open, all takeoff items and all measurements are loaded into memory,
  then the viewport draws only measurements whose `PageFolder` matches the
  currently open page folder.

Do not assume a source PDF folder is still used after import. After import, the
app opens the copied PDF under `<job>\sources` using the relative path stored in
each sheet's `source.json`.

## What The User Clicks

### Launch

The user normally starts the packaged app from the Desktop shortcut, not from
source. The current v4 shortcut on this machine is:

`C:\Users\User\Desktop\OurPlaneCore-v4.lnk`

It points to:

`C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore-v4.exe`

### Create A Job From A Folder Of PDFs

1. Open the app.
2. Click the top ribbon button `Open / Import`.
3. Choose `New job from a folder of PDFs...`.
4. In the folder picker `Select folder with PDFs for the new job`, select the
   source folder that contains PDFs. The app searches this folder recursively
   for `*.pdf`.
5. If no PDFs are found, the app shows `No PDF files were found in the selected
   folder or its subfolders.`
6. Enter the job name in the `New Job` prompt. Default name is the selected PDF
   folder name.
7. The job is created under the resolved jobs root:
   - selected jobs root from the Job Picker, if available;
   - otherwise the current job parent;
   - otherwise saved `JobsRootPath` / recent jobs roots from app settings;
   - otherwise `Documents\OurPlaneCore Jobs` if available;
   - fallback: `Desktop\OurPlaneCore Jobs`.
8. The new job opens immediately.
9. PDF import starts automatically on idle. The app imports all found PDFs into
   the new job without asking for page names (`confirmPageNames: false`).
10. The `Import PDF Options` dialog appears. It offers
    `Build readable raster cache and strict black-line snap index`. If checked,
    raster/snap cache is built during import; original PDFs still remain the
    source for export, metadata, layers, and rebuild.
11. After import, the Pages tree reloads and the first imported page opens.

### Create A Blank Job

1. Click `Open / Import`.
2. Choose `Blank job - start empty, add sheets later`.
3. Enter the job name in the `Blank Job` prompt.
4. The app creates the job folder and base structure, opens it, but creates no
   PDF sheets.
5. To add sheets later, use `Add Pages`, `Blank Sheet`, or the current-job
   import menu.

### Add PDF Sheets Into An Existing Job

Top ribbon / Pages area exposes:

- `Add Pages`: calls PDF import into the current job.
- `Blank Sheet`: creates an empty PDF-backed sheet.
- `PDF Takeoffs`: imports PDFs plus supported PDF measurement annotations.

From `Open / Import`, the submenu `Import into the current job` has:

- `PDF file(s)...`
- `Folder of PDFs...`
- `PlanSwift project...`

`PDF file(s)...` opens a file picker and lets the user select one or many PDFs.
For this path, the app asks for page names first. `Folder of PDFs...` searches
recursively and imports without asking for page names.

### Import PDF Takeoffs

1. Click `PDF Takeoffs` in the ribbon, or `Open / Import` ->
   `Import PDF Takeoffs...`.
2. Pick the source folder with PDF takeoff annotations.
3. Choose mode:
   - `Create new job from PDF takeoffs`; or
   - `Import into current job`.
4. For a new job, choose `New job parent folder` and `New job name`.
5. Choose import options:
   - `Import PDF takeoff lines / areas / counts as editable takeoffs`;
   - `Import PDF dimensions as Ruler annotations`;
   - `Remove supported PDF measurement annotations from sheet background`.
6. The app scans PDFs through the PDF takeoff annotation helper, shows a preview,
   and only writes job files after confirmation.
7. Destination buckets are:
   - Pages: `<job>\Pages\from pdf` for new jobs, or
     `<current import folder>\from pdf` for current-job import.
   - Takeoffs: `<job>\Takeoffs\from pdf` for new jobs, or
     `<current takeoff parent>\from pdf` for current-job import.
8. The app writes a report under `<job>\import_reports`.

### Import PlanSwift

There are two PlanSwift paths:

- Top ribbon `PlanSwift`: creates a separate converted job.
- `Open / Import` -> `Import into the current job` -> `PlanSwift project...`:
  imports into the current job.

For current-job import, the app places imported content under
`Pages\01. planswift` and `Takeoffs\01. planswift`.

The PlanSwift source folder is read-only. Pages are converted from PlanSwift
page images into PDF sheets. By default, PlanSwift pages without measured
takeoff geometry are skipped unless `Import all PlanSwift sheets and takeoff
folders` is enabled.

## What Gets Created On Disk

Creating a job calls `OurPlaneCoreJobStore.CreateJob(...)`, which delegates to
`JobLayout.CreateJob(...)`.

Base layout:

```text
<job root>\
  Data.xml
  sources\
    Data.xml
  Pages\
    Data.xml
    --------others\
      Data.xml
    00. imported\              (created when PDF import needs the default bucket)
      Data.xml
      <sheet folder>\
        Data.xml
        source.json
        source_pdf.json         (optional, after PDF metadata/blank-page paths)
        layers.json             (optional, if PDF layer cache exists)
        annotations.json        (optional, page annotations/rulers)
  Takeoffs\
    Data.xml
    <folder or takeoff item>\
      Data.xml
      measurements.json         (only takeoff item folders need this)
  AI_Context\
    ...
  bookmarks.json                (optional)
  import_reports\               (optional)
```

Every node folder has `Data.xml`. `Data.xml` stores display name, class/type,
GUID, order index, and node properties. The folder name on disk is sanitized and
made unique; the visible name is read from `Data.xml`.

## Where Sheets Come From And Where They Are Stored

### Source PDF Discovery

For `New job from a folder of PDFs...` and `Folder of PDFs...`, the app uses
`PdfImportSourceFinder.FindPdfFilesRecursive(...)`.

Rules:

- It scans the selected source folder recursively.
- It finds `*.pdf`.
- It ignores inaccessible folders.
- It sorts by relative path inside the selected source folder.

The selected source folder is only the import source. It is not the long-term
runtime location for the sheets.

### Source PDF Copy

During import, `PageStore.ImportPdf(...)` copies each source PDF to:

`<job>\sources\<original pdf file name>`

If a file name conflicts, the app chooses a unique file path. After import,
page loading uses this copied PDF path, not the original selected source path.

### Sheet Folder Creation

Each imported PDF page becomes a separate page folder under the destination
Pages folder.

For ordinary PDF import into the default bucket:

`<job>\Pages\00. imported\<page name>\`

For a multi-page PDF, each PDF page becomes its own sheet folder. The same copied
PDF under `<job>\sources` can be referenced by many sheet folders, each with a
different `page` index in `source.json`.

For blank sheets, `PageStore.CreateBlankPage(...)` creates a blank PDF under
`<job>\sources` and a sheet folder under the selected Pages destination.

### Sheet Files

The sheet folder's important files are:

- `Data.xml`: visible sheet name and order.
- `source.json`: canonical sheet source.
- `source_pdf.json`: optional extracted/analyzed metadata.
- `layers.json`: optional layer manifest.
- `annotations.json`: optional annotations/rulers.

Example `source.json` meaning:

```json
{
  "pdf": "..\\..\\sources\\Arch.pdf",
  "page": 0,
  "scale_m_per_pt": 0.004233333333333333,
  "pdf_layers_cached": false,
  "pdf_layers": [],
  "legend_takeoff_order": [],
  "legend_takeoff_order_mode": "auto",
  "hidden_takeoffs": []
}
```

`pdf` is relative to the sheet folder. On load, the app resolves it back to an
absolute path. `page` is zero-based. `scale_m_per_pt` is the page scale used by
line/area measurements.

## How Sheets Are Loaded Later

Opening a job calls `OpenJob(...)` in `MainWindow.JobLifecycle.cs`.

The relevant sequence is:

1. `OurPlaneCoreJobStore.LoadJob(rootPath)` ensures base folders exist and
   returns an `OurPlaneCoreJob`.
2. `ReloadPagesTree(_currentJob.PagesRoot)` rebuilds the Pages tree.
3. `FillPagesTree(...)` walks folders under `<job>\Pages`.
4. A folder is treated as a page if `OurPlaneCoreJobStore.TryReadPage(folder)`
   succeeds.
5. `TryReadPage(...)` reads `source.json`, resolves the copied PDF path and page
   index, and returns `PageInfo`.
6. Selecting a sheet in the Pages tree calls `OpenPageInActiveTab(page)`.
7. `LoadPageIntoViewport(page, restoreView)` sets `_currentPage`,
   `_currentPdfPath`, viewport scale, and calls `_viewport.LoadPage(...)`.
8. `PdfViewport.LoadPage(...)` renders the copied PDF page, using raster cache
   if available and otherwise using the PDF renderer/cache path.

`source_pdf.json` can help rename/scale/metadata UI, but the actual open-sheet
path is `source.json -> copied PDF -> page index`.

The last opened job and page are saved in:

`%APPDATA%\OurPlaneCore\settings.json`

Fields:

- `LastJobPath`
- `LastPageFolder`
- `JobsRootPath`
- `JobsRootPaths`
- `RecentJobs`

On later startup/open flows, those settings can make the app reopen the last job
or show it in recent jobs.

## Where Takeoffs Are Stored

Takeoff folders live under:

`<job>\Takeoffs`

There are two kinds of takeoff nodes:

- Folder node: `Data.xml` has `SmartNodeKind=folder`.
- Item node: `Data.xml` has `SmartNodeKind=item` and item properties such as
  `Color`, `MeasurementType`, `CountSymbol`, unit price, notes, joist settings,
  and measurement counts.

For a takeoff item, geometry is stored in:

`<job>\Takeoffs\<...takeoff item...>\measurements.json`

`measurements.json` is a JSON array. Each entry includes:

- `id`
- `mtype`: `point`, `line`, or `area`
- `name`
- `notes`
- `points_pdf`
- `holes_pdf`
- `color`
- `count_symbol`
- `page_folder`
- `scale_m_per_pt`
- joist fields

`page_folder` is the important sheet link. It stores the page folder path that
the measurement belongs to. `takeoff_folder` is not stored in the DTO because it
is implied by the folder containing `measurements.json`; when loaded, the app
sets `Measurement.TakeoffFolder` to that takeoff item folder.

## How A New Takeoff Gets Attached To A Sheet

The normal manual record flow is:

1. Open a job.
2. Open/select a sheet in the Pages tree.
3. Select an existing takeoff item in the right Takeoffs tree, or create one:
   - `New Item` in the Takeoffs panel; or
   - click a drawing tool such as `Count`, `Line`, `Area`, `J Area`, `Beam`, or
     `Openings`, which can create a locked matching takeoff item if needed.
4. The selected takeoff becomes `_activeItem`.
5. The viewport receives:
   - `ActiveColor`
   - `ActiveTakeoffFolder`
   - `ActiveCountSymbol`
6. Drawing on the page creates a `Measurement` in PDF coordinate space.
7. The new measurement's `PageFolder` is the current sheet folder.
8. `OnMeasurementAdded(...)` resolves the active takeoff item, sets
   `TakeoffFolder`, applies scale/properties, adds it to the item, refreshes UI,
   and queues autosave.
9. Autosave calls `OurPlaneCoreJobStore.SaveTakeoffItem(item)`.
10. `SaveTakeoffItem(...)` writes takeoff properties to `Data.xml` and writes
    geometry to `measurements.json`.

Line/area tools require a page scale before drawing. Count/point does not need
scale in the same way.

## How Takeoffs Are Loaded Onto A Sheet

Opening a job calls `LoadTakeoffsForJob()`.

The relevant sequence is:

1. `BuildTakeoffChildren(_currentJob.TakeoffsRoot, loadedItems)` recursively
   walks the Takeoffs tree.
2. `OurPlaneCoreJobStore.TryReadTakeoffItem(folder)` identifies item folders.
3. `TakeoffStore.LoadMeasurements(folder)` reads that item's `measurements.json`.
4. Loaded measurements get `TakeoffFolder = <item folder>`.
5. The app repairs some stale `PageFolder` references when possible.
6. `_viewport.SetMeasurements(_takeoffItems.SelectMany(i => i.Measurements))`
   sends all measurements from all takeoff items to the viewport.
7. The viewport indexes measurements by `PageFolder`.
8. When a sheet is open, the viewport draws only measurements where
   `Measurement.PageFolder` matches the active `_pageFolder`.

So takeoffs are not physically stored inside the sheet folder. They are stored
under `Takeoffs`, and the per-measurement `page_folder` link decides which sheet
shows each section/count/area.

The Pages tree also uses the same link to show linked takeoff nodes under each
sheet. Refresh paths such as `RefreshPageTakeoffIndicatorsForFolder(...)`
rebuild those visible linked rows from the loaded measurements.

## Important Repair/Gotcha Notes

- If a job folder is copied outside the app, `measurements.json` may still have
  stale absolute `page_folder` paths. Then takeoffs exist on disk but do not show
  on sheets. The app has repair logic in `RepairMeasurementPageFolderReferences`,
  but data repair should be exact and conservative.
- Do not manually change `source.json` paths unless you understand that they are
  relative to the page folder.
- Do not assume visible names are unique. Disk folders are uniqued; display names
  live in `Data.xml`.
- Do not store takeoff geometry in page folders. The app expects geometry under
  takeoff item folders and uses `page_folder` to attach it to sheets.
- `source_pdf.json` is useful for sheet metadata, but deleting it should not by
  itself prevent a sheet from opening. Breaking `source.json` will.

## Fast Checklist For Debugging

To verify a job on disk:

1. Check job root has `Data.xml`, `Pages`, `Takeoffs`, `sources`.
2. Pick a sheet folder under `Pages` and confirm it has `source.json`.
3. Open `source.json`; resolve the `pdf` path relative to that sheet folder and
   confirm the copied PDF exists under `sources`.
4. Pick a takeoff item folder under `Takeoffs` and confirm `measurements.json`
   exists.
5. In `measurements.json`, confirm each measurement has a `page_folder` that
   points to an existing sheet folder under this job's `Pages`.
6. If takeoffs do not display on a sheet, compare:
   - active sheet folder path;
   - measurement `page_folder`;
   - hidden takeoff visibility for that sheet.

## Source Files Used

- `MainWindow.OpenImportMenu.cs`
- `MainWindow.JobPicker.cs`
- `MainWindow.JobLifecycle.cs`
- `MainWindow.PdfImport.cs`
- `MainWindow.PdfTakeoffImport.cs`
- `MainWindow.PlanSwiftImport.cs`
- `MainWindow.PagesTree.cs`
- `MainWindow.PageTabs.cs`
- `MainWindow.ToolControls.cs`
- `MainWindow.TakeoffsCreation.cs`
- `MainWindow.MeasurementCallbacks.cs`
- `MainWindow.MeasurementClipboard.cs`
- `MainWindow.TakeoffsTree.cs`
- `Controls/PdfViewport.PageApi.cs`
- `Controls/PdfViewport.MeasurementApi.cs`
- `Controls/PdfViewport.SelectionState.cs`
- `Models/PdfImportSourceFinder.cs`
- `Models/Storage/JobLayout.cs`
- `Models/Storage/PageStore.cs`
- `Models/Storage/TakeoffStore.cs`
- `Models/Storage/PageAnnotationStore.cs`
- `Models/Storage/PageBookmarkStore.cs`
- `Models/Storage/StorageDtos.cs`
- `Models/Storage/StorageSupport.cs`
- `Models/AppSettingsStore.cs`
- `Models/Import/PlanSwiftProjectImporter.cs`
- `Models/Import/PlanSwiftProjectImporter.Pages.cs`
- `Models/Import/PlanSwiftProjectImporter.Takeoffs.cs`
