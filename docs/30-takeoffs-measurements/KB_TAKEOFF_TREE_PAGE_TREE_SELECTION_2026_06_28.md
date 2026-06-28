# KB: Takeoffs Tree, Page Tree Linked Takeoff Visibility, and Count Copy

Date: 2026-06-28

This note captures the final behavior and implementation surface for the June 28
tree/Count workflow changes.

## Final Behavior

- Page tree sheet rows can show linked takeoffs under a sheet.
- Linked takeoff rows in the Page tree use the actual takeoff glyph as the
  show/hide control. There is no separate colored dot, left numbering, or
  square hidden outline.
- The linked takeoff glyph size is `14` in both Page tree and Takeoffs tree.
- Clicking the linked glyph hides/shows that takeoff on the current sheet.
  Hidden current measurements stay hidden, but newly drawn measurements remain
  visible.
- Selecting a takeoff in the Takeoffs tree highlights the matching linked
  takeoff rows in the Page tree and scrolls a preferred linked row into view.
- Selecting ordinary sheet rows in the Page tree clears that linked-takeoff
  highlight, so previous sheets do not stay visually selected.
- The Page tree no longer paints linked rows just because a takeoff is the
  active drawing target. Linked-row selection color is reserved for explicit
  linked selection/highlight state.
- Takeoffs tree nested section/count rows are hidden by default.
- The old nested section/count rows can be restored with
  `Settings > Defaults > Show section/count rows under takeoffs`.
- When nested rows are hidden, viewport/estimating selection that targets a
  section falls back to selecting the owning takeoff item.
- Copy/paste of Count measurements preserves the Count display symbol in the
  viewport and in pasted/new takeoff targets.
- Keyboard `-` collapses both Page tree and Takeoffs tree. Mouse minus buttons
  still collapse their own tree only.
- Marquee selection works in both Page tree and Takeoffs tree.

## Main Implementation Surface

- `MainWindow.PageTakeoffLegend.cs`
  - Builds linked Page tree takeoff rows and glyph visibility controls.
  - Owns glyph sizing constants:
    `PageTakeoffGlyphSize = 14`,
    `PageTakeoffActiveGlyphSize = PageTakeoffGlyphSize`,
    `PageTakeoffGlyphHostSize = PageTakeoffGlyphSize`.
- `MainWindow.PageTakeoffLegend.Visibility.cs`
  - Applies per-sheet hidden takeoff/measurement state to viewport and page
    source persistence.
- `MainWindow.PagesTree.cs`
  - Handles Page tree clicks, linked takeoff clicks, and clearing linked
    selection when ordinary sheets/folders are selected.
- `MainWindow.PagesSelection.cs`
  - Applies Page tree visual states.
  - Linked takeoff selection uses `_pageTakeoffMultiSelection`.
  - Sheet-row active-takeoff background is gated by
    `_pageTakeoffMultiSelection.Count > 0` so it clears after ordinary page
    selection.
- `MainWindow.TakeoffSelectionNavigation.cs`
  - `RevealPagesForTakeoffItems(...)` is the Takeoffs tree -> Page tree bridge.
  - It adds linked row selection keys with
    `_pageTakeoffMultiSelection.Add(PageTakeoffSelectionKey(...))`.
  - It scrolls linked rows into view without setting `preferredLinked.IsSelected`.
- `MainWindow.TakeoffSections.cs`
  - `RefreshTakeoffSectionNodes(...)` builds nested section/count rows only
    when `_settings.ShowTakeoffSectionsInTree` is true.
- `MainWindow.TakeoffsSelectionHelpers.cs`
  - Falls back to owning takeoff item selection when section rows are hidden.
- `MainWindow.TakeoffSelectionNavigation.cs`
  - `SelectTakeoffSectionNode(...)` also falls back to the owning takeoff when
    section rows are hidden.
- `MainWindow.SettingsManager.cs`
  - Adds the Settings > Defaults checkbox:
    `Show section/count rows under takeoffs`.
  - `RefreshTakeoffSectionTreeVisibility()` rebuilds Takeoffs tree section
    children immediately after toggling the setting.
- `Models/AppSettingsStore.cs`
  - Stores `ShowTakeoffSectionsInTree`, default `false`.
- `MainWindow.MeasurementClipboard.cs` and `MainWindow.SupportTypes.cs`
  - Clipboard entries carry both `MeasurementCountSymbol` and
    `SourceTakeoffCountSymbol`.
  - Pasted Count measurements resolve the copied symbol before falling back to
    the target/default.
- `MainWindow.Shortcuts.cs` and `MainWindow.TreeExpansion.cs`
  - Keyboard `-` collapses both trees while mouse buttons stay tree-specific.

## Regression Tests

Primary guard file: `Tests/TakeoffsTreeRegressionTests.cs`.

Important test coverage:

- `ProjectTreeCollapseAndTakeoffDeleteSelectionAreWired`
- `PageMeasurementVisibilityToggleIsWired`
- `PageTakeoffSelectionSyncsTakeoffsTree`
- `TakeoffTreeSectionRowsDefaultHiddenAndSettingWired`
- `MeasurementPastePreservesCountSymbol`
- Existing copy/move/drag/drop tree tests around nested row target resolution.

`Tests/Program.cs` includes the new/updated test entries.

## Commits

- `4ccc708` - Bind keyboard minus to collapse trees
- `1f608dd` - Use takeoff symbols for page visibility
- `67caaf0` - Compact page takeoff legend rows
- `c4d668e` - Unify takeoff tree symbol sizes
- `24addc0` - Preserve pasted count symbols
- `2cebfaf` - Hide takeoff sections by default
- `e02584c` - Restore page linked takeoff highlight

## Verification

Latest verified package in the user-facing update folder:

- Path:
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`
- SHA256:
  `BB3B790653F3FC9C758B1E31FC61642513B215681A50965A42F4E8F39F4422A3`

Verification commands:

```powershell
dotnet build .\ourplanecore.sln
dotnet run --project .\Tests\OurPlaneCore.Tests.csproj --no-build
dotnet publish .\ourplanecore.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o .\bin\publish
```

Results:

- Build: 0 warnings / 0 errors.
- Tests: 400/400 passed.
- Packaged launch: process alive, no `ERROR` entries after the latest
  `Application startup.`, with `Loaded takeoffs` and `Viewport` log evidence.
- Desktop shortcut target:
  `C:\Users\User\Desktop\updates\OurPlaneCore\ourplanecore.exe`.
- Desktop shortcut working directory:
  `C:\Users\User\Desktop\updates\OurPlaneCore`.

## Future Guardrails

- Do not remove `_pageTakeoffMultiSelection.Add(...)` from
  `RevealPagesForTakeoffItems(...)`; it is required so selecting a takeoff in
  the Takeoffs tree highlights matching linked rows in the Page tree.
- Do not restore unconditional `IsActivePageTakeoffNode(...)` background
  painting in `ApplyPageTreeItemVisual(...)`; that is what made rows look
  selected when the user did not select them.
- If section/count rows are hidden, code that targets a section should select
  the owning takeoff item rather than trying to select a missing tree row.
- Keep `ShowTakeoffSectionsInTree` defaulted to `false`; the old nested-row
  behavior is opt-in through Settings.
- Count copy/paste must preserve `CountSymbol` for both same-takeoff paste and
  new-takeoff paste.
