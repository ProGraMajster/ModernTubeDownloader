# Framework master integration for 1.3.0-candidate.1

Follow-up: the [2026-11-10 Dashboard stage](dashboard-completion-20261110.md)
implements the owner's component-test strategy. Historical native failures below
remain preserved as **DEFERRED / NOT QUALIFIED** until final controlled qualification.
The results below describe their original revision, not subsequent UI changes.

The central `ModernFormsNextCommit` now points to
`7bf58d06d795d7298c86d9bf9471bbfb46852bab`, the merged result of
[ModernFormsNext PR #433](https://github.com/ProGraMajster/ModernFormsNext/pull/433).
CI and `Build-Release.ps1` consume that exact source pin and reject a mismatched
or dirty framework checkout. The application version and runtime architecture
are unchanged. This does not authorize an application release or merge of Draft
PR #3.

## Framework provenance and compatibility

The earlier correction `5fe28ea` was replayed onto master `32b4fe3` in a new
isolated worktree. All 54 newer master commits were retained. The cherry-pick
had no textual conflicts and an identical stable patch ID; the overlapping
Control, Windows provider and window-message code was reviewed against newer
virtualization, lifecycle, RTL/layout, typography and resource-ownership changes.
Designer and Android changes were not replaced with older files.

The correction releases renderer-owned hidden buffers, scopes UIA publication
to fragment subscriptions and fixes MSAA descendant event identity/coalescing.
It retains the canonical semantic tree, caller-owned images and public APIs.
New native regression fixtures drive 100 subscribe/hide/reparent/show/unsubscribe
cycles for 8 and 800 descendants through an external Windows UIA client. They
verify stable identity, all 200 requested Name events, bounded MSAA delivery,
no descendant HIDE addressed to the root and zero hidden subtree bitmap bytes.

Local framework Debug and Release each passed 5,859/5,859 tests, with zero failed
or skipped. The [required Windows CI](https://github.com/ProGraMajster/ModernFormsNext/actions/runs/42511630347)
passed the same 5,859 tests on head `c675b1b` / PR merge revision `0f7a5a4`.
The final master has the identical tested Git tree. Master has no separate push
workflow; this is not a claim of an additional test run on the final merge SHA.
Existing Designer/VSIX warnings (three NU1902 and six VSTHRD010) were retained.
Bounded JIT and trimmed DynamicResource probes and the native ControlGallery
Button sample passed; no Android-device, Windows NativeAOT or manual screen-reader
qualification is inferred from these results.

## Application qualification

Evidence is retained in `artifacts/mfn-master-integration-20261108/`, including
raw failed attempts, logs, TRX, process samples, UIA results, HWND PNGs and hashes.
The earlier measurements in [stability-completion.md](stability-completion.md)
remain historical baseline rather than being rewritten as current results.

The strict local package on the merged pin passed Debug 982/982, Release 982/982,
JavaScript 92/92, zero failed/skipped, zero build warnings/errors, and package
hygiene for all 812 entries. The source change is limited to the central pin;
Classic and Modern still share the existing queue, history, Batch and LIVE services.

Before framework merge, the same source tree also passed production external UIA
playlist flows and 20 Classic/Modern transitions, physical 5K/225% DPI and
150/200% application text-scale probes. The predeclared same-fixture 5K A/B run
measured 967.89/970.36 MiB peak private memory on old/integrated framework. Timing
varied between runs, so this integration makes no new speedup claim.

Post-pin acceptance passed all 11 declared lanes: production external UIA playlist
and group flows, published Classic/Modern Batch and inspector operations, plus
separate Debug-bridge LIVE, appearance, restart and recovery scenarios. The first
PL lane spans an environment suspension and is functional evidence only, not a
latency or soak measurement. Its original output remains retained.

The unchanged performance/DPI matrix reported **7 PASS / 4 FAIL**. The full-root
10,000-entry scan, Light and Classic exceeded the 180-second guard. PL150 failed
the Batch visibility assertion after its HWND capture shrank to 160x28 pixels.
The separate reusable-pager audit passed all **69 Classic pages / 2,750 identities**;
the Modern audit reached **54 pages / 2,160 identities** before the same guard.
Those failures remain failures. No timeout, assertion or original test was relaxed.
Two fresh legacy-package comparisons on the same fixtures (reported-size and
10,000-entry) also failed with approximately two-second native Invoke calls.
This establishes that the symptom is not exclusive to the new framework pin;
it does not establish its complete cause or turn the current matrix green.

Current passing Modern reported-size and 10,000-entry runs peaked at 318.93 and
325.76 MiB private memory, with at most 40 History cards. Physical 5K/225% peaked
at 959.54 MiB; tracked hidden renderer bytes remained zero. The 1,300-job group's
first internal projection took 198.9 ms, while the external Invoke took 2,207 ms.
These are distinct measurements, not interchangeable claims about user latency.
All completed process audits retained identical queue/history/LIVE/Batch hashes.
Forced-stop lanes lack final cleanup samples and do not qualify final zero ownership.

The detailed integration report retains same-fixture comparisons, additional
diagnostic runs, original stacks, capture manifests and exact-head application
push/PR CI evidence. Draft PR #3 remains open for the outstanding native and visual
qualification; this pin update is not a release-readiness declaration.

## Limits of the evidence

Fresh-process timings are not cold-OS boot measurements. Process private bytes,
managed bytes, tracked images and renderer buffers are separate measurements;
their difference is not an exhaustive native-heap census. Roughly 1 GiB private
memory on a physical 5K monitor remains substantial. Large full-root UIA scans
remain costly and must be reported separately from reusable-pager tests.

Independent shell-switch runs exercise the repaired mechanisms; they do not prove
that every historical intermittent NavDownloads failure has been eliminated.
The known 200% pager-text clipping and remaining six-screen visual refinement
are outside this framework integration. Manual screen-reader, physical keyboard/
hover/Explorer drag, Internet-media, long-duration soak and owner visual acceptance
are not claimed. All original worktrees, dirty changes and reports are retained.
