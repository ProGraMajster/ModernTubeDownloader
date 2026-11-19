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
