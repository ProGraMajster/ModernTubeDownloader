# ModernTubeDownloader 1.2.0-candidate.1 — implementation and qualification

**Historical candidate snapshot.** The final-release request on 2026-10-11 supersedes
the acceptance decision below: MFN #204/#205 and listed wider manual gaps are accepted
KNOWN LIMITATION / POST-1.2 QA / NOT RELEASE BLOCKER. Original evidence/counts and the
earlier recommendation are retained, not promoted to PASS. See CURRENT_STATUS.md,
RELEASE_CHECKLIST.md and the final local FINAL_RELEASE_1_2_0.md.

2026-10-11. Stages A–F are implemented; **NOT READY for complete 1.2.0 acceptance**
because required composite-control typography and manual/visual qualification remain partial.
This is local QA output, not a public release. No commit/push/tag/publication was performed.

## MODERNFORMSNEXT

- Previous application pin: `69c5ea2b20b07d9df801340c8ae9e3e1ca5dfbc3`.
- Intermediate fetch: `8be1346627405f06284ad8676086182bf83ba74c`.
- Final fetched `origin/master`: **`d36015711f63945a49025675acb9aca3df60101d`**.
- Clean detached `.mfn-master-worktree`; no framework source modifications.
- Central `Directory.Build.props` is the development/CI/release pin. Debug uses the
  existing MFN Automation bridge; Release has no Automation project references/endpoint.
- Final builds/native CLI use the final SHA. Its new virtualization foundations do
  not require another application scrolling implementation.

App branch/HEAD remains `master` / `3454e2c71c39994a3e5711bf521edd876b2e5320`.
The user's deleted `ModernTubeDownloader.slnx` and untracked `plika.slnx` are preserved;
the release script uses the test-project graph when the conventional solution is absent.
Index empty. No user queue/cookies/profile/settings were used as QA data or overwritten.

## TRAY — A

One `TrayApplicationController` owns MFN NotifyIcon/context menu, cached branding and
bounded UI-thread state refresh. Open/Downloads/LIVE/pause-resume/update-ready/Exit use
canonical handlers. Close/minimize support Exit/Hide/Ask with first-hide information
and fallback when the tray is unavailable. Notifications remain independent of tray.
`ExternalLinkInstance` restores the same instance; hidden autostart does not steal focus.
App shutdown drains preparation/owned services; there is no second queue/LIVE scheduler.

Stage files (relative to `ModernTubeDownloader/`): `Services/TrayApplicationController.cs`,
`Services/TrayPolicy.cs`, `Views/ShutdownChoiceForm.cs`, `Views/StartupSettingsPanel.cs`,
`Settings/StartupSettings.cs`, `Program.cs`, `MainForm.cs`,
`Infrastructure/ExternalLinkInstance.cs`, `Services/AppServices.cs` and language resources.

PASS: stage full tests 631/631; native PL/Dark and EN/Light NotifyIcon registration,
Ask/Hide, same-instance restore and Exit. Final-master PL/Dark hidden-autostart recheck
showed no visible main HWND; a manual second launch restored it and closed cleanly.
NOT EXECUTED: human shell-icon double-click/menu pointer gestures or hours-long hidden work.

## AUTOSTART

`Services/WindowsStartupService.cs` changes only the owned HKCU Run value
`ProGraMajster.ModernTubeDownloader`, after explicit Settings Apply. No admin/task/service.
Exact executable quoting plus `--autostart [--start-hidden]`; unsafe/relative/overlong
paths rejected. Foreign values are not overwritten. Missing/moved/stale registration
has an explicit repair action, not an unsolicited startup write.

PASS: 14 `StartupAndTrayTests` for ownership/path/repair policies and native hidden lifecycle.
NOT EXECUTED: changing the user's Run entry or actual Windows logout/login.

## UPDATER — B

`ApplicationUpdateService` uses public GitHub Stable release metadata, exact expected
ZIP/SHA assets, manual/daily/weekly checks and NotifyOnly/AutoDownload/InstallOnExit.
It is independent of ToolManager. UI shows version/release notes/check/download/later/
skip/install. Failed 30-second checks remain retryable; monitoring continues. A changed
release invalidates stale staged archives and installation intent.

SHA-256 verification is mandatory before staging. `UpdatePackageSecurity` bounds input,
expanded ZIP size/entry count/depth and rejects traversal, ADS/device/duplicate paths,
links, unexpected directories and user-data payloads. GitHub's checksum protects against
corruption, not independently signed publisher authenticity/code signing.

The independent Updater runs from a private staging copy, waits for exact parent PID/
start identity, journals backup before replacing installation files and rolls back on
failure/recovery. Newly introduced files are removed on rollback. AppData and unlisted
installation data are preserved. Optional restart is bounded. Install-on-exit means
real exit, not hide-to-tray; active-work warnings remain.

Files: `Services/ApplicationUpdateService.cs`, `Settings/UpdateSettings.cs`,
`Views/UpdateSettingsPanel.cs`, notification coordinator/settings; separate
`ModernTubeDownloader.Updater/{ModernTubeDownloader.Updater.csproj,Program.cs,
UpdatePackageSecurity.cs,UpdateTransaction.cs}`; `Tests/ApplicationUpdateTests.cs`
(test paths here and below mean `ModernTubeDownloader.Tests/`).

PASS: stage 655/655; final 26 updater tests cover detection/deadline/retry/checksum/ZIP
security/stale staging, real external process parent wait, synthetic install and injected
mid-copy rollback preserving userdata. Native update panel/check PL/Dark and EN/Light;
latest-master EN/Light recheck. No user's actual installation was replaced.
NOT EXECUTED: public update installation/restart on a clean PC.
Follow-up: bound/expire obsolete staged archives/runner copies.

## OTHER PAGE — C

`Views/OtherView.cs` and MainForm add one tools hub with a working Batch Import launcher
and up to five recent preparation sessions. No fake future tools. Responsive card width,
framework scrolling and shared theme/fonts; no second state store.
PASS: stage 655/655 and native PL/Dark hub/navigation; final large-font capture is readable.

## BATCH IMPORT — D

`BatchImportService` owns preparation only; source checking/metadata/authentication and
eventual downloads reuse canonical services. Window close leaves service-owned analysis
running; application shutdown cancels/drains/persists. Explicit ready selections enter
the existing queue. Active/upcoming LIVE/playlists requiring choice do not auto-record.

Streaming TXT, quoted/multiline CSV and JSON arrays use validated URL/options schemas.
Bounds: 250k rows, 256 MiB input, 16 KiB line/string, 64 KiB CSV record/JSON object,
depth 8. Default workers 3, range 1–8, per-operation deadline. No one task/control per URL.
Statuses distinguish preparation/ready/duplicate/unsupported/auth-required/added/
downloading/completed/failed/skipped/cancelled, rather than fake download progress.

GUID schema-1 sessions: immutable entries.jsonl + manifest commit marker, compact
revisioned updates.jsonl, separate ready-row metadata. Latest revision wins; only a
torn final journal record is tolerated. In-flight recovery is paused/pending. Original
input file deletion does not destroy the imported session. Store is outside the ZIP.

UI renders **20 rows/page**, service cap 100. Search/filter/select/edit/remove/export,
bounded optional thumbnails and recent-session resume. At 200% the toolbar wraps and
body scrolls. Handoff uses canonical duplicate-aware AddRange in chunks of 100,
journaled queue IDs and cancellation restoration. JSON/CSV reports strip query/fragment,
neutralize CSV formulas and omit raw signed metadata/cookie/auth data.

Files: `Batch/{BatchModels.cs,BatchImportParser.cs,BatchSessionStore.cs,BatchImportService.cs}`,
`Views/BatchImportForm.cs`, OtherView, AppServices, SourcePresentation, MainForm,
`Tests/BatchImportTests.cs` (20 final cases).

PASS: stage 673/673; 10/10k/50k streaming/persistence, worker limits, pause/cancel,
dedupe, selected options/queue handoff, auth/LIVE classification and safe export tests.
Native fixture import/select/analyze/paging/handoff/close; final PL/Dark at 200% passes.
PARTIAL real smoke: 20 public rows represented four identities plus fragment aliases,
not 20 distinct films. Selected YouTube `eRsGyueVLvQ` analyzed to 47 formats and handed
exactly one job to the paused VOD queue. Vimeo `1084537` failed `Error.Download.Unknown`
with a redacted upstream cookie diagnostic. Archive addresses were imported only.
No account cookies were requested/read; no actual media download in this smoke.

## TEXT SCALE — E

`AppTypography` centralizes eight logical tokens, weakly tracked bindings and absolute
base-font × AppTextScale (80–200%, reset 100%). Fonts/interaction styles update live,
never repeatedly multiplying current fonts. Row text space reflows; image/icon geometry
is independent. Owned layout subscriptions are disposed. Initialize/profile/font events
publish on the UI thread before async persistence. No backend DPI/RenderScaling/window
transform/bitmap magnification.

Files: `Theming/AppTypography.cs`, typography/service in AppearanceProfile,
`Views/TextScaleSettingsPanel.cs`, AppUi/AppTheme/AppVectorIcon/MainForm; bindings/layout
in DownloadsView, QueueItemCard, PlaylistPreviewControl, MediaRangeEditor, HistoryView,
HistoryEntryCard, LiveView, OtherView, BatchImportForm, SettingsView, SupportedSourcesForm
and VideoDetailsForm. `Tests/TypographyTests.cs` (13 final tests).

PASS: stage 686/686; native 125/150/175/200/100% persistence/navigation in PL/Dark and
EN/Light. Details all five tabs/reopen-close at 150/200%; owned PNGs inspected. Main
branding/title/actions remain visible; large content uses framework scrolling.
PARTIAL: MFN implicit TabStrip uses Theme.FontSize, not TabControl Font; NumericUpDown's
implicit TextBox retains its own font. Tabs/digits remain default-sized in native PNGs.
Minimal public-API repro and framework sites: PERSONALIZATION.md. No reflection/copied
framework/DPI hack. All long labels/all Settings cards at 200% are not qualified.
NOT EXECUTED: physical Windows 100/125/150% DPI (distinct from AppTextScale).

## PERSONALIZATION — F

Strict schema-1 `AppearanceProfile` in separate appearance.json, with Global/Typography/
Navigation/Downloads/LIVE/History/Other/Batch/Settings/Details. Safe missing-section
defaults/migration, including typography-only shape. One active profile now; no named
profile library. Default/Compact/Large/Minimal presets and per-section/global resets.

System/Light/Dark/Custom, seven HEX semantic colors/picker/reset/contrast warning,
corners, density/spacing, independent sidebar icon sizes and animations/intensity feed
app-owned consumers. Per-page thumbnail/facts/progress/actions/density/tab/technical
options preserve critical LIVE state and Settings. Global tokens do not comprehensively
change all framework composite/popup metrics.

Bounded JSON import/export (64 KiB/depth 10): invalid ranges/enums/colors/unknown or
duplicate fields/types/scripts/paths rejected. Corrupt file preserved, defaults applied.
Reset never changes auth/download settings. 80 ms coalescing and serialized async changes
avoid rebuild loops. UI publication stays synchronous on the owner thread.

Files: `Theming/AppearanceProfile.cs`, `Views/PersonalizationSettingsPanel.cs`, AppServices,
shared helpers/page consumers, language resources; `Tests/AppearanceProfileTests.cs`
(19 cases), AutomationTestHostTests, ReleaseReadinessTests and notification tests.
PASS: final model/persistence/import/export/reset/schema/preset tests; native presets,
custom accent and five text scales PL/Dark and EN/Light. Aggregate batch notification is
opt-in/default OFF; update-ready notification uses the existing privacy/master pipeline.
NOT EXECUTED: human ColorDialog/Open/Save picker/native Yes-No reset gestures.
Model tests are not manual dialog evidence. Complete global font coverage stays PARTIAL.

## SETTINGS

Eleven localized categories: General, Downloads, LIVE, Notifications, Authentication,
Browser, Web Remote, Updates, Startup, Personalization, Advanced. Canonical values retained
across categories. Critical controls are never hidden by profile settings.
PASS: category navigation, auth PL/Dark, updates EN/Light, startup PL/Dark; original
20-toggle require-access-token scroll repro EN/Light plus wheel/track/thumb/fast wheel.
The latter uses DEBUG-only all-cards mode checking exactly 18 cards, not production
category visibility. No fake scrollbars or app wheel forwarding.
Optional settings search/sticky header/automatic sidebar collapse are deferred.

## PERFORMANCE

Indicative single-run 50k TXT: import **637.1 ms**, reload **545.3 ms**, filter/page
**2.7 ms**. Final Release whole 50k JSON/TXT durability test cases: 1.22/1.50 s.
Not a network-throughput benchmark or guaranteed threshold; no 50k-panel tree/process burst.

Final native PL/Dark startup to HWND/input-idle: **1492.7 ms**; published Release **1402.1 ms**.
Not tools Ready/cold-PC qualification. Logged UI apply before persistence: text-scale
**170.3–535.3 ms**, profile **45.4–527.0 ms** across final PL/Dark and EN/Light passes.
These are measured costs, not a claim of
sub-frame updates; large changes can briefly block the UI. Optimize/coalesce further
if manual acceptance finds noticeable latency. Memory/CPU/long-running soak not executed.

## VALIDATION

Final `Build-Release.ps1`, final MFN pin:

- Restore/Debug/Release build: PASS; **0 warnings, 0 errors**.
- Debug **711/711**, Release **711/711**; **0 skipped/failed/not-executed**.
- Extension + Web Remote JS **92/92** (61 + 31), 0 skips.
- NativeHost/auth/updater/batch/appearance/localization are passing subsets of 711,
  not extra tests. Updater 26, Batch 20, Startup/tray 14, Typography 13, Appearance 19,
  LocalizationAndAppearance 7, ReleaseReadiness 4.
- Self-contained GUI + NativeHost + Updater publish; ZIP hygiene **810 entries**.
- Published Release normal start, four custom QA tools validated, no Automation endpoint,
  clean close: PASS. QA tools are not real first-run provisioning/download evidence.
- Separate symbols; no PDB/yt-dlp/FFmpeg/ffprobe/Deno/user settings/profile/Auth/Batch/log/test
  or Automation tooling in ordinary ZIP.

TRX: `artifacts/validation/20261011-153054-4c896070/{Debug,Release}/release-validation.trx`.
Logs: `artifacts/validation/candidate120/final-pipeline.log`, `final-appearance-pl-dark.log`,
`final-appearance-en-light-native-bounds.log`, `final-publish-startup.log`,
`final-range-details-confirmation.log`; earlier stage failed
TRXs and the first final-range failure remain, not erased.
The first final-range smoke failed because its queued fixture correctly triggered the
new active-work shutdown confirmation and the old harness never answered it. The harness
now requires that dialog and explicitly invokes ShutdownExit; unchanged app code then
passes range persistence, all Details sections and clean shutdown. This is not a timeout
increase or bypass of production safety. Several initial appearance PNGs had tiny semantic
peer rectangles and were unusable; bitmap allocation now measures the same owned HWND
as PrintWindow. Those cropped images are not visual-acceptance evidence.
The subsequent EN/Light pass produced 25 main captures at native dimensions; inspected
200% Downloads/Settings/Details visible content confirms the layout and unchanged numeric/
tab-label limitation. Portions outside the desktop-visible rendered area are not inferred
from blank capture regions; exhaustive resize/clipping acceptance remains open.
During stage F, off-thread construction-time font mutation, TestHost disposal across an
await and double-disposed Details cancellation were fixed at lifecycle/ownership boundaries.
No assertion/skip/threshold/retry weakening to manufacture PASS.

### Native profiles on final master

All directories below are isolated `mtd-automation-smoke-<suffix>` under local Temp,
not user AppData; fixture media tools are QA.

| Scenario | Result / suffix |
| --- | --- |
| Tray/hidden autostart PL/Dark | PASS `a6d0dce0c1d644c3b7d459f114866b59` |
| Update manual check EN/Light | PASS `658e5384329d48edb7875de02519c7b8` |
| Cookie Settings PL/Dark | PASS `57e8da03ba504fd78d81da545d8e98e5` |
| Appearance EN/Light | PASS functional / PARTIAL fonts `db3e246ad544453e9e87c4e160041a0c` |
| Appearance PL/Dark, scales/pages/Details/presets/accent | PASS functional / PARTIAL fonts `b1b7dae338ed40e3af7262bfd9c2250e` |
| Appearance EN/Light, corrected owned-HWND capture | PASS functional / PARTIAL fonts/visual matrix `fa71c3a406264bb49f7195cdba1b515e` |
| Real Batch EN/Dark | PARTIAL sources `11c5b083161a4b068ce0b198e4f77e44` |
| Batch PL/Dark 200% | PASS `6d465383a2f34c2db41791af6078aac3` |
| Settings 20-toggle EN/Light | PASS, recorded final scroll smoke |
| Range/queue persistence/Details/active-work close EN/Dark | PASS `55d6b6102ffa495890ff819509d1c238` |
| Published Release EN/Dark | PASS `4f74c0ac5e2a463f83693a36651df478` |

### Local artifact

`artifacts/release/ModernTubeDownloader-1.2.0-candidate.1-win-x64.zip`

Self-contained win-x64, multi-file, untrimmed, no AOT/ReadyToRun.
**143,722,822 bytes**, SHA-256
`972764af11b9be88d225bb93630f63b7db8c33ca48155904b980d1437f0b6a89`.
Includes native dependencies, native-host/, updater/, MIT LICENSE/exact notices and
README/UPDATES/TRAY_AND_STARTUP/BATCH_IMPORT/PERSONALIZATION docs. Symbols ZIP/checksum separate.
Required docs: README, ARCHITECTURE, CURRENT_STATUS, CHANGELOG, RELEASE_CHECKLIST,
AUTOMATION_TESTING and the four operational guides updated in the source repository.

## KNOWN LIMITATIONS / FOLLOW-UP

- MFN implicit tabs/numeric typography propagation: focused framework repro/follow-up,
  no framework hack mixed into this app candidate.
- Exhaustive large-font/long-label/resize/DPI matrix and human picker/reset/tray/login.
- Clean-PC update/restart/moved-path repair and bounded staging retention.
- Twenty distinct successful real Batch sources; investigate upstream Vimeo failure
  using public inputs/non-secret diagnostics.
- Optimize largest measured live appearance updates if manual latency is objectionable.
- Memory/background/LIVE soak and physical device/browser/media checks remain open.
- Existing SponsorBlock Remove and LIVE FromStart remain Experimental/source-dependent;
  no new media qualification inferred. Prior human cookie/notification/LAN evidence is
  historical v1.1.0, not a fresh acceptance pass in this round.
- Optional Settings search/sticky headers, auto-collapse and richer Batch counters deferred.

## Final decision

PASS: ordered A–F implementation, canonical ownership, bounded streaming/paging,
synthetic updater rollback/data preservation, final builds/tests/JS/publish/package,
recorded native semantic flows and isolated profile safety.

PARTIAL: complete global text scaling/composite controls, exhaustive appearance visuals,
real multi-source Batch acceptance and live-change latency qualification.

NOT EXECUTED: physical DPI/login/shell tray gestures, public update install/restart,
clean-PC/long-running soak, human file/color/reset dialogs, new real-media A/V/LAN/browser
cookie-sync/notification acceptance. No credentials/firewall/security posture changed.

REMAINING 1.2.0 BLOCKERS: complete required text-scale coverage and explicit native/manual
acceptance gaps above. Partial evidence is not promoted by successful builds.

RECOMMENDATION: **NOT READY** for complete 1.2.0 acceptance. Local candidate is usable
for targeted testing; no stable/public publication authority is inferred.
