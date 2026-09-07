# PlanSwift User Flow

Implementation notes updated 2026-09-06 for OurPlanCore 2.2.7-preview.
The PlanSwift source summaries below are retained historical research, not a
fresh audit of an external PlanSwift installation. For current OurPlanCore
commands see [current status](../CURRENT_OURPLANECORE_STATUS.md) and the
[workspace map](../60-ux-ui/WORKSPACE_TAB_COMMAND_MAP.md).

## Scope

This document reconstructs the main PlanSwift workflow that OurPlanCore should
match first: create or open a job, add pages, set scale, create takeoff items,
digitize measurements, and review quantities.

## Job Start

### PlanSwift behavior

PlanSwift opens on a ribbon-style interface. The welcome/start area offers New
and Open actions for Jobs. After a Job is open, the Home tab is the main takeoff
screen, and the Estimating tab is the other primary work area.

Source: https://help.constructconnect.com/getting-started-with-planswift-117/getting-started-with-planswift-2656

### What this means for our app

The product contract keeps opening, creating and finding projects first-class.
Current UI: Main → Open / Import → Open project..., blank/from-PDF creation,
imports into the current job and Manage job folders.... Ctrl+O opens the unified
picker directly. It accepts `.ourplan` and existing folder jobs; new projects
use `.ourplan`. Main → Save As retains the opened package/folder format.
After opening, Main View presents Pages, canvas, Takeoffs and status.

## Add Pages

### PlanSwift behavior

On the Pages tab, Add Pages can add image-backed pages or blank pages. The
image-backed flow selects files, applies converter settings, confirms, then
adds the pages to the Pages, Bookmarks panel. Blank pages require page name,
dimensions, and scale.

Source: https://help.constructconnect.com/04-a-detailed-look-at-the-page-tab-and-working-with-pages-177/planswift-04-02-add-pages-to-the-current-job-1854

### What this means for our app

PDF remains the core import path. Main → Open / Import and Page → Add Pages
add sheets to the active job, preserving source metadata and per-page scale.
Blank Sheet and the separate PlanSwift converter are also implemented. Optional
raster/snap preparation adds disk work and is not required for every vector PDF.

## Select and Organize Pages

### PlanSwift behavior

Pages appear in the Pages, Bookmarks panel. Selecting a page displays it in the
active tab by default. Pages and folders can be reordered by drag and drop.
PlanSwift also supports opening pages in new tabs or windows.

Sources:

- https://help.constructconnect.com/04-a-detailed-look-at-the-page-tab-and-working-with-pages-177/planswift-04-10-opening-and-closing-tabbed-pages-1477
- https://help.constructconnect.com/14-plan-management-and-advanced-takeoff-tools-187/planswift-14-02-opening-a-page-in-a-new-tab-or-new-window-and-undocking-tabs-1851
- https://help.constructconnect.com/04-a-detailed-look-at-the-page-tab-and-working-with-pages-177/planswift-04-09-adjusting-the-page-or-folder-order-1475

### What this means for our app

Page tabs and detached windows are implemented. Main tabs retain page/view
context; Detached Sheets enables independent windows and tiling. Activating a
detached window makes it the Pages navigation target; activating the main canvas
returns navigation there. Drawing, Beam/Openings, repeat, paste, transforms and
Undo must use the target window's page and scale. A second window with only
visually copied geometry does not satisfy this ownership contract.

## Set or Verify Scale

### PlanSwift behavior

PlanSwift stores scale per page. If a takeoff starts on an unscaled page,
PlanSwift can open the Scale window. PlanSwift can calculate scale from a known
line and can handle different horizontal and vertical scales.

Sources:

- https://help.constructconnect.com/14-advanced-plans-and-takeoff-tools-and-printing-187/planswift-14-03-calculating-scale-and-handling-different-horizontal-and-vertical-scales-on-the-same-plan-1757
- https://help.constructconnect.com/04-a-detailed-look-at-the-page-tab-and-working-with-pages-177/planswift-04-12-page-sheet-information-scale-page-size-measurement-type-1852

### What this means for our app

Scale-dependent drawing is gated on a valid page scale; Count remains scale-free.
The app stores one scale per page and the scale on each saved measurement.
Different horizontal and vertical scales remain outside the implemented model.

## Draw Takeoff

### PlanSwift behavior

Linear, Area, Segment, and Count are takeoff item types. Choosing a tool opens a
Properties window where the user names the item and selects visual properties.
After OK, Digitizer Record is active and the user clicks points on the plan.

Sources:

- https://help.constructconnect.com/03-a-detailed-look-at-the-home-tab-and-drawing-takeoff-and-annotations-176/planswift-03-11-the-area-takeoff-tool-1806
- https://help.constructconnect.com/03-a-detailed-look-at-the-home-tab-and-drawing-takeoff-and-annotations-176/home-tab-linear-takeoff-tool-connected-segments-1810
- https://help.constructconnect.com/07-a-detailed-look-at-the-planswift-estimating-tab-fine-tuning-your-estimate-180/planswift-07-03-estimating-tab-new-item-takeoff-item-assembly-or-part-1772

### What this means for our app

The app uses Count in the UI (legacy persisted `point` is an internal type).
Tool selection creates/selects a matching fixed-type target before drawing,
with explicit Record state. Ordinary Line is connected; Repeat Line makes
independent two-point measurements. Repeat Beam continues after each accepted
item dialog; Esc or dialog cancellation stops repeat.

## Reuse Existing Takeoff Geometry

### PlanSwift behavior

Repeated sheets often need the same takeoff geometry reused rather than redrawn.
Exact PlanSwift copy/paste labels still need testing in a real install, but the
target workflow is clear: select completed takeoff objects, copy them, move to
another sheet, and paste them into the desired takeoff context.

### What this means for our app

The app has a non-recording `Select` tool. In Select mode, left-button drag
selects measurements by area on the active sheet. `Ctrl+C` copies the selected
measurements, and `Ctrl+V` pastes them onto the active sheet. Paste asks whether
to reuse the original takeoff items or create new copied takeoff items. The
pasted set's upper-left bounds anchor is positioned at the cursor/right-click
point. The dialog labels are Same takeoffs, New takeoffs and Cancel. Paper
geometry is translated without automatic real-size rescaling; a scaled target
uses its scale, while an unscaled target requires confirmation to reuse saved
measurement scale. Main/detached paths share preflight, commit, selection refresh
and Undo, including cleanup of empty items created by the undone paste.

## Review Quantities

### PlanSwift behavior

Quantities are visible near the page item in the Pages, Bookmarks area and can
also be reviewed in the Estimating tab. Estimating supports new items, takeoff
items, assemblies, parts, and editable estimate table cells.

Source: https://help.constructconnect.com/07-a-detailed-look-at-the-planswift-estimating-tab-fine-tuning-your-estimate-180/planswift-07-03-estimating-tab-new-item-takeoff-item-assembly-or-part-1772

### What this means for our app

Totals are implemented in the Takeoffs tree, page-linked contexts and Estimating.
Estimating includes sections, notes, prices/costs, current-sheet filtering and
CSV/TXT/Excel output. Full assemblies and cost-formula database parity remain
outside this contract. PDF Output → Preview supplies a separate live current-sheet
export view; Main → Export produces selected/all-sheet PDFs.
