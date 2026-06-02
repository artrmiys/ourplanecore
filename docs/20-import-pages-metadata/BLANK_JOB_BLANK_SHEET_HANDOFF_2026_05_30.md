# Blank Job / Blank Sheet Handoff - 2026-05-30

## Scope

- Added a no-PDF job creation path.
- Added blank sheet creation inside an existing job.
- Kept existing PDF import behavior intact:
  - `PDF job` / `New Job from PDF Folder...` still uses the folder-first recursive PDF import path.
  - `Blank job` creates only the normal OurPlaneCore job structure.
  - `Blank Sheet` creates an internal generated blank PDF, then stores the page through the existing `source.json` page contract.

## User-Facing Entry Points

- Open Job dialog:
  - `PDF job` creates a job from a folder of PDFs.
  - `Blank job` creates an empty job without selecting PDFs.
- `Open / Import` menu:
  - `New Job from PDF Folder...`
  - `Blank Job...`
- Page tab:
  - `Blank Sheet`
- Pages side panel:
  - `Blank Sheet`
- Pages tree context menu:
  - folder rows: `New Blank Sheet`
  - page rows: `New Blank Sheet in Parent`
- Command palette:
  - `Blank Job`
  - `Blank Sheet`

## Implementation

- Code commit: `5a2eb9e Add blank job and sheet`.
- New internal PDF generator:
  - `Models/BlankPagePdfService.cs`
  - default blank sheet size: `36 in x 24 in` landscape (`2592 x 1728` PDF points).
- Storage API:
  - `PageStore.CreateBlankPage(...)`
  - `OurPlaneCoreJobStore.CreateBlankPage(...)`
- Blank pages:
  - create a generated `*.blank.pdf` under the job `sources` folder;
  - write normal page `Data.xml`;
  - write normal `source.json` with relative PDF path;
  - write `source_pdf.json` with `source = manual-blank`, dimensions, sheet label, and rename candidate;
  - start unscaled, so Page Setup / Scale workflow remains explicit.
- Blank jobs:
  - reuse `OurPlaneCoreJobStore.CreateJob(...)`, so base folders, `AI_Context`, `Pages`, `Takeoffs`, and `sources` are created exactly like normal jobs.

## Verification

- `dotnet build .\ourplanecore.sln`
  - `0 warnings / 0 errors`
- `dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build`
  - `250/250 tests passed`
  - new coverage: `blank page creation writes renderable pdf and metadata`
- Compressed publish:
  - `dotnet publish .\ourplanecore.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o .\publish\ourplanecore-compressed-blank-job-20260530`
- Deployed package:
  - `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`
  - size: `176589088`
  - SHA256: `D9B24F614A91D80A35F340A06823BF06C114B11659697C379EAF9FC40E296A0C`
  - source/target hash match: yes
  - existing `ourplanecore.exe.bak` kept
- Desktop shortcut:
  - target: `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`
  - working directory: `C:\Users\User\Desktop\updates\OurPlaneCore`
- Packaged launch validation:
  - launched deployed exe;
  - process alive after 25 seconds;
  - latest log section after `Application startup.` had `0` errors;
  - `Loaded takeoffs` marker present;
  - `Viewport` marker present.

## Optimization Status

- No risky render-pipeline optimization was included in this feature slice.
- Current log evidence from `app-20260530.log` still points to viewport PDF rendering as the main visible bottleneck:
  - repeated `Viewport slow layer render` entries, usually hundreds of ms and sometimes over `1s`;
  - packaged validation showed `Viewport PyMuPDF preview cache hit`, then a `Viewport slow layer render 1057ms`;
  - one slow frame showed `inProgress:81ms` with `154` active measurements.
- Practical next optimization candidates:
  - reduce redundant PyMuPDF/layer worker passes when cached clean renders already satisfy the current layer/highlight state;
  - examine in-progress/overlay drawing cost on dense pages;
  - keep tree reload changes separate because recent logs do not show tree load as the primary lag during viewport navigation.

## Notes

- The existing untracked file `Assets/ourplanecore.ico.bak_20260522_132816` was left untouched.
- Blank sheet generation intentionally uses the existing PDF-backed page contract instead of a separate non-PDF page type, to avoid splitting viewport/export/measurement code paths.
