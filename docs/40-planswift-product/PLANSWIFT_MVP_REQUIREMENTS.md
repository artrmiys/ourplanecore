# PlanSwift MVP Requirements

Status annotation: 2026-09-06, OurPlanCore 2.2.7-preview. This document preserves
the original core product contracts; it is not an unimplemented task list.
[Current availability](../CURRENT_OURPLANECORE_STATUS.md) and the
[command map](../60-ux-ui/WORKSPACE_TAB_COMMAND_MAP.md) take precedence for UI guidance.

## MVP Goal

Build OurPlanCore as a practical local takeoff tool that follows the core
PlanSwift workflow closely enough for real estimating work: open a job, import
pages, set scale, draw Count/Linear/Area takeoffs, save, reopen, and export
quantities.

## Must Have

### Jobs and Pages

Acceptance criteria:

- User can create a local project. Current new projects use `.ourplan`.
- User can open a `.ourplan` project or an existing folder job. Save As retains
  the opened format; folder compatibility is not a second new-project format picker.
- User can import a PDF into page folders.
- User can select a page from the Pages tree.
- User can organize page folders without corrupting source metadata.

### Scale

Acceptance criteria:

- Each page stores its own scale.
- User can set scale by ratio.
- User can set scale from a known drawn calibration line.
- Linear and Area tools warn or prompt when page scale is missing.
- Existing measurements store the scale used to create them.

### Takeoff Items

Acceptance criteria:

- User can create fixed-type Count, Linear, and Area items.
- Tool selection cannot put geometry into the wrong item type.
- Item name, color, type, and total are visible.
- Multiple measurements can belong to one item.
- Measurements can be saved and reopened.
- Saved measurements must remain visible after sheet auto-name / folder
  organization. If a measurement points to a stale imported `Page N` folder, the
  app repairs it by matching to a unique current `source.json` PDF page index.

### Drawing

Acceptance criteria:

- Count: each click adds one visible marker.
- Linear: two or more points create a measured path.
- Area: three or more points create a measured polygon.
- User can undo last point while drawing.
- User can finish and cancel drawing intentionally.
- Active recording state is visually obvious.
- User can toggle Snap to magnetize to existing app-created points.
- User can toggle Ortho to constrain Line/Area to 90/45-degree axes.
- User can select and edit saved measurements: dragging a blue handle moves one
  vertex, and dragging the measurement body moves the whole saved measurement.
- Editing an existing saved measurement must win over active Record input so a
  user can correct old geometry without first switching tools.
- User can switch to Select mode, drag a left-button box around saved
  measurements, copy the selected set, and paste it onto the same or another
  sheet.
- Paste must ask whether the copied geometry should stay linked to the same
  takeoff items/values or create new copied takeoff items.

### Output

Acceptance criteria:

- Totals update after drawing.
- Totals respect metric/imperial setting.
- CSV export includes item rows and measurement rows.
- Saved data reopens with the same geometry, page links and quantities. Invalid
  or inaccessible records produce visible protected-file recovery rather than
  silent replacement with empty data.

## Post-MVP capabilities now implemented

- User-facing Count terminology (the persisted internal type can remain `point`).
- Active-target Record control and matching-type/scale checks.
- Estimating tables with sections, current-sheet filtering, notes and export.
- Section naming and selection for measurements under an item.
- Detached page windows with page-specific drawing/editing/clipboard behavior.
- Joist Area, Beam/Openings, P Line, Similar and separate repeat Line/Beam tools.
- Editable shortcut defaults and a separate Settings dialog; selection mirrors
  are assignable, preserving the original unassigned default.

Further polish still requires concrete user scenarios; implementation does not
mean every PlanSwift behavior has been reproduced or every UI command tested.

## Not Yet

- Full PlanSwift XML parity.
- Assemblies, parts, formulas, or cost databases.
- Full parity with PlanSwift specialty tools (implemented Joists, Beam/Openings,
  roof/rafter tools and Similar do not prove Grid or arbitrary single-click parity).
- Separate horizontal and vertical page scale.
- Arc drawing.

## Open Questions

- UNKNOWN - needs testing in real PlanSwift: exact Count workflow and finish
  behavior.
- The application name and namespace are now `OurPlanCore`; legacy settings/path
  readers remain compatibility behavior, not an unresolved naming decision.
- Multiple measurements under one item are exposed as sections; exact PlanSwift
  section/assembly parity remains a separate comparison.
- Regression note: for missing-on-canvas measurements or broken edit drag, start
  with [the measurement link/editing postmortem](../30-takeoffs-measurements/MEASUREMENT_PAGE_LINK_AND_EDITING_POSTMORTEM.md).
