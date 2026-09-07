# OurPlanCore Master Remediation and Release Plan

Status: **active master plan; historical baselines retained**.

Current code verification: **2026-09-06, 42c44b0, independent 2.2.7-preview**.
Build: **0 warnings / 0 errors**; C# console harness: **807/807**; unchanged
Python helper suite: **29/29**. The primary runtime remains .NET 9. The separate
.NET 10 experiment passed its build/runtime checks but is blocked on the full
Excel gate; it is not an approved migration.

Sections 5.3 and Phases 2–3 were implemented in 2.2.6-preview and are retained
in 2.2.7. The latter adds measured save/render fixes, common paste/Undo guards,
editable keyboard shortcuts, and the compressed-EXE profile-marker fix.
See [current technical context](PROJECT_CONTEXT.md),
[strategy evidence and exact build identities](STRATEGY_APP_EVIDENCE_2026_09_06.md),
and [delivered-package QA](../../QA-REPORT-227.md).

**Current scope overrides the general release policy below:** the original
source checkout, stable 2.2.5 updates/shortcut and the user's running older
Preview were preserved. This delivery uses its own folder, shortcut and profile.
No public release, stable replacement or file-association change is authorized
by this document. The failed intermediate marker launch did touch the older
Preview profile; the final QA records the checked consequences and correction.

The current source is the [parent directory](../) of this document. Old absolute
paths and template hashes in historical sections record provenance only; do not
select those old workbooks or delivery targets for new work. Use the current
preserved delivered templates and the explicit task's target.

**Next work:** follow the nine bounded items in the
[improvement plan](70-architecture-refactor/IMPROVEMENT_PLAN_2026_09_06.md),
starting with protected settings and the Excel gate. This plan retains the
complete phase requirements; a completed narrow fix does not close a whole phase.

This plan converts the 2026-07-14 full-project audit into an implementation
sequence. It supersedes stale priority ordering in older roadmaps, but it does
not erase historical handoffs. Confirmed current behavior, planned changes,
and release requirements are kept separate below.

## 1. Objective

Strengthen the existing application without replacing its working takeoff
workflows. The order is intentionally:

1. preserve and distribute every known-good build safely;
2. prevent silent data loss and multi-instance overwrite;
3. close job-file trust-boundary problems;
4. make bulk operations recoverable;
5. improve test and release gates;
6. optimize memory, trees, and rendering;
7. decompose the two god objects behind stable behavior boundaries;
8. finish accessibility and UX consistency work.

No phase may silently change takeoff quantities, page names, sheet scale,
folder placement, suffix rules, or existing keyboard behavior. Any intentional
behavior change must be called out in that phase's handoff.

## 2. Historical Verified Baseline — 2026-07-14

Baseline captured before implementation. These counts, versions, paths and
hashes are historical, not the current release or template-selection policy:

- `dotnet build .\ourplancore.sln`: 0 warnings, 0 errors.
- C# regression harness: 494/494 passed.
- Python precise metadata tests: 24/24 passed.
- NuGet vulnerability scan: no known vulnerable packages reported.
- Current packaged EXE:
  - path: `C:\Users\User\Desktop\updates\OurPlanCore\ourplancore.exe`;
  - product version: `2.2.3`;
  - size: `173,709,959` bytes;
  - SHA-256: `E0CABE4482F11B7FA874223DD1441808925DA581972CDDC73FA8983CF40EAB26`.
- Excel template source:
  - path: `C:\Users\User\Desktop\Python\1.macros\TemplateCom.xlsm`;
  - size: `705,516` bytes;
  - SHA-256: `6859E31B8C7DB3E1593B6B5ABDCE2677516C61171516270C822FDD57F4E2508D`.
- Git branch is 11 commits ahead of its remote tracking branch.
- Existing untracked `docs/60-ux-ui/` assets belong to the user and are not part
  of this plan unless explicitly selected later.

## 3. Permanent Release Policy

### 3.1 Local update package

Every completed user-facing milestone must leave these current files together:

```text
C:\Users\User\Desktop\updates\OurPlanCore\
  ourplancore.exe
  TemplateCom.xlsm
  DOWNLOAD-LATEST.txt
```

Rules:

- `ourplancore.exe` must be the compressed, self-contained, single-file x64
  build produced from the exact source commit being released.
- `TemplateCom.xlsm` must be copied from the verified source workbook, not from
  an old release folder.
- `DOWNLOAD-LATEST.txt` must target the exact release tag and be reverified after
  the GitHub Release exists.
- Keep `ourplancore.exe.bak` immutable. If it already exists, create a
  timestamped additional rollback copy; never overwrite an existing backup.
- The Desktop shortcut must target the packaged EXE and use the update folder
  as its working directory.
- The file uploaded to GitHub must be the exact `ourplancore.exe` from the
  update folder. SHA-256 of publish output, update copy, and release asset must
  match.

### 3.2 GitHub distribution

The EXE is about 166 MiB, above GitHub's 100 MiB normal Git-object limit. It
must therefore **not** be committed as a regular repository blob. Distribution
will use GitHub Release assets, which support individual files below 2 GiB:

- repository: `https://github.com/artrmiys/ourplanecore`;
- release page: `https://github.com/artrmiys/ourplanecore/releases/latest`;
- direct EXE: `https://github.com/artrmiys/ourplanecore/releases/latest/download/ourplancore.exe`;
- direct workbook: `https://github.com/artrmiys/ourplanecore/releases/latest/download/TemplateCom.xlsm`;
- direct note: `https://github.com/artrmiys/ourplanecore/releases/latest/download/DOWNLOAD-LATEST.txt`.

Each published release must contain:

```text
ourplancore.exe
TemplateCom.xlsm
DOWNLOAD-LATEST.txt
```

Release tag format:

```text
ourplancore-v<product-version>-<yyyyMMdd-HHmm>-<short-commit>
```

Release requirements:

- push the exact source commit before creating its tag;
- create a non-draft, non-prerelease release and mark it latest;
- upload assets only after local build, tests, deployment, and log validation;
- do not upload API keys, job files, settings, user paths, or private samples;
- scan the workbook/package for obvious secrets before the first public upload;
- record version, commit, UTC build time, sizes, and SHA-256 hashes in both the
  release notes and `DOWNLOAD-LATEST.txt`;
- if asset upload or hash verification fails, leave the previous latest release
  intact and report the failure.

### 3.3 Release automation deliverable

Add one rebrand-aware repository script with explicit parameters instead of
using the stale pre-rebrand helper. The script must:

1. verify repo, branch, remote, paths, free space, template, and tools;
2. refuse unrelated implicit staging (`git add -A` is forbidden);
3. restore `win-x64`, build, run all C# and Python gates;
4. publish with `PublishSingleFile`, native/content self-extraction,
   compression, and `DebugType=none`;
5. deploy through a staging directory and preserve rollback files;
6. copy the current Excel template;
7. retarget and verify `OurPlanCore.lnk`;
8. launch the installed EXE and validate only the latest startup log segment;
9. generate `DOWNLOAD-LATEST.txt` with SHA-256 values;
10. upload the exact update-folder assets to a GitHub Release;
11. verify the published asset names/sizes/digests and latest links.

GitHub CLI is available through the pinned portable tool path used by the
release script and is authenticated only for the publishing operator. Public
downloads do not require a GitHub account.

## 4. Phase 0 — Checkpoint and Release Foundation

Goal: make every later milestone recoverable and downloadable.

- [x] Fetch remote state and confirm branch divergence.
- [x] Create a named checkpoint tag before behavior-sensitive edits.
- [x] Commit this master plan explicitly despite the repository-wide `*.md`
      ignore rule.
- [x] Push the 11 existing local commits plus the plan commit to the existing
      feature branch without staging `docs/60-ux-ui/` accidentally.
- [x] Open/update one draft PR from the feature branch to `main` for reviewable
      source history.
- [x] Add the safe release script described in section 3.3.
- [x] Install and authenticate GitHub CLI for release upload.
- [x] Remove the embedded OpenAI key from the public workbook source, resolve it
      from `OPENAI_API_KEY`, and validate the saved workbook copy.
- [x] Create the first GitHub Release from the current known-good packaged EXE
      and current `TemplateCom.xlsm`.
- [x] Put `TemplateCom.xlsm` and `DOWNLOAD-LATEST.txt` beside the update EXE.
- [x] Verify direct latest links from a clean request.

Acceptance:

- source commit and release tag exist remotely;
- local update EXE and release EXE hashes match;
- workbook hashes match;
- shortcut launches the update EXE;
- latest log segment has no production `ERROR` after `Application startup.`.

Phase 0 completion evidence (2026-07-15):

- source commit: `568034e2f67d8726292599d599e4d02020170867`;
- release tag: `ourplancore-v2.2.3-20260715-568034e`;
- public release:
  `https://github.com/artrmiys/ourplanecore/releases/tag/ourplancore-v2.2.3-20260715-568034e`;
- draft PR: `https://github.com/artrmiys/ourplanecore/pull/2`;
- release state: public, non-draft, non-prerelease, and current `latest`;
- release assets: exactly `ourplancore.exe`, `TemplateCom.xlsm`, and
  `DOWNLOAD-LATEST.txt`;
- EXE: `171,710,350` bytes, SHA-256
  `536C5DF8F6787C078B0A720C0D8A4C811B3A3EEF3A7FB519FE5AEE4666EA170B`;
- workbook: `657,561` bytes, SHA-256
  `DF9EA6D54BDB433788CC892DF20BA25E6E9B6E72F21AD7C681B70072057D92AC`;
- note: `1,026` bytes, SHA-256
  `195E0091C15CE6027CCE748EC388A0EF9E0ECD536DC47B1244142FC2C6A531C6`;
- clean release worktree: C# tests `496/496`, Python detector tests `24/24`,
  build `0 warnings / 0 errors`;
- authenticated draft verification and unauthenticated pinned/latest downloads
  all reproduced the same hashes;
- installed ProductVersion:
  `2.2.3+568034e2f67d8726292599d599e4d02020170867`;
- Desktop shortcut target and working directory point to
  `C:\Users\User\Desktop\updates\OurPlanCore`;
- packaged log `app-20260715.log`, latest startup at line `3080`: zero
  `ERROR`, with `Loaded takeoffs` and `Viewport` signals.

## 5. Phase 1 — Data Safety v1

### 5.1 Reliable autosave

Owners: `TakeoffSaveService.cs`, `MainWindow.StatusBar.cs`, lifecycle and
shutdown hooks, storage tests.

- [x] Keep an item dirty until its save succeeds.
- [x] Requeue failed items automatically.
- [x] Distinguish `LastAttemptUtc` from `LastSuccessfulFlushUtc`.
- [x] Expose `Clean`, `Dirty`, `Saving`, and `Failed` states.
- [x] Never display `Saved` while a write failed or remains pending.
- [x] Flush before job switch, destructive operations, and base window close.
- [x] If final flush fails, block/confirm closing and preserve the dirty set.
- [x] Add injected write-failure, retry, deleted-folder, and partial-batch tests.

Reliability comes before moving writes off the UI thread. Async persistence may
be added only after immutable save snapshots or equivalent synchronization are
in place.

Reliable autosave completion evidence (2026-07-15):

- source commit: `55a75e6404edc58c92e174ff3a2d8c697152986a`;
- checkpoint tag before the implementation: `checkpoint-before-autosave-20260715`;
- release tag: `ourplancore-v2.2.3-20260715-55a75e6`;
- public release:
  `https://github.com/artrmiys/ourplanecore/releases/tag/ourplancore-v2.2.3-20260715-55a75e6`;
- release state: public, non-draft, non-prerelease, and current `latest`;
- failed and partial writes retain their dirty entries, retry automatically,
  and report `Failed` instead of advancing the successful-save timestamp;
- pending entries are bound to their job root, reload/job-switch/destructive
  boundaries require a successful flush, and unavailable folders are retained
  unless the operator explicitly chooses the close-time discard path;
- final save moved to cancellable `OnClosing`; detached sheets and the old job
  lock are released only after the old job has been saved successfully;
- deterministic regression coverage includes write failure, retry, partial
  batches, unavailable folders, reload, job switch, and close lifecycle;
- clean verification: C# tests `510/510`, Python detector tests `24/24`, build
  `0 warnings / 0 errors`;
- installed EXE: `171,714,143` bytes, ProductVersion
  `2.2.3+55a75e6404edc58c92e174ff3a2d8c697152986a`, SHA-256
  `F0EA8761CA7C47303739AB45940FDA7A159C45AE073E8C610098DB1DF487F20A`;
- workbook: `657,561` bytes, SHA-256
  `DF9EA6D54BDB433788CC892DF20BA25E6E9B6E72F21AD7C681B70072057D92AC`;
- note: `1,026` bytes, SHA-256
  `62726D27E05A499F2F9B1BE0C508A7EF5C3B8143B66C70828D3D83CDB969239B`;
- authenticated asset metadata plus unauthenticated pinned/latest downloads
  reproduced the installed sizes and hashes; direct latest EXE and workbook
  requests returned HTTP `200`;
- Desktop shortcut target and working directory point to
  `C:\Users\User\Desktop\updates\OurPlanCore`;
- packaged log `app-20260715.log`, latest startup at line `3582`: zero
  `ERROR`, with `Loaded takeoffs` and `Viewport` signals.

### 5.2 Enforced job lease

Owners: `JobRecoveryService`, `MainWindow.JobRecovery`, persistence gate.

- [x] Replace notification-only lock behavior with an instance lease containing
      machine, process, instance ID, start time, heartbeat, and app version.
- [x] Present `Open Read-Only`, `Retry`, `Take Over`, and `Cancel` choices.
- [x] Enforce read-only state at the persistence boundary, not only in buttons.
- [x] Treat remote-machine locks as active unless lease expiry proves otherwise.
- [x] Stop heartbeat and release only the current instance's lease.
- [x] Add two-instance, stale-local, active-remote, takeover, and crash tests.

Completed 2026-07-15. The v2 lease is fail-closed across storage services,
autosave, import, AI continuations, bookmarks, overlays, and 3D/massing edits.
Active local and remote owners cannot be taken over; only a proven stale lease
can be replaced with compare-and-exchange ownership checks. Verification:
build `0 warnings / 0 errors`, full regression suite `544/544`, precise sheet
metadata tests `24/24`, and packaged startup with zero errors. Public latest:
`ourplancore-v2.2.3-20260715-24cfab9`, source commit
`24cfab9ed9b7c33a2395e95e38a6d73b755c8f2a`.

### 5.3 Tri-state data loading

Owners: `Models/Storage/TakeoffStore`, annotation/bookmark/page stores, recovery
UI and tests.

- [x] Return `Missing`, `Valid`, `Corrupt`, or `Unreadable`; never turn a
      transient IO error into a valid empty collection.
- [x] Quarantine parse corruption, but do not quarantine sharing/access errors.
- [x] Mark affected items/pages protected and prevent autosave overwrite.
- [x] Provide explicit retry/repair/open-folder actions.
- [x] Back up `source.json` before repair and preserve every supported field,
      including legend order, hidden state, raster state, and overlays.

Acceptance for Phase 1:

- failure-injection tests prove dirty data remains recoverable;
- a second process cannot silently write an already-open job;
- unreadable existing JSON cannot be replaced with `[]` by an unrelated edit;
- manual packaged smoke passes against a copied sample job.

## 6. Phase 2 — Job and AI Trust Boundary

Owners: new focused path-validation service, bookmark controller, AI stores,
AI runner, marker/observation actions, tests.

- [x] Add one `SafeJobPathResolver` with canonical containment checks.
- [x] Reject rooted paths, `..` escapes, invalid IDs, reserved device names,
      and reparse/symlink escapes outside approved roots.
- [x] Normalize IDs to a bounded safe character set and reject, not silently
      rewrite, identifiers loaded from an untrusted job.
- [x] Allow only expected file types for crop/bookmark open actions.
- [x] Open images in an internal viewer instead of shell-executing arbitrary
      job-controlled files.
- [x] Limit AI input size/type and show the exact files that will be sent.
- [x] Keep layer manifests and crops inside the job context unless the user
      explicitly chooses a reviewed external file.
- [x] Add malicious-job regression fixtures for path traversal, rooted paths,
      executable bookmarks, oversized files, and AI local-file disclosure.

Acceptance:

- crafted job JSON cannot read, write, delete, execute, or upload files outside
  approved job/context roots;
- normal existing relative crop/bookmark workflows still work.

## 7. Phase 3 — Recoverable Bulk Operations and Undo

Owners: page organization, `NodeStore`, imports, source reference rewriting,
new operation journal/undo models.

- [x] Save a complete pre-operation order snapshot before every sort.
- [x] Add visible `Undo Last Page Sort`; scope it to the exact affected folder
      or root operation.
- [x] Ensure folder-scoped A/S never changes siblings outside that folder.
- [x] Use staged writes or a journal for move/copy/import/reference rewrites.
- [x] Roll back completed steps when a later step fails.
- [x] Preserve legend order, overlays, hidden state, scale, source paths,
      measurements, and takeoff links during page move/copy.
- [x] Stage PDF/PlanSwift import before exposing partially created nodes.
- [x] Persist undo manifests so recovery survives application restart.

Acceptance:

- interrupted sort/move/import tests restore the previous coherent state;
- accidental global sort has a documented one-click undo path;
- copied/moved pages retain complete metadata bit-for-bit except intended paths.

## 8. Phase 4 — Settings and Automation Integrity

Owners: `SettingsPresetStore`, `AppSettingsStore`, template stores, Settings UI.

**Still open at 42c44b0.** The protected shortcut store is a scoped implementation;
general `AppSettingsStore` and `SettingsPresetStore` still collapse failed loads
to defaults/null. It does not close the requirements below. The next bounded
slice and acceptance criteria are improvement-plan item 1.

- [ ] Replace silent `null/default` fallback with `Missing`, `Valid`, and
      `Invalid` results plus effective-scope reporting.
- [ ] Quarantine invalid JSON and preserve a last-known-good copy.
- [ ] Show a blocking warning before applying Auto Name, Auto Scale, suffix, or
      sort rules when the selected override failed to load.
- [ ] Show whether each rule currently comes from job, global, or built-in
      default.
- [ ] Preserve the established editable Settings pattern: exact current
      default, Reset, presets, global save, per-job save, and Apply.
- [ ] Move remaining user-facing taxonomies/routing/sort tokens out of hidden
      constants where they define observable behavior.

## 9. Phase 5 — Test, Runtime, and Supply-Chain Gate

**Partial evidence, gate still open.** The isolated .NET 10 spike at `69031529`
passed 791 C# tests, 29 Python tests, PDF/Skia/OCR/WPF and a compressed native
launch. Full Excel smoke fails on both compared runtimes; the cause remains
unresolved. See [compatibility matrix](../../platform-evidence/PLATFORM-COMPATIBILITY.md).
The current 807-test .NET 9 suite must not be relabeled as a .NET 10 run.
Console tests and real-project harnesses exist; standard discovery, per-test
timeouts, CI, SDK/package locks and the full release gate remain incomplete.

- [ ] Keep the current console harness while making tests discoverable through
      `Microsoft.NET.Test.Sdk` and a standard framework.
- [ ] Separate source-wiring contract tests from behavioral tests.
- [ ] Add per-test timeout/filter and failure-injection helpers.
- [ ] Give tests a separate log root containing process ID and test identity.
- [ ] Add CI for build, C# tests, Python tests, vulnerability scan, and package
      manifest checks.
- [ ] Add `global.json`, deterministic package locking, dependency inventory,
      licenses, and an SBOM/hash manifest.
- [ ] Verify and update the embedded servicing runtime against current official
      support information, then complete the .NET 10 compatibility gate.
      The spike itself was performed; full Excel acceptance remains blocked.
- [ ] Reconcile the Windows manifest with actually supported operating systems.
- [ ] Trim unused embedded Python standard-library tests and document exact
      vendored package versions/hashes without breaking runtime extraction.
- [ ] Plan code signing before external/customer distribution.

## 10. Phase 6 — Performance and Memory

**Measured slices delivered; systemic work still open.** Per-file metadata
classification removes repeated filesystem probes while retaining path checks.
Immutable bitmap leases remove whole-image copies; repaint coalescing retains
the trailing request and acknowledges requests consumed by an actual paint.
The [final repeated comparison](../../PERFORMANCE-COMPARISON-227-FINAL.md)
uses three runs per version/project: Westminster Save 128.04→8.97 s and Close
120.64→2.73 s; paint p95 358→330.5 ms. Its paint median is 2.1% higher,
Pages expand-all 18.7% higher and sampled private peak 2.2% higher. Takeoffs
reload remains about 4 s. None of this establishes a leak or a global memory
budget. The budget, trees and incremental-save items remain open below and
in improvement-plan items 5–7. Test-host open timing excludes process startup;
memory sampling stops before Close. Native all-214-sheet proof is separate.

- [ ] Introduce one global render-memory budget shared by all bitmap caches.
- [ ] Add memory-pressure trimming and bounded settings presets.
- [ ] Tile/source-clip sheet overlays instead of redrawing a full bitmap every
      frame where possible.
- [ ] Convert Pages/Takeoffs trees to data-bound models so WPF recycling is
      real, not only declared in XAML.
- [ ] Build filesystem snapshots off the UI thread and debounce tree search.
- [ ] Buffer application logging while preserving crash-time flush.
- [ ] Add packaged performance budgets for page open, zoom/pan, overlay frame,
      tree load/search, save latency, and peak memory.

## 11. Phase 7 — Architecture Decomposition

No big-bang rewrite. Use tested strangler boundaries.

- [ ] Add a central `TakeoffMutationCoordinator` / refresh plan so mutations
      trigger one consistent save and UI refresh sequence.
- [ ] Extract `SimilarCountReviewCoordinator` from the oversized workflow.
- [ ] Separate `PdfViewport` render/cache engine from interaction/tool state.
- [ ] Replace mutually exclusive drag booleans with a typed interaction state.
- [ ] Extract manager/workspace tabs into focused controllers/UserControls while
      keeping stable XAML names and shortcut shell wiring.
- [ ] Split `MainWindow.xaml` only after controller boundaries and behavioral
      tests exist.
- [ ] Remove confirmed dead render/import/legacy blocks after caller and feature
      gate verification.
- [ ] Keep new C# and XAML files within repository size/method limits.

## 12. Phase 8 — UX and Accessibility Completion

**Partial delivery:** editable shortcuts, focus/typing/modal guards and their
main/detached scenarios passed; the installed editor and correct separate
profile were inspected through the final shortcut launch. This does not prove
complete keyboard, screen-reader, minimum-width or accessibility coverage.
Recheck each remaining behavior below before assigning a new implementation.

- [ ] Do not intercept `Space` when a button/toggle/menu control owns keyboard
      focus.
- [ ] Add visible keyboard focus for command/tool controls.
- [ ] Make the F1 overlay modal to shortcuts, focusable, and dismissible by a
      predictable keyboard path.
- [ ] Add automation/live-region support for viewport/status surfaces where
      practical.
- [ ] Make status-bar segments responsive at the minimum window width.
- [ ] Centralize status/toast reporting and retain modal dialogs only for real
      choices, destructive confirmation, or unrecoverable failure.
- [ ] Update the F1 map and keyboard documentation from the same command source.

## 13. Gate for Every Releasable Milestone

No milestone is `done` until all applicable checks pass:

1. inspect `git status -sb`; preserve unrelated files;
2. `git diff --check` and conflict/TODO marker scan;
3. build with 0 warnings and 0 errors;
4. run the complete C# harness and precise Python metadata tests;
5. run focused new regression/failure tests;
6. perform the relevant real-job or copied-job manual scenario;
7. publish compressed self-contained single-file x64;
8. deploy through verified staging while preserving rollback;
9. copy current `TemplateCom.xlsm` and generate download/hash notes;
10. retarget and verify the Desktop shortcut;
11. launch the packaged EXE and inspect only the latest startup log segment;
12. commit only intended source/docs/scripts, push the branch, tag the exact
    commit, and publish the three GitHub Release assets;
13. verify hashes and permanent latest-download links.

If any gate fails, retain the previous update package and previous GitHub
latest release. Record the failure in `docs/DEVELOPMENT_LOG.md`; never label a
partially verified package as latest.

## 14. Current Order and Historical Sequence

Current order and concrete acceptance criteria are maintained in the
[2026-09-06 improvement plan](70-architecture-refactor/IMPROVEMENT_PLAN_2026_09_06.md):
protected settings → Excel recoverability/gate → reproducible release/runtime
gates → workspace capacity/backup → shared memory accounting → responsive trees
→ dirty-only save → visible AI cancellation/typed actions → one state-owner
extraction. These are planned, not implemented by the documentation audit.

The original phase sequence is retained below as historical reasoning; do not
restart completed Phases 0–3 merely because they appear in this list.

1. Complete Phase 0 release foundation and publish the current known-good
   baseline.
2. Implement Phase 1 in three independent checkpoints: autosave, lease, loaders.
3. Complete Phase 2 before opening shared jobs from untrusted sources.
4. Complete sort snapshot/undo first within Phase 3, then move/import journal.
5. Complete Phase 4 before further auto-name/scale/suffix rule expansion.
6. Establish Phase 5 gates before architectural extraction.
7. Execute Phases 6–8 as small measured slices, releasing each stable milestone.

This order protects the user's existing work first and keeps a downloadable,
rollback-capable EXE available throughout the longer refactor.
