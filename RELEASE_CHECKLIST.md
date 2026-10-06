# ModernTubeDownloader 1.0.0 release checklist

## Final publication gates — 2026-10-06

- [x] Owner selected MIT; standard LICENSE contains Copyright © 2026 ProGraMajster.
- [x] Owner explicitly approved commit, push master, PUBLIC visibility, annotated `v1.0.0` tag and GitHub Release. No Chrome Web Store / NuGet publication.
- [x] Latest fetched MFN `origin/master`: `f521f9dfcfe601bf9b6199b88132cccb2380d1bf`; clean detached development checkout and CI/release pins agree.
- [x] Fresh Debug and Release: 375/375 each, zero skips/warnings/errors. Extension 16/16; Web Remote JS 25/25; localization parity included.
- [x] Bounded current-tree and complete reachable-history secret/privacy scan; findings are only exact reviewed synthetic security-test fixtures. Only approved app/extension icons are binary source assets.
- [x] README / 1.0.0 changelog / release notes / third-party notices refreshed; older evidence retained as historical, not reopened release blockers.
- [x] SponsorBlock Remove Experimental/default OFF/warning, FromStart Experimental; post-1.0 matrices remain deferred.
- [x] Focused native ordinary/Details/Settings/quality EN/Dark, active LIVE PL/Light and 12-entry playlist EN/Dark smoke on the new pin (10 available entries queued).
- [x] Final self-contained packaging / root MIT LICENSE / dependency licenses (including full Apache terms) / forbidden files / checksum / extracted-EXE public VOD smoke. Main ZIP 374 entries; no duplicate or forbidden entries; separate symbols ZIP.
- [ ] Final clean source commit pushed; exact commit CI PASS before PUBLIC/tag.
- [ ] PUBLIC repository metadata/README/LICENSE/source checked; annotated tag points to the final commit.
- [ ] Tag-triggered Release PASS; main ZIP, checksum and symbols verified; published ZIP downloaded, hash compared, extracted and started/closed.

The last three external steps are completed only after the final commit exists; GitHub Actions/Release and the final handoff record their actual outcome. Broad soak/device/DPI/browser/extractor matrices and authenticated cookies remain **POST-1.0 / optional qualification**, not publication blockers.

## Historical scoped stabilization gates — 2026-10-05 priority change

This section supersedes the earlier exhaustive gate policy. Evidence and exact raw failures remain in `STABILIZATION_REPORT.md`. An unchecked optional/manual matrix is not automatically a release blocker. **NOT READY for public release: application LICENSE missing; stable core technically ready within scoped evidence.** No public-release approval is implied.

- [x] PASS — requested verified MFN pin remains `bcf2bcb1726cbb697d2e56600188c4ef2c91ebe1` consistently; dependency source clean. Later origin/master is `f521f9df...` (Android changes); no unrequested migration or claim that bcf is still latest fetched HEAD.
- [x] PASS — Debug/Release zero warnings/errors; 375/375 .NET tests each, zero skips; extension 16/16; remote time editor 25/25 JavaScript tests; restore/publish pass.
- [x] PASS — known opaque-codec selection and explicit-container contradiction reproduced before fix; regression tests, local input/post-remux probe gates pass.
- [x] PASS (scoped) — native normal/Details, 220-entry playlist (218 queued), 20 Settings/mouse-stress cycles, earlier PL/Light and final EN/Dark 20 quality-popup cycles with owned-HWND capture.
- [x] PASS (P0 scoped) — minimal Light/Dark and actual MTD PL/Light + EN/Dark each pass five × 100 native/canonical dropdown cycles. Exact CLI PL/Light focused/unfocused 100 each PASS. EN/Dark CLI focused 97/100, unfocused 100/100: three raw FAILs trace legitimate deactivation/outside MouseDown closing actual popup, not stale peer. Controlled second-owned-window witness PASS. Historical pass-15 cause is still unproven, POST-1.0 diagnostic follow-up; no workaround or speculative MFN issue.
- [x] PASS — ordinary final EN/Dark native automation: analysis, quality, queue, Settings, all Details tabs and clean shutdown/discovery removal.
- [ ] FAIL / RETAINED — optional final PL/Light two-cycle popup diagnostic stops at cycle 2; native trace records external foreground activation, loss of app active/focus and actual hidden popup with correct Collapsed peer. Not counted as PASS or rerun to chase green; environmental diagnostic evidence is in STABILIZATION_REPORT.md.
- [x] PASS (scoped) — shared video-card action bindings/compact PL/EN label and six Light/Dark/System style/contrast cases; actual final EN/Dark card inspected.
- [x] PASS (scoped) — phone feedback after Hours/Minutes/Seconds change; Chrome portrait/landscape/desktop range layout, real 2–10 s queue/download/History/probe/decode and user playback confirmation.
- [ ] PARTIAL / POST-1.0 QA — complete phone/LAN token-ON, QR, firewall prompt and LIVE controls; existing user phone confirmation retained, no automatic firewall/WAN change.
- [ ] OPTIONAL FEATURE QUALIFICATION — subtitle/LIVE human sync unanswered. SponsorBlock Remove human playback FAIL (freeze / apparent out-of-sequence frames; supplied recording): stays Experimental, default OFF with warning under user §11 A, not stable-qualified; Mark / explicit MKV remains supported. Do not transcode to conceal upstream defects.
- [ ] POST-1.0 QA — complete per-source/container matrix; Archive OGV-range invalid timestamp output is safely rejected, not successful qualification.
- [x] PASS (scoped) — Archive full Auto/MKV technical files/JSON/History; two-host production-service VOD .part reuse; final Release LIVE reconnect/Stop-save with three parallel VODs.
- [ ] PARTIAL / DEFERRED POST-1.0 — multi-hour soak: 83 minutes continuous observation before system suspension; no monotonic resource growth or crash observed. Full multi-hour uninterrupted soak deferred to post-1.0 QA. Not no-leak proof and not an active blocker; no repeat run.
- [x] PASS (P0 scoped) — actual self-contained EXE from ZIP closes normally/reopens same executable, Interrupted persistence, stable session/workspace, actual .part 2,151,096 → 2,223,664 bytes without truncation; final 34,076,243-byte MP4, ffprobe/10 s decode, exactly-one matching History.
- [x] PASS (explicit recovery only) — controlled owned VOD child kill retains 2,372,184 bytes; explicit Retry refreshes metadata/appends to 2,541,008 first-observed bytes and completes valid media/History. Normal auto-retry is NOT SUPPORTED for killed-child exit -1 classified Unknown; POST-1.0 hardening, not false automatic PASS.
- [x] PASS (scoped native layout) — Downloads/LIVE/Settings/Sources inspected at actual 100% PL/Light, 225% EN/Dark and 100% EN/System (resolved Dark). Existing animation completion observed, no application changes.
- [ ] NOT EXECUTED / POST-1.0 — 125% Dark / 150% System physical configurations unavailable; no OS display changes or fake DPI evidence. Exhaustive UI/browser/resource matrix deferred.
- [x] PASS (scoped) — final Build-Release.ps1, self-contained publish, ZIP contents/checksum; no bundled tools/runtime data/PDBs/test harness.
- [x] PASS (scoped package runtime) — native close/reopen/final-media/History gate above completed, not inferred from publish success. Prior first/second provisioning and FromNow evidence retained without repeat.
- [ ] NOT EXECUTED — optional authorized cookies.txt test requires user credentials; default OFF, synthetic safe errors/redaction PASS. Not a basic 1.0 blocker.
- [ ] USER DECISION REQUIRED — app license and final publication approval. No tag/release/publication.

### Release blocker triage

| Issue | Severity | User impact | Reproducible | Release blocker? | Reason |
| --- | --- | --- | --- | --- | --- |
| Application LICENSE missing | P0 / legal | Distribution terms unspecified | Yes | YES — USER DECISION REQUIRED | Concrete public-release prerequisite. |
| Dropdown interrupted by native input/activation | P2 / diagnostic | No proven incorrect control/peer behavior | Traced + owned-window witness | NO — POST-1.0 QA | Strict clean native variants pass; keep raw interrupted FAILs and old uncertainty. |
| Native normal-close VOD resume | P0 gate | Stable partial reuse | PASS | NO — CLOSED | Real ZIP EXE, appended bytes, final media, one History entry. |
| Unknown killed-child exit does not auto-retry | P2 | Explicit Retry required | Yes | NO — POST-1.0 QA | Safe Failed/retained data and explicit recovery PASS; not universal auto-recovery guarantee. |
| SponsorBlock Remove edit-point defects | P1 / Experimental | Visible freeze / out-of-sequence playback | Human FAIL + recording | NO for basic stable 1.0 — OPTIONAL FEATURE QUALIFICATION | User permits Experimental disposition with existing warning/default OFF; Mark/MKV preferred. |
| Interrupted soak/full device/browser/source matrix | P2 / qualification | Unqualified combinations, no confirmed defect | Partial/available subset | NO — POST-1.0 QA | Explicitly deferred; not fake PASS/no-leak proof. |
| Cookies/FromStart/upcoming/natural end | P2 / optional | Credential/timing-dependent behavior | Synthetic PASS, real incomplete | NO — OPTIONAL FEATURE QUALIFICATION | Cookies default OFF; FromStart Experimental; standard FromNow verified. |
| Final publication | Approval gate | User-controlled distribution | Not authorized | USER DECISION REQUIRED | No staging/commit/push/tag/release/publication. |

### Historical previous-policy gate disposition (0–47)

These are the retained **pre-priority-change** broad qualification statuses, not the current release-blocker list. The active P0 results/triage above supersede dropdown, native resume and disposition. Unfinished broad qualification remains unfinished, but user §18 places it post-1.0 rather than blocking by default. Historical “NOT READY” below must not be interpreted as requiring another soak or every physical device before 1.0.

| Gate | Area | Status | Evidence / remaining work |
| --- | --- | --- | --- |
| 0 | Read current documentation | PASS | Required documents read; active evidence separated from historical snapshots. |
| 1 | Latest ModernFormsNext | PASS | Audited/pinned bcf2bcb...; Oct 5 re-fetch unchanged; clean source checkout. |
| 2 | Feature freeze | PASS | Bug fixes, diagnostics, compatibility, tests and documentation only. |
| 3 | Functional inventory | PASS | Implementation, automated/real/visual evidence, limitations and feature status table recorded. |
| 4 | Audit areas A–X | PARTIAL | All inventoried; complete real/visual qualification remains open where listed. |
| 5 | Real multi-platform | PARTIAL | Sintel range and Archive full Auto/MKV technical PASS; Twitch LIVE PASS; Vimeo authentication, TED extraction and W3C network limitations; remaining source matrix unqualified. |
| 6 | Source-neutral FormatSelector | PASS | Reproduced selector/container defects; conservative opaque fallback and actual input/post-remux probes; real success and rejection retests. |
| 7 | Format/container/protocol matrix | PARTIAL | Automated compatibility plus real opaque combined/range/LIVE cases; complete HLS/DASH/audio/separate/container matrix pending. |
| 8 | Real VOD restart/child interruption | PARTIAL | Two production-service hosts prove .part reuse; native GUI retains partial, but continuation and controlled VOD child-kill not completed. |
| 9 | Real LIVE matrix | PARTIAL | Current FromNow/Stop-save/reconnect/3-VOD parallel PASS; older dated Partial/restart evidence retained; full fresh matrix/playback pending. |
| 10 | Imminent upcoming auto-start | NOT EXECUTED | No suitably bounded real event qualified; synthetic monitoring is not equivalent. |
| 11 | Natural LIVE end | NOT EXECUTED | No short naturally ending real source qualified. |
| 12 | Representative human playback | PARTIAL | User confirms actual Sintel 2–10 s A/V; full VOD/subtitle/Mark/Remove/LIVE/recovered-part playback pending. |
| 13 | SponsorBlock | PARTIAL | Mark explicit MKV and experimental Remove preserved; historical backward PTS retained; human edit-point review pending. |
| 14 | Subtitles | PARTIAL | Automated and historical real embed evidence; complete fresh language/format/container and human sync matrix pending. |
| 15 | User cookies.txt authentication | NOT EXECUTED | No user-supplied file; no browser DB/DPAPI access or credential bypass. |
| 16 | Playlist stress | PARTIAL | Native 220-entry preview/bulk add/order/persistence PASS (218 queued); full size/state/real-source matrix pending. |
| 17 | Queue stress | PARTIAL | 50 FakeTool completions at concurrency 3, native bulk queue and regression tests; full mixed-state/action matrix pending. |
| 18 | Multi-hour native UI soak | PARTIAL | Backend run NOT QUALIFIED: 91 samples/~83 min then overnight sleep; no multi-hour native UI run. |
| 19 | Resource leaks | PARTIAL | Bounded backend/native metrics; no sampled monotonic growth, not a no-leak guarantee; bitmap/event/view/full soak audit incomplete. |
| 20 | Real browser extension | PARTIAL | Current Chrome watch/SPA/playlist/replay/Light/Dark and running-app forwarding/Add PASS; Shorts/popup/cold launch/prompt/restart matrix pending. |
| 21 | Physical phone/LAN | PARTIAL | User confirms LAN/add and improved time editor; full token-ON/QR/LIVE/reconnect/long-session actions pending. |
| 22 | Firewall | PARTIAL | No silent rule or WAN changes; physical prompt/private-network disposition not confirmed. |
| 23 | Remote security | PARTIAL | Passing token/Host/Origin/body/rate/input/allowlist HTTP tests; complete physical/deployment review not implied. |
| 24 | PL/EN/theme/DPI/window matrix | PARTIAL | Native PL/Light and EN/Dark subsets; full System/125%/150% and all views/sizes pending. |
| 25 | Input/state contrast | PARTIAL | Fixed tested card contrast; basic native controls inspected; full hover/focus/disabled/validation/menu matrix pending. |
| 26 | Quality dropdown | FAIL | Final PL/Light repeat fails at pass 15 after Expand; earlier PASS and older ActionUnsupported retained. Minimal native root-cause repro still required. |
| 27 | History | PARTIAL | Actual current ordinary/multipart LIVE entries and persistence PASS; complete shell/missing/deleted/subtitle/context matrix pending. |
| 28 | Supported sources hardening | PARTIAL | Catalog/cache/update/custom/missing/broken/Unicode tests and dated native checks; full current performance/resource measurements pending. |
| 29 | Metadata-only Check URL | PARTIAL | Automated no-side-effect matrix and selected real metadata cases; complete fresh source/auth/upcoming matrix pending. |
| 30 | Managed tools | PARTIAL | Actual empty published profile installs yt-dlp/FFmpeg/Deno; unchanged second-start binaries; synthetic rollback/error tests; full real offline/update matrix pending. |
| 31 | Logging | PARTIAL | Sanitizer tests and 15 QA log files with zero specified secret-pattern matches; not an exhaustive audit. |
| 32 | Controlled crash-report seam | PASS | Report creation/diagnostics/redaction tests included in full suite; no forced ordinary-profile crash. |
| 33 | Filesystem/low disk | PARTIAL | Automated collision/Unicode/recovery coverage; full real permissions/low-space/temp-path matrix pending. |
| 34 | Network failures | PARTIAL | Bounded retry/classification/cancel tests and observed real source failures; full controlled transport/disconnect matrix pending. |
| 35 | Combined 3 VOD + 2 LIVE stress | PARTIAL | Synthetic five-child ownership PASS; real 3 VOD + 1 LIVE PASS; full real combined load/browser/UI/settings not qualified. |
| 36 | Shutdown stress | PARTIAL | Scheduler/process tests and normal/current LIVE shutdown checks; complete all-stage native shutdown matrix pending. |
| 37 | Startup recovery | PARTIAL | Corruption/migration/state tests and actual service-host resume; full native corrupt-state/recovery matrix pending. |
| 38 | Release package | PARTIAL | Publish/ZIP hygiene and extracted EXE start PASS; complete clean native runtime/resume qualification interrupted. |
| 39 | Application license | USER DECISION REQUIRED | No license selected; user must decide before public release. |
| 40 | Bug policy | PARTIAL | Fixed defects reproduced/tested/retested; popup cause remains unresolved, not hidden by sleeps/catches/layout resets. |
| 41 | Framework bug policy | PARTIAL | Latest upstream audited, framework untouched; popup minimal repro/ownership not yet established, so no speculative issue or workaround. |
| 42 | Performance | PARTIAL | Bounded metrics/native 220-entry behavior available; complete latency/baseline/regression measurement pending. |
| 43 | Honest documentation | PASS | Active results/counts/pin/failures synchronized; earlier dated snapshots remain explicitly historical. |
| 44 | Point-by-point checklist | PASS | Every requested gate has an explicit status and evidence boundary here. |
| 45 | Final suite/package smoke | PARTIAL | Restore, Debug/Release 375 each, 41 JS tests, publish/ZIP PASS; latest popup FAIL and clean-package runtime PARTIAL prevent full gate PASS. |
| 46 | Stabilization report | PASS | STABILIZATION_REPORT.md includes fixes, results, limitations, decisions and NOT READY rationale. |
| 47 | Scope / no publication | PASS | No new large features, staging/commit/push/tag/release/NuGet/extension publication. |

## Runtime supported sources gates — 2026-10-03 (local, not release sign-off)

- [x] Catalog from active yt-dlp, exact CLI syntax audited; no giant static site list.
- [x] Version/path/hash cache, tool-update invalidation, acquired-version identity, pending-update activation and failed-new-executable regression.
- [x] Active custom executable, missing executable, exact descriptions, duplicates, Unicode, generic/broken handling, 10k data list and 5k bounded UI list tests.
- [x] Metadata-only VOD/playlist/LIVE/upcoming/replay/generic/private/auth/invalid/unsupported checks have no queue/LIVE side effects.
- [x] Native PL/Light + EN/Dark catalog, search/pagination/wheel, Settings link and minimum dialog resize; actual source/type and Details / Source are semantic, not coordinate-only tests.
- [x] Real YouTube VOD/playlist/LIVE/replay, Twitch LIVE/completed VOD and Internet Archive metadata checks. Vimeo's login-required response is correctly presented; it is not a successful authenticated-media test.
- [x] Source-specific QA stays per feature; one new-platform metadata result does not grant Verified status. Extension and Web Remote allowlist remain YouTube-only.
- [x] Final Debug/Release 349/349 each, extension 16/16, Web Remote 7/7, localization parity, own-HWND Details Source/compact card, native LIVE/playlist and 20 Settings scrolling toggles.
- [x] Source dialog cache across two opens and explicit navigation without enqueue/recording; desktop non-YouTube replay → Downloads does not use the external-protocol allowlist.
- [ ] Archive.org tested sample download: upstream returns formats without codec IDs; current MTD quality selection is unavailable. Metadata recognition and the localized limitation are verified, not download support.
- [ ] Real authenticated Vimeo, arbitrary-source download/recording guarantees, real upcoming/FromStart and full DPI/System/Chrome/phone matrix (not implied by this round).

Current validation and final local package evidence are in `SUPPORTED_SOURCES.md`. Earlier counts/artifact hashes below apply to their explicitly dated historical snapshots.

## Final manual QA — 2026-10-03 (partial; not release sign-off)

### Current independent LIVE subsystem gates — 2026-10-03

- [x] Latest MFN `origin/master` fetched and all development/CI/release pins agree on `6d341963dfa0468c3636ea251e107ee34d0d1272`; dependency checkout clean; final app regression recorded in VALIDATION_REPORT.
- [x] VOD and LIVE have independent owners, stores, cancellation and 1–3 limits; no LIVE scheduling in QueueProcessor, no new LIVE queue records.
- [x] Mandatory 5 VOD + 3 LIVE integration test at limits 3/2: exact 3 + 2 active, third LIVE waits, independent slot releases/limit changes. Separate test observed five distinct child PIDs.
- [x] VOD pause allows active capture and upcoming → recording; stopping either scheduler leaves the other running.
- [x] Synthetic FromNow/FromStart, natural end, Stop and save, retry/Partial, cancellation policies, migration/restart, changed upcoming date, single monitor, private/unavailable and transient probing.
- [x] Real self-contained Release: 3 short public Sintel VOD sections completed while WildLifeCam LIVE recorded; four capture child processes observed concurrently.
- [x] Real FromNow / Stop and save / History / ffprobe A/V / two-second decode in Release and Debug native semantic UI.
- [x] Real controlled isolated yt-dlp termination: old data recovered as Part 1 and fresh capture saved Part 2; exhausted retries preserved a playable Partial.
- [x] Real self-contained Release graceful close/restart: manual and opt-in automatic resume both retain Part 1 and record Part 2. No isolated capture descendant remains after shutdown.
- [x] Separate `/api/live` safe DTO/actions, no LIVE fields in `/api/queue`, authentication/origin/rate-limit tests unchanged and passing.
- [x] Native semantic LIVE PL/Light and EN/Dark, Details, card ScrollIntoView and actual app-HWND inspection at desktop/minimum window sizes.
- [ ] Real upcoming classification and imminent upcoming → recording auto-start (synthetic tests are not equivalent).
- [ ] Real experimental FromStart, comparison with the current edge, real natural source end.
- [ ] Human audio/video playback and sync review, full DPI and System-theme visual matrix.
- [ ] Full fresh Chrome/Edge + phone/LAN verification after refactor; automated extension/API tests are not substitutes.
- [ ] License decision and final public-release approval.

Detailed results, evidence boundaries and artifact information: `VALIDATION_REPORT.md`. No commit, push, tag or publication was made in this round.

### Historical LIVE qualification before independent ownership refactor

- [x] Intermediate ModernFormsNext pin `1de8a6be35192614b690b6479143da211451a8a1` requalified before master advanced again; final active pin and qualification are in the current section above. Earlier dated evidence below applies to older code.
- [x] Synthetic FromNow/FromStart, Stop and save (including reconnect backoff), natural end, two-part retry, Partial, preservation OFF keeping only verified MKV parts, VOD `.part` continuation, upcoming transition/restart and native semantic LIVE UI smokes pass.
- [x] Active LIVE FromNow bounded capture, Stop and save, ffprobe audio/video, two-second decode and History passed on a real Twitch stream in Debug and self-contained Release. This is not human playback or an A/V sync review.
- [ ] Human playback and audio/video alignment review of the saved LIVE MKV.
- [x] Real controlled LIVE interruption/reconnect: killed only the isolated yt-dlp capture child, recovered Part 1, reanalyzed metadata, automatically retried after two seconds, and saved Part 2. Both parts passed ffprobe audio/video and two-second decode; no FFmpeg process from that profile remained.
- [x] Real exhausted-retry Partial: controlled yt-dlp exit with retry disabled preserved a verified 9.633-second, 9,929,232-byte MKV Part 1 with audio/video, History and two-second decode on the final ModernFormsNext pin.
- [ ] Raw-only unplayable workspace and its recovery message on a real source (synthetic coverage exists).
- [x] Self-contained Release close/restart during real LIVE and manual resume: old Part 1 and new Part 2 were saved separately, listed in History, probed with audio/video and decoded for two seconds each.
- [x] Self-contained Release LIVE automatic resume after a graceful close: old Part 1 was recovered, new Part 2 recorded, both verified and listed in History.
- [ ] Real VOD close/restart continuation.
- [ ] Upcoming real wait/auto-start; a synthetic transition does not close this gate.
- [ ] Experimental FromStart real smoke, and comparison with current live edge.
- [x] Self-contained Release active LIVE and Stop and save smoke passed in an isolated profile; the final published app saved a 15.733-second, 16,039,446-byte MKV part and one History entry.
- [ ] Self-contained Release PL/EN Light/Dark visual/DPI inspection.

No tag, GitHub Release, package or extension publication is authorized by these checks.

- [x] Rebuilt and re-inspected the local self-contained `win-x64` release candidate after the upcoming-live message fix and Chrome QA: restore, Debug/Release (0 warnings/errors), 277/277 Release tests, publish, 352-entry ZIP hygiene and checksum passed. Final ZIP SHA-256: `3238e504f4c34c99f7fe00f80f9a3785040db81b0986754f928cbe696ab42c7e`.
- [x] Requalified the 2026-10-03 ModernFormsNext `origin/master` pin (`4b0191d1c0de33e31e46c9f7a377dab833ae3d20`, 29 commits after the prior pin): restore, Debug/Release builds, 293 tests in both configurations, native Settings/playlist/queue-history/LIVE semantic smokes and a real self-contained Release LIVE smoke passed. This does not close visual/DPI or real Chrome gates. Earlier QA evidence below applies to the previous pin.
- [x] Ran browser-extension automated tests after the playlist-count fix: 14/14 passed. This does not close every real Chrome manual gate.
- [x] Real Chrome with the already installed unpacked extension: ordinary watch page showed one compact action and modal; quality 720p persisted after Esc/reopen; Shorts showed its button and modal; explicit NASA playlist showed button, modal and the correct playlist URL; SPA watch-to-watch navigation retained one button and updated the modal title/URL. Browser popup and Chrome restart were not tested.
- [x] Real Chrome `/live/<id>` routing: active NASA stream and completed replay both showed the LIVE / REPLAY modal and `Open in app` sent their `/live/` URLs to the existing app. The replay analyzed 104 formats; the active stream had already been verified as non-queueable in native semantic UI. No active stream was downloaded.
- [x] Real Chrome protocol delivery: `Open in app` started the closed Debug app with public *Sintel* in analyze-only mode; later replay/LIVE requests and a public *Sintel* Add-to-queue reached the same running process. The queue was paused before Add: it rose from 23 completed items to 24 with one Queued item, then the test item was removed and the original count/unpaused state restored. No full video was downloaded. The Windows/Chrome external-protocol prompt itself was not visible, and closed-app **Add to queue** remains untested.
- [x] Real Chrome playlist-count regression reproduced and retested after the user manually refreshed the unpacked extension: YouTube showed `19 filmów` but the old modal omitted it; the current metadata-view-model fallback now shows `19 filmów`. A new DOM-variant regression test passes.
- [x] Real official NASA active live (`M3HKLzjvKPc`) analyzed through the Debug app's semantic Automation bridge: `is_live`, Polish active-stream warning, `AddToQueueButton` unavailable, queue count 0; no media download was attempted. The separate real Chrome `/live` Open-in-app routing result is recorded above; Chrome Add-to-queue rejection was not attempted.
- [x] Real official NASA upcoming stream (`RU6gEobXVHA`) reproduced a generic-message regression. Direct yt-dlp confirmed exit 1 with `This live event will begin in 28 hours` and no JSON; classifier now maps that stderr to `Upcoming`. Targeted 11/11 classification cases passed, and the Debug app retest showed `Ta transmisja jeszcze się nie rozpoczęła.`, queue 0. No media download was attempted.
- [x] Real official NASA completed replay `46uxUxGpjtY` analyzed in the app via `/live/<id>`: 104 formats, active quality selector and Add to queue, queue 0 because no new download was requested. The previous 2026-09-27 full replay download remains the file-level evidence; the real Chrome `/live` Open-in-app route also passed above.
- [x] Re-probed the previously downloaded lawful *Sintel* MP4/MKV/WebM outputs with managed ffprobe: MP4 H.264/AAC/mov_text-eng, MKV VP9/AAC/subrip-eng, WebM VP9/Opus/webvtt-eng. Existing app FFmpeg commands use `-c copy` for merge and `-c:v copy -c:a copy` for subtitle embedding. This confirms the prior real-media smoke without another download.
- [x] Current publish-directory Release runtime: loopback Web Remote added public *Sintel* range 00:00:30–00:00:38 at 360p/MP4, resumed the queue, showed Completed, and wrote a 353,556-byte H.264/AAC MP4 of 8.083 s plus 773,439-byte parseable full JSON sidecar. FFmpeg log confirms `-c copy`, history has one completed entry and file CreationTime is the 2010-09-30 publication timestamp. This is a real download, not a fake-tool smoke.
- [x] Current Release local Web Remote sanity: isolated HTTP listener is only `127.0.0.1:18766`; auth-OFF status 200, foreign Host 403, foreign Origin 403, one oversized request 413. This does not replace the phone/LAN/firewall or auth-ON physical tests.
- [x] Current Release auth-ON loopback retest: `/api/auth-mode` reported `authenticationRequired=true`, unauthenticated `/api/status` and `/api/queue` returned 401, and the in-app browser showed the token login screen. This is not the physical-device/token-rotation matrix.
- [x] Launched the publish-directory EXE with an isolated tool-empty data root. Real first start fetched and validated yt-dlp 2026.08.19, FFmpeg/ffprobe N-127117-g98e92563a3-20261002 and Deno 2.9.7; managed Deno executable returned its version. No fake tool was used. On the second launch the tool manifest SHA-256 and 47-file inventory were unchanged; a window opened and closed cleanly.
- [x] Repeated the full Debug test suite (277/277), the extension test suite (14/14), Release Web Remote tests (6/6), Release localization/appearance tests (7/7), and the native MFN Automation smoke in PL/Light with custom-range flow. The isolated Release and Chrome-started app logs had no full YouTube URLs, Bearer header values, cookie/token assignments or `cookies.txt` paths in the searched patterns, and no new crash file was found.
- [ ] Complete remaining Chrome popup, actual protocol-prompt, closed-app Add-to-queue, browser restart, Light/medium/narrow viewport, plus physical phone/LAN, human audiovisual, DPI and full native visual gates listed below. The controlled native desktop and second-device surfaces are not available to this QA session.

This checklist distinguishes dated technical evidence from current blocker policy. Before any public release, close active release blockers above, choose the application license and obtain publication approval. Historical unchecked optional/post-1.0 rows are not all mandatory 1.0 gates. Do not create or push `v1.0.0` automatically.

The dated notes below retain the state of earlier validation passes. References there to an “existing release-candidate ZIP” mean the pre-2026-10-03 archive; the current local ZIP above was rebuilt from the present working tree and is still **not** approved for public release.

ModernFormsNext source revision: `dc55839b061485121a8e5dd8092ea2a79e4d7572` (`origin/master` fetched 2026-09-22).

Dependency-update validation (2026-09-22): restore, Debug/Release builds, 166 application tests, 32 upstream ScrollableControl tests, one upstream native Windows scroll-layout test, automation smoke, three-pass Queue ↔ History scroll repro, playlist pagination/scroll, external-URL forwarding, and 13 browser-extension tests passed. EN/Dark and PL/Light playlist previews were visually inspected at 1440×900 and 1280×720. Full Chrome, DPI, and manual UI matrices remain open below; this update did not republish the release-candidate ZIP.

2026-09-26 local follow-up: `/live/<id>` route and replay policy, classified failure details, bounded retries, and optional admission pacing were added after that release-candidate snapshot. Debug and Release builds passed with zero warnings/errors; 206/206 application tests passed in each configuration, 13/13 extension tests passed, and semantic MFN Automation smoke on a synthetic `/live/replay` passed. These were local/fake-tool results, not real-site or Chrome confirmation. No new ZIP, tag, or release was published in that pass.

LAN remote and media ranges added after that follow-up were **not part of the pre-2026-10-03 release-candidate ZIP**. Automated loopback/fake-tool validation does not close these manual gates:

- [ ] Real phone/tablet/desktop browser smoke over a trusted LAN, including Windows Firewall behavior and token rotation.
- [ ] Confirm the chosen LAN bind address is not reachable from an untrusted network; no port forwarding/UPnP.
- [ ] Real lawful VOD/replay section download: verify only the requested portion is transferred, probe output duration and A/V alignment in MP4, MKV and WebM, and document keyframe offset.
- [ ] Real active-live custom-range rejection and playlist containing a shorter entry.
- [ ] Native desktop range controls and web panel visual/responsive smoke in PL/EN, Light/Dark, and 100/125/150% DPI.
- [ ] Rebuild/publish a new release candidate only after these checks and the application-license decision; do not tag or publish automatically.

Subtitles, user-selected cookies-file authentication, and SponsorBlock were added locally on 2026-09-27 and were **not** in the older ZIP. Before any public release:

- [ ] Test real manual and automatic captions in at least one lawful video: sidecar SRT/VTT, MP4/MKV/WebM embedding, missing-language warning, and audio-only sidecar behavior.
- [ ] Verify SponsorBlock Mark chapters and Remove on lawful media; inspect A/V timing and keyframe-aligned cuts. Confirm no re-encoding occurs.
- [ ] Test an authorized login/age-gated/private video or playlist with a user-provided Netscape `cookies.txt`; never export, copy, or log cookie contents. Verify missing/unreadable/malformed/rejected-file errors.
- [ ] Recheck desktop PL/EN, Light/Dark, resize/DPI, playlist advanced options, browser quick/default and open-in-app flows, and Web Remote default-only switches on real devices/browsers.
- [ ] Keep custom range + subtitles/SponsorBlock and SponsorBlock Remove + subtitles disabled until timeline interactions are verified end-to-end.

### Real-media validation slice — 2026-09-27

This is a partial, local validation of the current working tree, not a sign-off for the existing release-candidate ZIP. Test outputs and logs are isolated under ignored `artifacts/manual-validation-20260927/`. The combined release gates above remain open where any scenario is missing.

- [x] Official Blender *Sintel* VOD: real yt-dlp/FFmpeg analysis and manual English SRT, VTT, ASS sidecars; automatic English SRT independently selected; files contained timed cues.
- [x] Real MP4/MKV/WebM subtitle embedding: ffprobe found `mov_text`/`subrip`/`webvtt` subtitle streams tagged `eng`, with audio/video stream copy in the FFmpeg commands.
- [x] Missing subtitle language (`zz`): video completed with a readable unavailable-subtitles warning, not a failed job. Audio-only plus subtitles produced a sidecar and a readable no-embed warning.
- [x] Real MP4 range 00:00:30–00:01:30, start-only 00:13:00–end, and end-only start–00:01:30: ffprobe confirmed short outputs; the log used yt-dlp sections without full-download fallback.
- [x] Real MKV/WebM 00:00:30–00:01:30 range outputs: ffprobe measured 65.523 s/70.000 s, respectively. The extra duration is a keyframe-cut limitation; A/V sync was not visually/audibly verified.
- [x] Open Blender *Big Buck Bunny* with a real SponsorBlock `outro` record: OFF baseline 596.521 s; Mark/MKV created the correct 490.180–580.312 s chapter; Remove/MP4 shortened output by the recorded 90.132 s, and direct yt-dlp FFmpeg commands used `-c copy`.
- [x] **SponsorBlock Mark/MP4 excluded:** historical MP4 output contained a 0–0 s SponsorBlock chapter instead of 490.180–580.312 s, reproduced with direct yt-dlp 2026.08.19. The current policy rejects Mark unless explicit MKV is selected; the supported Mark/MKV path was verified on real media. This closes only the malformed-MP4 exposure gate, not general SponsorBlock/Remove sign-off.
- [ ] **SponsorBlock Remove/MP4 edit-point timing FAIL:** decoded video presentation time stepped backward by about 83 ms at the cut, with one duplicate frame; audio timestamps in the checked window remained monotonic. A standalone yt-dlp Remove with the same format reproduced it, while the OFF baseline did not. The segment was removed and short decode windows succeeded, but human A/V playback and an acceptable edit-quality decision remain open.
- [ ] Authenticated-cookie smoke is pending a user-supplied file. Historical browser extraction failed here (Chrome database copy and Edge DPAPI); that mechanism has been replaced by user-selected Netscape `cookies.txt`. Synthetic header-only file tests validate path handling, not authenticated media.
- [x] Real Web Remote rejected custom range + subtitle defaults and custom range + SponsorBlock defaults before adding any queue item, with an explicit incompatibility message. Remove + subtitles still needs native UI verification.
- [x] Local Web Remote in an actual in-app browser: authentication on/off, token rejection without authentication, add-to-queue, pause/resume, completed list, and removal. Authentication was restored to ON and the temporary token cleared from the clipboard.
- [x] Official NASA completed-live replay (`was_live=True`): real app analyzed and downloaded a 152,953,887-byte 360p H.264/AAC MP4; ffprobe measured about 3675 s. Its completed entry remained visible in the History view after restarting the isolated app. Active/upcoming live and real Chrome `/live` routing remain untested.
- [x] Natural HTTP 403 while downloading WebM: automatic retry re-resolved formats and the third attempt completed. No fault was deliberately induced at the media service.
- [x] After the reproduced application defects were corrected, Debug/Release solution builds passed with zero warnings/errors and 268/268 application tests passed in each configuration; 13/13 extension unit tests passed.
- [x] Post-validation hardening: browser-profile extraction replaced by user-selected Netscape `cookies.txt`, with save/runtime validation, path redaction, and centralized `--cookies`; synthetic public-media yt-dlp analysis accepted the file. Successful authenticated media remains untested without a user-supplied file.
- [x] Mark/MP4 explicitly blocked; only the real-verified Mark/MKV path is supported. WebM and Auto Mark are also blocked pending verification. Remove remains experimental with a visible warning; human A/V quality approval is open.
- [x] Real pacing on four short public Blender ranges: concurrency 2 and 3 both completed all items with 3–5 s admission spacing, visible countdown and pause/resume. A separate countdown shutdown exited cleanly in 180 ms, retaining three queued and one cancelled item in persistence. Exact timestamps are in the ignored validation report.
- [x] Post-hardening restore and Debug/Release solution builds: 0 errors and 0 warnings; full application tests 276/276 in each configuration; extension unit tests 13/13; native MFN Automation range smoke in PL/Light and EN/Dark, including the localized custom-range warning. Real browser/visual/DPI checks remain separate.
- [ ] Still required: real Chrome extension; a successful authorized cookies-file flow; SponsorBlock Remove human A/V quality review; active/upcoming live samples; phone/LAN/firewall; native Light/Dark and PL/EN visual matrix; DPI. Do not mark the corresponding combined gates above complete.

## Repository and version

- [x] Git repository initialized on `master`.
- [x] `.gitignore` excludes build, test, runtime, tool, local-worktree, and Codex artifacts.
- [x] Source checked for absolute user paths, credentials, cookies, tokens, and local runtime data.
- [x] Version, AssemblyVersion, FileVersion, and InformationalVersion set to 1.0.0 centrally.
- [x] Product, description, author/company, copyright, and repository URL configured.
- [ ] Application license selected and `LICENSE` committed.
- [x] Private repository approved and GitHub `origin` created.
- [x] Final branded Windows `.ico` approved and wired to executable and application windows.

## Build and packaging

- [x] `dotnet restore`.
- [x] Debug build.
- [x] Release build.
- [x] Complete automated test suite.
- [x] Self-contained `win-x64` publish.
- [x] Publish launched from its output directory.
- [x] Main ZIP contains no PDB, tests, fake tool, yt-dlp, FFmpeg, Deno, logs, cache, or runtime user data.
- [x] Separate symbols ZIP created.
- [x] SHA-256 checksum created.
- [x] README and THIRD_PARTY_NOTICES included in the package.

## Managed tools and connectivity

- [x] Live clean-profile first start downloads and verifies yt-dlp.
- [x] Live clean-profile first start downloads and verifies FFmpeg/ffprobe.
- [x] Deterministic provisioning tests download, verify, activate, reuse, and update Deno.
- [x] Live clean-profile first start downloads and verifies Deno (2026-10-03 publish-directory EXE, isolated data root; second start reused the installed version).
- [x] Second start confirms valid tools are not downloaded again.
- [x] Installed-tools start without internet remains usable.
- [x] First start without internet shows a readable error and Retry without crashing.

## Download workflow

- [x] Deterministic fake-tool Analyze → queue → download → merge → final file.
- [x] Live lawful YouTube video download (Big Buck Bunny, 360p video + audio merge).
- [x] Live final JSON sidecar has the same basename and valid full metadata.
- [x] Live publication date produces the expected filesystem CreationTime.
- [x] Automated sidecar OFF behavior.
- [x] Automated publication timestamp OFF behavior.
- [ ] Live premiere/completed-live date smoke, if a suitable lawful sample is available.
- [x] Synthetic completed-live replay analysis → queue → download; active/upcoming/processing statuses classified in unit tests.
- [ ] Real `/live/<id>` browser-to-app smoke is still open: active stream and completed replay were analyzed through the native Debug app on 2026-10-03, but the Chrome extension/protocol leg is not available yet.
- [x] Synthetic HTTP 403 → retry → fresh metadata/format analysis → success.
- [x] Automatic retry categories, caps, cancellation during backoff, and settings normalization covered by tests.
- [x] Admission pacing tests verify spacing with concurrency 3; default-off behavior retains existing 1–3 concurrency tests.
- [ ] Manual 3–5 second pacing smoke with concurrency 2 and 3, pause/resume, cancel, and shutdown.
- [ ] Real Chrome `/live/<id>` quick-queue and open-in-app smoke, including SPA navigation.
- [ ] Reliable browser-side private-playlist signal remains unverified; do not infer from one CSS selector or translated label.
- [x] Automated `Auto`, MP4-compatible/incompatible, MKV stream-copy remux, WebM-compatible/incompatible, sparse quality, 1080p maximum, 4K, and audio-only selection coverage.
- [x] Live MP4/MKV/WebM container smoke on lawful *Sintel*, re-probed on 2026-10-03; stream-copy commands and actual codec/container results recorded in `VALIDATION_REPORT.md`.

## UI and persisted behavior

- [x] Details full metadata.
- [x] Details missing description, thumbnail, formats, subtitles, and chapters.
- [x] Details long Unicode title.
- [x] Details repeated open/close.
- [x] PL and EN principal screens.
- [x] Light and Dark principal screens.
- [x] System theme persistence covered automatically.
- [x] 1440×900 and 1280×720 layouts.
- [ ] 100%, 125%, and 150% DPI manual matrix.
- [x] One-item and several-item queue.
- [x] Queue overflow, auto-scroll, wheel routing, per-item cancel, retry, removal, persistence, and concurrency limits 1/2/3 covered by implementation/tests or observed smoke.
- [ ] 20+ item queue manual stress pass.
- [x] History creation and restart persistence.
- [ ] Manual missing-history-file action and shell open-file/open-folder matrix from published build.
- [x] Empty, invalid, and unsupported URL handling covered by implementation/tests.
- [x] Live unavailable-video error smoke.

## Documentation and publication

- [ ] Real Chrome smoke: Dark/Light select closed/open, keyboard/focus, closed-app quick queue, open-in-app options, running-app forwarding, popup 720p, watch, Shorts, explicit playlist, browser restart, and moved-EXE association repair.
- [ ] Confirm browser external-protocol prompt and a queue item after each action; custom-URI launch alone is not an acknowledgement.

- [x] README reviewed.
- [x] CHANGELOG 1.0.0 reviewed.
- [x] THIRD_PARTY_NOTICES reviewed against the selected yt-dlp, GPL FFmpeg, and Deno sources.
- [x] CURRENT_STATUS separates implemented, tested, limited, and planned behavior.
- [x] CI workflow prepared for push and pull requests.
- [x] Tag-only release workflow prepared with a hard `LICENSE` prerequisite.
- [x] Historical pre-2026-10-03 ZIP contents inspected (208 entries); current 352-entry ZIP hygiene and checksum are recorded in Final manual QA above.
- [ ] User approval for public release.
- [x] Push `master` to the private repository.
- [ ] Create and push `v1.0.0` tag.
- [ ] Verify GitHub Actions release job.
- [ ] Verify GitHub Release assets and checksum.
