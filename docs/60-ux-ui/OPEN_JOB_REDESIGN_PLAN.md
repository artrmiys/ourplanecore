# Open Job Dialog — Redesign Plan

> Исторический документ. Актуальный срез 2026-09-06: [состояние программы](../CURRENT_OURPLANECORE_STATUS.md), [release evidence](../STRATEGY_APP_EVIDENCE_2026_09_06.md) и [код этой области](../../MainWindow.OpenImportMenu.cs). Старые планы, пути и замеры ниже относятся к дате документа.

Date: 2026-05-20
Status: Spec frozen, not yet implemented.
Direction: **A — polished list with ribbon visual vocabulary** (chosen from
4-option mockup study; B/C — grid + preview pane, sidebar split — were rejected).

This is the implementation plan for refreshing `Dialogs/JobPickerDialog.cs`
to match the rest of the app (Viewport + PDF Output ribbon styling), and to
optimise for the user's primary need: **glance at what's in different
job-root folders**.

---

## 1. Primary user goal

When opening the dialog the user mainly wants to:

1. **See which job-root folders are configured and quickly switch between them.**
2. Find a recent / pinned job and open it.
3. Occasionally create a new job, or open a job from disk.

Everything else (sorting, drag-drop, fancy previews) was explicitly cut.

---

## 2. Visual vocabulary (reused from MainWindow.xaml)

The dialog must reuse existing ribbon resources defined in `MainWindow.xaml`
so it feels like part of the same app. No bespoke styles.

| Resource | Where it lives | Use in Open Job |
|----------|----------------|-----------------|
| `TopCommandButton` | MainWindow.xaml:9 | Footer Open / New job / Cancel; `+ Add folder` |
| `TopCommandToggle` | MainWindow.xaml:16 | (not used — source chips use the CheckBox style) |
| `TopCommandCheckBox` (ring + 7px accent dot) | MainWindow.xaml:39 | **Source chips** — ring + dot + label |
| `RibbonGroupCap` + `RibbonGroupLabel` | MainWindow.xaml:89 | `SOURCES` group caption under the source row |
| `RibbonSlider` + `RibbonSliderThumb` | MainWindow.xaml:99 | (not used now — kept for future) |

Background style for the dialog itself follows the rest of the app
(`SurfaceBackgroundBrush` / `ControlBorderBrush`).

---

## 3. Layout

```
┌─────────────────────────────────────────────────────────────────────┐
│ Open Job                                              [_][□][×]     │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│   ● All Jobs    ○ Projects    ○ OneDrive    ○ \\srv\share      [+] │
│   ─────────────────────────────────────────────────  SOURCES        │
│                                                                     │
│  [🔍 Search jobs...                                               ] │
│                                                                     │
│  ★ Pinned ───────────────────────────                               │
│   ▣  Acme Mall                       Yesterday    Projects     ★    │
│   ▣  Riverside HS                    Mar 12       OneDrive     ★    │
│                                                                     │
│  🕒 Recent ──────────────────────────                               │
│   ▣  Job_2_Rev3                      Today        Projects          │
│   ▣  North Plant Phase II            2h ago       OneDrive          │
│                                                                     │
│  📁 From "Projects" (19 more) ──────                                │
│   ▣  23-456 School Annex             Apr 02       Projects          │
│   ▣  24-001 Mixed Use Tower          Mar 28       Projects          │
│     …                                                               │
│                                                                     │
│  ⚠ Unavailable (1) ▸                                                │
│  ─────────────────────────────────────────────────                  │
│  Sample · Manage folders…         [Open]  [New job]  [Cancel]       │
└─────────────────────────────────────────────────────────────────────┘
```

Dialog size: keep current 760×500 default (MinWidth 600, MinHeight 380).

---

## 4. Source bar (top — primary navigation)

This is the most important element of the redesign.

- Each configured job root is a chip styled like `TopCommandCheckBox`:
  - 13×13 ring + 7px accent dot (visible when this is the active source).
  - Label = just the root's display name. **No counts. No kind label**
    (Local/Cloud/Net). Cleanest possible.
  - Clicking sets it as the active filter.
- First chip is always `● All Jobs`, active by default, no kind.
- Active state shows **only the dot** — background unchanged. (Subtle, not
  shout-y.)
- Trailing `+` button (style `TopCommandButton`): "Add folder…" — same flow
  as the current `Jobs Folder…` footer button (pick folder → add to roots,
  refresh list).
- Below the row: 1px top border + uppercase `SOURCES` label
  (`RibbonGroupCap` / `RibbonGroupLabel`).

Why "name only, no counts": user wanted a clean glance — names alone tell
which root is which; the list below already shows what's in the selected
root.

---

## 5. Search box

- Single TextBox, full width, FontSize 11, height ~24, ribbon-flavoured
  border (use existing brushes).
- Placeholder: `Search jobs...`.
- Filters by name | path | source root name (contains, case-insensitive).
- **When query is non-empty, group headers are hidden — flat list.**
  (Decision per user: easier to scan results.)
- Up/Down navigates list while focused in search (already works today).
- Enter opens the highlighted row. Escape closes.

---

## 6. List

### Groups (visible when search is empty)

Each group is a collapsible header. Group rows separator follows
`RibbonGroupCap` look (thin top border + small label).

| Order | Header | Contents | Default state |
|-------|--------|----------|---------------|
| 1 | `★ Pinned` | All pinned jobs across roots | Expanded |
| 2 | `🕒 Recent` | Recent jobs minus pinned | Expanded |
| 3 | `📁 From "<root>" (N more)` | Scanned jobs in each root, **minus** those already in Recent/Pinned. One header per configured root. | **Collapsed** by default |
| 4 | `⚠ Unavailable (N) ▸` | Missing recent jobs (folder gone, etc.) | Collapsed |

Default-collapse of "From <root>" + "Unavailable" keeps the dialog calm at
open; Pinned + Recent (the common case) are immediately visible.

Source filter from the source bar applies on top — chip `Projects` hides
the `From "OneDrive"` and `From "\\srv"` groups and filters Pinned/Recent
to only those rooted under Projects.

### Row

```
[thumb 80×54]   Name                       Date         Source     ☆/★
```

- **Thumb**: 80×54 PNG from `JobThumbnailService`. Empty box if absent.
- **Name**: primary text, ellipsize.
- **Date**:
  - For Recent jobs: human-friendly relative — `Today HH:mm`, `Yesterday`,
    `Nh ago`, `Nd ago` (≤7 days), then `MMM d` for current year, `MMM d, yyyy`
    otherwise. Source: `RecentJob.LastOpenedUtc`.
  - For scan-only jobs: same format applied to
    `File.GetLastWriteTimeUtc(Path.Combine(folder, "Data.xml"))`.
    Empty string if Data.xml doesn't exist.
- **Source badge**: short root name, small font, secondary colour. Shown
  always (helps when filter is "All Jobs").
- **Star**:
  - Pinned: solid `★` always visible on the right.
  - Not pinned: `☆` shown **only on row hover**, right side.
  - Click toggles pin; click must NOT also select the row (suppress
    bubbling — `e.Handled = true`).

Row height ~64px. Hover highlight via `ControlHoverBackgroundBrush`.
Double-click or Enter on a row opens that job.

### Right-click menu (kept from current dialog)

- Pin to Recent / Unpin
- Open Folder in Explorer
- Remove from Recent

---

## 7. Footer

Left (low-weight text links, `SecondaryForegroundBrush`):

- `Sample` — creates the sample job (current `CreateSampleJob`).
- `Manage folders…` — opens current `OpenJobFromJobsRootDialog` flow for
  managing job-root folders.

Right (ribbon `TopCommandButton`):

- `[Open]` — IsDefault, disabled when selection is missing/invalid. Accent
  background.
- `[New job]` — triggers `CreateJobFromDialog`.
- `[Cancel]` — IsCancel.

Removed from footer:

- `Browse Job…` — superseded by `Manage folders…` + double-click flow.
- `Jobs Folder…` — moved to the source bar's `+` button.

---

## 8. Behaviour decisions (frozen)

| Question | Decision |
|----------|----------|
| Where does the pin star live? | Right side, hover-only for non-pinned; always visible for pinned. |
| What date for scan-only (non-recent) jobs? | `Data.xml` Last Modified. Empty if file missing. |
| Drag-drop folder from Explorer? | **No.** Not in scope. |
| Search keeps groups? | **No** — flat list when query is non-empty. |
| What numbers on source chips? | **None.** Just the root name. |
| Active-chip indication? | Ring + dot only; background unchanged. |
| Where to put the styling? | Convert dialog to XAML so it pulls `StaticResource` from the app dictionaries. |

---

## 9. Implementation plan

### Files to add

- `Dialogs/JobPickerDialog.xaml` — the layout (DockPanel, source row,
  search, list with `GroupStyle`, footer). Reference resources from
  `MainWindow.xaml` via `StaticResource`.
- `Dialogs/JobPickerDialog.xaml.cs` — code-behind, holding the same
  public API as the current `JobPickerDialog` class (so the call sites in
  `MainWindow.JobPicker.cs` don't change).

### Files to modify

- `Controls/JobRootSelectorBar.cs` — replace bespoke `ToggleButton`s
  styled inline with `TopCommandCheckBox`-style chips. Remove the kind/path
  meta text from chip content; keep just the label.
- `MainWindow.JobPicker.cs`:
  - In `BuildJobPickerItems`, for scanned-folder items, look up
    `Data.xml` and produce a `LastOpened` value if the file exists.
  - Otherwise no API changes — `ShowRecentJobPicker` still constructs the
    dialog the same way.
- Delete the old `JobPickerDialog` class body once XAML version is wired.

### Data shape additions

`JobPickerItem` already carries `LastOpened` as a pre-formatted string.
Replace with `DateTime? LastOpenedUtc` and format on demand using a single
helper `JobPickerFormatting.RelativeDate(DateTime? utc)` so Recent and
scan-only paths share the formatter.

### Grouping

WPF `CollectionView.GroupDescriptions` with a `GroupStyle.ContainerStyle`
that uses an `Expander` for collapsibility. The `IsExpanded` default is
driven by the group key:

- `Pinned` → true
- `Recent` → true
- `From <root>` → false
- `Unavailable` → false

When the search box has a non-empty query, swap the ItemsSource to a flat
filtered list (no GroupDescriptions).

### Star click hit-region

The star is part of the row's DataTemplate. Make it a small `Button`
(transparent template) with `Click` handler that calls `SetPinned(item,
!item.IsPinned)` and sets `e.Handled = true` so the row's selection
behaviour doesn't fire.

---

## 10. Out of scope (do NOT do in this pass)

- Grid view (Option B) — postponed.
- Sidebar source tree (Option C) — postponed.
- Drag-and-drop folder import.
- Preview pane.
- Sortable column headers.
- Per-root job counts.
- Per-chip Local/Cloud/Net kind badge — removed per user preference.

---

## 11. Open question to confirm before coding

- **Default expand state** for `From "<root>"` groups: collapsed (proposal
  above) or expanded? Proposal is collapsed because the common workflow is
  Pinned/Recent; un-collapsing 5 root folders × 24 jobs each on open is
  noisy. To be confirmed when implementation starts.
