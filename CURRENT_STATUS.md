# Current status

## Optional Modern Dashboard — local 1.3.0-candidate.1

Development branch: `feature/modern-dashboard-ui`, based on `558ec43`. No stable
release promotion. Classic is the migration default; both shells share the same
AppServices and page instances. Source, responsiveness and qualification details:
[MODERN_DASHBOARD_UI.md](MODERN_DASHBOARD_UI.md). Visual match is PARTIAL: native premature ellipsis is tracked in MFN #272, and owner visual acceptance is pending. Physical high-DPI qualification has not been executed.
Local candidate validation: Debug/Release 781/781 each, JS 92/92, zero warnings/errors/skips. CI qualification is blocked by the unchanged updater deadline regression in [issue #4](https://github.com/ProGraMajster/ModernTubeDownloader/issues/4); no timeout/assertion was weakened. This candidate is not ready for visual acceptance or integration.
The full machine-specific evidence and branch-isolation report stays local in
`MODERN_DASHBOARD_CANDIDATE1_REPORT.md`, excluded from publication/package data.


## Local 1.2.1-candidate.1 hotfix — 2026-10-15

Qualification: **READY FOR OWNER MANUAL TEST**, not public/stable promotion.
Source commit/push authorized on 2026-10-15; no tag/release publication. Authoritative current
results and limitations: [hotfix report](HOTFIX_1_2_1_CANDIDATE_1_REPORT.md).
MFN pin is committed notification hotfix `8d8a7679779608b4edbee7dc687c13e3b1a611d9`, based on
master `18f47b085a59e65eb611e3fd1d38e8dffa3fb19c`. Framework [PR #271](https://github.com/ProGraMajster/ModernFormsNext/pull/271)
is separate from the app update; no protected-master merge or framework release is implied.
Storage latch/checkpoint, notification resilience and Batch JSON UX are the only hotfix scope.
Real Windows 11 23H2, physical-device/playback and broad soak are not claimed.
Final local builds/tests: Debug/Release 749/749 each; JS 92/92; MFN notification tests
100/100 and scroll/layout 34/34 per configuration; zero build warnings/errors/test skips.
Isolated native PL/Dark and EN/Light storage/JSON/error/failed-checkpoint flows pass.
Tray/hidden startup, updater check, LIVE fixture, cookie Settings, loopback Web Remote,
range/Details, appearance/text scale and extracted-candidate production UIA pass.
Forced optional-provider absence selects Shell and accepts one native test notification;
no banner visibility claim. Local win-x64 ZIP has 811 entries and no runtime/user data.
The 1.2.0 evidence below is historical, not qualification of this candidate.

## Release 1.2.0 — 2026-10-11

Qualification: **READY WITH KNOWN LIMITATIONS**. Published status is recorded by the
linked GitHub Release/workflow; final post-publication results are in the local report.

Version/InformationalVersion: **1.2.0**; AssemblyVersion/FileVersion: **1.2.0.0**.
Clean MFN origin/master fetched 2026-10-11: `d36015711f63945a49025675acb9aca3df60101d`.
No framework edits. The owner explicitly authorized master push, exact-head CI,
annotated v1.2.0 and the existing public release workflow on 2026-10-11.
The [release page](https://github.com/ProGraMajster/ModernTubeDownloader/releases/tag/v1.2.0)
is the published-asset authority; local `FINAL_RELEASE_1_2_0.md` records final run URLs,
hashes and downloaded-asset smoke without a post-tag status commit.
Implemented: tray/hidden startup, owned HKCU Run registration boundary, independent
GitHub app updater with SHA-256/transactional rollback, Other tools hub, streaming
Batch preparation with bounded workers/paged UI, central 80–200% typography,
schema-1 appearance profiles/presets/live per-page settings and eleven Settings categories.
Existing queue/LIVE/auth/notification ownership is retained.

Fresh final local validation: restore, Debug/Release **713/713 each**, extension/Web Remote
JS **92/92**, zero warnings/errors/skips, self-contained GUI/NativeHost/Updater publish
and **810-entry** ZIP hygiene. Native tray/hidden start/same-instance restore, update check,
PL/Dark Batch at 200%, EN/Light appearance at 100/125/150/175/200/reset, 20-toggle Settings
scrolling, EN/Dark range/Details, active and scheduled LIVE fixtures and cookie Settings pass.
The Release UIA client uses production accessibility without a Debug endpoint: Downloads,
LIVE, Other/Batch, Updates, Startup, Personalization, text 125/reset, Current/no self-update
and clean close pass in an isolated profile. No HKCU Run changes; temporary URI association
restored. Custom QA tools/fixtures are not real provisioning/media-download evidence.
Synthetic updater installation/checksum rejection/rollback preserves userdata; source/history
privacy scan has no findings. Historical real Batch YouTube metadata/one paused handoff passed;
Vimeo failed upstream. Multi-source acceptance is PARTIAL. Candidate baseline counts/hash and
the earlier release decision are preserved in its dated implementation report.

Known limitation, **NOT RELEASE BLOCKER**: MFN implicit TabStrip labels/NumericUpDown
digits do not follow their owner's font; complete global text scaling remains PARTIAL.
Tracked by [#204](https://github.com/ProGraMajster/ModernFormsNext/issues/204) and
[#205](https://github.com/ProGraMajster/ModernFormsNext/issues/205); repro: PERSONALIZATION.md.
Physical DPI/login/shell tray gestures, clean-PC update/restart, exhaustive large-font
clipping, human file/color dialogs and long-running soak are NOT EXECUTED.
These gaps are accepted **POST-1.2 QA / PARTIAL / NOT EXECUTED**, not evidence of PASS
and not blockers. Crashes, data loss, corrupt state, security leaks, broken core flows,
CI or package failures still block publication. Fresh final validation is recorded
in the final report; the candidate report retains historical counts/hash/earlier decision.
Planned: fix MFN #204, then #205 upstream, update the app's pin, qualify a likely 1.2.1.
The v1.1.0 and older records below describe historical pins/results, not current release qualification.

## Release 1.1.0 — 2026-10-11

Version/InformationalVersion: **1.1.0**; AssemblyVersion/FileVersion: **1.1.0.0**. Development, CI and Release use clean ModernFormsNext master `69c5ea2b20b07d9df801340c8ae9e3e1ca5dfbc3`, freshly fetched 2026-10-11; no framework edits. Public publication is governed by the exact-SHA CI/tag workflow and [GitHub Release](https://github.com/ProGraMajster/ModernTubeDownloader/releases/tag/v1.1.0), not by the historical candidate records below. The post-publication local `FINAL_RELEASE_1_1_0.md` records final hashes/run URLs without adding a post-tag status commit.

Implemented: the existing VOD/playlist/LIVE subsystem plus Web Remote pairing/localization/HTTPS, extension Options/localization/generic handoff/lifecycle and optional native notifications. Secure opt-in cookie sync now includes consent-bound reverse refresh with real newer-snapshot proof, a new jar and exactly one auth retry. Active LIVE defers retry until the failed part is preserved. Expert arguments use reviewed upstream spelling/arity facts to protect credential/execution ownership. The thumbnail lock root cause was reproduced and attributed to an inherited Skia file handle in a parallel child process; bounded managed-file reads avoid inheritable native file handles.

Fresh local validation: Debug and Release **617/617 each**, extension **61/61**, Web Remote **31/31**, NativeHost **5/5** and CookieSecurity **174/174** (subsets); zero warnings/errors/skips. Native cookie Settings PL/Dark and EN/Light, EN/Dark range/Details, PL/Light active LIVE fixture, notifications settings/test action PL/Dark and self-contained normal startup/clean close pass. Fixtures are not new real-media or human banner evidence. Local self-contained package has **611 entries**, separate symbols and a checksum; source/history scan has no findings. Extension profiles can be removed from the visible list only after acknowledged deletion, without losing pending-error visibility. Chrome acceptance and publication/downloaded-asset results are recorded by the [release checklist](RELEASE_CHECKLIST.md), release workflow/page and the final local report. Failed investigative runs remain in ignored artifacts; no assertions, skips, timeouts or cleanup retries were weakened to manufacture a pass.

First release-head CI [38137460230](https://github.com/ProGraMajster/ModernTubeDownloader/actions/runs/38137460230) retained a Release-only timeout in the second-auth-failure fixture. Its 600 ms wall-clock budget measured DPAPI/disk/responder scheduling instead of the intended state machine. The canonical store now accepts a `TimeProvider` (system clock by default); tests explicitly advance expiry and retain a real watchdog. An additional expired-ACK regression rejects false freshness. Production 20-second refresh/15-second presence limits and one-retry policy are unchanged. Tagging remains gated on the final corrected head's terminal CI success.

Known non-blockers: SponsorBlock Remove Experimental/default OFF; FromStart Experimental/source-dependent; real account/extractor validity, Edge/Brave/Chromium, complete PSL, profile tombstone compaction, optional API regrant reload, exhaustive DPI/device/clean-PC/soak and provider capabilities require further qualification. Moving the installation requires native bridge repair. No real account cookies or browser DB are needed for the synthetic release test. [Feature-gap roadmap](YTDLP_FEATURE_GAP_AUDIT.md) remains planned, not implemented.

## Historical candidate.5 validation — 2026-10-10 (Unreleased at that time)

**NOT READY for complete cookie-sync acceptance or stable promotion.** This is a local reviewable candidate, not a new public release. The clean detached ModernFormsNext master pin remains `69c5ea2b20b07d9df801340c8ae9e3e1ca5dfbc3`. No commit/push/tag/publication; pre-existing candidate changes are preserved and the index is empty.

Implemented: opt-in selected-domain browser snapshots over a separate Native Messaging host; DPAPI CurrentUser store; exclusive None/Manual/Synced sources; attempt-owned Netscape jars through the canonical yt-dlp runner; consent/revoke ordering, bounded protocol, redaction and metadata-only status. Cookie events do not start the main UI, download queue or LIVE scheduler. Registration detects physically redirected AppData instead of claiming Chrome can see a tool-private manifest. See [COOKIE_SYNC.md](COOKIE_SYNC.md) and the [candidate.5 report](CANDIDATE5_COOKIE_SYNC_YTDLP_AUDIT_REPORT.md).

Tested: final Debug and Release **491/491 each**, extension **49/49**, Web Remote **31/31**, zero final compiler warnings/errors/test skips. Focused CookieSecurity **49/49** and NativeHost **3/3** are subsets, not additional full-suite tests. Real Chrome with owner consent synchronized only synthetic example.com data; the owner confirmed automatic change sync, all three deletions and fail-closed cleanup of the temporary registration/store. No account cookies were read. Native cookie Settings PL/Dark and EN/Light, 20-toggle Settings scroll, ordinary EN/Dark range/Details/Settings and published normal startup/ready/clean close pass. Native range/startup use custom QA tools, not real provisioning/download evidence.

Package: self-contained win-x64, untrimmed multi-file GUI plus standalone NativeHost; **611 entries**, **106,529,646 bytes**, SHA-256 `0c52878fdf8390a6c87749bea926773778c9e8149215aea8907a2837cf3df3ae`. No cookie store/jars/user manifest/runtime data or test tooling is included. Exact path/evidence is in the report.

Known open work: request §36 desktop-initiated browser refresh after auth failure and one bounded retry are **not implemented**; extension-before-handoff JIT and using the latest already received snapshot are not substitutes. Remaining expert-argument credential bypass edges need hardening. Full PSL, optional API regrant/reload behavior, real authenticated sessions and non-Chrome deployment are not comprehensively qualified. An earlier full Debug run failed one existing thumbnail-fixture cleanup with an in-use file; focused and final full reruns pass, but its root cause remains unestablished. No assertion/skip/retry weakening or unrelated thumbnail fix was made. Prior experimental SponsorBlock and native activation limits remain.

Planned, not implemented: [yt-dlp feature-gap audit](YTDLP_FEATURE_GAP_AUDIT.md) covers **250/250 distinct help long options** in 17 categories / 100 grouped rows. Its top-ten features are proposals, not a bulk implementation or universal extractor-support claim.

## Historical candidate.4 validation — 2026-10-10

Clean detached MFN master `69c5ea2b20b07d9df801340c8ae9e3e1ca5dfbc3` is the central development/CI/release pin. Candidate.4 fixes cached WebP notification compatibility via bounded persistent PNG normalization and adds source-neutral QuickQueue for extension /live/. Existing desktop metadata classification remains unchanged; active/upcoming cannot enter VOD or start unattended recording.

Owner confirmed native completed/progress/LIVE thumbnail visibility and no image under privacy/OFF; progress/LIVE screenshots retained only in ignored QA artifacts. Real Chrome replay `MBxOLJe-ZqE` reached actual desktop analysis, was classified was_live/not active and created one paused VOD item (360p, full range), no LIVE session. Native event presentation uses real metadata/cache with synthetic status transitions, not a full replay download. Full validation and remaining qualification limits are recorded in [candidate.4 report](CANDIDATE4_THUMBNAIL_LIVE_REPLAY_REPORT.md). No commit/push/tag/publication; v1.0.0 records below remain historical.

Final candidate.4 pipeline: Debug and Release **442/442** tests each, extension **30/30**, Web Remote **31/31**, zero compiler warnings/errors/skips; PL/EN parity included. Native range/Details/Settings (EN/Dark), LIVE-to-replay navigation (PL/Light) and active-LIVE fixture (EN/Dark) pass. Self-contained win-x64 publish, 410-entry ZIP hygiene and published normal startup/ready/clean close pass; published startup uses custom QA tools, not a real provisioning/download claim. ZIP is **69,378,287 bytes**, SHA-256 `114eb259ca65dd1d034bccf189629e64816671855c46685ba2338186365ca8b7`. Native cold/action/clean-PC qualification or explicit acceptance remains before stable promotion.

## Historical candidate.3 validation — 2026-10-09

Clean detached MFN master `16af62c050b346107ee37ca38dc95976ab2de94c` is the central development/CI/release pin. Optional official Windows system notification integration, extension invalidation lifecycle and pipeline hardening are implemented. See [candidate.3 report](CANDIDATE3_NOTIFICATIONS_PIPELINE_REPORT.md) for current final validation and remaining native-platform limits, and [System notifications](SYSTEM_NOTIFICATIONS.md) for defaults/deployment. No commit/push/tag/publication is authorized; v1.0.0 is unchanged.

Real Chrome reproduced the prior `/live/` modal reload failure at `src/i18n.js:12`. Post-fix stale UI disappears; the owner confirms no new extension error, and a normal page reload restores fresh injection. Windows App SDK / Ready / Accepted was not initially visible when launched directly by the tool host. The identical isolated QA harness launched through Explorer produced a visible notification, confirmed by the owner; clicking opened Downloads in the same instance. This qualifies the test notification and warm Open app route, not cold activation or file/folder/LIVE actions.

Follow-up Chrome check: an ordinary click on an already-open watch/Mix tab reproduced a new invalidation error from its older pre-guard script. One same-tab reload restored the working modal; three further open/cancel cycles produced no new invalidation log. Older open tabs need this one-time reload after the extension update; SPA navigation alone does not replace their scripts. Added first-stale-click regression, standalone extension tests **30/30**; no additional production code change. Details: `CANDIDATE3_NOTIFICATIONS_PIPELINE_REPORT.md`.

Current validation: Debug/Release 427/427 each, JavaScript 29 extension + 31 Web Remote, zero compiler warnings/errors/skips; normal native EN/Dark Details/range smoke, PL/Light 20-toggle Settings scroll, synthetic active-LIVE/Details and 12-entry playlist preview pass. Published self-contained startup/clean close and the same check in an isolated copy missing the optional AppSdk provider pass; tools are custom QA fixtures, not real provisioning/download evidence. Three final audit regressions also protect capability/throttling on language changes and removed LIVE progress identities. Focused native action/deployment limits remain explicitly listed in the report before stable promotion.

## Historical candidate.2 validation — 2026-10-08

MFN master `9214c8f5cec66e3731e5bf118cc8bfd5a9fba623` is pinned in development, CI and release. This round refines pairing state/automatic generation, Follow app remote language, idle polling rendering and shared extension language/Options. Exact current validation and limits: [BROWSER_REMOTE_1_1_IMPLEMENTATION.md](BROWSER_REMOTE_1_1_IMPLEMENTATION.md). No commit, push, tag or publication is authorized.

The original reported browser cookie state cannot be established retrospectively (owner does not recall prior pairing). Clean-profile native enable/save and anonymous HTTP checks prove auth is on; a paired cookie + consumed code reproduces dashboard-without-code legitimately. Do not describe this as a proven authentication bypass or claim certainty about the original browser.

Chrome remote login/code/logout/new-code/runtime-language smoke passed; user confirmed Vimeo popup immediate PL/EN, Options link and reset. The supplied 9.7 s remote recording exposed redundant polling DOM writes, now covered by a stable-node regression. Phone trust and exhaustive OS menu/closed-app handoff matrices remain unqualified. SponsorBlock Remove remains Experimental/default OFF.

Final candidate.2: restore, Debug/Release builds and 402/402 tests in each configuration, 57/57 JavaScript tests and self-contained win-x64 publish PASS; zero compiler warnings/errors/skips. Native EN/Dark ordinary smoke and PL/Light 20-toggle pairing/scroll regression PASS. Post-fix Chrome polling retains the unchanged dashboard and form edits/focus. Local ZIP is 60,644,845 bytes; exact hash and limits are in the candidate report. READY FOR 1.1.0 FINAL ACCEPTANCE within this scoped evidence, not public-release approval.

## Release closure — 2026-10-06

ModernTubeDownloader **1.0.0 is release-ready within the scoped evidence**. The owner selected **MIT**, Copyright © 2026 ProGraMajster, and explicitly approved committing, pushing master, making the repository public, tagging and publishing GitHub Release. External publication is gated on the final commit's CI; GitHub Actions and the `v1.0.0` release are the authoritative publication records.

- Development, CI and Release use clean ModernFormsNext `f521f9dfcfe601bf9b6199b88132cccb2380d1bf`, fetched from `origin/master` on 2026-10-06: three commits after `bcf2bcb1726cbb697d2e56600188c4ef2c91ebe1`. Shared application/window lifecycle changes were inspected; no framework or application compatibility patch was needed.
- Fresh Debug and Release: **375/375 tests each**, zero skipped tests, zero build warnings/errors. JavaScript: **16 extension + 25 Web Remote** tests, all passing. PL/EN resource parity is included in the .NET suite.
- The first master CI exposed a test-only worker-retirement race. The test now waits for final-save retirement, keeps its original deadline and strengthens final-state/start-count assertions; 25 consecutive Release executions pass. No production queue behavior changed. The final commit requires a new green CI before publication; the failed run remains documented in the stabilization report.
- A subsequent CI hang was reproduced locally on two logical cores: theme/style tests applied the process-wide theme before creating their owned UI host. All test appearance initializations now run inside the serialized headless host scope, with the host created after awaited setup and before theme/control creation. Production code and MFN source are unchanged. CI/package validation preserves TRX and bounded hang diagnostics (no memory dumps); interrupted/failed runs remain recorded below.
- The first corrected master CI passed and PUBLIC visibility was verified. Additional tag CI exposed a natural-end fixture reading persistence before final-save retirement; it now awaits existing graceful shutdown and verifies durable identity/state/parts. The initial unpublished tag/release run was withdrawn before binary publication, pending the corrected final commit's CI; exact runs and tag provenance remain in the stabilization report.
- Fresh native ordinary EN/Dark including Settings/Details and one strict quality-popup cycle, active LIVE PL/Light with own persistence/Details, and 12-entry playlist EN/Dark (10 available entries queued) pass. A LIVE harness invocation initially supplied its ordinary VOD default URL and correctly failed the LIVE-start precondition; the explicit active-LIVE fixture passes. This invocation error is not an application/framework regression.
- Packaging now requires the exact clean framework pin and application LICENSE, includes dependency redistribution notices, runs both configurations and checks zero skipped tests. Windows x64 remains self-contained, multi-file, untrimmed; symbols are separate. Tools, test/automation hosts, user data, logs and media are excluded.
- Final local Build-Release and ZIP inspection pass (374 entries, no duplicates/forbidden files). Native UIA against the extracted self-contained EXE analyzed public Sintel without enqueue/download, opened Downloads/Settings/LIVE and closed normally; owned-HWND captures were inspected. Public GitHub artifact checksum will be independently checked after the tag workflow builds it (not assumed equal to the local ZIP).
- Stable core readiness does not erase previous raw failures or broaden source qualification. SponsorBlock Remove remains **Experimental / default OFF / known playback defect**; FromStart is Experimental. Mark with explicit MKV is the supported SponsorBlock path.
- Post-1.0: deferred soak, exhaustive DPI/device/browser/source matrices, Unknown child automatic-retry hardening and optional user-supplied authenticated cookies qualification. No universal extractor support or exhaustive manual sign-off is claimed.

The 2026-10-05 snapshot below is historical, including its then-missing license and old dependency pin. See the final closure section in `STABILIZATION_REPORT.md` and the release checklist for the remaining focused native/package results.

## Historical snapshot — 2026-10-05

Status date: 2026-10-05. **NOT READY for public release because application LICENSE is missing.** Stable core is technically ready within scoped P0 evidence, not universally qualified. The user's revised policy defers broad device/source/soak matrices; missing coverage alone is not a blocker. Current evidence and triage are in `STABILIZATION_REPORT.md` / `RELEASE_CHECKLIST.md`; older snapshots below are historical.

## Historical stabilization — local, not released

- Development/CI/release retain the user-requested clean verified MFN master pin `bcf2bcb1726cbb697d2e56600188c4ef2c91ebe1`. Later fetch now shows `origin/master=f521f9df...` (+3 Android commits); bcf is not called latest fetched HEAD. No unrequested dependency migration/framework source change or dirty-main-checkout mutation.
- Debug and Release builds have zero warnings/errors; all 375 .NET tests pass in each configuration with zero skips. Extension tests pass 16/16. Web Remote time-editor JavaScript tests pass 25/25. Restore, final Build-Release.ps1 and self-contained publish pass.
- Conservative opaque direct-media selection is enabled only for non-LIVE Auto/MKV, with actual local A/V/duration verification before exposing a final output and again after remux. Missing codecs are not invented. Explicit MP4/WebM reject contradictory or unknown codec facts. The real Archive OGV range produced invalid remux timestamps; this is not a successful download qualification.
- P0 dropdown: minimal native Light/Dark and actual MTD PL/Light + EN/Dark each pass 500/500 (five variants × 100), native popup and canonical peer agree. PL/Light exact CLI passes 200/200; EN/Dark exact CLI raw 197/200 has three preserved FAILs traced to real native deactivation/outside click after Expand. Controlled owned-window activation reproduces legitimate close/state agreement. This is environmental harness interference, not stale semantic state or confirmed app/MFN bug. Old untraced PL/Light pass 15 remains uncertain, post-1.0 follow-up. Strict assertions unchanged; no sleep/retry/focus/layout hack. Shared smoke records native focus/foreground/popup around actions.
- Final ordinary EN/Dark native automation PASS. Separate final two-cycle PL/Light popup diagnostic FAIL at cycle 2: foreground changed to a foreign HWND, native active/focus became 0 and the actual popup/fresh peer both became hidden/Collapsed. The raw failure is retained, not rerun or counted as PASS; it is distinct from the successful ordinary smoke.
- Video-card actions reuse the existing shared styles and compact PL/EN More options label. Six new headless PL/EN × Light/Dark/System cases verify bindings and interactive text contrast; the Dark on-accent and Light secondary-pressed semantic text tokens were corrected without changing backgrounds/layout.
- Native P0 ZIP VOD close/reopen PASS: real .part 2,151,096 → 2,223,664 bytes, minimum unchanged, stable session/workspace, retained prefix hash, valid 34,076,243-byte / 888.093605 s MP4, ffprobe/10 s decode, exactly-one correct History. Both processes close normally. Child-kill safely becomes Failed/Unknown (no auto-retry); existing explicit Retry refreshes metadata/reuses 2,372,184 bytes/completes. Automatic unknown-child recovery is post-1.0 hardening, not PASS. Prior Archive, provisioning and Release LIVE FromNow/Stop-save/reconnect/parallel proof retained without repetition.
- Real phone LAN adding worked per user feedback. The numeric-keyboard/colon mismatch was reproduced in the old form and replaced with separate Hours/Minutes/Seconds fields, responsive layout, preview and validation. The user confirmed the new phone UX and playback of a real 2–10 s Sintel clip. Token was disabled only in the explicitly approved temporary QA profile; product default/security policy is unchanged. The temporary listener was closed after the test, with no firewall rule added.
- Soak: **PARTIAL / DEFERRED POST-1.0**, 91 samples/~83 continuous minutes before sleep; no sampled monotonic growth/crash, not no-leak proof. Wall-clock marker invalidated; no rerun or active soak blocker.
- Scoped native layout inspection: four main screens at actual 100% PL/Light, 225% EN/Dark and 100% EN/System (resolved Dark), no unusable clipping/overlap. 125%/150% unavailable and NOT EXECUTED; full DPI/browser/phone/extractor/filesystem/network matrices post-1.0, no display/firewall changes.
- User-confirmed SponsorBlock Remove playback FAIL with screen recording: frozen/apparently out-of-sequence picture at the cut. Existing file has backward PTS and 104 frames squeezed into ~1 ms of timeline at the join. Retain **Experimental, default OFF + existing warning**, not stable-qualified; Mark / explicit MKV recommended. No transcoding workaround. Subtitle and normal/recovered LIVE short human playback remain unanswered, not PASS. Cookies NOT EXECUTED without credentials; default OFF + safe synthetic failure/redaction checks, optional not basic-release blocker.
- Detailed pre-pause recording frames confirm the visible freeze. 146 exact decoded-frame matches to the existing source around 09:38–09:46 explain the later scene after the outro removal; arbitrary foreign-frame insertion is not proven. Audio discontinuity/sustained A/V sync remain unqualified.
- Local candidate ZIP: artifacts/release/ModernTubeDownloader-1.0.0-win-x64.zip, self-contained, multi-file, untrimmed; current checksum/size are in STABILIZATION_REPORT.md. ZIP hygiene passes, not full runtime sign-off. No staging, commit, push, tag or publication was performed. License remains USER DECISION REQUIRED.

## Release blocker triage

| Issue | Severity | User impact | Reproducible | Release blocker? | Reason |
| --- | --- | --- | --- | --- | --- |
| Missing application license | P0 / legal | No approved distribution terms | Yes | YES — USER DECISION REQUIRED | Concrete release prerequisite. |
| Quality dropdown closes during automation | P2 / diagnostic | No confirmed incorrect native/peer behavior | Real external activation/click traced | NO — POST-1.0 QA | 100-cycle native variants pass; raw interrupted failures/old uncertainty retained. |
| Native normal-close VOD resume | P0 gate | Advertised partial continuation | PASS | NO — CLOSED | Actual appended bytes, final media and exactly-one History. |
| Killed-child Unknown exit / no auto-retry | P2 / hardening | Explicit Retry needed | Yes | NO — POST-1.0 QA | Preserved partial, fresh metadata and valid manual recovery; no universal auto-recovery promise. |
| SponsorBlock Remove bad playback | P1 / Experimental | Frozen / out-of-sequence frames | Human FAIL + recording | NO for basic stable 1.0 — OPTIONAL FEATURE QUALIFICATION | User permits Experimental/default OFF/warning; not stable-qualified. |
| Soak/full device/source matrices | P2 / qualification | Remaining combinations unqualified | Partial | NO — POST-1.0 QA | Explicitly deferred; do not claim full PASS/leak safety. |
| Cookies/FromStart/upcoming/natural-end/human sync | P2 / optional | Credential/timing/experimental qualification | Synthetic PASS; real incomplete | NO — OPTIONAL FEATURE QUALIFICATION | Standard VOD/FromNow verified; safeguards retained. |
| Publication approval | Approval gate | User-controlled distribution | Not authorized | USER DECISION REQUIRED | No automatic publication. |

## Historical qualification snapshots — through 2026-10-03

## Historical: runtime supported sources — local, not released

- Downloads and Settings / Download engine expose Supported services: active executable version/count, search, grouped families, optional technical mode and 12-row pagination. ToolManager updates invalidate the version/path/hash cache and refresh an open dialog. No static full extractor list is committed.
- Metadata-only Check URL uses the existing yt-dlp pipeline, bounded flat playlist preview and explicit navigation. It never queues or records. Source/type and actual subtitle/chapter/format/live facts appear in the compact card, LIVE analysis and Details / Source. Desktop is source-neutral; extension and Web Remote remain YouTube-scoped.
- The active managed yt-dlp is `2026.08.19`: 1752 CLI rows, **1751 unique names** after duplicate `generic` removal, 136 explicit CURRENTLY BROKEN markers. This is extractor availability, not 1751 MTD-verified services.
- The small QA registry only claims dated per-feature YouTube (`youtube`) and Twitch (`twitch:stream`) evidence from prior real qualification. YouTube playlist, Twitch VOD, Vimeo and Archive.org are not promoted to Verified by one successful metadata check.
- Current real metadata checks pass for YouTube VOD/playlist/active LIVE/replay, Twitch LIVE/completed VOD and Internet Archive. The Vimeo sample requires login; localized cookies-file guidance is confirmed without credentials. Archive.org's tested formats lack codec IDs: recognition passes, but the current quality selector cannot queue that sample; the new source facts explain this rather than guessing codecs.
- Debug/Release each pass 349/349 .NET tests with no skips; extension 16/16, Web Remote 7/7 and localization parity pass. Native source dialog PL/Light and EN/Dark, search/paging/wheel, Settings entry, 760×720 resize, cache across reopen and Open without enqueue/recording pass. Details / Source, LIVE, playlist/wheel and 20 Settings checkbox/scroll toggles pass. Desktop LIVE → replay routing no longer incorrectly applies the YouTube-only external-protocol validator; external/remote security boundaries are unchanged. Detailed evidence and remaining gates: `SUPPORTED_SOURCES.md`.
- ModernFormsNext remains the unchanged clean source checkout at `6d341963dfa0468c3636ea251e107ee34d0d1272`. No commit, push, tag or public publication is authorized or performed in this round.
- Final `Build-Release.ps1` PASS: self-contained, multi-file, untrimmed win-x64 local candidate `artifacts/release/ModernTubeDownloader-1.0.0-win-x64.zip`, **60,460,341 bytes**, **353 ZIP entries**; SHA-256 `e831c46b49a3a29cfa463c297ad9c51c0e5911ae0b30ef448b93a16ba2dc1381`. Source documentation is included; PDBs are in a separate symbols ZIP. No media tools, test harness, runtime data, logs or cache are bundled.
- Final ordinary native PL/Light smoke PASS. One additional optional quality-dropdown capture repeat failed at Collapse with `ActionUnsupported` after Expand/capture; the dynamic popup state was no longer actionable for Collapse. That attempt is not counted as PASS or silently caught. Repeatable dropdown visual/focus QA remains open; successful source-dialog and Details runs are separate evidence.

## Historical: independent LIVE subsystem refactor — local, not released

- The final fetched `origin/master` is `6d341963dfa0468c3636ea251e107ee34d0d1272`, four commits after the initial `4b0191d1...` baseline below (DPI events and Form.Activate). The intermediate `1de8a6b...` qualification is retained in the validation report. All active development/CI/release pins match; the detached dependency checkout is clean, and the unrelated dirty main MFN checkout was not touched. No framework compatibility patch was necessary.
- LIVE is actually owned by `LiveSessionService → LiveRecordingScheduler → existing LiveRecordingExecutor`. VOD stays `DownloadQueueService → QueueProcessor → DownloadJobExecutor`. Separate stores (`live-sessions.json` / `queue.json`) and limits allow 3 VOD + 2 LIVE; VOD pause and either scheduler shutdown do not stop the other subsystem. Legacy migration is destination-first/idempotent and retains recovery workspaces and ordinary queue data.
- The LIVE page presents active/scheduled/recoverable sessions, explicit confirmation, own counters, duration/bytes/speed/start times, Edit, Stop and save, Cancel, Resume and Details. Queue cards/footer are VOD only. Web Remote has a separate safe `/api/live` projection/actions; starting LIVE still requires desktop confirmation. Existing extension transport remains unchanged.
- Current validation, real-media profiles and remaining gates are recorded in `VALIDATION_REPORT.md`. Required independent-limit/pause/shutdown tests and a five-distinct-child-PID test pass. Real Release parallel 3 VOD + 1 LIVE, Stop and save, controlled yt-dlp interruption into Part 2, exhausted-retry Partial, and manual/automatic restart recovery pass with actual MKV A/V, ffprobe and short decode. Debug semantic UI also analyzed/recorded/stopped a real LIVE, opened Details and closed cleanly. These are not human A/V playback or real upcoming auto-start evidence.
- Visual inspection used only captured app HWNDs: PL/Light at 1280×850 and EN/Dark at 1000×720 with scheduled-card ScrollIntoView. Settings dynamic scrolling, playlist paging/scroll, normal Details and external-instance forwarding were exercised through native semantic Automation. Dedicated DPI, human playback, real FromStart, real scheduled transition and a fresh complete Chrome/phone-LAN matrix remain open.
- Final-pin qualification: Debug/Release each 313/313 tests, zero build warnings/errors, 16 extension tests, self-contained publish and 352-entry ZIP hygiene passed. Native LIVE/Settings/playlist/Details/protocol tests and real Release parallel capture/manual restart were repeated on exact `6d341963...`; the detailed interruption/Partial/automatic-resume runs used the intermediate pin, as explicitly recorded in the report.

## Historical baseline before runtime ownership separation

## 2026-10-03 ModernFormsNext and LIVE working-tree update — not released

- ModernFormsNext development, CI and release pins now target fetched `origin/master` `4b0191d1c0de33e31e46c9f7a377dab833ae3d20` (29 commits after the previous `dc55839b...` pin). The separate framework checkout is clean; the unrelated dirty main framework checkout was not modified. Restore and Debug/Release builds on this pin passed with zero warnings/errors.
- Active LIVE now has explicit FromNow and experimental eligible-extractor FromStart recording intents, a durable `LiveSession`, Stop and save, MKV stream-copy verification/recovery, separate parts after reconnect, `Partial`/`Interrupted` states, and History entries for verified parts. Upcoming LIVE can persist as WaitingForLive with app-level randomized polling and an immediate recheck after restart. The browser extension opens `/live/` in the app instead of unattended quick queueing. Web Remote presents the states and can stop/save/cancel/retry an existing job; starting LIVE remotely is intentionally rejected with an open-in-app message.
- VOD jobs now use stable format-keyed workspaces to retain compatible yt-dlp `.part` data across retries, while a changed format is isolated. The default restart policy is manual resume, and exact byte/fragment continuation is source/protocol-dependent. No automatic transcode or unverified concat is performed.
- Process-level fake-tool tests cover FromNow/FromStart flags, natural end, Stop and save, reconnection into separate parts, exhausted-retry Partial, stable-workspace VOD `.part` continuation, waiting transition and restart persistence. Native semantic Automation passed for active LIVE (PL/Dark) and upcoming LIVE (EN/Light) with isolated fake tools. Bounded real Twitch LIVE FromNow recording and Stop and save passed in Debug and self-contained Release. Published Release runs also recovered old Part 1 and recorded new Part 2 after both manual and automatic restart resume. A controlled termination of only the yt-dlp child recovered Part 1 and automatically retried into Part 2 after fresh metadata; a Windows Job Object closed its FFmpeg descendant. With retry disabled, the same controlled failure ended as Partial with one verified playable part. All reported parts had audio/video and passed short FFmpeg decode. Persisted LIVE queue data contained no raw signed metadata. Human playback, real FromStart, real upcoming auto-start, real VOD restart, DPI and visual gates remain open.

## 2026-09-27 local enhancement update — not released

- Added subtitle track projection from existing yt-dlp metadata, manual/automatic language selection, persisted per-job options, subtitle-only download/conversion, sidecar output, and optional stream-copy media embedding for MP4/MKV/WebM. Playlist workers apply a common policy after per-entry analysis; unavailable requested languages warn without failing the video.
- Replaced browser-profile extraction with an opt-in, user-selected Netscape `cookies.txt` path. Lightweight validation runs at Settings save and operation time; contents are neither stored nor copied, and process/crash logs redact the path. A user-authorized authenticated-media test remains open.
- Real media confirmed SponsorBlock Mark/MKV, but direct yt-dlp also reproduced malformed Mark/MP4 chapter metadata and an approximately 83 ms backward video PTS step at a Remove/MP4 edit. Mark is now allowed only for explicit MKV across desktop, queue, worker, and Web Remote; Remove remains available with an experimental warning and is not signed off for audiovisual quality. Custom ranges remain incompatible with subtitles/SponsorBlock, and Remove with subtitles.
- Added a localized custom-range keyframe-alignment hint. Native semantic Automation exposed and verified it after switching to Custom in PL/Light and EN/Dark; this is not a complete visual/DPI matrix. Real concurrency-2/3 pacing, countdown, pause/resume, and graceful countdown shutdown passed on short public Blender ranges. Real subtitle/Mark/Remove/range and completed-live evidence is in the ignored manual validation report; active/upcoming live, real Chrome, phone/LAN, and DPI checks remain open where unverified.

## 2026-09-26 local LAN remote and media-range update — not released

- Implemented an opt-in, default-off Kestrel web remote over the existing application services, with explicit private IPv4/loopback binding, random in-memory bearer token, address-only QR, strict web input/Origin/Host checks, and bounded API. Only YouTube-family HTTPS URLs are accepted by the remote add form in this MVP; the desktop remains source-neutral. HTTP is unencrypted and must remain on a trusted LAN.
- Added custom start/end range controls for analyzed videos and a common playlist selection, `MediaTimeRange` validation/persistence/retry/history, yt-dlp section requests for the selected video/audio formats, and best-effort final-duration logging via ffprobe. No full-download fallback or automatic re-encode is implemented; stream-copy cuts may be keyframe-aligned rather than exact.
- Deterministic fake-tool tests and real loopback HTTP tests cover core behavior. These are not evidence of a real-site segment, phone/second-device LAN, firewall, or all DPI/theme combinations. Complete validation for this round is recorded in the final task report and release checklist; the older 206-test snapshot below predates these changes.

## 2026-09-26 local update — not yet released

- YouTube `/live/<id>` is accepted end-to-end by the extension and desktop protocol. The actual `live_status`/availability from yt-dlp controls downloadability: completed replay/VOD with ordinary formats proceeds, while active, upcoming, processing, private, and unavailable items get distinct feedback.
- Automatic retry is bounded and category-driven: default one initial attempt plus two additional attempts. HTTP 403/429/5xx, timeout, and network failures retry with fresh metadata/format selection and isolated attempt directories; permanent failures do not.
- Optional inclusive-range admission pacing is off by default. When enabled it spaces starts of separate queue items, including under concurrency 2–3. Retry backoff is separate; cancellation and shutdown interrupt waiting.
- Localized queue failure details and structured, URL/credential-redacted logs were added. Automated fake-tool and extension tests validate the new flows; no real YouTube completed-live URL or private playlist was supplied for live verification. Browser-side playlist privacy is intentionally not guessed from unstable DOM text; yt-dlp remains authoritative.
- Validation of this local update: Debug and Release build with zero warnings/errors, 206/206 .NET tests in each configuration, 13/13 extension tests, and MFN Automation smoke of a synthetic `/live/replay` page flow. Real Chrome and real completed-live/private-playlist smoke remain NOT EXECUTED.

## Historical implemented scope — before current stabilization

- Native ModernFormsNext Windows desktop UI on .NET 10.
- Official multi-resolution Windows application icon shared by the executable and application windows.
- URL analysis, metadata normalization, “up to” quality selection, per-job `Auto`/MP4/MKV/WebM output-container selection, single-item queueing, stream downloads, FFmpeg stream-copy merge/remux, finalization, and persistent history.
- Playlist URL analysis and paged entry selection, bulk addition to the ordinary queue, per-entry format selection at download time, and playlist context in queue/history (local working tree; not part of a published 1.0.0 artifact).
- Automatic yt-dlp, GPL-configured FFmpeg/ffprobe, and Deno provisioning with validation, atomic activation, rollback preservation, and update checks.
- Persistent queue with optional, index-derived position badges (on by default), a live 1–3 concurrent-job limit, pause, per-item cancellation, retry, ordering, removal, automatic vertical scrolling, and safe restart recovery.
- Compact analyzed-video presentation and Details sections for overview, metadata, formats, subtitles, and chapters.
- Polish/English localization and System/Light/Dark themes.
- Full raw metadata sidecars and publication-date file timestamps, independently configurable and enabled by default.
- Local AppData logging and a fatal-error boundary that records full exceptions and displays a readable fallback dialog.
- Self-contained, multi-file, untrimmed Windows x64 publishing and deterministic ZIP packaging.

## Historical tested scope — before current stabilization

- Debug and Release solution builds with serialized MSBuild and shared compilation disabled.
- Against ModernFormsNext `dc55839b061485121a8e5dd8092ea2a79e4d7572`: restore, Debug/Release builds, 166 application tests, 32 upstream ScrollableControl regressions, one native Windows scroll-layout regression, semantic automation (EN/Dark and PL/Light), Queue ↔ History scroll reproduction, playlist pagination/scroll, external URL forwarding, and 13 extension tests passed on 2026-09-22. The last upstream commit changed only `CHANGELOG.md`; final restore, builds, app tests, and automation smoke were repeated on the exact pinned SHA. Visual captures were inspected at 1440×900 (EN/Dark) and 1280×720 (PL/Light); this is not a full manual UI or DPI matrix.
- Complete automated unit/integration suite, including tool installation/update failure paths, queue persistence, details null safety, localization parity, timestamp/sidecar post-processing, and scroll planning.
- Debug-only semantic automation smoke (normal startup without a bridge, explicit CLI startup, analysis, queue, Settings, Details tabs, and clean shutdown) with isolated synthetic tools; headless TestHost coverage for Details and a queue card.
- Local playlist validation: deterministic parser/selection/queue/concurrency and end-to-end fake-tool tests, PL/EN TestHost preview, native CLI smoke for 12 and 220 entries (including 218 queued jobs), and unchanged single-video smoke. Real-site playlist extraction is not yet a verified claim.
- Deterministic process-level workflow: analysis → separate streams → FFmpeg service merge → final file → metadata sidecar → timestamp → history.
- Native-window visual checks at 1440×900 and 1280×720 in Polish/English and Light/Dark, including a long Unicode title.
- Repeated Details opening with full and incomplete synthetic metadata, plus a live analyzed item; every tab, vertical scrolling, resize, close, and reopen were observed.
- Release output launch against isolated application-data roots, including successful clean-profile provisioning, no redundant second-start download, installed-tools offline startup, and readable first-start offline recovery UI.
- Live Big Buck Bunny download at 360p: separate video/audio acquisition, FFmpeg merge/remux, valid full JSON sidecar, publication-date CreationTime, history entry, and restart persistence.

See [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md) for the exact current validation state. Automated and observed manual evidence are intentionally reported separately.

## Historical limitations — before current stabilization

- The queue/history hide/show scroll anomaly was reproduced on the earlier ModernFormsNext `edb9bed983d334234f6b244c5a64f316d680d5ee`. The fix for [ModernFormsNext #130](https://github.com/ProGraMajster/ModernFormsNext/issues/130) is included in the current source pin; upstream unit/native regressions and the app's three-pass Queue ↔ History repro pass. No app-side workaround was added. A full manual DPI and interaction matrix remains pending.
- The browser extension and desktop protocol/single-instance path are local MVP work. The page modal and popup now distinguish quick queueing from opening the analyzed options UI in the app, and the validated request carries quality, container, and action intent. The extension uses accessible native selects with explicit dark/light styling. Automated parser/registry/IPC/UI/extension tests pass. Real Chrome click-through, opened-dropdown appearance, and OS association smoke remain unverified before release.
- The custom URI is one-way. It cannot return yt-dlp capabilities to the extension, so browser quality choices are documented “up to” presets rather than claimed real-time availability. Exact available presets are derived from yt-dlp metadata after opening the app. Dynamic browser-side capabilities would require a separately installed and authenticated Native Messaging host; DOM/player-response scraping is intentionally not used.

- The public project license is not selected; the generated package is a local release candidate, not an approved public release.
- The source repository is private at `ProGraMajster/ModernTubeDownloader`; no tag or GitHub Release has been published.
- The application intentionally uses a pinned ModernFormsNext source checkout at `6d341963dfa0468c3636ea251e107ee34d0d1272` (`origin/master` last fetched 2026-10-03, four commits after the 4b0191d1 baseline above). Development, CI and release use the same source revision; a NuGet migration has not been validated in this update.
- The official `yt-dlp.exe` carries `yt-dlp-ejs`; ModernTubeDownloader manages Deno 2.3.0+ and passes its verified path explicitly. Extractor behavior can still change with target sites and yt-dlp releases.
- Interrupted jobs preserve stable session data and default to manual resume. VOD continuation reuses matching `.part` data when yt-dlp and the source allow it; LIVE reconnect starts a separate part after fresh metadata, not byte-perfect append. Bounded real LIVE graceful restart and child-process interruption pass; abrupt whole-app termination, arbitrary fragmented sources and real VOD restart still need separate qualification.
- Explicit playlist URLs use a flat metadata request; individual-video analysis and downloads still use `--no-playlist`. Watch URLs with `&list=...` remain single-video jobs. Other source-specific playlist URL shapes and live/private extractor behavior have not been verified against real network sources.
- Automatic channel expansion, manual codec selection, media transcoding, cloud sync, and a library view remain outside the current scope. Subtitle embedding is limited to compatible MP4/MKV/WebM outputs; no media transcoding is implicit. Explicit MP4/WebM requests fail when no stream-copy-compatible formats exist.
- Dedicated 125% and 150% DPI observation remains pending.

## Historical plans after 1.0.0 approval

- Select and commit the application license.
- Move to a compatible ModernFormsNext NuGet release when the required master changes are published.
- Complete the remaining premiere/live-date, DPI, 20+ item queue, and history shell-action manual matrix before tagging.
- Only after approval: create tag `v1.0.0` and allow the guarded release workflow to publish the GitHub Release.
