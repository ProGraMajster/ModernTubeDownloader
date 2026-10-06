# Validation reports

Current release evidence (2026-10-06, MFN `f521f9dfcfe601bf9b6199b88132cccb2380d1bf`) is in the final closure section of `STABILIZATION_REPORT.md`. The dated reports below retain previous pins/results and are historical, not current dependency instructions or publication gates.

## Historical stabilization — 2026-10-04–05

Current working-tree evidence is in `STABILIZATION_REPORT.md`, with all requested gates numbered in `RELEASE_CHECKLIST.md`. ModernFormsNext `bcf2bcb1726cbb697d2e56600188c4ef2c91ebe1` still equals fetched origin/master on Oct 5. Debug/Release each pass 375/375 .NET tests with zero skips and zero build warnings/errors; extension 16/16 and Web Remote time-editor 25/25 JavaScript tests pass. Final restore, Build-Release.ps1, publish and ZIP hygiene pass.

Scoped current evidence includes full Archive Auto/MKV technical flows, two-service-host real VOD byte reuse, final Release LIVE reconnect with three parallel VODs, native 220-entry playlist/Settings stress, card style/contrast regressions, Chrome watch/playlist/replay transport and user-confirmed phone input plus short range playback. These are not full source/device/recovery qualification.

Earlier 20-cycle popup runs pass, but the final Oct 5 PL/Light repeat FAILS at pass 15 after Expand reports Collapsed; root cause is still unproven. The backend soak is NOT QUALIFIED after machine sleep (91 samples, about 83 minutes); its next-day wall-clock PASS is not accepted. Extracted native package VOD retains a partial but close/reopen/continuation is incomplete. The broader release recommendation is NOT READY; missing gates are not inherited from older results.

## Historical independent LIVE subsystem validation — 2026-10-03

All sections below retain the Oct 3 snapshot, including its pins, counts, package hash and then-open gates. They do not supersede the current stabilization section above.

Local working-tree qualification, not public release approval. No commit, push, tag, GitHub Release, NuGet or Chrome Web Store publication was performed. Existing unrelated dirty app/framework changes were preserved. Historical validation in the changelog/checklist remains historical.

## ModernFormsNext

- Initial validated baseline: `4b0191d1c0de33e31e46c9f7a377dab833ae3d20` (293 app tests before the ownership refactor).
- Intermediate qualification: `1de8a6be35192614b690b6479143da211451a8a1` (+2), used for the detailed real-media profiles below.
- Final fetched `origin/master`: `6d341963dfa0468c3636ea251e107ee34d0d1272` (+4 from initial baseline, +2 from intermediate pin).
- Difference: `34ae376 Add public DPI change events`, `1de8a6b Merge pull request #157`, `dc7de6b Add Form.Activate method`, `6d34196 Merge pull request #159`. The latter update is additive and does not change existing app calls; no conversion of the current external-request activation code was made as unrelated scope.
- `Directory.Build.props`, CI, release workflow and active documentation agree on the new SHA. Local ProjectReference uses clean, detached `.mfn-master-worktree`; the unrelated dirty main ModernFormsNext checkout was not changed. No framework patch or compatibility workaround was introduced.
- App regression used serialized MSBuild (`-m:1 /p:UseSharedCompilation=false`). Native default EN/Dark analysis/queue/Settings/Details, PL/Light Settings dynamic scrolling (20 authentication-checkbox toggles, wheel/track/thumb), 220-entry playlist paging/scroll and external process forwarding passed on the final pin. Queue↔History three-cycle diagnostic completed on the intermediate pin; its nested-item count is diagnostic, not a complete History shell-action oracle. MouseMove/Capture diagnostics were retained, not replaced by a new system.

## BEFORE → AFTER runtime ownership

Before: `DownloadQueueService` stored LIVE records inside ordinary queue items, `QueueProcessor` coordinated upcoming checks, capture admission and retry, and `DownloadJobExecutor` dispatched a LIVE special case. LIVE consumed normal download slots.

After:

| Responsibility | Ordinary downloads | LIVE |
| --- | --- | --- |
| Central state/actions | DownloadQueueService | LiveSessionService |
| Tasks, cancellation, admission, retry, shutdown | QueueProcessor | LiveRecordingScheduler |
| Media execution | DownloadJobExecutor | Existing LiveRecordingExecutor |
| Durable store | queue.json | live-sessions.json |
| Limit | MaxSimultaneousDownloads (1–3) | MaxSimultaneousLiveRecordings (1–3, default 1) |
| Desktop presentation | DownloadsView / QueueItemCard / shell footer | LiveView / own statistics |
| Remote projection/actions | /api/queue | /api/live |

LIVE owns per-session tasks, linked tokens, child processes, stable workspaces and reconnect state. Neither scheduler references the other's state service. There is no shared execution/admission semaphore. Shared tools, failure classifier, timing abstraction, process runner, cookies and History remain shared lower-level services. The view observes the central service and never owns a process.

### Mandatory architectural proof — PASS

`FiveVodAndThreeLive_HaveIndependentThreeAndTwoWorkerOwnership` enqueues five VOD jobs and three sessions at limits 3/2. It asserts exact 3 VOD + 2 LIVE active, third LIVE pending, freeing VOD starts only another VOD, freeing LIVE admits the third LIVE, and independent lowering/raising of both settings. Stores are checked separately.

`FiveVodAndThreeLive_RunFiveDistinctChildProcessesWithIndependentOwners` additionally observes **five distinct actual child PIDs** through the process-level FakeTool (not five mocked task counters). It proves the same ownership/slot release boundary. This is process integration evidence, not five simultaneous public-network sources.

`PausedVod_StillAllowsUpcomingLiveToPollAndRecord` keeps five VOD jobs queued while active LIVE and upcoming→recording run. `StoppingEitherScheduler_DoesNotStopOrBlockTheOther` checks both shutdown directions. LIVE limits 1/2/3, once-only admission, handoff cancellation and durable final saves are covered.

## Preserved mechanisms and persistence

- Reused LiveRecordingExecutor, existing availability probe and capture policy, fresh metadata before capture/reconnect, FromNow, eligible experimental FromStart, Stop and save, MKV remux/ffprobe, Parts, Partial, cookies and Windows Job Object. VOD matching-format `.part` workspace behavior was not replaced.
- New LIVE workspace: `Temp/LiveSessions/<session-id>/part-NNNN`. Legacy migration retains its earlier `Temp/sessions/<session-id>` recovery workspace. It writes destination first, deduplicates durable IDs, then removes only LIVE queue records; retry is idempotent and normal metadata/IDs are retained.
- Waiting sessions persist and recheck immediately after restart. Monitoring uses short metadata probes, randomized inclusive configured intervals (default 30–60 s), not a long-running `--wait-for-video` process. The app must be running. A scheduled date change, single in-flight monitor, private/unavailable outcomes and transient probe recovery are tested.
- Recording/Reconnecting sessions load as Interrupted by default; the separate auto-resume switch defaults off. Partial/Interrupted can be resumed explicitly. Cancel and Stop and save remain distinct. Preserve-on-failure/reconnect default on, preserve-on-cancel off. Individual invalid JSON records are skipped/logged and complete corrupt JSON gets a recovery copy.
- Verified output is retained; unplayable raw data is not advertised as a final file. Matching separate A/V streams are recovered only from the same part/format IDs, known aligned starts and the smaller positive duration. Unknown/misaligned origins are not guessed. No transcoding, automatic concat or false byte-perfect FFmpeg append is claimed.
- Installed `yt-dlp --help` confirms continue is default and live-from-start remains experimental. This does not guarantee live-from-start or exact fragment continuation for an arbitrary source. See the [upstream documentation](https://github.com/yt-dlp/yt-dlp).
- History is shared, with LIVE/Partial/part metadata, and exposes only verified playable parts. Final worker saves remain owned/awaited by the respective scheduler.

## UI, extension and Web Remote

- LiveView has explicit analysis/start confirmation, active/scheduled/recoverable groups, duration/bytes/speed/start and schedule facts, Edit/Stop/Cancel/Resume/Open/Details, own counters and stable semantic IDs. No percentage for LIVE. Downloads and footer count only VOD.
- `/live/` opens LiveView. Watch/Shorts classified as active/upcoming by yt-dlp route to LIVE, not unattended quick recording. Replay/VOD offers Downloads. The browser extension transport/assets were not rewritten; quality presets and existing confirmed launch flow remain intact.
- `/api/queue` has no LIVE DTO fields. `/api/live` exposes safe presentation state, duration, bytes/speed, schedule/retry/counts; `/stop`, `/cancel`, `/resume` call LiveSessionService. `/api/live/analyze` is bounded. No workspace, raw metadata, signed manifest, cookie data or command arguments are returned. Recording start remains a desktop confirmation.
- Authentication, Host/Origin, body/rate limits and the production YouTube-only remote add allowlist were not relaxed. Seven real loopback Kestrel integration tests include the independent LIVE API. These are not a physical phone/LAN test.
- Native semantic live smoke: PL/Light 1280×850 and EN/Dark 1000×720. Details opens/closes and card ScrollIntoView uses framework behavior. App-only HWND captures were inspected; no screenshot of an obscuring application is used as evidence. Multiline LIVE Details text is top-aligned. This is bounded app-window inspection, not complete DPI/System/hover qualification.
- Native real Debug UI: analyzed public WildLifeCam, explicitly started FromNow, invoked Stop and save through semantic IDs, opened/closed Details, checked empty VOD store, own stats, ffprobe A/V and decode. File duration 33.800 s. Profile: `%TEMP%/mtd-automation-smoke-019fb423e5ff46c0ae4e2080f60d8c7a`.
- Intermediate-pin synthetic layout/Details profiles (including card start/schedule facts): `%TEMP%/mtd-automation-smoke-771d660f40a74e829c18b27585024cac` and `%TEMP%/mtd-automation-smoke-f2e8a1c6d7674b2fb9a67deba81fba7e`. Intermediate external forwarding profile: `%TEMP%/mtd-automation-smoke-efe8c74e61b24d71b2885dd25eda80a7`. Final-pin repetitions are recorded below.

## Real media qualification — self-contained Release

Public source: `https://www.twitch.tv/wildlifecam` (public wildlife webcam). Each capture is bounded; no multi-hour recording. Tools are real validated installed yt-dlp, FFmpeg/ffprobe and Deno, not FakeTool. Profiles are isolated with `MODERNTUBEDOWNLOADER_DATA_ROOT`; ordinary user AppData is not moved or deleted.

| Scenario | Result | Verified parts / final part bytes | Profile under %TEMP% |
| --- | --- | --- | --- |
| FromNow + parallel VOD + Stop and save | PASS | Part 1: 55.934 s, 12,129,118 bytes | mtd-release-live-f1d5542a10f340f586f827d06c593d6b |
| Controlled yt-dlp interruption → reconnect | PASS | Two playable parts; Part 2: 37.099 s, 8,431,122 bytes | mtd-release-live-25c34d31ee984b8d8401b8e494ffd6b5 |
| Controlled interruption, no additional retry | PASS | Partial Part 1: 9.866 s, 2,744,489 bytes | mtd-release-live-a99be05641d54d7fa1b815c0bf6f5921 |
| Graceful app close → restart → manual Resume | PASS | Old Part 1 + new Part 2; Part 2: 31.433 s, 8,731,361 bytes | mtd-release-live-ffbe1ea3d0794524bbc2975c8c1ee95b |
| Graceful app close → opt-in automatic Resume | PASS | Old Part 1 + new Part 2; Part 2: 31.433 s, 8,730,494 bytes | mtd-release-live-2415297dc2354346a1877cacfc6ce19e |

Every listed part has positive duration and both video/audio according to ffprobe, and passed a two-second FFmpeg decode. History contains the corresponding one/two verified outputs. Old bytes were actually remuxed into playable Part 1 before fresh Part 2; they were not discarded or silently redownloaded. This is separate-part recovery, **not** exact fragment append or uninterrupted timeline coverage. No isolated yt-dlp/FFmpeg descendants remained after these runs.

The parallel test added three distinct eight-second sections of Blender's public [Sintel](https://www.youtube.com/watch?v=eRsGyueVLvQ), 360p/MKV, through the ordinary remote add API. Peak: **3 VOD + 1 LIVE**, with **four yt-dlp capture child processes** observed together; all three VOD completed while LIVE stayed Recording. Final VOD sizes: 247,663 / 300,841 / 354,559 bytes. Each has parseable same-basename raw JSON (47 formats) and UTC CreationTime `2010-09-30 13:28:21`. Stream-copy/keyframe alignment means the second requested eight-second section probed about ten seconds; this known VOD range behavior is not a new LIVE defect. The separate mandatory synthetic process test establishes exact 3 + 2; this real-network test does not claim two simultaneous public LIVE streams.

Commands: `scripts/Test-ReleaseLive.ps1` with explicit validated local tool paths, `-CaptureSeconds 30`, and respectively `-ParallelDownloads`, `-InterruptDownloader`, `-InterruptDownloader -NoRetryAfterInterruption`, `-RestartDuringCapture`, or `-RestartDuringCapture -AutoResumeAfterRestart`. The interruption harness kills only its own identified capture child, never system networking or unrelated processes.

### Final-pin runtime repetition — PASS

The five detailed interruption/Partial/restart scenarios above used intermediate MFN `1de8a6b...`. After the additive framework update to exact `6d341963dfa0468c3636ea251e107ee34d0d1272`, the rebuilt self-contained Release repeated:

- `-ParallelDownloads`: **3 VOD + 1 LIVE**, four simultaneous capture children, all three VOD completed while LIVE remained Recording. Stop and save produced a verified **51.699-second, 14,218,313-byte** MKV Part 1 and one History entry. Profile: `%TEMP%/mtd-release-live-15e16e7db92c469b849d0a17c91f706b`. The three VOD files again had full same-basename JSON and publication-date CreationTime.
- `-RestartDuringCapture`: graceful shutdown retained old capture data; default-off automatic resume did not start a new capture unattended. Manual Resume recovered **2,744,853 bytes** as playable Part 1 and saved a new **33.033-second, 9,234,411-byte** Part 2. Both outputs are in History. Profile: `%TEMP%/mtd-release-live-aa245af44d434662a65b4c6a26727f07`.
- Every listed final-pin part passed ffprobe A/V and the harness's two-second FFmpeg decode. No app/FakeTool/yt-dlp/FFmpeg/ffprobe process remained at the final process-inventory check. These repeats do not imply that the other three intermediate scenarios were rerun on the final pin.

Final-pin native semantic repetitions also passed: active LIVE EN/Dark at 1000×720 (`mtd-automation-smoke-7d641650e7304446853cf57033de31f4`), upcoming LIVE PL/Light at 1280×850 (`mtd-automation-smoke-0579973c2cff4fbe8e93efa1f637a8f2`), normal analysis/queue/Details (`mtd-automation-smoke-9bcb5589788c4a01ac2f313700d31195`), Settings authentication toggles/wheel/track/thumb, 220-entry playlist paging/scroll (`mtd-automation-smoke-257ceb987a8b425cbd808d8e137ebcee`), and external forwarding (`mtd-automation-smoke-647eb7d0aa814de5988b86582c9541d0`). Actual app-only Dark/Light captures were inspected. The forwarding test checks active/upcoming requests with queue intent open the LIVE confirmation with **zero sessions until user confirmation**, while replay offers Downloads; this is a process-protocol test, not a fresh Chrome click test.

## Issues found during qualification

- Completion/history ordering race: VOD previously published Completed before History finished. History is now recorded before that notification; a synchronous completion-event regression asserts it is already present. Warning behavior is retained if persistence fails.
- Worker-lifetime race: removing a worker before its final save let shutdown miss that save. Both schedulers retain task ownership through final durability, then release the slot. No sleep/timeout workaround was added.
- QA array enumeration: PowerShell treated a remote JSON array as one pipeline object, incorrectly reporting zero active VOD. Explicit enumeration restored exact assertions; source logs already proved three starts/completions. Security limits were not changed.
- QA file sharing: Get-Content could deny atomic JSON replacement on Windows during a tight observer loop. The native smoke reader now allows delete-sharing and reads one old snapshot; application timeouts were not increased and exceptions were not suppressed.
- A new transient-probe test initially used an unclassified synthetic message. It now emits the actual recognized `connection reset` category; permanent failure assertions are retained. A prior long API test hit the real mutation limit, so LIVE API coverage has its own isolated host/test rather than weaker limits.
- The corruption fixture initially raced its own Add-triggered asynchronous save. It now awaits `Live.SaveAsync` before intentionally overwriting JSON; the bad-record assertions are unchanged. No production retry or test timeout was increased.
- An initial screen capture contained an obscuring window and was removed. LIVE captures now target only the test app's HWND through PrintWindow, avoiding other desktop content. Details multiline alignment was corrected after inspecting that actual window.

## Final build/test/package result

Final qualification uses exact MFN `6d341963dfa0468c3636ea251e107ee34d0d1272`. All commands retain serial MSBuild/shared-compilation-off; required tests are neither skipped nor weakened.

- Restore / Debug / Release / publish / Automation CLI build: PASS, zero build warnings and errors.
- Full .NET Debug and Release: **313/313 PASS in each**, zero skipped. Both runs include ownership/PID, LIVE/API, localization and existing regression suites. Earlier 309-test snapshots predate four added upcoming regressions.
- Dedicated LiveSubsystemTests: 16/16 PASS. WebRemoteIntegrationTests: 7/7 PASS and included in the final-pin complete suite. Localization parity and existing framework TestHost UI/style tests remain included.
- Extension: 16/16 PASS; Web Remote app.js syntax PASS.
- Local ZIP: `artifacts/release/ModernTubeDownloader-1.0.0-win-x64.zip`; **60,427,311 bytes**, self-contained, multi-file, untrimmed Windows x64. SHA-256: `c87d1f17eb2c449bf7b9f9e97b445da4662418323a8ac0b17fb3258ea653c19d`. Inspection: **352 entries, zero forbidden entries** (PDB, bundled runtime tools, FakeTool, logs, queue/LIVE runtime stores or Codex data). No public publication. Separate symbols/checksum are generated by Build-Release.ps1.
- Whole dirty-tree `git diff --check` reports existing whitespace in the untouched app csproj line 21; it was deliberately not cleaned as part of this refactor. Nothing is staged.

## PASS / FAIL / PARTIAL / NOT EXECUTED

PASS: independent runtime owners/limits/stores, mandatory 3+2 task/PID oracle, pause/shutdown isolation, migration, LIVE capture/Parts/Partial/cancel/restart tests, safe separate remote API, extension tests, bounded real parallel capture/reconnect/Partial/restart, native semantic LIVE actions/Details and app-only layout inspection.

FAIL: no remaining build/test/runtime failure in completed final qualification. Intermediate failures and their causes are documented above, not hidden.

PARTIAL: upcoming lifecycle is qualified synthetically, not by an actual imminently starting public broadcast. Resume preserves verified data as separate Parts, not exact continuous FFmpeg append. Visual qualification is only the reported sizes/themes; loopback Remote qualification is not mobile LAN.

NOT EXECUTED: real upcoming classification/auto-start; real experimental FromStart; natural end of a real public source; real VOD close/restart continuation; abrupt whole-app crash/power-loss recovery; authenticated-media run with user cookies; new full Chrome/Edge click matrix; physical phone/LAN/firewall; human playback/A/V-sync review; dedicated 125/150% DPI and full System-theme visual review.

KNOWN LIMITATIONS: app must run for polling; source/protocol/yt-dlp determine resume and FromStart support; only conservative MKV LIVE finalization is exposed; no unsafe concat/transcode; unavailable/unplayable raw data remains recoverable rather than a false Completed result; old numeric LIVE queue contracts remain migration-only. HTTP Remote remains unencrypted trusted-LAN functionality.

REMAINING RELEASE BLOCKERS: unexecuted real/human/platform gates above, application license decision and explicit final release approval. This is a validated local refactor, not a claim that the entire historical release checklist is signed off.

## Files touched by this refactor

- Active MFN pins: `Directory.Build.props`, `.github/workflows/ci.yml`, `.github/workflows/release.yml`.
- Model/settings/path/composition: `Models/LiveSession.cs`, `Models/DownloadModels.cs` (LIVE History fields/migration contract), `Settings/AppSettings.cs`, `Services/SettingsService.cs`, `Infrastructure/AppPaths.cs`, `Services/AppServices.cs`, `.gitignore` (new runtime store).
- LIVE: `Services/LiveSessionService.cs`, `LiveSessionPersistenceService.cs`, `LiveRecordingScheduler.cs`, adapted existing `LiveRecordingExecutor.cs`; conservative common-duration support in `FfmpegService.cs`.
- VOD separation: `Services/QueueProcessor.cs`, `DownloadQueueService.cs`, `DownloadJobExecutor.cs`; `Views/DownloadsView.cs`, `QueueItemCard.cs`.
- UI/resources: `MainForm.cs`, `Views/LiveView.cs`, `SettingsView.cs`, `HistoryEntryCard.cs`, `Theming/AppVectorIcon.cs`, `Resources/Languages/pl.json`, `en.json`.
- Remote: `WebRemote/WebRemoteHost.cs`, `wwwroot/index.html`, `wwwroot/app.js`.
- Tests/harness: `Tests/LiveSubsystemTests.cs`, ported `QueueProcessorTests.cs` and `WorkflowIntegrationTests.cs`, `WebRemoteIntegrationTests.cs`, `FakeTool/Program.cs`, `scripts/Test-Automation.ps1`, `scripts/Test-ReleaseLive.ps1`.
- Documentation: README, ARCHITECTURE, CURRENT_STATUS, CHANGELOG, RELEASE_CHECKLIST, AUTOMATION_TESTING, WEB_REMOTE, browser-extension/README and this report. Prefixes in this compact list are relative to the app/test project directories. Other pre-existing dirty files are not this round's work.
