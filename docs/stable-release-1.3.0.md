# Stable 1.3.0 release qualification

This is the source-freeze record for the owner's explicitly authorized stable
release. Publication remains conditional on the gates below. It does not turn
historical candidate failures or unexecuted qualifications into passes.

## Scope and implementation

- Product/informational version is `1.3.0`; assembly/file version remains
  `1.3.0.0`, .NET 10 and ModernFormsNext pin
  `7bf58d06d795d7298c86d9bf9471bbfb46852bab` are unchanged.
- English canonical notes and the Polish summary describe the actual
  `v1.2.1..feature/modern-dashboard-ui` changes. The changelog retains the
  historical 1.2.1 section. The release page provides the actual publication date.
- What's New immediately shows the exact installed bundled record, then fetches
  only the corresponding official GitHub tag through the shared HTTP client.
  It does not discover or install updates. Four retained read-only editors keep
  complete text selectable and scrollable without full-document native bitmaps.
- The reader validates version/tag/publication state/official URL, limits the
  payload/body, supports cancellation, throttling, ETag/304 and an atomic bounded
  cache. Invalid/offline responses retain validated cache or local notes. Source
  and English fallback are explicit. Later/X never acknowledge; Done uses the
  existing durable experience save.
- Classic/Modern share existing runtime and stores. No framework modification,
  new NuGet distribution, user-profile migration rewrite or updater replacement.

## Targeted implementation evidence

Evidence root: `artifacts/stable-release-20261119` (preserved locally).

- `targeted-02`: 234/234 tests passed, zero skipped, covering HTTP/cache,
  migration, updater/policy, local catalog and experience presentation. This
  precedes the bounded-editor refinement and is not final-package qualification.
- `targeted-08`: 83/83 tests passed after the bounded-editor refinement, including
  the real official notes and a near-limit document at 200% text scale, retained
  controls/focus/scroll, full text access, cancellation and cache reopening.
- The first build found an incorrect test focus API. Subsequent integration
  builds exposed TextBox API-name/type mismatches; all original logs remain.
- `targeted-06` exposed a real dispose-before-Closed cancellation lifetime bug;
  the Closed callback now respects an already disposed owner. `targeted-07`
  exposed stale assertions requiring Label for content now held in a read-only
  editor. Tests now assert the exact complete text and read-only semantics; no
  assertion content, timeout or scenario was removed.
- Default presentation fixtures now use controlled HTTP rather than depending
  on whether the public release already exists. Online, offline and malformed
  responses have separate deterministic tests.

## Final gates and publication ledger

At this source freeze the final Debug/Release/JavaScript build, production
package/native scenarios, final feature CI, merge, actual-master CI, tag and
public asset verification are **PENDING**. Record terminal results, exact SHAs,
workflow URLs, hashes, raw logs and screenshots in the publication evidence and
PR #3 before declaring publication. Earlier green runs are not transferred.

Use the existing `Build-Release.ps1`, a new artifact directory and ordinary
GitHub Actions release workflow. After feature CI succeeds, use an exact-head
normal merge, verify merged-master CI, then create one annotated `v1.3.0` tag.
Never rewrite a published tag or bypass a failed validator. Independently
download/hash the public assets and verify both exact-tag and latest APIs.

Actual published 1.2.1 updater service qualification must use an isolated
headless process loading verified official binaries: its old GUI startup can
change shared HKCU registrations even with a separate data root. This evidence
is explicitly distinct from an old-GUI or end-to-end installation test.

## Qualification follow-up (2026-11-19)

The product source frozen at `36e3b5205a4f6fb5e6facf496292e3c018fcd385`
passed local `Build-Release.ps1`: Debug 1597/1597, Release 1597/1597,
JavaScript 93/93, zero skipped tests, and 816 package entries. The matching PR
CI run 44591427628 also passed. Push CI run 44591413530 failed one pacing
test (1596/1597 Debug); its Release/package stages were not executed. Both
original runs, logs and TRX files are retained. A green PR run does not cancel
the push failure.

The single-worker pacing test previously compared executor wall-clock times,
which include variable durable-checkpoint and scheduler latency after queue
admission. It now uses the existing controlled admission clock, verifies the
free slot and waiting item, and requires the full exact 500 ms interval. The
three related targeted Release tests pass. Runtime, deadlines and queue policy
are unchanged; both CI events on the new head remain required.

The first production-wizard probe encountered an atomic experience-file save
failure. Its `Get-Content` observer could deny delete sharing while the product
replaced the ledger. An isolated Windows PowerShell/.NET experiment reproduces
that exact UnauthorizedAccessException and proves that a snapshot reader using
ReadWrite/Delete sharing permits replacement while retaining a coherent old
snapshot. Only the observer was corrected; JSON assertions, action counts and
timeouts remain intact. The original failure is preserved, and the corrected
native probe must complete before publication.

The actual unpacked product passed six helper scenarios and production UIA
navigation through both shells, with 1300 Batch imports, selection/analysis/
handoff of 60 items, cancellation and reopening. A separate What's New probe
was interrupted by an external foreground transition and is not a pass.
Remaining native and public-asset results belong in the terminal publication
ledger; no merge, tag or public release is asserted by this source document.

## Master qualification follow-up (2026-11-20)

Both push and PR CI passed on `82261d642b45b6ea3b234b233086a579459e6ee9`
(runs 44597376600 and 44597389747, Debug/Release 1597 each, JavaScript 93).
PR #3 was normally merged as `b8cc26a127c09acd8779c8a02ecfce4d1d765468`.
Actual-master run 44602383522 then failed one Debug test: the five-VOD/three-LIVE
scenario retained a completed recording's worker while a pending recording did
not start. Release/package stages were not executed. No tag was created and no
release was published at that point. Original logs and failures remain intact.

Inspection found that every JSON file shared one global semaphore. The LIVE
worker correctly awaits its final durable save before relinquishing ownership;
an unrelated slow JSON read could hold that global semaphore and prevent this
handoff. Three deterministic regressions failed before the fix, including an
actual LIVE scheduler blocked behind a controlled reader in another store.
The exact semaphore owner in the historical CI failure is unknown: that runner
did not provide a process dump. The controlled reproduction establishes the
cross-file blocking mechanism, not a claim about that unobserved owner.

The follow-up branch replaces the global gate with reference-counted gates for
canonical absolute paths. The captured path is used for both gate identity and
I/O. Waiters retain the gate, cancellation does not release another operation's
permit, and the final holder/waiter retires the gate. Atomic replacement, flush
behavior, LIVE final-save ordering and the original integration-test assertions
and deadlines are unchanged. This is canonical path identity, not physical
identity across junctions or hard links; existing stores use the common AppPaths
root. New lifecycle tests cover cancellation, reacquisition, disposal races and
recovery after read, serialization and publication failures.

The targeted atomic suite passes 19/19, including the unchanged three RED
regressions. Full final validation, new package checks and CI must qualify the
follow-up commit and its actual master merge before the stable tag is allowed.

### Qualification fixture races

The first full local run on `51c970a` passed Debug 1607/1607 and failed Release
1606/1607 during thumbnail fixture cleanup. Export, history-failure handling and
decoded-dimension assertions had passed; Windows rejected deleting `export.png`
with sharing violation `0x80070020`. Original logs/TRX remain preserved and that
run did not produce a qualified package.

A four-case isolated experiment established that a native Skia file stream/path
codec can pass its CRT file handle to a concurrently launched child. After the
parent disposes the stream, exclusive access remains blocked while the child is
alive and succeeds immediately after its explicit exit. The child receives no
image path. The native-without-child and managed FileStream/SKData controls both
allow immediate exclusive access. All four cases passed with exact SkiaSharp
3.119.2 and native binary hashes retained. This explains a reproducible race in
the fixture's direct `SKBitmap.Decode(path)` usage; the historical child identity
was not captured. The production thumbnail cache already uses managed reads.
The remaining fixture decoder now reads bytes through a managed handle, keeps
all original assertions, and additionally requires immediate exclusive access.
Cleanup still fails on a genuinely held file; no retries or sleeps were added.

Push CI 44780967884 on the same commit failed Debug 1606/1607 in the mixed
Batch/VOD/LIVE/organizer scenario. Its test-only FakeTool wrote a readiness JSON
directly to the final filename, so the observer could see three filenames and
read one while its writer was still open. The producer now closes its staged
write before atomically publishing the marker. Original consumer assertions and
deadlines remain intact. A controlled real-child handshake holds staging open
to prove the final marker is absent, then verifies complete PID/ownership data
and immediate exclusive access after publication. This changes the test protocol,
not production readiness or runtime behavior.

The targeted Release run passes all 12 thumbnail, marker-publication and
original mixed-runtime/five-process integration cases, with zero skipped tests.
The first attempted targeted build overlapped an unfinished FakeTool edit and
did not execute tests; its compiler log is retained separately. Complete frozen
head and package validation remain required after these fixture corrections.

## Native observer qualification follow-up

The frozen `5fcef77` product/test graph passed local Debug 1609/1609, Release
1609/1609, JavaScript 93/93 and 816-entry package validation. Both exact-head CI
events also passed (44784331104 and 44784347741, attempt 1). The actual unpacked
EXE passed helper qualification (6 cases) and the Classic-to-Modern-to-Classic
UIA journey, including 1300 Batch entries and 60-item analysis/handoff. An initial
misconfigured run started Modern while expecting Classic's separate BatchWindow;
it is retained as a failed harness invocation, not a product window defect.
Separate Debug native workflows passed organizer/undo, playlist persistence and
grouping, LIVE stop/save, upgrade choice, and a real public VOD download/probe.

The new production wizard run exposed a remaining observer race: its JSON
polling could briefly hold a destination open while the application replaced it.
The earlier snapshot experiment established success only after reader close.
A new named-pipe-controlled experiment held the actual Read-Experience stream
immediately after Open(Read, ReadWrite|Delete), before CopyTo/Dispose. Windows
rejected replacement with the same UnauthorizedAccessException/0x80070005,
preserved the old JSON, and allowed replacement immediately after explicit
reader close. This proves a remaining interference mechanism; it does not
identify an untraced historical handle owner. The original native FAIL remains.

The native client now waits for the exact UI progress value, the target step's
control and the enabled Next button before asserting the persisted snapshot.
Setter checks cross an existing awaited Next/Close boundary, and acknowledgement
is read only after the notes window closes. The JSON values and all earlier
assertions remain required; no timeout, arbitrary delay, retry or production
write behavior changed. The fresh production run passed Draft, Complete and
Restart on the same package, including six steps, retained values, native close,
three process lifetimes and durable acknowledgement. Original FAIL and corrected
PASS have separate profiles, logs and captures. This script-only correction
still requires exact-head CI; the product/test graph is byte-identical and its
completed local full build is not repeated.

## Accepted issue and remaining boundaries

The owner accepts [ModernFormsNext #534](https://github.com/ProGraMajster/ModernFormsNext/issues/534)
for this release: maximization can cover a persistent Windows taskbar or produce
an incorrect strip. Use a restored window; auto-hide may help. This behavior is
documented and does not itself block 1.3.0. A new data-loss/security/updater/package
or repeatable critical-crash defect still blocks publication.

Independent popup/tooltip scenarios remain NOT QUALIFIED. Physical 5K/225% DPI,
long memory soak and complete native-speaker editorial review are NOT EXECUTED.
Synthetic tests do not certify every private source, physical power loss or
another user's collection. Native UIA/directed HWND evidence is not a claim of
physical-input or screen-reader qualification. Existing historical FAIL logs,
worktrees and data remain preserved.
