# PlanSwift Takeoff Tools

> **Historical research / proposed acceptance criteria.** The PlanSwift
> summaries and implementation ideas below are retained for provenance. They
> are not a current description of OurPlanCore 2.2.7-preview and were not
> revalidated against a current PlanSwift installation in this documentation
> review. Read [current OurPlanCore status](../CURRENT_OURPLANECORE_STATUS.md)
> and the [updated user flow](PLANSWIFT_USER_FLOW.md) first.
>
> In current OurPlanCore, Count/Line/Area/Joist Area tool actions create a new
> takeoff item after confirmation. To continue an existing item, select it and
> enable Record. Do not implement the older "create only if no matching item is
> active" criterion below without a new, explicitly reviewed behavior change.

## Linear

### PlanSwift behavior

The Linear Takeoff Tool calculates lineal footage, yards, meters, and similar
length quantities. The top half of the Linear button starts a connected-segment
linear takeoff. The user opens Properties, enters a name, chooses color, clicks
OK, then clicks two or more points on the plan.

Sources:

- https://help.constructconnect.com/03-a-detailed-look-at-the-home-tab-and-drawing-takeoff-and-annotations-176/home-tab-linear-takeoff-tool-connected-segments-1810
- https://constructconnect-help.atlassian.net/wiki/spaces/PSUPPORT/pages/48825024/Home%2BTab%2BLinear%2BTakeoff%2BTool%2BConnected%2BSegments

### What this means for our app

User story: As an estimator, I want to select Linear and click wall endpoints so
that I can measure total length.

Acceptance criteria:

- User can select Linear.
- If no matching item is active, a Linear item dialog opens.
- User can click two or more points.
- User can undo last point.
- User can finish the line.
- Length uses the page scale stored on the measurement.

## Area

### PlanSwift behavior

The Area Takeoff Tool calculates square footage, yards, meters, and similar area
quantities. The user opens the Area Properties window, enters name/color/fill
settings, clicks OK, then performs the area takeoff. After completion, the user
can right-click Stop. PlanSwift 9.3+ supports double-click completion that keeps
Digitizer Record on for another grouped area.

Sources:

- https://help.constructconnect.com/03-a-detailed-look-at-the-home-tab-and-drawing-takeoff-and-annotations-176/planswift-03-11-the-area-takeoff-tool-1806
- https://constructconnect-help.atlassian.net/wiki/spaces/PSUPPORT/pages/48988566/Home%2BTab%3A%2BArea%2BTakeoff%2BTool

### What this means for our app

User story: As an estimator, I want to select Area and click corners so that I
can measure floor, wall, roof, or siding area.

Acceptance criteria:

- User can select Area.
- A fixed-type Area item is active before drawing.
- User can click three or more points.
- User can finish the polygon.
- Area fill and outline are visible.
- Total area appears in the item total.

## Count

### PlanSwift behavior

Official source located so far confirms that Count is a standard takeoff item
type and can be created from the Estimating tab. Specialty takeoff items are
available from Area, Linear, Segment, and Count drop-down menus.

Sources:

- https://help.constructconnect.com/07-a-detailed-look-at-the-planswift-estimating-tab-fine-tuning-your-estimate-180/planswift-07-03-estimating-tab-new-item-takeoff-item-assembly-or-part-1772
- https://help.constructconnect.com/14-plan-management-and-advanced-takeoff-tools-187/planswift-14-specialty-takeoff-items-roof-area-takeoff-2689

### What this means for our app

Current user-facing Point should become Count. Each click should add one counted
instance to the active Count item.

Acceptance criteria:

- Toolbar says Count, not Point.
- Count item type is fixed.
- Each click creates a visible marker.
- Count markers can be displayed as circle, cross, or square.
- A selected group of Count markers can have its display style changed
  together.
- The selected marker display appears in the viewport, sheet legend, Takeoffs
  tree, Pages linked rows, and PDF export.
- Total shows count as each/EA.

## Selection and Copy/Paste

### PlanSwift behavior

PlanSwift takeoff workflows commonly rely on selecting completed takeoff
objects, copying them, and reusing them on another sheet or inside another
takeoff item. Exact command details still need confirmation in a real PlanSwift
install, so this section records app behavior as an aligned MVP rather than a
claim of exact parity.

### What this means for our app

User story: As an estimator, I want to box-select several completed
measurements and copy them to another sheet so repeated plan areas do not need
to be redrawn from scratch.

Acceptance criteria:

- User can select the Select tool.
- Left-button drag draws a visible selection box.
- Dragging the selection box left-to-right selects measurements touched by the
  box.
- Dragging the selection box right-to-left selects only measurements fully
  enclosed by the box.
- `Ctrl+Click` toggles individual measurements.
- `Ctrl+C` copies the selected measurements.
- `Ctrl+V` pastes copied measurements to the active sheet.
- Paste asks whether to use the same takeoff items/values or create new copied
  takeoff items.
- Paste is cursor anchored: the copied set's upper-left bounds corner moves to
  the current cursor/right-click point so placement is predictable against the
  plan area the user is aiming at.
- Pasted measurements receive new IDs and point to the current sheet.

## Segment

### PlanSwift behavior

The Segment tool is similar to Linear but draws disconnected segments.

Source: https://help.constructconnect.com/03-a-detailed-look-at-the-home-tab-and-drawing-takeoff-and-annotations-176/home-tab-linear-takeoff-tool-connected-segments-1810

### What this means for our app

Segment is not required for first MVP. The current Line tool can represent
connected Linear takeoff only.

## Specialty Tools

### PlanSwift behavior

PlanSwift includes specialty takeoff items from tool drop-down menus, including
Single-Click Area, Linear, and Count, Roof Area, Price Per SQ FT, Area Cubic
Yards, Grid, and Joists.

Source: https://help.constructconnect.com/14-plan-management-and-advanced-takeoff-tools-187/planswift-14-specialty-takeoff-items-roof-area-takeoff-2689

### What this means for our app

Do not implement specialty tools yet. Keep the first app centered on Linear,
Area, and Count.
