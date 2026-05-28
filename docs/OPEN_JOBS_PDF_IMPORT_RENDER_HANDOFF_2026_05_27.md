# Open Jobs, PDF Import, Render, and Sheet Naming Handoff - 2026-05-27

## Current Status

Current packaged build:

- Deployed exe:
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`
- Final SHA256:
  `2C1DF76A22D41B9B60A4E58745D3AD36615E61EE019D2355728AD81F9DFB565A`
- Desktop shortcut target:
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`
- Desktop shortcut working directory:
  `C:\Users\User\Desktop\updates\OurPlaneCore`
- Backup exe:
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe.bak`
  was preserved.
- Packaged launch validation:
  process alive, `0` errors after latest `Application startup`,
  `Loaded takeoffs` present, `DocnetSlowCount=0`, and only clean PyMuPDF
  layer-render entries were observed on the checked PDF pages.

Commits:

- `b0ad839 Add job folder removal`
- `0678432 Import PDFs from job folders`
- `8d60fc2 Fix PDF render and sheet naming`
- `da31009 Use clean PDF preview on page open`

## User Requests Covered

- Add a delete/remove folder button in Open Jobs near Manage folders.
- Change new job creation so the PDF folder is selected first, then job name is
  entered.
- Import every PDF found recursively under the selected folder.
- Fix imported sheet rendering artifacts: green/purple outlines, black regions,
  and unreadable text that were not visible in the original PDF.
- Improve sheet Auto Name behavior, especially for multi-page plan PDFs where
  the previous parser picked unrelated footer text.
- Prevent the bad green/purple render from flashing even briefly when opening a
  page.
- Keep page loading as fast as possible.

## Open Jobs Changes

Files:

- `Dialogs/JobPickerDialog.xaml`
- `Dialogs/JobPickerDialog.xaml.cs`
- `Models/AppSettings.cs`

Behavior:

- The Open Jobs dialog now has `Remove folder` beside `Manage folders`.
- Removing a folder removes it from the jobs-root list only. It does not delete
  files on disk.
- The button is disabled when no folder row is selected.
- Folder removal persists through app settings.

## Folder-First PDF Import

Files:

- `Dialogs/NewJobFromPdfFolderDialog.cs`
- `MainWindow.JobManagement.cs`
- `MainWindow.PdfImport.cs`
- `Models/PdfImportSourceFinder.cs`
- `Tests/Program.cs`

Behavior:

- `Create New Job` now opens the folder picker first.
- After a PDF folder is selected, the job-name dialog appears.
- All `*.pdf` files under the chosen folder are discovered recursively.
- The same recursive discovery is available from
  `Import PDF Folder to Current Job...`.
- If no PDFs are found, the app shows a clear no-PDF message and does not create
  a job/import plan.

## Render Artifact Root Cause

Live failing job inspected:

```text
C:\Users\User\Desktop\Takeof_desctop\87. Caretta Senior Lvg_Bliffert
```

Source PDF inspected:

```text
C:\Users\User\Desktop\Takeof_desctop\87. Caretta Senior Lvg_Bliffert\sources\260428 Caretta Senior Living - Sussex Conformed CD_ADD-1_ADD_2.pdf
```

Findings:

- The viewport was still using Docnet as the first and sometimes final visible
  render.
- PyMuPDF render output for the same pages was clean.
- The app could apply a clean PyMuPDF render and then later let a queued
  Docnet render overwrite it at a different zoom level.
- This caused the user-visible green/purple outlined text, black regions, and
  unreadable artifacts to return on some pages.

## Render Fix

Files:

- `Controls/PdfViewport.PageApi.cs`
- `Controls/PdfViewport.Layers.cs`
- `Controls/PdfViewport.ViewTransform.cs`
- `Controls/PdfViewport.cs`

Behavior:

- Normal PDF page open no longer displays a Docnet preview.
- First visible render is now a clean PyMuPDF layer-render path, even when the
  PDF has no OCG/layers.
- To keep loading fast, the first render uses
  `ViewportRenderPolicy.InstantPagePreviewRenderScale` (`0.35`).
- After that first clean preview appears, `ZoomFit` / restored view schedules a
  normal quality PyMuPDF rerender.
- Once a PyMuPDF render applies, pending Docnet renders are invalidated so they
  cannot overwrite the clean image.
- Zoom rerender is blocked while a page switch is waiting for its first clean
  PyMuPDF frame, preventing a Docnet request from slipping in during open.
- Docnet is now only a fallback path if PyMuPDF render fails.

Speed note:

- This intentionally avoids the bad Docnet flash.
- The first clean preview is lower-resolution and fast.
- On the inspected heavy Caretta PDF, one first-preview slow-log entry was about
  `1196ms` at scale `0.35`; normal pages may be below the slow-log threshold.

## Sheet Naming Root Cause

Bad observed metadata example:

- Folder: `Pages\00. imported\a26 n (26)`
- `source_pdf.json` said page `75` was `A26 n`.
- The rendered page was actually `A451 / WALL SECTIONS`.

Cause:

- `Tools/pdf_layers_helper.py` could fall back to arbitrary bottom/footer text.
- The Caretta combined PDF contains Revit/file-path footer text like `A26` near
  the bottom of many pages.
- That footer text was being treated as the sheet number.
- Pages with no reliable sheet id could also propose `-`, creating many
  folders named `-`, `- (2)`, etc.

## Sheet Naming Fix

Files:

- `Tools/pdf_layers_helper.py`
- `Models/PdfSheetMetadataService.cs`
- `Tests/Program.cs`

Behavior:

- Sheet id extraction now prefers a prominent title-block sheet number in the
  top/right title block area.
- Revit/file-path/footer noise is ignored for sheet-id fallback.
- `AR` is accepted as a valid sheet prefix, so `AR001`, `AR101`, etc. are not
  rejected.
- Unknown sheet ids no longer propose `-`; the rename proposal stays blank.
- Blank rename proposals are not auto-applied.

Representative helper checks against the Caretta PDF:

- Page `1`: `G001 / TITLE SHEET` -> `g001 n`
- Page `2`: `G002 / SHEET INDEX` -> `g002 n`
- Page `3`: `G003 / BUILDING SYSTEMS` -> `g003 n`
- Page `4`: `AR001 / LIFE SAFETY SUMMARY` -> `ar001 n`
- Page `5`: `AR002 / LIFE SAFETY / ENERGY / CODE SUMMARY` -> `ar002 n`
- Page `75`: `A451 / WALL SECTIONS` -> `a451 sec`
- Pages without a confident sheet number: blank rename proposal instead of `-`.

Important caveat:

- Existing already-imported bad folders were not mass-renamed during this pass.
  The fix applies to new imports and to future/re-run Auto Name flows. A
  separate reviewed repair pass should be used if existing job folders need to
  be renamed in bulk.

## Verification

Commands run:

```powershell
.\Tools\python\python.exe -m py_compile .\Tools\pdf_layers_helper.py
dotnet build .\ourplanecore.sln
dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build
dotnet publish .\ourplanecore.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o .\bin\publish
```

Final results:

- Python helper compile: passed.
- Build: `0 warnings / 0 errors`.
- Tests: `235/235` passed.
- Publish: compressed single-file Release win-x64 completed.
- Deployed to:
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`
- Publish/update SHA256 matched:
  `2C1DF76A22D41B9B60A4E58745D3AD36615E61EE019D2355728AD81F9DFB565A`
- Packaged app launch checked by log:
  - process alive;
  - `0` errors after latest `Application startup`;
  - `Loaded takeoffs` present;
  - `DocnetSlowCount=0`;
  - clean PyMuPDF layer-render entries present.

## Follow-Up Notes

- If page open still feels slow on very large PDFs, the next performance step
  should be an explicit persisted render cache for clean PyMuPDF preview images,
  keyed by source PDF path, modified time, page index, and scale.
  Implemented on 2026-05-28 in
  `docs/PDF_PREVIEW_CACHE_HANDOFF_2026_05_28.md`.
- Do not return to Docnet as a visible first frame for problem PDFs; it fixes
  perceived speed but reintroduces the artifact flash.
- The untracked file `Assets/ourplanecore.ico.bak_20260522_132816` remains
  unrelated and was intentionally not touched.
