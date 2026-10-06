# Stabilization report — 2026-10-04–05

**Historical report:** the sections below preserve the October 4–5 evidence, old framework pins, raw failures and then-missing license. The October 6 publication pass is recorded separately in the final release closure section; those historical blockers are not current release gates.

## Executive summary

**Assessment: NOT READY for public release: application license is missing.** No confirmed blocker in the stable main VOD / native resume / FromNow flow was found in the scoped P0 pass. The user's Oct 5 policy supersedes the previous exhaustive gate policy: missing device/source combinations and the suspended soak are not automatically blockers. SponsorBlock Remove has a confirmed human-visible defect and remains explicitly Experimental, default OFF, not stable-qualified. This report does not turn interrupted tests into PASS or claim absence of memory leaks.

No commit, staging, push, tag, release, NuGet or extension publication. No license chosen. Existing unrelated dirty app state and the dirty main ModernFormsNext checkout were preserved. Test profiles/helper projects/media/captures stay outside the source tree or ignored artifacts; ordinary user AppData was not moved/deleted. No firewall rule or WAN exposure was added.

## ModernFormsNext

- Before: `6d341963dfa0468c3636ea251e107ee34d0d1272`.
- After fetched `origin/master`: `bcf2bcb1726cbb697d2e56600188c4ef2c91ebe1`.
- Initial Oct 5 re-fetch equaled the pin. A later fetch in this P0 pass advanced `origin/master` to `f521f9dfcfe601bf9b6199b88132cccb2380d1bf` (three Android windowing/documentation commits). The app retains the explicitly requested verified `bcf2bcb...` pin, consistently across development/CI/release. It is an ancestor of current master, not described as latest fetched HEAD. No unrequested dependency migration was mixed into this scoped pass.
- Four upstream commits: canonical focus ownership (`021b632`, merge `984fbca`) and native validation transactions (`8d7d883`, merge `bcf2bcb`). Main focus/validation contracts and compatibility diff were audited before switching the clean detached dependency checkout.
- Local ProjectReference uses `.mfn-master-worktree`; development, CI/release and active documentation pins agree. No framework source change/workaround was introduced.
- Serialized restore/build/test (`-m:1 /p:UseSharedCompilation=false`) passes on the new pin. Final Debug/Release each: 375/375 tests, zero skips and zero build warnings/errors. Extension: 16/16. Remote time editor: 25/25 JavaScript tests. The seven Web Remote HTTP/theory cases are included in the .NET total, not additional tests.

## Functional inventory

Statuses apply to complete feature qualification, not just implementation existence. Automated coverage here refers to the current passing suite; dated real-media results are explicitly historical where not repeated.

| Feature | Implementation | Automated tests | Current real test | Visual/device test | Known limitation | Status |
| --- | --- | --- | --- | --- | --- | --- |
| VOD analysis/download | Existing metadata → selector → worker | Unit/process/integration | Public Sintel short range; Archive full opaque source | Native basic flow | Not all sources/options | PARTIAL |
| Playlists | Existing paged preview/batch workers | Entries/filter/order/duplicates | No fresh real playlist batch | Native 220-entry preview/add stress | Full real/size/state matrix pending | PARTIAL |
| LIVE | Separate session service/scheduler/executor | Limits/migration/recovery/probe/actions | Current Release FromNow, Stop/save, child reconnect + 3 parallel VODs | Earlier/current synthetic native flows | Full real matrix/playback incomplete | PARTIAL |
| Source catalog | Active executable list/cache/identity | Parser/cache/failure/update/privacy | Active managed catalog audited | Earlier dated source-dialog QA | Full performance/device matrix pending | PARTIAL |
| Check URL | Metadata-only service, explicit navigation | No queue/LIVE mutations | Archive; failed TED/Vimeo/W3C attempts below | Earlier dated dialog QA | Complete real/auth/source matrix pending | PARTIAL |
| Format selection | Known codecs plus conservative probe-gated fallback | New selector/executor regressions | Archive full Auto/MKV pass; bad OGV range rejected | Basic quality selection | Only conservative opaque fallback qualified | PARTIAL |
| Containers | Existing lossless merge/remux | Compatibility/process tests | Auto short range; full opaque OGV and MKV | No complete current matrix | Full MP4/WebM matrix pending | PARTIAL |
| Time ranges | Existing MediaTimeRange | Parser/API/process coverage | Sintel 2–10 s, actual final media | Native/remote subset | Broader range/container/keyframe matrix pending | PARTIAL |
| Remote time editor (scoped fix) | Numeric Hours/Minutes/Seconds → existing API | 25 client cases plus HTTP assets/range tests | Real 2–10 s queue/download | User confirms phone editor and playback; responsive Chrome widths | Does not qualify every range/device/source | VERIFIED |
| Subtitles | Existing manual/automatic sidecar/embed | Unit/process/compatibility | Historical MP4/MKV/WebM evidence | Fresh human sync matrix absent | Complete language/embed/playback matrix pending | PARTIAL |
| SponsorBlock | Existing Mark MKV / experimental Remove | Policy/argument tests | Malformed Remove join re-probed | User playback FAIL + supplied recording inspected | Experimental Remove with visible defects, not stable-qualified | PARTIAL / Remove FAIL |
| Cookies.txt | Explicit user file, never browser DB | Safe diagnostic/redaction tests | No user-supplied cookie file | Not tested with an account | Authenticated media not qualified | UNVERIFIED |
| Retries/resume | Existing bounded retry/workspaces | Failure/timing/.part fake tests | Real two-host and native ZIP VOD .part reuse; LIVE reconnect | Current normal native close/reopen completes | Unknown killed-child exit needs explicit Retry | VERIFIED scoped native resume / broader PARTIAL |
| Pacing | Existing admission timing | Clock/random/cancel tests | No new dedicated real pacing test | Native normal flow | Fresh dedicated real matrix pending | PARTIAL |
| History | Existing central completion/persistence | Ordinary/LIVE entry tests | New Sintel/Archive completion | Native normal navigation | Missing/deleted shell targets not fully tested | PARTIAL |
| Extension | Existing shared modal/transport | 16/16 Node tests | Chrome watch/SPA/playlist/replay and running-app forwarding | Actual Dark/Light modal inspection | Complete contexts/popup/protocol/restart matrix pending | PARTIAL |
| Web Remote | Existing opt-in host/queue/LIVE projection | Auth/Host/Origin/rate/input/shutdown tests | Actual phone LAN + real short download | User confirms revised numeric editor | Token-ON/QR/firewall/full LIVE actions pending | PARTIAL |
| Managed tools | Existing provision/validate/update/leases | Failure/rollback/checksum/custom tests | Fresh managed install and unchanged second-start binaries | Published EXE empty profile; Ready and real browser request | Offline/update matrix incomplete | PARTIAL |
| Logging | Existing central sanitization | Sanitizer/failure tests | 15 QA logs: zero specified secret-pattern matches | No debug console added | Not an exhaustive privacy audit | PARTIAL |
| Crash diagnostics | Existing fatal reporter/state seam | Controlled report-file/redaction tests | No production-profile crash forced | No observed QA app crash | Continuous soak not qualified | PARTIAL |
| PL/EN | Existing shared resources | Localization parity and client labels | Actual Polish remote flow | Native PL/Light + EN/Dark subset | Exhaustive UI pass absent | PARTIAL |
| Themes | Existing Light/Dark/System | Appearance and six card-style cases | Current remote/Chrome subset | Native Light popup / Dark card and playlist | Full System/interaction matrix pending | PARTIAL |
| DPI/layout | Existing framework/layout | Bounds/scroll tests | Actual native 100/225% HWND | Four screens PL/Light, EN/Dark/System inspected; remote widths retained | Physical 125/150% unavailable, full matrix deferred | PARTIAL / scoped layout PASS |
| Startup/recovery/shutdown | Existing separate stores/policies | Corruption/ownership/cancel tests | Phone closure, native LIVE reconnect; GUI VOD retains partial | Native smoke shutdown | Full interruption matrix pending | PARTIAL |
| Packaging | Existing self-contained script/profile | Release-readiness tests | Publish/ZIP hygiene, first/second provisioning retained | Current ZIP native VOD close/reopen/final output PASS | Not every deployment combination qualified | VERIFIED scoped / broader PARTIAL |
| Security/privacy | Existing narrow external/remote policy | Current boundary tests | Scoped QA auth OFF only by user request | No firewall/credential changes beyond QA request | Not a penetration-test claim | PARTIAL |

## Fixed during stabilization

### Missing codec facts / contradictory container facts

Real Archive metadata has three direct formats with no codec names. Before the fix, selection rejected all of them. New before-fix tests reproduced four failures: two safe opaque-selection expectations and two contradictory explicit-container audio cases.

Known compatible streams remain preferred. The new fallback is only for non-LIVE Auto/MKV, positive dimensions, recognized media extension, actual direct HTTP(S) URL/protocol, both codec names absent and no DRM marker. Explicit `none`, audio-only, unknown protocols/extensions and explicit MP4/WebM are not guessed. `RequiresStreamProbe` forces actual positive-duration A/V/format-name validation before exposing final media; a remuxed opaque candidate is probed again. Missing/invalid/silent media fail once with Format classification, retain workspace and create no success History/final file. Cancellation propagates through the bounded probe.

An actual 2–12 s Archive OGV → MKV test returned tool exit 0 but invalid timestamps/no valid final duration. The old intermediate harness caught the invalid final file after the app had marked it completed. The new post-remux guard was added with a process regression and real retest: **Failed/Format, no final file, no History, recovery input retained**. No hidden transcoding was introduced. This is correct failure handling, not successful OGV-range support.

### Phone time input

Root cause: the former text box required `hh:mm:ss` but specified `inputmode="numeric"`; mobile numeric keyboards do not expose the needed colon. Replaced it with labeled numeric Hours/Minutes/Seconds groups for From/To, 48 px touch fields, 16 px input font, responsive layout, live preview, PL/EN messages and clear optional-bound semantics. The HTTP API still receives the original time strings. Full mode disables hidden fields and sends blank bounds, fixing stale hidden ranges.

25 client tests cover serialization, 24+ hours, the 30-day bound, malformed components, required/order errors, mode switching, no submission on invalid input and language refresh without resetting edits. HTTP integration verifies embedded assets and preserves the existing real API range assertion; CI executes the client suite.

The user confirmed the revised phone editor is substantially better, and separately confirmed playback of the generated short Sintel file. Neither statement is extrapolated to other devices or LIVE/SponsorBlock sync.

### Card action styling / shared text contrast

Actual published-app capture showed three unstyled card actions and a clipped full dialog-title label. Root cause: their constructor omitted the existing AppUi Primary/Secondary bindings, and the narrow thumbnail-width action used Advanced.Title instead of the existing compact Advanced.MoreOptions. Restored those bindings and reused the short PL/EN label without changing bounds, assets or layout.

Six new PL/EN × Light/Dark/System cases first failed on the long label. Their contrast assertions then exposed two shared token errors: white on the light Dark accent (2.79:1), and AccentText on the pale Light secondary pressed background (1.30:1). Dark on-accent text now uses the existing background palette color; secondary pressed uses NavigationSelectedText. Framework PrimaryText and app AccentText agree. Backgrounds/spacing remain unchanged. All four interactive-state contrast assertions meet 4.5:1; native EN/Dark 1000×720 capture shows readable compact actions. No threshold was relaxed.

One diagnostic attempt of the new test mistakenly disposed TestHost after an await on another thread. It was corrected to dispose synchronously before awaiting service shutdown. The wedged owned testhost was terminated; a premature concurrent rebuild had file-lock warnings and was not counted as validation. Final sequential runs pass with zero warnings/errors.

## VOD / multi-platform

Current tools: yt-dlp `2026.08.19`; FFmpeg/ffprobe `N-127117-g98e92563a3-20261002` (GPL build); Deno `2.9.7`. Profiles are isolated. Services and actual tools, not FakeTool, perform the real-media checks.

| Source / sample | Extractor | Analysis/selection | Actual download/probe/history | Human playback | Result |
| --- | --- | --- | --- | --- | --- |
| YouTube public Sintel `eRsGyueVLvQ` | YouTube | PASS through remote add | 360p Auto 2–10 s, 303,533-byte VP9/AAC MP4, 8.0078 s, successful decode/History | User confirmed | PASS scoped range |
| Archive `BigBuckBunny_328`, full | ArchiveOrg | PASS, opaque format `1` | Auto OGV 48,721,561 bytes, 596.471394 s, actual A/V, short decode, matching valid JSON, one History entry | Not executed | PARTIAL full qualification; technical flow PASS |
| Archive same sample, full MKV | ArchiveOrg | PASS, opaque format `1` | MKV 47,767,733 bytes, 596.483 s; input/post-remux A/V probes, short decode, JSON and one History entry | Not executed | PARTIAL full qualification; technical flow PASS |
| Archive same sample, 2–12 s MKV | ArchiveOrg | PASS | Bad remux timestamps; new probe correctly rejects, no final/History | Not applicable | FAIL source range output / PASS guard |
| Vimeo public BBB `1041646710` | Vimeo diagnostic | Account required | NOT EXECUTED; no login/DRM bypass | Not executed | PARTIAL / authentication gate |
| TED Matt Cutts 30 days | Not returned | yt-dlp metadata fails: JSON NoneType error | NOT EXECUTED | Not executed | FAIL upstream extraction in this environment |
| W3C Sintel trailer direct URL | Not returned | Network error before metadata | NOT EXECUTED | Not executed | PARTIAL / network |
| YouTube playlist/replay | Historical documented extractors | Earlier dated metadata/flow | Fresh real batch/replay pending | Not executed | PARTIAL |
| Twitch VOD/LIVE | Historical Twitch extractors | Earlier dated metadata/capture | Fresh current real matrix pending | Not executed | PARTIAL |
| Kick / TikTok / further sources | Not yet qualified | NOT EXECUTED | NOT EXECUTED | Not executed | UNVERIFIED |

Sample discovery references: [TED talk](https://www.ted.com/talks/matt_cutts_try_something_new_for_30_days), [Vimeo BBB page](https://vimeo.com/1041646710), [W3C HTML5 media test](https://www.w3.org/2010/05/video/mediaevents.html). Actual success/failure claims above come from executed tools, not those pages.

Real service-host shutdown and continuation passed across two separate production-service processes: a 2,219,478-byte .part survived; the stable queue/session IDs were restored as Interrupted; manual Retry produced first observed progress of 2,220,502 bytes (existing + 1,024), then a verified 596.471394-second A/V file and one History entry. Profile: `%TEMP%/mtd-stabilization-resume-start-2e5260c4d61a44f9add04f3096009856`. This is actual service-host byte reuse, distinct from the newly completed native ZIP P0 gate below. Full container/source qualification is deferred.

## Playlists / queue / History

Native EN/Dark 1000×720, 220-entry synthetic playlist: PASS preview/paging/filtering, bulk add, duplicate/order/persistence checks, **218 queued** after excluded entries. Profile: `%TEMP%/mtd-automation-smoke-4e7e900ce792493da24902895df07edf`. This is process/native UI evidence, not 218 real Internet downloads.

The backend soak begins with 50 FakeTool VOD jobs completed at concurrency 3 and then persists ordinary queue/history while exercising synthetic LIVE completions. Complete mixed-state native queue stress, every 1/10/40/100 playlist size, filesystem shell actions and final real recovery still require qualification.

## LIVE

Ownership is unchanged: `LiveSessionService → LiveRecordingScheduler → existing LiveRecordingExecutor`; separate stores/limits from VOD. Current automated FromNow/FromStart/monitor/migration/retry/Partial/restart/shutdown and independence tests pass. Existing dated real Twitch FromNow, Stop/save, reconnect parts, Partial and manual/automatic restart evidence stays in `VALIDATION_REPORT.md`; it is not silently promoted to current full validation.

Fresh final-candidate self-contained Release Twitch WildLifeCam passes FromNow, Stop/save, controlled capture-child termination and reconnect into two independently verified MKV parts. Three short real Sintel VOD sections complete concurrently: VOD peak 3, LIVE 1 and four capture child processes observed. Both LIVE parts pass ffprobe A/V/positive duration and two-second decode; two LIVE History entries. Final Part 2 is 4,991,108 bytes / 17.866 s. Profile: `%TEMP%/mtd-release-live-36cdac183af8401192e979d8f826a579`. This is not human sync review, natural source end or a 3+2 real-load claim.

Fresh full real restart/Partial/eligible FromStart, imminent upcoming → active and natural source end remain PARTIAL/NOT EXECUTED. Older dated evidence is not silently promoted. No hours-long recording/event wait is used to claim those gates.

## Subtitles / SponsorBlock / cookies

Existing tests pass; compatibility remains unchanged: custom ranges exclude subtitle/SponsorBlock post-processing, Mark requires explicit MKV, Remove is experimental and excludes subtitle embedding. In this pass the user reports frozen video / apparently out-of-sequence frames at the Remove edit point and supplies a 38.2-second screen recording, `20261005-1745-25.2640432.mp4`. Frame inspection confirms unstable playback near 08:10. This is **human playback FAIL**, not an acceptable invisible timestamp defect. Audible discontinuity and obvious sustained A/V sync are not independently qualified.

Read-only re-probing the existing BBB `(5).mp4` finds the known 490.250 → 490.1667 s backward presentation step plus **104 decoded frames with 0.000011 s durations**, crowded into 490.166667–490.167822 s. This is materially stronger than merely noting one duplicate/backward frame. Earlier standalone yt-dlp reproduction establishes the upstream stream-copy Remove pipeline boundary, but the precise player-freeze mechanism is not newly proven by a contact sheet alone. No new large download, remux or transcoding workaround was performed. The original file and screen recording were preserved outside source control.

Closer inspection samples the recording every 0.5 s at 5–13 s, before the later deliberate pause/seek; repeated picture occurs while the player still displays its Pause action. A decoded-frame MD5 comparison also finds **146 exact image matches** between Remove 489–494 s and the existing uncut/Mark MKV 578–586 s (original timebase 1/24). Thus the later scene is genuinely from the source around 09:38–09:46, relocated near 08:10 by removing the outro; an arbitrary foreign-frame insertion is **not proven**. The malformed join timing and frozen playback are confirmed. Visible playback defect: YES; audible discontinuity and sustained A/V-sync defect: UNQUALIFIED, not invented.

**Release disposition (user policy §11 A): retain Remove as Experimental with the existing warning, default OFF, no stable/sync guarantee; recommend Mark / explicit MKV instead.** Do not promote Remove to supported-quality PASS. Ordinary VOD playback already has the user's scoped confirmation. Short subtitle / normal LIVE / recovered LIVE playback questions were sent with existing files; absent a reply they remain NOT EXECUTED, not PASS. Broader language/container qualification is POST-1.0 QA.

Cookies: **NOT EXECUTED — optional feature requiring user credentials.** No user-supplied cookies.txt; default OFF, safe path/content redaction and readable failures remain covered synthetically. No browser DB/DPAPI access. Not a basic 1.0 blocker.

## Supported sources / browser extension

Dynamic executable path/version/hash catalog, strict broken markers, grouping/search/pagination, cache/update/failure and side-effect-free checks remain covered by the passing suite. Registry stays per extractor/feature/date; Archive metadata or one full opaque output does not grant a platform-wide Verified badge. Generic is not universal. No external/remote allowlist expansion.

Current Chrome: watch button/modal/cancel, centered Dark/Light modals, no enqueue on opening, watch-to-watch SPA title refresh and a single button PASS. NASA playlist modal shows the actual title and 3 items; /live/ replay uses explicit open-in-app only. Actual Open in app and Add at 360p reached the running self-contained QA app through second-instance forwarding (one primary process, queued while paused). This is verified by app logs, persisted queue and own-HWND capture, not just extension success text. The browser's initial Device theme was restored after Light testing. Full Shorts/active-live/popup/cold app launch/protocol-prompt/browser restart matrix remains PARTIAL; user tabs were not restarted. Extension runtime/transport/assets were unchanged.

## Web Remote / phone / security

Phone LAN: user reports completed connection/add testing and confirms improved numeric editor after refresh. Chrome 320/390 portrait, 844 landscape, 1024 desktop: no horizontal overflow, six correctly-sized numeric fields, retained edits after Full/Custom. Actual short range completed and was human-played. Authentication was disabled only in the explicitly approved temporary QA profile; ordinary default is still token ON. App/temporary listener closed cleanly after testing; user media was not removed. No firewall modifications.

Current .NET HTTP cases cover default opt-in, token validation/rotation, wrong Host/Origin, request/body/rate limits, remote input/actions, LIVE projection, listener restart and shutdown. Complete physical token-ON pairing/QR/firewall prompt/LIVE controls/reconnect/long-session matrix remains PARTIAL, not inferred from desktop emulation. The user's final “Jest dobrze” specifically confirms normal picture/sound playback of the actual Sintel 2–10 s output; it does not qualify the other media gates.

## UI / DPI / themes / quality dropdown

Native normal PL/Light 1280×850 smoke checks analysis/queue, Settings, Details sections/open-close and shutdown. An initial quality series passed **20 strict Expand → inspect Expanded/supports Collapse → Collapse → inspect Collapsed** cycles with own-popup-HWND capture (`mtd-automation-smoke-69eb4ed368a14c90b6755da3d2345297`). Final card-style EN/Dark 1000×720 also passes 20 cycles and readable card/options capture (`mtd-automation-smoke-e6f13867d051477cb9e3b9000c49ced9`). Playlist and 20-cycle Settings/mouse stress are separate tests.

The older Oct 5 PL/Light repeat **FAIL at pass 15** is retained: fresh `Collapsed, Focusable, HasPopup`, actions `Expand, Focus`, profile `%TEMP%/mtd-automation-smoke-f6b61b0e72cd4edb8ca7b7d969437fd6`. That old run did not record native deactivation/input, so its exact cause remains unproven.

### P0 dropdown diagnosis — native and canonical state together

Minimal Form/TextBox/ComboBox repro and the actual production MainForm/DownloadsView use the public Windows Automation client/CLI and fresh canonical peers. Trace includes iteration/time/id/runtime identity/states/actions, managed selected/focus owner, native foreground/focus, real popup HWND existence/visibility/DPI and close-event stack. ComboBox is a windowless Skia control: there is **no ComboBox.Handle/IsHandleCreated**; those properties belong to its Form/PopupWindow. No duplicate semantic tree was built.

- Minimal Light and Dark: **500/500 each**, five variants × 100 (focused, unfocused, mouse moved, keyboard focus, inactive window).
- Actual MTD PL/Light and EN/Dark: **500/500 each**, the same five variants, real visible native popup after every Expand and hidden popup after every Collapse.
- Exact fresh-process CLI pattern: PL/Light **100/100 focused + 100/100 unfocused**. EN/Dark raw result **97/100 focused + 100/100 unfocused**; three failures are preserved, not recounted as PASS.
- EN/Dark failures 39/86: popup opened and became native-visible, then real window deactivation closed it before inspect. Failure 43: actual native MouseDown outside the popup invoked `Control.RaiseMouseDown → Application.ClosePopups → WindowBase.Hide`. The fresh peer and actual native popup both became Collapsed/hidden. No app view/theme/control reconstruction or stale RuntimeId occurred.
- A separate controlled activation of a second owned Form reproduces the same legitimate closure: native popup opens, main deactivates, popup hides, peer reports Collapsed / Expand / Focus. This witness PASS proves the mechanism, not the untraced historical pass-15 event.
- Final regression diagnostic PL/Light, two requested cycles: **raw FAIL at cycle 2**, not rerun to chase green. Cycle 1 opens a real 433×102 popup and collapses normally. Between cycle-2 Expand and inspect, foreground changes from the main HWND `5115742` to foreign HWND `64030030`; the app thread's active/focus HWND becomes 0, actual popup is hidden and the fresh peer correctly reports Collapsed. Profile: `%TEMP%/mtd-automation-smoke-c24e1928846c4e139e7bf577c8204fb8/quality-dropdown-native-trace.jsonl`. This matches the independently proven deactivation mechanism, not a contradictory native/semantic state.
- Separate ordinary final native EN/Dark smoke **PASS**, profile `%TEMP%/mtd-automation-smoke-519b8f51eebe4bf4a9bbdc4ab95e45c6`: normal startup without bridge, Analyze/quality/queue, Settings, all Details tabs and clean shutdown/discovery removal. This does not replace or erase the failing optional popup-lifetime test.

**Ownership:** the traced repeat failures are environmental native input/activation interrupting the harness's assumption that a popup remains open between separate requests. This is not an incorrect semantic bridge state, nor a confirmed MFN/MTD defect. It is not labeled “Automation limitation” of peer accuracy. The strict assertions remain unchanged; no Expand retry, sleep, focus reset, forced-collapse workaround or swallowed failure was added. `Test-Automation.ps1` now records native foreground/active/focus/capture and visible popup alongside every action/state; the old arbitrary capture delay was removed. No speculative framework issue or framework fix was made. The remaining historical uncertainty is POST-1.0 diagnostic follow-up, not a demonstrated user-facing blocker.

Evidence/repro source: `%TEMP%/mtd-dropdown-repro-64d9e13fea5247068564e57fb35a1c80/{Program.cs,trace profiles,summary.json}`. Diagnostic source/setup errors were fixture-only, not product failures.

### Scoped physical DPI/theme sanity

Owned native windows, canonical navigation and actual HWND backing pixels were inspected after the existing ready/view animations completed. Downloads (analyzed card), empty LIVE, Settings and populated Supported Sources: **100% PL/Light, 225% EN/Dark and 100% EN/System (resolved Dark)** show readable controls, no unusable clipping/overlap and correctly placed scrollbars. Native DPI is 96 / 216 and framework scale is 1 / 2.25. Profile captures: `dpi-primary-light-final`, `dpi-secondary-dark-final`, `dpi-primary-system-final` beneath the repro directory. This is scoped layout inspection, not the full interaction/scroll matrix.

Available monitors are 100% and 225%; **125% Dark / 150% System NOT EXECUTED — unavailable physical configuration**. Display settings were not changed and DPI messages were not faked. Earlier captures taken under the ready overlay or during transition are not accepted as page inspection. The fixture's exact `Opacity == 1` completion oracle was corrected to scheduler completion plus MFN's documented-in-source 0.0001 setter tolerance; no application animation was changed. Full/requested extra DPI combinations are POST-1.0 QA, not a confirmed layout defect.

## Soak / performance / resources

The isolated backend soak is **PARTIAL / DEFERRED POST-1.0**, no longer running and not an active release blocker. “83 minutes continuous observation before system suspension; no monotonic resource growth or crash observed. Full multi-hour uninterrupted soak deferred to post-1.0 QA.” Target was 7,260 s after the 50-job warmup. Profile: `%TEMP%/mtd-stabilization-soak-f452efe551b74f97a3f3884ed2922da4`. Production services with FakeTool processes repeatedly persisted state, accessed the catalog and completed fake LIVE sessions. There are **91 actual samples**, ending at 5,012.8686 s (~83 min) on Oct 4. No new soak was run.

Last private bytes 40,988,672; working set 82,341,888; handles 426; threads 16; 50 VOD records; 141 History records; 91 LIVE sessions. Earlier handles 618 subsequently fell. No sampled monotonic growth was established; this is not a no-leak guarantee. Its service snapshot also predates the final post-remux/card changes.

The machine entered sleep at Oct 4 20:18:53 local (Kernel-Power event 42) and resumed next day. The helper's wall-clock loop wrote `PASS 23:21:35` without intervening samples. That mechanically successful marker is **not accepted** as a continuous two-hour soak. Original evidence is retained with QUALIFICATION_NOTE.md; the outside-repo helper now rejects prolonged sampling gaps. No new uninterrupted run is claimed.

This is **not** a native UI multi-hour soak, nor proof of thumbnail/SKBitmap/event/Details/browser/resize leak safety. Existing native stress runs are separate. Startup/Analyze/220-list/catalog/view-switch latency and full resource baselines remain PARTIAL; no micro-optimization without evidence.

## Tools / logging / filesystem / network / shutdown

Current automated suites cover managed tools, bad archives/checksums/rollback/custom paths, controlled crash reports/redaction, file collisions/Unicode, corruption/recovery, bounded retry/cancel and scheduler shutdown. An actually empty profile running the published self-contained EXE installs/validates yt-dlp 2026.08.19, FFmpeg N-127149-g50d206a75b-20261003 (GPL), and Deno 2.9.7. Second start validates the unchanged binary inventory instead of downloading again. Profile: `%TEMP%/mtd-stabilization-clean-479c7c090eb44264be5d0ed360f2621c`. Tools code is unchanged by the later card fix; final Release LIVE uses this new Oct 3 FFmpeg build, while the earlier media used the Oct 2 build above.

Offline/update and real low-space/output-permissions matrices remain PARTIAL/NOT EXECUTED. A scoped pattern scan of 15 QA-profile log files finds zero suspected raw Authorization/cookie-path/header/signed-query secret matches; it is not an exhaustive privacy audit. No real crash is forced in ordinary AppData. QA startup policy changed the per-user protocol association; it was explicitly repaired to the stable published EXE afterward, not claimed unchanged. No ordinary profile was reset.

`git diff --check` reports existing trailing whitespace in `ModernTubeDownloader.csproj` plus CRLF conversion notices. That file was not changed by this stabilization pass; unrelated cleanup was not performed. Build warnings/errors are independently zero.

## Package

Final Build-Release.ps1 passes restore, Debug/Release builds (zero warnings/errors), 375 Release tests and publish, including the final Oct 5 run. Debug full tests also pass separately (375/375); final Node run passes 41/41 with zero skips (16 extension + 25 Remote time-editor). Local self-contained, multi-file, untrimmed win-x64 candidate: `artifacts/release/ModernTubeDownloader-1.0.0-win-x64.zip`, **60,471,168 bytes**, **353 entries**, SHA-256 `d785b29f46e22904edce5d7004b8b50ef1f5d70249bb019f9e744639b3cc7ca0`. The ZIP was refreshed with the final supported-sources documentation; all **350 non-Markdown packaged files** are byte-identical to the candidate used for the current runtime checks. Separate symbols ZIP retains PDBs. Inspection finds one app EXE/runtime and zero forbidden PDB/FakeTool/Automation/media-tool/runtime-data/log/cache entries; the packaged supported-sources document exactly matches the source. Authentication.Cookies.dll is a legitimate .NET library, not a user's cookies file. Ordinary native smoke passes; the optional final two-cycle popup test retains its native-deactivation FAIL above. Earlier hashes are historical. No tag/release/publication; app license remains USER DECISION REQUIRED.

The earlier extracted Archive GUI attempt retained 1,705,672 bytes after machine sleep/closure; its continuation was not executed. That historical partial result remains distinct from the new completed P0 below.

### P0 native ZIP VOD close/reopen — PASS

Actual self-contained EXE extracted from the candidate ZIP, isolated profile, real managed tools, public Blender Sintel at 360p/MP4. Native GUI owns production services; existing loopback HTTP actions add/resume the job. First process closes normally (`CloseMainWindow`, exit 0), persists Interrupted without success History, then the **same extracted EXE** starts again. No implicit resume; explicit Retry/resume finishes.

| Evidence | Value |
| --- | --- |
| existingPartBytesBeforeResume | 2,151,096 |
| firstObservedBytesAfterResume | 2,223,664 |
| minimumPartBytesObserved | 2,151,096 (never zeroed/truncated) |
| finalBytes | 34,076,243 |
| JobSessionId | d361827a-ef0f-4c51-9e82-900de3a6a859 |
| QueueItemId | f9851a70-bcca-4697-a2a4-ff510aa24b57 |
| workspace | profile/temp/sessions/d361827aef0f4c519e82900de3a6a859/formats-65D84C259711/video |
| Final media | MP4 H.264 640×272 + AAC, 888.093605 s; ffprobe + 10 s decode PASS |
| History | Exactly one, correct QueueItemId/final path |

FileSystemWatcher/actual file length proves append reuse, not an old UI/persisted progress counter; retained first-MiB SHA-256 matches. Both native app processes close normally. Evidence/source/final media: `%TEMP%/mtd-native-vod-resume-48b001dacfbc4587a22997d4b1385563/close-reopen-verified/{evidence.json,profile/output,package}`.

### Short native VOD child termination — limitation, explicit recovery PASS

Only the owned capturing yt-dlp child PID 40724 (parent native EXE PID 29424, matching workspace session) was terminated; no machine/network shutdown. Exit -1 / empty diagnostic maps to `Unknown`, non-retryable: **the requested normal automatic retry does not happen**. This is not claimed as automatic-retry PASS and the classifier was not broadened to retry every unknown failure. Preserved `.part`, Failed state and no false success are safe; explicit existing Retry refreshes metadata, retains session/workspace and completes.

Actual part 2,372,184 → first observed append 2,541,008 bytes; minimum remains 2,372,184. Session `ab2a3cb0-76ac-45de-9d1a-e437ff71abbb`, same `formats-65D84C259711/video` workspace; final 34,076,243-byte / 888.093605 s H.264/AAC MP4, ffprobe/decode and exactly-one matching History. Log contains metadata analysis before the initial run and after explicit Retry. Profile: same temporary harness root, `child-kill-verified`. Harness initially failed its final live-log read due to FileShare, after media/History assertions; the app closed cleanly. A separate closed-profile post-check confirms completion, fresh metadata, media/decode/History. That reader failure is not a product crash or a qualified automatic retry. Broader unknown tool-crash auto-recovery is POST-1.0 hardening, not broken normal-close resume/data loss.

Final cleanup inventory finds no owned app/FakeTool/QA tool/testhost process and no listener on QA ports 18766/18768. After the last native smoke, the per-user protocol was explicitly registered to `artifacts/release/win-x64/ModernTubeDownloader.exe` with the quoted `%1` argument, not left pointing at Debug or an ephemeral extracted directory. The framework source checkout remains clean, and the app staging area is empty. Existing unrelated local changes and outside-repo QA evidence are preserved.

## Disposition

- **PASS:** audited MFN pin/regression; current build/test counts; scoped normal native/220-entry playlist tests; selected 20-cycle popup runs; phone editor and real short-range/human playback; full Archive Auto/MKV technical flow; service-host VOD byte reuse; real Release LIVE reconnect with three parallel VODs; publish/ZIP hygiene and managed first/second startup.
- **FIXED:** unknown-codec selection unnecessarily blocked safe direct media; explicit-container codec contradictions; opaque remux could be marked completed despite invalid media; numeric keyboard required an unavailable colon; Full mode submitted stale hidden bounds; card actions missed shared styles; action label clipping; Dark on-accent and Light secondary-pressed text contrast.
- **FAIL / RETAINED:** Archive OGV range timestamp output (safely rejected), TED upstream extraction, old untraced pass-15 popup repeat, three exact-CLI environmental interruptions, final two-cycle popup diagnostic interrupted by native deactivation and human SponsorBlock Remove playback. These are not platform-wide failure claims or all stable-core blockers.
- **PARTIAL:** broad source/container/subtitle/SponsorBlock/LIVE/phone/browser/UI/security/resource qualification; exact older dropdown cause.
- **NOT EXECUTED:** authenticated cookies, unanswered subtitle/LIVE human playback, 125%/150% physical DPI and full matrix, real natural/upcoming transition without a suitable bounded event. These are optional/post-1.0 qualification, not silently PASS.
- **USER DECISIONS REQUIRED:** application license and final publication approval; no credentials requested or invented.
- **P0 CLOSED (scoped):** correct native/canonical dropdown agreement, traced environmental interruption ownership and 100-cycle variants; complete native close/reopen byte reuse/final media/History. Old untraced popup event remains an uncertainty, not a newly proven defect.
- **RELEASE RECOMMENDATION: NOT READY for public release solely because no application LICENSE has been selected.** Stable core is technically ready within current scoped evidence, not universally certified. No publication without the user's separate final approval.

## Release blocker triage

| Issue | Severity | User impact | Reproducible | Release blocker? | Reason |
| --- | --- | --- | --- | --- | --- |
| Missing application LICENSE | P0 / legal | No approved distribution terms | Yes: no LICENSE | YES — USER DECISION REQUIRED | Concrete publication prerequisite; cannot choose for user. |
| Dropdown closes between automation requests | P2 / harness environment | No demonstrated incorrect popup/peer behavior | Yes with real outside click/deactivation; historic pass 15 untraced | NO — POST-1.0 diagnostic follow-up | Native and peer agree; 100-cycle clean variants pass. Do not fake the three interrupted FAILs. |
| Native VOD normal-close resume | P0 gate | Advertised resumability | Complete current ZIP scenario PASS | NO — CLOSED | Actual bytes appended, stable session/workspace, valid final file, History exactly one. |
| Terminated yt-dlp child / no diagnostic → Unknown | P2 / hardening | Requires explicit Retry rather than automatic retry | Yes, exit -1 | NO — POST-1.0 QA | Failed safely, partial retained, explicit refresh/reuse/final success proven. Not advertised universal process-crash auto-retry. |
| SponsorBlock Remove frozen/out-of-sequence playback | P1 / optional Experimental | Defective edit-point output | Human FAIL + supplied recording + malformed join timing | NO for stable basic 1.0 — OPTIONAL FEATURE QUALIFICATION | User §11 A permits retaining Experimental; default OFF + existing warning, not stable-qualified. Mark/MKV recommended. |
| 83-minute suspended soak / resource certainty | P2 / qualification | No observed crash/monotonic growth; leaks not disproven | Suspension documented | NO — POST-1.0 QA | User explicitly deferred uninterrupted multi-hour observation; do not rerun. |
| Full DPI/Chrome/phone/filesystem/network/extractor matrix | P2 / qualification | Remaining coverage unknown, not proven broken | No confirmed defect from missing combinations | NO — POST-1.0 QA | Scoped evidence retained; unavailable physical 125/150% not forged. |
| Upcoming/natural LIVE/FromStart real timing | P2 / optional qualification | No universal real-timing guarantee | Synthetic PASS, incomplete real samples | NO — OPTIONAL FEATURE QUALIFICATION | Standard FromNow verified; FromStart stays Experimental; no hours-long event wait. |
| Authorized cookies.txt media | P2 / optional qualification | Authenticated sources unqualified | NOT EXECUTED without credentials | NO — OPTIONAL FEATURE QUALIFICATION | Default OFF, synthetic failure/redaction PASS, no credentials invented. |
| Final public publication | Approval gate | User controls release | Not authorized | USER DECISION REQUIRED | No staging/commit/push/tag/release/publication performed. |

**POST-1.0 QA:** uninterrupted soak, historical popup event tracing, unknown-child automatic recovery policy, exhaustive device/browser/DPI/extractor/filesystem/network matrices. **OPTIONAL UNVERIFIED FEATURES:** real cookies, FromStart/upcoming/natural-end qualifications, unanswered subtitle/LIVE sync; Remove is worse than unverified (confirmed Experimental defect). **USER DECISIONS:** application license, then final publication approval. No empty test slot is promoted to a real release defect.

## Final release closure — 2026-10-06

The owner explicitly authorized the final commit/master push, PRIVATE → PUBLIC change, annotated `v1.0.0` tag and GitHub Release, and selected **MIT License, Copyright © 2026 ProGraMajster**. LICENSE, central package metadata, user README/changelog and dedicated release notes now agree. Chrome Web Store, NuGet and other registries remain out of scope.

### Framework and focused regression

- `git fetch origin` in the clean dependency checkout found `f521f9dfcfe601bf9b6199b88132cccb2380d1bf`, three commits after `bcf2bcb1726cbb697d2e56600188c4ef2c91ebe1`: Android application/windowing host, stale Android documentation correction and merge. Shared dispatcher/application lifetime, window host policy/display origin/surface ownership and Form icon/limits diffs were inspected. No compatibility patch or new MFN issue was necessary.
- Detached development checkout is clean; central ProjectReference pin, CI/release and active documents use the same exact commit. Dirty separate MFN main checkout was not changed. Historical pins above remain historical.
- Restore, serialized Debug/Release builds and self-contained publish PASS; **zero build warnings/errors**. Full .NET tests **375/375 in Debug and 375/375 in Release**, **zero skipped**; resource parity included. Extension **16/16**, Remote JS **25/25**, total JS **41/41**, zero skips. Build-Release verifies exact framework provenance and all-pass/no-skip TRX counts.
- Native ordinary EN/Dark including Downloads, Settings persistence, Details tabs/close and one strict quality Expand/inspect/Collapse cycle PASS. Native active-LIVE PL/Light own store/completion/Details/close PASS; 12-entry playlist EN/Dark PASS (10 available entries queued). Native-owned captures inspected; no screenshots are source assets.
- Preserve an invocation FAIL: the first `-LiveSmoke` command omitted `-SampleUrl`, so it analyzed the script's ordinary `sample123` VOD default and correctly never exposed LiveStart. The log confirms ordinary VOD, not a lifecycle regression. The explicit `/live/active` fixture passes; assertions were not weakened. Existing older raw popup interruptions are still retained above.
- QA script now explicitly places temporary media/output beneath its own generated profile, rather than the user's default download directories. The isolated active-LIVE smoke on this setting passes.

### Packaging and public-source hygiene

- Packaging defect fixed: root application LICENSE was absent from ZIP. The artifact now contains MIT LICENSE and exact dependency-package redistribution notices (MFN, .NET/ASP.NET Core, Skia/HarfBuzz/native libraries, System.Drawing/SystemEvents, QRCoder, Markdig and RichTextKit including full Apache 2.0 terms).
- `Build-Release.ps1` PASS. Initial local license-only repack before CI closure: `ModernTubeDownloader-1.0.0-win-x64.zip`, **60,621,450 bytes**, **374 entries**, SHA-256 `1066d5630ef284e734c21dd60471da7c457b944876e8f80dd220f8e92e62b09c`. Separate symbols ZIP: **21,861,442 bytes**. This is a historical local validation artifact, not an assumed checksum of GitHub's independent build.
- No duplicate ZIP entries, test/FakeTool/Debug Automation/MicroCom assemblies, PDBs in the ordinary ZIP, runtime tools, downloaded media, settings/queue/history, cookies, logs or profiles. Self-contained win-x64, multi-file, no trimming/AOT/ReadyToRun. Redistribution texts and README/THIRD_PARTY_NOTICES verified. Apache text/notice whitespace-only normalization does not alter executable output.
- Extracted final self-contained EXE started; native Windows UIA (not Debug bridge) analyzed public Sintel, presented the actual title/formats without queueing/downloading, navigated Downloads → Settings → LIVE and closed normally. Settled owned-HWND captures inspected; isolated profile, existing real tools reused. No broad media matrix/soak rerun.
- Bounded `Test-PublicRepository.cjs` audits the current candidate files and every unique blob/path in all reachable commits. Original history: two commits / 93 blob-path pairs. No actual secrets or personal paths found; six exact synthetic redaction/validation/QR fixture occurrences and the scanner's own fixture definitions were reviewed. Only existing application branding and derived extension icons are binary source assets. `.gitignore` excludes all local/runtime/release outputs. This is a bounded inspection, not a guarantee against every conceivable secret encoding.

### Publication sequencing and remaining limitations

The final source commit is eligible for publication after its master CI passes. The tag-triggered workflow checks public visibility, application/tag version and exact commit identity, reruns both configurations/JS tests, builds the ZIP/checksum/symbols and publishes user-facing release notes. Actual CI/run/release URLs and independently downloaded GitHub ZIP checksum/start-close smoke are recorded in the final handoff; they cannot be represented as completed before this source commit is pushed.

SponsorBlock Remove remains Experimental/default OFF with confirmed playback defects; no transcoding workaround. FromStart remains Experimental/source-dependent. Cookies are optional user-supplied files, not authenticated qualification. Extractor availability is not universal URL/account/feature verification. Multi-hour soak, exhaustive device/DPI/browser/source matrices and automatic Unknown-child recovery remain POST-1.0. Existing stable VOD close/reopen resume and real LIVE FromNow/Stop-save/recovery evidence are retained, not repeated or erased.

### CI closure before the public tag

- First master CI run [37501313254](https://github.com/ProGraMajster/ModernTubeDownloader/actions/runs/37501313254) failed one of 375 tests. `CancelAndFailure_AffectOnlyTheirItemAndQueueKeepsAdvancing` observed `Failed` and called `Single()` on the scheduler's active workers before the final durable save had retired the failed worker. This is an incorrect test synchronization assumption, not a History error or a duplicated queue start.
- The test now waits for actual failed-worker retirement with the existing five-second deadline. It retains the single-surviving-worker assertion and additionally verifies the cancelled/failed final statuses, empty scheduler after stop and exactly-one start per item. No product scheduler, retry policy, timeout, skip or concurrency limit changed. **25 consecutive Release executions pass**.
- CI/release actions use verified Node 24 runtime versions; the obsolete Node 20 action annotation is not suppressed. Packaging saves each validation's TRX files under a unique run directory, preserving prior results without overwrite warnings.
- Fresh `Build-Release.ps1` after this correction passes: Debug **375/375**, Release **375/375**, JavaScript **41/41**, no skipped tests, zero build warnings/errors. TRX run: `artifacts/validation/20261006-171747-70f9ca7c`. Regenerated local main ZIP: **60,621,450 bytes**, **374 entries**, SHA-256 `d4f0c2b205ffdff6557ba801d74bbc9c4bc9c59f4bfd18719490bc4ab48eb027`; checksum, required licenses, no duplicates/forbidden entries PASS. Native Release EXE Downloads/Settings/LIVE/start/normal-close smoke passes again. This local checksum remains separate from the future GitHub build.
- The corrected source must pass a new master CI before visibility change or tagging. Actual successful CI/release run references and independently downloaded asset measurements belong to the final handoff and GitHub's publication records.

### Headless UI ownership closure before public tag

- CI run `37502857027` on `939b2021f8335a28e7ef8601bdfa92ec90b00f19` built cleanly, then stopped progressing in the test step for more than ten minutes. It was deliberately cancelled, not counted as PASS. A three-minute inactivity watchdog and text-only TRX/sequence preservation were added to CI/package validation; memory dumps are disabled to avoid publishing process/environment contents. Assertions and existing phase deadlines were not relaxed.
- A local two-core diagnostic run first completed 374/375 tests with a ten-second Stop-and-Save phase timeout in the five-child-process ownership test. Its raw failure remains under `artifacts/ci-two-core-diagnostic-local`; no production scheduler change or longer timeout was made. That test now emits synthetic item/worker states and the existing redacted logger tail on failure, rethrows, and bounds fixture shutdown to ten seconds. Its isolated run and five repeats passed; this does not erase the earlier timing failure or prove a universal scheduling guarantee.
- The next full two-core run was aborted after 346 completed tests. Its saved sequence identified `SidebarNavigationStyleTests` applying the global theme outside a host. MFN's synchronous `ThemeManager.Apply` delegates to the UI dispatcher: after async test setup the caller can be on a different pool thread from the earlier process-wide dispatcher, with no UI loop draining the posted work. This is an application test-fixture ownership defect, not a demonstrated MFN runtime bug.
- Sidebar tests now join the existing nonparallel `ModernFormsNext TestHost` collection and create an owned host before applying appearance. Other test appearance initializations (combo/input, playlist, sources and video-card) now use the same ordering after awaited setup. The first narrow 28/28 run passed; the next full run exposed the same missed video-card ordering, which was then corrected. Those intermediate aborted runs are not reported as successful validation. No production application/framework behavior, tests/assertions or animation policy was changed.
- After the complete fixture correction, sequential `Build-Release.ps1` with `DOTNET_PROCESSOR_COUNT=2` passes restore, both builds, **Debug 375/375 and Release 375/375**, **zero skips/warnings/errors**, JavaScript **41/41** and publish. TRX evidence: `artifacts/validation/20261006-175645-ff7dc0e2`. The five-child-process test and every previously hanging appearance case complete within their unchanged deadlines. The two intermediate video-card hangs each retained 363 completed tests and an incomplete sequence; neither was PASS.
- Regenerated local ZIP inspection PASS: **60,621,448 bytes**, **374 entries**, SHA-256 `cd8d1700068e4a571775c97f696baa2f21167bb73df0248b272e4e47b8c6169f`; no duplicate/forbidden entries, required application/dependency licenses and checksum verified. Separate symbols ZIP: **21,861,444 bytes**. Public assets must still be measured independently from GitHub's tagged build, and publication still requires green CI for the final commit.

### Unpublished tag CI persistence race

- Master CI `37507862304` on `3f860880783d260735d7387133994a316bedbd19` passed: 375/375, zero skips, zero warnings/errors/annotations. Full-history hygiene passed. The repository was made PUBLIC; anonymous README, source, master and MIT License access were verified. Annotated `v1.0.0` was initially pushed on that exact green commit (tag object `e0f63ab54f49d45d06c40f5005f21347e87231ee`).
- Additional tag CI `37508796417` then failed 374/375: `ActiveLive_NaturalEnd_SavesPlayablePartAndHistory(FromStart)` read `live-sessions.json` while a persistence rename/save was still finishing (`IOException`, test line 315). Terminal state is not worker/final-save retirement. No playback, recording or production persistence defect was demonstrated by this fixture read.
- Release workflow `37508796548` was cancelled before artifact upload or GitHub Release creation. The first cancellation request returned HTTP 502; the second was accepted, and completed/cancelled plus skipped Create GitHub Release and absence of a release were verified. No public binary release was published from the old tag.
- The natural-end test now awaits the existing graceful shutdown before opening persistence, additionally asserting empty LIVE worker ownership and the exact durable Completed identity/parts. Original natural-end deadline, content/security/argument/history assertions are retained; no production code or universal file-I/O retry was introduced. The unpublished tag is withdrawn and will be recreated only on the corrected green commit, without force-push or rewriting any commit history. Published/immutable release tags are not being moved.
- Focused test PASS (both policies), followed by ten consecutive two-policy Release runs: 20/20, zero skips. Final sequential two-core `Build-Release.ps1` PASS again: Debug 375/375, Release 375/375, JavaScript 41/41, zero skips/build warnings/errors, restore/publish/packaging pass. TRX: `artifacts/validation/20261006-181612-21c420df`. Local main ZIP **60,621,448 bytes**, **374 entries**, SHA-256 `c751b5d4e41b27fc8eb6ed8aa7b0a3f2631e30c0cab1f96a4a7eb0cbbcd57912`; this remains a local measurement, not an assumed GitHub asset hash.
