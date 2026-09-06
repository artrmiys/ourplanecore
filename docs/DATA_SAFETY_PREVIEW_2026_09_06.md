# Data safety preview 2.2.6

This independent version implements master-plan section 5.3 and Phases 2–3.
The stable 2.2.5 update package and shortcut must remain unchanged. A full
source/Git, installed-package, shortcut and profile backup was hash-verified
before editing: 25,572 files, with SHA-256 manifest and restoration instructions.

## Behavior

- `DataFileReader` distinguishes Missing, Valid, Corrupt and Unreadable.
  Sharing/access failures leave bytes in place; parse failures keep quarantine
  copies. Persistent protection markers stop empty fallbacks from overwriting
  affected files, including after application restart.
- Project menu > **Project Data Recovery** offers Retry, Restore from copy and
  Open file folder. Explicit recovery validates the document and keeps a copy
  before clearing protection. Ordinary successful reads do not silently allow
  stale in-memory objects to save. Truly missing source metadata can still be
  reconstructed; corrupt or inaccessible source metadata requires recovery.
- `SafeJobPathResolver` enforces containment, bounded IDs, invalid/device-name
  checks and junction/symlink rejection, preserving approved cloud placeholders.
  Existing paths contained in the job remain supported. Page/raster/overlay
  references and AI attachments use this boundary. Images open internally.
  AI preflight lists crops, layers, model and marker context, with size/type caps.
- `JobOperationJournal` records original metadata bytes, file/directory
  inventories and move intents before mutations. Nested PDF/PlanSwift imports,
  page sorts, copy/move and reference rewrites share one operation. Recovery runs
  before a writable job opens; pending operations block read-only open and
  package checkpoints. Partial work is not silently accepted after a failure.
- **Undo Last Page Sort** appears in the project and Pages context menus.
  **Undo Last Page Operation** appears in the project menu. Undo preserves
  unrelated later edits and refuses conflicting changes. Interrupted undo is
  also recoverable. Rollback reloads the UI without saving stale measurements.
  Displaced imported/copied files remain in the journal. Unknown source fields,
  manual legend order, overlays and visibility survive source rewrites.

Undo records live under `.undo/operations` in the retained working directory.
They survive app restart in that workspace; they are not portable history
embedded in `.ourplan`. If a storage lock prevents rollback, the pending record
and original backups remain for recovery once access is restored.

## Verification

- Build: zero warnings/errors; baseline console regression suite: 790/790;
  the added long-path raster test makes 791 total. Python metadata/crop tests:
  26 + 3 passed. The final run is recorded with release QA artifacts.
- Actual WPF command smoke passed scoped A/S, sibling isolation, measurement
  links, exact undo bytes and the recovery dialog's Retry action.
- Existing feedback UI smoke passed detached Beam/Opening, repeat tools,
  Joist note, live PDF preview and ribbon checks.
- Release QA additionally uses disposable copies of a real project with
  214 sheets, 371 takeoffs and 3,589 measurements. The viewport stress harness
  opens every sheet, returns to earlier sheets, uses tabs, zooms to 400% and pans.
  Its opt-in real-project safety harness calls real A/S and clipboard commands,
  compares Pages/Takeoffs JSON/XML hashes after Undo, checks all item quantities,
  and injects a failed move and a locked measurement read. Results and images
  are kept with local release artifacts rather than the source repository.
- The large-project run exposed native filename decoding failure above Windows
  MAX_PATH: valid raster caches were ignored, triggering slower PDF rendering
  and unnecessary raster rebuilds. `RasterBitmapFile` opens through managed
  streams instead. Regression coverage reproduces native failure at 298
  characters and verifies full/overview dimensions and pixels through the
  production cache-reader methods.

```powershell
dotnet build .\ourplancore.sln
dotnet run --project .\Tests\OurPlanCore.Tests.csproj --no-build
dotnet run --project .\Tests\OurPlanCore.Tests.csproj --no-build -- data-safety-ui-smoke
dotnet run --project .\Tests\OurPlanCore.Tests.csproj --no-build -- feedback-ui-smoke
python .\Tests\test_pdf_sheet_metadata_precise_v2.py
python .\Tests\test_pdf_sheet_metadata_crop_profiles.py
```

## Preview release policy

The `-preview` build uses a separate `OurPlanCore Preview` profile, skips legacy
profile migration and leaves the `.ourplan` file association on the stable app.
Publish compressed/self-contained/single-file x64 into a new Preview folder;
copy current installed workbook templates and create a separate shortcut.
Preserve rendering and rule presets in the separate profile. Never replace
stable update files, the old shortcut or public latest release for this task.
Verify matching publish/delivery EXE hashes and its fresh runtime log.

Next master-plan work: Phase 4, Settings and Automation Integrity.
