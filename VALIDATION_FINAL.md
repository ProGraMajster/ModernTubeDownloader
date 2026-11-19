# Modern Dashboard — validation evidence

20 October 2026. Visual acceptance is independent of automated tests. Existing branch and Draft PR #3 are retained. No release, merge, tag, version change or MFN modification.

Implementation checkpoint: `7c31b67` (44 source/test/helper files). Full build and subsequent native matrix use this implementation. Reports are committed separately; their later commit does not alter executable sources.

## Evidence from this round

| Lane | Result | Evidence / limits |
| --- | --- | --- |
| Focused Debug tests | PASS, 131/131, 0 skipped | `artifacts/dashboard-visual-first/30-tests.log`, `ModernTubeDownloader.Tests/TestResults/iteration30.trx`; includes rendered metric numbers at 100/150/200 and output-format facts |
| Native PL/Dark/100 | PASS automation | `31-populated.log`: 1536x1024, HWND DPI96, all routes, populated Batch/LIVE, one Classic/Modern switch, same-profile restart, one owned instance and clean exit |
| Final six-view populated capture | PASS native automation | `56-final-populated.log`: final implementation, all six original HWND captures and comparison sheets opened; real PNG/JPEG export flow, mixed Batch rows, active FakeTool LIVE, Classic/Modern switch and restart/clean exit |
| Empty queue | PASS automation | `38-empty.log`: empty queue/inspector and zero activity widgets; routes, switch and restart. Native image opened |
| Native thumbnail PNG/JPEG | PASS HWND flow | Runs 24/25 completed module but stopped later at Explorer; runs 29/31 completed whole native suite. Actual 960x540 PNG and JPEG decoded; one persistent history record per export. Tool UIA actions + Windows dialog HWND controls, not common-dialog UIA qualification |
| Native shell file-drop adapter | PASS same-process HWND test | Unicode, multi-file rejection, enabled/disabled/detached states; separate from real Explorer |
| Physical Explorer drag | NOT EXECUTED to completion | Endpoint ownership guard found BeamNG.drive covering the source. No mouse gesture injected. User selected continuation of other tests |
| Full release build/test/package | PASS | `32-full-build.log`: Debug 864/864, Release 864/864, zero skipped; JS 92/92; builds zero warnings/errors; package hygiene 812 entries |
| Light/large text/5K matrix | PASS automation | `33-pl-light-100.log` 1536x1024/DPI96; `34-pl-dark-150.log` 1536x1024/DPI96; `35-en-dark-200.log` 1920x1080/DPI96; `36-pl-dark-5k225.log` actual 5120x2880/DPI216, PL/Dark/TextScale100. All routes, shell switch and same-profile restart |
| Source hygiene | PASS, zero findings | `32-source-hygiene.log`, final `58-final-source-hygiene.log`: 323 working candidates, 35 commits, 785 historical blobs; no audit-rule exemptions added |
| 20 Classic/Modern cycles | PASS automation | `39-switch20-many.log`: 100 canonical entries, 40-row paging/next/previous identities, unchanged queue across 20 switches, same-profile Modern restart, single runtime/tray registration checks and clean exit |
| Classic recovery/appearance/storage | PASS native automation | `40-classic-recovery.log`, `41-classic-appearance.log`, `42-classic-hotfix.log`, `48-classic-range.log`: saved Modern preference survives `--classic-ui`; live text scaling/persistence; bounded JSON import, ENOSPC queue protection, custom range |
| Tray/cookies/Web Remote/protocol/updates | PASS native automation | Runs 43-47: native tray/same-instance restore; settings without real browser cookies; authenticated localhost pairing and protected endpoints; external URI handoff; localized Stable update check without installing a release |
| Canonical Batch and active LIVE | PASS native automation | `49-modern-batch.log`: import/duplicate/analyze/selected queue handoff/cancel. `50-modern-live.log`: controlled FakeTool active recording, details and finalization. No public Internet stream qualification |
| Custom range/layout/theme roundtrip | PASS native automation | `52-layout.log`: canonical persisted range; Full/Custom analysis and inspector; owned-HWND theme change both ways; measured inspector without horizontal overflow. Native dark/light/custom-range images opened |
| Controlled active transfer | PASS native automation | `53-transfer.log`: actual queue/executor/progress route with FakeTool, active screenshot and completion. Admission Resume/Pause is exercised; real transfer pause/resume is not qualified |
| Production Release UIA (PL) | PASS | `51-release-uia-pl.log`: external Windows UIA, both interfaces/all Modern routes, settings/text scale, one production process, no Debug bridge or coordinate clicks; public URI association restored |
| Production Release UIA (EN/Light) | PASS | `57-release-uia-en.log`: same unchanged UIA suite starts Classic, checks its modal Batch, switches Modern/all routes/focus, returns Classic; clean exit, public URI association restored |
| Notification settings/action | PASS native automation; OS appearance NOT QUALIFIED | `55-notifications.log`: test notification invoked in isolated profile. Visible toast/click behavior requires separate human verification |
| Exact-head push and PR CI | Post-commit evidence | Both lanes required. The final exact SHA, run URLs, terminal conclusions and completed checks are recorded after pushing this report commit in [the CI receipt](artifacts/dashboard-visual-first/ci-final.json) and the delivery message. Previous-head results are not current evidence |

Original captures, semantic trees and six comparison sheets: [VISUAL_COMPARISONS.md](VISUAL_COMPARISONS.md). Synthetic profiles are isolated under `%TEMP%/mtd-automation-smoke-*`; production user data and cookies are not used. Native PrintWindow captures are not desktop screenshots. Test helpers target only owned processes/windows.

The report commit changes documentation only. Local executable evidence applies to implementation `7c31b67`; GitHub CI must run the full final commit including these reports. The CI receipt is generated after terminal results, outside versioned sources, to avoid a self-referential commit/result cycle.

Local package (not published): unchanged version `1.3.0-candidate.1`, 143854910 bytes, ZIP SHA-256 `ab66161fe5bf222aa9daa4332f5254c8f783ad4d6f48e6318153cafca94ece5b`. The tested Release executable SHA-256 is `2e86cee1ba0ce592313fe46ab902a9553928995d71e1e0ddc4d19a2ba5f0b4bf`. Build-Release generated the self-contained win-x64 application and symbols package locally; this is not a new release/candidate publication.

## Known limitations and interpretation

- Visual fidelity is PARTIAL on all six pages; no automated success grants owner acceptance. Remaining differences are explicit in [VISUAL_GAP_AND_FIXES.md](VISUAL_GAP_AND_FIXES.md).
- One switch reported private bytes 312360960 before / 325943296 after. This is a short, non-GC-normalized observation, not leak freedom. A longer controlled switch run is separate.
- The 20-cycle/100-entry run reported 318038016 / 364228608 private bytes before/after, final handles 588. Values fluctuated during the run; no forced-GC normalization or long soak was performed. Functional identity checks passed; leak freedom is not claimed.
- Physical 5K/225% is verified independently of TextScale. That run used substantially more memory (1812496384 / 1785298944 private bytes before/after one switch, with large screenshot/render buffers); this is not a performance or leak qualification. Its actual native PNGs retain all 5120x2880 pixels.
- Debug shutdown can return `OutcomeUnknown` when its transport closes; the harness still requires process exit within 15 seconds, exit code 0 and bridge removal. Run 31 satisfied those conditions.
- No real YouTube/Twitch/media-playback, physical keyboard, screen-reader or visible Windows-toast acceptance is implied by FakeTool/UIA/semantic tests.
- Historical head 3a4164a had PR-green/push-red disparity (pacing and `.tmp` fixture cleanup); this round must report actual new results rather than suppressing or weakening those tests. Known updater MTD #4 remains separately tracked, not silently fixed here.
- `37-layout-transfer.log` failed because the QA invocation combined incompatible fixtures: Layout deliberately keeps admission paused, while Transfer requires a completed job. Persisted state was Queued/0%, with no final file. This was an orchestration error; neither timeout nor assertion was weakened. Correctly separated runs 52 (layout) and 53 (transfer) both passed; the original failure log remains available.
- `54-release-uia-en.log` failed at the Classic modal Batch assertion because the invocation started in Modern (whose Batch is an embedded route). The unchanged production UIA suite first expects Classic, then explicitly switches to Modern and back. This was a fixture-selection error, not evidence that Modern must open a Classic modal. Run 57 passed with the required Classic starting mode; assertions and timeouts were retained. The failure log remains available.
- System save-dialog UIA descendants were not qualified; actual export HWND behavior was. See [MFN_FINDINGS.md](MFN_FINDINGS.md).
