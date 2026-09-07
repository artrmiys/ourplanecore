# PDF Full Render Cache Handoff - 2026-05-28

## Goal

After the low-scale PyMuPDF preview cache, repeated page opens could still feel
slow because the viewport still had to run the normal/full-scale PyMuPDF render
after the first preview frame.

This slice adds a bounded persisted cache for clean PyMuPDF rerenders, so repeat
opens and repeat zoom render scales can reuse the previous clean render without
waiting on the single Python worker.

## Scope

Changed only the clean render path:

- no PDF layer states hidden;
- no highlighted PDF layers;
- render scale between the instant preview scale and `2.25`;
- estimated rendered pixels at or below `30,000,000`;
- PNG bytes at or below `96,000,000`.

The cache still uses the existing persisted cache root:

- `%LOCALAPPDATA%\OurPlaneCore\render-cache\pymupdf-preview`
- override env var: `OURPLANECORE_PDF_PREVIEW_CACHE_ROOT`

The folder name is kept for backward compatibility with the earlier preview
cache.

## Implementation

Files changed:

- `Models/PdfPreviewRenderCache.cs`
  - added `TryReadCleanRender` / `TryWriteCleanRender`;
  - kept `TryReadCleanPreview` / `TryWriteCleanPreview` as wrappers;
  - added `IsCleanRenderRequest`;
  - added bounded scale/pixel/byte gates;
  - persisted layer metadata with `LayersCaptured`.
- `Models/PdfLayerRenderService.cs`
  - marks successful PyMuPDF render responses as `LayersCaptured = true`;
  - writes all bounded clean renders, not only the `0.35` preview.
- `Controls/PdfViewport.Layers.cs`
  - tries persisted clean render cache before queueing the PyMuPDF worker for
    non-reset layer renders;
  - does not bypass worker layer discovery when cached layer metadata is
    unknown;
  - logs cache hits as `Viewport PyMuPDF render cache hit`.
- `Tests/Program.cs`
  - extended cache round-trip coverage for normal clean render scale.
- `Tests/TakeoffsTreeRegressionTests.cs`
  - added source regression for the full-scale cache before worker queueing and
    for bounded clean-only cache rules.

## Safety Rules

- Initial page load still queues the worker render/discovery path; the new cache
  is for non-reset clean rerenders.
- Hidden layer states and layer highlights never use this clean cache.
- Unknown layer metadata does not bypass worker discovery.
- Existing temp-file fallback and inline PNG protocol are unchanged.
- No worker-pool, named pipe, shared memory, or new dependency was added.

## Verification

Checkpoint before code:

- `checkpoint/before-full-render-cache-20260528-184340`

Commands/checks run from `C:\Users\User\Desktop\ourplanecore`:

```powershell
git diff --check
rg "TODO|throw new NotImplementedException|<<<<<<<|>>>>>>>|=======" -g "!bin/**" -g "!obj/**" -g "!cache/**" -g "!reference/**"
dotnet build .\ourplanecore.sln /p:OutDir=.\cache\verify_build\ /p:UseAppHost=false
dotnet run --project .\Tests\OurPlaneCore.Tests.csproj
dotnet publish .\ourplanecore.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o .\bin\publish
```

Results:

- `git diff --check`: pass.
- Conflict/TODO scan: no actionable matches.
- Verify build: pass, `0 warnings / 0 errors`.
- Regression tests: pass, `244/244`.
- Compressed single-file publish: pass.

## Deployment

Published exe:

- `C:\Users\User\Desktop\ourplanecore\bin\publish\ourplanecore.exe`

Deployed exe:

- `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`

Package details:

- SHA256:
  `BB5AE3C191CC79283BA9407271DE16EEF6E1C3E987D0F06B446C7C0E0BBC85F6`
- size: `176,559,250` bytes
- existing rollback file preserved:
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe.bak`

Shortcut check:

- target:
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`
- working directory:
  `C:\Users\User\Desktop\updates\OurPlaneCore`

Packaged launch/log check:

- launched deployed exe twice from the update folder;
- process alive after wait both times;
- errors after latest startup: `0`;
- `Loaded takeoffs` signal present.

The hidden packaged smoke did not open a sheet far enough to produce a viewport
render log entry, so it did not observe a live `Viewport PyMuPDF render cache
hit`. That is expected for this launch-only smoke. Manual verification path:

1. Open the packaged app.
2. Open the same PDF sheet once and wait for the normal render.
3. Reopen the same sheet or zoom back to the same render scale.
4. Check `%APPDATA%\OurPlaneCore\logs\app-20260528.log` for:
   `Viewport PyMuPDF render cache hit`.

## Commits

- Code commit: `7b03854 Cache clean PDF rerenders`
