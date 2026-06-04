# PDF Exterior Contour Snap Handoff - 2026-06-04

Status: NOT DONE. The latest build is better than the previous one, but the
user still considers the contour result unacceptable. Continue from this file
before touching code again.

## User Rule

The priority order must be:

1. Build the exterior building footprint / exterior wall contour first.
2. Only after the exterior contour is stable, consider interior contours.
3. Interior doors must not become selected contours, area polygons, or fallback
   polylines.
4. A large bridge value may help continue through exterior windows/openings, but
   it must not glue unrelated figures or hallucinate random shapes.

In the user's words after the last deploy: "better, but still bad; need to keep
working." Do not treat the current result as finished.

## Current Deployed Baseline

Latest relevant commits:

- `3bc2bb1 Add aggressive PDF contour cycles`
- `78f6426 Guard PDF contour bridge selection`
- `0b9a81f Prioritize exterior PDF contours`

Latest deployed exe:

- Path: `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`
- SHA256: `FF7BA0F24EA35820A8C03299205B6A42104EC12B274E871D392DE02193893D18`
- Backup made before deploy: `ourplanecore.exe.bak_20260604_010949`

Verification already done for that deploy:

```powershell
dotnet build .\ourplanecore.sln -c Release
dotnet run --project .\Tests\OurPlaneCore.Tests.csproj -c Release --no-build
```

Result at deploy time: build 0 warnings / 0 errors, tests `289/289`.
Runtime smoke: app started from deployed exe, no `ERROR` after latest
`Application startup.`, current viewport opened `a203 2nd`.

## Current Code Ownership

Main files:

- `Controls/PdfViewport.EdgeSnap.cs`
  - Finds PDF edge snap candidates.
  - Closed-boundary search now uses `searchTolerance = max(snap tolerance, bridge)`.
  - Candidate limit is larger for closed-boundary mode.
  - If selected PDF segment looks like an interior door, it rejects fallback
    open polylines and accepts only a contour that passes exterior-support checks.

- `Controls/PdfViewport.EdgeSnapContour.cs`
  - Builds graph/raster/envelope boundary contours.
  - Graph bridge is guarded; it no longer blindly uses `bridge * 3` up to 240pt.
  - Bridge candidate must be directional, low lateral drift, supported by
    incident segment length, and not ambiguous.

- `Controls/PdfViewport.EdgeSnapWallCore.cs`
  - Builds dense wall-core component.
  - Calls the door-symbol filter for sparse/interior wall-core segments.

- `Controls/PdfViewport.EdgeSnapDoor.cs`
  - New partial for door detection and door-selected contour rejection.
  - Handles narrow paired door lines and fragmented a203-style swing arcs.
  - Rejects small door/raster capsules unless contour has real exterior side
    support.

Tests:

- `Tests/Program.cs`
  - `PdfRasterEdgeSnapBridgesSmallEndpointGaps` now includes large bridge,
    interior door, a203-like fragmented door, and door-selected interior capsule
    regression cases.
- `Tests/TakeoffsTreeRegressionTests.cs`
  - Wiring checks for expanded closed-boundary candidate search and door partial.

## Real Sample Context

Current working sample observed from runtime log:

- Job: `C:\Users\User\Desktop\Takeof_desctop\90. The Avenue_Bliffert`
- Page: `Pages\00. imported\Arch\a203 2nd`
- Source page: `a203 2nd`, PDF page index 21, page size `3024 x 2160 pt`
- Raster snap index:
  `C:\Users\User\Desktop\Takeof_desctop\90. The Avenue_Bliffert\Pages\00. imported\Arch\a203 2nd\raster\snap.json`
- Snap index size at that time:
  - `snap_point_count`: 23005
  - `snap_segment_count`: 14795
  - `snap_black_only`: true

Important finding from real `a203 2nd` data:

- Door swings are not represented as clean arcs.
- They are usually many tiny diagonal or near-axis segments, often around
  4-17pt, and some pieces have `dx <= 2.5pt`, so a loose axis classifier can
  accidentally classify a swing piece as vertical/horizontal.
- Narrow paired door lines can be separated by about `0.96pt`, so a minimum
  pair separation of `1.0pt` misses real doors.

Example real-like cluster used in tests:

- Around `x=1490..1511`, `y=721..745` on `a203 2nd`.
- Door leaf segments around:
  - `(1509.72,721.44) -> (1509.72,745.44)`
  - `(1510.68,721.44) -> (1510.68,745.44)`
- Swing fragments around:
  - `(1506.00,721.92) -> (1510.68,721.44)`
  - `(1501.56,723.24) -> (1506.00,721.92)`
  - `(1497.36,725.52) -> (1501.56,723.24)`
  - `(1493.76,728.40) -> (1497.36,725.52)`
  - `(1490.76,732.12) -> (1493.76,728.40)`

## What Is Still Wrong

The latest implementation is still too heuristic. It can reject obvious door
capsules, but the user still sees poor contour behavior in real use.

Likely remaining failure modes:

1. Candidate ranking still starts too locally. It may choose an interior line
   candidate before a reliable exterior candidate, then reject it, but not
   always recover to the right exterior footprint.
2. Exterior footprint detection is not yet a first-class solve. It is still a
   candidate-by-candidate contour pass, not a global "find the exterior building
   outline" pass.
3. Door filtering is reactive. It detects some door symbols, but it does not
   fully remove door geometry from the graph before exterior tracing.
4. Large bridge values can still change topology too much. Guarded bridge helps,
   but the algorithm still mixes "close gaps" and "connect graph components".
5. Raster boundary pass can create thick capsules around interior wall/door
   clusters. The code rejects some of these, but the approach is still brittle.

## Next Implementation Plan

Do not start with more random thresholds. Start by separating the contour modes:

1. Add a true `ExteriorFootprint` solve path for Area + PDF snap.
   - Input: all strict black raster/PDF segments on current sheet.
   - Output: one or more large candidate exterior loops.
   - It should rank by bounds, area, exterior-side support, distance to cursor,
     and wall-density support.
   - It should not be seeded only by the nearest segment.

2. Pre-filter or downweight interior door symbols before tracing.
   - Detect narrow paired leaf lines plus fragmented swing clusters.
   - Remove these segments from the exterior solve graph.
   - Keep them available only for future interior/annotation workflows, not for
     exterior footprint area.

3. Split bridge semantics.
   - `GapCloseBridge`: closes openings/windows inside a selected exterior wall
     band or raster dilation.
   - `ComponentJoinBridge`: much stricter; should not connect distant figures.
   - UI slider can drive both, but with separate caps and scoring.

4. Add a diagnostic overlay before more threshold tuning.
   - Show candidate number, mode, score, rejected reason, bounds, area, and
     whether it was rejected as door/interior/capsule.
   - This is needed because current visual feedback only shows the final bad
     result, not why alternatives were skipped.

5. Add replay fixtures from real pages.
   - Use `a203 2nd` first.
   - Also test `a204` and `a250`, because the user previously asked to collect
     contours there.
   - Save small synthetic fixtures in tests only after understanding the real
     failure; avoid inventing overly clean rectangles.

## Immediate Reproduction Checklist

1. Open:
   `C:\Users\User\Desktop\Takeof_desctop\90. The Avenue_Bliffert`
2. Open page:
   `Pages\00. imported\Arch\a203 2nd`
3. Enable PDF Snap / raster snap as before.
4. Area tool, press Tab through PDF contour modes.
5. Check:
   - Does first useful contour prefer exterior footprint?
   - Does it avoid interior doors?
   - Does increasing bridge continue through exterior windows without selecting
     random interior shapes?
   - Does it avoid small capsules around interior wall/door clusters?

## Rule For The Next Agent

Do not call this finished just because tests pass. The user's real acceptance
condition is visual: on real sheets, Tab should quickly produce an approximate
full exterior building contour, and interior doors must not be selected as
contours. If the result is still visually wrong, keep iterating.
