# Final UI refinement — 2026-11-12

**Partial qualification of `1.3.0-candidate.1`; implementation checkpoint, not release approval.** Native captures were reviewed against the supplied references and used to correct visible defects. Pixel-perfect parity is not claimed. Exact-head delivery/CI results are recorded separately after the single push, as described below.

## Scope and preserved state

- Existing branch/worktree: `feature/modern-dashboard-ui`, Draft PR [#3](https://github.com/ProGraMajster/ModernTubeDownloader/pull/3).
- Starting application commit: `e8f493f9cae5fe5df674cc48ed9fc88616e5bb19`.
- Central ModernFormsNext pin remains `7bf58d06d795d7298c86d9bf9471bbfb46852bab`; application version is unchanged.
- The main checkout remains outside the write scope. Its audited starting commit was `4ab4957faa57d04fbcf7b41dda8279a7e2a630ff` with no working-tree changes. The historical performance worktree's existing modified/untracked files were recorded and preserved.
- One runtime, queue, History store, importer and update service remain shared by Classic and Modern. No updater/migration redesign, tag, release, NuGet publication, force push or merge is part of this iteration.
- Local evidence root: `artifacts/final-ui-refinement-20261112/`. `initial-state.json` records the audited checkout state; it was captured after the first new regression-test file had been created. Raw logs, TRX files and PNGs remain local ignored artifacts, not public-user-data fixtures.
- Previous report: [onboarding/dashboard iteration](docs/onboarding-dashboard-20261111.md). Existing journey and update contracts remain documented in [ONBOARDING.md](ONBOARDING.md), [WHATS_NEW.md](WHATS_NEW.md), [UPDATES.md](UPDATES.md) and [MODERN_DASHBOARD_UI.md](MODERN_DASHBOARD_UI.md).

## Release CI diagnosis and correction

The original push CI on `e8f493f` failed two Release cases of `OtherComposesWorkingToolsAndRealRecentMetadataAndRestoresClassic`, at `(1060,150)` and `(1700,200)`. Its Debug result was **1111/1111**, Release **1109/1111**. The separate PR workflow passed. The original push-job log is retained as `baseline-push-ci.log`; the original failure is not relabeled as success.

The thrown exception was `InvalidOperationException: Collection was modified; enumeration operation may not execute` at `AppTypography.RefreshPresentation`, line 95 in the baseline. MFN `ControlCollection.GetEnumerator()` returns the underlying `List<Control>` enumerator. Setting a bound font can invoke application `FontChanged`/layout callbacks, so retaining that enumerator while recursively applying fonts is unsafe when a callback removes/reparents descendants. A focused deterministic regression removes/disposes a sibling, moves a captured subtree outside the current root, and inserts a replacement during `FontChanged`. Before the fix, it fails with the same exception and source location.

The corrected traversal captures a finite list of bound targets and recipes before applying any font. Before each application it checks the current binding, immediate parent and ancestry against the original root. Disposed targets have their bindings removed; detached/reparented subtrees and superseded recipes are skipped. `Apply` also stops writing stale styles when its own `FontChanged` callback disposes/rebinds a target or changes its presentation. The list is local to one call, with no growing retained cache, exception suppression or retry loop.

There was a second relevant boundary in `OtherView`: asynchronous recent-operation loading rebuilt `recent.Controls` directly after I/O. That continuation could run without the canonical UI synchronization context, including direct test-host/shell-transition entry points. Publication now runs as one operation on the existing UI dispatcher, with the generation and disposed checks inside that operation. A dedicated test starts the refresh without a UI context and verifies that every observed add/remove occurs on the owning UI thread. The remote stack alone does not establish which callback/continuation mutated the list in that particular CI run; the report distinguishes the proven traversal defect from the independently corrected publication boundary.

No framework pin/source change, timeout increase, skipped assertion or retry-until-green was used. A single baseline run of the original three Release cases passed locally; that does not erase the remote timing failure.

## Implemented presentation changes

### First run, existing-user choice, and What's New

- Shared native dialog finish reuses the Downloads surface gradients and caption branding. The native MFN titlebar keeps its drag/caption-button/accessibility behavior, while a decorative mouse-transparent caption supplies matching typography.
- The six-step wizard now has a measured header, step icon, explicit numbered progress and progress strip. Each step uses measured cards rather than loosely positioned text. Fields, toggles, folder actions and the summary use the same visual language as the dashboard.
- Classic/Modern cards contain the existing real application preview PNGs, descriptions and an explicit selected indicator. Narrow/large-text layouts stack cards vertically; selection still writes only the journey draft until completion.
- The summary includes filename/conflict policy, notification and tray choices as well as language/interface/folders/quality/update policy. The existing-user screen remains a one-time interface/update-policy choice, not the six-step new-user wizard.
- Update-mode selection is disabled when automatic updates are disabled, without erasing the stored selection or replacing the existing migration/installation policy.
- What's New adds a version/header hierarchy, measured local-release-note cards, compact wrapping navigation actions, and a visible Later action. Later/X leave acknowledgement unchanged; the existing shown-plus-explicit-acceptance contract remains.
- Save/link failure handlers now relayout the measured error region when feedback text appears, so the first failure is visible without resizing or taking another action.
- Native review caught a defect that the first functional journey run did not: the wizard over Classic initially resolved missing `Downloads.*` colors as transparent, leaving gray surfaces. The same nine existing palette entries are now available in both themes. A focused Dark/Light test checks exact colors, opaque alpha and unchanged existing Classic colors through Classic → Modern → Classic. The original gray captures remain explicitly **VISUAL FAIL**, even though their persistence scenarios passed.
- Actual preview bitmaps retain explicit ownership and disposal. No reference mockup is embedded as a fake application screen.

### LIVE and History

- LIVE source controls, search and sorting share a row when the measured width permits, and wrap when needed. The controls remain interactive, and the viewport is checked for overlap/horizontal overflow by targeted tests.
- LIVE cards, sections and session-details window reuse the Downloads gradients. The details window uses thematic recording, recovery and saved-file sections, contextual actions and the shared caption finish. All values still come from the canonical session.
- History has a continuous table/pager surface, coordinated header/filter/action gradients, denser measured row typography and full status tooltips/accessibility names. The Polish failed-state caption is compact `Błąd`, avoiding a lone final letter wrapped into a second line. Existing selection, row menus, recycled-record guards and paging remain intact.
- The History limit of 40 live rows and existing bounded image loading are retained. This visual iteration does not claim new 2750/10000-entry performance measurements.

### Batch Import and Other

- Batch uses the same drop-zone, counter-card, header, selected-row and action gradients as Downloads. Duplicate/error counts have distinct semantic colors. A framed status area uses an icon/color derived from the real current state, including analysis/readiness/error states. The full source path is available by tooltip.
- Existing parser counters, paging, source icons, optional thumbnail preference and explicit Add-to-queue handoff remain. No download is triggered for a screenshot; no throughput or completion data is invented.
- The empty state uses one import instruction, an explicit no-file label and genuine zero parser/selection/progress counters instead of repeating the same instruction five times. Classic retains its original empty-state text.
- At 100% in the wide layout, Batch rows are 32 px with four 26 px actions, allowing ten complete rows instead of eight in the captured viewport. Measured text can raise the minimum height; compact/large-text layouts retain their prior spacing. Tests require the new exact geometry, accessible action names and every child's bounds. Counter corners are 8 px like the adjacent Downloads-style cards.
- Other uses larger colored native icon tiles for the existing working tools/routes, revised header spacing, and consistent surfaces for its import workflow and quick actions.
- Recent operations remain real saved Batch sessions and thumbnail exports. Conversion/subtitle/merge modules absent from the product were not replaced with decorative dead buttons. An empty recent list is intentionally empty.

### Settings

- The scroll host now reacts to actual viewport-width changes when a vertical scrollbar appears, using bounded layout passes instead of retaining an allocation wider than the visible viewport.
- Path fields and browse commands use measured widths and stack when their useful widths cannot fit together. Full text/path tooltips remain available. Eleven categories and existing search/navigation remain.
- The nested cookie editor has responsive measured rows, wrapping commands and a preferred-height notification. Classic uses its existing layout; no cookie authorization, file or system registration is changed by layout verification.
- The first targeted run exposed reentrant nested-editor layout: notifying the parent during intermediate sizing let the parent's Classic-coordinate preparation overwrite unfinished Modern bounds. The editor now publishes its preferred height only after its complete measured layout. The original four failures are preserved; the focused follow-up passes.

### Restoring surfaces after a shell switch

Final review found that `DashboardVisual.RestoreTree` correctly clears Modern gradients
when returning to Classic, but constructor-only recipes on new History/Batch/Other
surfaces were not rebound when returning to Modern. Activation now restores the same
existing recipes on the retained table surface, counter/status/header panels, icon
tiles and workflow strip. Three focused cases run two actual restoration cycles each,
check the clearing and exact restoration of every affected gradient, and retain the
same controls and empty queue. This does not waive the historical intermittent external
UIA/NavDownloads issues.

## Targeted verification to date

Commands used the isolated .NET 10.0.401 SDK, the unchanged pinned MFN checkout, sequential MSBuild (`-m:1`, shared compilation disabled), and explicit `dotnet test --filter`. Counts below overlap and must not be added into one total.

| Evidence | Scope | Result |
| --- | --- | --- |
| `typography-baseline-release.log` / matching TRX | Three original Other composition cases, baseline Release | **PASS 3/3**; remote timing failure did not reproduce in this one local baseline run |
| `typography-regression-before02.log` / matching TRX | New mutation regression on original implementation | **FAIL** with the original `RefreshPresentation:95` exception |
| `ci-fix-debug-02.log` / matching TRX | Original three cases plus mutation/publication regressions | **PASS 5/5**, Debug |
| `ci-fix-release.log` / matching TRX | Same focused set | **PASS 5/5**, Release |
| `views-targeted-01.log` / matching TRX | Targeted presentation/journey set | **FAIL: 71 passed, 4 failed, 75 total** |
| `feedback-refinement-debug.log` / root-level matching TRX | Settings, Experience and LIVE presentation suites after the nested-layout/feedback corrections | **PASS 47/47**, Debug, zero skipped |
| `visual-corrections-debug.log` / matching TRX | Experience/palette, History and Batch after native-driven palette/density corrections | **PASS 29/29**, Debug |
| `palette-async-debug.log` / matching TRX | Palette test after replacing blocking waits with `await` | **PASS 2/2**, Debug; no new xUnit blocking-wait warnings |
| `history-status-debug.log` / matching TRX | History presentation/actions after the compact failure caption | **PASS 7/7**, Debug |
| `surface-roundtrip-debug.log` / matching TRX | Three new gradient-restoration cases, History actions/presentation and Batch geometry | **PASS 13/13**, Debug |
| `batch-empty-final.log` / matching TRX | Final empty-state content, clipboard handoff, Classic restoration and three Batch geometry cases | **PASS 4/4**, Debug |
| `native-roundtrip-batch.log` / `native-roundtrip-batch-observable.log` | Additional native Batch shell-cycle and active-analysis observation | **FAIL / NOT QUALIFIED**: first missed the transient Pause state; second captured active analysis but it finished before the post-capture client read. Original logs and all PNGs retained; no further retry. |
| `native-live-final.log` | Isolated LIVE HWND/bridge screen scenario, normal startup and shutdown | **PASS**, 1448×1086, actual HWND DPI 96, text 100%; not full external UIA qualification |

The four `views-targeted-01` failures are `DownloadPathsAndNestedCookieEditorFitTheActualScrollViewport` for PL/EN at 150% and 200%, in the feedback visibility/bounds assertion. The PL/EN 100% cases passed in that run. Original failures remain in the TRX. The reentrant-layout correction described above is covered by the **47/47** focused follow-up; the complete 75-case set was not needlessly rerun.

Other retained development failures are explicitly distinct from the product regression: an initial new-test compilation used MFN's non-public `IsDisposed` property; an early dispatcher-test fixture did not populate the Classic recent list; one build encountered an incomplete concurrent `SettingsScrollPanel` edit; another found the new decorative icon forwarding `EventArgs` where `MouseEventArgs` is required. These logs are retained, and the latter code/build issues were corrected before the focused 5/5 results. No failed log was removed.

## Native PNG evidence and visual comparison

The five original reference files plus `Pobieranie.png` are visual design references, not screenshots generated by this iteration. The `before/` folder preserves selected actual native screenshots from the prior iteration. New captures use isolated synthetic profiles, the real application/runtime, and owned HWND capture. They are not ImageGen mockups, browser renders, or screenshots with fabricated runtime data.

The [local gallery](artifacts/final-ui-refinement-20261112/gallery.html) pairs references with real captures and preserves earlier before/after frames. Every PNG is copied byte-for-byte; the gallery's thumbnails link to full-resolution PNGs. `png-sha256.json`, per-window capture metadata, original semantic trees and `native-manifest.json` preserve provenance. Paths below are relative to `artifacts/final-ui-refinement-20261112/native/`.

| Surface/state | Actual PNG evidence | Result and geometry |
| --- | --- | --- |
| Wizard steps 2, 5, summary | `native-experience-summary/experience-step-2.png`, `experience-step-5.png`, `experience-step-6.png`, `experience-summary-bottom.png` | **PASS**, PL/Dark/100%, 1040×820; all six steps, completion and restart. The summary scrolls; the bottom capture exposes every remaining preference. |
| Resume / larger text | `native-resume-light-150-corrected/experience-step-3-resumed.png`, `experience-step-5.png` | **PASS**, PL/Light/150%, 1040×820; draft path and step survive closing/restarting. |
| Existing-user choice | `native-upgrade-classic-corrected/experience-upgrade-classic.png`, `native-upgrade-modern-corrected/experience-upgrade-moderndashboard.png` | **PASS**, PL/Dark/100%, 1040×820; both choices persist and no longer recur after completion/restart. Original history fields/IDs and authentication sentinel are preserved. |
| What's New | `native-experience-summary/experience-first-run-news.png`; `native-news-en-light/experience-news-later.png`, `experience-news-reopened.png` | **PASS**, PL/Dark and EN/Light/100%, 1040×820. Later closes the actual HWND and leaves the acknowledgement and complete marker SHA-256 unchanged; manual reopening and explicit acceptance still work. |
| LIVE | `native-live-final/live-current.png`, `live-scheduled-selected.png`, `live-filter-empty.png`, `live-search.png`, `live-recent.png`, `live-details.png` | **PASS**, PL/Dark/100%, 1448×1086; details 900×720. The additional `native-live-light-150/` run passes with stacked larger-text details. |
| History | `native-history-status-final/history-selected.png`, `history-errors.png`, `history-empty-search.png` | **PASS** after the final compact status caption; selected/filtered/empty states use 1448×1086, PL/Dark/100%. |
| Batch Import | `native-batch-in-progress/batch-import-empty.png`, `batch-import-parsed.png`, `batch-import-analyzing.png`, `batch-import-analyzed.png` | **PASS**, PL/Dark/100%, 1448×1086. Real parser counters: 12 rows, 10 valid, one duplicate, one error; queue handoff is not invoked. |
| Other | `native-other-final/other-tools.png`; `native-batch-in-progress/other-recent-import.png` | **PASS**, PL/Dark/100%, 1448×1086. Both empty recent activity and a real session saved by the preceding import are captured. |
| Settings | `native-settings-final/settings-general.png`, `settings-downloads.png`, `settings-cookie-consent.png`, `settings-cookie-actions.png`; corresponding `native-settings-light-200/` files | **PASS**, 1448×1086 at PL/Dark/100% and PL/Light/200%. Categories, search and nested scrolling work with no horizontal scrollbar in the reviewed captures. No consent/repair/registration action is invoked. |

All captured HWNDs report **96 DPI**. Application text scale 150/200% is not physical monitor DPI qualification. Long editable paths scroll within their text fields and expose full tooltips; a long path is not forced into a single non-scrollable label.

The additional native `native-roundtrip-history` and `native-roundtrip-other` scenarios **PASS** after actual Modern → Classic → Modern navigation. Their before/after HWND PNGs retain the corrected gradients, and persisted queue SHA-256 remains unchanged. This is a bounded shell-transition check, not full external UIA stress qualification.

The first fast Batch transition capture (`native-batch-corrected/batch-import-analyzing.png`) was already completed by capture time and is **not evidence of an in-progress frame**. The additional dedicated scenario uses the existing fake metadata provider with explicitly controlled 1500 ms response latency, and asserts that analysis is active in the captured semantic snapshot and immediately after HWND capture. No application delay or test timeout was changed; this is state-presentation evidence, not a performance result. The original late capture is retained.

`native-roundtrip-batch.log` retains a later **FAIL** while waiting for the transient enabled `BatchPause` state. The saved session and original application log show all ten valid entries reached Ready with metadata/revision 2, with the duplicate and invalid URL retained correctly. Analysis ran from 18:59:52.226 to 18:59:53.092 (0.866 s), before the separate automation client observed the pause state. This is evidence of a fast-fixture observation race, not an application hang. The screen fixture now uses the same explicitly controlled metadata-provider latency whenever its scenario needs to observe active analysis. All pause/capture assertions and timeouts remain unchanged; the final strict scenario also checks active analysis at capture time. The failed run remains separate and is never relabeled PASS.

The final `native-roundtrip-batch-observable` attempt also **FAILS**, at the additional post-capture Pause check: the semantic snapshot and actual PNG show analysis running, but it had ended before the following client read. Its `batch-import-empty.png` confirms the final no-file/zero-counter presentation, and its before/after shell frames show restored gradients. The complete post-switch active-analysis scenario is nevertheless **NOT QUALIFIED**. No additional delay, timeout increase, assertion removal or retry was used to turn this result green. The earlier direct Batch scenario's PASS does not supersede this failed shell-cycle scenario.

The LIVE log reports 2616 ms from process start to native HWND/input-idle. This is an indicative observation only: it is not tools-ready time, a cold-PC measurement, or a comparable before/after performance claim.

Before-change screenshot review identified flat Batch drop-zone/counter/table surfaces and unframed parser feedback, plus small outline-only icons and inconsistent action surfaces on Other. Actual after frames show framed semantic feedback, 8 px metric corners and ten visible Batch rows. LIVE's first row moved from approximately y=305 to y=261 at 1448×1086 (reference y≈263), by sharing toolbar rows rather than removing controls. History now shows ten records with a continuous table/pager surface. The original reference images remain distinct from geometric synthetic thumbnails and genuine runtime facts.

## CI and delivery checkpoint

| Commit/run | Push workflow | PR workflow |
| --- | --- | --- |
| Baseline `e8f493f` | **FAILURE**, run `43080003378`; Debug 1111/1111, Release 1109/1111 | **SUCCESS**, run `43080016341` |
| This iteration | Automatic run created by the single normal push; exact head/run/result recorded in the PR and local CI appendix | Independent PR run, recorded with the same delivery head in the PR and local CI appendix |

The tracked report necessarily precedes its own commit/push. To avoid a documentation-only second push and redundant CI, delivery metadata and observed run results are written after publication to `artifacts/final-ui-refinement-20261112/CI_RESULTS.md` / `ci-runs.json` and the existing Draft PR description. Neither a baseline green run nor local tests count as CI for the delivered head. No workflow rerun, merge or release is authorized by this iteration.

The final public-source hygiene audit reports **zero findings** (`public-hygiene-delivery.log`). Before delivery, main and framework worktrees remain clean at their original commits, and the performance worktree's porcelain status matches the initial snapshot exactly.

## Remaining issues, separated by category

### Release blockers / unresolved implementation evidence

- The original Release mutation defect, the four Settings feedback-layout failures and the missing wizard palette have corrections and separate passing targeted/native evidence. Original failures remain available.
- Exact-head automatic CI must be assessed from the delivery appendix/PR. A red CI result is a blocker, not an environmental waiver.
- Native Batch active-analysis capture after a shell cycle remains NOT QUALIFIED, as detailed above. The actual images show the expected state, but the complete assertion sequence failed and remains a qualification gap.
- Full release qualification has not run in this iteration; partial tests cannot establish readiness to publish 1.3.0.

### Known visual differences

- Branding, candidate version, synthetic media, actual counters and supported commands deliberately differ from the reference artwork.
- Other contains only available working tools; it cannot reproduce reference tiles for unimplemented modules or invented recent operations.
- Batch retains its real four per-row actions and global import preferences. No unsupported per-row destination data or speculative throughput estimate is shown.
- Large text and narrower windows use vertical stacking/scrolling where required, rather than shrinking typography to preserve a reference-size layout.
- Settings retains eleven real categories across two rows and routes overview summaries to the full editors; it does not reproduce the reference's nine tabs and every inline shortcut. Other has four available tool tiles and shorter honest activity panels, leaving more free space than the reference's six tools/five invented activity records.
- History keeps the established 40-row page model and actual row actions rather than adding reference-only checkboxes/notes. Batch retains its separate pager and operational footer, and full quality text is available by tooltip where the narrow table elides it. LIVE does not invent chat/viewer/reaction counts.
- Dialog surfaces now match the shared Downloads palette, while a wizard opened over Classic intentionally retains the active profile's accent for toggles/progress and its input colors. Main branding and some typography, icon shapes, header spacing and button proportions still differ from the reference artwork. No pixel-perfect claim is made.

### Deferred environmental and release QA

Full local `Build-Release.ps1`, whole-suite Debug/Release/JavaScript, complete external Windows UIA/MSAA traversal, physical 5K/225% DPI, the complete DPI matrix, long-running soak, published-package/install-on-clean-PC qualification and broad 2750/10000-entry performance measurement remain the final release gate. Historical intermittent NavDownloads/Invoke/full-root UIA issues remain **DEFERRED / NOT QUALIFIED**, unless a repeatable normal-use product regression is found. Their earlier logs/reports are preserved. No updater installation is performed against the user's real installation.
