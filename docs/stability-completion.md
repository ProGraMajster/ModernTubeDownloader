# 1.3.0 integration and stability qualification

This iteration integrates the large-history correction into the existing Modern
Dashboard branch. Classic and Modern continue to share the same queue, history,
batch store, LIVE scheduler and processing services. The version remains
`1.3.0-candidate.1`; this document does not authorize a release or merge.

## Integration and preservation

The initial audit recorded the clean master checkout, the stable-fix worktree's
30 existing changed files and the dashboard worktree's 37 existing changed files.
It saved their SHA-256 inventories, copies and binary Git diffs before editing.
Common fixes were compared individually. History filtering/paging, thumbnail
ownership and disposal were adapted to retain Modern filters, selection, inspector,
playlist provenance, export and canonical job actions. No existing data is migrated
or deleted by this correction.

The original framework checkout is untouched. A separate framework worktree carries
the rendering/accessibility corrections; the central framework pin identifies the
tested commit. There is no private fork of the application runtime or semantic tree.

## Diagnosed causes and corrections

* History previously materialized every record twice while still hidden during
  startup. It now filters a canonical snapshot off the UI thread and presents at
  most 40 reusable cards. Generation/cancellation checks reject obsolete results.
  Filter counts and exports still cover the whole matching collection.
* Thumbnail decoding and uncontrolled ownership retained native Skia allocations.
  The decoded LRU is limited to 32 MiB, four decoding workers and a maximum 512 px
  display dimension. Controls own their displayed copies; rebinding/disposal
  releases obsolete copies. Explicit export retains source-resolution semantics.
* Application forms explicitly dispose their public control trees, including
  thumbnail bindings and timers. The framework window's own disposal alone did
  not release those application-owned descendants.
* Shell/theme changes repeatedly published partially configured descendants to
  native accessibility. The same canonical pages are prepared off-window and
  attached once; layout and typography avoid redundant recursive work.
* A captured stalled UI stack ended in `NotifyWinEvent` through
  `WindowsAccessibilityService.NotifyClients`, `Control.OnVisibleChanged` and
  `MainForm.ShowView`. Descendant events incorrectly targeted the HWND client's
  `CHILDID_SELF`; hiding a page could therefore announce a hidden window client.
  The existing MSAA provider cache now assigns stable weak descendant event IDs.
  Parent SHOW/HIDE covers descendant visibility; per-dispatcher coalescing removes
  redundant native events without changing shared observers or UIA semantics.
* The global UIA listening flag caused unrelated listeners to trigger a full
  fragment event storm. The native fragment root now implements
  `IRawElementProviderAdviseEvents`, with per-event/property reference counts and
  borrowed SAFEARRAY ownership. An out-of-process Windows client verifies that
  subscribed property events still arrive. Events are not globally disabled.
* Hidden controls retained render buffers proportional to physical pixel area.
  Effective visibility loss now releases only renderer-owned buffers; showing the
  page recreates and repaints them. A regression verifies child/parent disposal,
  unchanged identities/state and byte-identical rendered output after return.
* Structural playlist expansion keeps the queue host attached and prepares only
  new visible rows. The 40-row cap, continued header, canonical GUID bindings and
  existing in-place page reuse remain in effect; there is no off-page row pool.
* The compact sidebar reserves its scrollbar width at 150/200% text scale.

## Measurement protocol

Local evidence is retained under `artifacts/stability-completion-20261108/`, including
the initial inventory, original failed runs, stacks, process samples, diagnostic
JSONL, native result JSON, TRX and actual HWND PNGs with hashes/DPI manifests.
`docs/large-history-performance.md` records the preceding P0 investigation and its
unresolved failures; it is historical evidence, not the current qualification.

Fixtures are isolated temporary copies: 2,750 history records / 2,617 queue jobs /
2,750 distinct local images, and 10,000 history records / 3,000 jobs / 10,000 images.
Durability checks compare queue/history/LIVE and Batch files before and after.
No external media downloads or private user profile are used. The matrix also
covers Classic, light theme, Polish 150%, English 200%, 120/1,300-job groups,
repeated shell switches and a physical 5120x2880 monitor at 216 DPI (225%).

`Test-Performance.ps1` retains its 180-second/6-GiB safety guard and 15-second UIA
lookup deadline. `Test-StabilityMatrix.ps1` runs each declared lane once and retains
failures. Native screenshot readiness observes the asynchronous Invoke result
within the existing bound, rather than asserting that an already-created hidden
page is immediately visible. Lookup, pattern acquisition and Invoke times are
recorded separately. No timeout or CI retry is used to obtain a pass.

The first-window timing is a fresh process, not a cold operating-system boot.
Private bytes, working set, managed bytes, owned image bytes and render-buffer bytes
are distinct measures; private-minus-managed is not a precise native-heap census.
Render diagnostics are opt-in and read-only. PNG capture can itself affect peak
memory and heartbeat gaps. The same-machine comparisons do not predict every PC.

## Reproduction

Generate synthetic profiles with `qa/PerformanceProbe` and preserve their manifest.
Pass a JSON array with `name`, `fixture` and `arguments` to
`scripts/Test-StabilityMatrix.ps1 -ApplicationPath <candidate-exe>
-FixtureManifest <matrix-json> -EvidenceRoot <new-directory>`.
Each lane passes its arguments to the existing performance harness. Use
`scripts/Build-Release.ps1 -ModernFormsNextRoot <clean-pinned-checkout>
-ArtifactsDirectory artifacts/<new-qualification-directory>` to preserve previous
packages and logs. A custom artifacts directory must be new and inside this checkout.

## Final results

Local full candidate validation passed: Debug **982/982**, Release **982/982**,
JavaScript **92/92**, zero failures or skipped tests, and package hygiene **812
entries PASS**. Both application builds produced zero warnings/errors. The pinned
framework correction (`5fe28ea26b8f65bc4d8ee37c83bb5bf0938fb114`) passed its full
Debug **4484/4484** and Release **4484/4484** suites. The framework build retains
three pre-existing NU1902 warnings in Designer.VSIX. Its native ControlGallery
startup/capture/shutdown smoke passed; this is not screen-reader acceptance.

Final published candidate measurements (same machine, fresh processes):

| Scenario | First HWND | First History readiness through UIA | Peak private memory | Median History page render |
| --- | ---: | ---: | ---: | ---: |
| Modern H2750 / Q2617 / I2750 | 2.497 s | 2.388 s | 315.50 MiB | 34 ms |
| Modern H10000 / Q3000 / I10000 | 2.929 s | 1.606 s | 326.42 MiB | 35 ms |
| Modern PL text 150% | 2.683 s | 3.975 s | 308.58 MiB | 38 ms |
| Modern EN text 200% | 2.939 s | 3.552 s | 320.46 MiB | 36 ms |
| Physical 5K / Windows 225%, H2750 | 2.412 s | 3.754 s | 984.14 MiB | 47 ms |

UIA readiness includes client lookup and Invoke, so it must not be substituted for
the instrumented page-render measurement. The large case kept 40 History cards,
3,544 total controls, at most 494 renderer buffers and 81 displayed image bitmaps;
the decoded cache reached 56 images within its byte budget. Counter maxima need
not occur in the same sample. Hidden renderer bytes were zero in the final lanes.

The directly comparable 5K baseline used 1564.75 MiB peak private memory, showed
its first HWND in 5.206 s and rendered History pages at a median 423 ms. The final
5K History PNG is byte-identical to the baseline PNG (5120x2880, DPI 216), despite
the buffer-lifetime change. A diagnostic intermediate build attributed roughly
729 MiB to hidden renderer buffers; the final build retains none. Approximately
1 GiB total private memory at 5K is still substantial. These counters do not fully
attribute all native heap/graphics allocations or establish long-soak behavior.

The earlier stable v1.2.1 P0 comparison remains separate: H2750 startup 38.675 →
1.629 s and private memory 3051 → 315 MiB. The v1.2.1 10,000-image baseline crossed
the 6-GiB safety guard without a window. This iteration's Modern results must not
be presented as a same-shell comparison with those Classic measurements.

### Functional qualification

Production Release, external Windows UIA passed PL/Dark and EN/Light playlist
transfer/group flows, 120/1,300-job fixtures, GUID/order/restart assertions and
MessageBoxForm confirmations. PL completed 20 Classic/Modern transitions with
the unchanged 15-second lookup limit. The published Classic-to-Modern scenario
also passed 1,300-entry Batch import, cross-page selection/analysis/handoff of 60
entries, cancellation/reopen, inspector editing and all Modern routes.

Separate Debug bridge scenarios passed synthetic Batch in both shells, active
LIVE Stop & Save/details, scheduled LIVE, replay navigation, appearance/text
100/125/150/175/200/reset, 20 Classic/Modern pairs with restart, and `--classic-ui`
recovery retaining the saved Modern preference. These are not external UIA tests
or real network-media qualification; the canonical runtime uses controlled FakeTool
processes and output files.

The first acceptance plan retained **6 PASS / 4 FAIL** caused by incorrect runner
inputs: the published client expected Classic startup but was given Modern; two
Batch lanes requested real installed tools without supplying them; LIVE received
a VOD URL and correctly did not enable recording. The corrected, explicitly
synthetic plan passed **5/5** (including both active and scheduled LIVE). No
application change, timeout increase or assertion removal was used for those
configuration corrections. Original plans/results/logs remain available.

There are 224 indexed original HWND PNGs in the final matrix and acceptance
evidence, with dimensions, SHA-256 and source profiles. Public-source hygiene
reported zero findings. Exact-commit remote CI is recorded on Draft PR #3 and in
the local final report; older-commit CI is not evidence for this change.

## Native discovery versus page work

The final unchanged-discovery matrix finished **10 PASS / 1 FAIL**. Passing lanes
were reported-size Modern, 10,000-entry Modern, light theme, PL150, EN200, physical
5K225, EN200 shell changes, Classic, and 120/1,300-job groups. The former reported,
large and EN200 failures passed; the complete discovery scan below did not.

The comparable EN200 shell baseline exceeded 180 seconds and recorded a maximum
51.463-second UI heartbeat gap. The final candidate completed six shell changes
with a 542 ms maximum gap and no horizontal sidebar scrolling. The historic
intermittent NavDownloads lookup failure is not assumed to have a single cause;
the correction addresses the publication/event mechanisms observed in this audit.

For the same 1,300-job group fixture under native UIA, first expansion's internal
UI work fell from 12148.8 to 597.0 ms, including attachment 11755.6 → 206.8 ms.
Subsequent expansions took 233.3 and 176.4 ms. This audit's slow baseline is not
the earlier report's 1–1.4-second run and is not an average. The final result is
improved but not a claim of instantaneous expansion on every computer.

The original full-tree discovery scan remains a **FAIL** at the unchanged
180-second guard. Its `HistoryNext` lookup alone cost about 0.55–1.12 seconds per
page, followed by another full-tree summary lookup, while Invoke/page rendering
typically took tens of milliseconds and the UI heartbeat remained responsive.
Its original logs and missing final-disposal sample remain recorded as failures.

`-ReusePager` is a separate, explicit native audit protocol: retain the same pager
and summary providers, invoke every page, and compare every displayed row's
canonical GUID/part and order with the fixture. It does not shorten the collection,
skip assertions, change deadlines, or retry a failed lane. The discovery scan and
this audit are reported separately; their total elapsed times are not interchangeable.
Large full-tree UIA queries remain an accessibility-client performance limitation.

The separate `verified-large-scan` passed once: **250 pages / 10,000 canonical
identities**, 147.19 seconds for the entire native scenario, 403.99 MiB peak private
memory, 29.28 ms median page render and 459 ms maximum UI heartbeat gap. It retained
40 History cards, unchanged persistence hashes and zero owned image/cache/request
counters after shutdown. The higher peak than the 30-page scenario is reported
without claiming a flat private-memory curve or an exhaustive native leak audit.

## Remaining qualification and screen finishing

The evidence supports bounded collections and the diagnosed lifetime/publication
repairs, not unconditional release readiness. Manual screen-reader interaction,
physical hover/Shift+Tab, Explorer drag/drop, Internet media-service behavior,
owner visual acceptance and prolonged memory soak are not qualified by this round.
Physical DPI coverage is 96 and 216; application text scaling is a separate axis.

For the final six-screen pass, retain the existing services and concentrate on:

* **Downloads:** pager label width at 200%, selected child/header visibility at
  narrow heights, and visual hierarchy of expanded playlist rows.
* **History:** table density, truncation and inspector/action spacing across scales.
* **LIVE:** inspector density and real scheduled/recording/replay/error states.
* **Other:** consistent tool-card spacing and long translated descriptions.
* **Batch Import:** narrow column priorities and details-panel readability.
* **Settings:** scaled category navigation, keyboard traversal and preview density.

Automated native success does not constitute pixel-perfect acceptance of these
screens. Draft PR #3 remains draft; no merge, tag or release is part of this work.
