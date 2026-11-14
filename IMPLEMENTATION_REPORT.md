# Batch automation, existing-download organizer, locales and startup updates

Development iteration starting 2026-11-12, based on `66d4fd359aac0de228c96a586d07941a6882f5a5`.
Worktree: `ModernTubeDownloader-modern-dashboard`, branch `feature/modern-dashboard-ui`, Draft PR #3.
Version remains `1.3.0-candidate.1`; central ModernFormsNext pin remains
`7bf58d06d795d7298c86d9bf9471bbfb46852bab`. This is partial development validation,
not release qualification or permission to publish.

## Implemented

### Per-list output and Batch automation

New jobs can carry an explicit output directory. Existing/null values retain the
global fallback. A list resolves a safe destination before admission; editing an
admitted destination is prohibited. Existing parser, metadata checks, format selection,
queue persistence and `QueueProcessor` execute the work. There is no parallel scheduler.

The explicit Import & Download action persists workflow phases, source fingerprints
and deterministic task identities. Repeated actions, source re-import and restart
reuse the session rather than duplicating tasks. Progress shows import, analysis,
queue handoff, downloads and final state from actual canonical jobs. Pausing/cancelling
analysis does not delete already admitted jobs. A globally paused queue stays paused;
resuming it requires an explicit MessageBoxForm explaining that older jobs also resume.

Audio keeps its selected/original format; this change does not add MP3 transcoding.
See [Batch behavior and persistence](BATCH_IMPORT.md).

### Existing-download organizer

Batch and Other open one native dialog using the same services. It supports multiple
URL-list files or the current Batch session, source scan root, recursion, per-list
destination, required JSON, filters, bounded pages, detailed dry-run, candidate choice,
selected moves, pause/cancel, recent journals, recovery and verified undo.

Only verified URL/provider identity authorizes relocation. Raw adjacent JSON,
subtitles and thumbnails remain paired. Invalid or mismatched adjacent JSON blocks
relocation. When adjacent JSON is absent, verified
central metadata for a single canonical ID is copied; the central original is retained.
History and completed queue paths update atomically within their respective existing
stores, preserving IDs and unrelated fields. A journal bridges crashes between stores.
The updated references are History `FinalPath` and completed queue `FinalFile`;
raw JSON filepath strings and the original job output-directory policy remain unchanged.
Copy/publication verifies complete hashes before removing the original. Changed or
occupied destinations stop execution. Failed History load now prevents subsequent
empty-state saves from overwriting the unreadable original.

The immutable operation manifest, compact state header and per-file checkpoints avoid
rewriting a full 10,000-entry journal after each file. The UI retains forty rows and
one hundred operation summaries, prioritizing unfinished operations. Startup checks
all operation headers, including older pending work. See [safety and recovery](FILE_ORGANIZER_SAFETY.md).

### Localization

One registry supplies desktop setup, Settings and Web Remote. It includes PL/EN plus
DE, FR, ES, IT, PT-BR, NL, CS, SK, RU, HU, TR, JA, KO and ZH-CN. Every locale contains
1,387 keys with matching format placeholders. Native selection supports search,
translated/native names, vector region icons, persistent selection and legacy aliases.
The remote catalog exposes only public UI resources; authentication remains unchanged.

Resources were generated offline and corrected selectively. Key coverage is complete;
native-speaker review of every sentence is **NOT QUALIFIED**. Safety wording review
found and fixed mistranslated negations, JSON schema identifiers, required-metadata
instructions and update labels. See [coverage and provenance](LOCALIZATION_COVERAGE.md).

### Optional startup installation

The new preference defaults off and preserves legacy opt-outs, Skip/Later and on-exit
policy. Foreground preflight has an eight-second budget and a Continue action. Existing
queue/LIVE/Batch/recovery work, background startup or external activation defers it.
The same updater validates and stages stable packages. Workers remain deferred until
startup admission finishes. Launch failure continues ordinary startup without reviving
stopped services. A per-request commitment marker prevents installation if the parent
crashes before final authorization; the updater still verifies parent PID/start-time.
Results and attempted version persist to prevent an automatic installation loop.
See [startup policy](STARTUP_UPDATE_POLICY.md).

## Diagnoses and retained failures

Evidence root: `artifacts/batch-organizer-20261112/` (ignored local evidence; no real
owner downloads were scanned, moved or updated).

- Initial targeted run: **119/124 PASS, 5 FAIL**. A History-lock assertion expected
  IOException but Windows returned UnauthorizedAccessException; byte/path/journal
  invariants remain asserted. An audio fixture incorrectly assumed new MP3 conversion;
  it now checks the actual original M4A contract without removing destination checks.
- Three Organizer layout cases exposed width oscillation; row layout now guards
  re-entry and batches its transaction. A subsequent **61/65** run exposed test input
  dispatched outside the UI dispatcher, producing concurrent layout/disposal. Input
  now uses the same dispatcher as existing native-host tests. Bounds/timeouts unchanged.
- The focused follow-up passed **19/19**, including Organizer at 100/150/200%.
- The first native startup crashed with **0xC00000FD**. ProcDump first-chance evidence
  and `diagnostics/clrstack.txt` show repeated `UpdateSettingsPanel.LayoutForWidth`
  through SizeChanged. Intermediate/final heights alternated recursively. The panel
  now commits only its final measured height. Four regression cases cover both
  presentations at 100/200%. Startup/locale targeted run passed **62/62**, zero skipped.
- Initial requested 3440×2300 window was clamped by the current desktop to 3138×1289;
  that geometry run remains FAIL. Subsequent captures use an actual 1536×1024 HWND
  at 96 DPI. This is not evidence for physical 5K/225% DPI.
- First passing native Batch/Organizer/locale flow exposed tiny dynamic Organizer
  fonts and new Batch controls below the fold. Their original PNGs remain retained;
  the presentation was corrected before final visual capture.
- The first combined targeted run completed 317 cases before an asynchronous startup
  dialog test aborted its host. Its Shown event had been raised under xUnit's context,
  then closed a UI-owned surface from the pool. The fixture now dispatches Show just
  like native button input. The aborted run is **FAIL**, not a 317-test suite PASS.
- Final screenshot review found clipped localized Organizer buttons, a truncated
  destination heading and the unresolved Classic details key. Widths now use measured
  text; the Classic action uses the existing Common.Details resource. An earlier
  Classic capture failed because the capture script used Batch.Title instead of
  Other.Batch.Title; that original failed log is retained separately.

Original compile failures, test failures, crash dump, logs and preliminary images are
retained. No assertion weakening, timeout increase or CI retry was used.

The first push CI for `f3e556b` failed before test execution: the repository's
`-warnaserror` gate rejected two xUnit2031 analyzer diagnostics. Both assertions now
use `Assert.Single(collection, predicate)` instead of filtering with Where first;
their predicates and cardinality requirements are unchanged. The original failed
CI logs remain in `ci-push-f3e556b-failed.log` (and the separate PR run record).
The correction is a new commit, not a rerun of the failed build.

## Performance evidence

Real isolated files and distinct JSON metadata, asynchronous indexed planning:

| Entries | Files | Plan time | Managed-memory delta |
| ---: | ---: | ---: | ---: |
| 2,750 | 5,500 | 4,319.1 ms | 24,263,296 B |
| 10,000 | 20,000 | 16,546.3 ms | 40,081,864 B |

These are absolute measurements from `presentation-locales-organizer.trx`, not a
before/after improvement claim. This organizer did not exist in the baseline.
The native-host 10,000-URL fixture checks forty visible rows and bounded controls at
100/150/200%. No thumbnails are decoded for organizer rows. Full application memory,
bitmap counters, prolonged multi-screen navigation and cold-PC performance remain
part of final qualification.

## Final validation checkpoint

Development validation completed on 2026-11-14. Counts overlap and must not be added.

| Check | Result | Evidence under the local evidence root |
| --- | --- | --- |
| Batch, organizer, update admission/handoff, locale, experience, remote and History targeted integration | **342/342 PASS**, zero skipped, exit 0 | `integration-qualified.log`, TRX under `tests/` |
| Final Batch-label/layout and resource follow-up | **27/27 PASS**, zero skipped | `batch-final-labels.log`, `batch-final-labels.trx` |
| CI analyzer correction: Batch/Organizer with `-warnaserror` | **90/90 PASS**, zero skipped | `ci-analyzer-correction.log`, TRX under `tests/` |
| Web Remote JavaScript localization | **6/6 PASS** | `remote-locales-final.log` |
| Real native workflow: Modern PL/Dark/100, EN/Light/150, DE/Dark/200, JA/Dark/200; Classic PL/Dark/100 | **5/5 PASS** | `native-*-reviewed.log`, final PL label follow-ups, `native-manifest.json` |
| Public repository hygiene | **0 findings** | `public-hygiene-final.log` |

Native flows import three URL rows (two unique), execute two real FakeTool processes
through the canonical queue, verify destination media and JSON, repeat the action
without changing task IDs, move the selected recordings, verify History paths, undo,
and open/search the shared locale picker. PL also changes the persisted language to
English and back. Fixtures are synthetic and perform no public-site downloads.
The window is an actual 1536×1024 HWND at **96 DPI**. Application text scales
100/150/200% are not Windows monitor-DPI qualification. Actions use the Debug semantic
bridge; screenshots use native HWND PrintWindow. They do not qualify external UIA.

54 final original PNGs, capture metadata, semantic snapshots, hashes, a synthetic immutable
dry-run manifest and `gallery.html` are collected under the evidence root. Earlier
failed profiles/logs remain present. The binaries were compiled from the changes
subsequently committed; no claim of a release-package test is made.

Automatic push and PR CI must be checked against the resulting exact commit. The
post-push record is `CI_RESULTS.md` / `ci-runs.json` in the evidence root, and the
current Draft PR #3 description links the runs. Local results above are independent
of that remote outcome; no CI rerun is used to replace a failure.

## Visual review against references

**VISUALLY REVIEWED:** Batch retains the reference's dark blue surfaces, electric-blue
primary actions, dashed import area, parsing counters, dense table and lower summary.
The new per-list destination and real phase strip sit above the table so they are
reachable before queue admission. Organizer uses the same surface, border, accent,
typography and native-dialog language, with a separate dry-run inspector and a fixed
action/progress footer. It has no dedicated supplied reference image.

EN Light 150 and DE/JA 200 preserve readable action labels; large text switches to
stacked controls and vertical scrolling. The inspector exposes complete file paths.
Classic retains its existing smaller typography and shell rather than adopting the
Modern geometry. Final captures include both presentations.

Visual parity is **PARTIAL**, not pixel-identical: real branding, action set, data and
synthetic media differ; the destination/workflow strip is additional functionality.
Full six-screen parity and final large-text refinement remain later release work.

## Remaining limits / NOT QUALIFIED

- Completed LIVE parts, unfinished recordings and unproved filename/title matches
  remain excluded. Shared-basename multiformat companions require manual filesystem review.
- Interrupted partial staging evidence is retained; no automatic cleanup is provided.
- Physical disk-full, removable-device disconnect, genuine cross-volume behavior and
  power loss are unqualified. Fault injection exercises the production copy/recovery
  path on isolated local files; it does not stand in for those physical tests.
- Revalidation rejects changed/reparse paths, but no handle-relative sandbox is claimed
  against hostile concurrent replacement of path components.
- Organizer currently accepts local drive paths only; UNC/network and device paths
  are rejected explicitly rather than treated as qualified relocation targets.
- A closed Batch view may leave a terminal phase conservatively stale and defer a
  startup update until the session is refreshed. It cannot silently start other jobs.
- Original intermittent external Windows UIA/MSAA/NavDownloads cases remain DEFERRED.
  Semantic bridge actions plus native pixels are not external UIA qualification.
- Full Debug/Release/JavaScript/UIA/DPI/package matrices, keyboard/screen-reader coverage,
  clean-PC update and rollback, physical 5K memory and language review are deferred.

Classic, the shared runtime and main checkout are preserved. Draft PR #3 stays unmerged;
no version bump, tag, NuGet package or release is authorized by this report.
