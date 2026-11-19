# Modern Dashboard — functional completion stage, 2026-11-10

This stage extends LIVE, History, Batch Import, Other and Settings on `feature/modern-dashboard-ui`, starting at `c39d23336106310d2b40440a13038dafd8b4e1e1`. It follows the owner's new implementation-first strategy: component tests and native captures, with full release qualification deferred. This is **not release approval or complete reference parity**.

ModernFormsNext remains pinned to merged master `7bf58d06d795d7298c86d9bf9471bbfb46852bab`. Classic uses the existing views and services. No second queue, recorder, history or Batch store was introduced. The main checkout, the dirty history-performance worktree and historical reports were left intact. No framework source, version, tag, release or NuGet publication was changed.

## Implemented behavior

- **LIVE:** provider filters, culture-aware title/channel/URL search, newest/oldest/title sorting, accurate active/recent counts and paging with at most 40 materialized cards. The recent tab includes resumable interrupted sessions. Selection is exposed through the existing accessibility contract and keyboard Enter/Space. The inspector shows actual recording metadata and routes stop/save, edit, resume and replay analysis to the canonical service. Actions re-resolve the session when invoked. Busy/error analysis stays visible; hidden views do not refresh cards every second. Toolbars, status/actions, tabs and inspector reflow with larger text.
- **History:** first/last/direct-page navigation on the existing 40-row reusable pager. Selection and inspector now follow the visible page; an empty filter clears stale details. Classic keeps its pager. The 10,000-entry fixture checks retained card identities, jumps to the final and twentieth page, filtering and ordering.
- **Batch Import:** the Modern “Paste links” command imports clipboard text through the same parser, validation, deduplication, durable session and explicit queue handoff as file imports. Clipboard input is bounded to 8 MiB UTF-8; empty/oversized inputs fail with localized messages. No implicit download starts. Existing TXT/CSV/JSON limits and file imports remain covered. New chrome applies its existing typography recipes after attachment, fixing undersized first-frame headings/counters. Classic controls are restored on switching back.
- **Other:** the file drop area now contains an accessible, working file-picker button. It navigates to the existing Batch page and uses its native picker and import flow; it does not own a parallel import session. Existing drag routing and tool history remain in place.
- **Settings:** search now includes translated option captions, not only category names, with case/diacritic-insensitive matching. Enter opens the sole matching category; no-match feedback is visible. User-entered values, credentials and paths are not search-indexed. Clearing the search restores the categories.

## Problems found and corrected in this stage

1. Paging LIVE disposed old cards without removing them from the parent collection. The framework's disposal contract does not detach controls. Cards are now explicitly detached before disposal; an 85-session test verifies 40/40/5 materialization and unchanged persisted IDs.
2. History could show the previous page's selected item in the inspector. Page rendering now preserves a selection only when it remains on that page, otherwise choosing the first visible item or clearing the inspector.
3. Detached Batch chrome received Classic-sized fonts before attachment to a Modern root. Reapplying the existing recipes once after attachment fixes the first frame, including counter captions and table headers. Geometry tests cover 100/150/200% text.
4. Initial LIVE captures exposed a clipped recent-tab caption. Tab widths now use measured text; narrow layouts stack tabs. Long state captions have ellipsis and full-text tooltips.

No new startup/RAM speedup is claimed. The 10,000-session test proves projection completeness and identity preservation; it is not a native 10,000-session latency benchmark.

## Targeted verification

Raw evidence is retained locally under `artifacts/dashboard-completion-20261110/`, including TRX, logs, original native PNGs/semantic trees, synthetic profile snapshots and a SHA-256 manifest. Counts below overlap and must not be added as unique tests.

| Run | Scope | Result |
| --- | --- | --- |
| `live-targeted-03` | LIVE presentation, projection, navigation and subsystem | 31 PASS, 0 FAIL, 0 skipped |
| `dashboard-targeted-04` | LIVE plus shared inspector consumers in Downloads/History | 57 PASS, 0 FAIL, 0 skipped |
| `components-06` | History paging/presentation, Batch parser/service/presentation, Settings | 60 PASS, 0 FAIL, 0 skipped |
| `batch-presentation-07` | Final Batch typography and clipboard-view/picker dispatch | 4 PASS, 0 FAIL, 0 skipped; no compiler/analyzer warnings in this build |

Component builds used Debug, the exact framework source pin, sequential MSBuild (`-m:1`, shared compilation disabled) and `GeneratePackageOnBuild=false`. The first earlier build generated a local framework package; nothing was published. The 60-test run included an analyzer warning from a blocking assertion on an already completed task; the test now awaits the completed task, and the final four-test run is warning-free.

Important coverage includes 10,000 LIVE projection identities; 85 persisted LIVE sessions with page/filter/sort changes; 10,000 History entries in Classic and Modern with 40 reused controls; canonical clipboard deduplication, queue handoff idempotency and restart IDs; existing large JSON/CSV bounds and cancellation/recovery; localized Settings search; and component geometry at 100/150/200% text.

Retained unsuccessful attempts:

- `live-targeted.log`: test compilation failed because `Control.IsDisposed` is internal; the test now observes the public `Disposed` event.
- `live-targeted-02`: 30 PASS / 1 FAIL exposed the detached-card defect described above.
- `history-live-targeted-05`: 24 PASS / 1 FAIL was a test lookup using `Button` for the new `NumericUpDown`. Corrected to the actual control type; the case passes in `components-06`.
- The first in-memory smoke invocation lost PowerShell's `PSScriptRoot` and failed before launching the app. Subsequent runs invoke the existing script directly. The original empty log is retained. No product timeout was increased and no assertion was weakened.

## Native captures and interaction checks

All images are real HWND captures of the application. They use isolated **synthetic QA data**, existing test thumbnail fixtures and the controlled FakeTool process. No production-only screenshot data or pretend controls were added. This is Debug semantic-bridge interaction evidence, **not full external Windows UIA qualification**. Normal startup in each lane first verifies no bridge is exposed and shuts down cleanly.

| Lane | Geometry | Verified behavior | Capture folder under `native/` |
| --- | --- | --- | --- |
| LIVE recording | 1536×1024, DPI 96, text 100% | Analyze → real controlled recording → details → stop/save/output | `live-native-active-02` |
| LIVE final | 1536×1024, DPI 96, text 100% | Current sessions, selection, empty provider filter, search, recent/replay action | `live-native-final` |
| History | 1536×1024, DPI 96, text 100% | Selected canonical history record and inspector | `history-native` |
| Batch final | 1536×1024, DPI 96, text 100% | CSV import and analysis: 12 rows, 10 ready, 1 duplicate, 1 invalid | `batch-native-final` |
| Other | 1536×1024, DPI 96, text 100% | Working picker exposed/enabled and tool layout | `other-native` |
| Settings | 1536×1024, DPI 96, text 100% | General overview, option-caption search and Downloads category | `settings-native` |
| LIVE large text | 1920×1080, DPI 96, text 150% | Same targeted interactions; compact sidebar and scrollable inspector | `live-native-150` |

All listed lanes passed. The evidence directory also retains the intermediate LIVE/Batch images, for **27 original PNGs** total. The 150% lane changes application text size, not Windows DPI. At that size the page and inspector scroll vertically; their entire contents are not simultaneously visible. No 225% physical-monitor result is claimed in this stage.

Visual comparison with the supplied references found and corrected the tab and Batch typography issues. LIVE now has the reference's source strip, two session tabs, compact rows, inspector and bottom activity area. History keeps the reference's table/inspector relationship with a functional direct pager. Batch has working clipboard/file entry paths, counters, analysis rows and summary. Other and Settings retain their actual supported tool/setting inventories instead of displaying reference-only features.

## Deferred qualification and remaining work

- Historical intermittent `NavDownloads`, approximately two-second native Invoke, full-root UIA discovery and missing context-menu cases: **DEFERRED / NOT QUALIFIED**. Existing failures, captures and call stacks in `artifacts/mfn-master-integration-20261108/` remain unchanged. Environment contention is a hypothesis, not an established cause or fix.
- Full local Debug/Release, JavaScript, external Windows UIA, five-resolution/DPI matrix, 5K/225% memory, cold startup, long soak, published-package and full large-collection performance qualification: **NOT EXECUTED in this stage**, reserved for final 1.3.0 qualification. Existing automatic PR CI is not a substitute for these native checks.
- Physical clipboard round-trip and opening/completing the OS file picker from Other: **NOT EXECUTED end-to-end**. Clipboard parsing/view rendering and picker dispatch are component-tested; the native lane verifies the exposed controls. Physical Explorer drag remains deferred.
- Real Internet streams/provider authentication, screen reader and owner visual approval remain unqualified. Real stop/save was exercised using the existing controlled process, not a public streaming service.
- Reference parity remains partial: LIVE chat/viewer statistics and several reference-specific recording options are not implemented; Other has the actual Batch/thumbnail tools, not fictitious conversion/subtitle modules. History notes/bulk actions and Batch thumbnail/destination-column refinements still need a scoped implementation decision. Settings still uses overview summaries linking to full categories for several groups. Further density, spacing and icon refinement across six screens remains appropriate.
- Downloads/playlist group behavior was not redesigned in this stage. The earlier native playlist expansion/automation blockers remain deferred; the shared inspector's Downloads consumer was covered by targeted presentation tests.

Keep Draft PR #3 open and unmerged. Version remains `1.3.0-candidate.1`. The stage is useful functional progress, not a declaration that all five screens or release 1.3.0 are finished.
