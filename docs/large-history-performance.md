# Large history / startup memory repair

> Historical P0 snapshot. The follow-up integration and qualification are recorded in [stability-completion.md](stability-completion.md). Original failures below are retained.

Local implementation, 2026-11-07. The stable correction is based on v1.2.1 (`4ab4957faa57d04fbcf7b41dda8279a7e2a630ff`) in branch `fix/large-history-startup-memory`. The adapted dashboard correction is based on `8e632ee3aa4992d490758d1e6751112464f922e5` in `feature/modern-dashboard-ui` (Draft PR #3). Main checkout and pinned ModernFormsNext sources are unchanged. No version, tag, merge, remote publication or retention policy changed.

## Implementation

History builds no hidden cards at startup. A cancellable, debounced projection filters the entire canonical snapshot off the UI thread, publishes explicitly on the UI thread, and reuses at most 40 row controls. Navigation retains the page and rows. The dashboard retains its filters, selection, inspector, date sorting and full-model export; Classic shares the bounded rendering path.

ThumbnailCacheService limits decoded cache storage to 32 MiB and parallel decoding to four workers; displayed images have a 512 px maximum dimension. GetAsync now returns a **caller-owned copy**: detach it from the control before disposal. ThumbnailImageBinding owns displayed/pending copies and rejects obsolete callbacks. Disk bytes/dimensions are validated before publishing a new cache file. The dashboard's explicit thumbnail export preserves source resolution within its existing limits. Existing disk cache is retained.

AppWindowLifetime gives application forms explicit ownership of their public control trees, because the pinned WindowBase lifecycle does not dispose that tree. Closing forms now releases image bindings, controls, subscriptions and timers. Services and storage still load in the original safe order; no schema or JSON rewrite was needed.

## Reproduction and evidence

Build with `scripts/Build-Release.ps1` using the clean, centrally pinned ModernFormsNext checkout. Build `qa/PerformanceProbe/PerformanceProbe.csproj` in Release, then generate a **new** TEMP directory named `mtd-automation-smoke-*`: arguments are root, history count, queue count, unique image count, FakeTool build directory. Example counts: `2750 2617 2750`; the queue contains 2526 completed plus 91 queued jobs. All URLs and thumbnails are synthetic; queue processing remains paused.

`scripts/Test-Performance.ps1` copies this fixture to a fresh TEMP profile, refuses an existing app instance, launches production Release, captures only its own HWND, runs search/paging/navigation, compares persistence hashes and checks final bitmap/request counters. Use `-RequirePaging -RequireBitmapRelease`; feature additionally supports `-Modern -Groups -ShellCycles 3`. Existing evidence is never overwritten. The harness caps process memory at 6 GiB and total runtime at 180 s, with per-lookup limits unchanged.

Diagnostics are opt-in via `MTD_PERFORMANCE_DIAGNOSTICS=1`. They record bounded numeric JSONL (maximum 20000 records), including stage timings, UI heartbeat, allocations, process/GC/bitmap counters. `nativeBitmapBytes` covers cache and thumbnail copies, not every Skia renderer surface or temporary codec allocation. Process working set/private bytes are reported separately.

Detailed local evidence lives in `artifacts/performance-large-history/` in the stable worktree: ROOT_CAUSES.md, BENCHMARK_BEFORE_AFTER.md + CSV/JSON, HISTORY_AND_STARTUP_QA.md, PLAYLIST_GROUPS_FOLLOWUP.md, INTEGRATION_PLAN.md, original PNG/TRX/logs, per-run binary hashes and source-manifest.json. Large synthetic data stays outside Git. No private profile was used. The exact reported 7.5 GB was not reproduced: the 10000-image baseline hit the predeclared 6 GiB safety cap before showing a window.

## Validation boundaries

Final local full validation: stable Debug 789/789 and Release 789/789; dashboard Debug 980/980 and Release 980/980; JavaScript 92/92 in each; package hygiene 810/812 entries, no skipped tests. See original logs 28-stable-full.log / 34-feature-final-full.log. Earlier failures remain preserved, including invalid-image disk publication and the old synchronous history assertion, corrected without dropping content/order checks.

For the same H2750/Q2617/2750-image fixture, stable process-to-HWND fell from 38.675 s to 1.629 s and peak private bytes from 3051 MiB to 315 MiB. First History readiness after the correction was 0.918 s. The 10000-image correction showed HWND in 1.876 s and used 380 MiB peak private bytes while visiting all 250 pages; the baseline hit the 6 GiB safety cap without a window. These are single-machine measurements, not a promise for another computer.

Physical 5K / Windows225% remains more expensive: stable reached 1263 MiB peak private bytes and Modern 1608 MiB, despite bounded thumbnail memory. The remaining native allocation sources were not attributed; renderer surfaces are a hypothesis. No comparable pre-change 5K baseline was collected. The 315–380 MiB figures must not be generalized to every display configuration.

The final Full native Modern matrix has 7 PASS / 4 FAIL. A separately predeclared ShellOnly EN200% pair completed all six transitions after the presentation patch, whereas the prior binary timed out at 180 s. Maximum heartbeat gaps were 56.4 s before and 6.4 s after; the latter still represents a visible stall. This narrower result does not override Full failures or establish release readiness.

Native final results and actual screenshots are authoritative in the evidence report. Fresh-process startup is not an OS-cold boot; filesystem cache and unrelated user workloads were not reset. An independent Windows testhost overlapped the beginning of the later presentation matrix. NavDownloads stalled for 21.2 s at 200% in an earlier feature run; captured stacks identified synchronous UIA/layout during presentation changes. The adapted patch prepares the same page tree off-window and batches row layout, but the subsequent 200% combined scenario also exceeded the unchanged 180 s limit. This issue is not certified fixed. First playlist expansion remains a separate performance limitation. No new push/PR CI was run for this uncommitted patch; CI from the baseline SHA does not qualify these changes. Publication or installation for another user requires separate owner authorization.
