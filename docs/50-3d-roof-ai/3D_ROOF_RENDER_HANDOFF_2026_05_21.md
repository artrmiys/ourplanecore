# 3D Roof Render Handoff - 2026-05-21

## Current Status

The active OurPlaneCore roof build is deployed to:

`C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`

The Desktop shortcut was checked and points to that same update exe:

- Shortcut: `C:\Users\User\Desktop\OurPlaneCore.lnk`
- Target: `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`
- Working directory: `C:\Users\User\Desktop\updates\OurPlaneCore`

Important runtime note: during the last check there was an already-running
`ourplanecore.exe` process from the update folder. That can make the user see
the previous visual state even after the file has been replaced. The process was
gone before the final deploy and the fresh packaged exe was log-verified.

## User Problem Being Addressed

The user reported that the generated roof geometry builds correctly, but the 3D
view still looked visually broken:

- roof color was not clearly visible and read as gray;
- faces still showed lines inside planar roof surfaces;
- it looked like old or stale behavior might be coming from the Desktop shortcut.

## Implemented Behavior

### Roof face color

`MainWindow.ThreeDWalls.cs` now renders roof planes with a dedicated visible roof
color path:

- `AddThreeDRoofPlaneMesh(...)` uses `ToVisibleRoofColor(...)`, not the general
  subdued wall/slab tint.
- roof plane opacity is almost solid:
  - selected roof group: `1.0`
  - non-selected roof group: `0.96`
- `CreateRoofFaceMaterial(...)` creates a `MaterialGroup` with:
  - `DiffuseMaterial`
  - light `EmissiveMaterial` at `0.18` opacity

Reason: WPF 3D lighting was making the roof read too gray even when the source
color was correct. The small emissive component keeps the color visible without
making it glossy.

### Interior planar lines

The latest render pass removes the two likely causes of visible internal planar
lines:

- `TryAddProjectedRoofTriangles(...)` no longer adds each triangle twice in
  reverse order.
- `AddFanRoofTriangles(...)` no longer adds each fan triangle twice in reverse
  order.

The roof still uses `BackMaterial`, so back-side visibility is preserved without
duplicating coplanar triangles. This avoids z-fighting that can show as broken
lines inside one roof plane.

### Roof edge overlay

`AddThreeDRoofPlaneMesh(...)` now draws extra edge bars only for boundary edges:

- outer roof perimeter: drawn;
- interior plane edges / shared intersections: not drawn as extra bars;
- triangulation diagonals: not drawn.

Reason: the user asked to stop seeing "plans" or lines inside the plane. Real
roof shape changes should come from the mesh surface and lighting, not from
extra bars laid on top of every plane edge.

### Face normals

`ApplyFlatFaceNormals(...)` assigns one averaged normal to each roof face and
flips it upward when needed:

- all triangles in the same roof face shade identically;
- triangulation diagonals should not appear through per-triangle lighting;
- upside-down normals should not make a face look dark/gray.

## Relevant Commits

Latest roof render commits on `feature/area-vertex-editing`:

- `99d914f` - `Clean 3D roof surface rendering`
- `a5d795a` - `Fix 3D roof face lighting`
- `02bb5ce` - `Make 3D roof clearly colored, not washed gray`
- `e42e52a` - `Tune 3D roof look: stronger tint, solid body, orbit pivot above model`
- `e0e1be7` - `Clean up 3D roof rendering: stable center, flat faces, edge-only lines`
- `32197e9` - `Use matte monochrome-tinted clean mesh for 3D model`
- `5deada6` - `Polish 3D roof side viewport`

## Relevant Files

- `MainWindow.ThreeDWalls.cs`
  - roof mesh material, opacity, normals, triangulation, edge overlay
  - wall/slab/roof 3D scene building
  - roof hit registration
- `MainWindow.ThreeDViewer.cs`
  - main and side 3D viewport input paths
  - roof move/gizmo drag paths
  - 3D camera fit/orbit support
- `MainWindow.ThreeDSidePanel.cs`
  - compact side 3D panel
  - roof edge pitch controls
  - Generate Roof / Move Roof / Reset Pos controls
- `Models/ThreeDRoofRenderBoundaryEdges.cs`
  - boundary edge detection for roof render cleanup
- `Models/ThreeDRoofSurface.cs`
  - downstream roof surface height dependency for walls
- `Tests/RoofProbeTests.cs`
  - current roof geometry regression probes

## Verification Completed

Commands run from `C:\Users\User\Desktop\ourplanecore`:

```powershell
dotnet build .\ourplanecore.sln
dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build
dotnet publish .\ourplanecore.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o .\bin\publish
```

Results:

- build: success, `0 warnings / 0 errors`
- tests: `212/212 tests passed`
- publish: success

Final deployed exe:

- path: `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`
- size: `175112015`
- SHA256: `DAD7F17E135ED338EB2967F608B5CD27053C0F1B8125E5C9C7038B0AB049168C`
- backup created: `ourplanecore.exe.bak-20260521-125400`

Launch verification:

- packaged exe was launched from the update folder;
- process stayed alive after 25 seconds;
- log checked:
  `C:\Users\User\AppData\Roaming\OurPlaneCore\logs\app-20260521.log`
- after the last `Application startup.` marker:
  - `ERROR` count: `0`
  - `Loaded takeoffs tree` present
  - `Viewport` line present
- the verification process was closed after the log check.

## Known Local Worktree Noise

Unrelated dirty state existed and was intentionally not touched:

- many deleted files under `Tools/python_deps/...`
- untracked `.claude/`

Do not revert or commit those as part of roof rendering work unless the user
explicitly asks.

## Product Rule For Next Roof Render Work

PlanSwift/Revit-style expectation:

- the model should read as clean shaded massing, not as exposed triangulation;
- roof color must be clearly visible;
- outer roof boundary can be outlined;
- generated construction/guide lines should not clutter completed roof faces;
- if the user says the Desktop shortcut looks old, check the running process and
  shortcut target before changing geometry again.

## If The User Still Sees Internal Lines

Check this order before changing roof geometry:

1. Confirm no old `ourplanecore.exe` process is still running.
2. Confirm `C:\Users\User\Desktop\OurPlaneCore.lnk` targets the update exe.
3. Confirm the update exe hash is
   `DAD7F17E135ED338EB2967F608B5CD27053C0F1B8125E5C9C7038B0AB049168C` or newer.
4. In `MainWindow.ThreeDWalls.cs`, inspect `AddThreeDRoofPlaneMesh(...)` and do
   not reintroduce reverse duplicate coplanar triangles.
5. If some lines are still needed for real ridges/hips/valleys, draw them from
   generated seam guides as a separate optional overlay, not as per-plane bars
   on every polygon edge.
