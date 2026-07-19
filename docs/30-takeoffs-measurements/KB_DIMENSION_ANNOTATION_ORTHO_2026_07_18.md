# KB 2026-07-18: Dimension Scale, Continuous Annotations, and Universal Ortho

Status: completed, regression-tested, published, and deployed to the local
user package.

This handoff records the final behavior and implementation boundaries for four
related viewport changes:

1. existing Ruler/Beam dimensions following the current page scale,
2. continuous multi-segment annotation lines,
3. normal selection/editing/clipboard behavior for annotations,
4. consistent Shift/F8 Ortho during creation and editing.

## User-Facing Result

### Existing dimensions follow page-scale changes

Ruler and Beam annotations no longer keep showing a value calculated from the
scale that existed when the annotation was created.

The dimension-scale rule is now:

1. use the page's current scale when it is positive and finite,
2. otherwise fall back to the valid scale stored with the annotation,
3. otherwise display the raw PDF length in points.

This makes existing automatic dimension labels update immediately after a
scale change in the main viewport and detached sheet windows. It also keeps old
job data usable when a page has no current scale.

Explicit nonempty annotation text remains literal. Only automatic dimension
text is recalculated.

Clearing the current page scale to zero intentionally exposes the saved
annotation-scale fallback; it does not erase the historical value stored in
the annotation.

### Draw Line is one continuous open polyline

The annotation `Draw Line` tool, hotkey `D`, no longer finishes after the first
two points.

- Each normal click appends another vertex.
- `C`, `Esc`, or double-click completes the current line.
- `Backspace` or `Ctrl+Z` removes the last point while the line is still being
  created.
- Completing a line leaves the Draw Line tool active for the next line.
- The saved object is one open `PageAnnotation` with `Kind="line"` and all of
  its points.

Rendering, PDF export, hit-testing, snapping, and selection-box intersection
all traverse every line segment. The final point is never connected back to
the first point.

The behavior of other annotation tools remains intentional:

- Area is a closed multi-point shape.
- Arrow, Ruler, Box, Cloud, and Highlight remain repeating two-point tools.

### Annotation selection and clipboard parity

Annotations participate in the normal viewport editing workflow.

- Clicking selects an annotation for editing.
- Annotation selection has priority when the active selection domain is
  annotations.
- Right-clicking one member of a selected annotation group preserves the group.
- `Ctrl+A` selects all annotations on the active page when annotations are the
  active domain.
- CAD box-selection rules apply:
  - left-to-right is a window selection and requires full containment,
  - right-to-left is a crossing selection and includes intersected geometry.
- A selected group can be moved together or deleted together.
- `Ctrl+C` and `Ctrl+V` copy and paste a deep-cloned annotation group.
- The viewport context menu exposes Copy, Paste, and Delete selected markups.
- Read-only viewports allow copying but block paste, delete, and geometry edits.

Pasted annotations receive:

- new annotation IDs,
- the target page folder,
- the target page's current scale when it is positive, otherwise the copied
  annotation's saved scale,
- an offset anchored from the copied group's top-left to the paste cursor,
- one group undo record,
- active selection of the newly pasted group,
- normal `PageAnnotationAdded` callbacks so persistence follows the established
  save path.

The shared viewport payload discriminator remembers which type was copied most
recently: Cut Regions, Measurements, or Annotations. Annotation geometry is
shared between viewport instances, so an annotation group can be copied to
another sheet window. Measurement clipboard content is owned by `MainWindow`;
Cut Region geometry remains local to its originating viewport. Copying
annotations or measurements also clears stale Cut Region content in the
current viewport.

### Ortho is consistent and strictly horizontal/vertical

Ortho now means strict dominant-axis horizontal or vertical projection.
Diagonal 45-degree projection is no longer used by the common Ortho resolver.
An exact horizontal/vertical tie resolves horizontally for stable behavior.

Activation rules:

- holding `Shift` forces Ortho while the key is held,
- F8 enables persistent Ortho,
- F8 Ortho plus `Shift` remains enabled,
- detached sheet viewports synchronize the F8 state with the main viewport.

The old exclusive-OR behavior (`F8 XOR Shift`) was removed. Ortho is now
enabled by `F8 OR Shift`.

The order of point resolution is also fixed:

1. project the raw cursor point onto the horizontal or vertical axis,
2. resolve snapping,
3. project the snapped result back onto the chosen axis.

An incompatible edge snap therefore cannot replace an active Ortho constraint.
The same rule is used for preview and commit.

Ortho applies to these creation paths:

- Scale,
- Ruler,
- Beam,
- annotation Draw Line, Arrow, and Area,
- takeoff Line, Area, and Joist area,
- AreaCut polygon,
- Joist direction,
- 3D roof guides.

Rectangular two-corner tools remain naturally axis-aligned without needing the
polyline resolver.

Ortho also applies during:

- measurement vertex movement,
- measurement body movement,
- annotation vertex movement,
- annotation body movement,
- annotation group movement.

Rotation retains its existing `Shift` behavior: 15-degree angle steps.

### Selection modifier rule

Plain `Shift` is reserved for Ortho and no longer means
remove-from-selection. This prevents holding Shift before mouse-down from
blocking a drag.

- `Ctrl`: add to or toggle selection.
- `Ctrl+Shift`: remove from selection.
- `Shift`: force Ortho during supported drawing/editing operations.

## Keyboard Interaction Summary

| Input | Result |
| --- | --- |
| `D` | Activate annotation Draw Line |
| Click | Append a point to the current continuous line |
| `C` / `Esc` / double-click | Complete the current line |
| `Backspace` / `Ctrl+Z` | Remove the last unfinished point |
| Hold `Shift` | Force horizontal/vertical Ortho |
| `F8` | Toggle persistent Ortho |
| `Ctrl` + select | Add to or toggle selection |
| `Ctrl+Shift` + select | Remove from selection |
| `Ctrl+A` | Select all objects in the active selection domain |
| `Ctrl+C` / `Ctrl+V` | Copy/paste the selected annotation group |
| `Delete` | Delete selected editable annotations |

## Implementation Ownership

### Dimension scale and page-scale propagation

Primary files:

- `Controls/AnnotationGlyphRenderer.cs`
- `Controls/PdfViewport.cs`
- `Controls/PdfViewport.AnnotationRendering.cs`
- `Models/PdfExporter.Annotations.cs`
- `Dialogs/DetachedSheetWindow.cs`
- `MainWindow.DetachedSheets.cs`
- `MainWindow.Utilities.cs`
- `MainWindow.PageSetup.cs`
- `MainWindow.PagesScale.cs`

`AnnotationGlyphRenderer.ResolveDimensionScale(pageScale, annotationScale)` is
the canonical dimension-scale resolver. The viewport and PDF exporter both use
it, preventing screen/export drift.

`PdfViewport.ScaleMetersPerPt` now repaints when its effective value changes.
Detached scale refreshes use `DetachedSheetWindow.RefreshPageScale` and
`MainWindow.RefreshDetachedPageScale`.

The refresh is called from every supported page-scale mutation:

- `SaveCurrentPageScale`,
- `ApplyFloatingPageSetupScale`,
- `ApplyScaleToPagesCore`,
- the detached Scale tool callback.

Scale refresh deliberately avoids calling the full measurement replacement
path, so selection and undo state are preserved.

### Continuous line geometry

Primary files:

- `Controls/PdfViewport.Tools.cs`
- `Controls/PdfViewport.ScaleDrawTools.cs`
- `Controls/PdfViewport.Input.cs`
- `Controls/PdfViewport.AnnotationRendering.cs`
- `Controls/PdfViewport.HitTesting.cs`
- `Controls/PdfViewport.Geometry.cs`
- `Controls/PdfViewport.DigitizerSnap.cs`
- `Models/PdfExporter.Annotations.cs`

The invariant is that annotation `Kind="line"` is an open polyline everywhere:
creation, viewport rendering, export, hit-testing, snapping, and box-selection
geometry must never add a closing segment.

### Annotation selection, clipboard, persistence, and undo

Primary files:

- `Controls/PdfViewport.AnnotationClipboard.cs`
- `Controls/PdfViewport.SelectionState.cs`
- `Controls/PdfViewport.BoxSelection.cs`
- `Controls/PdfViewport.ReadOnly.cs`
- `Controls/PdfViewport.CutRegions.cs`
- `MainWindow.MeasurementClipboard.cs`
- `MainWindow.ViewportContextMenu.cs`
- `MainWindow.ViewportCallbacks.cs`

`Controls/PdfViewport.AnnotationClipboard.cs` owns annotation cloning, paste
placement, group undo, pasted-group selection, and persistence callbacks.

### Ortho and editing

Primary files:

- `Controls/PdfViewport.DigitizerSnap.cs`
- `Controls/PdfViewport.SelectionEditing.cs`
- `Controls/PdfViewport.Input.cs`
- `Controls/PdfViewport.RoofGuides.cs`
- `Controls/PdfViewport.VertexSelection.cs`
- `MainWindow.DetachedSheets.cs`
- `MainWindow.ToolControls.cs`

The status/command/help wording was updated in:

- `MainWindow.xaml`
- `MainWindow.CommandPalette.cs`
- `Models/SampleJobGuideBuilder.cs`
- `Controls/PdfViewport.ToolStatus.cs`

The UI now describes Shift as forcing horizontal/vertical Ortho instead of
advertising the former 90/45-degree or toggle semantics.

## Regression Coverage

Focused regression coverage is in:

- `Tests/AnnotationOrthoRegressionTests.cs`
- `Tests/Program.cs`

The tests cover:

- dominant-axis horizontal/vertical Ortho and stable ties,
- current page-scale priority and annotation-scale fallback,
- continuous Draw Line source wiring and multi-point finalization,
- annotation group copy/paste, undo, selection, and persistence callbacks,
- last-payload clipboard routing,
- `F8 OR Shift`,
- Ortho-before-snap ordering,
- plain Shift no longer acting as a selection-removal modifier,
- annotation vertex/body/group Ortho editing,
- 3D roof-guide use of the common resolver.

## Verification Snapshot

Source:

- Branch: `feature/ourcore-design-overhaul`
- Commit: `a21310dc64d12297ed749d02d0d67982d44a5431`
- Commit subject: `Fix annotation scale and ortho editing`
- Pre-change checkpoint:
  `checkpoint/pre-dimension-annotation-ortho-20260718`
- Checkpoint target:
  `7f5438f29a3ed8b3943fc51d9450ee65e5d9bb7a`

Verification:

- `dotnet build .\ourplancore.sln`: `0 warnings`, `0 errors`
- full regression harness: `557/557` passed
- `git diff --check`: clean
- task-file scan: no conflict markers, `TODO`, `FIXME`, or `HACK`

Compressed single-file publish source:

```text
C:\Users\User\Desktop\ourplanecore\bin\publish\dimension-annotation-ortho-20260718-a21310d\ourplancore.exe
```

Installed user-facing executable:

```text
C:\Users\User\Desktop\updates\OurPlanCore\ourplancore.exe
```

Installed package identity:

- Size: `174,262,670` bytes
- ProductVersion:
  `2.2.3+a21310dc64d12297ed749d02d0d67982d44a5431`
- SHA-256:
  `627548A2E061A80D50D8DFD67BB35DF1419FABC3838345E3637AF806F60CABF1`
- Publish-source and installed hashes matched.

Desktop shortcut:

- Shortcut: `C:\Users\User\Desktop\OurPlanCore.lnk`
- Target: `C:\Users\User\Desktop\updates\OurPlanCore\ourplancore.exe`
- Working directory: `C:\Users\User\Desktop\updates\OurPlanCore`

Packaged runtime validation:

- Log: `%APPDATA%\OurPlanCore\logs\app-20260718.log`
- Checked startup marker:
  `2026-07-18T16:00:50.8596355-03:00`
- Process remained alive after 40 seconds.
- The scoped startup block contained zero `ERROR` entries.
- `Loaded takeoffs` and `Viewport` records were present.

## Rollback

The existing package rollback was preserved without replacement:

- File:
  `C:\Users\User\Desktop\updates\OurPlanCore\ourplancore.exe.bak`
- SHA-256:
  `2802DBFBE6F1B8FBE3B6581D215DE71ECB7FA4F4B4251989119FE0623CF47FE8`

The immediately previous deployed EXE was also preserved:

- File:
  `C:\Users\User\Desktop\updates\OurPlanCore\ourplancore.exe.pre-dimension-annotation-ortho-20260718.bak`
- SHA-256:
  `814CBB39EBC4ED22440A3811F90F5EE7509887B45B050AADD30E63F0CB222D3F`

Source rollback can use the checkpoint tag:

```text
checkpoint/pre-dimension-annotation-ortho-20260718
```

## Future Regression Guardrails

Keep these invariants when changing the same code:

1. A valid current page scale always wins over the annotation's saved scale.
2. The saved annotation scale remains a fallback when the current page scale
   is missing or zero.
3. Viewport and PDF export use the same dimension-scale resolver.
4. Annotation line geometry remains open throughout every geometry consumer.
5. Ortho activation is `F8 OR Shift`, never exclusive-OR.
6. Ortho projection happens before snapping, and the final snapped point stays
   on the selected axis.
7. Plain Shift must not remove selection or prevent an edit drag.
8. Annotation paste is one group undo operation and must fire normal
   persistence callbacks.
9. Clipboard routing uses the most recently copied supported payload type;
   remember that Cut Region geometry itself is viewport-local.
10. Detached viewports receive current page-scale and F8 Ortho state changes.

## Quick Manual Smoke

Use this checklist after future edits touching scale, annotations, snapping, or
selection:

1. Draw an automatic Ruler, change the page scale, and confirm the existing
   label changes immediately.
2. Export the page and confirm the PDF dimension matches the viewport.
3. Repeat the scale change with the sheet open in a detached window.
4. Press `D`, place at least four points, complete with `C`, and confirm one
   open line is saved.
5. Repeat using double-click and `Esc`; use `Backspace` and `Ctrl+Z` before
   completion to remove unfinished points.
6. Select the continuous line by each of its segments and by both CAD box-drag
   directions.
7. Select several annotations, move them, copy/paste them, delete them, and
   undo each group operation.
8. Copy annotations in one sheet window and paste them into another.
9. With F8 off, hold Shift during drawing and editing; confirm strict
   horizontal/vertical movement.
10. With F8 on, also hold Shift; confirm Ortho remains active.
11. Repeat near snap candidates and confirm preview and committed geometry
    remain on the selected axis.
12. Confirm `Ctrl` toggles/adds selection, `Ctrl+Shift` removes selection, and
    plain Shift does neither.
13. Confirm rotation still uses Shift for 15-degree steps.
