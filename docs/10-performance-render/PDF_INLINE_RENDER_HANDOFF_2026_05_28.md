# PDF Inline Render Handoff - 2026-05-28

## Current Status

Implemented the safer portable version of the render round-trip optimization:
bounded PNG render images can now travel inline in the existing JSON
PyMuPDF-worker response instead of always being written to a temp `page.png`
and read back by C#.

This is designed for distribution to other users:

- no named pipes;
- no shared memory;
- no new runtime dependency;
- no absolute local path requirement;
- the same bundled `Tools\python\python.exe` and `Tools\pdf_layers_helper.py`
  path resolution are used;
- the old temp-file PNG path remains as fallback for large renders and older or
  unexpected helper behavior.

Git checkpoint before the risky change:

- `checkpoint/before-inline-png-render-20260528-175239`
- Checkpoint target: `a69b408 Document page open UI perf cleanup`

Code commit:

- `a6d55e0 Inline bounded PDF render images`

## Scope

The C# render request now sends:

- `inline_image = true`
- `inline_image_max_pixels = 3000000`
- the existing `image` temp path

The Python helper behavior:

- if inline is requested and the rendered pixmap is within the pixel limit, it
  returns `image_base64` containing PNG bytes from `Pixmap.tobytes("png")`;
- if the render is above the limit, inline conversion fails, or inline is not
  requested, it writes the old temp PNG path using `base.save(image_path)`;
- if neither inline nor file output is possible, it returns a normal
  `{ ok: false, error: ... }` response.

The C# response reader behavior:

- prefers `response.ImageBase64` when present;
- decodes it with `Convert.FromBase64String`;
- otherwise reads `response.Image` with `File.ReadAllBytes(...)`;
- reports the same user-facing render failure if neither exists.

## Files

- `Models/PdfLayerRenderService.cs`
  - Added `InlineRenderImageMaxPixels`.
  - Added inline request fields to `RenderRequest`.
  - Added `ImageBase64` to `RenderResponse`.
  - Added `TryReadRenderImageBytes(...)` to choose inline bytes first and temp
    file fallback second.
- `Tools/pdf_layers_helper.py`
  - Added base64 support.
  - Added `_render_image_payload(...)`.
  - Keeps `base.save(image_path)` fallback.
- `Tests/Program.cs`
  - Registered the portable inline protocol regression.
- `Tests/TakeoffsTreeRegressionTests.cs`
  - Added source-level coverage that C# requests/decodes inline PNG data, keeps
    file fallback, and Python helper supports both inline and fallback output.

## Verification

Commands run from `C:\Users\User\Desktop\ourplanecore`:

```powershell
.\Tools\python\python.exe -m py_compile .\Tools\pdf_layers_helper.py
git diff --check
dotnet build .\ourplanecore.sln
dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build
dotnet publish .\ourplanecore.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o .\bin\publish
```

Results:

- Python helper compile: passed.
- `git diff --check`: pass; only CRLF normalization warnings.
- `dotnet build`: pass, `0 warnings / 0 errors`.
- Regression tests: pass, `242/242 tests passed`.
- Direct helper smoke: passed.
  - small synthetic PDF returned inline `image_base64` PNG data without writing
    fallback `page.png`;
  - forced low pixel limit used the fallback PNG file path.
- Publish: pass, compressed single-file output created in `bin\publish`.

Final deployed package:

- Path: `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`
- Size: `176,558,203` bytes
- SHA256:
  `FDE0FCF63BF5C3B7555DE5C4DB9C0C0CC1190B1E7341BE11DED326E547A26EDE`
- Publish/update SHA256 matched.
- `ourplanecore.exe.bak`: exists and was preserved, size `417,459,157` bytes.
- Desktop shortcut target:
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`
- Desktop shortcut working directory:
  `C:\Users\User\Desktop\updates\OurPlaneCore`

Packaged app validation:

- First startup smoke from the update package:
  - process alive;
  - `0` errors after latest `Application startup`;
  - `Loaded takeoffs` present.
- Packaged viewport smoke:
  - temporarily created a one-page synthetic job under `%TEMP%`;
  - temporarily pointed app settings at that job, then restored settings;
  - ran the deployed update exe with
    `OURPLANECORE_VIEWPORT_PAGE_STRESS_SMOKE=1`;
  - report result: passed;
  - page count: `1`;
  - open results: `1`;
  - failures: `0`;
  - log after latest `Application startup`: `0` errors, `Loaded takeoffs`
    present, `Viewport slow layer render` present.

## Remaining Performance Work

Still not changed in this slice:

- global `WorkerSemaphore = new(1, 1)` still serializes all PyMuPDF traffic;
- hidden-layer renders still reopen/rewrite a temporary fitz document path;
- `Bitmap.Copy()` on Docnet in-memory cache hits remains unchanged;
- very large renders still use the temp PNG fallback by design to avoid sending
  huge base64 strings through the worker line protocol.

The next safe performance candidate is either a carefully bounded worker-pool
split for independent PyMuPDF actions, or caching/reusing hidden-layer filtered
documents. Both are riskier than this inline protocol because they can affect
ordering, memory pressure, and PDF-layer state.

## Notes

- The unrelated untracked file
  `Assets/ourplanecore.ico.bak_20260522_132816` was not touched.
