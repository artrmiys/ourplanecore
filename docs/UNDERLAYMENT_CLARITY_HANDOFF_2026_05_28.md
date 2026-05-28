# Underlayment Clarity Handoff - 2026-05-28

## Scope

User report:

- sheet underlayment/overlay looked blurry in the viewport.

Code inspection found no separate `underlayment` renderer. The matching
visible path is sheet-to-sheet overlay drawing in:

- `Controls/PdfViewport.SheetOverlay.cs`
- method: `DrawSheetOverlay(SKCanvas canvas)`

That overlay bitmap is the underlay-style reference sheet drawn below takeoffs
and markups.

## Change

Changed sheet overlay bitmap sampling from:

- fast navigation frame: `SKFilterQuality.Low`
- normal frame: `SKFilterQuality.Medium`
- bitmap antialiasing: enabled

to:

- fast navigation frame: `SKFilterQuality.Medium`
- normal frame: `SKFilterQuality.High`
- bitmap antialiasing: disabled

Reason:

- sheet overlays are alignment/reference drawings, so crisp linework matters
  more than soft bitmap smoothing;
- keeping fast-frame at `Medium` avoids the obvious temporary blur while
  panning/zooming;
- using `High` on settled frames improves small text and line clarity without
  changing the main page renderer.

## Files Changed

- `Controls/PdfViewport.SheetOverlay.cs`
  - sharpened `DrawSheetOverlay` paint settings.
- `Tests/TakeoffsTreeRegressionTests.cs`
  - added `SheetOverlayRenderingUsesSharperSampling`.
- `Tests/Program.cs`
  - registered the new regression test.

## Verification

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
- Regression tests: pass, `243/243`.
- Compressed single-file publish: pass.

## Deployment

Published exe:

- `C:\Users\User\Desktop\ourplanecore\bin\publish\ourplanecore.exe`

Deployed exe:

- `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`

Package details:

- SHA256:
  `9DCFEF64D6F8F75C62BEE38A7850981F9550102FFE1DCBC328CBAC8BEE756211`
- size: `176,558,214` bytes
- existing rollback file preserved:
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe.bak`

Shortcut check:

- target:
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`
- working directory:
  `C:\Users\User\Desktop\updates\OurPlaneCore`

Packaged launch/log check:

- launched deployed exe from the update folder;
- process alive after wait;
- latest log marker: `Application startup.`;
- errors after latest startup: `0`;
- viewport/takeoff load signals after startup: present.

## Commits

- Code commit: `efb0bd1 Sharpen sheet overlay rendering`

## Caveat

This change sharpens the sheet overlay/underlay bitmap rendering. It does not
change the primary PDF page bitmap renderer, PyMuPDF render scale, persisted
preview cache, or PDF export overlay path.
